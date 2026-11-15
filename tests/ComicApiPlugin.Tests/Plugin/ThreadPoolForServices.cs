using System.Threading;
using static TUnit.Core.HookType;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// Lets the thread pool grow at once. Each test that starts the local API service holds three pool threads: one waits
/// for the service to report ready, and .NET Framework reads each of the service's two output pipes on a blocked thread.
/// Past its minimum, the pool adds about one thread each second, so with fifty such tests at once the thread that would
/// read a ready line can come 20 seconds late. ComicRack starts one service, so the plugin itself needs no such setting.
/// </summary>
public static class ThreadPoolForServices
{
	[Before(Assembly)]
	public static void LetThePoolGrow() => ThreadPool.SetMinThreads(400, 400);
}
