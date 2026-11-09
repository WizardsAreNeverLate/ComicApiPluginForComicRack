# Python source map: UI, configuration, and infrastructure

Reference for the C# port. Paths are relative to `comic-vine-scraper/` at commit `a4c100b` (v1.0.102).

## Forms

All forms derive from `CVForm`, which derives from `PersistentForm`, which derives from `Form`. The base classes do the following:

- Set the owner, turn off minimize and maximize, hide the icon and the taskbar entry, and use a FixedSingle border.
- Handle two keys in `ProcessCmdKey`: Esc closes the form, and Ctrl+Shift+L runs `log.save()`.

`PersistentForm` restores the location and size from `geometry.dat`. It re-centres a form whose centre is off-screen. On close, it saves the geometry, but only if the form moved or changed size. The save runs on a throwaway thread.

| Form | When it shows | Controls | Result |
|---|---|---|---|
| ConfigForm | The ConfigScript hook, the Welcome dialog's Settings button, or a missing API key | Five tabs: ComicVine (API key, link), Details (`CheckedListBox` of 21 fields, All and None), Behaviour, Data, and Advanced (`RichTextBox`, 64K limit). Buttons: OK, Cancel, Restore Defaults (keeps the key). | OK saves the settings and returns a `Configuration`. Cancel returns None. See the rules below. |
| WelcomeForm | Before a scrape, when `WELCOME_DIALOG` is on | "about to scrape N books"; Start, Settings, Cancel | A bool. The configuration is reloaded afterwards. |
| ComicForm | During the whole run (not modal) | Progress bar, "Scraping: name", cover panel, Cancel ("Cancel ({0} remaining)") | Closing it any way other than `close_threadsafe` calls `scraper.cancel()`. Clicking the cover pages through the book (first, previous, next, last zones, with arrow overlays). |
| SearchForm | A manual search, or after a failed search | TextBox (Enter searches, Esc cancels); Search, Skip, Cancel | SEARCH(terms), SKIP, PERMSKIP (Ctrl+Skip), or CANCEL |
| SeriesForm | Choosing a series | Grid (Series, Year, Issues, Publisher; hidden ID, Match, ModelID) sorted by MatchScore, highest first. OK, Skip, Search Again, Show Issues. IssueCoverPanel. | OK(ref), SHOW(ref), SKIP, PERMSKIP, SEARCH, or CANCEL |
| IssueForm | Choosing an issue | Grid (Issue in natural sort order, Title), with the hinted issue preselected. OK, Skip, Go Back | OK(ref), SKIP, PERMSKIP, BACK, or CANCEL |
| ProgressBarForm | Series and issue queries | ProgressBar; the title shows the count or percent | Closing it cancels the scrape |
| FinishForm | The end of the run, when `summaryDialog` is on | Counts of scraped and skipped books | none |

ConfigForm enables its controls with these rules:

- OK is disabled while the API key is empty.
- Autochoose and ConfirmIssue are mutually exclusive.
- IgnoreBlanks needs OverwriteExisting, and PreserveThumbs needs DownloadThumbs.
- The form opens on the Details tab when a key exists. Otherwise it opens on the ComicVine tab.

Other form behaviour:

- In the series and issue forms, holding Ctrl changes the Skip label to "- Skip -".
- `ButtonDataGridView` turns Enter into a click on OK.
- When the user picks a cover other than the default, it is stored as `config.session_data_map["<issue_key>-altcover"] = url`.

### Threading

- The engine runs on ComicRack's UI thread. It shows modal dialogs owned by `MainWindow` and calls `Application.DoEvents()`.
- ComicForm runs on its own thread with `Application.Run`. `show_threadsafe` blocks on Monitor wait and pulse.
- IssueCoverPanel uses two `Scheduler` threads. DBPictureBox uses one, and caches images.
- `Scheduler` keeps one running task plus at most one queued task. A newer task replaces the queued one.
- `utils.invoke` does nothing when the control has no handle or is disposed.

### Cancellation

- `engine.cancel()` sets a flag, then calls the cancel listeners through `BeginInvoke` on `MainWindow`.
- The search, series, and issue forms register `Close` as a cancel listener.
- Query progress callbacks return the cancelled flag.
- The scrape delay wait checks for cancellation every 500 ms.

## Configuration files

All files live in `%APPDATA%\Comic Vine Scraper\`. If `settings.dat` is missing there, the four `.dat` files are copied once from the plugin folder. `localCache` is only ever logged.

### settings.dat, geometry.dat, and series.dat

These three files are written by `utils.persist_map`. The encoding is .NET `Encoding.UTF8`, which writes a BOM. Example:

```
:: This file was generated on 2024.01.31 13:45:10

apiKey : abc123
updateSeries : True
```

Rules:

- Keys are sorted. Each line is written as `key + ' : ' + value`.
- Booleans are written as `True` and `False`.
- An entry is skipped on write if its key or its value contains `:`.
- When reading, a line is split on `:` and kept only if it gives exactly 2 parts.
- `true` and `false` are matched without regard to case.
- Other values become a float if they contain `.`, an int if they parse as one, and a string otherwise.

### settings.dat keys

All keys are bools that default to True unless noted.

- `apiKey` (string, default "")
- `updateSeries`, `updateNumber`, `updatePublished`, `updateReleased`, `updateTitle`, `updateCrossovers`, `updateWriter`, `updatePenciller`, `updateInker`, `updateCoverArtist`, `updateColorist`, `updateLetterer`, `updateEditor`, `updateSummary`, `updatePublisher`, `updateImprint`, `updateVolume`, `updateCharacters`, `updateTeams`, `updateLocations`, `updateWebpage`
- `overwriteExisting`, `ignoreBlanks` (False), `convertImprints`, `autochooseSeries` (False), `confirmIssue` (False), `downloadThumbs`, `preserveThumbs`, `fastRescrape`, `updateNotes`, `updateTags` (False), `summaryDialog`

`scrape_in_groups_b` (True) is never saved.

### geometry.dat

Values are `X,Y` and `W,H`. The keys are `comicformLocation`, `comicformSize`, `configformLocation`, `welcomeformLocation`, `searchformLocation`, `seriesformLocation`, `issueformLocation`, `finishformLocation`, and `pbformLocation`.

### series.dat

One line `<series_key> : <series_key>` for each series the user has chosen. MatchScore gives these series a bonus.

### advanced.dat

Raw UTF-8 text. Each line must match `^(?i)KEY\s*=\s*['"]?(.+?)['"]?$`. Keys, with defaults:

- `IGNORE_PUBLISHER` (can repeat; the value is lowercased)
- `IGNORE_SEARCHTERM` (letters and digits only)
- `IGNORE_BEFORE_YEAR` (0), `IGNORE_AFTER_YEAR` (9999999), `NEVER_IGNORE_THRESHOLD` (9999999)
- `SCRAPE_RATING` (false), `SHOW_COVERS` (true), `WELCOME_DIALOG` (true)
- `ALT_SEARCH_REGEX` ("", ignored if it does not compile)
- `IGNORE_FOLDERS`, `FORCE_SERIES_ART`, `NOTE_SCRAPE_DATE` (all false)
- `PUBLISHER_ALIAS=a->b` and `IMPRINT=imprint->publisher`. The arrow matches `[-=]+>`, and each side is at most 50 characters.
- `SCRAPE_DELAY` (default 1, but any parsed value is clamped to 2..3600)
- `MAX_SEARCH_RESULTS` (100, clamped to 10..5000)

## Logging

- `log.py` captures stdout and stderr into an in-memory list guarded by a mutex. Nothing goes to disk automatically.
- The user saves the log with a SaveFileDialog (`cvs-debug-log-yyyy-MM-dd.txt`). Ctrl+Shift+L or an error prompt opens it.
- `handle_error` maps `DatabaseConnectionError` code 100 (invalid key), 107 (rate limit), and 0 (generic) to localized warnings. Anything else shows "Unexpected error, save log?".
- `Application.ThreadException` goes to `handle_error`.

## i18n

- Strings come from `ComicRack.Localize("Script.ComicVineScraper", key, englishDefault)`.
- The English defaults are read from the bundled `en.zip`. Literal `\n` and `\t` are unescaped. A missing key throws.
- ComicRackCE's `PluginEnvironment.Localize` (`ComicRack.Plugins/PluginEnvironment.cs:82`) loads `<culture>.zip/<resourceKey>.xml` from the plugin folder.
- Each zip holds `LanguageInfo.xml`, `comic-vine-scraper.xml` (the hook name and description), and `Script.ComicVineScraper.xml` (`<TR><Texts><Text Key Text Comment/>`).
- Languages: en (120 keys), de, fr, it, nl, nl-BE, pt-BR (118), and pl (117).
- Placeholders use the `{0}` syntax.
- `MessageBoxManager.dll` relabels MessageBox buttons for the length of a run.

## Packaging

`src/resources/Package.ini`:

```
Name        = Comic Vine Scraper
Author      = Cory Banack
Version     = 1.0.102
Description = Scrapes comic book info from Comic Vine.
Image       = packagelogo.jpg
KeepFiles   = geometry.dat, settings.dat, series.dat, advanced.dat
HelpLink    = https://github.com/cbanack/comic-vine-scraper/wiki
```

- Hooks:
  - `#@Key comic-vine-scraper`, `#@Hook ConfigScript` → `cvs_config`.
  - `#@Hook Books, Editor` with `#@Image comicvinescraper.png` → `cvs_scrape`.
- The minimum ComicRack version is 0.9.165.
- Images:
  - `comicvinelogo.png`: a placeholder. A copy at alpha 0.3 is the loading image.
  - `comicvinescraper.png`: the hook icon.
  - The arrow images for ComicForm.
  - `packagelogo.jpg`.
- Build: the Ant `dist` target flattens `src/`, replaces `!DEV!` with the version, and zips everything to `ComicApiPlugin-<Version>.crplugin`.

## Launcher and tests

- `tools/launcher/Launcher.py` fakes the `ComicRack` object:
  - `App.ProductVersion='999.999.99999'`
  - `GetComicPage` returns None
  - `MainWindow` is a bare Form that is never shown, so `invoke` does nothing
  - `Localize` returns the fallback text
- It loads `sample.pickled`. That file holds real ComicBook objects serialized with BinaryFormatter against ComicRack.Engine 1.0.4803, and it cannot be used from C#.
- Tests (`ant test`):
  - `test_bookdata`
  - `test_fnameparser`, data-driven from `tests/test_fnameparser.data` (286 lines)
  - `test_utils` (`natural_compare` and `natural_key`)
  - There are no GUI tests.
