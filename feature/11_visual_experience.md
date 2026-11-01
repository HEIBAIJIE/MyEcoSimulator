# F11 连续视觉与通用世界观察

状态：已完成（以下验证范围）。依赖 F10、F07、F08，规则 0.3、town-1、运行观察 F11 修订。替代 F10 中无动作及按食品工坊定制的展示边界，历史验收保留。

## 边界与实现

MapView 保留 Pixi Application、canvas、镜头与输入监听；数据和选中状态更新只重画场景，不重新初始化渲染器。区域切换过滤地形、建筑、树木及人物。main.tsx 根据快照列举区域和市场物品，使用新增 ObserverSnapshot.ItemTypes 映射市场标签，统计区域地面和随身库存；汇总全部企业现金，经济面板提供组织列表与区域市场。兼容场景企业名称使用真实标识，town-1 保留自身名称。

activity.ts 从最近历史读取意图和实际 action/intent 结果，失败优先，死亡和无历史分别标注。人物头顶展示行动，成功行动在运行时呈现原地节奏和姿态，暂停或减少动态效果时停止。动作只是已结算事实的示意，不增加通勤、寻路或经济行为。真实坐标与原始凭据仍可查询。

## 验证（2026-10-31）

- `npm run build --prefix web`：通过，保留既有大包提示。
- `dotnet test -c Release --filter 'FullyQualifiedName~TownTests|FullyQualifiedName~SpaceEnvironmentTests'`：9 项通过。
- `npx --prefix web playwright test --config web/playwright.config.ts game-world.spec.ts --timeout 45000`：通过，真实 MongoDB 开局、单步、详情、证据、镜头、时间控制、刷新恢复、390px 窄屏；增加轮询及人物选择前后 canvas 身份不变断言。
- `npx --prefix web playwright test --config web/playwright.config.ts visual-experience.spec.ts --timeout 45000`：通过，双区域、双企业、非食品物品类型与市场标签映射、区域库存和参考价、现金汇总、失败行为标记。此项采用接口 fixture，不宣称完整多区域经济回归。

不修改经济策略、人口行动选择与结算，不扩大 720 回合测试。当前仍重画场景子对象，未实现逐对象图形缓存；历史趋势保持既有场景汇总口径并明确说明，不是任意物品的独立历史曲线。

桌面及390px截图实际检查后，扩大同点人物展开间距，避免行动文字互相覆盖。截图见 web/test-results/f10-resident.png、f10-mobile.png。人物只在真实落点周围展开，不解释为世界移动。
