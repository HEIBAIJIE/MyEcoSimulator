using System.Numerics;
namespace EcoSimulator.Core;

public enum OrderSide { Buy, Sell }
public sealed record MarketOrder(string Id, string Entity, string Region, string Tag, OrderSide Side, long Limit, long Quantity);
public sealed record ClearingResult(long Quantity, long? Lower, long? Upper, long? Price, IReadOnlyDictionary<string,long> Allocations);

public static class Clearing
{
    // The Kth highest bid and Kth lowest ask bound the entire maximum-volume plateau.
    public static ClearingResult Clear(IEnumerable<MarketOrder> input, Random? random = null)
    {
        random ??= Random.Shared;
        var orders = input.ToArray();
        ScenarioLoader.Require(orders.All(o => o.Limit > 0 && o.Quantity > 0 && Enum.IsDefined(o.Side)) && orders.Select(o => o.Id).Distinct().Count() == orders.Length, "出清订单非法");
        ScenarioLoader.Require(orders.Select(o => (o.Region,o.Tag)).Distinct().Count() <= 1, "必须按市场分别出清");
        var valid = orders.Where(o => orders.Count(x => x.Entity == o.Entity) == 1).ToArray();
        var buys = valid.Where(o => o.Side == OrderSide.Buy).OrderByDescending(o => o.Limit).ToArray();
        var sells = valid.Where(o => o.Side == OrderSide.Sell).OrderBy(o => o.Limit).ToArray();
        var allocations = orders.ToDictionary(o => o.Id, _ => 0L);
        long k = 0, lower = 0, upper = 0; int b = 0, s = 0; long bn = 0, sn = 0;
        while (b < buys.Length && s < sells.Length && buys[b].Limit >= sells[s].Limit)
        {
            var n = Math.Min(buys[b].Quantity-bn,sells[s].Quantity-sn);
            k = checked(k+n); lower = sells[s].Limit; upper = buys[b].Limit;
            bn += n; sn += n;
            if (bn == buys[b].Quantity) { b++; bn=0; }
            if (sn == sells[s].Quantity) { s++; sn=0; }
        }
        if (k == 0) return new(0,null,null,null,allocations.AsReadOnly());
        void Allocate(MarketOrder[] side)
        {
            long left = k;
            foreach (var group in side.GroupBy(o => o.Limit))
            {
                long total = group.Aggregate(0L,(a,o) => checked(a+o.Quantity));
                if (total <= left) { foreach(var o in group) allocations[o.Id]=o.Quantity; left-=total; }
                else
                {
                    var ranked = group.Select(o => { var product=(BigInteger)left*o.Quantity; allocations[o.Id]=(long)(product/total); return (Order:o,Remainder:product%total,Tie:random.NextInt64()); }).OrderByDescending(x => x.Remainder).ThenBy(x => x.Tie).ToArray();
                    var extra=left-ranked.Sum(x => allocations[x.Order.Id]);
                    foreach(var x in ranked.Take(checked((int)extra))) allocations[x.Order.Id]++;
                    break;
                }
                if(left==0) break;
            }
        }
        Allocate(buys); Allocate(sells);
        return new(k,lower,upper,lower+(upper-lower)/2,allocations.AsReadOnly());
    }
}
