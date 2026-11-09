// ComicApiPlugin.Core: the scrape loop. Ports scrapeengine.py as an explicit per-book state machine.
// Runs on whatever SynchronizationContext calls RunAsync. In the plugin that is ComicRack's UI thread,
// so IScrapeBook and IScrapeUi calls need no marshalling. Comic Vine I/O awaits; nothing blocks.

namespace ComicApiPlugin.Engine;

public enum BookOutcome { Scraped, Skipped, Delayed, Cancelled }

public sealed record RunSummary(int Scraped, int Skipped, bool Cancelled);

/// <summary>Everything a run depends on. One per run; Comic Vine caches live and die with it.</summary>
public sealed record ScrapeServices(
    IComicVine ComicVine,       // a ComicVineCatalog, created per run over the plugin-lifetime ComicVineHttp
    IScrapeUi Ui,
    IClock Clock,
    DebugLog Log,
    ChosenSeries Chosen,        // series.dat; MatchScore's +7 and record_choice
    FileNameParser Parser,      // carries ALT_SEARCH_REGEX and its failed-pattern memory
    CoverPreferences Covers);   // written by the cover panels, read when the write is planned

/// <summary>The steps a test can observe through <see cref="ScrapeRun.StepEntered"/>.</summary>
public enum StepKind { Begin, Search, ChooseSeries, FindIssue, Write, SkipForever, Done }

public sealed class ScrapeRun(ScrapeServices services, Configuration config)
{
    /// <summary>Raised as each book enters a step, so tests assert the path without reading private state.</summary>
    public event Action<BookFacts, StepKind>? StepEntered;

    /// <summary>
    /// Scrapes the books in Python's sort order. Returns when every book is done, or on cancellation
    /// (ct, or a Cancel choice). Comic Vine errors other than per-book recoverable ones propagate to the
    /// caller, which shows them; books already written stay written, and no book is ever half-written.
    /// </summary>
    public Task<RunSummary> RunAsync(IReadOnlyList<IScrapeBook> books, CancellationToken ct)
        => throw new NotImplementedException();
    // TODO pseudocode:
    //   using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)   // a Cancel choice cancels cts
    //   facts  = books.Select((b, i) => BookFacts.From(i, b.Read(), Parser, config.IgnoreFolders))
    //   queue  = BookOrder.Sort(facts)          // skip/known-issue first, then UniqueSeriesKey (culture compare), then padded number
    //   seriesCache = new Dictionary<string, ResolvedSeries>()   // the Python scrape_cache, keyed by UniqueSeriesKey
    //   for i in 0..queue.Count (queue may grow):
    //       delayed = i >= originalCount
    //       if i > 0 && !delayed: await Clock.Delay(config.ScrapeDelay, cts.Token)
    //       Ui.BookStarted(book, facts, queue.Count - i)
    //       outcome = await ScrapeBookAsync(entry, delayed ? default : new BookPolicy(config.FastRescrape, config.Choose == ChooseMode.AutoChooseSeries), cts.Token)
    //       Delayed && !delayed -> queue.Add(entry);  Scraped -> scraped++;  Cancelled -> break
    //   OperationCanceledException from cts -> summary with Cancelled = true

    /// <summary>Per-book switches. Both are off for a delayed book's second pass. Auto is ChooseMode.AutoChooseSeries.</summary>
    private readonly record struct BookPolicy(bool FastRescrape, bool Auto);

    /// <summary>One book's position in the flow. Each loop in Python becomes a transition here.</summary>
    private abstract record Step
    {
        /// <summary>Skip marker, fast rescrape, then series lookup: cache, stored series, cvinfo, auto-match.</summary>
        public sealed record Begin : Step;
        /// <summary>Search Comic Vine; ask for terms first when Manual or the book has no series name.</summary>
        public sealed record Search(bool Manual, string? FailedTerms) : Step;
        public sealed record ChooseSeries(string Terms, IReadOnlyList<SeriesRef> Found) : Step;
        /// <summary>Series known. Find the issue automatically or ask. Found is null when the series came from the cache.</summary>
        public sealed record FindIssue(ResolvedSeries Series, bool ForceDialog, string? Terms, IReadOnlyList<SeriesRef>? Found) : Step;
        /// <summary>Issue known. RecordChoice is null for a fast rescrape, which Python does not record in series.dat.</summary>
        public sealed record Write(IssueId Issue, SeriesId? RecordChoice) : Step;
        public sealed record SkipForever : Step;
        public sealed record Done(BookOutcome Outcome) : Step;
    }

    private Task<BookOutcome> ScrapeBookAsync(QueueEntry entry, BookPolicy policy, CancellationToken ct)
        => throw new NotImplementedException();
    // TODO: Step step = new Begin(); while (step is not Done d) step = await NextAsync(entry, step, policy, ct); return d.Outcome;

    /// <summary>The transition function. Every edge below is one engine test with a ScriptedUi.</summary>
    private Task<Step> NextAsync(QueueEntry entry, Step step, BookPolicy policy, CancellationToken ct)
        => throw new NotImplementedException();
    // TODO pseudocode, per state:
    //   Begin:
    //     Marker is SkipForever                          -> Done(Skipped)
    //     Marker is Issue(id) && policy.FastRescrape    -> Write(id, null); Write's GetIssue failure maps to:
    //                                                       catch ComicVineException { IsRecoverable } -> Done(Delayed)
    //                                                       (InvalidApiKey and RateLimited propagate: deviations #4, #5)
    //     seriesCache has key                            -> FindIssue(cached, config.ConfirmIssue, null, null)
    //     StoredSeries && policy.FastRescrape           -> cache it -> FindIssue
    //     CvInfo.Find(FilePath) resolves                 -> cache it -> FindIssue
    //     policy.Auto: AutoMatcher.FindAsync            -> found ? cache -> FindIssue : Done(Delayed)
    //     else                                           -> Search(Manual: false, null)
    //   Search(manual, failed):
    //     terms = Effective.Series
    //     if manual || terms == "": switch await Ui.AskSearchTermsAsync(...)
    //         Search s -> terms = s.Terms; Skip -> Done(Skipped); SkipForever -> SkipForever; Cancel -> cancel run, Done(Cancelled)
    //     found = await ComicVine.SearchSeriesAsync(terms, progress, ct)   // filtered by advanced settings
    //     found empty -> Search(Manual: true, FailedTerms: terms)          // Python's UNSCRAPED + forced manual search
    //     else        -> ChooseSeries(terms, found)
    //   ChooseSeries(terms, found):
    //     ranked = MatchScore.Rank(facts, found, Chosen, Clock.Now)          // stable sort, best first
    //     Pick p -> cache it; FindIssue(series, ForceDialog: p.ShowIssues, terms, found)
    //     SearchAgain -> Search(Manual: true, null); Skip/SkipForever/Cancel as above
    //   FindIssue(series, force, terms, found):
    //     policy.Auto: number == "" ? (IssueCount <= 1 && issues.Count == 1 ? that : null) : FindIssueAsync(series, number)
    //                   null -> uncache; Done(Delayed)
    //     else IssueChooser.TryChoose(...)   // pure: query-by-number shortcut, duplicate-number refusal, natural_key match, single-issue rule
    //          needs dialog -> load issues once (ResolvedSeries caches them); empty -> Ui.ShowNoIssues; Back
    //          Pick p -> Write(p.Issue.Id, series.Ref.Id);  Back -> uncache; found != null ? ChooseSeries(terms, found) : Search(Manual: true, null)
    //          Skip/SkipForever -> if force && !config.ConfirmIssue, uncache; Done(Skipped) or SkipForever
    //   Write(id, recordChoice):
    //     issue  = await ComicVine.GetIssueAsync(id, config.ScrapeRating, ct)
    //     cover  = covers.Preferred(issue.Id) ?? issue.CoverUrls.FirstOrDefault()
    //     plan   = MergeRules.PlanThumbnail(book.Read(), cover, settings)
    //     thumb  = plan is null ? null : await ComicVine.GetImageAsync(plan.Url, ct)
    //     ct.ThrowIfCancellationRequested()
    //     // ---- no await from here to Done: read, plan, and apply are one UI-thread turn ----
    //     facts = BookFacts.From(entry.Index, book.Read(), ...)     // re-read so the merge sees current values
    //     book.Apply(MergeRules.Plan(facts, issue, settings, Clock.Now), thumb)
    //     if recordChoice is { } sid: Chosen.Record(sid)              // series.dat, written atomically
    //     -> Done(Scraped)
    //   SkipForever: book.Apply(MergeRules.PlanSkipForever(...), null) -> Done(Skipped)
}

/// <summary>A series chosen for a UniqueSeriesKey in this run, with its issue list once loaded.</summary>
public sealed class ResolvedSeries(SeriesRef series)
{
    public SeriesRef Ref { get; } = series;
    public IReadOnlyList<IssueRef>? Issues { get; set; } // loaded at most once per run
}

internal sealed record QueueEntry(int Index, IScrapeBook Book, BookFacts Facts);
