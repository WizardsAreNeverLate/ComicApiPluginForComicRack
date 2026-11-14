using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// Shown before a scrape when WELCOME_DIALOG is on: one start button per available action in priority order, the settings,
/// or cancel. The Settings button can change which actions are available, so the start buttons are rebuilt after it.
/// </summary>
internal sealed class WelcomeForm : ScraperForm
{
	private const int RowHeight = 30;

	private readonly List<Button> starts = [];
	private readonly Button settings;
	private readonly Button cancel;
	private ScrapeAction? chosen;

	private WelcomeForm(PluginContext ctx, int count) : base(ctx, "welcomeform")
	{
		Text = ProductTitle;
		var message = new Label
		{
			Name = "message",
			UseMnemonic = false,
			AutoSize = true,
			Location = new Point(9, 10),
			Text = count == 1 ? Strings[TextKeys.WelcomeFormTextSingle] : Strings.Format(TextKeys.WelcomeFormTextPlural, count),
			TabIndex = 2,
		};
		settings = Make("settings", Strings[TextKeys.WelcomeFormSettings], 100, 3);
		settings.Click += (_, _) =>
		{
			ConfigForm.Show(this, Ctx);
			ShowStartButtons();
		};
		cancel = Make("cancel", Strings[TextKeys.WelcomeFormCancel], 90, 1);
		cancel.DialogResult = DialogResult.Cancel;
		CancelButton = cancel;
		Controls.AddRange([message, cancel, settings]);
		ShowStartButtons();
	}

	/// <summary>The action the user started, or null for Cancel. Settings saved from the Settings button are on disk when this returns.</summary>
	public static ScrapeAction? Show(IWin32Window? owner, PluginContext ctx, int count)
	{
		using var form = new WelcomeForm(ctx, count);
		return form.ShowModal(owner, CancellationToken.None) == DialogResult.OK ? form.chosen : null;
	}

	/// <summary>The first button is named "start" and is the default; the others carry their action's name.</summary>
	private void ShowStartButtons()
	{
		foreach (Button old in starts)
		{
			Controls.Remove(old);
			old.Dispose();
		}
		starts.Clear();
		IReadOnlyList<ScrapeAction> actions = Ctx.Settings.Load().AvailableActions();
		for (int i = 0; i < actions.Count; i++)
		{
			ScrapeAction action = actions[i];
			Button start = Make(i == 0 ? "start" : "start" + action, StartText(action), 190, 0);
			start.Location = new Point(10, 68 + i * RowHeight);
			start.Click += (_, _) =>
			{
				chosen = action;
				DialogResult = DialogResult.OK;
			};
			starts.Add(start);
			Controls.Add(start);
		}
		AcceptButton = starts.Count > 0 ? starts[0] : null;
		settings.Location = new Point(214, 68);
		cancel.Location = new Point(314, 68);
		ClientSize = new Size(416, 68 + System.Math.Max(1, starts.Count) * RowHeight + 10);
	}

	private string StartText(ScrapeAction action) => Strings[action switch
	{
		ScrapeAction.ComicVineQuery => TextKeys.WelcomeFormStart,
		ScrapeAction.VerseDbCoverSearch => TextKeys.WelcomeFormCoverSearch,
		_ => throw new System.ArgumentOutOfRangeException(nameof(action)),
	}];

	private Button Make(string name, string text, int width, int tabIndex) => new()
	{
		Name = name,
		Text = text,
		Size = new Size(width, 23),
		UseVisualStyleBackColor = true,
		TabIndex = tabIndex,
	};

}
