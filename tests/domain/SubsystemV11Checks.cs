using System.Numerics;
using OneBeam.Levels.Domain;

/// <summary>
/// Rules added from the subsystem documents V1.1 (Feishu, 2026-11-27): diagonal light moves,
/// remains after withering, the shovel, harvesting mothers, remains fading after some days.
/// </summary>
internal static class SubsystemV11Checks
{
    public static void Run(GrowthRules rules, LevelDefinition level, int n)
    {
        Check(rules is { DiagonalMove: true, LeaveRemains: true, RemainsDays: 3, HarvestSeeds: 3, ShovelCost: 0 },
            "standard config: diagonal moves, remains for 3 days, harvest 3 seeds, free shovel");
        Diagonal(rules, level, n);
        RemainsAndShovel(rules, level, n);
        Harvest(rules, level, n);
        RemainsFade(rules, level, n);
        Console.WriteLine("PASS subsystem V1.1 checks");
    }

    private static void Diagonal(GrowthRules rules, LevelDefinition level, int n)
    {
        var sim = new LevelSimulation(rules, level, n);
        Check(sim.TryMoveLamp(n + 1), "aim one cell right and one down");
        sim.Advance(.2);
        Check(Near(sim.LampPosition, new Vector2(.5f, .5f)), "both axes move at once (half way after 0.2 s)");
        sim.Advance(.2);
        Check(sim.LampPosition == new Vector2(1, 1) && !sim.IsLampMoving, "a diagonal step still takes 0.4 s");

        Check(sim.TryMoveLamp(3 * n + 3), "aim two right and two down");
        sim.Advance(.8);
        Check(sim.LampPosition == new Vector2(3, 3), "two diagonal cells take 0.8 s");

        var old = new LevelSimulation(rules with { DiagonalMove = false }, level, n);
        Check(old.TryMoveLamp(n + 1), "old rule: same target");
        old.Advance(.4);
        Check(Near(old.LampPosition, new Vector2(1, 0)), "with diagonalMove off the beam still goes across first");
    }

    private static void RemainsAndShovel(GrowthRules rules, LevelDefinition level, int n)
    {
        int corner = n * n - 1;
        var sim = new LevelSimulation(rules, level with { AddedSeeds = 5 }, n);
        Check(sim.TryPlant(corner) && sim.TryPlant(12), "two seeds out of the light");
        sim.Advance(37);
        Check(sim.Slots[corner] == null && sim.Remains[corner] == new LevelRemains(GrowthStage.Seed, 1),
            "a seed withers to death at 36 s and leaves remains dated day 1");
        Check(!sim.IsPitEmpty(corner) && !sim.TryPlant(corner), "nothing can be sown on remains");

        int seeds = sim.AvailableSeeds;
        Check(sim.TryShovel(corner) && sim.IsPitEmpty(corner) && sim.Remains[corner] == null, "the shovel clears remains");
        Check(sim.TryPlant(corner), "the cleared pit can be sown again");
        Check(sim.TryShovel(corner) && sim.IsPitEmpty(corner) && sim.AvailableSeeds == seeds - 1,
            "shovelling a live plant leaves an empty pit, no remains and no seed back");
        Check(!sim.TryShovel(corner), "an empty pit has nothing to shovel");

        var paid = new LevelSimulation(rules with { ShovelCost = 1 }, level, n);
        Check(paid.TryPlant(0) && !paid.TryShovel(0) && paid.Slots[0] != null,
            "with a cost and no reward points the shovel does nothing");

        var demo = new LevelSimulation(rules with { LeaveRemains = false }, level, n);
        Check(demo.TryPlant(corner), "old rule: seed in the dark");
        demo.Advance(37);
        Check(demo.IsPitEmpty(corner), "with leaveRemains off a dead plant leaves an empty pit");
    }

    private static void Harvest(GrowthRules rules, LevelDefinition level, int n)
    {
        var sim = new LevelSimulation(rules, level, n);
        Check(sim.TryPlant(0) && sim.TryPlant(1), "two seeds in the start beam");
        sim.Advance(20);
        Check(!sim.TryHarvest(0), "a normal plant cannot be harvested");
        sim.Advance(10.1);
        Check(sim.Slots[0] is { Stage: GrowthStage.Mother }, "a mother after 30 s of light");
        int seeds = sim.AvailableSeeds;
        Check(sim.TryHarvest(0), "harvest the mother");
        Check(sim.AvailableSeeds == seeds + rules.HarvestSeeds && sim.IsPitEmpty(0),
            "harvest gives harvestSeeds seeds; the plant dies without remains");
        Check(!sim.TryHarvest(0), "nothing left to harvest");
        sim.Advance(5);
        Check(sim.Slots[1] is { Stage: GrowthStage.Mother, SeedsProduced: > 0 }, "an unharvested mother still makes seeds itself");
    }

    private static void RemainsFade(GrowthRules rules, LevelDefinition level, int n)
    {
        int corner = n * n - 1;
        var days = Enumerable.Range(1, 4).Select(d => level with { Number = d, TargetVitality = 0 }).ToArray();
        var sim = new LevelSimulation(rules, days[0], n);
        Check(sim.TryPlant(corner), "seed in the dark on day 1");
        sim.Advance(level.DurationSeconds);
        Check(sim.Status == LevelRunStatus.Won && sim.Remains[corner] != null, "day 1 ends with remains");
        Check(sim.TryStartNext(days[1]) && sim.Remains[corner] != null, "remains stay on day 2");
        Check(sim.TryShovel(corner), "shovel them on day 2");
        sim.RestartCurrent();
        Check(sim.Remains[corner] != null, "restarting day 2 puts the remains back");
        sim.Advance(level.DurationSeconds);
        Check(sim.TryStartNext(days[2]) && sim.Remains[corner] != null, "remains stay on day 3");
        sim.Advance(level.DurationSeconds);
        Check(sim.TryStartNext(days[3]) && sim.IsPitEmpty(corner), "remains are gone 3 days after they formed");
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-4f;

    private static void Check(bool condition, string step)
    {
        if (!condition) throw new InvalidOperationException(step);
    }
}
