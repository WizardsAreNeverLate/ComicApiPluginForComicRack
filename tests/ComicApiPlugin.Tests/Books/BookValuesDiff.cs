using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComicApiPlugin.Books;

namespace ComicApiPlugin.Tests.Books;

/// <summary>Which fields differ between two BookValues, one projection per BookFields bit.</summary>
internal static class BookValuesDiff
{
	public static IReadOnlyDictionary<BookFields, Func<BookValues, string>> Projections { get; } = new Dictionary<BookFields, Func<BookValues, string>>
	{
		[BookFields.Series] = v => v.Series,
		[BookFields.Number] = v => v.Number,
		[BookFields.Volume] = v => Str(v.Volume),
		[BookFields.Title] = v => v.Title,
		[BookFields.Crossovers] = v => Join(v.Crossovers),
		[BookFields.Summary] = v => v.Summary,
		[BookFields.Publisher] = v => v.Publisher,
		[BookFields.Imprint] = v => v.Imprint,
		[BookFields.Characters] = v => Join(v.Characters),
		[BookFields.Teams] = v => Join(v.Teams),
		[BookFields.Locations] = v => Join(v.Locations),
		[BookFields.Writers] = v => Join(v.Writers),
		[BookFields.Pencillers] = v => Join(v.Pencillers),
		[BookFields.Inkers] = v => Join(v.Inkers),
		[BookFields.Colorists] = v => Join(v.Colorists),
		[BookFields.Letterers] = v => Join(v.Letterers),
		[BookFields.CoverArtists] = v => Join(v.CoverArtists),
		[BookFields.Editors] = v => Join(v.Editors),
		[BookFields.Webpage] = v => v.Webpage,
		[BookFields.Rating] = v => v.Rating?.ToString("R", CultureInfo.InvariantCulture) ?? "",
		[BookFields.Tags] = v => Join(v.Tags),
		[BookFields.Notes] = v => v.Notes,
		[BookFields.IssueKey] = v => v.IssueKey,
		[BookFields.SeriesKey] = v => v.SeriesKey,
		[BookFields.Released] = v => $"{Str(v.Released.Year)}-{Str(v.Released.Month)}-{Str(v.Released.Day)}",
		[BookFields.PublishedYear] = v => Str(v.Published.Year),
		[BookFields.PublishedMonth] = v => Str(v.Published.Month),
		[BookFields.PublishedDay] = v => Str(v.Published.Day),
		[BookFields.Format] = v => v.Format,
	};

	public static BookFields[] SingleFields { get; } = Enum.GetValues(typeof(BookFields)).Cast<BookFields>()
		.Where(field => field != BookFields.None && (field & (field - 1)) == 0)
		.ToArray();

	public static BookFields AllFields { get; } = SingleFields.Aggregate(BookFields.None, (all, field) => all | field);

	public static BookFields Changed(BookValues before, BookValues after) =>
		Projections.Where(pair => pair.Value(before) != pair.Value(after)).Aggregate(BookFields.None, (all, pair) => all | pair.Key);

	public static string Describe(BookValues values) =>
		string.Join("\n", SingleFields.Select(field => $"{field}={Projections[field](values)}"));

	private static string Str(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

	private static string Join(IReadOnlyList<string> items) => string.Join("\u0001", items);
}
