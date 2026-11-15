using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The service ships as service.zip beside the plugin's DLLs and runs from a folder outside ComicRack's Scripts folder.
/// ComicRack installs a .crplugin flat, without its subfolders, and it scans every DLL under Scripts, where the service's
/// own ComicApiPlugin.Core.dll would be taken for the plugin's.
/// </summary>
internal static class ServicePackage
{
	public const string ZipName = "service.zip";
	public const string ExeName = "ComicApiPlugin.Service.exe";

	/// <summary>Written last into an unpacked folder, and deleted first when a folder is removed. A folder without it is not whole.</summary>
	private const string CompleteMarker = ".complete";

	/// <summary>A partial folder younger than this can belong to another ComicRack that is still unpacking into it.</summary>
	private static readonly TimeSpan PartialAge = TimeSpan.FromMinutes(10);

	public static string DefaultRoot =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicApiPlugin", "service");

	/// <summary>
	/// The path of the service's exe, unpacked under <paramref name="root"/> in a folder named after the zip's content. A
	/// folder holds the completion marker only when it is whole, and a second call with a whole folder does nothing. A folder
	/// without the marker is unpacked again, unless its service is running. Folders of other versions are removed when no
	/// service runs from them.
	/// </summary>
	public static string Unpack(string zipPath, string root)
	{
		string folder = Path.Combine(root, ContentId(zipPath));
		if (!IsWhole(folder) && !IsInUse(folder))
		{
			string partial = folder + "." + Guid.NewGuid().ToString("N") + ".partial";
			ZipFile.ExtractToDirectory(zipPath, partial);
			File.WriteAllText(Path.Combine(partial, CompleteMarker), "");
			if (Directory.Exists(folder) && !IsWhole(folder))
			{
				// An interrupted unpack, which no service runs from.
				Directory.Delete(folder, recursive: true);
			}
			try
			{
				Directory.Move(partial, folder);
			}
			catch (IOException) when (IsWhole(folder))
			{
				// Another start unpacked the same zip first.
				Directory.Delete(partial, recursive: true);
			}
		}
		RemoveOthers(root, folder);
		return Path.Combine(folder, ExeName);
	}

	private static bool IsWhole(string folder) => File.Exists(Path.Combine(folder, CompleteMarker));

	/// <summary>A service runs from the folder: Windows refuses to open the exe of a running process for writing.</summary>
	private static bool IsInUse(string folder)
	{
		try
		{
			using (new FileStream(Path.Combine(folder, ExeName), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
			{
				return false;
			}
		}
		catch (FileNotFoundException)
		{
			return false;
		}
		catch (DirectoryNotFoundException)
		{
			return false;
		}
		catch (IOException)
		{
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}

	private static string ContentId(string zipPath)
	{
		using var sha = SHA256.Create();
		using FileStream zip = File.OpenRead(zipPath);
		return string.Concat(sha.ComputeHash(zip).Take(8).Select(b => b.ToString("x2")));
	}

	/// <summary>
	/// An older version, or a partial folder that a crash left. A folder that another ComicRack runs its service from, or a
	/// partial folder it may still be unpacking into, stays until a later start.
	/// </summary>
	private static void RemoveOthers(string root, string keep)
	{
		foreach (string other in Directory.GetDirectories(root).Where(other => !string.Equals(other, keep, StringComparison.OrdinalIgnoreCase)))
		{
			bool partial = other.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);
			if (partial ? DateTime.UtcNow - Directory.GetCreationTimeUtc(other) < PartialAge : IsInUse(other))
			{
				continue;
			}
			try
			{
				// The marker goes first, so a folder that is only partly deleted is never taken for whole.
				File.Delete(Path.Combine(other, CompleteMarker));
				Directory.Delete(other, recursive: true);
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
