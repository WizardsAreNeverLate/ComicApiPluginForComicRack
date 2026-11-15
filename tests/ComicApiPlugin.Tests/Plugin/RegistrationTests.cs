using System.Threading.Tasks;
using ComicApiPlugin.Plugin;
using cYo.Projects.ComicRack.Plugins;

namespace ComicApiPlugin.Tests.Plugin;

public sealed class RegistrationTests
{
	[Test]
	public async Task ComicRack_registers_the_plugin_under_its_permanent_key()
	{
		DotNetPlugin plugin = new ComicApiCommands();

		await Assert.That(plugin.Key).IsEqualTo("comic-api-net");
		await Assert.That(plugin.Name).IsEqualTo("Comic API Plugin");
		await Assert.That(plugin.Image).IsEqualTo("comicvinescraper.png");
	}

	[Test]
	public async Task The_plugin_scrapes_from_the_book_list_and_the_book_editor_and_has_a_configure_action()
	{
		var plugin = new ComicApiCommands();

		await Assert.That(plugin is IBooks).IsTrue();
		await Assert.That(plugin is IEditor).IsTrue();
		await Assert.That(plugin is IConfig).IsTrue();
	}
}
