using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class SpaceEnvironmentTests
{
    static Scenario Space() { var s=Baseline.Create(); return s with { EnabledMechanisms=["space-1"],Space=new("space-1",2),Regions=[..s.Regions,new("R1",2,0,1)],Markets=[..s.Markets,new("R1","food",200,200,null)] }; }
    static TurnInput Act(MainAction action)=>new([new("P00",null,action,[],"test")],[]);
    [Fact] public void TravelBoundaryRecoveryAndIsolation()
    {
        var s=Space(); var w=World.Load(s); var target=new Tile("R1",3,4);
        w.Advance(Act(new(ActionKind.Travel){Target=target}));
        for(int i=1;i<23;i++) w.Advance(new([],[]));
        Assert.Equal("R0",w.Snapshot().Entities.Single(e=>e.Id=="P00").Location.Region);
        Assert.Equal(100,w.Snapshot().Entities.Single(e=>e.Id=="P00").Satiety);
        w=World.Restore(s,w.Capture());
        var r=w.Advance(new([],[new("buy","P00","R0","food",OrderSide.Buy,100,1)]));
        Assert.Contains(r.Results,x=>x.Subject=="buy" && !x.Success);
        Assert.Equal(target,w.Snapshot().Entities.Single(e=>e.Id=="P00").Location);
        Assert.Equal(24,Assert.Single(w.Snapshot().Transport).Completed);
        Assert.Equal(200,w.Snapshot().Markets.Single(m=>m.Region=="R1").ReferencePrice);
    }
    [Fact] public void TravelZeroNeedsStillKillsAndInterrupts()
    {
        var s=Space(); s=s with { Entities=s.Entities.Select(e=>e.Id=="P00"?e with { Health=5,Satiety=0 }:e).ToArray() };
        var w=World.Load(s); w.Advance(Act(new(ActionKind.Travel){Target=new("R1",0,0)}));
        Assert.Equal(0,w.Snapshot().Entities.Single(e=>e.Id=="P00").Health);
        Assert.Equal("interrupted",Assert.Single(w.Snapshot().Transport).State);
        Assert.DoesNotContain(w.Lots,l=>l.Location.Inventory=="P00");
    }
    [Fact] public void HaulReservesAndMovesTogetherOrInterrupts()
    {
        var s=Space(); s=s with { ItemTypes=[..s.ItemTypes,new("crate","crate","piece",0,1,false,null)],Markets=[..s.Markets,new("R0","crate",100,100,null),new("R1","crate",100,100,null)],Lots=[..s.Lots,new("crate","crate",100,1,10,0,1,"crate","F",new(new Tile("R0",0,0),null),"F")],Authorizations=[..s.Authorizations,new("P00","F","carry")] };
        var w=World.Load(s); var action=new MainAction(ActionKind.Haul){Lot="crate",Target=new("R0",2,2)};
        var r=w.Advance(new(Act(action).People,[new("sell","F","R0","crate",OrderSide.Sell,100,1)]));
        Assert.Contains(r.Results,x=>x.Subject=="sell" && !x.Success);
        Assert.Equal(new Tile("R0",0,0),w.Lots.Single(l=>l.Id=="crate").Location.Tile);
        w=World.Restore(s,w.Capture()); w.Advance(new([],[]));
        Assert.Equal(w.Snapshot().Entities.Single(e=>e.Id=="P00").Location,w.Lots.Single(l=>l.Id=="crate").Location.Tile);
        Assert.Equal(new Tile("R0",2,2),w.Lots.Single(l=>l.Id=="crate").Location.Tile);
    }
    [Fact] public void SpatialScenariosRunThroughServiceMetrics()
    {
        foreach(var environment in new[]{false,true})
        { var s=SpatialScenarios.Create(environment);var w=World.Load(s);for(int i=0;i<48;i++){var h=w.AdvanceAutonomously();var metrics=SimulationRun.Metrics(h,w.Events.Where(e=>e.Turn==h.Settlement.Turn).ToArray(),s.ConfigurationVersion);Assert.Null(metrics.Price);Assert.Equal(s.Issuance,w.Snapshot().Accounts.Values.Sum(a=>a.Cash));} }
    }
    [Fact] public void InterruptionAndInvalidTargetsDoNotMoveOrDuplicate()
    {
        var s=Space();var w=World.Load(s);w.Advance(Act(new(ActionKind.Travel){Target=new("R1",0,0)}));
        w.Advance(Act(new(ActionKind.Rest)));Assert.Equal("interrupted",Assert.Single(w.Snapshot().Transport).State);Assert.Equal("R0",w.Snapshot().Entities.Single(e=>e.Id=="P00").Location.Region);
        var r=w.Advance(Act(new(ActionKind.Travel){Target=new("missing",0,0)}));Assert.Contains(r.Results,x=>x.Kind=="action" && !x.Success);
        Assert.Equal(s.Issuance,w.Snapshot().Accounts.Values.Sum(a=>a.Cash));
    }
    [Fact] public void WarehouseDeterminesMarketAndForeignStockCannotFillSell()
    {
        var s=Space();s=s with { Lots=s.Lots.Select(l=>l.Owner=="F"?l with { Location=new(new Tile("R1",0,0),null) }:l).ToArray() };
        var w=World.Load(s);var r=w.Advance(new([],[new("sell","F","R0","food",OrderSide.Sell,100,1),new("foreign","F","R1","food",OrderSide.Sell,200,1)]));
        Assert.All(r.Results.Where(x=>x.Kind=="order"),x=>Assert.False(x.Success));
    }
    static Scenario Environment()
    {
        var s=Space(); var tile=new Tile("R0",0,0);
        return s with { EnabledMechanisms=["space-1","environment-1"],Environment=new("environment-1",["flat","water"],[],[new("wall",["flat"],true)],[new("wall-0","wall",tile,"P01")],[new("house",tile,1,1,["wall-0"],"F",7)]) };
    }
    [Fact] public void EnvironmentValidatesOwnershipConditionsAndRestOnce()
    {
        var s=Environment(); s=s with { Entities=s.Entities.Select(e=>e.Id=="P00"?e with { Energy=10 }:e).ToArray() };
        var w=World.Load(s); w.Advance(Act(new(ActionKind.Rest)));
        Assert.Equal(37,w.Snapshot().Entities.Single(e=>e.Id=="P00").Energy);
        Assert.Equal("P01",Assert.Single(w.Snapshot().Environment!.Surfaces).Owner);
        var bad=s with { Environment=s.Environment! with { Cells=[new(new("R0",0,0),"water")] } };
        Assert.Throws<RuleException>(()=>World.Load(bad));
        s=s with { Recipes=s.Recipes.Select(r=>r with { Site=new("water",null,null) }).ToArray() };
        w=World.Load(s); var cash=w.Report("F").Cash; var result=w.Advance(Act(new(ActionKind.Labor,"task-P00")));
        Assert.Equal(cash,w.Report("F").Cash); Assert.Contains(result.Results,r=>r.Kind=="action" && !r.Success);
    }
    [Fact] public void ConstructionConflictDoesNotPayOrOverwriteAndRecovers()
    {
        var s=Environment(); s=s with { Environment=s.Environment! with { Surfaces=[],Buildings=[] },Recipes=s.Recipes.Select(r=>r with { Output=1,Site=new("flat",null,null),Construction=new("wall",2,1,true,5) }).ToArray(),Tasks=s.Tasks.Select(t=>t with { Output=1 }).ToArray() };
        var w=World.Load(s); var cash=w.Report("F").Cash;
        var r=w.Advance(new([new("P00",null,new(ActionKind.Labor,"task-P00"),[],"build"),new("P01",null,new(ActionKind.Labor,"task-P01"),[],"build")],[]));
        Assert.Single(w.Snapshot().Environment!.Buildings); Assert.Equal(cash-100,w.Report("F").Cash);
        Assert.Contains(r.Results,r=>!r.Success && r.Reason.Contains("conflict"));
        w=World.Restore(s,w.Capture()); Assert.Equal(2,w.Snapshot().Environment!.Surfaces.Length);
    }
}
