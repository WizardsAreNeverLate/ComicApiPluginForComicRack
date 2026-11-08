using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ComicVineScraper.Plugin;

/// <summary>The modal window that hosts a scrape: what is being scraped, overall progress, and Cancel. Phase 4 adds the cover panel.</summary>
internal sealed class ComicForm : Form
{
	private readonly PluginContext ctx;
	private readonly Label scraping = new() { AutoSize = false, Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
	private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 20 };
	private readonly Button cancel = new() { AutoSize = true, Anchor = AnchorStyles.Right };

	public ComicForm(PluginContext ctx)
	{
		this.ctx = ctx;
		Text = "Comic Vine Scraper - v" + typeof(ComicForm).Assembly.GetName().Version.ToString(3);
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowIcon = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(420, 96);
		Padding = new Padding(10);
		cancel.Click += (_, _) => Close();
		CancelButton = cancel;
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
		buttons.Controls.Add(cancel);
		Controls.Add(buttons);
		Controls.Add(progress);
		Controls.Add(scraping);
		ctx.Theme?.ApplyTheme(this);
	}

	public void ShowBook(string name, int done, int total)
	{
		scraping.Text = ctx.Strings["ComicFormScrapingLabel"] + " " + name;
		progress.Maximum = total;
		progress.Value = done;
		cancel.Text = ctx.Strings.Format("ComicFormCancelButton", total - done);
	}

	/// <summary>Cancel was asked for; the form stays up until the run observes it.</summary>
	public void ShowCancelling() => cancel.Enabled = false;
}
