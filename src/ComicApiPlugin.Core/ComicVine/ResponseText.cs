using System.Net;
using System.Text;
using System.Xml;

namespace ComicApiPlugin.ComicVine;

/// <summary>What happens to a response body before it is parsed (utils.get_html_string, cvconnection.py:244-259).</summary>
internal static class ResponseText
{
	/// <summary>HTML-decodes the whole body, as Python does, then removes characters XML does not allow.</summary>
	public static string Clean(string body) => StripInvalidXmlChars(WebUtility.HtmlDecode(body));

	/// <summary>Comic Vine sends characters that XML forbids (Python issue 51). Valid surrogate pairs stay (deviations #8).</summary>
	public static string StripInvalidXmlChars(string text)
	{
		StringBuilder? kept = null;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
			{
				kept?.Append(c).Append(text[i + 1]);
				i++;
			}
			else if (XmlConvert.IsXmlChar(c))
			{
				kept?.Append(c);
			}
			else
			{
				kept ??= new StringBuilder(text, 0, i, text.Length);
			}
		}
		return kept?.ToString() ?? text;
	}
}
