using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>UniqueSeriesKey.Compute against ComicBook.__unique_series_s.</summary>
public sealed class UniqueSeriesKeyOracleTests
{
	private const int Index = 7;

	private static readonly string[] Series =
	[
		"", "Batman", "X-Men", "Spider-Man: Blue", "!!!", "Ünïcödé", "İstanbul", "ẞig", "Ⅻ roman", "a_b", "日本語", "😀 emoji",
		"Ǆungla", "Two  Spaces", "ÆON flux",
	];

	private static readonly string[] Formats = ["", "Annual", "Giant-Size", "TPB", "İ"];

	private static readonly int?[] Volumes = [null, 0, 1, 1990, 2147483647];

	private static readonly string[] Paths =
	[
		"", @"C:\Comics\Batman 012.cbz", @"D:\Ünï\X.cbr", @"\\server\share\a.cbz", "relative.cbz", @"C:\a\b\c\d.cbz",
		@"C:\x\", @"C:\Comics\日本\a.cbz",
	];

	[Test]
	public async Task Compute_matches_unique_series_s()
	{
		List<string> differences = [];
		int compared = 0;
		foreach (string series in Series)
		{
			foreach (string format in Formats)
			{
				foreach (int? volume in Volumes)
				{
					foreach (string path in Paths)
					{
						foreach (bool ignoreFolders in new[] { false, true })
						{
							var python = (object[])Oracle.Call("cvs_book_oracle", "unique_series", series, format, volume ?? -1, path, ignoreFolders)!;
							// Python falls back to id(self) for a nameless book; the port uses the book's index in the run.
							string expected = ((string)python[0]).Replace("uniqueid-" + python[1], "uniqueid-" + Index);
							string actual = UniqueSeriesKey.Compute(series, format, volume, path, ignoreFolders, Index);
							compared++;
							if (actual != expected)
							{
								differences.Add($"{series}|{format}|{volume}|{path}|{ignoreFolders}: python {expected}, C# {actual}");
							}
						}
					}
				}
			}
		}

		await Assert.That(differences.Count > 20 ? differences.GetRange(0, 20) : differences).IsEmpty();
		await Assert.That(compared).IsEqualTo(Series.Length * Formats.Length * Volumes.Length * Paths.Length * 2);
	}

	[Test]
	[Arguments("tr-TR")]
	[Arguments("en-US")]
	[Arguments("lt-LT")]
	public async Task Compute_lowercases_the_same_whatever_the_current_culture(string cultureName)
	{
		CultureInfo original = Thread.CurrentThread.CurrentCulture;
		List<string> differences = [];
		try
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
			foreach (string series in new[] { "IRON MAN", "İstanbul", "ÌÍĨ", "Batman" })
			{
				var python = (object[])Oracle.Call("cvs_book_oracle", "unique_series", series, "Annual", 1990, "", true)!;
				string actual = UniqueSeriesKey.Compute(series, "Annual", 1990, "", true, Index);
				if (actual != (string)python[0])
				{
					differences.Add($"{series}: python {python[0]}, C# {actual}");
				}
			}
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = original;
		}

		await Assert.That(differences).IsEmpty();
	}
}
