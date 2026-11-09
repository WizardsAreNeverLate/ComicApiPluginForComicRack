using System.Collections.Generic;
using ComicApiPlugin.Books;

namespace ComicApiPlugin.Config;

/// <summary>
/// What the merge rules read from the configuration, and nothing else. Computed by Configuration.ToMergeSettings.
/// Phase 3d owns the rules that use it.
/// </summary>
public sealed record MergeSettings(
	BookFields Update,            // the toggled fields, plus Rating when SCRAPE_RATING is on
	bool OverwriteExisting,
	bool IgnoreBlanks,            // the saved value, whatever OverwriteExisting says
	bool ConvertImprints,
	bool DownloadThumbs,
	bool PreserveThumbs,          // false whenever DownloadThumbs is false
	bool NoteScrapeDate,
	IReadOnlyDictionary<string, string> UserImprints,       // lowercase imprint to publisher
	IReadOnlyDictionary<string, string> PublisherAliases);  // lowercase alias to publisher
