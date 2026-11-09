using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.ComicVine;

/// <summary>
/// The client against the live API: five requests. Explicit, so it runs only when selected
/// (--treenode-filter "/*/*/LiveSmokeTests/*") with CVS_API_KEY_FILE naming the file that holds the key.
/// </summary>
public sealed class LiveSmokeTests
{
	private const string UserAgent = "ComicApiPlugin.NET/0.1 (https://github.com/cbanack/comic-vine-scraper/)";

	[Test]
	[Explicit]
	public async Task The_client_searches_finds_and_reads_an_issue_and_reports_a_bad_key()
	{
		var settings = new SearchSettings([], [], 0, 9999, 9999, 100);
		using var http = new ComicVineHttp(new HttpClientHandler(), LiveApiKey.Read(), UserAgent, new SystemClock());
		var catalog = new ComicVineCatalog(http, settings);

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);
		IssueRef? issueRef = await catalog.FindIssueAsync(new SeriesId(796), "497", CancellationToken.None);
		Issue issue = await catalog.GetIssueAsync(issueRef!.Id, includeRating: true, CancellationToken.None);
		using var badKey = new ComicVineHttp(new HttpClientHandler(), "not-a-valid-key", UserAgent, new SystemClock());
		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(
			() => new ComicVineCatalog(badKey, settings).GetIssueAsync(new IssueId(37566), false, CancellationToken.None)))!;

		await Assert.That(found.Count).IsEqualTo(100);
		await Assert.That(found.Any(s => s.Id == new SeriesId(796) && s.Name == "Batman" && s.StartYear == 1940)).IsTrue();
		await Assert.That(issueRef.Id).IsEqualTo(new IssueId(37566));
		await Assert.That(issue.SeriesName).IsEqualTo("Batman");
		await Assert.That(issue.Publisher).IsEqualTo("DC Comics");
		await Assert.That(issue.Writers).Contains("Doug Moench");
		await Assert.That(issue.CoverUrls.Count).IsGreaterThan(1);
		await Assert.That(error.Error).IsEqualTo(ComicVineError.InvalidApiKey);
		await Assert.That(error.Url).DoesNotContain("not-a-valid-key");
	}

	private sealed class SystemClock : IClock
	{
		public DateTime Now => DateTime.Now;

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
	}
}
