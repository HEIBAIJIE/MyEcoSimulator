# 移动、动作与生产

[返回总览](../00_index.md)

## 移动

### 区域与移动

人可以在一个区域内移动，也可以在区域间移动。

在区域内移动时，无论移动的次数或移动的距离，不消耗回合数。

在区域间移动时，消耗回合数。

### 移动与物品的获取

虽然一个人可以在一个区域内多次移动，但是他必须选择一个小地块作为落点。

一个人在一个回合中，只能与物品栏中的物品，或落点的物品进行交互。交互包括装进自己的物品栏，或者把物品从物品栏拿出来。装入和拿取本身，不视为动作。

当前阶段，一个人只能把属于自己的、属于自己组织的（在权限范围内）和无主的物品装进自己的物品栏。

无主的物品进入物品栏后，转变为自己的物品。

### 跨区域移动

当前阶段，跨区域移动的回合中，人视为留在当前区域的当前小地块。

无法装入物品栏的物品，当前无法跨区域搬运。

## 同回合移动与免费物品交互

一个主体在回合内声明一个区域内落点，并提交有序的免费物品交互意图。所有交互仍只能针对其物品栏或落点中的物品。系统按统一规则处理多人争抢、重复拾取和权属冲突，不按照主体遍历顺序自然决定胜负。

装入和拿出本身不占用主动作，但不意味着可以绕过所有权、组织权限、预留或物品位置检查。已被其他意图预留的物品不能被重复拾取、消耗或出售。

## 跨区域移动中的主体状态

跨区域移动期间冻结的是饱腹度与精力值等世界规则明确冻结的需求变化，不赋予主体额外动作或市场资格。到达前，人仍位于原区域原小地块，但不能利用该位置进行劳动、交易或其他主动作；到达结算完成后，才更新区域与落点。

## 动作

### 人与动作

每个回合，每个人可以执行一个动作，或者不执行动作。

不执行动作的人，处于闲置状态。

由于区域内移动不需要消耗回合数，所以区域内移动不属于动作。

一个动作可以持续一个回合，或者持续多个回合。

持续多个回合的动作可以被打断。打断后已完成的结算不回溯。

进食属于一种动作。一次进食可以进食任意数量的食物。

睡眠属于一种动作。

区域间移动属于动作，当前阶段，统一消耗二十四个回合，在过程中冻结饱食度与精力值。

### 劳动

各种劳动都属于动作。

大多数劳动都需要一定的条件。这种条件可以关于组织、职位、地形、地表实体、建筑、物品等等。

劳动可以有物品产出、地表实体产出或建筑产出。当前阶段，劳动按回合结算产出，不以不可分割物品的小数数量直接生成实物。多回合劳动的整数分摊按本篇规则执行。

每种劳动必须定义劳动产出的原料提供方、工具提供方、执行者、物权归属。

一部分劳动有工具的要求，要求劳动者携带或者劳动的小地块具备某种物品。

一部分劳动会对工具的耐久产生消耗，每回合扣除。

一部分劳动需要原料。当前阶段，原料按回合以完整整数单位扣除，不以不可分割原料的小数数量直接扣减。多回合劳动的整数分摊按本篇规则执行。

### 搬运

当物品不能被装进物品栏，又需要移动时，可以对其进行搬运。

当前阶段，搬运是一种动作，由一个人执行。

搬运发生时，人处在物品所在的小地块。搬运结束时，人与物品处于目标地块。

### 动作的加成

动作的产出可以由于时间、地点、小地块的物品等要素产生变化。

例如，睡眠时处于建筑物内、有床、处于夜晚有加成。

## 动作状态机

每个主动作至少区分以下状态：未开始、进行中、已完成、被打断、预检失败。系统应记录动作类型、开始回合、已完成回合数、目标进度、参与者、目标和本回合执行结果。

多回合动作默认继续，但每回合仍需通过合法性检查。动作条件失效时进入被打断或预检失败状态，已完成的结算不回溯，未执行部分不产生效果。

## 多回合劳动与资源释放

多回合劳动按回合投入、产出、支付工资和消耗耐久。每回合只预留该回合确定需要使用的资源；动作完成、中断或员工未实际参加劳动后，未使用的预留应在当回合结算中释放。

每回合劳动开始前重新检查执行者、原料、工具、地点、权限和工资条件。员工因生存紧急情况未参加企业劳动时，该员工当回合不计小时工资，其未使用工资预留不得转作其他支出后再支付。

## 工具与耐久结算

工具提供方和执行者可以不同，但使用权限必须明确。同一工具是否可被多个动作共享，由工具属性和场景规则明确；首个最小场景默认一个工具在一个回合内只能被一个劳动动作使用。

工具在回合开始预检时必须可用。当回合劳动效果与耐久消耗统一结算，耐久归零的物品在回合末清理，不因清理顺序重复参与其他动作。

## 多回合劳动的整数结算

首个最小场景的生产配方按一回合定义，投入和产出均为完整整数单位，不启用跨回合平均分摊。

通用多回合配方若只给出总投入或总产出 \(Q\) 和持续回合数 \(H\)，其中 \(Q\) 为非负整数、\(H\) 为正整数，则第 \(r\) 回合的累计应结算量定义为 \(\lfloor Qr/H\rfloor\)，本回合结算量为：

\[
\Delta_r=\lfloor Qr/H\rfloor-\lfloor Q(r-1)/H\rfloor
\]

该公式不得用于将总投入、总产出独立分摊后直接释放产品；此分摊只适用于已满足完整投入条件的独立步骤，具体执行须满足本篇生产步骤的投入前置约束。某回合 \(\Delta_r=0\) 时，不生成或扣除实物，只推进动作进度；所有回合的结算量之和恰好为 \(Q\)。每回合预检独立步骤的完整投入；不能仅用分摊后的投入量替代完整投入条件。中断仅保留已经满足投入前置约束的步骤。

## 生产步骤的投入前置约束

每个可释放产品的步骤必须定义完整原料、劳动和工具投入；全部满足后才原子扣除并生成对应产品。任意执行前缀不得产生尚未支付对应投入的可用产出。中断保留已合法完成的步骤；重新开始不得复用已消费投入或未兑现进度。

反例：总原料1、产品2、时长2，若投入和产出独立向下取整，会在首回合无原料地产出1件，反复中断重启即可套利。该配方解释无效，加载器必须拒绝。累计整数公式仅解决数量分摊，不证明投入时序合法。

当前可执行配方必须拆为一回合可独立结算的步骤，各步明确整数投入产出、工资和工时。无法拆分的配方待在制品、累计劳动成本、中断报废与回收规则完整定义后启用。最小食品配方每员工每回合独立完成，工资与产出同一原子事件确认。

production-1 执行边界：步骤数组长度等于时长，各步工资与产量之和等于配方总数，每步工时为1。每个产出步骤须有配方声明的全部材料类型及正整数用量；工具声明提供方、正整数耐久消耗和 Shared=false。不同产量步骤可以显式定义不同完整用量，不以总量分摊自动推断完整投入。任务在进行中持续公开，默认继续；中断后旧任务不再生效，新任务从首步开始，已消费投入不能复用。进度及失败、释放记录进入历史和恢复边界。
## 能力

当前阶段，不考虑个体间的智力、技能等差异。

## 关系

当前阶段，不考虑人与人的私人关系。

## 家庭

当前阶段，不考虑性别与家庭。

## 年龄与生育

当前阶段，不考虑年龄与生育。

## 物流

当前阶段暂不考虑区域内物流。


## space-1 timing

The starting turn is step 1. Travel started at t=0 arrives in stage 7 at t=23; trading resumes at t=24. Natural needs remain frozen on the arrival turn. Missing intents continue transport; an explicit different action interrupts it. Interruption and death leave the person at the origin; restarting begins at step 1. Travel cannot include local movement or free interactions.

HaulTurns must be an explicit positive integer. Hauling exclusively reserves the entire nonportable lot and rechecks origin and carry authorization each turn. Unowned hauling is not enabled. Person and lot move together only upon completion; interruption leaves both at the origin. Existing resource arbitration applies without reassignment of failed claims.

## environment-1 content and construction

Terrain cells sparsely override the scenario default. Unregionalized macro tiles retain the default; regional macro tiles do not duplicate terrain. Buildings require a full rectangle of artificial surfaces. Building ownership does not change component ownership. Initial people, lots, warehouses and tasks use the building anchor; local moves into other building cells normalize to the anchor.

Construction is one complete hourly labor-only step, with Output=1, explicit site condition, surface type, dimensions, building flag and RestBonus. Material or multi-step construction is rejected. Output belongs to the task company; wages are expensed without fictional inventory. Buildings and surfaces are not valued or traded. Stage 7 resolves spatial conflicts in actual commit order: first valid construction wins, later conflicts neither overwrite nor pay wages. New buildings cannot qualify another labor action in the same turn.

Construction normalizes occupants and lots to the anchor. Construction cannot cover non-anchor fixed warehouses, public task locations, or active transport endpoints. Site conditions are checked in stage 3. RestBonus is a nonnegative integer added once to actual stage-7 rest recovery, capped by the maximum; stage 8 does not apply it again.
