using ComicApiPlugin.Config;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin.Forms;

public sealed class FinishFormTests
{
	[Test]
	[Arguments(1, 3, "Scraped details for 1 comic book.", "Skipped 3 comic books.")]
	[Arguments(12, 1, "Scraped details for 12 comic books.", "Skipped 1 comic book.")]
	[Arguments(0, 0, "Scraped details for 0 comic books.", "Skipped 0 comic books.")]
	public async Task The_summary_counts_scraped_and_skipped_books(int scraped, int skipped, string scrapedText, string skippedText)
	{
		using var session = UiSession.Create();

		(string Scraped, string Skipped, string Title) seen = Sta.Run(() =>
		{
			(string, string, string) captured = ("", "", "");
			using IDisposable driver = Ui.WhenOpen<FinishForm>(form =>
			{
				captured = (Ui.Find<Label>(form, "scraped").Text, Ui.Find<Label>(form, "skipped").Text, form.Text);
				Ui.Find<Button>(form, "ok").PerformClick();
			});
			FinishForm.Show(null, session.Ctx, new RunSummary(scraped, skipped, Cancelled: false));
			return captured;
		});

		await Assert.That(seen.Scraped).IsEqualTo(scrapedText);
		await Assert.That(seen.Skipped).IsEqualTo(skippedText);
		await Assert.That(seen.Title).IsEqualTo("Comic API Plugin");
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		using var session = UiSession.Create(marked: true);

		string[] unlocalized = Sta.Run(() =>
		{
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<FinishForm>(form =>
			{
				found = [.. Ui.UnlocalizedTexts(form)];
				Ui.Render(form, "FinishForm");
				form.Close();
			});
			FinishForm.Show(null, session.Ctx, new RunSummary(4, 2, Cancelled: false));
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}
}

public sealed class WelcomeFormTests
{
	[Test]
	[Arguments(1, "You are about to download and store details for 1 comic book.")]
	[Arguments(7, "You are about to download and store details for 7 comic books.")]
	public async Task The_text_names_how_many_books_will_be_scraped(int count, string opening)
	{
		using var session = UiSession.Create().WithKeys();

		string text = Sta.Run(() =>
		{
			string seen = "";
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				seen = Ui.Find<Label>(form, "message").Text;
				form.Close();
			});
			WelcomeForm.Show(null, session.Ctx, count);
			return seen;
		});

		await Assert.That(text).StartsWith(opening);
	}

	[Test]
	public async Task The_start_button_reads_Query_API_and_the_message_names_it()
	{
		using var session = UiSession.Create().WithKeys();

		(string Button, string Message) seen = Sta.Run(() =>
		{
			(string, string) captured = ("", "");
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				captured = (Ui.Find<Button>(form, "start").Text, Ui.Find<Label>(form, "message").Text);
				form.Close();
			});
			WelcomeForm.Show(null, session.Ctx, 3);
			return captured;
		});

		await Assert.That(seen.Button).IsEqualTo("Query API");
		await Assert.That(seen.Message).EndsWith("Click 'Query API' to begin.");
	}

	[Test]
	[Arguments("start", true)]
	[Arguments("cancel", false)]
	public async Task Start_begins_the_scrape_and_Cancel_does_not(string button, bool expected)
	{
		using var session = UiSession.Create().WithKeys();

		ScrapeAction? start = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form => Ui.Find<Button>(form, button).PerformClick());
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(start).IsEqualTo(expected ? ScrapeAction.ComicVineQuery : null);
	}

	[Test]
	public async Task Escape_does_not_start_the_scrape()
	{
		using var session = UiSession.Create().WithKeys();

		ScrapeAction? start = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form => Ui.PressCommandKey(Ui.Find<Button>(form, "start"), Keys.Escape));
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(start).IsNull();
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		using var session = UiSession.Create(marked: true).WithKeys("k", "token");

		string[] unlocalized = Sta.Run(() =>
		{
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				found = [.. Ui.UnlocalizedTexts(form)];
				Ui.Render(form, "WelcomeForm");
				form.Close();
			});
			WelcomeForm.Show(null, session.Ctx, 5);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}

	[Test]
	public async Task A_start_button_per_available_action_in_priority_order_with_the_first_as_default()
	{
		using var session = UiSession.Create().WithKeys("k", "token", ProviderKind.VerseDb, ProviderKind.ComicVine);

		(string First, string Second, string Default) seen = Sta.Run(() =>
		{
			(string, string, string) captured = ("", "", "");
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				captured = (Ui.Find<Button>(form, "start").Text, Ui.Find<Button>(form, "startComicVineQuery").Text, ((Button)form.AcceptButton!).Name);
				form.Close();
			});
			WelcomeForm.Show(null, session.Ctx, 3);
			return captured;
		});

		await Assert.That(seen.First).IsEqualTo("Cover Search (VerseDB)");
		await Assert.That(seen.Second).IsEqualTo("Query API");
		await Assert.That(seen.Default).IsEqualTo("start");
	}

	[Test]
	public async Task Starting_the_second_action_returns_it()
	{
		using var session = UiSession.Create().WithKeys("k", "token");

		ScrapeAction? picked = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form => Ui.Find<Button>(form, "startVerseDbCoverSearch").PerformClick());
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(picked).IsEqualTo(ScrapeAction.VerseDbCoverSearch);
	}

	[Test]
	public async Task A_disabled_or_keyless_provider_has_no_start_button()
	{
		using var session = UiSession.Create().WithKeys("k", "");

		bool hasSecond = Sta.Run(() =>
		{
			bool found = true;
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				found = form.Controls.ContainsKey("startVerseDbCoverSearch");
				form.Close();
			});
			WelcomeForm.Show(null, session.Ctx, 3);
			return found;
		});

		await Assert.That(hasSecond).IsFalse();
	}

	[Test]
	public async Task The_start_buttons_are_rebuilt_after_the_settings_add_a_token()
	{
		using var session = UiSession.Create().WithKeys("k", "");

		ScrapeAction? picked = Sta.Run(() =>
		{
			WelcomeForm? welcome = null;
			using IDisposable settings = Ui.WhenOpen<ConfigForm>(form =>
			{
				Ui.Find<TextBox>(form, "verseDbKey").Text = "token";
				Ui.Find<Button>(form, "ok").PerformClick();
				welcome!.BeginInvoke(new Action(() => Ui.Find<Button>(welcome, "startVerseDbCoverSearch").PerformClick()));
			});
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				welcome = form;
				Ui.Find<Button>(form, "settings").PerformClick();
			});
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(picked).IsEqualTo(ScrapeAction.VerseDbCoverSearch);
	}
}