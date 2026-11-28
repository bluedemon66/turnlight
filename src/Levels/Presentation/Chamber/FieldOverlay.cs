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
    private readonly Dictionary<int, LanternPlant> _plants = new();
    private readonly List<LanternPlant> _dying = new();
    // Remains (placeholder art): the wilted picture, still and darkened. A seed that died stays a dark seed.
    private readonly Dictionary<int, LanternPlant> _remains = new();
    private static readonly Color RemainsTint = new(.42f, .36f, .31f);
    private readonly Node2D _plantLayer = new() { Name = "Plants" };
    // Status bars sit above every plant (z 10+), front rows over back rows, the selected one on top.
    // The HUD (card, hints, corners) is a higher CanvasLayer, so it still covers them.
    private readonly Node2D _barLayer = new() { Name = "Bars", ZIndex = 10 };
    private readonly Dictionary<int, PlantStatusBar> _bars = new();
    private readonly List<PlantStatusBar> _fadingBars = new();
    private Texture2D _seed = null!;
    private Font _serif = null!;

    /// <summary>The pit whose card was opened last: its outline and status bar go on top.</summary>
    public int Selected { get; set; } = -1;
    /// <summary>Every pit with an open info card: each gets the gold outline and a fully opaque bar.</summary>
    public ISet<int> Marked { get; } = new HashSet<int>();
    public bool Sowing { get; set; }
    public bool Shoveling { get; set; }
    public int Hover { get; private set; } = -1;

    public void Setup(ChamberView view, Font serif)
    {
        _view = view;
        _serif = serif;
        _seed = GD.Load<Texture2D>("res://art/plants/lantern/lantern_0_seed.png");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_plantLayer);
        AddChild(_barLayer);
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
        return (_plants.TryGetValue(slot, out var s) || _remains.TryGetValue(slot, out s)) && s.Visible
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

        if (Shoveling && Hover >= 0 && !sim.IsPitEmpty(Hover))
            Dashed(_view.PitOutline(Hover, .02f), ChamberStyle.Warn with { A = .9f }, 1.8f);

        if (Sowing && Hover >= 0 && sim.IsPitEmpty(Hover))
        {
            Dashed(_view.PitOutline(Hover, .02f), gold with { A = .7f }, 1.6f);
            float h = _view.CellPixels(Hover) * .32f;
            var px = new Vector2(h * _seed.GetWidth() / _seed.GetHeight(), h);
            var at = _view.Screen(_view.CellCenter(Hover, -ChamberView.PitDepth));
            DrawTextureRect(_seed, new Rect2(at - new Vector2(px.X * .5f, px.Y * .85f), px), false,
                new Color(1, 1, 1, .45f));
        }

        foreach (int slot in Marked)
            if (slot != Selected) Dashed(_view.PitOutline(slot, .03f), gold with { A = .75f }, 1.8f);
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

    /// <summary>Height of the plant above the soil, in screen pixels.</summary>
    private float PlantHeight(int slot) =>
        _plants.TryGetValue(slot, out var p) || _remains.TryGetValue(slot, out p) ? p.VisibleHeight(p.Index) : 0;

    /// <summary>Drop every plant without animating, e.g. when the level restarts.</summary>
    public void ResetPlants()
    {
        foreach (var p in _plants.Values.Concat(_dying).Concat(_remains.Values)) p.QueueFree();
        _remains.Clear();
        foreach (var b in _bars.Values.Concat(_fadingBars)) b.QueueFree();
        _plants.Clear();
        _dying.Clear();
        _bars.Clear();
        _fadingBars.Clear();
    }

    private void SyncBar(LevelSimulation sim, int slot, LevelPlant plant, LanternPlant view, bool lit, bool fresh)
    {
        if (!_bars.TryGetValue(slot, out var bar))
        {
            bar = new PlantStatusBar { Name = $"Bar{slot}" };
            _bars[slot] = bar;
            _barLayer.AddChild(bar);
            fresh = true;
        }
        bar.Anchor = view.Position;
        bar.Sync(plant, sim.Rules, lit, view.VisibleHeight(PictureOf(plant)), snap: fresh);
        bar.Selected = Marked.Contains(slot) || slot == Selected;
        bar.ZIndex = slot == Selected ? sim.GridSize + 1 : bar.Selected ? sim.GridSize : slot / sim.GridSize;
    }

    /// <summary>The status bar over a slot, if a plant grows there (for tests).</summary>
    public PlantStatusBar? BarAt(int slot) => _bars.GetValueOrDefault(slot);

    private static int PictureOf(LevelPlant plant) =>
        plant.Wilted && plant.Stage != GrowthStage.Seed ? LanternPlant.Wilted : (int)plant.Stage;

    /// <summary>Remains in a pit: the dying plant turns into them in place, or they appear (e.g. after a restart).</summary>
    private void SyncRemains(LevelSimulation sim, int slot, LevelRemains remains)
    {
        if (!_remains.TryGetValue(slot, out var view))
        {
            bool turning = _plants.Remove(slot, out view);
            if (!turning)
            {
                view = new LanternPlant { Name = $"Remains{slot}" };
                _plantLayer.AddChild(view);
            }
            else view!.Name = $"Remains{slot}";
            view!.Still = true;
            bool seed = remains.Stage == GrowthStage.Seed;
            view.DarkSeed = seed;
            view.Show(seed ? LanternPlant.Seed : LanternPlant.Wilted, animate: turning);
            _remains[slot] = view;
        }
        view!.PixelScale = _view.CellPixels(slot) * 1.05f / 474f;
        view.Modulate = RemainsTint;
        view.Position = _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth * .55f));
        view.ZIndex = slot / sim.GridSize;
    }

    /// <summary>The remains view in a slot, if any (for tests).</summary>
    public bool HasRemainsView(int slot) => _remains.ContainsKey(slot);

    private void SyncPlants(LevelSimulation sim)
    {
        for (int slot = 0; slot < sim.SlotCount; slot++)
        {
            var plant = sim.Slots[slot];
            if (plant == null)
            {
                if (sim.Remains[slot] is { } remains)
                    SyncRemains(sim, slot, remains);
                else
                {
                    // Gone (shovelled, harvested, remains cleared, or no remains rule): sink into the soil.
                    if (_plants.Remove(slot, out var gone) || _remains.Remove(slot, out gone))
                    {
                        gone.Die();
                        _dying.Add(gone);
                    }
                }
                if (_bars.Remove(slot, out var bar))
                {
                    bar.FadeOut();
                    _fadingBars.Add(bar);
                }
                continue;
            }
            if (_remains.Remove(slot, out var old)) old.QueueFree();
            bool fresh = !_plants.TryGetValue(slot, out var view);
            if (fresh)
            {
                view = new LanternPlant { Name = $"Plant{slot}", Phase = slot * .731f };
                _plants[slot] = view;
                _plantLayer.AddChild(view);
            }
            view!.PixelScale = _view.CellPixels(slot) * 1.05f / 474f;
            view.DarkSeed = plant.Wilted && plant.Stage == GrowthStage.Seed;
            view.Show(PictureOf(plant), animate: !fresh);
            bool lit = sim.IsLit(slot);
            view.Modulate = lit ? new Color(1.1f, 1.04f, .92f) : new Color(.56f, .61f, .72f);
            view.Position = _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth * .55f));
            view.ZIndex = slot / sim.GridSize;
            SyncBar(sim, slot, plant, view, lit, fresh);
        }
        for (int i = _fadingBars.Count - 1; i >= 0; i--)
        {
            if (!_fadingBars[i].Finished) continue;
            _fadingBars[i].QueueFree();
            _fadingBars.RemoveAt(i);
        }
        for (int i = _dying.Count - 1; i >= 0; i--)
        {
            if (!_dying[i].Finished) continue;
            _dying[i].QueueFree();
            _dying.RemoveAt(i);
        }
    }
}
