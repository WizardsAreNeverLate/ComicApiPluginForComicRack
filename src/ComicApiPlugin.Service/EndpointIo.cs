using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ComicApiPlugin.Service;

/// <summary>How every endpoint reads a request and writes an answer.</summary>
internal static class EndpointIo
{
	public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

	/// <exception cref="WireFormatException">The request has no such header, or the header is blank.</exception>
	public static string Header(HttpContext context, string name) =>
		context.Request.Headers[name].ToString() is { } value && !string.IsNullOrWhiteSpace(value) ? value : throw new WireFormatException($"The header {name} is required.");

	/// <exception cref="WireFormatException">The body is not the JSON of this type.</exception>
	public static async Task<T> ReadJson<T>(HttpContext context) where T : class
	{
		using var reader = new StreamReader(context.Request.Body, Utf8);
		return WireJson.FromJson<T>(await reader.ReadToEndAsync(context.RequestAborted));
	}

	/// <summary>
	/// The parts of a multipart/form-data body by name, each read into memory. ASP.NET Core's own form reader is not used,
	/// because it writes a part of more than 64 KB to a temporary file.
	/// </summary>
	/// <exception cref="WireFormatException">The body is not multipart/form-data.</exception>
	public static async Task<IReadOnlyDictionary<string, byte[]>> ReadParts(HttpContext context)
	{
		if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? type)
			|| !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
			|| HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
		{
			throw new WireFormatException("The body must be multipart/form-data.");
		}
		var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		var reader = new MultipartReader(boundary, context.Request.Body);
		try
		{
			while (await reader.ReadNextSectionAsync(context.RequestAborted) is { } section)
			{
				if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition)
					&& HeaderUtilities.RemoveQuotes(disposition.Name).Value is { Length: > 0 } name)
				{
					using var bytes = new MemoryStream();
					await section.Body.CopyToAsync(bytes, context.RequestAborted);
					parts[name] = bytes.ToArray();
				}
			}
		}
		catch (InvalidDataException error)
		{
			throw new WireFormatException("The multipart/form-data body cannot be read: " + error.Message, error);
		}
		return parts;
	}

	/// <exception cref="WireFormatException">The body has no part of this name.</exception>
	public static byte[] Part(IReadOnlyDictionary<string, byte[]> parts, string name) =>
		parts.TryGetValue(name, out byte[]? bytes) ? bytes : throw new WireFormatException($"The multipart/form-data part '{name}' is required.");

	public static async Task WriteJson<T>(HttpContext context, int status, T body) where T : class
	{
		context.Response.StatusCode = status;
		context.Response.ContentType = WireJson.ContentType;
		await context.Response.WriteAsync(WireJson.ToJson(body), Utf8, context.RequestAborted);
	}
}
