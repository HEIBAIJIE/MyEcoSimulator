using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class StrategyTests
{
    static DecisionView Person(long satiety=100,long energy=100,long cash=10000,long inventory=0,long turn=0,long reference=100)
    {
        var w=World.Load(Baseline.Create()); var v=w.View("P00");
        return v with { Turn=turn,Self=v.Self with { Satiety=satiety,Energy=energy },Account=v.Account with { Cash=cash },Inventory=Array.AsReadOnly(v.Inventory.Select(l => l with { Quantity=inventory }).Where(l => l.Quantity>0).ToArray()),Markets=Array.AsReadOnly(new[]{v.Markets.Single() with { ReferencePrice=reference }}) };
    }
    static CompanyView Company(long turn=0,long inventory=100,long cash=200000,long reference=100)
    {
        var s=Baseline.Create(); var stable=s.Employees.Select(e => e.Person).ToArray();
        return new(turn,"F",new(cash,0,0,0,cash),inventory,s.Markets[0] with { ReferencePrice=reference },Array.AsReadOnly(stable),Array.AsReadOnly(stable),Array.AsReadOnly(Array.Empty<WorkTask>()),s.Recipes[0],s.Warehouses.Single(w => w.Organization=="F").Location);
    }
    [Fact] public void PersonalPriorityRestAndBudgets()
    {
        var strategy=new BaselinePersonStrategy(); var s=Baseline.Create();
        PersonalDecision Decide(DecisionView v,PersonalMemory? m=null)=>strategy.Decide(v,m??new(),s.World,s.Strategy);
        var hungry=Decide(Person(40,20)); Assert.Equal(ActionKind.Eat,hungry.Intent.Action.Kind); Assert.Equal(3,hungry.Intent.Action.Maximum); Assert.Equal(125,hungry.Order!.Limit);
        Assert.Equal(ActionKind.Rest,Decide(Person(50,79),new(true)).Intent.Action.Kind);
        Assert.NotEqual(ActionKind.Rest,Decide(Person(50,80),new(true)).Intent.Action.Kind);
        Assert.Null(Decide(Person(50,20)).Order);
        Assert.Null(Decide(Person(cash:100)).Order);
        Assert.Equal(1,Decide(Person(40,cash:125)).Order!.Quantity);
        var capped=Decide(Person(20,cash:1000),new(false,99)); Assert.Equal(170,capped.Order!.Limit); Assert.True(capped.Order.Quantity*capped.Order.Limit<=1000);
        Assert.Equal(3,Decide(Person()).Memory.Demand); Assert.Equal(4,Decide(Person()).Order!.Quantity);
        Assert.Equal(20,StrategyMath.Purchase(120,0,100,0));
        Assert.Equal(6,StrategyMath.Feedback(6,null,null));
        Assert.Equal(0,StrategyMath.Feedback(6,hungry.Order,new("x","order",true,"",hungry.Order.Quantity)));
        Assert.Equal(7,StrategyMath.Feedback(6,hungry.Order,new("x","order",true,"",1)));
        Assert.Equal(40,StrategyMath.Target(40,100,true)); Assert.Equal(0,StrategyMath.Target(0,100,false)); Assert.Equal(960,StrategyMath.Target(10000,0,false)); Assert.Equal(22,StrategyMath.Target(1,0,false));
    }
    [Fact] public void CompanyCommitmentsCrossDayAndStableRotation()
    {
        var strategy=new BaselineCompanyStrategy(); var v=Company(inventory:0);
        var first=strategy.Decide(v,new()); Assert.Equal(60,first.Memory.Target); Assert.Equal("P01",first.Tasks[0].Employee);
        var current=first.Tasks.Select(t => t with { EffectiveTurn=1 }).ToArray();
        var next=strategy.Decide(v with { Turn=1,Commitments=Array.AsReadOnly(current) },first.Memory);
        Assert.Equal(10,next.Tasks.Count);
        var cross=strategy.Decide(Company(23,0),new(0,100,40,7,700)); Assert.Equal(14,cross.Tasks.Count);
        var dawn=strategy.Decide(Company(24,100) with { Commitments=cross.Tasks },new(0,100,40,9,900));
        Assert.Equal(0,dawn.Memory.Target); Assert.Empty(dawn.Tasks); // published first-hour commitments remain intact
        var dead=strategy.Decide(v with { Turn=19,LivingEmployees=Array.AsReadOnly(v.LivingEmployees.Where(p => p!="P00").ToArray()) },new());
        Assert.Equal("P01",dead.Tasks[0].Employee);
    }
    [Fact] public void LossPauseUsesCompleteDaysAndHysteresis()
    {
        var strategy=new BaselineCompanyStrategy();
        var pause=strategy.Decide(Company(72,0),new(2,40,0,10,490,10,2));
        Assert.Equal(73,pause.Memory.PausedSince); Assert.Empty(pause.Tasks);
        Assert.Null(strategy.Decide(Company(72,0),new(2,40,0,0,0,10,2)).Memory.PausedSince);
        Assert.Empty(strategy.Decide(Company(96,0),pause.Memory).Tasks);
        Assert.NotNull(strategy.Decide(Company(97,0,100,59),pause.Memory).Memory.PausedSince);
        Assert.NotNull(strategy.Decide(Company(97,20,100,60),pause.Memory).Memory.PausedSince);
        var resumed=strategy.Decide(Company(97,0,100,60),pause.Memory);
        Assert.Null(resumed.Memory.PausedSince); Assert.Equal(98,Assert.Single(resumed.Tasks).EffectiveTurn);
    }
    [Fact] public void BaselineRuns720TurnsWithConservationAndExplainableHistory()
    {
        var w=World.Load(Baseline.Create()); long produced=0,consumed=0;
        for(int i=0;i<720;i++)
        {
            var r=w.AdvanceAutonomously(); produced+=r.ActualProduction;
            consumed+=r.Settlement.Results.Where(x => x.Kind=="action" && x.Success && x.Reason=="完成进食").Sum(x => x.Quantity);
            Assert.Equal(i,r.Settlement.Turn); Assert.Equal(400000,r.Settlement.After.Accounts.Values.Sum(a => a.Cash));
            Assert.All(r.Settlement.After.Accounts.Values,a => Assert.True(a.Balanced && a.Cash>=0));
            Assert.Equal(180+produced-consumed,w.Lots.Sum(l => l.Quantity));
            Assert.All(r.PublishedTasks,t => Assert.Equal(i+1,t.EffectiveTurn));
            Assert.All(r.Decisions,d => { Assert.Equal("0.3",d.RuleVersion); Assert.False(string.IsNullOrEmpty(d.Inputs)); });
            if(i==0) { Assert.Equal(40,r.Target); Assert.Equal(40,r.ActualProduction); Assert.Empty(r.PublishedTasks); }
        }
        Assert.False(w.Paused); Assert.Equal(720,w.Turn); Assert.Equal(720,w.AutonomousHistory.Count);
    }
    [Fact] public void ViewsAreReadOnlyAndExcludePrivateEmployeeState()
    {
        var v=Person(); Assert.Throws<NotSupportedException>(()=>((IList<Lot>)v.Inventory).Clear());
        Assert.DoesNotContain(typeof(CompanyView).GetProperties(),p => p.PropertyType==typeof(Entity) || p.PropertyType==typeof(ObserverSnapshot));
        var s=Baseline.Create(); s=s with { Lots=s.Lots.Where(l => l.Owner!="P00").ToArray(),Entities=s.Entities.Select(e => e.Id=="P00"?e with { Satiety=40,Energy=20 }:e).ToArray(),Cash=s.Cash.Select(c => c.Entity=="P00"?c with { Amount=0 }:c).ToArray(),Issuance=s.Issuance-10000 };
        var w=World.Load(s); var r=w.AdvanceAutonomously();
        Assert.Equal(ActionKind.Eat,r.Input.People.Single(p => p.Person=="P00").Action.Kind);
        Assert.Contains(r.Settlement.Results,x => x.Subject=="P00" && x.Kind=="action" && !x.Success);
        Assert.Equal(16,w.View("P00").Self.Energy);
    }
    [Fact] public void ReferenceUses24TradingTurnsAndIgnoresEmptyTurns()
    {
        var scenario=Baseline.Create(); scenario=scenario with { World=scenario.World with { SatietyDecay=0,EnergyDecay=0 } }; var w=World.Load(scenario); var trades=new List<(long Price,long Quantity)>();
        for(int i=0;i<30;i++)
        {
            long price=60+i,quantity=i%2+1;
            w.Advance(new([],new[]{new MarketOrder("b","P00","R0","food",OrderSide.Buy,price,quantity),new MarketOrder("s","F","R0","food",OrderSide.Sell,price,quantity)}));
            trades.Add((price,quantity)); var last=trades.TakeLast(24).ToArray();
            long expected=last.Sum(t => t.Price*t.Quantity)/last.Sum(t => t.Quantity);
            w.Advance(new([],[])); var market=w.Snapshot().Markets.Single();
            Assert.Null(market.LatestPrice); Assert.Equal(expected,market.ReferencePrice); Assert.Equal(100,market.AccountingValue);
        }
    }
    [Fact] public void ZeroPopulationCanRemainIdleFor720Turns()
    {
        var s=Baseline.Create(); s=s with { Entities=s.Entities.Select(e => e.Kind==EntityKind.Person ? e with { Health=5,Satiety=0,Energy=0 }:e).ToArray(),Lots=s.Lots.Where(l => l.Owner=="F").ToArray() };
        var w=World.Load(s);
        for(int i=0;i<720;i++) w.AdvanceAutonomously();
        Assert.False(w.Paused); Assert.Empty(w.Employees); Assert.Empty(w.AutonomousHistory.Last().Input.People);
        Assert.Equal(400000,w.Snapshot().Accounts.Values.Sum(a => a.Cash));
    }
}
