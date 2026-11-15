using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Plugin;

/// <summary>The plugin's HTTP connection to the local API service: one HttpClient for each launch of the service, not one for each call.</summary>
internal sealed class ServiceConnection(ServiceHost host) : IDisposable
{
	private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

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

	/// <summary>Sends one request to the service, which is started first when it is not running, and reads the answer.</summary>
	/// <param name="request">Makes the request: its path, its headers and its body.</param>
	/// <param name="read">Reads the answer to a request that succeeded.</param>
	/// <param name="failure">
	/// The exception for a request that the service answered with an error status and this body, or null when the body
	/// names no failure the caller knows. The call then throws ServiceUnavailableException.
	/// </param>
	/// <exception cref="OperationCanceledException">ct was cancelled.</exception>
	/// <exception cref="ServiceUnavailableException">The service could not be started or reached, or its answer cannot be read.</exception>
	public async Task<T> CallAsync<T>(
		Func<HttpRequestMessage> request, Func<HttpResponseMessage, Task<T>> read, Func<ErrorResponseDto, Exception?> failure, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		try
		{
			HttpClient client = await ConnectAsync(ct).ConfigureAwait(false);
			using HttpRequestMessage message = request();
			// HttpClient makes the address absolute when it sends the request.
			string path = message.RequestUri.OriginalString;
			// Without this, .NET Framework first waits for a "100 Continue" before it sends the body.
			message.Headers.ExpectContinue = false;
			using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			// .NET Framework reads a response body without looking at the token, so a cancel closes the response under the read.
			using (ct.Register(response.Dispose))
			{
				if (!response.IsSuccessStatusCode)
				{
					throw await FailureAsync(path, response, failure).ConfigureAwait(false);
				}
				return await read(response).ConfigureAwait(false);
			}
		}
		catch (Exception error) when (ct.IsCancellationRequested)
		{
			throw new OperationCanceledException("The call to the local API service was cancelled.", error, ct);
		}
		catch (Exception error) when (error is HttpRequestException or IOException or ObjectDisposedException or OperationCanceledException)
		{
			throw new ServiceUnavailableException("The local API service did not answer: " + error.Message, error);
		}
		catch (WireFormatException error)
		{
			throw new ServiceUnavailableException("The local API service gave an answer this plugin cannot read: " + error.Message, error);
		}
	}

	public static HttpContent Json<T>(T body) where T : class => new StringContent(WireJson.ToJson(body), Utf8, WireJson.ContentType);

	/// <summary>A multipart/form-data body with one part for each name, which is how an image goes to the service.</summary>
	public static HttpContent Multipart(params (string Name, byte[] Bytes)[] parts)
	{
		var form = new MultipartFormDataContent();
		foreach ((string name, byte[] bytes) in parts)
		{
			form.Add(new ByteArrayContent(bytes), name);
		}
		return form;
	}

	public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response) where T : class =>
		WireJson.FromJson<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

	public void Dispose()
	{
		lock (gate)
		{
			current?.Client.Dispose();
			current = null;
		}
	}

	/// <summary>The exception for a response that is not a success: the provider's failure as the service reported it, or the service's own.</summary>
	private static async Task<Exception> FailureAsync(string path, HttpResponseMessage response, Func<ErrorResponseDto, Exception?> failure)
	{
		string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		ErrorResponseDto? error = body.Length > 0 ? WireJson.FromJson<ErrorResponseDto>(body) : null;
		return (error is null ? null : failure(error))
			?? new ServiceUnavailableException($"The local API service answered {path} with HTTP {(int)response.StatusCode}" + (error?.Message is { } message ? ": " + message : "."));
	}
}
