using System.IO;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;

namespace ComicApiPlugin.Plugin;

/// <summary>What the plugin reaches outside ComicRack: the local API service, the settings folder, and the clock.</summary>
/// <param name="Service">How to start the local API service, through which the plugin asks Comic Vine and VerseDB and compares covers.</param>
internal sealed record PluginPorts(ServiceLaunch Service, string SettingsFolder, IClock Clock)
{
	/// <summary>The folder ComicRack installed the plugin into, which also holds service.zip.</summary>
	public static string PluginFolder => Path.GetDirectoryName(typeof(PluginPorts).Assembly.Location)!;

	public static PluginPorts Live => new(ServiceLaunch.FromPackage(PluginFolder), SettingsStore.DefaultFolder, SystemClock.Instance);

	/// <summary>
	/// The live ports, except in a build made with -p:CvsRecordedResponses=true, where CVS_NET_RECORDED_RESPONSES or CVS_NET_LIVE_FOLDER can
	/// swap them for recorded responses (RecordedResponses.cs) or a live run (LiveFolder.cs). A normal build does not contain that code.
	/// </summary>
	public static PluginPorts FromProcess()
	{
#if CVS_RECORDED_RESPONSES
		if (RecordedResponses.FromEnvironment() is { } recorded)
		{
			return recorded;
		}
		if (LiveFolder.FromEnvironment() is { } live)
		{
			return live;
		}
#endif
		return Live;
	}
}
