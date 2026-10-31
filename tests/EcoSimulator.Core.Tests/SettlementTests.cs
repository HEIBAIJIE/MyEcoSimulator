using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class SettlementTests
{
    static PersonIntent Intent(string p, MainAction a, Tile? tile=null, params ItemInteraction[] interactions) => new(p,tile,a,interactions,"固定测试意图");
    static MarketOrder Buy(string id,string p,long price,long n)=>new(id,p,"R0","food",OrderSide.Buy,price,n);
    static MarketOrder Sell(long price,long n)=>new("sell","F","R0","food",OrderSide.Sell,price,n);
    [Fact] public void ClearingMatchesExhaustiveDefinition()
    {
        for(long b1=1;b1<=5;b1++) for(long b2=1;b2<=5;b2++) for(long s1=1;s1<=5;s1++) for(long s2=1;s2<=5;s2++)
        {
            MarketOrder[] orders=[Buy("a","A",b1,2),Buy("b","B",b2,3),new("c","C","R0","food",OrderSide.Sell,s1,2),new("d","D","R0","food",OrderSide.Sell,s2,2)];
            var values=Enumerable.Range(1,5).Select(p => (Price:(long)p,Volume:Math.Min(orders.Where(o => o.Side==OrderSide.Buy && o.Limit>=p).Sum(o => o.Quantity),orders.Where(o => o.Side==OrderSide.Sell && o.Limit<=p).Sum(o => o.Quantity)))).ToArray();
            var k=values.Max(x => x.Volume); var r=Clearing.Clear(orders); Assert.Equal(k,r.Quantity);
            if(k==0) Assert.Null(r.Price); else { Assert.Equal(values.First(x => x.Volume==k).Price,r.Lower); Assert.Equal(values.Last(x => x.Volume==k).Price,r.Upper); Assert.Equal(k,r.Allocations["a"]+r.Allocations["b"]); Assert.Equal(k,r.Allocations["c"]+r.Allocations["d"]); }
        }
        var example=Clearing.Clear([Buy("a","A",10,2),Buy("b","B",8,2),new("c","C","R0","food",OrderSide.Sell,4,1),new("d","D","R0","food",OrderSide.Sell,7,2)]);
        Assert.Equal((3L,7L,8L,7L),(example.Quantity,example.Lower!.Value,example.Upper!.Value,example.Price!.Value)); Assert.Equal(2,example.Allocations["a"]); Assert.Equal(1,example.Allocations["b"]);
    }
    [Fact] public void WageProductAndCostsCommitTogether()
    {
        var w=World.Load(Baseline.Create()); var result=w.Advance(new([Intent("P00",new(ActionKind.Labor,"task-P00"))],[]));
        Assert.Equal(10100,w.Report("P00").Cash); Assert.Equal(199900,w.Report("F").Cash); Assert.Equal(10100,w.Report("F").Inventory);
        Assert.Equal(102,w.Lots.Where(l => l.Owner=="F").Sum(l => l.Quantity)); Assert.Equal(100,w.Lots.Single(l => l.Owner=="F" && l.OriginalQuantity==2).Cost);
        Assert.Equal(Enumerable.Range(1,10),result.Stages); Assert.Equal(98,w.Snapshot().Entities.Single(e => e.Id=="P00").Satiety);
        Assert.Empty(w.View("P00").Inventory.Where(l => l.Owner!="P00")); Assert.Empty(w.View("P00").Tasks);
    }
    [Fact] public void MarketPurchaseCanBeEatenAndNoTradePreservesReference()
    {
        var s=Baseline.Create(); s=s with { Lots=s.Lots.Where(l => l.Owner!="P01").ToArray(),Entities=s.Entities.Select(e => e.Id=="P01"?e with { Satiety=95 }:e).ToArray() };
        var w=World.Load(s); var r=w.Advance(new([Intent("P01",new(ActionKind.Eat,Type:"food",Minimum:1,Maximum:3,Conditional:true))],[Buy("buy","P01",100,1),Sell(100,1)]));
        Assert.Equal(100,w.Snapshot().Entities.Single(e => e.Id=="P01").Satiety); Assert.Equal(9900,w.Report("P01").Cash); Assert.Equal(0,w.Report("P01").Inventory); Assert.DoesNotContain(w.Lots,l => l.Owner=="P01");
        Assert.Contains(r.Results,x => x.Subject=="P01" && x.Kind=="action" && x.Quantity==1);
        w.Advance(new([],[])); var m=Assert.Single(w.Snapshot().Markets); Assert.Null(m.LatestPrice); Assert.Equal(100,m.ReferencePrice); Assert.Equal(100,m.AccountingValue);
    }
    [Fact] public void FrozenSleepAndFailedEatingDoNotSwitchActions()
    {
        var s=Baseline.Create(); s=s with { Lots=s.Lots.Where(l => l.Owner!="P01").ToArray(),Entities=s.Entities.Select(e => e.Id=="P01"?e with { Satiety=0,Energy=0,Health=50 }:e).ToArray() };
        var w=World.Load(s); var r=w.Advance(new([Intent("P00",new(ActionKind.Rest)),Intent("P01",new(ActionKind.Eat,Type:"food",Conditional:true))],[Buy("buy","P00",100,1),Sell(100,1)]));
        Assert.Contains(r.Results,x => x.Subject=="buy" && !x.Success); var p=w.Snapshot().Entities.Single(e => e.Id=="P01"); Assert.Equal(40,p.Health); Assert.Equal(0,p.Energy); Assert.Equal(2,w.Statuses.Count(x => x.Person=="P01"));
    }
    [Fact] public void WagesCannotFundInitialOrdersAndProductionCannotBeSoldEarly()
    {
        var s=Baseline.Create(); s=s with { Cash=s.Cash.Select(c => c.Entity=="P00"?c with { Amount=0 }:c).ToArray(),Issuance=s.Issuance-10000,Lots=s.Lots.Where(l => l.Owner!="F").ToArray() };
        var w=World.Load(s); var r=w.Advance(new([Intent("P00",new(ActionKind.Labor,"task-P00"))],[Buy("buy","P00",1,1),Sell(1,1)]));
        Assert.Equal(100,w.Report("P00").Cash); Assert.Equal(2,w.Lots.Where(l => l.Owner=="F").Sum(l => l.Quantity)); Assert.Contains(r.Results,x => x.Subject=="buy" && !x.Success); Assert.Contains(r.Results,x => x.Subject=="sell" && !x.Success);
    }
    [Fact] public void DeathTransfersCashInvestmentAndDropsCustody()
    {
        var s=Baseline.Create(); s=s with { Entities=s.Entities.Select(e => e.Id=="P00"?e with { Health=5,Satiety=0,Energy=0 }:e).ToArray(),Lots=s.Lots.Select(l => l.Owner=="F"?l with { Location=new(null,"P00"),Custodian="P00" }:l).ToArray() };
        var w=World.Load(s); w.Advance(new([],[])); Assert.Equal(0,w.Report("P00").Cash); Assert.Equal(10000,w.Report("S").Cash); Assert.Equal(200000,w.Report("S").Investment); Assert.Equal(200000,w.Report("F").Cash); Assert.Equal("S",Assert.Single(w.Shares).Holder);
        Assert.DoesNotContain(w.Lots,l => l.Location.Inventory=="P00"); Assert.Equal(4,w.Lots.Where(l => l.Owner==null).Sum(l => l.Quantity)); Assert.Equal(10000,w.Report("F").Inventory);
        var r=w.Advance(new([Intent("P00",new(ActionKind.Rest))],[Buy("dead","P00",1,1)])); Assert.Contains(r.Results,x => x.Subject=="P00" && !x.Success); Assert.Empty(w.Snapshot().Tasks);
    }
    [Fact] public void PickupCompetitionAndMoveDependencyAreLegal()
    {
        for(var run=0;run<20;run++)
        {
            var s=Baseline.Create(); var ground=s.Lots[0] with { Owner=null,Location=new(new("R0",1,1),null),Custodian=null };
            s=s with { Lots=[ground,..s.Lots.Skip(1)] }; var w=World.Load(s);
            var r=w.Advance(new([Intent("P00",new(ActionKind.Eat,Type:"food"),new("R0",1,1),new ItemInteraction(ground.Id,true)),Intent("P01",new(ActionKind.Idle),new("R0",1,1),new ItemInteraction(ground.Id,true)),Intent("P02",new(ActionKind.Labor,"task-P02"),new("R0",100,0))],[]));
            Assert.Equal(10000,w.Report("P00").Cash);
            Assert.Equal(10000,w.Report("P02").Cash); Assert.Single(r.Results.Where(x => x.Kind=="interaction" && x.Success));
            Assert.All(w.Snapshot().Accounts.Values,a => Assert.True(a.Balanced)); Assert.Equal(400000,w.Snapshot().Accounts.Values.Sum(a => a.Cash));
        }
    }
    [Fact] public void WageShortageRotatesAndFailedRoundRetainsEvidence()
    {
        var s=Baseline.Create(); s=s with { Cash=s.Cash.Select(c => c.Entity=="F"?c with { Amount=100 }:c).ToArray(),Shares=[s.Shares[0] with { Investment=100 }],Issuance=200100 };
        var w=World.Load(s); var r=w.Advance(new([Intent("P00",new(ActionKind.Labor,"task-P00")),Intent("P01",new(ActionKind.Labor,"task-P01"))],[]));
        Assert.Equal(10100,w.Report("P00").Cash); Assert.Equal(10000,w.Report("P01").Cash); Assert.Equal(0,w.Report("F").Cash);
        var before=w.Snapshot(); Assert.Throws<RuleException>(() => w.Advance(new([], [Buy("overflow","P01",long.MaxValue,2)]))); Assert.True(w.Paused); Assert.NotNull(w.Failure); Assert.Equal(before.Turn,w.Turn); Assert.Equal(before.Lots,w.Lots); Assert.Equal(before.Accounts["F"],w.Report("F"));
    }
    [Fact] public void OrganizationPickupDoesNotGrantConsumptionAndExplicitConsumptionUsesOwnerBooks()
    {
        var s=Baseline.Create(); var stock=s.Lots.Single(l => l.Owner=="F");
        s=s with { Authorizations=[..s.Authorizations,new("P01","F","pickup"),new("P01","F","custody")] };
        var w=World.Load(s); var r=w.Advance(new([Intent("P01",new(ActionKind.Idle),null,new ItemInteraction(stock.Id,true))],[]));
        Assert.Equal("F",w.Lots.Single(l => l.Id==stock.Id).Owner); Assert.Equal("P01",w.Lots.Single(l => l.Id==stock.Id).Location.Inventory); Assert.Equal(10000,w.Report("F").Inventory);
        var permitted=s with { Authorizations=[..s.Authorizations,new("P01","F","consume")],Lots=s.Lots.Where(l => l.Owner!="P01").ToArray() };
        w=World.Load(permitted); w.Advance(new([Intent("P01",new(ActionKind.Eat,Type:"food"))],[])); Assert.Equal(9900,w.Report("F").Inventory); Assert.Equal(0,w.Report("P01").Inventory);
    }
    [Fact] public void DualOrdersDoNotAffectOthersAndMarginalRemaindersConserve()
    {
        var w=World.Load(Baseline.Create()); var r=w.Advance(new([], [Buy("bad-buy","P00",100,1),new("bad-sell","P00","R0","food",OrderSide.Sell,1,1),Buy("good","P01",100,1),Sell(100,1)]));
        Assert.Equal(10000,w.Report("P00").Cash); Assert.Equal(9900,w.Report("P01").Cash); Assert.Equal(2,r.Results.Count(x => x.Kind=="order" && !x.Success));
        for(int i=0;i<30;i++) { var clear=Clearing.Clear([Buy("a","A",10,3),Buy("b","B",10,3),Sell(1,3)]); Assert.Equal(3,clear.Allocations["a"]+clear.Allocations["b"]); Assert.InRange(clear.Allocations["a"],1,2); }
    }
    [Fact] public void DemandRecoveryImmediatelyRemovesStatusesAndDoesNotInventFractionalFood()
    {
        var s=Baseline.Create(); s=s with { Entities=s.Entities.Select(e => e.Id=="P01"?e with { Satiety=0,Energy=100,Health=80 }:e).ToArray() };
        var w=World.Load(s); w.Advance(new([],[])); Assert.Contains(w.Statuses,x => x.Person=="P01" && x.Kind=="hunger");
        w.Advance(new([Intent("P01",new(ActionKind.Eat,Type:"food"))],[])); Assert.DoesNotContain(w.Statuses,x => x.Person=="P01" && x.Kind=="hunger"); Assert.Equal(3,w.Lots.Where(l => l.Owner=="P01").Sum(l => l.Quantity));
    }}
