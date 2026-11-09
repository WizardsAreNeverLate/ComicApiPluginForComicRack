using System;
using ComicApiPlugin.ComicVine;

namespace ComicApiPlugin.Diagnostics;

/// <summary>What the error dialog shows. Text and title are keys in Script.ComicVineScraper.xml; the dialog code localizes them.</summary>
/// <param name="OffersLogSave">True for the "unexpected error, save the log?" Yes/No prompt. Otherwise the dialog is an OK warning.</param>
/// <param name="DatabaseName">The {0} argument of the text, for the database warnings; null otherwise.</param>
public sealed record ErrorPrompt(string TextKey, string TitleKey, bool OffersLogSave, string? DatabaseName)
{
	public const string DatabaseDisplayName = "Comic Vine";

	public const string InvalidApiKeyText = "LogDBErrorApiKeyText";
	public const string RateLimitText = "LogDBErrorRateText";
	public const string ConnectionText = "LogDBErrorText";
	public const string DatabaseTitle = "LogDBErrorTitle";
	public const string UnexpectedText = "LogErrorText";
	public const string UnexpectedTitle = "LogErrorTitle";

	public const string SaveTitle = "LogSaveTitle";
	public const string SaveFilter = "LogSaveFilter";
	public const string SavedText = "LogSavedText";
	public const string SavedTitle = "LogSavedTitle";
	public const string SaveFailedText = "LogSaveFailedText";

	/// <summary>log.handle_error: an invalid key (100), a rate limit (107), and a connection failure (0) get their own warning; anything else is unexpected.</summary>
	public static ErrorPrompt For(Exception error) => error is ComicVineException { Error: var kind } && Database(kind) is { } text
		? new ErrorPrompt(text, DatabaseTitle, false, DatabaseDisplayName)
		: new ErrorPrompt(UnexpectedText, UnexpectedTitle, true, null);

	/// <summary>The default name of a saved log, cvs-debug-log-yyyy-MM-dd.txt.</summary>
	public static string LogFileName(DateTime today) => "cvs-debug-log-" + today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".txt";

	private static string? Database(ComicVineError kind) => kind switch
	{
		ComicVineError.InvalidApiKey => InvalidApiKeyText,
		ComicVineError.RateLimited => RateLimitText,
		ComicVineError.Network => ConnectionText,
		_ => null,
	};
}
