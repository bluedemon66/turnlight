using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OneBeam.Levels.Domain;

public sealed class LevelSimulation
{
    private const double Epsilon = 1e-9;
    private readonly LevelPlant?[] _slots;
    private LevelPlant?[] _entryPlants;
    private int _entrySeeds;
    private double _lampX;
    private double _lampY;
    private (double X, double Y) _entryLamp;
    private int _entryLampTarget;
    private BeamDirection _entryDirection;

    public LevelDefinition Level { get; private set; }
    public GrowthRules Rules { get; }
    public int GridSize { get; }
    public int SlotCount => _slots.Length;
    public IReadOnlyList<LevelPlant?> Slots { get; }
    public LevelRunStatus Status { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double RemainingSeconds => Level.DurationSeconds - ElapsedSeconds;
    public Vector2 LampPosition => new((float)_lampX, (float)_lampY);
    public int LampTarget { get; private set; }
    public BeamDirection Direction { get; private set; }
    public bool IsLampMoving => _lampX != LampTarget % GridSize || _lampY != LampTarget / GridSize;
    public int AvailableSeeds { get; private set; }
    public double Vitality => _slots.Sum(p => p == null ? 0 :
        Rules.Stages[(int)p.Stage].Vitality * (p.Wilted ? Rules.WiltMultiplier : 1));

    public LevelSimulation(GrowthRules rules, LevelDefinition level, int gridSize)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (gridSize <= 0 || (long)gridSize * gridSize > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        if (level == null || !level.IsValidFor(gridSize)) throw new ArgumentException("Invalid level.", nameof(level));
        GridSize = gridSize;
        Level = level;
        Rules = rules with { Stages = Array.AsReadOnly(rules.Stages.ToArray()) };
        _slots = new LevelPlant?[gridSize * gridSize];
        Slots = Array.AsReadOnly(_slots);
        _entryPlants = new LevelPlant?[_slots.Length];
        AvailableSeeds = _entrySeeds = level.AddedSeeds;
    }

    private static int Snap(double position) => (int)Math.Floor(position + .5);

    public bool IsLit(int slot) =>
        slot >= 0 && slot < SlotCount && IsLit(slot, Snap(_lampX), Snap(_lampY));

    private bool IsLit(int slot, int anchorX, int anchorY)
    {
        int dx = slot % GridSize - anchorX;
        int dy = slot / GridSize - anchorY;
        return Direction switch
        {
            BeamDirection.East => dy == 0 && dx >= 0 && dx < Level.LightRange,
            BeamDirection.South => dx == 0 && dy >= 0 && dy < Level.LightRange,
            BeamDirection.West => dy == 0 && dx <= 0 && -dx < Level.LightRange,
            BeamDirection.North => dx == 0 && dy <= 0 && -dy < Level.LightRange,
            _ => false
        };
    }

    public bool TryPlant(int slot)
    {
        if (Status != LevelRunStatus.Playing || AvailableSeeds == 0 ||
            slot < 0 || slot >= SlotCount || _slots[slot] != null) return false;
        AvailableSeeds--;
        _slots[slot] = new LevelPlant();
        return true;
    }

    private (int X, int Y) ClampAnchor(int x, int y, BeamDirection direction)
    {
        int last = GridSize - 1;
        int reach = Level.LightRange - 1;
        return direction switch
        {
            BeamDirection.East => (Math.Clamp(x, 0, last - reach), y),
            BeamDirection.South => (x, Math.Clamp(y, 0, last - reach)),
            BeamDirection.West => (Math.Clamp(x, reach, last), y),
            BeamDirection.North => (x, Math.Clamp(y, reach, last)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
    }

    public int ClampLampTarget(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return -1;
        var anchor = ClampAnchor(slot % GridSize, slot / GridSize, Direction);
        return anchor.X + anchor.Y * GridSize;
    }

    public bool TryMoveLamp(int target)
    {
        if (Status != LevelRunStatus.Playing || target < 0 || ClampLampTarget(target) != target ||
            target == LampTarget) return false;
        LampTarget = target;
        return true;
    }

    public bool TryRotateLamp(int quarterTurns)
    {
        if (Status != LevelRunStatus.Playing || quarterTurns is not (-1 or 1)) return false;
        var direction = (BeamDirection)(((int)Direction + quarterTurns + 4) % 4);
        var current = (X: Snap(_lampX), Y: Snap(_lampY));
        var target = (X: LampTarget % GridSize, Y: LampTarget / GridSize);
        if (ClampAnchor(current.X, current.Y, direction) != current ||
            ClampAnchor(target.X, target.Y, direction) != target) return false;
        Direction = direction;
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
            double nextX = _lampX;
            double nextY = _lampY;
            int targetX = LampTarget % GridSize;
            int targetY = LampTarget / GridSize;
            if (_lampX != targetX)
                nextX = MoveOneInterval(_lampX, targetX, ref slice);
            else if (_lampY != targetY)
                nextY = MoveOneInterval(_lampY, targetY, ref slice);

            int sampleX = Snap((_lampX + nextX) / 2);
            int sampleY = Snap((_lampY + nextY) / 2);
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] is { } plant)
                    _slots[i] = AdvancePlant(plant, IsLit(i, sampleX, sampleY), slice);
            _lampX = nextX;
            _lampY = nextY;
            remaining -= slice;
        }
        ElapsedSeconds = end;
        if (RemainingSeconds <= Epsilon)
        {
            ElapsedSeconds = Level.DurationSeconds;
            Status = Vitality >= Level.TargetVitality ? LevelRunStatus.Won : LevelRunStatus.Lost;
        }
    }

    private double MoveOneInterval(double position, int target, ref double seconds)
    {
        int sign = Math.Sign(target - position);
        // Each interval ends at the destination or at the next change in illuminated cells.
        double boundary = sign > 0 ? Math.Floor(position + .5) + .5 : Math.Ceiling(position - .5) - .5;
        double destination = sign > 0 ? Math.Min(boundary, target) : Math.Max(boundary, target);
        double travel = Math.Abs(destination - position) * Rules.MoveSecondsPerSlot;
        if (seconds < travel)
            return position + sign * seconds / Rules.MoveSecondsPerSlot;
        seconds = travel;
        return destination;
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
            !next.IsValidFor(GridSize)) return false;
        Level = next;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] is { } p) _slots[i] = p with { Darkness = 0, Wilted = false };
        _entryPlants = (LevelPlant?[])_slots.Clone();
        _entrySeeds = AvailableSeeds + next.AddedSeeds;
        int x = Snap(_lampX);
        int y = Snap(_lampY);
        var anchor = ClampAnchor(x, y, Direction);
        _entryLamp = (anchor.X == x ? _lampX : anchor.X, anchor.Y == y ? _lampY : anchor.Y);
        _entryLampTarget = ClampLampTarget(LampTarget);
        _entryDirection = Direction;
        RestartCurrent();
        return true;
    }

    public void RestartCurrent()
    {
        Array.Copy(_entryPlants, _slots, SlotCount);
        AvailableSeeds = _entrySeeds;
        (_lampX, _lampY) = _entryLamp;
        LampTarget = _entryLampTarget;
        Direction = _entryDirection;
        ElapsedSeconds = 0;
        Status = LevelRunStatus.Playing;
    }
}
