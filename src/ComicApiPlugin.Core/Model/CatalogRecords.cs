using System;
using System.Collections.Generic;

namespace ComicApiPlugin.Model;

/// <summary>One row of a series search, or a series resolved by id.</summary>
public sealed record SeriesRef(
	SeriesId Id,
	string Name,          // trimmed, "&amp;" decoded; empty becomes "Series {id}"
	int? StartYear,       // Python's -1 is null
	string Publisher,     // trimmed; "" when Comic Vine has none
	int IssueCount,       // max(0, n)
	Uri? ThumbUrl);

/// <summary>One row of a series' issue list.</summary>
public sealed record IssueRef(IssueId Id, string IssueNumber, string Title, Uri? ThumbUrl);

/// <summary>Full issue details, already cleaned: summary stripped, roles mapped, lists free of blanks and ',' ';'.</summary>
public sealed record Issue(
	IssueId Id,
	SeriesId SeriesId,
	string SeriesName,
	int? VolumeYear,
	string IssueNumber,
	string Title,
	string Summary,
	string Publisher,      // parent publisher after cvimprints mapping
	string Imprint,        // "" when none
	PartialDate Published, // cover_date
	PartialDate Released,  // store_date
	IReadOnlyList<string> Crossovers,
	IReadOnlyList<string> Characters,
	IReadOnlyList<string> Teams,
	IReadOnlyList<string> Locations,
	IReadOnlyList<string> Writers,
	IReadOnlyList<string> Pencillers,
	IReadOnlyList<string> Inkers,
	IReadOnlyList<string> Colorists,
	IReadOnlyList<string> Letterers,
	IReadOnlyList<string> CoverArtists,
	IReadOnlyList<string> Editors,
	string Webpage,        // only when it starts with http
	float? Rating,         // only when slow data was requested; 0..5
	IReadOnlyList<Uri> CoverUrls); // preference order small, medium, large, super, thumb; then alternates

/// <summary>What a cvinfo file or a Comic Vine URL names. Parsed by ComicVineUrl.TryParse (phase 3b).</summary>
public abstract record ComicVineTarget
{
	private ComicVineTarget() { }

	/// <summary>4050-N or 49-N.</summary>
	public sealed record OfSeries(SeriesId Id) : ComicVineTarget;

	/// <summary>4000-N, resolved to its series.</summary>
	public sealed record OfIssue(IssueId Id) : ComicVineTarget;
}
