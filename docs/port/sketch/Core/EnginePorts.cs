// ComicApiPlugin.Core: what the engine needs from the outside, and what users can answer.
// The plugin implements IScrapeBook and IScrapeUi with ComicRack and WinForms.
// Tests implement them with FakeBook and ScriptedUi (Tests/ScriptedUi.cs).

namespace ComicApiPlugin.Engine;

/// <summary>
/// One library book. Every member is called on the thread that runs the scrape (ComicRack's UI thread
/// in the plugin) and is synchronous: none of them may do network I/O.
/// </summary>
public interface IScrapeBook
{
    BookSnapshot Read();

    /// <summary>A copy of the page with the back cover stripped, or null (fileless, unreadable, out of range). Caller disposes.</summary>
    Bitmap? GetPage(int index);

    /// <summary>
    /// Writes every field in patch.Fields, then sets the thumbnail if one is given. No reads of Comic Vine, no awaits.
    /// Idempotent: applying the same patch twice leaves the same book.
    /// </summary>
    void Apply(BookPatch patch, Bitmap? thumbnail);
}

/// <summary>User answers. Each dialog's result is a closed union; the engine switches on it exhaustively.</summary>
public abstract record SearchChoice
{
    private SearchChoice() { }
    public sealed record Search(string Terms) : SearchChoice;
    public sealed record Skip : SearchChoice;
    public sealed record SkipForever : SearchChoice;   // Ctrl+Skip
    public sealed record Cancel : SearchChoice;
}

public abstract record SeriesChoice
{
    private SeriesChoice() { }
    public sealed record Pick(SeriesRef Series, bool ShowIssues) : SeriesChoice; // OK, or "Show Issues"
    public sealed record SearchAgain : SeriesChoice;
    public sealed record Skip : SeriesChoice;
    public sealed record SkipForever : SeriesChoice;
    public sealed record Cancel : SeriesChoice;
}

public abstract record IssueChoice
{
    private IssueChoice() { }
    public sealed record Pick(IssueRef Issue) : IssueChoice;
    public sealed record Back : IssueChoice;
    public sealed record Skip : IssueChoice;
    public sealed record SkipForever : IssueChoice;
    public sealed record Cancel : IssueChoice;
}

public sealed record RankedSeries(SeriesRef Series, int Score);

public sealed record SeriesQuestion(BookFacts Book, string SearchTerms, IReadOnlyList<RankedSeries> Candidates); // best first

public sealed record IssueQuestion(BookFacts Book, SeriesRef Series, IReadOnlyList<IssueRef> Issues, IssueRef? Hint); // natural order

/// <summary>Covers the user flipped to in a cover panel. Replaces session_data_map["<key>-altcover"]. Written by the UI, read by MergeRules' caller.</summary>
public sealed class CoverPreferences
{
    public void Prefer(IssueId issue, Uri cover) => throw new NotImplementedException();
    public Uri? Preferred(IssueId issue) => throw new NotImplementedException();
}

/// <summary>
/// The engine's view of the UI. Dialog methods may complete synchronously (WinForms ShowDialog) or
/// later (tests). Each must return Cancel, not throw, when ct is cancelled while the dialog is open.
/// </summary>
public interface IScrapeUi
{
    /// <summary>ComicForm: "Scraping: name", the cover, "Cancel (N remaining)".</summary>
    void BookStarted(IScrapeBook book, BookFacts facts, int remaining);

    Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct);
    Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct);
    Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct);

    /// <summary>The "no issues available in {series}" warning; the engine then goes back to the series list.</summary>
    void ShowNoIssues(SeriesRef series);

    /// <summary>ProgressBarForm for one query. Shown on the first report below 100%, closed on Dispose.</summary>
    IQueryProgress BeginQuery(QueryKind kind);
}

public enum QueryKind { SeriesSearch, IssueList }

public interface IQueryProgress : IProgress<QueryProgress>, IDisposable;

/// <summary>Series search reports (matches so far, expected pages); issue loads report a 0..1 fraction.</summary>
public readonly record struct QueryProgress(int Done, int Total);

/// <summary>Time, injected so MatchScore's recency term, note dates, and the scrape delay are testable.</summary>
public interface IClock
{
    DateTime Now { get; }
    Task Delay(TimeSpan delay, CancellationToken ct);
}
