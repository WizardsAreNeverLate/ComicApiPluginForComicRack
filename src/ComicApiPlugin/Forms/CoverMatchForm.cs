using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Engine;
using ComicApiPlugin.VerseDb;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The candidates VerseDB found for one book's cover, best first: the book's own cover on the left, the candidates in the
/// middle, and the cover of the selected candidate on the right. Select picks it, Skip leaves the book, Cancel ends the run.
/// </summary>
internal sealed class CoverMatchForm : ScraperForm
{
	private readonly CoverLookup lookup;
	private readonly ButtonGrid grid;
	private readonly CoverPictureBox candidateCover;
	private readonly PictureBox ownCover;
	private readonly Button select;

	private CoverMatchForm(PluginContext ctx, IVerseDb verseDb, IScrapeBook book, BookFacts facts, CoverLookup lookup) : base(ctx, "covermatchform")
	{
		this.lookup = lookup;
		Text = Strings[TextKeys.CoverMatchFormTitle];
		ClientSize = new Size(900, 430);

		select = MakeButton("select", Strings[TextKeys.CoverMatchFormSelect], new Point(215, 395), 90, DialogResult.OK, 1);
		Button skip = MakeButton("skip", Strings[TextKeys.IssueFormSkip], new Point(310, 395), 90, DialogResult.Ignore, 2);
		Button cancel = MakeButton("cancel", Strings[TextKeys.MessageBoxCancel], new Point(405, 395), 90, DialogResult.Cancel, 3);
		AcceptButton = select;
		CancelButton = cancel;

		var message = new Label
		{
			Name = "message",
			UseMnemonic = false,
			Location = new Point(215, 12),
			Size = new Size(675, 34),
			Text = Strings.Format(TextKeys.CoverMatchFormChooseText, Describe(facts)),
		};
		var ownLabel = new Label { Name = "ownLabel", UseMnemonic = false, Location = new Point(10, 30), Size = new Size(195, 16), Text = Strings[TextKeys.CoverMatchFormYourCover] };
		ownCover = new PictureBox { Name = "ownCover", Location = new Point(10, 50), Size = new Size(195, 330), SizeMode = PictureBoxSizeMode.Zoom, Image = book.GetPage(0) };

		grid = new ButtonGrid("candidates", select, new Point(215, 50), new Size(465, 330)) { TabIndex = 0 };
		grid.AddColumn("series", Strings[TextKeys.SeriesFormSeriesCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.Fill);
		grid.AddColumn("number", Strings[TextKeys.IssueFormIssueCol], DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("title", Strings[TextKeys.IssueFormTitleCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.Fill);
		grid.AddColumn("publisher", Strings[TextKeys.SeriesFormPublisherCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("date", Strings[TextKeys.CoverMatchFormDateCol], DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("model", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		for (int i = 0; i < lookup.Candidates.Count; i++)
		{
			CoverCandidate candidate = lookup.Candidates[i];
			string year = candidate.StartYear is > 0 ? " (" + candidate.StartYear.Value.ToString(CultureInfo.InvariantCulture) + ")" : "";
			grid.Rows.Add(candidate.SeriesName + year, candidate.IssueNumber, candidate.Title, candidate.Publisher, candidate.CoverDate, i);
		}
		grid.CellDoubleClick += (_, e) =>
		{
			if (e.RowIndex >= 0)
			{
				select.PerformClick();
			}
		};

		candidateCover = new CoverPictureBox(async (url, ct) => await Download(verseDb, url, ct), ctx)
		{
			Name = "candidateCover",
			Location = new Point(690, 50),
			Size = new Size(200, 330),
			SizeMode = PictureBoxSizeMode.Zoom,
		};

		Controls.AddRange([message, ownLabel, ownCover, grid, candidateCover, select, skip, cancel]);
	}

	/// <summary>The candidate the user picked, Skip, or Cancel. A cancelled token answers Cancel.</summary>
	public static CoverChoice Ask(IWin32Window? owner, PluginContext ctx, IVerseDb verseDb, IScrapeBook book, BookFacts facts, CoverLookup lookup, CancellationToken ct)
	{
		if (lookup.Candidates.Count == 0)
		{
			throw new ArgumentException("The cover dialog needs at least one candidate.", nameof(lookup));
		}
		using var form = new CoverMatchForm(ctx, verseDb, book, facts, lookup);
		return form.ShowModal(owner, ct) switch
		{
			DialogResult.OK when form.grid.SelectedModel is int i => new CoverChoice.Picked(lookup.Candidates[i]),
			DialogResult.Ignore => new CoverChoice.Skip(),
			_ => new CoverChoice.Cancel(),
		};
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		grid.CurrentCell = grid.Rows[0].Cells["series"];
		grid.SelectionChanged += (_, _) => SelectionChanged();
		SelectionChanged();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			ownCover.Image?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void SelectionChanged()
	{
		int? selected = grid.SelectedModel;
		candidateCover.ShowCover(selected is int i ? lookup.Candidates[i].CoverUrl : null);
		select.Enabled = selected is not null;
	}

	/// <summary>Null when the picture cannot be fetched or decoded, which shows the logo.</summary>
	private static async Task<Bitmap?> Download(IVerseDb verseDb, Uri url, CancellationToken ct)
	{
		try
		{
			// VerseDB serves every cover as WebP, which GDI+ cannot read; ComicRack's libwebp converts it and returns other formats unchanged.
			byte[] bytes = WebpImage.ConvertToJpeg(await verseDb.GetImageAsync(url, ct));
			using var image = new Bitmap(new MemoryStream(bytes));
			// A copy, because GDI+ reads a stream-backed bitmap lazily.
			return new Bitmap(image);
		}
		catch (Exception e) when (e is VerseDbException or ArgumentException)
		{
			return null;
		}
	}

	private static string Describe(BookFacts facts)
	{
		string file = Path.GetFileName(facts.Snapshot.FilePath.Trim());
		if (file.Length > 0)
		{
			return file;
		}
		BookValues book = facts.Effective;
		return book.Number.Length > 0 ? book.Series + " #" + book.Number : book.Series;
	}

	private static Button MakeButton(string name, string text, Point location, int width, DialogResult result, int tabIndex) => new()
	{
		Name = name,
		Text = text,
		Location = location,
		Size = new Size(width, 24),
		DialogResult = result,
		TabIndex = tabIndex,
		UseVisualStyleBackColor = true,
	};
}
