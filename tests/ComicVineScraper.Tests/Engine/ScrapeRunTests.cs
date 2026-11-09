using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using ComicVineScraper.Text;
using TUnit.Assertions.Enums;
using static ComicVineScraper.Engine.StepKind;
using ComicBook = cYo.Projects.ComicRack.Engine.ComicBook;

namespace ComicVineScraper.Tests.Engine;

/// <summary>One book at a time through each path of the state machine, over the recorded Comic Vine responses.</summary>
public sealed class ScrapeRunTests
{
	private const string Batman497 = @"C:\Comics\Batman 497.cbz";
	private const string Batman1 = @"C:\Comics\Batman 1.cbz";

	[Test]
	public async Task A_search_then_a_picked_series_finds_the_issue_by_number_and_writes_it()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497);

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, Search, ChooseSeries, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(book.Applied.Count).IsEqualTo(1);
		await Assert.That(book.Book.Series).IsEqualTo("Batman");
		await Assert.That(book.Book.Number).IsEqualTo("497");
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
		await Assert.That(book.Book.GetCustomValue("comicvine_volume")).IsEqualTo("796");
		await Assert.That(book.Book.Notes).Contains("[CVDB37566]");
		await Assert.That(world.SeriesDat()).IsEquivalentTo(new[] { "796" });
		await Assert.That(world.Ui.Queries).IsEquivalentTo(new[] { QueryKind.SeriesSearch });
		await Assert.That(world.Ui.OpenQueries).IsEqualTo(0);
		world.Ui.AssertAllAnswered();
	}

	[Test]
	public async Task The_series_question_ranks_the_candidates_best_first()
	{
		using var world = new EngineWorld();
		world.Ui.Series(new SeriesChoice.Skip());

		await world.RunAsync(FakeBook.File(Batman497));

		SeriesQuestion question = world.Ui.SeriesQuestions.Single();
		await Assert.That(question.SearchTerms).IsEqualTo("Batman");
		await Assert.That(question.Candidates.Select(c => c.Series.Id)).Contains(new SeriesId(796));
		await Assert.That(question.Candidates.Select(c => c.Score)).IsEquivalentTo(question.Candidates.Select(c => c.Score).OrderByDescending(s => s), CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_known_issue_is_rescraped_without_a_question_and_is_not_recorded_as_a_choice()
	{
		using var world = new EngineWorld();
		var book = FakeBook.File(Batman497, b => b.Tags = "CVDB37566");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(1, 0, false));
		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEmpty();
		await Assert.That(world.ApiPaths()).IsEquivalentTo(new[] { "issue/4000-37566/", "volume/4050-796/" }, CollectionOrdering.Matching);
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
		await Assert.That(world.SeriesDat()).IsEmpty();
	}

	[Test]
	public async Task A_skip_marker_skips_the_book_without_a_request()
	{
		using var world = new EngineWorld();
		var book = FakeBook.File(Batman497, b => b.Notes = "cvdbskip");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, Done }, CollectionOrdering.Matching);
		await Assert.That(world.Requests).IsEmpty();
		await Assert.That(book.Applied).IsEmpty();
	}

	[Test]
	public async Task A_stored_series_key_finds_the_issue_without_a_search_and_records_the_series()
	{
		using var world = new EngineWorld();
		var book = FakeBook.File(Batman1, b => b.SetCustomValue("comicvine_volume", "91273"));

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary.Scraped).IsEqualTo(1);
		await Assert.That(world.StepsOf("Batman 1.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(world.ApiPaths()).IsEquivalentTo(new[] { "issues/volume:91273,issue_number:1", "issue/4000-535328/", "volume/4050-91273/" }, CollectionOrdering.Matching);
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		// Python's record_choice runs after every scrape that went through a series, not only after a series the user picked.
		await Assert.That(world.SeriesDat()).IsEquivalentTo(new[] { "91273" });
	}

	[Test]
	public async Task A_cvinfo_file_in_the_book_folder_names_the_series()
	{
		using var world = new EngineWorld();
		string folder = Path.Combine(world.Folder, "Batman");
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, "cvinfo.txt"), "https://comicvine.gamespot.com/batman/4050-796/\r\n");
		var book = FakeBook.File(Path.Combine(folder, "Batman 497.cbz"));

		await world.RunAsync(book);

		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(world.ApiPaths().Take(2)).IsEquivalentTo(new[] { "volume/4050-796/", "issues/volume:796,issue_number:497" }, CollectionOrdering.Matching);
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
	}

	[Test]
	public async Task A_search_that_finds_nothing_asks_for_new_terms_and_names_the_failed_ones()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Search("  Batman ")).Series(new SeriesChoice.Skip());
		var book = FakeBook.File(@"C:\Comics\zzqxv qqzzx.cbz");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.StepsOf("zzqxv qqzzx.cbz")).IsEquivalentTo(new[] { Begin, Search, Search, ChooseSeries, Done }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "search:zzqxv qqzzx|zzqxv qqzzx", "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(book.Applied).IsEmpty();
	}

	[Test]
	public async Task A_book_without_a_series_name_asks_for_terms_first()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Skip());

		RunSummary summary = await world.RunAsync(FakeBook.File(""));

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "search:|" }, CollectionOrdering.Matching);
		await Assert.That(world.Requests).IsEmpty();
	}

	[Test]
	public async Task Blank_search_terms_cancel_the_run_as_the_search_form_does()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Search("   "));

		// The nameless book's key, "uniqueid-0", sorts before Zorro's.
		RunSummary summary = await world.RunAsync(FakeBook.File(""), FakeBook.File(@"C:\Comics\Zorro 1.cbz"));

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 2, true));
		await Assert.That(world.Requests).IsEmpty();
	}

	[Test]
	public async Task Search_again_asks_for_terms_without_naming_a_failed_search()
	{
		using var world = new EngineWorld();
		world.Ui.Series(new SeriesChoice.SearchAgain()).Search(new SearchChoice.Search("Batman")).Series(new SeriesChoice.Skip());

		await world.RunAsync(FakeBook.File(Batman497));

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman", "search:Batman|", "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman 497.cbz")).IsEquivalentTo(new[] { Begin, Search, ChooseSeries, Search, ChooseSeries, Done }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Show_issues_loads_the_whole_list_once_and_offers_the_matching_issue_as_the_hint()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 91273, showIssues: true)).Issue(q => new IssueChoice.Pick(q.Hint!));
		var book = FakeBook.File(Batman1);

		await world.RunAsync(book);

		IssueQuestion question = world.Ui.IssueQuestions.Single();
		await Assert.That(question.Hint!.Id).IsEqualTo(new IssueId(535328));
		await Assert.That(question.Issues.Count).IsEqualTo(163);
		await Assert.That(question.Issues.Select(i => i.IssueNumber)).IsEquivalentTo(question.Issues.Select(i => i.IssueNumber).OrderBy(n => n, NaturalOrder.Instance), CollectionOrdering.Matching);
		await Assert.That(world.ApiPaths().Where(p => p.StartsWith("issues/"))).IsEquivalentTo(new[] { "issues/volume:91273", "issues/volume:91273" }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Queries).IsEquivalentTo(new[] { QueryKind.SeriesSearch, QueryKind.IssueList }, CollectionOrdering.Matching);
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
	}

	[Test]
	public async Task With_confirm_issue_a_picked_series_still_finds_its_issue_without_asking()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.ConfirmIssue) };
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497);

		await world.RunAsync(book);

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
	}

	[Test]
	public async Task With_confirm_issue_a_series_from_elsewhere_shows_the_issue_dialog_and_a_skip_keeps_the_series()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.ConfirmIssue) };
		world.Ui.Issue(new IssueChoice.Skip()).Issue(new IssueChoice.Skip());
		var first = FakeBook.File(Batman1, b => b.SetCustomValue("comicvine_volume", "91273"));
		var second = FakeBook.File(@"C:\Comics\Batman 2.cbz");

		RunSummary summary = await world.RunAsync(first, second);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 2, false));
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "issue:91273:535328", "issue:91273:" + Issue2Of91273(world) }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman 2.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Done }, CollectionOrdering.Matching);
		await Assert.That(world.ApiPaths().Count(p => p == "issues/volume:91273")).IsEqualTo(2);
	}

	[Test]
	public async Task Show_issues_then_skip_forgets_the_series_for_the_next_book()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 91273, showIssues: true)).Issue(new IssueChoice.Skip()).Series(new SeriesChoice.Skip());

		await world.RunAsync(FakeBook.File(Batman1), FakeBook.File(@"C:\Comics\Batman 2.cbz"));

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman", "issue:91273:535328", "series:Batman" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_picked_series_serves_the_next_book_of_the_same_series_without_a_question()
	{
		using var world = new EngineWorld();
		world.Respond(Synthetic.IssueNumberUrl(91273, "2"), Synthetic.IssueList((9200002, "2")));
		world.Respond(Synthetic.IssueUrl(9200002), Synthetic.Issue(9200002, 91273, "2"));
		world.Ui.Series(q => Pick(q, 91273));
		var second = FakeBook.File(@"C:\Comics\Batman 2.cbz");

		RunSummary summary = await world.RunAsync(FakeBook.File(Batman1), second);

		await Assert.That(summary.Scraped).IsEqualTo(2);
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman 2.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Write, Done }, CollectionOrdering.Matching);
		await Assert.That(second.Book.GetCustomValue("comicvine_volume")).IsEqualTo("91273");
	}

	[Test]
	public async Task A_number_that_appears_twice_is_never_guessed()
	{
		using var world = new EngineWorld();
		world.Respond(Synthetic.IssueListUrl(9999), Synthetic.IssueList((9100003, "7"), (9100004, "7"), (9100005, "8")));
		world.Ui.Issue(new IssueChoice.Skip());

		await world.RunAsync(FakeBook.File(@"C:\Comics\Odd 7.cbz", b => b.SetCustomValue("comicvine_volume", "9999")));

		await Assert.That(world.ApiPaths()).IsEquivalentTo(new[] { "issues/volume:9999,issue_number:7", "issues/volume:9999" }, CollectionOrdering.Matching);
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "issue:9999:" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_number_with_the_same_natural_key_is_found_in_the_loaded_list()
	{
		using var world = new EngineWorld();
		world.Respond(Synthetic.IssueListUrl(9999), Synthetic.IssueList((9100003, "7"), (9100004, "7"), (9100005, "8.0")));
		world.Respond(Synthetic.IssueNumberUrl(9999, "8"), Synthetic.IssueList());
		world.Respond(Synthetic.IssueUrl(9100005), Synthetic.Issue(9100005, 796, "8.0"));
		world.Ui.Issue(new IssueChoice.Skip());
		var eight = FakeBook.File(@"C:\Comics\Odd 8.cbz", b => b.SetCustomValue("comicvine_volume", "9999"));

		await world.RunAsync(FakeBook.File(@"C:\Comics\Odd 7.cbz", b => b.SetCustomValue("comicvine_volume", "9999")), eight);

		await Assert.That(eight.Book.GetCustomValue("comicvine_issue")).IsEqualTo("9100005");
		await Assert.That(world.ApiPaths().Count(p => p.StartsWith("issues/volume:9999,"))).IsEqualTo(1);
	}

	[Test]
	public async Task A_book_without_a_number_takes_the_only_issue_of_its_series()
	{
		using var world = new EngineWorld();
		world.Respond(Synthetic.IssueUrl(354236), Synthetic.Issue(354236, 796, "1"));
		var book = new FakeBook(new ComicBook { Series = "Batman" });
		book.Book.SetCustomValue("comicvine_volume", "51658");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary.Scraped).IsEqualTo(1);
		await Assert.That(world.Ui.Asked).IsEmpty();
		await Assert.That(book.Book.GetCustomValue("comicvine_issue")).IsEqualTo("354236");
		// A fileless book gets the cover as its thumbnail; this one cannot be downloaded, so the fields are written without it.
		await Assert.That(world.Requests.Last()).IsEqualTo("https://comicvine.gamespot.com/a/uploads/scale_medium/i354236.jpg");
		await Assert.That(book.App.Thumbnails).IsEmpty();
		await Assert.That(book.Applied.Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_series_with_no_issues_warns_and_goes_back_to_a_search()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Skip());

		await world.RunAsync(FakeBook.File(@"C:\Comics\Batman.cbz", b => b.SetCustomValue("comicvine_volume", "9997")));

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "no-issues:9997", "search:Batman|" }, CollectionOrdering.Matching);
		await Assert.That(world.StepsOf("Batman.cbz")).IsEquivalentTo(new[] { Begin, FindIssue, Search, Done }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task The_no_issues_warning_gets_the_runs_token_and_a_cancel_there_ends_the_run()
	{
		using var world = new EngineWorld();
		using var run = new CancellationTokenSource();
		bool warningSawCancel = false;
		world.Ui.WhileNoIssuesOpen = ct =>
		{
			run.Cancel();
			warningSawCancel = ct.IsCancellationRequested;
		};

		RunSummary summary = await world.RunAsync(run.Token, FakeBook.File(@"C:\Comics\Batman.cbz", b => b.SetCustomValue("comicvine_volume", "9997")));

		await Assert.That(warningSawCancel).IsTrue();
		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, Cancelled: true));
		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "no-issues:9997" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Back_from_the_issue_dialog_returns_to_the_same_series_list()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 91273, showIssues: true)).Issue(new IssueChoice.Back()).Series(new SeriesChoice.Skip());

		await world.RunAsync(FakeBook.File(Batman1));

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "series:Batman", "issue:91273:535328", "series:Batman" }, CollectionOrdering.Matching);
		await Assert.That(world.ApiPaths().Count(p => p.StartsWith("search/"))).IsEqualTo(1);
	}

	[Test]
	public async Task Back_from_the_issue_dialog_of_a_stored_series_asks_for_a_search()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.ConfirmIssue) };
		world.Ui.Issue(new IssueChoice.Back()).Search(new SearchChoice.Skip());

		await world.RunAsync(FakeBook.File(Batman1, b => b.SetCustomValue("comicvine_volume", "91273")));

		await Assert.That(world.Ui.Asked).IsEquivalentTo(new[] { "issue:91273:535328", "search:Batman|" }, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("search")]
	[Arguments("series")]
	[Arguments("issue")]
	public async Task Skip_forever_marks_the_book_with_CVDBSKIP_from_any_dialog(string dialog)
	{
		using var world = new EngineWorld();
		switch (dialog)
		{
			case "search":
				world.Ui.Series(new SeriesChoice.SearchAgain()).Search(new SearchChoice.SkipForever());
				break;
			case "series":
				world.Ui.Series(new SeriesChoice.SkipForever());
				break;
			default:
				world.Ui.Series(q => Pick(q, 91273, showIssues: true)).Issue(new IssueChoice.SkipForever());
				break;
		}
		var book = FakeBook.File(Batman1, b => b.Notes = "mine");

		RunSummary summary = await world.RunAsync(book);

		await Assert.That(summary).IsEqualTo(new RunSummary(0, 1, false));
		await Assert.That(world.StepsOf("Batman 1.cbz").AsEnumerable().Reverse().Take(2)).IsEquivalentTo(new[] { Done, SkipForever }, CollectionOrdering.Matching);
		await Assert.That(book.Applied.Count).IsEqualTo(1);
		await Assert.That(book.Book.Notes).StartsWith("mine");
		await Assert.That(book.Book.Notes).Contains("CVDBSKIP");
		// Python saves every field it read, so the series and number parsed from the file name are written too.
		await Assert.That(book.Book.Series).IsEqualTo("Batman");
		await Assert.That(book.Book.GetCustomValue("comicvine_issue") ?? "").IsEqualTo("");
	}

	[Test]
	[Arguments(BookFields.Tags, "Tags")]
	[Arguments(BookFields.Notes, "Notes")]
	[Arguments(BookFields.None, "Tags,Notes")]
	public async Task Skip_forever_logs_where_it_wrote_the_flag(BookFields update, string where)
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.Ask) with { Update = update } };
		world.Ui.Series(new SeriesChoice.SkipForever());

		await world.RunAsync(FakeBook.File(Batman1));

		string[] log = [.. world.Log.Snapshot().Where(line => line.StartsWith("Added CVDBSKIP", StringComparison.Ordinal))];
		await Assert.That(log).IsEquivalentTo(where.Split(',').Select(name => $"Added CVDBSKIP flag to comic book '{name}'"), CollectionOrdering.Matching);
	}

	[Test]
	public async Task The_write_merges_into_the_book_as_it_is_now_not_as_it_was_read_at_the_start()
	{
		using var world = new EngineWorld();
		var book = FakeBook.File(Batman497, b => b.Notes = "before");
		world.Ui.Series(q =>
		{
			book.Book.Notes = "edited while the dialog was open";
			return Pick(q, 796);
		});

		await world.RunAsync(book);

		await Assert.That(book.Book.Notes).StartsWith("edited while the dialog was open");
		await Assert.That(book.Book.Notes).Contains("[CVDB37566]");
	}

	[Test]
	public async Task A_fileless_book_gets_the_cover_the_user_preferred_as_its_thumbnail()
	{
		using var world = new EngineWorld();
		var preferred = new Uri("https://comicvine.gamespot.com/a/uploads/original/alternate-497.jpg");
		world.Covers.Prefer(new IssueId(37566), preferred);
		using var cover = new Bitmap(8, 8);
		world.ImageFor = url => url == preferred.AbsoluteUri ? cover : null;
		world.Ui.Search(new SearchChoice.Search("Batman")).Series(q => Pick(q, 796));
		var book = new FakeBook(new ComicBook { Number = "497" });

		await world.RunAsync(book);

		await Assert.That(book.App.Thumbnails.Count).IsEqualTo(1);
		await Assert.That(world.Requests.Count(r => r.Contains("/uploads/"))).IsEqualTo(1);
	}

	[Test]
	public async Task A_kept_thumbnail_is_not_downloaded_again()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Search("Batman")).Series(q => Pick(q, 796));
		var book = new FakeBook(new ComicBook { Number = "497", CustomThumbnailKey = "mine" });

		await world.RunAsync(book);

		await Assert.That(book.Applied.Count).IsEqualTo(1);
		await Assert.That(world.Requests.Any(r => r.Contains("/uploads/"))).IsFalse();
	}

	private static readonly string[] MergeLabels =
	[
		"Series", "Issue Number", "Title", "Crossovers", "Summary", "Release Date", "Publish Date", "Volume", "Imprint", "Publisher",
		"Characters", "Teams", "Locations", "Writers", "Pencillers", "Inkers", "Colorists", "Letterers", "CoverArtists", "Editors",
		"Webpage", "Rating", "Tags", "Notes", "Issue Key", "Series Key", "Cover Art URL",
	];

	private const string MergeHeader = "setting values for this comic book ('*' = changed):";

	[Test]
	public async Task The_write_logs_one_line_per_field_in_Pythons_order_and_marks_the_changes()
	{
		using var world = new EngineWorld();
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497, b => { b.Series = "Batman"; b.Number = "497"; b.Title = "Old title"; });

		await world.RunAsync(book);

		string[] log = [.. world.Log.Snapshot()];
		int header = Array.IndexOf(log, MergeHeader);
		string[] lines = [.. log.Skip(header + 1).Take(MergeLabels.Length)];
		await Assert.That(header).IsGreaterThan(Array.IndexOf(log, "querying comicvine for issue details..."));
		await Assert.That(lines.Select(line => line.Substring(5, 15).TrimEnd())).IsEquivalentTo(MergeLabels, CollectionOrdering.Matching);
		await Assert.That(lines.All(line => line.StartsWith("--> ", StringComparison.Ordinal) && line.Substring(20, 2) == ": ")).IsTrue();
		await Assert.That(lines[0]).IsEqualTo("-->  Series         : Batman");
		await Assert.That(lines[2]).StartsWith("--> *Title          : ");
		await Assert.That(lines[24]).IsEqualTo("--> *Issue Key      : 37566");
		await Assert.That(lines[25]).IsEqualTo("--> *Series Key     : 796");
		await Assert.That(lines[26]).StartsWith("-->  Cover Art URL  : https://");
		await Assert.That(log.Any(line => line.Contains(EngineWorld.ApiKey))).IsFalse();
	}

	[Test]
	public async Task A_field_that_is_not_written_logs_skipped()
	{
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.Ask) with { Update = BookFields.Series } };
		world.Ui.Series(q => Pick(q, 796));
		var book = FakeBook.File(Batman497);

		await world.RunAsync(book);

		string[] log = [.. world.Log.Snapshot()];
		string[] lines = [.. log.Skip(Array.IndexOf(log, MergeHeader) + 1).Take(MergeLabels.Length)];
		await Assert.That(lines[2]).IsEqualTo("-->  Title          : --- skipped ---");
		await Assert.That(lines[0]).StartsWith("-->  Series");
	}

	[Test]
	public async Task A_thumbnail_ComicRack_refuses_is_logged_after_the_field_lines()
	{
		using var world = new EngineWorld();
		using var cover = new Bitmap(8, 8);
		world.ImageFor = _ => cover;
		world.Ui.Search(new SearchChoice.Search("Batman")).Series(q => Pick(q, 796));
		var book = new FakeBook(new ComicBook { Number = "497" }) { App = new Plugin.FakeApplication { Accepts = false } };

		await world.RunAsync(book);

		string[] log = [.. world.Log.Snapshot()];
		int refused = Array.FindIndex(log, line => line.StartsWith("ERROR: can't set thumbnail: https://", StringComparison.Ordinal));
		await Assert.That(refused).IsGreaterThan(Array.IndexOf(log, MergeHeader) + MergeLabels.Length);
	}

	[Test]
	public async Task A_thumbnail_that_cannot_be_downloaded_is_logged_after_the_cover_line()
	{
		using var world = new EngineWorld();
		world.Ui.Search(new SearchChoice.Search("Batman")).Series(q => Pick(q, 796));
		var book = new FakeBook(new ComicBook { Number = "497" });

		await world.RunAsync(book);

		string[] log = [.. world.Log.Snapshot()];
		int failed = Array.FindIndex(log, line => line.StartsWith("ERROR: can't download thumbnail: https://", StringComparison.Ordinal));
		await Assert.That(failed).IsEqualTo(Array.FindIndex(log, line => line.Contains("Cover Art URL")) + 1);
	}

	internal static SeriesChoice Pick(SeriesQuestion question, int id, bool showIssues = false) =>
		new SeriesChoice.Pick(question.Candidates.Single(c => c.Series.Id == new SeriesId(id)).Series, showIssues);

	private static int Issue2Of91273(EngineWorld world) => world.Ui.IssueQuestions[0].Issues.Single(i => i.IssueNumber == "2").Id.Value;
}

internal static class Configs
{
	public static Configuration With(ChooseMode choose) => new() { Advanced = AdvancedSettings.Parse(null), Choose = choose };
}
