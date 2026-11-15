using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Service.Tests;

/// <summary>The real service exe with its VerseDB requests sent to a fake on a loopback port.</summary>
internal sealed class VerseDbWorld : IDisposable
{
	public const string Run = "run-7";

	private VerseDbWorld(FakeVerseDbServer verseDb, RunningService service)
	{
		VerseDb = verseDb;
		Service = service;
		Client = service.Client();
	}

	public FakeVerseDbServer VerseDb { get; }

	public RunningService Service { get; }

	public HttpClient Client { get; }

	/// <param name="first">Answers a request before the fixtures do, or returns null to leave it to them.</param>
	/// <param name="baseUrl">Where the service looks for VerseDB, when that is not the fake.</param>
	public static async Task<VerseDbWorld> StartAsync(Func<FakeRequest, FakeAnswer?>? first = null, byte[]? cover = null, string? baseUrl = null)
	{
		var verseDb = new FakeVerseDbServer(first, cover);
		try
		{
			return new VerseDbWorld(verseDb, await RunningService.StartAsync(verseDbBaseUrl: baseUrl ?? verseDb.BaseUrl));
		}
		catch
		{
			verseDb.Dispose();
			throw;
		}
	}

	public Task<HttpResponseMessage> LookUpCoverAsync(byte[] image, string? token = VerseDbFixtures.Token, string? run = Run) =>
		SendAsync(ServiceProtocol.VerseDbLookupCoverPath, RunningService.Multipart((ServiceProtocol.ImagePart, image)), token, run);

	public Task<HttpResponseMessage> PostJsonAsync(string path, string body, string? token = VerseDbFixtures.Token, string? run = Run) =>
		SendAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), token, run);

	public async Task<HttpResponseMessage> SendAsync(string path, HttpContent content, string? token = VerseDbFixtures.Token, string? run = Run)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
		if (token is not null)
		{
			request.Headers.Add(ServiceProtocol.VerseDbTokenHeader, token);
		}
		if (run is not null)
		{
			request.Headers.Add(ServiceProtocol.RunIdHeader, run);
		}
		return await Client.SendAsync(request);
	}


	public void Dispose()
	{
		Client.Dispose();
		Service.Dispose();
		VerseDb.Dispose();
	}
}
