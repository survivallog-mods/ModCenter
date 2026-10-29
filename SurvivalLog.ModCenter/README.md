# MOD 管理中心 ModCenter（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不碰游戏本体文件（它只写各 mod 自己的配置，见第三节）
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

套件 mod 装多了，调个参数得一个个打开 `BepInEx\config` 里的记事本文件改完再重启游戏。本 mod 在主菜单和游戏内 ESC 菜单各加一个「MOD 管理」入口，打开面板就能集中查看、开关、调参整套件 mod——滑条拖完多数几秒内生效，不用重启。注意：它是个管理器，本身不改变任何游戏玩法。

## 二、功能清单

- **双入口**：主菜单与游戏内 ESC 设置菜单各一个入口按钮，对局中也能随时打开调参。
- **集中管理套件 9 个 mod**（背包扩容、柜子容量、保鲜增强、动作加速、储藏柜直达、角色解锁、机器人提速、自选规划、灾前加速）：开关、滑条、下拉直改配置，多数即时生效；面板每项标注生效时机（即时 / 读档后 / 重启后）。
- **正规写配置**：面板改的就是各 mod 自己的 `BepInEx\config` 配置文件，关面板自动保存（约 3 秒防抖落盘）；手改文件与面板互不冲突。
- **第三方 mod 三档接入**：未对接的第三方 dll 自动读取其配置生成通用设置页（能看能改）；带接入清单的按对方作者定义的中文页面展示；深度对接的开关可即时生效。
- **第三方 dll 一键禁用/启用**：禁用 = 把该 dll 改名为 `.disabled`（只改名、绝不删除文件），下次启动游戏不再加载；随时在面板里改回原名。
- **游戏内更新/安装**：面板里检查 12 个套件仓的新版，确认后下载、校验、暂存，重启生效；没有网络时仍可照旧用网盘包。
- **彻底卸载**：面板里卸载任一 mod = 删除它的 dll + 配置文件 + 管理器自身记录，下一次启动插件加载前执行；强杀游戏也不会让已卸载的 mod 复活。
- **手动加载排序**：拖拽或键盘 ↑↓ 调整加载顺序，规避部分 mod 之间的写序冲突；硬依赖链会自动保持依赖顺序。
- **冲突体检**：行内图标提示已知冲突（红）、运行时补丁碰撞（橙）、功能同域（灰），点击看原因与处置建议；只提示不自动处理，风险自负。
- **待生效条**：所有需要重启的动作会汇总在面板底部；可一键撤销，或点「立即重启」由 Steam 重拉游戏。

## 三、安全与还原

- 不改存档 / 不改变游戏玩法（本 mod 是管理器）/ 删 dll 即还原（入口按钮消失）。
- 面板的改动只写 `BepInEx\config\` 下各 mod 自己的配置文件（这正是它的功能），不碰游戏本体文件。
- 禁用第三方 mod = 该 dll 改名为 `.disabled`（只改名不删除）；重新启用即改回。若禁用后直接卸载本 mod，该 dll 保持 `.disabled` 状态，手动去掉文件名后缀即可恢复。
- 更新/安装/卸载/排序都是"写待办清单 → 下次启动插件加载前执行"，因此重启游戏后才会落地；执行逻辑在 `SurvivalLog.ModCenter.dll` 内（`CleanupRunner`），由永久冻结的引导壳 `BepInEx\patchers\MCShell.dll` 在补丁器阶段唤起——**游戏内更新 ModCenter 即更新清理逻辑**，引导壳只有接口签名变更才需随安装器更新。从旧版升级请删除旧的 `BepInEx\patchers\MCCleaner.dll`（安装器会自动清理）。
- 卸载本 mod 后，各 mod 保持面板所改的配置值（那些是各 mod 的正式配置，不是本 mod 的私有数据）。
- 面板打开期间键盘（含中文输入法）交给面板，游戏快捷键暂不触发；关闭面板即还原。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包 / 单个 zip**：已装 BepInEx 6 的玩家，把 `SurvivalLog.ModCenter.dll` 放进 `<游戏目录>\BepInEx\plugins\`，并把 `MCShell.dll` 放进 `<游戏目录>\BepInEx\patchers\`（**两个都要放**；引导壳在下次启动时唤起本体内的清理逻辑，执行更新/卸载/排序）。从旧版升级请**用安装器/完整包**或手动放置 `MCShell.dll` 并删除旧 `MCCleaner.dll`——只删旧件不放新壳会让清理链失效（面板会显示"启动清理组件缺失"）。

**卸载** = 删掉 `BepInEx\plugins\SurvivalLog.ModCenter.dll` 与 `BepInEx\patchers\MCShell.dll`，再删 `BepInEx\ModCenter\`（待办清单/暂存目录，存在才删；Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响），入口按钮消失；各 mod 配置保持面板所改值，被禁用的第三方 dll 需手动去掉 `.disabled` 后缀恢复。面板内"卸载本面板"走两阶段：确认后第一次重启只写卸载标记（本面板仍在本靴运行），第二次重启由引导壳删除本体、配置与 `BepInEx\ModCenter\` 记录后不再加载——DLL 被加载时无法自删，这是物理限制。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.modcenter.cfg`。本 mod 面板管理的是其他所有 mod 的配置，自身几乎无可调项，用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| Network.MirrorPrefix | （空） | GitHub 访问镜像前缀，默认空 = 直连。直连拉取/下载更新包正常就不要填；直连不通（如某些网络环境）时填一个镜像前缀，形如 `https://gh-proxy.com`（要求同时代理 api.github.com 与 release 下载，ghproxy.net/ghfast.top 只代理下载、不能用于本键）。镜像服务会迁移失效，失效换个前缀即可，不用改代码 |
| Network.CheckTimeoutMs | 5000 | 检查更新（拉版本信息）的单次超时毫秒数。超时即判失败（当次更新不可用），不重试 |
| Network.DownloadTimeoutMs | 60000 | 下载更新包的超时毫秒数。超时即失败并清理半成品文件，不重试 |
| Debug.Probe | false | 开发诊断探针（对配置读写通道做一轮自检并把证据写到 BepInEx 目录的 ModCenter_probe.txt）。玩家长开无意义，验完请改回 false |
| Debug.NetSelfTest | false | 网络自诊断探针（对 GitHub 更新源跑一轮 取版本→下载→校验→清理 并把证据写到 BepInEx 目录的 ModCenter_netspeed.txt）。玩家长开无意义，验完请改回 false |
| Debug.RepoSelfTest | false | 仓库决策层自诊断探针（游戏版本通道 dump + 全部 12 仓拉版本并做五态决策自检，证据写 BepInEx 目录的 ModCenter_gamever.txt / ModCenter_reposelftest.txt）。一轮消耗 12 次 GitHub 匿名 API 请求，玩家长开无意义，验完请改回 false |
| Debug.PatchScan | false | 补丁碰撞诊断探针（把运行时已挂补丁方法与归属 dump 到 BepInEx 目录的 ModCenter_patchscan.txt）。玩家长开无意义，验完请改回 false |

面板还会在自己的 cfg 里记录被禁用的第三方 dll 名单（`[DisabledTp]` 节，由面板自动维护，无需手改）。

## 六、English

**Mod Center** — A unified in-game panel to view, toggle and tune every mod in this suite (and manage third-party plugins).

**Features**
- One entry in the main menu and one in the in-game ESC menu; open the panel anytime.
- Manage all 12 suite mods (backpack, cabinet capacity, cabinet burden, fridge, action speed, cabinet access, role unlock, robot speed, plan pick, pre-disaster speed, endless mode, this panel): toggles, sliders and dropdowns write the real config, most take effect within seconds without restarting; each item shows when it applies (instant / after loading / after reboot).
- Writes each mod's own config file under BepInEx\config (debounced autosave); manual edits and panel edits stay in sync.
- Third-party plugins at three levels: auto-generated generic settings page, manifest-defined Chinese pages, or deep-integration with instant on/off.
- One-click disable/enable for third-party DLLs: disable = rename to `.disabled` (never deleted); re-enable anytime in the panel.
- In-game update/install of the 12 suite repos (check → download → verify → stage → apply after restart), thorough uninstall (DLL + config + records, executes before plugins load, survives force-kill), drag-and-drop / keyboard load-order sorting with hard-dependency protection, and a three-layer conflict check (known conflicts / runtime patch collisions / same-domain). Everything is advisory only — no automatic fixes.

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package: put `SurvivalLog.ModCenter.dll` into `BepInEx\plugins\` **and** `MCShell.dll` into `BepInEx\patchers\` (both are required; the frozen shell runs the cleanup logic embedded in the plugin DLL before plugins load). Delete the old `MCCleaner.dll` when upgrading — the installer removes it automatically. Uninstall = delete both DLLs (and `BepInEx\ModCenter\` if present); the menu entries disappear and every mod keeps its current config. Third-party DLLs disabled through the panel stay renamed to `.disabled` — remove the suffix to re-enable them. In-game updates of ModCenter also update the cleanup logic; the shell never changes unless its interface signature changes. Uninstalling this panel from inside it takes two restarts (first writes the tombstone, second lets the shell delete the DLL and records).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.modcenter.cfg`. The panel manages every other mod's config. Local keys: an optional GitHub mirror prefix (empty by default = direct; set e.g. `https://gh-proxy.com` only if direct connections fail — the mirror must proxy both api.github.com and release downloads), check/download timeouts, and debug probes that should stay false.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
