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

### Phase 2: scaffold and `DatFile`

- **The oracle is IronPython, not CPython.** `PyStr` ports the IronPython 2.7.4 runtime code for `str(float)`, `float()`, and `int()`, read from the shipped `IronPython.dll`. It is not the CPython algorithm, because the two differ. For example, IronPython writes `str(999999999999.5)` as `1000000000000.0` where CPython writes `1e+12`, and IronPython's `int("- 5")` returns -5. `PyStr.Format` calls .NET Framework's own number formatting, as IronPython does, so it matches only on .NET Framework. The oracle test `Format_matches_str_of_float` checks about 6,000 values.
- **`DatValue.Int` holds a `long`, not an `int`.** IronPython returns a `long` above 2^31, and series ids are whole numbers. A `long` keeps such values typed. Values beyond the 64-bit range stay text, as `deviations.md` records.
- **`DatFile` gained `ToBytes` and `FromBytes`.** These pure functions own the UTF-8 BOM, so the oracle can compare the files byte for byte. `SettingsStore` writes the bytes from `ToBytes` atomically.
- **The plugin key is `comic-vine-scraper-net`, not `comic-vine-scraper`.** The phase 2 brief changed it, and the package name is `Comic Vine Scraper .NET`, so that the C# and Python plugins can be installed side by side for the end-to-end comparison. With the same key, `PluginEngine.Initialize` keeps only the first command it loads. One effect follows. ComicRack localizes a command's name and description from `<Key>.xml` in the language zips, and those zips contain only `comic-vine-scraper.xml`. So the command name is not localized until the zips gain a `comic-vine-scraper-net.xml`, or until the key changes back after the comparison.
- **Test projects are AnyCPU, not x64.** A TUnit exe on .NET Framework needs `Prefer32Bit=false` to run as a 64-bit process. With that setting, AnyCPU runs as x64 on a 64-bit machine and stays portable.
- **The oracle search path leaves out `tools/comicrack`.** That folder in `build.xml`'s `include.paths` holds an old `ComicRack.Engine.dll` and no Python source, and no oracle call needs it.
- **Phase 2 builds only the slice of each type that the prototype uses.** `BookValues` holds `Notes` only, and `ComicRackBook.Write` handles `Notes` only and throws `NotImplementedException` for any other field. `GetPage` throws too. `PluginContext` holds `Strings` and the theme, but not yet the settings store or the debug log. Phases 3c, 3d, and 4 fill these in to the sketch's shape. Until then, an exception from a command reaches ComicRack's own `ScriptUtility.ShowError`, not `ErrorDialogs`.
- **A cancelled ComicForm disables its Cancel button instead of showing "Cancelling...".** `en.zip` has no key for that text, and Core and the plugin show no text that is not in `en.zip`. Disabling the button shows that the cancel was accepted.
- **Configure shows a placeholder message in English.** Its caption is the localized `ConfigFormTitle`. Phase 4 replaces it with `ConfigDialog`.
- **The in-app proof is not done yet.** The user's ComicRack was running, and ComicRack allows one instance per machine (the `ComicRackSingleInstance` name does not depend on `-ac`). So the two open risks above are still open.
- **The recorded-response fixture store moves from phase 2 to phase 3b.** `PLAN.md` lists it under phase 2, but nothing in phase 2 reads a recorded response. Phase 3b, the Comic Vine client, is its first consumer and decides its format.
- **The oracle harness is a static `Oracle.Call`, not the sketch's typed `PythonOracle`.** `Oracle.Call(module, function, args)` marshals arguments and results generically (lists and tuples to `object[]`, dicts to `Dictionary<object, object?>`), so each new oracle test needs no wrapper type per Python module. The sketch's `PythonOracle` would need one typed method for each function called. The harness imports the modules into one in-process IronPython engine, so there is no process to start or dispose, and a lock serialises calls because the Python modules keep global state.

### Phase 3 contract: shared types

The shared types compile in `ComicVineScraper.Core` so that workstreams 3a to 3d add files and do not edit each other's. Differences from the sketch:

- **New namespaces and one file per concern.** The model lives in `ComicVineScraper.Model` (`Model/`), the client port and exception in `ComicVineScraper.ComicVine` (`ComicVine/`). The engine ports are split into `Engine/Choices.cs`, `IScrapeUi.cs`, `IClock.cs`, and `CoverPreferences.cs`, next to Phase 2's `IScrapeBook.cs`. The sketch's `EnginePorts.cs` is not one file.
- **`FieldTable` is in `ComicVineScraper.Config`, not `Books`.** It belongs to the settings, and the task assigned it to the configuration contract. `FieldTable.All` is added: the union of the 21 rows. Label keys are the `ConfigForm*CB` keys in `en.zip`. A test checks each one exists. Cover artists use `ConfigFormCoverCB` and webpage uses `ConfigFormWebCB`, as in the Python form.
- **`MergeSettings` and `SearchSettings` are in `ComicVineScraper.Config`.** The sketch put them in `Merge.cs` and `ComicVine.cs`. Both are plain data computed by `Configuration`, so Config owns them and 3b and 3d only read them.
- **`Configuration.Update` defaults to `FieldTable.All | Notes`,** the Python defaults (every `update*` key and `updateNotes` true, `updateTags` false). `ToMergeSettings` adds `Rating` when `Advanced.ScrapeRating` is on and clears it otherwise. It reports `IgnoreBlanks` false without `OverwriteExisting` and `PreserveThumbs` false without `DownloadThumbs`. `ToSearchSettings` is a field copy. The stored properties keep the raw values, so the forms can show what the user chose.
- **`AdvancedSettings` is data only.** 3c adds `AdvancedSettings.Parse`, which lowercases publishers, aliases, and imprints and applies the clamps. `Configuration.FromMaps`, `ToSettingsMap`, `SettingsStore`, `ChosenSeries`, and `GeometryMap` are also left to 3c. `DatValue` stays as Phase 2 built it, with `Int` holding a `long`.
- **`BookFacts.From` is left to 3d.** It needs `FileNameParser` (3a) and the marker search. `BookFacts`, `StoredMarker`, `BookSnapshot`, and `BookPatch` are data.
- **`IssueId.TryParseStored` accepts a leading `+` and surrounding whitespace, as Python's `int()` does, and accepts `0`.** It uses invariant culture, so `12.0` parses on a German locale. It rejects fractions, negatives, exponents, group separators, and values above `int.MaxValue`.
- **`PartialDate.Of` checks day against 1..31 only,** not against the month length, as the Python setters do. Year 0 is valid.
- **`ComicVineException` keeps `IsRecoverable`.** The property is a one-line classification of the error kind. 3b owns the URL redaction that fills `redactedUrl`.
- **`ComicRackBook` is unchanged.** It still writes `Notes` only. 3d fills in the other fields.
