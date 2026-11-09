using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ComicVineScraper.Diagnostics;

namespace ComicVineScraper.Plugin;

/// <summary>The prompt half of log.handle_error. The code that caught the error logs it, once, before it calls Show.</summary>
internal static class ErrorDialogs
{
	public static void Show(IWin32Window? owner, PluginContext ctx, Exception error)
	{
		ErrorPrompt prompt = ErrorPrompt.For(error);
		string text = prompt.DatabaseName is { } database ? ctx.Strings.Format(prompt.TextKey, database) : ctx.Strings[prompt.TextKey];
		string caption = ctx.Strings[prompt.TitleKey];
		if (!prompt.OffersLogSave)
		{
			PromptDialog.Show(owner, ctx, text, caption, MessageBoxIcon.Warning, PromptButtons.Ok);
		}
		else if (PromptDialog.Show(owner, ctx, text, caption, MessageBoxIcon.Error, PromptButtons.YesNo) == DialogResult.Yes)
		{
			LogFile.SaveWithDialog(owner, ctx, confirm: true);
		}
	}
}

/// <summary>log.save: the user picks a file, and every debug log line is written to it.</summary>
internal static class LogFile
{
	/// <param name="confirm">Tell the user the log was saved; the error prompt does, Ctrl+Shift+L does not.</param>
	public static void SaveWithDialog(IWin32Window? owner, PluginContext ctx, bool confirm)
	{
		using var dialog = new SaveFileDialog
		{
			FileName = ErrorPrompt.LogFileName(DateTime.Today),
			Title = ctx.Strings[ErrorPrompt.SaveTitle],
			Filter = ctx.Strings[ErrorPrompt.SaveFilter] + "|*.*",
		};
		if (dialog.ShowDialog(owner) == DialogResult.OK && dialog.FileName.Length > 0)
		{
			Save(owner, ctx, dialog.FileName, confirm);
		}
	}

	public static void Save(IWin32Window? owner, PluginContext ctx, string path, bool confirm)
	{
		try
		{
			ctx.Log.Debug("wrote debug logfile: " + Path.GetFileName(path));
			using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
			{
				ctx.Log.SaveTo(writer);
			}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			ctx.Log.Error(error);
			PromptDialog.Show(owner, ctx, ctx.Strings[ErrorPrompt.SaveFailedText], "", MessageBoxIcon.None, PromptButtons.Ok);
			return;
		}
		if (confirm)
		{
			PromptDialog.Show(owner, ctx, ctx.Strings[ErrorPrompt.SavedText], ctx.Strings[ErrorPrompt.SavedTitle], MessageBoxIcon.Information, PromptButtons.Ok);
		}
	}
}
