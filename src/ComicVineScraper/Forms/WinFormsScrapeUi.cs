using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.Books;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Plugin;

/// <summary>
/// IScrapeUi over the WinForms dialogs. Each dialog is modal to the ComicForm, so every Task returned here is already
/// complete. A cancelled token closes an open dialog, which then answers Cancel, as Python's cancel listeners did.
/// </summary>
internal sealed class WinFormsScrapeUi(ComicForm host, PluginContext ctx, IComicVine comicVine, Configuration config) : IScrapeUi
{
	public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) => host.ShowBook(book, facts, remaining);

	public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct) =>
		Task.FromResult(SearchForm.Ask(host, ctx, initialTerms, failedTerms, ct));

	public Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct) =>
		Task.FromResult(SeriesForm.Ask(host, ctx, comicVine, config, question, covers, ct));

	public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct) =>
		Task.FromResult(IssueForm.Ask(host, ctx, comicVine, config, question, covers, ct));

	public void ShowNoIssues(SeriesRef series, CancellationToken ct) => PromptDialog.Show(
		host, ctx, ctx.Strings.Format(TextKeys.NoIssuesAvailableText, series.Name), ctx.Strings[TextKeys.NoIssuesAvailableTitle], MessageBoxIcon.Warning, PromptButtons.Ok, ct);

	public IQueryProgress BeginQuery(QueryKind kind) => new QueryProgressWindow(host, ctx, kind);
}
