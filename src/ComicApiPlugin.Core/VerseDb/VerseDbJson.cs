using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.VerseDb;

/// <summary>
/// The boundary where VerseDB's JSON becomes domain records. The DTOs name only the fields the plugin uses, and a
/// null or missing array is an empty one. Anything the serializer cannot read becomes a VerseDbException.
/// </summary>
internal static class VerseDbJson
{
	private static readonly Dictionary<string, Role[]> RoleMap = new(StringComparer.OrdinalIgnoreCase)
	{
		["writer"] = [Role.Writer],
		["penciller"] = [Role.Penciller],
		["penciler"] = [Role.Penciller],
		["artist"] = [Role.Penciller, Role.Inker],
		["inker"] = [Role.Inker],
		["colorist"] = [Role.Colorist],
		["colorer"] = [Role.Colorist],
		["letterer"] = [Role.Letterer],
		["cover artist"] = [Role.CoverArtist],
		["cover"] = [Role.CoverArtist],
		["editor"] = [Role.Editor],
	};

	private static readonly Regex Paragraph = new("<[bB][rR] ?/?>|</[Pp] ?>", RegexOptions.CultureInvariant);
	private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);
	private static readonly Regex Date = new(@"^\s*(\d{4})(?:-(\d{1,2}))?(?:-(\d{1,2}))?", RegexOptions.CultureInvariant);

	private enum Role
	{
		Writer,
		Penciller,
		Inker,
		Colorist,
		Letterer,
		CoverArtist,
		Editor,
	}

	public static CoverLookup ParseCoverLookup(string json)
	{
		CoverLookupDto dto = Read<CoverLookupDto>(json);
		return new CoverLookup(dto.ClearMatch, [.. (dto.Candidates ?? []).Where(c => c?.Issue is not null).Select(ToCandidate)]);
	}

	public static IssueMetadata ParseIssue(string json)
	{
		IssueDto issue = Read<IssueEnvelope>(json).Data ?? throw new VerseDbException(null, "VerseDB's answer held no issue.");
		Dictionary<Role, List<string>> roles = Enum.GetValues(typeof(Role)).Cast<Role>().ToDictionary(r => r, _ => new List<string>());
		foreach (CreatorDto creator in issue.Creators ?? [])
		{
			string? name = creator?.Name?.Trim();
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}
			foreach (string role in (creator!.Role ?? "").Split(',').Select(r => r.Trim()))
			{
				if (RoleMap.TryGetValue(role, out Role[]? mapped))
				{
					foreach (Role target in mapped.Where(t => !roles[t].Contains(name!)))
					{
						roles[target].Add(name!);
					}
				}
			}
		}

		string rawSummary = issue.Solicitation ?? "";
		return new IssueMetadata(
			SeriesName: Clean(issue.Series?.Name),
			// Like Comic Vine's volume year: ComicRack's Volume holds the series' start year, not VerseDB's volume_number.
			VolumeYear: issue.Series?.StartYear,
			IssueNumber: Text(issue.IssueNumber),
			Title: Clean(issue.Name),
			Summary: StripHtml(rawSummary),
			Publisher: Clean(issue.Publisher?.Name),
			Imprint: Clean(issue.Imprint?.Name),
			Published: ParseDate(issue.CoverDate),
			Released: ParseDate(issue.ReleaseDate),
			Crossovers: Names(issue.StoryArcs),
			Characters: Names(issue.Characters),
			Teams: Names(issue.Teams),
			Locations: LocationNames(issue.Locations),
			Writers: roles[Role.Writer],
			Pencillers: roles[Role.Penciller],
			Inkers: roles[Role.Inker],
			Colorists: roles[Role.Colorist],
			Letterers: roles[Role.Letterer],
			CoverArtists: roles[Role.CoverArtist],
			Editors: roles[Role.Editor],
			// versedb.com/issues/{slug}, /issue/{slug} and /comics/{slug} all 404, so there is no page to link to.
			Webpage: "",
			Rating: issue.AverageRating is >= 0 and <= 5 ? (float)issue.AverageRating : null);
	}

	/// <summary>The message VerseDB puts in an error body, or null.</summary>
	public static string? ParseErrorMessage(string json)
	{
		try
		{
			return Clean(Read<ErrorDto>(json).Message) is { Length: > 0 } message ? message : null;
		}
		catch (VerseDbException)
		{
			return null;
		}
	}

	internal static PartialDate ParseDate(string? text)
	{
		Match match = Date.Match(text ?? "");
		return match.Success
			? PartialDate.Of(Number(match.Groups[1]), Number(match.Groups[2]), Number(match.Groups[3]))
			: PartialDate.None;
	}

	internal static string StripHtml(string html)
	{
		string text = Paragraph.Replace(html, "\n");
		text = Tag.Replace(text, "");
		return WebUtility.HtmlDecode(text).Replace("\r", "").Trim();
	}

	private static int? Number(Group group) =>
		group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : null;

	private static CoverCandidate ToCandidate(CandidateDto candidate)
	{
		CandidateIssueDto issue = candidate.Issue!;
		SeriesDto? series = issue.Series;
		return new CoverCandidate(
			new VerseDbIssueId(candidate.IssueId != 0 ? candidate.IssueId : issue.Id),
			Clean(series?.Name),
			series?.StartYear,
			series?.VolumeNumber,
			Text(issue.IssueNumber),
			Clean(issue.Name),
			Clean(series?.PublisherName),
			Clean(issue.CoverDate),
			FirstUri(issue.Images?.CoverMd, issue.CoverUrl, issue.Images?.Thumb));
	}

	private static Uri? FirstUri(params string?[] candidates) =>
		candidates.Select(c => Uri.TryCreate(c, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null)
			.FirstOrDefault(uri => uri is not null);

	private static string Clean(string? text) => text?.Trim() ?? "";

	/// <summary>A value VerseDB documents as a string; a bare JSON number is accepted too.</summary>
	private static string Text(object? value) => value switch
	{
		string text => Clean(text),
		IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
		_ => "",
	};

	private static IReadOnlyList<string> Names(NamedDto[]? items) =>
		[.. (items ?? []).Select(i => Clean(i?.Name)).Where(n => n.Length > 0).Distinct()];

	/// <summary>VerseDB's locations is null, a string, or a list of strings (the spec and the live API disagree). The serializer cannot read objects inside it, so those are ignored.</summary>
	private static IReadOnlyList<string> LocationNames(object? locations) => locations switch
	{
		string text => [.. text.Split(',', ';').Select(Clean).Where(n => n.Length > 0).Distinct()],
		object[] items => [.. items.OfType<string>().Select(Clean).Where(n => n.Length > 0).Distinct()],
		_ => [],
	};


	private static T Read<T>(string json)
	{
		try
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!;
		}
		catch (Exception e) when (e is SerializationException or ArgumentException or InvalidCastException or NullReferenceException)
		{
			throw new VerseDbException(null, "VerseDB sent an answer this plugin cannot read.", inner: e);
		}
	}

	[DataContract]
	private sealed class CoverLookupDto
	{
		[DataMember(Name = "clear_match")] public bool ClearMatch { get; set; }
		[DataMember(Name = "candidates")] public CandidateDto[]? Candidates { get; set; }
	}

	[DataContract]
	private sealed class CandidateDto
	{
		[DataMember(Name = "issue_id")] public int IssueId { get; set; }
		[DataMember(Name = "issue")] public CandidateIssueDto? Issue { get; set; }
	}

	[DataContract]
	private sealed class CandidateIssueDto
	{
		[DataMember(Name = "id")] public int Id { get; set; }
		[DataMember(Name = "issue_number")] public object? IssueNumber { get; set; }
		[DataMember(Name = "name")] public string? Name { get; set; }
		[DataMember(Name = "cover_date")] public string? CoverDate { get; set; }
		[DataMember(Name = "cover_url")] public string? CoverUrl { get; set; }
		[DataMember(Name = "images")] public ImagesDto? Images { get; set; }
		[DataMember(Name = "series")] public SeriesDto? Series { get; set; }
	}

	[DataContract]
	private sealed class ImagesDto
	{
		[DataMember(Name = "thumb")] public string? Thumb { get; set; }
		[DataMember(Name = "cover_md")] public string? CoverMd { get; set; }
	}

	[DataContract]
	private sealed class SeriesDto
	{
		[DataMember(Name = "name")] public string? Name { get; set; }
		[DataMember(Name = "start_year")] public int? StartYear { get; set; }
		[DataMember(Name = "volume_number")] public int? VolumeNumber { get; set; }
		[DataMember(Name = "publisher_name")] public string? PublisherName { get; set; }
	}

	[DataContract]
	private sealed class IssueEnvelope
	{
		[DataMember(Name = "data")] public IssueDto? Data { get; set; }
	}

	[DataContract]
	private sealed class IssueDto
	{
		[DataMember(Name = "issue_number")] public object? IssueNumber { get; set; }
		[DataMember(Name = "name")] public string? Name { get; set; }
		[DataMember(Name = "solicitation")] public string? Solicitation { get; set; }
		[DataMember(Name = "release_date")] public string? ReleaseDate { get; set; }
		[DataMember(Name = "cover_date")] public string? CoverDate { get; set; }
		[DataMember(Name = "average_rating")] public double? AverageRating { get; set; }
		[DataMember(Name = "locations")] public object? Locations { get; set; }
		[DataMember(Name = "series")] public SeriesDto? Series { get; set; }
		[DataMember(Name = "publisher")] public NamedDto? Publisher { get; set; }
		[DataMember(Name = "imprint")] public NamedDto? Imprint { get; set; }
		[DataMember(Name = "creators")] public CreatorDto[]? Creators { get; set; }
		[DataMember(Name = "characters")] public NamedDto[]? Characters { get; set; }
		[DataMember(Name = "teams")] public NamedDto[]? Teams { get; set; }
		[DataMember(Name = "story_arcs")] public NamedDto[]? StoryArcs { get; set; }
	}

	[DataContract]
	private sealed class NamedDto
	{
		[DataMember(Name = "name")] public string? Name { get; set; }
	}

	[DataContract]
	private sealed class CreatorDto
	{
		[DataMember(Name = "name")] public string? Name { get; set; }
		[DataMember(Name = "role")] public string? Role { get; set; }
	}

	[DataContract]
	private sealed class ErrorDto
	{
		[DataMember(Name = "message")] public string? Message { get; set; }
	}
}
