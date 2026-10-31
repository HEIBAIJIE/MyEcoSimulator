# MyEcoSimulator

规则来源见 [spec总览](spec/00_index.md)，实施状态见 [feature索引](feature/README.md)。

当前已完成F01：独立C#内存核心，可初始化、核对资产与严格加载场景；尚不能推进经济回合。

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
