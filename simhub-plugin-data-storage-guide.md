# SimHub Plugin Data Storage Guide

## Overview

A SimHub plugin needs two distinct kinds of storage:

- **In-memory (runtime) storage** — fast, frame-safe state for the current session (lap lists, accumulators, current sector splits). Lives in class-level fields. Lost when SimHub closes.
- **Persistent (on-disk) storage** — data that survives restarts (all-time best laps, historical sector times, per-car records). Written to disk in `End()`, read back in `Init()`.

The right approach for lap and sector time data is almost always a combination of both: keep a fast in-memory model during the session, and flush it to disk at the right moment (lap complete, session end, SimHub close).

---

## What SimHub Already Provides

Before building your own storage, it is worth knowing what the built-in `PersistantTrackerPlugin` already tracks, so you do not duplicate work:

| Property | Description |
|---|---|
| `PersistantTrackerPlugin.PreviousLap_00` | Last completed lap time (ms). `_01`, `_02`... up to `_09` |
| `PersistantTrackerPlugin.PreviousLap_00_DeltaToBest` | Delta of that lap to the all-time best |
| `PersistantTrackerPlugin.AllTimeBest` | All-time best lap, stored in `LapRecords.csv` in the SimHub folder |
| `PersistantTrackerPlugin.SessionBestLiveDeltaSeconds` | Live delta to session best |
| `PersistantTrackerPlugin.SessionBest` | Session best lap time |
| `DataCorePlugin.GameData.NewData.LastLapTime` | Raw last lap time from the game |
| `DataCorePlugin.GameData.NewData.CurrentLapTime` | Current lap time (running) |
| `DataCorePlugin.GameData.NewData.Sector1Time` | Last S1 split (where game provides it) |

**Limitations of the built-in tracker:**
- The `PreviousLap_*` list only holds 10 laps and does not reset between sessions automatically
- `AllTimeBest` includes invalid laps (cuts, off-tracks) which may not be useful
- Sector times depend entirely on the game providing them — many don't
- No cross-session history beyond the LapRecords.csv best-lap file
- No structured query capability (you cannot ask "what was my best S1 on this car+track combination last month")

A custom plugin can solve all of these.

---

## Option 1: In-Memory Collections (Session Data)

The simplest approach. Use standard C# collections as class-level fields to accumulate lap and sector data during a session. This is always the foundation — all other storage methods are just ways of persisting this at the right moments.

### Data Model

```csharp
public class SectorTime
{
    public double S1 { get; set; }  // seconds
    public double S2 { get; set; }
    public double S3 { get; set; }
}

public class LapRecord
{
    public int     LapNumber    { get; set; }
    public double  LapTime      { get; set; }  // seconds
    public bool    IsValid      { get; set; }
    public SectorTime Sectors   { get; set; }
    public string  CarModel     { get; set; }
    public string  TrackName    { get; set; }
    public string  GameName     { get; set; }
    public DateTime Timestamp   { get; set; }
}
```

### Plugin Class Fields

```csharp
public class MyPlugin : IPlugin, IDataPlugin, IWPFSettings
{
    // Session-scoped in-memory state
    private List<LapRecord> _sessionLaps    = new List<LapRecord>();
    private LapRecord       _currentLap     = null;
    private int             _lastLapNumber  = -1;
    private double          _sessionBest    = double.MaxValue;
    private double          _allTimeBest    = double.MaxValue;

    // Sector accumulation
    private double _s1Time = 0;
    private double _s2Time = 0;
    private bool   _s1Done = false;
    private bool   _s2Done = false;
}
```

### Lap Detection in DataUpdate()

Lap completion is detected by watching `data.NewData.CompletedLaps` (an incrementing integer) change:

```csharp
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    if (!data.GameRunning || data.NewData == null || data.OldData == null)
        return;

    int currentLapNumber = data.NewData.CompletedLaps;

    // Lap just completed — OldData.CompletedLaps is the lap we just finished
    if (currentLapNumber != _lastLapNumber && _lastLapNumber >= 0)
    {
        double lapTime = data.NewData.LastLapTime;  // seconds

        var record = new LapRecord
        {
            LapNumber  = _lastLapNumber,
            LapTime    = lapTime,
            IsValid    = data.NewData.IsLapValid,   // not all games provide this
            CarModel   = data.NewData.CarModel,
            TrackName  = data.NewData.TrackName,
            GameName   = data.GameName,
            Timestamp  = DateTime.UtcNow,
            Sectors    = new SectorTime
            {
                S1 = _s1Time,
                S2 = _s2Time,
                S3 = lapTime - _s1Time - _s2Time  // inferred S3
            }
        };

        _sessionLaps.Add(record);

        if (lapTime > 0 && lapTime < _sessionBest)
            _sessionBest = lapTime;

        if (lapTime > 0 && lapTime < _allTimeBest)
            _allTimeBest = lapTime;

        // Reset sector accumulators for the new lap
        _s1Time = 0;
        _s2Time = 0;
        _s1Done = false;
        _s2Done = false;

        // Publish to SimHub properties
        pluginManager.SetPropertyValue("MyPlugin.LastLapTime",   this.GetType(), lapTime);
        pluginManager.SetPropertyValue("MyPlugin.SessionBest",   this.GetType(), _sessionBest);
        pluginManager.SetPropertyValue("MyPlugin.SessionLapCount", this.GetType(), _sessionLaps.Count);
    }

    _lastLapNumber = currentLapNumber;
}
```

### Sector Split Detection

Sector splits are messier because not every game provides `Sector1Time` / `Sector2Time`. The cleanest approach is to track when `data.NewData.CurrentSector` changes:

```csharp
// Add to DataUpdate() inside the GameRunning check:
int sector = data.NewData.CurrentSector;  // 0, 1, 2 (game-dependent)
int prevSector = data.OldData.CurrentSector;

if (sector == 1 && prevSector == 0 && !_s1Done)
{
    // Just crossed S1/S2 boundary
    _s1Time = data.NewData.CurrentLapTime;
    _s1Done = true;
    pluginManager.SetPropertyValue("MyPlugin.Sector1Time", this.GetType(), _s1Time);
}
else if (sector == 2 && prevSector == 1 && _s1Done && !_s2Done)
{
    // Just crossed S2/S3 boundary
    _s2Time = data.NewData.CurrentLapTime - _s1Time;
    _s2Done = true;
    pluginManager.SetPropertyValue("MyPlugin.Sector2Time", this.GetType(), _s2Time);
}
```

> **Note:** `CurrentSector` numbering (0-based vs 1-based) and the number of sectors varies by game. Test with the specific games you want to support. Some games (ACC, iRacing) provide explicit sector split times via `Sector1Time` / `Sector2Time` on `GameData` — prefer those where available.

---

## Option 2: JSON File Storage (Recommended for Most Plugins)

JSON is the dominant pattern in the SimHub plugin ecosystem. SimHub already bundles **Newtonsoft.Json**, so there are no extra dependencies to ship.

### When to Use
- All-time bests, per-car/track records, historical session summaries
- Data sets up to a few hundred sessions / a few thousand laps
- When you want human-readable files users can inspect or back up

### Storage Location

Always use `PluginManager.GetCommonStoragePath()` rather than hardcoded paths:

```csharp
string path = PluginManager.GetCommonStoragePath("MyPlugin.lapdata.json");
// Resolves to: SimHub\PluginsData\Common\MyPlugin.lapdata.json
```

### Data Model for Persistence

```csharp
public class SessionSummary
{
    public string   Game       { get; set; }
    public string   Track      { get; set; }
    public string   Car        { get; set; }
    public DateTime Date       { get; set; }
    public List<LapRecord> Laps { get; set; } = new List<LapRecord>();
    public double   BestLap    { get; set; }
    public double   BestS1     { get; set; }
    public double   BestS2     { get; set; }
    public double   BestS3     { get; set; }
}

public class LapDatabase
{
    public List<SessionSummary> Sessions  { get; set; } = new List<SessionSummary>();
    public Dictionary<string, double> AllTimeBests { get; set; } = new Dictionary<string, double>();
    // Key pattern: "Game|Track|Car"  →  best lap time in seconds
}
```

### Loading in Init()

```csharp
private LapDatabase _db;
private string _dbPath;

public void Init(PluginManager pluginManager)
{
    _dbPath = PluginManager.GetCommonStoragePath("MyPlugin.lapdata.json");

    try
    {
        string json = File.ReadAllText(_dbPath);
        _db = JsonConvert.DeserializeObject<LapDatabase>(json) ?? new LapDatabase();
        Logging.Current.Info($"MyPlugin - Loaded {_db.Sessions.Count} sessions from disk.");
    }
    catch (FileNotFoundException)
    {
        _db = new LapDatabase();
        Logging.Current.Info("MyPlugin - No existing data file, starting fresh.");
    }
    catch (Exception ex)
    {
        _db = new LapDatabase();
        Logging.Current.Error($"MyPlugin - Failed to load data: {ex.Message}");
    }

    // Expose all-time best as a property
    pluginManager.AddProperty("MyPlugin.AllTimeBest",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.SessionBest",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.LastLapTime",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.Sector1Time",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.Sector2Time",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.Sector3Time",    this.GetType(), 0.0);
    pluginManager.AddProperty("MyPlugin.SessionLapCount", this.GetType(), 0);
}
```

### Saving a Lap (on lap completion)

Called inside `DataUpdate()` when a new lap is detected:

```csharp
private void OnLapCompleted(PluginManager pluginManager, LapRecord lap)
{
    // Add to in-memory session list
    _sessionLaps.Add(lap);

    // Update the all-time best dictionary
    string key = $"{lap.GameName}|{lap.TrackName}|{lap.CarModel}";
    if (lap.IsValid && lap.LapTime > 0)
    {
        if (!_db.AllTimeBests.ContainsKey(key) || lap.LapTime < _db.AllTimeBests[key])
        {
            _db.AllTimeBests[key] = lap.LapTime;
            pluginManager.SetPropertyValue("MyPlugin.AllTimeBest", this.GetType(), lap.LapTime);
        }
    }

    // Note: do NOT write to disk here — do it in End() or on session change
    // Writing JSON on every lap completion at 60Hz = disk thrash
}
```

### Saving in End()

```csharp
public void End(PluginManager pluginManager)
{
    // Flush the current session into the database
    if (_sessionLaps.Count > 0)
    {
        var summary = new SessionSummary
        {
            Game  = _currentGame,
            Track = _currentTrack,
            Car   = _currentCar,
            Date  = DateTime.UtcNow,
            Laps  = new List<LapRecord>(_sessionLaps),
            BestLap = _sessionLaps.Where(l => l.IsValid && l.LapTime > 0)
                                   .Select(l => l.LapTime)
                                   .DefaultIfEmpty(0)
                                   .Min(),
            BestS1 = _sessionLaps.Select(l => l.Sectors.S1).Where(s => s > 0).DefaultIfEmpty(0).Min(),
            BestS2 = _sessionLaps.Select(l => l.Sectors.S2).Where(s => s > 0).DefaultIfEmpty(0).Min(),
            BestS3 = _sessionLaps.Select(l => l.Sectors.S3).Where(s => s > 0).DefaultIfEmpty(0).Min(),
        };
        _db.Sessions.Add(summary);
    }

    // Write to disk
    try
    {
        string json = JsonConvert.SerializeObject(_db, Formatting.Indented);
        File.WriteAllText(_dbPath, json, Encoding.UTF8);
        Logging.Current.Info($"MyPlugin - Saved {_db.Sessions.Count} sessions to {_dbPath}");
    }
    catch (Exception ex)
    {
        Logging.Current.Error($"MyPlugin - Failed to save data: {ex.Message}");
    }
}
```

### Loading the All-Time Best for Current Car/Track

```csharp
// Call this when car or track changes
private void UpdateAllTimeBestProperty(PluginManager pluginManager, string game, string track, string car)
{
    string key = $"{game}|{track}|{car}";
    double best = _db.AllTimeBests.ContainsKey(key) ? _db.AllTimeBests[key] : 0.0;
    pluginManager.SetPropertyValue("MyPlugin.AllTimeBest", this.GetType(), best);
}
```

### JSON File Size Considerations

| Sessions | Laps each | Approx file size |
|---|---|---|
| 100 | 20 | ~500 KB |
| 1000 | 20 | ~5 MB |
| 5000 | 20 | ~25 MB |

JSON starts to slow down at tens of thousands of records. Consider pruning old sessions or switching to SQLite at that scale.

---

## Option 3: CSV File Storage (Simplest Persistent Format)

CSV is the format used by SimHub's own `LapRecords.csv` and by the Final Drive Lap Board plugin. Best for simple tabular data you also want to open in Excel.

### Writing a Lap CSV

```csharp
private string _csvPath;

public void Init(PluginManager pluginManager)
{
    _csvPath = PluginManager.GetCommonStoragePath("MyPlugin.laps.csv");

    // Write header if the file does not exist
    if (!File.Exists(_csvPath))
    {
        File.WriteAllText(_csvPath,
            "Timestamp,Game,Track,Car,Lap,LapTime,Sector1,Sector2,Sector3,Valid\n",
            Encoding.UTF8);
    }
}

private void AppendLapToCSV(LapRecord lap)
{
    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5:F3},{6:F3},{7:F3},{8:F3},{9}\n",
        lap.Timestamp,
        lap.GameName,
        lap.TrackName,
        lap.CarModel,
        lap.LapNumber,
        lap.LapTime,
        lap.Sectors.S1,
        lap.Sectors.S2,
        lap.Sectors.S3,
        lap.IsValid ? 1 : 0);

    File.AppendAllText(_csvPath, line, Encoding.UTF8);
}
```

**Pros:** Human-readable, opens directly in Excel, append-only so no read-modify-write cycle needed.

**Cons:** Querying is painful in code (no indexed lookup). Reading back the best lap for a specific car+track requires scanning the whole file.

---

## Option 4: SQLite Database (Best for Large or Queryable Data)

SQLite is a single-file embedded database, ideal when you need:
- Hundreds of thousands of laps
- Fast indexed queries ("best S1 on this car+track", "all laps under 1:45")
- Cross-session analytics

### NuGet Package

For .NET Framework 4.8 (which SimHub requires), use:

```xml
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
```

> **Deployment note:** `System.Data.SQLite.Core` ships a native `SQLite.Interop.dll`. You must copy it into the SimHub folder alongside your plugin DLL. Set `Copy Local = True` in the reference and ensure it is in the build output.

### Schema

```csharp
private const string CREATE_TABLES = @"
CREATE TABLE IF NOT EXISTS sessions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    game        TEXT NOT NULL,
    track       TEXT NOT NULL,
    car         TEXT NOT NULL,
    date        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS laps (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  INTEGER NOT NULL REFERENCES sessions(id),
    lap_number  INTEGER NOT NULL,
    lap_time    REAL,      -- seconds
    sector1     REAL,
    sector2     REAL,
    sector3     REAL,
    is_valid    INTEGER,   -- 1 = valid, 0 = invalid
    timestamp   TEXT
);

CREATE INDEX IF NOT EXISTS idx_laps_session ON laps(session_id);
CREATE INDEX IF NOT EXISTS idx_laps_track_car ON laps(session_id, lap_time);
";
```

### Initialization

```csharp
using System.Data.SQLite;

private SQLiteConnection _connection;
private long _currentSessionId = -1;
private string _dbFilePath;

public void Init(PluginManager pluginManager)
{
    _dbFilePath = PluginManager.GetCommonStoragePath("MyPlugin.db");
    string connStr = $"Data Source={_dbFilePath};Version=3;";

    _connection = new SQLiteConnection(connStr);
    _connection.Open();

    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = CREATE_TABLES;
        cmd.ExecuteNonQuery();
    }

    Logging.Current.Info($"MyPlugin - SQLite database opened at {_dbFilePath}");
}
```

### Starting a Session

```csharp
private void StartSession(string game, string track, string car)
{
    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = @"
            INSERT INTO sessions (game, track, car, date)
            VALUES (@game, @track, @car, @date);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@game",  game);
        cmd.Parameters.AddWithValue("@track", track);
        cmd.Parameters.AddWithValue("@car",   car);
        cmd.Parameters.AddWithValue("@date",  DateTime.UtcNow.ToString("o"));
        _currentSessionId = (long)cmd.ExecuteScalar();
    }
}
```

### Inserting a Lap

```csharp
private void SaveLap(LapRecord lap)
{
    if (_currentSessionId < 0) return;

    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = @"
            INSERT INTO laps (session_id, lap_number, lap_time, sector1, sector2, sector3, is_valid, timestamp)
            VALUES (@sid, @lap, @time, @s1, @s2, @s3, @valid, @ts)";
        cmd.Parameters.AddWithValue("@sid",   _currentSessionId);
        cmd.Parameters.AddWithValue("@lap",   lap.LapNumber);
        cmd.Parameters.AddWithValue("@time",  lap.LapTime);
        cmd.Parameters.AddWithValue("@s1",    lap.Sectors.S1);
        cmd.Parameters.AddWithValue("@s2",    lap.Sectors.S2);
        cmd.Parameters.AddWithValue("@s3",    lap.Sectors.S3);
        cmd.Parameters.AddWithValue("@valid", lap.IsValid ? 1 : 0);
        cmd.Parameters.AddWithValue("@ts",    lap.Timestamp.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}
```

> **Performance:** Insert inside `DataUpdate()` is fine for individual laps (one insert per lap, not per frame). If you need to insert high-frequency data (e.g., every frame), batch them and commit in `End()` using a `SQLiteTransaction`.

### Querying All-Time Best

```csharp
private double GetAllTimeBest(string game, string track, string car)
{
    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT MIN(l.lap_time)
            FROM laps l
            JOIN sessions s ON l.session_id = s.id
            WHERE s.game = @game AND s.track = @track AND s.car = @car
              AND l.is_valid = 1 AND l.lap_time > 0";
        cmd.Parameters.AddWithValue("@game",  game);
        cmd.Parameters.AddWithValue("@track", track);
        cmd.Parameters.AddWithValue("@car",   car);

        object result = cmd.ExecuteScalar();
        return result is DBNull || result == null ? 0.0 : Convert.ToDouble(result);
    }
}

// Theoretical best using best individual sectors
private (double S1, double S2, double S3) GetBestSectors(string game, string track, string car)
{
    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT MIN(l.sector1), MIN(l.sector2), MIN(l.sector3)
            FROM laps l
            JOIN sessions s ON l.session_id = s.id
            WHERE s.game = @game AND s.track = @track AND s.car = @car
              AND l.is_valid = 1";
        cmd.Parameters.AddWithValue("@game",  game);
        cmd.Parameters.AddWithValue("@track", track);
        cmd.Parameters.AddWithValue("@car",   car);

        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
                return (reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2));
        }
    }
    return (0, 0, 0);
}
```

### Closing in End()

```csharp
public void End(PluginManager pluginManager)
{
    try
    {
        _connection?.Close();
        _connection?.Dispose();
        Logging.Current.Info("MyPlugin - SQLite connection closed.");
    }
    catch (Exception ex)
    {
        Logging.Current.Error($"MyPlugin - Error closing database: {ex.Message}");
    }
}
```

---

## Option 5: Background Writer Thread (Async Safety)

If you write to disk or SQLite frequently (e.g., every lap, every session change), doing it on the `DataUpdate()` thread risks stalling at 60Hz. A background queue offloads that work safely.

```csharp
using System.Collections.Concurrent;
using System.Threading;

private ConcurrentQueue<LapRecord> _writeQueue = new ConcurrentQueue<LapRecord>();
private Thread _writerThread;
private volatile bool _running = true;

public void Init(PluginManager pluginManager)
{
    // ... open DB or JSON file as normal ...

    _writerThread = new Thread(WriterLoop)
    {
        IsBackground = true,
        Name = "MyPlugin-Writer"
    };
    _writerThread.Start();
}

private void WriterLoop()
{
    while (_running)
    {
        while (_writeQueue.TryDequeue(out LapRecord lap))
        {
            try { SaveLap(lap); }
            catch (Exception ex)
            {
                Logging.Current.Error($"MyPlugin - Write error: {ex.Message}");
            }
        }
        Thread.Sleep(100); // poll every 100ms
    }

    // Drain remaining on shutdown
    while (_writeQueue.TryDequeue(out LapRecord lap))
    {
        try { SaveLap(lap); } catch { }
    }
}

// In DataUpdate(), enqueue instead of writing directly:
private void OnLapCompleted(LapRecord lap)
{
    _sessionLaps.Add(lap);
    _writeQueue.Enqueue(lap);  // non-blocking
}

public void End(PluginManager pluginManager)
{
    _running = false;
    _writerThread?.Join(3000); // wait up to 3s for drain
    _connection?.Close();
}
```

---

## Comparison Summary

| Approach | Complexity | Query capability | File size | Best for |
|---|---|---|---|---|
| In-memory only | Very low | C# LINQ | None | Session-only data |
| JSON file | Low | LINQ after load | Small–medium | All-time bests, session history |
| CSV file | Very low | Scan only | Small–medium | Export / Excel viewing |
| SQLite | Medium | Full SQL | Any size | Historical analysis, large datasets |
| Background writer | Add-on | Any | Any | High-frequency or async writes |

---

## Recommended Architecture for Lap/Sector Time Storage

For a plugin that tracks lap and sector times across sessions, the recommended approach is:

1. **In-memory `List<LapRecord>`** for the current session — zero overhead in `DataUpdate()`
2. **JSON file** for the all-time best dictionary (keyed by `"Game|Track|Car"`) — fast lookup on session start, written in `End()`
3. **SQLite** if you want full historical session data with queryable sectors — optional but powerful

The key timing rules are:
- **Read** from disk in `Init()` only
- **Write** from disk in `End()` only, or via a background thread
- **Never** do file I/O inside `DataUpdate()`

---

## Detecting Session Start and Car/Track Changes

You need to reset your in-memory state and look up new all-time bests whenever the game, car, or track changes:

```csharp
private string _lastGame  = string.Empty;
private string _lastTrack = string.Empty;
private string _lastCar   = string.Empty;

public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    if (!data.GameRunning || data.NewData == null || data.OldData == null)
        return;

    string game  = data.GameName;
    string track = data.NewData.TrackName;
    string car   = data.NewData.CarModel;

    bool contextChanged = game  != _lastGame  ||
                          track != _lastTrack ||
                          car   != _lastCar;

    if (contextChanged)
    {
        OnContextChanged(pluginManager, game, track, car);
        _lastGame  = game;
        _lastTrack = track;
        _lastCar   = car;
    }

    // ... rest of DataUpdate lap detection ...
}

private void OnContextChanged(PluginManager pluginManager, string game, string track, string car)
{
    // Save previous session if there were laps
    if (_sessionLaps.Count > 0)
        FlushSession();

    // Reset session state
    _sessionLaps.Clear();
    _sessionBest   = double.MaxValue;
    _lastLapNumber = -1;

    // Look up all-time best for the new context
    double atBest = GetAllTimeBest(game, track, car);
    pluginManager.SetPropertyValue("MyPlugin.AllTimeBest", this.GetType(), atBest);

    // Start new SQLite session if using that approach
    StartSession(game, track, car);

    Logging.Current.Info($"MyPlugin - Context: {game} / {track} / {car}");
}
```
