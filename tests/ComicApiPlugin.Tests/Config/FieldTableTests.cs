using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Tests.Config;

public sealed class FieldTableTests
{
	private static readonly string[] PythonUpdateKeys =
	[
		"updateSeries", "updateVolume", "updateNumber", "updateTitle", "updatePublished", "updateReleased",
		"updateCrossovers", "updatePublisher", "updateImprint", "updateWriter", "updatePenciller", "updateInker",
		"updateColorist", "updateLetterer", "updateCoverArtist", "updateEditor", "updateSummary", "updateCharacters",
		"updateTeams", "updateLocations", "updateWebpage",
	];

	[Test]
	public async Task Lists_exactly_the_21_Python_update_keys_in_ConfigForm_order()
	{
		string[] keys = FieldTable.Toggleable.Select(row => row.SettingsKey).ToArray();

		await Assert.That(keys.SequenceEqual(PythonUpdateKeys)).IsTrue();
		await Assert.That(keys.Distinct().Count()).IsEqualTo(21);
	}

	[Test]
	public async Task Each_row_controls_its_own_bits()
	{
		BookFields seen = BookFields.None;
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			await Assert.That(row.Controls).IsNotEqualTo(BookFields.None);
			await Assert.That(seen & row.Controls).IsEqualTo(BookFields.None);
			seen |= row.Controls;
		}

		await Assert.That(FieldTable.All).IsEqualTo(seen);
		await Assert.That(seen).IsNotEqualTo(BookFields.None);
	}

	[Test]
	public async Task Published_switches_year_month_and_day_together()
	{
		FieldTable.Row row = FieldTable.Toggleable.Single(r => r.SettingsKey == "updatePublished");

		await Assert.That(row.Controls).IsEqualTo(BookFields.Published);
	}

	[Test]
	public async Task The_table_never_controls_the_fields_set_by_other_settings()
	{
		BookFields others = BookFields.Tags | BookFields.Notes | BookFields.Rating | BookFields.IssueKey | BookFields.SeriesKey | BookFields.Format;

		await Assert.That(FieldTable.All & others).IsEqualTo(BookFields.None);
	}

	[Test]
	public async Task Every_label_key_exists_in_the_English_strings()
	{
		string xml = ReadEnglishStrings();

		await Assert.That(FieldTable.Toggleable.Count).IsEqualTo(21);
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			await Assert.That(Regex.IsMatch(xml, "Key=\"" + Regex.Escape(row.LabelTextKey) + "\"")).IsTrue();
		}
	}

	private static string ReadEnglishStrings()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			string zip = Path.Combine(dir.FullName, "src", "ComicApiPlugin", "Package", "Languages", "en.zip");
			if (File.Exists(zip))
			{
				using ZipArchive archive = ZipFile.OpenRead(zip);
				using var reader = new StreamReader(archive.GetEntry("Script.ComicVineScraper.xml")!.Open());
				return reader.ReadToEnd();
			}
		}
		throw new DirectoryNotFoundException("en.zip was not found above " + AppContext.BaseDirectory);
	}
}
