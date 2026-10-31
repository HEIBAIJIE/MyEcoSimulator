namespace EcoSimulator.Core;
public static class Baseline
{
    public static Scenario Create()
    {
        var tile = new Tile("R0",0,0); var people = Enumerable.Range(0,20).Select(i => $"P{i:00}").ToArray();
        return new("基准","0.3","baseline-1",new("0.3",100,2,4,20,5,5,60,1),new("baseline-1",24,1,40,24),new(720,720,1,268435456),[],[new("R0",0,0,1)],"flat",
            [new("S",EntityKind.Country,tile,0,0,0),new("F",EntityKind.Company,tile,0,0,0),..people.Select(p => new Entity(p,EntityKind.Person,tile,100,100,100))],
            people.SelectMany(p => new[] {new Membership(p,"S"),new Membership(p,"F")}).ToArray(),
            people.Select(p => new Employee(p,"F","food-worker")).ToArray(),[],[new("P00","F",200000)],
            people.Select(p => new Authorization(p,"F","labor")).ToArray(),[new("S",tile),new("F",tile)],
            [new("food","food","piece",20,1,true,null)],[new("food-work","1","F","food-worker","food",2,100,1)],
            people.Select(p => new WorkTask("task-"+p,"F",p,"food-work","1",tile,0,100,2)).ToArray(),
            [..people.Select((p,i) => new Lot("stock-"+p,"opening-"+p,i,4,400,0,4,"food",p,new(null,p),p)),new("stock-F","opening-F",20,100,10000,0,100,"food","F",new(tile,null),"F")],
            [new("R0","food",100,100,null)],"S",400000,[new("S",0),new("F",200000),..people.Select(p => new CashAllocation(p,10000))],"confiscate");
    }
}
