using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The settings dialog: the API key, the 21 fields to update, behaviour, data, and the advanced text. OK saves through
/// SettingsStore; Cancel saves nothing. It is resizable and keeps its size; it never gets smaller than the original layout.
/// </summary>
internal sealed class ConfigForm : ScraperForm
{
	private const string ApiKeyPage = "https://www.comicvine.gamespot.com/api";
	private const AnchorStyles AcrossTop = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
	private const AnchorStyles AllEdges = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
	private const AnchorStyles TopRight = AnchorStyles.Top | AnchorStyles.Right;
	private const AnchorStyles BottomRight = AnchorStyles.Bottom | AnchorStyles.Right;

	private readonly List<(Control Control, AnchorStyles Edges)> anchors = [];
	private readonly Button ok;
	private readonly TabControl tabs = new() { Name = "tabs", Location = new Point(10, 15), Size = new Size(395, 302), TabIndex = 3 };
	private readonly TabPage comicVineTab;
	private readonly TabPage detailsTab;
	private readonly TextBox apiKey = new() { Name = "apiKey", Location = new Point(34, 135), Size = new Size(287, 20), UseSystemPasswordChar = true };
	private readonly Button showKey = new() { Name = "showKey", AccessibleName = "Show or hide the API key", Location = new Point(325, 134), Size = new Size(24, 22), UseVisualStyleBackColor = true };
	private readonly CheckedListBox fields = new()
	{
		Name = "fields",
		CheckOnClick = true,
		ColumnWidth = 125,
		ThreeDCheckBoxes = true,
		Location = new Point(15, 65),
		MultiColumn = true,
		IntegralHeight = false,
		SelectionMode = SelectionMode.One,
		Size = new Size(260, 170),
	};
	private readonly CheckBox autochoose;
	private readonly CheckBox confirmIssue;
	private readonly CheckBox fastRescrape;
	private readonly CheckBox rescrapeNotes;
	private readonly CheckBox rescrapeTags;
	private readonly CheckBox summaryDialog;
	private readonly CheckBox convertImprints;
	private readonly CheckBox overwrite;
	private readonly CheckBox ignoreBlanks;
	private readonly CheckBox downloadThumbs;
	private readonly CheckBox preserveThumbs;
	private readonly RichTextBox advanced = new() { Name = "advanced", Multiline = true, MaxLength = 65536, WordWrap = true, Location = new Point(15, 50), Size = new Size(355, 200) };

	private ConfigForm(PluginContext ctx) : base(ctx, "configform", persistSize: true)
	{
		Text = Strings[TextKeys.ConfigFormTitle];
		FormBorderStyle = FormBorderStyle.Sizable;
		ClientSize = new Size(416, 375);

		ok = Pin(MakeButton("ok", TextKeys.ConfigFormOK, new Point(228, 343), 80, 0), BottomRight);
		ok.DialogResult = DialogResult.OK;
		Button cancel = Pin(MakeButton("cancel", TextKeys.ConfigFormCancel, new Point(315, 343), 90, 1), BottomRight);
		cancel.DialogResult = DialogResult.Cancel;
		Button restore = Pin(MakeButton("restore", TextKeys.ConfigFormRestore, new Point(10, 343), 170, 2), AnchorStyles.Bottom | AnchorStyles.Left);
		restore.Click += (_, _) => RestoreDefaults();
		Pin(tabs, AllEdges);

		autochoose = MakeCheckBox("autochoose", TextKeys.ConfigFormAutochooseSeriesCB, 82, 45, 300, 34);
		confirmIssue = MakeCheckBox("confirmIssue", TextKeys.ConfigFormConfirmIssueCB, 82, 75, 300, 34);
		fastRescrape = MakeCheckBox("fastRescrape", TextKeys.ConfigFormRescrapeCB, 52, 116, 300, 34);
		rescrapeNotes = MakeCheckBox("rescrapeNotes", TextKeys.ConfigFormRescrapeNotesCB, 82, 151, 270, 17);
		rescrapeTags = MakeCheckBox("rescrapeTags", TextKeys.ConfigFormRescrapeTagsCB, 82, 181, 270, 17);
		summaryDialog = MakeCheckBox("summaryDialog", TextKeys.ConfigFormShowSummaryCB, 52, 214, 300, 34);
		convertImprints = MakeCheckBox("convertImprints", TextKeys.ConfigFormImprintsCB, 52, 35, 300, 34);
		overwrite = MakeCheckBox("overwrite", TextKeys.ConfigFormOverwriteCB, 52, 85, 310, 34);
		ignoreBlanks = MakeCheckBox("ignoreBlanks", TextKeys.ConfigFormOverwriteEmptyCB, 82, 125, 270, 34);
		ignoreBlanks.TextAlign = ContentAlignment.TopLeft;
		downloadThumbs = MakeCheckBox("downloadThumbs", TextKeys.ConfigFormFilelessCB, 52, 165, 300, 34);
		preserveThumbs = MakeCheckBox("preserveThumbs", TextKeys.ConfigFormFilelessOverwriteCB, 82, 205, 270, 34);
		preserveThumbs.TextAlign = ContentAlignment.TopLeft;

		comicVineTab = BuildComicVineTab();
		detailsTab = BuildDetailsTab();
		tabs.TabPages.AddRange(
		[
			comicVineTab,
			detailsTab,
			Tab("behaviourTab", TextKeys.ConfigFormBehaviourTab,
				Pin(new Label { Name = "firstScrape", Text = Strings[TextKeys.ConfigFormFirstScrapeLabel], FlatStyle = FlatStyle.System, Location = new Point(52, 27), Size = new Size(300, 17) }, AcrossTop),
				autochoose, confirmIssue, fastRescrape, rescrapeTags, rescrapeNotes, summaryDialog),
			Tab("dataTab", TextKeys.ConfigFormDataTab, overwrite, ignoreBlanks, convertImprints, downloadThumbs, preserveThumbs),
			Tab("advancedTab", TextKeys.ConfigFormAdvancedTab, Description(TextKeys.ConfigFormAdvancedText, 14, 25), Pin(advanced, AllEdges)),
		]);
		advanced.ContextMenuStrip = EditMenu(advanced);

		Controls.AddRange([ok, cancel, restore, tabs]);
		apiKey.TextChanged += (_, _) => UpdateEnabled();
	}

	/// <summary>Shows the saved settings; on OK saves the user's choices and returns them. A failed save is reported, and the choices are still returned.</summary>
	public static Configuration? Show(IWin32Window? owner, PluginContext ctx)
	{
		ctx.Log.Debug("opened the settings dialog.");
		using var form = new ConfigForm(ctx);
		form.Display(ctx.Settings.Load());
		form.tabs.SelectedTab = form.apiKey.Text.Trim().Length > 0 ? form.detailsTab : form.comicVineTab;
		if (form.ShowModal(owner, CancellationToken.None) != DialogResult.OK)
		{
			ctx.Log.Debug("cancelled the settings dialog.");
			return null;
		}
		Configuration config = form.Chosen();
		if (ctx.Settings.Save(config).Error is { } error)
		{
			ErrorDialogs.Show(owner, ctx, error);
		}
		ctx.Log.Debug("closed the settings dialog.");
		return config;
	}

	private void Display(Configuration config)
	{
		for (int i = 0; i < FieldTable.Toggleable.Count; i++)
		{
			BookFields controls = FieldTable.Toggleable[i].Controls;
			fields.SetItemChecked(i, (config.Update & controls) == controls);
		}
		overwrite.Checked = config.OverwriteExisting;
		convertImprints.Checked = config.ConvertImprints;
		autochoose.Checked = config.Choose == ChooseMode.AutoChooseSeries;
		confirmIssue.Checked = config.Choose == ChooseMode.ConfirmIssue;
		ignoreBlanks.Checked = config.IgnoreBlanks;
		downloadThumbs.Checked = config.DownloadThumbs;
		preserveThumbs.Checked = config.PreserveThumbs;
		fastRescrape.Checked = config.FastRescrape;
		rescrapeNotes.Checked = config.Update.HasFlag(BookFields.Notes);
		rescrapeTags.Checked = config.Update.HasFlag(BookFields.Tags);
		summaryDialog.Checked = config.SummaryDialog;
		advanced.Text = config.Advanced.RawText;
		apiKey.Text = config.ApiKey.Trim();
		UpdateEnabled();
	}

	private Configuration Chosen()
	{
		BookFields update = FieldTable.Toggleable
			.Where((_, i) => fields.GetItemChecked(i))
			.Aggregate(BookFields.None, (all, row) => all | row.Controls);
		if (rescrapeNotes.Checked)
		{
			update |= BookFields.Notes;
		}
		if (rescrapeTags.Checked)
		{
			update |= BookFields.Tags;
		}
		return new Configuration
		{
			ApiKey = apiKey.Text.Trim(),
			Update = update,
			OverwriteExisting = overwrite.Checked,
			IgnoreBlanks = ignoreBlanks.Checked,
			ConvertImprints = convertImprints.Checked,
			Choose = autochoose.Checked ? ChooseMode.AutoChooseSeries : confirmIssue.Checked ? ChooseMode.ConfirmIssue : ChooseMode.Ask,
			DownloadThumbs = downloadThumbs.Checked,
			PreserveThumbs = preserveThumbs.Checked,
			FastRescrape = fastRescrape.Checked,
			SummaryDialog = summaryDialog.Checked,
			Advanced = AdvancedSettings.Parse(advanced.Text),
		};
	}

	private void RestoreDefaults()
	{
		string key = apiKey.Text;
		Display(new Configuration { Advanced = AdvancedSettings.Parse("") });
		apiKey.Text = key;
		Ctx.Log.Debug("all settings were restored to their default values");
	}

	private void UpdateEnabled()
	{
		ignoreBlanks.Enabled = overwrite.Checked;
		preserveThumbs.Enabled = downloadThumbs.Checked;
		if (confirmIssue.Checked)
		{
			autochoose.Checked = false;
		}
		if (autochoose.Checked)
		{
			confirmIssue.Checked = false;
		}
		confirmIssue.Enabled = !autochoose.Checked;
		autochoose.Enabled = !confirmIssue.Checked;
		ok.Enabled = apiKey.Text.Trim().Length > 0;
	}

	private TabPage BuildComicVineTab()
	{
		var link = new LinkLabel
		{
			Name = "register",
			UseMnemonic = false,
			AutoSize = false,
			Location = new Point(34, 170),
			Size = new Size(315, 34),
			Text = Strings[TextKeys.ConfigFormComicVineClickHere],
		};
		link.LinkClicked += (_, _) => Process.Start(ApiKeyPage);
		apiKey.ContextMenuStrip = EditMenu(apiKey);
		showKey.Paint += (_, e) => DrawEye(e.Graphics, showKey.ClientRectangle, showKey.ForeColor, crossed: !apiKey.UseSystemPasswordChar);
		showKey.Click += (_, _) =>
		{
			apiKey.UseSystemPasswordChar = !apiKey.UseSystemPasswordChar;
			showKey.Invalidate();
		};
		var text = new Label { Name = "comicVineText", UseMnemonic = false, AutoSize = false, Location = new Point(34, 80), Size = new Size(315, 54), Text = Strings[TextKeys.ConfigFormComicVineText] };
		return Tab("comicVineTab", TextKeys.ConfigFormComicVineTab, Pin(text, AcrossTop), Pin(apiKey, AcrossTop), Pin(showKey, TopRight), Pin(link, AcrossTop));
	}

	/// <summary>An eye, crossed out while the key shows, in the button's text colour so it follows ComicRack's theme.</summary>
	private static void DrawEye(Graphics g, Rectangle bounds, Color color, bool crossed)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		float size = Math.Min(bounds.Width, bounds.Height) * 0.62f;
		var centre = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
		float half = size / 2, lid = size / 2.6f;
		using var pen = new Pen(color, Math.Max(1f, size / 10f));
		using var outline = new GraphicsPath();
		outline.AddBezier(centre.X - half, centre.Y, centre.X - half / 2, centre.Y - lid, centre.X + half / 2, centre.Y - lid, centre.X + half, centre.Y);
		outline.AddBezier(centre.X + half, centre.Y, centre.X + half / 2, centre.Y + lid, centre.X - half / 2, centre.Y + lid, centre.X - half, centre.Y);
		g.DrawPath(pen, outline);
		using var pupil = new SolidBrush(color);
		float radius = size / 7;
		g.FillEllipse(pupil, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
		if (crossed)
		{
			g.DrawLine(pen, centre.X - half, centre.Y + size / 2.2f, centre.X + half, centre.Y - size / 2.2f);
		}
	}

	private TabPage BuildDetailsTab()
	{
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			fields.Items.Add(Strings[row.LabelTextKey]);
		}
		Button all = Pin(MakeButton("selectAll", TextKeys.ConfigFormDetailsAll, new Point(280, 107), 100, 0), TopRight);
		all.Click += (_, _) => CheckAll(true);
		Button none = Pin(MakeButton("selectNone", TextKeys.ConfigFormDetailsNone, new Point(280, 138), 100, 0), TopRight);
		none.Click += (_, _) => CheckAll(false);
		return Tab("detailsTab", TextKeys.ConfigFormDetailsTab, Description(TextKeys.ConfigFormDetailsText, 14, 35), all, none, Pin(fields, AllEdges));
	}

	private void CheckAll(bool check)
	{
		for (int i = 0; i < fields.Items.Count; i++)
		{
			fields.SetItemChecked(i, check);
		}
	}

	private TabPage Tab(string name, string textKey, params Control[] controls)
	{
		var page = new TabPage { Name = name, Text = Strings[textKey], UseVisualStyleBackColor = true };
		page.Controls.AddRange(controls);
		return page;
	}

	private Label Description(string textKey, int x, int y) =>
		new() { UseMnemonic = false, AutoSize = true, Location = new Point(x, y), Text = Strings[textKey] };

	private Button MakeButton(string name, string textKey, Point location, int width, int tabIndex) =>
		new() { Name = name, Text = Strings[textKey], Location = location, Size = new Size(width, 23), TabIndex = tabIndex, UseVisualStyleBackColor = true };

	private CheckBox MakeCheckBox(string name, string textKey, int x, int y, int width, int height)
	{
		var box = new CheckBox
		{
			Name = name,
			Text = Strings[textKey],
			AutoSize = false,
			FlatStyle = FlatStyle.System,
			Location = new Point(x, y),
			Size = new Size(width, height),
		};
		box.CheckedChanged += (_, _) => UpdateEnabled();
		return Pin(box, AcrossTop);
	}

	/// <summary>Records the edges a control follows when the dialog resizes; OnLoad applies them.</summary>
	private T Pin<T>(T control, AnchorStyles edges) where T : Control
	{
		anchors.Add((control, edges));
		return control;
	}

	protected override void OnLoad(EventArgs e)
	{
		// Anchors go on here, when every tab page has its real size. Set while building, they measure against TabPage's
		// placeholder size, and the controls on tabs not yet shown land outside their page.
		foreach ((Control control, AnchorStyles edges) in anchors)
		{
			control.Anchor = edges;
		}
		MinimumSize = SizeFromClientSize(ClientSize);
		base.OnLoad(e);
	}

	protected override void SizeByDefault() => Width += Width / 4;
}
