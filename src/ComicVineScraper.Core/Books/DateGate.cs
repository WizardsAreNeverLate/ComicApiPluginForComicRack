using System;

namespace ComicVineScraper.Books;

/// <summary>
/// The date rules of PluginBookData.update: which date bits a patch may write, given its values. MergeRules applies
/// it when it plans, and ComicRackBook.Apply checks it before it writes.
/// </summary>
public static class DateGate
{
	/// <summary>
	/// Drops Released unless the date is complete and real, PublishedMonth unless the year is known, and PublishedDay
	/// unless the year and month are known. A missing part never clears what the book already holds.
	/// </summary>
	public static BookFields Restrict(BookFields fields, BookValues values)
	{
		if (!CanWriteReleased(values))
		{
			fields &= ~BookFields.Released;
		}
		BookDate published = values.Published;
		if (published.Year is null)
		{
			fields &= ~BookFields.Published;
		}
		else if (published.Month is null)
		{
			fields &= ~(BookFields.PublishedMonth | BookFields.PublishedDay);
		}
		else if (published.Day is null)
		{
			fields &= ~BookFields.PublishedDay;
		}
		return fields;
	}

	// Python's BookData blanks a year outside 1..9998, and its DateTime constructor throws on a day the month lacks.
	private static bool CanWriteReleased(BookValues values) =>
		values.Released is { Year: > 0 and < 9999 and int year, Month: int month, Day: int day }
		&& day <= DateTime.DaysInMonth(year, month);
}
