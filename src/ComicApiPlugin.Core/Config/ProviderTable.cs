using System;
using System.Collections.Generic;
using System.Linq;

namespace ComicApiPlugin.Config;

public enum ProviderKind
{
	ComicVine,
	VerseDb,
}

/// <summary>What the user can ask a scrape to do. Each provider offers one today.</summary>
public enum ScrapeAction
{
	ComicVineQuery,
	VerseDbCoverSearch,
}

/// <summary>A provider's switch. Priority is the position in Configuration.Providers.</summary>
public sealed record ProviderSetting(ProviderKind Kind, bool Enabled);

public static class ProviderTable
{
	public static IReadOnlyList<ProviderKind> All { get; } = [ProviderKind.ComicVine, ProviderKind.VerseDb];

	public static ProviderList Default { get; } = new(All.Select(kind => new ProviderSetting(kind, true)));

	public static ScrapeAction ActionOf(ProviderKind kind) => kind switch
	{
		ProviderKind.ComicVine => ScrapeAction.ComicVineQuery,
		ProviderKind.VerseDb => ScrapeAction.VerseDbCoverSearch,
		_ => throw new ArgumentOutOfRangeException(nameof(kind)),
	};

	/// <summary>Keeps the first of each known kind, then appends the kinds that are missing, enabled, in default order.</summary>
	public static ProviderList Normalize(IEnumerable<ProviderSetting> settings)
	{
		List<ProviderSetting> kept = [];
		foreach (ProviderSetting setting in settings)
		{
			if (Enum.IsDefined(typeof(ProviderKind), setting.Kind) && kept.All(k => k.Kind != setting.Kind))
			{
				kept.Add(setting);
			}
		}
		kept.AddRange(Default.Where(d => kept.All(k => k.Kind != d.Kind)));
		return new ProviderList(kept);
	}
}
