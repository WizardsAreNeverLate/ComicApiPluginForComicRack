using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Matching;
using TUnit.Assertions.Enums;

namespace ComicApiPlugin.Tests.Engine;

/// <summary>
/// The Comic Vine cover auto-accept, through ScrapeRun. "Batman 1 (2016)" ranks 91273 (a recorded series with a recorded issue 1)
/// first, then 49597 and 48729, which these tests answer with synthetic issue lists. Each candidate's cover is a bitmap whose
/// width says which one it is, so the fake comparer can decide per candidate.
/// </summary>
public sealed class CoverAutoAcceptRunTests
{
	private const int RealWidth = 99;
	private const int SyntheticWidth = 20;
	private const string Batman1 = @"C:\Comics\Batman 1 (2016).cbz";

	[Test]
	public async Task A_single_matching_cover_picks_the_series_without_the_series_dialog()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth]);

		RunSummary summary = await s.Run();

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(s.World.Ui.Asked).IsEmpty();
		await Assert.That(s.Comparer.Calls).IsEqualTo(3);
		await Assert.That(s.Book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		await Assert.That(s.World.SeriesDat()).IsEquivalentTo(new[] { "91273" });
		await Assert.That(s.Lines.Count(l => l.StartsWith("cover check: Batman #1: ", StringComparison.Ordinal))).IsEqualTo(3);
		await Assert.That(s.Lines).Contains("cover auto-accept: chose Batman");
	}

	[Test]
	public async Task Two_matching_covers_leave_the_choice_to_the_user()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth, SyntheticWidth]);
		s.World.Ui.Series(new SeriesChoice.Skip());

		RunSummary summary = await s.Run();

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task No_matching_cover_opens_the_series_dialog()
	{
		using Scenario s = Setup(count: 3, matching: []);
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.Lines).Contains("cover auto-accept: 0 of 3 covers match, so the series is not chosen automatically.");
	}

	[Test]
	public async Task With_the_setting_off_the_dialog_opens_and_no_cover_is_downloaded()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth], enabled: false);
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.Comparer.Calls).IsEqualTo(0);
		await Assert.That(s.World.Requests.Any(r => !r.Contains("/api/"))).IsFalse();
		await Assert.That(s.World.ApiPaths().Any(p => p.StartsWith("issues/", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task Without_a_comparer_the_dialog_opens_even_with_the_setting_on()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth]);
		s.World.Comparer = null;
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.World.Requests.Any(r => !r.Contains("/api/"))).IsFalse();
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Only_the_top_N_series_are_looked_up(int count)
	{
		using Scenario s = Setup(count, matching: []);
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.ApiPaths().Count(p => p.StartsWith("issues/volume:", StringComparison.Ordinal))).IsEqualTo(count);
		await Assert.That(s.Comparer.Calls).IsEqualTo(count);
	}

	[Test]
	public async Task A_comparer_that_cannot_load_OpenCV_falls_back_to_the_dialog()
	{
		using Scenario s = Setup(count: 3, matching: []);
		s.Comparer.Throw = new OpenCvUnavailableException("OpenCvSharpExtern.dll was not found.");
		s.World.Ui.Series(new SeriesChoice.Skip());

		RunSummary summary = await s.Run();

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.Lines.Any(l => l.StartsWith("cover auto-accept is unavailable: OpenCvSharpExtern.dll", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task A_comparer_that_fails_on_a_cover_counts_it_as_no_match()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth]);
		s.Comparer.Throw = new InvalidOperationException("bad image");
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.Lines).Contains("cover check: Batman #1: the comparison failed: bad image");
	}

	[Test]
	public async Task A_book_without_a_number_opens_the_dialog()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth], file: @"C:\Comics\Batman (2016).cbz");
		s.World.Ui.Series(new SeriesChoice.Skip());

		await s.Run();

		await Assert.That(s.World.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(s.Comparer.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task Going_back_from_the_issue_dialog_asks_for_the_series_instead_of_picking_it_again()
	{
		using Scenario s = Setup(count: 3, matching: [RealWidth]);
		s.World.Config = s.World.Config with { Choose = ChooseMode.ConfirmIssue };
		s.World.Ui.Issue(new IssueChoice.Back()).Series(new SeriesChoice.Skip());

		RunSummary summary = await s.Run();

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(s.World.Ui.Asked.Count).IsEqualTo(2);
		await Assert.That(s.World.Ui.Asked[0]).StartsWith("issue:91273:");
		await Assert.That(s.World.Ui.Asked[1]).IsEqualTo("series:Batman");
		await Assert.That(s.Comparer.Calls).IsEqualTo(3);
	}

	private static Scenario Setup(int count, int[] matching, bool enabled = true, string file = Batman1)
	{
		var s = new Scenario(file, matching);
		s.World.Config = new Configuration { Advanced = AdvancedSettings.Parse(null), ComicVineCoverAutoAccept = enabled, ComicVineCoverCompareCount = count };
		s.World.Respond(Synthetic.IssueNumberUrl(49597, "1"), Synthetic.IssueList((9400002, "1")));
		s.World.Respond(Synthetic.IssueNumberUrl(48729, "1"), Synthetic.IssueList((9400003, "1")));
		return s;
	}

	/// <summary>An EngineWorld with one book, and a cover per candidate: the recorded issue's is RealWidth wide, a synthetic one's SyntheticWidth.</summary>
	private sealed class Scenario : IDisposable
	{
		private readonly Bitmap firstPage = new(40, 60);

		public Scenario(string file, int[] matching)
		{
			Comparer = new WidthComparer(matching);
			Book = FakeBook.File(file, firstPage: firstPage);
			World.Comparer = Comparer;
			World.ImageFor = url => new Bitmap(url.Contains("/i94") ? SyntheticWidth : RealWidth, 8);
		}

		public EngineWorld World { get; } = new();

		public WidthComparer Comparer { get; }

		public FakeBook Book { get; }

		public IReadOnlyList<string> Lines => World.Log.Snapshot();

		public Task<RunSummary> Run() => World.RunAsync(Book);

		public void Dispose()
		{
			World.Dispose();
			firstPage.Dispose();
		}
	}

	/// <summary>Scores a candidate by the width of its cover, so a test says which candidates match.</summary>
	private sealed class WidthComparer(int[] matching) : ICoverComparer
	{
		private int calls;

		public int Calls => calls;

		public Exception? Throw { get; set; }

		public CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover)
		{
			Interlocked.Increment(ref calls);
			if (Throw is not null)
			{
				throw Throw;
			}
			using var stream = new MemoryStream(candidateCover);
			using Image image = Image.FromStream(stream);
			return matching.Contains(image.Width) ? new CoverSimilarity(0.95, 0.5) : new CoverSimilarity(0.1, 0.01);
		}
	}
}
