using System.Text.Json;
using System.Diagnostics;
using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public sealed class RuntimeTests
{
    [Fact] public void CommandsSerializeDeduplicateAndPause()
    {
        var r=new SimulationRun(Baseline.Create());
        Parallel.For(0,20,_=>r.Command("same","step")); Assert.Equal(1,r.Status.Turn);
        Assert.Throws<RuleException>(()=>r.Command("same","pause"));
        r.Command("go","resume"); r.Command("stop","pause"); r.Tick(DateTime.MaxValue); Assert.Equal(1,r.Status.Turn);
        Assert.Throws<RuleException>(()=>r.Command("manual","intervene"));
        var small=new SimulationRun(Baseline.Create() with { Limits=new(720,720,1,100000) });
        small.Command("one","step"); Assert.Equal(0,small.Status.Turn); Assert.Contains("memory-budget",small.Export());
    }
    [Fact] public void ScheduledRecipesKeepAlreadyPublishedCommitments()
    {
        var s=Baseline.Create() with { Schedule=[new(0,"new-output",1,null,null)] };
        var w=World.Load(s); var first=w.AdvanceAutonomously();
        Assert.Equal(40,first.ActualProduction);
        Assert.All(first.PublishedTasks,t=> { Assert.Equal(1,t.Output); Assert.Equal("new-output",t.RecipeVersion); });
        Assert.Equal(2000,w.Events.Where(e=>e.Stage=="7").SelectMany(e=>e.Entries).Where(e=>e.Entity=="F" && e.Account==Account.Inventory).Sum(e=>e.Debit));
    }
    [Fact] public void WireIsLosslessAndConfigurationRoundTrips()
    {
        var s=Baseline.Create(); s=s with { Issuance=9007199254740993,Cash=s.Cash.Select(c=>c.Entity=="S"?c with {Amount=9007199254340993}:c).ToArray() };
        var json=Wire.Write(s); Assert.Contains("\"9007199254740993\"",json);
        var copy=JsonSerializer.Deserialize<Scenario>(json,Wire.Options)!; World.Load(copy);
        Assert.Throws<RuleException>(()=>ScenarioLoader.Read(ScenarioLoader.Write(s with {RuleVersion="future"})));
    }
    [Theory] [InlineData("baseline")] [InlineData("no-resident-cash")] [InlineData("low-capital")] [InlineData("productivity")] [InlineData("demand")] [InlineData("pause")]
    public void FullHistoryAndVariants(string variant)
    {
        var watch=Stopwatch.StartNew(); var s=Variants.Create(variant); var r=new SimulationRun(s);
        for(int i=0;i<720;i++) r.Command("step-"+i,"step");
        Assert.Equal(720,r.Status.Turn);
        using var document=JsonDocument.Parse(r.History()); var turns=document.RootElement.EnumerateArray().ToArray(); Assert.Equal(720,turns.Length);
        foreach(var t in turns)
        {
            var metric=t.GetProperty("Metrics"); long N(string key)=>long.Parse(metric.GetProperty(key).GetString()!);
            Assert.Equal(s.Issuance,N("ResidentCash")+N("CompanyCash")+N("CountryCash"));
            var history=t.GetProperty("History"); var settlement=history.GetProperty("Settlement");
            long Cash(JsonElement snap)=>snap.GetProperty("Accounts").EnumerateObject().Where(p=>p.Name.StartsWith("P")).Sum(p=>long.Parse(p.Value.GetProperty("Cash").GetString()!));
            Assert.Equal(N("Wages")-N("FoodSpending")-N("Confiscation"),Cash(settlement.GetProperty("After"))-Cash(settlement.GetProperty("Before")));
        }
        if(variant=="low-capital") { var m=turns[0].GetProperty("Metrics"); Assert.Equal("0",m.GetProperty("Wages").GetString()); Assert.Equal(20,turns[0].GetProperty("Metrics").GetProperty("StopReasons").GetArrayLength() > 0 ? 20 : 0); }
        if(variant=="no-resident-cash") Assert.Empty(turns[0].GetProperty("History").GetProperty("Input").GetProperty("Orders").EnumerateArray().Where(o=>o.GetProperty("Side").GetString()=="Buy"));
        if(variant=="demand") Assert.Contains("\\u0022demand\\u0022:5",r.History(240,240));
        if(variant=="productivity") {
            foreach(var turn in turns.Skip(240)) foreach(var task in turn.GetProperty("History").GetProperty("PublishedTasks").EnumerateArray()) { Assert.Equal("1",task.GetProperty("Output").GetString()); Assert.Equal("productivity-1",task.GetProperty("RecipeVersion").GetString()); }
        }
        if(variant=="pause") foreach(var turn in turns.Skip(240).Take(24)) Assert.Equal(0,turn.GetProperty("History").GetProperty("PublishedTasks").GetArrayLength());
        Assert.Contains("end-of-turn",r.Export()); Assert.NotEmpty(r.Daily()); Assert.NotEmpty(r.Snapshot(720));
        Console.WriteLine($"{variant}: {watch.ElapsedMilliseconds}ms, retained={r.Status.RetainedBytes}, peak={Process.GetCurrentProcess().PeakWorkingSet64}, cpu={Environment.ProcessorCount}");
    }
}
