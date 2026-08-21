using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatsPlus;

internal sealed class CircuitDisplayParts
{
    public string CircuitNameDisplay { get; set; } = string.Empty;
    public string CircuitLayoutDisplay { get; set; } = string.Empty;
}

internal sealed class StatsPlusTrackDisplayContext
{
    public StatsPlusTrackDisplayContext(IReadOnlyDictionary<string, string> assettoCorsaTrackMap)
    {
        AssettoCorsaTrackMap = assettoCorsaTrackMap;
    }

    public IReadOnlyDictionary<string, string> AssettoCorsaTrackMap { get; }
}

internal interface IStatsPlusGameProfile
{
    string SettingsKey { get; }
    string DisplayName { get; }
    bool Matches(string gameName);
    bool IsRecordingEnabled(PluginSettings settings);
    bool UsesCapturedSectorsAsLapBoundaryEvidence { get; }
    string GetTrackDisplayName(string rawTrackNameWithConfig, StatsPlusTrackDisplayContext context);
    CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName);
    void InferSectorLayout(double lapTime, ref double sector1, ref double sector2, ref double sector3);
}

internal sealed class StatsPlusGameProfileRegistry
{
    private readonly IReadOnlyList<IStatsPlusGameProfile> _profiles;
    private readonly IStatsPlusGameProfile _fallbackProfile = new GenericStatsPlusGameProfile();

    private StatsPlusGameProfileRegistry(IReadOnlyList<IStatsPlusGameProfile> profiles)
    {
        _profiles = profiles;
    }

    public static StatsPlusGameProfileRegistry CreateDefault()
    {
        return new StatsPlusGameProfileRegistry(new IStatsPlusGameProfile[]
        {
            new AssettoCorsaProfile(),
            new AssettoCorsaCompetizioneProfile(),
            new AssettoCorsaEvoProfile(),
            new Automobilista2Profile(),
            new IRacingProfile(),
            new LeMansUltimateProfile(),
            new RFactor2Profile(),
            new RaceRoomProfile()
        });
    }

    public IReadOnlyList<IStatsPlusGameProfile> SupportedProfiles => _profiles;

    public IStatsPlusGameProfile Resolve(string gameName)
    {
        return _profiles.FirstOrDefault(profile => profile.Matches(gameName)) ?? _fallbackProfile;
    }
}

internal abstract class StatsPlusGameProfileBase : IStatsPlusGameProfile
{
    private readonly string[] _normalizedAliases;

    protected StatsPlusGameProfileBase(string settingsKey, string displayName, params string[] aliases)
    {
        SettingsKey = settingsKey ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        _normalizedAliases = (aliases ?? Array.Empty<string>())
            .Select(StatsPlusGameName.Normalize)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToArray();
    }

    public string SettingsKey { get; }
    public string DisplayName { get; }
    public bool Matches(string gameName) => _normalizedAliases.Contains(StatsPlusGameName.Normalize(gameName));
    public abstract bool IsRecordingEnabled(PluginSettings settings);
    public virtual bool UsesCapturedSectorsAsLapBoundaryEvidence => false;
    public virtual string GetTrackDisplayName(string rawTrackNameWithConfig, StatsPlusTrackDisplayContext context) => rawTrackNameWithConfig ?? string.Empty;
    public virtual CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName) => SplitCircuitDisplay(trackDisplayName, "-");

    public virtual void InferSectorLayout(double lapTime, ref double sector1, ref double sector2, ref double sector3)
    {
        if (lapTime <= 0)
        {
            sector1 = 0.0;
            sector2 = 0.0;
            sector3 = 0.0;
            return;
        }

        if (sector1 > 0 && sector2 > 0)
        {
            sector3 = Math.Max(0.0, lapTime - sector1 - sector2);
            return;
        }

        sector3 = lapTime;
    }

    protected static CircuitDisplayParts SameCircuitAndLayoutDisplay(string trackDisplayName)
    {
        string value = trackDisplayName ?? string.Empty;
        return new CircuitDisplayParts { CircuitNameDisplay = value, CircuitLayoutDisplay = value };
    }

    protected static CircuitDisplayParts SplitCircuitDisplay(string trackDisplayName, string separator)
    {
        string normalizedTrackDisplayName = NormalizeCircuitDisplayPart(trackDisplayName);
        if (string.IsNullOrWhiteSpace(trackDisplayName))
        {
            return new CircuitDisplayParts { CircuitNameDisplay = normalizedTrackDisplayName };
        }

        int separatorIndex = trackDisplayName.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new CircuitDisplayParts { CircuitNameDisplay = normalizedTrackDisplayName };
        }

        return new CircuitDisplayParts
        {
            CircuitNameDisplay = NormalizeCircuitDisplayPart(trackDisplayName.Substring(0, separatorIndex)),
            CircuitLayoutDisplay = NormalizeCircuitDisplayPart(trackDisplayName.Substring(separatorIndex + separator.Length))
        };
    }

    protected static string NormalizeCircuitDisplayPart(string value) => (value ?? string.Empty).Trim().Replace('_', ' ');

    protected static void InferAssettoFamilySectorLayout(double lapTime, ref double sector1, ref double sector2, ref double sector3)
    {
        if (lapTime <= 0)
        {
            sector1 = 0.0;
            sector2 = 0.0;
            sector3 = 0.0;
            return;
        }

        if (sector1 > 0 && sector2 > 0)
        {
            sector3 = Math.Max(0.0, lapTime - sector1 - sector2);
            return;
        }

        if (sector1 > 0 && sector2 <= 0)
        {
            sector2 = Math.Max(0.0, lapTime - sector1);
            sector3 = 0.0;
            return;
        }

        sector3 = lapTime;
    }
}

internal static class StatsPlusGameName
{
    public static string Normalize(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(gameName.Length);
        foreach (char character in gameName)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

internal abstract class AssettoFamilyStatsPlusGameProfileBase : StatsPlusGameProfileBase
{
    protected AssettoFamilyStatsPlusGameProfileBase(
        string settingsKey,
        string displayName,
        params string[] aliases)
        : base(settingsKey, displayName, aliases)
    {
    }

    public override bool UsesCapturedSectorsAsLapBoundaryEvidence => true;

    public override void InferSectorLayout(
        double lapTime,
        ref double sector1,
        ref double sector2,
        ref double sector3)
    {
        InferAssettoFamilySectorLayout(lapTime, ref sector1, ref sector2, ref sector3);
    }
}

internal sealed class AssettoCorsaProfile : AssettoFamilyStatsPlusGameProfileBase
{
    public AssettoCorsaProfile() : base("assettocorsa", "Assetto Corsa", "AssettoCorsa", "Assetto Corsa") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordAssettoCorsa == true;
    public override string GetTrackDisplayName(string rawTrackNameWithConfig, StatsPlusTrackDisplayContext context)
    {
        return context?.AssettoCorsaTrackMap != null && rawTrackNameWithConfig != null && context.AssettoCorsaTrackMap.TryGetValue(rawTrackNameWithConfig, out string mappedName)
            ? mappedName
            : rawTrackNameWithConfig ?? string.Empty;
    }
    public override CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName) => SameCircuitAndLayoutDisplay(trackDisplayName);
}

internal sealed class AssettoCorsaCompetizioneProfile : AssettoFamilyStatsPlusGameProfileBase
{
    public AssettoCorsaCompetizioneProfile() : base("assettocorsacompetizione", "Assetto Corsa Competizione", "AssettoCorsaCompetizione", "Assetto Corsa Competizione") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordAssettoCorsaCompetizione == true;
    public override CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName) => SameCircuitAndLayoutDisplay(trackDisplayName);
}

internal sealed class AssettoCorsaEvoProfile : AssettoFamilyStatsPlusGameProfileBase
{
    public AssettoCorsaEvoProfile() : base("assettocorsaevo", "Assetto Corsa EVO", "AssettoCorsaEvo", "Assetto Corsa EVO") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordAssettoCorsaEvo == true;
    public override string GetTrackDisplayName(string rawTrackNameWithConfig, StatsPlusTrackDisplayContext context)
    {
        return context?.AssettoCorsaTrackMap != null && rawTrackNameWithConfig != null && context.AssettoCorsaTrackMap.TryGetValue(rawTrackNameWithConfig, out string mappedName)
            ? mappedName
            : rawTrackNameWithConfig ?? string.Empty;
    }
}

internal sealed class Automobilista2Profile : StatsPlusGameProfileBase
{
    public Automobilista2Profile() : base("automobilista2", "Automobilista 2", "Automobilista2", "Automobilista 2") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordAutomobilista2 == true;
}

internal sealed class IRacingProfile : StatsPlusGameProfileBase
{
    public IRacingProfile() : base("iracing", "iRacing", "IRacing", "iRacing") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordIRacing == true;
    public override CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName)
    {
        CircuitDisplayParts parts = SplitCircuitDisplay(trackDisplayName, "-");
        parts.CircuitNameDisplay = ToCircuitTitleCase(parts.CircuitNameDisplay);
        return parts;
    }
    private static string ToCircuitTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < words.Length; index++)
        {
            string word = words[index];
            words[index] = string.Equals(word, "gp", StringComparison.OrdinalIgnoreCase)
                ? "GP"
                : char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
        }

        return string.Join(" ", words);
    }
}

internal sealed class LeMansUltimateProfile : StatsPlusGameProfileBase
{
    public LeMansUltimateProfile() : base("lmu", "Le Mans Ultimate", "LMU", "Le Mans Ultimate") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordLeMansUltimate == true;
    public override CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName) => SameCircuitAndLayoutDisplay(trackDisplayName);
}

internal sealed class RFactor2Profile : StatsPlusGameProfileBase
{
    public RFactor2Profile() : base("rfactor2", "rFactor 2", "RFactor2", "rFactor 2") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordRFactor2 == true;
    public override CircuitDisplayParts GetCircuitDisplayParts(string trackDisplayName) => SplitCircuitDisplay(trackDisplayName, "--");
}

internal sealed class RaceRoomProfile : StatsPlusGameProfileBase
{
    public RaceRoomProfile() : base("raceroomracingexperience", "RaceRoom Racing Experience", "RaceRoom Racing Experience", "R3E", "RRRE") { }
    public override bool IsRecordingEnabled(PluginSettings settings) => settings?.RecordR3E == true;
}

internal sealed class GenericStatsPlusGameProfile : StatsPlusGameProfileBase
{
    public GenericStatsPlusGameProfile() : base(string.Empty, string.Empty) { }
    public override bool IsRecordingEnabled(PluginSettings settings) => false;
}
