using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// Comic Vine for one scrape run, asked through the local API service. The service owns the caches, the paging and the
/// throttle. This class sends each call with the API key, the run id and the search settings, and turns the answer back
/// into what ComicVineCatalog returns or throws. A service that cannot be started or reached is a ComicVineException
/// of kind ServiceUnavailable, whose inner exception says why.
/// </summary>
internal sealed class ServiceComicVine(ServiceConnection service, string apiKey, string runId, SearchSettings settings, DebugLog log) : IComicVine
{
	private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

	private readonly SearchSettingsDto settings = ComicVineWire.ToDto(settings);

	public Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct) =>
		CallAsync<SearchRequestDto, IReadOnlyList<SeriesRef>>(ServiceProtocol.ComicVineSearchPath, new SearchRequestDto { Settings = settings, Terms = terms },
			async response => [.. ComicVineWire.Need((await LastLineAsync(response, progress).ConfigureAwait(false)).Series, "series").Select(ComicVineWire.ToDomain)], ct);

	public Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct) =>
		CallAsync<IssuesRequestDto, IReadOnlyList<IssueRef>>(ServiceProtocol.ComicVineIssuesPath, new IssuesRequestDto { Settings = settings, Series = series.Value },
			async response => [.. ComicVineWire.Need((await LastLineAsync(response, progress).ConfigureAwait(false)).Issues, "issues").Select(ComicVineWire.ToDomain)], ct);

	public Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct) =>
		CallAsync(ServiceProtocol.ComicVineFindIssuePath, new FindIssueRequestDto { Settings = settings, Series = series.Value, IssueNumber = issueNumber },
			async response => (await JsonAsync<FindIssueResponseDto>(response).ConfigureAwait(false)).Issue is { } found ? ComicVineWire.ToDomain(found) : null, ct);

	public Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct) =>
		CallAsync(ServiceProtocol.ComicVineIssuePath, new IssueRequestDto { Settings = settings, Issue = issue.Value, IncludeRating = includeRating },
			async response => ComicVineWire.ToDomain(await JsonAsync<IssueDto>(response).ConfigureAwait(false)), ct);

	public Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct)
	{
		var request = new ResolveSeriesRequestDto
		{
			Settings = settings,
			Series = (target as ComicVineTarget.OfSeries)?.Id.Value,
			Issue = (target as ComicVineTarget.OfIssue)?.Id.Value,
		};
		return CallAsync(ServiceProtocol.ComicVineResolveSeriesPath, request,
			async response => (await JsonAsync<ResolveSeriesResponseDto>(response).ConfigureAwait(false)).Series is { } found ? ComicVineWire.ToDomain(found) : null, ct);
	}

	/// <summary>Null also when the service cannot be reached: an image never fails a scrape, and the next call for data reports the service.</summary>
	public async Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct)
	{
		try
		{
			return await CallAsync(ServiceProtocol.ComicVineImagePath, new ImageRequestDto { Settings = settings, Url = url.OriginalString }, async response =>
			{
				if (response.StatusCode == HttpStatusCode.NoContent)
				{
					return null;
				}
				// Bitmap reads its stream lazily, so the stream stays with the bitmap; a MemoryStream needs no disposal.
				return new Bitmap(new MemoryStream(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)));
			}, ct).ConfigureAwait(false);
		}
		catch (ComicVineException error) when (error.Error == ComicVineError.ServiceUnavailable)
		{
			log.Debug("could not load an image: " + error.InnerException?.Message);
			return null;
		}
	}

	private async Task<T> CallAsync<TRequest, T>(string path, TRequest body, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
		where TRequest : ComicVineRequestDto
	{
		ct.ThrowIfCancellationRequested();
		try
		{
			HttpClient client = await service.ConnectAsync(ct).ConfigureAwait(false);
			using var request = new HttpRequestMessage(HttpMethod.Post, path)
			{
				Content = new StringContent(ComicVineWire.ToJson(body), Utf8, ComicVineWire.JsonContentType),
			};
			request.Headers.TryAddWithoutValidation(ServiceProtocol.ComicVineKeyHeader, apiKey);
			request.Headers.TryAddWithoutValidation(ServiceProtocol.RunIdHeader, runId);
			// Without this, .NET Framework first waits for a "100 Continue" before it sends the body.
			request.Headers.ExpectContinue = false;
			using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			// .NET Framework reads a response body without looking at the token, so a cancel closes the response under the read.
			using (ct.Register(response.Dispose))
			{
				if (!response.IsSuccessStatusCode)
				{
					throw await FailureAsync(path, response).ConfigureAwait(false);
				}
				return await read(response).ConfigureAwait(false);
			}
		}
		catch (Exception error) when (ct.IsCancellationRequested)
		{
			throw new OperationCanceledException("The call to the local API service was cancelled.", error, ct);
		}
		catch (ServiceUnavailableException error)
		{
			throw Unavailable(path, error);
		}
		catch (Exception error) when (error is HttpRequestException or IOException or ObjectDisposedException or OperationCanceledException)
		{
			throw Unavailable(path, new ServiceUnavailableException("The local API service did not answer: " + error.Message, error));
		}
		catch (WireFormatException error)
		{
			throw Unavailable(path, new ServiceUnavailableException("The local API service gave an answer this plugin cannot read: " + error.Message, error));
		}
	}

	/// <summary>The exception for a response that is not a success: Comic Vine's failure as the service reported it, or the service's own.</summary>
	private static async Task<Exception> FailureAsync(string path, HttpResponseMessage response)
	{
		string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		ErrorResponseDto? error = body.Length > 0 ? ComicVineWire.FromJson<ErrorResponseDto>(body) : null;
		return error?.ComicVine is { } comicVine
			? ComicVineWire.ToDomain(comicVine)
			: new ServiceUnavailableException($"The local API service answered {path} with HTTP {(int)response.StatusCode}" + (error?.Message is { } message ? ": " + message : "."));
	}

	/// <summary>Reports each progress line as it arrives and returns the last line, which holds the result. An error line is thrown.</summary>
	private static async Task<StreamLineDto> LastLineAsync(HttpResponseMessage response, IProgress<QueryProgress>? progress)
	{
		using var reader = new StreamReader(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), Utf8);
		while (await reader.ReadLineAsync().ConfigureAwait(false) is { } text)
		{
			StreamLineDto line = ComicVineWire.FromJson<StreamLineDto>(text);
			if (line.Progress is { } report)
			{
				progress?.Report(ComicVineWire.ToDomain(report));
			}
			else if (line.Error is { } error)
			{
				throw ComicVineWire.ToDomain(error);
			}
			else
			{
				return line;
			}
		}
		throw new WireFormatException("The answer ended before its result.");
	}

	private static async Task<T> JsonAsync<T>(HttpResponseMessage response) where T : class =>
		ComicVineWire.FromJson<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

	private static ComicVineException Unavailable(string path, ServiceUnavailableException cause) =>
		new(ComicVineError.ServiceUnavailable, "local API service " + path, null, cause);
}
