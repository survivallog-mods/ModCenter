using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 补丁 owner 归属面（v2 任务 12）：owner 原始 Harmony instance id + 归属结果。
/// Guid=null 即"未知来源"（不臆造归属；卡片 who 里明确标注）。
/// </summary>
internal sealed class PatchOwnerInfo
{
    public string Id;      // Harmony instance id（原样）
    public string Guid;    // 归属到的已装插件 GUID；null=未知来源
    public string Display; // 面板显示串（套件中文名（En） / 第三方插件名（GUID） / 未知来源（id））
}

/// <summary>
/// 一条 L2 碰撞记录（同一游戏方法 ≥2 个 owner）。Method=短类型名.方法名（面板可读），
/// Key=完整类型全名::方法名（日志/去重）；PatchCount=补丁实例数（prefix/postfix/transpiler/
/// finalizer/ilmanipulator 之和——同 owner 多个补丁实例也如实计数）。
/// </summary>
internal sealed class PatchCollision
{
    public string Key;
    public string TypeFull;
    public string Assembly;
    public string Method;
    public string MethodName;
    public int PatchCount;
    public bool HasAttributed;
    public List<PatchOwnerInfo> Owners = new();

    /// <summary>who 显示用的 owner 串（保持 owner 原序）。</summary>
    internal List<string> Who()
    {
        var list = new List<string>();
        foreach (var o in Owners) list.Add(o.Display);
        return list;
    }
}

/// <summary>一轮扫描结果（快照/弹窗/汇总共用同一实例，杜绝口径漂移）。</summary>
internal sealed class PatchScanResult
{
    public long At;             // Unix 毫秒（面板"扫描于 …"）
    public int PatchedMethods;  // Harmony.GetAllPatchedMethods() 总数
    public int InScope;         // 游戏侧已挂补丁方法数（全部 owner 数，含单 owner；评审 I-1）
    public int Skipped;         // 非游戏侧已挂补丁方法数（框架/引擎/插件内部方法，全部 owner 数）
    public int SingleOwner;     // 单 owner 方法数（非碰撞；两 scope 合计，仅日志）
    public int MultiOwnerGame;  // 游戏侧 ≥2 owner 组数（= Items + Unknown）
    public int MultiOwnerFramework; // 非游戏侧 ≥2 owner 组数（裁定 A：汇总必须披露，不铺行）
    public long Ms;             // 扫描耗时（性能证据：>50ms 则改后台，裁定 1）
    public string Error;        // 扫描异常（null=正常）
    public string Scope = "游戏侧（HotUpdate 程序集 / GameCore.* 命名空间）";
    public List<PatchCollision> Items = new();   // 游戏侧 ≥1 个 owner 可归属（行内橙标 / 详情卡）
    public List<PatchCollision> Unknown = new(); // 游戏侧全部 owner 未知来源（只进扫描汇总）
    public List<PatchCollision> Framework = new(); // 非游戏侧多 owner（只进汇总披露，裁定 A）
    public int Total => Items.Count + Unknown.Count;
}

/// <summary>
/// 体检 L2：运行时补丁碰撞实测（v2 任务 12）。
/// 数据源=Harmony.GetAllPatchedMethods() → Harmony.GetPatchInfo(method).Owners（HarmonyX
/// 2.10.2 的 Patches.Owners / Patch.owner 双路径，探针实证见 DESIGN.md「L2 补丁碰撞实证记录」）。
/// 归组规则：同一方法 ≥2 个 owner 即成一条；命名空间过滤=只看游戏侧方法（声明类型在
/// HotUpdate 程序集或 GameCore.* 命名空间），框架/引擎/Vuplex/插件自身内部方法不参与
/// （报告记录取舍）。owner→mod 归属（控制器裁定 2）：①精确匹配已装 GUID ②前缀匹配
/// （owner 以 GUID+'.' 开头，如 com.local.survivallog.robotspeed.trace）③其余归"未知来源"。
/// 行内橙标只挂"≥1 个 owner 可归属"的碰撞；全未知来源的碰撞只进扫描汇总（裁定 3）。
/// 统计口径（fix I-1）：InScope/Skipped 按"全部已挂补丁方法"计数（含单 owner），碰撞组数单独
/// 统计（MultiOwnerGame）；框架侧多 owner 组（MultiOwnerFramework/Framework）继续过滤不铺行，
/// 但汇总必须披露"框架侧 N 组未计入"（Ruling 14②，真实共挂不隐身）。
/// 扫描时机：面板打开（MC_OPEN）+ 手动刷新（MC_SCAN），主线程执行、不后台轮询（裁定 1）。
/// 探针：cfg Debug.PatchScan（默认关）启动 10s 后 dump PatchInfo 真实形态到
/// BepInEx/ModCenter_patchscan.txt（硬规则 9：不依赖 LogOutput）。
/// </summary>
internal static class PatchScan
{
    private static PatchScanResult _last;

    // 游戏侧判定：游戏主托管程序集名（BepInEx/interop/HotUpdate.dll）+ 命名空间前缀双保险
    private const string GameAssembly = "HotUpdate";
    private const string GameNamespacePrefix = "GameCore.";

    /// <summary>最近一轮结果（未扫描时惰性扫一次；BuildSnapshot 兜底用）。</summary>
    internal static PatchScanResult Current => _last ??= Scan();

    /// <summary>立即重扫（MC_OPEN/MC_SCAN 主线程调用；顺带记录耗时）。</summary>
    internal static PatchScanResult Refresh()
    {
        _last = Scan();
        Log($"patchscan: {_last.PatchedMethods} 个已挂补丁方法 → 游戏侧 {_last.InScope} 个（非游戏侧 {_last.Skipped} 个） → " +
            $"游戏侧碰撞 {_last.Total} 组（可归属 {_last.Items.Count} / 未知来源 {_last.Unknown.Count}），" +
            $"框架侧 {_last.MultiOwnerFramework} 组未计入，{_last.Ms}ms" +
            (_last.Error != null ? "（异常：" + _last.Error + "）" : ""));
        return _last;
    }

    /// <summary>该 mod 的碰撞组（owner 归属到该 GUID 的组；无归属=空表）。</summary>
    internal static List<PatchCollision> CollisionsFor(string guid)
    {
        var list = new List<PatchCollision>();
        if (string.IsNullOrEmpty(guid)) return list;
        foreach (var c in Current.Items)
            foreach (var o in c.Owners)
                if (o.Guid != null && string.Equals(o.Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(c);
                    break;
                }
        return list;
    }

    /// <summary>行内橙标（每个 mod 一枚聚合；v3 tip=脚本碰撞 ×N + 对方显示名前 2 个）。
    /// 无碰撞=null。</summary>
    internal static IcInfo IcsFor(string guid)
    {
        var hits = CollisionsFor(guid);
        if (hits.Count == 0) return null;
        var others = new List<string>();
        for (var i = 0; i < hits.Count && others.Count < 2; i++) // 终审 F5：外层同守卫，最多 2 个对方
            foreach (var o in hits[i].Owners)
            {
                if (o.Guid != null && string.Equals(o.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
                if (others.IndexOf(o.Display) < 0) others.Add(o.Display);
                if (others.Count >= 2) break;
            }
        var names = string.Join("、", others);
        return new IcInfo
        {
            K = "orange",
            Click = "cf",
            Tip = "脚本碰撞 ×" + hits.Count + (names.Length > 0 ? "：与" + names : "") + "，点看详情",
        };
    }

    /// <summary>面板摘要事件体（MC_SCAN_DATA；游戏侧碰撞全量汇总 + 框架侧未计入披露）。</summary>
    internal static PatchScanData BuildData()
    {
        var r = Current;
        var d = new PatchScanData
        {
            At = r.At, Patched = r.PatchedMethods, Game = r.InScope,
            Total = r.Total, Routed = r.Items.Count, Unknown = r.Unknown.Count,
            Framework = r.MultiOwnerFramework,
            Ms = r.Ms, Scope = r.Scope, Error = r.Error,
        };
        foreach (var c in r.Items) d.Items.Add(ToItem(c));
        foreach (var c in r.Unknown) d.Items.Add(ToItem(c));
        foreach (var c in r.Framework) d.FrameworkItems.Add(ToItem(c));
        return d;
    }

    private static PatchScanItem ToItem(PatchCollision c) => new()
    {
        Method = c.Method,
        Patches = c.PatchCount,
        Unknown = !c.HasAttributed,
        Owners = c.Who(),
    };

    // ============ 扫描实现 ============

    /// <summary>一轮扫描（主线程；Harmony 静态账本无锁保护，全部调用点都在 Ticker/Pump 主线程）。</summary>
    internal static PatchScanResult Scan()
    {
        var sw = Stopwatch.StartNew();
        var res = new PatchScanResult { At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        List<(string Guid, string Name)> installed;
        try { installed = InstalledPlugins(); }
        catch (Exception e)
        {
            installed = new List<(string, string)>();
            res.Error = "插件枚举异常: " + e.GetType().Name + " " + e.Message;
        }

        List<MethodBase> methods;
        try { methods = new List<MethodBase>(Harmony.GetAllPatchedMethods()); }
        catch (Exception e)
        {
            res.Error = "GetAllPatchedMethods 异常: " + e.GetType().Name + " " + e.Message;
            res.Ms = sw.ElapsedMilliseconds;
            Log("patchscan 失败: " + res.Error);
            return res;
        }
        res.PatchedMethods = methods.Count;

        foreach (var m in methods)
        {
            try
            {
                Patches pi;
                try { pi = Harmony.GetPatchInfo(m); }
                catch { continue; }
                if (pi == null) continue;

                var owners = OwnersOf(pi);
                if (owners.Count == 0) continue;
                // I-1 修正：scope 计数按"全部已挂补丁方法"统计（单 owner 也计入），
                // 与多 owner 碰撞组数分开——面板"游戏侧 G/P 方法"不再等于碰撞组数。
                // 计数缺口说明：GetPatchInfo 抛异常或返回 null 的方法（账本与读取不一致，实测 0 例）
                // 不进入 InScope/Skipped 计数；口径=可读取 PatchInfo 的已挂补丁方法。
                var inScope = InScope(m);
                if (inScope) res.InScope++; else res.Skipped++;
                if (owners.Count < 2) { res.SingleOwner++; continue; }

                var t = m.DeclaringType;
                var asm = "";
                try { asm = t?.Assembly?.GetName()?.Name ?? ""; } catch { }
                var col = new PatchCollision
                {
                    Key = (t?.FullName ?? "?") + "::" + m.Name,
                    TypeFull = t?.FullName ?? "?",
                    Assembly = asm,
                    Method = (t?.Name ?? "?") + "." + m.Name,
                    MethodName = m.Name,
                    PatchCount = CountPatches(pi),
                };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var oid in owners)
                {
                    if (!seen.Add(oid)) continue;
                    var (guid, name) = Attribute(oid, installed);
                    col.Owners.Add(new PatchOwnerInfo
                    {
                        Id = oid,
                        Guid = guid,
                        Display = DisplayOf(oid, guid, name),
                    });
                }
                col.HasAttributed = col.Owners.Any(o => o.Guid != null);
                if (inScope)
                {
                    res.MultiOwnerGame++;
                    if (col.HasAttributed) res.Items.Add(col);
                    else res.Unknown.Add(col);
                }
                else
                {
                    // 裁定 A（Ruling 14②）：框架侧多 owner 组不铺行/不计入碰撞总数，但汇总必须披露
                    res.MultiOwnerFramework++;
                    res.Framework.Add(col);
                }
            }
            catch (Exception e)
            {
                Log("warn patchscan 单方法跳过 " + SafeName(m) + ": " + e.Message);
            }
        }
        res.Items.Sort((a, b) => string.Compare(a.Method, b.Method, StringComparison.OrdinalIgnoreCase));
        res.Unknown.Sort((a, b) => string.Compare(a.Method, b.Method, StringComparison.OrdinalIgnoreCase));
        res.Framework.Sort((a, b) => string.Compare(a.Method, b.Method, StringComparison.OrdinalIgnoreCase));
        res.Ms = sw.ElapsedMilliseconds;
        return res;
    }

    /// <summary>owner 双路径收集：Patches.Owners（探针主路径）+ 各补丁实例 Patch.owner 字段
    /// （降级/补漏路径——探针实测两条路径一致，此合并防 HarmonyX 版本差异）。去重保序。</summary>
    private static List<string> OwnersOf(Patches pi)
    {
        var list = new List<string>(4);
        void Add(string o)
        {
            if (string.IsNullOrEmpty(o)) return;
            foreach (var x in list)
                if (string.Equals(x, o, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(o);
        }
        try
        {
            var os = pi.Owners; // ReadOnlyCollection<string>（HarmonyX 2.10.2 实证）
            if (os != null) foreach (var o in os) Add(o);
        }
        catch { }
        AddPatchOwners(pi.Prefixes, Add);
        AddPatchOwners(pi.Postfixes, Add);
        AddPatchOwners(pi.Transpilers, Add);
        AddPatchOwners(pi.Finalizers, Add);
        try { AddPatchOwners(pi.ILManipulators, Add); } catch { }
        return list;
    }

    private static void AddPatchOwners(System.Collections.ObjectModel.ReadOnlyCollection<Patch> patches,
        Action<string> add)
    {
        try
        {
            if (patches == null) return;
            foreach (var p in patches) if (p != null) add(p.owner);
        }
        catch { }
    }

    private static int CountPatches(Patches pi)
    {
        var n = 0;
        try { if (pi.Prefixes != null) n += pi.Prefixes.Count; } catch { }
        try { if (pi.Postfixes != null) n += pi.Postfixes.Count; } catch { }
        try { if (pi.Transpilers != null) n += pi.Transpilers.Count; } catch { }
        try { if (pi.Finalizers != null) n += pi.Finalizers.Count; } catch { }
        try { if (pi.ILManipulators != null) n += pi.ILManipulators.Count; } catch { }
        return n;
    }

    /// <summary>游戏侧过滤：声明类型来自游戏主 interop 程序集（HotUpdate）或 GameCore.* 命名空间。
    /// 框架（BepInEx/HarmonyLib/MonoMod/Il2CppInterop）、引擎（UnityEngine/Vuplex）、第三方与
    /// 套件自身类型不参与碰撞统计（报告记录取舍：它们不是"游戏方法"）。</summary>
    private static bool InScope(MethodBase m)
    {
        var t = m.DeclaringType;
        if (t == null) return false;
        var asm = "";
        try { asm = t.Assembly?.GetName()?.Name ?? ""; } catch { }
        if (string.Equals(asm, GameAssembly, StringComparison.Ordinal)) return true;
        var ns = t.Namespace ?? "";
        return ns.StartsWith(GameNamespacePrefix, StringComparison.Ordinal);
    }

    private static string SafeName(MethodBase m)
    {
        try { return (m.DeclaringType?.FullName ?? "?") + "::" + m.Name; } catch { return "?"; }
    }

    /// <summary>已装插件（GUID, Name）快照（主线程枚举 IL2CPPChainloader）。</summary>
    private static List<(string Guid, string Name)> InstalledPlugins()
    {
        var list = new List<(string, string)>();
        var plugins = IL2CPPChainloader.Instance?.Plugins;
        if (plugins == null) return list;
        foreach (var p in plugins.Values)
        {
            var md = p?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            list.Add((md.GUID, md.Name));
        }
        return list;
    }

    /// <summary>owner→mod 归属（裁定 2）：①精确 GUID 全表 ②前缀（owner 以 GUID+'.' 开头）全表
    /// ③未知来源。两遍扫描保证精确匹配永远优先于前缀（如已装 a.b 与 a.b.c 时 owner=a.b.c 归 a.b.c）。</summary>
    private static (string Guid, string Name) Attribute(string owner, List<(string Guid, string Name)> installed)
    {
        foreach (var (g, n) in installed)
            if (string.Equals(owner, g, StringComparison.OrdinalIgnoreCase)) return (g, n);
        foreach (var (g, n) in installed)
            if (owner.Length > g.Length + 1
                && owner.StartsWith(g, StringComparison.OrdinalIgnoreCase)
                && owner[g.Length] == '.')
                return (g, n);
        return (null, null);
    }

    /// <summary>owner 显示串：Meta 中文名（含 CabinetBurden/ModCenter 附加表）优先 → 第三方
    /// 插件自报名（GUID）→ 未知来源（id）。</summary>
    private static string DisplayOf(string ownerId, string guid, string pluginName)
    {
        if (guid == null) return "未知来源（" + ownerId + "）";
        var meta = Meta.DisplayNameOf(guid);
        if (!string.IsNullOrEmpty(meta) && !string.Equals(meta, guid, StringComparison.OrdinalIgnoreCase))
            return meta;
        if (!string.IsNullOrEmpty(pluginName) && !string.Equals(pluginName, guid, StringComparison.OrdinalIgnoreCase))
            return pluginName + "（" + guid + "）";
        return guid;
    }

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}

/// <summary>
/// L2 补丁碰撞自诊断探针（cfg Debug.PatchScan 门控，Ticker 10s 后拉起，一轮即终态；仿 NetProbe/
/// RepoProbe）。实证点：Il2CppInterop 挂的游戏方法补丁，PatchInfo.patchMethods/owners 是否可读、
/// 能否按 owner 归组。证据追加 BepInEx/ModCenter_patchscan.txt：全量已挂补丁方法清单（owners/
/// 补丁数/声明类型/程序集）+ 第一方套件挂点全量（上限 10，现有 9 个）+ 常用游戏方法补足的
/// PatchInfo 逐字段 dump（Owners 属性、
/// Patch.owner 字段、反射路径、GetAllPatchedMethods 身份、IL2CPP GetMethodBody 降级面）+
/// 产品扫描器 dry-run 结果与耗时。Ticker 主线程只收割 Done 旗。
/// </summary>
internal static class PatchScanProbe
{
    internal static volatile bool Done;
    internal static string Summary = "";

    private static string EvidencePath => System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_patchscan.txt");

    private static void P(string line)
    {
        try { System.IO.File.AppendAllText(EvidencePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        ModCenterPlugin.Log("patchscan-probe " + line);
    }

    /// <summary>Ticker 主线程调：清旧证据 → 写头行 → 同线程 dump（Harmony 账本主线程读，裁定 1）。</summary>
    public static void Start()
    {
        Done = false;
        Summary = "";
        try { if (System.IO.File.Exists(EvidencePath)) System.IO.File.Delete(EvidencePath); } catch { }
        var harmonyVer = "?";
        try { harmonyVer = typeof(Harmony).Assembly.FullName; } catch { }
        P($"probe start: harmony='{harmonyVer}' runtime={Environment.Version} pid={Environment.ProcessId}");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            P("FAIL " + e.GetType().Name + ": " + e.Message);
            Summary = "FAIL " + e.GetType().Name + "（探针一轮终态）";
        }
        finally { Done = true; }
    }

    private static void Run()
    {
        var sw = Stopwatch.StartNew();
        List<MethodBase> all;
        try { all = new List<MethodBase>(Harmony.GetAllPatchedMethods()); }
        catch (Exception e)
        {
            P("FAIL GetAllPatchedMethods: " + e.GetType().Name + " " + e.Message);
            Summary = "FAIL GetAllPatchedMethods（探针一轮终态）";
            return;
        }
        var enumMs = sw.ElapsedMilliseconds;
        P($"GetAllPatchedMethods: {all.Count} 个方法（枚举 {enumMs}ms）");

        // ---- ① 全量清单（最多 240 行）：声明类型/程序集/owner 数/owner 原串/补丁数 ----
        var multi = 0;
        // M-6 修正（评审 round 2）：重点 dump 先保证第一方套件 owner 方法，再用常用游戏方法补足；
        // 候选上限从 8 提到 10（现有第一方挂点 9 个，避免再静默丢样本），总 dump 上限 12。
        const int SuiteSampleCap = 10;
        const int DumpCap = 12;
        var suiteCandidates = new List<MethodBase>();
        var targetCandidates = new List<MethodBase>();
        var targetNames = new[]
        {
            "BagComponent", "EndlessManager", "SurvivalPlanningComponent", "SurvivalResultsManager",
            "WebUIVm", "GameTimeManager", "Ac_ToolTable_RefreshBag", "FurnitureTagRules",
            "WebUILayer", "ItemManager", "ConfigManager",
        };
        var i = 0;
        foreach (var m in all)
        {
            i++;
            Patches pi = null;
            try { pi = Harmony.GetPatchInfo(m); } catch { }
            var owners = new List<string>();
            var patchCount = -1;
            if (pi != null)
            {
                try { if (pi.Owners != null) owners.AddRange(pi.Owners); } catch { }
                patchCount = CountPatches(pi);
            }
            if (owners.Count >= 2) multi++;
            // M-6 修正（评审）：套件 owner 判定必须用第一方前缀 com.local.survivallog.*；
            // 第三方 GUID 也含 "survivallog" 字样（com.dev./com.ivmakk.survivallog.*），旧判定会把
            // 第三方方法当"套件挂点"挑进重点 dump，套件真实挂点反而被挤出。
            var isSuite = owners.Any(o => o != null
                && o.StartsWith("com.local.survivallog.", StringComparison.OrdinalIgnoreCase));
            var isTarget = TargetNameOf(m, targetNames);
            if (isSuite) suiteCandidates.Add(m);
            else if (isTarget) targetCandidates.Add(m);
            if (i <= 240)
                P($"#{i} decl={SafeFull(m)} asm={SafeAsm(m)} owners={owners.Count}[{string.Join("|", owners)}] " +
                  $"patches={patchCount} scope={(InScopeProbe(m) ? "game" : "other")} suite={isSuite} target={isTarget}");
        }
        if (all.Count > 240) P($"...（全量清单截断，共 {all.Count} 行，仅前 240 行入证据）");
        P($"owner 数 ≥2 的方法: {multi}");
        var suiteHits = new List<MethodBase>();
        foreach (var m in suiteCandidates)
        {
            if (suiteHits.Count >= SuiteSampleCap) break;
            suiteHits.Add(m);
        }
        foreach (var m in targetCandidates)
        {
            if (suiteHits.Count >= DumpCap) break;
            suiteHits.Add(m);
        }

        // ---- ② 套件已挂方法重点 dump（第一方全量优先 + target 补足）：PatchInfo 逐字段 + 多反射路径 ----
        P($"--- 重点方法 dump（{suiteHits.Count} 个：套件 owner 优先，常用游戏方法补足） ---");
        foreach (var m in suiteHits) DumpDetail(m);

        // ---- ③ 产品扫描器 dry-run（与探针同一 API；耗时=性能裁定依据） ----
        P("--- PatchScan.Scan() dry-run ---");
        var scanSw = Stopwatch.StartNew();
        var r = PatchScan.Scan();
        var scanMs = scanSw.ElapsedMilliseconds;
        P($"扫描耗时 {r.Ms}ms（stopwatch {scanMs}ms）；GetAllPatchedMethods={r.PatchedMethods} " +
          $"游戏侧={r.InScope} 非游戏侧={r.Skipped} 单owner={r.SingleOwner} " +
          $"游戏侧≥2owner={r.MultiOwnerGame} 框架侧≥2owner={r.MultiOwnerFramework}");
        foreach (var c in r.Items)
            P($"COLLIDE {c.Key} asm={c.Assembly} patches={c.PatchCount} owners=[" +
              string.Join(" | ", c.Owners.Select(o => o.Id + " => " + (o.Guid ?? "<未知>") + " / " + o.Display)) + "]");
        foreach (var c in r.Unknown)
            P($"UNKNOWN {c.Key} asm={c.Assembly} patches={c.PatchCount} owners=[" +
              string.Join(" | ", c.Owners.Select(o => o.Id)) + "]");
        foreach (var c in r.Framework)
            P($"FRAMEWORK_NOT_COUNTED {c.Key} asm={c.Assembly} patches={c.PatchCount} owners=[" +
              string.Join(" | ", c.Owners.Select(o => o.Id + " => " + (o.Guid ?? "<未知>") + " / " + o.Display)) + "]");
        P($"汇总: 游戏侧碰撞 {r.Total} 组（可归属 {r.Items.Count} / 全未知 {r.Unknown.Count}）" +
          $" + 框架侧 {r.MultiOwnerFramework} 组未计入（裁定 A 披露）");

        // ---- ④ 性能裁定 ----
        var verdict = r.Ms > 50 ? ">50ms → 需后台 Task（本次实现仍按主线程，见报告）" : "≤50ms → 主线程直接扫（裁定 1 前段）";
        P($"性能结论: {verdict}");
        Summary = $"DONE methods={r.PatchedMethods} game={r.InScope} other={r.Skipped} collisions={r.Total} " +
                  $"(routed={r.Items.Count} unknown={r.Unknown.Count}) framework={r.MultiOwnerFramework} {r.Ms}ms";
    }

    private static void DumpDetail(MethodBase m)
    {
        P("==== " + SafeFull(m) + " ====");
        try { P($"  type={m.DeclaringType?.FullName} asm={SafeAsm(m)} name={m.Name} " +
                $"static={m.IsStatic} bodySupported={BodySupported(m)}"); }
        catch (Exception e) { P("  meta 读取异常: " + e.Message); }
        Patches pi = null;
        try { pi = Harmony.GetPatchInfo(m); }
        catch (Exception e) { P("  GetPatchInfo 异常: " + e.GetType().Name + " " + e.Message); }
        if (pi == null) { P("  PatchInfo=null（不可读）"); return; }
        P($"  PatchInfo 类型={pi.GetType().FullName}");
        try
        {
            var os = pi.Owners;
            P($"  D1 Patches.Owners: count={os?.Count} [{string.Join("|", os ?? new System.Collections.ObjectModel.ReadOnlyCollection<string>(new List<string>()))}]");
        }
        catch (Exception e) { P("  D1 Patches.Owners 异常: " + e.GetType().Name + " " + e.Message); }
        DumpPatches("prefix", pi.Prefixes);
        DumpPatches("postfix", pi.Postfixes);
        DumpPatches("transpiler", pi.Transpilers);
        DumpPatches("finalizer", pi.Finalizers);
        try { DumpPatches("ilmanipulator", pi.ILManipulators); } catch (Exception e) { P("  ilmanipulator 读取异常: " + e.Message); }
        try
        {
            var prop = pi.GetType().GetProperty("Owners", BindingFlags.Public | BindingFlags.Instance);
            var val = prop?.GetValue(pi);
            var txt = val is System.Collections.IEnumerable en
                ? string.Join("|", en.Cast<object>().Select(o => Convert.ToString(o)))
                : Convert.ToString(val);
            P($"  D3 反射 GetProperty(Owners): {(prop == null ? "属性不存在" : "count=" + (val as System.Collections.ICollection)?.Count + " [" + txt + "]")}");
        }
        catch (Exception e) { P("  D3 反射读取异常: " + e.GetType().Name + " " + e.Message); }
        try
        {
            var again = Harmony.GetAllPatchedMethods().Contains(m);
            P($"  D4 GetAllPatchedMethods.Contains(this)={again}");
        }
        catch (Exception e) { P("  D4 身份比对异常: " + e.Message); }
    }

    private static void DumpPatches(string kind, System.Collections.ObjectModel.ReadOnlyCollection<Patch> patches)
    {
        try
        {
            if (patches == null || patches.Count == 0) { P($"  D2 {kind}=0"); return; }
            var parts = new List<string>();
            foreach (var p in patches)
            {
                var owner = "?";
                var pm = "?";
                try { owner = p?.owner ?? "<null>"; } catch (Exception e) { owner = "<异常:" + e.Message + ">"; }
                try
                {
                    var info = p?.PatchMethod;
                    pm = info == null ? "<null>" : (info.DeclaringType?.FullName ?? "?") + "." + info.Name;
                }
                catch (Exception e) { pm = "<异常:" + e.Message + ">"; }
                parts.Add($"owner={owner} patchMethod={pm}");
            }
            P($"  D2 {kind}={patches.Count}: {string.Join(" ;; ", parts)}");
        }
        catch (Exception e) { P($"  D2 {kind} 读取异常: " + e.GetType().Name + " " + e.Message); }
    }

    private static int CountPatches(Patches pi)
    {
        var n = 0;
        try { if (pi.Prefixes != null) n += pi.Prefixes.Count; } catch { }
        try { if (pi.Postfixes != null) n += pi.Postfixes.Count; } catch { }
        try { if (pi.Transpilers != null) n += pi.Transpilers.Count; } catch { }
        try { if (pi.Finalizers != null) n += pi.Finalizers.Count; } catch { }
        try { if (pi.ILManipulators != null) n += pi.ILManipulators.Count; } catch { }
        return n;
    }

    private static bool InScopeProbe(MethodBase m)
    {
        var t = m.DeclaringType;
        if (t == null) return false;
        var asm = SafeAsm(m);
        if (asm == "HotUpdate") return true;
        return (t.Namespace ?? "").StartsWith("GameCore.", StringComparison.Ordinal);
    }

    private static bool TargetNameOf(MethodBase m, string[] names)
    {
        try
        {
            var tn = m.DeclaringType?.Name ?? "";
            foreach (var n in names) if (string.Equals(tn, n, StringComparison.Ordinal)) return true;
        }
        catch { }
        return false;
    }

    private static string BodySupported(MethodBase m)
    {
        try { var b = m.GetMethodBody(); return b == null ? "null" : (b.GetILAsByteArray()?.Length + "B"); }
        catch (Exception e) { return e.GetType().Name; }
    }

    private static string SafeFull(MethodBase m)
    {
        try { return (m.DeclaringType?.FullName ?? "?") + "::" + m.Name; } catch { return "?"; }
    }

    private static string SafeAsm(MethodBase m)
    {
        try { return m.DeclaringType?.Assembly?.GetName()?.Name ?? ""; } catch { return ""; }
    }
}
