using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 网络层 GitHub 仓库客户端（v2 生命周期任务 5）：更新源契约 = 拆仓设计 §6/§7（已冻结）——
/// <c>GET https://api.github.com/repos/survivallog-mods/{repo}/releases/latest</c> → release
/// JSON 的 assets 数组里找名为 <c>update.json</c> 的资产 → 下载（browser_download_url）→
/// 解析五字段 {mod, version, asset, md5, gamekb}；zip 资产名 = update.json.asset 字段值。
/// 供 T6 决策层 / T8 面板仓库页消费（接口签名按任务 brief 冻结，勿改）。
///
/// 线程纪律（控制器裁定 1）：本类全部 HTTP 调用只在后台线程跑（调用方 Task.Run 起任务，
/// Ticker 主线程收割——探针参考 ConfigBridge.Pump 模式），任何同步阻塞主线程 = 缺陷。
/// 失败语义（裁定 4）：任何异常（网络/解析/资产缺失）→ <see cref="FetchLatest"/> 返回 null /
/// <see cref="DownloadAsset"/> 抛出，调用方降级；类内不做任何重试。fetch 异常留一行 warn 日志
/// （Log 静态门，评审 I-1）——失败原因（超时/403/404/解析）可排障；结构性 null（资产/字段缺失）
/// 由调用方按"无更新信息"降级展示。
///
/// cfg 键（绑定在 ModCenterPlugin.Load，Network 节）：MirrorPrefix（默认空 = 直连）、
/// CheckTimeoutMs=5000、DownloadTimeoutMs=60000。镜像前缀语义（裁定 2）：非空时对
/// https://api.github.com/... 与 https://github.com/.../releases/download/... 两类 URL 做
/// 前缀替换（形态 <c>&lt;镜像前缀&gt;/https://github.com/.../xxx.zip</c>，拆仓设计 §7），其余 URL 原样。
/// UA（裁定 3）：survivallog-modcenter（GitHub API 必带）；API 请求另带
/// Accept: application/vnd.github+json。
/// </summary>
public static class RepoClient
{
    private const string UserAgent = "survivallog-modcenter";
    private const string UpdateJsonName = "update.json";
    private const string OrgPrefix = "https://api.github.com/repos/survivallog-mods/";

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

    /// <summary>更新信息（拆仓设计 §6 五字段 + zip 资产直连 URL——URL 已含镜像映射前的原始形态，
    /// 映射在 <see cref="DownloadAsset"/> 内做，两次调用间改 cfg 即时生效）。</summary>
    public sealed class UpdateInfo
    {
        public string Mod;      // mod id（如 "RobotSpeed"）
        public string Version;  // 如 "1.7.1"
        public string Asset;    // zip 资产文件名（如 "SurvivalLog.RobotSpeed_v1.7.0.zip"）
        public string Md5;      // zip 的 MD5（小写十六进制；校验属调用方，T8 消费）
        public string Gamekb;   // 构建依据的 gamekb 快照版本（"适配到此"，T6 决策用）
        public string AssetUrl; // zip 的 browser_download_url（github.com 原始 URL）
    }

    /// <summary>fetch 结果（T16/D）：Info=null 时 Status 区分失败原因——403=GitHub 限流，
    /// 面板据此给专门提示（不显示成普通"检查失败"）；200=HTTP 成功但契约/资产不完整；0=网络/解析失败。</summary>
    public sealed class FetchResult
    {
        public UpdateInfo Info;
        public int Status;
        public string Error;
    }

    /// <summary>
    /// 拉取某仓最新 Release 的更新信息（T16/D 带状态版）：成功返回 Info；HTTP 403 时
    /// Status=403（GitHub 匿名限额）；其他失败 Status=0 或 200（契约不完整）。不抛出、不重试。
    /// </summary>
    public static async Task<FetchResult> FetchLatestEx(string repoName)
    {
        try
        {
            // 第一跳：latest release 元数据（镜像前缀只作用于 api.github.com/github.com 两族 URL）
            var relJson = await GetString(MapUrl(OrgPrefix + repoName + "/releases/latest"), CheckTimeoutMs, api: true);
            using var doc = JsonDocument.Parse(relJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return new FetchResult { Status = 200 };

            // assets 里找 update.json（browser_download_url）与 zip（名 = update.json.asset 字段值）
            string updUrl = null;
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in assets.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                if (!TryGetStr(a, "name", out var name) || string.IsNullOrEmpty(name)) continue;
                if (!TryGetStr(a, "browser_download_url", out var url)) continue;
                byName[name] = url;
                if (name == UpdateJsonName) updUrl = url;
            }
            if (updUrl == null) return new FetchResult { Status = 200 }; // 资产缺失：无 update.json（仓未发版/发版缺资产）

            // 第二跳：下载并解析 update.json（同样受 CheckTimeout 约束——几 KB 契约文件）
            var updJson = await GetString(MapUrl(updUrl), CheckTimeoutMs, api: false);
            using var udoc = JsonDocument.Parse(updJson);
            var u = udoc.RootElement;
            if (u.ValueKind != JsonValueKind.Object) return new FetchResult { Status = 200 };
            var info = new UpdateInfo
            {
                Mod = GetStr(u, "mod"),
                Version = GetStr(u, "version"),
                Asset = GetStr(u, "asset"),
                Md5 = GetStr(u, "md5"),
                Gamekb = GetStr(u, "gamekb"),
            };
            if (string.IsNullOrEmpty(info.Mod) || string.IsNullOrEmpty(info.Version) ||
                string.IsNullOrEmpty(info.Asset) || string.IsNullOrEmpty(info.Md5) ||
                string.IsNullOrEmpty(info.Gamekb))
                return new FetchResult { Status = 200 }; // 五字段任一缺失 = 契约不完整，按失败降级
            if (!byName.TryGetValue(info.Asset, out var zipUrl) || string.IsNullOrEmpty(zipUrl))
                return new FetchResult { Status = 200 }; // zip 资产不在场
            info.AssetUrl = zipUrl;
            return new FetchResult { Info = info, Status = 200 };
        }
        catch (HttpRequestException e)
        {
            // T16/D：403 = GitHub 匿名限额（60 次/h）；其余 HTTP 状态也留痕。类内零重试。
            var status = e.StatusCode.HasValue ? (int)e.StatusCode.Value : 0;
            ModCenterPlugin.Log($"warn fetch {repoName} 失败: HTTP {status} {e.Message}");
            return new FetchResult { Status = status, Error = e.Message };
        }
        catch (Exception e)
        {
            // 快速失败（超时/拒绝/DNS/解析），语义=调用方降级；类内零重试。
            // 评审 I-1：留痕失败原因（超时/403 限流/404/JSON 坏等）——玩家"更新不可用"排障
            // 与 README 换镜像前缀指引依赖此行；走既有 Log 静态门（NetProbe.P 同款用法）。
            ModCenterPlugin.Log($"warn fetch {repoName} 失败: {e.GetType().Name} {e.Message}");
            return new FetchResult { Status = 0, Error = e.Message };
        }
    }

    /// <summary>
    /// 拉取某仓最新 Release 的更新信息：latest → update.json 资产 → 五字段 + zip 资产 URL。
    /// 任何失败（网络/非 2xx/JSON 坏/资产缺失/字段缺失）返回 null，不抛出、不重试（调用方降级）。
    /// 兼容包装（原冻结签名）：需要 HTTP 状态（403 限流提示）时用 <see cref="FetchLatestEx"/>。
    /// </summary>
    public static async Task<UpdateInfo> FetchLatest(string repoName)
        => (await FetchLatestEx(repoName)).Info;

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

    /// <summary>GET 文本（api=true 时附 GitHub API 必备 Accept 头；UA 已在客户端默认头）。</summary>
    private static async Task<string> GetString(string url, int timeoutMs, bool api)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (api) req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var resp = await Http.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cts.Token);
    }

    private static bool TryGetStr(JsonElement e, string prop, out string value)
    {
        value = null;
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    private static string GetStr(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
