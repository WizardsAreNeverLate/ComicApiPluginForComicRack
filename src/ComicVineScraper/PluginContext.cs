using System;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Config;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Engine;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Plugin;

/// <summary>Per-invocation services the forms share.</summary>
public sealed record PluginContext(Strings Strings, IThemePlugin? Theme, SettingsStore Settings, DebugLog Log)
{
	/// <summary>Opens the services and, as the Python plugin did at start-up, copies the settings from the plugin folder when the profile has none.</summary>
	public static PluginContext Open(IPluginEnvironment env)
	{
		var log = new DebugLog();
		var settings = new SettingsStore(SettingsStore.DefaultFolder, env.CommandPath, new PluginClock(), log);
		settings.MigrateIfNeeded();
		return new PluginContext(Strings.Load(env), env.Theme, settings, log);
	}

	private sealed class PluginClock : IClock
	{
		public DateTime Now => DateTime.Now;

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
	}
}
