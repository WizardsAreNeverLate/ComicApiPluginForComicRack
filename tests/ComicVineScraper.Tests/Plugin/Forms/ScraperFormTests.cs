using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Plugin;
using ComicVineScraper.Tests.Engine;

namespace ComicVineScraper.Tests.Plugin.Forms;

public sealed class ScraperFormTests
{
	[Test]
	public async Task A_form_opens_where_geometry_dat_says_and_saves_where_the_user_moved_it()
	{
		using var session = UiSession.Create();
		Rectangle screen = Screen.PrimaryScreen.WorkingArea;
		var stored = new Point(screen.Left + 40, screen.Top + 30);
		var moved = new Point(screen.Left + 90, screen.Top + 70);
		session.Settings.LoadGeometry().Set("probeform", stored, null);

		Point opened = Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			Point at = Point.Empty;
			Ui.WhenShown(form, () =>
			{
				at = form.Location;
				form.Location = moved;
				form.Close();
			});
			form.ShowModal(null, CancellationToken.None);
			return at;
		});

		await Assert.That(opened).IsEqualTo(stored);
		await Assert.That(session.Settings.LoadGeometry().Get("probeform").Location).IsEqualTo(moved);
	}

	[Test]
	public async Task A_form_that_is_not_moved_does_not_rewrite_geometry_dat()
	{
		using var session = UiSession.Create();
		Rectangle screen = Screen.PrimaryScreen.WorkingArea;
		session.Settings.LoadGeometry().Set("probeform", new Point(screen.Left + 40, screen.Top + 30), null);
		string file = Path.Combine(session.Folder, "geometry.dat");
		DateTime written = File.GetLastWriteTimeUtc(file);
		File.SetLastWriteTimeUtc(file, written.AddMinutes(-5));

		Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			Ui.WhenShown(form, form.Close);
			form.ShowModal(null, CancellationToken.None);
		});

		await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written.AddMinutes(-5));
	}

	[Test]
	public async Task A_form_whose_stored_centre_is_off_every_screen_is_centred_on_screen_and_saved_there()
	{
		using var session = UiSession.Create();
		Rectangle all = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
		session.Settings.LoadGeometry().Set("probeform", new Point(all.Right + 500, all.Bottom + 500), null);

		Rectangle shown = Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			Rectangle bounds = Rectangle.Empty;
			Ui.WhenShown(form, () =>
			{
				bounds = form.Bounds;
				form.Close();
			});
			form.ShowModal(null, CancellationToken.None);
			return bounds;
		});

		var centre = new Point(shown.X + shown.Width / 2, shown.Y + shown.Height / 2);
		await Assert.That(all.Contains(centre)).IsTrue();
		await Assert.That(session.Settings.LoadGeometry().Get("probeform").Location).IsEqualTo(shown.Location);
	}

	[Test]
	public async Task A_form_with_no_stored_geometry_opens_on_screen()
	{
		using var session = UiSession.Create();

		Rectangle shown = Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			Rectangle bounds = Rectangle.Empty;
			Ui.WhenShown(form, () =>
			{
				bounds = form.Bounds;
				form.Close();
			});
			form.ShowModal(null, CancellationToken.None);
			return bounds;
		});

		Rectangle all = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
		await Assert.That(all.Contains(new Point(shown.X + shown.Width / 2, shown.Y + shown.Height / 2))).IsTrue();
	}

	[Test]
	public async Task Escape_closes_a_form_as_cancelled()
	{
		using var session = UiSession.Create();

		DialogResult result = Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			Ui.WhenShown(form, () => Ui.PressCommandKey(form.Ok, Keys.Escape));
			return form.ShowModal(null, CancellationToken.None);
		});

		await Assert.That(result).IsEqualTo(DialogResult.Cancel);
	}

	[Test]
	public async Task Cancelling_the_token_closes_an_open_form_as_cancelled()
	{
		using var session = UiSession.Create();

		(DialogResult Result, bool WasShown) outcome = Sta.Run(() =>
		{
			using var cts = new CancellationTokenSource();
			using var form = new ProbeForm(session.Ctx);
			bool shown = false;
			Ui.WhenShown(form, () =>
			{
				shown = true;
				cts.Cancel();
			});
			return (form.ShowModal(null, cts.Token), shown);
		});

		await Assert.That(outcome.WasShown).IsTrue();
		await Assert.That(outcome.Result).IsEqualTo(DialogResult.Cancel);
	}

	[Test]
	public async Task Cancelling_the_token_from_another_thread_closes_the_form()
	{
		using var session = UiSession.Create();

		DialogResult result = Sta.Run(() =>
		{
			using var cts = new CancellationTokenSource();
			using var form = new ProbeForm(session.Ctx);
			Ui.WhenShown(form, () => Task.Run(cts.Cancel));
			return form.ShowModal(null, cts.Token);
		});

		await Assert.That(result).IsEqualTo(DialogResult.Cancel);
	}

	[Test]
	public async Task An_already_cancelled_token_returns_cancel_without_showing_the_form()
	{
		using var session = UiSession.Create();

		(DialogResult Result, bool WasShown) outcome = Sta.Run(() =>
		{
			using var form = new ProbeForm(session.Ctx);
			bool shown = false;
			form.Shown += (_, _) => shown = true;
			return (form.ShowModal(null, new CancellationToken(canceled: true)), shown);
		});

		await Assert.That(outcome.WasShown).IsFalse();
		await Assert.That(outcome.Result).IsEqualTo(DialogResult.Cancel);
	}

	[Test]
	public async Task A_choice_made_before_the_cancel_is_still_reported_as_cancel()
	{
		using var session = UiSession.Create();

		DialogResult result = Sta.Run(() =>
		{
			using var cts = new CancellationTokenSource();
			using var form = new ProbeForm(session.Ctx);
			form.FormClosing += (_, _) => cts.Cancel();
			Ui.WhenShown(form, form.Ok.PerformClick);
			return form.ShowModal(null, cts.Token);
		});

		await Assert.That(result).IsEqualTo(DialogResult.Cancel);
	}

	[Test]
	public async Task ComicRacks_theme_is_applied_to_the_form()
	{
		using var session = UiSession.Create();
		var theme = new RecordingTheme();
		PluginContext themed = session.Ctx with { CurrentTheme = () => theme };

		Sta.Run(() =>
		{
			using var form = new ProbeForm(themed);
			Ui.WhenShown(form, form.Close);
			form.ShowModal(null, CancellationToken.None);
		});

		await Assert.That(theme.Applied).IsEquivalentTo(["ProbeForm"]);
	}

	private sealed class RecordingTheme : cYo.Projects.ComicRack.Plugins.Theme.IThemePlugin
	{
		public System.Collections.Generic.List<string> Applied { get; } = [];
		public void ApplyTheme(Control control) => Applied.Add(control.GetType().Name);
		public cYo.Common.Windows.Forms.Theme.UIComponent UIComponent => throw new NotSupportedException();
		public cYo.Common.Windows.Forms.Theme.Themes CurrentTheme => throw new NotSupportedException();
		public bool IsDarkModeEnabled => false;
		public ToolStripRenderer ToolStripRenderer => throw new NotSupportedException();
	}

	private sealed class ProbeForm : ScraperForm
	{
		public ProbeForm(PluginContext ctx) : base(ctx, "probeform")
		{
			ClientSize = new Size(200, 80);
			Ok = new Button { DialogResult = DialogResult.OK, Location = new Point(10, 10) };
			Controls.Add(Ok);
		}

		public Button Ok { get; }
	}
}

public sealed class PromptDialogTests
{
	[Test]
	public async Task An_ok_prompt_shows_the_text_and_caption_with_a_localized_OK_button()
	{
		using var session = UiSession.Create(marked: true);

		(DialogResult Result, string Ok, string Text, string Caption, string[] Unlocalized) seen = Sta.Run(() =>
		{
			(string, string, string, string[]) captured = ("", "", "", []);
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				Button ok = Ui.Find<Button>(prompt, "ok");
				captured = (ok.Text, Ui.Find<Label>(prompt, "message").Text, prompt.Text, Ui.UnlocalizedTexts(prompt, "Some text", "Some caption").ToArray());
				Ui.Render(prompt, "PromptDialog-ok");
				ok.PerformClick();
			});
			DialogResult result = PromptDialog.Show(null, session.Ctx, "Some text", "Some caption", MessageBoxIcon.Warning, PromptButtons.Ok);
			return (result, captured.Item1, captured.Item2, captured.Item3, captured.Item4);
		});

		await Assert.That(seen.Result).IsEqualTo(DialogResult.OK);
		await Assert.That(seen.Ok).IsEqualTo(UiSession.Open + "OK" + UiSession.Close);
		await Assert.That(seen.Text).IsEqualTo("Some text");
		await Assert.That(seen.Caption).IsEqualTo("Some caption");
		await Assert.That(seen.Unlocalized).IsEmpty();
	}

	[Test]
	[Arguments("yes", DialogResult.Yes)]
	[Arguments("no", DialogResult.No)]
	public async Task A_yes_no_prompt_returns_the_button_clicked(string button, DialogResult expected)
	{
		using var session = UiSession.Create();

		DialogResult result = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				Ui.Render(prompt, "PromptDialog-yesno");
				Ui.Find<Button>(prompt, button).PerformClick();
			});
			return PromptDialog.Show(null, session.Ctx, "Save the log?", "Unexpected Error", MessageBoxIcon.Error, PromptButtons.YesNo);
		});

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	public async Task Escape_answers_no_to_a_yes_no_prompt()
	{
		using var session = UiSession.Create();

		DialogResult result = Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt => Ui.PressCommandKey(Ui.Find<Button>(prompt, "yes"), Keys.Escape));
			return PromptDialog.Show(null, session.Ctx, "Save the log?", "Unexpected Error", MessageBoxIcon.Error, PromptButtons.YesNo);
		});

		await Assert.That(result).IsEqualTo(DialogResult.No);
	}

	[Test]
	public async Task Yes_and_No_are_labelled_in_ComicRacks_language()
	{
		using var session = UiSession.Create(marked: true);

		string[] labels = Sta.Run(() =>
		{
			string[] seen = [];
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				seen = [Ui.Find<Button>(prompt, "yes").Text, Ui.Find<Button>(prompt, "no").Text];
				Ui.Find<Button>(prompt, "no").PerformClick();
			});
			PromptDialog.Show(null, session.Ctx, "text", "caption", MessageBoxIcon.Error, PromptButtons.YesNo);
			return seen;
		});

		await Assert.That(labels).IsEquivalentTo([UiSession.Open + "Yes" + UiSession.Close, UiSession.Open + "No" + UiSession.Close]);
	}
}

public sealed class ErrorDialogsTests
{
	[Test]
	[Arguments(ComicVineError.InvalidApiKey, "The Comic Vine online database is not responding because your API key is invalid.")]
	[Arguments(ComicVineError.RateLimited, "The Comic Vine online database cannot be reached because you've exceeded your database access limit.")]
	[Arguments(ComicVineError.Network, "The Comic Vine online database could not be reached.")]
	public async Task A_Comic_Vine_failure_shows_its_own_warning_with_only_OK(ComicVineError kind, string opening)
	{
		using var session = UiSession.Create();

		(string Text, string Caption, bool HasNo) seen = Sta.Run(() =>
		{
			(string, string, bool) captured = ("", "", false);
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				captured = (Ui.Find<Label>(prompt, "message").Text, prompt.Text, prompt.Controls.Find("no", true).Length > 0);
				Ui.Find<Button>(prompt, "ok").PerformClick();
			});
			ErrorDialogs.Show(null, session.Ctx, new ComicVineException(kind, "https://comicvine.gamespot.com/api/search/", null, null));
			return captured;
		});

		await Assert.That(seen.Text).StartsWith(opening);
		await Assert.That(seen.Caption).IsEqualTo("Cannot Access Online Database");
		await Assert.That(seen.HasNo).IsFalse();
	}

	[Test]
	public async Task Any_other_error_asks_to_save_the_log()
	{
		using var session = UiSession.Create();

		(string Text, string Caption) seen = Sta.Run(() =>
		{
			(string, string) captured = ("", "");
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				captured = (Ui.Find<Label>(prompt, "message").Text, prompt.Text);
				Ui.Find<Button>(prompt, "no").PerformClick();
			});
			ErrorDialogs.Show(null, session.Ctx, new InvalidOperationException("the engine broke"));
			return captured;
		});

		await Assert.That(seen.Text).StartsWith("An unexpected error occurred.");
		await Assert.That(seen.Caption).IsEqualTo("Unexpected Error");
	}

	[Test]
	public async Task A_run_ending_error_shown_to_the_user_is_logged_once_and_without_the_key()
	{
		using var session = UiSession.Create();
		using var world = new EngineWorld { Config = Configs.With(ChooseMode.Ask) with { ApiKey = EngineWorld.ApiKey } };
		RecordedResponse invalid = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "invalid-key.txt"));
		world.Respond(Synthetic.IssueUrl(37566), invalid.Body, invalid.Status);
		ScrapeFailedException failure = (await Assert.ThrowsAsync<ScrapeFailedException>(
			() => world.RunAsync(FakeBook.File(@"C:\Comics\Batman 497.cbz", b => b.Tags = "CVDB37566"))))!;

		Sta.Run(() =>
		{
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt => Ui.Find<Button>(prompt, "ok").PerformClick());
			ErrorDialogs.Show(null, session.Ctx with { Log = world.Log }, failure.InnerException!);
		});

		string[] log = [.. world.Log.Snapshot()];
		await Assert.That(log.Count(line => line.StartsWith("Caught ComicVineException", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(log.Any(line => line.Contains(EngineWorld.ApiKey))).IsFalse();
	}
}

public sealed class LogFileTests
{
	[Test]
	public async Task Saving_writes_every_log_line_and_confirms_when_asked()
	{
		using var session = UiSession.Create();
		session.Log.Debug("first line");
		session.Log.Debug("second line");
		string path = Path.Combine(Path.GetTempPath(), "cvs-log-" + Guid.NewGuid().ToString("N") + ".txt");

		string confirmation = Sta.Run(() =>
		{
			string text = "";
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				text = Ui.Find<Label>(prompt, "message").Text;
				Ui.Find<Button>(prompt, "ok").PerformClick();
			});
			LogFile.Save(null, session.Ctx, path, confirm: true);
			return text;
		});

		try
		{
			string written = File.ReadAllText(path);
			await Assert.That(written).Contains("first line\nsecond line\n");
			await Assert.That(confirmation).StartsWith("A log file for this error has been saved.");
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task A_failed_save_says_so()
	{
		using var session = UiSession.Create();
		string path = Path.Combine(Path.GetTempPath(), "cvs-missing-" + Guid.NewGuid().ToString("N"), "log.txt");

		string message = Sta.Run(() =>
		{
			string text = "";
			using IDisposable driver = Ui.WhenOpen<PromptDialog>(prompt =>
			{
				text = Ui.Find<Label>(prompt, "message").Text;
				Ui.Find<Button>(prompt, "ok").PerformClick();
			});
			LogFile.Save(null, session.Ctx, path, confirm: false);
			return text;
		});

		await Assert.That(message).IsEqualTo("Error saving debug log.");
	}
}
