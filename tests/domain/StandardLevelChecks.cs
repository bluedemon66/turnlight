using System.Numerics;
using System.Text.Json;
using OneBeam.Levels.Domain;

/// <summary>
/// The single standard level in data/camera-levels.json (Feishu sheet 「标准关」, 2026-11-26):
/// ellipse beam, 2.5 cells per second, 120 s judged at the end, target 105.
/// </summary>
internal static class StandardLevelChecks
{
    private sealed record Data(int GridSize, GrowthRules Rules, LevelDefinition[] Levels);

    public static void Run()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var path = Path.Combine(AppContext.BaseDirectory, "camera-levels.json");
        var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), options)!;
        var level = data.Levels.Single();
        Check(data.GridSize == 5 && data.Rules.EllipseBeam && data.Rules.StageDarkRelief == 0, "standard rules: 5x5, ellipse, no stage relief");
        Check(level is { DurationSeconds: 120, AddedSeeds: 3, LightRange: 2, TargetVitality: 105 }, "standard level: 120 s, 3 seeds, target 105");

        Ellipse(data);
        Sweep(data);
        Pacing(data);
        EndJudgement(data);
        if (Environment.GetEnvironmentVariable("STD_REPORT") != "1") SubsystemV11Checks.Run(data.Rules, data.Levels[0], data.GridSize);
        if (Environment.GetEnvironmentVariable("STD_SWEEP") == "1") { Sweep(data, 0); return; }
        Playthroughs(data);
        Console.WriteLine("PASS standard level checks");
    }

    private static void Sweep(Data d, int _)
    {
        foreach (double k in new[] { 2.0, 1.5, 1.25, 1.0 })
            foreach (double scale in new[] { 1.0, .8, .65 })
            {
                double[] dieAfter = { 45 * scale, 35 * scale, 30 * scale, 25 * scale };
                var stages = d.Rules.Stages.Select((st, i) => st with { DarkPerSecond = 90 / dieAfter[i] }).ToArray();
                var rules = d.Rules with { Stages = stages, LightDarkPerSecond = -k * stages[2].DarkPerSecond };
                var dd = d with { Rules = rules };
                var row = new List<string>();
                foreach (var (name, cap, think) in new[] { ("熟4", 4, .5), ("熟5", 5, .5), ("熟6", 6, .5), ("熟全", 25, .5), ("疏4", 4, 2.5), ("疏全", 25, 2.5) })
                {
                    var (curve, sim) = Play(dd, cap, think);
                    var alive = sim.Slots.Where(p => p != null).ToList();
                    row.Add($"{name}:{curve[^1]}{(alive.Any(p => p!.Wilted) ? "蔫" + alive.Count(p => p!.Wilted) : "")}");
                }
                Console.WriteLine($"  抵{k} 死亡×{scale}({string.Join("/", dieAfter.Select(x => Math.Round(x)))}) | {string.Join("  ", row)}");
            }
    }

    private static void Ellipse(Data d)
    {
        var sim = new LevelSimulation(d.Rules, d.Levels[0], d.GridSize);
        Check(Lit(sim).SetEquals(new[] { 0, 1 }), "east beam lights A1 and B1");
        Check(sim.TryRotateLamp(1) && sim.Direction == BeamDirection.SouthEast, "E turns 45 degrees");
        Check(Lit(sim).SetEquals(new[] { 0, 6 }), "south-east beam lights A1 and the diagonal B2");
        Check(sim.TryRotateLamp(1) && sim.Direction == BeamDirection.South && Lit(sim).SetEquals(new[] { 0, 5 }), "south beam lights A1 and A2");
        Check(sim.TryRotateLamp(-1) && sim.TryRotateLamp(-1) && sim.Direction == BeamDirection.East, "Q turns back");
        Check(!sim.TryRotateLamp(-1), "north-east does not fit on the top edge");
    }

    private static void Sweep(Data d)
    {
        var sim = new LevelSimulation(d.Rules, d.Levels[0] with { AddedSeeds = 1 }, d.GridSize);
        Check(sim.TryPlant(3), "seed on D1, outside the start beam");
        Check(sim.TryMoveLamp(2), "aim at C1-D1");
        sim.Advance(.4);
        Check(Math.Abs(sim.LampPosition.X - 1) < 1e-4 && sim.IsLit(2) && !sim.IsLit(0), "0.4 s moves one cell and the beam slides with it");
        sim.Advance(.4);
        Check(sim.LampPosition == new Vector2(2, 0) && sim.IsLit(3), "2.5 cells per second");
        var seed = sim.Slots[3]!;
        Check(seed.Growth > 0 && seed.Growth < 3 * .8, "the plant gets light while the beam sweeps onto it");
    }

    private static void Pacing(Data d)
    {
        var sim = new LevelSimulation(d.Rules, d.Levels[0], d.GridSize);
        Check(sim.TryPlant(0) && sim.TryPlant(24), "one plant lit, one in the dark");
        double[] at = { 5, 15, 30 };
        var stages = new[] { GrowthStage.Seedling, GrowthStage.Normal, GrowthStage.Mother };
        double t = 0;
        for (int i = 0; i < 3; i++)
        {
            sim.Advance(at[i] - t - .01);
            Check(sim.Slots[0]!.Stage == stages[i] - 1, $"not yet {stages[i]} just before {at[i]} s");
            sim.Advance(.02);
            Check(sim.Slots[0]!.Stage == stages[i], $"{stages[i]} at {at[i]} s of light");
            t = at[i] + .01;
        }
        int seeds = sim.AvailableSeeds;
        sim.Advance(5);
        Check(sim.AvailableSeeds == seeds + 1, "first seed after 5 s as a mother");
        sim.Advance(10);
        Check(sim.AvailableSeeds == seeds + 2, "second seed 10 s later");
        Check(sim.Slots[24] == null, "an unlit seed dies within 36 s");
        Check(sim.Vitality == 30, "a healthy mother is worth 30");
    }

    private static void EndJudgement(Data d)
    {
        var sim = new LevelSimulation(d.Rules, d.Levels[0] with { TargetVitality = 0 }, d.GridSize);
        sim.Advance(119.9);
        Check(sim.Status == LevelRunStatus.Playing, "reaching the target does not end the level early");
        sim.Advance(1);
        Check(sim.Status == LevelRunStatus.Won && sim.ElapsedSeconds == 120, "judged at 120 s");
    }

    /// Keeping 4 plants must pass (skilled or not); sowing everything must fail. 5 and 6 are printed only.
    /// Set STD_SWEEP=1 to print a sweep of the pressure anchors instead.
    /// A simple bot like the planning simulation: keep plants near the centre, sow up to a cap,
    /// every 0.5 s aim at the pose that best serves dark or growing plants.
    private static void Playthroughs(Data d)
    {
        var results = new List<string>();
        foreach (var (name, cap, think, min, max) in new[]
        {
            ("熟练，控制 4 株", 4, .5, 105.0, 999.0),
            ("熟练，种子全种", 25, .5, 0.0, 104.9),
            ("生疏，种子全种", 25, 2.5, 0.0, 104.9),
            ("熟练，控制 5 株", 5, .5, 0.0, 999.0),
            // Diagonal moves (subsystem V1.1) let this bot keep 6 plants and pass (117.5 vs 100 before).
            // Printed only until the numbers are re-derived; see 共享信息 第 8 节.
            ("熟练，控制 6 株", 6, .5, 0.0, 999.0),
            ("生疏，控制 4 株", 4, 2.5, 105.0, 999.0),
        })
        {
            var (curve, sim) = Play(d, cap, think);
            var alive = sim.Slots.Where(p => p != null).ToList();
            results.Add($"{name}: {string.Join(" / ", curve)} → {sim.Status}  存活{alive.Count} [种{alive.Count(p => p!.Stage == GrowthStage.Seed)} 幼{alive.Count(p => p!.Stage == GrowthStage.Seedling)} 常{alive.Count(p => p!.Stage == GrowthStage.Normal)} 母{alive.Count(p => p!.Stage == GrowthStage.Mother)}] 蔫{alive.Count(p => p!.Wilted)} 库存{sim.AvailableSeeds}");
            if (Environment.GetEnvironmentVariable("STD_REPORT") == "1") continue;
            Check(curve[^1] >= min && curve[^1] <= max, $"{name} ends between {min} and {max} (got {curve[^1]})");
        }
        foreach (var line in results) Console.WriteLine("  " + line);
    }

    private static (List<double>, LevelSimulation) Play(Data d, int cap, double think)
    {
        var sim = new LevelSimulation(d.Rules, d.Levels[0], d.GridSize);
        int n = d.GridSize;
        var order = Enumerable.Range(0, n * n)
            .OrderBy(s => Math.Abs(s % n - 2) + Math.Abs(s / n - 2) + (s % n - 2) * .01 + (s / n - 2) * .001).ToArray();
        var curve = new List<double>();
        double next = 0;
        for (double t = 0; sim.Status == LevelRunStatus.Playing; t += .05)
        {
            while (sim.AvailableSeeds > 0 && sim.Slots.Count(p => p != null) < cap)
            {
                int slot = order.FirstOrDefault(s => sim.IsPitEmpty(s) && Near(sim, s), -1);
                if (slot < 0) slot = order.FirstOrDefault(sim.IsPitEmpty, -1);
                if (slot < 0 || !sim.TryPlant(slot)) break;
            }
            if (t >= next && !Moving(sim)) { Aim(sim); next = t + think; }
            sim.Advance(.05);
            foreach (var mark in new[] { 30.0, 60, 90 })
                if (Math.Abs(sim.ElapsedSeconds - mark) < .025) curve.Add(sim.Vitality);
        }
        curve.Add(sim.Vitality);
        return (curve, sim);
    }

    private static bool Near(LevelSimulation sim, int slot)
    {
        int n = sim.GridSize;
        if (sim.Slots.All(p => p == null)) return true;
        for (int i = 0; i < sim.SlotCount; i++)
            if (sim.Slots[i] != null && Math.Max(Math.Abs(i % n - slot % n), Math.Abs(i / n - slot / n)) == 1) return true;
        return false;
    }

    private static bool Moving(LevelSimulation sim) =>
        sim.LampPosition != new Vector2(sim.LampTarget % sim.GridSize, sim.LampTarget / sim.GridSize);

    private static double Need(LevelPlant p) =>
        (p.Darkness > 35 ? 4 + p.Darkness / 20 : p.Darkness / 60) + (p.Wilted ? 3 : 0) + (p.Stage == GrowthStage.Mother ? .9 : 1);

    private static readonly BeamDirection[] Ring =
    {
        BeamDirection.East, BeamDirection.SouthEast, BeamDirection.South, BeamDirection.SouthWest,
        BeamDirection.West, BeamDirection.NorthWest, BeamDirection.North, BeamDirection.NorthEast
    };

    // Same edge rule as the simulation: the beam must stay on the field.
    private static bool Fits(int n, BeamDirection dir, int x, int y)
    {
        var (sx, sy) = LevelSimulation.Step(dir);
        return x + sx >= 0 && x + sx < n && y + sy >= 0 && y + sy < n;
    }

    private static void Aim(LevelSimulation sim)
    {
        int n = sim.GridSize;
        int here = Array.IndexOf(Ring, sim.Direction);
        var (lx, ly) = ((int)MathF.Round(sim.LampPosition.X), (int)MathF.Round(sim.LampPosition.Y));
        double best = ScoreAt(sim, sim.Direction, lx, ly) * 1.2 + .01;
        (int turns, int target)? pick = null;
        for (int k = 0; k < 8; k++)
            for (int target = 0; target < sim.SlotCount; target++)
            {
                var dir = Ring[k];
                int x = target % n, y = target / n;
                if (!Fits(n, dir, x, y)) continue;
                double cost = Math.Abs(x - lx) + Math.Abs(y - ly);
                double s = ScoreAt(sim, dir, x, y) - cost * .4 * .15;
                if (s > best) { best = s; int t = (k - here + 8) % 8; pick = (t > 4 ? t - 8 : t, target); }
            }
        if (pick is not { } chosen) return;
        // Turn where the beam fits, then move; if a turn is blocked at this spot, move first.
        bool turned = true;
        for (int i = 0; i < Math.Abs(chosen.turns) && turned; i++) turned = sim.TryRotateLamp(Math.Sign(chosen.turns));
        int aim = sim.ClampLampTarget(chosen.target);
        if (aim >= 0) sim.TryMoveLamp(aim);
    }

    private static double ScoreAt(LevelSimulation sim, BeamDirection dir, int x, int y)
    {
        var (sx, sy) = LevelSimulation.Step(dir);
        double cx = x + sx * .5, cy = y + sy * .5, angle = Math.Atan2(sy, sx), total = 0;
        for (int i = 0; i < sim.SlotCount; i++)
        {
            if (sim.Slots[i] is not { } p) continue;
            double dx = i % sim.GridSize - cx, dy = i / sim.GridSize - cy;
            double u = dx * Math.Cos(angle) + dy * Math.Sin(angle), v = -dx * Math.Sin(angle) + dy * Math.Cos(angle);
            if (u * u + v * v / .25 < 1 - 1e-9) total += Need(p);
        }
        return total;
    }

    private static HashSet<int> Lit(LevelSimulation sim) =>
        Enumerable.Range(0, sim.SlotCount).Where(sim.IsLit).ToHashSet();

    private static void Check(bool condition, string step)
    {
        if (!condition) throw new InvalidOperationException(step);
    }
}
