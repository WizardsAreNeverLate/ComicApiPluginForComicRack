// i18n and user-facing errors. Core has no user-visible strings: it returns typed results and
// ComicVineException kinds, and this layer turns them into localized text.

namespace ComicVineScraper.Plugin;

/// <summary>
/// Localize("Script.ComicVineScraper", key, englishDefault). English defaults come from the bundled en.zip
/// (Script.ComicVineScraper.xml), the same file the other languages translate, so defaults are never
/// duplicated in C#. Literal "\n" and "\t" are unescaped. An unknown key throws, as in Python.
/// </summary>
public sealed class Strings
{
    public const string ResourceKey = "Script.ComicVineScraper";

    public static Strings Load(IPluginEnvironment env) => throw new NotImplementedException(); // reads <CommandPath>\en.zip with ZipArchive
    public string this[string key] => throw new NotImplementedException();
    public string Format(string key, params object[] args) => throw new NotImplementedException();
}

/// <summary>Key names, one constant per en.zip key. A test asserts this set equals the keys in en.zip.</summary>
public static class TextKeys
{
    public const string SearchProgbarText = nameof(SearchProgbarText);
    public const string IssuesProgbarText = nameof(IssuesProgbarText);
    public const string NoIssuesAvailableText = nameof(NoIssuesAvailableText);
    public const string NoIssuesAvailableTitle = nameof(NoIssuesAvailableTitle);
    // ... the other 116
}

/// <summary>Ports log.handle_error. Called by the Scrape and Configure commands at their catch, never via Application.ThreadException.</summary>
internal static class ErrorDialogs
{
    /// <summary>
    /// ComicVineException InvalidApiKey -> invalid-key warning (now reachable on HTTP 401: deviations #4);
    /// RateLimited -> rate-limit warning; Network -> generic connection warning;
    /// anything else -> "unexpected error, save log?" and, on Yes, LogFile.SaveWithDialog.
    /// </summary>
    public static void Show(IWin32Window owner, PluginContext ctx, Exception error) => throw new NotImplementedException();
}

internal static class LogFile
{
    /// <summary>SaveFileDialog with cvs-debug-log-yyyy-MM-dd.txt; confirms success or failure.</summary>
    public static void SaveWithDialog(IWin32Window owner, PluginContext ctx) => throw new NotImplementedException();
}
