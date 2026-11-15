using System;
using System.Linq;
using System.Net;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;
using ComicApiPlugin.VerseDb;
using static ComicApiPlugin.Service.WireJson;

namespace ComicApiPlugin.Service;

/// <summary>The boundary between the /versedb/ and /covers/ JSON bodies and the domain types, for both the plugin and the service.</summary>
public static class VerseDbWire
{
	/// <summary>A cover picture as VerseDB served it. Its bytes are not changed, so the type is not named.</summary>
	public const string ImageContentType = "application/octet-stream";

	public static CoverLookupDto ToDto(CoverLookup lookup) => new()
	{
		ClearMatch = lookup.ClearMatch,
		Candidates = [.. lookup.Candidates.Select(candidate => new CoverCandidateDto
		{
			IssueId = candidate.IssueId.Value,
			SeriesName = candidate.SeriesName,
			StartYear = candidate.StartYear,
			Volume = candidate.Volume,
			IssueNumber = candidate.IssueNumber,
			Title = candidate.Title,
			Publisher = candidate.Publisher,
			CoverDate = candidate.CoverDate,
			CoverUrl = candidate.CoverUrl?.OriginalString,
		})],
	};

	public static CoverLookup ToDomain(CoverLookupDto dto) => new(
		dto.ClearMatch,
		[.. Need(dto.Candidates, "candidates").Select(candidate => new CoverCandidate(
			new VerseDbIssueId(Need(candidate, "candidates").IssueId),
			Need(candidate.SeriesName, "candidate.seriesName"),
			candidate.StartYear,
			candidate.Volume,
			Need(candidate.IssueNumber, "candidate.issueNumber"),
			Need(candidate.Title, "candidate.title"),
			Need(candidate.Publisher, "candidate.publisher"),
			Need(candidate.CoverDate, "candidate.coverDate"),
			OptionalUrl(candidate.CoverUrl, "candidate.coverUrl")))]);

	public static IssueMetadataDto ToDto(IssueMetadata issue) => new()
	{
		SeriesName = issue.SeriesName,
		VolumeYear = issue.VolumeYear,
		IssueNumber = issue.IssueNumber,
		Title = issue.Title,
		Summary = issue.Summary,
		Publisher = issue.Publisher,
		Imprint = issue.Imprint,
		Published = ComicVineWire.ToDto(issue.Published),
		Released = ComicVineWire.ToDto(issue.Released),
		Crossovers = [.. issue.Crossovers],
		Characters = [.. issue.Characters],
		Teams = [.. issue.Teams],
		Locations = [.. issue.Locations],
		Writers = [.. issue.Writers],
		Pencillers = [.. issue.Pencillers],
		Inkers = [.. issue.Inkers],
		Colorists = [.. issue.Colorists],
		Letterers = [.. issue.Letterers],
		CoverArtists = [.. issue.CoverArtists],
		Editors = [.. issue.Editors],
		Webpage = issue.Webpage,
		Rating = issue.Rating,
	};

	public static IssueMetadata ToDomain(IssueMetadataDto dto) => new(
		Need(dto.SeriesName, "seriesName"),
		dto.VolumeYear,
		Need(dto.IssueNumber, "issueNumber"),
		Need(dto.Title, "title"),
		Need(dto.Summary, "summary"),
		Need(dto.Publisher, "publisher"),
		Need(dto.Imprint, "imprint"),
		ComicVineWire.ToDomain(dto.Published, "published"),
		ComicVineWire.ToDomain(dto.Released, "released"),
		Strings(dto.Crossovers, "crossovers"),
		Strings(dto.Characters, "characters"),
		Strings(dto.Teams, "teams"),
		Strings(dto.Locations, "locations"),
		Strings(dto.Writers, "writers"),
		Strings(dto.Pencillers, "pencillers"),
		Strings(dto.Inkers, "inkers"),
		Strings(dto.Colorists, "colorists"),
		Strings(dto.Letterers, "letterers"),
		Strings(dto.CoverArtists, "coverArtists"),
		Strings(dto.Editors, "editors"),
		Need(dto.Webpage, "webpage"),
		dto.Rating);

	public static VerseDbErrorDto ToDto(VerseDbException error) => new()
	{
		Status = (int?)error.Status,
		Message = error.Message,
		RetryAfterSeconds = error.RetryAfter?.TotalSeconds,
	};

	/// <summary>The exception the service caught, with the same status, message and wait.</summary>
	public static VerseDbException ToDomain(VerseDbErrorDto dto) => new(
		(HttpStatusCode?)dto.Status,
		Need(dto.Message, "error.message"),
		dto.RetryAfterSeconds is { } seconds ? TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)) : null);

	public static CompareResponseDto ToDto(CoverSimilarity? similarity) => new()
	{
		Similarity = similarity is { } s ? new CoverSimilarityDto { Correlation = s.Correlation, FeatureMatch = s.FeatureMatch } : null,
	};

	public static CoverSimilarity? ToDomain(CompareResponseDto dto) =>
		dto.Similarity is { } s ? new CoverSimilarity(s.Correlation, s.FeatureMatch) : null;
}
