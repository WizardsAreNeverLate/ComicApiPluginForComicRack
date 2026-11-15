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

	public static string DefaultRoot =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicApiPlugin", "service");

	/// <summary>
	/// The path of the service's exe, unpacked under <paramref name="root"/> in a folder named after the zip's content. A
	/// folder is moved into place only when it is complete, so one that exists is whole, and a second call does nothing.
	/// Folders of other versions are removed.
	/// </summary>
	public static string Unpack(string zipPath, string root)
	{
		string folder = Path.Combine(root, ContentId(zipPath));
		if (!Directory.Exists(folder))
		{
			string partial = folder + "." + Guid.NewGuid().ToString("N") + ".partial";
			ZipFile.ExtractToDirectory(zipPath, partial);
			try
			{
				Directory.Move(partial, folder);
			}
			catch (IOException) when (Directory.Exists(folder))
			{
				// Another start unpacked the same zip first.
				Directory.Delete(partial, recursive: true);
			}
		}
		RemoveOthers(root, folder);
		return Path.Combine(folder, ExeName);
	}

	private static string ContentId(string zipPath)
	{
		using var sha = SHA256.Create();
		using FileStream zip = File.OpenRead(zipPath);
		return string.Concat(sha.ComputeHash(zip).Take(8).Select(b => b.ToString("x2")));
	}

	/// <summary>An older version, or a partial folder that a crash left. One that is still in use stays until a later start.</summary>
	private static void RemoveOthers(string root, string keep)
	{
		foreach (string other in Directory.GetDirectories(root).Where(other => !string.Equals(other, keep, StringComparison.OrdinalIgnoreCase)))
		{
			try
			{
				Directory.Delete(other, recursive: true);
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
