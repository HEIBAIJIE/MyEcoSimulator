# F12 小镇状态与昼夜观察

状态：实施中。规则0.3，内容town-2，通用需求策略needs-1。依赖F11、F07—F09、F04A。不考虑旧存档兼容。

## 当前设计与验收边界

生活由需求、资源、任务与个体决策涌现，不通过个人餐时、睡觉窗口、固定目的地或随机闲逛制造。此前town-life-1方案已按用户反馈撤销，TownLife.cs及专用配置、记忆均删除。权威规则见spec/decision-rules/05_town_life_strategy.md及spec/content-config/04_town_life.md。

实现入口：DecisionRules.cs的通用需求决策、Model.cs的共享参数、Strategies.cs的任务与反馈、Settlement.cs的有限公开建筑视图、TownScenario.cs的内容初始化。企业公开工作窗口为组织制度，个人按自身需求与任务选择；Idle原地停留。建筑条件决定休息落点，食品可达性决定消费落点，保持预留、守恒和原子结算。当前简单策略不承诺现实作息、持续繁荣或永久不同步。

时间与回合集中在右上角：日历、大时钟、当前回合、暂停/继续、单步、速度和运行状态。桌面钟盘112px，390px窄屏86px，详情避让整个组件；底部不再保留控制。事件中的发生时间仍随证据显示。

## 验证记录

2026-10-31：针对性测试命令 `dotnet test --filter 'FullyQualifiedName~TownLifeTests|FullyQualifiedName~TownTests|FullyQualifiedName~SpaceEnvironmentTests|FullyQualifiedName~StrategyTests' --nologo`，20项通过；覆盖状态优先级、不依赖钟点、连续休息、窗口、前48小时多动作及落点、守恒和恢复。`npm run build --prefix web`通过，存在包体积提示。

`npm run test:e2e --prefix web -- town-life game-world visual-experience missing-run`，4项通过：桌面、390px、各光照关键小时、跨日、刷新、画布保留、独立多区域多组织观察、失效存档回到新游戏。截图artifacts/f12/hour-*.png及mobile.png；已检查窄屏时间控制与详情无重叠。

`dotnet test --filter FullyQualifiedName~TownLifeAcceptanceTests --nologo`用于真实MongoDB默认场景720回合、24小时后恢复与流式导出；运行标识和导出在artifacts/f12/run-id.txt、town-2.ndjson。初版脚本统计不再代表当前规则，后续以新导出为准。

## 剩余验收

高倍速跨轮询与分页缺口的专门浏览器断言、720结束界面、交易/工资/缺勤/目标完成摘要及证据跳转仍未充分完成。F12保持实施中，不把已完成的局部验证等同于整个feature完成。生活策略与production-1/organizations-1组合明确拒绝，独立既有场景保留。
