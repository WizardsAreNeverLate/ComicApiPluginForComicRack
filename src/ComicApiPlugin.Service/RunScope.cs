using System.Threading;

namespace ComicApiPlugin.Service;

/// <summary>The scrape run the current request belongs to, for the log lines written below the endpoint.</summary>
internal static class RunScope
{
	private static readonly AsyncLocal<string?> Run = new();

	public static string? Current
	{
		get => Run.Value;
		set => Run.Value = value;
	}
}
