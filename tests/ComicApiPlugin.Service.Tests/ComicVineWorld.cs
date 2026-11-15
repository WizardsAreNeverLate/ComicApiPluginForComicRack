using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Service.Tests;

/// <summary>The real service exe with its Comic Vine requests sent to a fake on a loopback port.</summary>
internal sealed class ComicVineWorld : IDisposable
{
	public const string Key = "cv-key-7Hq2mZx";

	public const string Settings =
		"""{"ignoredSearchTerms":[],"ignoredPublishers":[],"ignoreBeforeYear":0,"ignoreAfterYear":9999,"neverIgnoreThreshold":9999,"maxSearchResults":200}""";

	private ComicVineWorld(FakeComicVineServer comicVine, RunningService service)
	{
		ComicVine = comicVine;
		Service = service;
		Client = service.Client();
	}

	public FakeComicVineServer ComicVine { get; }

	public RunningService Service { get; }

	public HttpClient Client { get; }

	/// <param name="answer">Answers a request before the recorded responses do, or returns null to leave it to them.</param>
	public static async Task<ComicVineWorld> StartAsync(Func<FakeRequest, Task<FakeAnswer?>>? answer = null)
	{
		FakeComicVineServer comicVine = FakeComicVineServer.FromFixtures(FixtureFiles.ReadFolder(FixtureFiles.OutputFolder), answer);
		try
		{
			return new ComicVineWorld(comicVine, await RunningService.StartAsync(comicVineBaseUrl: comicVine.BaseUrl));
		}
		catch
		{
			comicVine.Dispose();
			throw;
		}
	}

	/// <summary>A request body: the settings and the members given, which are JSON text.</summary>
	public static string Body(string members, string settings = Settings) => $$"""{"settings":{{settings}}{{(members.Length > 0 ? "," + members : "")}}}""";

	public HttpRequestMessage Request(string path, string body, string? key = Key, string? run = "run-1")
	{
		var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		if (key is not null)
		{
			request.Headers.Add(ServiceProtocol.ComicVineKeyHeader, key);
		}
		if (run is not null)
		{
			request.Headers.Add(ServiceProtocol.RunIdHeader, run);
		}
		return request;
	}

	public async Task<HttpResponseMessage> PostAsync(string path, string body, string? key = Key, string? run = "run-1", CancellationToken ct = default)
	{
		using HttpRequestMessage request = Request(path, body, key, run);
		return await Client.SendAsync(request, ct);
	}

	/// <summary>The parsed lines of a JSON Lines response.</summary>
	public static async Task<List<JsonElement>> LinesAsync(HttpResponseMessage response)
	{
		var lines = new List<JsonElement>();
		using var reader = new StringReader(await response.Content.ReadAsStringAsync());
		while (await reader.ReadLineAsync() is { } line)
		{
			using JsonDocument json = JsonDocument.Parse(line);
			lines.Add(json.RootElement.Clone());
		}
		return lines;
	}

	public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
	{
		using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return json.RootElement.Clone();
	}

	/// <summary>Stops the service, so its log file is complete, and returns the log.</summary>
	public async Task<string> StopAndReadLogAsync()
	{
		using HttpResponseMessage shutdown = await Client.PostAsync(ServiceProtocol.ShutdownPath, null);
		await Service.ExitsWithinAsync(TimeSpan.FromSeconds(10));
		return Service.ReadLogs();
	}

	public void Dispose()
	{
		Client.Dispose();
		Service.Dispose();
		ComicVine.Dispose();
	}
}
