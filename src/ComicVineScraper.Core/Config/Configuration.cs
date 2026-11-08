using System;
using System.Text;
using ComicVineScraper.Books;

namespace ComicVineScraper.Config;

/// <summary>
/// Immutable settings for one run: settings.dat plus advanced.dat. Edit with `with`.
/// MergeSettings and SearchSettings are computed on demand and never stored, so they cannot drift.
/// FromMaps and ToSettingsMap convert from and to the settings.dat map.
/// </summary>
public sealed partial record Configuration
{
	public string ApiKey { get; init; } = "";

	/// <summary>The fields the user switched on: the 21 FieldTable rows, plus Tags (updateTags) and Notes (updateNotes). Rating comes from Advanced.ScrapeRating.</summary>
	public BookFields Update { get; init; } = FieldTable.All | BookFields.Notes;

	public bool OverwriteExisting { get; init; } = true;

	/// <summary>Saved as the checkbox was; Python reads it without looking at OverwriteExisting.</summary>
	public bool IgnoreBlanks { get; init; }

	public bool ConvertImprints { get; init; } = true;
	public ChooseMode Choose { get; init; }
	public bool DownloadThumbs { get; init; } = true;

	/// <summary>Meaningful only with DownloadThumbs. ToMergeSettings reports false otherwise.</summary>
	public bool PreserveThumbs { get; init; } = true;

	public bool FastRescrape { get; init; } = true;
	public bool SummaryDialog { get; init; } = true;
	public required AdvancedSettings Advanced { get; init; }

	/// <summary>The generated ToString would print ApiKey, and so into the debug log.</summary>
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("ApiKey = ").Append(ApiKey.Length == 0 ? "<none>" : "<redacted>");
		builder.Append(", Update = ").Append(Update);
		builder.Append(", OverwriteExisting = ").Append(OverwriteExisting);
		builder.Append(", IgnoreBlanks = ").Append(IgnoreBlanks);
		builder.Append(", ConvertImprints = ").Append(ConvertImprints);
		builder.Append(", Choose = ").Append(Choose);
		builder.Append(", DownloadThumbs = ").Append(DownloadThumbs);
		builder.Append(", PreserveThumbs = ").Append(PreserveThumbs);
		builder.Append(", FastRescrape = ").Append(FastRescrape);
		builder.Append(", SummaryDialog = ").Append(SummaryDialog);
		builder.Append(", Advanced = ").Append(Advanced);
		return true;
	}

	public TimeSpan ScrapeDelay => TimeSpan.FromSeconds(Advanced.ScrapeDelaySeconds);
	public bool ConfirmIssue => Choose == ChooseMode.ConfirmIssue;
	public bool ScrapeRating => Advanced.ScrapeRating;
	public bool IgnoreFolders => Advanced.IgnoreFolders;

	public MergeSettings ToMergeSettings() => new(
		Update: ScrapeRating ? Update | BookFields.Rating : Update & ~BookFields.Rating,
		OverwriteExisting,
		IgnoreBlanks,
		ConvertImprints,
		DownloadThumbs,
		PreserveThumbs: PreserveThumbs && DownloadThumbs,
		Advanced.NoteScrapeDate,
		Advanced.UserImprints,
		Advanced.PublisherAliases);

	public SearchSettings ToSearchSettings() => new(
		Advanced.IgnoredSearchTerms,
		Advanced.IgnoredPublishers,
		Advanced.IgnoreBeforeYear,
		Advanced.IgnoreAfterYear,
		Advanced.NeverIgnoreThreshold,
		Advanced.MaxSearchResults);
}
