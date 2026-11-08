using System;
using System.Collections.Generic;
using System.Linq;
using ComicVineScraper.Books;
using ComicVineScraper.Text;

namespace ComicVineScraper.Engine;

/// <summary>
/// scrapeengine.__sort_books: the order a run visits its books. With fast rescrape on, books that are skipped or already
/// know their issue come first, so the user's questions come at the end (Python issue 161). Each group sorts by the
/// unique series key, then by the issue number padded to four digits, both compared in the current culture as
/// IronPython's str.CompareTo does. Equal books keep their input order, as Python's stable sort keeps them.
/// </summary>
public static class BookOrder
{
	private static readonly char[] PadStrip = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];

	public static IReadOnlyList<BookFacts> Sort(IEnumerable<BookFacts> books, bool fastRescrape)
	{
		StringComparer culture = StringComparer.CurrentCulture;
		return
		[
			.. books
				.OrderBy(book => fastRescrape && book.Marker is not StoredMarker.None ? 0 : 1)
				.ThenBy(book => book.UniqueSeriesKey, culture)
				.ThenBy(book => Pad(book.Effective.Number), culture),
		];
	}

	// float(num.lower().strip('abcdefgh')): "2b" pads to "0002b", and a number Python cannot read is left as it is.
	private static string Pad(string number)
	{
		if (!PyStr.TryParseFloat(PyStr.Lower(number).Trim(PadStrip), out double value))
		{
			return number;
		}
		return value switch
		{
			< 10 => "000" + number,
			< 100 => "00" + number,
			< 1000 => "0" + number,
			_ => number,
		};
	}
}
