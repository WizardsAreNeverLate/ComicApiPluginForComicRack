using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Engine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;
using ComicVineScraper.Plugin;
using ComicVineScraper.Tests.Plugin;
using ComicBook = cYo.Projects.ComicRack.Engine.ComicBook;

namespace ComicVineScraper.Tests.Engine;

/// <summary>Answers each dialog from a queue and records every question. A question with no answer queued fails the run.</summary>
internal sealed class ScriptedUi : IScrapeUi
{
	private readonly Queue<Func<string, string?, CancellationToken, SearchChoice>> searches = new();
	private readonly Queue<Func<SeriesQuestion, CancellationToken, SeriesChoice>> series = new();
	private readonly Queue<Func<IssueQuestion, CancellationToken, IssueChoice>> issues = new();

	/// <summary>"search:initial|failed", "series:terms", "issue:series id:hint id", "no-issues:series id", in order.</summary>
	public List<string> Asked { get; } = [];

	public List<string> Started { get; } = [];

	public List<SeriesQuestion> SeriesQuestions { get; } = [];

	public List<IssueQuestion> IssueQuestions { get; } = [];

	public List<QueryKind> Queries { get; } = [];

	public int OpenQueries { get; private set; }

	public ScriptedUi Search(SearchChoice answer) => Search((_, _, _) => answer);

	public ScriptedUi Search(Func<string, string?, CancellationToken, SearchChoice> answer)
	{
		searches.Enqueue(answer);
		return this;
	}

	public ScriptedUi Series(SeriesChoice answer) => Series((_, _) => answer);

	public ScriptedUi Series(Func<SeriesQuestion, SeriesChoice> answer) => Series((q, _) => answer(q));

	public ScriptedUi Series(Func<SeriesQuestion, CancellationToken, SeriesChoice> answer)
	{
		series.Enqueue(answer);
		return this;
	}

	public ScriptedUi Issue(IssueChoice answer) => Issue((_, _) => answer);

	public ScriptedUi Issue(Func<IssueQuestion, IssueChoice> answer) => Issue((q, _) => answer(q));

	public ScriptedUi Issue(Func<IssueQuestion, CancellationToken, IssueChoice> answer)
	{
		issues.Enqueue(answer);
		return this;
	}

	public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) =>
		Started.Add($"{Path.GetFileName(facts.Snapshot.FilePath)}:{remaining}");

	public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct)
	{
		Asked.Add($"search:{initialTerms}|{failedTerms}");
		return Task.FromResult(Next(searches, "search")(initialTerms, failedTerms, ct));
	}

	public async Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct)
	{
		Asked.Add("series:" + question.SearchTerms);
		SeriesQuestions.Add(question);
		// A real dialog returns later; yielding keeps the engine honest about awaiting it.
		await Task.Yield();
		return Next(series, "series")(question, ct);
	}

	public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct)
	{
		Asked.Add($"issue:{question.Series.Id}:{question.Hint?.Id}");
		IssueQuestions.Add(question);
		return Task.FromResult(Next(issues, "issue")(question, ct));
	}

	public void ShowNoIssues(SeriesRef series) => Asked.Add("no-issues:" + series.Id);

	public IQueryProgress BeginQuery(QueryKind kind)
	{
		Queries.Add(kind);
		OpenQueries++;
		return new Progress(() => OpenQueries--);
	}

	public void AssertAllAnswered()
	{
		if (searches.Count + series.Count + issues.Count > 0)
		{
			throw new InvalidOperationException($"Unused answers: {searches.Count} search, {series.Count} series, {issues.Count} issue.");
		}
	}

	private static T Next<T>(Queue<T> queue, string dialog) =>
		queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException($"The {dialog} dialog was not scripted.");

	private sealed class Progress(Action closed) : IQueryProgress
	{
		public void Report(QueryProgress value)
		{
		}

		public void Dispose() => closed();
	}
}

/// <summary>A real ComicRack ComicBook behind the real adapter, recording every patch. GetPage serves a set first page.</summary>
internal sealed class FakeBook : IScrapeBook
{
	private readonly ComicRackBook adapter;

	public FakeBook(ComicBook book)
	{
		Book = book;
		adapter = new ComicRackBook(book, App);
	}

	public ComicBook Book { get; }

	public FakeApplication App { get; } = new();

	public List<BookPatch> Applied { get; } = [];

	public Bitmap? FirstPage { get; init; }

	public static FakeBook File(string path, Action<ComicBook>? setup = null, Bitmap? firstPage = null)
	{
		var book = new ComicBook { FilePath = path };
		setup?.Invoke(book);
		return new FakeBook(book) { FirstPage = firstPage };
	}

	/// <summary>The managed thread of every Read and Apply.</summary>
	public List<int> Threads { get; } = [];

	public BookSnapshot Read()
	{
		Threads.Add(Environment.CurrentManagedThreadId);
		return adapter.Read();
	}

	public Bitmap? GetPage(int index) => index == 0 && FirstPage is not null ? new Bitmap(FirstPage) : null;

	public void Apply(BookPatch patch, Bitmap? thumbnail)
	{
		Threads.Add(Environment.CurrentManagedThreadId);
		Applied.Add(patch);
		adapter.Apply(patch, thumbnail);
	}
}

/// <summary>
/// The real ComicVineCatalog over the recorded fixtures, plus synthetic responses, served images, and injected failures.
/// Its clock is the HTTP clock (throttle and retry); the engine gets its own, so the scrape delay can be asserted alone.
/// </summary>
internal sealed class EngineWorld : IDisposable
{
	public const string ApiKey = "0123456789abcdef0123456789abcdef01234567";

	private readonly Dictionary<string, RecordedResponse> extra = new(StringComparer.Ordinal);
	private readonly string folder = Path.Combine(Path.GetTempPath(), "cvs-engine-" + Guid.NewGuid().ToString("N"));
	private readonly Lazy<ComicVineCatalog> catalog;
	private readonly ServingHandler handler;

	public EngineWorld()
	{
		Directory.CreateDirectory(folder);
		Store = new SettingsStore(folder, Clock);
		Chosen = Store.LoadChosenSeries();
		handler = new ServingHandler(this);
		catalog = new Lazy<ComicVineCatalog>(() =>
		{
			var httpClock = new ManualClock();
			var http = new ComicVineHttp(handler, ApiKey, "ua", httpClock, () => httpClock.Elapsed);
			return new ComicVineCatalog(http, Config.ToSearchSettings());
		});
	}

	public ManualClock Clock { get; } = new();

	public DebugLog Log { get; } = new();

	public SettingsStore Store { get; }

	public ChosenSeries Chosen { get; }

	public CoverPreferences Covers { get; } = new();

	public ScriptedUi Ui { get; } = new();

	public Configuration Config { get; set; } = new() { Advanced = AdvancedSettings.Parse(null) };

	public string Folder => folder;

	/// <summary>The image served for a URL, or null for a 404.</summary>
	public Func<string, Bitmap?> ImageFor { get; set; } = _ => null;

	/// <summary>Requests (fixture keys, key redacted) in the order they were sent.</summary>
	public ConcurrentQueue<string> Requests { get; } = new();

	/// <summary>Called for each request before it is answered: throw to fail it, or cancel something.</summary>
	public Action<string>? BeforeResponse { get; set; }

	public List<StepTrace> Steps { get; } = [];

	public void Respond(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
		extra[FixtureFiles.Key(url)] = new RecordedResponse(url, status, body);

	/// <summary>Fails the next <paramref name="times"/> requests whose URL contains <paramref name="urlPart"/> with a network error.</summary>
	public void FailNetwork(string urlPart, int times = int.MaxValue)
	{
		int left = times;
		Action<string>? before = BeforeResponse;
		BeforeResponse = url =>
		{
			before?.Invoke(url);
			if (url.Contains(urlPart) && Interlocked.Decrement(ref left) >= 0)
			{
				throw new HttpRequestException("network down for " + urlPart);
			}
		};
	}

	public async Task<RunSummary> RunAsync(params IScrapeBook[] books) => await RunAsync(CancellationToken.None, books);

	/// <summary>Runs on a one-thread message loop, as ComicRack's UI thread does.</summary>
	public async Task<RunSummary> RunAsync(CancellationToken ct, params IScrapeBook[] books) =>
		await Task.Run(() => SingleThreadContext.Run(() => StartAsync(ct, books)));

	/// <summary>Calls ScrapeRun.RunAsync on the caller's thread and context, whatever they are.</summary>
	public async Task<RunSummary> StartAsync(CancellationToken ct, params IScrapeBook[] books)
	{
		var run = new ScrapeRun(new ScrapeServices(catalog.Value, Ui, Clock, Log, Chosen, Covers), Config);
		run.StepEntered += (facts, step) => Steps.Add(new StepTrace(Path.GetFileName(facts.Snapshot.FilePath), step));
		return await run.RunAsync(books, ct);
	}

	/// <summary>The steps of one book, by file name, in order; a second pass follows the first.</summary>
	public StepKind[] StepsOf(string fileName) => [.. Steps.Where(s => s.File == fileName).Select(s => s.Step)];

	/// <summary>Each API request as its path after /api/, with the filter when there is one: "issue/4000-1/", "issues/volume:796,issue_number:1".</summary>
	public string[] ApiPaths() => [.. Requests.Where(r => r.Contains("/api/")).Select(ApiPath)];

	private static string ApiPath(string url)
	{
		var uri = new Uri(url);
		string path = uri.AbsolutePath.Substring("/api/".Length);
		string? detail = uri.Query.TrimStart('?').Split('&')
			.FirstOrDefault(p => p.StartsWith("filter=", StringComparison.Ordinal) || p.StartsWith("query=", StringComparison.Ordinal));
		return detail is null ? path : path + detail.Substring(detail.IndexOf('=') + 1);
	}

	public IReadOnlyCollection<string> SeriesDat() => Store.ReadMap(SettingsStore.SeriesFileName).Keys.ToList();

	public void Dispose()
	{
		try
		{
			Directory.Delete(folder, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	internal sealed record StepTrace(string File, StepKind Step);

	private sealed class ServingHandler(EngineWorld world) : HttpMessageHandler
	{
		private readonly FixtureHandler fixtures = FixtureHandler.FromOutputFolder();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			// A real response completes on a pool thread, so the engine must find its own way back to the thread it runs on.
			await Task.Delay(1, cancellationToken).ConfigureAwait(false);
			string url = request.RequestUri!.AbsoluteUri;
			string key = FixtureFiles.Key(url);
			world.Requests.Enqueue(key);
			world.BeforeResponse?.Invoke(key);
			cancellationToken.ThrowIfCancellationRequested();
			if (!key.Contains("/api/") && world.ImageFor(url) is { } image)
			{
				using var png = new MemoryStream();
				image.Save(png, ImageFormat.Png);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png.ToArray()), RequestMessage = request };
			}
			if (!key.Contains("/api/"))
			{
				return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
			}
			RecordedResponse? response = world.extra.TryGetValue(key, out RecordedResponse? found) ? found : fixtures.Find(url);
			if (response is null)
			{
				throw new InvalidOperationException("No fixture for " + key);
			}
			return new HttpResponseMessage(response.Status)
			{
				Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(response.Body)),
				RequestMessage = request,
			};
		}
	}
}

/// <summary>Responses in Comic Vine's shape for the cases no recorded fixture covers.</summary>
internal static class Synthetic
{
	public const string Api = "https://comicvine.gamespot.com/api/";
	private const string Query = "?api_key=REDACTED&client=cvscraper&format=xml";

	public static string IssueListUrl(int series) => $"{Api}issues/{Query}&field_list=name,issue_number,id,image&filter=volume:{series}";

	public static string IssueNumberUrl(int series, string number) => $"{IssueListUrl(series)},issue_number:{number}";

	public static string IssueUrl(int issue) => $"{Api}issue/4000-{issue}/{Query}";

	public static string IssueList(params (int Id, string Number)[] issues) =>
		Response(issues.Length, string.Concat(issues.Select(i =>
			$"<issue><id>{i.Id}</id><image><small_url><![CDATA[https://comicvine.gamespot.com/a/uploads/scale_small/i{i.Id}.jpg]]></small_url><thumb_url><![CDATA[https://comicvine.gamespot.com/a/uploads/scale_avatar/i{i.Id}.jpg]]></thumb_url></image><issue_number>{i.Number}</issue_number><name><![CDATA[Issue {i.Number}]]></name></issue>")));

	public static string Issue(int id, int series, string number) =>
		Response(1, $"<associated_images/><cover_date><![CDATA[1993-07]]></cover_date><description/><id>{id}</id><image><medium_url><![CDATA[https://comicvine.gamespot.com/a/uploads/scale_medium/i{id}.jpg]]></medium_url><thumb_url><![CDATA[https://comicvine.gamespot.com/a/uploads/scale_avatar/i{id}.jpg]]></thumb_url></image><issue_number>{number}</issue_number><name><![CDATA[Title {id}]]></name><volume><id>{series}</id><name><![CDATA[Series {series}]]></name></volume>");

	private static string Response(int total, string results) =>
		"<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error><![CDATA[OK]]></error><limit>100</limit><offset>0</offset>"
		+ $"<number_of_page_results>{total}</number_of_page_results><number_of_total_results>{total}</number_of_total_results><status_code>1</status_code>"
		+ $"<results>{results}</results>" + "<version>1.0</version></response>";
}
