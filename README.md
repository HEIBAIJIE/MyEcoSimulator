# MyEcoSimulator

规则来源见 [spec总览](spec/00_index.md)，实施状态见 [feature索引](feature/README.md)。

当前已完成F01—F04：独立C#内存核心，支持严格场景加载、资产核对、固定意图结算及基准自主策略720回合运行。

[F04A](feature/04a_persistence.md)已按已验证范围完成：API使用MongoDB持久化，支持状态恢复、分页历史与流式导出。已知限制见feature记录。

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


## F04 历史内存服务接口（由下方F04A入口替代）

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
## F04A 持久化入口

API 已切换为 MongoDB 必需配置，驱动 MongoDB.Driver 3.5.2，验证服务器 MongoDB 8.2。
本地 Windows：`./scripts/start-mongo.ps1`；容器：`docker compose up -d`。
使用 mongosh 初始化：
`mongosh mongodb://localhost:27018 --eval 'rs.initiate({_id:"eco-rs",members:[{_id:0,host:"localhost:27018"}]})'`。

```powershell
$env:Mongo__Connection='mongodb://localhost:27018/?directConnection=true'
$env:Mongo__Database='eco'
dotnet run --project src/EcoSimulator.Api --urls http://localhost:5184
```

创建请求新增必填 Policy：
`{"RequestId":"create-1","Variant":"baseline","Configuration":null,"Policy":{"Schema":"mongo-1","StorageBytes":4294967296,"InflightBytes":67108864,"ProcessBytes":2147483648,"PageSize":20}}`。
旧F04场景可通过显式提供此部署边界创建持久化运行；旧runtime-1导出不是恢复输入。
历史接口改为 `/history?after=-1&limit=20&subject=P00&item=food&market=R0:food`，返回Records及Next游标；完整回合保留关联。
导出改为 runtime-2 NDJSON，首行配置与截止回合，次行初始化事件，其后每行一个完整回合；响应流逐回合读取并等待写出。
重启默认停止调度，显式resume继续。恢复状态world-1保存余额、批次、任务、股份、生理状态、策略记忆与成交窗口。
数据库连接不进入导出。初始化会检查副本集及实际事务能力；无数据库时启动失败。

F04A完成范围以已验证用例为准，已知限制详见feature记录。
