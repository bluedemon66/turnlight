using Godot;
using OneBeam.Domain;

namespace OneBeam.Presentation;

public partial class PlantView : Node2D
{
    private PlantState? _plant;
    private bool _lit;
    private bool _selected;

    public void Render(PlantState plant, bool lit, bool selected)
    {
        _plant = plant;
        _lit = lit;
        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_plant is not { } p) return;
        Color leaf = (_lit ? new Color("a7d681") : new Color("608f77")).Lerp(
            new Color("a8886c"), Mathf.Clamp(p.Darkness, 0, 1));
        Color pale = leaf.Lightened(0.24f);
        float lean = p.Darkness * 6;
        DrawSetTransform(new Vector2(0, 2), 0, new Vector2(1, 0.34f));
        DrawCircle(Vector2.Zero, 12, new Color("0b1b225f"), true, -1, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (p.Stage == PlantStage.Seed)
        {
            DrawColoredPolygon(new Vector2[] { new(-6, 0), new(-2, -6), new(4, -5), new(7, 0), new(1, 4), new(-3, 3) }, leaf);
            DrawLine(new(-2, 1), new(2, -3), pale, 1.3f, true);
        }
        else if (p.SpeciesId == SpeciesRules.SkyBambooId)
        {
            DrawBamboo(p, leaf, pale);
        }
        else
        {
            bool mature = p.Stage == PlantStage.Mature;
            Vector2 tip = new(lean, mature ? -24 + lean : -13 + lean * 0.65f);
            DrawLine(Vector2.Zero, tip, leaf, 2.3f, true);
            DrawLeaf(tip * 0.35f, new(-10, -7), leaf);
            DrawLeaf(tip * 0.6f, new(10, -9), pale);
            if (mature && p.SpeciesId == SpeciesRules.LightMotherId)
            {
                DrawCircle(tip, 13, new Color("e6c17529"), true, -1, true);
                DrawCircle(tip, 6, new Color("e6b16d"), true, -1, true);
                DrawCircle(tip + new Vector2(-1,-1), 3, new Color("fff1bc"), true, -1, true);
                DrawLeaf(tip + new Vector2(-2,4), new(-6,-7), leaf);
                DrawLeaf(tip + new Vector2(2,4), new(6,-7), pale);
            }
            else if (mature)
            {
                for (int i = 0; i < 8; ++i)
                {
                    var petal = Vector2.Up.Rotated(i * Mathf.Tau / 8);
                    DrawCircle(tip + petal * 5, 3.6f, new Color("f5ce76"), true, -1, true);
                }
                DrawCircle(tip, 4, new Color("795638"), true, -1, true);
            }
        }
        if (p.BoostSteps > 0)
            DrawCircle(new(9, -6), 2.5f, new Color("ffd583"), true, -1, true);
        if (_selected)
        {
            DrawArc(Vector2.Zero, 15, 0.18f, Mathf.Pi-0.18f, 20, new Color("f4e6ab"), 1.5f, true);
            DrawArc(Vector2.Zero, 15, Mathf.Pi+0.18f, Mathf.Tau-0.18f, 20, new Color("f4e6ab"), 1.5f, true);
        }
    }

    private void DrawBamboo(PlantState p, Color leaf, Color pale)
    {
        var direction = new Vector2(p.Direction.X, p.Direction.Y).Normalized();
        if (direction == Vector2.Zero) direction = Vector2.Up;
        float length = Mathf.Max(9, p.Height);
        Vector2 end = direction * length;
        // The tip visual follows actual geometric height and growth direction.
        DrawLine(Vector2.Zero, end, leaf, 4.5f, true);
        Vector2 across = direction.Orthogonal();
        int segments = Mathf.Clamp(Mathf.FloorToInt(length / 17), 1, 70);
        for (int i = 1; i <= segments; i++)
        {
            var joint = end * i / segments;
            DrawLine(joint - across*3, joint + across*3, pale, 1.6f, true);
            DrawLeaf(joint, across * (i % 2 == 0 ? 9 : -9) + direction*7, leaf);
        }
    }

    private void DrawLeaf(Vector2 start, Vector2 delta, Color color)
    {
        var middle = start + delta * 0.5f;
        var width = delta.Orthogonal().Normalized() * 2.8f;
        DrawColoredPolygon(new[] { start, middle + width, start + delta, middle - width }, color);
    }
}
