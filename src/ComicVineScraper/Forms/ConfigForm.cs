using System;
using System.Windows.Forms;
using ComicVineScraper.Config;

namespace ComicVineScraper.Plugin;

internal sealed class ConfigForm : ScraperForm
{
	private ConfigForm(PluginContext ctx) : base(ctx, "configform") { }

	public static Configuration? Show(IWin32Window? owner, PluginContext ctx) => throw new NotImplementedException();
}
