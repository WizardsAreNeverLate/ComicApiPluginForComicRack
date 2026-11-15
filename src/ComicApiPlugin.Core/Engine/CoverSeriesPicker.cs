using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Engine;

/// <summary>
/// Picks a series without the series dialog: compares the book's first page with the cover of the book's issue in each of
/// the top-ranked series, and returns the series only when exactly one cover matches. New behaviour, not in the Python plugin (deviations.md).
/// Any trouble (no number, no readable page, OpenCV missing, a failed request) is logged and answered with null, so the dialog opens as it always did.
/// </summary>
internal sealed class CoverSeriesPicker(IComicVine comicVine, ICoverComparer comparer, DebugLog log, int compareCount)
{
	public async Task<SeriesRef?> PickAsync(IScrapeBook book, string issueNumber, IReadOnlyList<RankedSeries> ranked, CancellationToken ct)
	{
		if (issueNumber.Length == 0)
		{
			log.Debug("cover auto-accept: this book has no issue number, so the issue cannot be found in each series.");
			return null;
		}
		byte[]? bookCover = EncodeFirstPage(book);
		if (bookCover is null)
		{
			log.Debug("cover auto-accept: this book has no readable first page.");
			return null;
		}

		var scored = new List<(SeriesRef, CoverSimilarity?)>();
		try
		{
			foreach (SeriesRef series in ranked.Take(compareCount).Select(r => r.Series))
			{
				scored.Add((series, await ScoreAsync(series, issueNumber, bookCover, ct)));
			}
		}
		catch (OpenCvUnavailableException e)
		{
			log.Debug("cover auto-accept is unavailable: " + e.Message);
			return null;
		}
		catch (ComicVineException e) when (e.IsRecoverable)
		{
			log.Debug("cover auto-accept stopped on a failed request: " + e.Message);
			return null;
		}

		SeriesRef? picked = CoverAutoAccept.PickSingleMatch(scored);
		log.Debug(picked is null
			? $"cover auto-accept: {scored.Count(s => s.Item2 is { IsMatch: true })} of {scored.Count} covers match, so the series is not chosen automatically."
			: "cover auto-accept: chose " + picked.Name);
		return picked;
	}

	private async Task<CoverSimilarity?> ScoreAsync(SeriesRef series, string number, byte[] bookCover, CancellationToken ct)
	{
		string label = $"cover check: {series.Name} #{number}: ";
		IssueRef? issue = await comicVine.FindIssueAsync(series.Id, number, ct);
		if (issue is null)
		{
			log.Debug(label + $"no single issue #{number} in this series ({series.StartYear?.ToString() ?? "year unknown"}, {series.IssueCount} issues)");
			return null;
		}
		if (issue.ThumbUrl is not { } url)
		{
			log.Debug(label + "the issue has no cover image");
			return null;
		}
		byte[]? candidate = await DownloadAsync(url, ct);
		if (candidate is null)
		{
			log.Debug(label + "the cover could not be downloaded");
			return null;
		}
		CoverSimilarity? score;
		try
		{
			score = await Task.Run(() => comparer.Compare(bookCover, candidate), ct);
		}
		catch (Exception e) when (e is not OperationCanceledException and not OpenCvUnavailableException)
		{
			log.Debug(label + "the comparison failed: " + e.Message);
			return null;
		}
		log.Debug(label + (score is { } s ? $"{s} -> {(s.IsMatch ? "match" : "no match")}" : "the cover could not be decoded"));
		return score;
	}

	private async Task<byte[]?> DownloadAsync(Uri url, CancellationToken ct)
	{
		using Bitmap? image = await comicVine.GetImageAsync(url, ct);
		return image is null ? null : CoverJpeg.Encode(image);
	}

	private static byte[]? EncodeFirstPage(IScrapeBook book)
	{
		using Bitmap? page = book.GetPage(0);
		return page is null ? null : CoverJpeg.Encode(page);
	}
}
