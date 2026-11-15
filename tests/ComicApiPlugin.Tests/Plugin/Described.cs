using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ComicApiPlugin.Model;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>Compares what a provider gives through the local API service with what it gives in this process.</summary>
internal static class Described
{
	/// <summary>Every public value of a result or an exception, lists element by element, so two that differ anywhere give different text.</summary>
	public static string Describe(object? value) => value switch
	{
		null => "null",
		string text => "\"" + text + "\"",
		Exception error => $"{error.GetType().Name} {{ Message = {Describe(error.Message)}, {Properties(error, error.GetType())} }}",
		Uri url => url.OriginalString,
		IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Describe)) + "]",
		_ when value.GetType().IsPrimitive || value is Enum or SeriesId or IssueId or PartialDate or VerseDbIssueId or TimeSpan => value.ToString(),
		_ => $"{value.GetType().Name} {{ {Properties(value, value.GetType())} }}",
	};

	private static string Properties(object value, Type type) => string.Join(", ",
		type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Where(property => property.Name != "EqualityContract")
			.Select(property => property.Name + " = " + Describe(property.GetValue(value))));
}
