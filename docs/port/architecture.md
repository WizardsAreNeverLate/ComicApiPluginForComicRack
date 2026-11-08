# Architecture

This document describes the design that the C# port of Comic Vine Scraper is built on. The type sketch in `sketch/` is the contract, and `sketch/README.md` gives the reading order.

The design came from two independent sketches, one by an Opus model and one by a Fable model. A Sonnet model judged them. See [Synthesis decision](#synthesis-decision).

## Problem

The port must behave like the Python plugin, except for the bug fixes listed in `deviations.md`. Four facts from ComicRackCE constrain the design:

- ComicRack runs a `Books` command synchronously on the UI thread. It sets an undo marker before the call (`ComicRack/ScriptUtility.cs:95-113`). The `Editor` command receives a copy of the book, and ComicRack reads that copy back as soon as the command returns (`ComicRack/Dialogs/ComicBookDialog.cs:1126-1143`). The command must therefore not return until the scrape is complete. If it returns early, the undo group misses the writes and the book dialog loses the result.
- ComicRack creates one instance per plugin class, with `IPluginEnvironment`. A `ConfigScript` method with the same `Key` becomes the **Configure** action of the scrape command.
- The Python write-back downloads the thumbnail in the middle of writing the fields. If the download fails, the book is left half-written.
- `settings.dat`, `advanced.dat`, `series.dat`, `geometry.dat`, the `Script.ComicVineScraper` localization key, and the `CVDBn` tags are formats shared with the Python plugin. They must not change.

## Usage

The plugin class is the only caller of the engine:

```csharp
[PluginCommand("Books,Editor", Key = "comic-vine-scraper", Name = "Comic Vine Scraper", Image = "comicvinescraper.png")]
public void Scrape(ComicBook[] books)
{
	var config = store.Load();
	RunSummary summary = ScrapeHost.Run(env.MainWindow, ctx, (host, ct) =>
		new ScrapeRun(Services(host, config), config)
			.RunAsync(books.Select(b => new ComicRackBook(b, env.App)).ToList(), ct));
	if (config.SummaryDialog) FinishDialog.Show(env.MainWindow, ctx, summary);
}

[PluginCommand("ConfigScript", Key = "comic-vine-scraper")]
public void Configure() => ConfigDialog.Show(env.MainWindow, ctx, store);
```

A test drives the same engine with no UI. It uses scripted dialog answers and recorded Comic Vine responses:

```csharp
var book = new FakeBook(Snapshots.Named("Batman 012 (1990).cbz"));
var ui = new ScriptedUi().Series(q => new SeriesChoice.Pick(q.Candidates[0].Series, ShowIssues: false));
var run = new ScrapeRun(Services.With(ui, ComicVineFake.FromFixtures("batman")), Configs.Default);
var steps = new List<StepKind>();
run.StepEntered += (_, step) => steps.Add(step);
RunSummary summary = await run.RunAsync([book], CancellationToken.None);
// summary.Scraped == 1, book.Applied has exactly one BookPatch, and steps lists the path the book took.
```

An oracle test passes the same input to a pure Core function and to its Python original, which runs under IronPython 2.7.4. It then compares the two results.

## Shape

### Projects

All projects target net48 and AnyCPU, with LangVersion 14 and PolySharp. The plugin needs no NuGet packages at run time.

- `ComicVineScraper.Core.dll` contains the model, the merge rules, the engine, the Comic Vine client, the configuration and `.dat` files, the algorithms, and the debug log. It references System.Drawing, System.Net.Http, and System.Xml.Linq, and nothing from ComicRack or WinForms. The compiler therefore stops Core from naming `ComicBook` or `Form`.
- `ComicVineScraper.dll` contains the plugin entry points, the `ComicBook` adapter, the forms, i18n, and the error dialogs. It references ComicRack with `Private=false`.
- The `.crplugin` ships both DLLs flat, with `Package.ini`, the images, and the language zips. ComicRack does not scan Core for commands, because Core does not reference `ComicRack.Plugins`.
- `ComicVineScraper.Tests` uses TUnit on x64. It covers Core, the recorded responses, and the adapter round trip.
- `ComicVineScraper.Oracle.Tests` holds the IronPython differential tests, so the fast suite never loads IronPython.

### Core types

- `SeriesId` and `IssueId` are typed ids. `IssueId.TryParseStored` is the only parser for stored keys, and it accepts `12.0` (deviation 9).
- `PartialDate` cannot hold a month without a year. Python's `-1` sentinels become `int?`.
- `BookFields` is a `[Flags]` enum that replaces Python's `dont_update` strings. A write is a `BookPatch`: the values, plus the fields to write.
- `FieldTable` is the one list of the 21 update settings. The settings keys, the ConfigForm checkboxes, and their labels all come from it.
- `ChooseMode { Ask, AutoChooseSeries, ConfirmIssue }` replaces two booleans that must never both be true.
- Each dialog returns a closed choice type: `SearchChoice`, `SeriesChoice`, or `IssueChoice`. `BookOutcome` replaces the string `BookStatus`.
- `BookFacts` is the engine's input for one book. It holds the book snapshot plus derived values: the name fields filled in from the filename, the stored marker (none, skip forever, or a known issue), and the unique series key.

### ComicRack boundary

- `ComicRackBook` is the only code that touches a ComicRack `ComicBook`. It has three methods: `Read`, `GetPage`, and `Apply(patch, thumbnail)`.
- `MergeRules.Plan` is a pure function. It is the only place that holds the overwrite, ignore-blanks, and update rules, the publisher and imprint mapping, the date rules, and the tag and note rewrite. It uses one rule row per field.
- The engine finishes all I/O for a book before it writes: first the issue details, then the thumbnail. It then reads the book again, plans the patch, and applies it in one UI-thread step with no `await`. A cancel or a network failure can therefore not leave a book half-written.
- Reading the book again just before the write means the merge always uses the book's current values.
- `Apply` is idempotent. A round-trip test reads a book, applies every field, and reads it again. An exhaustive test checks that every `BookFields` bit is handled.

### Engine

Each book moves through a state machine:

```
Begin → Search(manual, failedTerms) → ChooseSeries(terms, found) → FindIssue → Write → Done(outcome)
```

- `SkipForever` is a final step.
- Python restarts a book from the top on `UNSCRAPED`. Here that is the edge to `Search(Manual: true, failed)`.
- `BACK` returns to `ChooseSeries` with the same results, or to a manual `Search`.
- The run owns a cache of resolved series, keyed by the unique series key. A delayed book's second pass turns off fast rescrape and auto-choose.
- `StepEntered` reports each step, so a test can assert the path a book took.
- The engine reaches outside Core only through `IScrapeBook`, `IScrapeUi`, `IComicVine`, `IClock`, and `CoverPreferences`.

### Threading

- Everything runs on ComicRack's UI thread. Only HTTP requests, XML parsing, and image hashing leave it.
- `ScrapeHost.Run` opens the ComicForm with `ShowDialog(MainWindow)`. The form's `Shown` handler starts the async run on the WinForms `SynchronizationContext`, and the form closes when the run ends. The command therefore stays blocked until the run is done, and ComicRack's windows keep painting.
- The search, series, and issue dialogs and the message boxes are modal to the ComicForm. ProgressBarForm is a non-modal child that receives progress through `IProgress<QueryProgress>`.
- One `CancellationToken` covers the whole run. Each open dialog registers the token, so a cancel closes the dialog and it returns `Cancel`. Closing the ComicForm or a progress form cancels the run.
- The cover panels load images through `LatestImageLoader`. Each new request cancels the previous one, which replaces Python's `Scheduler`.
- This design removes `DoEvents`, the second `Application.Run` thread, the Monitor handshake, `utils.invoke`, and `GC.Collect`.

### Comic Vine client

- `IComicVine` returns domain types only. It covers search, the issues in a series, finding an issue by number, issue details, resolving a series, and images. `ComicVineCatalog` implements it for one run. It owns the caches, the search cleanup, paging, and the ignore filters.
- `ComicVineHttp` lives as long as the plugin. It owns the `HttpClient`, and it takes an injectable `HttpMessageHandler`, so recorded-response tests need no network. It also owns:
  - a throttle of 1100 ms between requests across threads (deviation 6)
  - one retry after 2500 ms for network errors, 5xx responses, empty bodies, and invalid XML
  - no retry for codes 100 or 107 (deviation 5)
  - reading the body on a 4xx status, so HTTP 401 shows the invalid-key message (deviation 4)
  - an invalid-character filter that keeps valid surrogate pairs (deviation 8)
- `ComicVineException` carries the error kind and the URL with the API key removed. `IsRecoverable` decides whether a failed fast rescrape delays the book or ends the run. An invalid key or a rate limit ends the run.
- XML is parsed into domain types with `XDocument`. There is no port of `xml2py`.

### Configuration, i18n, and logging

- `Configuration` is an immutable record. `MergeSettings` and `SearchSettings` are computed from it, never stored.
- `DatFile.Parse` and `DatFile.Format` are pure, and the oracle tests check them in both directions.
- `SettingsStore` is the only code that touches `%APPDATA%\Comic Vine Scraper`. It does the one-time migration, writes atomically (a temp file, then `File.Replace`), uses UTF-8 with a BOM, and keeps `settings.dat` keys it does not know. A user can therefore switch back to the Python plugin.
- The raw text of `advanced.dat` is the source of truth for the advanced settings.
- Core contains no text that the user sees. `Strings` reads the English defaults from the bundled `en.zip`. A test checks that every string key the code uses exists in `Script.ComicVineScraper.xml`.
- `DebugLog` is an in-memory, thread-safe log that the user saves with Ctrl+Shift+L or from an error prompt. Errors are caught at the two commands and shown by `ErrorDialogs`. The plugin installs no process-wide exception handler.

## Synthesis decision

The design uses the Opus sketch as its base. Both sketches agreed on the structure: a command that blocks until the scrape ends, a Core assembly without WinForms or ComicRack, a state machine for each book, a typed field set, a pure plan followed by a one-step apply, typed ids, and closed dialog choices.

The two sketches differed on what the user can do while a scrape runs:

- The Fable sketch kept ComicRack's main window usable with a nested `Application.Run`, which is closer to Python's `DoEvents` loop.
- The Opus sketch runs the scrape inside the ComicForm's `ShowDialog`, which disables the main window.

The judge chose Opus, mainly for data safety. The Fable sketch planned the write from the snapshot taken when the run started and then awaited the cover download. A user edit made during the run would therefore be overwritten, and the fields and the thumbnail were written in two calls. With a usable main window, the user could also start a second scrape or close ComicRack in the middle of a run.

Grafts from the Fable sketch:

- the `StepEntered` trace event
- the sketch reading order (`sketch/README.md`)
- the undo-group evidence in [Problem](#problem)

The judge also asked for an injectable HTTP handler and a store for alternate covers. The Opus sketch already had both (`ComicVineHttp(HttpMessageHandler …)` and `CoverPreferences`). The synthesis only adds `CoverPreferences` to `ScrapeServices`.

Rejected from the Fable sketch:

- the nested message loop
- planning the write from the snapshot taken at the start of the run
- writing the thumbnail and the fields in separate calls
- a `ThreadException` handler that each session installs

## Tradeoffs accepted

- We accept that ComicRack's main window is disabled during a scrape. In exchange, the command blocks, which the undo group and the book dialog need, and no edit or second scrape can race the merge. This changes Python's behaviour, and `deviations.md` records it.
- We accept reading each book twice in exchange for merging against its current values.
- We accept a long switch statement in `ComicRackBook.Apply` in exchange for keeping all `ComicBook` field mapping in one file, with an exhaustive test.
- We accept shipping two DLLs instead of one, so that the compiler enforces the Core boundary.

## Decisions on the open questions

| Question | Decision | Why |
|---|---|---|
| An unset `ReleasedTime` reads as (1,1,1), not blank | Bug, fixed as deviation 11 | The release date is filled in only when "overwrite existing" is on, which is clearly not intended |
| Skip forever writes the series, number, and year from the filename, and clears the Comic Vine ids | Keep | It fills only blank fields and removes stale ids. That looks intended, not wrong |
| The inferred format is always written to `Format` | Keep | It makes a value that ComicRack inferred explicit. That is not a wrong result |
| `UrlPathEncode` does not escape `&`, `+`, or `#` | Not observable, so closed | Search cleanup removes those characters first. The port uses `Uri.EscapeDataString` |
| `SCRAPE_DELAY` defaults to 1, but parsed values are clamped to 2..3600 | Keep | Unusual but harmless |
| Book sort uses culture-sensitive compare | Keep | The order is visible to users and matches Python |
| A fast rescrape that hits an invalid key or a rate limit | End the run | Every following request would fail the same way |
| `MessageBoxManager.dll` labels buttons in ComicRack's language | Decide in the UI phase | It needs a localized prompt dialog or the Windows button labels |

## Open risks

- Does `PluginEnvironment.Localize` find the same language zips that Python's `ComicRack.Localize` found? The first command in phase 2 checks this.
- Does a minutes-long async run inside the ComicForm's `ShowDialog` keep the undo group, the book dialog's copy, and cancellation working in ComicRack? The phase 2 prototype checks this before the engine is built.
- `associated_images.original_url` may be nested differently from what Python reads. The client phase checks it against a recorded live response.

## Implementation reconciliation

Implementers record accepted deviations from this design here. Each entry says why the shared contract still holds.
