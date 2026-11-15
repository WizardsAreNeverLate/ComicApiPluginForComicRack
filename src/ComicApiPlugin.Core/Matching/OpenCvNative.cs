using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ComicApiPlugin.Matching;

/// <summary>The OpenCV native library is missing or cannot be loaded. The cover comparison is unavailable; nothing else is affected.</summary>
public sealed class OpenCvUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Loads OpenCvSharpExtern.dll by full path before OpenCvSharp first uses it. ComicRack loads the plugin from its
/// Scripts folder, outside the default DLL search path, so OpenCvSharp's own lookup by name can miss it. A library that is
/// already loaded satisfies the later by-name DllImport.
/// </summary>
internal static class OpenCvNative
{
	private const string LibraryName = "OpenCvSharpExtern.dll";
	private const uint LoadWithAlteredSearchPath = 0x8;
	private static readonly object Gate = new();
	private static bool loaded;

	public static void EnsureLoaded()
	{
		lock (Gate)
		{
			if (loaded)
			{
				return;
			}
			if (!Environment.Is64BitProcess)
			{
				throw new OpenCvUnavailableException("The cover comparison needs a 64-bit process.");
			}
			string[] searched = [.. Candidates()];
			foreach (string path in searched)
			{
				if (!File.Exists(path))
				{
					continue;
				}
				if (LoadLibraryEx(path, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
				{
					throw new OpenCvUnavailableException($"Windows could not load {path}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
				}
				loaded = true;
				return;
			}
			throw new OpenCvUnavailableException($"{LibraryName} was not found. Looked in: {string.Join("; ", searched)}");
		}
	}

	private static System.Collections.Generic.IEnumerable<string> Candidates()
	{
		foreach (Assembly assembly in new[] { typeof(OpenCvSharp.Mat).Assembly, typeof(OpenCvNative).Assembly })
		{
			if (!string.IsNullOrEmpty(assembly.Location) && Path.GetDirectoryName(assembly.Location) is { } folder)
			{
				yield return Path.Combine(folder, LibraryName);
				yield return Path.Combine(folder, "dll", "x64", LibraryName);
			}
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);
}
