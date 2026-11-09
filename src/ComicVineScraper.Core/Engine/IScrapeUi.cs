using System;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Model;

namespace ComicVineScraper.Engine;

/// <summary>
/// The engine's view of the UI. Dialog methods may complete synchronously (WinForms ShowDialog) or
/// later (tests). Each must return Cancel, not throw, when ct is cancelled while the dialog is open.
/// Every IScrapeUi call is made on the UI thread; only IQueryProgress.Report may arrive from another thread.
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

public enum QueryKind
{
	SeriesSearch,
	IssueList,
}

public interface IQueryProgress : IProgress<QueryProgress>, IDisposable;

/// <summary>Series search reports (matches so far, expected pages); issue loads report a 0..1 fraction.</summary>
public readonly record struct QueryProgress(int Done, int Total);
