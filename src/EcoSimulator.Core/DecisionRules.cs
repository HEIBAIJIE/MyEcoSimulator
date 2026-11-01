namespace EcoSimulator.Core;

public static class DecisionRules
{
    public static void Validate(Scenario s)
    {
        var p=s.Decisions;
        ScenarioLoader.Require(s.Strategy.Version=="needs-1" ? p is { Version:"needs-1" } : p is null,"决策参数版本不匹配");
        if(p is null) return;
        ScenarioLoader.Require(!s.EnabledMechanisms.Intersect(new[]{"production-1","organizations-1"}).Any(),"尚未支持该策略组合");
        ScenarioLoader.Require(s.ItemTypes.Any(t=>t.Id==p.FoodType && t.Id=="food" && t.Tag=="food" && t.Satiety==20),"采购策略目前只支持同质food");
        ScenarioLoader.Require(p.EatThreshold>=20 && p.EatThreshold<p.EatTarget && p.EatTarget<=s.World.Maximum && p.RestThreshold>=12 && p.RestThreshold<p.RestTarget && p.RestTarget<=s.World.Maximum,"需求决策阈值非法");
        ScenarioLoader.Require(p.WorkWindows.Select(w=>w.Company).Order().SequenceEqual(s.Entities.Where(e=>e.Kind==EntityKind.Company).Select(e=>e.Id).Order()),"企业工作窗口缺失或重复");
        foreach(var w in p.WorkWindows) ScenarioLoader.Require(w.Hours.Length>0 && w.Hours.Distinct().Count()==w.Hours.Length && w.Hours.All(h=>h>=0&&h<24),"工作窗口非法");
        ScenarioLoader.Require(s.Tasks.All(t=>p.WorkWindows.Single(w=>w.Company==t.Company).Hours.Contains((int)(t.EffectiveTurn%24))),"任务超出工作窗口");
    }
}

// Shared decision policy: no personal timetable, prescribed destination or town identifier.
public sealed class NeedsPersonStrategy(DecisionPolicy policy, ItemType[] types) : IPersonStrategy
{
    public PersonalDecision Decide(DecisionView v, PersonalMemory m, WorldParameters p, StrategyParameters s)
    {
        var self=v.Self;
        var task=v.Tasks.FirstOrDefault(t=>t.Employee==self.Id && t.EffectiveTurn==v.Turn);
        MainAction action; string reason; Tile? destination=self.Location;
        bool resting=false;
        if(self.Satiety<=20) { action=Eat();reason="生存紧急：饥饿优先进食"; }
        else if(self.Energy<=12) { action=new(ActionKind.Rest);resting=true;reason="生存紧急：恢复精力"; }
        else if(v.Transport is { State:"running" } trip) { action=new(trip.Kind){Target=trip.Target,Lot=trip.Lot};destination=null;reason="继续已开始运输"; }
        else if(v.Production is { State:"running" } work) { action=new(ActionKind.Labor,work.Task);destination=v.Tasks.Single(t=>t.Id==work.Task).Location;reason="继续已开始劳动"; }
        else if(self.Satiety<=policy.EatThreshold) { action=Eat();reason="自身食品缓冲不足，补充饱腹"; }
        else if(self.Energy<=policy.RestThreshold || m.Resting&&self.Energy<policy.RestTarget) { action=new(ActionKind.Rest);resting=true;reason="自身精力不足，连续恢复至目标"; }
        else if(task is not null) { action=new(ActionKind.Labor,task.Id);destination=task.Location;reason="执行公开任务取得工资"; }
        else { action=new(ActionKind.Idle);reason="需求已满足且没有公开任务，原地停留"; }
        if(resting)
        {
            var site=v.RestSites.OrderByDescending(b=>b.RestBonus).ThenBy(b=>Math.Abs(b.Anchor.X-self.Location.X)+Math.Abs(b.Anchor.Y-self.Location.Y)).FirstOrDefault();
            if(site is not null) destination=site.Anchor;
        }
        if(action.Kind==ActionKind.Eat)
        {
            var food=v.Inventory.Where(l=>l.Owner==self.Id && l.Type==policy.FoodType && l.Location.Tile is {} t && t.Region==self.Location.Region).OrderBy(l=>Math.Abs(l.Location.Tile!.Value.X-self.Location.X)+Math.Abs(l.Location.Tile!.Value.Y-self.Location.Y)).FirstOrDefault();
            if(!v.Inventory.Any(l=>l.Owner==self.Id && l.Type==policy.FoodType && l.Location.Inventory==self.Id) && food is not null) destination=food.Location.Tile;
        }
        var purchase=new BaselinePersonStrategy().Decide(v,m with { Resting=false },p,s);
        // Procurement is shared, but eligibility follows the action actually selected above.
        return new(new(self.Id,destination,action,[],reason),action.Kind is ActionKind.Rest or ActionKind.Travel ? null : purchase.Order,purchase.Memory with { Resting=resting },System.Text.Json.JsonSerializer.Serialize(new { Policy=policy, Self=self, Branch=reason, Destination=destination, Task=task?.Id, Procurement=purchase.Inputs }));
        MainAction Eat()=>new(ActionKind.Eat,Type:policy.FoodType,Maximum:Math.Max(1,StrategyMath.Ceil(Math.Max(0,policy.EatTarget-self.Satiety),types.Single(t=>t.Id==policy.FoodType).Satiety)),Conditional:true);
    }
}
