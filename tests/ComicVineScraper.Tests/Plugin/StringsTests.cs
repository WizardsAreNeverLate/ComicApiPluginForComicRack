using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicVineScraper.Tests.Plugin;

public sealed class StringsTests
{
	[Test]
	public async Task Backslash_n_sequences_in_the_bundled_defaults_become_real_newlines()
	{
		var strings = ComicVineScraper.Plugin.Strings.Load(new FakeEnvironment(FindLanguagesFolder()));

		string text = strings["SeriesSearchFailedText"];

		await Assert.That(text).Contains("search terms:\n\n     '{0}'\n\n");
		await Assert.That(text).DoesNotContain("\\n");
	}

	private static string FindLanguagesFolder()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, "src", "ComicVineScraper", "Package", "Languages");
			if (File.Exists(Path.Combine(candidate, "en.zip")))
			{
				return candidate;
			}
		}
		throw new DirectoryNotFoundException("src/ComicVineScraper/Package/Languages/en.zip was not found above " + AppContext.BaseDirectory);
	}

	private sealed class FakeEnvironment(string commandPath) : IPluginEnvironment
	{
		public IEnumerable<string> LibraryPaths => [];
		public IWin32Window MainWindow => null!;
		public IApplication App => null!;
		public IOpenBooksManager OpenBooks => null!;
		public IBrowser Browser => null!;
		public IComicDisplay ComicDisplay => null!;
		public string CommandPath { get; set; } = commandPath;
		public IThemePlugin Theme { get; set; } = null!;
		public string Localize(string resourceKey, string elementKey, string text) => text;
		public object Clone() => this;
	}
}
