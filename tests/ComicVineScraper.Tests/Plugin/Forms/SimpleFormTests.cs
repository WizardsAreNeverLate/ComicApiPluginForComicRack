using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.Engine;
using ComicVineScraper.Plugin;

namespace ComicVineScraper.Tests.Plugin.Forms;

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
		await Assert.That(seen.Title).IsEqualTo("Comic Vine Scraper - v" + typeof(FinishForm).Assembly.GetName().Version.ToString(3));
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
		using var session = UiSession.Create();

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
	[Arguments("start", true)]
	[Arguments("cancel", false)]
	public async Task Start_begins_the_scrape_and_Cancel_does_not(string button, bool expected)
	{
		using var session = UiSession.Create();

		bool start = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form => Ui.Find<Button>(form, button).PerformClick());
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(start).IsEqualTo(expected);
	}

	[Test]
	public async Task Escape_does_not_start_the_scrape()
	{
		using var session = UiSession.Create();

		bool start = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form => Ui.PressCommandKey(Ui.Find<Button>(form, "start"), Keys.Escape));
			return WelcomeForm.Show(null, session.Ctx, 3);
		});

		await Assert.That(start).IsFalse();
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		using var session = UiSession.Create(marked: true);

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
}
