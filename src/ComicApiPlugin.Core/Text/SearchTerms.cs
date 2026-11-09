using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicApiPlugin.Text;

/// <summary>
/// The search text sent to Comic Vine. A search runs <see cref="StripIgnored"/>, then <see cref="Clean"/> with
/// alternate false; when that finds nothing, it retries once with Clean(cleaned, alternate: true) if the result differs.
/// </summary>
public static class SearchTerms
{
	private static readonly Regex Ampersand = new(" & ", RegexOptions.CultureInvariant);
	private static readonly Regex ScanTags = new(@"\b(c2c|ctc|noads+|tbp)\b", RegexOptions.CultureInvariant);
	private static readonly Regex Word = new(@"[\w':.-]{1,}", RegexOptions.CultureInvariant);

	/// <summary>db.query_series_refs: removes each ignored term made only of letters and digits, as a whole word, ignoring case.</summary>
	public static string StripIgnored(string terms, IReadOnlyList<string> ignoredTerms)
	{
		string[] words = ignoredTerms.Where(term => term is { Length: > 0 } && IsAlphanumeric(term)).Select(term => term.Trim()).ToArray();
		return words.Length == 0
			? terms
			: Regex.Replace(terms, @"(?i)\b(" + string.Join("|", words) + @")\b", "", RegexOptions.CultureInvariant);
	}

	/// <summary>
	/// cvdb.__cleanup_search_terms: lowercase, "&amp;" to "and", no scan tags, only word-like tokens. The alternate form
	/// also spells out number words, or if there are none to spell out, turns words into numbers.
	/// </summary>
	public static string Clean(string terms, bool alternate)
	{
		string s = PyStr.Lower(terms);
		s = Ampersand.Replace(s, " and ");
		s = ScanTags.Replace(s, "");
		if (alternate)
		{
			string expanded = NumberWords.Expand(s);
			s = expanded == s ? NumberWords.Contract(s) : expanded;
		}
		return string.Join(" ", Word.Matches(s).Cast<Match>().Select(m => m.Value));
	}

	/// <summary>
	/// IronPython's str.isalnum: every UTF-16 unit is a letter or digit. A surrogate unit is neither, so a character
	/// outside the BMP never counts, even a letter.
	/// </summary>
	private static bool IsAlphanumeric(string text)
	{
		for (int i = 0; i < text.Length; i++)
		{
			if (!char.IsLetterOrDigit(text, i))
			{
				return false;
			}
		}
		return true;
	}
}
