using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 版本台账会话缓存（spec 2026-11-21 §6 版本自由选择）：repoKey → 该仓
/// <see cref="VersionInfo"/> 列表（<see cref="RepoClient.FetchVersions"/> 后台拉取成功后
/// 由主线程收割 <see cref="Put"/> 入驻）。会话级即失缓存：不落盘（版本弹窗按需拉取，
/// 失败 null 由面板提示重试，无跨会话兜底语义）；同 key 重复 Put 整体覆盖旧表。
/// 线程纪律：主线程唯一读写（RepoQueue 收割上下文，同 <see cref="SuiteRoster"/>）；
/// 键 OrdinalIgnoreCase（仓键路由主键约定）。
/// </summary>
internal static class VersionCatalog
{
    private static readonly Dictionary<string, List<VersionInfo>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>查缓存（未拉取/拉取失败 = false；返回内表引用，调用方只读不改）。</summary>
    public static bool TryGet(string repoKey, out List<VersionInfo> list)
    {
        list = null;
        if (string.IsNullOrEmpty(repoKey)) return false;
        return _map.TryGetValue(repoKey, out list);
    }

    /// <summary>写入/整体覆盖（主线程；repoKey 空或 list null 忽略——失败轮不进本口）。</summary>
    public static void Put(string repoKey, List<VersionInfo> list)
    {
        if (string.IsNullOrEmpty(repoKey) || list == null) return;
        _map[repoKey] = list;
    }

    /// <summary>台账行 → 更新信息（MC_UPD_VER 下载链构造）：Mod=仓键，AssetUrl 统一桶内
    /// zip 直连（<see cref="RepoClient.AssetUrlOf"/>），其余字段原样；Notes 不映射（版本
    /// 说明由面板直接读台账行展示，不走下载链）。</summary>
    public static RepoClient.UpdateInfo ToUpdateInfo(string repoKey, VersionInfo v) => new()
    {
        Mod = repoKey,
        Version = v.Version,
        Asset = v.Asset,
        Md5 = v.Md5,
        Gamekb = v.Gamekb,
        AssetUrl = RepoClient.AssetUrlOf(repoKey, v.Asset),
    };
}
