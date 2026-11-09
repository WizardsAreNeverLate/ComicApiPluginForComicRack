using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.Books;

/// <summary>The date rules of test_bookdata.py and BookData's setters: each part is kept or dropped on its own.</summary>
public sealed class BookDateTests
{
	[Test]
	[Arguments(2012, 9, 15)]
	[Arguments(1, 1, 0)]
	[Arguments(9998, 12, 45)]
	public async Task Valid_parts_are_kept(int year, int month, int day)
	{
		BookDate date = BookDate.Of(year, month, day);

		await Assert.That(date.Year).IsEqualTo(year);
		await Assert.That(date.Month).IsEqualTo(month);
		await Assert.That(date.Day).IsEqualTo(day);
	}

	[Test]
	[Arguments(0)]
	[Arguments(-1)]
	[Arguments(9999)]
	[Arguments(10000)]
	public async Task A_year_outside_1_to_9998_is_unknown(int year)
	{
		await Assert.That(BookDate.Of(year, 5, 3).Year).IsNull();
	}

	[Test]
	[Arguments(0)]
	[Arguments(13)]
	[Arguments(-1)]
	public async Task A_month_outside_1_to_12_is_unknown(int month)
	{
		await Assert.That(BookDate.Of(2012, month, 3).Month).IsNull();
	}

	[Test]
	public async Task A_negative_day_is_unknown()
	{
		await Assert.That(BookDate.Of(2012, 5, -1).Day).IsNull();
	}

	[Test]
	public async Task A_month_without_a_year_is_kept_and_is_not_blank()
	{
		BookDate date = BookDate.Of(-1, 5, -1);

		await Assert.That(date.Year).IsNull();
		await Assert.That(date.Month).IsEqualTo(5);
		await Assert.That(date.IsBlank).IsFalse();
	}

	[Test]
	public async Task Only_three_unknown_parts_are_blank()
	{
		await Assert.That(BookDate.Of(null, null, null).IsBlank).IsTrue();
		await Assert.That(BookDate.None.IsBlank).IsTrue();
		await Assert.That(BookDate.Of(null, null, 0).IsBlank).IsFalse();
	}

	[Test]
	public async Task A_comic_vine_date_converts_with_the_book_rules()
	{
		BookDate full = PartialDate.Of(1990, 5, 3);
		BookDate yearZero = PartialDate.Of(0, 5, 3);

		await Assert.That(full).IsEqualTo(BookDate.Of(1990, 5, 3));
		await Assert.That(yearZero).IsEqualTo(BookDate.Of(null, 5, 3));
	}
}
