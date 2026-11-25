namespace OneBeam.Domain;

public sealed record SpeciesRules(
    string SpeciesId,
    int StepsPerStage,
    int DarkLimitSteps,
    int HarvestYield,
    float ShadeRadius,
    int FruitPeriodSteps,
    float MaxHeight,
    float StretchPerStep)
{
    public const string SunFlowerId = "sun_flower";
    public const string LightMotherId = "light_mother";
    public const string SkyBambooId = "sky_bamboo";

    public static SpeciesRules[] Defaults => new[]
    {
        new SpeciesRules(SunFlowerId, 6, 56, 2, 132, 0, 0, 0),
        new SpeciesRules(LightMotherId, 8, 62, 2, 0, 8, 0, 0),
        new SpeciesRules(SkyBambooId, 8, 58, 2, 0, 0, 260, 9)
    };

    public static bool IsKnown(string? id) => id is SunFlowerId or LightMotherId or SkyBambooId;

    internal bool IsValid => IsKnown(SpeciesId) && StepsPerStage > 0 && DarkLimitSteps > 0 &&
        HarvestYield > 0 && float.IsFinite(ShadeRadius) && ShadeRadius >= 0 &&
        FruitPeriodSteps >= 0 && float.IsFinite(MaxHeight) && MaxHeight >= 0 &&
        float.IsFinite(StretchPerStep) && StretchPerStep >= 0 &&
        (SpeciesId != LightMotherId || FruitPeriodSteps > 0) &&
        (SpeciesId != SkyBambooId || (MaxHeight > 0 && StretchPerStep > 0));
}

public sealed record EnvironmentRules
{
    public int DaySteps { get; init; } = 30;
    public int FogSteps { get; init; } = 12;
    public int NightSteps { get; init; } = 16;
    public int BoostDuration { get; init; } = 12;
    public int Columns { get; init; } = 4;
    public int Rows { get; init; } = 2;
    public int RequiredZones { get; init; } = 6;
    public float SizeMultiplier { get; init; } = 1.5f;

    public int CycleLength => DaySteps + FogSteps + NightSteps;
    internal bool IsValid => DaySteps > 0 && FogSteps > 0 && NightSteps > 0 &&
        (long)DaySteps + FogSteps + NightSteps < int.MaxValue && BoostDuration > 0 &&
        Columns > 0 && Rows > 0 && (long)Columns * Rows <= int.MaxValue &&
        RequiredZones > 0 && RequiredZones <= Columns * Rows &&
        float.IsFinite(SizeMultiplier) && SizeMultiplier > 1;
}
