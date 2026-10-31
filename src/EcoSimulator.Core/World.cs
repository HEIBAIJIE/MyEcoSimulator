namespace EcoSimulator.Core;
public static class Inventory
{
    internal static void Validate(IEnumerable<Lot> lots, IReadOnlyDictionary<string,Entity> entities, IReadOnlyDictionary<string,ItemType> types, Action<Tile> checkTile)
    {
        var all = lots.ToArray();
        ScenarioLoader.Require(all.Select(l => l.Id).Distinct().Count() == all.Length, "重复批次标识");
        foreach (var l in all)
        {
            ScenarioLoader.Require(!string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.Source) && l.Sequence >= 0 && l.OriginalQuantity > 0 && l.OriginalCost >= 0 && l.Start >= 0 && l.Quantity > 0 && checked(l.Start+l.Quantity) <= l.OriginalQuantity && types.ContainsKey(l.Type) && (l.Owner is null || entities.ContainsKey(l.Owner)), "批次数值或引用非法");
            ScenarioLoader.Require((l.Location.Tile is null) != (l.Location.Inventory is null), "物品必须有唯一位置");
            if (l.Location.Tile is { } tile) checkTile(tile);
            if (l.Location.Inventory is { } person) ScenarioLoader.Require(entities.TryGetValue(person,out var p) && p.Kind == EntityKind.Person && l.Custodian == person, "物品栏或保管引用非法");
            if (l.Custodian is not null) ScenarioLoader.Require(entities.ContainsKey(l.Custodian), "保管引用非法");
            _ = l.Cost;
        }
        foreach (var group in all.GroupBy(l => l.Source))
        {
            var first = group.First(); long end = 0;
            foreach (var l in group.OrderBy(l => l.Start))
            {
                ScenarioLoader.Require(l.OriginalCost == first.OriginalCost && l.OriginalQuantity == first.OriginalQuantity && l.Sequence == first.Sequence && l.Type == first.Type && l.Start >= end, "来源批次重叠或元数据不一致");
                end = checked(l.Start+l.Quantity);
            }
        }
        ScenarioLoader.Require(all.GroupBy(l => l.Sequence).All(g => g.Select(l => l.Source).Distinct().Count() == 1), "来源入账顺序重复");
    }
    public static (Lot[] Taken, Lot[] Remaining) TakeFifo(IEnumerable<Lot> lots, string owner, string type, long quantity)
    {
        if (quantity <= 0) throw new RuleException("出库数量非法");
        var remaining = lots.ToList(); var taken = new List<Lot>();
        foreach (var lot in remaining.Where(l => l.Owner == owner && l.Type == type).OrderBy(l => l.Sequence).ThenBy(l => l.Start).ToArray())
        {
            var n = Math.Min(quantity, lot.Quantity); var split = lot.Split(n, Guid.NewGuid().ToString("N"));
            remaining.Remove(lot); if (split.Remainder is not null) remaining.Add(split.Remainder); taken.Add(split.Taken); quantity -= n; if (quantity == 0) break;
        }
        if (quantity != 0) throw new RuleException("库存不足");
        return (taken.ToArray(), remaining.ToArray());
    }
}
public sealed partial class World
{
    private Dictionary<(string,Account),long> balances = new();
    private Lot[] lots = [];
    private readonly List<EconomicEvent> events = [];
    private readonly Scenario scenario;
    private readonly object gate = new();
    public IReadOnlyList<Lot> Lots { get { lock(gate) return Array.AsReadOnly(lots.ToArray()); } }
    public IReadOnlyList<EconomicEvent> Events { get { lock(gate) return events.Select(e => e with { Entries = e.Entries.ToArray() }).ToArray(); } }
    private World(Scenario s) { scenario = ScenarioLoader.Read(ScenarioLoader.Write(s)); }
    public static World Load(Scenario s)
    {
        ScenarioLoader.Validate(s); var w = new World(s); s = w.scenario;
        void Post(string id, string reason, params Entry[] entries) => w.Commit(new(id,reason,0,"initialization",entries),w.lots);
        Entry D(string e, Account a,long n) => new(e,a,n,0); Entry C(string e, Account a,long n) => new(e,a,0,n);
        Post("issue","国家期初发行，对方科目为期初净资产",D(s.Issuer,Account.Cash,s.Issuance),C(s.Issuer,Account.OpeningEquity,s.Issuance));
        foreach (var p in s.Cash.Where(c => c.Entity != s.Issuer && s.Entities.Single(e => e.Id == c.Entity).Kind != EntityKind.Company))
        {
            var capital = s.Shares.Where(h => h.Holder == p.Entity).Aggregate(0L,(a,h) => checked(a+h.Investment)); var amount = checked(p.Amount+capital);
            Post("allocate-"+p.Entity,"期初货币分配（包括随后投入资本）",D(s.Issuer,Account.OpeningEquity,amount),C(s.Issuer,Account.Cash,amount),D(p.Entity,Account.Cash,amount),C(p.Entity,Account.OpeningEquity,amount));
        }
        foreach (var h in s.Shares) Post("capital-"+h.Holder+"-"+h.Company,"现金转为企业股东投入及个人投资资产，不增加发行",D(h.Holder,Account.Investment,h.Investment),C(h.Holder,Account.Cash,h.Investment),D(h.Company,Account.Cash,h.Investment),C(h.Company,Account.ContributedCapital,h.Investment));
        foreach (var l in s.Lots)
        {
            Entry[] entries = l.Owner is null ? [] : [D(l.Owner,Account.Inventory,l.Cost),C(l.Owner,Account.OpeningEquity,l.Cost)];
            w.Commit(new("stock-"+l.Id,"明确期初实物及净资产来源，非运行中生产",0,"initialization",entries),[..w.lots,l]);
        }
        foreach (var c in s.Cash) ScenarioLoader.Require(w.Report(c.Entity).Cash == c.Amount,"初始化现金不符");
        return w;
    }
    public BalanceSheet Report(string entity)
    {
        lock(gate) return Report(balances,entity);
    }
    private static BalanceSheet Report(Dictionary<(string,Account),long> b,string e)
    {
        long V(Account a) => b.GetValueOrDefault((e,a));
        return new(V(Account.Cash),V(Account.Inventory),V(Account.Investment),checked(-V(Account.Liability)),checked(-V(Account.OpeningEquity)-V(Account.ContributedCapital)-V(Account.RetainedEarnings)));
    }
    // The caller supplies an entire candidate inventory and balanced journal group; publication occurs only after every check succeeds.
    public void Commit(EconomicEvent fact, IEnumerable<Lot> candidateLots)
    {
        lock(gate)
        {
            try
            {
                ScenarioLoader.Require(!string.IsNullOrWhiteSpace(fact.Id) && !string.IsNullOrWhiteSpace(fact.Reason) && fact.Turn >= 0 && !events.Any(e => e.Id == fact.Id),"事件标识或信息非法");
                var entries = fact.Entries.ToArray(); var candidate = new Dictionary<(string,Account),long>(balances); var inventory = candidateLots.ToArray();
                var entities = scenario.Entities.ToDictionary(e => e.Id);
                foreach (var e in entries)
                {
                    ScenarioLoader.Require(entities.ContainsKey(e.Entity) && Enum.IsDefined(e.Account) && e.Debit >= 0 && e.Credit >= 0 && (e.Debit == 0 || e.Credit == 0),"分录非法");
                    candidate[(e.Entity,e.Account)] = checked(candidate.GetValueOrDefault((e.Entity,e.Account))+e.Debit-e.Credit);
                }
                foreach (var g in entries.GroupBy(e => e.Entity)) ScenarioLoader.Require(g.Aggregate(0L,(a,e) => checked(a+e.Debit)) == g.Aggregate(0L,(a,e) => checked(a+e.Credit)),"实体分录不平");
                Inventory.Validate(inventory,entities,scenario.ItemTypes.ToDictionary(t => t.Id), t => scenario.Regions.Single(r => r.Id == t.Region).Validate(t));
                foreach (var e in scenario.Entities)
                {
                    var report = Report(candidate,e.Id);
                    ScenarioLoader.Require(report.Balanced && report.Cash >= 0 && report.Inventory >= 0 && report.Investment >= 0 && report.Liabilities >= 0,"资产负债非法");
                    ScenarioLoader.Require(report.Inventory == inventory.Where(l => l.Owner == e.Id).Aggregate(0L,(a,l) => checked(a+l.Cost)),"存货账实不符");
                }
                long total = scenario.Entities.Aggregate(0L,(a,e) => checked(a+Report(candidate,e.Id).Cash));
                ScenarioLoader.Require(total == scenario.Issuance,"现金不守恒");
                balances = candidate; lots = inventory; events.Add(fact with { Entries = entries });
            }
            catch (OverflowException) { throw new RuleException("金额或数量溢出，未提交"); }
        }
    }
}

