using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Config;

/// <summary>Every test runs in its own temp folder. The real %APPDATA%\Comic Vine Scraper is never touched.</summary>
public sealed class SettingsStoreTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "cvs-store-" + Guid.NewGuid().ToString("N"));
	private string Profile => Path.Combine(root, "profile");
	private string Plugin => Path.Combine(root, "plugin");

	public SettingsStoreTests()
	{
		Directory.CreateDirectory(Plugin);
	}

	public void Dispose() => Directory.Delete(root, true);

	private SettingsStore Store() => new(Profile, Plugin, new FixedClock());

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
	public async Task Migration_copies_the_four_files_once_when_settings_dat_is_missing()
	{
		foreach (string name in new[] { "settings.dat", "advanced.dat", "geometry.dat", "series.dat" })
		{
			File.WriteAllText(Path.Combine(Plugin, name), "legacy " + name);
		}
		SettingsStore store = Store();

		store.MigrateIfNeeded();
		File.WriteAllText(Path.Combine(Profile, "series.dat"), "changed after migration");
		File.WriteAllText(Path.Combine(Plugin, "series.dat"), "legacy changed");
		store.MigrateIfNeeded();

		await Assert.That(File.ReadAllText(Path.Combine(Profile, "settings.dat"))).IsEqualTo("legacy settings.dat");
		await Assert.That(File.ReadAllText(Path.Combine(Profile, "geometry.dat"))).IsEqualTo("legacy geometry.dat");
		await Assert.That(File.ReadAllText(Path.Combine(Profile, "series.dat"))).IsEqualTo("changed after migration");
	}

	[Test]
	public async Task Migration_does_nothing_when_settings_dat_already_exists_and_copes_with_missing_legacy_files()
	{
		Directory.CreateDirectory(Profile);
		File.WriteAllText(Path.Combine(Profile, "settings.dat"), "mine");
		File.WriteAllText(Path.Combine(Plugin, "advanced.dat"), "legacy");

		Store().MigrateIfNeeded();

		await Assert.That(File.Exists(Path.Combine(Profile, "advanced.dat"))).IsFalse();

		Directory.Delete(Profile, true);
		Store().MigrateIfNeeded();

		await Assert.That(File.Exists(Path.Combine(Profile, "settings.dat"))).IsFalse();
		await Assert.That(File.ReadAllText(Path.Combine(Profile, "advanced.dat"))).IsEqualTo("legacy");
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
	[Arguments("1")]
	[Arguments("a,b")]
	[Arguments("1,")]
	[Arguments("99999999999,1")]
	public async Task Geometry_ignores_a_value_that_is_not_two_integers(string value)
	{
		Directory.CreateDirectory(Profile);
		File.WriteAllText(Path.Combine(Profile, "geometry.dat"), "xLocation : " + value + "\n");

		await Assert.That(Store().LoadGeometry().Get("x").Location).IsNull();
	}

	private sealed class FixedClock : IClock
	{
		public DateTime Now => new(2024, 1, 31, 13, 45, 10);

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
	}
}
