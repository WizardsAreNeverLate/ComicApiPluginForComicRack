using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace ComicApiPlugin.Service;

/// <summary>
/// The service logs no headers. Should a later change log a header collection, this writes the launch token and the
/// provider keys as a placeholder.
/// </summary>
internal sealed class SecretHeaderPolicy : IDestructuringPolicy
{
	public const string Redacted = "<redacted>";

	private static readonly HashSet<string> Secret = new(StringComparer.OrdinalIgnoreCase)
	{
		"Authorization",
		ServiceProtocol.ComicVineKeyHeader,
		ServiceProtocol.VerseDbTokenHeader,
	};

	public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
	{
		if (value is not IHeaderDictionary headers)
		{
			result = null;
			return false;
		}
		result = new DictionaryValue(headers.Select(header => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
			new ScalarValue(header.Key),
			new ScalarValue(Secret.Contains(header.Key) ? Redacted : header.Value.ToString()))));
		return true;
	}
}
