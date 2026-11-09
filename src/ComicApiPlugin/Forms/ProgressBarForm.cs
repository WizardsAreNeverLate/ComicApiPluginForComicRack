using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ComicApiPlugin.Engine;

namespace ComicApiPlugin.Plugin;

/// <summary>A bare progress bar whose title says how far a query has got. The user closing it cancels the run.</summary>
internal sealed class ProgressBarForm : ScraperForm
{
	private readonly Action userClosed;
	private bool closingByCode;

	public ProgressBarForm(PluginContext ctx, Action userClosed) : base(ctx, "pbform")
	{
		this.userClosed = userClosed;
		ClientSize = new Size(400, 23);
		Controls.Add(Bar);
	}

	public ProgressBar Bar { get; } = new() { Name = "progress", Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, Step = 1 };

	public void CloseByCode()
	{
		closingByCode = true;
		Close();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		base.OnFormClosed(e);
		// The owner is already closing; asking it to close again would re-enter its FormClosed.
		if (!closingByCode && e.CloseReason != CloseReason.FormOwnerClosing)
		{
			userClosed();
		}
	}
}

/// <summary>
/// The progress of one series search or issue load. Reports arrive on the thread that reads the responses and are
/// posted to the UI thread. The window opens on the first report that leaves work to do and closes on Dispose.
/// </summary>
internal sealed class QueryProgressWindow : IQueryProgress
{
	private readonly ComicForm host;
	private readonly PluginContext ctx;
	private readonly QueryKind kind;
	private readonly SynchronizationContext? ui = SynchronizationContext.Current;
	private ProgressBarForm? window;
	private bool disposed;

	public QueryProgressWindow(ComicForm host, PluginContext ctx, QueryKind kind)
	{
		this.host = host;
		this.ctx = ctx;
		this.kind = kind;
	}

	public void Report(QueryProgress value)
	{
		if (ui is null)
		{
			Show(value);
		}
		else
		{
			ui.Post(_ => Show(value), null);
		}
	}

	public void Dispose()
	{
		disposed = true;
		if (ui is null)
		{
			Close();
		}
		else
		{
			ui.Post(_ => Close(), null);
		}
	}

	private void Close()
	{
		window?.CloseByCode();
		window?.Dispose();
		window = null;
	}

	private void Show(QueryProgress value)
	{
		if (disposed)
		{
			return;
		}
		if (kind == QueryKind.SeriesSearch)
		{
			ProgressBarForm searching = Open(value.Total);
			searching.Bar.PerformStep();
			searching.Text = ctx.Strings.Format(TextKeys.SearchProgbarText, value.Done);
			return;
		}
		double done = value.Total <= 0 ? 1 : Math.Max(0.0, Math.Min(1.0, value.Done / (double)value.Total));
		int percent = (int)(done * 100);
		if (window is null && done < 1.0)
		{
			Open(100);
		}
		if (window is not null)
		{
			window.Bar.Value = percent;
			window.Text = ctx.Strings.Format(TextKeys.IssuesProgbarText, percent);
		}
	}

	/// <summary>Opens the window with the given maximum the first time; afterwards returns the open window.</summary>
	private ProgressBarForm Open(int maximum)
	{
		if (window is null)
		{
			window = new ProgressBarForm(ctx, host.RequestCancel);
			window.Bar.Maximum = maximum;
			window.Owner = host;
			window.Show();
		}
		return window;
	}
}
