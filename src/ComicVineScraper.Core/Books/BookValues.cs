using System.Collections.Generic;
using ComicVineScraper.Model;

namespace ComicVineScraper.Books;

/// <summary>All values the scraper reads or writes, in engine types. Lists are already split; blanks are empty or null.</summary>
public sealed record BookValues
{
	public string Series { get; init; } = "";
	public string Number { get; init; } = "";
	public int? Volume { get; init; }
	public string Format { get; init; } = "";
	public string Title { get; init; } = "";
	public IReadOnlyList<string> Crossovers { get; init; } = [];
	public string Summary { get; init; } = "";
	public string Publisher { get; init; } = "";
	public string Imprint { get; init; } = "";
	public IReadOnlyList<string> Characters { get; init; } = [];
	public IReadOnlyList<string> Teams { get; init; } = [];
	public IReadOnlyList<string> Locations { get; init; } = [];
	public IReadOnlyList<string> Writers { get; init; } = [];
	public IReadOnlyList<string> Pencillers { get; init; } = [];
	public IReadOnlyList<string> Inkers { get; init; } = [];
	public IReadOnlyList<string> Colorists { get; init; } = [];
	public IReadOnlyList<string> Letterers { get; init; } = [];
	public IReadOnlyList<string> CoverArtists { get; init; } = [];
	public IReadOnlyList<string> Editors { get; init; } = [];
	public IReadOnlyList<string> Tags { get; init; } = [];
	public string Notes { get; init; } = "";
	public string Webpage { get; init; } = "";

	/// <summary>CommunityRating 0 reads as null (Python's blank is 0.0).</summary>
	public float? Rating { get; init; }

	/// <summary>Raw text of the custom value comicvine_issue.</summary>
	public string IssueKey { get; init; } = "";

	/// <summary>Raw text of the custom value comicvine_volume.</summary>
	public string SeriesKey { get; init; } = "";

	/// <summary>An unset ReleasedTime reads as blank (deviations.md #11). Stays PartialDate because ComicRack stores it as a full DateTime, so only the issue date can lack a part, unlike Published.</summary>
	public PartialDate Released { get; init; }

	/// <summary>Year, Month, and Day as the book holds them, each kept or dropped on its own (BookDate).</summary>
	public BookDate Published { get; init; }
}

/// <summary>What the adapter reads from one ComicRack book, on the UI thread, in one synchronous call.</summary>
public sealed record BookSnapshot(
	BookValues Values,
	string FilePath,           // "" for fileless books
	int PageCount,
	bool HasCustomThumbnail);  // CustomThumbnailKey is set

/// <summary>One write: every flag in Fields takes its value from Values. Date bits obey DateGate.</summary>
public sealed record BookPatch(BookValues Values, BookFields Fields);
