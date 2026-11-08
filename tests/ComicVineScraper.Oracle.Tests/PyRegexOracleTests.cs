using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Config;
using ComicVineScraper.Text;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>PyRegex against IronPython's re.compile and re.match.</summary>
[NotInParallel("python-resources")]
public sealed class PyRegexOracleTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	[Test]
	public async Task Patterns_compile_and_match_as_in_Python()
	{
		var mismatches = new List<string>();
		foreach (string pattern in AdvancedCorpus.Regexes)
		{
			bool python = PythonConfig.Compiles(pattern);
			var csharp = PyRegex.TryCompile(pattern, Timeout);
			if (python != (csharp is not null))
			{
				mismatches.Add($"compile {pattern}: python {python}, C# {csharp is not null}");
				continue;
			}
			if (csharp is null)
			{
				continue;
			}
			foreach (string sample in AdvancedCorpus.RegexSamples)
			{
				string expected = PythonConfig.GroupsOf(pattern, sample);
				string actual = PythonConfig.CSharpGroups(csharp, sample);
				if (expected != actual)
				{
					mismatches.Add($"match {pattern} on {sample}: python {expected}, C# {actual}");
				}
			}
		}

		await Assert.That(mismatches.Take(8)).IsEmpty();
	}

	[Test]
	public async Task The_corpus_has_the_non_ASCII_names_and_the_odd_flag_groups()
	{
		await Assert.That(AdvancedCorpus.RegexSamples).Contains("Café 12").And.Contains("Ünïcode 7").And.Contains("漫画 3");
		foreach (string pattern in new[] { "(?u)", "(?L)", "(?ui)", "a{,2}", @"\N" })
		{
			await Assert.That(AdvancedCorpus.Regexes.Any(r => r.Contains(pattern))).IsTrue().Because(pattern);
		}
	}

	[Test]
	public async Task AdvancedParser_compiles_the_alt_search_regex_with_a_match_timeout()
	{
		var parsed = AdvancedSettings.Parse("ALT_SEARCH_REGEX=(?P<series>.+)");

		await Assert.That(parsed.AltSearchRegex!.MatchTimeout).IsNotEqualTo(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout);
	}
}
