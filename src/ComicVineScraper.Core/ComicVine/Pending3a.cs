using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using ComicVineScraper.Config;
using ComicVineScraper.Model;

namespace ComicVineScraper.ComicVine;

/// <summary>
/// Interim copies of rules that workstream 3a owns: search cleanup and number words, summary cleanup, the series filter,
/// and the back-cover strip. The catalog calls only these members, so the merge replaces each body with a call to 3a's
/// version and then deletes this file.
/// </summary>
internal static class Pending3a
{
	private static readonly Regex Ampersand = new(" & ");
	private static readonly Regex Noise = new(@"\b(c2c|ctc|noads+|tbp)\b");
	private static readonly Regex SearchWord = new(@"[\w':.-]{1,}");

	private static readonly (string Digits, string Words)[] NumberWords =
	[
		("0", "zero"), ("1", "one"), ("2", "two"), ("3", "three"), ("4", "four"), ("5", "five"), ("6", "six"), ("7", "seven"),
		("8", "eight"), ("9", "nine"), ("10", "ten"), ("11", "eleven"), ("12", "twelve"), ("13", "thirteen"), ("14", "fourteen"),
		("15", "fifteen"), ("16", "sixteen"), ("17", "seventeen"), ("18", "eighteen"), ("19", "nineteen"), ("20", "twenty"),
		("0th", "zeroth"), ("1rst", "first"), ("2nd", "second"), ("3rd", "third"), ("4th", "fourth"), ("5th", "fifth"),
		("6th", "sixth"), ("7th", "seventh"), ("8th", "eighth"), ("9th", "ninth"), ("10th", "tenth"), ("11th", "eleventh"),
		("12th", "twelveth"), ("13th", "thirteenth"), ("14th", "fourteenth"), ("15th", "fifteenth"), ("16th", "sixteenth"),
		("17th", "seventeenth"), ("18th", "eighteenth"), ("19th", "nineteenth"), ("20th", "twentieth"),
	];

	private static readonly Regex Overview = new("Overview");
	private static readonly Regex Paragraph = new("<[bB][rR] ?/?>|<[Pp] ?>");
	private static readonly Regex Nbsp = new("&nbsp;?");
	private static readonly Regex MultiSpaces = new(" {2,}");
	private static readonly Regex Tags = new("<.*?>");
	private static readonly Regex ListOfCovers = new("(?is)list of covers.*$");

	/// <summary>cvdb.__cleanup_search_terms.</summary>
	public static string CleanSearchTerms(string terms, bool alternate)
	{
		string text = Noise.Replace(Ampersand.Replace(terms.ToLowerInvariant(), " and "), "");
		if (alternate)
		{
			string expanded = ConvertNumberWords(text, expand: true);
			text = expanded != text ? expanded : ConvertNumberWords(text, expand: false);
		}
		return string.Join(" ", SearchWord.Matches(text).Cast<Match>().Select(match => match.Value));
	}

	/// <summary>utils.convert_number_words.</summary>
	public static string ConvertNumberWords(string phrase, bool expand)
	{
		foreach ((string digits, string words) in NumberWords)
		{
			phrase = expand
				? Regex.Replace(phrase, @"\b" + digits + @"\b", words)
				: Regex.Replace(phrase, @"\b" + words + @"\b", digits);
		}
		return expand
			? Regex.Replace(phrase, @"\b1st\b", "first")
			: Regex.Replace(Regex.Replace(phrase, @"\btwelfth\b", "12th"), @"\beightteenth\b", "18th");
	}

	/// <summary>cvdb.__issue_parse_summary.</summary>
	public static string CleanSummary(string description)
	{
		string text = Overview.Replace(description, "");
		text = Paragraph.Replace(text, "\n");
		text = Tags.Replace(text, "");
		text = MultiSpaces.Replace(text, " ");
		text = Nbsp.Replace(text, " ");
		text = Paragraph.Replace(text, "\n");
		text = text.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
		return ListOfCovers.Replace(text, "").Trim();
	}

	/// <summary>dbutils.filter_series_refs, keeping Comic Vine's order.</summary>
	public static IReadOnlyList<SeriesRef> FilterSeries(IReadOnlyList<SeriesRef> series, SearchSettings settings) =>
		series.Where(s => s.IssueCount >= settings.NeverIgnoreThreshold
			|| ((s.StartYear is null || (s.StartYear >= settings.IgnoreBeforeYear && s.StartYear <= settings.IgnoreAfterYear))
				&& !settings.IgnoredPublishers.Contains(s.Publisher.ToLowerInvariant().Trim())))
			.ToList();

	/// <summary>utils.strip_back_cover: an image 1.2 to 1.5 times wider than tall is a back and front cover; keep the right half.</summary>
	public static Bitmap StripBackCover(Bitmap image)
	{
		double ratio = (double)image.Width / image.Height;
		if (ratio >= 1.5 || ratio <= 1.2)
		{
			return image;
		}
		var front = new Bitmap(image.Width / 2, image.Height);
		using (Graphics graphics = Graphics.FromImage(front))
		{
			// Python 2's -w/2 floors, so an odd width shifts one pixel further left than C#'s truncating division.
			graphics.DrawImage(image, -((image.Width + 1) / 2), 0);
		}
		image.Dispose();
		return front;
	}
}
