using System.Collections.Generic;

namespace ComicApiPlugin.Config;

/// <summary>The advanced.dat settings that shape search results. Computed by Configuration.ToSearchSettings. Phase 3b owns the code that uses it.</summary>
public sealed record SearchSettings(
	IReadOnlyList<string> IgnoredSearchTerms,
	IReadOnlyList<string> IgnoredPublishers, // lowercase
	int IgnoreBeforeYear,
	int IgnoreAfterYear,
	int NeverIgnoreThreshold,
	int MaxSearchResults);                   // clamped 10..5000
