using System;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Config;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Engine;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Plugin;

/// <summary>
/// The services the forms share. The plugin opens one for its lifetime, so every command and form sees one store and one log.
/// CurrentTheme is read each time a form applies its theme, because ComicRack can change IPluginEnvironment.Theme while it runs.
/// Strings holds the environment itself and asks it to localize on every lookup, and a language change needs a restart anyway.
/// </summary>
public sealed record PluginContext(Strings Strings, Func<IThemePlugin?> CurrentTheme, SettingsStore Settings, DebugLog Log)
{
	public static PluginContext Open(IPluginEnvironment env, string settingsFolder, IClock clock)
	{
		var log = new DebugLog();
		return new PluginContext(Strings.Load(env), () => env.Theme, new SettingsStore(settingsFolder, clock, log), log);
	}
}

internal sealed class SystemClock : IClock
{
	public static readonly SystemClock Instance = new();

	public DateTime Now => DateTime.Now;

	public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
