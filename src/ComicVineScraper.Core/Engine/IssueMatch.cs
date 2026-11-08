using System.Collections.Generic;
using System.Linq;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.Engine;

/// <summary>Steps 3 and 4 of scrapeengine.__choose_issue_ref: the issue a book's number picks from a series' issue list without asking.</summary>
internal static class IssueMatch
{
	/// <summary>
	/// Null when the user must choose. A number that appears more than once, compared as text, is never guessed. Otherwise
	/// the first issue whose number has the same natural key wins; Python took the first in set order, which is undefined.
	/// A book with no number takes the series' only issue.
	/// </summary>
	public static IssueRef? Find(IReadOnlyList<IssueRef> issues, string number)
	{
		if (number.Length == 0)
		{
			return issues.Count == 1 ? issues[0] : null;
		}
		if (issues.Count(issue => issue.IssueNumber == number) > 1)
		{
			return null;
		}
		return issues.FirstOrDefault(issue => NaturalOrder.KeysEqual(issue.IssueNumber, number));
	}
}
