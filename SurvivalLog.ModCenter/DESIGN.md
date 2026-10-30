# MOD 管理中心 ModCenter — 设计文档（v2 生命周期）

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：ModCenter `1.7.0`（作者定号，硬规则 1）。**版本线只有一条**（Ruling 21 单 dll 化）：
> 清理逻辑并入 `SurvivalLog.ModCenter.dll`；`BepInEx\patchers\MCShell.dll` 是**永久冻结**的
> 引导壳（1.0.0 仅是 `[PatcherPluginInfo]` 属性占位，不构成版本线，只有壳接口签名变更才需
> 随安装器更新）。版本号一律由作者定，实施任务不得自选/递增。
> 菜单锚点单源：本目录 `ANCHORS.md`（挂钩点变更必须同步该文件与 `tools/gamekb/moddeps.json`）。
> 当前状态（v2，2026-10-28/29）：**单 dll 化** = 插件本体（`BepInEx\plugins\SurvivalLog.ModCenter.dll`，
> 内含 CleanupRunner 清理执行体）+ 冻结引导壳（`BepInEx\patchers\MCShell.dll`，只负责在补丁器
> 阶段唤起 CleanupRunner，并处理卸载墓碑/自更新预置），二者经 `BepInEx\ModCenter\pending.json`
> （+`staging\`）交接；游戏内生命周期五链
> （更新/安装/卸载/排序/停用）+ 冲突体检三层（L1 已知库 / L2 运行时碰撞 / L3 功能域）。
>
> **文档结构**：A 部（§1–§10）= v2 现行设计；B 部（附录）= **v1（历史）**原文归档
> （v1 章节号保留原编号，正文引用写作「v1 §4.1」）。v1 中已被 v2 取代的机制
> （DllDisabler 退出机械、单槽 MCCleaner（Ruling 8）、`[Disabled]` 名单等）见 §7 退役记录。
> 实测报告与证据文件在 suite 仓 `.superpowers/sdd/2026-10-28-modcenter-v2-lifecycle/`，
> 统一索引见 §9。

## 1. 功能定位（v2）

v1 定位不变：在主菜单（MainUI）与游戏内 ESC 菜单（OutSetting）各注入一个入口，
打开 WebUI 面板，集中查看/开关/调参本套件各 mod（各 mod 的 `General.Enabled` 与
配置键），替代逐个手改 cfg。管理器本身不改任何玩法、不改存档、不碰游戏本体文件。

v2 在此之上补齐**生命周期闭环**（作者 2026-10-28 拍板，spec §1 D1–D10）：

- **更新/安装**：面板内检查 12 个组织仓的 GitHub Release，下载 → 校验 → 暂存 →
  写待办清单，重启生效（不做进程内热换）；
- **卸载**：目标 dll + 该 mod cfg + ModCenter 自身记录全清；执行点在下次启动、
  **插件加载之前**（强杀也不复活）；
- **停用/启用**：第三方 dll 改名为 `.disabled`；执行点从 v1 的"退出时分离进程改名"
  迁到启动清理器（意图先落盘，强杀失效窗口结构性消失）；
- **手动排序**：拖拽/键盘调整加载顺序，规避部分写序类冲突；硬依赖链受保护；
- **冲突体检**：L1 已知冲突库（红）/ L2 运行时补丁碰撞实测（橙）/ L3 功能域标注（灰）；
  行内图标 + 点击详情 + 玩家自决，**全程不自动替玩家处置**。

| # | 决策点 | 结论（2026-10-28 作者拍板） |
|---|---|---|
| D1 | 热更新口径 | 游戏内检测/下载/落盘 + 重启生效；不做进程内热换 |
| D2 | 卸载彻底度 | 真删除（dll + cfg + ModCenter 记录）；执行点在下次启动、插件加载之前 |
| D3 | 排序机制 | A1：启动清理（CleanupRunner）Harmony 改序（Cecil 改写第三方 dll 依赖属性被否——改玩家文件，对方发新版即失效） |
| D4 | 冲突体检 | 三层全上；行内警示标 + 点击详情 + 玩家自决 |
| D5 | 安装范围 | 套件 12 组织仓；第三方仍=玩家手丢 dll 面板纳管；不做第三方商店（转载红线）；基座修复走网盘 |
| D6 | 更新触发 | **面板打开时自动查一次**（15 分钟会话缓存；Ruling 22 + T16/D 加固）+ 手动刷新 60 秒冷却；**无后台轮询** |
| D7 | 本地 ≥ 远端 | **只告警不拦截**；确认窗红字"版本回退风险"，是否降级由玩家决定；保留"重装当前版本"入口 |
| D8 | gamekb 不符 | **只告警不拦截**；行内 ⊘ 橙 + 确认窗红字，按钮保持可用；本地版本读不到 → "未校验游戏版本"照常放行 |
| D9 | 面板形态 | 融合式（仓库入口卡 + 排序模式融入侧栏）；徽标全图标化；排序用拖拽 + 键盘 ↑↓ 替代（WCAG 2.2 AA），不用按钮堆 |
| D10 | 列表密度 | 徽标 18px 圆片 / 侧栏 280px / 行内距 6px；第三方显示名去掉"（第三方）"后缀 |

非目标：进程内热换；第三方 mod 商店/代下载；BepInEx 基座自动安装；自动处置冲突；
后台静默轮询更新；**引导壳游戏内自更新**（T16：壳永久冻结，清理逻辑随 ModCenter.dll
本体更新，只有壳接口签名变更才需安装器）。

## 2. 架构：单 dll + 冻结引导壳

### 2.1 职责边界

四个约束拼出的形状：体检要活在运行态（插件侧）、UI/网络要活在游戏内（插件侧）、
文件操作要活在插件加载前（补丁器侧）、纯退出路径已被 D2 否决。T16 把这些拼成
**单 dll + 一个永久冻结的引导壳**（方案 A，Ruling 21）：壳实际约 320 行（含墓碑/自更新
两分支、路径校验与证据写入；核心流程约 120 行），不承载任何业务清理逻辑。

```
┌─ ModCenter 插件本体（BepInEx\plugins\SurvivalLog.ModCenter.dll）──────────┐
│ 面板 UI：仓库页/更新/卸载/体检/排序模式/待生效条（panel.js）                │
│ 网络层：GitHub API + 资产下载 + 镜像回退（RepoClient）                      │
│ 决策层：五态比对 + gamekb 门 + 待办写入（RepoDecision/GameVersion/PendingWriter）│
│ 体检三层 + 警示标渲染（Meta 静态表 / PatchScan / ConfigBridge）             │
│ CleanupRunner（补丁器阶段由壳反射调用，禁碰 IL2CPP）：                     │
│ ① marker 翻译 ② pending 执行（幂等/MD5/原子重写）③ 加载序 Harmony 覆盖     │
│ ④ 目录 sweep ⑤ 自卸载墓碑/自更新预置（两阶段，写 flag 由壳下一靴执行）      │
└───────────────────────┬───────────────────────────────────────────────────┘
                        │ 写/读（唯一运行期介质）
     BepInEx\ModCenter\pending.json（+ staging\ 暂存目录）
                        │ 启动时读取执行
┌─ MCShell（BepInEx\patchers\MCShell.dll，永久冻结）────────────────────────┐
│ ① 墓碑分支：见 uninstalled.flag → 删本体+cfg+ModCenter 目录 → 不加载 → 退出 │
│ ② 自更新分支：见 selfupdate.flag → 加载前替换本体（一次重启即生效）          │
│ ③ 定位本体 → Assembly.LoadFrom → 反射调 CleanupRunner.Run()                 │
│ 任何异常自吞写证据，绝不阻断游戏启动                                        │
└────────────────────────────────────────────────────────────────────────────┘
```

纪律：

- **插件本体是唯一的意图生产者**：面板交互、网络下载、决策全部在这里；所有文件动作
  一律落成 `pending.json` 的 op，由 CleanupRunner 在下次启动执行。插件运行期不直接
  删除/替换 plugins 下任何文件（运行中被锁，且强杀会留下半状态）。
- **CleanupRunner 是唯一的文件操作执行者**：由壳在补丁器阶段唤起（plugins 文件无锁，
  实证 E4），不读游戏运行态、不联网、不弹 UI；任何单条失败只记证据并保留该 op，
  不阻断启动。代码纪律：只允许 BepInEx.*/HarmonyLib/System.IO 等，**禁碰任何 IL2CPP
  游戏类型**（Il2Cppmscorlib/Il2CppSystem/HotUpdate/UnityEngine）——它在插件加载前运行。
  边界：玩家把本体手工改名 `.disabled` 时壳不加载它（尊重停用意图），本靴清理不运行；
  面板同时不在，无新意图产生，显式启用后下一靴继续。
- **契约源码只有一份**：`Contracts/McPending.cs` 直接编译进插件项目（T16 起不再有
  MCCleaner link 编译的第二份）；壳不解析 pending.json，只认两个 flag 文件与
  `CleanupRunner.Run()` 三个接口。
- **引导壳永久冻结**（基座级，与 BepInEx 基座同边界）：无独立版本线（1.0.0 仅属性占位），
  不参与游戏内更新；只有壳接口签名（flag 格式 / Run 签名）变更才需要安装器更新。
- **物理约束（T16 实证）**：ModCenter.dll 被壳 `Assembly.LoadFrom` 后文件被 CLR 锁定，
  `File.Delete` 失败；同程序集身份也不允许同靴内被 Chainloader 重新 `LoadFrom`。
  因此自删/自替换必须**两阶段**：boot N 只写 flag（墓碑/预置），boot N+1 壳在加载前执行。
- **自更新一次重启生效**：面板更新 ModCenter 时在 pending op 之外写 `selfupdate.flag`；
  壳在下一靴 LoadFrom 之前替换本体，当靴即加载新程序集——清理逻辑随新 dll 生效
  （证据行含程序集版本）。无壳预置时（旧面板/异常）则由 CleanupRunner 在 boot N 补写
  预置并接管，代价=多一次重启。

### 2.2 pending.json 契约

`BepInEx\ModCenter\pending.json`（ModCenter 写、清理器读执行后重写）：

```jsonc
{
  "ops": [                                  // 幂等：目标不在场即视为成功
    { "op": "delete",  "target": "plugins/SurvivalLog.X.dll" },
    { "op": "delete",  "target": "config/SurvivalLog.X.cfg" },
    { "op": "install", "staged": "ModCenter/staging/x.dll", "target": "plugins/SurvivalLog.X.dll", "md5": "…" },
    { "op": "replace", "staged": "ModCenter/staging/x.dll", "target": "plugins/SurvivalLog.X.dll", "md5": "…" },
    { "op": "disable", "target": "plugins/ThirdParty.dll" },   // → .disabled（可恢复停用）
    { "op": "enable",  "target": "plugins/ThirdParty.dll" }
  ],
  "order": ["guid.a", "guid.b", "…"]        // 可选：加载序覆盖（见 §5.4）
}
```

字段与语义（实现口径，两端逐字一致）：

- `target` / `staged` 均为 **BepInEx 根相对路径**（`/` 或 `\` 均可，执行侧归一；
  绝对路径/盘符/`..`/根起反斜杠一律拒绝）。`staged` 通常位于 `ModCenter/staging/`。
- `md5` = **暂存文件自身的 MD5**（大小写不敏感）。install/replace 执行时先校验
  `MD5(staged) == md5`，再删旧 target、建目录、`File.Copy(staged, target, true)`。
  zip 完整性（update.json.md5 对 zip）在下载链完成——两层校验闭环（Ruling 11）。
- **幂等不变量**：delete 目标不在场=成功；install/replace 目标可覆盖；disable 目标
  不在场=成功；disable 时 dll 与 `.disabled` 双在场=删裸 dll、保留 `.disabled`（成功，
  不再每 boot 一条 FAIL）；enable 目标已在场=成功。重跑同一清单不得产生额外副作用。
- **失败语义**：单条 op 失败保留在清单（下次启动再试），证据文件记录失败原因；
  不阻断其余 op 与游戏启动。有成功条目时以"剩余条目 + 原 order"**原子重写**
  （`path.tmp` → `File.Replace`/`File.Move`）；无成功条目不重写（零 churn）。
- **order-only 文件**：`ops` 为空但 `order` 存在时，执行器零动作、不改写清单；
  顺序覆盖由 postfix 在链式加载排序时应用（§5.4）。
- **引导壳槽守卫（T16）**：install/replace 的 target 文件名为 `MCShell.dll` / 旧 `MCCleaner.dll`
  （任意目录）**或**位于 `patchers\` 下 → 拒绝执行、op 保留、证据记
  `拒绝执行: 引导壳属基座级组件，请用安装器更新`。
- **自操作两阶段（T16 物理约束）**：delete 目标=本体 dll → 本靴不删（已被壳 LoadFrom 锁定），
  写 `uninstalled.flag`（时间戳 + cfg 路径）并从清单移除（视为已接管）；install/replace
  目标=本体 dll → 本靴不落盘（同程序集身份不允许同靴重新 LoadFrom），写 `selfupdate.flag`
  （staged 绝对路径 / staged md5 / target 绝对路径 三行）并从清单移除；两者由壳在下一靴
  **加载前**执行。目标已是期望内容（`MD5(target)==op.md5`）→ 幂等成功（壳已预置/清单重跑）。
- 执行器**不清理 staging**：暂存文件在 op 成功消费后仍留在盘上，由目录 sweep
  在清单消化完时统一回收（§5.5）。
- 写入纪律：**主线程唯一写者**（`PendingWriter`，Ticker/消息分发上下文），写前原子替换；
  后台线程只回传结果，不直接写文件。

### 2.3 分发边界与版本线

- **zip 布局（release.ps1 产出，T16）**：
  `BepInEx\plugins\SurvivalLog.ModCenter.dll` + `BepInEx\patchers\MCShell.dll` + `README.md`；
  `release.ps1` 打包后**断言**三件套在场且 dll 恰两枚（防 deps.json/探针混入）。`update.json`
  仍是五字段 `{mod, version, asset, md5, gamekb}`（契约冻结）。
- **旧清理器清理**：包内不再含 `MCCleaner.dll`；升级由安装器 `[InstallDelete]` 预删旧件
  （保留该条目的目的就是 v1 旧清理器升级清理），手动升级玩家需自行删除（并存会重复执行
  改名/排序）。
- **安装器分发**：`installer.iss` 的 `mod_center` 组件包含 `BepInEx\patchers\MCShell.dll`；
  `[InstallDelete]` 预删旧 `MCShell.dll` / `MCCleaner.dll` 与 `BepInEx\ModCenter\` 的
  自更新预置/墓碑（安装器替换本体后旧意图必须作废）。
  套件发布 `publish.ps1` 把各仓 zip 解包成 `release\SurvivalLog.<Mod>_v<ver>\` 供 ISCC 引用。
- **游戏内更新链**：只解包 `BepInEx/plugins/*.dll`（直下条目），不碰 patchers；
  ModCenter 自身更新同样只替换插件 dll（**清理逻辑随本体更新**）；引导壳永不参与游戏内更新。
- **版本线**：ModCenter csproj `<Version>` 为唯一权威，`ModCenterPlugin.Version` const
  必须一致（`release.ps1` 校验）。`MCShell.csproj` `<Version>1.0.0</Version>` 是
  `[PatcherPluginInfo]` 的**固定占位**，不是独立版本线（壳永不变更，接口签名变更才需安装器）。

### 2.4 组件清单

| 文件 | 职责 |
|---|---|
| `ModCenterPlugin.cs` | 插件入口。cfg 绑定（`Network.*` / `Debug.*`）、Ticker 泵（Pump / FocusTick / 注入巡检 / PatchScanProbe / 探针收割）、退出钩子（FlushDirty 双钩子幂等）、自证 `ModCenter_loaded.txt` |
| `ConfigBridge.cs` | 配置桥与全部消息路由。快照（Meta 九表 + 第三方三层 + ics）／写值（TrySet/TrySetTp）／启停（SetModEnabled 热还原 + 第三方 marker）／待生效汇总（pending.json 汇总、⟳、底条）／仓库页状态机与下载链／排序／卸载（MC_UNINST*）／冲突详情（MC_CF）／L2 扫描推送／重启与撤销 |
| `Meta.cs` | 九张套件页面定义 + `KnownConflicts`（L1 冲突库）+ `Domains`（L3 功能域）+ 显示名/分组/标签 |
| `Payload.cs` | 全部 DTO（Snapshot/ModInfo/ModItem/ItemInfo/IcInfo/RepoRowInfo/RepoData/PatchScanData/UninstInfo/CfData…）+ 手写 JSON（AOT 零反射） |
| `panel.js` | 面板 UI 全部（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`）：侧栏/配置页/仓库页/排序模式/冲突与更新与卸载弹窗/待生效条/toast/outbox 契约 |
| `RepoClient.cs` | GitHub release 客户端：两跳 fetch `update.json` 五字段、资产下载（`.part` → 原子落位）、镜像映射、MD5 工具 |
| `RepoDecision.cs` | 12 仓五态决策（Install/Update/Downgrade/Ok + Warn 覆盖）+ `PendingWriter`（pending 主线程唯一写者三口：AddUpdate/AddDelete/SetOrder）+ 15 分钟缓存/60 秒冷却口径常量 |
| `GameVersion.cs` | 本地游戏版本通道：Steam appmanifest `buildid` 主通道 ↔ gamekb 快照名 `_N` 直接相等；`Version.txt` 复合版本后备展示；失败降级"未校验游戏版本" |
| `PatchScan.cs` | L2 运行时补丁碰撞：`Harmony.GetAllPatchedMethods()` → owner 归组 → 游戏侧过滤 → GUID 精确/前缀归属 → 行内橙标/MC_CF 橙卡/MC_SCAN_DATA；`Debug.PatchScan` 探针 |
| `RestartHelper.cs` | 通用分离进程启动器（`StartDetachedPS`，EncodedCommand）+ 一键重启（PID 精确等待 → 9s Steam 延迟补偿 → `steam://` 重拉）+ `QuitGame()` |
| `DllDisabler.cs` | **仅意图写入与扫描**：`[DisabledTp]` marker 读/写、Enable 即时反向改名、ScanOnLoad 预 Bind、AbsentDisabled 缺席行、Tier2 软停重放。退出改名机械已退役（§7.1） |
| `Contracts/McPending.cs` | pending 契约（常量/Op/Load/Save/LoadOrder/原子写/手写解析），插件项目直接编译（T16 起单份，无 link） |
| `CleanupRunner.cs` | 插件内的清理执行体（`public static Run()`，壳反射入口；**禁碰 IL2CPP 类型**）：marker 翻译 → pending 执行器（含引导壳槽守卫 / 自操作两阶段）→ ModifyLoadOrder postfix → 目录 sweep；`OrderReorder` 纯函数（依赖保护）；自证 `MCCleaner_loaded.txt` / `MCCleaner_ops.txt`（文件名沿用 T1/T2 证据格式，内容加程序集版本） |
| `MCShell/ShellPatcher.cs` | 永久冻结引导壳（`BasePatcher`）：墓碑分支 → 自更新预置 → 定位本体/LoadFrom/反射调 `CleanupRunner.Run()`；任何异常自吞写 `MCShell_loaded.txt`，绝不阻断启动 |
| `MCShell/MCShell.csproj` | 壳 csproj（net6.0，`<Version>1.0.0</Version>` 固定属性占位、非版本线）；只引用 BepInEx.Core / BepInEx.Preloader.Core（`Private=false`）；**不引用插件项目**（反射解耦） |

### 2.5 启动时序（每次 boot）

1. **补丁器阶段（MCShell）**：BepInEx 加载 `patchers\MCShell.dll` → `Initialize()` 写
   `MCShell_loaded.txt`（时间戳 + 壳位置）。**墓碑分支优先**（存在
   `BepInEx\ModCenter\uninstalled.flag`）：递归删 `plugins\` 下的本体 dll（含 `.disabled`）+
   自身 cfg + `BepInEx\ModCenter\` 目录（含 flag）→ **不加载**本体 → 退出。
   否则**自更新预置**（存在 `selfupdate.flag`）在 LoadFrom 前校验 md5 并替换本体
   （一次重启即加载新程序集）；最后定位 `plugins\SurvivalLog.ModCenter.dll`（根 + 递归兜底）
   → `Assembly.LoadFrom` → 反射调 `CleanupRunner.Run()`（T16 探针实证：Chainloader 随后
   复用同一程序集实例 `SAME-ASM copies=1`，插件数不变、面板可开、无 IL2CPP 新增异常）。
2. **CleanupRunner 生命周期自证**：写 `MCCleaner_loaded.txt`
   （时间戳 + `CleanupRunner.Run v<程序集版本>` + dll 路径）——游戏内更新后此行的版本号
   即新程序集版本，证明清理逻辑随 dll 生效。
3. **marker 翻译**（Ruling 1/Ruling 9）：读 ModCenter cfg `[DisabledTp]`；
   `true + dll 在场` → disable 派生 op；`false + .disabled 在场` → enable 派生 op；
   **无键 + .disabled 在场 → 不动**（玩家手工禁用保持）。持久 pending 同 target 优先，
   翻译 op 执行完即弃（成功不写清单，失败留 `[MARKER]` 证据下次重译）。
4. **执行 pending ops**：逐条执行、幂等、MD5 校验、原子重写（§2.2）。证据
   `MCCleaner_ops.txt` 追加行（时间/操作/结果/原因）。自操作两阶段：
   delete 本体 → 写 `uninstalled.flag`；install/replace 本体 → 写 `selfupdate.flag`
   （证据 `[SELFUNINST]` / `[SELFUPDATE]`）；目标 md5 已一致 → 幂等成功。
5. **挂加载序覆盖**：Harmony postfix 挂封闭泛型
   `BaseChainloader<BasePlugin>.ModifyLoadOrder`；postfix 在链式加载器排序时读
   `pending.order` 并应用 `OrderReorder`（纯内存，不写文件）。失败记
   `[ORDER] HOOK-FAIL` 后放弃，不阻断启动。
6. **目录 sweep**：ops 消化完且 `BepInEx\ModCenter` 只剩 `staging`/`pending.json` →
   整个目录回收（ModCenter 自卸载收尾；引导壳在 patchers，与本目录无关）。存在
   `uninstalled.flag` / `selfupdate.flag` 时属"其他内容"→ 保守 SKIP。
7. **插件加载**：链式加载器按生效序加载各插件 → ModCenter `Load()`（自证、cfg、
   探针、Quit 双钩子）→ 面板可用。Load 时检查 `patchers\MCShell.dll` 是否在场；
   缺失 → 证据 `ModCenter_shell_missing.txt` + LogOutput warn + 快照 `shellWarn` 面板
   顶部告警（"启动清理组件缺失：更新/安装/卸载/停用/排序不会生效，请用安装器或完整包补装"）。
   壳在场时该证据自动清除。

补丁器阶段环境陷阱（实证，写新代码必读）：

- `Paths.ConfigPath` 在 patcher 阶段为**空串**（preloader 尚未赋值）；从
  `Paths.BepInExRootPath` 推导 `config`，不要直接拼 `Paths.ConfigPath`。
- ModCenter 的 cfg 文件名是 **GUID 命名** `com.local.survivallog.modcenter.cfg`
  （不是 `SurvivalLog.ModCenter.cfg`）。
- 补丁器阶段不引 `System.Text.Json`（可用性未实证）：pending 读写一律手写解析/序列化。
- plugins 下文件在补丁器阶段**无锁**（E4 实证 `File.Delete` 成功），这是"真删除"的前提。
- `Paths.PluginPath` 也可能未赋值：递归扫描/缺席行需回退 `BepInExRootPath\plugins`。
- **CleanupRunner/壳禁碰 IL2CPP 游戏类型**（T16）：它们在本体插件 Load 之前运行；
  只允许 BepInEx.* / HarmonyLib / System.IO / System.Reflection / 0Harmony 等。
- **已加载 dll 不可删**（T16 实证）：`File.Delete` 对壳 LoadFrom 的本体失败
  （`UnauthorizedAccessException`）；同程序集身份也不允许同靴重新 `LoadFrom`
  （`FileLoadException: Assembly with same name is already loaded`）。这是两阶段
  墓碑/预置的物理依据。

## 3. 消息协议全表

通道（两段，v1 契约原样保留）：

- **JS→C#**：面板把消息 push 进 `window.__mcOutbox`，C# 侧每 2s `Eval` 调
  `__mcDrain` 回读（JSON 字符串）入队；`ConfigBridge.Pump` 在 Ticker 主线程逐条分发。
  批内每条独立 try/catch——单条异常只弃该条，不弃同批后续。
- **C#→JS**：`WebBridge.Exec`（Root 上下文）直推。全量快照走 `window.__mcRender(snapshot)`；
  事件走 `window.__mcEvent({type, …})`。面板不在场时推送丢弃，不缓存（下次 MC_OPEN 重推）。
- **兼容纪律**：新字段一律可选/可缺省（`ics` 空表不输出字段、`g`/`w` 未指定走推断），
  老面板配新 C#、新 panel.js 配老 C# 均不破。

### 3.1 入（面板 → C#，outbox）

| type | 字段 | 行为 |
|---|---|---|
| `MC_OPEN` | `host` | 键盘门开（FocusTick）+ L2 立即扫描 + 推全量快照 + `MC_SCAN_DATA` + `MC_REPO_DATA`（15 分钟缓存；过期则起一轮后台 fetch——D6/Ruling 22）+ `MC_PENDING_SUMMARY` |
| `MC_CLOSE` | `why?` | 键盘门双还原 + 立即落盘全部脏 cfg；`why='host-doc-gone'` 为孤儿看门狗补发 |
| `MC_SET` | `guid, section, key, value` | 写值（Meta 键或第三方真实键）→ 回 `MC_SET_OK{k,badge}` / `MC_SET_FAIL{k,err}`；未登记键拒绝 |
| `MC_TOGGLE_MOD` | `guid, enabled` | 套件=直写该 mod `General.Enabled`（热还原）；第三方=marker/软停三层语义；回推新快照 |
| `MC_EVIDENCE` | `tag` | 验证辅助（只读）：写多表断言快照到 `ModCenter_evidence.txt`；正常玩家不触发 |
| `MC_REPO_GET` | `force` | （v3 起面板不再发送，保留兼容）打开拉取：15 分钟会话缓存内推缓存；fetch 在跑只推现状 |
| `MC_CHK` | — | 头部「检查更新」：60 秒冷却（GitHub 匿名 60 次/h），冷却内 only toast `刚检查过，稍后再试` |
| `MC_UPD` | `repoKey` | 确认更新 → 串行下载链（下载→zip MD5→解包 dll→暂存→写 pending） |
| `MC_INST` | `repoKey` | 安装（与更新同链；前端按 local==null 分流） |
| `MC_ORDER_GET` | — | 返回生效序（pending.order 命中前置 + 其余 GUID 字母序）+ 行元数据 + 默认序 |
| `MC_ORDER_SAVE` | `guids[]` | 白名单精确校验（可排序集必须不重不漏）；通过 → 写 pending.order；回 `MC_ORDER_OK` / `MC_ORDER_FAIL{err}` |
| `MC_UNINST` | `guid` | 构造卸载明细（self/套件/链上第三方/缺席行四路由）→ `MC_UNINST_DATA`；不可达 → `MC_TOAST` 拒因 |
| `MC_UNINST_DO` | `guid` | 写 delete（dll+cfg；缺席行只 `.disabled`）+ 清 marker + 移除 order 记录 + FlushDirty + 刷新；`self=true` 时另清自更新预置（T16） |
| `MC_CF` | `guid` | 冲突详情 → `MC_CF_DATA{guid,cards[]}`（L1/L2/L3 同源卡片） |
| `MC_SCAN` | — | L2 重扫（主线程）→ 推新快照 + `MC_SCAN_DATA` |
| `MC_RESTART` | — | FlushDirty → 拉分离进程（PID 精确等待→9s→steam 协议重拉）→ 1.6s 后 `Application.Quit()`；请求失败不退出 |
| `MC_PENDING_UNDO` | — | 删除 `pending.json`（ops+order 全清）+ 删除自更新预置 flag（T16）→ 同拍推快照/摘要 + toast |

### 3.2 出（C# → 面板）

| 事件 | 形状 | 说明 |
|---|---|---|
| `__mcRender` | `snapshot = {self:{ver}, mods[], tp[]}` | 全量快照；每 mod 可带 `ics[]`（§4.1）。MC_OPEN/MC_TOGGLE_MOD/各类写操作后推送 |
| `MC_SET_OK` | `{type,k,badge}` | 写值回执；`k`=展示键原串，`badge`=生效时机 |
| `MC_SET_FAIL` | `{type,k,err}` | 写值拒绝/异常；面板错误 toast 覆盖乐观提示 |
| `MC_TOAST` | `{type,main,small}` | 面板 toast 原文（`small` 可缺省）；守卫/拒绝/结果统一走此通道 |
| `MC_PENDING_SUMMARY` | `{type,n,labels[],applied?}` | 底部待生效条：`n/labels` 只计待生效档；`applied`=中性档（"自定义加载序生效中"） |
| `MC_REPO_DATA` | `{type,rows[12],lastCheck,checking,mirror,dl?}` | 仓库页全量状态；`rows[]` 含 `name/status/local/remote/why/repoKey/guid/gamekb?/verNote?/pending?` |
| `MC_DL` | `{type,repoKey,pct,stage,err?}` | 下载进度；`stage=down\|check\|unzip\|write\|ok\|err`；250ms/1% 双门限节流 |
| `MC_ORDER_DATA` | `{type,guids[],rows[],def[]}` | 排序模式数据；`guids`=生效序，`rows`=显示名/版本，`def`=默认字母序 |
| `MC_ORDER_OK` / `MC_ORDER_FAIL` | `{type}` / `{type,err}` | 保存结果；OK 后面板自动退出排序模式 |
| `MC_UNINST_DATA` | `{type,guid,name,self,files[],note?}` | 卸载明细（BepInEx 根相对路径）；`self=true` 为 ModCenter 自身 |
| `MC_CF_DATA` | `{type,guid,cards[]}` | 冲突卡片：`{lv,tag,title,who[],reason,tips[]}`；不在链上回空 `cards` |
| `MC_SCAN_DATA` | `{type,at,patched,game,total,routed,unknown,framework,ms,scope,error,items[],frameItems[]}` | L2 扫描汇总；`framework`=框架侧未计入组数，`frameItems`=披露明细；`items[].owners[]`/`frameItems[].owners[]`=显示名（v3：套件中文名/第三方插件名/`未知来源（id）`） |

### 3.3 快照 `ics` 字段

`ModInfo.ics`（空表不输出）：

| 字段 | 类型 | 说明 |
|---|---|---|
| `k` | string | 色级 `red\|orange\|blue\|amber`（v3 两级：gray 退役；旧快照 gray 由面板 icGlyph legacy 映射 ◎） |
| `tip` | string | 悬停完整提示（title） |
| `click` | string? | `cf`=冲突详情 / `upd`=更新确认；缺省=纯状态（不弹窗） |
| `g` | string? | 字形消歧（§4.1）；缺省按 `k+click` 推断 |
| `w` | bool? | 停用弱化（`General.Enabled=false` 的套件行；面板 `.__mc-weak`） |

## 4. 面板与图标语言（v3 两级）

> **布局/样式的可视对照稿 = 本目录 `prototype.html`（项目资产）**：独立浏览器可开，
> CSS 与 DOM 结构逐字对照 `panel.js`，模拟数据覆盖全部视图与弹窗状态，顶部有场景切换条。
> **同步纪律：调整布局样式先在 prototype.html 上改并目检，定稿后把改动回写 `panel.js`
> （CSS 字符串 + 对应渲染函数），两处必须同一次提交内一致**；历史原型
> （2026-10-24 v4 / 2026-10-28 v2）在套件仓 docs/superpowers/specs/，已由本文件取代为现行对照稿。
> **v3（2026-10-30 作者拍板，已实施）**：
> ① 提示信息全面精简（短句/白话/无术语），冲突两级化——**严重**（红 ⚠，明确冲突只能二选一）
> 与**普通告警**（橙，可能有问题；⇄脚本碰撞 / ◎功能叠加），gray 色级退役（◎ 用显式 `G="◎"`）；
> ② 取消独立「套件仓库」页——顶部「检查更新」按钮 + 列表行内图标 + 详情页「维护」区按钮，
> 按钮不进左侧列表；补丁碰撞汇总挪到底部图例栏；
> ③ 「重装当前版本」与「全部更新（MC_UPDALL）」退役（需要时先卸载再安装 / 逐仓更新）。
> 逐字文案总表与边界：`docs/superpowers/specs/2026-10-30-modcenter-v3-panel-design.md`。

### 4.1 图标语言（v3 两级：色随级、形随义）

18px 圆片、字号 9.5px、容器 flex `gap:3px`；悬停 `brightness(1.25)` + title 全文；
**义不单靠颜色**（WCAG 色义规则：每形字形不同）。冲突两级：红=严重（只能二选一/多选一）、
橙=普通告警（可能有问题；⇄/◎ 靠字形区分）。gray 色级退役（panel `icGlyph` 的 gray 分支
保留为 legacy——仅旧 C# 快照兼容）。

| 形 | 色级 | 含义 | 点击路由 | 数据源 |
|---|---|---|---|---|
| `⚠` | red | **严重冲突**（明确冲突，只能二选一/多选一） | `cf` | `Meta.KnownConflicts` 命中（tip=表内 `TipShort` 逐字） |
| `⇄` | orange | 普通告警·脚本碰撞（运行时补丁碰撞，每 mod 聚合一枚） | `cf` | `PatchScan.IcsFor`（tip=`脚本碰撞 ×N：与…`） |
| `◎` | orange | 普通告警·功能叠加（同域共存，只标注不判定；显式 `G="◎"`） | `cf` | `Meta.Domains` 域内已装登记 mod ≥2 |
| `↑` | blue | 可更新（远端 > 本地） | `upd` | `RepoDecision` State=Update |
| `↓` | orange | 可降级更新（远端 < 本地，D7 风险自担） | `upd` | State=Downgrade |
| `⊘` | orange | 游戏版本告警（D8 不拦截） | `upd` | State=Warn/kbw（`G="kbw"` 与 ↓ 消歧） |
| `⟳` | amber | 待生效（重启后应用） | 无（`cursor:default`） | pending.json 归属到本行的 op（tip 固定 `待生效：重启游戏后应用`） |

补充口径（v3）：

- **等宽布局**：图标容器 `flex-shrink:0`；行内图标顺序=冲突标 → 仓库态 → 待生效。
- **聚合**：L2 碰撞每行只挂一枚 ⇄（tip=`脚本碰撞 ×N：与{对方显示名}`，对方取前 2 个）；
  L1 多冲突可多枚。
- **停用弱化**（Ruling 15 / T12 M7）：套件行 `General.Enabled=false` 时冲突/碰撞/叠加标
  保留但 `w:true` + tip 前缀"（已停用）"——**补丁不因停用而移除**，不得表述为"已移除"。
- **待生效意图吃动作徽标**：同一行已有 pending 意图（含 delete 卸载）时只显 ⟳，
  ↑/↓/⊘ 消失（避免"看到可更新却已在卸载"）。
- 点击图标 `stopPropagation`，不触发行选中；⟳ 点击无动作。

### 4.2 弹窗与视图

- **冲突详情**（`cf`，v3 两级卡片）：h3=`{tag} · {mod名}`（tag=严重冲突/脚本碰撞/功能叠加）+
  一句话结论 + 涉及 mod + 原因（空则不渲染该段）+ 怎么办（编号列表）；
  底部固定风险行删除（各卡自含）；四钮：关闭 / 停用此 mod / 去调序 / 卸载此 mod…（转确认弹窗）。
- **更新确认**（`upd`，v3 精简）：版本对比一行；D7 回退红字、D8 告警红字仅异常时出现
  （均不拦截）；主钮按态着色（更新=琥珀 / 安装=蓝 / 降级=橙）。
- **仓库视图**：退役（v3）——顶部「检查更新」+ 行内图标 + 详情「维护」区承接；
  「全部更新」「重装当前版本」入口随之退役（D3/D4）。
- **卸载确认**（v3 精简）：一句话说明（存档/其他 mod 不受影响）+ 文件明细红字（末行固定
  "停用 / 排序记录（如有）"）；self 分支红字（两靴 + 引导壳基座级保留）。
- **进度弹窗**：`进行中` + 内联进度条（下载/校验/解包/写入待生效）→ `已就绪` / `{动词}失败`；
  进度更新不重建弹窗；可「隐藏」。
- **补丁碰撞汇总**（v3）：底部图例栏按钮（有碰撞才显示）→ 一句结论 + 碰撞位置（中文 mod 名）
  + 框架侧未计入披露段；可「重新扫描」。
- 全部弹窗为面板内浮层；ESC 先关弹窗再关面板；✕/点遮罩为保底路径。

### 4.3 底部待生效条

- 文案：`待生效 N 项（label · label）· 重启游戏后应用`；`n=0` 时隐藏。
- **聚合**：同 guid 的多 op 合成一档（如 dll+cfg 两条 delete → "卸载 X"）；
  动词序：卸载/安装/更新/停用/启用；order 单开一档 `加载顺序调整`。
- **生效中性档**：`order` 已在本靴生效（首拍有效序相同或 pending mtime 早于进程启动）
  → `applied="自定义加载序生效中"`，弱化呈现、不计入 `n`。
- 两钮：撤销（删除 pending.json，ops+order 全清）／立即重启（§5.7）。
- `order==默认字母序` 不进摘要（与删除 order 等效，避免常驻噪音）。

## 5. 生命周期五链

> 五链共用同一介质与执行者：意图全部落成 `pending.json`（§2.2），文件动作全部由清理器在
> 下次启动、插件加载前完成。每条链给"机制 + 装机实测要点"，证据索引见 §9。

### 5.1 网络层 RepoClient（v2 生命周期任务 5）

更新源契约 = 拆仓设计 §6/§7（冻结）：`GET https://api.github.com/repos/survivallog-mods/{repo}/releases/latest`
→ release assets 里找 `update.json` 资产 → 下载解析五字段 {mod, version, asset, md5, gamekb}；
zip 资产名 = update.json.asset 字段值。接口签名按任务 brief 冻结，T6/T8 消费。

#### 5.1.1 机制

- **`RepoClient`（静态类）**：`FetchLatest(repoName)` 两跳（latest 元数据 + update.json，各受
  CheckTimeout 约束）→ `UpdateInfo{Mod,Version,Asset,Md5,Gamekb,AssetUrl}`；五字段任一缺失 /
  zip 资产不在场 / 网络/非 2xx/JSON 坏 → **返回 null**（不抛不重试）。`DownloadAsset(info, progress)`
  下载 zip 到 `McPending.StagingDir`：先落 `<asset>.part`，成功后关流→清陈旧→`File.Move` 落位
  `<asset>` 并返回绝对路径（T8 pending op 的 Staged 即其 BepInEx 根相对路径）；progress 在
  下载线程每 64KB 回调（total<0=无 Content-Length），节流归调用方；任何失败清 .part 后**抛出**。
  另供 `MapUrl`（internal，镜像映射）与 `Md5Of`（internal，小写十六进制，T8 校验消费）。
- **UA 与头**：`User-Agent: survivallog-modcenter`（GitHub API 必带）常驻客户端默认头；API 请求
  另附 `Accept: application/vnd.github+json`。单例 HttpClient（跟随系统代理；总超时无限——
  超时由每次请求的 CTS 按 cfg 键控制，改 cfg 即时生效）。
- **cfg（Network 节，绑定在 ModCenterPlugin.Load）**：`MirrorPrefix`（默认空=直连）、
  `CheckTimeoutMs`=5000、`DownloadTimeoutMs`=60000（下限保护 500）。镜像前缀语义：非空时对
  `https://api.github.com/...` 与 `https://github.com/...` 两族 URL 前置镜像域
  （`<前缀>/https://github.com/.../xxx.zip` 形态，拆仓设计 §7），其余 URL 原样。
- **线程纪律（控制器裁定）**：全部 HTTP 只在后台 Task 跑（调用方 Task.Run），任何同步阻塞
  主线程=缺陷；探针自证走"后台写共享状态+证据文件，Ticker 主线程收割"（ConfigBridge.Pump 同款）。
- **`NetProbe` 探针（cfg `Debug.NetSelfTest` 门控，默认关，一轮即终态）**：Ticker 10s 后拉起
  后台 Task 对 `survivallog-mods/RobotSpeed` 跑 fetch（版本/md5/gamekb+耗时）→ 下载
  （25% 台阶进度）→ MD5 对 update.json → **删 zip 不留残留**；Done 旗由 Ticker 收割打印。
  证据 `BepInEx/ModCenter_netspeed.txt`（硬规则 9）。

#### 5.1.2 镜像选型实测（开放点 3 收口，2026-10-29 本机）

| 前缀 | api.github.com | release 下载 | 结论 |
|---|---|---|---|
| （直连） | 200 | 200 | 默认取值（本机直连可达） |
| `https://gh-proxy.com` | 200（真 JSON） | 200 | **README 示例取值**（双端点全通） |
| `https://ghproxy.net` | 403 "Invalid input." | 200 | 只代理下载，不能作本键取值（fetch 必败） |
| `https://ghfast.top` | 403 | 200 | 同上 |
| `https://mirror.ghproxy.com` | 超时 | 超时 | 不可用 |
| `https://gh.ddlc.top` | 429 | 429 | 限流不可用 |

**默认值定案：空=直连**（本机及目标玩家群直连普遍可用；ghproxy 类镜像寿命/限流不稳，
且多数只代理下载不代理 API）。README 记 `https://gh-proxy.com` 为示例并写明双端点要求。

#### 5.1.3 装机实测记录（2026-10-29 三轮 boot，主菜单阶段，探针证据）

- **直连**：fetch OK 3054ms（version=1.7.0 / md5=3468ea…f5b6 / gamekb=2026-10-23_26157732，
  与仓上 update.json 逐字一致）→ 下载 OK 825ms 22577B → MD5 match=PASS → zip 已删、staging 清空。
- **镜像 gh-proxy.com**：fetch OK 2490ms + 下载 OK 744ms，md5 PASS，同上清理（两族 URL 均走镜像）。
- **失败降级**（MirrorPrefix=https://127.0.0.1:9 不可达）：fetch=null @2069ms（< CheckTimeout
  5000ms 上限，秒级失败零重试），staging 零触碰；LogOutput 顺序证明 Ticker 主线程全程存活
  （后台 FAIL 行之后收割行照常打印），面板无卡顿。
- 过程实坑（直连第 1 轮抓出即修）：`File.Move` 落位留在 FileStream 的 using 声明块内——
  Windows 句柄锁使 100% 下载后 IOException（using 声明在块尾才释放）；改显式 using 块先关流再
  Move 后复验全绿。证据全文存 `.superpowers/sdd/2026-10-28-modcenter-v2-lifecycle/task-5-netspeed-*.txt`。

### 5.2 决策层 + gamekb 版本通道（v2 生命周期任务 6）

消费 T5 `RepoClient.FetchLatest`（失败=null）与 v1 §4.1① `IL2CPPChainloader.Instance.Plugins`；
产出 `RepoDecision`（12 仓五态）+ `GameVersion`（本地游戏版本通道，spec §6 开放点 2 收口）+
`PendingWriter`（pending 意图主线程唯一写者）。D7/D8 均**只告警不拦截**（作者拍板）。

#### 5.2.1 决策口径（五态与优先级）

- 基态（D7）：未装=Install；本地>远端=Downgrade（Why="远端低于本地"，面板复用提示）；
  远端>本地=Update；相等=Ok。版本比较走 SemanticVersioning（csproj 补引
  `BepInEx\core\SemanticVersioning.dll`——PluginInfo.Metadata.Version 的类型所在程序集，
  BepInEx.Core 只是转发引用不内嵌）；远端不可解析=Ok 保守不动作（自家管线不会发生）。
- **Warn 覆盖**（D8）：仅对可动作态（Install/Update）判 gamekb——远端快照名 `_N` 后缀 ≠
  本地基线 buildid → Warn（Why=原因文案，按钮仍可用）；Downgrade/Ok 不参与改态
  （Downgrade 自身即告警态；Ok=同版本无动作面）。
- **基线不可用**（通道失败/无法映射）：不判 Warn，`VerNote="未校验游戏版本：…"`（独立字段，
  不占 Why——控制器裁定"Why 仅 warn 态非空"；fetch 成功行均可携带）。
- **fetch 失败**：Remote=null + State=Ok（五态无 fail 态，不虚报 update/warn；面板按 Remote
  null 渲染"检查失败"）。
- `RepoRow.Status` 固定小写五值 `"ok"|"update"|"downgrade"|"warn"|"install"`（控制器裁定 2，
  T7/T8 直接消费）。冻结面外增量字段：`Gamekb`（fetch 附带）与 `VerNote`。
- 接口：`RepoDecision.RepoKeys`（12 仓键）/ `GuidOf`（=com.local.survivallog.<小写仓键>，已对
  Meta.cs 九表 + config 目录实表核对）/ `DllRelOf`（plugins/SurvivalLog.<仓键>.dll，release.ps1
  打包布局）/ `SnapshotLocal()`（**主线程**，IL2CPPChainloader）/ `Build(src, localGame=null)`
  （纯函数任意线程；省略 localGame=ReadCached）。

#### 5.2.2 gamekb 通道选型（开放点 2 收口；证据 BepInEx/ModCenter_gamever.txt）

两侧形态：远端 gamekb=快照目录名 `YYYY-MM-DD_<N>`，`N`=**Steam buildid**（快照 meta.json
buildid 与目录名后缀逐字一致；release.ps1 L61 只取 `^\d{4}-\d{2}-\d{2}_\d+$` 最新快照）。
本地四候选实测：

| 候选 | 实测值 | 结论 |
|---|---|---|
| ①interop `UnityEngine.Application.version` | `1.1.19611`（=③复合版本，bundleVersion 即全量内容版本） | 与③互为佐证；无 buildid，不进基线 |
| ②Steam appmanifest（gameRoot 上两级 `appmanifest_4164790.acf` 的 `"buildid" "N"`） | `26157732` | **定案主通道**：与快照名 `_N` 直接相等比较，零映射表 |
| ③`SurvivalLog_Data\Version.txt`（1.1/19611/…） | 复合 `1.1.19611`（=快照 meta.json runtime_assets_version） | 后备展示通道；Baseline 需对照表（占位空表） |
| ④`SurvivalLog.exe` FileVersionInfo | `6000.2.7.10444810`（Unity Player 版本） | 非游戏内容版本，弃用（依据落证据） |

失败路径（全 null）：appmanifest 与 Version.txt 均不可读 → Baseline=null + Note=原因 →
决策层不判 Warn、标"未校验游戏版本"（D8 照常放行）。appmanifest 在但 Version.txt 不在 →
Baseline=buildid、Display="build N"（可比对不降级）。

**映射方案（报告定稿，实现占位）**：appmanifest 通道失效场景（非 Steam 布局/文件被清）下，
`AssetsToGamekb` 对照表（键=Version.txt 复合版本，值=快照名）由发版管线扫 `gamekb\*\meta.json`
生成、随版内置插件——`GameVersion.ReadCore` 已留挂点；更优解是 release.ps1 在 update.json 增补
`buildid` 字段（免表直比，五字段契约加一个可选键），T8 前二选一拍板。

#### 5.2.3 PendingWriter（控制器裁定 3）

静态薄封装 McPending，**主线程唯一写者**（Ticker/消息分发上下文；后台写=缺陷）。三口：
`AddUpdate(row, info, stagedAbs)`（install/replace 按本地在场与否；Target=DllRelOf；Staged=
staging 绝对路径转根相对；Md5=update.json）、`AddDelete(dllRel, cfgRel)`（cfgRel 调用方传
GUID 命名，T4 实证 BepInEx cfg 按 GUID 命名）、`SetOrder(order)`（T9，ops 原样）。合并语义：
读旧→**旧 ops 在前新 ops 在后**→原子写，order 恒回传旧值不丢 T3 顺序覆盖；坏文件移存 `.bad`
自愈后从空表开始。Save 防抖不必（写频率极低）。

#### 5.2.4 RepoSelfTest 探针（cfg `Debug.RepoSelfTest`，默认关，一轮即终态）

Ticker 10s 拉起（NetProbe 同款三拍）：主线程跑 gamekb 四候选 dump（→ModCenter_gamever.txt，
interop 候选须主线程）+ SnapshotLocal；后台 Task 一轮 12 仓顺序 fetch（匿名限流预算内，
不连环多轮）→ 四档自证：**A** 真仓真基线 12 行五态分布；**B** 强制基线 null（Step 4：断言
warn=0 不误报 + 未校验标注行数=fetch 成功行数）；**C** 合成分支自证（合成 RepoRow 走同一
`Build` 路径——不碰本机枚举/网络，覆盖五态+形态异常+fetch 失败+通道失败翻转）；**D**
PendingWriter 合并语义对临时文件 `selftest-pending.json` 自证（旧前新后+order 保留，
验后即删——**真 pending.json 全程零触碰**）。证据 `BepInEx/ModCenter_reposelftest.txt`。

#### 5.2.5 装机实测记录（2026-10-29 01:58 boot 一轮，主菜单阶段，直连）

- **通道**：四候选全 dump（gamever.txt 8 行）——①`Application.version='1.1.19611'`（主线程 interop
  正常）②appmanifest buildid=`26157732` ③Version.txt 复合=`1.1.19611` ④exe=Unity 版本（弃用依据）；
  定案 channel='appmanifest+versionfile' baseline='26157732' display='1.1.19611 (build 26157732)'。
- **A 真仓真基线**：12/12 fetch OK @20.1s（全部 remote=1.7.0 gamekb=2026-10-23_26157732，与远端
  release 逐字一致）；分布 `ok=11 update=0 downgrade=1 warn=0 install=0`——QuickAction 本地
  2.1.2（Metadata.Version，dev 版领先）> 远端 1.7.0 → Downgrade + Why（D7 实证）。
- **B 通道失败降级**（强制基线 null，Step 4）：warn=0（不误报）+ 未校验标注 12/12 行（= fetch
  成功行数）→ PASS；QuickAction 保持 Downgrade（Why 不受影响）。
- **C 合成分支自证**：10 用例全 PASS，五态覆盖 `ok=2 update=1 downgrade=1 warn=3 install=1`
  （含快照名形态异常→Warn、fetch 失败→Ok+Remote null、通道失败→Install/Update+未校验不误报 Warn）。
- **D PendingWriter 合并语义**（临时文件）：追加后 ops=3（旧2前新1后）+ order=2（g1,g2 保留）→
  PASS；selftest-pending.json 验后即删；真 pending.json 与 staging 全程零触碰（BepInEx\ModCenter
  目录不存在）。
- Ticker 收割行（LogOutput）：`DONE A[ok=11 update=0 downgrade=1 warn=0 install=0] fetchFail=0 |
  B 通道降级 PASS | C 五态分支 PASS | D 合并语义 PASS`；全程无 warn/error。
- 证据全文存 `.superpowers/sdd/2026-10-28-modcenter-v2-lifecycle/task-6-gamever.txt` /
  `task-6-reposelftest.txt`（BepInEx 侧原件已按惯例清，副本在 sdd 目录）。

### 5.3 更新链与检查更新（v3：无独立仓库页；机制承接 v2 任务 8）

> v2 的"套件仓库页"（12 行五态 + 检查/全部更新按钮 + 上次检查行 + 入口卡）已退役；
> **机制全部保留**，入口改为三处：顶部「检查更新」按钮（`MC_CHK`）、列表行内图标
> （↑/↓/⊘，点击直接确认）、详情「维护」区按钮（`maintHtml`：更新到 X / 安装 X / 降级到 X）。

- **查询节流**：面板打开自动查一次（15 分钟会话缓存，D6/Ruling 22 不变）+ 手动 60 秒冷却
  （冷却内 toast `刚检查过，稍后再试`）+ **绝不后台轮询**。检查完成汇总 toast 由面板拼
  （JS 在 `MC_CHK` 后等 `MC_REPO_DATA`：`检查完成：N 个可更新、…`/`都是最新版`，spec D8）。
  **403 专门提示**：`GitHub 限流`+`请稍后再试`（`RepoData.rateLimited` 口径不变）。
- **fetch/下载链**：与 v2 完全一致——后台 12 仓顺序 fetch → `RepoDecision.Build` 五态 →
  串行下载（`MC_DL` 节流）→ zip MD5 → 解包暂存 → `PendingWriter.AddUpdate` → 行内 ⟳。
- **`MC_UPDALL`（全部更新）退役**（v3/D4）：面板入口与 C# 分支/处理器删除；Warn/降级/
  安装一律逐仓确认。
- **「重装当前版本」退役**（v3/D3）：`repoVerbOf` 重装分支与 ok 行按钮删除；需要时先卸载
  再安装。Ok+kbw 行的 ⊘ 图标仍可走"更新到同版本"确认（D8 不拦截口径不变）。
- **详情「维护」区（`maintHtml`）**：仓库行 pending → 文本 `⟳ 待生效 · 重启游戏后应用`；
  update/downgrade/install 且 Remote 在 → 按钮 + 版本灰字；非 tp 非 missing 才有「卸载…」行
  （hint=`删文件，重启后生效`）；missing 只有安装行。__self 页保留仅卸载的 `uninstActHtml`
  （hint=`需重启两次`）。
- **行内图标 tip 逐字**：见 §4.1 表（未装行不产 ↑ 图标——v2 同口径，安装走「维护」区）。
- **装机实测（v2 历史记录，结论不受呈现层改动影响）**：RobotSpeed 本地 1.6.0 → 面板查到
  1.7.0 → 确认 → 下载 22.5KB 全程约 2s → `pending.json` 落 `replace` + staging dll 在场、
  无 zip 残留、`op.md5 == 暂存 dll MD5` → 重启 → 清理器 `replace ... OK` → ⟳ 消失、底条隐藏。
  守卫/冷却/D7 侧栏 ↓/Remote=null 合成态均实测。

### 5.4 手动排序（v2 生命周期任务 9）

- **面板**：排序模式 = 侧栏平铺全列表（不分组），行 = 拖柄 `⋮⋮` + 两位序号 + 名 + 版本 +
  冲突 chip；拖拽（插入指示线）与键盘 ↑↓ 等价（WCAG 2.2 AA）；行点击不选行，只显示
  `click:"cf"` 的冲突 chip；"恢复默认"（本地重排）与"保存顺序"（写 pending）。
- **生效序**：`pending.order` 命中项按序前置 + 其余 GUID 字母序（与清理器 `OrderReorder`
  语义一致）；可排序集 = 当前 Plugins − 探针族；幽灵/重复项丢弃。
- **保存校验**：收到 `guids[]` 必须与可排序集**精确相等**（条数+成员，大小写不敏感）；
  重复/白名单外/条数不符 → 整单拒绝 `MC_ORDER_FAIL`（pending 不动）。通过 →
  `PendingWriter.SetOrder` 整替（ops 原样保留）→ 摘要即时推。
- **清理器侧**（`OrderReorder.Apply`，纯函数）：优先级 = order 命中序 / `orderCount+原索引`
  （全表唯一 → 稳定）；Kahn 拓扑排序每步取"硬依赖已全放置"项中优先级最小者 →
  order 项前置、其余保持原相对序、硬依赖冲突项被拉回其依赖之后；软依赖不参与；
  依赖环兜底尾置（正常不可达）；输出 `[ORDER-PROTECTED] guid(依赖 dep)`。
- **不变量**：order 覆盖是**纯内存** postfix，不改任何 dll/cfg；卸载时从 order 移除该 guid
  （避免幽灵引用）；`order==默认序` 与删除 order 等效（摘要不亮）。
- **装机实测**：18 行（20 插件 − 2 探针）拖拽保存 → `pending.order` 18 项 == DOM 序 →
  重启 `[ORDER]` 20/20 与 `LogOutput` 实际 Loading 序逐位吻合；恢复默认后再次重启回到
  字母序；GHOST guid 注入被拒且 pending 未动。

### 5.5 彻底卸载（v2 生命周期任务 10）

- **明细**（`MC_UNINST` → 四路由）：①ModCenter 自身（self 特别分支）②套件 mod（Meta 解析）
  ③链上第三方（`FindTp`，探针/第一方已豁免）④缺席行（`AbsentDisabled`，guid=dll 相对名）。
  cfg 路径严格取 `PluginInfo.Instance as BasePlugin → Config.ConfigFilePath`，不在场不列
  （不自拼 `<GUID>.cfg`）。回包 `MC_UNINST_DATA{guid,name,self,files[],note?}`。
- **写入**（`MC_UNINST_DO`）：`PendingWriter.AddDelete(dllRel, cfgRel)`（缺席行只写
  `.disabled`，同在场冗余 `.disabled` 一并列删）→ marker 处理（链上第三方 `Enable`；
  **缺席行不动 marker**——终审必修 2：marker=false + `.disabled` 在场会被清理器翻成
  enable 复活，卸载意图只由 pending delete 承载；套件/self 跳过）→
  `pending.order` 移除该 guid → `FlushDirty()`（marker 立即落盘，不留防抖窗口）→
  刷新摘要/快照/仓库数据。重复确认同目标 delete → 幂等 toast，不二次写。
- **强杀免疫**：意图已落盘；下次启动 CleanupRunner 在插件加载前 `File.Delete`（E4 无锁）——
  即使确认后立即 `taskkill`，被卸载 mod 也不会复活。
- **ModCenter 自身卸载（T16 两阶段墓碑）**：DLL 被壳 `Assembly.LoadFrom` 后文件锁定，
  **不能在本靴自删**。boot N：CleanupRunner 遇到 delete 本体的 op 不执行，写
  `BepInEx\ModCenter\uninstalled.flag`（时间戳 + cfg 路径）、该 op 从清单移除（视为已接管），
  证据 `[SELFUNINST]`；boot N+1：壳见 flag → 递归删 `plugins\` 下本体（含 `.disabled`）+
  自身 cfg + `BepInEx\ModCenter\` 目录（含 flag）→ **不加载**本体 → 退出（入口消失）。
  引导壳 `MCShell.dll` 保留（基座级；可用安装器卸载或手动删除）。⇒ 面板内"卸载本面板"
  需要**两次重启**；面板弹窗与 README 均按此口径。
- **目录 sweep**（`RunDirSweep`）：ops 消化完、`BepInEx\ModCenter` 只剩 `staging`/`pending.json`
  时整目录递归删除。守卫：order 非空时必须 `selfUninst=true`（本靴接管过 ModCenter
  自卸载）**且**面板 dll/cfg 全形态不在场（递归 plugins 扫 dll 与 `.disabled`、cfg 按
  `McCfgPath`）；否则 SKIP（防手工停用/子目录安装时误删 order 记录）。T16 自卸载靴因
  `uninstalled.flag` 属"其它内容"必然 SKIP，目录由 boot N+1 壳清理。有其它内容/解析
  失败一律保守放弃。
- **带出项**：已有 delete 意图的行只显 ⟳（吃 ↑/↓/⊘）；`ORD.src` 存 guid（拖拽会话不受
  键盘重排影响）；`HandleOrderSave` 异常回 `MC_ORDER_FAIL` 且复位 saving。
- **装机实测**：8 boots——载入第三方 sample1 强杀后仍完成卸载、sample2 常规卸载三次启动
  无残留、缺席行只删 `.disabled`、disable 双在场幂等、`RunDirSweep` 空态回收；
  self 卸载未真机执行（会拆面板），只验弹窗与 pending 写入。

### 5.6 停用/启用迁移（v2 生命周期任务 2/4，DllDisabler 退出机械退役）

- **意图源**：ModCenter cfg `[DisabledTp]` 节，key = **plugins 目录下 dll 相对名**
  （dll 禁用后插件不在链上，GUID 无从解析）；面板停用=写 marker + 刷新 + FlushDirty，
  启用=写 false + 立即反向改名（dll 未加载不锁；失败 warn，下次启动清理器重译自愈）。
- **执行点**（清理器，补丁器阶段先翻译后执行）：
  - `marker=true` 且裸 dll 在场 → disable op（改名 `<dll>.disabled`）；
  - `marker=false` 且 `.disabled` 在场 → enable op（反向改名，**仅显式 false 触发**）；
  - 无键且 `.disabled` 在场 → **不动**（Ruling 9：玩家手工改名代表玩家意图；面板缺席行
    继续供显式启用）。
  - 派生 op 与持久 op 走同一 `ExecuteOp`（幂等/守卫语义免费复用），执行完即弃；
    失败留 `[MARKER]` 证据，下次启动重译重试。
- **待生效语义**：面板横幅/toast 三档——缺席行（"dll 已禁用：当前未加载"）、
  Tier 2 软停（"行为已立即停止"，`OnEnabledChanged(false)` 本会话生效）、重启级
  （"本次会话仍在运行"）。所有档位都写 marker（dll 改名兜底"下次启动不再加载"）。
- **插件侧 ScanOnLoad**：`.disabled` 在场 → 预 Bind marker 键（防 BepInEx Save 丢未 Bind
  的 true）+ 日志；dll 在场且 marker=true（清理器异常没跑）→ 预 Bind + `Residue` 登记 +
  Tier 2 `ReapplySoft` 软停重放；均不在场 → 惰性忽略。无残留补拉、无 toast。
- **幂等化**（T10 带出）：disable 时 dll 与 `.disabled` 双在场 → 删裸 dll、保 `.disabled`、
  记 OK（不再每 boot 一条 FAIL）。
- **装机实测**：A 停用→重启（20→19 插件、LogOutput 0 次加载）；B 启用→重启恢复；
  C 无键 `.disabled` 手工停用保持（Ruling 9）；D 运行中写意图 + `taskkill /F` → 下次启动
  仍完成改名（v1 的"退出时改名"失效窗口结构性消失）；Tier 2 软停重放日志实证。

### 5.7 立即重启助手与待生效撤销（v2 生命周期任务 13）

#### 5.7.1 机制

- **重启链**（`RestartHelper.cs`，spec §4.1）：面板底部待生效条"立即重启"→ 确认弹窗 →
  `MC_RESTART` → `ConfigBridge.HandleRestart`：先 `FlushDirty()` 落盘脏 cfg，再
  `RestartHelper.Request()` 拉分离 PowerShell（`StartDetachedPS`：`-NoProfile
  -WindowStyle Hidden -EncodedCommand` + base64(UTF-16LE)，脚本 ASCII-only）。分离脚本：
  ①`Get-Process -Id <本进程PID>` 轮询（1s 一拍，上限 180s）等**本进程**退出（PID 精确等待，
  不按进程名——同机可能有别的实例）；②退出后 `Start-Sleep 9`（仓库硬规则：Steam 对刚杀
  实例有 8~10s 延迟）；③`Start-Process 'steam://rungameid/4164790'`（必须走 steam 协议，
  直接起 exe 会被 Steamworks 转拉导致插件哑火）。**超时未退出则不重拉**（防双实例）。
  拉起成功后 C# 先推 toast（"正在重启游戏…/已请求重启：本进程退出后经 Steam 重拉（约 10 秒）"），
  `ConfigBridge.Pump` 延迟 1.6s 调 `RestartHelper.QuitGame()`（`Application.Quit()`，主线程；
  让 toast 先上屏）。拉起失败（PS 不可用）→ toast 报错且**不退出**（口径："已请求重启"≠
  "重启成功"，不假装成功）。连点/双发只会拉起一条等待链（`_restartRequested` 守卫）。
- **撤销**（`MC_PENDING_UNDO`）：删除 `pending.json`（ops 清空；**order 一并清空**=回到默认
  字母序——"撤销全部待生效操作"包含本会话刚保存的顺序）+ 删除自更新预置 flag（T16），
  同拍强制重读 + 推新快照/摘要
  （行内 ⟳ 与底条一起消失）+ toast；删除失败只报错不清内存态。staging 不做运行期清理
  （启动清理器 DirSweep 回收）。
- **面板**：`pendEl` 事件委托（`[data-x]`）——撤销直接入 outbox；立即重启开面板内浮层确认
  弹窗（文案："将退出游戏并在约 10 秒后自动重启以应用 N 项待生效操作。未保存的对局进度
  会丢失。"，取消/确认重启；n=0 仅中性档时去掉"N 项待生效操作"）。两钮 T7 占位 `disabled`
  已移除；结果一律 C# 回推，面板不做乐观提示。

#### 5.7.2 装机实测记录（2026-10-29，主菜单阶段，CDP 9222，最终构建）

- **撤销链**：伪造 pending `{"ops":[{"op":"delete","target":"plugins/T13Noop.dll"}],
  "order":["com.local.survivallog.robotspeed"]}`（无害假件：不存在目标=清理器幂等成功）→
  底条显示"待生效 2 项（卸载 T13Noop · 加载顺序调整）"→ 点撤销 → 约 1s 内底条隐藏、toast
  "已撤销全部待生效操作/加载顺序一并恢复默认（字母序）…"、`pending.json` 已删除（目录空）。
- **重启链时间戳**（最终构建）：14:33:00.799 确认弹窗出现 → 14:33:01.403 点确认 →
  14:33:01.905 toast"正在重启游戏…" → ~14:33:05.0 游戏进程退出（确认后约 3.4s=2s drain+
  1.6s 延迟）→ 14:33:18.368 新进程被 Steam 拉起（退出后约 **13.4s**：1s 轮询 + 9s 延迟 +
  Steam 自身启动）→ 14:33:18.964 清理器 Initialize → 14:33:18.974
  `delete plugins/T13Noop.dll OK 目标不在场(幂等)` → 18.977 pending 原子重写 0/1 →
  19.008 `[DIRSWEEP] OK ModCenter 目录已清空` → 14:33:22 插件 loaded → 14:33:39.669 面板
  重开：底条隐藏、`MC_PENDING_SUMMARY{n:0}`。
  连点/双发分支在前一版构建（与最终构建仅差一条 toast 文案）实测：第二次只回"重启已请求"，
  时间线只出现一个新 PID，无第二实例。更早一版构建同流程（14:21 序列）见
  `task-13-cleaner-evidence.txt` 的 T13Noop 三行证据。
- **口径注记**：面板文案"约 10 秒"为用户可读近似；实测退出→重拉 13.4s（9s 延迟 + 1s 轮询 +
  Steam 拉起约 3.4s）。不为追数字改文案（避免承诺过精确）。
- **回归**：`mc_fixture.js menu` ALL PASS（14 SKIP，99s）、`toggle` ALL PASS（113s）。
- 证据：`task-13-live-timeline.txt`（进程/CDP/pending 0.5s 采样）、`task-13-cdp.txt`
  （弹窗文案/toast/撤销 DOM）、`task-13-cleaner-evidence.txt`（清理器 ops 行）。

## 6. 冲突体检三层

### 6.1 L1 已知冲突库 + L3 功能域标注（v2 生命周期任务 11）

- **L1 `Meta.KnownConflicts`**（表驱动，新增条目只改表；随 ModCenter 发版内置，不做在线拉表）：

  | # | 匹配 GUID | 匹配 dll | 级别 | 冲突对象 | 处置建议 |
  |---|---|---|---|---|---|
  | 1 | `com.jangfullmoon.storagecapacityexpansion` | `StorageCapacityExpansion.dll` | red | CabinetSize / BackpackExpand / CabinetEverywhere | 3 条 |
  | 2 | `survivallog.expandedstorage` | `ExpandedStorage` | orange（功能叠加 ◎，v3 由 gray 并入） | 背包扩容 | 1 条 |

  首条 v2 原因逐字含"改写 73 件柜族家具 BagId 至共享包并改包尺寸、强写 ColdRate/PowerCost
  ……镜像缓存错位失效"（v3 起两条目的 TipShort/Tag/Title/Reason/Tips 按 spec §5.2 逐字重写，
  上句为 v2 断言史录）。`com.jangfullmoon.karinasprite` **明确排除**（纯立绘替换、零游戏钩子，
  同作者包名易误联想，收录只会误报）——表尾注释防后人误加。
- **匹配 `Meta.MatchL1(guid, dllName)`**：GUID 全表优先 → dll 文件名全表兜底；文件名只取
  最后一段、自动剥 `.dll`（可省）、`OrdinalIgnoreCase`；纯函数（无 BepInEx 依赖）。
- **L3 `Meta.Domains`**：12 套件 mod 全覆盖（储物 5 / 规划 1 / 速度 2 / 机器人 1 / 角色 1 /
  面板 1 / 其他 1）+ 已知第三方 4（sce、expandedstorage=储物；bettertrade=交易；statsui=面板）；
  karina 不登记。表只用于"同域共存"灰标，**只标注不判定**。
- **快照 ics 产出**（`ConfigBridge.BuildSnapshot`，一次枚举全行复用）：
  1. 本行命中 L1 → 每条一个 ics（`k=red|gray`，`click:"cf"`），tip=`冲突 · {Title} · 对象 {who} · 处置建议 N 条`；
  2. 套件行被已装冲突源波及（`Entry.Who` 含本行 En 且冲突源确在链上）→ 同款 ics（未装不误报）；
  3. L3 同域（本行在域表、域内已装登记 mod ≥2）→ 灰 ics（tip 列同域其他已装 mod）。
  未加载行（套件 dll 缺失/缺席行）不产冲突标；多冲突多 ics（BackpackExpand 红+灰实测）。
- **`MC_CF` 卡片**：`{guid,cards:[{lv,tag,title,who[],reason,tips[]}]}`；顺序与 ics 同源，
  L1 按 red→orange→gray 稳定排序（同级保表序），L3 卡最后；Who 渲染"中文名（En）"；
  不在链上/缺席行回空 `cards`（面板空态壳，不误报）。L3 卡 `lv=gray/tag=同域/
  title=同域共存（功能域标注）`，tips=`保留一方` / `维持现状（接受叠加结果）`。
- **呈现**：冲突详情弹窗每卡三段（冲突对象/原因/编号处置建议），底部固定风险文案；
  三钮：关闭 / 停用此 mod（走既有 `MC_TOGGLE_MOD`）/ 卸载此 mod…（转 T10 确认弹窗）。
- **实测**：37 项单元自证（含首条原因逐字断言）+ 43 项 CDP 实测（多图标、灰卡标题、
  停用后弱化、卸载转场）；T11 十项 Minor 中 M2–M7 并入 T12 顺手修（第三方不误挂灰、
  L1 已有卡片跳过 L3 灰、灰卡标题、显示名、文案空格、停用弱化）。

### 6.2 L2 运行时补丁碰撞实测（v2 生命周期任务 12）

#### 6.2.1 探针实证（开放点 1 收口；证据 `BepInEx/ModCenter_patchscan.txt`）

- 环境：本机 BepInEx 6 IL2CPP 构型，`0Harmony 2.10.2.0`（HarmonyX），20 插件（12 套件 + 6 第三方 + 2 探针）。
- **结论：Il2CppInterop 挂的游戏方法补丁 owner 可读，归组可用**。`Harmony.GetAllPatchedMethods()`
  返回 58 个已挂补丁方法（**游戏侧 55 / 非游戏侧 3**：`System.Console.SetOut`、
  `BepInEx.BaseChainloader.ModifyLoadOrder`、`Vuplex.BaseWebView.HandleMessageEmitted`）；
  `Harmony.GetPatchInfo(m)` 对每个方法返回 `HarmonyLib.Patches`，
  `Patches.Owners`（`ReadOnlyCollection<string>`）与各补丁实例 `Patch.owner` 字段**两路一致**，
  `Patch.PatchMethod` 可读（补丁方法声明类/名完整）。`GetAllPatchedMethods().Contains(m)` 为 True
  （patch 字典键与 Attribute 补丁的目标 MethodBase 同一身份）。
- 第一方已挂方法 dump（fix round 2 上限提高后，9 个第一方全部入 dump）：8 个套件游戏侧挂点——
  `BagComponent.OnInit`（cabinetsize）、`EndlessManager.FallMapPoint` / `Initialize` /
  `SurvivalPlanningComponent.OnInitBuffs`（endlessguard）、`SurvivalResultsManager.OnBuySurvivalPlan`
  （planpick）、`WebUIVm.HandleCallback` / `GameTimeManager.ChangeGameSpeed`（predisasterspeed）、
  `Ac_ToolTable_RefreshBag.SendAction`（cabineteverywhere.coalesce）——owner=各 mod Harmony id，
  与 `new Harmony(Guid)` 约定吻合；补丁方法声明类均在插件程序集。
  + 1 个第一方非游戏挂点：`BepInEx.BaseChainloader.ModifyLoadOrder`（owner=
  `com.local.survivallog.modcenter.order`，CleanupRunner 排序补丁）。
  （fix round 1 时第一方候选上限=8，实际 dump 为 **7 个套件游戏侧挂点 + ModifyLoadOrder**；
  `GameTimeManager.ChangeGameSpeed` 恰因上限被截断未入 dump。fix round 2 上限提至 10 后 9 个
  第一方全部入 dump。另 fix M-6：探针"套件 owner"判定改第一方前缀 `com.local.survivallog.`；
  此前 `IndexOf("survivallog")` 会把 `com.dev./com.ivmakk.survivallog.*` 第三方也当套件，
  套件挂点反被挤出。）
- **本机实测的 ≥2 owner 方法共 9 个**：8 个游戏侧（BetterTrade × ProjectCook × StorageHub × StatsUI
  两两/三方共挂：FurnitureTagRules.Evaluate/TryGetMatchRank/EnsureCache、FurnitureTagComponent.TryGetPutRank、
  WebUILayer.OnUpdate、WebUI_BackpackUI.CallShowAction、ItemManager.CollectTradeContainers、
  Reducer_Data_Item.RA_GetCookingBagList）+ 1 个框架侧（Vuplex `BaseWebView.HandleMessageEmitted`，
  3 owner）。**当前套件自身没有同方法多补丁场景**（brief 假设的"柜子系×快速系同挂表方法"未出现：
  套件仅 CabinetSize / CabinetEverywhere / EndlessGuard / PlanPick / PreDisasterSpeed 五处 Harmony 挂点，
  目标互不重叠）——扫描报告非空来自真实第三方两两碰撞；套件归属/行内橙标/停用弱化用一次性
  验证插件构造实证（见 §6.2.3）。
- 性能：全量枚举 + 每方法 PatchInfo 读取 **0~8ms**（本机），**主线程直接扫**（裁定 1，无需后台 Task）。

#### 6.2.2 机制（`PatchScan.cs`）

- **扫描时机**：MC_OPEN（面板打开即扫）+ MC_SCAN（手动"重新扫描"）；不后台轮询（裁定 1）。
  结果缓存 `PatchScan._last`，快照 ics / MC_CF 卡片 / MC_SCAN_DATA 三处同源。
- **归组**：同方法 ≥2 owner 一条；owner 双路径合并去重（Patches.Owners + Patch.owner，防版本差异）。
  `patchedCount` = prefix+postfix+transpiler+finalizer+ilmanipulator 实例数。
- **范围过滤与统计口径**：只看游戏侧方法（声明类型程序集=`HotUpdate` 或命名空间 `GameCore.*`）；
  框架（BepInEx/HarmonyLib/MonoMod）、引擎（UnityEngine/Vuplex）、插件自身内部方法不计入碰撞组数。
  `InScope`/`Skipped` 按**全部已挂补丁方法**计数（含单 owner）；碰撞组数单独 `MultiOwnerGame`
  统计——面板"游戏侧 55/58 方法"不再等于碰撞组数（fix I-1）。框架侧多 owner 组
  （`MultiOwnerFramework`/`Framework`）继续过滤不铺行，但 `MC_SCAN_DATA.framework/frameItems`
  与面板汇总行/弹窗**必须披露"框架侧 N 组未计入"**（Ruling 14②；实测 Vuplex
  `BaseWebView.HandleMessageEmitted` 3 owner 真实共挂不再隐身）。计数口径=可读取 `PatchInfo` 的
  已挂补丁方法；`GetPatchInfo` 异常/返回 null 的方法不入 `InScope/Skipped`（实测 0 例，注释留档）。
- **owner→mod 归属**（裁定 2）：①精确 GUID 全表 ②前缀（owner 以 `<GUID>.` 开头，如
  `com.local.survivallog.quickaction.trace` → quickaction）③其余=未知来源（显示"未知来源（id）"，
  不臆造归属）。两遍扫描保证精确优先于前缀。
- **行内橙标**（裁定 3；v3 tip 精简）：每个 mod **聚合一枚** ⇄（tip=`脚本碰撞 ×N：与{对方显示名}，点看详情`，多组不铺图标洪水），click=cf 复用冲突详情弹窗；`MC_CF` 返回该 mod 涉及的每组一张橙卡（tag=脚本碰撞，title 一句话含方法名，who=owner 显示名名单）。
- **未知来源组不铺行**，只进扫描汇总（`MC_SCAN_DATA` 的 `unknown` 计数与 `[未知来源]` 组）。
- **卡片文案**（裁定 3；v3 精简）：title=`和「{对方}」改了同一处游戏逻辑（{方法名}），后加载的生效，行为可能异常。`；reason 不再单列；tips=`一般不用管` / `出现异常就停用其中一个，或试试「去调序」`。
- **面板**：侧栏仓库入口卡下方"补丁碰撞 N 组"汇总行（可归属/未知来源/框架侧未计入/游戏侧方法数/
  耗时/扫描时刻；点击弹汇总窗，列出全部游戏侧组 + 框架侧"未计入"披露区，含"重新扫描"→ MC_SCAN）；
  橙标点击走既有 cf 弹窗。
- **M7（T11 Minor）**：套件行 `General.Enabled=false` 时，行内 cf 类徽标（红/橙/灰）保留但加
  `w:true`（面板 `.__mc-weak` 透明度 .45）且 tip 前缀"（已停用）"——保留信息量又不误导。
  **裁定 C（Ruling 15）语义**：停用=入口隐藏/General.Enabled 关闭（套件行为停写），**已挂
  Harmony 补丁不从账本移除**；扫描仍会看到该 owner，徽标因此保留、仅标注停用态（不表述为
  "补丁已移除"）。

#### 6.2.3 装机实测记录（2026-10-29 主菜单阶段）

- **真实环境**（6 第三方在场）：扫描 8 组（可归属 8 / 未知来源 0 / **游戏侧 55/58 方法** /
  **框架侧 1 组未计入** / 0~8ms）。BetterTrade 与 ProjectCook 各 7 组（行内各一枚 ⇄ 聚合标），
  StatsUI 1 组，StorageHub 2 组；cf 弹窗逐卡对拍=方法名+双方显示名+原因+tips（含"后写覆盖前写"/
  "风险自负"）；汇总弹窗 8 组全列 + "框架侧未计入（1 组，非游戏方法）"披露区
  （`BaseWebView.HandleMessageEmitted`：StorageHub/NeighborBasket/BetterTrade 3 owner）；"重新扫描"
  往返（时间戳更新、弹窗就地重绘）。DOM 文本证据 `task-12-cdp.txt`（fix round）。
- **构造验证**（一次性插件，验后删除）：
  ① `T12L2Dummy` 以 `com.local.survivallog.quickaction.trace` 给 `BagComponent.OnInit` 挂 no-op
  postfix → 与 CabinetSize 成一组；**前缀归属**到 QuickAction/CabinetSize，两行橙标；cf 弹窗橙卡
  who=动作加速（quickaction）、柜子容量（cabinetsize）；停用 CabinetSize 后其橙+灰标 `w:true`
  （DOM class `__mc-weak`、tip"（已停用）"），重启前恢复。
  ② `ExpandedStorage.dll`（GUID `survivallog.expandedstorage`，命中 L1 灰）→ BackpackExpand/
  ExpandedStorage 各**仅 1 枚 L1 灰标**（M3：跳过 L3 同域灰），灰卡标题"同域 mod"（M4）。
  ③ `T12UnknownDummy`（两个不可归属 id `t12.unk.a/b`）→ 与 StatsUI 混组的卡 who 显示
  "未知来源（t12.unk.a）/（t12.unk.b）"；全未知组 `BagComponent.SetBagConfigId` 出现在汇总
  `[未知来源]`（可归属 9 / 未知 1），**不挂任何行**；插件行自身 0 图标。
- 截图（目检通过）：`task-12-shot-a-sidebar-orange.png`（侧栏橙标+汇总行）、`-b-cf-orange-modal.png`
  （7 卡橙弹窗）、`-c-scan-modal.png`（汇总窗）、`-d-rescan.png`（重扫后）、`-e-suite-orange.png`
  （套件橙卡+灰卡"同域 mod"）、`-f-weak-disabled.png`（停用弱化）、`-g-m3-l1gray.png`（M3/M4）、
  `-h-unknown-who.png`（未知来源 who）、`-i-unknown-summary.png`（未知来源汇总）。
- **T11 六 Minor（同任务修）**：M2 `Meta.Domains` 删除 bettertrade/statsui（第三方仅进 L1 名录后带
  类型）→ 实测两行只剩橙标、无灰标；M3 L1 已有卡片跳过 L3 灰标（`CfIcsFor`/`HandleCf` 同规则）→
  实测 1 枚灰；M4 灰卡标题"同域 mod"（含 L3 与 L1 灰卡）；M5 `Meta.DisplayNameOf` 补
  CabinetBurden=柜子负重解放 / ModCenter=Mod 管理中心（L3 who 实测显示中文）；M6 "储物功能域"去空格；
  M7 见 §6.2.2。自检 23 PASS（`task-12-selftest.txt`）。

### 6.3 三层呈现与边界

- **图标优先级与聚合**（v3 两级）：冲突标置最前（L1 红=严重 / L2 橙⇄=脚本碰撞 / L3 橙◎=功能叠加），
  随后仓库态（↑/↓/⊘），最后 ⟳。L2 每行聚合一枚 ⇄（tip=`脚本碰撞 ×N：与…`，多组不铺图标洪水）；
  L1 多冲突可多枚。
- **L2 范围口径**（Ruling 14）：只看游戏侧方法（声明类型程序集 `HotUpdate` 或命名空间
  `GameCore.*`）；框架（BepInEx/HarmonyLib/MonoMod）、引擎（UnityEngine/Vuplex）、插件自身
  内部方法不计入碰撞组数。**框架侧多 owner 组继续过滤不铺行，但必须在扫描汇总披露
  "框架侧 N 组未计入"**（实测 Vuplex `BaseWebView.HandleMessageEmitted` 3 owner 真实共挂）。
  全未知 owner 组进汇总 `[未知来源]`，不挂任何行（Ruling 14①）。
- **停用语义边界**（Ruling 15）：套件 mod 停用只隐藏入口/写 `General.Enabled`，
  **已挂 Harmony 补丁不会从账本移除**（本会话仍生效）；面板/DESIGN 文案不得表述为
  "补丁已移除"。停用行保留冲突/碰撞/同域标但加 `w:true`（透明度 .45）+ tip 前缀"（已停用）"。
- **不自动处置**：三层只标注与解释，停用/卸载/调序全部由玩家点击；调序建议旁固定
  风险文案（"只能规避部分冲突（写序类），不能解决数值叠加类冲突"）。
- **L3 叠加标范围**（Ruling 13；v3 由灰改橙 ◎=普通告警）：维持 spec 定义（域内已装登记
  mod ≥2 即标注），只标注不判定。
- **数据同源**：快照 ics 与 `MC_CF` 卡片共用 `EntriesFor / DomainMates` 一套口径，
  杜绝两处漂移；L2 的 ics/卡片/汇总共用 `PatchScan._last` 一份缓存。
- **探针**：`Debug.PatchScan`（默认关）跑一轮 dump 到 `BepInEx/ModCenter_patchscan.txt`，
  含第一方挂点全量（上限 10）+ 常用游戏方法补足样本（总 dump 上限 12）；扫描本身
  0~8ms 主线程直跑，无后台轮询。

## 7. 退役记录与不变量

### 7.1 DllDisabler 退出机械退役（v2 生命周期任务 4）

v1 的第三方重启级禁用流程 = 面板写 `[DisabledTp]` marker → 游戏退出时（`OnApplicationQuit`
+ `Application.quitting` 双钩子）`FlushDirty` 后拉**分离 PowerShell** 循环 `Rename-Item`
把 dll 改成 `.disabled`，并带残留补拉/撤销句柄/`MC_TOAST` 提示。

**退役**（`DllDisabler.cs` 删代码，非改语义）：`OnQuit`、`SpawnDisableBatch`、`SpawnReverse`、
`Armed` 句柄表、`MarkedTrue`、`_pendingToast`/`ConsumeToast`、`DisableRetryMs`/`ReverseRetryMs`、
`StartDetached`（迁至 `RestartHelper.StartDetachedPS`，现仅用于一键重启）。

**保留**：`MarkerOf/Disable/Enable`（意图写入；Enable 保留即时反向改名——dll 未加载不锁，
失败 warn + 下次启动清理器重译自愈）、`ParseMarkerTrues`、`ReapplySoft`（Tier 2 软停重放）、
`ScanOnLoad`（语义改为三路对照预 Bind/Residue）、`AbsentDisabled`、`TrySetAbsent`、
`DllRelOf`、`IsSafeRelName`。

**迁移动机**：退出路径对强杀有结构性失效窗口（退出钩子没跑 → dll 仍加载）；迁移后
"意图在盘上、每次启动先于插件加载执行"，强杀场景仍生效（T4-D 实测）。代价 = 停用/启用
必须重启才落地（面板文案已是重启级）。

**Ruling 9 修正**：`enable` 翻译只在 marker **显式 =false** 时触发；无键 + `.disabled`
在场代表玩家手工禁用，清理器不动（v1 字面实现会把它自动恢复，属破坏行为）。
面板"缺席行"仍可供显式启用（写 false → 下次启动改名）。

### 7.2 双槽方案证伪（Ruling 8）→ 单 dll 化定案（T16 / Ruling 21）

- **原方案**（spec 初稿 §4.7）：清理器双槽 `MCCleanerA.dll` + `MCCleanerB.dll` 同装，
  游戏内更新写非运行槽、重启轮换，避免"自己更新自己"的死锁。
- **T1 实证证伪**：两个 dll 都被 BepInEx `Examining`、各产出一个 patcher type、
  `2 patcher plugins loaded`，`Initialize` 执行两次——**执行层成立**；但两行自证都报告
  `Assembly.Location = ...\MCCleanerA.dll`。两份 dll 程序集简单名+版本完全相同
  （`MCCleaner 1.0.0.0`），CLR 按**同一程序集身份**解析，第二份复用了第一个 Assembly 对象。
  ⇒ 槽自识别（写 A 读 B）不可靠，双槽前提不成立。
- **中间定案（Ruling 8，已被 T16 取代）**：清理器**单槽** `BepInEx\patchers\MCCleaner.dll`，
  基座级组件、不参与游戏内自更新；执行器守卫 patchers\ 下目标。该定案牺牲了"清理逻辑可
  随面板更新"，且 T16 单 dll 化后不再成立。
- **T16 定案（Ruling 21，方案 A）**：清理逻辑全部并入 `SurvivalLog.ModCenter.dll`
  （`CleanupRunner`），`patchers\` 只留**永久冻结**的引导壳 `MCShell.dll`（无独立版本线，
  1.0.0 仅属性占位）。游戏内更新 ModCenter 即更新清理逻辑；壳只在接口签名变更时随安装器更新。
  - 自更新：面板写 `selfupdate.flag`，壳在下一靴 LoadFrom 前替换本体 → **一次重启生效**；
  - 自卸载：boot N 写 `uninstalled.flag`（墓碑），boot N+1 壳删本体+cfg+目录 → 两阶段。
- **教训**：同名同版本程序集在 CLR 中会被身份合并；用文件名/路径区分"槽位/实例"不可靠，
  必须用独立 AssemblyName 或独立进程。T16 追加两条更硬的物理事实：**已加载程序集文件不可
  `File.Delete`**（`UnauthorizedAccessException`），**同程序集身份不允许同靴重新
  `Assembly.LoadFrom`**（`FileLoadException`）——因此自操作必须靠"下一靴加载前执行"的
  两阶段 flag，而不是"自己替换自己"。

### 7.3 其他退役/作废口径

| 对象 | 旧口径 | 现行 |
|---|---|---|
| `[Disabled]<guid>` 名单 | v1 T13/T14 的本插件 cfg 停用名单 | 退役；套件启停直写各 mod `General.Enabled`，第三方用 `[DisabledTp]` marker；旧 cfg 孤儿节无人读（惰性） |
| `ConsumeToast` / 残留补拉 toast | v1 强杀残留提示 | 退役（无残留补拉概念）；panel.js 的 `MC_TOAST` 渲染保留（面板自身结果通知仍用） |
| 分离进程自删清理器运行槽 | spec 初稿 §4.3 / Ruling 12 前文案 | **作废**：引导壳在 patchers 本就不在本体运行槽；卸载收尾=两阶段墓碑 + 目录清理（T16），壳永不动 |
| 双槽 A/B 轮换 | spec 初稿 §4.7 / plan T14 文案 | **作废**（§7.2），单槽 |
| 单槽 MCCleaner.dll 基座级（Ruling 8） | 清理器独立 1.0.x 版本线、不参与游戏内自更新 | **作废**（T16/Ruling 21）：清理逻辑并入 ModCenter.dll；patchers 只留冻结 `MCShell.dll`；游戏内更新本体即更新清理逻辑 |
| `StartDetached`（DllDisabler） | v1 退出改名执行器 | 迁 `RestartHelper.StartDetachedPS`，唯一用途=一键重启 |
| 第三方 `[Disabled]` 标记名单 | v1 T6/T14 | 退役（T16 已换 `.disabled` 真禁用），本版不留读取方 |
| 独立「套件仓库」页视图 | v2 任务 8（12 行五态/检查行/全部更新按钮/入口卡） | **v3 退役**（spec D2）：顶部「检查更新」+ 行内图标 + 详情「维护」区承接；fetch/下载链机制不变，`MC_REPO_DATA` 照推 |
| `MC_UPDALL`（全部更新） | v2 Update 态整批串行 | **v3 退役**（spec D4）：面板入口与 C# 分支/处理器删除，逐仓确认 |
| 「重装当前版本」入口 | 必修 5（D7）ok 行重装按钮 | **v3 退役**（spec D3）：需要时先卸载再安装；Ok+kbw ⊘ 仍可"更新到同版本"确认 |
| `gray` 冲突色级 | v2 三级（红/橙/灰，◎=同域灰标） | **v3 退役**（spec D1）：功能叠加并入 orange + 显式 `G="◎"`；panel `icGlyph` gray 分支保留为 legacy |

### 7.4 幂等不变量（所有 op 必须幂等）

**红线**：任何新增 op 类型/执行分支，必须定义"目标已达成时重跑"的行为，且不得产生额外
副作用或重复证据。当前执行器口径：

| op | 已达成状态 | 行为 |
|---|---|---|
| delete | target 不在场 | 成功（幂等） |
| delete | target = 本体 dll（T16） | **不执行**：写 `uninstalled.flag`、op 视为已接管（移除），下一靴壳删除 |
| install | target 已存在 | 可覆盖（先删旧再 copy） |
| install/replace | `MD5(target) == op.md5`（T16） | 成功（目标已是期望内容，壳已预置/清单重跑） |
| install/replace | target = 本体 dll（T16） | **不本靴落盘**：写 `selfupdate.flag`、op 视为已接管（移除），下一靴壳加载前替换 |
| replace | target 不在场 | 同 install（可安装） |
| disable | target 不在场且 `.disabled` 在场 | 成功（幂等，原因注明） |
| disable | 双在场 | 删裸 dll、保留 `.disabled`、成功（T10 幂等化） |
| enable | target 已存在 | 成功（幂等） |
| enable | target 与 `.disabled` 均不在场 | 成功（幂等） |

- 执行器只在**有成功条目**时原子重写清单；无成功条目不重写（重跑零 churn，T2 轮 4
  字节级不变实证）。
- marker 派生 op 执行完即弃（成功不写 pending.json），失败留 `[MARKER]` 证据由下次启动重译。
- 目录 sweep 的守卫（self-uninstall 签名 + 面板全形态不在场）也是幂等纪律的一部分：
  误判会删掉玩家 order 记录，宁可 SKIP。
- 已知边界：install/replace 是"删旧 → `File.Copy`"（非 tmp+rename）。copy 极端中断可能留下
  半写 target，下次启动重试会覆盖；若未来需要更强原子性，可改为 tmp+`File.Replace` 并重新
  验证 BepInEx 加载时序（当前未发生实害，记录备查）。

### 7.5 其他不变量

- **主线程唯一写者**：`PendingWriter`（pending.json）、`ConfigBridge` 快照/写值、`PatchScan`
  （Harmony 静态账本无锁）全部在 Ticker/消息分发主线程；后台 Task 只入队回传。
- **原子落盘**：pending.json 与 update.json 走 tmp + `File.Replace`/`File.Move`；
  证据文件追加写、UTF-8 无 BOM（`MCCleaner_loaded.txt` 首行 BOM 为 .NET `Encoding.UTF8`
  首写行为，无碍）。
- **不写游戏本体**：面板只写各 mod cfg 与 `BepInEx\ModCenter`；不改存档、不改游戏资源；
  删 dll 即还原（v1 安全承诺保留）。
- **无后台轮询**：网络只在面板打开/手动触发；L2 只在 MC_OPEN/MC_SCAN；pending 巡检为
  2s mtime 比较（本地零网络）。
- **键盘门**：面板开=键盘全量进 WebView + IME On；关=双还原；宿主 iframe 消亡由孤儿
  看门狗补发 `MC_CLOSE`（v1 §6.6 机制原样保留）。

## 8. 维护注意

1. **基类虚方法被覆写则 postfix 静默失效**（T3 评审 Minor2，BepInEx 升级必复验）：
   加载序覆盖挂的是 `BaseChainloader<BasePlugin>.ModifyLoadOrder` 的 postfix。若未来
   BepInEx 版本在派生类（如 `IL2CPPChainloader`）**覆写**了该虚方法、或链式加载器改走
   另一条排序路径，postfix 会**静默不执行**（无异常、无日志）。复验方法：置一个自定义
   order 重启，看 `MCCleaner_ops.txt` 是否出现 `[ORDER] 加载序覆盖生效`，并与
   `LogOutput.log` 的 Loading 序对拍。必须挂**封闭泛型**（开放泛型会被 Il2CppInterop
   的 Harmony 后端以 `ContainsGenericParameters` 拒绝）。
2. **所有 op 必须幂等**（§7.4）：新增 op 前先写清"已达成状态"的判定，再加执行分支；
   禁止"失败即删清单"或"成功也重复写"的写法。
3. **L2 探针上限**：`PatchScan` 的 `Debug.PatchScan` 探针 dump 有硬上限——第一方套件
   挂点候选**上限 10**（当前 9 个），总 dump **上限 12**。新增第一方 Harmony 挂点后若不做
   提升，探针样本会被**静默截断**（扫描本身不受影响，只影响诊断文件）；调上限时连同
   `Take(10)`/`12` 两处一起改。
4. **补丁器阶段陷阱**（§2.5）：`Paths.ConfigPath`/`PluginPath` 可能为空；cfg 按 GUID 命名；
   不引 `System.Text.Json`；路径只接受 BepInEx 根相对且拒绝 `..`/盘符/绝对路径；
   plugins 文件此时无锁（真删除前提）。**CleanupRunner 与壳禁碰 IL2CPP 游戏类型**（它们在本体
   Load 之前运行）；已加载 dll 不可删、不可同靴重新 LoadFrom（T16 实证）——自操作一律两阶段。
5. **发版链（单一版本 + 三件套断言）**：ModCenter csproj `<Version>` = `Plugin.cs` const；
   `MCShell.csproj` 固定 1.0.0（属性占位，不校验版本）；`release.ps1` 断言 zip 内
   `BepInEx/plugins/SurvivalLog.ModCenter.dll` + `BepInEx/patchers/MCShell.dll` + `README.md`
   且 dll 恰两枚；`installer.iss` 的 `mod_center` 组件必须含 `MCShell.dll`、
   `[InstallDelete]` 必须保留旧 `MCCleaner.dll`（v1 升级清理）并预删旧 `MCShell.dll` 与
   `BepInEx\ModCenter\` 的 selfupdate/uninstalled flag。发版号由作者定（硬规则 1），
   CHANGELOG 必须有 `## v<版本>` 节，否则 `release.ps1` 发版模式拒绝。
   **升级文案硬要求（T16 I-2）**：CHANGELOG/ReleaseREADME 必须写明"本版起 patchers 组件
   换名 `MCShell.dll`，必须用安装器/完整包或手动放置；只删旧 `MCCleaner.dll` 会让启动清理链
   失效（更新/卸载/排序/停用不再执行）"。
6. **安装器与 pending 队列的交互（待作者裁定的开放点）**：安装器只替换 dll，不清理
   `BepInEx\ModCenter\pending.json`/`staging`。若玩家在旧版面板里排了待生效 op 后直接跑
   安装器升级，下次启动 CleanupRunner 仍会执行旧 op（例如把刚装的新版 replace 回旧暂存 dll）。
   当前按"不越权删除用户意图"保守处理；T16 起自更新/自卸载预置 flag（selfupdate/uninstalled）
   已被 `[InstallDelete]` 预删（安装器替换本体后旧意图必须作废）。若要收口 pending.json，
   可在 `[InstallDelete]` 预删——需作者拍板。
7. **L1 冲突库维护**：新增条目只改 `Meta.KnownConflicts`（GUID 优先、dll 名兜底）；
   必须有反汇编/实机证据，不收录纯立绘/零钩子 mod（karina 为反例，表尾有注释）；
   第三方只有进入 L1 才带 L3 类型。
8. **快照/协议兼容纪律**：新增字段一律可选、空表不输出；`ics`/`g`/`w` 均有推断缺省；
   改 panel.js 与 C# 两侧时先跑 `mc_fixture.js menu` + `toggle`（主菜单阶段即可）。
9. **证据文件清单**（BepInEx 根，硬规则 9）：`ModCenter_loaded.txt`、`ModCenter_kb.txt`、
   `MCShell_loaded.txt`（壳生命周期/墓碑/自更新）、`MCCleaner_loaded.txt` / `MCCleaner_ops.txt`
   （CleanupRunner 执行证据，文件名沿用 T1/T2 格式，loaded 行含程序集版本）、
   `ModCenter_shell_missing.txt`（T16 I-2：缺壳告警，壳在场自动删）、`ModCenter_patchscan.txt`、
   `ModCenter_gamever.txt`、`ModCenter_reposelftest.txt`、`ModCenter_netspeed.txt`、
   `ModCenter_probe.txt`（按需）；另有派生披露文件
   `BepInEx\ModCenter\order_protected.txt`（加载序依赖保护，每靴由 CleanupRunner 覆盖，见 §5.4）
   与接口 flag `BepInEx\ModCenter\uninstalled.flag` / `selfupdate.flag`（两阶段自操作，见 §2.1）。
   自动化验证后清理游戏侧部署与临时文件，证据副本存 suite 仓 sdd 目录。
10. **壳接口变更才需安装器（T16 取代终审必修 10）**：pending 契约（`McPending.Op`/`order`/
   `order_protected` 等）与清理逻辑同住 ModCenter.dll，**随本体发版即可**，不再需要同步
   分发独立清理器。但引导壳是永久冻结件：**改动以下接口任一 → 必须同步更新安装器里的
   MCShell.dll**：①`uninstalled.flag` 的存在性/内容格式与删除范围；②`selfupdate.flag`
   的三行格式与替换语义；③`SurvivalLog.ModCenter.CleanupRunner.Run()` 的签名/类型名/程序集名。
   壳对未知/损坏 flag 一律留证据后按旧路径继续（绝不阻断启动）。**缺壳保护（I-2）**：插件
   Load 检查 `patchers\MCShell.dll` 存在性；缺失 → `ModCenter_shell_missing.txt` 证据 +
   LogOutput warn + 快照 `shellWarn` 面板顶部告警（不阻断本体加载，但明确告知生命周期链失效）。
11. **框架侧过滤是启发式（终审必修 10）**：`PatchScan` 的框架/引擎方法过滤按程序集与命名
   空间启发式判定，不是硬编码全表；未来游戏大版本拆程序集或引入新命名空间时，真实游戏
   方法可能被误归"框架侧"（只披露、不计入碰撞组），须在游戏更新后复核过滤规则与
   `ModCenter_patchscan.txt` 证据。

## 9. E2E 实测记录索引

> 完整报告与原始证据在 suite 仓 `.superpowers/sdd/2026-10-28-modcenter-v2-lifecycle/`。
> 本表只记"哪条链、什么结论、证据文件"，细节看对应 task-N-report。

| 链 / 任务 | 实测结论（摘要） | 证据 |
|---|---|---|
| T1 清理器骨架 / 单槽 | 补丁器阶段 Initialize 可达；双槽被 CLR 身份合并证伪 → 单槽定稿 | `task-1-report.md`；LogOutput `Examining`/`Loaded 1 patcher type` |
| T2 pending 契约 + 执行器 | 5 轮 boot：转义往返、幂等复跑零 churn、MD5 失败保留、自身槽拒绝守卫 | `task-2-report.md`、`task-2-evidence-ops.txt`、`task-2-evidence-loaded.txt` |
| T3 顺序覆盖 | selftest 构造序 + 真实 20 插件 order 对调；LogOutput 实际加载序 1:1 | `task-3-report.md` |
| T4 停用迁移 | 停用/启用/手工停用保持/强杀场景四组；`[MARKER]` 证据；Ruling 9 返工 A/B | `task-4-report.md` |
| T5 网络层 | 直连/镜像/不可达三组；fetch+下载+MD5+清理；失败 2s 内返回不卡主线程 | `task-5-report.md`、`task-5-netspeed-*.txt` |
| T6 决策层 + gamekb | 12 仓五态（ok=11/downgrade=1）；通道失败不误报；合成 10 用例；PendingWriter 合并 | `task-6-report.md`、`task-6-gamever.txt`、`task-6-reposelftest.txt` |
| T7 图标 + 待生效条 | 七形渲染/路由、⟳ 聚合、底条、ics 空表与 v1 视觉一致 | `task-7-report.md`、`task-7-shot-*.png` |
| T8 仓库页 + 更新链 | 1.6.0→1.7.0 真实下载/校验/暂存/重启 replace；冷却/守卫/Remote=null 合成态 | `task-8-report.md`、`task-8-shot-*.png` |
| T9 排序 | 18 行拖拽/键盘保存 → 重启 Loading 序 20/20 吻合；恢复默认；GHOST 拒绝 | `task-9-report.md` |
| T10 卸载 | 8 boots：强杀免疫、缺席行只删 `.disabled`、DirSweep 空态回收、self 弹窗 | `task-10-report.md`、`task-10-shot-a-uninst-modal.png` |
| T11 L1 + L3 | 37 项单元自证 + 43 项 CDP；红/灰多图标、灰卡、停用弱化 | `task-11-report.md`、`task-11-cdp.txt`、`task-11-selftest.txt` |
| T12 L2 碰撞 | 真实 8 组（55/58 游戏侧方法、框架侧 1 组披露）；构造未知来源/前缀归属 | `task-12-report.md`、`task-12-patchscan-evidence.txt`、`task-12-shot-*.png` |
| T13 重启 + 撤销 | 确认→3.4s 退出→13.4s Steam 重拉→清理器消费 op→底条归零；撤销 ops+order 全清 | `task-13-report.md`、`task-13-live-timeline.txt`、`task-13-cleaner-evidence.txt` |
| T16 单 dll 化 | 探针 4 点全绿（LoadFrom / 反射调用 / 同程序集 `SAME-ASM copies=1` / 无新增 IL2CPP 异常）；五类 op、marker 翻译、排序 20/20、两阶段自卸载、游戏内自更新（新程序集版本证据）、D6、夹具 menu+toggle | `task-16-report.md`、`evidence-t16/*` |

## 10. v2 迭代清单（任务与落点）

| 任务 | commit 区间（modcenter-v2） | 落点 | 本文章节 |
|---|---|---|---|
| T1 清理器骨架 + 单槽定稿 | `db29478..79392b4` | `MCCleaner/` 新项目；Ruling 8 | §2、§7.2 |
| T2 pending 契约 + 执行器 | `79392b4..0cad6d3` | `Contracts/McPending.cs`；CleanerPatcher 执行段 | §2.2、§7.4 |
| T3 顺序覆盖 | `0cad6d3..e0a6a5c` | `OrderReorder` + ModifyLoadOrder postfix | §5.4 |
| T4 停用迁移（含 Ruling 9） | `e0a6a5c..d93d7f3` | marker 翻译；DllDisabler 退出机械退役 | §5.6、§7.1 |
| T5 网络层 | `d93d7f3..79531e9` | `RepoClient.cs` + Network cfg + NetProbe | §5.1 |
| T6 决策层 + gamekb | `79531e9..7059ac6` | `RepoDecision.cs` / `GameVersion.cs` / PendingWriter | §5.2 |
| T7 图标徽标 + 待生效条 | `7059ac6..cecbf9e` | `panel.js` 七形图标/弹窗/底条；Payload `ics` | §3.3、§4 |
| T8 仓库页 + 更新链 | `cecbf9e..8437079` | ConfigBridge 仓库态机 + 下载链；panel 仓库视图 | §5.3 |
| T9 排序模式 | `8437079..3ca82b1` | MC_ORDER_* 协议；拖拽/键盘；待生效 order 档 | §5.4 |
| T10 彻底卸载 | `3ca82b1..d5cc890` | MC_UNINST*；DirSweep；待生效中性档 | §5.5 |
| T11 L1 + L3 | `d5cc890..3e169ad` | `Meta.KnownConflicts` / `Domains`；MC_CF | §6.1 |
| T12 L2 碰撞 | `3e169ad..295aac3` | `PatchScan.cs`；橙标/汇总/探针 | §6.2、§6.3 |
| T13 重启 + 撤销 | `295aac3..46111e2` | `RestartHelper` 实装；MC_RESTART / MC_PENDING_UNDO | §5.7 |
| T14 发版链 + 文档同步 | 本任务 | `release.ps1` 双产物；installer.iss；DESIGN/CHANGELOG | §2.3、§8.5 |
| T16 单 dll 化（追加） | 本任务 | `CleanupRunner.cs` 迁入；`MCShell/` 冻结壳；删除 `MCCleaner/`；release/installer/DESIGN 同步 | §2、§7.2、§8.5/§8.10 |

- 版本与发版（2026-10-28 起，v2 迁移后）：版本号单源=本仓 csproj `<Version>`（作者定义）；
  **T16 起只有一条版本线**（清理逻辑在 ModCenter.dll 内）；`MCShell.dll` 固定 1.0.0
  属性占位、不构成版本线；单 mod 发版=本仓 `.\release.ps1`；套件编排=suite `tools\publish.ps1`
  （各仓 `-PackageOnly`）；N 网发布仍是作者另行确认的手工步骤，不自动化。
- v1 历史迭代条目见附录 B「v1 §7」。

# 附录 B — v1（历史）设计归档

> 以下为 v1（2026-10-24 定稿：游戏内配置面板，无生命周期链）原文归档，
> **章节号保留 v1 原编号**（正文引用写作「v1 §x.y」）。
> 已被 v2 取代、仅作历史参考的机制：
> - v1 §2 的三层架构表只列 v1 组件；v2 现行组件表见 §2.4；
> - v1 §6.3 的 `[Disabled]<guid>` 名单、v1 §6.8.1 的 `[Disabled]` 标记名单 → 已被
>   `[DisabledTp]` marker + 清理器改名取代（见 §5.6）；
> - v1 §6.10 `DllDisabler` 的"退出时分离进程改名 / 强杀残留自愈 / 缺席行"整套退出机械 →
>   已退役（见 §7.1），仅 marker 读写、扫描预 Bind 与 Tier 2 软停重放保留；
> - v1 §6.11–§6.14 不在此归档（它们本身就是 v2 机制，已整理进 §5/§6）。

## 1. 功能定位

套件内 mod 的统一管理面板：在主菜单（MainUI）与游戏内 ESC 菜单（OutSetting）各注入一个
入口按钮，打开 WebUI 面板，集中查看 / 开关本套件各 mod（各 mod 的 `General.Enabled`
配置 gate），替代逐个手改 cfg 文件。不改存档、不改游戏文件，删 dll 即还原。

主菜单 WebView 可达性已由任务 2 实机实证 GO；锚点选择器（`.menu` 尾部 appendChild /
`.menu-list` insertBefore `.btn-restart`）见 ANCHORS.md，**禁止按文案匹配**（全 i18n）。

## 2. 三层架构

| 层 | 代码 | 职责 |
|---|---|---|
| C# 宿主层 | `ModCenterPlugin` / `ModCenterTicker` / `ConfigBridge` / `Meta` / `Payload`（任务 6）/ `FocusTick`（任务 8）/ `DllDisabler`（任务 16） | BepInEx 插件宿主。Ticker 每帧跑 `ConfigBridge.Pump`（outbox 消息分发 + Save 防抖计时，主线程），每 0.3s（unscaled，暂停照走）跑 `FocusTick.Tick`（键盘门沿交接，§6.6），每 2s（unscaled）跑注入巡检；`ConfigBridge`：快照 `BuildSnapshot()` / 写 `TrySet`（联动 writeKeys 等值）/ `SetModEnabled`（任务 13 直写各 mod `General.Enabled`；任务 16 第三方分流——Tier 2 软停回调优先，§6.10）/ `MC_EVIDENCE` 验证辅助；`DllDisabler`：第三方重启级 dll 禁用（[DisabledTp] marker + 退出分离进程改名 + 残留自愈，§6.10）；`Meta` 九表 + `Payload` DTO/手写 JSON。统一经 `ModCenterPlugin.Log(msg)` 静态门输出 |
| 注入层 | `WebBridge` / `MenuEntryInjector`（任务 5；实机 UI 三修改事件驱动） | `WebBridge.GetWebView()`（TagFilter.cs:64-77 原样）+ `Exec`（注入与 C#→JS 推送共用主通道，Root 上下文）/ `Eval`（JS→C# 消息回读，任务 6 起承载 outbox drain；回调 DelegateSupport 包装，异常只 Warn 不中断轮询）。`MenuEntryInjector.Tick` 每 2s **自愈守卫拍**（不再每拍全量重发）：极小表达式校验 `__mcObserver`（Root 侧常驻 MutationObserver）+ `__McInstalled`（面板）+ `__mcSelfCheck()`（双锚点入口未被抹除），任一失守才重发完整引导（幂等）；入口即时性由 Root 侧 Observer（body 子树，iframe 插入当场）+ 目标页 iframe load 钩（页面加载完成当场 install）承担；outbox 轮询节奏不变（回读 JSON 入队 `ConfigBridge`，分发在其主线程 Pump 执行） |
| 前端面板层 | `panel.js`（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`；任务 7 已长成完整面板） | 面板 UI 与交互（原型 v4 移植，机制与实测见 §6.5）。全局名 `__mcOutbox` / `__mcDrain` / `__McInstalled` / `__mcOpen(hostDoc)` / `__mcClose` / `__mcEsc` / `__mcRender(snapshot)` / `__mcEvent(event)`（调试镜像 `__mcLastSnapshot` / `__mcLastEvent` / 推送序号 `__mcSnapSeq`）为既定契约，全程一致；任务 8 起 `MC_OPEN`/`MC_CLOSE` 兼作键盘门信号 + Root 侧孤儿看门狗（§6.6） |

panel.js 作为内嵌资源随 dll 分发，不落盘、不改游戏资源文件（同 CabinetEverywhere
tagfilter.page.js 模式）。

## 3. 工程与验证约定

- 构建：`dotnet build SurvivalLog.ModCenter -c Release` 判退出码（硬规则 8，禁管道）。
- 装机自证：Load 写 `<BepInEx 根>/ModCenter_loaded.txt`（硬规则 9，不依赖 LogOutput.log；
  本机绝对路径 `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt`，
  代码用 `Paths.BepInExRootPath` 拼接以兼容发布到其他机器）。
- 回归夹具：`tools/mc_fixture.js` **六模式**（`menu` 全量对账 / `toggle` 启停热还原 /
  `enterworld` 开面板进对局 / `ingame` 对局共存 / `outsetting` 游戏内入口 / `loadgame` 兼容
  保留），exit 0=全 PASS（SKIP 不判负）/ 1=断言 FAIL / 2=环境性不结论。改 ConfigBridge /
  Meta / panel.js 后先跑 `menu` + `toggle` 两模式回归（连 DevTools 9222，主菜单阶段即可，
  无需读档；六模式口径与全量实证见任务 17 报告）。
- 引用结构抄 CabinetEverywhere.csproj（BepInEx core + interop 游戏目录引用，`Private=false`
  不拷贝产物）；`ModShared/WelcomeTip.cs` 链接编译（套件防盗署名气泡，publish.ps1 要求）。

## 4. 反射桥 API 实证记录（任务 4，2026-10-24 实机；任务 6 ConfigBridge 逐字消费）

> 验证方式：`BridgeProbe`（`ModCenterPlugin.cs` 尾部，cfg `Debug.Probe` 门控），
> 实机一轮三拍：A 枚举+读+写 3 → B 验表/写回 → C 终验/落盘。
> 证据文件 `BepInEx/ModCenter_probe.txt`（验后已删，全文存
> `.superpowers/sdd/2026-10-24-modcenter/task-4-report.md`）。
> 静态 API 面来自 Mono.Cecil dump 本机 `BepInEx/core/BepInEx.Core.dll` +
> `BepInEx.Unity.IL2CPP.dll`（本机构 BepInEx 为 IL2CPP BE 构型，带 XML 文档但文档不全，
> **勿按 5.x 记忆猜 API**）。

### 4.1 确切可用写法（编译通过 + 实机跑通，全部强类型、零反射）

```csharp
// ① 枚举插件：本构型【无】静态 Chainloader.PluginInfos！实例字典路径如下
//    （类型是 IL2CPPChainloader，继承 BepInEx.Bootstrap.BaseChainloader<BasePlugin>）
var plugins = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins;
//    → System.Collections.Generic.Dictionary<string, BepInEx.PluginInfo>（public 实例属性）
//    实测枚举到 18 个插件；键 = GUID 字符串。

// ② 定位目标插件：PluginInfo.Metadata（public，BepInPlugin）与 .Instance（public，object）
var info = plugins.Values.FirstOrDefault(p => p.Metadata.GUID == "com.local.survivallog.robotspeed");

// ③ 取 ConfigFile：BasePlugin.Config 是【public getter 公共属性】，转型后直取，无需 AccessTools
if (!(info?.Instance is BepInEx.Unity.IL2CPP.BasePlugin bp)) return;
var cfgFile = bp.Config;   // BepInEx.Configuration.ConfigFile

// ④ 读 entry：泛型 TryGetEntry（T 必须与目标 mod Bind 时的类型字面一致，此处 float）
ConfigEntry<float> entry;                       // 声明在 if 外（out var 声明不能直接作内嵌语句）
if (!cfgFile.TryGetEntry("机器人提速", "移速倍率", out entry)) return;
float cur = entry.Value;                        // 实测读到 2

// ⑤ 写值热生效：typed Value 直接赋值
entry.Value = 3f;
//    → 链路（IL 反汇编实证）：ConfigEntry<T>.set_Value → ClampValue(AcceptableValue) →
//      新旧 Equals 相同则【直接 return 不发事件】→ ConfigEntryBase.OnSettingChanged →
//      ConfigFile.OnSettingChanged → ConfigFile.SettingChanged 事件
//    → RobotSpeed 的订阅回调（Config.SettingChanged += Rerun=true）实测收到：
//      内存表 Config_RobotAttribute.MoveSpeed 5s 内 4.8→7.2（=其缓存原值 2.4 × 3），
//      写回 2f 后表回 4.8（= 2.4 × 2）。热生效链闭环。

// ⑥ 枚举全部 entry（任务 6 通用页用）：ConfigFile 还公开
//    GetConfigEntries() → ConfigEntryBase[]；this[ConfigDefinition]/this[sec,key] → ConfigEntryBase；
//    ConfigDefinitions → ReadOnlyCollection<ConfigDefinition>；Count/ContainsKey（IDictionary 实现）。
//    注意 Entries 字典属性是 protected（Family），外部不可直读。

// ⑦ 落盘：SaveOnConfigSet 属性（实测 RobotSpeed cfg = True，每写即存）；
//    兜底显式 cfgFile.Save()（public）。
```

### 4.2 类型与线程上下文

- **全链无反射**：IL2CPP 互操作只发生在游戏侧（ConfigManager 表读）；插件侧 Chainloader/
  PluginInfo/ConfigFile/ConfigEntry 全是 BepInEx 托管对象，普通 LINQ/泛型可用。
- **ConfigEntry<T> 的 T 按目标 Bind 字面类型取**：float→`ConfigEntry<float>`；不确定类型时走
  非泛型路径 `cfgFile[new ConfigDefinition(sec,key)]` 得 `ConfigEntryBase`，用 `SettingType`
  判型后 `BoxedValue`（object get/set，写同样触发事件链）。
- **线程**：探针在 `ModCenterTicker.Update`（Unity 主线程）执行，与各 mod Ticker 同上下文；
  Value 读写本身是纯托管操作，实测主线程安全。任务 6 的 outbox 命令也应在 Ticker 主线程执行。
- **事件语义**：`entry.SettingChanged`（entry 自身）与 `cfgFile.SettingChanged`（文件级，参数
  `SettingChangedEventArgs.ChangedSetting` 可判哪个 entry）都公开可订阅。

### 4.3 踩坑与注意点（任务 6 必读）

1. **同值写入不发事件**：`set_Value` 对 `Equals(旧值)` 的写入直接短路返回——
   "重写当前值以触发 Rerun" 的思路行不通，必须值真的变化才触发 SettingChanged。
2. **写 cfg 即落盘**：SaveOnConfigSet=True（默认）时每次 Value 写立即重写该 mod 的 cfg 文件；
   探针写 3f 曾瞬时落盘，靠写回原值 + 显式 Save() 收尾（验后 robotspeed cfg 确认 = 2 无脏值）。
   面板滑条拖动会产生大量中间值写 → 每次都落盘，任务 6 要考虑**防抖**或先
   `SaveOnConfigSet=false` 改完再恢复 + Save 一次。
3. **gamekb 表快照 ≠ 原始基线**：gamekb 的 Config_RobotAttribute MoveSpeed=4.8 是**套件生效中**
   dump 的（RobotSpeed ×2 后）；真原始值 = 2.4（本轮由 4.8→7.2→4.8 反推并与其幂等缓存语义
   吻合）。任务 6 做"恢复默认值"时禁止拿 gamekb 数值当原始基线，应读 entry.DefaultValue。
4. **本构型 API 与常见记忆不符**：无 `Chainloader.Instance.Plugins` 静态、无
   `Chainloader.PluginInfos` 静态字典；正确入口 `IL2CPPChainloader.Instance.Plugins`（实例）。
   BepInEx 侧 API（Chainloader/BasePlugin.Config/ConfigFile/ConfigEntry）**不在 moddeps.json 登记口径内**
   ——那是游戏侧挂钩点/表字段/字节签名的登记表（硬规则 11 范围）；此处依赖的复核方式 =
   升级 BepInEx 后重验 §4.1 API 面（任务 4 的 Cecil dump 法，`tools/dump_bepinex_api.ps1`）。
5. **`TryGetEntry` 区分大小写与全角**：section/key 必须与目标 mod Bind 字符串逐字一致
   （本靶为中文 `机器人提速`/`移速倍率`，UTF-8 无障碍）。
6. **探针门控**：`Debug.Probe` 默认 false 常驻 `com.local.survivallog.modcenter.cfg` [Debug] 节，
   开一轮跑一次三拍后自动停（状态机终态），证据文件在 BepInEx 根目录 `ModCenter_probe.txt`。

### 4.4 实测时间线（2026-10-24 17:01–17:02）

启动（steam:// 协议）→ 17:01:50 RobotSpeed 首效（13s，ConfigManager 主菜单前就绪）→
17:02:15 探针 A（枚举 18 插件 / entry=2 / 写 3f）→ 17:02:20 B（entry=3、表 7.2×3 行、写回 2）→
17:02:23 C（entry=2、表回 4.8、Save）。全程未读档，主菜单阶段即验完。

## 5. 注入骨架机制与实证（任务 5，2026-10-24）

### 5.1 通道与落点

- C# 侧每 2s（Ticker，unscaled，实机 UI 三修后为**自愈守卫拍**）：`GetWebView()` 为 null
  （主菜单前/重建中）静默跳过本轮；就绪则只 `Exec` 一个极小守卫表达式
  （`__mcObserver && __McInstalled && (!__mcSelfCheck || __mcSelfCheck())`）——失守
  （WebView/Root 重建、或运行中入口被页面异常抹除）才重发完整引导（panel.js + EntryJs，
  双守卫幂等）→ `Eval(drain)` 回读面板 outbox（节奏不变，非入口延迟来源）。
- 入口即时性（问题1 修复）由 Root 侧常驻设施承担（EntryJs 一次性安装，`__mcObserver`
  防重装）：① MutationObserver 观察 **document.body 子树**（childList+subtree，含
  `#iframe-container` 增删；观察 body 而非容器节点——Root.html 的 Vue mount 会克隆替换
  #app 内容、容器节点身份会换，tagfilter.page.js:899 同款）→ 回调当场跑幂等 install；
  ② 目标页 iframe（id MainUI/OutSetting）**load 事件钩**（元素属性防重挂）——Observer
  回调时 contentDocument 常还是 about:blank（锚点未在），真正就位时机 = load（页面脚本
  已跑、Vue 已 mount）→ 当场 install；③ `__mcSelfCheck` 供守卫拍判定"锚点已在而入口
  被抹"的自愈重装（保留旧免维护语义，TagFilter.cs:46-50）。
- EntryJs 在 Root 扫 `#iframe-container iframe`（pathname 优先 / id 兜底，ANCHORS.md §三），
  命中 MainUI / OutSetting 后进目标 `contentDocument` 插入口按钮；点击闭包在 **Root realm**
  执行（`window.__mcOpen` 即 Root 契约函数），按钮元素归属 iframe 文档。

### 5.2 关键取证结论（改 UI 前必读）

1. **面板 DOM 必须挂在入口所在的 iframe 文档内，不挂 Root**：各页 webui-core.js 的
   clickThrough（selector `.unity-interactive, button, input, select, a, [data-interactive]`）
   把命中元素矩形经 Root 以协议 9 同步给 Unity，实机点击才落进 WebView；**Root 文档自身
   不跑 clickThrough**（Root.html:471 `syncInteractiveRects` 只收子页 postMessage），挂 Root
   的按钮收不到鼠标。故 `__mcOpen(hostDoc)` 把面板建进宿主 iframe；overlay 带
   `data-interactive`（打开期间全屏矩形进同步，点击不穿透游戏），入口/关闭钮均为
   `<button>` + `data-interactive` 双保险。
2. **button 元素注入两锚点的样式策略**：克隆官方类（MainUI=`.menu-item` 内层 `.bar`+
   `.menu-text`；OutSetting=`.main-btn`），入口选择器用**每页一份的 id 选择器样式块**
   （`#__mc_entry{...}`）只重置 button 的 UA 默认值（background/border/font-family 等），
   声明哪些属性就只压哪些（CSS 特异性按属性算，不误伤官方类）；accent 用蓝色系
   （bar 渐变 `#7fb8e8→#2f6ea8`、文字 `#a9cdf5`、OutSetting 左边条 `#4f9fe0`）区分官方金色。
   实测 computed 与官方项逐属性一致（font-size 29.6px/weight 700/letter-spacing 6px/
   padding 5px 15px 5px 5px/bar 8×34px 全同），仅颜色为 accent。
3. **OutSetting 锚点约束执行**：`insertBefore(btn, list.querySelector('.btn-restart, .btn-exit'))`
   —— restart 行 v-if 消失时锚点自动退化为 `.btn-exit`；永不插在两者之间（617 行相邻兄弟规则）。
4. **ESC 的现实**：OutSetting 页收不到 ESC（页面 1351 行注释：Unity 侧处理）。面板 ESC 关闭
   的 JS 接线在（Root + 宿主 doc 各挂 keydown → `__mcEsc`），DevTools/焦点在 WebView 时生效；
   **实机 OutSetting 场景下 ESC 可能先被 Unity 拿去关暂停菜单**——✕ 按钮与遮罩点击是保底
   关闭路径；任务 8 键盘门（消费 MC_CLOSE）落地时再统一处理。
5. **outbox 消息形状**：`{type:'MC_OPEN', host:'<宿主页 pathname>'}` / `{type:'MC_CLOSE'}`
   —— host 字段方便日志定位面板挂在哪页。任务 5 时 C# 只 Log（回读成功实证见 5.3）；
   任务 6 起入队 ConfigBridge 主线程分发（MC_SET/MC_TOGGLE_MOD 协议见 §6.2）。

### 5.3 实测记录（2026-10-24 17:28–17:33，主菜单阶段）

- 工具：`tools/_mc_verify_cdp.js`（连法照抄 `_probe_menu_cdp.js`；18 项断言）+
  实机 LogOutput 回读。截图 `task-5-shot-menu.png` / `task-5-shot-panel-open.png`
  （.superpowers/sdd/2026-10-24-modcenter/）。
- 结果：**18/18 PASS**——入口（button.menu-item、data-interactive、`MOD 管理`、.menu 尾部、
  单例、跨 tick 幂等、官方样式生效）→ 点击（同 iframe 文档 overlay+panel、标题、✕）→
  outbox（MC_OPEN/MC_CLOSE 均在 10s 内被 C# 轮询取走，`__mcDrain` 补丁留痕证明 C# 侧真在调）→
  关闭（✕ 移除 overlay、ESC keydown 关闭）→ 终态干净（1 入口、无 overlay）。
- 日志回读（BepInEx LogOutput.log）：`WebView 通道就绪，注入巡检启动` +
  `面板消息: [{"type":"MC_OPEN","host":".../UI/MainUI/MainUI.html"}]` 等 5 条——
  Eval 回调委托（DelegateSupport 包装）实测可用。
- **游戏内 OutSetting 断言未跑**（读档会动作者存档，自动化不冒险），代码路径与主菜单
  同一脚本分支（仅锚点/插入方式不同）——**任务 7 已补实机断言（§6.5.3），4/4 PASS**。

## 6. 配置桥与消息协议（任务 6，2026-10-24）

### 6.1 组成与纪律

- `Meta.cs`：9 mod 页面定义唯一蓝本 = spec §7 v4 九张表逐行转写（结构对照原型 v4 MODS 数组）。
  **默认值与范围不写死为准**：快照时从 `ConfigEntry.BoxedValue`（当前值）与
  `Description.AcceptableValues`（`AcceptableValueRange.MinValue/MaxValue` / `AcceptableValueList`，
  closed generic 走 `GetType().GetProperty` 反射读）合入；Meta 里的 Def/Min/Max 只在范围缺失或
  entry 尚不存在时兜底（数值照 spec §7 表抄）。步进 st 无反射来源，永远兜底。
- `Payload.cs`：DTO（Snapshot/ModInfo/GroupInfo/ItemInfo/OvrInfo）+ 手写 JSON（AOT 零反射）；
  入向消息解析 `JsonDocument`（EndlessGuard 同款实绩 API）。契约签名冻结：任务 7 UI 逐字消费
  （`{self:{ver},mods,tp}` / `Item={k,l,t,v,min,max,st,b,note,opts}` / ovr 类 mod 另挂 `ovr:[{id,ms}]`）。
- `ConfigBridge.cs`：枚举/快照/写/启停/消息分发/Save 防抖，全部在 Ticker 主线程（Pump）执行；
  Eval 回调线程只入队（`ConcurrentQueue`）。
- 两处对 spec 的实证修正（实证优先）：**UiFit.Mode 合法值是 Scroll/Zoom/Off**（ViewportFit.cs
  描述明示；spec §7 表 "Fit" 为笔误，写 Fit 会静默按 Scroll 处理）；RoleUnlock `Plan.ClearPool/
  VaseRecipe` 两 entry 任务 8 才新增，快照先按 spec 默认 true 兜底展示（写会被拒并回 MC_SET_FAIL）。

### 6.2 消息协议（JS→C# 走 outbox drain；C#→JS 走 Exec 直推 Root）

| 入（type） | 字段 | 行为 |
|---|---|---|
| MC_OPEN | host | 键盘门开（任务 8：`FocusTick.NotifyOpen`）→ 推全量快照：`window.__mcRender(snapshot)` |
| MC_SET | guid, section, key, value | `TrySet` → 成功回 `__mcEvent({type:'MC_SET_OK',k,badge})`（k=Meta 展示键，badge=Item.b）；失败回 `{type:'MC_SET_FAIL',k,err}` |
| MC_TOGGLE_MOD | guid, enabled | `SetModEnabled` → 推新快照（`__mcRender`） |
| MC_CLOSE | why? | 键盘门双还原（任务 8：`FocusTick.NotifyClose`）→ 立即落盘全部脏 cfg（防抖收口）。`why='host-doc-gone'` 为 panel.js 孤儿看门狗补发（§6.6） |
| MC_EVIDENCE | tag | 验证辅助（只读）：写 `BepInEx/ModCenter_evidence.txt` 一行**多表断言快照**（任务 13 扩）：RobotSpeed 移速倍率 entry + Config_RobotAttribute.MoveSpeed 全行 + 判别动作行 During（9075/9098/9099/9100/9130/9131/1611/1615/1915）+ 判别包行 Size/Burden（背包 1/8/11、货舱 1061/1063、柜子 110001/110009、冰箱 1004/115000/3002、载具 1023）+ 冰箱家具 ColdRate 统计（受管 16 行中已压低行数@最小值）+ Talent 受限行数与花瓶 325 等级 + 储物柜白名单集合计数。toggle 前后各拍一轮对照（表/trace/cfg 三源之一）；正常玩家不触发 |

- **逐条 try（任务 7 落地，原 §6.2 旧债收口）**：HandleOutbox 的批内循环每条消息独立
  try/catch——单条异常只弃该条（warn 日志），不弃同批后续（面板滑条连发+关闭常同批到达）。

- **guid 路由**：完整 GUID / En 短名 / GUID 尾段均可（大小写不敏感；`'RobotSpeed'`、`'robotspeed'`、
  `'com.local.survivallog.robotspeed'` 同效）。
- **key 路由**：主键 / 任一 writeKeys / 组合展示键（联动项 `"Expand.WidthMult + HeightMult"`）均可；
  **实写键一律以 Meta.WriteKeys 为准**（联动 = 宽高两键等值写；Item.v 展示宽度键值）。
- **只写 Meta 声明过的键**：未登记的 section/key 一律拒绝（spec §2.2 暴露原则的写侧执行）。
- 值转换按 entry.SettingType：float/double/int/long/short/byte 系/bool/string/enum（enum 名大小写不敏感）。

### 6.3 启停（任务 13 实装）与 Save 防抖

- `SetModEnabled(guid, enabled)`：**直写目标 mod 自己的 `General.Enabled`**（节/键按
  `MetaMod.EnabledSection/EnabledKey`，默认 General:Enabled，与 T9-T12 九 mod Bind 一致；
  全套件无键名差异，字段留作个别 mod 例外位）。写入经 §4.1 同款事件链触发该 mod 现役
  `SettingChanged→Rerun`：热关闭=其 Ticker 首行 gate 按原值缓存还原并停写（≤其轮询周期），
  重开=重新落笔（例外：PDS 无 Rerun 订阅——`General.Enabled` 系既有键复用，重启级语义
  =Injector 停注入，见其 DESIGN；Meta 该页为仅启停形态。CabinetSize 已随其 v4 补 Bind 该键
  （2026-10-25，T19），热启停全语义可用——T17 时代"toggle 拒绝 + 快照回推自愈"缺口闭合）。
  Save 走统一防抖（MarkDirty 该 mod cfg）。**旧的本插件 cfg `[Disabled]`
  名单机制已移除**（避免双轨；快照真值源=各 mod entry 本身，旧 cfg 里残留的 `[Disabled]` 节
  无人再读，行为惰性）。dll 不在链上 / entry 不存在（旧版 dll）→ 拒写并 Log。
- 快照 `enabled = 插件在链上 && entry 值`（entry 缺失按 Bind 默认 true 兜底）。
- 防抖（DESIGN §4.3-2 落地）：首写某 ConfigFile 前 `SaveOnConfigSet=false`（记住原值）→ 标脏；
  **3s 节流**（首写起算，连写不重置——长拖动按 3s 批落盘）到点 / MC_CLOSE 时 `Save()` 全部脏文件
  并**恢复各自原 SaveOnConfigSet**。进程被强杀最多丢 3s 内的未落盘写（面板改值路径可接受）。

### 6.4 实测记录（2026-10-24，主菜单阶段，无需读档）

`tools/_mc_verify6_cdp.js`（连法照抄 `_mc_verify_cdp.js`）：注入脚本调 `__mcOpen()` → 断言
`__mcLastSnapshot`：mods 9 项（名字/启停/分组对账 Meta）+ 柜子扩容三项联动键 + 动作加速 ovr 数组 +
更好的储物柜 enabled=true 仅启停 + RoleUnlock 兜底项；实弹 MC_SET（RobotSpeed 移速倍率→4）→
MC_EVIDENCE 轮询读表：MoveSpeed 4.8→9.6（=2.4×4，≤5s）→ 写回 2 → 表回 4.8；MC_TOGGLE_MOD
一轮（false→名单落盘 `com.local.survivallog.modcenter.cfg [Disabled]`→true→恢复）；MC_CLOSE 即时
落盘断言。证据：`.superpowers/sdd/2026-10-24-modcenter/task-6-report.md`（输出全文）。

### 6.5 面板 UI（任务 7，2026-10-24）

#### 6.5.1 移植要点（原型 v4 → panel.js 的实装差异）

- **可点元素改造（T5 结论的执行）**：宿主页 clickThrough 清单是
  `.unity-interactive, button, input, select, a, [data-interactive]`——原型的 `<span>` 迷你开关
  改为 `<button>`（自带全量样式重置），左列 mod 行是 `<div>` 故显式带 `data-interactive`；
  数值/滑条/下拉天然在清单内。全部选择器锚定 `#__mc_overlay` + `__mc` 前缀（零样式泄漏）。
- **观感取舍**：原型遮罩的 `backdrop-filter:blur(3px)` 未迁（纯色 rgba(5,6,10,.8) 加深替代）——
  WebView 内全屏实时模糊的 性能/兼容 保守取舍，观感差异仅在背景糊化。
- **状态语义**：`ST.sel` 记住当前选中 mod（跨开关面板保持——重开面板回到上次页，实测确认）；
  `toggleMod` **乐观翻转直接改写快照对象内的 mod 字段**——`__mcLastSnapshot` 与 `ST.snap` 同引用，
  故"快照里 enabled 已变"≠"C# 已回推"；判真推送用 `__mcSnapSeq`（`__mcRender` 每次自增，
  CDP 断言/未来 UI 逻辑都应以此为准）。
- **改值链**：input 事件即时 `MC_SET`，滑条 100ms 节流（首条立即发、窗口内合并为末值补发；
  实测 5 连发 → 恰好 2 条消息 [首值,末值]；`__mcClose` 清空节流句柄，关闭后无补发）。数值框
  input 阶段发送钳制值但不重写在打文本，change 时归位显示；下拉走 change。bool/下拉/滑条全部
  乐观更新本地 `it.v`（C# 不为 MC_SET 推快照，重绘不打断拖动）。
- **MC_SET 路由（Fix Round 1 定案）**：`section` 发展示键 `k` 按首个 `.` 解析的节前缀，
  **`key` 一律发完整 `k` 原串**（"节.键" / 联动"节.键A + 键B"）——C# `Match` 的 `it.K` 分支
  逐字命中（单键项整串即 `it.K`，联动项即组合展示键）。**禁止**再发剥前缀的 key：联动项
  "WidthMult + HeightMult" 在 Key/WKeys/K 三候选全不命中（初版实坑，背包/柜子 4 滑条写必败，
  fixture 已补 4 条端到端断言回归）。`其他.Overrides` 走 ovrCommit 固定 section/key（it.Key 分支）。
  **T17 补（B3）**：`Match` 的 K 分支已**前置放行**（完整展示键逐字命中且 mod 内唯一时先于节守卫
  执行）——点分节名键（首例=柜子 v3 `Tier.Small.Width`，routeOf 首点切分出 section="Tier"）
  此前必被"节.键"守卫拦截全拒；短键/ovr 分支仍走节守卫，行为不变。
- **ovr 序列化（面板侧承担，T6 评审备忘 B 兑现）**：增/删/拖 → 本地数组编辑 → 序列化
  `id:ms,id:ms`（**按 id 升序 canonical**）整串 `MC_SET 其他.Overrides`；打开面板从快照 `ovr`
  数组初始化；实测 8012 增(1000)→拖(250)→快照往返 `[{id:8012,ms:250}]`→删("")→`[]`，cfg 盘上
  `Overrides = 8012:250` → `""` 精确还原。
- **toast**：按徽标四档小字（now≈5秒内/reload=读档后/cfg=生效视该 mod——本任务套件全有徽标，
  cfg 档文案就绪待 T14）；`MC_SET_FAIL` 到达时以错误样式覆盖乐观 toast。
- **tp 空数组**：渲染"第三方 MOD"分类头 + "（暂无第三方 mod 接入）"占位行（T14 填充后自动长行）。

#### 6.5.2 实机现状备忘：UiFit.Mode/Enabled 写路径（已随 T17 Meta 柜子位改稿过时，留档）

> 2026-10-25 T17 注：Meta 柜子位已对齐 CabinetSize v3（无 UiFit 条目），本节写路径不再可达；
> "entry 缺失→兜底展示+写拒绝回 MC_SET_FAIL"的设计行为本身仍有效（见 §7 任务 17 条目）。

装机版 CabinetExpand.dll（作者现装，10-20 构建）尚未 Bind `UiFit.Mode`（cfg 无该键；仓库源
ViewportFit.cs:44 已 Bind，属作者未部署的 WIP）——快照按 Meta 兜底展示，写被桥拒绝回
`MC_SET_FAIL`（与 RoleUnlock Plan.* 待 T8 同类，**设计内行为**，实测 FAIL 回执+错误 toast+cfg
零改动）。作者部署新版 CabinetExpand 后该写自动转 OK 路径，无需改本 mod。

#### 6.5.3 实测记录（2026-10-24，主菜单 41/41 + 游戏内 4/4）

- 主菜单（`tools/_mc_fixture.js menu`）：面板布局（样式/头/关闭/四徽标图例/toast/左列 10 行
  =9 套件+自身锁定/tp 空占位/三分类头）→ 9 页逐项对账（分组标题/项数/控件类型/联动动态注记
  与快照自洽；仅启停页=占位文案）→ **PlanPick Cost min=100/max=300 反射合入断言（T5/6 遗留 D，
  快照+DOM 双侧）** → 自身页防呆文案 → 滑条（消息形态 guid/section/key/value 数值型 + OK 回执
  + 配对数值框联动 + toast + 节流形态）→ bool（消息值=布尔型/UI 翻转/回执）→ 下拉（FAIL 路径，
  §6.5.2）→ reload 徽标 toast → 左列启停（消息形态/真回推 snapSeq/行置灰/停用横幅/[Disabled]
  落盘/还原）→ ovr 往返 → ✕ 关闭（overlay 移除+MC_CLOSE）→ 全部被写 cfg 还原基线。
  截图 `task-7-shot-panel.png`。
- 游戏内（遗留 A）：`loadgame` 模式点"继续生存灾后 · 第90天"进对局 → SendKeys ESC
  （`tools/_mc_sendkey.ps1`，AppActivate+SendKeys）→ `outsetting` 模式断言：入口
  `BUTTON.main-btn` 在 `.menu-list` 内、`insertBefore` 位置正确（本档暂停菜单无 .btn-restart，
  锚点自动退化为 .btn-exit——**相邻兄弟规则的退化路径首次实机验证**，entry 紧邻其后）→
  点击开面板（挂 OutSetting 文档，10 行渲染）→ ✕ 关闭干净 → 共存扫描（#ppEntry 所在页未开，
  前后扫描一致、无残留 overlay）。跑两轮均 4/4 PASS。截图 `task-7-shot-outsetting.png`。
- **读档安全处置**：进对局后游戏自身在继续时做存档槽轮转（.bytes→.bak）并在约 13s 后自动
  落盘一次（暂停菜单打开前后）；主存档被写入约 90 秒世界漂移。验证后已用游戏自己的 .bak
  **复制还原**（md5 与作者 18:48 原态一致），HistorySave 本就同哈希未动——作者存档零净变化。
  教训：该游戏"继续"≠只读，后续自动化进对局前必须先备份 Saves 目录。
- 生命周期红线遵守：steam:// 启动；期间作者两次短暂上号（18:42-18:48 干扰首轮运行、按红线
  未杀未等自然退出后重跑）；验证后 taskkill + sleep 3 + tasklist 原始输出确认死透。

## 6.6 键盘门复用（任务 8，2026-10-24）

### 6.6.1 机制（照抄 PlanPick Injector.cs:100-140 十二轮定案）

- **开**（`_panelOpen` 上升沿）：`WebUILayer.SetKeyboardEnabled(true)`（裸键/英文进浏览器的
  唯一开关，PlanPick S1/S3 实证）+ 记录进入前 `Input.imeCompositionMode` 后设 `On`
  （中文 IME 组合由 Vuplex 直投浏览器，PlanPick 十一轮实证）。面板打开期间覆盖 ID 数字框、
  滑条配对数值框等可实机键入，且游戏热键不触发（键全量进 WebView）。
- **关**（下降沿）：`SetKeyboardEnabled(false)` + `imeCompositionMode` 精确还原进入前值。
- **门控判据与 PlanPick 的差异**：PlanPick 是官方页 `IsPageActive` 三候选（uiKey 列表）；
  本面板不是官方页——门控 = 面板开合状态，由 `MC_OPEN`/`MC_CLOSE` 消息驱动
  （`ConfigBridge.HandleOutbox` 主线程 Pump 调 `FocusTick.NotifyOpen/NotifyClose` 置位，
  与 `FocusTick.Tick` 同在 Ticker 主线程，无竞态）。`Tick` 由 Ticker 每 0.3s（unscaled，
  暂停菜单打开也要照走——OutSetting 场景 timescale 不可依赖）驱动，沿触发、异常只 Warn。
- **对 PlanPick 的两处有意加固**（非照抄部分，代码注释标注）：
  1. `_imeSaved` 会话标记：层短暂丢失后重交接时不把当前值（已还原的原值）误记为"进入前值"，
     保证"还原精确回到进入前状态"闭环（PlanPick 未区分，重交接场景会把 On 记成原值）。
  2. 层丢失收尾（`layer == null` 且已交出）：PlanPick 只复位标志；本面板状态由 JS 消息驱动，
     层没了 ⇒ WebView/JS 也没了 ⇒ 面板必已消亡且永远等不到 MC_CLOSE——就地还原 IME 静态
     （Unity 侧，层死不影响）并复位开合状态，防键盘门漏开（键盘门标志随层销毁，新层默认门关）。
- **panel.js 孤儿看门狗**（Root 侧 1s interval，宿主 iframe 消亡不影响 Root 定时器）：
  宿主死亡判据 = `宿主 doc.defaultView.frameElement` 为 null 或失连（**T17 修复**：本引擎
  Vuplex/CEF 里被移除 iframe 的孤儿文档内元素 `isConnected` 恒 true——旧判据"面板根
  `!isConnected`"对宿主 iframe 被移除永不触发，开面板进对局键盘门漏开实锤；frameElement 对
  "iframe 被移除"与"文档被导航"两死法均实测归 null，且其自身属 Root 文档、isConnected 语义
  正常）。面板开着时宿主死亡（如暂停菜单被 Unity 侧 ESC 收起、开面板直接点继续进对局）⇒ 补发
  `MC_CLOSE{why:'host-doc-gone'}`（键盘门还原 + 脏 cfg 落盘收口）。✕/点遮罩仍是保底关闭
  路径（T5 结论：OutSetting 的 ESC 被 Unity 截走，ESC 仅增强）。前瞻脚注：若未来出现宿主 =
  Root 顶层文档的挂载形态，frameElement 恒 null 会在开面板 1s 后误关——当前架构入口只喂
  iframe 文档，不可达；Vuplex 升级后建议复跑 `enterworld` 模式验证两死法仍成立。
- **自证（硬规则 9）**：每次沿写一行 `<BepInEx 根>/ModCenter_kb.txt`（Load 时清空）：
  `OPEN kb=on ime Auto->On`（kb 读回自 `get_IsKeyboardEnabled`）/ `CLOSE kb=off ime->Auto`
  （还原值）/ `ORPHAN(layer-lost) ime->X kb-dies-with-layer`。CDP 合成键不受 Vuplex 门控，
  不能作门开证据——自证文件是装机断言的唯一权威。

### 6.6.2 实测记录（2026-10-24，主菜单阶段，不碰存档）

`tools/_mc_kb_cdp.js`（连法照抄 `_mc_fixture.js`）：入口点击开面板 → 轮询 ModCenter_kb.txt
断言 OPEN 行（kb=on、ime 进入前值→On）→ ✕ 关闭 → 断言 CLOSE 行（kb=off、还原值逐字等于
本轮记录的进入前值）→ 往返三轮共 6 行、无 ORPHAN 行；outbox spy 断言 ≥3 MC_OPEN +
≥3 MC_CLOSE 被 C# 取走；第 1 轮附带实机打字探针（面板开 → 动作加速页 → CDP focus
`#__mc_ovr_new` → `tools/_mc_kb_type.ps1` AppActivate+SendKeys '8012' → 回读 input.value
——实机物理键穿 Vuplex 门进浏览器，探针失败只 WARN 不判负）。证据全文存
`.superpowers/sdd/2026-10-24-modcenter/task-8-report.md`。

## 6.7 启停热还原断言（任务 13，2026-10-24，主菜单阶段）

工具 `tools/_mc_fixture.js toggle`（MC_TOGGLE_MOD 真实面板开关驱动 + `MC_EVIDENCE`
多表快照 + trace/cfg 双源；面板活性自愈——宿主页丢失自动重开面板，被接管则中止）。
**实测 42/42 PASS（2 项 SKIP：CabinetExpand 系——并行任务已把 Meta 的柜子扩容位换成
CabinetSize（c899f20 系列），面板无该页，热还原断言待套件组成定稿后补）**。断言面
（T9-T12 累积欠账收口）：

1. **三件套热还原 ×2 轮**（T9）：RS 面板关 → ≤5s MoveSpeed 4.8→2.4（×3 行）、
   9098/9099/9100/9131→300/300/60/300、货舱 1061/1063→4x2/4x3（9130 保持 60=QA 接管）；
   重开逐值回 4.8/150/8x2；BP（bag1 8x7/9600、bag11 9x11）与 QA（9075→3600 等）同过。
2. **BP ExcludeIds 继承性语义**（T9 评审 A）：ExcludeIds=8 预置（cfg 预设+重启，面板无
   此键）——bag8 全程停官方 9x8（"排除即停管"）；"先排除已扩容行再热关留残值"场景经
   面板不可达（ExcludeIds 不在 Meta，MC_SET 被拒实证——桥只写登记键）。
3. **9130 双 mod 同关**（T9 评审 B）：**QA/RS 各加共管让位守卫**（还原时"当前值>本缓存
   ⇒不回写"；QA 自愈分支改"只升不降"——此前 QA 会把 RS 还原的 300 压回其污染缓存 150）。
   实测：QA 单关（RS 开）9130=450（QA 按污染缓存还原，RS 让位守卫保持自家目标）；双关
   **B 序（QA 先关、RS 后关）终值=900 官方确定**（RS 干净缓存最后还原）；**A 序（RS 先关
   +6.5s 间隔保证 QA 轮询命中）终值=450**——QA 仍启用期间按己方目标重写 60、其还原再落
   污染缓存 450，**关序决定终值**（彻底收敛需跨 mod 原值共享注册表，超出最小修复范围，
   如实记录）；两序恢复全 60。
4. **FridgeChill**（T10）：热关 → cold 16 行全回 0.2 + 还原日志行；重开回 16/16@0.1；
   **KeepMult 2→1 热迁移**（FC 仍启用）→ 早退区也回写 cold 全 0.2、cfg=1 落盘；回 2 复写。
   CabinetExpand RestoreAllSizes 运行中路径随套件组成变化 SKIP（见上）。
5. **CabinetEverywhere**（T11）：热关 → 白名单集合 710→1（evidence 活计数）+ CE_gate_trace
   `RemoveAll` 移除前后计数行；重开重注入 710。RoleUnlock General 热关 → Talent 123 /
   花瓶 0/0 回官方 + RU_gate_trace 还原行；两开关（ClearPool/VaseRecipe）面板热切换
   123/0-0 ⇄ 77/1-1 双向 + cfg 落盘。
6. **PlanPick/PDS**：cfg 直写（false→true 落盘核对）+ 快照回推 + 行置灰/停用横幅
   （注入 UI 级留 T17 存档轮）。

过程实录（第 1 轮 23:12-23:19）：跑到一半被作者接管会话（面板 DOM 消亡后 fixture 盲等
超时，FC/CEW/RU/PP 段全"未达桥"；23:18:50 作者点继续 → WebView 关闭 → fixture 中止）；
第 2 轮修 fixture（活性自愈 + FC 滑块实参 bug + QA 守卫收窄）后全绿。证据全文：
`.superpowers/sdd/2026-10-24-modcenter/task-13-report.md` 与 task-13-ev-*.txt。

## 6.8 Tier 0 第三方通用页（任务 14，2026-10-24）

接入协议单源：`docs/modcenter-integration-guide.md` §二（mcSpec 1）。本节只记实现口径与实测。

### 6.8.1 机制（ConfigBridge `BuildTp`/`FindTp`/`TrySetTp`/`SetTpEnabled`）

- **枚举与豁免**：快照时枚举 `IL2CPPChainloader.Instance.Plugins`（§4.1① 同源 API），按
  Name 排序稳定输出。豁免 = Meta 登记（套件 9）+ ModCenter 自身 + **第一方 GUID 前缀
  `com.local.survivallog.`**（覆盖 Meta 未登记的本仓 mod：CabinetBurden/EndlessGuard/
  StackLimit999 等——简报名单之外的本机实装情形，实证后扩展）+ 探针名兜底
  （ActionProbe/ViewportProbe/ConfigDumpProbe，名字含即豁免，防 GUID 换族）。
- **通用页构建**：`BasePlugin.Config` → `cfg.Keys`（现役 API；`ConfigDefinitions` 已过时）
  逐 `ConfigDefinition` 取 entry → section 分组沿用对方 cfg 节名（首现顺序）→ Item：
  `k=节.键`、`l=键名`（Tier 0 英文键名即标签）、徽标一律 `cfg`、
  `ConfigDescription.Description`→note、AcceptableValueRange/List 反射合入（§6.1 同款
  `ApplyRange`，已从 BuildItem 抽出共用）、enum→下拉/无范围 string→**文本框（新 t=`str`）**/
  数值带范围→滑条（步进兜底：整型 1/浮点 0.1）。无 entry（无 ConfigFile 或空 cfg）→
  仅启停形态（空 groups + desc 说明，同灾前加速页）。
- **写路由（通用）**：`TrySet` 解析不到 Meta 时落 `FindTp`（完整 GUID/插件名/GUID 尾段）→
  `TrySetTp`：按 section/key 直取对方 entry——**只写实际存在的键**（指南 §二核心原则，
  无登记表）；面板 key 发整展示键 k 原串，首查未中时剥一次节前缀兜底。写入触发对方
  ClampValue+SettingChanged 链（订阅即热响应），Save 走统一防抖。回执 k 用展示键原串
  （`DisplayK`：已带节前缀不重复拼——初版重复拼出 `Features.Features.X` 实坑，fixture
  回执断言拦截）、badge 一律 `cfg`。
- **启停（标记名单，重启级）**：`SetModEnabled` 解析不到 Meta 时走 `SetTpEnabled`——回用
  T6 时代机制：本插件 cfg `[Disabled]` 节懒 Bind `<guid>` bool 标记（true=已标记停用），
  快照 enabled=标记取反。**本会话对方仍在运行**（面板横幅/toast 均重启级文案，T7 已备）。
  任务 16 起换"退出时 dll 改名 `.disabled`"真禁用，名单届时退役（横幅文案语义不变）。
- **panel.js**：T7 已备第三方分组渲染（空数组安全→自动长行）与 cfg 档徽标/toast 分支；
  本任务仅新增 `str` 文本控件（ctlHtml 渲染 + change 提交整串，不逐键发送）。
  套件侧不受影响：现役套件 string 项均带 Opts（sel），无 Opts 的 string 才降级 str。

### 6.8.2 实测记录（2026-10-24，主菜单，59/59 PASS）

靶 = 作者实装第三方 4 件（`com.ivmakk.survivallog.*`：Better Trade/Compact Inventory/
Project Cook/Stats UI，全 bool entry）。`tools/_mc_fixture.js` menu 模式新增 18 项 tp 断言组
（结构以**对方 cfg 文件盘上内容为基准**对账）：

- 快照 4 件全数入列（分组=cfg 节集合、逐项 k/l/b/t/v 对盘一致；注意 cfg.Keys=内存 Bind
  序 ≠ cfg 文件 Save 序（字典/排序差异），断言按集合比对）；豁免断言：tp 内无
  `com.local.survivallog.*`、无 probe 名（ViewportProbe/ConfigDumpProbe 在装未出现）；
- 面板：第三方分类头 + 4 行（含启停开关）；BetterTrade 页渲染（页题/分组/条目/bool 控件/
  note 数与快照自洽）；
- 交互：`General.Verbose` false→true（MC_SET 形态 guid/section/key=整串/value=布尔 →
  OK 回执 k=`General.Verbose` badge=`cfg` → 对方 cfg 防抖落盘 true → 写回 false 精确还原）；
- 启停：标记停用 → 快照回推 enabled=false + 行置灰 + 重启级横幅（"本次会话仍在运行，
  下次启动后不再加载"）→ modcenter cfg `[Disabled] com.ivmakk...bettertrade = true` 落盘
  → 恢复 → false；
- str 控件：合成快照（真实渲染路径）→ 文本框渲染 + change 提交 MC_SET（string 值）→
  MC_OPEN 刷新后合成行清除（C# string 写路径由 ovr Overrides 实弹久证）；
- 遗留 41 项全回归 PASS（侧列行数断言改自适应 10+N tp；tp 空占位断言改与 tp 数一致性）。
- 副作用披露：modcenter cfg `[Disabled]` 节经本轮 Save 后，T13 时代的 9 条套件残留项被
  BepInEx 自然清出（只写现役 Bind 项）——现该节仅 4 条 tp 标记（全 false 惰性），
  作者原计划"顺手删"的残留自动了结。
- 过程杂记：第 2 轮启动空跑（上局强杀后 webview 孤儿占 9222、新进程未起，fixture 误连
  孤儿后随其中止）——以 `ModCenter_loaded.txt` 时间戳为新一轮就绪判据重跑即过；
  第 1 轮 1 FAIL 即上文 `DisplayK` 重复拼前缀实坑，修复后全绿。

## 6.9 Tier 1 声明式清单（任务 15，2026-10-24）

接入协议单源：`docs/modcenter-integration-guide.md` §三（字段表即行为规范）。

### 6.9.1 机制（`ThirdParty.cs` 清单发现与合并层，叠在 §6.8 Tier 0 之上）

- **发现**：`BuildTpMod` 先按 Tier 0 构建，再交 `ThirdParty.Apply`——插件实例
  `GetType().Assembly.GetManifestResourceNames()` 匹配 `modcenter.json`（精确名或
  `*.modcenter.json` 后缀均认：指南 csproj 片段不带 LogicalName 时资源实名是
  `根命名空间.modcenter.json`）；同一程序集多份清单**取第一个**（数组序），其余忽略记日志。
  清单逐插件解析一次入缓存（会话不变）。
- **校验容错（指南"绝不报错"承诺）**：JSON 解析失败 / `mcSpec` 缺失或不认识（≠1）/
  `groups` 整段缺失 → **整单忽略降级 Tier 0**；清单键在实际 cfg 不存在 → 该项隐藏
  （容忍版本差异，计入日志）；组缺 title/项缺 key 跳过该组/该项；display/summary 缺项
  逐字段回落 Tier 0 值。`id` 与 GUID 不一致只记日志不拒（清单内嵌于该 dll 即归属该插件）。
- **合并渲染**：display/summary 覆盖显示名/简介（`en` 恒为插件名，短名路由不变）；
  groups **全量重组**——Tier 0"cfg 节名分组/键名即标签"被清单替代，**清单没列的键
  不出现**；组/项按 `order` 权重排序（小者在前，缺省 0，LINQ 稳定排序保清单序）；
  项的 label/note（优先于 entry Description）/min-max-step 按清单覆盖 Tier 0 推断值
  （范围覆盖**优先于** AcceptableValueRange 反射，缺省回落反射——指南字段表）；
  类型/当前值/选项仍来自 entry 反射；步进与"无范围 string→str"兜底同 Tier 0。
  整组项全隐藏 → 空组不渲染。
- **徽标归一**：指南 §六词汇 `now/reload/cold/缺省 cfg` → 面板契约
  `now/reload/reboot/cfg`（Payload.ItemInfo.B 口径，cold→reboot）；未知值诚实落 `cfg`。
  `MC_SET_OK` 回执 badge 经 `ThirdParty.BadgeOf` 取清单徽标（Tier 0/未列项回退 cfg）；
  查表 `Badges`（展示键→归一徽标）在 `Apply` 渲染每项时填充——首测漏填致回执恒 cfg，已修。
- **softDisable**：清单声明暂存（`SoftDisableOf`），任务 16 启停两层语义消费；快照
  `ModInfo` 增量字段 `tier`（Tier 1 输出 1，Tier 0 省略）供面板/fixture 判别。
- **写路由零改动**：Tier 1 项展示键仍 `节.键`，面板发整串 k，`TrySetTp` 剥前缀兜底
  既有链直用；注意 manifest min/max 只覆盖**显示**范围，实际写入仍走对方 entry 的
  ClampValue（如清单 0.5-2.0 覆盖显示、entry AcceptableValueRange 0.8-1.2 仍钳写入——
  BepInEx 语义如实保留，面板滑条在两者交集外拖动会被对方 cfg 钳回）。
- **panel.js 零改动**：分组/标签/徽标渲染 T7 已备，清单页全部由快照数据驱动。

### 6.9.2 实测记录（2026-10-24/25 主菜单，靶 = 2 个一次性假第三方 dll）

靶工程 `.superpowers/sdd/2026-10-24-modcenter/t15-target/`（ViewportProbe 插件壳副本，
**验后删除不发布**）：`Tier1Demo.dll`（GUID tier1.demo，内嵌 mcSpec 1 清单：中文
display/summary/softDisable=true/两组带 order + 中文 label/badge(now/reload/cold)/
min-max-step 覆盖 + 幽灵键 UI:Ghost 故意不 Bind + 未列键 UI:Secret 故意 Bind）与
`Tier0FallbackDemo.dll`（GUID tier0.fallback，两份 mcSpec 99 清单：精确名 LogicalName +
后缀名重复资源）。`tools/_mc_fixture.js` menu 模式新增 7 项 t15 断言（无靶机器 SKIP，
不计失败；T14 tpStruct 组自动跳过 tier=1 页）。关键事实见 task-15-report：
tier=1 标记/display"Tier1 示例"/summary 覆盖；分组 order（显示1<性能2，清单序反排）；
幽灵键与未列键双隐藏；Scale 清单范围 0.5-2.0/step 0.05 覆盖反射 0.8-1.2 + badge
cold→reboot 文案；RefreshMs 无 AcceptableValueRange 纯清单范围 100-5000/step 100/badge
now；滑条写路由（MC_SET 整展示键 → 回执 badge=reboot → 对方 cfg 落盘 1.1 → 还原 1）；
mcSpec 99 双清单 → Tier 0 兜底（tier 省略/Secret 照常显示/反射范围 0.8-1.2）。

## 6.10 Tier 2 深度对接 + 重启级 dll 禁用（任务 16，2026-10-24/25）

接入协议单源：`docs/modcenter-integration-guide.md` §四（Contracts）/§五（启停语义）。

### 6.10.1 机制

- **Contracts 编入**：指南 §4.1 接口源码原样编入 `Contracts/ModCenterIntegration.cs`
  （命名空间 `ModCenter.Integration`，接口 `IModCenterIntegration` + `McItemState`）。
  **发现机制 = 反射按类型名 duck-typing，禁止接口强转**——第三方 mod 编入的是自己那份
  接口副本，跨程序集接口类型互不可 cast。`ThirdParty.T2Of`：`asm.GetType("ModCenterIntegration")`
  （未中再按简单名扫 `GetTypes()` 兜底）→ 要求 `public static bool OnEnabledChanged(bool)`；
  静态属性 `McSpec` 在场且 ≠1 → 不识别降级 Tier 1/0（接口冻结）；`QueryStates` 可选。
  逐插件解析一次入缓存；任何缺失/异常都只是"没有 Tier 2"，绝不报错（指南承诺）。
- **软停回调**：`ThirdParty.FireEnabledChanged(info, enabled)` 反射调
  `OnEnabledChanged.Invoke(null, new object[]{enabled})`。返回 false/异常 → 降级重启级
  并标记 Degraded（本会话后续快照 Soft 同步回落）。`SetTpEnabled` 停用分流（指南 §五）：
  Tier 2 回调返回 true（软停，行为立即停止）或清单 `softDisable=true`（对方自实现 gate）
  → 面板软停语义文案；否则重启级文案。**三层统一写 [DisabledTp] marker**——Tier 2 行的
  "下次启动不再加载"由 dll 改名兜底（ModCenter 不依赖对方自实现 Load 首行 gate）。
- **状态回报**：`ThirdParty.QueryStatesOf(info)` 反射调 `QueryStates()` 读 `Key/State/Reason`
  属性（duck-typing，上限 64 项）；`ConfigBridge.ApplyStates` 合入快照：Key "Section:Key" →
  项 K "Section.Key" 置灰（`ItemInfo.Fail`=Reason，panel.js 锁控件+红字）；空 Key=整 mod 级
  （Desc 追加 ⚠）。State 只认 ok，非 ok 一律 failed。
- **DllDisabler**（重启级禁用执行方，指南 §五"工程细节"）：
  - **marker**：ModCenter 自身 cfg `[DisabledTp]` 节，key = **plugins 目录下 dll 相对名**
    （dll 禁用后插件不在链上，GUID 无从解析）。T6/T14 的 `[Disabled]<guid>` 名单退役
    （无读取方；游戏侧 cfg 的 9 条孤儿裸行随本任务清理，4 条 tp GUID 惰性条目保留）。
  - **退出执行**：运行中 dll 被锁（OnApplicationQuit 时本进程仍持有），改名交**分离进程**
    `powershell -NoProfile -WindowStyle Hidden -EncodedCommand <base64(UTF-16LE)>`
    （脚本 ASCII-only；EncodedCommand 彻底规避引号/反斜杠转义）循环 Rename-Item
    （`<名>.dll`→`<名>.dll.disabled`，**只改名绝不删除**；`-NewName` 喂 `Split-Path -Leaf`
    ——BepInEx 递归加载 plugins 子目录，marker 记的是子目录相对名如 `Pack\Mod.dll`，
    带路径喂 `-NewName` 必败→禁用静默失败，Fix Round 1 P2）直到成功/目标消失，超时 10 分钟。
    退出钩子 = **双钩子幂等**（`ModCenterTicker.OnApplicationQuit` 消息 +
    `Application.add_quitting` 事件，`RunQuitHooks._quitDone` 守卫只跑一次）；
    顺序 = 先 `ConfigBridge.FlushDirty()`（marker 必须先落盘）再拉分离进程。
    名字边界守卫 `IsSafeRelName`（剔绝对路径/盘符/`..` 越界；`'` 等合法字符单引号翻倍转义放行——
    EncodedCommand 无注入面，Fix Round 1 P3-1）。
  - **启用** = 反向改名：dll 未加载不锁，直接 `File.Move`（marker 存相对名→直接拼全路径，
    子目录天然支持）；失败才分离进程兜底（2 分钟，同样 Leaf）。同时撤销残留补拉进程
    （`Armed` 句柄表 Kill——玩家当场反悔时改名意图已失效）。
  - **强杀残留自愈**：`ScanOnLoad`（Load 时）解析自身 cfg 的 [DisabledTp] true 项 × plugins
    目录对照——marker=true 且 `.disabled` 在场 → 预 Bind（快照行/再启用路由就绪）；
    marker=true 但 dll 还在（退出钩子没跑）→ **残留**：补拉一次分离进程（本会话结束后
    生效）+ 面板下次打开 `MC_TOAST` 提示一轮（panel.js `toastRaw` 原文展示）+ Tier 2 者
    `ReapplySoft` 软停回调重放（Ticker 首帧——此时 Chainloader 全插件就绪）。
  - **缺席行**：`AbsentDisabled()` **递归**扫描 plugins `*.dll.disabled`（BepInEx 递归加载
    子目录——非递归扫不到子目录禁用文件，缺席行永不出现，Fix Round 1 P2；排除第一方
    `SurvivalLog.*`）→ 快照补 enabled=false 的"仅启停"行（guid=dll 相对名**剥 `.disabled` 后缀**，
    `Absent` 标记），供玩家重新启用（`TrySetAbsent` 路由按相对名拼全路径反向改名）。
    注意 guid 可含 `\`/`'`——CDP/fixture 查询一律属性值比对，勿拼进 CSS 选择器字符串。
- **panel.js 两层语义**：第三方停用横幅/toast 三档——缺席行（"dll 已禁用：当前未加载"）/
  软停（"行为已立即停止"）/ 重启级（"本次会话仍在运行"）；`MC_TOAST` 事件 → 原文 toast。

### 6.10.2 实测记录（2026-10-25，三轮，靶 = Tier2Demo + Tier0FallbackDemo 一次性 dll）

见 `.superpowers/sdd/2026-10-24-modcenter/task-16-report.md`（菜单断言组 + 正常退出改名 +
重启不加载 + 再启用恢复 + taskkill 强杀残留 toast/自愈全链路）。

## v1 §7 迭代维护清单（历史条目）

> v1 时代的逐任务改动记录，保留供追溯；v2 任务与 commit 区间见 §10。

- 2026-10-24 实机 UI 三修（bugfix round 2，作者试用三问题）：①ESC 入口出现慢（≤2s 轮询
  等待）→ `MenuEntryInjector.cs` 改事件驱动常驻：EntryJs 一次性安装 MutationObserver
  （body 子树）+ 目标 iframe load 钩，回调当场跑幂等 install（锚点逻辑照旧，ANCHORS.md
  单源不变）；C# 2s 慢拍降级为守卫自愈（`__mcObserver`/`__McInstalled`/`__mcSelfCheck`
  三分量极小表达式，失守才重发完整引导）；DrainOutbox 节奏不动。②滑条轨道不可见（游戏
  WebView 不画 accent-color 原生默认轨道）→ panel.js 显式 `-webkit-appearance:none` +
  自绘 runnable-track（4px #2a2f3d）/ slider-thumb（13px 圆 #e8a33d，margin-top -4.5px
  居中）+ -moz 双保险。③动作加速"新增覆盖"ID 栏 number 框原生加减钮 → 改
  `type=text` + `inputmode=numeric`（去 min；parseInt 校验与防重复逻辑不变；补
  `.__mc-ovr-add input` flex 覆盖防通用 text 规则拉伸）。锚点/挂钩点零变更（moddeps.json
  不动）。实测见 `.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-report.md`。
- 2026-10-25 背包数值化对账（作者定稿极简三键版）：BackpackExpand 倍率制改数值制，Bind 面 =
  `General.Enabled + Expand.Width/Height/Burden + Global.MaxWidth/MaxHeight` 共 6 键；Meta 背包位换
  两组 5 键（扩容数值 3：Width/Height Badge now、Burden Badge reload；防呆上限 2）。Height 语义=
  基础高度，天赋加行三连（31931-31933）每级 +1 由 mod 按链级数表计算（天赋照常生效）。旧倍率条目
  （`Expand.WidthMult/HeightMult` 联动 + `Expand.BurdenMult`）与试点期的 `[Bag.*]` 逐包节、
  Preset/ExtraIds/ExcludeIds/Debug.* 键全部退役（玩家 cfg 由 mod 首跑迁移删除）。**全面板自此无
  live 联动项**：联动渲染仍由合成快照覆盖、TrySet 联动写机器保留但无条目命中；夹具 `LINKED`
  数组清空（空真）、`backpackDynNote` 断言改 `backpackZeroNote`（首条 note 含"0=恢复官方原值"）、
  默认页断言改两组 3+2 / 5 滑条 5 徽标。
- 2026-10-25 任务 20（作者令：①工具柜并回"专属"一块不再单列、②展示顺序 专属→通用→个性化、
  ③补个性化功能"类似动作加速这种设计"、④全部数值输入框去原生加减钮只允许数字）：
  - Meta 柜子页两组+清单块：`专属修改：常用 13 包`（26 键，工具柜 10007 并入）→`通用：档位与上限`
    （10 键原样）→`个性化修改`DOM 块（BagOvrHost，非 Meta group）。
  - **bagOvr 机制（OvrHost 同构）**：`MetaMod.BagOvrHost` 宿主项（柜子容量 Global.BagOverrides，
    "id:WxH,id:WxH" 整串）→ 快照 `ReadBagOvr` 解析 + `ModInfo.BagOvr:[{id,w,h}]` 序列化 →
    panel.js `bagOvrHtml/serializeBov/bovCommit`（新增柜子 ID/逐行宽高纯文本框/删除，新行默认
    16x12）→ 提交走常规 MC_SET（**FindItem 补搜 BagOvrHost**——不补则整串写必被"无登记项"拒）。
    值语义在对方 mod（Resolve 最高优先级+受管集注入绕黑名单，见其 DESIGN v4.1）。
  - **numbox 全量化**：ctlHtml 滑条伴生框/ovr 行/bov 行/无范围数值框全部 `type=text
    class=__mc-numbox inputmode=numeric data-num=int|float`——游戏 WebView 的 number 框带原生
    加减钮（遮挡/误触，实机 UI 三修·问题3 先例推广到全面板）；wireCfg 输入清洗（int 剥非数字、
    float 收敛一个小数点，置于 data-act 判定前——清单新增 ID 框同受辖）；CSS numbox 规则必须置于
    input[type=text] 通用规则之后（同为 text，层叠序否则 flex:1 左对齐吃掉定宽居中）。
  - 夹具 v5：柜子页 2+1 组 36 项、`numSpinners===0` 全页断言、bagOvr 回路（新增 3002→改宽→删→
    还原基线）、bov/tier/bag 三回路组索引随新序换位（专属=0/通用=1）。
- 2026-10-25 任务 19（作者令：柜子容量页三组重构——12 包+工具柜入面板、工具柜单独一块、
  档位归通用组；**ConfigBridge 零代码改动**）：Meta 柜子页由单组 10 键扩为三组——
  「通用：档位与上限」（原 10 键原样）+「专属修改：常用 12 包」（24 键，`BagW/BagH`
  工厂）+「专属修改：工具柜」（10007 两键，独立块不随小档）。前置实证（决定架构）：
  部署版 BepInEx（6.0.0-dev.2025.12.31 = 上游 577eba4）`ConfigFile.Save()` **不重读磁盘**，
  orphan（未绑定）节按**加载时**内存快照回写——若走"[Bag.*] 文件直写"方案，面板写的值会被
  下一次 cfg 落盘打回，且 T17 陈旧文件闸会被文件 mtime 刷新绕过。故由对方 mod v4 把 13 包
  `[Bag.<id>]` 升级为 **BepInEx 绑定键**（BagBindings），本表 `Section="Bag.<id>"` 直达既有
  反射桥（快照反射/TrySet/防抖/T17 闸全复用，B3 点分节名路由 T17 已前置放行）。注意：
  10007=包名"工具柜"（家具 406）；游戏内另一"工具柜"家具 70006 走 110001（与中型置物架
  共包，面板 110001 项 note 已注明）。夹具 `cabinetsize no-gate toggle` 断言随之改语义
  （toggle 由"拒绝自愈"改为"接受+热还原"）。
- 2026-10-25 任务 18（文档终版 + 发版接线，零代码改动）：状态头改"全 18 任务实机定稿"口径
  （三档接入/启停两层/双列表入口/键盘门/六模式夹具 + task-17-report §5 验收清单衔接）；§3 补
  `mc_fixture.js` 六模式条目；§6.5.1 补 B3；§6.6.1 看门狗判据文案对齐 T17 修复；§7 T17 条目补
  B3/B4/验收清单。PreDisasterSpeed DESIGN 补 cfg 表与 ModCenter 启停位记载（既有键复用）；
  指南 §三补 BOM 勘误（清单文件存 UTF-8 无 BOM，BOM 会被容错层静默降级 Tier 0）。
  publish.ps1 `$suiteMods` 与 installer.iss 组件表加 ModCenter——两文件系作者脏文件，
  **只改内容不代提交**（改动行清单见 task-18-report）。moddeps.json 本任务补登 T13 证据表
  5 项漏登读依赖（`Config_Action.During` / `Config_Bag.Size,Burden` / `Config_Furniture.ColdRate,BagId` /
  `Config_Talent.PlayerSelectId` / `Config_ProductionList.craft_level,show_level`——WriteEvidence 只读，
  modcompat 实跑 ALL GREEN）；无新挂钩点/字节签名；T4 登记的 Config_RobotAttribute 读依赖在役不变。
- 2026-10-25 任务 17：Meta 柜子位对齐作者 CabinetSize v3 终表。原条目（`Expand.WidthMult/
  FridgeWidthMult/VehicleWidthMult` 联动倍率 + `UiFit.*`）是作者 CabinetSize 倍率原型期（c899f20
  换位时点）的形状；作者 aa20303→d02e985 演进为**档位模型**（Bind 面 = `Global.MaxWidth/
  MaxHeight/Preset/ExcludeIds` + `Tier.Small..XLarge.Width/Height` + `Debug.*`，无 `Expand`/`UiFit`
  节）后旧条目全部悬空——快照按 Def 兜底展示虚构值、全部写被拒（T16 run4 实证 cabinetsize cfg
  无 Expand 节、联动对 `orig:[null,null]` 静默跳过）。修正：Meta 柜子页改 10 键（上限 2 + 四档
  宽高 8，int 滑条，Badge now——全部键已接 SettingChanged→Rerun 热改）；`Preset`（勿手改）/
  `ExcludeIds`（同 BP 口径不暴露）/`Debug.*` 不进面板；UiFit 组撤销（v3 用官方自适应，§6.5.2
  随之过时）。附带语义记录：CabinetSize 尚未 Bind `General.Enabled`，面板启停该 mod 走"toggle
  拒绝 + 快照回推自愈"路径（HandleToggle 恒回推，行视觉瞬时翻转后复位；接 gate 属作者侧后续）。
  回归夹具定稿 `tools/mc_fixture.js`（`_mc_fixture.js` 退役删除；tp 组净机 SKIP 语义）。
  **T17 看门狗修复（实锤产品 bug）**：孤儿看门狗旧判据 `S.root.isConnected` 在本引擎对被移除
  iframe 的孤儿文档恒 true（与 DOM 规范不符，CDP 实测）——宿主 iframe 被移除时看门狗永不触发，
  开面板进对局（或 ESC 收暂停菜单）键盘门漏开（enterworld 实证 kb 恒 on，人工 ESC 才还原）。
  改判 `宿主 doc.defaultView.frameElement`（iframe 被移除或文档被导航均 null，两死法实测）
  + `frameElement.isConnected`（Root 文档内元素，语义正常）。§6.6 孤儿看门狗机制段随之生效。
  T17 另两修：③ `ConfigBridge.Match` 完整展示键逐字命中**前置放行**（B3，§6.5.1 路由段补记）
  ——点分节名键（首例=柜子 v3 `Tier.Small.Width`，routeOf 首点切分出 section="Tier"）此前
  必被节守卫全拒；④ CabinetSize 侧 `ApplyHotKeys` 与本 mod 防抖落盘竞态（B4：陈旧文件 mtime
  事件被 Ticker Poll 在新写之后处理，旧盘值被当"记事本热改"赋回，UI 与 cfg 脱钩）——修在对方
  mod（`_lastBindChangeUtc` 陈旧文件闸，见其 DESIGN），**作者复核项**。延后项 6 项全闭环与
  作者人工验收清单（6 项体感）见 task-17-report.md §3/§5。
- 2026-10-25 任务 16 Fix Round 1（评审 P2+P3）：P2 子目录 dll 禁用——分离进程 `-NewName` 改喂
  `Split-Path -Leaf`（带路径名必败→静默失败+重复残留 toast）、`AbsentDisabled` 改递归扫描且
  返回相对名**剥 `.disabled`**（原递归化时首版漏剥后缀，实机抽查抓出即修）、启用反向改名同 Leaf；
  P3-1 名字过滤白名单删除→`IsSafeRelName` 边界守卫（仅剔绝对路径/盘符/`..`，`'` 翻倍转义放行）；
  P3-2 fixture t14 组排除缺席行（`TPL` 过滤，防净机残留 .disabled 误报）+ clickRow/tpClickToggle/
  tpRows/offRow 改属性值比对（guid 含 `\`/`'` 时 CSS 选择器失效，实机抓出）+ tpBool 还原补点
  加按钮态前置检查（T15-run3 同款点击竞态，防误补点翻回错值）。冒烟：分离进程脚本带
  子目录+空格+`'`+`;` 假名双向往返 PASS；实机子目录假 .disabled 缺席行出现→面板启用恢复 PASS；
  fixture run2/run4 均 70 PASS/6 SKIP/0 FAIL。实测补记见 §6.10.2 与 task-16-report Fix Round 1。
- 2026-10-25 任务 16：Tier 2 深度对接 + 重启级 dll 禁用。新增 `Contracts/ModCenterIntegration.cs`
  （指南 §4.1 接口源码原样编入，ModCenter.Integration 命名空间）与 `DllDisabler.cs`
  （[DisabledTp]<dll 相对名> marker / 退出双钩子 FlushDirty→分离进程 EncodedCommand 循环
  Rename-Item 只改名不删除 / 启用反向改名+残留补拉撤销 / ScanOnLoad 残留自愈+MC_TOAST
  一轮提示 / AbsentDisabled 缺席行 / TrySetAbsent 再启用路由）；`ThirdParty.cs` 增 Tier 2
  反射 duck-typing 发现（类型名+静态签名，禁接口强转）与 FireEnabledChanged/QueryStatesOf/
  SoftOf（返回 false 降级 Degraded）；`ConfigBridge.cs`：SetTpEnabled 三层分流（Tier2 回调/
  清单 softDisable/重启级，统一写 marker）、BuildTpMod 真值源换 marker+Soft/Absent 标记+
  ApplyStates 状态回报、BuildTp 补缺席行、MC_OPEN 残留 toast、MarkDirty 收 internal；
  `Payload.cs`：ModInfo.Soft/Absent、ItemInfo.Fail；`ModCenterPlugin.cs`：Load 接 ScanOnLoad+
  quitting 双钩子、Ticker 首帧 ReapplySoft、OnApplicationQuit→RunQuitHooks；panel.js：第三方
  停用三档横幅/toast（软停/重启级/缺席）、MC_TOAST→toastRaw、failed 项置灰锁控件。
  旧 [Disabled]<guid> 名单退役（游戏侧 cfg 9 条孤儿裸行已清理，4 条 tp 惰性条目保留）。
  实测见 §6.10.2。
- 2026-10-24 任务 15：Tier 1 声明式清单。新增 `ThirdParty.cs`（清单发现=程序集内嵌资源
  精确名/后缀名匹配、重复取第一个；解析容错=mcSpec 不识别/解析失败/缺 groups 整单降级
  Tier 0、键不在 cfg 项隐藏、逐字段回落；合并=display/summary/groups/label/note/badge/
  min-max-step/order 覆盖、cold→reboot 徽标归一、softDisable 暂存 `SoftDisableOf` 供 T16）；
  `ConfigBridge.cs` 三处：`Entry/ApplyRange/TypeName` 收 internal 供共用、`BuildTpMod` 接
  `ThirdParty.Apply`（false 保持 Tier 0 形态）、`HandleSet` tp 回执 badge 走
  `ThirdParty.BadgeOf`；`Payload.cs` `ModInfo` 增 `tier?` 字段（Tier 1=1）；panel.js 零改动。
  `tools/_mc_fixture.js` 增 7 项 t15 断言（净机 SKIP 语义）+ tpStruct 组跳过 tier=1 页。
  实测见 §6.9.2。
- 2026-10-24 任务 14：Tier 0 第三方通用页。`ConfigBridge.cs` 增 `BuildTp`（枚举插件→豁免→
  通用页：section 分组沿用对方 cfg 节名/键名即标签/徽标一律 cfg/Description→note/
  ApplyRange 反射合入抽公用/无范围 string→str 文本框/步进兜底）、`FindTp`+`TrySetTp`
  （通用写路由：只写实际存在的键，展示键原串剥前缀兜底）、`SetTpEnabled`（启停回用
  T6 时代本插件 cfg `[Disabled]` 标记名单，重启级；T16 换 dll .disabled 真禁用）、
  `HandleSet` 回执 k 用 `DisplayK` 展示键原串（防节前缀重复拼）；`panel.js` 增 str 文本
  控件（change 提交）；`tools/_mc_fixture.js` menu 模式增 18 项 tp 断言组 + 侧列行数/
  空占位断言自适应。实测见 §6.8.2。
- 2026-10-24 任务 13：启停接线升级。`ConfigBridge.SetModEnabled` 从本插件 cfg `[Disabled]`
  名单改为**直写各 mod 自己的 `General.Enabled`**（`Meta.cs` 新增 `MetaMod.EnabledSection/
  EnabledKey`，默认 General:Enabled，全套件无例外）；写入触发目标 mod 现役 SettingChanged→
  Rerun 热还原/重应用链；快照 enabled 真值源改为各 mod entry；名单机制整体移除（`Marker()`
  删除，旧 cfg 残留 `[Disabled]` 节惰性无人读）。`MC_EVIDENCE` 扩为多表断言快照（§6.2）。
  `tools/_mc_fixture.js` 新增 `toggle` 模式（任务 13 热还原全断言，供 T17 回归），menu 模式
  toggle 持久化断言改 PDS 自身 cfg。实测见 §6.7。
- 2026-10-24 任务 8：键盘门复用。新增 `FocusTick.cs`（面板开 `SetKeyboardEnabled(true)` +
  IME On、关双还原；门控 = MC_OPEN/MC_CLOSE 消息而非 IsPageActive——面板非官方页；
  `_imeSaved` 会话标记 + 层丢失就地还原 IME 两处对 PlanPick 的加固；沿写自证
  `BepInEx/ModCenter_kb.txt`，Load 清空）；`ModCenterPlugin.cs` Ticker 增 0.3s（unscaled）
  FocusTick 泵 + Load 清自证；`ConfigBridge.cs` MC_OPEN/MC_CLOSE 分别置开合；`panel.js`
  Root 侧 1s 孤儿看门狗（面板根失连补发 `MC_CLOSE{why:'host-doc-gone'}`）；csproj 增
  `UnityEngine.InputLegacyModule` 引用（`UnityEngine.Input` 所在模块，PlanPick 同款）。
  验证工具 `tools/_mc_kb_cdp.js` + `tools/_mc_kb_type.ps1`。实测见 §6.6.2 / task-8-report。
- 2026-10-24 任务 7 Fix Round 1（评审 NO-GO 三项）：①C1——`sendSet` 改发完整展示键 `k` 原串
  （初版剥前缀的联动 key 在桥 Match 三候选全不命中，背包/柜子 4 联动滑条写必败；
  §6.5.1 路由段重写），fixture 补 4 条联动端到端断言（消息形态→OK 回执→两键同值落盘→还原）；
  ②`__mcClose` 清节流句柄（备忘 3）；③DESIGN 补观感取舍注（备忘 2）。实测见 task-7-report
  Fix Round 1。
- 2026-10-24 任务 7：面板 UI 完整移植。`panel.js` 空壳→完整面板（原型 v4 `<style>` 面板段 +
  renderSide/renderCfg/ctl/toast/toggle/ovr 逻辑；span 开关改 button、行 div 带
  data-interactive 以进 clickThrough；ST.sel 记忆选中；toggle 乐观翻转+`__mcSnapSeq` 真推送
  判据；滑条 100ms 节流；ovr 面板侧序列化 canonical 排序；tp 空占位）；`ConfigBridge.cs`
  HandleOutbox 逐条 try（单条异常不弃同批后续，§6.2 旧债收口）；新增
  `tools/_mc_fixture.js`（menu/loadgame/outsetting 三模式装机断言）与 `tools/_mc_sendkey.ps1`
  （游戏窗口 ESC 注入）。实测：主菜单 41/41、OutSetting 4/4（遗留 A）、PlanPick 反射范围断言
  （遗留 D）、ovr 序列化往返、UiFit.Mode 拒绝路径（§6.5.2）；DESIGN 新增 §6.5。
- 2026-10-24 任务 6：配置桥正式化。新增 `Meta.cs`（九表）/`Payload.cs`（DTO+手写 JSON）/
  `ConfigBridge.cs`（快照/写/启停名单/消息分发/Save 防抖/MC_EVIDENCE）；`ModCenterPlugin.cs`
  Ticker 接 Pump、BridgeProbe.Table 转 internal、探针注释去已证伪期望值（真原值 2.4 口径）；
  `MenuEntryInjector.DrainOutbox` 回读入队；`panel.js` 增 `__mcRender/__mcEvent` 存根；
  DESIGN §4.1④ 样例修 CS1023、§2/§4.3-4 措辞清理；新增 §6（协议/防抖/实测记录）。
- 2026-10-24 任务 5：注入骨架。`WebBridge.cs`（通道+Eval）、`MenuEntryInjector.cs`
  （双锚点入口 + outbox 轮询）、`panel.js`（占位→空壳面板：遮罩/标题/✕/ESC/outbox 契约）、
  Ticker 接线；主菜单阶段 CDP 18 项全绿 + 日志回读。关键结论固化 §5（Root 无 clickThrough →
  面板挂宿主 iframe；button UA 重置按属性声明；OutSetting ESC 归 Unity）。
- 2026-10-24 任务 4：反射桥 API 实证。`ModCenterPlugin.cs` 增 `Debug.Probe` cfg 门控 +
  `BridgeProbe` 三拍探针（枚举→Config→Value 写→热生效验表→还原落盘）；结论固化 §4。
  探针代码常驻为诊断功能（默认关）。
