using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Tests.Config;

public sealed class ProviderSettingsTests
{
	private const string Token = "vdb_secret_token_1234567890";

	private static Configuration Blank => new() { Advanced = AdvancedSettings.Parse("") };

	private static Configuration Load(Dictionary<string, DatValue> map) => Configuration.FromMaps(map, "");

	[Test]
	public async Task Provider_settings_round_trip_through_the_settings_map()
	{
		Configuration saved = Blank with
		{
			ApiKey = "cv",
			VerseDbApiKey = Token,
			Providers = ProviderTable.Normalize([new ProviderSetting(ProviderKind.VerseDb, true), new ProviderSetting(ProviderKind.ComicVine, false)]),
		};

		Configuration loaded = Configuration.FromMaps(saved.ToSettingsMap(), "");

		await Assert.That(loaded.VerseDbApiKey).IsEqualTo(Token);
		await Assert.That(loaded.Providers.Select(p => (p.Kind, p.Enabled)).ToArray())
			.IsEquivalentTo(new[] { (ProviderKind.VerseDb, true), (ProviderKind.ComicVine, false) });
		await Assert.That(loaded.Providers[0].Kind).IsEqualTo(ProviderKind.VerseDb);
	}

	[Test]
	public async Task Missing_provider_keys_give_defaults()
	{
		Configuration loaded = Load(new Dictionary<string, DatValue> { ["apiKey"] = new DatValue.Text("cv") });

		await Assert.That(loaded.VerseDbApiKey).IsEqualTo("");
		await Assert.That(loaded.Providers).IsEquivalentTo(ProviderTable.Default);
		await Assert.That(loaded.Providers[0].Kind).IsEqualTo(ProviderKind.ComicVine);
	}

	[Test]
	public async Task Unknown_and_duplicate_providers_are_dropped_and_missing_ones_appended()
	{
		Configuration loaded = Load(new Dictionary<string, DatValue>
		{
			["providerOrder"] = new DatValue.Text("Bogus,VerseDb,VerseDb,99"),
			["verseDbEnabled"] = new DatValue.Bool(false),
		});

		await Assert.That(loaded.Providers.Select(p => p.Kind).ToArray()).IsEquivalentTo(new[] { ProviderKind.VerseDb, ProviderKind.ComicVine });
		await Assert.That(loaded.Providers[0].Kind).IsEqualTo(ProviderKind.VerseDb);
		await Assert.That(loaded.Providers[0].Enabled).IsFalse();
		await Assert.That(loaded.Providers[1].Enabled).IsTrue();
	}

	[Test]
	public async Task Saving_leaves_the_keys_the_Python_plugin_reads_as_they_were()
	{
		IReadOnlyDictionary<string, DatValue> map = (Blank with { ApiKey = "cv" }).ToSettingsMap();

		await Assert.That(map["apiKey"]).IsEqualTo(new DatValue.Text("cv"));
		await Assert.That(map.ContainsKey("verseDbApiKey")).IsTrue();
	}

	[Test]
	public async Task Available_actions_follow_priority_and_skip_disabled_or_keyless_providers()
	{
		Configuration both = Blank with
		{
			ApiKey = "cv",
			VerseDbApiKey = Token,
			Providers = ProviderTable.Normalize([new ProviderSetting(ProviderKind.VerseDb, true), new ProviderSetting(ProviderKind.ComicVine, true)]),
		};

		await Assert.That(both.AvailableActions().ToArray()).IsEquivalentTo(new[] { ScrapeAction.VerseDbCoverSearch, ScrapeAction.ComicVineQuery });
		await Assert.That(both.AvailableActions()[0]).IsEqualTo(ScrapeAction.VerseDbCoverSearch);
		await Assert.That((both with { VerseDbApiKey = "  " }).AvailableActions().ToArray()).IsEquivalentTo(new[] { ScrapeAction.ComicVineQuery });
		await Assert.That((both with { Providers = ProviderTable.Normalize([new ProviderSetting(ProviderKind.VerseDb, false), new ProviderSetting(ProviderKind.ComicVine, true)]) })
			.AvailableActions().ToArray()).IsEquivalentTo(new[] { ScrapeAction.ComicVineQuery });
		await Assert.That(Blank.AvailableActions()).IsEmpty();
	}

	[Test]
	public async Task ToString_does_not_print_the_VerseDB_token()
	{
		string text = (Blank with { VerseDbApiKey = Token }).ToString();

		await Assert.That(text).DoesNotContain(Token);
		await Assert.That(text).Contains("VerseDbApiKey = <redacted>");
	}
}
