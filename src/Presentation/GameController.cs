using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OneBeam.Config;
using OneBeam.Domain;
using OneBeam.Persistence;
using Point = System.Numerics.Vector2;

namespace OneBeam.Presentation;

public partial class GameController : Node
{
    private const double SecondsPerStep = 0.5;
    private const string CheckpointFile = "user://checkpoint.json";

    [Export] public PlantDefinition Species { get; set; } = null!;
    [Export] public PlantDefinition Mother { get; set; } = null!;
    [Export] public PlantDefinition Bamboo { get; set; } = null!;
    [Export] public int StartingSeeds { get; set; } = 3;
    [Export] public Vector2 DishSize { get; set; } = new(600, 390);
    [Export] public Vector2 InitialLampPosition { get; set; } = new(180, 185);
    [Export] public float LampRadius { get; set; } = 84f;
    [Export] public int DaySteps { get; set; } = 30;
    [Export] public int FogSteps { get; set; } = 12;
    [Export] public int NightSteps { get; set; } = 16;
    [Export] public int FruitBoostSteps { get; set; } = 12;
    [Export] public int RequiredZones { get; set; } = 6;

    private EcologySimulation _simulation = null!;
    private WorldSnapshot _checkpoint = null!;
    private WorldView _world = null!;
    private Hud _hud = null!;
    private SpeciesRules[] _rules = null!;
    private IReadOnlyDictionary<string, string> _names = null!;
    private string _activeSpecies = "";
    private string _checkpointPath = "";
    private WorldInteraction _mode;
    private double _elapsed;
    private int? _selectedId;
    private bool _paused;

    private WorldState State => _simulation.State;
    private bool CanAct => !_paused && State.Status == RunStatus.Playing;

    public override void _Ready()
    {
        _world = GetNode<WorldView>("WorldView");
        _hud = GetNode<Hud>("Interface/Hud");
        var definitions = new[] { Species, Mother, Bamboo };
        _rules = definitions.Select(definition => definition.ToRules()).ToArray();
        _names = definitions.ToDictionary(definition => definition.SpeciesId,
            definition => definition.DisplayName);
        var checkpointFile = ProjectSettings.GetSetting("one_beam/checkpoint_file", CheckpointFile).AsString();
        _checkpointPath = ProjectSettings.GlobalizePath(checkpointFile);

        _hud.PlantRequested += () => ToggleMode(WorldInteraction.Plant);
        _hud.MoveRequested += () => ToggleMode(WorldInteraction.Move);
        _hud.AimRequested += () => ToggleMode(WorldInteraction.Aim);
        _hud.HarvestRequested += Harvest;
        _hud.FruitRequested += GiveFruit;
        _hud.PauseRequested += TogglePause;
        _hud.RestoreRequested += Restore;
        _hud.NewGameRequested += NewGame;
        _hud.SpeciesRequested += SelectSpecies;
        _world.PlantPressed += SelectPlant;
        _world.GroundPressed += HandleGround;
        _world.LampDragged += MoveLamp;

        LoadSession(CreateWorld());
        _checkpoint = State.Capture();
        if (File.Exists(_checkpointPath))
        {
            if (CheckpointStore.TryLoad(_checkpointPath, out var saved, out _) && saved != null)
            {
                _checkpoint = saved.Capture();
                _hud.ShowFeedback("检测到检查点，可从下方恢复。");
            }
            else
            {
                _hud.ShowFeedback("读取检查点失败，已准备新的培养皿。");
            }
        }
        Refresh();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!CanAct || (State.Plants.Count == 0 && !State.GardenReady))
            return;

        _elapsed += delta;
        if (_elapsed < SecondsPerStep)
            return;

        _elapsed -= SecondsPerStep;
        var before = (State.Phase, State.Day, State.GardenReady);
        _simulation.AdvanceTime();
        FinishChange(before);
        Refresh();
    }

    private WorldState CreateWorld()
    {
        var environment = new EnvironmentRules
        {
            DaySteps = DaySteps,
            FogSteps = FogSteps,
            NightSteps = NightSteps,
            BoostDuration = FruitBoostSteps,
            RequiredZones = RequiredZones
        };
        return new WorldState(ToPoint(DishSize), ToPoint(InitialLampPosition), LampRadius,
            Species.SpeciesId, StartingSeeds, environment);
    }

    private void LoadSession(WorldState world)
    {
        _simulation = new EcologySimulation(world, _rules);
        _elapsed = 0;
        _paused = false;
        _selectedId = null;
        _activeSpecies = Species.SpeciesId;
        _mode = WorldInteraction.Inspect;
        _world.ResetInteraction();
    }

    private void SelectSpecies(string id)
    {
        if (!CanAct || !_names.ContainsKey(id))
            return;

        _activeSpecies = id;
        _mode = WorldInteraction.Inspect;
        _hud.ShowFeedback(State.IntroducedSpecies.Contains(id)
            ? ""
            : id == Mother.SpeciesId ? "首次收获阳光花可获得母体种子。" : "光源母体成形后可获得窜天竹种子。");
        Refresh();
    }

    private void SelectPlant(int id)
    {
        if (!CanAct)
            return;
        _selectedId = id;
        _hud.ShowFeedback("");
        Refresh();
    }

    private void ToggleMode(WorldInteraction mode)
    {
        if (!CanAct)
            return;

        _mode = _mode == mode ? WorldInteraction.Inspect : mode;
        _hud.ShowFeedback(_mode switch
        {
            WorldInteraction.Plant => "点击空地播种。",
            WorldInteraction.Move => "点击新的栽种位置。",
            WorldInteraction.Aim => "点击想让窜天竹指向的位置。",
            _ => ""
        });
        Refresh();
    }

    private void HandleGround(Vector2 position)
    {
        if (!CanAct)
            return;

        var before = (State.Phase, State.Day, State.GardenReady);
        var target = ToPoint(position);
        switch (_mode)
        {
            case WorldInteraction.Plant:
                if (!_simulation.TryPlantSeed(_activeSpecies, target))
                {
                    _hud.ShowFeedback("播种失败。请检查库存并与其他植株留出间距。");
                    return;
                }
                _selectedId = State.Plants[^1].Id;
                _hud.ShowFeedback("");
                break;
            case WorldInteraction.Move when _selectedId is int movingId:
                if (!_simulation.TryMovePlant(movingId, target))
                {
                    _hud.ShowFeedback("移栽失败。请在可种区域内选一片空地。");
                    return;
                }
                _hud.ShowFeedback("已移栽。");
                break;
            case WorldInteraction.Aim when _selectedId is int bambooId:
                if (!_simulation.TryAimPlant(bambooId, target))
                {
                    _hud.ShowFeedback("请点击与竹根不同的位置。");
                    return;
                }
                _hud.ShowFeedback("已调整窜天竹的生长方向。");
                break;
            default:
                return;
        }

        _mode = WorldInteraction.Inspect;
        FinishChange(before);
        Refresh();
    }

    private void MoveLamp(Vector2 position)
    {
        if (!CanAct)
            return;
        if (!_simulation.TryMoveLamp(ToPoint(position)))
        {
            if (State.Phase != GamePhase.MovableLamp)
                _hud.ShowFeedback("实验灯已固定。果实促生长，阳光花帮助抗暗。");
            return;
        }

        _hud.ShowFeedback("");
        Refresh();
    }

    private void Harvest()
    {
        if (!CanAct || _selectedId is not int id)
            return;
        var before = (State.Phase, State.Day, State.GardenReady);
        if (!_simulation.TryHarvest(id))
        {
            _hud.ShowFeedback("这株植物还没到收种时间。");
            return;
        }

        _hud.ShowFeedback(State.GardenReady && !before.GardenReady
            ? "已收种。实验员交给你两颗光源母体种子。"
            : "种子已入库。");
        FinishChange(before);
        Refresh();
    }

    private void GiveFruit()
    {
        if (!CanAct || _selectedId is not int id)
            return;
        if (!_simulation.TryGiveFruit(id))
        {
            _hud.ShowFeedback("果实不足，或该植株正受到上一枚果实的支援。");
            return;
        }

        _hud.ShowFeedback("果实已给予选中的植株。");
        Refresh();
    }

    private void TogglePause()
    {
        if (State.Status != RunStatus.Playing)
            return;
        _paused = !_paused;
        _world.ResetInteraction();
        _hud.ShowFeedback(_paused ? "生态时钟暂停。" : "");
        Refresh();
    }

    private void Restore()
    {
        if (!WorldState.TryRestore(_checkpoint, out var restored) || restored == null)
        {
            _hud.ShowFeedback("检查点无法恢复。");
            return;
        }

        LoadSession(restored);
        _hud.ShowFeedback("已恢复到检查点。");
        Refresh();
    }

    private void NewGame()
    {
        LoadSession(CreateWorld());
        _hud.ShowFeedback("新的培养皿已准备好。");
        SaveCheckpoint();
        Refresh();
    }

    private void FinishChange((GamePhase Phase, int Day, bool GardenReady) before)
    {
        if (State.Status == RunStatus.Failed)
        {
            _world.ResetInteraction();
            _hud.ShowFeedback("可从最近一次检查点继续。");
            return;
        }

        if (State.Status == RunStatus.Won)
        {
            _world.ResetInteraction();
            _hud.ShowFeedback("实验室已形成能越夜的生态。");
            return;
        }

        if (State.Phase != before.Phase)
        {
            _world.ResetInteraction();
            if (State.Phase == GamePhase.FixedLamp)
            {
                _world.PlayDeparture();
                _hud.ShowFeedback("实验员离开了。光源母体开始结果，实验灯固定在当前位置。");
            }
            else if (State.Phase == GamePhase.OpenLab)
            {
                _world.PlayBreach();
                _hud.ShowFeedback("窜天竹破顶。白昼开始，培养皿外的区域可以播种。");
            }
        }

        if (State.Phase != before.Phase || State.GardenReady != before.GardenReady
            || (State.Phase == GamePhase.OpenLab && State.Day != before.Day))
            SaveCheckpoint();
    }

    private void SaveCheckpoint()
    {
        _checkpoint = State.Capture();
        if (!CheckpointStore.TrySave(_checkpointPath, _checkpoint, out var error))
        {
            GD.PushWarning($"Checkpoint write failed: {error}");
            _hud.ShowFeedback("本局可恢复检查点。写入磁盘失败。");
        }
    }

    private string Objective()
    {
        if (State.Status == RunStatus.Failed)
        {
            var id = State.FailureSpecies;
            var name = id != null && _names.TryGetValue(id, out var species) ? species : "物种";
            return $"{name}已灭绝。";
        }
        if (State.Status == RunStatus.Won)
            return "实验室的生态已稳定。";
        return State.Phase switch
        {
            GamePhase.MovableLamp when !State.GardenReady =>
                "在光下培育阳光花，成熟后收割留种。",
            GamePhase.MovableLamp =>
                "把光源母体种在靠近顶部的位置。它成形时，实验灯会固定。",
            GamePhase.FixedLamp =>
                "在顶部附近培养窜天竹，给它果实并向上生长，直到破顶。",
            _ => $"保住所有物种，占据至少 {State.RequiredZones} 个区域并度过首夜。"
        };
    }

    private void Refresh()
    {
        if (_selectedId is int id && !State.Plants.Any(plant => plant.Id == id))
        {
            _selectedId = null;
            _mode = WorldInteraction.Inspect;
        }

        var mode = CanAct ? _mode : WorldInteraction.Inspect;
        _world.Render(_simulation, _selectedId, mode);
        _hud.Render(_simulation, _names, _activeSpecies, _selectedId, mode, _paused,
            Objective(), true);
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
