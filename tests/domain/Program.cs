using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OneBeam.Domain;

internal static class Program
{
    private const string Sun = SpeciesRules.SunFlowerId;
    private const string Mother = SpeciesRules.LightMotherId;
    private const string Bamboo = SpeciesRules.SkyBambooId;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private static int Main()
    {
        try
        {
            SowingAndHarvest();
            SupportUsesStepStart();
            BambooAndExtinction();
            FruitAndDaylightHaveSeparateEffects();
            SnapshotContinuation();
            LevelChecks.Run();
            StandardLevelChecks.Run();
            Console.WriteLine("PASS one-beam domain and checkpoint checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void SowingAndHarvest()
    {
        var world = NewWorld();
        var sim = new EcologySimulation(world);
        Check(sim.TryPlantSeed(Sun, new Vector2(100, 100)), "first seed is placed");
        var afterPlanting = ToJson(world);
        Check(!sim.TryPlantSeed(Sun, new Vector2(105, 100)), "overlapping seeds are rejected");
        Check(!sim.TryPlantSeed(Sun, new Vector2(-1, 100)), "out-of-bounds seeds are rejected");
        Check(!sim.TryPlantSeed(Bamboo, new Vector2(240, 160)), "locked species are rejected");
        Check(!sim.TryHarvest(world.Plants[0].Id), "immature plants cannot be harvested");
        Check(JsonNode.DeepEquals(afterPlanting, ToJson(world)), "rejected commands preserve all world data");

        var ripe = world.Plants[0] with { Stage = PlantStage.Mature, Growth = 1 };
        var restored = Restore(world.Capture() with { Plants = [ripe], Seeds = new() { [Sun] = 0 } });
        var harvest = new EcologySimulation(restored);
        Check(harvest.TryHarvest(ripe.Id) && restored.Status == RunStatus.Playing, "harvest the last plant and keep seeds");
        int yield = restored.CountSeeds(Sun);
        Check(yield > 0 && !harvest.TryHarvest(ripe.Id) && restored.CountSeeds(Sun) == yield, "harvest rewards appear once");
        Check(harvest.TryPlantSeed(Sun, ripe.Position) && restored.Plants.Single().Id > ripe.Id, "IDs survive harvest and reload");
    }

    private static void SupportUsesStepStart()
    {
        var sun = new PlantState(1, Sun, new Vector2(100, 100)) { Stage = PlantStage.Seedling, Growth = .9999f };
        var target = new PlantState(2, Sun, new Vector2(200, 100));
        var template = NewWorld(sun.Position).Capture() with { Plants = [sun, target], NextPlantId = 3 };
        var first = new EcologySimulation(Restore(template));
        var reversed = new EcologySimulation(Restore(template with { Plants = [target, sun] }));
        float previous = 0;
        float firstIncrease = 0;
        for (int step = 1; step <= 2; step++)
        {
            first.AdvanceTime();
            reversed.AdvanceTime();
            foreach (var plant in first.State.Plants)
                Check(plant == reversed.State.Plants.Single(p => p.Id == plant.Id), "plant ordering changes numerical result");
            var darkness = first.State.Plants.Single(p => p.Id == target.Id).Darkness;
            if (step == 1)
            {
                Check(first.State.Plants.Single(p => p.Id == sun.Id).Stage == PlantStage.Mature, "fixture must mature on first step");
                firstIncrease = darkness;
            }
            else Check(darkness - previous < firstIncrease, "newly mature sunflower protects neighbour starting next step");
            previous = darkness;
        }
    }

    private static void BambooAndExtinction()
    {
        var basePosition = new Vector2(180, 150);
        var bamboo = new PlantState(1, Bamboo, basePosition) { Stage = PlantStage.Mature, Height = 149 };
        var fixedLamp = NewWorld(basePosition).Capture() with
        {
            Phase = GamePhase.FixedLamp, GardenReady = true,
            Plants = [bamboo], Seeds = new() { [Sun] = 1, [Mother] = 1, [Bamboo] = 0 }, NextPlantId = 2
        };
        var sideways = new EcologySimulation(Restore(fixedLamp));
        Check(!sideways.TryMoveLamp(Vector2.Zero) && sideways.State.LampPosition == basePosition, "lamp is fixed after departure");
        Check(sideways.TryAimPlant(bamboo.Id, basePosition + new Vector2(100, -5)), "aim at a visible point to the side");
        Advance(sideways, 40);
        Check(sideways.State.Phase == GamePhase.FixedLamp, "sideways growth cannot open the dome");

        var tooFar = bamboo with { Position = new Vector2(180, 360), Height = 220 };
        var farSim = new EcologySimulation(Restore(fixedLamp with { Plants = [tooFar], LampPosition = tooFar.Position }));
        Advance(farSim, 40);
        Check(farSim.State.Phase == GamePhase.FixedLamp, "finite bamboo height cannot reach a distant roof");

        var upward = new EcologySimulation(Restore(fixedLamp));
        upward.AdvanceTime();
        Check(upward.State.Phase == GamePhase.OpenLab && upward.State.Size.X > fixedLamp.DishSize.X, "roof contact opens the lab");
        var openedAt = upward.State.OpenedAtStep;
        upward.AdvanceTime();
        Check(upward.State.OpenedAtStep == openedAt, "opening event remains committed");

        var dying = new PlantState(2, Sun, new Vector2(550, 350)) { Darkness = .9999f };
        var loss = new EcologySimulation(Restore(fixedLamp with
        {
            Plants = [bamboo, dying], NextPlantId = 3, Seeds = new() { [Sun] = 0, [Mother] = 1, [Bamboo] = 0 }
        }));
        loss.AdvanceTime();
        Check(loss.State.Status == RunStatus.Failed && loss.State.FailureSpecies == Sun, "last sunflower extinction ends session");
        Check(loss.State.Phase == GamePhase.FixedLamp, "extinction in the same step takes precedence over opening");
    }

    private static void FruitAndDaylightHaveSeparateEffects()
    {
        var environment = new EnvironmentRules { DaySteps = 3, FogSteps = 2, NightSteps = 2, BoostDuration = 2 };
        var left = new PlantState(1, Sun, new Vector2(220, 420));
        var right = new PlantState(2, Sun, new Vector2(780, 420));
        var sim = new EcologySimulation(Restore(NewWorld().Capture() with
        {
            Phase = GamePhase.OpenLab, OpenedAtStep = 0, Environment = environment,
            Plants = [left, right], NextPlantId = 3, FruitCount = 1,
            GardenReady = true, Seeds = new() { [Sun] = 1, [Mother] = 1, [Bamboo] = 1 }
        }));

        Check(sim.IsIlluminated(right.Position), "daylight reaches the outer beds");
        Advance(sim, environment.DaySteps);
        Check(sim.State.Sky == SkyPhase.Fog && sim.IsIlluminated(left.Position) &&
            !sim.IsIlluminated(right.Position), "fog leaves a lit strip");
        Check(sim.TryGiveFruit(right.Id) && sim.State.FruitCount == 0 && !sim.TryGiveFruit(right.Id),
            "fruit is transferred exactly once");
        float growthBefore = sim.State.Plants[1].Growth;
        sim.AdvanceTime();
        var fedPlant = sim.State.Plants[1];
        Check(fedPlant.Growth > growthBefore && fedPlant.Darkness > 0,
            "fruit promotes growth in darkness while darkness still accumulates");
        sim.AdvanceTime();
        Check(sim.State.Sky == SkyPhase.Night && !sim.IsIlluminated(left.Position) &&
            sim.IsIlluminated(sim.State.LampPosition) && sim.State.Plants[1].BoostSteps == 0,
            "night leaves the fixed lamp on and the fruit effect expires");
    }

    private static void SnapshotContinuation()
    {
        var source = NewWorld();
        var direct = new EcologySimulation(source);
        Check(direct.TryPlantSeed(Sun, source.LampPosition), "set up continuation fixture");
        Advance(direct, 3);
        var snapshot = source.Capture();
        var copy = Restore(snapshot);
        snapshot.Seeds[Sun] = 4096;
        snapshot.Plants[0] = snapshot.Plants[0] with { Darkness = .8f };
        Check(source.CountSeeds(Sun) != 4096 && copy.CountSeeds(Sun) != 4096, "snapshots own mutable inventory");
        Check(source.Plants[0].Darkness != .8f && copy.Plants[0].Darkness != .8f, "snapshots own plant list");
        CheckpointChecks.Run(source);
        Advance(direct, 20);
        Advance(new EcologySimulation(copy), 20);
        Check(JsonNode.DeepEquals(ToJson(source), ToJson(copy)), "restored simulation follows the same next twenty steps");
    }

    private static WorldState NewWorld(Vector2? lamp = null) =>
        new(new Vector2(600, 390), lamp ?? new Vector2(180, 185), 84, Sun, 3);

    private static WorldState Restore(WorldSnapshot snapshot) => WorldState.TryRestore(snapshot, out var world)
        ? world! : throw new InvalidOperationException("fixture rejected by world validation");

    private static JsonNode? ToJson(WorldState state) => JsonSerializer.SerializeToNode(state.Capture(), Json);

    private static void Advance(EcologySimulation simulation, int steps)
    {
        for (int i = 0; i < steps; i++) simulation.AdvanceTime();
    }

    private static void Check(bool condition, string step)
    {
        if (!condition) throw new InvalidOperationException(step);
    }
}
