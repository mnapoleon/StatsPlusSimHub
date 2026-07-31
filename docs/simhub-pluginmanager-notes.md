# SimHub PluginManager Notes

These notes capture what we learned while investigating how SimHub's built-in Statistics / Lap History plugin gets timing data. They are intended as a parking lot for follow-up work after the current LiteDB migration branch is committed and pushed.

## Source Of Findings

The findings below came from inspecting the installed SimHub assemblies:

- `C:\Program Files (x86)\SimHub\SimHub.Plugins.dll`
- `C:\Program Files (x86)\SimHub\GameReaderCommon.dll`

The built-in Statistics plugin code lives under this namespace:

```text
SimHub.Plugins.DataPlugins.PersistantTracker
```

Notable classes:

- `PersistantTrackerPlugin`
- `LapHitoryPlugin`
- `DBManager`
- `DataRecord`
- `HistoryModel`

## How Built-In Statistics Gets Lap Timing

The Statistics plugin does not read Assetto Corsa's own database for lap timing. It receives timing from SimHub's normalized game-reader data.

`PersistantTrackerPlugin` implements:

```text
GameReaderCommon.ITrackStatisticsProvider
```

That interface exposes:

```csharp
double? GetAllTimeBestLap();
double GetTrackLength();
void TrackNewLap(
    int completedLapNumber,
    bool testLap,
    ref GameData data,
    Dictionary<int, SectorHistoryEntry> sectorTimes);
```

During `Init`, the built-in plugin registers itself as the game manager's track statistics provider:

```csharp
pluginManager.GameManager.TrackStatisticsProvider = this;
```

SimHub's game manager owns the lap-completion decision and calls `TrackNewLap`.

Inside `TrackNewLap` / `FillRecordData`, Statistics records:

- Lap time from `data.NewData.LastLapTime`
- Sector 1 from `data.OldData.Sector1Time`
- Sector 2 from `data.OldData.Sector2Time`
- Sector 3 calculated as `LastLapTime - Sector2Time - Sector1Time`
- Session identity from `data.SessionId`, `data.LapId`, and `data.SessionStartDate`
- Car and track identity from `data.NewData.CarModel`, `CarId`, `TrackIdWithConfig`, and `TrackCode`
- Record date from `DateTime.Now`
- Speed, fuel, pit, overtake, and position stats accumulated during `DataUpdate` by `UpdateLapTelemetry`

It ignores laps where `LastLapTime.TotalSeconds <= 5`.

## Built-In Statistics Persistence

`DBManager.SaveDataRecord` persists records into SimHub's per-game LiteDB store.

Collections:

- `ptp_datarecords`
- `ptp_sessionrecords`

The database file is the per-game SimHub Statistics database:

```text
C:\Program Files (x86)\SimHub\PluginsData\<Game>\DataStore.db
```

For Assetto Corsa, we saw data in:

```text
C:\Program Files (x86)\SimHub\PluginsData\AssettoCorsa\DataStore.db
```

The persisted record types are from `GameReaderCommon`:

- `DataRecordBase`
- `SessionRecord`

Important correction: `PluginManager.WithGameDatabase(...)` exists, but it is internal / assembly-only. Built-in Statistics can call it because it lives inside `SimHub.Plugins.dll`; our external plugin should not depend on it.

## Public PluginManager Surface That Matters

`PluginManager` publicly exposes several useful events:

- `NewLap`
- `DataUpdated`
- `SDataUpdated`
- `DataSampleChanged`
- `SessionRestart`
- `CarChanged`
- `GameStateChanged`
- `GameStatusChanged`
- `GameRunningChanged`
- `GameProcessDetectedChanged`
- `LowFuelAlert`
- `InputPressed`
- `InputReleased`
- `InputTriggered`
- `InputToActionTriggered`
- `PropertyUpdated`
- `ApplicationExit`
- `PreApplicationExit`

The most interesting one for StatsPlus is `NewLap`.

Its delegate signature is:

```csharp
void NewLap(
    int completedLapNumber,
    bool testLap,
    PluginManager manager,
    ref GameData data);
```

Likely usage:

```csharp
pluginManager.NewLap += OnNewLap;

private void OnNewLap(
    int completedLapNumber,
    bool testLap,
    PluginManager manager,
    ref GameData data)
{
    var lapTime = data.NewData.LastLapTime;
    var sector1 = data.OldData?.Sector1Time;
    var sector2 = data.OldData?.Sector2Time;
}
```

This should let StatsPlus use SimHub's own lap-completed event instead of inferring lap completion from `DataUpdate`.

## Public PluginManager State

Useful public properties:

- `Status`
- `GameManager`
- `GameName`
- `GameFamily`
- `GameDescription`
- `LastCarId`
- `LastTrackIdWithConfig`
- `IsApplicationExiting`
- `IsSimHubLicenceValid`
- `ToastManager`
- `UIUpdateDispatcher`

Useful storage helpers:

- `GetCommonStoragePath(...)`
- `GetGameStoragePath(...)`

Useful property/action/event APIs:

- `AddProperty`
- `AttachProperty`
- `AttachDelegate`
- `DetachDelegate`
- `GetPropertyValue`
- `SetPropertyValue`
- `GetAllPropertiesNames`
- `AddAction`
- `TriggerAction`
- `ClearActions`
- `AddEvent`
- `AttachEvent`
- `TriggerEvent`
- `AddInput`
- `AddInputMapping`
- `TriggerInput`
- `TriggerInputPress`
- `TriggerInputRelease`

Plugin/device lookup APIs:

- `GetPlugin<T>()`
- `GetPlugins<T>()`
- `GetPluginInterface<T>()`
- `GetPluginsInterface<T>()`
- `GetDevice<T>()`
- `GetDevices<T>()`
- `GetAllDevices(...)`
- `GetConnectedGameControllers()`

## Recommended Follow-Up

For StatsPlus lap capture, prefer:

1. Subscribe to `PluginManager.NewLap`.
2. Use `DataUpdated` only for supplemental telemetry if needed.
3. Use `SessionRestart`, `CarChanged`, and `GameStateChanged` to reset transient state.
4. Continue writing StatsPlus data to our own LiteDB store.

Avoid:

- Assigning `GameManager.TrackStatisticsProvider`; it is a single provider slot and may replace/break built-in Statistics.
- Depending on `PluginManager.WithGameDatabase(...)`; it is internal and not intended for external plugin use.
- Writing to SimHub's built-in `DataStore.db`; read-only inspection is fine for diagnostics, but StatsPlus should keep its own store.

## Why This Matters

In the Assetto Corsa comparison, built-in Statistics captured the first lap of the last session while StatsPlus captured only the later laps. The likely reason is that Statistics receives SimHub's canonical lap-completed callback, while StatsPlus currently infers completion during normal data updates.

Moving StatsPlus to `PluginManager.NewLap` should make lap capture closer to SimHub's built-in Statistics behavior without touching the built-in Statistics provider slot.
