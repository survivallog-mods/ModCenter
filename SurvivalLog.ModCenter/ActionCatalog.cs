using System.Collections.Generic;
using System.IO;

namespace SurvivalLog.ModCenter;

/// <summary>
/// QuickAction 动作目录读取（BepInEx\QuickActionCatalog.txt，QuickAction v1.8.0+ 每会话表
/// 就绪后重写一次）。行格式 id|名称|分类|原耗时毫秒，分类 1..6（1物品使用 2家具功能
/// 3房屋维护 4杂项交互 5娱乐锻炼 6野外与其他=引用轴漏网的 Other 玩家动作，如野外探险
/// 探索中109）。QuickAction 侧已过全部守卫（表外收益/周期正收益/纯消耗拦Outside8/瞬时
/// 哨兵/禁改类）——列进来的动作覆盖一定生效，是"本 mod 可加速动作"的权威单源（守卫口径
/// 随 QuickAction 迭代，本侧只读不复算）。
/// 文件缺失/格式旧（旧版 dll、首次运行且总开关关）→ null → 面板回退 ID 手输框。
/// 缓存按 LastWriteTimeUtc：BuildSnapshot 在 MC_OPEN/MC_SET 后高频重建，不重复读盘；
/// QuickAction 重写文件（新会话）后时间戳变化自动失效。
/// </summary>
internal static class ActionCatalog
{
    private const string Marker = "ID|名称|分类"; // 表头格式标记（版本容错：无标记=旧格式整文件弃用）

    private static List<ActInfo> _cache;
    private static long _stamp;

    internal static List<ActInfo> Load()
    {
        try
        {
            var p = Path.Combine(BepInEx.Paths.BepInExRootPath, "QuickActionCatalog.txt");
            if (!File.Exists(p)) return null;
            var stamp = File.GetLastWriteTimeUtc(p).Ticks;
            if (_cache != null && stamp == _stamp) return _cache;
            var acts = Parse(File.ReadAllLines(p));
            _cache = acts.Count > 0 ? acts : null; // 空表视为无目录（面板回退手输）
            _stamp = stamp;
            return _cache;
        }
        catch { return null; }
    }

    private static List<ActInfo> Parse(string[] lines)
    {
        var acts = new List<ActInfo>();
        bool head = false;
        foreach (var l in lines)
        {
            var seg = l.Trim();
            if (seg.Length == 0 || seg.StartsWith("==")) { if (!head && seg.IndexOf(Marker) >= 0) head = true; continue; }
            if (!head) { head = true; continue; } // 容错：无表头也从首个非空行后开始按数据行尝试
            var parts = seg.Split('|');
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[0], out var id) || id <= 0) continue;
            var n = parts[1].Trim();
            if (n.Length == 0 || n == "?") continue;
            if (!int.TryParse(parts[2], out var c) || c < 1 || c > 6) continue;
            if (!int.TryParse(parts[3], out var ms) || ms < 0) continue;
            acts.Add(new ActInfo { Id = id, N = n, C = c, Ms = ms });
        }
        return acts;
    }
}
