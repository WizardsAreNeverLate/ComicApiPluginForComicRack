using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Plugin;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>ComicRackBook.Read then BookFacts.From, against PluginBookData and ComicBook on the same ComicRack book.</summary>
public sealed class BookFactsOracleTests
{
	private const int Index = 5;

	[Test]
	public async Task From_matches_what_ComicBook_derives()
	{
		var random = new Random(31);
		List<string> differences = [];
		int compared = 0;
		for (int i = 0; i < 3000; i++)
		{
			ComicBook book = BookCorpus.RandomBook(random);
			bool ignoreFolders = random.Next(2) == 0;

			var python = (object[])Oracle.Call("cvs_book_oracle", "facts", new ComicBook(book), ignoreFolders)!;
			string marker = string.Join(" ", (object[])python[3]);
			var seriesRef = (string)python[4];
			if (!IsComicVineId(marker, "issue ") || !IsComicVineId(seriesRef, ""))
			{
				// deviations.md #9 and #13: Python raises on 12.0 and 0, and keeps ids that are no Comic Vine id.
				continue;
			}
			compared++;
			string uniqueKey = ((string)python[5]).Replace("uniqueid-" + python[6], "uniqueid-" + Index);
			string expected = string.Join("|", python[0], python[1], python[2], marker, seriesRef, uniqueKey);

			BookFacts facts = BookFacts.From(Index, new ComicRackBook(book, null!).Read(), BookCorpus.ParseFileName, ignoreFolders);
			string actual = string.Join("|",
				facts.Effective.Series, facts.Effective.Number, facts.Effective.Published.Year ?? -1, Describe(facts.Marker),
				facts.StoredSeries?.ToString() ?? "", facts.UniqueSeriesKey);
			if (actual != expected)
			{
				differences.Add($"{BookCorpus.Describe(book)}: python {expected}, C# {actual}");
			}
		}

		await Assert.That(differences.Take(20)).IsEmpty();
		await Assert.That(compared).IsGreaterThan(1400);
	}

	// Python's marker is "skip", "none", or "issue <key>"; its series ref is "" or the key. Anything else raised.
	private static bool IsComicVineId(string python, string prefix)
	{
		if (python is "skip" or "none" or "")
		{
			return true;
		}
		return python.StartsWith(prefix, StringComparison.Ordinal)
			&& int.TryParse(python.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= 1;
	}

	private static string Describe(StoredMarker marker) => marker switch
	{
		StoredMarker.SkipForever => "skip",
		StoredMarker.Issue issue => "issue " + issue.Id,
		_ => "none",
	};
}
