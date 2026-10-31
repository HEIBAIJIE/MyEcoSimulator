using EcoSimulator.Core;
using System.Text.Json.Nodes;
namespace EcoSimulator.Core.Tests;
public class FoundationTests
{
    [Fact] public void BaselineReconcilesAndExplainsSources()
    {
        var s = ScenarioLoader.Read(ScenarioLoader.Write(Baseline.Create())); var w = World.Load(s);
        Assert.Equal(400000,s.Entities.Sum(e => w.Report(e.Id).Cash)); Assert.Equal(180,w.Lots.Sum(l => l.Quantity));
        Assert.Equal(new BalanceSheet(10000,400,200000,0,210400),w.Report("P00"));
        Assert.Equal(new BalanceSheet(200000,10000,0,0,210000),w.Report("F"));
        Assert.All(s.Entities,e => Assert.True(w.Report(e.Id).Balanced));
        Assert.Equal(20,s.Tasks.Length); Assert.All(s.Tasks,t => { Assert.Equal(0,t.EffectiveTurn); Assert.Equal("1",t.RecipeVersion); Assert.Equal(100,t.Wage); });
        Assert.All(s.Entities.Where(e => e.Kind == EntityKind.Person),e => Assert.Equal(new Tile("R0",0,0),e.Location));
        Assert.Contains(w.Events,e => e.Id == "capital-P00-F"); Assert.Equal(43,w.Events.Count);
    }
    [Fact] public void SourceIntervalsSurviveSplitAndFifo()
    {
        var s = Baseline.Create(); var lot = new Lot("tiny","tiny",0,3,1,0,3,"food","P00",new(null,"P00"),"P00");
        var split = lot.Split(1,"first"); Assert.Equal(1,split.Taken.MergeAdjacent(split.Remainder!).Cost); var merged = new[] { split.Remainder!,split.Taken };
        var costs = new List<long>();
        for(var i=0;i<3;i++) { var result = Inventory.TakeFifo(merged,"P00","food",1); costs.Add(result.Taken.Sum(l => l.Cost)); merged=result.Remaining; }
        Assert.Equal(new long[] {0,0,1},costs); Assert.Empty(merged);
        Assert.Throws<RuleException>(() => Inventory.TakeFifo([lot],"P00","food",4)); Assert.Equal(3,lot.Quantity);
    }
    [Fact] public void InvalidConfigurationsAreRejected()
    {
        var s = Baseline.Create();
        Scenario[] invalid = [s with { RuleVersion="old" },s with { EnabledMechanisms=["tools"] },s with { Issuance=-1 },s with { DeathInvestmentPolicy="freeze" },
            s with { Regions=[new("R0",0,0,1),new("R1",0,0,2)] },s with { Regions=[new("R0",long.MaxValue,0,1)] },
            s with { Entities=[..s.Entities,new("bad",EntityKind.Person,new("R0",100,0),100,100,100)] },
            s with { Tasks=[s.Tasks[0] with { Employee="missing" }] },s with { Cash=s.Cash.Select(c => c.Entity=="P00" ? c with {Amount=long.MaxValue}:c).ToArray() },
            s with { Lots=[s.Lots[0] with { OriginalCost=-1 }] },s with { ItemTypes=[s.ItemTypes[0] with { Durability=10 }] },
            s with { Lots=[s.Lots[0],s.Lots[0] with {Id="copy"}] }];
        Assert.All(invalid,c => Assert.Throws<RuleException>(() => World.Load(c)));
        var json = JsonNode.Parse(ScenarioLoader.Write(s))!.AsObject(); json.Remove("World"); Assert.Throws<RuleException>(() => ScenarioLoader.Read(json.ToJsonString()));
        json=JsonNode.Parse(ScenarioLoader.Write(s))!.AsObject(); json["Extra"]=true; Assert.Throws<RuleException>(() => ScenarioLoader.Read(json.ToJsonString()));
        json=JsonNode.Parse(ScenarioLoader.Write(s))!.AsObject(); json["Limits"]=null; Assert.Throws<RuleException>(() => ScenarioLoader.Read(json.ToJsonString()));
    }
    [Fact] public void AtomicCommitRejectsPartialPhysicalOrAccountingChanges()
    {
        var w = World.Load(Baseline.Create()); var before=w.Report("P00"); var events=w.Events.Count; var lots=w.Lots.ToArray();
        var bad = new EconomicEvent("bad","测试账实不符",0,"test",[new("P00",Account.Inventory,0,400),new("P00",Account.RetainedEarnings,400,0)]);
        Assert.Throws<RuleException>(() => w.Commit(bad,lots));
        Assert.Throws<RuleException>(() => w.Commit(bad,lots.Where(l => l.Owner!="P00").Append(lots[0] with {Owner="missing"})));
        var overflow = bad with { Entries=[new("P00",Account.Cash,long.MaxValue,0),new("P00",Account.OpeningEquity,0,long.MaxValue)] };
        Assert.Throws<RuleException>(() => w.Commit(overflow,lots));
        Assert.Equal(before,w.Report("P00")); Assert.Equal(events,w.Events.Count); Assert.Equal(lots,w.Lots);
        w.Commit(bad,lots.Where(l => l.Owner!="P00")); Assert.Equal(0,w.Report("P00").Inventory);
        Assert.Throws<RuleException>(() => w.Commit(bad,w.Lots));
    }
    [Fact] public void MinimalSceneHasNoHardCodedPopulationCompanyOrFood()
    {
        var s=Baseline.Create(); var tile=new Tile("R0",99,99);
        s=s with { Name="minimal", Entities=[new("S",EntityKind.Country,tile,0,0,0),new("A",EntityKind.Person,tile,100,100,100)],
            Memberships=[new("A","S")],Employees=[],Positions=[],Shares=[],Authorizations=[],Warehouses=[new("S",tile)],Recipes=[],Tasks=[],Lots=[],
            Cash=[new("S",0),new("A",7)],Issuance=7,ItemTypes=[new("meal","meal","piece",20,1,true,null)],Markets=[new("R0","meal",3,3,null)] };
        var w=World.Load(ScenarioLoader.Read(ScenarioLoader.Write(s))); Assert.Equal(7,w.Report("A").Cash); Assert.Empty(w.Lots);
        s.Regions[0].Validate(tile); Assert.Throws<RuleException>(() => s.Regions[0].Validate(tile with {X=-1}));
    }
}


