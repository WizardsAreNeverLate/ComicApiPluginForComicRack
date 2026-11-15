namespace ComicApiPlugin.Service;

/// <summary>
/// The contract between the plugin, which starts the local API service, and the service. Both sides compile this file, so a
/// name cannot differ between them.
/// </summary>
public static class ServiceProtocol
{
	/// <summary>The environment variable that carries the per-launch bearer token. Never a command-line argument, which other processes can read.</summary>
	public const string TokenVariable = "COMICAPI_SERVICE_TOKEN";

	public const string ParentPidArgument = "--parent-pid";

	public const string LogFolderArgument = "--log-dir";

	/// <summary>The service writes this prefix and a JSON object as one stdout line once it accepts requests: {"port":n,"pid":n,"version":"v"}.</summary>
	public const string ReadyPrefix = "COMICAPI-READY ";

	public const string LoopbackHost = "127.0.0.1";

	public const string HealthPath = "/health";

	public const string ShutdownPath = "/shutdown";

	public const string ComicVineKeyHeader = "X-ComicVine-Key";

	public const string VerseDbTokenHeader = "X-VerseDb-Token";

	/// <summary>Names the run a request belongs to. The service keeps one set of Comic Vine caches for each run and writes the name in its log.</summary>
	public const string RunIdHeader = "X-Run-Id";

	public const string ComicVineSearchPath = "/comicvine/search";

	public const string ComicVineIssuesPath = "/comicvine/issues";

	public const string ComicVineFindIssuePath = "/comicvine/find-issue";

	public const string ComicVineIssuePath = "/comicvine/issue";

	public const string ComicVineResolveSeriesPath = "/comicvine/resolve-series";

	public const string ComicVineImagePath = "/comicvine/image";

	public const string VerseDbLookupCoverPath = "/versedb/lookup-cover";

	public const string VerseDbIssuePath = "/versedb/issue";

	public const string VerseDbImagePath = "/versedb/image";

	public const string CoverComparePath = "/covers/compare";

	/// <summary>The multipart/form-data part of a lookup-cover request that holds the cover image.</summary>
	public const string ImagePart = "image";

	/// <summary>The two parts of a compare request: the book's own cover, and the cover it is compared with.</summary>
	public const string BookCoverPart = "book";

	public const string CandidateCoverPart = "candidate";

	/// <summary>
	/// For tests only: the service sends its Comic Vine API requests to this base URL and not to comicvine.gamespot.com, so a
	/// test can answer them from a local server. It must end with a slash.
	/// </summary>
	public const string ComicVineBaseUrlVariable = "COMICAPI_COMICVINE_BASE_URL";

	/// <summary>
	/// For tests only: the service sends its VerseDB API requests to this base URL and not to versedb.com. A cover picture is
	/// always downloaded from the URL that VerseDB gave.
	/// </summary>
	public const string VerseDbBaseUrlVariable = "COMICAPI_VERSEDB_BASE_URL";

	/// <summary>Where the .NET runtime that the service needs is downloaded.</summary>
	public const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/11.0";
}