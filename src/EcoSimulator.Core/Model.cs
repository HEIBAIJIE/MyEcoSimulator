namespace EcoSimulator.Core;

public sealed class RuleException(string message) : Exception(message);
public readonly record struct Tile(string Region, long X, long Y);
public sealed record Region(string Id, long X, long Y, long Side)
{
    public void Validate(Tile tile)
    {
        if (tile.Region != Id || tile.X < 0 || tile.Y < 0 || tile.X >= checked(Side * 100) || tile.Y >= checked(Side * 100))
            throw new RuleException("小地块坐标越界");
    }
}
public enum EntityKind { Person, Country, Company }
public sealed record Entity(string Id, EntityKind Kind, Tile Location, long Health, long Satiety, long Energy);
public sealed record Membership(string Person, string Organization);
public sealed record Employee(string Person, string Company, string Job);
public sealed record Position(string Organization, string Name, string[] Holders);
public sealed record Shareholding(string Holder, string Company, long Investment);
public sealed record Authorization(string Person, string Organization, string Permission);
public sealed record Warehouse(string Organization, Tile Location);
public sealed record ItemType(string Id, string Tag, string Unit, long Satiety, decimal Quality, bool Portable, long? Durability);
public sealed record Recipe(string Id, string Version, string Company, string Job, string OutputType, long Output, long Wage, long Hours);
public sealed record WorkTask(string Id, string Company, string Employee, string Recipe, string RecipeVersion, Tile Location, long EffectiveTurn, long Wage, long Output);
public sealed record ItemLocation(Tile? Tile, string? Inventory);
// A slice retains its original source interval, even after splitting or merging.
public sealed record Lot(string Id, string Source, long Sequence, long OriginalQuantity, long OriginalCost,
    long Start, long Quantity, string Type, string? Owner, ItemLocation Location, string? Custodian)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long Cost => checked((long)(((System.Numerics.BigInteger)OriginalCost * (Start + Quantity)) / OriginalQuantity
        - ((System.Numerics.BigInteger)OriginalCost * Start) / OriginalQuantity));
    public Lot MergeAdjacent(Lot next)
    {
        if (Source != next.Source || Sequence != next.Sequence || OriginalQuantity != next.OriginalQuantity || OriginalCost != next.OriginalCost ||
            checked(Start + Quantity) != next.Start || Type != next.Type || Owner != next.Owner || Location != next.Location || Custodian != next.Custodian)
            throw new RuleException("仅允许相邻且全部属性一致的来源区间合并");
        return this with { Quantity = checked(Quantity + next.Quantity) };
    }
    public (Lot Taken, Lot? Remainder) Split(long quantity, string takenId)
    {
        if (quantity <= 0 || quantity > Quantity) throw new RuleException("拆分数量非法");
        return (this with { Id = takenId, Quantity = quantity }, quantity == Quantity ? null : this with { Start = checked(Start + quantity), Quantity = Quantity - quantity });
    }
}
public enum Account { Cash, Inventory, Investment, OpeningEquity, ContributedCapital, RetainedEarnings, Liability }
public sealed record Entry(string Entity, Account Account, long Debit, long Credit);
public sealed record EconomicEvent(string Id, string Reason, long Turn, string Stage, Entry[] Entries);
public sealed record BalanceSheet(long Cash, long Inventory, long Investment, long Liabilities, long Equity)
{
    public long Assets => checked(Cash + Inventory + Investment);
    public bool Balanced => Assets == checked(Liabilities + Equity);
}
public sealed record WorldParameters(string Version, long Maximum, long SatietyDecay, long EnergyDecay, long RestRecovery, long HungerDamage, long FatigueDamage, long HealthyThreshold, long HealthRecovery);
public sealed record StrategyParameters(string Version, long Window, long SafetyStock, long TrialProduction, long ObservationTurns);
public sealed record RunLimits(long Turns, long HistoryTurns, long SnapshotInterval, long MemoryBytes);
public sealed record Market(string Region, string Tag, long ReferencePrice, long AccountingValue, long? LatestPrice);
public sealed record CashAllocation(string Entity, long Amount);
public sealed record Scenario(string Name, string RuleVersion, string ConfigurationVersion, WorldParameters World,
    StrategyParameters Strategy, RunLimits Limits, string[] EnabledMechanisms, Region[] Regions, string Terrain,
    Entity[] Entities, Membership[] Memberships, Employee[] Employees, Position[] Positions, Shareholding[] Shares,
    Authorization[] Authorizations, Warehouse[] Warehouses, ItemType[] ItemTypes, Recipe[] Recipes, WorkTask[] Tasks,
    Lot[] Lots, Market[] Markets, string Issuer, long Issuance, CashAllocation[] Cash, string DeathInvestmentPolicy)
{
    public ScheduledChange[] Schedule { get; init; } = [];
}
public sealed record ScheduledChange(long Turn, string Version, long? Output, long? SatietyDecay, bool? BlockNewTasks);



