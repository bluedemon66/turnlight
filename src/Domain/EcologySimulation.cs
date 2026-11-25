using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace OneBeam.Domain;

public sealed class EcologySimulation
{
    public const float MinimumSpacing = 30;
    public WorldState State { get; }
    public IReadOnlyDictionary<string, SpeciesRules> Rules { get; }

    public EcologySimulation(WorldState state, IEnumerable<SpeciesRules>? rules = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        var byId = (rules ?? SpeciesRules.Defaults).ToDictionary(r => r.SpeciesId);
        if (byId.Count != 3 || byId.Values.Any(r => !r.IsValid))
            throw new ArgumentException("Expected valid sunflower, light mother and bamboo definitions.", nameof(rules));
        Rules = new ReadOnlyDictionary<string, SpeciesRules>(byId);
    }

    public bool TryPlantSeed(string speciesId, Vector2 position)
    {
        if (State.Status != RunStatus.Playing || State.CountSeeds(speciesId) <= 0 ||
            State.NextPlantId == int.MaxValue || !CanOccupy(position)) return false;
        State.Sow(speciesId, position);
        return true;
    }

    private bool CanOccupy(Vector2 position, int movingId = 0) =>
        State.Contains(position) && State.Plants.All(p => p.Id == movingId ||
            Vector2.DistanceSquared(p.Position, position) >= MinimumSpacing * MinimumSpacing);

    public bool TryMoveLamp(Vector2 position)
    {
        if (State.Status != RunStatus.Playing || State.Phase != GamePhase.MovableLamp ||
            !State.Contains(position)) return false;
        State.LampPosition = position;
        return true;
    }

    public bool TryMovePlant(int plantId, Vector2 position)
    {
        int index = State.IndexOf(plantId);
        if (State.Status != RunStatus.Playing || index < 0 || !CanOccupy(position, plantId)) return false;
        State.UpdateAt(index, State.Plants[index] with { Position = position });
        ResolveOutcome();
        return true;
    }

    public bool TryAimPlant(int plantId, Vector2 target)
    {
        int index = State.IndexOf(plantId);
        if (State.Status != RunStatus.Playing || index < 0 || !State.Contains(target)) return false;
        var plant = State.Plants[index];
        Vector2 displacement = target - plant.Position;
        if (plant.SpeciesId != SpeciesRules.SkyBambooId || displacement.LengthSquared() <= .0001f)
            return false;
        State.UpdateAt(index, plant with { Direction = Vector2.Normalize(displacement) });
        ResolveOutcome();
        return true;
    }

    public bool TryHarvest(int plantId)
    {
        int index = State.IndexOf(plantId);
        if (State.Status != RunStatus.Playing || index < 0) return false;
        PlantState plant = State.Plants[index];
        int yield = Rules[plant.SpeciesId].HarvestYield;
        if (!plant.Harvestable || State.CountSeeds(plant.SpeciesId) > int.MaxValue - yield) return false;

        State.RemoveAt(index);
        State.AddSeeds(plant.SpeciesId, yield);
        if (!State.GardenReady && plant.SpeciesId == SpeciesRules.SunFlowerId)
        {
            State.GardenReady = true;
            State.Introduce(SpeciesRules.LightMotherId, 2);
        }
        ResolveOutcome();
        return true;
    }

    public bool TryGiveFruit(int plantId)
    {
        int index = State.IndexOf(plantId);
        if (State.Status != RunStatus.Playing || index < 0 || State.FruitCount == 0 ||
            State.Plants[index].BoostSteps > 0) return false;
        State.FruitCount--;
        State.UpdateAt(index, State.Plants[index] with { BoostSteps = State.Environment.BoostDuration });
        return true;
    }

    public bool IsIlluminated(Vector2 position)
    {
        if (!State.Contains(position)) return false;
        if (Vector2.DistanceSquared(position, State.LampPosition) <= State.LampRadius * State.LampRadius)
            return true;
        return State.Phase == GamePhase.OpenLab && (State.Sky == SkyPhase.Day ||
            (State.Sky == SkyPhase.Fog && position.X <= State.Size.X / 3f));
    }

    public bool HasShadeSupport(PlantState target) =>
        State.Plants.Any(source => source.Stage == PlantStage.Mature && source.StoredLight > 0 &&
            WithinShadeRadius(source, target.Position));

    private bool WithinShadeRadius(PlantState source, Vector2 position)
    {
        float radius = Rules[source.SpeciesId].ShadeRadius;
        return radius > 0 && Vector2.DistanceSquared(source.Position, position) <= radius * radius;
    }

    public void AdvanceTime()
    {
        if (State.Status != RunStatus.Playing) return;
        var nextPlants = new List<PlantState>(State.Plants.Count);
        int fruit = State.FruitCount;
        foreach (var plant in State.Plants)
        {
            SpeciesRules rules = Rules[plant.SpeciesId];
            bool illuminated = IsIlluminated(plant.Position);
            float darkness = Math.Clamp(plant.Darkness + (illuminated ? -2f :
                HasShadeSupport(plant) ? .2f : 1f) / rules.DarkLimitSteps, 0, 1);
            if (darkness >= 1 - .000001f) continue;
            bool boosted = plant.BoostSteps > 0;
            float light = (illuminated ? 1 : 0) + (boosted ? 1 : 0);
            float growth = Math.Min(1f, plant.Growth + light / rules.StepsPerStage);
            if (growth >= 1f - .000001f) growth = 1f;
            PlantStage stage = plant.Stage;
            if (stage != PlantStage.Mature && growth >= 1)
            {
                stage = stage == PlantStage.Seed ? PlantStage.Seedling : PlantStage.Mature;
                growth = 0;
            }

            float stored = plant.StoredLight;
            float height = plant.Height;
            int fruitProgress = plant.FruitProgress;
            if (plant.Stage == PlantStage.Mature)
            {
                if (rules.ShadeRadius > 0)
                    stored = Math.Clamp(stored + (illuminated ? .1f : -.025f), 0, 1);
                if (rules.FruitPeriodSteps > 0 && illuminated && ++fruitProgress >= rules.FruitPeriodSteps)
                {
                    fruit = fruit == int.MaxValue ? fruit : fruit + 1;
                    fruitProgress = 0;
                }
                height = Math.Min(rules.MaxHeight, height + rules.StretchPerStep * light);
            }
            else if (stage == PlantStage.Mature && illuminated && rules.ShadeRadius > 0)
                stored = .1f;
            nextPlants.Add(plant with { Stage = stage, Growth = growth, Darkness = darkness,
                BoostSteps = Math.Max(0, plant.BoostSteps - 1), Height = height,
                FruitProgress = fruitProgress, StoredLight = stored });
        }
        State.ReplacePlants(nextPlants);
        State.FruitCount = fruit;
        State.Step++;
        ResolveOutcome();
    }

    private void ResolveOutcome()
    {
        foreach (string speciesId in State.IntroducedSpecies)
        {
            if (State.CountSeeds(speciesId) != 0 || State.Plants.Any(p => p.SpeciesId == speciesId)) continue;
            State.FailureSpecies = speciesId;
            State.Status = RunStatus.Failed;
            return;
        }

        switch (State.Phase)
        {
            case GamePhase.MovableLamp when State.Plants.Any(p =>
                p.SpeciesId == SpeciesRules.LightMotherId && p.Stage == PlantStage.Mature):
                State.Phase = GamePhase.FixedLamp;
                State.Introduce(SpeciesRules.SkyBambooId, 2);
                if (State.FruitCount < int.MaxValue) State.FruitCount++;
                break;
            case GamePhase.FixedLamp when State.Plants.Any(TouchesRoof):
                State.Phase = GamePhase.OpenLab;
                State.OpenedAtStep = State.Step;
                break;
            case GamePhase.OpenLab when State.OpenSteps >= State.Environment.CycleLength &&
                State.CoveredZones >= State.RequiredZones:
                State.Status = RunStatus.Won;
                break;
        }
    }

    private bool TouchesRoof(PlantState plant)
    {
        if (plant.SpeciesId != SpeciesRules.SkyBambooId || plant.Stage != PlantStage.Mature ||
            plant.Direction.Y >= 0 || plant.Height <= 0) return false;
        float distance = -plant.Position.Y / plant.Direction.Y;
        float x = plant.Position.X + plant.Direction.X * distance;
        return distance <= plant.Height && x >= 0 && x <= State.DishSize.X;
    }
}
