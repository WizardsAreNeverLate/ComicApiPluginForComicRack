using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Engine;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Plugin;

/// <summary>ICoverSearchUi over the WinForms dialogs, each modal to the ComicForm, so every Task returned here is already complete.</summary>
internal sealed class WinFormsCoverSearchUi(ComicForm host, PluginContext ctx, IVerseDb verseDb) : ICoverSearchUi
{
	public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) => host.ShowBook(book, facts, remaining);

	public Task<CoverChoice> ChooseAsync(IScrapeBook book, BookFacts facts, CoverLookup lookup, CancellationToken ct) =>
		Task.FromResult(CoverMatchForm.Ask(host, ctx, verseDb, book, facts, lookup, ct));

	public void ShowNoMatches(BookFacts facts) => PromptDialog.Show(
		host, ctx, ctx.Strings.Format(TextKeys.CoverSearchNoMatchesText, DescribeBook(facts)), ctx.Strings[TextKeys.FinishFormTitle], MessageBoxIcon.Information, PromptButtons.Ok);

	public void ShowRateLimited(TimeSpan wait) =>
		host.ShowStatus(ctx.Strings.Format(TextKeys.CoverSearchRateLimited, (int)Math.Ceiling(wait.TotalSeconds)));

	private static string DescribeBook(BookFacts facts) =>
		facts.Snapshot.FilePath.Length > 0
			? System.IO.Path.GetFileName(facts.Snapshot.FilePath)
			: (facts.Effective.Series + " #" + facts.Effective.Number).Trim();
}
