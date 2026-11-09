// The ComicRack boundary. The only file that reads or writes cYo ComicBook properties.

namespace ComicApiPlugin.Plugin;

/// <summary>
/// Adapts one ComicRack book to IScrapeBook. Field-by-field mapping lives here and nowhere else;
/// which fields to write and with what values is decided in Core (MergeRules) before Apply runs.
/// A round-trip test (Read, then Apply with every field, then Read) must return equal BookValues.
/// </summary>
internal sealed class ComicRackBook(ComicBook book, IApplication app) : IScrapeBook
{
    private const string IssueKeyName = "comicvine_issue";
    private const string SeriesKeyName = "comicvine_volume";

    public BookSnapshot Read() => throw new NotImplementedException();
    // TODO: Series, Number, Year/Month/Day (not shadows); ShadowVolume, ShadowFormat; ReleasedTime
    //   (DateTime.MinValue -> PartialDate.None, see open question); lists split on ',';
    //   CommunityRating 0 -> null; GetCustomValue(IssueKeyName/SeriesKeyName); FilePath; PageCount;
    //   HasCustomThumbnail = !string.IsNullOrEmpty(book.CustomThumbnailKey)

    public Bitmap? GetPage(int index) => throw new NotImplementedException();
    // TODO: fileless -> null; app.GetComicPage(book, index) in try/catch (bug 235: PageCount can be 0); BackCover.Strip

    /// <summary>
    /// Writes in one synchronous pass. No method here awaits or touches the network, so a write can
    /// only be partial if a ComicRack setter throws; the first throw stops the pass and propagates.
    /// </summary>
    public void Apply(BookPatch patch, Bitmap? thumbnail) => throw new NotImplementedException();
    // TODO: Debug.Assert(SynchronizationContext.Current is WindowsFormsSynchronizationContext)
    //   foreach (BookFields f in Enum flags set in patch.Fields) Write(f, patch.Values)
    //   lists: string.Join(", ", items.Select(RemoveCommas))   // re.sub(",(\s+)", "\1") then "," -> " "
    //   Released -> ReleasedTime = new DateTime(y, m, d); PublishedYear/Month/Day -> Year/Month/Day
    //   IssueKey/SeriesKey -> SetCustomValue
    //   thumbnail != null -> app.SetCustomBookThumbnail(book, thumbnail) (false is logged, not thrown)

    /// <summary>The exhaustive switch. A test enumerates every single-bit BookFields value and asserts Write handles it.</summary>
    private void Write(BookFields field, BookValues values) => throw new NotImplementedException();
}
