using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Plugin;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>BookOrder.Sort against ScrapeEngine.__sort_books on the same ComicRack books.</summary>
public sealed class BookOrderOracleTests
{
	private static readonly string[] SeriesNames = ["", "Batman", "batman", "Bätman", "Ärger", "Zorro", "X-Men", "Apple", "Æon", "Spider-Man", "Ölfass"];
	private static readonly string[] Numbers =
	[
		"", "", "1", "1", "2", "9", "10", "11", "99", "100", "101", "999", "1000", "5000", "01", "007", "2b", "2B", "a3", "h", "3h", "1.5", ".5",
		"½", "5½", "-1", "-20", "nan", "inf", "1e3", " 3 ", "12.0", "abc", "1/2", "Ⅻ", "٣", "١٢", "x9",
	];
	private static readonly string[] Paths =
	[
		"", @"C:\Comics\Batman 012 (1990).cbz", @"C:\Comics\Batman 012 (1990).cbz", @"D:\Lib\Batman\Batman 3.cbz", @"C:\Comics\Zorro 100.cbr",
		@"C:\Comics\X-Men 1000.cbz", @"C:\c\untitled.cbz", @"C:\c\Ärger 5.cbz",
	];
	private static readonly string[] Tags = ["", "", "", "CVDBSKIP", "CVDB123", "x, CVDB77"];
	// No "12.0": int("12.0") raises in __extract_issue_ref, so Python cannot sort such a book at all (deviations.md #9).
	private static readonly string[] IssueKeys = ["", "", "", "456", " 7 "];
	private static readonly string[] Cultures = ["en-US", "sv-SE", "de-DE", "da-DK", ""];

	[Test]
	public async Task Sort_matches_sort_books()
	{
		var random = new Random(53);
		List<string> differences = [];
		int compared = 0, skipped = 0, fastCases = 0, books = 0;
		CultureInfo saved = Thread.CurrentThread.CurrentCulture;
		try
		{
			for (int i = 0; i < 600; i++)
			{
				ComicBook[] crbooks = [.. Enumerable.Range(0, random.Next(2, 13)).Select(_ => RandomBook(random))];
				bool fastRescrape = random.Next(3) > 0;
				bool ignoreFolders = random.Next(2) == 0;
				Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(Cultures[random.Next(Cultures.Length)]);

				BookFacts[] facts = [.. crbooks.Select((book, index) => BookFacts.From(index, new ComicRackBook(book, null!).Read(), BookCorpus.ParseFileName, ignoreFolders))];
				// A nameless book's key holds Python's id(self) where the port holds its position, so two of them sort apart.
				if (facts.Count(f => f.UniqueSeriesKey.StartsWith("uniqueid-", StringComparison.Ordinal)) > 1)
				{
					skipped++;
					continue;
				}
				var python = (object[])Oracle.Call("cvs_engine_oracle", "sort_books", crbooks.Select(book => new ComicBook(book)).ToArray(), fastRescrape, ignoreFolders)!;
				string expected = string.Join(",", python);
				string actual = string.Join(",", BookOrder.Sort(facts, fastRescrape).Select(f => f.Index));
				compared++;
				books += crbooks.Length;
				fastCases += fastRescrape && facts.Any(f => f.Marker is not StoredMarker.None) ? 1 : 0;
				if (actual != expected)
				{
					differences.Add($"{CultureInfo.CurrentCulture.Name} fast={fastRescrape}: python {expected}, C# {actual}; "
						+ string.Join(" / ", facts.Select(f => $"{f.Index}:{f.UniqueSeriesKey}#{f.Effective.Number}")));
				}
			}
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = saved;
		}

		await Assert.That(differences.Take(10)).IsEmpty();
		await Assert.That(compared).IsGreaterThan(450);
		await Assert.That(fastCases).IsGreaterThan(200);
		await Assert.That(compared + skipped).IsEqualTo(600);
		await Assert.That(books).IsGreaterThan(3000);
	}

	private static ComicBook RandomBook(Random random)
	{
		var book = new ComicBook
		{
			Series = BookCorpus.Pick(random, SeriesNames),
			Number = BookCorpus.Pick(random, Numbers),
			FilePath = BookCorpus.Pick(random, Paths),
			Tags = BookCorpus.Pick(random, Tags),
			Volume = BookCorpus.Pick(random, new[] { -1, -1, 1990, 2011 }),
			Format = BookCorpus.Pick(random, new[] { "", "", "Annual" }),
		};
		book.SetCustomValue("comicvine_issue", BookCorpus.Pick(random, IssueKeys));
		return book;
	}
}
