using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ComicApiPlugin.Service;

/// <summary>
/// Runs before routing, so no endpoint is anonymous. A request must name the bound loopback address as its Host, which a
/// browser that was sent to this port under another name (DNS rebinding) cannot do, and carry the launch token.
/// </summary>
internal sealed class RequestGuard(string token)
{
	private const string Scheme = "Bearer ";

	private readonly byte[] expected = Encoding.UTF8.GetBytes(token);

	public Task Invoke(HttpContext context, RequestDelegate next)
	{
		if (context.Request.Host.Value != $"{ServiceProtocol.LoopbackHost}:{context.Connection.LocalPort}")
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return Task.CompletedTask;
		}
		if (!Authorized(context.Request.Headers.Authorization))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return Task.CompletedTask;
		}
		return next(context);
	}

	private bool Authorized(string? header) =>
		header is not null
		&& header.StartsWith(Scheme, StringComparison.Ordinal)
		&& CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[Scheme.Length..]), expected);
}
