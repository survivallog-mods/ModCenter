using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 第三方 dll 重启级禁用——意图层（任务 16 建立；任务 4 起执行点迁移）：
///
/// - **marker**：ModCenter 自身 cfg `[DisabledTp]` 节，key = plugins 目录下 dll 相对名
///   （如 <c>BetterTrade.dll</c>——dll 被禁用后插件不在链上，GUID 无从解析，故按文件名记账；
///   T6/T14 时代按 GUID 的 `[Disabled]` 名单随之退役，残留孤儿行见任务 16 报告清理记录）。
///   value=true 表示停用意图；懒 Bind 幂等，BepInEx Save 只写现役 Bind 项。
/// - **执行点=启动清理器（T4）**：v1 的"退出时分离进程改名"机械整体退役（OnQuit/
///   SpawnDisableBatch/SpawnReverse/强杀残留补拉/残留 toast 全删；通用分离进程启动器迁
///   <see cref="RestartHelper"/> 供 T10/T13 复用）。MCCleaner 在 patcher 阶段（先于插件加载，
///   dll 无锁）读 cfg marker 翻译为 disable/enable op 当场改名（CleanerPatcher.TranslateMarkers，
///   清理器侧独立行级解析）。强杀残留概念随之消失：意图在 cfg，每次启动先于加载执行。
/// - **本类职责（纯意图写入 + 面板真值源）**：Disable/Enable 写 marker；MarkerOf 供快照取反显示；
///   Enable 的即时反向改名保留（dll 未加载不锁，直接 File.Move），失败不再分离进程兜底——
///   下次启动清理器按 marker 重译自愈。ScanOnLoad 仍做 Load 时对照：marker=true 但 dll 在场
///   = 清理器异常没跑 → 登记残留供 Tier 2 软停重放（ReapplySoft，插件本会话仍在链上）。
/// - **快照缺席行**：plugins 里存在 <c>*.dll.disabled</c>（非第一方）→ 面板补一行 enabled=false
///   的"仅启停"行（guid=dll 相对名），供玩家重新启用（反向改名）。
/// 线程纪律：全部入口都在 Ticker 主线程（Load/Update/Pump）。
/// </summary>
internal static class DllDisabler
{
    private const string Section = "DisabledTp";
    private const string Suffix = ".disabled";

    // Load 检出的"marker=true 但 dll 未改名"（=启动清理器没跑成，插件本会话仍在链上）——软停重放枚举源
    private static readonly List<string> Residue = new();

    private static void Log(string msg) => ModCenterPlugin.Log(msg);

    // ============ 对外契约（ConfigBridge / ModCenterPlugin 消费） ============

    /// <summary>插件 dll 相对名（plugins 目录相对；Location 不在 plugins 下时退化为文件名）。</summary>
    internal static string DllRelOf(BepInEx.PluginInfo info)
    {
        try
        {
            var loc = info?.Location;
            if (string.IsNullOrEmpty(loc)) return null;
            var full = Path.GetFullPath(loc);
            var root = Paths.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length + 1);
            return Path.GetFileName(full);
        }
        catch { return null; }
    }

    /// <summary>marker 当前值（懒 Bind，读取已停用意图；Bind 失败按未停用兜底）。</summary>
    internal static bool MarkerOf(string dllRel)
    {
        try
        {
            var mk = Marker(dllRel);
            return mk != null && mk.Value;
        }
        catch { return false; }
    }

    /// <summary>停用：写 marker=true（DisabledTp）——下次启动由 MCCleaner 翻译执行改名（T4）。</summary>
    internal static void Disable(string dllRel)
    {
        var mk = Marker(dllRel);
        if (mk == null) { Log("toggle 拒绝：marker entry 创建失败 " + dllRel); return; }
        MarkDirtySelf();
        mk.Value = true;
    }

    /// <summary>
    /// 启用：若 dll 已被改名则即时反向改名（未加载不锁，直接 Move；失败不兜底——下次启动
    /// 由 MCCleaner 按 marker=false 重译自愈，T4）→ marker=false。
    /// </summary>
    internal static void Enable(string dllRel)
    {
        var disabledPath = Path.Combine(Paths.PluginPath, dllRel + Suffix);
        try
        {
            if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, Path.Combine(Paths.PluginPath, dllRel));
                Log($"enable: dll 已恢复 {dllRel}（未加载不锁，直接反向改名）");
            }
        }
        catch (Exception e)
        {
            Log($"warn enable 直接改名失败（下次启动由 MCCleaner 按 marker 重译自愈）: {e.Message}");
        }
        try
        {
            var mk = Marker(dllRel);
            if (mk != null && mk.Value) { MarkDirtySelf(); mk.Value = false; }
        }
        catch (Exception e) { Log("warn enable marker 清除异常: " + e.Message); }
    }

    /// <summary>Load 时对照（ModCenterPlugin.Load 末尾调）：cfg [DisabledTp] true 项 × plugins 目录。
    /// v2 语义（T4）：改名执行在启动清理器——正常路径此处 dll 均已改名（.disabled 在场，仅预 Bind
    /// 保 marker 不被 BepInEx Save 丢键）；marker=true 但 dll 仍在场 = 清理器异常没跑 → 登记
    /// Residue 供 Tier 2 软停重放（插件本会话仍在链上），不再补拉改名、无残留 toast。</summary>
    internal static void ScanOnLoad()
    {
        try
        {
            foreach (var dllRel in ParseMarkerTrues())
            {
                var dll = Path.Combine(Paths.PluginPath, dllRel);
                var disabled = dll + Suffix;
                if (File.Exists(disabled))
                {
                    Marker(dllRel); // 预 Bind：缺席行/再启用路由就绪（BepInEx Save 只写已 Bind 键）
                    Log($"dll禁用 scan: {dllRel} 已停用（.disabled 在场）——面板将显示停用行");
                }
                else if (File.Exists(dll))
                {
                    Marker(dllRel); // 预 Bind 同上
                    Residue.Add(dllRel);
                    Log($"dll禁用 scan: {dllRel} marker=停用但 dll 在场（启动清理器未执行改名？）"
                        + " → 登记软停重放（Tier 2），改名待下次启动清理器自愈");
                }
                else
                {
                    Log($"dll禁用 scan: marker {dllRel} 对应 dll/.disabled 均不在场（惰性忽略）");
                }
            }
        }
        catch (Exception e) { Log("warn dll禁用 scan 异常: " + e.Message); }
    }

    /// <summary>残留软停重放（Ticker 首帧调——此时 Chainloader 全部插件已就绪）：Tier 2 者重申 OnEnabledChanged(false)。
    /// 场景（T4）：marker=true 且 dll 仍在场（清理器异常没跑）→插件在场仍需软停；
    /// 清理器正常已改名→插件不加载→本表自然为空。</summary>
    internal static void ReapplySoft()
    {
        foreach (var dllRel in Residue.ToList())
        {
            try
            {
                var info = FindLoadedByDllRel(dllRel);
                if (info == null) continue;
                var ok = ThirdParty.FireEnabledChanged(info, false);
                Log($"dll禁用 residue 软停重放 {dllRel}: "
                    + (ok ? "OnEnabledChanged(false) 已处理（本会话行为已停止，下次启动 dll 禁用）"
                          : "无 Tier 2 契约/已降级（重启级语义，本会话对方仍在运行）"));
            }
            catch (Exception e) { Log("warn residue 软停重放异常: " + e.Message); }
        }
    }

    /// <summary>plugins 目录 .disabled 扫描（快照缺席行源；第一方 SurvivalLog.* 不进第三方组）。
    /// **递归**（评审 P2：BepInEx 递归加载 plugins 子目录，禁用后的 <c>Pack\Mod.dll.disabled</c>
    /// 在子目录——非递归扫不到→缺席行永远不出现）；返回 plugins 相对名（与 marker/启用路径拼接同源）。</summary>
    internal static List<string> AbsentDisabled()
    {
        var list = new List<string>();
        try
        {
            var root = Paths.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (!name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var dllRel = name.Substring(0, name.Length - Suffix.Length);
                if (!dllRel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || dllRel.StartsWith("SurvivalLog.", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = f.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? f.Substring(root.Length + 1) : name; // 相对名（子目录含路径，与 DllRelOf 同源）
                if (!rel.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                rel = rel.Substring(0, rel.Length - Suffix.Length); // 剥 .disabled——marker/再启用路由用 dll 原名
                if (IsSafeRelName(rel)) list.Add(rel);
            }
        }
        catch (Exception e) { Log("warn .disabled 扫描异常: " + e.Message); }
        return list;
    }

    /// <summary>缺席行启停路由（快照行 guid=dll 相对名）：只有启用方向（该行本就停用）。</summary>
    internal static bool TrySetAbsent(string id, bool enabled)
    {
        try
        {
            if (!File.Exists(Path.Combine(Paths.PluginPath, id + Suffix))) return false; // 非缺席行
            if (!enabled) { Log("toggle 拒绝：该 mod 已是停用状态 " + id); return true; }
            Enable(id);
            Log($"toggle(tp-absent) {id} → 启用（dll 反向改名恢复，下次启动加载）");
            return true;
        }
        catch (Exception e) { Log("warn tp-absent 启停异常: " + e.Message); return true; }
    }

    // ============ 内部实现 ============

    private static ConfigEntry<bool> Marker(string dllRel)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) return null;
        return self.Config.Bind(Section, dllRel, false,
            "第三方 mod 停用标记（true=已停用：下次启动由启动清理器改名 .disabled，不加载）。key = plugins 目录下 dll 相对名。");
    }

    private static void MarkDirtySelf()
    {
        var self = ModCenterPlugin.Instance;
        if (self != null) ConfigBridge.MarkDirty(self.Config);
    }

    /// <summary>解析自身 cfg [DisabledTp] true 项（Load 时对照的枚举源——未 Bind 键无 API 枚举）。</summary>
    private static List<string> ParseMarkerTrues()
    {
        var trues = new List<string>();
        var path = ModCenterPlugin.Instance?.Config?.ConfigFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return trues;
        var cur = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
            {
                cur = line.Substring(1, line.Length - 2).Trim();
                continue;
            }
            if (!string.Equals(cur, Section, StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line.Substring(0, eq).Trim();
            var v = line.Substring(eq + 1).Trim();
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase) && k.Length > 0) trues.Add(k);
        }
        return trues;
    }

    private static BepInEx.PluginInfo FindLoadedByDllRel(string dllRel)
    {
        try
        {
            var plugins = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance?.Plugins;
            if (plugins == null) return null;
            foreach (var info in plugins.Values)
                if (string.Equals(DllRelOf(info), dllRel, StringComparison.OrdinalIgnoreCase)) return info;
        }
        catch { }
        return null;
    }

    /// <summary>相对名边界守卫（评审 P3-1：非字符白名单——只剔能越出 plugins 目录的形态：
    /// 空/绝对路径（盘符或反斜杠开头）/ 含 .. 段）。</summary>
    private static bool IsSafeRelName(string n)
    {
        if (string.IsNullOrEmpty(n) || n.IndexOf(':') >= 0
            || n[0] == '\\' || n[0] == '/') return false;
        foreach (var seg in n.Split('\\', '/'))
            if (seg == "..") return false;
        return true;
    }
}
