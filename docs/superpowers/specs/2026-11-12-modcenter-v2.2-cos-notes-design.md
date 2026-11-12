# ModCenter v2.2.0 设计：发版说明可见化 + 版本台账 + gamekb 退役 + 待生效可视化

日期：2026-11-12　状态：待作者批准
对话基线：COS 主源迁移已完成（v2.1.0，GitHub 全量回退保留）；本文档是其后继改造。

## 1. 意图与动机

四个玩家可感知的痛点：

1. **更新说明不可见**：面板只能检测/执行更新，每个 mod 更新了什么玩家无从得知。
   说明数据存在于各 mod 的 CHANGELOG（发版时抽为 GitHub release notes），但没进
   `update.json`/`suite.json` 契约，客户端拿不到。
2. **未安装 mod 无功能介绍**：未安装的 mod 能进详情页（无本地信息可展示），安装时
   也没有功能说明供用户判断是否安装。
3. **桶内无版本档案**：COS 桶 `mods/<mod>/` 下 zip 文件名带版本、历史自然累积，但无
   元数据标明哪个是最新（只有 update.json 指向 latest）、每个版本更新了什么无处可查。
4. **gamekb 门制造无意义更新死循环**：游戏更新后 mod 的制作基线（gamekb 快照）落后
   → 行标 ⚠橙 → 引导「更新到同版本」→ 下载同一个 zip（md5 相同）→ 重启后 ⚠橙依旧。
   作者拍板：游戏版本不一致不代表 mod 不能用，此门对玩家无意义，完全退役。
5. **待生效操作不可见**：改参数/更新/卸载/排序需重启生效，操作后只有一闪而过的
   toast + 一行底条计数，无明细可查；cfg 参数修改根本不进待生效汇总。

## 2. 目标

- G1 说明数据流：`各 mod CHANGELOG（唯一源头，职责在 mod 自身）→ release.ps1 自动
  抽取 → update.json/suite.json 带 notes → 面板展示`。面板零硬编码说明文案。
- G2 版本台账：桶内 `mods/<mod>/versions.json` 记录全版本历史（version/date/notes/
  asset/md5/gamekb），发版脚本自动追加，migrate 一次性回填 GitHub 全史。
- G3 检查完成后弹「可用更新」列表弹窗：勾选 + 展开看各 mod 发版说明 + 一键更新所选。
- G4 未安装 mod 点行 → 「功能说明 + 安装」确认弹窗（复用 v2.0.9 USAGE 数据）；
  不再进详情页。
- G5 已安装详情页加「更新内容」区块（有新版时显示 notes）。
- G6 gamekb 决策层+面板全退役；契约字段保留（发版侧照旧生成，客户端仅日志留痕）。
- G7 待生效可视化：操作反馈统一带「⏳ 重启游戏后生效」；底条可点开明细弹窗；
  cfg 参数修改（reload/restart badge）纳入待生效汇总；重启应用后自动清空。
- G8 职责与流程固化进 survivallog-release skill。

## 3. 非目标 / 反目标

- 非目标：玩家侧版本历史浏览与回滚（装旧版）；桶自动清理；Nexus/抖音自动化；
  CDN 接入。
- 反目标：说明文案硬编码进 panel.js（会导致每个 mod 发版牵动 ModCenter 发版——
  USAGE 例外：功能介绍相对稳定，随 ModCenter 版本走）；破坏五字段契约向后兼容
  （新字段必须可选，老客户端/新客户端互读不炸）。

## 4. 契约变更（向后兼容：全部为新增可选字段）

### 4.1 update.json（五字段 → 六字段）

```json
{ "mod": "...", "version": "...", "asset": "...", "md5": "...",
  "gamekb": "...", "notes": "CHANGELOG 本版节原文（可多行 markdown，UTF-8）" }
```

老客户端 `GetStr` 容错忽略 ✓。`UpdateInfo` 增加 `Notes` 字段。

### 4.2 suite.json 行

`publish-index.ps1` 聚合时从各仓 update.json 带 `notes` 进每行。客户端
`ParseSuiteJson`/`TryFetchCosJson` 解析存 `Notes`（缺失 = null，面板降级不显示）。

### 4.3 versions.json（新，桶内 `mods/<mod>/versions.json`）

```json
{ "generatedAt": <unix ms>, "mod": "<Mod>",
  "versions": [ { "version": "...", "date": "yyyy-MM-dd", "notes": "...",
                  "asset": "...", "md5": "...", "gamekb": "..." } ] }
```

顶部新 → 旧。客户端本期不消费（服务端档案；为后续可能的版本历史功能留料）。

## 5. 发布侧改动

### 5.1 release.ps1（10 仓同一块）

- 已有的 CHANGELOG 节抽取（`$notes`，用于 GitHub release）复用：写进 update.json
  的 `notes` 字段（同一份内容，两处载体）。
- 新增：**在 gh release create 之前**拉桶内旧 `versions.json`（匿名 GET 公有读）→
  顶部 unshift 本版行。失败语义：HTTP 404 = 首版起建（从本行起建台账）；其余失败
  （网络/5xx）= throw 中止发版（防一次网络抖动静默重置全史台账——此时 GitHub release
  也未发，无半态）。
- 版本行 date 取当天。
- 上传三件套：zip / update.json / versions.json。

### 5.2 sync-cos.ps1

`-Mod` 模式增加 `-Versions <path>` 参数（第三件上传物）。

### 5.3 publish-index.ps1

聚合读各仓 update.json 的 `notes` → suite.json 行带 `notes`。

### 5.4 migrate-cos.ps1（扩展为台账回填）

`gh api repos/{org}/{mod}/releases`（全量列表，含 body=notes、tag、created_at、资产）
→ 逐仓生成全史 versions.json 上传。无 update.json 资产的老 release（v2 管线之前）
跳过——台账 = 契约内全史（与 publish-index 现行为一致）。幂等可重跑。

**存量 notes 回填**（作者拍板）：migrate 同时把各仓 latest release 的 body 写进桶内
`mods/<mod>/update.json` 的 `notes` 字段（10 个小文件重传）——保证存量 10 仓在
v2.2.0 面板立即可见说明，而非等各自下次发版。跑完后重跑 publish-index.ps1。

## 6. 客户端改动（ModCenter v2.2.0）

### 6.1 RepoClient

- `UpdateInfo` + `Notes`；`TryFetchCosJson`/`ParseSuiteJson` 解析（可选字段，缺失 null）。
- gamekb 解析保留（字段仍在契约里），仅决策层不再消费。

### 6.2 RepoDecision（gamekb 退役）

- `Build()`：删 gamekb 门段（Warn 覆盖、Kbw/KbWhy、VerNote 未校验注记）。
  决策回归纯版本比较：未装=Install、tag>本地=Update、其余=Ok。
- `RepoActionable()`：删「同版本放行」路径（Kbw 行不复存在）——tag 不大于本地一律拒。
- `RepoRow` 的 Kbw/KbWhy/VerNote 字段保留结构但恒空（协议不删字段，防新旧面板互炸），
  代码注释标注退役。

### 6.3 ConfigBridge（检查轮 + 下载链 + 待生效汇总）

- 检查完成回调：若存在 Update 态行 → 推新事件 `MC_UPDATES_READY`（行数组：repoKey/
  name/local/remote/notes）。**只含 Update 态且无 pending 意图的行**（Install 归 G4
  安装弹窗，不重叠；待重启的行放着只会误导）。启动 +10s 预检不推（只标行，现状不变）。
  弹窗被关/面板关闭致事件丢弃 → 再入口 = 现有仓库页「检查更新」按钮（60s 冷却重查），
  不新增持久入口。
- `MC_UPD` 升级为可批量：新消息 `MC_UPD_SELECTED {repos[]}`——逐仓校验 actionable、
  串行走现有 `_repoChain` 单闸下载链（进度/MD5/解包/pending 写入全部复用）。
- `MC_INST` 路径不变（未安装安装）；详情页数据带 notes（快照协议 RepoData 行 + notes）。
- 待生效汇总扩展：`MC_PENDING_SUMMARY` 在现有 `{n, labels}` 基础上**新增** `items`
  数组 `{guid, mod, kind: update|install|uninstall|order|cfg, label, badge}`。
  - **cfg 项判定源（作者拍板口径）**：会话级字典 `_cfgTouched[(guid,key)] = badge`——
    在面板参数写路径（FlushDirty 落盘同一处）记录；badge=now 不记（立即生效），
    reload/restart 记录（启停算 restart 类）。重启后新会话字典为空 = 生效后自动清空，
    零新增清理逻辑。第三方 mod 的 cfg 写入同样经过面板写路径，一并计入。
  - **计数口径**：新面板底条计数 = items.Length（含 cfg）；旧字段 `n`/`labels` 语义
    冻结（不含 cfg），仅供老面板兼容降级。
  - **撤销口径（作者拍板）**：`MC_PENDING_UNDO` 仅作用于 pending.json 的 ops（更新/
    卸载/排序）；cfg 已落盘无法自动回滚，明细弹窗里 cfg 项不带撤销、只提示「再手动
    改回」。撤销 toast 改口「已撤销更新/卸载/排序操作（参数修改不回滚）」。
- 操作完成 toast 按 badge 分组：reload 类「⏳ 读档后生效」、restart 类「⏳ 重启游戏后
  生效」；mod 更新/安装/卸载/排序保存统一「⏳ 重启游戏后生效」。

### 6.4 panel.js

- **更新列表弹窗**（`__mc-modal` 复用）：勾选框（默认全选）+ mod 名 + `v本地 → v新版`
  + 「发版说明」展开区（notes 纯文本渲染，按行拆 <p>；markdown 语法字符原样显示不做
  渲染）+ 底部「更新所选 (n)」→ `MC_UPD_SELECTED`。ESC/底/关闭钮收口（同现有弹窗）。
- **安装确认弹窗**（未安装行点击）：USAGE 一句话简介 + 怎么用前 2~3 条 + 「安装」/
  「取消」→ `MC_INST {repoKey}`。未安装行不再进详情页。
- **详情页**：版本区下加「更新内容」区块（Remote>Local 且 notes 非空时显示）。
- **待生效明细弹窗**：底条整条可点击 → 弹窗分组列表（items 渲染：mod 名 + 操作说明
  徽标）。底条样式不变（计数 + 撤销/重启钮保留）。
- 删 Kbw ⚠橙 渲染与 VerNote 行（数据恒空，防御性保留分支）。

### 6.5 探针

`RepoSelfTest` 中 gamekb 五态断言改为三态（Install/Update/Ok）+ 同版本拒绝断言。

## 7. 发布顺序

1. 发布侧先行：10 仓 release.ps1 + sync-cos/publish-index/migrate 扩展 → 跑
   migrate 回填全史台账 → 重跑 publish-index（suite 带 notes）。
2. ModCenter v2.2.0（本文档全部客户端改动）→ `release.ps1`（update.json 自动带
   notes + versions.json 台账同步建立）→ `publish-index.ps1`。
3. skill 更新（survivallog-release：说明职责节 + versions.json 自动维护说明 +
   CHANGELOG 格式要求已有）。

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| notes 中文多行进 JSON（PS5.1 编码坑） | 统一 UTF-8 无 BOM 写出（现有 update.json 同款）；migrate 从 gh api 拿 body 走 UTF-8 解码（publish-index 已有同款处理） |
| versions.json 拉旧追加的并发写 | 单人发版不成立；规范写明"发版脚本独占维护，勿手改" |
| 新旧面板/dll 交叉运行 | 协议全部新增可选字段（notes/items），旧端忽略；RepoRow 退役字段保留恒空 |
| migrate 回填拉全史 release 列表 | gh 认证 5000 次/h，10 仓 × 1 次 = 10 次，预算内 |
| USAGE 缺某 mod（新 mod 未写） | 安装弹窗降级显示 README 一句话（ReleaseREADME 已有），不阻断 |

## 9. 待实现者定的细节

- 更新列表弹窗行内 notes 展开的交互形态（手风琴/直接展开）——按现有 modal 排版惯例。
- items 的 label 文案措辞。
- MC_UPD_SELECTED 与既有 MC_UPD 的代码合并方式（保持 MC_UPD 兼容）。
