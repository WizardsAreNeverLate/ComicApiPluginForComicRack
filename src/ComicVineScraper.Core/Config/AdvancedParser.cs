using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ComicVineScraper.Text;

namespace ComicVineScraper.Config;

/// <summary>advanced.dat parsing, line for line as Configuration.__set_advanced_settings_s does it.</summary>
internal static class AdvancedParser
{
	private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
	private static readonly Regex Arrow = new(@"^\s*(\S.*?)\s*[-=]+>\s*(\S.*?)$", RegexOptions.CultureInvariant);
	private static readonly char[] QuoteTrim = [' ', '\'', '"'];

	private static Regex Key(string name) => new(@"^" + name + @"\s*=\s*['""]?(.+?)['""]?$", Options);

	private static readonly Regex IgnorePublisher = Key("IGNORE_PUBLISHER");
	private static readonly Regex IgnoreSearchTerm = Key("IGNORE_SEARCHTERM");
	private static readonly Regex IgnoreBeforeYear = Key("IGNORE_BEFORE_YEAR");
	private static readonly Regex IgnoreAfterYear = Key("IGNORE_AFTER_YEAR");
	private static readonly Regex NeverIgnoreThreshold = Key("NEVER_IGNORE_THRESHOLD");
	private static readonly Regex ScrapeRating = Key("SCRAPE_RATING");
	private static readonly Regex ShowCovers = Key("SHOW_COVERS");
	private static readonly Regex WelcomeDialog = Key("WELCOME_DIALOG");
	private static readonly Regex AltSearchRegex = Key("ALT_SEARCH_REGEX");
	private static readonly Regex IgnoreFolders = Key("IGNORE_FOLDERS");
	private static readonly Regex ForceSeriesArt = Key("FORCE_SERIES_ART");
	private static readonly Regex NoteScrapeDate = Key("NOTE_SCRAPE_DATE");
	private static readonly Regex PublisherAlias = Key("PUBLISHER_ALIAS");
	private static readonly Regex Imprint = Key("IMPRINT");
	private static readonly Regex ScrapeDelay = Key("SCRAPE_DELAY");
	private static readonly Regex MaxSearchResults = Key("MAX_SEARCH_RESULTS");

	public static AdvancedSettings Parse(string? text)
	{
		string raw = (text ?? "").Trim();
		var publishers = new List<string>();
		var terms = new List<string>();
		var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
		var imprints = new Dictionary<string, string>(StringComparer.Ordinal);
		int before = 0, after = 9999999, threshold = 9999999, delay = 1, maxResults = 100;
		bool rating = false, covers = true, welcome = true, folders = false, seriesArt = false, noteDate = false;
		Regex? alt = null;

		foreach (string line in raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
		{
			if (Value(IgnorePublisher, line) is { } publisher)
			{
				AddOnce(publishers, Lower(publisher).Trim());
			}
			if (Value(IgnoreSearchTerm, line) is { } found && Lower(found).Trim() is { Length: > 0 } term && term.All(char.IsLetterOrDigit))
			{
				AddOnce(terms, term);
			}
			before = Number(IgnoreBeforeYear, line) ?? before;
			after = Number(IgnoreAfterYear, line) ?? after;
			threshold = Number(NeverIgnoreThreshold, line) ?? threshold;
			rating = Flag(ScrapeRating, line) ?? rating;
			covers = Flag(ShowCovers, line) ?? covers;
			welcome = Flag(WelcomeDialog, line) ?? welcome;
			if (Value(AltSearchRegex, line) is { } pattern && PythonRegex.TryCompile(pattern) is { } compiled)
			{
				alt = compiled;
			}
			folders = Flag(IgnoreFolders, line) ?? folders;
			seriesArt = Flag(ForceSeriesArt, line) ?? seriesArt;
			noteDate = Flag(NoteScrapeDate, line) ?? noteDate;
			if (Value(PublisherAlias, line) is { } aliasText && Arrow.Match(aliasText) is { Success: true } alias)
			{
				string from = Lower(alias.Groups[1].Value.Trim(QuoteTrim));
				string to = alias.Groups[2].Value.Trim(QuoteTrim);
				if (from.Length > 0 && to.Length > 0 && to.Length <= 50)
				{
					aliases[from] = to;
				}
			}
			if (Value(Imprint, line) is { } imprintText && Arrow.Match(imprintText) is { Success: true } imprint)
			{
				string name = Lower(imprint.Groups[1].Value.Trim(QuoteTrim));
				string parent = imprint.Groups[2].Value.Trim(QuoteTrim);
				if (name.Length > 0 && parent.Length > 0 && parent.Length <= 50 && name.Length <= 50)
				{
					imprints[name] = parent;
				}
			}
			delay = Number(ScrapeDelay, line) is { } d ? Math.Min(3600, Math.Max(2, d)) : delay;
			maxResults = Number(MaxSearchResults, line) is { } m ? Math.Min(5000, Math.Max(10, m)) : maxResults;
		}
		return new AdvancedSettings(raw, publishers, terms, before, after, threshold, rating, covers, welcome, alt,
			folders, seriesArt, noteDate, aliases, imprints, delay, maxResults);
	}

	private static string Lower(string text) => text.ToLowerInvariant();

	private static void AddOnce(List<string> list, string item)
	{
		if (!list.Contains(item))
		{
			list.Add(item);
		}
	}

	private static string? Value(Regex key, string line) => key.Match(line) is { Success: true } match ? match.Groups[1].Value : null;

	private static bool? Flag(Regex key, string line) => Value(key, line) is { } value ? Lower(value.Trim()) == "true" : null;

	/// <summary>int(float(text)). Python raises on nan and infinity, which would abort loading every setting, so those lines are ignored. Huge values saturate.</summary>
	private static int? Number(Regex key, string line)
	{
		if (Value(key, line) is not { } text || !PyStr.TryParseFloat(text, out double number) || double.IsNaN(number) || double.IsInfinity(number))
		{
			return null;
		}
		double whole = Math.Truncate(number);
		return whole >= int.MaxValue ? int.MaxValue : whole <= int.MinValue ? int.MinValue : (int)whole;
	}
}
