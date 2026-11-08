using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Config;

/// <summary>
/// advanced.dat: the raw text is the source of truth (the Advanced tab edits it). Parse fills the other members and
/// applies the clamps.
/// </summary>
public sealed record AdvancedSettings(
	string RawText,
	IReadOnlyList<string> IgnoredPublishers,
	IReadOnlyList<string> IgnoredSearchTerms,
	int IgnoreBeforeYear,
	int IgnoreAfterYear,
	int NeverIgnoreThreshold,
	bool ScrapeRating,
	bool ShowCovers,
	bool WelcomeDialog,
	Regex? AltSearchRegex,                // null when blank or it does not compile
	bool IgnoreFolders,
	bool ForceSeriesArt,
	bool NoteScrapeDate,
	IReadOnlyDictionary<string, string> PublisherAliases,
	IReadOnlyDictionary<string, string> UserImprints,
	int ScrapeDelaySeconds,               // default 1, parsed values clamped 2..3600
	int MaxSearchResults)                 // default 100, clamped 10..5000
{
	public static AdvancedSettings Parse(string? text) => AdvancedParser.Parse(text);
}
