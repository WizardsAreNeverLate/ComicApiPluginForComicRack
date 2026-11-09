using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.Config;

/// <summary>Every test runs in its own temp folder. The real %APPDATA%\Comic Vine Scraper is never touched.</summary>
public sealed class SettingsStoreTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "cvs-store-" + Guid.NewGuid().ToString("N"));
	private string Profile => Path.Combine(root, "profile");

	public void Dispose() => Directory.Delete(root, true);

	private SettingsStore Store() => new(Profile, new FixedClock());

	[Test]
	public async Task The_default_folder_is_Comic_Vine_Scraper_under_AppData()
	{
		await Assert.That(SettingsStore.DefaultFolder).IsEqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comic Vine Scraper"));
	}

	[Test]
	public async Task Load_with_no_files_gives_the_defaults()
	{
		Configuration config = Store().Load();

		await Assert.That(config.Update).IsEqualTo(new Configuration { Advanced = AdvancedSettings.Parse("") }.Update);
		await Assert.That(config.ApiKey).IsEqualTo("");
		await Assert.That(config.Advanced.ScrapeDelaySeconds).IsEqualTo(1);
	}

	[Test]
	public async Task Save_then_Load_returns_the_same_settings()
	{
		SettingsStore store = Store();
		var config = new Configuration
		{
			Advanced = AdvancedSettings.Parse("SCRAPE_DELAY=7\nIGNORE_PUBLISHER=Panini\nPUBLISHER_ALIAS=dc->DC Comics"),
			ApiKey = "0123456789abcdef0123456789abcdef01234567",
			Update = BookFields.Series | BookFields.Tags,
			OverwriteExisting = false,
			Choose = ChooseMode.AutoChooseSeries,
			FastRescrape = false,
		};

		store.Save(config);
		Configuration loaded = store.Load();

		await Assert.That(loaded.ApiKey).IsEqualTo(config.ApiKey);
		await Assert.That(loaded.Update).IsEqualTo(config.Update);
		await Assert.That(loaded.OverwriteExisting).IsFalse();
		await Assert.That(loaded.Choose).IsEqualTo(ChooseMode.AutoChooseSeries);
		await Assert.That(loaded.FastRescrape).IsFalse();
		await Assert.That(loaded.Advanced.RawText).IsEqualTo(config.Advanced.RawText);
		await Assert.That(loaded.Advanced.ScrapeDelaySeconds).IsEqualTo(7);
		await Assert.That(loaded.Advanced.IgnoredPublishers).IsEquivalentTo(["panini"]);
	}

	[Test]
	public async Task Save_writes_UTF8_with_a_BOM_and_leaves_no_temp_file()
	{
		Store().Save(new Configuration { Advanced = AdvancedSettings.Parse("IGNORE_PUBLISHER=Ünï") });

		foreach (string name in new[] { "settings.dat", "advanced.dat" })
		{
			await Assert.That(File.ReadAllBytes(Path.Combine(Profile, name)).Take(3).ToArray()).IsEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
		}
		await Assert.That(Directory.GetFiles(Profile).Select(Path.GetFileName)).IsEquivalentTo(["settings.dat", "advanced.dat"]);
		await Assert.That(File.ReadAllText(Path.Combine(Profile, "advanced.dat"), Encoding.UTF8)).IsEqualTo("IGNORE_PUBLISHER=Ünï");
	}

	[Test]
	public async Task Save_keeps_settings_keys_this_version_does_not_know()
	{
		Directory.CreateDirectory(Profile);
		File.WriteAllBytes(Path.Combine(Profile, "settings.dat"), DatFile.ToBytes(new Dictionary<string, DatValue>
		{
			["futureKey"] = new DatValue.Text("keep me"),
			["futureFlag"] = new DatValue.Bool(true),
			["updateSeries"] = new DatValue.Bool(true),
		}, DateTime.Now));
		SettingsStore store = Store();

		store.Save(store.Load() with { Update = BookFields.None });

		IReadOnlyDictionary<string, DatValue> saved = DatFile.FromBytes(File.ReadAllBytes(Path.Combine(Profile, "settings.dat")));
		await Assert.That(saved["futureKey"]).IsEqualTo(new DatValue.Text("keep me"));
		await Assert.That(saved["futureFlag"]).IsEqualTo(new DatValue.Bool(true));
		await Assert.That(saved["updateSeries"]).IsEqualTo(new DatValue.Bool(false));
	}

	[Test]
	public async Task Chosen_series_records_through_to_the_file_and_reloads()
	{
		SettingsStore store = Store();
		ChosenSeries chosen = store.LoadChosenSeries();

		await Assert.That(chosen.Contains(new SeriesId(18436))).IsFalse();
		chosen.Record(new SeriesId(18436));
		chosen.Record(new SeriesId(18436));
		chosen.Record(new SeriesId(7));

		await Assert.That(chosen.Contains(new SeriesId(18436))).IsTrue();
		ChosenSeries reloaded = store.LoadChosenSeries();
		await Assert.That(reloaded.Contains(new SeriesId(7))).IsTrue();
		await Assert.That(reloaded.Contains(new SeriesId(18436))).IsTrue();
		await Assert.That(reloaded.Contains(new SeriesId(8))).IsFalse();
		string[] lines = File.ReadAllLines(Path.Combine(Profile, "series.dat")).Skip(2).ToArray();
		await Assert.That(lines).IsEquivalentTo(["18436 : 18436", "7 : 7"]);
	}

	[Test]
	public async Task Chosen_series_survives_parallel_recording()
	{
		SettingsStore store = Store();
		ChosenSeries chosen = store.LoadChosenSeries();

		Parallel.For(1, 41, id => chosen.Record(new SeriesId(id)));

		ChosenSeries reloaded = store.LoadChosenSeries();
		await Assert.That(Enumerable.Range(1, 40).All(id => reloaded.Contains(new SeriesId(id)))).IsTrue();
	}

	[Test]
	public async Task Geometry_round_trips_and_keeps_other_forms()
	{
		GeometryMap geometry = Store().LoadGeometry();

		geometry.Set("comicform", new Point(10, -20), new Size(300, 400));
		geometry.Set("configform", new Point(5, 6), null);

		await Assert.That(geometry.Get("comicform")).IsEqualTo((new Point(10, -20), new Size(300, 400)));
		await Assert.That(geometry.Get("configform")).IsEqualTo(((Point?)new Point(5, 6), (Size?)null));
		await Assert.That(geometry.Get("searchform")).IsEqualTo(((Point?)null, (Size?)null));
		string text = File.ReadAllText(Path.Combine(Profile, "geometry.dat"));
		await Assert.That(text).Contains("comicformLocation : 10,-20");
		await Assert.That(text).Contains("comicformSize : 300,400");
	}

	[Test]
	[Arguments("1x")]
	[Arguments("a,b")]
	[Arguments("1,")]
	[Arguments("99999999999,1")]
	public async Task Geometry_ignores_a_text_value_that_is_not_two_comma_separated_integers(string value)
	{
		Directory.CreateDirectory(Profile);
		File.WriteAllText(Path.Combine(Profile, "geometry.dat"), "xLocation : " + value + "\n");

		await Assert.That(Store().LoadGeometry().Get("x").Location).IsNull();
	}

	[Test]
	public async Task Geometry_ignores_a_value_that_dat_parsing_turns_into_a_whole_number()
	{
		Directory.CreateDirectory(Profile);
		File.WriteAllText(Path.Combine(Profile, "geometry.dat"), "xLocation : 1\n");

		await Assert.That(Store().LoadGeometry().Get("x").Location).IsNull();
	}

	[Test]
	[Arguments("1")]
	[Arguments(" 7 ")]
	[Arguments("")]
	public async Task A_pair_needs_a_comma_before_either_part_is_parsed(string text)
	{
		await Assert.That(GeometryMap.ParsePair(text)).IsNull();
	}

	[Test]
	public async Task A_pair_is_two_integers_split_at_the_comma()
	{
		await Assert.That(GeometryMap.ParsePair(" 10 , -20 ")).IsEqualTo((10, -20));
	}

	private static FileStream Lock(string path, FileShare share) => new(path, FileMode.Open, FileAccess.Read, share);

	private SettingsStore Store(DebugLog log) => new(Profile, new FixedClock(), log);

	[Test]
	public async Task Recording_a_series_while_series_dat_is_locked_logs_and_carries_on()
	{
		var log = new DebugLog();
		ChosenSeries chosen = Store(log).LoadChosenSeries();
		chosen.Record(new SeriesId(1));
		string path = Path.Combine(Profile, "series.dat");

		using (Lock(path, FileShare.Read))
		{
			chosen.Record(new SeriesId(2));
		}
		chosen.Record(new SeriesId(3));

		await Assert.That(string.Join("\n", log.Snapshot())).Contains("series.dat");
		await Assert.That(chosen.Contains(new SeriesId(2))).IsTrue();
		await Assert.That(Store().LoadChosenSeries().Contains(new SeriesId(2))).IsTrue();
		await Assert.That(Directory.GetFiles(Profile).Select(Path.GetFileName)).IsEquivalentTo(["series.dat"]);
	}

	[Test]
	public async Task Setting_geometry_while_geometry_dat_is_read_only_logs_and_carries_on()
	{
		var log = new DebugLog();
		GeometryMap geometry = Store(log).LoadGeometry();
		geometry.Set("a", new Point(1, 2), null);
		string path = Path.Combine(Profile, "geometry.dat");
		File.SetAttributes(path, FileAttributes.ReadOnly);
		try
		{
			geometry.Set("b", new Point(3, 4), new Size(5, 6));
		}
		finally
		{
			File.SetAttributes(path, FileAttributes.Normal);
		}

		await Assert.That(string.Join("\n", log.Snapshot())).Contains("geometry.dat");
		await Assert.That(geometry.Get("b").Location).IsNull();
		await Assert.That(geometry.Get("a").Location).IsEqualTo(new Point(1, 2));
	}

	[Test]
	public async Task Save_reports_a_locked_file_instead_of_throwing()
	{
		var log = new DebugLog();
		SettingsStore store = Store(log);
		var config = new Configuration { Advanced = AdvancedSettings.Parse("") };
		await Assert.That(store.Save(config).Succeeded).IsTrue();

		SaveResult result;
		using (Lock(Path.Combine(Profile, "settings.dat"), FileShare.Read))
		{
			result = store.Save(config with { ApiKey = "changed" });
		}

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.Error).IsNotNull();
		await Assert.That(string.Join("\n", log.Snapshot())).Contains("settings.dat");
	}

	[Test]
	public async Task An_unreadable_series_dat_is_never_overwritten_in_the_session()
	{
		Directory.CreateDirectory(Profile);
		string path = Path.Combine(Profile, "series.dat");
		File.WriteAllText(path, ":: header\n\n18436 : 18436\n");
		byte[] before = File.ReadAllBytes(path);
		var log = new DebugLog();
		ChosenSeries chosen;
		using (Lock(path, FileShare.None))
		{
			chosen = Store(log).LoadChosenSeries();
		}

		chosen.Record(new SeriesId(7));

		await Assert.That(File.ReadAllBytes(path)).IsEquivalentTo(before);
		await Assert.That(string.Join("\n", log.Snapshot())).Contains("series.dat");
	}

	[Test]
	public async Task An_unreadable_settings_dat_keeps_its_unknown_keys_because_Save_does_not_write_it()
	{
		Directory.CreateDirectory(Profile);
		string path = Path.Combine(Profile, "settings.dat");
		File.WriteAllBytes(path, DatFile.ToBytes(new Dictionary<string, DatValue> { ["futureKey"] = new DatValue.Text("keep me") }, DateTime.Now));
		byte[] before = File.ReadAllBytes(path);
		SettingsStore store = Store(new DebugLog());
		using (Lock(path, FileShare.None))
		{
			store.Load();
		}

		SaveResult result = store.Save(new Configuration { Advanced = AdvancedSettings.Parse("") });

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(File.ReadAllBytes(path)).IsEquivalentTo(before);
	}

	[Test]
	public async Task An_unreadable_geometry_dat_is_not_rewritten_by_Set()
	{
		Directory.CreateDirectory(Profile);
		string path = Path.Combine(Profile, "geometry.dat");
		File.WriteAllText(path, ":: header\n\naLocation : 1,2\n");
		byte[] before = File.ReadAllBytes(path);
		GeometryMap geometry = Store(new DebugLog()).LoadGeometry();
		using (Lock(path, FileShare.None))
		{
			geometry.Get("a");
		}

		geometry.Set("b", new Point(3, 4), null);

		await Assert.That(File.ReadAllBytes(path)).IsEquivalentTo(before);
	}

	[Test]
	public async Task A_missing_file_is_empty_and_is_written_normally()
	{
		SettingsStore store = Store(new DebugLog());

		store.LoadChosenSeries().Record(new SeriesId(5));

		await Assert.That(Store().LoadChosenSeries().Contains(new SeriesId(5))).IsTrue();
	}

	private sealed class FixedClock : IClock
	{
		public DateTime Now => new(2024, 1, 31, 13, 45, 10);

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
	}
}
