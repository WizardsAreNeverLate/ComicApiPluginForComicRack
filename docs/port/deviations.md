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

## Candidates still to classify

Each of these items is either a bug or intended. The port decides which, writes the reason here, and moves the item to the table above if it is a bug.

- `skip_forever` (PERMSKIP) writes the series, number, and year parsed from the filename back to the book, and blanks the custom values (`scrapeengine.py`).
- An unset `ReleasedTime` reads as (1, 1, 1), not blank. So the release date is written only when "overwrite existing" is on.
- The search query is encoded with `HttpUtility.UrlPathEncode`, which does not escape `&`, `+`, or `#` (`cvconnection.py:64`).
- `associated_images.original_url` is read as a direct child. Comic Vine may nest it differently (`cvdb.py`).
- Book sorting uses the culture-sensitive `String.CompareTo`.
