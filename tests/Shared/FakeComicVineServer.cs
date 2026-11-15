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

/// <summary>What the fake answers one request with.</summary>
public sealed record FakeAnswer(HttpStatusCode Status, byte[] Body, string ContentType)
{
	public static FakeAnswer Xml(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status, Encoding.UTF8.GetBytes(body), "text/xml; charset=utf-8");
}

/// <summary>One request the fake received.</summary>
/// <param name="Target">The path and query exactly as sent, API key included.</param>
/// <param name="ReceivedAt">Time since the fake started.</param>
/// <param name="ClientGone">Completes when the client closes the connection before the fake has answered.</param>
public sealed record FakeRequest(string Target, TimeSpan ReceivedAt, Task ClientGone);

/// <summary>
/// A Comic Vine that listens on a loopback port, so the service process and an in-process ComicVineHttp can both be
/// pointed at it. It speaks just enough HTTP/1.1 for HttpClient: one GET per connection.
/// </summary>
public sealed class FakeComicVineServer : IDisposable
{
	private readonly TcpListener listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource stopping = new();
	private readonly ConcurrentQueue<FakeRequest> requests = new();
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private readonly Func<FakeRequest, Task<FakeAnswer>> answer;

	public FakeComicVineServer(Func<FakeRequest, Task<FakeAnswer>> answer)
	{
		this.answer = answer;
		listener.Start();
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		_ = Task.Run(AcceptAsync);
	}

	/// <summary>Answers the API from recorded responses, and finds nothing for a request no fixture names.</summary>
	/// <param name="first">Answers a request before the recorded responses do, or returns null to leave it to them.</param>
	public static FakeComicVineServer FromFixtures(IEnumerable<RecordedResponse> responses, Func<FakeRequest, Task<FakeAnswer?>>? first = null)
	{
		var fixtures = new FixtureHandler(responses);
		return new FakeComicVineServer(async request =>
		{
			if (first is not null && await first(request).ConfigureAwait(false) is { } custom)
			{
				return custom;
			}
			string url = "https://comicvine.gamespot.com" + request.Target;
			RecordedResponse recorded = fixtures.Find(url) ?? FixtureHandler.NothingFound(FixtureFiles.Key(url));
			return FakeAnswer.Xml(recorded.Body, recorded.Status);
		});
	}

	public int Port { get; }

	/// <summary>The value for ComicVineHttp's base URL and for the service's COMICAPI_COMICVINE_BASE_URL.</summary>
	public string BaseUrl => $"http://127.0.0.1:{Port}/api/";

	public IReadOnlyList<FakeRequest> Requests => requests.ToArray();

	/// <summary>The fixture key of every request, in order: the URL Comic Vine itself would have seen, with the key redacted.</summary>
	public IReadOnlyList<string> RequestKeys => [.. Requests.Select(request => FixtureFiles.Key("https://comicvine.gamespot.com" + request.Target))];

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
				string head = await ReadHeadAsync(stream).ConfigureAwait(false);
				string[] requestLine = head.Split('\r', '\n')[0].Split(' ');
				if (requestLine.Length < 2)
				{
					return;
				}
				using var answered = new CancellationTokenSource();
				var request = new FakeRequest(requestLine[1], clock.Elapsed, WatchForCloseAsync(client.Client, answered.Token));
				requests.Enqueue(request);
				FakeAnswer response = await answer(request).ConfigureAwait(false);
				answered.Cancel();
				byte[] header = Encoding.ASCII.GetBytes(
					$"HTTP/1.1 {(int)response.Status} {response.Status}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {response.Body.Length}\r\nConnection: close\r\n\r\n");
				await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
				await stream.WriteAsync(response.Body, 0, response.Body.Length).ConfigureAwait(false);
			}
			catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
			{
				// The client went away. A test that cares waits on ClientGone.
			}
		}
	}

	private static async Task<string> ReadHeadAsync(NetworkStream stream)
	{
		var head = new StringBuilder();
		byte[] buffer = new byte[4096];
		while (!head.ToString().Contains("\r\n\r\n"))
		{
			int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}
			head.Append(Encoding.ASCII.GetString(buffer, 0, read));
		}
		return head.ToString();
	}

	/// <summary>A GET has no body, so the socket turns readable with nothing to read only when the client has closed it.</summary>
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
