using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>SeriesFilter, MatchScore, ImageHash, and CoverImage against dbutils.py, matchscore.py, imagehash.py, and utils.py.</summary>
public sealed class MatchingOracleTests
{
	private static readonly string[] BookSeries =
	[
		"", "Batman", "batman", "The Amazing Spider-Man", "X-Men", "Giant-Size X-Men", "Giant Sized X-Men", "King Size Hulk", "Hulk", "Thor's Saga",
		"One-Shot Wonder", "oneshot", "Batman Batman", "Swamp Thing", "Ποπ", "Les Mondes d'Aldébaran", "2000 AD", "100 Bullets", "S.H.I.E.L.D.",
	];

	private static readonly string[] Formats = ["", "", "Annual", "TPB", "Giant-Size", "One-Shot", "Special"];

	private static readonly string[] Numbers = ["", "1", "1.0", "12", "50", "99", "101", "150", "500", "0", "-1", "½", "5a", "#3", "1-2", "x", "٣", "1.2.3", "1e3", "..", "-"];

	private static readonly string[] SeriesNames =
	[
		"Batman", "The Batman", "Batman Beyond", "Amazing Spider-Man", "The Amazing Spider-Man", "X-Men", "Uncanny X-Men", "Giant-Size X-Men",
		"King-Size Hulk", "Incredible Hulk", "Thor", "Thors Saga", "One Shot Wonder", "Swamp Thing", "Ποπ", "Les Mondes d'Aldébaran", "2000 AD",
		"100 Bullets", "S.H.I.E.L.D.", "Series 7",
	];

	private static readonly string[] Publishers =
	[
		"", "DC Comics", "Marvel", "Panini Comics", "panini", "DeAgostina", "Marvel Italia", "MARVEL UK", "Marvel UK Ltd", "Semic_AS", "Abril", "abril jovem",
		"Image", "Dark Horse",
	];

	[Test]
	public async Task MatchScore_matches_compute_n()
	{
		int currentYear = DateTime.Now.Year;
		int[] years = [-1, 0, 1, 1899, 1900, 1901, 1950, 1990, 2004, 2011, currentYear, currentYear + 1, currentYear + 2];
		var random = new Random(9001);
		T Pick<T>(T[] items) => items[random.Next(items.Length)];
		int compared = 0;
		for (int i = 0; i < 3000; i++)
		{
			string series = Pick(BookSeries), format = Pick(Formats), number = Pick(Numbers);
			int published = Pick(years), released = Pick(years);
			int key = random.Next(1, 40), seriesYear = Pick(years), count = random.Next(0, 4) == 0 ? random.Next(100, 300) : random.Next(0, 120);
			string name = Pick(SeriesNames), publisher = Pick(Publishers);
			int[] prior = Enumerable.Range(1, 40).Where(_ => random.Next(4) == 0).ToArray();

			double python = Convert.ToDouble(OracleAdapters.Call("match_score",
				new object[] { series, format, number, published, released },
				new object?[] { key, name, seriesYear, publisher, count, null },
				prior.Select(k => (object)k.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()));

			var book = new BookValues { Series = series, Format = format, Number = number, Published = PartialDate.Of(published, null, null), Released = PartialDate.Of(released, null, null) };
			var seriesRef = new SeriesRef(new SeriesId(key), name, seriesYear < 0 ? null : seriesYear, publisher, count, null);
			double csharp = MatchScore.Compute(book, seriesRef, id => prior.Contains(id.Value), currentYear);
			await Assert.That(csharp).IsEqualTo(python).Because($"book ({series}|{format}|{number}|{published}|{released}) series ({key}|{name}|{seriesYear}|{publisher}|{count})");
			compared++;
		}
		await Assert.That(compared).IsEqualTo(3000);
	}

	[Test]
	public async Task SeriesFilter_keeps_the_series_filter_series_refs_keeps()
	{
		var random = new Random(515);
		int[] years = [-1, 1800, 1899, 1900, 1950, 1999, 2000, 2001, 2010, 2099, 2100];
		string[] publishers = ["", "DC Comics", "dc comics", "Marvel", "MARVEL", "Panini", "Image Comics", "Ποπ"];
		string[][] ignoredLists = [[], ["dc comics"], ["marvel", "panini"], ["image comics", "ποπ", ""], ["dc"]];
		int compared = 0;
		for (int i = 0; i < 400; i++)
		{
			var rows = Enumerable.Range(1, random.Next(0, 30))
				.Select(key => new object?[] { key, "Series", years[random.Next(years.Length)], publishers[random.Next(publishers.Length)], random.Next(0, 60), null })
				.ToArray();
			string[] ignored = ignoredLists[random.Next(ignoredLists.Length)];
			int before = years[random.Next(1, years.Length)], after = years[random.Next(1, years.Length)], threshold = random.Next(0, 70);

			int[] python = ((object[])OracleAdapters.Call("filter_series_keys", rows, ignored, before, after, threshold)!).Select(Convert.ToInt32).ToArray();

			SeriesRef[] found = rows.Select(r => new SeriesRef(new SeriesId((int)r[0]!), "Series", (int)r[2]! < 0 ? null : (int)r[2]!, (string)r[3]!, (int)r[4]!, null)).ToArray();
			var settings = new SearchSettings([], ignored, before, after, threshold, 100);
			int[] csharp = SeriesFilter.Apply(found, settings).Select(s => s.Id.Value).ToArray();
			await Assert.That(csharp).IsEquivalentTo(python, TUnit.Assertions.Enums.CollectionOrdering.Matching).Because($"case {i}");
			compared++;
		}
		await Assert.That(compared).IsEqualTo(400);
	}

	[Test]
	public async Task ImageHash_matches_imagehash_hash_on_generated_bitmaps()
	{
		int compared = 0;
		foreach (Func<Bitmap> make in GeneratedBitmaps())
		{
			using Bitmap image = make();
			ulong? python = ToHash(Oracle.Call("imagehash", "hash", image));
			await Assert.That(ImageHash.Compute(image)).IsEqualTo(python).Because($"{image.Width}x{image.Height} {image.PixelFormat}");
			compared++;
		}
		await Assert.That(compared).IsGreaterThan(60);
	}

	[Test]
	public async Task ImageHash_Similarity_matches_imagehash_similarity()
	{
		var random = new Random(64);
		ulong Next() => (ulong)random.Next() << 34 ^ (ulong)random.Next() << 17 ^ (ulong)random.Next();
		ulong?[] hashes = [null, 0, ulong.MaxValue, 1, 1UL << 63, .. Enumerable.Range(0, 40).Select(_ => (ulong?)Next())];
		foreach (ulong? a in hashes)
		{
			foreach (ulong? b in hashes)
			{
				double python = (double)Oracle.Call("imagehash", "similarity", ToPython(a), ToPython(b))!;
				await Assert.That(ImageHash.Similarity(a, b)).IsEqualTo(python).Because($"{a} {b}");
			}
		}
	}

	[Test]
	[Arguments(100, 100)]
	[Arguments(120, 100)]
	[Arguments(121, 100)]
	[Arguments(149, 100)]
	[Arguments(150, 100)]
	[Arguments(1301, 1000)]
	[Arguments(1300, 1000)]
	[Arguments(37, 29)]
	[Arguments(200, 150)]
	[Arguments(201, 150)]
	public async Task CoverImage_StripBackCover_matches_strip_back_cover_at_96_dpi(int width, int height)
	{
		using Bitmap source = Noise(width, height, PixelFormat.Format32bppArgb, width * 7919 + height);
		source.SetResolution(96, 96);
		using var pythonInput = new Bitmap(source);
		pythonInput.SetResolution(96, 96);
		using var csharpInput = new Bitmap(source);
		csharpInput.SetResolution(96, 96);

		using var python = (Bitmap)Oracle.Call("utils", "strip_back_cover", pythonInput)!;
		using Bitmap csharp = CoverImage.StripBackCover(csharpInput);

		await Assert.That(ReferenceEquals(csharp, csharpInput)).IsEqualTo(ReferenceEquals(python, pythonInput));
		await Assert.That(Pixels(csharp)).IsEquivalentTo(Pixels(python), TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task CoverImage_StripBackCover_keeps_the_right_half_where_Python_scales_by_dpi()
	{
		using Bitmap source = Noise(131, 100, PixelFormat.Format24bppRgb, 5);
		using var pythonInput = new Bitmap(source);
		pythonInput.SetResolution(300, 300);
		using var csharpInput = new Bitmap(source);
		csharpInput.SetResolution(300, 300);
		int[] rightHalf = Pixels(source, 66);

		using var python = (Bitmap)Oracle.Call("utils", "strip_back_cover", pythonInput)!;
		using Bitmap csharp = CoverImage.StripBackCover(csharpInput);

		await Assert.That(Pixels(python).SequenceEqual(rightHalf)).IsFalse();
		await Assert.That(Pixels(csharp)).IsEquivalentTo(rightHalf, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	private static IEnumerable<Func<Bitmap>> GeneratedBitmaps()
	{
		(int, int)[] sizes = [(1, 1), (2, 3), (8, 8), (9, 7), (13, 29), (64, 64), (100, 150), (300, 450), (640, 480), (1024, 77)];
		PixelFormat[] formats = [PixelFormat.Format32bppArgb, PixelFormat.Format24bppRgb, PixelFormat.Format32bppRgb, PixelFormat.Format64bppArgb];
		int seed = 0;
		foreach ((int w, int h) in sizes)
		{
			foreach (PixelFormat format in formats)
			{
				int s = ++seed;
				yield return () => Noise(w, h, format, s);
			}
			yield return () => Solid(w, h, Color.FromArgb(200, 10, 90));
			yield return () => Gradient(w, h);
			yield return () => Blocks(w, h);
		}
		yield return () => Solid(50, 50, Color.FromArgb(0, 255, 255, 255));
	}

	private static Bitmap Noise(int width, int height, PixelFormat format, int seed)
	{
		var random = new Random(seed);
		var image = new Bitmap(width, height, format);
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				image.SetPixel(x, y, Color.FromArgb(format == PixelFormat.Format32bppArgb ? random.Next(256) : 255, random.Next(256), random.Next(256), random.Next(256)));
			}
		}
		return image;
	}

	private static Bitmap Solid(int width, int height, Color color)
	{
		var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(color);
		return image;
	}

	private static Bitmap Gradient(int width, int height)
	{
		var image = new Bitmap(width, height, PixelFormat.Format24bppRgb);
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				image.SetPixel(x, y, Color.FromArgb(x * 255 / Math.Max(1, width - 1), y * 255 / Math.Max(1, height - 1), 128));
			}
		}
		return image;
	}

	private static Bitmap Blocks(int width, int height)
	{
		var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				image.SetPixel(x, y, (x * 3 / Math.Max(1, width) + y * 5 / Math.Max(1, height)) % 2 == 0 ? Color.Black : Color.White);
			}
		}
		return image;
	}

	private static int[] Pixels(Bitmap image, int fromX = 0)
	{
		var pixels = new List<int>();
		for (int x = fromX; x < image.Width; x++)
		{
			for (int y = 0; y < image.Height; y++)
			{
				pixels.Add(image.GetPixel(x, y).ToArgb());
			}
		}
		return [image.Width - fromX, image.Height, .. pixels];
	}

	private static ulong? ToHash(object? python) => python switch
	{
		null => null,
		BigInteger big => (ulong)big,
		_ => Convert.ToUInt64(python),
	};

	private static object? ToPython(ulong? hash) => hash is { } value ? new BigInteger(value) : null;
}
