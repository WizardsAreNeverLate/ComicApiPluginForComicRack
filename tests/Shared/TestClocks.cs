using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Engine;

namespace ComicApiPlugin.Fixtures;

/// <summary>
/// A clock whose Delay advances Now (and Elapsed) instead of waiting, so a test asserts the 1100 ms gap without spending it.
/// RealPause makes each Delay also yield for a moment of real time, which widens races between threads.
/// </summary>
public sealed class ManualClock(DateTime start) : IClock
{
	private readonly object gate = new();
	private readonly List<TimeSpan> delays = [];
	private DateTime now = start;
	private TimeSpan elapsed;

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

	/// <summary>A monotonic reading: it advances with Delay and ignores JumpWallClock, like a Stopwatch.</summary>
	public TimeSpan Elapsed
	{
		get
		{
			lock (gate)
			{
				return elapsed;
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

	/// <summary>Moves the wall clock without any time passing, as a daylight-saving change or an NTP step does.</summary>
	public void JumpWallClock(TimeSpan by)
	{
		lock (gate)
		{
			now += by;
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
			elapsed += delay;
		}
	}
}
