using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using ComicVineScraper.Plugin;

namespace ComicVineScraper.Tests.Plugin.Forms;

public sealed class WinFormsScrapeUiTests
{
	private static readonly SeriesRef Batman = new(new SeriesId(796), "Batman", 1940, "DC Comics", 713, null);
	private static readonly IssueRef Five = new(new IssueId(505), "5", "", null);

	/// <summary>Runs work as the engine would: inside ScrapeHost, with a WinFormsScrapeUi over the ComicForm it shows.</summary>
	private static T Hosted<T>(Func<WinFormsScrapeUi, ComicForm, CancellationToken, Task<T>> work) => Sta.Run(() =>
	{
		using var session = UiSession.Create();
		return ScrapeHost.Run(null!, session.Ctx, (form, ct) => work(new WinFormsScrapeUi(form, session.Ctx, new FakeComicVine(), TestBooks.Config()), form, ct));
	});

	private static async Task Until(Func<bool> condition)
	{
		DateTime end = DateTime.UtcNow.AddSeconds(30);
		while (!condition())
		{
			if (DateTime.UtcNow > end)
			{
				throw new TimeoutException("The UI did not reach the expected state in time.");
			}
			await Task.Delay(10);
		}
	}

	[Test]
	public async Task BookStarted_shows_the_book_on_the_ComicForm()
	{
		string label = Hosted((ui, form, _) =>
		{
			ui.BookStarted(new ColouredBook(), TestBooks.Facts(), 2);
			return Task.FromResult(Ui.Find<Label>(form, "scraping").Text + " / " + Ui.Find<Button>(form, "cancel").Text);
		});

		await Assert.That(label).IsEqualTo("Scraping: Batman 005.cbz / Cancel (2 remaining)");
	}

	[Test]
	public async Task The_search_dialog_is_modal_to_the_ComicForm_and_returns_the_answer()
	{
		(SearchChoice Choice, bool OwnedByHost) outcome = Hosted(async (ui, form, ct) =>
		{
			bool owned = false;
			using IDisposable driver = Ui.WhenOpen<SearchForm>(dialog =>
			{
				owned = dialog.Owner == form;
				Ui.Find<Button>(dialog, "search").PerformClick();
			});
			return (await ui.AskSearchTermsAsync(TestBooks.Facts(), "Batman", null, ct), owned);
		});

		await Assert.That(outcome.Choice).IsEqualTo(new SearchChoice.Search("Batman"));
		await Assert.That(outcome.OwnedByHost).IsTrue();
	}

	[Test]
	public async Task The_series_and_issue_dialogs_return_the_users_pick()
	{
		(SeriesChoice Series, IssueChoice Issue) outcome = Hosted(async (ui, _, ct) =>
		{
			var covers = new CoverPreferences();
			SeriesChoice series;
			using (Ui.WhenOpen<SeriesForm>(dialog => Ui.Find<Button>(dialog, "showIssues").PerformClick()))
			{
				series = await ui.ChooseSeriesAsync(new SeriesQuestion(TestBooks.Facts(), "batman", [new(Batman, 1)]), covers, ct);
			}
			using (Ui.WhenOpen<IssueForm>(dialog => Ui.Find<Button>(dialog, "ok").PerformClick()))
			{
				return (series, await ui.ChooseIssueAsync(new IssueQuestion(TestBooks.Facts(), Batman, [Five], Five), covers, ct));
			}
		});

		await Assert.That(outcome.Series).IsEqualTo(new SeriesChoice.Pick(Batman, ShowIssues: true));
		await Assert.That(outcome.Issue).IsEqualTo(new IssueChoice.Pick(Five));
	}

	[Test]
	[Arguments("search")]
	[Arguments("series")]
	[Arguments("issue")]
	public async Task Cancelling_the_run_closes_an_open_dialog_and_it_answers_Cancel(string dialog)
	{
		object answer = Hosted<object>(async (ui, _, ct) =>
		{
			using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
			using IDisposable driver = dialog switch
			{
				"search" => Ui.WhenOpen<SearchForm>(_ => run.Cancel()),
				"series" => Ui.WhenOpen<SeriesForm>(_ => run.Cancel()),
				_ => Ui.WhenOpen<IssueForm>(_ => run.Cancel()),
			};
			return dialog switch
			{
				"search" => await ui.AskSearchTermsAsync(TestBooks.Facts(), "Batman", null, run.Token),
				"series" => await ui.ChooseSeriesAsync(new SeriesQuestion(TestBooks.Facts(), "batman", [new(Batman, 1)]), new CoverPreferences(), run.Token),
				_ => await ui.ChooseIssueAsync(new IssueQuestion(TestBooks.Facts(), Batman, [Five], null), new CoverPreferences(), run.Token),
			};
		});

		await Assert.That(answer.GetType().Name).IsEqualTo("Cancel");
	}

	[Test]
	public async Task No_issues_warns_with_the_series_name()
	{
		(string Text, string Title) seen = Hosted((ui, _, _) =>
		{
			(string, string) captured = ("", "");
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				captured = (Ui.Find<Label>(prompt, "message").Text, prompt.Text);
				Ui.Find<Button>(prompt, "ok").PerformClick();
			});
			ui.ShowNoIssues(Batman);
			return Task.FromResult(captured);
		});

		await Assert.That(seen.Text).StartsWith("You selected 'Batman'.");
		await Assert.That(seen.Title).IsEqualTo("Series has No Issues");
	}

	[Test]
	public async Task A_series_search_shows_a_progress_window_that_counts_matches_and_closes_when_done()
	{
		(string Title1, int Value1, int Max, string Title2, int Value2, bool OwnedByHost, bool OpenAfter, bool Cancelled) seen = Hosted(async (ui, form, ct) =>
		{
			IQueryProgress query = ui.BeginQuery(QueryKind.SeriesSearch);
			await Task.Run(() => query.Report(new QueryProgress(100, 3)));
			await Until(() => Ui.Mine<ProgressBarForm>() is not null);
			ProgressBarForm window = Ui.Mine<ProgressBarForm>()!;
			ProgressBar bar = Ui.Find<ProgressBar>(window, "progress");
			(string, int, int) first = (window.Text, bar.Value, bar.Maximum);
			await Task.Run(() => query.Report(new QueryProgress(200, 3)));
			await Until(() => bar.Value == 2);
			Ui.Render(window, "ProgressBarForm");
			(string, int) second = (window.Text, bar.Value);
			bool owned = window.Owner == form;
			query.Dispose();
			return (first.Item1, first.Item2, first.Item3, second.Item1, second.Item2, owned, Ui.Mine<ProgressBarForm>() is not null, ct.IsCancellationRequested);
		});

		await Assert.That(seen.Title1).IsEqualTo("Searching Comic Vine (100 matches)");
		await Assert.That((seen.Value1, seen.Max)).IsEqualTo((1, 3));
		await Assert.That(seen.Title2).IsEqualTo("Searching Comic Vine (200 matches)");
		await Assert.That(seen.Value2).IsEqualTo(2);
		await Assert.That(seen.OwnedByHost).IsTrue();
		await Assert.That(seen.OpenAfter).IsFalse();
		await Assert.That(seen.Cancelled).IsFalse();
	}

	[Test]
	public async Task An_issue_load_shows_its_percentage()
	{
		(string Title, int Value, int Max) seen = Hosted(async (ui, _, _) =>
		{
			using IQueryProgress query = ui.BeginQuery(QueryKind.IssueList);
			await Task.Run(() => query.Report(new QueryProgress(100, 400)));
			await Until(() => Ui.Mine<ProgressBarForm>() is not null);
			ProgressBarForm window = Ui.Mine<ProgressBarForm>()!;
			ProgressBar bar = Ui.Find<ProgressBar>(window, "progress");
			return (window.Text, bar.Value, bar.Maximum);
		});

		await Assert.That(seen).IsEqualTo(("Loading Series Details (25% complete)", 25, 100));
	}

	[Test]
	public async Task An_issue_load_that_is_complete_at_its_first_report_shows_no_window()
	{
		bool shown = Hosted(async (ui, _, _) =>
		{
			using IQueryProgress query = ui.BeginQuery(QueryKind.IssueList);
			query.Report(new QueryProgress(500, 400));
			await Task.Delay(100);
			return Ui.Mine<ProgressBarForm>() is not null;
		});

		await Assert.That(shown).IsFalse();
	}

	[Test]
	public async Task Closing_the_progress_window_cancels_the_run()
	{
		bool cancelled = Hosted(async (ui, _, ct) =>
		{
			using IQueryProgress query = ui.BeginQuery(QueryKind.SeriesSearch);
			query.Report(new QueryProgress(100, 5));
			await Until(() => Ui.Mine<ProgressBarForm>() is not null);
			Ui.PressCommandKey(Ui.Mine<ProgressBarForm>()!, Keys.Escape);
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(10), ct);
			}
			catch (OperationCanceledException)
			{
			}
			return ct.IsCancellationRequested;
		});

		await Assert.That(cancelled).IsTrue();
	}

	[Test]
	public async Task The_progress_window_text_is_localized()
	{
		string[] unlocalized = Sta.Run(() =>
		{
			using var session = UiSession.Create(marked: true);
			return ScrapeHost.Run(null!, session.Ctx, async (form, _) =>
			{
				var ui = new WinFormsScrapeUi(form, session.Ctx, new FakeComicVine(), TestBooks.Config());
				using IQueryProgress query = ui.BeginQuery(QueryKind.IssueList);
				query.Report(new QueryProgress(1, 2));
				await Until(() => Ui.Mine<ProgressBarForm>() is not null);
				return Ui.UnlocalizedTexts(Ui.Mine<ProgressBarForm>()!).ToArray();
			});
		});

		await Assert.That(unlocalized).IsEmpty();
	}
}
