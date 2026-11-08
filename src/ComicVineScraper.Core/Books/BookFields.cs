using System;

namespace ComicVineScraper.Books;

/// <summary>Every ComicRack property the scraper may write. A write is a BookPatch: values, plus the fields to write.</summary>
[Flags]
public enum BookFields : long
{
	None = 0,
	Series = 1L << 0,
	Number = 1L << 1,
	Volume = 1L << 2,
	Title = 1L << 3,
	Crossovers = 1L << 4,
	Summary = 1L << 5,
	Publisher = 1L << 6,
	Imprint = 1L << 7,
	Characters = 1L << 8,
	Teams = 1L << 9,
	Locations = 1L << 10,
	Writers = 1L << 11,
	Pencillers = 1L << 12,
	Inkers = 1L << 13,
	Colorists = 1L << 14,
	Letterers = 1L << 15,
	CoverArtists = 1L << 16,
	Editors = 1L << 17,
	Webpage = 1L << 18,
	Rating = 1L << 19,
	Tags = 1L << 20,
	Notes = 1L << 21,
	IssueKey = 1L << 22,
	SeriesKey = 1L << 23,
	Released = 1L << 24,
	PublishedYear = 1L << 25,
	PublishedMonth = 1L << 26,
	PublishedDay = 1L << 27,
	Format = 1L << 28,

	Published = PublishedYear | PublishedMonth | PublishedDay,
}
