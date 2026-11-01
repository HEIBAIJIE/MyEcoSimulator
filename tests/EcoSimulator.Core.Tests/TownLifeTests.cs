using EcoSimulator.Core;
using System.Text.Json;
namespace EcoSimulator.Core.Tests;
public class TownLifeTests
{
 [Fact] public void ConfigurationVersionsAndValidation()
 {
  var s=TownScenario.Create();ScenarioLoader.Validate(s);Assert.Empty(s.Tasks);
  Assert.Equal("town-2",Variants.Create("town").ConfigurationVersion);
  Assert.Equal("needs-1",ScenarioLoader.Read(ScenarioLoader.Write(s)).Decisions!.Version);
  Assert.Throws<RuleException>(()=>ScenarioLoader.Validate(s with { Decisions=s.Decisions! with { WorkWindows=[] } }));
  Assert.Throws<RuleException>(()=>ScenarioLoader.Validate(s with { Decisions=s.Decisions! with { EatThreshold=100 } }));

 }
 [Fact] public void PrioritiesAndRestContinuation()
 {
  var s=TownScenario.Create();var w=World.Load(s);var v=w.View("P00");var strategy=new NeedsPersonStrategy(s.Decisions!,s.ItemTypes);
  PersonalDecision Decide(long sat,long energy,long turn,PersonalMemory? m=null)=>strategy.Decide(v with { Turn=turn,Self=v.Self with { Satiety=sat,Energy=energy } },m??new(),s.World,s.Strategy);
  Assert.Equal(ActionKind.Eat,Decide(20,0,21).Intent.Action.Kind);
  Assert.Equal(ActionKind.Rest,Decide(60,12,12).Intent.Action.Kind);
  Assert.Null(Decide(60,12,12).Order);
  Assert.Equal(ActionKind.Eat,Decide(50,80,7).Intent.Action.Kind);
  Assert.Equal(Decide(60,40,7).Intent.Action.Kind,Decide(60,40,21).Intent.Action.Kind);
  var rest=Decide(60,30,10);Assert.True(rest.Memory.Resting);
  Assert.True(Decide(60,60,11,rest.Memory).Memory.Resting);
  Assert.False(Decide(60,80,12,rest.Memory).Memory.Resting);
  Assert.Equal(v.Self.Location,Decide(80,80,10).Intent.Destination);

 }
 [Fact] public void First48HoursConserveAndRecover()
 {
  var s=TownScenario.Create();var w=World.Load(s);bool mixed=false;var moved=new HashSet<string>();
  for(int i=0;i<48;i++)
  {
   var h=w.AdvanceAutonomously();mixed|=h.Input.People.Select(p=>p.Action.Kind).Distinct().Count()>1;
   foreach(var e in h.Settlement.After.Entities.Where(e=>e.Kind==EntityKind.Person))if(e.Location!=h.Settlement.Before.Entities.Single(p=>p.Id==e.Id).Location)moved.Add(e.Id);
   Assert.All(h.PublishedTasks,t=>Assert.InRange(t.EffectiveTurn%24,8,16));
   Assert.Equal(s.Issuance,w.Snapshot().Accounts.Values.Sum(a=>a.Cash));Assert.All(w.Snapshot().Accounts.Values,a=>Assert.True(a.Balanced));
   var state=w.Capture();var restored=World.Restore(s,JsonSerializer.Deserialize<RecoveryState>(JsonSerializer.Serialize(state))!);
   Assert.Equal(JsonSerializer.Serialize(state.People),JsonSerializer.Serialize(restored.Capture().People));
   w=restored;
  }
  Assert.True(mixed);Assert.True(moved.Count>=2);Assert.Null(w.Failure);
 }
 [Fact] public void RemoteGroundFoodCannotBeConsumed()
 {
  var s=TownScenario.Create();s=s with { Cash=s.Cash.Select(c=>c.Entity=="P00"?c with { Amount=0 }:c).ToArray(),Issuance=s.Issuance-10000,Entities=s.Entities.Select(e=>e.Id=="P00"?e with { Satiety=10 }:e).ToArray(),Lots=s.Lots.Select(l=>l.Owner=="P00"?l with { Location=new(new("R0",15,15),null),Custodian=null }:l).ToArray() };
  var world=World.Load(s);
  var intent=new PersonIntent("P00",new("R0",4,4),new(ActionKind.Eat,Type:"food",Maximum:4,Conditional:true),[],"Remote inventory check");
  var result=world.Advance(new([intent],[]));
  Assert.Contains(result.Results,r=>r.Subject=="P00"&&!r.Success);
  Assert.DoesNotContain(result.Results,r=>r.Subject=="P00"&&r.Kind=="action"&&r.Success&&r.Quantity>0);

 }
}
