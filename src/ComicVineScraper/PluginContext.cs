using System;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Config;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Engine;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Plugin;

/// <summary>The services the forms share. The plugin opens one for its lifetime, so every command and form sees one store and one log.</summary>
public sealed record PluginContext(Strings Strings, IThemePlugin? Theme, SettingsStore Settings, DebugLog Log)
{
	public static PluginContext Open(IPluginEnvironment env, string settingsFolder, IClock clock)
	{
		var log = new DebugLog();
		return new PluginContext(Strings.Load(env), env.Theme, new SettingsStore(settingsFolder, clock, log), log);
	}
}

internal sealed class SystemClock : IClock
{
	public static readonly SystemClock Instance = new();

	public DateTime Now => DateTime.Now;

	public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
