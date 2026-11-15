using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ComicApiPlugin.Plugin;

/// <summary>Where the running service answers, and the token every request to it must carry.</summary>
internal sealed record ServiceEndpoint(Uri BaseAddress, string Token, int Pid)
{
	/// <summary>A client that talks to the service directly, never through the system proxy.</summary>
	public HttpClient CreateClient(TimeSpan timeout)
	{
		var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = BaseAddress, Timeout = timeout };
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
		return client;
	}

	// The token must not reach a log through ToString.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("BaseAddress = ").Append(BaseAddress).Append(", Pid = ").Append(Pid);
		return true;
	}
}

/// <summary>The service's lifecycle as the plugin sees it. Exactly one case holds at a time.</summary>
internal abstract record ServiceState
{
	private ServiceState()
	{
	}

	public sealed record NotStarted : ServiceState;

	/// <summary>One start is in flight, and every caller waits on it.</summary>
	public sealed record Starting(Task<ServiceEndpoint> Start) : ServiceState;

	/// <summary>The service reported ready. The process may have exited since, and the next start then replaces it.</summary>
	public sealed record Running(ServiceEndpoint Endpoint, Process Process) : ServiceState;

	/// <summary>The last start failed. The next call starts again.</summary>
	public sealed record Failed(string Reason) : ServiceState;

	/// <summary>The plugin stopped the service. A later call starts it again, because another plugin can cancel ComicRack's close.</summary>
	public sealed record Stopped : ServiceState;
}

/// <summary>The service could not be started or reached. The message is fit to show the user.</summary>
internal sealed class ServiceUnavailableException(string reason) : Exception(reason);
