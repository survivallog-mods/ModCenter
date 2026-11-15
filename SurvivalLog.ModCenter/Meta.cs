using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>元数据表条目：一个 mod 的面板页定义（spec §7 v4 表逐行转写）。</summary>
internal sealed class MetaMod
{
    public string Guid;  // BepInEx 插件 GUID（路由主键）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（短名路由兼容）
    public string Desc;  // 一句话简介
    public List<MetaGroup> Groups = new();
    public MetaItem OvrHost; // ovr 清单宿主项（动作加速 其他.Overrides；不进 groups items，可写）
    public MetaItem BagOvrHost = null; // bagOvr 清单宿主项（柜子容量 个性化 Global.BagOverrides，T20；不进 groups items，可写。2026-11-07 CabinetSize 退役后暂无使用者，字段保留兼容 ConfigBridge/panel.js 数据契约）

    // 启停键（任务 13）：该 mod 总开关在【其自己 cfg】里的真实节/键——SetModEnabled 直写
    // 此 entry（默认 General:Enabled，与 T9-T12 九 mod 的 Bind 一致；个别 mod 键名不同时覆盖）。
    public string EnabledSection = "General";
    public string EnabledKey = "Enabled";
}

internal sealed class MetaGroup
{
    public string Title;   // 分组标题（显示；≠cfg 节名）
    public List<MetaItem> Items = new();
}

internal sealed class MetaItem
{
    public string Section;   // cfg 真实节名
    public string Key;       // 主键（路由/兜底锚点；联动项 = 宽度键）
    public string[] WriteKeys; // 实写键数组（联动=宽高两键等值写；null=单键 [Key]）
    public string Label;     // 面板中文名（l）
    public string Badge;     // 生效徽标（b）：now / reload / reboot / cfg
    public string Type;      // float/int/bool/sel（entry 在场时以反射 SettingType 为准，此处仅兜底）
    public object Def;       // v 兜底：entry 尚不存在时用（如 RoleUnlock Plan.* 两键待任务 8 落地）
    public double? Min, Max; // 范围兜底：AcceptableValueRange 缺失时用（对照 spec §7 表）
    public double? St;       // 滑条步进（AcceptableValue 不携带步进，永远用兜底）
    public string[] Opts;    // sel 选项兜底（string 型无 AcceptableValueList，如 UiFit.Mode）
    public string Note;      // 静态补充说明
    public string LinkedSuffix; // 联动项动态 note 后缀（前缀"当前 宽w×高h"由桥现场拼）
    public string VisKey;    // 条件显示：同 mod 内宿主项完整展示键（K 口径"节.键"，如 General.Difficulty）；null=恒显示
    public string VisVal;    // 宿主项当前值==此值才在面板显示（string 比较；宿主值随写入即时变，panel 侧重渲）

    // ---- mcSpec 2 投影专用（spec 2026-11-15 §1/§3；Meta 表自身不赋值，默认值即旧行为） ----
    public bool RequireEntry; // 清单项无 type+def 兜底：entry 缺失时快照隐藏该项（spec §3"无兜底→隐藏"；false=照旧展示）
    public List<ItemInfo.CabInfo> Cabs; // 清单 cabs 透传（ucb 下拉数据源→快照 ItemInfo.Cabs）；null 不透传

    public bool Linked => WriteKeys != null && WriteKeys.Length > 1;

    /// <summary>实写键（永不 null：未声明联动 = 单键 [Key]；TrySet/快照统一走此口）。</summary>
    public string[] WKeys => WriteKeys != null && WriteKeys.Length > 0 ? WriteKeys : new[] { Key };

    /// <summary>展示键 k（仅显示；写路由以 Section+WriteKeys 为准）。联动 = "节.键A + 键B"（原型 v4 同款）。</summary>
    public string K => Linked
        ? Section + "." + WriteKeys[0] + " + " + WriteKeys[1]
        : Section + "." + Key;
}

/// <summary>
/// L1 已知冲突库条目（v2 任务 11；spec 2026-10-28-lifecycle-v2 §4.4 首批逐字照用）。
/// 匹配键=GUID 优先、dll 文件名兜底（大小写不敏感、仅比较文件名、.dll 后缀可省）；
/// 匹配逻辑纯函数见 <see cref="Meta.MatchL1"/>。Level=red|orange（v3 两级：gray 退役，功能叠加
/// 并入 orange + 显式字形；v3.1 字形统一小号黄叹号 !）。TipShort=行内图标短提示（逐字）；Tag=弹窗卡级标签（逐字：
/// 严重冲突/功能叠加）。Who=冲突对象（Meta.En 内部名；卡片渲染时转"中文名（En）"）。
/// 表驱动：新增/撤收录只动表，不动逻辑。
/// </summary>
internal sealed class ConflictEntry
{
    public string MatchGuid; // BepInEx 插件 GUID（主匹配键）
    public string MatchDll;  // dll 文件名（兜底匹配键；仅文件名，可带/不带 .dll）
    public string Level;     // red | orange（v3 两级）
    public string TipShort;  // 行内图标短提示（v3 逐字；完整结论在弹窗卡片）
    public string Tag;       // 弹窗卡级标签（v3 逐字；仅 L1 表内条目使用）
    public string Title;     // 卡片结论一句话（弹窗副标题）
    public string[] Who;     // 冲突对象（Meta.En 内部名；未登记原样展示）
    public string Reason;    // 原因（弹窗"原因"段逐字；可空=面板不渲染该段）
    public string[] Tips;    // 处置建议（弹窗"怎么办"编号列表）
}

/// <summary>
/// 元数据表（任务 6）：套件 mod 页面定义唯一蓝本 = spec §7 v4 九张表 + EndlessGuard/CabinetBurden 后续补登
/// （docs/superpowers/specs/2026-10-24-modcenter-design.md §7；结构对照原型 v4 MODS 数组）。
/// 纪律（作者 v3 评审 + spec §5.4）：**默认值与范围不写死为准**——快照时从
/// ConfigEntry.DefaultValue/AcceptableValueRange 反射合入；此处的 Def/Min/Max 仅在
/// AcceptableValueRange 缺失或 entry 尚不存在时兜底（数值照 spec §7 表抄，防漂移口径见 DESIGN §6）。
/// 两处对 spec 的实证修正（实证优先，硬规则 12）：
/// ① UiFit.Mode 合法值是 Scroll/Zoom/Off（ViewportFit.cs 描述明示；spec §7 表 "Fit" 为笔误）；
/// ② RoleUnlock Plan.ClearPool / Plan.VaseRecipe 两个 entry 任务 8 才新增，先按 spec 默认 true 兜底展示。
/// </summary>
internal static class Meta
{

    internal static readonly List<MetaMod> Mods = new()
    {
        // mcSpec 2（spec 2026-11-15 §5 阶段 3）：8 张已迁移表删除，配置页由各 mod 内嵌清单驱动
        // （ThirdParty.EffectOf 路由①）；本表仅剩临时登记项——转正时迁清单或退役时删。
        new MetaMod
        {
            Guid = "com.local.survivallog.dailysurprise",
            Name = "喜从天降", En = "DailySurprise",
            Desc = "每个游戏日随机降临一个天降事件（好运/平淡/霉运，45 事件全量）：原生事件卡弹窗 + 即时奖惩，仅灾后生效。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "难度",
                    Items =
                    {
                        new MetaItem { Section = "General", Key = "Difficulty", Label = "难度档", Badge = "now", Type = "sel", Def = "日复一日",
                            Opts = new[] { "幸运儿", "顺风局", "日复一日", "命运多舛", "厄运缠身", "自定义" },
                            Note = "次日凌晨掷骰生效；默认日复一日（好45/平25/霉30）" }
                    }
                },
                new MetaGroup
                {
                    Title = "自定义（选「自定义」难度时显示）",
                    Items =
                    {
                        new MetaItem { Section = "General", Key = "CustomGoodPercent", Label = "好运天数%", Badge = "now", Type = "int", Def = 45, Min = 0, Max = 100, St = 5,
                            VisKey = "General.Difficulty", VisVal = "自定义", Note = "三项和须=100，非法回退档 3" },
                        new MetaItem { Section = "General", Key = "CustomPlainPercent", Label = "平淡天数%", Badge = "now", Type = "int", Def = 25, Min = 0, Max = 100, St = 5,
                            VisKey = "General.Difficulty", VisVal = "自定义" },
                        new MetaItem { Section = "General", Key = "CustomBadPercent", Label = "霉运天数%", Badge = "now", Type = "int", Def = 30, Min = 0, Max = 100, St = 5,
                            VisKey = "General.Difficulty", VisVal = "自定义", Note = "允许 0=纯抽奖" },
                        new MetaItem { Section = "General", Key = "CustomRewardMultiplier", Label = "奖励倍率", Badge = "now", Type = "float", Def = 1, Min = 0.1, Max = 5, St = 0.1,
                            VisKey = "General.Difficulty", VisVal = "自定义", Note = "缩放物品数量" },
                        new MetaItem { Section = "General", Key = "CustomLossMultiplier", Label = "损失倍率", Badge = "now", Type = "float", Def = 1, Min = 0.1, Max = 5, St = 0.1,
                            VisKey = "General.Difficulty", VisVal = "自定义", Note = "缩放耐久/偷窃/属性损失" }
                    }
                },
                new MetaGroup
                {
                    Title = "触发与展示",
                    Items =
                    {
                        new MetaItem { Section = "General", Key = "TriggerHour", Label = "每天几点触发", Badge = "now", Type = "int", Def = 1, Min = 0, Max = 23, St = 1,
                            Note = "游戏内小时（1=凌晨1点睡醒后）；0=日结算时立即触发" },
                        new MetaItem { Section = "General", Key = "ShowPlainPopup", Label = "平淡事件弹卡", Badge = "now", Type = "bool", Def = true,
                            Note = "false=平淡事件不拉事件卡，改为主角头顶气泡播台词（记账照常）" },
                        new MetaItem { Section = "General", Key = "AllowPermanentNegative", Label = "永久负面事件", Badge = "now", Type = "bool", Def = true,
                            Note = "false=心结/代价/低语全走替补（重病/雷击/管道爆裂）" }
                    }
                },
                new MetaGroup
                {
                    Title = "调试（临时测试，验完删除）",
                    Items =
                    {
                        new MetaItem { Section = "General", Key = "DebugHourlyPopup", Label = "每小时随机弹窗", Badge = "now", Type = "bool", Def = false,
                            Note = "每个游戏小时从 45 事件随机弹一个卡（仅展示，不发放不记账）；进档先弹一次" },
                        new MetaItem { Section = "General", Key = "DebugRandomDailyHour", Label = "每天随机小时触发", Badge = "now", Type = "bool", Def = false,
                            Note = "每日掷 1~23 点触发一次，覆盖上面「每天几点触发」" },
                        new MetaItem { Section = "General", Key = "DebugForceEventKey", Label = "强制指定事件", Badge = "now", Type = "sel", Def = "关闭",
                            Note = "选具体事件=进档后强制弹该事件卡一次（仅展示不发放；换档或换选值可再触发）" }
                    }
                }
            }
        }
    };



    // ============ L1 已知冲突库（v2 任务 11；spec §4.4 首批逐字） ============

    /// <summary>
    /// 首批两条（第三条 karinasprite 按 spec §4.4 明确排除，见表尾注释）。字段口径见
    /// <see cref="ConflictEntry"/>。新增条目只改本表；快照 ics/弹窗卡片都由本表派生
    /// （BuildSnapshot → <see cref="MatchL1"/>；MC_CF → 同表反向查对象）。
    /// </summary>
    internal static readonly List<ConflictEntry> KnownConflicts = new()
    {
        new ConflictEntry
        {
            MatchGuid = "com.jangfullmoon.storagecapacityexpansion",
            MatchDll = "StorageCapacityExpansion.dll",
            Level = "red",
            TipShort = "严重冲突：与「储物扩容」只能留一个，点看详情",
            Tag = "严重冲突",
            Title = "和第三方「储物扩容」只能留一个：同时开启会互相覆盖柜子和背包的容量数值。",
            Who = new[] { "CabinetSize", "BackpackExpand", "CabinetEverywhere" },
            Reason = "「储物扩容」改写 73 件柜子的内部数据并强改数值，与套件的修改相乘叠加（×1.5 ×1.5 = ×2.25）。",
            Tips = new[] { "停用或卸载「储物扩容」（推荐）", "或者停用上面任意一个套件 mod", "调加载顺序没有用" }
        },
        new ConflictEntry
        {
            MatchGuid = "survivallog.expandedstorage",
            MatchDll = "ExpandedStorage",
            Level = "orange",
            TipShort = "功能叠加：与套件背包扩容效果叠加，点看详情",
            Tag = "功能叠加",
            Title = "这几个 mod 都在改储物数值，效果会叠加，可能超出预期。",
            Who = new[] { "BackpackExpand" },
            Reason = "",
            Tips = new[] { "介意叠加就只留一个" }
        }
        // 明确排除（spec §4.4 末条，勿因"同作者/同包名族"误加）：
        // com.jangfullmoon.karinasprite / KarinaSprite.dll —— 纯立绘替换、零游戏钩子，
        // 反汇编鉴定档案已排除；收录只会产生误报。
    };

    // ============ L3 功能域标注（v2 任务 11；Guid → 域） ============

    /// <summary>
    /// 套件成员 GUID 集（= 组织 12 仓，与 Domains 表"12 套件 mod"注释块同源）。
    /// v3.3 冲突裁定：套件↔套件默认兼容（同组织协同设计），只有涉及套件外 mod 的
    /// 碰撞/叠加才报告——判定入口 <see cref="IsSuiteGuid"/>。
    /// </summary>
    internal static readonly string[] SuiteGuids =
    {
        "com.local.survivallog.backpackexpand",
        "com.local.survivallog.cabinetburden",
        "com.local.survivallog.cabineteverywhere",
        "com.local.survivallog.cabinetsize",
        "com.local.survivallog.endlessguard",
        "com.local.survivallog.fridgechill",
        "com.local.survivallog.modcenter",
        "com.local.survivallog.planpick",
        "com.local.survivallog.predisasterspeed",
        "com.local.survivallog.quickaction",
        "com.local.survivallog.robotspeed",
        "com.local.survivallog.roleunlock",
        "com.local.survivallog.unlockhub",
    };

    /// <summary>是否套件成员（OrdinalIgnoreCase；空值=false）。未知来源（guid=null）不算套件。</summary>
    internal static bool IsSuiteGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return false;
        foreach (var g in SuiteGuids)
            if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 功能域标注表：12 套件 mod 全覆盖 + 已收录第三方（进 L1 名录后才有类型；其余第三方不登记，
    /// 防"同域"盲目误报）。只用于"同域共存"灰标，只标注不判定。同域灰标的产出条件（控制器裁定）：
    /// 域内已装 ≥2 个且至少一个在本表登记——即只把"已登记且已装"的 mod 计入域内计数。
    /// 排除项 karinasprite 与 L1 一致不登记（纯立绘替换零钩子）。
    /// v2 任务 12（T11 M2）：非 L1 第三方（bettertrade/statsui）登记删除——spec §4.4"第三方仅在
    /// 进入 L1 名录后带类型"，未收录者不参与域计数（防跨域误报）。
    /// </summary>
    internal static readonly Dictionary<string, string> Domains = new(StringComparer.OrdinalIgnoreCase)
    {
        // 12 套件 mod（= 组织 12 仓；CabinetBurden 已补登 Meta 表，ModCenter 自身走附加名表）
        ["com.local.survivallog.backpackexpand"] = "储物",
        ["com.local.survivallog.cabinetburden"] = "储物",
        ["com.local.survivallog.cabineteverywhere"] = "储物",
        ["com.local.survivallog.cabinetsize"] = "储物",
        ["com.local.survivallog.endlessguard"] = "其他",
        ["com.local.survivallog.fridgechill"] = "储物",
        ["com.local.survivallog.modcenter"] = "面板",
        ["com.local.survivallog.planpick"] = "规划",
        ["com.local.survivallog.predisasterspeed"] = "速度",
        ["com.local.survivallog.quickaction"] = "速度",
        ["com.local.survivallog.robotspeed"] = "机器人",
        ["com.local.survivallog.roleunlock"] = "角色",
        // v1.8.0 更名（2026-11-02）：RoleUnlock → UnlockHub；旧 guid 保留登记（旧版玩家未升级期间仍算套件成员）
        ["com.local.survivallog.unlockhub"] = "解锁",
        // 已收录第三方（L1 名录）
        ["com.jangfullmoon.storagecapacityexpansion"] = "储物",
        ["survivallog.expandedstorage"] = "储物"
        // 排除：com.jangfullmoon.karinasprite —— 纯立绘替换零钩子（spec §4.4 档案已排除），与 L1 同口径不登记。
    };

    /// <summary>
    /// 非 Meta 九表的套件成员显示名（T11 M5）：L3 who / L2 owner / L1 冲突对象里出现时给中文名
    /// （此前 CabinetBurden/ModCenter 只能显示插件自报英文名）。只影响显示，不参与路由/匹配。
    /// </summary>
    private sealed class ExtraName
    {
        public string Guid;
        public string En;
        public string Name;
    }

    private static readonly ExtraName[] ExtraNames =
    {
        new() { Guid = "com.local.survivallog.cabinetburden", En = "CabinetBurden", Name = "柜子负重解放" },
        new() { Guid = "com.local.survivallog.modcenter", En = "ModCenter", Name = "Mod 管理中心" },
    };

    /// <summary>非 Meta 九表套件成员的中文名（GUID 精确；null=未登记）。</summary>
    internal static string ExtraNameOf(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var e in ExtraNames)
            if (string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase)) return e.Name;
        return null;
    }

    // ============ 套件成员册 roster（mcSpec 2，spec 2026-11-15 §4.4） ============

    /// <summary>roster 行：迁移名单内 mod 的"dll 缺席"兜底身份（guid=主键，仓键=
    /// RepoDecision.GuidOf 反查，En 与仓键同名不重复存）。Name/En/Desc 从 Meta.Mods 对应
    /// 条目逐字转写；迁移完成 Meta 九表删除后即唯一身份来源。</summary>
    internal sealed class RosterEntry
    {
        public string Guid;
        public string En;
        public string Name;
        public string Desc;
    }

    /// <summary>9 名迁移成员（DailySurprise 临时登记不进——走 Meta 冻结路径）。
    /// 仅"新增套件 mod"时改动（彼时本就要注册仓库）。</summary>
    internal static readonly List<RosterEntry> Roster = new()
    {
        new() { Guid = "com.local.survivallog.fridgechill", En = "FridgeChill", Name = "保鲜增强",
            Desc = "冰箱/保鲜类容器食材保鲜时长倍率；天赋保鲜上限可调。" },
        new() { Guid = "com.local.survivallog.quickaction", En = "QuickAction", Name = "动作加速",
            Desc = "全游戏动作耗时按分类统一加速（毫秒，0=官方原值）；单个动作可加覆盖单独调。" },
        new() { Guid = "com.local.survivallog.cabineteverywhere", En = "CabinetEverywhere", Name = "更好的储物柜",
            Desc = "全容器作为制作/烹饪/酿造等取料源直达；柜子页签标签筛选与场景固定视图。高度修改：25 包逐包可调（0=恢复原生），宽度恒 12（材料架恒 20）；缩高放不下的物品掉在脚下。" },
        new() { Guid = "com.local.survivallog.unlockhub", En = "UnlockHub", Name = "解锁中心",
            Desc = "通用解锁合集：角色专属内容逐项解锁 + 交易解锁（货架买后即刷）+ 探索解锁（每日一次探索点不限次）+ 重生解锁（大重生机会基本不限次）+ 属性上限解锁（饱腹/心态/精力/生命封顶 500→可调）；关闭即还原官方门槛。v1.8.0 前名「角色解锁 RoleUnlock」。" },
        new() { Guid = "com.local.survivallog.robotspeed", En = "RobotSpeed", Name = "机器人提速",
            Desc = "机器人移速/充电/续航/耗电/动作时长/货舱六维倍率 + 与手动动作对齐。" },
        new() { Guid = "com.local.survivallog.planpick", En = "PlanPick", Name = "自选生存规划",
            Desc = "生存规划改自选：解锁日全表候选 + 搜索 + 键盘；整数百分比加成。" },
        new() { Guid = "com.local.survivallog.savemgr", En = "SaveMgr", Name = "存档管理",
            Desc = "每日结算自动存档点（间隔可设）+ 局内 ESC 菜单「立即存档/读取存档」。存档点与官方存档同列同管（× 删除），零隐藏副本。" },
        new() { Guid = "com.local.survivallog.predisasterspeed", En = "PreDisasterSpeed", Name = "灾前加速",
            Desc = "灾变倒计时前的官方速度条增强（热键变档 + 真三倍校正）。仅支持启用/停用。" },
        new() { Guid = "com.local.survivallog.endlessguard", En = "EndlessGuard", Name = "囤鼠桃园",
            Desc = "无尽模式永不沦陷：拦新沦陷 + 恢复已沦陷的交易点/探险图/营地 + 抹掉沦陷倒计时；附 11 条拾遗扩展规划（D100-D300）。" },
    };

    /// <summary>roster 查询（GUID，OrdinalIgnoreCase；空/未登记=null）。</summary>
    internal static RosterEntry RosterOf(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var r in Roster)
            if (string.Equals(r.Guid, guid, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    /// <summary>
    /// L1 匹配（GUID 优先、dll 文件名兜底）。两条路径各自全表扫描：先 GUID 全表，命中即返；
    /// 再 dll 文件名全表（去目录、去 .dll 后缀、OrdinalIgnoreCase）。dllName 可传绝对路径。
    /// 纯函数（无 BepInEx 依赖）——装载期外可由独立自检程序直接调用（T11 单元自证）。
    /// </summary>
    internal static ConflictEntry MatchL1(string guid, string dllName)
    {
        if (!string.IsNullOrEmpty(guid))
            foreach (var e in KnownConflicts)
                if (!string.IsNullOrEmpty(e.MatchGuid)
                    && string.Equals(guid, e.MatchGuid, StringComparison.OrdinalIgnoreCase))
                    return e;
        var dll = DllBaseName(dllName);
        if (dll.Length > 0)
            foreach (var e in KnownConflicts)
            {
                var want = DllBaseName(e.MatchDll);
                if (want.Length > 0 && string.Equals(dll, want, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        return null;
    }

    /// <summary>dll 文件名基准：取最后一段路径、剥 .dll 后缀（大小写不敏感）；空入参 = 空串。</summary>
    internal static string DllBaseName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var i = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        var name = i >= 0 ? path.Substring(i + 1) : path;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        return name;
    }

    /// <summary>GUID → 功能域；未登记 = null（L3 灰标不产出）。</summary>
    internal static string DomainOf(string guid) =>
        !string.IsNullOrEmpty(guid) && Domains.TryGetValue(guid, out var d) ? d : null;

    /// <summary>冲突对象显示名：Who 里的 Meta.En → "中文名（En）"（如 柜子容量（CabinetSize））；
    /// 非 Meta 九表套件成员（CabinetBurden/ModCenter，T11 M5）走附加表；未登记原样返回
    /// （表外对象也能读）。括号口径：让 spec 内部名逐字可核 + 面板可读。</summary>
    internal static string DisplayNameOf(string enOrGuid)
    {
        if (string.IsNullOrEmpty(enOrGuid)) return "";
        foreach (var mm in Mods)
            if (string.Equals(mm.En, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mm.Guid, enOrGuid, StringComparison.OrdinalIgnoreCase))
                return mm.Name + "（" + enOrGuid + "）";
        // mcSpec 2：Meta 九表删除后 roster 补位（spec §4.4 兜底链）
        foreach (var r in Roster)
            if (string.Equals(r.En, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Guid, enOrGuid, StringComparison.OrdinalIgnoreCase))
                return r.Name + "（" + enOrGuid + "）";
        foreach (var e in ExtraNames)
            if (string.Equals(e.En, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Guid, enOrGuid, StringComparison.OrdinalIgnoreCase))
                return e.Name + "（" + enOrGuid + "）";
        return enOrGuid;
    }

    /// <summary>色级 → 卡片标签（红=冲突 / 橙=碰撞 / 灰=同域）。</summary>
    internal static string TagOf(string level) =>
        level == "red" ? "冲突" : (level == "orange" ? "碰撞" : "同域");
}
