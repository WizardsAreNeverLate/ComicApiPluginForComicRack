// ComicVineScraper.Core: Comic Vine domain model.
// No ComicRack, no WinForms, no XML types. Everything here is immutable.

namespace ComicVineScraper.Model;

/// <summary>Comic Vine volume id (the N in 4050-N).</summary>
public readonly record struct SeriesId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Comic Vine issue id (the N in 4000-N).</summary>
public readonly record struct IssueId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a stored key: "123", and also whole-number floats such as "12.0" (deviations.md #9).
    /// Returns null for blank or non-integral text. This is the only place a stored issue key is parsed.
    /// </summary>
    public static IssueId? TryParseStored(string? text) => throw new NotImplementedException();
}

/// <summary>A date where later parts are known only if earlier parts are. Month needs Year; Day needs Month.</summary>
public readonly record struct PartialDate
{
    public int? Year { get; }
    public int? Month { get; }
    public int? Day { get; }

    public static PartialDate None => default;

    /// <summary>
    /// Validates like Issue's Python setters: day 1..31, month 1..12, year >= 0, anything else becomes unknown.
    /// Then drops any part whose predecessor is unknown, so (null, 5, 3) cannot exist.
    /// </summary>
    public static PartialDate Of(int? year, int? month, int? day) => throw new NotImplementedException();

    public bool IsComplete => Day is not null;
    public bool IsBlank => Year is null;
}

/// <summary>One row of a series search, or a series resolved by id.</summary>
public sealed record SeriesRef(
    SeriesId Id,
    string Name,          // trimmed, "&amp;" decoded; empty becomes "Series {id}"
    int? StartYear,       // Python's -1 is null
    string Publisher,     // trimmed; "" when Comic Vine has none
    int IssueCount,       // max(0, n)
    Uri? ThumbUrl);

/// <summary>One row of a series' issue list.</summary>
public sealed record IssueRef(IssueId Id, string IssueNumber, string Title, Uri? ThumbUrl);

/// <summary>Full issue details, already cleaned: summary stripped, roles mapped, lists free of blanks and ',' ';'.</summary>
public sealed record Issue(
    IssueId Id,
    SeriesId SeriesId,
    string SeriesName,
    int? VolumeYear,
    string IssueNumber,
    string Title,
    string Summary,
    string Publisher,     // parent publisher after cvimprints mapping
    string Imprint,       // "" when none
    PartialDate Published, // cover_date
    PartialDate Released,  // store_date
    IReadOnlyList<string> Crossovers,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Locations,
    IReadOnlyList<string> Writers,
    IReadOnlyList<string> Pencillers,
    IReadOnlyList<string> Inkers,
    IReadOnlyList<string> Colorists,
    IReadOnlyList<string> Letterers,
    IReadOnlyList<string> CoverArtists,
    IReadOnlyList<string> Editors,
    string Webpage,       // only when it starts with http
    float? Rating,        // only when slow data was requested; 0..5
    IReadOnlyList<Uri> CoverUrls); // preference order small, medium, large, super, thumb; then alternates

/// <summary>What a cvinfo file or a Comic Vine URL names. Parsed by ComicVineUrl.TryParse.</summary>
public abstract record ComicVineTarget
{
    private ComicVineTarget() { }
    public sealed record OfSeries(SeriesId Id) : ComicVineTarget;   // 4050-N or 49-N
    public sealed record OfIssue(IssueId Id) : ComicVineTarget;     // 4000-N, resolved to its series
}
