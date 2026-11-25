using System.Numerics;
using System.Text.Json;
using OneBeam.Levels.Domain;

internal static class LevelChecks
{
    private sealed record DemoData(int GridSize, GrowthRules Rules, LevelDefinition[] Levels);

    public static void Run()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var path = Path.Combine(AppContext.BaseDirectory, "levels-demo.json");
        var config = JsonSerializer.Deserialize<DemoData>(File.ReadAllText(path), options)!;
        var practice = config.Levels[0] with { DurationSeconds = 600 };

        CheckGrid(config.Rules, practice, 3);
        CheckGrid(config.Rules, practice, 5);
        CheckTravel(config.Rules, practice);
        CheckGrowth(config.Rules, practice, config.GridSize);
        CheckLevelTransitions(config);
        Console.WriteLine("PASS workbook levels domain checks");
    }

    private static void CheckGrid(GrowthRules rules, LevelDefinition level, int n)
    {
        var sim = new LevelSimulation(rules, level, n);
        int last = n * n - 1;
        Check(sim.GridSize == n && sim.SlotCount == n * n && sim.Slots.Count == n * n, $"{n}x{n} slots");
        Check(sim.LampPosition == Vector2.Zero && sim.Direction == BeamDirection.East, "initial lamp pose");
        CheckLit(sim, 0, 1);
        Check(sim.TryPlant(last) && !sim.TryPlant(last) && !sim.TryPlant(last + 1),
            "bottom-right cell can be planted once; slots end at the board boundary");
        Check(!sim.TryRotateLamp(-1) && sim.Direction == BeamDirection.East, "north cannot fit at top edge");
        Check(sim.ClampLampTarget(last) == last - 1 && sim.ClampLampTarget(-1) == -1 &&
            sim.ClampLampTarget(last + 1) == -1, "clamp an in-board edge without wrapping out-of-board slots");
        Check(!sim.TryMoveLamp(last) && sim.LampTarget == 0, "east beam cannot wrap into another row");
        Check(sim.TryMoveLamp(last - 1) && !sim.TryRotateLamp(1), "pending bottom-edge target blocks south rotation");
        sim.Advance((2 * n - 3) * rules.MoveSecondsPerSlot);
        Check(sim.LampPosition == new Vector2(n - 2, n - 1) && !sim.IsLampMoving, "arrive at bottom edge");
        CheckLit(sim, last - 1, last);
        Check(sim.TryMoveLamp(n + 1) && !sim.TryRotateLamp(1), "current bottom-edge location blocks south rotation");
        sim.Advance((2 * n - 5) * rules.MoveSecondsPerSlot);
        int center = n + 1;
        Check(sim.TryRotateLamp(1) && sim.Direction == BeamDirection.South, "E rotates east to south");
        CheckLit(sim, center, center + n);
        Check(sim.TryRotateLamp(1) && sim.Direction == BeamDirection.West, "south to west");
        CheckLit(sim, center - 1, center);
        Check(sim.TryRotateLamp(1) && sim.Direction == BeamDirection.North, "west to north");
        CheckLit(sim, center - n, center);
        Check(sim.TryRotateLamp(1) && sim.TryRotateLamp(-1) && sim.Direction == BeamDirection.North,
            "E and Q wrap around the north/east boundary in opposite directions");
    }

    private static void CheckTravel(GrowthRules rules, LevelDefinition level)
    {
        const int n = 5;
        var sim = new LevelSimulation(rules, level, n);
        var stepped = new LevelSimulation(rules, level, n);
        foreach (var instance in new[] { sim, stepped })
        {
            Check(instance.TryPlant(0) && instance.TryPlant(7) && instance.TryPlant(13), "sow along the two-axis route");
            Check(instance.TryMoveLamp(12), "target column two, row two");
        }
        double t = rules.MoveSecondsPerSlot;
        sim.Advance(.49 * t);
        CheckLit(sim, 0, 1);
        sim.Advance(.02 * t);
        CheckLit(sim, 1, 2);
        sim.Advance(1.49 * t);
        Check(sim.LampPosition == new Vector2(2, 0), "horizontal leg finishes before vertical movement");
        sim.Advance(.5 * t);
        CheckLit(sim, 7, 8);
        sim.Advance(1.5 * t);
        Check(sim.LampPosition == new Vector2(2, 2) && !sim.IsLampMoving, "two columns and two rows cost four intervals");
        CheckLit(sim, 12, 13);
        Near(sim.Slots[0]!.Growth, .5 * t * rules.LightGrowthPerSecond, "illumination at departure");
        Near(sim.Slots[7]!.Growth, t * rules.LightGrowthPerSecond, "illumination during vertical travel");
        Near(sim.Slots[13]!.Growth, .5 * t * rules.LightGrowthPerSecond, "illumination upon arrival");

        for (int i = 0; i < 100; i++) stepped.Advance(4 * t / 100);
        Check(sim.TryMoveLamp(0) && stepped.TryMoveLamp(0), "return across both axes");
        sim.Advance(4 * t);
        for (int i = 0; i < 16; i++) stepped.Advance(t / 4);
        Check(sim.LampPosition == Vector2.Zero && stepped.LampPosition == Vector2.Zero, "return to origin");
        for (int i = 0; i < sim.SlotCount; i++)
        {
            var a = sim.Slots[i];
            var b = stepped.Slots[i];
            Check(a?.Stage == b?.Stage && a?.Wilted == b?.Wilted, $"same plant in cell {i}");
            if (a == null || b == null) continue;
            Near(a.Growth, b.Growth, $"same growth in cell {i}");
            Near(a.Darkness, b.Darkness, $"same darkness in cell {i}");
        }
    }

    private static void CheckGrowth(GrowthRules rules, LevelDefinition practice, int n)
    {
        var dark = new LevelSimulation(rules, practice, n);
        int unlit = n * n - 1;
        Check(dark.TryPlant(2) && dark.TryPlant(unlit), "sow two unlit seeds");
        dark.Advance(126);
        Check(dark.Slots[2] is { Darkness: 63, Wilted: true, Growth: 0 }, "darkness 63 wilts a seed");
        Check(dark.TryMoveLamp(1), "move the beam one slot");
        dark.Advance(1.25);
        Check(dark.Slots[2] is { Wilted: true, Growth: 0 }, "seed stays wilted while the lamp travels");
        dark.Advance(.25);
        Check(dark.Slots[2] is { Wilted: false, Growth: > .12 and < .13 },
            "only the light after recovery counts toward growth");
        dark.Advance(52.375);
        Check(dark.Slots[unlit] is { Wilted: true, Darkness: < 90 }, "plant survives just before darkness 90");
        dark.Advance(.125);
        Check(dark.Slots[unlit] == null, "darkness 90 removes the dead seed");

        var mother = new LevelSimulation(rules, practice, n);
        Check(mother.TryPlant(0), "sow under the lamp");
        mother.Advance(50);
        Check(mother.Slots[0] is { Stage: GrowthStage.Normal, Growth: 0 }, "50 seconds to a normal plant");
        mother.Advance(180);
        Check(mother.Slots[0] is { Stage: GrowthStage.Mother, SeedsProduced: 0 }, "another 180 seconds to a mother");
        mother.Advance(60);
        Check(mother.Slots[0] is { SeedsProduced: 1 } && mother.AvailableSeeds == 3, "first seed needs 180 growth");
        mother.Advance(90);
        Check(mother.Slots[0] is { SeedsProduced: 2 } && mother.AvailableSeeds == 4, "second seed needs 270 growth");
    }

    private static void CheckLevelTransitions(DemoData config)
    {
        var demo = new LevelSimulation(config.Rules, config.Levels[0], config.GridSize);
        Check(demo.TryPlant(0) && demo.TryPlant(1) && demo.TryPlant(2), "use the three seeds of level 1");
        demo.Advance(45);
        demo.TryMoveLamp(1);
        demo.Advance(55);
        demo.TryMoveLamp(0);
        demo.Advance(45);
        Check(demo.Vitality == 150 && demo.Status == LevelRunStatus.Playing, "150 points before the cutoff");
        demo.TryMoveLamp(1);
        demo.Advance(34.875);
        Check(demo.Status == LevelRunStatus.Playing && !demo.TryStartNext(config.Levels[1]), "cutoff is exclusive before 180s");
        int nextRow = config.GridSize + 1;
        Check(demo.TryRotateLamp(1) && demo.TryMoveLamp(nextRow), "turn and start moving before cutoff");
        demo.Advance(.125);
        Check(demo.Status == LevelRunStatus.Won && demo.ElapsedSeconds == 180, "level 1 settles at 180 seconds");
        var previous = demo.Slots[0]!;
        var lamp = demo.LampPosition;
        Check(previous.Darkness > 0 && demo.TryStartNext(config.Levels[1]), "enter level 2 from a completed level");
        Check(demo.Slots[0] == (previous with { Darkness = 0, Wilted = false }) &&
            demo.ElapsedSeconds == 0 && demo.AvailableSeeds == 1, "carry growth and replenish seeds");
        Check(demo.LampPosition == lamp && demo.LampTarget == nextRow && demo.Direction == BeamDirection.South,
            "lamp direction, in-flight position and target carry across levels");
        Check(demo.TryPlant(3), "sow one more in level 2");
        demo.Advance(5);
        Check(demo.TryRotateLamp(-1) && demo.TryMoveLamp(0), "change the lamp pose during level 2");
        demo.RestartCurrent();
        Check(demo.Slots[3] == null && demo.AvailableSeeds == 1 && demo.Vitality == 150, "restart restores the exact level entry");
        Check(demo.LampPosition == lamp && demo.LampTarget == nextRow && demo.Direction == BeamDirection.South,
            "restart restores the entry lamp pose and ongoing movement");

        var empty = new LevelSimulation(config.Rules, config.Levels[0], config.GridSize);
        empty.Advance(181);
        Check(empty.Status == LevelRunStatus.Lost && empty.ElapsedSeconds == 180 && !empty.TryStartNext(config.Levels[1]),
            "an empty board loses at the exact deadline and cannot progress");
    }

    private static void CheckLit(LevelSimulation sim, params int[] expected)
    {
        var actual = Enumerable.Range(0, sim.SlotCount).Where(sim.IsLit);
        Check(actual.SequenceEqual(expected), $"expected footprint in cells {string.Join(", ", expected)}");
    }

    private static void Near(double a, double b, string step)
    {
        Check(Math.Abs(a - b) < 1e-7, step);
    }

    private static void Check(bool condition, string step)
    {
        if (!condition)
            throw new InvalidOperationException(step);
    }
}
