using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Model;

public sealed class IssueIdTests
{
	[Test]
	[Arguments("12", 12)]
	[Arguments("12.0", 12)]
	[Arguments("12.000", 12)]
	[Arguments(" 12 ", 12)]
	[Arguments("+12", 12)]
	[Arguments("2147483647", int.MaxValue)]
	public async Task TryParseStored_accepts_whole_numbers(string text, int expected)
	{
		await Assert.That(IssueId.TryParseStored(text)).IsEqualTo(new IssueId(expected));
	}

	[Test]
	[Arguments("12.5")]
	[Arguments("12.0.0")]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments(null)]
	[Arguments("-3")]
	[Arguments("-3.0")]
	[Arguments("abc")]
	[Arguments("1e3")]
	[Arguments("1,200")]
	[Arguments("2147483648")]
	public async Task TryParseStored_rejects_everything_else(string? text)
	{
		await Assert.That(IssueId.TryParseStored(text)).IsNull();
	}

	[Test]
	public async Task TryParseStored_ignores_the_current_culture()
	{
		CultureInfo previous = Thread.CurrentThread.CurrentCulture;
		Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
		try
		{
			await Assert.That(IssueId.TryParseStored("12.0")).IsEqualTo(new IssueId(12));
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = previous;
		}
	}
}
