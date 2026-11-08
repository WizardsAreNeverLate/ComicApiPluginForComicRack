using System;
using System.Globalization;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>Seeded random ComicRack books for the book oracle tests, with the awkward values each field can hold.</summary>
internal static class BookCorpus
{
	private static readonly string[] Texts = ["", " ", "Batman", "  The Joker  ", "a,b", "Ünïcode", "\tTab", "line\nbreak", "X"];
	private static readonly string[] SeriesNames = ["", " ", "Batman", "X-Men", "Spider-Man: Blue", "!!!", " Detective Comics ", "Ms. Marvel"];
	private static readonly string[] Numbers = ["", "1", "12", " 7 ", "1/2", "½", "0", "-1", "5a"];
	private static readonly string[] Lists = ["", "a", "a, b", "a,,b", " a , b ,", "Joe,Bob", "x , ", "Grant Morrison, Frank Quitely"];
	private static readonly string[] Formats = ["", "Annual", " Giant "];
	private static readonly string[] Webs = ["", "https://comicvine.gamespot.com/x/4000-1/", " http://y "];
	private static readonly string[] IssueKeys = ["", "123", " 456 ", "12.0", "abc", "0", "-5", "+7", "99999999999"];
	private static readonly string[] SeriesKeys = ["", "796", " 12 ", "12.0", "0", "-3", "abc"];
	private static readonly int[] Years = [-1, -1, 0, 1990, 2010, 9999, 10000, -5];
	private static readonly int[] Months = [-1, -1, 0, 5, 12, 13];
	private static readonly int[] Days = [-1, -1, 0, 3, 31, 45];
	private static readonly int[] Volumes = [-1, -1, 0, 1990, 5];
	private static readonly float[] Ratings = [0f, 0f, 3.5f, 5f, 2.25f];
	private static readonly DateTime[] Released = [DateTime.MinValue, DateTime.MinValue, new(2010, 5, 3), new(1990, 12, 31), new(9999, 12, 31)];

	private static readonly string[] Paths =
	[
		"", "", @"C:\Comics\Batman 012 (1990).cbz", @"C:\Comics\X-Men v2 #21 (1993).cbr", @"D:\Lib\Spawn\Spawn 005.cbz",
		@"C:\Comics\Amazing Spider-Man 001 (1963).cbz", @"C:\Comics\2000AD 1234 (2001).cbz", @"C:\c\Saga 01 of 06 (2012).cbz",
		@"C:\c\untitled.cbz", @"C:\c\Batman Annual 03.cbz", @"\\nas\c\Ms. Marvel #1 (2014).cbr", @"C:\c\(1990).cbz",
	];

	public static ComicBook RandomBook(Random random)
	{
		var book = new ComicBook
		{
			Series = Pick(random, SeriesNames),
			Number = Pick(random, Numbers),
			Title = Pick(random, Texts),
			Summary = Pick(random, Texts),
			Publisher = Pick(random, ["", "Marvel", "DC Comics", " Vertigo ", "Image"]),
			Imprint = Pick(random, ["", "Vertigo", "MAX", "Marvel", " Icon "]),
			AlternateSeries = Pick(random, Lists),
			Characters = Pick(random, Lists),
			Teams = Pick(random, Lists),
			Locations = Pick(random, Lists),
			Writer = Pick(random, Lists),
			Penciller = Pick(random, Lists),
			Inker = Pick(random, Lists),
			Colorist = Pick(random, Lists),
			Letterer = Pick(random, Lists),
			CoverArtist = Pick(random, Lists),
			Editor = Pick(random, Lists),
			Tags = string.Join(", ", Pick(random, KeyMarkerOracleTests.TagCorpus)),
			Notes = Pick(random, KeyMarkerOracleTests.NoteCorpus),
			Web = Pick(random, Webs),
			Volume = Pick(random, Volumes),
			Format = Pick(random, Formats),
			Year = Pick(random, Years),
			Month = Pick(random, Months),
			Day = Pick(random, Days),
			ReleasedTime = Pick(random, Released),
			CommunityRating = Pick(random, Ratings),
			FilePath = Pick(random, Paths),
		};
		book.SetCustomValue("comicvine_issue", Pick(random, IssueKeys));
		book.SetCustomValue("comicvine_volume", Pick(random, SeriesKeys));
		return book;
	}

	/// <summary>The original fnameparser.extract, standing in for workstream 3a's FileNameParser.</summary>
	public static (string Series, string Number, string Year) ParseFileName(string fileName)
	{
		var parsed = (object[])Oracle.Call("fnameparser", "extract", fileName)!;
		return ((string)parsed[0], (string)parsed[1], parsed.Length > 2 ? (string)parsed[2] : "");
	}

	public static string Describe(ComicBook book) => string.Join("¦",
		book.Series, book.Number, book.FilePath, book.Year.ToString(CultureInfo.InvariantCulture), book.Month.ToString(CultureInfo.InvariantCulture),
		book.Day.ToString(CultureInfo.InvariantCulture), book.Volume.ToString(CultureInfo.InvariantCulture), book.Format, book.Tags,
		book.Notes.Replace("\n", "\\n"), book.CustomValuesStore);

	public static T Pick<T>(Random random, T[] items) => items[random.Next(items.Length)];
}
