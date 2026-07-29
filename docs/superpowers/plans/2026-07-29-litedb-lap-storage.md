# LiteDB Lap Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace StatsPlus runtime lap-history persistence with LiteDB and add a one-time SQLite-to-LiteDB migration utility.

**Architecture:** The plugin uses `StatsPlusLiteDbRepository` against `StatsPlus.laps.ldb`. LiteDB stores one `trackHistories` document per game/car/track variation and one `laps` document per recorded lap. SQLite exists only in `StatsPlus.Migration`, which converts current normalized SQLite data into the LiteDB document structure.

**Tech Stack:** C# `net48`, MSTest, LiteDB `5.0.21`, System.Data.SQLite.Core `1.0.119` only in the migration project and migration-related tests, SimHub SDK stubs for test builds.

## Global Constraints

- `StatsPlus.settings.json` remains JSON because it stores plugin settings, not lap history.
- The plugin will not read, write, import, or fall back to `StatsPlus.laps.json`.
- Existing SQLite lap data will be moved once by a separate console migration utility, not by plugin startup code.
- Runtime lap-history persistence uses `PluginsData\StatsPlus\StatsPlus.laps.ldb`.
- The main plugin project no longer references `System.Data.SQLite.Core`.
- The main plugin project references `LiteDB`.
- SQLite package references are allowed in the migration project only.
- `trackHistories.CreatedUtc` is set once and is not changed by later sessions.
- `trackHistories.LastUpdatedUtc` advances to the newest `laps.TimestampUtc`.
- `laps.TimestampUtc` is the source of truth for when each recorded lap occurred.

---

## File Structure

- Modify `StatsPlus/StatsPlus.csproj`: replace `System.Data.SQLite.Core` with `LiteDB`.
- Create `StatsPlus/LiteDbLapStoreDocuments.cs`: shared LiteDB document classes for `trackHistories` and `laps`.
- Create `StatsPlus/StatsPlusLiteDbRepository.cs`: runtime LiteDB repository.
- Delete `StatsPlus/StatsPlusSqliteRepository.cs`: remove SQLite runtime repository.
- Modify `StatsPlus/StatsPlusPlugin.cs`: point runtime storage at LiteDB, remove JSON lap-history fallback, and call `StatsPlusLiteDbRepository`.
- Modify `StatsPlus/SettingsControl.xaml`: replace user-facing "SQLite" text with "LiteDB".
- Modify `StatsPlus.Tests/StatsPlus.Tests.csproj`: replace direct SQLite package with `LiteDB` during repository work, then add SQLite back when migration tests need direct SQLite setup.
- Delete `StatsPlus.Tests/StatsPlusSqliteRepositoryTests.cs`.
- Create `StatsPlus.Tests/StatsPlusLiteDbRepositoryTests.cs`: behavior tests for the LiteDB repository.
- Modify `StatsPlus.Tests/StatsPlusPluginStorageTests.cs`: update file-extension expectations and remove JSON lap-history path tests.
- Create `StatsPlus.Migration/StatsPlus.Migration.csproj`: console migration project referencing `StatsPlus`, `LiteDB`, and `System.Data.SQLite.Core`.
- Create `StatsPlus.Migration/Program.cs`: command-line entry point.
- Create `StatsPlus.Migration/SqliteToLiteDbMigrator.cs`: migration logic.
- Create `StatsPlus.Tests/StatsPlusMigrationTests.cs`: tests covering SQLite-to-LiteDB conversion.
- Modify `StatsPlus.sln`: include `StatsPlus.Migration`.

---

### Task 1: Add LiteDB Document Types and Repository Tests

**Files:**
- Create: `StatsPlus/LiteDbLapStoreDocuments.cs`
- Create: `StatsPlus.Tests/StatsPlusLiteDbRepositoryTests.cs`
- Modify: `StatsPlus/StatsPlus.csproj`
- Modify: `StatsPlus.Tests/StatsPlus.Tests.csproj`

**Interfaces:**
- Produces: `public sealed class TrackHistoryDocument` with the fields in the design spec.
- Produces: `public sealed class LapDocument` with the fields in the design spec.
- Produces test expectations for `public sealed class StatsPlusLiteDbRepository : IDisposable`.
- Consumes: existing `RecordedLap`, `StoredTrackSummary`, `RecordedLapView`, and `StatsPlusPropertyNames`.

- [ ] **Step 1: Update package references for the failing tests**

In `StatsPlus/StatsPlus.csproj`, replace:

```xml
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
```

with:

```xml
<PackageReference Include="LiteDB" Version="5.0.21" />
```

In `StatsPlus.Tests/StatsPlus.Tests.csproj`, replace:

```xml
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
```

with:

```xml
<PackageReference Include="LiteDB" Version="5.0.21" />
```

- [ ] **Step 2: Add document classes used by tests and implementation**

Create `StatsPlus/LiteDbLapStoreDocuments.cs`:

```csharp
using System;

namespace StatsPlus
{
    public sealed class TrackHistoryDocument
    {
        public long Id { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string NormalizedGameName { get; set; } = string.Empty;
        public string CarModel { get; set; } = string.Empty;
        public string NormalizedModelName { get; set; } = string.Empty;
        public string DisplayModelName { get; set; } = string.Empty;
        public string RawTrackName { get; set; } = string.Empty;
        public string TrackNameWithConfig { get; set; } = string.Empty;
        public string NormalizedTrackNameWithConfig { get; set; } = string.Empty;
        public string DisplayTrackNameWithConfig { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }

    public sealed class LapDocument
    {
        public long Id { get; set; }
        public long TrackHistoryId { get; set; }
        public int LapNumber { get; set; }
        public double LapTimeSeconds { get; set; }
        public double Sector1Seconds { get; set; }
        public double Sector2Seconds { get; set; }
        public double Sector3Seconds { get; set; }
        public bool IsValid { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
```

- [ ] **Step 3: Replace repository tests with LiteDB behavior tests**

Create `StatsPlus.Tests/StatsPlusLiteDbRepositoryTests.cs` with these tests:

```csharp
using System;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusLiteDbRepositoryTests
    {
        private string _tempDirectory;
        private string _databasePath;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusLiteDbTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _databasePath = Path.Combine(_tempDirectory, "StatsPlus.laps.ldb");
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void Initialize_CreatesEmptyStore()
        {
            using (var repository = CreateRepository())
            {
                Assert.IsFalse(repository.HasLapData());
                Assert.AreEqual(0, repository.GetTrackSummaries().Count);
            }
        }

        [TestMethod]
        public void AddLap_UsesCaseInsensitiveIdentity_AndPreservesCreatedUtc()
        {
            var firstTimestamp = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc);
            var newerTimestamp = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc);

            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap
                {
                    LapNumber = 1,
                    LapTimeSeconds = 122.0,
                    Sector1Seconds = 40.0,
                    Sector2Seconds = 41.0,
                    Sector3Seconds = 41.0,
                    IsValid = true,
                    TimestampUtc = firstTimestamp
                });

                repository.AddLap("assettocorsa", "bmw m3 gt2", "spa", "SPA-GP", new RecordedLap
                {
                    LapNumber = 2,
                    LapTimeSeconds = 119.0,
                    Sector1Seconds = 39.0,
                    Sector2Seconds = 40.0,
                    Sector3Seconds = 40.0,
                    IsValid = true,
                    TimestampUtc = newerTimestamp
                });
            }

            using (var database = new LiteDatabase(_databasePath))
            {
                var histories = database.GetCollection<TrackHistoryDocument>("trackHistories").FindAll().ToList();
                Assert.AreEqual(1, histories.Count);
                Assert.AreEqual(firstTimestamp, histories[0].CreatedUtc);
                Assert.AreEqual(newerTimestamp, histories[0].LastUpdatedUtc);
            }
        }

        [TestMethod]
        public void AddLap_IgnoresOlderLapWhenUpdatingLastUpdatedUtc()
        {
            var newerTimestamp = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc);
            var olderTimestamp = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc);

            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.0, IsValid = true, TimestampUtc = newerTimestamp });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = olderTimestamp });
            }

            using (var database = new LiteDatabase(_databasePath))
            {
                var history = database.GetCollection<TrackHistoryDocument>("trackHistories").FindAll().Single();
                Assert.AreEqual(newerTimestamp, history.LastUpdatedUtc);
            }
        }

        [TestMethod]
        public void ToggleLapValidity_RecalculatesBestLap()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc) });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 2, 0, DateTimeKind.Utc) });

                var laps = repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp");
                repository.ToggleLapValidity(laps[0].LapId);

                Assert.AreEqual(122.0, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "SPA-GP"), 0.0001);
                Assert.AreEqual(122.0, repository.GetTrackSummaries().Single().BestLapSeconds, 0.0001);
                Assert.IsFalse(repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp").First().IsValid);
            }
        }

        [TestMethod]
        public void DeleteGameData_AndClearAllData_RemoveExpectedDocuments()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc) });
                repository.AddLap("Automobilista2", "BMW M4 GT4", "Watkins Glen", "Watkins_Glen-Watkins_Glen_GPIL", new RecordedLap { LapNumber = 1, LapTimeSeconds = 118.5, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc) });

                repository.DeleteGameData("assetto corsa");

                var remainingSummaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, remainingSummaries.Count);
                Assert.AreEqual("Automobilista2", remainingSummaries[0].GameName);
                Assert.AreEqual(0.0, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "spa-gp"), 0.0001);

                repository.ClearAllData();

                Assert.IsFalse(repository.HasLapData());
                Assert.AreEqual(0, repository.GetTrackSummaries().Count);
            }
        }

        [TestMethod]
        public void Queries_ReturnSummariesLapsAndPersonalBests()
        {
            var newestLapTime = new DateTime(2026, 5, 25, 1, 4, 0, DateTimeKind.Utc);
            var olderLapTime = new DateTime(2026, 5, 25, 1, 2, 0, DateTimeKind.Utc);

            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 121.5, Sector1Seconds = 40.1, Sector2Seconds = 40.5, Sector3Seconds = 40.9, IsValid = true, TimestampUtc = olderLapTime });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.25, Sector1Seconds = 39.7, Sector2Seconds = 39.8, Sector3Seconds = 39.75, IsValid = false, TimestampUtc = newestLapTime });

                var summaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, summaries.Count);
                Assert.AreEqual("AssettoCorsa", summaries[0].GameName);
                Assert.AreEqual("BMW M3 GT2", summaries[0].CarModel);
                Assert.AreEqual("spa-gp", summaries[0].TrackNameWithConfig);
                Assert.AreEqual(2, summaries[0].LapCount);
                Assert.AreEqual(121.5, summaries[0].BestLapSeconds, 0.0001);
                Assert.AreEqual(newestLapTime, summaries[0].LastRecordedUtc);

                var laps = repository.GetTrackLaps("assettocorsa", "bmw m3 gt2", "SPA-GP");
                Assert.AreEqual(2, laps.Count);
                Assert.AreEqual(2, laps[0].LapNumber);
                Assert.AreEqual(newestLapTime, laps[0].TimestampUtc);
                Assert.AreEqual(1, laps[1].LapNumber);

                var personalBests = repository.GetPersonalBestPropertyValues();
                Assert.AreEqual(1, personalBests.Count);
                Assert.AreEqual(121.5, personalBests["StatsPlus.PersonalBest.AssettoCorsa.BMW_M3_GT2.spa_gp"], 0.0001);
            }
        }

        private StatsPlusLiteDbRepository CreateRepository()
        {
            var repository = new StatsPlusLiteDbRepository(_databasePath);
            repository.Initialize();
            return repository;
        }
    }
}
```

Delete `StatsPlus.Tests/StatsPlusSqliteRepositoryTests.cs`.

- [ ] **Step 4: Run tests to verify they fail for the expected reason**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: build fails because `StatsPlusLiteDbRepository` does not exist yet. If package restore fails because the sandbox blocks NuGet access, rerun the same command with escalation.

- [ ] **Step 5: Keep the red repository tests for Task 2**

Do not commit this red state. Task 2 implements the repository and commits the tests, package changes, document classes, and repository together after `StatsPlusLiteDbRepositoryTests` pass.

---

### Task 2: Implement the LiteDB Repository

**Files:**
- Create: `StatsPlus/StatsPlusLiteDbRepository.cs`
- Delete: `StatsPlus/StatsPlusSqliteRepository.cs`
- Test: `StatsPlus.Tests/StatsPlusLiteDbRepositoryTests.cs`

**Interfaces:**
- Consumes: `TrackHistoryDocument`, `LapDocument`, `RecordedLap`, `StoredTrackSummary`, `RecordedLapView`.
- Produces: `public sealed class StatsPlusLiteDbRepository : IDisposable` with the repository API listed in the spec.

- [ ] **Step 1: Implement the repository skeleton and LiteDB initialization**

Create `StatsPlus/StatsPlusLiteDbRepository.cs` with constructor, `Initialize()`, collection properties, index creation, `Dispose()`, and normalization helpers. Use these collection names:

```csharp
private const string TrackHistoriesCollectionName = "trackHistories";
private const string LapsCollectionName = "laps";
```

Initialize indexes for `TrackHistoryDocument.NormalizedGameName`, `NormalizedModelName`, `NormalizedTrackNameWithConfig`, and `LapDocument.TrackHistoryId`, `TimestampUtc`, `IsValid`, `LapTimeSeconds`.

- [ ] **Step 2: Implement create and lookup behavior**

Implement:

```csharp
public bool HasLapData()
public void AddLap(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)
private TrackHistoryDocument GetOrCreateTrackHistory(string gameName, string carModel, string trackName, string trackNameWithConfig, DateTime timestampUtc)
```

Behavior:

- Normalize game names by keeping only letters and digits, lowercased.
- Normalize car and track identity values with `Trim().ToUpperInvariant()`.
- Query `trackHistories` by all three normalized fields before inserting.
- On first insert, set `CreatedUtc` and `LastUpdatedUtc` to the lap timestamp.
- On later inserts, leave `CreatedUtc` unchanged and update `LastUpdatedUtc` only when `lap.TimestampUtc` is newer.
- Insert one `LapDocument` per call.

- [ ] **Step 3: Run repository tests and verify the creation tests pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusLiteDbRepositoryTests
```

Expected: creation and timestamp tests pass; query/toggle/delete tests may still fail until later steps in this task are complete.

- [ ] **Step 4: Implement query and mutation methods**

Implement:

```csharp
public void ToggleLapValidity(long lapId)
public void DeleteGameData(string gameName)
public void ClearAllData()
public double GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)
public List<StoredTrackSummary> GetTrackSummaries()
public List<RecordedLapView> GetTrackLaps(string gameName, string carModel, string trackNameWithConfig)
public Dictionary<string, double> GetPersonalBestPropertyValues()
public string TryGetCarDisplayName(string gameName, string carModel)
```

Use `trackHistories` for identity/display fields and `laps` for lap details. `DeleteGameData` must remove matching track histories and all laps whose `TrackHistoryId` matches those histories. `GetTrackLaps` must return descending `TimestampUtc`.

- [ ] **Step 5: Run repository tests to verify green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusLiteDbRepositoryTests
```

Expected: all `StatsPlusLiteDbRepositoryTests` pass.

- [ ] **Step 6: Remove SQLite repository**

Delete `StatsPlus/StatsPlusSqliteRepository.cs`.

- [ ] **Step 7: Commit repository implementation**

```powershell
git add -- StatsPlus/StatsPlusLiteDbRepository.cs StatsPlus/StatsPlusSqliteRepository.cs
git add -- StatsPlus/StatsPlus.csproj StatsPlus.Tests/StatsPlus.Tests.csproj StatsPlus/LiteDbLapStoreDocuments.cs StatsPlus.Tests/StatsPlusLiteDbRepositoryTests.cs StatsPlus.Tests/StatsPlusSqliteRepositoryTests.cs
git commit -m "Implement LiteDB lap repository"
```

---

### Task 3: Wire LiteDB Into the Plugin and Remove JSON Lap-History Runtime Paths

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Modify: `StatsPlus/SettingsControl.xaml`
- Modify: `StatsPlus.Tests/StatsPlusPluginStorageTests.cs`
- Test: `StatsPlus.Tests/StatsPlusPluginStorageTests.cs`

**Interfaces:**
- Consumes: `StatsPlusLiteDbRepository`.
- Produces: plugin runtime storage path `StatsPlus.laps.ldb`.
- Removes: runtime calls to `LoadDatabase()`, `SaveDatabase()`, `ResolveLegacyDataPath()`, and `StatsPlus.laps.json`.

- [ ] **Step 1: Update plugin storage tests first**

Modify `StatsPlus.Tests/StatsPlusPluginStorageTests.cs`:

- Change database file expectations from `StatsPlus.laps.db` to `StatsPlus.laps.ldb`.
- Rename test method names containing `Legacy` where they now mean storage-file relocation.
- Delete `ResolveLegacyDataPath_PrefersStatsPlusFolderThenCommonSubfolderThenCommonRoot`.
- Keep `GetStatsPlusStorageRoot_ReturnsPluginsDataStatsPlusSiblingOfCommon`, `MigrateFileIfNeeded_MovesLegacyCommonRootFileWhenTargetMissing`, `MigrateFileIfNeeded_LeavesLegacyFileWhenTargetAlreadyExists`, and `BackupFileIfPresent_CreatesAndOverwritesRollingBackup`, with `.ldb` paths.

- [ ] **Step 2: Run storage tests to verify they fail against current plugin code**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginStorageTests
```

Expected: failures or build errors reference `StatsPlus.laps.db`, `ResolveLegacyDataPath`, or SQLite-specific runtime code.

- [ ] **Step 3: Rename plugin fields and constants**

In `StatsPlus/StatsPlusPlugin.cs`:

- Replace `private const string LegacyDataFileName = "StatsPlus.laps.json";` by removing the constant.
- Replace `private const string SqliteDataFileName = "StatsPlus.laps.db";` with `private const string LiteDbDataFileName = "StatsPlus.laps.ldb";`.
- Remove `_legacyDatabasePath`.
- Remove `_database`.
- Replace `_sqliteRepository` with `_liteDbRepository`.
- Replace `UseSqlite` with `HasLapRepository => _liteDbRepository != null`.

- [ ] **Step 4: Replace database initialization and shutdown**

Rewrite `InitializeDatabase()` so it only creates `StatsPlusLiteDbRepository`, calls `Initialize()`, logs failures, disposes partial state, and leaves `_liteDbRepository = null` on failure.

Rewrite `End()` to dispose `_liteDbRepository`, set it to `null`, back up the LiteDB file, save settings, and log shutdown.

- [ ] **Step 5: Remove JSON lap-history methods and branches**

Remove these methods from `StatsPlus/StatsPlusPlugin.cs`:

```csharp
private LapDatabase LoadDatabase()
private void SaveDatabase()
private void NormalizeLoadedDatabase(LapDatabase database)
internal static string ResolveLegacyDataPath(string statsPlusStorageRoot, string commonStorageRoot)
```

Remove JSON branches from:

```csharp
ToggleSelectedLapValidity()
ClearSelectedGameData()
ClearAllData()
AddLapToDatabase(...)
GetBestLapSeconds(...)
BuildTrackSummaries()
BuildPersonalBestPropertyValues()
TryGetTrackBucket(...)
LoadSelectedTrackLaps(...)
```

Use safe no-op behavior when `HasLapRepository` is false.

- [ ] **Step 6: Update storage path setup**

In `InitializeStoragePaths(PluginManager pluginManager)`, set:

```csharp
_databasePath = Path.Combine(statsPlusStorageRoot, LiteDbDataFileName);
```

Keep settings relocation behavior. For lap-history relocation, only move previous `StatsPlus.laps.ldb` files from common locations into `PluginsData\StatsPlus`; do not move `.db` or `.json` files.

- [ ] **Step 7: Update UI copy**

In `StatsPlus/SettingsControl.xaml`, replace visible "SQLite" references with "LiteDB" or "local lap history store".

- [ ] **Step 8: Run plugin storage tests and full test project**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginStorageTests
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: all tests pass except migration tests not yet added.

- [ ] **Step 9: Commit plugin integration**

```powershell
git add -- StatsPlus/StatsPlusPlugin.cs StatsPlus/SettingsControl.xaml StatsPlus.Tests/StatsPlusPluginStorageTests.cs
git commit -m "Use LiteDB for plugin lap storage"
```

---

### Task 4: Add the SQLite-to-LiteDB Migration Utility

**Files:**
- Create: `StatsPlus.Migration/StatsPlus.Migration.csproj`
- Create: `StatsPlus.Migration/Program.cs`
- Create: `StatsPlus.Migration/SqliteToLiteDbMigrator.cs`
- Create: `StatsPlus.Tests/StatsPlusMigrationTests.cs`
- Modify: `StatsPlus.sln`
- Modify: `StatsPlus.Tests/StatsPlus.Tests.csproj`

**Interfaces:**
- Produces: `public sealed class SqliteToLiteDbMigrator`.
- Produces: `public MigrationResult Migrate(string sourcePath, string targetPath, bool overwrite)`.
- Produces: `public sealed class MigrationResult` with `TrackHistoryCount` and `LapCount`.
- Consumes: current SQLite table schema formerly implemented by `StatsPlusSqliteRepository` and LiteDB document classes from `StatsPlus`.

- [ ] **Step 1: Add migration project**

Create `StatsPlus.Migration/StatsPlus.Migration.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LiteDB" Version="5.0.21" />
    <PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\StatsPlus\StatsPlus.csproj" />
  </ItemGroup>
</Project>
```

Add it to the solution:

```powershell
dotnet sln StatsPlus.sln add StatsPlus.Migration\StatsPlus.Migration.csproj
```

- [ ] **Step 2: Add failing migration tests**

Create `StatsPlus.Tests/StatsPlusMigrationTests.cs`. The test should:

- Create a temporary SQLite database with the current `games`, `cars`, `tracks`, `track_contexts`, and `laps` schema.
- Insert two track contexts and three laps.
- Run `new SqliteToLiteDbMigrator().Migrate(sourcePath, targetPath, overwrite: false)`.
- Open the target through `StatsPlusLiteDbRepository`.
- Assert two summaries, three laps total, preserved validity, preserved timestamps, and expected personal-best values.
- Assert a second migration to the same target with `overwrite: false` throws `IOException`.

Use helper methods in the test file to create the SQLite schema and insert rows; do not depend on deleted runtime SQLite repository code.

- [ ] **Step 3: Reference the migration project from tests**

In `StatsPlus.Tests/StatsPlus.Tests.csproj`, add:

```xml
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.119" />
<ProjectReference Include="..\StatsPlus.Migration\StatsPlus.Migration.csproj" />
```

The test project needs `System.Data.SQLite.Core` because `StatsPlusMigrationTests` creates the SQLite source database directly. Confirm the main plugin project remains SQLite-free.

- [ ] **Step 4: Run migration tests to verify they fail for missing migrator**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusMigrationTests
```

Expected: build fails because `SqliteToLiteDbMigrator` does not exist yet.

- [ ] **Step 5: Implement migration logic**

Create `StatsPlus.Migration/SqliteToLiteDbMigrator.cs`:

- Validate source exists.
- Validate target does not exist unless `overwrite` is true.
- Write to `targetPath + ".tmp"`.
- Read joined SQLite track context rows:

```sql
SELECT
    tc.id,
    g.name,
    g.normalized_name,
    c.model_name,
    c.normalized_model_name,
    c.display_model_name,
    t.raw_track_name,
    t.track_name_with_config,
    t.normalized_track_name_with_config,
    t.display_track_name_with_config,
    tc.created_utc,
    tc.last_updated_utc
FROM track_contexts tc
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id;
```

- Insert one `TrackHistoryDocument` per row.
- Store a `Dictionary<long, long>` mapping SQLite `track_contexts.id` to LiteDB `TrackHistoryDocument.Id`.
- Read SQLite laps:

```sql
SELECT
    id,
    track_context_id,
    lap_number,
    lap_time_seconds,
    sector1_seconds,
    sector2_seconds,
    sector3_seconds,
    is_valid,
    timestamp_utc
FROM laps;
```

- Insert one `LapDocument` per row with `TrackHistoryId` from the map.
- Preserve lap `id` in `LapDocument.Id`.
- Move the temp file to `targetPath` only after success.
- Delete the temp file on failure.

- [ ] **Step 6: Add CLI entry point**

Create `StatsPlus.Migration/Program.cs`:

```csharp
using System;

namespace StatsPlus.Migration
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: StatsPlus.Migration.exe --source <StatsPlus.laps.db> --target <StatsPlus.laps.ldb> [--overwrite]");
                return 2;
            }

            string source = null;
            string target = null;
            bool overwrite = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    source = args[++i];
                }
                else if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    target = args[++i];
                }
                else if (string.Equals(args[i], "--overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    overwrite = true;
                }
            }

            try
            {
                var result = new SqliteToLiteDbMigrator().Migrate(source, target, overwrite);
                Console.WriteLine($"Migrated {result.TrackHistoryCount} track histories and {result.LapCount} laps.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
```

- [ ] **Step 7: Run migration tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusMigrationTests
```

Expected: all migration tests pass.

- [ ] **Step 8: Commit migration utility**

```powershell
git add -- StatsPlus.Migration StatsPlus.Tests/StatsPlusMigrationTests.cs StatsPlus.Tests/StatsPlus.Tests.csproj StatsPlus.sln
git commit -m "Add SQLite to LiteDB migration utility"
```

---

### Task 5: Final Cleanup and Verification

**Files:**
- Inspect: `StatsPlus/StatsPlusPlugin.cs`
- Inspect: `StatsPlus.Tests/StatsPlus.Tests.csproj`
- Inspect: `StatsPlus/StatsPlus.csproj`
- Modify: only files found by the verification commands to still contain forbidden runtime SQLite or JSON lap-history references.

**Interfaces:**
- Consumes: all previous tasks.
- Produces: verified LiteDB-only runtime with external migration utility.

- [ ] **Step 1: Search for forbidden runtime references**

Run:

```powershell
rg -n "StatsPlusSqliteRepository|System\.Data\.SQLite|SQLite|Sqlite|StatsPlus\.laps\.db|StatsPlus\.laps\.json|LegacyDataFileName|ResolveLegacyDataPath|LoadDatabase\(|SaveDatabase\(" StatsPlus StatsPlus.Tests StatsPlus.Migration
```

Expected:

- No matches in `StatsPlus` for `System.Data.SQLite`, `StatsPlusSqliteRepository`, `StatsPlus.laps.db`, `StatsPlus.laps.json`, `LegacyDataFileName`, `ResolveLegacyDataPath`, `LoadDatabase(`, or `SaveDatabase(`.
- `SQLite` matches may remain in `StatsPlus.Migration`, `StatsPlus.Tests/StatsPlusMigrationTests.cs`, and historical markdown docs only.
- No visible UI text says SQLite.

- [ ] **Step 2: Verify main plugin project is SQLite-free**

Run:

```powershell
dotnet build StatsPlus\StatsPlus.csproj /p:UseSimHubSdkStubs=true /p:SimHubInstallPath=C:\does-not-exist
```

Expected: build succeeds and the main plugin project restores/builds with LiteDB but without `System.Data.SQLite.Core`.

- [ ] **Step 3: Run all automated tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 4: Build the full solution**

Run:

```powershell
dotnet build StatsPlus.sln /p:UseSimHubSdkStubs=true /p:SimHubInstallPath=C:\does-not-exist
```

Expected: solution build succeeds, including `StatsPlus.Migration`.

- [ ] **Step 5: Commit final cleanup**

If any cleanup changes were needed:

```powershell
git add -- StatsPlus StatsPlus.Tests StatsPlus.Migration StatsPlus.sln
git commit -m "Clean up LiteDB storage migration"
```

If no cleanup changes were needed, do not create an empty commit.
