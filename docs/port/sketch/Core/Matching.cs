// ComicApiPlugin.Core: series ranking, cover hashing, and auto-match.

namespace ComicApiPlugin.Matching;

/// <summary>MatchScore.compute_n. Pure given the chosen-series set and the current year.</summary>
public static class MatchScore
{
    public static int Compute(BookFacts book, SeriesRef series, Func<SeriesId, bool> chosenBefore, int currentYear) => throw new NotImplementedException();

    /// <summary>Best first. Stable: equal scores keep Comic Vine's order.</summary>
    public static IReadOnlyList<RankedSeries> Rank(BookFacts book, IReadOnlyList<SeriesRef> found, Func<SeriesId, bool> chosenBefore, int currentYear)
        => throw new NotImplementedException();
}

/// <summary>8x8 average hash on a 64bppArgb greyscale copy; x outer, y inner, R channel, strictly-above-average bits.</summary>
public static class ImageHash
{
    public static ulong? Compute(Bitmap? image) => throw new NotImplementedException();
    public static double Similarity(ulong? a, ulong? b) => throw new NotImplementedException(); // 1 - popcount/64; 0 if either missing
}

/// <summary>
/// automatcher.find_series_ref. Ties go to the earliest series in Comic Vine's order (deviations #7).
/// The one engine component that does I/O on its own, so it takes the ports it needs explicitly.
/// </summary>
public sealed class AutoMatcher(IComicVine comicVine, Func<SeriesId, bool> chosenBefore, int currentYear)
{
    public Task<SeriesRef?> FindAsync(BookFacts book, IScrapeBook source, SearchSettings search, CancellationToken ct) => throw new NotImplementedException();
    // TODO: localHash = ImageHash.Compute(source.GetPage(0)) on the calling (UI) thread, hash work in Task.Run;
    //   best = first max of MatchScore over SearchSeriesAsync(Effective.Series);
    //   issue 1 or no number: give up if best's cover is > 0.77 similar to the 2nd or 3rd series' cover;
    //   accept when local vs remote (issue cover, its alternates, or series cover) > 0.87.
}
