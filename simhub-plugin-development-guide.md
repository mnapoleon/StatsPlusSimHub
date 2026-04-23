# SimHub Plugin Development Guide

## Overview & Prerequisites

SimHub provides three demo projects to get you started, located at `C:\Program Files (x86)\SimHub\PluginSdk`:

- `User.DeviceExtensionDemo` — attach an extension to an existing device
- `User.LedEditorEffect` — create custom LED effects
- `User.PluginSdkDemo` — create a full plugin that registers properties and actions

**Requirements:**
- Visual Studio 2022 or later
- WPF and C# knowledge
- .NET Framework 4.8 (non-negotiable — SimHub itself is a .NET Framework 4.8 WPF application)

> **Warning:** The SDK is limited to SimHub's core as demonstrated in the examples. Reusing undocumented components is not blocked, but can lead to broken plugins after a SimHub update.

---

## Project Structure

A typical plugin project contains:

```
MyPlugin/
├── MyPlugin.cs                  # Main plugin class
├── MyPluginSettings.cs          # Settings data container
├── SettingsControl.xaml         # WPF settings UI (optional)
├── SettingsControl.xaml.cs
└── MyPlugin.csproj              # Targeting net48
```

The namespace convention used by the community is `Author.PluginName` (e.g., `Viper.PluginCalcLngWheelSlip`).

### Project File (`.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>Author.MyPlugin</AssemblyName>
    <RootNamespace>Author.MyPlugin</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference SimHub DLLs from the SimHub install folder -->
    <Reference Include="SimHub.Plugins">
      <HintPath>C:\Program Files (x86)\SimHub\SimHub.Plugins.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="GameReaderCommon">
      <HintPath>C:\Program Files (x86)\SimHub\GameReaderCommon.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
</Project>
```

---

## The Plugin Interfaces

Your main class implements one or more interfaces:

| Interface | Purpose |
|---|---|
| `IPlugin` | Required. Lifecycle methods + metadata attributes |
| `IDataPlugin` | Provides `DataUpdate()` called every frame |
| `IWPFSettings` / `IWPFSettingsV2` | Provides a settings UI panel inside SimHub |

```csharp
using GameReaderCommon;
using SimHub.Plugins;

[PluginName("My Plugin Name")]
[PluginDescription("What this plugin does")]
[PluginAuthor("YourName")]
public class MyPlugin : IPlugin, IDataPlugin, IWPFSettings
{
    public PluginManager PluginManager { get; set; }

    public void Init(PluginManager pluginManager) { ... }
    public void DataUpdate(PluginManager pluginManager, ref GameData data) { ... }
    public void End(PluginManager pluginManager) { ... }

    public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        => new SettingsControl();

    // Legacy — return null if only using WPF
    public System.Windows.Forms.Control GetSettingsControl(PluginManager pluginManager)
        => null;
}
```

---

## The Plugin Lifecycle

```
SimHub starts
    └─> Init()              ← register properties & actions, load settings
         └─> [frame loop ~60Hz]
              └─> DataUpdate()   ← called every frame with fresh GameData
         └─> End()          ← save settings, clean up resources
```

- `Init()` runs **once** at startup
- `DataUpdate()` runs **every frame** (~60 times per second) — keep it fast
- `End()` runs **once** on shutdown or game switch

> **Performance rule:** Declare all persistent variables at the class level. Never do file I/O, network calls, or heavy loops inside `DataUpdate()`. Do expensive work in `Init()` or `End()`, and only read/write cached fields in `DataUpdate()`.

---

## Registering Properties & Actions

In `Init()`, declare what your plugin will publish to the rest of SimHub. These properties become available in dashboards, overlays, NCalc expressions, and other plugins.

```csharp
public void Init(PluginManager pluginManager)
{
    // Register output properties with initial values
    pluginManager.AddProperty("MyPlugin.MyDoubleValue", this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.MyStringValue", this.GetType(), "n/a");
    pluginManager.AddProperty("MyPlugin.IsReady",       this.GetType(), false);

    // Register triggerable actions (e.g., from button boxes or Stream Deck)
    pluginManager.AddAction("MyPlugin.ResetSomething", this.GetType(), (manager, b) =>
    {
        this.resetFlag = true;
    });
}
```

Then in `DataUpdate()`, push updated values each frame:

```csharp
pluginManager.SetPropertyValue("MyPlugin.MyDoubleValue", this.GetType(), computedValue);
```

Your properties then appear in SimHub's property browser under your plugin's name and can be referenced in dashboards as `[MyPlugin.MyDoubleValue]`.

---

## Accessing GameData (Normalized Telemetry)

The `GameData data` parameter in `DataUpdate()` contains two snapshots:
- `data.NewData` — current frame telemetry
- `data.OldData` — previous frame telemetry (useful for detecting changes)

**Always guard against null and verify the game is running:**

```csharp
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    if (data.GameRunning && data.OldData != null && data.NewData != null)
    {
        double speedKmh   = data.NewData.SpeedKmh;
        double throttle   = data.NewData.Throttle;    // 0.0 – 1.0
        double brake      = data.NewData.Brake;        // 0.0 – 1.0
        double clutch     = data.NewData.Clutch;       // 0.0 – 1.0
        string gear       = data.NewData.Gear;         // "N", "R", "1"–"8"
        string carModel   = data.NewData.CarModel;
        string trackName  = data.NewData.TrackName;
        string gameName   = data.GameName;
        int    currentLap = data.NewData.CurrentLap;
        bool   isInPit    = data.NewData.IsInPit;
    }
}
```

### Common `data.NewData` Properties

| Property           | Type     | Description                      |
| ------------------ | -------- | -------------------------------- |
| `SpeedKmh`         | double   | Car speed in km/h                |
| `Rpms`             | double   | Engine RPM                       |
| `MaxRpm`           | double   | Engine redline RPM               |
| `Gear`             | string   | Current gear ("N", "R", "1"–"8") |
| `Throttle`         | double   | Throttle input (0–1)             |
| `Brake`            | double   | Brake input (0–1)                |
| `Clutch`           | double   | Clutch input (0–1)               |
| `Steering`         | double   | Steering input                   |
| `CarModel`         | string   | Car identifier string            |
| `TrackName`        | string   | Current track name               |
| `CurrentLap`       | int      | Current lap number               |
| `IsInPit`          | bool     | Whether car is in pit lane       |
| `IsInMenu`         | bool     | Whether player is in a menu      |
| `SessionType`      | string   | Race/Qualify/Practice etc.       |
| `TyreTemperature`  | double[] | Tyre temps (FL, FR, RL, RR)      |
| `TyrePressure`     | double[] | Tyre pressures (FL, FR, RL, RR)  |
| `BrakeTemperature` | double[] | Brake temps (FL, FR, RL, RR)     |
| `FuelLevel`        | double   | Current fuel level               |
| `FuelPercent`      | double   | Fuel as percentage of capacity   |

### Detecting Changes Between Frames

```csharp
// Detect car/session change
if (data.OldData.CarModel != data.NewData.CarModel)
{
    // Car has changed — reset computed values
}

// Detect gear change
if (data.OldData.Gear != data.NewData.Gear)
{
    // Gear shifted
}
```

---

## Accessing Raw Game Data

Beyond the normalized `GameData` properties, you can reach raw, game-specific telemetry packets via `pluginManager.GetPropertyValue()`. This is how you access physics values that SimHub doesn't expose in its normalized layer.

The path pattern is:
```
DataCorePlugin.GameRawData.<GameSpecificPropertyPath>
```

**Always check for null — not all raw properties exist in all games or all modes (e.g., shared memory vs UDP):**

```csharp
var rawValue = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Physics.WheelAngularSpeed01");
if (rawValue != null)
{
    float angularSpeed = (float)rawValue;
}
```

### Raw Data Examples by Game

```csharp
// --- Speed (works across most games) ---
float speedMs = (float)((double)pluginManager.GetPropertyValue(
    "DataCorePlugin.GameData.NewData.SpeedKmh") / 3.6);

// --- Assetto Corsa / ACC / AC EVO / AC Rally ---
// Lateral G-force (used as proxy for lateral velocity — not available directly)
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Physics.AccG01")
// Wheel angular speed (radians/sec) — FL, FR, RL, RR = 01, 02, 03, 04
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Physics.WheelAngularSpeed01")

// --- rFactor 2 / Le Mans Ultimate ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.CurrentPlayer.mLocalVel.x")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.CurrentPlayerTelemetry.mWheels01.mRotation")

// --- Project CARS 2 / Automobilista 2 (Shared Memory) ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.mLocalVelocity01")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.mTyreRPS01")  // FL=01, FR=02, RL=03, RR=04

// --- Project CARS 2 / AMS2 (UDP mode) ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.sTelemetryData.sLocalVelocity01")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.sTelemetryData.sTyreRPS01")

// --- Race Room Racing Experience ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Player.LocalVelocity.X")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.TireRps.FrontLeft")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.TireRps.FrontRight")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.TireRps.RearLeft")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.TireRps.RearRight")

// --- F1 2018–2022 ---
// Wheel speeds (order from API: RL=01, RR=02, FL=03, FR=04)
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.PlayerMotionData.m_wheelSpeed01")

// --- F1 2023–2025 (property names changed) ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.PacketMotionExData.m_wheelSpeed01")

// --- Gran Turismo 7 ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.LocalVelocity.X")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Wheel_RevPerSecond01")

// --- EA WRC 2023 ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionUpdate.vehicle_speed")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionUpdate.vehicle_cp_forward_speed_fl")

// --- Dirt Rally 2.0 ---
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.WheelSpeedFrontLeft")
pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.WheelSpeedFrontRight")
```

> **Tip:** The best way to discover available raw property paths is SimHub's built-in **property browser** (accessible from the dash editor). Filter by `GameRawData` while a game is running to see everything available for that sim.

### Multi-Game Switch Pattern

Gate raw data access by `data.GameName` to avoid null reference errors:

```csharp
switch (data.GameName)
{
    case "AssettoCorsaCompetizione":
    case "AssettoCorsa":
        tyreSpeed = (float)pluginManager.GetPropertyValue(
            "DataCorePlugin.GameRawData.Physics.WheelAngularSpeed01");
        break;

    case "RFactor2":
    case "LMU":
        tyreSpeed = (float)(double)pluginManager.GetPropertyValue(
            "DataCorePlugin.GameRawData.CurrentPlayerTelemetry.mWheels01.mRotation");
        break;

    case "F12024":
    case "F12025":
        tyreSpeed = (float)pluginManager.GetPropertyValue(
            "DataCorePlugin.GameRawData.PacketMotionExData.m_wheelSpeed03");
        break;
    // etc.
}
```

---

## Settings Persistence

SimHub provides a path helper for storing plugin data in a standard location:

```csharp
// Resolves to: SimHub\PluginsData\Common\MyPlugin.settings.json
string path = PluginManager.GetCommonStoragePath("MyPlugin.settings.json");
```

### Typical Settings Pattern

```csharp
// In Init() — load settings
try
{
    string json = File.ReadAllText(PluginManager.GetCommonStoragePath("MyPlugin.json"));
    var settings = JsonConvert.DeserializeObject<MySettings>(json);
    // apply settings...
}
catch
{
    // File doesn't exist yet — apply defaults
}

// In End() — save settings
string outputPath = PluginManager.GetCommonStoragePath("MyPlugin.json");
File.WriteAllText(outputPath, JsonConvert.SerializeObject(mySettings), Encoding.UTF8);
```

> **Performance note:** Only read settings in `Init()` and write in `End()`. Never do file I/O during `DataUpdate()`.

---

## Settings UI (WPF)

The settings panel is a standard WPF `UserControl`. SimHub embeds it under **Additional Plugins → Your Plugin Name**.

**SettingsControl.xaml:**
```xml
<UserControl x:Class="Author.MyPlugin.SettingsControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="10">
        <Label Content="Min Speed (km/h)" />
        <Slider x:Name="SpeedSlider" Minimum="10" Maximum="200"
                Value="{Binding MinSpeed}" />
        <TextBlock Text="{Binding MinSpeed, StringFormat={}{0:F0} km/h}" />
    </StackPanel>
</UserControl>
```

**SettingsControl.xaml.cs:**
```csharp
public partial class SettingsControl : UserControl
{
    public SettingsControl()
    {
        InitializeComponent();
        DataContext = AccData.Instance; // bind to your settings object
    }
}
```

Return it from `GetWPFSettingsControl()`:
```csharp
public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
    => new SettingsControl();
```

---

## Logging

Use SimHub's built-in logger rather than `Console.WriteLine()`:

```csharp
using SimHub;

Logging.Current.Info("MyPlugin - Settings loaded successfully.");
Logging.Current.Warn("MyPlugin - Unexpected null value for property X.");
Logging.Current.Error("MyPlugin - Failed to parse data file: " + ex.Message);
```

Logs appear in `SimHub\Logs\simhub.txt`.

---

## Complete Minimal Example

A full plugin that reads speed and RPM and publishes derived properties:

```csharp
using GameReaderCommon;
using SimHub.Plugins;
using SimHub;

namespace Author.MyPlugin
{
    [PluginName("My Telemetry Plugin")]
    [PluginDescription("Reads speed and RPM, publishes derived properties")]
    [PluginAuthor("YourName")]
    public class MyPlugin : IPlugin, IDataPlugin, IWPFSettings
    {
        public PluginManager PluginManager { get; set; }

        // Class-level state (persists between frames)
        private double _peakRpm = 0;
        private string _lastCar = string.Empty;

        public void Init(PluginManager pluginManager)
        {
            Logging.Current.Info("MyPlugin - Starting up");

            pluginManager.AddProperty("MyPlugin.SpeedMs",  this.GetType(), 0.0);
            pluginManager.AddProperty("MyPlugin.PeakRpm",  this.GetType(), 0.0);
            pluginManager.AddProperty("MyPlugin.RpmPercent", this.GetType(), 0.0);

            pluginManager.AddAction("MyPlugin.ResetPeakRpm", this.GetType(), (manager, b) =>
            {
                _peakRpm = 0;
            });

            Logging.Current.Info("MyPlugin - Initialised");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning || data.NewData == null || data.OldData == null)
                return;

            // Detect car change and reset peak RPM
            if (data.NewData.CarModel != _lastCar)
            {
                _peakRpm = 0;
                _lastCar = data.NewData.CarModel;
            }

            double speedMs     = data.NewData.SpeedKmh / 3.6;
            double rpm         = data.NewData.Rpms;
            double maxRpm      = data.NewData.MaxRpm;
            double rpmPercent  = maxRpm > 0 ? rpm / maxRpm * 100.0 : 0;

            if (rpm > _peakRpm)
                _peakRpm = rpm;

            pluginManager.SetPropertyValue("MyPlugin.SpeedMs",    this.GetType(), speedMs);
            pluginManager.SetPropertyValue("MyPlugin.PeakRpm",    this.GetType(), _peakRpm);
            pluginManager.SetPropertyValue("MyPlugin.RpmPercent", this.GetType(), rpmPercent);
        }

        public void End(PluginManager pluginManager)
        {
            Logging.Current.Info("MyPlugin - Shutting down");
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
            => new SettingsControl();

        public System.Windows.Forms.Control GetSettingsControl(PluginManager pluginManager)
            => null;
    }
}
```

---

## Installation

1. Build your project in Release mode — this produces `Author.MyPlugin.dll`
2. Copy the DLL into the SimHub installation folder alongside `SimHubWPF.exe` (usually `C:\Program Files (x86)\SimHub\`)
3. Launch SimHub — it will detect the new plugin and ask if you want to enable it
4. Confirm, and your plugin will appear under **Settings → Plugins**

---

## Debugging

Set up Visual Studio to launch SimHub directly from the debugger:

1. Right-click your project → **Properties** → **Debug**
2. Set **Start external program** to `C:\Program Files (x86)\SimHub\SimHubWPF.exe`
3. Set the working directory to the SimHub folder
4. Choose **Debug** configuration and press **Start** (F5)

SimHub will launch with your plugin loaded and Visual Studio will break on exceptions and hit breakpoints inside `DataUpdate()`.

> Start with Debug configuration rather than building in Release and manually starting SimHub — it will break and tell you what went wrong when errors occur.

---

## Key Gotchas

**Cast `GetPropertyValue()` carefully.** It returns `object`. Different games return different underlying types for the same conceptual value. The wheel slip plugin consistently double-casts with `(float)(double)` in some places and `(float)` in others depending on the game. Always null-check before casting.

**Check for both shared memory and UDP modes.** Games like AMS2 and pCARS2 can run in either mode, which exposes completely different raw property paths. Check which mode is active before reading, or read a sentinel property to detect the mode.

**`data.OldData` can be null at session start.** Always guard `data.OldData != null` before using it for change detection, not just `data.GameRunning`.

**Property names changed between F1 game years.** F1 2022 and earlier use `PlayerMotionData.m_wheelSpeed*`, while F1 2023+ use `PacketMotionExData.m_wheelSpeed*`. Always check game-year-specific versions.

**Keep `DataUpdate()` under ~16ms.** It runs at approximately 60Hz. Anything slower will start to degrade SimHub's overall performance for other plugins and dashboards.

**Don't reference undocumented SimHub internals.** It works until a SimHub update breaks it. Stick to `SimHub.Plugins` and `GameReaderCommon` public APIs.

---

## Key Open Source Examples

| Plugin                                                                         | Author     | What it demonstrates                                                               |
| ------------------------------------------------------------------------------ | ---------- | ---------------------------------------------------------------------------------- |
| [CalcLngWheelSlip](https://github.com/viper4gh/SimHub-Plugin-CalcLngWheelSlip) | viper4gh   | Raw data access across 10+ games, per-game switch logic, JSON persistence, actions |
| [SimHub SLI Plugin](https://github.com/simelation/simhub-plugins)              | simelation | Well-documented shift light indicator, clean structure                             |
| [SimHubPropertyServer](https://github.com/pre-martin/SimHubPropertyServer)     | pre-martin | TCP property server, computed properties via JavaScript                            |
| [DIY FFB Pedal](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal)            | ChrGri     | Full-featured plugin with serial comms, vJoy, complex WPF UI                       |
| [SimHubPluginSdk](https://github.com/blekenbleu/SimHubPluginSdk)               | blekenbleu | Portable SDK demo with namespace best-practices notes                              |

---

## Useful Property Paths Reference

These are the property path patterns used in NCalc, dashboards, and `GetPropertyValue()`:

```
DataCorePlugin.GameData.NewData.SpeedKmh       ← normalized speed
DataCorePlugin.GameData.NewData.Rpms           ← normalized RPM
DataCorePlugin.GameData.NewData.Gear           ← normalized gear
DataCorePlugin.GameData.NewData.Throttle       ← normalized throttle
DataCorePlugin.GameData.NewData.Brake          ← normalized brake
DataCorePlugin.GameData.NewData.CarModel       ← car identifier
DataCorePlugin.CurrentGame                     ← currently selected game name
DataCorePlugin.GameRawData.*                   ← raw game-specific telemetry
YourPlugin.YourProperty                        ← your own published properties
```

In NCalc dashboard expressions, wrap them in square brackets:
```
[DataCorePlugin.GameData.NewData.SpeedKmh]
[YourPlugin.YourProperty]
```
