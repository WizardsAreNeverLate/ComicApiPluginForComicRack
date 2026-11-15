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

	/// <summary>Names the scrape run a request belongs to. The service keeps one set of Comic Vine caches for each run.</summary>
	public const string RunIdHeader = "X-Run-Id";

	public const string ComicVineSearchPath = "/comicvine/search";

	public const string ComicVineIssuesPath = "/comicvine/issues";

	public const string ComicVineFindIssuePath = "/comicvine/find-issue";

	public const string ComicVineIssuePath = "/comicvine/issue";

	public const string ComicVineResolveSeriesPath = "/comicvine/resolve-series";

	public const string ComicVineImagePath = "/comicvine/image";

	/// <summary>
	/// For tests only: the service sends its Comic Vine API requests to this base URL and not to comicvine.gamespot.com, so a
	/// test can answer them from a local server. It must end with a slash.
	/// </summary>
	public const string ComicVineBaseUrlVariable = "COMICAPI_COMICVINE_BASE_URL";

	/// <summary>Where the .NET runtime that the service needs is downloaded.</summary>
	public const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/11.0";
}