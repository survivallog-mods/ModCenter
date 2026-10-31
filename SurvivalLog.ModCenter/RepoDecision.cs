using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SurvivalLog.ModCenter;

/// <summary>仓库决策五态（spec §4.1/D7/D8 定稿）：Ok 相等 / Update 远端新 / Downgrade 本地高
/// （D7 告警不拦截）/ Warn gamekb 不匹配（D8 告警不拦截）/ Install 未安装。</summary>
public enum RepoState { Ok, Update, Downgrade, Warn, Install }

/// <summary>
/// 仓库页一行（任务 6 冻结接口，T7/T8 面板消费）。Status 固定小写五值
/// "ok"|"update"|"downgrade"|"warn"|"install"（控制器裁定 2）。Why 在 Warn 与 Downgrade
/// 态非空（其余态空）；VerNote 为 D8"未校验游戏版本"注记
/// （基线不可用时任何 fetch 成功行可携带，不改 State——冻结面之外的增量字段）。
/// </summary>
public sealed class RepoRow
{
    public string Name;    // 显示名（= 仓键）
    public RepoState State;
    public string Local;   // 本地版本（PluginInfo.Metadata.Version）；null = 未安装
    public string Remote;  // 远端版本（update.json.version）；null = fetch 失败（面板按 null 渲染"检查失败"，State 不虚报）
    public string Why;     // warn 原因 / downgrade 提示；其余态空
    public string RepoKey; // 仓键（= 组织仓名，12 名之一，Ruling 5）
    public string Guid;    // 套件插件 GUID（com.local.survivallog.<小写仓键>）

    // ---- 冻结面之外的增量字段（T8 决策/PendingWriter 消费，不破坏冻结签名）----
    public string Gamekb;  // 远端 gamekb 快照名（update.json.gamekb，fetch 时附带）
    public string VerNote; // gamekb 未校验注记（D8："未校验游戏版本：…"）；基线可用 = 空
    public bool Kbw;       // 终审必修 6：Ok/Downgrade 行 gamekb 不符（不改 State，行内 ⊘ + 行级原因）
    public string KbWhy;   // 上述告警原因全文；空 = 无
    public int? HttpStatus; // T16/D：本轮 fetch 的 HTTP 状态（403=GitHub 限流；面板专门提示）

    /// <summary>面板态字符串（控制器裁定 2 固定口径，小写）。</summary>
    public string Status => State.ToString().ToLowerInvariant();
}

/// <summary>
/// 仓库决策层（ModCenter v2 生命周期任务 6）：12 仓清单 + 本地插件快照 + 五态决策。
/// 决策口径（spec §4.1 + D7/D8 作者拍板，均**只告警不拦截**）：
/// - 本地版本 &gt; 远端 = Downgrade（Why="远端低于本地"）；远端 &gt; 本地 = Update；相等 = Ok；
///   未安装 = Install；fetch 失败（Remote=null）= Ok + Remote null（五态无 fail 态，不虚报）。
/// - gamekb 告警（D8）：远端 gamekb ≠ 本地基线 → Warn（Install/Update 态）或
///   Kbw 标记（Ok/Downgrade 态——保持原态，主列表行内 ⊘ 仍给；终审必修 6）；
///   基线不可用（通道失败/无法映射）→ 不判 Warn/Kbw，VerNote="未校验游戏版本：…"。
/// <see cref="Build"/> 为纯函数（不碰 IO/interop，任意线程可调）；本地枚举在
/// <see cref="SnapshotLocal"/>（主线程，IL2CPPChainloader 契约）。
/// </summary>
public static class RepoDecision
{
    /// <summary>13 仓清单（Ruling 5：仓键 = 组织仓名；GUID 映射见 <see cref="GuidOf"/>）。
    /// 2026-10-31 +SaveMgr（第 13 仓，GUID 符合约定；Release 未发 → 面板显示未发版）。</summary>
    public static readonly string[] RepoKeys =
    {
        "BackpackExpand", "CabinetBurden", "CabinetEverywhere", "CabinetSize",
        "EndlessGuard", "FridgeChill", "ModCenter", "PlanPick",
        "PreDisasterSpeed", "QuickAction", "RobotSpeed", "RoleUnlock", "SaveMgr",
    };

    /// <summary>仓键 → 套件插件 GUID（com.local.survivallog.&lt;小写仓键&gt;）。</summary>
    public static string GuidOf(string repoKey) => "com.local.survivallog." + repoKey.ToLowerInvariant();

    /// <summary>套件 zip 内 dll 的 BepInEx 根相对路径（release.ps1 打包布局：BepInEx\plugins\SurvivalLog.&lt;mod&gt;.dll）。</summary>
    public static string DllRelOf(string repoKey) => "plugins/SurvivalLog." + repoKey + ".dll";

    /// <summary>
    /// 本地插件快照（**主线程**：IL2CPPChainloader.Instance.Plugins，v1 §4.1①实证 API）：
    /// 构造 N 行，Local 填 PluginInfo.Metadata.Version（SemanticVersioning.Version），
    /// 未安装 = null。Remote/Gamekb 由调用方 fetch 后附（T8 / 探针）。
    /// </summary>
    public static List<RepoRow> SnapshotLocal()
    {
        Dictionary<string, BepInEx.PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; }
        catch { plugins = null; } // Chainloader 未就绪（理论不可达：探针 10s 起跑晚于插件全 Load）

        var rows = new List<RepoRow>(RepoKeys.Length);
        foreach (var key in RepoKeys)
        {
            var guid = GuidOf(key);
            string ver = null;
            if (plugins != null && plugins.TryGetValue(guid, out var pi) && pi?.Metadata?.Version != null)
                ver = pi.Metadata.Version.ToString();
            rows.Add(new RepoRow { Name = key, RepoKey = key, Guid = guid, Local = ver });
        }
        return rows;
    }

    /// <summary>
    /// 五态决策（纯函数，就地改写 src 的 State/Why/VerNote 后返回）。localGame 省略 = 取
    /// <see cref="GameVersion.ReadCached"/>（ReadLocal 首调前 = 不可比对 → 未校验降级）；
    /// 探针 Step 4 传 <see cref="GameVersion.Local.Unverified"/> 强制通道失败路径。
    /// </summary>
    public static List<RepoRow> Build(List<RepoRow> src, GameVersion.Local localGame = null)
    {
        var local = localGame ?? GameVersion.ReadCached()
                    ?? GameVersion.Local.Unverified("本地版本通道未读取（ReadLocal 未调用）");

        foreach (var row in src)
        {
            row.Why = "";
            row.VerNote = "";
            row.Kbw = false;
            row.KbWhy = "";

            // fetch 失败：无可比远端——Ok + Remote=null（面板按 Remote null 渲染"检查失败"；不虚报 update/warn）
            if (string.IsNullOrEmpty(row.Remote))
            {
                row.State = RepoState.Ok;
                continue;
            }

            // 版本基态（D7：Downgrade 告警不拦截）
            RepoState basis;
            if (row.Local == null)
            {
                basis = RepoState.Install;
            }
            else
            {
                var cmp = CompareVersions(row.Local, row.Remote);
                if (cmp == null)
                {
                    basis = RepoState.Ok; // 远端版本不可解析（自家管线不会发生）：保守不动作，Remote 原样展示
                }
                else if (cmp > 0)
                {
                    basis = RepoState.Downgrade;
                    row.Why = $"远端 {row.Remote} 低于本地 {row.Local}（D7 告警不拦截）";
                }
                else if (cmp < 0)
                {
                    basis = RepoState.Update;
                }
                else
                {
                    basis = RepoState.Ok;
                }
            }
            row.State = basis;

            // gamekb 门（D8 告警不拦截）：fetch 成功且带 gamekb 的行参与
            if (!string.IsNullOrEmpty(row.Gamekb))
            {
                if (local.Baseline == null)
                {
                    // 通道失败/无法映射：不判 Warn，标"未校验游戏版本"（注记不改 State，任何基态可携带）
                    row.VerNote = "未校验游戏版本：" + (local.Note ?? "本地基线不可用") + "（不判兼容告警）";
                }
                else
                {
                    var why = GameVersion.GamekbMismatchReason(row.Gamekb, local);
                    if (why != null)
                    {
                        // 终审必修 6：D8 字面"主列表行内 ⊘"不限于可动作态——Ok/Downgrade
                        // 保持原态（无更新动作面）但仍带 Kbw 标记，面板行内给 ⊘。
                        row.Kbw = true;
                        row.KbWhy = why;
                        if (basis is RepoState.Install or RepoState.Update)
                        {
                            row.State = RepoState.Warn; // Warn 覆盖可动作态（要装/更的东西适配构建不一致才是告警主场景）
                            row.Why = why;
                        }
                    }
                }
            }
        }
        return src;
    }

    /// <summary>语义版本比较（本地/远端均为 x.y.z 形态）；任一不可解析 = null（保守不动作）。</summary>
    private static int? CompareVersions(string local, string remote)
    {
        if (!SemanticVersioning.Version.TryParse(remote, out var r) ||
            !SemanticVersioning.Version.TryParse(local, out var l))
            return null;
        return l.CompareTo(r);
    }
}

/// <summary>
/// pending 意图写入器（任务 6，控制器裁定 3）：对 <see cref="McPending"/> 的静态薄封装——
/// **主线程唯一写者**（全部方法只在主线程 Ticker/消息分发上下文调用；后台写 pending = 缺陷，
/// 清理器启动早期独占读改写，运行期单写者避免竞争。唯一例外：探针对**临时路径**的后台自证
/// 走 internal <see cref="AppendOps(string, List{McPending.Op})"/>，不写真 pending.json）。
/// 写前读旧清单合并（保留他人 ops，**旧 ops 在前新 ops 在后**）；Save 防抖不必（写频率极低——面板确认动作级）。
/// cfg 文件名 = GUID 命名 com.local.survivallog.&lt;mod&gt;.cfg（T4 实证 BepInEx cfg 按 GUID 命名）——
/// <see cref="AddDelete"/> 的 cfgRel 由调用方传，本封装不管映射。
/// 消费方：T8 更新/安装（AddUpdate）、T10 卸载（AddDelete）、T9 顺序（SetOrder）。
/// </summary>
public static class PendingWriter
{
    /// <summary>
    /// 更新/安装意图（T8）：install（未装，row.Local==null）/ replace（已装）一条 op。
    /// Target = plugins/SurvivalLog.&lt;RepoKey&gt;.dll；Staged = staging 绝对路径转 BepInEx 根相对
    /// ——T8 下载链解 zip 后暂存的 **dll**（清理器 install/replace 实测语义 = File.Copy(staged→target)
    /// 且先对 staged 本件强校验 Md5，故 op.Md5 取暂存 dll 自身 MD5；zip 整包 MD5（update.json.md5）
    /// 由下载链在解包前校验，此处不重复。info 仅契约保留）。
    /// </summary>
    public static void AddUpdate(RepoRow row, RepoClient.UpdateInfo info, string stagedAbsPath)
    {
        if (row == null || string.IsNullOrEmpty(row.RepoKey)) throw new ArgumentException("row/RepoKey 为空", nameof(row));
        if (info == null || string.IsNullOrEmpty(info.Md5)) throw new ArgumentException("info/Md5 为空（update.json 契约五字段）", nameof(info));
        if (string.IsNullOrEmpty(stagedAbsPath) || !File.Exists(stagedAbsPath))
            throw new ArgumentException("staged dll 不在场: " + stagedAbsPath, nameof(stagedAbsPath));

        AppendOps(McPending.File, new List<McPending.Op>
        {
            new()
            {
                Type = string.IsNullOrEmpty(row.Local) ? McPending.TypeInstall : McPending.TypeReplace,
                Target = RepoDecision.DllRelOf(row.RepoKey),
                Staged = ToRel(stagedAbsPath),
                Md5 = RepoClient.Md5Of(stagedAbsPath), // 清理器执行侧对 staged 本件强校验（T2 实测语义）
            },
        });

        // T16 自更新预置（壳的接口）：更新对象是 ModCenter 本体时，除上条 pending op
        // （撤销/待生效摘要/⟳ 徽标照常）外再写 BepInEx\ModCenter\selfupdate.flag——
        // 下一靴壳在 LoadFrom **之前**替换本体，当靴即加载新程序集。原因：本体被壳 LoadFrom 后
        // 同程序集身份不允许在本靴内被 Chainloader 重新加载，CleanupRunner 只能在下一靴落盘。
        // 写预置失败不影响已落盘的 pending op（旧 CleanupRunner 会自行写预置，代价=多一次重启）。
        if (!string.IsNullOrEmpty(row.Guid)
            && string.Equals(row.Guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase))
        {
            var targetAbs = Path.Combine(Paths.BepInExRootPath,
                RepoDecision.DllRelOf(row.RepoKey).Replace('/', '\\'));
            // 写预置失败不抛：pending op 已落盘，下次启动 CleanupRunner 会重试补写（代价=多一次重启）。
            if (!CleanupRunner.WriteSelfUpdateFlag(stagedAbsPath, RepoClient.Md5Of(stagedAbsPath), targetAbs))
                ModCenterPlugin.Log("warn 自更新预置写入失败（pending op 已落盘，下次启动重试）");
        }
    }

    /// <summary>卸载意图（T10）：dll + cfg 两条 delete op（cfgRel 传 GUID 命名 cfg 的 BepInEx 根相对
    /// 路径，如 "config/com.local.survivallog.robotspeed.cfg"；null = 仅删 dll）。</summary>
    public static void AddDelete(string dllRel, string cfgRel)
    {
        if (string.IsNullOrEmpty(dllRel)) throw new ArgumentException("dllRel 为空", nameof(dllRel));
        var add = new List<McPending.Op> { new() { Type = McPending.TypeDelete, Target = dllRel } };
        if (!string.IsNullOrEmpty(cfgRel))
            add.Add(new McPending.Op { Type = McPending.TypeDelete, Target = cfgRel });
        AppendOps(McPending.File, add);
    }

    /// <summary>顺序覆盖（T9）：ops 原样保留（不读不改），order 整体替换。</summary>
    public static void SetOrder(List<string> order)
    {
        var oldOps = SafeLoad(McPending.File, out var _);
        McPending.Save(McPending.File, oldOps, order ?? new List<string>());
    }

    /// <summary>
    /// 读旧→追加→原子写（合并语义：旧 ops 在前新 ops 在后，order 恒回传旧值不丢 T3 顺序覆盖）。
    /// internal 带路径重载供探针对临时文件自证合并语义（不碰真 pending.json——真文件会被
    /// 清理器下次启动消费，探针绝不写入假意图）。
    /// </summary>
    internal static List<McPending.Op> AppendOps(string path, List<McPending.Op> add)
    {
        var oldOps = SafeLoad(path, out var oldOrder);
        var merged = new List<McPending.Op>(oldOps.Count + add.Count);
        merged.AddRange(oldOps); // 旧在前
        merged.AddRange(add);    // 新在后
        McPending.Save(path, merged, oldOrder);
        return merged;
    }

    /// <summary>读旧清单（坏文件移存 .bad 自愈后从空表开始——坏 ops 清理器同样无法执行，留 .bad 可排障）。</summary>
    private static List<McPending.Op> SafeLoad(string path, out List<string> order)
    {
        try
        {
            order = McPending.LoadOrder(path);
            return McPending.Load(path);
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log($"warn {Path.GetFileName(path)} 解析失败，移存 .bad 后重写: {e.Message}");
            try { System.IO.File.Move(path, path + ".bad"); } catch { }
            order = new List<string>();
            return new List<McPending.Op>();
        }
    }

    /// <summary>绝对路径 → BepInEx 根相对（'/' 分隔；越界拒收——清理器 ResolveRel 同款语义）。</summary>
    private static string ToRel(string absPath)
    {
        if (string.IsNullOrEmpty(absPath)) throw new ArgumentException("stagedAbsPath 为空", nameof(absPath));
        var rel = Path.GetRelativePath(Paths.BepInExRootPath, absPath).Replace('\\', '/');
        if (rel.Length == 0 || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new ArgumentException("staged 不在 BepInEx 根下: " + absPath, nameof(absPath));
        return rel;
    }
}
