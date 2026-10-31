# MyEcoSimulator

规则来源见 [spec总览](spec/00_index.md)，实施状态见 [feature索引](feature/README.md)。

当前已完成F01—F03：独立C#内存核心，支持严格场景加载、资产核对、固定意图结算及基准自主策略720回合运行。

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

策略接口为 `IPersonStrategy` / `ICompanyStrategy`，仅接收各自可见信息；观察者快照不传给策略。基准固定工资、单企业、无融资分红、禁止个人食品生产；库存压力降价等为策略假设。验证关注合法性与可解释性，不要求价格稳定或人口长期存活。运行服务和观察界面留F04/F05。
