using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class OrganizationTests
{
    [Fact] public void OrganizationPermissionsStayScopedAndCarryRequiresAuthorization()
    {
        var s=OrganizationScenario.Create(); var stock=s.Lots.Single(l=>l.Owner=="G");
        s=s with { Authorizations=[..s.Authorizations,new("P02","F","pickup"),new("P02","F","custody"),new("P02","F","view")] };
        var w=World.Load(s);
        Assert.DoesNotContain(w.View("P02").Inventory,l=>l.Owner=="G");
        var r=w.Advance(new([new("P02",null,new(ActionKind.Idle),[new(stock.Id,true)],"scope")],[]));
        Assert.Contains(r.Results,x=>x.Kind=="interaction"&&!x.Success);
        s=s with { Authorizations=[..s.Authorizations,new("P02","G","pickup"),new("P02","G","custody")] };
        w=World.Load(s); w.Advance(new([new("P02",null,new(ActionKind.Idle),[new(stock.Id,true)],"pickup")],[]));
        Assert.Equal("P02",w.Lots.Single(l=>l.Id==stock.Id).Location.Inventory);
        r=w.Advance(new([new("P02",new("R0",1,0),new(ActionKind.Idle),[],"move")],[]));
        Assert.Contains(r.Results,x=>x.Kind=="move"&&!x.Success);
    }
    [Fact] public void EmployeeStableRotationSkipsDeadWithoutRenumbering()
    {
        var s=OrganizationScenario.Create();
        s=s with { Entities=s.Entities.Select(e=>e.Id=="P00"?e with {Health=0}:e).ToArray() };
        var w=World.Load(s); var h=w.AdvanceAutonomously();
        Assert.DoesNotContain(h.PublishedTasks,t=>t.Employee=="P00");
        Assert.Contains(w.Shares,x=>x.Holder=="S"&&x.Company=="F");
        Assert.Contains(h.Decisions,x=>x.Subject=="G"&&x.Inputs.Contains("P00"));
    }
    [Fact] public void MultipleTasksPayOnlyChosenJobAndTradeTogether()
    {
        var s=ScenarioLoader.Read(ScenarioLoader.Write(OrganizationScenario.Create())); var w=World.Load(s);
        var t=s.Tasks.Single(t=>t.Id=="pack-0");
        var r=w.Advance(new([new("P00",null,new(ActionKind.Labor,t.Id),[],"chosen job")], [new("f","F","R0","food",OrderSide.Sell,80,10),new("g","G","R0","food",OrderSide.Sell,80,10),new("b","P02","R0","food",OrderSide.Buy,100,10)]));
        Assert.Equal(10120,w.Report("P00").Cash); Assert.Equal(1,r.Results.Count(r=>r.Reason=="完成劳动"));
        Assert.Equal(90,r.After.Markets.Single().LatestPrice); Assert.Equal(10,r.Results.Where(r=>r.Kind=="order"&&r.Subject is "f" or "g").Sum(r=>r.Quantity));
        Assert.Equal(s.Issuance,r.After.Accounts.Values.Sum(a=>a.Cash)); Assert.All(r.After.Accounts.Values,a=>Assert.True(a.Balanced));
    }
    [Fact] public void ShareholderCannotSellAndCustodyCannotConsume()
    {
        var s=OrganizationScenario.Create(); var w=World.Load(s);
        var r=w.Advance(new([new("P00",null,new(ActionKind.Eat,Type:"food"),[],"test")],[new MarketOrder("unauthorized","F","R0","food",OrderSide.Sell,100,1){Actor="P00"}]));
        Assert.Contains(r.Results,x=>x.Subject=="unauthorized"&&!x.Success);
        s=s with { Lots=s.Lots.Where(l=>l.Owner!="P00").ToArray(),Authorizations=[..s.Authorizations,new("P00","F","custody")] }; w=World.Load(s);
        r=w.Advance(new([new("P00",null,new(ActionKind.Eat,Type:"food"),[],"test")],[]));
        Assert.DoesNotContain(r.Results,x=>x.Reason=="完成进食");
        Assert.Throws<RuleException>(()=>World.Load(s with { SharedResourceRule=null }));
        Assert.Throws<RuleException>(()=>World.Load(s with { Employees=[..s.Employees,new("P02","G","missing")] }));
    }
    [Fact] public void AutonomousRunRestoresAndKeepsEmptyPausedOrganizations()
    {
        var s=OrganizationScenario.Create(); var w=World.Load(s);
        for(int i=0;i<72;i++) { var h=w.AdvanceAutonomously(); Assert.All(h.Settlement.After.Accounts.Values,a=>Assert.True(a.Balanced&&a.Cash>=0)); Assert.Equal(s.Issuance,h.Settlement.After.Accounts.Values.Sum(a=>a.Cash)); }
        w=World.Restore(s,w.Capture()); w.AdvanceAutonomously(); Assert.Equal(73,w.Turn);
        s=s with { Employees=s.Employees.Where(e=>e.Company!="G").ToArray(),Tasks=s.Tasks.Where(t=>t.Company!="G").ToArray(),Authorizations=s.Authorizations.Where(a=>a.Organization!="G").ToArray(),Organizations=s.Organizations.Select(p=>p with { Paused=true }).ToArray() };
        w=World.Load(s); var before=w.Report("G"); var turn=w.AdvanceAutonomously(); Assert.Equal(before,w.Report("G")); Assert.Contains(w.Shares,h=>h.Company=="G"); Assert.Empty(turn.PublishedTasks);
    }
}
