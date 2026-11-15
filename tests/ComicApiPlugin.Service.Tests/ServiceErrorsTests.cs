using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Service.ComicVine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ComicApiPlugin.Service.Tests;

/// <summary>
/// The Comic Vine endpoints in this process, on a transport that fails with an exception no endpoint expects: the one
/// failure the real exe cannot be made to show.
/// </summary>
public sealed class ServiceErrorsTests
{
	[Test]
	public async Task A_call_that_fails_unexpectedly_before_it_answers_is_a_500_that_names_the_error()
	{
		await using InProcessService service = await InProcessService.StartAsync(_ => true);

		using HttpResponseMessage response = await service.PostAsync(ServiceProtocol.ComicVineFindIssuePath, ComicVineWorld.Body("\"series\":796,\"issueNumber\":\"497\""));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
		await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"comicVine":null,"message":null,"serviceError":"InvalidOperationException: unexpected"}""");
	}

	[Test]
	public async Task A_search_that_fails_unexpectedly_after_its_first_progress_line_ends_with_a_line_that_names_the_error()
	{
		await using InProcessService service = await InProcessService.StartAsync(url => url.Contains("&page=2"));

		using HttpResponseMessage response = await service.PostAsync(ServiceProtocol.ComicVineSearchPath, ComicVineWorld.Body("\"terms\":\"batman\""));
		List<JsonElement> lines = await ComicVineWorld.LinesAsync(response);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(lines[0].TryGetProperty("progress", out _)).IsTrue();
		await Assert.That(lines[^1].GetRawText()).IsEqualTo("""{"serviceError":"InvalidOperationException: unexpected"}""");
	}

	private sealed class InProcessService : IAsyncDisposable
	{
		private readonly WebApplication app;
		private readonly FakeComicVineServer comicVine;
		private readonly HttpClient client;

		private InProcessService(WebApplication app, FakeComicVineServer comicVine)
		{
			this.app = app;
			this.comicVine = comicVine;
			client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
		}

		/// <summary>The service's error handling and Comic Vine endpoints, on a transport that throws for each URL that <paramref name="fails"/> names.</summary>
		public static async Task<InProcessService> StartAsync(Func<string, bool> fails)
		{
			FakeComicVineServer comicVine = FakeComicVineServer.FromFixtures(FixtureFiles.ReadFolder(FixtureFiles.OutputFolder));
			WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
			builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
			builder.Services.AddRoutingCore();
			WebApplication app = builder.Build();
			app.Use(ServiceErrors.Invoke);
			app.UseRouting();
			var sessions = new ComicVineSessions(key => new ComicVineHttp(new Failing(fails), key, "ua", new NoWait(), comicVine.BaseUrl), TimeProvider.System);
			new ComicVineEndpoints(sessions).Map(app);
			await app.StartAsync();
			return new InProcessService(app, comicVine);
		}

		public async Task<HttpResponseMessage> PostAsync(string path, string body)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body) };
			request.Headers.Add(ServiceProtocol.ComicVineKeyHeader, ComicVineWorld.Key);
			request.Headers.Add(ServiceProtocol.RunIdHeader, "run-1");
			return await client.SendAsync(request);
		}

		public async ValueTask DisposeAsync()
		{
			client.Dispose();
			await app.DisposeAsync();
			comicVine.Dispose();
		}
	}

	private sealed class Failing(Func<string, bool> fails) : DelegatingHandler(new HttpClientHandler { UseProxy = false })
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			fails(request.RequestUri!.ToString()) ? throw new InvalidOperationException("unexpected") : base.SendAsync(request, cancellationToken);
	}

	private sealed class NoWait : IClock
	{
		public DateTime Now => DateTime.Now;

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
	}
}
