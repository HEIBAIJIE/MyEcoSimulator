using EcoSimulator.Core;
using MongoDB.Bson;
using MongoDB.Driver;
namespace EcoSimulator.Core.Tests;
public sealed class PersistenceTests
{
    const string Connection="mongodb://localhost:27018/?directConnection=true";
    [Fact] public async Task RealReplicaSet()
    {
        var client=new MongoClient(Connection); var admin=client.GetDatabase("admin");
        try { await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate",new BsonDocument { {"_id","eco-rs"},{"members",new BsonArray {new BsonDocument { {"_id",0},{"host","localhost:27018"} }}} })); } catch(MongoCommandException e) when(e.CodeName=="AlreadyInitialized") { }
        for(int i=0;i<100;i++) { var h=await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello",1)); if(h.GetValue("isWritablePrimary",false).AsBoolean) break; await Task.Delay(100); }
        string database="eco_test_"+Guid.NewGuid().ToString("N");
        var store=new MongoRunStore(Connection,database); await store.Initialize();
        var scenario=Baseline.Create(); var id=await store.Create("create",scenario,PersistencePolicy.Local);
        Assert.Equal(id,await store.Create("create",scenario,PersistencePolicy.Local));
        await Assert.ThrowsAsync<RuleException>(()=>store.Create("create",Variants.Create("demand"),PersistencePolicy.Local));
        await store.Command(id,"one","step");
        var duplicates=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>new MongoRunStore(Connection,database).Command(id,"parallel","step")));
        Assert.All(duplicates,r=>Assert.Equal(2,r.Turn));
        store.Fault=stage=> { if(stage=="during") throw new IOException("injected abort"); };
        await Assert.ThrowsAsync<IOException>(()=>store.Command(id,"two","step")); Assert.Equal(2,(await store.Status(id)).Turn);
        store.Fault=stage=> { if(stage=="after") throw new IOException("lost response"); };
        Assert.Equal(3,(await store.Command(id,"two","step")).Turn);
        store=new MongoRunStore(Connection,database); await store.Initialize();
        Assert.Equal(3,(await store.Command(id,"two","step")).Turn);
        Assert.False((await store.Status(id)).Running);
        var expected=await store.Snapshot(id); Assert.Equal(expected,await new MongoRunStore(Connection,database).Snapshot(id));
        for(int i=3;i<28;i++) await store.Command(id,"s"+i,"step");
        var page=await store.History(id,-1,3,"P00","food","R0:food"); Assert.Contains("Records",page);
        using var export=new MemoryStream(); await store.Export(id,export); Assert.Equal(30,System.Text.Encoding.UTF8.GetString(export.ToArray()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
        await client.DropDatabaseAsync(database);
    }
}
