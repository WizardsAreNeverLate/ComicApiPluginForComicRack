using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Service;
using ComicApiPlugin.Tests.VerseDb;
using ComicApiPlugin.VerseDb;
using static ComicApiPlugin.Tests.Plugin.Described;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// ServiceVerseDb and ServiceCoverComparer against the service the build packaged. The service and an in-process
/// VerseDbHttp ask the same fake VerseDB, so each test compares what the cover search gets on both routes.
/// </summary>
public sealed class ServiceVerseDbTests
{
	private static readonly byte[] Jpeg = Encoding.ASCII.GetBytes("JPEG-BYTES-OF-A-COVER");

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task A_cover_lookup_gives_the_same_candidates_and_sends_VerseDB_the_same_form_as_the_transport(bool clear)
	{
		using var world = new World(request => clear || request.Method != "POST" ? null : FakeAnswer.Json(VerseDbFixtures.UnclearCoverLookup));

		CoverLookup expected = await world.InProcess.LookUpCoverAsync(Jpeg, CancellationToken.None);
		CoverLookup found = await world.ThroughService.LookUpCoverAsync(Jpeg, CancellationToken.None);

		await Assert.That(found.Candidates.Count).IsEqualTo(clear ? 1 : 2);
		await Assert.That(found.ClearMatch).IsEqualTo(clear);
		await Assert.That(Describe(found)).IsEqualTo(Describe(expected));
		FakeRequest direct = world.VerseDb.Requests[0];
		FakeRequest remote = world.VerseDb.Requests[1];
		await Assert.That(remote.Method + " " + remote.Target).IsEqualTo(direct.Method + " " + direct.Target);
		await Assert.That(remote.Headers["Authorization"]).IsEqualTo("Bearer " + VerseDbFixtures.Token);
		await Assert.That(Parts(remote)).IsEqualTo(Parts(direct));
		await Assert.That(Parts(remote)).IsEqualTo(
			"Content-Type: image/jpeg\r\nContent-Disposition: form-data; name=\"image\"; filename=\"cover.jpg\"\r\n\r\nJPEG-BYTES-OF-A-COVER\r\n");
	}

	[Test]
	public async Task The_details_of_an_issue_are_the_same_as_the_transports()
	{
		using var world = new World();

		IssueMetadata expected = await world.InProcess.GetIssueAsync(new VerseDbIssueId(5432), CancellationToken.None);
		IssueMetadata issue = await world.ThroughService.GetIssueAsync(new VerseDbIssueId(5432), CancellationToken.None);

		await Assert.That(issue.Title).IsEqualTo("The Joker");
		await Assert.That(issue.Rating).IsEqualTo(4.5f);
		await Assert.That(Describe(issue)).IsEqualTo(Describe(expected));
		await Assert.That(issue.Published).IsEqualTo(expected.Published);
	}

	[Test]
	public async Task A_cover_picture_has_the_bytes_VerseDB_served_and_its_download_carries_no_token()
	{
		byte[] webp = [.. Enumerable.Range(0, 70_000).Select(i => (byte)(i * 31))];
		using var world = new World(cover: webp);

		byte[] expected = await world.InProcess.GetImageAsync(world.VerseDb.CoverUrl, CancellationToken.None);
		byte[] image = await world.ThroughService.GetImageAsync(world.VerseDb.CoverUrl, CancellationToken.None);

		await Assert.That(image.SequenceEqual(webp)).IsTrue();
		await Assert.That(image.SequenceEqual(expected)).IsTrue();
		await Assert.That(world.VerseDb.Requests.Any(request => request.Headers.ContainsKey("Authorization"))).IsFalse();
	}

	[Test]
	[Arguments(401, false)]
	[Arguments(422, false)]
	[Arguments(429, true)]
	[Arguments(503, false)]
	public async Task A_VerseDB_failure_is_the_same_exception_as_the_transports(int status, bool retryAfter)
	{
		using var world = new World(request => FakeAnswer.Json("{ \"message\": \"Refused for " + VerseDbFixtures.Token + ".\" }", (HttpStatusCode)status) with
		{
			Headers = retryAfter ? new Dictionary<string, string> { ["Retry-After"] = "42" } : new Dictionary<string, string>(),
		});

		VerseDbException expected = (await Assert.ThrowsAsync<VerseDbException>(() => world.InProcess.LookUpCoverAsync(Jpeg, CancellationToken.None)))!;
		VerseDbException error = (await Assert.ThrowsAsync<VerseDbException>(() => world.ThroughService.LookUpCoverAsync(Jpeg, CancellationToken.None)))!;

		await Assert.That(error.Status).IsEqualTo((HttpStatusCode)status);
		await Assert.That(error.RetryAfter).IsEqualTo(retryAfter ? TimeSpan.FromSeconds(42) : null);
		await Assert.That(error.IsRateLimited).IsEqualTo(status == 429);
		await Assert.That(Describe(error)).IsEqualTo(Describe(expected));
		await Assert.That(error.ToString()).DoesNotContain(VerseDbFixtures.Token);
	}

	[Test]
	public async Task A_cover_picture_that_is_missing_is_the_same_exception_as_the_transports()
	{
		using var world = new World();
		var missing = new Uri(world.VerseDb.BaseUrl + "/covers/none.webp");

		VerseDbException expected = (await Assert.ThrowsAsync<VerseDbException>(() => world.InProcess.GetImageAsync(missing, CancellationToken.None)))!;
		VerseDbException error = (await Assert.ThrowsAsync<VerseDbException>(() => world.ThroughService.GetImageAsync(missing, CancellationToken.None)))!;

		await Assert.That(error.Status).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(Describe(error)).IsEqualTo(Describe(expected));
	}

	[Test]
	public async Task A_cover_search_through_the_service_waits_as_long_as_a_429_asks_shows_the_wait_and_asks_again()
	{
		int lookups = 0;
		using var world = new World(request => request.Method == "POST" && Interlocked.Increment(ref lookups) == 1
			? FakeAnswer.Json("""{ "message": "Too many requests." }""", (HttpStatusCode)429) with { Headers = new Dictionary<string, string> { ["Retry-After"] = "42" } }
			: null);
		var ui = new CoverSearchRunTests.FakeUi();
		var clock = new ManualClock();
		var book = new CoverSearchRunTests.FakeScrapeBook();
		var run = new CoverSearchRun(world.ThroughService, ui, clock, world.Log, new Configuration { Advanced = AdvancedSettings.Parse("") });
		ui.Answers.Enqueue(new CoverChoice.Picked(new CoverCandidate(new VerseDbIssueId(5432), "Batman", 1940, 1, "12", "The Joker", "DC Comics", "1990-06-01", null)));

		RunSummary summary = await run.RunAsync([book], CancellationToken.None);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(ui.RateLimits).IsEquivalentTo([TimeSpan.FromSeconds(42)]);
		await Assert.That(clock.Delays).IsEquivalentTo([TimeSpan.FromSeconds(42)]);
		await Assert.That(lookups).IsEqualTo(2);
		await Assert.That(book.Applied.Single().Values.Title).IsEqualTo("The Joker");
		await Assert.That(world.Log.Snapshot()).Contains("VerseDB asked to wait 42 seconds.");
	}

	[Test]
	public async Task A_service_that_cannot_start_ends_the_cover_search_with_its_reason_and_the_runtime_download_link()
	{
		using var world = new World(BuiltService.Script("@echo You must install .NET to run this application. 1>&2", "@exit /b -2147450730"));

		VerseDbException error = (await Assert.ThrowsAsync<VerseDbException>(() => world.ThroughService.LookUpCoverAsync(Jpeg, CancellationToken.None)))!;

		await Assert.That(error.Status).IsNull();
		await Assert.That(error.IsRateLimited).IsFalse();
		await Assert.That(error.InnerException is ServiceUnavailableException).IsTrue();
		await Assert.That(error.Message).IsEqualTo(
			"The local API service needs the .NET 11 runtime (ASP.NET Core Runtime 11, x64), which is not installed. "
			+ $"Download it from {ServiceProtocol.RuntimeDownloadUrl} and restart ComicRack.");
		await Assert.That(world.VerseDb.Requests).IsEmpty();
	}

	[Test]
	public async Task Cancelling_a_call_throws_OperationCanceledException_and_a_cancelled_call_sends_nothing()
	{
		using var world = new World();
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();

		OperationCanceledException? thrown = null;
		try
		{
			await world.ThroughService.GetIssueAsync(new VerseDbIssueId(5432), cancel.Token);
		}
		catch (OperationCanceledException error)
		{
			thrown = error;
		}

		await Assert.That(thrown).IsNotNull();
		await Assert.That(world.VerseDb.Requests).IsEmpty();
	}

	[Test]
	public async Task The_token_reaches_VerseDB_and_neither_the_service_log_nor_the_plugin_log_holds_it()
	{
		using var world = new World();

		await world.ThroughService.LookUpCoverAsync(Jpeg, CancellationToken.None);
		await world.ThroughService.GetImageAsync(world.VerseDb.CoverUrl, CancellationToken.None);
		world.Host.Stop();
		string serviceLog = BuiltService.ReadLogs(world.Launch.LogFolder);

		await Assert.That(world.VerseDb.Requests[0].Headers["Authorization"]).IsEqualTo("Bearer " + VerseDbFixtures.Token);
		await Assert.That(serviceLog).Contains($"VerseDB POST 127.0.0.1:{world.VerseDb.Port}{FakeVerseDbServer.LookupPath} responded 200 in ");
		await Assert.That(serviceLog).Contains($"VerseDB GET 127.0.0.1:{world.VerseDb.Port}{FakeVerseDbServer.CoverPath} responded 200 in ");
		await Assert.That(serviceLog).Contains($"(run {world.RunId})");
		await Assert.That(serviceLog).DoesNotContain(VerseDbFixtures.Token);
		await Assert.That(world.Log.Snapshot().Any(line => line.Contains(VerseDbFixtures.Token))).IsFalse();
	}

	[Test]
	public async Task The_service_scores_a_cropped_copy_of_a_cover_as_a_match_another_cover_as_none_and_bytes_that_are_no_image_as_nothing()
	{
		using var world = new World();
		using Bitmap cover = SyntheticCover.Make(2);
		using Bitmap cropped = SyntheticCover.Cropped(cover, 0.05);
		using Bitmap sibling = SyntheticCover.Make(4);
		byte[] book = SyntheticCover.Encode(cover, ImageFormat.Png);

		CoverSimilarity? same = world.Comparer.Compare(book, SyntheticCover.Encode(cropped, ImageFormat.Jpeg));
		CoverSimilarity? other = world.Comparer.Compare(book, SyntheticCover.Encode(sibling, ImageFormat.Png));
		CoverSimilarity? none = world.Comparer.Compare(book, [1, 2, 3, 4, 5]);

		await Assert.That(same!.Value.IsMatch).IsTrue();
		await Assert.That(same.Value.Correlation).IsGreaterThan(CoverSimilarity.CorrelationThreshold);
		await Assert.That(other!.Value.IsMatch).IsFalse();
		await Assert.That(none).IsNull();
	}

	[Test]
	public async Task A_cover_search_with_auto_accept_writes_the_single_candidate_whose_cover_the_service_matches_without_asking()
	{
		using Bitmap cover = SyntheticCover.Make(2);
		using Bitmap cropped = SyntheticCover.Cropped(cover, 0.05);
		using var world = new World(cover: SyntheticCover.Encode(cropped, ImageFormat.Jpeg));
		var ui = new CoverSearchRunTests.FakeUi();
		var book = new CoverSearchRunTests.FakeScrapeBook { Page = () => new Bitmap(cover) };
		var config = new Configuration { Advanced = AdvancedSettings.Parse(""), VerseDbAutoAccept = true };

		RunSummary summary = await new CoverSearchRun(world.ThroughService, ui, new ManualClock(), world.Log, config, world.Comparer).RunAsync([book], CancellationToken.None);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(ui.Choices).IsEqualTo(0);
		await Assert.That(book.Applied.Single().Values.Title).IsEqualTo("The Joker");
		await Assert.That(world.Log.Snapshot().Any(line => line.StartsWith("cover check: Batman #12: correlation ", StringComparison.Ordinal) && line.EndsWith("-> match", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task A_cover_search_with_auto_accept_asks_the_user_when_the_service_cannot_start()
	{
		using var verseDb = new World();
		using var noService = new World(BuiltService.Script("@echo You must install .NET to run this application. 1>&2", "@exit /b -2147450730"));
		var ui = new CoverSearchRunTests.FakeUi();
		ui.Answers.Enqueue(new CoverChoice.Skip());
		var book = new CoverSearchRunTests.FakeScrapeBook();
		var config = new Configuration { Advanced = AdvancedSettings.Parse(""), VerseDbAutoAccept = true };

		RunSummary summary = await new CoverSearchRun(verseDb.InProcess, ui, new ManualClock(), noService.Log, config, noService.Comparer).RunAsync([book], CancellationToken.None);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(ui.Choices).IsEqualTo(1);
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(noService.Log.Snapshot()).Contains(
			"cover auto-accept is unavailable: The local API service needs the .NET 11 runtime (ASP.NET Core Runtime 11, x64), which is not installed. "
			+ $"Download it from {ServiceProtocol.RuntimeDownloadUrl} and restart ComicRack.");
	}

	/// <summary>The parts of a multipart body without its boundary lines, which differ between two requests.</summary>
	private static string Parts(FakeRequest request)
	{
		string boundary = request.Headers["Content-Type"].Split(["boundary="], StringSplitOptions.None)[1].Trim('"');
		return string.Concat(Encoding.ASCII.GetString(request.Body).Split(["--" + boundary + "--\r\n", "--" + boundary + "\r\n"], StringSplitOptions.RemoveEmptyEntries));
	}

	/// <summary>One fake VerseDB, asked by a transport in this process and by the packaged service.</summary>
	private sealed class World : IDisposable
	{
		private readonly VerseDbHttp http;
		private readonly ServiceConnection connection;

		public World(Func<FakeRequest, FakeAnswer?>? answer = null, byte[]? cover = null) : this(BuiltService.Launch(), answer, cover)
		{
		}

		public World(ServiceLaunch launch, Func<FakeRequest, FakeAnswer?>? answer = null, byte[]? cover = null)
		{
			VerseDb = new FakeVerseDbServer(answer, cover);
			Launch = launch with { Environment = new Dictionary<string, string> { [ServiceProtocol.VerseDbBaseUrlVariable] = VerseDb.BaseUrl } };
			http = new VerseDbHttp(new HttpClientHandler { UseProxy = false }, VerseDbFixtures.Token, "ua", new Uri(VerseDb.BaseUrl));
			Host = new ServiceHost(Launch, () => false, Log.Debug);
			connection = new ServiceConnection(Host);
			ThroughService = new ServiceVerseDb(connection, VerseDbFixtures.Token, RunId);
			Comparer = new ServiceCoverComparer(connection);
		}

		public FakeVerseDbServer VerseDb { get; }

		public ServiceLaunch Launch { get; }

		public ServiceHost Host { get; }

		public DebugLog Log { get; } = new();

		public string RunId { get; } = "run-" + Guid.NewGuid().ToString("N");

		public IVerseDb InProcess => http;

		public IVerseDb ThroughService { get; }

		public ICoverComparer Comparer { get; }

		public void Dispose()
		{
			connection.Dispose();
			Host.Dispose();
			http.Dispose();
			VerseDb.Dispose();
		}
	}
}
