// ComicVineScraper.Tests (TUnit, net48, x64): Core only, no ComicRack install needed except for the
// adapter round-trip tests, which reference ComicRack.Engine to construct real ComicBook objects.
// ComicVineScraper.Oracle.Tests: the IronPython 2.7.4 differential tests, separate so they can be skipped
// on a machine without ComicRack's IronPython.

namespace ComicVineScraper.Tests;

/// <summary>Answers dialogs from a script and records what the engine asked. Unscripted questions fail the test.</summary>
public sealed class ScriptedUi : IScrapeUi
{
    public ScriptedUi Search(SearchChoice answer) => throw new NotImplementedException();
    public ScriptedUi Series(Func<SeriesQuestion, SeriesChoice> answer) => throw new NotImplementedException();
    public ScriptedUi Issue(Func<IssueQuestion, IssueChoice> answer) => throw new NotImplementedException();
    public IReadOnlyList<string> Asked { get; } = []; // "search", "series:<terms>", "issue:<series id>", "no-issues"

    public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) { }
    public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct) => throw new NotImplementedException();
    public Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct) => throw new NotImplementedException();
    public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct) => throw new NotImplementedException();
    public void ShowNoIssues(SeriesRef series) => throw new NotImplementedException();
    public IQueryProgress BeginQuery(QueryKind kind) => throw new NotImplementedException();
}

/// <summary>An in-memory book. Records every Apply, so a test asserts on the exact writes.</summary>
public sealed class FakeBook(BookSnapshot initial) : IScrapeBook
{
    public IReadOnlyList<BookPatch> Applied { get; } = [];
    public BookSnapshot Read() => throw new NotImplementedException();
    public Bitmap? GetPage(int index) => null;
    public void Apply(BookPatch patch, Bitmap? thumbnail) => throw new NotImplementedException();
}

/// <summary>Replays fixtures/cv/*.xml by redacted URL; records nothing live. Drives the real ComicVineHttp.</summary>
public sealed class FixtureHandler(string fixtureFolder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new NotImplementedException();
}

/// <summary>A clock whose Delay completes at once and advances Now, so the 1100 ms gap is asserted, not waited.</summary>
public sealed class ManualClock(DateTime start) : IClock
{
    public DateTime Now => throw new NotImplementedException();
    public Task Delay(TimeSpan delay, CancellationToken ct) => throw new NotImplementedException();
}

public sealed class EngineTests
{
    // [Test]  a book scraped end to end with scripted choices
    public async Task Manual_search_then_pick_writes_one_patch()
    {
        var book = new FakeBook(Snapshots.Named("Batman 012 (1990).cbz"));
        var ui = new ScriptedUi()
            .Series(q => new SeriesChoice.Pick(q.Candidates[0].Series, ShowIssues: false));
        var run = new ScrapeRun(Services.With(ui, ComicVineFake.FromFixtures("batman")), Configs.Default);

        RunSummary summary = await run.RunAsync([book], CancellationToken.None);

        // assert: summary.Scraped == 1; book.Applied.Count == 1;
        //         book.Applied[0].Fields has Series|Number|IssueKey|SeriesKey|Tags...; ui.Asked == ["series:batman"]
        throw new NotImplementedException();
    }
}
