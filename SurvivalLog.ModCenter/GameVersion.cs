using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 本地游戏版本读取通道（ModCenter v2 生命周期任务 6，spec §6 开放点 2 收口——gamekb 告警门
/// D8 的本地侧）：为 <see cref="RepoDecision"/> 提供"可与 update.json.gamekb 快照名比对的本地基线"。
///
/// 两侧值形态（探针实证，2026-10-29，证据 BepInEx/ModCenter_gamever.txt）：
/// - 远端：gamekb = gamekb 快照目录名，形态 <c>YYYY-MM-DD_&lt;N&gt;</c>（release.ps1 只取
///   ^\d{4}-\d{2}-\d{2}_\d+$ 的最新快照），<c>N</c> = Steam buildid（快照 meta.json 的 buildid
///   字段与目录名后缀逐字一致，如 2026-10-23_26157732 ↔ buildid 26157732）。
/// - 本地候选四条：①interop UnityEngine.Application.version（主线程探针 dump）②Steam
///   appmanifest 的 buildid（文件侧）③游戏数据目录 Version.txt 复合版本（文件侧）④主程序
///   exe FileVersionInfo（文件侧，实测为 Unity 引擎版本号——弃用依据见探针证据）。
///
/// 通道定案：**②appmanifest 为主通道**——gameRoot\..\..\appmanifest_&lt;appid&gt;.acf 的
/// <c>"buildid" "N"</c> 与 gamekb 名的 <c>_N</c> 后缀**直接字符串相等比较，零映射表**；
/// ③Version.txt（"1.1/19611/…" → 复合 "1.1.19611" = 快照 meta.json 的 runtime_assets_version）
/// 为后备展示通道，Baseline 需"版本→快照名"对照表（占位空表，方案见任务 6 报告；发版管线
/// 生成后填 <see cref="AssetsToGamekb"/> 即启用）。全通道失败 → Baseline=null → 决策层
/// **不判 Warn、标注"未校验游戏版本"**（D8 照常放行）。
///
/// 线程纪律：<see cref="ReadLocal"/> 纯文件通道（无 interop 调用，任意线程可调；结果缓存）；
/// interop 候选①只在 <see cref="ProbeRead"/>（主线程探针）dump，不进基线。
/// </summary>
public static class GameVersion
{
    /// <summary>Steam App ID（spec §4.1 重启同款 steam://rungameid/4164790；appmanifest 文件名用）。</summary>
    public const int SteamAppId = 4164790;

    /// <summary>gamekb 通道探针证据文件（BepInEx 根，硬规则 9：不依赖 LogOutput）。</summary>
    internal static readonly string EvidencePath = Path.Combine(Paths.BepInExRootPath, "ModCenter_gamever.txt");

    /// <summary>
    /// "版本→快照名"对照表占位（通道③用）：键 = Version.txt 复合版本（如 "1.1.19611"），
    /// 值 = 该游戏构建对应的 gamekb 快照名（如 "2026-10-23_26157732"）。空表 = 通道③只能给
    /// Display 不能给 Baseline（不判 Warn）。生成方案（任务 6 报告定稿）：发版管线扫
    /// gamekb\*\meta.json 组装为插件内置表随版更新，或 release.ps1 直接在 update.json 增补
    /// buildid 字段（后者免表，二选一在 T8 前拍板）。
    /// </summary>
    private static readonly Dictionary<string, string> AssetsToGamekb = new();

    private static readonly Regex BuildidRe = new("\"buildid\"\\s*\"(\\d+)\"", RegexOptions.Compiled);

    private static Local _cached;

    /// <summary>
    /// 本地游戏版本读取结果。<see cref="Baseline"/> 非空 = gamekb 可比对（buildid 字符串）；
    /// null = 不可比对（通道失败或无法映射），决策层按"未校验游戏版本"降级（D8）。
    /// </summary>
    public sealed class Local
    {
        /// <summary>gamekb 可比基线（= Steam buildid 字符串，与快照名 <c>_N</c> 后缀相等比较）；null=不可比对。</summary>
        public string Baseline;

        /// <summary>人读本地游戏版本（如 "1.1.19611 (build 26157732)"）；全通道失败 = null。</summary>
        public string Display;

        /// <summary>命中通道（"appmanifest" / "versionfile" / "appmanifest+versionfile"）；全失败 = null。</summary>
        public string Channel;

        /// <summary>不可比对原因（通道失败/无法映射快照名）；可比对 = null。决策层 VerNote 消费。</summary>
        public string Note;

        /// <summary>不可比对实例工厂（探针 Step 4 强制通道失败 = 决策层收到此实例）。</summary>
        public static Local Unverified(string note) => new() { Baseline = null, Display = null, Channel = null, Note = note };
    }

    /// <summary>缓存读取（ReadLocal 首次调用前的 ambient 值；null = 尚未读取）。</summary>
    public static Local ReadCached() => _cached;

    /// <summary>
    /// 读本地游戏版本（纯文件通道，线程无关；结果缓存，重复调用零 IO）。通道顺序：
    /// ①appmanifest buildid（Baseline）→ ②Version.txt 复合版本（Display；其 Baseline 仅在
    /// 对照表命中时给出，占位空表 = 无）。全失败 → Baseline=null + Note=原因（D8 未校验降级）。
    /// </summary>
    public static Local ReadLocal()
    {
        if (_cached != null) return _cached;
        lock (typeof(GameVersion)) // 双检锁：缓存字段读写闭环（探针/决策并发首调各一次的场景）
        {
            if (_cached != null) return _cached;
            _cached = ReadCore(null);
            return _cached;
        }
    }

    /// <summary>
    /// 探针读取（主线程，cfg Debug.RepoSelfTest 门控内）：dump 四候选实测值 + 通道选型结论进
    /// <see cref="EvidencePath"/>，再走 <see cref="ReadLocal"/> 同款通道逻辑（刷新缓存）。
    /// 返回读取结果（探针把它交给决策轮次；失败路径同样实证）。
    /// </summary>
    internal static Local ProbeRead(Action<string> p)
    {
        try { if (File.Exists(EvidencePath)) File.Delete(EvidencePath); } catch { }
        P(p, "channel probe: 四候选实测（主线程）");
        P(p, $"gameRoot={Paths.GameRootPath}");

        // 候选①interop UnityEngine.Application.version（主线程才安全；仅 dump 不进基线）
        try
        {
            var av = UnityEngine.Application.version;
            P(p, $"①interop Application.version = '{av}'（引擎 PlayerSettings.bundleVersion；与 ③ Version.txt 首行对照）");
        }
        catch (Exception e) { P(p, $"①interop Application.version 读取异常: {e.GetType().Name} {e.Message}"); }

        // 候选②appmanifest buildid（定案主通道）——dump 在场性与值
        var acf = ManifestPath();
        P(p, $"②appmanifest 路径 = '{acf}'（在场={File.Exists(acf)}）");
        if (File.Exists(acf))
        {
            try { P(p, $"②appmanifest buildid = '{ReadBuildid(acf) ?? "(未解析到)"}'"); }
            catch (Exception e) { P(p, $"②appmanifest 读取异常: {e.GetType().Name} {e.Message}"); }
        }

        // 候选③游戏数据目录 Version.txt
        var vt = VersionTxtPath();
        if (File.Exists(vt))
        {
            try
            {
                var lines = File.ReadAllLines(vt);
                var composite = lines.Length >= 2 ? lines[0].Trim() + "." + lines[1].Trim() : null;
                P(p, $"③Version.txt = {string.Join(" / ", lines)} → 复合版本 '{composite}'（= 快照 meta.json runtime_assets_version 形态）");
            }
            catch (Exception e) { P(p, $"③Version.txt 读取异常: {e.GetType().Name} {e.Message}"); }
        }
        else P(p, $"③Version.txt 不在场: '{vt}'");

        // 候选④主程序 exe FileVersionInfo（预期 = Unity 引擎版本，弃用依据落证据）
        try
        {
            var exe = Path.Combine(Paths.GameRootPath, "SurvivalLog.exe");
            var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            P(p, $"④SurvivalLog.exe FileVersion='{fv.FileVersion}' ProductVersion='{fv.ProductVersion}'" +
                 "（Unity Player 版本号，非游戏内容版本 → 弃用）");
        }
        catch (Exception e) { P(p, $"④exe FileVersionInfo 异常: {e.GetType().Name} {e.Message}"); }

        // 正式读取（走 ReadLocal 同款逻辑并刷新缓存）
        lock (typeof(GameVersion)) { _cached = null; }
        var local = ReadLocal();
        P(p, $"channel 定案: channel='{local.Channel}' baseline='{local.Baseline}' display='{local.Display}' note='{local.Note}'");
        P(p, local.Baseline != null
            ? $"比对规则: gamekb 名最后 '_' 段全数字且 == buildid '{local.Baseline}' → 匹配（快照名形态 YYYY-MM-DD_<buildid>，release.ps1 L61 正则冻结）"
            : "比对规则: 基线不可用 → 决策层不判 Warn、标'未校验游戏版本'（D8 照常放行）");
        return local;
    }

    /// <summary>通道读取核心（p=null 为 ReadLocal 路径；带 p 时顺带 dump 中间结论）。</summary>
    private static Local ReadCore(Action<string> p)
    {
        string buildid = null, composite = null;
        var manifestOk = false;

        // 通道①（主）：appmanifest buildid——Steam 标准库布局 <library>\steamapps\common\<game> 的
        // gameRoot 上两级即 steamapps；非 Steam 布局/多库挂载点下文件不在场 → 通道自然失败（实证路径）
        try
        {
            var acf = ManifestPath();
            if (File.Exists(acf)) buildid = ReadBuildid(acf);
        }
        catch { buildid = null; }
        manifestOk = !string.IsNullOrEmpty(buildid);

        // 通道②（辅）：Version.txt 复合版本（Display 用；Baseline 仅对照表命中时给出）
        try
        {
            var vt = VersionTxtPath();
            if (File.Exists(vt))
            {
                var lines = File.ReadAllLines(vt);
                if (lines.Length >= 2) composite = lines[0].Trim() + "." + lines[1].Trim();
            }
        }
        catch { composite = null; }

        if (manifestOk)
        {
            var display = composite != null ? $"{composite} (build {buildid})" : $"build {buildid}";
            return new Local
            {
                Baseline = buildid,
                Display = display,
                Channel = composite != null ? "appmanifest+versionfile" : "appmanifest",
                Note = null,
            };
        }

        // appmanifest 不可用：Version.txt 有值但对照表占位 → 有版本、无基线（不可比对但不算全失败）
        if (composite != null)
        {
            var mapped = AssetsToGamekb.TryGetValue(composite, out var gk) ? gk : null;
            return new Local
            {
                Baseline = mapped,
                Display = composite,
                Channel = "versionfile",
                Note = mapped != null
                    ? null
                    : $"本地游戏版本 {composite} 无法映射 gamekb 快照名（appmanifest 通道不可用，对照表占位未启用；方案见任务 6 报告）",
            };
        }

        // 全通道失败（D8 失败路径）：Baseline=null + 原因 → 决策层标"未校验游戏版本"
        return new Local
        {
            Baseline = null,
            Display = null,
            Channel = null,
            Note = "游戏版本通道全部失败（appmanifest 与 Version.txt 均不可读）",
        };
    }

    /// <summary>gamekb 快照名 ↔ 本地基线比对。返回 null=匹配；非 null=不匹配原因文案
    /// （决策层放 Why，D8 告警不拦截）。前提：local.Baseline 非空（null 由决策层先行降级）。</summary>
    public static string GamekbMismatchReason(string gamekbName, Local local)
    {
        if (string.IsNullOrEmpty(gamekbName))
            return "远端未携带 gamekb 快照名（update.json 契约异常）";
        var i = gamekbName.LastIndexOf('_');
        var suffix = i >= 0 && i + 1 < gamekbName.Length ? gamekbName[(i + 1)..] : null;
        if (suffix == null || suffix.Length == 0 || !IsAllDigits(suffix))
            return $"gamekb 快照名「{gamekbName}」形态异常（期望 YYYY-MM-DD_<buildid>，后缀应全数字）";
        if (!string.Equals(suffix, local.Baseline, StringComparison.Ordinal))
            return $"远端构建依据 gamekb {gamekbName} ≠ 本地游戏基线 build {local.Baseline}（适配构建不一致，可能不兼容；告警不拦截）";
        return null;
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
            if (c < '0' || c > '9')
                return false;
        return true;
    }

    /// <summary>appmanifest 绝对路径：gameRoot 上两级的 steamapps\appmanifest_&lt;appid&gt;.acf。</summary>
    private static string ManifestPath()
    {
        var steamapps = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", ".."));
        return Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
    }

    /// <summary>游戏数据目录 Version.txt 绝对路径（Unity 标准布局 &lt;game&gt;_Data）。</summary>
    private static string VersionTxtPath()
    {
        var exe = Path.Combine(Paths.GameRootPath, "SurvivalLog.exe");
        var dataDir = Path.Combine(Paths.GameRootPath, "SurvivalLog_Data");
        return Path.Combine(Directory.Exists(dataDir) ? dataDir : Path.GetDirectoryName(exe), "Version.txt");
    }

    private static string ReadBuildid(string acfPath)
    {
        var m = BuildidRe.Match(File.ReadAllText(acfPath));
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>证据双写（gamever.txt + 探针转发行，探针再汇入 reposelftest 摘要）。</summary>
    private static void P(Action<string> p, string line)
    {
        try { File.AppendAllText(EvidencePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        p?.Invoke(line);
    }
}
