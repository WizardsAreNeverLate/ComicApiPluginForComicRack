using System.Collections.Generic;
using System.Threading.Tasks;
using ComicApiPlugin.Matching;

namespace ComicApiPlugin.Tests.Matching;

public sealed class CoverAutoAcceptTests
{
	private static readonly CoverSimilarity Match = new(0.95, 0.49);
	private static readonly CoverSimilarity Other = new(0.10, 0.02);

	[Test]
	public async Task Accepts_the_one_candidate_that_matches()
	{
		string? picked = CoverAutoAccept.PickSingleMatch(Scored(("a", Other), ("b", Match), ("c", null)));

		await Assert.That(picked).IsEqualTo("b");
	}

	[Test]
	public async Task Asks_when_nothing_matches()
	{
		await Assert.That(CoverAutoAccept.PickSingleMatch(Scored(("a", Other), ("b", null)))).IsNull();
	}

	[Test]
	public async Task Asks_when_two_candidates_match()
	{
		await Assert.That(CoverAutoAccept.PickSingleMatch(Scored(("a", Match), ("b", Match), ("c", Other)))).IsNull();
	}

	[Test]
	public async Task A_match_by_either_measure_counts()
	{
		await Assert.That(new CoverSimilarity(0.82, 0.0).IsMatch).IsTrue();
		await Assert.That(new CoverSimilarity(0.30, 0.30).IsMatch).IsTrue();
		await Assert.That(new CoverSimilarity(0.69, 0.24).IsMatch).IsFalse();
	}

	[Test]
	public async Task Scores_print_for_the_debug_log()
	{
		await Assert.That(Match.ToString()).IsEqualTo("correlation 0.95, features 0.49");
	}

	private static IReadOnlyList<(string, CoverSimilarity?)> Scored(params (string, CoverSimilarity?)[] items) => items;
}
