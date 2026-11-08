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

	[PluginCommand(PluginEngine.ScriptTypeConfig, Key = Key)]
	public void Configure()
	{
		PluginContext ctx = PluginContext.Open(env);
		MessageBox.Show(env.MainWindow, "The settings dialog arrives in phase 4.", ctx.Strings["ConfigFormTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
	}
}
