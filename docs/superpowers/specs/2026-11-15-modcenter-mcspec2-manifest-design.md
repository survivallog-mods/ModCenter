# ModCenter mcSpec 2 声明式配置清单设计

日期：2026-11-15 · 状态：待作者批准

## 意图与动因

套件 mod 的面板页定义硬编码在 ModCenter `Meta.Mods`（10 张表，MetaItem 含节/键/联动/徽标/区间/选项/条件显示/清单宿主），另有使用说明硬编码在 `panel.js` 的 `USAGE` JS 静态表。任何 mod 的配置面变更（加键/改文案/调区间）都要改 ModCenter 代码 + 发版 ModCenter。

目标：**配置变更零接触 ModCenter**。每个 mod 在自己仓库维护一份声明式清单（`modcenter.json`，内嵌自己 dll），ModCenter 扫描→解析→渲染；类型/当前值/区间全部反射自 cfg entry，清单只补文案、兜底、分类、排序。

成功判据：此后任一 mod 加改配置项，只动该 mod 仓（json + mod 代码 Bind），ModCenter 无感。

## 目标 / 非目标

**目标**
- mcSpec 2 清单 schema：覆盖 `MetaItem`/`MetaMod`/`MetaGroup` 全部现役能力（含 2026-11-15 新增的 ucb 控件，见 §2）
- 套件与第三方共用同一解析器（Tier 1 `ThirdParty` 升级为通用合并层）
- 未安装行由内置 roster 兜底（guid→中文名/En/简介/仓键）
- 使用说明（usage）随清单内嵌，`panel.js` USAGE 表降级为兜底（清单优先），阶段 3 退役
- ucb 柜子白名单（panel.js `UNCOMMON_CABS`）随清单内嵌，表退役——消灭"两处同源同步"硬编码
- 渐进迁移：清单优先、Meta 冻结兜底共存，逐 mod 搬运

**非目标（反目标）**
- 跨组全局排序、按控件类型自动聚类——排序只在组内生效，作者显式声明
- 未安装行保留完整置灰配置页——退化为"名字+简介+去安装"（作者已在对话确认方向）
- 分组折叠（collapsed）——本轮不做，调试组靠 `order: 90+` 沉底
- 冲突库（KnownConflicts）/功能域（Domains）/SuiteGuids 迁出代码——套件级治理数据，非单 mod 页面定义，留 ModCenter
- 新控件类型——控件集即现 panel 全集，将来走 mcSpec 版本号增量

## 约束

- **panel.js 控件集不变**（bool 开关/int·float 滑条/sel 下拉/str 文本框/联动双键滑条/ovr·bagOvr 清单编辑器/ucb 下拉行编辑器）。改动仅限：usage 数据源、ucb 白名单数据源、未安装行空组渲染、noToggle 锁定开关（§4.5 全量清单）
- Payload 快照契约**只增不改**：新字段（`usage`、`noToggle`、item 级 `cabs`）空值不输出，旧面板/旧夹具无感
- AOT 零反射序列化纪律（Payload 手写 StringBuilder）与入向 JsonDocument 解析纪律不变
- 线程纪律：清单逐插件解析一次入缓存（会话内不变），只在主线程（Pump/BuildSnapshot）
- 写路由白名单纪律不变：TrySet 只写"清单声明过的键"（writeKeys 并集）
- **mcSpec 2 扩展字段仅套件成员生效**（`Meta.IsSuiteGuid`/roster 判定）：`enabled`/`lists`/`writeKeys`/`linkedSuffix`/`vis`/`cabs`/`type:"ucb"` 出现在第三方清单时解析后搁置+记日志，不改变第三方写语义（启停仍走 DllDisabler marker、写入仍走"只写实际存在的键"）。集成指南 mcSpec 2 章节写明适用范围
- 容错纪律照抄 Tier 1 承诺"绝不报错"：mcSpec 不识别→整单忽略；组缺 title→跳组；项缺 key→跳项；同程序集多份清单取第一份；全部只记日志。键缺失行为按版本分叉（§3）

## 方案选择（Approaches）

| 方案 | 要点 | 取舍 |
|---|---|---|
| **A. 内嵌清单 + roster 兜底（选定）** | 清单编译期内嵌各 mod dll 资源，ModCenter 同进程反射读取；未安装行走内置 roster 小表 | 版本天然同步（清单=dll 一部分）；更新链零改动（zip 原样搬 dll）；发现代码复用 Tier 1 现役实现。缺点：dll 不在则清单不可读→roster 兜底 |
| B. dll 旁散文件 | `<dll>.mc.json` 落盘 BepInEx/plugins | 手动换 dll 时清单滞后失同步；更新链解包步骤要扩展；卸载残留孤儿文件；需新增目录扫描+按名配对。否 |
| C. 集中目录清单 | ModCenter 包内/配置目录集中放全部清单 | 维护又回到中心点，分发要改更新链，与动因相悖。否 |
| D. 维持 Meta 硬编码 | 现状 | 每次配置变更改 ModCenter+发版，正是要消灭的痛点。否 |

选定 A。内嵌的可读性不是设想：Tier 1（`ThirdParty.FindResource`）已在生产环境用 `插件实例.GetType().Assembly.GetManifestResourceStream()` 读第三方 mod 内嵌清单并渲染中文定制页。停用态（General.Enabled=false）dll 仍在链上，资源照读；时序上 Chainloader 先加载完全部插件，ModCenter 快照只在面板打开时构建，资源必然就绪。

## 1. 对象结构（mcSpec 2 清单 schema）

每 mod 一份 `modcenter.json`，源文件随代码进各自仓 git，csproj 一行 `<EmbeddedResource Include="modcenter.json" />` 内嵌。mcSpec 2 = mcSpec 1 的严格超集（新增字段全可选，旧 Tier 1 清单在新解析器下继续有效）。

```json
{
  "mcSpec": 2,
  "id": "com.local.survivallog.cabineteverywhere",
  "display": "更好的储物柜",
  "en": "CabinetEverywhere",
  "summary": "全容器作为制作/烹饪/酿造等取料源直达；柜子页签标签筛选与场景固定视图……",
  "usage": {
    "intro": "一句话功能说明（安装确认段取此行）",
    "sections": [
      { "heading": "怎么用", "bullets": ["要点 1", "要点 2"] },
      { "heading": "提示", "bullets": ["……"] }
    ]
  },
  "enabled": { "section": "General", "key": "Enabled" },
  "groups": [
    {
      "title": "高度修改（0=恢复原生……）",
      "order": 1,
      "items": [
        {
          "key": "Rail:BackpackHeight",
          "label": "背包高度",
          "badge": "now",
          "type": "int", "def": 0, "min": 0, "max": 30, "step": 1,
          "note": "普通/登山/仓管背包共 9 包统一行数（原生 18）",
          "order": 10
        },
        {
          "key": "Rail:UncommonHeights",
          "label": "不常用柜子清单",
          "badge": "now",
          "type": "ucb", "def": "",
          "cabs": [
            { "id": 1006, "name": "矮柜（抽屉柜）", "native": 6 },
            { "id": 10002, "name": "金属柜", "native": 8 }
          ]
        }
      ]
    }
  ],
  "lists": [
    { "kind": "actionOverride", "key": "其他:Overrides", "label": "动作覆盖清单", "badge": "now" }
  ]
}
```

### 字段表

| 层 | 字段 | 必填 | 说明 |
|---|---|---|---|
| 顶层 | `mcSpec` | ✓ | 整数 2；≠2 整单忽略降级（Tier 0/roster），记日志 |
| | `id` | | guid；与插件 GUID 不一致时记日志、按内嵌程序集归属继续应用（Tier 1 同款） |
| | `display` / `en` / `summary` | | 中文名 / 短名（En 路由兼容）/ 一句话简介；缺项回退 Tier 0 值（插件自报名） |
| | `usage` | | `{intro, sections:[{heading, bullets[]}]}`，结构镜像现 `USAGE` 表 `{i, secs:[{h, ls}]}`——渲染逻辑仅换数据源；详情页「使用说明」弹窗 + 仓库页安装确认功能说明段（intro+首段前 3 条）两处消费；缺省=按钮不显示（现状无说明第三方同形） |
| | `enabled` | | `{section,key}`；省略=`General:Enabled`；字符串 `"none"`=无总开关（行恒亮启用，写入拒绝；快照带 `noToggle:true` 供面板渲染锁定开关） |
| | `groups` | | 数组；整段缺失→整单忽略降级（Tier 1 同款） |
| group | `title` | ✓ | 分类块标题（显示用，≠cfg 节名）；缺则跳组 |
| | `order` | number 权重，小者在前，缺省 0，同权按声明序（稳定排序） |
| | `items` | | item 数组 |
| item | `key` | ✓ | `"节:键"`（首个冒号；无冒号=空节裸键容忍写法） |
| | `label` | | 中文名；缺省回落键名（Tier 0 同形） |
| | `badge` | | `now` / `reload` / `reboot` / `cfg`；`cold`→`reboot` 归一；缺省/未知→`cfg`（Tier 1 NormBadge 同款） |
| | `type` | | `int`/`float`/`bool`/`sel`/`str`/`ucb`；**entry 在场以反射 SettingType 为准，此处仅兜底**（ucb 直通，见 §2） |
| | `def` | | entry 尚不存在时的展示值兜底 |
| | `min` / `max` / `step` / `opts` | | 区间/选项兜底，见 §3 |
| | `writeKeys` | | 联动等值写：同节多键数组（含主键）；省略=`[key]`；长度>1 即联动项（K 展示为"节.键A + 键B"） |
| | `linkedSuffix` | | 联动动态 note 后缀（前缀"当前 宽w×高h"由桥现场拼，Meta.LinkedSuffix 同位） |
| | `vis` | | `{key,value}`：宿主项（同 mod，`"节:键"`）当前值==value 才显示；string 比较，panel 即时显隐 |
| | `cabs` | | `[{id,name,native}]` 柜子白名单表，`type:"ucb"` 专用（下拉数据源，快照透传 `ItemInfo.cabs`）；替代 panel.js `UNCOMMON_CABS` 硬编码——"游戏更新加新柜子"从两处同源同步变一处（mod 仓清单） |
| | `note` / `order` | | 静态说明 / 组内排序权重 |
| list | `kind` | ✓ | `actionOverride`（id:ms 表 + 动作目录下拉）/ `bagOverride`（id:WxH 表）；封闭词表，映射到现 panel 两个清单编辑器 |
| | `key` | ✓ | 宿主 string entry（`"节:键"`） |
| | `label` / `badge` / `note` | | 同 item |

### 分类与排序约定（写进集成指南，软约定）

- 一个 group = 一个参数类别块；**块内 item 可跨 cfg 节聚合**（key 自带节名，显示分组与 cfg 节解耦）
- 分类法按用户意图不按 cfg 结构：核心开关 / 常用数值 / 低频细调 / 调试
- `item.order` **只在组内生效**（不提供跨组全局排序）；都不写=JSON 书写顺序
- 分段留空隙防连号重排：item 10~19 核心开关 / 20~39 常用数值 / 40~69 低频细调 / 90+ 调试；group 同风格
- 排序纯展示层：不影响写路由、badge、vis 行为

## 2. 控件类型

类型判定优先级：**entry 在场以反射 `SettingType` 为准，清单 `type` 仅兜底**（entry 尚不存在时，如新键未随 dll 落地；`ucb` 是唯一例外——宿主是 string entry，靠清单 type 声明切换渲染器，Meta 现行为直通）。

| type | 控件 | 值/选项来源 |
|---|---|---|
| `bool` | 开关 | entry |
| `int` / `float` | 滑条（整数/小数步进） | entry |
| `sel` | 下拉 | enum 名 / AcceptableValueList 反射 / `opts` 兜底 |
| `str` | 文本框 | 无选项 string（Tier 0 已有路径） |
| `ucb` | 下拉行编辑器（尾部下拉选柜子→加行滑调→✕ 删行还原），值=整串 `"BagId:行数,…"` 常规 MC_SET | `cabs` 白名单（清单声明，快照透传）；现 panel.js ucbHtml 编辑器复用 |
| 联动 | 双键滑条（等值写） | `writeKeys.length>1` 推导，非独立类型 |
| `lists[].kind` | actionOverride / bagOverride 清单编辑器 | 现有 ovr/bagOvr 编辑器与 acts 目录复用 |

## 3. 参数值区间与键缺失语义（按 mcSpec 版本分叉）

沿用现纪律"范围不写死为准"，纯展示层兜底，写路径零新增校验：

**区间优先级——mcSpec 2（套件迁移等价要求，镜像现 Meta.BuildItem 语义）：**

1. `AcceptableValueRange` 反射 → min/max（第一真相源 = mod 代码 Bind 声明）
2. `AcceptableValueList` / enum 反射 → opts
3. 反射缺失 → 清单 `min`/`max`/`opts` 兜底；entry 整个不存在 → `type`+`def` 全兜底展示（RoleUnlock Plan.* 两键先例）
4. `step` 永远来自清单（AcceptableValue 不携带步进）
5. bool/sel 无区间；写入仍走 BepInEx entry 内建 ClampValue 夹持

**mcSpec 1 不变（已发布承诺冻结）：** 清单 min/max/opts **覆盖**反射值（现 `ThirdParty.BuildItem` 行为、指南 §三"覆盖滑条范围"原文）。

**键缺失语义：** mcSpec 1——清单键不在实际 cfg → 一律隐藏（现行为）。mcSpec 2——entry 缺失且清单给 `type`+`def` → 兜底展示；entry 缺失且无兜底 → 隐藏。统一 BuildItem 按 mcSpec 版本选择合并顺序与缺失分支，指南 mcSpec 2 章节明示两处差异。

## 4. 运行时设计（ModCenter 侧改动）

### 4.1 解析层：ThirdParty 升级为通用合并层

- `ThirdParty.Parse` 扩 mcSpec 2 字段（en/usage/enabled/lists/writeKeys/linkedSuffix/vis/cabs/type 扩 ucb/def/opts），McManifest/McItem 结构体扩容；`McSpecSupported` 收敛为"认识的版本集 {1,2}"（1 走旧字段集+旧合并语义，2 全量+§3 分叉语义）
- Tier 2 约定类型 `ModCenterIntegration.McSpec` 门同步放行 {1,2}（`T2Of` 现共用同一常量；Contracts 签名未变，1 继续有效——扩展放行而非破坏冻结，指南注明）
- **套件扩展字段适用范围**：第三方清单出现 `enabled`/`lists`/`writeKeys`/`linkedSuffix`/`vis`/`cabs`/`ucb` 时解析后搁置+记日志（套件专属语义，见约束节），不引入第三方写路径变更
- 套件与第三方同一解析器；套件清单 = 带套件扩展字段的 Tier 1 清单
- 解析一次入缓存（guid→清单/无清单），会话内不变，主线程调用

### 4.2 快照层：ConfigBridge

- `BuildSnapshot` 套件页改为三路由：
  1. **dll 在链上且有有效清单** → 清单页（新统一 BuildItem：反射类型/值/区间 + 清单文案/兜底/排序/vis/writeKeys/联动 note）
  2. **dll 在链上无清单**（旧版 dll）→ **Meta 冻结兜底**（现路径不动；Meta 自迁移期起冻结不再编辑，语义自洽：旧 dll ↔ 旧表）
  3. **dll 不在**（未安装/卸载后重启）→ **roster 最小行**：`Missing=true`、groups 空、面板渲染"未安装/去仓库安装"直达
- `lists` 处理：kind=actionOverride → 现 `ReadOvr` + `ActionCatalog.Load()`（acts 数据源不变，QuickActionCatalog.txt 单源）；kind=bagOverride → 现 `ReadBagOvr`。OvrHost/BagOvrHost 从 MetaMod 退役，改由清单 lists 声明驱动
- `usage`/`noToggle` 进 `ModInfo` 新可选字段，`cabs` 进 `ItemInfo` 新可选字段（均空不输出，Payload 契约只增）；`type:"ucb"` 直通快照 T（Meta 现行为同款）
- 冲突徽标/仓库徽标/待生效汇总等既有链路不动（消费 guid，与页面来源无关）

### 4.3 写路径

- `Resolve`/`FindItem`：清单条目（含 writeKeys 并集）→ Meta 冻结条目 → 第三方路由，白名单纪律不变
- `SetModEnabled`：套件启停键读清单 `enabled`（"none"→拒绝并记日志，快照 noToggle 让面板锁开关）；Meta 路径不变
- MC_SET_OK 回执 badge 经清单查表（BadgeOf 扩 guid 通用）

### 4.4 roster（套件成员册）

- 骨架复用 `RepoDecision.RepoKeys`（10 仓键）+ `GuidOf` 推导；新增小表 guid→(中文名/En/一句话简介)（放 Meta.cs 或并 RepoDecision，实现自选）
- 用途：未安装最小行 + `DisplayNameOf` 显示名兜底。仅新增套件 mod 时改动（彼时本就要注册仓库）
- DailySurprise（临时登记，作者明示先不管）：留 Meta 冻结路径，不迁清单、不进 roster

### 4.5 panel.js（受控改动全量清单）

1. `USAGE` 表**降级为兜底**：快照 `ModInfo.usage` 优先，无清单 mod（迁移过渡期）回落 JS 表；阶段 3 全部迁移完成后清空表。`__self`（ModCenter 自身页）条目永久静态保留——自身不在迁移名单、无 ModInfo/清单载体
2. `UNCOMMON_CABS` 表**降级为兜底**（同 USAGE 模式）：快照 `ItemInfo.cabs` 优先，无 cabs 的 ucb 项（旧 dll 走 Meta 冻结路径不带 cabs）回落 JS 表；阶段 3 全部迁移完成后删除
3. 未安装套件行 groups 空时详情页渲染"未安装，去仓库安装"引导（不再渲染置灰控件壳）
4. `noToggle:true` → 行开关渲染锁定态（不发无效写入）

## 5. 迁移计划（三阶段）

**阶段 1（ModCenter vNext）**：§4 全部 + 集成指南（`suite/docs/modcenter-integration-guide.md`）增 mcSpec 2 章节（schema/字段表/分类排序约定/csproj 片段/容错承诺）。此版发布后旧 dll 走 Meta 冻结路径，零行为变化。

**阶段 2（逐 mod 仓）**：9 个 mod 各加 `modcenter.json` + csproj 一行（FridgeChill/QuickAction/CabinetEverywhere/UnlockHub/RobotSpeed/PlanPick/SaveMgr/PreDisasterSpeed/EndlessGuard）。每迁一个，用快照 diff 对照旧 Meta 页（k/l/t/min/max/st/b/note 逐项）验证等价后，该 mod 的 Meta 条目标记冻结（不再随配置变更维护）。

**阶段 3（收尾）**：随套件整体发版确认最低版本 dll 均带清单后，删除 `Meta.Mods` 中**已迁移的 9 张冻结表**；DailySurprise 条目保留（临时登记，转正时随转正迁清单或退役时删）；panel.js `USAGE` 表清空（仅剩 `__self` 静态条目）；KnownConflicts/Domains/SuiteGuids/ExtraNames 并入 roster 侧继续代码维护。

**升级注意**：mod 配置变更的版本起，旧 ModCenter（<vNext）面板上该 mod 仍显示冻结旧表（键不在 cfg→隐藏/拒绝写），玩家更新该 mod 时应同时更新 ModCenter——套件整包发版惯例已覆盖，发版说明里注明即可。

## 评审裁定记录（builder check，2026-11-15）

独立评审代理通读设计+代码后提 8 问，逐条裁定如下（1~8 编号对应评审报告）：

1. **ucb 控件（2026-11-15 工作区新增）**：`type:"ucb"` 直通 + item 级 `cabs:[{id,name,native}]` 白名单进清单；panel `UNCOMMON_CABS` 退役改读快照。CabinetEverywhere 阶段 2 迁移等价成立。
2. **min/max 优先级版本分叉**：mcSpec 1 冻结"清单覆盖反射"（Tier 1 已发布承诺）；mcSpec 2"反射优先、清单兜底"（套件迁移等价要求，镜像现 Meta 语义）。§3 已写明。
3. **键缺失 vs def 兜底矛盾**：mcSpec 2 = 有 `type`+`def` 兜底展示、无兜底隐藏；mcSpec 1 一律隐藏（现行为）。§3 已写明。
4. **usage 过渡窗口 + self 条目**：USAGE 表降级为兜底（阶段 1 零行为变化成立）；`__self` 条目永久静态保留。§4.5-1 已写明。
5. **usage 数据形状**：采结构化 `{intro, sections[{heading, bullets}]}` 镜像现 `{i, secs:[{h, ls}]}`，渲染逻辑仅换数据源（对话早期口头说 string[] 按行渲染，以本修正为准）。§1 已改。
6. **扩展字段对第三方**：仅套件生效，解析后搁置+日志，指南写明适用范围。约束节+§4.1 已写明。
7. **阶段 3 与 DailySurprise 矛盾**：只删 9 张已迁移表，DailySurprise 条目保留。§5 已改。
8. **Tier 2 McSpec 门**：放行 {1,2}（扩展放行非破坏冻结，Contracts 未变）。§4.1 已写明。

## 6. 留给实现的自选

- 统一 BuildItem 的落点（ConfigBridge 静态方法 vs ThirdParty 内聚）与 McManifest 内部结构体形状
- roster 表的具体安放（Meta.cs 静态表 / RepoDecision 伴生）
- 清单解析缓存与 ThirdParty 现 Cache/None 的合并方式
- 快照 diff 验证的夹具形态（现 Contracts/ 目录测试同款扩展）
