using System.Collections.Generic;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;

namespace ComicVineScraper.Tests.Config;

public sealed class ConfigurationTests
{
	private static AdvancedSettings Advanced(bool scrapeRating = false) => new(
		RawText: "",
		IgnoredPublishers: ["panini"],
		IgnoredSearchTerms: ["tpb"],
		IgnoreBeforeYear: 1950,
		IgnoreAfterYear: 2030,
		NeverIgnoreThreshold: 5,
		ScrapeRating: scrapeRating,
		ShowCovers: true,
		WelcomeDialog: true,
		AltSearchRegex: null,
		IgnoreFolders: false,
		ForceSeriesArt: false,
		NoteScrapeDate: true,
		PublisherAliases: new Dictionary<string, string> { ["dc"] = "DC Comics" },
		UserImprints: new Dictionary<string, string> { ["vertigo"] = "DC Comics" },
		ScrapeDelaySeconds: 2,
		MaxSearchResults: 100);

	[Test]
	public async Task The_default_update_set_is_every_table_field_plus_notes()
	{
		var config = new Configuration { Advanced = Advanced() };

		await Assert.That(config.Update).IsEqualTo(FieldTable.All | BookFields.Notes);
		await Assert.That(config.Update.HasFlag(BookFields.Tags)).IsFalse();
		await Assert.That(FieldTable.All).IsNotEqualTo(BookFields.None);
	}

	[Test]
	public async Task Merge_settings_add_Rating_only_when_the_advanced_setting_is_on()
	{
		MergeSettings off = new Configuration { Advanced = Advanced(scrapeRating: false) }.ToMergeSettings();
		MergeSettings on = new Configuration { Advanced = Advanced(scrapeRating: true) }.ToMergeSettings();

		await Assert.That(off.Update.HasFlag(BookFields.Rating)).IsFalse();
		await Assert.That(on.Update.HasFlag(BookFields.Rating)).IsTrue();
		await Assert.That(on.Update & ~BookFields.Rating).IsEqualTo(off.Update);
	}

	[Test]
	public async Task Merge_settings_drop_Rating_even_if_Update_holds_it_when_the_advanced_setting_is_off()
	{
		var config = new Configuration { Advanced = Advanced(scrapeRating: false), Update = BookFields.Series | BookFields.Rating };

		await Assert.That(config.ToMergeSettings().Update).IsEqualTo(BookFields.Series);
	}

	[Test]
	public async Task Merge_settings_keep_the_saved_IgnoreBlanks_whatever_OverwriteExisting_is()
	{
		var config = new Configuration { Advanced = Advanced(), IgnoreBlanks = true, OverwriteExisting = false };

		await Assert.That(config.ToMergeSettings().IgnoreBlanks).IsTrue();
		await Assert.That((config with { OverwriteExisting = true }).ToMergeSettings().IgnoreBlanks).IsTrue();
	}

	[Test]
	public async Task Merge_settings_force_PreserveThumbs_off_without_download()
	{
		var config = new Configuration { Advanced = Advanced(), PreserveThumbs = true, DownloadThumbs = false };

		await Assert.That(config.ToMergeSettings().PreserveThumbs).IsFalse();
		await Assert.That((config with { DownloadThumbs = true }).ToMergeSettings().PreserveThumbs).IsTrue();
	}

	[Test]
	public async Task Merge_settings_carry_the_advanced_maps_and_flags()
	{
		MergeSettings merge = new Configuration { Advanced = Advanced(), ConvertImprints = false }.ToMergeSettings();

		await Assert.That(merge.NoteScrapeDate).IsTrue();
		await Assert.That(merge.ConvertImprints).IsFalse();
		await Assert.That(merge.PublisherAliases["dc"]).IsEqualTo("DC Comics");
		await Assert.That(merge.UserImprints["vertigo"]).IsEqualTo("DC Comics");
	}

	[Test]
	public async Task Search_settings_copy_the_advanced_filters()
	{
		SearchSettings search = new Configuration { Advanced = Advanced() }.ToSearchSettings();

		await Assert.That(search.IgnoredPublishers).IsEquivalentTo(new[] { "panini" });
		await Assert.That(search.IgnoredSearchTerms).IsEquivalentTo(new[] { "tpb" });
		await Assert.That(search.IgnoreBeforeYear).IsEqualTo(1950);
		await Assert.That(search.IgnoreAfterYear).IsEqualTo(2030);
		await Assert.That(search.NeverIgnoreThreshold).IsEqualTo(5);
		await Assert.That(search.MaxSearchResults).IsEqualTo(100);
	}

	[Test]
	public async Task Choose_mode_drives_ConfirmIssue()
	{
		var config = new Configuration { Advanced = Advanced() };

		await Assert.That(config.ConfirmIssue).IsFalse();
		await Assert.That((config with { Choose = ChooseMode.ConfirmIssue }).ConfirmIssue).IsTrue();
		await Assert.That((config with { Choose = ChooseMode.AutoChooseSeries }).ConfirmIssue).IsFalse();
	}
}
