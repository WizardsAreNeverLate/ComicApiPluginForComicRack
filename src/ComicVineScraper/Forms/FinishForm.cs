using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>The summary after a run: how many books were scraped and how many skipped.</summary>
internal sealed class FinishForm : ScraperForm
{
	private FinishForm(PluginContext ctx, RunSummary summary) : base(ctx, "finishform")
	{
		Text = ProductTitle;
		ClientSize = new Size(300, 90);
		var ok = new Button
		{
			Name = "ok",
			Text = Strings[TextKeys.MessageBoxOk],
			DialogResult = DialogResult.OK,
			Location = new Point(105, 58),
			Size = new Size(90, 23),
			UseVisualStyleBackColor = true,
			TabIndex = 0,
		};
		AcceptButton = ok;
		Controls.Add(Centred("scraped", 10, summary.Scraped == 1
			? Strings[TextKeys.FinishFormScrapedSingle]
			: Strings.Format(TextKeys.FinishFormScrapedPlural, summary.Scraped)));
		Controls.Add(Centred("skipped", 30, summary.Skipped == 1
			? Strings[TextKeys.FinishFormSkippedSingle]
			: Strings.Format(TextKeys.FinishFormSkippedPlural, summary.Skipped)));
		Controls.Add(ok);
	}

	public static void Show(IWin32Window? owner, PluginContext ctx, RunSummary summary)
	{
		using var form = new FinishForm(ctx, summary);
		form.ShowModal(owner, CancellationToken.None);
	}

	private static Label Centred(string name, int top, string text) => new()
	{
		Name = name,
		Text = text,
		UseMnemonic = false,
		Location = new Point(10, top),
		Size = new Size(280, 13),
		TextAlign = ContentAlignment.MiddleCenter,
	};
}
