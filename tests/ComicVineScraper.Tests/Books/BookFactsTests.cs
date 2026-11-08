using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Books;

public sealed class BookFactsTests
{
	private static BookSnapshot Snapshot(BookValues values, string path = @"C:\Comics\Batman 012 (1990).cbz") => new(values, path, 24, false);

	[Test]
	public async Task Blank_name_fields_are_filled_from_the_file_name()
	{
		List<string> parsed = [];
		BookFacts facts = BookFacts.From(0, Snapshot(new BookValues()), name => { parsed.Add(name); return ("Batman", "12", "1990"); }, ignoreFolders: false);

		await Assert.That(parsed).IsEquivalentTo(["Batman 012 (1990).cbz"]);
		await Assert.That(facts.Effective.Series).IsEqualTo("Batman");
		await Assert.That(facts.Effective.Number).IsEqualTo("12");
		await Assert.That(facts.Effective.Published).IsEqualTo(BookDate.Of(1990, null, null));
	}

	[Test]
	public async Task Known_name_fields_are_kept()
	{
		var values = new BookValues { Series = "Detective", Number = "5", Published = BookDate.Of(2001, 3, null) };

		BookFacts facts = BookFacts.From(0, Snapshot(values), _ => ("Batman", "12", "1990"), ignoreFolders: false);

		await Assert.That(facts.Effective.Series).IsEqualTo("Detective");
		await Assert.That(facts.Effective.Number).IsEqualTo("5");
		await Assert.That(facts.Effective.Published).IsEqualTo(BookDate.Of(2001, 3, null));
	}

	[Test]
	public async Task A_parsed_year_keeps_the_month_the_book_already_had()
	{
		var values = new BookValues { Series = "Detective", Number = "5", Published = BookDate.Of(null, 4, null) };

		BookFacts facts = BookFacts.From(0, Snapshot(values), _ => ("", "", " 1990 "), ignoreFolders: false);

		await Assert.That(facts.Effective.Published).IsEqualTo(BookDate.Of(1990, 4, null));
	}

	[Test]
	[Arguments("")]
	[Arguments("19x0")]
	[Arguments("0")]
	public async Task A_year_that_is_no_year_stays_blank(string year)
	{
		BookFacts facts = BookFacts.From(0, Snapshot(new BookValues()), _ => ("Batman", "12", year), ignoreFolders: false);

		await Assert.That(facts.Effective.Published.Year).IsNull();
	}

	[Test]
	public async Task A_fileless_book_is_not_parsed()
	{
		BookFacts facts = BookFacts.From(0, Snapshot(new BookValues(), path: ""), _ => throw new InvalidOperationException(), ignoreFolders: false);

		await Assert.That(facts.Effective.Series).IsEqualTo("");
	}

	[Test]
	public async Task The_marker_the_series_id_and_the_series_key_come_from_the_effective_values()
	{
		var values = new BookValues { Tags = ["CVDB77"], SeriesKey = "796.0", Volume = 1990 };

		BookFacts facts = BookFacts.From(3, Snapshot(values), _ => ("Batman", "12", "1990"), ignoreFolders: true);

		await Assert.That(facts.Index).IsEqualTo(3);
		await Assert.That(facts.Marker).IsEqualTo(new StoredMarker.Issue(new IssueId(77)));
		await Assert.That(facts.StoredSeries).IsEqualTo(new SeriesId(796));
		await Assert.That(facts.UniqueSeriesKey).IsEqualTo(UniqueSeriesKey.Compute("Batman", "", 1990, @"C:\Comics\Batman 012 (1990).cbz", true, 3));
	}
}
