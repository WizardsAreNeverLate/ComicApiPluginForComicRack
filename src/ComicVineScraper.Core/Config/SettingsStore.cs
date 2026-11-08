using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Config;

/// <summary>
/// File I/O for the four .dat files, and the only code that touches %APPDATA%\Comic Vine Scraper. Writes are atomic
/// (a temp file in the same folder, then File.Replace) and UTF-8 with a BOM, as Python wrote them. Saving settings.dat
/// merges over the file's current map, so keys this version does not know survive and the Python plugin still reads it.
/// </summary>
public sealed class SettingsStore
{
	public const string SettingsFileName = "settings.dat";
	public const string AdvancedFileName = "advanced.dat";
	public const string GeometryFileName = "geometry.dat";
	public const string SeriesFileName = "series.dat";

	private static readonly string[] DatFiles = [SettingsFileName, AdvancedFileName, GeometryFileName, SeriesFileName];

	private readonly string folder;
	private readonly string pluginFolder;
	private readonly IClock clock;
	private readonly DebugLog? log;
	private readonly object gate = new();
	private readonly HashSet<string> unreadable = new(StringComparer.OrdinalIgnoreCase);

	public SettingsStore(string folder, string pluginFolder, IClock clock, DebugLog? log = null)
	{
		this.folder = folder;
		this.pluginFolder = pluginFolder;
		this.clock = clock;
		this.log = log;
	}

	public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comic Vine Scraper");

	public string Folder => folder;

	/// <summary>Copies the four .dat files from the plugin folder once, when settings.dat is missing (resources.py __import_legacy_settings). Never overwrites.</summary>
	public void MigrateIfNeeded()
	{
		lock (gate)
		{
			Directory.CreateDirectory(folder);
			if (File.Exists(PathOf(SettingsFileName)))
			{
				return;
			}
			foreach (string name in DatFiles)
			{
				string source = Path.Combine(pluginFolder, name);
				string target = PathOf(name);
				if (File.Exists(source) && !File.Exists(target))
				{
					File.Copy(source, target, false);
				}
			}
		}
	}

	public Configuration Load()
	{
		lock (gate)
		{
			return Configuration.FromMaps(ReadMap(SettingsFileName), ReadText(AdvancedFileName));
		}
	}

	/// <summary>
	/// Writes settings.dat and advanced.dat. Never throws for a file problem: the result carries the first error, and the debug log has
	/// the details. A file that could not be read earlier in this session is not written, so the user's other keys are not lost.
	/// </summary>
	public SaveResult Save(Configuration config)
	{
		lock (gate)
		{
			Dictionary<string, DatValue> merged = ReadMap(SettingsFileName).ToDictionary(e => e.Key, e => e.Value);
			foreach (KeyValuePair<string, DatValue> entry in config.ToSettingsMap())
			{
				merged[entry.Key] = entry.Value;
			}
			Exception? settings = WriteMap(SettingsFileName, merged);
			Exception? advanced = TryWrite(AdvancedFileName, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(config.Advanced.RawText)]);
			return new SaveResult(settings ?? advanced);
		}
	}

	public ChosenSeries LoadChosenSeries() => new(this);

	public GeometryMap LoadGeometry() => new(this);

	internal IReadOnlyDictionary<string, DatValue> ReadMap(string fileName)
	{
		byte[]? bytes = ReadBytes(fileName);
		return bytes is null ? new Dictionary<string, DatValue>() : DatFile.FromBytes(bytes);
	}

	/// <summary>Null is returned for a missing file and for an unreadable one. Only the unreadable one is remembered, so it is never written.</summary>
	private byte[]? ReadBytes(string fileName)
	{
		try
		{
			return File.ReadAllBytes(PathOf(fileName));
		}
		catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
		{
			return null;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException)
		{
			unreadable.Add(fileName);
			log?.Debug("Could not read " + fileName + ", so it will not be written this session.");
			log?.Error(error);
			return null;
		}
	}

	/// <summary>Null on success. Returns the error instead of throwing, as persist_map returned False.</summary>
	internal Exception? WriteMap(string fileName, IReadOnlyDictionary<string, DatValue> map) => TryWrite(fileName, DatFile.ToBytes(map, clock.Now));

	private Exception? TryWrite(string fileName, byte[] bytes)
	{
		if (unreadable.Contains(fileName))
		{
			var skipped = new IOException(fileName + " could not be read earlier in this session, so it is not overwritten.");
			log?.Debug("Did not write " + fileName + ": " + skipped.Message);
			return skipped;
		}
		try
		{
			Directory.CreateDirectory(folder);
			WriteAtomically(PathOf(fileName), bytes);
			return null;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException)
		{
			log?.Debug("Could not write " + fileName + ".");
			log?.Error(error);
			return error;
		}
	}

	internal void Locked(Action action)
	{
		lock (gate)
		{
			action();
		}
	}

	private string ReadText(string fileName)
	{
		byte[]? bytes = ReadBytes(fileName);
		if (bytes is null)
		{
			return "";
		}
		byte[] bom = Encoding.UTF8.GetPreamble();
		int start = bytes.Length >= bom.Length && bytes.Take(bom.Length).SequenceEqual(bom) ? bom.Length : 0;
		return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
	}
	private static void WriteAtomically(string path, byte[] bytes)
	{
		string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllBytes(temp, bytes);
			if (File.Exists(path))
			{
				File.Replace(temp, path, null);
			}
			else
			{
				File.Move(temp, path);
			}
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}
	}

	private string PathOf(string fileName) => Path.Combine(folder, fileName);
}
