using System;
using System.Text.Json;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels;

/// <summary>
/// Interface switches for the camera-feed scene (data/camera-levels.json "interface").
/// There is no settings menu yet, so the two card switches live here.
/// </summary>
public sealed record InterfaceSettings(
    double HoursPerDay = 24,
    bool AutoCardsForLitPlants = true,
    bool CloseCardsOnSpeedChange = true,
    double StuckMessageSeconds = 1.5,
    double MessageSeconds = 2.3,
    double NearWiltDarkness = 54);

public sealed record LevelDemoConfig(int GridSize, GrowthRules Rules, LevelDefinition[] Levels,
    InterfaceSettings? Interface = null)
{
    public InterfaceSettings Ui => Interface ?? new InterfaceSettings();

    private const string ConfigPath = "res://data/levels-demo.json";

    public static LevelDemoConfig Load(string path = ConfigPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<LevelDemoConfig>(
            FileAccess.GetFileAsString(path), options);

        // One or more levels numbered 1, 2, ... (the camera build ships a single standard level).
        bool numbered = config is { GridSize: > 0, Rules: not null, Levels.Length: > 0 };
        for (int i = 0; numbered && i < config!.Levels.Length; i++)
            numbered = config.Levels[i]?.Number == i + 1;
        if (!numbered)
            throw new InvalidOperationException("Expected demo levels numbered from 1.");

        return config!;
    }
}
