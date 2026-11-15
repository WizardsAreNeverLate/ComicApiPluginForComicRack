using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Matching;
using ComicApiPlugin.VerseDb;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// ComicRack creates one instance per process and calls every hook on the UI thread. The Books and Editor hooks both scrape,
/// and the Configure action opens the settings dialog. The Startup and Shutdown hooks start and stop the local API service.
/// </summary>
public sealed class ComicApiCommands : DotNetPlugin, IBooks, IEditor, IConfig, IStartup, IShutdown
{
	private static readonly string Version = typeof(ComicApiCommands).Assembly.GetName().Version.ToString(3);
	private static readonly string UserAgent = $"ComicApiPlugin/{Version} (https://github.com/cbanack/comic-vine-scraper/)";

	private readonly PluginPorts ports;

	// Stateless; it loads the OpenCV native library on its first comparison, so a plugin that never uses the cover auto-accept never touches it.
	private readonly OpenCvCoverComparer coverComparer = new();
	private IPluginEnvironment env;
	private PluginContext? context;
	private ComicVineHttp? http;
	private string? httpKey;
	private VerseDbHttp? verseDb;
	private string? verseDbToken;
	private LogWindow? logWindow;
	private ServiceHost? service;
	private bool running;

	/// <summary>ComicRack's constructor. ComicRack passes the environment to <see cref="OnLoad"/>.</summary>
	public ComicApiCommands() : this(null!, PluginPorts.FromProcess())
	{
	}

	internal ComicApiCommands(IPluginEnvironment env, PluginPorts ports)
	{
		this.env = env;
		this.ports = ports;
	}

	// Not the Python plugin's "comic-vine-scraper", so both can be installed for the end-to-end comparison.
	public override string Key { get; set; } = "comic-api-net";

	public override string Name { get; set; } = "Comic API Plugin";

	public override string Image { get; set; } = "comicvinescraper.png";

	protected override void OnLoad()
	{
		env = ComicRack;
	}

	void IBooks.Execute(ComicBook[] books) => Scrape(books);

	void IEditor.Execute(ComicBook[] books) => Scrape(books);

	void IConfig.Execute() => Configure();

	void IStartup.Execute()
	{
		try
		{
			CheckService(Context);
		}
		catch (Exception error)
		{
			// Nothing may reach ComicRack from its startup. A context that cannot open fails the first scrape, which shows the error.
			Trace.TraceError("Comic API Plugin could not start its local API service: " + error);
		}
	}

	bool IShutdown.Execute(bool user_is_closing)
	{
		service?.Stop();
		return true;
	}

	/// <summary>One store and one log for the plugin's lifetime, so two commands never write the settings over each other.</summary>
	internal PluginContext Context => context ??= PluginContext.Open(env, ports.SettingsFolder, ports.Clock);

	/// <summary>
	/// scrapeengine.py's scrape. Blocks until the run ends, as both hooks require: the Editor hook passes a copy of the book
	/// and reads it back the moment this returns. A second call while a run is in progress returns at once.
	/// </summary>
	public void Scrape(ComicBook[] books)
	{
		if (running)
		{
			return;
		}
		// Opened before the flag is raised and failing as Configure does (to ComicRack), so a failed open cannot leave the flag set.
		PluginContext ctx = Context;
		// Python built its Configuration before loading it, so the summary default (on) holds when the load itself fails.
		Configuration config = new() { Advanced = AdvancedSettings.Parse(null) };
		var summary = new RunSummary(0, books.Length, Cancelled: false);
		int firstLine = ctx.Log.Snapshot().Count;
		bool logShown = false;
		running = true;
		try
		{
			LogStart(ctx);
			CheckService(ctx);
			if (books.Length == 0)
			{
				// Python still logged the header, said it finished and showed the summary for an empty selection.
				ctx.Log.Debug("Scraper terminated normally (scraped 0, skipped 0).");
				return;
			}
			config = ctx.Settings.Load();
			if (config.AvailableActions().Count == 0)
			{
				ctx.Log.Debug("No provider has an API key and is enabled.  Showing config dialog.");
				ConfigForm.Show(env.MainWindow, ctx);
				config = ctx.Settings.Load();
				if (config.AvailableActions().Count == 0)
				{
					ctx.Log.Debug("Still no provider available.  Aborting.");
					return;
				}
			}
			ScrapeAction action = config.AvailableActions()[0];
			if (config.Advanced.WelcomeDialog)
			{
				ScrapeAction? picked = WelcomeForm.Show(env.MainWindow, ctx, books.Length);
				config = ctx.Settings.Load();
				if (picked is not { } chosen)
				{
					ctx.Log.Debug("Cancelled!");
					return;
				}
				action = chosen;
			}
			if (config.SummaryDialog)
			{
				ShowLog(ctx, firstLine);
				logShown = true;
			}
			summary = action == ScrapeAction.VerseDbCoverSearch ? RunCoverSearch(ctx, config, books) : Run(ctx, config, books);
		}
		catch (ScrapeFailedException failure)
		{
			// The engine logged the cause.
			summary = failure.Summary;
			ErrorDialogs.Show(env.MainWindow, ctx, failure.InnerException!);
		}
		catch (CoverSearchFailedException failure)
		{
			summary = failure.Summary;
			ErrorDialogs.Show(env.MainWindow, ctx, failure.InnerException!);
		}
		catch (Exception error)
		{
			ctx.Log.Error(error);
			ErrorDialogs.Show(env.MainWindow, ctx, error);
		}
		finally
		{
			try
			{
				// Python's finally: the summary follows every run, including one that stopped early or failed. It goes into the log
				// window, which opens here only if the run ended before opening it; one the user closed during the run stays closed.
				if (config.SummaryDialog)
				{
					Guarded(ctx, () =>
					{
						LogSummary(ctx, summary);
						if (!logShown)
						{
							ShowLog(ctx, firstLine);
						}
					});
				}
			}
			finally
			{
				// Guarded's own error dialog can throw too; the flag must still drop, or every later Scrape does nothing.
				running = false;
			}
			(env.MainWindow as Form)?.Activate();
		}
	}

	/// <summary>The window still open from an earlier run is reused; otherwise a new one opens with this run's lines so far.</summary>
	private void ShowLog(PluginContext ctx, int firstLine) => logWindow = LogWindow.ShowOrReuse(logWindow, env.MainWindow, ctx, firstLine);

	private static void LogSummary(PluginContext ctx, RunSummary summary)
	{
		Strings strings = ctx.Strings;
		ctx.Log.Debug("");
		ctx.Log.Debug(summary.Scraped == 1 ? strings[TextKeys.FinishFormScrapedSingle] : strings.Format(TextKeys.FinishFormScrapedPlural, summary.Scraped));
		ctx.Log.Debug(summary.Skipped == 1 ? strings[TextKeys.FinishFormSkippedSingle] : strings.Format(TextKeys.FinishFormSkippedPlural, summary.Skipped));
	}

	/// <summary>The Configure action: the settings dialog, which saves on OK.</summary>
	public void Configure()
	{
		PluginContext ctx = Context;
		Guarded(ctx, () =>
		{
			if (ConfigForm.Show(env.MainWindow, ctx) is not null)
			{
				// Applies a changed console setting now, not at the next scrape.
				CheckService(ctx);
			}
		});
	}

	private RunSummary Run(PluginContext ctx, Configuration config, ComicBook[] books)
	{
		var catalog = new ComicVineCatalog(Http(config.ApiKey), config.ToSearchSettings());
		List<IScrapeBook> targets = [.. books.Select(book => new ComicRackBook(book, env.App))];
		return ScrapeHost.Run(env.MainWindow, ctx, (form, ct) =>
		{
			var services = new ScrapeServices(catalog, new WinFormsScrapeUi(form, ctx, catalog, config), ports.Clock, ctx.Log, ctx.Settings.LoadChosenSeries(), new CoverPreferences())
			{
				CoverComparer = config.ComicVineCoverAutoAccept ? coverComparer : null,
			};
			return new ScrapeRun(services, config).RunAsync(targets, ct);
		});
	}

	private RunSummary RunCoverSearch(PluginContext ctx, Configuration config, ComicBook[] books)
	{
		VerseDbHttp api = VerseDb(config.VerseDbApiKey);
		List<IScrapeBook> targets = [.. books.Select(book => new ComicRackBook(book, env.App))];
		return ScrapeHost.Run(env.MainWindow, ctx, (form, ct) =>
			new CoverSearchRun(api, new WinFormsCoverSearchUi(form, ctx, api), ports.Clock, ctx.Log, config, config.VerseDbAutoAccept ? coverComparer : null).RunAsync(targets, ct));
	}

	/// <summary>
	/// Starts the local API service when it is not running, asks for its health, and logs the answer or the failure. All of
	/// it runs in the background, so the caller neither waits nor fails. Until the provider calls move to the service, a
	/// scrape does not depend on it.
	/// </summary>
	private void CheckService(PluginContext ctx)
	{
		if (ports.Service is not { } launch)
		{
			return;
		}
		ServiceHost host = service ??= new ServiceHost(launch, () => ctx.Settings.Load().ShowServiceConsole, ctx.Log.Debug);
		Task.Run(async () => ctx.Log.Debug(await host.CheckAsync().ConfigureAwait(false)));
	}

	/// <summary>One HttpClient for the plugin's lifetime, replaced only when the user changes the token.</summary>
	private VerseDbHttp VerseDb(string token)
	{
		if (verseDb is null || verseDbToken != token)
		{
			verseDb?.Dispose();
			verseDb = new VerseDbHttp(ports.Handler(), token, UserAgent);
			verseDbToken = token;
		}
		return verseDb;
	}

	/// <summary>One HttpClient and one throttle for the plugin's lifetime, replaced only when the user changes the key.</summary>
	private ComicVineHttp Http(string apiKey)
	{
		if (http is null || httpKey != apiKey)
		{
			http?.Dispose();
			http = new ComicVineHttp(ports.Handler(), apiKey, UserAgent, ports.Clock);
			httpKey = apiKey;
		}
		return http;
	}

	private void LogStart(PluginContext ctx)
	{
		string rule = new('-', 80);
		ctx.Log.Debug("");
		ctx.Log.Debug(rule);
		ctx.Log.Debug($"CV Scraper Version:  {Version}  (using {RuntimeInformation.FrameworkDescription})");
		ctx.Log.Debug($"Running As:          ComicRack Plugin (CR version {env.App.ProductVersion})");
		ctx.Log.Debug("Settings Folder:     " + ctx.Settings.Folder);
		ctx.Log.Debug(rule);
		ctx.Log.Debug("");
	}

	/// <summary>An error is logged and shown, never passed to ComicRack.</summary>
	private void Guarded(PluginContext ctx, Action action)
	{
		try
		{
			action();
		}
		catch (Exception error)
		{
			ctx.Log.Error(error);
			ErrorDialogs.Show(env.MainWindow, ctx, error);
		}
	}
}
