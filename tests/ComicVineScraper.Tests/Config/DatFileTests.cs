using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ComicVineScraper.Config;

namespace ComicVineScraper.Tests.Config;

public sealed class DatFileTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	[Test]
	[Arguments("True", true)]
	[Arguments("true", true)]
	[Arguments("FALSE", false)]
	[Arguments("fAlSe", false)]
	public async Task Reads_booleans_in_any_case(string text, bool expected)
	{
		await Assert.That(DatFile.Parse("k : " + text)["k"]).IsEqualTo(new DatValue.Bool(expected));
	}

	[Test]
	[Arguments("5", 5L)]
	[Arguments("+5", 5L)]
	[Arguments("-5", -5L)]
	[Arguments("0005", 5L)]
	[Arguments("- 5", -5L)]
	[Arguments("2147483648", 2147483648L)]
	[Arguments("٣", 3L)]
	public async Task Reads_whole_numbers_as_ints(string text, long expected)
	{
		await Assert.That(DatFile.Parse("k : " + text)["k"]).IsEqualTo(new DatValue.Int(expected));
	}

	[Test]
	[Arguments("1.5", 1.5)]
	[Arguments("1.", 1.0)]
	[Arguments(".5", 0.5)]
	[Arguments("-.5", -0.5)]
	[Arguments("1.5e3", 1500.0)]
	[Arguments("٣.٥", 3.5)]
	public async Task Reads_text_with_a_dot_as_a_float(string text, double expected)
	{
		await Assert.That(DatFile.Parse("k : " + text)["k"]).IsEqualTo(new DatValue.Float(expected));
	}

	[Test]
	public async Task Reads_an_overflowing_float_as_infinity()
	{
		await Assert.That(DatFile.Parse("a : 1.5e400\nb : -1.5e400"))
			.IsEquivalentTo(new Dictionary<string, DatValue> { ["a"] = new DatValue.Float(double.PositiveInfinity), ["b"] = new DatValue.Float(double.NegativeInfinity) });
	}

	[Test]
	[Arguments("1.2.3")]
	[Arguments(".")]
	[Arguments("nan")]
	[Arguments("nan.")]
	[Arguments("1e5")]
	[Arguments("0x10")]
	[Arguments("1_0")]
	[Arguments("5L")]
	[Arguments("1 000")]
	[Arguments("1,5")]
	[Arguments("abc")]
	[Arguments("")]
	[Arguments("99999999999999999999")]
	public async Task Keeps_other_values_as_text(string text)
	{
		await Assert.That(DatFile.Parse("k : " + text)["k"]).IsEqualTo(new DatValue.Text(text));
	}

	[Test]
	public async Task Skips_lines_that_do_not_split_into_two_parts_and_trims_the_rest()
	{
		IReadOnlyDictionary<string, DatValue> map = DatFile.Parse(":: This file was generated on 2024.01.31 13:45:10\n\nnocolon\na : b : c\n  spaced key   :   spaced value  \r\n:no key\nk : 1\nk : 2");

		await Assert.That(map).IsEquivalentTo(new Dictionary<string, DatValue>
		{
			["spaced key"] = new DatValue.Text("spaced value"),
			[""] = new DatValue.Text("no key"),
			["k"] = new DatValue.Int(2),
		});
	}

	[Test]
	public async Task Writes_a_header_then_sorted_key_value_lines()
	{
		var map = new Dictionary<string, DatValue>
		{
			["updateSeries"] = new DatValue.Bool(true),
			["apiKey"] = new DatValue.Text("abc123"),
			["Zed"] = new DatValue.Int(-7),
			["ignoreBlanks"] = new DatValue.Bool(false),
			["ratio"] = new DatValue.Float(1.0 / 3),
		};

		await Assert.That(DatFile.Format(map, Now)).IsEqualTo(
			":: This file was generated on 2024.01.31 13:45:10\n\n" +
			"Zed : -7\napiKey : abc123\nignoreBlanks : False\nratio : 0.333333333333\nupdateSeries : True\n");
	}

	[Test]
	public async Task Drops_entries_whose_key_or_value_contains_a_colon_and_trims_the_rest()
	{
		var map = new Dictionary<string, DatValue>
		{
			["url"] = new DatValue.Text("http://example.com"),
			["a:b"] = new DatValue.Text("x"),
			[" padded "] = new DatValue.Text(" value "),
		};

		await Assert.That(DatFile.Format(map, Now)).IsEqualTo(":: This file was generated on 2024.01.31 13:45:10\n\npadded : value\n");
	}

	[Test]
	public async Task Writes_UTF8_with_a_BOM_and_reads_it_back()
	{
		var map = new Dictionary<string, DatValue> { ["kéy"] = new DatValue.Text("välüé"), ["n"] = new DatValue.Int(18436) };

		byte[] bytes = DatFile.ToBytes(map, Now);

		await Assert.That(bytes.Take(3).ToArray()).IsEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
		await Assert.That(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)).IsEqualTo(DatFile.Format(map, Now));
		await Assert.That(DatFile.FromBytes(bytes)).IsEquivalentTo(map);
	}
}
