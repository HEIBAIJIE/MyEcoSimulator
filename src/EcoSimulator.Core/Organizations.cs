namespace EcoSimulator.Core;

public static class OrganizationRules
{
    public static void Validate(Scenario s)
    {
        bool enabled=s.EnabledMechanisms.Contains("organizations-1");
        void R(bool b,string reason)=>ScenarioLoader.Require(b,reason);
        if(!enabled) { R(s.Organizations.Length==0 && s.TaskPriorities.Length==0 && s.SharedResourceRule is null,"Disabled organization configuration"); return; }
        R(!s.EnabledMechanisms.Contains("production-1") && s.Regions.Length==1 && s.Schedule.All(c=>c.Output is null),"organizations-1 uses single-region independent hourly recipes; production-1 remains a separate capability");
        R(s.SharedResourceRule=="priority-rotation-1","Missing shared resource arbitration rule");
        var companies=s.Entities.Where(e=>e.Kind==EntityKind.Company).Select(e=>e.Id).ToArray();
        R(s.Organizations.Select(p=>p.Company).Distinct().Count()==companies.Length && s.Organizations.Length==companies.Length,"Each company needs one explicit policy");
        foreach(var p in s.Organizations) R(companies.Contains(p.Company) && p.StrategyVersion=="organizations-1" && p.SafetyStock>=0 && p.SellPercent>=80 && p.SellPercent<=110,"Invalid company strategy");
        R(s.TaskPriorities.Select(p=>(p.Company,p.Job)).Distinct().Count()==s.TaskPriorities.Length,"Duplicate job priority");
        R(s.ItemTypes.Length==1 && s.ItemTypes[0].Id=="food" && s.ItemTypes[0].Satiety==20 && s.Recipes.All(r=>r.Wage>0),"organizations-1 requires homogeneous food and positive fixed wages");
        foreach(var r in s.Recipes) R(r.Hours==1 && r.Steps.Length==0 && r.OutputType=="food" && !string.IsNullOrWhiteSpace(r.Job) && s.TaskPriorities.Any(p=>p.Company==r.Company && p.Job==r.Job),"Missing hourly job priority or unsupported recipe");
        foreach(var e in s.Employees) R(s.Recipes.Count(r=>r.Company==e.Company && r.Job==e.Job)==1,"Employee job must have one recipe");
        R(s.Recipes.GroupBy(r=>(r.Company,r.Job)).All(g=>g.Count()==1),"Duplicate job recipe");
        foreach(var p in s.TaskPriorities) R(p.Priority>=0 && s.Recipes.Any(r=>r.Company==p.Company && r.Job==p.Job),"Invalid task priority");
        foreach(var g in s.Recipes.GroupBy(r=>(r.Company,r.Job))) R(g.Select(r=>r.Wage).Distinct().Count()==1,"Job wage must be fixed");
    }
}

public sealed partial class World
{
    private long Priority(WorkTask t)=>scenario.TaskPriorities.FirstOrDefault(p=>p.Company==t.Company && p.Job==scenario.Recipes.Single(r=>r.Id==t.Recipe).Job)?.Priority??0;
    private WorkTask[] RankedTasks(IEnumerable<WorkTask> tasks,long turn)
    {
        var stable=scenario.Recipes.Select(r=>(r.Company,r.Id)).ToArray();
        return tasks.OrderBy(Priority).ThenBy(t=> { int i=Array.IndexOf(stable,(t.Company,t.Recipe)); return (i-turn%stable.Length+stable.Length)%stable.Length; }).ToArray();
    }
    private AutonomousTurn AdvanceOrganizationsAutonomously()
    {
        var before=Snapshot(); var people=new List<PersonalDecision>(); var decisions=new List<DecisionRecord>();
        foreach(var e in before.Entities.Where(e=>e.Kind==EntityKind.Person && e.Health>0))
        {
            var view=View(e.Id); var ranked=RankedTasks(view.Tasks,Turn);
            var d=new BaselinePersonStrategy().Decide(view with { Tasks=ranked,Inventory=view.Inventory.Where(l=>l.Owner==e.Id).ToArray() },personalMemories.GetValueOrDefault(e.Id,new()),EffectiveWorld,scenario.Strategy);
            if(d.Intent.Action.Kind==ActionKind.Labor) d=d with { Intent=d.Intent with { Destination=ranked[0].Location } };
            people.Add(d); decisions.Add(new(Turn,e.Id,scenario.RuleVersion,"organizations-1",d.Intent.Reason,Wire.Write(new { Baseline=d.Inputs,Candidates=ranked,Selected=d.Intent.Action.Task })));
        }
        var orders=people.Select(p=>p.Order).OfType<MarketOrder>().ToList(); var tasks=new List<WorkTask>();
        foreach(var policy in scenario.Organizations)
        {
            var warehouse=scenario.Warehouses.Single(w=>w.Organization==policy.Company).Location;
            long stock=lots.Where(l=>l.Owner==policy.Company && l.Type=="food").Sum(l=>l.Quantity);
            var market=before.Markets.Single(m=>m.Tag=="food");
            if(stock>policy.SafetyStock) orders.Add(new($"sell-{Turn}-{policy.Company}",policy.Company,warehouse.Region,"food",OrderSide.Sell,Math.Max(1,market.ReferencePrice*policy.SellPercent/100),stock-policy.SafetyStock));
            long budget=before.Accounts[policy.Company].Cash;
            var memory=companyMemories.GetValueOrDefault(policy.Company,new());
            if(memory.Day!=Turn/24) memory=memory with { Day=Turn/24,Target=Math.Max(Turn==0?scenario.Strategy.TrialProduction:0,Math.Max(0,(memory.Day<0?scenario.Strategy.TrialProduction:memory.Sales)+policy.SafetyStock-stock)),Produced=0,Sales=0,Revenue=0 };
            var committed=before.Tasks.Where(t=>t.Company==policy.Company).Sum(t=>t.Output);
            long remaining=Math.Max(0,memory.Target-memory.Produced-committed);
            var stable=scenario.Employees.Where(e=>e.Company==policy.Company).Select(e=>e.Person).Distinct().ToArray();
            if(!policy.Paused && !BlockNewTasks)
            foreach(var id in stable.Skip(stable.Length==0?0:(int)((Turn+1)%stable.Length)).Concat(stable.Take(stable.Length==0?0:(int)((Turn+1)%stable.Length))))
            foreach(var employee in scenario.Employees.Where(e=>e.Company==policy.Company && e.Person==id))
            {
                var r=scenario.Recipes.Single(r=>r.Company==employee.Company && r.Job==employee.Job);
                if(remaining<=0 || People[id].Health==0 || budget<r.Wage || !scenario.Authorizations.Contains(new(id,policy.Company,"labor"))) continue;
                tasks.Add(new($"org-{Turn+1}-{policy.Company}-{r.Id}-{id}",policy.Company,id,r.Id,r.Version,warehouse,Turn+1,r.Wage,r.Output)); budget-=r.Wage; remaining-=r.Output;
            }
            companyMemories[policy.Company]=memory;
            decisions.Add(new(Turn,policy.Company,scenario.RuleVersion,policy.StrategyVersion,policy.Paused?"Strategy paused":stable.Length==0?"No employees":budget==0?"Wage budget exhausted":"Daily sales target and fixed job wage budget; configured quote assumption",Wire.Write(new { policy,memory,committed,remaining,StableEmployees=stable,Budget=budget,Stock=stock })));
        }
        var input=new TurnInput(people.Select(p=>p.Intent).ToArray(),orders); var result=Advance(input);
        publishedTasks.RemoveAll(t=>t.EffectiveTurn<Turn); publishedTasks.AddRange(tasks);
        result=result with { After=Snapshot(),Facts=result.Facts.Concat(tasks.Select(t=>new StateFact(result.Turn,2,t.Company,"publish-future-task","turn-start-plan",Wire.Write(t)))).ToArray() };
        foreach(var policy in scenario.Organizations)
        {
            var sold=result.Results.SingleOrDefault(r=>r.Kind=="order" && r.Subject==$"sell-{result.Turn}-{policy.Company}")?.Quantity??0;
            var produced=result.Results.Where(r=>r.Kind=="action" && r.Success && r.Reason=="完成劳动" && input.People.Any(p=>p.Person==r.Subject && before.Tasks.Any(t=>t.Id==p.Action.Task && t.Company==policy.Company))).Sum(r=>r.Quantity);
            var memory=companyMemories[policy.Company];
            companyMemories[policy.Company]=memory with { Produced=memory.Produced+produced,Sales=memory.Sales+sold,Revenue=memory.Revenue+sold*(result.After.Markets.Single().LatestPrice??0) };
        }
        foreach(var p in people)
        {
            var feedback=result.Results.SingleOrDefault(r=>r.Kind=="order" && r.Subject==p.Order?.Id);
            var worked=result.Results.Any(r=>r.Subject==p.Intent.Person && r.Kind=="action" && r.Success && r.Reason=="\u5b8c\u6210\u52b3\u52a8");
            personalMemories[p.Intent.Person]=p.Memory with { IncompleteOrders=StrategyMath.Feedback(p.Memory.IncompleteOrders,p.Order,feedback),LastIncome=worked?before.Tasks.Single(t=>t.Id==p.Intent.Action.Task).Wage:0,LastSpending=(feedback?.Quantity??0)*(result.After.Markets.Single().LatestPrice??0),LastUnfilled=p.Order is null?0:p.Order.Quantity-(feedback?.Quantity??0),LastPrice=result.After.Markets.Single().LatestPrice,LastOutcome=string.Join(";",result.Results.Where(r=>r.Subject==p.Intent.Person && r.Kind=="action").Select(r=>r.Reason)) };
        }
        var history=new AutonomousTurn(result,input,tasks,decisions,tasks.Sum(t=>t.Output),result.Results.Where(r=>r.Kind=="action" && r.Success && r.Reason=="完成劳动").Sum(r=>r.Quantity),0);
        autonomousHistory.Add(history); return history;
    }
}

public static class OrganizationScenario
{
    public static Scenario Create()
    {
        var s=Baseline.Create(); var tile=s.Warehouses.Single(w=>w.Organization=="F").Location;
        var recipes=new[]{s.Recipes[0],s.Recipes[0] with { Id="pack",Job="packer",Wage=120,Output=3 },s.Recipes[0] with { Id="rival",Company="G",Wage=90 }};
        return s with { Name="Two companies and fixed jobs",ConfigurationVersion="organizations-1",EnabledMechanisms=["organizations-1"],SharedResourceRule="priority-rotation-1",
            Organizations=[new("F","organizations-1",20,100,false),new("G","organizations-1",10,90,false)],TaskPriorities=recipes.Select(r=>new TaskPriority(r.Company,r.Job,0)).ToArray(),
            Entities=[..s.Entities,new("G",EntityKind.Company,tile,100,100,100)],Cash=[..s.Cash,new("G",100000)],Issuance=s.Issuance+100000,Shares=[..s.Shares,new("P01","G",100000)],Warehouses=[..s.Warehouses,new("G",tile)],
            Employees=[..s.Employees,new("P00","F","packer"),new("P00","G",recipes[2].Job),new("P01","G",recipes[2].Job)],
            Authorizations=[..s.Authorizations,new("P00","G","labor"),new("P01","G","labor")],Recipes=recipes,
            Tasks=[..s.Tasks,new("pack-0","F","P00","pack",recipes[1].Version,tile,0,120,3),new("rival-0","G","P00","rival",recipes[2].Version,tile,0,90,2)],
            Lots=[..s.Lots,new("rival-stock","rival-stock",21,50,5000,0,50,"food","G",new(tile,null),"G")] };
    }
}
