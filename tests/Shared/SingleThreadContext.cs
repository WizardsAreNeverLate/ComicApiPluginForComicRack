using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ComicVineScraper.Fixtures;

/// <summary>A one-thread message loop, as ComicRack's UI thread is: every continuation posted to it runs on the thread that called Run.</summary>
internal sealed class SingleThreadContext : SynchronizationContext
{
	private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

	public static T Run<T>(Func<Task<T>> work)
	{
		SynchronizationContext? previous = Current;
		var context = new SingleThreadContext();
		SetSynchronizationContext(context);
		try
		{
			Task<T> task = work();
			task.ContinueWith(_ => context.queue.CompleteAdding(), TaskScheduler.Default);
			foreach ((SendOrPostCallback callback, object? state) in context.queue.GetConsumingEnumerable())
			{
				callback(state);
			}
			return task.GetAwaiter().GetResult();
		}
		finally
		{
			SetSynchronizationContext(previous);
		}
	}

	public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

	public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
}
