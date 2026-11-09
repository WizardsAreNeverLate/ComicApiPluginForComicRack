using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Text;

namespace ComicVineScraper.Plugin;

/// <summary>
/// A stand-in for ScrapeRun that proves the host: one second per book, then one real write through Apply.
/// Phase 5 replaces it with the engine.
/// </summary>
internal static class PrototypeRun
{
	public const string Stamp = "cvs-net prototype";

	public static async Task<RunSummary> RunAsync(IReadOnlyList<IScrapeBook> books, ComicForm form, CancellationToken ct)
	{
		int scraped = 0;
		try
		{
			foreach (IScrapeBook book in books)
			{
				BookFacts facts = BookFacts.From(scraped, book.Read(), ParseName, ignoreFolders: false);
				form.ShowBook(book, facts, books.Count - scraped);
				await Task.Delay(TimeSpan.FromSeconds(1), ct);
				string notes = book.Read().Values.Notes;
				book.Apply(new BookPatch(new BookValues { Notes = notes.Length == 0 ? Stamp : notes + Environment.NewLine + Stamp }, BookFields.Notes), null);
				scraped++;
			}
			return new RunSummary(scraped, 0, Cancelled: false);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return new RunSummary(scraped, books.Count - scraped, Cancelled: true);
		}
	}

	private static (string Series, string Number, string Year) ParseName(string name)
	{
		ParsedName parsed = FileNameParser.Extract(name);
		return (parsed.Series, parsed.Number, parsed.Year);
	}
}
