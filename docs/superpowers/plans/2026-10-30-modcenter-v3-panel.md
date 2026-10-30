# ModCenter 面板 v3（文案精简 · 冲突两级 · 更新链重做）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 prototype.html 定稿的 v3 方向回写进 panel.js 与 C#：冲突两级化（红严重/橙普告警，gray 退役）、删独立仓库页（顶部检查更新 + 列表图标 + 详情维护区）、全部玩家文案逐字精简。

**Architecture:** 机制零改动（pending/清理器/下载链/协议结构原样），只动呈现层与文案：C# 侧 Meta.cs 冲突表 + ConfigBridge ics/卡片/提示产出 + MC_UPDALL 退役；panel.js 渲染函数按 spec §5.4 逐字回写；原型一致性核对后重写 DESIGN.md 相应章节，最后实机 CDP 回归 + 套件夹具同步。

**Tech Stack:** C# / .NET 6（BepInEx 插件，零反射手写 JSON）、panel.js（ES5，内嵌资源）、CDP 9222 实机断言 + 套件仓 `suite/tools/mc_fixture.js` 回归。

**Spec:** `docs/superpowers/specs/2026-10-30-modcenter-v3-panel-design.md`（本文所有"逐字"均指向它 §5.2/§5.4；执行者两份都要读）

## Global Constraints

- 版本号不动：csproj `<Version>`、`ModCenterPlugin.Version` 一律不改（作者定号，硬规则 1）。
- 文案逐字：spec §5.2（L1 两条件文）与 §5.4（文案总表）为准，不得同义改写；未列入的文案不动。
- 兼容纪律（spec §5.6）：◎ 必须带 `G="◎"`；panel.js `icGlyph` 的 gray 分支保留为 legacy；
  新字段（`TipShort`/`Tag`）只增不改协议结构。
- 机制不变：主线程唯一写者、无后台轮询、15 分钟缓存/60 秒冷却、MC_REPO_DATA 照推、
  下载链与 pending 写入原样。
- 同步纪律：本计划完成后 prototype.html 与 panel.js 渲染逐字一致（同分支内核对，Task 5）。
- 行号为参考锚点，以函数名/字符串 grep 为准（文件会漂移）。
- 项目无单测框架：每任务的"测试"= `dotnet build` 通过 + Task 6 实机 CDP 断言（无实机环境时
  CDP 步骤显式 SKIP 并记录，不得静默跳过）。

## Review Focus

1. **旧面板 + 新 C#**：◎ 字形靠 `G="◎"` 显式传递——若漏传，旧面板按 orange+cf 推断成 ⇄，
   功能叠加会显示成脚本碰撞。→ Task 1 步骤 3 断言 + Task 6 CDP 断言。
2. **新面板 + 旧 C#**：旧快照仍发 `k:"gray"`——`icGlyph` 删掉 gray 分支会渲染成 ⚠ 兜底。→
   Task 4 步骤 5 grep 断言 legacy 分支仍在。
3. **仓库数据缺席时点图标**：`MC_REPO_DATA` 未到/检查失败时点行内 ↑ → 必须回退 toast
   `暂无更新信息`，不得弹空壳或抛错。→ Task 3 步骤 8 + Task 6 CDP。
4. **下载中关弹窗/面板**：`onDlEvent` 删仓库行按钮块时不得破坏 `S.mdl` 判空与
   `S.mdlRepoKey` 归属判定（进度弹窗就地更新路径）。→ Task 3 步骤 9 + Task 6 CDP。
5. **退役入口残留**：`MC_UPDALL`/`repo-open`/`patchscan`/`back|chk|repogoto` 删除后，
   任何残留 DOM 属性或事件分支都是死代码或孤儿点击。→ Task 3/4 步骤内 grep 断言零引用。

---

### Task 1: C# 冲突两级化与 L1/L2/L3 提示重写

**Files:**
- Modify: `SurvivalLog.ModCenter/Meta.cs`（ConflictEntry 字段 + KnownConflicts 两条件文，:62/:499-524）
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（CfIcsFor :2025、L1 波及行 :465、cf 卡片拼装 :2063-2122、L2 行内橙标 tip 拼装点）
- Build: `SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj`

**Interfaces:**
- Consumes: 现有 `IcInfo{K,Tip,Click,G,W}`、`Meta.DisplayNameOf`、`Meta.Domains`（均不改签名）。
- Produces（Task 3/4 的 panel.js 与 Task 6 断言依赖）:
  - `IcInfo` 只产出 `K ∈ {red, orange, blue, amber}`；◎ 一律 `G="◎"`；
  - L1/L3/L2 行内 Tip 与 cf 卡片 `{lv,tag,title,who,reason,tips}` 语义 = spec §5.1/§5.2；
  - `ConflictEntry` 新字段 `TipShort`、`Tag`（string，可空——L2/L3 卡不走表）。

- [ ] **Step 1: Meta.cs — ConflictEntry 加字段并逐字重写两条件文**

`ConflictEntry` 增加 `public string TipShort;` 与 `public string Tag;`（XML 注释注明：TipShort=行内短提示逐字；Tag=弹窗卡级标签，仅 L1 表内条目使用）。条目 1/条目 2 的 TipShort/Tag/Title/Reason/Tips 按 spec §5.2 逐字录入；条目 2 `Level = "gray"` 改 `"orange"`；karina 排除注释原样保留。

- [ ] **Step 2: ConfigBridge.cs — ics 产出两级化**

`CfIcsFor`（:2025 起）及其 L1 波及分支（:465）：L1 行内 tip 改 `e.TipShort`；L3 分支改
`K="orange", G="◎", Click="cf", Tip=$"功能叠加：{域名}类 mod 效果会叠加，点看详情"`（域名取
`Meta.Domains` 中文值）；确认 `K="gray"` 在本文件不再产出（grep 断言）。L2 行内橙标 tip
拼装点（grep `脚本碰撞` 不中则 grep `PatchScan._last` 的快照消费处）改
`脚本碰撞 ×{N}：与{对方显示名}，点看详情`（N=该 mod 命中组数；对方=组内其他 owner 的
`OwnerDisplay`，取前 2 个）。cf 卡片拼装（:2063-2122）：`Tag=e.Tag`、`Title=e.Title`、
`Reason=e.Reason`（空串原样传出，面板负责省略段）；L2 卡 title=`和「{对方}」改了同一处游戏逻辑（{组数} 处），后加载的生效，行为可能异常。`、reason 空、tips=spec §5.2 两条；L3 卡 tag=`功能叠加`、title=`这几个 mod 都在改{域}数值，效果会叠加，可能超出预期。`、tips=`介意叠加就只留一个`；卡片排序 red→orange（gray 档删除）。

- [ ] **Step 3: 构建与静态断言**

Run: `grep -n '"gray"' SurvivalLog.ModCenter/ConfigBridge.cs SurvivalLog.ModCenter/Meta.cs`
Expected: 仅剩历史注释，无产出 `K="gray"`/`Level="gray"` 的活动代码。
Run: `dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release`
Expected: Build succeeded，0 warning 新增。

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.ModCenter/Meta.cs SurvivalLog.ModCenter/ConfigBridge.cs
git commit -m "feat(ModCenter): v3 冲突两级化——L1 逐字重写 + L3 并橙显式◎ + L2 tip/卡片精简（C#）"
```

### Task 2: C# 更新链精简（UPDALL 退役 · 图标 tip · owners 显示名）

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（RepoIcs 系列 tip :1066-1106、MC_CHK 冷却 toast :1111-1122、`case "MC_UPDALL"` :2282、HandleUpdAll :1184、pendingTip）
- Modify: `SurvivalLog.ModCenter/PatchScan.cs`（MC_SCAN_DATA owners 产出核对，:136 事件拼装）
- Build: `SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj`

**Interfaces:**
- Consumes: Task 1 的色级口径（蓝↑/橙↓/⊘ g=kbw 不变，仅 Tip 文案变）。
- Produces（Task 3 依赖）: `MC_UPDALL` 不再被处理（panel 端不得再发）；`MC_SCAN_DATA` 的
  `items[].owners[]`/`frameItems[].owners[]` 为**显示名**（复用 `OwnerDisplay`，含
  `未知来源（id）`）；行内 Tip 逐字 = spec §5.3（update/install/downgrade/⊘/⟳ 五条）。

- [ ] **Step 1: 图标 Tip 逐字替换（ConfigBridge :1066-1106 区域）**

update=`可更新：{local} → {remote}`；install=`可安装：{remote}`；downgrade=`可降级：本地 {local} 比远端 {remote} 新`；⊘（warn 及 kbw 附加）=`可能不适配当前游戏版本`；amber 待生效 tip=`待生效：重启游戏后应用`。G="kbw" 消歧机制不动。

- [ ] **Step 2: MC_UPDALL 退役 + MC_CHK 文案**

删 `case "MC_UPDALL":` 分支（:2282）与 `HandleUpdAll`（:1184 起整段）及头注中相应描述；MC_CHK 冷却 toast 改 `刚检查过，稍后再试`；403 专文案 `GitHub 限流，请稍后再试` 与 fetch 失败 toast（grep `检查失败` 定位）改 `检查失败，稍后再试`。分发 switch 若有 default 拒绝路径，确认未知 type 仍安静丢弃（兼容旧面板）。

- [ ] **Step 3: MC_SCAN_DATA owners 显示名**

核对 :136 起事件拼装：`owners` 数组元素须为 `OwnerDisplay`（PatchScan.cs :353）产出的显示串；
若当前推的是裸 ownerId，改为 Display。`MC_SCAN_DATA` 其余字段不动。

- [ ] **Step 4: 构建与静态断言**

Run: `grep -n "UPDALL\|全部更新" SurvivalLog.ModCenter/ConfigBridge.cs`
Expected: 零活动代码引用（注释/文档行除外）。
Run: `dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release`
Expected: Build succeeded。

- [ ] **Step 5: Commit**

```bash
git add SurvivalLog.ModCenter/ConfigBridge.cs SurvivalLog.ModCenter/PatchScan.cs
git commit -m "feat(ModCenter): v3 更新链精简——UPDALL 退役 + 图标/检查文案逐字 + owners 显示名（C#）"
```

### Task 3: panel.js 更新链回写（头按钮 · 维护区 · 删仓库视图）

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`（build 骨架 :334-396、renderSide :534、renderCfg :1140、
  repo 数据/视图函数 :1049-1194、upd/uninst/reboot 弹窗 :1410-1590、wireSide :729、
  wireCfg :1674、onDlEvent :1886、__mcOpen :1991）
- Build: `SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj`（panel.js 为 EmbeddedResource，构建含语法校验）
- Test: Task 6 实机 CDP（本任务先过构建 + grep 断言）

**Interfaces:**
- Consumes: Task 1/2 的 C# 产出（Tip 逐字、◎ 显式字形、owners 显示名、无 MC_UPDALL 回包）。
- Produces（Task 4/5/6 依赖）: `maintHtml(m)`（维护区，签名 `(m) -> html string`）、
  `renderPatchBtn()`、`chkWaiting` 状态标记；`ST.repo` 与仓库视图函数全部移除后的零引用状态。

- [ ] **Step 1: 头部「检查更新」按钮 + 汇总 toast**

`build()`（:334）head 段：✕ 前插入 `__mc-btn` 常规按钮「检查更新」（`margin-left:auto`；✕ 改 `margin-left:0`），点击 push `MC_CHK` 并置 `ST.chkWaiting=true`、toast `正在检查更新…`。`__mcEvent` 的 `MC_REPO_DATA` 分支：`chkWaiting` 为真时清标记并按 `repoCounts()` 拼 spec §5.4 汇总 toast（全最新则 `检查完成：都是最新版`，small `列表里已用图标标出，点图标或进详情处理` 仅在有动作项时带）。

- [ ] **Step 2: 详情「维护」区**

新增 `maintHtml(m)`（spec §5.3 三态：pending 文本行 / 动作按钮行 / missing 仅安装），
`renderCfg` 末尾以 `maintHtml(m)` 取代原 `uninstActHtml(m.guid, …)` 调用（missing 不渲染卸载行）；
`__self` 分支改用单行 `uninstActHtml(SELF_GUID)`（hint=`需重启两次`）；删 missing 分支的
`repogoto` 按钮。按钮用 `data-ract="upd|inst"` 复用既有 `wireCfg` 分支。

- [ ] **Step 3: 删除仓库视图全家**

删 `repoEntryHtml`/`patchEntryHtml`/`scanTimeText`/`repoViewHtml`/`repoRowHtml`/`lastCheckText`/
`openRepoView`/T7 兜底壳 `updModalHtml`；`renderSide` 去掉两入口卡；`wireSide` 删
`repo-open`/`patchscan` 分支；`wireCfg` 删 `back|chk|updall|repogoto` 分支；`onDlEvent` 删
`ST.repo` 行按钮块（`S.mdl`/`S.mdlRepoKey` 弹窗分支原样保留）；`ST` 定义去 `repo` 字段，
全文件 grep `ST.repo` 零引用。

- [ ] **Step 4: 更新弹窗与重装退役**

`repoVerbOf` 删重装分支（ok→更新 语义不再出现在弹窗）；`updModalRealHtml`/`updModalDlHtml`/
`updModalDoneHtml`/`updModalErrHtml` 按 spec §5.4 逐字（D7/D8 红字新文案、`进行中`、`隐藏`、
`已就绪 · X`、`{动词}失败 · X`）；`openUpdModal` 兜底改 toast `暂无更新信息`/`点右上「检查更新」先查一次`。

- [ ] **Step 5: 卸载与重启弹窗 + toast**

`uninstModalHtml`（sub 一句 + 文件清单末行 `停用 / 排序记录（如有）` + self 红字逐字）、
卸载确认 toast（`已加入待生效：卸载 {name}` / `重启游戏后执行`）、`openRebootModal`
（spec §5.4 逐字）与重启 toast、`MC_PENDING_UNDO` 撤销 toast 逐字。

- [ ] **Step 6: 静态断言与构建**

Run: `grep -n "repoViewHtml\|repoRowHtml\|lastCheckText\|openRepoView\|updModalHtml\|ST\.repo\|updall\|repogoto\|重装" SurvivalLog.ModCenter/panel.js`
Expected: 零引用。
Run: `node -c SurvivalLog.ModCenter/panel.js 2>/dev/null || node -e "new Function(require('fs').readFileSync('SurvivalLog.ModCenter/panel.js','utf8'))"`
Expected: 无语法错误。
Run: `dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release`
Expected: Build succeeded。

- [ ] **Step 7: Commit**

```bash
git add SurvivalLog.ModCenter/panel.js
git commit -m "feat(ModCenter): v3 更新链回写 panel.js——头按钮/维护区/删仓库视图/弹窗精简"
```

### Task 4: panel.js 冲突两级与全局文案回写

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`（icGlyph :434、renderSide/renderCfg 文案、cfCardHtml/cfModalHtml/
  openCfModal :1233-1296、patchModalHtml :1359、头部副标题 :1871、底部图例 :347、toggleMod :1634、
  ordReset/ordSave :683-696、sideFoot/ordbar :578/:612）

**Interfaces:**
- Consumes: Task 1 的卡片语义（`tag` 前置、`reason` 可空、`G="◎"` 显式）与 Task 2 的
  owners 显示名（面板只 `join('、')`）。
- Produces: v3 最终渲染形态（Task 5 一致性核对、Task 6 CDP 断言的对象）。

- [ ] **Step 1: cf 弹窗两级化**

`cfCardHtml`：`lv` 只认 red/orange（否则归 orange）；h3 改 `{tag} · {mod名}`；"原因"段
reason 非空才渲染；"同域 mod/冲突对象"二分支删除，统一"涉及 mod"；删底部固定风险行；
"怎么办"段 tips 编号保留。`cfModalHtml` 删 `__mc-risk` 行。`openCfModal`/`onCfData` 无
结构改动（数据语义已由 C# 承载）。

- [ ] **Step 2: 补丁碰撞汇总弹窗 + 底部按钮**

`patchModalHtml` 按 spec §5.4 逐字重写（sub 一句 + 碰撞位置列表 owners `join('、')` +
框架侧披露段 + 空态 `没有发现碰撞。`，删数字串/风险行/范围行）；新增 `renderPatchBtn()`
（`#__mc_patch_btn`，`total>0` 显示、文案 `补丁碰撞 {N} 组`），`build()` foot 图例后插按钮
（`data-mact` 无、点击 `openPatchModal()`），`MC_SCAN_DATA` 分支与重扫完成 toast 后调用；
重扫 toast 改 `扫描完成：碰撞 {N} 处`（small 空）。

- [ ] **Step 3: 全局文案逐字回写**

按 spec §5.4 表逐字改：头部副标题、底部图例四项与脚注、`TOAST_SMALL` 四值、`toggleMod`
四分支、五种停用/未安装横幅、空配置组、`__self` 描述与卸载 hint、`ordbar` 工具条、
`ordReset`/`ordSave`/撤销 toast。写值类 toast（`X · 标签 → 值`）不动。

- [ ] **Step 4: icGlyph legacy 化**

`icGlyph` 的 `k==="gray"` 分支保留，注释标注 legacy（旧 C# 快照兼容，v3 不再产出 gray）；
orange+cf 推断 ⇄、orange+upd 推断 ↓、`g` 优先逻辑不动。

- [ ] **Step 5: 静态断言与构建**

Run: `grep -n "启动清理器\|账本\|写序类\|owner\b" SurvivalLog.ModCenter/panel.js`
Expected: 玩家可见文案零命中（代码注释命中可留，逐条目检）。
Run: `grep -n '"gray"' SurvivalLog.ModCenter/panel.js`
Expected: 仅 icGlyph legacy 分支一处。
Run: `node -e "new Function(require('fs').readFileSync('SurvivalLog.ModCenter/panel.js','utf8'))"` 与
`dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release`
Expected: 均通过。

- [ ] **Step 6: Commit**

```bash
git add SurvivalLog.ModCenter/panel.js
git commit -m "feat(ModCenter): v3 冲突两级与全局文案回写 panel.js"
```

### Task 5: 原型一致性核对 + DESIGN.md 重写

**Files:**
- Modify: `SurvivalLog.ModCenter/DESIGN.md`（§3 协议表、§4 引言与 4.1/4.2、§5.3、§6.1/6.2/6.3、§7.3 退役表）
- Modify: `SurvivalLog.ModCenter/prototype.html`（仅当核对发现不一致时修正）

**Interfaces:**
- Consumes: Task 1-4 完成后的 panel.js 渲染产出。
- Produces: 与实现一致的活文档（后续迭代的对照基线）。

- [ ] **Step 1: 原型 ↔ panel.js 逐字核对**

对 spec §5.4 总表逐行：panel.js 渲染字符串与 prototype.html 对应模板逐字 diff（人工核对并在
本步骤勾选时列出不匹配项）；发现不一致以 prototype.html 为准修 panel.js（反之仅当原型笔误）。

- [ ] **Step 2: DESIGN.md 相应章节重写**

§3：MC_UPDALL 行移入退役记录、MC_SCAN_DATA owners 语义、ics ics 字段表删 gray；
§4：4.1 图标表删灰行并改两级口径、4.2 弹窗与视图按新文案；§5.3 仓库页段落改为"更新链
（无独立页）"并记 UPDALL/重装退役；§6 三层呈现改两级；§7.3 退役表补三行
（独立仓库页视图 / MC_UPDALL / 重装当前版本）。§4 引言中"panel.js 待回写"字样清除。

- [ ] **Step 3: 全量构建**

Run: `dotnet build SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj -c Release && dotnet build MCShell/MCShell.csproj -c Release`
Expected: 两个工程均 Build succeeded。

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.ModCenter/DESIGN.md SurvivalLog.ModCenter/prototype.html
git commit -m "docs(ModCenter): DESIGN.md v3 重写（两级冲突/无仓库页/UPDALL 与重装退役）+ 原型一致性核对"
```

### Task 6: 实机 CDP 回归 + 套件夹具同步

**Files:**
- Test: 实机（游戏主菜单 + BepInEx 装新构建 dll，CDP 9222）
- Modify: `E:\Game\Mod\survivallog-mods\suite\tools\mc_fixture.js`（套件仓，repo 视图/旧文案断言）

**Interfaces:**
- Consumes: Task 1-5 全部产出（装机验证对象）。
- Produces: 实测证据（按硬规则 9 惯例存档到套件仓 sdd 目录）与 ALL PASS 的夹具。

- [ ] **Step 1: 部署新构建并打开面板**

Run: 按 release.ps1 产物或直接复制 `SurvivalLog.ModCenter.dll` 到游戏 `BepInEx\plugins\`，启动游戏到主菜单，CDP 连 9222 打开面板。
Expected: 面板正常打开；侧栏三段无任何按钮/入口卡；`gray` 徽标零出现。

- [ ] **Step 2: 冲突与图标断言**

DOM 断言：柜子容量行红 ⚠、自选生存规划行橙 ⇄+橙 ◎（`G="◎"` 显式）、囤鼠桃园橙 ↓、
灾前加速蓝 ↑、动作加速琥珀 ⟳；图标悬停 title 与 spec §5.1/§5.3 逐字一致。
点击柜子容量 ⚠：弹窗首卡 `严重冲突 · 柜子容量` + 结论句逐字 + 无底部风险行；
点击第三方 ⇄：`脚本碰撞 · {名}` 卡。Expected: 全部逐字命中。

- [ ] **Step 3: 更新链断言**

点头部「检查更新」→ toast `正在检查更新…` → `MC_REPO_DATA` 到达后汇总 toast 逐字；
点行内 ↑（保鲜增强）→ 确认弹窗逐字（含 `下载并校验后暂存，重启游戏时自动替换（不保留旧版）。`）→
确认 → 进度 `进行中`/阶段文案 → `已就绪 · X` → 底条出现、行内 ⟳、维护区 pending 行；
灾前加速（未装）详情维护区 = 仅「安装 X」行无卸载行。Expected: 逐字命中，无 JS 异常。

- [ ] **Step 4: 碰撞汇总/卸载/排序/撤销断言**

底部「补丁碰撞 N 组」→ 汇总弹窗（中文 owner 名 + `另有 N 处框架内部碰撞` 段 + 重新扫描就地
刷新）；卸载弹窗普通款与 self 款逐字；排序工具条逐字 + 恢复默认 toast + 保存 toast；
伪造 pending → 撤销 toast 逐字；`MC_REPO_DATA` 未到时点图标 → `暂无更新信息` toast
（Review Focus 3）。Expected: 逐字命中。

- [ ] **Step 5: 套件夹具同步**

在套件仓更新 `mc_fixture.js`：删除仓库视图相关断言、旧文案断言改为 §5.4 新值、
补两级冲突断言。Run: 套件仓运行 `mc_fixture.js menu` 与 `toggle`。
Expected: ALL PASS（SKIP 数不新增）。

- [ ] **Step 6: 证据归档与提交（本仓无改动则跳过提交）**

实测输出按惯例存 `suite/.superpowers/sdd/2026-10-30-modcenter-v3-panel/`（套件仓提交）；
本仓若回归中发现修复，按所属任务粒度补 commit（`fix(ModCenter): v3 回归修复——{点}`）。

---

## Self-Review 记录

- Spec 覆盖：§5.1→Task1、§5.2→Task1、§5.3→Task2/3、§5.4→Task3/4、§5.5→Task3/4、
  §5.6→Task1/2/4 断言步、§6→Task5/6，无缺口。
- Review Focus 5 条分别钉在 Task1 步3、Task4 步4、Task3 步4+Task6 步4、Task3 步3+Task6、
  Task3 步6/Task4 步5 的 grep 断言。
- 类型一致性：`maintHtml(m)`/`renderPatchBtn()`/`TipShort`/`Tag` 各任务引用同名同形。
- 篇幅：计划只含签名、锚点、断言与逐字文案引用（文案正文在 spec，不重复）。
