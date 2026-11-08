# Deliberate differences from the Python plugin

The maintainer decided on 2026-11-09 that the port fixes the original's bugs instead of reproducing them. Behaviour that is unusual but intended stays as it is.

Every entry in this file gives the Python behaviour, the C# behaviour, and the test that pins the C# behaviour. The oracle tests in the test project skip exactly the inputs listed here, and they skip nothing else.

Paths are relative to `comic-vine-scraper/src/py/` at v1.0.102.

## Bugs to fix

The status of each entry is `open` until its test lands. The entry then names the test.

| # | Area | Python behaviour | C# behaviour | Source | Status |
|---|---|---|---|---|---|
| 1 | Issue number fallback | `query_issue_ref` uses the regex `0*.50*` with an unescaped `.` and no end anchor. So "15" and "25" are retried as "0½". | Only numbers equal to 0.5 are retried as "½" or "0½". | `database/comicvine/cvdb.py:391-429` | open |
| 2 | Issue number fallback | The later `str.replace` calls take regex patterns but do literal replacement, so they never change anything. | Apply the intended replacements. | `database/comicvine/cvdb.py:391-429` | open |
| 3 | Imprints | The keys `"Amerotica "` and `"Eurotica "` have a trailing space, so they never match a publisher. | Match `Amerotica` and `Eurotica`. | `database/comicvine/cvimprints.py` | open |
| 4 | Invalid API key | Comic Vine now answers a bad key with HTTP 401. `WebRequest` throws before the body (which holds `status_code` 100) is read, so the user sees the generic connection error. | Read the body on HTTP 4xx and show the invalid-key message for `status_code` 100. | `utils/utils.py:363-397`, `database/comicvine/cvconnection.py:159-219` | open |
| 5 | Invalid key and rate limit | An invalid key (100) or a rate limit (107) is retried after 2500 ms, like a network error. | Do not retry these codes. | `database/comicvine/cvconnection.py:159-219` | open |
| 6 | Request throttle | The 1100 ms gap between requests uses shared state with no lock. Requests from two threads can break the gap. | Enforce the gap across threads. | `database/comicvine/cvconnection.py:262-275` | open |
| 7 | Auto-match ties | Candidate series are held in a Python `set`, so when two scores tie, the winner depends on hash order. | When scores tie, the series that comes first in Comic Vine's result order wins. | `utils/automatcher.py` | open |
| 8 | Response text | Removing invalid XML characters one UTF-16 unit at a time also removes valid surrogate pairs, so emoji and other non-BMP characters disappear. | Remove only characters that XML does not allow, and keep valid surrogate pairs. | `database/comicvine/cvconnection.py:244-259` | open |
| 9 | Stored issue key | A key stored as `12.0` in a custom value throws in `int("12.0")`. | Parse whole-number values such as `12.0`. | `book/comicbook.py:139` | open |
| 10 | Scrape note time | `%X` formats the time in the user's locale. The regex that finds the old note then fails to match on some locales, and duplicate notes pile up. | Write the time as invariant `HH:mm:ss`. Still recognise notes written in the old locale formats. | `book/comicbook.py` | open |
| 11 | Release date | An unset `ReleasedTime` reads as (1, 1, 1), which is not blank. So a missing release date is filled in only when "overwrite existing" is on. | An unset `ReleasedTime` counts as blank. | `book/pluginbookdata.py:43-75` | open |

## Classified as intended

These items were checked and kept. `architecture.md` gives the reason for each.

- Skip forever writes the series, number, and year parsed from the filename into blank fields, and clears the Comic Vine ids.
- The inferred format is always written to `Format`.
- `SCRAPE_DELAY` defaults to 1, but parsed values are clamped to 2..3600.
- Book sorting uses culture-sensitive compare.
- `UrlPathEncode` does not escape `&`, `+`, or `#`. Search cleanup removes these characters first, so users never see the difference. The port uses `Uri.EscapeDataString`.

## Changes that are not bug fixes

| Area | Python behaviour | C# behaviour | Why |
|---|---|---|---|
| Main window during a scrape | ComicRack stays partly usable between `DoEvents` calls. The user can edit a book or start a second scrape while a merge is in progress. | ComicRack's main window is disabled until the scrape ends. The ComicForm shows progress and has a Cancel button. | Without this, an edit or a second scrape can race the merge, and the command must block until the end anyway for the undo group and the book dialog. See `architecture.md`. |
| `.dat` header time | `persist_map` writes the header time with `%X`, which follows the user's locale. | `DatFile.Format` writes the header time as invariant `HH:mm:ss`. | Python skips the header line when it reads, so no reader sees the difference. The oracle test `Files_match_byte_for_byte_after_the_header_line` compares everything after that line. |
| Very large whole numbers in `.dat` files | `load_map` reads a whole number beyond the 64-bit range as a Python `long`. | `DatFile.Parse` keeps such a value as text, so it is written back unchanged. | No key the plugin writes holds such a number. The oracle test `Whole_numbers_beyond_long_stay_text_where_Python_returns_a_long` pins the difference. |
| `str()` of a double just below 1e12 | IronPython's `str(float)` throws `ArgumentOutOfRangeException` for doubles in [1e12 - 2e-3, 1e12), because `Math.Log10` rounds up to 12.0 and the precision becomes -1. | `PyStr.Format` returns `1000000000000.0`, the same text `str()` gives for the doubles just below that band. | A crash on a setting value is not worth reproducing, and the nearest valid output is what the neighbouring values print. The oracle test `Format_matches_str_of_float_just_below_1e12_where_IronPython_does_not_throw` expects this text where the oracle throws. |
| Book label in the ComicForm | `comicform.py` sets the label text to `i18n.get("ComicFormScrapingLabel") + book_name`, with no space. | `ComicForm.ShowBook` puts a space between the label and the file name. | A small display fix. `i18n` strips the label (`Scraping:  ` in `en.zip`), so the Python text runs the colon into the file name. |
| `str()` of a double just below 1e12 | IronPython's `str(float)` throws `ArgumentOutOfRangeException` for doubles in [1e12 - 2e-3, 1e12), because `Math.Log10` rounds up to 12.0 and the precision becomes -1. | `PyStr.Format` returns `1000000000000.0`, the same text `str()` gives for the doubles just below that band. | A crash on a setting value is not worth reproducing, and the nearest valid output is what the neighbouring values print. The oracle test `Format_matches_str_of_float_just_below_1e12_where_IronPython_does_not_throw` expects this text where the oracle throws. |
| Non-finite numbers in `advanced.dat` | `int(float(text))` raises for `inf`, `-inf`, `nan`, and `1e400`. `__set_advanced_settings_s` has no handler, so `load_defaults` throws and no setting loads. Huge finite values become a Python `long`. | `AdvancedSettings.Parse` ignores a number line that is not finite, and saturates a huge finite value to the `int` range. | A typo in one advanced line must not discard the settings. The oracle test `Non_finite_numbers_make_Python_throw_and_the_port_ignores_the_line` pins it, and the corpus leaves those values out. |

## Still to check

- `associated_images.original_url` may be nested differently from what Python reads. The client phase checks it against a recorded live response.
