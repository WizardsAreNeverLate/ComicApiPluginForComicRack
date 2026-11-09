using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin.Forms;

public sealed class ConfigFormTests
{
	private const string Key = "0123456789abcdef0123456789abcdef01234567";

	private static readonly string[] PythonFormOrder =
	[
		"Series", "Volume", "Number", "Title", "Published Date", "Release Date", "Crossovers", "Publisher", "Imprint", "Writer",
		"Penciller", "Inker", "Colorist", "Letterer", "Cover Artist", "Editor", "Summary", "Characters", "Teams", "Locations", "Webpage",
	];

	private static Configuration Defaults => new() { Advanced = AdvancedSettings.Parse("") };

	[Test]
	public async Task The_Details_list_shows_the_21_fields_in_the_Python_order_checked_as_saved()
	{
		using var session = UiSession.Create();
		session.Settings.Save(Defaults with { ApiKey = Key, Update = (FieldTable.All | BookFields.Notes) & ~BookFields.Writers & ~BookFields.Published });

		(string Label, bool Checked)[] items = Sta.Run(() =>
		{
			(string, bool)[] seen = [];
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				CheckedListBox list = Ui.Find<CheckedListBox>(form, "fields");
				seen = Enumerable.Range(0, list.Items.Count).Select(i => (list.Items[i].ToString(), list.GetItemChecked(i))).ToArray();
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(items.Select(item => item.Label).SequenceEqual(PythonFormOrder)).IsTrue();
		await Assert.That(items.Where(item => !item.Checked).Select(item => item.Label)).IsEquivalentTo(["Writer", "Published Date"]);
	}

	[Test]
	public async Task OK_saves_what_the_form_shows_and_returns_it()
	{
		using var session = UiSession.Create();
		session.Settings.Save(Defaults with { ApiKey = Key });

		Configuration? returned = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				CheckedListBox list = Ui.Find<CheckedListBox>(form, "fields");
				list.SetItemChecked(Array.IndexOf(PythonFormOrder, "Summary"), false);
				list.SetItemChecked(Array.IndexOf(PythonFormOrder, "Volume"), false);
				Ui.Find<CheckBox>(form, "confirmIssue").Checked = true;
				Ui.Find<CheckBox>(form, "rescrapeNotes").Checked = false;
				Ui.Find<CheckBox>(form, "rescrapeTags").Checked = true;
				Ui.Find<CheckBox>(form, "summaryDialog").Checked = false;
				Ui.Find<CheckBox>(form, "fastRescrape").Checked = false;
				Ui.Find<CheckBox>(form, "convertImprints").Checked = false;
				Ui.Find<CheckBox>(form, "ignoreBlanks").Checked = true;
				Ui.Find<CheckBox>(form, "preserveThumbs").Checked = false;
				Ui.Find<RichTextBox>(form, "advanced").Text = "SCRAPE_DELAY=5\nIGNORE_PUBLISHER=Panini";
				Ui.Find<TextBox>(form, "apiKey").Text = "  " + Key + "  ";
				Ui.Find<Button>(form, "ok").PerformClick();
			});
			return ConfigForm.Show(null, session.Ctx);
		});

		Configuration saved = session.Settings.Load();
		Configuration expected = Defaults with
		{
			ApiKey = Key,
			Update = (FieldTable.All & ~BookFields.Summary & ~BookFields.Volume) | BookFields.Tags,
			Choose = ChooseMode.ConfirmIssue,
			SummaryDialog = false,
			FastRescrape = false,
			ConvertImprints = false,
			IgnoreBlanks = true,
			PreserveThumbs = false,
		};
		foreach (Configuration config in new[] { returned!, saved })
		{
			await Assert.That(config with { Advanced = expected.Advanced }).IsEqualTo(expected);
			await Assert.That(config.Advanced.ScrapeDelaySeconds).IsEqualTo(5);
			await Assert.That(config.Advanced.IgnoredPublishers).IsEquivalentTo(["panini"]);
		}
	}

	[Test]
	public async Task Cancel_returns_nothing_and_writes_nothing()
	{
		using var session = UiSession.Create();

		Configuration? returned = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				Ui.Find<TextBox>(form, "apiKey").Text = Key;
				Ui.Find<Button>(form, "cancel").PerformClick();
			});
			return ConfigForm.Show(null, session.Ctx);
		});

		await Assert.That(returned).IsNull();
		await Assert.That(File.Exists(Path.Combine(session.Folder, SettingsStore.SettingsFileName))).IsFalse();
	}

	[Test]
	public async Task OK_is_disabled_until_an_API_key_is_entered()
	{
		using var session = UiSession.Create();

		(bool Empty, bool Blank, bool Typed) enabled = Sta.Run(() =>
		{
			(bool, bool, bool) seen = default;
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				Button ok = Ui.Find<Button>(form, "ok");
				TextBox key = Ui.Find<TextBox>(form, "apiKey");
				bool empty = ok.Enabled;
				key.Text = "   ";
				bool blank = ok.Enabled;
				key.Text = Key;
				seen = (empty, blank, ok.Enabled);
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(enabled).IsEqualTo((false, false, true));
	}

	[Test]
	[Arguments(true, "detailsTab")]
	[Arguments(false, "comicVineTab")]
	public async Task The_form_opens_on_Details_with_a_key_and_on_Comic_Vine_without_one(bool hasKey, string expected)
	{
		using var session = UiSession.Create();
		if (hasKey)
		{
			session.Settings.Save(Defaults with { ApiKey = Key });
		}

		string tab = Sta.Run(() =>
		{
			string seen = "";
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				seen = Ui.Find<TabControl>(form, "tabs").SelectedTab.Name;
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(tab).IsEqualTo(expected);
	}

	[Test]
	public async Task Auto_choose_and_confirm_issue_exclude_each_other()
	{
		using var session = UiSession.Create();

		(bool AutoChecked, bool AutoEnabled, bool ConfirmEnabled, bool ConfirmEnabledAfter) state = Sta.Run(() =>
		{
			(bool, bool, bool, bool) seen = default;
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				CheckBox auto = Ui.Find<CheckBox>(form, "autochoose");
				CheckBox confirm = Ui.Find<CheckBox>(form, "confirmIssue");
				auto.Checked = true;
				bool confirmEnabledWithAuto = confirm.Enabled;
				auto.Checked = false;
				bool confirmEnabledWithout = confirm.Enabled;
				confirm.Checked = true;
				seen = (auto.Checked, auto.Enabled, confirmEnabledWithAuto, confirmEnabledWithout);
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(state.ConfirmEnabled).IsFalse();
		await Assert.That(state.ConfirmEnabledAfter).IsTrue();
		await Assert.That(state.AutoChecked).IsFalse();
		await Assert.That(state.AutoEnabled).IsFalse();
	}

	[Test]
	public async Task Ignore_blanks_needs_overwrite_and_preserve_thumbs_needs_download_thumbs()
	{
		using var session = UiSession.Create();

		(bool IgnoreOn, bool IgnoreOff, bool PreserveOn, bool PreserveOff) enabled = Sta.Run(() =>
		{
			(bool, bool, bool, bool) seen = default;
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				CheckBox overwrite = Ui.Find<CheckBox>(form, "overwrite");
				CheckBox ignore = Ui.Find<CheckBox>(form, "ignoreBlanks");
				CheckBox download = Ui.Find<CheckBox>(form, "downloadThumbs");
				CheckBox preserve = Ui.Find<CheckBox>(form, "preserveThumbs");
				overwrite.Checked = true;
				download.Checked = true;
				bool ignoreOn = ignore.Enabled;
				bool preserveOn = preserve.Enabled;
				overwrite.Checked = false;
				download.Checked = false;
				seen = (ignoreOn, ignore.Enabled, preserveOn, preserve.Enabled);
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(enabled).IsEqualTo((true, false, true, false));
	}

	[Test]
	public async Task Restore_Defaults_resets_everything_but_the_API_key()
	{
		using var session = UiSession.Create();
		session.Settings.Save(Defaults with
		{
			ApiKey = Key,
			Update = BookFields.Series | BookFields.Tags,
			OverwriteExisting = false,
			Choose = ChooseMode.AutoChooseSeries,
			DownloadThumbs = false,
			SummaryDialog = false,
			Advanced = AdvancedSettings.Parse("SCRAPE_DELAY=9"),
		});

		Configuration? returned = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				Ui.Find<Button>(form, "restore").PerformClick();
				Ui.Find<Button>(form, "ok").PerformClick();
			});
			return ConfigForm.Show(null, session.Ctx);
		});

		await Assert.That(returned!).IsEqualTo(Defaults with { ApiKey = Key, Advanced = returned!.Advanced });
		await Assert.That(returned.Advanced.RawText).IsEqualTo("");
		await Assert.That(session.Settings.Load() with { Advanced = returned.Advanced }).IsEqualTo(returned);
	}

	[Test]
	public async Task Select_All_and_Select_None_check_and_clear_every_field()
	{
		using var session = UiSession.Create();

		(int AfterNone, int AfterAll) counts = Sta.Run(() =>
		{
			(int, int) seen = default;
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				CheckedListBox list = Ui.Find<CheckedListBox>(form, "fields");
				Ui.Find<TabControl>(form, "tabs").SelectedTab = Ui.Find<TabPage>(form, "detailsTab");
				Ui.Find<Button>(form, "selectNone").PerformClick();
				int none = list.CheckedItems.Count;
				Ui.Find<Button>(form, "selectAll").PerformClick();
				seen = (none, list.CheckedItems.Count);
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		await Assert.That(counts).IsEqualTo((0, 21));
	}

	[Test]
	public async Task A_settings_file_that_cannot_be_saved_is_reported_and_the_choice_still_returned()
	{
		using var session = UiSession.Create();
		session.Settings.Save(Defaults with { ApiKey = Key });
		string settings = Path.Combine(session.Folder, SettingsStore.SettingsFileName);

		(Configuration? Returned, string Prompt) outcome;
		using (new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			outcome = Sta.Run(() =>
			{
				string prompt = "";
				using IDisposable answer = Ui.WhenOpen<PromptDialog>(dialog =>
				{
					prompt = Ui.Find<Label>(dialog, "message").Text;
					Ui.Find<Button>(dialog, "no").PerformClick();
				});
				using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
				{
					Ui.Find<TextBox>(form, "apiKey").Text = Key;
					Ui.Find<CheckBox>(form, "summaryDialog").Checked = false;
					Ui.Find<Button>(form, "ok").PerformClick();
				});
				return (ConfigForm.Show(null, session.Ctx), prompt);
			});
		}

		await Assert.That(outcome.Returned!.SummaryDialog).IsFalse();
		await Assert.That(outcome.Prompt).StartsWith("An unexpected error occurred.");
		await Assert.That(session.Settings.Load().SummaryDialog).IsTrue();
	}

	[Test]
	public async Task The_Welcome_forms_Settings_button_opens_the_settings_and_returns_to_the_welcome()
	{
		using var session = UiSession.Create();
		session.Settings.Save(Defaults with { ApiKey = Key });

		bool started = Sta.Run(() =>
		{
			WelcomeForm? welcome = null;
			using IDisposable settings = Ui.WhenOpen<ConfigForm>(form =>
			{
				Ui.Find<CheckBox>(form, "summaryDialog").Checked = false;
				Ui.Find<Button>(form, "ok").PerformClick();
				welcome!.BeginInvoke(new Action(() => Ui.Find<Button>(welcome, "start").PerformClick()));
			});
			using IDisposable driver = Ui.WhenOpen<WelcomeForm>(form =>
			{
				welcome = form;
				Ui.Find<Button>(form, "settings").PerformClick();
			});
			return WelcomeForm.Show(null, session.Ctx, 2);
		});

		await Assert.That(started).IsTrue();
		await Assert.That(session.Settings.Load().SummaryDialog).IsFalse();
	}

	[Test]
	public async Task Every_text_is_localized_on_every_tab()
	{
		using var session = UiSession.Create(marked: true);

		string[] unlocalized = Sta.Run(() =>
		{
			string[] found = [];
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				found = [.. Ui.UnlocalizedTexts(form)];
				TabControl tabs = Ui.Find<TabControl>(form, "tabs");
				foreach (TabPage page in tabs.TabPages)
				{
					tabs.SelectedTab = page;
					Ui.Render(form, "ConfigForm-" + page.Name);
				}
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return found;
		});

		await Assert.That(unlocalized).IsEmpty();
	}

	[Test]
	public async Task The_text_boxes_offer_localized_cut_copy_and_paste()
	{
		using var session = UiSession.Create();

		string[][] menus = Sta.Run(() =>
		{
			string[][] seen = [];
			using IDisposable driver = Ui.WhenOpen<ConfigForm>(form =>
			{
				seen = new Control[] { Ui.Find<TextBox>(form, "apiKey"), Ui.Find<RichTextBox>(form, "advanced") }
					.Select(box => box.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray())
					.ToArray();
				form.Close();
			});
			ConfigForm.Show(null, session.Ctx);
			return seen;
		});

		foreach (string[] menu in menus)
		{
			await Assert.That(menu.SequenceEqual(["Cut", "Copy", "Paste"])).IsTrue();
		}
	}
}
