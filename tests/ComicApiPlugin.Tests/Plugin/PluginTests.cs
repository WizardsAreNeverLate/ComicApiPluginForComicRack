using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;
using ComicApiPlugin.Config;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Tests.Engine;
using ComicApiPlugin.Tests.Plugin.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;
using TUnit.Assertions.Enums;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// The plugin's two commands, driven as ComicRack calls them: on a UI thread that owns a main window, with the real forms
/// answered through their controls and Comic Vine answered from the recorded fixtures.
/// </summary>
public sealed class PluginTests
{
	private const string Issue37566 = "https://comicvine.gamespot.com/api/issue/4000-37566/?api_key=REDACTED&client=cvscraper&format=xml";

	[Test]
	public async Task A_first_scrape_asks_for_the_key_then_welcomes_scrapes_two_fileless_books_and_sums_up()
	{
		using var world = new PluginWorld();
		ComicBook batman497 = new() { Series = "Batman", Number = "497" };
		ComicBook batman1 = new() { Series = "Batman", Number = "1", Volume = 2016 };
		ComicBook late = new() { Series = "Late", Number = "1" };
		bool mainEnabledDuringRun = true;
		bool secondScrapeReturnedAtOnce = false;
		string welcome = "", scraped = "", skipped = "";

		world.Script
			.On<ConfigForm>(form =>
			{
				Ui.Find<TextBox>(form, "apiKey").Text = EngineWorld.ApiKey;
				Ui.Find<Button>(form, "ok").PerformClick();
			})
			.On<WelcomeForm>(form =>
			{
				welcome = Ui.Find<Label>(form, "message").Text;
				Ui.Find<Button>(form, "start").PerformClick();
			})
			.On<SeriesForm>(form =>
			{
				mainEnabledDuringRun = IsWindowEnabled(world.Main.Handle);
				var watch = Stopwatch.StartNew();
				world.Plugin.Scrape([late]);
				secondScrapeReturnedAtOnce = watch.ElapsedMilliseconds < 1000;
				PickSeries(form);
			})
			.On<SeriesForm>(PickSeries)
			.On<FinishForm>(form =>
			{
				(scraped, skipped) = (Ui.Find<Label>(form, "scraped").Text, Ui.Find<Label>(form, "skipped").Text);
				Ui.Find<Button>(form, "ok").PerformClick();
			});

		world.Run(plugin => plugin.Scrape([batman497, batman1]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "ConfigForm", "WelcomeForm", "SeriesForm", "SeriesForm", "FinishForm" }, CollectionOrdering.Matching);
		await Assert.That(batman497.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
		await Assert.That(batman497.GetCustomValue("comicvine_volume")).IsEqualTo("796");
		await Assert.That(batman497.Notes).Contains("[CVDB37566]");
		await Assert.That(batman1.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		await Assert.That(batman1.GetCustomValue("comicvine_volume")).IsEqualTo("91273");
		await Assert.That(late.GetCustomValue("comicvine_issue")).IsNull();
		await Assert.That(secondScrapeReturnedAtOnce).IsTrue();
		await Assert.That(mainEnabledDuringRun).IsFalse();
		await Assert.That(world.MainEnabledAfter).IsTrue();
		await Assert.That(welcome).IsEqualTo(world.Strings.Format(TextKeys.WelcomeFormTextPlural, 2));
		await Assert.That(scraped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormScrapedPlural, 2));
		await Assert.That(skipped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormSkippedPlural, 0));
		await Assert.That(new SettingsStore(world.Folder, new ManualClock()).Load().ApiKey).IsEqualTo(EngineWorld.ApiKey);
		await Assert.That(world.Plugin.Context.Settings.LoadChosenSeries().Contains(new SeriesId(796))).IsTrue();
		await Assert.That(world.Plugin.Context.Settings.LoadChosenSeries().Contains(new SeriesId(91273))).IsTrue();
		await Assert.That(world.Log.Any(line => line.Contains(EngineWorld.ApiKey))).IsFalse();
		// "/issue/" is one issue's details (ApiRequest.IssueDetail), which the scrape itself fetches, so a miss there is a real gap in the fixtures.
		// A full run also misses cover images and a few "/issues/" list queries that no fixture records. The "/issue/" substring does not match
		// "/issues/", so those expected misses stay out of the assertion.
		await Assert.That(world.Unrecorded.Any(key => key.Contains("/issue/") || key.Contains("/search/") || key.Contains("/volume/"))).IsFalse();
	}

	[Test]
	public async Task Cancelling_the_key_prompt_stops_before_any_request_and_still_sums_up_as_Python_did()
	{
		using var world = new PluginWorld();
		ComicBook book = new() { Series = "Batman", Number = "497" };
		world.Script
			.On<ConfigForm>(form => Ui.Find<Button>(form, "cancel").PerformClick())
			.On<FinishForm>(form => Ui.Find<Button>(form, "ok").PerformClick());

		world.Run(plugin => plugin.Scrape([book]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "ConfigForm", "FinishForm" }, CollectionOrdering.Matching);
		await Assert.That(world.Requested).IsEmpty();
		await Assert.That(book.GetCustomValue("comicvine_issue")).IsNull();
		await Assert.That(world.Log).Contains("API key still not available.  Aborting.");
	}

	[Test]
	public async Task A_run_ending_error_is_shown_once_and_logged_once_and_then_the_partial_summary()
	{
		using var world = new PluginWorld(Override(Issue37566, "invalid-key.txt"));
		world.SaveSettings(new Configuration { ApiKey = EngineWorld.ApiKey, Advanced = AdvancedSettings.Parse(null) });
		ComicBook known = new() { Series = "Batman", Number = "497", Tags = "CVDB37566" };
		string error = "", scraped = "";
		world.Script
			.On<WelcomeForm>(form => Ui.Find<Button>(form, "start").PerformClick())
			.On<PromptDialog>(prompt =>
			{
				error = prompt.Text;
				Ui.Find<Button>(prompt, "ok").PerformClick();
			})
			.On<FinishForm>(form =>
			{
				scraped = Ui.Find<Label>(form, "scraped").Text;
				Ui.Find<Button>(form, "ok").PerformClick();
			});

		world.Run(plugin => plugin.Scrape([known]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "WelcomeForm", "PromptDialog", "FinishForm" }, CollectionOrdering.Matching);
		await Assert.That(error).IsEqualTo("Cannot Access Online Database");
		await Assert.That(scraped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormScrapedPlural, 0));
		await Assert.That(world.Log.Count(line => line.StartsWith("Caught ComicVineException", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(world.Log.Any(line => line.Contains(EngineWorld.ApiKey))).IsFalse();
		await Assert.That(known.GetCustomValue("comicvine_issue")).IsNull();
	}

	[Test]
	public async Task A_run_that_fails_before_the_settings_load_still_shows_the_error_and_the_summary()
	{
		// Python built its Configuration before loading it, so the summary default held when the load failed. SettingsStore.Load
		// swallows every bad file the tests can write, so the failure comes from the call that precedes the load in the same try.
		using var world = new PluginWorld();
		world.ProductVersion = () => throw new InvalidOperationException("no version");
		string scraped = "", skipped = "";
		world.Script
			.On<PromptDialog>(prompt => Ui.Find<Button>(prompt, "no").PerformClick())
			.On<FinishForm>(form =>
			{
				(scraped, skipped) = (Ui.Find<Label>(form, "scraped").Text, Ui.Find<Label>(form, "skipped").Text);
				Ui.Find<Button>(form, "ok").PerformClick();
			});

		world.Run(plugin => plugin.Scrape([new ComicBook { Series = "Batman", Number = "497" }]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "PromptDialog", "FinishForm" }, CollectionOrdering.Matching);
		await Assert.That(scraped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormScrapedPlural, 0));
		await Assert.That(skipped).IsEqualTo(world.Strings[TextKeys.FinishFormSkippedSingle]);
	}

	[Test]
	public async Task A_run_that_cannot_open_its_context_fails_visibly_and_does_not_block_the_next_run()
	{
		using var world = new PluginWorld();
		string good = LanguagePack.Folder;
		world.CommandPath = Path.Combine(world.Folder, "no-such-pack");
		world.Script
			.On<ConfigForm>(form => Ui.Find<Button>(form, "cancel").PerformClick())
			.On<FinishForm>(form => Ui.Find<Button>(form, "ok").PerformClick());
		var firstFailure = new List<Exception>();
		var secondFailure = new List<Exception>();

		world.Run(plugin =>
		{
			ComicBook[] books = [new ComicBook { Series = "Batman", Number = "497" }];
			try { plugin.Scrape(books); } catch (Exception error) { firstFailure.Add(error); }
			try { plugin.Scrape(books); } catch (Exception error) { secondFailure.Add(error); }
			world.CommandPath = good;
			plugin.Scrape(books);
		});

		await Assert.That(firstFailure.Count).IsEqualTo(1);
		await Assert.That(secondFailure.Count).IsEqualTo(1);
		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "ConfigForm", "FinishForm" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task An_empty_selection_logs_the_header_and_still_shows_the_summary_as_Python_did()
	{
		using var world = new PluginWorld();
		string scraped = "", skipped = "";
		world.Script.On<FinishForm>(form =>
		{
			(scraped, skipped) = (Ui.Find<Label>(form, "scraped").Text, Ui.Find<Label>(form, "skipped").Text);
			Ui.Find<Button>(form, "ok").PerformClick();
		});

		world.Run(plugin => plugin.Scrape([]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "FinishForm" }, CollectionOrdering.Matching);
		await Assert.That(scraped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormScrapedPlural, 0));
		await Assert.That(skipped).IsEqualTo(world.Strings.Format(TextKeys.FinishFormSkippedPlural, 0));
		await Assert.That(world.Requested).IsEmpty();
		await Assert.That(world.Log).Contains("Scraper terminated normally (scraped 0, skipped 0).");
		await Assert.That(world.Log.Any(line => line.StartsWith("CV Scraper Version:", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task Configure_opens_the_settings_dialog_over_the_same_store_and_log_as_the_scrape()
	{
		using var world = new PluginWorld();
		world.Script
			.On<ConfigForm>(form => Ui.Find<Button>(form, "cancel").PerformClick())
			.On<FinishForm>(form => Ui.Find<Button>(form, "ok").PerformClick())
			.On<ConfigForm>(form =>
			{
				Ui.Find<TextBox>(form, "apiKey").Text = EngineWorld.ApiKey;
				Ui.Find<Button>(form, "ok").PerformClick();
			});

		world.Run(plugin =>
		{
			plugin.Scrape([new ComicBook { Series = "Batman", Number = "497" }]);
			plugin.Configure();
		});

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "ConfigForm", "FinishForm", "ConfigForm" }, CollectionOrdering.Matching);
		await Assert.That(world.Log).Contains("API key not available.  Showing config dialog.");
		await Assert.That(world.Log.Count(line => line == "opened the settings dialog.")).IsEqualTo(2);
		await Assert.That(world.Plugin.Context.Settings.Load().ApiKey).IsEqualTo(EngineWorld.ApiKey);
	}

	[Test]
	public async Task Settings_changed_from_the_welcome_form_apply_to_the_scrape_that_follows()
	{
		using var world = new PluginWorld();
		world.SaveSettings(new Configuration { ApiKey = EngineWorld.ApiKey, Advanced = AdvancedSettings.Parse(null) });
		ComicBook book = new() { Series = "Batman", Number = "497" };
		world.Script
			.On<WelcomeForm>(form =>
			{
				Ui.Find<Button>(form, "settings").PerformClick();
				Ui.Find<Button>(form, "start").PerformClick();
			})
			.On<ConfigForm>(form =>
			{
				Ui.Find<CheckBox>(form, "summaryDialog").Checked = false;
				Ui.Find<Button>(form, "ok").PerformClick();
			})
			.On<SeriesForm>(PickSeries);

		world.Run(plugin => plugin.Scrape([book]));

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "WelcomeForm", "ConfigForm", "SeriesForm" }, CollectionOrdering.Matching);
		await Assert.That(book.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
	}

	[Test]
	public async Task Two_scrapes_in_sequence_both_run_and_share_one_http_client()
	{
		using var world = new PluginWorld();
		world.SaveSettings(QuietConfiguration(EngineWorld.ApiKey));
		ComicBook first = new() { Series = "Batman", Number = "497" };
		ComicBook second = new() { Series = "Batman", Number = "1", Volume = 2016 };
		world.Script.On<SeriesForm>(PickSeries).On<SeriesForm>(PickSeries);

		world.Run(plugin =>
		{
			plugin.Scrape([first]);
			plugin.Scrape([second]);
		});

		await Assert.That(world.Script.Seen).IsEquivalentTo(new[] { "SeriesForm", "SeriesForm" }, CollectionOrdering.Matching);
		await Assert.That(first.GetCustomValue("comicvine_issue")).IsEqualTo("37566");
		await Assert.That(second.GetCustomValue("comicvine_issue")).IsEqualTo("535328");
		await Assert.That(world.Handlers.Count).IsEqualTo(1);
		await Assert.That(world.Handlers[0].Disposed).IsFalse();
	}

	[Test]
	public async Task Changing_the_api_key_between_runs_replaces_and_disposes_the_http_client()
	{
		const string newKey = "fedcba9876543210fedcba9876543210fedcba98";
		using var world = new PluginWorld();
		world.SaveSettings(QuietConfiguration(EngineWorld.ApiKey));
		world.Script.On<SeriesForm>(PickSeries).On<SeriesForm>(PickSeries);

		world.Run(plugin =>
		{
			plugin.Scrape([new ComicBook { Series = "Batman", Number = "497" }]);
			world.SaveSettings(QuietConfiguration(newKey));
			plugin.Scrape([new ComicBook { Series = "Batman", Number = "1", Volume = 2016 }]);
		});

		await Assert.That(world.Handlers.Count).IsEqualTo(2);
		await Assert.That(world.Handlers[0].Disposed).IsTrue();
		await Assert.That(world.Handlers[1].Disposed).IsFalse();
		// Image requests carry no key, so only the API calls are checked.
		string[] oldCalls = [.. world.Handlers[0].Urls.Where(url => url.Contains("/api/"))];
		string[] newCalls = [.. world.Handlers[1].Urls.Where(url => url.Contains("/api/"))];
		await Assert.That(oldCalls).IsNotEmpty();
		await Assert.That(newCalls).IsNotEmpty();
		await Assert.That(oldCalls.All(url => url.Contains("api_key=" + EngineWorld.ApiKey))).IsTrue();
		await Assert.That(newCalls.All(url => url.Contains("api_key=" + newKey))).IsTrue();
	}

	/// <summary>A key, no welcome form, and no summary, so a run shows only the series dialog.</summary>
	private static Configuration QuietConfiguration(string apiKey) =>
		new() { ApiKey = apiKey, SummaryDialog = false, Advanced = AdvancedSettings.Parse("WELCOME_DIALOG = False") };

	private static void PickSeries(SeriesForm form)
	{
		string book = Ui.Find<Label>(Ui.Mine<ComicForm>()!, "scraping").Text;
		long id = book.Contains("497") ? 796 : 91273;
		DataGridView grid = Ui.Find<DataGridView>(form, "series");
		DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(r => Convert.ToInt64(r.Cells["id"].Value) == id);
		grid.CurrentCell = row.Cells["name"];
		Ui.Find<Button>(form, "ok").PerformClick();
	}

	private static RecordedResponse Override(string url, string fixture)
	{
		RecordedResponse recorded = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, fixture));
		return recorded with { Url = url };
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowEnabled(IntPtr window);

	/// <summary>A plugin instance over a main window of its own, a settings folder of its own, and the recorded responses.</summary>
	private sealed class PluginWorld : IDisposable
	{
		private readonly List<RecordedResponse> responses;
		private readonly List<TrackedHandler> handlers = [];
		private readonly List<string> unrecorded = [];
		private readonly HostEnvironment env = new();

		public PluginWorld(params RecordedResponse[] overrides)
		{
			responses = [.. FixtureFiles.ReadFolder(FixtureFiles.OutputFolder), .. overrides];
			var ports = new PluginPorts(NewHandler, Folder, new ManualClock());
			Plugin = new ComicApiPlugin(env, ports);
		}

		public string Folder { get; } = Path.Combine(Path.GetTempPath(), "cvs-plugin-" + Guid.NewGuid().ToString("N"));

		public ComicApiPlugin Plugin { get; }

		public DialogScript Script { get; } = new();

		public Form Main => env.Main!;

		public bool MainEnabledAfter { get; private set; }

		/// <summary>What ComicRack reports as its version. Throwing from it fails the run before the settings load.</summary>
		public Func<string> ProductVersion
		{
			set => env.Fake.Version = value;
		}

		/// <summary>Where the plugin opens its language pack from. A bad folder fails the context open.</summary>
		public string CommandPath
		{
			set => env.CommandPath = value;
		}

		public Strings Strings => Plugin.Context.Strings;

		public IReadOnlyList<string> Log => Plugin.Context.Log.Snapshot();

		/// <summary>One per HttpClient the plugin made, in order.</summary>
		public IReadOnlyList<TrackedHandler> Handlers => handlers;

		public IReadOnlyList<string> Requested => [.. handlers.SelectMany(handler => handler.Requested)];

		/// <summary>The requests no fixture answered: cover lookups and images the dialogs ask for.</summary>
		public IReadOnlyList<string> Unrecorded => unrecorded;

		public void SaveSettings(Configuration config) => new SettingsStore(Folder, new ManualClock()).Save(config);

		/// <summary>Runs the commands on a UI thread with a visible main window, as ComicRack calls them.</summary>
		public void Run(Action<ComicApiPlugin> commands) => Sta.Run(() =>
		{
			using var main = new Form { Text = "ComicRack (test)", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
			env.Main = main;
			main.Show();
			using IDisposable driver = Script.Start();
			commands(Plugin);
			MainEnabledAfter = IsWindowEnabled(main.Handle);
		});

		public void Dispose()
		{
			try
			{
				Directory.Delete(Folder, recursive: true);
			}
			catch (DirectoryNotFoundException)
			{
			}
		}

		private TrackedHandler NewHandler()
		{
			var handler = new TrackedHandler(new FixtureHandler(responses, key =>
			{
				lock (unrecorded)
				{
					unrecorded.Add(key);
				}
				return FixtureHandler.NothingFound(key);
			}));
			handlers.Add(handler);
			return handler;
		}
	}

	/// <summary>The fixture handler, plus the raw request URLs (with the key) and whether the plugin disposed it.</summary>
	private sealed class TrackedHandler(FixtureHandler inner) : DelegatingHandler(inner)
	{
		public List<string> Urls { get; } = [];

		public bool Disposed { get; private set; }

		public IReadOnlyList<string> Requested => inner.Requested;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
		{
			lock (Urls)
			{
				Urls.Add(request.RequestUri!.AbsoluteUri);
			}
			return base.SendAsync(request, ct);
		}

		protected override void Dispose(bool disposing)
		{
			Disposed = true;
			base.Dispose(disposing);
		}
	}

	private sealed class HostEnvironment : IPluginEnvironment
	{
		public Form? Main { get; set; }
		public IWin32Window MainWindow => Main!;
		public FakeApplication Fake { get; } = new() { Pages = (_, _) => null };
		public IApplication App => Fake;
		public IEnumerable<string> LibraryPaths => [];
		public IOpenBooksManager OpenBooks => null!;
		public IBrowser Browser => null!;
		public IComicDisplay ComicDisplay => null!;
		public string CommandPath { get; set; } = LanguagePack.Folder;
		public IThemePlugin Theme { get; set; } = null!;
		public string Localize(string resourceKey, string elementKey, string text) => text;
		public object Clone() => this;
	}
}

/// <summary>
/// Answers the plugin's dialogs in order as each one opens on the test's UI thread. A dialog the script does not expect
/// is recorded and closed, so the test fails on its assertions instead of hanging.
/// </summary>
internal sealed class DialogScript
{
	// The test's main window is a plain Form; the ComicForm and the progress window are the run's own windows, not dialogs.
	private static readonly HashSet<string> Ignored = ["Form", "ComicForm", "ProgressBarForm"];

	private readonly Queue<(Type Type, Action<Form> Act)> steps = new();
	private readonly HashSet<Form> handled = [];

	public List<string> Seen { get; } = [];

	public DialogScript On<T>(Action<T> act) where T : Form
	{
		steps.Enqueue((typeof(T), form => act((T)form)));
		return this;
	}

	public IDisposable Start()
	{
		var timer = new Timer { Interval = 10 };
		timer.Tick += (_, _) =>
		{
			foreach (Form form in Mine().Where(form => !handled.Contains(form) && !Ignored.Contains(form.GetType().Name)))
			{
				handled.Add(form);
				Seen.Add(form.GetType().Name);
				if (steps.Count > 0 && steps.Peek().Type == form.GetType())
				{
					Action<Form> act = steps.Dequeue().Act;
					form.BeginInvoke(new Action(() => act(form)));
				}
				else
				{
					form.BeginInvoke(new Action(form.Close));
				}
			}
		};
		timer.Start();
		return timer;
	}

	private static List<Form> Mine()
	{
		try
		{
			return [.. Application.OpenForms.Cast<Form>().Where(form => !form.InvokeRequired && form.Visible && form.IsHandleCreated)];
		}
		catch (InvalidOperationException)
		{
			return [];
		}
	}
}
