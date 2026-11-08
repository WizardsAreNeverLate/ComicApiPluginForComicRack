// ComicVineScraper.Core: the merge rules. The only code that decides what gets written.

namespace ComicVineScraper.Books;

/// <summary>
/// The complete write for one book: target values plus the fields to write. Built by MergeRules,
/// applied by IScrapeBook.Apply in one synchronous UI-thread call with no I/O in between.
/// Invariant: every flag in Fields has its final ComicRack value in Values (dates already gated:
/// Released only when complete, Month only with Year, Day only with Month).
/// </summary>
public sealed record BookPatch(BookValues Values, BookFields Fields)
{
    public static BookPatch Empty { get; } = new(new BookValues(), BookFields.None);
}

/// <summary>A cover the run decided to install as the custom thumbnail. Downloaded before Apply.</summary>
public sealed record ThumbnailPlan(Uri Url);

/// <summary>
/// The settings the merge reads. A projection of Configuration so the oracle harness can build it
/// from Python's Configuration without the rest.
/// </summary>
public sealed record MergeSettings(
    BookFields Update,            // from the 21 update* keys, plus Rating (SCRAPE_RATING), Tags, Notes
    bool OverwriteExisting,
    bool IgnoreBlanks,            // only meaningful when OverwriteExisting; ConfigForm enforces, load clamps
    bool ConvertImprints,
    bool DownloadThumbs,
    bool PreserveThumbs,
    bool NoteScrapeDate,
    IReadOnlyDictionary<string, string> UserImprints,      // lowercase imprint -> publisher
    IReadOnlyDictionary<string, string> PublisherAliases); // lowercase alias -> publisher

public static class MergeRules
{
    /// <summary>
    /// Pure. Ports ComicBook.update and the __massage_* helpers. Oracle seam: same inputs as Python's
    /// ComicBook.update over a pure-Python BookData stub; compare the (field, value) writes.
    /// </summary>
    public static BookPatch Plan(BookFacts book, Issue issue, MergeSettings settings, DateTime now)
        => throw new NotImplementedException();
    // TODO pseudocode:
    //   b = new PatchBuilder(book.Effective)
    //   foreach rule in Rules: rule.Apply(b, issue, settings)       // one table row per field
    //   (publisher, imprint) = Publishers.Resolve(issue.Publisher, issue.Imprint, settings)
    //   b.String(Imprint, imprint, ...); b.String(Publisher, publisher, ...)
    //   b.List(Tags, KeyMarkers.WithIssueTag(eff.Tags, issue.Id), update: Update.Has(Tags), overwrite: true, ignoreBlanks: false)
    //   b.String(Notes, KeyMarkers.WithIssueNote(eff.Notes, issue.Id, now, settings.NoteScrapeDate), ...)
    //   b.String(IssueKey, issue.Id.ToString(), true, true, false); same for SeriesKey
    //   b.GateDates()   // Released needs Y,M,D known; Month needs Year; Day needs Month
    //   return b.Build()

    /// <summary>The PERMSKIP write. See the open question on whether it also writes parsed name fields.</summary>
    public static BookPatch PlanSkipForever(BookFacts book, bool writeTags, bool writeNotes)
        => throw new NotImplementedException();

    /// <summary>The thumbnail rule: fileless book, DownloadThumbs, and not (has thumbnail and PreserveThumbs).</summary>
    public static ThumbnailPlan? PlanThumbnail(BookSnapshot snapshot, Uri? cover, MergeSettings settings)
        => throw new NotImplementedException();

    /// <summary>
    /// One row per scraped field. Row kinds: StringRule, ListRule, NumberRule (with validator), DateRule.
    /// Series and Number always ignore blanks. Adding a field is adding a row; nothing else lists fields.
    /// </summary>
    private static readonly FieldRule[] Rules = [/* Series, Number, Title, Crossovers, Summary, Released,
        Published, Volume (x > 0), Characters, Teams, Locations, Writers, Pencillers, Inkers, Colorists,
        Letterers, CoverArtists, Editors, Webpage, Rating (0..5) */];

    private abstract record FieldRule(BookFields Field)
    {
        public abstract void Apply(PatchBuilder builder, Issue issue, MergeSettings settings);
    }

    /// <summary>The massage rule: write when update and (overwrite or old blank) and not (ignoreBlanks and new blank).</summary>
    private sealed class PatchBuilder
    {
        // Accumulates (BookValues with-expressions, BookFields) and the debug-log lines Python printed ("*" marks a change).
    }
}

/// <summary>Publisher and imprint mapping in Python's order: user imprints, convert-imprints, aliases, equal-blanks.</summary>
public static class Publishers
{
    public static (string Publisher, string Imprint) Resolve(string publisher, string imprint, MergeSettings settings)
        => throw new NotImplementedException();
}
