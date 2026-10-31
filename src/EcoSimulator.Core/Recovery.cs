namespace EcoSimulator.Core;

public sealed record BalanceValue(string Entity, Account Account, long Value);
public sealed record TradeValue(string Region, string Tag, long Price, long Quantity);
public sealed record RecoveryState(string Schema, long Turn, Entity[] Entities, Lot[] Lots,
    BalanceValue[] Balances, Market[] Markets, WorkTask[] Tasks, Shareholding[] Shares,
    PersonStatus[] Statuses, TradeValue[] Trades, Dictionary<string,PersonalMemory> People,
    Dictionary<string,CompanyMemory> Companies);

public sealed partial class World
{
    // The persistent runtime calls this only at a committed boundary. Full facts live in storage.
    public RecoveryState Capture()
    {
        lock(gate) return new("world-1",Turn,People.Values.ToArray(),lots.ToArray(),
            balances.Select(b=>new BalanceValue(b.Key.Item1,b.Key.Item2,b.Value)).ToArray(),
            (currentMarkets??scenario.Markets).ToArray(),
            publishedTasks.Where(t=>t.EffectiveTurn>=Turn).ToArray(),Shares.ToArray(),statuses.ToArray(),
            trades.GroupBy(t=>(t.Region,t.Tag)).SelectMany(g=>g.TakeLast(checked((int)scenario.Strategy.Window)))
                .Select(t=>new TradeValue(t.Region,t.Tag,t.Price,t.Quantity)).ToArray(),
            new(personalMemories),new(companyMemories));
    }
    public static World Restore(Scenario scenario, RecoveryState state)
    {
        ScenarioLoader.Validate(scenario);
        ScenarioLoader.Require(state.Schema=="world-1" && state.Turn>=0,"不兼容恢复模式");
        var w=new World(scenario) { Turn=state.Turn, currentEntities=state.Entities.ToDictionary(e=>e.Id),
            lots=state.Lots.ToArray(), balances=state.Balances.ToDictionary(b=>(b.Entity,b.Account),b=>b.Value),
            currentMarkets=state.Markets.ToArray(),currentShares=state.Shares.ToArray() };
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
