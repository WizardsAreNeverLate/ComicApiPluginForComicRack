using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Model;
using ComicApiPlugin.Service;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// VerseDB for one cover search, asked through the local API service. This class sends each call with the API token and
/// the run id, and turns the answer back into what VerseDbHttp returns or throws. A service that cannot be started or
/// reached is a VerseDbException without a status, whose inner exception is the ServiceUnavailableException. A service
/// that fails on an error it did not expect is a ServiceErrorException.
/// </summary>
internal sealed class ServiceVerseDb(ServiceConnection service, string token, string runId) : IVerseDb
{
	public Task<CoverLookup> LookUpCoverAsync(byte[] image, CancellationToken ct) =>
		CallAsync(ServiceProtocol.VerseDbLookupCoverPath, () => ServiceConnection.Multipart((ServiceProtocol.ImagePart, image)),
			async response => VerseDbWire.ToDomain(await ServiceConnection.ReadJsonAsync<CoverLookupDto>(response).ConfigureAwait(false)), ct);

	public Task<IssueMetadata> GetIssueAsync(VerseDbIssueId issue, CancellationToken ct) =>
		CallAsync(ServiceProtocol.VerseDbIssuePath, () => ServiceConnection.Json(new VerseDbIssueRequestDto { Issue = issue.Value }),
			async response => VerseDbWire.ToDomain(await ServiceConnection.ReadJsonAsync<IssueMetadataDto>(response).ConfigureAwait(false)), ct);

	public Task<byte[]> GetImageAsync(Uri url, CancellationToken ct) =>
		CallAsync(ServiceProtocol.VerseDbImagePath, () => ServiceConnection.Json(new VerseDbImageRequestDto { Url = url.OriginalString }),
			response => response.Content.ReadAsByteArrayAsync(), ct);

	private async Task<T> CallAsync<T>(string path, Func<HttpContent> body, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
	{
		try
		{
			return await service.CallAsync(() =>
			{
				var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body() };
				request.Headers.TryAddWithoutValidation(ServiceProtocol.VerseDbTokenHeader, token);
				request.Headers.TryAddWithoutValidation(ServiceProtocol.RunIdHeader, runId);
				return request;
			}, read, error => error.VerseDb is { } verseDb ? VerseDbWire.ToDomain(verseDb) : null, ct).ConfigureAwait(false);
		}
		catch (ServiceUnavailableException error)
		{
			throw new VerseDbException(null, error.Message, inner: error);
		}
	}
}
