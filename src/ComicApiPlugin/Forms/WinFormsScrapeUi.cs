using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// IScrapeUi over the WinForms dialogs. Each dialog is modal to the ComicForm, so every Task returned here is already
/// complete. A cancelled token closes an open dialog, which then answers Cancel, as Python's cancel listeners did.
/// </summary>
internal sealed class WinFormsScrapeUi(ComicForm host, PluginContext ctx, IComicVine comicVine, Configuration config, ICoverComparer? comparer = null) : IScrapeUi
{
	private IScrapeBook? current;

	public void BookStarted(IScrapeBook book, BookFacts facts, int remaining)
	{
		current = book;
		host.ShowBook(book, facts, remaining);
	}

	public Task<SearchChoice> AskSearchTermsAsync(BookFacts book, string initialTerms, string? failedTerms, CancellationToken ct) =>
		Task.FromResult(SearchForm.Ask(host, ctx, initialTerms, failedTerms, ct));

	public Task<SeriesChoice> ChooseSeriesAsync(SeriesQuestion question, CoverPreferences covers, CancellationToken ct) =>
		Task.FromResult(SeriesForm.Ask(host, ctx, comicVine, config, question, covers, ct, Scoring()));

	/// <summary>The series dialog's cover scores, when a comparer is given: the current book's first page against each cover on show.</summary>
	private CoverScoring? Scoring() => comparer is null || current is not { } book ? null : new CoverScoring(() =>
	{
		using Bitmap? page = book.GetPage(0);
		return page is null ? null : CoverJpeg.Encode(page);
	}, comparer);

	public Task<IssueChoice> ChooseIssueAsync(IssueQuestion question, CoverPreferences covers, CancellationToken ct) =>
		Task.FromResult(IssueForm.Ask(host, ctx, comicVine, config, question, covers, ct));

	public void ShowNoIssues(SeriesRef series, CancellationToken ct) => PromptDialog.Show(
		host, ctx, ctx.Strings.Format(TextKeys.NoIssuesAvailableText, series.Name), ctx.Strings[TextKeys.NoIssuesAvailableTitle], MessageBoxIcon.Warning, PromptButtons.Ok, ct);

	public IQueryProgress BeginQuery(QueryKind kind) => new QueryProgressWindow(host, ctx, kind);
}
