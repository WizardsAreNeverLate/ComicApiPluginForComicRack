using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin.Forms;

public sealed class SearchFormTests
{
	private static SearchChoice Ask(UiSession session, string initial, string? failed, Action<SearchForm> act) => Sta.Run(() =>
	{
		using IDisposable driver = Ui.WhenOpen(act);
		return SearchForm.Ask(null, session.Ctx, initial, failed, CancellationToken.None);
	});

	[Test]
	public async Task Search_returns_the_typed_terms_trimmed()
	{
		using var session = UiSession.Create();

		SearchChoice choice = Ask(session, "Batman", null, form =>
		{
			Ui.Find<TextBox>(form, "terms").Text = "  Detective Comics  ";
			Ui.Find<Button>(form, "search").PerformClick();
		});

		await Assert.That(choice).IsEqualTo(new SearchChoice.Search("Detective Comics"));
	}

	[Test]
	public async Task Enter_in_the_text_box_searches_for_the_initial_terms()
	{
		using var session = UiSession.Create();

		SearchChoice choice = Ask(session, "Batman", null, form => Ui.PressCommandKey(Ui.Find<TextBox>(form, "terms"), Keys.Enter));

		await Assert.That(choice).IsEqualTo(new SearchChoice.Search("Batman"));
	}

	[Test]
	public async Task Search_is_disabled_while_the_terms_are_blank_even_at_first()
	{
		using var session = UiSession.Create();

		(bool AtFirst, bool Typed, bool Blanked) enabled = Sta.Run(() =>
		{
			(bool, bool, bool) seen = default;
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				Button search = Ui.Find<Button>(form, "search");
				TextBox terms = Ui.Find<TextBox>(form, "terms");
				bool atFirst = search.Enabled;
				terms.Text = "Batman";
				bool typed = search.Enabled;
				terms.Text = "   ";
				seen = (atFirst, typed, search.Enabled);
				form.Close();
			});
			SearchForm.Ask(null, session.Ctx, "", null, CancellationToken.None);
			return seen;
		});

		await Assert.That(enabled).IsEqualTo((false, true, false));
	}

	[Test]
	public async Task A_failed_search_explains_itself_and_offers_the_failed_terms_again()
	{
		using var session = UiSession.Create();

		(string Title, string Failure, string Terms, bool FailureVisible) seen = Sta.Run(() =>
		{
			(string, string, string, bool) captured = ("", "", "", false);
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				Label failure = Ui.Find<Label>(form, "failure");
				captured = (form.Text, failure.Text, Ui.Find<TextBox>(form, "terms").Text, failure.Visible);
				Ui.Render(form, "SearchForm-failed");
				form.Close();
			});
			SearchForm.Ask(null, session.Ctx, "Batman", "Batmn Begins", CancellationToken.None);
			return captured;
		});

		await Assert.That(seen.Title).IsEqualTo("Search Failed");
		await Assert.That(seen.Failure).Contains("'Batmn Begins'");
		await Assert.That(seen.FailureVisible).IsTrue();
		await Assert.That(seen.Terms).IsEqualTo("Batmn Begins");
	}

	[Test]
	public async Task A_first_search_has_no_failure_text()
	{
		using var session = UiSession.Create();

		(string Title, bool FailureVisible) seen = Sta.Run(() =>
		{
			(string, bool) captured = ("", true);
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				captured = (form.Text, Ui.Find<Label>(form, "failure").Visible);
				Ui.Render(form, "SearchForm");
				form.Close();
			});
			SearchForm.Ask(null, session.Ctx, "Batman", null, CancellationToken.None);
			return captured;
		});

		await Assert.That(seen.Title).IsEqualTo("Search for a Comic Book");
		await Assert.That(seen.FailureVisible).IsFalse();
	}

	[Test]
	public async Task Skip_skips_the_book()
	{
		using var session = UiSession.Create();

		SearchChoice choice = Ask(session, "Batman", null, form => Ui.Find<Button>(form, "skip").PerformClick());

		await Assert.That(choice).IsTypeOf<SearchChoice.Skip>();
	}

	[Test]
	public async Task Holding_Ctrl_relabels_Skip_and_makes_it_skip_forever()
	{
		using var session = UiSession.Create();

		(SearchChoice Choice, string Held) outcome = Sta.Run(() =>
		{
			string held = "";
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				Ui.KeyDown(Ui.Find<TextBox>(form, "terms"), Keys.ControlKey);
				Button skip = Ui.Find<Button>(form, "skip");
				held = skip.Text;
				skip.PerformClick();
			});
			return (SearchForm.Ask(null, session.Ctx, "Batman", null, CancellationToken.None), held);
		});

		await Assert.That(outcome.Held).IsEqualTo("- Skip -");
		await Assert.That(outcome.Choice).IsTypeOf<SearchChoice.SkipForever>();
	}

	[Test]
	public async Task Ctrl_Skip_stays_skip_forever_when_the_closing_dialog_loses_focus()
	{
		using var session = UiSession.Create();

		SearchChoice choice = Ask(session, "Batman", null, form =>
		{
			Ui.Activate(form);
			Ui.KeyDown(Ui.Find<TextBox>(form, "terms"), Keys.ControlKey);
			Ui.Find<Button>(form, "skip").PerformClick();
			Ui.Deactivate(form);
		});

		await Assert.That(choice).IsTypeOf<SearchChoice.SkipForever>();
	}

	[Test]
	public async Task Releasing_Ctrl_restores_a_plain_Skip()
	{
		using var session = UiSession.Create();

		(SearchChoice Choice, string Released) outcome = Sta.Run(() =>
		{
			string released = "";
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				TextBox terms = Ui.Find<TextBox>(form, "terms");
				Ui.KeyDown(terms, Keys.ControlKey);
				Ui.KeyUp(terms, Keys.ControlKey);
				Button skip = Ui.Find<Button>(form, "skip");
				released = skip.Text;
				skip.PerformClick();
			});
			return (SearchForm.Ask(null, session.Ctx, "Batman", null, CancellationToken.None), released);
		});

		await Assert.That(outcome.Released).IsEqualTo("Skip");
		await Assert.That(outcome.Choice).IsTypeOf<SearchChoice.Skip>();
	}

	[Test]
	public async Task Cancel_and_Escape_cancel_the_run()
	{
		using var session = UiSession.Create();

		SearchChoice clicked = Ask(session, "Batman", null, form => Ui.Find<Button>(form, "cancel").PerformClick());
		SearchChoice escaped = Ask(session, "Batman", null, form => Ui.PressCommandKey(Ui.Find<TextBox>(form, "terms"), Keys.Escape));

		await Assert.That(clicked).IsTypeOf<SearchChoice.Cancel>();
		await Assert.That(escaped).IsTypeOf<SearchChoice.Cancel>();
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		using var session = UiSession.Create(marked: true);

		string[] unlocalized = Sta.Run(() =>
		{
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<SearchForm>(form =>
			{
				Ui.KeyDown(Ui.Find<TextBox>(form, "terms"), Keys.ControlKey);
				found = [.. Ui.UnlocalizedTexts(form, "Batmn Begins")];
				form.Close();
			});
			SearchForm.Ask(null, session.Ctx, "Batman", "Batmn Begins", CancellationToken.None);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}
}
