using System;
using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.ComicVine;

namespace ComicApiPlugin.Service.ComicVine;

/// <summary>
/// The Comic Vine state the service keeps between requests, all of it in memory. Each API key has one transport for the
/// life of the service, so every run that uses a key shares its request throttle. Each run has its own catalog, and so
/// its own caches, which the service drops when the run has sent no request for <see cref="IdleLimit"/>.
/// </summary>
/// <param name="connect">Makes the transport for an API key.</param>
internal sealed class ComicVineSessions(Func<string, ComicVineHttp> connect, TimeProvider time) : IDisposable
{
	public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

	private readonly object gate = new();
	private readonly Dictionary<string, ComicVineHttp> transports = new(StringComparer.Ordinal);
	private readonly Dictionary<RunKey, Run> runs = [];

	/// <summary>The catalog of one run. The settings are part of its identity, because the catalog filters with them.</summary>
	/// <exception cref="WireFormatException">The settings lack a member.</exception>
	public ComicVineCatalog Catalog(string apiKey, string runId, SearchSettingsDto settings)
	{
		var key = new RunKey(apiKey, runId, ComicVineWire.ToJson(settings));
		DateTimeOffset now = time.GetUtcNow();
		lock (gate)
		{
			foreach (RunKey idle in runs.Where(run => now - run.Value.LastUsed > IdleLimit).Select(run => run.Key).ToList())
			{
				runs.Remove(idle);
			}
			if (!runs.TryGetValue(key, out Run? run))
			{
				if (!transports.TryGetValue(apiKey, out ComicVineHttp? transport))
				{
					transports[apiKey] = transport = connect(apiKey);
				}
				runs[key] = run = new Run(new ComicVineCatalog(transport, ComicVineWire.ToDomain(settings)));
			}
			run.LastUsed = now;
			return run.Catalog;
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			foreach (ComicVineHttp transport in transports.Values)
			{
				transport.Dispose();
			}
			transports.Clear();
			runs.Clear();
		}
	}

	private readonly record struct RunKey(string ApiKey, string RunId, string Settings);

	private sealed class Run(ComicVineCatalog catalog)
	{
		public ComicVineCatalog Catalog { get; } = catalog;

		public DateTimeOffset LastUsed { get; set; }
	}
}
