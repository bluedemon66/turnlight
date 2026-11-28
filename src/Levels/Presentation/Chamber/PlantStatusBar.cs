using System;
using Godot;
using OneBeam.Levels.Domain;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// The small status bar above one plant (46×6 screen pixels, never scaled by perspective).
/// Gold growth fills from the left; darkness is a shadow pushed in from the right that
/// darkens whatever it covers. A small triangle under the bar marks the wilt line.
/// Design: 飞书《植物成长与枯萎》1.2 版 3.4–3.6; drawing reference
/// docs/ui-mockup/hpbar/source/draw_hpbar_v7.py bar_D2() and draw_hpbar_v8_stageup.py stage_up().
/// The node's origin is the bar's top-left pixel; FieldOverlay places it.
/// </summary>
public partial class PlantStatusBar : Node2D
{
    public const int Width = 46, Height = 6;
    /// <summary>Gap between the top of the plant picture and the bottom of the bar.</summary>
    public const float Gap = 10;

    // Darkness at which the bar starts warning (flashing red) before the wilt line.
    // Not in the level data yet; 54 = about 2.4 s in the dark before a normal plant wilts.
    public static double NearWiltDarkness { get; set; } = 54;

    private const float WarnPulse = .8f, LitPulse = 1.6f, Feather = 3;
    private const float Follow = .06f;           // smoothing time constant: catches up in about 0.2 s
    private const float HeightMove = .45f;
    private const float Flash = .10f, StageUpTotal = .45f, Sweep = .15f, Rise = 6;
    private const float FadeSeconds = .52f;      // same as the plant sinking into the soil

    private static readonly Color Ring = new("#020306");
    private static readonly Color Track = new("#3A4552");
    private static readonly Color Gold = new("#E5B568");
    private static readonly Color PausedGold = new("#C1A36F");
    private static readonly Color Tip = new("#FFE4AC");
    private static readonly Color Warn = new("#E25947");
    private static readonly Color Mark = new("#B7A992");

    // Current rules state, set by Sync.
    private float _growth, _dark, _wiltLine;
    private double _darkness;
    private bool _wilted, _lit, _mother;
    private GrowthStage _stage;
    private int _seeds;

    // What is drawn: smoothed toward the state.
    private float _shownGrowth, _shownDark;
    private float _heightFrom, _heightTo, _heightShown;
    private double _heightStart = double.NegativeInfinity;
    private double _stageUpStart = double.NegativeInfinity;
    private bool _seedPop;
    private double _fadeStart = double.NegativeInfinity;
    private bool _synced;

    public bool Selected { get; set; }
    /// <summary>Screen point where the plant meets the soil; the bar floats above it.</summary>
    public Vector2 Anchor { get; set; }
    public bool Dying => !double.IsNegativeInfinity(_fadeStart);
    public bool Finished => Dying && Seconds - _fadeStart >= FadeSeconds;
    /// <summary>True while the stage-up / seed animation plays (for tests).</summary>
    public bool CelebratingStage => Seconds - _stageUpStart < StageUpTotal;
    /// <summary>The bar's height above the soil point it follows, in pixels, as currently shown.</summary>
    public float ShownHeight => _heightShown;

    /// <summary>Tests only: a fixed clock in seconds (negative = real time), for still frames of the animations.</summary>
    public double TestClock { get; set; } = -1;
    private double Seconds => TestClock >= 0 ? TestClock : Time.GetTicksMsec() / 1000.0;

    private static GrowthRules? _testRules;

    /// <summary>
    /// Tests only (tests/plant_status_bar_sheet.gd): same as <see cref="Sync"/>, with the rules
    /// of the standard level and the plant given field by field.
    /// </summary>
    public void SyncForTest(int stage, double growth, double darkness, bool wilted, int seeds, bool lit, bool snap)
    {
        _testRules ??= LevelDemoConfig.Load("res://data/camera-levels.json").Rules;
        Sync(new LevelPlant((GrowthStage)stage, growth, darkness, wilted, seeds), _testRules, lit, 0, snap);
    }

    /// <summary>
    /// Take the plant's state. <paramref name="pictureHeight"/> is the plant's current picture
    /// height above the soil. <paramref name="snap"/> skips every transition (new plant, restart).
    /// </summary>
    public void Sync(LevelPlant plant, GrowthRules rules, bool lit, float pictureHeight, bool snap)
    {
        var rule = rules.Stages[(int)plant.Stage];
        double goal = rule.GrowthToNext ?? (rules.FirstSeedGrowth + plant.SeedsProduced * rules.SeedGrowthIncrement);
        bool stageUp = _synced && (plant.Stage > _stage || plant.SeedsProduced > _seeds);
        _seedPop = stageUp && plant.Stage == GrowthStage.Mother && _stage == GrowthStage.Mother;

        _stage = plant.Stage;
        _seeds = plant.SeedsProduced;
        _growth = Mathf.Clamp((float)(plant.Growth / goal), 0, 1);
        _darkness = plant.Darkness;
        _dark = Mathf.Clamp((float)(plant.Darkness / rule.DieAt), 0, 1);
        _wiltLine = (float)(rule.WiltAt / rule.DieAt);
        _wilted = plant.Wilted;
        _lit = lit;
        _mother = plant.Stage == GrowthStage.Mother;

        if (snap || !_synced)
        {
            _shownGrowth = _growth;
            _shownDark = _dark;
            _heightFrom = _heightTo = _heightShown = pictureHeight;
            _heightStart = double.NegativeInfinity;
            _stageUpStart = double.NegativeInfinity;
            _synced = true;
            return;
        }
        if (stageUp) _stageUpStart = Seconds;
        if (!Mathf.IsEqualApprox(pictureHeight, _heightTo))
        {
            _heightFrom = _heightShown;
            _heightTo = pictureHeight;
            _heightStart = Seconds;
        }
    }

    /// <summary>The plant died: fade out together with it.</summary>
    public void FadeOut()
    {
        if (!Dying) _fadeStart = Seconds;
    }

    public override void _Process(double delta)
    {
        double now = Seconds;
        float h = Ease((float)((now - _heightStart) / HeightMove));
        _heightShown = Mathf.Lerp(_heightFrom, _heightTo, h);
        Position = new Vector2(Mathf.Round(Anchor.X - Width * .5f), Mathf.Round(Anchor.Y - _heightShown - Gap - Height));

        float follow = 1 - Mathf.Exp(-(float)delta / Follow);
        if (CelebratingStage)
        {
            // The bar is drawn full, then sweeps up from zero; the shadow holds still.
            // Keep the smoothed growth at the new value so it never slides back afterwards.
            _shownGrowth = _growth;
        }
        else
        {
            _shownGrowth = Mathf.Lerp(_shownGrowth, _growth, follow);
            _shownDark = Mathf.Lerp(_shownDark, _dark, follow);
        }

        float alpha = Selected || _lit ? 1 : .82f;
        if (Dying) alpha *= 1 - Mathf.Clamp((float)((now - _fadeStart) / FadeSeconds), 0, 1);
        Modulate = new Color(1, 1, 1, alpha);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_synced) return;
        double now = Seconds;
        float t = (float)(now - _stageUpStart);
        bool celebrating = t < StageUpTotal;
        float warn = !_wilted && _darkness >= NearWiltDarkness && _dark < _wiltLine
            ? .5f - .5f * Mathf.Cos(Mathf.Tau * (float)now / WarnPulse)
            : 0;

        float growth = _shownGrowth;
        if (celebrating) growth = t < Flash ? 1 : _growth * Ease((t - Flash) / Sweep);

        if (warn > 0) Halo(warn);
        DrawRect(new Rect2(-1, -1, Width + 2, Height + 2), Ring);
        Columns(growth, _shownDark, warn, now);
        WiltMark();
        if (_mother) SeedIcon(1, Mark, true);
        if (celebrating) StageUp(t);
    }

    private void Columns(float growth, float dark, float warn, double now)
    {
        int gx = Mathf.RoundToInt(Width * growth);
        float edge = Width * (1 - dark);
        var gold = _wilted ? PausedGold : Gold;
        float pulse = .5f - .5f * Mathf.Cos(Mathf.Tau * (float)now / LitPulse);
        for (int px = 0; px < Width; px++)
        {
            var col = px < gx ? gold : Track;
            int i = gx - 1 - px;
            // In the light the front 2 px of the growth glow softly; drawn before the shadow so it darkens under it.
            if (_lit && !_wilted && i >= 0 && i < 2)
                col = col.Lerp(Tip, (.45f + .55f * pulse) * (1 - i / 2f));
            float f = dark > 0 ? Mathf.Clamp((px + .5f - edge) / Feather + .5f, 0, 1) : 0;
            col = col.Lerp(Colors.Black, .58f * f);
            if (dark > 0 && f > 0)
            {
                float red = _wilted ? 1 : warn;
                float nearEdge = 1 - Mathf.Min(1, Mathf.Abs(px + .5f - edge) / 2);
                col = col.Lerp(Warn, red * nearEdge * .9f + (_wilted ? .12f : 0) * f);
            }
            DrawRect(new Rect2(px, 0, 1, Height), col);
            if (px < gx && !_wilted)
            {
                DrawRect(new Rect2(px, 0, 1, 1), col.Lerp(Colors.White, .18f));
                DrawRect(new Rect2(px, Height - 1, 1, 1), col.Lerp(Colors.Black, .18f));
            }
        }
    }

    /// <summary>Soft red light around the bar while it warns (about a 2.5 px blur).</summary>
    private void Halo(float strength)
    {
        for (int e = 5; e >= 1; e--)
        {
            float a = .47f * strength * .28f * (1 - (e - 1) / 5f);
            DrawRect(new Rect2(-e, -e, Width + 2 * e, Height + 2 * e), Warn with { A = a });
        }
    }

    /// <summary>Upward triangle 1 px under the bar, at the wilt line.</summary>
    private void WiltMark()
    {
        float tx = Mathf.Round(Width * (1 - _wiltLine)), ty = Height + 1;
        var col = _wilted || _darkness >= NearWiltDarkness ? Warn : Mark;
        DrawColoredPolygon(new[] { new Vector2(tx, ty - 1), new Vector2(tx - 4, ty + 3.5f), new Vector2(tx + 4, ty + 3.5f) }, Ring);
        DrawColoredPolygon(new[] { new Vector2(tx, ty), new Vector2(tx - 3, ty + 3), new Vector2(tx + 3, ty + 3) }, col);
    }

    /// <summary>Seed icon left of a mother's bar: about 4×6, light with a dark outline.</summary>
    private void SeedIcon(float scale, Color color, bool outline)
    {
        var center = new Vector2(-5.5f, Height * .5f);
        if (outline) Ellipse(center, new Vector2(2.5f, 3) * scale, Ring);
        Ellipse(center, new Vector2(1.5f, 2) * scale, color);
    }

    private void Ellipse(Vector2 center, Vector2 radius, Color color)
    {
        DrawSetTransform(center, 0, radius);
        DrawCircle(Vector2.Zero, 1, color);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Stage-up / new seed: flash the full bar, then a thin light line floats off the top.</summary>
    private void StageUp(float t)
    {
        float edge = _shownDark > 0 ? Mathf.Round(Width * (1 - _shownDark)) : Width;
        if (t < Flash)
        {
            float a = 1 - Mathf.Abs(2 * t / Flash - 1);
            float pop = Mathf.Round(a);
            DrawRect(new Rect2(0, -pop, edge, Height + 2 * pop), Tip with { A = .9f * a });
            if (_seedPop) SeedIcon(1 + .6f * a, Mark, true);
            return;
        }
        float u = (t - Flash) / (StageUpTotal - Flash);
        float dy = Mathf.Round(1 + Rise * Ease(u));
        DrawRect(new Rect2(0, -dy - 2, edge, 2), Tip with { A = .86f * Mathf.Pow(1 - u, 1.5f) });
    }

    private static float Ease(float u)
    {
        u = Mathf.Clamp(u, 0, 1);
        return 1 - (1 - u) * (1 - u) * (1 - u);
    }
}
