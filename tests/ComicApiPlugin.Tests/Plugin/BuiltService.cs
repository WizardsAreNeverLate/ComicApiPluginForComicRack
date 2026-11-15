using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>The service as the build packaged it: service.zip from the artifacts folder, unpacked the way the plugin unpacks it.</summary>
internal static class BuiltService
{
	private static readonly Lazy<string> Unpacked = new(() => ServicePackage.Unpack(Zip, NewFolder("unpacked")));

	/// <summary>artifacts\&lt;Configuration&gt;\Comic API Plugin\service.zip. The tests run from tests\...\bin\&lt;Configuration&gt;\net48.</summary>
	public static string Zip
	{
		get
		{
			string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\')).Parent!.Name;
			for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
			{
				string candidate = Path.Combine(dir.FullName, "artifacts", configuration, "Comic API Plugin", ServicePackage.ZipName);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			throw new FileNotFoundException($"artifacts/{configuration}/Comic API Plugin/{ServicePackage.ZipName} was not found above {AppContext.BaseDirectory}");
		}
	}

	public static string Exe => Unpacked.Value;

	public static string NewFolder(string name) => Path.Combine(Path.GetTempPath(), "comicapi-plugin-tests", name + "-" + Guid.NewGuid().ToString("N"));

	/// <summary>A launch of the real service with a log folder of its own, which also tells its process apart from other tests'.</summary>
	public static ServiceLaunch Launch() => new(() => Exe, NewFolder("logs"));

	/// <summary>A script that stands in for a service that fails. CreateProcess runs a .cmd file like an exe.</summary>
	public static ServiceLaunch Script(params string[] lines)
	{
		string folder = NewFolder("fake");
		Directory.CreateDirectory(folder);
		string script = Path.Combine(folder, "service.cmd");
		File.WriteAllLines(script, lines);
		return new ServiceLaunch(() => script, Path.Combine(folder, "logs"));
	}

	/// <summary>The command lines of the running service processes that log to <paramref name="logFolder"/>.</summary>
	public static IReadOnlyList<string> CommandLines(string logFolder)
	{
		using var search = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE Name = '{ServicePackage.ExeName}'");
		using ManagementObjectCollection found = search.Get();
		return [.. found.Cast<ManagementBaseObject>().Select(process => process["CommandLine"] as string ?? "").Where(line => line.Contains(logFolder))];
	}

	public static string ReadLogs(string logFolder) =>
		string.Concat(Directory.GetFiles(logFolder, "service-*.log").Select(file =>
		{
			using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}));
}
