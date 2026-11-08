using System;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Engine;

public sealed class CoverPreferencesTests
{
	private static readonly Uri First = new("https://example.test/a.jpg");
	private static readonly Uri Second = new("https://example.test/b.jpg");

	[Test]
	public async Task An_issue_with_no_preference_returns_null()
	{
		await Assert.That(new CoverPreferences().Preferred(new IssueId(1))).IsNull();
	}

	[Test]
	public async Task Prefer_is_remembered_per_issue_and_the_last_choice_wins()
	{
		var covers = new CoverPreferences();

		covers.Prefer(new IssueId(1), First);
		covers.Prefer(new IssueId(2), Second);
		covers.Prefer(new IssueId(1), Second);

		await Assert.That(covers.Preferred(new IssueId(1))).IsEqualTo(Second);
		await Assert.That(covers.Preferred(new IssueId(2))).IsEqualTo(Second);
		await Assert.That(covers.Preferred(new IssueId(3))).IsNull();
	}

	[Test]
	public async Task Concurrent_writers_lose_nothing()
	{
		var covers = new CoverPreferences();

		await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
		{
			for (int i = 0; i < 500; i++)
			{
				covers.Prefer(new IssueId(worker * 1000 + i), new Uri($"https://example.test/{worker}/{i}.jpg"));
			}
		})));

		for (int worker = 0; worker < 8; worker++)
		{
			for (int i = 0; i < 500; i++)
			{
				await Assert.That(covers.Preferred(new IssueId(worker * 1000 + i))).IsEqualTo(new Uri($"https://example.test/{worker}/{i}.jpg"));
			}
		}
	}
}
