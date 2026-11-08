using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;
using TUnit.Assertions.Enums;

namespace ComicVineScraper.Tests.ComicVine;

public sealed class ComicVineCatalogTests
{
	private static readonly SearchSettings NoFilters = new([], [], 0, 9999, 9999, 100);

	private static (ComicVineCatalog Catalog, FixtureHandler Handler) Catalog(SearchSettings? settings = null)
	{
		var handler = FixtureHandler.FromOutputFolder();
		var clock = new ManualClock();
		var http = new ComicVineHttp(handler, "test-key", "ua", clock, () => clock.Elapsed);
		return (new ComicVineCatalog(http, settings ?? NoFilters), handler);
	}

	[Test]
	public async Task A_search_stops_at_MaxSearchResults_after_the_first_page()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();
		var progress = new Recorder();

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("Batman", progress, CancellationToken.None);

		await Assert.That(found.Count).IsEqualTo(100);
		await Assert.That(found[1]).IsEqualTo(new SeriesRef(new SeriesId(796), "Batman", 1940, "DC Comics", 716,
			new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/10/103530/3421824-2.png")));
		await Assert.That(handler.Requested.Count).IsEqualTo(1);
		await Assert.That(progress.Reports).IsEquivalentTo(new[] { new QueryProgress(100, 22) }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_search_pages_until_MaxSearchResults_and_reports_progress_per_page()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog(NoFilters with { MaxSearchResults = 200 });
		var progress = new Recorder();

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("batman", progress, CancellationToken.None);

		await Assert.That(found.Count).IsEqualTo(200);
		await Assert.That(found.Select(s => s.Id).Distinct().Count()).IsEqualTo(200);
		await Assert.That(handler.Requested[1]).EndsWith("&query=batman&page=2");
		await Assert.That(progress.Reports).IsEquivalentTo(new[] { new QueryProgress(100, 22), new QueryProgress(200, 22) }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task An_empty_later_page_is_skipped_and_paging_goes_on()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog(NoFilters with { MaxSearchResults = 1000 });

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("emptypage", null, CancellationToken.None);

		await Assert.That(found.Select(s => s.Name)).IsEquivalentTo(new[] { "Empty Page One", "Empty Page Two", "Empty Page Three" }, CollectionOrdering.Matching);
		await Assert.That(handler.Requested.Count).IsEqualTo(3);
	}

	[Test]
	public async Task A_first_page_with_no_results_is_empty_even_when_the_total_is_not()
	{
		(ComicVineCatalog catalog, _) = Catalog();

		await Assert.That(await catalog.SearchSeriesAsync("bugthreetwonine", null, CancellationToken.None)).IsEmpty();
		await Assert.That(await catalog.SearchSeriesAsync("zzqxv qqzzx", null, CancellationToken.None)).IsEmpty();
	}

	[Test]
	public async Task Search_results_are_mapped_like_SeriesRef_including_xml2py_folding()
	{
		(ComicVineCatalog catalog, _) = Catalog();

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("edgecases", null, CancellationToken.None);

		// xml2py reads an element whose only child is a string element as that string, so an image with one URL has no
		// thumb_url (900015) and a publisher with only a name has no publisher (900016).
		Uri Small(int id) => new($"https://comicvine.gamespot.com/a/uploads/scale_small/{id}.jpg");
		await Assert.That(found).IsEquivalentTo(new[]
		{
			new SeriesRef(new SeriesId(900011), "Null Publisher", 2004, "", 3, Small(900011)),
			new SeriesRef(new SeriesId(900012), "Year With Dash", 2004, "Image", 5, Small(900012)),
			new SeriesRef(new SeriesId(900013), "Spaced & Named", 1999, "Vertigo", 7, Small(900013)),
			new SeriesRef(new SeriesId(900014), "Series 900014", 1950, "Marvel", 0, Small(900014)),
			new SeriesRef(new SeriesId(900015), "Thumb Only", null, "Marvel", 0, null),
			new SeriesRef(new SeriesId(900016), "Many Issues 1950", 1950, "", 40, null),
		}, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Ignored_publishers_and_years_filter_results_unless_the_series_is_long()
	{
		(ComicVineCatalog catalog, _) = Catalog(NoFilters with { IgnoredPublishers = ["vertigo", "image"], IgnoreBeforeYear = 1960, NeverIgnoreThreshold = 40 });

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("edgecases", null, CancellationToken.None);

		await Assert.That(found.Select(s => s.Id.Value)).IsEquivalentTo(new[] { 900011, 900015, 900016 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Ignored_search_terms_are_removed_as_whole_words_before_the_search()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog(NoFilters with { IgnoredSearchTerms = ["the", "vol 2", "bat"] });

		await catalog.SearchSeriesAsync("The Batman THE", null, CancellationToken.None);

		await Assert.That(handler.Requested.Single()).EndsWith("&query=batman");
	}

	[Test]
	public async Task Number_words_are_tried_when_the_plain_search_finds_nothing()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("Fantastic 4 c2c", null, CancellationToken.None);

		await Assert.That(found.Single().Name).IsEqualTo("Fantastic Four");
		await Assert.That(handler.Requested.Select(r => r.Substring(r.IndexOf("&query=", StringComparison.Ordinal))))
			.IsEquivalentTo(new[] { "&query=fantastic 4", "&query=fantastic four" }, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("https://comicvine.gamespot.com/batman/4050-796/", 1)]
	[Arguments("cv 4000-37566", 2)]
	[Arguments("4000-999999999 or 4050-796", 3)]
	public async Task A_Comic_Vine_url_in_the_terms_names_the_series(string terms, int requests)
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync(terms, null, CancellationToken.None);

		await Assert.That(found.Single().Id).IsEqualTo(new SeriesId(796));
		await Assert.That(handler.Requested.Count).IsEqualTo(requests);
	}

	[Test]
	public async Task A_repeated_search_comes_from_the_cache_and_the_cache_empties_past_ten_entries()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();
		await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);
		await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);
		await Assert.That(handler.Requested.Count).IsEqualTo(1);

		for (int i = 0; i < 10; i++)
		{
			await catalog.SearchSeriesAsync("batman" + new string(' ', i + 1), null, CancellationToken.None);
		}
		await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);
		int afterEleven = handler.Requested.Count;
		await catalog.SearchSeriesAsync("batman" + new string(' ', 11), null, CancellationToken.None);
		await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);

		await Assert.That(afterEleven).IsEqualTo(11);
		await Assert.That(handler.Requested.Count).IsEqualTo(13);
	}

	[Test]
	public async Task A_cancelled_search_throws_and_is_not_cached()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog(NoFilters with { MaxSearchResults = 200 });
		using var cancel = new CancellationTokenSource();
		var progress = new Recorder(_ => cancel.Cancel());

		await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.SearchSeriesAsync("batman", progress, cancel.Token));
		IReadOnlyList<SeriesRef> again = await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);

		await Assert.That(again.Count).IsEqualTo(200);
		await Assert.That(handler.Requested.Count).IsEqualTo(3);
	}

	[Test]
	public async Task The_issues_of_a_series_page_with_offset_and_report_progress_as_a_fraction()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();
		var progress = new Recorder();

		IReadOnlyList<IssueRef> issues = await catalog.GetIssuesAsync(new SeriesId(91273), progress, CancellationToken.None);

		await Assert.That(issues.Count).IsEqualTo(163);
		await Assert.That(handler.Requested.Last()).EndsWith("&filter=volume:91273&page=2&offset=100");
		await Assert.That(progress.Reports).IsEquivalentTo(new[] { new QueryProgress(100, 163), new QueryProgress(200, 163) }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_series_with_one_issue_returns_it_and_the_last_series_is_cached()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		IReadOnlyList<IssueRef> issues = await catalog.GetIssuesAsync(new SeriesId(51658), null, CancellationToken.None);
		await catalog.GetIssuesAsync(new SeriesId(51658), null, CancellationToken.None);

		await Assert.That(issues.Count).IsEqualTo(1);
		await Assert.That(handler.Requested.Count).IsEqualTo(1);
	}

	[Test]
	public async Task FindIssue_strips_leading_zeros()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		IssueRef? found = await catalog.FindIssueAsync(new SeriesId(796), " 00497 ", CancellationToken.None);

		await Assert.That(found).IsEqualTo(new IssueRef(new IssueId(37566), "497", "The Broken Bat",
			new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/11112/111120209/5511529-batman%20%23497b.jpg")));
		await Assert.That(handler.Requested.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments("0.5", "Half Issue", new[] { ".5", "½" })]
	[Arguments("5.5", "Five And A Half", new[] { "5.5", "5½" })]
	public async Task FindIssue_retries_a_fraction_with_its_symbol(string number, string title, string[] queried)
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		IssueRef? found = await catalog.FindIssueAsync(new SeriesId(9999), number, CancellationToken.None);

		await Assert.That(found?.Title).IsEqualTo(title);
		await Assert.That(handler.Requested.Select(r => r.Substring(r.LastIndexOf(':') + 1))).IsEquivalentTo(queried, CollectionOrdering.Matching);
	}

	[Test]
	public async Task FindIssue_does_not_retry_15_as_one_half_and_needs_exactly_one_match()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		await Assert.That(await catalog.FindIssueAsync(new SeriesId(9999), "15", CancellationToken.None)).IsNull();
		await Assert.That(handler.Requested.Count).IsEqualTo(1);
		await Assert.That(await catalog.FindIssueAsync(new SeriesId(9999), "7", CancellationToken.None)).IsNull();
		await Assert.That(await catalog.FindIssueAsync(new SeriesId(9999), "  ", CancellationToken.None)).IsNull();
		await Assert.That(handler.Requested.Count).IsEqualTo(2);
	}

	[Test]
	public async Task An_issue_maps_credits_roles_dates_summary_and_its_series_details()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		Issue issue = await catalog.GetIssueAsync(new IssueId(37566), includeRating: false, CancellationToken.None);

		await Assert.That(issue.SeriesId).IsEqualTo(new SeriesId(796));
		await Assert.That(issue.SeriesName).IsEqualTo("Batman");
		await Assert.That(issue.VolumeYear).IsEqualTo(1940);
		await Assert.That(issue.Publisher).IsEqualTo("DC Comics");
		await Assert.That(issue.Imprint).IsEqualTo("");
		await Assert.That(issue.Title).IsEqualTo("The Broken Bat");
		await Assert.That(issue.Published).IsEqualTo(PartialDate.Of(1993, 7, 31));
		await Assert.That(issue.Released).IsEqualTo(PartialDate.None);
		await Assert.That(issue.Crossovers).IsEquivalentTo(new[] { "\"Batman\" Knightfall" }, CollectionOrdering.Matching);
		await Assert.That(issue.Pencillers).IsEquivalentTo(new[] { "Jim Aparo", "Kelley Jones" }, CollectionOrdering.Matching);
		await Assert.That(issue.Inkers).IsEquivalentTo(new[] { "Dick Giordano", "Kelley Jones" }, CollectionOrdering.Matching);
		await Assert.That(issue.CoverArtists).IsEquivalentTo(new[] { "Bob LeRose", "Curtis King Jr.", "Kelley Jones" }, CollectionOrdering.Matching);
		await Assert.That(issue.Editors).IsEquivalentTo(new[] { "Curtis King Jr.", "Dennis O'Neil", "Jordan B. Gorfinkel" }, CollectionOrdering.Matching);
		await Assert.That(issue.Webpage).IsEqualTo("https://comicvine.gamespot.com/batman-497-the-broken-bat/4000-37566/");
		await Assert.That(issue.Summary).StartsWith("Batman confronted Bane.");
		await Assert.That(issue.Summary).DoesNotContain("<");
		await Assert.That(issue.CoverUrls.Count).IsEqualTo(1);
		await Assert.That(issue.Rating).IsNull();
		await Assert.That(handler.Requested.Count).IsEqualTo(2);
	}

	[Test]
	public async Task Alternate_covers_come_from_associated_images_and_series_details_are_cached()
	{
		(ComicVineCatalog catalog, FixtureHandler handler) = Catalog();

		Issue first = await catalog.GetIssueAsync(new IssueId(37566), includeRating: true, CancellationToken.None);
		Issue again = await catalog.GetIssueAsync(new IssueId(37566), includeRating: true, CancellationToken.None);
		Issue modern = await catalog.GetIssueAsync(new IssueId(535328), includeRating: true, CancellationToken.None);

		await Assert.That(first.CoverUrls.Select(u => u.OriginalString)).IsEquivalentTo(new[]
		{
			"https://comicvine.gamespot.com/a/uploads/scale_small/11112/111120209/5511529-batman%20%23497b.jpg",
			"https://comicvine.gamespot.com/a/uploads/original/11112/111120209/5511528-batman%20%23497a%20back.jpg",
			"https://comicvine.gamespot.com/a/uploads/original/11112/111120209/5511527-batman%20%23497a.jpg",
		}, CollectionOrdering.Matching);
		await Assert.That(again).IsNotNull();
		await Assert.That(modern.CoverUrls.Count).IsEqualTo(50);
		await Assert.That(modern.Released).IsEqualTo(PartialDate.Of(2016, 6, 15));
		await Assert.That(modern.Summary).DoesNotContain("List of covers");
		await Assert.That(handler.Requested.Count(r => r.Contains("/volume/"))).IsEqualTo(2);
	}

	[Test]
	public async Task Synthetic_issue_covers_imprints_lists_and_bad_dates()
	{
		(ComicVineCatalog catalog, _) = Catalog();

		Issue issue = await catalog.GetIssueAsync(new IssueId(9000001), includeRating: true, CancellationToken.None);
		Issue sparse = await catalog.GetIssueAsync(new IssueId(9000002), includeRating: true, CancellationToken.None);

		await Assert.That(issue.Publisher).IsEqualTo("DC Comics");
		await Assert.That(issue.Imprint).IsEqualTo("Vertigo");
		await Assert.That(issue.VolumeYear).IsNull();
		await Assert.That(issue.SeriesName).IsEqualTo("Imprint Series");
		await Assert.That(issue.IssueNumber).IsEqualTo("0.5");
		await Assert.That(issue.Title).IsEqualTo("Synthetic Title");
		await Assert.That(issue.Published).IsEqualTo(PartialDate.Of(2001, null, null));
		await Assert.That(issue.Released).IsEqualTo(PartialDate.Of(2001, null, null));
		await Assert.That(issue.Webpage).IsEqualTo("");
		await Assert.That(issue.Crossovers).IsEquivalentTo(new[] { "Arc, One" }, CollectionOrdering.Matching);
		await Assert.That(issue.Characters).IsEquivalentTo(new[] { "Solo Character" }, CollectionOrdering.Matching);
		await Assert.That(issue.Teams).IsEquivalentTo(new[] { "TeamA" }, CollectionOrdering.Matching);
		await Assert.That(issue.Writers).IsEquivalentTo(new[] { "Pun Ctuated", "" }, CollectionOrdering.Matching);
		await Assert.That(issue.Pencillers).IsEquivalentTo(new[] { "Art Ist", "Pen Inker" }, CollectionOrdering.Matching);
		await Assert.That(issue.Inkers).IsEquivalentTo(new[] { "Art Ist", "Pen Inker" }, CollectionOrdering.Matching);
		await Assert.That(issue.Colorists).IsEquivalentTo(new[] { "Col Orer" }, CollectionOrdering.Matching);
		await Assert.That(issue.Letterers).IsEquivalentTo(new[] { "Col Orer" }, CollectionOrdering.Matching);
		await Assert.That(issue.CoverArtists).IsEmpty();
		await Assert.That(issue.Summary).IsEqualTo("Line one & more text\nLine two spaced \"quoted\" \U0001F600 emoji ctrl.");
		await Assert.That(issue.CoverUrls.Select(u => u.OriginalString)).IsEquivalentTo(new[] { "https://comicvine.gamespot.com/a/uploads/original/alt1.jpg" }, CollectionOrdering.Matching);
		await Assert.That(sparse.Published).IsEqualTo(PartialDate.Of(1993, 7, null));
		await Assert.That(sparse.Released).IsEqualTo(PartialDate.None);
		await Assert.That(sparse.Title).IsEqualTo("");
		await Assert.That(sparse.Summary).IsEqualTo("");
		await Assert.That(sparse.CoverUrls).IsEmpty();
	}

	[Test]
	public async Task ResolveSeries_finds_a_series_by_its_id_or_by_one_of_its_issues()
	{
		(ComicVineCatalog catalog, _) = Catalog();

		SeriesRef? bySeries = await catalog.ResolveSeriesAsync(new ComicVineTarget.OfSeries(new SeriesId(796)), CancellationToken.None);
		SeriesRef? byIssue = await catalog.ResolveSeriesAsync(new ComicVineTarget.OfIssue(new IssueId(37566)), CancellationToken.None);
		SeriesRef? missing = await catalog.ResolveSeriesAsync(new ComicVineTarget.OfIssue(new IssueId(999999999)), CancellationToken.None);

		await Assert.That(bySeries).IsEqualTo(new SeriesRef(new SeriesId(796), "Batman", 1940, "DC Comics", 716,
			new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/10/103530/3421824-2.png")));
		await Assert.That(byIssue).IsEqualTo(bySeries);
		await Assert.That(missing).IsNull();
	}

	[Test]
	public async Task ResolveSeries_lets_an_invalid_key_end_the_run()
	{
		RecordedResponse invalidKey = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "invalid-key.txt"));
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Reply(invalidKey.Status, invalidKey.Body));
		var catalog = new ComicVineCatalog(new ComicVineHttp(handler, "test-key", "ua", clock, () => clock.Elapsed), NoFilters);

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(
			() => catalog.ResolveSeriesAsync(new ComicVineTarget.OfSeries(new SeriesId(796)), CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(ComicVineError.InvalidApiKey);
	}

	[Test]
	[Arguments(150, 100, 150)]
	[Arguments(149, 100, 74)]
	[Arguments(140, 100, 70)]
	[Arguments(121, 100, 60)]
	[Arguments(120, 100, 120)]
	[Arguments(150, 99, 150)]
	public async Task An_image_of_two_pages_side_by_side_keeps_only_the_front_cover(int width, int height, int expectedWidth)
	{
		var clock = new ManualClock();
		byte[] png = ComicVineHttpTests.Png(width, height);
		var handler = new ScriptedHandler(clock, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
		var catalog = new ComicVineCatalog(new ComicVineHttp(handler, "test-key", "ua", clock, () => clock.Elapsed), NoFilters);

		using Bitmap? image = await catalog.GetImageAsync(new Uri("https://comicvine.gamespot.com/a/1.png"), CancellationToken.None);

		await Assert.That(image!.Width).IsEqualTo(expectedWidth);
		await Assert.That(image.Height).IsEqualTo(height);
	}

	private sealed class Recorder(Action<QueryProgress>? onReport = null) : IProgress<QueryProgress>
	{
		private readonly List<QueryProgress> reports = [];

		public IReadOnlyList<QueryProgress> Reports => reports;

		public void Report(QueryProgress value)
		{
			reports.Add(value);
			onReport?.Invoke(value);
		}
	}
}
