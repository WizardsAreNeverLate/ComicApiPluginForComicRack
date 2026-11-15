using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ComicApiPlugin.Service;

/// <summary>Stops the service when the process that started it exits, however that process ended.</summary>
internal sealed class ParentWatch(Process parent, IHostApplicationLifetime lifetime, ILogger<ParentWatch> log) : BackgroundService
{
	/// <summary>Null when no such process is running, which at startup means the parent has already gone.</summary>
	public static Process? TryOpen(int pid)
	{
		try
		{
			Process process = Process.GetProcessById(pid);
			return process.HasExited ? null : process;
		}
		catch (Exception error) when (error is ArgumentException or InvalidOperationException)
		{
			return null;
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await parent.WaitForExitAsync(stoppingToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		log.LogInformation("Parent process {ParentPid} exited. Stopping.", parent.Id);
		lifetime.StopApplication();
	}
}
