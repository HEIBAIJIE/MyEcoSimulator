using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
namespace EcoSimulator.Core;

public static class ScenarioLoader
{
    public static Scenario Read(string json)
    {
        try
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info => { if (info.Kind == JsonTypeInfoKind.Object) foreach (var p in info.Properties) if (p.Set is not null) p.IsRequired = true; });
            var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString, TypeInfoResolver = resolver, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            options.Converters.Add(new JsonStringEnumConverter());
            var scenario = JsonSerializer.Deserialize<Scenario>(json, options) ?? throw new RuleException("场景为空");
            Validate(scenario);
            return scenario;
        }
        catch (Exception e) when (e is JsonException or OverflowException or ArgumentException or NullReferenceException)
        { throw new RuleException("配置无效: " + e.Message); }
    }
    public static string Write(Scenario scenario) => JsonSerializer.Serialize(scenario, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
    internal static void Require(bool valid, string reason) { if (!valid) throw new RuleException(reason); }
    public static void Validate(Scenario s)
    {
        try { ValidateCore(s); }
        catch (Exception e) when (e is OverflowException or ArgumentException or NullReferenceException) { throw new RuleException("配置非法: " + e.Message); }
    }
    private static void ValidateCore(Scenario s)
    {
        void R(bool b, string message) => Require(b, message);
        R(!string.IsNullOrWhiteSpace(s.Name) && s.RuleVersion == "0.3" && !string.IsNullOrWhiteSpace(s.ConfigurationVersion), "不支持的版本或空名称");
        R(s.EnabledMechanisms.Distinct().Count() == s.EnabledMechanisms.Length && s.EnabledMechanisms.All(x => x is "production-1" or "organizations-1"), "当前阶段不支持启用扩展机制");
        bool organizations=s.EnabledMechanisms.Contains("organizations-1");
        OrganizationRules.Validate(s);
        if(s.EnabledMechanisms.Contains("production-1"))
        {
            var p=s.Procurement;
            R(s.Strategy.Version=="procurement-1" && p is not null && p.Version=="procurement-1" && p.Window>0 && p.Safety>=0 && p.Trial>0 && p.Observation>0 && p.MinimumCash>=0 && p.StopTurns>0 && p.ResumeTurns>0 && p.BuyPercent>=100 && p.BuyPercent<=170 && p.SellPercent>=80 && p.SellPercent<=110, "Invalid or disabled procurement parameters");
            R(s.Entities.Count(e => e.Kind==EntityKind.Company)==1 && s.Recipes.Length==1 && s.Recipes[0].Steps.Length>0 && s.Schedule.All(c => c.Output is null), "F06 requires one company, one stepped recipe, and no productivity schedule");
            R(s.ItemTypes.Any(t=>t.Id=="food" && t.Tag=="food" && t.Satiety==20) && s.Recipes[0].OutputType=="food" && s.ItemTypes.Select(t=>t.Tag).Distinct().Count()==s.ItemTypes.Length, "procurement-1 requires food and unique homogeneous material tags");
        }
        else R(s.Procurement is null && s.Strategy.Version=="baseline-1", "Invalid or disabled procurement parameters");
        R(s.Terrain == "flat" && s.DeathInvestmentPolicy == "confiscate", "不支持的地形或股权死亡机制");
        R(s.World.Version == "0.3" && (s.Strategy.Version == "baseline-1" || s.Strategy.Version == "procurement-1"), "不支持的参数版本");
        R(s.World.Maximum > 0 && new[] { s.World.SatietyDecay, s.World.EnergyDecay, s.World.RestRecovery, s.World.HungerDamage, s.World.FatigueDamage, s.World.HealthyThreshold, s.World.HealthRecovery }.All(x => x >= 0) && s.World.HealthyThreshold <= s.World.Maximum, "世界参数非法");
        R(s.Strategy.Window > 0 && s.Strategy.SafetyStock >= 0 && s.Strategy.TrialProduction >= 0 && s.Strategy.ObservationTurns > 0, "策略参数非法");
        R(s.Limits.Turns > 0 && s.Limits.HistoryTurns >= s.Limits.Turns && s.Limits.SnapshotInterval == 1 && s.Limits.MemoryBytes > 0, "运行边界非法");
        R(s.Schedule.Select(c => c.Turn).Distinct().Count() == s.Schedule.Length && s.Schedule.All(c => c.Turn >= 0 && c.Turn < s.Limits.Turns && !string.IsNullOrWhiteSpace(c.Version) && (c.Output is null || c.Output > 0) && (c.SatietyDecay is null || c.SatietyDecay >= 0) && (c.Output is not null || c.SatietyDecay is not null || c.BlockNewTasks is not null)), "Schedule invalid");
        var regions = s.Regions.ToDictionary(x => x.Id);
        R(regions.Count > 0, "缺少区域");
        foreach (var a in s.Regions)
        {
            R(!string.IsNullOrWhiteSpace(a.Id) && a.Side > 0, "区域非法");
            _ = checked(a.Side * 100); _ = checked(a.X + a.Side); _ = checked(a.Y + a.Side);
            foreach (var b in s.Regions.Where(b => b.Id != a.Id))
                R(!(a.X < checked(b.X + b.Side) && b.X < checked(a.X + a.Side) && a.Y < checked(b.Y + b.Side) && b.Y < checked(a.Y + a.Side)), "区域重叠");
        }
        void TileCheck(Tile t) { R(regions.ContainsKey(t.Region), "未知区域"); regions[t.Region].Validate(t); }
        var entities = s.Entities.ToDictionary(x => x.Id);
        bool Kind(string id, EntityKind k) => entities.TryGetValue(id, out var e) && e.Kind == k;
        R(Kind(s.Issuer, EntityKind.Country) && s.Entities.Count(x => x.Kind == EntityKind.Country) == 1, "发行国家非法");
        foreach (var e in s.Entities)
        {
            R(!string.IsNullOrWhiteSpace(e.Id) && Enum.IsDefined(e.Kind), "主体非法"); TileCheck(e.Location);
            R(new[] { e.Health, e.Satiety, e.Energy }.All(x => x >= 0 && x <= s.World.Maximum), "需求数值非法");
        }
        R(s.Memberships.Distinct().Count() == s.Memberships.Length && s.Employees.Distinct().Count() == s.Employees.Length, "重复关系");
        foreach (var m in s.Memberships) R(Kind(m.Person, EntityKind.Person) && entities.ContainsKey(m.Organization) && !Kind(m.Organization, EntityKind.Person), "成员引用非法");
        foreach (var p in s.Entities.Where(x => x.Kind == EntityKind.Person)) R(s.Memberships.Contains(new(p.Id, s.Issuer)), "个人必须属于国家");
        foreach (var e in s.Employees) R(Kind(e.Person, EntityKind.Person) && Kind(e.Company, EntityKind.Company) && !string.IsNullOrWhiteSpace(e.Job), "员工引用非法");
        R(organizations || s.Positions.Length == 0, "本阶段不支持额外职位");
        foreach(var position in s.Positions) R(Kind(position.Organization,EntityKind.Company) && !string.IsNullOrWhiteSpace(position.Name) && position.Holders.Distinct().Count()==position.Holders.Length && position.Holders.All(h=>Kind(h,EntityKind.Person)),"职位引用非法");
        R(s.Positions.Select(p=>(p.Organization,p.Name)).Distinct().Count()==s.Positions.Length && s.Authorizations.Distinct().Count()==s.Authorizations.Length,"Duplicate position or authorization");
        foreach (var a in s.Authorizations) R(Kind(a.Person, EntityKind.Person) && (Kind(a.Organization, EntityKind.Company) || s.EnabledMechanisms.Contains("production-1") && entities.ContainsKey(a.Organization) && a.Permission is "material" or "tool") && new[] { "labor", "view", "custody", "pickup", "drop", "carry", "sell", "consume", "material", "tool" }.Contains(a.Permission) && (a.Permission != "labor" || s.Employees.Any(e => e.Person == a.Person && e.Company == a.Organization)), "不支持的授权");
        R(s.Warehouses.Select(x => x.Organization).Distinct().Count() == s.Warehouses.Length, "重复仓库");
        foreach (var w in s.Warehouses) { R(entities.ContainsKey(w.Organization) && !Kind(w.Organization, EntityKind.Person), "仓库引用非法"); TileCheck(w.Location); }
        foreach (var c in s.Entities.Where(e => e.Kind != EntityKind.Person)) R(s.Warehouses.Any(w => w.Organization == c.Id), "组织缺少固定仓库");
        var types = s.ItemTypes.ToDictionary(x => x.Id);
        R(types.Count > 0, "缺物品定义");
        foreach (var t in s.ItemTypes) R(!string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Tag) && t.Unit == "piece" && t.Quality == 1 && t.Satiety >= 0 && t.Portable && (t.Durability is null || s.EnabledMechanisms.Contains("production-1") && t.Durability > 0), "不支持的物品属性");
        var recipes = s.Recipes.ToDictionary(x => x.Id);
        foreach (var r in s.Recipes) R(Kind(r.Company, EntityKind.Company) && types.ContainsKey(r.OutputType) && r.Output > 0 && r.Wage >= 0 && (r.Hours == 1 || s.EnabledMechanisms.Contains("production-1")) && !string.IsNullOrWhiteSpace(r.Version), "配方非法");
        foreach (var r in s.Recipes) ProductionRules.Validate(s, r);
        R(s.Tasks.Select(x => x.Id).Distinct().Count() == s.Tasks.Length, "重复任务");
        foreach (var t in s.Tasks)
        {
            R(recipes.ContainsKey(t.Recipe), "任务配方悬空"); var r = recipes[t.Recipe]; TileCheck(t.Location);
            R(t.EffectiveTurn == 0 && t.Company == r.Company && t.RecipeVersion == r.Version && t.Wage == (r.Steps.Length == 0 ? r.Wage : r.Steps[0].Wage) && t.Output == (r.Steps.Length == 0 ? r.Output : r.Steps[0].Output) && s.Employees.Any(e => e.Person == t.Employee && e.Company == t.Company && e.Job == r.Job) && s.Authorizations.Contains(new(t.Employee,t.Company,"labor")), "任务条件非法");
        }
        R(organizations || s.Tasks.Select(t => t.Employee).Distinct().Count() == s.Tasks.Length, "员工重复任务");
        foreach (var h in s.Shares) R(Kind(h.Holder, EntityKind.Person) && Kind(h.Company, EntityKind.Company) && h.Investment >= 0, "股东非法");
        foreach (var c in s.Entities.Where(e => e.Kind == EntityKind.Company)) R(s.Shares.Any(h => h.Company == c.Id), "企业缺股东");
        R(s.Shares.Select(h => (h.Holder,h.Company)).Distinct().Count() == s.Shares.Length, "重复股权");
        R(s.Cash.Select(x => x.Entity).Distinct().Count() == entities.Count && s.Cash.Length == entities.Count && s.Cash.All(x => entities.ContainsKey(x.Entity) && x.Amount >= 0), "现金分配非法");
        R(s.Issuance >= 0 && s.Cash.Aggregate(0L,(a,c) => checked(a+c.Amount)) == s.Issuance, "发行与分配不符");
        foreach (var c in s.Entities.Where(e => e.Kind == EntityKind.Company)) R(s.Shares.Where(h => h.Company == c.Id).Aggregate(0L,(a,h) => checked(a+h.Investment)) == s.Cash.Single(x => x.Entity == c.Id).Amount, "现金资本与投资不符");
        R(s.Markets.Select(m => (m.Region,m.Tag)).Distinct().Count() == s.Markets.Length, "重复市场");
        foreach (var m in s.Markets) R(regions.ContainsKey(m.Region) && types.Values.Any(t => t.Tag == m.Tag) && m.ReferencePrice > 0 && m.AccountingValue > 0 && m.LatestPrice is null, "市场非法");
        foreach (var r in s.Regions) foreach (var t in s.ItemTypes) R(s.Markets.Any(m => m.Region == r.Id && m.Tag == t.Tag), "缺初始价格");
        Inventory.Validate(s.Lots, entities, types, TileCheck);
    }
}
