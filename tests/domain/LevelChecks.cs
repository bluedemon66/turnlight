using System.Text.Json;
using OneBeam.Levels.Domain;

internal static class LevelChecks
{
    private sealed record DemoData(int SlotCount, GrowthRules Rules, LevelDefinition[] Levels);

    public static void Run()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var path = Path.Combine(AppContext.BaseDirectory, "levels-demo.json");
        var config = JsonSerializer.Deserialize<DemoData>(File.ReadAllText(path), options)!;
        var practice = config.Levels[0] with { DurationSeconds = 600 };

        var dark = new LevelSimulation(config.Rules, practice, config.SlotCount);
        Check(dark.TryPlant(2) && dark.TryPlant(4), "sow two unlit seeds");
        dark.Advance(126);
        Check(dark.Slots[2] is { Darkness: 63, Wilted: true, Growth: 0 }, "darkness 63 wilts a seed");
        Check(dark.TryMoveLamp(1), "move the beam one slot");
        dark.Advance(1.25);
        Check(dark.Slots[2] is { Wilted: true, Growth: 0 }, "seed stays wilted while the lamp travels");
        dark.Advance(.25);
        Check(dark.Slots[2] is { Wilted: false, Growth: > .12 and < .13 },
            "only the light after recovery counts toward growth");
        dark.Advance(52.5);
        Check(dark.Slots[4] == null, "darkness 90 removes the dead seed");

        var mother = new LevelSimulation(config.Rules, practice, config.SlotCount);
        Check(mother.TryPlant(0), "sow under the lamp");
        mother.Advance(50);
        Check(mother.Slots[0] is { Stage: GrowthStage.Normal, Growth: 0 }, "50 seconds to a normal plant");
        mother.Advance(180);
        Check(mother.Slots[0] is { Stage: GrowthStage.Mother, SeedsProduced: 0 }, "another 180 seconds to a mother");
        mother.Advance(60);
        Check(mother.Slots[0] is { SeedsProduced: 1 } && mother.AvailableSeeds == 3, "first seed needs 180 growth");
        mother.Advance(90);
        Check(mother.Slots[0] is { SeedsProduced: 2 } && mother.AvailableSeeds == 4, "second seed needs 270 growth");

        var demo = new LevelSimulation(config.Rules, config.Levels[0], config.SlotCount);
        Check(demo.TryPlant(0) && demo.TryPlant(1) && demo.TryPlant(2), "use the three seeds of level 1");
        demo.Advance(45);
        demo.TryMoveLamp(1);
        demo.Advance(55);
        demo.TryMoveLamp(0);
        demo.Advance(45);
        Check(demo.Vitality == 150 && demo.Status == LevelRunStatus.Playing, "150 points before the cutoff");
        demo.TryMoveLamp(1);
        demo.Advance(35);
        Check(demo.Status == LevelRunStatus.Won, "level 1 settles at 180 seconds");
        var previous = demo.Slots[0]!;
        Check(previous.Darkness > 0 && demo.TryStartNext(config.Levels[1]), "enter level 2 from a completed level");
        Check(demo.Slots[0] == (previous with { Darkness = 0, Wilted = false }) &&
            demo.ElapsedSeconds == 0 && demo.AvailableSeeds == 1, "carry growth and replenish seeds");
        Check(demo.TryPlant(3), "sow one more in level 2");
        demo.RestartCurrent();
        Check(demo.Slots[3] == null && demo.AvailableSeeds == 1 && demo.Vitality == 150, "restart restores the exact level entry");

        Console.WriteLine("PASS workbook levels domain checks");
    }

    private static void Check(bool condition, string step)
    {
        if (!condition)
            throw new InvalidOperationException(step);
    }
}
