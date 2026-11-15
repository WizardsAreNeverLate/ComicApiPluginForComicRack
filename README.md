# Comic API Plugin for ComicRack CE

[![Build and test](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/actions/workflows/build.yml/badge.svg)](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/actions/workflows/build.yml)

A ComicRack plugin that scrapes comic book details and covers from [Comic Vine](https://comicvine.gamespot.com/) and [VerseDB](https://versedb.com/) into your library.

This is a native C# port of Cory Banack's [Comic Vine Scraper](https://github.com/cbanack/comic-vine-scraper), version 1.0.102. **It is not a Python plugin.** The dialogs and the scrape flow are a .NET Framework 4.8 assembly with no IronPython code. The calls to Comic Vine and VerseDB, and the cover comparison, run in a small local service on .NET 11 that the plugin starts (see [The local service](#the-local-service)).

## Requirements

> [!IMPORTANT]
> This plugin needs **a modified ComicRack Community Edition that can load .NET plugins.** Stock ComicRack CE loads only Python (IronPython) plugins, and it cannot load this one.

- Build ComicRack CE from branch [`integration`](https://github.com/WizardsAreNeverLate/ComicRackCE/tree/integration) of the fork [WizardsAreNeverLate/ComicRackCE](https://github.com/WizardsAreNeverLate/ComicRackCE). That branch is upstream `master` with feature branches merged in:
	- upstream's [`feature/dotnet-plugin`](https://github.com/maforget/ComicRackCE/tree/feature/dotnet-plugin), which loads plugin classes that derive from `DotNetPlugin` and implement hook interfaces such as `IBooks`. This plugin is one of those classes.
	- this fork's [`feat/dotnet-plugins`](https://github.com/WizardsAreNeverLate/ComicRackCE/tree/feat/dotnet-plugins), which installs `.crplugin` packages that contain .NET assemblies.
- **Upstream ComicRack CE has not released this support yet.** `feature/dotnet-plugin` is a branch of upstream, not yet merged into its `master`.
- You need a Comic Vine API key, which is free. You can get one at <https://comicvine.gamespot.com/api/>. A VerseDB token is optional and enables the VerseDB cover search.
- Install the **ASP.NET Core Runtime 11** from <https://dotnet.microsoft.com/download/dotnet/11.0>. Choose the Windows x64 installer. The local service needs it, so without it a scrape stops with a warning that gives the download link, and no book changes.
- Use 64-bit Windows. The service includes a 64-bit native OpenCV library.

## Features

The port keeps the original plugin's behaviour:

- It searches for series and issues, matches covers, and has an auto-choose mode.
- It uses the same merge rules and options as the original.
- It has the same dialogs, and it is localized from the original language packs.

Where the original had bugs, the port fixes them instead of reproducing them. Each change is listed in [`docs/port/deviations.md`](docs/port/deviations.md) with the test that pins it.

The plugin reads and writes the original plugin's settings files in `%APPDATA%\Comic Vine Scraper` (`settings.dat`, `advanced.dat`, `series.dat` and `geometry.dat`). You can switch between the two plugins and keep your settings. The command key is `comic-api-net`, not the original's `comic-vine-scraper`, so both plugins can be installed together. They then share those files. This is a deliberate decision ([#2](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/issues/2)), not a temporary state.

## Install

1. Install the ASP.NET Core Runtime 11 (see Requirements).
2. Build the plugin or get `ComicApiPlugin.crplugin` (see Build). A build writes it to `artifacts\<Configuration>\ComicApiPlugin.crplugin`.
3. In a ComicRack CE build of `integration`, open **Edit > Preferences > Scripts**, then install the `.crplugin`. Restart ComicRack when it asks.
4. Select books, then run **Comic API Plugin** from ComicRack's scripts (the toolbar's Scripts button, or the books' right-click Automation menu). On the first run, the plugin asks for your API key.

As an alternative to step 3, copy the folder `artifacts\<Configuration>\Comic API Plugin\` into `%APPDATA%\cYo\ComicRack Community Edition\Scripts\`. Close ComicRack first.

The package is about 28 MB. Most of that is the OpenCV library that compares covers.

If you installed an earlier build, its package is named **Comic Vine Scraper .NET**. Uninstall it first. ComicRack installs each package into a folder named after the package, so the old package would stay installed beside this one with the same command key, and ComicRack would load only one of them.

If Windows blocked the downloaded file, the plugin's DLL is blocked too, and ComicRack reports it. Unblock the `.crplugin` (**Properties > Unblock**) before you install it.

## Settings

Open the settings dialog with the plugin's Configure action.

- The **Comic Vine** tab holds the API key and a cover check. When you select **Compare the covers of the top search results and accept a single confident match without asking**, you can also set how many results to compare.
- The **VerseDB** tab holds the VerseDB token. Its checkbox accepts a match without asking when VerseDB finds one result and its cover matches the book.
- The **Providers** tab sets the order of the providers and which are on. Its **Show the local API service console window (for debugging)** checkbox opens the service's console. A change takes effect when you save the settings.

## The local service

When ComicRack starts, the plugin starts `ComicApiPlugin.Service.exe`, a local web service on .NET 11. It listens on `127.0.0.1` only, accepts requests that carry a random per-launch token, and stops when ComicRack closes or crashes. The plugin sends every Comic Vine and VerseDB call and every cover comparison to it. [`docs/local-api-service.md`](docs/local-api-service.md) describes the design, the endpoints, and the security controls.

- **Logs.** The service writes one log file each day to `%LOCALAPPDATA%\ComicApiPlugin\logs\service-YYYYMMDD.log` and keeps 14 files. The log never contains API keys or tokens. The plugin writes its own messages about the service to ComicRack's activity log.
- **Unpacked files.** The package holds the service as `service.zip`. The plugin unpacks it to `%LOCALAPPDATA%\ComicApiPlugin\service\` on the first start after an update.
- **Antivirus.** `ComicApiPlugin.Service.exe` is not code-signed, and an unsigned program that is unpacked and started at run time can trigger antivirus software. If the service does not start, check the activity log, then add an exclusion for `%LOCALAPPDATA%\ComicApiPlugin\service\`. The section [Antivirus and Windows security warnings](docs/local-api-service.md#antivirus-and-windows-security-warnings) lists the behaviors that products flag. No antivirus product has been tested.
- **Problems.** [Troubleshooting](docs/local-api-service.md#troubleshooting) maps each symptom to a log line and a cause.

## Build

You need Visual Studio 2026 or its Build Tools (MSBuild 18 with the .NET Framework 4.8 targeting pack), the .NET 11 SDK, and a local build of ComicRack CE from `WizardsAreNeverLate/ComicRackCE@integration`. The plugin compiles against that build's assemblies.

```powershell
$env:COMICRACK_DIR = "C:\path\to\ComicRackCE\ComicRack\bin\Release\net48"
./Build.ps1            # build, then run the three test suites
./Build.ps1 -Rebuild   # clean build
./Build.ps1 -SkipTests
```

The build writes `artifacts\<Configuration>\ComicApiPlugin.crplugin` and the unpacked plugin in `artifacts\<Configuration>\Comic API Plugin\`. The build publishes the service and packs it into the package as `service.zip`.

The CI workflow (`.github/workflows/build.yml`) runs in this order:

1. It builds ComicRack CE from `WizardsAreNeverLate/ComicRackCE@integration`, cached by commit. A manual run can pick another branch or commit.
2. It checks out the original Python plugin for the oracle tests.
3. It installs the .NET 11 SDK and runs `Build.ps1 -Configuration Release`, which builds the service and runs the three test suites.
4. It uploads the `.crplugin` as an artifact.

Use `Build.ps1` rather than `dotnet build`. The plugin and its test projects contain binary `.resx` resources, which `dotnet build` can't compile for .NET Framework. `Build.ps1` builds the .NET 11 projects with the same MSBuild.

## Tests

- **`tests/ComicApiPlugin.Tests`:** fast unit tests using [TUnit](https://github.com/thomhurst/TUnit). They run against recorded Comic Vine responses, which are stored with the API key removed.
- **`tests/ComicApiPlugin.Oracle.Tests`:** these tests run the original Python modules under IronPython in the test process. They check that the port gives the same results on a shared set of inputs.
- **`tests/ComicApiPlugin.Service.Tests`:** tests on .NET 11 that start the local API service as a process and call it over HTTP.
- **`tools/smoke`:** an in-app check that starts ComicRack and drives the plugin over recorded responses.
- **`tools/differential`:** an in-app comparison of the Python and the C# plugin. It runs against the live API, and needs an API key file.

Both in-app tools use a separate ComicRack configuration and a temporary settings folder. They never change your real settings.

## Project layout

| Path | Contents |
|---|---|
| `src/ComicApiPlugin.Core` | Engine, matching, Comic Vine client and settings. No WinForms or ComicRack references. |
| `src/ComicApiPlugin` | The ComicRack plugin: commands, forms, and the mapping to `ComicBook`. |
| `src/ComicApiPlugin.Service` | The local API service (.NET 11) that the plugin starts. It ships in the package as `service.zip`. |
| `docs/port` | Port plan, architecture, and the list of differences from the original. |
| `.audit/cvs-port.tsv` | Decision log for the port. |

## Licence

Apache License 2.0, the same as the original. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). The original Comic Vine Scraper is by Cory Banack. This project is not affiliated with Comic Vine or GameSpot.
