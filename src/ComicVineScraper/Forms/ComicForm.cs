using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>
/// The window that hosts a scrape: overall progress, the book being scraped, its pages, and Cancel. ScrapeHost shows it
/// modally and turns any close into a cancel of the run.
/// </summary>
internal sealed class ComicForm : ScraperForm
{
	private readonly ProgressBar progress = new()
	{
		Name = "progress",
		Minimum = 0,
		Maximum = 0,
		Step = 1,
		Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
		Location = new Point(13, 15),
		Size = new Size(320, 20),
	};
	private readonly Label scraping = new()
	{
		Name = "scraping",
		UseMnemonic = false,
		AutoSize = false,
		Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
		Location = new Point(13, 45),
		Size = new Size(320, 15),
	};
	private readonly BookPagePanel pages = new()
	{
		Name = "pages",
		Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
		Location = new Point(13, 65),
		Size = new Size(320, 496),
	};
	private readonly Button cancel = new()
	{
		Name = "cancel",
		Anchor = AnchorStyles.Bottom,
		Location = new Point(78, 572),
		Size = new Size(190, 23),
		TabIndex = 0,
	};

	public ComicForm(PluginContext ctx) : base(ctx, "comicform", persistSize: true)
	{
		Text = ProductTitle;
		ClientSize = new Size(346, 604);
		MinimumSize = new Size(166, 275);
		FormBorderStyle = FormBorderStyle.Sizable;
		cancel.Click += (_, _) =>
		{
			cancel.Enabled = false;
			Close();
		};
		Controls.AddRange([progress, scraping, pages, cancel]);
	}

	/// <summary>The engine starts a book: its name, its first page, one more step of progress, and how many books are left, this one included.</summary>
	public void ShowBook(IScrapeBook book, BookFacts facts, int remaining)
	{
		// Python concatenated the label as ComicRack returned it, which is untrimmed (en.zip has "Scraping:  "), and fr.zip has no trailing space at all.
		scraping.Text = Strings[TextKeys.ComicFormScrapingLabel].TrimEnd() + " " + Describe(facts);
		pages.ShowBook(book, facts.Snapshot.PageCount);
		progress.PerformStep();
		progress.Maximum = progress.Value + remaining;
		cancel.Text = Strings.Format(TextKeys.ComicFormCancelButton, remaining);
	}

	/// <summary>Cancel was asked for; the form stays up until the run observes it.</summary>
	public void ShowCancelling() => cancel.Enabled = false;

	/// <summary>What the user's own close does: ScrapeHost cancels the run. The progress window uses it when the user closes that.</summary>
	public void RequestCancel() => Close();

	/// <summary>Python's ComicForm.CenterToParent: one form width left of centre, so the dialogs it owns do not cover it.</summary>
	protected override void PlaceByDefault()
	{
		base.PlaceByDefault();
		Location = new Point(Location.X - Width, Location.Y);
	}

	/// <summary>The file name, or for a fileless book "Series #N (Vol. 1990)" or "Series #N (2001)".</summary>
	private string Describe(BookFacts facts)
	{
		string file = Path.GetFileName(facts.Snapshot.FilePath.Trim());
		if (file.Length > 0)
		{
			return file;
		}
		BookValues book = facts.Effective;
		string name = book.Series.Length > 0 ? book.Series : "<" + Strings[TextKeys.ComicFormUnknown] + ">";
		if (book.Number.Length > 0)
		{
			name += " #" + book.Number;
		}
		if (book.Volume is >= 0 and var volume)
		{
			name += " (" + Strings[TextKeys.ComicFormVolume] + " " + volume + ")";
		}
		else if (book.Published.Year is >= 0 and var year)
		{
			name += " (" + year + ")";
		}
		return name;
	}
}
