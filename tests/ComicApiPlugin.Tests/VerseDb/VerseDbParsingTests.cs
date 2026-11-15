using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Model;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.VerseDb;

public sealed class VerseDbParsingTests
{
	private static VerseDbHttp Client(string body) =>
		new(new VerseDbHandler(_ => VerseDbFixtures.Json(HttpStatusCode.OK, body)), VerseDbFixtures.Token, "test");

	[Test]
	public async Task A_clear_cover_match_lists_its_candidate_with_the_medium_cover()
	{
		using VerseDbHttp client = Client(VerseDbFixtures.ClearCoverLookup);

		CoverLookup lookup = await client.LookUpCoverAsync([1], CancellationToken.None);

		await Assert.That(lookup.ClearMatch).IsTrue();
		CoverCandidate candidate = lookup.Candidates.Single();
		await Assert.That(candidate).IsEqualTo(new CoverCandidate(
			new VerseDbIssueId(5432), "Batman", 1940, 1, "12", "The Joker", "DC Comics", "1990-06-01", new System.Uri("https://img.versedb.com/m/12.jpg")));
	}

	[Test]
	public async Task An_unclear_match_keeps_the_order_and_tolerates_nulls()
	{
		using VerseDbHttp client = Client(VerseDbFixtures.UnclearCoverLookup);

		CoverLookup lookup = await client.LookUpCoverAsync([1], CancellationToken.None);

		await Assert.That(lookup.ClearMatch).IsFalse();
		await Assert.That(lookup.Candidates.Select(c => c.IssueId.Value).ToArray()).IsEquivalentTo(new[] { 11, 12 });
		await Assert.That(lookup.Candidates[0].Title).IsEqualTo("");
		await Assert.That(lookup.Candidates[0].StartYear).IsNull();
		await Assert.That(lookup.Candidates[0].Publisher).IsEqualTo("");
		await Assert.That(lookup.Candidates[0].CoverUrl).IsEqualTo(new System.Uri("https://img.versedb.com/full/11.jpg"));
		await Assert.That(lookup.Candidates[1].CoverUrl).IsNull();
	}

	[Test]
	public async Task A_lookup_with_no_candidates_is_empty()
	{
		using VerseDbHttp client = Client("""{ "matched_by": "image", "clear_match": false, "issue_id": null, "variant_id": null, "candidates": [] }""");

		CoverLookup lookup = await client.LookUpCoverAsync([1], CancellationToken.None);

		await Assert.That(lookup.Candidates).IsEmpty();
	}

	[Test]
	public async Task Issue_detail_maps_to_metadata_with_roles_and_cleaned_text()
	{
		using VerseDbHttp client = Client(VerseDbFixtures.Issue);

		IssueMetadata issue = await client.GetIssueAsync(new VerseDbIssueId(5432), CancellationToken.None);

		await Assert.That(issue.SeriesName).IsEqualTo("Batman");
		await Assert.That(issue.VolumeYear).IsEqualTo(1940);
		await Assert.That(issue.IssueNumber).IsEqualTo("12");
		await Assert.That(issue.Title).IsEqualTo("The Joker");
		await Assert.That(issue.Summary).IsEqualTo("The Joker returns & he is angry.\nSecond paragraph.");
		await Assert.That(issue.Publisher).IsEqualTo("DC Comics");
		await Assert.That(issue.Imprint).IsEqualTo("Vertigo");
		await Assert.That(issue.Published).IsEqualTo(PartialDate.Of(1990, 6, 1));
		await Assert.That(issue.Released).IsEqualTo(PartialDate.Of(1990, 4, 17));
		await Assert.That(issue.Writers).IsEquivalentTo(["Grant Morrison"]);
		await Assert.That(issue.Pencillers).IsEquivalentTo(["Jim Lee", "Pat Penciller"]);
		await Assert.That(issue.Inkers).IsEquivalentTo(["Jim Lee", "Scott Williams"]);
		await Assert.That(issue.Colorists).IsEquivalentTo(["Alex Sinclair"]);
		await Assert.That(issue.Letterers).IsEquivalentTo(["Richard Starkings"]);
		await Assert.That(issue.CoverArtists).IsEquivalentTo(["Cover Person"]);
		await Assert.That(issue.Editors).IsEquivalentTo(["Bob Schreck"]);
		await Assert.That(issue.Characters).IsEquivalentTo(["Batman", "Joker"]);
		await Assert.That(issue.Teams).IsEquivalentTo(["Bat Family"]);
		await Assert.That(issue.Crossovers).IsEquivalentTo(["Knightfall"]);
		await Assert.That(issue.Locations).IsEmpty();
		await Assert.That(issue.Webpage).IsEqualTo("");
		await Assert.That(issue.Rating).IsEqualTo(4.5f);
	}

	[Test]
	public async Task Issue_detail_with_nulls_everywhere_maps_to_blanks()
	{
		using VerseDbHttp client = Client(VerseDbFixtures.SparseIssue);

		IssueMetadata issue = await client.GetIssueAsync(new VerseDbIssueId(9), CancellationToken.None);

		await Assert.That(issue.SeriesName).IsEqualTo("");
		await Assert.That(issue.VolumeYear).IsNull();
		await Assert.That(issue.IssueNumber).IsEqualTo("7");
		await Assert.That(issue.Summary).IsEqualTo("");
		await Assert.That(issue.Publisher).IsEqualTo("");
		await Assert.That(issue.Published.IsBlank).IsTrue();
		await Assert.That(issue.Writers).IsEmpty();
		await Assert.That(issue.Locations).IsEquivalentTo(["Gotham", "Metropolis"]);
		await Assert.That(issue.Rating).IsNull();
	}

	[Test]
	public async Task A_body_that_is_not_JSON_is_a_VerseDbException()
	{
		using VerseDbHttp client = Client("<html>nope</html>");

		VerseDbException? error = await Assert.That(async () => await client.GetIssueAsync(new VerseDbIssueId(1), CancellationToken.None)).Throws<VerseDbException>();

		await Assert.That(error!.Status).IsNull();
	}

	[Test]
	[Arguments("2020-03-09", 2020, 3, 9)]
	[Arguments("2020-03", 2020, 3, null)]
	[Arguments("2020", 2020, null, null)]
	[Arguments("2020-13-40", 2020, null, null)]
	[Arguments("", null, null, null)]
	public async Task Dates_keep_the_parts_they_have(string text, int? year, int? month, int? day)
	{
		await Assert.That(VerseDbJson.ParseDate(text)).IsEqualTo(PartialDate.Of(year, month, day));
	}
}
