# MyEcoSimulator

规则来源见 [spec总览](spec/00_index.md)，实施状态见 [feature索引](feature/README.md)。

当前已完成F01—F04：独立C#内存核心，支持严格场景加载、资产核对、固定意图结算及基准自主策略720回合运行。

使用.NET SDK 9.0.300，目标.NET 8：

```powershell
dotnet test -c Release
```

核心调用：

```csharp
using EcoSimulator.Core;
var scenario = Baseline.Create();
var json = ScenarioLoader.Write(scenario);
var world = World.Load(ScenarioLoader.Read(json));
var company = world.Report("F"); // 现金200000，存货10000，权益210000分
```

`World.Commit`供后续结算器提交完整候选物品集合与实体平衡分录；它验证账实和现金守恒，不代替F02的授权、意图与预留检查。初始化事件、物品和报告均为内存数据。成本与金额使用整数，批次摊销中间乘法使用BigInteger防止溢出。

固定意图结算入口为 `World.Advance(new TurnInput(people, orders))`，结果包含前后快照、十阶段、预留、执行反馈和状态事实。`World.View(entity)`提供主体自身信息；`World.Snapshot()`供全局观察。非法回合回滚并暂停，`World.Failure`保留输入和失败原因。

自主运行：

```csharp
var world = World.Load(Baseline.Create());
for (int t = 0; t < 720; t++) world.AdvanceAutonomously();
var history = world.AutonomousHistory; // 完整快照、决策输入/版本、公开任务和实际反馈
```

策略接口为 `IPersonStrategy` / `ICompanyStrategy`，仅接收各自可见信息；观察者快照不传给策略。基准固定工资、单企业、无融资分红、禁止个人食品生产；库存压力降价等为策略假设。验证关注合法性与可解释性，不要求价格稳定或人口长期存活。运行服务由F04提供，观察界面留F05。


## 内存运行服务

启动：`dotnet run --project src/EcoSimulator.Api --urls http://localhost:5184`。

`POST /runs`：`{"RequestId":"create-1","Variant":"baseline","Configuration":null}`。
变体：`baseline`、`no-resident-cash`、`low-capital`、`productivity`、`demand`、`pause`。
导出的Configuration对象转为JSON字符串后，可代替Variant提交，开始新运行。

`POST /runs/{id}/commands`：`{"RequestId":"step-1","Operation":"step","Speed":null}`。
支持pause、resume、step、speed；Speed为每秒回合数（大于0且不超过1000），实际速度受计算耗时限制。
同请求标识同命令返回原结果，冲突命令拒绝。

读取：`GET /runs/{id}`、`/snapshot?turn=1`、`/history?from=0&to=23&subject=P00&item=food&market=R0:food`、`/daily`、`/export`。
历史筛选返回匹配对象所在完整回合，保留关联交易和分录。Int64字段统一为十进制字符串。
导出模式runtime-1；配置严格校验，不接受整个导出作为配置，不支持快照续跑。

每回合保存快照、意图、理由、预留、执行反馈与批次变更事实；日汇总按日及参数版本分段。
MemoryBudget按保留历史UTF-16序列化大小计费，提交前保守预留；不等于进程工作集上限。
对象及导出临时缓冲另占内存，性能记录见F04。预算不足暂停且保留历史供导出。
