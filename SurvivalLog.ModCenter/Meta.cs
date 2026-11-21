using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>面板页定义载体：ThirdParty.Project（清单投影）/ SuiteRoster 条目合成基线的产出形态，
/// ConfigBridge 快照与写路由的唯一消费面（mcSpec 2 中间表示）。</summary>
internal sealed class MetaMod
{
    public string Guid;  // BepInEx 插件 GUID（路由主键）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（短名路由兼容；名单行 = 仓键 Mod）
    public string Desc;  // 一句话简介
    public List<MetaGroup> Groups = new();
    public MetaItem OvrHost; // ovr 清单宿主项（动作加速 其他.Overrides；不进 groups items，可写）
    public MetaItem BagOvrHost = null; // bagOvr 清单宿主项（柜子容量 个性化 Global.BagOverrides，T20；不进 groups items，可写。2026-11-07 CabinetSize 退役后暂无使用者，字段保留兼容 ConfigBridge/panel.js 数据契约）

    // 启停键（任务 13）：该 mod 总开关在【其自己 cfg】里的真实节/键——SetModEnabled 直写
    // 此 entry（默认 General:Enabled，与 T9-T12 九 mod 的 Bind 一致；清单 enabled 覆盖）。
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

    // ---- mcSpec 2 投影专用（spec 2026-11-15 §1/§3；合成基线不赋值，默认值即旧行为） ----
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
/// 严重冲突/功能叠加）。Who=冲突对象（En 内部名；卡片渲染时转"中文名（En）"）。
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
    public string[] Who;     // 冲突对象（En 内部名；未登记原样展示）
    public string Reason;    // 原因（弹窗"原因"段逐字；可空=面板不渲染该段）
    public string[] Tips;    // 处置建议（弹窗"怎么办"编号列表）
}

/// <summary>
/// 元数据杂项（v2.4.0 套件名单单源后）：L1 已知冲突库 + 套件身份/显示名查询。
/// 成员资格唯一来源是 <see cref="SuiteRoster"/>（suite.json 名单缓存），本类不再持有
/// 任何静态成员名单（旧 Meta.Mods/Roster/SuiteGuids/ExtraNames/Domains 全部退役）。
/// </summary>
internal static class Meta
{
    /// <summary>ModCenter 自身显示名（待生效明细/排序面/卸载明细共用；自身不在套件名单成员语义内）。</summary>
    internal const string SelfName = "Mod 管理中心";

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

    /// <summary>是否套件成员（名单单源：SuiteRoster 查询；名单未就绪/空值=false——
    /// 未知来源（guid=null）不算套件）。</summary>
    internal static bool IsSuiteGuid(string guid) => SuiteRoster.TryGetByGuid(guid, out _);

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

    /// <summary>冲突对象显示名：名单 display（En/仓键或 GUID 命中）→ "中文名（入参）"
    /// （如 保鲜增强（FridgeChill））；未登记原样返回（表外对象也能读）。括号口径：让
    /// spec 内部名逐字可核 + 面板可读。</summary>
    internal static string DisplayNameOf(string enOrGuid)
    {
        if (string.IsNullOrEmpty(enOrGuid)) return "";
        foreach (var e in SuiteRoster.Entries)
            if (string.Equals(e.Mod, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SuiteRoster.GuidOf(e), enOrGuid, StringComparison.OrdinalIgnoreCase))
                return SuiteRoster.DisplayOf(e) + "（" + enOrGuid + "）";
        return enOrGuid;
    }

    /// <summary>色级 → 卡片标签（红=冲突 / 橙=碰撞）。</summary>
    internal static string TagOf(string level) =>
        level == "red" ? "冲突" : (level == "orange" ? "碰撞" : "同域");
}
