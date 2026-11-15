using System;
using System.Collections.Generic;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Service.VerseDb;

/// <summary>One VerseDB transport, and so one HttpClient, for each API token, kept in memory for the life of the service.</summary>
/// <param name="connect">Makes the transport for a token.</param>
internal sealed class VerseDbClients(Func<string, VerseDbHttp> connect) : IDisposable
{
	private readonly object gate = new();
	private readonly Dictionary<string, VerseDbHttp> clients = new(StringComparer.Ordinal);

	public VerseDbHttp For(string token)
	{
		lock (gate)
		{
			if (!clients.TryGetValue(token, out VerseDbHttp? client))
			{
				clients[token] = client = connect(token);
			}
			return client;
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			foreach (VerseDbHttp client in clients.Values)
			{
				client.Dispose();
			}
			clients.Clear();
		}
	}
}
