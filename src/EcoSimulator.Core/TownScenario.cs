namespace EcoSimulator.Core;

public static class TownScenario
{
    private static Scenario CreateBase()
    {
        var s=Baseline.Create();
        var cells=new List<TerrainCell>();
        for(long y=0;y<30;y++) for(long x=0;x<30;x++)
            if(x>=19+(long)Math.Floor(Math.Sin(y*.24)*2) && x<=22+(long)Math.Floor(Math.Sin(y*.24)*2)) cells.Add(new(new("R0",x,y),"water"));
        var surfaces=new List<SurfaceEntity>();
        var buildings=new List<Building>();
        void Building(string id,long x,long y,long w,long h,string owner)
        {
            var parts=new List<string>();
            for(long dx=0;dx<w;dx++) for(long dy=0;dy<h;dy++) { var part=id+"-"+dx+"-"+dy; parts.Add(part); surfaces.Add(new(part,"wall",new("R0",x+dx,y+dy),owner)); }
            buildings.Add(new(id,new("R0",x,y),w,h,parts.ToArray(),owner,0));
        }
        Building("workshop",0,0,2,2,"F");
        Building("town-hall",7,3,2,2,"S");
        Building("storehouse",3,8,2,2,"S");
        for(long x=1;x<18;x++) for(long y=1;y<22;y++)
            if((x*17+y*31)%19==0 && !surfaces.Any(e=>e.Tile.X==x&&e.Tile.Y==y) && (x>10||y>12)) surfaces.Add(new("tree-"+x+"-"+y,"tree",new("R0",x,y),null));
        return s with { Name="河湾镇", ConfigurationVersion="town-2",EnabledMechanisms=["space-1","environment-1"],Space=new("space-1",2),Environment=new("environment-1",["flat","water"],cells.ToArray(),[new("wall",["flat"],true),new("tree",["flat"],false)],surfaces.ToArray(),buildings.ToArray()) };
    }
    public static Scenario Create()
    {
        var s=CreateBase(); var env=s.Environment!;
        var parts=new List<SurfaceEntity>();
        for(int x=8;x<10;x++) for(int y=8;y<10;y++) parts.Add(new($"rest-house-{x}-{y}","wall",new("R0",x,y),"S"));
        return s with { ConfigurationVersion="town-2",Strategy=s.Strategy with { Version="needs-1" },Tasks=[],
            Decisions=new("needs-1","food",50,80,30,80,[new("F",Enumerable.Range(8,9).ToArray())]),
            Entities=s.Entities.Select((e,i)=>e.Kind!=EntityKind.Person?e:e with { Location=new("R0",4+i%2,4+i%4/2),Satiety=58+8*(i%4),Energy=38+16*(i%4) }).ToArray(),
            Environment=env with { Surfaces=env.Surfaces.Concat(parts).ToArray(),Buildings=env.Buildings.Append(new("rest-house",new("R0",8,8),2,2,parts.Select(p=>p.Id).ToArray(),"S",0)).ToArray() } };
    }
}
