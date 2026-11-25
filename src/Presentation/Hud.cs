using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OneBeam.Domain;

namespace OneBeam.Presentation;

public partial class Hud : Control
{
    public event Action? PlantRequested;
    public event Action<string>? SpeciesRequested;
    public event Action? MoveRequested;
    public event Action? AimRequested;
    public event Action? HarvestRequested;
    public event Action? FruitRequested;
    public event Action? PauseRequested;
    public event Action? RestoreRequested;
    public event Action? NewGameRequested;

    private OptionButton _species = null!;
    private ConfirmationDialog _confirm = null!;
    private Action? _confirmedAction;

    public override void _Ready()
    {
        _species = GetNode<OptionButton>("Tools/Species");
        _species.ItemSelected += index =>
            SpeciesRequested?.Invoke(_species.GetItemMetadata((int)index).AsString());
        GetNode<Button>("Tools/PlantButton").Pressed += () => PlantRequested?.Invoke();
        GetNode<Button>("Tools/Actions/Move").Pressed += () => MoveRequested?.Invoke();
        GetNode<Button>("Tools/Actions/Aim").Pressed += () => AimRequested?.Invoke();
        GetNode<Button>("Tools/Actions/Harvest").Pressed += () => HarvestRequested?.Invoke();
        GetNode<Button>("Tools/Actions/Fruit").Pressed += () => FruitRequested?.Invoke();
        GetNode<Button>("Session/Pause").Pressed += () => PauseRequested?.Invoke();

        _confirm = GetNode<ConfirmationDialog>("Confirm");
        _confirm.Confirmed += () =>
        {
            var action = _confirmedAction;
            _confirmedAction = null;
            action?.Invoke();
        };
        _confirm.Canceled += () => _confirmedAction = null;
        GetNode<Button>("Session/Restore").Pressed += () =>
            Confirm("恢复检查点", "将回到最近一次记录的培养状态。", () => RestoreRequested?.Invoke());
        GetNode<Button>("Session/NewGame").Pressed += () =>
            Confirm("重新开始", "将开一只新的培养皿，并从第一颗种子开始。", () => NewGameRequested?.Invoke());
    }

    private void Confirm(string title, string message, Action action)
    {
        _confirmedAction = action;
        _confirm.Title = title;
        _confirm.DialogText = message;
        _confirm.PopupCentered();
    }

    public void Render(EcologySimulation simulation, IReadOnlyDictionary<string, string> names,
        string speciesId, int? selectedId, WorldInteraction mode, bool paused, string objective, bool canRestore)
    {
        var state = simulation.State;
        var plant = state.Plants.FirstOrDefault(candidate => candidate.Id == selectedId);
        bool active = !paused && state.Status == RunStatus.Playing;
        bool planting = mode == WorldInteraction.Plant;
        RenderSpecies(names, state, speciesId);
        _species.Disabled = !active;
        SetLabel("Objective", objective);
        SetLabel("Phase", PhaseCaption(state, paused));
        SetLabel("Hint", Hint(state, mode, paused));
        SetLabel("Tools/Inventory", $"种子库存　{state.CountSeeds(speciesId)}");
        var plantButton = GetNode<Button>("Tools/PlantButton");
        plantButton.Text = planting ? "取消播种" : $"播种{names[speciesId]}";
        plantButton.Disabled = !active || (!planting && state.CountSeeds(speciesId) == 0);

        RenderSpecimen(simulation, plant, names);
        var move = GetNode<Button>("Tools/Actions/Move");
        move.Disabled = !active || plant == null;
        move.Text = mode == WorldInteraction.Move ? "取消移栽" : "移栽";
        var aim = GetNode<Button>("Tools/Actions/Aim");
        aim.Disabled = !active || plant?.SpeciesId != SpeciesRules.SkyBambooId;
        aim.Text = mode == WorldInteraction.Aim ? "取消定向" : "调整方向";
        GetNode<Button>("Tools/Actions/Harvest").Disabled = !active || plant?.Harvestable != true;
        GetNode<Button>("Tools/Actions/Fruit").Disabled = !active || plant == null ||
            state.FruitCount == 0 || plant.BoostSteps > 0;
        var pause = GetNode<Button>("Session/Pause");
        pause.Text = paused ? "继续" : "暂停";
        pause.Disabled = state.Status != RunStatus.Playing;
        GetNode<Button>("Session/Restore").Disabled = !canRestore;
        SetLabel("Tools/Stock", $"活株　{state.Plants.Count}　　　果实　{state.FruitCount}");
        SetLabel("Tools/Progress", state.Phase == GamePhase.OpenLab
            ? $"已覆盖 {state.CoveredZones} / {state.Environment.Columns * state.Environment.Rows} 区　目标 {state.RequiredZones} 区"
            : state.GardenReady ? "已收获阳光花种子" : "首次收获将开启母体培养");
    }

    private void RenderSpecies(IReadOnlyDictionary<string, string> names, WorldState state, string speciesId)
    {
        if (_species.ItemCount == 0)
        {
            foreach (var entry in names)
            {
                _species.AddItem(entry.Value);
                _species.SetItemMetadata(_species.ItemCount - 1, entry.Key);
            }
        }
        for (int i = 0; i < _species.ItemCount; i++)
        {
            string id = _species.GetItemMetadata(i).AsString();
            _species.SetItemText(i, state.IntroducedSpecies.Contains(id) ? names[id] : $"{names[id]}（待发现）");
            if (id == speciesId && _species.Selected != i)
                _species.Select(i);
        }
    }

    private void RenderSpecimen(EcologySimulation sim, PlantState? plant, IReadOnlyDictionary<string, string> names)
    {
        int growth = plant == null ? 0 : Mathf.RoundToInt(100 * plant.Growth);
        int darkness = plant == null ? 0 : Mathf.RoundToInt(100 * plant.Darkness);
        string selection = "植株观察";
        string care = "点击植株可查看生长与暗耗。";
        if (plant != null)
        {
            string light = sim.IsIlluminated(plant.Position) ? "受光" : "未受光";
            selection = $"{names[plant.SpeciesId]} #{plant.Id}　{light}";
            care = plant.SpeciesId switch
            {
                SpeciesRules.SunFlowerId when plant.Stage == PlantStage.Mature =>
                    $"储光 {Mathf.RoundToInt(plant.StoredLight * 100)}% · 为邻株抵御黑暗",
                SpeciesRules.LightMotherId when plant.Stage == PlantStage.Mature => "母体成熟，持续受光可以结果。",
                SpeciesRules.SkyBambooId => $"竹身 {Mathf.RoundToInt(plant.Height)} · 调整方向可指向穹顶",
                _ => "保持受光，植株会逐渐成熟。"
            };
            if (plant.BoostSteps > 0) care += " 果实生效中。";
            else if (plant.Darkness > 0 && sim.HasShadeSupport(plant)) care += " 正受邻株庇护。";
        }
        SetLabel("Tools/Selection", selection);
        string stage = plant?.Stage switch
        {
            PlantStage.Seed => "种子",
            PlantStage.Seedling => "幼苗",
            PlantStage.Mature => "成熟 · 结籽",
            _ => "生长"
        };
        SetLabel("Tools/GrowthLabel", plant?.Harvestable == true ? "成熟 · 可以收种" : $"{stage}　{growth}%");
        GetNode<ProgressBar>("Tools/GrowthBar").Value = growth;
        SetLabel("Tools/DarkLabel", $"暗耗　{darkness}%");
        GetNode<ProgressBar>("Tools/DarkBar").Value = darkness;
        SetLabel("Tools/Care", care);
    }

    private static string PhaseCaption(WorldState state, bool paused)
    {
        string phase = state.Phase switch
        {
            GamePhase.MovableLamp => "移动灯",
            GamePhase.FixedLamp => "固定灯",
            _ => state.Sky switch { SkyPhase.Day => "白昼", SkyPhase.Fog => "起雾", _ => "夜间" }
        };
        if (paused) return $"第 {state.Day} 日　·　已暂停";
        if (state.Phase == GamePhase.OpenLab)
        {
            int t = state.OpenSteps % state.Environment.CycleLength;
            int end = state.Sky switch
            {
                SkyPhase.Day => state.Environment.DaySteps,
                SkyPhase.Fog => state.Environment.DaySteps + state.Environment.FogSteps,
                _ => state.Environment.CycleLength
            };
            return $"第 {state.Day} 日　·　{phase}　剩 {end - t} 拍";
        }
        return $"第 {state.Day} 日　·　{phase}";
    }

    private static string Hint(WorldState state, WorldInteraction mode, bool paused)
    {
        if (state.Status != RunStatus.Playing) return "可恢复检查点，或重新开始培养。";
        if (paused) return "生态时钟已暂停，点击「继续」恢复。";
        return mode switch
        {
            WorldInteraction.Plant => "在培养区点击空地放下种子。",
            WorldInteraction.Move => "点击目的地，将选中的植株移栽过去。",
            WorldInteraction.Aim => "从竹根朝穹顶选一个方向，点击确定。",
            _ when state.Phase == GamePhase.MovableLamp => "拖动琥珀色灯心调整光照，点击植株查看状态。",
            _ when state.Phase == GamePhase.FixedLamp => "灯已固定。把窜天竹移到靠近穹顶的位置。",
            _ => "每个带绿色角标的区域，都有成熟植株生长。"
        };
    }

    public void ShowFeedback(string message) => SetLabel("Feedback", message);
    private void SetLabel(string path, string value) => GetNode<Label>(path).Text = value;
}
