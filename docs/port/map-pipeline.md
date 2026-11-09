# Python source map: scrape pipeline and matching

Reference for the C# port. Paths are relative to `comic-vine-scraper/src/py/` at commit `a4c100b` (v1.0.102).

## Control flow of one scrape run

1. `ComicVineScraper.py:67` `cvs_scrape(books)` (hook `Books, Editor`) calls `__launch` (:82). `__launch` sets up resources, logging, the `Application.ThreadException` handler, and i18n. It runs `__validate_environment` (:131, requires ComicRack 0.9.165 or later). At the end it shuts down `db`, i18n, and logging.
2. `scrapeengine.py:116` `scrape` calls `__scrape` (:166):
   - Load the configuration. If there is no API key, show `ConfigForm`.
   - Show `WelcomeForm`, then call `db.initialize`.
   - Wrap each book in `ComicBook` and sort with `__sort_books` (:588). Skipped books and books with a known issue go first, then books sort by `unique_series_s`, then by padded issue number.
   - Show `ComicForm.show_threadsafe`.
3. The per-book loop starts at :228:
   - `__wait_until_ready` (:833) applies the scrape delay, then the start-scrape listeners run.
   - `__scrape_book` (:301) runs. If the result is UNSCRAPED, it runs again with a forced manual search.
   - A DELAYED book is added to the end of the list once.
   - `GC.Collect` runs after each book.
4. `__scrape_book` tries these in order:
   1. A `CVDBSKIP` marker skips the book.
   2. Fast rescrape (:381): if the issue is known, call `db.query_issue` and `book.update`.
   3. Known series (:405): custom value `comicvine_volume`.
   4. Magic file (:416): `db.check_magic_file` reads `cvinfo.txt` or `cvinfo` in the book folder (`cvdb.py:90`). The file holds a URL that contains `4050-N` (a series) or `4000-N` (an issue).
   5. Auto-match (:426): `automatcher.find_series_ref`.
   6. Search: `SearchForm` if needed, then `__query_series_refs` (:756), which calls `db.query_series_refs` and `dbutils.filter_series_refs`.
   7. Choose a series: `__choose_series_ref` (:640, `SeriesForm`).
   8. Choose an issue:
      - In auto mode, `db.query_issue_ref` (`cvdb.py:391`), with ½, ¼, and ¾ retries.
      - Otherwise `__choose_issue_ref` (:664). This refuses to guess when an issue number appears twice, compares numbers with `natural_key`, and takes the only issue when the book has no number and the series has exactly 1 issue. If none of these applies, it shows `IssueForm`.
   9. Write back: `db.query_issue`, then `ComicBook.update` (`comicbook.py:245`), then `PluginBookData.update` (`pluginbookdata.py:102`), then `MatchScore.record_choice`.

## ComicRack API surface used

- Read (`pluginbookdata.py:43-75`):
  - Plain fields: `Series`, `Number`, `Title`, `Summary`, `Publisher`, `Imprint`, `Notes`, `FilePath`, `Web`.
  - Fields split on commas: `AlternateSeries`, `Characters`, `Teams`, `Locations`, `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`, `Tags`.
  - Numeric fields: `Year`, `Month`, `Day`, `ReleasedTime.Year`, `ReleasedTime.Month`, `ReleasedTime.Day`, `CommunityRating`, `PageCount`.
  - Shadow fields: `ShadowVolume`, `ShadowFormat`.
  - Custom values: `GetCustomValue("comicvine_issue")` and `GetCustomValue("comicvine_volume")`.
  - `CustomThumbnailKey`.
- Written: the same fields, plus `Volume` and `Format`.
  - `ReleasedTime` is written only when the year, month, and day are all known.
  - `Month` and `Day` are written only when the fields before them are known.
  - Custom values are written with `SetCustomValue`.
  - List fields are written as `', '.join`, after commas inside each item are removed.
  - `Format` is always written as `ShadowFormat`, because `format_s` is never excluded.
- App: `App.ProductVersion`, `App.GetComicPage(book, idx)`, and `App.SetCustomBookThumbnail(book, bmp)`. The thumbnail is set only for fileless books, only when "download thumbs" is on, and only when not preserving an existing thumbnail.
- Window: `MainWindow` owns the dialogs and receives `.Activate()`, `Invoke`, and `BeginInvoke` (`utils.invoke`, :123).
- Threading:
  - The scrape runs on the hook thread and keeps the UI alive with `Application.DoEvents` inside the progress callbacks.
  - Dialogs are modal to `MainWindow`.
  - `ComicForm` runs on its own `Application.Run` thread (`comicform.py:224`). Page fetches are marshalled to `MainWindow` (`comicform.py:367`).
  - `Scheduler` (`utils/scheduler.py`) is a single background thread. A new task replaces any task already queued (Monitor Wait/Pulse).

## Algorithms that must match

### Filename parsing

`fnameparser.extract` (:51-235) works in these steps:

1. Strip the extension and apply two prefix and title rewrites.
2. Find the year: `V\d{4}` first, otherwise the last bracketed 4-digit year between 1900 and 2100.
3. Remove nested brackets, volume tags, page counts, "N of M covers", "of/de/di/von/van/z N", and "-N" ranges. Dashes become spaces only when the name has no spaces.
4. Collect number candidates with `(^|[_\s#])(-?\d*\.?\d\w*)`.
   - Years are dropped unless a `#` comes before them or the series is 2000AD or Beano.
   - A reading-list prefix is stripped.
   - The last candidate wins.
   - Leading zeros are stripped.
   - The number is normalized with `sstr(float|int)`. Python 2 `str` behavior applies, so `21.0000000` becomes `21.0`.
5. On failure or a blank result, return `(name, "", "")`.

`regex()` accepts user-supplied named groups `series`, `num`, and `year`. A pattern that fails is remembered and skipped from then on.

### Match score

`MatchScore.compute_n` (:33) adds these terms:

- Name: +5 for each matching word, −1 for each unmatched word on either side. Words are normalized for giant/king size and one-shot.
- +7 if the series was chosen before.
- −6 for mirror publishers: Panini, deagostina, Marvel Italia, Marvel UK, semic_as, abril.
- Issue count: ±100. The test is count > 100, or issue number − 1 ≤ count.
- Year: −100 or −500.
- Recency: −(now − start year)/100. This uses `DateTime.Now.Year`, so inject a clock.

### Auto-match

`automatcher.py`:

- The best series is picked with `reduce`, and ties keep the earlier item. The candidates come from a Python `set`, so their order is undefined.
- For issue 1, or a book with no number, auto-match gives up if the best series cover is more than 0.77 similar (hamming ≤ 14) to the cover of the second or third series.
- Otherwise it accepts the match when the local cover and the remote issue or series cover are more than 0.87 similar (hamming ≤ 8). It also tries the issue's alternate covers.

### Image hash

`imagehash`:

- An 8×8 average hash, computed on a `Format64bppArgb` bitmap.
- The image is converted to greyscale with a `ColorMatrix` (0.3, 0.59, 0.11) and drawn with HighQuality and HighQualityBicubic.
- Pixels are read with x as the outer loop and y as the inner loop, using the R channel. A bit is set when the pixel is strictly above the average.
- Similarity is 1 − popcount(a^b)/64. A missing hash gives 0.

### Back cover strip

`strip_back_cover` applies when 1.2 < width/height < 1.5. It keeps the right half, using integer `Width/2`.

### Existing-metadata short-circuit

`comicbook.py:139` checks, in order:

1. `CVDBSKIP` (any case) in tags, then in notes.
2. `CVDB(\d+)` or the legacy `ComicVine.?\[(\d+)`, in tags (joined with `", "`), then in notes.
3. Custom value `comicvine_issue`.

`int("12.0")` throws in Python.

### Tags and notes rewrite

- The existing key tag or note is replaced, using `(?i)Scraped.*?CVDBn(]\.|.*?[\d\.]{8,} [\d:]{6,}\.)`. If there is none, the new text is appended.
- Note text: `Scraped metadata from ComicVine [CVDBn] on %Y.%m.%d %X.`. Because `%X` is locale-dependent, the port uses invariant `HH:mm:ss`.

### Unique series key

`unique_series_s`:

- Series plus format, with `\W+` removed, lowercased.
- Then the first 5 bytes, as uppercase hex, of the MD5 of the UTF-8 bytes of folder + volume. The folder is left out when ignore-folders is set.
- Books without a name fall back to `id(self)`.

### Sorting and merge rules

- Book sort uses `String.CompareTo`, which is culture-sensitive.
- Merge rules (`__massage_*`) follow the update, overwrite, and ignore-blanks settings:
  - Rating must be between 0 and 5, and volume must be > 0.
  - Blank is −1 (0.0 for rating).
  - An unset `ReleasedTime` (`DateTime.MinValue`) reads as (1,1,1), which counts as not blank.
- Publisher mapping applies, in order: user imprints, convert-imprints, aliases. An imprint equal to the publisher is blanked.
- `skip_forever` calls `bd.update()` with every property still dirty. So PERMSKIP also writes the series, number, and year parsed from the filename, and blanks the custom values.

## Persisted state

- `%APPDATA%\Comic Vine Scraper\series.dat` holds the series ids chosen before.
- `settings.dat`, `advanced.dat`, and `geometry.dat` sit beside it, with `localCache\`.
- Format (`utils.persist_map` and `load_map`):
  - UTF-8 with a `:: generated…` header, then one `key : value` line per entry, sorted by key.
  - When reading, lines that do not split into exactly 2 parts on `:` are skipped. Values are converted to bool, int, or float (float when the value contains `.`).
- Files are migrated from the plugin's script folder (`resources.py:127`).
- In books: custom values, `CVDBn` and `CVDBSKIP` in Tags and Notes, and `cvinfo` files.

## Python behavior that needs deliberate .NET equivalents

- `BookData` property lists come from reflection, and `dont_update` strings track what changed. Use an explicit flag set instead.
- `natural_key` returns mixed lists, and Python 2 orders float before str. This needs a custom comparer.
- `is_number` is Python `float()`, which also accepts "nan", "inf", and "1e3". Parse with InvariantCulture.
- `sstr(float)` follows Python 2 `str`: `21.0`, 12 significant digits.
- The order of `set` and `reduce` decides ties.
- `BookStatus` and the form results are string enums.

## Golden tests

- `tests/test_fnameparser.data`: 286 lines of filename → (series, number, optional year).
- `test_utils.py`: `natural_compare` ordering and `natural_key` equalities.
- `test_bookdata.py`: setter and normalization rules for each property.
- There are no tests for MatchScore, automatcher, imagehash, unique_series, or the tag and note rewrite.
