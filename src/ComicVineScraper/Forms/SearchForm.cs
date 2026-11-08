using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>Asks for search terms, and after a failed search says which terms found nothing.</summary>
internal sealed class SearchForm : ScraperForm
{
	private readonly TextBox terms;

	private SearchForm(PluginContext ctx, string initialTerms, string? failedTerms) : base(ctx, "searchform")
	{
		bool failed = !string.IsNullOrWhiteSpace(failedTerms);
		int shift = failed ? 100 : 0;
		Text = Strings[failed ? TextKeys.SeriesSearchFailedTitle : TextKeys.SearchFormTitle];
		ClientSize = new Size(435, 100 + shift);

		var failure = new Label
		{
			Name = "failure",
			UseMnemonic = false,
			Location = new Point(10, 10),
			Size = new Size(415, 100),
			Visible = failed,
			Text = failed ? Strings.Format(TextKeys.SeriesSearchFailedText, failedTerms!) : "",
		};
		var prompt = new Label { Name = "prompt", UseMnemonic = false, Location = new Point(10, 10 + shift), Size = new Size(415, 20), Text = Strings[TextKeys.SearchFormText] };
		Button search = MakeButton("search", TextKeys.SearchFormSearch, 125, 70 + shift, 100, DialogResult.OK, 1);
		Button skip = MakeButton("skip", TextKeys.SearchFormSkip, 235, 70 + shift, 90, DialogResult.Ignore, 2);
		Button cancel = MakeButton("cancel", TextKeys.SearchFormCancel, 335, 70 + shift, 90, DialogResult.Cancel, 3);
		UseSkipButton(skip, TextKeys.SearchFormSkip);
		terms = new TextBox { Name = "terms", Location = new Point(10, 35 + shift), Size = new Size(415, 20), TabIndex = 0, Text = failed ? failedTerms! : initialTerms };
		terms.ContextMenuStrip = EditMenu(terms);
		terms.TextChanged += (_, _) => search.Enabled = terms.Text.Trim().Length > 0;
		search.Enabled = terms.Text.Trim().Length > 0;
		terms.SelectAll();
		AcceptButton = search;
		CancelButton = cancel;
		Controls.AddRange([failure, prompt, terms, search, skip, cancel]);
	}

	public static SearchChoice Ask(IWin32Window? owner, PluginContext ctx, string initialTerms, string? failedTerms, CancellationToken ct)
	{
		using var form = new SearchForm(ctx, initialTerms, failedTerms);
		return form.ShowModal(owner, ct) switch
		{
			DialogResult.OK => new SearchChoice.Search(form.terms.Text.Trim()),
			DialogResult.Ignore when form.SkipForever => new SearchChoice.SkipForever(),
			DialogResult.Ignore => new SearchChoice.Skip(),
			_ => new SearchChoice.Cancel(),
		};
	}

	private Button MakeButton(string name, string textKey, int x, int y, int width, DialogResult result, int tabIndex) => new()
	{
		Name = name,
		Text = Strings[textKey],
		Location = new Point(x, y),
		Size = new Size(width, 23),
		DialogResult = result,
		TabIndex = tabIndex,
		UseVisualStyleBackColor = true,
	};
}
