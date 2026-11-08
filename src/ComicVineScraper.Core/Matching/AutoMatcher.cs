using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.Matching;

/// <summary>
/// automatcher.find_series_ref: picks a book's series without asking, but only when the best-scoring series has a
/// cover that looks like the book's own cover.
/// </summary>
public sealed class AutoMatcher(IComicVine comicVine, Func<SeriesId, bool> chosenBefore, int currentYear)
{
	private const double MatchThreshold = 0.87;
	private const double TooSimilarThreshold = MatchThreshold - 0.10;

	/// <summary>
	/// Null when no series is a clear match. Call it on the thread that may read <paramref name="source"/>; the
	/// image hashing runs on the thread pool.
	/// </summary>
	public async Task<SeriesRef?> FindAsync(BookFacts book, IScrapeBook source, CancellationToken ct)
	{
		if (await FindBestSeriesAsync(book.Effective, ct) is not { } best)
		{
			return null;
		}
		// A hash of 0 (every pixel of the 8x8 copy equal, as for an all-black page) is a hash. Python's "if hash_local:"
		// treated it as no hash (deviations.md #21); only a missing page or a failed hash is "no hash" here.
		if (await HashAsync(source.GetPage(0), ct) is not { } local)
		{
			return null;
		}

		IssueRef? issue = book.Effective.Number.Length > 0 ? await comicVine.FindIssueAsync(best.Id, book.Effective.Number, ct) : null;
		if (IsSameCover(local, await RemoteHashAsync(issue is null ? best.ThumbUrl : issue.ThumbUrl, ct)))
		{
			return best;
		}
		if (issue is null)
		{
			return null;
		}
		Issue details = await comicVine.GetIssueAsync(issue.Id, includeRating: true, ct);
		foreach (Uri cover in details.CoverUrls)
		{
			if (IsSameCover(local, await RemoteHashAsync(cover, ct)))
			{
				return best;
			}
		}
		return null;
	}

	/// <summary>
	/// automatcher.__find_best_series. The top three series by score, where a tie goes to the series that comes first
	/// in Comic Vine's order (deviations.md #7). For a first or unnumbered issue, gives up when the best series' cover
	/// looks like the second's or third's: that is usually a collected edition and the series it collects.
	/// </summary>
	private async Task<SeriesRef?> FindBestSeriesAsync(BookValues book, CancellationToken ct)
	{
		// Python held the results in a set, which merged a series that Comic Vine returned twice.
		List<SeriesRef> candidates = [.. (await comicVine.SearchSeriesAsync(book.Series, null, ct)).GroupBy(s => s.Id).Select(g => g.First())];
		SeriesRef? primary = TakeBest(book, candidates);
		SeriesRef? secondary = TakeBest(book, candidates);
		SeriesRef? tertiary = TakeBest(book, candidates);
		if (primary is null || secondary is null || !IsFirstIssue(book.Number))
		{
			return primary;
		}

		ulong? primaryHash = await RemoteHashAsync(primary.ThumbUrl, ct);
		if (ImageHash.Similarity(primaryHash, await RemoteHashAsync(secondary.ThumbUrl, ct)) > TooSimilarThreshold)
		{
			return null;
		}
		if (tertiary is not null && ImageHash.Similarity(primaryHash, await RemoteHashAsync(tertiary.ThumbUrl, ct)) > TooSimilarThreshold)
		{
			return null;
		}
		return primary;
	}

	private SeriesRef? TakeBest(BookValues book, List<SeriesRef> candidates)
	{
		SeriesRef? best = null;
		double bestScore = double.NegativeInfinity;
		foreach (SeriesRef series in candidates)
		{
			double score = MatchScore.Compute(book, series, chosenBefore, currentYear);
			if (best is null || score > bestScore)
			{
				best = series;
				bestScore = score;
			}
		}
		if (best is not null)
		{
			candidates.Remove(best);
		}
		return best;
	}

	private static bool IsFirstIssue(string number) => number.Length == 0 || (PyStr.TryParseFloat(number, out double value) && value == 1.0);

	private static bool IsSameCover(ulong local, ulong? remote) => ImageHash.Similarity(local, remote) > MatchThreshold;

	private async Task<ulong?> RemoteHashAsync(Uri? url, CancellationToken ct) =>
		url is null ? null : await HashAsync(await comicVine.GetImageAsync(url, ct), ct);

	private static async Task<ulong?> HashAsync(Bitmap? image, CancellationToken ct)
	{
		if (image is null)
		{
			return null;
		}
		ulong? hash = await Task.Run(() =>
		{
			using (image)
			{
				return ImageHash.Compute(image);
			}
		});
		ct.ThrowIfCancellationRequested();
		return hash;
	}
}
