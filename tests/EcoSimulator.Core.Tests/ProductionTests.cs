using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class ProductionTests
{
    [Fact] public void ExternalProviderCostsTransferOnlyWithAuthorization()
    {
        var s=ProductionScenario.Create();
        var recipe=s.Recipes[0];
        recipe=recipe with { Steps=recipe.Steps.Select(step=>step with { Materials=step.Materials.Select(m=>m with { Provider="P01" }).ToArray(),Tools=step.Tools.Select(t=>t with { Provider="P01" }).ToArray() }).ToArray() };
        s=s with { Recipes=[recipe],Lots=s.Lots.Select(l=>l.Type is "grain" or "mill" ? l with { Owner="P01" }:l).ToArray() };
        var denied=World.Load(s); denied.Advance(Labor("P00")); Assert.Equal(10000,denied.Report("P00").Cash);
        // Personal providers grant the same explicit capability as organizational providers.
        s=s with { Authorizations=[..s.Authorizations,new("P00","P01","material"),new("P00","P01","tool")] };
        var world=World.Load(s); world.Advance(Labor("P00"));
        Assert.Equal(10100,world.Report("P00").Cash);
        Assert.Equal(40,s.Lots.Where(l=>l.Owner=="P01").Sum(l=>l.Cost)-world.Report("P01").Inventory);
        Assert.All(world.Snapshot().Accounts.Values,b=>Assert.True(b.Balanced));
    }
    static TurnInput Labor(params string[] people) => new(people.Select(p => new PersonIntent(p,null,new(ActionKind.Labor,"task-"+p),[],"??")).ToArray(),[]);
    [Fact] public void IndependentStepsContinueRecoverAndWearOut()
    {
        var s=ProductionScenario.Create(); var w=World.Load(s);
        w.Advance(Labor("P00"));
        Assert.Equal(1,Assert.Single(w.ProductionProgress).Completed);
        Assert.Equal(10100,w.Report("P00").Cash);
        Assert.Equal(140,w.Lots.Single(l=>l.Type=="food" && l.OriginalQuantity==1 && l.Owner=="F").Cost);
        w=World.Restore(s,w.Capture()); w.Advance(new([],[])); w.Advance(new([],[]));
        Assert.Equal("completed",Assert.Single(w.ProductionProgress).State);
        Assert.Equal(105,w.Lots.Where(l=>l.Type=="food" && l.Owner=="F").Sum(l=>l.Quantity));
        Assert.Equal(3,w.Lots.Single(l=>l.Type=="mill").RemainingDurability);
        Assert.Equal(10300,w.Report("P00").Cash);
        Assert.All(w.Snapshot().Accounts.Values,b=>Assert.True(b.Balanced));
    }
    [Fact] public void MissingInputsAndPermissionPayNothing()
    {
        foreach(var s in new[]{ProductionScenario.Create() with { Lots=ProductionScenario.Create().Lots.Where(l=>l.Type!="grain").ToArray() },ProductionScenario.Create() with { Authorizations=ProductionScenario.Create().Authorizations.Where(a=>a.Permission!="tool").ToArray() }})
        {
            var w=World.Load(s); var r=w.Advance(Labor("P00")); Assert.Equal(10000,w.Report("P00").Cash); Assert.Contains(r.Results,r=>r.Kind=="action" && !r.Success); Assert.Equal(6,w.Lots.Single(l=>l.Type=="mill").RemainingDurability);
        }
    }
    [Fact] public void ExclusiveToolCompetitionAndInterruption()
    {
        var s=ProductionScenario.Create(); s=s with { Tasks=[s.Tasks[0],s.Tasks[0] with { Id="task-P01",Employee="P01" }] };
        var w=World.Load(s); var r=w.Advance(Labor("P00","P01")); Assert.Single(r.Results.Where(r=>r.Reason=="完成劳动"));
        var worker=Assert.Single(w.ProductionProgress).Person;
        w.Advance(new([new(worker,null,new(ActionKind.Rest),[],"????")],[]));
        Assert.Equal("interrupted",Assert.Single(w.ProductionProgress).State); Assert.Equal(1,Assert.Single(w.ProductionProgress).Completed);
        Assert.Equal(5,w.Lots.Single(l=>l.Type=="mill").RemainingDurability);
    }
    [Fact] public void ExhaustedToolIsDeletedAndCannotRunAgain()
    {
        var s=ProductionScenario.Create(); s=s with { Lots=s.Lots.Select(l=>l.Type=="mill"?l with { RemainingDurability=1,WearCost=100 }:l).ToArray() };
        var w=World.Load(s); w.Advance(Labor("P00")); Assert.DoesNotContain(w.Lots,l=>l.Type=="mill");
        var r=w.Advance(new([],[])); Assert.Contains(r.Results,r=>r.Kind=="action" && !r.Success); Assert.Equal(10100,w.Report("P00").Cash);
    }
    [Fact] public void PartialPurchaseRemainsButCompleteStepFails()
    {
        var s=ProductionScenario.Create(); var tile=s.Tasks[0].Location;
        var recipe=s.Recipes[0] with { Hours=1,Output=2,Wage=100,Steps=[new(2,100,1,[new("grain","F",2)],[new("mill","F",1)])] };
        s=s with { Recipes=[recipe],Tasks=[s.Tasks[0] with { Output=2 }],Lots=s.Lots.Where(l=>l.Type!="grain").Append(new("seller-grain","seller-grain",23,1,20,0,1,"grain","P01",new(null,"P01"),"P01")).ToArray() };
        var w=World.Load(s); var r=w.Advance(new([new("P00",null,new(ActionKind.Labor,"task-P00",Conditional:true),[],"????")],[new("buy","F","R0","grain",OrderSide.Buy,20,2),new("sell","P01","R0","grain",OrderSide.Sell,20,1)]));
        Assert.Equal(1,w.Lots.Where(l=>l.Type=="grain" && l.Owner=="F").Sum(l=>l.Quantity)); Assert.Equal(199980,w.Report("F").Cash); Assert.Equal(10000,w.Report("P00").Cash); Assert.Contains(r.Results,r=>r.Kind=="action" && !r.Success);
    }
    [Fact] public void RejectDeferredInputAndCheckIntegerAllocation()
    {
        Assert.Equal(new long[]{1,2,2},Enumerable.Range(1,3).Select(r=>ProductionRules.Portion(5,3,r)));
        Assert.Equal(20,StrategyMath.Purchase(120,0,100,0));
        var s=ProductionScenario.Create(); var r=s.Recipes[0] with { Hours=2,Output=2,Wage=200,Steps=[new(1,100,1,[],[]),new(1,100,1,[new("grain","F",1)],[])] };
        Assert.Throws<RuleException>(()=>World.Load(s with { Recipes=[r] }));
    }
    [Fact] public void AutonomousScenarioIsBoundedAndExplainable()
    {
        var s=ProductionScenario.Create(); var w=World.Load(s);
        for(int i=0;i<72;i++) { var h=w.AdvanceAutonomously(); SimulationRun.Metrics(h,w.Events.Where(e=>e.Turn==h.Settlement.Turn).ToArray(),"production-1"); Assert.Equal(400000,w.Snapshot().Accounts.Values.Sum(b=>b.Cash)); if(i==1) w=World.Restore(s,w.Capture()); }
        Assert.False(w.Paused);
    }
}
