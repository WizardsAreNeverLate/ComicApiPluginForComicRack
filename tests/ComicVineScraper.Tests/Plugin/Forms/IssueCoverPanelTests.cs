using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.Model;
using ComicVineScraper.Plugin;

namespace ComicVineScraper.Tests.Plugin.Forms;

public sealed class IssueCoverPanelTests
{
	private static readonly SeriesRef Batman = new(new SeriesId(796), "Batman", 1940, "DC Comics", 713, FakeComicVine.Url("Navy"));
	private static readonly IssueRef Issue5 = new(new IssueId(5005), "5", "The Joker", FakeComicVine.Url("Red"));
	private static readonly IssueRef Issue6 = new(new IssueId(6006), "6", "", FakeComicVine.Url("Green"));

	/// <summary>Hosts a panel in a form, runs act once it is up, and returns what act returned.</summary>
	private static T WithPanel<T>(FakeComicVine comicVine, bool showCovers, string? hint, Func<IssueCoverPanel, T> act) => Sta.Run(() =>
	{
		using var session = UiSession.Create();
		using var host = new Form { ClientSize = new Size(220, 380), StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = false };
		var panel = new IssueCoverPanel(session.Ctx, comicVine, showCovers, hint) { Location = new Point(10, 10) };
		host.Controls.Add(panel);
		T result = default!;
		Ui.WhenShown(host, () =>
		{
			try
			{
				result = act(panel);
			}
			finally
			{
				host.Close();
			}
		});
		host.ShowDialog();
		return result;
	});

	private static bool Showing(IssueCoverPanel panel, string colour) =>
		Ui.Find<PictureBox>(panel, "cover").Image is Bitmap image && image.Width == 20 && image.GetPixel(5, 5).ToArgb() == Color.FromName(colour).ToArgb();

	private static string Label(IssueCoverPanel panel) => Ui.Find<LinkLabel>(panel, "coverLabel").Text;

	private static void ClickMoreCovers(IssueCoverPanel panel)
	{
		LinkLabel label = Ui.Find<LinkLabel>(panel, "coverLabel");
		label.Focus();
		Ui.KeyDown(label, Keys.Enter);
	}

	[Test]
	public async Task An_issue_shows_its_thumbnail_and_offers_more_covers()
	{
		var comicVine = new FakeComicVine();

		(string Label, string Link, bool Next, bool Previous) seen = WithPanel(comicVine, true, null, panel =>
		{
			panel.ShowIssue(Issue5);
			Ui.PumpUntil(() => Showing(panel, "Red"));
			LinkLabel label = Ui.Find<LinkLabel>(panel, "coverLabel");
			LinkLabel.Link link = label.Links.Cast<LinkLabel.Link>().Single();
			Ui.Render(panel.FindForm()!, "IssueCoverPanel-issue");
			return (label.Text, label.Text.Substring(link.Start, link.Length), Ui.Find<Button>(panel, "nextCover").Visible, Ui.Find<Button>(panel, "previousCover").Visible);
		});

		await Assert.That(seen.Label).IsEqualTo("Issue 5 (more covers)");
		await Assert.That(seen.Link).IsEqualTo("more covers");
		await Assert.That(seen.Next).IsFalse();
		await Assert.That(seen.Previous).IsFalse();
	}

	[Test]
	public async Task More_covers_loads_the_alternates_and_the_buttons_page_through_them()
	{
		var comicVine = new FakeComicVine();
		comicVine.CoversByIssue[Issue5.Id] = [FakeComicVine.Url("Red"), FakeComicVine.Url("Gold"), FakeComicVine.Url("Purple")];

		string[] labels = WithPanel<string[]>(comicVine, true, null, panel =>
		{
			panel.ShowIssue(Issue5);
			Ui.PumpUntil(() => Showing(panel, "Red"));
			ClickMoreCovers(panel);
			Ui.PumpUntil(() => Label(panel).Contains("of"));
			string first = Label(panel);
			Ui.Find<Button>(panel, "nextCover").PerformClick();
			Ui.PumpUntil(() => Showing(panel, "Gold"));
			string second = Label(panel);
			Ui.Find<Button>(panel, "nextCover").PerformClick();
			Ui.PumpUntil(() => Showing(panel, "Purple"));
			bool nextAtEnd = Ui.Find<Button>(panel, "nextCover").Visible;
			Ui.Find<Button>(panel, "previousCover").PerformClick();
			Ui.PumpUntil(() => Showing(panel, "Gold"));
			Ui.Render(panel.FindForm()!, "IssueCoverPanel-alternate");
			return [first, second, nextAtEnd.ToString()];
		});

		await Assert.That(labels[0]).IsEqualTo("Issue 5  -  Cover 1 of 3");
		await Assert.That(labels[1]).IsEqualTo("Issue 5  -  Cover 2 of 3");
		await Assert.That(labels[2]).IsEqualTo("False");
	}

	[Test]
	public async Task An_issue_with_no_alternates_says_single_cover()
	{
		var comicVine = new FakeComicVine();
		comicVine.CoversByIssue[Issue6.Id] = [FakeComicVine.Url("Green")];

		string label = WithPanel(comicVine, true, null, panel =>
		{
			panel.ShowIssue(Issue6);
			ClickMoreCovers(panel);
			Ui.PumpUntil(() => Label(panel).Contains("Single"));
			return Label(panel);
		});

		await Assert.That(label).IsEqualTo("Issue 6  -  Single Cover");
	}

	[Test]
	public async Task A_series_shows_the_cover_of_the_issue_the_book_names()
	{
		var comicVine = new FakeComicVine();
		comicVine.IssuesByNumber[(Batman.Id, "5")] = Issue5;

		string label = WithPanel(comicVine, true, "5", panel =>
		{
			panel.ShowSeries(Batman);
			Ui.PumpUntil(() => Showing(panel, "Red"));
			return Label(panel);
		});

		await Assert.That(label).IsEqualTo("Issue 5 (more covers)");
		await Assert.That(comicVine.IssueLookups.ToArray()).IsEquivalentTo([(Batman.Id, "5")]);
	}

	[Test]
	[Arguments("99")]
	[Arguments(null)]
	public async Task A_series_without_that_issue_or_without_a_number_shows_series_art(string? hint)
	{
		var comicVine = new FakeComicVine();

		string label = WithPanel(comicVine, true, hint, panel =>
		{
			panel.ShowSeries(Batman);
			Ui.PumpUntil(() => Showing(panel, "Navy"));
			return Label(panel);
		});

		await Assert.That(label).IsEqualTo("Series Art");
	}

	[Test]
	public async Task The_preferred_cover_is_the_alternate_on_show_and_none_for_the_first_cover()
	{
		var comicVine = new FakeComicVine();
		comicVine.CoversByIssue[Issue5.Id] = [FakeComicVine.Url("Red"), FakeComicVine.Url("Gold")];

		((IssueId, Uri)? AtFirst, (IssueId, Uri)? AtSecond) seen = WithPanel(comicVine, true, null, panel =>
		{
			panel.ShowIssue(Issue5);
			(IssueId, Uri)? atFirst = panel.PreferredCover;
			ClickMoreCovers(panel);
			Ui.PumpUntil(() => Ui.Find<Button>(panel, "nextCover").Visible);
			Ui.Find<Button>(panel, "nextCover").PerformClick();
			return (atFirst, panel.PreferredCover);
		});

		await Assert.That(seen.AtFirst).IsNull();
		await Assert.That(seen.AtSecond).IsEqualTo((Issue5.Id, FakeComicVine.Url("Gold")));
	}

	[Test]
	public async Task The_newest_cover_wins_and_the_one_it_replaced_is_cancelled()
	{
		var comicVine = new FakeComicVine();
		comicVine.Hold(Issue5.ThumbUrl!);

		bool green = WithPanel(comicVine, true, null, panel =>
		{
			panel.ShowIssue(Issue5);
			Ui.PumpUntil(() => comicVine.ImageRequests.Contains(Issue5.ThumbUrl!));
			panel.ShowIssue(Issue6);
			Ui.PumpUntil(() => Showing(panel, "Green"));
			comicVine.Release(Issue5.ThumbUrl!);
			for (int i = 0; i < 20; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			return Showing(panel, "Green");
		});

		await Assert.That(green).IsTrue();
		await Assert.That(comicVine.CancelledImages.ToArray()).IsEquivalentTo([Issue5.ThumbUrl!]);
	}

	[Test]
	public async Task With_covers_hidden_nothing_is_downloaded()
	{
		var comicVine = new FakeComicVine();
		comicVine.IssuesByNumber[(Batman.Id, "5")] = Issue5;

		(bool Visible, string Label) seen = WithPanel(comicVine, false, "5", panel =>
		{
			panel.ShowSeries(Batman);
			panel.ShowIssue(Issue6);
			for (int i = 0; i < 20; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			return (Ui.Find<PictureBox>(panel, "cover").Visible, Label(panel));
		});

		await Assert.That(seen.Visible).IsFalse();
		await Assert.That(comicVine.ImageRequests).IsEmpty();
		await Assert.That(comicVine.IssueLookups).IsEmpty();
	}

	[Test]
	public async Task Nothing_shown_is_the_Comic_Vine_logo_with_no_label()
	{
		var comicVine = new FakeComicVine();

		(Size Image, string Label) seen = WithPanel(comicVine, true, null, panel =>
		{
			panel.ShowIssue(null);
			return (Ui.Find<PictureBox>(panel, "cover").Image.Size, Label(panel));
		});

		await Assert.That(seen.Image).IsEqualTo(new Size(195, 325));
		await Assert.That(seen.Label).IsEqualTo("");
	}
}
