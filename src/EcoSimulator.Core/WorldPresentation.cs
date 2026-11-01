namespace EcoSimulator.Core;

// Observer-only content. Never passed to decision strategies or used for settlement.
public sealed record ObjectPresentation(string Id,string Name,string Appearance,string? Subject);
public sealed record WorldPresentation(string Name,string Version,long DayLength,long MaximumNeed,string FoodTag,Tile Camera,ObjectPresentation[] Objects)
{
    public static WorldPresentation Create(Scenario s)
    {
        var names=new[]{"林舟","陈禾","许沐","苏青","周遥","陆川","沈星","叶宁","宋棠","顾言","姜雨","唐栗","方野","白榆","温岚","夏溪","秦桐","江晚","孟秋","李苇"};
        var objects=s.Entities.Select((e,i)=>new ObjectPresentation(e.Id,e.Kind==EntityKind.Person?names[Array.FindIndex(s.Entities.Where(x=>x.Kind==EntityKind.Person).ToArray(),p=>p.Id==e.Id)%names.Length]:e.Kind==EntityKind.Company?(s.ConfigurationVersion.StartsWith("town-")?"河湾食品工坊":e.Id):(s.ConfigurationVersion.StartsWith("town-")?"镇务所":e.Id),e.Kind.ToString(),e.Id)).ToList();
        foreach(var b in s.Environment?.Buildings??[]) objects.Add(new(b.Id,b.Id=="workshop"?"河湾食品工坊":b.Id=="town-hall"?"镇公所":b.Id=="storehouse"?"公共仓舍":b.Id,b.Id=="workshop"?"workshop":"civic",b.Id=="workshop"?b.Owner:null));
        foreach(var t in s.Environment?.Surfaces.Where(t=>t.Type=="tree")??[]) objects.Add(new(t.Id,"河岸松",t.Type,null));
        return new(s.Name,s.ConfigurationVersion,24,s.World.Maximum,"food",s.Warehouses.First().Location,objects.ToArray());
    }
}
