using System;
using System.Collections.Generic;

namespace OneBeam.Levels.Domain;

public enum GrowthStage { Seed, Seedling, Normal, Mother }
public enum LevelRunStatus { Playing, Won, Lost }

public sealed record LevelDefinition(int Number, double DurationSeconds, int AddedSeeds,
    int LightRange, double TargetVitality)
{
    internal bool IsValidFor(int slots) => Number > 0 && double.IsFinite(DurationSeconds) &&
        DurationSeconds > 0 && AddedSeeds >= 0 && LightRange > 0 && LightRange <= slots &&
        double.IsFinite(TargetVitality) && TargetVitality >= 0;
}

public sealed record StageRule(double? GrowthToNext, double DarkPerSecond, double WiltAt,
    double DieAt, double Vitality);

public sealed record GrowthRules(IReadOnlyList<StageRule> Stages, double LightGrowthPerSecond,
    double LightDarkPerSecond, double MoveSecondsPerSlot, double FirstSeedGrowth,
    double SeedGrowthIncrement, int SeedsPerHarvest, double RecoverDark, double WiltMultiplier,
    double StageDarkRelief = 20)
{
    internal void Validate()
    {
        static bool Positive(double value) => double.IsFinite(value) && value > 0;
        if (Stages == null || Stages.Count != 4 || !Positive(LightGrowthPerSecond) ||
            !Positive(-LightDarkPerSecond) || !Positive(MoveSecondsPerSlot) ||
            !Positive(FirstSeedGrowth) || !double.IsFinite(SeedGrowthIncrement) ||
            SeedGrowthIncrement < 0 || SeedsPerHarvest <= 0 || !double.IsFinite(RecoverDark) ||
            RecoverDark < 0 || !double.IsFinite(WiltMultiplier) || WiltMultiplier is < 0 or > 1 ||
            !double.IsFinite(StageDarkRelief) || StageDarkRelief < 0)
            throw new ArgumentException("Invalid growth rules.");
        for (int i = 0; i < Stages.Count; i++)
        {
            var s = Stages[i];
            if (s == null || !Positive(s.DarkPerSecond) || !Positive(s.WiltAt) ||
                RecoverDark > s.WiltAt || !Positive(s.DieAt) || s.DieAt <= s.WiltAt ||
                !double.IsFinite(s.Vitality) || s.Vitality < 0 ||
                (i == (int)GrowthStage.Mother ? s.GrowthToNext.HasValue :
                    !s.GrowthToNext.HasValue || !Positive(s.GrowthToNext.Value)))
                throw new ArgumentException("Invalid plant stage.", nameof(Stages));
        }
    }
}

public sealed record LevelPlant(GrowthStage Stage = GrowthStage.Seed, double Growth = 0,
    double Darkness = 0, bool Wilted = false, int SeedsProduced = 0);
