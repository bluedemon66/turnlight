using System;
using Godot;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// One lantern grass, drawn as thin horizontal strips of its picture that are bent and
/// stretched along a few landmarks (ground, foliage, lantern, calyx, stem tip).
/// Ported from 植物动画制作/lantern-grass-motion.html: same landmarks, breathing,
/// and stage motions (emerge from the seed, grow, wilt), plus the reverse of wilting
/// for recovery and a short sink into the soil on death.
/// Breathing runs at normal speed; stage motions run three times faster, as approved.
/// The node's origin is the point where the stem meets the soil; roots are not drawn.
/// </summary>
public partial class LanternPlant : Node2D
{
    public const int Seed = 0, Seedling = 1, Normal = 2, Mother = 3, Wilted = 4;

    private const float MotionRate = 3;
    private const float EmergeMs = 1520 / MotionRate, GrowMs = 1340 / MotionRate, WiltMs = 2240 / MotionRate;
    private const float DeathMs = 520;
    private const float BreathPeriod = 3.8f, SwayPeriod = 5.1f;
    private const int RowStep = 6;

    private static readonly string[] ArtPaths =
    {
        "res://art/plants/lantern/lantern_0_seed.png",
        "res://art/plants/lantern/lantern_1_seedling.png",
        "res://art/plants/lantern/lantern_2_normal.png",
        "res://art/plants/lantern/lantern_3_mother.png",
        "res://art/plants/lantern/lantern_4_wilted.png",
    };
    private static Texture2D[]? _art;
    private static readonly Vector2 SeedOrigin = new(102, 246);
    // How strongly each picture breathes: seed still, mother a little less, wilted barely.
    private static readonly float[] Breathing = { 0, 1, 1, .88f, .3f };

    // Landmarks per picture: source x, source y, width of the plant there.
    // Ground, foliage, bottom of lantern, lantern center, calyx, stem apex.
    private static readonly Vector3[][] Profiles =
    {
        Array.Empty<Vector3>(),
        new Vector3[] { new(146, 274, 112), new(144, 211, 130), new(116, 125, 66), new(111, 102, 61), new(111, 75, 76), new(144, 13, 93) },
        new Vector3[] { new(177, 474, 205), new(174, 398, 251), new(173, 300, 160), new(171, 221, 169), new(170, 135, 169), new(211, 14, 132) },
        new Vector3[] { new(276, 552, 350), new(282, 438, 470), new(286, 318, 208), new(278, 239, 197), new(276, 146, 216), new(325, 13, 156) },
        new Vector3[] { new(193, 346, 257), new(188, 315, 296), new(111, 268, 120), new(104, 207, 133), new(102, 136, 127), new(159, 14, 161) },
    };

    private enum Motion { None, Emerge, Grow, Wilt, Recover, Die }

    /// <summary>Screen pixels per source pixel of the pictures.</summary>
    public float PixelScale { get; set; } = .2f;
    /// <summary>Seconds added to the breathing clock so neighbours do not move in step.</summary>
    public float Phase { get; set; }
    /// <summary>A wilted seed has no picture of its own; it just darkens.</summary>
    public bool DarkSeed { get; set; }

    /// <summary>No breathing or sway: used for remains (placeholder: the wilted picture, darkened).</summary>
    public bool Still { get; set; }

    public int Index { get; private set; } = -1;
    public bool Finished { get; private set; }

    private int _previous;
    private Motion _motion;
    private double _motionStart;

    private static Texture2D Art(int index)
    {
        _art ??= Array.ConvertAll(ArtPaths, GD.Load<Texture2D>);
        return _art[index];
    }

    /// <summary>Height above the soil of the picture at <paramref name="index"/>, in screen pixels.</summary>
    public float VisibleHeight(int index) =>
        (index == Seed ? SeedOrigin.Y * .5f : Profiles[index][0].Y) * PixelScale;

    /// <summary>Show a picture; animate the change unless this is the first state or <paramref name="animate"/> is false.</summary>
    public void Show(int index, bool animate)
    {
        if (index == Index && _motion != Motion.Die) return;
        int from = Index;
        Index = index;
        _previous = from;
        if (!animate || from < 0)
        {
            _motion = Motion.None;
            return;
        }
        _motion = from == Wilted && index != Seed ? Motion.Recover
            : index == Wilted ? Motion.Wilt
            : from == Seed || index == Seed ? Motion.Emerge
            : Motion.Grow;
        _motionStart = Now;
    }

    public void Die()
    {
        if (_motion == Motion.Die) return;
        _motion = Motion.Die;
        _motionStart = Now;
    }

    private static double Now => Time.GetTicksMsec();

    private float Progress => Clamp((float)((Now - _motionStart) / (_motion switch
    {
        Motion.Emerge => EmergeMs,
        Motion.Grow => GrowMs,
        Motion.Die => DeathMs,
        _ => WiltMs
    })));

    public override void _Process(double delta)
    {
        if (_motion != Motion.None && Progress >= 1)
        {
            if (_motion == Motion.Die)
            {
                Finished = true;
                Visible = false;
            }
            _motion = Motion.None;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Index < 0 || Finished) return;
        float seconds = (float)(Now / 1000.0) + Phase;
        float p = Progress;
        if (_motion == Motion.None || p >= 1)
        {
            Paint(Index, seconds);
            return;
        }
        switch (_motion)
        {
            case Motion.Emerge: PaintEmergence(seconds, p); break;
            case Motion.Grow: PaintGrowth(seconds, p); break;
            case Motion.Wilt: PaintWilt(seconds, _previous, p); break;
            case Motion.Recover: PaintWilt(seconds, Index, 1 - p); break;
            case Motion.Die: PaintDeath(seconds, p); break;
        }
    }

    // ---- Stage motions -------------------------------------------------------------

    private void PaintEmergence(float seconds, float progress)
    {
        float t = _previous == Seed ? progress : 1 - progress;
        int growing = _previous == Seed ? Index : _previous;
        if (t < .42f) DrawSeed(PhaseOf(t, .04f, .42f), Colors.White);
        else Paint(growing, seconds, EmergePose(growing, t), PhaseOf(t, .76f, 1));
    }

    private void PaintGrowth(float seconds, float progress)
    {
        var pose = MorphPose(_previous, Index, progress, false);
        float motion = 1 - .45f * PhaseOf(progress, 0, .22f) * (1 - PhaseOf(progress, .75f, 1));
        float unfolding = (1 - PhaseOf(progress, .20f, .87f)) * .56f;
        if (progress >= .51f) Paint(Index, seconds, pose, motion, unfolding);
        else Paint(_previous, seconds, pose, motion);
    }

    /// <summary>Wilting from <paramref name="healthy"/>; recovery plays it backwards.</summary>
    private void PaintWilt(float seconds, int healthy, float progress)
    {
        var pose = MorphPose(healthy, Wilted, progress, true);
        float motion = 1 - .8f * PhaseOf(progress, 0, .45f);
        if (progress >= .67f)
        {
            Paint(Wilted, seconds, pose, Lerp(motion, 1, PhaseOf(progress, .86f, 1)));
            return;
        }
        float recession = PhaseOf(progress, .08f, .76f) * .64f;
        float dryness = PhaseOf(progress, .10f, .67f);
        Paint(healthy, seconds, pose, motion, recession, Dry(dryness));
    }

    private void PaintDeath(float seconds, float progress)
    {
        int index = Index == Seed ? Seed : Index;
        var tint = Dry(1) * new Color(.75f, .75f, .75f, 1 - PhaseOf(progress, .55f, 1));
        if (index == Seed)
        {
            DrawSeed(PhaseOf(progress, 0, 1), tint);
            return;
        }
        Paint(index, seconds, EmergePose(index, 1 - .85f * PhaseOf(progress, 0, 1)), 0, 0, tint);
    }

    private static Color Dry(float dryness) =>
        (Colors.White.Lerp(new Color(.86f, .76f, .6f), dryness * .6f) * (1 - dryness * .23f)) with { A = 1 };

    // ---- Poses --------------------------------------------------------------------

    private static Vector3[] MorphPose(int from, int to, float t, bool wilting)
    {
        var first = Profiles[from];
        var last = Profiles[to];
        Vector2[] timing = wilting
            ? new Vector2[] { new(0, 1), new(.20f, .94f), new(.12f, .81f), new(.05f, .72f), new(.02f, .68f), new(0, .65f) }
            : new Vector2[] { new(0, 1), new(.02f, .72f), new(.10f, .82f), new(.17f, .88f), new(.21f, .92f), new(.25f, .96f) };
        float arc = wilting ? PhaseOf(t, .03f, .53f) * (1 - PhaseOf(t, .65f, 1)) : 0;
        float[] lean = { 0, -4, -22, -28, -24, -37 };
        float[] sag = { 0, 2, 10, 9, 3, 3 };
        float[] pinch = { 0, .10f, .20f, .28f, .14f, .08f };
        float unfold = wilting ? 0 : PhaseOf(t, .47f, .77f) * (1 - PhaseOf(t, .77f, 1));
        float rise = 0;
        var pose = new Vector3[first.Length];
        for (int i = 0; i < first.Length; i++)
        {
            float progress = PhaseOf(t, timing[i].X, timing[i].Y);
            if (i > 0)
            {
                float span = Lerp(first[i - 1].Y - first[i].Y, last[i - 1].Y - last[i].Y, progress);
                rise += Mathf.Max(3, span - sag[i] * arc);
            }
            float center = Lerp(first[i].X - first[0].X, last[i].X - last[0].X, progress) + lean[i] * arc;
            float width = Lerp(first[i].Z, last[i].Z, progress) *
                (1 - pinch[i] * arc + (i == 1 ? .05f * unfold : 0));
            pose[i] = new Vector3(center, rise, width);
        }
        return pose;
    }

    private static Vector3[] EmergePose(int index, float t)
    {
        var joints = Profiles[index];
        float rootX = joints[0].X;
        float rise = 0;
        var pose = new Vector3[joints.Length];
        float flare = PhaseOf(t, .49f, .79f) * (1 - PhaseOf(t, .79f, 1));
        for (int i = 0; i < joints.Length; i++)
        {
            float delay = i * .075f;
            float open = PhaseOf(t, .10f + delay, .58f + delay);
            if (i > 0) rise += (joints[i - 1].Y - joints[i].Y) * Lerp(.18f, 1, open);
            pose[i] = new Vector3((joints[i].X - rootX) * Lerp(.15f, 1, open) + Mathf.Sin(i) * flare * 4, rise,
                joints[i].Z * Lerp(.35f, 1, open) * (1 + (i == 1 ? .06f * flare : 0)));
        }
        return pose;
    }

    // ---- Drawing ------------------------------------------------------------------

    private void DrawSeed(float amount, Color tint)
    {
        var image = Art(Seed);
        float s = PixelScale * .5f;
        float sink = 42 * amount, widen = 1 + .08f * amount, shorten = 1 - .26f * amount;
        float w = image.GetWidth() * s * widen, h = image.GetHeight() * s * shorten;
        float x = -SeedOrigin.X * s * widen, top = (sink - SeedOrigin.Y * shorten) * s;
        float visible = Mathf.Min(h, -top);   // only the part above the soil line
        if (visible <= 0) return;
        if (DarkSeed) tint *= new Color(.55f, .55f, .6f);
        DrawTextureRectRegion(image, new Rect2(x, top, w, visible),
            new Rect2(0, 0, image.GetWidth(), image.GetHeight() * visible / h), tint);
    }

    private void Paint(int index, float seconds) => Paint(index, seconds, null, 1);

    private void Paint(int index, float seconds, Vector3[]? pose, float motion, float branchFold = 0, Color? tint = null)
    {
        var color = tint ?? Colors.White;
        if (index == Seed)
        {
            DrawSeed(0, color);
            return;
        }
        var image = Art(index);
        var joints = Profiles[index];
        var mapped = SpriteRows(index, pose);
        var source = SourceRows(index);
        float rootY = joints[0].Y;
        float width = image.GetWidth(), height = image.GetHeight();
        float response = Still ? 0 : Breathing[index] * motion;
        float breath = Mathf.Sin(seconds * Mathf.Tau / BreathPeriod);
        float sway = Mathf.Sin(seconds * Mathf.Tau / SwayPeriod - .72f);
        float s = PixelScale;

        // Screen geometry of one source row: left edge, width of one source pixel, y.
        (float Left, float Unit, float Y, float Fold) Row(float row)
        {
            var (cx, rise, rowScale) = SampleRow(joints, mapped, row);
            float sourceX = SampleRow(joints, source, row).X;
            float bend = Clamp(rise / rootY);
            float wind = bend * bend * response;
            float wobble = wind * (sway * 10 + Mathf.Sin(seconds * 2 - rise / rootY * 2) * 2.1f);
            float widen = rowScale * (1 + breath * .012f * wind);
            float y = -rise * (1 + .025f * breath * bend * response) * s;
            float fold = index == Mother ? branchFold * Smooth((rootY - row) / 145) : 0;
            return ((cx + wobble - sourceX * widen) * s, s * widen, y, fold);
        }

        const float outer = 160;   // the mother's side lanterns live in the outer 160 columns
        var top = Row(0);
        for (int r = 0; r < rootY; r += RowStep)
        {
            float end = Mathf.Min(r + RowStep, rootY);
            var bottom = Row(end);
            float v0 = r / height, v1 = end / height;
            if (index == Mother && (top.Fold > .005f || bottom.Fold > .005f))
            {
                // Side lanterns draw inward towards the main stem.
                float[] Columns((float Left, float Unit, float Y, float Fold) g)
                {
                    float inner = outer * g.Unit, outerW = inner * (1 - g.Fold);
                    float right = g.Left + (width - outer) * g.Unit;
                    return new[] { g.Left + inner - outerW, g.Left + inner, right, right + outerW };
                }
                var a = Columns(top);
                var b = Columns(bottom);
                float[] u = { 0, outer / width, (width - outer) / width, 1 };
                for (int k = 0; k < 3; k++)
                    Strip(image, a[k], a[k + 1], top.Y, b[k], b[k + 1], bottom.Y, u[k], u[k + 1], v0, v1, color);
            }
            else
            {
                Strip(image, top.Left, top.Left + width * top.Unit, top.Y,
                    bottom.Left, bottom.Left + width * bottom.Unit, bottom.Y, 0, 1, v0, v1, color);
            }
            top = bottom;
        }
    }

    private void Strip(Texture2D image, float topLeft, float topRight, float topY,
        float bottomLeft, float bottomRight, float bottomY, float u0, float u1, float v0, float v1, Color color)
    {
        if (bottomY - topY < .01f && topY - bottomY < .01f) return;
        DrawPrimitive(
            new[] { new Vector2(topLeft, topY), new Vector2(topRight, topY), new Vector2(bottomRight, bottomY), new Vector2(bottomLeft, bottomY) },
            new[] { color, color, color, color },
            new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) },
            image);
    }

    private static Vector3[] SpriteRows(int index, Vector3[]? pose)
    {
        var joints = Profiles[index];
        float rootX = joints[0].X, rootY = joints[0].Y;
        var rows = new Vector3[joints.Length];
        for (int i = 0; i < joints.Length; i++)
            rows[i] = pose == null
                ? new Vector3(joints[i].X - rootX, rootY - joints[i].Y, 1)
                : new Vector3(pose[i].X, pose[i].Y, i == 0 ? 1 : Mathf.Clamp(pose[i].Z / joints[i].Z, .26f, 3));
        return rows;
    }

    private static Vector3[] SourceRows(int index)
    {
        var joints = Profiles[index];
        float rootY = joints[0].Y;
        var rows = new Vector3[joints.Length];
        for (int i = 0; i < joints.Length; i++) rows[i] = new Vector3(joints[i].X, rootY - joints[i].Y, 1);
        return rows;
    }

    /// <summary>Center x, height above ground and width scale at a source row.</summary>
    private static (float X, float Rise, float Width) SampleRow(Vector3[] joints, Vector3[] mapped, float row)
    {
        int last = mapped.Length - 1;
        if (row >= joints[0].Y) return (mapped[0].X, joints[0].Y - row, 1);
        if (row <= joints[last].Y)
            return (mapped[last].X, mapped[last].Y + (joints[last].Y - row) * Mathf.Min(mapped[last].Z, 1), mapped[last].Z);
        int i = 1;
        while (row < joints[i].Y) i++;
        float weight = (joints[i - 1].Y - row) / (joints[i - 1].Y - joints[i].Y);
        return (Lerp(mapped[i - 1].X, mapped[i].X, weight), Lerp(mapped[i - 1].Y, mapped[i].Y, weight),
            Lerp(mapped[i - 1].Z, mapped[i].Z, weight));
    }

    private static float Clamp(float t) => Mathf.Clamp(t, 0, 1);
    private static float Smooth(float t) { t = Clamp(t); return t * t * (3 - 2 * t); }
    private static float PhaseOf(float t, float from, float to) => Smooth((t - from) / (to - from));
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
