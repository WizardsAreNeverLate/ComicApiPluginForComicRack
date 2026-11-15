using System;
using System.IO;
using System.Net.Http;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;

namespace ComicApiPlugin.Plugin;

/// <summary>What the plugin reaches outside ComicRack: the providers' transport, the settings folder, and the clock.</summary>
/// <param name="Handler">Makes the handler for each in-process ComicVineHttp and VerseDbHttp. Its owner disposes it.</param>
internal sealed record PluginPorts(Func<HttpMessageHandler> Handler, string SettingsFolder, IClock Clock)
{
	/// <summary>
	/// How to start the local API service, through which the plugin then asks Comic Vine and VerseDB and compares covers.
	/// Null runs without the service: Comic Vine and VerseDB are then asked in this process through <see cref="Handler"/>,
	/// and no covers are compared. The recorded-response tests and tools need that until their responses are served
	/// through the service.
	/// </summary>
	public ServiceLaunch? Service { get; init; }

	public static PluginPorts Live => new(() => new HttpClientHandler(), SettingsStore.DefaultFolder, SystemClock.Instance)
	{
		Service = ServiceLaunch.FromPackage(Path.GetDirectoryName(typeof(PluginPorts).Assembly.Location)!),
	};

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
