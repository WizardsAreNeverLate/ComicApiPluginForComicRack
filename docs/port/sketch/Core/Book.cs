// ComicVineScraper.Core: the book as the engine sees it.
// The plugin adapter (Plugin/ComicRackBook.cs) is the only code that converts to and from cYo ComicBook.

namespace ComicVineScraper.Books;

/// <summary>
/// Every ComicRack property the scraper may write. Replaces Python's "dont_update" strings:
/// a write is a (BookValues, BookFields) pair, never a set of property-name strings.
/// </summary>
[Flags]
public enum BookFields : long
{
    None = 0,
    Series = 1L << 0, Number = 1L << 1, Volume = 1L << 2, Title = 1L << 3,
    Crossovers = 1L << 4, Summary = 1L << 5, Publisher = 1L << 6, Imprint = 1L << 7,
    Characters = 1L << 8, Teams = 1L << 9, Locations = 1L << 10,
    Writers = 1L << 11, Pencillers = 1L << 12, Inkers = 1L << 13, Colorists = 1L << 14,
    Letterers = 1L << 15, CoverArtists = 1L << 16, Editors = 1L << 17,
    Webpage = 1L << 18, Rating = 1L << 19, Tags = 1L << 20, Notes = 1L << 21,
    IssueKey = 1L << 22,   // custom value comicvine_issue
    SeriesKey = 1L << 23,  // custom value comicvine_volume
    Released = 1L << 24,   // ReleasedTime; written only as a complete date
    PublishedYear = 1L << 25, PublishedMonth = 1L << 26, PublishedDay = 1L << 27,
    Format = 1L << 28,     // see open question: Python always writes ShadowFormat back to Format

    Published = PublishedYear | PublishedMonth | PublishedDay,
}

/// <summary>
/// The one list of user-toggleable fields. ConfigForm's CheckedListBox, settings.dat's update* keys,
/// and the label i18n keys all derive from this table. A test asserts the table covers each
/// settings.dat update* key exactly once.
/// </summary>
public static class FieldTable
{
    public sealed record Row(string SettingsKey, string LabelTextKey, BookFields Controls);

    public static IReadOnlyList<Row> Toggleable { get; } =
    [
        new("updateSeries", "Series", BookFields.Series),
        new("updateNumber", "Number", BookFields.Number),
        new("updatePublished", "Published", BookFields.Published),
        new("updateReleased", "Released", BookFields.Released),
        new("updateTitle", "Title", BookFields.Title),
        // ... 16 more rows, in ConfigForm display order: Crossovers, Writer, Penciller, Inker, CoverArtist,
        // Colorist, Letterer, Editor, Summary, Publisher, Imprint, Volume, Characters, Teams, Locations, Webpage.
    ];
}

/// <summary>All values the scraper reads or writes, in engine types. Lists are already split; blanks are null.</summary>
public sealed record BookValues
{
    public string Series { get; init; } = "";
    public string Number { get; init; } = "";
    public int? Volume { get; init; }
    public string Format { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Crossovers { get; init; } = [];
    public string Summary { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Imprint { get; init; } = "";
    public IReadOnlyList<string> Characters { get; init; } = [];
    public IReadOnlyList<string> Teams { get; init; } = [];
    public IReadOnlyList<string> Locations { get; init; } = [];
    public IReadOnlyList<string> Writers { get; init; } = [];
    public IReadOnlyList<string> Pencillers { get; init; } = [];
    public IReadOnlyList<string> Inkers { get; init; } = [];
    public IReadOnlyList<string> Colorists { get; init; } = [];
    public IReadOnlyList<string> Letterers { get; init; } = [];
    public IReadOnlyList<string> CoverArtists { get; init; } = [];
    public IReadOnlyList<string> Editors { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Notes { get; init; } = "";
    public string Webpage { get; init; } = "";
    public float? Rating { get; init; }            // CommunityRating 0 reads as null (Python blank is 0.0)
    public string IssueKey { get; init; } = "";    // raw custom value text
    public string SeriesKey { get; init; } = "";
    public PartialDate Released { get; init; }     // DateTime.MinValue reads as blank: see open question
    public PartialDate Published { get; init; }
}

/// <summary>What the adapter reads from one ComicRack book, on the UI thread, in one synchronous call.</summary>
public sealed record BookSnapshot(
    BookValues Values,
    string FilePath,           // "" for fileless books
    int PageCount,
    bool HasCustomThumbnail);  // CustomThumbnailKey is set

/// <summary>What a previous scrape left in Tags, Notes, or the comicvine_issue custom value.</summary>
public abstract record StoredMarker
{
    private StoredMarker() { }
    public sealed record None : StoredMarker;
    public sealed record SkipForever : StoredMarker;            // CVDBSKIP, any case
    public sealed record Issue(IssueId Id) : StoredMarker;      // CVDBn, legacy ComicVine[n, or custom value
}

/// <summary>
/// The engine's per-book input: the snapshot plus everything derived from it. Built once per book by
/// <see cref="BookFacts.From"/>, which is pure and oracle-tested against comicbook.py.
/// </summary>
public sealed record BookFacts(
    int Index,                    // position in the run; the nameless-book fallback for the series key
    BookSnapshot Snapshot,
    BookValues Effective,         // Series, Number, Published.Year filled from the file name when blank
    StoredMarker Marker,          // checked in Python's order: tags then notes for SKIP, then CVDB, then custom value
    SeriesId? StoredSeries,       // custom value comicvine_volume
    string UniqueSeriesKey)       // groups books of one series within a run
{
    public static BookFacts From(int index, BookSnapshot snapshot, FileNameParser parser, bool ignoreFolders)
        => throw new NotImplementedException();
    // TODO pseudocode:
    //   eff = snapshot.Values
    //   if eff.Series/Number/Published.Year blank: (s, n, y) = parser.Extract(Path.GetFileName(FilePath)); fill blanks
    //   Marker = KeyMarkers.Find(eff.Tags, eff.Notes, eff.IssueKey)
    //   UniqueSeriesKey = SeriesKey.Compute(eff.Series, eff.Format, folder(FilePath), eff.Volume, ignoreFolders)
    //                     ?? "#" + index   (Python used id(self))
}
