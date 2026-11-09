using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.Config;

/// <summary>A value in a utils.persist_map file. Parse converts like Python's load_map: bool (any case), float if it has '.', int, else text.</summary>
public abstract record DatValue
{
	private DatValue() { }

	public sealed record Bool(bool Value) : DatValue;

	public sealed record Int(long Value) : DatValue;

	public sealed record Float(double Value) : DatValue;

	public sealed record Text(string Value) : DatValue;

	/// <summary>The value as utils.sstr writes it.</summary>
	public string ToFileText() => this switch
	{
		Bool b => b.Value ? "True" : "False",
		Int i => i.Value.ToString(CultureInfo.InvariantCulture),
		Float f => PyStr.Format(f.Value),
		Text t => t.Value,
		_ => throw new InvalidOperationException(),
	};
}

/// <summary>The utils.persist_map and utils.load_map format of settings.dat, series.dat, and geometry.dat.</summary>
public static class DatFile
{

	/// <summary>Lines that do not split into exactly two parts on ':' are skipped; both parts are trimmed. A later key wins.</summary>
	public static IReadOnlyDictionary<string, DatValue> Parse(string text)
	{
		var map = new Dictionary<string, DatValue>();
		using var reader = new StringReader(text);
		for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
		{
			string[] pair = line.Trim().Split(':');
			if (pair.Length == 2)
			{
				map[pair[0].Trim()] = ParseValue(pair[1].Trim());
			}
		}
		return map;
	}

	/// <summary>
	/// ":: This file was generated on yyyy.MM.dd HH:mm:ss", a blank line, then "key : value" lines sorted by key.
	/// An entry whose trimmed key or value contains ':' is dropped, because it could not be read back.
	/// </summary>
	public static string Format(IReadOnlyDictionary<string, DatValue> map, DateTime now)
	{
		var text = new StringBuilder(":: This file was generated on ")
			.Append(now.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture))
			.Append("\n\n");
		foreach (KeyValuePair<string, DatValue> entry in map.OrderBy(e => e.Key, StringComparer.Ordinal))
		{
			string key = entry.Key.Trim();
			string value = entry.Value.ToFileText().Trim();
			if (key.IndexOf(':') < 0 && value.IndexOf(':') < 0)
			{
				text.Append(key).Append(" : ").Append(value).Append('\n');
			}
		}
		return text.ToString();
	}

	/// <summary>The file bytes: UTF-8 with a BOM, as .NET's Encoding.UTF8 wrote them for Python.</summary>
	public static byte[] ToBytes(IReadOnlyDictionary<string, DatValue> map, DateTime now) =>
		[.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Format(map, now))];

	/// <summary>Reads file bytes as Python's UTF-8 StreamReader did: a leading BOM is dropped, and nothing else is detected.</summary>
	public static IReadOnlyDictionary<string, DatValue> FromBytes(byte[] bytes)
	{
		byte[] bom = Encoding.UTF8.GetPreamble();
		int start = bytes.Length >= bom.Length && bytes.Take(bom.Length).SequenceEqual(bom) ? bom.Length : 0;
		return Parse(Encoding.UTF8.GetString(bytes, start, bytes.Length - start));
	}

	private static DatValue ParseValue(string value)
	{
		string lowered = PyStr.Lower(value);
		if (lowered == "false")
		{
			return new DatValue.Bool(false);
		}
		if (lowered == "true")
		{
			return new DatValue.Bool(true);
		}
		if (value.IndexOf('.') >= 0)
		{
			return PyStr.TryParseFloat(value, out double number) ? new DatValue.Float(number) : new DatValue.Text(value);
		}
		return PyStr.TryParseInt(value, out long integer) ? new DatValue.Int(integer) : new DatValue.Text(value);
	}
}
