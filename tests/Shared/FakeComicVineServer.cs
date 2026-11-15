using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ComicApiPlugin.Fixtures;

/// <summary>
/// A Comic Vine that listens on a loopback port, so the service process and an in-process ComicVineHttp can both be
/// pointed at it. It answers the API from recorded responses, and finds nothing for a request no fixture names.
/// </summary>
public sealed class FakeComicVineServer : IDisposable
{
	private const string Site = "https://comicvine.gamespot.com";

	private readonly FakeHttpServer server;
	private readonly ConcurrentQueue<string> unrecorded = new();
	private readonly FixtureHandler fixtures;
	private readonly Func<FakeRequest, Task<FakeAnswer?>>? first;
	private readonly bool ownImages;
	private readonly Action<string>? missed;

	private FakeComicVineServer(FixtureHandler fixtures, Func<FakeRequest, Task<FakeAnswer?>>? first, bool ownImages, Action<string>? missed)
	{
		this.fixtures = fixtures;
		this.first = first;
		this.ownImages = ownImages;
		this.missed = missed;
		server = new FakeHttpServer(AnswerAsync);
	}

	/// <param name="first">Answers a request before the recorded responses do, or returns null to leave it to them.</param>
	/// <param name="ownImages">
	/// Makes every image URL in an answer name this server. A whole scrape also asks for covers, and without this the
	/// service would ask the real Comic Vine for them. No fixture records an image, so each one is not found.
	/// </param>
	/// <param name="missed">Told the fixture key of each request no fixture names, as it arrives.</param>
	public static FakeComicVineServer FromFixtures(
		IEnumerable<RecordedResponse> responses, Func<FakeRequest, Task<FakeAnswer?>>? first = null, bool ownImages = false, Action<string>? missed = null) =>
		new(new FixtureHandler(responses), first, ownImages, missed);

	public int Port => server.Port;

	/// <summary>The value for ComicVineHttp's base URL and for the service's COMICAPI_COMICVINE_BASE_URL.</summary>
	public string BaseUrl => $"http://127.0.0.1:{Port}/api/";

	public IReadOnlyList<FakeRequest> Requests => server.Requests;

	/// <summary>The fixture key of every request, in order: the URL Comic Vine itself would have seen, with the key redacted.</summary>
	public IReadOnlyList<string> RequestKeys => [.. Requests.Select(request => FixtureFiles.Key(Site + request.Target))];

	/// <summary>The fixture keys of the requests that no fixture names, in order.</summary>
	public IReadOnlyList<string> Unrecorded => unrecorded.ToArray();

	public void Dispose() => server.Dispose();

	private async Task<FakeAnswer> AnswerAsync(FakeRequest request)
	{
		if (first is not null && await first(request).ConfigureAwait(false) is { } custom)
		{
			return custom;
		}
		string url = Site + request.Target;
		RecordedResponse? recorded = fixtures.Find(url);
		if (recorded is null)
		{
			string key = FixtureFiles.Key(url);
			unrecorded.Enqueue(key);
			missed?.Invoke(key);
			recorded = FixtureHandler.NothingFound(key);
		}
		return FakeAnswer.Xml(ownImages ? recorded.Body.Replace(Site + "/a/", $"http://127.0.0.1:{Port}/a/") : recorded.Body, recorded.Status);
	}
}
