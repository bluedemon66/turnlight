using System;
using System.Text.Json;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels;

public sealed record LevelDemoConfig(int GridSize, GrowthRules Rules, LevelDefinition[] Levels)
{
    private const string ConfigPath = "res://data/levels-demo.json";

    public static LevelDemoConfig Load()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<LevelDemoConfig>(
            FileAccess.GetFileAsString(ConfigPath), options);

        if (config is not { GridSize: > 0, Rules: not null, Levels.Length: 2 }
            || config.Levels[0].Number != 1 || config.Levels[1].Number != 2)
            throw new InvalidOperationException("Expected demo configuration for levels 1 and 2.");

        return config;
    }
}
