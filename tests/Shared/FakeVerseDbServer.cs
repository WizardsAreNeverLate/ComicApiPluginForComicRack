using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace ComicApiPlugin.Fixtures;

/// <summary>
/// A VerseDB that listens on a loopback port, so the service process and an in-process VerseDbHttp can both be pointed
/// at it. It answers a cover lookup, issue 5432 and one cover picture, and 404 for anything else.
/// </summary>
public sealed class FakeVerseDbServer : IDisposable
{
	public const string LookupPath = "/api/v1/lookup/cover";
	public const string IssuePath = "/api/v1/issues/5432";
	public const string CoverPath = "/covers/5432.webp";

	private readonly FakeHttpServer server;

	/// <param name="first">Answers a request before the fixtures do, or returns null to leave it to them.</param>
	/// <param name="cover">The bytes of the cover picture.</param>
	public FakeVerseDbServer(Func<FakeRequest, FakeAnswer?>? first = null, byte[]? cover = null)
	{
		server = new FakeHttpServer(request => Task.FromResult(first?.Invoke(request) ?? (request.Method, request.Target.Split('?')[0]) switch
		{
			("POST", LookupPath) => FakeAnswer.Json(VerseDbFixtures.ClearCoverLookup),
			("GET", IssuePath) => FakeAnswer.Json(VerseDbFixtures.Issue),
			("GET", CoverPath) => new FakeAnswer(HttpStatusCode.OK, cover ?? [1, 2, 3], "image/webp"),
			_ => FakeAnswer.Json("""{ "message": "Not found." }""", HttpStatusCode.NotFound),
		}));
	}

	public int Port => server.Port;

	/// <summary>The value for VerseDbHttp's base URL and for the service's COMICAPI_VERSEDB_BASE_URL.</summary>
	public string BaseUrl => $"http://127.0.0.1:{Port}";

	public Uri CoverUrl => new(BaseUrl + CoverPath);

	public IReadOnlyList<FakeRequest> Requests => server.Requests;

	public void Dispose() => server.Dispose();
}
