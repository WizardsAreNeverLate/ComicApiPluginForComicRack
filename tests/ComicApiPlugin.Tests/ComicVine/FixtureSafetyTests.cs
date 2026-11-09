using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Tests.ComicVine;

public sealed class FixtureSafetyTests
{
	[Test]
	public async Task No_fixture_holds_an_api_key()
	{
		string[] files = Directory.GetFiles(FixtureFiles.OutputFolder);
		var keyShape = new Regex(@"(?<![0-9a-fA-F])[0-9a-fA-F]{40}(?![0-9a-fA-F])");

		string[] leaking = files.Where(file => keyShape.IsMatch(File.ReadAllText(file))).Select(Path.GetFileName).ToArray()!;

		await Assert.That(files.Length).IsGreaterThan(20);
		await Assert.That(leaking).IsEmpty();
	}

	[Test]
	public async Task Every_fixture_parses_and_names_a_redacted_Comic_Vine_url()
	{
		var responses = FixtureFiles.ReadFolder(FixtureFiles.OutputFolder);

		await Assert.That(responses.All(r => r.Url.StartsWith("https://comicvine.gamespot.com/api/") && r.Url.Contains("?api_key=REDACTED&"))).IsTrue();
		await Assert.That(responses.Select(r => FixtureFiles.Key(r.Url)).Distinct().Count()).IsEqualTo(responses.Count);
	}
}
