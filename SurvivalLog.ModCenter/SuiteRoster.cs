using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 套件名单单源（spec 2026-11-21 §1/§3）：COS 桶根 suite.json 是唯一名单来源，本类是它的
/// 客户端驻留副本——启动 <see cref="LoadCache"/> 读 BepInEx\ModCenter\suite-cache.json（上次
/// 成功拉取的落盘副本，插件 Load 期即可用），网络预检成功后 <see cref="ApplyFromFetch"/>
/// 整体替换并重写缓存。缓存不构成第二来源，只是 COS 数据的本地副本；无缓存且拉取失败 →
/// <see cref="Ready"/>=false（调用方按 spec §4 清单自报补偿降级）。
/// 线程纪律：LoadCache/ApplyFromFetch/BuildRows 均主线程调用；<see cref="BuildRows"/> 的
/// localVers 由 <see cref="RepoDecision.CaptureLocalVersions"/> 主线程取好传入，本类不碰
/// Chainloader。序列化手写 StringBuilder + <see cref="JsonW.Str"/>（AOT 零反射纪律同
/// Payload.cs），入向解析 JsonDocument 逐行容错（缺 mod 丢行）；写临时文件 + File.Move
/// 原子替换，坏缓存移存 .bad 自愈（PendingWriter.SafeLoad 同款）。
/// </summary>
internal static class SuiteRoster
{
    private static List<RepoClient.UpdateInfo> _entries; // null = 未就绪（无缓存且未成功拉取）

    private static string CachePath => Path.Combine(Paths.BepInExRootPath, "ModCenter", "suite-cache.json");

    /// <summary>名单是否就绪（读到缓存或拉取成功过；缓存由成功轮写入，正常不为空表）。</summary>
    public static bool Ready => _entries != null;

    /// <summary>名单条目（未就绪 = 空表；返回内表引用，调用方只读不改）。</summary>
    public static IReadOnlyList<RepoClient.UpdateInfo> Entries =>
        _entries ?? (IReadOnlyList<RepoClient.UpdateInfo>)Array.Empty<RepoClient.UpdateInfo>();

    /// <summary>按套件 GUID 查（key = <see cref="GuidOf"/>——显式字段优先，缺省命名约定）。</summary>
    public static bool TryGetByGuid(string guid, out RepoClient.UpdateInfo e)
    {
        e = null;
        if (_entries == null || string.IsNullOrEmpty(guid)) return false;
        foreach (var i in _entries)
            if (string.Equals(GuidOf(i), guid, StringComparison.OrdinalIgnoreCase)) { e = i; return true; }
        return false;
    }

    /// <summary>按仓键查（e.Mod，OrdinalIgnoreCase——仓键路由主键）。</summary>
    public static bool TryGetByRepo(string repoKey, out RepoClient.UpdateInfo e)
    {
        e = null;
        if (_entries == null || string.IsNullOrEmpty(repoKey)) return false;
        foreach (var i in _entries)
            if (string.Equals(i.Mod, repoKey, StringComparison.OrdinalIgnoreCase)) { e = i; return true; }
        return false;
    }

    /// <summary>条目 GUID：显式 <c>guid</c> 字段优先，缺省回退命名约定 com.local.survivallog.&lt;小写仓键&gt;。</summary>
    public static string GuidOf(RepoClient.UpdateInfo e) =>
        string.IsNullOrEmpty(e.Guid) ? "com.local.survivallog." + e.Mod.ToLowerInvariant() : e.Guid;

    /// <summary>条目显示名：显式 <c>display</c> 优先，缺省回退仓键（spec §1 字段表）。</summary>
    public static string DisplayOf(RepoClient.UpdateInfo e) =>
        string.IsNullOrEmpty(e.Display) ? e.Mod : e.Display;

    /// <summary>安装/替换目标 dll 的 BepInEx 根相对路径：显式 <c>dll</c> 优先，缺省回退
    /// <see cref="RepoDecision.DllRelOf"/>（名单行与缺省约定同形态 "plugins/…"）。</summary>
    public static string DllOf(RepoClient.UpdateInfo e) =>
        string.IsNullOrEmpty(e.Dll) ? RepoDecision.DllRelOf(e.Mod) : e.Dll;

    /// <summary>
    /// 读缓存（插件 Load 期主线程调；此时 IL2CPPChainloader 未就绪也不受影响——本口只读文件）。
    /// 任何失败只 warn 不抛，不影响启动。
    /// </summary>
    public static void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) { Log("roster: 无缓存（等首轮拉取）"); return; }
            var parsed = ParseCache(File.ReadAllText(CachePath));
            if (parsed == null) throw new InvalidDataException("结构不符（非对象/缺 mods 数组）");
            _entries = parsed;
            Log($"roster: {_entries.Count} 条（缓存）");
        }
        catch (Exception e)
        {
            // 坏缓存移存 .bad 留排障，从空开始：Ready=false，等首轮拉取重建
            Log($"warn suite-cache.json 解析失败，移存 .bad 后从空开始: {e.Message}");
            try { File.Move(CachePath, CachePath + ".bad"); } catch { }
            _entries = null;
        }
    }

    /// <summary>
    /// 网络预检成功后整体替换名单并重写缓存（主线程——RepoQueue 收割上下文）。
    /// 拉取失败调用方不进本口（保留旧缓存不动，spec §3 失败语义）。
    /// </summary>
    public static void ApplyFromFetch(List<RepoClient.UpdateInfo> entries)
    {
        _entries = entries ?? new List<RepoClient.UpdateInfo>();
        Log($"roster: {_entries.Count} 条（拉取）");
        SaveCache(_entries);
    }

    /// <summary>
    /// 名单 → 仓库页行（纯函数不读静态表；localVers 主线程取好传入）：
    /// Local 按 GUID 查本地版本（缺 = null 未安装），Remote = 名单 version，
    /// State/协议字段由调用方走 <see cref="RepoDecision.Build"/> 附。
    /// </summary>
    public static List<RepoRow> BuildRows(IEnumerable<RepoClient.UpdateInfo> entries, Dictionary<string, string> localVers)
    {
        var rows = new List<RepoRow>();
        if (entries == null) return rows;
        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.Mod)) continue;
            var guid = GuidOf(e);
            rows.Add(new RepoRow
            {
                Name = DisplayOf(e),
                RepoKey = e.Mod,
                Guid = guid,
                Dll = DllOf(e),
                Remote = e.Version,
                Local = localVers != null && localVers.TryGetValue(guid, out var ver) ? ver : null,
            });
        }
        return rows;
    }

    /// <summary>解析缓存（JSON 语法坏抛 JsonException 由 LoadCache 收；结构不符返回 null 同按坏缓存处理；
    /// 行内缺 mod 丢行——其余字段缺失留给缺省回退，不丢行）。</summary>
    private static List<RepoClient.UpdateInfo> ParseCache(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<RepoClient.UpdateInfo>();
        foreach (var m in mods.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            var info = new RepoClient.UpdateInfo
            {
                Mod = GetStr(m, "mod"),
                Version = GetStr(m, "version"),
                Asset = GetStr(m, "asset"),
                Md5 = GetStr(m, "md5"),
                Gamekb = GetStr(m, "gamekb"),
                AssetUrl = GetStr(m, "assetUrl"),
                Notes = GetStr(m, "notes"),
                Guid = GetStr(m, "guid"),
                Display = GetStr(m, "display"),
                Summary = GetStr(m, "summary"),
                Dll = GetStr(m, "dll"),
            };
            if (string.IsNullOrEmpty(info.Mod)) continue; // 逐行容错：缺 mod 丢行（无路由主键）
            list.Add(info);
        }
        return list;
    }

    /// <summary>写缓存（字段同 suite.json 行 + schema:2；可选字段空不写出——读回保持 null 语义）。
    /// 临时文件 + File.Move 原子替换；失败只 warn（内存名单不受影响，下次成功轮重写）。</summary>
    private static void SaveCache(List<RepoClient.UpdateInfo> entries)
    {
        var tmp = CachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)); // 首用建目录（幂等）
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":2,\"mods\":[");
            var first = true;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Mod)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"mod\":").Append(JsonW.Str(e.Mod))
                  .Append(",\"version\":").Append(JsonW.Str(e.Version))
                  .Append(",\"asset\":").Append(JsonW.Str(e.Asset))
                  .Append(",\"md5\":").Append(JsonW.Str(e.Md5))
                  .Append(",\"gamekb\":").Append(JsonW.Str(e.Gamekb))
                  .Append(",\"assetUrl\":").Append(JsonW.Str(e.AssetUrl));
                if (!string.IsNullOrEmpty(e.Guid)) sb.Append(",\"guid\":").Append(JsonW.Str(e.Guid));
                if (!string.IsNullOrEmpty(e.Display)) sb.Append(",\"display\":").Append(JsonW.Str(e.Display));
                if (!string.IsNullOrEmpty(e.Summary)) sb.Append(",\"summary\":").Append(JsonW.Str(e.Summary));
                if (!string.IsNullOrEmpty(e.Dll)) sb.Append(",\"dll\":").Append(JsonW.Str(e.Dll));
                if (!string.IsNullOrEmpty(e.Notes)) sb.Append(",\"notes\":").Append(JsonW.Str(e.Notes));
                sb.Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            File.Move(tmp, CachePath, true); // net6 单 API 原子覆盖（无 Delete+Move 的空窗/半态窗口）
        }
        catch (Exception e)
        {
            Log($"warn suite-cache.json 写入失败（内存名单不受影响）: {e.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static void Log(string msg) => ModCenterPlugin.Log(msg);

    private static string GetStr(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
