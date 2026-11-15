using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ComicApiPlugin.Tests.VerseDb;

/// <summary>Answers each request from a script and keeps what was sent, with the request body already read.</summary>
internal sealed class VerseDbHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler
{
	private int index;

	public List<(HttpRequestMessage Request, byte[]? Body)> Sent { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		byte[]? body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync();
		Sent.Add((request, body));
		return replies[Math.Min(index++, replies.Length - 1)](request);
	}
}
