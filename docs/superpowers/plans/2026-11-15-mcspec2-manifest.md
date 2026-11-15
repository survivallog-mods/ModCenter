# mcSpec 2 声明式配置清单实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 套件 mod 面板页定义从 ModCenter 硬编码（Meta.Mods + panel.js USAGE/UNCOMMON_CABS 表）迁移到各 mod 内嵌的 mcSpec 2 清单，配置变更零接触 ModCenter。

**Architecture:** Tier 1 `ThirdParty` 解析器升级为 mcSpec 1/2 通用层；ConfigBridge 套件页三路由（清单页→Meta 冻结页→roster 未安装最小行）；Payload 契约只增（usage/noToggle/cabs）；panel.js 四处受控改动（数据源切换+兜底降级）。先迁 FridgeChill 全链路验证，再批量迁其余 8 mod，最后删旧配置代码。

**Tech Stack:** BepInEx (net6.0, IL2CPP)、C# 手写 JSON（AOT 零反射出向 / JsonDocument 入向）、内嵌资源（EmbeddedResource）、原生 JS 面板（Vuplex WebView 注入）。

**Spec:** `docs/superpowers/specs/2026-11-15-modcenter-mcspec2-manifest-design.md`（含评审裁定记录，schema/语义以 spec 为准，本计划不重复字段表）

## Global Constraints

- **仓库边界**：ModCenter 改动提交在 `E:\Game\Mod\survivallog-mods\ModCenter`；各 mod 清单提交在各 mod 仓（如 `E:\Game\Mod\survivallog-mods\FridgeChill`）；集成指南提交在 `E:\Game\Mod\survivallog-mods\suite`。commit 格式 `<type>：<描述>`（技术名词保留英文）。
- **前置**：工作区现有未提交的 ucb 改动（Meta.cs/ConfigBridge.cs/Payload.cs/panel.js/CHANGELOG）必须先由作者提交，本计划基于其之上。
- Payload 快照契约只增不改：新字段 `usage`/`noToggle`/`cabs` 空值不输出（旧面板互炸防护）。
- mcSpec 1 行为冻结：清单 min/max/opts 覆盖反射、键不在 cfg 一律隐藏、Tier 2 `McSpec==1` 继续有效（门放行 {1,2} 是扩展）。
- mcSpec 2 分叉语义：反射优先清单兜底；entry 缺失且有 type+def 兜底展示，无兜底隐藏（spec §3）。
- 扩展字段（enabled/lists/writeKeys/linkedSuffix/vis/cabs/ucb）仅套件成员（`Meta.IsSuiteGuid`）生效，第三方出现→搁置+日志。
- 容错"绝不报错"：解析/校验失败只降级+`Log`，不抛异常。
- 线程纪律：清单解析一次入缓存，只在主线程（Pump/BuildSnapshot）。
- 写路由白名单：TrySet 只写清单/Meta 声明过的键。
- panel.js 控件集不变；改动仅限 spec §4.5 四项。
- 行数上限：新增/改动的 C# 模块 500 行内、工具函数 300 行内（存量 ConfigBridge.cs 2750 行超限只提示不拆）。
- DailySurprise（临时登记）不迁移、不进 roster、Meta 条目保留。
- 游戏部署路径：dll 复制到 `E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\`；日志看 `BepInEx\LogOutput.log`；面板入口=主菜单尾部「Mod 管理中心」。

## Review Focus

1. **旧版 dll（无清单）+ 新 ModCenter**：必须走 Meta 冻结页且与旧版面板逐项一致（k/l/t/min/max/st/b/note）——阶段 1 "零行为变化"承诺。→ Task 7 真机回归；Task 9 步骤 5 反向验证。
2. **mcSpec 1 第三方清单行为回退**：清单覆盖反射、键缺失隐藏不得被统一 BuildItem 改掉。→ Task 1 语义分叉实现 + Task 7 真机（Tier 1 mod 定制页范围不变）。
3. **未安装行兜底**：dll+cfg 均缺席时 roster 最小行（未安装/去仓库安装），不得从列表消失。→ Task 4 + Task 9 步骤 6。
4. **坏清单绝不报错**：坏 JSON/mcSpec 不识别/缺 groups/键不在 cfg → 降级+日志，游戏与面板不崩。→ Task 1 实现 + Task 15 步骤 4（临时坏清单探针）。
5. **过渡期数据源降级**：未迁移 mod 的 usage 回落 JS 表、旧 dll 的 ucb 项回落 UNCOMMON_CABS。→ Task 5 实现 + Task 7 回归（CabinetEverywhere 旧 dll 的不常用柜子下拉仍可用）。

**已知的验收盲区（明示风险）**：`vis` 条件显示、`writeKeys` 联动写、`bagOverride`、`enabled:"none"` 四个机制在 DailySurprise 排除迁移后**没有真实消费者**——真机验收覆盖不到，仅由 Task 1/3 实现审查与解析日志保障。DailySurprise 转正迁移时这四项随迁并用真机补验。

---

### Task 1: 解析层 mcSpec 2（ThirdParty.cs）

**Files:**
- Modify: `SurvivalLog.ModCenter/ThirdParty.cs`

**Interfaces:**
- Consumes: 现有 `McManifest`/`McGroup`/`McItem`、`ManifestOf`/`Parse`/`FindResource`、`NormBadge`/`Split`。
- Produces（后续任务依赖的精确形状）:
  - `internal const int McSpecSupported = 2` 语义改为"最高认识版本"；新增 `internal static bool SpecKnown(int v)` → `v == 1 || v == 2`。
  - `McManifest` 扩：`internal string En; internal McUsage Usage; internal McEnabled Enabled; internal List<McList> Lists;`
  - `internal sealed class McUsage { internal string Intro; internal List<McUsageSec> Sections; }` + `McUsageSec { string Heading; List<string> Bullets; }`
  - `internal sealed class McEnabled { internal string Section; internal string Key; internal bool None; }`（JSON `"none"` → None=true）
  - `internal sealed class McList { internal string Kind; internal string Key; internal string Label; internal string Badge; internal string Note; }`
  - `McItem` 扩：`internal string Type; internal object Def; internal List<string> Opts; internal string[] WriteKeys; internal string LinkedSuffix; internal McVis Vis; internal List<McCab> Cabs;`
  - `internal sealed class McVis { internal string Key; internal string Value; }` / `internal sealed class McCab { internal long Id; internal string Name; internal double Native; }`
  - `Parse` 按版本分叉：v1 只认旧字段集；v2 全量。任何不合格 → null 降级（现纪律）。
  - T2 门 `T2Of`：`McSpecSupported` 常量引用改为 `SpecKnown(...)` 放行 {1,2}。
  - 套件判定挂钩：`Apply` 增参 `bool suite`（ConfigBridge 传 `Meta.IsSuiteGuid(md.GUID)`），非套件清单出现扩展字段 → 记日志搁置（解析保留，Apply 不消费）。

- [ ] **Step 1: 实现 Parse v2 字段集与版本分叉**（坏输入全降级 null；v1 路径行为逐字节不变）
- [ ] **Step 2: 实现扩展结构体与 `SpecKnown`/T2 门改动**
- [ ] **Step 3: 构建门**：`dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release` → 0 error（新字段尚无消费者会有 unused 警告，可接受）
- [ ] **Step 4: Commit（ModCenter 仓）**：`feat(mc)：清单解析层升级 mcSpec 2——字段集/版本分叉/T2 门放行 {1,2}/套件扩展字段挂钩（阶段 1）`

### Task 2: Payload 契约扩展（Payload.cs）

**Files:**
- Modify: `SurvivalLog.ModCenter/Payload.cs`

**Interfaces:**
- Produces:
  - `ModInfo` 增 `public string UsageJson;`（手写序列化：usage 为对象 `{intro,sections:[{h,ls[]}]}`，null 不输出——字段名沿用现 USAGE 渲染期望 `i`/`secs`/`h`/`ls`，panel 侧改动最小）与 `public bool? NoToggle;`（true 输出 `"noToggle":true`）
  - `ItemInfo` 增 `public List<CabInfo> Cabs;` + `public sealed class CabInfo { public long Id; public string Name; public double Native; }`（空不输出；id 数字/name 串/native `0.###`）
  - `TypeName` 不动（ucb 由 ConfigBridge 直通 T，不走 TypeName）

- [ ] **Step 1: 实现三个字段的 Write 分支**（空值严格不输出，转义走 `JsonW.Str`）
- [ ] **Step 2: 构建门**：同 Task 1 命令 → 0 error
- [ ] **Step 3: Commit**：`feat(mc)：快照契约扩展——usage/noToggle/cabs 三可选字段（空不输出，只增不改）`

### Task 3: ConfigBridge 三路由与统一 BuildItem

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（BuildSnapshot 套件循环、BuildItem、TrySet/Resolve/FindItem、SetModEnabled；新增代码注意行数预算，超出则把清单合并逻辑放 ThirdParty 侧）

**Interfaces:**
- Consumes: Task 1 的 McManifest 全结构、Task 2 的 Payload 字段、`Meta.IsSuiteGuid`、现有 `Entry`/`ApplyRange`/`ReadOvr`/`ReadBagOvr`/`ActionCatalog.Load`/`LinkedNote`。
- Produces:
  - `BuildSnapshot` 套件循环三路由：①dll 在链上且 `ManifestOf` 命中 → 清单页（新 `ThirdParty.BuildSuitePage(MetaMod 兜底元数据, McManifest, ConfigFile)` 返回填好的 ModInfo，或等价内联实现——落点实现自选，spec §6）；②dll 在链上无清单 → 现 Meta 路径原样；③dll 不在 → Task 4 的 roster 最小行。
  - 统一 `BuildItemFromManifest(McItem it, ConfigFile cfg)`：类型/值走 entry 反射，entry 缺失→`type`+`def` 兜底展示、无兜底→隐藏；min/max/opts 反射优先清单兜底；step/vis/writeKeys/linkedSuffix/cabs/note/badge 按清单；`type:"ucb"` 直通 `T` 并透传 cabs。
  - `lists` 处理：kind=actionOverride → 现 ReadOvr + `ActionCatalog.Load()` 填 Ovr/Acts；kind=bagOverride → ReadBagOvr 填 BagOvr。
  - `enabled`：清单声明（缺省 General/Enabled；"none" → NoToggle=true 且 SetModEnabled 拒绝+日志）。
  - `Resolve`/`FindItem`：清单条目索引（section+writeKeys 并集）→ Meta 条目 → tp，白名单纪律不变。
  - `HandleSet` 回执 badge：`ThirdParty.BadgeOf` 扩为按 guid 通用查表（套件清单页也命中）。

- [ ] **Step 1: 实现三路由 + 清单页构建**（Meta 路径代码零改动）
- [ ] **Step 2: 实现清单写路径（FindItem/白名单/enabled/noToggle）**
- [ ] **Step 3: 构建门**：`dotnet build ... -c Release` → 0 error
- [ ] **Step 4: 真机冒烟（此时无任何 mod 带清单，全部走②路由）**：复制 ModCenter.dll 到游戏 plugins → 启动游戏 → 面板打开 → 全部套件页与旧版一致（视觉对照）+ `LogOutput.log` 无"清单"命中行、无异常
- [ ] **Step 5: Commit**：`feat(mc)：ConfigBridge 套件页三路由——清单页/Meta 冻结兜底/统一 BuildItem（mcSpec 2 分叉语义）`

### Task 4: roster 与未安装最小行

**Files:**
- Modify: `SurvivalLog.ModCenter/Meta.cs`（新增 Roster 表；不动 Mods 表）
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（路由③）

**Interfaces:**
- Produces:
  - `Meta.Roster`：`internal sealed class RosterEntry { public string Guid; public string En; public string Name; public string Desc; }`，9 条（FridgeChill/QuickAction/CabinetEverywhere/UnlockHub/RobotSpeed/PlanPick/SaveMgr/PreDisasterSpeed/EndlessGuard——Name/En/Desc 从现 Meta.Mods 对应条目逐字复制；仓键经 `RepoDecision.GuidOf` 反查，不重复存）。
  - 路由③：dll 不在（CfgCache null 且清单缓存 none）→ `ModInfo{Guid,Name,En,Desc,Ver=null,Missing=true,Groups=[],Enabled=false}`；DailySurprise 例外：保留现 Meta 行为（在 Meta.Mods 即走旧路径）。
  - `Meta.DisplayNameOf` 兜底链：Mods → Roster → ExtraNames。

- [ ] **Step 1: 写 Roster 表（9 条逐字转写）+ DisplayNameOf 扩展**
- [ ] **Step 2: 实现路由③**（判定注意：mod 停用 dll 仍在链上=不进此路由）
- [ ] **Step 3: 构建门 + 真机冒烟**：卸载态验证留到 Task 9（本任务只验"已装 mod 不误入路由③"——面板全部行正常）
- [ ] **Step 4: Commit**：`feat(mc)：套件成员 roster——未安装最小行兜底+显示名兜底链`

### Task 5: panel.js 四处受控改动

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`

**Interfaces:**
- Consumes: Task 2 的快照字段（`usage`/`noToggle`/`cabs`，形状 `{i,secs:[{h,ls[]}]}` 与现 USAGE 内部结构一致）。
- Steps:
- [ ] **Step 1: usage 双源**：`usageOf(guid, mod)` = 快照 `mod.usage` 优先 → `USAGE[guid]` 兜底 → null；两消费点（usageBtnHtml/usageModalHtml、仓库页 usageIntroHtml）与 `__self` 静态条目改经此函数
- [ ] **Step 2: ucb 白名单双源**：`ucbCabsOf(it)` = `it.cabs` 优先 → `UNCOMMON_CABS` 兜底（id/name/native 映射到现 {id,n,h} 形状）；`ucbDdShow`/`ucbCabOf`/`ucbHtml` 改经此函数
- [ ] **Step 3: 未安装行空组渲染**：`m.missing && m.groups.length===0` → 详情区渲染"未安装，去仓库安装"引导（点击路由复用现仓库页安装入口）
- [ ] **Step 4: noToggle 锁定**：`m.noToggle===true` → 行开关渲染锁定态（禁用+title 说明），不发 MC_TOGGLE_MOD
- [ ] **Step 5: 真机冒烟**：部署 → 面板全功能过一遍（详情/仓库页/使用说明弹窗/不常用柜子下拉——旧 dll 全部走兜底，行为与现在一致）
- [ ] **Step 6: Commit**：`feat(mc)：面板数据源双源切换——usage/cabs 快照优先 JS 表兜底+未安装行引导+noToggle 锁定`

### Task 6: 集成指南 mcSpec 2 章节（suite 仓）

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\suite\docs\modcenter-integration-guide.md`

- [ ] **Step 1: 新增 mcSpec 2 章节**：schema 字段表逐字从 spec §1 转写；明确三点差异（区间优先级/键缺失语义/扩展字段仅套件生效——spec §3 与约束节）；分类排序软约定（10~19 核心开关/20~39 常用数值/40~69 低频细调/90+ 调试）；csproj `<EmbeddedResource Include="modcenter.json" />` 片段；Tier 2 McSpec 门放行 {1,2} 注记
- [ ] **Step 2: Commit（suite 仓）**：`docs：ModCenter 集成指南增 mcSpec 2 章节（schema/分叉语义/适用范围/排序约定）`

### Task 7: 阶段 1 真机回归（零行为变化门）

**Files:** 无新改动（纯验收；发现问题回上游任务修）

- [ ] **Step 1: 部署**：构建 ModCenter.dll + MCShell（`release.ps1` 同款 `dotnet build -c Release`）→ 复制到游戏 plugins/patchers → 启动游戏
- [ ] **Step 2: 回归清单（全部旧 dll，预期全部走 Meta 冻结路由②）**：10 个套件页逐页视觉对照旧版（分组/项/文案/徽标/区间）；动作加速 ovr 编辑器；更好的储物柜 ucb 下拉（13 柜全在=UNCOMMON_CABS 兜底生效）；仓库页安装确认的功能说明段（USAGE 兜底生效）；第三方 Tier 1 mod 定制页不回退（无则查日志无降级行）
- [ ] **Step 3: 日志断言**：`LogOutput.log` 无 `清单(tp) ... mcSpec 不识别/解析失败`、无异常栈
- [ ] **Step 4: 通过标准**：与当前版本行为零差异。失败 → 回 Task 1-5 修复后重跑本任务

### Task 8: 试点 FridgeChill 清单

**Files:**
- Create: `E:\Game\Mod\survivallog-mods\FridgeChill\SurvivalLog.FridgeChill\modcenter.json`
- Modify: `E:\Game\Mod\survivallog-mods\FridgeChill\SurvivalLog.FridgeChill\SurvivalLog.FridgeChill.csproj`（ItemGroup 加 `<EmbeddedResource Include="modcenter.json" />`）

**Interfaces:**
- Produces: FridgeChill dll 内嵌清单（资源实名 `SurvivalLog.FridgeChill.modcenter.json`，Tier 1 发现规则命中 `*.modcenter.json` 后缀）。

- [ ] **Step 1: 写 modcenter.json**——内容三处来源逐字转写：`display/en/summary` ← Meta.cs FridgeChill 条目；`usage` ← panel.js USAGE 表 fridgechill 条目（i→intro、secs→sections，h→heading、ls→bullets）；groups（1 组 2 项：Chill:KeepMult float 1~50 st0.5 badge now；Chill:MaxTotalMult float 1~500 st1 badge now + note"启动时字节补丁安装失败则置灰"）。**只列面板暴露的 2 键**——Chill:BagIds/Debug:Verbose 不进清单（清单没列的键不出现，与现面板一致）
- [ ] **Step 2: csproj 加 EmbeddedResource 行**
- [ ] **Step 3: 构建**：`dotnet build -c Release` → 检查产物资源在（如 `dotnet build` 后用 ILSpy/`strings` 抽查或直接进真机验收）
- [ ] **Step 4: Commit（FridgeChill 仓）**：`feat：内嵌 ModCenter mcSpec 2 配置清单（面板页定义自维护，阶段 2 试点）`

### Task 9: 试点真机验收（全链路）

**Files:** 无（纯验收）

- [ ] **Step 1: 部署**：新 ModCenter.dll + 新 FridgeChill.dll → plugins → 启动
- [ ] **Step 2: 清单路由命中**：`LogOutput.log` 含 `清单 ... 命中资源 'SurvivalLog.FridgeChill.modcenter.json'`/`定制页生效`；面板保鲜增强页 = 2 项（标签/徽标/区间/步进/说明逐项对照旧版一致——等价判据：k/l/t/min/max/st/b/note 八字段）
- [ ] **Step 3: 写入链**：拖动"保鲜时长倍率"→ 提交 → `BepInEx\config\com.local.survivallog.fridgechill.cfg` 文件值同步变化 + toast 成效徽标 now；进档抽查保鲜时长生效（游戏内真实场景）
- [ ] **Step 4: usage 链**：详情页「使用说明」弹窗内容 = 清单 intro/sections（非 JS 兜底——可临时改 JS 表一个字验证来源，验完还原）
- [ ] **Step 5: 回退路由**：换回上一版 FridgeChill.dll（无清单）→ 重启 → 页面仍完整（Meta 冻结路由②，八字段一致）——阶段 1 承诺的反向验证
- [ ] **Step 6: 未安装路由**：移走 FridgeChill.dll+cfg → 重启 → 保鲜增强行变最小行（未安装/去仓库安装，点击直达安装确认），无置灰配置页；恢复 dll+cfg 验证可还原
- [ ] **Step 7: 通过标准**：六步全过 → 试点模式定版，进入 Task 10-13 批量；任何失败回 Task 1-8 修复

### Task 10: 简单批四 mod（PlanPick / SaveMgr / PreDisasterSpeed / EndlessGuard）

**Files（每 mod 同 Task 8 三件套：Create modcenter.json、Modify csproj）:**
- `E:\Game\Mod\survivallog-mods\PlanPick\SurvivalLog.PlanPick\`
- `E:\Game\Mod\survivallog-mods\SaveMgr\SurvivalLog.SaveMgr\`
- `E:\Game\Mod\survivallog-mods\PreDisasterSpeed\SurvivalLog.PreDisasterSpeed\`
- `E:\Game\Mod\survivallog-mods\EndlessGuard\SurvivalLog.EndlessGuard\`

- [ ] **Step 1: 四份清单逐字转写**（Meta.cs 对应表 + panel.js USAGE 对应条目；PreDisasterSpeed=空 groups 仅启停；EndlessGuard 三项含 note；PlanPick CostMultiplierPercent 不写 min/max——Bind 自带 AcceptableValueRange 反射为准）
- [ ] **Step 2: 四仓各自 csproj 加 EmbeddedResource**
- [ ] **Step 3: 构建四仓 + 部署 + 真机**：日志四条命中行；四页八字段对照；各页任写一项验 cfg 落盘；PreDisasterSpeed 验启停链（General.Enabled）
- [ ] **Step 4: 四仓分别 Commit**：`feat：内嵌 ModCenter mcSpec 2 配置清单`

### Task 11: UnlockHub / RobotSpeed

**Files（同三件套）:**
- `E:\Game\Mod\survivallog-mods\UnlockHub\SurvivalLog.UnlockHub\`（注意仓曾名 RoleUnlock，目录名以实际为准）
- `E:\Game\Mod\survivallog-mods\RobotSpeed\SurvivalLog.RobotSpeed\`

- [ ] **Step 1: 两份清单转写**（UnlockHub 6 组 9 项含两条 reload/reboot badge 与 note；RobotSpeed 1 组 7 项，MultItem 五键+货舱+对齐开关——节名"机器人提速"是中文 cfg 节，key 格式照 `"机器人提速:移速倍率"`，冒号分隔规则不受中文影响）
- [ ] **Step 2: csproj + 构建 + 部署 + 真机八字段对照与写入抽验**
- [ ] **Step 3: 两仓 Commit**（同格式）

### Task 12: QuickAction（lists: actionOverride）

**Files（同三件套）:** `E:\Game\Mod\survivallog-mods\QuickAction\SurvivalLog.QuickAction\`

- [ ] **Step 1: 清单转写**：groups=分类耗时 5 项（MsItem 形：int 0~5000 st50 def500 badge now）；`lists: [{kind:"actionOverride", key:"其他:Overrides", label:"动作覆盖清单", badge:"now"}]`
- [ ] **Step 2: csproj + 构建 + 部署 + 真机**：ovr 编辑器渲染、动作目录下拉（acts 来自 QuickActionCatalog.txt，链路不变）、增删覆盖行→cfg `"id:ms"` 整串写入、Ms 五项写入
- [ ] **Step 3: Commit（QuickAction 仓）**

### Task 13: CabinetEverywhere（ucb/cabs + 15+2 项）

**Files（同三件套）:** `E:\Game\Mod\survivallog-mods\CabinetEverywhere\SurvivalLog.CabinetEverywhere\`

- [ ] **Step 1: 清单转写**：高度组 15 项（Rail:BackpackHeight 0~30 + 14 个 Height_* 0~100，含各自 note）+ ucb 项 `Rail:UncommonHeights type:"ucb" def:""` + `cabs` 13 条（id/name/native ← panel.js UNCOMMON_CABS 逐字，含注释里的共用说明进 note）+ 负重组 2 项（Badge reload）
- [ ] **Step 2: csproj + 构建 + 部署 + 真机**：ucb 下拉 13 柜来自快照 cabs（可临时改 JS 表一字验证非兜底）；加行/滑调/删行→cfg `"BagId:行数,..."` 写入；15+2 项八字段对照；cabs 直传后 UNCOMMON_CABS 仍保留（兜底）
- [ ] **Step 3: Commit（CabinetEverywhere 仓）**

### Task 14: 阶段 3 清理——旧配置代码归零

**Files:**
- Modify: `SurvivalLog.ModCenter/Meta.cs`（删 9 张已迁移 MetaMod 块 + MsItem/MultItem 助手；保留 DailySurprise 块、MetaItem/MetaGroup/MetaMod 类、KnownConflicts/Domains/SuiteGuids/ExtraNames/Roster）
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（路由②收窄为 DailySurprise 专用；无清单且非 DailySurprise 的在链套件 dll → Tier 0 通用页同款降级，日志说明——防旧 dll 玩家拿到空白页）
- Modify: `SurvivalLog.ModCenter/panel.js`（USAGE 表清空仅剩 `__self` 条目；UNCOMMON_CABS 删除，ucbCabsOf 只读快照）

**Interfaces:** Payload 契约不动；Meta.Mods 剩 DailySurprise 一条后 `DisplayNameOf`/`TpExempt` 继续成立（Roster 补位）。

- [ ] **Step 1: 删 Meta 9 表与助手、ConfigBridge 路由②收窄**
- [ ] **Step 2: panel.js 两表清理**
- [ ] **Step 3: 构建 + 部署 + 真机**：9 页仍全部来自清单（日志命中行）；DailySurprise 页照旧（Meta 路由）；构造旧 dll 场景（拿任一 mod 上一版 dll 覆盖回去）→ 该 mod 降级 Tier 0 通用页不空白、日志说明行；恢复新 dll
- [ ] **Step 4: Commit（ModCenter 仓）**：`refactor(mc)：旧配置代码归零——Meta 九表退役/USAGE·UNCOMMON_CABS 清空（清单全面接管，DailySurprise 临时登记保留）`

### Task 15: 终验收（真实场景全链路）

- [ ] **Step 1: 全量部署**：ModCenter + MCShell + 9 mod 新 dll 全套 → 冷启动
- [ ] **Step 2: 全页清单驱动断言**：日志 9 条命中行、零降级行、零异常；10 个套件页（9 清单 + DailySurprise）逐页八字段终对照（对照基线=本计划执行前的截图/记录）
- [ ] **Step 3: 场景抽验**：任一 mod 写入→cfg 落盘→进档生效；启停链（General.Enabled 热生效）；仓库页检查更新→安装确认（usage 来自清单）；未安装行（移走任一 mod dll+cfg）→ 最小行→仓库安装直达；排序模式/冲突弹窗/待生效底条等既有功能回归
- [ ] **Step 4: 坏清单探针**：临时把 FridgeChill modcenter.json 改坏（mcSpec:99 / 缺 groups / 语法错）重新内嵌部署 → 该 mod 降级（在链旧语义：Tier 0 通用页）、日志记因、游戏面板不崩 → 恢复
- [ ] **Step 5: 通过标准**：全部通过 → 阶段 3 完成，mcSpec 2 上线；遗留（DailySurprise 转正、发版）另行走生存周期能力

## 发版不在本计划内

ModCenter vNext 与 9 mod 的发版/版本台账走 survivallog-release 既有流程（作者发起）；本计划止于 Task 15 验收通过。
