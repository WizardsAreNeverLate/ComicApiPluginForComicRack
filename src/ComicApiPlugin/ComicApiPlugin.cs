using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicApiPlugin.Plugin;

/// <summary>ComicRack creates one instance per process and calls both commands on the UI thread.</summary>
public sealed class ComicApiPlugin
{
	// Not the Python plugin's "comic-vine-scraper", so both can be installed for the end-to-end comparison.
	private const string Key = "comic-api-net";

	private static readonly string Version = typeof(ComicApiPlugin).Assembly.GetName().Version.ToString(3);
	private static readonly string UserAgent = $"ComicApiPlugin/{Version} (https://github.com/cbanack/comic-vine-scraper/)";

	private readonly IPluginEnvironment env;
	private readonly PluginPorts ports;
	private PluginContext? context;
	private ComicVineHttp? http;
	private string? httpKey;
	private bool running;

	public ComicApiPlugin(IPluginEnvironment env) : this(env, PluginPorts.FromProcess())
	{
	}

	internal ComicApiPlugin(IPluginEnvironment env, PluginPorts ports)
	{
		this.env = env;
		this.ports = ports;
	}

	/// <summary>One store and one log for the plugin's lifetime, so two commands never write the settings over each other.</summary>
	internal PluginContext Context => context ??= PluginContext.Open(env, ports.SettingsFolder, ports.Clock);

	/// <summary>
	/// scrapeengine.py's scrape. Blocks until the run ends, as both hooks require: the Editor hook passes a copy of the book
	/// and reads it back the moment this returns. A second call while a run is in progress returns at once.
	/// </summary>
	[PluginCommand(PluginEngine.ScriptTypeBooks + "," + PluginEngine.ScriptTypeEditor, Key = Key, Name = "Comic Vine Scraper", Image = "comicvinescraper.png")]
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
		running = true;
		try
		{
			LogStart(ctx);
			if (books.Length == 0)
			{
				// Python still logged the header, said it finished and showed the summary for an empty selection.
				ctx.Log.Debug("Scraper terminated normally (scraped 0, skipped 0).");
				return;
			}
			config = ctx.Settings.Load();
			if (string.IsNullOrWhiteSpace(config.ApiKey))
			{
				ctx.Log.Debug("API key not available.  Showing config dialog.");
				ConfigForm.Show(env.MainWindow, ctx);
				config = ctx.Settings.Load();
				if (string.IsNullOrWhiteSpace(config.ApiKey))
				{
					ctx.Log.Debug("API key still not available.  Aborting.");
					return;
				}
			}
			if (config.Advanced.WelcomeDialog)
			{
				bool start = WelcomeForm.Show(env.MainWindow, ctx, books.Length);
				config = ctx.Settings.Load();
				if (!start)
				{
					ctx.Log.Debug("Cancelled!");
					return;
				}
			}
			summary = Run(ctx, config, books);
		}
		catch (ScrapeFailedException failure)
		{
			// The engine logged the cause.
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
				// Python's finally: the summary follows every run, including one that stopped early or failed.
				if (config.SummaryDialog)
				{
					Guarded(ctx, () => FinishForm.Show(env.MainWindow, ctx, summary));
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

	/// <summary>The Configure action: the settings dialog, which saves on OK.</summary>
	[PluginCommand(PluginEngine.ScriptTypeConfig, Key = Key)]
	public void Configure()
	{
		PluginContext ctx = Context;
		Guarded(ctx, () => ConfigForm.Show(env.MainWindow, ctx));
	}

	private RunSummary Run(PluginContext ctx, Configuration config, ComicBook[] books)
	{
		var catalog = new ComicVineCatalog(Http(config.ApiKey), config.ToSearchSettings());
		List<IScrapeBook> targets = [.. books.Select(book => new ComicRackBook(book, env.App))];
		return ScrapeHost.Run(env.MainWindow, ctx, (form, ct) =>
		{
			var services = new ScrapeServices(catalog, new WinFormsScrapeUi(form, ctx, catalog, config), ports.Clock, ctx.Log, ctx.Settings.LoadChosenSeries(), new CoverPreferences());
			return new ScrapeRun(services, config).RunAsync(targets, ct);
		});
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
