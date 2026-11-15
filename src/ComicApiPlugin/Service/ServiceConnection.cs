using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ComicApiPlugin.Plugin;

/// <summary>The plugin's HTTP connection to the local API service: one HttpClient for each launch of the service, not one for each call.</summary>
internal sealed class ServiceConnection(ServiceHost host) : IDisposable
{
	private readonly object gate = new();
	private (ServiceEndpoint Endpoint, HttpClient Client)? current;

	/// <summary>A client for the running service, which is started first when it is not running. The caller does not dispose it.</summary>
	/// <exception cref="ServiceUnavailableException">The service could not be started.</exception>
	public async Task<HttpClient> ConnectAsync(CancellationToken ct)
	{
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(ct).ConfigureAwait(false);
		lock (gate)
		{
			if (current is { } live && live.Endpoint == endpoint)
			{
				return live.Client;
			}
			// The service was started again, so the old client's port and token are dead.
			current?.Client.Dispose();
			// No timeout: a search can page for minutes, and cancelling the scrape cancels the request.
			HttpClient client = endpoint.CreateClient(Timeout.InfiniteTimeSpan);
			current = (endpoint, client);
			return client;
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			current?.Client.Dispose();
			current = null;
		}
	}
}
