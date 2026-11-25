using System.Text.Json;
using System.Text.Json.Nodes;
using OneBeam.Domain;
using OneBeam.Persistence;

internal static class CheckpointChecks
{
    public static void Run(WorldState original)
    {
        var directory = Directory.CreateTempSubdirectory("one-beam-checkpoint-");
        var path = Path.Combine(directory.FullName, "checkpoint.json");
        var options = new JsonSerializerOptions { IncludeFields = true };
        try
        {
            Check(!CheckpointStore.TryLoad(path, out _, out var missing) && missing.Length > 0,
                "missing checkpoint gives a recoverable error");
            var snapshot = original.Capture();
            Check(CheckpointStore.TrySave(path, snapshot, out var error), error);
            Check(CheckpointStore.TryLoad(path, out var loaded, out error) && loaded != null, error);
            var expected = JsonSerializer.SerializeToNode(snapshot, options)!;
            var actual = JsonSerializer.SerializeToNode(loaded!.Capture(), options);
            Check(JsonNode.DeepEquals(expected, actual), "complete snapshot round trip, including positions");
            Check(WorldState.TryRestore(snapshot, out var baseline), "build uninterrupted comparison state");
            var savedRun = new EcologySimulation(loaded);
            var uninterrupted = new EcologySimulation(baseline!);
            for (int i = 0; i < 20; i++)
            {
                savedRun.AdvanceTime();
                uninterrupted.AdvanceTime();
            }
            Check(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(loaded.Capture(), options),
                JsonSerializer.SerializeToNode(baseline!.Capture(), options)), "disk-restored world continues identically");
            Check(CheckpointStore.TrySave(path, loaded.Capture(), out error), error);
            Check(CheckpointStore.TryLoad(path, out var nextSave, out error) && nextSave!.Step == loaded.Step,
                "subsequent checkpoint replaces the previous one");

            var originalBytes = File.ReadAllBytes(path);
            var empty = new WorldState(original.DishSize, original.LampPosition, original.LampRadius,
                SpeciesRules.SunFlowerId, 0);
            Check(empty.Status == RunStatus.Failed && empty.FailureSpecies == SpeciesRules.SunFlowerId &&
                WorldState.TryRestore(empty.Capture(), out _), "zero-seed starting state is a coherent loss");
            Check(!CheckpointStore.TrySave(path, empty.Capture(), out _) &&
                File.ReadAllBytes(path).SequenceEqual(originalBytes), "loss cannot replace a playable checkpoint");
            var invalid = expected.DeepClone().AsObject();
            invalid["Version"] = -1;
            Check(!CheckpointStore.TrySave(path, invalid.Deserialize<WorldSnapshot>(options)!, out _),
                "reject unknown version before replacing checkpoint");
            Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "old checkpoint survives rejected save");

            // A required numeric field can be zero legitimately. Omitting it must still fail.
            var broken = expected.DeepClone().AsObject();
            broken.Remove("FruitCount");
            File.WriteAllText(path, broken.ToJsonString());
            Check(!CheckpointStore.TryLoad(path, out var rejected, out error) && rejected == null && error.Length > 0,
                "reject missing snapshot data");
            File.WriteAllText(path, "{");
            Check(!CheckpointStore.TryLoad(path, out rejected, out error) && rejected == null && error.Length > 0,
                "reject truncated checkpoint");
        }
        finally
        {
            File.Delete(path);
            directory.Delete();
        }
    }

    private static void Check(bool condition, string step)
    {
        if (!condition) throw new InvalidOperationException(step);
    }
}
