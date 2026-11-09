using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin;

public sealed class TextKeysTests
{
	[Test]
	public async Task TextKeys_has_exactly_the_keys_of_en_zip()
	{
		string[] declared = typeof(TextKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToArray();

		await Assert.That(declared.OrderBy(k => k, StringComparer.Ordinal)).IsEquivalentTo(Keys("en.zip").OrderBy(k => k, StringComparer.Ordinal));
		await Assert.That(declared.Length).IsEqualTo(120);
	}

	[Test]
	public async Task Each_constant_is_named_after_its_value()
	{
		foreach (FieldInfo field in typeof(TextKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			await Assert.That((string)field.GetRawConstantValue()!).IsEqualTo(field.Name);
		}
	}

	[Test]
	public async Task Every_key_that_Core_names_exists_in_en_zip()
	{
		HashSet<string> keys = Keys("en.zip");
		string[] used =
		[
			.. FieldTable.Toggleable.Select(row => row.LabelTextKey),
			.. typeof(ErrorPrompt).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.Name != nameof(ErrorPrompt.DatabaseDisplayName)).Select(f => (string)f.GetRawConstantValue()!),
		];

		await Assert.That(used.Where(key => !keys.Contains(key))).IsEmpty();
		await Assert.That(used.Length).IsGreaterThan(25);
	}
	[Test]
	[Arguments("de.zip")]
	[Arguments("en.zip")]
	[Arguments("fr.zip")]
	[Arguments("it.zip")]
	[Arguments("nl.zip")]
	[Arguments("nl-BE.zip")]
	[Arguments("pl.zip")]
	[Arguments("pt-BR.zip")]
	public async Task Every_language_names_and_describes_the_dot_net_command(string zip)
	{
		using ZipArchive archive = ZipFile.OpenRead(Path.Combine(LanguagePack.Folder, zip));
		ZipArchiveEntry entry = archive.GetEntry("comic-api-net.xml")!;
		await Assert.That(entry).IsNotNull();
		XElement table = XElement.Load(entry.Open());
		Dictionary<string, string> texts = table.Element("Texts")!.Elements("Text").ToDictionary(t => (string)t.Attribute("Key")!, t => (string)t.Attribute("Text")!);

		await Assert.That((string)table.Attribute("Name")!).IsEqualTo("comic-api-net");
		await Assert.That(texts["Name"]).Contains(".NET");
		await Assert.That(texts["Description"].Length).IsGreaterThan(10);
	}

	private static HashSet<string> Keys(string zip)
	{
		using ZipArchive archive = ZipFile.OpenRead(Path.Combine(LanguagePack.Folder, zip));
		using Stream stream = archive.GetEntry("Script.ComicVineScraper.xml")!.Open();
		return XElement.Load(stream).Element("Texts")!.Elements("Text").Select(t => ((string?)t.Attribute("Key"))!.Trim()).ToHashSet();
	}
}

/// <summary>Where the bundled language zips are, found from the test binaries.</summary>
internal static class LanguagePack
{
	public static string Folder { get; } = Find();

	private static string Find()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, "src", "ComicApiPlugin", "Package", "Languages");
			if (File.Exists(Path.Combine(candidate, "en.zip")))
			{
				return candidate;
			}
		}
		throw new DirectoryNotFoundException("src/ComicApiPlugin/Package/Languages/en.zip was not found above " + AppContext.BaseDirectory);
	}
}
