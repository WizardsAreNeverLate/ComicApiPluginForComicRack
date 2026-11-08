using System;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using TUnit.Assertions.Enums;
using static ComicVineScraper.Engine.StepKind;

namespace ComicVineScraper.Tests.Engine;

/// <summary>One book at a time through each path of the state machine, over the recorded Comic Vine responses.</summary>
public sealed class ScrapeRunTests
{
	private const string Batman497 = @"C:\Comics\Batman 497.cbz";

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

	private static SeriesChoice Pick(SeriesQuestion question, int id, bool showIssues = false) =>
		new SeriesChoice.Pick(question.Candidates.Single(c => c.Series.Id == new SeriesId(id)).Series, showIssues);
}
