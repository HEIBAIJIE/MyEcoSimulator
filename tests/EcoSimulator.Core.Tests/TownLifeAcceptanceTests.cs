using EcoSimulator.Core;
using System.Text.Json;
namespace EcoSimulator.Core.Tests;
public class TownLifeAcceptanceTests
{
 [Fact] public async Task Mongo720AndExport()
 {
  var store=new MongoRunStore("mongodb://localhost:27018/?directConnection=true","eco_f12_acceptance");await store.Initialize();
  var s=TownScenario.Create();var id=await store.Create("f12-"+Guid.NewGuid(),s,PersistencePolicy.Local);
  Directory.CreateDirectory("../../../../../artifacts/f12");
  await File.WriteAllTextAsync("../../../../../artifacts/f12/run-id.txt",id);
  for(int i=0;i<720;i++){Assert.Equal(i+1,(await store.Command(id,"step-"+i,"step")).Turn);if(i==23){var snap=await store.Snapshot(id);store=new MongoRunStore("mongodb://localhost:27018/?directConnection=true","eco_f12_acceptance");Assert.Equal(snap,await store.Snapshot(id));}}
  await using(var output=File.Create("../../../../../artifacts/f12/town-2.ndjson"))await store.Export(id,output);
  Assert.Equal(720,(await store.Status(id)).Turn);
 }
}
