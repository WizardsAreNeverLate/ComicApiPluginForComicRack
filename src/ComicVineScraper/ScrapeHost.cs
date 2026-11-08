using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ComicVineScraper.Plugin;

/// <summary>
/// Runs an async scrape inside the ComicForm's modal loop, so the command blocks until the run ends
/// (the Editor hook and the undo group need that) while ComicRack's windows keep painting.
/// </summary>
internal static class ScrapeHost
{
	/// <summary>
	/// Shows the ComicForm with ShowDialog(owner) and starts work from its Shown handler, on the UI SynchronizationContext.
	/// The form closes when work ends, and Run returns its result or rethrows its exception with the original stack.
	/// Closing the form any other way (Cancel, Esc, the close box) cancels the token and keeps the form up until work
	/// observes it, so work never outlives Run.
	/// </summary>
	public static T Run<T>(IWin32Window owner, PluginContext ctx, Func<ComicForm, CancellationToken, Task<T>> work)
	{
		using var cts = new CancellationTokenSource();
		using var form = new ComicForm(ctx);
		Task<T>? run = null;
		bool finished = false;

		form.Shown += (_, _) => run = RunThenClose();
		form.FormClosing += (_, e) =>
		{
			if (run is not null && !finished)
			{
				e.Cancel = true;
				// Deferred so a continuation that runs inline on Cancel cannot call form.Close() inside this handler.
				form.BeginInvoke(new Action(cts.Cancel));
				form.ShowCancelling();
			}
		};
		form.ShowDialog(owner);

		return run is null ? throw new InvalidOperationException("The scrape window closed before it was shown.") : run.GetAwaiter().GetResult();

		async Task<T> RunThenClose()
		{
			try
			{
				return await work(form, cts.Token);
			}
			finally
			{
				finished = true;
				form.Close();
			}
		}
	}
}
