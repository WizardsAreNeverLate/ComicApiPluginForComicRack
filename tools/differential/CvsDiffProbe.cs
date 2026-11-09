using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using cYo.Common;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace CvsDiffProbe;

/// <summary>
/// Scrapes two identical sets of books (books.tsv: fileless ones, and ones made from a .cbz with a real cover) with the original Python plugin and with the C# plugin, both in auto-choose
/// mode against the live Comic Vine API, then writes every field either plugin can write to snapshot.tsv. Dialogs that still
/// appear are answered by one policy for both plugins and logged. Closes ComicRack through MainForm.MenuClose when done.
/// </summary>
public sealed class Probe(IPluginEnvironment env)
{
	private const string PythonKey = "comic-vine-scraper";
	private const string NetKey = "comic-vine-scraper-net";
	private readonly string output = Environment.GetEnvironmentVariable("CVS_DIFF_OUT")!;
	private readonly Report report = new(Path.Combine(Environment.GetEnvironmentVariable("CVS_DIFF_OUT")!, "probe.txt"));
	private readonly HashSet<Form> handled = [];
	private readonly HashSet<IntPtr> nativeHandled = [];
	private Form main = null!;
	private string currentSet = "setup";
	private int dialogCount;
	private readonly Dictionary<string, int> signatures = [];

	/// <summary>One row of books.tsv. Cover books are made from a .cbz (File, written by the runner); the rest are fileless.</summary>
	private sealed record Book(string Series, string Number, int Year, long VolumeId, bool FromName, string File, string Why)
	{
		public string Label => $"{Series} #{Number}";
	}

	private readonly List<Book> books = [];
	private readonly Dictionary<int, List<string>> events = [];
	private int currentBook = -1;

	private void LoadBooks()
	{
		string[] lines = File.ReadAllLines(Path.Combine(Environment.GetEnvironmentVariable("CVS_DIFF_INPUT")!, "books.tsv"));
		string[] header = lines[0].Split('\t');
		foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
		{
			string[] cells = line.Split('\t');
			string Cell(string name) => cells[Array.IndexOf(header, name)];
			long.TryParse(Cell("VolumeId"), out long volumeId);
			books.Add(new Book(Cell("Series"), Cell("Number"), int.Parse(Cell("Year"), CultureInfo.InvariantCulture), volumeId, Cell("FromName") == "y", Cell("File"), Cell("Why")));
		}
	}

	[PluginCommand("Startup", Name = "CvsDiff Probe")]
	public void Start()
	{
		main = (Form)env.MainWindow;
		var timer = new System.Windows.Forms.Timer { Interval = 100 };
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
		Command python = engine.GetAllCommands().Single(c => c.Key == PythonKey && c.IsHook("Books"));
		Command net = engine.GetAllCommands().Single(c => c.Key == NetKey && c.IsHook("Books"));
		report.Add($"python command: key={python.Key} hook={python.Hook}; net command: key={net.Key} hook={net.Hook}");

		using var snapshot = new StreamWriter(Path.Combine(output, "snapshot.tsv"), false, new UTF8Encoding(false));
		LoadBooks();
		ComicBook[] baseline = NewBooks();
		Dump(snapshot, "baseline", baseline);
		Preflight(baseline);

		for (int i = 0; i < books.Count; i++)
		{
			Line(snapshot, "baseline", i, "~label", $"{books[i].Label} (volume {books[i].Year}, {(books[i].File.Length > 0 ? "from a .cbz" + (books[i].FromName ? " named after the book" : "") : "fileless")}): {books[i].Why}");
		}
		snapshot.Flush();

		foreach ((string set, Command command) in new[] { ("python", python), ("net", net) })
		{
			currentSet = set;
			lastProgress = DateTime.UtcNow;
			events.Clear();
			ComicBook[] batch = NewBooks();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			createBookCode.Invoke(null, [main, command, (Func<IEnumerable<ComicBook>>)(() => batch)]);
			report.Add($"{set} scrape of {batch.Length} books returned after {watch.Elapsed.TotalSeconds:F0} s");
			Dump(snapshot, set, batch);
			if (fatal)
			{
				throw new InvalidOperationException("the probe stopped the run after a failure; see the EXCEPTION line above");
			}
			for (int i = 0; i < batch.Length; i++)
			{
				Line(snapshot, set, i, "~dialogs", events.TryGetValue(i, out List<string>? seen) ? string.Join(" ", seen) : "");
			}
			snapshot.Flush();
		}
		currentSet = "done";
		report.Add($"dialogs answered in total: {dialogCount}");
	}

	/// <summary>ComicRack must hand each cover book's first page to a plugin before any request is spent; a failure here stops the run.</summary>
	private void Preflight(ComicBook[] baseline)
	{
		for (int i = 0; i < baseline.Length; i++)
		{
			if (books[i].File.Length == 0)
			{
				continue;
			}
			ComicBook book = baseline[i];
			System.Drawing.Bitmap? page = null;
			var worker = new System.Threading.Thread(() => page = env.App.GetComicPage(book, 0));
			worker.Start();
			bool returned = worker.Join(TimeSpan.FromSeconds(30));
			report.Add($"PREFLIGHT {Path.GetFileName(books[i].File)}: pages={book.PageCount} GetComicPage(0) {(returned ? (page is null ? "returned null" : $"returned {page.Width}x{page.Height}") : "did not return in 30 s")}");
			if (!returned || page is null)
			{
				throw new InvalidOperationException("ComicRack cannot supply the cover page of " + Path.GetFileName(books[i].File));
			}
			page.Dispose();
		}
	}

	private ComicBook[] NewBooks() =>
		[.. books.Select(b =>
		{
			ComicBook book = b.File.Length > 0 ? ComicBook.Create(b.File, RefreshInfoOptions.GetPageCount) : new ComicBook();
			if (!b.FromName)
			{
				book.Series = b.Series;
				book.Number = b.Number;
				if (b.Year > 0)
				{
					book.Volume = b.Year;
				}
			}
			return book;
		})];

	private static readonly HashSet<Type> Simple = [typeof(string), typeof(bool), typeof(int), typeof(long), typeof(float), typeof(double), typeof(DateTime), typeof(Guid), typeof(short), typeof(byte)];

	private static bool IsSimple(Type t)
	{
		t = Nullable.GetUnderlyingType(t) ?? t;
		return t.IsEnum || Simple.Contains(t);
	}

	/// <summary>One line per set, book and field: set, book index, field, value. A field is every readable and writable simple property of ComicBook.</summary>
	private void Dump(StreamWriter writer, string set, ComicBook[] books)
	{
		PropertyInfo[] properties = [.. typeof(ComicBook).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && IsSimple(p.PropertyType))
			.OrderBy(p => p.Name, StringComparer.Ordinal)];
		for (int i = 0; i < books.Length; i++)
		{
			ComicBook book = books[i];
			foreach (PropertyInfo p in properties)
			{
				object? value;
				try
				{
					value = p.GetValue(book, null);
				}
				catch (Exception e)
				{
					value = "<throws " + e.GetType().Name + ">";
				}
				Line(writer, set, i, p.Name, value);
			}
			foreach (StringPair pair in book.GetCustomValues().OrderBy(v => v.Key, StringComparer.Ordinal))
			{
				Line(writer, set, i, "custom:" + pair.Key, pair.Value);
			}
			Line(writer, set, i, "thumbnail.sha1", ThumbnailHash(book.CustomThumbnailKey));
		}
		writer.Flush();
	}

	private static string ThumbnailHash(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return "";
		}
		Type program = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("cYo.Projects.ComicRack.Viewer.Program")).First(t => t != null);
		object pool = program.GetProperty("ImagePool", BindingFlags.Public | BindingFlags.Static)!.GetValue(null, null)!;
		var folder = (string)pool.GetType().GetProperty("CustomThumbnailFolder")!.GetValue(pool, null)!;
		string file = Path.Combine(folder, key);
		if (!File.Exists(file))
		{
			return "<missing file>";
		}
		byte[] bytes = File.ReadAllBytes(file);
		using var sha = SHA1.Create();
		return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() + " (" + bytes.Length + " bytes)";
	}

	private static void Line(StreamWriter writer, string set, int index, string field, object? value)
	{
		string text = value switch
		{
			null => "",
			float f => f.ToString("R", CultureInfo.InvariantCulture),
			double d => d.ToString("R", CultureInfo.InvariantCulture),
			DateTime t => t.ToString("o", CultureInfo.InvariantCulture),
			IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? "",
		};
		text = text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
		writer.WriteLine($"{set}\t{index}\t{field}\t{text}");
	}

	private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(4);
	private DateTime lastProgress = DateTime.UtcNow;
	private bool fatal;

	/// <summary>An unknown book or a dialog the policy cannot answer is not waited on: the run is recorded as failed, the plugin's forms are closed, and ComicRack exits.</summary>
	private void Fail(string reason)
	{
		if (fatal)
		{
			return;
		}
		fatal = true;
		report.Add("EXCEPTION " + reason);
		foreach (Form form in Application.OpenForms.Cast<Form>().Where(f => f != main && IsPluginForm(f)).ToList())
		{
			form.Close();
		}
		main.BeginInvoke(new Action(() => main.GetType().GetMethod("MenuClose", BindingFlags.Instance | BindingFlags.Public)!.Invoke(main, null)));
	}

	private void DumpWindows()
	{
		foreach (Form form in Application.OpenForms.Cast<Form>())
		{
			report.Add($"  open form {form.GetType().FullName} '{form.Text}' visible={form.Visible} enabled={form.Enabled} modal={form.Modal}");
		}
	}

	private void Drive()
	{
		if (!fatal && DateTime.UtcNow - lastProgress > StuckAfter)
		{
			DumpWindows();
			Fail($"no progress for {StuckAfter.TotalMinutes:F0} minutes");
		}
		try
		{
			DriveNative();
			foreach (Form news in Application.OpenForms.Cast<Form>().Where(f => f.Visible && f.GetType().Name == "NewsDialog").ToList())
			{
				report.Add($"ComicRack's own '{news.Text}' dialog (modal={news.Modal}) was open and blocked the scrape; closing it");
				news.Close();
				lastProgress = DateTime.UtcNow;
			}
			List<Form> open = [.. Application.OpenForms.Cast<Form>().Where(f => f != main && f.Visible && f.IsHandleCreated && IsPluginForm(f))];
			foreach (Form form in open)
			{
				string type = TypeName(form);
				string? scraping = Descendants(form).OfType<Label>().Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Scraping:", StringComparison.Ordinal));
				if (scraping is not null)
				{
					string name = System.Text.RegularExpressions.Regex.Replace(scraping.Substring("Scraping:".Length), @"\s*\(Vol\. [^)]*\)\s*$", "");
					name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
					int found = books.FindIndex(b => name == (b.File.Length > 0 ? Path.GetFileName(b.File) : System.Text.RegularExpressions.Regex.Replace(b.Label, @"\s+", " ")));
					if (found < 0)
					{
						Fail($"the status form names a book that is not in books.tsv: '{name}'");
						return;
					}
					if (found != currentBook)
					{
						lastProgress = DateTime.UtcNow;
					}
					currentBook = found;
				}
				if (IsStatusForm(form, type) || !handled.Add(form))
				{
					continue;
				}
				form.BeginInvoke(new Action(() => Answer(type, form)));
			}
		}
		catch (InvalidOperationException)
		{
		}
	}

	/// <summary>The forms that only show progress: the C# ComicForm and ProgressBarForm, and the Python ones (a dynamic type with no useful name) that show "Scraping:" or a progress bar.</summary>
	private static bool IsStatusForm(Form form, string type) =>
		type is "ComicForm" or "ProgressBarForm"
		|| Descendants(form).OfType<ProgressBar>().Any()
		|| Descendants(form).OfType<Label>().Any(l => l.Text.StartsWith("Scraping:", StringComparison.Ordinal))
		|| Descendants(form).OfType<Button>().Any(b => b.Text.Contains(" remaining)"));

	private static string TypeName(Form form) => form.GetType().Name.Split('$')[0];

	private static bool IsPluginForm(Form form) =>
		form.GetType().Namespace == "ComicVineScraper.Plugin" || form.GetType().Assembly.IsDynamic;

	/// <summary>
	/// The same policy for both plugins: select the best-scoring row of any candidate list, press the form's accept button when it
	/// is enabled, otherwise its cancel button, otherwise close it. What the form showed is logged.
	/// </summary>
	private void Answer(string type, Form form)
	{
		if (IsStatusForm(form, type))
		{
			return;
		}
		lastProgress = DateTime.UtcNow;
		dialogCount++;
		var labels = Descendants(form).OfType<Label>().Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
		var buttons = Descendants(form).OfType<Button>().Select(b => b.Text + (b.Enabled ? "" : "(disabled)"));
		string bookLabel = currentBook >= 0 ? books[currentBook].Label : "unknown book";
		report.Add($"DIALOG set={currentSet} book='{bookLabel}' type={type} title='{form.Text}' labels=[{string.Join(" | ", labels)}] buttons=[{string.Join(" | ", buttons)}]");
		Button[] all = [.. Descendants(form).OfType<Button>().Where(b => b.Enabled && b.Visible)];
		Button? Named(string text) => all.FirstOrDefault(b => b.Text == text);
		Button? accept = form.AcceptButton as Button ?? all.FirstOrDefault(b => b.DialogResult == DialogResult.OK || b.Text == "OK");
		Button? skip = Named("Skip") ?? form.CancelButton as Button;
		string signature = type + "|" + form.Text + "|" + string.Join("|", labels) + "|" + bookLabel;
		signatures.TryGetValue(signature, out int seenBefore);
		signatures[signature] = seenBefore + 1;

		DataGridView? grid = Descendants(form).OfType<DataGridView>().FirstOrDefault();
		if (grid is not null)
		{
			report.Add($"  grid {grid.Name}: {grid.Rows.Count} rows");
			foreach (DataGridViewRow row in grid.Rows.Cast<DataGridViewRow>().Take(6))
			{
				report.Add("    " + string.Join(" ; ", row.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value)?.Replace("\r\n", " ") ?? "")));
			}
		}
		if (seenBefore >= 3)
		{
			Record("repeated", "skip");
			Press(form, skip ?? form.CancelButton as Button);
			return;
		}
		if (grid is not null && grid.Rows.Count > 0)
		{
			bool isSeriesList = ColumnNamed(grid, "Match") is not null;
			Book? book = currentBook >= 0 ? books[currentBook] : null;
			DataGridViewRow? row = null;
			string why;
			if (book is null)
			{
				why = "the book being scraped is unknown";
				Fail("a dialog appeared while no known book was being scraped: " + form.Text);
				return;
			}
			else if (isSeriesList)
			{
				DataGridViewColumn? id = ColumnNamed(grid, "ID");
				row = book.VolumeId == 0 || id is null ? null : grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Convert.ToInt64(r.Cells[id.Index].Value, CultureInfo.InvariantCulture) == book.VolumeId);
				why = book.VolumeId == 0 ? "the book has no correct volume" : $"volume {book.VolumeId} is not listed";
			}
			else
			{
				row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => string.Equals(Convert.ToString(r.Cells[0].Value)?.Trim(), book.Number, StringComparison.Ordinal));
				why = $"issue {book.Number} is not listed";
			}
			string kind = isSeriesList ? "series" : "issue";
			if (row is null)
			{
				Record(kind, "skip: " + why);
				report.Add($"  {kind} dialog: {why}; pressing Skip");
				Press(form, skip);
				return;
			}
			grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
			grid.ClearSelection();
			row.Selected = true;
			Record(kind, "pick");
			report.Add($"  selected row {row.Index}");
			Press(form, accept);
			return;
		}
		Record("other", "default");
		Press(form, accept ?? form.CancelButton as Button);
	}

	private void Record(string kind, string action)
	{
		if (currentBook < 0)
		{
			return;
		}
		if (!events.TryGetValue(currentBook, out List<string>? list))
		{
			events[currentBook] = list = [];
		}
		list.Add($"{kind}={action.Split(':')[0]}");
	}

	private void Press(Form form, Button? button)
	{
		if (button is { Enabled: true })
		{
			report.Add("  pressed '" + button.Text + "'");
			button.PerformClick();
		}
		else
		{
			report.Add("  no usable button; closing the form");
			form.Close();
		}
	}

	private static DataGridViewColumn? ColumnNamed(DataGridView grid, string name) =>
		grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.HeaderText, name, StringComparison.OrdinalIgnoreCase));

	private void DriveNative()
	{
		var visible = new List<IntPtr>();
		EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
		{
			var name = new StringBuilder(64);
			GetClassName(window, name, name.Capacity);
			if (name.ToString() == "#32770" && IsWindowVisible(window))
			{
				visible.Add(window);
			}
			return true;
		}, IntPtr.Zero);
		nativeHandled.IntersectWith(visible);
		foreach (IntPtr dialog in visible.Where(nativeHandled.Add))
		{
			dialogCount++;
			var title = new StringBuilder(256);
			GetWindowText(dialog, title, title.Capacity);
			var texts = new List<string>();
			EnumChildWindows(dialog, (child, _) =>
			{
				var name = new StringBuilder(64);
				GetClassName(child, name, name.Capacity);
				var text = new StringBuilder(1024);
				GetWindowText(child, text, text.Capacity);
				if (name.ToString() == "Static" && text.Length > 0)
				{
					texts.Add(text.ToString().Replace("\r\n", " "));
				}
				return true;
			}, IntPtr.Zero);
			int choice = new[] { 1, 6, 2, 7 }.FirstOrDefault(id => GetDlgItem(dialog, id) != IntPtr.Zero);
			report.Add($"DIALOG set={currentSet} type=MessageBox title='{title}' text=[{string.Join(" | ", texts)}] pressed button id {choice}");
			if (choice == 0)
			{
				PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
			}
			else
			{
				PostMessage(GetDlgItem(dialog, choice), BmClick, IntPtr.Zero, IntPtr.Zero);
			}
		}
	}

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

	private const int BmClick = 0x00F5;
	private const int WmClose = 0x0010;

	private delegate bool EnumProc(IntPtr window, IntPtr data);

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

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class Report(string path)
{
	public void Add(string line) => File.AppendAllText(path, line + Environment.NewLine);
}
