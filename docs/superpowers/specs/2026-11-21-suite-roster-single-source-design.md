# ModCenter 套件名单单源化、在线安装与版本选择设计

日期：2026-11-21 · 状态：待作者批准

## 意图与动因

套件新增 mod（CodexHint、CraftQueue）发到 COS 后，玩家面板里看不到、装不了、也没有更新提示。根因是"套件成员"在客户端有 5 处写死的名单：`RepoDecision.RepoKeys`、`Meta.Roster`、`Meta.SuiteGuids`、`Meta.Domains`、`Meta.ExtraNames`，另有 `Meta.Mods` 临时表；任何新增/退役都要改 ModCenter 代码并重新发版，漏一处就出现"消失"（套件页不认、第三方页又被第一方前缀排除）。

同时面板自身还混在 mod 列表里，允许卸载面板本体；版本只能升不能降，历史版本台账（`versions.json`）已存在但客户端从未读取。

本设计把**套件成员身份的唯一真值源定为 COS 桶根 `suite.json`**：

> 名单里有的 mod = 套件 mod：进套件列表，支持在线安装、更新、任意版本切换。
> 名单里没有的 mod = 三方 mod：进第三方列表，照旧自动识别配置页。

发布侧由 `publish-index.ps1` 自动从桶内目录聚合名单，客户端零硬编码。

**成功判据**：新增套件 mod 只需发版（release.ps1 自动维护 suite.json 与版本台账），全体玩家面板自动出现可安装/可更新入口；ModCenter 代码零改动。退役 mod 从桶内删除后自动从名单消失。

## 目标 / 非目标

**目标**

- `suite.json`（COS 桶根）作为套件成员唯一真值源；客户端拉取后本地缓存
- 名单内 mod 全部支持：在线安装、更新检查、任意历史版本安装/降级
- 名单外 mod 一律进第三方列表（含 GUID 恰好是 `com.local.survivallog.*` 前缀的三方 mod）
- ModCenter 自身从 mod 列表移除、禁止面板内卸载，但保留在线更新（入口迁到面板头部）
- 各 mod 的配置页、使用说明、更新说明继续由各 mod 自己维护（`modcenter.json` / `update.json` / `versions.json`）
- 删除客户端全部套件成员静态名单

**非目标（反目标）**

- 不做第三方 mod 的在线安装/更新/版本选择——第三方仍走"手动装、面板只做管理与配置"
- 不改各 mod 的 `modcenter.json` 配置清单格式（mcSpec 2 不变）
- 不做增量更新、差分包、签名校验（沿用 HTTPS + zip MD5）
- 不允许未发布到 COS 的本地 mod 被识别为套件成员——"没发布"就是三方，这是名单制的必然语义

## 现状（关键事实）

- `suite.json` 已存在：`publish-index.ps1` 逐仓读 COS `mods/<仓>/update.json` 聚合成单文件，双发 COS 桶根与 GitHub `survivallog-mods/index` 仓 latest release
- 客户端 `RepoClient.FetchSuite` 已能拉取：COS 优先、GitHub 回退；行字段 `mod/version/asset/md5/gamekb/assetUrl/notes`
- 客户端启动 +10s 自动预检一轮（`ConfigBridge` 启动节拍），手动"检查更新"另有 60s 冷却
- 套件页枚举源 = `Meta.Mods` + `Meta.Roster`（`EnumeratePageMods`）；写路由 `Resolve` 同源
- 第三方页排除条件 `TpExempt`：GUID 前缀 `com.local.survivallog.` 一票排除 → 未登记的第一方 mod 两边都不进
- 各 mod 仓均有内嵌 `modcenter.json`（mcSpec 2，含 `id`/`display`/`summary`），ModCenter 自身无清单
- 各 mod 仓发版时写 COS `mods/<仓>/versions.json` 版本台账（version/date/notes/asset/md5/gamekb），客户端未消费
- 台账覆盖不全：ModCenter 16 条、CabinetEverywhere 15 条、RobotSpeed/PlanPick 2 条、CodexHint 1 条
- 桶内 `mods/` 实测 12 个目录；退役的 3 个仓已删除（HTTP 404），自动枚举不会复活
- `RepoActionable` 明确拒绝"本地版本不低于远端"，降级无入口

## 约束

- **`suite.json` 旧字段不改**：`mod/version/asset/md5/gamekb/assetUrl/notes` 原样保留，旧客户端行为不变；新增字段全部可选
- **Payload 契约只增不改**：快照新增字段缺省不输出，旧面板/旧夹具无感
- **GUID 必须显式**：套件身份按 `suite.json` 的 `guid` 精确匹配，不按前缀推断（前缀可被冒用，作者已否）
- **清单 `suite` 标识只做降级补偿**：名单可用时一律以名单为准；补偿只在"无名单可读"时决定已装 mod 的分组，不产生任何安装/更新入口
- 线程纪律：名单读取/快照构建在主线程；网络拉取沿用现有后台任务 + 主线程收割模式
- 下载链不变：单链路串行、zip MD5 强校验、解包暂存、pending 落盘、重启生效
- 待生效语义不变：一个 mod 同时最多一个 pending 操作
- AOT 零反射序列化纪律与手写 JSON 写入不变

## 方案选择（Approaches）

| 方案 | 要点 | 取舍 |
|---|---|---|
| **A. suite.json 单源 + 本地缓存（选定）** | 客户端拉 COS 桶根 `suite.json`，名单集合驱动套件/三方分流、仓库行、安装更新；本地缓存供离线归类 | 唯一一份名单，发布侧自动生成；离线用旧缓存，最坏情况是归类不准但不消失。缺点：名单新鲜度依赖 publish-index 是否跑 |
| B. GUID 前缀判定套件 | 无需任何名单数据 | 前缀可被任意三方冒用；作者明确否决。否 |
| C. 客户端内置静态名单 + 服务端只发版本 | 现状小改 | 新增 mod 仍要改客户端发版，正是要消灭的痛点。否 |
| D. 客户端逐仓拉 update.json 现场聚合 | 客户端自建名单 | N 次请求/轮、慢、易触发 GitHub 限流；与现有一轮聚合设计相悖。否 |

选定 A。名单新鲜度由发布流程自动保证：发版脚本在每次发版时 upsert 本 mod 的名单行与版本台账，无需人工跑聚合。

## 设计

### 1. 身份源：suite.json v2

COS 桶根 `https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com/suite.json`，**唯一来源**。不再回退 GitHub index 仓（该仓仅为旧客户端保留双发，新客户端不读）。

```json
{
  "schema": 2,
  "generatedAt": 1795018751955,
  "mods": [
    {
      "mod": "CabinetEverywhere",
      "guid": "com.local.survivallog.cabineteverywhere",
      "display": "更好的储物柜",
      "summary": "全容器作为制作/烹饪/酿造等取料源直达……",
      "dll": "plugins/SurvivalLog.CabinetEverywhere.dll",
      "version": "2.2.4",
      "asset": "SurvivalLog.CabinetEverywhere_v2.2.4.zip",
      "md5": "27a293eb309b73b56a95faeee1e24925",
      "gamekb": "2026-11-12_26615062",
      "assetUrl": "https://.../mods/CabinetEverywhere/SurvivalLog.CabinetEverywhere_v2.2.4.zip",
      "notes": "## v2.2.4……"
    }
  ]
}
```

| 字段 | 必填 | 缺省回退 | 用途 |
|---|---|---|---|
| `mod` | 是 | — | 仓键、路由主键 |
| `version`/`asset`/`md5`/`gamekb`/`assetUrl` | 是（现有契约） | 缺行则丢行 | 在线安装/更新 |
| `guid` | 否 | `com.local.survivallog.<mod 小写>` | 身份精确匹配（套件/三方分流、本机版本读取） |
| `display` | 否 | 仓名 | 列表/详情显示名 |
| `summary` | 否 | 空 | 未安装行简介 |
| `dll` | 否 | `plugins/SurvivalLog.<mod>.dll` | 安装/替换目标路径 |
| `notes` | 否 | 空 | 发版说明 |

旧客户端只读已存在的六个字段，新字段忽略即兼容。

### 2. 发布侧

**发版即维护（主路径）**：各 mod 的 `release.ps1` 结尾已经调用共享脚本 `suite/tools/sync-cos.ps1`（上传 zip / update.json / versions.json）。在共享脚本里追加两步，12 份 release.ps1 一行不改：

1. **对齐版本台账**：上传前用 `coscli ls cos://sl/mods/<仓>/` 列出桶内实际存在的 zip，把本次生成的 `versions.json` 过滤成"只保留桶里真有 zip 的版本 + 本次新版本"再上传。从此版本列表与 COS 实际文件恒等，客户端选到的版本一定能下载。
2. **upsert suite.json**：读 COS 桶根现有 suite.json（公开读），按 `mod` 替换/追加本行——身份字段（`guid`/`display`/`summary`/`dll`）从本机 `..\<仓>\SurvivalLog.<仓>\modcenter.json` 读（缺省回退命名约定/仓名），版本字段来自刚上传的 update.json；写回桶根，其他 mod 的行原样保留。

失败语义：suite.json upsert 失败不炸发版（zip/update.json 已就绪），打醒目警告并给出补救命令；下次任一 mod 发版或手动全量重建会补上。

**全量重建（首次/修复）**：`publish-index.ps1` 保留，改为"全量扫描桶内目录重建 suite.json + 审计版本台账"。用于首次上线、手工修数据、某次 upsert 失败后补救。名单来源 `coscli ls cos://sl/mods/`；身份字段同样从本机清单读。

**首次同步审计**：上线前跑一次全量：逐仓比对 `versions.json` 与桶内 zip，剔除桶里不存在的版本行（这些版本从此不可选，因为只允许从 COS 安装）；再重建 suite.json。

**GitHub**：不再向 GitHub index 仓发布 suite.json，新版客户端也不读它。旧客户端（< v2.1.0）会停在最后一份索引，发版说明要求先升级 ModCenter。

**各 mod 仓**：`release.ps1` 零改动；配置页由各自 `modcenter.json` 维护，更新说明由 `update.json`/`versions.json` 维护。仅清单需增加 `"suite": true` 标识（§4 补偿机制）。

### 3. 客户端名单缓存

新增 `SuiteRoster`（ModCenter 内部静态类）：

- 数据：`Dictionary<string, SuiteEntry>`（GUID 主键，OrdinalIgnoreCase）+ 仓键索引
- 来源：启动时先读缓存 `BepInEx\ModCenter\suite-cache.json`（原子写：临时文件 + Move），首帧快照即可正确分流；网络预检成功后整体替换并重写缓存
- 网络：**只从 COS 桶根拉取**（`RepoClient.FetchSuite` 的 GitHub index 回退删除）。本地缓存只是上次 COS 数据的副本，不构成第二来源
- 失败：保留缓存不动；无缓存且失败 → `Ready=false`。COS 不可达时整套更新数据一起不可用——这是"COS 唯一来源"的必然结果

### 4. 套件 / 三方分流

| 情况 | 归类 | 呈现 |
|---|---|---|
| GUID 在名单 + 已加载 | 套件 | 清单投影配置页（`ThirdParty.EffectOf`，mcSpec 2 不变）；无清单退化为仅启停 |
| GUID 在名单 + 未加载（未装 / 硬禁用） | 套件 | 用名单 `display`/`summary` 出"可安装"行 + 去安装入口；硬禁用缺席行沿用 `DllDisabler` 现状 |
| GUID 不在名单 + 已加载 | 三方 | 现有 Tier 0 / Tier 1 通用页（不看 GUID 前缀） |
| GUID 不在名单 + 未加载 | 不进列表（除 `.disabled` 缺席行，沿用现状） | — |
| 自身 GUID | 不进任何列表 | 见 §7 |
| 探针（按名称） | 不进任何列表 | 现有 `TpProbeNames` 保留 |

**名单不可用时的补偿（作者裁定）**：`modcenter.json` 新增可选字段 `"suite": true`，由各套件 mod 在清单里自报身份。仅当名单未就绪（无缓存且本轮拉取失败）时作为分组依据：已装且清单自报 `suite` 的 mod 仍进套件列表（用清单 display/summary 渲染，无远端版本/更新入口）；其余按三方显示。名单可用时该字段**一律忽略**，成员身份只看名单——防止三方自报混入。未安装 mod 没有 dll，补偿机制对其无效（只能靠名单）。

代码改动：

- `EnumeratePageMods`：来源改为 `SuiteRoster`（名单内全部条目，含未安装），不再遍历 `Meta.Mods`/`Meta.Roster`
- `Resolve`：只查名单；已装成员按现有 `EffectiveOf` 投影
- `TpExempt`：改为"名单命中 ∪ 自身 ∪ 探针"才排除；GUID 前缀不再参与
- `Meta.IsSuiteGuid`：改为名单命中（名单未就绪时 false）
- 删除（成员名单类）：`Meta.Mods`、`Meta.Roster`、`Meta.SuiteGuids`、`RepoDecision.RepoKeys`
- 保留（非成员名单类）：
  - `Meta.KnownConflicts`：跨仓冲突库（人工维护的已知冲突/叠加知识），不决定成员身份
  - `Meta.ExtraNames`：冲突卡/待生效行的显示名补充，只影响文案；ModCenter 自身中文名保留
- **L3 同域提示退役**（作者裁定）：`Meta.Domains`、`DomainOf`、`DomainMates`、`HasExternalMate`、`DomainIc` 及 `CfIcsFor` 里的 L3 分支一并删除。理由：域标签是人工维护，覆盖面只有两个已知第三方，且与 L1 冲突库高度重叠；靠三方 mod 自报不可控。冲突判断保留 L1（已知冲突库）+ L2（运行时补丁碰撞扫描）；运行期错误另走 §10 的错误日志与红标

**已知行为变化**：DailySurprise（临时测试 mod，未发布到 COS、无清单）将从"套件页"落为"三方页"，配置项标签退化为 cfg 原文。若需保留套件待遇，需发布入桶并补 `modcenter.json`（本设计不做）。

### 5. 仓库行与在线安装/更新

- 仓库行来源：名单全部条目（不再 `RepoDecision.SnapshotLocal()` 扫 `RepoKeys`）
  - `Local`：按 `guid` 查 `IL2CPPChainloader.Instance.Plugins` 的 `Metadata.Version`
  - `Remote`：本轮成功拉到的名单 `version`；本轮失败但有缓存 → 用缓存名单版本（行照常可安装/更新，标"名单未刷新"）；无缓存 → `Remote=null`（"检查失败"）
  - `State`：沿用现行纯函数（未装=Install；远端高=Update；其余 Ok）
- **逐仓 GitHub 回退链退役**：`StartRepoFetch` 里"索引不可用则逐仓 `FetchLatestEx` 补查"整段删除。理由：新成员只能通过名单发现，逐仓补查无法补出新 mod；保留只会造成"索引旧、版本新"的不一致。**GitHub index 整文件回退也一并删除**（作者裁定：COS 唯一来源）；更新检查链路自此完全不碰 GitHub API，匿名限流问题随之消失
- 下载/安装链完全复用：`RepoClient.DownloadAsset(assetUrl)` → zip MD5 对名单 `md5` → 解包 `BepInEx/plugins/*.dll` → `PendingWriter.AddUpdate`
- 目标 dll 路径 = 名单 `dll`（缺省约定）；cfg 路径沿用 `config/<guid>.cfg`
- 卸载：名单内/外一致，`PendingWriter.AddDelete`；仅自身禁止（§7）
- `RepoRow.Name` 用名单 `display`，`RepoKey` 保留仓键（事件路由与文件命名仍按仓键）

### 6. 版本自由选择

**数据源**：每仓 COS `mods/<仓>/versions.json`。行字段：`version/date/notes/asset/md5/gamekb`。
**对齐保证**：台账由发布侧 `sync-cos.ps1` 每次发版按桶内实际 zip 过滤（§2），首次上线前全量审计一次。因此台账列出的版本 = 桶里能下载的版本，客户端不需要任何探测。

**客户端新增**

- `RepoClient.FetchVersions(repoKey)`：COS 直连拉台账（1 次请求，不耗 GitHub API），会话缓存；失败返回 null，面板提示"版本列表拉取失败，稍后再试"
- 面板：
  - 已装 mod 详情页底部新增"选择版本"按钮 → 弹窗列表（版本号、日期、说明摘要）；当前离线版本标"使用中"
  - 未安装 mod 的安装确认弹窗内同样可选版本（默认最新）
  - 选中后事件 `MC_UPD_VER {repoKey, version}`
- 服务端 `HandleRepoUpdVer(repoKey, version)`：
  1. 从会话缓存的版本台账取该行，构造 `UpdateInfo`（Asset/Md5/AssetUrl）
  2. 下载 URL：**只允许 COS** `mods/<仓>/<asset>`，不尝试 GitHub。404 = 数据不一致（正常不应发生，台账已按桶内文件对齐）→ 报错、清暂存、不写 pending，提示重新检查更新
  3. **绕过 `RepoActionable` 的方向守卫**（允许降级/同版本重装），仅保留"已有待生效操作 / 下载链占用"拦截。为此把 `RepoActionable` 拆为"阻塞判定"与"方向判定"两个函数
  4. 后续链路（下载进度、MD5、解包、pending、重启）与普通更新完全一致
- 降级确认：选中版本 < 本地版本时二次确认，提示"旧版本可能不适配当前游戏版本，且存档由新版写过，建议先备份存档"；不阻断
- **ModCenter 自身不提供版本选择**：头部入口只做"更新到最新"（详见 §7）；服务端 `MC_UPD_VER` 对自身 GUID 直接拒绝。理由：面板本体回退会丢失新版协议/修复，且需求只要求"自身在线更新"
- 台账没有的版本：选不了。首次审计时已把桶里不存在的版本行剔除；不提供 GitHub 回填

### 7. ModCenter 自身

- **列表移除**：`panel.js` 删除侧栏 `__self` 行（当前顶部固定行）与"自身详情页"（使用说明/卸载入口）；快照 `Self` 仅保留版本与更新徽标，不再作为可选 mod 行
- **在线更新入口迁移**：头部副标题的版本号处（现 `v2.3.1 · 多数改动即时生效`）渲染可点击的更新提示：名单显示自身有新版本时变为"v2.3.1 → v2.4.0 可更新"；点击走现有 `MC_UPD('ModCenter')` 自更新链（`selfupdate.flag` 机制不变，下靴换本体）。**只更新到最新，不提供版本选择**
- **禁止卸载**：面板无入口；服务端 `HandleUninst` / `HandleUninstDo` 对自身 GUID 直接拒绝并记日志。玩家只能手动删除 `BepInEx\plugins\SurvivalLog.ModCenter.dll`（及 cfg/目录）
- 自身仍保留在名单中（更新检查需要），但不参与套件页渲染、不参与三方页

### 8. 兼容与发布顺序

- **旧客户端 + 新 suite.json**：新字段忽略，旧字段照常；旧客户端仍用自带静态名单（看不到新 mod，符合预期）
- **新客户端 + 旧 suite.json**（无 v2 字段）：`guid` 按命名约定、`display` 用仓名，功能可用
- **发布顺序**：① 发新版 ModCenter（含 v2 解析与名单驱动）→ ② 跑一次全量审计 + `publish-index.ps1` 重建 v2 suite.json（首次）→ ③ 此后各 mod 正常发版，`sync-cos.ps1` 自动 upsert 名单与台账。顺序颠倒时新版客户端按约定回退，不会崩
- **旧客户端 + 新流程**：v2.1.0+ 的旧客户端本来就优先读 COS 桶根 suite.json，行字段兼容，可持续拿到更新；< v2.1.0 只能停在最后一份 GitHub index，发版说明要求升级

### 9. 失败与边界

| 场景 | 行为 |
|---|---|
| suite.json 拉取失败、有缓存 | 用缓存名单归类、版本取缓存值（可安装/更新入口照常）；顶部提示"名单未刷新，使用上次结果" |
| suite.json 拉取失败、无缓存 | 已装 mod 按清单 `suite: true` 补偿归类（套件 mod 仍进套件列表，无更新入口）；无该标识 → 三方。顶部提示"套件名单未获取，检查更新后恢复"。不消失、不崩 |
| 某次发版 upsert suite.json 失败 | zip/update.json 已就绪，仅名单行滞后；重跑该仓 `sync-cos.ps1` 或全量重建补救 |
| 名单内 mod 未安装 | 套件列表出"可安装"行，点行直达安装确认 |
| 名单内 mod 硬禁用（`.dll.disabled`） | 沿用 `DllDisabler.AbsentDisabled` 缺席行，可在第三方区重新启用；套件行显示未安装态 |
| 名单外已装 mod | 三方页，配置/启停/卸载照旧 |
| 台账缺某版本 | 选不了该版本；面板只列台账现有版本 |
| 台账与桶内文件不一致 | 正常不应发生（发布侧每次过滤对齐）；若发生 → 下载 404，报错不写 pending，提示重新检查更新 |
| MD5 不符 | 中止、清暂存、不写 pending（现状不变） |
| 同时点多个安装/更新 | 单下载链闸（现状不变） |
| 游戏版本不匹配 | 只展示台账 `gamekb`，不拦截（沿用 v2.2.0 决策） |

### 10. 运行错误日志与告警（新增）

**采集**

- ModCenter 加载时注册 BepInEx 日志监听器（`BepInEx.Logging.Logger.Listeners.Add(ILogListener)`；当前 BepInEx.Core.dll 已确认提供）
- 只收 `LogLevel.Error` / `Fatal`；Warning 不收（噪音）
- 归属：用 `LogEvent.Source.SourceName` 匹配已加载插件的 `Metadata.Name`/GUID（OrdinalIgnoreCase）→ 该 mod；匹配不到归"未归属"桶（常见于框架自身/加载早期）
- 线程：监听器可能被任意线程调用 → 只入并发队列；由 Pump 在主线程统一入缓冲、更新红标、推快照（主线程纪律不破）
- 去重：连续相同消息（同来源、同文本）只保留一条并记重复次数；重复次数只在详情里显示，行上不显示数字

**存储（仅本次启动，不落盘）**

- 纯内存：每个 mod 一个环形缓冲，**最多保留最近 50 条** Error/Fatal；超出上限丢弃最旧的一条
- 不写日志文件、不跨启动保留；游戏重启即清空（作者裁定：不要历史记录）
- 未归属错误单独一个缓冲，同样 50 条上限
- 面板直接读内存；复制取该 mod 当前缓冲内容（≤50 条）

**红标（本次启动口径，无未读状态）**

- **位置与点击行为完全沿用现有设计**：红标仍在行内现有 ics 槽（mod 名称之后、开关之前）；点击仍打开原有告警弹窗（`MC_CF` → `MC_CF_DATA` 卡片列表），**不改成跳转错误日志页**
- **告警按级别分两组**：红组（严重）、橙组（一般）。本次启动的 Error/Fatal 固定归入**红组**
- 行内图标**按组产出**：红组非空 → 一枚 ⚠红（18px）；橙组非空 → 一枚 ⚠橙（15px）；同组不按条目重复出图标。↑黄是更新徽标，不属于告警组
- 运行异常不新造分组、不单开图标：它就是红组里的一条。已有红色冲突的 mod，红组内同时有冲突条目和异常条目，行上仍是一枚红三角
- 点击任一警告图标仍是打开同一张告警列表；列表按红组→橙组排列，红组内原有冲突条目在前、异常条目在后
- 红标**持续整个会话**：查看错误面板后也不消失；重启后按新一次启动重新计
- 原有冲突/碰撞卡片的内容、文案、`去调序`按钮全部保留
- 红标只表示"本次启动报过错"，不下"不可用/冲突"结论，不自动禁用或卸载

**告警弹窗里的运行异常卡（`HandleCf` 追加）**

- 标题：红三角 + `运行异常 · {mod 名}`；副标题："本次启动运行时报错 N 次，可能影响功能。"
- 段落：`原因` = 最近一条错误的消息（单行截断）；`怎么办` = 1. 打开错误日志，复制详情反馈给作者；2. 重启后仍出现，先停用该 mod 排查
- 弹窗底部按钮：原`关闭`保留；有异常卡时增加`查看错误日志`（主按钮）→ 关闭弹窗并进入错误日志面板并定位该 mod
- 无运行错误的 mod：弹窗行为与现状逐字节一致

**错误日志面板**

- 入口：头部按钮"错误日志"（常驻）+ 告警弹窗里的`查看错误日志`按钮
- 形态：内容区切换（与详情页同级的整页视图，非小弹窗），左上"← 返回"
- 左栏按普通页面同样分两组：**套件** / **三方**（区标题只在有内容时显示）；"未归属"归入三方区；全空显示"暂无错误"
- 每个 mod 行：红三角 + 名称 + 条数 + 最近一条时间
- 右栏（选中 mod 后）：
  - 标题行：中文名 + 版本 + GUID（等宽字体）；右侧"仅保留最近 50 条"提示 + **一个复制图标按钮**
  - 错误条目：时间、级别、来源、消息；异常堆栈默认折叠，点"展开完整堆栈"；条目上不再放复制按钮
  - 复制按钮：一次复制该 mod 当前全部错误（≤50 条），带环境头（格式见下）
  - 不提供"清空"入口
- 复制格式（纯文本，直接贴给作者）：

  ```
  ModCenter v2.x.x · 游戏 0.x.x · 2026-11-21 12:34:56
  Mod: CabinetEverywhere v2.2.4 (com.local.survivallog.cabineteverywhere)
  ---
  [2026-11-21 12:30:01] [Error] 来源
  <消息 / 异常堆栈>
  ```

- 空态：左栏"本次启动没有错误"，右栏提示"选择一个 mod 查看错误详情"

**原型（ASCII 线框）**

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Mod 管理中心  v2.4.0                                    [检查更新] [错误日志] × │
├──────────────────────────┬───────────────────────────────────────────────────┤
│ 套件                      │  更好的储物柜   v2.2.4      仅保留最近 50 条 [⧉]  │
│ ⚠ 更好的储物柜    3 条    │  com.local.survivallog.cabineteverywhere           │
│   12:30:01 最近           │  ─────────────────────────────────────────────────│
│ ⚠ 制作队列        1 条    │  12:30:01  Error                                   │
│   11:02:18 最近           │  NullReferenceException: Object reference ...     │
│                           │    at CabinetEverywhere.Patch.Postfix()           │
│ 三方                      │    at Game.Method()                               │
│ ⚠ 未归属          2 条    │    [展开完整堆栈 ▾]                                │
│   12:31:10 最近           │  ─────────────────────────────────────────────────│
│                           │  12:28:44  Error                                   │
│                           │  InvalidOperationException: ...                   │
│                           │                                                   │
│                           │                                                   │
└──────────────────────────┴───────────────────────────────────────────────────┘
```
（右上角 `⧉` = 复制图标按钮，复制该 mod 当前全部错误）

视觉沿用现有面板令牌：底 `#0f1219` / 面板 `#171a23` / 边框 `#2a2f3d` / 正文 `#e8e6e0` / 次要 `#9aa0ad` / 弱化 `#6b7180` / 红 `#f87171` / 强调 `#e8a33d`；正文 12.5–13px，堆栈 Consolas。

**边界**

- 只覆盖经过 BepInEx 日志通道的错误；Unity 侧直接抛、未进入 BepInEx 的异常看不到
- 加载早期（插件 Logger 尚未建立）的错误可能归"未归属"
- 自身（ModCenter）的错误同样记录，面板中以"Mod 管理中心"显示
- **不保留历史**：内存缓冲随进程结束清空；错误日志面板只展示本次启动，重启后为空
- **每 mod 上限 50 条**：超出丢最旧；复制只作用于这 50 条

**回归保护（不得改坏现有告警）**

- 现有三类行内标记的触发、颜色、尺寸、点击路由、文案全部不动：⚠红 18px（L1 严重冲突；被冲突波及的套件行）/ ⚠橙 15px（L1 功能叠加、L2 运行时补丁碰撞、L3 同域——L3 本轮按作者裁定删除）/ ↑黄 18px（仓库有新版本）
- `Ics` 仍按告警组产出：同级别一枚。原有单条数据下图标数量、颜色、尺寸、点击路由完全不变；`MC_CF_DATA.cards` 结构不变，只是红组里多一条
- 原有卡片内容与`去调序`按钮不变；顺序仍为红组→橙组，红组内原条目在前、异常条目在后，橙组内部顺序不变
- 停用行（`General.Enabled=false`）沿用 `WeakenCfIcs`：异常图标同样弱化 + tip 前缀"（已停用）"
- 未安装/未加载行不挂异常图标（与现有"未安装不挂红标"一致）；待生效行照常显示异常图标（不同于仓库动作徽标的抑制规则）
- 自身行不挂行内异常图标（§7 自身已移出列表），自身错误只在日志面板可见

### 11. 验收清单

- 只装 ModCenter 首次启动：+10s 预检后，套件列表列出全部名单成员（可安装行），版本号正确
- 某 mod 正常发版后**无需任何手工步骤**：COS 桶根 suite.json 自动出现/更新该 mod 行，客户端点"检查更新"即可见；其他 mod 的行不受影响
- 版本列表与桶内 zip 完全一致：首次审计剔除的版本不再出现；发版后新增版本出现在列表顶部；选任一版本都能从 COS 下载成功
- 名单内已装 mod：详情页可改配置、可选版本、可卸载
- 名单外 mod（含使用 `com.local.survivallog` 前缀的测试 mod）：进第三方列表，配置页正常
- 版本选择：升到最新、降到旧版、同版本重装三种都能落 pending 并重启生效；降级有确认提示；任何情况都不请求 GitHub
- 运行错误：任一已装 mod 打一条 `LogError` → 该行**开关前**立即出现红色三角（无数字）；**点击红标仍弹出原有告警列表**，有错误的 mod 在列表末尾多一张"运行异常"卡；卡上`查看错误日志`或头部按钮进入日志页，可见详情与堆栈；右上角复制图标复制全部错误（带 ModCenter/游戏/Mod 环境头）；**查看后红标仍在，直至本次启动结束**；重启后缓冲清空、面板为空
- 上限：同一 mod 连续报 60 条 → 面板只保留最近 50 条，左栏计数 50，复制内容同样只有 50 条
- 同一 mod 同时有红色冲突和运行错误：红组有两条，行上仍只有一枚红三角；弹窗红组内冲突卡在前、异常卡在后，原有`关闭`/`去调序`行为不受影响
- 仅有橙组告警（如 L2 碰撞）的 mod：只有橙标，不新增红标；仅有运行错误的 mod：红标，弹窗红组只有异常卡
- 错误面板：无错误时显示空态；未归属错误归入"三方"区；左栏按套件/三方分两组（区标题仅在有内容时出现，无历史）
- ModCenter 自身：不在任何列表；面板无卸载入口；服务端直发 `MC_UNINST` 也拒绝；有新版本时头部出现更新入口并可完成自更新
- 离线：有缓存时归类正确；删除缓存并断网时已装 mod 按清单 `suite: true` 补偿归类（无该标识的进三方），无崩溃
- 旧 suite.json（无 v2 字段）下新版客户端功能可用（显示名回退仓名）

## 留给实现者

- `suite.json` 新字段名与 `SuiteEntry` 类字段映射（`guid/display/summary/dll`）
- `sync-cos.ps1` 追加逻辑：桶内 zip 列举过滤台账、suite.json upsert 的读取-替换-写回细节
- `publish-index.ps1` 全量重建/审计的本地清单路径解析与缺失回退；ModCenter 自身显示名取值
- 版本选择弹窗的布局、文案与排序（按语义版本降序）、说明摘要截断长度
- 错误面板布局、告警弹窗异常卡的排版与`查看错误日志`按钮位置、日志轮转参数、复制文本的精确格式
- `RepoActionable` 拆分后的函数命名与调用点
- `suite-cache.json` 写入时机与损坏自愈（坏文件删除重建）
