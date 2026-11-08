using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Matching;
using ComicVineScraper.Model;
using TUnit.Assertions.Enums;

namespace ComicVineScraper.Tests.Matching;

public sealed class MatchScoreTests
{
	private static readonly BookValues Batman12 = new() { Series = "Batman", Number = "12", Published = PartialDate.Of(1990, null, null) };

	private static SeriesRef Series(int id, string name, int? year = 1940, string publisher = "DC Comics", int count = 50) =>
		new(new SeriesId(id), name, year, publisher, count, null);

	[Test]
	public async Task Adds_the_name_issue_count_year_and_recency_terms()
	{
		// name +5, issue count +100, year 0, recency -(2026 - 1940) / 100
		await Assert.That(MatchScore.Compute(Batman12, Series(1, "Batman"), _ => false, 2026)).IsEqualTo(105 - 0.86);
		// "the" unmatched -1, a mirror publisher -6, chosen before +7, a series started after the book -500
		await Assert.That(MatchScore.Compute(Batman12, Series(2, "The Batman", 2004, "Panini Comics"), _ => true, 2026)).IsEqualTo(100 + 4 - 6 + 7 - 500 - 0.22);
	}

	[Test]
	public async Task Rank_puts_the_best_first_and_keeps_Comic_Vine_order_for_ties()
	{
		SeriesRef[] found = [Series(1, "Detective Comics"), Series(2, "Batman", 1940), Series(3, "Batman", 1940), Series(4, "Batman", 2011)];

		RankedSeries[] ranked = [.. MatchScore.Rank(Batman12, found, _ => false, 2026)];

		await Assert.That(ranked.Select(r => r.Series.Id.Value)).IsEquivalentTo(new[] { 2, 3, 1, 4 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task SeriesFilter_keeps_Comic_Vine_order()
	{
		SeriesRef[] found = [Series(5, "E", 2001), Series(1, "A", 1800), Series(4, "D", null, "Marvel"), Series(2, "B", 1800, count: 900), Series(3, "C", 2001, "Panini")];
		var settings = new SearchSettings([], ["panini"], 1900, 2100, 500, 100);

		await Assert.That(SeriesFilter.Apply(found, settings).Select(s => s.Id.Value)).IsEquivalentTo(new[] { 5, 4, 2 }, CollectionOrdering.Matching);
	}
}
