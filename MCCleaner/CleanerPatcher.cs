using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Preloader.Core.Patching;
using SurvivalLog.ModCenter;

namespace MCCleaner;

/// <summary>
/// MC 启动清理器（ModCenter v2 生命周期组件一）：BepInEx patcher 阶段的纯前置执行器——
/// 无任何 Patch 方法，仅借 Initialize 在启动最早阶段执行动作（探针 E1 已实证可行）。
/// 单槽基座级组件：始终以 BepInEx\patchers\MCCleaner.dll 单份在场，游戏内通道不更新自身，
/// 见 spec §4.7（双槽同装已被 CLR 程序集身份合并证伪，Ruling 8）。
/// T2：Initialize 依次执行——①序列化自证（标记文件 BepInEx\ModCenter\selftest.flag 触发，
/// 裁定 2：补丁器阶段 cfg 读取不可靠，弃 cfg 键 Debug.SelfTest 改标记文件）；②pending 执行器
/// （幂等 / MD5 校验 / 失败保留 / 原子重写，spec §3）。后续任务继续追加（加载序改挂 / 停用迁移）。
/// 自证文件（硬规则，不依赖 LogOutput.log）：BepInEx\MCCleaner_loaded.txt（T1 生命周期）
/// + BepInEx\MCCleaner_ops.txt（T2 起 op 执行证据，追加行：时间/操作/结果/原因）。
/// </summary>
[PatcherPluginInfo("com.local.survivallog.mccleaner", "MC Cleaner", "1.0.0")]
public class CleanerPatcher : BasePatcher
{
    /// <summary>自证文件：BepInEx 根目录下 MCCleaner_loaded.txt</summary>
    private static string EvidencePath =>
        Path.Combine(Paths.BepInExRootPath, "MCCleaner_loaded.txt");

    /// <summary>op 执行证据：BepInEx 根目录下 MCCleaner_ops.txt</summary>
    private static string OpsEvidencePath =>
        Path.Combine(Paths.BepInExRootPath, "MCCleaner_ops.txt");

    /// <summary>序列化自证触发标记（裁定 2）：BepInEx\ModCenter\selftest.flag 存在即跑一轮，跑完即删。</summary>
    private static string SelfTestFlagPath =>
        Path.Combine(Paths.BepInExRootPath, "ModCenter", "selftest.flag");

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static void Ev(string line)
    {
        try
        {
            File.AppendAllText(
                EvidencePath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n",
                Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>op 证据追加行：时间戳 + 内容（UTF-8 无 BOM）。</summary>
    private static void OpsEv(string line)
    {
        try
        {
            File.AppendAllText(
                OpsEvidencePath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n",
                Utf8NoBom);
        }
        catch { }
    }

    public override void Initialize()
    {
        // 生命周期自证：时间戳 + 本 dll 实际加载位置（Assembly.Location 原值）
        Ev("Initialize reached, dll=" + typeof(CleanerPatcher).Assembly.Location);

        RunSelfTestIfFlagged(); // T2 Step1：McPending 序列化自证（标记文件触发）
        RunPendingOps();        // T2 Step2：pending 执行器

        // ---- 后续任务在此之后追加执行段（加载序改挂 / 停用迁移，停用翻译须在 RunPendingOps 之前，Ruling 1）----
    }

    // ==================== T2 Step 1：序列化自证 ====================

    /// <summary>
    /// 标记文件存在即对含引号/反斜杠/换行/中文/空格的 ops 与 order 做 Save→Load 往返对拍，
    /// 结果写 ops 证据，随后删除标记与临时文件。全程不触碰真实 pending.json。
    /// </summary>
    private static void RunSelfTestIfFlagged()
    {
        try
        {
            if (!File.Exists(SelfTestFlagPath)) return;

            var ops = new List<McPending.Op>
            {
                new()
                {
                    Type = McPending.TypeInstall,
                    Target = "plugins/名\"称\\測試 路徑\n.dll", // 引号+反斜杠+换行+中文+空格（仅序列化用例，不落盘）
                    Staged = "staging/暫存 測試.bin",
                    Md5 = "d41d8cd98f00b204e9800998ecf8427e",
                },
                new() { Type = McPending.TypeDelete, Target = "config/删 除 假 件.cfg" },
            };
            var order = new List<string> { "com.local.测试\"a\\b\n", "com.本地.排序" };

            var tmp = Path.Combine(Paths.BepInExRootPath, "ModCenter", "selftest.tmp.json");
            McPending.Save(tmp, ops, order);
            var back = McPending.Load(tmp);
            var backOrder = McPending.LoadOrder(tmp);

            var why = "";
            var ok = back.Count == ops.Count && backOrder.Count == order.Count;
            if (!ok) why = "条数不符 ops " + back.Count + "/" + ops.Count + " order " + backOrder.Count + "/" + order.Count;
            for (var i = 0; ok && i < ops.Count; i++)
            {
                if (back[i].Type != ops[i].Type || back[i].Target != ops[i].Target ||
                    back[i].Staged != ops[i].Staged || back[i].Md5 != ops[i].Md5)
                { ok = false; why = "第 " + i + " 条 op 字段不一致"; }
            }
            for (var i = 0; ok && i < order.Count; i++)
            {
                if (backOrder[i] != order[i]) { ok = false; why = "order[" + i + "] 不一致"; }
            }

            OpsEv("SELFTEST " + (ok ? "OK" : "FAIL") + " 往返对拍 " + ops.Count + " ops / " +
                  order.Count + " order 项（含引号/反斜杠/换行/中文/空格）" + (why.Length > 0 ? " — " + why : ""));
            try { File.Delete(tmp); } catch { }
            File.Delete(SelfTestFlagPath);
        }
        catch (Exception ex)
        {
            OpsEv("SELFTEST FAIL 异常: " + ex.Message);
        }
    }

    // ==================== T2 Step 2：pending 执行器 ====================

    /// <summary>
    /// 执行 pending.json（spec §3）：按序逐条执行；幂等（目标不在场=成功）；单条失败保留该条
    /// 并记证据；install/replace 先 MD5 校验；install/replace 目标为清理器槽（Ruling 8）拒绝执行；
    /// 有成功条目时以剩余条目原子重写清单（order 原样回传，不破坏 T3 顺序覆盖）。
    /// 任何异常不阻断游戏启动。
    /// </summary>
    private static void RunPendingOps()
    {
        try
        {
            if (!File.Exists(McPending.File)) return;

            List<McPending.Op> ops;
            List<string> order;
            try
            {
                ops = McPending.Load(McPending.File);
                order = McPending.LoadOrder(McPending.File);
            }
            catch (Exception ex)
            {
                OpsEv("pending 解析 FAIL 清单保留待修复: " + ex.Message);
                return;
            }
            if (ops.Count == 0) return; // 空 op（含 order-only 文件，T3 消费）不动清单

            var remaining = new List<McPending.Op>();
            var done = 0;
            foreach (var op in ops)
            {
                if (ExecuteOp(op, out var reason))
                {
                    done++;
                    OpsEv(Summarize(op) + " OK" + (reason.Length > 0 ? " " + reason : ""));
                }
                else
                {
                    remaining.Add(op);
                    OpsEv(Summarize(op) + " FAIL " + reason);
                }
            }
            if (done > 0)
            {
                McPending.Save(McPending.File, remaining, order);
                OpsEv("pending 原子重写 OK 保留 " + remaining.Count + "/" + ops.Count + " 条（失败条目下次再试）");
            }
        }
        catch (Exception ex)
        {
            OpsEv("pending 执行器异常: " + ex.Message);
        }
    }

    private static string Summarize(McPending.Op op) =>
        (op.Type ?? "?") + " " + (op.Type == McPending.TypeInstall || op.Type == McPending.TypeReplace
            ? op.Staged + " -> " + op.Target
            : op.Target);

    /// <summary>执行单条 op。返回 true=成功（幂等不在场也算），false=失败（reason 给证据）。</summary>
    private static bool ExecuteOp(McPending.Op op, out string reason)
    {
        reason = "";
        switch ((op.Type ?? "").ToLowerInvariant())
        {
            case "delete":
            {
                var t = ResolveRel(op.Target);
                if (t == null) { reason = "非法相对路径: " + op.Target; return false; }
                if (!File.Exists(t)) { reason = "目标不在场(幂等)"; return true; }
                try { File.Delete(t); return true; }
                catch (Exception ex) { reason = "删除失败: " + ex.Message; return false; }
            }

            case "install":
            case "replace":
            {
                var t = ResolveRel(op.Target);
                if (t == null) { reason = "非法相对路径: " + op.Target; return false; }
                if (IsCleanerSlot(op.Target))
                {
                    reason = "拒绝执行: 清理器属基座级组件，请用安装器更新 (Target=" + op.Target + ")";
                    return false;
                }
                if (string.IsNullOrEmpty(op.Md5)) { reason = "缺少 md5"; return false; }
                var s = ResolveRel(op.Staged);
                if (s == null) { reason = "非法暂存路径: " + op.Staged; return false; }
                if (!File.Exists(s)) { reason = "staged 不在场: " + op.Staged; return false; }
                string actual;
                try { actual = Md5Of(s); }
                catch (Exception ex) { reason = "md5 计算失败: " + ex.Message; return false; }
                if (!string.Equals(actual, op.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "md5 不匹配: 期望 " + op.Md5 + " 实得 " + actual;
                    return false;
                }
                try
                {
                    if (File.Exists(t)) File.Delete(t); // replace：旧件在场先删（install 场景本就不在场）
                    var dir = Path.GetDirectoryName(t);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(s, t, true);
                    return true;
                }
                catch (Exception ex) { reason = "落盘失败: " + ex.Message; return false; }
            }

            case "disable":
            {
                var t = ResolveRel(op.Target);
                if (t == null) { reason = "非法相对路径: " + op.Target; return false; }
                if (!File.Exists(t)) { reason = "目标不在场(幂等)"; return true; }
                try { File.Move(t, t + ".disabled"); return true; }
                catch (Exception ex) { reason = "改名失败: " + ex.Message; return false; }
            }

            case "enable":
            {
                var t = ResolveRel(op.Target);
                if (t == null) { reason = "非法相对路径: " + op.Target; return false; }
                if (File.Exists(t)) { reason = "目标已在场(幂等)"; return true; }
                var d = t + ".disabled";
                if (!File.Exists(d)) { reason = "目标与 .disabled 均不在场(幂等)"; return true; }
                try { File.Move(d, t); return true; }
                catch (Exception ex) { reason = "改名失败: " + ex.Message; return false; }
            }

            default:
                reason = "未知 op 类型: " + op.Type;
                return false;
        }
    }

    /// <summary>
    /// BepInEx 根相对路径 → 绝对路径（-LiteralPath 等价语义：'/' 归一为 '\' 后 Path.Combine，
    /// 不吃通配/相对段歧义；绝对路径/盘符/../ 均拒绝返回 null）。
    /// </summary>
    private static string ResolveRel(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        var n = rel.Trim().Replace('/', '\\');
        if (n.Length == 0 || n.StartsWith('\\') || Path.IsPathRooted(n) || n.Contains("..") || n.Contains(':'))
            return null;
        return Path.Combine(Paths.BepInExRootPath, n);
    }

    /// <summary>自身槽守卫（Ruling 8）：install/replace 目标为 MCCleaner.dll（任意目录）或 patchers\ 下任何文件。</summary>
    private static bool IsCleanerSlot(string rel)
    {
        var n = rel.Trim().Replace('/', '\\');
        if (Path.GetFileName(n).Equals("MCCleaner.dll", StringComparison.OrdinalIgnoreCase)) return true;
        var first = n.Split('\\')[0];
        return first.Equals("patchers", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>文件 MD5（小写十六进制）。</summary>
    private static string Md5Of(string path)
    {
        using var md5 = MD5.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(fs)).ToLowerInvariant();
    }
}
