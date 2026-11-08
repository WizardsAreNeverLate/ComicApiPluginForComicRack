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

	/// <summary>Writes settings.dat and advanced.dat.</summary>
	public void Save(Configuration config)
	{
		lock (gate)
		{
			Dictionary<string, DatValue> merged = ReadMap(SettingsFileName).ToDictionary(e => e.Key, e => e.Value);
			foreach (KeyValuePair<string, DatValue> entry in config.ToSettingsMap())
			{
				merged[entry.Key] = entry.Value;
			}
			WriteMap(SettingsFileName, merged);
			WriteText(AdvancedFileName, config.Advanced.RawText);
		}
	}

	public ChosenSeries LoadChosenSeries() => new(this);

	public GeometryMap LoadGeometry() => new(this);

	internal IReadOnlyDictionary<string, DatValue> ReadMap(string fileName)
	{
		try
		{
			string path = PathOf(fileName);
			return File.Exists(path) ? DatFile.FromBytes(File.ReadAllBytes(path)) : new Dictionary<string, DatValue>();
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException)
		{
			log?.Error(error);
			return new Dictionary<string, DatValue>();
		}
	}

	internal void WriteMap(string fileName, IReadOnlyDictionary<string, DatValue> map)
	{
		Directory.CreateDirectory(folder);
		WriteAtomically(PathOf(fileName), DatFile.ToBytes(map, clock.Now));
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
		try
		{
			string path = PathOf(fileName);
			if (!File.Exists(path))
			{
				return "";
			}
			byte[] bytes = File.ReadAllBytes(path);
			byte[] bom = Encoding.UTF8.GetPreamble();
			int start = bytes.Length >= bom.Length && bytes.Take(bom.Length).SequenceEqual(bom) ? bom.Length : 0;
			return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException)
		{
			log?.Error(error);
			return "";
		}
	}

	private void WriteText(string fileName, string text)
	{
		Directory.CreateDirectory(folder);
		WriteAtomically(PathOf(fileName), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]);
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
