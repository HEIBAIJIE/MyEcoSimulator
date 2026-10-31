using System.Collections.Concurrent;
using EcoSimulator.Core;
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddHostedService<RunClock>();
var app=builder.Build();
app.Use(async (context,next)=> { try { await next(context); } catch(Exception e) when(e is RuleException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException) { context.Response.StatusCode=400; await context.Response.WriteAsJsonAsync(new { error=e.Message }); } });
IResult Json(string json)=>Results.Text(json,"application/json");
app.MapPost("/runs",(CreateRun request,RunRegistry registry)=>Json(Wire.Write(new { Id=registry.Create(request) })));
app.MapGet("/runs/{id}",(string id,RunRegistry registry)=>Json(Wire.Write(registry.Get(id).Status)));
app.MapPost("/runs/{id}/commands",(string id,RunCommand request,RunRegistry registry)=>Json(Wire.Write(registry.Get(id).Command(request.RequestId,request.Operation,request.Speed))));
app.MapGet("/runs/{id}/snapshot",(string id,long? turn,RunRegistry registry)=>Json(registry.Get(id).Snapshot(turn)));
app.MapGet("/runs/{id}/history",(string id,long? from,long? to,string? subject,string? item,string? market,RunRegistry registry)=>Json(registry.Get(id).History(from??0,to??long.MaxValue,subject,item,market)));
app.MapGet("/runs/{id}/daily",(string id,RunRegistry registry)=>Json(registry.Get(id).Daily()));
app.MapGet("/runs/{id}/export",(string id,RunRegistry registry)=>Json(registry.Get(id).Export()));
app.Run();
public sealed record CreateRun(string RequestId,string? Variant,string? Configuration);
public sealed record RunCommand(string RequestId,string Operation,double? Speed);
public sealed class RunRegistry
{
    private readonly object gate=new();
    private readonly Dictionary<string,(CreateRun Request,string Id)> creations=[];
    private readonly ConcurrentDictionary<string,SimulationRun> runs=new();
    public string Create(CreateRun request) { lock(gate) {
        if(string.IsNullOrWhiteSpace(request.RequestId)) throw new RuleException("请求标识必填");
        if(creations.TryGetValue(request.RequestId,out var old)) { if(old.Request!=request) throw new RuleException("请求标识冲突"); return old.Id; }
        if((request.Configuration is null)==(request.Variant is null)) throw new RuleException("明确提供variant或configuration之一");
        var run=new SimulationRun(request.Configuration is null ? Variants.Create(request.Variant!) : ScenarioLoader.Read(request.Configuration));
        string id=Guid.NewGuid().ToString("N"); runs[id]=run; creations.Add(request.RequestId,(request,id)); return id;
    } }
    public SimulationRun Get(string id)=>runs.TryGetValue(id,out var run)?run:throw new RuleException("运行不存在");
    public void Tick() { foreach(var run in runs.Values) run.Tick(DateTime.UtcNow); }
}
public sealed class RunClock(RunRegistry registry):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellation) { using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(10)); try { while(await timer.WaitForNextTickAsync(cancellation)) registry.Tick(); } catch(OperationCanceledException) when(cancellation.IsCancellationRequested) {} }
}
