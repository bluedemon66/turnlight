using System;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// Levels 1-2 seen through the chamber camera. Same rules as the levels demo
/// (<see cref="LevelSimulation"/>); the light is moved only with the keyboard
/// (WASD moves one cell, Q/E turns the beam one step: 45 degrees with the ellipse beam in
/// data/camera-levels.json), seeds are sown by clicking a pit.
/// </summary>
public partial class ChamberGame : Node
{
    private const string ConfigPath = "res://data/camera-levels.json";
    private static readonly double[] Speeds = { .5, 1, 2 };
    private const float CardWidth = 250;

    private LevelDemoConfig _config = null!;
    private LevelSimulation _game = null!;
    private ChamberView _view = null!;
    private FieldOverlay _overlay = null!;
    private CameraFrame _frame = null!;

    private Label _rec = null!, _cam = null!, _day = null!, _level = null!, _timer = null!, _score = null!, _hint = null!;
    private MeterBar _timeBar = null!, _scoreBar = null!;
    private Button _seeds = null!, _pause = null!, _speed = null!, _restart = null!, _journalTab = null!;
    private PanelContainer _journal = null!;
    private Label _journalText = null!;
    private PanelContainer _card = null!;
    private Label _cardTitle = null!, _cardState = null!, _cardNote = null!, _growth = null!, _darkness = null!;
    private MeterBar _growthBar = null!, _darkBar = null!;
    private Control _growthRow = null!;
    private Control _result = null!;
    private Label _resultTitle = null!, _resultSummary = null!, _resultNote = null!;
    private Button _next = null!, _retry = null!;

    private int _selected = -1;
    private bool _readyToStart, _paused, _sowing;
    private int _speedIndex = 1;
    private string _message = "";
    private double _messageTime;
    private Vector2 _beamCenter;
    private float _beamAngle;
    private bool _beamPlaced;

    public LevelSimulation Simulation => _game;
    public ChamberView View => _view;

    // For scripted checks (tests/chamber_levels.gd): where a pit is on screen, and the rules state.
    public Vector2 CellScreen(int slot) => _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth));
    public double ElapsedSeconds() => _game.ElapsedSeconds;
    public bool IsSlotLit(int slot) => _game.IsLit(slot);
    public int LampTargetSlot() => _game.LampTarget;
    public void AdvanceForTest(double seconds) => _game.Advance(seconds);

    public override void _Ready()
    {
        _config = LevelDemoConfig.Load(ConfigPath);
        _game = new LevelSimulation(_config.Rules, _config.Levels[0], _config.GridSize);

        _view = new ChamberView { Name = "Chamber" };
        AddChild(_view);
        _view.Build(_config.GridSize);

        var field = new CanvasLayer { Name = "Field", Layer = 1 };
        AddChild(field);
        _overlay = new FieldOverlay { Name = "Overlay" };
        field.AddChild(_overlay);
        _overlay.Setup(_view, ChamberStyle.Serif);
        _overlay.SlotPressed += PressSlot;

        var hud = new CanvasLayer { Name = "Hud", Layer = 2 };
        AddChild(hud);
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(root);
        BuildHud(root);
        ResetInterface();
    }

    private void BuildHud(Control root)
    {
        _frame = new CameraFrame { Name = "Frame" };
        root.AddChild(_frame);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float right = size.X - 40;

        _cam = Add(root, ChamberStyle.Text("Cam", "CAM 02", 14, ChamberStyle.Muted, ChamberStyle.Mono), 40, 18, 80, 22);
        _rec = Add(root, ChamberStyle.Text("Rec", "● REC", 16, ChamberStyle.Cream with { A = .8f }, ChamberStyle.Mono), 40, 18, 90, 22);
        _day = Add(root, ChamberStyle.Text("Day", "", 14, ChamberStyle.Muted, ChamberStyle.Mono), right - 220, 18, 220, 22);
        _day.HorizontalAlignment = HorizontalAlignment.Right;
        _level = Add(root, ChamberStyle.Text("Level", "", 32, ChamberStyle.Cream), 40, 44, 150, 48);
        _timer = Add(root, ChamberStyle.Text("Timer", "", 30, ChamberStyle.Cream), 212, 42, 160, 44);
        _timeBar = Add(root, new MeterBar { Name = "TimeBar" }, 212, 88, 172, 6);
        _score = Add(root, ChamberStyle.Text("Score", "", 28, ChamberStyle.Cream), right - 320, 42, 320, 44);
        _scoreBar = Add(root, new MeterBar { Name = "ScoreBar" }, right - 320, 88, 320, 7);

        _journalTab = Add(root, ChamberStyle.Button("JournalTab", "实\n验\n日\n志\n›", 18), 0, 290, 38, 150);
        _journalTab.Pressed += () => { _journal.Visible = !_journal.Visible; Refresh(); };
        _journal = Add(root, new PanelContainer { Name = "Journal", Visible = false }, 46, 270, 300, 200);
        _journal.AddThemeStyleboxOverride("panel", ChamberStyle.Box(ChamberStyle.Panel, ChamberStyle.Gold));
        _journalText = ChamberStyle.Text("JournalText", "", 18, ChamberStyle.Cream);
        _journal.AddChild(_journalText);

        _seeds = Add(root, ChamberStyle.Button("Seeds", "", 20), 40, size.Y - 74, 190, 42);
        _seeds.Pressed += () => { _sowing = !_sowing; _messageTime = 0; Refresh(); };
        _restart = Add(root, ChamberStyle.Button("Restart", "重来"), right - 82, size.Y - 74, 82, 42);
        _speed = Add(root, ChamberStyle.Button("Speed", ""), right - 176, size.Y - 74, 82, 42);
        _pause = Add(root, ChamberStyle.Button("Pause", ""), right - 270, size.Y - 74, 82, 42);
        _pause.Pressed += ToggleClock;
        _speed.Pressed += () => { _speedIndex = (_speedIndex + 1) % Speeds.Length; Refresh(); };
        _restart.Pressed += () => { _game.RestartCurrent(); ResetInterface(); };
        _hint = Add(root, ChamberStyle.Text("Hint", "", 17, ChamberStyle.Cream with { A = .82f }), size.X * .5f - 320, size.Y - 54, 640, 26);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        BuildCard(root);
        BuildResult(root, size);
    }

    private void BuildCard(Control root)
    {
        _card = Add(root, new PanelContainer { Name = "Card", Visible = false }, 0, 0, CardWidth, 10);
        _card.AddThemeStyleboxOverride("panel", ChamberStyle.Box(ChamberStyle.Panel, ChamberStyle.Gold));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        _card.AddChild(box);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 0);
        box.AddChild(head);
        _cardTitle = ChamberStyle.Text("CardTitle", "", 21, ChamberStyle.Cream);
        _cardState = ChamberStyle.Text("CardState", "", 21, ChamberStyle.Cream);
        head.AddChild(_cardTitle);
        head.AddChild(_cardState);
        _cardNote = ChamberStyle.Text("CardNote", "", 15, ChamberStyle.Muted);
        box.AddChild(_cardNote);
        box.AddChild(new HSeparator { Modulate = ChamberStyle.GoldDim });
        var rows = new VBoxContainer { Name = "Rows" };
        rows.AddThemeConstantOverride("separation", 3);
        box.AddChild(rows);
        _growthRow = rows;
        _growth = ChamberStyle.Text("Growth", "", 17, ChamberStyle.Cream);
        _growthBar = new MeterBar { CustomMinimumSize = new Vector2(0, 6) };
        _darkness = ChamberStyle.Text("Darkness", "", 17, ChamberStyle.Cream);
        _darkBar = new MeterBar { CustomMinimumSize = new Vector2(0, 6), Fill = ChamberStyle.Cream with { A = .75f } };
        rows.AddChild(_growth);
        rows.AddChild(_growthBar);
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        rows.AddChild(_darkness);
        rows.AddChild(_darkBar);
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        // The card floats over the field; clicks must reach the pits underneath.
        IgnoreMouse(_card);
    }

    private static void IgnoreMouse(Control control)
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in control.GetChildren())
            if (child is Control c) IgnoreMouse(c);
    }

    private void BuildResult(Control root, Vector2 size)
    {
        _result = Add(root, new Control { Name = "Result", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop }, 0, 0, size.X, size.Y);
        var dim = new ColorRect { Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _result.AddChild(dim);
        var card = Add(_result, new PanelContainer { Name = "ResultCard" }, size.X * .5f - 290, size.Y * .5f - 30, 580, 200);
        var style = ChamberStyle.Box(new Color(.03f, .04f, .06f, .9f), ChamberStyle.Gold);
        style.ContentMarginTop = style.ContentMarginBottom = 22;
        card.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 12);
        card.AddChild(box);
        _resultTitle = ChamberStyle.Text("ResultTitle", "", 36, ChamberStyle.Cream);
        _resultSummary = ChamberStyle.Text("Summary", "", 24, ChamberStyle.Cream);
        _resultNote = ChamberStyle.Text("ResultNote", "", 17, ChamberStyle.Muted);
        foreach (var l in new[] { _resultTitle, _resultSummary, _resultNote })
        {
            l.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(l);
        }
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 28);
        box.AddChild(buttons);
        _next = ChamberStyle.Button("Next", "进入第 2 关", 21, primary: true);
        _retry = ChamberStyle.Button("Retry", "重来本关", 21);
        _next.CustomMinimumSize = _retry.CustomMinimumSize = new Vector2(180, 46);
        buttons.AddChild(_next);
        buttons.AddChild(_retry);
        _next.Pressed += () => { if (_game.TryStartNext(_config.Levels[1])) ResetInterface(); };
        _retry.Pressed += () => { _game.RestartCurrent(); ResetInterface(); };
    }

    private static T Add<T>(Control parent, T control, float x, float y, float w, float h) where T : Control
    {
        parent.AddChild(control);
        ChamberStyle.Place(control, x, y, w, h);
        return control;
    }

    public override void _Process(double delta)
    {
        if (_game.Status == LevelRunStatus.Playing && !_readyToStart && !_paused)
            _game.Advance(delta * Speeds[_speedIndex]);
        _messageTime -= delta;
        UpdateBeam((float)delta);
        Refresh();
    }

    private void UpdateBeam(float delta)
    {
        int n = _game.GridSize;
        var (dx, dy) = FieldOverlay.Step(_game.Direction);
        float reach = (_game.Level.LightRange - 1) * .5f;
        var pos = _game.LampPosition;
        var target = new Vector2(pos.X + dx * reach - (n - 1) * .5f, pos.Y + dy * reach - (n - 1) * .5f);
        float angle = Mathf.Atan2(dy, dx);
        if (!_beamPlaced)
        {
            _beamCenter = target;
            _beamAngle = angle;
            _beamPlaced = true;
        }
        float k = 1 - Mathf.Exp(-16 * delta);
        _beamCenter = _beamCenter.Lerp(target, k);
        _beamAngle = Mathf.LerpAngle(_beamAngle, angle, k);
        _view.SetBeam(_beamCenter, _beamAngle, new Vector2(_game.Level.LightRange * .5f, .5f));
    }

    private void ResetInterface()
    {
        _selected = -1;
        _message = "";
        _messageTime = 0;
        _readyToStart = true;
        _paused = false;
        _sowing = false;
        _speedIndex = 1;
        _journal.Visible = false;
        _beamPlaced = false;
        UpdateBeam(0);
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
        Key control = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        if (control is Key.Q or Key.E)
        {
            if (!_game.TryRotateLamp(control == Key.E ? 1 : -1))
                Notify("田边空间不足，先把光移开再转向。");
        }
        else
        {
            var (x, y) = control switch
            {
                Key.W => (0, -1),
                Key.A => (-1, 0),
                Key.S => (0, 1),
                Key.D => (1, 0),
                _ => (0, 0)
            };
            if (x == 0 && y == 0) return;
            int n = _game.GridSize;
            int column = Math.Clamp(_game.LampTarget % n + x, 0, n - 1);
            int row = Math.Clamp(_game.LampTarget / n + y, 0, n - 1);
            _game.TryMoveLamp(_game.ClampLampTarget(row * n + column));
        }
        GetViewport().SetInputAsHandled();
    }

    private void PressSlot(int slot)
    {
        if (_game.Status != LevelRunStatus.Playing) return;
        _selected = slot;
        if (_game.Slots[slot] == null)
        {
            if (_paused) Notify("已暂停。点「继续」后可以播种。");
            else if (!_sowing) Notify("先点左下角的种子，再点要播种的空坑。");
            else if (_game.TryPlant(slot))
            {
                _readyToStart = false;
                _sowing = _game.AvailableSeeds > 0;
                Notify($"已在 {Plot(slot)} 播种。");
            }
            else Notify("没有可用种子。");
        }
        else
        {
            _sowing = false;
            _messageTime = 0;
        }
        Refresh();
    }

    private void Notify(string message)
    {
        _message = message;
        _messageTime = 2.3;
    }

    private string Plot(int slot) => $"{(char)('A' + slot % _game.GridSize)}{slot / _game.GridSize + 1}";

    private void Refresh()
    {
        bool over = _game.Status != LevelRunStatus.Playing;
        int number = _game.Level.Number;
        bool revealed = number >= 3;
        _cam.Visible = _day.Visible = revealed;
        _rec.Position = new Vector2(revealed ? 112 : 40, 18);
        _rec.Modulate = new Color(1, 1, 1, Time.GetTicksMsec() / 600 % 2 == 0 || over ? 1 : .35f);
        int remaining = Math.Max(0, (int)Math.Ceiling(_game.RemainingSeconds - 1e-6));
        if (revealed)
        {
            int clock = (int)_game.ElapsedSeconds + 42 * 60;
            _day.Text = $"DAY {number:000}   {clock / 3600:00}:{clock / 60 % 60:00}:{clock % 60:00}";
        }
        _level.Text = $"第 {number} 关";
        _timer.Text = $"{remaining / 60}:{remaining % 60:00}";
        _timer.AddThemeColorOverride("font_color", remaining <= 15 && !over ? ChamberStyle.Warn : ChamberStyle.Cream);
        _timeBar.Value = (float)(_game.RemainingSeconds / _game.Level.DurationSeconds);
        _timeBar.QueueRedraw();

        double target = _game.Level.TargetVitality;
        _score.Text = $"生机  {_game.Vitality:0.#} / {target:0.#}";
        _score.HorizontalAlignment = HorizontalAlignment.Right;
        _scoreBar.Value = (float)(_game.Vitality / (target * 1.2));
        _scoreBar.Mark = 1 / 1.2f;
        _scoreBar.QueueRedraw();

        _seeds.Text = $"{(_sowing ? "已选  " : "")}种子 ×{_game.AvailableSeeds}";
        _seeds.Disabled = over || _paused || _game.AvailableSeeds == 0;
        _seeds.AddThemeStyleboxOverride("normal", ChamberStyle.Box(ChamberStyle.Panel, _sowing ? ChamberStyle.Cream : ChamberStyle.Gold, _sowing ? 2 : 1));
        _pause.Text = _readyToStart ? "开始" : _paused ? "继续" : "暂停";
        _pause.Disabled = _speed.Disabled = over;
        _speed.Text = $"{Speeds[_speedIndex]:0.#}×";

        _journalText.Text = $"实验日志 · 第 {number} 天\n\n目标　生机 ≥ {target:0.#}\n时限　{_game.Level.DurationSeconds:0} 秒\n光照　{_game.Level.LightRange} 格\n新增种子　{_game.Level.AddedSeeds} 颗";

        _overlay.Selected = _selected;
        _overlay.Sowing = _sowing && !_paused && !over;
        _overlay.Render(_game);
        RefreshCard();
        RefreshHint(over);
        RefreshResult(over);
        _frame.QueueRedraw();
    }

    private void RefreshHint(bool over)
    {
        if (over) _hint.Text = "";
        else if (_messageTime > 0) _hint.Text = _message;
        else if (_paused) _hint.Text = "已暂停　点选植株查看状态";
        else if (_sowing) _hint.Text = "点空坑播种";
        else if (_readyToStart) _hint.Text = "点种子再点空坑播种　第一颗种子落地开始计时";
        else _hint.Text = "WASD 移光　Q / E 转向";
        _frame.HintHalfWidth = ChamberStyle.Serif.GetStringSize(_hint.Text, HorizontalAlignment.Left, -1, 17).X * .5f;
    }

    private void RefreshCard()
    {
        if (_selected < 0 || _game.Status != LevelRunStatus.Playing)
        {
            _card.Visible = false;
            _frame.LeaderFrom = _frame.LeaderTo = null;
            return;
        }
        _card.Visible = true;
        var plant = _game.Slots[_selected];
        _cardState.AddThemeColorOverride("font_color", ChamberStyle.Cream);
        if (plant == null)
        {
            _cardTitle.Text = $"{Plot(_selected)} · 空坑";
            _cardState.Text = "";
            _cardNote.Text = _game.AvailableSeeds > 0 ? "选种子后点这里播种" : "没有可用种子";
            _cardNote.Visible = true;
            _growthRow.Visible = false;
        }
        else
        {
            var rule = _game.Rules.Stages[(int)plant.Stage];
            bool lit = _game.IsLit(_selected);
            _cardTitle.Text = $"{Plot(_selected)} · {LevelDish.StageName(plant.Stage)} · ";
            _cardState.Text = plant.Wilted ? "打蔫" : lit ? "受光" : "背光";
            if (plant.Wilted) _cardState.AddThemeColorOverride("font_color", ChamberStyle.Warn);
            _cardNote.Text = plant.Wilted ? "生机减半　停止生长" : "";
            _cardNote.Visible = plant.Wilted;
            _growthRow.Visible = true;
            bool mother = plant.Stage == GrowthStage.Mother;
            double goal = rule.GrowthToNext ?? (_game.Rules.FirstSeedGrowth + plant.SeedsProduced * _game.Rules.SeedGrowthIncrement);
            _growth.Text = $"{(mother ? "结籽" : "生长")}　{plant.Growth:0} / {goal:0}";
            _growthBar.Value = (float)(plant.Growth / goal);
            _darkness.Text = $"黑暗　{plant.Darkness:0} / {rule.DieAt:0}";
            _darkness.AddThemeColorOverride("font_color", plant.Wilted ? ChamberStyle.Warn : ChamberStyle.Cream);
            _darkBar.Value = (float)(plant.Darkness / rule.DieAt);
            _darkBar.Mark = (float)(rule.WiltAt / rule.DieAt);
            _growthBar.QueueRedraw();
            _darkBar.QueueRedraw();
        }

        _card.Size = new Vector2(CardWidth, 0);
        Vector2 cardSize = _card.GetCombinedMinimumSize();
        cardSize.X = CardWidth;
        Vector2 anchor = _overlay.PlantAnchor(_selected);
        var outline = _view.PitOutline(_selected);
        float pitLeft = Mathf.Min(outline[0].X, outline[3].X), pitRight = Mathf.Max(outline[1].X, outline[2].X);
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        bool left = pitLeft - 40 - cardSize.X >= 52;
        float x = left ? pitLeft - 40 - cardSize.X : pitRight + 40;
        if (!left && x + cardSize.X > screen.X - 40) x = 52;
        float y = Mathf.Clamp(anchor.Y - cardSize.Y * .5f, 120, screen.Y - 96 - cardSize.Y);
        _card.Position = new Vector2(x, y);
        _card.Size = cardSize;
        _frame.LeaderFrom = new Vector2(left ? x + cardSize.X : x, y + cardSize.Y * .45f);
        _frame.LeaderTo = anchor;
    }

    private void RefreshResult(bool over)
    {
        _result.Visible = over;
        if (!over) return;
        bool won = _game.Status == LevelRunStatus.Won;
        bool last = _game.Level.Number >= _config.Levels.Length;
        _resultTitle.Text = won ? $"第 {_game.Level.Number} 关　通过" : $"第 {_game.Level.Number} 关　未达标";
        _resultSummary.Text = $"生机 {_game.Vitality:0.#}　/　目标 {_game.Level.TargetVitality:0.#}";
        _next.Visible = won && !last;
        _next.Text = $"进入第 {_game.Level.Number + 1} 关";
        _resultNote.Text = _next.Visible
            ? $"下一关保留植株，黑暗清零，补 {_config.Levels[_game.Level.Number].AddedSeeds} 颗种子"
            : "重来会回到本关开始时的样子";
        _retry.Text = "重来本关";
    }
}
