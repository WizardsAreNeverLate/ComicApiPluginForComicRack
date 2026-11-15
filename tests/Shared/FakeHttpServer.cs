using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ComicApiPlugin.Fixtures;

/// <summary>What a fake answers one request with.</summary>
public sealed record FakeAnswer(HttpStatusCode Status, byte[] Body, string ContentType)
{
	/// <summary>Response headers beyond the content type and length.</summary>
	public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

	public static FakeAnswer Xml(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status, Encoding.UTF8.GetBytes(body), "text/xml; charset=utf-8");

	public static FakeAnswer Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status, Encoding.UTF8.GetBytes(body), "application/json");
}

/// <summary>One request a fake received.</summary>
/// <param name="Target">The path and query exactly as sent, API key included.</param>
/// <param name="ReceivedAt">Time since the fake started.</param>
/// <param name="ClientGone">Completes when the client closes the connection before the fake has answered.</param>
public sealed record FakeRequest(string Target, TimeSpan ReceivedAt, Task ClientGone)
{
	public string Method { get; init; } = "GET";

	/// <summary>The request headers by name, whatever the case of the name.</summary>
	public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

	public byte[] Body { get; init; } = [];
}

/// <summary>
/// An HTTP server on a loopback port, so a service process and a client in the test process can both be pointed at it.
/// It speaks just enough HTTP/1.1 for HttpClient: one request per connection, with a body when Content-Length names one.
/// </summary>
public sealed class FakeHttpServer : IDisposable
{
	private readonly TcpListener listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource stopping = new();
	private readonly ConcurrentQueue<FakeRequest> requests = new();
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private readonly Func<FakeRequest, Task<FakeAnswer>> answer;

	public FakeHttpServer(Func<FakeRequest, Task<FakeAnswer>> answer)
	{
		this.answer = answer;
		listener.Start();
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		_ = Task.Run(AcceptAsync);
	}

	public int Port { get; }

	public IReadOnlyList<FakeRequest> Requests => requests.ToArray();

	public void Dispose()
	{
		stopping.Cancel();
		listener.Stop();
	}

	private async Task AcceptAsync()
	{
		while (!stopping.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
			}
			catch (Exception error) when (error is ObjectDisposedException or SocketException or InvalidOperationException)
			{
				return;
			}
			_ = Task.Run(() => ServeAsync(client));
		}
	}

	private async Task ServeAsync(TcpClient client)
	{
		using (client)
		{
			try
			{
				NetworkStream stream = client.GetStream();
				if (await ReadRequestAsync(stream).ConfigureAwait(false) is not var (method, target, headers, body))
				{
					return;
				}
				using var answered = new CancellationTokenSource();
				var request = new FakeRequest(target, clock.Elapsed, WatchForCloseAsync(client.Client, answered.Token)) { Method = method, Headers = headers, Body = body };
				requests.Enqueue(request);
				FakeAnswer response = await answer(request).ConfigureAwait(false);
				answered.Cancel();
				string extra = string.Concat(response.Headers.Select(header => $"{header.Key}: {header.Value}\r\n"));
				byte[] head = Encoding.ASCII.GetBytes(
					$"HTTP/1.1 {(int)response.Status} {response.Status}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {response.Body.Length}\r\n{extra}Connection: close\r\n\r\n");
				await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
				await stream.WriteAsync(response.Body, 0, response.Body.Length).ConfigureAwait(false);
			}
			catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
			{
				// The client went away. A test that cares waits on ClientGone.
			}
		}
	}

	/// <summary>Null when the client sent no request line.</summary>
	private static async Task<(string Method, string Target, Dictionary<string, string> Headers, byte[] Body)?> ReadRequestAsync(NetworkStream stream)
	{
		var received = new MemoryStream();
		byte[] buffer = new byte[16384];
		int headEnd;
		while ((headEnd = IndexOfHeadEnd(received)) < 0)
		{
			int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}
			received.Write(buffer, 0, read);
		}
		string[] lines = Encoding.ASCII.GetString(received.GetBuffer(), 0, headEnd < 0 ? (int)received.Length : headEnd).Split(["\r\n"], StringSplitOptions.None);
		string[] requestLine = lines[0].Split(' ');
		if (requestLine.Length < 2)
		{
			return null;
		}
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in lines.Skip(1))
		{
			int colon = line.IndexOf(':');
			if (colon > 0)
			{
				headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
			}
		}
		if (headers.TryGetValue("Expect", out string? expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
		{
			// .NET Framework waits for this before it sends the body of a POST.
			byte[] proceed = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
			await stream.WriteAsync(proceed, 0, proceed.Length).ConfigureAwait(false);
		}
		int length = headers.TryGetValue("Content-Length", out string? text) && int.TryParse(text, out int parsed) ? parsed : 0;
		int bodyStart = headEnd + 4;
		while (received.Length - bodyStart < length)
		{
			int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}
			received.Write(buffer, 0, read);
		}
		byte[] body = new byte[Math.Max(0, Math.Min(length, (int)received.Length - bodyStart))];
		Array.Copy(received.GetBuffer(), bodyStart, body, 0, body.Length);
		return (requestLine[0], requestLine[1], headers, body);
	}

	private static int IndexOfHeadEnd(MemoryStream received)
	{
		byte[] bytes = received.GetBuffer();
		for (int i = 0; i + 3 < received.Length; i++)
		{
			if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>Once the request is read, the socket turns readable with nothing to read only when the client has closed it.</summary>
	private async Task WatchForCloseAsync(Socket socket, CancellationToken answered)
	{
		while (!answered.IsCancellationRequested && !stopping.IsCancellationRequested)
		{
			try
			{
				if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
				{
					return;
				}
			}
			catch (Exception error) when (error is SocketException or ObjectDisposedException)
			{
				return;
			}
			await Task.Delay(20).ConfigureAwait(false);
		}
		await Task.Delay(Timeout.Infinite, stopping.Token).ConfigureAwait(false);
	}
}
