# SimHub Plugin UI Guide

## How Plugin UIs Work in SimHub

SimHub is itself a WPF application targeting .NET Framework 4.8. Plugin UIs are standard WPF `UserControl` objects that SimHub embeds into its own window. There is no separate window — your plugin's UI is hosted directly inside SimHub's main UI, in one of two locations:

- **Settings panel** — appears under **Additional Plugins → Your Plugin Name** in the left-hand menu. This is the standard location for plugin configuration.
- **Left menu entry** — if your plugin registers itself as a top-level menu item (via `IWPFSettingsV2`), it gets its own entry in SimHub's left navigation sidebar, like a first-class SimHub feature.

The entire UI stack is pure WPF/XAML. SimHub also bundles **MahApps.Metro** and its own **SimHub.Plugins.Styles** assembly, both of which you can reference freely to produce a UI that matches SimHub's own look and feel.

---

## The Two UI Interfaces

### `IWPFSettings`

The basic interface. Your plugin appears under **Additional Plugins** in SimHub's menu.

```csharp
public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
{
    return new SettingsControl(this) { DataContext = Settings };
}

// Legacy method — return null unless you need WinForms compatibility
public System.Windows.Forms.Control GetSettingsControl(PluginManager pluginManager)
    => null;
```

### `IWPFSettingsV2`

Adds the ability to appear as a dedicated entry in SimHub's left-hand navigation sidebar (same level as "Dash Studio", "ShakeIt", etc.). Preferred for plugins with substantial UIs.

```csharp
// Note: as of ~2023, some developers report removing IWPFSettingsV2 from the
// class declaration resolves loading issues — use IWPFSettings if you have problems.
public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
{
    return new SettingsControl(this) { DataContext = Settings };
}
```

> **Key pattern:** Pass both the plugin instance (`this`) and set `DataContext = Settings` when constructing the control. This gives the XAML bindings direct access to your settings object, and the code-behind access to the plugin for triggering actions.

---

## Project File Setup

Your `.csproj` needs to reference both SimHub's style assembly and MahApps, which are already present in the SimHub installation folder:

```xml
<ItemGroup>
  <Reference Include="SimHub.Plugins">
    <HintPath>C:\Program Files (x86)\SimHub\SimHub.Plugins.dll</HintPath>
    <Private>False</Private>
  </Reference>
  <Reference Include="MahApps.Metro">
    <HintPath>C:\Program Files (x86)\SimHub\MahApps.Metro.dll</HintPath>
    <Private>False</Private>
  </Reference>
</ItemGroup>

<!-- WPF support -->
<ItemGroup>
  <Page Include="SettingsControl.xaml">
    <SubType>Designer</SubType>
    <Generator>MSBuild:Compile</Generator>
  </Page>
</ItemGroup>
```

> Set `<Private>False</Private>` on SimHub references so the DLLs are not copied into your output folder. SimHub already provides them at runtime.

---

## File Structure

A typical plugin with a settings UI contains these files:

```
MyPlugin/
├── MyPlugin.cs               # Plugin class (IPlugin, IDataPlugin, IWPFSettings)
├── MyPluginSettings.cs       # Settings POCO — this becomes the DataContext
├── SettingsControl.xaml      # The WPF UserControl markup
└── SettingsControl.xaml.cs   # Code-behind (event handlers, etc.)
```

---

## The Settings Object (DataContext)

This is the class that backs your UI bindings. For two-way bindings to work reactively, implement `INotifyPropertyChanged`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class MyPluginSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private int _minSpeedKmh = 50;
    public int MinSpeedKmh
    {
        get => _minSpeedKmh;
        set { _minSpeedKmh = value; OnPropertyChanged(); }
    }

    private bool _enableFeature = true;
    public bool EnableFeature
    {
        get => _enableFeature;
        set { _enableFeature = value; OnPropertyChanged(); }
    }

    private double _threshold = 0.001;
    public double Threshold
    {
        get => _threshold;
        set { _threshold = value; OnPropertyChanged(); }
    }
}
```

In your plugin class, hold and expose it:

```csharp
public MyPluginSettings Settings { get; private set; }

public void Init(PluginManager pluginManager)
{
    // Load from disk or create defaults
    Settings = this.ReadCommonSettings<MyPluginSettings>("Settings", () => new MyPluginSettings());
    // ... register properties, actions etc.
}

public void End(PluginManager pluginManager)
{
    this.SaveCommonSettings("Settings", Settings);
}

public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
{
    return new SettingsControl(this) { DataContext = Settings };
}
```

> `ReadCommonSettings` and `SaveCommonSettings` are helper methods provided by SimHub's plugin base. They handle serialization to the `PluginsData` folder automatically.

---

## The XAML File

### Namespaces

Every SimHub settings control needs these XAML namespace declarations:

```xml
<UserControl
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"

    <!-- SimHub's own style components -->
    xmlns:styles="clr-namespace:SimHub.Plugins.Styles;assembly=SimHub.Plugins"

    <!-- MahApps.Metro controls (ToggleSwitch, NumericUpDown, etc.) -->
    xmlns:mah="http://metro.mahapps.com/winfx/xaml/controls"

    x:Class="Author.MyPlugin.SettingsControl"
    mc:Ignorable="d">
```

### SimHub Style Components

SimHub provides a small set of styled layout components in `SimHub.Plugins.Styles` that match its internal UI. The most important is `SHSection`:

```xml
<!-- SHSection: a collapsible titled panel — the standard SimHub settings block -->
<styles:SHSection Title="Plugin Options">
    <StackPanel>
        <!-- your controls here -->
    </StackPanel>
</styles:SHSection>
```

`SHSection` renders as a titled, bordered panel exactly matching SimHub's own settings sections. Always wrap your settings inside one (or more) of these.

### Full Minimal Example

```xml
<UserControl
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:styles="clr-namespace:SimHub.Plugins.Styles;assembly=SimHub.Plugins"
    xmlns:mah="http://metro.mahapps.com/winfx/xaml/controls"
    x:Class="Author.MyPlugin.SettingsControl"
    mc:Ignorable="d">

    <ScrollViewer HorizontalScrollBarVisibility="Auto">
        <StackPanel>

            <styles:SHSection Title="General Settings">
                <StackPanel Margin="10">

                    <!-- Toggle / checkbox -->
                    <StackPanel Orientation="Horizontal" Margin="0,5">
                        <TextBlock Text="Enable feature" VerticalAlignment="Center" Width="150"/>
                        <mah:ToggleSwitch IsOn="{Binding EnableFeature, Mode=TwoWay}"/>
                    </StackPanel>

                    <!-- Numeric spinner -->
                    <StackPanel Orientation="Horizontal" Margin="0,5">
                        <TextBlock Text="Min speed (km/h)" VerticalAlignment="Center" Width="150"/>
                        <mah:NumericUpDown
                            Value="{Binding MinSpeedKmh, Mode=TwoWay}"
                            Minimum="1" Maximum="300"
                            Width="100"/>
                    </StackPanel>

                    <!-- Decimal spinner -->
                    <StackPanel Orientation="Horizontal" Margin="0,5">
                        <TextBlock Text="Threshold" VerticalAlignment="Center" Width="150"/>
                        <mah:NumericUpDown
                            Value="{Binding Threshold, Mode=TwoWay}"
                            Interval="0.0001" Minimum="0.0001" Maximum="1.0"
                            Width="120"/>
                    </StackPanel>

                    <!-- Text box -->
                    <StackPanel Orientation="Horizontal" Margin="0,5">
                        <TextBlock Text="Label" VerticalAlignment="Center" Width="150"/>
                        <TextBox Text="{Binding SomeText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                 Width="200"/>
                    </StackPanel>

                    <!-- Standard button -->
                    <Button Content="Reset to defaults" Click="ResetButton_Click"
                            Margin="0,10,0,0" HorizontalAlignment="Left"/>

                </StackPanel>
            </styles:SHSection>

            <styles:SHSection Title="About">
                <StackPanel Margin="10">
                    <TextBlock TextWrapping="Wrap" FontWeight="Bold" Text="My Plugin v1.0"/>
                    <TextBlock TextWrapping="Wrap" Margin="0,5,0,0"
                               Text="Description of what this plugin does goes here."/>
                </StackPanel>
            </styles:SHSection>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

---

## The Code-Behind

```csharp
using System.Windows.Controls;

namespace Author.MyPlugin
{
    public partial class SettingsControl : UserControl
    {
        private readonly MyPlugin _plugin;

        // Constructor receives the plugin instance for triggering actions
        public SettingsControl(MyPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();
        }

        private void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // DataContext is the Settings object — cast to access it
            if (DataContext is MyPluginSettings settings)
            {
                settings.MinSpeedKmh   = 50;
                settings.EnableFeature = true;
                settings.Threshold     = 0.001;
                // Because Settings implements INotifyPropertyChanged,
                // the UI updates automatically
            }
        }
    }
}
```

---

## MahApps.Metro Controls Reference

SimHub bundles MahApps.Metro, giving you access to all its controls. The most useful ones for plugin settings:

### ToggleSwitch (on/off toggle)

```xml
<mah:ToggleSwitch IsOn="{Binding EnableFeature, Mode=TwoWay}"
                  OnContent="On" OffContent="Off"/>
```

### NumericUpDown (integer or decimal spinner)

```xml
<!-- Integer -->
<mah:NumericUpDown Value="{Binding MinSpeed, Mode=TwoWay}"
                   Minimum="0" Maximum="300" Interval="1"/>

<!-- Decimal -->
<mah:NumericUpDown Value="{Binding Threshold, Mode=TwoWay}"
                   Minimum="0.001" Maximum="1.0"
                   Interval="0.001"
                   StringFormat="F4"/>
```

### ComboBox (dropdown)

```xml
<!-- Bind to a list of strings -->
<ComboBox ItemsSource="{Binding AvailableOptions}"
          SelectedItem="{Binding SelectedOption, Mode=TwoWay}"
          Width="200"/>
```

### CheckBox

```xml
<CheckBox IsChecked="{Binding SomeBool, Mode=TwoWay}"
          Content="Enable this option"/>
```

### Slider

```xml
<StackPanel Orientation="Horizontal">
    <Slider Value="{Binding SliderValue, Mode=TwoWay}"
            Minimum="0" Maximum="100"
            Width="200"
            TickFrequency="10" IsSnapToTickEnabled="True"/>
    <TextBlock Text="{Binding SliderValue, StringFormat={}{0:F0}}"
               VerticalAlignment="Center" Margin="8,0,0,0"/>
</StackPanel>
```

### DataGrid (for displaying tabular data like lap times)

```xml
<DataGrid ItemsSource="{Binding SessionLaps}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          Margin="0,10">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Lap"     Binding="{Binding LapNumber}" Width="50"/>
        <DataGridTextColumn Header="Time"    Binding="{Binding LapTime, StringFormat={}{0:F3}}" Width="100"/>
        <DataGridTextColumn Header="Sector 1" Binding="{Binding Sectors.S1, StringFormat={}{0:F3}}" Width="80"/>
        <DataGridTextColumn Header="Sector 2" Binding="{Binding Sectors.S2, StringFormat={}{0:F3}}" Width="80"/>
        <DataGridTextColumn Header="Sector 3" Binding="{Binding Sectors.S3, StringFormat={}{0:F3}}" Width="80"/>
        <DataGridTextColumn Header="Valid"   Binding="{Binding IsValid}" Width="60"/>
    </DataGrid.Columns>
</DataGrid>
```

---

## Two Binding Approaches

### Approach 1: XAML Data Binding (Recommended)

Set `DataContext = Settings` when constructing the control. XAML binds directly to properties on the settings object. No code-behind needed for routine reads/writes.

```csharp
// In GetWPFSettingsControl():
return new SettingsControl(this) { DataContext = Settings };
```

```xml
<!-- In XAML — binds directly to Settings.MinSpeedKmh: -->
<mah:NumericUpDown Value="{Binding MinSpeedKmh, Mode=TwoWay}" .../>
```

This is clean and requires no code-behind event handlers for simple property changes.

### Approach 2: Code-Behind Event Handlers

Used in the wheel-slip plugin and many community plugins. Controls are given `x:Name` and values are read/written in C# event handlers. Less idiomatic WPF, but simpler for beginners and avoids binding edge cases.

```xml
<mah:NumericUpDown x:Name="SpeedInput" ValueChanged="SpeedInput_ValueChanged"
                   Minimum="1" Maximum="250"/>
```

```csharp
// Load values when the section becomes visible
private void SHSection_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    if ((bool)e.NewValue)
    {
        SpeedInput.Value = AccData.Speed;   // populate from settings
    }
}

// Write back to settings on change
private void SpeedInput_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
{
    if (e.NewValue.HasValue)
        AccData.Speed = (int)e.NewValue.Value;
}
```

Both approaches work. XAML binding is cleaner for settings forms; code-behind event handlers are useful when you need to trigger side effects (like validation or live updates to plugin properties) when a value changes.

---

## Displaying Live Telemetry in the UI

If you want your settings panel to show live data (e.g., a readout of current telemetry values, a lap list that updates in real time), you need to be careful about threading. `DataUpdate()` runs on SimHub's data thread, not the UI thread. Any update to an observable collection or bound property that the UI is watching must be dispatched to the UI thread:

```csharp
// In DataUpdate() — DO NOT update UI-bound collections directly
// Instead, dispatch to the UI thread:
System.Windows.Application.Current?.Dispatcher.BeginInvoke(
    System.Windows.Threading.DispatcherPriority.Normal,
    new Action(() =>
    {
        SessionLaps.Add(newLap);          // ObservableCollection
        LiveSpeedText = $"{speedKmh:F1} km/h";  // INotifyPropertyChanged property
    }));
```

For a live data view, use `ObservableCollection<T>` instead of `List<T>` — it automatically notifies the UI when items are added or removed:

```csharp
// In your plugin class or a ViewModel:
public ObservableCollection<LapRecord> SessionLaps { get; } = new ObservableCollection<LapRecord>();
```

```xml
<!-- In XAML — updates automatically when laps are added -->
<DataGrid ItemsSource="{Binding SessionLaps}" .../>
```

---

## Common Layout Patterns

### Two-Column Settings Row

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="180"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Grid.Column="0" Text="Min Speed (km/h)" VerticalAlignment="Center"/>
    <mah:NumericUpDown Grid.Row="0" Grid.Column="1" Value="{Binding MinSpeedKmh, Mode=TwoWay}"
                       Minimum="1" Maximum="300" Width="120" HorizontalAlignment="Left"/>

    <TextBlock Grid.Row="1" Grid.Column="0" Text="Enable feature" VerticalAlignment="Center" Margin="0,8,0,0"/>
    <mah:ToggleSwitch Grid.Row="1" Grid.Column="1" IsOn="{Binding EnableFeature, Mode=TwoWay}" Margin="0,8,0,0"/>
</Grid>
```

### Read-Only Status Row

```xml
<StackPanel Orientation="Horizontal" Margin="0,5">
    <TextBlock Text="Session laps:" Width="120" VerticalAlignment="Center"/>
    <TextBlock Text="{Binding SessionLapCount}" FontWeight="Bold" VerticalAlignment="Center"/>
</StackPanel>
```

### Multiple Sections

```xml
<ScrollViewer>
    <StackPanel>
        <styles:SHSection Title="Detection Limits">
            <!-- detection settings -->
        </styles:SHSection>

        <styles:SHSection Title="Session Data">
            <!-- lap history DataGrid -->
        </styles:SHSection>

        <styles:SHSection Title="About">
            <!-- version info, description -->
        </styles:SHSection>
    </StackPanel>
</ScrollViewer>
```

Always wrap the outer `StackPanel` in a `ScrollViewer` — SimHub's panel area has a fixed height and content will be clipped without it.

---

## Practical Example: Real Plugin XAML (Wheel Slip Plugin)

From the open-source `CalcLngWheelSlip` plugin, this is a production-quality real settings control:

```xml
<UserControl
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:styles="clr-namespace:SimHub.Plugins.Styles;assembly=SimHub.Plugins"
    xmlns:Custom="http://metro.mahapps.com/winfx/xaml/controls"
    x:Class="Viper.PluginCalcLngWheelSlip.SettingsControl"
    mc:Ignorable="d">

    <ScrollViewer HorizontalScrollBarVisibility="Auto">
        <Grid>
            <styles:SHSection x:Name="SHSectionPluginOptions" Title="Plugin Options"
                              IsVisibleChanged="SHSection_IsVisibleChanged">
                <StackPanel>
                    <TextBlock HorizontalAlignment="Left" Margin="34,10,0,10"
                               TextWrapping="Wrap"
                               Text="Limits for triggering the tyre diameter calculation"/>
                    <Grid HorizontalAlignment="Left" Height="247" Width="360">
                        <Grid.RowDefinitions>
                            <RowDefinition/><RowDefinition/><RowDefinition/>
                            <RowDefinition/><RowDefinition/>
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition/><ColumnDefinition/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Text="Speed min km/h" Width="96"
                                   HorizontalAlignment="Left" Margin="66,17,0,17"/>
                        <Custom:NumericUpDown x:Name="Speed" ValueChanged="Speed_ValueChanged"
                                             Margin="38,11,38,12" Grid.Column="1"
                                             Minimum="1" Maximum="250"/>

                        <TextBlock Text="Brake max %" Grid.Row="1"
                                   HorizontalAlignment="Left" Margin="66,17,0,17"/>
                        <Custom:NumericUpDown x:Name="Brake" ValueChanged="Brake_ValueChanged"
                                             Grid.Column="1" Grid.Row="1"
                                             Margin="38,11,38,12" Minimum="0" Maximum="100"/>

                        <TextBlock Text="Throttle max %" Grid.Row="2"
                                   HorizontalAlignment="Left" Margin="66,17,0,17"/>
                        <Custom:NumericUpDown x:Name="Throttle" ValueChanged="Throttle_ValueChanged"
                                             Grid.Column="1" Grid.Row="2"
                                             Margin="38,11,38,12" Minimum="0" Maximum="100"/>

                        <TextBlock Text="VelocityX/Speed max" Grid.Row="3"
                                   ToolTip="Lateral speed ratio for preventing detection in turns"
                                   HorizontalAlignment="Left" Margin="66,17,0,17"/>
                        <Custom:NumericUpDown x:Name="Vel" ValueChanged="Vel_ValueChanged"
                                             Grid.Column="1" Grid.Row="3"
                                             Margin="38,11,38,12"
                                             Interval="0.0001" Minimum="0.0001" Maximum="0.0099"/>
                    </Grid>

                    <TextBlock TextWrapping="Wrap" FontWeight="Bold" Text="Plugin Description"/>
                    <TextBlock TextWrapping="Wrap"
                               Text="This plugin calculates longitudinal wheel slip..."/>
                    <TextBlock TextWrapping="Wrap" FontWeight="Bold" Text="Plugin Version: 1.5.2"
                               Margin="0,10,0,0"/>
                </StackPanel>
            </styles:SHSection>
        </Grid>
    </ScrollViewer>
</UserControl>
```

Key things to note from this real example:

- `xmlns:Custom="http://metro.mahapps.com/winfx/xaml/controls"` — MahApps namespace (alias varies between plugins, `Custom`, `mah`, or `Controls` are all common)
- `styles:SHSection` with `IsVisibleChanged` event — used to populate controls with current settings values when the panel is first shown
- `Custom:NumericUpDown` with `ValueChanged` event — code-behind approach rather than data binding, straightforward for simple forms
- `ScrollViewer` as the outermost element

---

## Key Tips

**Always use `ScrollViewer` as the root** — SimHub's settings area has a fixed height. Without it, content below the fold is inaccessible.

**Use `SHSection` for layout grouping** — it matches SimHub's native look and makes your plugin feel integrated rather than bolted on.

**Prefer XAML data binding for settings** — binding to an `INotifyPropertyChanged` settings object means you never have to manually sync UI ↔ model. The viper4gh plugin uses code-behind events (which also work fine), but binding is cleaner for new plugins.

**Dispatch UI updates from `DataUpdate()`** — never modify `ObservableCollection` or `INotifyPropertyChanged` properties from the data thread directly. Use `Dispatcher.BeginInvoke()`.

**Don't use the XAML Designer** — SimHub's plugin UI does not render in Visual Studio's XAML designer. The designer can crash or show errors because the SimHub assemblies aren't in a designer-compatible path. Edit XAML directly in the text editor and verify visually by running SimHub in debug mode.

**Use XAML Hot Reload while debugging** — when SimHub is launched via Visual Studio in debug mode, XAML Hot Reload works and lets you tweak layout in real time without restarting.

**`IWPFSettingsV2` removal** — some community developers have noted that removing `IWPFSettingsV2` from the class declaration and keeping only `IWPFSettings` resolves certain loading errors in recent SimHub versions. If you experience plugin loading issues, try dropping to just `IWPFSettings`.
