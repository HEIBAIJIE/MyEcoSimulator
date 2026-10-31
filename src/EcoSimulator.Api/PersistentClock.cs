using EcoSimulator.Core;
namespace EcoSimulator.Api;
// Scheduling is bounded per instance; the database revision fences overlapping instances.
public sealed class PersistentClock(MongoRunStore store):BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(double Speed,DateTime Next)> active=new();
    public void Pause(string id)=>active.TryRemove(id,out _);
    public void Resume(string id,double speed)
    {
        if(active.Count>=32 && !active.ContainsKey(id)) throw new RuleException("调度运行上限32");
        active[id]=(speed,DateTime.UtcNow);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try { while(await timer.WaitForNextTickAsync(stoppingToken)) foreach(var entry in active.ToArray())
        {
            if(entry.Value.Next>DateTime.UtcNow) continue;
            try
            {
                var result=await store.Command(entry.Key,"clock-"+Guid.NewGuid().ToString("N"),"step",ct:stoppingToken);
                if(result.PauseReason!=null) Pause(entry.Key);
                else active.TryUpdate(entry.Key,(entry.Value.Speed,DateTime.UtcNow.AddSeconds(1/entry.Value.Speed)),entry.Value);
            }
            catch(Exception e) when(e is not OperationCanceledException) { Pause(entry.Key); Console.Error.WriteLine("持久化调度暂停: "+e.GetType().Name); }
        } } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
}
