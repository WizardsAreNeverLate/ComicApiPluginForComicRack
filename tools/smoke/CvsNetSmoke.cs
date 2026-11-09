using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace CvsNetSmoke;

/// <summary>
/// Drives the Comic API Plugin inside ComicRack: a scrape of two fileless books, every dialog answered through
/// its controls (no keyboard injection), DrawToBitmap renders, Win32 IsWindowEnabled on the main window, and a debug log
/// save from two forms through the shared log. Writes report.txt and the renders to CVS_NET_SMOKE_OUT, then closes ComicRack.
/// The report carries one ASSERT line for the claim that the forms share one log; run-smoke.ps1 adds the claim about the user's settings.
/// </summary>
public sealed class Probe(IPluginEnvironment env)
{
	private const string Key = "comic-api-net";
	private const string ApiKey = "0123456789abcdef0123456789abcdef01234567";
	private readonly Report report = new(Path.Combine(Environment.GetEnvironmentVariable("CVS_NET_SMOKE_OUT")!, "report.txt"));
	private readonly HashSet<Form> handled = [];
	private readonly string output = Environment.GetEnvironmentVariable("CVS_NET_SMOKE_OUT")!;
	private Form main = null!;
	private int renders;

	[PluginCommand("Startup", Name = "CvsNet Smoke")]
	public void Start()
	{
		main = (Form)env.MainWindow;
		var timer = new System.Windows.Forms.Timer { Interval = 50 };
		timer.Tick += (_, _) => Drive();
		try
		{
			timer.Start();
			Run();
		}
		catch (Exception e)
		{
			report.Add("EXCEPTION " + e);
		}
		finally
		{
			timer.Stop();
			// A plain Close only hides ComicRack to the tray (CloseMinimizesToTray); MenuClose is its File > Exit.
			main.BeginInvoke(new Action(() => main.GetType().GetMethod("MenuClose", BindingFlags.Instance | BindingFlags.Public)!.Invoke(main, null)));
		}
	}

	private void Run()
	{
		Type su = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("cYo.Projects.ComicRack.Viewer.ScriptUtility")).First(t => t != null);
		var engine = (PluginEngine)su.GetProperty("Scripts")!.GetValue(null, null)!;
		MethodInfo createBookCode = su.GetMethod("CreateBookCode")!;
		Command scrape = engine.GetAllCommands().Single(c => c.Key == Key && c.IsHook("Books"));
		report.Add($"command key={scrape.Key} hook={scrape.Hook} configure={scrape.Configure?.Hook}");
		report.Add("main.IsWindowEnabled before=" + IsWindowEnabled(main.Handle));

		ComicBook a = new() { Series = "Batman", Number = "497" };
		ComicBook b = new() { Series = "Batman", Number = "1", Volume = 2016 };
		var watch = System.Diagnostics.Stopwatch.StartNew();
		createBookCode.Invoke(null, [main, scrape, (Func<IEnumerable<ComicBook>>)(() => new[] { a, b })]);
		report.Add($"scrape returned after {watch.ElapsedMilliseconds} ms; main.IsWindowEnabled after=" + IsWindowEnabled(main.Handle));
		Fields("book A (Batman #497)", a);
		Fields("book B (Batman #1, volume 2016)", b);

		watch.Restart();
		scrape.Configure!.Invoke([]);
		report.Add($"configure returned after {watch.ElapsedMilliseconds} ms");
		report.Add("dialogs seen: " + string.Join(", ", seen));
		if (!sharedLogChecked)
		{
			Check("forms share one log", false, "the FinishForm never saved its log, so there was nothing to compare");
		}
	}

	private readonly List<string> seen = [];
	private bool sharedLogChecked;
	private bool comicFormRendered;

	private void Drive()
	{
		List<Form> open;
		try
		{
			open = [.. Application.OpenForms.Cast<Form>().Where(f => f.Visible && f.IsHandleCreated && f.GetType().Namespace == "ComicApiPlugin.Plugin")];
		}
		catch (InvalidOperationException)
		{
			return;
		}
		foreach (Form form in open)
		{
			string type = form.GetType().Name;
			if (type == "ComicForm")
			{
				if (!comicFormRendered && form.Controls.Find("scraping", true).FirstOrDefault()?.Text.Contains("Batman") == true)
				{
					comicFormRendered = true;
					Render(form, "ComicForm");
					report.Add("during run: main.IsWindowEnabled=" + IsWindowEnabled(main.Handle) + " comicForm.Modal=" + form.Modal);
				}
				continue;
			}
			if (type == "ProgressBarForm" || !handled.Add(form))
			{
				continue;
			}
			seen.Add(type);
			form.BeginInvoke(new Action(() => Answer(type, form)));
		}
	}

	private void Answer(string type, Form form)
	{
		string shot = Render(form, type);
		report.Add($"dialog {type}: title='{form.Text}' owner={form.Owner?.GetType().Name ?? "none"} main.IsWindowEnabled={IsWindowEnabled(main.Handle)} render={Path.GetFileName(shot)}");
		switch (type)
		{
			case "ConfigForm" when seen.Count(s => s == "ConfigForm") == 1:
				SaveLog(form, "log-from-configform.txt");
				Find<TextBox>(form, "apiKey").Text = ApiKey;
				Find<Button>(form, "ok").PerformClick();
				break;
			case "ConfigForm":
				Find<Button>(form, "cancel").PerformClick();
				break;
			case "WelcomeForm":
				report.Add("  welcome text: " + Find<Label>(form, "message").Text);
				Find<Button>(form, "start").PerformClick();
				break;
			case "SeriesForm":
				Form comic = Application.OpenForms.Cast<Form>().First(f => f.GetType().Name == "ComicForm");
				string scraping = Find<Label>(comic, "scraping").Text;
				long id = scraping.Contains("497") ? 796 : 91273;
				var grid = Find<DataGridView>(form, "series");
				DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(r => Convert.ToInt64(r.Cells["id"].Value) == id);
				grid.CurrentCell = row.Cells["name"];
				report.Add($"  for '{scraping}': {grid.Rows.Count} candidates, picked {row.Cells["name"].Value} ({id}), comicForm.IsWindowEnabled={IsWindowEnabled(comic.Handle)}");
				Find<Button>(form, "ok").PerformClick();
				break;
			case "FinishForm":
				report.Add("  finish: " + Find<Label>(form, "scraped").Text + " / " + Find<Label>(form, "skipped").Text);
				SaveLog(form, "log-from-finishform.txt");
				CheckSharedLog();
				Find<Button>(form, "ok").PerformClick();
				break;
			default:
				report.Add("  unexpected dialog; closing it: " + string.Join(" | ", Descendants(form).Where(c => c is Label).Select(c => c.Text)));
				form.Close();
				break;
		}
	}

	/// <summary>
	/// What Ctrl+Shift+L does, through the form's own ProcessCmdKey. The save dialog it opens is answered by window messages:
	/// the file name is set with WM_SETTEXT and Save is pressed with BM_CLICK.
	/// </summary>
	private void SaveLog(Form form, string name)
	{
		string path = Path.Combine(output, name);
		var closer = new System.Windows.Forms.Timer { Interval = 200 };
		int attempts = 0;
		IntPtr answered = IntPtr.Zero;
		closer.Tick += (_, _) =>
		{
			attempts++;
			IntPtr dialog = FindThreadDialog();
			if (dialog == IntPtr.Zero)
			{
				if (attempts is 10 or 75)
				{
					report.Add($"  after {attempts * 200} ms, thread windows: " + ThreadWindows());
				}
				if (attempts > 75)
				{
					closer.Stop();
					report.Add("  save dialog never appeared visibly for " + name + "; closing every #32770 of this thread");
					foreach (IntPtr hidden in ThreadWindowHandles("#32770"))
					{
						PostMessage(hidden, WmClose, IntPtr.Zero, IntPtr.Zero);
					}
				}
				return;
			}
			if (answered == dialog)
			{
				report.Add($"  save dialog still open after Save for {name}; closing it");
				PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
				return;
			}
			answered = dialog;
			IntPtr edit = FindFileNameEdit(dialog);
			report.Add($"  save dialog for {name} after {attempts * 200} ms, file name box {(edit == IntPtr.Zero ? "not found" : "found")}");
			SendMessage(edit, WmSetText, IntPtr.Zero, path);
			SendMessage(GetDlgItem(dialog, 1), BmClick, IntPtr.Zero, IntPtr.Zero);
		};
		closer.Start();
		MethodInfo process = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var message = Message.Create(form.Handle, 0x100, (IntPtr)Keys.L, IntPtr.Zero);
		object handledKey = process.Invoke(form, [message, Keys.Control | Keys.Shift | Keys.L])!;
		closer.Stop();
		report.Add($"  Ctrl+Shift+L in {form.GetType().Name}: handled={handledKey}, file {(File.Exists(path) ? new FileInfo(path).Length + " bytes" : "not written")}");
	}

	/// <summary>
	/// The ConfigForm saved its log, then the FinishForm saved its own. One shared log means the second file holds the first form's
	/// lines, including the line the first save wrote about itself, in order before the second save's own line.
	/// </summary>
	private void CheckSharedLog()
	{
		sharedLogChecked = true;
		string first = ReadLog("log-from-configform.txt");
		string second = ReadLog("log-from-finishform.txt");
		const string opened = "opened the settings dialog.";
		const string firstSave = "wrote debug logfile: log-from-configform.txt";
		const string secondSave = "wrote debug logfile: log-from-finishform.txt";
		bool ok = first.Contains(opened) && first.Contains(firstSave) && second.Contains(opened) && second.Contains(firstSave)
			&& second.Contains(secondSave) && second.IndexOf(firstSave, StringComparison.Ordinal) < second.IndexOf(secondSave, StringComparison.Ordinal);
		Check("forms share one log", ok, $"first file {first.Length} chars, second file {second.Length} chars; the second must hold '{opened}' and '{firstSave}' before '{secondSave}'");
	}

	private string ReadLog(string name)
	{
		string path = Path.Combine(output, name);
		return File.Exists(path) ? File.ReadAllText(path) : "";
	}

	/// <summary>One "ASSERT PASS|FAIL name: detail" line in report.txt. run-smoke.ps1 fails the run on any FAIL, or on a missing assertion.</summary>
	private void Check(string name, bool ok, string detail) => report.Add($"ASSERT {(ok ? "PASS" : "FAIL")} {name}: {detail}");

	private void Fields(string label, ComicBook book)
	{
		report.Add(label + ":");
		report.Add($"  Series='{book.Series}' Number='{book.Number}' Volume={book.Volume} Title='{book.Title}' Year={book.Year} Month={book.Month} Day={book.Day}");
		report.Add($"  Publisher='{book.Publisher}' Imprint='{book.Imprint}' Writer='{book.Writer}' Penciller='{book.Penciller}' Inker='{book.Inker}' Colorist='{book.Colorist}' Letterer='{book.Letterer}' CoverArtist='{book.CoverArtist}' Editor='{book.Editor}'");
		report.Add($"  Characters='{Short(book.Characters)}' Teams='{Short(book.Teams)}' Locations='{Short(book.Locations)}' StoryArc='{book.StoryArc}' AlternateSeries='{book.AlternateSeries}'");
		report.Add($"  Web='{book.Web}' Tags='{book.Tags}' Count={book.Count} Format='{book.Format}' Summary.Length={book.Summary?.Length ?? 0} CustomThumbnailKey='{book.CustomThumbnailKey}'");
		report.Add($"  Notes='{book.Notes?.Replace("\r\n", "\\n")}'");
		report.Add($"  custom comicvine_issue={book.GetCustomValue("comicvine_issue")} comicvine_volume={book.GetCustomValue("comicvine_volume")}");
	}

	private static string Short(string? text) => text is null ? "" : text.Length <= 80 ? text : text.Substring(0, 80) + "...";

	private string Render(Form form, string name)
	{
		string path = Path.Combine(output, $"{++renders:00}-{name}.png");
		using var bitmap = new Bitmap(form.Width, form.Height);
		form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
		bitmap.Save(path, ImageFormat.Png);
		return path;
	}

	private static T Find<T>(Control root, string name) where T : Control =>
		root.Controls.Find(name, true).OfType<T>().Single();

	private static IEnumerable<Control> Descendants(Control control)
	{
		foreach (Control child in control.Controls)
		{
			yield return child;
			foreach (Control grandchild in Descendants(child))
			{
				yield return grandchild;
			}
		}
	}

	private static string ThreadWindows()
	{
		var parts = new List<string>();
		EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
		{
			var name = new StringBuilder(64);
			GetClassName(window, name, name.Capacity);
			var title = new StringBuilder(128);
			GetWindowText(window, title, title.Capacity);
			parts.Add($"{name}{(IsWindowVisible(window) ? "" : "(hidden)")}'{title}'");
			return true;
		}, IntPtr.Zero);
		return string.Join("; ", parts);
	}

	private static List<IntPtr> ThreadWindowHandles(string className)
	{
		var found = new List<IntPtr>();
		EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
		{
			var name = new StringBuilder(64);
			GetClassName(window, name, name.Capacity);
			if (name.ToString() == className)
			{
				found.Add(window);
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

	private static IntPtr FindThreadDialog()
	{
		IntPtr found = IntPtr.Zero;
		EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
		{
			var name = new StringBuilder(64);
			GetClassName(window, name, name.Capacity);
			if (name.ToString() == "#32770" && IsWindowVisible(window))
			{
				found = window;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	// The Vista-style save dialog: DUIViewWndClassName > DirectUIHWND > FloatNotifySink > ComboBox > Edit.
	private static IntPtr FindFileNameEdit(IntPtr dialog)
	{
		IntPtr combo = GetDlgItem(dialog, 0x47C);
		if (combo != IntPtr.Zero)
		{
			IntPtr edit = FindWindowEx(combo, IntPtr.Zero, "Edit", null);
			if (edit != IntPtr.Zero)
			{
				return edit;
			}
		}
		IntPtr result = IntPtr.Zero;
		EnumChildWindows(dialog, (window, _) =>
		{
			var name = new StringBuilder(64);
			GetClassName(window, name, name.Capacity);
			if (name.ToString() == "Edit" && IsWindowVisible(window))
			{
				result = window;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return result;
	}

	private const int WmSetText = 0x000C;
	private const int BmClick = 0x00F5;
	private const int WmClose = 0x0010;

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

	private delegate bool EnumProc(IntPtr window, IntPtr data);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowEnabled(IntPtr window);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(IntPtr window);

	[DllImport("user32.dll")]
	private static extern bool EnumThreadWindows(uint thread, EnumProc callback, IntPtr data);

	[DllImport("user32.dll")]
	private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr window, StringBuilder name, int count);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string text);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class Report(string path)
{
	public void Add(string line) => File.AppendAllText(path, line + Environment.NewLine);
}