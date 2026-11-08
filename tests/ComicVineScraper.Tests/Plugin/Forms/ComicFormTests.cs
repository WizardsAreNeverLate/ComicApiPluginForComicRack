using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Plugin;

namespace ComicVineScraper.Tests.Plugin.Forms;

/// <summary>A book whose page i is a solid colour, Pages[i], so a test reads which page the user sees.</summary>
internal sealed class ColouredBook(params Color[] pages) : IScrapeBook
{
	public List<int> Asked { get; } = [];

	public Color[] Pages { get; } = pages;

	public BookSnapshot Read() => throw new NotSupportedException();

	public Bitmap? GetPage(int index)
	{
		Asked.Add(index);
		if (index < 0 || index >= Pages.Length)
		{
			return null;
		}
		var page = new Bitmap(60, 90);
		using Graphics g = Graphics.FromImage(page);
		g.Clear(Pages[index]);
		return page;
	}

	public void Apply(BookPatch patch, Bitmap? thumbnail) => throw new NotSupportedException();
}

public sealed class ComicFormTests
{
	private static readonly Color[] FivePages = [Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue];

	/// <summary>Shows a ComicForm, runs act once it is up, closes it, and returns what act returned.</summary>
	private static T WithForm<T>(Func<ComicForm, T> act, bool marked = false) => Sta.Run(() =>
	{
		using var session = UiSession.Create(marked);
		using var form = new ComicForm(session.Ctx);
		T result = default!;
		Ui.WhenShown(form, () =>
		{
			try
			{
				result = act(form);
			}
			finally
			{
				form.Close();
			}
		});
		form.ShowDialog();
		return result;
	});

	private static Color Showing(ComicForm form)
	{
		PictureBox picture = Ui.Find<PictureBox>(form, "page");
		return ((Bitmap)picture.Image).GetPixel(picture.Image.Width / 2, picture.Image.Height / 2);
	}

	[Test]
	public async Task Each_book_updates_the_label_the_progress_and_the_remaining_count()
	{
		(string Label, string Cancel, int Value, int Maximum)[] states = WithForm(form =>
		{
			var seen = new List<(string, string, int, int)>();
			ProgressBar bar = Ui.Find<ProgressBar>(form, "progress");
			string[] names = [@"C:\Comics\Batman 001.cbz", @"C:\Comics\Batman 002.cbz", @"C:\Comics\Batman 003.cbz"];
			for (int i = 0; i < names.Length; i++)
			{
				form.ShowBook(new ColouredBook(Color.Red), TestBooks.Facts(path: names[i]), names.Length - i);
				seen.Add((Ui.Find<Label>(form, "scraping").Text, Ui.Find<Button>(form, "cancel").Text, bar.Value, bar.Maximum));
			}
			return seen.ToArray();
		});

		await Assert.That(states[0]).IsEqualTo(("Scraping: Batman 001.cbz", "Cancel (3 remaining)", 0, 3));
		await Assert.That(states[1]).IsEqualTo(("Scraping: Batman 002.cbz", "Cancel (2 remaining)", 1, 3));
		await Assert.That(states[2]).IsEqualTo(("Scraping: Batman 003.cbz", "Cancel (1 remaining)", 2, 3));
	}

	[Test]
	[Arguments("Batman", "5", 1990, null, "Scraping: Batman #5 (Vol. 1990)")]
	[Arguments("", "5", null, 2001, "Scraping: <unknown> #5 (2001)")]
	[Arguments("Batman", "", null, null, "Scraping: Batman")]
	public async Task A_fileless_book_is_named_from_its_details(string series, string number, int? volume, int? year, string expected)
	{
		string label = WithForm(form =>
		{
			BookFacts facts = TestBooks.Facts(series: series, number: number, path: "", volume: volume);
			facts = facts with { Effective = facts.Effective with { Published = BookDate.Of(year, null, null) } };
			form.ShowBook(new ColouredBook(), facts, 1);
			return Ui.Find<Label>(form, "scraping").Text;
		});

		await Assert.That(label).IsEqualTo(expected);
	}

	[Test]
	public async Task The_cover_is_the_first_page_and_clicking_the_page_zones_turns_the_pages()
	{
		var book = new ColouredBook(FivePages);

		Color[] seen = WithForm(form =>
		{
			form.ShowBook(book, TestBooks.Facts(pages: FivePages.Length), 1);
			Panel panel = Ui.Find<Panel>(form, "pages");
			PictureBox picture = Ui.Find<PictureBox>(form, "page");
			int middle = picture.Top + picture.Height / 2;
			var next = new Point(panel.Width / 2 + 5, middle);
			var previous = new Point(panel.Width / 2 - 5, middle);
			var last = new Point(picture.Right - 2, middle);
			var first = new Point(picture.Left + 2, middle);
			var colours = new List<Color> { Showing(form) };
			foreach (Point at in new[] { next, next, previous, last, previous, first })
			{
				Ui.Click(panel, at);
				colours.Add(Showing(form));
			}
			Ui.MouseMove(panel, next);
			Ui.Render(form, "ComicForm");
			return colours.ToArray();
		});

		Color[] expected = [Color.Red, Color.Orange, Color.Yellow, Color.Orange, Color.Blue, Color.Green, Color.Red];
		await Assert.That(seen.Select(c => c.ToArgb()).SequenceEqual(expected.Select(c => c.ToArgb()))).IsTrue();
	}

	[Test]
	public async Task Clicking_past_either_end_stays_on_that_page()
	{
		var book = new ColouredBook(Color.Red, Color.Blue);

		(Color AtStart, Color AtEnd) seen = WithForm(form =>
		{
			form.ShowBook(book, TestBooks.Facts(pages: 2), 1);
			Panel panel = Ui.Find<Panel>(form, "pages");
			int middle = panel.Height / 2;
			Ui.Click(panel, new Point(panel.Width / 2 - 5, middle));
			Color atStart = Showing(form);
			Ui.Click(panel, new Point(panel.Width / 2 + 5, middle));
			Ui.Click(panel, new Point(panel.Width / 2 + 5, middle));
			return (atStart, Showing(form));
		});

		await Assert.That(seen.AtStart.ToArgb()).IsEqualTo(Color.Red.ToArgb());
		await Assert.That(seen.AtEnd.ToArgb()).IsEqualTo(Color.Blue.ToArgb());
	}

	[Test]
	public async Task A_book_without_a_page_shows_the_logo_and_no_hand_cursor()
	{
		(Size Image, Cursor Cursor) seen = WithForm(form =>
		{
			form.ShowBook(new ColouredBook(), TestBooks.Facts(path: "", pages: 0), 1);
			return (Ui.Find<PictureBox>(form, "page").Image.Size, Ui.Find<Panel>(form, "pages").Cursor);
		});

		await Assert.That(seen.Image).IsEqualTo(new Size(195, 325));
		await Assert.That(seen.Cursor).IsEqualTo(Cursors.Default);
	}

	[Test]
	public async Task A_book_with_pages_to_turn_shows_a_hand_cursor()
	{
		Cursor cursor = WithForm(form =>
		{
			form.ShowBook(new ColouredBook(FivePages), TestBooks.Facts(pages: 5), 1);
			return Ui.Find<Panel>(form, "pages").Cursor;
		});

		await Assert.That(cursor).IsEqualTo(Cursors.Hand);
	}

	[Test]
	public async Task The_form_keeps_its_size_in_geometry_dat()
	{
		using var session = UiSession.Create();
		Rectangle screen = Screen.PrimaryScreen.WorkingArea;
		var size = new Size(400, 640);

		Size reopened = Sta.Run(() =>
		{
			using (var form = new ComicForm(session.Ctx))
			{
				Ui.WhenShown(form, () =>
				{
					form.Bounds = new Rectangle(screen.Left + 20, screen.Top + 20, size.Width, size.Height);
					form.Close();
				});
				form.ShowDialog();
			}
			using var again = new ComicForm(session.Ctx);
			Size seen = Size.Empty;
			Ui.WhenShown(again, () =>
			{
				seen = again.Size;
				again.Close();
			});
			again.ShowDialog();
			return seen;
		});

		await Assert.That(reopened).IsEqualTo(size);
	}

	[Test]
	public async Task Cancel_ends_the_run_the_form_hosts()
	{
		using var session = UiSession.Create();

		RunSummary summary = Sta.Run(() =>
		{
			return ScrapeHost.Run<RunSummary>(null!, session.Ctx, async (form, ct) =>
			{
				form.BeginInvoke(new Action(() => Ui.Find<Button>(form, "cancel").PerformClick()));
				try
				{
					await Task.Delay(Timeout.Infinite, ct);
				}
				catch (OperationCanceledException)
				{
				}
				return new RunSummary(0, 1, ct.IsCancellationRequested);
			});
		});

		await Assert.That(summary.Cancelled).IsTrue();
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		string[] unlocalized = WithForm(form =>
		{
			form.ShowBook(new ColouredBook(FivePages), TestBooks.Facts(pages: 5), 4);
			return Ui.UnlocalizedTexts(form, "Batman 005.cbz").ToArray();
		}, marked: true);

		await Assert.That(unlocalized).IsEmpty();
	}
}
