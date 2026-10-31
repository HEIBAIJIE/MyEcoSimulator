namespace EcoSimulator.Core;

public sealed record PersonalMemory(bool Resting = false, long IncompleteOrders = 0, long Demand = 3,
    long LastIncome = 0, long LastSpending = 0, long LastUnfilled = 0, long? LastPrice = null, string LastOutcome = "尚无反馈");
public sealed record PersonalDecision(PersonIntent Intent, MarketOrder? Order, PersonalMemory Memory, string Inputs);
public interface IPersonStrategy
{
    PersonalDecision Decide(DecisionView view, PersonalMemory memory, WorldParameters parameters, StrategyParameters strategy);
}
public static class StrategyMath
{
    public static long Ceil(long n, long d) => checked(n / d + (n % d == 0 ? 0 : 1));
    public static long Purchase(long demand, long safety, long inventory, long arrivals) => Math.Max(0, checked(demand + safety - inventory - arrivals));
    public static long Target(long sales, long inventory, bool initial) => Math.Min(960, Ceil(Math.Max(initial ? 40 : 0, checked(sales + 20 - inventory)), 2) * 2);
    public static long Feedback(long previous, MarketOrder? order, IntentResult? result) => order is null || result is null || !result.Success ? previous : result.Quantity == order.Quantity ? 0 : checked(previous + 1);
}
public sealed class BaselinePersonStrategy : IPersonStrategy
{
    public PersonalDecision Decide(DecisionView v, PersonalMemory m, WorldParameters p, StrategyParameters s)
    {
        var self = v.Self;
        var demand = v.Turn % 24 == 0 ? StrategyMath.Ceil(checked(24 * p.SatietyDecay), 20) : m.Demand;
        var resting = self.Satiety > 40 && (self.Energy <= 20 || m.Resting && self.Energy < 80);
        var task = v.Tasks.FirstOrDefault(t => t.Employee == self.Id && t.EffectiveTurn == v.Turn);
        var action = self.Satiety <= 40 ? new MainAction(ActionKind.Eat, Type: "food", Maximum: StrategyMath.Ceil(100-self.Satiety,20), Conditional:true)
            : resting ? new(ActionKind.Rest) : task is null ? new(ActionKind.Idle) : new(ActionKind.Labor,task.Id);
        long inventory = v.Inventory.Where(l => l.Type == "food").Sum(l => l.Quantity);
        long reference = v.Markets.Single().ReferencePrice;
        long percent = Math.Min(170, (self.Satiety <= 20 ? 150 : self.Satiety <= 40 ? 125 : 100) + Math.Min(20,m.IncompleteOrders / 3 * 10));
        long limit = Math.Max(1,checked(reference * percent) / 100);
        long budget = Math.Max(0,v.Account.Cash - (self.Satiety <= 40 ? 0 : 100));
        long gap = StrategyMath.Purchase(demand,s.SafetyStock,inventory,0);
        long quantity = resting ? 0 : Math.Min(gap,budget / limit);
        MarketOrder? order = quantity == 0 ? null : new($"buy-{v.Turn}-{self.Id}",self.Id,self.Location.Region,"food",OrderSide.Buy,limit,quantity);
        var inputs = System.Text.Json.JsonSerializer.Serialize(new { Previous=m, demand, Safety=s.SafetyStock, inventory, Arrivals=0, budget, reference, percent, limit, gap, quantity, m.IncompleteOrders, NecessaryBudget=demand*reference, CashDays= demand*reference==0 ? (decimal?)null : (decimal)v.Account.Cash/(demand*reference), InventoryHours=p.SatietyDecay==0 ? (decimal?)null : (decimal)inventory*20/p.SatietyDecay, SatietyBuffer=self.Satiety });
        return new(new(self.Id,new(self.Location.Region,0,0),action,Array.Empty<ItemInteraction>(),self.Satiety<=40 ? "饥饿优先进食；失败保持闲置" : resting ? "连续休息至80" : task is null ? "无公开任务闲置" : "执行公开任务"),order,m with { Resting=resting, Demand=demand },inputs);
    }
}
public sealed record CompanyView(long Turn, string Company, BalanceSheet Account, long Inventory, Market Market,
    IReadOnlyList<string> StableEmployees, IReadOnlyList<string> LivingEmployees, IReadOnlyList<WorkTask> Commitments, Recipe Recipe, Tile Warehouse);
public sealed record CompanyMemory(long Day = -1, long Target = 0, long Produced = 0, long Sales = 0, long Revenue = 0,
    long LastDaySales = 40, long LowPriceDays = 0, long? PausedSince = null, long IncompleteOrders = 0);
public sealed record CompanyDecision(IReadOnlyList<WorkTask> Tasks, MarketOrder? Order, CompanyMemory Memory, string Inputs);
public interface ICompanyStrategy { CompanyDecision Decide(CompanyView view, CompanyMemory memory); }
public sealed class BaselineCompanyStrategy : ICompanyStrategy
{
    public CompanyDecision Decide(CompanyView v, CompanyMemory memory)
    {
        var m = memory;
        if(m.Day != v.Turn / 24)
        {
            long low = m.Day < 0 ? 0 : m.Sales > 0 && (System.Numerics.BigInteger)m.Revenue * v.Recipe.Output < (System.Numerics.BigInteger)m.Sales * v.Recipe.Wage ? m.LowPriceDays+1 : 0;
            m = m with { Day=v.Turn/24, LastDaySales=m.Day<0 ? 40 : m.Sales, LowPriceDays=low, Target=StrategyMath.Target(m.Day<0 ? 40 : m.Sales,v.Inventory,v.Turn==0), Produced=0, Sales=0, Revenue=0 };
            if(low>=3 && m.PausedSince is null) m=m with { PausedSince=v.Turn+1 };
        }
        if(m.PausedSince is long since && v.Turn>=since+24 && v.Market.ReferencePrice>=60 && v.Account.Cash>=100 && v.Inventory<20)
            m=m with { PausedSince=null, LowPriceDays=0 };
        long effective=v.Turn+1;
        bool cross=effective%24==0;
        long target=cross ? StrategyMath.Target(m.Sales,v.Inventory,false) : m.Target;
        long committed=v.Commitments.Where(t => t.EffectiveTurn/24==effective/24).Sum(t => t.Output);
        long remaining=Math.Max(0,target-(cross ? 0 : m.Produced)-committed);
        long count=Math.Min(StrategyMath.Ceil(remaining,v.Recipe.Output),v.Account.Cash/v.Recipe.Wage);
        var tasks=new List<WorkTask>();
        if(m.PausedSince is null || effective<m.PausedSince)
        {
            for(int i=0;i<v.StableEmployees.Count && tasks.Count<count;i++)
            {
                string employee=v.StableEmployees[(int)((effective%v.StableEmployees.Count+i)%v.StableEmployees.Count)];
                if(!v.LivingEmployees.Contains(employee)) continue;
                tasks.Add(new($"work-{effective}-{employee}",v.Company,employee,v.Recipe.Id,v.Recipe.Version,v.Warehouse,effective,v.Recipe.Wage,v.Recipe.Output));
            }
        }
        long quantity=v.Account.Cash<2000 || v.Inventory>100 ? v.Inventory : Math.Max(0,v.Inventory-20);
        long percent=Math.Clamp(100-(v.Inventory>100 || m.IncompleteOrders>=3 ? 10 : 0)-(v.Account.Cash<2000 ? 10 : 0)+(v.Inventory<20 ? 10 : 0),80,110);
        long price=Math.Max(1,checked(v.Market.ReferencePrice*percent)/100);
        MarketOrder? order=quantity==0 ? null : new($"sell-{v.Turn}-{v.Company}",v.Company,v.Warehouse.Region,"food",OrderSide.Sell,price,quantity);
        return new(tasks.AsReadOnly(),order,m,System.Text.Json.JsonSerializer.Serialize(new { target, m.Produced, committed, remaining, Cash=v.Account.Cash, v.Inventory, effective, cross, percent, price, quantity, m.PausedSince, m.LowPriceDays, ExpectedSales=cross?m.Sales:m.LastDaySales }));
    }
}
public sealed record DecisionRecord(long Turn, string Subject, string RuleVersion, string StrategyVersion, string Reason, string Inputs);
public sealed record AutonomousTurn(TurnResult Settlement, TurnInput Input, IReadOnlyList<WorkTask> PublishedTasks, IReadOnlyList<DecisionRecord> Decisions, long Target, long ActualProduction, long ExcessProduction);

public sealed partial class World
{
    private readonly Dictionary<string,PersonalMemory> personalMemories = new();
    private readonly Dictionary<string,CompanyMemory> companyMemories = new();
    private readonly List<AutonomousTurn> autonomousHistory = [];
    public IReadOnlyList<AutonomousTurn> AutonomousHistory { get { lock(gate) return autonomousHistory.AsReadOnly(); } }
    public AutonomousTurn AdvanceAutonomously(IPersonStrategy? personStrategy = null, ICompanyStrategy? companyStrategy = null)
    {
        lock(gate)
        {
            personStrategy ??= new BaselinePersonStrategy(); companyStrategy ??= new BaselineCompanyStrategy();
            var before=Snapshot();
            var people=before.Entities.Where(e => e.Kind==EntityKind.Person && e.Health>0).Select(e => personStrategy.Decide(View(e.Id),personalMemories.GetValueOrDefault(e.Id,new()),EffectiveWorld,scenario.Strategy)).ToArray();
            var companies=before.Entities.Where(e => e.Kind==EntityKind.Company).Select(e => {
                var stable=scenario.Employees.Where(x => x.Company==e.Id).Select(x => x.Person).Distinct().ToArray();
                var living=Employees.Where(x => x.Company==e.Id).Select(x => x.Person).ToArray();
                var view=new CompanyView(Turn,e.Id,Report(e.Id),lots.Where(l => l.Owner==e.Id).Sum(l => l.Quantity),before.Markets.Single(),Array.AsReadOnly(stable),Array.AsReadOnly(living),Array.AsReadOnly(scenario.Tasks.Concat(publishedTasks).Where(t => t.Company==e.Id && t.EffectiveTurn>=Turn).ToArray()),EffectiveRecipe(e.Id),scenario.Warehouses.Single(w => w.Organization==e.Id).Location);
                return (Id:e.Id,Decision:companyStrategy.Decide(view,companyMemories.GetValueOrDefault(e.Id,new())));
            }).ToArray();
            var orders=people.Select(p => p.Order).Concat(companies.Select(c => c.Decision.Order)).OfType<MarketOrder>().ToArray();
            var input=new TurnInput(Array.AsReadOnly(people.Select(p => p.Intent).ToArray()),Array.AsReadOnly(orders));
            var tasks=BlockNewTasks ? [] : companies.SelectMany(c => c.Decision.Tasks).ToArray();
            foreach(var task in tasks)
            {
                var recipe=RecipeVersions.SingleOrDefault(r => r.Id==task.Recipe && r.Version==task.RecipeVersion);
                ScenarioLoader.Require(task.EffectiveTurn==Turn+1 && recipe is not null && task.Company==recipe.Company && task.Output==recipe.Output && task.Wage==recipe.Wage && Employees.Any(e => e.Person==task.Employee && e.Company==task.Company && e.Job==recipe.Job) && task.Location==scenario.Warehouses.Single(w => w.Organization==task.Company).Location,"策略发布非法任务");
            }
            ScenarioLoader.Require(tasks.Select(t => t.Id).Distinct().Count()==tasks.Length && !tasks.Any(t => scenario.Tasks.Concat(publishedTasks).Any(old => old.Id==t.Id || old.EffectiveTurn==t.EffectiveTurn && old.Employee==t.Employee)),"重复公开任务");
            var result=Advance(input);
            publishedTasks.AddRange(tasks);
            result=result with { After=Snapshot(), Facts=Array.AsReadOnly(result.Facts.Concat(tasks.Select(t => new StateFact(result.Turn,2,t.Company,"公开次回合任务","回合初信息生成",System.Text.Json.JsonSerializer.Serialize(t)))).ToArray()) };
            foreach(var p in people)
            {
                var feedback=result.Results.SingleOrDefault(r => r.Kind=="order" && r.Subject==p.Order?.Id);
                var actionResults=result.Results.Where(r => r.Subject==p.Intent.Person && r.Kind=="action").ToArray();
                long income=actionResults.Any(r => r.Success && r.Reason=="完成劳动") ? before.Tasks.Single(t => t.Employee==p.Intent.Person).Wage : 0;
                personalMemories[p.Intent.Person]=p.Memory with {
                    IncompleteOrders=StrategyMath.Feedback(p.Memory.IncompleteOrders,p.Order,feedback), LastIncome=income,
                    LastSpending=(feedback?.Quantity??0)*(result.After.Markets.Single().LatestPrice??0),
                    LastUnfilled=p.Order is null ? p.Memory.LastUnfilled : p.Order.Quantity-(feedback?.Quantity??0),
                    LastPrice=result.After.Markets.Single().LatestPrice, LastOutcome=string.Join("；",actionResults.Select(r => r.Reason)) };
            }
            long actual=0,excess=0;
            foreach(var c in companies)
            {
                var d=c.Decision; var sold=result.Results.SingleOrDefault(r => r.Kind=="order" && r.Subject==d.Order?.Id);
                long production=result.Results.Where(r => r.Kind=="action" && r.Success && r.Reason=="完成劳动" && before.Tasks.Any(t => t.Company==c.Id && t.Employee==r.Subject)).Sum(r => r.Quantity);
                long n=sold?.Quantity??0; long revenue=n*(result.After.Markets.Single().LatestPrice??0);
                var m=d.Memory;
                excess+=Math.Max(0,m.Produced+production-m.Target)-Math.Max(0,m.Produced-m.Target); actual+=production;
                companyMemories[c.Id]=m with { Produced=m.Produced+production, Sales=m.Sales+n, Revenue=m.Revenue+revenue, IncompleteOrders=StrategyMath.Feedback(m.IncompleteOrders,d.Order,sold) };
            }
            var records=people.Select(p => new DecisionRecord(result.Turn,p.Intent.Person,scenario.RuleVersion,scenario.Strategy.Version,p.Intent.Reason,p.Inputs)).Concat(companies.Select(c => new DecisionRecord(result.Turn,c.Id,scenario.RuleVersion,scenario.Strategy.Version,"日目标、承诺去重、工资预算及停产滞回；报价为策略假设",c.Decision.Inputs))).ToArray();
            var history=new AutonomousTurn(result,input,Array.AsReadOnly(tasks),Array.AsReadOnly(records),companies.Sum(c => c.Decision.Memory.Target),actual,excess);
            autonomousHistory.Add(history); return history;
        }
    }
}
