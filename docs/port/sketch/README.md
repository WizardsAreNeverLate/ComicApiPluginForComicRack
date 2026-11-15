# Architecture sketch

These files are the design contract for the port. They are reference only and are not part of any build. The bodies throw `NotImplementedException`, and `// TODO` pseudocode marks the logic that is hard to get right. `../architecture.md` explains the design.

To trace one book from the command to the write-back, read the files in this order:

1. `Plugin/ComicApiCommands.cs`: the `DotNetPlugin` class and its two entry points, the re-entrancy guard, and error handling at the command boundary.
2. `Plugin/ScrapeHost.cs`: the modal ComicForm that hosts the async run, and cancellation.
3. `Plugin/ComicRackBook.cs`: the only code that reads or writes a ComicRack `ComicBook`.
4. `Core/ScrapeRun.cs`: the run loop and the per-book state machine.
5. `Core/EnginePorts.cs`: the dialog questions and closed choices, `IScrapeBook`, `IComicVine`, `IClock`, and the cover preferences.
6. `Core/Book.cs` and `Core/Merge.cs`: `BookFacts`, `BookFields`, `BookPatch`, and the pure `MergeRules.Plan`.
7. `Core/ComicVine.cs`: the HTTP transport (throttle, retry, errors) and the per-run catalog (caches, search cleanup, filters).
8. `Core/Matching.cs` and `Core/Algorithms.cs`: match score, auto-match, image hash, the filename parser, and text rules.
9. `Core/Config.cs` and `Core/DebugLog.cs`: settings, the `.dat` files, and the debug log.
10. `Tests/Harness.cs` and `Tests/Oracle.cs`: the scripted UI, the fake book, the recorded responses, and the IronPython oracle.

Dependencies: the `ComicApiPlugin` plugin references `ComicApiPlugin.Core`. Core references no WinForms and no ComicRack assembly.
