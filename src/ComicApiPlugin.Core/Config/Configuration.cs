using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ComicApiPlugin.Books;

namespace ComicApiPlugin.Config;

/// <summary>
/// Immutable settings for one run: settings.dat plus advanced.dat. Edit with `with`.
/// MergeSettings and SearchSettings are computed on demand and never stored, so they cannot drift.
/// FromMaps and ToSettingsMap convert from and to the settings.dat map.
/// </summary>
public sealed partial record Configuration
{
	public string ApiKey { get; init; } = "";

	public string VerseDbApiKey { get; init; } = "";

	/// <summary>In priority order, first is highest. Always holds every ProviderKind once: assign through ProviderTable.Normalize.</summary>
	public ProviderList Providers { get; init; } = ProviderTable.Default;

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
	/// <summary>VerseDB: write the match without asking when the lookup finds one candidate and its cover matches the book's.</summary>
	public bool VerseDbAutoAccept { get; init; }

	/// <summary>The local API service runs with its console window on show, for debugging.</summary>
	public bool ShowServiceConsole { get; init; }

	/// <summary>Comic Vine: compare the covers of the top search results and take a single confident match without asking.</summary>
	public bool ComicVineCoverAutoAccept { get; init; }

	public const int MinCoverCompareCount = 1;
	public const int MaxCoverCompareCount = 20;

	/// <summary>How many top-ranked series ComicVineCoverAutoAccept compares. Always within MinCoverCompareCount..MaxCoverCompareCount.</summary>
	public int ComicVineCoverCompareCount
	{
		get;
		init => field = Math.Max(MinCoverCompareCount, Math.Min(MaxCoverCompareCount, value));
	} = 5;

	public required AdvancedSettings Advanced { get; init; }

	/// <summary>The generated ToString would print ApiKey, and so into the debug log.</summary>
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("ApiKey = ").Append(ApiKey.Length == 0 ? "<none>" : "<redacted>");
		builder.Append(", VerseDbApiKey = ").Append(VerseDbApiKey.Length == 0 ? "<none>" : "<redacted>");
		builder.Append(", Providers = ").Append(string.Join(",", Providers.Select(p => p.Kind + (p.Enabled ? "" : "(off)"))));
		builder.Append(", Update = ").Append(Update);
		builder.Append(", OverwriteExisting = ").Append(OverwriteExisting);
		builder.Append(", IgnoreBlanks = ").Append(IgnoreBlanks);
		builder.Append(", ConvertImprints = ").Append(ConvertImprints);
		builder.Append(", Choose = ").Append(Choose);
		builder.Append(", DownloadThumbs = ").Append(DownloadThumbs);
		builder.Append(", PreserveThumbs = ").Append(PreserveThumbs);
		builder.Append(", FastRescrape = ").Append(FastRescrape);
		builder.Append(", SummaryDialog = ").Append(SummaryDialog);
		builder.Append(", VerseDbAutoAccept = ").Append(VerseDbAutoAccept);
		builder.Append(", ShowServiceConsole = ").Append(ShowServiceConsole);
		builder.Append(", ComicVineCoverAutoAccept = ").Append(ComicVineCoverAutoAccept);
		builder.Append(", ComicVineCoverCompareCount = ").Append(ComicVineCoverCompareCount);
		builder.Append(", Advanced = ").Append(Advanced);
		return true;
	}

	public string KeyOf(ProviderKind kind) => kind == ProviderKind.ComicVine ? ApiKey : VerseDbApiKey;

	/// <summary>Actions the user can run, in priority order: the provider is enabled and its key is not blank.</summary>
	public IReadOnlyList<ScrapeAction> AvailableActions() =>
		[.. Providers.Where(p => p.Enabled && KeyOf(p.Kind).Trim().Length > 0).Select(p => ProviderTable.ActionOf(p.Kind))];

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
