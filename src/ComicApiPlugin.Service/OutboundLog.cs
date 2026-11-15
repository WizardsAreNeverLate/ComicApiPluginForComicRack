using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ComicApiPlugin.Service;

/// <summary>
/// Logs every call the service makes to a provider: one line with the provider, the method, the host and path, the
/// status and the time. It never logs the query string, which holds the Comic Vine API key, or a header, which holds the VerseDB token.
/// </summary>
internal sealed class OutboundLog(string provider, HttpMessageHandler inner) : DelegatingHandler(inner)
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string target = request.RequestUri is { } uri ? uri.Authority + uri.AbsolutePath : "";
		var watch = Stopwatch.StartNew();
		HttpResponseMessage response;
		try
		{
			response = await base.SendAsync(request, cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			// The type only: a transport error's message can quote the request URL.
			Log.Warning("{Provider:l} {Method:l} {Target:l} failed with {Error:l} in {Elapsed} ms (run {RunId:l})",
				provider, request.Method.Method, target, error.GetType().Name, watch.ElapsedMilliseconds, RunScope.Current);
			throw;
		}
		int status = (int)response.StatusCode;
		if (status >= 400)
		{
			Log.Warning("{Provider:l} {Method:l} {Target:l} responded {StatusCode} in {Elapsed} ms (run {RunId:l})",
				provider, request.Method.Method, target, status, watch.ElapsedMilliseconds, RunScope.Current);
		}
		else
		{
			Log.Information("{Provider:l} {Method:l} {Target:l} responded {StatusCode} in {Elapsed} ms (run {RunId:l})",
				provider, request.Method.Method, target, status, watch.ElapsedMilliseconds, RunScope.Current);
		}
		return response;
	}
}
