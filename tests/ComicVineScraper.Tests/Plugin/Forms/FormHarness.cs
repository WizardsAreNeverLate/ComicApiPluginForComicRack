using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ComicVineScraper.Config;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Plugin;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Tests.Plugin.Forms;

/// <summary>WinForms needs a single-threaded apartment. Each call runs on its own STA thread and rethrows what the body threw.</summary>
internal static class Sta
{
	public static T Run<T>(Func<T> body)
	{
		T result = default!;
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() =>
		{
			try
			{
				// Without this, an exception in a timer or event handler shows WinForms' own error dialog, and the test hangs.
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
				result = body();
			}
			catch (Exception e)
			{
				error = ExceptionDispatchInfo.Capture(e);
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		if (!thread.Join(TimeSpan.FromSeconds(60)))
		{
			throw new TimeoutException("The UI thread did not finish within 60 s; a dialog was probably left open.");
		}
		error?.Throw();
		return result;
	}

	public static void Run(Action body) => Run(() =>
	{
		body();
		return 0;
	});
}

/// <summary>A settings folder of its own and a PluginContext over it. With marked strings, every localized text is wrapped in ⟦ ⟧.</summary>
internal sealed class UiSession : IDisposable
{
	public const string Open = "⟦";
	public const string Close = "⟧";

	private UiSession(bool marked)
	{
		Folder = Path.Combine(Path.GetTempPath(), "cvs-ui-" + Guid.NewGuid().ToString("N"));
		Log = new DebugLog();
		Settings = new SettingsStore(Folder, new ManualClock(), Log);
		Ctx = new PluginContext(ComicVineScraper.Plugin.Strings.Load(new FormsEnvironment(LanguagePack.Folder, marked)), null, Settings, Log);
	}

	public string Folder { get; }
	public DebugLog Log { get; }
	public SettingsStore Settings { get; }
	public PluginContext Ctx { get; }
	public Strings Strings => Ctx.Strings;

	public static UiSession Create(bool marked = false) => new(marked);

	/// <summary>Defender or the indexer can briefly hold a file just written, so deleting retries. Cleanup that still fails is left to the temp folder.</summary>
	public void Dispose()
	{
		for (int attempt = 0; attempt < 20 && Directory.Exists(Folder); attempt++)
		{
			try
			{
				Directory.Delete(Folder, true);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(50);
			}
		}
	}

	private sealed class FormsEnvironment(string commandPath, bool marked) : IPluginEnvironment
	{
		public IEnumerable<string> LibraryPaths => [];
		public IWin32Window MainWindow => null!;
		public IApplication App => null!;
		public IOpenBooksManager OpenBooks => null!;
		public IBrowser Browser => null!;
		public IComicDisplay ComicDisplay => null!;
		public string CommandPath { get; set; } = commandPath;
		public IThemePlugin Theme { get; set; } = null!;
		public string Localize(string resourceKey, string elementKey, string text) => marked ? Open + text + Close : text;
		public object Clone() => this;
	}
}

/// <summary>Drives forms the way a user does: through their controls, keys, and mouse messages.</summary>
internal static class Ui
{
	private const int WmKeyDown = 0x100;
	private const int WmKeyUp = 0x101;
	private const int WmActivate = 0x006;
	private const int WmMouseMove = 0x200;
	private const int WmLButtonDown = 0x201;
	private const int WmLButtonUp = 0x202;

	private static readonly Regex Marked = new(Regex.Escape(UiSession.Open) + ".*?" + Regex.Escape(UiSession.Close), RegexOptions.Singleline);

	/// <summary>Runs act once the form is up and its Shown handlers are done, from inside its message loop.</summary>
	public static void WhenShown(Form form, Action act) => form.Shown += (_, _) => form.BeginInvoke(act);

	/// <summary>
	/// Runs act on the first visible form of type T, from whatever modal loop is running. Polls Application.OpenForms,
	/// because the code under test creates the form. Must be called on the UI thread before the loop starts.
	/// </summary>
	public static IDisposable WhenOpen<T>(Action<T> act) where T : Form
	{
		var timer = new System.Windows.Forms.Timer { Interval = 10 };
		bool done = false;
		timer.Tick += (_, _) =>
		{
			if (!done && Mine<T>() is { } form)
			{
				done = true;
				timer.Stop();
				// Posted, so it runs after the form's own Shown handlers, which WinForms posts the same way.
				form.BeginInvoke(new Action(() => act(form)));
			}
		};
		timer.Start();
		return timer;
	}

	/// <summary>
	/// The first visible T on this thread. OpenForms spans every thread, and tests run in parallel, so other threads' forms
	/// are skipped, and a list changed under the enumeration is read again on the next tick.
	/// </summary>
	public static T? Mine<T>() where T : Form
	{
		try
		{
			return Application.OpenForms.OfType<T>().FirstOrDefault(form => !form.InvokeRequired && form.Visible && form.IsHandleCreated);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	public static T Find<T>(Control root, string name) where T : Control =>
		root.Controls.Find(name, true).OfType<T>().SingleOrDefault() ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}' in {root.GetType().Name}.");

	/// <summary>Pumps messages until the condition holds, so async continuations on the UI thread run.</summary>
	public static void PumpUntil(Func<bool> condition, int timeoutMs = 30000)
	{
		DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (!condition())
		{
			if (DateTime.UtcNow > end)
			{
				throw new TimeoutException("The UI did not reach the expected state in time.");
			}
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	/// <summary>A key as the message loop sees it before any control handles it (ProcessCmdKey), with no modifier held.</summary>
	public static bool PressCommandKey(Control target, Keys key)
	{
		Message message = Message.Create(target.Handle, WmKeyDown, (IntPtr)key, IntPtr.Zero);
		return target.PreProcessMessage(ref message);
	}

	public static void KeyDown(Control target, Keys key) => SendMessage(target.Handle, WmKeyDown, (IntPtr)key, IntPtr.Zero);

	public static void KeyUp(Control target, Keys key) => SendMessage(target.Handle, WmKeyUp, (IntPtr)key, (IntPtr)unchecked((int)0xC0000001));

	/// <summary>What a form receives when it becomes the active window, as a dialog the user works in is.</summary>
	public static void Activate(Form form) => SendMessage(form.Handle, WmActivate, (IntPtr)1, IntPtr.Zero);

	/// <summary>What a form receives when another window takes the focus, as happens to a modal dialog while it closes.</summary>
	public static void Deactivate(Form form) => SendMessage(form.Handle, WmActivate, IntPtr.Zero, IntPtr.Zero);

	public static void MouseMove(Control target, Point at) => SendMessage(target.Handle, WmMouseMove, IntPtr.Zero, Position(at));

	public static void Click(Control target, Point at)
	{
		MouseMove(target, at);
		SendMessage(target.Handle, WmLButtonDown, (IntPtr)1, Position(at));
		SendMessage(target.Handle, WmLButtonUp, IntPtr.Zero, Position(at));
	}

	/// <summary>Draws the control with DrawToBitmap into the test output's form-renders folder and returns the file.</summary>
	public static string Render(Control control, string name)
	{
		string folder = Path.Combine(AppContext.BaseDirectory, "form-renders");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, name + ".png");
		using var bitmap = new Bitmap(control.Width, control.Height);
		control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
		bitmap.Save(path, ImageFormat.Png);
		return path;
	}

	/// <summary>
	/// The visible texts that hold a letter outside a localized string, given marked strings. Data the test supplied, such as a
	/// series name, is removed first. Covers the window title, every control, grid headers, list items, and context menus.
	/// </summary>
	public static IReadOnlyList<string> UnlocalizedTexts(Control root, params string[] data)
	{
		var texts = new List<string>();
		Collect(root, texts);
		return texts.Where(text => Unmarked(text, data).Any(char.IsLetter)).ToList();
	}

	private static string Unmarked(string text, string[] data)
	{
		string rest = Marked.Replace(text, "");
		return data.Aggregate(rest, (current, item) => current.Replace(item, ""));
	}

	private static void Collect(Control control, List<string> texts)
	{
		if (control is not TextBoxBase)
		{
			texts.Add(control.Text);
		}
		if (control.ContextMenuStrip is { } menu)
		{
			texts.AddRange(menu.Items.Cast<ToolStripItem>().Select(item => item.Text));
		}
		switch (control)
		{
			case DataGridView grid:
				texts.AddRange(grid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible).Select(column => column.HeaderText));
				break;
			case ListBox list:
				texts.AddRange(list.Items.Cast<object>().Select(item => item.ToString()));
				break;
		}
		foreach (Control child in control.Controls)
		{
			Collect(child, texts);
		}
	}

	private static IntPtr Position(Point at) => (IntPtr)((at.Y << 16) | (at.X & 0xFFFF));

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
