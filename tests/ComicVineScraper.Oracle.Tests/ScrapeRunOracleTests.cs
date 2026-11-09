using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>
/// ScrapeRun against the original __scrape loop and __scrape_book on random scenarios: the same books, the same Comic Vine
/// tables, and the same scripted answers. Both sides report the same events (books started, waits, the questions with
/// their candidates, the writes) and the summary, so any difference in which books get written, and with which issue,
/// shows up as a different trace.
/// </summary>
public sealed class ScrapeRunOracleTests
{
	private static readonly string[] Names = ["alpha", "alpha", "beta", "gamma", "delta", ""];
	private static readonly string[] Terms = ["alpha", "beta", "gamma", "delta", "omega"];
	// No two numbers with the same natural key but different text (deviations.md #32).
	private static readonly string[] IssueNumbers = ["1", "1", "2", "3", "7", ""];
	private static readonly string[] BookNumbers = ["1", "2", "3", "7", "9", "", ""];

	[Test]
	public async Task RunAsync_matches_scrape_on_random_scenarios()
	{
		var random = new Random(97);
		List<string> differences = [];
		int skipped = 0, runs = 0, writes = 0, delayed = 0, errors = 0, cancels = 0, questions = 0;
		string root = Path.Combine(Path.GetTempPath(), "cvs-engine-oracle-" + Guid.NewGuid().ToString("N"));
		try
		{
			for (int i = 0; i < 1000; i++)
			{
				var scenario = Scenario.Random(random, Path.Combine(root, i.ToString()));
				if (HasTwoNamelessBooks(scenario.Books))
				{
					skipped++;
					continue;
				}
				IReadOnlyList<string> python = Python(scenario);
				IReadOnlyList<string> csharp = await CSharp(scenario);
				runs++;
				writes += python.Count(e => e.StartsWith("write", StringComparison.Ordinal));
				delayed += python.Count(e => e.StartsWith("start", StringComparison.Ordinal)) > scenario.Books.Length ? 1 : 0;
				errors += python.Contains("error") ? 1 : 0;
				cancels += python.Any(e => e.EndsWith("True", StringComparison.Ordinal)) ? 1 : 0;
				questions += python.Count(e => e.StartsWith("search", StringComparison.Ordinal) || e.StartsWith("series", StringComparison.Ordinal) || e.StartsWith("issue", StringComparison.Ordinal));
				if (!python.SequenceEqual(csharp))
				{
					int at = python.Zip(csharp, (p, c) => p == c).TakeWhile(same => same).Count();
					differences.Add($"#{i} {scenario.Describe()}\n  python: {string.Join(" / ", python.Skip(Math.Max(0, at - 3)))}\n  C#:     {string.Join(" / ", csharp.Skip(Math.Max(0, at - 3)))}");
				}
			}
		}
		finally
		{
			try
			{
				Directory.Delete(root, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		await Assert.That(differences.Take(5)).IsEmpty();
		await Assert.That(skipped).IsLessThan(20);
		await Assert.That(runs + skipped).IsEqualTo(1000);
		await Assert.That(writes).IsGreaterThan(600);
		await Assert.That(delayed).IsGreaterThan(180);
		await Assert.That(errors).IsGreaterThan(30);
		await Assert.That(cancels).IsGreaterThan(400);
		await Assert.That(questions).IsGreaterThan(1800);
	}

	// A nameless book's key holds Python's id(self) where the port holds its position, so the order of two of them is undefined in Python.
	private static bool HasTwoNamelessBooks(ComicBook[] books) =>
		books.Select((book, index) => BookFacts.From(index, new ComicRackBook(book, null!).Read(), BookCorpus.ParseFileName, ignoreFolders: false))
			.Count(facts => facts.UniqueSeriesKey.StartsWith("uniqueid-", StringComparison.Ordinal)) > 1;

	[Test]
	public async Task Two_books_with_no_series_and_no_file_are_the_undefined_order_case()
	{
		ComicBook nameless() => new() { Number = "1" };

		await Assert.That(HasTwoNamelessBooks([nameless(), nameless()])).IsTrue();
		await Assert.That(HasTwoNamelessBooks([nameless(), new ComicBook { Series = "alpha" }])).IsFalse();
		await Assert.That(HasTwoNamelessBooks([nameless(), new ComicBook { FilePath = Path.Combine(Path.GetTempPath(), "alpha 1.cbz") }])).IsFalse();
	}

	private static IReadOnlyList<string> Python(Scenario scenario)
	{
		var result = (object[])Oracle.Call("cvs_engine_oracle", "run_scrape", scenario.Books.Select(b => new ComicBook(b)).ToArray(), scenario.ToPython())!;
		return [.. result.Cast<string>()];
	}

	private static async Task<IReadOnlyList<string>> CSharp(Scenario scenario)
	{
		List<string> events = [];
		string store = Path.Combine(scenario.Folder, "store");
		Directory.CreateDirectory(store);
		var clock = new RecordingClock(events);
		var settings = new SettingsStore(store, clock);
		var config = new Configuration
		{
			Advanced = AdvancedSettings.Parse(null),
			FastRescrape = scenario.FastRescrape,
			Choose = scenario.Choose,
		};
		var services = new ScrapeServices(new ScenarioComicVine(scenario), new ScriptedUi(scenario, events), clock, new DebugLog(), settings.LoadChosenSeries(), new CoverPreferences())
		{
			AutoMatch = (facts, _, _) => Task.FromResult(scenario.AutoMatch.TryGetValue(facts.Index, out int key) ? scenario.SeriesRef(key) : null),
		};
		IScrapeBook[] books = [.. scenario.Books.Select((book, i) => new RecordingBook(new ComicBook(book), i, events))];
		RunSummary summary;
		try
		{
			summary = await Task.Run(() => SingleThreadContext.Run(() => new ScrapeRun(services, config).RunAsync(books, CancellationToken.None)));
		}
		catch (ScrapeFailedException e)
		{
			events.Add("error");
			summary = e.Summary;
		}
		events.Add($"end {summary.Scraped} {summary.Skipped} {(summary.Cancelled ? "True" : "False")}");
		events.Add("recorded " + string.Join(" ", settings.ReadMap(SettingsStore.SeriesFileName).Keys.OrderBy(k => k, StringComparer.Ordinal)));
		return events;
	}

	private sealed record Scenario(
		string Folder,
		ComicBook[] Books,
		bool FastRescrape,
		ChooseMode Choose,
		Dictionary<int, (string Name, int Year, int Count)> Series,
		Dictionary<int, (int Key, string Number)[]> Issues,
		Dictionary<string, int[]> Search,
		Dictionary<string, int> Magic,
		Dictionary<int, int> AutoMatch,
		Dictionary<int, int> Fail,
		string[] SearchAnswers,
		string[] SeriesAnswers,
		string[] IssueAnswers)
	{
		public static Scenario Random(Random random, string folder)
		{
			string[] folders = [.. Enumerable.Range(0, 3).Select(k => Path.Combine(folder, "f" + k))];
			foreach (string f in folders)
			{
				Directory.CreateDirectory(f);
			}
			int[] keys = [11, 12, 13, 14, 15, 16];
			Dictionary<int, (string, int, int)> series = keys.ToDictionary(k => k, k => ("Series " + k, 1990 + k, Pick(random, new[] { 0, 1, 1, 2, 5, 200 })));
			Dictionary<int, (int, string)[]> issues = keys.ToDictionary(k => k, k =>
				Enumerable.Range(0, Pick(random, new[] { 0, 1, 1, 2, 3, 4 })).Select(j => (k * 100 + j, Pick(random, IssueNumbers))).ToArray());
			Dictionary<string, int[]> search = new()
			{
				["alpha"] = [11, 12],
				["beta"] = [13],
				["gamma"] = [14, 15, 16],
				["delta"] = Pick(random, new[] { Array.Empty<int>(), new[] { 12, 16 } }),
			};
			Dictionary<string, int> magic = [];
			if (random.Next(3) == 0)
			{
				magic[folders[0]] = Pick(random, keys);
				File.WriteAllText(Path.Combine(folders[0], "cvinfo.txt"), $"https://comicvine.gamespot.com/x/4050-{magic[folders[0]]}/");
			}
			int[] allIssues = [.. issues.Values.SelectMany(list => list.Select(i => i.Item1))];
			ComicBook[] books = [.. Enumerable.Range(0, random.Next(1, 6)).Select(i => Book(random, i, folders, keys, allIssues))];
			Dictionary<int, int> auto = [];
			Dictionary<int, int> fail = [];
			for (int i = 0; i < books.Length; i++)
			{
				if (random.Next(2) == 0)
				{
					auto[i] = Pick(random, keys);
				}
			}
			foreach (int key in allIssues.Where(_ => random.Next(6) == 0))
			{
				fail[key] = Pick(random, new[] { 1, 99 });
			}
			return new Scenario(folder, books, random.Next(3) > 0, Pick(random, new[] { ChooseMode.Ask, ChooseMode.Ask, ChooseMode.AutoChooseSeries, ChooseMode.ConfirmIssue }),
				series, issues, search, magic, auto, fail,
				Answers(random, () => Pick(random, new[] { "SEARCH:" + Pick(random, Terms), "SEARCH:" + Pick(random, Terms), "SKIP", "PERMSKIP", "CANCEL" })),
				Answers(random, () => Pick(random, new[] { "OK:" + random.Next(4), "OK:" + random.Next(4), "SHOW:" + random.Next(4), "SEARCH", "SKIP", "PERMSKIP", "CANCEL" })),
				Answers(random, () => Pick(random, new[] { "OK:" + random.Next(5), "OK:" + random.Next(5), "BACK", "NONE", "SKIP", "PERMSKIP", "CANCEL" })));
		}

		public SeriesRef? SeriesRef(int key) => Series.TryGetValue(key, out var s) ? new SeriesRef(new SeriesId(key), s.Name, s.Year, "", s.Count, null) : null;

		public IssueRef IssueRef(int key, string number) => new(new IssueId(key), number, "", null);

		/// <summary>The issue a number filter finds: the only issue whose number is exactly that text.</summary>
		public int? Find(int series, string number)
		{
			(int Key, string Number)[] matches = Issues.TryGetValue(series, out var list) ? [.. list.Where(i => i.Number == number)] : [];
			return matches.Length == 1 ? matches[0].Key : null;
		}

		public Dictionary<string, object> ToPython() => new()
		{
			["fast"] = FastRescrape,
			["auto"] = Choose == ChooseMode.AutoChooseSeries,
			["confirm"] = Choose == ChooseMode.ConfirmIssue,
			["series"] = Series.ToDictionary(p => p.Key.ToString(), p => new object[] { p.Value.Name, p.Value.Year, p.Value.Count }),
			["issues"] = Issues.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(i => new object[] { i.Key, i.Number }).ToArray()),
			["find"] = Issues.SelectMany(p => IssueNumbers.Concat(BookNumbers).Distinct().Select(n => (Key: $"{p.Key}|{n}", Found: Find(p.Key, n))))
				.Where(f => f.Found is not null).ToDictionary(f => f.Key, f => (object)f.Found!.Value),
			["search"] = Search.ToDictionary(p => p.Key, p => p.Value),
			["magic"] = Magic.ToDictionary(p => p.Key, p => (object)p.Value),
			["automatch"] = AutoMatch.ToDictionary(p => p.Key.ToString(), p => (object)p.Value),
			["fail"] = Fail.ToDictionary(p => p.Key.ToString(), p => (object)p.Value),
			["answers_search"] = SearchAnswers,
			["answers_series"] = SeriesAnswers,
			["answers_issue"] = IssueAnswers,
		};

		public string Describe() =>
			$"fast={FastRescrape} {Choose} books=[{string.Join("; ", Books.Select(b => $"{b.Series}#{b.Number} {(b.FilePath.Length == 0 ? "fileless" : Path.GetFileName(Path.GetDirectoryName(b.FilePath)))} {b.Tags} v{b.GetCustomValue("comicvine_volume")}"))}] "
			+ $"issues=[{string.Join(" ", Issues.Select(p => p.Key + ":" + string.Join(",", p.Value.Select(i => i.Item2))))}] auto=[{string.Join(",", AutoMatch.Select(p => p.Key + ">" + p.Value))}] "
			+ $"fail=[{string.Join(",", Fail.Select(p => p.Key + "x" + p.Value))}] magic={Magic.Values.FirstOrDefault()} "
			+ $"answers={string.Join(",", SearchAnswers)} | {string.Join(",", SeriesAnswers)} | {string.Join(",", IssueAnswers)}";

		private static ComicBook Book(Random random, int i, string[] folders, int[] keys, int[] issues)
		{
			var book = new ComicBook
			{
				Series = Pick(random, Names),
				Number = Pick(random, BookNumbers),
				FilePath = random.Next(6) == 0 ? "" : Path.Combine(Pick(random, folders), $"book {i}.cbz"),
			};
			switch (random.Next(8))
			{
				case 0:
					book.Tags = "CVDBSKIP";
					break;
				case 1 when issues.Length > 0:
					book.Tags = "CVDB" + Pick(random, issues);
					break;
				case 2:
					book.Tags = "CVDB" + (900 + i);
					break;
				case 3:
					book.SetCustomValue("comicvine_volume", Pick(random, keys).ToString());
					break;
			}
			return book;
		}

		private static string[] Answers(Random random, Func<string> next) => [.. Enumerable.Range(0, random.Next(0, 9)).Select(_ => next())];

		private static T Pick<T>(Random random, T[] items) => items[random.Next(items.Length)];
	}

	private sealed class ScenarioComicVine(Scenario scenario) : IComicVine
	{
		private readonly Dictionary<int, int> fail = new(scenario.Fail);

		public Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct) =>
			Task.FromResult<IReadOnlyList<SeriesRef>>(scenario.Search.TryGetValue(terms, out int[]? keys) ? [.. keys.Select(k => scenario.SeriesRef(k)!)] : []);

		public Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct) =>
			Task.FromResult<IReadOnlyList<IssueRef>>(scenario.Issues.TryGetValue(series.Value, out var list) ? [.. list.Select(i => scenario.IssueRef(i.Key, i.Number))] : []);

		public Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct) =>
			Task.FromResult(scenario.Find(series.Value, issueNumber) is { } key ? scenario.IssueRef(key, issueNumber) : null);

		public Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct)
		{
			if (fail.TryGetValue(issue.Value, out int left) && left > 0)
			{
				fail[issue.Value] = left - 1;
				throw new ComicVineException(ComicVineError.Network, "https://comicvine.gamespot.com/api/issue/4000-" + issue + "/", null, null);
			}
			return Task.FromResult(new Issue(issue, new SeriesId(1), "Series", null, "1", "", "", "", "", default, default,
				[], [], [], [], [], [], [], [], [], [], [], "", null, []));
		}

		public Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct) =>
			Task.FromResult(target is ComicVineTarget.OfSeries s ? scenario.SeriesRef(s.Id.Value) : null);

		public Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct) => Task.FromResult<Bitmap?>(null);
	}

	private sealed class ScriptedUi(Scenario scenario, List<string> events) : IScrapeUi
	{
		private readonly Queue<string> search = new(scenario.SearchAnswers);
		private readonly Queue<string> series = new(scenario.SeriesAnswers);
		private readonly Queue<string> issue = new(scenario.IssueAnswers);

		public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) => events.Add($"start {facts.Index} {remaining}");

		public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct)
		{
			events.Add($"search {initialTerms}|{failedTerms}");
			string answer = Next(search);
			return Task.FromResult<SearchChoice>(answer switch
			{
				_ when answer.StartsWith("SEARCH:", StringComparison.Ordinal) => new SearchChoice.Search(answer.Substring(7)),
				"SKIP" => new SearchChoice.Skip(),
				"PERMSKIP" => new SearchChoice.SkipForever(),
				_ => new SearchChoice.Cancel(),
			});
		}

		public Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct)
		{
			SeriesRef[] refs = [.. question.Candidates.Select(c => c.Series).OrderBy(s => s.Id.Value)];
			events.Add($"series {question.SearchTerms} [{string.Join(" ", refs.Select(r => r.Id))}]");
			string answer = Next(series);
			string[] parts = answer.Split(':');
			return Task.FromResult<SeriesChoice>(parts[0] switch
			{
				"OK" or "SHOW" => new SeriesChoice.Pick(refs[int.Parse(parts[1]) % refs.Length], ShowIssues: parts[0] == "SHOW"),
				"SEARCH" => new SeriesChoice.SearchAgain(),
				"SKIP" => new SeriesChoice.Skip(),
				"PERMSKIP" => new SeriesChoice.SkipForever(),
				_ => new SeriesChoice.Cancel(),
			});
		}

		public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct)
		{
			IssueRef[] refs = [.. question.Issues.OrderBy(i => i.Id.Value)];
			events.Add($"issue {question.Series.Id} {question.Hint?.Id.ToString() ?? "-"} [{string.Join(" ", refs.Select(r => r.Id))}]");
			string answer = Next(issue);
			string[] parts = answer.Split(':');
			return Task.FromResult<IssueChoice>(parts[0] switch
			{
				"OK" => new IssueChoice.Pick(refs[int.Parse(parts[1]) % refs.Length]),
				"BACK" or "NONE" => new IssueChoice.Back(),
				"SKIP" => new IssueChoice.Skip(),
				"PERMSKIP" => new IssueChoice.SkipForever(),
				_ => new IssueChoice.Cancel(),
			});
		}

		public void ShowNoIssues(SeriesRef series, CancellationToken ct) => events.Add("noissues");

		public IQueryProgress BeginQuery(QueryKind kind) => new NoProgress();

		private static string Next(Queue<string> queue) => queue.Count > 0 ? queue.Dequeue() : "CANCEL";

		private sealed class NoProgress : IQueryProgress
		{
			public void Report(QueryProgress value)
			{
			}

			public void Dispose()
			{
			}
		}
	}

	private sealed class RecordingBook(ComicBook book, int position, List<string> events) : IScrapeBook
	{
		private readonly ComicRackBook adapter = new(book, null!);

		public BookSnapshot Read() => adapter.Read();

		public Bitmap? GetPage(int index) => null;

		public bool Apply(BookPatch patch, Bitmap? thumbnail)
		{
			bool skip = patch.Values.Notes.Contains("CVDBSKIP") || patch.Values.Tags.Any(t => t.Contains("CVDBSKIP"));
			events.Add(skip ? $"skipforever {position}" : $"write {position} {patch.Values.IssueKey}");
			return adapter.Apply(patch, thumbnail);
		}
	}

	private sealed class RecordingClock(List<string> events) : IClock
	{
		public DateTime Now { get; } = new(2026, 11, 9, 12, 0, 0);

		public Task Delay(TimeSpan delay, CancellationToken ct)
		{
			events.Add("wait");
			return Task.CompletedTask;
		}
	}
}
