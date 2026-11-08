using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Fixtures;

/// <summary>
/// A clock whose Delay advances Now instead of waiting, so a test asserts the 1100 ms gap without spending it.
/// RealPause makes each Delay also yield for a moment of real time, which widens races between threads.
/// </summary>
public sealed class ManualClock(DateTime start) : IClock
{
	private readonly object gate = new();
	private readonly List<TimeSpan> delays = [];
	private DateTime now = start;

	public ManualClock() : this(new DateTime(2026, 11, 9, 12, 0, 0))
	{
	}

	public TimeSpan RealPause { get; init; }

	public DateTime Now
	{
		get
		{
			lock (gate)
			{
				return now;
			}
		}
	}

	public IReadOnlyList<TimeSpan> Delays
	{
		get
		{
			lock (gate)
			{
				return delays.ToArray();
			}
		}
	}

	public async Task Delay(TimeSpan delay, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (RealPause > TimeSpan.Zero)
		{
			await Task.Delay(RealPause, ct);
		}
		lock (gate)
		{
			delays.Add(delay);
			now += delay;
		}
	}
}
