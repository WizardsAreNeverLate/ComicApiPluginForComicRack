using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>IssueMatch.Find against ScrapeEngine.__choose_issue_ref over a loaded issue list, with the IssueForm stubbed.</summary>
public sealed class IssueMatchOracleTests
{
	// No "nan" and no text after a fraction: natural_key differs there on purpose (deviations.md #16 and #19).
	private static readonly string[] Numbers =
	[
		"1", "01", "1.0", "001", "2", "7", "7", "07", "7.0", "007", "8", "8.0", "08", "½", "0.5", ".5", "1a", "1A", "3", "abc", "ABC", "-1", "1.5", "1½", "10", "", "x",
	];

	[Test]
	public async Task Find_matches_choose_issue_ref_outside_the_ambiguous_numbers_where_it_asks()
	{
		var random = new Random(71);
		List<string> differences = [];
		int compared = 0, setOrder = 0, picked = 0, refused = 0;
		for (int i = 0; i < 4000; i++)
		{
			IssueRef[] issues = [.. Enumerable.Range(0, random.Next(1, 7)).Select(k => new IssueRef(new IssueId(100 + k), BookCorpusPick(random), "", null))];
			string number = BookCorpusPick(random);
			bool force = random.Next(2) == 0;

			int naturalMatches = issues.Count(issue => NaturalOrder.KeysEqual(issue.IssueNumber, number));
			bool textDuplicate = issues.Count(issue => issue.IssueNumber == number) > 1;
			if (number.Length > 0 && !textDuplicate && naturalMatches > 1)
			{
				// deviations.md #32: Python takes the first match in set order; the port never guesses and asks.
				setOrder++;
				if (IssueMatch.Find(issues, number) is not null)
				{
					differences.Add($"[{string.Join(", ", issues.Select(x => x.IssueNumber))}] '{number}': several natural-key matches, C# did not answer dialog");
				}
				continue;
			}

			var python = (object[])Oracle.Call("cvs_engine_oracle", "choose_issue",
				issues.Select(issue => new object[] { issue.Id.Value, issue.IssueNumber }).ToArray(), number, force)!;
			string expected = $"{python[0]} {python[1]}";
			IssueRef? found = IssueMatch.Find(issues, number);
			string actual = found is not null && !force ? $"ok {found.Id.Value}" : $"dialog {found?.Id.Value}";
			compared++;
			picked += actual.StartsWith("ok", StringComparison.Ordinal) ? 1 : 0;
			refused += textDuplicate && number.Length > 0 ? 1 : 0;
			if (actual != expected)
			{
				differences.Add($"[{string.Join(", ", issues.Select(x => x.IssueNumber))}] '{number}' force={force}: python {expected}, C# {actual}");
			}
		}

		await Assert.That(differences.Take(20)).IsEmpty();
		await Assert.That(compared).IsGreaterThan(3500);
		await Assert.That(picked).IsGreaterThan(400);
		await Assert.That(refused).IsGreaterThan(30);
		await Assert.That(setOrder).IsGreaterThan(50);
	}

	private static string BookCorpusPick(Random random) => BookCorpus.Pick(random, Numbers);
}
