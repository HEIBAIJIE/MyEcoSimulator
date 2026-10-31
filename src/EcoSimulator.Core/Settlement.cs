namespace EcoSimulator.Core;

public enum ActionKind { Idle, Eat, Rest, Labor }
public sealed record MainAction(ActionKind Kind, string? Task = null, string? Type = null, long Minimum = 1, long Maximum = 1, bool Conditional = false);
public sealed record ItemInteraction(string Lot, bool PickUp);
public sealed record PersonIntent(string Person, Tile? Destination, MainAction Action, IReadOnlyList<ItemInteraction> Interactions, string Reason);
public sealed record TurnInput(IReadOnlyList<PersonIntent> People, IReadOnlyList<MarketOrder> Orders);
public sealed record IntentResult(string Subject, string Kind, bool Success, string Reason, long Quantity = 0);
public sealed record Reservation(string Subject, string Purpose, string Resource, long Quantity, long Turn);
public sealed record PersonStatus(string Person, string Kind, long FirstTurn, long RefreshedTurn);
public sealed record StateFact(long Turn, int Stage, string Subject, string Kind, string Input, string Output);
public sealed record ObserverSnapshot(long Turn, IReadOnlyList<Entity> Entities, IReadOnlyList<Lot> Lots, IReadOnlyDictionary<string,BalanceSheet> Accounts, IReadOnlyList<Market> Markets, IReadOnlyList<WorkTask> Tasks)
{
    public IReadOnlyList<ActionProgress> Production { get; init; } = [];
}
public sealed record DecisionView(long Turn, Entity Self, BalanceSheet Account, IReadOnlyList<Lot> Inventory, IReadOnlyList<Authorization> Permissions, IReadOnlyList<WorkTask> Tasks, IReadOnlyList<Market> Markets);
public sealed record TurnResult(long Turn, ObserverSnapshot Before, ObserverSnapshot After, IReadOnlyList<IntentResult> Results, IReadOnlyList<Reservation> Reservations, IReadOnlyList<StateFact> Facts, IReadOnlyList<int> Stages);
public sealed record FailedTurn(long Turn, ObserverSnapshot Before, TurnInput Input, string Reason);

public sealed partial class World
{
    private Dictionary<string,Entity>? currentEntities;
    private Market[]? currentMarkets;
    private readonly List<WorkTask> publishedTasks = [];
    private Shareholding[]? currentShares;
    private readonly List<PersonStatus> statuses = [];
    private readonly List<(string Region,string Tag,long Price,long Quantity)> trades = [];
    private long NextSequence() => checked(Math.Max(scenario.Lots.Select(l => l.Sequence).DefaultIfEmpty(-1).Max(), lots.Select(l => l.Sequence).DefaultIfEmpty(-1).Max()) + 1 + events.Count);
    public long Turn { get; private set; }
    public bool Paused { get; private set; }
    public FailedTurn? Failure { get; private set; }
    public IReadOnlyList<PersonStatus> Statuses { get { lock(gate) return statuses.ToArray(); } }
    public IReadOnlyList<Shareholding> Shares { get { lock(gate) return (currentShares ?? scenario.Shares).ToArray(); } }
    public IReadOnlyList<Membership> Memberships { get { lock(gate) return scenario.Memberships.Where(m => People[m.Person].Health>0).ToArray(); } }
    public IReadOnlyList<Employee> Employees { get { lock(gate) return scenario.Employees.Where(e => People[e.Person].Health>0).ToArray(); } }
    private Dictionary<string,Entity> People => currentEntities ??= scenario.Entities.ToDictionary(e => e.Id);
    public ObserverSnapshot Snapshot()
    {
        lock(gate) return new(Turn,Array.AsReadOnly(People.Values.ToArray()),Lots,
            scenario.Entities.ToDictionary(e => e.Id,e => Report(e.Id)).AsReadOnly(),Array.AsReadOnly((currentMarkets ?? scenario.Markets).ToArray()),
            Array.AsReadOnly(scenario.Tasks.Concat(publishedTasks).Where(t => (t.EffectiveTurn==Turn || productionProgress.Values.Any(p => p.Task==t.Id && p.State=="running")) && People[t.Employee].Health>0).ToArray())) { Production=ProductionProgress };
    }
    public DecisionView View(string entity)
    {
        lock(gate) { var snap=Snapshot(); var self=People[entity];
            return new(Turn,self,Report(entity),Array.AsReadOnly(lots.Where(l => l.Owner==entity).ToArray()),Array.AsReadOnly(scenario.Authorizations.Where(a => a.Person==entity).ToArray()),
                Array.AsReadOnly(snap.Tasks.Where(t => t.Employee==entity || t.Company==entity).ToArray()),snap.Markets); }
    }
    public TurnResult Advance(TurnInput supplied)
    {
        lock(gate)
        {
            ScenarioLoader.Require(!Paused,"非法状态已暂停");
            var input = new TurnInput(supplied.People.Concat(productionProgress.Values.Where(p => p.State=="running" && !supplied.People.Any(i => i.Person==p.Person)).Select(p => new PersonIntent(p.Person,null,new(ActionKind.Labor,p.Task),[],"Continue active production"))).Select(p => p with { Interactions=Array.AsReadOnly(p.Interactions.ToArray()) }).ToArray(), supplied.Orders.ToArray());
            var before=Snapshot(); var oldBalances=new Dictionary<(string,Account),long>(balances); var oldLots=lots.ToArray(); var oldEvents=events.Count;
            var oldProgress = new Dictionary<string,ActionProgress>(productionProgress);
            var oldPeople=People.ToDictionary(x => x.Key,x => x.Value); var oldMarkets=currentMarkets; var oldShares=currentShares; var oldStatuses=statuses.ToArray(); var oldTrades=trades.Count;
            try { return Settle(input,before); }
            catch(Exception e) when(e is RuleException or OverflowException or InvalidOperationException or ArgumentException)
            {
                balances=oldBalances; lots=oldLots; events.RemoveRange(oldEvents,events.Count-oldEvents); currentEntities=oldPeople; currentMarkets=oldMarkets; currentShares=oldShares;
                statuses.Clear(); statuses.AddRange(oldStatuses); trades.RemoveRange(oldTrades,trades.Count-oldTrades);
                productionProgress.Clear(); foreach(var p in oldProgress) productionProgress.Add(p.Key,p.Value);
                Paused=true; Failure=new(Turn,before,input,e.Message); throw new RuleException("回合未提交，已暂停留证: "+e.Message);
            }
        }
    }
    private TurnResult Settle(TurnInput input, ObserverSnapshot before)
    {
        ScenarioLoader.Require(input.People.Select(p => p.Person).Distinct().Count()==input.People.Count && input.Orders.Select(o => o.Id).Distinct().Count()==input.Orders.Count,"重复意图标识");
        var results=new List<IntentResult>(); var reservations=new List<Reservation>(); var facts=new List<StateFact>();
        void Fact(int stage,string subject,string kind,object a,object b) => facts.Add(new(Turn,stage,subject,kind,System.Text.Json.JsonSerializer.Serialize(a),System.Text.Json.JsonSerializer.Serialize(b)));
        void Result(string subject,string kind,bool ok,string reason,long n=0) => results.Add(new(subject,kind,ok,reason,n));
        void Post(int stage,string subject,string reason,IEnumerable<Lot> inventory,params Entry[] entries)
        { var prior=lots.ToArray(); Commit(new(Guid.NewGuid().ToString("N"),reason,Turn,stage.ToString(),entries),inventory); Fact(stage,subject,reason,prior.Where(l => !lots.Contains(l)).ToArray(),new { Lots=lots.Where(l => !prior.Contains(l)).ToArray(),Entries=entries }); }
        Entry D(string e,Account a,long n)=>new(e,a,n,0); Entry C(string e,Account a,long n)=>new(e,a,0,n);
        var actions=new Dictionary<string,MainAction>(); var destinations=new Dictionary<string,Tile>(); var validMove=new Dictionary<string,bool>();
        foreach(var p in input.People)
        {
            if(!People.TryGetValue(p.Person,out var person) || person.Kind!=EntityKind.Person || person.Health==0) { Result(p.Person,"intent",false,"主体不存在或已死亡"); continue; }
            bool move=true; var destination=p.Destination ?? person.Location;
            try { scenario.Regions.Single(r => r.Id==person.Location.Region).Validate(destination); } catch(RuleException) { move=false; }
            validMove[p.Person]=move; destinations[p.Person]=move?destination:person.Location;
            var a=p.Action; bool ok=Enum.IsDefined(a.Kind);
            if(a.Kind==ActionKind.Eat) ok &= move && a.Minimum>0 && a.Maximum>=a.Minimum && scenario.ItemTypes.Any(t => t.Id==a.Type && t.Satiety>0);
            if(a.Kind==ActionKind.Labor) ok &= move && before.Tasks.Any(t => t.Id==a.Task && t.Employee==p.Person && t.Location==destination && scenario.Authorizations.Contains(new(p.Person,t.Company,"labor")));
            if(!ok) { Result(p.Person,"action",false,"静态预检失败"); a=new(ActionKind.Idle); }
            actions[p.Person]=a;
            Fact(2,p.Person,"intent",p,a);
        }
        bool Accessible(Lot l,string person) => l.Location.Inventory==person || l.Location.Tile==People[person].Location;
        bool AtDestination(Lot l,string person) => l.Location.Inventory==person || l.Location.Tile==destinations[person];
        bool Allowed(Lot l,string person,string permission) => l.Owner==person || l.Owner!=null && scenario.Authorizations.Contains(new(person,l.Owner,permission));
        Lot[] TakeAccessible(IEnumerable<Lot> source,long quantity)
        {
            var taken=new List<Lot>();
            foreach(var l in source.OrderBy(l => l.Sequence).ThenBy(l => l.Start))
            { var n=Math.Min(quantity,l.Quantity); if(n==0) break; taken.Add(l.Split(n,Guid.NewGuid().ToString("N")).Taken); quantity-=n; }
            ScenarioLoader.Require(quantity==0,"可用食品不足"); return taken.ToArray();
        }
        var claimed=new Dictionary<string,string>(); var actionLots=new Dictionary<string,List<string>>();
        // Survival claims precede public labor and sale. A pickup-consumption chain has one claim.
        foreach(var p in input.People.Where(p => actions.ContainsKey(p.Person)).OrderBy(_ => Random.Shared.NextInt64()))
        {
            var a=actions[p.Person]; if(a.Kind!=ActionKind.Eat) continue;
            var candidates=lots.Where(l => l.Type==a.Type && AtDestination(l,p.Person) && (Allowed(l,p.Person,"consume") || l.Owner==null && p.Interactions.Any(i => i.PickUp && i.Lot==l.Id))).OrderBy(l => l.Sequence).ThenBy(l => l.Start).ToArray();
            var available=candidates.Where(l => !claimed.ContainsKey(l.Id)).Sum(l => l.Quantity);
            if(available<a.Minimum && !a.Conditional) { actions[p.Person]=new(ActionKind.Idle); Result(p.Person,"action",false,"食品不足或争抢失败"); continue; }
            actionLots[p.Person]=[]; long needed=Math.Min(available,a.Maximum);
            foreach(var l in candidates.Where(l => !claimed.ContainsKey(l.Id))) { if(needed<=0) break; claimed[l.Id]=p.Person; actionLots[p.Person].Add(l.Id); reservations.Add(new(p.Person,"eat",l.Id,Math.Min(needed,l.Quantity),Turn)); needed-=l.Quantity; }
        }
        var cash=before.Accounts.ToDictionary(x => x.Key,x => x.Value.Cash);
        foreach(var group in actions.Where(x => x.Value.Kind==ActionKind.Labor).GroupBy(x => before.Tasks.Single(t => t.Id==x.Value.Task).Company))
        {
            var sequence=scenario.Employees.Where(e => e.Company==group.Key).Select(e => e.Person).Distinct().ToArray();
            foreach(var x in group.OrderBy(x => (Array.IndexOf(sequence,x.Key)-(Turn%sequence.Length)+sequence.Length)%sequence.Length))
            { var task=before.Tasks.Single(t => t.Id==x.Value.Task); if(cash[group.Key]<(IsStepped(task)?Step(task).Wage:task.Wage)) { actions[x.Key]=new(ActionKind.Idle); Result(x.Key,"action",false,"工资预留不足"); } else { cash[group.Key]-=IsStepped(task)?Step(task).Wage:task.Wage; reservations.Add(new(x.Key,"wage",group.Key,IsStepped(task)?Step(task).Wage:task.Wage,Turn)); } }
        }
        var productionMaterials = new Dictionary<string,Lot[]>();
        var productionTools = new Dictionary<string,(string Id,long Wear)[]>();
        bool SelectProduction(string person, WorkTask task, bool postMarket)
        {
            var step=Step(task); var materials=new List<Lot>(); var tools=new List<(string,long)>();
            bool Permission(string provider,string permission) => provider==person || scenario.Authorizations.Contains(new(person,provider,permission));
            foreach(var m in step.Materials)
            {
                if(!Permission(m.Provider,"material")) return false;
                var stock=lots.Where(l => l.Owner==m.Provider && l.Type==m.Type && AtDestination(l,person) && (!claimed.TryGetValue(l.Id,out var holder) || holder==person)).ToArray();
                if(stock.Sum(l => l.Quantity)<m.Quantity) return false;
                materials.AddRange(TakeAccessible(stock,m.Quantity));
            }
            foreach(var tool in step.Tools)
            {
                if(!Permission(tool.Provider,"tool")) return false;
                var lot=lots.FirstOrDefault(l => l.Owner==tool.Provider && l.Type==tool.Type && l.RemainingDurability>=tool.Wear && AtDestination(l,person) && (!claimed.TryGetValue(l.Id,out var holder) || holder==person));
                if(lot is null) return false; tools.Add((lot.Id,tool.Wear));
            }
            foreach(var part in materials) { var original=lots.Single(l => l.Source==part.Source && l.Start==part.Start); claimed[original.Id]=person; reservations.Add(new(person,"material",original.Id,part.Quantity,Turn)); }
            foreach(var tool in tools) { claimed[tool.Item1]=person; reservations.Add(new(person,"tool",tool.Item1,tool.Item2,Turn)); }
            productionMaterials[person]=materials.ToArray(); productionTools[person]=tools.ToArray(); return true;
        }
        foreach(var x in actions.Where(x => x.Value.Kind==ActionKind.Labor).ToArray())
        {
            var task=before.Tasks.Single(t => t.Id==x.Value.Task);
            if(IsStepped(task) && !x.Value.Conditional && !SelectProduction(x.Key,task,false)) { actions[x.Key]=new(ActionKind.Idle); Result(x.Key,"action",false,"Insufficient complete materials, tool durability, or authorization"); }
        }
        var validOrders=new List<MarketOrder>(); var sellLots=new Dictionary<string,Lot[]>();
        foreach(var o in input.Orders)
        {
            bool ok=People.TryGetValue(o.Entity,out var entity) && (entity.Kind!=EntityKind.Person || entity.Health>0) && o.Quantity>0 && o.Limit>0 && Enum.IsDefined(o.Side) && scenario.Markets.Any(m => m.Region==o.Region && m.Tag==o.Tag);
            ok &= input.Orders.Count(x => x.Entity==o.Entity && x.Region==o.Region && x.Tag==o.Tag)==1;
            if(entity!=null)
            {
                var location=entity.Kind==EntityKind.Person?entity.Location:scenario.Warehouses.Single(w => w.Organization==entity.Id).Location;
                ok &= location.Region==o.Region && (!actions.TryGetValue(o.Entity,out var a) || a.Kind!=ActionKind.Rest);
                ok &= o.Side==OrderSide.Buy ? (entity.Kind==EntityKind.Person || scenario.EnabledMechanisms.Contains("production-1")) : (entity.Kind==EntityKind.Company || scenario.EnabledMechanisms.Contains("production-1"));
            }
            if(ok && o.Side==OrderSide.Buy) { var budget=checked(o.Limit*o.Quantity); ok=cash[o.Entity]>=budget; if(ok) { cash[o.Entity]-=budget; reservations.Add(new(o.Id,"buy",o.Entity,budget,Turn)); } }
            if(ok && o.Side==OrderSide.Sell)
            {
                var stock=lots.Where(l => l.Owner==o.Entity && scenario.ItemTypes.Single(t => t.Id==l.Type).Tag==o.Tag && !claimed.ContainsKey(l.Id)).ToArray();
                stock=stock.Where(l => l.RemainingDurability is null).ToArray();
                ok=stock.Sum(l => l.Quantity)>=o.Quantity;
                if(ok) { var type=stock[0].Type; ok=stock.All(l => l.Type==type); if(ok) { var taken=Inventory.TakeFifo(stock,o.Entity,type,o.Quantity).Taken; sellLots[o.Id]=taken; foreach(var l in stock) claimed[l.Id]=o.Id; foreach(var l in taken) reservations.Add(new(o.Id,"sell",l.Source+":"+l.Start,l.Quantity,Turn)); } }
            }
            if(ok) validOrders.Add(o); else Result(o.Id,"order",false,"订单资格、双向申报、现货或预算无效");
        }
        foreach(var p in input.People.Where(p => actions.ContainsKey(p.Person)))
        {
            var person=People[p.Person]; var destination=destinations[p.Person];
            Result(p.Person,"move",validMove[p.Person],validMove[p.Person]?"落点有效":"非法移动");
            if(person.Location!=destination) { People[p.Person]=person with { Location=destination }; Fact(4,p.Person,"move",person.Location,destination); }
        }
        // Equal candidates for each ground lot are shuffled before any interaction is committed.
        var pickupWinners=new Dictionary<string,string>();
        foreach(var g in input.People.Where(p => actions.ContainsKey(p.Person) && validMove[p.Person]).SelectMany(p => p.Interactions.Where(i => i.PickUp).Select(i => (p.Person,i.Lot))).GroupBy(x => x.Lot))
        {
            var l=lots.SingleOrDefault(l => l.Id==g.Key); if(l==null) continue;
            var candidates=g.Where(x => l.Location.Tile==People[x.Person].Location && (l.Owner==null || Allowed(l,x.Person,"pickup")) && (!claimed.TryGetValue(l.Id,out var owner) || owner==x.Person)).Select(x => x.Person).Distinct().ToArray();
            if(candidates.Length>0) pickupWinners[g.Key]=candidates[Random.Shared.Next(candidates.Length)];
        }
        foreach(var p in input.People.Where(p => actions.ContainsKey(p.Person))) foreach(var i in p.Interactions)
        {
            var l=lots.SingleOrDefault(l => l.Id==i.Lot); bool ok=validMove[p.Person] && l!=null && (!claimed.TryGetValue(i.Lot,out var owner) || owner==p.Person);
            ok &= i.PickUp ? l!=null && pickupWinners.GetValueOrDefault(i.Lot)==p.Person && l.Location.Tile==People[p.Person].Location && (l.Owner==null || Allowed(l,p.Person,"pickup")) : l!=null && l.Location.Inventory==p.Person && Allowed(l,p.Person,"drop");
            if(ok)
            {
                var updated=l! with { Owner=l.Owner??p.Person,Location=i.PickUp?new(null,p.Person):new(People[p.Person].Location,null),Custodian=i.PickUp?p.Person:null };
                Entry[] entries=l.Owner==null?[D(p.Person,Account.Inventory,l.Cost),C(p.Person,Account.RetainedEarnings,l.Cost)]:[];
                Post(4,p.Person,"免费交互",lots.Select(x => x.Id==l.Id?updated:x),entries);
                claimed[l.Id]=p.Person;
            }
            Result(p.Person,"interaction",ok,ok?"完成":"位置、权限或预留冲突");
        }
        var marketNext=new List<Market>();
        foreach(var m in before.Markets)
        {
            var orders=validOrders.Where(o => o.Region==m.Region && o.Tag==m.Tag).ToArray(); var clear=Clearing.Clear(orders);
            var candidate=lots.ToList(); var entries=new List<Entry>(); long marketSequence=NextSequence();
            var supply=new Queue<(MarketOrder Order,long Quantity)>(orders.Where(o => o.Side==OrderSide.Sell && clear.Allocations[o.Id]>0).Select(o => (o,clear.Allocations[o.Id])));
            foreach(var buyer in orders.Where(o => o.Side==OrderSide.Buy && clear.Allocations[o.Id]>0))
            {
                long left=clear.Allocations[buyer.Id];
                while(left>0)
                {
                    var seller=supply.Dequeue(); long n=Math.Min(left,seller.Quantity); long amount=checked(n*clear.Price!.Value);
                    var type=sellLots[seller.Order.Id][0].Type;
                    var stock=Inventory.TakeFifo(candidate,seller.Order.Entity,type,n); candidate=stock.Remaining.ToList();
                    long cost=stock.Taken.Sum(l => l.Cost);
                    foreach(var l in stock.Taken) Fact(5,buyer.Id,"delivery",l,new { Owner=buyer.Entity,Quantity=l.Quantity,Price=clear.Price });
                    var id=Guid.NewGuid().ToString("N"); long seq=marketSequence++;
                    candidate.Add(new(id,id,seq,n,amount,0,n,type,buyer.Entity,People[buyer.Entity].Kind==EntityKind.Person ? new(null,buyer.Entity) : new(scenario.Warehouses.Single(w => w.Organization==buyer.Entity).Location,null),buyer.Entity));
                    entries.AddRange([C(buyer.Entity,Account.Cash,amount),D(buyer.Entity,Account.Inventory,amount),D(seller.Order.Entity,Account.Cash,amount),C(seller.Order.Entity,Account.RetainedEarnings,amount),D(seller.Order.Entity,Account.RetainedEarnings,cost),C(seller.Order.Entity,Account.Inventory,cost)]);
                    left-=n; if(seller.Quantity>n) supply.Enqueue((seller.Order,seller.Quantity-n));
                }
            }
            if(clear.Quantity>0) { Post(5,m.Region,"市场原子交割",candidate,entries.ToArray()); trades.Add((m.Region,m.Tag,clear.Price!.Value,clear.Quantity)); }
            foreach(var o in orders) Result(o.Id,"order",true,"有效订单结算",clear.Allocations[o.Id]);
            var window=trades.Where(t => t.Region==m.Region && t.Tag==m.Tag).TakeLast(checked((int)scenario.Strategy.Window)).ToArray();
            long reference=window.Length==0?m.ReferencePrice:(long)(window.Aggregate(System.Numerics.BigInteger.Zero,(a,t) => a+(System.Numerics.BigInteger)t.Price*t.Quantity)/window.Sum(t => t.Quantity));
            marketNext.Add(m with { LatestPrice=clear.Price,ReferencePrice=reference });
            Fact(5,m.Region,"market",orders,new { Clear=clear,Declared=input.Orders.Where(o => o.Region==m.Region && o.Tag==m.Tag).ToArray(),Valid=orders });
        }
        currentMarkets=marketNext.ToArray();
        // Freeze the complete executable set before producing anything.
        var eating=new Dictionary<string,Lot[]>();
        foreach(var x in actions.ToArray())
        {
            if(x.Value.Kind==ActionKind.Eat)
            {
                var stock=lots.Where(l => l.Type==x.Value.Type && Allowed(l,x.Key,"consume") && Accessible(l,x.Key) && (!claimed.TryGetValue(l.Id,out var owner) || owner==x.Key)).ToArray();
                long n=Math.Min(x.Value.Maximum,stock.Sum(l => l.Quantity));
                if(n<x.Value.Minimum) { actions[x.Key]=new(ActionKind.Idle); Result(x.Key,"action",false,"市场后食品数量不足或拾取失败"); }
                else { eating[x.Key]=TakeAccessible(stock,n); foreach(var l in eating[x.Key]) { var original=lots.Single(v => v.Source==l.Source && v.Start==l.Start); claimed[original.Id]=x.Key; if(!reservations.Any(r => r.Subject==x.Key && r.Purpose=="eat" && r.Resource==original.Id)) reservations.Add(new(x.Key,"execute-eat",l.Source+":"+l.Start,l.Quantity,Turn)); } }
            }
            if(x.Value.Kind==ActionKind.Labor)
            { var t=before.Tasks.Single(t => t.Id==x.Value.Task); if(People[x.Key].Location!=t.Location || Report(t.Company).Cash<t.Wage || IsStepped(t) && (x.Value.Conditional ? !SelectProduction(x.Key,t,true) : productionMaterials[x.Key].Any(part => !lots.Any(l => l.Source==part.Source && l.Start==part.Start && l.Quantity>=part.Quantity && l.Owner==part.Owner && Accessible(l,x.Key))) || productionTools[x.Key].Any(tool => !lots.Any(l => l.Id==tool.Id && l.RemainingDurability>=tool.Wear && Accessible(l,x.Key))))) { actions[x.Key]=new(ActionKind.Idle); Result(x.Key,"action",false,"执行前劳动条件失效"); } }
        }
        foreach(var x in actions)
        {
            var p=People[x.Key]; var a=x.Value;
            if(a.Kind==ActionKind.Eat)
            {
                var selected=eating[x.Key]; var ids=selected.Select(l => l.Source).ToHashSet(); var stock=lots.Where(l => ids.Contains(l.Source) && Allowed(l,x.Key,"consume") && Accessible(l,x.Key)).ToArray();
                var consumed=TakeAccessible(stock,selected.Sum(l => l.Quantity));
                var candidate=lots.ToList(); var consumptionEntries=new List<Entry>();
                foreach(var part in consumed)
                {
                    var original=candidate.Single(l => l.Source==part.Source && l.Start==part.Start);
                    var split=original.Split(part.Quantity,part.Id); candidate.Remove(original); if(split.Remainder!=null) candidate.Add(split.Remainder);
                    consumptionEntries.AddRange([C(part.Owner!,Account.Inventory,part.Cost),D(part.Owner!,Account.RetainedEarnings,part.Cost)]);
                }
                long n=consumed.Sum(l => l.Quantity);
                Post(7,x.Key,"进食",candidate,consumptionEntries.ToArray());
                People[x.Key]=p with { Satiety=Math.Min(EffectiveWorld.Maximum,checked(p.Satiety+checked(n*scenario.ItemTypes.Single(t => t.Id==a.Type).Satiety))) }; Fact(7,x.Key,"satiety",p,People[x.Key]); Result(x.Key,"action",true,"完成进食",n);
            }
            else if(a.Kind==ActionKind.Labor)
            {
                var t=before.Tasks.Single(t => t.Id==a.Task);
                if(IsStepped(t))
                {
                    var step=Step(t); var candidate=lots.ToList(); var entries=new List<Entry>(); long cost=step.Wage;
                    foreach(var part in productionMaterials[x.Key])
                    {
                        var original=candidate.Single(l => l.Source==part.Source && l.Start==part.Start); var split=original.Split(part.Quantity,part.Id);
                        candidate.Remove(original); if(split.Remainder is not null) candidate.Add(split.Remainder);
                        cost=checked(cost+part.Cost); entries.Add(C(part.Owner!,Account.Inventory,part.Cost));
                        if(part.Owner!=t.Company) { entries.Add(D(part.Owner!,Account.RetainedEarnings,part.Cost)); entries.Add(C(t.Company,Account.RetainedEarnings,part.Cost)); }
                    }
                    foreach(var tool in productionTools[x.Key])
                    {
                        var original=candidate.Single(l => l.Id==tool.Id); var maximum=scenario.ItemTypes.Single(i => i.Id==original.Type).Durability!.Value;
                        long remaining=original.RemainingDurability!.Value-tool.Wear;
                        long wear=(long)((System.Numerics.BigInteger)original.OriginalCost*(maximum-remaining)/maximum);
                        long delta=wear-original.WearCost; cost=checked(cost+delta);
                        candidate[candidate.IndexOf(original)]=original with { RemainingDurability=remaining,WearCost=wear };
                        entries.Add(C(original.Owner!,Account.Inventory,delta));
                        if(original.Owner!=t.Company) { entries.Add(D(original.Owner!,Account.RetainedEarnings,delta)); entries.Add(C(t.Company,Account.RetainedEarnings,delta)); }
                    }
                    var productId=Guid.NewGuid().ToString("N");
                    candidate.Add(new(productId,productId,NextSequence(),step.Output,cost,0,step.Output,RecipeVersions.Single(r => r.Id==t.Recipe && r.Version==t.RecipeVersion).OutputType,t.Company,new(t.Location,null),t.Company));
                    entries.AddRange([C(t.Company,Account.Cash,step.Wage),D(t.Company,Account.Inventory,cost),D(x.Key,Account.Cash,step.Wage),C(x.Key,Account.RetainedEarnings,step.Wage)]);
                    Post(7,x.Key,"劳动工资与产品成本",candidate,entries.ToArray());
                    var prior=productionProgress.GetValueOrDefault(x.Key); int completed=prior?.Task==t.Id && prior.State=="running" ? prior.Completed+1 : 1;
                    var progress=new ActionProgress(x.Key,t.Id,t.Recipe,t.RecipeVersion,prior?.Task==t.Id && prior.State=="running" ? prior.Started : Turn,completed,completed==RecipeVersions.Single(r => r.Id==t.Recipe && r.Version==t.RecipeVersion).Hours ? "completed" : "running");
                    productionProgress[x.Key]=progress; Fact(7,x.Key,"production-step",new { Materials=productionMaterials[x.Key],Tools=productionTools[x.Key],step },progress);
                    Result(x.Key,"action",true,"完成劳动",step.Output); continue;
                }
                var recipe=RecipeVersions.Single(r => r.Id==t.Recipe && r.Version==t.RecipeVersion); var id=Guid.NewGuid().ToString("N"); var seq=NextSequence();
                var product=new Lot(id,id,seq,t.Output,t.Wage,0,t.Output,recipe.OutputType,t.Company,new(scenario.Warehouses.Single(w => w.Organization==t.Company).Location,null),t.Company);
                Post(7,x.Key,"劳动工资与产品成本",[..lots,product],C(t.Company,Account.Cash,t.Wage),D(t.Company,Account.Inventory,t.Wage),D(x.Key,Account.Cash,t.Wage),C(x.Key,Account.RetainedEarnings,t.Wage)); Result(x.Key,"action",true,"完成劳动",t.Output);
            }
            else if(a.Kind==ActionKind.Rest) { People[x.Key]=p with { Energy=Math.Min(EffectiveWorld.Maximum,checked(p.Energy+EffectiveWorld.RestRecovery)) }; Fact(7,x.Key,"rest",p,People[x.Key]); Result(x.Key,"action",true,"完成休息"); }
            else Result(x.Key,"action",true,"实际闲置");
        }
        foreach(var p in People.Values.Where(e => e.Kind==EntityKind.Person && e.Health>0).ToArray())
        {
            var a=actions.GetValueOrDefault(p.Id,new(ActionKind.Idle)); var w=EffectiveWorld;
            long satiety=a.Kind==ActionKind.Eat?p.Satiety:Math.Max(0,p.Satiety-w.SatietyDecay), energy=a.Kind==ActionKind.Rest?p.Energy:Math.Max(0,p.Energy-w.EnergyDecay);
            void Status(string name,bool active) { var prior=statuses.SingleOrDefault(s => s.Person==p.Id && s.Kind==name); statuses.RemoveAll(s => s.Person==p.Id && s.Kind==name); if(active) statuses.Add(new(p.Id,name,prior?.FirstTurn??Turn,Turn)); }
            Status("hunger",satiety==0); Status("fatigue",energy==0);
            var damage=checked((satiety==0?w.HungerDamage:0)+(energy==0?w.FatigueDamage:0));
            var health=damage>0?Math.Max(0,p.Health-damage):satiety>=w.HealthyThreshold && energy>=w.HealthyThreshold?Math.Min(w.Maximum,checked(p.Health+w.HealthRecovery)):p.Health;
            People[p.Id]=p with { Satiety=satiety,Energy=energy,Health=health }; Fact(8,p.Id,"needs",p,People[p.Id]);
        }
        foreach(var p in People.Values.Where(e => e.Kind==EntityKind.Person && e.Health==0 && (before.Entities.Single(x => x.Id==e.Id).Health>0 || lots.Any(l => l.Owner==e.Id || l.Location.Inventory==e.Id) || Report(e.Id).Cash>0 || Report(e.Id).Investment>0)).ToArray())
        {
            var report=Report(p.Id); var entries=new List<Entry> { C(p.Id,Account.Cash,report.Cash),D(p.Id,Account.RetainedEarnings,report.Cash),D(scenario.Issuer,Account.Cash,report.Cash),C(scenario.Issuer,Account.RetainedEarnings,report.Cash),C(p.Id,Account.Inventory,report.Inventory),D(p.Id,Account.RetainedEarnings,report.Inventory),C(p.Id,Account.Investment,report.Investment),D(p.Id,Account.RetainedEarnings,report.Investment),D(scenario.Issuer,Account.Investment,report.Investment),C(scenario.Issuer,Account.RetainedEarnings,report.Investment) };
            var candidate=lots.Select(l => l.Owner==p.Id?l with { Owner=null,Location=new(p.Location,null),Custodian=null }:l.Location.Inventory==p.Id?l with { Location=new(p.Location,null),Custodian=null }:l).ToArray();
            Post(9,p.Id,"死亡资产清理充公",candidate,entries.ToArray());
            currentShares=(currentShares??scenario.Shares).Select(h => h.Holder==p.Id?h with { Holder=scenario.Issuer }:h).ToArray();
            Fact(9,p.Id,"关系清理",scenario.Memberships.Where(m => m.Person==p.Id),"死亡者不再参与任务及成员活动");
        }
        foreach(var p in productionProgress.Values.Where(p => p.State=="running").ToArray())
            if(!actions.TryGetValue(p.Person,out var action) || action.Kind!=ActionKind.Labor || action.Task!=p.Task || People[p.Person].Health==0)
            { var interrupted=p with { State="interrupted" }; productionProgress[p.Person]=interrupted; Fact(9,p.Person,"production-interrupted",p,interrupted); }
        foreach(var intent in input.People.Where(p=>p.Action.Kind==ActionKind.Labor && results.Any(r=>r.Subject==p.Person && r.Kind=="action" && !r.Success)))
            Fact(9,intent.Person,"production-precheck-failed",intent.Action,new { Progress=productionProgress.GetValueOrDefault(intent.Person),Released=reservations.Where(r=>r.Subject==intent.Person).ToArray() });
        foreach(var lot in lots.Where(l => l.RemainingDurability==0).ToArray()) Post(9,lot.Owner!,"exhausted-tool-cleanup",lots.Where(l => l.Id!=lot.Id));
        Fact(10,"world","反馈与预留释放",reservations,results); Turn++;
        return new(Turn-1,before,Snapshot(),Array.AsReadOnly(results.ToArray()),Array.AsReadOnly(reservations.ToArray()),Array.AsReadOnly(facts.ToArray()),Array.AsReadOnly(Enumerable.Range(1,10).ToArray()));
    }
}
