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
| 2 | Comic Vine calls move to the service | Planned |
| 3 | VerseDB calls and the OpenCV cover comparison move to the service | Planned |
| 4 | Tests that use recorded responses run through the service | Planned |
| 5 | Packaging and documentation | Planned |

In phase 1, a scrape only checks that the service answers and writes the result to the debug log. A scrape works as
before when the service cannot start. From phase 2, a scrape needs the service.

## Runtime requirement

The service is **framework-dependent**: it does not contain the .NET runtime. The user must install the
**ASP.NET Core Runtime 11** from <https://dotnet.microsoft.com/download/dotnet/11.0>. The Windows Desktop runtime is not
necessary, because Core uses the `System.Drawing.Common` package on .NET 11.

This choice keeps the download small. A self-contained service would add approximately 70 MB. The cost is one install step for
the user.

When the runtime is missing, the service exits at once and writes "You must install..." to its error output. The plugin
recognizes that text and records this message:

> The local API service needs the .NET 11 runtime. Download it from https://dotnet.microsoft.com/download/dotnet/11.0 and restart ComicRack.

In phase 1 the message goes to the debug log. From phase 2 the plugin shows it when a scrape needs the service.

## Process lifetime

### When the service starts

- **At ComicRack startup.** The plugin implements ComicRack's `IStartup` hook. The hook starts the service on a
  background thread and does not wait for it, so ComicRack does not start more slowly.
- **When a scrape needs it.** If the service is not running (for example, it failed or someone stopped it), the next
  scrape starts it.
- **After a settings change.** When the user saves the settings dialog with a different console setting, the plugin
  restarts the service with the new setting.

All starts go through one state machine in `ServiceHost`: `NotStarted`, `Starting`, `Running`, `Failed`, or `Stopped`.
Callers that ask for the service at the same time share one start, so only one process is created.

### How the service stops

The service stops in four ways. Each one covers a case that the others do not.

| Event | Mechanism |
|---|---|
| ComicRack closes normally | The `IShutdown` hook sends `POST /shutdown`, waits up to 3 seconds for the exit, then kills the process. |
| ComicRack crashes or is ended in Task Manager | A Windows **job object** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` holds the service. Windows closes the job when ComicRack's process ends, and that kills the service. |
| The job object is not available | The service gets ComicRack's process ID (`--parent-pid`). It waits for that process to exit, then stops itself. |
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
| The Comic Vine API key and the VerseDB token | The user, in `settings.dat` | An HTTP header on each request from the plugin to the service (from phase 2) |

The service keeps no API keys. The plugin sends the key with each request:

- `X-ComicVine-Key: <Comic Vine API key>`
- `X-VerseDb-Token: <VerseDB personal access token>`

Reasons for this design:

- One place stores the keys: `settings.dat`, which the Python plugin shares. The service has no second copy that can
  get out of date.
- When the user changes a key, the next request uses it. The service does not have to restart.
- Headers do not go into URLs, so the keys are not in URL logs. Query strings are not logged either.

The service never logs headers or query strings. If a later change logs a header collection, a Serilog destructuring policy
(`SecretHeaderPolicy`) writes `<redacted>` for the values of `Authorization`, `X-ComicVine-Key`, and `X-VerseDb-Token`. A test sends a known value in `X-ComicVine-Key` and confirms that the
log file does not contain it. The service sends the Comic Vine key to Comic Vine in the query string, as before, because
the Comic Vine API requires it there.

From phase 2 the service will keep one HTTP client and one rate-limit throttle for each API key, and caches that belong to a
run ID that the plugin sends. This is necessary because the throttle and caches currently live in the plugin.

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
- **Cost:** the first start after an update unpacks approximately 1.4 MB. Later starts use the existing folder.

The zip contains the framework-dependent publish output: the exe, its DLLs, `runtimeconfig.json`, and `deps.json`. It
does not contain PDB files.

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
| Content | Startup and the bound port, each request (method, path, status, time), warnings, errors, parent exit, shutdown |
| Never logged | Headers, query strings, the service token, API keys |

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
- **What the user sees:** if antivirus software blocks or deletes the exe, the service does not start. In phase 1, the
  debug log records the reason. From phase 2, scrapes fail with the "local API service unavailable" message.

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
| The debug log says "needs the .NET 11 runtime" | Debug log or activity log | ASP.NET Core Runtime 11 is not installed |
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

Without `--parent-pid`, the service does not watch a parent process. Without `--log-dir`, it does not write a log file.

## Endpoints (phase 1)

| Method and path | Response |
|---|---|
| `GET /health` | `200` with `{"version":"...","pid":n,"uptimeSeconds":n}` |
| `POST /shutdown` | `202`, then the service stops |

Every request needs the bearer token and the correct `Host` header. Phases 2 and 3 add the provider endpoints.

## Tests

| Suite | What it proves |
|---|---|
| `tests/ComicApiPlugin.Service.Tests` (.NET 11) | Starts the real exe: 401 without or with a wrong token, 400 for a wrong `Host`, the READY line and loopback binding, `POST /shutdown`, exit when the parent process ends, no secret header value in the log file |
| `tests/ComicApiPlugin.Tests` (`ServiceHostTests`) | Starts the packaged exe from `service.zip`: one process for concurrent callers, kill through the job object, graceful stop, restart after an exit, restart when the console setting changes, clear failures for a missing exe, a missing runtime, and a service that never reports ready |

## Known limitations and open items

- **Not tested inside ComicRack yet:** the `IStartup` and `IShutdown` hooks, and the visible console window.
- **CI:** the GitHub Actions build installs .NET 11, but the runner's MSBuild must be version 18 or later to build the .NET 11
  projects.
- **The service starts with every ComicRack start**, also when the user does not scrape. It uses little memory when it is
  idle, but a lazy start (only at the first scrape) is an option.
- **No PDBs in the package,** so stack traces from the service have no line numbers.
- **TUnit version:** the new test project uses the repository's existing TUnit pin. That version was 8 days old when it
  was added.
- **The executable is not signed.** See "Antivirus and Windows security warnings".
