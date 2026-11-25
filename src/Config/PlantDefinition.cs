using Godot;
using OneBeam.Domain;

namespace OneBeam.Config;

[GlobalClass]
public partial class PlantDefinition : Resource
{
    [Export] public string SpeciesId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.Range, "1,120,1")] public int StepsPerStage { get; set; } = 6;
    [Export(PropertyHint.Range, "1,240,1")] public int DarkLimitSteps { get; set; } = 56;
    [Export(PropertyHint.Range, "1,10,1")] public int HarvestYield { get; set; } = 2;
    [Export(PropertyHint.Range, "0,600,1")] public float ShadeRadius { get; set; }
    [Export(PropertyHint.Range, "0,120,1")] public int FruitPeriodSteps { get; set; }
    [Export(PropertyHint.Range, "0,600,1")] public float MaxHeight { get; set; }
    [Export(PropertyHint.Range, "0,60,0.5")] public float StretchPerStep { get; set; }

    public SpeciesRules ToRules() => new(SpeciesId, StepsPerStage, DarkLimitSteps,
        HarvestYield, ShadeRadius, FruitPeriodSteps, MaxHeight, StretchPerStep);
}
