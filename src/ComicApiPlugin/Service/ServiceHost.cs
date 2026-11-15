using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Plugin;

/// <summary>How to start the service.</summary>
/// <param name="Executable">Gives the path of the service's exe. It may unpack the service first, and it may throw.</param>
/// <param name="LogFolder">Where the service writes its rolling log files.</param>
internal sealed record ServiceLaunch(Func<string> Executable, string LogFolder)
{
	public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(20);

	/// <summary>How long a stop waits for the service to answer the shutdown request.</summary>
	public TimeSpan ShutdownRequestTimeout { get; init; } = TimeSpan.FromSeconds(2);

	/// <summary>How long a stop then waits for the service to exit before it kills the process.</summary>
	public TimeSpan ShutdownExitTimeout { get; init; } = TimeSpan.FromSeconds(3);

	/// <summary>Environment variables for the service process, on top of this process's own. Only a test sets one.</summary>
	public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

	public static string DefaultLogFolder =>
		Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ComicApiPlugin", "logs");

	/// <summary>The service that ships as service.zip in <paramref name="pluginFolder"/>.</summary>
	public static ServiceLaunch FromPackage(string pluginFolder) =>
		new(() => ServicePackage.Unpack(Path.Combine(pluginFolder, ServicePackage.ZipName), ServicePackage.DefaultRoot), DefaultLogFolder);
}

/// <summary>
/// Owns the local API service process for one ComicRack process: starts it on demand, hands out its endpoint, and stops
/// it. The process is held in a job object, so it cannot outlive this process. Where Windows refuses the job, the
/// service's own watch on this process stops it.
/// </summary>
internal sealed class ServiceHost(ServiceLaunch launch, Func<bool> showConsole, Action<string> log, Action<Process>? joinJob = null) : IDisposable
{
	private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);
	private static readonly Regex ReadyLine = new(
		"^" + Regex.Escape(ServiceProtocol.ReadyPrefix) + @"\{""port"":(\d{1,5}),""pid"":(\d{1,10}),""version"":""[^""]*""\}$", RegexOptions.CultureInvariant);

	private readonly object gate = new();
	private readonly Lazy<ProcessJob> job = new();
	private ServiceState state = new ServiceState.NotStarted();

	public ServiceState State
	{
		get
		{
			lock (gate)
			{
				return state;
			}
		}
	}

	/// <summary>
	/// The endpoint of the running service, which is started first when it is not running. Concurrent callers share one
	/// start. A service that failed to start, exited, or was stopped is started again. A service whose console window does
	/// not match the setting is replaced: the start stops it first, on a pool thread, so the caller never waits for it.
	/// </summary>
	/// <exception cref="ServiceUnavailableException">The start failed. <see cref="State"/> holds the same reason.</exception>
	public Task<ServiceEndpoint> EnsureStartedAsync(CancellationToken ct)
	{
		bool show = showConsole();
		Task<ServiceEndpoint> start;
		ServiceState.Running? replaced = null;
		lock (gate)
		{
			switch (state)
			{
				case ServiceState.Running running when !HasExited(running.Process) && running.ConsoleShown == show:
					return Task.FromResult(running.Endpoint);
				case ServiceState.Starting starting:
					start = starting.Start;
					break;
				default:
					if (state is ServiceState.Running live)
					{
						if (HasExited(live.Process))
						{
							live.Process.Dispose();
						}
						else
						{
							replaced = live;
						}
					}
					var pending = new TaskCompletionSource<ServiceEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
					var begun = new ServiceState.Starting(pending.Task);
					state = begun;
					Task.Run(() =>
					{
						if (replaced is not null)
						{
							ShutDown(replaced);
						}
						Start(begun, pending);
					});
					start = pending.Task;
					break;
			}
		}
		if (replaced is not null)
		{
			log("Restarting the local API service to " + (show ? "show" : "hide") + " its console window");
		}
		return WithCancellation(start, ct);
	}

	/// <summary>One log line that says the service answers, or why it does not. It starts the service when needed and never throws.</summary>
	public async Task<string> CheckAsync()
	{
		try
		{
			ServiceEndpoint endpoint = await EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);
			using HttpClient client = endpoint.CreateClient(HealthTimeout);
			using HttpResponseMessage response = await client.GetAsync(ServiceProtocol.HealthPath).ConfigureAwait(false);
			return response.IsSuccessStatusCode
				? $"Local API service ready on {endpoint.BaseAddress.Authority} (pid {endpoint.Pid})"
				: $"Local API service on {endpoint.BaseAddress.Authority} answered its health check with HTTP {(int)response.StatusCode}";
		}
		catch (Exception error) when (error is ServiceUnavailableException or HttpRequestException or TaskCanceledException)
		{
			return "Local API service unavailable: " + error.Message;
		}
	}

	/// <summary>Asks the service to shut down, waits for the exit, and kills it when it does not exit in time. Never throws.</summary>
	public void Stop()
	{
		ServiceState previous;
		lock (gate)
		{
			previous = state;
			state = new ServiceState.Stopped();
		}
		// A start in flight sees the changed state and kills its own process.
		if (previous is ServiceState.Running running)
		{
			ShutDown(running);
		}
	}

	/// <summary>Asks a service that is no longer the current one to shut down, and kills it when it does not exit in time.</summary>
	private void ShutDown(ServiceState.Running running)
	{
		using Process process = running.Process;
		if (HasExited(process))
		{
			return;
		}
		RequestShutdown(running.Endpoint, launch.ShutdownRequestTimeout);
		if (!process.WaitForExit((int)launch.ShutdownExitTimeout.TotalMilliseconds))
		{
			log("Local API service did not exit in time and was killed");
			Kill(process);
		}
	}

	/// <summary>Closes the job object, which kills the service at once.</summary>
	public void Dispose()
	{
		lock (gate)
		{
			(state as ServiceState.Running)?.Process.Dispose();
			state = new ServiceState.Stopped();
		}
		if (job.IsValueCreated)
		{
			job.Value.Dispose();
		}
	}

	private void Start(ServiceState.Starting begun, TaskCompletionSource<ServiceEndpoint> pending)
	{
		ServiceState next;
		try
		{
			next = Launch();
		}
		catch (Exception error)
		{
			// Every failure ends in Failed. An exception that escaped here would leave the callers waiting for ever.
			next = new ServiceState.Failed("The local API service could not be started: " + error.Message);
		}
		lock (gate)
		{
			if (ReferenceEquals(state, begun))
			{
				state = next;
			}
			else
			{
				if (next is ServiceState.Running unwanted)
				{
					Kill(unwanted.Process);
					unwanted.Process.Dispose();
				}
				next = new ServiceState.Failed("The local API service was stopped while it started.");
			}
		}
		if (next is ServiceState.Running running)
		{
			pending.SetResult(running.Endpoint);
		}
		else
		{
			pending.SetException(new ServiceUnavailableException(((ServiceState.Failed)next).Reason));
		}
	}

	private ServiceState Launch()
	{
		string exe = launch.Executable();
		if (!File.Exists(exe))
		{
			return new ServiceState.Failed($"The local API service was not found at {exe}.");
		}
		string token = NewToken();
		int parentPid;
		using (Process self = Process.GetCurrentProcess())
		{
			parentPid = self.Id;
		}
		bool consoleShown = showConsole();
		var info = new ProcessStartInfo(exe)
		{
			// A trailing backslash would escape the closing quote.
			Arguments = $"{ServiceProtocol.ParentPidArgument} {parentPid.ToString(CultureInfo.InvariantCulture)} {ServiceProtocol.LogFolderArgument} \"{launch.LogFolder.TrimEnd('\\')}\"",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			CreateNoWindow = !consoleShown,
		};
		// The environment, not the command line, which any process of the user can read.
		foreach (KeyValuePair<string, string> variable in launch.Environment)
		{
			info.EnvironmentVariables[variable.Key] = variable.Value;
		}
		info.EnvironmentVariables[ServiceProtocol.TokenVariable] = token;

		var process = new Process { StartInfo = info };
		var ready = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var errors = new ErrorLines();
		process.OutputDataReceived += (_, e) =>
		{
			// A null line is the end of stdout: the process exited, or closed it, before it was ready.
			if (e.Data is null || e.Data.StartsWith(ServiceProtocol.ReadyPrefix, StringComparison.Ordinal))
			{
				ready.TrySetResult(e.Data);
			}
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is { Length: > 0 } line && errors.Add(line) && ready.Task.IsCompleted)
			{
				log("Local API service: " + line);
			}
		};
		process.Start();
		try
		{
			JoinJob(process);
			// Reading both pipes for the life of the process keeps a full pipe from blocking the service.
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			if (!ready.Task.Wait(launch.ReadyTimeout))
			{
				Kill(process);
				process.Dispose();
				return new ServiceState.Failed(
					$"The local API service did not report that it was ready within {launch.ReadyTimeout.TotalSeconds:0.#} seconds." + errors.Describe());
			}
			if (ready.Task.Result is not { } line)
			{
				if (!process.WaitForExit((int)ExitTimeout.TotalMilliseconds))
				{
					Kill(process);
				}
				// The overload without a timeout also waits for the rest of stderr.
				process.WaitForExit();
				int exitCode = process.ExitCode;
				process.Dispose();
				return new ServiceState.Failed(DescribeExit(exitCode, errors));
			}
			if (ReadyLine.Match(line) is not { Success: true } match)
			{
				Kill(process);
				process.Dispose();
				return new ServiceState.Failed("The local API service reported that it was ready in a form this plugin does not know: " + line);
			}
			int port = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
			int pid = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			return new ServiceState.Running(new ServiceEndpoint(new Uri($"http://{ServiceProtocol.LoopbackHost}:{port}/"), token, pid), process, consoleShown);
		}
		catch
		{
			Kill(process);
			process.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Windows can refuse a job object, for example when ComicRack itself runs in a job that forbids one. The service then
	/// still stops when this process exits, through its watch on the parent process, so it keeps running.
	/// </summary>
	private void JoinJob(Process process)
	{
		try
		{
			(joinJob ?? job.Value.Add)(process);
		}
		catch (Win32Exception error)
		{
			log($"The local API service could not join a job object ({error.Message}), so only its watch on this process stops it if ComicRack crashes.");
		}
	}

	private static string DescribeExit(int exitCode, ErrorLines errors)
	{
		// The apphost's own message when it finds no .NET, or no ASP.NET Core 11, on the machine.
		if (errors.Contains("You must install"))
		{
			return "The local API service needs the .NET 11 runtime (ASP.NET Core Runtime 11, x64), which is not installed. "
				+ $"Download it from {ServiceProtocol.RuntimeDownloadUrl} and restart ComicRack.";
		}
		return $"The local API service exited with code {exitCode} before it was ready." + errors.Describe();
	}

	private static void RequestShutdown(ServiceEndpoint endpoint, TimeSpan timeout)
	{
		try
		{
			// On a pool thread, so the wait cannot deadlock on the UI thread's synchronization context.
			Task.Run(async () =>
			{
				using HttpClient client = endpoint.CreateClient(timeout);
				using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.ShutdownPath, null).ConfigureAwait(false);
			}).Wait(timeout);
		}
		catch (AggregateException)
		{
			// The service does not answer. The caller kills it when it does not exit.
		}
	}

	private static string NewToken()
	{
		byte[] bytes = new byte[32];
		using (var random = RandomNumberGenerator.Create())
		{
			random.GetBytes(bytes);
		}
		return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private static bool HasExited(Process process)
	{
		try
		{
			return process.HasExited;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}

	private static void Kill(Process process)
	{
		try
		{
			process.Kill();
			process.WaitForExit((int)ExitTimeout.TotalMilliseconds);
		}
		catch (Exception error) when (error is InvalidOperationException or Win32Exception)
		{
			// It has already exited.
		}
	}

	private static async Task<ServiceEndpoint> WithCancellation(Task<ServiceEndpoint> start, CancellationToken ct)
	{
		if (!ct.CanBeCanceled)
		{
			return await start.ConfigureAwait(false);
		}
		var cancelled = new TaskCompletionSource<ServiceEndpoint>();
		using (ct.Register(() => cancelled.TrySetCanceled()))
		{
			return await (await Task.WhenAny(start, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
		}
	}

	/// <summary>The first lines of the service's stderr, which name the cause of a failed start. The cap keeps a flood out of the log.</summary>
	private sealed class ErrorLines
	{
		private const int Limit = 40;

		private readonly List<string> lines = [];

		/// <summary>False once the cap is reached and the line was dropped.</summary>
		public bool Add(string line)
		{
			lock (lines)
			{
				if (lines.Count == Limit)
				{
					return false;
				}
				lines.Add(line);
				return true;
			}
		}

		public bool Contains(string text)
		{
			lock (lines)
			{
				return lines.Exists(line => line.IndexOf(text, StringComparison.Ordinal) >= 0);
			}
		}

		public string Describe()
		{
			lock (lines)
			{
				return lines.Count == 0 ? "" : " It wrote: " + string.Join(" | ", lines);
			}
		}
	}
}