using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ComicApiPlugin.Plugin;

/// <summary>Shown before a scrape when WELCOME_DIALOG is on: start, change the settings first, or cancel.</summary>
internal sealed class WelcomeForm : ScraperForm
{
	private WelcomeForm(PluginContext ctx, int count) : base(ctx, "welcomeform")
	{
		Text = ProductTitle;
		ClientSize = new Size(416, 100);
		var message = new Label
		{
			Name = "message",
			UseMnemonic = false,
			AutoSize = true,
			Location = new Point(9, 10),
			Text = count == 1 ? Strings[TextKeys.WelcomeFormTextSingle] : Strings.Format(TextKeys.WelcomeFormTextPlural, count),
			TabIndex = 2,
		};
		var start = Make("start", TextKeys.WelcomeFormStart, new Point(10, 68), 145, 0);
		start.DialogResult = DialogResult.OK;
		var settings = Make("settings", TextKeys.WelcomeFormSettings, new Point(208, 68), 100, 3);
		settings.Click += (_, _) => ConfigForm.Show(this, Ctx);
		var cancel = Make("cancel", TextKeys.WelcomeFormCancel, new Point(314, 68), 90, 1);
		cancel.DialogResult = DialogResult.Cancel;
		AcceptButton = start;
		CancelButton = cancel;
		Controls.AddRange([message, start, cancel, settings]);
	}

	/// <summary>True to start the scrape. Settings saved from the Settings button are on disk when this returns.</summary>
	public static bool Show(IWin32Window? owner, PluginContext ctx, int count)
	{
		using var form = new WelcomeForm(ctx, count);
		return form.ShowModal(owner, CancellationToken.None) == DialogResult.OK;
	}

	private Button Make(string name, string textKey, Point location, int width, int tabIndex) => new()
	{
		Name = name,
		Text = Strings[textKey],
		Location = location,
		Size = new Size(width, 23),
		UseVisualStyleBackColor = true,
		TabIndex = tabIndex,
	};
}
