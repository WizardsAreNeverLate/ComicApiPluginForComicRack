using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Engine;

public sealed class IssueMatchTests
{
	[Test]
	public async Task The_one_issue_whose_number_has_the_same_natural_key_wins()
	{
		IssueRef[] issues = Issues("08", "9", "10");

		await Assert.That(IssueMatch.Find(issues, "8")?.Id).IsEqualTo(new IssueId(1));
		await Assert.That(IssueMatch.Find([.. issues.Reverse()], "8.0")?.Id).IsEqualTo(new IssueId(1));
	}

	[Test]
	public async Task A_number_that_matches_more_than_one_issue_is_never_guessed()
	{
		await Assert.That(IssueMatch.Find(Issues("7", "7", "07"), "7")).IsNull();
		// Deviation 32: the natural key matches twice, so the user chooses, whatever the text and the mode.
		await Assert.That(IssueMatch.Find(Issues("7", "07"), "7")).IsNull();
		await Assert.That(IssueMatch.Find(Issues("7", "7"), "07")).IsNull();
		await Assert.That(IssueMatch.Find(Issues("7", "7.0", "8"), "07")).IsNull();
		await Assert.That(IssueMatch.Find(Issues("7", "8"), "07")?.Id).IsEqualTo(new IssueId(1));
	}

	[Test]
	public async Task A_book_without_a_number_takes_only_the_only_issue()
	{
		await Assert.That(IssueMatch.Find(Issues("1"), "")?.Id).IsEqualTo(new IssueId(1));
		await Assert.That(IssueMatch.Find(Issues("1", "2"), "")).IsNull();
		await Assert.That(IssueMatch.Find(Issues(), "")).IsNull();
	}

	[Test]
	public async Task A_number_the_list_lacks_finds_nothing()
	{
		await Assert.That(IssueMatch.Find(Issues("1", "2"), "3")).IsNull();
		await Assert.That(IssueMatch.Find(Issues("1"), "3")).IsNull();
	}

	private static IssueRef[] Issues(params string[] numbers) =>
		[.. numbers.Select((number, i) => new IssueRef(new IssueId(i + 1), number, "", null))];
}
