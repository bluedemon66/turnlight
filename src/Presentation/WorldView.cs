using System;
using System.Collections.Generic;
using Godot;
using OneBeam.Domain;

namespace OneBeam.Presentation;

public enum WorldInteraction { Inspect, Plant, Move, Aim }

public partial class WorldView : Node2D
{
    [Export] public PackedScene PlantScene { get; set; } = null!;
    public event Action<Vector2>? GroundPressed;
    public event Action<Vector2>? LampDragged;
    public event Action<int>? PlantPressed;

    private readonly Dictionary<int, PlantView> _plants = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _removed = new();
    private Node2D _plantLayer = null!;
    private Polygon2D _lamp = null!;
    private WorldState? _state;
    private WorldInteraction _mode;
    private float _scale = 1;
    private Vector2 _field;
    private bool _dragging;
    private Vector2 _pressPosition;
    private bool _lampMoved;
    private float _animation;
    private bool _breaching;

    public override void _Ready()
    {
        _lamp = GetNode<Polygon2D>("Lamp");
        _plantLayer = GetNode<Node2D>("Plants");
        SetProcess(false);
    }

    public void Render(EcologySimulation simulation, int? selectedId, WorldInteraction mode)
    {
        WorldState state = simulation.State;
        _state = state;
        _mode = mode;
        _scale = Mathf.Min(600f / state.Size.X, 390f / state.Size.Y);
        _field = new Vector2(state.Size.X, state.Size.Y) * _scale;
        _plantLayer.Scale = Vector2.One * _scale;
        _lamp.Position = new Vector2(state.LampPosition.X, state.LampPosition.Y) * _scale;
        _lamp.Color = state.Phase == GamePhase.MovableLamp ? new Color("ffde8a") : new Color("d5b981");
        _seen.Clear();
        foreach (var plant in state.Plants)
        {
            var id = plant.Id;
            _seen.Add(id);
            if (!_plants.TryGetValue(id, out var view))
            {
                view = PlantScene.Instantiate<PlantView>();
                _plantLayer.AddChild(view);
                _plants.Add(id, view);
            }
            view.Position = new Vector2(plant.Position.X, plant.Position.Y);
            view.Render(plant, simulation.IsIlluminated(plant.Position), id == selectedId);
        }
        _removed.Clear();
        foreach (var pair in _plants)
            if (!_seen.Contains(pair.Key)) _removed.Add(pair.Key);
        foreach (int id in _removed)
        {
            var view = _plants[id];
            _plantLayer.RemoveChild(view);
            view.QueueFree();
            _plants.Remove(id);
        }
        QueueRedraw();
    }

    public void PlayDeparture() => StartTransition(false);
    public void PlayBreach() => StartTransition(true);

    private void StartTransition(bool breach)
    {
        _breaching = breach;
        _animation = 1;
        SetProcess(true);
        QueueRedraw();
    }

    public void ResetInteraction()
    {
        _dragging = false;
        _animation = 0;
        SetProcess(false);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _animation = Mathf.Max(0, _animation - (float)delta / 1.2f);
        QueueRedraw();
        if (_animation == 0) SetProcess(false);
    }

    public override void _Draw()
    {
        if (_state is not { } state) return;
        bool open = state.Phase == GamePhase.OpenLab;
        bool night = open && state.Sky == SkyPhase.Night;
        bool fog = open && state.Sky == SkyPhase.Fog;
        var bounds = new Rect2(Vector2.Zero, _field);
        var fill = !open ? new Color("183934") : night ? new Color("192d3d") :
            fog ? new Color("384d46") : new Color("305146");
        DrawRect(bounds.Grow(5),new Color("142928"));
        DrawRect(bounds, fill);
        DrawSubstrate(bounds, open);
        DrawGrid(bounds, open, state);
        if (fog)
        {
            var fogStart = _field.X / 3f;
            DrawRect(new Rect2(fogStart, 0, _field.X - fogStart, _field.Y), new Color("bad6d028"));
            for (int band = 0; band < 5; ++band)
            {
                float y = 37 + 65 * band;
                DrawLine(new(fogStart+15, y+12),new(_field.X-5, y),new Color("cfdfd613"),5,true);
            }
        }
        DrawLamp(bounds, state.LampRadius * _scale);
        Color rim = new("7a9c8b");
        DrawRect(bounds, rim, false, 1.5f);
        for (int x = 0; x <= _field.X; x+=20)
            DrawLine(new(x,_field.Y-1),new(x,_field.Y-(x%100==0 ? 7 : 3)), rim,1);
        for (int y = 0; y <= _field.Y; y+=20)
            DrawLine(new(0,y),new(y%100==0 ? 7 : 3,y), rim,1);
        float topW = state.DishSize.X * _scale;
        if (open)
        {
            var oldDish = new Rect2(0,0,topW,state.DishSize.Y*_scale);
            DrawLine(new(topW, 0),oldDish.End,new Color("bdd4b681"),1);
            DrawLine(new(0,oldDish.End.Y),oldDish.End,new Color("bdd4b681"),1);
            DrawLine(new(0,3),new(topW*0.36f,3),new Color("c5ddd3"),2,true);
            DrawLine(new(topW*0.74f,3),new(topW,3),new Color("c5ddd3"),2,true);
        }
        else
        {
            DrawLine(new(4,3),new(topW-4,3),new Color("b1c8bc"),2,true);
            DrawLine(new(7,6),new(topW-7,6),new Color("d1ebdf38"),1,true);
        }
        if (state.Phase != GamePhase.MovableLamp)
            DrawArc(_lamp.Position,13,0,Mathf.Tau,4,new Color("ffe1a8"),1,true);
        if (_animation > 0) DrawTransition(topW);
    }

    private void DrawSubstrate(Rect2 field, bool open)
    {
        // Fine sediment and air bubbles stay in fixed locations as the light moves.
        var sediment = open ? new Color("d2e7b51c") : new Color("acccc31c");
        var rimHighlight = new Color("bad7c855");
        float width = field.Size.X;
        float height = field.Size.Y;
        for (int band = 0; band < 4; band++)
        {
            var contour = new Vector2[28];
            for (int i = 0; i < contour.Length; i++)
            {
                float t = (float)i / (contour.Length - 1);
                float recess = 3 + (1 - t) * (1 - t) * (18 + band * 8);
                float ripple = Mathf.Sin(t * 14 + band * .7f) * 1.7f;
                contour[i] = new(t * width * .74f, height - recess - band * 2 + ripple);
            }
            DrawPolyline(contour, sediment, 1, true);
        }
        for (int i = 0; i < 31; i++)
        {
            float x = 18 + ((i * 97 + 32) % 541) / 570f * width;
            float y = 16 + ((i * 71 + i * i * 11) % 342) / 375f * height;
            float radius = i % 6 == 0 ? 3 : 1.3f;
            DrawArc(new(x, y), radius, -.6f, 3.3f, 12, sediment, 1, true);
        }
        foreach (var corner in new[] {
            new Vector2(9, 11), new Vector2(width - 9, 11),
            new Vector2(9, height - 11), new Vector2(width - 9, height - 11) })
        {
            DrawArc(corner, 3, 0, Mathf.Tau, 12, rimHighlight, 1, true);
            DrawLine(corner - Vector2.One, corner + Vector2.One, rimHighlight, 1, true);
        }
    }

    private void DrawGrid(Rect2 bounds, bool open, WorldState state)
    {
        var grid = new Color("bbd2bb14");
        for (float x=20*_scale; x<bounds.Size.X; x+=40*_scale)
            for (float y=20*_scale; y<bounds.Size.Y; y+=40*_scale)
                DrawCircle(new(x,y),1,grid);
        if (!open) return;
        int cols = state.Environment.Columns, rows = state.Environment.Rows;
        var cell = bounds.Size / new Vector2(cols, rows);
        for (int i=1; i<cols; i++)
            DrawDashedLine(new(cell.X*i,0), new(cell.X*i,bounds.Size.Y),new Color("c5d8b349"),1,5);
        for (int j=1; j<rows; j++)
            DrawDashedLine(new(0,cell.Y*j), new(bounds.Size.X,cell.Y*j),new Color("c5d8b349"),1,5);
        foreach (var plant in state.Plants)
        {
            if (plant.Stage != PlantStage.Mature) continue;
            var col = Mathf.Clamp(Mathf.FloorToInt(plant.Position.X*_scale/cell.X),0,cols-1);
            var row = Mathf.Clamp(Mathf.FloorToInt(plant.Position.Y*_scale/cell.Y),0,rows-1);
            Vector2 corner = new(col*cell.X+5,row*cell.Y+9);
            DrawLine(corner,corner+new Vector2(0,-4),new Color("a3d698"),2);
            DrawLine(corner+new Vector2(0,-4),corner+new Vector2(10,-4),new Color("a3d698"),2);
        }
    }

    private void DrawLamp(Rect2 bounds, float radius)
    {
        var disk = new Vector2[96];
        for (int i=0;i<disk.Length;i++)
            disk[i]=_lamp.Position + Vector2.Right.Rotated(Mathf.Tau*i/disk.Length)*radius;
        var box = new[] { Vector2.Zero,new Vector2(_field.X,0),_field,new Vector2(0,_field.Y) };
        foreach (var shape in Geometry2D.IntersectPolygons(disk,box))
            if (shape.Length >= 3) DrawColoredPolygon(shape,new Color("ffe3a124"));
        for (int ring = 1; ring <= 3; ring++)
        {
            var halo = new Vector2[disk.Length];
            float fraction = ring * .22f;
            for (int i = 0; i < halo.Length; i++)
                halo[i] = _lamp.Position.Lerp(disk[i], fraction);
            foreach (var shape in Geometry2D.IntersectPolygons(halo, box))
                if (shape.Length >= 3) DrawColoredPolygon(shape, new Color("ffe4a107"));
        }
        for (int i=0;i<disk.Length;i++)
        {
            Vector2 a=disk[i], b=disk[(i+1)%disk.Length];
            if (bounds.HasPoint(a) && bounds.HasPoint(b)) DrawLine(a,b,new Color("efcd8ac9"),1.1f,true);
        }
    }

    private void DrawTransition(float topW)
    {
        float t = 1-_animation;
        if (_breaching)
        {
            for (int i=0; i<10; i++)
            {
                Vector2 a = new(topW*(0.38f + i*0.038f)+(i-5)*t*5,5 + t*t*48);
                DrawLine(a,a+new Vector2((i%3-1)*5,4+i%4),new Color(0.92f,0.95f,0.83f,_animation),2,true);
            }
        }
        else
        {
            // A lab sleeve withdraws. The fixed mount remains around the lamp.
            Vector2 tip = _lamp.Position.Lerp(new Vector2(_lamp.Position.X,-22),t);
            if (tip.Y>0)
            {
                DrawLine(new(tip.X,0),tip,new Color(0.68f,0.78f,0.75f,_animation*0.65f),9,true);
                DrawLine(tip,tip+new Vector2(-9,5),new Color(0.76f,0.84f,0.81f,_animation),3,true);
                DrawLine(tip,tip+new Vector2(9,5),new Color(0.76f,0.84f,0.81f,_animation),3,true);
            }
        }
    }

    private Vector2 LogicalPoint(Vector2 screen) =>
        (GetGlobalTransformWithCanvas().AffineInverse()*screen)/_scale;

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state is null || e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;
        Vector2 local = GetGlobalTransformWithCanvas().AffineInverse()*click.Position;
        if (!new Rect2(Vector2.Zero,_field).HasPoint(local)) return;
        GetViewport().SetInputAsHandled();
        if (_mode != WorldInteraction.Inspect) { GroundPressed?.Invoke(local/_scale); return; }
        if (local.DistanceTo(_lamp.Position) <= 12f)
        {
            _dragging = true;
            _lampMoved = false;
            _pressPosition = local;
            return;
        }
        SelectNearestPlant(local);
    }

    private void SelectNearestPlant(Vector2 local)
    {
        int? nearest = null;
        float nearestDistance = 18f;
        foreach (var (id, view) in _plants)
        {
            var distance=local.DistanceTo(view.Position*_scale);
            if (distance > nearestDistance) continue;
            nearest=id;
            nearestDistance=distance;
        }
        if (nearest.HasValue) PlantPressed?.Invoke(nearest.Value);
    }

    public override void _Input(InputEvent e)
    {
        if (!_dragging || _state is null) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _dragging = false;
            if (!_lampMoved) SelectNearestPlant(_pressPosition);
        }
        else if (e is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0) { _dragging=false; return; }
            var target = LogicalPoint(motion.Position);
            if (!_lampMoved && _pressPosition.DistanceTo(target * _scale) < 3f) return;
            _lampMoved = true;
            LampDragged?.Invoke(target.Clamp(Vector2.Zero, _field / _scale));
        }
    }
}
