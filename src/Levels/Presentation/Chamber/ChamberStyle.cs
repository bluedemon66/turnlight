using Godot;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>Colors, fonts and widget factories for the camera-feed interface.</summary>
public static class ChamberStyle
{
    public static readonly Color Gold = new("d6b47c");
    public static readonly Color GoldDim = new(.84f, .7f, .48f, .45f);
    public static readonly Color Cream = new("efe5cf");
    public static readonly Color Muted = new("98a0ad");
    public static readonly Color Warn = new("e0826b");
    public static readonly Color Engrave = new(.84f, .72f, .5f, .7f);
    public static readonly Color Panel = new(.025f, .035f, .055f, .74f);
    public static readonly Color Track = new(.13f, .15f, .2f, .9f);

    public static readonly Font Serif = new SystemFont
    {
        FontNames = new[] { "Noto Serif SC", "Source Han Serif SC", "Songti SC", "SimSun", "serif" },
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    public static readonly Font Mono = new SystemFont
    {
        FontNames = new[] { "Consolas", "Cascadia Mono", "Courier New", "monospace" },
    };

    public static Label Text(string name, string text, int size, Color color, Font? font = null)
    {
        var label = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", font ?? Serif);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .7f));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeConstantOverride("shadow_outline_size", 4);
        return label;
    }

    public static Button Button(string name, string text, int size = 20, bool primary = false)
    {
        var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontOverride("font", Serif);
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", primary ? new Color("1a140c") : Cream);
        button.AddThemeColorOverride("font_hover_color", primary ? new Color("1a140c") : Colors.White);
        button.AddThemeColorOverride("font_pressed_color", primary ? new Color("1a140c") : Gold);
        button.AddThemeColorOverride("font_disabled_color", Muted with { A = .5f });
        button.AddThemeStyleboxOverride("normal", Box(primary ? Gold : Panel, Gold));
        button.AddThemeStyleboxOverride("hover", Box(primary ? Gold.Lightened(.15f) : new Color(.06f, .07f, .1f, .85f), Gold.Lightened(.2f)));
        button.AddThemeStyleboxOverride("pressed", Box(primary ? Gold.Darkened(.1f) : new Color(.1f, .09f, .06f, .9f), Gold));
        button.AddThemeStyleboxOverride("disabled", Box(Panel with { A = .4f }, GoldDim with { A = .25f }));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    public static StyleBoxFlat Box(Color fill, Color border, int width = 1) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = width, BorderWidthTop = width, BorderWidthRight = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    public static void Place(Control control, float x, float y, float width, float height)
    {
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(width, height);
    }
}

/// <summary>A thin progress line with an optional target tick.</summary>
public partial class MeterBar : Control
{
    public float Value { get; set; }
    public float Mark { get; set; } = -1;
    public Color Fill { get; set; } = ChamberStyle.Gold;

    public MeterBar() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, ChamberStyle.Track);
        DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * Mathf.Clamp(Value, 0, 1), Size.Y)), Fill);
        DrawRect(r, ChamberStyle.GoldDim, false, 1);
        if (Mark >= 0)
        {
            float x = Size.X * Mathf.Clamp(Mark, 0, 1);
            DrawLine(new Vector2(x, -5), new Vector2(x, Size.Y + 5), ChamberStyle.Cream, 1.5f);
        }
    }
}

/// <summary>
/// Camera viewfinder drawn over the whole screen: corner brackets, rules beside the hint,
/// and the leader line from the info card to the selected plant.
/// </summary>
public partial class CameraFrame : Control
{
    public Vector2? LeaderFrom { get; set; }
    public Vector2? LeaderTo { get; set; }
    public float HintHalfWidth { get; set; } = 160;

    public CameraFrame()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var c = ChamberStyle.Cream with { A = .75f };
        const float inset = 14, arm = 34;
        var s = Size;
        foreach (var (corner, dir) in new[]
        {
            (new Vector2(inset, inset), new Vector2(1, 1)),
            (new Vector2(s.X - inset, inset), new Vector2(-1, 1)),
            (new Vector2(inset, s.Y - inset), new Vector2(1, -1)),
            (new Vector2(s.X - inset, s.Y - inset), new Vector2(-1, -1)),
        })
        {
            DrawLine(corner, corner + new Vector2(dir.X * arm, 0), c, 2);
            DrawLine(corner, corner + new Vector2(0, dir.Y * arm), c, 2);
        }
        // Rule under the level name and divider before the timer.
        DrawLine(new Vector2(40, 96), new Vector2(176, 96), ChamberStyle.GoldDim, 1);
        DrawLine(new Vector2(194, 50), new Vector2(194, 100), ChamberStyle.GoldDim, 1);
        float mid = s.X * .5f, y = s.Y - 41;
        DrawLine(new Vector2(mid - HintHalfWidth - 110, y), new Vector2(mid - HintHalfWidth - 14, y), ChamberStyle.GoldDim, 1);
        DrawLine(new Vector2(mid + HintHalfWidth + 14, y), new Vector2(mid + HintHalfWidth + 110, y), ChamberStyle.GoldDim, 1);
        if (LeaderFrom is { } from && LeaderTo is { } to)
        {
            DrawLine(from, to, ChamberStyle.Gold with { A = .8f }, 1.2f, true);
            DrawCircle(to, 3, ChamberStyle.Cream);
        }
    }
}
