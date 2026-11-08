using System.Text.RegularExpressions;

namespace ComicVineScraper.ComicVine;

internal static class ApiKeyRedaction
{
	private static readonly Regex KeyParameter = new("api_key=[^&]*", RegexOptions.CultureInvariant);

	/// <summary>dberrors.py's rule (api_key=... in the query), plus the key itself wherever else it appears.</summary>
	public static string Redact(string text, string apiKey)
	{
		string redacted = KeyParameter.Replace(text, "api_key=...");
		return apiKey.Length > 0 ? redacted.Replace(apiKey, "...") : redacted;
	}
}
