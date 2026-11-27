using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OneBeam.Levels.Domain;

public sealed class LevelSimulation
{
    private const double Epsilon = 1e-9;
    private readonly LevelPlant?[] _slots;
    private readonly LevelRemains?[] _remains;
    private LevelPlant?[] _entryPlants;
    private LevelRemains?[] _entryRemains;
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
    /// <summary>Withered remains per slot (only with GrowthRules.LeaveRemains). A slot holds a plant or remains, never both.</summary>
    public IReadOnlyList<LevelRemains?> Remains { get; }
    /// <summary>
    /// Reward points pay for the shovel (GrowthRules.ShovelCost). Nothing earns them yet: the
    /// reward system is not designed, so the standard level keeps the cost at 0.
    /// </summary>
    public int RewardPoints { get; private set; }
    public bool IsPitEmpty(int slot) => slot >= 0 && slot < SlotCount && _slots[slot] == null && _remains[slot] == null;
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
        _remains = new LevelRemains?[_slots.Length];
        Remains = Array.AsReadOnly(_remains);
        _entryPlants = new LevelPlant?[_slots.Length];
        _entryRemains = new LevelRemains?[_slots.Length];
        AvailableSeeds = _entrySeeds = level.AddedSeeds;
    }

    private static int Snap(double position) => (int)Math.Floor(position + .5);

    public bool IsLit(int slot) =>
        slot >= 0 && slot < SlotCount && (Rules.EllipseBeam
            ? IsInEllipse(slot, _lampX, _lampY)
            : IsLit(slot, Snap(_lampX), Snap(_lampY)));

    // Q/E order when EllipseBeam is on: 45 degrees per press.
    private static readonly BeamDirection[] EighthTurns =
    {
        BeamDirection.East, BeamDirection.SouthEast, BeamDirection.South, BeamDirection.SouthWest,
        BeamDirection.West, BeamDirection.NorthWest, BeamDirection.North, BeamDirection.NorthEast
    };

    // Longest simulated slice while the ellipse sweeps, so plants it passes over get their share of light.
    private const double SweepSlice = .05;

    public static (int X, int Y) Step(BeamDirection direction) => direction switch
    {
        BeamDirection.East => (1, 0),
        BeamDirection.South => (0, 1),
        BeamDirection.West => (-1, 0),
        BeamDirection.North => (0, -1),
        BeamDirection.SouthEast => (1, 1),
        BeamDirection.SouthWest => (-1, 1),
        BeamDirection.NorthWest => (-1, -1),
        BeamDirection.NorthEast => (1, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    /// <summary>
    /// Ellipse beam: semi-axes LightRange/2 along the beam direction and 0.5 across it. The beam
    /// starts at the anchor cell and reaches LightRange - 1 cells along the direction (a diagonal
    /// step counts as one), so at range 2 it covers the anchor plus one side or diagonal neighbour.
    /// A plant is lit when its cell centre is strictly inside.
    /// </summary>
    private bool IsInEllipse(int slot, double anchorX, double anchorY)
    {
        var (sx, sy) = Step(Direction);
        double reach = (Level.LightRange - 1) * .5;
        double cx = anchorX + sx * reach, cy = anchorY + sy * reach;
        double angle = Math.Atan2(sy, sx);
        double dx = slot % GridSize - cx, dy = slot / GridSize - cy;
        double u = dx * Math.Cos(angle) + dy * Math.Sin(angle);
        double v = -dx * Math.Sin(angle) + dy * Math.Cos(angle);
        double a = Level.LightRange * .5, b = .5;
        return u * u / (a * a) + v * v / (b * b) < 1 - 1e-9;
    }

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
        if (Status != LevelRunStatus.Playing || AvailableSeeds == 0 || !IsPitEmpty(slot)) return false;
        AvailableSeeds--;
        _slots[slot] = new LevelPlant();
        return true;
    }

    /// <summary>
    /// Shovel a live plant or remains out of a pit: it becomes an empty pit, the plant leaves
    /// no remains and no seeds come back. Costs GrowthRules.ShovelCost reward points.
    /// </summary>
    public bool TryShovel(int slot)
    {
        if (Status != LevelRunStatus.Playing || slot < 0 || slot >= SlotCount || IsPitEmpty(slot) ||
            RewardPoints < Rules.ShovelCost) return false;
        RewardPoints -= Rules.ShovelCost;
        _slots[slot] = null;
        _remains[slot] = null;
        return true;
    }

    /// <summary>Harvest a mother plant: GrowthRules.HarvestSeeds seeds, and the plant dies without remains.</summary>
    public bool TryHarvest(int slot)
    {
        if (Status != LevelRunStatus.Playing || slot < 0 || slot >= SlotCount ||
            _slots[slot] is not { Stage: GrowthStage.Mother }) return false;
        _slots[slot] = null;
        AvailableSeeds += Rules.HarvestSeeds;
        return true;
    }

    private (int X, int Y) ClampAnchor(int x, int y, BeamDirection direction)
    {
        int last = GridSize - 1;
        int reach = Level.LightRange - 1;
        var (dx, dy) = Step(direction);
        static int Fit(int value, int sign, int last, int reach) =>
            sign > 0 ? Math.Clamp(value, 0, last - reach) : sign < 0 ? Math.Clamp(value, reach, last) : value;
        return (Fit(x, dx, last, reach), Fit(y, dy, last, reach));
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

    /// <summary>Turns the beam one step: a quarter turn, or 45 degrees when the beam is an ellipse.</summary>
    public bool TryRotateLamp(int quarterTurns)
    {
        if (Status != LevelRunStatus.Playing || quarterTurns is not (-1 or 1)) return false;
        var direction = Rules.EllipseBeam
            ? EighthTurns[(Array.IndexOf(EighthTurns, Direction) + quarterTurns + 8) % 8]
            : (BeamDirection)(((int)Direction + quarterTurns + 4) % 4);
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
            if ((Rules.EllipseBeam || Rules.DiagonalMove) && (_lampX != targetX || _lampY != targetY))
                slice = Math.Min(slice, SweepSlice);
            if (Rules.DiagonalMove)
                (nextX, nextY) = MoveTogether(targetX, targetY, ref slice);
            else if (_lampX != targetX)
                nextX = MoveOneInterval(_lampX, targetX, ref slice);
            else if (_lampY != targetY)
                nextY = MoveOneInterval(_lampY, targetY, ref slice);

            double midX = (_lampX + nextX) / 2, midY = (_lampY + nextY) / 2;
            int sampleX = Snap(midX);
            int sampleY = Snap(midY);
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] is { } plant)
                {
                    _slots[i] = AdvancePlant(plant, Rules.EllipseBeam
                        ? IsInEllipse(i, midX, midY)
                        : IsLit(i, sampleX, sampleY), slice);
                    // Withered to death: leave remains when the rules say so.
                    if (_slots[i] == null && Rules.LeaveRemains)
                        _remains[i] = new LevelRemains(plant.Stage, Level.Number);
                }
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

    /// <summary>
    /// GrowthRules.DiagonalMove: both axes travel at once along a straight line, taking
    /// MoveSecondsPerSlot per cell of the longer axis, so a diagonal step takes as long as a
    /// straight one.
    /// </summary>
    private (double X, double Y) MoveTogether(int targetX, int targetY, ref double seconds)
    {
        double dx = targetX - _lampX, dy = targetY - _lampY;
        double travel = Math.Max(Math.Abs(dx), Math.Abs(dy)) * Rules.MoveSecondsPerSlot;
        if (travel <= 0) return (_lampX, _lampY);
        if (seconds >= travel)
        {
            seconds = travel;
            return (targetX, targetY);
        }
        double f = seconds / travel;
        return (_lampX + dx * f, _lampY + dy * f);
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
        for (int i = 0; i < SlotCount; i++)
            if (_remains[i] is { } r && next.Number - r.Day >= Rules.RemainsDays) _remains[i] = null;
        _entryPlants = (LevelPlant?[])_slots.Clone();
        _entryRemains = (LevelRemains?[])_remains.Clone();
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
        Array.Copy(_entryRemains, _remains, SlotCount);
        AvailableSeeds = _entrySeeds;
        (_lampX, _lampY) = _entryLamp;
        LampTarget = _entryLampTarget;
        Direction = _entryDirection;
        ElapsedSeconds = 0;
        Status = LevelRunStatus.Playing;
    }
}
