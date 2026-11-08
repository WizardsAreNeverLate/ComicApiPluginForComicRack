using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Diagnostics;

namespace ComicVineScraper.Tests.Diagnostics;

public sealed class DebugLogTests
{
	[Test]
	public async Task Lines_are_kept_in_order_and_saved_with_newlines()
	{
		var log = new DebugLog();
		log.Debug("first");
		log.Debug("second");

		var writer = new StringWriter();
		log.SaveTo(writer);

		await Assert.That(log.Snapshot()).IsEquivalentTo(["first", "second"]);
		await Assert.That(writer.ToString()).IsEqualTo("first\nsecond\n");
	}

	[Test]
	public async Task The_snapshot_is_a_copy()
	{
		var log = new DebugLog();
		log.Debug("a");
		var snapshot = log.Snapshot();

		log.Debug("b");

		await Assert.That(snapshot.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Parallel_writers_lose_no_lines()
	{
		var log = new DebugLog();

		Parallel.For(0, 8, thread =>
		{
			for (int i = 0; i < 500; i++)
			{
				log.Debug(thread + ":" + i);
			}
		});

		string[] lines = log.Snapshot().ToArray();
		await Assert.That(lines.Length).IsEqualTo(4000);
		await Assert.That(lines.Distinct().Count()).IsEqualTo(4000);
	}

	[Test]
	public async Task An_error_is_logged_with_its_type_message_and_stack()
	{
		var log = new DebugLog();
		Exception error;
		try
		{
			throw new InvalidOperationException("boom");
		}
		catch (Exception caught)
		{
			error = caught;
		}

		log.Error(error);

		string text = string.Join("\n", log.Snapshot());
		await Assert.That(text).Contains("Caught InvalidOperationException: boom");
		await Assert.That(text).Contains("An_error_is_logged_with_its_type_message_and_stack");
		await Assert.That(text).DoesNotContain("\r");
	}

	[Test]
	[Arguments(ComicVineError.InvalidApiKey, "LogDBErrorApiKeyText", false)]
	[Arguments(ComicVineError.RateLimited, "LogDBErrorRateText", false)]
	[Arguments(ComicVineError.Network, "LogDBErrorText", false)]
	[Arguments(ComicVineError.Api, "LogErrorText", true)]
	public async Task A_Comic_Vine_error_maps_to_the_log_handle_error_prompt(ComicVineError kind, string textKey, bool offersLog)
	{
		ErrorPrompt prompt = ErrorPrompt.For(new ComicVineException(kind, "https://comicvine.gamespot.com/api/search/?api_key=REDACTED", 101, null));

		await Assert.That(prompt.TextKey).IsEqualTo(textKey);
		await Assert.That(prompt.OffersLogSave).IsEqualTo(offersLog);
		await Assert.That(prompt.TitleKey).IsEqualTo(offersLog ? "LogErrorTitle" : "LogDBErrorTitle");
		await Assert.That(prompt.DatabaseName).IsEqualTo(offersLog ? null : "Comic Vine");
	}

	[Test]
	public async Task Any_other_exception_is_unexpected_and_offers_to_save_the_log()
	{
		ErrorPrompt prompt = ErrorPrompt.For(new InvalidOperationException());

		await Assert.That(prompt).IsEqualTo(new ErrorPrompt("LogErrorText", "LogErrorTitle", true, null));
	}

	[Test]
	public async Task The_log_file_name_uses_the_invariant_date()
	{
		await Assert.That(ErrorPrompt.LogFileName(new DateTime(2024, 3, 9))).IsEqualTo("cvs-debug-log-2024-03-09.txt");
	}
}
