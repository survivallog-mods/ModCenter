using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Preloader.Core.Patching;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SurvivalLog.ModCenter;

namespace MCCleaner;

/// <summary>
/// MC 启动清理器（ModCenter v2 生命周期组件一）：BepInEx patcher 阶段的纯前置执行器——
/// 无任何 Patch 方法，仅借 Initialize 在启动最早阶段执行动作（探针 E1 已实证可行）。
/// 单槽基座级组件：始终以 BepInEx\patchers\MCCleaner.dll 单份在场，游戏内通道不更新自身，
/// 见 spec §4.7（双槽同装已被 CLR 程序集身份合并证伪，Ruling 8）。
/// T2：Initialize 依次执行——①序列化自证（标记文件 BepInEx\ModCenter\selftest.flag 触发，
/// 裁定 2：补丁器阶段 cfg 读取不可靠，弃 cfg 键 Debug.SelfTest 改标记文件）；②pending 执行器
/// （幂等 / MD5 校验 / 失败保留 / 原子重写，spec §3）。T3：③挂 BaseChainloader&lt;BasePlugin&gt;.
/// ModifyLoadOrder postfix 实现加载序覆盖（pending.order 驱动 + 硬依赖保护，纯内存不写文件）。
/// 后续任务继续追加（停用迁移）。
/// 自证文件（硬规则，不依赖 LogOutput.log）：BepInEx\MCCleaner_loaded.txt（T1 生命周期）
/// + BepInEx\MCCleaner_ops.txt（T2 起 op 执行证据，追加行：时间/操作/结果/原因；
/// T3 起加载序对拍：[ORDER] #NN guid 一行一项 + [ORDER-PROTECTED] guid(依赖 dep)）。
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

        RunSelfTestIfFlagged(); // T2 Step1：McPending 序列化自证；T3：加载序重排自证（同标记触发）
        RunPendingOps();        // T2 Step2：pending 执行器
        InstallOrderOverride(); // T3：挂 ModifyLoadOrder postfix（order 读取在 postfix 内，晚于全部 op 落地）

        // ---- 后续任务在此之后追加执行段（停用翻译须在 RunPendingOps 之前，Ruling 1）----
    }

    // ==================== T2 Step 1：序列化自证 ====================

    /// <summary>
    /// 标记文件存在即跑两轮自证，结果各写一行 ops 证据，随后删除标记与临时文件：
    /// ①序列化往返（含引号/反斜杠/换行/中文/空格的 ops 与 order 的 Save→Load 对拍）；
    /// ②加载序重排（T3，构造数据喂 OrderReorder.Apply：对调/前置/幽灵 GUID/硬依赖保护/软依赖不保护）。
    /// 全程不触碰真实 pending.json。
    /// </summary>
    private static void RunSelfTestIfFlagged()
    {
        if (!File.Exists(SelfTestFlagPath)) return;
        SelfTestSerialize();
        SelfTestOrder();
        try { File.Delete(SelfTestFlagPath); } catch { }
    }

    private static void SelfTestSerialize()
    {
        try
        {
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
        }
        catch (Exception ex)
        {
            OpsEv("SELFTEST FAIL 异常: " + ex.Message);
        }
    }

    /// <summary>
    /// T3 自证：构造 7 项加载表喂 <see cref="OrderReorder.Apply"/>（裁定 3 放宽——PluginInfo be.788
    /// 公共面为无参 ctor + 只读 Metadata/Dependencies，无法注入 GUID/依赖，故用轻量 Node 记录；
    /// 投影层 PluginInfo→Node 由装机对拍覆盖）。断言：order 命中项前置且无依赖对调生效、
    /// 幽灵 GUID 忽略、硬依赖冲突项被拉回依赖项之后（被保护）、软依赖不保护、输入表不被改动（纯函数）。
    /// </summary>
    private static void SelfTestOrder()
    {
        try
        {
            OrderReorder.Node Mk(string g, params (string Guid, bool Hard)[] deps) =>
                new() { Guid = g, Deps = deps.ToList() };

            // 原始序（BepInEx 拓扑序，依赖健全）：a.base → b.lib → c.feature → d.standalone → e.soft → f.pad → g.pad
            var current = new List<OrderReorder.Node>
            {
                Mk("a.base"),
                Mk("b.lib", ("a.base", true)),
                Mk("c.feature", ("b.lib", true)),
                Mk("d.standalone"),
                Mk("e.soft", ("d.standalone", false)), // 软依赖：不参与保护
                Mk("f.pad"),
                Mk("g.pad"),
            };
            var before = current.Select(x => x.Guid).ToList();

            // order：c.feature 抢首（硬依赖冲突→应被保护）、h.ghost 幽灵项、e.soft 前置于其软依赖（允许）、
            // g.pad 前置于 f.pad（两个无依赖项对调生效）
            var order = new List<string> { "c.feature", "h.ghost", "a.base", "e.soft", "d.standalone", "g.pad" };

            var applied = OrderReorder.Apply(current, order, out var protected_);

            // 期望（Kahn 优先级拓扑）：a.base(序1)→e.soft(序2,软依赖放行)→d.standalone(序3)→g.pad(序4,
            // 对调生效)→b.lib(依赖 a.base 已放置)→c.feature(保护：拉回依赖 b.lib 之后)→f.pad(其余原相对序)
            var expected = new[] { "a.base", "e.soft", "d.standalone", "g.pad", "b.lib", "c.feature", "f.pad" };
            var expectedProtected = new[] { "c.feature(依赖 b.lib)" };

            var why = "";
            var ok = true;
            var got = applied.Select(x => x.Guid).ToList();
            if (!got.SequenceEqual(expected))
            { ok = false; why = "输出序不符: " + string.Join(",", got); }
            if (ok && !protected_.SequenceEqual(expectedProtected))
            { ok = false; why = "被保护项不符: " + string.Join(";", protected_); }
            if (ok && !current.Select(x => x.Guid).SequenceEqual(before))
            { ok = false; why = "输入表被改动（非纯函数）"; }
            if (ok)
            {
                // 通用不变量：最终序中每个硬依赖项仍在其全部在场硬依赖之后
                var pos = got.Select((g, i) => (g, i)).ToDictionary(t => t.g, t => t.i);
                foreach (var node in current)
                    foreach (var d in node.Deps)
                        if (d.Hard && pos.TryGetValue(d.Guid, out var di) && di > pos[node.Guid])
                        { ok = false; why = "硬依赖 " + d.Guid + " 仍排在 " + node.Guid + " 之后"; }
            }

            OpsEv("SELFTEST-ORDER " + (ok ? "OK" : "FAIL") + " 7 项构造序（order 6 项含 1 幽灵）：无依赖对调生效、" +
                  "硬依赖保护 1 项(c.feature←b.lib)、软依赖(e.soft←d.standalone)不保护、输入表未改动；" +
                  "输出序 " + string.Join(",", got) + (why.Length > 0 ? " — " + why : ""));
        }
        catch (Exception ex)
        {
            OpsEv("SELFTEST-ORDER FAIL 异常: " + ex.Message);
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

    // ==================== T3：加载序覆盖（Harmony 改序 + 依赖保护） ====================

    /// <summary>Harmony 实例 ID（补丁器阶段自用，与插件侧 Harmony ID 无冲突）。</summary>
    private const string OrderHarmonyId = "com.local.survivallog.mccleaner.order";

    /// <summary>
    /// 补丁器阶段挂 BaseChainloader&lt;BasePlugin&gt;.ModifyLoadOrder 的 postfix（spec §2 教训：
    /// 必须封闭泛型，开放泛型会让 Il2CppInterop Harmony 后端炸 ContainsGenericParameters）。
    /// 仅挂补丁不做任何 IO；失败记一行 ops 证据后放弃覆盖（不阻断启动）。
    /// </summary>
    private static void InstallOrderOverride()
    {
        try
        {
            var original = AccessTools.Method(
                typeof(BaseChainloader<BasePlugin>), "ModifyLoadOrder");
            if (original == null)
            {
                OpsEv("[ORDER] HOOK-FAIL 未解析到 BaseChainloader<BasePlugin>.ModifyLoadOrder，加载序覆盖不生效");
                return;
            }
            var postfix = new HarmonyMethod(typeof(CleanerPatcher).GetMethod(
                nameof(ModifyLoadOrderPostfix),
                BindingFlags.NonPublic | BindingFlags.Static));
            new Harmony(OrderHarmonyId).Patch(original, postfix: postfix);
        }
        catch (Exception ex)
        {
            OpsEv("[ORDER] HOOK-FAIL " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// ModifyLoadOrder postfix（spec §2 验证签名）：读 pending.order → OrderReorder.Apply（纯内存，
    /// 不写任何文件——幂等不变量，order 由插件侧维护，清理器只消费）→ 依结果对 __result
    /// <b>原位</b>重排（先在副本上映射齐全再 Clear+Add，绝不替换 __result 引用——chainloader
    /// 后续用同一 list 对象）。无 order / order 空时零动作零证据；任何异常不阻断加载。
    /// </summary>
    private static void ModifyLoadOrderPostfix(object __instance, IList<PluginInfo> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0) return;

            List<string> order;
            try { order = McPending.LoadOrder(McPending.File); }
            catch (Exception ex)
            {
                OpsEv("[ORDER] SKIP pending 解析失败: " + ex.Message);
                return;
            }
            if (order == null || order.Count == 0) return;

            // PluginInfo → Node 投影（GUID + 全量依赖及硬/软标志），并留 Node→PluginInfo 反查（引用键）
            var ownerOf = new Dictionary<OrderReorder.Node, PluginInfo>(__result.Count);
            var projected = new List<OrderReorder.Node>(__result.Count);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in __result)
            {
                var node = new OrderReorder.Node { Guid = p?.Metadata?.GUID };
                if (p?.Dependencies != null)
                {
                    foreach (var d in p.Dependencies)
                    {
                        if (d == null || string.IsNullOrEmpty(d.DependencyGUID)) continue;
                        node.Deps.Add((d.DependencyGUID,
                            (d.Flags & BepInDependency.DependencyFlags.HardDependency) != 0));
                    }
                }
                projected.Add(node);
                ownerOf[node] = p;
                if (!string.IsNullOrEmpty(node.Guid)) guids.Add(node.Guid);
            }

            var applied = OrderReorder.Apply(projected, order, out var protected_);

            // 映射回 PluginInfo 并原位重排（applied 是输入同实例的排列，逐一映射必齐全）
            var reordered = new List<PluginInfo>(applied.Count);
            foreach (var node in applied)
                if (ownerOf.TryGetValue(node, out var p)) reordered.Add(p);
            if (reordered.Count != __result.Count)
            {
                OpsEv("[ORDER] SKIP 内部异常：重排映射不完整 " + reordered.Count + "/" + __result.Count);
                return;
            }
            __result.Clear();
            foreach (var p in reordered) __result.Add(p);

            // 对拍证据：order 命中统计 + 最终加载序（一行一项）+ 被保护项
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hits = 0;
            var misses = new List<string>();
            foreach (var og in order)
            {
                if (string.IsNullOrEmpty(og) || !seen.Add(og)) continue;
                if (guids.Contains(og)) hits++;
                else misses.Add(og);
            }
            OpsEv("[ORDER] 加载序覆盖生效：插件 " + __result.Count + " 个，order " + order.Count +
                  " 项（命中 " + hits + (misses.Count > 0
                      ? "，未命中 " + misses.Count + ": " + string.Join(",", misses)
                      : "") + "）");
            for (var i = 0; i < __result.Count; i++)
                OpsEv("[ORDER] #" + i.ToString("00") + " " + (__result[i]?.Metadata?.GUID ?? "?"));
            if (protected_.Count == 0) OpsEv("[ORDER-PROTECTED] none");
            else foreach (var s in protected_) OpsEv("[ORDER-PROTECTED] " + s);
        }
        catch (Exception ex)
        {
            OpsEv("[ORDER] FAIL " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}

/// <summary>
/// 加载序稳定重排纯函数（裁定 3：纯静态、零 IO，SelfTest 喂构造数据断言）。
/// 语义：order 命中项按 order 序前置，其余保持原相对序；硬依赖保护优先于 order——
/// 朴素序中排到某硬依赖之前的项被拓扑约束拉回其全部硬依赖之后（跟随依赖项位置，保持依赖在前）。
/// 算法：优先级 = order 命中序（重复/幽灵跳过）或 orderCount+原始下标（保证唯一），
/// Kahn 拓扑排序每步取"硬依赖已全部放置"的项中优先级最小者；依赖环兜底按原相对序尾置
/// （ModifyLoadOrder 输入本身拓扑成立，正常不可达）。软依赖不参与。
/// protected_ 输出被保护项名单（"guid(依赖 dep1,dep2)"，朴素序里排到硬依赖之前的项）。
/// </summary>
internal static class OrderReorder
{
    /// <summary>PluginInfo 的轻量投影：GUID + 全量依赖（含硬/软标志；Apply 只认硬依赖）。</summary>
    internal sealed class Node
    {
        public string Guid;                               // null=不可排序，保持原相对序
        public List<(string Guid, bool Hard)> Deps = new();
    }

    public static List<Node> Apply(IList<Node> current, List<string> order, out List<string> protected_)
    {
        protected_ = new List<string>();
        var n = current.Count;
        if (n == 0) return new List<Node>();

        // guid → 下标（OrdinalIgnoreCase，与手写 order 的容错一致；重复 guid 首个为准）
        var idx = new Dictionary<string, int>(n, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < n; i++)
        {
            var g = current[i].Guid;
            if (!string.IsNullOrEmpty(g) && !idx.ContainsKey(g)) idx[g] = i;
        }

        // 优先级：order 命中 = 命中序 0..k-1；未命中 = k + 原始下标（全表唯一 → 稳定全序）
        var prio = new int[n];
        for (var i = 0; i < n; i++) prio[i] = -1;
        var k = 0;
        if (order != null)
            foreach (var og in order)
            {
                if (string.IsNullOrEmpty(og)) continue;
                if (idx.TryGetValue(og, out var hit) && prio[hit] < 0) prio[hit] = k++;
            }
        for (var i = 0; i < n; i++)
            if (prio[i] < 0) prio[i] = k + i;

        // 硬依赖边（仅两端均在场的硬依赖；自依赖/重复边忽略）
        var deps = new List<int>[n];
        var indeg = new int[n];
        for (var i = 0; i < n; i++)
        {
            deps[i] = new List<int>(4);
            if (current[i].Deps == null) continue;
            foreach (var d in current[i].Deps)
            {
                if (!d.Hard || string.IsNullOrEmpty(d.Guid)) continue;
                if (idx.TryGetValue(d.Guid, out var j) && j != i && !deps[i].Contains(j))
                {
                    deps[i].Add(j);
                    indeg[i]++;
                }
            }
        }

        // 朴素序（order 前置 + 其余原相对序）→ 判定被保护项（排到自身硬依赖之前的项）
        var naive = new List<int>(n);
        for (var i = 0; i < n; i++) naive.Add(i);
        naive.Sort((a, b) => prio[a].CompareTo(prio[b]));
        var naivePos = new int[n];
        for (var t = 0; t < n; t++) naivePos[naive[t]] = t;
        for (var i = 0; i < n; i++)
        {
            var violated = new List<string>();
            foreach (var j in deps[i])
                if (naivePos[i] < naivePos[j]) violated.Add(current[j].Guid);
            if (violated.Count > 0)
                protected_.Add(current[i].Guid + "(依赖 " + string.Join(",", violated) + ")");
        }

        // Kahn 优先级拓扑：每步取可放置项（在场硬依赖已全放置）中优先级最小者
        var result = new List<Node>(n);
        var remaining = (int[])indeg.Clone();
        var placed = new bool[n];
        while (result.Count < n)
        {
            var best = -1;
            for (var i = 0; i < n; i++)
            {
                if (placed[i] || remaining[i] > 0) continue;
                if (best < 0 || prio[i] < prio[best]) best = i;
            }
            if (best < 0) break; // 依赖环兜底：剩余项按原相对序尾置（正常不可达）
            placed[best] = true;
            result.Add(current[best]);
            for (var i = 0; i < n; i++)
                if (!placed[i] && deps[i].Contains(best)) remaining[i]--;
        }
        for (var i = 0; i < n; i++)
            if (!placed[i]) result.Add(current[i]);
        return result;
    }
}
