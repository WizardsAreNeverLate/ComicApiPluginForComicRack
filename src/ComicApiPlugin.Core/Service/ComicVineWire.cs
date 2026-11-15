using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Service;

/// <summary>A body that is not the JSON the contract names: bad JSON, or a required member is missing.</summary>
public sealed class WireFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The boundary between the /comicvine/ JSON bodies and the domain types, for both the plugin and the service. The
/// serializer is the one .NET Framework and .NET both ship, so the plugin needs no JSON package inside ComicRack.
/// </summary>
public static class ComicVineWire
{
	public const string JsonContentType = "application/json";

	/// <summary>JSON Lines: one JSON object on each line.</summary>
	public const string LinesContentType = "application/x-ndjson";

	/// <summary>One line of JSON, with no line break inside it.</summary>
	public static string ToJson<T>(T value) where T : class
	{
		using var stream = new MemoryStream();
		new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <exception cref="WireFormatException">The text is not a JSON object of this type.</exception>
	public static T FromJson<T>(string json) where T : class
	{
		try
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T ?? throw new WireFormatException($"The JSON holds no {typeof(T).Name}.");
		}
		catch (SerializationException error)
		{
			throw new WireFormatException($"The JSON is not a {typeof(T).Name}: {error.Message}", error);
		}
	}

	public static SearchSettingsDto ToDto(SearchSettings settings) => new()
	{
		IgnoredSearchTerms = [.. settings.IgnoredSearchTerms],
		IgnoredPublishers = [.. settings.IgnoredPublishers],
		IgnoreBeforeYear = settings.IgnoreBeforeYear,
		IgnoreAfterYear = settings.IgnoreAfterYear,
		NeverIgnoreThreshold = settings.NeverIgnoreThreshold,
		MaxSearchResults = settings.MaxSearchResults,
	};

	public static SearchSettings ToDomain(SearchSettingsDto? dto)
	{
		SearchSettingsDto settings = Need(dto, "settings");
		return new SearchSettings(
			Strings(settings.IgnoredSearchTerms, "settings.ignoredSearchTerms"),
			Strings(settings.IgnoredPublishers, "settings.ignoredPublishers"),
			settings.IgnoreBeforeYear,
			settings.IgnoreAfterYear,
			settings.NeverIgnoreThreshold,
			settings.MaxSearchResults);
	}

	public static SeriesDto ToDto(SeriesRef series) => new()
	{
		Id = series.Id.Value,
		Name = series.Name,
		StartYear = series.StartYear,
		Publisher = series.Publisher,
		IssueCount = series.IssueCount,
		ThumbUrl = series.ThumbUrl?.OriginalString,
	};

	public static SeriesRef ToDomain(SeriesDto dto) => new(
		new SeriesId(dto.Id),
		Need(dto.Name, "series.name"),
		dto.StartYear,
		Need(dto.Publisher, "series.publisher"),
		dto.IssueCount,
		OptionalUrl(dto.ThumbUrl, "series.thumbUrl"));

	public static IssueRefDto ToDto(IssueRef issue) => new()
	{
		Id = issue.Id.Value,
		IssueNumber = issue.IssueNumber,
		Title = issue.Title,
		ThumbUrl = issue.ThumbUrl?.OriginalString,
	};

	public static IssueRef ToDomain(IssueRefDto dto) => new(
		new IssueId(dto.Id),
		Need(dto.IssueNumber, "issue.issueNumber"),
		Need(dto.Title, "issue.title"),
		OptionalUrl(dto.ThumbUrl, "issue.thumbUrl"));

	public static IssueDto ToDto(Issue issue) => new()
	{
		Id = issue.Id.Value,
		SeriesId = issue.SeriesId.Value,
		SeriesName = issue.SeriesName,
		VolumeYear = issue.VolumeYear,
		IssueNumber = issue.IssueNumber,
		Title = issue.Title,
		Summary = issue.Summary,
		Publisher = issue.Publisher,
		Imprint = issue.Imprint,
		Published = ToDto(issue.Published),
		Released = ToDto(issue.Released),
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
		CoverUrls = [.. issue.CoverUrls.Select(url => url.OriginalString)],
	};

	public static Issue ToDomain(IssueDto dto) => new(
		new IssueId(dto.Id),
		new SeriesId(dto.SeriesId),
		Need(dto.SeriesName, "seriesName"),
		dto.VolumeYear,
		Need(dto.IssueNumber, "issueNumber"),
		Need(dto.Title, "title"),
		Need(dto.Summary, "summary"),
		Need(dto.Publisher, "publisher"),
		Need(dto.Imprint, "imprint"),
		ToDomain(dto.Published, "published"),
		ToDomain(dto.Released, "released"),
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
		dto.Rating,
		[.. Strings(dto.CoverUrls, "coverUrls").Select(url => Url(url, "coverUrls"))]);

	public static ProgressDto ToDto(QueryProgress progress) => new() { Done = progress.Done, Total = progress.Total };

	public static QueryProgress ToDomain(ProgressDto dto) => new(dto.Done, dto.Total);

	/// <summary>The target a resolve-series body names.</summary>
	public static ComicVineTarget ToDomain(ResolveSeriesRequestDto dto) => (dto.Series, dto.Issue) switch
	{
		({ } series, null) => new ComicVineTarget.OfSeries(new SeriesId(series)),
		(null, { } issue) => new ComicVineTarget.OfIssue(new IssueId(issue)),
		_ => throw new WireFormatException("Exactly one of 'series' and 'issue' is required."),
	};

	public static ComicVineErrorDto ToDto(ComicVineException error) => new()
	{
		Kind = error.Error.ToString(),
		Url = error.Url,
		ApiStatus = error.ApiStatus,
		Detail = error.InnerException?.Message,
	};

	/// <summary>The exception the service caught, with the same kind, URL and status. The transport failure behind it comes back as its message only.</summary>
	public static ComicVineException ToDomain(ComicVineErrorDto dto)
	{
		if (!Enum.TryParse(Need(dto.Kind, "error.kind"), out ComicVineError kind) || !Enum.IsDefined(typeof(ComicVineError), kind))
		{
			throw new WireFormatException($"'{dto.Kind}' is not a Comic Vine error kind.");
		}
		return new ComicVineException(kind, Need(dto.Url, "error.url"), dto.ApiStatus, dto.Detail is { } detail ? new IOException(detail) : null);
	}

	/// <exception cref="WireFormatException">The member is missing.</exception>
	public static T Need<T>(T? value, string name) where T : class => value ?? throw new WireFormatException($"'{name}' is required.");

	/// <exception cref="WireFormatException">The text is not an absolute URL.</exception>
	public static Uri Url(string text, string name) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? url) ? url : throw new WireFormatException($"'{name}' is not an absolute URL.");

	private static Uri? OptionalUrl(string? text, string name) => text is null ? null : Url(text, name);

	private static IReadOnlyList<string> Strings(string[]? values, string name) =>
		[.. Need(values, name).Select(value => Need(value, name))];

	private static DateDto ToDto(PartialDate date) => new() { Year = date.Year, Month = date.Month, Day = date.Day };

	private static PartialDate ToDomain(DateDto? dto, string name)
	{
		DateDto date = Need(dto, name);
		return PartialDate.Of(date.Year, date.Month, date.Day);
	}
}
