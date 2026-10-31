namespace EcoSimulator.Core;
public sealed partial class World
{
    private IEnumerable<ScheduledChange> ActiveChanges => scenario.Schedule.Where(c => c.Turn <= Turn).OrderBy(c => c.Turn);
    public string ParameterVersion => ActiveChanges.LastOrDefault()?.Version ?? scenario.ConfigurationVersion;
    private WorldParameters EffectiveWorld => scenario.World with { SatietyDecay = ActiveChanges.LastOrDefault(c => c.SatietyDecay is not null)?.SatietyDecay ?? scenario.World.SatietyDecay };
    private bool BlockNewTasks => ActiveChanges.LastOrDefault(c => c.BlockNewTasks is not null)?.BlockNewTasks ?? false;
    private IEnumerable<Recipe> RecipeVersions => scenario.Recipes.Concat(scenario.Schedule.Where(c => c.Output is not null).SelectMany(c => scenario.Recipes.Select(r => r with { Version=c.Version, Output=c.Output!.Value })));
    private Recipe EffectiveRecipe(string company)
    {
        var original=scenario.Recipes.Single(r => r.Company==company);
        var change=ActiveChanges.LastOrDefault(c => c.Output is not null);
        return change is null ? original : original with { Version=change.Version, Output=change.Output!.Value };
    }
}
public static class Variants
{
    public static Scenario Create(string name)
    {
        var s=Baseline.Create();
        return name switch {
            "baseline" => s,
            "space" => SpatialScenarios.Create(),
            "environment" => SpatialScenarios.Create(true),
            "production" => ProductionScenario.Create(),
            "organizations" => OrganizationScenario.Create(),
            "no-resident-cash" => s with { Name=name, ConfigurationVersion=name, Cash=s.Cash.Select(c => c.Entity.StartsWith("P") ? c with { Amount=0 } : c).ToArray(), Issuance=200000 },
            "low-capital" => s with { Name=name, ConfigurationVersion=name, Cash=s.Cash.Select(c => c.Entity=="F" ? c with { Amount=50 } : c).ToArray(), Shares=[new("P00","F",50)], Issuance=200050 },
            "productivity" => s with { Name=name, Schedule=[new(240,"productivity-1",1,null,null)] },
            "demand" => s with { Name=name, Schedule=[new(240,"demand-4",null,4,null)] },
            "pause" => s with { Name=name, Schedule=[new(240,"pause-on",null,null,true),new(264,"pause-off",null,null,false)] },
            _ => throw new RuleException("未知变体")
        };
    }
}
