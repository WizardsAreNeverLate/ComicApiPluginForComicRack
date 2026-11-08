using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>
/// A stand-in for ScrapeRun that proves the host: one second per book, then one real write through Apply.
/// Phase 5 replaces it with the engine.
/// </summary>
internal static class PrototypeRun
{
	public const string Stamp = "cvs-net prototype";

	public static async Task<RunSummary> RunAsync(IReadOnlyList<IScrapeBook> books, ComicForm form, Strings strings, CancellationToken ct)
	{
		int scraped = 0;
		try
		{
			foreach (IScrapeBook book in books)
			{
				string file = Path.GetFileName(book.Read().FilePath);
				form.ShowBook(file.Length > 0 ? file : strings["ComicFormUnknown"], scraped, books.Count);
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
}
