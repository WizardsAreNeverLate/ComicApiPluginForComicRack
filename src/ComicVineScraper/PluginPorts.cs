using System;
using System.Net.Http;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>What the plugin reaches outside ComicRack: Comic Vine's transport, the settings folder, and the clock.</summary>
/// <param name="Handler">Makes the handler for each ComicVineHttp, which disposes it.</param>
internal sealed record PluginPorts(Func<HttpMessageHandler> Handler, string SettingsFolder, IClock Clock)
{
	public static PluginPorts Live => new(() => new HttpClientHandler(), SettingsStore.DefaultFolder, SystemClock.Instance);

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
