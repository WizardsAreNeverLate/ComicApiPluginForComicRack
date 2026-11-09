using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.Model;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.Engine;

/// <summary>Steps 3 and 4 of scrapeengine.__choose_issue_ref: the issue a book's number picks from a series' issue list without asking.</summary>
internal static class IssueMatch
{
	/// <summary>
	/// Null when the user must choose. Two or more issues whose numbers share the book's natural key (such as 7 and 7.0, or
	/// a book numbered 07 against two 7s) are never guessed between, in any mode. A book with no number takes the series'
	/// only issue.
	/// </summary>
	public static IssueRef? Find(IReadOnlyList<IssueRef> issues, string number)
	{
		if (number.Length == 0)
		{
			return issues.Count == 1 ? issues[0] : null;
		}
		IssueRef[] matches = [.. issues.Where(issue => NaturalOrder.KeysEqual(issue.IssueNumber, number)).Take(2)];
		return matches.Length == 1 ? matches[0] : null;
	}
}
