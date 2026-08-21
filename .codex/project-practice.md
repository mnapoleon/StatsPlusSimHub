# StatsPlusSimHub Project Practices

Use these practices for each StatsPlusSimHub change unless the user explicitly gives different instructions for the current task.

## Standing Practices

1. Start by checking current repo state: branch, clean or dirty status, recent commits, and whether the checkout is on `main` or a feature branch.

2. For code changes, avoid implementing directly on `main` unless the user explicitly asks for a direct main commit. Prefer a feature branch or worktree for larger changes.

3. Do not start new branch names with the `codex/` prefix. Use a short descriptive branch name without that prefix unless the user explicitly requests a different naming pattern.

4. When pulling `main`, watch for local-ahead history. If local `main` is ahead but has no file diff from `origin/main`, explain the situation before any reset or cleanup.

5. Mirror proven Affinity plugin patterns when the request says "like Affinity" or when StatsPlus has a sibling feature there: settings shape, UI layout, logging model, game naming, and storage behavior.

6. Before changing behavior, identify the affected surfaces: plugin logic, settings model, WPF settings UI, storage or migration, diagnostics, tests, README, and docs.

7. Use TDD for bug fixes and feature work where practical: write or update a focused failing regression test first, confirm red, then make the smallest production change.

8. For telemetry or lap-capture bugs, use systematic debugging first. Check real SimHub logs, plugin diagnostics, stored database rows, and game-specific telemetry behavior before patching.

9. Treat each sim/game as potentially different. Do not assume `CompletedLaps`, `LastLapTime`, sectors, car, or track fields update atomically across games.

10. For `LMU` and `rFactor2`-family behavior, remember lap rollover can be delayed or incomplete. Queue and finalize lap boundaries only when lap time and sectors are stable.

11. For ACC, remember track context may come from `TrackNameWithConfig` even when `TrackName` is blank or late.

12. Keep per-game support complete across all expected places: recording toggle, settings UI checkbox, game normalization/key mapping, diagnostic logging option, runtime recognition, tab/history display, and tests.

13. Put new game-specific differences behind the StatsPlus game-profile interface and per-game implementations whenever that behavior fits the profile model. Prefer adding or extending an `IStatsPlusGameProfile` implementation over adding new direct `IsXGame` branches in plugin code; only keep direct branches when the behavior is intentionally outside the profile boundary and document why.

14. Keep diagnostic logging centralized. Route through the plugin's logging helper, use per-game logs when enabled, and verify logs under `C:\Program Files (x86)\SimHub\PluginsData\StatsPlus`.

15. Runtime storage belongs under `PluginsData\StatsPlus`. Settings remain JSON; lap history is LiteDB at `StatsPlus.laps.ldb`.

16. Preserve SimHub dependency compatibility. StatsPlus must not overwrite SimHub's shared `LiteDB.dll` or `System.Buffers.dll`; the LiteDB `4.1.4` warning is currently known and accepted.

17. When inspecting live SimHub LiteDB files, copy them to a temp or workspace snapshot first if SimHub may have them open.

18. Standard verification before claiming done:

    ```powershell
    dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
    dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
    ```

    Use the solution build with the same no-deploy override when broader coverage is needed.

19. If deploying for manual SimHub testing, run:

    ```powershell
    dotnet build StatsPlus\StatsPlus.csproj
    ```

    Verify it copied only plugin output to `C:\Program Files (x86)\SimHub`.

20. If SimHub is running and locks DLLs, report that clearly and ask the user to close SimHub before retrying the copy.

21. After manual testing, verify with evidence: check the latest per-game diagnostic log, query the LiteDB snapshot, confirm lap count, times, sectors, game, car, and track, and call out any remaining oddities.

22. Update docs when a reusable SimHub behavior, storage lesson, dependency gotcha, or game-specific telemetry rule is learned.

23. Before commits or pull requests, inspect the diff and staged files so only intended changes are included.

24. Use pull request titles with the repo convention: `major:`, `minor:`, or `patch:`. New game support and visible feature additions usually use `minor:`; fixes use `patch:`; storage or schema migrations use `major:`.

25. Pull request bodies should include summary, verification commands and results, known warnings such as LiteDB `NU1904`, and any manual SimHub smoke-test evidence.

26. After a pull request is merged, switch back to `main`, pull, confirm status, and clean up local feature branches/worktrees only when safe.

27. If scope expands mid-branch, pause and make a note in docs instead of piling unrelated follow-up work into the same change.

28. Keep final responses concise but evidence-based: files changed, tests/build run, deployment status, branch or pull request link, and known caveats.
