using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Config;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>series.dat and geometry.dat as SettingsStore writes and reads them, against utils.load_map and persist_map.</summary>
public sealed class StoreFilesOracleTests
{
	[Test]
	public async Task Python_reads_the_series_keys_CSharp_records()
	{
		using var folder = new TempFolder();
		ChosenSeries chosen = Store(folder).LoadChosenSeries();
		int[] ids = [1, 7, 18436, 2147483647, 40, 0];
		foreach (int id in ids)
		{
			chosen.Record(new SeriesId(id));
		}

		var python = (Dictionary<object, object?>)Oracle.Call("utils", "load_map", Path.Combine(folder.Path, "series.dat"))!;

		await Assert.That(python.Keys.Select(k => (string)k).OrderBy(k => k, StringComparer.Ordinal)).IsEquivalentTo(ids.Select(i => i.ToString()).OrderBy(k => k, StringComparer.Ordinal));
	}

	[Test]
	public async Task CSharp_recognises_the_series_Python_records()
	{
		using var folder = new TempFolder();
		var keys = new Dictionary<string, string> { ["18436"] = "18436", ["7"] = "7" };
		Oracle.Call("utils", "persist_map", keys, Path.Combine(folder.Path, "series.dat"));

		ChosenSeries chosen = Store(folder).LoadChosenSeries();

		await Assert.That(chosen.Contains(new SeriesId(18436))).IsTrue();
		await Assert.That(chosen.Contains(new SeriesId(7))).IsTrue();
		await Assert.That(chosen.Contains(new SeriesId(8))).IsFalse();
	}

	[Test]
	public async Task Python_reads_the_geometry_CSharp_writes_and_the_reverse()
	{
		using var folder = new TempFolder();
		GeometryMap geometry = Store(folder).LoadGeometry();
		geometry.Set("comicform", new Point(-12, 340), new Size(640, 480));

		var python = (Dictionary<object, object?>)Oracle.Call("utils", "load_map", Path.Combine(folder.Path, "geometry.dat"))!;
		await Assert.That(python["comicformLocation"]).IsEqualTo("-12,340");
		await Assert.That(python["comicformSize"]).IsEqualTo("640,480");

		Oracle.Call("utils", "persist_map", new Dictionary<string, string> { ["pbformLocation"] = "5,6", ["pbformSize"] = "7,8" }, Path.Combine(folder.Path, "geometry.dat"));
		await Assert.That(geometry.Get("pbform")).IsEqualTo((new Point(5, 6), new Size(7, 8)));
	}

	private static SettingsStore Store(TempFolder folder) => new(folder.Path, new FixedClock());

	private sealed class FixedClock : IClock
	{
		public DateTime Now => new(2024, 1, 31, 13, 45, 10);

		public Task Delay(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
	}
}
