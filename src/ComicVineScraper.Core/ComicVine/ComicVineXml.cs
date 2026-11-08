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

	public static int Total(XDocument doc) => Int(Text(doc.Root.Get("number_of_total_results"))) ?? 0;

	/// <summary>The series of a volume detail response, when it holds exactly one result.</summary>
	public static SeriesRef? ParseSeries(XDocument doc) =>
		Total(doc) == 1 && doc.Root.Get("results") is { } results ? ParseVolume(results) : null;

	/// <summary>The volume id of an issue detail response, when it holds exactly one result.</summary>
	public static SeriesId? ParseIssueSeries(XDocument doc) =>
		Total(doc) == 1 ? VolumeId(doc.Root.Get("results")) : null;

	public static SeriesDetails ParseSeriesDetails(XDocument doc)
	{
		XElement? results = doc.Root.Get("results");
		int? year = Int(Text(results.Get("start_year"))) is >= 0 and var y ? y : null;
		string publisher = Text(results.Get("publisher").Get("name")) ?? "";
		return new SeriesDetails(year, publisher);
	}

	public static SeriesId? IssueVolume(XDocument doc) => VolumeId(doc.Root.Get("results"));

	/// <summary>
	/// An issue detail response, with its volume (IssueVolume) and that volume's details already looked up.
	/// The alternate covers (associated_images) are read only when asked for, as Python's slow data.
	/// </summary>
	public static Issue ParseIssue(XDocument doc, IssueId requested, SeriesId seriesId, SeriesDetails details, bool alternateCovers)
	{
		XElement results = doc.Root.Get("results") ?? new XElement("results");
		string parent = Imprints.FindParentPublisher(details.Publisher);
		string webpage = Text(results.Get("site_detail_url")) is { } url && url.StartsWith("http", StringComparison.Ordinal) ? url : "";
		string description = Text(results.Get("description")) is { } html ? SummaryText.Clean(html) : "";

		var roles = new Dictionary<Role, List<string>>();
		foreach (Role role in Enum.GetValues(typeof(Role)))
		{
			roles[role] = [];
		}
		foreach (XElement person in Items(results, "person_credits", "person"))
		{
			if (person.Get("role") is not { } roleElement)
			{
				continue;
			}
			string? name = Text(person.Get("name"));
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
			covers.AddRange(results.Get("associated_images").GetAll("original_url").Select(Text).OfType<string>());
		}

		return new Issue(
			Id: Int(Text(results.Get("id"))) is > 0 and var id ? new IssueId(id) : requested,
			SeriesId: seriesId,
			SeriesName: Text(results.Get("volume").Get("name"))?.Trim() ?? "",
			VolumeYear: details.StartYear,
			IssueNumber: Text(results.Get("issue_number"))?.Trim() ?? "",
			Title: Text(results.Get("name"))?.Trim() ?? "",
			Summary: description,
			Publisher: parent,
			Imprint: parent != details.Publisher ? details.Publisher : "",
			Published: Date(Text(results.Get("cover_date"))),
			Released: Date(Text(results.Get("store_date"))),
			Crossovers: Names(results, "story_arc_credits", "story_arc").Where(NotBlank).ToList(),
			Characters: CleanList(Names(results, "character_credits", "character")),
			Teams: CleanList(Names(results, "team_credits", "team")),
			Locations: CleanList(Names(results, "location_credits", "location")),
			Writers: CleanPeople(roles[Role.Writer]),
			Pencillers: CleanPeople(roles[Role.Penciller]),
			Inkers: CleanPeople(roles[Role.Inker]),
			Colorists: CleanPeople(roles[Role.Colorist]),
			Letterers: CleanPeople(roles[Role.Letterer]),
			CoverArtists: CleanPeople(roles[Role.CoverArtist]),
			Editors: CleanPeople(roles[Role.Editor]),
			Webpage: webpage,
			Rating: null,
			CoverUrls: covers.Select(ToUri).OfType<Uri>().ToList());
	}

	/// <summary>
	/// xml2py's rule: an element whose only content is one text or CDATA node, or one child element that itself reads
	/// as a string, reads as that string. So &lt;image&gt;&lt;small_url&gt;u&lt;/small_url&gt;&lt;/image&gt; is the string u, not an
	/// image with a small_url. An empty element, or one with several children, is not a string, and Python's is_string
	/// checks then treat the field as missing.
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
		return count != 1 ? null : only switch
		{
			XText text => text.Value,
			XElement child => Text(child),
			_ => null,
		};
	}

	/// <summary>A child element as Python sees it: an element that reads as a string has no children.</summary>
	private static XElement? Get(this XElement? parent, string name) =>
		parent is null || Text(parent) is not null ? null : parent.Element(name);

	private static IEnumerable<XElement> GetAll(this XElement? parent, string name) =>
		parent is null || Text(parent) is not null ? [] : parent.Elements(name);

	private static ResultPage<T> ParsePage<T>(XDocument doc, string itemName, Func<XElement, T?> parse) where T : class
	{
		List<XElement> elements = doc.Root.Get("results").GetAll(itemName).ToList();
		return new ResultPage<T>(
			Total(doc),
			Int(Text(doc.Root.Get("number_of_page_results"))),
			elements.Count,
			elements.Select(parse).OfType<T>().ToList());
	}

	private static SeriesRef? ParseVolume(XElement volume)
	{
		if (Int(Text(volume.Get("id"))) is not { } id || id <= 0)
		{
			return null;
		}
		string name = Text(volume.Get("name"))?.Trim().Replace("&amp;", "&") ?? "";
		XElement? publisher = volume.Get("publisher");
		string publisherName = publisher is not null && publisher.HasElements ? Text(publisher.Get("name")) ?? "" : "";
		return new SeriesRef(
			new SeriesId(id),
			name.Length > 0 ? name : "Series " + id,
			Int(Text(volume.Get("start_year"))?.TrimEnd('-', ' ')) is >= 0 and var year ? year : null,
			publisherName.Trim(),
			Math.Max(0, Int(Text(volume.Get("count_of_issues"))) ?? 0),
			ToUri(ImageUrl(volume)));
	}

	private static IssueRef? ParseIssueRef(XElement issue) =>
		Int(Text(issue.Get("id"))) is > 0 and var id
			? new IssueRef(
				new IssueId(id),
				Text(issue.Get("issue_number"))?.Trim() ?? "",
				Text(issue.Get("name"))?.Trim() ?? "",
				ToUri(ImageUrl(issue)))
			: null;

	private static SeriesId? VolumeId(XElement? results) =>
		Int(Text(results.Get("volume").Get("id"))) is > 0 and var id ? new SeriesId(id) : null;

	private static string? ImageUrl(XElement parent)
	{
		XElement? image = parent.Get("image");
		return image is null ? null : ImagePreference.Select(name => Text(image.Get(name))).FirstOrDefault(url => url is not null);
	}

	private static IEnumerable<XElement> Items(XElement results, string credits, string item) =>
		results.Get(credits).GetAll(item);

	private static IEnumerable<string> Names(XElement results, string credits, string item) =>
		Items(results, credits, item).Select(element => Text(element.Get("name"))).OfType<string>();

	/// <summary>Issue's list setters: drop blank entries, then remove ',' and ';' (an entry of only "," becomes "").</summary>
	private static List<string> CleanList(IEnumerable<string> names) =>
		names.Where(NotBlank).Select(name => ListSeparators.Replace(name, "")).ToList();

	/// <summary>A creator credited with two roles that map to one list ("penciler, artist") is listed once, at the first place.</summary>
	private static List<string> CleanPeople(IEnumerable<string> names) =>
		CleanList(names).Distinct(StringComparer.Ordinal).ToList();

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
