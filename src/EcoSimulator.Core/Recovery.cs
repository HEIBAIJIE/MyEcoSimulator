namespace EcoSimulator.Core;

public sealed record BalanceValue(string Entity, Account Account, long Value);
public sealed record TradeValue(string Region, string Tag, long Price, long Quantity);
public sealed record RecoveryState(string Schema, long Turn, Entity[] Entities, Lot[] Lots,
    BalanceValue[] Balances, Market[] Markets, WorkTask[] Tasks, Shareholding[] Shares,
    PersonStatus[] Statuses, TradeValue[] Trades, Dictionary<string,PersonalMemory> People,
    Dictionary<string,CompanyMemory> Companies)
{
    public EnvironmentConfiguration? Environment { get; init; }
    public TransportProgress[] Transport { get; init; } = [];
    public ActionProgress[] Production { get; init; } = [];
    public ProductionMemory? Procurement { get; init; }
}

public sealed partial class World
{
    // The persistent runtime calls this only at a committed boundary. Full facts live in storage.
    public RecoveryState Capture()
    {
        lock(gate) return new(scenario.EnabledMechanisms.Contains("environment-1") ? "world-environment-1" : scenario.EnabledMechanisms.Contains("space-1") ? "world-space-1" : scenario.EnabledMechanisms.Contains("organizations-1") ? "world-organizations-1" : scenario.EnabledMechanisms.Contains("production-1") ? "world-production-1" : "world-1",Turn,People.Values.ToArray(),lots.ToArray(),
            balances.Select(b=>new BalanceValue(b.Key.Item1,b.Key.Item2,b.Value)).ToArray(),
            (currentMarkets??scenario.Markets).ToArray(),
            publishedTasks.Where(t=>t.EffectiveTurn>=Turn || productionProgress.Values.Any(p => p.Task==t.Id && p.State=="running")).ToArray(),Shares.ToArray(),statuses.ToArray(),
            trades.GroupBy(t=>(t.Region,t.Tag)).SelectMany(g=>g.TakeLast(checked((int)scenario.Strategy.Window)))
                .Select(t=>new TradeValue(t.Region,t.Tag,t.Price,t.Quantity)).ToArray(),
            new(personalMemories),new(companyMemories)) { Environment=currentEnvironment??scenario.Environment, Transport=transport.Values.ToArray(), Production=productionProgress.Values.ToArray(), Procurement=procurementMemory };
    }
    public static World Restore(Scenario scenario, RecoveryState state)
    {
        ScenarioLoader.Validate(scenario);
        ScenarioLoader.Require(state.Schema==(scenario.EnabledMechanisms.Contains("environment-1") ? "world-environment-1" : scenario.EnabledMechanisms.Contains("space-1") ? "world-space-1" : scenario.EnabledMechanisms.Contains("organizations-1") ? "world-organizations-1" : scenario.EnabledMechanisms.Contains("production-1") ? "world-production-1" : "world-1") && state.Turn>=0,"不兼容恢复模式");
        var w=new World(scenario) { Turn=state.Turn, currentEntities=state.Entities.ToDictionary(e=>e.Id),
            lots=state.Lots.ToArray(), balances=state.Balances.ToDictionary(b=>(b.Entity,b.Account),b=>b.Value),
            currentMarkets=state.Markets.ToArray(),currentShares=state.Shares.ToArray() };
        w.currentEnvironment=state.Environment;
        if(scenario.Environment is not null) EnvironmentRules.Validate(scenario with { Environment=state.Environment,Entities=state.Entities,Lots=state.Lots,Tasks=[] });
        foreach(var p in state.Transport)
        {
            ScenarioLoader.Require(p.Completed>0 && p.Completed<=p.Required && p.Required==(p.Kind==ActionKind.Travel?24:scenario.Space!.HaulTurns) && new[]{"running","completed","interrupted"}.Contains(p.State),"Invalid transport recovery");
            if(p.State=="running") ScenarioLoader.Require(p.Completed<p.Required && w.People[p.Person].Health>0 && w.People[p.Person].Location==p.Origin && w.TransportValid(p.Person,new MainAction(p.Kind) { Target=p.Target,Lot=p.Lot },p.Origin),"Invalid active transport");
            w.transport.Add(p.Person,p);
        }
        foreach(var p in state.Production) w.productionProgress.Add(p.Person,p);
        foreach(var p in state.Production)
        {
            var recipe=scenario.Recipes.SingleOrDefault(r=>r.Id==p.Recipe && r.Version==p.Version);
            ScenarioLoader.Require(recipe is not null && p.Completed>0 && p.Completed<=recipe.Hours && new[]{"running","completed","interrupted"}.Contains(p.State) && (p.State!="running" || p.Completed<recipe.Hours),"非法恢复动作进度");
        }
        w.procurementMemory=state.Procurement;
        w.publishedTasks.AddRange(state.Tasks); w.statuses.AddRange(state.Statuses);
        w.trades.AddRange(state.Trades.Select(t=>(t.Region,t.Tag,t.Price,t.Quantity)));
        foreach(var p in state.People) w.personalMemories.Add(p.Key,p.Value);
        foreach(var c in state.Companies) w.companyMemories.Add(c.Key,c.Value);
        Inventory.Validate(w.lots,w.People,scenario.ItemTypes.ToDictionary(t=>t.Id),t=>scenario.Regions.Single(r=>r.Id==t.Region).Validate(t));
        ScenarioLoader.Require(w.People.Keys.Order().SequenceEqual(scenario.Entities.Select(e=>e.Id).Order()),"恢复主体不符");
        foreach(var e in w.People.Keys) ScenarioLoader.Require(w.Report(e).Balanced && w.Report(e).Cash>=0 && w.Report(e).Inventory==w.lots.Where(l=>l.Owner==e).Sum(l=>l.Cost),"恢复账实非法");
        ScenarioLoader.Require(w.People.Keys.Sum(e=>w.Report(e).Cash)==scenario.Issuance,"恢复现金不守恒");
        return w;
    }
}
