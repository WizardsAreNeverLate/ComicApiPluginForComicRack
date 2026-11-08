using System;
using System.Globalization;
using System.IO;
using ComicVineScraper.Model;

namespace ComicVineScraper.Books;

/// <summary>The engine's per-book input: the snapshot plus everything derived from it.</summary>
public sealed record BookFacts(
	int Index,                  // position in the run; the nameless-book fallback for the series key
	BookSnapshot Snapshot,
	BookValues Effective,       // Series, Number, Published.Year filled from the file name when blank
	StoredMarker Marker,        // checked in Python's order: tags then notes for SKIP, then CVDB, then custom value
	SeriesId? StoredSeries,     // custom value comicvine_volume
	string UniqueSeriesKey)     // groups books of one series within a run
{
	/// <param name="parseFileName">File name to (series, number, year), as FileNameParser.Extract (with the alternate regex) gives them.</param>
	public static BookFacts From(int index, BookSnapshot snapshot, Func<string, (string Series, string Number, string Year)> parseFileName, bool ignoreFolders)
	{
		BookValues values = WithNameFromFile(snapshot, parseFileName);
		return new BookFacts(
			index,
			snapshot,
			values,
			KeyMarkers.Find(values.Tags, values.Notes, values.IssueKey),
			SeriesId.TryParseStored(values.SeriesKey),
			Books.UniqueSeriesKey.Compute(values.Series, values.Format, values.Volume, snapshot.FilePath, ignoreFolders, index));
	}

	// comicbook.py's __parse_extra_details_from_path: only blank fields are filled, and only for a book with a file.
	private static BookValues WithNameFromFile(BookSnapshot snapshot, Func<string, (string Series, string Number, string Year)> parseFileName)
	{
		BookValues values = snapshot.Values;
		bool noSeries = values.Series.Length == 0;
		bool noNumber = values.Number.Length == 0;
		bool noYear = values.Published.Year is null;
		if (!(noSeries || noNumber || noYear) || snapshot.FilePath.Length == 0)
		{
			return values;
		}
		(string series, string number, string year) = parseFileName(Path.GetFileName(snapshot.FilePath));
		BookDate published = values.Published;
		return values with
		{
			Series = noSeries ? series.Trim() : values.Series,
			Number = noNumber ? number.Trim() : values.Number,
			Published = noYear ? BookDate.Of(ParseYear(year), published.Month, published.Day) : published,
		};
	}

	private static int? ParseYear(string year) =>
		int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
}
