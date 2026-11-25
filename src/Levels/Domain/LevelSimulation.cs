using System;
using System.Collections.Generic;
using System.Linq;

namespace OneBeam.Levels.Domain;

public sealed class LevelSimulation
{
    private const double Epsilon = 1e-9;
    private readonly LevelPlant?[] _slots;
    private LevelPlant?[] _entryPlants;
    private int _entrySeeds;
    private double _entryLamp;
    private int _entryLampTarget;

    public LevelDefinition Level { get; private set; }
    public GrowthRules Rules { get; }
    public int SlotCount => _slots.Length;
    public IReadOnlyList<LevelPlant?> Slots { get; }
    public LevelRunStatus Status { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double RemainingSeconds => Level.DurationSeconds - ElapsedSeconds;
    public double LampPosition { get; private set; }
    public int LampTarget { get; private set; }
    public bool IsLampMoving => Math.Abs(LampPosition - LampTarget) > Epsilon;
    public int AvailableSeeds { get; private set; }
    public double Vitality => _slots.Sum(p => p == null ? 0 :
        Rules.Stages[(int)p.Stage].Vitality * (p.Wilted ? Rules.WiltMultiplier : 1));

    public LevelSimulation(GrowthRules rules, LevelDefinition level, int slotCount)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (level == null || !level.IsValidFor(slotCount)) throw new ArgumentException("Invalid level.", nameof(level));
        Level = level;
        Rules = rules with { Stages = Array.AsReadOnly(rules.Stages.ToArray()) };
        _slots = new LevelPlant?[slotCount];
        Slots = Array.AsReadOnly(_slots);
        _entryPlants = new LevelPlant?[slotCount];
        AvailableSeeds = _entrySeeds = level.AddedSeeds;
    }

    public bool IsLit(int slot) => IsLit(slot, LampPosition);

    private bool IsLit(int slot, double position)
    {
        int left = (int)Math.Floor(position + .5);
        return slot >= left && slot < left + Level.LightRange && slot < SlotCount;
    }

    public bool TryPlant(int slot)
    {
        if (Status != LevelRunStatus.Playing || AvailableSeeds == 0 ||
            slot < 0 || slot >= SlotCount || _slots[slot] != null) return false;
        AvailableSeeds--;
        _slots[slot] = new LevelPlant();
        return true;
    }

    public bool TryMoveLamp(int targetLeftSlot)
    {
        if (Status != LevelRunStatus.Playing || targetLeftSlot < 0 ||
            targetLeftSlot > SlotCount - Level.LightRange || targetLeftSlot == LampTarget) return false;
        LampTarget = targetLeftSlot;
        return true;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Status != LevelRunStatus.Playing || seconds == 0) return;
        double remaining = Math.Min(seconds, RemainingSeconds);
        double end = ElapsedSeconds + remaining;
        while (remaining > 0)
        {
            double slice = remaining;
            double next = LampPosition;
            if (IsLampMoving)
            {
                int direction = Math.Sign(LampTarget - LampPosition);
                // Split exactly where the moving beam changes the set of illuminated slots.
                double boundary = direction > 0 ? Math.Floor(LampPosition + .5) + .5 :
                    Math.Ceiling(LampPosition - .5) - .5;
                double destination = direction > 0 ? Math.Min(boundary, LampTarget) : Math.Max(boundary, LampTarget);
                double travel = Math.Abs(destination - LampPosition) * Rules.MoveSecondsPerSlot;
                slice = Math.Min(remaining, travel);
                next = slice == travel ? destination : LampPosition + direction * slice / Rules.MoveSecondsPerSlot;
            }

            double sample = (LampPosition + next) / 2;
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] is { } plant)
                    _slots[i] = AdvancePlant(plant, IsLit(i, sample), slice);
            LampPosition = next;
            remaining -= slice;
        }
        ElapsedSeconds = end;
        if (RemainingSeconds <= Epsilon)
        {
            ElapsedSeconds = Level.DurationSeconds;
            Status = Vitality >= Level.TargetVitality ? LevelRunStatus.Won : LevelRunStatus.Lost;
        }
    }

    private LevelPlant? AdvancePlant(LevelPlant plant, bool lit, double seconds)
    {
        StageRule stage = Rules.Stages[(int)plant.Stage];
        if (!lit)
        {
            double dark = plant.Darkness + seconds * stage.DarkPerSecond;
            if (dark >= stage.DieAt - Epsilon) return null;
            return plant with { Darkness = dark, Wilted = plant.Wilted || dark >= stage.WiltAt - Epsilon };
        }

        double darkness = Math.Max(0, plant.Darkness + seconds * Rules.LightDarkPerSecond);
        if (plant.Wilted)
        {
            if (darkness > Rules.RecoverDark + Epsilon) return plant with { Darkness = darkness };
            double recovering = Math.Max(0, plant.Darkness - Rules.RecoverDark) / -Rules.LightDarkPerSecond;
            seconds = Math.Max(0, seconds - recovering);
        }

        var stageId = plant.Stage;
        double growth = plant.Growth + seconds * Rules.LightGrowthPerSecond;
        while (stageId != GrowthStage.Mother && growth + Epsilon >= Rules.Stages[(int)stageId].GrowthToNext!.Value)
        {
            growth = Math.Max(0, growth - Rules.Stages[(int)stageId].GrowthToNext!.Value);
            darkness = Math.Max(0, darkness - Rules.StageDarkRelief);
            stageId++;
        }

        int seedsProduced = plant.SeedsProduced;
        if (stageId == GrowthStage.Mother)
        {
            double threshold = Rules.FirstSeedGrowth + seedsProduced * Rules.SeedGrowthIncrement;
            while (growth + Epsilon >= threshold)
            {
                growth = Math.Max(0, growth - threshold);
                seedsProduced++;
                AvailableSeeds += Rules.SeedsPerHarvest;
                threshold = Rules.FirstSeedGrowth + seedsProduced * Rules.SeedGrowthIncrement;
            }
        }
        return new LevelPlant(stageId, growth, darkness, false, seedsProduced);
    }

    public bool TryStartNext(LevelDefinition next)
    {
        if (Status != LevelRunStatus.Won || next == null || next.Number != Level.Number + 1 ||
            !next.IsValidFor(SlotCount)) return false;
        Level = next;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] is { } p) _slots[i] = p with { Darkness = 0, Wilted = false };
        _entryPlants = (LevelPlant?[])_slots.Clone();
        _entrySeeds = AvailableSeeds + next.AddedSeeds;
        _entryLamp = Math.Min(LampPosition, SlotCount - next.LightRange);
        _entryLampTarget = Math.Min(LampTarget, SlotCount - next.LightRange);
        RestartCurrent();
        return true;
    }

    public void RestartCurrent()
    {
        Array.Copy(_entryPlants, _slots, SlotCount);
        AvailableSeeds = _entrySeeds;
        LampPosition = _entryLamp;
        LampTarget = _entryLampTarget;
        ElapsedSeconds = 0;
        Status = LevelRunStatus.Playing;
    }
}
