using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin.Forms;

internal static class TestBooks
{
	public static BookFacts Facts(string series = "Batman", string number = "5", string path = @"C:\Comics\Batman 005.cbz", int pages = 24, int? volume = null)
	{
		var values = new BookValues { Series = series, Number = number, Volume = volume };
		return new BookFacts(0, new BookSnapshot(values, path, pages, false), values, new StoredMarker.None(), null, series.ToLowerInvariant());
	}

	public static Configuration Config(string advanced = "") => new() { ApiKey = "key", Advanced = AdvancedSettings.Parse(advanced) };
}

public sealed class SeriesFormTests
{
	private static readonly SeriesRef Batman = new(new SeriesId(796), "Batman", 1940, "DC Comics", 713, FakeComicVine.Url("Navy"));
	private static readonly SeriesRef Detective = new(new SeriesId(18058), "Detective Comics", null, "DC Comics", 27, FakeComicVine.Url("Teal"));
	private static readonly SeriesRef Annual = new(new SeriesId(5000), "Batman Annual", 1961, "", 28, null);

	private static readonly SeriesQuestion Question = new(TestBooks.Facts(), "batman", [new(Batman, 99.5), new(Detective, 12), new(Annual, -3)]);

	private static SeriesChoice Ask(Action<SeriesForm> act, SeriesQuestion? question = null, CoverPreferences? covers = null, FakeComicVine? comicVine = null, string advanced = "") =>
		Sta.Run(() =>
		{
			using var session = UiSession.Create();
			using IDisposable driver = Ui.WhenOpen(act);
			return SeriesForm.Ask(null, session.Ctx, comicVine ?? new FakeComicVine(), TestBooks.Config(advanced), question ?? Question, covers ?? new CoverPreferences(), CancellationToken.None);
		});

	private static void Select(DataGridView grid, int row) => grid.CurrentCell = grid.Rows[row].Cells[0];

	[Test]
	public async Task The_grid_lists_the_candidates_best_first_with_their_details()
	{
		string[][] rows = [];
		Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "series");
			rows = grid.Rows.Cast<DataGridViewRow>()
				.Select(row => new[] { row.Cells["name"].FormattedValue, row.Cells["year"].FormattedValue, row.Cells["issues"].FormattedValue, row.Cells["publisher"].FormattedValue }
					.Select(value => value?.ToString() ?? "").ToArray())
				.ToArray();
			form.Close();
		});

		await Assert.That(rows.Select(r => string.Join("|", r)).SequenceEqual(["Batman|1940|713|DC Comics", "Detective Comics|--|27|DC Comics", "Batman Annual|1961|28|"])).IsTrue();
	}

	[Test]
	public async Task Only_the_series_year_issues_and_publisher_columns_are_visible()
	{
		string[] visible = [];
		Ask(form =>
		{
			visible = Ui.Find<DataGridView>(form, "series").Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Select(c => c.HeaderText).ToArray();
			form.Close();
		});

		await Assert.That(visible.SequenceEqual(["Series", "Year", "Issues", "Publisher"])).IsTrue();
	}

	[Test]
	public async Task OK_picks_the_best_candidate_when_the_user_changes_nothing()
	{
		SeriesChoice choice = Ask(form => Ui.Find<Button>(form, "ok").PerformClick());

		await Assert.That(choice).IsEqualTo(new SeriesChoice.Pick(Batman, ShowIssues: false));
	}

	[Test]
	public async Task OK_picks_the_selected_row_even_after_the_user_sorts_the_grid()
	{
		SeriesChoice choice = Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "series");
			grid.Sort(grid.Columns["name"], System.ComponentModel.ListSortDirection.Descending);
			Select(grid, 0);
			Ui.Find<Button>(form, "ok").PerformClick();
		});

		await Assert.That(choice).IsEqualTo(new SeriesChoice.Pick(Detective, ShowIssues: false));
	}

	[Test]
	public async Task Enter_in_the_grid_clicks_OK()
	{
		SeriesChoice choice = Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "series");
			Select(grid, 2);
			Ui.PressCommandKey(grid, Keys.Enter);
		});

		await Assert.That(choice).IsEqualTo(new SeriesChoice.Pick(Annual, ShowIssues: false));
	}

	[Test]
	public async Task Show_Issues_picks_the_series_and_asks_for_the_issue_list()
	{
		SeriesChoice choice = Ask(form =>
		{
			Select(Ui.Find<DataGridView>(form, "series"), 1);
			Ui.Find<Button>(form, "showIssues").PerformClick();
		});

		await Assert.That(choice).IsEqualTo(new SeriesChoice.Pick(Detective, ShowIssues: true));
	}

	[Test]
	public async Task Search_Again_Skip_Ctrl_Skip_and_Cancel_return_their_choices()
	{
		SeriesChoice again = Ask(form => Ui.Find<Button>(form, "searchAgain").PerformClick());
		SeriesChoice skip = Ask(form => Ui.Find<Button>(form, "skip").PerformClick());
		SeriesChoice forever = Ask(form =>
		{
			Ui.KeyDown(Ui.Find<DataGridView>(form, "series"), Keys.ControlKey);
			Ui.Find<Button>(form, "skip").PerformClick();
		});
		SeriesChoice cancel = Ask(form => Ui.PressCommandKey(Ui.Find<DataGridView>(form, "series"), Keys.Escape));

		await Assert.That(again).IsTypeOf<SeriesChoice.SearchAgain>();
		await Assert.That(skip).IsTypeOf<SeriesChoice.Skip>();
		await Assert.That(forever).IsTypeOf<SeriesChoice.SkipForever>();
		await Assert.That(cancel).IsTypeOf<SeriesChoice.Cancel>();
	}

	[Test]
	public async Task The_message_counts_the_matches_or_asks_to_confirm_a_single_one()
	{
		string many = "";
		string one = "";
		Ask(form =>
		{
			many = Ui.Find<Label>(form, "message").Text;
			form.Close();
		});
		Ask(form =>
		{
			one = Ui.Find<Label>(form, "message").Text;
			form.Close();
		}, Question with { Candidates = [new(Batman, 1)] });

		await Assert.That(many).StartsWith("Searched for 'batman' and found 3 matching series.");
		await Assert.That(one).StartsWith("Searched for 'batman' and found one matching series.");
	}

	[Test]
	public async Task The_cover_follows_the_selected_series_as_its_hinted_issue()
	{
		var comicVine = new FakeComicVine();
		comicVine.IssuesByNumber[(Detective.Id, "5")] = new IssueRef(new IssueId(77), "5", "", FakeComicVine.Url("Orange"));

		string label = "";
		Ask(form =>
		{
			Select(Ui.Find<DataGridView>(form, "series"), 1);
			Ui.PumpUntil(() => Ui.Find<LinkLabel>(form, "coverLabel").Text == "Issue 5 (more covers)");
			label = Ui.Find<LinkLabel>(form, "coverLabel").Text;
			form.Close();
		}, comicVine: comicVine);

		await Assert.That(label).IsEqualTo("Issue 5 (more covers)");
		await Assert.That(comicVine.IssueLookups.Select(l => l.Item1)).Contains(Detective.Id);
	}

	[Test]
	public async Task FORCE_SERIES_ART_shows_series_art_without_looking_up_the_issue()
	{
		var comicVine = new FakeComicVine();

		string label = "";
		Ask(form =>
		{
			Ui.PumpUntil(() => Ui.Find<LinkLabel>(form, "coverLabel").Text.Length > 0);
			label = Ui.Find<LinkLabel>(form, "coverLabel").Text;
			form.Close();
		}, comicVine: comicVine, advanced: "FORCE_SERIES_ART=true");

		await Assert.That(label).IsEqualTo("Series Art");
		await Assert.That(comicVine.IssueLookups).IsEmpty();
	}

	[Test]
	public async Task An_alternate_cover_left_on_show_is_preferred_only_when_the_user_confirms()
	{
		var comicVine = new FakeComicVine();
		var issue = new IssueRef(new IssueId(4242), "5", "", FakeComicVine.Url("Orange"));
		comicVine.IssuesByNumber[(Batman.Id, "5")] = issue;
		comicVine.CoversByIssue[issue.Id] = [FakeComicVine.Url("Orange"), FakeComicVine.Url("Olive")];

		CoverPreferences Run(string button)
		{
			var covers = new CoverPreferences();
			Ask(form =>
			{
				LinkLabel label = Ui.Find<LinkLabel>(form, "coverLabel");
				Ui.PumpUntil(() => label.Text == "Issue 5 (more covers)");
				label.Focus();
				Ui.KeyDown(label, Keys.Enter);
				Button next = Ui.Find<Button>(form, "nextCover");
				Ui.PumpUntil(() => next.Visible);
				next.PerformClick();
				Ui.Find<Button>(form, button).PerformClick();
			}, covers: covers, comicVine: comicVine);
			return covers;
		}

		await Assert.That(Run("ok").Preferred(issue.Id)).IsEqualTo(FakeComicVine.Url("Olive"));
		await Assert.That(Run("skip").Preferred(issue.Id)).IsNull();
	}

	[Test]
	public async Task With_covers_off_the_grid_takes_the_whole_width_and_no_cover_shows()
	{
		(int Width, bool Panel) seen = default;
		Ask(form =>
		{
			seen = (Ui.Find<DataGridView>(form, "series").Width, Ui.Find<Panel>(form, "coverPanel").Visible);
			form.Close();
		}, advanced: "SHOW_COVERS=false");

		await Assert.That(seen).IsEqualTo((710, false));
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		string[] unlocalized = Sta.Run(() =>
		{
			using var session = UiSession.Create(marked: true);
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<SeriesForm>(form =>
			{
				Ui.PumpUntil(() => Ui.Find<LinkLabel>(form, "coverLabel").Text.Length > 0);
				found = [.. Ui.UnlocalizedTexts(form, "batman")];
				Ui.Render(form, "SeriesForm");
				form.Close();
			});
			SeriesForm.Ask(null, session.Ctx, new FakeComicVine(), TestBooks.Config(), Question, new CoverPreferences(), CancellationToken.None);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}
}

public sealed class IssueFormTests
{
	private static readonly SeriesRef Batman = new(new SeriesId(796), "Batman", 1940, "DC Comics", 713, null);
	private static readonly IssueRef One = new(new IssueId(101), "1", "The Legend", FakeComicVine.Url("Red"));
	private static readonly IssueRef Two = new(new IssueId(102), "2", "", FakeComicVine.Url("Green"));
	private static readonly IssueRef Ten = new(new IssueId(110), "10", "Ten Nights", FakeComicVine.Url("Blue"));

	private static IssueQuestion Question(IssueRef? hint = null) => new(TestBooks.Facts(), Batman, [One, Two, Ten], hint);

	private static IssueChoice Ask(Action<IssueForm> act, IssueQuestion? question = null, CoverPreferences? covers = null, FakeComicVine? comicVine = null, string advanced = "") =>
		Sta.Run(() =>
		{
			using var session = UiSession.Create();
			using IDisposable driver = Ui.WhenOpen(act);
			return IssueForm.Ask(null, session.Ctx, comicVine ?? new FakeComicVine(), TestBooks.Config(advanced), question ?? Question(), covers ?? new CoverPreferences(), CancellationToken.None);
		});

	[Test]
	public async Task The_grid_lists_the_issues_in_the_order_given_with_indented_titles()
	{
		string[] rows = [];
		Ask(form =>
		{
			rows = Ui.Find<DataGridView>(form, "issues").Rows.Cast<DataGridViewRow>()
				.Select(row => row.Cells["number"].FormattedValue + "|" + row.Cells["title"].FormattedValue).ToArray();
			form.Close();
		});

		await Assert.That(rows.SequenceEqual(["1|   The Legend", "2|", "10|   Ten Nights"])).IsTrue();
	}

	[Test]
	public async Task The_hinted_issue_is_selected_and_OK_picks_it()
	{
		IssueChoice choice = Ask(form => Ui.Find<Button>(form, "ok").PerformClick(), Question(hint: Two));

		await Assert.That(choice).IsEqualTo(new IssueChoice.Pick(Two));
	}

	[Test]
	public async Task Without_a_hint_the_first_issue_is_selected()
	{
		IssueChoice choice = Ask(form => Ui.PressCommandKey(Ui.Find<DataGridView>(form, "issues"), Keys.Enter));

		await Assert.That(choice).IsEqualTo(new IssueChoice.Pick(One));
	}

	[Test]
	public async Task Sorting_by_issue_number_uses_natural_order()
	{
		string[] numbers = [];
		Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "issues");
			grid.Sort(grid.Columns["number"], System.ComponentModel.ListSortDirection.Descending);
			numbers = grid.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["number"].Value).ToArray();
			form.Close();
		});

		await Assert.That(numbers.SequenceEqual(["10", "2", "1"])).IsTrue();
	}

	[Test]
	public async Task Go_Back_Skip_Ctrl_Skip_and_Cancel_return_their_choices()
	{
		IssueChoice back = Ask(form => Ui.Find<Button>(form, "back").PerformClick());
		IssueChoice skip = Ask(form => Ui.Find<Button>(form, "skip").PerformClick());
		IssueChoice forever = Ask(form =>
		{
			Ui.KeyDown(Ui.Find<DataGridView>(form, "issues"), Keys.ControlKey);
			Ui.Find<Button>(form, "skip").PerformClick();
		});
		IssueChoice cancel = Ask(form => Ui.PressCommandKey(Ui.Find<DataGridView>(form, "issues"), Keys.Escape));

		await Assert.That(back).IsTypeOf<IssueChoice.Back>();
		await Assert.That(skip).IsTypeOf<IssueChoice.Skip>();
		await Assert.That(forever).IsTypeOf<IssueChoice.SkipForever>();
		await Assert.That(cancel).IsTypeOf<IssueChoice.Cancel>();
	}

	[Test]
	[Arguments("Batman", "DC Comics", 1940, "The following issues were found for 'Batman' (DC Comics, 1940).\nPlease choose")]
	[Arguments("Batman", "DC Comics", null, "The following issues were found for 'Batman' (DC Comics).\nPlease choose")]
	[Arguments("Batman", "", 1940, "The following issues were found for 'Batman'.\nPlease choose")]
	[Arguments("The Amazing Spider-Man Family Annual", "Marvel", 2008, "The following issues were found for 'The Amazing Spider-Man Family Annual' (Marvel, 2008).  Please choose")]
	public async Task The_message_names_the_series_and_breaks_the_line_only_for_a_short_name(string name, string publisher, int? year, string opening)
	{
		string message = "";
		Ask(form =>
		{
			message = Ui.Find<Label>(form, "message").Text;
			form.Close();
		}, Question() with { Series = Batman with { Name = name, Publisher = publisher, StartYear = year } });

		await Assert.That(message).StartsWith(opening);
	}

	[Test]
	public async Task An_alternate_cover_left_on_show_is_preferred_when_the_user_picks_the_issue()
	{
		var comicVine = new FakeComicVine();
		comicVine.CoversByIssue[One.Id] = [FakeComicVine.Url("Red"), FakeComicVine.Url("Pink")];
		var covers = new CoverPreferences();

		IssueChoice choice = Ask(form =>
		{
			LinkLabel label = Ui.Find<LinkLabel>(form, "coverLabel");
			Ui.PumpUntil(() => label.Text == "Issue 1 (more covers)");
			label.Focus();
			Ui.KeyDown(label, Keys.Enter);
			Button next = Ui.Find<Button>(form, "nextCover");
			Ui.PumpUntil(() => next.Visible);
			next.PerformClick();
			Ui.Find<Button>(form, "ok").PerformClick();
		}, covers: covers, comicVine: comicVine);

		await Assert.That(choice).IsEqualTo(new IssueChoice.Pick(One));
		await Assert.That(covers.Preferred(One.Id)).IsEqualTo(FakeComicVine.Url("Pink"));
	}

	[Test]
	public async Task With_covers_off_the_grid_takes_the_whole_width()
	{
		(int Width, int Left) seen = default;
		Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "issues");
			seen = (grid.Width, grid.Left);
			form.Close();
		}, advanced: "SHOW_COVERS=false");

		await Assert.That(seen).IsEqualTo((708, 10));
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		string[] unlocalized = Sta.Run(() =>
		{
			using var session = UiSession.Create(marked: true);
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<IssueForm>(form =>
			{
				Ui.PumpUntil(() => Ui.Find<LinkLabel>(form, "coverLabel").Text.Length > 0);
				found = [.. Ui.UnlocalizedTexts(form, "Batman", "DC Comics")];
				Ui.Render(form, "IssueForm");
				form.Close();
			});
			IssueForm.Ask(null, session.Ctx, new FakeComicVine(), TestBooks.Config(), Question(hint: Two), new CoverPreferences(), CancellationToken.None);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}
}
