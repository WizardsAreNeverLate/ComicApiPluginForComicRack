using ComicApiPlugin.Model;

namespace ComicApiPlugin.Books;

/// <summary>
/// A published date as a ComicRack book holds it. Unlike PartialDate, each part is kept or dropped on its own, as in
/// Python's BookData, so a month without a year survives a read and counts as not blank.
/// </summary>
public readonly record struct BookDate
{
	private BookDate(int? year, int? month, int? day)
	{
		Year = year;
		Month = month;
		Day = day;
	}

	public int? Year { get; }
	public int? Month { get; }
	public int? Day { get; }

	public static BookDate None => default;

	/// <summary>BookData's setters: year 1..9998, month 1..12, day 0 or more. Anything else is unknown.</summary>
	public static BookDate Of(int? year, int? month, int? day) => new(
		year is > 0 and < 9999 ? year : null,
		month is >= 1 and <= 12 ? month : null,
		day is >= 0 ? day : null);

	/// <summary>Blank only when all three parts are unknown, as Python compares with (-1, -1, -1).</summary>
	public bool IsBlank => Year is null && Month is null && Day is null;

	public static implicit operator BookDate(PartialDate date) => Of(date.Year, date.Month, date.Day);
}
