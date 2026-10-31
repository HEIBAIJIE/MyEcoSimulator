using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
namespace EcoSimulator.Core;

public sealed record PersistencePolicy(string Schema,long StorageBytes,long InflightBytes,long ProcessBytes,int PageSize)
{
    public static PersistencePolicy Local => new("mongo-1",4L*1024*1024*1024,64*1024*1024,2L*1024*1024*1024,20);
    public void Validate() => StorageGuard.Require(Schema=="mongo-1" && StorageBytes>0 && InflightBytes>0 && ProcessBytes>0 && PageSize is >0 and <=100,"持久化边界必须显式配置");
}
public interface IRunStore
{
    Task Initialize(CancellationToken ct=default);
    Task<string> Create(string request,Scenario scenario,PersistencePolicy policy,CancellationToken ct=default);
    Task<RunStatus> Command(string id,string request,string operation,double? speed=null,CancellationToken ct=default);
    Task<string> Snapshot(string id,long? turn=null,CancellationToken ct=default);
    Task<string> History(string id,long after=-1,int limit=20,string? subject=null,string? item=null,string? market=null,CancellationToken ct=default);
    Task Export(string id,Stream output,CancellationToken ct=default);
}

// Every large payload is losslessly encoded JSON, split into <=1 MiB BSON binary documents.
// Metadata cursors and unique request keys are committed in the same majority transaction.
public sealed class MongoRunStore : IRunStore
{
    private readonly IMongoDatabase db;
    private readonly IMongoCollection<BsonDocument> runs,requests,chunks,turns;
    public Action<string>? Fault { get; set; }
    public MongoRunStore(string connection,string database)
    {
        db=new MongoClient(connection).GetDatabase(database);
        runs=db.GetCollection<BsonDocument>("runs"); requests=db.GetCollection<BsonDocument>("requests");
        chunks=db.GetCollection<BsonDocument>("chunks"); turns=db.GetCollection<BsonDocument>("turns");
    }
    public async Task Initialize(CancellationToken ct=default)
    {
        var hello=await db.RunCommandAsync<BsonDocument>(new BsonDocument("hello",1),cancellationToken:ct);
        StorageGuard.Require(hello.Contains("setName") || hello.GetValue("msg","")=="isdbgrid","MongoDB必须支持事务");
        using var s=await db.Client.StartSessionAsync(cancellationToken:ct); s.StartTransaction();
        await db.GetCollection<BsonDocument>("probe").InsertOneAsync(s,new BsonDocument("_id",Guid.NewGuid().ToString()),cancellationToken:ct);
        await s.AbortTransactionAsync(ct);
        await turns.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { {"run",1},{"turn",1} },new CreateIndexOptions { Unique=true }),cancellationToken:ct);
        foreach(var field in new[]{"subjects","items","markets"})
            await turns.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { {"run",1},{field,1},{"turn",1} }),cancellationToken:ct);
        await chunks.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { {"run",1},{"key",1},{"part",1} },new CreateIndexOptions { Unique=true }),cancellationToken:ct);
    }
    static T Read<T>(string json)=>JsonSerializer.Deserialize<T>(json,Wire.Options)??throw new RuleException("空存储数据");
    static string Key(string run,string request)=>Wire.Write(new[]{run,request});
    static TransactionOptions Tx=>new(readConcern:ReadConcern.Snapshot,writeConcern:WriteConcern.WMajority);
    async Task Put(IClientSessionHandle session,string run,string key,string json,CancellationToken ct)
    {
        var bytes=Encoding.UTF8.GetBytes(json);
        for(int offset=0,part=0;offset<bytes.Length;offset+=1024*1024,part++)
            await chunks.InsertOneAsync(session,new BsonDocument { {"run",run},{"key",key},{"part",part},{"data",new BsonBinaryData(bytes.AsSpan(offset,Math.Min(1024*1024,bytes.Length-offset)).ToArray())} },cancellationToken:ct);
    }
    async Task<string> Payload(string run,string key,CancellationToken ct)
    {
        using var cursor=await chunks.FindAsync(new BsonDocument { {"run",run},{"key",key} },new FindOptions<BsonDocument> { Sort=new BsonDocument("part",1),BatchSize=1 },ct);
        using var data=new MemoryStream(); int part=0;
        while(await cursor.MoveNextAsync(ct)) foreach(var d in cursor.Current) { StorageGuard.Require(d["part"].AsInt32==part++,"数据分片不连续"); await data.WriteAsync(d["data"].AsByteArray,ct); }
        StorageGuard.Require(part>0,"存储数据不存在"); return Encoding.UTF8.GetString(data.ToArray());
    }
    async Task<BsonDocument> Run(string id,CancellationToken ct)
    {
        var d=await runs.Find(new BsonDocument("_id",id)).FirstOrDefaultAsync(ct)??throw new RuleException("运行不存在");
        StorageGuard.Require(d["schema"]=="mongo-1","不兼容持久化模式"); return d;
    }
    public async Task<string> Create(string request,Scenario scenario,PersistencePolicy policy,CancellationToken ct=default)
    {
        StorageGuard.Require(!string.IsNullOrWhiteSpace(request),"请求标识必填"); policy.Validate(); ScenarioLoader.Validate(scenario);
        string signature=Wire.Write(new {scenario,policy}), key=Key("create",request);
        var old=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(ct);
        if(old!=null) { StorageGuard.Require(old["signature"]==signature,"请求标识冲突"); return old["result"].AsString; }
        string id=Guid.NewGuid().ToString("N"); var w=World.Load(scenario);
        using var s=await db.Client.StartSessionAsync(cancellationToken:ct); s.StartTransaction(Tx);
        try
        {
            await Put(s,id,"config",ScenarioLoader.Write(scenario),ct); await Put(s,id,"state-0",Wire.Write(w.Capture()),ct);
            await Put(s,id,"initial",Wire.Write(w.Events),ct);
            await runs.InsertOneAsync(s,new BsonDocument { {"_id",id},{"schema","mongo-1"},{"turn",0L},{"revision",0L},{"policy",Wire.Write(policy)},{"speed",1.0},{"bytes",0L},{"pause",BsonNull.Value} },cancellationToken:ct);
            await requests.InsertOneAsync(s,new BsonDocument { {"_id",key},{"signature",signature},{"result",id} },cancellationToken:ct);
            await s.CommitTransactionAsync(ct); return id;
        }
        catch(MongoException) { var found=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(CancellationToken.None); if(found!=null) { StorageGuard.Require(found["signature"]==signature,"请求标识冲突"); return found["result"].AsString; } throw; }
    }
    public async Task<RunStatus> Status(string id,CancellationToken ct=default)
    {
        var r=await Run(id,ct); var config=ScenarioLoader.Read(await Payload(id,"config",ct));
        return new(r["turn"].AsInt64,false,r["speed"].ToDouble(),r["pause"].IsBsonNull?null:r["pause"].AsString,0,config.Limits.MemoryBytes);
    }
    public async Task<RunStatus> Command(string id,string request,string operation,double? speed=null,CancellationToken ct=default)
    {
        StorageGuard.Require(!string.IsNullOrWhiteSpace(request),"请求标识必填");
        string key=Key(id,request), signature=Wire.Write(new {operation,speed});
        var old=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(ct);
        if(old!=null) { StorageGuard.Require(old["signature"]==signature,"请求标识冲突"); return Read<RunStatus>(old["result"].AsString); }
        // Claim an exclusive lease before reading or calculating. Revision and owner are fencing tokens.
        string owner=Guid.NewGuid().ToString("N");
        var leaseFilter=new BsonDocument { {"_id",id},{"$or",new BsonArray {
            new BsonDocument("leaseUntil",new BsonDocument("$exists",false)),
            new BsonDocument("leaseUntil",new BsonDocument("$lt",DateTime.UtcNow)) }} };
        var claimed=await runs.FindOneAndUpdateAsync(leaseFilter,new BsonDocument("$set",new BsonDocument { {"owner",owner},{"leaseUntil",DateTime.UtcNow.AddSeconds(60)} }),new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument=ReturnDocument.After },ct);
        if(claimed is null)
        {
            for(int attempt=0;attempt<100;attempt++)
            {
                old=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(ct);
                if(old!=null) { StorageGuard.Require(old["signature"]==signature,"请求标识冲突"); return Read<RunStatus>(old["result"].AsString); }
                await Task.Delay(20,ct);
            }
            throw new RuleException("运行正由其他写入者持有");
        }
        try
        {
        old=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(ct);
        if(old!=null) { StorageGuard.Require(old["signature"]==signature,"请求标识冲突"); return Read<RunStatus>(old["result"].AsString); }
        var r=await Run(id,ct); long turn=r["turn"].AsInt64, revision=r["revision"].AsInt64;
        var config=ScenarioLoader.Read(await Payload(id,"config",ct)); var policy=Read<PersistencePolicy>(r["policy"].AsString); policy.Validate();
        var state=Read<RecoveryState>(await Payload(id,"state-"+turn,ct));
        string? pause=r["pause"].IsBsonNull?null:r["pause"].AsString;
        RuntimeRecord? record=null; RecoveryState? candidate=null;
        if(operation=="step" && turn<config.Limits.Turns)
        {
            StorageGuard.Require(pause is null,"运行已暂停："+pause);
            var w=World.Restore(config,state); var h=w.AdvanceAutonomously(); var events=w.Events.ToArray();
            record=new(config.Schedule.Where(c=>c.Turn<=turn).OrderBy(c=>c.Turn).LastOrDefault()?.Version??config.ConfigurationVersion,"end-of-turn",h,events,SimulationRun.Metrics(h,events,config.Schedule.Where(c=>c.Turn<=turn).OrderBy(c=>c.Turn).LastOrDefault()?.Version??config.ConfigurationVersion));
            candidate=w.Capture();
        }
        else if(operation=="speed") StorageGuard.Require(speed is >0 and <=1000 && double.IsFinite(speed.Value),"速度非法");
        else StorageGuard.Require(operation is "pause" or "resume" || operation=="step","未知命令或不支持手动干预");
        string? data=record is null?null:Wire.Write(record), recovery=candidate is null?null:Wire.Write(candidate);
        long inflight=Encoding.UTF8.GetByteCount((data??"")+(recovery??""));
        if(inflight>policy.InflightBytes || GC.GetTotalMemory(true)>config.Limits.MemoryBytes || System.Diagnostics.Process.GetCurrentProcess().WorkingSet64>policy.ProcessBytes) { pause="memory-budget"; record=null; candidate=null; }
        if(r["bytes"].AsInt64+inflight>policy.StorageBytes) { pause="storage-budget"; record=null; candidate=null; }
        long end=candidate?.Turn??turn; if(end>=config.Limits.Turns) pause="completed";
        var result=new RunStatus(end,false,operation=="speed"?speed!.Value:r["speed"].ToDouble(),pause,GC.GetTotalMemory(false),config.Limits.MemoryBytes);
        Fault?.Invoke("before");
        using var s=await db.Client.StartSessionAsync(cancellationToken:ct); s.StartTransaction(Tx);
        try
        {
            var changed=await runs.UpdateOneAsync(s,new BsonDocument { {"_id",id},{"revision",revision},{"owner",owner},{"leaseUntil",new BsonDocument("$gt",DateTime.UtcNow)} },new BsonDocument { {"$set",new BsonDocument { {"turn",end},{"speed",result.Speed},{"pause",pause is null?BsonNull.Value:new BsonString(pause)} }},{"$inc",new BsonDocument { {"revision",1L},{"bytes",candidate is null?0L:inflight} }} },cancellationToken:ct);
            StorageGuard.Require(changed.ModifiedCount==1,"运行写入冲突，请查询原请求后重试");
            if(candidate!=null)
            {
                await Put(s,id,"record-"+turn,data!,ct); await Put(s,id,"state-"+end,recovery!,ct);
                // Recovery boundaries remain immutable and queryable.
                var subjects=record!.History.Decisions.Select(d=>d.Subject).Concat(record.History.Input.Orders.SelectMany(o=>new[]{o.Id,o.Entity})).Concat(record.History.Settlement.Facts.Select(f=>f.Subject)).Distinct();
                await turns.InsertOneAsync(s,new BsonDocument { {"run",id},{"turn",turn},{"subjects",new BsonArray(subjects)},{"items",new BsonArray(config.ItemTypes.SelectMany(t=>new[]{t.Id,t.Tag}).Distinct())},{"markets",new BsonArray(config.Markets.SelectMany(m=>new[]{m.Region,m.Region+":"+m.Tag}).Distinct())} },cancellationToken:ct);
            }
            await requests.InsertOneAsync(s,new BsonDocument { {"_id",key},{"signature",signature},{"result",Wire.Write(result)} },cancellationToken:ct);
            Fault?.Invoke("during"); await s.CommitTransactionAsync(ct); Fault?.Invoke("after"); return result;
        }
        catch
        {
            if(s.IsInTransaction) try { await s.AbortTransactionAsync(CancellationToken.None); } catch(MongoException) { }
            var found=await requests.Find(new BsonDocument("_id",key)).FirstOrDefaultAsync(CancellationToken.None);
            if(found!=null) { StorageGuard.Require(found["signature"]==signature,"请求标识冲突"); return Read<RunStatus>(found["result"].AsString); }
            throw;
        }
        }
        finally
        {
            await runs.UpdateOneAsync(new BsonDocument { {"_id",id},{"owner",owner} },new BsonDocument("$unset",new BsonDocument { {"owner",1},{"leaseUntil",1} }),cancellationToken:CancellationToken.None);
        }
    }
    public async Task<string> Snapshot(string id,long? turn=null,CancellationToken ct=default)
    {
        var r=await Run(id,ct); long committed=r["turn"].AsInt64; long target=turn??committed;
        StorageGuard.Require(target>=0 && target<=committed,"回合不存在");
        if(target==committed) { var config=ScenarioLoader.Read(await Payload(id,"config",ct)); return Wire.Write(World.Restore(config,Read<RecoveryState>(await Payload(id,"state-"+target,ct))).Snapshot()); }
        return Wire.Write(Read<RuntimeRecord>(await Payload(id,"record-"+target,ct)).History.Settlement.Before);
    }
    public async Task<string> History(string id,long after=-1,int limit=20,string? subject=null,string? item=null,string? market=null,CancellationToken ct=default)
    {
        await Run(id,ct); StorageGuard.Require(limit is >0 and <=100,"分页大小须1至100");
        var filter=new BsonDocument { {"run",id},{"turn",new BsonDocument("$gt",after)} };
        if(subject!=null) filter.Add("subjects",subject); if(item!=null) filter.Add("items",item); if(market!=null) filter.Add("markets",market);
        var page=await turns.Find(filter).Sort(new BsonDocument("turn",1)).Limit(limit).ToListAsync(ct);
        var records=new List<RuntimeRecord>(); foreach(var t in page) records.Add(Read<RuntimeRecord>(await Payload(id,"record-"+t["turn"].AsInt64,ct)));
        return Wire.Write(new {Records=records,Next=page.Count==0?after:page[^1]["turn"].AsInt64});
    }
    public async Task Export(string id,Stream output,CancellationToken ct=default)
    {
        var r=await Run(id,ct); long end=r["turn"].AsInt64;
        async Task Line(string line) { await output.WriteAsync(Encoding.UTF8.GetBytes(line+"\n"),ct); await output.FlushAsync(ct); }
        await Line(Wire.Write(new {Schema="runtime-2-ndjson",Configuration=ScenarioLoader.Read(await Payload(id,"config",ct)),Turn=end,Phase="end-of-turn"}));
        await Line(await Payload(id,"initial",ct));
        for(long t=0;t<end;t++) { ct.ThrowIfCancellationRequested(); await Line(await Payload(id,"record-"+t,ct)); }
    }
}



internal static class StorageGuard
{
    public static void Require(bool condition,string reason) { if(!condition) throw new RuleException(reason); }
}
