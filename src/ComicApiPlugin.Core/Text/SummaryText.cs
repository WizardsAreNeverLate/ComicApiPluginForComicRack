using System.Text.RegularExpressions;

namespace ComicApiPlugin.Text;

/// <summary>cvdb.__issue_parse_summary: an issue's description HTML as plain summary text.</summary>
public static class SummaryText
{
	private static readonly Regex Overview = new("Overview", RegexOptions.CultureInvariant);
	private static readonly Regex Paragraph = new("<[bB][rR] ?/?>|<[Pp] ?>", RegexOptions.CultureInvariant);
	private static readonly Regex NonBreakingSpace = new("&nbsp;?", RegexOptions.CultureInvariant);
	private static readonly Regex RepeatedSpaces = new(" {2,}", RegexOptions.CultureInvariant);
	private static readonly Regex Tag = new("<.*?>", RegexOptions.CultureInvariant);
	private static readonly Regex ListOfCovers = new("(?is)list of covers.*$", RegexOptions.CultureInvariant);

	/// <summary>The Python steps, in the Python order.</summary>
	public static string Clean(string description)
	{
		string s = Overview.Replace(description, "");
		s = Paragraph.Replace(s, "\n");
		s = Tag.Replace(s, "");
		s = RepeatedSpaces.Replace(s, " ");
		s = NonBreakingSpace.Replace(s, " ");
		s = Paragraph.Replace(s, "\n");
		s = s.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
		s = ListOfCovers.Replace(s, "");
		return s.Trim();
	}
}
