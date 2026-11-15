using System.Collections.Generic;
using System.Threading.Tasks;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Tests.Config;

public sealed class CoverAutoAcceptSettingsTests
{
	private static Configuration Blank => new() { Advanced = AdvancedSettings.Parse("") };

	[Test]
	public async Task Auto_accept_is_off_and_compares_five_results_by_default()
	{
		Configuration loaded = Configuration.FromMaps(new Dictionary<string, DatValue>(), "");

		await Assert.That(loaded.VerseDbAutoAccept).IsFalse();
		await Assert.That(loaded.ComicVineCoverAutoAccept).IsFalse();
		await Assert.That(loaded.ComicVineCoverCompareCount).IsEqualTo(5);
	}

	[Test]
	public async Task The_settings_round_trip_through_the_settings_map()
	{
		Configuration saved = Blank with { VerseDbAutoAccept = true, ComicVineCoverAutoAccept = true, ComicVineCoverCompareCount = 12 };

		Configuration loaded = Configuration.FromMaps(saved.ToSettingsMap(), "");

		await Assert.That(loaded.VerseDbAutoAccept).IsTrue();
		await Assert.That(loaded.ComicVineCoverAutoAccept).IsTrue();
		await Assert.That(loaded.ComicVineCoverCompareCount).IsEqualTo(12);
	}

	[Test]
	public async Task The_settings_use_their_own_keys_and_leave_the_Python_keys_alone()
	{
		IReadOnlyDictionary<string, DatValue> map = (Blank with { VerseDbAutoAccept = true, ComicVineCoverCompareCount = 7 }).ToSettingsMap();

		await Assert.That(map["verseDbAutoAccept"]).IsEqualTo(new DatValue.Bool(true));
		await Assert.That(map["comicVineCoverAutoAccept"]).IsEqualTo(new DatValue.Bool(false));
		await Assert.That(map["comicVineCoverCompareCount"]).IsEqualTo(new DatValue.Int(7));
		await Assert.That(map["autochooseSeries"]).IsEqualTo(new DatValue.Bool(false));
	}

	[Test]
	[Arguments(0, 1)]
	[Arguments(-4, 1)]
	[Arguments(1, 1)]
	[Arguments(20, 20)]
	[Arguments(21, 20)]
	[Arguments(5000, 20)]
	public async Task The_count_is_clamped_whether_set_in_code_or_read_from_the_file(int value, int expected)
	{
		Configuration fromCode = Blank with { ComicVineCoverCompareCount = value };
		Configuration fromFile = Configuration.FromMaps(new Dictionary<string, DatValue> { ["comicVineCoverCompareCount"] = new DatValue.Int(value) }, "");

		await Assert.That(fromCode.ComicVineCoverCompareCount).IsEqualTo(expected);
		await Assert.That(fromFile.ComicVineCoverCompareCount).IsEqualTo(expected);
	}

	[Test]
	public async Task A_count_that_is_not_a_number_keeps_the_default()
	{
		Configuration loaded = Configuration.FromMaps(new Dictionary<string, DatValue> { ["comicVineCoverCompareCount"] = new DatValue.Text("many") }, "");

		await Assert.That(loaded.ComicVineCoverCompareCount).IsEqualTo(5);
	}

	[Test]
	public async Task The_debug_log_line_shows_the_settings()
	{
		string text = (Blank with { ComicVineCoverAutoAccept = true }).ToString();

		await Assert.That(text).Contains("ComicVineCoverAutoAccept = True");
		await Assert.That(text).Contains("ComicVineCoverCompareCount = 5");
		await Assert.That(text).Contains("VerseDbAutoAccept = False");
	}
}
