using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Plugin;

/// <summary>Per-invocation services the forms share. Phase 3c adds the settings store and the debug log.</summary>
public sealed record PluginContext(Strings Strings, IThemePlugin? Theme)
{
	public static PluginContext Open(IPluginEnvironment env) => new(Strings.Load(env), env.Theme);
}
