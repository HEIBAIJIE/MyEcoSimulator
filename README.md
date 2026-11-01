# MyEcoSimulator

一个让人通过小镇生活理解经济的 2D 游戏。居民需要进食与休息，工坊组织劳动、支付工资、生产并出售食品；食物、现金和人的选择共同推动世界变化。

当前以“河湾镇”为唯一玩家开局：20 位居民、一家食品工坊，以及河流、树木和公共建筑。通过全屏等距地图探索聚落，点选人物和工坊，逐层了解需求、行动理由、生产与交易。项目目前处于**观察式游戏**阶段，尚未加入玩家经营指令。

## 当前体验

- 拖动地图、滚轮缩放，点击人物、工坊、公共建筑或树木查看详情。
- 从居民列表选择同地点人物，查看健康、饱腹、精力、现金和持有物品。
- 暂停、继续、单步推进，切换 1×、4×、12× 速度。
- 按需打开经济趋势、最近动态和历史凭据，追踪行动、资源预留及资产记录。
- 世界自动持久化到 MongoDB，支持刷新恢复、按存档标识载入和导出完整记录。

快捷键：`空格` 暂停或继续，`H` 回到聚落，`Esc` 关闭面板。

默认开局固定；未来起点差异将通过开局参数配置表达。历史场景变体保留为内部验证及兼容接口，不在玩家界面罗列。

## 本地启动

以下命令在仓库根目录执行，示例使用 PowerShell。

### 环境要求

- .NET SDK **9.0.300**，补丁版本选择遵循 `global.json`；项目目标为 .NET 8。
- Node.js **22.12+** 和 npm。
- MongoDB **8.2** 副本集及 `mongosh`，或可运行仓库 Compose 配置的 Docker 环境。

MongoDB 必须支持事务。后端启动时会检查数据库，不会退回内存模式。

### 1. 启动 MongoDB

任选一种方式：

```powershell
# Docker，数据保存在 eco-mongo 卷中
docker compose up -d
```

```powershell
# 本地 Windows，默认使用 C:/Program Files/MongoDB/Server/8.2/bin/mongod.exe
./scripts/start-mongo.ps1
```

首次启动后初始化副本集；已有副本集无需重复执行：

```powershell
mongosh mongodb://localhost:27018 --eval 'rs.initiate({_id:"eco-rs",members:[{_id:0,host:"localhost:27018"}]})'
```

### 2. 启动后端

```powershell
$env:Mongo__Connection='mongodb://localhost:27018/?directConnection=true'
$env:Mongo__Database='eco'
dotnet run --no-launch-profile --project src/EcoSimulator.Api --urls http://localhost:5184
```

保留这个终端运行，在另一个终端启动前端：

```powershell
npm ci --prefix web
npm run dev --prefix web -- --port 5173 --strictPort
```

打开 **http://127.0.0.1:5173/**，点击“开始新的生活”。Vite 将 `/runs` 和 `/game` 请求转发到后端 `5184` 端口。

也可先执行 `npm run build --prefix web`，再用上述命令启动后端，直接打开 **http://localhost:5184/**。构建产物由 ASP.NET Core 提供，无需另开前端服务。

### 存档与继续

浏览器保存当前运行标识，世界状态及历史保存在 MongoDB。刷新页面可继续查看；后端重启后默认暂停，需要显式继续。清除浏览器存储或更换浏览器后，可在开始菜单输入原存档标识载入。存档标识可在“玩法说明”面板查看。

NDJSON 导出是事实记录，当前不支持将整个导出文件直接导入为存档。

## 规则与当前边界

世界规则、决策策略和内容配置分层。所有世界数据、对象名称、状态和展示元数据来自后端；前端使用 React、TypeScript 和 PixiJS 负责渲染与交互。C# 模拟核心不依赖界面及数据库，ASP.NET Core 与 MongoDB 负责运行控制及持久化。

- 意图经过校验、资源预留及统一结算，不能直接修改世界；账实变更原子提交并保持守恒。
- 决策主体只能使用自身可见信息，观察者权限不传给策略。
- 金额使用整数分，API 的 Int64 值以十进制字符串传输。历史记录实际结果，不承诺跨运行重放一致。
- 当前默认开局固定工资、不融资、不分红，个人不能生产食品。建筑与树木不产生未经配置的经济效果。
- 当前没有玩家建造、采集、住房、寻路或指挥居民功能。人物以原地姿态示意最近成功结算的行动，失败明确标注，暂停时停止动作；局部展开便于选择，详情显示真实坐标。
- 停产、经济停滞和居民死亡都是允许出现的结果，不以价格稳定或长期繁荣作为验收条件，也不自动补贴资源。

客户端展示最近 48 条回合历史，完整事实通过服务端分页查询和流式导出。持久化容量与性能的已知边界见 [F04A](feature/04a_persistence.md)。

## 开发与验证

构建前端：

```powershell
npm run build --prefix web
```

当前游戏开局及相关规则的针对性验证：

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~TownTests|FullyQualifiedName~SpaceEnvironmentTests|FullyQualifiedName~StrategyTests'
```

游戏界面验收需要前后端及真实 MongoDB 已启动，前端地址为 `http://127.0.0.1:5173`：

```powershell
npx --prefix web playwright install chromium
npx --prefix web playwright test --config web/playwright.config.ts game-world.spec.ts --timeout 45000
```

测试覆盖开局、单步、居民详情、经济趋势、镜头操作、暂停继续、刷新恢复及窄屏布局，截图保存在 `web/test-results/f10-*.png`。旧浏览器脚本对应历史界面，当前游戏入口以 `game-world.spec.ts` 为准。

完整 .NET 测试入口为 `dotnet test -c Release`；涉及 MongoDB 的测试环境要求及历史验收范围见相应 feature。日常验证按变更影响选择范围，无需每次执行全变体 720 回合回归。

## 代码与文档导航

| 路径 | 内容 |
| --- | --- |
| `src/EcoSimulator.Core/` | 世界状态、决策、市场、生产和结算 |
| `src/EcoSimulator.Persistence/` | MongoDB 原子持久化、恢复、分页历史与导出 |
| `src/EcoSimulator.Api/` | HTTP 接口、运行调度及构建后的前端资源 |
| `web/` | 游戏界面与浏览器验收 |
| `tests/` | 核心及持久化验证 |
| [spec 总览](spec/00_index.md) | 当前目标与规则的权威来源 |
| [feature 索引](feature/README.md) | 实施计划及历史验收记录 |
| [F10 游戏世界](feature/10_game_world.md) | 当前游戏界面的实现与验收范围 |

当前规则版本为 `0.3`，玩家开局内容版本为 `town-1`。F01—F10 的完成范围以各 feature 记录为准；历史方案不代表所有机制都已进入默认游戏。
