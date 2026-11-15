using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ComicApiPlugin.Fixtures;

/// <summary>
/// A Comic Vine that listens on a loopback port, so the service process and an in-process ComicVineHttp can both be
/// pointed at it.
/// </summary>
public sealed class FakeComicVineServer(Func<FakeRequest, Task<FakeAnswer>> answer) : IDisposable
{
	private readonly FakeHttpServer server = new(answer);

	/// <summary>Answers the API from recorded responses, and finds nothing for a request no fixture names.</summary>
	/// <param name="first">Answers a request before the recorded responses do, or returns null to leave it to them.</param>
	public static FakeComicVineServer FromFixtures(IEnumerable<RecordedResponse> responses, Func<FakeRequest, Task<FakeAnswer?>>? first = null)
	{
		var fixtures = new FixtureHandler(responses);
		return new FakeComicVineServer(async request =>
		{
			if (first is not null && await first(request).ConfigureAwait(false) is { } custom)
			{
				return custom;
			}
			string url = "https://comicvine.gamespot.com" + request.Target;
			RecordedResponse recorded = fixtures.Find(url) ?? FixtureHandler.NothingFound(FixtureFiles.Key(url));
			return FakeAnswer.Xml(recorded.Body, recorded.Status);
		});
	}

	public int Port => server.Port;

	/// <summary>The value for ComicVineHttp's base URL and for the service's COMICAPI_COMICVINE_BASE_URL.</summary>
	public string BaseUrl => $"http://127.0.0.1:{Port}/api/";

	public IReadOnlyList<FakeRequest> Requests => server.Requests;

	/// <summary>The fixture key of every request, in order: the URL Comic Vine itself would have seen, with the key redacted.</summary>
	public IReadOnlyList<string> RequestKeys => [.. Requests.Select(request => FixtureFiles.Key("https://comicvine.gamespot.com" + request.Target))];

	public void Dispose() => server.Dispose();
}
