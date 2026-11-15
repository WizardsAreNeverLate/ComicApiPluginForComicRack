using System;
using System.Globalization;
using System.IO;

namespace ComicApiPlugin.Service;

/// <summary>What one launch was given. The token comes from the environment only.</summary>
/// <param name="ParentPid">The process whose exit stops the service, or null when it runs on its own.</param>
internal sealed record ServiceOptions(int? ParentPid, string LogFolder, string Token)
{
	public static string DefaultLogFolder =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicApiPlugin", "logs");

	/// <exception cref="ArgumentException">The launch is not usable. The message says why and never holds the token.</exception>
	public static ServiceOptions Parse(string[] args, Func<string, string?> environment)
	{
		int? parentPid = null;
		string logFolder = DefaultLogFolder;
		for (int i = 0; i < args.Length; i++)
		{
			string name = args[i];
			if (name is not (ServiceProtocol.ParentPidArgument or ServiceProtocol.LogFolderArgument))
			{
				throw new ArgumentException($"Unknown argument '{name}'.");
			}
			if (++i == args.Length)
			{
				throw new ArgumentException($"{name} needs a value.");
			}
			if (name == ServiceProtocol.LogFolderArgument)
			{
				logFolder = args[i];
			}
			else if (int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
			{
				parentPid = pid;
			}
			else
			{
				throw new ArgumentException($"{name} needs a process id, not '{args[i]}'.");
			}
		}
		if (environment(ServiceProtocol.TokenVariable) is not { Length: > 0 } token)
		{
			throw new ArgumentException($"The environment variable {ServiceProtocol.TokenVariable} is not set. Every request must carry its value as a bearer token.");
		}
		return new ServiceOptions(parentPid, logFolder, token);
	}
}
