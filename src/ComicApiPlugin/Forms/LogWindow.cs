using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The activity log: a window that is not modal and shows each debug log line as it is written. A run opens it, or keeps the
/// one still open. Closing it never stops or pauses the run.
/// </summary>
internal sealed class LogWindow : ScraperForm
{
	private const int WmEnable = 0x000A;

	private readonly TextBox log = new()
	{
		Name = "log",
		Multiline = true,
		ReadOnly = true,
		BackColor = SystemColors.Window,
		WordWrap = false,
		ScrollBars = ScrollBars.Both,
		MaxLength = int.MaxValue,
		Font = new Font(FontFamily.GenericMonospace, 8.25F),
		Location = new Point(10, 10),
		Size = new Size(540, 268),
		Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
		TabIndex = 1,
	};

	private LogWindow(PluginContext ctx) : base(ctx, "logwindow", persistSize: true)
	{
		Text = ProductTitle;
		FormBorderStyle = FormBorderStyle.Sizable;
		ClientSize = new Size(560, 320);
		MinimumSize = SizeFromClientSize(new Size(300, 150));
		var close = new Button
		{
			Name = "close",
			Text = Strings[TextKeys.LogWindowClose],
			Location = new Point(460, 288),
			Size = new Size(90, 23),
			Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
			UseVisualStyleBackColor = true,
			TabIndex = 0,
		};
		close.Click += (_, _) => Close();
		log.ContextMenuStrip = CopyMenu();
		Controls.AddRange([log, close]);
		Ctx.Log.LineWritten += OnLine;
	}

	/// <summary>
	/// Returns existing while it is still open, so it keeps what it shows. Otherwise opens a new window without blocking, starting
	/// with the log's lines from firstLine on, so it also holds what the run wrote before it opened.
	/// </summary>
	public static LogWindow ShowOrReuse(LogWindow? existing, IWin32Window? owner, PluginContext ctx, int firstLine)
	{
		if (existing is { IsDisposed: false, Visible: true })
		{
			return existing;
		}
		existing?.Dispose();
		var window = new LogWindow(ctx);
		window.Append(ctx.Log.Snapshot().Skip(firstLine));
		if (owner is null)
		{
			window.Show();
		}
		else
		{
			window.Show(owner);
		}
		return window;
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		Ctx.Log.LineWritten -= OnLine;
		base.OnFormClosed(e);
	}

	// A run shows its ComicForm and dialogs with ShowDialog, which disables every other window of the thread. This one stays
	// enabled, so it can be scrolled and closed while the run goes on.
	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);
		if (m.Msg == WmEnable && m.WParam == IntPtr.Zero)
		{
			EnableWindow(Handle, true);
		}
	}

	private void OnLine(string line)
	{
		if (IsDisposed)
		{
			return;
		}
		if (!InvokeRequired)
		{
			Append([line]);
			return;
		}
		try
		{
			BeginInvoke(new Action(() => Append([line])));
		}
		catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
		{
			// The window closed between the check and the call.
		}
	}

	private void Append(IEnumerable<string> lines)
	{
		string text = string.Concat(lines.Select(line => line.Replace("\r", "").Replace("\n", "\r\n") + "\r\n"));
		if (text.Length > 0 && !IsDisposed)
		{
			log.AppendText(text);
		}
	}

	private ContextMenuStrip CopyMenu()
	{
		var menu = new ContextMenuStrip();
		menu.Items.Add(Strings[TextKeys.TextCopy], null, (_, _) => log.Copy());
		return menu;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
