# F10 可探索的游戏世界

状态：已完成（以下验证范围）。依据规则0.3、town-1及运行观察F10修订。依赖F05、F09，替代F05的默认报表式入口，不修改历史验收。

交付单一玩家开局、真实环境对象、全屏等距地图、镜头操作、对象详情、渐进经济信息、存档恢复及时间控制。经济规则不变，不包含玩家命令、寻路、住房、采集及可变开局参数。旧变体保留内部验证与接口兼容。

所有内容与世界数据从后端取得，前端只负责表现和交互。验收包括场景严格加载、守恒及48回合推进、恢复；浏览器开始/继续、对象选择、镜头、时间、证据及刷新恢复；桌面与窄屏截图检查迭代；构建与相关测试通过。客户端最多保留近期48条历史，完整历史保存在服务端。


## 实施与验收（2026-10-31）

规则0.3，town-1，F10观察呈现修订。入口：TownScenario、WorldPresentation、ObserverSnapshot.Presentation、GET /game/start；前端main.tsx、MapView.tsx及style.css。Presentation由后端提供名称、用途、选择关联、时间尺度、需求上限及镜头锚点，不进入决策策略。开局预算来自后端；旧变体保留内部兼容，不在玩家界面展示。

验证：`npm run build --prefix web`通过（现有Pixi打包体积提示）；`dotnet test -c Release --filter 'FullyQualifiedName~TownTests|FullyQualifiedName~SpaceEnvironmentTests|FullyQualifiedName~StrategyTests'`，16项通过。新开局严格序列化加载，48回合自主运行、现金总量400000分及恢复验证通过。未扩大全变体720回合持久化压力回归，因为未修改经济结算机制。

`npx --prefix web playwright test --config web/playwright.config.ts game-world.spec.ts --timeout 45000`通过：真实MongoDB创建开局、单步、居民需求及证据、经济曲线、镜头拖动缩放归位、暂停继续、刷新存档和390px窄屏无横向溢出，无浏览器运行异常。截图在web/test-results/f10-*.png。已实际检查桌面世界、居民面板及手机画面；调整河流入镜、面板布局、刷新镜头保存。采用程序化矢量风格，尚无角色行动动画；同点人物展开明确标注，不宣称有通勤、住房或建造玩法。历史高级入口可按主体、物品、市场及游标查询完整回合，原始证据折叠展示。

已知边界：当前世界固定开局，开放参数留待后续feature；旧F05/F07/F09浏览器脚本对应历史界面，未改写历史记录，新界面由game-world.spec.ts验收。F04A既有持久化容量限制仍有效。
