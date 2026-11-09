using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Plugin;

namespace ComicVineScraper.Tests.Plugin.Forms;

public sealed class LatestLoaderTests
{
	[Test]
	public async Task A_new_load_cancels_the_one_before_and_only_the_newest_is_applied()
	{
		var loader = new LatestLoader(new DebugLog());
		var first = new TaskCompletionSource<string>();
		var second = new TaskCompletionSource<string>();
		CancellationToken firstToken = default;
		var applied = new List<string>();

		Task one = loader.Load(ct =>
		{
			firstToken = ct;
			ct.Register(() => first.TrySetCanceled());
			return first.Task;
		}, applied.Add);
		Task two = loader.Load(_ => second.Task, applied.Add);
		second.SetResult("second");
		await Task.WhenAll(one, two);

		await Assert.That(firstToken.IsCancellationRequested).IsTrue();
		await Assert.That(applied).IsEquivalentTo(["second"]);
	}

	[Test]
	public async Task A_superseded_result_that_arrives_anyway_is_disposed_and_not_applied()
	{
		var loader = new LatestLoader(new DebugLog());
		var stale = new TaskCompletionSource<Probe>();
		var applied = new List<Probe>();

		Task one = loader.Load(_ => stale.Task, applied.Add);
		Task two = loader.Load(_ => Task.FromResult(new Probe("fresh")), applied.Add);
		var late = new Probe("late");
		stale.SetResult(late);
		await Task.WhenAll(one, two);

		await Assert.That(applied.Select(p => p.Name)).IsEquivalentTo(["fresh"]);
		await Assert.That(late.Disposed).IsTrue();
	}

	[Test]
	public async Task A_failed_load_is_logged_and_never_throws()
	{
		var log = new DebugLog();
		var loader = new LatestLoader(log);
		var applied = new List<string>();

		await loader.Load<string>(_ => throw new InvalidOperationException("cover server down"), applied.Add);

		await Assert.That(applied).IsEmpty();
		await Assert.That(log.Snapshot().Any(line => line.Contains("cover server down"))).IsTrue();
	}

	[Test]
	public async Task Cancel_stops_the_load_in_flight()
	{
		var loader = new LatestLoader(new DebugLog());
		var pending = new TaskCompletionSource<string>();
		var applied = new List<string>();

		Task load = loader.Load(ct =>
		{
			ct.Register(() => pending.TrySetCanceled());
			return pending.Task;
		}, applied.Add);
		loader.Cancel();
		await load;

		await Assert.That(applied).IsEmpty();
	}

	private sealed class Probe(string name) : IDisposable
	{
		public string Name { get; } = name;
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}
}
