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
/// Scrapes two identical sets of fileless books with the original Python plugin and with the C# plugin, both in auto-choose
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
	private string? scrapingNumber;
	private readonly Dictionary<string, int> signatures = [];

	/// <summary>The books both sets contain. Series, number and volume (the series' start year) are all a fileless book carries.</summary>
	private static readonly (string Series, string Number, int Volume, string Why)[] Books =
	[
		("Saga", "1", 2012, "clear single match, Image"),
		("Saga", "2", -1, "no volume year"),
		("Batman", "497", 1940, "long series, first volume"),
		("Batman", "1", 2016, "long series, later volume"),
		("Batman Annual", "1", 2012, "annual"),
		("Savage Dragon", "0", -1, "issue #0, Image"),
		("Guardians of the Galaxy", "0.1", 2013, "decimal issue number"),
		("Wizard", "\u00BD", -1, "fraction issue number"),
		("Y: The Last Man", "1", 2002, "punctuation, Vertigo"),
		("Hack/Slash", "1", -1, "slash in the title"),
		("Rick & Morty", "1", 2015, "ampersand; Comic Vine says 'Rick and Morty'"),
		("The Walking Dead", "1", 2003, "Image, with article"),
		("Hellboy", "1", -1, "Dark Horse, colon title on Comic Vine"),
		("Teenage Mutant Ninja Turtles", "1", 2011, "IDW"),
		("Amazing Spider-Man", "300", 1963, "Comic Vine adds 'The'"),
		("Zzxqv Nonexistent Chronicle", "1", -1, "no series match"),
		("Batman", "99999", 1940, "series exists, issue does not"),
	];

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
		ComicBook[] baseline = NewBooks();
		Dump(snapshot, "baseline", baseline);
		for (int i = 0; i < Books.Length; i++)
		{
			Line(snapshot, "baseline", i, "~label", $"{Books[i].Series} #{Books[i].Number} (volume {Books[i].Volume}): {Books[i].Why}");
		}
		snapshot.Flush();

		foreach ((string set, Command command) in new[] { ("python", python), ("net", net) })
		{
			currentSet = set;
			ComicBook[] books = NewBooks();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			createBookCode.Invoke(null, [main, command, (Func<IEnumerable<ComicBook>>)(() => books)]);
			report.Add($"{set} scrape of {books.Length} books returned after {watch.Elapsed.TotalSeconds:F0} s");
			Dump(snapshot, set, books);
		}
		currentSet = "done";
		report.Add($"dialogs answered in total: {dialogCount}");
	}

	private static ComicBook[] NewBooks() =>
		[.. Books.Select(b =>
		{
			var book = new ComicBook { Series = b.Series, Number = b.Number };
			if (b.Volume > 0)
			{
				book.Volume = b.Volume;
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

	private void Drive()
	{
		try
		{
			DriveNative();
			List<Form> open = [.. Application.OpenForms.Cast<Form>().Where(f => f != main && f.Visible && f.IsHandleCreated && IsPluginForm(f))];
			foreach (Form form in open)
			{
				string type = TypeName(form);
				string? scraping = Descendants(form).OfType<Label>().Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Scraping:", StringComparison.Ordinal));
				if (scraping is not null)
				{
					int hash = scraping.LastIndexOf('#');
					scrapingNumber = hash < 0 ? null : scraping.Substring(hash + 1).Split(' ')[0];
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
		dialogCount++;
		var labels = Descendants(form).OfType<Label>().Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t));
		var buttons = Descendants(form).OfType<Button>().Select(b => b.Text + (b.Enabled ? "" : "(disabled)"));
		report.Add($"DIALOG set={currentSet} type={type} title='{form.Text}' labels=[{string.Join(" | ", labels)}] buttons=[{string.Join(" | ", buttons)}]");
		foreach (DataGridView grid in Descendants(form).OfType<DataGridView>())
		{
			report.Add($"  grid {grid.Name}: {grid.Rows.Count} rows");
			foreach (DataGridViewRow row in grid.Rows.Cast<DataGridViewRow>().Take(6))
			{
				report.Add("    " + string.Join(" ; ", row.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value)?.Replace("\r\n", " ") ?? "")));
			}
			if (grid.Rows.Count > 0)
			{
				DataGridViewRow chosen = BestRow(grid, scrapingNumber);
				grid.CurrentCell = chosen.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
				grid.ClearSelection();
				chosen.Selected = true;
				report.Add("  selected row " + chosen.Index);
			}
		}
		string signature = type + "|" + form.Text + "|" + string.Join("|", labels);
		signatures.TryGetValue(signature, out int seenBefore);
		signatures[signature] = seenBefore + 1;
		bool repeated = signatures[signature] > 3;
		Button[] all = [.. Descendants(form).OfType<Button>().Where(b => b.Enabled && b.Visible)];
		Button? accept = form.AcceptButton as Button ?? all.FirstOrDefault(b => b.DialogResult == DialogResult.OK || b.Text == "OK");
		if (repeated)
		{
			report.Add("  the same dialog appeared more than three times; cancelling it");
			(form.CancelButton as Button ?? all.FirstOrDefault(b => b.DialogResult == DialogResult.Cancel || b.Text is "Cancel" or "Skip"))?.PerformClick();
			if (form.Visible)
			{
				form.Close();
			}
		}
		else if (accept is { Enabled: true })
		{
			report.Add("  pressed accept button '" + accept.Text + "'");
			accept.PerformClick();
		}
		else if (form.CancelButton is Button cancel)
		{
			report.Add("  pressed cancel button '" + cancel.Text + "'");
			cancel.PerformClick();
		}
		else
		{
			report.Add("  no accept or cancel button; closing the form");
			form.Close();
		}
	}

	/// <summary>On an issue list, the row whose number is the one being scraped. Otherwise the row with the highest Match score, the lowest ID among equals, so the choice does not depend on the order of tied rows. Without a Match column, the first row.</summary>
	private static DataGridViewRow BestRow(DataGridView grid, string? number)
	{
		bool isIssueList = !grid.Columns.Cast<DataGridViewColumn>().Any(c => c.HeaderText == "Match" || c.Name == "Match");
		if (isIssueList && number is not null)
		{
			DataGridViewRow? byNumber = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => string.Equals(Convert.ToString(r.Cells[0].Value)?.Trim(), number, StringComparison.Ordinal));
			if (byNumber is not null)
			{
				return byNumber;
			}
		}
		DataGridViewColumn? Column(string name) => grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.HeaderText, name, StringComparison.OrdinalIgnoreCase));
		DataGridViewColumn? match = Column("Match");
		DataGridViewColumn? id = Column("ID");
		if (match is null || id is null)
		{
			return grid.Rows[0];
		}
		return grid.Rows.Cast<DataGridViewRow>()
			.OrderByDescending(r => Convert.ToDouble(r.Cells[match.Index].Value, CultureInfo.InvariantCulture))
			.ThenBy(r => Convert.ToInt64(r.Cells[id.Index].Value, CultureInfo.InvariantCulture))
			.First();
	}

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
