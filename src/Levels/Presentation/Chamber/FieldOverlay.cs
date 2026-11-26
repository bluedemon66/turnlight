using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// Everything drawn on top of the chamber at cell positions: engraved coordinates,
/// selection and destination outlines, the sowing preview, and the plants.
/// It also turns clicks into cell presses.
/// </summary>
public partial class FieldOverlay : Control
{
    public event Action<int>? SlotPressed;

    private ChamberView _view = null!;
    private LevelSimulation? _sim;
    private readonly Dictionary<int, Sprite2D> _plants = new();
    private readonly Node2D _plantLayer = new() { Name = "Plants" };
    private Texture2D _mature = null!, _wilted = null!, _seedling = null!, _seed = null!, _mask = null!;
    private Shader _breath = null!;
    private Font _serif = null!;

    public int Selected { get; set; } = -1;
    public bool Sowing { get; set; }
    public int Hover { get; private set; } = -1;

    public void Setup(ChamberView view, Font serif)
    {
        _view = view;
        _serif = serif;
        _mature = GD.Load<Texture2D>("res://art/plants/plant_alfalfa_mature_640.png");
        _mask = GD.Load<Texture2D>("res://art/plants/plant_alfalfa_mature_mask.png");
        _wilted = GD.Load<Texture2D>("res://art/plants/wilted_mature.png");
        _seedling = GD.Load<Texture2D>("res://art/plants/seedling.png");
        _seed = GD.Load<Texture2D>("res://art/plants/seed.png");
        _breath = GD.Load<Shader>("res://shaders/plant_breath.gdshader");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_plantLayer);
        MouseExited += () => Hover = -1;
    }

    public void Render(LevelSimulation sim)
    {
        _sim = sim;
        SyncPlants(sim);
        QueueRedraw();
    }

    /// <summary>Screen point at the top of the plant in a slot (or the pit center when empty).</summary>
    public Vector2 PlantAnchor(int slot)
    {
        var soil = _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth));
        return _plants.TryGetValue(slot, out var s) && s.Visible
            ? soil - new Vector2(0, PlantHeight(slot) * .6f)
            : soil;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion)
        {
            Hover = _view.SlotAt(motion.Position);
            QueueRedraw();
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
        {
            int slot = _view.SlotAt(button.Position);
            if (slot >= 0)
            {
                SlotPressed?.Invoke(slot);
                AcceptEvent();
            }
        }
    }

    public override void _Draw()
    {
        if (_sim is not { } sim) return;
        int n = sim.GridSize;
        float half = n * .5f;
        var gold = ChamberStyle.Gold;
        for (int i = 0; i < n; i++)
        {
            float x = i - (n - 1) * .5f;
            var top = _view.Screen(new Vector3(x, 0, -half - .2f));
            DrawCentered(((char)('A' + i)).ToString(), top, 24, ChamberStyle.Engrave);
            var left = _view.Screen(new Vector3(-half - .24f, 0, x));
            DrawCentered((i + 1).ToString(), left, 24, ChamberStyle.Engrave);
        }

        if (sim.IsLampMoving && sim.Status == LevelRunStatus.Playing)
            foreach (int slot in Footprint(sim, sim.LampTarget))
                Dashed(_view.PitOutline(slot, .02f), gold with { A = .85f }, 2f);

        if (Sowing && Hover >= 0 && sim.Slots[Hover] == null)
        {
            Dashed(_view.PitOutline(Hover, .02f), gold with { A = .7f }, 1.6f);
            float px = _view.CellPixels(Hover) * .3f;
            var at = _view.Screen(_view.CellCenter(Hover, -ChamberView.PitDepth));
            DrawTextureRect(_seed, new Rect2(at - new Vector2(px, px) * .5f, new Vector2(px, px)), false,
                new Color(1, 1, 1, .45f));
        }

        if (Selected >= 0)
            Dashed(_view.PitOutline(Selected, .03f), gold, 2.4f);
    }

    /// <summary>Cells lit by the lamp when its anchor sits at <paramref name="anchor"/>.</summary>
    public static IEnumerable<int> Footprint(LevelSimulation sim, int anchor)
    {
        int n = sim.GridSize;
        var (dx, dy) = Step(sim.Direction);
        for (int k = 0; k < sim.Level.LightRange; k++)
        {
            int x = anchor % n + dx * k, y = anchor / n + dy * k;
            if (x >= 0 && x < n && y >= 0 && y < n) yield return y * n + x;
        }
    }

    public static (int X, int Y) Step(BeamDirection direction) => LevelSimulation.Step(direction);

    private void DrawCentered(string text, Vector2 at, int size, Color color)
    {
        var dims = _serif.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        DrawString(_serif, at + new Vector2(-dims.X * .5f, dims.Y * .3f), text, HorizontalAlignment.Left, -1, size, color);
    }

    private void Dashed(Vector2[] corners, Color color, float width)
    {
        const float dash = 9, gap = 6;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 a = corners[i], b = corners[(i + 1) % corners.Length];
            float length = a.DistanceTo(b);
            Vector2 dir = (b - a) / length;
            for (float t = 0; t < length; t += dash + gap)
                DrawLine(a + dir * t, a + dir * Mathf.Min(t + dash, length), color, width, true);
        }
    }

    private float PlantHeight(int slot)
    {
        if (_sim?.Slots[slot] is not { } plant) return 0;
        float cell = _view.CellPixels(slot);
        return plant.Stage switch
        {
            GrowthStage.Seed => cell * .25f,
            GrowthStage.Seedling => cell * .62f,
            GrowthStage.Normal => cell * 1.18f,
            _ => cell * 1.6f
        };
    }

    private void SyncPlants(LevelSimulation sim)
    {
        for (int slot = 0; slot < sim.SlotCount; slot++)
        {
            var plant = sim.Slots[slot];
            if (plant == null)
            {
                if (_plants.Remove(slot, out var gone)) gone.QueueFree();
                continue;
            }
            if (!_plants.TryGetValue(slot, out var sprite))
            {
                sprite = new Sprite2D { Name = $"Plant{slot}", Centered = false };
                _plants[slot] = sprite;
                _plantLayer.AddChild(sprite);
            }
            Dress(sprite, plant, slot);
            bool lit = sim.IsLit(slot);
            sprite.Modulate = plant.Wilted ? new Color(.62f, .62f, .66f)
                : lit ? new Color(1.12f, 1.04f, .9f) : new Color(.52f, .58f, .7f);
            sprite.Position = _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth * .55f));
            sprite.ZIndex = slot / sim.GridSize;
        }
    }

    private void Dress(Sprite2D sprite, LevelPlant plant, int slot)
    {
        float height = PlantHeight(slot);
        Texture2D texture;
        Vector2 root;
        float drawn;
        switch (plant.Stage)
        {
            case GrowthStage.Seed:
                texture = _seed; root = new Vector2(128, 140); drawn = 120;
                break;
            case GrowthStage.Seedling:
                texture = _seedling; root = new Vector2(256, 506); drawn = 480;
                break;
            default:
                bool wilt = plant.Wilted;
                texture = wilt ? _wilted : _mature;
                root = wilt ? new Vector2(256, 506) : new Vector2(320, 600);
                drawn = wilt ? 470 : 545;
                break;
        }
        if (sprite.Texture != texture)
        {
            sprite.Texture = texture;
            sprite.Material = texture == _mature ? Breathing(slot) : null;
        }
        sprite.Offset = -root;
        sprite.Scale = Vector2.One * (height / drawn);
    }

    private ShaderMaterial Breathing(int slot)
    {
        var m = new ShaderMaterial { Shader = _breath };
        m.SetShaderParameter("mask", _mask);
        m.SetShaderParameter("phase", Mathf.PosMod(slot * .37f + slot / 5 * .23f, 1f));
        return m;
    }
}
