using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.ComicVine;

/// <summary>
/// What the engine and the forms ask Comic Vine. Domain types only; no URLs to build, no XML, no paging.
/// All methods throw OperationCanceledException on ct and ComicVineException on failure.
/// </summary>
public interface IComicVine
{
	/// <summary>Cleans the terms (ignored terms, search cleanup, number-word retry), pages to MaxSearchResults, filters by advanced settings. Result order is Comic Vine's.</summary>
	Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct);

	Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct);

	/// <summary>The filter=volume:N,issue_number:M query with leading-zero stripping and the 0.5 retries (deviations #1, #2).</summary>
	Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct);

	Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct);

	/// <summary>A series by id, or the series of an issue for ComicVineTarget.OfIssue (cvinfo, stored keys).</summary>
	Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct);

	/// <summary>A cover or thumbnail, back cover stripped. Null when it cannot be fetched after one retry. Caller disposes.</summary>
	Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct);
}
