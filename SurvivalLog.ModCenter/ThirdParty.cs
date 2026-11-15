using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx.Configuration;

namespace SurvivalLog.ModCenter;

/// <summary>
/// Tier 1 声明式清单（任务 15）：第三方 mod 在其插件程序集内嵌 <c>modcenter.json</c>
/// （mcSpec 1，字段表 = docs/modcenter-integration-guide.md §三，行为规范单源）获得
/// 中文定制页。本类是 Tier 0 通用页（ConfigBridge.BuildTp，任务 14）之上的发现与合并层：
///
/// - 发现：插件实例 <c>GetType().Assembly.GetManifestResourceNames()</c> 匹配
///   <c>modcenter.json</c>（精确名或 <c>*.modcenter.json</c> 后缀——指南 csproj 片段不带
///   LogicalName 时资源实名为 <c>根命名空间.modcenter.json</c>，两种都要认）；
///   同一程序集多份清单**取第一个**，其余忽略并记日志（指南 §三行为约定）。
/// - 校验容错（指南承诺"绝不报错"）：JSON 解析失败 / mcSpec 缺失或不认识（≠1）/
///   groups 整段缺失 → **整单忽略降级 Tier 0**；清单键在实际 cfg 不存在 → 该项隐藏
///   （容忍版本差异）；display/summary 缺项逐字段回退 Tier 0 值；组无 title / 项无 key
///   → 跳过该组/该项并记日志。
/// - 合并渲染：display/summary 覆盖显示名与简介；groups 全量重组（Tier 0"cfg 节名分组/
///   键名即标签"被清单替代——**清单没列的键不出现**）；item 的 label/note/badge/
///   min-max-step/order 逐项覆盖 Tier 0 推断值（范围覆盖优先于 AcceptableValueRange 反射，
///   缺省回落反射值——指南 §三字段表）。badge 词汇表归一：指南 now/reload/cold/缺省 cfg →
///   面板契约 now/reload/reboot/cfg（Payload.ItemInfo.B 口径，cold→reboot）。
/// - softDisable：清单声明暂存（<see cref="SoftDisableOf"/>），任务 16 启停两层语义消费。
/// - 写路由零改动：Tier 1 项展示键仍是 节.键（面板发整串 k，TrySetTp 剥前缀兜底既有链）；
///   MC_SET_OK 回执 badge 经 <see cref="BadgeOf"/> 取清单徽标（缺省 cfg）。
/// 线程纪律：只在 ConfigBridge.Pump 主线程（快照构建）被调；清单逐插件解析一次入缓存。
/// </summary>
internal static class ThirdParty
{
    /// <summary>当前认识的最高协议版本（mcSpec 1 字段冻结 + 2 套件扩展；不认识 → 整单忽略）。
    /// 认识的版本集见 <see cref="SpecKnown"/>（Tier 2 McSpec 门同步放行，接口冻结之扩展而非变更）。</summary>
    private const int McSpecSupported = 2;

    /// <summary>是否认识的 mcSpec 版本（1=第三方 Tier 1 冻结集；2=套件扩展集，spec 2026-11-15 §1）。</summary>
    internal static bool SpecKnown(int v) => v == 1 || v == 2;

    // 清单缓存（会话内不变）：guid → 解析成功的清单；_none = 已查过且无有效清单（防重复扫）
    private static readonly Dictionary<string, McManifest> Cache = new();
    private static readonly HashSet<string> None = new();

    // internal：EffectOf/Project 签名需要（对 ConfigBridge 暴露投影面）；嵌套类型保持 private
    internal sealed class McManifest
    {
        internal string Id, Display, Summary;
        internal int Spec;   // 实际解析版本（日志口径；1/2）
        internal bool SoftDisable;
        internal string En;                    // mcSpec 2：内部短名（En 路由兼容）
        internal McUsage Usage;                // mcSpec 2：使用说明（panel USAGE 表退役的数据源）
        internal McEnabled Enabled;            // mcSpec 2：启停键声明（缺省 General/Enabled；"none"=无总开关）
        internal List<McList> Lists = new();   // mcSpec 2：清单编辑器宿主（actionOverride/bagOverride）
        internal List<McGroup> Groups = new();
        internal Dictionary<string, string> Badges = new(); // 展示键 k → 归一徽标（Apply 渲染时填充，MC_SET_OK 回执查表）
    }

    internal sealed class McGroup
    {
        internal string Title;
        internal double? Order;
        internal List<McItem> Items = new();
    }

    internal sealed class McItem
    {
        internal string Key, Label, Note, Badge; // Key = "Section:Key"（指南格式）
        internal double? Min, Max, Step, Order;
        // ---- mcSpec 2 扩展（仅套件生效；spec §约束：第三方出现→解析保留、消费侧搁置+日志） ----
        internal string Type;          // int/float/bool/sel/str/ucb——entry 缺失时类型兜底（ucb 恒以此声明）
        internal object Def;           // entry 尚不存在时的展示值兜底（number→double/string/bool）
        internal List<string> Opts;    // sel 选项兜底（反射无 AcceptableValueList 时）
        internal string[] WriteKeys;   // 联动等值写键组（同节，含主键；null=单键 [Key]）
        internal string LinkedSuffix;  // 联动动态 note 后缀（前缀"当前 宽w×高h"由桥现场拼）
        internal McVis Vis;            // 条件显示（宿主项当前值==Value 才显示）
        internal List<McCab> Cabs;     // ucb 柜子白名单（快照透传面板下拉数据源）
    }

    /// <summary>mcSpec 2 使用说明：intro 一句话（安装确认段取此行）+ 分段要点（详情页弹窗）。</summary>
    internal sealed class McUsage
    {
        internal string Intro;
        internal List<McUsageSec> Sections = new();
    }

    internal sealed class McUsageSec
    {
        internal string Heading;
        internal List<string> Bullets = new();
    }

    /// <summary>mcSpec 2 启停键：对象 {section,key}；字符串 "none" → None=true（无总开关，行恒亮）。</summary>
    internal sealed class McEnabled
    {
        internal string Section, Key;
        internal bool None;
    }

    /// <summary>mcSpec 2 清单编辑器宿主（kind 封闭词表，映射面板 ovr/bagOvr 编辑器）。</summary>
    internal sealed class McList
    {
        internal string Kind;   // actionOverride | bagOverride
        internal string Key;    // 宿主 string entry（"节:键"）
        internal string Label, Badge, Note;
    }

    /// <summary>mcSpec 2 条件显示：宿主项（同 mod "节:键"）当前值==Value 才显示（string 比较）。</summary>
    internal sealed class McVis
    {
        internal string Key, Value;
    }

    /// <summary>mcSpec 2 ucb 柜子白名单行（id=BagId，name=显示名，native=原生行数）。</summary>
    internal sealed class McCab
    {
        internal long Id;
        internal string Name;
        internal double Native;
    }

    // ============ 对外契约 ============

    /// <summary>
    /// 把清单合并进第三方通用页 ModInfo（ConfigBridge.BuildTpMod 调用）：
    /// 无有效清单（返回 false）时调用方保持 Tier 0 形态。cfg 允许 null（无 ConfigFile 的
    /// mod 也能有清单——全部项隐藏，落仅启停形态；display/summary 照常生效）。
    /// </summary>
    internal static bool Apply(BepInEx.PluginInfo info, ModInfo mi, ConfigFile cfg)
    {
        var md = info.Metadata;
        var man = ManifestOf(info);
        if (man == null) return false;
        mi.Tier = 1; // 快照标记（面板/fixture 判别 Tier 1 定制页；Payload 增量字段）
        if (!string.IsNullOrEmpty(man.Display)) mi.Name = man.Display;
        if (!string.IsNullOrEmpty(man.Summary)) mi.Desc = man.Summary;
        if (!string.IsNullOrEmpty(man.Id) && !string.Equals(man.Id, md.GUID, StringComparison.OrdinalIgnoreCase))
            Log($"清单 id '{man.Id}' 与插件 GUID '{md.GUID}' 不一致（指南建议一致；按内嵌程序集归属继续应用）");

        int hidden = 0;
        foreach (var g in man.Groups.OrderBy(x => x.Order)) // order 权重小者在前（缺省 0；LINQ 稳定排序）
        {
            var gi = new GroupInfo { Title = g.Title };
            foreach (var it in g.Items.OrderBy(x => x.Order))
            {
                var e = Split(it.Key, out var section, out var key)
                    ? (cfg != null ? ConfigBridge.Entry(cfg, section, key) : null)
                    : null;
                if (e == null) { hidden++; continue; } // 清单键不在实际 cfg → 隐藏（容忍版本差异）
                var ii = BuildItem(it, section, key, e);
                gi.Items.Add(ii);
                man.Badges[ii.K] = ii.B; // 渲染时填回执查表（HandleSet→BadgeOf；未列/隐藏项缺省 cfg）
            }
            if (gi.Items.Count > 0) mi.Groups.Add(gi); // 整组项全隐藏 → 空组不渲染
        }
        if (hidden > 0)
            Log($"清单(tp) {md.GUID}: {hidden} 个清单键不在其 cfg（隐藏，容忍版本差异）");
        return true;
    }

    /// <summary>清单声明的 softDisable（任务 16 启停两层语义消费；无清单=false）。</summary>
    internal static bool SoftDisableOf(string guid) =>
        Cache.TryGetValue(guid ?? "", out var m) && m != null && m.SoftDisable;

    /// <summary>清单徽标（展示键 k → 归一面板徽标；Tier 0 / 未列项 → null，回执缺省 cfg）。</summary>
    internal static string BadgeOf(string guid, string displayK) =>
        displayK != null && Cache.TryGetValue(guid ?? "", out var m) && m != null
            && m.Badges.TryGetValue(displayK, out var b) ? b : null;

    // ============ Tier 2 深度对接（任务 16；指南 §四，Contracts 见 Contracts/ModCenterIntegration.cs） ============

    /// <summary>
    /// 第三方运行时状态（QueryStates 回报，duck-typing 读出后归一为托管副本；
    /// 空串 Key 代表整个 mod，State 只认 ok——非 ok 一律按 failed 展示 Reason）。
    /// </summary>
    internal sealed class McState
    {
        internal string Key, State, Reason;
    }

    /// <summary>发现的 Tier 2 约定类型句柄（反射按名 duck-typing——不同程序集各自编入的
    /// 接口类型不可强转，禁止 cast；Degraded=OnEnabledChanged 曾返回 false，本会话降级重启级）。</summary>
    private sealed class T2Integration
    {
        internal System.Reflection.MethodInfo OnEnabled;  // static bool OnEnabledChanged(bool)
        internal System.Reflection.MethodInfo Query;      // static Xxx QueryStates()（可选）
        internal bool Degraded;
    }

    private static readonly Dictionary<string, T2Integration> T2Cache = new();
    private static readonly HashSet<string> T2None = new();

    /// <summary>
    /// 约定类型发现：插件程序集内 <c>ModCenterIntegration</c>（先精确全局名，后按简单名兜底扫描——
    /// 允许作者套命名空间），要求 public static <c>bool OnEnabledChanged(bool)</c>；
    /// 静态属性 <c>McSpec</c> 在场且 ≠1 → 不认识，降级 Tier 1/0（指南 §七接口冻结）。
    /// QueryStates 可选。逐插件解析一次入缓存；任何缺失/异常都只是"没有 Tier 2"，绝不报错。
    /// </summary>
    private static T2Integration T2Of(BepInEx.PluginInfo info)
    {
        var guid = info.Metadata?.GUID;
        if (string.IsNullOrEmpty(guid)) return null;
        if (T2Cache.TryGetValue(guid, out var hit)) return hit;
        if (T2None.Contains(guid)) return null;

        T2Integration t2 = null;
        try
        {
            var asm = info.Instance.GetType().Assembly;
            var t = asm.GetType("ModCenterIntegration", false);
            if (t == null)
            {
                foreach (var ty in asm.GetTypes())
                    if (ty.Name == "ModCenterIntegration") { t = ty; break; }
            }
            if (t != null)
            {
                var on = t.GetMethod("OnEnabledChanged",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                    new[] { typeof(bool) }, null);
                if (on == null || on.ReturnType != typeof(bool))
                    Log($"Tier2(tp) {guid}: 类型 ModCenterIntegration 在场但缺 static bool OnEnabledChanged(bool)——按 Tier 1/0 对待");
                else
                {
                    var specProp = t.GetProperty("McSpec",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (specProp != null)
                    {
                        var specOk = false;
                        try { specOk = SpecKnown(Convert.ToInt32(specProp.GetValue(null), CultureInfo.InvariantCulture)); }
                        catch { }
                        if (!specOk)
                        {
                            Log($"Tier2(tp) {guid}: McSpec 不识别——按 Tier 1/0 对待（接口冻结：mcSpec 1）");
                            t = null;
                        }
                    }
                    if (t != null)
                    {
                        t2 = new T2Integration
                        {
                            OnEnabled = on,
                            Query = t.GetMethod("QueryStates",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                                Type.EmptyTypes, null),
                        };
                        Log($"Tier2(tp) {guid}: 约定类型命中（OnEnabledChanged 就绪，QueryStates {(t2.Query != null ? "就绪" : "缺席(可选)")}）——软停回调生效");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {guid}: 发现异常（按 Tier 1/0 对待）: {e.Message}");
            t2 = null;
        }
        if (t2 == null) T2None.Add(guid);
        else T2Cache[guid] = t2;
        return t2;
    }

    /// <summary>软停能力（面板两层语义判据，任务 16）：Tier 2 约定在场且未降级，或清单声明 softDisable。</summary>
    internal static bool SoftOf(BepInEx.PluginInfo info)
    {
        var t2 = T2Of(info);
        return (t2 != null && !t2.Degraded) || SoftDisableOf(info.Metadata?.GUID);
    }

    /// <summary>
    /// 启停回调（主线程 Pump 调）：enabled=false 对方应立即还原全部行为，true 恢复。
    /// 返回 true=对方已处理（软停语义）；无约定/异常/返回 false → false（调用方降级重启级语义），
    /// 返回 false 时本会话标记 Degraded（后续快照 Soft 同步回落）。
    /// </summary>
    internal static bool FireEnabledChanged(BepInEx.PluginInfo info, bool enabled)
    {
        var t2 = T2Of(info);
        if (t2 == null || t2.Degraded && !enabled) return false; // 已降级后不再打扰；enable 重试交给下次真启用
        try
        {
            var r = t2.OnEnabled.Invoke(null, new object[] { enabled });
            var ok = r is bool b && b;
            if (!ok && !enabled)
            {
                t2.Degraded = true;
                Log($"Tier2(tp) {info.Metadata?.GUID}: OnEnabledChanged(false) 返回 false——本会话降级重启级语义");
            }
            return ok;
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {info.Metadata?.GUID}: OnEnabledChanged({enabled}) 异常（降级重启级）: {e.Message}");
            if (!enabled) t2.Degraded = true;
            return false;
        }
    }

    /// <summary>状态回报（快照构建时调；无约定/异常/缺席 → null）。上限 64 项防病态清单。</summary>
    internal static List<McState> QueryStatesOf(BepInEx.PluginInfo info)
    {
        var t2 = T2Of(info);
        if (t2?.Query == null) return null;
        try
        {
            var r = t2.Query.Invoke(null, Array.Empty<object>());
            if (r is not System.Collections.IEnumerable en) return null;
            var list = new List<McState>();
            foreach (var o in en)
            {
                if (o == null || list.Count >= 64) continue;
                var ty = o.GetType();
                string P(string name) => ty.GetProperty(name)?.GetValue(o) as string;
                list.Add(new McState { Key = P("Key") ?? "", State = P("State") ?? "ok", Reason = P("Reason") ?? "" });
            }
            return list;
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {info.Metadata?.GUID}: QueryStates 异常（忽略）: {e.Message}");
            return null;
        }
    }

    // ============ 清单发现与解析 ============

    /// <summary>套件投影（spec 2026-11-15 §4.2 路由①）：dll 在链上且清单有效 → 投影为 MetaMod
    /// 供 ConfigBridge 全部既有消费面（快照 Groups/OvrHost/BagOvrHost、TrySet/FindItem 白名单、
    /// SetModEnabled 启停键）零改动复用——ConfigBridge.BuildItem 的"反射优先+Meta 兜底"本就是
    /// mcSpec 2 区间语义。清单缺失（返回 mm 原样）= 路由② Meta 冻结。man 出参=null 即路由②，
    /// 非 null 时调用方附挂 UsageJson/NoToggle。线程：主线程（BuildSnapshot/Pump 上下文）。</summary>
    internal static MetaMod EffectOf(MetaMod mm, ConfigFile cfg,
        Dictionary<string, BepInEx.PluginInfo> plugins, out McManifest man)
    {
        man = null;
        if (cfg == null || plugins == null) return mm; // dll 不在链上（路由③由调用方处理）或枚举不可用
        if (!plugins.TryGetValue(mm.Guid, out var pi) || pi?.Instance == null) return mm;
        man = ManifestOf(pi);
        return man == null ? mm : Project(mm, man);
    }

    /// <summary>纯投影（自检可离线调）：McManifest → MetaMod。display/en/summary 缺项回退基线 mm；
    /// groups/items 按 order 稳定排序；lists→OvrHost/BagOvrHost；enabled→EnabledSection/Key
    /// （"none" 保持缺省 General/Enabled——entry 缺失自然拒绝，DailySurprise 现行同款；
    /// NoToggle 由快照侧按 man 挂）。badge 经 NormBadge 归一（回执/快照共用口径）。</summary>
    internal static MetaMod Project(MetaMod mm, McManifest man)
    {
        var p = new MetaMod
        {
            Guid = mm.Guid, // 路由主键恒用基线（roster/Meta 一致）
            Name = string.IsNullOrEmpty(man.Display) ? mm.Name : man.Display,
            En = string.IsNullOrEmpty(man.En) ? mm.En : man.En,
            Desc = string.IsNullOrEmpty(man.Summary) ? mm.Desc : man.Summary,
        };
        if (man.Enabled != null && !man.Enabled.None)
        {
            p.EnabledSection = man.Enabled.Section;
            p.EnabledKey = man.Enabled.Key;
        }
        foreach (var g in man.Groups.OrderBy(x => x.Order ?? 0))
            p.Groups.Add(new MetaGroup
            {
                Title = g.Title,
                Items = g.Items.OrderBy(x => x.Order ?? 0).Select(ToMetaItem).ToList(),
            });
        foreach (var l in man.Lists)
        {
            if (l.Kind != "actionOverride" && l.Kind != "bagOverride") continue;
            if (!Split(l.Key, out var section, out var key)) continue;
            var host = new MetaItem
            {
                Section = section, Key = key, Type = "sel", Def = "",
                Label = string.IsNullOrEmpty(l.Label) ? key : l.Label,
                Badge = NormBadge(l.Badge), Note = l.Note,
            };
            if (l.Kind == "actionOverride") p.OvrHost = host;
            else p.BagOvrHost = host;
        }
        return p;
    }

    /// <summary>McItem → MetaItem（mcSpec 2 字段逐项落位；vis 冒号键转 panel 点口径）。</summary>
    private static MetaItem ToMetaItem(McItem it)
    {
        Split(it.Key, out var section, out var key);
        var m = new MetaItem
        {
            Section = section,
            Key = key,
            WriteKeys = it.WriteKeys != null && it.WriteKeys.Length > 0 ? it.WriteKeys : null,
            Label = string.IsNullOrEmpty(it.Label) ? key : it.Label,
            Badge = NormBadge(it.Badge),
            Type = it.Type,
            Def = it.Def,
            Min = it.Min, Max = it.Max, St = it.Step,
            Opts = it.Opts?.ToArray(),
            Note = it.Note,
            LinkedSuffix = it.LinkedSuffix,
            // spec §3：无 type+def 兜底 → entry 缺失即隐藏（BuildItem 判 RequireEntry 返回 null）
            RequireEntry = string.IsNullOrEmpty(it.Type) && it.Def == null,
        };
        if (it.Vis != null && Split(it.Vis.Key, out var vs, out var vk))
        {
            m.VisKey = vs + "." + vk;
            m.VisVal = it.Vis.Value;
        }
        if (it.Cabs != null && it.Cabs.Count > 0)
            m.Cabs = it.Cabs.Select(c => new ItemInfo.CabInfo { Id = c.Id, Name = c.Name, Native = c.Native }).ToList();
        return m;
    }

    private static McManifest ManifestOf(BepInEx.PluginInfo info)
    {
        var guid = info.Metadata?.GUID;
        if (string.IsNullOrEmpty(guid)) return null;
        if (Cache.TryGetValue(guid, out var hit)) return hit;
        if (None.Contains(guid)) return null;

        var res = FindResource(info);
        var man = res != null ? Parse(res.Stream, guid) : null;
        if (man == null) None.Add(guid);
        else
        {
            Cache[guid] = man;
            Log($"清单(tp) {guid}: 命中资源 '{res.Name}'"
                + (res.Dups > 1 ? $"（同程序集 {res.Dups} 份 modcenter.json，取第一个，其余忽略）" : "")
                + $"——mcSpec {man.Spec} 定制页生效（{man.Groups.Count} 组）");
        }
        return man;
    }

    private sealed class ResInfo
    {
        internal string Name;
        internal Stream Stream;
        internal int Dups;
    }

    /// <summary>资源发现：精确名 modcenter.json 或 *.modcenter.json 后缀（均忽略大小写），
    /// 多份取第一个（GetManifestResourceNames 数组序）。</summary>
    private static ResInfo FindResource(BepInEx.PluginInfo info)
    {
        try
        {
            var asm = info.Instance.GetType().Assembly;
            string first = null;
            int dups = 0;
            foreach (var n in asm.GetManifestResourceNames())
            {
                var isMc = string.Equals(n, "modcenter.json", StringComparison.OrdinalIgnoreCase)
                    || (n.Length > 15 && n.EndsWith(".modcenter.json", StringComparison.OrdinalIgnoreCase));
                if (!isMc) continue;
                dups++;
                if (first == null) first = n;
            }
            if (first == null) return null;
            var s = asm.GetManifestResourceStream(first);
            if (s == null) return null;
            return new ResInfo { Name = first, Stream = s, Dups = dups };
        }
        catch (Exception e)
        {
            Log($"warn 清单(tp) {info.Metadata?.GUID} 资源发现异常: {e.Message}");
            return null;
        }
    }

    /// <summary>解析与校验（JsonDocument，套件入向解析同款纪律）。任何不合格 → null（降级 Tier 0）。
    /// mcSpec 1 字段集与合并语义冻结（指南 §七）；mcSpec 2 = 1 的严格超集，扩展字段仅套件生效
    /// （spec 2026-11-15 §1/§3）——v1 清单行为逐字节不变，v2 逐字段额外解析。</summary>
    private static McManifest Parse(Stream stream, string guid)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { Log($"清单(tp) {guid} 根非对象，忽略"); return null; }
            if (!root.TryGetProperty("mcSpec", out var specEl)
                || specEl.ValueKind != JsonValueKind.Number
                || !specEl.TryGetInt32(out var spec)
                || !SpecKnown(spec))
            {
                Log($"清单(tp) {guid} mcSpec 不识别（{Raw(specEl)}）——整单忽略，按 Tier 0 通用页兜底");
                return null;
            }
            var v2 = spec >= 2;
            var man = new McManifest
            {
                Spec = spec,
                Id = GetS(root, "id"),
                Display = GetS(root, "display"),
                Summary = GetS(root, "summary"),
                SoftDisable = root.TryGetProperty("softDisable", out var sd) && sd.ValueKind == JsonValueKind.True,
            };
            if (v2)
            {
                man.En = GetS(root, "en");
                man.Usage = ParseUsage(root, guid);
                man.Enabled = ParseEnabled(root, guid);
                ParseLists(root, guid, man.Lists);
                if (!Meta.IsSuiteGuid(guid) && HasExtFields(root))
                    Log($"清单(tp) {guid}: mcSpec 2 扩展字段仅套件清单生效，本清单按 Tier 1 字段集对待（解析保留、消费搁置）");
            }
            if (!root.TryGetProperty("groups", out var gsEl) || gsEl.ValueKind != JsonValueKind.Array)
            {
                Log($"清单(tp) {guid} 缺 groups 段——整单忽略，按 Tier 0 通用页兜底");
                return null;
            }
            foreach (var ge in gsEl.EnumerateArray())
            {
                if (ge.ValueKind != JsonValueKind.Object) continue;
                var title = GetS(ge, "title");
                if (string.IsNullOrEmpty(title)) { Log($"清单(tp) {guid} 组缺 title，跳过该组"); continue; }
                var g = new McGroup { Title = title, Order = GetN(ge, "order") };
                if (ge.TryGetProperty("items", out var itsEl) && itsEl.ValueKind == JsonValueKind.Array)
                    foreach (var ie in itsEl.EnumerateArray())
                    {
                        if (ie.ValueKind != JsonValueKind.Object) continue;
                        var key = GetS(ie, "key");
                        if (string.IsNullOrEmpty(key)) { Log($"清单(tp) {guid} '{title}' 有项缺 key，跳过该项"); continue; }
                        var it = new McItem
                        {
                            Key = key, Label = GetS(ie, "label"), Note = GetS(ie, "note"), Badge = GetS(ie, "badge"),
                            Min = GetN(ie, "min"), Max = GetN(ie, "max"), Step = GetN(ie, "step"),
                            Order = GetN(ie, "order"),
                        };
                        if (v2) ParseItemExt(ie, it);
                        g.Items.Add(it);
                    }
                man.Groups.Add(g);
            }
            return man;
        }
        catch (Exception e)
        {
            Log($"清单(tp) {guid} 解析失败（{e.Message}）——整单忽略，按 Tier 0 通用页兜底");
            return null;
        }
    }

    // ---- mcSpec 2 扩展字段解析（任何不合格只是忽略该字段，绝不废整单） ----

    /// <summary>usage：{intro, sections:[{heading, bullets[]}]}</summary>
    private static McUsage ParseUsage(JsonElement root, string guid)
    {
        if (!root.TryGetProperty("usage", out var ue) || ue.ValueKind != JsonValueKind.Object) return null;
        var u = new McUsage { Intro = GetS(ue, "intro") };
        if (ue.TryGetProperty("sections", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var se in ss.EnumerateArray())
            {
                if (se.ValueKind != JsonValueKind.Object) continue;
                var sec = new McUsageSec { Heading = GetS(se, "heading") };
                if (se.TryGetProperty("bullets", out var bs) && bs.ValueKind == JsonValueKind.Array)
                    foreach (var b in bs.EnumerateArray())
                        if (b.ValueKind == JsonValueKind.String && b.GetString().Length > 0)
                            sec.Bullets.Add(b.GetString());
                u.Sections.Add(sec);
            }
        if (string.IsNullOrEmpty(u.Intro) && u.Sections.Count == 0) return null;
        return u;
    }

    /// <summary>enabled：{section,key} 对象或 "none" 字符串；畸形 → 忽略（缺省 General/Enabled）。</summary>
    private static McEnabled ParseEnabled(JsonElement root, string guid)
    {
        if (!root.TryGetProperty("enabled", out var ee)) return null;
        if (ee.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(ee.GetString()?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return new McEnabled { None = true };
            Log($"清单(tp) {guid} enabled 字符串不识别（{Raw(ee)}），按缺省 General/Enabled 对待");
            return null;
        }
        if (ee.ValueKind == JsonValueKind.Object)
        {
            var s = GetS(ee, "section"); var k = GetS(ee, "key");
            if (!string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(k)) return new McEnabled { Section = s, Key = k };
            Log($"清单(tp) {guid} enabled 对象缺 section/key，按缺省 General/Enabled 对待");
        }
        return null;
    }

    /// <summary>lists[]：kind+key 必填（kind 封闭词表），label/badge/note 可选；坏行跳过。</summary>
    private static void ParseLists(JsonElement root, string guid, List<McList> into)
    {
        if (!root.TryGetProperty("lists", out var le) || le.ValueKind != JsonValueKind.Array) return;
        foreach (var e in le.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var kind = GetS(e, "kind"); var key = GetS(e, "key");
            if (kind != "actionOverride" && kind != "bagOverride")
            { Log($"清单(tp) {guid} lists 行 kind 不识别（{Raw(GetKindEl(e))}），跳过"); continue; }
            if (string.IsNullOrEmpty(key)) { Log($"清单(tp) {guid} lists 行缺 key，跳过"); continue; }
            into.Add(new McList { Kind = kind, Key = key, Label = GetS(e, "label"), Badge = GetS(e, "badge"), Note = GetS(e, "note") });
        }
    }

    private static JsonElement GetKindEl(JsonElement e) =>
        e.TryGetProperty("kind", out var k) ? k : default;

    /// <summary>item 扩展字段：type/def/opts/writeKeys/linkedSuffix/vis/cabs。</summary>
    private static void ParseItemExt(JsonElement ie, McItem it)
    {
        it.Type = GetS(ie, "type");
        it.Def = ParseDef(ie);
        if (ie.TryGetProperty("opts", out var oe) && oe.ValueKind == JsonValueKind.Array)
        {
            it.Opts = new List<string>();
            foreach (var o in oe.EnumerateArray())
                if (o.ValueKind == JsonValueKind.String && o.GetString().Length > 0)
                    it.Opts.Add(o.GetString());
            if (it.Opts.Count == 0) it.Opts = null;
        }
        if (ie.TryGetProperty("writeKeys", out var we) && we.ValueKind == JsonValueKind.Array)
        {
            var ks = new List<string>();
            foreach (var w in we.EnumerateArray())
                if (w.ValueKind == JsonValueKind.String && w.GetString().Length > 0)
                    ks.Add(w.GetString());
            if (ks.Count > 0) it.WriteKeys = ks.ToArray();
        }
        it.LinkedSuffix = GetS(ie, "linkedSuffix");
        if (ie.TryGetProperty("vis", out var ve) && ve.ValueKind == JsonValueKind.Object)
        {
            var vk = GetS(ve, "key"); var vv = GetS(ve, "value");
            if (!string.IsNullOrEmpty(vk) && !string.IsNullOrEmpty(vv)) it.Vis = new McVis { Key = vk, Value = vv };
        }
        if (ie.TryGetProperty("cabs", out var ce) && ce.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in ce.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                var id = GetN(c, "id"); var nm = GetS(c, "name");
                if (id == null || string.IsNullOrEmpty(nm)) continue;
                it.Cabs ??= new List<McCab>();
                it.Cabs.Add(new McCab { Id = (long)id.Value, Name = nm, Native = GetN(c, "native") ?? 0 });
            }
        }
    }

    /// <summary>def 兜底值：number→double / string / bool；其他形态（对象/数组/null）不兜底。</summary>
    private static object ParseDef(JsonElement ie)
    {
        if (!ie.TryGetProperty("def", out var de)) return null;
        return de.ValueKind switch
        {
            JsonValueKind.Number => de.TryGetInt64(out var l) && l >= int.MinValue && l <= int.MaxValue
                ? (object)(int)l  // int 型常见，落 int 便于反射口径一致；超界回 double
                : de.GetDouble(),
            JsonValueKind.String => de.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>是否出现任一 mcSpec 2 扩展字段（en/usage/enabled/lists 及 item 级 writeKeys/vis/cabs 等）——
    /// 非套件 v2 清单的"搁置"提示判据（一次性，解析期）。</summary>
    private static bool HasExtFields(JsonElement root)
    {
        foreach (var p in new[] { "en", "usage", "enabled", "lists" })
            if (root.TryGetProperty(p, out _)) return true;
        if (!root.TryGetProperty("groups", out var gs) || gs.ValueKind != JsonValueKind.Array) return false;
        foreach (var g in gs.EnumerateArray())
        {
            if (!g.TryGetProperty("items", out var isEl) || isEl.ValueKind != JsonValueKind.Array) continue;
            foreach (var it in isEl.EnumerateArray())
                foreach (var p in new[] { "type", "def", "opts", "writeKeys", "linkedSuffix", "vis", "cabs" })
                    if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty(p, out _)) return true;
        }
        return false;
    }

    // ============ 合并细节 ============

    /// <summary>清单项 → ItemInfo：类型/当前值/选项来自 cfg entry 反射（Tier 0 同源），
    /// label/note/badge/min-max-step 按清单覆盖（缺省回落 Tier 0 推断值）。</summary>
    private static ItemInfo BuildItem(McItem it, string section, string key, ConfigEntryBase e)
    {
        var ii = new ItemInfo
        {
            K = section + "." + key,
            L = string.IsNullOrEmpty(it.Label) ? key : it.Label, // 缺 label 回落键名（Tier 0 同形）
            B = NormBadge(it.Badge),
            T = ConfigBridge.TypeName(e.SettingType),
            V = e.BoxedValue,
            Note = it.Note ?? e.Description?.Description, // 清单定制 note 优先于 entry Description
        };
        ConfigBridge.ApplyRange(ii, e); // AcceptableValueRange/List 反射（缺省范围来源）
        if (it.Min != null) ii.Min = it.Min; // 清单范围覆盖反射值（指南：缺省才用 AcceptableValueRange）
        if (it.Max != null) ii.Max = it.Max;
        if (ii.T == "sel" && ii.Opts == null && e.SettingType == typeof(string)) ii.T = "str"; // Tier 0 同款
        if (it.Step != null) ii.St = it.Step;
        else if (ii.St == null && ii.Min != null) ii.St = ii.T == "int" ? 1 : 0.1; // tp 步进兜底同 Tier 0
        return ii;
    }

    /// <summary>徽标归一：指南 now/reload/cold（§六）→ 面板契约 now/reload/reboot（cold=重启后）；
    /// 缺省/未知 → cfg（诚实缺省，指南 §六）。</summary>
    private static string NormBadge(string b)
    {
        if (string.IsNullOrEmpty(b)) return "cfg";
        return b.Trim().ToLowerInvariant() switch
        {
            "now" => "now",
            "reload" => "reload",
            "cold" => "reboot",
            "cfg" => "cfg",
            _ => "cfg",
        };
    }

    /// <summary>拆 "Section:Key"（首个冒号；无冒号=空节全串为键——容忍裸键写法）。</summary>
    private static bool Split(string k, out string section, out string key)
    {
        section = ""; key = "";
        if (string.IsNullOrEmpty(k)) return false;
        var ix = k.IndexOf(':');
        if (ix < 0) { key = k; return true; }
        if (ix == 0 || ix == k.Length - 1) return false; // "Section:" / ":Key" 畸形 → 隐藏
        section = k.Substring(0, ix);
        key = k.Substring(ix + 1);
        return true;
    }

    private static string GetS(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetN(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static string Raw(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number || e.ValueKind == JsonValueKind.String ? e.GetRawText() : "缺失";

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}
