using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;
using TUnit.Assertions.Enums;
using static ComicVineScraper.Engine.StepKind;
using static ComicVineScraper.Tests.Engine.ScrapeRunTests;

namespace ComicVineScraper.Tests.Engine;

/// <summary>The run around the books: order, the scrape delay, delayed books, auto-matching, cancellation, and errors.</summary>
public sealed class ScrapeRunLoopTests
{
	private const string Batman1 = @"C:\Comics\Batman 1.cbz";
	private const string Batman497 = @"C:\Comics\Batman 497.cbz";

	[Test]
	public async Task The_scrape_delay_comes_before_every_book_but_the_first_and_never_on_the_second_pass()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.AutoChooseSeries) };
		world.Ui.Series(new SeriesChoice.Skip());
		var skipped = FakeBook.File(@"C:\Comics\Aaa 1.cbz", b => b.Tags = "CVDBSKIP");
		var unmatched = FakeBook.File(Batman1);
		var alsoSkipped = FakeBook.File(@"C:\Comics\Zzz 1.cbz", b => b.Tags = "CVDBSKIP");

		RunSummary summary = await world.RunAsync(unmatched, skipped, alsoSkipped);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 3, false));
		await Assert.That(world.Clock.Delays).IsEquivalentTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Started).IsEquivalentTo(new[] { "Aaa 1.cbz:3", "Zzz 1.cbz:2", "Batman 1.cbz:1", "Batman 1.cbz:1" }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman 1.cbz")).IsEquivalentTo(new[] { Begin, Done, Begin, Search, ChooseSeries, Done }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Delayed_books_go_to_the_end_once_and_the_second_pass_asks_the_user()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.AutoChooseSeries) };
		world.Respond(Synthetic.IssueNumberUrl(91273, "2"), Synthetic.IssueList((9200002, "2")));
		world.Respond(Synthetic.IssueUrl(9200002), Synthetic.Issue(9200002, 91273, "2"));
		world.Ui.Series(q => Pick(q, 91273));
		var first = FakeBook.File(Batman1);
		var second = FakeBook.File(@"C:\Comics\Batman 2.cbz");

		RunSummary summary = await world.RunAsync(first, second);

		await Assert.That(summary).IsEqualTo(new RunSummary(2, 0, false));
		await Assert.That(world.Ui.Started).IsEquivalentTo(new[] { "Batman 1.cbz:2", "Batman 2.cbz:2", "Batman 1.cbz:2", "Batman 2.cbz:1" }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman 2.cbz")).IsEquivalentTo(new[] { Begin, Done, Begin, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(second.Book.GetCustomValue("comicvine_issue")).IsEqualTo("9200002");
	}

	[Test]
	public async Task Auto_choose_scrapes_a_book_whose_cover_matches_without_a_question()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.AutoChooseSeries) };
		using Bitmap cover = LeftHalfBlack();
		world.ImageFor = url => url.Contains("5265989-01") ? cover : null;
		// The year makes the 2016 series the best match.
		var book = FakeBook.File(@"C:\Comics\Batman 1 (2016).cbz", firstPage: cover);

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(world.StepsOf("Batman 1 (2016).cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEmpty();
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		await Assert.That(world.SeriesDat()).IsEquivalentTo(new[] { "91273" });
	}

	[Test]
	public async Task An_auto_matched_series_without_the_issue_is_forgotten_and_the_book_waits_for_the_second_pass()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.AutoChooseSeries) };
		world.Respond(Synthetic.IssueNumberUrl(91273, "9999"), Synthetic.IssueList());
		world.Respond(Synthetic.IssueNumberUrl(796, "9999"), Synthetic.IssueList());
		using Bitmap cover = LeftHalfBlack();
		world.ImageFor = _ => cover;
		world.Ui.Series(new SeriesChoice.Skip());

		RunSummary summary = await world.RunAsync(FakeBook.File(@"C:\Comics\Batman 9999.cbz", firstPage: cover));

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.StepsOf("Batman 9999.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Done, Begin, Search, ChooseSeries, Done }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Auto_choose_shows_the_issue_dialog_when_two_issues_match_the_books_number()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.AutoChooseSeries) };
		// Deviation 32: "7" and "7.0" share a natural key, so no mode guesses between them.
		foreach (int series in new[] { 91273, 796, 166619 })
		{
			world.Respond(Synthetic.IssueNumberUrl(series, "7"), Synthetic.IssueList((9300001, "7"), (9300002, "7.0")));
		}
		world.Respond(Synthetic.IssueListUrl(796), Synthetic.IssueList((9300001, "7"), (9300002, "7.0")));
		using Bitmap cover = LeftHalfBlack();
		world.ImageFor = _ => cover;
		world.Ui.Series(q => Pick(q, 796)).Issue(new IssueChoice.Skip());

		RunSummary summary = await world.RunAsync(FakeBook.File(@"C:\Comics\Batman 7.cbz", firstPage: cover));

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman", "issue:796:" }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.IssueQuestions.Single().Issues.Select(i => i.Id.Value)).IsEquivalentTo(new[] { 9300001, 9300002 });
		await Assert.That(world.StepsOf("Batman 7.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Done, Begin, Search, ChooseSeries, FindIssue, Done }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_fast_rescrape_that_hits_a_network_error_is_retried_as_a_normal_scrape_at_the_end()
	{
		using var world = new EngineWorld();
		world.FailNetwork("/issue/4000-37566/", times: 2);
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497, b => b.Tags = "CVDB37566");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, Write, Done, Begin, Search, ChooseSeries, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(book.Applied.Count).IsEqualTo(1);
		await Assert.That(world.Log.Snapshot()).Contains("Error rescraping details:");
	}

	[Test]
	public async Task A_fast_rescrape_with_an_invalid_key_ends_the_run_and_the_log_never_holds_the_key()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.Ask) with { ApiKey = EngineWorld.ApiKey } };
		RecordedResponse invalid = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "invalid-key.txt"));
		world.Respond(Synthetic.IssueUrl(37566), invalid.Body, invalid.Status);
		var book = FakeBook.File(Batman497, b => b.Tags = "CVDB37566");

		ScrapeFailedException? failure = null;
		try
		{
			await world.RunAsync(book, FakeBook.File(@"C:\Comics\Zorro 1.cbz"));
		}
		catch (ScrapeFailedException e)
		{
			failure = e;
		}

		await Assert.That(failure).IsNotNull();
		await Assert.That(failure!.Summary).IsEqualTo(new RunSummary(0, 2, false));
		await Assert.That((failure.InnerException as ComicVineException)?.Error).IsEqualTo(ComicVineError.InvalidApiKey);
		await Assert.That(world.Ui.Started.Count).IsEqualTo(1);
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(world.Log.Snapshot().Any(line => line.Contains(EngineWorld.ApiKey))).IsFalse();
		await Assert.That(world.Log.Snapshot().Any(line => line.Contains("InvalidApiKey"))).IsTrue();
	}

	[Test]
	public async Task A_network_error_mid_run_keeps_the_books_already_written_and_leaves_the_failing_book_untouched()
	{
		using var world = new EngineWorld();
		world.FailNetwork("/issue/4000-37566/");
		world.Ui.Series(q => Pick(q, 796));
		var done = FakeBook.File(@"C:\Comics\Batman 1.cbz", b => b.Tags = "CVDB535328");
		var failing = FakeBook.File(Batman497, b => b.Notes = "untouched");

		ScrapeFailedException? failure = null;
		try
		{
			await world.RunAsync(failing, done);
		}
		catch (ScrapeFailedException e)
		{
			failure = e;
		}

		await Assert.That(failure?.Summary).IsEqualTo(new RunSummary(1, 1, false));
		await Assert.That((failure!.InnerException as ComicVineException)?.Error).IsEqualTo(ComicVineError.Network);
		await Assert.That(done.Book.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		await Assert.That(failing.Applied).IsEmpty();
		await Assert.That(failing.Book.Notes).IsEqualTo("untouched");
	}

	[Test]
	public async Task A_rate_limit_during_a_search_ends_the_run()
	{
		using var world = new EngineWorld();

		ScrapeFailedException? failure = null;
		try
		{
			await world.RunAsync(FakeBook.File(@"C:\Comics\rate limited.cbz"));
		}
		catch (ScrapeFailedException e)
		{
			failure = e;
		}

		await Assert.That((failure?.InnerException as ComicVineException)?.Error).IsEqualTo(ComicVineError.RateLimited);
		await Assert.That(world.Ui.OpenQueries).IsEqualTo(0);
	}

	[Test]
	[Arguments("search")]
	[Arguments("series")]
	[Arguments("issue")]
	public async Task Cancel_in_any_dialog_ends_the_run_and_writes_nothing(string dialog)
	{
		using var world = new EngineWorld();
		switch (dialog)
		{
			case "search":
				world.Ui.Series(new SeriesChoice.SearchAgain()).Search(new SearchChoice.Cancel());
				break;
			case "series":
				world.Ui.Series(new SeriesChoice.Cancel());
				break;
			default:
				world.Ui.Series(q => Pick(q, 91273, showIssues: true)).Issue(new IssueChoice.Cancel());
				break;
		}
		var book = FakeBook.File(Batman1);
		var next = FakeBook.File(@"C:\Comics\Zorro 1.cbz");

		RunSummary summary = await world.RunAsync(book, next);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 2, true));
		await Assert.That(world.Ui.Started).IsEquivalentTo(new[] { "Batman 1.cbz:2" }, CollectionOrdering.Matching);
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(next.Applied).IsEmpty();
	}

	[Test]
	public async Task A_cancel_that_arrives_while_a_dialog_is_open_wins_over_its_answer()
	{
		using var world = new EngineWorld();
		using var cts = new CancellationTokenSource();
		world.Ui.Series((q, _) =>
		{
			cts.Cancel();
			return Pick(q, 796);
		});
		var book = FakeBook.File(Batman497);

		RunSummary summary = await world.RunAsync(cts.Token, book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, true));
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(world.ApiPaths().Any(p => p.StartsWith("issues/"))).IsFalse();
	}

	[Test]
	public async Task A_cancel_that_arrives_with_a_skip_forever_answer_writes_no_marker()
	{
		using var world = new EngineWorld();
		using var cts = new CancellationTokenSource();
		world.Ui.Series((_, _) =>
		{
			cts.Cancel();
			return new SeriesChoice.SkipForever();
		});
		var book = FakeBook.File(Batman497);

		RunSummary summary = await world.RunAsync(cts.Token, book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, true));
		await Assert.That(book.Applied).IsEmpty();
	}

	[Test]
	public async Task A_cancel_during_the_issue_request_leaves_the_book_unwritten()
	{
		using var world = new EngineWorld();
		using var cts = new CancellationTokenSource();
		world.BeforeResponse = url =>
		{
			if (url.Contains("/issue/4000-37566/"))
			{
				cts.Cancel();
			}
		};
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497);

		RunSummary summary = await world.RunAsync(cts.Token, book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, true));
		await Assert.That(book.Applied).IsEmpty();
		await Assert.That(world.SeriesDat()).IsEmpty();
	}

	[Test]
	public async Task A_cancel_during_the_scrape_delay_stops_before_the_next_book()
	{
		using var world = new EngineWorld();
		using var cts = new CancellationTokenSource();
		world.Ui.Series((_, _) =>
		{
			cts.Cancel();
			return new SeriesChoice.Skip();
		});

		RunSummary summary = await world.RunAsync(cts.Token, FakeBook.File(Batman1), FakeBook.File(@"C:\Comics\Zorro 1.cbz"));

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 2, true));
		await Assert.That(world.Ui.Started.Count).IsEqualTo(1);
		await Assert.That(world.Clock.Delays).IsEmpty();
	}

	[Test]
	public async Task Books_are_read_and_written_only_on_the_thread_that_runs_the_scrape()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497, b => b.Tags = "x");
		var fast = FakeBook.File(Batman1, b => b.Tags = "CVDB535328");
		int runThread = 0;

		RunSummary summary = await Task.Run(() => SingleThreadContext.Run(() =>
		{
			runThread = Environment.CurrentManagedThreadId;
			return world.RunAsync(book, fast);
		}));

		await Assert.That(summary.Scraped).IsEqualTo(2);
		await Assert.That(book.Threads.Concat(fast.Threads).Distinct()).IsEquivalentTo(new[] { runThread });
	}

	private static Bitmap LeftHalfBlack()
	{
		var image = new Bitmap(64, 64);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(Color.White);
		g.FillRectangle(Brushes.Black, 0, 0, 32, 64);
		return image;
	}
}
