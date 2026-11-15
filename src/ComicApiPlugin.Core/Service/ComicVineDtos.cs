using System.Runtime.Serialization;

namespace ComicApiPlugin.Service;

// The JSON bodies of the service's /comicvine/ endpoints. Both sides compile this file. A member is nullable when JSON
// can leave it out; ComicVineWire turns each body into domain types and rejects one that lacks a required member.

/// <summary>The advanced settings that shape a search. Every request carries them, because they are part of the identity of a run's caches.</summary>
[DataContract]
public sealed class SearchSettingsDto
{
	[DataMember(Name = "ignoredSearchTerms")]
	public string[]? IgnoredSearchTerms { get; set; }

	[DataMember(Name = "ignoredPublishers")]
	public string[]? IgnoredPublishers { get; set; }

	[DataMember(Name = "ignoreBeforeYear")]
	public int IgnoreBeforeYear { get; set; }

	[DataMember(Name = "ignoreAfterYear")]
	public int IgnoreAfterYear { get; set; }

	[DataMember(Name = "neverIgnoreThreshold")]
	public int NeverIgnoreThreshold { get; set; }

	[DataMember(Name = "maxSearchResults")]
	public int MaxSearchResults { get; set; }
}

/// <summary>What every /comicvine/ request body carries.</summary>
[DataContract]
public abstract class ComicVineRequestDto
{
	[DataMember(Name = "settings")]
	public SearchSettingsDto? Settings { get; set; }
}

[DataContract]
public sealed class SearchRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "terms")]
	public string? Terms { get; set; }
}

[DataContract]
public sealed class IssuesRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "series")]
	public int Series { get; set; }
}

[DataContract]
public sealed class FindIssueRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "series")]
	public int Series { get; set; }

	[DataMember(Name = "issueNumber")]
	public string? IssueNumber { get; set; }
}

[DataContract]
public sealed class IssueRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "issue")]
	public int Issue { get; set; }

	[DataMember(Name = "includeRating")]
	public bool IncludeRating { get; set; }
}

/// <summary>Exactly one of Series and Issue is set: a series by its id, or the series of an issue.</summary>
[DataContract]
public sealed class ResolveSeriesRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "series")]
	public int? Series { get; set; }

	[DataMember(Name = "issue")]
	public int? Issue { get; set; }
}

[DataContract]
public sealed class ImageRequestDto : ComicVineRequestDto
{
	[DataMember(Name = "url")]
	public string? Url { get; set; }
}

[DataContract]
public sealed class SeriesDto
{
	[DataMember(Name = "id")]
	public int Id { get; set; }

	[DataMember(Name = "name")]
	public string? Name { get; set; }

	[DataMember(Name = "startYear")]
	public int? StartYear { get; set; }

	[DataMember(Name = "publisher")]
	public string? Publisher { get; set; }

	[DataMember(Name = "issueCount")]
	public int IssueCount { get; set; }

	[DataMember(Name = "thumbUrl")]
	public string? ThumbUrl { get; set; }
}

[DataContract]
public sealed class IssueRefDto
{
	[DataMember(Name = "id")]
	public int Id { get; set; }

	[DataMember(Name = "issueNumber")]
	public string? IssueNumber { get; set; }

	[DataMember(Name = "title")]
	public string? Title { get; set; }

	[DataMember(Name = "thumbUrl")]
	public string? ThumbUrl { get; set; }
}

[DataContract]
public sealed class DateDto
{
	[DataMember(Name = "year")]
	public int? Year { get; set; }

	[DataMember(Name = "month")]
	public int? Month { get; set; }

	[DataMember(Name = "day")]
	public int? Day { get; set; }
}

[DataContract]
public sealed class IssueDto
{
	[DataMember(Name = "id")]
	public int Id { get; set; }

	[DataMember(Name = "seriesId")]
	public int SeriesId { get; set; }

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

	[DataMember(Name = "coverUrls")]
	public string[]? CoverUrls { get; set; }
}

/// <summary>The answer to find-issue. Issue is null when Comic Vine has no single issue with that number.</summary>
[DataContract]
public sealed class FindIssueResponseDto
{
	[DataMember(Name = "issue")]
	public IssueRefDto? Issue { get; set; }
}

/// <summary>The answer to resolve-series. Series is null when Comic Vine does not know the id.</summary>
[DataContract]
public sealed class ResolveSeriesResponseDto
{
	[DataMember(Name = "series")]
	public SeriesDto? Series { get; set; }
}

[DataContract]
public sealed class ProgressDto
{
	[DataMember(Name = "done")]
	public int Done { get; set; }

	[DataMember(Name = "total")]
	public int Total { get; set; }
}

/// <summary>A failed Comic Vine request, as ComicVineException holds it. Url never holds the API key.</summary>
[DataContract]
public sealed class ComicVineErrorDto
{
	/// <summary>A ComicVineError name.</summary>
	[DataMember(Name = "kind")]
	public string? Kind { get; set; }

	[DataMember(Name = "url")]
	public string? Url { get; set; }

	[DataMember(Name = "apiStatus")]
	public int? ApiStatus { get; set; }

	/// <summary>The message of the transport failure behind a network error, when there was one.</summary>
	[DataMember(Name = "detail")]
	public string? Detail { get; set; }
}

/// <summary>The body of a response that is not a success.</summary>
[DataContract]
public sealed class ErrorResponseDto
{
	/// <summary>Set when Comic Vine failed the request.</summary>
	[DataMember(Name = "comicVine")]
	public ComicVineErrorDto? ComicVine { get; set; }

	/// <summary>Set when VerseDB failed the request.</summary>
	[DataMember(Name = "verseDb", EmitDefaultValue = false)]
	public VerseDbErrorDto? VerseDb { get; set; }

	/// <summary>Set when the service cannot load the OpenCV library, so it cannot compare covers. The text says why.</summary>
	[DataMember(Name = "openCvUnavailable", EmitDefaultValue = false)]
	public string? OpenCvUnavailable { get; set; }

	/// <summary>Set when the service failed on an error it did not expect, such as an image OpenCV cannot read. The text names the error.</summary>
	[DataMember(Name = "serviceError", EmitDefaultValue = false)]
	public string? ServiceError { get; set; }

	/// <summary>Set when the service rejected the request itself.</summary>
	[DataMember(Name = "message")]
	public string? Message { get; set; }
}

/// <summary>
/// One line of the search and issues responses, which are JSON Lines. Exactly one member is set. Progress lines come
/// first, and the last line holds the result or the error.
/// </summary>
[DataContract]
public sealed class StreamLineDto
{
	[DataMember(Name = "progress", EmitDefaultValue = false)]
	public ProgressDto? Progress { get; set; }

	[DataMember(Name = "series", EmitDefaultValue = false)]
	public SeriesDto[]? Series { get; set; }

	[DataMember(Name = "issues", EmitDefaultValue = false)]
	public IssueRefDto[]? Issues { get; set; }

	[DataMember(Name = "error", EmitDefaultValue = false)]
	public ComicVineErrorDto? Error { get; set; }
}
