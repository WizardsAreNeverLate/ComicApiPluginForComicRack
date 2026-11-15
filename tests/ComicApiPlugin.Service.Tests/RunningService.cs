using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComicApiPlugin.Service.Tests;

/// <summary>The built service exe, started as the plugin starts it: the token in the environment, stdout and stderr as pipes.</summary>
internal sealed class RunningService : IDisposable
{
	public const string Token = "test-token-b5Zk1cQ";

	private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

	private RunningService(Process process, string logFolder)
	{
		Process = process;
		LogFolder = logFolder;
	}

	public Process Process { get; }

	public string LogFolder { get; }

	public int Port { get; private set; }

	public int ReportedPid { get; private set; }

	public string ReportedVersion { get; private set; } = "";

	/// <summary>Every stdout line the service wrote before and including the ready line.</summary>
	public List<string> StandardOutput { get; } = [];

	public static Process Launch(string? token, int? parentPid, string logFolder)
	{
		var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ComicApiPlugin.Service.exe"))
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		start.ArgumentList.Add(ServiceProtocol.LogFolderArgument);
		start.ArgumentList.Add(logFolder);
		if (parentPid is { } pid)
		{
			start.ArgumentList.Add(ServiceProtocol.ParentPidArgument);
			start.ArgumentList.Add(pid.ToString());
		}
		start.Environment.Remove(ServiceProtocol.TokenVariable);
		if (token is not null)
		{
			start.Environment[ServiceProtocol.TokenVariable] = token;
		}
		return Process.Start(start)!;
	}

	public static async Task<RunningService> StartAsync(int? parentPid = null)
	{
		string logFolder = NewLogFolder();
		var service = new RunningService(Launch(Token, parentPid, logFolder), logFolder);
		try
		{
			Task<string?> ready = service.ReadUntilReadyAsync();
			if (await Task.WhenAny(ready, Task.Delay(ReadyTimeout)) != ready || await ready is not { } line)
			{
				throw new InvalidOperationException("The service did not report ready. stderr: " + await service.Process.StandardError.ReadToEndAsync());
			}
			using JsonDocument json = JsonDocument.Parse(line[ServiceProtocol.ReadyPrefix.Length..]);
			service.Port = json.RootElement.GetProperty("port").GetInt32();
			service.ReportedPid = json.RootElement.GetProperty("pid").GetInt32();
			service.ReportedVersion = json.RootElement.GetProperty("version").GetString()!;
			return service;
		}
		catch
		{
			service.Dispose();
			throw;
		}
	}

	public static string NewLogFolder() => Path.Combine(Path.GetTempPath(), "comicapi-service-tests", Guid.NewGuid().ToString("N"));

	public HttpClient Client(string? token = Token)
	{
		var client = new HttpClient { BaseAddress = new Uri($"http://{ServiceProtocol.LoopbackHost}:{Port}") };
		if (token is not null)
		{
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}
		return client;
	}

	public async Task<bool> ExitsWithinAsync(TimeSpan limit)
	{
		Task exit = Process.WaitForExitAsync();
		return await Task.WhenAny(exit, Task.Delay(limit)) == exit;
	}

	/// <summary>The log files hold the whole run only once the service has exited.</summary>
	public string ReadLogs()
	{
		string text = "";
		foreach (string file in Directory.GetFiles(LogFolder, "service-*.log"))
		{
			text += File.ReadAllText(file);
		}
		return text;
	}

	public void Dispose()
	{
		try
		{
			if (!Process.HasExited)
			{
				Process.Kill();
				Process.WaitForExit(5000);
			}
		}
		catch (InvalidOperationException)
		{
		}
		Process.Dispose();
		try
		{
			Directory.Delete(LogFolder, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private async Task<string?> ReadUntilReadyAsync()
	{
		while (await Process.StandardOutput.ReadLineAsync() is { } line)
		{
			StandardOutput.Add(line);
			if (line.StartsWith(ServiceProtocol.ReadyPrefix, StringComparison.Ordinal))
			{
				return line;
			}
		}
		return null;
	}
}
