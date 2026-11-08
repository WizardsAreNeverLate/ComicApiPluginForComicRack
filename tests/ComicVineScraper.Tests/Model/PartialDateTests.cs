using System.Threading.Tasks;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Model;

public sealed class PartialDateTests
{
	[Test]
	public async Task A_full_date_is_kept()
	{
		PartialDate date = PartialDate.Of(1990, 5, 3);

		await Assert.That(date.Year).IsEqualTo(1990);
		await Assert.That(date.Month).IsEqualTo(5);
		await Assert.That(date.Day).IsEqualTo(3);
		await Assert.That(date.IsComplete).IsTrue();
		await Assert.That(date.IsBlank).IsFalse();
	}

	[Test]
	[Arguments(null, 5, 3)]
	[Arguments(null, 5, null)]
	[Arguments(null, null, 3)]
	[Arguments(null, null, null)]
	[Arguments(-1, 5, 3)]
	public async Task Without_a_year_nothing_remains(int? year, int? month, int? day)
	{
		PartialDate date = PartialDate.Of(year, month, day);

		await Assert.That(date).IsEqualTo(PartialDate.None);
		await Assert.That(date.IsBlank).IsTrue();
	}

	[Test]
	[Arguments(1990, null, 3)]
	[Arguments(1990, 13, 3)]
	[Arguments(1990, 0, 3)]
	public async Task Without_a_month_the_day_is_dropped(int year, int? month, int? day)
	{
		PartialDate date = PartialDate.Of(year, month, day);

		await Assert.That(date.Year).IsEqualTo(year);
		await Assert.That(date.Month).IsNull();
		await Assert.That(date.Day).IsNull();
		await Assert.That(date.IsComplete).IsFalse();
	}

	[Test]
	[Arguments(0)]
	[Arguments(32)]
	[Arguments(-1)]
	public async Task A_day_outside_1_to_31_becomes_unknown(int day)
	{
		PartialDate date = PartialDate.Of(1990, 5, day);

		await Assert.That(date.Year).IsEqualTo(1990);
		await Assert.That(date.Month).IsEqualTo(5);
		await Assert.That(date.Day).IsNull();
	}

	[Test]
	public async Task Year_zero_is_a_valid_year_and_the_default_value_is_blank()
	{
		await Assert.That(PartialDate.Of(0, null, null).Year).IsEqualTo(0);
		await Assert.That(default(PartialDate)).IsEqualTo(PartialDate.None);
		await Assert.That(PartialDate.None.IsBlank).IsTrue();
	}
}
