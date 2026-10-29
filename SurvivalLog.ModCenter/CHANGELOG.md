# 更新日志（SurvivalLog.ModCenter / MOD 管理中心）

> 本文件只记本 mod 的更新；套件/安装器版本史见总仓根 CHANGELOG.md。
> 版本号单源：本仓 csproj `<Version>`（作者定义，硬规则 1）。

## v<待作者定号>

> 版本号由作者定（硬规则 1）：发版前把本节标题改成实际版本号（如 `## v1.8.0`），并同步
> 本仓 csproj `<Version>` 与 `ModCenterPlugin.Version`。T16 单 dll 化后**只有一条版本线**
> （清理逻辑并入 ModCenter.dll）；`patchers\MCShell.dll` 是永久冻结的引导壳（1.0.0 只是
> 属性占位），只有壳接口签名变更才需随安装器更新。`release.ps1` 发版模式会校验本节标题
> 与 csproj 版本一致，未定号前只能 `-PackageOnly`。

### 新功能

- **游戏内生命周期**：更新/安装（12 组织仓 GitHub Release：检查、下载、MD5 校验、暂存、
  重启生效）、彻底卸载（dll + cfg + ModCenter 记录；强杀免疫）、第三方停用迁移
  （启动清理器改名为 `.disabled`）、手动加载排序（拖拽 + 键盘替代 + 硬依赖保护）。
- **冲突体检三层**：L1 已知冲突库（红）、L2 运行时补丁碰撞实测（橙）、L3 功能域同域
  （灰）；行内图标 + 点击详情 + 玩家自决，不自动处置。
- **套件仓库页与更新确认链**：12 仓五态、只告警不拦截（版本回退 / gamekb 不符）、
  全部更新（仅 Update 态）、下载进度与失败清理。
- **待生效条**：聚合摘要、撤销全部（含加载顺序）、立即重启（Steam 重拉）。
- **新组件 MCShell**：`BepInEx\patchers\MCShell.dll`（永久冻结的引导壳，单槽基座级；
  清理逻辑已并入 `SurvivalLog.ModCenter.dll` 的 `CleanupRunner`——游戏内更新 ModCenter
  即更新清理逻辑，版本线只有一条）。自卸载为两阶段墓碑（第一次重启写标记，第二次重启
  由壳删除本体）；游戏内自更新由壳在加载前预置替换（一次重启即生效）。

### 兼容与注意

- 打包布局变化：zip 现含 `BepInEx\plugins\SurvivalLog.ModCenter.dll` +
  `BepInEx\patchers\MCShell.dll` + `README.md`；手动安装需两个 dll 都放到对应目录。
- 从旧版升级：请删除 `BepInEx\patchers\MCCleaner.dll`（安装器 `[InstallDelete]` 自动清理；
  旧清理器与新逻辑并存会重复执行改名/排序）。游戏内更新只替换 ModCenter.dll（含清理逻辑），
  引导壳 MCShell.dll 只有接口签名变更才随安装器更新。
- 旧版 `[Disabled]` 名单退役（无读取方）；第三方停用状态沿用 `[DisabledTp]` marker，
  停用/启用为重启级。

## v1.7.0（2026-10-25）

- **新 MOD：MOD 管理中心 ModCenter**：主菜单与游戏内 ESC 菜单各加「MOD 管理」入口，面板集中
  开关/调参全套件 mod（滑条改完几秒生效，不再逐个手改 cfg 重启），含第三方 mod 通用页与
  重启级禁用；管理器本身不改任何玩法
