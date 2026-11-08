using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Engine;

public sealed class IssueMatchTests
{
	[Test]
	public async Task The_first_issue_whose_number_has_the_same_natural_key_wins()
	{
		IssueRef[] issues = Issues("08", "8.0", "9");

		await Assert.That(IssueMatch.Find(issues, "8")?.Id).IsEqualTo(new IssueId(1));
		await Assert.That(IssueMatch.Find([.. issues.Reverse()], "8")?.Id).IsEqualTo(new IssueId(2));
	}

	[Test]
	public async Task A_number_listed_twice_as_the_same_text_is_never_guessed()
	{
		await Assert.That(IssueMatch.Find(Issues("7", "7", "07"), "7")).IsNull();
		// The refusal compares text, as Python's does, so these are guesses. Python took one of the two matches in set
		// order; the port takes the first in Comic Vine order (deviations.md #32).
		await Assert.That(IssueMatch.Find(Issues("7", "07"), "7")?.Id).IsEqualTo(new IssueId(1));
		await Assert.That(IssueMatch.Find(Issues("7", "7"), "07")?.Id).IsEqualTo(new IssueId(1));
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
