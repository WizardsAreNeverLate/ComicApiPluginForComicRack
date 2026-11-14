using System;
using System.Net;

namespace ComicApiPlugin.VerseDb;

/// <summary>The only exception IVerseDb throws besides cancellation. The message never holds the API token.</summary>
public sealed class VerseDbException(HttpStatusCode? status, string message, TimeSpan? retryAfter = null, Exception? inner = null)
	: Exception(message, inner)
{
	/// <summary>Null when VerseDB was not reached or its answer could not be read.</summary>
	public HttpStatusCode? Status { get; } = status;

	/// <summary>How long a 429 asks the client to wait.</summary>
	public TimeSpan? RetryAfter { get; } = retryAfter;

	public bool IsRateLimited => Status == (HttpStatusCode)429;
}
