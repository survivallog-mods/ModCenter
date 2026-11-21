# 套件名单单源化、在线安装/版本选择、运行错误日志 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把套件成员身份收敛到 COS 桶根 `suite.json` 单一来源，实现新 mod 自动可见、在线安装/更新、任意版本切换、ModCenter 自身移出列表并可在线更新，以及本次启动运行错误日志与告警接入。

**Architecture:** 客户端新增 `SuiteRoster`（名单缓存）与 `ErrorLog`（日志监听/缓冲）两个独立模块；`ConfigBridge` 的页面枚举、仓库行、写路由、冲突判定全部改查 `SuiteRoster`；发布侧由共享 PowerShell 助手让每次发版自动 upsert `suite.json` 并按桶内实际 zip 过滤 `versions.json`。GitHub 只保留代码托管与发版，不再参与名单/版本数据。

**Tech Stack:** C# net6.0 / BepInEx 6 (IL2CPP)、Unity WebUI (`panel.js`)、PowerShell 5.1、腾讯云 COS（coscli）、System.Text.Json（只读解析）+ 手写 StringBuilder（AOT 写入）。

**Spec:** `docs/superpowers/specs/2026-11-21-suite-roster-single-source-design.md`
**Prototype:** `docs/superpowers/specs/2026-11-21-error-log-panel-prototype.html`

## Global Constraints

- **COS 是唯一数据源**：名单、版本、zip 下载全部只走 `https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com`；不请求 GitHub index、不做逐仓 GitHub 补查、不做 GitHub 资产回退。
- `suite.json` 旧字段 `mod/version/asset/md5/gamekb/assetUrl/notes` 原样保留；新字段 `guid/display/summary/dll` 全部可选，缺省回退命名约定/仓名。
- 套件身份只认 `suite.json` 的 `guid`（显式）；GUID 前缀不参与判定。
- 行数上限：新增前端/后端模块 ≤500 行，工具/辅助模块 ≤300 行；存量超限文件只做必要修改，不主动重构。
- 线程纪律：`IL2CPPChainloader.Plugins`、快照构建、面板消息只在主线程；日志监听器可被任意线程调用，只入并发队列，由 `Pump` 主线程消费。
- 运行错误：只收 `LogLevel.Error | Fatal`；纯内存、不落盘；每个 mod 最多 50 条；重启清空；不做未读/历史。
- 告警：红组/橙组两级；运行异常归红组；行内图标按组出一枚；原有冲突卡内容、顺序、`去调序`行为不变。
- 本轮删除 L3 同域提示（`Meta.Domains`/`DomainOf`/`DomainMates`/`HasExternalMate`/`DomainIc`）。
- 项目无测试工程；按用户规则**不新增自动化测试**。每个任务的验证 = Release 编译 + 指定实机/只读 PowerShell 场景。
- 工作区已有用户未提交改动（`Meta.cs` 增加 CodexHint、`RepoDecision.cs` 增加 `"CodexHint"`）；本计划会删除这两张静态表，执行前确认这些改动可随重构一并处理。
- 提交信息格式：`<type>：<描述>`（中文冒号，技术名词保留英文）。

## Review Focus

- `suite.json` 缺失/结构坏/单行字段不全：客户端不得崩溃，名单不可用时已装 mod 仍可见（三方或清单 `suite:true` 补偿）。
- 来源无法归属的错误：必须进"未归属"，不得静默丢弃或错挂到别的 mod。
- 版本台账有记录但桶内 zip 缺失：下载必须报错且不写 pending；正常路径不得出现 404。
- 面板自身卸载：任何面板入口或直接 `MC_UNINST` 都必须拒绝。
- 高频/重复错误：超过 50 条只保留最新 50；连续相同消息折叠为一条并记录重复次数。

---

### Task 1: 发布侧共享助手 `suite-common.ps1`

**Files:**
- Create: `E:\Game\Mod\survivallog-mods\suite\tools\suite-common.ps1`
- Test: 只读 PowerShell 会话（不写桶）

**Interfaces:**
- Consumes: `coscli.exe` / `secrets\coscli.yaml`（同目录）、桶内 `mods/<仓>/update.json`、本机 `..\<仓>\SurvivalLog.<仓>\modcenter.json`
- Produces:
  - `Get-CosModNames` → `string[]`（桶内 `mods/` 一级目录名，按名称排序）
  - `Get-ModIdentity([string]$Mod)` → `[ordered]@{ guid; display; summary }`（清单缺失回退命名约定/仓名）
  - `Read-CosJson([string]$Url)` → `PSCustomObject`（字节读 + UTF-8 解码）
  - `New-SuiteRow([string]$Mod, $Update, $Identity)` → `[ordered]` 行（`mod/guid/display/summary/dll/version/asset/md5/gamekb/assetUrl/notes`）
  - `Upsert-SuiteRow($Row)` → 读桶根 `suite.json`、按 `mod` 替换/追加、写临时文件、`coscli cp` 回桶根
  - `Filter-VersionsByBucket([string]$Mod, [string]$VersionsPath)` → 过滤后的临时文件路径（只保留桶内实际存在的 asset + 本次新行）
  - `Upload-File([string]$LocalPath, [string]$CosObjectPath)` → `coscli cp` 薄封装（失败抛错）

- [ ] **Step 1: 写脚本骨架与只读函数**

实现 `Get-CosModNames`：调用 `& $coscli -c $cfg ls cos://sl/mods/`，只匹配 `^\s*mods/([^/]+)/\s+\|\s+DIR` 的目录行，返回 `$Matches[1]` 排序数组；`$LASTEXITCODE -ne 0` 抛错。
实现 `Read-CosJson`：`Invoke-WebRequest -UseBasicParsing` + `RawContentStream` 字节读 + `[Text.Encoding]::UTF8.GetString`（沿用 `publish-index.ps1` 现有写法），再 `ConvertFrom-Json`。
实现 `Get-ModIdentity`：`$suiteRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`；清单路径 `$suiteRoot\<Mod>\SurvivalLog.<Mod>\modcenter.json`；读 `id`/`display`/`summary`；缺失时 `guid = 'com.local.survivallog.' + $Mod.ToLowerInvariant()`、`display = $Mod`、`summary = ''`。`ModCenter` 自身无清单：`display = 'Mod 管理中心'`、`guid = 'com.local.survivallog.modcenter'`。

- [ ] **Step 2: 写 `New-SuiteRow` 与 `Upsert-SuiteRow`**

`New-SuiteRow` 的输出顺序固定为：`mod,guid,display,summary,dll,version,asset,md5,gamekb,assetUrl,notes`；`dll` 固定 `plugins/SurvivalLog.<Mod>.dll`；`assetUrl = 'https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com/mods/' + $Mod + '/' + $Update.asset`。
`Upsert-SuiteRow`：`Read-CosJson '.../suite.json'`；若 404/坏结构则从空 `mods` 数组开始；按 `mod` 原地替换，找不到则追加；保留其他行原字段与顺序；`ConvertTo-Json -Depth 6` + `UTF8Encoding($false)` 写 `$env:TEMP\suite.json`；`& $coscli -c $cfg cp $temp 'cos://sl/suite.json'`。

- [ ] **Step 3: 写 `Filter-VersionsByBucket`**

读 `$VersionsPath` 的 `versions` 数组；`& $coscli -c $cfg ls ('cos://sl/mods/' + $Mod + '/')` 得到桶内文件名集合；保留 `asset` 在集合中的行（保持原顺序）；写 `$env:TEMP\versions_<Mod>_filtered.json`（`UTF8Encoding($false)`）；返回该路径。
实现 `Upload-File`：`& $coscli -c $cfg cp $LocalPath $CosObjectPath`，`$LASTEXITCODE -ne 0` 抛错。

- [ ] **Step 4: 只读验证**

Run:
```powershell
powershell -NoProfile -Command ". 'E:\Game\Mod\survivallog-mods\suite\tools\suite-common.ps1'; Get-CosModNames; Get-ModIdentity CabinetEverywhere | Format-List"
```
Expected: 打印 12 个目录名；`CabinetEverywhere` 返回 `guid=com.local.survivallog.cabineteverywhere`、中文 `display`。不得上传任何文件。

- [ ] **Step 5: Commit（suite 仓）**

```bash
cd E:\Game\Mod\survivallog-mods\suite
git add tools/suite-common.ps1
git commit -m "feat：新增套件索引共享助手（COS 目录枚举/身份读取/suite.json upsert/版本台账过滤）"
```

---

### Task 2: `sync-cos.ps1` 发版时自动维护名单与台账

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\suite\tools\sync-cos.ps1`
- Test: 只读/临时文件验证

**Interfaces:**
- Consumes: Task 1 的 `Filter-VersionsByBucket`、`New-SuiteRow`、`Upsert-SuiteRow`、`Read-CosJson`
- Produces: 每次单 mod 上传后，桶根 `suite.json` 已 upsert 本行；桶内 `versions.json` 只含实际存在的 zip

- [ ] **Step 1: 接入助手**

脚本开头 `$ErrorActionPreference = 'Continue'` 之后加 `. (Join-Path $PSScriptRoot 'suite-common.ps1')`。
`$Mod` 分支顺序改为：上传 zip → 若 `$Versions` 则 `$filtered = Filter-VersionsByBucket $Mod $Versions` → 上传 `update.json` → 若 `$Versions` 上传 `$filtered` → 读 `$upd = Get-Content $UpdateJson -Raw | ConvertFrom-Json`，`$id = Get-ModIdentity $Mod`，`Upsert-SuiteRow (New-SuiteRow $Mod $upd $id)`。

- [ ] **Step 2: 失败语义**

`Upsert-SuiteRow` 失败不抛断整次发版：`try { ... } catch { Write-Warning ('suite.json upsert 失败：' + $_.Exception.Message + '；zip/update.json/versions 已上传，重跑本脚本或 publish-index 全量重建补救') }`。zip/update/versions 上传失败仍按现有 `throw`。

- [ ] **Step 3: 验证（不写桶）**

Run:
```powershell
powershell -NoProfile -Command ". 'E:\Game\Mod\survivallog-mods\suite\tools\suite-common.ps1'; $u = Read-CosJson 'https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com/mods/CabinetEverywhere/update.json'; New-SuiteRow 'CabinetEverywhere' $u (Get-ModIdentity 'CabinetEverywhere') | ConvertTo-Json"
```
Expected: 行含 `guid/display/summary/dll/version/asset/md5/gamekb/assetUrl/notes`，`version` 与桶内 update.json 一致；不产生上传。

- [ ] **Step 4: Commit（suite 仓）**

```bash
cd E:\Game\Mod\survivallog-mods\suite
git add tools/sync-cos.ps1
git commit -m "feat：sync-cos 发版时自动 upsert suite.json 并按桶内文件过滤 versions.json"
```

---

### Task 3: `publish-index.ps1` 全量重建 + 首次审计

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\publish-index.ps1`
- Test: `-DryRun` 只读

**Interfaces:**
- Consumes: Task 1 助手
- Produces: `publish-index.ps1 [-Yes] [-DryRun]`；全量扫描桶内目录重建 v2 `suite.json`；审计各仓 `versions.json`（剔除桶内不存在的 asset）

- [ ] **Step 1: 参数与名单**

`param([switch]$Yes, [switch]$DryRun)`。删除写死的 `$mods` 数组与 `$ghExe`/`$idxRepo`；`$mods = Get-CosModNames`。保留 `$cosBase` 常量或改用助手内常量。

- [ ] **Step 2: 聚合与审计**

逐仓：`Read-CosJson ($cosBase + '/mods/' + $m + '/update.json')`（失败记入 `$skipped`）；`$identity = Get-ModIdentity $m`；`$rows.Add((New-SuiteRow $m $u $identity))`。
若 `$Versions` 可用（桶内存在 `versions.json`）：读出并 `Filter-VersionsByBucket`；`$DryRun` 时只累计 `原行数 - 过滤后行数` 并打印 `AUDIT <mod>: N -> M`；非 DryRun 时 `Upload-File $filtered ('cos://sl/mods/' + $m + '/versions.json')`。

- [ ] **Step 3: 输出与上传**

`$suite = [ordered]@{ schema = 2; generatedAt = ...; mods = $rows }`，写 `$env:TEMP\suite.json`。`$DryRun` → 打印路径与行数后 `exit 0`，不上传。非 DryRun → 调用 `suite\tools\sync-cos.ps1 -Index $suitePath`（COS 唯一上传）；删除原 GitHub index release 段。

- [ ] **Step 4: 验证**

Run:
```powershell
powershell -NoProfile -File E:\Game\Mod\survivallog-mods\publish-index.ps1 -Yes -DryRun
```
Expected: 打印 12 行 `mod vX`、`AUDIT` 行（如 CabinetEverywhere 15→7）、不产生任何上传；退出码 0。

- [ ] **Step 5: 备份（无 git 跟踪）**

`publish-index.ps1` 位于套件工作区根，套件根不是 git 仓：改动前复制一份 `publish-index.ps1.bak`；本任务无 commit。

---

### Task 4: 各 mod 清单增加 `suite` 标识

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\<Mod>\SurvivalLog.<Mod>\modcenter.json`（11 份：CabinetEverywhere、EndlessGuard、FridgeChill、PlanPick、PreDisasterSpeed、QuickAction、RobotSpeed、SaveMgr、UnlockHub、CodexHint、CraftQueue）
- Test: PowerShell 解析

**Interfaces:**
- Produces: 每份清单顶层 `"suite": true`（可选字段）

- [ ] **Step 1: 加字段**

每份清单在 `"mcSpec": 2,` 后加一行 `"suite": true,`。不动其他字段。

- [ ] **Step 2: 验证**

Run:
```powershell
powershell -NoProfile -Command "Get-ChildItem E:\Game\Mod\survivallog-mods -Directory | ForEach-Object { $p = Join-Path $_.FullName ('SurvivalLog.' + $_.Name + '\modcenter.json'); if (Test-Path $p) { $j = Get-Content $p -Raw | ConvertFrom-Json; '{0}: {1}' -f $_.Name, $j.suite } }"
```
Expected: 11 个清单输出 `True`；无清单的 ModCenter 除外。

- [ ] **Step 3: Commit（各 mod 仓分别提交）**

```bash
git commit -m "feat：清单增加 suite 标识（名单不可用时的套件身份补偿）"
```

---

### Task 5: `RepoClient` v2 字段与 COS-only 拉取

**Files:**
- Modify: `SurvivalLog.ModCenter/RepoClient.cs`
- Test: 编译

**Interfaces:**
- Consumes: 无
- Produces:
  - `RepoClient.UpdateInfo` 新增 `public string Guid, Display, Summary, Dll;`（可选）
  - `public static string AssetUrlOf(string repoKey, string asset)` → `CosBase + "/mods/" + repoKey + "/" + asset`
  - `public static async Task<List<VersionInfo>> FetchVersions(string repoKey)`（Task 11 使用；本任务先建签名可留空实现？不——本任务只改 v2 字段与 FetchSuite；`FetchVersions` 放 Task 11）
  - `FetchSuite` 只请求 COS 桶根；失败返回 `SuiteResult { Status = 0 }`，不请求 GitHub

- [ ] **Step 1: 扩展 `UpdateInfo` 与 `ParseSuiteJson`**

`UpdateInfo` 追加四个可选字段。`ParseSuiteJson` 逐行读 `guid/display/summary/dll`（`GetStr`），缺失为 null，**不参与丢行判定**；现有六字段完整性校验保持不变。

- [ ] **Step 2: 删除 GitHub 回退**

`FetchSuite` 删除 GitHub index 分支（`OrgPrefix + "index/releases/latest"`、`FetchLatestEx`、`TryFetchCosJson` 及其调用）；只保留 `GetString(CosBase + "/suite.json", ..., api: false)`。COS 失败 → `new SuiteResult { Status = 0 }` 并 `Log` 一行 warn。
**过渡规则（保证每任务可编译）**：`FetchLatest` / `FetchLatestEx` 本任务暂不删除——RepoProbe 仍在调用；Task 7 适配 RepoProbe 后再删。

- [ ] **Step 3: 编译**

Run:
```powershell
E:\Game\Mod\survivallog-mods\sdk\dotnet\dotnet.exe build SurvivalLog.ModCenter\SurvivalLog.ModCenter.csproj -c Release --nologo
```
Expected: 0 warning/0 error（允许存量 warning）。

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor：RepoClient 名单行支持 guid/display/summary/dll，套件数据只走 COS"
```

---

### Task 6: `SuiteRoster.cs` 名单模型与缓存

**Files:**
- Create: `SurvivalLog.ModCenter/SuiteRoster.cs`（≤300 行）
- Modify: `SurvivalLog.ModCenter/RepoDecision.cs`（加 `RepoRow.Dll`、`CaptureLocalVersions`、`BuildRows`）
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（`HasDeleteIntentFor` 改收目标 dll）
- Modify: `SurvivalLog.ModCenter/ModCenterPlugin.cs`（Load 时 `SuiteRoster.LoadCache()`）

**Interfaces:**
- Consumes: Task 5 的 `UpdateInfo` 新字段
- Produces:
  - `internal static class SuiteRoster`
    - `static bool Ready { get; }`
    - `static IReadOnlyList<RepoClient.UpdateInfo> Entries { get; }`
    - `static bool TryGetByGuid(string guid, out RepoClient.UpdateInfo e)`
    - `static bool TryGetByRepo(string repoKey, out RepoClient.UpdateInfo e)`
    - `static string GuidOf(RepoClient.UpdateInfo e)` / `DisplayOf(e)` / `DllOf(e)`
    - `static void LoadCache()`
    - `static void ApplyFromFetch(List<RepoClient.UpdateInfo> entries)`
    - `static List<RepoRow> BuildRows(IEnumerable<RepoClient.UpdateInfo> entries, Dictionary<string,string> localVers)`
  - `RepoDecision.CaptureLocalVersions()` → `Dictionary<string,string>`（主线程读 Chainloader；key=GUID，value=版本串）
  - `RepoRow.Dll` 字段

- [ ] **Step 1: 实现 `SuiteRoster` 缓存读写**

缓存路径 `Path.Combine(Paths.BepInExRootPath, "ModCenter", "suite-cache.json")`。写入用 `StringBuilder` + `JsonW.Str`（字段同 suite.json 行，外加 `schema:2`）；读取用 `JsonDocument`，逐行容错（缺 `mod` 丢行），写临时文件后 `File.Move` 原子替换。`LoadCache()` 失败只 `Log` warn，不影响启动。

- [ ] **Step 2: 实现查询与行构建**

`TryGetByGuid` 的 key = `GuidOf(e)`（显式 `e.Guid`，否则 `"com.local.survivallog." + e.Mod.ToLowerInvariant()`）；`TryGetByRepo` 按 `Mod`（OrdinalIgnoreCase）。`BuildRows`：每个条目生成 `RepoRow { Name = DisplayOf(e), RepoKey = e.Mod, Guid = GuidOf(e), Dll = DllOf(e), Remote = e.Version, Local = localVers.TryGetValue(guid) }`；不读静态表。

- [ ] **Step 3: `RepoDecision` 清理**

删除 `RepoKeys` 与 `SnapshotLocal()` 的**调用面**留到 Task 7；本任务先把两者标记 `[Obsolete("Task 7 删除")]`，保留实现以免 `StartRepoFetch`/RepoProbe 编译中断。保留 `Build`、`CompareVersions`、`GuidOf`、`DllRelOf`（`DllRelOf` 仅作为 `SuiteRoster.DllOf` 的缺省实现）。新增 `CaptureLocalVersions()`（复制 `SnapshotLocal` 的枚举逻辑，只返回 GUID→版本字典）。

- [ ] **Step 4: 目标 dll 路径统一走 `RepoRow.Dll`**

`PendingWriter.AddUpdate`：`Target = row.Dll ?? RepoDecision.DllRelOf(row.RepoKey)`；自更新预置目标同样用该值。
`ConfigBridge.HasDeleteIntentFor(string repoKey)` 改为 `HasDeleteIntentFor(string dllRel)`，调用点传 `ui.Row.Dll`。
`RepoDecision.DllRelOf` 保留为缺省推导（不再被下载链直接使用）。

- [ ] **Step 5: 注册缓存加载**

`ModCenterPlugin.Load` 末尾调用 `SuiteRoster.LoadCache();`（主线程，`IL2CPPChainloader` 尚未就绪时只用缓存，不访问 Chainloader）。

- [ ] **Step 6: 编译 + 启动验证**

Run build；部署 `bin\Release\SurvivalLog.ModCenter.dll` 到游戏 `BepInEx\plugins\`，启动游戏。
Expected: LogOutput 出现 `roster: N 条（缓存）`；面板仍能打开（此时页面尚未切换，允许旧行为）。

- [ ] **Step 7: Commit**

```bash
git commit -m "feat：新增 SuiteRoster 名单缓存与仓库行构建，RepoDecision 移除静态仓清单"
```

---

### Task 7: `StartRepoFetch` 改为名单驱动

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Modify: `SurvivalLog.ModCenter/ModCenterPlugin.cs`（RepoProbe 适配）
- Test: 编译 + 实机

**Interfaces:**
- Consumes: Task 6 的 `SuiteRoster.BuildRows` / `CaptureLocalVersions`
- Produces: `_repoRows`、`_repoInfos` 由 `suite.json` 条目构建；`RepoUi` 增加 `List<RepoClient.UpdateInfo> Entries`

- [ ] **Step 1: 重构拉取流程**

`StartRepoFetch(bool manual)`：
1. 主线程 `var localVers = RepoDecision.CaptureLocalVersions();`
2. `var rows = SuiteRoster.BuildRows(SuiteRoster.Entries, localVers);`（当前缓存；可为空）
3. 后台 `var suite = await RepoClient.FetchSuite();`
4. 成功（`suite.Suite != null`）：`entries = suite.Suite`；`rows = SuiteRoster.BuildRows(entries, localVers)`；`RepoDecision.Build(rows, local)`；`RepoUi { Kind=1, Rows=rows, Entries=entries, Infos=entries.ToDictionary(e=>e.Mod), ... }`
5. 失败：`entries = SuiteRoster.Entries`；`rows` 用缓存条目重建；`RepoDecision.Build(rows, local)`；`RepoUi` 带 `Entries=entries`、`FetchFailed=true`
6. 删除"索引不可用则逐仓 `FetchLatestEx` 补查"整段及 403 限流分支；`RateWaitText` 若不再被引用则删除。
7. 本任务最后删除过渡件：`RepoDecision.RepoKeys`、`RepoDecision.SnapshotLocal()`、`RepoClient.FetchLatest`、`RepoClient.FetchLatestEx`、`TryFetchCosJson`（此时所有调用方已改完）。

- [ ] **Step 2: 主线程收割**

Kind 1 分支：先 `SuiteRoster.ApplyFromFetch(ui.Entries)`（仅成功轮；失败轮传 null 不覆盖缓存），再 `_repoRows = ui.Rows`、`_repoInfos = ui.Infos`，后续 `PushRepoData`/徽标逻辑不变。`ui.FetchFailed` 时顶部/检查更新 toast 文案沿用现有限流/失败提示的简化版（"检查失败，使用上次结果"）。

- [ ] **Step 3: RepoProbe 适配**

`ModCenterPlugin.cs` 的 `RepoProbe.Start()`：`_rows = SuiteRoster.BuildRows(SuiteRoster.Entries, RepoDecision.CaptureLocalVersions())`；头行仓数改 `SuiteRoster.Entries.Count`。`RunAsync` 的逐仓 `RepoClient.FetchLatest(r.RepoKey)` 循环改为一次 `await RepoClient.FetchSuite()`；成功则按 `Mod` 填 `_rows` 的 `Remote/Gamekb`，失败则 `fetchFail = _rows.Count`。B/C 合成分支保持原样（仍走 `RepoDecision.Build`）。

- [ ] **Step 4: 编译 + 实机**

Build + 部署，启动游戏（只装 ModCenter）。
Expected：LogOutput 打印 `repo fetch: 名单 N 行`；面板套件区出现可安装行（页面切换在 Task 8 完成前可能仍为旧形态，但 `_repoRows` 已按名单生成）；`Debug.RepoSelfTest=true` 时探针仍能生成证据文件、不再请求 GitHub。

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor：仓库检查改为 suite.json 名单驱动，删除逐仓 GitHub 补查"
```

---

### Task 8: 页面枚举/写路由/冲突判定改查名单

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Modify: `SurvivalLog.ModCenter/Meta.cs`
- Modify: `SurvivalLog.ModCenter/ThirdParty.cs`
- Test: 编译 + 实机

**Interfaces:**
- Consumes: `SuiteRoster`
- Produces: `EnumeratePageMods()` 只产出名单条目；`Resolve`/`MetaOfGuid`/`DisplayNameOf`/`IsSuiteGuid`/`TpExempt` 只查名单；`Meta.KnownConflicts` 保留

- [ ] **Step 1: 页面枚举与未安装行**

`EnumeratePageMods()` 改为 `foreach (var e in SuiteRoster.Entries) yield return new MetaMod { Guid = SuiteRoster.GuidOf(e), Name = SuiteRoster.DisplayOf(e), En = e.Mod, Desc = e.Summary ?? "" };`。
`BuildSnapshot` 的 route ③ 条件 `cfg == null && Meta.RosterOf(...)` 改为 `cfg == null && SuiteRoster.TryGetByGuid(orig.Guid, out var entry)`；`Missing` 行的 `Name/En/Desc` 用 `SuiteRoster.DisplayOf(entry)`/`e.Mod`/`e.Summary`。

- [ ] **Step 2: 写路由与显示名**

`Resolve`：先按 GUID/En/尾段查 `SuiteRoster`，命中即 `EffectiveOf(base)`；删除 `Meta.Mods`/`Meta.Roster` 两个循环。
`MetaOfGuid`：`SuiteRoster.TryGetByGuid` 命中 → 合成 `MetaMod` 基线。
`DisplayNameOf`：名单 `display + "（" + En + "）"`；未命中返回原串。`InstalledCfRows` 的 `Name` 同源。
`PendNameOf`/`nameOf` 等原 `Meta.Roster` 遍历改为 `SuiteRoster.Entries`。

- [ ] **Step 3: 套件判定与三方排除**

`Meta.IsSuiteGuid(guid)` → `SuiteRoster.TryGetByGuid(guid, out _)`（名单未就绪返回 false）。
`TpExempt(guid, name)`：豁免条件 = 名单命中 ∪ `ModCenterPlugin.Guid` ∪ `TpProbeNames`；删除 `FirstPartyPrefix` 判断。
`ThirdParty.cs` 中 `Meta.RosterOf(...)`/`Meta.IsSuiteGuid(...)` 调用改为名单版本；mcSpec 2 扩展字段日志条件保持"非套件"语义。

- [ ] **Step 4: 删除静态表与 L3**

删除 `Meta.Mods`、`Meta.Roster`、`RosterEntry`、`RosterOf`、`SuiteGuids`、`ExtraNames`、`ExtraNameOf`、`Domains`、`DomainOf`。
删除 L3：`CfRow.Domain`、`DomainMates`、`HasExternalMate`、`DomainIc`、`CfIcsFor` 中的 L3 分支、`HandleCf` 中的 L3 卡片。
保留 `KnownConflicts`、`MatchL1`、`DllBaseName`、`TagOf`；`SelfName = "Mod 管理中心"` 常量放 `Meta`。

- [ ] **Step 5: 编译 + 实机三场景**

Build + 部署，启动游戏：
1. 只装 ModCenter：套件区列出名单成员（可安装行）。
2. 装一个不在名单的第一方前缀测试 mod：进三方区，配置页可见。
3. 名单内已装 mod：配置页可读写、启停正常。
Expected：不再出现"两边都不进"的 mod；L3 同域橙色提示消失。

- [ ] **Step 6: Commit**

```bash
git commit -m "refactor：套件页面/写路由/冲突判定改为 suite.json 名单驱动，删除静态名单与 L3 同域提示"
```

---

### Task 9: 名单不可用时的清单补偿

**Files:**
- Modify: `SurvivalLog.ModCenter/ThirdParty.cs`
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Test: 实机（删缓存 + 断网）

**Interfaces:**
- Consumes: Task 4 的清单 `suite` 字段
- Produces: `ThirdParty.DeclaresSuite(PluginInfo info)`；`!SuiteRoster.Ready` 时套件页枚举补偿

- [ ] **Step 1: 解析 `suite` 字段**

`McManifest` 增加 `public bool Suite;`；`Parse` 中 `Suite = root.TryGetProperty("suite", out var s) && s.ValueKind == JsonValueKind.True;`。新增 `internal static bool DeclaresSuite(BepInEx.PluginInfo info)`：`ManifestOf(info)?.Suite == true`。

- [ ] **Step 2: 补偿枚举**

`EnumeratePageMods()` 改为：先产出 `SuiteRoster.Entries`；若 `!SuiteRoster.Ready`，再枚举 `LoadedPlugins()`，对 `Instance != null`、非自身/探针、`DeclaresSuite(info)` 且 GUID 不在名单的插件，产出 `MetaMod { Guid, Name = 清单 display ?? 插件名, En = 清单 en ?? 插件名, Desc = 清单 summary ?? "" }`（去重）。

- [ ] **Step 3: 实机验证**

删除 `BepInEx\ModCenter\suite-cache.json`，断网启动。
Expected：套件 mod（清单 `suite:true`）仍在套件区，配置页可读；无标识的 mod 进三方区；顶部提示"套件名单未获取"。

- [ ] **Step 4: Commit**

```bash
git commit -m "feat：名单不可用时按清单 suite 标识补偿套件分组"
```

---

### Task 10: ModCenter 自身移出列表、禁止卸载、头部更新入口

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Test: 实机

**Interfaces:**
- Consumes: 快照 `Self { Ver, Ics }`
- Produces: 面板无 `__self` 行/详情页；头部版本区可点更新；`HandleUninst`/`HandleUninstDo` 拒绝自身 GUID

- [ ] **Step 1: 面板删除自身行与页面**

删除 `side()` 中的 `__mc-mod-row` 自身行块（`data-guid="__self"`）与 `h += '<div class="__mc-cat">面板自身</div>'`。
删除自身详情页分支（`ST.sel === '__self'`）、`uninstActHtml(SELF_GUID)` 的渲染路径、`findMod` 的 `__self` 虚拟行、`if (guid === '__self') return;` 等防呆分支。保留 `SELF_GUID`/`SELF_NAME` 常量供 `repoRowOfGuid` 与头部使用。

- [ ] **Step 2: 头部更新入口**

`S.sub` 保持版本文本；当 `snap.self.ics` 含 `k === 'yellow'` 时，渲染为可点击元素（`cursor:pointer`，文本 `vX → 可更新`，title 用 ics.tip）。点击处理：`var m = findMod(SELF_GUID); if (m) openUpdModal(m);`（`repoRowOfGuid(SELF_GUID)` 必须命中仓库行；未命中不响应）。

- [ ] **Step 3: 服务端禁止卸载**

`HandleUninst(guid)` 与 `HandleUninstDo(guid)` 开头：`if (string.Equals(guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase)) { McToast("面板自身不能卸载", "err", "想删除请手动删除 BepInEx\\plugins\\SurvivalLog.ModCenter.dll"); Log("uninst 拒绝：面板自身"); return; }`。`BuildUninst` 可不改。

- [ ] **Step 4: 实机验证**

Build + 部署。
Expected：侧栏无"Mod 管理中心"行；详情页无卸载入口；直接发 `MC_UNINST`（探针/控制台）被拒；有新版本时头部出现更新入口，点击可完成自更新。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat：面板自身移出 mod 列表并禁止面板内卸载，更新入口迁到头部版本区"
```

---

### Task 11: 版本台账读取

**Files:**
- Create: `SurvivalLog.ModCenter/VersionCatalog.cs`（≤150 行）
- Modify: `SurvivalLog.ModCenter/RepoClient.cs`
- Test: 编译

**Interfaces:**
- Consumes: `RepoClient.AssetUrlOf`
- Produces:
  - `public sealed class VersionInfo { string Version, Date, Notes, Asset, Md5, Gamekb; }`
  - `public static async Task<List<VersionInfo>> RepoClient.FetchVersions(string repoKey)`（COS `mods/<repo>/versions.json`，失败 null）
  - `internal static class VersionCatalog { static bool TryGet(string repoKey, out List<VersionInfo> list); static void Put(string repoKey, List<VersionInfo> list); static RepoClient.UpdateInfo ToUpdateInfo(string repoKey, VersionInfo v); }`

- [ ] **Step 1: `FetchVersions`**

URL `CosBase + "/mods/" + repoKey + "/versions.json"`；`GetString(..., api:false)`；`JsonDocument` 读 `versions` 数组；每行要求 `version/asset/md5` 非空，`date/notes/gamekb` 可空；返回按数组原顺序（发布侧已最新在前）。异常/结构坏返回 null，不抛。

- [ ] **Step 2: `VersionCatalog`**

会话内 `Dictionary<string, List<VersionInfo>>`（OrdinalIgnoreCase，主线程唯一读写）；`ToUpdateInfo`：`AssetUrl = RepoClient.AssetUrlOf(repoKey, v.Asset)`，其余字段原样。

- [ ] **Step 3: 编译**

Run build，Expected 0 error。

- [ ] **Step 4: Commit**

```bash
git commit -m "feat：新增版本台账读取与版本目录缓存"
```

---

### Task 12: 版本选择事件与下载链

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Modify: `SurvivalLog.ModCenter/Payload.cs`
- Test: 实机 + 只读检查

**Interfaces:**
- Consumes: Task 11 的 `VersionCatalog`
- Produces:
  - 面板→C#：`MC_VER_GET {repoKey}`；`MC_UPD_VER {repoKey, version}`
  - C#→面板：`MC_VER_DATA {repoKey, current, versions:[{v,date,notes}]}`
  - `StartRepoChainJobs(List<(RepoRow row, RepoClient.UpdateInfo info)>)`
  - `RepoBlocked(RepoRow r, HashSet<string> pend, out string why)`（只查待生效/链占用）

- [ ] **Step 1: 拆分可动作判定**

从 `RepoActionable` 抽出 `RepoBlocked`（`pending`/`_repoChain` 检查），`RepoActionable` = `RepoBlocked` + `Remote`/`_repoInfos`/方向检查。行为保持不变。

- [ ] **Step 2: 下载链接受显式 job**

`StartRepoChain(List<RepoRow> targets)` 保留（内部组 jobs 后调用）；新增 `StartRepoChainJobs(jobs)` 承担原 `_repoChain`/`_chainGuids`/`Log`/`RunRepoChainAsync` 逻辑。版本路径直接调用新方法。

- [ ] **Step 3: 事件处理**

`HandleOutbox` 增加：
- `case "MC_VER_GET"`：取 `repoKey`；`VersionCatalog.TryGet` 命中直接回；未命中后台 `RepoClient.FetchVersions`，成功后 `VersionCatalog.Put`，经 `RepoQueue` 主线程回 `MC_VER_DATA`（`current` 从 `_repoRows` 该仓 `Local` 取）。
- `case "MC_UPD_VER"`：取 `repoKey/version`；`VersionCatalog.TryGet` 找行（未命中先同步拒绝"版本列表过期，重新打开"）；取 `_repoRows` 行；`RepoBlocked` 通过后 `StartRepoChainJobs(new(){ (row, VersionCatalog.ToUpdateInfo(repoKey, v)) })`；`_chainGuids.Add(row.Guid)` 沿用。
- `Payload.cs` 新增 `VerListData { RepoKey, Current, List<VerRowInfo> Versions }` 与手写 `ToJson()`；`VerRowInfo { V, Date, Notes }`。

- [ ] **Step 4: 编译 + 只读验证**

Build；用临时 PowerShell 读桶内 `mods/CabinetEverywhere/versions.json`，确认最新版 asset 在桶内（`coscli ls` 命中）。
Expected：构建通过；台账行 asset 可在桶内找到。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat：版本选择事件与显式版本下载链（COS 唯一来源）"
```

---

### Task 13: 版本选择面板

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`
- Test: 实机

**Interfaces:**
- Consumes: `MC_VER_GET` / `MC_VER_DATA` / `MC_UPD_VER`
- Produces: 详情页"版本"按钮 + 版本弹窗；未安装 mod 安装弹窗内"选择版本"入口

- [ ] **Step 1: 版本弹窗**

新增 `openVerModal(m)`：发送 `MC_VER_GET {repoKey: repoRowOfGuid(m.guid).repoKey}`，占位"读取版本列表…"；`MC_VER_DATA` 到达后渲染列表（版本号、日期、说明摘要、当前版本标"使用中"）。每行点击"安装此版本"→ `MC_UPD_VER`；低于当前版本的条目二次确认文案："旧版本可能不适配当前游戏版本，存档由新版写过，建议先备份存档"；选 ModCenter 自身时不允许（面板不提供该入口）。

- [ ] **Step 2: 入口**

已装 mod 详情页维护区（更新/卸载按钮旁）加"版本"按钮，仅 `!isTpMod(m)` 且 `repoRowOfGuid(m.guid)` 存在时显示。未安装行安装确认弹窗 `openRepoModal` 内加"选择版本"链接 → `openVerModal(m)`（安装仍走 `MC_UPD_VER`，`row.Local == null` 即安装）。

- [ ] **Step 3: 实机验证**

Build + 部署。
Expected：
- 已装 CabinetEverywhere 打开版本列表可见台账版本；选旧版 → 确认 → 下载 → pending → 重启后旧版生效。
- 选同版本可重装。
- 未安装成员可从安装弹窗选版本安装。
- 全程只请求 COS；断网时提示版本列表拉取失败。

- [ ] **Step 4: Commit**

```bash
git commit -m "feat：面板版本选择与降级确认（仅名单内 mod）"
```

---

### Task 14: `ErrorLog` 监听与缓冲

**Files:**
- Create: `SurvivalLog.ModCenter/ErrorLog.cs`（≤300 行）
- Modify: `SurvivalLog.ModCenter/ModCenterPlugin.cs`
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Test: 实机

**Interfaces:**
- Consumes: `BepInEx.Logging.ILogListener`（`LogLevelFilter`、`LogEvent(object, LogEventArgs)`）、`IL2CPPChainloader.Instance.Plugins`
- Produces:
  - `internal sealed class ErrRecord { DateTime At; string Level, Source, Message, Stack; int Repeat; }`
  - `internal static class ErrorLog`
    - `static void Register()`（`Logger.Listeners.Add(new Listener())`）
    - `static void Drain()`（主线程）
    - `static bool HasErrors(string guid)`
    - `static IReadOnlyList<ErrRecord> ForGuid(string guid)`
    - `static IEnumerable<string> Keys`（含 `_unattributed`）
    - `static string Unattributed = "_unattributed"`
  - `ErrorLogListener : ILogListener { public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal; public void LogEvent(object sender, LogEventArgs e) }`

- [ ] **Step 1: 监听与入队**

`LogEvent` 只在任意线程拷贝 `e.Level`、`e.Source?.SourceName`、`e.Data?.ToString()`（异常对象 ToString 含堆栈）到 `ConcurrentQueue<ErrRecord>`；不访问 Chainloader/Unity。`LogLevelFilter` 只收 Error/Fatal。

- [ ] **Step 2: 主线程 Drain 与归属**

`Drain()` 逐个出队：用 `SourceName` 匹配 `IL2CPPChainloader.Instance.Plugins` 中 `Metadata.Name` 或 `Metadata.GUID`（OrdinalIgnoreCase）→ GUID；匹配不到 → `_unattributed`。每 key 维护 `List<ErrRecord>`（最新在前），新记录与 `[0]` 同 `Source+Message` 时 `Repeat++`，否则 `Insert(0)`；`Count > 50` 时 `RemoveRange(50, Count-50)`。`ConfigBridge.Pump` 每拍调用 `ErrorLog.Drain()`。

- [ ] **Step 3: 注册**

`ModCenterPlugin.Load` 在 `SuiteRoster.LoadCache()` 之后调用 `ErrorLog.Register()`；用 try/catch 包住，失败只 `Log` warn（不得影响面板）。

- [ ] **Step 4: 实机验证**

Build + 部署；启动游戏后触发任意已装 mod 的 `LogError`（可用现有会报错的第三方 mod，或临时在探针中打一条 Error 后移除）。
Expected：LogOutput 有 ModCenter 自身日志；面板 `HasErrors` 为真（Task 15 后可见红标）；超出 50 条只留最新 50；重复消息 `Repeat>1`。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat：新增运行错误监听与每 mod 50 条内存缓冲"
```

---

### Task 15: 告警红组接入

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Modify: `SurvivalLog.ModCenter/Payload.cs`
- Test: 实机

**Interfaces:**
- Consumes: `ErrorLog.HasErrors/ForGuid`
- Produces:
  - `CfCardInfo.Err`（bool，仅 true 时输出 `"err":true`）
  - `CfIcsFor` 按红/橙组各出一枚图标
  - `HandleCf` 卡片顺序：红组 L1 → 运行异常 → 橙组 L1 → L2

- [ ] **Step 1: 图标按组产出**

重写 `CfIcsFor`：`bool hasRed = entries.Any(e => e.Level == "red") || ErrorLog.HasErrors(guid); bool hasOrange = entries.Any(e => e.Level == "orange") || PatchScan.IcsFor(guid) != null;`
- `hasRed` → 一枚 `IcInfo { K="red", Click="cf", Tip = ... }`。Tip：仅有错误 → `"运行报错，点击查看"`；仅有冲突 → 原 `EntryIc` 的 `TipShort`；两者都有 → `"严重告警：冲突与运行报错，点击查看"`。
- `hasOrange` → 一枚 `IcInfo { K="orange", Click="cf", Tip = ... }`。Tip 取第一条橙色 L1 的 `TipShort`，否则 L2 的 `Tip`。
- 顺序：红 → 橙（保持现有"冲突徽标置最前"）；更新黄标仍由 `RepoIcsOf` 追加在后。
- 停用行仍走 `WeakenCfIcs`。

- [ ] **Step 2: 弹窗卡片顺序**

`HandleCf`：`entries` 按 `Level` 拆成 `reds`/`oranges`；依次 `reds.Select(EntryCard)` → 若 `ErrorLog.HasErrors(guid)` 追加异常卡 → `oranges.Select(EntryCard)` → L2 `PatchCard`。异常卡字段：
```csharp
var recs = ErrorLog.ForGuid(guid);
var n = recs.Sum(r => 1 + r.Repeat); // 总发生次数（含被折叠的重复）
new CfCardInfo {
  Lv = "red", Tag = "运行异常", Err = true,
  Title = $"本次启动运行时报错 {n} 次，可能影响功能。",
  Reason = recs[0].Message,
  Tips = { "打开错误日志，复制详情反馈给作者", "重启后仍出现，先停用该 mod 排查" }
}
```
`CfCardInfo.Write` 在 `Err` 为 true 时追加 `,"err":true`（false 不输出，保持旧 payload 字节不变）。`HandleCf` 的日志行改为输出红/橙/异常卡数量。

- [ ] **Step 3: 编译 + 实机**

Build + 部署；触发一个仅有运行错误的 mod 和一个既有 L1 红冲突又有错误的 mod。
Expected：前者行上有一枚红标；后者行上仍只有一枚红标；点开弹窗红组内冲突卡在前、异常卡在后；仅有橙组告警的 mod 只有橙标；原有"去调序"按钮和冲突卡文案不变。

- [ ] **Step 4: Commit**

```bash
git commit -m "feat：运行异常接入告警红组与告警弹窗卡片"
```

---

### Task 16: 错误日志面板

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Modify: `SurvivalLog.ModCenter/Payload.cs`
- Test: 实机

**Interfaces:**
- Consumes: `ErrorLog`、`SuiteRoster`、`MC_CF_DATA.cards[].err`
- Produces:
  - `MC_ERR_GET` → `MC_ERR_DATA {rows:[{guid,name,ver,suite,count,last,entries:[{t,lv,src,msg,stack,rep}]}]}`（`suite` 为 true/false；未归属行 guid=`_unattributed`、name=`未归属`、suite=false）
  - 面板：头部"错误日志"入口；告警弹窗异常卡`查看错误日志`；左栏按套件/三方分组；右栏详情 + 右上角单一复制图标

- [ ] **Step 1: 服务端 payload**

`Payload.cs` 新增 `ErrData`/`ErrRowInfo`/`ErrEntryInfo` 与手写 `ToJson()`（字段名用短名：`t/lv/src/msg/stack/rep`）。`ConfigBridge.BuildErrData()`：遍历 `ErrorLog.Keys`，名称/版本从 `_repoRows`（优先）或 `SuiteRoster` 取；`suite` = `SuiteRoster.TryGetByGuid(guid, out _)`；`last` = 最新记录 `HH:mm:ss`；`count` = 记录条数（含重复折叠后的条数，不是 Repeat 总和）；`entries` 最多 50 条。
`HandleOutbox` 增加 `case "MC_ERR_GET"` → `PushEvent(BuildErrData().ToJson())`。

- [ ] **Step 2: 面板视图**

新增 `ST.errRows/ST.errSel` 与 `view='err'` 分支：头部左侧"← 返回"、标题"错误日志"；左栏按 `suite` 分组渲染（套件在前、三方在后；组标题仅在有内容时出现；"未归属"归三方；全空显示"暂无错误"）；右栏标题（名/版本/GUID）+ "仅保留最近 50 条" + 复制图标；条目时间/级别/来源/消息 + 可展开堆栈。
条目 `rep > 1` 时在消息末尾追加 `（重复 N 次）`；左栏条数 = 记录条数（折叠后），不是总发生次数。
入口：头部按钮 `data-act="errlog"`；`onCfData` 检测 `cards.some(c => c.err)` 时在弹窗按钮区加`查看错误日志`（`data-mact="errlog"`），点击关闭弹窗并打开错误面板定位该 mod。

- [ ] **Step 3: 复制**

JS `copyText(text)`：优先 `navigator.clipboard.writeText`，失败回退 `textarea + document.execCommand('copy')`；再失败弹出可选中的文本框。复制内容：
```
ModCenter v{self.ver} · 游戏 {gameVer} · {yyyy-MM-dd HH:mm:ss}
Mod: {name} {ver} ({guid})
---
[{t}] [{lv}] {src}
{msg}
{stack}
```
（逐条拼接，最多 50 条）。复制成功 toast "已复制到剪贴板"。

- [ ] **Step 4: 实机验证**

Build + 部署。
Expected：
- 头部"错误日志"打开面板；左栏套件/三方分组，`未归属`在三方组。
- 有错误的 mod 行红标点击 → 原告警列表末尾/红组内出现"运行异常"卡，`查看错误日志`进入面板并定位。
- 右上角复制图标复制全部当前错误（≤50），内容带环境头；无清空/批量按钮、无历史。
- 重启后面板为空；连续 60 条只显示 50 条。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat：新增错误日志面板（套件/三方分组、单复制按钮、本次启动 50 条上限）"
```

---

## 发布顺序（代码完成后）

- [ ] 1. 发新版 ModCenter（含 v2 解析、名单驱动、版本选择、错误面板）。
- [ ] 2. 跑一次 `publish-index.ps1 -Yes`（首次全量审计 + 重建 v2 `suite.json`，上传 COS 桶根）。
- [ ] 3. 此后各 mod 正常 `release.ps1`：`sync-cos.ps1` 自动 upsert 名单行、过滤台账。
- [ ] 4. 旧客户端（< v2.1.0）不再收到 GitHub index 更新，发版说明要求先升级 ModCenter。

## 自检记录

- 规格覆盖：名单单源化（T1-T8）、补偿（T4/T9）、在线安装/更新（T7/T12）、版本选择（T11-T13）、自身（T10）、错误日志与告警（T14-T16）、发布流程（T1-T4 + 发布顺序）。
- 类型一致性：`SuiteRoster.GuidOf/DisplayOf/DllOf`、`RepoRow.Dll`、`VersionCatalog.ToUpdateInfo`、`ErrorLog.ForGuid/HasErrors`、`CfCardInfo.Err` 在后续任务中被引用且签名一致。
- 无自动化测试：按用户规则不新增；验证为 Release 编译 + 实机场景 + 只读 PowerShell。
