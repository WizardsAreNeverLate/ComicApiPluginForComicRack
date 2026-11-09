using System.Threading.Tasks;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Tests.Config;

public sealed class ConfigurationToStringTests
{
	private const string Key = "0123456789abcdef0123456789abcdef01234567";

	[Test]
	public async Task ToString_does_not_print_the_API_key()
	{
		string text = (new Configuration { Advanced = AdvancedSettings.Parse(""), ApiKey = Key }).ToString();

		await Assert.That(text).DoesNotContain(Key);
		await Assert.That(text).DoesNotContain("0123456789abcdef");
		await Assert.That(text).Contains("ApiKey = <redacted>");
		await Assert.That(text).Contains("OverwriteExisting = True");
	}

	[Test]
	public async Task ToString_says_when_there_is_no_key()
	{
		string text = (new Configuration { Advanced = AdvancedSettings.Parse("") }).ToString();

		await Assert.That(text).Contains("ApiKey = <none>");
	}
}
