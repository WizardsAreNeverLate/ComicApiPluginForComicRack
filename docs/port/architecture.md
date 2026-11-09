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
- `SettingsStore` is the only code that touches `%APPDATA%\Comic Vine Scraper`. It writes atomically (a temp file, then `File.Replace`), uses UTF-8 with a BOM, and keeps `settings.dat` keys it does not know. A user can therefore switch back to the Python plugin.
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
| Skip forever writes the series, number, and year from the filename, and writes every other field back as read | Keep | It fills only blank fields. The ids are written back unchanged, not cleared (phase 3d oracle) |
| The inferred format is always written to `Format` | Keep | It makes a value that ComicRack inferred explicit. That is not a wrong result |
| `UrlPathEncode` does not escape `&`, `+`, or `#` | Not observable, so closed | Search cleanup removes those characters first. The port uses `Uri.EscapeDataString` |
| `SCRAPE_DELAY` defaults to 1, but parsed values are clamped to 2..3600 | Keep | Unusual but harmless |
| Book sort uses culture-sensitive compare | Keep | The order is visible to users and matches Python |
| A fast rescrape that hits an invalid key or a rate limit | End the run | Every following request would fail the same way |
| `MessageBoxManager.dll` labels buttons in ComicRack's language | Decided in phase 4: `PromptDialog`, a small form with buttons labelled from the `MessageBox*` strings | It needs no hook into a system window, and tests can drive it. See "Phase 4: the dialogs" |

## Open risks

- Does `PluginEnvironment.Localize` find the same language zips that Python's `ComicRack.Localize` found? The first command in phase 2 checks this.
- Does a minutes-long async run inside the ComicForm's `ShowDialog` keep the undo group, the book dialog's copy, and cancellation working in ComicRack? The phase 2 prototype checks this before the engine is built.
- Resolved in phase 3b: `associated_images.original_url` is a direct child, as Python reads it (`deviations.md`, "Classified as intended").

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

### Phase 3a: text and matching algorithms

The algorithms live in `ComicVineScraper.Text` (`Text/`) and `ComicVineScraper.Matching` (`Matching/`). Differences from the sketch:

- **`RankedSeries.Score` is a `double`, not an `int`.** This changes a contract type instead of adding to it. `compute_n` returns a float: its recency term, `-(now - start year) / 100`, is the tie-breaker between series with the same other scores. An `int` would lose it. Nothing used the type yet.
- **`MatchScore.Compute` and `Rank` take `BookValues`, not `BookFacts`.** The score reads only the effective values (series, number, format, and the two years). The caller passes `BookFacts.Effective`.
- **`AutoMatcher.FindAsync(book, source, ct)` has no `SearchSettings` parameter.** `IComicVine.SearchSeriesAsync` already strips the ignored terms, cleans the search, and applies the series filter, as Python's `db.query_series_refs` and `filter_series_refs` did before `__find_best_series` scored the result.
- **`FileNameParser` is a static class.** The sketch made it an instance to remember a failed ALT_SEARCH_REGEX. The regex is compiled once, with `PyRegex.TryCompile`. A match that times out is remembered per `Regex` instance (a `ConditionalWeakTable`) and logged once to the optional `DebugLog`, as Python's `__failed_regex` skipped a failed pattern. `FileNameParser.Parse(fileName, altSearchRegex)` runs the regex and then `Extract`, as `comicbook.py` does.
- **Phase 3c must build `AdvancedSettings.AltSearchRegex` with `PyRegex.TryCompile(pattern, timeout)`, not `new Regex(pattern)`.** The pattern is Python syntax. `PyRegex` ports IronPython's pre-parse, which rewrites `(?P<name>...)` and `(?P=name)` and drops `(?u)` and `(?L)`. Without it, the documented `(?P<series>...)` form does not compile. A timeout of about one second is enough for a file name.
- **`SearchTerms` mirrors the Python calls.** `StripIgnored(terms, ignored)` is the strip in `db.py`. `Clean(terms, alternate)` is `cvdb.__cleanup_search_terms`. Phase 3b runs `Clean(StripIgnored(terms, ignored), false)`, searches when the result is not blank, and when nothing is found retries once with `Clean(cleaned, true)` if that differs. The sketch's `WithNumberWords(cleaned)` would apply the number words after the punctuation strip, which is not the Python order.
- **The back cover strip is `CoverImage.StripBackCover` in `Matching`.** 3b's `GetImageAsync` and 3d's `GetPage` call it, so `AutoMatcher` does not strip again. `StripBackCover` never disposes its argument. The caller owns both the argument and a new result. Python stripped twice, but the second strip never applies to a half image.
- **`SeriesFilter.Apply(found, SearchSettings)` keeps Comic Vine's order and does not remove duplicates.** Python held search results in a `set`, which also merged equal series ids. Phase 3b should drop a repeated series id when it pages.
- **`ImageHash.Compute` takes an `Image?`.** `imagehash.hash` accepted any .NET image.
- **Golden file count.** `test_fnameparser.data` has 286 lines, of which 218 are cases. The others are comments and blank lines. `FileNameParserGoldenTests` checks the count.
- **Oracle adapters.** `tests/ComicVineScraper.Oracle.Tests/Python/oracle_text.py` builds the inputs that some Python code expects, such as a book object for `compute_n` and a DOM stub for the summary cleanup. `OracleAdapters` loads it with `imp.load_source`, so `Oracle.cs` is unchanged. The adapter calls the original functions and adds no logic.
- **Not in 3a.** The sketch's `Algorithms.cs` also lists `KeyMarkers`, `SeriesKey`, `ComicVineUrl`, and `BookOrder`. The phase 3 brief gives the first two to 3d and the URL to 3b. `BookOrder` has no owner in phase 3.

### Phase 3b: the Comic Vine client

- **Files.** `ApiRequest` builds the request URLs. `ComicVineHttp` (with `Throttle`) is the transport, and `ComicVineCatalog` implements `IComicVine`. `ComicVineXml` maps the responses, and `ComicVineUrl` holds the id patterns and the cvinfo lookup. The smaller rules live in `IssueNumbers`, `Imprints`, `ResponseText`, and `ApiKeyRedaction`. Everything is in `Core/ComicVine`, and only `ComicVineUrl`, `ComicVineHttp`, and `ComicVineCatalog` are public.
- **3a's rules.** The catalog calls `SearchTerms.StripIgnored` and `SearchTerms.Clean` (alternate retry when the first pass finds nothing and the alternate text differs, as `_query_series_refs` does), `SummaryText.Clean`, `SeriesFilter.Apply`, and `CoverImage.StripBackCover`. `GetImageAsync` disposes the downloaded bitmap when the strip returns a new one, because `StripBackCover` leaves its argument to the caller.
- **xml2py is reproduced, not ported.** `ComicVineXml.Text` applies xml2py's rule: an element whose only content is one text node, or one child element that reads as a string, is that string, and it hides its children. The oracle found this rule. Without it, a one-URL `<image>` gave a thumbnail where Python gave none.
- **Errors.** A network error, a timeout (any cancellation the caller did not request), an HTTP 5xx, an empty body, unparsable XML, or a missing `status_code` is `Network` (Python code "0"). A `status_code` of 100 is `InvalidApiKey`, 107 is `RateLimited`, and any other code is `Api` with `ApiStatus`. HTTP 401 with code 100 is therefore the invalid key. The `Url` and the message pass through the redaction, which removes `api_key=…` and also the key wherever else it appears. An inner exception keeps the transport's exception, unless its text holds the key; then a redacted copy replaces it. A missing volume in an issue response is also `Network`.
- **`ComicVineHttp`** throws `ArgumentException` for a blank key, as `cvdb._initialize` does. Its timeout is an explicit 100 s, the default that Python relied on. `HttpClientHandler` uses the operating system's TLS default instead of Python's forced TLS 1.2, so the plugin must not pin a protocol. Images use the same user agent as API requests. Python sent a second, non-standard user agent for images, which Comic Vine does not check. A downloaded image is a `Bitmap` over a `MemoryStream` that stays with the bitmap, because GDI+ reads the stream lazily.
- **Progress.** A series search reports `(results read so far, total / 100)`, as Python's callback did. An issue load reports `(issues read so far, total)`, and `Done / Total` is Python's fraction. On the last page that value can be above 1, as in Python. `IScrapeUi`'s comment "issue loads report a 0..1 fraction" means `Done / Total`.
- **`GetIssueAsync(issue, includeRating)`.** The flag is Python's `slow_data`, which reads the alternate covers. Comic Vine sends no rating, and Python's `rating_n` was always the blank value 0.0, so `Issue.Rating` is always null.
- **`ResolveSeriesAsync`** returns null when Comic Vine does not know the id, or when a recoverable error occurs. An invalid key or a rate limit is thrown (`deviations.md`).
- **`ComicVineUrl.TryParse`** returns the issue id when the text has both ids. The catalog's search keeps Python's fallback to the series id through the internal `Targets`. The cvinfo path cannot keep it, because `FindCvInfo` returns one target (`deviations.md`, "cvinfo with two ids").
- **Caches.** The search cache keys on the terms after the ignored terms are removed. It stores unfiltered results and filters on every call, so the cache holds what `db.py` held. A lock guards the caches, because the forms may load issue details for alternate covers while the engine waits on a dialog.
- **Fixture store.** Fixtures are text files in `tests/fixtures/comicvine`: `GET url`, `HTTP status`, a blank line, then the body. They are matched by URL, with the key redacted and percent escapes decoded. `.gitattributes` keeps their bytes exact. `FixtureHandler` and `ManualClock` live in `tests/Shared` and are linked into both test projects. `FixtureCapture` (explicit) records the missing fixtures, and `LiveSmokeTests` (explicit) is the one live smoke test. Fixtures whose name starts with `synthetic-` are built from the shape of real responses. They cover the edge cases that live data cannot produce on demand: the rate limit (the real HTTP status for 107 was not observed, so 420 is a guess), empty pages, fractions, an imprint, and odd field shapes.
- **Oracle harness.** `Oracle` adds the test output's `py` folder to the IronPython search path. `py/cvoracle.py` replaces only the transport of the original modules: `utils.get_html_string`, `cvconnection.wait_until_ready`, and the retry sleep. So the real `db.py`, `cvdb.py`, and `dbutils.py` replay the same fixtures as the C# catalog, and the tests compare results, progress, and the exact sequence of requests. Core gained `InternalsVisibleTo` for `ComicVineScraper.Oracle.Tests`.

### Phase 3c: configuration, i18n, logging

- **`Configuration.FromMaps` and `ToSettingsMap` live in `Config/ConfigurationMaps.cs`** (the record is now `partial`). A non-bool value in `settings.dat` counts by Python truthiness, which is what `load_defaults` stored. If both `autochooseSeries` and `confirmIssue` are true, the file loads as `ConfirmIssue`, because the Python engine auto-chooses only when `confirmIssue` is off. `apiKey` is read as text whatever its shape, where Python would store a number if the key looked like one.
- **`AdvancedSettings.Parse` is `AdvancedParser`.** Publishers, search terms, aliases, and imprints keep first-seen order, because Python's set order is arbitrary. Number lines that are not finite are ignored, and huge values saturate to `int` (see `deviations.md`).
- **`AdvancedParser` compiles `ALT_SEARCH_REGEX` with `PyRegex.TryCompile(pattern, 1 s)`** (3a's IronPython pre-parse port, with a match timeout). The user writes Python syntax, such as `(?P<series>...)`. An oracle test compares compile success and the named groups against IronPython's `re` for the whole pattern corpus. Match with `AltSearchRegex.Match(name)` and require `Index == 0`, which is Python's `re.match`.
- **`SettingsStore` takes an optional `DebugLog`** and reports read failures there, as `load_map` did. `PluginContext` carries `Settings` and `Log`.
- **`ChosenSeries` and `GeometryMap` read the store's files through `internal` members.** `series.dat` holds `<id> : <id>` lines. Geometry reads one file per call, as `PersistentForm` does.
- **`DebugLog` does not capture stdout and stderr.** Python needed that to collect `print` output. C# code calls `Debug` and `Error`. Each line also goes to `Trace`.
- **`ErrorPrompt` (Core/Diagnostics) is the pure half of `handle_error`.** It maps a `ComicVineException` to message keys. An invalid key, a rate limit, and a network failure get the OK warning. Any other error, including `Api`, offers to save the log, as Python did for a code other than 100, 107, and 0. The dialog code is phase 4's.
- **The command is localized by `comic-vine-scraper-net.xml` in every language zip.** `tools/Add-NetCommandEntry.ps1` copies each zip's `comic-vine-scraper.xml`, renames the table, and writes the name with ".NET" before the ellipsis. The script is idempotent, and a test checks all eight zips. When the key goes back to `comic-vine-scraper`, drop the entry and the script.
- **`TextKeys` has 120 constants, one per key in `en.zip`.** A test compares the set to the file. `it.zip` still carries one stale key, `ConfigFormRescrapeTab`, which ComicRack ignores.
- **There is no one-time migration of the Python settings.** An earlier draft copied the four `.dat` files from `env.CommandPath` when `settings.dat` was missing, as `Resources.__import_legacy_settings` did. That folder is the .NET plugin's own install folder, and the Python plugin never wrote its `.dat` files there, so the copy could never find anything. The code, `MigrateIfNeeded`, the `pluginFolder` constructor argument, and their tests are deleted. Both plugins read and write the same `%APPDATA%\Comic Vine Scraper` files, so a user who ran the Python plugin already has them.
- **Write failures never reach the caller as exceptions, and an unreadable file is never written.** `ChosenSeries.Record` and `GeometryMap.Set` log a failed write through `DebugLog` and carry on, as `persist_map` did. `SettingsStore.Save` returns a `SaveResult` whose `Error` is null on success, so phase 4's Configure dialog can show the failure. A file that threw an `IOException` or `UnauthorizedAccessException` on read is remembered for the session and not written, so a locked `series.dat` or `settings.dat` is not replaced by an empty map. A missing file is empty, as before. `deviations.md` records the Python bug.
- **Two commands saving at once can lose an update.** `SettingsStore` serialises its own callers with a lock, but two `SettingsStore` instances over the same folder do not see each other, and two ComicRack processes never do. The last writer wins, as in Python. Phase 5 should keep one `PluginContext`, and so one `SettingsStore`, per plugin instance, and not open one per command.
- **`Configuration.ToString` and `DebugLog` redact the API key.** `Configuration` overrides `PrintMembers` and prints `ApiKey = <redacted>` (or `<none>`). `DebugLog` replaces any run of 40 or more hex digits with `<redacted>` in every line, exception text included, as a second line of defence.

### Phase 3d: book data and merge rules

- **`BookValues.Published` is a `BookDate`, not a `PartialDate`.** ComicRack stores Year, Month, and Day separately, and Python's `BookData` keeps or drops each part on its own. So a book can hold a month without a year, and Python counts that date as not blank, which decides whether a merge without overwrite writes the date. `PartialDate` cannot hold such a date. `BookDate` has the same `Year`, `Month`, `Day`, and `IsBlank` members and converts implicitly from `PartialDate`, so code that reads the date or assigns a `PartialDate` still compiles. Comic Vine dates stay `PartialDate`.
- **`DateGate.Restrict` is the one date write rule.** `MergeRules` applies it when it plans, and `ComicRackBook.Apply` refuses a patch that breaks it before it writes anything. `Released` is written only when the date is complete, the year is 1..9998, and the day exists in that month (deviation 13).
- **`BookFacts.From` takes a parser delegate,** `Func<string, (string Series, string Number, string Year)>`, not workstream 3a's `FileNameParser`. That avoids a dependency between the workstreams. The engine passes `name => parser.Extract(name)` with the tuple fields. The oracle tests pass Python's own `fnameparser.extract`.
- **`SeriesId.TryParseStored` is added.** Python parses `comicvine_volume` with the same `int()` call as the issue key, so it shares `IssueId.TryParseStored`'s rules through an internal `StoredKey`.
- **`MergeRules.PlanSkipForever(book, settings, now)` takes the settings and the time,** because the skip note carries the scrape date when that setting is on. `MergeRules.PlanThumbnail` returns the cover `Uri` or null, not a `ThumbnailPlan` record.
- **`Configuration.ToMergeSettings` changes Python's merge behaviour, and this branch does not change it.** `ToMergeSettings` reports `IgnoreBlanks` as false when overwrite is off. Python's `ConfigForm` saves the ignore-blanks checkbox even while it is disabled, and `ComicBook.update` reads the raw value. With overwrite off and the saved checkbox on, Python keeps a field that holds only whitespace, and the port clears it to "". `MergeRules` uses `IgnoreBlanks` as it is given, and the merge oracle passes the raw value. To match Python, `ToMergeSettings` must pass the raw value. That is a change to a shared contract file, so this branch leaves it to the coordinator.
- **The merge writes no debug log lines yet.** Python logs one line per field, with `*` marking a change. `MergeRules.Plan` returns only the patch. The engine phase can add the lines when the debug log exists.
- **The oracle runs the original `ComicBook` over real ComicRack books.** A helper module, `tests/ComicVineScraper.Oracle.Tests/python/cvs_book_oracle.py`, is on the oracle's search path. It builds `ComicBook` with a stand-in scraper whose config holds the test settings. The oracle project now references `ComicRack.Engine` and the plugin project, and the plugin grants `InternalsVisibleTo` to both test projects.
- **Deviation 10 is not a bug under IronPython 2.7.4.** `%X` gives `HH:mm:ss` under every thread culture and every `LC_TIME` culture, and nothing calls `setlocale`. So Python's own regex finds every note it wrote. The port writes the same note text and keeps Python's regex.
- **IronPython's `str.lower()` uses a fixed culture,** `ToLower(new CultureInfo("en"))`. The unique series key and the publisher lookups lowercase the same way, so they do not change with the user's culture.
- **`ComicRackBook.GetPage` still throws.** It needs 3a's back-cover strip, and its caller, the cover panel, comes in phase 4.

### Phase 4: the dialogs

- **Files.** The forms live in `src/ComicVineScraper/Forms`, in the `ComicVineScraper.Plugin` namespace. Each form has one static entry point that returns a Core type: `ConfigForm.Show` returns `Configuration?`, `WelcomeForm.Show` returns whether to start, `SearchForm.Ask`, `SeriesForm.Ask`, and `IssueForm.Ask` return the choice types, and `FinishForm.Show` returns nothing. No form returns a string result.
- **`ScraperForm` is CVForm and PersistentForm in one class.** It restores the location, and the size where the form asks for it, through `GeometryMap`. It re-centres a form whose centre is off every screen. It saves the geometry on `FormClosed`, not `FormClosing`, because `ScrapeHost` refuses the ComicForm's first close while the run stops. It saves on the UI thread. Python used a throwaway thread, but the write is one small atomic file.
- **`ScraperForm.ShowModal(owner, ct)` replaces the cancel listeners.** A cancelled token closes the open dialog. The result is `Cancel` whenever the token was cancelled, even if the user also chose something.
- **Ctrl+Skip.** Every form sets `KeyPreview`. Ctrl key events relabel Skip as "- Skip -", and the Skip button's click records whether Ctrl was held. Python read `Control.ModifierKeys` after `ShowDialog` returned. Reading the state any later than the click is wrong here, because a closing dialog loses the focus before `ShowDialog` returns, and losing the focus releases Ctrl. `SearchFormTests.Ctrl_Skip_stays_skip_forever_when_the_closing_dialog_loses_focus` pins this. `deviations.md` records that the label and the result now always agree.
- **Message boxes (the open question on `MessageBoxManager.dll`).** `PromptDialog` is a small `ScraperForm` with an icon, the text, and OK or Yes and No buttons labelled from the `MessageBox*` strings. It needs no hook into a system window. Closing it any other way reads as OK, or as No for a yes or no prompt. `ErrorDialogs`, `LogFile`, and the no-issues warning use it.
- **Window titles.** The ComicForm, the welcome form, and the finish form take their title from `FinishFormTitle` ("Comic Vine Scraper - v{0}"). Python hard-coded the same text in English as `SCRIPT_FULLNAME`.
- **`FieldTable` rows are reordered (a contract change that is not additive).** The rows now follow the order in which the Python form adds the checkboxes: Series, Volume, Number, Title, Published, Released, and so on. The table's summary already claimed that order, but the rows were in Python's `__get_configuration` order. Nothing read the order: `DatFile` sorts keys, and the other readers iterate or build sets. `FieldTableTests.Lists_exactly_the_21_Python_update_keys_in_ConfigForm_order` now checks the exact sequence.
- **ConfigForm.** It reads the settings through `SettingsStore.Load` and saves them on OK. When `Save` reports an error, `ErrorDialogs` shows the unexpected-error prompt, which offers to save the debug log that holds the details, and the form still returns the user's choices. Restore Defaults also clears the advanced text, as Python's `Configuration()` did.
- **Cover loading.** `LatestLoader` replaces `Scheduler`. A new load cancels the previous one, and only the newest result is applied. `CoverPictureBox` (DBPictureBox) keeps each downloaded cover for the life of the control. `IssueCoverPanel` changes Python's behaviour in three places:
  - A series shown with the book's issue number shows that issue's cover. The lookup goes through `FindIssueAsync`, the newest lookup wins, and a result is kept per series. A failed lookup shows the series art and is not kept. Python left the previous cover on show.
  - `FORCE_SERIES_ART` passes no issue number. Python passed `-9991`, which sent a request that found nothing.
  - With `SHOW_COVERS` off, the panel looks nothing up. Python looked up the issue and never showed it.
- **Alternate covers.** The panel loads alternates with `GetIssueAsync(includeRating: true)`, which is Python's `slow_data`. Moving to another row does not cancel that load, so the result is ready when the user comes back. Disposing the panel cancels it. The series and issue forms call `CoverPreferences.Prefer` only when the user confirms with OK, as Python wrote `session_data_map` only for OK.
- **Images.** The logo and the four arrows are embedded resources of the plugin assembly, so the forms and the tests load them the same way. The `.crplugin` still ships all the images, because `Package.ini` and the command icon name them.
- **ComicForm.** It moved to `Forms/` and derives from `ScraperForm`. `ShowBook(IScrapeBook, BookFacts, remaining)` replaces the phase 2 signature. `WinFormsScrapeUi.BookStarted` calls it (phase 5b removed `PrototypeRun`). `RequestCancel` does what the user's own close does. `BookPagePanel` turns the page when the left button is released over the page. A double click therefore turns two pages, as Python's MouseClick and MouseDoubleClick handlers did. WinForms raises `MouseClick` only when the window under the pointer is the panel, which made tests depend on window stacking.
- **Progress.** `QueryProgressWindow` posts each report to the UI thread, because the catalog reports from the thread that reads the response. A series search opens the window on its first report, and an issue load opens it on its first report below 100%, as Python did. The `IScrapeUi` comment "shown on the first report below 100%" therefore applies to issue loads only, because a series search report is a count of matches and pages, not a fraction. The window's `Owner` property is set, because `Show(owner)` sets only the native owner. The user closing the window calls `ComicForm.RequestCancel`.
- **The no-issues warning and the run's token.** Phase 4 left the warning open when the run was cancelled, because `ShowNoIssues` took no token. Phase 5b added the token (see "Phase 5b: the plugin").
- **Not covered by tests.** The Ctrl+Shift+L shortcut and the save dialog behind it are not tested. A test cannot inject modifier keys into `PreProcessMessage`, and it cannot drive the common file dialog. `LogFile.Save`, which writes the file and confirms, is tested. The link to the API key page calls `Process.Start` and is not tested.
- **Test harness.** `tests/ComicVineScraper.Tests/Plugin/Forms/FormHarness.cs` runs each test on its own STA thread and turns an exception in a UI handler into a test failure. It drives forms through their controls, keys, and mouse messages. It can wrap every localized string in `⟦ ⟧`, and `Ui.UnlocalizedTexts` then lists any visible text with a letter outside those marks. Every form has such a test. `Ui.Render` draws a form with `DrawToBitmap` into the test output's `form-renders` folder.
- **Book pages are decoded on ComicRack's UI thread.** `ComicForm` and `BookPagePanel` call `IScrapeBook.GetPage`, which fills the `ImagePool`, from the UI thread. This is deliberate: `ComicBook` is never touched off the thread it belongs to. Python decoded pages on its scraper thread. The cost is that a very large archive can stall the window briefly while a page decodes.

### Phase 5a: the engine

- **Files.** `Engine/ScrapeRun.cs` holds `ScrapeRun`, `ScrapeServices`, `StepKind`, `BookOutcome`, `ResolvedSeries`, and `ScrapeFailedException`. `Engine/BookOrder.cs` ports `__sort_books`, and `Engine/IssueMatch.cs` ports steps 3 and 4 of `__choose_issue_ref`, the guess from a loaded issue list. No shared contract file changed: `IScrapeUi`, the choice types, `IScrapeBook`, `RunSummary`, `IComicVine`, and the model are as main has them.
- **`ScrapeServices` has no `Parser`.** `FileNameParser` is static (phase 3a), so the engine parses with `config.Advanced.AltSearchRegex` and the run's `DebugLog`. It has an `internal` `AutoMatch` delegate that replaces `AutoMatcher` in the engine oracle, which decides per book whether a match is found without cover images. The plugin leaves it null.
- **A run-ending error is `ScrapeFailedException`.** Its `Summary` counts the books written before the error, and `InnerException` is the cause (a `ComicVineException` for an invalid key, a rate limit, or a network failure outside a fast rescrape). Python showed the error and then, in `scrape`'s `finally`, the FinishForm with the counts. The plugin can do both from this one exception. The engine logs the cause once, so the plugin should not log it again, and should pass `InnerException` to `ErrorPrompt`. A cancel is not an error: `RunAsync` returns a summary with `Cancelled`.
- **A fast rescrape delays the book only for a recoverable `ComicVineException`.** Python's bare `except` delayed the book for any error, including an invalid key, a rate limit, and a bug in the write. The invalid key and the rate limit end the run, as decided above. Any other exception is a bug and ends the run too (`deviations.md`).
- **The issue being written when the user cancels stays unwritten.** Python finished the `query_issue` and `book.update` in progress and then stopped. Here the cancel reaches the request through the token, and the book is not touched (`deviations.md`).
- **Every write that went through a series records it in `series.dat`.** This is Python's `record_choice`, which runs after the series dialog, a cached series, a stored series key, a cvinfo file, and the auto-matcher alike. The phase 5a brief said "after a manual series choice". The engine follows Python, and `ScrapeRunTests.A_stored_series_key_finds_the_issue_without_a_search_and_records_the_series` pins it. A fast rescrape records nothing, as in Python.
- **A stored series key needs no request.** As Python's sparse `SeriesRef(series_key, None, -1, '', -1, None)`, the series is `"Series N"` with no year, no publisher, and 0 issues. The issue dialog shows that name, as Python's did.
- **The auto path does not keep the issue list it loads** for a book without a number, as Python's `__query_issue_refs` did not fill `ScrapedSeries.issue_refs`. A later interactive book of the same series therefore still asks Comic Vine for its number first.
- **`IssueQuestion.Issues` is in natural order, with ties in Comic Vine's order.** Python's IssueForm sorted its grid, which does not keep the order of ties.
- **Search terms are trimmed, and blank terms cancel the run,** as SearchForm answered CANCEL for an empty text box. A search that has not failed passes `failedTerms` as null where Python passed "".
- **The thumbnail decision is checked again after the re-read,** so a book that gained a custom thumbnail while a dialog was open keeps it when preserve-thumbs is on.
- **The scrape delay is the full delay before every book of the first pass but the first,** also after a skipped book, because `__wait_until_ready` waits from the moment it is called. The second pass has no delay, as in Python.
- **`ScrapeRun` is for one run.** Its series cache lives as long as the object.
- **The engine oracle drives the original loop.** `python/cvs_engine_oracle.py` calls `ScrapeEngine.__scrape` on a stand-in engine. It replaces only the edges: the forms answer from scripted queues, `db` and the auto-matcher answer from scenario tables, `ComicBook.update` and `skip_forever` record the write, and `__wait_until_ready` records a wait. `ScrapeRunOracleTests` runs the same 1000 random scenarios through `ScrapeRun` and compares the event traces: books started, waits, each question with its candidates and hint, the no-issues warning, each write with its issue, each skip forever, the summary, and the series recorded.

### Phase 5b: the plugin

- **`IScrapeUi.ShowNoIssues(series, ct)` takes the run's token (a shared contract change, not additive).** A cancel while the warning is open closes it, as the other dialogs close. The engine passes its own token, and `PromptDialog.Show` takes an optional token. `ScrapeRunTests.The_no_issues_warning_gets_the_runs_token_and_a_cancel_there_ends_the_run` and `WinFormsScrapeUiTests.Cancelling_the_run_closes_the_no_issues_warning` pin both sides.
- **An error is logged once, by the code that catches it.** The engine logs the cause of a `ScrapeFailedException`, and `SettingsStore` logs a failed save, before either reaches the user. `ErrorDialogs.Show` therefore only shows the prompt. It used to log as well, so each of these errors was written twice. A caller that catches an error nobody logged, such as the commands' last-resort catch, logs it before it calls `ErrorDialogs`. `ErrorDialogsTests.A_run_ending_error_shown_to_the_user_is_logged_once_and_without_the_key` pins it.
- **The commands.** `Scrape` follows `scrapeengine.py`'s `scrape`. It logs a header, loads the settings, and opens `ConfigForm` when there is no API key. If there is still no key, it stops. It shows `WelcomeForm` when `WELCOME_DIALOG` is on, then reloads the settings, because the welcome form's Settings button can change them. A cancel there stops the scrape. The run itself is `ScrapeRun` inside `ScrapeHost`, over `WinFormsScrapeUi`, `ComicRackBook` adapters, a `ComicVineCatalog` for this run, `ChosenSeries`, a new `CoverPreferences`, and the system clock. On `ScrapeFailedException`, `ErrorDialogs` shows the cause. Any other exception is logged and shown the same way. `Configure` opens `ConfigForm`. Neither command lets an exception reach ComicRack.
- **The finish form follows every scrape when `summaryDialog` is on,** as Python's `finally` did. This includes a scrape that stopped at the key prompt or the welcome form, which shows 0 scraped. `PluginTests.Cancelling_the_key_prompt_stops_before_any_request_and_still_sums_up_as_Python_did` pins it. After a run-ending error the form shows the partial summary.
- **One `PluginContext` per plugin instance,** created on first use. Every command and every form therefore shares one `SettingsStore` and one `DebugLog`, and Ctrl+Shift+L in any form saves that log. The plugin also keeps one `ComicVineHttp`, so one `HttpClient` and one throttle serve every run. It makes a new one only when the API key changes, because the key is fixed for the life of a `ComicVineHttp`. The user agent is Python's `ComicVineScraper/{version} (https://github.com/cbanack/comic-vine-scraper/)`.
- **A blank key is a whitespace-only key too.** Python tested `not api_key_s`, so a key of spaces went on to `db.initialize`, which threw. The plugin opens `ConfigForm` instead, which trims the key it saves.
- **`PluginPorts` is the seam to the outside:** the `HttpMessageHandler` factory, the settings folder, and the clock. ComicRack's constructor uses `PluginPorts.FromProcess()`, which is the live handler, `%APPDATA%\Comic Vine Scraper`, and the system clock. Tests use the internal constructor. `PluginTests` drives both commands through it on an STA thread that owns a visible main window. It answers each real dialog through its controls, serves the recorded fixtures, and checks the fields written, the dialogs in order, the re-entrancy guard, the main window disabled (Win32 `IsWindowEnabled`) during the run, and the log.
- **The recorded-response switch is for the in-app smoke test only.** Building with `-p:CvsRecordedResponses=true` defines `CVS_RECORDED_RESPONSES` and compiles `Smoke/RecordedResponses.cs` and `tests/Shared/ComicVineFixtures.cs` into the plugin. Only that build reads the environment variable `CVS_NET_RECORDED_RESPONSES`. When the variable names a folder, the plugin answers every Comic Vine request from `<folder>\fixtures\*.txt`, keeps its settings in `<folder>\settings`, and writes each request no fixture answered to `<folder>\unrecorded.txt`. An unrecorded API request finds nothing, and an unrecorded image is not found. A normal build has no code that reads the variable, so the switch cannot be turned on by accident. Build the switch into Release, never into the Debug output that `Build.ps1` tests, and never ship that build.
- **Not driven headless.** The Ctrl+Shift+L save dialog (the common file dialog), the link to the API key page, and ComicRack's own undo group and book dialog. The in-app smoke test covers ComicRack's side.
