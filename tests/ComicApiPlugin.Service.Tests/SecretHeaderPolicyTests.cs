using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ComicApiPlugin.Service.Tests;

public sealed class SecretHeaderPolicyTests
{
	[Test]
	public async Task A_logged_header_collection_shows_the_token_and_the_provider_keys_as_a_placeholder()
	{
		var output = new Rendered();
		using Logger logger = new LoggerConfiguration().Destructure.With<SecretHeaderPolicy>().WriteTo.Sink(output).CreateLogger();
		IHeaderDictionary headers = new HeaderDictionary
		{
			["authorization"] = "Bearer launch-token",
			[ServiceProtocol.ComicVineKeyHeader] = "comic-vine-key",
			[ServiceProtocol.VerseDbTokenHeader.ToUpperInvariant()] = "versedb-token",
			["Accept"] = "application/json",
		};

		logger.Information("Headers {@Headers}", headers);
		string line = output.Text;

		await Assert.That(line).DoesNotContain("launch-token");
		await Assert.That(line).DoesNotContain("comic-vine-key");
		await Assert.That(line).DoesNotContain("versedb-token");
		await Assert.That(line).Contains(SecretHeaderPolicy.Redacted);
		await Assert.That(line).Contains("application/json");
	}

	private sealed class Rendered : ILogEventSink
	{
		public string Text { get; private set; } = "";

		public void Emit(LogEvent logEvent) => Text += logEvent.RenderMessage();
	}
}