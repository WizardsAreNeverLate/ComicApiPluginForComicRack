using System;

namespace ComicApiPlugin.Config;

/// <summary>The outcome of <see cref="SettingsStore.Save"/>. Error is null on success; otherwise it says why a file was not written.</summary>
public readonly record struct SaveResult(Exception? Error)
{
	public bool Succeeded => Error is null;
}
