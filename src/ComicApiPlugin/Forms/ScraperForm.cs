using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// CVForm and PersistentForm in one: no minimize or maximize box, no icon or taskbar entry, a fixed border, Esc closes,
/// Ctrl+Shift+L saves the debug log, ComicRack's theme, and the location (and size, when asked) kept in geometry.dat.
/// </summary>
internal abstract class ScraperForm : Form
{
	private readonly string? geometryKey;
	private readonly bool persistSize;
	private bool loaded;
	private bool boundsChanged;
	private CancellationToken cancel;
	private Button? skip;
	private string skipText = "";
	private bool controlHeld;
	private bool skippedForever;

	/// <param name="geometryKey">The geometry.dat prefix, such as "searchform", or null for a form that always opens centred.</param>
	protected ScraperForm(PluginContext ctx, string? geometryKey, bool persistSize = false)
	{
		Ctx = ctx;
		this.geometryKey = geometryKey;
		this.persistSize = persistSize;
		AutoScaleDimensions = new SizeF(6F, 13F);
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowIcon = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		KeyPreview = true;
	}

	protected PluginContext Ctx { get; }

	protected Strings Strings => Ctx.Strings;

	/// <summary>"Comic Vine Scraper - v1.2.3". Python hard-coded the English name; FinishFormTitle is the same text, localized.</summary>
	protected string ProductTitle => Strings.Format(TextKeys.FinishFormTitle, typeof(ScraperForm).Assembly.GetName().Version.ToString(3));

	/// <summary>
	/// Whether Skip was clicked while it read "- Skip -" (Ctrl held). Taken at the click, because a closing dialog loses the
	/// focus before ShowDialog returns, and losing the focus releases Ctrl.
	/// </summary>
	protected bool SkipForever => skippedForever;

	/// <summary>
	/// ShowDialog that a cancelled token closes, the way Python's cancel listeners closed each dialog. The result is Cancel
	/// whenever the token was cancelled, even if the user also chose something.
	/// </summary>
	public DialogResult ShowModal(IWin32Window? owner, CancellationToken ct)
	{
		if (ct.IsCancellationRequested)
		{
			return DialogResult.Cancel;
		}
		cancel = ct;
		DialogResult result;
		using (ct.Register(() =>
		{
			if (IsHandleCreated)
			{
				try
				{
					BeginInvoke(new Action(CloseAsCancelled));
				}
				catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
				{
					// The handle went away between the check and the call: the dialog is already closing.
				}
			}
		}))
		{
			result = owner is null ? ShowDialog() : ShowDialog(owner);
		}
		return ct.IsCancellationRequested ? DialogResult.Cancel : result;
	}

	/// <summary>Makes the button the form's Skip button, whose label shows when Ctrl is held.</summary>
	protected void UseSkipButton(Button button, string textKey)
	{
		skip = button;
		skipText = Strings[textKey];
		button.Text = skipText;
		button.Click += (_, _) => skippedForever = controlHeld;
	}

	/// <summary>Cut, Copy, and Paste in ComicRack's language, where Windows would show its own.</summary>
	protected ContextMenuStrip EditMenu(TextBoxBase box)
	{
		var menu = new ContextMenuStrip();
		menu.Items.Add(Strings[TextKeys.TextCut], null, (_, _) => box.Cut());
		menu.Items.Add(Strings[TextKeys.TextCopy], null, (_, _) => box.Copy());
		menu.Items.Add(Strings[TextKeys.TextPaste], null, (_, _) => box.Paste());
		return menu;
	}

	/// <summary>Where the form goes when geometry.dat has no location for it.</summary>
	protected virtual void PlaceByDefault() => CenterToParent();

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		Ctx.CurrentTheme()?.ApplyTheme(this);
		RestoreGeometry();
		loaded = true;
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		if (cancel.IsCancellationRequested)
		{
			CloseAsCancelled();
		}
	}

	protected override void OnMove(EventArgs e)
	{
		base.OnMove(e);
		boundsChanged |= loaded;
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		boundsChanged |= loaded;
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		if (boundsChanged && geometryKey is not null)
		{
			Ctx.Settings.LoadGeometry().Set(geometryKey, Location, persistSize ? Size : null);
		}
		base.OnFormClosed(e);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Escape:
				Close();
				return true;
			case Keys.Control | Keys.Shift | Keys.L:
				LogFile.SaveWithDialog(this, Ctx, confirm: false);
				return true;
			default:
				return base.ProcessCmdKey(ref msg, keyData);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.KeyCode == Keys.ControlKey)
		{
			HoldControl(true);
		}
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
		if (e.KeyCode == Keys.ControlKey)
		{
			HoldControl(false);
		}
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		HoldControl(false);
	}

	private void HoldControl(bool held)
	{
		if (skip is null || controlHeld == held)
		{
			return;
		}
		controlHeld = held;
		skip.Text = held ? "- " + skipText + " -" : skipText;
	}

	private void CloseAsCancelled() => DialogResult = DialogResult.Cancel;

	private void RestoreGeometry()
	{
		(Point? location, Size? size) = geometryKey is null ? (null, null) : Ctx.Settings.LoadGeometry().Get(geometryKey);
		if (persistSize && size is { } stored)
		{
			Size = stored;
		}
		if (location is { } at)
		{
			Location = at;
		}
		else
		{
			PlaceByDefault();
		}
		Rectangle screens = Screen.AllScreens.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
		var centre = new Point(Left + Width / 2, Top + Height / 2);
		if (screens.Contains(centre))
		{
			boundsChanged = false;
			return;
		}
		Ctx.Log.Debug("WARNING: form's location was offscreen; adjusted it");
		CenterToScreen();
		boundsChanged = true;
	}
}
