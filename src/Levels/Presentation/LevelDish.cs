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
    private const float TrackY = 39;
    private const float SoilY = 206;
    private const float Inset = 28;

    private float CellWidth => (Size.X - Inset * 2) / (_simulation?.SlotCount ?? 1);
    public Vector2 CellCenter(int slot) => new(Inset + (slot + .5f) * CellWidth, SoilY - 21);
    private float LampX(double left) => Inset + ((float)left + (_simulation?.Level.LightRange ?? 1) / 2f) * CellWidth;
    public Vector2 RailPoint(int leftSlot) => new(LampX(leftSlot), TrackY);

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
        _lastTarget = -1;
        _hover = -1;
    }

    private int SlotAt(Vector2 position)
    {
        if (_simulation == null || position.Y < 71 || position.Y > Size.Y - 9 ||
            position.X < Inset || position.X >= Size.X - Inset) return -1;
        return Mathf.FloorToInt((position.X - Inset) / CellWidth);
    }

    private void TargetLampAt(float localX)
    {
        if (_simulation == null) return;
        int target = Mathf.Clamp(Mathf.RoundToInt((localX - Inset) / CellWidth - _simulation.Level.LightRange / 2f),
            0, _simulation.SlotCount - _simulation.Level.LightRange);
        if (target == _lastTarget) return;
        _lastTarget = target;
        LampRequested?.Invoke(target);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_simulation == null || _simulation.Status != LevelRunStatus.Playing) return;
        if (input is InputEventMouseMotion motion)
        {
            if (_dragging) TargetLampAt(motion.Position.X);
            _hover = SlotAt(motion.Position);
            QueueRedraw();
            return;
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return;
        if (!button.Pressed)
        {
            _dragging = false;
            return;
        }
        if (button.Position.Y < 71)
        {
            _lastTarget = -1;
            _dragging = true;
            TargetLampAt(button.Position.X);
        }
        else if (SlotAt(button.Position) is var slot && slot >= 0)
            SlotPressed?.Invoke(slot);
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (_simulation is not { } sim) return;
        Rect2 bounds = new(Vector2.Zero, Size);
        DrawRect(bounds, new Color("182f30"));
        DrawRect(bounds.Grow(-1), new Color("57756a"), false, 1);
        DrawRect(bounds.Grow(-5), new Color("324c44"), false, 1);
        DrawChamber();

        float center = LampX(sim.LampPosition);
        float tilt = sim.IsLampMoving ? Mathf.Clamp((float)(sim.LampTarget - sim.LampPosition) * 9, -17, 17) : 0;
        float lamp = center - tilt;
        float lampHalf = Mathf.Min(42, CellWidth * .3f);
        float lightEdge = CellWidth * sim.Level.LightRange / 2f;
        DrawPolygon(new[] { new Vector2(lamp - lampHalf, 56), new(lamp + lampHalf, 56),
                new(center + lightEdge, SoilY + 12), new(center - lightEdge, SoilY + 12) },
            new[] { new Color("ffe7a656"), new Color("ffe7a656"), new Color("efdc9523"), new Color("efdc9523") });
        DrawSetTransform(new(center, SoilY + 6), 0, new Vector2(1, 11 / lightEdge));
        DrawCircle(Vector2.Zero, lightEdge, new Color("f2dda527"), true, -1, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawLine(new(Inset, TrackY - 1), new(Size.X - Inset, TrackY - 1), new Color("759086"), 4, true);
        DrawLine(new(Inset, TrackY + 2), new(Size.X - Inset, TrackY + 2), new Color("364c48"), 2, true);
        for (int i = 0; i <= sim.SlotCount - sim.Level.LightRange; i++)
        {
            Vector2 point = RailPoint(i);
            DrawLine(point - Vector2.Down * 5, point + Vector2.Down * 5, new Color("a1b4a5"), 1, true);
        }
        if (sim.IsLampMoving)
        {
            var target = RailPoint(sim.LampTarget);
            DrawArc(target, 9, 0, Mathf.Tau, 22, new Color("efce8e"), 1.4f, true);
            DrawDashedLine(new(lamp, TrackY - 11), target - new Vector2(0, 11), new Color("dcc794"), 1, 5);
        }
        DrawRect(new Rect2(lamp - lampHalf, TrackY - 9, lampHalf * 2, 18), new Color("9d8565"));
        DrawRect(new Rect2(lamp - lampHalf + 2, TrackY - 7, lampHalf * 2 - 4, 12), new Color("34423d"));
        for (int i = -2; i <= 2; i++)
            DrawLine(new(lamp + i * 5, TrackY - 3), new(lamp + i * 5, TrackY + 4), new Color("c2b78e"), 1.5f);
        DrawRect(new Rect2(lamp - lampHalf + 3, TrackY + 8, lampHalf * 2 - 6, 4), new Color("ffe4a0"));
        var font = GetThemeDefaultFont();
        for (int i = 0; i < sim.SlotCount; i++)
            DrawSlot(sim, i, font);
    }

    private void DrawChamber()
    {
        // Glass fasteners, mounting gauge and the recessed bench identify the playable plane.
        var metal = new Color("769184");
        foreach (float x in new[] { 13f, Size.X - 13 })
        {
            foreach (float y in new[] { 13f, Size.Y - 13 })
            {
                DrawCircle(new(x, y), 3, new Color("34524e"), true, -1, true);
                DrawLine(new(x - 1, y + 1), new(x + 1, y - 1), metal, 1, true);
            }
        }
        for (int i = 0; i <= 40; i++)
        {
            float x = Inset + (Size.X - 2 * Inset) * i / 40;
            float h = i % 5 == 0 ? 5 : 2;
            DrawLine(new(x, TrackY - 11 - h), new(x, TrackY - 11), new Color("9db5a747"), 1);
        }
        DrawPolygon(new[] { new Vector2(Inset + 14, SoilY - 14), new(Size.X - Inset - 14, SoilY - 14),
                new(Size.X - Inset + 1, SoilY + 7), new(Inset - 1, SoilY + 7) },
            new[] { new Color("29433d"), new Color("29433d"), new Color("203a34"), new Color("203a34") });
        DrawLine(new(Inset + 14, SoilY - 14), new(Size.X - Inset - 14, SoilY - 14), new Color("6e867549"), 1);
        for (int i = 0; i < 3; i++)
        {
            float x = Inset + 32 + i * 10;
            DrawLine(new(x, 70), new(x + 32, 54), new Color("bbd6c716"), 1, true);
        }
    }

    private void DrawSlot(LevelSimulation sim, int index, Font font)
    {
        bool lit = sim.IsLit(index);
        var plant = sim.Slots[index];
        float x = CellCenter(index).X;
        float width = CellWidth - 6;
        Color edge = index == _selected ? new Color("e6d49e") :
            index == _hover ? new Color("9ebba5") : new Color("57776e");
        var plate = new Rect2(x - width / 2, SoilY + 6, width, 39);
        DrawRect(plate, lit ? new Color("354e3f") : new Color("2a4240"));
        DrawRect(plate, edge, false, index == _selected ? 2 : 1);
        DrawLine(new(x - width / 2 + 3, SoilY + 6), new(x + width / 2 - 3, SoilY + 6),
            lit ? new Color("e7cb83") : new Color("658477"), 3, true);
        for (int i = 1; i <= 4; i++)
        {
            float grainX = x - width / 2 + width * i / 5f;
            DrawLine(new(grainX - 2, SoilY + 13 + i % 3), new(grainX + 2, SoilY + 13 + i % 3),
                new Color("b5c4a547"), 1);
        }
        if (index == _selected)
            DrawArc(new(x, SoilY - 1), Mathf.Min(21, width * .3f), .2f, Mathf.Pi - .2f, 22,
                new Color("e6d49e"), 1.5f, true);
        if (plant == null && _sowing && index == _hover)
        {
            DrawArc(new(x, SoilY - 5), 12, 0, Mathf.Tau, 22, new Color("e1d5a1"), 1, true);
            DrawColoredPolygon(new[] { new Vector2(x - 5, SoilY - 4), new(x - 2, SoilY - 10),
                new(x + 5, SoilY - 9), new(x + 6, SoilY - 4), new(x, SoilY - 2) }, new Color("dcc88ccc"));
        }
        else if (plant == null)
        {
            var faint = lit ? new Color("b7bc90") : new Color("78988a");
            DrawLine(new(x-4, SoilY-5), new(x+4, SoilY-5), faint, 1);
            DrawLine(new(x, SoilY-9), new(x, SoilY-1), faint, 1);
        }
        else DrawSpecimen(new(x, SoilY), plant, lit, index);
        string name = plant == null ? "空槽" : StageName(plant.Stage);
        if (plant?.Wilted == true) name = "萎蔫";
        DrawString(font, new(x - width / 2, SoilY + 35), $"{index + 1}  {name}",
            HorizontalAlignment.Center, width, CellWidth < 90 ? 13 : 16, new Color("e1e8d5"));
        string light = lit ? "受光" : "背光";
        DrawString(font, new(x - width / 2, SoilY + 64), light,
            HorizontalAlignment.Center, width, 13, lit ? new Color("e0c889") : new Color("9db2a8"));
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
        float scale = Mathf.Min(1f, CellWidth / 82f);
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
