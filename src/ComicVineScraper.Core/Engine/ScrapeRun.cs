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
using ComicVineScraper.Matching;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.Engine;

public enum BookOutcome
{
	Scraped,
	Skipped,
	Delayed,
	Cancelled,
}

/// <summary>The steps a test can observe through <see cref="ScrapeRun.StepEntered"/>.</summary>
public enum StepKind
{
	Begin,
	Search,
	ChooseSeries,
	FindIssue,
	Write,
	SkipForever,
	Done,
}

/// <summary>Everything a run depends on. One per run; the Comic Vine caches live and die with it.</summary>
public sealed record ScrapeServices(
	IComicVine ComicVine,
	IScrapeUi Ui,
	IClock Clock,
	DebugLog Log,
	ChosenSeries Chosen,
	CoverPreferences Covers)
{
	/// <summary>Stands in for the AutoMatcher, so the engine oracle can say which books match without cover images.</summary>
	internal Func<BookFacts, IScrapeBook, CancellationToken, Task<SeriesRef?>>? AutoMatch { get; init; }
}

/// <summary>
/// A run that stopped on an error: an invalid key, a rate limit, a network failure outside a fast rescrape, or a bug.
/// Summary counts the books written before it; none is half-written. The cause is already in the debug log.
/// </summary>
public sealed class ScrapeFailedException(RunSummary summary, Exception cause)
	: Exception("The scrape stopped: " + cause.Message, cause)
{
	public RunSummary Summary { get; } = summary;
}

/// <summary>
/// scrapeengine.py's main loop and __scrape_book, as one state machine per book. Runs on the caller's
/// SynchronizationContext (ComicRack's UI thread in the plugin), so IScrapeBook and IScrapeUi need no marshalling.
/// </summary>
public sealed class ScrapeRun(ScrapeServices services, Configuration config)
{
	private readonly MergeSettings merge = config.ToMergeSettings();

	private readonly Func<BookFacts, IScrapeBook, CancellationToken, Task<SeriesRef?>> autoMatch =
		services.AutoMatch ?? new AutoMatcher(services.ComicVine, services.Chosen.Contains, services.Clock.Now.Year).FindAsync;

	// Python's scrape_cache: the series chosen for each unique series key, shared by every book of the run.
	private readonly Dictionary<string, ResolvedSeries> seriesCache = new(StringComparer.Ordinal);

	/// <summary>Raised as each book enters a step, so a test can assert the path a book took.</summary>
	public event Action<BookFacts, StepKind>? StepEntered;

	/// <summary>
	/// Scrapes the books in BookOrder. Returns when every book is done, or when the run is cancelled through ct or a
	/// Cancel answer. Any other failure ends the run with <see cref="ScrapeFailedException"/>.
	/// </summary>
	public async Task<RunSummary> RunAsync(IReadOnlyList<IScrapeBook> books, CancellationToken ct)
	{
		services.Log.Debug(config.ToString());
		List<QueueEntry> queue =
		[
			.. BookOrder.Sort(books.Select((book, index) => Facts(index, book.Read())), config.FastRescrape)
				.Select(facts => new QueueEntry(books[facts.Index], facts)),
		];
		int scraped = 0;
		try
		{
			for (int i = 0; i < queue.Count; i++)
			{
				bool secondPass = i >= books.Count;
				if (i > 0 && !secondPass)
				{
					await services.Clock.Delay(config.ScrapeDelay, ct);
				}
				QueueEntry entry = queue[i];
				services.Log.Debug("======> scraping next comic book: '" + Describe(entry.Facts) + "'");
				services.Ui.BookStarted(entry.Book, entry.Facts, queue.Count - i);
				BookPolicy policy = secondPass ? default : new BookPolicy(config.FastRescrape, config.Choose == ChooseMode.AutoChooseSeries);
				switch (await ScrapeBookAsync(entry, policy, ct))
				{
					case BookOutcome.Scraped:
						scraped++;
						break;
					case BookOutcome.Delayed when !secondPass:
						services.Log.Debug("we'll retry scraping this book again at the end.");
						queue.Add(entry);
						break;
					case BookOutcome.Cancelled:
						return Summary(cancelled: true);
				}
			}
			services.Log.Debug($"Scraper terminated normally (scraped {scraped}, skipped {books.Count - scraped}).");
			return Summary(cancelled: false);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			services.Log.Debug("Cancelled!");
			return Summary(cancelled: true);
		}
		catch (Exception e)
		{
			services.Log.Error(e);
			throw new ScrapeFailedException(Summary(cancelled: false), e);
		}

		RunSummary Summary(bool cancelled) => new(scraped, books.Count - scraped, cancelled);
	}

	private async Task<BookOutcome> ScrapeBookAsync(QueueEntry entry, BookPolicy policy, CancellationToken ct)
	{
		Step step = new Step.Begin();
		while (true)
		{
			StepEntered?.Invoke(entry.Facts, step.Kind);
			if (step is Step.Done done)
			{
				return done.Outcome;
			}
			ct.ThrowIfCancellationRequested();
			step = step switch
			{
				Step.Begin => await BeginAsync(entry, policy, ct),
				Step.Search search => await SearchAsync(entry, search, ct),
				Step.ChooseSeries choose => await ChooseSeriesAsync(entry, choose, ct),
				Step.FindIssue find when policy.Auto => await FindIssueAutomaticallyAsync(entry, find, ct),
				Step.FindIssue find => await AskForIssueAsync(entry, find, ct),
				Step.Write write => await WriteAsync(entry, write, ct),
				Step.SkipForever => SkipForever(entry),
				_ => throw new InvalidOperationException("Unknown step " + step),
			};
		}
	}

	// __scrape_book steps 1 to 3c: the skip marker, a fast rescrape, then the series from the cache, the stored series
	// key, a cvinfo file, or the auto-matcher.
	private async Task<Step> BeginAsync(QueueEntry entry, BookPolicy policy, CancellationToken ct)
	{
		BookFacts facts = entry.Facts;
		if (facts.Marker is StoredMarker.SkipForever)
		{
			services.Log.Debug("found SKIP tag, so skipping the scrape for this book.");
			return Done(BookOutcome.Skipped);
		}
		if (facts.Marker is StoredMarker.Issue known && policy.FastRescrape)
		{
			services.Log.Debug($"rescraping details in book identified its issue as: '{known.Id}'");
			return new Step.Write(known.Id, RecordChoice: null, FastRescrape: true);
		}

		string key = facts.UniqueSeriesKey;
		if (!seriesCache.ContainsKey(key) && facts.StoredSeries is { } stored && policy.FastRescrape)
		{
			services.Log.Debug($"rescraping details in book identified its series as: '{stored}'");
			// Python's sparse SeriesRef(series_key, None, -1, '', -1, None); no request is made.
			seriesCache[key] = new ResolvedSeries(new SeriesRef(stored, "Series " + stored, null, "", 0, null));
		}
		if (!seriesCache.ContainsKey(key) && ComicVineUrl.FindCvInfo(facts.Snapshot.FilePath) is { } target
			&& await services.ComicVine.ResolveSeriesAsync(target, ct) is { } magic)
		{
			services.Log.Debug($"a 'magic' file identified this book's series as: '{magic.Name}'");
			seriesCache[key] = new ResolvedSeries(magic);
		}
		if (!seriesCache.ContainsKey(key) && policy.Auto)
		{
			services.Log.Debug("trying to match this book automatically...");
			if (await autoMatch(facts, entry.Book, ct) is not { } auto)
			{
				services.Log.Debug("...couldn't find a match. leave it until the end.");
				return Done(BookOutcome.Delayed);
			}
			services.Log.Debug("...found a suitable match:  " + auto.Name);
			seriesCache[key] = new ResolvedSeries(auto);
		}
		return seriesCache.TryGetValue(key, out ResolvedSeries? series)
			? new Step.FindIssue(series, ForceDialog: config.ConfirmIssue, Terms: null, Found: null)
			: new Step.Search(Manual: false, FailedTerms: null);
	}

	// __scrape_book step 3d. No series found is Python's UNSCRAPED: the book starts again with a search the user must confirm.
	private async Task<Step> SearchAsync(QueueEntry entry, Step.Search step, CancellationToken ct)
	{
		string terms = entry.Facts.Effective.Series;
		if (step.Manual || terms.Length == 0)
		{
			services.Log.Debug("asking user for series search terms...");
			switch (await services.Ui.AskSearchTermsAsync(entry.Facts, terms, step.FailedTerms, ct))
			{
				case SearchChoice.Search { Terms: var typed } when typed.Trim().Length > 0:
					terms = typed.Trim();
					break;
				case SearchChoice.Skip:
					return Done(BookOutcome.Skipped);
				case SearchChoice.SkipForever:
					return new Step.SkipForever();
				default:
					// SearchForm answers CANCEL for blank terms.
					return Done(BookOutcome.Cancelled);
			}
		}

		services.Log.Debug($"searching for series that match '{terms}'...");
		IReadOnlyList<SeriesRef> found;
		using (IQueryProgress progress = services.Ui.BeginQuery(QueryKind.SeriesSearch))
		{
			found = await services.ComicVine.SearchSeriesAsync(terms, progress, ct);
		}
		if (found.Count == 0)
		{
			services.Log.Debug("...no results found for this search");
			return new Step.Search(Manual: true, FailedTerms: terms);
		}
		services.Log.Debug($"...found {found.Count} results");
		return new Step.ChooseSeries(terms, found);
	}

	private async Task<Step> ChooseSeriesAsync(QueueEntry entry, Step.ChooseSeries step, CancellationToken ct)
	{
		services.Log.Debug("displaying the series selection dialog...");
		IReadOnlyList<RankedSeries> ranked = MatchScore.Rank(entry.Facts.Effective, step.Found, services.Chosen.Contains, services.Clock.Now.Year);
		switch (await services.Ui.ChooseSeriesAsync(new SeriesQuestion(entry.Facts, step.Terms, ranked), services.Covers, ct))
		{
			case SeriesChoice.Pick pick:
				services.Log.Debug("   ...user chose " + (pick.ShowIssues ? "to show the issues of " : "") + pick.Series.Name);
				var series = new ResolvedSeries(pick.Series);
				seriesCache[entry.Facts.UniqueSeriesKey] = series;
				// A series the user picked ignores ConfirmIssue: only "Show Issues" forces the issue dialog.
				return new Step.FindIssue(series, ForceDialog: pick.ShowIssues, step.Terms, step.Found);
			case SeriesChoice.SearchAgain:
				return new Step.Search(Manual: true, FailedTerms: null);
			case SeriesChoice.Skip:
				return Done(BookOutcome.Skipped);
			case SeriesChoice.SkipForever:
				return new Step.SkipForever();
			default:
				return Done(BookOutcome.Cancelled);
		}
	}

	// __scrape_book step 5a: an auto-chosen series must yield its issue without asking, or the book waits for the second pass.
	private async Task<Step> FindIssueAutomaticallyAsync(QueueEntry entry, Step.FindIssue step, CancellationToken ct)
	{
		SeriesRef series = step.Series.Ref;
		string number = entry.Facts.Effective.Number;
		IssueRef? issue = null;
		if (number.Length > 0)
		{
			issue = await services.ComicVine.FindIssueAsync(series.Id, number, ct);
		}
		else if (series.IssueCount <= 1)
		{
			// Python asks for the list without keeping it in the series' issue list.
			IReadOnlyList<IssueRef> issues = await LoadIssuesAsync(series, ct);
			issue = issues.Count == 1 ? issues[0] : null;
		}
		if (issue is null)
		{
			services.Log.Debug("couldn't find issue number.  leaving until the end.");
			seriesCache.Remove(entry.Facts.UniqueSeriesKey);
			return Done(BookOutcome.Delayed);
		}
		services.Log.Debug("   ...identified issue number " + number);
		return new Step.Write(issue.Id, RecordChoice: series, FastRescrape: false);
	}

	// __scrape_book step 5b.
	private async Task<Step> AskForIssueAsync(QueueEntry entry, Step.FindIssue step, CancellationToken ct)
	{
		IssueChoice choice = await ChooseIssueAsync(entry.Facts, step.Series, step.ForceDialog, ct);
		switch (choice)
		{
			case IssueChoice.Pick pick:
				return new Step.Write(pick.Issue.Id, RecordChoice: step.Series.Ref, FastRescrape: false);
			case IssueChoice.Back:
				seriesCache.Remove(entry.Facts.UniqueSeriesKey);
				return step.Found is { } found && step.Terms is { } terms
					? new Step.ChooseSeries(terms, found)
					: new Step.Search(Manual: true, FailedTerms: null);
			case IssueChoice.Skip or IssueChoice.SkipForever when step.ForceDialog && !config.ConfirmIssue:
				// The user clicked "Show Issues" and then skipped, so the series choice does not stand.
				seriesCache.Remove(entry.Facts.UniqueSeriesKey);
				return SkipChoice();
			case IssueChoice.Skip or IssueChoice.SkipForever:
				return SkipChoice();
			default:
				return Done(BookOutcome.Cancelled);
		}

		Step SkipChoice() => choice is IssueChoice.SkipForever ? new Step.SkipForever() : Done(BookOutcome.Skipped);
	}

	/// <summary>__choose_issue_ref: find the issue without asking when the book's number allows it, and ask otherwise or when forced.</summary>
	private async Task<IssueChoice> ChooseIssueAsync(BookFacts facts, ResolvedSeries series, bool force, CancellationToken ct)
	{
		string number = facts.Effective.Number;
		IssueRef? found = null;
		if (series.Issues is null && number.Length > 0 && !force)
		{
			found = await services.ComicVine.FindIssueAsync(series.Ref.Id, number, ct);
		}
		if (series.Issues is null && (found is null || force))
		{
			series.Issues = await LoadIssuesAsync(series.Ref, ct);
			if (series.Issues.Count == 0)
			{
				services.Log.Debug("   ...no issues in this series; user must go back");
				services.Ui.ShowNoIssues(series.Ref);
				return new IssueChoice.Back();
			}
		}
		IReadOnlyList<IssueRef> issues = series.Issues ?? [];
		found ??= IssueMatch.Find(issues, number);
		if (found is not null && !force)
		{
			services.Log.Debug("   ...identified issue number " + number);
			return new IssueChoice.Pick(found);
		}
		services.Log.Debug("displaying the issue selection dialog...");
		IReadOnlyList<IssueRef> ordered = [.. issues.OrderBy(issue => issue.IssueNumber, NaturalOrder.Instance)];
		return await services.Ui.ChooseIssueAsync(new IssueQuestion(facts, series.Ref, ordered, found), services.Covers, ct);
	}

	// Every network read happens before the book is touched. Re-reading, planning, and applying are one synchronous
	// stretch, so a cancel or a failed request can never leave a book half-written.
	private async Task<Step> WriteAsync(QueueEntry entry, Step.Write step, CancellationToken ct)
	{
		Issue issue;
		Uri? cover;
		Bitmap? thumbnail;
		try
		{
			services.Log.Debug("querying comicvine for issue details...");
			issue = await services.ComicVine.GetIssueAsync(step.Issue, config.ScrapeRating, ct);
			cover = services.Covers.Preferred(issue.Id) ?? issue.CoverUrls.FirstOrDefault();
			Uri? thumbnailUrl = MergeRules.PlanThumbnail(entry.Book.Read(), cover, merge);
			thumbnail = thumbnailUrl is null ? null : await services.ComicVine.GetImageAsync(thumbnailUrl, ct);
			if (thumbnailUrl is not null && thumbnail is null)
			{
				services.Log.Debug("ERROR: can't download thumbnail: " + thumbnailUrl);
			}
		}
		catch (ComicVineException e) when (step.FastRescrape && e.IsRecoverable)
		{
			services.Log.Debug("Error rescraping details:");
			services.Log.Error(e);
			return Done(BookOutcome.Delayed);
		}

		using (thumbnail)
		{
			ct.ThrowIfCancellationRequested();
			BookFacts current = Facts(entry.Facts.Index, entry.Book.Read());
			Bitmap? install = MergeRules.PlanThumbnail(current.Snapshot, cover, merge) is null ? null : thumbnail;
			entry.Book.Apply(MergeRules.Plan(current, issue, merge, services.Clock.Now), install);
		}
		if (step.RecordChoice is { } chosen)
		{
			// MatchScore.record_choice runs after every scrape that went through a series, however it was found.
			services.Chosen.Record(chosen.Id);
		}
		return Done(BookOutcome.Scraped);
	}

	private Step SkipForever(QueueEntry entry)
	{
		BookFacts current = Facts(entry.Facts.Index, entry.Book.Read());
		entry.Book.Apply(MergeRules.PlanSkipForever(current, merge, services.Clock.Now), null);
		services.Log.Debug("Added the CVDBSKIP flag to this comic book.");
		return Done(BookOutcome.Skipped);
	}

	private async Task<IReadOnlyList<IssueRef>> LoadIssuesAsync(SeriesRef series, CancellationToken ct)
	{
		services.Log.Debug($"finding all issues for '{series.Name}'...");
		using IQueryProgress progress = services.Ui.BeginQuery(QueryKind.IssueList);
		IReadOnlyList<IssueRef> issues = await services.ComicVine.GetIssuesAsync(series.Id, progress, ct);
		services.Log.Debug($"   ...found {issues.Count} issues at comicvine.gamespot.com");
		return issues;
	}

	private BookFacts Facts(int index, BookSnapshot snapshot) => BookFacts.From(index, snapshot, ParseFileName, config.IgnoreFolders);

	private (string Series, string Number, string Year) ParseFileName(string fileName)
	{
		ParsedName parsed = FileNameParser.Parse(fileName, config.Advanced.AltSearchRegex, services.Log);
		return (parsed.Series, parsed.Number, parsed.Year);
	}

	private static string Describe(BookFacts facts) =>
		facts.Snapshot.FilePath.Length == 0
			? $"FILELESS (\"{facts.Effective.Series} #{facts.Effective.Number}\")"
			: Path.GetFileName(facts.Snapshot.FilePath);

	private static Step.Done Done(BookOutcome outcome) => new(outcome);

	/// <summary>Per-book switches. Both are off for a delayed book's second pass.</summary>
	private readonly record struct BookPolicy(bool FastRescrape, bool Auto);

	private sealed record QueueEntry(IScrapeBook Book, BookFacts Facts);

	/// <summary>One book's place in the flow. Each loop and early return of __scrape_book is an edge between these.</summary>
	private abstract record Step
	{
		public StepKind Kind => this switch
		{
			Begin => StepKind.Begin,
			Search => StepKind.Search,
			ChooseSeries => StepKind.ChooseSeries,
			FindIssue => StepKind.FindIssue,
			Write => StepKind.Write,
			SkipForever => StepKind.SkipForever,
			_ => StepKind.Done,
		};

		public sealed record Begin : Step;

		/// <summary>Asks for terms first when Manual or the book has no series name. FailedTerms is the search that found nothing.</summary>
		public sealed record Search(bool Manual, string? FailedTerms) : Step;

		public sealed record ChooseSeries(string Terms, IReadOnlyList<SeriesRef> Found) : Step;

		/// <summary>The series is known. Terms and Found are the search it came from, or null when it came from elsewhere, so Back has no list to return to.</summary>
		public sealed record FindIssue(ResolvedSeries Series, bool ForceDialog, string? Terms, IReadOnlyList<SeriesRef>? Found) : Step;

		/// <summary>RecordChoice is null for a fast rescrape, which Python does not record in series.dat. A fast rescrape that fails recoverably delays the book.</summary>
		public sealed record Write(IssueId Issue, SeriesRef? RecordChoice, bool FastRescrape) : Step;

		public sealed record SkipForever : Step;

		public sealed record Done(BookOutcome Outcome) : Step;
	}
}

/// <summary>A series chosen for a unique series key in this run, with its issue list once the issue dialog needed it.</summary>
public sealed class ResolvedSeries(SeriesRef series)
{
	public SeriesRef Ref { get; } = series;

	/// <summary>Loaded at most once per run; null until then.</summary>
	public IReadOnlyList<IssueRef>? Issues { get; set; }
}
