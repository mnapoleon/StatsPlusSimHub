# StatsPlus Per-Game Debug Logging Design

## Purpose

StatsPlus should mirror Affinity's debug logging model. Diagnostic logging will be globally disabled by default, each supported game will have its own disabled-by-default logging toggle, and enabled diagnostics will be written to separate files per game.

## Settings

`PluginSettings` will add:

- `EnableDebugLogging`, default `false`
- `GameDebugLogging`, a `Dictionary<string, bool>` defaulting to an empty dictionary

`Reset()` will restore `EnableDebugLogging` to `false` and clear `GameDebugLogging`.

On plugin initialization, StatsPlus will ensure default entries exist for supported games, all set to `false`:

- `assettocorsa` -> `Assetto Corsa`
- `assettocorsaevo` -> `Assetto Corsa EVO`
- `automobilista2` -> `Automobilista 2`
- `iracing` -> `iRacing`
- `lmu` -> `Le Mans Ultimate`
- `rfactor2` -> `rFactor 2`
- `raceroomracingexperience` -> `RaceRoom Racing Experience`

RaceRoom aliases `r3e` and `rrre` will normalize to `raceroomracingexperience`.

## Settings UI

The settings tab will add a `Debug Logging` section below `Enabled Games`.

The section will include:

- A global checkbox bound to `StatsPlusPlugin.IsDebugLoggingEnabled`
- A short note that diagnostic logs are written to separate files per game
- An `ItemsControl` bound to `GameDebugLoggingOptions`
- Per-game checkboxes bound to each option's `IsEnabled`

The per-game list will be disabled when global debug logging is disabled.

## Logging Behavior

Diagnostic logging will require both controls:

- If `EnableDebugLogging` is `false`, no diagnostic file is written.
- If `EnableDebugLogging` is `true` but the current game's entry is `false`, no diagnostic file is written for that event.
- If both are enabled, the diagnostic line is appended to that game's log file.

The base diagnostic path remains `StatsPlus.diagnostics.log`. Game-specific logs are derived by appending the normalized game key before the extension:

- `StatsPlus.diagnostics.iracing.log`
- `StatsPlus.diagnostics.lmu.log`
- `StatsPlus.diagnostics.raceroomracingexperience.log`

Diagnostic events that already know the active game will pass that game to logging. Events based on the current stored context will use `CurrentGameName`. Missing-context events will use `data.GameName` when available. If a diagnostic event has no usable game key, it will not write a per-game diagnostic line.

## Components

Add `GameDebugLoggingOption`, adapted from Affinity, with:

- `SettingsKey`
- `DisplayName`
- `IsEnabled`
- an `Action<string, bool>` callback for updating settings

Add these helpers to `StatsPlusPlugin`:

- `EnsureDefaultGameDebugLoggingSettings()`
- `EnsureGameDebugLoggingConfigured(string gameName)`
- `RefreshGameDebugLoggingOptions()`
- `UpdateGameDebugLoggingSetting(string settingsKey, bool isEnabled)`
- `ShouldWriteDiagnosticLog(string gameName)`
- `GetDiagnosticLogPath(string gameName)`
- `GetDebugLoggingSettingsKey(string gameName)`
- `GetDebugLoggingDisplayName(string settingsKey)`
- `IsSupportedDebugLoggingSettingsKey(string settingsKey)`

The existing `WriteDiagnosticLog` method will be changed to accept a game name and will use those helpers before writing.

## Tests

Focused MSTest coverage will prove:

- New settings default debug logging off with an initialized dictionary.
- Reset clears debug logging selections and disables global logging.
- Default game logging entries are added disabled.
- Per-game options render with friendly labels and update settings.
- Global disabled logging writes no file.
- Global enabled plus game disabled writes no file.
- Global enabled plus game enabled writes to the game-specific diagnostic file.
- RaceRoom aliases normalize to `raceroomracingexperience`.

## Out of Scope

This change does not alter lap recording toggles, lap storage, tab layout, or published SimHub properties except for any diagnostic path property explicitly needed by existing StatsPlus behavior.
