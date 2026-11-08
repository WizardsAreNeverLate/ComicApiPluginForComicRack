using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using cYo.Projects.ComicRack.Plugins;

namespace ComicVineScraper.Plugin;

/// <summary>
/// Localize("Script.ComicVineScraper", key, englishDefault), as i18n.py does it. The English defaults and the set
/// of valid keys come from the bundled en.zip, the same file the other languages translate. An unknown key throws.
/// </summary>
public sealed class Strings
{
	public const string ResourceKey = "Script.ComicVineScraper";
	private const string DefaultsZip = "en.zip";

	private readonly IPluginEnvironment env;
	private readonly IReadOnlyDictionary<string, string> defaults;

	private Strings(IPluginEnvironment env, IReadOnlyDictionary<string, string> defaults)
	{
		this.env = env;
		this.defaults = defaults;
	}

	public static Strings Load(IPluginEnvironment env)
	{
		using ZipArchive zip = ZipFile.OpenRead(Path.Combine(env.CommandPath, DefaultsZip));
		ZipArchiveEntry entry = zip.GetEntry(ResourceKey + ".xml") ?? throw new InvalidDataException($"{DefaultsZip} has no {ResourceKey}.xml");
		using Stream stream = entry.Open();
		Dictionary<string, string> defaults = XElement.Load(stream).Element("Texts")!.Elements("Text")
			.Select(text => (Key: ((string?)text.Attribute("Key"))?.Trim() ?? "", Text: ((string?)text.Attribute("Text"))?.Trim() ?? ""))
			.Where(text => text.Key.Length > 0 && text.Text.Length > 0)
			.GroupBy(text => text.Key)
			.ToDictionary(group => group.Key, group => group.Last().Text.Replace("\n", "\n").Replace("\t", "\t"));
		return new Strings(env, defaults);
	}

	public string this[string key] => defaults.TryGetValue(key, out string? english)
		? env.Localize(ResourceKey, key, english)
		: throw new KeyNotFoundException("unrecognized i18n key: " + key);

	public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
}
