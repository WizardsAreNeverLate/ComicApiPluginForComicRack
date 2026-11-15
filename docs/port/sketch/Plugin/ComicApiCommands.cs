// ComicApiPlugin (the plugin assembly): references ComicRack.Plugins, ComicRack.Engine, WinForms, and Core.
// ComicRack creates one instance of this DotNetPlugin class per process, passes IPluginEnvironment to OnLoad,
// and calls every hook on the UI thread.

namespace ComicApiPlugin.Plugin;

public sealed class ComicApiCommands : DotNetPlugin, IBooks, IEditor, IConfig
{
    // Not the Python plugin's "comic-vine-scraper", so both can be installed at once (issue #2).
    public override string Key { get; set; } = "comic-api-net";
    public override string Name { get; set; } = "Comic API Plugin";
    public override string Image { get; set; } = "comicvinescraper.png";

    private IPluginEnvironment env;   // set in OnLoad
    protected override void OnLoad() => env = ComicRack;

    void IBooks.Execute(ComicBook[] books) => Scrape(books);
    void IEditor.Execute(ComicBook[] books) => Scrape(books);
    void IConfig.Execute() => Configure();

    private ComicVineHttp? http;   // plugin lifetime: one HttpClient, one throttle, created on first scrape
    private bool running;          // re-entrancy guard; the modal host makes a second click unlikely, not impossible

    /// <summary>
    /// Blocks until the run ends, as both hooks require: ComicBookDialog (the Editor hook) passes a copy
    /// of the book and reads it back the moment this returns. The UI stays live because the run happens
    /// inside ScrapeHost's modal message loop.
    /// </summary>
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

    public void Configure() => throw new NotImplementedException();
    // TODO: ctx = PluginContext.Open(env); ConfigDialog.Show(env.MainWindow, ctx, ctx.Store.Load()) saves on OK.
}

/// <summary>Per-invocation services the forms share. Not a container: four fields, built in one place.</summary>
public sealed record PluginContext(Strings Strings, SettingsStore Store, DebugLog Log, IThemePlugin? Theme)
{
    public static PluginContext Open(IPluginEnvironment env) => throw new NotImplementedException();
}
