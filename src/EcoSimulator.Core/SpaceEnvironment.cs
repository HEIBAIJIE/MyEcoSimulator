namespace EcoSimulator.Core;

public sealed record ConstructionOutput(string SurfaceType, long Width, long Height, bool Building, long RestBonus);
public sealed record SpatialConfiguration(string Version, long HaulTurns);
public sealed record TransportProgress(string Person, ActionKind Kind, Tile Origin, Tile Target, string? Lot, long Started, long Completed, long Required, string State);
public sealed record TerrainCell(Tile Tile, string Terrain);
public sealed record SurfaceType(string Id, string[] Terrains, bool Artificial);
public sealed record SurfaceEntity(string Id, string Type, Tile Tile, string? Owner);
public sealed record Building(string Id, Tile Anchor, long Width, long Height, string[] Components, string? Owner, long RestBonus);
public sealed record SiteCondition(string? Terrain, string? Surface, string? Building);
public sealed record EnvironmentConfiguration(string Version, string[] Terrains, TerrainCell[] Cells, SurfaceType[] SurfaceTypes, SurfaceEntity[] Surfaces, Building[] Buildings);

public static class EnvironmentRules
{
    public static string Terrain(Scenario s, Tile tile)=>s.Environment?.Cells.SingleOrDefault(c=>c.Tile==tile)?.Terrain??s.Terrain;
    public static Tile Normalize(Scenario s, Tile tile)
    {
        var b=s.Environment?.Buildings.SingleOrDefault(b=>b.Anchor.Region==tile.Region && tile.X>=b.Anchor.X && tile.Y>=b.Anchor.Y && tile.X-b.Anchor.X<b.Width && tile.Y-b.Anchor.Y<b.Height);
        return b?.Anchor??tile;
    }
    public static long RestBonus(Scenario s,Tile tile)=>s.Environment?.Buildings.SingleOrDefault(b=>b.Anchor==tile)?.RestBonus??0;
    public static bool Matches(Scenario s,SiteCondition? condition,Tile tile)=>condition is null ||
        (condition.Terrain is null || Terrain(s,tile)==condition.Terrain) &&
        (condition.Surface is null || s.Environment!.Surfaces.Any(e=>e.Tile==tile && e.Type==condition.Surface)) &&
        (condition.Building is null || s.Environment!.Buildings.Any(b=>b.Id==condition.Building && b.Anchor==tile));
    public static void Validate(Scenario s)
    {
        void R(bool b,string message)=>ScenarioLoader.Require(b,message);
        if(!s.EnabledMechanisms.Contains("environment-1")) { R(s.Environment is null && s.Recipes.All(r=>r.Site is null && r.Construction is null),"Disabled environment configuration"); return; }
        var e=s.Environment; R(e is not null && e.Version=="environment-1" && s.EnabledMechanisms.Contains("space-1"),"Missing environment version or dependency");
        var env=e!;
        foreach(var r in s.Recipes.Where(r=>r.Construction is not null)) { var c=r.Construction!; R(r.Site is not null && r.Hours==1 && r.Steps.Length==0 && r.Output==1 && c.Width>0 && c.Height>0 && c.RestBonus>=0 && (c.Building || c.Width==1 && c.Height==1 && c.RestBonus==0) && env.SurfaceTypes.Any(t=>t.Id==c.SurfaceType && (!c.Building || t.Artificial)),"Invalid complete construction step"); }
        R(env.Terrains.Length>0 && env.Terrains.Distinct().Count()==env.Terrains.Length && env.Terrains.Contains(s.Terrain),"Invalid terrain definitions");
        void Check(Tile t)=>s.Regions.Single(r=>r.Id==t.Region).Validate(t);
        bool Owner(string? owner)=>owner is null || s.Entities.Any(e=>e.Id==owner);
        R(env.Cells.Select(c=>c.Tile).Distinct().Count()==env.Cells.Length,"Duplicate terrain cell");
        foreach(var c in env.Cells) { Check(c.Tile); R(env.Terrains.Contains(c.Terrain),"Unknown terrain"); }
        R(env.SurfaceTypes.Select(t=>t.Id).Distinct().Count()==env.SurfaceTypes.Length,"Duplicate surface type");
        foreach(var t in env.SurfaceTypes) R(t.Terrains.Length>0 && t.Terrains.All(env.Terrains.Contains),"Invalid surface terrain definition");
        R(env.Surfaces.Select(e=>e.Id).Distinct().Count()==env.Surfaces.Length && env.Surfaces.Select(e=>e.Tile).Distinct().Count()==env.Surfaces.Length,"Duplicate surface entity or occupied tile");
        foreach(var v in env.Surfaces) { Check(v.Tile); R(Owner(v.Owner) && env.SurfaceTypes.Any(t=>t.Id==v.Type && t.Terrains.Contains(Terrain(s,v.Tile))),"Invalid surface owner or terrain"); }
        R(env.Buildings.Select(b=>b.Id).Distinct().Count()==env.Buildings.Length,"Duplicate building");
        var used=new HashSet<string>();
        foreach(var b in env.Buildings)
        {
            Check(b.Anchor); R(b.Width>0 && b.Height>0 && b.RestBonus>=0 && Owner(b.Owner),"Invalid building bounds or owner");
            Check(new(b.Anchor.Region,checked(b.Anchor.X+b.Width-1),checked(b.Anchor.Y+b.Height-1)));
            R(b.Components.LongLength==checked(b.Width*b.Height) && b.Components.Distinct().Count()==b.Components.Length,"Building must fill rectangle");
            foreach(var id in b.Components) { var v=env.Surfaces.SingleOrDefault(v=>v.Id==id); R(v is not null && used.Add(id) && env.SurfaceTypes.Single(t=>t.Id==v.Type).Artificial && v.Tile.Region==b.Anchor.Region && v.Tile.X>=b.Anchor.X && v.Tile.X-b.Anchor.X<b.Width && v.Tile.Y>=b.Anchor.Y && v.Tile.Y-b.Anchor.Y<b.Height,"Invalid building component or overlap"); }
        }
        foreach(var r in s.Recipes.Where(r=>r.Site is not null)) { var c=r.Site!; R((c.Terrain is null || env.Terrains.Contains(c.Terrain)) && (c.Surface is null || env.SurfaceTypes.Any(t=>t.Id==c.Surface)) && (c.Building is null || env.Buildings.Any(b=>b.Id==c.Building)),"Unknown production site condition"); }
        foreach(var t in s.Entities.Select(e=>e.Location).Concat(s.Warehouses.Select(w=>w.Location)).Concat(s.Tasks.Select(t=>t.Location)).Concat(s.Lots.Where(l=>l.Location.Tile is not null).Select(l=>l.Location.Tile!.Value))) R(Normalize(s,t)==t,"Building occupants and warehouses must use anchor");
    }
}

public sealed partial class World
{
    private readonly Dictionary<string,TransportProgress> transport=[];
    private EnvironmentConfiguration? currentEnvironment;
    private Scenario SpatialScenario=>scenario with { Environment=currentEnvironment??scenario.Environment };
    private bool ConstructionValid(WorkTask task)
    {
        var c=scenario.Recipes.Single(r=>r.Id==task.Recipe).Construction;
        if(c is null) return true;
        var env=currentEnvironment??scenario.Environment!;
        bool Inside(Tile t)=>t.Region==task.Location.Region && t.X>=task.Location.X && t.Y>=task.Location.Y && t.X-task.Location.X<c.Width && t.Y-task.Location.Y<c.Height;
        if(c.Building && scenario.Warehouses.Select(w=>w.Location).Concat(Snapshot().Tasks.Select(t=>t.Location)).Any(t=>t!=task.Location && Inside(t))) return false;
        if(c.Building && transport.Values.Any(p=>p.State=="running" && (Inside(p.Origin) || Inside(p.Target)))) return false;
        var type=env.SurfaceTypes.Single(t=>t.Id==c.SurfaceType);
        var region=scenario.Regions.Single(r=>r.Id==task.Location.Region);
        if(c.Width>region.Side*100-task.Location.X || c.Height>region.Side*100-task.Location.Y) return false;
        for(long x=0;x<c.Width;x++) for(long y=0;y<c.Height;y++)
        { var tile=new Tile(task.Location.Region,task.Location.X+x,task.Location.Y+y); if(env.Surfaces.Any(e=>e.Tile==tile) || !type.Terrains.Contains(EnvironmentRules.Terrain(SpatialScenario,tile))) return false; }
        return true;
    }
    private void Construct(WorkTask task)
    {
        var c=scenario.Recipes.Single(r=>r.Id==task.Recipe).Construction!; var env=currentEnvironment??scenario.Environment!;
        var id=Guid.NewGuid().ToString("N"); var surfaces=new List<SurfaceEntity>();
        for(long x=0;x<c.Width;x++) for(long y=0;y<c.Height;y++) surfaces.Add(new(id+"-"+x+"-"+y,c.SurfaceType,new(task.Location.Region,task.Location.X+x,task.Location.Y+y),task.Company));
        currentEnvironment=env with { Surfaces=[..env.Surfaces,..surfaces],Buildings=c.Building?[..env.Buildings,new Building(id,task.Location,c.Width,c.Height,surfaces.Select(e=>e.Id).ToArray(),task.Company,c.RestBonus)]:env.Buildings };
        Tile Normalize(Tile t)=>EnvironmentRules.Normalize(SpatialScenario,t);
        foreach(var p in People.Values.ToArray()) People[p.Id]=p with { Location=Normalize(p.Location) };
        lots=lots.Select(l=>l.Location.Tile is {} t?l with { Location=new(Normalize(t),null) }:l).ToArray();
    }
    private bool TransportValid(string person,MainAction a,Tile destination)
    {
        if(scenario.Space is null || a.Target is not { } target || destination!=People[person].Location || EnvironmentRules.Normalize(scenario,target)!=target) return false;
        var region=scenario.Regions.SingleOrDefault(r=>r.Id==target.Region); if(region is null) return false;
        try { region.Validate(target); } catch(RuleException) { return false; }
        if(a.Kind==ActionKind.Travel) return target.Region!=destination.Region && a.Lot is null && !lots.Any(l=>l.Location.Inventory==person && l.Owner!=person && l.Owner is not null && !scenario.Authorizations.Contains(new(person,l.Owner,"carry")));
        var lot=lots.SingleOrDefault(l=>l.Id==a.Lot);
        return target.Region==destination.Region && target!=destination && lot is not null && !scenario.ItemTypes.Single(t=>t.Id==lot.Type).Portable && lot.Location.Tile==destination && (lot.Owner==person || lot.Owner is not null && scenario.Authorizations.Contains(new(person,lot.Owner,"carry")));
    }
}

public static class SpatialScenarios
{
    public static Scenario Create(bool environment=false)
    {
        var s=Baseline.Create();
        s=s with { Name="Two regional markets",ConfigurationVersion="space-1",EnabledMechanisms=["space-1"],Space=new("space-1",2),Regions=[..s.Regions,new("R1",2,0,1)],Markets=[..s.Markets,new("R1","food",200,200,null)],Entities=s.Entities.Select(e=>e.Id=="P19"?e with { Location=new("R1",0,0) }:e).ToArray() };
        if(!environment) return s;
        var tile=new Tile("R0",0,0);
        return s with { Name="Building rest and site production",ConfigurationVersion="environment-1",EnabledMechanisms=["space-1","environment-1"],Environment=new("environment-1",["flat","water"],[new(new("R1",3,3),"water")],[new("wall",["flat"],true)],[new("wall-0","wall",tile,"P01")],[new("house",tile,1,1,["wall-0"],"F",7)]),Recipes=s.Recipes.Select(r=>r with { Site=new("flat","wall","house") }).ToArray() };
    }
}
