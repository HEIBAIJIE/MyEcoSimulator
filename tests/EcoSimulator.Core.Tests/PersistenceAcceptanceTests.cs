using System.Diagnostics;
using System.Text.Json;
using EcoSimulator.Core;
using MongoDB.Bson;
using MongoDB.Driver;
namespace EcoSimulator.Core.Tests;
public sealed class PersistenceAcceptanceTests
{
    [Theory]
    [InlineData("baseline")][InlineData("no-resident-cash")][InlineData("low-capital")][InlineData("productivity")][InlineData("demand")][InlineData("pause")][InlineData("production")][InlineData("organizations")]
    public async Task FullRun(string variant)
    {
        const string connection="mongodb://localhost:27018/?directConnection=true";
        var database="eco_accept_"+Guid.NewGuid().ToString("N"); var store=new MongoRunStore(connection,database); await store.Initialize();
        var original=Variants.Create(variant); var s=original with { Limits=original.Limits with { MemoryBytes=2L*1024*1024*1024 } }; var id=await store.Create("create",s,PersistencePolicy.Local);
        var timer=Stopwatch.StartNew(); long at240=0;
        for(int i=0;i<720;i++)
        {
            var result=await store.Command(id,"step-"+i,"step"); Assert.Equal(i+1,result.Turn);
            if(i==239) { at240=GC.GetTotalMemory(true); var before=await store.Snapshot(id); store=new MongoRunStore(connection,database); Assert.Equal(before,await store.Snapshot(id)); }
        }
        var elapsed=timer.ElapsedMilliseconds; timer.Restart(); var restored=await new MongoRunStore(connection,database).Snapshot(id); var restoreMs=timer.ElapsedMilliseconds;
        for(long after=-1;after<719;)
        {
            using var page=JsonDocument.Parse(await store.History(id,after,20));
            foreach(var r in page.RootElement.GetProperty("Records").EnumerateArray())
            {
                var m=r.GetProperty("Metrics"); long N(string key)=>long.Parse(m.GetProperty(key).GetString()!);
                Assert.Equal(s.Issuance,N("ResidentCash")+N("CompanyCash")+N("CountryCash"));
                var h=r.GetProperty("History").GetProperty("Settlement");
                long Cash(string snapshot)=>h.GetProperty(snapshot).GetProperty("Accounts").EnumerateObject().Where(p=>p.Name.StartsWith("P")).Sum(p=>long.Parse(p.Value.GetProperty("Cash").GetString()!));
                Assert.Equal(N("Wages")-N("FoodSpending")-N("Confiscation"),Cash("After")-Cash("Before"));
                foreach(var account in h.GetProperty("After").GetProperty("Accounts").EnumerateObject()) Assert.True(account.Value.GetProperty("Balanced").GetBoolean());
            }
            after=long.Parse(page.RootElement.GetProperty("Next").GetString()!);
        }
        timer.Restart(); await store.History(id,600,20); long queryMs=timer.ElapsedMilliseconds;
        var stats=await new MongoClient(connection).GetDatabase(database).RunCommandAsync<BsonDocument>(new BsonDocument("dbStats",1));
        var report=$"{variant}: turns=720 runMs={elapsed} restoreMs={restoreMs} pageMs={queryMs} managed240={at240} managed720={GC.GetTotalMemory(true)} peak={Process.GetCurrentProcess().PeakWorkingSet64} db={stats["storageSize"]}";
        Directory.CreateDirectory("../../../../../.local"); await File.AppendAllTextAsync("../../../../../.local/acceptance.txt",report+Environment.NewLine);
        await new MongoClient(connection).DropDatabaseAsync(database);
    }
}

