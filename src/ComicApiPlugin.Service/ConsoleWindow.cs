using System;
using System.IO;

namespace ComicApiPlugin.Service;

internal static class ConsoleWindow
{
	/// <summary>
	/// A writer to the console window, or null when the process has none (the plugin hides it by default). When the plugin
	/// starts the service, stdout is a pipe, so the window is opened by its device name.
	/// </summary>
	public static TextWriter? TryOpen()
	{
		if (!Console.IsOutputRedirected)
		{
			return Console.Out;
		}
		try
		{
			return new StreamWriter(new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			return null;
		}
	}
}
