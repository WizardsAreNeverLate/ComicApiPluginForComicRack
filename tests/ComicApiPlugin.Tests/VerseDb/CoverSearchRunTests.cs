using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.VerseDb;

public sealed class CoverSearchRunTests
{
	private static readonly CoverCandidate Candidate = new(new VerseDbIssueId(5432), "Batman", 1940, 1, "12", "The Joker", "DC Comics", "1990-06-01", null);

	private static readonly IssueMetadata Metadata = new(
		"Batman", 1940, "12", "The Joker", "Summary.", "DC Comics", "", PartialDate.Of(1990, 6, 1), PartialDate.None,
		[], ["Batman"], [], [], ["Grant Morrison"], [], [], [], [], [], [], "", null);

	private static readonly BookFields IdentityFields = BookFields.Tags | BookFields.Notes | BookFields.IssueKey | BookFields.SeriesKey;

	private static CoverLookup Lookup(params CoverCandidate[] candidates) => new(false, candidates);

	private static VerseDbException RateLimited(TimeSpan? after) => new((HttpStatusCode)429, "Too many requests.", after);

	private sealed class Harness
	{
		public FakeVerseDb Api { get; } = new();
		public FakeUi Ui { get; } = new();
		public ManualClock Clock { get; } = new();
		public DebugLog Log { get; } = new();
		public bool AutoAccept { get; init; }
		public ICoverComparer? Comparer { get; init; }

		public Task<RunSummary> Run(IReadOnlyList<IScrapeBook> books, CancellationToken ct = default) =>
			new CoverSearchRun(Api, Ui, Clock, Log, new Configuration { Advanced = AdvancedSettings.Parse(""), VerseDbAutoAccept = AutoAccept }, Comparer).RunAsync(books, ct);
	}

	[Test]
	public async Task A_picked_candidate_writes_the_metadata_and_no_Comic_Vine_markers()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup(Candidate));
		h.Api.Issues.Enqueue(() => Metadata);
		h.Ui.Answers.Enqueue(new CoverChoice.Picked(Candidate));
		var book = new FakeScrapeBook();

		RunSummary summary = await h.Run([book]);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(h.Api.IssueRequests).IsEquivalentTo([Candidate.IssueId]);
		BookPatch patch = book.Applied.Single();
		await Assert.That(patch.Values.Series).IsEqualTo("Batman");
		await Assert.That(patch.Values.Writers).IsEquivalentTo(["Grant Morrison"]);
		await Assert.That(patch.Fields & IdentityFields).IsEqualTo(BookFields.None);
		await Assert.That(book.Values.Series).IsEqualTo("Batman");
	}

	[Test]
	public async Task The_uploaded_image_is_a_JPEG_no_bigger_than_1200_pixels()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup());
		var book = new FakeScrapeBook { Page = () => new Bitmap(2400, 3600) };

		await h.Run([book]);

		using var image = Image.FromStream(new System.IO.MemoryStream(h.Api.Uploads.Single()));
		await Assert.That(image.RawFormat.Guid).IsEqualTo(System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
		await Assert.That(Math.Max(image.Width, image.Height)).IsEqualTo(1200);
		await Assert.That(image.Width).IsEqualTo(800);
	}

	[Test]
	public async Task Skip_leaves_the_book_alone_and_moves_on()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup(Candidate));
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());
		var book = new FakeScrapeBook();

		RunSummary summary = await h.Run([book]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(h.Api.IssueRequests).IsEmpty();
	}

	[Test]
	public async Task Cancel_stops_the_run_before_the_next_book()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup(Candidate));
		h.Ui.Answers.Enqueue(new CoverChoice.Cancel());
		var first = new FakeScrapeBook();
		var second = new FakeScrapeBook();

		RunSummary summary = await h.Run([first, second]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 2, true));
		await Assert.That(h.Api.Uploads.Count).IsEqualTo(1);
		await Assert.That(h.Ui.Started).IsEqualTo(1);
	}

	[Test]
	public async Task A_cancelled_token_ends_the_run_as_cancelled()
	{
		var h = new Harness();
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		RunSummary summary = await h.Run([new FakeScrapeBook()], cts.Token);

		await Assert.That(summary.Cancelled).IsTrue();
		await Assert.That(h.Api.Uploads).IsEmpty();
	}

	[Test]
	public async Task A_book_with_no_first_page_is_skipped_without_a_lookup()
	{
		var h = new Harness();
		var book = new FakeScrapeBook { Page = () => null };

		RunSummary summary = await h.Run([book]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(h.Api.Uploads).IsEmpty();
	}

	[Test]
	public async Task No_candidates_tells_the_UI_and_counts_as_skipped()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup());

		RunSummary summary = await h.Run([new FakeScrapeBook()]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(h.Ui.NoMatches).IsEqualTo(1);
		await Assert.That(h.Ui.Choices).IsEqualTo(0);
	}

	[Test]
	public async Task A_rate_limit_waits_as_asked_and_retries_once()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => throw RateLimited(TimeSpan.FromSeconds(42)));
		h.Api.Lookups.Enqueue(() => Lookup(Candidate));
		h.Api.Issues.Enqueue(() => Metadata);
		h.Ui.Answers.Enqueue(new CoverChoice.Picked(Candidate));
		var book = new FakeScrapeBook();

		RunSummary summary = await h.Run([book]);

		await Assert.That(summary.Scraped).IsEqualTo(1);
		await Assert.That(h.Clock.Delays).IsEquivalentTo([TimeSpan.FromSeconds(42)]);
		await Assert.That(h.Ui.RateLimits).IsEquivalentTo([TimeSpan.FromSeconds(42)]);
	}

	[Test]
	public async Task A_rate_limit_without_Retry_After_waits_a_minute()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => throw RateLimited(null));
		h.Api.Lookups.Enqueue(() => Lookup());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Clock.Delays).IsEquivalentTo([TimeSpan.FromSeconds(60)]);
	}

	[Test]
	public async Task A_second_rate_limit_ends_the_run_with_an_error()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => throw RateLimited(TimeSpan.FromSeconds(1)));
		h.Api.Lookups.Enqueue(() => throw RateLimited(TimeSpan.FromSeconds(1)));

		CoverSearchFailedException? error = await Assert.That(async () => await h.Run([new FakeScrapeBook()])).Throws<CoverSearchFailedException>();

		await Assert.That(error!.InnerException).IsTypeOf<VerseDbException>();
	}

	[Test]
	public async Task Another_VerseDB_error_stops_the_run_and_keeps_the_count_so_far()
	{
		var h = new Harness();
		h.Api.Lookups.Enqueue(() => Lookup(Candidate));
		h.Api.Issues.Enqueue(() => Metadata);
		h.Ui.Answers.Enqueue(new CoverChoice.Picked(Candidate));
		h.Api.Lookups.Enqueue(() => throw new VerseDbException(HttpStatusCode.Unauthorized, "Bad token."));

		CoverSearchFailedException? error = await Assert.That(async () => await h.Run([new FakeScrapeBook(), new FakeScrapeBook()])).Throws<CoverSearchFailedException>();

		await Assert.That(error!.Summary).IsEqualTo(new RunSummary(1, 1, false));
		await Assert.That(((VerseDbException)error.InnerException!).Status).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	private static readonly CoverCandidate WithCover = Candidate with { CoverUrl = new Uri("https://versedb.test/cover.webp") };

	private sealed class FakeComparer(Func<CoverSimilarity?> score) : ICoverComparer
	{
		public int Calls { get; private set; }

		public CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover)
		{
			Calls++;
			return score();
		}
	}

	private static Harness AutoAcceptHarness(FakeComparer comparer, params CoverCandidate[] candidates)
	{
		var h = new Harness { AutoAccept = true, Comparer = comparer };
		h.Api.Lookups.Enqueue(() => Lookup(candidates));
		h.Api.Issues.Enqueue(() => Metadata);
		h.Api.Image = () => [1, 2, 3];
		return h;
	}

	[Test]
	public async Task One_candidate_whose_cover_matches_is_written_without_asking()
	{
		var comparer = new FakeComparer(() => new CoverSimilarity(0.95, 0.49));
		Harness h = AutoAcceptHarness(comparer, WithCover);
		var book = new FakeScrapeBook();

		RunSummary summary = await h.Run([book]);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(h.Ui.Choices).IsEqualTo(0);
		await Assert.That(h.Api.ImageRequests).IsEquivalentTo([WithCover.CoverUrl!]);
		await Assert.That(book.Applied.Count).IsEqualTo(1);
		await Assert.That(h.Log.Snapshot()).Contains("cover check: Batman #12: correlation 0.95, features 0.49 -> match");
	}

	[Test]
	public async Task One_candidate_whose_cover_does_not_match_opens_the_dialog()
	{
		Harness h = AutoAcceptHarness(new FakeComparer(() => new CoverSimilarity(0.1, 0.01)), WithCover);
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		RunSummary summary = await h.Run([new FakeScrapeBook()]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(h.Ui.Choices).IsEqualTo(1);
	}

	[Test]
	public async Task Two_candidates_open_the_dialog_even_when_both_covers_match()
	{
		var comparer = new FakeComparer(() => new CoverSimilarity(0.95, 0.49));
		Harness h = AutoAcceptHarness(comparer, WithCover, WithCover with { IssueId = new VerseDbIssueId(5433) });
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Ui.Choices).IsEqualTo(1);
		await Assert.That(comparer.Calls).IsEqualTo(0);
		await Assert.That(h.Api.ImageRequests).IsEmpty();
	}

	[Test]
	public async Task With_the_setting_off_the_dialog_opens_and_no_image_is_fetched()
	{
		var comparer = new FakeComparer(() => new CoverSimilarity(0.95, 0.49));
		Harness h = new() { Comparer = comparer };
		h.Api.Lookups.Enqueue(() => Lookup(WithCover));
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Ui.Choices).IsEqualTo(1);
		await Assert.That(comparer.Calls).IsEqualTo(0);
		await Assert.That(h.Api.ImageRequests).IsEmpty();
	}

	[Test]
	public async Task A_candidate_without_a_cover_url_opens_the_dialog()
	{
		var comparer = new FakeComparer(() => new CoverSimilarity(0.95, 0.49));
		Harness h = AutoAcceptHarness(comparer, Candidate);
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Ui.Choices).IsEqualTo(1);
		await Assert.That(comparer.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task A_comparer_that_cannot_load_OpenCV_opens_the_dialog()
	{
		Harness h = AutoAcceptHarness(new FakeComparer(() => throw new OpenCvUnavailableException("OpenCvSharpExtern.dll was not found.")), WithCover);
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		RunSummary summary = await h.Run([new FakeScrapeBook()]);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(h.Ui.Choices).IsEqualTo(1);
		await Assert.That(h.Log.Snapshot().Any(l => l.StartsWith("cover auto-accept is unavailable", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task A_failed_image_download_opens_the_dialog()
	{
		var comparer = new FakeComparer(() => new CoverSimilarity(0.95, 0.49));
		Harness h = AutoAcceptHarness(comparer, WithCover);
		h.Api.Image = () => throw new VerseDbException(HttpStatusCode.NotFound, "Gone.");
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Ui.Choices).IsEqualTo(1);
	}

	[Test]
	public async Task A_cover_that_cannot_be_decoded_opens_the_dialog()
	{
		Harness h = AutoAcceptHarness(new FakeComparer(() => null), WithCover);
		h.Ui.Answers.Enqueue(new CoverChoice.Skip());

		await h.Run([new FakeScrapeBook()]);

		await Assert.That(h.Ui.Choices).IsEqualTo(1);
	}
	internal sealed class FakeVerseDb : IVerseDb
	{
		public Queue<Func<CoverLookup>> Lookups { get; } = new();
		public Queue<Func<IssueMetadata>> Issues { get; } = new();
		public List<byte[]> Uploads { get; } = [];
		public List<VerseDbIssueId> IssueRequests { get; } = [];

		public Task<CoverLookup> LookUpCoverAsync(byte[] image, CancellationToken ct)
		{
			Uploads.Add(image);
			return Task.FromResult(Lookups.Dequeue()());
		}

		public Task<IssueMetadata> GetIssueAsync(VerseDbIssueId issue, CancellationToken ct)
		{
			IssueRequests.Add(issue);
			return Task.FromResult(Issues.Dequeue()());
		}

		public List<Uri> ImageRequests { get; } = [];
		public Func<byte[]> Image { get; set; } = () => throw new NotSupportedException();

		public Task<byte[]> GetImageAsync(Uri url, CancellationToken ct)
		{
			ImageRequests.Add(url);
			return Task.FromResult(Image());
		}
	}

	internal sealed class FakeUi : ICoverSearchUi
	{
		public Queue<CoverChoice> Answers { get; } = new();
		public int Started { get; private set; }
		public int Choices { get; private set; }
		public int NoMatches { get; private set; }
		public List<TimeSpan> RateLimits { get; } = [];

		public void BookStarted(IScrapeBook book, BookFacts facts, int remaining) => Started++;

		public Task<CoverChoice> ChooseAsync(IScrapeBook book, BookFacts facts, CoverLookup lookup, CancellationToken ct)
		{
			Choices++;
			return Task.FromResult(Answers.Dequeue());
		}

		void ICoverSearchUi.ShowNoMatches(BookFacts facts) => NoMatches++;

		public void ShowRateLimited(TimeSpan wait) => RateLimits.Add(wait);
	}

	internal sealed class FakeScrapeBook : IScrapeBook
	{
		public BookValues Values { get; private set; } = new() { Series = "Old", Tags = ["CVDB1"] };
		public List<BookPatch> Applied { get; } = [];
		public Func<Bitmap?> Page { get; init; } = () => new Bitmap(100, 150);

		public BookSnapshot Read() => new(Values, "", 20, false);

		public Bitmap? GetPage(int index) => index == 0 ? Page() : null;

		public bool Apply(BookPatch patch, Bitmap? thumbnail)
		{
			Applied.Add(patch);
			Values = patch.Values;
			return true;
		}
	}
}
