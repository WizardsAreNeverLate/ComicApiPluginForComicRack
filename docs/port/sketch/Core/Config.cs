// ComicApiPlugin.Core: configuration and the .dat files in %APPDATA%\Comic Vine Scraper\.

namespace ComicApiPlugin.Config;

/// <summary>Python's autochooseSeries and confirmIssue bools, which are mutually exclusive. One enum, so both-true cannot exist.</summary>
public enum ChooseMode { Ask, AutoChooseSeries, ConfirmIssue }

/// <summary>
/// Immutable settings for one run: settings.dat plus advanced.dat. Load, edit with `with`, save.
/// Derived views (MergeSettings, SearchSettings) are computed, never stored, so they cannot drift.
/// </summary>
public sealed record Configuration
{
    public string ApiKey { get; init; } = "";
    public BookFields Update { get; init; }              // the 21 update* keys via FieldTable, plus Tags/Notes/Rating
    public bool OverwriteExisting { get; init; } = true;
    public bool IgnoreBlanks { get; init; }              // forced false when !OverwriteExisting
    public bool ConvertImprints { get; init; } = true;
    public ChooseMode Choose { get; init; }
    public bool DownloadThumbs { get; init; } = true;
    public bool PreserveThumbs { get; init; } = true;    // forced false when !DownloadThumbs
    public bool FastRescrape { get; init; } = true;
    public bool SummaryDialog { get; init; } = true;
    public required AdvancedSettings Advanced { get; init; }

    public TimeSpan ScrapeDelay => TimeSpan.FromSeconds(Advanced.ScrapeDelaySeconds);
    public bool ConfirmIssue => Choose == ChooseMode.ConfirmIssue;
    public bool ScrapeRating => Advanced.ScrapeRating;
    public bool IgnoreFolders => Advanced.IgnoreFolders;

    public MergeSettings ToMergeSettings() => throw new NotImplementedException();
    public SearchSettings ToSearchSettings() => throw new NotImplementedException();

    /// <summary>settings.dat map to settings. Unknown keys are ignored here and preserved by SettingsStore.</summary>
    public static Configuration FromMaps(IReadOnlyDictionary<string, DatValue> settings, string advancedText) => throw new NotImplementedException();
    public IReadOnlyDictionary<string, DatValue> ToSettingsMap() => throw new NotImplementedException();
}

/// <summary>advanced.dat: raw text is the source of truth (the Advanced tab edits it). Parsed, never re-serialised.</summary>
public sealed record AdvancedSettings(
    string RawText,
    IReadOnlyList<string> IgnoredPublishers, IReadOnlyList<string> IgnoredSearchTerms,
    int IgnoreBeforeYear, int IgnoreAfterYear, int NeverIgnoreThreshold,
    bool ScrapeRating, bool ShowCovers, bool WelcomeDialog,
    Regex? AltSearchRegex,                // null when blank or it does not compile
    bool IgnoreFolders, bool ForceSeriesArt, bool NoteScrapeDate,
    IReadOnlyDictionary<string, string> PublisherAliases,
    IReadOnlyDictionary<string, string> UserImprints,
    int ScrapeDelaySeconds,               // see open question: default 1, parsed values clamped 2..3600
    int MaxSearchResults)                 // 100, clamped 10..5000
{
    public static AdvancedSettings Parse(string text) => throw new NotImplementedException();
}

/// <summary>A value in a utils.persist_map file. Load converts like Python: bool (any case), float if it has '.', int, else string.</summary>
public abstract record DatValue
{
    private DatValue() { }
    public sealed record Bool(bool Value) : DatValue;   // written "True" / "False"
    public sealed record Int(int Value) : DatValue;
    public sealed record Float(double Value) : DatValue; // written with PyStr.Format
    public sealed record Text(string Value) : DatValue;
}

/// <summary>The persist_map / load_map format. Pure; oracle-tested both directions against utils.py.</summary>
public static class DatFile
{
    /// <summary>Lines not splitting into exactly 2 parts on ':' are skipped; parts are trimmed.</summary>
    public static IReadOnlyDictionary<string, DatValue> Parse(string text) => throw new NotImplementedException();

    /// <summary>":: This file was generated on yyyy.MM.dd HH:mm:ss", blank line, sorted "key : value" lines; entries containing ':' are dropped.</summary>
    public static string Format(IReadOnlyDictionary<string, DatValue> map, DateTime now) => throw new NotImplementedException();
}

/// <summary>
/// File I/O for the four .dat files. The only code that touches %APPDATA%\Comic Vine Scraper.
/// Writes are atomic (temp file + File.Replace) and UTF-8 with BOM, as .NET Encoding.UTF8 wrote them.
/// Saving settings.dat merges over the file's current map, so keys this version does not know survive.
/// </summary>
public sealed class SettingsStore(string folder, string pluginFolder, IClock clock)
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comic Vine Scraper");

    /// <summary>Copies the four .dat files from the plugin folder once, when settings.dat is missing (resources.py:127). Idempotent.</summary>
    public void MigrateIfNeeded() => throw new NotImplementedException();

    public Configuration Load() => throw new NotImplementedException();
    public void Save(Configuration config) => throw new NotImplementedException(); // settings.dat and advanced.dat

    public ChosenSeries LoadChosenSeries() => throw new NotImplementedException();
    public GeometryMap LoadGeometry() => throw new NotImplementedException();
}

/// <summary>series.dat: the series the user has picked before. Record writes through at once, like record_choice.</summary>
public sealed class ChosenSeries
{
    public bool Contains(SeriesId id) => throw new NotImplementedException();
    public void Record(SeriesId id) => throw new NotImplementedException(); // no-op if present; else add and save
}

/// <summary>geometry.dat: "{form}Location" = "X,Y", "{form}Size" = "W,H". Saved only when a form moved or resized.</summary>
public sealed class GeometryMap
{
    public (Point? Location, Size? Size) Get(string formKey) => throw new NotImplementedException();
    public void Set(string formKey, Point location, Size? size) => throw new NotImplementedException();
}
