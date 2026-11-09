# AGENTS.md

Guidance for an agent working in this repository. See [`README.md`](README.md) for what the project does and how to install it; this file covers how to build, test, and change it.

## What this is

A .NET Framework 4.8 ComicRack plugin, `ComicApiPlugin`, that is a native C# port of Cory Banack's Python [Comic Vine Scraper](https://github.com/cbanack/comic-vine-scraper) (v1.0.102). It must behave like the Python plugin except where `docs/port/deviations.md` says otherwise.

## Build

```powershell
$env:COMICRACK_DIR = "C:\path\to\ComicRackCE\ComicRack\bin\Release\net48"
./Build.ps1            # build, then run both test suites
./Build.ps1 -Rebuild
./Build.ps1 -SkipTests
```

- Use `Build.ps1`, not `dotnet build`, for the plugin and test projects. They carry binary `.resx` resources that `dotnet build` cannot compile for .NET Framework.
- `src/ComicApiPlugin.Core` has no ComicRack dependency and no `.resx`. `dotnet build src/ComicApiPlugin.Core/ComicApiPlugin.Core.csproj` works directly and is the fastest way to check a Core-only change compiles.
- Everything else (`src/ComicApiPlugin`, both test projects, `tools/smoke`, `tools/differential`) needs `ComicRackDir`/`COMICRACK_DIR` pointed at a ComicRack CE build of `WizardsAreNeverLate/ComicRackCE@integration` (branch includes `feat/dotnet-plugins`). Without it, `Directory.Build.targets` fails the build with a clear error rather than a missing-reference error.

## Tests

- `tests/ComicApiPlugin.Tests`: TUnit unit tests against recorded Comic Vine responses.
- `tests/ComicApiPlugin.Oracle.Tests`: runs the original Python modules under IronPython, in-process, and compares results against the C# port.
- `tools/smoke`, `tools/differential`: in-app checks that start ComicRack itself. Not part of the normal test loop.

**Never invoke a Python interpreter directly** (`python`, `python3`, `py`, `ipy`). A hook blocks it, and it would hang on stdin even without the hook — this machine's `python3` is the Windows Store stub, not a real interpreter. To check or compare Python behaviour, write an oracle test that calls `Oracle.Call` (see `tests/ComicApiPlugin.Oracle.Tests/Oracle.cs`); IronPython 2.7.4 runs inside the test process, loaded from ComicRack CE's own `IronPython.dll`.

## Conventions

- Tabs for indentation, `Nullable` enabled, `ImplicitUsings` disabled, warnings are errors (`Directory.Build.props`). A change that adds a warning fails the build, by design.
- A behavioural difference from the Python plugin is not a bug to silently fix. Add an entry to `docs/port/deviations.md` (Python behaviour, C# behaviour, source reference, pinning test) and only then change the code. The oracle tests skip exactly the inputs listed there.

## Formats that must not change

`settings.dat`, `advanced.dat`, `series.dat`, `geometry.dat`, the `Script.ComicVineScraper` localization resource key, and the `CVDBn`/`CVDBSKIP` tags written into comic books are shared with the Python plugin. Both plugins read and write the same files in `%APPDATA%\Comic Vine Scraper`, so a format change breaks a user who runs both side by side or switches between them. Don't rename or reshape these as part of an unrelated refactor.

The command key is `comic-api-net`, intentionally different from the Python plugin's `comic-vine-scraper`, so both can be installed and run at once for comparison (see `docs/port/architecture.md`). It is localized per-language by a generated `comic-api-net.xml` entry inside each zip in `src/ComicApiPlugin/Package/Languages/`; regenerate those with `tools/Add-NetCommandEntry.ps1` if the key or its source `comic-vine-scraper.xml` text changes, don't hand-edit the zips.

## Docs that are living vs. frozen

- `docs/port/architecture.md`, `deviations.md`, and the `map-*.md` files describe the current design. Keep them in sync when you change the thing they describe.
- `.audit/cvs-port.tsv` and `docs/port/inapp-phase2-report.txt` are frozen records of specific past runs and decisions (timestamps, captured output). Don't edit old entries to match later renames or decisions — append new rows/files instead if the activity needs a new record.

## Project layout

| Path | Contents |
|---|---|
| `src/ComicApiPlugin.Core` | Engine, matching, Comic Vine client and settings. No WinForms or ComicRack references. |
| `src/ComicApiPlugin` | The ComicRack plugin: commands, forms, and the mapping to `ComicBook`. |
| `tests/ComicApiPlugin.Tests`, `tests/ComicApiPlugin.Oracle.Tests` | The two test suites described above. |
| `tools/smoke`, `tools/differential` | In-app probes driven against a real ComicRack process. |
| `docs/port` | Port plan, architecture, and the list of deviations from the original. |
| `.audit/cvs-port.tsv` | Decision log for the port (historical, see above). |
