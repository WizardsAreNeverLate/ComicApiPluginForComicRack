using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ComicApiPlugin.Plugin;

internal enum PromptButtons
{
	Ok,
	YesNo,
}

/// <summary>
/// A message box whose buttons are labelled in ComicRack's language. Python relabelled the system MessageBox with
/// MessageBoxManager.dll; this dialog uses the MessageBox* strings instead and needs no hook into another window.
/// </summary>
internal sealed class PromptDialog : ScraperForm
{
	private const int TextWidth = 360;

	private readonly Bitmap? image;

	private PromptDialog(PluginContext ctx, string text, string caption, MessageBoxIcon icon, PromptButtons buttons) : base(ctx, null)
	{
		Text = caption;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Padding = new Padding(12);

		var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Location = new Point(12, 12) };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		image = IconFor(icon);
		if (image is not null)
		{
			layout.Controls.Add(new PictureBox { Image = image, Size = image.Size, Margin = new Padding(0, 0, 12, 12) }, 0, 0);
		}
		layout.Controls.Add(new Label
		{
			Name = "message",
			Text = text,
			UseMnemonic = false,
			AutoSize = true,
			MaximumSize = new Size(TextWidth, 0),
			Margin = new Padding(0, 0, 0, 12),
		}, 1, 0);

		var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
		if (buttons == PromptButtons.Ok)
		{
			Button ok = Make("ok", TextKeys.MessageBoxOk, DialogResult.OK);
			row.Controls.Add(ok);
			AcceptButton = ok;
			CancelButton = ok;
		}
		else
		{
			Button yes = Make("yes", TextKeys.MessageBoxYes, DialogResult.Yes);
			Button no = Make("no", TextKeys.MessageBoxNo, DialogResult.No);
			row.Controls.Add(no);
			row.Controls.Add(yes);
			AcceptButton = yes;
			CancelButton = no;
		}
		layout.Controls.Add(row, 0, 1);
		layout.SetColumnSpan(row, 2);
		Controls.Add(layout);
	}

	/// <summary>
	/// Shows the prompt modal to owner. Closing it any other way, or cancelling ct, reads as OK, or as No for a yes/no prompt.
	/// </summary>
	public static DialogResult Show(IWin32Window? owner, PluginContext ctx, string text, string caption, MessageBoxIcon icon, PromptButtons buttons, CancellationToken ct = default)
	{
		using var dialog = new PromptDialog(ctx, text, caption, icon, buttons);
		DialogResult result = dialog.ShowModal(owner, ct);
		return buttons == PromptButtons.Ok ? DialogResult.OK : result == DialogResult.Yes ? DialogResult.Yes : DialogResult.No;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			image?.Dispose();
		}
		base.Dispose(disposing);
	}

	private Button Make(string name, string textKey, DialogResult result) =>
		new() { Name = name, Text = Strings[textKey], DialogResult = result, AutoSize = true, MinimumSize = new Size(80, 23), UseVisualStyleBackColor = true };

	private static Bitmap? IconFor(MessageBoxIcon icon) => icon switch
	{
		MessageBoxIcon.Warning => SystemIcons.Warning.ToBitmap(),
		MessageBoxIcon.Error => SystemIcons.Error.ToBitmap(),
		MessageBoxIcon.Information => SystemIcons.Information.ToBitmap(),
		MessageBoxIcon.Question => SystemIcons.Question.ToBitmap(),
		_ => null,
	};
}
