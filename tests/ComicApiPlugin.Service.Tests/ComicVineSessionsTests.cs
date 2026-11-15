using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Service.ComicVine;

namespace ComicApiPlugin.Service.Tests;

public sealed class ComicVineSessionsTests
{
	private static SearchSettingsDto Settings(int maxSearchResults = 100) => new()
	{
		IgnoredSearchTerms = [],
		IgnoredPublishers = [],
		IgnoreAfterYear = 9999,
		NeverIgnoreThreshold = 9999,
		MaxSearchResults = maxSearchResults,
	};

	[Test]
	public async Task A_run_keeps_its_catalog_while_it_sends_requests_and_loses_it_after_thirty_idle_minutes()
	{
		var time = new ManualTime();
		using var sessions = new ComicVineSessions(Connect, time);

		ComicVineCatalog first = sessions.Catalog("key", "run-1", Settings());
		time.Advance(TimeSpan.FromMinutes(29));
		ComicVineCatalog used = sessions.Catalog("key", "run-1", Settings());
		time.Advance(TimeSpan.FromMinutes(29));
		ComicVineCatalog usedAgain = sessions.Catalog("key", "run-1", Settings());
		time.Advance(TimeSpan.FromMinutes(31));
		ComicVineCatalog afterIdle = sessions.Catalog("key", "run-1", Settings());

		await Assert.That(used).IsSameReferenceAs(first);
		await Assert.That(usedAgain).IsSameReferenceAs(first);
		await Assert.That(afterIdle).IsNotSameReferenceAs(first);
	}

	[Test]
	public async Task A_catalog_belongs_to_one_key_one_run_and_one_set_of_settings_and_a_key_has_one_transport()
	{
		int transports = 0;
		using var sessions = new ComicVineSessions(key =>
		{
			transports++;
			return Connect(key);
		}, new ManualTime());

		ComicVineCatalog run = sessions.Catalog("key", "run-1", Settings());

		await Assert.That(sessions.Catalog("key", "run-1", Settings())).IsSameReferenceAs(run);
		await Assert.That(sessions.Catalog("key", "run-2", Settings())).IsNotSameReferenceAs(run);
		await Assert.That(sessions.Catalog("key", "run-1", Settings(maxSearchResults: 50))).IsNotSameReferenceAs(run);
		await Assert.That(transports).IsEqualTo(1);
		await Assert.That(sessions.Catalog("other-key", "run-1", Settings())).IsNotSameReferenceAs(run);
		await Assert.That(transports).IsEqualTo(2);
	}

	private static ComicVineHttp Connect(string apiKey) => new(new HttpClientHandler(), apiKey, "ua", new RealClock());

	private sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

		public void Advance(TimeSpan by) => now += by;

		public override DateTimeOffset GetUtcNow() => now;
	}

	private sealed class RealClock : IClock
	{
		public DateTime Now => DateTime.Now;

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
	}
}
