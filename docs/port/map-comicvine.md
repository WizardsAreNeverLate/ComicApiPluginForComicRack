# Python source map: Comic Vine data layer

Reference for the C# port. Paths are relative to `comic-vine-scraper/src/py/` at commit `a4c100b` (v1.0.102). The layers are:

- `database/db.py`: the facade.
- `database/comicvine/cvdb.py`: parsing and mapping.
- `cvconnection.py`: HTTP.
- `xml2py.py` and `ipypulldom.py`: XML parsing.

## Endpoints

The base URL is `https://comicvine.gamespot.com/api/...`. Every request carries `api_key=K&client=cvscraper&format=xml`. No request sorts.

| Purpose | URL and parameters | Paging | file:line |
|---|---|---|---|
| Series search | `search/?limit=100&resources=volume&field_list=name,start_year,publisher,id,image,count_of_issues&query=` | `&page=N`, sent only after page 1 ("bprd vampire" bug) | `cvconnection.py:55-65` |
| Series detail | `volume/4050-{id}/?field_list=name,start_year,publisher,image,count_of_issues,id` | none | `:78-85` |
| Issues in a series | `issues/?field_list=name,issue_number,id,image&filter=volume:{id}` | `&page=N&offset=(N-1)*100`, both sent, only after page 1 | `:101-108` |
| Issue by number | `issues/?...same fields...&filter=volume:{id},issue_number:{n}` | none | `:121-135` |
| Issue detail | `issue/4000-{id}/`, no field_list (full payload) | none | `:149-155` |
| Images | any thumbnail or cover URL | none | `cvdb.py:432-471` |

- Search text has whitespace collapsed (`:64`) and is encoded with `HttpUtility.UrlPathEncode`. That encoder does not escape `&`, `+`, or `#`.
- Issue numbers lose leading zeros, and an all-zero number becomes `"0"` (`:126-129`).
- Paging loops: `cvdb.py:154-229` for series and `:323-377` for issues.
- Series results stop at `max_search_results`: default 100, advanced setting `MAX_SEARCH_RESULTS` clamped to 10..5000.
- Progress callbacks: the series callback gets `(iteration, total//100)`, the issue callback gets a fraction from 0 to 1. Either callback can cancel, and a cancelled query returns an empty result.

## API key, transport, and errors

- The key is supplied by the user. There is no default.
  - It is stored as `apiKey` in `%APPDATA%\Comic Vine Scraper\settings.dat`.
  - When the key is missing, `scrapeengine.py:180-187` opens `ConfigForm`.
  - `cvdb._initialize` throws on an empty key (`cvdb.py:54`).
- Transport (`utils.py:363-397`):
  - Forces TLS 1.2 and uses `WebRequest`.
  - User agent: `ComicApiPlugin/{ver} (https://github.com/cbanack/comic-vine-scraper/)`. Image requests use `[ComicApiPlugin, version {ver}]` instead (`cvdb.py:453`).
  - Any status other than 200 throws a WebException.
  - The whole response body is passed through `HtmlDecode` before the XML is parsed.
  - No timeout is set, so the default of 100 s applies.
- Throttle: at least 1100 ms between any two requests, images included (`cvconnection.py:262-275`). It has no lock.
- Retry (`__get_dom`, `:159-219`):
  - These failures all retry the same way: network error, empty body, parse error, missing `status_code`, and `status_code != 1`. The code waits 2500 ms, retries once, then throws.
  - Invalid-key and rate-limit responses are retried too.
  - Images retry once immediately, then return None.
- Errors:
  - `DatabaseConnectionError(db, url, cause, code="0")` (`dberrors.py:20-33`). It redacts `api_key=...` in the message.
  - A WebException or IOException becomes code "0" (`cvconnection.py:240`). An API failure carries the Comic Vine `status_code` (`:206`).
  - `log.py:156-185` maps codes to messages: 100 invalid key, 107 rate limit, 0 generic. Any other code shows "unexpected error, save log?".
- Measured 2026-11-09: a bad key now returns **HTTP 401** with the body `<status_code>100</status_code><error>Invalid API Key</error>`. `WebRequest` throws before the body is read, so the Python plugin shows the generic error instead of the invalid-key message.

## Data model (`dbmodels.py`)

### IssueRef (`:14-99`)

- Immutable fields: `issue_num_s`, `issue_key` (the XML id as a string), `title_s`, `thumb_url_s` (empty becomes None).
- Equality and hash use `str(key)`.

### SeriesRef (`:107-221`)

- `series_key` is an int.
- `series_name_s` is trimmed and `&amp;` becomes `&`. An empty name becomes `"Series {key}"`.
- `publisher_s` is trimmed. A publisher element with no children gives `''`.
- `volume_year_n` is `max(-1, int)`, or -1.
- `issue_count_n` is `max(0, int)`, or 0.
- In search results, `start_year.rstrip("- ")` runs first (`cvdb.py:238`).

### Issue (`:228-620`)

- Setters validate their values:
  - Day must be 1..31, month 1..12, and year ≥ 0. Anything else becomes -1.
  - Rating must be 0..5.
- List setters drop empty entries.
- `,` and `;` are removed from people, characters, teams, and locations.

### Mapping (`cvdb.py:497-710`)

- `cover_date` fills pub_y/m/d, and `store_date` fills rel_y/m/d. Both are split on `-`.
- `webpage_s` is set only when the value starts with `http`.
- Image preference order: small, medium, large, super, thumb (`:713-735`).
- Arcs, characters, teams, and locations come from `*_credits/*/name`.
- Roles come from `person.role`, split on commas, through ROLE_DICT:
  - `artist` maps to both pencillers and inkers.
  - `colorer` and `colorist` both map to colorists.
- `associated_images.original_url` is read only for slow data. It is read as a direct child, which may not match the payload Comic Vine sends.

### Summary cleanup (`:628-652`)

These steps run in order:

1. Remove `Overview`.
2. Replace `<br>` and `<p>` with `\n`.
3. Strip all tags.
4. Collapse spaces.
5. Replace `&nbsp;` with a space.
6. Decode `&amp; &quot; &lt; &gt;`.
7. Cut everything from "list of covers" (case-insensitive) to the end.
8. Trim.

### Imprints (`cvimprints.py`)

- An exact-match map from imprint name to parent publisher. A match sets the publisher to the parent and the imprint to the original name.
- The keys `"Amerotica "` and `"Eurotica "` have trailing spaces, so they can never match.

### Issue lookup and keys

- `query_issue_ref` tries alternate issue numbers up to 3 times (`:391-429`). The regex `0*.50*` has an unescaped `.` and no end anchor, so "15" becomes "0½". The later `str.replace` calls are literal strings, not regexes, so they do nothing.
- `4000-N` resolves to an issue and then its volume. `4050-N` and `49-N` resolve to a volume (`:243-286`). The same rule applies to `cvinfo.txt` and `cvinfo` (`:90-120`).
- Key tags are written as `CVDB{n}`. They are read with `CVDB(\d+)`, or the legacy `ComicVine.?\[(\d+)`.

### Search cleanup (`:290-319`)

1. Lowercase the text.
2. Replace ` & ` with ` and `.
3. Drop `c2c|ctc|noads+|tbp`.
4. Keep only `[\w':.-]` tokens.
5. If nothing is found, retry with number words converted (`utils.py:194`).

Ignored search terms are stripped before this starts (`db.py:139-143`).

### Series filter (`dbutils.py:10-37`)

- A series is dropped by publisher or by year range, unless `issue_count_n ≥ threshold`.
- A year of -1 always passes.

### Back cover strip

`strip_back_cover` (`utils.py:335`) runs in `query_image`. When the aspect ratio is between 1.2 and 1.5, it keeps the right half.

## Caching

The cache is in memory only. It lives from `db.initialize` to `shutdown`, and nothing goes to disk.

- Series search: maps search terms to refs. The whole map is cleared when it holds more than 10 entries.
- Issue refs: holds one series.
- Series details: maps a volume id to (start_year, publisher). This map has no limit.

## XML quirks

- The whole body is HTML-decoded before parsing. CDATA text keeps the decoded `<`, `&`, and NBSP (U+00A0).
- Invalid XML characters are stripped one UTF-16 unit at a time. In IronPython this also strips surrogate pairs, so emoji disappear.
- `xml2py`:
  - An element whose only child is text becomes a string.
  - An empty element becomes an object.
  - A repeated child becomes a list, and a single child becomes a scalar.
  - Attributes merge with child elements.
- Results can be empty even when the total count is greater than 0 (bug 329). A single page can also be empty (issues 33 and 396).

## Suggested .NET shape

- A typed `HttpClient` client with:
  - a `SemaphoreSlim` throttle (1100 ms)
  - a single retry after 2500 ms
  - an exception that carries the Comic Vine code and a redacted URL
  - the response body read on a 4xx status
  - an explicit timeout
- Pure functions with unit tests:
  - search cleanup and number words
  - leading-zero stripping and the alternate issue number
  - URL and cvinfo parsing, and key tags
  - summary cleanup and role mapping
  - list cleanup and date splitting
  - image preference order and imprints
  - the series filter, the back cover strip, and the invalid-character strip
- Recorded-response tests for:
  - search results: 1, many, multi-page, an empty page, and bug 329
  - a null publisher, and a start_year such as "2004-"
  - a person with several roles
  - a description with entities, "list of covers", and NBSP
  - invalid characters and emoji
  - status codes 100, 107, and 101, plus HTTP 401 and 5xx
  - an empty body
  - the "½" and "0½" issue filter
  - associated_images
