using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Plugin;

/// <summary>The series a search found, best match first, with the cover of the selected one.</summary>
internal sealed class SeriesForm : ScraperForm
{
	private readonly SeriesQuestion question;
	private readonly ButtonGrid grid;
	private readonly IssueCoverPanel cover;
	private readonly Button ok;
	private readonly Button showIssues;

	private SeriesForm(PluginContext ctx, IComicVine comicVine, Configuration config, SeriesQuestion question) : base(ctx, "seriesform")
	{
		this.question = question;
		bool covers = config.Advanced.ShowCovers;
		Text = Strings[TextKeys.SeriesFormTitle];
		ClientSize = new Size(730, 395);

		ok = MakeButton("ok", TextKeys.SeriesFormOK, new Point(15, 362), 90, DialogResult.OK, 1);
		Button skip = MakeButton("skip", TextKeys.SeriesFormSkip, new Point(110, 362), 90, DialogResult.Ignore, 2);
		UseSkipButton(skip, TextKeys.SeriesFormSkip);
		Button again = MakeButton("searchAgain", TextKeys.SeriesFormAgain, new Point(covers ? 275 : 485, 362), 115, DialogResult.Retry, 3);
		showIssues = MakeButton("showIssues", TextKeys.SeriesFormIssues, new Point(covers ? 395 : 605, 362), 115, DialogResult.Yes, 4);

		var message = new Label
		{
			Name = "message",
			UseMnemonic = false,
			Location = new Point(10, 20),
			Size = new Size(480, 40),
			Text = question.Candidates.Count > 1
				? Strings.Format(TextKeys.SeriesFormChooseText, question.SearchTerms, question.Candidates.Count)
				: Strings.Format(TextKeys.SeriesFormConfirmText, question.SearchTerms),
		};

		grid = new ButtonGrid("series", ok, new Point(10, 60), covers ? new Size(500, 290) : new Size(710, 290)) { TabIndex = 6 };
		DataGridViewColumn name = grid.AddColumn("name", Strings[TextKeys.SeriesFormSeriesCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.Fill);
		name.FillWeight = 200;
		grid.AddColumn("year", Strings[TextKeys.SeriesFormYearCol], DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("issues", Strings[TextKeys.SeriesFormIssuesCol], DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells);
		grid.AddColumn("publisher", Strings[TextKeys.SeriesFormPublisherCol], DataGridViewContentAlignment.MiddleLeft, DataGridViewAutoSizeColumnMode.Fill);
		grid.AddColumn("id", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		grid.AddColumn("match", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		grid.AddColumn("model", "", DataGridViewContentAlignment.MiddleCenter, DataGridViewAutoSizeColumnMode.AllCells, visible: false);
		for (int i = 0; i < question.Candidates.Count; i++)
		{
			(SeriesRef series, double score) = (question.Candidates[i].Series, question.Candidates[i].Score);
			grid.Rows.Add(series.Name, series.StartYear, series.IssueCount, series.Publisher, series.Id.Value, score, i);
		}
		grid.SelectionChanged += (_, _) => SelectionChanged();

		string? hint = config.Advanced.ForceSeriesArt ? null : question.Book.Effective.Number;
		cover = new IssueCoverPanel(ctx, comicVine, covers, hint) { Location = new Point(523, 30), Visible = covers, TabIndex = 5 };

		Controls.AddRange([message, grid, ok, skip, again, showIssues, cover]);
	}

	public static SeriesChoice Ask(IWin32Window? owner, PluginContext ctx, IComicVine comicVine, Configuration config, SeriesQuestion question, CoverPreferences covers, CancellationToken ct)
	{
		if (question.Candidates.Count == 0)
		{
			throw new ArgumentException("The series dialog needs at least one series.", nameof(question));
		}
		using var form = new SeriesForm(ctx, comicVine, config, question);
		DialogResult result = form.ShowModal(owner, ct);
		SeriesRef? selected = form.grid.SelectedModel is int i ? question.Candidates[i].Series : null;
		switch (result)
		{
			case DialogResult.OK when selected is not null:
				if (form.cover.PreferredCover is var (issue, url))
				{
					covers.Prefer(issue, url);
				}
				return new SeriesChoice.Pick(selected, ShowIssues: false);
			case DialogResult.Yes when selected is not null:
				return new SeriesChoice.Pick(selected, ShowIssues: true);
			case DialogResult.Retry:
				return new SeriesChoice.SearchAgain();
			case DialogResult.Ignore when form.SkipForever:
				return new SeriesChoice.SkipForever();
			case DialogResult.Ignore:
				return new SeriesChoice.Skip();
			default:
				return new SeriesChoice.Cancel();
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		SelectionChanged();
	}

	private void SelectionChanged()
	{
		int? selected = grid.SelectedModel;
		cover.ShowSeries(selected is int i ? question.Candidates[i].Series : null);
		ok.Enabled = selected is not null;
		showIssues.Enabled = selected is not null;
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
