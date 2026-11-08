using System;

namespace ComicVineScraper.ComicVine;

public enum ComicVineError
{
	/// <summary>Python code "0": unreachable, timeout, repeated bad responses.</summary>
	Network,

	/// <summary>status_code 100, also sent with HTTP 401.</summary>
	InvalidApiKey,

	/// <summary>status_code 107.</summary>
	RateLimited,

	/// <summary>Any other status_code; ApiStatus holds it.</summary>
	Api,
}

/// <summary>The only exception type IComicVine throws. Phase 3b makes sure Message and Url are redacted ("api_key=...").</summary>
public sealed class ComicVineException(ComicVineError error, string redactedUrl, int? apiStatus, Exception? inner)
	: Exception($"Comic Vine request failed ({error}): {redactedUrl}", inner)
{
	public ComicVineError Error { get; } = error;
	public string Url { get; } = redactedUrl;
	public int? ApiStatus { get; } = apiStatus;

	/// <summary>True when retrying the book later can help. False for a bad key or a rate limit, which end the run.</summary>
	public bool IsRecoverable => Error is ComicVineError.Network or ComicVineError.Api;
}
