# Local API service: design notes

This document explains how the Comic API Plugin runs its local API service, and why it was built that way. It covers the
process lifetime, port selection, security, the exchange of API keys and tokens, settings, packaging, logging, and the
warnings that antivirus software can give.

`docs/port/architecture.md` (section "Local API service") is the short reference. This document gives the reasons.

## Why a separate service

The plugin runs inside ComicRack, a .NET Framework 4.8 process. The service is a separate .NET 11 process. Moving the
provider code into it has these benefits:

- The Comic Vine and VerseDB calls and the OpenCV cover comparison can be tested in isolation, with `curl` or an HTTP
  test, without ComicRack.
- The plugin gets smaller. It keeps only the dialogs, the scrape flow, and the code that reads and writes ComicRack's books.
- The provider code can use a modern runtime and modern libraries.

The scrape flow and every dialog stay in the plugin, because they need ComicRack's UI thread and its `ComicBook` objects.
Only the code behind the `IComicVine`, `IVerseDb`, and `ICoverComparer` interfaces moves. Core is built for both
`net48` and `net11.0-windows`, so the plugin and the service share one copy of the Comic Vine, VerseDB, settings, and
matching code.

## Status

The move is planned in five phases:

| Phase | Content | Status |
|---|---|---|
| 1 | Service skeleton: start, health check, secure access, stop, logging | Done |
| 2 | Comic Vine calls move to the service | Done |
| 3 | VerseDB calls and the OpenCV cover comparison move to the service | Done |
| 4 | Tests that use recorded responses run through the service | Done |
| 5 | Packaging and documentation | Done |

A Comic Vine scrape and a VerseDB cover search need the service. The plugin sends each provider call to it (see
"Comic Vine endpoints" and "VerseDB and cover comparison endpoints"). If the service cannot start, the run stops at its
first provider call and the plugin shows the reason. The cover comparison also runs in the service. If the service
cannot compare two covers, the plugin opens the usual dialog and the user chooses.

## Runtime requirement

The service is **framework-dependent**: it does not contain the .NET runtime. The user must install the
**ASP.NET Core Runtime 11** from <https://dotnet.microsoft.com/download/dotnet/11.0>. The Windows Desktop runtime is not
necessary, because Core uses the `System.Drawing.Common` package on .NET 11.

This choice keeps the download small. A self-contained service would add approximately 70 MB. The cost is one install step for
the user.

When the runtime is missing, the service exits at once and writes "You must install..." to its error output. The plugin
recognizes that text and records this message:

> The local API service needs the .NET 11 runtime. Download it from https://dotnet.microsoft.com/download/dotnet/11.0 and restart ComicRack.

The message goes to the debug log when ComicRack starts. When a scrape needs the service, the plugin shows the message in
a warning dialog with the title "Local API Service", and the scrape stops. No book is changed.

## Process lifetime

### When the service starts

- **At ComicRack startup.** The plugin implements ComicRack's `IStartup` hook. The hook starts the service on a
  background thread and does not wait for it, so ComicRack does not start more slowly.
- **When a scrape needs it.** If the service is not running (for example, it failed or someone stopped it), the next
  scrape starts it.
- **After a settings change.** When the user saves the settings dialog with a different console setting, the plugin
  restarts the service with the new setting. The next call that needs the service does the restart. The old
  service stops on a background thread, so the call does not wait on the UI thread for it to exit.

All starts go through one state machine in `ServiceHost`: `NotStarted`, `Starting`, `Running`, `Failed`, or `Stopped`.
Callers that ask for the service at the same time share one start, so only one process is created.

### How the service stops

The service stops in four ways. Each one covers a case that the others do not.

| Event | Mechanism |
|---|---|
| ComicRack closes normally | The `IShutdown` hook sends `POST /shutdown`, waits up to 3 seconds for the exit, then kills the process. |
| ComicRack crashes or is ended in Task Manager | A Windows **job object** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` holds the service. Windows closes the job when ComicRack's process ends, and that kills the service. |
| The job object is not available | The service gets ComicRack's process ID (`--parent-pid`). It waits for that process to exit, then stops itself. If Windows refuses the job object (for example, when ComicRack itself runs in a job that does not allow it), the plugin writes a warning to its debug log and keeps the service. |
| A user or a test stops it | `POST /shutdown` with a valid token. |

The `IShutdown` hook alone is not sufficient, because ComicRack does not call it when it crashes. Without the job object
and the parent watch, a crash would leave the service running.

A `Stopped` service can start again. Another plugin can cancel ComicRack's close after this plugin's `IShutdown` hook has run.

## Port selection

The service does not use a fixed port, and the plugin does not search for a free one.

1. The service binds Kestrel to `http://127.0.0.1:0`. Port 0 tells Windows to choose a free port.
2. When the server accepts requests, the service writes one line to its standard output, for example:
   `COMICAPI-READY {"port":58711,"pid":56848,"version":"0.1.0"}`
3. The plugin reads standard output until it gets that line. If the line does not come in 20 seconds, the start fails
   and the plugin records the last lines of the service's error output.

There is no race condition. A "find a free port, then use it" approach can fail when another program takes the port
between the two steps. A fixed port can conflict with other software. With port 0 the service binds and reports in one
step, and two ComicRack instances get two different ports.

After the READY line, the plugin continues to read standard output and standard error, so the pipes never fill and block
the service.

## Security

The service accepts requests from the local computer only. "Localhost only" is not access control by itself, so the
design adds three more controls.

### 1. Loopback address only

Kestrel listens on `127.0.0.1`, not on `0.0.0.0` or `localhost`. Other computers on the network cannot connect. A test
confirms this: the listening socket's local address is `127.0.0.1`.

### 2. A random token for each launch

Each start of the service has a new token: 32 random bytes from `RandomNumberGenerator`, encoded as base64url.

- The plugin gives the token to the service in the environment variable `COMICAPI_SERVICE_TOKEN`. The token is never a
  command-line argument, because other processes of the same user can read command lines (for example, with Task Manager).
- Each request must have `Authorization: Bearer <token>`. The service compares tokens in constant time. A request without
  the correct token gets **401**.
- No endpoint is anonymous, the health check included.

### 3. Host header check (DNS rebinding)

A web page in the user's browser can try to send requests to `127.0.0.1` through DNS rebinding. Such a request has the
attacker's host name in its `Host` header. The service rejects a request with **400** if its `Host` header is not exactly
`127.0.0.1:<port>`. The check runs before the token check and before routing.

### What the controls do not prevent

A malicious program that runs as the same Windows user can read the memory or environment of the user's other processes,
and so it can get the token. That program could also read `settings.dat`, which holds the API keys. The service does not
make that case worse, and it does not try to defend against it. The controls stop other computers, other users' programs
that cannot open the user's processes, and web pages.

## Exchange of API keys and tokens

There are two kinds of secret:

| Secret | Who owns it | How it travels |
|---|---|---|
| The service token | The plugin creates it for each launch | Environment variable to the service, then the `Authorization` header on each request |
| The Comic Vine API key and the VerseDB token | The user, in `settings.dat` | An HTTP header on each request from the plugin to the service |

The service stores no API keys on disk. The plugin sends the key with each request:

- `X-ComicVine-Key: <Comic Vine API key>`
- `X-VerseDb-Token: <VerseDB personal access token>`

The service holds a key or a token in memory only, as the name of that key's HTTP client (and, for Comic Vine, its
throttle). A key or a token is never in a URL of the service.

Reasons for this design:

- One place stores the keys: `settings.dat`, which the Python plugin shares. The service has no second copy that can
  get out of date.
- When the user changes a key, the next request uses it. The service does not have to restart.
- Headers do not go into URLs, so the keys are not in URL logs. Query strings are not logged either.

The service never logs headers or query strings. If a later change logs a header collection, a Serilog destructuring policy
(`SecretHeaderPolicy`) writes `<redacted>` for the values of `Authorization`, `X-ComicVine-Key`, and `X-VerseDb-Token`. A test sends a known value in `X-ComicVine-Key` and confirms that the
log file does not contain it. The service sends the Comic Vine key to Comic Vine in the query string, as before, because
the Comic Vine API requires it there.

The service keeps one HTTP client and one rate-limit throttle for each API key, and caches that belong to a run ID that
the plugin sends. See "Comic Vine state and caches".

## Comic Vine endpoints

Each endpoint is a `POST` with a JSON body. Each request must have these items:

- The header `X-ComicVine-Key`: the Comic Vine API key.
- The header `X-Run-Id`: a name for the scrape run. The plugin makes a new GUID for each run.
- The body member `settings`: the advanced settings that change search results. The plugin sends them with each request,
  because they are part of the identity of a run's caches.

```json
{"ignoredSearchTerms":[],"ignoredPublishers":[],"ignoreBeforeYear":0,"ignoreAfterYear":9999,"neverIgnoreThreshold":9999,"maxSearchResults":100}
```

| Path | Other body members | Response |
|---|---|---|
| `/comicvine/search` | `terms` (text) | JSON Lines: progress lines, then `{"series":[...]}` |
| `/comicvine/issues` | `series` (id) | JSON Lines: progress lines, then `{"issues":[...]}` |
| `/comicvine/find-issue` | `series` (id), `issueNumber` (text) | `{"issue":{...}}`, or `{"issue":null}` if no single issue has that number |
| `/comicvine/issue` | `issue` (id), `includeRating` (true or false) | The issue details |
| `/comicvine/resolve-series` | `series` (id) or `issue` (id), but not both | `{"series":{...}}`, or `{"series":null}` if Comic Vine does not know the id |
| `/comicvine/image` | `url` (the image URL) | The image as PNG, or `204` if the image cannot be downloaded |

The endpoints are the six methods of `IComicVine`. `ComicVineCatalog` and `ComicVineHttp` do the work in the service, as
they did in the plugin. The image endpoint removes a back cover before it sends the image, as before. PNG keeps all
pixels of the decoded image.

Example, with the service started as in "Troubleshooting":

```powershell
$body = '{"settings":{"ignoredSearchTerms":[],"ignoredPublishers":[],"ignoreBeforeYear":0,"ignoreAfterYear":9999,"neverIgnoreThreshold":9999,"maxSearchResults":100},"series":796,"issueNumber":"497"}'
curl.exe -H "Authorization: Bearer dev-token" -H "X-ComicVine-Key: <your key>" -H "X-Run-Id: test" -H "Content-Type: application/json" -d $body http://127.0.0.1:<port>/comicvine/find-issue
```

### JSON

`ComicVineDtos` and `ComicVineWire` in Core define the JSON bodies and change them to and from the domain records. The
plugin and the service compile the same files, so the two sides cannot disagree.

The serializer is `DataContractJsonSerializer`. .NET Framework and .NET both include it, so the plugin package has no JSON
library. `System.Text.Json` was the first choice, but it is not safe in the plugin: ComicRack includes
`System.Text.Json` 10.0.0.10 and its configuration file has no binding redirect for it. A plugin that is compiled for one
version of that assembly stops loading when ComicRack includes a different version.

### Progress

A search or an issue list can need many pages from Comic Vine, and the plugin shows a progress bar. To keep the progress
bar, the search and issues endpoints answer in JSON Lines (`application/x-ndjson`): one JSON object on each line.

```json
{"progress":{"done":100,"total":22}}
{"progress":{"done":200,"total":22}}
{"series":[{"id":796,"name":"Batman","startYear":1940,"publisher":"DC Comics","issueCount":716,"thumbUrl":"https://..."}]}
```

The service writes a progress line when the catalog reports a page. The last line is the result. The plugin reports each
progress line to the progress bar when the line arrives.

The alternative was one request for each page, with the loop in the plugin. That design was rejected, because the
paging, the caches, and the search filters would then be in two processes.

### Cancellation

When the user cancels a scrape, the plugin cancels its HTTP request. The service sees the closed connection and cancels
its request to Comic Vine.

### Timeouts

The plugin waits at most 150 seconds for a sign of life from the service: first for the headers of the answer, and then,
in a JSON Lines answer, for each next line. The service waits at most 100 seconds for one answer from Comic Vine or
VerseDB, so only a service that is stuck reaches the limit.

- For Comic Vine, a stuck service is a network failure (`ComicVineException` of the kind `Network`), the same failure as
  when Comic Vine does not answer. The plugin shows "Cannot Access Online Database", and a fast rescrape skips the book.
- For VerseDB, it is a `VerseDbException` without a status. The plugin shows the VerseDB warning.
- An image request gives no image.
- A call that sends more than one request to Comic Vine, for example the details of an issue and of its series, can take
  longer than 150 seconds when Comic Vine is very slow. The plugin then also reports a network failure.
- The cover comparison keeps its own, shorter limit of 60 seconds (see "Errors" in "VerseDB and cover comparison
  endpoints").

### Errors

| Cause | Status | Body |
|---|---|---|
| Comic Vine rejects the API key | `401` | `{"comicVine":{"kind":"InvalidApiKey","url":"...","apiStatus":100,"detail":null}}` |
| Comic Vine reports its rate limit | `429` | `{"comicVine":{"kind":"RateLimited",...,"apiStatus":107}}` |
| Comic Vine cannot be reached, or gives a bad answer two times | `502` | `{"comicVine":{"kind":"Network",...}}` |
| Comic Vine gives a different status code | `502` | `{"comicVine":{"kind":"Api",...,"apiStatus":n}}` |
| A header or a body member is missing | `400` | `{"message":"..."}` |
| The service fails on an error it did not expect | `500` | `{"serviceError":"NullReferenceException: ..."}` |

- The `url` in an error never contains the API key.
- If a failure occurs after the first progress line, the status is already `200`. The error is then the last line:
  `{"error":{"kind":...}}`.
  An error that the service did not expect is then the last line `{"serviceError":"..."}`.
- A `401` from the token check has no body. A `401` with a `comicVine` body is an invalid Comic Vine key.
- The plugin changes each `comicVine` error back to the `ComicVineException` that the catalog threw. Thus the engine and
  the error dialogs do the same as before the move.
- If the plugin cannot start or reach the service, it stops the scrape and shows the reason. An image request gives
  no image instead.
- A `serviceError` is a fault in the service, not in Comic Vine and not in the connection. The plugin ends the run as
  on any unexpected error: it shows the message that offers to save the debug log.

## Comic Vine state and caches

The service keeps this state in memory. Nothing is written to disk.

| State | Lifetime | Why |
|---|---|---|
| One HTTP client and one throttle (1100 ms between requests) for each API key | Until the service stops | Comic Vine limits requests for each key. Two runs with the same key must share the throttle. |
| One catalog (the caches for searches, the last issue list, and series details) for each API key, run ID, and set of search settings | Until the run has sent no request for 30 minutes | A run keeps the caches that it had in the plugin. Two runs do not share a cache, so a new run always gets new data. |

If a run continues after 30 minutes without a request, it only loses its caches. The service then gets the data from
Comic Vine again.

## VerseDB and cover comparison endpoints

Each endpoint is a `POST`.

| Path | Request | Response |
|---|---|---|
| `/versedb/lookup-cover` | `multipart/form-data` with one part, `image`: the cover as JPEG or PNG, smaller than 5 MB | `{"clearMatch":true,"candidates":[...]}`. The best candidate is first. |
| `/versedb/issue` | JSON: `{"issue":423295}` (the VerseDB issue id) | The issue metadata that a scrape writes into a book |
| `/versedb/image` | JSON: `{"url":"https://media.versedb.com/..."}` | The image bytes as VerseDB sent them (`application/octet-stream`) |
| `/covers/compare` | `multipart/form-data` with two parts: `book` (the book's cover) and `candidate` | `{"similarity":{"correlation":0.96,"featureMatch":0.51}}`, or `{"similarity":null}` if an image cannot be decoded |

A `/versedb/` request must have these headers:

- `X-VerseDb-Token`: the VerseDB personal access token.
- `X-Run-Id`: a name for the run. The service writes it in the log line of each call to VerseDB.

`/covers/compare` needs only the bearer token of the service. It makes no call to a provider.

The endpoints are the three methods of `IVerseDb` and the one method of `ICoverComparer`. `VerseDbHttp` and
`OpenCvCoverComparer` do the work in the service, as they did in the plugin.

Example, with the service started as in "Troubleshooting":

```powershell
curl.exe -H "Authorization: Bearer dev-token" -H "X-VerseDb-Token: <your token>" -H "X-Run-Id: test" -F "image=@cover.jpg" http://127.0.0.1:<port>/versedb/lookup-cover
curl.exe -H "Authorization: Bearer dev-token" -F "book=@cover.jpg" -F "candidate=@candidate.webp" http://127.0.0.1:<port>/covers/compare
```

### Images

An image is always sent as bytes. It is never text in a JSON body.

- **In a request:** the image is a part of a `multipart/form-data` body. `curl -F` and `MultipartFormDataContent` make
  such a body. The service reads each part into memory. It does not use the ASP.NET Core form reader, because that
  reader writes a part larger than 64 KB to a temporary file.
- **In a response:** the image is the full body.
- **Why not base64 in JSON:** base64 makes an image one third larger, and the serializer then holds the image as one
  large string in each process.

VerseDB sends its covers in the WebP format. The service does not change them: `/versedb/image` returns the WebP bytes,
and the comparison decodes WebP itself (OpenCV can read it). The plugin converts a WebP cover to show it in the cover
dialog, with ComicRack's own converter, as before. This keeps one meaning for the bytes of `IVerseDb.GetImageAsync` on
the two routes, and no cover is encoded a second time.

### State

| State | Lifetime | Why |
|---|---|---|
| One HTTP client for each VerseDB token | Until the service stops | One client keeps its connections to VerseDB open between calls. |
| One cover comparer | Until the service stops | The comparer keeps no data. It loads the OpenCV library at its first comparison. |

VerseDB had no throttle and no cache in the plugin, and it has none in the service. Each comparison runs on a
thread-pool thread and uses approximately 100 ms of processor time.

### Errors

| Cause | Status | Body |
|---|---|---|
| VerseDB rejects the token | `401` | `{"verseDb":{"status":401,"message":"VerseDB refused the request (401): ...","retryAfterSeconds":null}}` |
| VerseDB reports its rate limit | `429` | `{"verseDb":{"status":429,"message":"...","retryAfterSeconds":42}}` |
| VerseDB gives a different error status, or an image download fails | `502` | `{"verseDb":{"status":422,...}}` |
| VerseDB cannot be reached | `502` | `{"verseDb":{"status":null,"message":"Could not reach VerseDB: ..."}}` |
| The service cannot load the OpenCV library | `503` | `{"openCvUnavailable":"The OpenCV library could not be used: ..."}` |
| OpenCV fails the comparison, for example for an image whose header claims too many pixels | `500` | `{"serviceError":"OpenCVException: ..."}` |
| A header, a part, or a body member is missing | `400` | `{"message":"..."}` |
| The service fails on an error it did not expect | `500` | `{"serviceError":"..."}` |

- The `message` never contains the VerseDB token.
- The plugin changes each `verseDb` error back to the `VerseDbException` that `VerseDbHttp` threw, with the same status,
  message, and wait. Thus the cover search does the same as before the move. After a `429`, it shows "VerseDB asks to
  wait", waits for the time that VerseDB gave, and tries one more time.
- A `serviceError` from a `/versedb/` endpoint ends the cover search as on any unexpected error, as for Comic Vine.
- If the plugin cannot start or reach the service, the cover search stops with the "Local API Service" warning, which
  gives the reason. The cover dialog shows the logo for a cover that it cannot get.
- If a comparison is not possible (`503`, no service, or no answer in 60 seconds), the plugin writes
  `cover auto-accept is unavailable` and the reason to its debug log. Then it opens the usual dialog. A comparison
  cannot be cancelled, so the limit of 60 seconds makes sure that a service that hangs does not block a run.
- If OpenCV fails one comparison (`500` with `serviceError`), the plugin writes `the comparison failed` and the reason to
  its debug log. Only that cover counts as no match. The Comic Vine auto-accept goes on with the next series, as it did
  before the comparison moved to the service.

## Settings

| Setting | Where | Default | Effect |
|---|---|---|---|
| Show the local API service console window (for debugging) | Checkbox on the **Providers** tab; key `showServiceConsole` in `settings.dat` | Off | The service runs with a visible console window that shows its log lines. A change takes effect when the user saves the settings. |

`settings.dat` is shared with the Python plugin. The new key is added, and no existing key changes. The Python plugin
ignores keys that it does not know. `advanced.dat` has no service settings.

The console window shows the same log lines as the log file. Standard output is a pipe to the plugin (for the READY
line), so the service writes its log to the window through the console device `CONOUT$`.

## Packaging: why `service.zip`

The `.crplugin` package contains a file `service.zip` next to the plugin's DLLs. The service is not stored unpacked in the
plugin folder, for two reasons. Both are in ComicRack CE's source code:

1. **ComicRack flattens a package when it installs it.** `PackageManager.UnzipFile` writes each entry to its file name only,
   and `CommitInstallPackage` copies only top-level files (`ComicRack.Engine/PackageManager.cs`). A `service\` subfolder
   would not survive installation.
2. **ComicRack loads every DLL under the `Scripts` folder.** `PluginEngine.Initialize` scans `Scripts` recursively, and
   `DotNetPluginInitializer` loads each DLL to look for plugins. The service has its own .NET 11 copy of
   `ComicApiPlugin.Core.dll`. If that copy were in `Scripts`, it would conflict with the plugin's .NET Framework copy of the
   same name.

So the service is a zip, and the plugin unpacks it outside `Scripts`:

- **Location:** `%LOCALAPPDATA%\ComicApiPlugin\service\<content id>\`. The content id is the first 8 bytes of the SHA-256
  of `service.zip`, so each build of the service gets its own folder.
- **Atomic:** the plugin unpacks to a temporary `.partial` folder, then renames it. A folder with the final name is always
  complete. If two starts unpack at the same time, one wins and the other deletes its copy.
- **Clean-up:** after a successful unpack, the plugin deletes the folders of earlier versions and partial folders that a
  crash left. It skips a folder that is still in use.
- **Cost:** the first start after an update unpacks a zip of 28 MB to approximately 72 MB, which takes approximately
  one second. Later starts use the existing folder.

The zip contains the framework-dependent publish output: the exe, its DLLs, `runtimeconfig.json`, and `deps.json`. It
does not contain PDB files.

### OpenCV

The OpenCV library for the cover comparison is in `service.zip` only. The plugin's own files do not include it.

- `OpenCvSharp.dll` (1.0 MB) is the .NET binding. `OpenCvSharpExtern.dll` (68.6 MB, 64-bit) is the native library. The
  two files are in the same folder as the exe, and .NET finds the native library there by its name.
- The NuGet package of the native library also contains `opencv_videoio_ffmpeg*.dll` (28 MB, for video). The service
  does not include that file, because it is not necessary to read covers.
- The plugin had these two files in its own folder before phase 3, and it loaded the native library with
  `LoadLibraryEx`. That code is deleted.

Package sizes (Debug build):

| Item | Before phase 3 | After phase 3 |
|---|---|---|
| `ComicApiPlugin.crplugin` (the download) | 28.9 MB | 28.4 MB |
| `service.zip` in the package | 1.4 MB | 28.3 MB |
| The plugin folder in `Scripts`, with PDBs | 71.9 MB | 29.1 MB |
| The unpacked service in `%LOCALAPPDATA%` | approximately 4 MB | approximately 72 MB |

The download does not become smaller, because `service.zip` is in the package: the OpenCV files moved from the
plugin's files into the zip. The plugin folder that ComicRack scans becomes 43 MB smaller, and it now contains no
native library.

Build output:

- `artifacts\<Configuration>\ComicApiPlugin.crplugin`: the package to install in ComicRack.
- `artifacts\<Configuration>\Comic API Plugin\`: the same files, unpacked, plus PDBs, to copy into `Scripts`.

## Logging

| Item | Value |
|---|---|
| Library | Serilog (`Serilog.AspNetCore`, `Serilog.Sinks.File`, `Serilog.Sinks.Console`) |
| File | `%LOCALAPPDATA%\ComicApiPlugin\logs\service-YYYYMMDD.log` |
| Rolling | One file each day, 14 files kept |
| Level | Information. ASP.NET Core's own messages: Warning only, because its request log includes query strings. |
| Content | Startup and the bound port, each request (method, path, status, time), each call to a provider, warnings, errors, parent exit, shutdown |
| Never logged | Headers, query strings, the service token, API keys |

### Calls to providers

Each HTTP client that the service uses for a provider sends its requests through one handler, `OutboundLog`. The handler
writes one line for each remote call: the provider, the method, the host and path, the status, the time, and the run ID.

```text
[INF] Comic Vine GET comicvine.gamespot.com/api/search/ responded 200 in 118 ms (run 5f0c...)
[INF] HTTP POST /comicvine/search responded 200 in 225.6070 ms
[INF] VerseDB POST versedb.com/api/v1/lookup/cover responded 200 in 2309 ms (run 81c2...)
[INF] HTTP POST /versedb/lookup-cover responded 200 in 2389.0506 ms
[INF] VerseDB GET media.versedb.com/uploads/issues/.../...-cover_md.webp responded 200 in 342 ms (run 81c2...)
[INF] HTTP POST /versedb/image responded 200 in 349.3835 ms
[INF] HTTP POST /covers/compare responded 200 in 263.2690 ms
```

- The handler never writes the query string, because Comic Vine gets the API key there. It never writes a header,
  because VerseDB gets the token there.
- The download of a VerseDB cover is also a line, with the host of the image.
- A cover comparison makes no call to a provider, so it has only the line of the request.
- A status of 400 or more is a warning (`WRN`). A request that fails without a response is a warning that gives the type
  of the failure.
- When Comic Vine fails a request and the service tries it again after 2500 ms, the service writes a warning with the
  kind of failure and the URL without the key. It writes a second warning if the request fails again.
- The wait for the throttle is not in the log, because each request waits for its turn.
- For the service's own requests, a status of 400 or more is a warning. Only a `500` or an exception is an error.

Tests send a Comic Vine key and a VerseDB token through the API. They make sure that the log has the line for each call
and does not have the key or the token.

The plugin also writes the service state to its own debug log, which the activity log window shows, for example:
`Local API service ready on 127.0.0.1:58711 (pid 56848)`, or the reason the service is not available.

To follow the log file in PowerShell:

```powershell
Get-Content "$env:LOCALAPPDATA\ComicApiPlugin\logs\service-$(Get-Date -Format yyyyMMdd).log" -Wait
```

The log is in `%LOCALAPPDATA%`, not in `%APPDATA%\Comic Vine Scraper`. That folder is shared with the Python plugin and holds
only the settings files.

## Antivirus and Windows security warnings

`ComicApiPlugin.Service.exe` is **not code-signed**. Antivirus software uses heuristics, and some of this design's
behaviors are also typical of malware. The table gives each behavior and the expected effect. These are expectations, not
test results: no antivirus product has been tested.

| Behavior | Why heuristics can flag it | Expected effect |
|---|---|---|
| An unsigned exe that is new on the internet | Reputation systems (Microsoft Defender SmartScreen, cloud reputation in other products) have no history for it | Possible "unrecognized app" or reputation warning, or a quarantine by stricter products |
| A program unpacks an exe from a zip at runtime | Droppers do this | Possible detection when the exe is written or first run |
| The exe runs from `%LOCALAPPDATA%` | Many malware families run from user-writable folders | Higher suspicion score in some products |
| Another process starts it with a hidden window | Typical of hidden background tools | Higher suspicion score |
| It opens a listening TCP socket | Typical of backdoors | Usually no effect, because it listens on `127.0.0.1` only |

Notes:

- **Windows Firewall:** the service listens on `127.0.0.1` only, so the Windows Defender Firewall "allow access" dialog is
  not expected. That dialog is for programs that listen on network interfaces.
- **SmartScreen:** SmartScreen checks files that have the "Mark of the Web" (downloaded from the internet). The plugin
  unpacks the service with .NET's `ZipFile`, which does not copy that mark to the unpacked files. So a SmartScreen prompt
  for the exe is not expected, but this has not been tested. The downloaded `.crplugin` itself can have the mark.
- **What the user sees:** if antivirus software blocks or deletes the exe, the service does not start. The debug log
  records the reason, and a Comic Vine scrape stops with the "Local API Service" warning, which gives the same reason.

Mitigations, from most to least effective:

1. **Sign the exe and the DLLs** with an Authenticode certificate. Signing gives a stable publisher identity, and reputation
   then belongs to the publisher, not to each build. An EV certificate gets SmartScreen reputation faster.
2. **Submit false positives** to the vendors, for example Microsoft's portal at
   <https://www.microsoft.com/wdsi/filesubmission>.
3. **Keep the path stable.** The content-hash folder name changes with each build, so each update looks like a new
   unknown file. A fixed folder name would make an exclusion rule possible. This is a trade-off against the atomic
   update described above.
4. **Document an exclusion** for `%LOCALAPPDATA%\ComicApiPlugin\service\` for users whose antivirus software blocks the
   service.

## Troubleshooting

| Symptom | Where to look | Likely cause |
|---|---|---|
| A scrape stops with the "Local API Service" warning | The warning text; the debug log has the same reason | The service did not start, or it did not answer |
| The debug log says "needs the .NET 11 runtime" | Debug log or activity log | ASP.NET Core Runtime 11 is not installed |
| A scrape stops with "Cannot Access Online Database" | Service log file: the `Comic Vine` lines and their status | Comic Vine did not answer, or it rejected the key or the request rate |
| A cover search stops with a VerseDB warning | Service log file: the `VerseDB` lines and their status | VerseDB did not answer, or it rejected the token, the image, or the request rate |
| Cover auto-accept is on, but the dialog always opens | Debug log: `cover check` and `cover auto-accept is unavailable` lines | The covers do not match, or the service cannot compare them. The reason is in the line. |
| "did not report that it was ready within 20 seconds" | Debug log; it includes the service's error output | Antivirus scan delay, or a startup failure |
| No `ComicApiPlugin.Service.exe` in Task Manager after ComicRack starts | Debug log; `%LOCALAPPDATA%\ComicApiPlugin\service\` | Antivirus software removed or blocked the exe |
| The service starts but requests fail | Service log file | See the logged status codes. 401 = wrong token, 400 = wrong `Host` header. |
| You want to see the service live | Providers tab checkbox | Turn on the console window |

To run the service without ComicRack (for testing):

```powershell
$env:COMICAPI_SERVICE_TOKEN = "dev-token"
& "$env:LOCALAPPDATA\ComicApiPlugin\service\<content id>\ComicApiPlugin.Service.exe"
# The first output line gives the port.
curl.exe -H "Authorization: Bearer dev-token" http://127.0.0.1:<port>/health
```

Without `--parent-pid`, the service does not watch a parent process. Without `--log-dir`, it writes its log files to
`%LOCALAPPDATA%\ComicApiPlugin\logs`.

## Endpoints

| Method and path | Response |
|---|---|
| `GET /health` | `200` with `{"version":"...","pid":n,"uptimeSeconds":n}` |
| `POST /shutdown` | `202`, then the service stops |
| `POST /comicvine/...` | See "Comic Vine endpoints" |
| `POST /versedb/...`, `POST /covers/compare` | See "VerseDB and cover comparison endpoints" |

Every request needs the bearer token and the correct `Host` header.

## Test seams

- **`COMICAPI_COMICVINE_BASE_URL`.** If this environment variable is set when the service starts, the service sends its
  Comic Vine API requests to that base URL and not to `https://comicvine.gamespot.com/api/`. The value must be an `http`
  or `https` URL that ends with a slash. Only tests and the switch build set it. A normal build of the plugin never sets
  it: `ServiceLaunch.Environment`, which carries it, is empty in the live ports.
- **`FakeComicVineServer`** (`tests/Shared`) is a small HTTP server on a loopback port. It answers Comic Vine requests
  from the recorded responses in `tests/fixtures/comicvine`. The service tests, the plugin tests, and the switch build
  point the service at it. See "Test architecture".
- **`COMICAPI_VERSEDB_BASE_URL`.** If this environment variable is set when the service starts, the service sends its
  VerseDB API requests to that base URL and not to `https://versedb.com`. The value must be an `http` or `https` URL.
  The service always gets a cover image from the URL in the lookup answer. Only tests set this variable.
- **`FakeVerseDbServer`** (`tests/Shared`) is a small HTTP server on a loopback port. It answers a cover lookup with one
  candidate, the details of that issue, and the cover of that issue. A test can give a different answer for a request.
  `FakeHttpServer` is the HTTP part that the two fake servers share.
- **Stop timeouts.** `ServiceLaunch.ShutdownRequestTimeout` (2 seconds) and `ShutdownExitTimeout` (3 seconds) are the
  times that a stop waits for the shutdown request and for the exit. The tests set 30 seconds for each, so that a stop
  on a busy computer always ends through `POST /shutdown` and the log file is complete.
- **Thread pool in the plugin tests.** Each plugin test that starts the service uses three thread-pool threads for the
  life of the service. `ThreadPoolForServices` increases the minimum size of the pool for the test run. Without it,
  approximately 50 such tests at the same time made the pool so slow that the plugin read a ready line more than
  20 seconds late, and tests failed at random.

## Test architecture

The plugin has one route to Comic Vine and VerseDB: the service. Production, the plugin tests, and the in-app tools all
use it. A test does not replace the route. It replaces the provider that the service calls.

```text
plugin (ComicApiCommands) -> ServiceHost -> ComicApiPlugin.Service.exe -> FakeComicVineServer (recorded responses)
```

### The fake provider

`FakeComicVineServer` listens on `127.0.0.1` with a port that Windows chooses. The service gets its address in
`COMICAPI_COMICVINE_BASE_URL`.

- **Recorded responses.** The server finds the answer by the request URL, with the API key removed. The files are in
  `tests/fixtures/comicvine`.
- **A request that no file names.** An API request gets an empty result, and an image gets `404`. The server keeps the
  URL of each such request in `Unrecorded`.
- **All requests.** `Requests` has each request as the server received it, with the API key. `RequestKeys` has the same
  requests as Comic Vine URLs without the key.
- **Images.** With `ownImages`, the server changes each image URL in an answer to its own address. Without this, the
  service would get the covers from the real Comic Vine.

The fake provider is test code. It is not in `ComicApiPlugin.Service.exe`, and it is not in a normal build of the plugin.

### Plugin tests

Each test in `PluginTests` makes a `PluginWorld`. A `PluginWorld` has a plugin, a fake Comic Vine, and the packaged
service from `service.zip`, which the plugin starts through `ServiceHost` as in ComicRack. `ServiceLaunch.Environment`
gives the address of the fake to the service. The test reads the requests from its own fake, and the end of the test
stops the service.

The tests do not share a service. A service of its own lets a test replace a recorded response, count the service
processes, and stop the service, with no effect on a different test. The cost is one to two seconds for each
start.

### Time

The service waits 1100 ms between two Comic Vine requests that use the same key. The tests do not change that wait. A
test that scrapes two books makes approximately 12 requests to the fake and takes approximately 17 seconds. The tests
run at the same time, and each service has its own wait, so the longest test sets the time of the suite.

| Suite | Before phase 4 | After phase 4 |
|---|---|---|
| `tests/ComicApiPlugin.Tests` (1023 tests) | 33.5 s | 46 s to 49 s |
| `tests/ComicApiPlugin.Service.Tests` (56 tests) | 7.4 s | 7.3 s |

The times are from one computer, with one run before and three runs after.

### In-app runs

The two in-app tools use the switch build of the plugin, which is built with `-p:CvsRecordedResponses=true`. Run
`Build.ps1` first. Close ComicRack, because each script stops if a ComicRack process is running.

```powershell
$env:COMICRACK_DIR = "C:\path\to\ComicRackCE\ComicRack\bin\Release\net48"
./tools/smoke/run-smoke.ps1
./tools/differential/run-differential.ps1 -PythonSource <src folder of the Python plugin> -KeyFile <file with the API key>
```

- **Smoke run** (`CVS_NET_RECORDED_RESPONSES`). The plugin inside ComicRack starts the fake Comic Vine and then the
  service. The fake answers from a copy of the fixtures in the run folder, and it is also the service's VerseDB, so no
  request leaves the computer. The plugin unpacks the service into the run folder, and the service writes its log
  there. Thus the run does not use `%LOCALAPPDATA%\ComicApiPlugin`. The fake is in the plugin, not in a separate
  process, because it then starts and stops with ComicRack and the script has no second process to control. The script
  reads the service log and fails if the service made no call to the fake, if a call went to a different host, or if a
  service process from the run folder is still running.
- **Differential run** (`CVS_NET_LIVE_FOLDER`). The service calls the real Comic Vine. The script counts the requests of
  the C# plugin from the `Comic Vine GET` lines of the service log.

## Tests

| Suite | What it proves |
|---|---|
| `tests/ComicApiPlugin.Service.Tests` (.NET 11) | Starts the real exe: 401 without or with a wrong token, 400 for a wrong `Host`, the READY line and loopback binding, `POST /shutdown`, exit when the parent process ends, no secret header value in the log file |
| `tests/ComicApiPlugin.Service.Tests` (`ComicVineEndpointTests`, `ComicVineSessionsTests`) | Starts the real exe with a fake Comic Vine: each `/comicvine/` endpoint, progress lines that arrive while the query runs, the error bodies and status codes, an error after the first progress line, one cache for each run ID, one throttle for each key, cancellation that reaches Comic Vine, the log line for each call without the key, `400` for an incomplete request, and the 30-minute idle limit |
| `tests/ComicApiPlugin.Tests` (`ServiceHostTests`) | Starts the packaged exe from `service.zip`: one process for concurrent callers, kill through the job object, graceful stop, restart after an exit, restart when the console setting changes (off the caller's thread, one new process for concurrent callers), clear failures for a missing exe, a missing runtime, and a service that never reports ready, and a service that runs on when Windows refuses the job object |
| `tests/ComicApiPlugin.Tests` (`ServiceComicVineTests`) | Points the packaged service and an in-process catalog at one fake Comic Vine: the same results, progress reports, requests, and exceptions on both routes; cancellation; a service that cannot start; a service that died and starts again; no key in the service log or the plugin log |
| `tests/ComicApiPlugin.Service.Tests` (`VerseDbEndpointTests`) | Starts the real exe with a fake VerseDB: a lookup that is clear and one that is not, the issue details, the image bytes, the multipart form that VerseDB gets (quoted `name` and `filename`, no `filename*`), the error bodies for 401, 422, 429 with `Retry-After`, and 503, a VerseDB that cannot be reached, `400` for an incomplete request, and the log line for each call without the token |
| `tests/ComicApiPlugin.Service.Tests` (`CoverEndpointTests`, `OpenCvCoverComparerTests`) | `/covers/compare` gives the scores that the comparer gives: a cropped copy matches, a different cover does not, bytes that are not an image give `null`, an image that OpenCV fails gives `500` with `serviceError`. A service without `OpenCvSharpExtern.dll` answers `503` and continues. The comparer tests moved here from `tests/ComicApiPlugin.Tests`, because OpenCV is now only in the service. |
| `tests/ComicApiPlugin.Tests` (`ServiceVerseDbTests`) | Points the packaged service and an in-process `VerseDbHttp` at one fake VerseDB: the same results, requests, and exceptions on both routes; a cover search that waits after a 429 and tries again; cover auto-accept through the service; the dialog when the service cannot start; no token in the service log or the plugin log |
| `tests/ComicApiPlugin.Tests` (`PluginTests`) | Each scrape goes through the packaged service to a fake Comic Vine: the dialogs in order, the fields written, no unrecorded request of the scrape itself, one error dialog for a rejected key, one service process for two scrapes, and a changed API key in the next requests. A full cover search through the service writes the book without a dialog when the cover matches; a scrape or a cover search without the runtime shows the warning with the download link and changes no book; a saved console setting restarts the service; a shutdown before a start starts no service |

## Known limitations and open items

- **Tested inside ComicRack by the smoke run:** the start of the service, a scrape of two books through the service, and
  the stop through `POST /shutdown` when ComicRack closes.
- **Not tested inside ComicRack yet:** the visible console window, a cover search with the cover dialog through the
  service, and the stop of the service when ComicRack crashes.
- **The differential run has not been run since phase 4.** It needs an API key and the Python plugin. Its new request
  count from the service log is not tested.
- **The download is not smaller after phase 3.** See "OpenCV". A smaller download needs a different delivery of the
  native library, for example a download on first use.
- **A cover comparison cannot be cancelled.** `ICoverComparer.Compare` has no cancellation token. A cancelled run waits
  for the comparison that is in progress, which is usually less than one second and at most 60 seconds.
- **Each comparison sends the book's cover again.** The Comic Vine auto-accept compares one book with a maximum of 20
  covers, and each request contains the book's cover (approximately 300 KB) again.
- **Each cover image is decoded in the service and sent to the plugin as PNG.** The pixels are the same, but a PNG is
  larger than the JPEG from Comic Vine, and the image is decoded two times.
- **The service keeps a client for each API key and each VerseDB token until it stops.** A user who changes a key many
  times in one ComicRack session leaves the old clients in memory.
- **Two ComicRack processes have two services.** Each service has its own throttle, so the two processes together can
  send requests for one key faster than one request each 1100 ms.
- **CI:** the GitHub Actions build installs .NET 11 and checks the runner's MSBuild version first. It must be 18 or later to build the .NET 11
  projects. The workflow has not run on this branch.
- **The service starts with every ComicRack start**, also when the user does not scrape. It uses little memory when it is
  idle, but a lazy start (only at the first scrape) is an option.
- **No PDBs in the package,** so stack traces from the service have no line numbers.
- **TUnit version:** the new test project uses the repository's existing TUnit pin. That version was 8 days old when it
  was added.
- **The executable is not signed.** See "Antivirus and Windows security warnings".
