using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 网络层更新源客户端（v2 生命周期任务 5）：更新源契约 = 拆仓设计 §6/§7（已冻结）。
/// v2.4.0 套件名单单源（COS-only）：检查唯一入口 = <see cref="FetchSuite"/>——1 次直拉
/// 腾讯云 COS 公有读桶根 suite.json（cfg <see cref="CosBase"/>，Network.CosBase）→ 解析
/// N 行 <see cref="UpdateInfo"/>（五字段 + assetUrl + 可选名单字段 guid/display/summary/dll；
/// 发布侧 = 套件根 publish-index.ps1 聚合脚本）；行内 AssetUrl 统一重写为桶内 zip 直连，
/// 下载走 <see cref="DownloadAsset"/>。逐仓 GitHub 补查（FetchLatest/FetchLatestEx，含 403
/// 限流通道）已随名单单源删除——失败即 Status=0，调用方按名单缓存（SuiteRoster）或空表降级。
/// 供决策层 / 面板仓库页 / 自诊断探针消费。
///
/// 线程纪律（控制器裁定 1）：本类全部 HTTP 调用只在后台线程跑（调用方 Task.Run 起任务，
/// Ticker 主线程收割——探针参考 ConfigBridge.Pump 模式），任何同步阻塞主线程 = 缺陷。
/// 失败语义（裁定 4）：任何异常（网络/解析/资产缺失）→ <see cref="FetchSuite"/> 返回
/// Status=0/Suite=null / <see cref="DownloadAsset"/> 抛出，调用方降级；类内不做任何重试。
/// fetch 异常留一行 warn 日志（Log 静态门，评审 I-1）——失败原因（超时/404/解析）可排障；
/// 结构性 null（字段缺失丢行）由调用方按"无该行"降级展示。
///
/// cfg 键（绑定在 ModCenterPlugin.Load，Network 节）：MirrorPrefix（默认空 = 直连）、
/// CheckTimeoutMs=5000、DownloadTimeoutMs=60000。镜像前缀语义（裁定 2）：非空时对
/// https://api.github.com/... 与 https://github.com/.../releases/download/... 两类 URL 做
/// 前缀替换（形态 <c>&lt;镜像前缀&gt;/https://github.com/.../xxx.zip</c>，拆仓设计 §7），
/// 其余 URL（COS 全族）原样。
/// UA（裁定 3）：survivallog-modcenter（HTTP 通行习惯，统一携带）。
/// </summary>
public static class RepoClient
{
    private const string UserAgent = "survivallog-modcenter";

    // 懒初始化线程安全（评审 M-1）：Lazy 默认 ExecutionAndPublication 双检锁闭环，
    // T6/T8 并发 fetch 时只会有一个 HttpClient 实例（否则各 new 一个且孤儿不 Dispose）。
    private static readonly Lazy<HttpClient> HttpLazy = new(() =>
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        c.Timeout = Timeout.InfiniteTimeSpan;
        return c;
    });

    /// <summary>单例客户端（默认跟随系统代理；总超时无限——超时由每次请求的 CTS 按 cfg 键控制）。</summary>
    private static HttpClient Http => HttpLazy.Value;

    private static int CheckTimeoutMs
    {
        get
        {
            var v = ModCenterPlugin.NetworkCheckTimeoutMs?.Value ?? 5000;
            return v >= 500 ? v : 500; // 下限保护：0/负值会让 CTS 立即取消（视为配置错误）
        }
    }

    private static int DownloadTimeoutMs
    {
        get
        {
            var v = ModCenterPlugin.NetworkDownloadTimeoutMs?.Value ?? 60000;
            return v >= 500 ? v : 500;
        }
    }

    /// <summary>COS 主源基址（cfg Network.CosBase，v2.1.0；v2.4.0 起检查/下载唯一源）：
    /// 检查 = 桶根 suite.json，下载 = 桶内 mods/&lt;repo&gt;/ zip。空串/全斜杠 = 未配置
    /// （FetchSuite 必失败，调用方按名单缓存/空表降级）。已 Trim + 去尾斜杠
    /// （拼接处直接 <c>base + "/mods/..."</c>）。</summary>
    internal static string CosBase
    {
        get
        {
            var v = ModCenterPlugin.NetworkCosBase?.Value;
            if (string.IsNullOrEmpty(v)) return null;
            v = v.Trim().TrimEnd('/');
            return v.Length == 0 ? null : v;
        }
    }

    /// <summary>更新信息（拆仓设计 §6 五字段 + zip 资产直连 URL——URL 已含镜像映射前的原始形态，
    /// 映射在 <see cref="DownloadAsset"/> 内做，两次调用间改 cfg 即时生效）。
    /// v2.2.0：+Notes 发版说明（CHANGELOG 本版节原文，发布侧自动带；**可选字段**——旧源缺失
    /// = null，面板降级不显示，不参与完整性校验）。
    /// v2.4.0：+Guid/Display/Summary/Dll 名单行四字段（套件名单单源 spec）——同 Notes 属
    /// **可选字段**，旧源缺失 = null，不参与完整性校验。</summary>
    public sealed class UpdateInfo
    {
        public string Mod;      // mod id（如 "RobotSpeed"）
        public string Version;  // 如 "1.7.1"
        public string Asset;    // zip 资产文件名（如 "SurvivalLog.RobotSpeed_v1.7.0.zip"）
        public string Md5;      // zip 的 MD5（小写十六进制；校验属调用方，T8 消费）
        public string Gamekb;   // 构建依据的 gamekb 快照版本（"适配到此"，T6 决策用）
        public string AssetUrl; // zip 直连 URL（FetchSuite 已统一重写为桶内直连；镜像映射在 DownloadAsset 内做）
        public string Notes;    // 发版说明（v2.2.0 可选：CHANGELOG 节原文，多行 markdown）
        public string Guid;     // mod guid（v2.4.0 可选：套件名单行自带，名单单源）
        public string Display;  // 显示名（v2.4.0 可选：缺 null 时面板用仓库名降级）
        public string Summary;  // 一句话简介（v2.4.0 可选：缺 null 时面板不显示）
        public string Dll;      // mod 程序集文件名（v2.4.0 可选：定位已装 dll 用）
    }

    // ============ v2.0.6：聚合检查（spec 2026-11-07 单轮检查；v2.4.0 起名单单源唯一入口） ============

    /// <summary>聚合结果：Suite=null = 名单不可用（Status=0 失败 / 200 拿到）。
    /// 调用方失败语义：按 <see cref="SuiteRoster"/> 缓存名单兜底（无缓存=空表）。</summary>
    public sealed class SuiteResult
    {
        public List<UpdateInfo> Suite; // 解析成功的行；null = 名单不可用
        public int Status;             // 200=拿到（≥1 行）；0=其余失败（CosBase 未配/网络/解析/零行）
    }

    /// <summary>聚合检查（v2.4.0 COS-only）：GET 桶根 suite.json（<b>1 次直拉</b>，无 API 消耗）
    /// → 解析 N 行 UpdateInfo（契约与 update.json 五字段同构 + assetUrl + 可选名单字段；
    /// 发布侧 = 套件根 publish-index.ps1 聚合脚本）。任何失败（CosBase 未配置/网络/JSON 坏/
    /// 解析不符/零行）→ Status=0、Suite=null，调用方按名单缓存兜底或空表降级。
    /// 类内零重试（同款纪律）。</summary>
    public static async Task<SuiteResult> FetchSuite()
    {
        try
        {
            var json = await GetString(CosBase + "/suite.json", CheckTimeoutMs);
            var parsed = ParseSuiteJson(json);
            if (parsed != null)
            {
                // 下载也走桶：assetUrl 统一重写为桶内 zip 直连（suite.json 文件本身可能还是
                // GitHub 形态——publish-index 未切/双发过渡期；重写幂等，以桶内布局为准）
                foreach (var i in parsed.Suite)
                    i.AssetUrl = AssetUrlOf(i.Mod, i.Asset);
                return parsed;
            }
            ModCenterPlugin.Log("warn fetch suite 解析不符（零行/结构坏）");
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log($"warn fetch suite 失败: {e.GetType().Name} {e.Message}");
        }
        return new SuiteResult { Status = 0 };
    }

    /// <summary>桶内 zip 直连 URL（v2.4.0）：&lt;CosBase&gt;/mods/&lt;repoKey&gt;/&lt;asset&gt;。
    /// 供 FetchSuite 行重写与决策/面板层拼装（repoKey/asset 均来自名单行，受控输入免编码）。
    /// CosBase 未配置时返回相对形态（调用方不可直连，仅在 FetchSuite 失败前置场景出现）。</summary>
    public static string AssetUrlOf(string repoKey, string asset) => CosBase + "/mods/" + repoKey + "/" + asset;

    /// <summary>解析 suite.json 聚合文件（COS 桶根，发布侧 publish-index.ps1 产出）：
    /// mods 数组逐行五字段 + assetUrl 校验，行契约不完整丢行（该 mod 不进本轮名单——无逐仓
    /// 补查）；至少 1 行成功返回 SuiteResult(Status=200)，结构不符（非对象/无 mods 数组）或
    /// 零行返回 null（调用方按名单缓存/空表降级）。JSON 语法坏抛 JsonException，由调用方 catch。</summary>
    private static SuiteResult ParseSuiteJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<UpdateInfo>();
        foreach (var m in mods.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            var info = new UpdateInfo
            {
                Mod = GetStr(m, "mod"), Version = GetStr(m, "version"), Asset = GetStr(m, "asset"),
                Md5 = GetStr(m, "md5"), Gamekb = GetStr(m, "gamekb"), AssetUrl = GetStr(m, "assetUrl"),
                Notes = GetStr(m, "notes"), // v2.2.0 可选：不参与丢行判定
                Guid = GetStr(m, "guid"), Display = GetStr(m, "display"),
                Summary = GetStr(m, "summary"), Dll = GetStr(m, "dll"), // v2.4.0 可选：不参与丢行判定
            };
            if (string.IsNullOrEmpty(info.Mod) || string.IsNullOrEmpty(info.Version) ||
                string.IsNullOrEmpty(info.Asset) || string.IsNullOrEmpty(info.Md5) ||
                string.IsNullOrEmpty(info.Gamekb) || string.IsNullOrEmpty(info.AssetUrl))
                continue; // 行契约不完整：丢行（该 mod 不进本轮名单）
            list.Add(info);
        }
        return list.Count == 0 ? null : new SuiteResult { Suite = list, Status = 200 };
    }

    /// <summary>
    /// 下载 zip 资产到 StagingDir（BepInEx\ModCenter\staging\，T2 契约目录）：先落
    /// <c>&lt;asset&gt;.part</c> 临时名，成功后原子落位为 <c>&lt;asset&gt;</c> 并返回该绝对路径
    /// （T8 写 pending op 的 Staged 字段即此文件的 BepInEx 根相对路径）。
    /// progress(done, total) 在下载线程回调（total&lt;0 = 服务器未报 Content-Length），
    /// 每读满 64KB 一次——节流由调用方做。任何失败：清理 .part 后抛出（不重试）。
    /// </summary>
    public static async Task<string> DownloadAsset(UpdateInfo info, Action<long, long> progress)
    {
        if (info == null || string.IsNullOrEmpty(info.AssetUrl))
            throw new ArgumentException("info/AssetUrl 为空", nameof(info));
        var assetFile = Path.GetFileName(info.Asset); // 只取文件名——资产名不是路径，防越界
        if (string.IsNullOrEmpty(assetFile) || assetFile == ".part")
            throw new ArgumentException("Asset 资产名非法: " + info.Asset, nameof(info));

        var dir = McPending.StagingDir;
        Directory.CreateDirectory(dir); // 首用建目录（幂等）
        var finalPath = Path.Combine(dir, assetFile);
        var tmpPath = finalPath + ".part";
        try
        {
            using var cts = new CancellationTokenSource(DownloadTimeoutMs);
            using var resp = await Http.GetAsync(MapUrl(info.AssetUrl), HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;
            using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
            using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                var buf = new byte[65536];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                    done += n;
                    progress?.Invoke(done, total);
                }
            } // dst 必须先关再 Move（Windows 句柄锁：using 声明在块尾释放，Move 若留在同块内必撞 IOException）

            if (File.Exists(finalPath)) File.Delete(finalPath); // 陈旧同名残留先清（staging 由下载方独占管理）
            File.Move(tmpPath, finalPath); // 落位（同卷原子）
            return finalPath;
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    /// <summary>镜像前缀映射：非空时对 https://github.com / https://api.github.com 两族 URL
    /// 前置镜像域（&lt;prefix&gt;/&lt;原 URL&gt;，ghproxy 类通行形态），其余 URL 原样。</summary>
    internal static string MapUrl(string url)
    {
        var p = ModCenterPlugin.NetworkMirrorPrefix?.Value;
        if (string.IsNullOrEmpty(p)) return url;
        p = p.Trim().TrimEnd('/');
        if (p.Length == 0) return url;
        if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal) ||
            url.StartsWith("https://github.com/", StringComparison.Ordinal))
            return p + "/" + url;
        return url;
    }

    /// <summary>文件 MD5（小写十六进制；与清理器侧 Md5Of 同款，T8 校验消费）。</summary>
    internal static string Md5Of(string path)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>GET 文本（UA 已在客户端默认头）。非 2xx 抛 HttpRequestException 由调用方
    /// catch（404=桶无该文件/配置错，超时=TaskCanceledException）。</summary>
    private static async Task<string> GetString(string url, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();
        // v2.3.0 实证修复：BepInEx 环境下部分 handler 对无 charset 响应按 ISO-8859-1 解
        // ReadAsStringAsync——桶内 update.json/suite.json（Content-Type: application/json 无
        // charset）的中文 notes 全变 ï¼ˆ 式 mojibake（静态 UI 中文不受影响，v2.2.0 前拉取的
        // 五字段全 ASCII 故未暴露）。JSON 规范即 UTF-8：按字节读+强制 UTF-8 解码，无视响应头。
        var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string GetStr(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
