using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.VerseDb;

/// <summary>
/// The VerseDB transport, one per token. The bearer token goes on API requests only, never on a picture download, and
/// never into an exception message.
/// </summary>
public sealed class VerseDbHttp : IVerseDb, IDisposable
{
	public static readonly Uri BaseUri = new("https://versedb.com");

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);
	private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);

	private readonly HttpClient client;
	private readonly string token;

	/// <param name="handler">HttpClientHandler in production; a scripted handler in tests. Disposed with this object.</param>
	/// <param name="baseUri">Where the VerseDB API answers; only a test names one.</param>
	public VerseDbHttp(HttpMessageHandler handler, string token, string userAgent, Uri? baseUri = null)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new ArgumentException("A VerseDB API token is required.", nameof(token));
		}
		this.token = token.Trim();
		client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout, BaseAddress = baseUri ?? BaseUri };
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
		client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
	}

	public async Task<CoverLookup> LookUpCoverAsync(byte[] image, CancellationToken ct)
	{
		using var file = new ByteArrayContent(image);
		file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
		// VerseDB answers 404 unless name and filename are quoted and filename* is absent. Add(content, name, fileName)
		// writes them unquoted and adds filename*, so the header is set by hand, as curl writes it.
		file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"image\"", FileName = "\"cover.jpg\"" };
		using var form = new MultipartFormDataContent { file };
		using var request = ApiRequest(HttpMethod.Post, "/api/v1/lookup/cover");
		request.Content = form;
		return VerseDbJson.ParseCoverLookup(await SendAsync(request, ct).ConfigureAwait(false));
	}

	public async Task<IssueMetadata> GetIssueAsync(VerseDbIssueId issue, CancellationToken ct)
	{
		using var request = ApiRequest(HttpMethod.Get, "/api/v1/issues/" + issue);
		return VerseDbJson.ParseIssue(await SendAsync(request, ct).ConfigureAwait(false));
	}

	public async Task<byte[]> GetImageAsync(Uri url, CancellationToken ct)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				throw new VerseDbException(response.StatusCode, "Could not download the cover picture (HTTP " + (int)response.StatusCode + ").");
			}
			return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
		}
		catch (Exception e) when (IsTransportFailure(e, ct))
		{
			throw Unreachable(e);
		}
	}

	public void Dispose() => client.Dispose();

	private HttpRequestMessage ApiRequest(HttpMethod method, string path)
	{
		var request = new HttpRequestMessage(method, path);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return request;
	}

	private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		try
		{
			using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
			string body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
			if (response.IsSuccessStatusCode)
			{
				return body;
			}
			throw new VerseDbException(response.StatusCode, Describe(response.StatusCode, body), RetryAfter(response));
		}
		catch (Exception e) when (IsTransportFailure(e, ct))
		{
			throw Unreachable(e);
		}
	}

	private string Describe(HttpStatusCode status, string body)
	{
		string message = VerseDbJson.ParseErrorMessage(body) ?? "HTTP " + (int)status;
		message = message.Replace(token, "...");
		return "VerseDB refused the request (" + (int)status + "): " + (message.Length > 300 ? message.Substring(0, 300) : message);
	}

	private static TimeSpan? RetryAfter(HttpResponseMessage response)
	{
		RetryConditionHeaderValue? header = response.Headers.RetryAfter;
		TimeSpan? wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
		return wait is { } w && w >= TimeSpan.Zero ? (w > MaxRetryAfter ? MaxRetryAfter : w) : null;
	}

	private VerseDbException Unreachable(Exception e) =>
		new(null, "Could not reach VerseDB: " + e.Message.Replace(token, "..."));

	private static bool IsTransportFailure(Exception e, CancellationToken ct) =>
		e is HttpRequestException or IOException || (e is OperationCanceledException && !ct.IsCancellationRequested);
}
