using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ComicVineScraper.Engine;

namespace ComicVineScraper.ComicVine;

/// <summary>
/// The transport, one per plugin lifetime: the HttpClient, the request throttle, retries, and error classification
/// (cvconnection.py and utils.get_html_string). Tests drive it through a recorded-response handler.
/// </summary>
public sealed class ComicVineHttp : IDisposable
{
	internal static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(1100);
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(2500);
	internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

	private readonly HttpClient client;
	private readonly string apiKey;
	private readonly IClock clock;
	private readonly Throttle throttle;

	/// <param name="handler">HttpClientHandler in the plugin; a recorded-response handler in tests. Disposed with this object.</param>
	/// <param name="apiKey">Never logged: every URL in an exception passes through the redaction.</param>
	public ComicVineHttp(HttpMessageHandler handler, string apiKey, string userAgent, IClock clock)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new ArgumentException("A Comic Vine API key is required.", nameof(apiKey));
		}
		this.apiKey = apiKey;
		this.clock = clock;
		throttle = new Throttle(clock, RequestGap);
		client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
	}

	/// <summary>
	/// The parsed response to an API request. Every request waits its turn at the throttle. A network error, an HTTP 5xx,
	/// an empty or unparsable body, or a status_code other than 1 is retried once after 2.5 s. An invalid key (100) or a
	/// rate limit (107) is not retried (deviations #5), and a 4xx body is read, so HTTP 401 reports the invalid key (deviations #4).
	/// </summary>
	internal async Task<XDocument> GetAsync(ApiRequest request, CancellationToken ct)
	{
		string url = request.ToUrl(apiKey);
		Outcome first = await TryGetAsync(url, ct).ConfigureAwait(false);
		if (first.Document is { } document)
		{
			return document;
		}
		if (!first.Error!.IsRecoverable)
		{
			throw first.Error;
		}
		await clock.Delay(RetryDelay, ct).ConfigureAwait(false);
		Outcome second = await TryGetAsync(url, ct).ConfigureAwait(false);
		return second.Document ?? throw second.Error!;
	}

	/// <summary>An image, after at most one immediate retry; null when both attempts fail. Images share the throttle.</summary>
	internal async Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct)
	{
		for (int attempt = 0; attempt < 2; attempt++)
		{
			await throttle.WaitTurnAsync(ct).ConfigureAwait(false);
			try
			{
				using HttpResponseMessage response = await client.GetAsync(url, ct).ConfigureAwait(false);
				if (response.StatusCode == HttpStatusCode.OK)
				{
					byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
					// Bitmap reads its stream lazily, so the stream stays with the bitmap; a MemoryStream needs no disposal.
					return new Bitmap(new MemoryStream(bytes));
				}
			}
			catch (Exception e) when (IsTransportFailure(e, ct) || e is ArgumentException)
			{
			}
		}
		return null;
	}

	internal string Redact(string text) => ApiKeyRedaction.Redact(text, apiKey);

	public void Dispose() => client.Dispose();

	private async Task<Outcome> TryGetAsync(string url, CancellationToken ct)
	{
		await throttle.WaitTurnAsync(ct).ConfigureAwait(false);
		HttpStatusCode status;
		string body;
		try
		{
			using HttpResponseMessage response = await client.GetAsync(url, ct).ConfigureAwait(false);
			status = response.StatusCode;
			byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
			using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
			body = reader.ReadToEnd();
		}
		catch (Exception e) when (IsTransportFailure(e, ct))
		{
			return Fail(ComicVineError.Network, url, null, e);
		}

		if ((int)status >= 500)
		{
			return Fail(ComicVineError.Network, url, null, null);
		}
		XElement? root = Parse(body);
		if (root?.Element("status_code") is not { } statusElement
			|| !int.TryParse(statusElement.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int code))
		{
			return Fail(ComicVineError.Network, url, null, null);
		}
		return code switch
		{
			1 when status == HttpStatusCode.OK => new Outcome(root.Document, null),
			1 => Fail(ComicVineError.Network, url, null, null),
			100 => Fail(ComicVineError.InvalidApiKey, url, code, null),
			107 => Fail(ComicVineError.RateLimited, url, code, null),
			_ => Fail(ComicVineError.Api, url, code, null),
		};
	}

	private static XElement? Parse(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}
		try
		{
			var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
			using var reader = XmlReader.Create(new StringReader(ResponseText.Clean(body)), settings);
			return XDocument.Load(reader).Root;
		}
		catch (XmlException)
		{
			return null;
		}
	}

	private static bool IsTransportFailure(Exception e, CancellationToken ct) =>
		e is HttpRequestException or IOException || (e is OperationCanceledException && !ct.IsCancellationRequested);

	private Outcome Fail(ComicVineError error, string url, int? apiStatus, Exception? inner)
	{
		Exception? safeInner = inner is not null && inner.ToString().Contains(apiKey) ? new HttpRequestException(Redact(inner.Message)) : inner;
		return new(null, new ComicVineException(error, Redact(url), apiStatus, safeInner));
	}

	private sealed record Outcome(XDocument? Document, ComicVineException? Error);
}

/// <summary>Keeps at least the gap between the starts of any two requests, across threads (deviations #6).</summary>
internal sealed class Throttle(IClock clock, TimeSpan gap)
{
	private readonly SemaphoreSlim turn = new(1, 1);
	private DateTime next = DateTime.MinValue;

	public async Task WaitTurnAsync(CancellationToken ct)
	{
		await turn.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			TimeSpan wait = next - clock.Now;
			if (wait > TimeSpan.Zero)
			{
				await clock.Delay(wait, ct).ConfigureAwait(false);
			}
			next = clock.Now + gap;
		}
		finally
		{
			turn.Release();
		}
	}
}
