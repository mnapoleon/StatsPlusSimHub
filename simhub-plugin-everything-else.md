# SimHub Plugin — Everything Else You Need to Know

This document covers the remaining practical knowledge that isn't strictly part of the plugin API but matters a lot when actually building, shipping, and maintaining a real plugin.

---

## 1. Dependency Management

### The Golden Rule: Never Ship What SimHub Already Has

SimHub already bundles a large set of DLLs in its installation folder. If your plugin also ships these DLLs, version conflicts will break either your plugin or SimHub itself. Always set `<Private>False</Private>` (equivalent to "Copy Local = false") on references that SimHub provides:

```xml
<Reference Include="SimHub.Plugins">
  <HintPath>C:\Program Files (x86)\SimHub\SimHub.Plugins.dll</HintPath>
  <Private>False</Private>   <!-- Do NOT copy to output -->
</Reference>
<Reference Include="GameReaderCommon">
  <HintPath>C:\Program Files (x86)\SimHub\GameReaderCommon.dll</HintPath>
  <Private>False</Private>
</Reference>
<Reference Include="MahApps.Metro">
  <HintPath>C:\Program Files (x86)\SimHub\MahApps.Metro.dll</HintPath>
  <Private>False</Private>
</Reference>
```

### DLLs SimHub Already Provides (do NOT ship these)

| Assembly | Notes |
|---|---|
| `SimHub.Plugins.dll` | Core SDK |
| `GameReaderCommon.dll` | GameData types |
| `MahApps.Metro.dll` | UI framework |
| `Newtonsoft.Json.dll` | JSON serialization |
| `SimHub.dll` | Logging etc. |
| `log4net.dll` | Logging backend |

### The Newtonsoft.Json Trap

SimHub has explicitly fixed issues where older versions of Newtonsoft.Json shipped by third-party plugins broke dashboard functionality. This is one of the most common ways plugins break SimHub for everyone else. Never ship Newtonsoft.Json. Reference the version in the SimHub folder and mark it `Private=False`.

### DLLs You Must Ship

Any NuGet package that SimHub does NOT already include must be copied to the SimHub root alongside your plugin DLL. Set `<Private>True</Private>` (or leave it as the default — it copies by default) for these:

```xml
<!-- Example: SQLite support — SimHub doesn't bundle this -->
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
```

After building, copy these from your `bin\Release` folder into `C:\Program Files (x86)\SimHub\`:
- Your plugin DLL (e.g. `Author.MyPlugin.dll`)
- Any dependency DLLs that aren't already in SimHub (e.g. `System.Data.SQLite.dll`, `SQLite.Interop.dll`)

### Checking What's Already in SimHub

Before adding any NuGet package, check the SimHub install folder. If the DLL is already there, reference it directly and don't ship your own copy.

---

## 2. Namespace — Use Your Own, Always

Avoid collisions with other plugins that did not change their namespace from the SDK demo default. The demo project uses `User.PluginSdkDemo` — if you leave this as-is, your plugin will conflict with any other plugin that also forgot to change it.

Always use a unique namespace following the `Author.PluginName` pattern:

```csharp
namespace VendorName.MyPlugin   // good
namespace User.PluginSdkDemo    // bad — the default from the demo, guaranteed to clash
```

The namespace also becomes the prefix for all your registered properties, so `Author.MyPlugin.SomeValue` is visible in SimHub's property browser — make it readable and recognizable.

---

## 3. Exception Handling

An unhandled exception in `DataUpdate()` doesn't crash SimHub — SimHub runs each plugin in its own thread and is resilient to individual plugin failures. However, a plugin that throws on every frame will log errors rapidly and may degrade performance. Always wrap your main logic:

```csharp
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    try
    {
        if (!data.GameRunning || data.NewData == null || data.OldData == null)
            return;

        // your logic here
    }
    catch (Exception ex)
    {
        // Log once, not every frame — use a flag to avoid log spam
        if (!_errorLogged)
        {
            Logging.Current.Error($"MyPlugin - DataUpdate error: {ex.Message}\n{ex.StackTrace}");
            _errorLogged = true;
        }
    }
}

private bool _errorLogged = false;
```

For `Init()` and `End()`, exceptions are more critical — wrap file I/O and database operations in try/catch with meaningful log messages, as these are the most common failure points.

---

## 4. Reading the Logs

SimHub writes all plugin activity (including your `Logging.Current.*` calls) to:

```
C:\Program Files (x86)\SimHub\Logs\simhub.txt
```

This file is your primary debugging tool when SimHub is running. Key things to look for:

- Plugin loaded/failed messages appear at startup
- `WARN` and `ERROR` lines show exceptions with stack traces
- Your own `Logging.Current.Info(...)` messages appear here with timestamps

Either in case of errors caused by a third party plugin or performance issues, you can disable in one click any third party plugins by going in Add/Remove Features and clicking on Disable third party plugins. This is useful when diagnosing whether your plugin is causing a broader SimHub issue.

### Useful Log Patterns

```csharp
// Startup confirmation
Logging.Current.Info("MyPlugin v1.2.0 - Initialised");

// Non-fatal warning
Logging.Current.Warn("MyPlugin - Property was null for game: " + data.GameName);

// Error with context
Logging.Current.Error($"MyPlugin - Failed to save: {ex.GetType().Name}: {ex.Message}");
```

---

## 5. Performance Profiling

`DataUpdate()` runs ~60 times per second. Each call budget is roughly 16ms, shared across all active plugins. A slow plugin degrades the whole system.

### What is Expensive (avoid in DataUpdate)

- File I/O (`File.ReadAllText`, `File.WriteAllText`)
- Database queries (SQLite reads/writes)
- Network calls of any kind
- LINQ on large collections (`.Where().OrderBy()` on thousands of items)
- String formatting with complex format strings called 60x/second
- Lock contention (misused `lock` statements)
- Allocating large objects every frame (triggers frequent GC)

### What is Fine

- Reading/writing primitive fields
- Simple arithmetic and comparisons
- Calling `pluginManager.SetPropertyValue()` — this is designed for 60Hz
- Calling `pluginManager.GetPropertyValue()` — also designed for 60Hz
- Appending to a `List<T>` (once per lap, not once per frame)
- Enqueuing to a `ConcurrentQueue` (non-blocking)

### Measuring

Add stopwatch timing during development to spot regressions:

```csharp
#if DEBUG
var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

// ... your DataUpdate logic ...

#if DEBUG
sw.Stop();
if (sw.ElapsedMilliseconds > 5)
    Logging.Current.Warn($"MyPlugin - DataUpdate slow: {sw.ElapsedMilliseconds}ms");
#endif
```

---

## 6. Telemetry Data Timing Quirks

Telemetry from games is not always perfectly timed. Several known quirks to be aware of:

### Lap Completion Timing

SimHub stored lap times can be off by one lap due to telemetry timing mismatches. This is a known issue even in SimHub's own code. When `CompletedLaps` increments, `LastLapTime` may not yet be updated to the just-completed lap — it may still reflect the previous lap for a frame or two. A safe pattern is to detect the lap change, then read `LastLapTime` on the following frame:

```csharp
private bool _lapJustCompleted = false;
private int _lastCompletedLaps = 0;

public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    if (!data.GameRunning || data.NewData == null) return;

    int completedLaps = data.NewData.CompletedLaps;

    if (_lapJustCompleted)
    {
        // Read LastLapTime one frame after the lap counter incremented
        double lapTime = data.NewData.LastLapTime;
        if (lapTime > 0)
            OnLapCompleted(lapTime);
        _lapJustCompleted = false;
    }

    if (completedLaps != _lastCompletedLaps && _lastCompletedLaps >= 0)
    {
        _lapJustCompleted = true;  // will process next frame
    }

    _lastCompletedLaps = completedLaps;
}
```

### Data During Menus

Most games only send meaningful telemetry when you're in an active session on track. `data.GameRunning` should be `false` when in menus, but some games return partial or stale data even when `GameRunning` is `true` while in a menu. Always null-check nested objects and sanity-check values (e.g., speed of -1 or lap time of 0 indicates bad data).

### Sector Availability

Not all games expose sector times. `data.NewData.Sector1Time` may be 0 or null on games that don't provide splits. Always check for > 0 before treating a sector time as valid.

---

## 7. SimHub Version Compatibility

SimHub updates frequently and occasionally changes internal APIs. The official warning: SimHub is a living project, and reusing undocumented components is not blocked but can lead to broken plugins after a SimHub update.

### What is Stable

- Everything in `SimHub.Plugins` and `GameReaderCommon` — these are the public SDK
- `PluginManager.AddProperty`, `SetPropertyValue`, `GetPropertyValue`, `AddAction`
- `PluginManager.GetCommonStoragePath`
- `this.ReadCommonSettings` / `this.SaveCommonSettings`
- `Logging.Current`
- `GameData` and its `NewData`/`OldData` properties

### What Can Break

- Internal SimHub classes accessed by reflection
- UI components referenced outside the documented SDK
- `DataCorePlugin.GameRawData.*` property paths — game-specific paths change when SimHub updates its game integrations. F1 2023 changed property names versus F1 2022. LMU updated in 2025. Always test after major SimHub updates.
- The `IWPFSettingsV2` interface — known to cause loading issues in some recent versions; fall back to `IWPFSettings` if needed

### Tracking Updates

SimHub's changelog is at https://www.simhubdash.com/download-2/ — skim it when a new version drops, looking for "plugin", "GameRawData", or game-name mentions.

---

## 8. Testing Without a Running Game

You don't need a game running to develop a plugin. SimHub has a built-in "test mode" and you can:

- Run SimHub in debug mode via Visual Studio with no game active — `DataUpdate` is still called with `data.GameRunning = false`
- Use SimHub's property browser to verify your properties are being registered
- Add `Logging.Current.Info()` calls and watch `simhub.txt` live (use a tail utility like `tail -f` via WSL, or just keep refreshing it in Notepad++)

For testing specific values, you can hardcode test data behind a debug flag:

```csharp
#if DEBUG
// Simulate a lap completion for UI testing
if (data.NewData.CompletedLaps == 0 && DateTime.Now.Second % 10 == 0)
{
    OnLapCompleted(new LapRecord { LapTime = 95.234, IsValid = true });
}
#endif
```

---

## 9. Shipping and Distribution

### What to Include in a Release

```
YourPlugin_v1.0.0.zip
├── Author.MyPlugin.dll          # Your plugin
├── SomeDependency.dll           # Only if not already in SimHub
├── README.md                    # Installation instructions
└── CHANGELOG.md                 # Optional but appreciated
```

### Installation Instructions (for users)

Standard installation that works for all plugins:

1. Close SimHub
2. Copy the DLL(s) into `C:\Program Files (x86)\SimHub\`
3. Start SimHub — it will prompt to enable the new plugin
4. Confirm and optionally enable it in the left menu

### Versioning Your Plugin

Expose your plugin version as a property — this makes it easy for users and you to confirm which version is loaded:

```csharp
public void Init(PluginManager pluginManager)
{
    pluginManager.AddProperty("MyPlugin.Version", this.GetType(), "1.0.0");
}
```

Users can then verify the version in SimHub's property browser or reference it in dashboards.

Use semantic versioning (`MAJOR.MINOR.PATCH`). Increment MAJOR for breaking changes to published properties (renaming or removing a property that dashboards may reference), MINOR for new features, PATCH for bug fixes.

---

## 10. Common Failure Modes and Fixes

| Symptom | Likely Cause | Fix |
|---|---|---|
| Plugin doesn't appear in SimHub at all | Wrong target framework, or DLL not in SimHub root | Verify `net48` target; check DLL is in the same folder as `SimHubWPF.exe` |
| Plugin loads but properties are empty | `AddProperty()` not called in `Init()`, or `SetPropertyValue()` wrong type string | Check `this.GetType()` is passed, not a hardcoded type |
| SimHub crashes on startup after adding plugin | Dependency DLL version conflict (often Newtonsoft.Json) | Set `Private=False` on all SimHub-bundled references; never ship your own copy |
| Settings panel doesn't appear | XAML resource URI mismatch — class name or namespace changed | Ensure `x:Class` in XAML matches the actual namespace+classname exactly |
| Settings panel appears but controls are empty | `DataContext` not set, or property names in binding don't match settings class | Set `DataContext = Settings` in `GetWPFSettingsControl()`; check binding paths |
| Duplicate plugin error on load | Old DLL left in SimHub folder alongside new one | Delete old DLL; only one DLL per plugin |
| `NullReferenceException` in DataUpdate | Accessing `data.OldData` before it's populated, or `data.NewData` nested object is null | Always null-check `data.OldData` and guard with `data.GameRunning` |
| Properties publish stale values | Reading game properties from `data.OldData` instead of `data.NewData` | Double-check you're reading `data.NewData.*` |
| UI not updating live during session | Updating `ObservableCollection` from DataUpdate thread directly | Wrap collection updates in `Dispatcher.BeginInvoke()` |

---

## 11. The SimHub Discord

The most active place for plugin development help is the **SimHub Discord server**: https://discord.com/invite/nBBMuX7

There is a dedicated channel for plugin/SDK development where SimHub's author (Wotever) and experienced plugin developers are active. Before posting, check the pinned messages and search for your issue — many common questions have been answered there.

The SimHub forum at https://www.simhubdash.com/community-2/ is also useful but slower-moving than Discord.

---

## 12. Reference Checklist Before First Release

- [ ] Namespace is unique (`Author.PluginName`, not the SDK demo default)
- [ ] All SimHub-bundled DLLs have `Private=False` — especially `Newtonsoft.Json`
- [ ] `DataUpdate()` is wrapped in try/catch
- [ ] No file I/O, network calls, or heavy LINQ inside `DataUpdate()`
- [ ] `data.GameRunning`, `data.NewData != null`, and `data.OldData != null` guards are in place
- [ ] Plugin version is published as a property
- [ ] Settings are loaded in `Init()` and saved in `End()`
- [ ] UI updates from background threads go through `Dispatcher.BeginInvoke()`
- [ ] `GetSettingsControl()` returns `null` (WinForms legacy method)
- [ ] Plugin tested with at least one game active and with no game active
- [ ] Logs are clean — no repeated errors or warnings in `simhub.txt`
- [ ] README includes: what the plugin does, installation steps, list of properties published

---

## 13. Quick Reference: Key Paths

| Path | Purpose |
|---|---|
| `C:\Program Files (x86)\SimHub\` | Plugin DLL installation folder (alongside `SimHubWPF.exe`) |
| `C:\Program Files (x86)\SimHub\Logs\simhub.txt` | Runtime logs — your debugging window |
| `C:\Program Files (x86)\SimHub\PluginSdk\` | Official SDK demo projects |
| `SimHub\PluginsData\Common\` | Where `GetCommonStoragePath()` resolves to |
| `SimHub\PluginsData\Common\MyPlugin.json` | Example settings/data file location |

---

## 14. Summary: The Full Plugin Development Loop

```
1. Copy SDK demo from PluginSdk\ → rename namespace to Author.MyPlugin
2. Set target framework to net48 in .csproj
3. Set Private=False on all SimHub DLL references
4. Implement Init() → register properties and actions, load settings
5. Implement DataUpdate() → read GameData, compute, SetPropertyValue
6. Implement End() → save settings
7. Build → copy DLL to SimHub folder
8. Launch SimHub via VS debugger (F5 with external program = SimHubWPF.exe)
9. Test: enable plugin, verify properties in property browser, run a game
10. Iterate: change code → build → SimHub auto-reloads on next launch
11. When ready: zip DLL(s) + README, share on Discord or OverTake.gg
```
