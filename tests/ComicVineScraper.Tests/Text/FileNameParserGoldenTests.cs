using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Text;

namespace ComicVineScraper.Tests.Text;

/// <summary>Every case in the Python project's tests/test_fnameparser.data, read the way test_fnameparser.py reads it.</summary>
public sealed class FileNameParserGoldenTests
{
	public static IEnumerable<Func<(string FileName, string Series, string Number, string Year)>> GoldenCases()
	{
		foreach (string raw in File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Golden", "test_fnameparser.data"), Encoding.UTF8))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}
			string quote = line.StartsWith("\"", StringComparison.Ordinal) ? "\"" : "'";
			string[] data = Regex.Matches(line, quote + "(.*?)" + quote).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
			if (data.Length == 3)
			{
				data = [.. data, ""];
			}
			if (data.Length != 4)
			{
				throw new InvalidDataException("badly formatted test data: " + line);
			}
			yield return () => (data[0], data[1], data[2], data[3]);
		}
	}

	[Test]
	public async Task The_golden_file_has_every_case()
	{
		await Assert.That(GoldenCases().Count()).IsEqualTo(218);
	}

	[Test]
	[MethodDataSource(nameof(GoldenCases))]
	public async Task Extracts_the_series_number_and_year(string fileName, string series, string number, string year)
	{
		await Assert.That(FileNameParser.Extract(fileName)).IsEqualTo(new ParsedName(series, number, year));
	}
}
