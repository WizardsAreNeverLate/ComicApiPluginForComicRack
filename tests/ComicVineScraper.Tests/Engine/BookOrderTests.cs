using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using TUnit.Assertions.Enums;

namespace ComicVineScraper.Tests.Engine;

public sealed class BookOrderTests
{
	[Test]
	public async Task Skipped_and_known_issue_books_come_first_when_fast_rescrape_is_on()
	{
		BookFacts[] books = [Book(0, "Avengers", "1"), Book(1, "Zorro", "1", tags: ["CVDBSKIP"]), Book(2, "Batman", "2"), Book(3, "X-Men", "4", tags: ["CVDB123"])];

		await Assert.That(Order(books, fastRescrape: true)).IsEquivalentTo(new[] { 3, 1, 0, 2 }, CollectionOrdering.Matching);
		await Assert.That(Order(books, fastRescrape: false)).IsEquivalentTo(new[] { 0, 2, 3, 1 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Numbers_sort_padded_to_four_digits_within_a_series()
	{
		BookFacts[] books = [Book(0, "Batman", "100"), Book(1, "Batman", "10"), Book(2, "Batman", "9"), Book(3, "Batman", "1000"), Book(4, "Batman", "2b"), Book(5, "Batman", "")];

		await Assert.That(Order(books, fastRescrape: true)).IsEquivalentTo(new[] { 5, 4, 2, 1, 0, 3 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Equal_keys_keep_their_input_order_and_a_leading_zero_pads_to_five_digits()
	{
		BookFacts[] books = [Book(0, "Batman", "1"), Book(1, "Batman", "1"), Book(2, "Batman", "01")];

		await Assert.That(Order(books, fastRescrape: true)).IsEquivalentTo(new[] { 2, 0, 1 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Series_keys_compare_in_the_current_culture()
	{
		BookFacts[] books = [Book(0, "Ärger", "1"), Book(1, "Zorro", "1"), Book(2, "Apple", "1")];
		CultureInfo saved = CultureInfo.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
			int[] english = Order(books, fastRescrape: true);
			Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
			int[] swedish = Order(books, fastRescrape: true);

			await Assert.That(english).IsEquivalentTo(new[] { 2, 0, 1 }, CollectionOrdering.Matching);
			await Assert.That(swedish).IsEquivalentTo(new[] { 2, 1, 0 }, CollectionOrdering.Matching);
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = saved;
		}
	}

	private static int[] Order(IEnumerable<BookFacts> books, bool fastRescrape) => [.. BookOrder.Sort(books, fastRescrape).Select(b => b.Index)];

	private static BookFacts Book(int index, string series, string number, IReadOnlyList<string>? tags = null) =>
		BookFacts.From(index, new BookSnapshot(new BookValues { Series = series, Number = number, Tags = tags ?? [] }, "", 0, false),
			_ => ("", "", ""), ignoreFolders: false);
}
