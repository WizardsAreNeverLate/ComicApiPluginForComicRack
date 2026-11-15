using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace ComicApiPlugin.Service;

internal static class Program
{
	private const int ExitBadLaunch = 2;
	private const int ExitParentGone = 3;

	private static readonly string Version = typeof(Program).Assembly.GetName().Version!.ToString(3);

	public static async Task<int> Main(string[] args)
	{
		ServiceOptions options;
		try
		{
			options = ServiceOptions.Parse(args, Environment.GetEnvironmentVariable);
		}
		catch (ArgumentException error)
		{
			Console.Error.WriteLine(error.Message);
			return ExitBadLaunch;
		}
		// A process this one starts must not inherit the token.
		Environment.SetEnvironmentVariable(ServiceProtocol.TokenVariable, null);

		Process? parent = null;
		if (options.ParentPid is { } parentPid && (parent = ParentWatch.TryOpen(parentPid)) is null)
		{
			Console.Error.WriteLine($"The parent process {parentPid} is not running.");
			return ExitParentGone;
		}

		// stdout carries the ready line to the plugin, so the log's console output goes to the console window itself.
		TextWriter ready = Console.Out;
		TextWriter? window = ConsoleWindow.TryOpen();
		Log.Logger = Logger(options.LogFolder, window);
		try
		{
			WebApplication app = Build(options, parent);
			await app.StartAsync();
			int port = new Uri(app.Urls.Single()).Port;
			Log.Information("Comic API service {Version} listening on {Host}:{Port} (pid {Pid}, parent {ParentPid})",
				Version, ServiceProtocol.LoopbackHost, port, Environment.ProcessId, options.ParentPid);
			ready.WriteLine(ServiceProtocol.ReadyPrefix + JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, version = Version }));
			ready.Flush();
			await app.WaitForShutdownAsync();
			Log.Information("Stopped");
			return 0;
		}
		catch (Exception error)
		{
			Log.Fatal(error, "The service stopped on an error");
			Console.Error.WriteLine(error);
			return 1;
		}
		finally
		{
			await Log.CloseAndFlushAsync();
			window?.Dispose();
		}
	}

	private static WebApplication Build(ServiceOptions options, Process? parent)
	{
		// No command line or ASPNETCORE_URLS reaches the host, so nothing can move the listener off loopback.
		WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
		builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
		builder.Services.AddLogging();
		builder.Services.AddRoutingCore();
		builder.Services.AddSerilog();
		// The plugin waits three seconds for the exit after it asks for a shutdown.
		builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(2));
		if (parent is not null)
		{
			builder.Services.AddHostedService(services => ActivatorUtilities.CreateInstance<ParentWatch>(services, parent));
		}

		WebApplication app = builder.Build();
		// Method, path without the query string, status and elapsed time. Headers are never logged: they carry the token and the provider keys.
		app.UseSerilogRequestLogging();
		var guard = new RequestGuard(options.Token);
		app.Use(guard.Invoke);
		app.UseRouting();

		var started = Stopwatch.StartNew();
		app.MapGet(ServiceProtocol.HealthPath, () => Results.Json(new Health(Version, Environment.ProcessId, (long)started.Elapsed.TotalSeconds)));
		app.MapPost(ServiceProtocol.ShutdownPath, (HttpContext context, IHostApplicationLifetime lifetime) =>
		{
			context.Response.OnCompleted(() =>
			{
				lifetime.StopApplication();
				return Task.CompletedTask;
			});
			return Results.Accepted();
		});
		return app;
	}

	private static Serilog.ILogger Logger(string logFolder, TextWriter? window)
	{
		LoggerConfiguration config = new LoggerConfiguration()
			.MinimumLevel.Information()
			// ASP.NET Core's own request log includes the query string.
			.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
			.Destructure.With<SecretHeaderPolicy>()
			.WriteTo.File(Path.Combine(logFolder, "service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
				formatProvider: CultureInfo.InvariantCulture);
		if (window is not null)
		{
			Console.SetOut(window);
			config = config.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
		}
		return config.CreateLogger();
	}

	private sealed record Health(string Version, int Pid, long UptimeSeconds);
}
