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
	/// <summary>Opens the services the forms share.</summary>
	public static PluginContext Open(IPluginEnvironment env)
	{
		var log = new DebugLog();
		var settings = new SettingsStore(SettingsStore.DefaultFolder, new PluginClock(), log);
		return new PluginContext(Strings.Load(env), env.Theme, settings, log);
	}

	private sealed class PluginClock : IClock
	{
		public DateTime Now => DateTime.Now;

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
	}
}
