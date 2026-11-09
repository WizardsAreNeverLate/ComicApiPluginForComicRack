using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ComicVineScraper.Fixtures;

/// <summary>A saved Comic Vine response. Url has the key replaced by REDACTED.</summary>
public sealed record RecordedResponse(string Url, HttpStatusCode Status, string Body);

/// <summary>
/// The fixture file format: "GET url", "HTTP status", a blank line, then the body exactly as received.
/// Files are matched by request, so a file name is only a label.
/// </summary>
public static class FixtureFiles
{
	public const string RedactedKey = "REDACTED";
	private static readonly Regex KeyParameter = new("api_key=[^&]*");

	public static string OutputFolder => Path.Combine(AppContext.BaseDirectory, "fixtures", "comicvine");

	/// <summary>The request identity: key redacted and percent escapes decoded, so a URL built by Uri.EscapeDataString
	/// (C#) and one built by HttpUtility.UrlPathEncode (Python) name the same fixture.</summary>
	public static string Key(string url) => Uri.UnescapeDataString(KeyParameter.Replace(url, "api_key=" + RedactedKey));

	public static RecordedResponse Read(string path)
	{
		string text = File.ReadAllText(path, new UTF8Encoding(false));
		int firstBreak = text.IndexOf('\n');
		int secondBreak = text.IndexOf('\n', firstBreak + 1);
		string requestLine = text.Substring(0, firstBreak);
		string statusLine = text.Substring(firstBreak + 1, secondBreak - firstBreak - 1);
		if (!requestLine.StartsWith("GET ", StringComparison.Ordinal) || !statusLine.StartsWith("HTTP ", StringComparison.Ordinal)
			|| text.Length < secondBreak + 2 || text[secondBreak + 1] != '\n')
		{
			throw new InvalidDataException($"{path} is not a fixture file.");
		}
		return new RecordedResponse(requestLine.Substring(4), (HttpStatusCode)int.Parse(statusLine.Substring(5)), text.Substring(secondBreak + 2));
	}

	public static void Write(string path, RecordedResponse response) =>
		File.WriteAllText(path, $"GET {response.Url}\nHTTP {(int)response.Status}\n\n{response.Body}", new UTF8Encoding(false));

	public static IReadOnlyList<RecordedResponse> ReadFolder(string folder) =>
		Directory.GetFiles(folder, "*.txt").OrderBy(path => path, StringComparer.Ordinal).Select(Read).ToList();
}

/// <summary>
/// Serves recorded responses by request. An unknown request throws InvalidOperationException, which ComicVineHttp
/// does not treat as a network error, so a missing fixture fails the test instead of being retried.
/// </summary>
public sealed class FixtureHandler : HttpMessageHandler
{
	private readonly Dictionary<string, RecordedResponse> responses;
	private readonly ConcurrentQueue<string> requested = new();
	private readonly Func<string, RecordedResponse?>? fallback;

	/// <param name="fallback">Answers a request no fixture matches; without one, such a request throws.</param>
	public FixtureHandler(IEnumerable<RecordedResponse> responses, Func<string, RecordedResponse?>? fallback = null)
	{
		this.fallback = fallback;
		this.responses = new Dictionary<string, RecordedResponse>(StringComparer.Ordinal);
		foreach (RecordedResponse response in responses)
		{
			this.responses[FixtureFiles.Key(response.Url)] = response;
		}
	}

	public static FixtureHandler FromOutputFolder() => new(FixtureFiles.ReadFolder(FixtureFiles.OutputFolder));

	/// <summary>
	/// A fallback for a whole run through the dialogs, which also look up covers no fixture records: an API request finds
	/// nothing, and an image is not found.
	/// </summary>
	public static RecordedResponse NothingFound(string key) => key.Contains("/api/")
		? new RecordedResponse(key, HttpStatusCode.OK,
			"<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error><![CDATA[OK]]></error><limit>100</limit><offset>0</offset>"
			+ "<number_of_page_results>0</number_of_page_results><number_of_total_results>0</number_of_total_results>"
			+ "<status_code>1</status_code><results></results><version>1.0</version></response>")
		: new RecordedResponse(key, HttpStatusCode.NotFound, "");

	/// <summary>Every request served, as fixture keys, in order.</summary>
	public IReadOnlyList<string> Requested => requested.ToArray();

	public RecordedResponse? Find(string url) => responses.TryGetValue(FixtureFiles.Key(url), out RecordedResponse? found) ? found : null;

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string key = FixtureFiles.Key(request.RequestUri!.AbsoluteUri);
		requested.Enqueue(key);
		if (!responses.TryGetValue(key, out RecordedResponse? response) && (response = fallback?.Invoke(key)) is null)
		{
			throw new InvalidOperationException("No fixture for " + key);
		}
		return Task.FromResult(new HttpResponseMessage(response.Status)
		{
			Content = new ByteArrayContent(Encoding.UTF8.GetBytes(response.Body)),
			RequestMessage = request,
		});
	}
}
