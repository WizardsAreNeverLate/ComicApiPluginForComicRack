using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.ComicVine;

/// <summary>One page of a list response. RawCount counts the result elements, including any that did not parse.</summary>
internal sealed record ResultPage<T>(int Total, int? PageResults, int RawCount, IReadOnlyList<T> Items);

/// <summary>A volume's start year and publisher, as the issue mapping reads them (cvdb.py:546-591).</summary>
internal readonly record struct SeriesDetails(int? StartYear, string Publisher);

/// <summary>Response XML to domain records (the cvdb.py mapping). Pure.</summary>
internal static class ComicVineXml
{
	private static readonly Regex ListSeparators = new(",|;");
	private static readonly string[] ImagePreference = ["small_url", "medium_url", "large_url", "super_url", "thumb_url"];

	private static readonly Dictionary<string, Role[]> RoleMap = new(StringComparer.Ordinal)
	{
		["writer"] = [Role.Writer],
		["penciler"] = [Role.Penciller],
		["artist"] = [Role.Penciller, Role.Inker],
		["inker"] = [Role.Inker],
		["cover"] = [Role.CoverArtist],
		["editor"] = [Role.Editor],
		["colorer"] = [Role.Colorist],
		["colorist"] = [Role.Colorist],
		["letterer"] = [Role.Letterer],
	};

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

	public static ResultPage<SeriesRef> ParseSeriesSearch(XDocument doc) => ParsePage(doc, "volume", ParseVolume);

	public static ResultPage<IssueRef> ParseIssueList(XDocument doc) => ParsePage(doc, "issue", ParseIssueRef);

	public static int Total(XDocument doc) => Int(Text(doc.Root?.Element("number_of_total_results"))) ?? 0;

	/// <summary>The series of a volume detail response, when it holds exactly one result.</summary>
	public static SeriesRef? ParseSeries(XDocument doc) =>
		Total(doc) == 1 && doc.Root?.Element("results") is { } results ? ParseVolume(results) : null;

	/// <summary>The volume id of an issue detail response, when it holds exactly one result.</summary>
	public static SeriesId? ParseIssueSeries(XDocument doc) =>
		Total(doc) == 1 ? VolumeId(doc.Root?.Element("results")) : null;

	public static SeriesDetails ParseSeriesDetails(XDocument doc)
	{
		XElement? results = doc.Root?.Element("results");
		int? year = Int(Text(results?.Element("start_year"))) is >= 0 and var y ? y : null;
		string publisher = Text(results?.Element("publisher")?.Element("name")) ?? "";
		return new SeriesDetails(year, publisher);
	}

	public static SeriesId? IssueVolume(XDocument doc) => VolumeId(doc.Root?.Element("results"));

	/// <summary>
	/// An issue detail response, with its volume (IssueVolume) and that volume's details already looked up.
	/// The alternate covers (associated_images) are read only when asked for, as Python's slow data.
	/// </summary>
	public static Issue ParseIssue(XDocument doc, IssueId requested, SeriesId seriesId, SeriesDetails details, bool alternateCovers)
	{
		XElement results = doc.Root?.Element("results") ?? new XElement("results");
		string parent = Imprints.FindParentPublisher(details.Publisher);
		string webpage = Text(results.Element("site_detail_url")) is { } url && url.StartsWith("http", StringComparison.Ordinal) ? url : "";
		string description = Text(results.Element("description")) is { } html ? Pending3a.CleanSummary(html) : "";

		var roles = new Dictionary<Role, List<string>>();
		foreach (Role role in Enum.GetValues(typeof(Role)))
		{
			roles[role] = [];
		}
		foreach (XElement person in Items(results, "person_credits", "person"))
		{
			if (person.Element("role") is not { } roleElement)
			{
				continue;
			}
			string? name = Text(person.Element("name"));
			foreach (string role in (Text(roleElement) ?? "").Split(',').Select(r => r.Trim()))
			{
				if (name is not null && RoleMap.TryGetValue(role, out Role[]? mapped))
				{
					foreach (Role target in mapped)
					{
						roles[target].Add(name);
					}
				}
			}
		}

		List<string> covers = [];
		if (ImageUrl(results) is { Length: > 0 } cover)
		{
			covers.Add(cover);
		}
		if (alternateCovers)
		{
			covers.AddRange(results.Element("associated_images")?.Elements("original_url").Select(Text).OfType<string>() ?? []);
		}

		return new Issue(
			Id: Int(Text(results.Element("id"))) is > 0 and var id ? new IssueId(id) : requested,
			SeriesId: seriesId,
			SeriesName: Text(results.Element("volume")?.Element("name"))?.Trim() ?? "",
			VolumeYear: details.StartYear,
			IssueNumber: Text(results.Element("issue_number"))?.Trim() ?? "",
			Title: Text(results.Element("name"))?.Trim() ?? "",
			Summary: description,
			Publisher: parent,
			Imprint: parent != details.Publisher ? details.Publisher : "",
			Published: Date(Text(results.Element("cover_date"))),
			Released: Date(Text(results.Element("store_date"))),
			Crossovers: Names(results, "story_arc_credits", "story_arc").Where(NotBlank).ToList(),
			Characters: CleanList(Names(results, "character_credits", "character")),
			Teams: CleanList(Names(results, "team_credits", "team")),
			Locations: CleanList(Names(results, "location_credits", "location")),
			Writers: CleanList(roles[Role.Writer]),
			Pencillers: CleanList(roles[Role.Penciller]),
			Inkers: CleanList(roles[Role.Inker]),
			Colorists: CleanList(roles[Role.Colorist]),
			Letterers: CleanList(roles[Role.Letterer]),
			CoverArtists: CleanList(roles[Role.CoverArtist]),
			Editors: CleanList(roles[Role.Editor]),
			Webpage: webpage,
			Rating: null,
			CoverUrls: covers.Select(ToUri).OfType<Uri>().ToList());
	}

	/// <summary>
	/// xml2py's rule: an element whose only content is one text or CDATA node reads as a string. An empty element,
	/// or one with child elements, does not, and Python's is_string checks then treat the field as missing.
	/// </summary>
	internal static string? Text(XElement? element)
	{
		if (element is null)
		{
			return null;
		}
		XNode? only = null;
		int count = 0;
		foreach (XNode node in element.Nodes())
		{
			if (node is XComment or XProcessingInstruction)
			{
				continue;
			}
			only = node;
			count++;
		}
		return count == 1 && only is XText text ? text.Value : null;
	}

	private static ResultPage<T> ParsePage<T>(XDocument doc, string itemName, Func<XElement, T?> parse) where T : class
	{
		List<XElement> elements = doc.Root?.Element("results")?.Elements(itemName).ToList() ?? [];
		return new ResultPage<T>(
			Total(doc),
			Int(Text(doc.Root?.Element("number_of_page_results"))),
			elements.Count,
			elements.Select(parse).OfType<T>().ToList());
	}

	private static SeriesRef? ParseVolume(XElement volume)
	{
		if (Int(Text(volume.Element("id"))) is not { } id || id <= 0)
		{
			return null;
		}
		string name = Text(volume.Element("name"))?.Trim().Replace("&amp;", "&") ?? "";
		XElement? publisher = volume.Element("publisher");
		string publisherName = publisher is not null && publisher.HasElements ? Text(publisher.Element("name")) ?? "" : "";
		return new SeriesRef(
			new SeriesId(id),
			name.Length > 0 ? name : "Series " + id,
			Int(Text(volume.Element("start_year"))?.TrimEnd('-', ' ')) is >= 0 and var year ? year : null,
			publisherName.Trim(),
			Math.Max(0, Int(Text(volume.Element("count_of_issues"))) ?? 0),
			ToUri(ImageUrl(volume)));
	}

	private static IssueRef? ParseIssueRef(XElement issue) =>
		Int(Text(issue.Element("id"))) is > 0 and var id
			? new IssueRef(
				new IssueId(id),
				Text(issue.Element("issue_number"))?.Trim() ?? "",
				Text(issue.Element("name"))?.Trim() ?? "",
				ToUri(ImageUrl(issue)))
			: null;

	private static SeriesId? VolumeId(XElement? results) =>
		Int(Text(results?.Element("volume")?.Element("id"))) is > 0 and var id ? new SeriesId(id) : null;

	private static string? ImageUrl(XElement parent)
	{
		XElement? image = parent.Element("image");
		return image is null ? null : ImagePreference.Select(name => Text(image.Element(name))).FirstOrDefault(url => url is not null);
	}

	private static IEnumerable<XElement> Items(XElement results, string credits, string item) =>
		results.Element(credits)?.Elements(item) ?? [];

	private static IEnumerable<string> Names(XElement results, string credits, string item) =>
		Items(results, credits, item).Select(element => Text(element.Element("name"))).OfType<string>();

	/// <summary>Issue's list setters: drop blank entries, then remove ',' and ';' (an entry of only "," becomes "").</summary>
	private static List<string> CleanList(IEnumerable<string> names) =>
		names.Where(NotBlank).Select(name => ListSeparators.Replace(name, "")).ToList();

	private static bool NotBlank(string text) => text.Trim().Length > 0;

	/// <summary>cover_date and store_date: "YYYY-MM-DD" split on '-'. Any part that is not a number drops the whole date.</summary>
	private static PartialDate Date(string? text)
	{
		if (text is null || text.Length <= 1)
		{
			return PartialDate.None;
		}
		int?[] parts = text.Split('-').Select(Int).ToArray();
		if (parts.Any(part => part is null))
		{
			return PartialDate.None;
		}
		return PartialDate.Of(parts[0], parts.Length >= 2 ? parts[1] : null, parts.Length >= 3 ? parts[2] : null);
	}

	/// <summary>Python's int(): white space and a sign allowed. Null where Python raises, or outside the int range.</summary>
	private static int? Int(string? text) =>
		text is not null && PyStr.TryParseInt(text, out long value) && value is >= int.MinValue and <= int.MaxValue ? (int)value : null;

	private static Uri? ToUri(string? url) =>
		url is { Length: > 0 } && Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) ? uri : null;
}
