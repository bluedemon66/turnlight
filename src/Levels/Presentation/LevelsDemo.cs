using System;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation;

public partial class LevelsDemo : Control
{
    private LevelDemoConfig _config = null!;
    private LevelSimulation _game = null!;
    private LevelDish _dish = null!;
    private Label _level = null!, _timer = null!, _score = null!;
    private Button _seeds = null!, _pause = null!, _speedButton = null!;
    private Label _detail = null!, _status = null!, _growth = null!, _darkness = null!, _hint = null!;
    private Label _result = null!, _summary = null!;
    private ColorRect _growthFill = null!, _darkFill = null!, _panel = null!;
    private Button _next = null!;
    private int _selected = -1;
    private string _message = "";
    private double _messageTime;
    private double _sinceRefresh;
    private bool _readyToStart, _paused, _sowing;
    private int _speedIndex = 1;
    private static readonly double[] Speeds = { .5, 1, 2 };
    private const float BarWidth = 217;

    public override void _Ready()
    {
        _config = LevelDemoConfig.Load();
        _game = new LevelSimulation(_config.Rules, _config.Levels[0], _config.SlotCount);
        _dish = GetNode<LevelDish>("Dish");
        _level = GetNode<Label>("Level");
        _timer = GetNode<Label>("Timer");
        _score = GetNode<Label>("Score");
        _seeds = GetNode<Button>("Seeds");
        _pause = GetNode<Button>("Pause");
        _speedButton = GetNode<Button>("Speed");
        _detail = GetNode<Label>("Detail");
        _status = GetNode<Label>("Status");
        _growth = GetNode<Label>("Growth");
        _darkness = GetNode<Label>("Darkness");
        _hint = GetNode<Label>("Hint");
        _result = GetNode<Label>("Result");
        _summary = GetNode<Label>("Summary");
        _panel = GetNode<ColorRect>("ResultPanel");
        _growthFill = GetNode<ColorRect>("GrowthFill");
        _darkFill = GetNode<ColorRect>("DarkFill");
        _next = GetNode<Button>("Next");
        _dish.SlotPressed += PressSlot;
        _dish.LampRequested += MoveLamp;
        _seeds.Pressed += () =>
        {
            _sowing = !_sowing;
            _messageTime = 0;
            Refresh();
        };
        _pause.Pressed += ToggleClock;
        _speedButton.Pressed += () =>
        {
            _speedIndex = (_speedIndex + 1) % Speeds.Length;
            Refresh();
        };
        GetNode<Button>("Restart").Pressed += () =>
        {
            _game.RestartCurrent();
            ResetInterface();
        };
        _next.Pressed += () =>
        {
            if (_game.TryStartNext(_config.Levels[1])) ResetInterface();
        };
        ResetInterface();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_game.Status != LevelRunStatus.Playing || _readyToStart || _paused) return;
        _game.Advance(delta * Speeds[_speedIndex]);
        _messageTime -= delta;
        _sinceRefresh += delta;
        if (_sinceRefresh < .06 && _game.Status == LevelRunStatus.Playing) return;
        _sinceRefresh = 0;
        Refresh();
    }

    private void ResetInterface()
    {
        _dish.ResetInput();
        _selected = -1;
        _message = "";
        _messageTime = 0;
        _readyToStart = true;
        _paused = false;
        _sowing = false;
        _speedIndex = 1;
        Refresh();
    }

    private void ToggleClock()
    {
        if (_readyToStart) _readyToStart = false;
        else _paused = !_paused;
        _messageTime = 0;
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_game.Status != LevelRunStatus.Playing || _paused ||
            input is not InputEventKey { Pressed: true, Echo: false } key) return;
        // This demo has one row. Either pair steps the projected light left or right.
        int direction = key.Keycode switch { Key.A or Key.Q => -1, Key.D or Key.E => 1, _ => 0 };
        if (direction == 0) return;
        MoveLamp(Math.Clamp(_game.LampTarget + direction, 0, _game.SlotCount - _game.Level.LightRange));
        GetViewport().SetInputAsHandled();
    }

    private void PressSlot(int slot)
    {
        _selected = slot;
        if (_game.Slots[slot] == null)
        {
            if (_paused) Notify("已暂停。点击「继续」后可以播种。");
            else if (!_sowing) Notify("先点右上角的种子，再点要播种的空槽。");
            else if (_game.TryPlant(slot))
            {
                _readyToStart = false;
                _sowing = _game.AvailableSeeds > 0;
                Notify($"已在 {slot + 1} 号槽播种。");
            }
            else
                Notify("没有可用种子。点击已有植株可以查看状态。");
        }
        else
        {
            _sowing = false;
            _messageTime = 0;
        }
        Refresh();
    }

    private void MoveLamp(int target)
    {
        if (!_paused && _game.TryMoveLamp(target))
        {
            Notify($"灯头移向第 {target + 1} 至 {target + _game.Level.LightRange} 槽。");
            Refresh();
        }
    }

    private void Notify(string message)
    {
        _message = message;
        _messageTime = 2.3;
    }

    private void Refresh()
    {
        bool over = _game.Status != LevelRunStatus.Playing;
        _level.Text = $"第 {_game.Level.Number} 关　·　{(_paused ? "已暂停" : _readyToStart ? "准备播种" : "到时结算生机")}";
        int seconds = Math.Max(0, (int)Math.Ceiling(_game.RemainingSeconds - .000001));
        _timer.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        _timer.Modulate = seconds <= 15 ? new Color("ffbd90") : Colors.White;
        _score.Text = $"{_game.Vitality:0.#} / {_game.Level.TargetVitality:0.#}";
        _seeds.Text = $"{(_sowing ? "已选" : "种子")}　×{_game.AvailableSeeds}";
        _seeds.Disabled = over || _paused || _game.AvailableSeeds == 0;
        _seeds.Modulate = _sowing ? new Color("ffda8c") : Colors.White;
        _pause.Text = _readyToStart ? "开始" : _paused ? "继续" : "暂停";
        _pause.Disabled = _speedButton.Disabled = over;
        _speedButton.Text = $"速度 {Speeds[_speedIndex]:0.#}×";
        _dish.Render(_game, _selected, _sowing && !_paused);
        RefreshSelection();
        bool passed = _game.Status == LevelRunStatus.Won;
        _panel.Visible = _result.Visible = _summary.Visible = over;
        _next.Visible = passed && _game.Level.Number == 1;
        if (over)
        {
            _result.Text = passed ? (_game.Level.Number == 1 ? "第一关通过" : "两关完成") : "生机未达标";
            _summary.Text = $"最终生机　{_game.Vitality:0.#}　/　目标　{_game.Level.TargetVitality:0.#}";
            _hint.Text = _next.Visible ? "进入第二关将保留植株，清除黑暗，并补充种子。" : "可点击「重来本关」再次培养。";
        }
        else if (_messageTime > 0) _hint.Text = _message;
        else if (_paused) _hint.Text = "培养已暂停。可点选植株观察，再点击「继续」。";
        else if (_sowing) _hint.Text = "把鼠标移入空槽预览，点击播种。";
        else if (_readyToStart) _hint.Text = "点右上角的种子，再点空槽播种。播下第一颗种子开始计时。";
        else _hint.Text = $"拖灯或按 A/D、Q/E 调光。每格 {_game.Rules.MoveSecondsPerSlot:0.#} 秒，同时照亮 {_game.Level.LightRange} 槽。";
    }

    private void RefreshSelection()
    {
        var plant = _selected < 0 ? null : _game.Slots[_selected];
        if (plant == null)
        {
            _detail.Text = _selected < 0 ? "培养槽" : $"{_selected + 1} 号槽　·　空槽";
            var stages = _game.Rules.Stages;
            _status.Text = _selected < 0
                ? $"幼苗 {stages[(int)GrowthStage.Seedling].Vitality:0}　常规 {stages[(int)GrowthStage.Normal].Vitality:0}　母体 {stages[(int)GrowthStage.Mother].Vitality:0} 生机"
                : "点击右上角种子可准备播种。";
            _growth.Text = "生长　—";
            _darkness.Text = "黑暗　—";
            SetBar(_growthFill, 0);
            SetBar(_darkFill, 0);
            return;
        }
        var rule = _game.Rules.Stages[(int)plant.Stage];
        string state = plant.Wilted ? "萎蔫" : _game.IsLit(_selected) ? "受光" : "背光";
        _detail.Text = $"{_selected + 1} 号槽　·　{LevelDish.StageName(plant.Stage)}";
        _status.Text = $"{state}　·　生机 {rule.Vitality * (plant.Wilted ? _game.Rules.WiltMultiplier : 1):0.#}";
        bool mother = plant.Stage == GrowthStage.Mother;
        double goal = rule.GrowthToNext ?? (_game.Rules.FirstSeedGrowth + plant.SeedsProduced * _game.Rules.SeedGrowthIncrement);
        _growth.Text = $"{(mother ? "结籽" : "生长")}　{plant.Growth:0} / {goal:0}";
        _darkness.Text = $"黑暗 {plant.Darkness:0} / {rule.DieAt:0}　·　{rule.WiltAt:0} 萎蔫";
        SetBar(_growthFill, plant.Growth / goal);
        SetBar(_darkFill, plant.Darkness / rule.DieAt);
    }

    private static void SetBar(Control fill, double fraction)
    {
        fill.Size = new Vector2(BarWidth * (float)Math.Clamp(fraction, 0, 1), fill.Size.Y);
    }
}
