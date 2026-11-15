#if CVS_RECORDED_RESPONSES
using System.IO;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The packaged service for a run of the switch build. It is unpacked into the run's "service" folder and writes its
/// log to the run's "service-logs" folder, so a run leaves the user's own unpacked service and log files alone.
/// </summary>
internal static class RunFolderService
{
	public const string LogFolderName = "service-logs";

	public static ServiceLaunch In(string folder) => new(
		() => ServicePackage.Unpack(Path.Combine(PluginPorts.PluginFolder, ServicePackage.ZipName), Path.Combine(folder, "service")),
		Path.Combine(folder, LogFolderName));
}
#endif
