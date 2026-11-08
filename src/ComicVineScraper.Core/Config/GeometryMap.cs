using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using ComicVineScraper.Text;

namespace ComicVineScraper.Config;

/// <summary>geometry.dat: "{form}Location" = "X,Y" and "{form}Size" = "W,H". Each call reads or rewrites the file, as PersistentForm does.</summary>
public sealed class GeometryMap
{
	private readonly SettingsStore store;

	internal GeometryMap(SettingsStore store) => this.store = store;

	/// <summary>A part that is missing or does not read as two integers is null.</summary>
	public (Point? Location, Size? Size) Get(string formKey)
	{
		IReadOnlyDictionary<string, DatValue> map = store.ReadMap(SettingsStore.GeometryFileName);
		return (Pair(map, formKey + "Location") is var (x, y) ? new Point(x, y) : null,
			Pair(map, formKey + "Size") is var (w, h) ? new Size(w, h) : null);
	}

	public void Set(string formKey, Point location, Size? size) => store.Locked(() =>
	{
		Dictionary<string, DatValue> map = store.ReadMap(SettingsStore.GeometryFileName).ToDictionary(e => e.Key, e => e.Value);
		map[formKey + "Location"] = new DatValue.Text(Format(location.X, location.Y));
		if (size is { } s)
		{
			map[formKey + "Size"] = new DatValue.Text(Format(s.Width, s.Height));
		}
		store.WriteMap(SettingsStore.GeometryFileName, map);
	});

	private static string Format(int a, int b) => a.ToString(CultureInfo.InvariantCulture) + "," + b.ToString(CultureInfo.InvariantCulture);

	private static (int, int)? Pair(IReadOnlyDictionary<string, DatValue> map, string key)
	{
		if (!map.TryGetValue(key, out DatValue? value) || value is not DatValue.Text text)
		{
			return null;
		}
		return ParsePair(text.Value);
	}

	/// <summary>Two integers separated by a comma, as PersistentForm reads them. Null where Python's split, int(), or index would raise.</summary>
	internal static (int, int)? ParsePair(string text)
	{
		string[] parts = text.Split(',');
		return parts.Length >= 2 && PyStr.TryParseInt(parts[0], out long a) && PyStr.TryParseInt(parts[1], out long b)
			&& a is >= int.MinValue and <= int.MaxValue && b is >= int.MinValue and <= int.MaxValue
			? ((int)a, (int)b)
			: null;
	}
}