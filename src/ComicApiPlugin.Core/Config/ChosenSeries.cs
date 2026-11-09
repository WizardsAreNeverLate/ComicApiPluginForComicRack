using System;
using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Config;

/// <summary>series.dat: the series the user has picked before, which MatchScore rewards. Record writes through at once, like record_choice.</summary>
public sealed class ChosenSeries
{
	private readonly SettingsStore store;
	private readonly HashSet<string> keys;

	internal ChosenSeries(SettingsStore store)
	{
		this.store = store;
		keys = new HashSet<string>(store.ReadMap(SettingsStore.SeriesFileName).Keys, StringComparer.Ordinal);
	}

	public bool Contains(SeriesId id)
	{
		lock (keys)
		{
			return keys.Contains(id.ToString());
		}
	}

	/// <summary>No-op if present; otherwise adds the series and rewrites series.dat.</summary>
	public void Record(SeriesId id)
	{
		lock (keys)
		{
			if (keys.Add(id.ToString()))
			{
				store.Locked(() => store.WriteMap(SettingsStore.SeriesFileName, keys.ToDictionary(k => k, k => (DatValue)new DatValue.Text(k))));
			}
		}
	}
}
