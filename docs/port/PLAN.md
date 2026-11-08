# Port plan: Comic Vine Scraper in C#

This repository ports the Comic Vine Scraper plugin for ComicRack (IronPython 2.7, about 12,300 lines, [cbanack/comic-vine-scraper](https://github.com/cbanack/comic-vine-scraper) v1.0.102) to a native C# plugin. It uses the `[PluginCommand]` support on ComicRackCE branch `feat/dotnet-plugins`.

The port keeps the plugin's behaviour and replaces Python patterns with .NET ones. It does not add features.

## Definition of done

The port is done when every item below is true:

1. The build produces `ComicVineScraper.crplugin`. The plugin installs in a ComicRackCE build of `feat/dotnet-plugins` and shows the **Comic Vine Scraper** command, including its **Configure** action.
2. The Python project's golden tests pass against the C# code:
   - the 286 filename cases in `test_fnameparser.data`
   - the `natural_compare` and `natural_key` cases
   - the BookData normalisation rules
3. The pure algorithms give the same output as the original Python run under IronPython (the "oracle") on a shared corpus of inputs. This covers filename parsing, match score, search-term cleanup, summary cleanup, key tag and note parsing and rewriting, the unique series key, natural sort, and the `.dat` file format. The only allowed differences are the ones listed in `deviations.md`.
4. The Comic Vine client passes tests against recorded responses for every endpoint and every documented edge case. One live smoke test passes with a real API key.
5. The plugin reads `settings.dat`, `advanced.dat`, `series.dat`, and `geometry.dat` as the Python version writes them. The Python version can read the files the plugin writes.
6. In ComicRack, scraping the same books with the C# plugin and with the Python plugin, in auto-choose mode, writes the same metadata. The only allowed differences are the ones listed in `deviations.md`.

## Rigor

Rigor is set high, for three reasons:

- The plugin writes metadata into users' libraries in bulk.
- It takes over existing users' settings.
- It talks to a rate-limited external API.

So every unit must pass its gates before the next unit starts. Each phase is reviewed by a model from a different family than the one that wrote it. Every decision is logged in `.audit/cvs-port.tsv`.

## Phases, in order

0. **Ground and spike.** Done.
   - The Python source is mapped in the `map-*.md` files in this folder.
   - The original Python modules run in process under ComicRack's IronPython 2.7.4, called from C#. `fnameparser.extract` returned the expected tuples, including the Python 2 `21.0` number formatting.
   - Comic Vine still serves XML. A bad key returns HTTP 401 with `status_code` 100 in the body.
1. **Architecture.** Sketch the projects, the core types, the boundary between the engine and the UI, the threading model, and the boundary with ComicRack. Compare at least two designs (`architect`).
2. **Scaffold and verification harness.**
   - The solution and projects, with a reference to ComicRack through `ComicRackDir`.
   - A TUnit test project on net48 that contains the Python oracle and the ported golden data.
   - A fixture store for recorded responses.
   - A build target for the `.crplugin`.
   - A first command that loads in ComicRack.
3. **Core, in parallel.** Each workstream has its own worktree and its own owner.
   - 3a. Text and matching algorithms: the filename parser, natural sort, Python 2 number formatting, search cleanup, number words, summary cleanup, key tags and notes, the unique series key, match score, and the image hash.
   - 3b. The Comic Vine client, models, imprints, cache, and errors.
   - 3c. Configuration and the `.dat` files, i18n, and logging.
   - 3d. Book data, the merge rules, and the mapping to ComicRack's `ComicBook`.
4. **UI forms.** The configuration, welcome, comic, search, series, issue, progress, and finish forms, with geometry persistence and theming.
5. **Engine and plugin entry points.** The scrape loop, auto-match, cancellation, and the `Books, Editor` and `ConfigScript` commands.
6. **Package and end-to-end check.** Build the `.crplugin`, install it in ComicRack, and scrape the same books with the Python and the C# plugins (item 6 of the definition of done).
7. **Review and hand back.** An independent review of the whole port, and a check of the decision trail.

## Behaviour that changes on purpose

By default the port keeps the Python behaviour exactly, quirks included. A change needs an entry in `deviations.md` and a test that pins the new behaviour. Changes are allowed only for:

- clear bugs, for example the issue-number regex that turns "15" into "0½", or the imprint keys with trailing spaces
- non-deterministic behaviour, for example ties broken by the order of a Python `set`
- behaviour that depends on the IronPython runtime, for example stripping emoji from responses
- errors that mislead the user, for example a bad API key that shows the generic error

## Open decisions for the maintainer

- **Licence.** The original is Apache 2.0, and ComicRackCE is GPL v2. A C# plugin compiles against ComicRack's assemblies, while the Python plugin only called them at run time. Until the maintainer decides, this repository keeps the Apache 2.0 licence and credits Cory Banack in `NOTICE`.
- **Settings compatibility.** The port reads and writes the Python `.dat` files in `%APPDATA%\Comic Vine Scraper\`, so a user can switch back to the Python version.

## Secrets

Live tests read the Comic Vine API key from the file path in the `CVS_API_KEY_FILE` environment variable. The key is never committed, logged, or recorded in a response fixture. A test that captures a fixture removes the key from the stored URL.
