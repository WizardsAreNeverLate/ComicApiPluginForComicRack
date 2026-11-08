using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Engine;
using ComicVineScraper.Matching;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Matching;

public sealed class AutoMatcherTests
{
	private static readonly Uri LeftCover = new("http://covers/left.jpg");
	private static readonly Uri TopCover = new("http://covers/top.jpg");
	private static readonly Uri AlsoLeftCover = new("http://covers/left-again.jpg");

	[Test]
	public async Task Ties_go_to_the_series_that_comes_first_in_Comic_Vine_order()
	{
		SeriesRef first = Series(10, LeftCover), second = Series(20, LeftCover);

		await Assert.That(await Find(Book("5"), new FakeComicVine(first, second), LeftHalfBlack)).IsEqualTo(first);
		await Assert.That(await Find(Book("5"), new FakeComicVine(second, first), LeftHalfBlack)).IsEqualTo(second);
	}

	[Test]
	public async Task A_series_returned_twice_is_one_candidate()
	{
		SeriesRef first = Series(10, LeftCover), second = Series(20, TopCover);
		var comicVine = new FakeComicVine(first, first, second);

		await Assert.That((await Find(Book("1"), comicVine, LeftHalfBlack))?.Id).IsEqualTo(new SeriesId(10));
	}

	[Test]
	public async Task Matches_on_the_issue_cover_when_the_number_is_found()
	{
		var comicVine = new FakeComicVine(Series(10, TopCover)) { Issue = new IssueRef(new IssueId(7), "5", "", LeftCover) };

		await Assert.That((await Find(Book("5"), comicVine, LeftHalfBlack))?.Id).IsEqualTo(new SeriesId(10));
		await Assert.That(comicVine.FoundIssues).IsEquivalentTo(new[] { "10:5" });
	}

	[Test]
	public async Task Matches_on_an_alternate_cover_from_the_issue_details()
	{
		var comicVine = new FakeComicVine(Series(10, TopCover)) { Issue = new IssueRef(new IssueId(7), "5", "", TopCover), Covers = [TopCover, AlsoLeftCover] };

		await Assert.That((await Find(Book("5"), comicVine, LeftHalfBlack))?.Id).IsEqualTo(new SeriesId(10));
	}

	[Test]
	public async Task Gives_up_when_no_cover_looks_like_the_book()
	{
		var comicVine = new FakeComicVine(Series(10, TopCover)) { Issue = new IssueRef(new IssueId(7), "5", "", TopCover), Covers = [TopCover] };

		await Assert.That(await Find(Book("5"), comicVine, LeftHalfBlack)).IsNull();
	}

	[Test]
	public async Task Gives_up_on_a_first_issue_when_the_top_two_series_share_a_cover()
	{
		var comicVine = new FakeComicVine(Series(10, LeftCover), Series(20, AlsoLeftCover));

		await Assert.That(await Find(Book("1"), comicVine, LeftHalfBlack)).IsNull();
		await Assert.That(await Find(Book(""), comicVine, LeftHalfBlack)).IsNull();
		await Assert.That((await Find(Book("2"), comicVine, LeftHalfBlack))?.Id).IsEqualTo(new SeriesId(10));
	}

	[Test]
	public async Task Gives_up_on_a_first_issue_when_the_best_and_third_series_share_a_cover()
	{
		var comicVine = new FakeComicVine(Series(10, LeftCover), Series(20, TopCover), Series(30, AlsoLeftCover));

		await Assert.That(await Find(Book("1.0"), comicVine, LeftHalfBlack)).IsNull();
	}

	[Test]
	public async Task Matches_a_first_issue_when_the_other_series_look_different()
	{
		var comicVine = new FakeComicVine(Series(10, LeftCover), Series(20, TopCover));

		await Assert.That((await Find(Book("1"), comicVine, LeftHalfBlack))?.Id).IsEqualTo(new SeriesId(10));
	}

	[Test]
	public async Task Does_not_match_without_a_page_or_with_a_page_that_hashes_to_zero()
	{
		var comicVine = new FakeComicVine(Series(10, LeftCover));

		await Assert.That(await Find(Book("5"), comicVine, () => null)).IsNull();
		await Assert.That(await Find(Book("5"), comicVine, () => Filled(Color.Black))).IsNull();
		await Assert.That(comicVine.FoundIssues).IsEmpty();
	}

	[Test]
	public async Task Prefers_the_series_with_the_better_score()
	{
		SeriesRef wrongName = new(new SeriesId(10), "Detective Comics", 1940, "DC Comics", 50, TopCover);
		SeriesRef rightName = Series(20, LeftCover);

		await Assert.That(await Find(Book("5"), new FakeComicVine(wrongName, rightName), LeftHalfBlack)).IsEqualTo(rightName);
	}

	private static Task<SeriesRef?> Find(BookValues book, FakeComicVine comicVine, Func<Bitmap?> page) =>
		new AutoMatcher(comicVine, _ => false, 2026).FindAsync(new BookFacts(0, new BookSnapshot(book, "", 1, false), book, new StoredMarker.None(), null, "key"), new FakeBook(page), CancellationToken.None);

	private static BookValues Book(string number) => new() { Series = "Batman", Number = number, Published = PartialDate.Of(1990, null, null) };

	private static SeriesRef Series(int id, Uri thumb) => new(new SeriesId(id), "Batman", 1940, "DC Comics", 50, thumb);

	private static Bitmap? LeftHalfBlack() => Halves(vertical: true);

	private static Bitmap Halves(bool vertical)
	{
		var image = new Bitmap(40, 60);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(Color.White);
		g.FillRectangle(Brushes.Black, 0, 0, vertical ? 20 : 40, vertical ? 60 : 30);
		return image;
	}

	private static Bitmap Filled(Color color)
	{
		var image = new Bitmap(40, 60);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(color);
		return image;
	}

	private sealed class FakeBook(Func<Bitmap?> page) : IScrapeBook
	{
		public BookSnapshot Read() => throw new NotSupportedException();

		public Bitmap? GetPage(int index) => index == 0 ? page() : null;

		public void Apply(BookPatch patch, Bitmap? thumbnail) => throw new NotSupportedException();
	}

	private sealed class FakeComicVine(params SeriesRef[] found) : IComicVine
	{
		public IssueRef? Issue { get; init; }

		public IReadOnlyList<Uri> Covers { get; init; } = [];

		public List<string> FoundIssues { get; } = [];

		public Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct) =>
			Task.FromResult<IReadOnlyList<SeriesRef>>(terms == "Batman" ? found : []);

		public Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct) => throw new NotSupportedException();

		public Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct)
		{
			FoundIssues.Add(series + ":" + issueNumber);
			return Task.FromResult(Issue);
		}

		public Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct) =>
			Task.FromResult(new Issue(issue, new SeriesId(10), "Batman", 1940, "5", "", "", "DC Comics", "", PartialDate.None, PartialDate.None,
				[], [], [], [], [], [], [], [], [], [], [], "", null, Covers));

		public Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct) => throw new NotSupportedException();

		public Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct) =>
			Task.FromResult<Bitmap?>(url == TopCover ? Halves(vertical: false) : url == LeftCover || url == AlsoLeftCover ? Halves(vertical: true) : null);
	}
}
