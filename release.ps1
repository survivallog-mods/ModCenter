# ============ Per-mod release pipeline (self-contained) ============
# Usage: .\release.ps1 [-Yes] [-PackageOnly]
#   -PackageOnly  build+package+update.json only (suite publish uses this); no gate/tag/release
# 版本权威：插件 csproj <Version>（作者定号），Plugin.cs 的 Version const 必须同字面。
# 版本线只有一条（T16 单 dll 化）：清理逻辑并入 ModCenter.dll 的 CleanupRunner；patchers\ 只留
# **永久冻结**的引导壳 MCShell.dll（csproj <Version> 固定 1.0.0 仅为 [PatcherPluginInfo] 占位，
# 不是独立版本线；只有壳接口签名变更才需安装器更新）。
# v2 打包布局（T16）：
#   BepInEx\plugins\SurvivalLog.ModCenter.dll
#   BepInEx\patchers\MCShell.dll
#   README.md
# 旧版 MCCleaner.dll 不在包内——升级由安装器 [InstallDelete] 预删（游戏内更新链不碰 patchers）；
# 手动升级的玩家需自行删除 BepInEx\patchers\MCCleaner.dll（留着会与新清理逻辑重复执行改名/排序）。
# update.json 仍为五字段契约（mod/version/asset/md5/gamekb）。
param([switch]$Yes, [switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
$repoDir = $PSScriptRoot
$root    = Split-Path $PSScriptRoot -Parent
$modName = Split-Path $repoDir -Leaf
$proj    = Join-Path $repoDir ('SurvivalLog.' + $modName)
$shellProj = Join-Path $repoDir 'MCShell\MCShell.csproj'
$game    = 'E:\SteamLibrary\steamapps\common\Survival Log'
$dotnet  = Join-Path $root 'sdk\dotnet\dotnet.exe'
$ghExe   = Join-Path $root 'suite\tools\gh\bin\gh.exe'
$org     = 'survivallog-mods'

# ---- 1. version authority: csproj (only ModCenter) ----
$csproj = Join-Path $proj ('SurvivalLog.' + $modName + '.csproj')
$t = [IO.File]::ReadAllText($csproj)
$ver = [regex]::Match($t, '<Version>([\d.]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'cannot read <Version> from csproj' }
$plug = Get-ChildItem $proj -Filter '*Plugin.cs' | Select-Object -First 1
if ($plug -and (([IO.File]::ReadAllText($plug.FullName)) -notmatch ('public const string Version\s*=\s*"' + [regex]::Escape($ver) + '"'))) {
    throw ($plug.Name + ' Version const != csproj ' + $ver)
}

# ---- 2. gates (release mode only) ----
# CHANGELOG.md lives inside the project dir in this mirror-repo layout (synced from mod_stack)
$logPath = Join-Path $proj 'CHANGELOG.md'
$logTxt = [IO.File]::ReadAllText($logPath)
if (-not $PackageOnly) {
    # 版本号红线（硬规则 1）：占位节未改成作者定号前拒绝真发版（防误命中旧版本小节）
    if ($logTxt -match '##\s*v<') { throw 'CHANGELOG.md still has the placeholder section - author must set the version first' }
    if ($logTxt -notmatch (('##\s*v' + [regex]::Escape($ver)) + '\b')) { throw ('CHANGELOG.md has no v' + $ver + ' section') }
    $dirty = @(git -C $repoDir status --porcelain)
    $blocking = @($dirty | Where-Object { $_ -notmatch 'CHANGELOG\.md\s*$' })
    if ($blocking.Count -gt 0) { $blocking | ForEach-Object { Write-Host ('  ' + $_) }; throw 'uncommitted changes - commit or stash first (CHANGELOG.md allowed)' }
    if (-not $Yes) { $a = Read-Host ('Release ' + $modName + ' v' + $ver + ' ? [Y/N]'); if ($a -notmatch '^[Yy]') { throw 'cancelled' } }
}

# ---- 3. build (plugin + frozen shell; both exit codes judged) ----
$env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build $proj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw ('build failed: ' + $modName) }
& $dotnet build $shellProj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'build failed: MCShell' }
$dll  = Join-Path $proj ('bin\Release\SurvivalLog.' + $modName + '.dll')
$sdll = Join-Path $repoDir 'MCShell\bin\Release\MCShell.dll'
if (-not (Test-Path $dll))  { throw ('dll missing: ' + $dll) }
if (-not (Test-Path $sdll)) { throw ('shell dll missing: ' + $sdll) }
Write-Host ('shell: MCShell (frozen 1.0.0) -> BepInEx\patchers\MCShell.dll')

# ---- 4. package (v2 layout: plugins dll + patchers shell + README) ----
$pkg = Join-Path $env:TEMP ('slpkg_' + $modName + '_v' + $ver)
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item (Join-Path $pkg 'BepInEx\plugins')  -ItemType Directory -Force | Out-Null
New-Item (Join-Path $pkg 'BepInEx\patchers') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $pkg 'BepInEx\config')   -ItemType Directory -Force | Out-Null
Copy-Item $dll  (Join-Path $pkg 'BepInEx\plugins')  -Force
Copy-Item $sdll (Join-Path $pkg 'BepInEx\patchers') -Force
# no preset cfg (first run generates defaults)
Copy-Item (Join-Path $repoDir 'ReleaseREADME.md') (Join-Path $pkg 'README.md') -Force
$relDir = Join-Path $repoDir 'release'
New-Item $relDir -ItemType Directory -Force | Out-Null
$zip = Join-Path $relDir ('SurvivalLog.' + $modName + '_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal

# zip 布局断言：三件套逐项在场 + dll 恰两枚（插件 + 壳；旧 MCCleaner.dll 不得混入）
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
$z = [IO.Compression.ZipFile]::OpenRead($zip)
try { $names = @($z.Entries | ForEach-Object { $_.FullName.Replace('\', '/').TrimStart('/') }) }
finally { $z.Dispose() }
$expect = @(
    ('BepInEx/plugins/SurvivalLog.' + $modName + '.dll'),
    'BepInEx/patchers/MCShell.dll',
    'README.md')
foreach ($need in $expect) {
    if ($names -notcontains $need) { throw ('zip layout missing: ' + $need + ' (have: ' + ($names -join ', ') + ')') }
}
$zipDlls = @($names | Where-Object { $_ -match '\.dll$' })
if ($zipDlls.Count -ne 2) { throw ('zip layout: expected exactly 2 dll (plugin + shell), got ' + $zipDlls.Count + ': ' + ($zipDlls -join ', ')) }

# ---- 5. update.json (contract: mod/version/asset/md5/gamekb/notes) + versions.json 台账 ----
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$gk = Get-ChildItem (Join-Path $root 'gamekb') -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d+$' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $gk) { throw 'no gamekb snapshot under <root>\gamekb' }
# v2.2.0：CHANGELOG 本版节 = 发版说明唯一源头——同一份进 update.json.notes、GitHub release
# body、versions.json 台账行（下方）。抽取原 ---- 6 ---- 的正则上移至此（6 节继续用 $notes）。
$notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n(---|#{1,2}\s)|\z)')).Value.Trim()
$uj = @{ mod = $modName; version = $ver; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name; notes = $notes } | ConvertTo-Json
$updatePath = Join-Path $env:TEMP 'update.json'
[IO.File]::WriteAllText($updatePath, $uj, (New-Object Text.UTF8Encoding($false)))
Write-Host $uj
if ($PackageOnly) { Write-Host ('package-only done: ' + $zip); exit 0 }

# v2.2.0 版本台账：gh release create **之前**拉桶内旧 versions.json → 顶部 unshift 本版行。
# 失败语义：HTTP 404 = 首版起建；其余失败（网络/5xx）throw 中止发版——一次网络抖动静默把
# 全史台账重置成单行是本文件最贵的坏法，此时 GitHub release 未发、无半态。
# 同版本行先剔除（删 release 补发场景防双行）。
$cosBase = 'https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com'
$versPath = Join-Path $env:TEMP ('versions_' + $modName + '.json')
$vrows = New-Object System.Collections.Generic.List[object]
try {
    $vr = Invoke-WebRequest -UseBasicParsing -Uri ($cosBase + '/mods/' + $modName + '/versions.json')
    $vtxt = $vr.Content; if ($vtxt -is [byte[]]) { $vtxt = [Text.Encoding]::UTF8.GetString($vtxt) }
    foreach ($r in @(($vtxt | ConvertFrom-Json).versions)) { if ($r.version -ne $ver) { $vrows.Add($r) } }
} catch {
    $code = 0
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $code = [int]$_.Exception.Response.StatusCode }
    if ($code -ne 404) { throw ('拉取桶内版本台账失败（非404，中止防静默重置）: ' + $_.Exception.Message) }
    # 404 = 首版起建
}
$vrows.Insert(0, [ordered]@{ version = $ver; date = (Get-Date -Format 'yyyy-MM-dd'); notes = $notes; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name })
[IO.File]::WriteAllText($versPath, ([ordered]@{ generatedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); mod = $modName; versions = $vrows } | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))

# ---- 6. release notes + tag + GitHub release ----
$notesFile = Join-Path $env:TEMP ('relnotes_' + $modName + '_v' + $ver + '.md')
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))
& $ghExe release create ('v' + $ver) --repo ($org + '/' + $modName) --title ('v' + $ver) --notes-file $notesFile $zip $updatePath
if ($LASTEXITCODE -ne 0) { throw ('gh release create failed: ' + $modName) }

# ---- 7. COS 主源双发（v2.1.0：zip + update.json → 桶内 mods/<mod>/，国内直连下载）----
# 失败不炸发版（GitHub 回退源已就绪，玩家暂见旧版），醒目提示补救；zip 先传、update.json 后传
# ——中途失败时桶内 update.json 仍旧版，版本与 zip 保持一致不出现 404 下载。
try {
    & (Join-Path $root 'suite\tools\sync-cos.ps1') -Mod $modName -Zip $zip -UpdateJson $updatePath -Versions $versPath
} catch {
    Write-Warning ('COS 上传失败（GitHub 已发版成功，玩家暂见旧版）：' + $_.Exception.Message)
    Write-Warning ('补救：suite\tools\sync-cos.ps1 -Mod ' + $modName + ' -Zip ' + $zip + ' -UpdateJson ' + $updatePath + ' -Versions ' + $versPath)
    Write-Warning ('补传后记得重跑套件根 publish-index.ps1 刷新聚合索引')
}
Write-Host ('=== ' + $modName + ' v' + $ver + ' released ===')
Write-Host 'Nexus Mods stays a separate author-confirmed step (docs/08). Not automated.'
