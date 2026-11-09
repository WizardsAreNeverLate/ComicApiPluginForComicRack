# Comic API Plugin for ComicRack CE

[![Build and test](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/actions/workflows/build.yml/badge.svg)](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/actions/workflows/build.yml)

A ComicRack plugin that scrapes comic book details and covers from [Comic Vine](https://comicvine.gamespot.com/) into your library.

This is a native C# (.NET Framework 4.8) port of Cory Banack's [Comic Vine Scraper](https://github.com/cbanack/comic-vine-scraper), version 1.0.102. **It is not a Python plugin.** It is a compiled .NET assembly, and it contains no IronPython code.

## Requirements

> [!IMPORTANT]
> This plugin needs **a modified ComicRack Community Edition that can load .NET plugins.** Stock ComicRack CE loads only Python (IronPython) plugins, and it cannot load this one.

- Build ComicRack CE from branch [`integration`](https://github.com/WizardsAreNeverLate/ComicRackCE/tree/integration) of the fork [WizardsAreNeverLate/ComicRackCE](https://github.com/WizardsAreNeverLate/ComicRackCE). That branch is upstream `master` with this fork's feature branches merged in, including [`feat/dotnet-plugins`](https://github.com/WizardsAreNeverLate/ComicRackCE/tree/feat/dotnet-plugins). That feature adds `[PluginCommand]` attributes, so a compiled class can provide ComicRack commands, and it adds `.crplugin` packages that contain .NET assemblies.
- **Upstream ComicRack CE does not include this support yet:** it hasn't been submitted as a pull request. The feature branch is kept separate from `integration` so that it can be offered upstream on its own.
- You need a Comic Vine API key, which is free. You can get one at <https://comicvine.gamespot.com/api/>.

## Features

The port keeps the original plugin's behaviour:

- It searches for series and issues, matches covers, and has an auto-choose mode.
- It uses the same merge rules and options as the original.
- It has the same dialogs, and it is localized from the original language packs.

Where the original had bugs, the port fixes them instead of reproducing them. Each change is listed in [`docs/port/deviations.md`](docs/port/deviations.md) with the test that pins it.

The plugin reads and writes the original plugin's settings files in `%APPDATA%\Comic Vine Scraper` (`settings.dat`, `advanced.dat`, `series.dat` and `geometry.dat`). You can switch between the two plugins and keep your settings. The command key is `comic-api-net`, not the original's `comic-vine-scraper`, so both plugins can be installed together. They then share those files. This is a deliberate decision ([#2](https://github.com/WizardsAreNeverLate/ComicApiPluginForComicRack/issues/2)), not a temporary state.

## Install

1. Build or get `ComicApiPlugin.crplugin` (see the next section).
2. In a ComicRack CE build of `integration`, open **Edit > Preferences > Scripts**, then install the `.crplugin`. Restart ComicRack when it asks.
3. Select books, then run **Comic API Plugin** from ComicRack's scripts (the toolbar's Scripts button, or the books' right-click Automation menu). On the first run, the plugin asks for your API key.

If you installed an earlier build, its package is named **Comic Vine Scraper .NET**. Uninstall it first. ComicRack installs each package into a folder named after the package, so the old package would stay installed beside this one with the same command key, and ComicRack would load only one of them.

If Windows blocked the downloaded file, the plugin's DLL is blocked too, and ComicRack reports it. Unblock the `.crplugin` (**Properties > Unblock**) before you install it.

## Build

You need Visual Studio 2022 or its Build Tools (MSBuild with the .NET Framework 4.8 targeting pack), and a local build of ComicRack CE from `WizardsAreNeverLate/ComicRackCE@integration`. The plugin compiles against that build's assemblies.

```powershell
$env:COMICRACK_DIR = "C:\path\to\ComicRackCE\ComicRack\bin\Release\net48"
./Build.ps1            # build, then run both test suites
./Build.ps1 -Rebuild   # clean build
./Build.ps1 -SkipTests
```

The package is written to `src\ComicApiPlugin\bin\<Configuration>\net48\ComicApiPlugin.crplugin`.

The CI workflow (`.github/workflows/build.yml`) runs in this order:

1. It builds ComicRack CE from `WizardsAreNeverLate/ComicRackCE@integration`, cached by commit. A manual run can pick another branch or commit.
2. It checks out the original Python plugin for the oracle tests.
3. It runs `Build.ps1 -Configuration Release`.
4. It uploads the `.crplugin` as an artifact.

Use `Build.ps1` rather than `dotnet build`. The projects contain binary `.resx` resources, which `dotnet build` can't compile for .NET Framework.

## Tests

- **`tests/ComicApiPlugin.Tests`:** fast unit tests using [TUnit](https://github.com/thomhurst/TUnit). They run against recorded Comic Vine responses, which are stored with the API key removed.
- **`tests/ComicApiPlugin.Oracle.Tests`:** these tests run the original Python modules under IronPython in the test process. They check that the port gives the same results on a shared set of inputs.
- **`tools/smoke`:** an in-app check that starts ComicRack and drives the plugin over recorded responses.
- **`tools/differential`:** an in-app comparison of the Python and the C# plugin. It runs against the live API, and needs an API key file.

Both in-app tools use a separate ComicRack configuration and a temporary settings folder. They never change your real settings.

## Project layout

| Path | Contents |
|---|---|
| `src/ComicApiPlugin.Core` | Engine, matching, Comic Vine client and settings. No WinForms or ComicRack references. |
| `src/ComicApiPlugin` | The ComicRack plugin: commands, forms, and the mapping to `ComicBook`. |
| `docs/port` | Port plan, architecture, and the list of differences from the original. |
| `.audit/cvs-port.tsv` | Decision log for the port. |

## Licence

Apache License 2.0, the same as the original. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). The original Comic Vine Scraper is by Cory Banack. This project is not affiliated with Comic Vine or GameSpot.
