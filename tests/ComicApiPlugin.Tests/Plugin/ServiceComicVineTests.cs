using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Service;
using TUnit.Assertions.Enums;
using static ComicApiPlugin.Tests.Plugin.Described;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// ServiceComicVine against the service the build packaged. The service and an in-process ComicVineCatalog ask the same
/// fake Comic Vine, which answers from the recorded responses, so each test compares what the engine gets on both routes.
/// </summary>
public sealed class ServiceComicVineTests
{
	private const string Key = "test-key-not-hex-so-the-log-mask-misses-it";

	private static readonly SearchSettings Settings = new([], [], 0, 9999, 9999, 200);
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

	[Test]
	public async Task A_search_gives_the_same_series_the_same_progress_and_the_same_requests_as_the_catalog()
	{
		using var world = new World();
		var direct = new Recorder();
		var remote = new Recorder();

		IReadOnlyList<SeriesRef> expected = await world.InProcess.SearchSeriesAsync("batman", direct, CancellationToken.None);
		IReadOnlyList<string> expectedRequests = world.TakeRequests();
		IReadOnlyList<SeriesRef> found = await world.ThroughService.SearchSeriesAsync("batman", remote, CancellationToken.None);

		await Assert.That(found.Count).IsEqualTo(200);
		await Assert.That(Describe(found)).IsEqualTo(Describe(expected));
		await Assert.That(remote.Reports).IsEquivalentTo(direct.Reports, CollectionOrdering.Matching);
		await Assert.That(remote.Reports.Count).IsEqualTo(2);
		await Assert.That(world.TakeRequests()).IsEquivalentTo(expectedRequests, CollectionOrdering.Matching);
	}

	[Test]
	public async Task The_issues_of_a_series_and_an_issue_found_by_number_are_the_same_as_the_catalogs()
	{
		using var world = new World();
		var direct = new Recorder();
		var remote = new Recorder();

		IReadOnlyList<IssueRef> expected = await world.InProcess.GetIssuesAsync(new SeriesId(91273), direct, CancellationToken.None);
		IssueRef? expectedFound = await world.InProcess.FindIssueAsync(new SeriesId(796), " 00497 ", CancellationToken.None);
		IssueRef? expectedNone = await world.InProcess.FindIssueAsync(new SeriesId(9999), "15", CancellationToken.None);
		IReadOnlyList<string> expectedRequests = world.TakeRequests();
		IReadOnlyList<IssueRef> issues = await world.ThroughService.GetIssuesAsync(new SeriesId(91273), remote, CancellationToken.None);
		IssueRef? found = await world.ThroughService.FindIssueAsync(new SeriesId(796), " 00497 ", CancellationToken.None);
		IssueRef? none = await world.ThroughService.FindIssueAsync(new SeriesId(9999), "15", CancellationToken.None);

		await Assert.That(issues.Count).IsEqualTo(163);
		await Assert.That(Describe(issues)).IsEqualTo(Describe(expected));
		await Assert.That(remote.Reports).IsEquivalentTo(direct.Reports, CollectionOrdering.Matching);
		await Assert.That(found).IsEqualTo(expectedFound);
		await Assert.That(found!.Id).IsEqualTo(new IssueId(37566));
		await Assert.That(expectedNone).IsNull();
		await Assert.That(none).IsNull();
		await Assert.That(world.TakeRequests()).IsEquivalentTo(expectedRequests, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(37566, false)]
	[Arguments(535328, true)]
	[Arguments(9000001, true)]
	[Arguments(9000002, true)]
	public async Task The_details_of_an_issue_are_the_same_as_the_catalogs(int id, bool includeRating)
	{
		using var world = new World();

		Issue expected = await world.InProcess.GetIssueAsync(new IssueId(id), includeRating, CancellationToken.None);
		IReadOnlyList<string> expectedRequests = world.TakeRequests();
		Issue issue = await world.ThroughService.GetIssueAsync(new IssueId(id), includeRating, CancellationToken.None);

		await Assert.That(Describe(issue)).IsEqualTo(Describe(expected));
		await Assert.That(issue.Published).IsEqualTo(expected.Published);
		await Assert.That(world.TakeRequests()).IsEquivalentTo(expectedRequests, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_series_resolved_by_its_id_or_by_an_issue_is_the_same_as_the_catalogs()
	{
		using var world = new World();
		ComicVineTarget[] targets =
		[
			new ComicVineTarget.OfSeries(new SeriesId(796)),
			new ComicVineTarget.OfIssue(new IssueId(37566)),
			new ComicVineTarget.OfIssue(new IssueId(999999999)),
		];
		var expected = new List<SeriesRef?>();
		var resolved = new List<SeriesRef?>();

		foreach (ComicVineTarget target in targets)
		{
			expected.Add(await world.InProcess.ResolveSeriesAsync(target, CancellationToken.None));
		}
		foreach (ComicVineTarget target in targets)
		{
			resolved.Add(await world.ThroughService.ResolveSeriesAsync(target, CancellationToken.None));
		}

		await Assert.That(resolved[0]).IsEqualTo(expected[0]);
		await Assert.That(resolved[0]!.Name).IsEqualTo("Batman");
		await Assert.That(resolved[1]).IsEqualTo(expected[1]);
		await Assert.That(resolved[1]!.Id).IsEqualTo(new SeriesId(796));
		await Assert.That(expected[2]).IsNull();
		await Assert.That(resolved[2]).IsNull();
	}

	[Test]
	[Arguments(140, 100)]
	[Arguments(60, 100)]
	public async Task An_image_has_the_same_size_and_pixels_as_the_catalogs_and_a_missing_one_is_null(int width, int height)
	{
		byte[] png = TwoColours(width, height);
		using var world = new World(request => Task.FromResult(request.Target == "/img/cover.png" ? new FakeAnswer(HttpStatusCode.OK, png, "image/png") : null));
		var cover = new Uri($"http://127.0.0.1:{world.ComicVine.Port}/img/cover.png");
		var missing = new Uri($"http://127.0.0.1:{world.ComicVine.Port}/img/missing.png");

		using Bitmap? expected = await world.InProcess.GetImageAsync(cover, CancellationToken.None);
		using Bitmap? image = await world.ThroughService.GetImageAsync(cover, CancellationToken.None);
		using Bitmap? none = await world.ThroughService.GetImageAsync(missing, CancellationToken.None);

		await Assert.That(image!.Size).IsEqualTo(expected!.Size);
		await Assert.That(Pixels(image)).IsEquivalentTo(Pixels(expected), CollectionOrdering.Matching);
		await Assert.That(none).IsNull();
	}

	[Test]
	[Arguments("invalid key", ComicVineError.InvalidApiKey)]
	[Arguments("rate limited", ComicVineError.RateLimited)]
	public async Task A_Comic_Vine_failure_is_the_same_exception_as_the_catalogs(string terms, ComicVineError kind)
	{
		using var world = new World();

		ComicVineException expected = (await Assert.ThrowsAsync<ComicVineException>(() => world.InProcess.SearchSeriesAsync(terms, null, CancellationToken.None)))!;
		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => world.ThroughService.SearchSeriesAsync(terms, null, CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(kind);
		await Assert.That(Describe(error)).IsEqualTo(Describe(expected));
		await Assert.That(error.Url).DoesNotContain(Key);
	}

	[Test]
	public async Task A_Comic_Vine_that_is_down_is_the_same_recoverable_network_error_as_the_catalogs()
	{
		using var world = new World(request => Task.FromResult<FakeAnswer?>(FakeAnswer.Xml("<html>down</html>", HttpStatusCode.ServiceUnavailable)));

		ComicVineException expected = (await Assert.ThrowsAsync<ComicVineException>(
			() => world.InProcess.GetIssueAsync(new IssueId(37566), false, CancellationToken.None)))!;
		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(
			() => world.ThroughService.GetIssueAsync(new IssueId(37566), false, CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(ComicVineError.Network);
		await Assert.That(error.IsRecoverable).IsTrue();
		await Assert.That(Describe(error)).IsEqualTo(Describe(expected));
	}

	[Test]
	public async Task A_failure_on_a_later_page_comes_after_the_same_progress_as_the_catalogs()
	{
		RecordedResponse rateLimit = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "synthetic-rate-limit.txt"));
		using var world = new World(request => Task.FromResult(
			request.Target.EndsWith("&page=2", StringComparison.Ordinal) ? FakeAnswer.Xml(rateLimit.Body, rateLimit.Status) : null));
		var direct = new Recorder();
		var remote = new Recorder();

		ComicVineException expected = (await Assert.ThrowsAsync<ComicVineException>(() => world.InProcess.SearchSeriesAsync("batman", direct, CancellationToken.None)))!;
		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => world.ThroughService.SearchSeriesAsync("batman", remote, CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(ComicVineError.RateLimited);
		await Assert.That(Describe(error)).IsEqualTo(Describe(expected));
		await Assert.That(remote.Reports).IsEquivalentTo(direct.Reports, CollectionOrdering.Matching);
		await Assert.That(remote.Reports.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Cancelling_a_call_throws_OperationCanceledException_and_stops_the_request_to_Comic_Vine()
	{
		var received = new TaskCompletionSource<FakeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var world = new World(async request =>
		{
			received.TrySetResult(request);
			await release.Task;
			return null;
		});
		using var cancel = new CancellationTokenSource();

		Task<IReadOnlyList<SeriesRef>> search = world.ThroughService.SearchSeriesAsync("preacher", null, cancel.Token);
		FakeRequest atComicVine = await WithinAsync(received.Task);
		cancel.Cancel();
		Exception? thrown = await CatchAsync(search);
		await WithinAsync(atComicVine.ClientGone);
		release.SetResult(true);

		await Assert.That(thrown is OperationCanceledException).IsTrue();
		await Assert.That(((OperationCanceledException)thrown!).CancellationToken).IsEqualTo(cancel.Token);
	}

	[Test]
	public async Task A_call_that_is_cancelled_before_it_starts_sends_nothing()
	{
		using var world = new World();

		Exception? thrown = await CatchAsync(world.ThroughService.SearchSeriesAsync("preacher", null, new CancellationToken(canceled: true)));

		await Assert.That(thrown is OperationCanceledException).IsTrue();
		await Assert.That(world.ComicVine.Requests).IsEmpty();
	}

	[Test]
	public async Task A_service_that_cannot_start_ends_the_run_with_its_reason_and_the_runtime_download_link()
	{
		using var world = new World(BuiltService.Script("@echo You must install .NET to run this application. 1>&2", "@exit /b -2147450730"));

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(
			() => world.ThroughService.GetIssueAsync(new IssueId(37566), false, CancellationToken.None)))!;
		using Bitmap? image = await world.ThroughService.GetImageAsync(new Uri("http://127.0.0.1:1/cover.png"), CancellationToken.None);

		await Assert.That(error.Error).IsEqualTo(ComicVineError.ServiceUnavailable);
		await Assert.That(error.IsRecoverable).IsFalse();
		await Assert.That(error.InnerException is ServiceUnavailableException).IsTrue();
		await Assert.That(error.InnerException!.Message).IsEqualTo(
			"The local API service needs the .NET 11 runtime (ASP.NET Core Runtime 11, x64), which is not installed. "
			+ $"Download it from {ServiceProtocol.RuntimeDownloadUrl} and restart ComicRack.");
		await Assert.That(image).IsNull();
		await Assert.That(world.ComicVine.Requests).IsEmpty();
	}

	[Test]
	public async Task A_service_that_died_is_started_again_by_the_next_call()
	{
		using var world = new World();

		SeriesRef? before = await world.ThroughService.ResolveSeriesAsync(new ComicVineTarget.OfSeries(new SeriesId(796)), CancellationToken.None);
		var running = (ServiceState.Running)world.Host.State;
		running.Process.Kill();
		running.Process.WaitForExit();
		SeriesRef? after = await world.ThroughService.ResolveSeriesAsync(new ComicVineTarget.OfSeries(new SeriesId(796)), CancellationToken.None);

		await Assert.That(after).IsEqualTo(before);
		await Assert.That(after!.Name).IsEqualTo("Batman");
		await Assert.That(((ServiceState.Running)world.Host.State).Endpoint.Pid).IsNotEqualTo(running.Endpoint.Pid);
	}

	[Test]
	public async Task The_key_reaches_Comic_Vine_and_neither_the_service_log_nor_the_plugin_log_holds_it()
	{
		using var world = new World();

		await world.ThroughService.SearchSeriesAsync("preacher", null, CancellationToken.None);
		world.Host.Stop();
		string serviceLog = BuiltService.ReadLogs(world.Launch.LogFolder);

		await Assert.That(world.ComicVine.Requests.Single().Target).Contains("api_key=" + Key + "&");
		await Assert.That(serviceLog).Contains($"Comic Vine GET 127.0.0.1:{world.ComicVine.Port}/api/search/ responded 200 in ");
		await Assert.That(serviceLog).DoesNotContain(Key);
		await Assert.That(world.Log.Snapshot().Any(line => line.Contains(Key))).IsFalse();
	}

	private static async Task<T> WithinAsync<T>(Task<T> task)
	{
		if (await Task.WhenAny(task, Task.Delay(Patience)) != task)
		{
			throw new TimeoutException($"Nothing happened within {Patience.TotalSeconds} seconds.");
		}
		return await task;
	}

	private static async Task WithinAsync(Task task)
	{
		if (await Task.WhenAny(task, Task.Delay(Patience)) != task)
		{
			throw new TimeoutException($"Nothing happened within {Patience.TotalSeconds} seconds.");
		}
		await task;
	}

	private static async Task<Exception?> CatchAsync(Task task)
	{
		try
		{
			await WithinAsync(task);
			return null;
		}
		catch (Exception error)
		{
			return error;
		}
	}

	private static byte[] TwoColours(int width, int height)
	{
		using var image = new Bitmap(width, height);
		using (Graphics g = Graphics.FromImage(image))
		{
			g.FillRectangle(Brushes.Red, 0, 0, width / 2, height);
			g.FillRectangle(Brushes.Blue, width / 2, 0, width - width / 2, height);
		}
		using var stream = new MemoryStream();
		image.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	private static List<int> Pixels(Bitmap image)
	{
		var pixels = new List<int>(image.Width * image.Height);
		for (int y = 0; y < image.Height; y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				pixels.Add(image.GetPixel(x, y).ToArgb());
			}
		}
		return pixels;
	}

	private sealed class Recorder : IProgress<QueryProgress>
	{
		private readonly List<QueryProgress> reports = [];

		public IReadOnlyList<QueryProgress> Reports
		{
			get
			{
				lock (reports)
				{
					return reports.ToArray();
				}
			}
		}

		public void Report(QueryProgress value)
		{
			lock (reports)
			{
				reports.Add(value);
			}
		}
	}

	/// <summary>One fake Comic Vine, asked by a catalog in this process and by the packaged service.</summary>
	private sealed class World : IDisposable
	{
		private readonly ComicVineHttp http;
		private readonly ServiceConnection connection;
		private int taken;

		public World(Func<FakeRequest, Task<FakeAnswer?>>? answer = null) : this(BuiltService.Launch(), answer)
		{
		}

		public World(ServiceLaunch launch, Func<FakeRequest, Task<FakeAnswer?>>? answer = null)
		{
			ComicVine = FakeComicVineServer.FromFixtures(FixtureFiles.ReadFolder(FixtureFiles.OutputFolder), answer);
			Launch = launch with { Environment = new Dictionary<string, string> { [ServiceProtocol.ComicVineBaseUrlVariable] = ComicVine.BaseUrl } };
			var clock = new ManualClock();
			http = new ComicVineHttp(new HttpClientHandler { UseProxy = false }, Key, "ua", clock, () => clock.Elapsed, ComicVine.BaseUrl);
			InProcess = new ComicVineCatalog(http, Settings);
			Host = new ServiceHost(Launch, () => false, Log.Debug);
			connection = new ServiceConnection(Host);
			ThroughService = new ServiceComicVine(connection, Key, "run-" + Guid.NewGuid().ToString("N"), Settings, Log);
		}

		public FakeComicVineServer ComicVine { get; }

		public ServiceLaunch Launch { get; }

		public ServiceHost Host { get; }

		public DebugLog Log { get; } = new();

		public IComicVine InProcess { get; }

		public IComicVine ThroughService { get; }

		/// <summary>The requests Comic Vine received since the last call, as fixture keys.</summary>
		public IReadOnlyList<string> TakeRequests()
		{
			IReadOnlyList<string> all = ComicVine.RequestKeys;
			List<string> fresh = [.. all.Skip(taken)];
			taken = all.Count;
			return fresh;
		}

		public void Dispose()
		{
			connection.Dispose();
			Host.Dispose();
			http.Dispose();
			ComicVine.Dispose();
		}
	}
}
