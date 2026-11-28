using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// The standard level seen through the chamber camera (<see cref="LevelSimulation"/>, rules in
/// data/camera-levels.json). The light is moved only with the keyboard (WASD one cell, two keys
/// at once go diagonally; Q/E turn 45 degrees); seeds are sown and the shovel used by clicking pits.
/// Each level is shown as a day (「第 1 天」) with a 24-hour clock.
/// </summary>
public partial class ChamberGame : Node
{
    private const string ConfigPath = "res://data/camera-levels.json";
    private static readonly double[] Speeds = { .5, 1, 2 };

    // Placement of the 24-hour clock. Not decided yet (监控画面案): beside the countdown for now.
    private static readonly Rect2 ClockSpot = new(404, 52, 260, 32);

    private LevelDemoConfig _config = null!;
    private LevelSimulation _game = null!;
    private ChamberView _view = null!;
    private FieldOverlay _overlay = null!;
    private CameraFrame _frame = null!;
    private SoundDirector _sound = null!;

    private Label _rec = null!, _cam = null!, _day = null!, _level = null!, _timer = null!, _clock = null!, _score = null!, _hint = null!;
    private MeterBar _timeBar = null!, _scoreBar = null!;
    private Button _seeds = null!, _shovel = null!, _pause = null!, _speed = null!, _restart = null!, _journalTab = null!;
    private PanelContainer _journal = null!;
    private Label _journalText = null!;
    private Control _cardLayer = null!;
    private Control _result = null!;
    private Label _resultTitle = null!, _resultSummary = null!, _resultNote = null!;
    private Button _next = null!, _retry = null!;

    // Info cards (监控画面案 ⑤): pinned by a click, shown while hovered, or shown for lit plants
    // while time runs. _open keeps the order they appeared in; the last one is drawn on top.
    private readonly Dictionary<int, PlantCard> _cards = new();
    private readonly List<int> _open = new();
    private readonly List<int> _pinned = new();
    private readonly HashSet<int> _closedByHand = new();

    private bool _readyToStart, _paused, _sowing, _shoveling;
    private int _speedIndex = 1;
    private string _message = "";
    private double _messageTime;
    private Vector2 _beamCenter;
    private float _beamAngle;
    private bool _beamPlaced;

    // Last seen state, to turn changes into sound events.
    private LevelPlant?[] _seenPlants = Array.Empty<LevelPlant?>();
    private LevelRemains?[] _seenRemains = Array.Empty<LevelRemains?>();
    private int _seenSeeds;
    private (int X, int Y) _seenLampCell;
    private LevelRunStatus _seenStatus;
    private int _seenHover = -1;

    public LevelSimulation Simulation => _game;
    public ChamberView View => _view;
    private InterfaceSettings Ui => _config.Ui;
    private int Selected => _pinned.Count > 0 ? _pinned[^1] : -1;
    private bool TimeRuns => _game.Status == LevelRunStatus.Playing && !_readyToStart && !_paused;

    // For scripted checks (tests/*.gd): where a pit is on screen, and the rules state.
    public Vector2 CellScreen(int slot) => _view.Screen(_view.CellCenter(slot, -ChamberView.PitDepth));
    public double ElapsedSeconds() => _game.ElapsedSeconds;
    public bool IsSlotLit(int slot) => _game.IsLit(slot);
    public int LampTargetSlot() => _game.LampTarget;
    public Vector2 LampPositionForTest() => new(_game.LampPosition.X, _game.LampPosition.Y);
    public void AdvanceForTest(double seconds) => _game.Advance(seconds);

    /// <summary>
    /// Tests only: put a plant of a given state in a slot without playing the rules. Reaches into
    /// the simulation by reflection so the rules code has no test back door. Stage -1 empties the slot.
    /// </summary>
    public void SetPlantForTest(int slot, int stage, double growth, double darkness, bool wilted, int seeds)
    {
        Field<LevelPlant?>("_slots")[slot] = stage < 0 ? null : new LevelPlant((GrowthStage)stage, growth, darkness, wilted, seeds);
        Field<LevelRemains?>("_remains")[slot] = null;
    }

    /// <summary>Tests only: leave remains of a plant of <paramref name="stage"/> in a slot.</summary>
    public void SetRemainsForTest(int slot, int stage)
    {
        Field<LevelPlant?>("_slots")[slot] = null;
        Field<LevelRemains?>("_remains")[slot] = new LevelRemains((GrowthStage)stage, _game.Level.Number);
    }

    private T[] Field<T>(string name) => (T[])typeof(LevelSimulation)
        .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(_game)!;

    /// <summary>Tests only: pin a card on a slot as a click would (-1 closes every pinned card).</summary>
    public void SelectForTest(int slot)
    {
        if (slot < 0) _pinned.Clear();
        else Pin(slot);
    }

    public int[] OpenCardSlots() => _open.ToArray();
    public bool HasRemainsForTest(int slot) => _game.Remains[slot] != null;
    public bool IsPitEmptyForTest(int slot) => _game.IsPitEmpty(slot);
    public int SeedsForTest() => _game.AvailableSeeds;
    public string[] SoundLog() => _sound.Log.ToArray();
    public bool HasRemainsViewForTest(int slot) => _overlay.HasRemainsView(slot);
    public bool BarCelebratingForTest(int slot) => _overlay.BarAt(slot)?.CelebratingStage ?? false;
    public bool HasBarForTest(int slot) => _overlay.BarAt(slot) != null;
    public Vector2 BarPositionForTest(int slot) => _overlay.BarAt(slot)?.Position ?? Vector2.Zero;

    public override void _Ready()
    {
        _config = LevelDemoConfig.Load(ConfigPath);
        _game = new LevelSimulation(_config.Rules, _config.Levels[0], _config.GridSize);
        PlantStatusBar.NearWiltDarkness = Ui.NearWiltDarkness;

        _sound = new SoundDirector { Name = "Sound" };
        AddChild(_sound);

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
        _sound.SetLoop(Sfx.A1, true);
        _sound.StartMusic();
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
        _clock = Add(root, ChamberStyle.Text("Clock", "", 22, ChamberStyle.Cream with { A = .9f }),
            ClockSpot.Position.X, ClockSpot.Position.Y, ClockSpot.Size.X, ClockSpot.Size.Y);
        _score = Add(root, ChamberStyle.Text("Score", "", 28, ChamberStyle.Cream), right - 320, 42, 320, 44);
        _scoreBar = Add(root, new MeterBar { Name = "ScoreBar" }, right - 320, 88, 320, 7);

        _journalTab = Add(root, ChamberStyle.Button("JournalTab", "实\n验\n日\n志\n›", 18), 0, 290, 38, 150);
        _journalTab.Pressed += () => { _journal.Visible = !_journal.Visible; _sound.Play(Sfx.F2); Refresh(); };
        _journal = Add(root, new PanelContainer { Name = "Journal", Visible = false }, 46, 270, 300, 200);
        _journal.AddThemeStyleboxOverride("panel", ChamberStyle.Box(ChamberStyle.Panel, ChamberStyle.Gold));
        _journalText = ChamberStyle.Text("JournalText", "", 18, ChamberStyle.Cream);
        _journal.AddChild(_journalText);

        _seeds = Add(root, ChamberStyle.Button("Seeds", "", 20), 40, size.Y - 74, 190, 42);
        _seeds.Pressed += () =>
        {
            _sowing = !_sowing;
            _shoveling = false;
            _messageTime = 0;
            _sound.Play(Sfx.D1);
            Refresh();
        };
        // The shovel: same use as the seeds (click it, then click a pit). Placement not designed yet.
        _shovel = Add(root, ChamberStyle.Button("Shovel", "", 20), 242, size.Y - 74, 118, 42);
        _shovel.Pressed += () =>
        {
            _shoveling = !_shoveling;
            _sowing = false;
            _messageTime = 0;
            Refresh();
        };
        _restart = Add(root, ChamberStyle.Button("Restart", "重来"), right - 82, size.Y - 74, 82, 42);
        _speed = Add(root, ChamberStyle.Button("Speed", ""), right - 176, size.Y - 74, 82, 42);
        _pause = Add(root, ChamberStyle.Button("Pause", ""), right - 270, size.Y - 74, 82, 42);
        _pause.Pressed += ToggleClock;
        _speed.Pressed += ChangeSpeed;
        _restart.Pressed += Restart;
        _hint = Add(root, ChamberStyle.Text("Hint", "", 17, ChamberStyle.Cream with { A = .82f }), size.X * .5f - 320, size.Y - 54, 640, 26);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        _cardLayer = Add(root, new Control { Name = "Cards", MouseFilter = Control.MouseFilterEnum.Ignore }, 0, 0, size.X, size.Y);
        BuildResult(root, size);
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
        _next = ChamberStyle.Button("Next", "", 21, primary: true);
        _retry = ChamberStyle.Button("Retry", "重开这一天", 21);
        _next.CustomMinimumSize = _retry.CustomMinimumSize = new Vector2(180, 46);
        buttons.AddChild(_next);
        buttons.AddChild(_retry);
        _next.Pressed += () =>
        {
            if (!_game.TryStartNext(_config.Levels[_game.Level.Number])) return;
            ResetInterface();
            _sound.Resume();
        };
        _retry.Pressed += Restart;
    }

    private static T Add<T>(Control parent, T control, float x, float y, float w, float h) where T : Control
    {
        parent.AddChild(control);
        ChamberStyle.Place(control, x, y, w, h);
        return control;
    }

    public override void _Process(double delta)
    {
        if (TimeRuns)
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
        _overlay.ResetPlants();
        CloseAllCards();
        _message = "";
        _messageTime = 0;
        _readyToStart = true;
        _paused = false;
        _sowing = false;
        _shoveling = false;
        _speedIndex = 1;
        _journal.Visible = false;
        _beamPlaced = false;
        UpdateBeam(0);
        _sound.SetPaused(false);
        Remember();
        Refresh();
    }

    private void Restart()
    {
        _game.RestartCurrent();
        ResetInterface();
        _sound.Resume();   // after a result: the next track; during play: nothing changes
        _sound.Play(Sfx.E6);
    }

    private void ToggleClock()
    {
        if (_readyToStart)
        {
            _readyToStart = false;
            _sound.Play(Sfx.E1);
        }
        else
        {
            _paused = !_paused;
            _sound.Play(Sfx.E2);
            _sound.SetPaused(_paused);
            // 监控画面案 ⑤: pausing closes every info card.
            if (_paused) CloseAllCards();
        }
        _messageTime = 0;
        Refresh();
    }

    private void ChangeSpeed()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        _sound.Play(Sfx.E3);
        // 监控画面案 ⑤ setting: changing speed closes the cards, except those of lit plants.
        if (Ui.CloseCardsOnSpeedChange)
        {
            _pinned.RemoveAll(slot => !_game.IsLit(slot));
            _closedByHand.Clear();
        }
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_game.Status != LevelRunStatus.Playing || _paused ||
            input is not InputEventKey { Pressed: true, Echo: false } key) return;
        Key control = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        if (control is Key.Q or Key.E)
        {
            if (_game.TryRotateLamp(control == Key.E ? 1 : -1)) _sound.Play(Sfx.B2);
            else
            {
                Notify("卡住了", Ui.StuckMessageSeconds);
                _sound.Play(Sfx.B3);
            }
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
            // Two keys at once (W+D, or D pressed while W is held) make one diagonal step;
            // the rules then slide the light straight to the diagonal cell (光照案, 斜向移动).
            if (x == 0) x = Held(Key.D) - Held(Key.A);
            else y = Held(Key.S) - Held(Key.W);
            int n = _game.GridSize;
            int column = Math.Clamp(_game.LampTarget % n + x, 0, n - 1);
            int row = Math.Clamp(_game.LampTarget / n + y, 0, n - 1);
            _game.TryMoveLamp(_game.ClampLampTarget(row * n + column));
        }
        GetViewport().SetInputAsHandled();
    }

    private static int Held(Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;

    private void PressSlot(int slot)
    {
        if (_game.Status != LevelRunStatus.Playing) return;
        if (_shoveling)
        {
            Shovel(slot);
            Refresh();
            return;
        }
        if (_game.IsPitEmpty(slot))
        {
            bool sown = false;
            if (_paused) Refuse("已暂停。点「继续」后可以播种。");
            else if (!_sowing) Refuse("先点左下角的种子，再点要播种的空坑。");
            else if (_game.TryPlant(slot))
            {
                sown = true;
                if (_readyToStart) _sound.Play(Sfx.E1);
                _readyToStart = false;
                _sowing = _game.AvailableSeeds > 0;
                Notify($"已在 {Plot(slot)} 播种。");
                _sound.Play(Sfx.D3);
            }
            else Refuse("没有可用种子。");
            // Sowing does not open a card each time; clicking a pit otherwise does.
            if (!sown) Pin(slot);
        }
        else
        {
            if (_game.Remains[slot] != null && _sowing) Refuse("残骸还在，先用铲子铲掉。");
            else
            {
                _sowing = false;
                _messageTime = 0;
            }
            Pin(slot);
        }
        Refresh();
    }

    private void Shovel(int slot)
    {
        if (_paused) Refuse("已暂停。点「继续」后可以用铲子。");
        else if (_game.IsPitEmpty(slot)) Refuse("这里没有可铲的东西。");
        else if (_game.TryShovel(slot))
        {
            Notify($"已铲除 {Plot(slot)}。");
            _shoveling = _game.RewardPoints >= _game.Rules.ShovelCost;
        }
        else Refuse("奖励点不够，不能用铲子。");
    }

    private void Harvest(int slot)
    {
        if (_paused) Refuse("已暂停。点「继续」后可以收割。");
        else if (_game.TryHarvest(slot))
            Notify($"已收割 {Plot(slot)}，得到 {_game.Rules.HarvestSeeds} 颗种子。");
        Refresh();
    }

    private void Refuse(string message)
    {
        Notify(message);
        _sound.Play(Sfx.D4);
    }

    private void Notify(string message, double? seconds = null)
    {
        _message = message;
        _messageTime = seconds ?? Ui.MessageSeconds;
    }

    private string Plot(int slot) => $"{(char)('A' + slot % _game.GridSize)}{slot / _game.GridSize + 1}";

    // ---- Info cards -------------------------------------------------------------------------

    /// <summary>A click keeps the card open (on top) until it is closed, time speed changes, or pause.</summary>
    private void Pin(int slot)
    {
        bool shown = _open.Contains(slot);
        _pinned.Remove(slot);
        _pinned.Add(slot);
        _closedByHand.Remove(slot);
        _open.Remove(slot);
        _open.Add(slot);
        if (!shown) _sound.Play(Sfx.F1);
    }

    private void CloseCard(int slot)
    {
        _pinned.Remove(slot);
        _closedByHand.Add(slot);
        Refresh();
    }

    private void CloseAllCards()
    {
        _pinned.Clear();
        _closedByHand.Clear();
    }

    /// <summary>Which pits show a card now, by the rules of 监控画面案 ⑤.</summary>
    private HashSet<int> WantedCards()
    {
        var wanted = new HashSet<int>();
        if (_game.Status != LevelRunStatus.Playing) return wanted;   // hidden at the result
        wanted.UnionWith(_pinned);
        int hover = _overlay.Hover;
        if (hover >= 0 && !_game.IsPitEmpty(hover) && !_closedByHand.Contains(hover)) wanted.Add(hover);
        if (Ui.AutoCardsForLitPlants && TimeRuns)
            for (int slot = 0; slot < _game.SlotCount; slot++)
                if (_game.Slots[slot] != null && _game.IsLit(slot) && !_closedByHand.Contains(slot)) wanted.Add(slot);
        // A card closed by hand may come back once the plant has left the light and the mouse.
        _closedByHand.RemoveWhere(slot => !_game.IsLit(slot) && slot != hover);
        return wanted;
    }

    private void RefreshCards()
    {
        var wanted = WantedCards();
        _open.RemoveAll(slot => !wanted.Contains(slot));
        foreach (int slot in wanted.OrderBy(s => s))
            if (!_open.Contains(slot)) _open.Add(slot);
        foreach (int slot in _cards.Keys.Where(s => !_open.Contains(s)).ToList())
        {
            _cards[slot].QueueFree();
            _cards.Remove(slot);
        }

        _frame.Leaders.Clear();
        _placed.Clear();
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        bool canHarvest = _game.Status == LevelRunStatus.Playing && !_paused;
        for (int i = 0; i < _open.Count; i++)
        {
            int slot = _open[i];
            if (!_cards.TryGetValue(slot, out var card))
            {
                card = new PlantCard(slot);
                card.CloseRequested += CloseCard;
                card.HarvestRequested += Harvest;
                _cards[slot] = card;
                _cardLayer.AddChild(card);
            }
            _cardLayer.MoveChild(card, i);   // later cards on top
            card.Fill(_game, Plot(slot), canHarvest);
            Place(card, slot, screen);
        }
    }

    /// <summary>Card beside its pit, left if it fits, else right; leader line to the plant.</summary>
    private void Place(PlantCard card, int slot, Vector2 screen)
    {
        card.Size = new Vector2(PlantCard.Width, 0);
        Vector2 cardSize = card.GetCombinedMinimumSize();
        cardSize.X = PlantCard.Width;
        Vector2 anchor = _overlay.PlantAnchor(slot);
        var outline = _view.PitOutline(slot);
        float pitLeft = Mathf.Min(outline[0].X, outline[3].X), pitRight = Mathf.Max(outline[1].X, outline[2].X);
        float top = 120, bottom = screen.Y - 96 - cardSize.Y;
        float y0 = Mathf.Clamp(anchor.Y - cardSize.Y * .5f, top, bottom);
        float leftX = pitLeft - 40 - cardSize.X, rightX = pitRight + 40;
        var sides = new List<(float X, bool Left)>();
        if (leftX >= 52) sides.Add((leftX, true));
        if (rightX + cardSize.X <= screen.X - 40) sides.Add((rightX, false));
        if (sides.Count == 0) sides.Add((52, false));

        // Older cards keep their place; this one takes the nearest free spot beside its pit
        // (left first, then right). If nothing is free it overlaps, newest on top.
        (float X, float Y, bool Left)? best = null;
        float bestCost = float.MaxValue;
        foreach (var (x, isLeft) in sides)
        {
            var ys = new List<float> { y0 };
            foreach (var r in _placed) { ys.Add(r.End.Y + 6); ys.Add(r.Position.Y - cardSize.Y - 6); }
            foreach (float y in ys)
            {
                if (y < top - .5f || y > bottom + .5f) continue;
                var rect = new Rect2(x, y, cardSize.X, cardSize.Y);
                if (_placed.Any(r => r.Intersects(rect))) continue;
                float cost = Mathf.Abs(y - y0) + (isLeft == sides[0].Left ? 0 : 60);
                if (cost < bestCost) { bestCost = cost; best = (x, y, isLeft); }
            }
        }
        var (cx, cy, left) = best ?? (sides[0].X, y0, sides[0].Left);
        card.Position = new Vector2(cx, cy);
        card.Size = cardSize;
        _placed.Add(new Rect2(card.Position, cardSize));
        _frame.Leaders.Add((new Vector2(left ? cx + cardSize.X : cx, cy + cardSize.Y * .45f), anchor));
    }

    private readonly List<Rect2> _placed = new();

    // ---- Refresh ----------------------------------------------------------------------------

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
        _level.Text = $"第 {number} 天";
        _timer.Text = $"{remaining / 60}:{remaining % 60:00}";
        _timer.AddThemeColorOverride("font_color", remaining <= 15 && !over ? ChamberStyle.Warn : ChamberStyle.Cream);
        _timeBar.Value = (float)(_game.RemainingSeconds / _game.Level.DurationSeconds);
        _timeBar.QueueRedraw();
        _clock.Text = ClockText();

        double target = _game.Level.TargetVitality;
        _score.Text = $"生机  {_game.Vitality:0.#} / {target:0.#}";
        _score.HorizontalAlignment = HorizontalAlignment.Right;
        _scoreBar.Value = (float)(_game.Vitality / (target * 1.2));
        _scoreBar.Mark = 1 / 1.2f;
        _scoreBar.QueueRedraw();

        _seeds.Text = $"{(_sowing ? "已选  " : "")}种子 ×{_game.AvailableSeeds}";
        _seeds.Disabled = over || _paused || _game.AvailableSeeds == 0;
        _seeds.AddThemeStyleboxOverride("normal", ChamberStyle.Box(ChamberStyle.Panel, _sowing ? ChamberStyle.Cream : ChamberStyle.Gold, _sowing ? 2 : 1));
        if (_seeds.Disabled) _sowing = false;
        _shovel.Text = _shoveling ? "已选  铲子" : "铲子";
        _shovel.Disabled = over || _paused || _game.RewardPoints < _game.Rules.ShovelCost;
        _shovel.AddThemeStyleboxOverride("normal", ChamberStyle.Box(ChamberStyle.Panel, _shoveling ? ChamberStyle.Warn : ChamberStyle.Gold, _shoveling ? 2 : 1));
        if (_shovel.Disabled) _shoveling = false;
        _pause.Text = _readyToStart ? "开始" : _paused ? "继续" : "暂停";
        _pause.Disabled = _speed.Disabled = over;
        _speed.Text = $"{Speeds[_speedIndex]:0.#}×";

        _journalText.Text = $"实验日志 · 第 {number} 天\n\n目标　生机 ≥ {target:0.#}\n时限　{Ui.HoursPerDay:0} 小时\n光照　{_game.Level.LightRange} 格\n新增种子　{_game.Level.AddedSeeds} 颗";

        _overlay.Sowing = _sowing && !_paused && !over;
        _overlay.Shoveling = _shoveling && !_paused && !over;
        RefreshCards();
        _overlay.Marked.Clear();
        _overlay.Marked.UnionWith(_open);
        _overlay.Selected = Selected;
        _overlay.Render(_game);
        RefreshHint(over);
        RefreshResult(over);
        PlaySounds();
        _frame.QueueRedraw();
    }

    /// <summary>「第 1 天　06:00」: the day's game time mapped onto 24 hours (5 s per hour at 120 s).</summary>
    private string ClockText()
    {
        double fraction = Math.Clamp(_game.ElapsedSeconds / _game.Level.DurationSeconds, 0, 1);
        int minutes = (int)Math.Floor(fraction * Ui.HoursPerDay * 60 + 1e-6);
        return $"第 {_game.Level.Number} 天　{minutes / 60:00}:{minutes % 60:00}";
    }

    private void RefreshHint(bool over)
    {
        if (over) _hint.Text = "";
        else if (_messageTime > 0) _hint.Text = _message;
        else if (_paused) _hint.Text = "已暂停　点选植株查看状态";
        else if (_shoveling) _hint.Text = "点植株或残骸铲除　再点铲子取消";
        else if (_sowing) _hint.Text = "点空坑播种";
        else if (_readyToStart) _hint.Text = "点种子再点空坑播种　第一颗种子落地开始计时";
        else _hint.Text = "WASD 移光　Q / E 转向";
        _frame.HintHalfWidth = ChamberStyle.Serif.GetStringSize(_hint.Text, HorizontalAlignment.Left, -1, 17).X * .5f;
    }

    private void RefreshResult(bool over)
    {
        _result.Visible = over;
        if (!over) return;
        int day = _game.Level.Number;
        bool won = _game.Status == LevelRunStatus.Won;
        bool last = day >= _config.Levels.Length;
        _resultTitle.Text = won ? $"第 {day} 天　通过" : $"第 {day} 天　未达标";
        _resultSummary.Text = $"生机 {_game.Vitality:0.#}　/　目标 {_game.Level.TargetVitality:0.#}";
        _next.Visible = won && !last;
        _next.Text = $"进入第 {day + 1} 天";
        _resultNote.Text = !won
            ? $"第 {day} 天未达标，可以重开这一天"
            : _next.Visible
                ? $"下一天保留植株，黑暗清零，补 {_config.Levels[day].AddedSeeds} 颗种子"
                : "重开会回到这一天开始时的样子";
        _retry.Text = "重开这一天";
    }

    // ---- Sound events (音效案 3.3) ------------------------------------------------------------

    private void Remember()
    {
        _seenPlants = _game.Slots.ToArray();
        _seenRemains = _game.Remains.ToArray();
        _seenSeeds = _game.AvailableSeeds;
        _seenLampCell = LampCell();
        _seenStatus = _game.Status;
    }

    private (int, int) LampCell() =>
        ((int)MathF.Round(_game.LampPosition.X), (int)MathF.Round(_game.LampPosition.Y));

    /// <summary>Compare with the last frame and send what happened to the sound director.</summary>
    private void PlaySounds()
    {
        if (_seenPlants.Length != _game.SlotCount) Remember();
        for (int slot = 0; slot < _game.SlotCount; slot++)
        {
            var before = _seenPlants[slot];
            var now = _game.Slots[slot];
            if (before != null && now == null && _game.Remains[slot] != null && _seenRemains[slot] == null)
                _sound.Play(Sfx.C5);
            if (before == null || now == null) continue;
            if (now.Stage > before.Stage) _sound.Play(now.Stage == GrowthStage.Mother ? Sfx.C2 : Sfx.C1);
            if (now.SeedsProduced > before.SeedsProduced) _sound.Play(Sfx.D5);
            if (now.Wilted && !before.Wilted) _sound.Play(Sfx.C3);
            if (!now.Wilted && before.Wilted) _sound.Play(Sfx.C4);
        }
        if (_game.AvailableSeeds > _seenSeeds) _sound.Play(Sfx.D6);
        var cell = LampCell();
        if (cell != _seenLampCell) _sound.Play(Sfx.B1);
        if (_seenStatus == LevelRunStatus.Playing && _game.Status != LevelRunStatus.Playing)
        {
            _sound.Play(_game.Status == LevelRunStatus.Won ? Sfx.E4 : Sfx.E5);
            _sound.Settle();
        }
        int hover = _overlay.Hover;
        if (_sowing && hover != _seenHover && hover >= 0 && _game.IsPitEmpty(hover)) _sound.Play(Sfx.D2);
        _seenHover = hover;
        _sound.SetLoop(Sfx.B4, _game.Status == LevelRunStatus.Playing);
        Remember();
    }
}
