using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ComicVineScraper.Engine;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicVineScraper.Plugin;

/// <summary>ComicRack creates one instance per process and calls both commands on the UI thread.</summary>
public sealed class ComicVineScraperPlugin(IPluginEnvironment env)
{
	// Not the Python plugin's "comic-vine-scraper", so both can be installed for the end-to-end comparison.
	private const string Key = "comic-vine-scraper-net";

	private bool running;

	/// <summary>
	/// Blocks until the run ends, as both hooks require: the Editor hook passes a copy of the book and reads it
	/// back the moment this returns. A second call while a run is in progress returns at once.
	/// </summary>
	[PluginCommand(PluginEngine.ScriptTypeBooks + "," + PluginEngine.ScriptTypeEditor, Key = Key, Name = "Comic Vine Scraper", Image = "comicvinescraper.png")]
	public void Scrape(ComicBook[] books)
	{
		if (running || books.Length == 0)
		{
			return;
		}
		running = true;
		try
		{
			PluginContext ctx = PluginContext.Open(env);
			List<IScrapeBook> targets = books.Select(book => (IScrapeBook)new ComicRackBook(book, env.App)).ToList();
			ScrapeHost.Run(env.MainWindow, ctx, (form, ct) => PrototypeRun.RunAsync(targets, form, ct));
		}
		finally
		{
			running = false;
			(env.MainWindow as Form)?.Activate();
		}
	}

	/// <summary>The Configure action: the settings dialog, which saves on OK. An error is logged and shown, never passed to ComicRack.</summary>
	[PluginCommand(PluginEngine.ScriptTypeConfig, Key = Key)]
	public void Configure()
	{
		PluginContext ctx = PluginContext.Open(env);
		try
		{
			ConfigForm.Show(env.MainWindow, ctx);
		}
		catch (Exception error)
		{
			ErrorDialogs.Show(env.MainWindow, ctx, error);
		}
	}
}
