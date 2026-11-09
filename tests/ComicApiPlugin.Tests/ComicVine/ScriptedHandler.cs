using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Engine;

namespace ComicApiPlugin.Tests.ComicVine;

/// <summary>Answers the Nth request with the Nth reply, and records what was sent and when.</summary>
internal sealed class ScriptedHandler(IClock clock, params Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler
{
	private readonly object gate = new();
	private readonly List<(HttpRequestMessage Request, DateTime At)> sent = [];

	public IReadOnlyList<(HttpRequestMessage Request, DateTime At)> Sent
	{
		get
		{
			lock (gate)
			{
				return sent.ToArray();
			}
		}
	}

	public static Func<HttpRequestMessage, HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
		_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };

	public static Func<HttpRequestMessage, HttpResponseMessage> Ok(string results = "<results/>") =>
		Reply(HttpStatusCode.OK, Body(1, results));

	public static Func<HttpRequestMessage, HttpResponseMessage> Throw(Exception error) => _ => throw error;

	public static string Body(int statusCode, string results = "<results/>") =>
		"<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error><![CDATA[x]]></error><limit>100</limit><offset>0</offset>"
		+ "<number_of_page_results>0</number_of_page_results><number_of_total_results>0</number_of_total_results>"
		+ $"<status_code>{statusCode}</status_code>{results}<version>1.0</version></response>";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		int index;
		lock (gate)
		{
			index = sent.Count;
			sent.Add((request, clock.Now));
		}
		if (index >= replies.Length)
		{
			throw new InvalidOperationException($"Request {index + 1} was not scripted: {ApiKeyRedaction.Redact(request.RequestUri!.ToString(), "")}");
		}
		return Task.FromResult(replies[index](request));
	}
}
