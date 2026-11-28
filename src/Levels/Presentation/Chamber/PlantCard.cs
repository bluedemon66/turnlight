using System;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// One info card for one pit (监控画面案 ⑤). Several can be open at once; ChamberGame decides
/// which are open, places them and draws their leader lines. The card lets clicks through to
/// the pits underneath, except on its two buttons: close (×) and, for a mother, harvest.
/// </summary>
public partial class PlantCard : PanelContainer
{
    public const float Width = 270;

    public event Action<int>? CloseRequested;
    public event Action<int>? HarvestRequested;

    public int Slot { get; }

    private readonly Label _title, _state, _note, _growth, _darkness;
    private readonly MeterBar _growthBar, _darkBar;
    private readonly Control _rows;
    private readonly Button _close, _harvest;

    public PlantCard(int slot)
    {
        Slot = slot;
        Name = $"Card{slot}";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", ChamberStyle.Box(ChamberStyle.Panel with { A = .86f }, ChamberStyle.Gold));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 0);
        box.AddChild(head);
        _title = ChamberStyle.Text("CardTitle", "", 21, ChamberStyle.Cream);
        _state = ChamberStyle.Text("CardState", "", 21, ChamberStyle.Cream);
        head.AddChild(_title);
        head.AddChild(_state);
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _close = SmallButton("Close", "×", 18);
        _close.TooltipText = "关闭信息卡";
        _close.Pressed += () => CloseRequested?.Invoke(Slot);
        head.AddChild(_close);

        _note = ChamberStyle.Text("CardNote", "", 15, ChamberStyle.Muted);
        box.AddChild(_note);
        box.AddChild(new HSeparator { Modulate = ChamberStyle.GoldDim, MouseFilter = MouseFilterEnum.Ignore });
        var rows = new VBoxContainer { Name = "Rows", MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 3);
        box.AddChild(rows);
        _rows = rows;
        _growth = ChamberStyle.Text("Growth", "", 17, ChamberStyle.Cream);
        _growthBar = new MeterBar { CustomMinimumSize = new Vector2(0, 6) };
        _darkness = ChamberStyle.Text("Darkness", "", 17, ChamberStyle.Cream);
        _darkBar = new MeterBar { CustomMinimumSize = new Vector2(0, 6), Fill = ChamberStyle.Cream with { A = .75f } };
        rows.AddChild(_growth);
        rows.AddChild(_growthBar);
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore });
        rows.AddChild(_darkness);
        rows.AddChild(_darkBar);
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore });

        _harvest = SmallButton("Harvest", "", 16);
        _harvest.Pressed += () => HarvestRequested?.Invoke(Slot);
        box.AddChild(_harvest);
    }

    private static Button SmallButton(string name, string text, int size)
    {
        var b = ChamberStyle.Button(name, text, size);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            if (b.GetThemeStylebox(state) is StyleBoxFlat flat)
            {
                var small = (StyleBoxFlat)flat.Duplicate();
                small.ContentMarginLeft = small.ContentMarginRight = 8;
                small.ContentMarginTop = small.ContentMarginBottom = 1;
                b.AddThemeStyleboxOverride(state, small);
            }
        b.MouseFilter = MouseFilterEnum.Stop;
        return b;
    }

    /// <summary>Show what is in the pit now.</summary>
    public void Fill(LevelSimulation game, string plot, bool canHarvest)
    {
        var plant = game.Slots[Slot];
        var remains = game.Remains[Slot];
        _state.AddThemeColorOverride("font_color", ChamberStyle.Cream);
        _harvest.Visible = false;
        if (plant == null)
        {
            _title.Text = remains != null ? $"{plot} · 残骸" : $"{plot} · 空坑";
            _state.Text = "";
            _note.Text = remains != null
                ? RemainsNote(game, remains)
                : game.AvailableSeeds > 0 ? "选种子后点这里播种" : "没有可用种子";
            _note.Visible = true;
            _rows.Visible = false;
            return;
        }

        var rule = game.Rules.Stages[(int)plant.Stage];
        bool lit = game.IsLit(Slot);
        _title.Text = $"{plot} · {LevelDish.StageName(plant.Stage)} · ";
        _state.Text = plant.Wilted ? "打蔫" : lit ? "受光" : "背光";
        if (plant.Wilted) _state.AddThemeColorOverride("font_color", ChamberStyle.Warn);
        _note.Text = plant.Wilted ? "生机减半　停止生长" : "";
        _note.Visible = plant.Wilted;
        _rows.Visible = true;
        bool mother = plant.Stage == GrowthStage.Mother;
        double goal = rule.GrowthToNext ?? (game.Rules.FirstSeedGrowth + plant.SeedsProduced * game.Rules.SeedGrowthIncrement);
        _growth.Text = $"{(mother ? "结籽" : "生长")}　{plant.Growth:0} / {goal:0}";
        _growthBar.Value = (float)(plant.Growth / goal);
        _darkness.Text = $"黑暗　{plant.Darkness:0} / {rule.DieAt:0}";
        _darkness.AddThemeColorOverride("font_color", plant.Wilted ? ChamberStyle.Warn : ChamberStyle.Cream);
        _darkBar.Value = (float)(plant.Darkness / rule.DieAt);
        _darkBar.Mark = (float)(rule.WiltAt / rule.DieAt);
        _growthBar.QueueRedraw();
        _darkBar.QueueRedraw();
        _harvest.Visible = mother;
        _harvest.Disabled = !canHarvest;
        _harvest.Text = $"收割　得 {game.Rules.HarvestSeeds} 颗种子";
    }

    private static string RemainsNote(LevelSimulation game, LevelRemains remains)
    {
        int left = remains.Day + game.Rules.RemainsDays - game.Level.Number;
        return $"不能播种　铲掉后变回空坑\n{left} 天后自然消散";
    }
}
