# 动作覆盖下拉改造 验收与发版计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把已实施并冒烟通过「动作覆盖下拉改造」（QuickAction 1.8.0 目录 + ModCenter 2.1.1 两级可搜索下拉）落库、实机验收并按仓纪律发版。

**Architecture:** 口径单源——QuickAction 每会话写 `BepInEx\QuickActionCatalog.txt`（ID|名称|分类 1..6|原耗时毫秒，守卫全过滤）；ModCenter `ActionCatalog.cs` 按时间戳缓存读取 → 快照 `acts` 字段 → panel.js/prototype 自绘两级可搜索 dd（类型→动作+搜索+方向自适应）。实现代码已完成、双仓编译 0 错误、原型冒烟全过；本计划只覆盖**落库、实机验收、发版**。

**Tech Stack:** BepInEx 6 IL2CPP / C# net6.0 / 游戏内 WebView（CEF）注入 JS / 各仓 release.ps1 发版链。

**Spec:** `ModCenter/docs/superpowers/specs/2026-11-07-quickaction-override-dropdown-design.md`（定稿+检查轮 D1~D11；计划据它论证，执行者两份都读）。

## Global Constraints

- **范围纪律（作者令）**：只动「动作覆盖」块；面板其余、QuickAction 引擎、写路径（`"id:ms"` 整串 MC_SET 其他.Overrides）零改动。
- **协议兼容**：`acts` 缺省不输出；旧 panel.js / 旧 QuickAction dll 各自独立可运行（回退 ID 手输框）。
- **版本单源**：QuickAction `Version` const = csproj `<Version>` = `1.8.0`；ModCenter csproj = `2.1.1`；suite `version.json` pin `QuickAction: 1.8.0`（已改，note 注明先发 1.8.0 zip）。
- **同步纪律**：panel.js 与 prototype.html 同一提交内一致；发版走各仓 `release.ps1`（作者拍板才执行 `-Yes`）。
- **实机步骤**（进游戏、开面板）由作者操作；代理负责给出检查清单并判读产出文件/日志。

## Review Focus（spec 隐含但冒烟未覆盖的输入类，逐条钉在所属任务）

1. **旧 QuickAction dll（无目录文件）+ 新 ModCenter**：面板回退 ID 手输框且增删调照常（D9）→ Task 3 Step 4。
2. **目录外旧 ID**（玩家 cfg 里手输过的 8012 等）：按钮显示 `动作 #ID（目录外）`、可改可删不串值 → Task 3 Step 3。
3. **弹层裁切**：尾行贴近滚动容器底边时向上弹（D10，原型已验 158px 场景）→ Task 3 Step 2 顺带目检。
4. **目录文件损坏/半行**：坏行静默跳过；整文件空/全坏按 null 回退手输框 → Task 2 Step 4。
5. **cfg 往返**：面板 canonical 序列化 vs 手改 cfg 乱序/空格/坏段容错，重启后回读正常（既有 ParseOverrides 行为不回归）→ Task 3 Step 5。

---

### Task 1: 两仓实现落库（回滚点）

**Files:**
- Commit（已改未提交）: `QuickAction/SurvivalLog.QuickAction/{QuickActionPlugin.cs, SurvivalLog.QuickAction.csproj, CHANGELOG.md, DESIGN.md, README.md}`
- Commit: `ModCenter/SurvivalLog.ModCenter/{ActionCatalog.cs(新), Payload.cs, ConfigBridge.cs, panel.js, prototype.html, SurvivalLog.ModCenter.csproj, CHANGELOG.md, DESIGN.md}`

**Interfaces:**
- Produces: 两个 feat 提交（QuickAction 1.8.0 / ModCenter 2.1.1），后续任务的部署与发版都以这两个提交为内容。

- [ ] **Step 1: QuickAction 仓提交**

```bash
cd E:/Game/Mod/survivallog-mods/QuickAction && git add SurvivalLog.QuickAction/
git commit -m "feat(QuickAction): v1.8.0 面板动作目录——QuickActionCatalog.txt（ID|名称|分类1..6|原耗时毫秒）每会话表就绪写一次；第6组「野外与其他」收引用轴漏网 Other 玩家动作（探索中109/搜索/翻找/撬开/瞭望/清点物资）；守卫全过滤=列进必生效；ModCenter 2.1.1 下拉单源；引擎/配置零改动"
```

- [ ] **Step 2: ModCenter 仓提交**

```bash
cd E:/Game/Mod/survivallog-mods/ModCenter && git add SurvivalLog.ModCenter/
git commit -m "feat(mc): v2.1.1 动作覆盖下拉化——行内自绘两级可搜索dd（类型→动作+搜索+返回；点动作即提交/换动作保留毫秒/重复拒/外点关/下方<300px向上弹）；acts 快照字段+ActionCatalog 缓存读 QuickActionCatalog.txt；无目录回退 ID 手输框；写路径/协议零改动"
```

- [ ] **Step 3: 验证工作区干净**

Run: `git -C <两仓> status --short`
Expected: 仅剩与本功能无关的既有未跟踪项（如 ModCenter `docs/promo/`、suite tools 临时件）。

### Task 2: 实机部署与目录文件验收

**Files:**
- 部署: `<游戏>/BepInEx/plugins/SurvivalLog.QuickAction.dll`、`SurvivalLog.ModCenter.dll`（取各仓 `bin/Debug/`）
- 产出: `<游戏>/BepInEx/QuickActionCatalog.txt`、`LogOutput.log`

**Interfaces:**
- Consumes: Task 1 的两个提交构建出的 dll。
- Produces: 生成目录文件，Task 3 的面板验收以它为数据源。

- [ ] **Step 1: 拷贝两个新 dll 进游戏 plugins 目录**（覆盖 `SurvivalLog.QuickAction.dll`、`SurvivalLog.ModCenter.dll`）。
- [ ] **Step 2: 启动游戏进存档，等 QuickAction 首轮生效（约 5~10s）**

Run: `grep "QuickActionCatalog" "<游戏>/BepInEx/LogOutput.log"`
Expected: `[QuickAction] QuickActionCatalog.txt 已生成（N 条，分类 1..6）`，N ≈ 3800。

- [ ] **Step 3: 目录内容抽查（作者令点名的野外探险必须在列）**

Run: `grep -E "^(109|114|115|1506)\|" "<游戏>/BepInEx/QuickActionCatalog.txt"` 与 `awk -F'|' 'NR>1{c[$3]++}END{for(k in c)print k,c[k]}' <游戏>/BepInEx/QuickActionCatalog.txt`
Expected: 109|冒险出门 探索中……|6|3000 等在列；分类列只出现 1..6；无 `|2|`（睡觉/社交）行；组计数与设计 §4 同量级（物品 ~2700+/家具 ~900+/其余百级）。

- [ ] **Step 4: 坏文件容错（Review Focus 4）**

把目录文件截断一半（保留表头+若干半行）→ 重开面板：动作加速页仍出下拉（坏行被跳过）；
把文件清空 → 重开面板：回退 ID 手输框。测完删文件重启让游戏重生成。

### Task 3: 面板实机验收（作者操作，清单判读）

**Files:**
- 读写: `<游戏>/BepInEx/config/com.local.survivallog.quickaction.cfg`（`[其他] Overrides`）

**Interfaces:**
- Consumes: Task 2 的目录文件。
- Produces: 验收结论（通过/问题清单回设计 D 表）。

- [ ] **Step 1: 下拉全流程**——开面板→动作加速→尾行点开：六组类型带数量 → 选「野外与其他」→ 列表只显动作名、顶部搜索框输「探索」过滤出「冒险出门 探索中……」→ 点选：toast 已添加=1000ms、下方自动补新空行、cfg 出现 `109:1000`。
- [ ] **Step 2: 弹层方向目检（Review Focus 3）**——清单滚到底使尾行贴底再点开：弹层向上展开不被裁切。
- [ ] **Step 3: 目录外旧 ID（Review Focus 2）**——cfg 手工加 `8012:250` 重开面板：该行按钮显示 `动作 #8012（目录外）`，可改可删；改后 cfg 串正确。
- [ ] **Step 4: 旧 dll 回退（Review Focus 1）**——改名目录文件重开面板：回退 ID 手输框+新增按钮，手输 109 增删照常；恢复文件名重开恢复下拉。
- [ ] **Step 5: cfg 往返（Review Focus 5）**——面板加 3 条 → cfg 串为 id 升序 `a:ms,b:ms`；手改成乱序+空格+一条坏段 `8012:250, 109 : abc,,1915:0` 重启重开：坏段静默丢、合法行照常显示可调。
- [ ] **Step 6: 通过后把实测计数（N 条/各组数）补进设计文档 §8 并提交**（`docs(mc): 动作覆盖下拉实机验收计数`）。

### Task 4: 发版 QuickAction 1.8.0（zip 先行）

**Interfaces:**
- Consumes: Task 1~3 全绿。
- Produces: QuickAction 仓 1.8.0 release zip（suite pin 已指向它）。

- [ ] **Step 1: 发版**——`cd QuickAction && ./release.ps1`（作者拍板后 `-Yes`；按仓 README 核对 gamekb/update.json 产出）。
- [ ] **Step 2: 验证**——release 页有 `SurvivalLog.QuickAction.dll` zip 与五字段 update.json（version=1.8.0）。

### Task 5: 发版 ModCenter 2.1.1（发版定档，作者拍板）

**Interfaces:**
- Consumes: Task 4 完成（套件 pin 复用该 zip）。
- Produces: ModCenter 2.1.1 release + index 仓聚合更新。

- [ ] **Step 1: 发版**——`cd ModCenter && ./release.ps1`（`-Yes` 由作者拍板）。
- [ ] **Step 2: publish-index**——总仓 `publish-index.ps1` 刷 index（套件检查 1 次 API 口径不回退）。
- [ ] **Step 3: 套件线**——suite 发版若随之滚动：`version.json` pin 已就位，按 suite 流程执行（另行拍板）。
