using System.Runtime.Serialization;

namespace ComicApiPlugin.Service;

// The JSON bodies of the service's /versedb/ and /covers/ endpoints. Both sides compile this file. An image travels as
// bytes, never as JSON: in a multipart/form-data part of a request, and as the whole body of a response.

[DataContract]
public sealed class VerseDbIssueRequestDto
{
	[DataMember(Name = "issue")]
	public int Issue { get; set; }
}

[DataContract]
public sealed class VerseDbImageRequestDto
{
	[DataMember(Name = "url")]
	public string? Url { get; set; }
}

[DataContract]
public sealed class CoverCandidateDto
{
	[DataMember(Name = "issueId")]
	public int IssueId { get; set; }

	[DataMember(Name = "seriesName")]
	public string? SeriesName { get; set; }

	[DataMember(Name = "startYear")]
	public int? StartYear { get; set; }

	[DataMember(Name = "volume")]
	public int? Volume { get; set; }

	[DataMember(Name = "issueNumber")]
	public string? IssueNumber { get; set; }

	[DataMember(Name = "title")]
	public string? Title { get; set; }

	[DataMember(Name = "publisher")]
	public string? Publisher { get; set; }

	[DataMember(Name = "coverDate")]
	public string? CoverDate { get; set; }

	[DataMember(Name = "coverUrl")]
	public string? CoverUrl { get; set; }
}

/// <summary>The answer to lookup-cover. The candidates are best first.</summary>
[DataContract]
public sealed class CoverLookupDto
{
	[DataMember(Name = "clearMatch")]
	public bool ClearMatch { get; set; }

	[DataMember(Name = "candidates")]
	public CoverCandidateDto[]? Candidates { get; set; }
}

/// <summary>The answer to /versedb/issue: what a scrape writes into a book.</summary>
[DataContract]
public sealed class IssueMetadataDto
{
	[DataMember(Name = "seriesName")]
	public string? SeriesName { get; set; }

	[DataMember(Name = "volumeYear")]
	public int? VolumeYear { get; set; }

	[DataMember(Name = "issueNumber")]
	public string? IssueNumber { get; set; }

	[DataMember(Name = "title")]
	public string? Title { get; set; }

	[DataMember(Name = "summary")]
	public string? Summary { get; set; }

	[DataMember(Name = "publisher")]
	public string? Publisher { get; set; }

	[DataMember(Name = "imprint")]
	public string? Imprint { get; set; }

	[DataMember(Name = "published")]
	public DateDto? Published { get; set; }

	[DataMember(Name = "released")]
	public DateDto? Released { get; set; }

	[DataMember(Name = "crossovers")]
	public string[]? Crossovers { get; set; }

	[DataMember(Name = "characters")]
	public string[]? Characters { get; set; }

	[DataMember(Name = "teams")]
	public string[]? Teams { get; set; }

	[DataMember(Name = "locations")]
	public string[]? Locations { get; set; }

	[DataMember(Name = "writers")]
	public string[]? Writers { get; set; }

	[DataMember(Name = "pencillers")]
	public string[]? Pencillers { get; set; }

	[DataMember(Name = "inkers")]
	public string[]? Inkers { get; set; }

	[DataMember(Name = "colorists")]
	public string[]? Colorists { get; set; }

	[DataMember(Name = "letterers")]
	public string[]? Letterers { get; set; }

	[DataMember(Name = "coverArtists")]
	public string[]? CoverArtists { get; set; }

	[DataMember(Name = "editors")]
	public string[]? Editors { get; set; }

	[DataMember(Name = "webpage")]
	public string? Webpage { get; set; }

	[DataMember(Name = "rating")]
	public float? Rating { get; set; }
}

/// <summary>A failed VerseDB request, as VerseDbException holds it. The message never holds the API token.</summary>
[DataContract]
public sealed class VerseDbErrorDto
{
	/// <summary>The HTTP status VerseDB answered with, or null when VerseDB was not reached or its answer could not be read.</summary>
	[DataMember(Name = "status")]
	public int? Status { get; set; }

	[DataMember(Name = "message")]
	public string? Message { get; set; }

	/// <summary>How long a 429 asks the client to wait.</summary>
	[DataMember(Name = "retryAfterSeconds")]
	public double? RetryAfterSeconds { get; set; }
}

/// <summary>The two measures of CoverSimilarity.</summary>
[DataContract]
public sealed class CoverSimilarityDto
{
	[DataMember(Name = "correlation")]
	public double Correlation { get; set; }

	[DataMember(Name = "featureMatch")]
	public double FeatureMatch { get; set; }
}

/// <summary>The answer to /covers/compare. Similarity is null when one of the two images cannot be decoded.</summary>
[DataContract]
public sealed class CompareResponseDto
{
	[DataMember(Name = "similarity")]
	public CoverSimilarityDto? Similarity { get; set; }
}
