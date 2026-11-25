using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace OneBeam.Domain;

public sealed class WorldState
{
    private readonly List<PlantState> _plants = new();
    private readonly Dictionary<string, int> _seeds = new();
    public IReadOnlyList<PlantState> Plants { get; }
    public IReadOnlyDictionary<string, int> Seeds { get; }
    public IReadOnlyCollection<string> IntroducedSpecies => _seeds.Keys;
    public EnvironmentRules Environment { get; }
    public Vector2 DishSize { get; }
    public Vector2 Size => Phase == GamePhase.OpenLab ? DishSize * Environment.SizeMultiplier : DishSize;
    public Vector2 LampPosition { get; internal set; }
    public float LampRadius { get; }
    public int FruitCount { get; internal set; }
    public int NextPlantId { get; private set; } = 1;
    public int Step { get; internal set; }
    public int? OpenedAtStep { get; internal set; }
    public GamePhase Phase { get; internal set; }
    public RunStatus Status { get; internal set; }
    public string? FailureSpecies { get; internal set; }
    public bool GardenReady { get; internal set; }
    public int OpenSteps => OpenedAtStep.HasValue ? Step - OpenedAtStep.Value : 0;
    public int Day => 1 + (OpenedAtStep ?? Step) / Environment.CycleLength + OpenSteps / Environment.CycleLength;
    public SkyPhase Sky
    {
        get
        {
            int t = OpenSteps % Environment.CycleLength;
            if (t < Environment.DaySteps) return SkyPhase.Day;
            return t < Environment.DaySteps + Environment.FogSteps ? SkyPhase.Fog : SkyPhase.Night;
        }
    }
    public int RequiredZones => Environment.RequiredZones;
    public int CoveredZones
    {
        get
        {
            var zones = new HashSet<(int x, int y)>();
            var size = Size;
            foreach (var plant in _plants)
            {
                if (plant.Stage != PlantStage.Mature) continue;
                int x = Math.Min((int)(plant.Position.X / size.X * Environment.Columns), Environment.Columns - 1);
                int y = Math.Min((int)(plant.Position.Y / size.Y * Environment.Rows), Environment.Rows - 1);
                zones.Add((x, y));
            }
            return zones.Count;
        }
    }
    public double Coverage => (double)CoveredZones / (Environment.Columns * Environment.Rows);

    public WorldState(Vector2 size, Vector2 lampPosition, float lampRadius,
        string speciesId, int startingSeeds, EnvironmentRules? environment = null)
    {
        Environment = environment ?? new EnvironmentRules();
        if (!Environment.IsValid) throw new ArgumentException("Invalid environment rules.", nameof(environment));
        DishSize = size;
        if (!IsFinite(size * Environment.SizeMultiplier) || size.X <= 0 || size.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!Contains(lampPosition)) throw new ArgumentOutOfRangeException(nameof(lampPosition));
        if (!float.IsFinite(lampRadius) || lampRadius <= 0) throw new ArgumentOutOfRangeException(nameof(lampRadius));
        if (speciesId != SpeciesRules.SunFlowerId)
            throw new ArgumentException("The initial species must be the sunflower.", nameof(speciesId));
        ArgumentOutOfRangeException.ThrowIfNegative(startingSeeds);
        LampPosition = lampPosition;
        LampRadius = lampRadius;
        Plants = _plants.AsReadOnly();
        Seeds = new ReadOnlyDictionary<string, int>(_seeds);
        _seeds.Add(speciesId, startingSeeds);
        if (startingSeeds == 0)
        {
            Status = RunStatus.Failed;
            FailureSpecies = speciesId;
        }
    }

    internal static bool IsFinite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
    internal bool Contains(Vector2 point) => IsInside(point, Size);
    private static bool IsInside(Vector2 p, Vector2 size) => p.X >= 0 && p.Y >= 0 && p.X <= size.X && p.Y <= size.Y;
    public int CountSeeds(string speciesId) => speciesId != null ? _seeds.GetValueOrDefault(speciesId) : 0;
    internal void Introduce(string speciesId, int seeds) => _seeds.TryAdd(speciesId, seeds);
    internal void AddSeeds(string speciesId, int seeds) => _seeds[speciesId] += seeds;
    internal int IndexOf(int id) => _plants.FindIndex(p => p.Id == id);
    internal void UpdateAt(int index, PlantState plant) => _plants[index] = plant;
    internal void RemoveAt(int index) => _plants.RemoveAt(index);
    internal void ReplacePlants(List<PlantState> next)
    {
        _plants.Clear();
        _plants.AddRange(next);
    }
    internal void Sow(string speciesId, Vector2 position)
    {
        _seeds[speciesId]--;
        _plants.Add(new PlantState(NextPlantId++, speciesId, position));
    }

    public WorldSnapshot Capture() => new(1, DishSize, LampPosition, LampRadius, Environment,
        _plants.ToArray(), new Dictionary<string, int>(_seeds), FruitCount, NextPlantId,
        Step, OpenedAtStep, Phase, Status, FailureSpecies, GardenReady);

    public static bool TryRestore(WorldSnapshot? snapshot, out WorldState? state)
    {
        state = null;
        if (snapshot is not { Version: 1, Environment: { IsValid: true }, Plants: not null,
            Seeds.Count: > 0, FruitCount: >= 0, NextPlantId: > 0, Step: >= 0 and < int.MaxValue } s)
            return false;
        if (!Enum.IsDefined(s.Phase) || !Enum.IsDefined(s.Status) ||
            !IsFinite(s.DishSize * s.Environment.SizeMultiplier) || s.DishSize.X <= 0 || s.DishSize.Y <= 0 ||
            !IsInside(s.LampPosition, s.DishSize) || !float.IsFinite(s.LampRadius) || s.LampRadius <= 0 ||
            (s.Phase == GamePhase.OpenLab) != s.OpenedAtStep.HasValue ||
            s.OpenedAtStep is < 0 || s.OpenedAtStep > s.Step ||
            s.Seeds.Any(kv => !SpeciesRules.IsKnown(kv.Key) || kv.Value < 0) ||
            !s.Seeds.ContainsKey(SpeciesRules.SunFlowerId) ||
            (s.GardenReady != s.Seeds.ContainsKey(SpeciesRules.LightMotherId)) ||
            (s.Phase != GamePhase.MovableLamp && !s.GardenReady) ||
            (s.Phase != GamePhase.MovableLamp) != s.Seeds.ContainsKey(SpeciesRules.SkyBambooId))
            return false;

        var size = s.Phase == GamePhase.OpenLab ? s.DishSize * s.Environment.SizeMultiplier : s.DishSize;
        var ids = new HashSet<int>();
        foreach (var p in s.Plants)
        {
            if (p == null || p.Id <= 0 || p.Id >= s.NextPlantId || !ids.Add(p.Id) ||
                !s.Seeds.ContainsKey(p.SpeciesId ?? "") || !IsInside(p.Position, size) ||
                !Enum.IsDefined(p.Stage) || p.Stage == PlantStage.Dead || !Unit(p.Growth) ||
                !Unit(p.Darkness) || p.Darkness >= 1 || !Unit(p.StoredLight) ||
                p.BoostSteps < 0 || p.BoostSteps > s.Environment.BoostDuration ||
                p.FruitProgress < 0 || !float.IsFinite(p.Height) || p.Height < 0 ||
                !IsFinite(p.Direction) || Math.Abs(p.Direction.LengthSquared() - 1f) > .001f)
                return false;
        }
        string? extinct = s.Seeds.Keys.FirstOrDefault(k => s.Seeds[k] == 0 && !s.Plants.Any(p => p.SpeciesId == k));
        if ((s.Status == RunStatus.Failed) != (extinct != null) ||
            (s.Status == RunStatus.Failed && (s.FailureSpecies == null ||
                !s.Seeds.TryGetValue(s.FailureSpecies, out int remaining) || remaining != 0 ||
                s.Plants.Any(p => p.SpeciesId == s.FailureSpecies))) ||
            (s.Status != RunStatus.Failed && s.FailureSpecies != null)) return false;

        var restored = new WorldState(s.DishSize, s.LampPosition, s.LampRadius, SpeciesRules.SunFlowerId, 0, s.Environment)
        {
            Step = s.Step, OpenedAtStep = s.OpenedAtStep, Phase = s.Phase, NextPlantId = s.NextPlantId,
            FruitCount = s.FruitCount, GardenReady = s.GardenReady, Status = s.Status, FailureSpecies = s.FailureSpecies
        };
        foreach (var item in s.Seeds) restored._seeds[item.Key] = item.Value;
        restored._plants.AddRange(s.Plants);
        if (s.Status == RunStatus.Won && (restored.Phase != GamePhase.OpenLab ||
            restored.OpenSteps < s.Environment.CycleLength || restored.CoveredZones < restored.RequiredZones))
            return false;
        state = restored;
        return true;
    }

    private static bool Unit(float n) => n >= 0 && n <= 1;
}

public sealed record WorldSnapshot(int Version, Vector2 DishSize, Vector2 LampPosition,
    float LampRadius, EnvironmentRules Environment, PlantState[] Plants, Dictionary<string, int> Seeds,
    int FruitCount, int NextPlantId, int Step, int? OpenedAtStep, GamePhase Phase, RunStatus Status,
    string? FailureSpecies, bool GardenReady);
