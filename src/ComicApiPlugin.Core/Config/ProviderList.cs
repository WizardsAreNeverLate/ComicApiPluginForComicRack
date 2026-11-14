using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ComicApiPlugin.Config;

/// <summary>The providers in priority order. Compares by content, so Configuration equality still means something.</summary>
public sealed class ProviderList : IReadOnlyList<ProviderSetting>, System.IEquatable<ProviderList>
{
	private readonly ProviderSetting[] items;

	public ProviderList(IEnumerable<ProviderSetting> settings) => items = [.. settings];

	public int Count => items.Length;

	public ProviderSetting this[int index] => items[index];

	public bool Equals(ProviderList? other) => other is not null && items.SequenceEqual(other.items);

	public override bool Equals(object? obj) => Equals(obj as ProviderList);

	public override int GetHashCode() => items.Aggregate(17, (hash, item) => hash * 31 + item.GetHashCode());

	public IEnumerator<ProviderSetting> GetEnumerator() => ((IEnumerable<ProviderSetting>)items).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
