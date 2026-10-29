# ============ Per-mod release pipeline (self-contained) ============
# Usage: .\release.ps1 [-Yes] [-PackageOnly]
#   -PackageOnly  build+package+update.json only (suite publish uses this); no gate/tag/release
# 版本权威：插件 csproj <Version>（作者定号），Plugin.cs 的 Version const 必须同字面。
# 清理器 MCCleaner.csproj 走独立小版本线（Ruling 7），构建后核对程序集版本与 csproj 一致。
# v2 打包布局（Ruling 8 单槽基座级清理器；双槽已被 CLR 程序集身份合并证伪）：
#   BepInEx\plugins\SurvivalLog.ModCenter.dll
#   BepInEx\patchers\MCCleaner.dll
#   README.md
# update.json 仍为五字段契约（mod/version/asset/md5/gamekb），不因清理器多一个版本字段。
param([switch]$Yes, [switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
$repoDir = $PSScriptRoot
$root    = Split-Path $PSScriptRoot -Parent
$modName = Split-Path $repoDir -Leaf
$proj    = Join-Path $repoDir ('SurvivalLog.' + $modName)
$game    = 'E:\SteamLibrary\steamapps\common\Survival Log'
$dotnet  = Join-Path $root 'sdk\dotnet\dotnet.exe'
$ghExe   = Join-Path $root 'suite\tools\gh\bin\gh.exe'
$org     = 'survivallog-mods'

# ---- 1. version authority: csproj ----
$csproj = Join-Path $proj ('SurvivalLog.' + $modName + '.csproj')
$t = [IO.File]::ReadAllText($csproj)
$ver = [regex]::Match($t, '<Version>([\d.]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'cannot read <Version> from csproj' }
$plug = Get-ChildItem $proj -Filter '*Plugin.cs' | Select-Object -First 1
if ($plug -and (([IO.File]::ReadAllText($plug.FullName)) -notmatch ('public const string Version\s*=\s*"' + [regex]::Escape($ver) + '"'))) {
    throw ($plug.Name + ' Version const != csproj ' + $ver)
}
# 清理器独立版本线：只要求 csproj 可读 + 产物程序集版本一致（构建后校验，见 §3）
$cleanerProj = Join-Path $repoDir 'MCCleaner\MCCleaner.csproj'
$tc = [IO.File]::ReadAllText($cleanerProj)
$cleanerVer = [regex]::Match($tc, '<Version>([\d.]+)</Version>').Groups[1].Value
if (-not $cleanerVer) { throw 'cannot read <Version> from MCCleaner csproj' }

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

# ---- 3. build (plugin + cleaner; both exit codes judged) ----
$env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build $proj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw ('build failed: ' + $modName) }
& $dotnet build $cleanerProj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'build failed: MCCleaner' }
$dll  = Join-Path $proj ('bin\Release\SurvivalLog.' + $modName + '.dll')
$cdll = Join-Path $repoDir 'MCCleaner\bin\Release\MCCleaner.dll'
if (-not (Test-Path $dll))  { throw ('dll missing: ' + $dll) }
if (-not (Test-Path $cdll)) { throw ('cleaner dll missing: ' + $cdll) }
# 清理器版本一致性：csproj <Version> vs 产物 AssemblyName.Version（1.0.0 → 1.0.0.0，按实际段比较）
$asmVer = [Reflection.AssemblyName]::GetAssemblyName($cdll).Version
$avs = @(($asmVer.Major, $asmVer.Minor, $asmVer.Build) | Where-Object { $_ -ge 0 })
if (($avs -join '.') -ne $cleanerVer) { throw ('MCCleaner assembly version ' + ($avs -join '.') + ' != csproj ' + $cleanerVer) }
Write-Host ('cleaner: MCCleaner v' + $cleanerVer + ' -> BepInEx\patchers\MCCleaner.dll')

# ---- 4. package (v2 layout: plugins dll + patchers cleaner + README) ----
$pkg = Join-Path $env:TEMP ('slpkg_' + $modName + '_v' + $ver)
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item (Join-Path $pkg 'BepInEx\plugins')  -ItemType Directory -Force | Out-Null
New-Item (Join-Path $pkg 'BepInEx\patchers') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $pkg 'BepInEx\config')   -ItemType Directory -Force | Out-Null
Copy-Item $dll  (Join-Path $pkg 'BepInEx\plugins')  -Force
Copy-Item $cdll (Join-Path $pkg 'BepInEx\patchers') -Force
# no preset cfg (first run generates defaults)
Copy-Item (Join-Path $repoDir 'ReleaseREADME.md') (Join-Path $pkg 'README.md') -Force
$relDir = Join-Path $repoDir 'release'
New-Item $relDir -ItemType Directory -Force | Out-Null
$zip = Join-Path $relDir ('SurvivalLog.' + $modName + '_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal

# zip 布局断言：三件套逐项在场 + dll 恰两枚（防 deps.json/探针等杂项混入发版）
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
$z = [IO.Compression.ZipFile]::OpenRead($zip)
try { $names = @($z.Entries | ForEach-Object { $_.FullName.Replace('\', '/').TrimStart('/') }) }
finally { $z.Dispose() }
$expect = @(
    ('BepInEx/plugins/SurvivalLog.' + $modName + '.dll'),
    'BepInEx/patchers/MCCleaner.dll',
    'README.md')
foreach ($need in $expect) {
    if ($names -notcontains $need) { throw ('zip layout missing: ' + $need + ' (have: ' + ($names -join ', ') + ')') }
}
$zipDlls = @($names | Where-Object { $_ -match '\.dll$' })
if ($zipDlls.Count -ne 2) { throw ('zip layout: expected exactly 2 dll (plugin + cleaner), got ' + $zipDlls.Count + ': ' + ($zipDlls -join ', ')) }

# ---- 5. update.json (contract: mod/version/asset/md5/gamekb) ----
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$gk = Get-ChildItem (Join-Path $root 'gamekb') -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d+$' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $gk) { throw 'no gamekb snapshot under <root>\gamekb' }
$uj = @{ mod = $modName; version = $ver; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name } | ConvertTo-Json
$updatePath = Join-Path $env:TEMP 'update.json'
[IO.File]::WriteAllText($updatePath, $uj, (New-Object Text.UTF8Encoding($false)))
Write-Host $uj
if ($PackageOnly) { Write-Host ('package-only done: ' + $zip); exit 0 }

# ---- 6. release notes + tag + GitHub release ----
$notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n(---|#{1,2}\s)|\z)')).Value.Trim()
$notesFile = Join-Path $env:TEMP ('relnotes_' + $modName + '_v' + $ver + '.md')
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))
& $ghExe release create ('v' + $ver) --repo ($org + '/' + $modName) --title ('v' + $ver) --notes-file $notesFile $zip $updatePath
if ($LASTEXITCODE -ne 0) { throw ('gh release create failed: ' + $modName) }
Write-Host ('=== ' + $modName + ' v' + $ver + ' released ===')
Write-Host 'Nexus Mods stays a separate author-confirmed step (docs/08). Not automated.'
