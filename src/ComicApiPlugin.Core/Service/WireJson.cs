using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ComicApiPlugin.Service;

/// <summary>A body that is not the JSON the contract names: bad JSON, or a required member is missing.</summary>
public sealed class WireFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The JSON of every body the plugin and the service exchange. The serializer is the one .NET Framework and .NET both
/// ship, so the plugin needs no JSON package inside ComicRack.
/// </summary>
public static class WireJson
{
	public const string ContentType = "application/json";

	/// <summary>One line of JSON, with no line break inside it.</summary>
	public static string ToJson<T>(T value) where T : class
	{
		using var stream = new MemoryStream();
		new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <exception cref="WireFormatException">The text is not a JSON object of this type.</exception>
	public static T FromJson<T>(string json) where T : class
	{
		try
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T ?? throw new WireFormatException($"The JSON holds no {typeof(T).Name}.");
		}
		catch (SerializationException error)
		{
			throw new WireFormatException($"The JSON is not a {typeof(T).Name}: {error.Message}", error);
		}
	}

	/// <exception cref="WireFormatException">The member is missing.</exception>
	public static T Need<T>(T? value, string name) where T : class => value ?? throw new WireFormatException($"'{name}' is required.");

	/// <exception cref="WireFormatException">The text is not an absolute URL.</exception>
	public static Uri Url(string text, string name) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? url) ? url : throw new WireFormatException($"'{name}' is not an absolute URL.");

	public static Uri? OptionalUrl(string? text, string name) => text is null ? null : Url(text, name);

	public static IReadOnlyList<string> Strings(string[]? values, string name) =>
		[.. Need(values, name).Select(value => Need(value, name))];
}
