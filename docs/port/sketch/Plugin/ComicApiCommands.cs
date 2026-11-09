// ComicApiPlugin (the plugin assembly): references ComicRack.Plugins, ComicRack.Engine, WinForms, and Core.
// ComicRack's DotNetCommand creates one instance of this class per process and calls both commands on the UI thread.

namespace ComicApiPlugin.Plugin;

public sealed class ComicApiCommands(IPluginEnvironment env)
{
    // Same key as the Python script's "#@Key comic-vine-scraper": the ConfigScript pairs with the Books
    // command, and comic-vine-scraper.xml in the language zips names it. If both plugins are installed,
    // ComicRack keeps whichever loads first (PluginEngine.Initialize skips a duplicate Key).
    private const string Key = "comic-vine-scraper";

    private ComicVineHttp? http;   // plugin lifetime: one HttpClient, one throttle, created on first scrape
    private bool running;          // re-entrancy guard; the modal host makes a second click unlikely, not impossible

    /// <summary>
    /// Blocks until the run ends, as both hooks require: ComicBookDialog (the Editor hook) passes a copy
    /// of the book and reads it back the moment this returns. The UI stays live because the run happens
    /// inside ScrapeHost's modal message loop.
    /// </summary>
    [PluginCommand(PluginEngine.ScriptTypeBooks + "," + PluginEngine.ScriptTypeEditor,
        Key = Key, Name = "Comic Vine Scraper", Image = "comicvinescraper.png")]
    public void Scrape(ComicBook[] books) => throw new NotImplementedException();
    // TODO pseudocode:
    //   if running || books.Length == 0: return
    //   running = true
    //   var ctx = PluginContext.Open(env)           // Strings (en.zip defaults + env.Localize), SettingsStore (migrated), DebugLog
    //   try
    //     config = ctx.Store.Load()
    //     if config.ApiKey == "": config = ConfigDialog.Show(env.MainWindow, ctx, config); if null: return
    //     if config.Advanced.WelcomeDialog:
    //        switch WelcomeDialog.Show(env.MainWindow, ctx, books.Length)   // Start | Settings (opens ConfigDialog, then re-asks) | Cancel
    //        config = ctx.Store.Load()
    //     http ??= new ComicVineHttp(new HttpClientHandler(), config.ApiKey, UserAgent, SystemClock.Instance)  // rebuilt if the key changed
    //     summary = ScrapeHost.Run(env.MainWindow, ctx, (host, ct) =>
    //        new ScrapeRun(new ScrapeServices(
    //              ComicVine: new ComicVineCatalog(http, config.ToSearchSettings()),
    //              Ui: new WinFormsScrapeUi(host, ctx, catalog, config),
    //              Clock: SystemClock.Instance, Log: ctx.Log,
    //              Chosen: ctx.Store.LoadChosenSeries(),
    //              Parser: new FileNameParser(config.Advanced.AltSearchRegex)), config)
    //          .RunAsync(books.Select(b => new ComicRackBook(b, env.App)).ToList(), ct))
    //     if config.SummaryDialog: FinishDialog.Show(env.MainWindow, ctx, summary)
    //   catch (Exception e) when (e is not OperationCanceledException)
    //     ctx.Log.Error(e); ErrorDialogs.Show(env.MainWindow, ctx, e)
    //   finally
    //     running = false; (env.MainWindow as Form)?.Activate()  // issue 159

    [PluginCommand(PluginEngine.ScriptTypeConfig, Key = Key, Name = "Comic Vine Scraper")]
    public void Configure() => throw new NotImplementedException();
    // TODO: ctx = PluginContext.Open(env); ConfigDialog.Show(env.MainWindow, ctx, ctx.Store.Load()) saves on OK.
}

/// <summary>Per-invocation services the forms share. Not a container: four fields, built in one place.</summary>
public sealed record PluginContext(Strings Strings, SettingsStore Store, DebugLog Log, IThemePlugin? Theme)
{
    public static PluginContext Open(IPluginEnvironment env) => throw new NotImplementedException();
}
