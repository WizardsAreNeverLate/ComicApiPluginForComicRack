using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Model;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>KeyMarkers against ComicBook.__extract_issue_ref, __add_key_to_tags, and __add_key_to_notes.</summary>
public sealed class KeyMarkerOracleTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	internal static readonly string[][] TagCorpus =
	[
		[], ["CVDB123"], ["foo", "CVDB123"], ["cvdb45", "bar"], ["CVDBSKIP"], ["cvdbskip x"], ["ComicVine[77"],
		["ComicVine [78"], ["ComicVine_[79"], ["comicvine[5"], ["CVDB0"], ["CVDB007"], ["CVDB12.5"],
		["a", "CVDB9", "b", "CVDB10"], ["CVDB99999999999"], ["CVDB2147483647"], ["CVDB2147483648"], ["xCVDB5y"], ["CVDB"],
		["CVDB١٢"], ["CVDB12", "CVDB123"], ["CVDB123", "CVDB12"], ["Scraped"], ["Marvel", "Favourite"], ["CVDB5", "cvdb5"],
	];

	internal static readonly string[] NoteCorpus =
	[
		"",
		"Scraped metadata from ComicVine [CVDB123].",
		"Scraped metadata from ComicVine [CVDB123] on 2013.05.14 21:43:06.",
		"hello\n\nScraped metadata from ComicVine [CVDB5] on 2013.05.14 9:43:06 PM.",
		"CVDB77 by hand",
		"ComicVine [88 legacy",
		"two CVDB1 CVDB2",
		"CVDBSKIP",
		"Scraped metadata from ComicVine [CVDBSKIP].",
		"scraped METADATA from comicvine [cvdb9].",
		"Scraped by me. Scraped metadata from ComicVine [CVDB4].",
		"Scraped metadata from ComicVine [CVDB12] on 2013.05.14 21:43:06.\nScraped metadata from ComicVine [CVDB12].",
		"Scraped CVDB123 on 2013.05.14 21:43:06. tail",
		"  padded  ",
		"Scraped metadata from ComicVine [CVDB0].",
		"line1\r\nScraped metadata from ComicVine [CVDB6] on 2010.01.02 03:04:05.\r\nline3",
		"Scraped metadata from ComicVine [CVDB12] on 2013.05.14 21:43:06.x",
		"Scraped metadata from ComicVine [CVDB007].",
		"Scraped metadata from ComicVine [CVDB١٢].",
		"Scraped metadata from ComicVine [CVDB3]",
		"user note,",
	];

	private static readonly string[] IssueKeyCorpus =
		["", "123", " 456 ", "12.0", "abc", "0", "-5", "1e3", "+7", "nan", "99999999999", "2147483647", "007", "1 2", "12."];

	private static readonly int?[] IssueCorpus = [null, 1, 12, 123, 999999];

	private static readonly string[] NoteFragments =
	[
		"Scraped", " metadata from ComicVine [", "CVDB", "12", "123", "0", "]", ".", " on ", "2013.05.14", " ", "21:43:06",
		"9:43:06 PM", "\n", "x", "ComicVine[", "CVDBSKIP", "scraped", ",", "\r\n",
	];

	[Test]
	public async Task Find_matches_extract_issue_ref()
	{
		List<string> differences = [];
		int compared = 0;
		foreach (string[] tags in TagCorpus)
		{
			foreach (string notes in NoteCorpus)
			{
				foreach (string issueKey in IssueKeyCorpus)
				{
					var python = (object[])Oracle.Call("cvs_book_oracle", "find_marker", tags, notes, issueKey)!;
					string expected = string.Join(" ", python);
					if (IsDeviation(expected, tags, notes, issueKey))
					{
						continue;
					}
					compared++;
					string actual = Describe(KeyMarkers.Find(tags, notes, issueKey));
					if (actual != expected)
					{
						differences.Add($"tags=[{string.Join("|", tags)}] notes={Show(notes)} key={Show(issueKey)}: python {expected}, C# {actual}");
					}
				}
			}
		}

		await Assert.That(differences).IsEmpty();
		await Assert.That(compared).IsGreaterThan(5000);
	}

	[Test]
	public async Task Custom_values_that_are_numbers_but_not_whole_make_Python_raise_and_the_port_reads_what_it_can()
	{
		foreach ((string key, string port) in new[] { ("12.0", "issue 12"), ("12.", "issue 12"), ("1e3", "none"), ("nan", "none") })
		{
			var python = (object[])Oracle.Call("cvs_book_oracle", "find_marker", Array.Empty<string>(), "", key)!;

			await Assert.That(string.Join(" ", python)).StartsWith("error");
			await Assert.That(Describe(KeyMarkers.Find([], "", key))).IsEqualTo(port);
		}
		foreach (string key in new[] { "abc", "1 2" })
		{
			var python = (object[])Oracle.Call("cvs_book_oracle", "find_marker", Array.Empty<string>(), "", key)!;

			await Assert.That(string.Join(" ", python)).IsEqualTo("none");
		}
	}

	[Test]
	public async Task WithKeyTag_matches_add_key_to_tags()
	{
		List<string> differences = [];
		foreach (string[] tags in TagCorpus)
		{
			foreach (int? issue in IssueCorpus)
			{
				object? python = Oracle.Call("cvs_book_oracle", "add_key_to_tags", tags, issue?.ToString(CultureInfo.InvariantCulture));
				string expected = python is object[] items ? Normalise(items.Cast<string>()) : (string)python!;
				string actual = Normalise(KeyMarkers.WithKeyTag(tags, Id(issue)));
				if (actual != expected)
				{
					differences.Add($"tags=[{string.Join("|", tags)}] issue={issue}: python {expected}, C# {actual}");
				}
			}
		}

		await Assert.That(differences).IsEmpty();
	}

	[Test]
	public async Task WithKeyNote_matches_add_key_to_notes_on_the_corpus()
	{
		await AssertNotesMatch(NoteCorpus, minimumCompared: NoteCorpus.Length * IssueCorpus.Length * 2);
	}

	[Test]
	public async Task WithKeyNote_matches_add_key_to_notes_on_generated_notes()
	{
		var random = new Random(20241109);
		string[] notes = Enumerable.Range(0, 1500)
			.Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 12)).Select(_ => NoteFragments[random.Next(NoteFragments.Length)])))
			.ToArray();

		await AssertNotesMatch(notes, minimumCompared: notes.Length * IssueCorpus.Length * 2);
	}

	/// <summary>
	/// deviations.md #10 assumed %X follows the user's locale. Under IronPython 2.7.4 it does not: %X is HH:mm:ss in every
	/// culture, by thread culture or by LC_TIME, and only the calendar year changes with LC_TIME. So Python's own pattern
	/// finds every note it wrote, and the port keeps that pattern.
	/// </summary>
	[Test]
	public async Task Notes_Python_wrote_under_every_culture_are_replaced_whole()
	{
		int[][] times = [[2013, 5, 14, 21, 43, 6, 1, 134, -1], [2013, 5, 14, 9, 3, 6, 1, 134, -1]];
		var defaultStamps = new SortedSet<string>(StringComparer.Ordinal);
		var localeStamps = new SortedSet<string>(StringComparer.Ordinal);
		List<string> refused = [];
		CultureInfo original = Thread.CurrentThread.CurrentCulture;
		try
		{
			foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
			{
				Thread.CurrentThread.CurrentCulture = culture;
				foreach (int[] time in times)
				{
					defaultStamps.Add((string)Oracle.Call("cvs_book_oracle", "strftime", "%Y.%m.%d %X", time)!);
					var stamp = (string)Oracle.Call("cvs_book_oracle", "strftime_in", culture.Name, "%Y.%m.%d %X", time)!;
					if (stamp.StartsWith("error ", StringComparison.Ordinal))
					{
						refused.Add(culture.Name);
					}
					else
					{
						localeStamps.Add(stamp);
					}
				}
			}
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = original;
		}

		List<string> missed = [];
		foreach (string stamp in defaultStamps.Concat(localeStamps))
		{
			string old = $"My note\n\nScraped metadata from ComicVine [CVDB7] on {stamp}.";
			if (KeyMarkers.WithKeyNote(old, new IssueId(42), Now) != "My note\n\nScraped metadata from ComicVine [CVDB42] on 2024.01.31 13:45:10.")
			{
				missed.Add(Show(stamp));
			}
		}

		await Assert.That(missed).IsEmpty();
		await Assert.That(refused.Count).IsLessThan(10);
		await Assert.That(defaultStamps).IsEquivalentTo(["2013.05.14 09:03:06", "2013.05.14 21:43:06"]);
		await Assert.That(localeStamps.Select(stamp => stamp.Substring(10))).IsEquivalentTo(
			localeStamps.Select(stamp => stamp.EndsWith("21:43:06", StringComparison.Ordinal) ? " 21:43:06" : " 09:03:06"));
	}

	private static async Task AssertNotesMatch(IEnumerable<string> notes, int minimumCompared)
	{
		List<string> differences = [];
		List<string> errors = [];
		int compared = 0;
		foreach (string note in notes)
		{
			foreach (int? issue in IssueCorpus)
			{
				foreach (DateTime? at in new DateTime?[] { null, Now })
				{
					var python = (string)Oracle.Call("cvs_book_oracle", "add_key_to_notes", note, issue?.ToString(CultureInfo.InvariantCulture), Stamp(at))!;
					if (python.StartsWith("error ", StringComparison.Ordinal))
					{
						errors.Add($"notes={Show(note)}: {python}");
						continue;
					}
					compared++;
					string actual = KeyMarkers.WithKeyNote(note, Id(issue), at);
					if (actual != python.Trim())
					{
						differences.Add($"notes={Show(note)} issue={issue} at={at}: python {Show(python)}, C# {Show(actual)}");
					}
				}
			}
		}

		await Assert.That(differences.Take(20)).IsEmpty();
		await Assert.That(errors.Take(20)).IsEmpty();
		await Assert.That(compared).IsGreaterThanOrEqualTo(minimumCompared);
	}

	// deviations.md #14: a key tag, note or custom value of 0 makes IssueRef raise, and a key beyond int.MaxValue is no Comic Vine id.
	// deviations.md #9: int() raises on a custom value that is no whole number, such as "12.0", "12.", "1e3" or "nan" (is_number accepts them as floats).
	// Every other error is a real difference and stays in the comparison.
	private static bool IsDeviation(string python, string[] tags, string notes, string issueKey) =>
		python.StartsWith("error", StringComparison.Ordinal)
			? tags.Append(notes).Any(text => ZeroKey.IsMatch(text)) || !WholeNumber.IsMatch(issueKey) && FloatNotInt.IsMatch(issueKey) || ZeroNumber.IsMatch(issueKey)
			: python.StartsWith("issue ", StringComparison.Ordinal) && !int.TryParse(python.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out _);

	private static readonly Regex ZeroKey = new(@"CVDB0+(?![0-9])", RegexOptions.IgnoreCase);
	private static readonly Regex WholeNumber = new(@"^\s*[+-]?[0-9]+\s*$");
	private static readonly Regex ZeroNumber = new(@"^[\s]*[+-]?0+[\s]*$");
	private static readonly Regex FloatNotInt = new(@"^\s*[+-]?([0-9]+\.?[0-9]*([eE][+-]?[0-9]+)?|\.[0-9]+([eE][+-]?[0-9]+)?|nan|inf|infinity)\s*$", RegexOptions.IgnoreCase);

	private static string Describe(StoredMarker marker) => marker switch
	{
		StoredMarker.SkipForever => "skip",
		StoredMarker.Issue issue => "issue " + issue.Id,
		_ => "none",
	};

	private static string Normalise(IEnumerable<string> tags) => string.Join("|", tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0));

	private static IssueId? Id(int? issue) => issue is int value ? new IssueId(value) : null;

	private static object[]? Stamp(DateTime? at) => at is DateTime t ? [t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second] : null;

	private static string Show(string text) => "'" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "'";
}
