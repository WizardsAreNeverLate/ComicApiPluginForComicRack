using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.Plugin;

/// <summary>The issues of one series in natural order, with the hinted issue selected and the cover of the selected one.</summary>
internal sealed class IssueForm : ScraperForm
{
	private readonly IssueQuestion question;
	private readonly ButtonGrid grid;
	private readonly IssueCoverPanel cover;
	private readonly Button ok;

	private IssueForm(PluginContext ctx, IComicVine comicVine, Configuration config, IssueQuestion question) : base(ctx, "issueform")
	{
		this.question = question;
		bool covers = config.Advanced.ShowCovers;
		Text = Strings[TextKeys.IssueFormTitle];
		ClientSize = new Size(730, 395);

		ok = MakeButton("ok", TextKeys.IssueFormOK, new Point(covers ? 223 : 10, 362), 90, DialogResult.OK, 1);
		Button skip = MakeButton("skip", TextKeys.IssueFormSkip, new Point(covers ? 318 : 105, 362), 90, DialogResult.Ignore, 2);
		UseSkipButton(skip, TextKeys.IssueFormSkip);
		Button back = MakeButton("back", TextKeys.IssueFormGoBack, new Point(595, 362), 125, DialogResult.Retry, 3);

		grid = new ButtonGrid("issues", ok, covers ? new Point(218, 60) : new Point(10, 60), covers ? new Size(500, 290) : new Size(708, 290)) { TabIndex = 5 };
		grid.AddColumn("number", Strings[TextKeys.IssueFormIssueCol], DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("title", Strings[TextKeys.IssueFormTitleCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.Fill);
		grid.AddColumn("id", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		grid.AddColumn("model", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		for (int i = 0; i < question.Issues.Count; i++)
		{
			IssueRef issue = question.Issues[i];
			grid.Rows.Add(issue.IssueNumber, issue.Title.Length > 0 ? "   " + issue.Title : "", issue.Id.Value, i);
		}
		grid.SortCompare += (_, e) =>
		{
			if (e.Column.Name == "number")
			{
				e.SortResult = NaturalOrder.Instance.Compare((string)e.CellValue1, (string)e.CellValue2);
				e.Handled = true;
			}
		};

		var message = new Label
		{
			Name = "message",
			UseMnemonic = false,
			Location = covers ? new Point(218, 20) : new Point(10, 20),
			Size = covers ? new Size(480, 40) : new Size(680, 40),
			Text = Describe(question.Series),
		};
		cover = new IssueCoverPanel(ctx, comicVine, covers, null) { Location = new Point(10, 30), TabIndex = 4 };

		Controls.AddRange([message, grid, ok, skip, back, cover]);
	}

	public static IssueChoice Ask(IWin32Window? owner, PluginContext ctx, IComicVine comicVine, Configuration config, IssueQuestion question, CoverPreferences covers, CancellationToken ct)
	{
		if (question.Issues.Count == 0)
		{
			throw new ArgumentException("The issue dialog needs at least one issue.", nameof(question));
		}
		using var form = new IssueForm(ctx, comicVine, config, question);
		DialogResult result = form.ShowModal(owner, ct);
		switch (result)
		{
			case DialogResult.OK when form.grid.SelectedModel is int i:
				if (form.cover.PreferredCover is var (issue, url))
				{
					covers.Prefer(issue, url);
				}
				return new IssueChoice.Pick(question.Issues[i]);
			case DialogResult.Retry:
				return new IssueChoice.Back();
			case DialogResult.Ignore when form.SkipForever:
				return new IssueChoice.SkipForever();
			case DialogResult.Ignore:
				return new IssueChoice.Skip();
			default:
				return new IssueChoice.Cancel();
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		int row = question.Hint is { } hint ? Math.Max(0, IndexOf(hint.Id)) : 0;
		grid.CurrentCell = grid.Rows[row].Cells["number"];
		grid.SelectionChanged += (_, _) => SelectionChanged();
		SelectionChanged();
	}

	private int IndexOf(IssueId id)
	{
		for (int i = 0; i < grid.Rows.Count; i++)
		{
			if ((int)grid.Rows[i].Cells["id"].Value == id.Value)
			{
				return i;
			}
		}
		return -1;
	}

	private void SelectionChanged()
	{
		int? selected = grid.SelectedModel;
		cover.ShowIssue(selected is int i ? question.Issues[i] : null);
		ok.Enabled = selected is not null;
	}

	/// <summary>"'Name' (Publisher, 1990)", then a line break when that is 40 characters or fewer, as Python built it.</summary>
	private string Describe(SeriesRef series)
	{
		string year = series.StartYear is > 0 and var y ? y.ToString(CultureInfo.InvariantCulture) : "";
		string name = series.Name.Length == 0 ? ""
			: series.Publisher.Length == 0 ? "'" + series.Name + "'"
			: year.Length == 0 ? "'" + series.Name + "' (" + series.Publisher + ")"
			: "'" + series.Name + "' (" + series.Publisher + ", " + year + ")";
		return Strings.Format(TextKeys.IssueFormChooseText, name, name.Length > 40 ? "  " : "\n");
	}

	private Button MakeButton(string name, string textKey, Point location, int width, DialogResult result, int tabIndex) => new()
	{
		Name = name,
		Text = Strings[textKey],
		Location = location,
		Size = new Size(width, 24),
		DialogResult = result,
		TabIndex = tabIndex,
	};
}
