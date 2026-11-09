using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Tests.Plugin.Forms;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicApiPlugin.Tests.Plugin;

public sealed class PluginContextTests
{
	[Test]
	public async Task The_theme_is_read_from_the_environment_each_time_not_when_the_context_opens()
	{
		var env = new ThemedEnvironment();
		PluginContext ctx = PluginContext.Open(env, Path.Combine(Path.GetTempPath(), "cvs-ctx-" + Guid.NewGuid().ToString("N")), new ManualClock());
		var light = new NamedTheme();
		var dark = new NamedTheme();

		env.Theme = light;
		IThemePlugin? first = ctx.CurrentTheme();
		env.Theme = dark;
		IThemePlugin? second = ctx.CurrentTheme();

		await Assert.That(first).IsSameReferenceAs(light);
		await Assert.That(second).IsSameReferenceAs(dark);
	}

	private sealed class NamedTheme : IThemePlugin
	{
		public void ApplyTheme(Control control)
		{
		}

		public cYo.Common.Windows.Forms.Theme.UIComponent UIComponent => throw new NotSupportedException();
		public cYo.Common.Windows.Forms.Theme.Themes CurrentTheme => throw new NotSupportedException();
		public bool IsDarkModeEnabled => false;
		public ToolStripRenderer ToolStripRenderer => throw new NotSupportedException();
	}

	private sealed class ThemedEnvironment : IPluginEnvironment
	{
		public IEnumerable<string> LibraryPaths => [];
		public IWin32Window MainWindow => null!;
		public IApplication App => null!;
		public IOpenBooksManager OpenBooks => null!;
		public IBrowser Browser => null!;
		public IComicDisplay ComicDisplay => null!;
		public string CommandPath { get; set; } = LanguagePack.Folder;
		public IThemePlugin Theme { get; set; } = null!;
		public string Localize(string resourceKey, string elementKey, string text) => text;
		public object Clone() => this;
	}
}
