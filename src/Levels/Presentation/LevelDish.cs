using System;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation;

public partial class LevelDish : Control
{
    public event Action<int>? SlotPressed;
    public event Action<int>? LampRequested;

    private LevelSimulation? _simulation;
    private int _selected = -1;
    private int _hover = -1;
    private bool _dragging;
    private bool _sowing;
    private int _lastTarget = -1;

    private int GridSize => _simulation?.GridSize ?? 1;
    private float HalfWidth => Size.X * .35f / GridSize;
    private float HalfHeight => (Size.Y - 94f) * .5f / GridSize;
    private Vector2 FarCorner => new(Size.X * .5f, 60);
    private static readonly Vector2 BedDepth = new(0, 13);

    // Increasing the column goes down-right, increasing the row goes down-left.
    private Vector2 Project(float column, float row) => FarCorner +
        new Vector2((column - row) * HalfWidth, (column + row) * HalfHeight);

    public Vector2 CellCenter(int slot) => Project(slot % GridSize + .5f, slot / GridSize + .5f);

    public Vector2 LampHandle()
    {
        if (_simulation is not { } sim) return new(Size.X * .5f, 23);
        return new(Project(sim.LampPosition.X + .5f, sim.LampPosition.Y + .5f).X, 23);
    }

    public void Render(LevelSimulation simulation, int selected, bool sowing)
    {
        _simulation = simulation;
        _selected = selected;
        _sowing = sowing;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public void ResetInput()
    {
        _dragging = false;
        _lastTarget = _hover = -1;
    }

    private int SlotAt(Vector2 local)
    {
        var plane = local - FarCorner;
        float x = plane.X / HalfWidth;
        float y = plane.Y / HalfHeight;
        int col = Mathf.FloorToInt((y + x) * .5f);
        int row = Mathf.FloorToInt((y - x) * .5f);
        return col >= 0 && col < GridSize && row >= 0 && row < GridSize ? row * GridSize + col : -1;
    }

    private void TargetAt(int slot)
    {
        if (_simulation == null || slot < 0) return;
        int target = _simulation.ClampLampTarget(slot);
        if (target < 0 || target == _lastTarget) return;
        _lastTarget = target;
        LampRequested?.Invoke(target);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_simulation == null || _simulation.Status != LevelRunStatus.Playing) return;
        if (input is InputEventMouseMotion motion)
        {
            _hover = SlotAt(motion.Position);
            if (_dragging) TargetAt(_hover);
            QueueRedraw();
            return;
        }
        if (input is not InputEventMouseButton button) return;
        int slot = SlotAt(button.Position);
        if (button.ButtonIndex == MouseButton.Right && button.Pressed && slot >= 0)
        {
            _lastTarget = -1;
            TargetAt(slot);
            AcceptEvent();
        }
        if (button.ButtonIndex != MouseButton.Left) return;
        if (!button.Pressed)
        {
            if (_dragging)
            {
                TargetAt(slot);
                _dragging = false;
                AcceptEvent();
                QueueRedraw();
            }
            return;
        }
        if (new Rect2(LampHandle() - new Vector2(30, 14), new Vector2(60, 28)).HasPoint(button.Position))
        {
            _lastTarget = -1;
            _dragging = true;
            AcceptEvent();
        }
        else if (slot >= 0)
        {
            SlotPressed?.Invoke(slot);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_simulation is not { } sim) return;
        Rect2 viewport = new(Vector2.Zero, Size);
        DrawRect(viewport, new Color("172b2c"));
        DrawRect(viewport.Grow(-.8f), new Color("557067"), false, 1);
        DrawRect(viewport.Grow(-5), new Color("314947"), false, 1);
        DrawSetTransform(new(Size.X * .5f, FarCorner.Y + (GridSize + .7f) * HalfHeight), 0,
            new Vector2(1, .28f));
        DrawCircle(Vector2.Zero, GridSize * HalfWidth * .97f, new Color("0e242448"), true, -1, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawBed(sim);
        DrawLight(sim);
        // Every plot surface is already painted; stems in front overlap those behind.
        for (int depth = 0; depth <= 2 * (GridSize - 1); depth++)
        {
            for (int row = Math.Max(0, depth - GridSize + 1); row <= Math.Min(depth, GridSize - 1); row++)
            {
                int slot = row * GridSize + depth - row;
                if (sim.Slots[slot] is { } plant)
                    DrawSpecimen(CellCenter(slot), plant, sim.IsLit(slot), slot);
            }
        }
        DrawLabels(sim);
    }

    private Vector2[] Tile(int col, int row, float inset)
    {
        return new[] { Project(col + inset, row + inset), Project(col + 1 - inset, row + inset),
            Project(col + 1 - inset, row + 1 - inset), Project(col + inset, row + 1 - inset) };
    }

    private void DrawBed(LevelSimulation sim)
    {
        var back = FarCorner;
        var right = Project(GridSize, 0);
        var front = Project(GridSize, GridSize);
        var left = Project(0, GridSize);
        var shadow = new Vector2(0, 19);
        DrawColoredPolygon(new[] { back + shadow, right + shadow, front + shadow, left + shadow }, new Color("0c201f"));
        DrawColoredPolygon(new[] { left, front, front + BedDepth, left + BedDepth }, new Color("635447"));
        DrawColoredPolygon(new[] { front, right, right + BedDepth, front + BedDepth }, new Color("443d34"));
        DrawPolyline(new[] { left + BedDepth * .66f, front + BedDepth * .66f, right + BedDepth * .66f },
            new Color("877054"), 1.4f, true);
        DrawPolyline(new[] { left + BedDepth, front + BedDepth, right + BedDepth }, new Color("749183"), 1, true);
        for (int i = 1; i < GridSize; i++)
        {
            Vector2 a = Project(i, GridSize), b = Project(GridSize, i);
            DrawLine(a, a + BedDepth, new Color("2f423a"), 1);
            DrawLine(b, b + BedDepth, new Color("233e35"), 1);
        }
        DrawColoredPolygon(new[] { back, right, front, left }, new Color("383d32"));
        var border = new Color("a1a38a");
        DrawPolyline(new[] { back, right, front, left, back }, border, 2, true);
        for (int i = 0; i < sim.SlotCount; i++) DrawSoil(sim, i);
    }

    private void DrawSoil(LevelSimulation sim, int slot)
    {
        int row = slot / GridSize, col = slot % GridSize;
        bool lit = sim.IsLit(slot);
        var fill = lit ? new Color("9a7751") : (row + col) % 2 == 0 ? new Color("5e503d") : new Color("655641");
        var tile = Tile(col, row, .02f);
        DrawColoredPolygon(tile, fill);
        // Shallow furrows lead the eye along the column axis.
        var groove = lit ? new Color("71573c") : new Color("453e32");
        var ridge = lit ? new Color("c4a06b") : new Color("837353");
        for (int f = 0; f < 3; f++)
        {
            float z = .22f + f * .27f;
            var a = Project(col + .12f, row + z);
            var b = Project(col + .86f, row + z);
            var mid = Project(col + .48f, row + z + .015f);
            DrawPolyline(new[] { a, mid, b }, groove, 2, true);
            DrawPolyline(new[] { a + Vector2.Down, mid + Vector2.Down, b + Vector2.Down }, ridge, 1, true);
        }
        // Fixed seed-like grain on the soil avoids noise flickering when the lamp moves.
        for (int grain = 0; grain < 12; grain++)
        {
            float u = .10f + ((slot * 31 + grain * 23) % 83) * .009f;
            float v = .10f + ((grain * grain * 13 + slot * 19 + grain) % 79) * .01f;
            var dot = Project(col + u, row + v);
            DrawLine(dot, dot + new Vector2(grain % 3 + 1, -.5f),
                grain % 3 == 0 ? ridge : groove, 1, true);
        }
        Color outline = slot == _selected ? new Color("e9f3d6") :
            slot == _hover ? new Color("b1e3c0") : lit ? new Color("f0ce85") : new Color("939277");
        DrawPolyline(new[] { tile[0], tile[1], tile[2], tile[3], tile[0] }, outline,
            slot == _selected || slot == _hover ? 2.3f : 1, true);
        var center = CellCenter(slot);
        if (sim.Slots[slot] != null)
        {
            DrawSetTransform(center, 0, new Vector2(1, .32f));
            DrawCircle(Vector2.Zero, Math.Min(14, HalfWidth / 3), new Color("1d302b9c"), true, -1, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else if (_sowing && slot == _hover && !_dragging)
        {
            DrawArc(center, 10, 0, Mathf.Tau, 20, new Color("e5f1cf"), 1.3f, true);
            DrawColoredPolygon(new[] { center + new Vector2(-4, 2), center + new Vector2(-2, -5),
                center + new Vector2(4, -6), center + new Vector2(6, 0) }, new Color("f1d49b"));
        }
    }

    private void DrawLight(LevelSimulation sim)
    {
        var head = LampHandle();
        var near = Project(sim.LampPosition.X + .5f, sim.LampPosition.Y + .5f);
        var forward = sim.Direction switch
        {
            BeamDirection.East => new Vector2(HalfWidth, HalfHeight),
            BeamDirection.South => new Vector2(-HalfWidth, HalfHeight),
            BeamDirection.West => new Vector2(-HalfWidth, -HalfHeight),
            _ => new Vector2(HalfWidth, -HalfHeight)
        };
        var center = near + forward * (sim.Level.LightRange - 1) / 2f;
        float lowerRadius = HalfWidth * .45f;
        DrawPolygon(new[] { head + new Vector2(-14, 7), head + new Vector2(14, 7),
            center + new Vector2(lowerRadius, 1), center - new Vector2(lowerRadius, 0) },
            new[] { new Color("f6db9150"), new Color("f6db9150"), new Color("fff3af04"), new Color("fff3af04") });
        // A direction arrow at the anchor disambiguates the illuminated pair.
        var axis = forward.Normalized();
        var side = axis.Orthogonal();
        var arrow = near + axis * Math.Min(21, HalfWidth * .34f);
        DrawColoredPolygon(new[] { arrow + axis * 5, arrow - axis * 3 + side * 3, arrow - axis * 3 - side * 3 },
            new Color("fff0a8"));
        if (sim.IsLampMoving || _dragging)
        {
            var target = CellCenter(sim.LampTarget);
            DrawArc(target, 8, 0, Mathf.Tau, 20, new Color("f5db91"), 1.3f, true);
        }
        DrawLine(new(58, 12), new(Size.X - 58, 12), new Color("536e68"), 2, true);
        DrawLine(new(head.X, 12), head, new Color("a7baba"), 2, true);
        DrawColoredPolygon(new[] { head + new Vector2(-23, -8), head + new Vector2(15, -8),
            head + new Vector2(23, -4), head + new Vector2(-17, -4) }, new Color("bba67c"));
        DrawRect(new Rect2(head - new Vector2(23, 4), new Vector2(46, 10)), new Color("79816f"));
        DrawRect(new Rect2(head - new Vector2(20, 3), new Vector2(40, 6)), new Color("3b4c44"));
        for (int i = -2; i <= 2; i++) DrawLine(head + new Vector2(i * 5, -2), head + new Vector2(i * 5, 2), new Color("cca96e"), 1);
        DrawRect(new Rect2(head + new Vector2(-18, 6), new Vector2(36, 3)), new Color("f8d994"));
    }

    private void DrawLabels(LevelSimulation sim)
    {
        var font = GetThemeDefaultFont();
        var muted = new Color("a1b7a8");
        for (int i = 0; i < GridSize; i++)
        {
            var row = Project(0, i + .5f) + new Vector2(-39, -3);
            var col = Project(i + .5f, 0) + new Vector2(7, -3);
            DrawString(font, row, $"{i + 1}行", HorizontalAlignment.Right, 32, 13, muted);
            DrawString(font, col, $"{i + 1}列", HorizontalAlignment.Left, 32, 13, muted);
        }
        float x = Size.X - 105, y = Size.Y - 52;
        DrawRect(new Rect2(x, y - 9, 12, 8), new Color("dbbb7a"));
        DrawString(font, new(x + 20, y), "受光", HorizontalAlignment.Left, -1, 14, new Color("e9d6a0"));
        DrawRect(new Rect2(x, y + 12, 12, 8), new Color("685843"));
        DrawString(font, new(x + 20, y + 20), "背光", HorizontalAlignment.Left, -1, 14, muted);
        DrawAxes(font);
    }

    private void DrawAxes(Font font)
    {
        var p = new Vector2(73, Size.Y - 58);
        foreach (var item in new[] { (new Vector2(22, 10), "D"), (new Vector2(-22, 10), "S"),
            (new Vector2(-22, -10), "A"), (new Vector2(22, -10), "W") })
        {
            DrawLine(p, p + item.Item1 * .72f, new Color("7f9c8c"), 1, true);
            DrawString(font, p + item.Item1 - new Vector2(7, -4), item.Item2,
                HorizontalAlignment.Center, 17, 13, new Color("b8cbb9"));
        }
    }
    public static string StageName(GrowthStage stage) => stage switch
    {
        GrowthStage.Seed => "种子",
        GrowthStage.Seedling => "幼苗",
        GrowthStage.Normal => "常规",
        GrowthStage.Mother => "母体",
        _ => ""
    };

    private void DrawSpecimen(Vector2 root, LevelPlant plant, bool lit, int index)
    {
        var dark = new Color("628e77");
        var leaf = lit ? new Color("a1c788") : dark;
        if (plant.Wilted) leaf = new Color("bd9474");
        var highlight = leaf.Lightened(.23f);
        float bend = plant.Wilted ? 13 : 0;
        if (plant.Stage == GrowthStage.Seed)
        {
            DrawColoredPolygon(new[] { root + new Vector2(-6, -2), root + new Vector2(-4, -8),
                root + new Vector2(2, -10), root + new Vector2(7, -5), root + new Vector2(4, -1) },
                new Color(lit ? "d5bc82" : "a8a487"));
            DrawLine(root + new Vector2(-3, -4), root + new Vector2(2, -7), new Color("f0dbab"), 1, true);
            return;
        }
        float scale = Mathf.Min(.72f, HalfHeight / 52f);
        bool grown = plant.Stage != GrowthStage.Seedling;
        DrawSetTransform(root, 0, Vector2.One * scale);
        int stems = grown ? 3 : 1;
        for (int i = 0; i < stems; i++)
        {
            float side = i == 0 ? 0 : i == 1 ? -1 : 1;
            float height = grown ? (i == 0 ? 96 : 62 + index % 3 * 3) : 33;
            var tip = new Vector2(side * 26 + bend, -height + bend * 1.7f);
            var joint = new Vector2(side * 9, -height * .51f);
            DrawPolyline(new[] { Vector2.Zero, joint, tip }, leaf, grown ? 2.9f : 2, true);
            for (int j = 0; j < (grown ? 3 : 2); j++)
            {
                float t = .18f + j * .28f;
                var start = joint.Lerp(tip, t);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var delta = new Vector2(sign * (grown ? 12 : 9), (plant.Wilted ? 4 : -7) - 1.8f * j);
                    Leaf(start, delta, sign < 0 ? leaf : highlight);
                }
            }
            if (grown)
            {
                var flower = plant.Wilted ? new Color("ae877d") : new Color(lit ? "cfadba" : "aa95b0");
                for (int k = 0; k < 5; k++)
                {
                    var petal = tip + new Vector2((k % 2 == 0 ? -1 : 1) * (4 - .55f * k), -2 * k);
                    DrawCircle(petal, 3.6f, flower.Lightened(k * .025f), true, -1, true);
                }
                if (plant.Stage == GrowthStage.Mother)
                    DrawCircle(tip + new Vector2(0, -12), 3.5f, new Color("f0d68d"), true, -1, true);
            }
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void Leaf(Vector2 start, Vector2 span, Color fill)
    {
        Vector2 mid = start + span * .55f;
        Vector2 width = span.Orthogonal().Normalized() * 3.2f;
        DrawColoredPolygon(new[] { start, mid + width, start + span, mid - width }, fill);
    }
}
