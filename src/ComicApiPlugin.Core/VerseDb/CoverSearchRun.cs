using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.VerseDb;

/// <summary>The user's answer to a book's candidate list.</summary>
public abstract record CoverChoice
{
	private CoverChoice() { }

	public sealed record Picked(CoverCandidate Candidate) : CoverChoice;

	public sealed record Skip : CoverChoice;

	public sealed record Cancel : CoverChoice;
}

/// <summary>
/// The cover search's view of the UI. Every call is made on the UI thread. ChooseAsync returns Cancel, not throws,
/// when ct is cancelled while the dialog is open.
/// </summary>
public interface ICoverSearchUi
{
	void BookStarted(IScrapeBook book, BookFacts facts, int remaining);

	Task<CoverChoice> ChooseAsync(IScrapeBook book, BookFacts facts, CoverLookup lookup, CancellationToken ct);

	/// <summary>VerseDB found nothing for this book's cover.</summary>
	void ShowNoMatches(BookFacts facts);

	/// <summary>VerseDB asked the run to wait; the run waits that long, then asks again once.</summary>
	void ShowRateLimited(TimeSpan wait);
}

/// <summary>A cover search that stopped on an error. Summary counts the books written before it.</summary>
public sealed class CoverSearchFailedException(RunSummary summary, Exception cause)
	: Exception("The cover search stopped: " + cause.Message, cause)
{
	public RunSummary Summary { get; } = summary;
}

/// <summary>
/// Photographs each book's first page at VerseDB, lets the user pick one of the candidates, and writes that issue's
/// metadata into the book. Runs on the caller's SynchronizationContext like ScrapeRun, so IScrapeBook and the UI need no marshalling.
/// It never writes the Comic Vine markers (MergeRules.PlanFromVerseDb). With VerseDbAutoAccept and a comparer, a lookup with one candidate whose cover matches the book's is written without asking (deviations.md).
/// </summary>
public sealed class CoverSearchRun(IVerseDb verseDb, ICoverSearchUi ui, IClock clock, DebugLog log, Configuration config, ICoverComparer? comparer = null)
{
	private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(60);

	private readonly MergeSettings merge = config.ToMergeSettings();

	public async Task<RunSummary> RunAsync(IReadOnlyList<IScrapeBook> books, CancellationToken ct)
	{
		int scraped = 0;
		try
		{
			log.Debug(config.ToString());
			foreach ((IScrapeBook book, int index) in books.Select((book, index) => (book, index)))
			{
				ct.ThrowIfCancellationRequested();
				BookFacts facts = Facts(index, book);
				log.Debug("======> searching VerseDB for the cover of: '" + Describe(facts) + "'");
				ui.BookStarted(book, facts, books.Count - index);
				switch (await SearchBookAsync(book, facts, ct))
				{
					case BookOutcome.Scraped:
						scraped++;
						break;
					case BookOutcome.Cancelled:
						return new RunSummary(scraped, books.Count - scraped, Cancelled: true);
				}
			}
			log.Debug($"Cover search finished (scraped {scraped}, skipped {books.Count - scraped}).");
			return new RunSummary(scraped, books.Count - scraped, Cancelled: false);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			log.Debug("Cancelled!");
			return new RunSummary(scraped, books.Count - scraped, Cancelled: true);
		}
		catch (Exception e)
		{
			log.Error(e);
			throw new CoverSearchFailedException(new RunSummary(scraped, books.Count - scraped, Cancelled: false), e);
		}
	}

	private async Task<BookOutcome> SearchBookAsync(IScrapeBook book, BookFacts facts, CancellationToken ct)
	{
		byte[]? image = PageJpeg(book);
		if (image is null)
		{
			log.Debug("This book has no readable first page, so it is skipped.");
			return BookOutcome.Skipped;
		}

		CoverLookup lookup = await WithRateLimitRetryAsync(() => verseDb.LookUpCoverAsync(image, ct), ct);
		if (lookup.Candidates.Count == 0)
		{
			log.Debug("VerseDB has no match for this cover.");
			ui.ShowNoMatches(facts);
			return BookOutcome.Skipped;
		}

		if (lookup.Candidates.Count == 1 && config.VerseDbAutoAccept && comparer is not null && await CoverMatchesAsync(comparer, image, lookup.Candidates[0], ct))
		{
			await WriteAsync(book, facts, lookup.Candidates[0], ct);
			return BookOutcome.Scraped;
		}

		switch (await ui.ChooseAsync(book, facts, lookup, ct))
		{
			case CoverChoice.Picked picked:
				await WriteAsync(book, facts, picked.Candidate, ct);
				return BookOutcome.Scraped;
			case CoverChoice.Cancel:
				return BookOutcome.Cancelled;
			default:
				return BookOutcome.Skipped;
		}
	}

	/// <summary>Any trouble (no cover URL, a failed download, OpenCV missing) is logged and answered with false, so the dialog opens as it always did.</summary>
	private async Task<bool> CoverMatchesAsync(ICoverComparer comparer, byte[] bookCover, CoverCandidate candidate, CancellationToken ct)
	{
		string label = $"cover check: {candidate.SeriesName} #{candidate.IssueNumber}: ";
		if (candidate.CoverUrl is null)
		{
			log.Debug(label + "VerseDB sent no cover image");
			return false;
		}
		try
		{
			byte[] candidateCover = await verseDb.GetImageAsync(candidate.CoverUrl, ct);
			CoverSimilarity? score = await Task.Run(() => comparer.Compare(bookCover, candidateCover), ct);
			log.Debug(label + (score is { } s ? $"{s} -> {(s.IsMatch ? "match" : "no match")}" : "the cover could not be decoded"));
			return score is { IsMatch: true };
		}
		catch (OpenCvUnavailableException e)
		{
			log.Debug("cover auto-accept is unavailable: " + e.Message);
			return false;
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			log.Debug(label + "the check failed: " + e.Message);
			return false;
		}
	}
	private async Task WriteAsync(IScrapeBook book, BookFacts facts, CoverCandidate candidate, CancellationToken ct)
	{
		log.Debug("querying VerseDB for issue details...");
		IssueMetadata metadata = await WithRateLimitRetryAsync(() => verseDb.GetIssueAsync(candidate.IssueId, ct), ct);
		ct.ThrowIfCancellationRequested();
		BookFacts current = Facts(facts.Index, book);
		BookPatch patch = MergeRules.PlanFromVerseDb(current, metadata, merge);
		log.Debug("setting values for this comic book ('*' = changed):");
		foreach (FieldDecision decision in patch.Decisions)
		{
			log.Debug(decision.ToLogLine());
		}
		book.Apply(patch, null);
	}

	private byte[]? PageJpeg(IScrapeBook book)
	{
		using System.Drawing.Bitmap? page = book.GetPage(0);
		return page is null ? null : CoverJpeg.Encode(page);
	}

	/// <summary>One retry after the wait VerseDB asked for. A second 429 ends the run.</summary>
	private async Task<T> WithRateLimitRetryAsync<T>(Func<Task<T>> call, CancellationToken ct)
	{
		try
		{
			return await call();
		}
		catch (VerseDbException e) when (e.IsRateLimited)
		{
			TimeSpan wait = e.RetryAfter ?? DefaultRateLimitWait;
			log.Debug($"VerseDB asked to wait {wait.TotalSeconds:0} seconds.");
			ui.ShowRateLimited(wait);
			await clock.Delay(wait, ct);
			return await call();
		}
	}

	private BookFacts Facts(int index, IScrapeBook book) => BookFacts.From(index, book.Read(), ParseFileName, config.IgnoreFolders);

	private (string Series, string Number, string Year) ParseFileName(string fileName)
	{
		ParsedName parsed = FileNameParser.Parse(fileName, config.Advanced.AltSearchRegex, log);
		return (parsed.Series, parsed.Number, parsed.Year);
	}

	private static string Describe(BookFacts facts) =>
		facts.Snapshot.FilePath.Length == 0
			? $"FILELESS (\"{facts.Effective.Series} #{facts.Effective.Number}\")"
			: Path.GetFileName(facts.Snapshot.FilePath);
}
