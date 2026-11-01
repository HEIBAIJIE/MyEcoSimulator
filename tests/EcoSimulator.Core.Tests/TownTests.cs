using EcoSimulator.Core;
namespace EcoSimulator.Core.Tests;
public class TownTests
{
 [Fact] public void TownRoundTripRecoveryAndConservation()
 {
  var s=ScenarioLoader.Read(ScenarioLoader.Write(TownScenario.Create()));var w=World.Load(s);
  Assert.Equal(4,w.Snapshot().Environment!.Buildings.Length);
  Assert.Contains(w.Snapshot().Environment!.Cells,c=>c.Terrain=="water");
  for(var i=0;i<48;i++) w.AdvanceAutonomously();
  Assert.Null(w.Failure);Assert.Equal(400000,w.Snapshot().Accounts.Values.Sum(a=>a.Cash));
  var restored=World.Restore(s,w.Capture());Assert.Equal(48,restored.Turn);
  Assert.Equal("town-2",restored.Snapshot().Presentation!.Version);
  Assert.Equal(20,restored.Snapshot().Presentation!.Objects.Count(o=>o.Appearance=="Person"));
 }
}
