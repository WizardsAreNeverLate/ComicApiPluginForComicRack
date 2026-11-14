using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.Plugin.Forms;

public sealed class CoverMatchFormTests
{
	private static readonly CoverCandidate First = new(new VerseDbIssueId(1), "Batman", 1940, 1, "5", "Hush", "DC Comics", "1990-06-01", new Uri("https://img.example/1.png"));
	private static readonly CoverCandidate Second = new(new VerseDbIssueId(2), "Batman", null, null, "6", "", "", "", new Uri("https://img.example/2.png"));

	private static CoverLookup Lookup => new(false, [First, Second]);

	private static CoverChoice Ask(Action<CoverMatchForm> act, FakeVerseDbImages? api = null, CancellationToken ct = default) =>
		Sta.Run(() =>
		{
			using var session = UiSession.Create();
			using IDisposable driver = Ui.WhenOpen(act);
			return CoverMatchForm.Ask(null, session.Ctx, api ?? new FakeVerseDbImages(), new PictureBook(), TestBooks.Facts(), Lookup, ct);
		});

	[Test]
	public async Task The_grid_lists_the_candidates_best_first_with_the_series_year()
	{
		string[] rows = [];
		Ask(form =>
		{
			rows = Ui.Find<DataGridView>(form, "candidates").Rows.Cast<DataGridViewRow>()
				.Select(row => string.Join("|", new[] { "series", "number", "title", "publisher", "date" }.Select(c => row.Cells[c].FormattedValue))).ToArray();
			form.Close();
		});

		await Assert.That(rows.SequenceEqual(["Batman (1940)|5|Hush|DC Comics|1990-06-01", "Batman|6|||"])).IsTrue();
	}

	[Test]
	public async Task The_first_candidate_is_selected_and_Select_picks_it()
	{
		CoverChoice choice = Ask(form => Ui.Find<Button>(form, "select").PerformClick());

		await Assert.That(choice).IsEqualTo(new CoverChoice.Picked(First));
	}

	[Test]
	public async Task Selecting_another_row_and_pressing_Select_picks_that_one()
	{
		CoverChoice choice = Ask(form =>
		{
			DataGridView grid = Ui.Find<DataGridView>(form, "candidates");
			grid.CurrentCell = grid.Rows[1].Cells["series"];
			Ui.Find<Button>(form, "select").PerformClick();
		});

		await Assert.That(choice).IsEqualTo(new CoverChoice.Picked(Second));
	}

	[Test]
	public async Task Skip_and_Cancel_and_Escape_answer_Skip_and_Cancel()
	{
		CoverChoice skip = Ask(form => Ui.Find<Button>(form, "skip").PerformClick());
		CoverChoice cancel = Ask(form => Ui.Find<Button>(form, "cancel").PerformClick());
		CoverChoice escape = Ask(form => Ui.PressCommandKey(Ui.Find<Button>(form, "select"), Keys.Escape));

		await Assert.That(skip).IsEqualTo(new CoverChoice.Skip());
		await Assert.That(cancel).IsEqualTo(new CoverChoice.Cancel());
		await Assert.That(escape).IsEqualTo(new CoverChoice.Cancel());
	}

	[Test]
	public async Task A_cancelled_token_answers_Cancel_without_showing_the_form()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		CoverChoice choice = Ask(_ => throw new InvalidOperationException("The form must not open."), ct: cts.Token);

		await Assert.That(choice).IsEqualTo(new CoverChoice.Cancel());
	}

	[Test]
	public async Task The_selected_candidates_cover_is_downloaded_once_and_the_own_cover_shows_on_the_left()
	{
		var api = new FakeVerseDbImages();
		bool ownShown = false;
		Ask(form =>
		{
			Ui.PumpUntil(() => api.Downloads.Count == 1);
			ownShown = Ui.Find<PictureBox>(form, "ownCover").Image is { Width: 20, Height: 30 };
			form.Close();
		}, api);

		await Assert.That(api.Downloads.ToArray()).IsEquivalentTo(new[] { First.CoverUrl! });
		await Assert.That(ownShown).IsTrue();
	}

	[Test]
	public async Task Every_text_is_localized()
	{
		string[] unlocalized = Sta.Run(() =>
		{
			using var session = UiSession.Create(marked: true);
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<CoverMatchForm>(form =>
			{
				found = [.. Ui.UnlocalizedTexts(form, "Batman", "Hush", "DC Comics", "5", "6", "Batman (1940)", "1990-06-01")];
				Ui.Render(form, "CoverMatchForm");
				form.Close();
			});
			CoverMatchForm.Ask(null, session.Ctx, new FakeVerseDbImages(), new PictureBook(), TestBooks.Facts(), Lookup, CancellationToken.None);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}

	private sealed class FakeVerseDbImages : IVerseDb
	{
		public List<Uri> Downloads { get; } = [];

		public Task<byte[]> GetImageAsync(Uri url, CancellationToken ct)
		{
			Downloads.Add(url);
			using var bitmap = new Bitmap(10, 15);
			using var stream = new MemoryStream();
			bitmap.Save(stream, ImageFormat.Png);
			return Task.FromResult(stream.ToArray());
		}

		public Task<CoverLookup> LookUpCoverAsync(byte[] image, CancellationToken ct) => throw new NotSupportedException();

		public Task<IssueMetadata> GetIssueAsync(VerseDbIssueId issue, CancellationToken ct) => throw new NotSupportedException();
	}

	private sealed class PictureBook : IScrapeBook
	{
		public BookSnapshot Read() => TestBooks.Facts().Snapshot;

		public Bitmap? GetPage(int index) => new(20, 30);

		public bool Apply(BookPatch patch, Bitmap? thumbnail) => true;
	}
}
