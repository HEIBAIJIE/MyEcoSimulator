using System.Text.Json;
using System.Text.Json.Serialization;
namespace EcoSimulator.Core;

// All Int64 fields use decimal strings, including small values, for a uniform lossless wire contract.
public static class Wire
{
    public static JsonSerializerOptions Options { get; } = new() { NumberHandling=JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString, Converters={new JsonStringEnumConverter()} };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value,Options);
}
public sealed record TurnMetrics(long Turn,string Version,long? Price,long ReferencePrice,long DeclaredDemand,long DeclaredSupply,long EffectiveDemand,long EffectiveSupply,long Volume,long UnfilledDemand,long UnfilledSupply,long Production,long Consumption,long Food,long FoodCost,long ResidentCash,long[] CashDistribution,long CompanyCash,long CountryCash,long Wages,long FoodSpending,long Confiscation,long AttendanceDenominator,decimal? Attendance,long Hunger,long Deaths,long Stockouts,string[] StopReasons);
public sealed record RuntimeRecord(string Version,string Phase,AutonomousTurn History,EconomicEvent[] Events,TurnMetrics Metrics);
public sealed record RunStatus(long Turn,bool Running,double Speed,string? PauseReason,long RetainedBytes,long MemoryBudget);
public sealed class SimulationRun
{
    private readonly object gate=new();
    private readonly Scenario configuration;
    private readonly World world;
    private readonly List<RuntimeRecord> records=[];
    private readonly Dictionary<string,(string Operation,RunStatus Result)> requests=[];
    private long retainedBytes;
    private bool running;
    private double speed=1;
    private string? pauseReason;
    private DateTime next=DateTime.MinValue;
    public SimulationRun(Scenario scenario)
    {
        configuration=ScenarioLoader.Read(ScenarioLoader.Write(scenario)); world=World.Load(configuration);
        retainedBytes=Wire.Write(configuration).Length*2L+Wire.Write(world.Snapshot()).Length*2L;
        if(retainedBytes>configuration.Limits.MemoryBytes) pauseReason="memory-budget";
    }
    private RunStatus StatusCore()=>new(world.Turn,running,speed,pauseReason,retainedBytes,configuration.Limits.MemoryBytes);
    public RunStatus Status { get { lock(gate) return StatusCore(); } }
    public RunStatus Command(string requestId,string operation,double? turnsPerSecond=null)
    {
        lock(gate)
        {
            if(string.IsNullOrWhiteSpace(requestId)) throw new RuleException("请求标识必填");
            string signature=operation+":"+turnsPerSecond;
            if(requests.TryGetValue(requestId,out var old)) { if(old.Operation!=signature) throw new RuleException("请求标识冲突"); return old.Result; }
            if(operation=="pause") running=false;
            else if(operation=="step") { running=false; Advance(); }
            else if(operation=="speed") { if(turnsPerSecond is null || !double.IsFinite(turnsPerSecond.Value) || turnsPerSecond<=0 || turnsPerSecond>1000) throw new RuleException("速度必须在0至1000之间"); speed=turnsPerSecond.Value; }
            else if(operation=="resume") { if(pauseReason is null) { running=true; next=DateTime.UtcNow; } }
            else throw new RuleException("不支持手动干预或未知命令");
            var result=StatusCore(); requests.Add(requestId,(signature,result)); return result;
        }
    }
    public void Tick(DateTime now) { lock(gate) if(running && now>=next) { Advance(); next=now.AddSeconds(1/speed); } }
    private void Advance()
    {
        if(pauseReason is not null) return;
        if(world.Turn>=configuration.Limits.Turns) { running=false; pauseReason="completed"; return; }
        // Conservative admission bound: each of the bounded baseline actors may emit a full inventory fact.
        // Never delete promised history. Reserve space before any economic mutation.
        long reservation=checked((Wire.Write(world.Snapshot()).Length*2L+16384)*(configuration.Entities.Length*8L+16));
        if(retainedBytes+reservation>configuration.Limits.MemoryBytes) { running=false; pauseReason="memory-budget"; return; }
        int eventStart=world.Events.Count;
        string version=world.ParameterVersion;
        try
        {
            var turn=world.AdvanceAutonomously();
            var events=world.Events.Skip(eventStart).ToArray();
            var record=new RuntimeRecord(version,"end-of-turn",turn,events,Metrics(turn,events,version));
            records.Add(record); retainedBytes+=Wire.Write(record).Length*2L;
            if(world.Turn>=configuration.Limits.Turns) { running=false; pauseReason="completed"; }
        }
        catch(Exception e) when(e is RuleException or OverflowException) { running=false; pauseReason=e.Message; }
    }
    public static TurnMetrics Metrics(AutonomousTurn h,EconomicEvent[] events,string version)
    {
        var r=h.Settlement; var before=r.Before; var after=r.After;
        var valid=h.Input.Orders.Where(o=>r.Results.Any(x=>x.Kind=="order" && x.Subject==o.Id && x.Success)).ToArray();
        long Sum(IEnumerable<MarketOrder> orders,OrderSide side)=>orders.Where(o=>o.Side==side).Sum(o=>o.Quantity);
        long volume=valid.Where(o=>o.Side==OrderSide.Buy).Sum(o=>r.Results.Single(x=>x.Subject==o.Id && x.Kind=="order").Quantity);
        long consumed=r.Results.Where(x=>x.Kind=="action" && x.Success && x.Reason=="完成进食").Sum(x=>x.Quantity);
        var people=after.Entities.Where(e=>e.Kind==EntityKind.Person).ToArray();
        long wages=events.Where(e=>e.Reason=="劳动工资与产品成本").SelectMany(e=>e.Entries).Where(e=>e.Account==Account.Cash && e.Debit>0).Sum(e=>e.Debit);
        long confiscation=events.Where(e=>e.Reason=="死亡资产清理充公").SelectMany(e=>e.Entries).Where(e=>e.Account==Account.Cash && e.Debit>0).Sum(e=>e.Debit);
        long workers=r.Results.Count(x=>x.Kind=="action" && x.Success && x.Reason=="完成劳动");
        long denominator=before.Tasks.Count(t=>before.Entities.Any(e=>e.Id==t.Employee && e.Health>0));
        return new(r.Turn,version,after.Markets.Single(m=>m.Tag=="food").LatestPrice,after.Markets.Single(m=>m.Tag=="food").ReferencePrice,Sum(h.Input.Orders,OrderSide.Buy),Sum(h.Input.Orders,OrderSide.Sell),Sum(valid,OrderSide.Buy),Sum(valid,OrderSide.Sell),volume,Sum(valid,OrderSide.Buy)-volume,Sum(valid,OrderSide.Sell)-volume,h.ActualProduction,consumed,after.Lots.Where(l=>l.Type=="food").Sum(l=>l.Quantity),after.Lots.Where(l=>l.Type=="food").Sum(l=>l.Cost),people.Sum(e=>after.Accounts[e.Id].Cash),people.Select(e=>after.Accounts[e.Id].Cash).Order().ToArray(),after.Entities.Where(e=>e.Kind==EntityKind.Company).Sum(e=>after.Accounts[e.Id].Cash),after.Entities.Where(e=>e.Kind==EntityKind.Country).Sum(e=>after.Accounts[e.Id].Cash),wages,volume*(after.Markets.Single(m=>m.Tag=="food").LatestPrice??0),confiscation,denominator,denominator==0?null:(decimal)workers/denominator,people.Count(e=>e.Health>0 && e.Satiety==0),people.Count(e=>e.Health==0 && before.Entities.Single(p=>p.Id==e.Id).Health>0),h.Input.People.Count(p=>p.Action.Kind==ActionKind.Eat && p.Action.Conditional && r.Results.Any(x=>x.Subject==p.Person && x.Kind=="action" && !x.Success)),r.Results.Where(x=>x.Kind=="action" && !x.Success).Select(x=>x.Reason).Concat(h.PublishedTasks.Count==0 ? new[]{"无新任务：参见企业决策输入、目标、工资预算及公开日程"}:[]).Distinct().ToArray());
    }
    public string Snapshot(long? turn=null) { lock(gate) return Wire.Write(turn is null ? world.Snapshot() : turn==0 ? (records.Count==0?world.Snapshot():records[0].History.Settlement.Before) : records.Single(r=>r.History.Settlement.After.Turn==turn).History.Settlement.After); }
    public string History(long from=0,long to=long.MaxValue,string? subject=null,string? item=null,string? market=null)
    {
        lock(gate) return Wire.Write(records.Where(r=>r.Metrics.Turn>=from && r.Metrics.Turn<=to && (subject is null || r.History.Decisions.Any(d=>d.Subject==subject) || r.History.Input.Orders.Any(o=>o.Id==subject || o.Entity==subject) || r.History.Settlement.Facts.Any(f=>f.Subject==subject || f.Input.Contains(subject,StringComparison.Ordinal) || f.Output.Contains(subject,StringComparison.Ordinal))) && (item is null || configuration.ItemTypes.Any(t=>(t.Id==item || t.Tag==item) && r.History.Settlement.Before.Lots.Any(l=>l.Type==t.Id))) && (market is null || r.History.Settlement.Before.Markets.Any(m=>m.Region==market || m.Region+":"+m.Tag==market))));
    }
    public string Daily() { lock(gate) return Wire.Write(records.GroupBy(r=>(Day:r.Metrics.Turn/24,r.Version)).Select(g=>new { g.Key.Day,g.Key.Version,Turns=g.Count(),Volume=g.Sum(r=>r.Metrics.Volume),Production=g.Sum(r=>r.Metrics.Production),Consumption=g.Sum(r=>r.Metrics.Consumption),Wages=g.Sum(r=>r.Metrics.Wages),FoodSpending=g.Sum(r=>r.Metrics.FoodSpending),Confiscation=g.Sum(r=>r.Metrics.Confiscation),AttendanceDenominator=g.Sum(r=>r.Metrics.AttendanceDenominator),Price=g.Sum(r=>r.Metrics.Volume)==0?(decimal?)null:g.Sum(r=>(decimal)(r.Metrics.Price??0)*r.Metrics.Volume)/g.Sum(r=>r.Metrics.Volume) })); }
    public string Export() { lock(gate) return Wire.Write(new { SchemaVersion="runtime-1",RuleVersion=configuration.RuleVersion,Configuration=configuration,HistoryRange=new {From=0,To=world.Turn},SnapshotInterval=configuration.Limits.SnapshotInterval,Status=StatusCore(),Phase="end-of-turn",InitialEvents=world.Events.Where(e=>e.Stage=="initialization"),Records=records,Failure=world.Failure,Assumptions=new[]{"固定工资","单企业","无融资分红","禁止个人食品生产","库存压力报价是策略假设","短历史零支出不代表无限购买力","配置重载开始新运行，不保证同历史"} }); }
}
