using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComicApiPlugin.Service.Tests;

public sealed class ServiceTests
{
	private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(10);

	[Test]
	public async Task A_request_without_the_launch_token_is_refused()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient anonymous = service.Client(token: null);
		using HttpClient wrong = service.Client(token: RunningService.Token + "x");

		using HttpResponseMessage none = await anonymous.GetAsync(ServiceProtocol.HealthPath);
		using HttpResponseMessage bad = await wrong.GetAsync(ServiceProtocol.HealthPath);
		using HttpResponseMessage shutdown = await anonymous.PostAsync(ServiceProtocol.ShutdownPath, null);

		await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(shutdown.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(service.Process.HasExited).IsFalse();
	}

	[Test]
	[Arguments("localhost")]
	[Arguments("evil.example")]
	[Arguments("127.0.0.1")]
	[Arguments("127.0.0.1:1")]
	public async Task A_request_whose_Host_is_not_the_bound_loopback_address_is_refused_even_with_the_token(string host)
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();
		using var request = new HttpRequestMessage(HttpMethod.Get, ServiceProtocol.HealthPath);
		request.Headers.Host = host;

		using HttpResponseMessage response = await client.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Health_reports_the_version_the_process_id_and_the_uptime()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();

		using HttpResponseMessage response = await client.GetAsync(ServiceProtocol.HealthPath);
		using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(json.RootElement.GetProperty("version").GetString()).IsEqualTo(service.ReportedVersion);
		await Assert.That(json.RootElement.GetProperty("pid").GetInt32()).IsEqualTo(service.Process.Id);
		await Assert.That(json.RootElement.GetProperty("uptimeSeconds").GetInt64()).IsGreaterThanOrEqualTo(0);
	}

	[Test]
	public async Task The_ready_line_is_the_only_stdout_line_and_names_a_port_that_listens_on_loopback_only()
	{
		using RunningService service = await RunningService.StartAsync();

		IPEndPoint[] listeners = [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(listener => listener.Port == service.Port)];

		await Assert.That(service.StandardOutput.Count).IsEqualTo(1);
		await Assert.That(service.ReportedPid).IsEqualTo(service.Process.Id);
		await Assert.That(service.ReportedVersion).IsEqualTo(typeof(ServiceProtocol).Assembly.GetName().Version!.ToString(3));
		await Assert.That(listeners.Select(listener => listener.Address.ToString())).IsEquivalentTo([ServiceProtocol.LoopbackHost]);
	}

	[Test]
	public async Task Shutdown_answers_202_and_the_process_then_exits_cleanly()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();

		using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.ShutdownPath, null);
		bool exited = await service.ExitsWithinAsync(ExitLimit);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
		await Assert.That(exited).IsTrue();
		await Assert.That(service.Process.ExitCode).IsEqualTo(0);
	}

	[Test]
	public async Task The_service_exits_when_its_parent_process_is_killed()
	{
		using Process parent = StartIdleProcess();
		using RunningService service = await RunningService.StartAsync(parent.Id);

		parent.Kill();
		bool exited = await service.ExitsWithinAsync(ExitLimit);

		await Assert.That(exited).IsTrue();
		await Assert.That(service.Process.ExitCode).IsEqualTo(0);
		await Assert.That(service.ReadLogs()).Contains($"Parent process {parent.Id} exited");
	}

	[Test]
	public async Task The_service_does_not_start_when_its_parent_has_already_gone()
	{
		int gone;
		using (Process parent = StartIdleProcess())
		{
			gone = parent.Id;
			parent.Kill();
			await parent.WaitForExitAsync();
		}
		using Process service = RunningService.Launch(RunningService.Token, gone, RunningService.NewLogFolder());

		string output = await service.StandardOutput.ReadToEndAsync();
		string error = await service.StandardError.ReadToEndAsync();
		await service.WaitForExitAsync();

		await Assert.That(service.ExitCode).IsEqualTo(3);
		await Assert.That(output).IsEmpty();
		await Assert.That(error).Contains($"parent process {gone} is not running");
	}

	[Test]
	public async Task The_service_does_not_start_without_a_token_in_its_environment()
	{
		using Process service = RunningService.Launch(token: null, parentPid: null, RunningService.NewLogFolder());

		string output = await service.StandardOutput.ReadToEndAsync();
		string error = await service.StandardError.ReadToEndAsync();
		await service.WaitForExitAsync();

		await Assert.That(service.ExitCode).IsEqualTo(2);
		await Assert.That(output).IsEmpty();
		await Assert.That(error).Contains(ServiceProtocol.TokenVariable);
	}

	[Test]
	public async Task The_log_file_records_each_request_without_the_token_a_provider_key_or_the_query_string()
	{
		const string providerKey = "provider-key-7Hq2mX";
		const string querySecret = "query-secret-9Lw4pT";
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{ServiceProtocol.HealthPath}?api_key={querySecret}");
		request.Headers.Add(ServiceProtocol.ComicVineKeyHeader, providerKey);
		request.Headers.Add(ServiceProtocol.VerseDbTokenHeader, providerKey);

		using HttpResponseMessage health = await client.SendAsync(request);
		using HttpResponseMessage shutdown = await client.PostAsync(ServiceProtocol.ShutdownPath, null);
		await service.ExitsWithinAsync(ExitLimit);
		string log = service.ReadLogs();

		await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(Directory.GetFiles(service.LogFolder).Select(Path.GetFileName).Single()).Matches(@"^service-\d{8}\.log$");
		await Assert.That(log).Contains($"HTTP GET {ServiceProtocol.HealthPath} responded 200 in ");
		await Assert.That(log).Contains($"HTTP POST {ServiceProtocol.ShutdownPath} responded 202 in ");
		await Assert.That(log).DoesNotContain(providerKey);
		await Assert.That(log).DoesNotContain(querySecret);
		await Assert.That(log).DoesNotContain(RunningService.Token);
	}

	/// <summary>A process that does nothing for two minutes and has no child that would outlive a kill.</summary>
	private static Process StartIdleProcess() =>
		Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
}
