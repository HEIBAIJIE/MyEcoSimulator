# F07 多企业与多工种

状态：已完成（下述范围）。依据：规则0.3，扩展场景独立版本 organizations-1。依赖：F06。

## 目标与来源

在同一结算机制中启用多企业、多工种与完整分类授权，观察竞争。

来源：[组织资产](../spec/world-rules/02_entities_property.md)、[企业市场](../spec/world-rules/05_markets_accounting.md)、[决策边界](../spec/decision-rules/01_agent_decisions.md)、[仲裁](../spec/world-rules/06_settlement_history.md)。

## 实施路径

1. 多企业多工种各自配置股东、成员、员工、职位、固定工资、配方、仓库和策略，校验角色引用。
2. 区分查看保管拾取搬运出售消费授权，不从成员或股东身份推定；共享资源明确任务优先级、公平仲裁。
3. 复用统一市场及资源预留，处理同员工多任务但只执行一个主动作；观察界面按企业工种筛选现金、生产与竞争事件。
4. 配置两企业多工种有限场景，全部策略参数与资格显式记录，验证竞争与授权。

## 验收与验证

- 同区域同质商品统一出清，不因编号或遍历顺序获未定义优势，随机结果可查。
- 持股不自动可卖企业货，保管不自动可消费，多组织授权不串用；无共享规则拒绝加载。
- 同员工多任务只结算实际单一劳动，不重复工资或资源；死亡跳过轮换但稳定全集不重新编号。
- 无员工、无人出勤或暂停企业保留资产股东历史，权限失败、缺勤、工资不足与策略停产分别可查。
- 用越权、竞争、多任务和无人组织场景验证，回归基准及F06成本投入边界。

## 范围与扩展

固定地块仓库不变；不启用管理、自由择业、辞退、成立解散、融资分红或工资形成。多工种只配置固定工资和资格。

完成时填写：实现入口、场景参数和验证结果。

## 实现记录（2026-10-31）

实现入口：`Organizations.cs`（场景、工种配置校验、独立企业日计划与任务轮换）、`ScenarioLoader.cs`（角色、职位、授权、共享规则校验）、`Settlement.cs`（工资优先级、分类权限、缺勤结果）、`Recovery.cs`（world-organizations-1）、`web/src/main.tsx`（企业工种筛选、企业现金与产量、相关员工结果）。API Variant=`organizations`，界面可直接创建。

场景保持规则0.3、单区域固定仓库、同质食品；F与G分别持有资本200000、100000分，P00、P01分别持股；F包含原食品工种及packer（120分、产3件），G食品工种90分、产2件。P00同时有多工种及多企业任务。每企业显式保存安全库存、报价百分比、暂停开关；任务优先级和priority-rotation-1共享规则必须配置。策略公式及授权语义见 `spec/decision-rules/04_organization_strategy.md`。

明确边界：organizations-1只启用一小时独立配方；与production-1组合尚未支持，加载时拒绝，F06成本投入机制保留原入口。carry约束组织物品随人员区域内移动，大件搬运仍属于F08；不启用共享工具、管理、自由择业、辞退、成立解散或融资分红。职位可配置但不隐含权限。不以成员或持股关系推定出售、消费、拾取等授权。旧拾取测试补显式custody授权，以符合当前分类权限要求，未修改历史feature记录。

验证：OrganizationTests覆盖同员工多任务只付实际工种工资、多卖方统一价格与总量、持股不授予出售权、保管不授予消费权、跨组织授权隔离、携带权限、共享规则缺失拒绝、工种引用拒绝、死亡跳过稳定全集、空员工及暂停组织存续、恢复继续与72回合守恒。`dotnet test --nologo --filter 'FullyQualifiedName~OrganizationTests|FullyQualifiedName~PersistenceTests.RealReplicaSet'` 6/6通过；此前核心回归43/43通过（不含持久化测试，随后新增两个专项用例亦通过）。

`dotnet test --no-build --nologo --filter 'FullyQualifiedName~PersistenceAcceptanceTests.FullRun&DisplayName~organizations'` 1/1通过：最新每日计划实现完成MongoDB 720回合，中途恢复、分页与全程现金/账实检查通过；耗时32634ms，恢复17ms，分页71ms，托管内存240回合8777576字节、720回合46716600字节，进程峰值223121408字节，数据库storageSize=16306176字节。本机Windows/.NET8/本地27018副本集，不作吞吐承诺，F04A既有限制继续有效。

`npm run build`通过，存在原有大包提示；`npx playwright test e2e/organizations.spec.ts` 1/1通过，验证真实浏览器创建、企业工种筛选、单步、刷新和无页面异常。一次组合回归43/44通过，原有RealReplicaSet因memory-budget暂停，单独复测通过；不将该组合结果表述为全绿。后续遵循AGENTS.md按变更相关范围验证，不要求每个feature全场景720回合回归。
