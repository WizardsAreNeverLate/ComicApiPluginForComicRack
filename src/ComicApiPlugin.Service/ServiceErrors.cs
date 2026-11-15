using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ComicApiPlugin.Service;

/// <summary>
/// The answer to an exception that no endpoint expected: a 500 whose body names the error, so the plugin can tell a
/// failure inside the service from a service that cannot be reached.
/// </summary>
internal static class ServiceErrors
{
	private static readonly string[] SecretHeaders = [ServiceProtocol.ComicVineKeyHeader, ServiceProtocol.VerseDbTokenHeader];

	public static async Task Invoke(HttpContext context, RequestDelegate next)
	{
		try
		{
			await next(context);
		}
		catch (Exception error) when (!context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
		{
			Log.Error(error, "{Path:l} failed inside the service", context.Request.Path.Value);
			await EndpointIo.WriteJson(context, StatusCodes.Status500InternalServerError, new ErrorResponseDto { ServiceError = Describe(context, error) });
		}
	}

	/// <summary>The type and message of the error, without the request's API key or token, because the plugin logs it.</summary>
	public static string Describe(HttpContext context, Exception error)
	{
		string text = error.GetType().Name + ": " + error.Message;
		foreach (string header in SecretHeaders)
		{
			if (context.Request.Headers[header].ToString() is { Length: > 0 } secret)
			{
				text = text.Replace(secret, "...");
			}
		}
		return text;
	}
}
