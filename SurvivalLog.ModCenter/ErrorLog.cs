using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 运行错误监听与内存缓冲（任务 14）：BepInEx ILogListener 只收 Error/Fatal，按插件 GUID 归属
/// 缓冲最近错误，供任务 15/16 面板红标与错误详情消费。纯内存（重启即清，无历史文件）。
/// 线程纪律：Listener.LogEvent 在任意日志线程回调——只拷贝 Level/SourceName/Data.ToString() 入
/// <see cref="Queue"/>（不碰 Chainloader/Unity/文件 IO）；归属、去重、容量裁剪全部在主线程
/// <see cref="Drain"/>（ConfigBridge.Pump 每拍调一次，自带 try 隔离）。
/// </summary>
internal static class ErrorLog
{
    /// <summary>归属不到已加载插件的键（SourceName 匹配不到 Metadata.Name/GUID 时使用）。</summary>
    internal const string Unattributed = "_unattributed";

    private const int Cap = 50; // 每 key 上限（超出裁最旧）

    /// <summary>一条错误记录（主线程缓冲内，newest-first 列表元素；任务 15/16 只读）。</summary>
    internal sealed class ErrRecord
    {
        public DateTime At;      // 本条（或重复族最近一次）发生时间
        public string Level;     // "Error" / "Fatal"
        public string Source;    // 日志源名（ILogSource.SourceName，通常=插件 Name）
        public string Message;   // 全文首行（\n 前，去尾部 \r）
        public string Stack;     // 首行之后的内容（异常 ToString 的堆栈段；可能为空）
        public int Repeat = 1;   // 同 key 内与上一条同 Source+Message 的连续重复次数
    }

    /// <summary>入队条目：监听线程只做字符串拷贝（不做拆行/归属——那是主线程 Drain 的事）。</summary>
    private sealed class RawEvent
    {
        public DateTime At;
        public string Level;
        public string Source;
        public string Full;      // Data.ToString() 全文（异常对象含堆栈）
    }

    private static readonly ConcurrentQueue<RawEvent> Queue = new();

    // 主线程独占（Drain 写 / HasErrors·ForGuid·Keys 读，均在 Pump/快照主线程上下文）；
    // key = 插件 GUID 或 Unattributed，列表 newest-first。
    private static readonly Dictionary<string, List<ErrRecord>> Buffer = new(StringComparer.OrdinalIgnoreCase);

    private static bool _registered;

    /// <summary>
    /// 挂监听（ModCenterPlugin.Load 在 SuiteRoster.LoadCache() 之后调，调用方 try/catch 包住——
    /// 失败只 warn，不影响面板）。幂等。
    /// </summary>
    internal static void Register()
    {
        if (_registered) return;
        _registered = true;
        Logger.Listeners.Add(new Listener());
    }

    /// <summary>错误监听器：LogLevelFilter 只收 Error/Fatal；LogEvent 任意线程回调，只入队。</summary>
    private sealed class Listener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal;

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                var full = e.Data?.ToString();
                if (!string.IsNullOrEmpty(full)) full += CaptureStack();
                Queue.Enqueue(new RawEvent
                {
                    At = DateTime.Now,
                    Level = e.Level.ToString(),
                    Source = e.Source?.SourceName,
                    Full = full,
                });
            }
            catch { } // 日志链路上的监听器绝不反抛（BepInEx 会兜，但仍不制造噪音）
        }

        /// <summary>补一段“记录线程栈”：部分框架（如 HarmonyX）只写异常消息+极短栈，
        /// 这里把写入日志时的调用链一并留档，便于反馈时看到更完整的上下文。
        /// 不截断行数（调用栈本身有限；50 条上限只约束错误记录数，不约束堆栈内容）。
        /// 放在监听线程执行（错误本就低频），失败静默忽略。</summary>
        private static string CaptureStack()
        {
            try
            {
                var lines = Environment.StackTrace.Split('\n');
                var sb = new System.Text.StringBuilder();
                sb.Append("\n--- 记录线程栈 ---\n");
                for (var i = 0; i < lines.Length; i++)
                {
                    var s = lines[i].TrimEnd('\r');
                    if (s.Length == 0) continue;
                    sb.Append(s).Append('\n');
                }
                return sb.ToString().TrimEnd();
            }
            catch { return ""; }
        }

        public void Dispose() { } // 无资源；不主动摘除（随进程生命周期）
    }

    /// <summary>
    /// 主线程排空队列（Pump 每拍一次）：逐条拆 Message/Stack → 按 SourceName 归属到插件 GUID
    ///（IL2CPPChainloader.Instance.Plugins 匹配 Metadata.Name 或 GUID，OrdinalIgnoreCase；
    /// 匹配不到/插件表未就绪 → <see cref="Unattributed"/>）→ 入对应 key 的 newest-first 列表：
    /// 与 [0] 同 Source+Message 只 Repeat++（At 刷新为最近一次），否则 Insert(0)；超 50 裁最旧。
    /// 返回本轮入账条数（新插 + 重复折叠各计 1）——Pump 据此触发红标即时推送（spec §11）。
    /// </summary>
    internal static int Drain()
    {
        var changed = 0;
        while (Queue.TryDequeue(out var raw))
        {
            if (raw == null || string.IsNullOrEmpty(raw.Full)) continue;

            var nl = raw.Full.IndexOf('\n');
            var rec = new ErrRecord
            {
                At = raw.At,
                Level = raw.Level,
                Source = raw.Source ?? "",
                Message = (nl < 0 ? raw.Full : raw.Full.Substring(0, nl)).TrimEnd('\r'),
                Stack = nl < 0 ? "" : raw.Full.Substring(nl + 1),
            };

            var key = AttributeTo(rec.Source);
            if (!Buffer.TryGetValue(key, out var list))
            {
                list = new List<ErrRecord>();
                Buffer[key] = list;
            }
            if (list.Count > 0
                && string.Equals(list[0].Source, rec.Source, StringComparison.Ordinal)
                && string.Equals(list[0].Message, rec.Message, StringComparison.Ordinal))
            {
                list[0].Repeat++;
                list[0].At = rec.At; // 重复族时间=最近一次出现
                changed++;
                continue;
            }
            list.Insert(0, rec);
            if (list.Count > Cap) list.RemoveRange(Cap, list.Count - Cap);
            changed++;
        }
        return changed;
    }

    /// <summary>SourceName → 插件 GUID（主线程；Chainloader 未就绪/异常/无匹配 → Unattributed）。</summary>
    private static string AttributeTo(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return Unattributed;
        Dictionary<string, BepInEx.PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch { }
        if (plugins == null) return Unattributed;
        foreach (var info in plugins.Values)
        {
            var md = info?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            if (string.Equals(md.Name, sourceName, StringComparison.OrdinalIgnoreCase)) return md.GUID;
            if (string.Equals(md.GUID, sourceName, StringComparison.OrdinalIgnoreCase)) return md.GUID;
        }
        return Unattributed;
    }

    // ============ 查询面（任务 15/16 消费；主线程） ============

    /// <summary>该 guid（或 <see cref="Unattributed"/>）是否有错误记录。</summary>
    internal static bool HasErrors(string guid) => ForGuid(guid).Count > 0;

    /// <summary>该 guid 的错误列表（newest-first，最多 50 条；无记录=空数组）。返回内部活列表，只读消费。</summary>
    internal static IReadOnlyList<ErrRecord> ForGuid(string guid) =>
        guid != null && Buffer.TryGetValue(guid, out var list)
            ? list
            : Array.Empty<ErrRecord>();

    /// <summary>有错误记录的全部 key（含 <see cref="Unattributed"/>，其在场时）。</summary>
    internal static IEnumerable<string> Keys => Buffer.Keys;
}
