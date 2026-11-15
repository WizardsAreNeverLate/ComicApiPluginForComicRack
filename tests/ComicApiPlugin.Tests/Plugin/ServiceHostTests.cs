using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Plugin;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>ServiceHost against the service the build packaged, started as a real process, and against scripts that fail as a service can.</summary>
public sealed class ServiceHostTests
{
	private const int ExitWaitMilliseconds = 10_000;

	[Test]
	public async Task Concurrent_callers_share_one_service_process_that_answers_its_health_check()
	{
		ServiceLaunch launch = BuiltService.Launch();
		using ServiceHost host = Host(launch);

		ServiceEndpoint[] endpoints = await Task.WhenAll(
			Task.Run(() => host.EnsureStartedAsync(CancellationToken.None)),
			Task.Run(() => host.EnsureStartedAsync(CancellationToken.None)),
			Task.Run(() => host.EnsureStartedAsync(CancellationToken.None)));
		ServiceEndpoint again = await host.EnsureStartedAsync(CancellationToken.None);
		using HttpClient client = endpoints[0].CreateClient(TimeSpan.FromSeconds(10));
		using HttpResponseMessage health = await client.GetAsync(ServiceProtocol.HealthPath);
		string report = await host.CheckAsync();

		await Assert.That(endpoints[1]).IsSameReferenceAs(endpoints[0]);
		await Assert.That(endpoints[2]).IsSameReferenceAs(endpoints[0]);
		await Assert.That(again).IsSameReferenceAs(endpoints[0]);
		await Assert.That(BuiltService.CommandLines(launch.LogFolder).Count).IsEqualTo(1);
		await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await health.Content.ReadAsStringAsync()).Contains($"\"pid\":{endpoints[0].Pid}");
		await Assert.That(report).IsEqualTo($"Local API service ready on 127.0.0.1:{endpoints[0].BaseAddress.Port} (pid {endpoints[0].Pid})");
		await Assert.That(host.State is ServiceState.Running).IsTrue();
	}

	[Test]
	public async Task The_token_is_random_per_launch_and_never_on_the_command_line_and_the_service_watches_this_process()
	{
		ServiceLaunch launch = BuiltService.Launch();
		using ServiceHost first = Host(launch);
		using ServiceHost second = Host(BuiltService.Launch());

		ServiceEndpoint endpoint = await first.EnsureStartedAsync(CancellationToken.None);
		ServiceEndpoint other = await second.EnsureStartedAsync(CancellationToken.None);
		string commandLine = BuiltService.CommandLines(launch.LogFolder)[0];

		await Assert.That(endpoint.Token.Length).IsEqualTo(43);
		await Assert.That(other.Token).IsNotEqualTo(endpoint.Token);
		await Assert.That(commandLine).DoesNotContain(endpoint.Token);
		await Assert.That(commandLine).Contains($"{ServiceProtocol.ParentPidArgument} {Process.GetCurrentProcess().Id} ");
		await Assert.That(endpoint.ToString()).DoesNotContain(endpoint.Token);
	}

	[Test]
	public async Task Closing_the_job_without_a_shutdown_kills_the_service()
	{
		ServiceHost host = Host(BuiltService.Launch());
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(CancellationToken.None);
		using Process service = Process.GetProcessById(endpoint.Pid);

		host.Dispose();
		bool exited = service.WaitForExit(ExitWaitMilliseconds);

		await Assert.That(exited).IsTrue();
	}

	[Test]
	public async Task Stop_shuts_the_service_down_through_its_endpoint_and_a_later_call_starts_it_again()
	{
		ServiceLaunch launch = BuiltService.Launch();
		using ServiceHost host = Host(launch);
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(CancellationToken.None);
		using Process service = Process.GetProcessById(endpoint.Pid);

		host.Stop();
		bool exited = service.WaitForExit(0);
		ServiceState stopped = host.State;
		string log = BuiltService.ReadLogs(launch.LogFolder);
		ServiceEndpoint restarted = await host.EnsureStartedAsync(CancellationToken.None);

		await Assert.That(exited).IsTrue();
		await Assert.That(stopped is ServiceState.Stopped).IsTrue();
		await Assert.That(log).Contains($"HTTP POST {ServiceProtocol.ShutdownPath} responded 202");
		await Assert.That(log).Contains("Stopped");
		await Assert.That(restarted.Pid).IsNotEqualTo(endpoint.Pid);
		await Assert.That(await host.CheckAsync()).StartsWith("Local API service ready on ");
	}

	[Test]
	public async Task A_service_that_exited_is_started_again_on_the_next_call()
	{
		using ServiceHost host = Host(BuiltService.Launch());
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(CancellationToken.None);
		using (Process service = Process.GetProcessById(endpoint.Pid))
		{
			service.Kill();
			service.WaitForExit(ExitWaitMilliseconds);
		}

		ServiceEndpoint restarted = await host.EnsureStartedAsync(CancellationToken.None);

		await Assert.That(restarted.Pid).IsNotEqualTo(endpoint.Pid);
		await Assert.That(restarted.Token).IsNotEqualTo(endpoint.Token);
		await Assert.That(await host.CheckAsync()).StartsWith("Local API service ready on ");
	}

	[Test]
	public async Task A_service_that_never_reports_ready_fails_with_the_reason_within_the_timeout()
	{
		ServiceLaunch launch = BuiltService.Script("@echo starting up", "@ping -n 60 127.0.0.1 >nul") with { ReadyTimeout = TimeSpan.FromSeconds(2) };
		using ServiceHost host = Host(launch);
		var watch = Stopwatch.StartNew();

		ServiceUnavailableException? failure = await Failure(host);
		TimeSpan elapsed = watch.Elapsed;

		await Assert.That(failure!.Message).IsEqualTo("The local API service did not report that it was ready within 2 seconds.");
		await Assert.That((host.State as ServiceState.Failed)?.Reason).IsEqualTo(failure.Message);
		await Assert.That(elapsed).IsBetween(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
	}

	[Test]
	public async Task A_missing_exe_fails_with_its_path_and_the_next_call_tries_again()
	{
		string missing = Path.Combine(BuiltService.NewFolder("missing"), ServicePackage.ExeName);
		var paths = new Queue<string>([missing, BuiltService.Exe]);
		using ServiceHost host = Host(new ServiceLaunch(paths.Dequeue, BuiltService.NewFolder("logs")));

		Task<ServiceEndpoint> first = host.EnsureStartedAsync(CancellationToken.None);
		ServiceUnavailableException? failure = await Failure(first);
		ServiceState failed = host.State;
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(CancellationToken.None);

		await Assert.That(failure!.Message).IsEqualTo($"The local API service was not found at {missing}.");
		await Assert.That((failed as ServiceState.Failed)?.Reason).IsEqualTo(failure.Message);
		await Assert.That(endpoint.Pid).IsGreaterThan(0);
	}

	[Test]
	public async Task A_launch_that_cannot_find_its_package_fails_without_an_exception_from_the_call()
	{
		string zip = Path.Combine(BuiltService.NewFolder("no-plugin"), ServicePackage.ZipName);
		using ServiceHost host = Host(new ServiceLaunch(() => ServicePackage.Unpack(zip, BuiltService.NewFolder("root")), BuiltService.NewFolder("logs")));

		string report = await host.CheckAsync();

		await Assert.That(report).StartsWith("Local API service unavailable: The local API service could not be started: ");
		await Assert.That(report).Contains(zip);
	}

	[Test]
	public async Task A_machine_without_the_dotnet_runtime_is_told_which_runtime_to_install_and_where()
	{
		// The first lines and the exit code of the apphost when it finds no .NET, as ComicApiPlugin.Service.exe 11.0.0 wrote them.
		ServiceLaunch launch = BuiltService.Script(
			"@echo You must install .NET to run this application. 1>&2",
			"@echo Architecture: x64 1>&2",
			"@echo App host version: 11.0.0 1>&2",
			"@echo .NET location: Not found 1>&2",
			"@exit /b -2147450749");
		using ServiceHost host = Host(launch);

		ServiceUnavailableException? failure = await Failure(host);

		await Assert.That(failure!.Message).IsEqualTo(
			"The local API service needs the .NET 11 runtime (ASP.NET Core Runtime 11, x64), which is not installed. "
			+ "Download it from https://dotnet.microsoft.com/download/dotnet/11.0 and restart ComicRack.");
	}

	[Test]
	public async Task A_service_that_exits_before_it_is_ready_fails_with_its_exit_code_and_what_it_wrote()
	{
		using ServiceHost host = Host(BuiltService.Script("@echo cannot open the log folder 1>&2", "@exit /b 7"));

		ServiceUnavailableException? failure = await Failure(host);

		await Assert.That(failure!.Message).IsEqualTo("The local API service exited with code 7 before it was ready. It wrote: cannot open the log folder ");
	}

	[Test]
	public async Task A_process_that_exited_before_it_could_join_the_job_is_not_an_error()
	{
		// A service that fails at once, as the apphost does without .NET, can exit before the plugin adds it to the job.
		using Process exited = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
		exited.WaitForExit();
		using var job = new ProcessJob();

		job.Add(exited);

		await Assert.That(exited.HasExited).IsTrue();
	}

	[Test]
	public async Task A_caller_that_cancels_stops_waiting_and_the_start_goes_on_for_the_others()
	{
		using ServiceHost host = Host(BuiltService.Launch());
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();

		Task<ServiceEndpoint> cancelled = host.EnsureStartedAsync(cancel.Token);
		ServiceEndpoint endpoint = await host.EnsureStartedAsync(CancellationToken.None);

		await Assert.That(async () => await cancelled).Throws<TaskCanceledException>();
		await Assert.That(endpoint.Pid).IsGreaterThan(0);
	}

	private static ServiceHost Host(ServiceLaunch launch) => new(launch, () => false, _ => { });

	private static Task<ServiceUnavailableException?> Failure(ServiceHost host) => Failure(host.EnsureStartedAsync(CancellationToken.None));

	private static async Task<ServiceUnavailableException?> Failure(Task<ServiceEndpoint> start)
	{
		try
		{
			await start;
			return null;
		}
		catch (ServiceUnavailableException failure)
		{
			return failure;
		}
	}
}
