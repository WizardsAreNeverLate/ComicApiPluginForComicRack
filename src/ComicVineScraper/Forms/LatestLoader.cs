using System;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Diagnostics;

namespace ComicVineScraper.Plugin;

/// <summary>
/// Replaces utils/scheduler.py for one cover control: starting a load cancels the one before it, and only the newest
/// result is applied. Call it on the UI thread; the continuation comes back there, so apply runs on the UI thread too.
/// </summary>
internal sealed class LatestLoader(DebugLog log)
{
	private CancellationTokenSource? current;

	/// <summary>Never throws. A failure is logged, and a result that is not the newest is disposed if it can be.</summary>
	public async Task Load<T>(Func<CancellationToken, Task<T>> fetch, Action<T> apply)
	{
		Cancel();
		// Not disposed when superseded: a fetch may still hold its token, and a source with no timer owns nothing to free.
		var mine = new CancellationTokenSource();
		current = mine;
		try
		{
			T result = await fetch(mine.Token);
			if (ReferenceEquals(current, mine) && !mine.IsCancellationRequested)
			{
				apply(result);
			}
			else
			{
				(result as IDisposable)?.Dispose();
			}
		}
		catch (OperationCanceledException) when (mine.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			log.Error(error);
		}
	}

	public void Cancel()
	{
		current?.Cancel();
		current = null;
	}
}
