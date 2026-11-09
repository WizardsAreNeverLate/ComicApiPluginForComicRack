using System;
using System.Collections.Concurrent;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Engine;

/// <summary>The covers the user flipped to in a cover panel. Replaces session_data_map["key-altcover"]. The UI writes it and the engine reads it. Thread-safe.</summary>
public sealed class CoverPreferences
{
	private readonly ConcurrentDictionary<IssueId, Uri> covers = new();

	public void Prefer(IssueId issue, Uri cover) => covers[issue] = cover;

	public Uri? Preferred(IssueId issue) => covers.TryGetValue(issue, out Uri? cover) ? cover : null;
}
