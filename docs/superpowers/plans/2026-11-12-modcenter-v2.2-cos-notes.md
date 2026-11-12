# ModCenter v2.2.0 实施计划（notes 契约 + 版本台账 + gamekb 退役 + 待生效可视化）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 发版说明从各 mod CHANGELOG 自动流到玩家面板（更新弹窗/详情页），桶内建全史版本台账，退役 gamekb 无意义告警，待生效操作（含参数修改）可视可查明细。

**Architecture:** 数据流 `CHANGELOG → release.ps1 抽取 → update.json/suite.json 的 notes 字段 + 桶内 versions.json 台账 → 客户端 UpdateInfo.Notes → panel.js 展示`。gamekb 决策层删除（契约字段保留）；cfg 待生效用会话级字典跟踪（重启自然清空）。

**Tech Stack:** C# (net6.0, BepInEx IL2CPP)、panel.js（EmbeddedResource 内嵌 dll）、PowerShell 5.1 发版脚本、腾讯云 COS（coscli）、gh CLI。

**Spec:** `docs/superpowers/specs/2026-11-12-modcenter-v2.2-cos-notes-design.md`（含作者拍板口径，随计划一起读）

## Global Constraints

- 契约只加**可选**字段（update.json +notes、suite.json 行 +notes、MC_PENDING_SUMMARY +items）：老客户端/老面板读到新数据不炸，新端读旧数据（字段缺失）降级不显示。
- 说明文案零硬编码进 panel.js（USAGE 功能介绍例外，随 ModCenter 版本走）。
- PS5.1 纪律：`$ErrorActionPreference='Continue'` + `$LASTEXITCODE` 显式判定；JSON 一律 UTF-8 无 BOM 写出；gh/HTTP 响应 Byte[] 先 UTF-8 解码再 ConvertFrom-Json。
- notes 是多行中文 markdown——进 JSON 必须 `\n`/`\"`/`\\` 转义（C# 侧手写 JSON 序列化处尤甚，Payload.cs 现有 Escape 复用或补齐）。
- commit 格式 `<type>：<描述>`（技术名词保留英文）；套件根非 git 仓（脚本改动无 commit 项），各 mod 仓独立 commit。
- 行数纪律：新增/修改模块 ≤500 行（存量 ConfigBridge.cs/panel.js 已超限，只提示不拆）。
- build：`E:\Game\Mod\survivallog-mods\sdk\dotnet\dotnet.exe`（`DOTNET_MULTILEVEL_LOOKUP=0`），0 错误为过（存量 Meta.cs 3 个 CS0649 警告忽略）。
- 本仓无单测框架：C# 验证 = 定向 build + 探针；脚本验证 = 对真桶幂等执行 + curl 匿名读回。

## Review Focus

1. **notes JSON 注入/转义**：notes 含 `"` `\n` `\` 时，Payload 手写 JSON 与 PS ConvertTo-Json 都不炸、面板拿到原文 → Task 8（Escape 断言步骤）、Task 2（update.json 写出后 ConvertFrom-Json 回读步骤）覆盖。
2. **拉旧台账非 404 失败静默重置**：网络错被当首版起建，全史台账清空 → Task 2（失败语义步骤：非 404 → throw）覆盖。
3. **旧数据无 notes 字段**：suite 行/update.json 缺 notes → Notes=null，弹窗/区块不显示不炸 → Task 6（解析容错）+ Task 10（渲染判空）覆盖。
4. **更新弹窗零勾选**：全取消后「更新所选」禁用，不发消息 → Task 10 覆盖。
5. **cfg 项与卸载 op 并存重复计数**：mod 的 cfg 修改 + 卸载同时待生效 → items 两行（不同 kind）可接受，但 n（旧计数）不得把 cfg 计入 → Task 9 覆盖。

---

### Task 1: sync-cos.ps1 增加 versions.json 上传

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\suite\tools\sync-cos.ps1`

**Interfaces:**
- Produces: `-Versions <path>` 可选参数（与 `-Mod/-Zip/-UpdateJson` 组合，第三件上传物，目标 `cos://sl/mods/<Mod>/versions.json`）。

- [ ] **Step 1: 加参数与上传段**

param() 块加 `[string]$Versions`；`-Mod` 分支在 update.json 上传后：`if ($Versions) { & $coscli -c $cfg cp $Versions $dir; if ($LASTEXITCODE -ne 0) { throw ('coscli upload versions.json failed: ' + $Mod) } }`。

- [ ] **Step 2: 验证**

跑 `sync-cos.ps1 -Mod RobotSpeed -Zip <任一现有本地zip> -UpdateJson <临时update.json> -Versions <临时versions.json>`（临时文件手写最小 JSON），`coscli ls cos://sl -r` 确认三件在场；`rm` 清理桶内测试覆盖产生的多余对象（同名覆盖则无残留）。

### Task 2: release.ps1（ModCenter 模板）——notes 进 update.json + versions.json 台账

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\ModCenter\release.ps1`（`---- 5. update.json ----` 节与 gh release create 之间）

**Interfaces:**
- Consumes: Task 1 的 `-Versions` 参数。
- Produces: update.json 六字段（+notes）；`$versPath`（temp 下 versions.json，含全史+本版）。

- [ ] **Step 1: update.json 写 notes**

现有 `$uj = @{ mod=...; version=...; asset=...; md5=...; gamekb=... }` 加 `notes = $notes`（`$notes` 变量在原脚本 `---- 6 ----` 节抽取——把它**上移**到 update.json 生成之前；正则与现有相同）。

- [ ] **Step 2: 拉旧台账 + unshift 本版行（gh release create 之前）**

```
$versUrl = $cosBase + '/mods/' + $modName + '/versions.json'
$versPath = Join-Path $env:TEMP ('versions_' + $modName + '.json')
$rows2 = New-Object System.Collections.Generic.List[object]
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $versUrl
    $old = ([Text.Encoding]::UTF8.GetString($resp.Content) | ConvertFrom-Json).versions
    foreach ($r in @($old)) { $rows2.Add($r) }
} catch [System.Net.WebException] {
    if (-not ($resp -and $resp.StatusCode -eq 404)) { throw ('拉取旧版本台账失败（非404，防静默重置）: ' + $_.Exception.Message) }
    # 404 = 首版起建
}
$rows2.Insert(0, [ordered]@{ version=$ver; date=(Get-Date -Format 'yyyy-MM-dd'); notes=$notes; asset=(Split-Path $zip -Leaf); md5=$md5; gamekb=$gk.Name })
```

`$cosBase = 'https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com'` 定义在脚本头变量区。404 判定按 PS5.1 实际异常形态调整（WebException.Response.StatusCode 或 $resp 为 $null 且消息含 404），保持"仅 404 豁免、其余 throw"语义。
（注意：套件根 release.ps1 变量名与本仓一致：`$notes`/`$md5`/`$gk`/`$zip`/`$ver`/`$modName` 已存在。）

- [ ] **Step 3: 写出 + 上传**

`[IO.File]::WriteAllText($versPath, ([ordered]@{ generatedAt=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); mod=$modName; versions=$rows2 } | ConvertTo-Json -Depth 4), UTF8无BOM)`。COS 双发调用（`---- 7 ----` 现有 try 块）加 `-Versions $versPath`。

- [ ] **Step 4: 验证（-PackageOnly 不够——本任务产物在发版路径）**

语法检查 `powershell -NoProfile -Command "& { Get-Command -Syntax }"` 或直接 `powershell -NoProfile -File release.ps1 -PackageOnly`（走不到台账段，只验语法不炸）；台账段逻辑用**手动 dry-run**：临时 PS 会话执行 Step 2 代码块（$ver 用 2.1.0、$notes 取 CHANGELOG v2.1.0 节、$zip 用本地现有 zip）产出 versions.json，`ConvertFrom-Json` 回读确认结构 + notes 多行转义正确 + 首行为本版。

- [ ] **Step 5: Commit**

`git add release.ps1 && git commit -m "feat(ci)：发版说明进契约+版本台账——update.json 带 notes、发版前拉桶内 versions.json 追加本版行（非404失败中止防静默重置）"`

### Task 3: 9 仓 release.ps1 同步注入

**Files:**
- Modify: `CabinetEverywhere/EndlessGuard/FridgeChill/PlanPick/PreDisasterSpeed/QuickAction/RobotSpeed/SaveMgr/UnlockHub` 各自 `release.ps1`（相对套件根）

**Interfaces:**
- Consumes: Task 2 的同一改动块。9 仓脚本尾部结构一致（已验证），但 update.json 生成段的变量名/抽取正则需逐仓核对（部分仓更老）。

- [ ] **Step 1: 逐仓 diff 核对 update.json 生成段与 gh release 段**

确认每仓都有 `$uj = @{...}` 五字段哈希与 `$notes` 抽取（或等价物）。若某仓无 `$notes` 抽取，按该仓 CHANGELOG 节正则补（同款）。

- [ ] **Step 2: 二进制安全注入（python -I 脚本文件，LF/CRLF 自适应，参照上次 sync-cos 注入模式）**

每仓：update.json 段插 `notes`、gh release 前插台账段、COS 调用加 `-Versions`。

- [ ] **Step 3: 验证**

9 仓各跑 `-PackageOnly`（语法闸）+ 抽 2 仓手动 dry-run 台账段（同 Task 2 Step 4）。

- [ ] **Step 4: Commit（每仓一个）**

`feat(ci)：发版说明进契约+版本台账（同 ModCenter 模板）`

### Task 4: publish-index.ps1 聚合带 notes

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\publish-index.ps1`（聚合循环 `$rows.Add` 处）

**Interfaces:**
- Produces: suite.json 行六字段（+notes）。

- [ ] **Step 1: 行加 notes**

`$rows.Add([ordered]@{ ...; assetUrl=...; notes = $u.notes })`（$u 来自 update.json 解析，老 update.json 无 notes → $null → ConvertTo-Json 输出 null，客户端 GetStr 容错）。

- [ ] **Step 2: 验证 + Commit**

语法过 + 下一次真实 publish（Task 5 Step 3 会跑）时回读桶根 suite.json 确认行带 notes。commit：`feat(ci)：suite.json 聚合行带发版说明 notes`。（脚本在套件根非 git 仓——无 commit，此步仅记录。）

### Task 5: migrate-cos.ps1 全史台账回填 + 存量 notes 回填 + 执行

**Files:**
- Modify: `E:\Game\Mod\survivallog-mods\migrate-cos.ps1`

**Interfaces:**
- Consumes: gh api `repos/{org}/{mod}/releases`（全量，非 latest）；各 release 的 assets 里 update.json。
- Produces: 桶内 10 仓 `versions.json`（契约内全史）+ latest `update.json` 补 notes。

- [ ] **Step 1: 全史遍历**

替换现有"latest 单版"逻辑为：`gh api "repos/$org/$m/releases" --jq` 逐 release（含 draft=false prerelease=false 过滤）取 tag_name/created_at/assets。对每个 release：有 update.json 资产 → 下载解析五字段 + body（notes）→ 台账行（date 取 tag/created_at 的 yyyy-MM-dd）；无 update.json 资产 → 跳过该 release。版本序 = releases 列表序（GitHub 默认新→旧）。

- [ ] **Step 2: latest update.json 补 notes**

列表首个（latest）的 body 写进桶内该仓 update.json 的 notes 字段（读现有桶内文件 → 补字段 → 重传）。

- [ ] **Step 3: 执行 + 验收**

`migrate-cos.ps1 -Yes` → `publish-index.ps1 -Yes` → curl 匿名验证：桶根 suite.json 10 行全带 notes 非空；抽 3 仓 versions.json 首行 = 当前 latest、行数 ≥2（有历史的仓）、notes 非空；update.json notes 非空。

### Task 6: RepoClient——UpdateInfo.Notes

**Files:**
- Modify: `SurvivalLog.ModCenter/RepoClient.cs`（UpdateInfo 类、TryFetchCosJson、ParseSuiteJson）

**Interfaces:**
- Produces: `UpdateInfo.Notes`（string，缺失 null）。suite 行与单仓 update.json 的 `notes` 字段都解析进去。

- [ ] **Step 1: 实现**

UpdateInfo 加 `public string Notes;`；TryFetchCosJson 的 info 初始化加 `Notes = GetStr(u, "notes")`（**不**参与五字段完整性校验——可选字段）；ParseSuiteJson 行解析加 `Notes = GetStr(m, "notes")`（同样不参与丢行判定）。

- [ ] **Step 2: build + Commit**

`dotnet build SurvivalLog.ModCenter -c Release` → 0 错误。`feat(mc)：UpdateInfo 增加 Notes 发版说明字段（可选，契约向后兼容）`

### Task 7: RepoDecision——gamekb 决策退役

**Files:**
- Modify: `SurvivalLog.ModCenter/RepoDecision.cs`（Build 的 gamekb 门段、RepoActionable）、`ConfigBridge.cs`（RepoSelfTest 探针断言，若涉五态）

**Interfaces:**
- Produces: 决策只有 Install/Update/Ok 三态；`RepoRow.Kbw/KbWhy/VerNote` 字段保留恒空（协议不删）；`RepoActionable` 无同版本放行（tag 不大于本地一律拒）。

- [ ] **Step 1: Build() 删 gamekb 门段**

删除 `if (!string.IsNullOrEmpty(row.Gamekb) && ...)` 整块（Warn 覆盖、Kbw 标记、VerNote 未校验注记）。字段 Kbw/KbWhy/VerNote 声明保留，头注标注"v2.2.0 退役恒空，协议字段防新旧互炸"。

- [ ] **Step 2: RepoActionable 删同版本放行**

现逻辑已拒 tag≤本地（CompareVersions>0 拒）——确认无 Kbw 特例残留即可（v4.1 注释里"同版本放行仅覆盖 ⚠橙 行"路径若已在条件外则仅删注释/死分支）。

- [ ] **Step 3: 探针断言改三态 + Kbw 清零断言**

RepoSelfTest 相关段：期望态集合去掉 Warn；加断言"任意行 Kbw==false"。

- [ ] **Step 4: build + Commit**

0 错误。`feat(mc)：gamekb 决策门退役——三态收敛（Install/Update/Ok），同版本更新路径删除，契约字段保留恒空`

### Task 8: ConfigBridge/Payload——MC_UPDATES_READY + MC_UPD_SELECTED + RepoData notes

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（检查完成收割点 Kind 1、消息分发、HandleRepoUpd）、`SurvivalLog.ModCenter/Payload.cs`（RepoData 行序列化 + JSON 转义）

**Interfaces:**
- Produces:
  - 事件 `MC_UPDATES_READY {type, rows:[{repoKey,name,local,remote,notes}]}`（仅 Update 态且不在 RepoPendingGuids；预检轮不推——收割点区分来源）。
  - 消息 `MC_UPD_SELECTED {repos:[string]}`（逐仓走 RepoActionable 校验 + 现有 `_repoChain` 串行链；任一不可动作仓跳过并 toast，不中断整批）。
  - `MC_REPO_DATA` 行 JSON + `notes` 字段（Escape 处理 `"` `\` `\n`）。

- [ ] **Step 1: Payload 行序列化加 notes**

RepoData 行 Append 处加 `,"notes":"..."`（经现有/新增 Escape：`\` → `\\`、`"` → `\"`、换行 → `\n`）。先写 Escape 的确定性验证：临时探针或 debug 输出对 `a"b\c` + 换行跑一遍断言输出 `a\"b\\c\n`。

- [ ] **Step 2: MC_UPDATES_READY**

检查轮收割（Kind 1 处理段）：收集 rows 中 Update 态、无 pending、Remote>Local 的行 → 非空则 PushJs（面板关闭时丢弃可接受——再入口=60s 冷却重查）。**仅手动轮推**：`StartRepoFetch` 加参数 `bool manual`（预检调用传 false）。

- [ ] **Step 3: MC_UPD_SELECTED**

消息分发加 case；实现复用 HandleRepoUpd 的目标收集循环（foreach repos → RepoActionable → jobs 列表）→ 同款 `_repoChain` 启动。不可动作仓 McToast 跳过原因。

- [ ] **Step 4: build + Commit**

0 错误。`feat(mc)：检查完成推 MC_UPDATES_READY 可更新列表 + MC_UPD_SELECTED 批量更新链 + RepoData 行带 notes`

### Task 9: cfg 会话跟踪 + MC_PENDING_SUMMARY items + toast 分组

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（写路径、PushPendingSummary、各 toast 点）

**Interfaces:**
- Produces:
  - `private static readonly Dictionary<string, string> _cfgTouched`（key=`guid:cfgKey`，value=badge）。写路径记录点：参数落盘路径（FlushDirty 调用侧的写键处，~行 1848/1914 上下文）+ 启停切换处（~行 257 上下文）；badge=now 不记。
  - `MC_PENDING_SUMMARY` JSON 新增 `"items":[{guid,mod,kind,label,badge}]`（kind: update|install|uninstall|order|cfg）。**旧 n/labels 不变**（不含 cfg）。
  - toast：参数写入按 badge（reload→「⏳ 读档后生效」restart→「⏳ 重启游戏后生效」）；更新/安装/卸载/排序保存 toast 加「⏳ 重启游戏后生效」；撤销 toast 改口「已撤销更新/卸载/排序操作（参数修改不回滚）」。

- [ ] **Step 1: _cfgTouched 记录**

定位面板参数写键的统一落点（MC_CF 消息处理/FlushDirty 前的键写入处）与启停切换点，写 `_cfgTouched[guid+":"+key] = badge`（now 跳过）。写后调 `PushPendingSummary()`（摘要即时更新）。

- [ ] **Step 2: items 组装**

PushPendingSummary：现有分组逻辑产出 pending 项（kind 按 op 类型）+ `_cfgTouched` 按 guid 聚合（label="N 项参数待重载/重启"，badge 取组内最高档 restart>reload）。**注意去重**：guid 同时有 pending op 和 cfg 项 → 两行都保留（kind 不同），n 不计 cfg。

- [ ] **Step 3: toast 三处改口**

按 Interfaces 逐点改（更新链完成 toast、启停/参数 toast、撤销 toast）。

- [ ] **Step 4: build + Commit**

0 错误。`feat(mc)：待生效可视化——cfg 参数修改纳入会话级跟踪、MC_PENDING_SUMMARY 带 items 明细、生效时机 toast 分组改口`

### Task 10: panel.js——更新列表弹窗 + 安装确认 + 详情更新内容 + Kbw 渲染删

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`

**Interfaces:**
- Consumes: `MC_UPDATES_READY`（rows[].notes 可空）、`MC_REPO_DATA` 行 notes、`MC_UPD_SELECTED`、`MC_INST {repoKey}`、现有 USAGE 表与 usageModalHtml 模式（1424 行附近）。

- [ ] **Step 1: MC_UPDATES_READY 处理 + 更新列表弹窗**

__mcEvent 分发加 case → 存 `updReady`（跨面板拆建保留，仿 pend 缓存模式）→ 开 modal（复用 __mc-modal 骨架）：行=勾选框（默认全选）+ mod 名 + `v{local} → v{remote}` + 展开区 notes（按 `\n` 拆 <p> 原文显示，null 则显示「无说明」）；底部「更新所选 (n)」按钮——勾选数 0 时 disabled。点击 → `send('MC_UPD_SELECTED', {repos:[勾选]})` → 关弹窗。

- [ ] **Step 2: 未安装行 → 安装确认弹窗**

仓库行点击路由处：row 未安装（local 空）→ 不进详情页，开安装确认 modal：USAGE[guid] 一句话简介 + 怎么用前 3 条 + 「安装」「取消」→ 安装发 `MC_INST {repoKey}`。USAGE 缺该 guid → 显示 ReleaseREADME 式一句话兜底文案（静态通用：「来自生存日志套件社区的 mod，安装后可在配置页调整」）——不硬编码具体 mod 说明。

- [ ] **Step 3: 详情页「更新内容」区块**

已安装详情页版本区下：Remote>Local 且 notes 非空 → 区块标题「更新内容」+ notes 按行渲染；否则不渲染。

- [ ] **Step 4: Kbw/VerNote 渲染删**

删 ⚠橙 分支与 VerNote 行显示（数据恒空；防御性保留判断不炸）。

- [ ] **Step 5: build + Commit**

build（panel.js 内嵌资源随编译入 dll）0 错误 + 语法快查（node --check panel.js 若环境有 node，否则 build 即验）。`feat(mc)：面板——可用更新勾选弹窗/未安装安装确认(USAGE复用)/详情页更新内容区块/gamekb 橙标渲染退役`

### Task 11: panel.js——待生效明细弹窗 + 底条可点

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`

**Interfaces:**
- Consumes: `MC_PENDING_SUMMARY` 新 items（可空——旧 dll 无此字段时降级只显示计数）。

- [ ] **Step 1: 底条可点 + 明细弹窗**

底条（__mc-pending）整条 click → modal：items 按 mod 分组渲染（mod 名 + 各行 label + kind 徽标 + badge 提示「读档后生效/重启后生效」）；items 空且 n>0（旧数据）→ 渲染 labels 现状。cfg 行不渲染撤销语义。底条现有「撤销/重启」两钮行为不变。

- [ ] **Step 2: build + Commit**

0 错误。`feat(mc)：待生效底条可点开明细弹窗（items 分组渲染，旧协议降级计数）`

### Task 12: v2.2.0 发版

**Files:**
- Modify: `SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj`（Version 2.2.0）、`ModCenterPlugin.cs`（Version const）、`CHANGELOG.md`（v2.2.0 节，按 skill 格式要求 C：标题+新增/修复/优化+升级注意）

**Interfaces:** 无代码接口；流程接口 = release.ps1（自动带 notes + 台账）+ publish-index.ps1。

- [ ] **Step 1: 版本三件套**

csproj `<Version>2.2.0</Version>`、Plugin const 同步、CHANGELOG v2.2.0 节（内容归纳自 Task 6-11 的玩家可感知变化）。

- [ ] **Step 2: 发版链**

`release.ps1 -Yes` → 确认输出含「cos ModCenter uploaded」→ `publish-index.ps1 -Yes` → curl 桶根 suite.json：ModCenter 行 version=2.2.0、notes 非空；桶内 `mods/ModCenter/versions.json` 首行 2.2.0、次行 2.1.0（台账追加成功验证）。

- [ ] **Step 3: Commit + push**

`git add -A && git commit -m "feat(mc)：v2.2.0 发版——更新说明可见化+版本台账+gamekb 退役+待生效明细" && git push`

### Task 13: skill 固化更新

**Files:**
- Modify: `C:\Users\wangk\.agents\skills\survivallog-release\SKILL.md`

**Interfaces:** 无。

- [ ] **Step 1: 更新 skill**

A 节第 3 步后补：CHANGELOG 是发版说明唯一源头（notes 自动进 update.json/suite.json/versions.json，勿手改桶内文件）；桶内 `mods/<mod>/versions.json` 为自动维护台账；§5.4 存量回填口径（新仓直接走 release.ps1，无需 migrate）。

- [ ] **Step 2: 自查**

通读 skill 全文一致性（无过时表述残留）。
