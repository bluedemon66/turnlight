using System.Numerics;

namespace OneBeam.Domain;

public enum PlantStage { Seed, Seedling, Mature, Dead }
public enum GamePhase { MovableLamp, FixedLamp, OpenLab }
public enum RunStatus { Playing, Failed, Won }
public enum SkyPhase { Day, Fog, Night }

public sealed record PlantState(int Id, string SpeciesId, Vector2 Position)
{
    public PlantStage Stage { get; init; } = PlantStage.Seed;
    public float Growth { get; init; }
    public float Darkness { get; init; }
    public int BoostSteps { get; init; }
    public Vector2 Direction { get; init; } = -Vector2.UnitY;
    public float Height { get; init; }
    public float StoredLight { get; init; }
    public int FruitProgress { get; init; }
    public bool Harvestable => Stage == PlantStage.Mature && Growth >= 1;
}
