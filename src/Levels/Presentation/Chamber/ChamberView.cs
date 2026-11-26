using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>
/// The inside of the biosphere chamber as seen by its camera: a stone plate with an
/// N x N grid of sunken pits, a dome of triangular glass panels on struts, and one beam
/// of light from a lamp that stays above the glass and is never shown.
/// Cell (column, row) sits at x = column - (N - 1) / 2, z = row - (N - 1) / 2; row 0 is
/// the back row, farthest from the camera.
/// </summary>
public partial class ChamberView : Node3D
{
    public const float PitInset = .09f;
    public const float PitDepth = .22f;
    public const float DomeRadius = 6f;
    // The dome is narrower side to side, so its panels frame the left and right of the picture.
    public const float DomeWidth = .66f;
    private const float LampHeight = 14f;
    private const int MaxShadowSegments = 48;

    private static readonly float[] RingElevations = { 0, 19, 37, 54, 70 };
    private const int PanelsPerRing = 11;

    public int GridSize { get; private set; } = 5;
    public Camera3D Camera { get; private set; } = null!;

    private ShaderMaterial _stone = null!, _soil = null!, _glass = null!, _beamMaterial = null!;
    private readonly ImmediateMesh _beamMesh = new();
    private readonly List<(Vector3 A, Vector3 B)> _struts = new();

    public Vector2 BeamCenter { get; private set; }
    public Vector3 EntryPoint { get; private set; }

    public void Build(int gridSize)
    {
        GridSize = gridSize;
        _stone = Material("res://shaders/chamber_floor.gdshader");
        _stone.SetShaderParameter("albedo_tex", Load("res://art/textures/floor_stone.png"));
        _stone.SetShaderParameter("clip_radius", DomeRadius);
        _stone.SetShaderParameter("clip_xscale", DomeWidth);
        _stone.SetShaderParameter("uv_scale", .28f);
        _soil = Material("res://shaders/chamber_floor.gdshader");
        _soil.SetShaderParameter("albedo_tex", Load("res://art/textures/soil.png"));
        _soil.SetShaderParameter("uv_scale", .55f);
        _soil.SetShaderParameter("tint", new Color(1.35f, 1.22f, 1.1f));
        _glass = Material("res://shaders/chamber_glass.gdshader");
        _glass.SetShaderParameter("droplets", Load("res://art/textures/glass_droplets.png"));
        _beamMaterial = Material("res://shaders/chamber_beam.gdshader");
        _beamMaterial.SetShaderParameter("noise", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { Frequency = .035f, FractalOctaves = 3 }
        });

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(.012f, .016f, .024f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(.42f, .48f, .6f),
            }
        });
        Camera = new Camera3D { Name = "Camera", Fov = 50, Near = .05f, Far = 60 };
        AddChild(Camera);
        Camera.Position = new Vector3(0, 3.75f, 4.45f);
        Camera.LookAt(new Vector3(0, -.15f, -.05f), Vector3.Up);

        AddChild(new MeshInstance3D { Name = "Floor", Mesh = BuildFloor() });
        AddChild(new MeshInstance3D { Name = "Glass", Mesh = BuildDome(), MaterialOverride = _glass });
        AddChild(new MeshInstance3D
        {
            Name = "Struts", Mesh = BuildStruts(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(.085f, .1f, .13f)
            }
        });
        AddChild(new MeshInstance3D
        {
            Name = "BaseRing",
            Mesh = new TorusMesh { InnerRadius = DomeRadius - .16f, OuterRadius = DomeRadius + .1f, Rings = 96 },
            Scale = new Vector3(DomeWidth, .45f, 1),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(.07f, .075f, .085f)
            }
        });
        AddChild(new MeshInstance3D
        {
            Name = "Beam", Mesh = _beamMesh, MaterialOverride = _beamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    public Vector3 CellCenter(int slot, float y = 0) =>
        new(slot % GridSize - (GridSize - 1) * .5f, y, slot / GridSize - (GridSize - 1) * .5f);

    /// <summary>Screen position of a world point (viewport coordinates).</summary>
    public Vector2 Screen(Vector3 world) => Camera.UnprojectPosition(world);

    /// <summary>The four corners of a pit opening on the plate, back-left first, clockwise.</summary>
    public Vector2[] PitOutline(int slot, float grow = 0)
    {
        var c = CellCenter(slot);
        float h = .5f - PitInset + grow;
        return new[]
        {
            Screen(c + new Vector3(-h, 0, -h)), Screen(c + new Vector3(h, 0, -h)),
            Screen(c + new Vector3(h, 0, h)), Screen(c + new Vector3(-h, 0, h))
        };
    }

    /// <summary>Width in pixels of one cell at the soil of the given slot.</summary>
    public float CellPixels(int slot)
    {
        var c = CellCenter(slot, -PitDepth);
        return Screen(c + new Vector3(.5f, 0, 0)).DistanceTo(Screen(c - new Vector3(.5f, 0, 0)));
    }

    public int SlotAt(Vector2 screen)
    {
        Vector3 origin = Camera.ProjectRayOrigin(screen);
        Vector3 dir = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-5f) return -1;
        float t = -origin.Y / dir.Y;
        if (t <= 0) return -1;
        Vector3 hit = origin + dir * t;
        int col = Mathf.FloorToInt(hit.X + GridSize * .5f);
        int row = Mathf.FloorToInt(hit.Z + GridSize * .5f);
        return col >= 0 && col < GridSize && row >= 0 && row < GridSize ? row * GridSize + col : -1;
    }

    /// <summary>
    /// Place the beam: center of its footprint on the plate (world x, z), the angle of its long
    /// axis around the vertical, and the half extents of the footprint along / across that axis.
    /// </summary>
    public void SetBeam(Vector2 center, float angle, Vector2 half)
    {
        BeamCenter = center;
        var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        // The lamp hangs slightly behind the field, so the shaft leans a little toward the camera.
        var lamp = new Vector3(center.X * .45f, LampHeight, center.Y * .45f - 1.4f);
        var floorCenter = new Vector3(center.X, 0, center.Y);
        EntryPoint = DomeEntry(floorCenter, lamp);

        var segments = ShadowSegments(lamp, center);
        foreach (var m in new[] { _stone, _soil })
        {
            m.SetShaderParameter("beam_center", center);
            m.SetShaderParameter("beam_axis", axis);
            m.SetShaderParameter("beam_half", half);
            m.SetShaderParameter("lamp_position", lamp);
            m.SetShaderParameter("shadow_segments", segments);
            m.SetShaderParameter("shadow_count", segments.Length);
        }
        _glass.SetShaderParameter("entry_point", EntryPoint);
        BuildBeam(lamp, floorCenter, axis, half + new Vector2(.06f, .06f));
    }

    private static Vector3 DomeEntry(Vector3 floorPoint, Vector3 lamp)
    {
        // Solve in the space where the dome is a sphere; t is the same in both spaces.
        var squash = new Vector3(1 / DomeWidth, 1, 1);
        Vector3 p = floorPoint * squash, d = (lamp - floorPoint) * squash;
        float a = d.LengthSquared();
        float b = 2 * p.Dot(d);
        float c = p.LengthSquared() - DomeRadius * DomeRadius;
        float t = (-b + Mathf.Sqrt(Mathf.Max(0, b * b - 4 * a * c))) / (2 * a);
        return floorPoint + (lamp - floorPoint) * t;
    }

    private Vector4[] ShadowSegments(Vector3 lamp, Vector2 center)
    {
        Vector2 OnFloor(Vector3 p)
        {
            float s = lamp.Y / (lamp.Y - p.Y);
            var q = lamp + (p - lamp) * s;
            return new Vector2(q.X, q.Z);
        }
        return _struts
            .Where(s => s.A.Y > .01f || s.B.Y > .01f)
            .Select(s => (A: OnFloor(s.A), B: OnFloor(s.B)))
            .OrderBy(s => Geometry2D.GetClosestPointToSegment(center, s.A, s.B).DistanceTo(center))
            .Take(MaxShadowSegments)
            .Select(s => new Vector4(s.A.X, s.A.Y, s.B.X, s.B.Y))
            .ToArray();
    }

    private void BuildBeam(Vector3 lamp, Vector3 floorCenter, Vector2 axis, Vector2 half)
    {
        const int sides = 28;
        var across = new Vector2(-axis.Y, axis.X);
        float top = (lamp.Y - EntryPoint.Y) / lamp.Y;
        var bottomRing = new Vector3[sides + 1];
        var topRing = new Vector3[sides + 1];
        var normals = new Vector3[sides + 1];
        for (int i = 0; i <= sides; i++)
        {
            float a = Mathf.Tau * i / sides;
            // Superellipse cross-section, a little squarer than an ellipse, like the footprint.
            float cx = Mathf.Cos(a), sy = Mathf.Sin(a);
            float rx = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), 2f / 3f);
            float ry = Mathf.Sign(sy) * Mathf.Pow(Mathf.Abs(sy), 2f / 3f);
            var offset = axis * rx * half.X + across * ry * half.Y;
            bottomRing[i] = floorCenter + new Vector3(offset.X, .01f, offset.Y);
            topRing[i] = lamp + (bottomRing[i] - lamp) * top;
            normals[i] = new Vector3(offset.X, 0, offset.Y).Normalized();
        }
        _beamMesh.ClearSurfaces();
        _beamMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _beamMaterial);
        for (int i = 0; i < sides; i++)
        {
            float u0 = (float)i / sides, u1 = (float)(i + 1) / sides;
            void V(Vector3 p, Vector3 n, float u, float v)
            {
                _beamMesh.SurfaceSetNormal(n);
                _beamMesh.SurfaceSetUV(new Vector2(u, v));
                _beamMesh.SurfaceAddVertex(p);
            }
            V(topRing[i], normals[i], u0, 0); V(topRing[i + 1], normals[i + 1], u1, 0); V(bottomRing[i + 1], normals[i + 1], u1, 1);
            V(topRing[i], normals[i], u0, 0); V(bottomRing[i + 1], normals[i + 1], u1, 1); V(bottomRing[i], normals[i], u0, 1);
        }
        _beamMesh.SurfaceEnd();
    }

    private ArrayMesh BuildFloor()
    {
        int n = GridSize;
        float half = n * .5f;
        var xs = new List<float> { -DomeRadius, DomeRadius };
        for (int i = 0; i < n; i++)
        {
            xs.Add(i - half + PitInset);
            xs.Add(i + 1 - half - PitInset);
        }
        xs.Sort();
        var stone = new SurfaceTool();
        var soil = new SurfaceTool();
        stone.Begin(Mesh.PrimitiveType.Triangles);
        soil.Begin(Mesh.PrimitiveType.Triangles);

        bool InPit(float x, float z)
        {
            float fx = x + half, fz = z + half;
            if (fx <= 0 || fz <= 0 || fx >= n || fz >= n) return false;
            float lx = fx - Mathf.Floor(fx), lz = fz - Mathf.Floor(fz);
            return lx > PitInset && lx < 1 - PitInset && lz > PitInset && lz < 1 - PitInset;
        }

        for (int i = 0; i + 1 < xs.Count; i++)
        for (int j = 0; j + 1 < xs.Count; j++)
        {
            float x0 = xs[i], x1 = xs[i + 1], z0 = xs[j], z1 = xs[j + 1];
            if (x1 - x0 < 1e-4f || z1 - z0 < 1e-4f || InPit((x0 + x1) * .5f, (z0 + z1) * .5f)) continue;
            Quad(stone, new(x0, 0, z0), new(x1, 0, z0), new(x1, 0, z1), new(x0, 0, z1), Vector3.Up);
        }

        float d = -PitDepth;
        for (int slot = 0; slot < n * n; slot++)
        {
            var c = CellCenter(slot);
            float h = .5f - PitInset;
            float l = c.X - h, r = c.X + h, b = c.Z - h, f = c.Z + h;
            Quad(soil, new(l, d, b), new(r, d, b), new(r, d, f), new(l, d, f), Vector3.Up);
            Quad(stone, new(l, 0, b), new(l, 0, f), new(l, d, f), new(l, d, b), Vector3.Right);
            Quad(stone, new(r, 0, f), new(r, 0, b), new(r, d, b), new(r, d, f), Vector3.Left);
            Quad(stone, new(r, 0, b), new(l, 0, b), new(l, d, b), new(r, d, b), Vector3.Back);
            Quad(stone, new(l, 0, f), new(r, 0, f), new(r, d, f), new(l, d, f), Vector3.Forward);
        }

        var mesh = stone.Commit();
        soil.Commit(mesh);
        mesh.SurfaceSetMaterial(0, _stone);
        mesh.SurfaceSetMaterial(1, _soil);
        return mesh;
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        foreach (var v in new[] { a, b, c, a, c, d })
        {
            st.SetNormal(normal);
            st.SetUV(new Vector2(v.X, v.Z));
            st.AddVertex(v);
        }
    }

    private ArrayMesh BuildDome()
    {
        var rings = RingElevations.Select((elevation, k) => Enumerable.Range(0, PanelsPerRing + 1).Select(i =>
        {
            float theta = Mathf.Tau * i / PanelsPerRing + k * Mathf.Pi / PanelsPerRing;
            float phi = Mathf.DegToRad(elevation);
            return new Vector3(DomeWidth * DomeRadius * Mathf.Cos(phi) * Mathf.Cos(theta), DomeRadius * Mathf.Sin(phi),
                DomeRadius * Mathf.Cos(phi) * Mathf.Sin(theta));
        }).ToArray()).ToArray();
        var apex = new Vector3(0, DomeRadius, 0);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var edges = new HashSet<(Vector3I, Vector3I)>();
        void Panel(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 mid = (a + b + c) / 3;
            Vector3 normal = new Vector3(mid.X / (DomeWidth * DomeWidth), mid.Y, mid.Z).Normalized();
            Vector3 e1 = (b - a).Normalized();
            Vector3 e2 = normal.Cross(e1).Normalized();
            foreach (var v in new[] { a, b, c })
            {
                st.SetNormal(normal);
                st.SetUV(new Vector2((v - a).Dot(e1), (v - a).Dot(e2)) * .55f);
                st.AddVertex(v);
            }
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        void Edge(Vector3 a, Vector3 b)
        {
            Vector3I ka = (Vector3I)(a * 1000).Round(), kb = (Vector3I)(b * 1000).Round();
            var key = ka < kb ? (ka, kb) : (kb, ka);
            if (edges.Add(key)) _struts.Add((a, b));
        }

        for (int k = 0; k + 1 < rings.Length; k++)
        for (int i = 0; i < PanelsPerRing; i++)
        {
            Panel(rings[k][i], rings[k][i + 1], rings[k + 1][i]);
            Panel(rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]);
        }
        var last = rings[^1];
        for (int i = 0; i < PanelsPerRing; i++)
            Panel(last[i], last[i + 1], apex);
        return st.Commit();
    }

    private ArrayMesh BuildStruts()
    {
        const float width = .05f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (a, b) in _struts)
        {
            Vector3 along = (b - a).Normalized();
            Vector3 m = (a + b) * .5f;
            Vector3 outward = new Vector3(m.X / (DomeWidth * DomeWidth), m.Y, m.Z).Normalized();
            Vector3 side = along.Cross(outward).Normalized() * width;
            Vector3 lift = outward * width;
            var corners = new[] { side + lift, -side + lift, -side - lift, side - lift };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = corners[i], q = corners[(i + 1) % 4];
                Quad(st, a + p, b + p, b + q, a + q, (p + q).Normalized());
            }
        }
        return st.Commit();
    }

    private static ShaderMaterial Material(string shader) => new() { Shader = GD.Load<Shader>(shader) };

    private static Texture2D? Load(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
}
