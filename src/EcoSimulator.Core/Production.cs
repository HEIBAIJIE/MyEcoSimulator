namespace EcoSimulator.Core;

public static class ProductionRules
{
    public static long Portion(long quantity, long hours, long step)
    {
        ScenarioLoader.Require(quantity >= 0 && hours > 0 && step > 0 && step <= hours, "Invalid integer allocation");
        return checked((long)((System.Numerics.BigInteger)quantity * step / hours - (System.Numerics.BigInteger)quantity * (step - 1) / hours));
    }
    public static void Validate(Scenario s, Recipe r)
    {
        if (r.Steps.Length == 0) { ScenarioLoader.Require(r.Hours == 1, "Multi-turn recipe requires independent steps"); return; }
        ScenarioLoader.Require(s.EnabledMechanisms.Contains("production-1") && r.Hours == r.Steps.Length && r.Steps.Sum(x => x.Output) == r.Output && r.Steps.Sum(x => x.Wage) == r.Wage, "Step totals do not match recipe");
        var materialTypes = r.Steps.SelectMany(x => x.Materials).Select(x => x.Type).Distinct().ToArray();
        ScenarioLoader.Require(!materialTypes.Contains(r.OutputType), "Production output cannot also be a material in this capability");
        foreach (var step in r.Steps)
        {
            ScenarioLoader.Require(step.Output > 0 && step.Wage >= 0 && step.LaborHours == 1 && step.Materials.Select(x => x.Type).Distinct().Count() == step.Materials.Length && step.Tools.Select(x => x.Type).Distinct().Count() == step.Tools.Length, "Each step requires one hour and complete integer output");
            // This capability supports fixed-input independent steps, never deferred input or WIP.
            ScenarioLoader.Require(materialTypes.All(t => step.Materials.Any(x => x.Type == t && x.Quantity > 0)), "Every product step requires complete materials");
            foreach (var x in step.Materials) ScenarioLoader.Require(x.Quantity > 0 && s.ItemTypes.Any(t => t.Id == x.Type && t.Durability is null) && s.Entities.Any(e => e.Id == x.Provider), "Invalid material or provider");
            foreach (var x in step.Tools) ScenarioLoader.Require(!x.Shared && x.Wear > 0 && s.ItemTypes.Any(t => t.Id == x.Type && t.Durability >= x.Wear) && s.Entities.Any(e => e.Id == x.Provider), "Invalid tool or provider");
        }
        ScenarioLoader.Require(s.Employees.Any(e=>e.Company==r.Company && e.Job==r.Job), "Production requires eligible employees");
    }
}

public sealed partial class World
{
    private readonly Dictionary<string,ActionProgress> productionProgress = new();
    public IReadOnlyList<ActionProgress> ProductionProgress { get { lock(gate) return productionProgress.Values.ToArray(); } }
    private ProductionStep Step(WorkTask t)
    {
        var recipe = RecipeVersions.Single(r => r.Id == t.Recipe && r.Version == t.RecipeVersion);
        var index = productionProgress.TryGetValue(t.Employee, out var p) && p.Task == t.Id && p.State == "running" ? p.Completed : 0;
        return recipe.Steps[index];
    }
    private bool IsStepped(WorkTask t) => RecipeVersions.Single(r => r.Id == t.Recipe && r.Version == t.RecipeVersion).Steps.Length > 0;
}

public sealed record ProductionMemory(long Day, long Target, long Produced, long Sales, long Revenue, long BadTurns, long? PausedAt, long[] SalesWindow);
public sealed partial class World
{
    private ProductionMemory? procurementMemory;
    private AutonomousTurn AdvanceProductionAutonomously()
    {
        var before=Snapshot(); var parameters=scenario.Procurement!; var recipe=scenario.Recipes.Single(); var company=recipe.Company;
        var warehouse=scenario.Warehouses.Single(w => w.Organization==company).Location;
        var productType=scenario.ItemTypes.Single(t => t.Id==recipe.OutputType);
        long Stock(string type) => lots.Where(l => l.Owner==company && l.Type==type).Sum(l => l.Quantity);
        var market=before.Markets.Single(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag==productType.Tag);
        var memory=procurementMemory ?? new ProductionMemory(-1,0,0,0,0,0,null,[]);
        var average=memory.SalesWindow.Length==0 ? parameters.Trial : StrategyMath.Ceil(memory.SalesWindow.Sum()*24,memory.SalesWindow.Length);
        if(memory.Day!=Turn/24) memory=memory with { Day=Turn/24,Target=Math.Max(Turn==0?parameters.Trial:0,StrategyMath.Purchase(average,parameters.Safety,Stock(recipe.OutputType),0)),Produced=0 };
        long estimatedCost=recipe.Steps.Sum(step => step.Wage+step.Materials.Sum(x => x.Quantity*before.Markets.Single(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag==scenario.ItemTypes.Single(t => t.Id==x.Type).Tag).ReferencePrice)+step.Tools.Sum(x => { var type=scenario.ItemTypes.Single(t => t.Id==x.Type); return StrategyMath.Ceil(before.Markets.Single(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag==type.Tag).ReferencePrice*x.Wear,type.Durability!.Value); }));
        bool bad=Turn>=parameters.Observation && (memory.SalesWindow.Sum()==0 || (System.Numerics.BigInteger)market.ReferencePrice*recipe.Output<estimatedCost);
        memory=memory with { BadTurns=bad?memory.BadTurns+1:0 };
        if(memory.BadTurns>=parameters.StopTurns && memory.PausedAt is null) memory=memory with { PausedAt=Turn };
        if(memory.PausedAt is long at && Turn-at>=parameters.ResumeTurns && Stock(recipe.OutputType)<parameters.Safety && (System.Numerics.BigInteger)market.ReferencePrice*recipe.Output>=estimatedCost && Report(company).Cash>=recipe.Steps[0].Wage+parameters.MinimumCash) memory=memory with { PausedAt=null,BadTurns=0 };
        var decisions=new List<DecisionRecord>(); var people=new List<PersonIntent>(); var orders=new List<MarketOrder>();
        var personalDecisions=new List<PersonalDecision>();
        foreach(var person in before.Entities.Where(e => e.Kind==EntityKind.Person && e.Health>0))
        {
            var view=View(person.Id); var pm=personalMemories.GetValueOrDefault(person.Id,new());
            var decision=new BaselinePersonStrategy().Decide(view with { Markets=before.Markets.Where(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag=="food").ToArray() },pm,EffectiveWorld,scenario.Strategy);
            personalDecisions.Add(decision);
            var task=before.Tasks.FirstOrDefault(t => t.Employee==person.Id);
            var intent=decision.Intent;
            if(task is not null && intent.Action.Kind==ActionKind.Idle && productionProgress.GetValueOrDefault(person.Id)?.State=="running") intent=intent with { Action=new(ActionKind.Labor,task.Id) };
            if(intent.Action.Kind==ActionKind.Labor && task is not null)
            {
                bool needsPurchase=Step(task).Materials.Any(m=>lots.Where(l=>l.Owner==m.Provider && l.Type==m.Type && (l.Location.Tile==task.Location || l.Location.Inventory==person.Id)).Sum(l=>l.Quantity)<m.Quantity);
                intent=intent with { Action=intent.Action with { Conditional=needsPurchase } };
            }
            people.Add(intent); if(decision.Order is not null) orders.Add(decision.Order);
            decisions.Add(new(Turn,person.Id,scenario.RuleVersion,parameters.Version,intent.Reason,decision.Inputs));
        }
        long wages=before.Tasks.Where(t => people.Any(p => p.Person==t.Employee && p.Action.Kind==ActionKind.Labor)).Sum(t => Step(t).Wage);
        long budget=Math.Max(0,Report(company).Cash-wages-parameters.MinimumCash);
        var purchases=new List<object>();
        var remainingTasks=before.Tasks.Where(t => IsStepped(t)).ToArray();
        foreach(var material in recipe.Steps.SelectMany(s => s.Materials).Where(x => x.Provider==company).Select(x => x.Type).Distinct())
        {
            long demand=remainingTasks.Sum(t => { int completed=productionProgress.TryGetValue(t.Employee,out var p) && p.Task==t.Id && p.State=="running"?p.Completed:0; return recipe.Steps.Skip(completed).Sum(s => s.Materials.Where(m => m.Type==material).Sum(m => m.Quantity)); });
            var tag=scenario.ItemTypes.Single(t => t.Id==material).Tag; var reference=before.Markets.Single(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag==tag).ReferencePrice;
            long limit=Math.Max(1,checked(reference*parameters.BuyPercent)/100); long gap=StrategyMath.Purchase(demand,parameters.Safety,Stock(material),0); long quantity=Math.Min(gap,budget/limit); budget-=quantity*limit;
            if(quantity>0) orders.Add(new($"procure-{Turn}-{material}",company,warehouse.Region,tag,OrderSide.Buy,limit,quantity));
            purchases.Add(new { material,demand,Inventory=Stock(material),Arrivals=0,gap,quantity,limit });
        }
        long sell=Math.Max(0,Stock(recipe.OutputType)-parameters.Safety);
        if(sell>0) orders.Add(new($"product-{Turn}",company,warehouse.Region,productType.Tag,OrderSide.Sell,Math.Max(1,market.ReferencePrice*parameters.SellPercent/100),sell));
        var tasks=new List<WorkTask>();
        if(memory.PausedAt is null && memory.Produced+before.Tasks.Sum(t => recipe.Steps.Skip(productionProgress.GetValueOrDefault(t.Employee)?.Completed??0).Sum(s => s.Output))<memory.Target)
        {
            long available=Report(company).Cash; var employees=scenario.Employees.Where(e => e.Company==company).ToArray();
            foreach(var employee in employees.Skip(employees.Length==0?0:(int)((Turn+1)%employees.Length)).Concat(employees.Take(employees.Length==0?0:(int)((Turn+1)%employees.Length))))
            {
                if(People[employee.Person].Health==0 || before.Tasks.Any(t => t.Employee==employee.Person) || available<recipe.Steps[0].Wage) continue;
                tasks.Add(new($"production-{Turn+1}-{employee.Person}",company,employee.Person,recipe.Id,recipe.Version,warehouse,Turn+1,recipe.Steps[0].Wage,recipe.Steps[0].Output)); available-=recipe.Steps[0].Wage;
                break;
            }
        }
        var input=new TurnInput(people,orders); var result=Advance(input); publishedTasks.AddRange(tasks);
        publishedTasks.RemoveAll(t => t.EffectiveTurn<Turn && !productionProgress.Values.Any(p => p.Task==t.Id && p.State=="running"));
        result=result with { After=Snapshot(), Facts=result.Facts.Concat(tasks.Select(t => new StateFact(result.Turn,2,company,"publish-future-task","turn-start-plan",Wire.Write(t)))).ToArray() };
        long production=result.Results.Where(r => r.Kind=="action" && r.Success && r.Reason=="完成劳动").Sum(r => r.Quantity);
        foreach(var decision in personalDecisions)
        {
            var feedback=result.Results.SingleOrDefault(r=>r.Kind=="order" && r.Subject==decision.Order?.Id);
            var person=decision.Intent.Person;
            personalMemories[person]=decision.Memory with {
                IncompleteOrders=StrategyMath.Feedback(decision.Memory.IncompleteOrders,decision.Order,feedback),
                LastIncome=result.Results.Any(r=>r.Subject==person && r.Success && r.Reason=="完成劳动") ? before.Tasks.Single(t=>t.Employee==person).Wage : 0,
                LastSpending=(feedback?.Quantity??0)*(result.After.Markets.Single(m=>m.Tag=="food" && m.Region==before.Entities.Single(e=>e.Id==person).Location.Region).LatestPrice??0),
                LastUnfilled=decision.Order is null?0:decision.Order.Quantity-(feedback?.Quantity??0),
                LastOutcome=string.Join(";",result.Results.Where(r=>r.Subject==person && r.Kind=="action").Select(r=>r.Reason)) };
        }
        var sold=result.Results.SingleOrDefault(r => r.Kind=="order" && r.Subject==$"product-{result.Turn}"); long sales=sold?.Quantity??0;
        procurementMemory=memory with { Produced=memory.Produced+production,Sales=sales,Revenue=sales*(result.After.Markets.Single(m => m.Region==scenario.Warehouses.Single(w=>w.Organization==recipe.Company).Location.Region && m.Tag==productType.Tag).LatestPrice??0),SalesWindow=memory.SalesWindow.Append(sales).TakeLast(checked((int)parameters.Window)).ToArray() };
        decisions.Add(new(result.Turn,company,scenario.RuleVersion,parameters.Version,"Wages reserved before procurement; independent steps; configured quote assumptions",Wire.Write(new { parameters,memory,purchases,wages,budget,estimatedCost,Planned=before.Tasks.Sum(t => recipe.Steps[before.Production.FirstOrDefault(p=>p.Person==t.Employee && p.Task==t.Id && p.State=="running")?.Completed??0].Output),Actual=production })));
        var history=new AutonomousTurn(result,input,tasks,decisions,memory.Target,production,Math.Max(0,memory.Produced+production-memory.Target)); autonomousHistory.Add(history); return history;
    }
}

public static class ProductionScenario
{
    public static Scenario Create()
    {
        var s=Baseline.Create(); var tile=s.Warehouses.Single(w => w.Organization=="F").Location;
        var recipe=s.Recipes[0] with { Version="production-1",Output=5,Wage=300,Hours=3,Steps=[new(1,100,1,[new("grain","F",1)],[new("mill","F",1)]),new(2,100,1,[new("grain","F",2)],[new("mill","F",1)]),new(2,100,1,[new("grain","F",2)],[new("mill","F",1)])] };
        return s with { Name="Constrained production",ConfigurationVersion="production-1",EnabledMechanisms=["production-1"],Strategy=s.Strategy with { Version="procurement-1" },Procurement=new("procurement-1",24,1,5,24,100,24,24,110,100),
            ItemTypes=[..s.ItemTypes,new("grain","grain","piece",0,1,true,null),new("mill","mill","piece",0,1,true,6)],Recipes=[recipe],Tasks=[new("task-P00","F","P00",recipe.Id,recipe.Version,tile,0,100,1)],
            Authorizations=[..s.Authorizations,..s.Employees.SelectMany(e => new[]{new Authorization(e.Person,"F","material"),new Authorization(e.Person,"F","tool")})],
            Lots=[..s.Lots,new("grain-stock","grain-stock",21,20,400,0,20,"grain","F",new(tile,null),"F"),new Lot("mill-stock","mill-stock",22,1,120,0,1,"mill","F",new(tile,null),"F") { RemainingDurability=6 }],Markets=[..s.Markets,new("R0","grain",20,20,null),new("R0","mill",120,120,null)] };
    }
}
