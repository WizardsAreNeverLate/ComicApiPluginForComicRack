using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Config;
using ComicApiPlugin.Diagnostics;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Service;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// A service that fails on an error it did not expect. The plugin talks to a test server that answers as such a service
/// would, so each test can choose the failure.
/// </summary>
public sealed class ServiceErrorTests
{
	private const string Unexpected = "NullReferenceException: Object reference not set to an instance of an object.";

	[Test]
	public async Task A_Comic_Vine_call_the_service_fails_is_an_unexpected_error_with_the_services_message_and_not_a_service_or_Comic_Vine_failure()
	{
		using var world = new World(_ => FakeAnswer.Json($"{{\"serviceError\":\"{Unexpected}\"}}", HttpStatusCode.InternalServerError));

		Exception error = await Catch(() => world.ComicVine.FindIssueAsync(new SeriesId(796), "497", CancellationToken.None));

		await Assert.That(error).IsTypeOf<ServiceErrorException>();
		await Assert.That(error.Message).IsEqualTo($"The local API service failed on {ServiceProtocol.ComicVineFindIssuePath}: {Unexpected}");
		await Assert.That(ErrorPrompt.For(error).OffersLogSave).IsTrue();
	}

	[Test]
	public async Task A_search_the_service_fails_after_its_first_progress_line_is_an_unexpected_error_with_the_services_message()
	{
		string lines = "{\"progress\":{\"done\":100,\"total\":3}}\n" + $"{{\"serviceError\":\"{Unexpected}\"}}\n";
		using var world = new World(_ => new FakeAnswer(HttpStatusCode.OK, Encoding.UTF8.GetBytes(lines), ComicVineWire.LinesContentType));
		var progress = new List<QueryProgress>();

		Exception error = await Catch(() => world.ComicVine.SearchSeriesAsync("batman", new Reports(progress), CancellationToken.None));

		await Assert.That(error).IsTypeOf<ServiceErrorException>();
		await Assert.That(error.Message).IsEqualTo($"The local API service failed on {ServiceProtocol.ComicVineSearchPath}: {Unexpected}");
		await Assert.That(progress.Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_VerseDB_call_the_service_fails_is_an_unexpected_error_and_not_a_VerseDB_failure()
	{
		using var world = new World(_ => FakeAnswer.Json($"{{\"serviceError\":\"{Unexpected}\"}}", HttpStatusCode.InternalServerError));

		Exception error = await Catch(() => world.VerseDb.GetIssueAsync(new VerseDbIssueId(423295), CancellationToken.None));

		await Assert.That(error).IsTypeOf<ServiceErrorException>();
		await Assert.That(error.Message).IsEqualTo($"The local API service failed on {ServiceProtocol.VerseDbIssuePath}: {Unexpected}");
	}

	private static async Task<Exception> Catch(Func<Task> call)
	{
		try
		{
			await call();
		}
		catch (Exception error)
		{
			return error;
		}
		throw new InvalidOperationException("The call did not fail.");
	}

	private sealed class Reports(List<QueryProgress> into) : IProgress<QueryProgress>
	{
		public void Report(QueryProgress value) => into.Add(value);
	}

	private sealed class World : IDisposable
	{
		private readonly FakeHttpServer service;
		private readonly ServiceHost host;
		private readonly ServiceConnection connection;

		public World(Func<FakeRequest, FakeAnswer> answer)
		{
			service = new FakeHttpServer(request => Task.FromResult(answer(request)));
			host = new ServiceHost(BuiltService.ReadyOn(service.Port), () => false, _ => { });
			connection = new ServiceConnection(host);
			ComicVine = new ServiceComicVine(connection, "key", "run", new SearchSettings([], [], 0, 9999, 9999, 100), new DebugLog());
			VerseDb = new ServiceVerseDb(connection, "token", "run");
		}

		public IComicVine ComicVine { get; }

		public IVerseDb VerseDb { get; }

		public void Dispose()
		{
			connection.Dispose();
			host.Dispose();
			service.Dispose();
		}
	}
}
