using System;
using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.Books;

namespace ComicApiPlugin.Config;

public sealed partial record Configuration
{
	private const string ApiKeyKey = "apiKey";
	// New keys, unknown to the Python plugin, which ignores them. DatFile drops a value containing ':', so a token with one is not saved.
	private const string VerseDbApiKeyKey = "verseDbApiKey";
	private const string ProviderOrderKey = "providerOrder";
	private const string ComicVineEnabledKey = "comicVineEnabled";
	private const string VerseDbEnabledKey = "verseDbEnabled";
	private const string OverwriteExistingKey = "overwriteExisting";
	private const string IgnoreBlanksKey = "ignoreBlanks";
	private const string ConvertImprintsKey = "convertImprints";
	private const string AutochooseSeriesKey = "autochooseSeries";
	private const string ConfirmIssueKey = "confirmIssue";
	private const string DownloadThumbsKey = "downloadThumbs";
	private const string PreserveThumbsKey = "preserveThumbs";
	private const string FastRescrapeKey = "fastRescrape";
	private const string UpdateNotesKey = "updateNotes";
	private const string UpdateTagsKey = "updateTags";
	private const string SummaryDialogKey = "summaryDialog";
	private const string VerseDbAutoAcceptKey = "verseDbAutoAccept";
	private const string ShowServiceConsoleKey = "showServiceConsole";
	private const string ComicVineCoverAutoAcceptKey = "comicVineCoverAutoAccept";
	private const string ComicVineCoverCompareCountKey = "comicVineCoverCompareCount";

	/// <summary>
	/// settings.dat map to settings; a missing key keeps its default. Unknown keys are ignored here and kept by SettingsStore.
	/// A value that is not a bool counts as Python truthiness would (non-zero, non-empty), which is what load_defaults stored.
	/// </summary>
	public static Configuration FromMaps(IReadOnlyDictionary<string, DatValue> settings, string advancedText)
	{
		var config = new Configuration { Advanced = AdvancedSettings.Parse(advancedText) };
		BookFields update = config.Update;
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			update = With(update, row.Controls, settings, row.SettingsKey);
		}
		update = With(update, BookFields.Notes, settings, UpdateNotesKey);
		update = With(update, BookFields.Tags, settings, UpdateTagsKey);

		bool autochoose = Flag(settings, AutochooseSeriesKey, false);
		bool confirm = Flag(settings, ConfirmIssueKey, false);
		return config with
		{
			ApiKey = settings.TryGetValue(ApiKeyKey, out DatValue? key) ? key.ToFileText() : config.ApiKey,
			VerseDbApiKey = settings.TryGetValue(VerseDbApiKeyKey, out DatValue? verseKey) ? verseKey.ToFileText() : config.VerseDbApiKey,
			Providers = ReadProviders(settings),
			Update = update,
			OverwriteExisting = Flag(settings, OverwriteExistingKey, config.OverwriteExisting),
			IgnoreBlanks = Flag(settings, IgnoreBlanksKey, config.IgnoreBlanks),
			ConvertImprints = Flag(settings, ConvertImprintsKey, config.ConvertImprints),
			Choose = confirm ? ChooseMode.ConfirmIssue : autochoose ? ChooseMode.AutoChooseSeries : ChooseMode.Ask,
			DownloadThumbs = Flag(settings, DownloadThumbsKey, config.DownloadThumbs),
			PreserveThumbs = Flag(settings, PreserveThumbsKey, config.PreserveThumbs),
			FastRescrape = Flag(settings, FastRescrapeKey, config.FastRescrape),
			SummaryDialog = Flag(settings, SummaryDialogKey, config.SummaryDialog),
			VerseDbAutoAccept = Flag(settings, VerseDbAutoAcceptKey, config.VerseDbAutoAccept),
			ShowServiceConsole = Flag(settings, ShowServiceConsoleKey, config.ShowServiceConsole),
			ComicVineCoverAutoAccept = Flag(settings, ComicVineCoverAutoAcceptKey, config.ComicVineCoverAutoAccept),
			ComicVineCoverCompareCount = Count(settings, ComicVineCoverCompareCountKey, config.ComicVineCoverCompareCount),
		};
	}

	/// <summary>The keys Configuration.save_defaults writes. advanced.dat is the raw text in Advanced.</summary>
	public IReadOnlyDictionary<string, DatValue> ToSettingsMap()
	{
		var map = new Dictionary<string, DatValue>
		{
			[ApiKeyKey] = new DatValue.Text(ApiKey),
			[VerseDbApiKeyKey] = new DatValue.Text(VerseDbApiKey),
			[ProviderOrderKey] = new DatValue.Text(string.Join(",", Providers.Select(p => p.Kind))),
			[ComicVineEnabledKey] = new DatValue.Bool(IsEnabled(ProviderKind.ComicVine)),
			[VerseDbEnabledKey] = new DatValue.Bool(IsEnabled(ProviderKind.VerseDb)),
			[OverwriteExistingKey] = new DatValue.Bool(OverwriteExisting),
			[IgnoreBlanksKey] = new DatValue.Bool(IgnoreBlanks),
			[ConvertImprintsKey] = new DatValue.Bool(ConvertImprints),
			[AutochooseSeriesKey] = new DatValue.Bool(Choose == ChooseMode.AutoChooseSeries),
			[ConfirmIssueKey] = new DatValue.Bool(Choose == ChooseMode.ConfirmIssue),
			[DownloadThumbsKey] = new DatValue.Bool(DownloadThumbs),
			[PreserveThumbsKey] = new DatValue.Bool(PreserveThumbs),
			[FastRescrapeKey] = new DatValue.Bool(FastRescrape),
			[UpdateNotesKey] = new DatValue.Bool(Update.HasFlag(BookFields.Notes)),
			[UpdateTagsKey] = new DatValue.Bool(Update.HasFlag(BookFields.Tags)),
			[SummaryDialogKey] = new DatValue.Bool(SummaryDialog),
			[VerseDbAutoAcceptKey] = new DatValue.Bool(VerseDbAutoAccept),
			[ShowServiceConsoleKey] = new DatValue.Bool(ShowServiceConsole),
			[ComicVineCoverAutoAcceptKey] = new DatValue.Bool(ComicVineCoverAutoAccept),
			[ComicVineCoverCompareCountKey] = new DatValue.Int(ComicVineCoverCompareCount),
		};
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			map[row.SettingsKey] = new DatValue.Bool((Update & row.Controls) == row.Controls);
		}
		return map;
	}

	private bool IsEnabled(ProviderKind kind) => Providers.Any(p => p.Kind == kind && p.Enabled);

	private static ProviderList ReadProviders(IReadOnlyDictionary<string, DatValue> settings)
	{
		bool comicVine = Flag(settings, ComicVineEnabledKey, true);
		bool verseDb = Flag(settings, VerseDbEnabledKey, true);
		IEnumerable<ProviderSetting> ordered = settings.TryGetValue(ProviderOrderKey, out DatValue? order)
			? order.ToFileText().Split(',').Select(name => Enum.TryParse(name.Trim(), true, out ProviderKind kind) && Enum.IsDefined(typeof(ProviderKind), kind)
				? new ProviderSetting(kind, kind == ProviderKind.ComicVine ? comicVine : verseDb)
				: null).OfType<ProviderSetting>()
			: [];
		return ProviderTable.Normalize(ordered.Concat([new ProviderSetting(ProviderKind.ComicVine, comicVine), new ProviderSetting(ProviderKind.VerseDb, verseDb)]));
	}

	private static BookFields With(BookFields update, BookFields bits, IReadOnlyDictionary<string, DatValue> settings, string key) =>
		settings.TryGetValue(key, out DatValue? value) ? (Truthy(value) ? update | bits : update & ~bits) : update;

	private static bool Flag(IReadOnlyDictionary<string, DatValue> settings, string key, bool fallback) =>
		settings.TryGetValue(key, out DatValue? value) ? Truthy(value) : fallback;

	/// <summary>An integer setting; a value that is not a whole number keeps the fallback. The property clamps the range.</summary>
	private static int Count(IReadOnlyDictionary<string, DatValue> settings, string key, int fallback) =>
		settings.TryGetValue(key, out DatValue? value) && value is DatValue.Int i ? (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, i.Value)) : fallback;

	private static bool Truthy(DatValue value) => value switch
	{
		DatValue.Bool b => b.Value,
		DatValue.Int i => i.Value != 0,
		DatValue.Float f => f.Value != 0,
		DatValue.Text t => t.Value.Length > 0,
		_ => false,
	};
}
