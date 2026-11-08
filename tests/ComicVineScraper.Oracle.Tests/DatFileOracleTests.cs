using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Config;
using ComicVineScraper.Text;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>DatFile against utils.persist_map and utils.load_map, in both directions and byte for byte.</summary>
public sealed class DatFileOracleTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	private static readonly string[] ValueCorpus =
	[
		"True", "true", "FALSE", "fAlSe", "True ", "tRUE ",
		"5", "+5", "-5", "0005", "- 5", "+ 5", "5L", "5l", "2147483647", "2147483648", "-2147483648", "-2147483649",
		"9223372036854775807", "-9223372036854775808",
		"1.5", "1.", ".5", "-.5", "+.5e-3", "1.5e3", "1.5E+3", "-0.0", "-0.", "12.0", "0.1",
		"1.5e400", "-1.5e400", "1.5e-400", "1e5.", "Infinity.", "NaN.", "nan.", "inf.",
		"1.2.3", ".", "nan", "inf", "-inf", "1e5", "0x10", "1_0", "1 000", "1,5", "1.5j", "1.5\0",
		"٣", "٣٣", "٣.٥", "۳", "۳.۵", "３", "०", "³",
		"abc", "", " x y ", " x ", "᠎x᠎", "​x", "\u001cx\u001f", "\u0085x\u0085", " em ", "välüé",
		"\U0001F600", "a\tb",
	];

	[Test]
	public async Task Parse_matches_load_map_on_the_value_corpus()
	{
		string text = ":: This file was generated on 2024.01.31 13:45:10\n\n"
			+ string.Join("\n", ValueCorpus.Select((value, i) => "k" + i.ToString("000", CultureInfo.InvariantCulture) + " : " + value))
			+ "\nnocolon\na : b : c\n  spaced key   :   spaced value  \n:empty key\nempty value:\r\nkéy : v\r\rlast:7";

		await AssertParseMatches(text);
	}

	[Test]
	public async Task Parse_matches_load_map_on_random_lines()
	{
		const string alphabet = "0123456789012345.-+eE:: \t  \r\nTrueFalsenaifx٠٣٩۳";
		var random = new Random(20261109);
		for (int file = 0; file < 20; file++)
		{
			var text = new StringBuilder();
			for (int line = 0; line < 200; line++)
			{
				text.Append('k').Append(line).Append(" : ");
				int length = random.Next(0, 12);
				for (int i = 0; i < length; i++)
				{
					text.Append(alphabet[random.Next(alphabet.Length)]);
				}
				text.Append('\n');
			}
			await AssertParseMatches(text.ToString());
		}
	}

	[Test]
	public async Task Python_reads_back_what_CSharp_writes()
	{
		string path = TempFile();
		try
		{
			File.WriteAllBytes(path, DatFile.ToBytes(CleanMap(), Now));

			await Assert.That(Tags(PythonLoad(path))).IsEquivalentTo(Tags(CleanMap()));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task CSharp_reads_back_what_Python_writes()
	{
		string path = TempFile();
		try
		{
			PythonPersist(CleanMap(), path);

			await Assert.That(Tags(DatFile.FromBytes(File.ReadAllBytes(path)))).IsEquivalentTo(Tags(CleanMap()));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task Files_match_byte_for_byte_after_the_header_line()
	{
		Dictionary<string, DatValue> map = CleanMap().ToDictionary(e => e.Key, e => e.Value);
		map["url"] = new DatValue.Text("http://example.com");
		map["a:b"] = new DatValue.Text("x");
		map[" padded "] = new DatValue.Text(" value ");
		map["third"] = new DatValue.Float(1.0 / 3);
		map["\U0001F600 emoji"] = new DatValue.Text("\U0001F600");
		map["￯"] = new DatValue.Text("high");
		string path = TempFile();
		try
		{
			PythonPersist(map, path);
			byte[] python = File.ReadAllBytes(path);
			byte[] csharp = DatFile.ToBytes(map, Now);

			await Assert.That(python.Take(3).ToArray()).IsEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
			await Assert.That(HeaderLine(python)).Matches(new Regex(@"^:: This file was generated on \d{4}\.\d{2}\.\d{2} \S.*$"));
			await Assert.That(HeaderLine(csharp)).IsEqualTo(":: This file was generated on 2024.01.31 13:45:10");
			await Assert.That(Convert.ToBase64String(AfterHeader(csharp))).IsEqualTo(Convert.ToBase64String(AfterHeader(python)));
			await Assert.That(Encoding.UTF8.GetString(AfterHeader(csharp))).IsEqualTo(Encoding.UTF8.GetString(AfterHeader(python)));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task Format_matches_str_of_float()
	{
		var random = new Random(20261109);
		var values = new List<double>
		{
			0.0, -0.0, 1.0, -1.0, 0.1, 0.5, 1.5, 100.0, 1e15, 1e16, 1e-4, 9.9999e-5, 1e-5, 1e100, 1e-100, 5e-324, double.MaxValue, double.Epsilon,
			123456789012.0, 1234567890123.0, 999999999999.5, 999999999999.4, 0.30000000000000004, 1.0 / 3, 2.0 / 3, 3.14159265358979,
			-0.0004999, -0.00001, -0.4, 0.00099999999999, 0.0099999999999, 0.099999999999, 0.999999999999, 0.9999999999995, 9.9999999999995,
			double.NaN, double.PositiveInfinity, double.NegativeInfinity,
		};
		for (int i = 0; i < 2000; i++)
		{
			values.Add(BitConverter.Int64BitsToDouble((long)(random.NextDouble() * long.MaxValue) * (random.Next(2) == 0 ? 1 : -1)));
			values.Add(Math.Round(random.NextDouble() * Math.Pow(10, random.Next(-6, 16)), random.Next(0, 15)));
			values.Add((random.Next(1, 1000000) + 0.5) / Math.Pow(10, random.Next(0, 13)));
		}
		var expected = (object[])Oracle.Call("__builtin__", "eval", "[str(x) for x in xs]", new Dictionary<string, object> { ["xs"] = values })!;

		string[] mismatches = values.Select((v, i) => (v, python: (string)expected[i], csharp: PyStr.Format(v)))
			.Where(r => r.python != r.csharp)
			.Select(r => $"{r.v:R}: python {r.python}, C# {r.csharp}")
			.ToArray();

		await Assert.That(mismatches).IsEmpty();
	}

	[Test]
	public async Task Whole_numbers_beyond_long_stay_text_where_Python_returns_a_long()
	{
		string path = TempFile();
		try
		{
			File.WriteAllText(path, "big : 9223372036854775808\n", new UTF8Encoding(true));

			await Assert.That(Tags(PythonLoad(path))["big"]).IsEqualTo("int:9223372036854775808");
			await Assert.That(Tags(DatFile.FromBytes(File.ReadAllBytes(path)))["big"]).IsEqualTo("text:9223372036854775808");
		}
		finally
		{
			File.Delete(path);
		}
	}

	private static async Task AssertParseMatches(string text)
	{
		string path = TempFile();
		try
		{
			File.WriteAllText(path, text, new UTF8Encoding(true));

			await Assert.That(Tags(DatFile.FromBytes(File.ReadAllBytes(path)))).IsEquivalentTo(Tags(PythonLoad(path)));
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>A map that survives a write and a read unchanged in both implementations.</summary>
	private static IReadOnlyDictionary<string, DatValue> CleanMap() => new Dictionary<string, DatValue>
	{
		["apiKey"] = new DatValue.Text("0123456789abcdef"),
		["updateSeries"] = new DatValue.Bool(true),
		["ignoreBlanks"] = new DatValue.Bool(false),
		["Zed"] = new DatValue.Int(-7),
		["18436"] = new DatValue.Int(18436),
		["big"] = new DatValue.Int(2147483648),
		["comicformLocation"] = new DatValue.Text("100,200"),
		["half"] = new DatValue.Float(0.5),
		["whole"] = new DatValue.Float(12.0),
		["kéy"] = new DatValue.Text("välüé"),
		["empty"] = new DatValue.Text(""),
		["with space"] = new DatValue.Text("two words"),
	};

	private static IReadOnlyDictionary<object, object?> PythonLoad(string path) => (Dictionary<object, object?>)Oracle.Call("utils", "load_map", path)!;

	private static void PythonPersist(IReadOnlyDictionary<string, DatValue> map, string path)
	{
		var python = map.ToDictionary(e => e.Key, e => ToPythonValue(e.Value));
		if (!(bool)Oracle.Call("utils", "persist_map", python, path)!)
		{
			throw new InvalidOperationException("persist_map failed");
		}
	}

	private static object ToPythonValue(DatValue value) => value switch
	{
		DatValue.Bool b => b.Value,
		DatValue.Int i when i.Value is >= int.MinValue and <= int.MaxValue => (int)i.Value,
		DatValue.Int i => new BigInteger(i.Value),
		DatValue.Float f => f.Value,
		DatValue.Text t => t.Value,
		_ => throw new ArgumentOutOfRangeException(nameof(value)),
	};

	private static Dictionary<string, string> Tags(IReadOnlyDictionary<string, DatValue> map) => map.ToDictionary(e => e.Key, e => Tag(ToPythonValue(e.Value)));

	private static Dictionary<string, string> Tags(IReadOnlyDictionary<object, object?> map) => map.ToDictionary(e => (string)e.Key, e => Tag(e.Value));

	private static string Tag(object? value) => value switch
	{
		bool b => "bool:" + b,
		int or BigInteger => "int:" + Convert.ToString(value, CultureInfo.InvariantCulture),
		double d => "float:" + BitConverter.DoubleToInt64Bits(d).ToString("x16", CultureInfo.InvariantCulture),
		string s => "text:" + s,
		_ => "other:" + value?.GetType().FullName,
	};

	private static string HeaderLine(byte[] file)
	{
		string text = Encoding.UTF8.GetString(file, 3, file.Length - 3);
		return text.Substring(0, text.IndexOf('\n'));
	}

	private static byte[] AfterHeader(byte[] file) => file.Skip(Array.IndexOf(file, (byte)'\n') + 1).ToArray();

	private static string TempFile() => Path.Combine(Path.GetTempPath(), "cvs-oracle-" + Guid.NewGuid().ToString("N") + ".dat");
}
