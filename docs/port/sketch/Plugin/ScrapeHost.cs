// Threading. Replaces Application.DoEvents, the ComicForm Application.Run thread, Monitor wait/pulse,
// and utils.invoke. Everything runs on ComicRack's UI thread; only HTTP and hashing leave it.

namespace ComicApiPlugin.Plugin;

/// <summary>
/// Runs an async scrape inside the ComicForm's modal loop, so the hook can block (the Editor hook needs that)
/// while windows keep painting and dialogs stay interactive.
/// </summary>
internal static class ScrapeHost
{
    /// <summary>
    /// Shows ComicForm with ShowDialog(owner). Its Shown handler starts work on the UI SynchronizationContext.
    /// When work finishes the form closes itself and Run returns the result or rethrows the exception
    /// (ExceptionDispatchInfo, so the stack is kept).
    /// Closing the form any other way (X, Esc, Cancel button) cancels the token and leaves the form up,
    /// showing "Cancelling...", until work observes it; work never outlives Run.
    /// </summary>
    public static T Run<T>(IWin32Window owner, PluginContext ctx, Func<ComicForm, CancellationToken, Task<T>> work)
        => throw new NotImplementedException();
}

/// <summary>
/// IScrapeUi over the WinForms dialogs. Dialogs are owned by the ComicForm and shown with ShowDialog,
/// so each Task it returns is already complete. Each dialog registers ct to close itself with Cancel,
/// which replaces the Python cancel listeners.
/// </summary>
internal sealed class WinFormsScrapeUi(ComicForm host, PluginContext ctx, IComicVine comicVine, Configuration config) : IScrapeUi
{
    public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) => throw new NotImplementedException();
    public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct) => throw new NotImplementedException();
    public Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct) => throw new NotImplementedException();
    public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct) => throw new NotImplementedException();
    public void ShowNoIssues(SeriesRef series) => throw new NotImplementedException();
    public IQueryProgress BeginQuery(QueryKind kind) => throw new NotImplementedException(); // ProgressBarForm; closing it cancels the run
}

/// <summary>
/// Replaces utils/scheduler.py for cover panels: one load in flight per panel, and a new request cancels
/// the previous one (newest wins). Images come from IComicVine.GetImageAsync, which shares the throttle.
/// </summary>
internal sealed class LatestImageLoader(IComicVine comicVine, DebugLog log)
{
    /// <summary>Cancels the previous load, awaits this one, and calls show on the UI thread unless superseded. Never throws.</summary>
    public void Load(Uri? url, Action<Bitmap?> show) => throw new NotImplementedException();
}

/// <summary>
/// Base for every form, merging CVForm and PersistentForm: owner, no minimise or maximise, no taskbar entry,
/// FixedSingle; Esc closes; Ctrl+Shift+L saves the log; geometry restored (re-centred when off-screen) and
/// saved on close only when moved or resized; env.Theme applied on load.
/// </summary>
internal abstract class ScraperForm(PluginContext ctx, string geometryKey) : Form
{
}

// Forms, one file each, all ScraperForm. Each returns a Core type, never a string result:
//   ConfigDialog.Show(owner, ctx, Configuration) -> Configuration?   (saves on OK; the 21 checkboxes come from FieldTable)
//   WelcomeDialog.Show(owner, ctx, int count)    -> WelcomeChoice { Start, Settings, Cancel }
//   ComicForm                                     (the modal host; cover panel pages through GetPage)
//   SearchForm, SeriesForm, IssueForm             -> SearchChoice, SeriesChoice, IssueChoice
//   ProgressBarForm, FinishDialog.Show(owner, ctx, RunSummary)
