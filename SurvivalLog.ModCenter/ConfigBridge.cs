using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 配置桥（任务 6）：面板的数据引擎。API 面为任务 4 实证定案（DESIGN §4，逐字消费）：
/// IL2CPPChainloader.Instance.Plugins 枚举 → BasePlugin.Config 直取 → cfg[ConfigDefinition]
/// 非泛型取 entry（SettingType 判型 + BoxedValue 读写，写同样触发 ClampValue+SettingChanged 链）。
/// 职责：快照（BuildSnapshot + BuildTp 第三方页·任务 14 Tier 0 通用 / 任务 15 ThirdParty
/// 清单合并定制）/ 写（TrySet——套件走 Meta 登记键+联动 writeKeys 等值，第三方走通用路由）/ 启停（SetModEnabled——套件=任务 13 直写各 mod
/// 自己的 General.Enabled；第三方=任务 14 回用 T6 时代本插件 cfg [Disabled] 名单标记（重启级，
/// 任务 16 起换 dll .disabled 真禁用））/ 消息分发（MC_OPEN→__mcRender 快照；MC_SET→TrySet→
/// __mcEvent MC_SET_OK；MC_TOGGLE_MOD→回新快照）/ Save 防抖（首写 SaveOnConfigSet=false，
/// 3s 节流 + MC_CLOSE 立即落盘后恢复原值）。
/// v2 生命周期任务 7：待生效清单汇总——BuildSnapshot 前主线程读 pending.json（mtime 节流），
/// 归属到的 mod 行挂 ics ⟳ 徽标；Pump 每 2s 巡检，摘要变化推 MC_PENDING_SUMMARY（底部待生效条）。
/// v2 生命周期任务 8：套件仓库页——MC_REPO_GET{force}（打开自动查：10 分钟会话缓存）/ MC_CHK
/// （手动刷新：60 秒冷却）→ 后台 12 仓 fetch + RepoDecision.Build（主线程收割推 MC_REPO_DATA +
/// 快照侧栏 ↑/↓/⊘ 徽标）；MC_UPD/MC_INST/MC_UPDALL 确认后走**串行**下载链（后台：下载（进度
/// 250ms 节流推 MC_DL）→ zip MD5 对 update.json → 解包 BepInEx/plugins/*.dll 暂存 → 主线程
/// PendingWriter.AddUpdate）→ 快照刷新（行内 ⟳ 出现、↑/↓ 消失）。GitHub 匿名 60 次/h：一轮
/// 12 次，绝不后台轮询。
/// v2 生命周期任务 9：加载排序模式——MC_ORDER_GET（组装生效序：pending.order 命中项按序
/// 前置 + 其余 GUID 字母序，与清理器 OrderReorder 语义一致）→ 推 MC_ORDER_DATA
/// {guids[],rows[],def[]}；MC_ORDER_SAVE 校验（白名单=当前 Plugins 键、元素不重不漏=与可排序
/// 集精确相等，失败整单拒绝 MC_ORDER_FAIL）→ PendingWriter.SetOrder 整替 → 摘要即时推
/// （待生效条"加载顺序调整"，order==默认序不进摘要——与"删除 order 字段"等效，裁定 1）→
/// MC_ORDER_OK。T8 遗留三项顺手修（评审转交）：fetch 后台异常复位检查锁（Kind 5）/解包与
/// 链中途失败清暂存/面板 ST.dl 复位条件（panel.js 侧）。
/// v2 生命周期任务 10：彻底卸载——MC_UNINST {guid} 校验（Meta 套件/链上第三方/缺席行
/// （guid=dll 相对名）/ModCenter 自身四路由）构造真值明细（dll=DllDisabler.DllRelOf；
/// cfg=PluginInfo.Instance as BasePlugin→Config.ConfigFilePath，v1 §4.1③ 实证写法）回
/// MC_UNINST_DATA {files[],note,self}；MC_UNINST_DO → PendingWriter.AddDelete(dll+cfg)+
/// marker 清除（在链上第三方走 DllDisabler.Enable，缺席行走 Forget 只清不改名）+ 排序记录移除 +
/// FlushDirty（强杀免疫：意图即时原子落盘）+ 快照/摘要/仓库数据刷新。带出：RepoPendingGuids
/// 扩含 delete（卸载意图吃掉 ↑/↓ 徽标）；HandleOrderSave 体内兜底异常→MC_ORDER_FAIL（防保存钮
/// 在途卡死）；待生效条 order 覆盖档分"已保存未重启/生效中"（首拍 order 有效序相同或
/// pending.json mtime 早于本进程启动 = 本靴已应用 → 中性标签不计入计数）。
/// v2 生命周期任务 11：冲突体检 L1/L3——BuildSnapshot 时把 Chainloader 插件投影为 CfRow
/// （GUID/显示名/dll 文件名/L1 命中/功能域），按 Meta.KnownConflicts（GUID 优先、dll 文件名兜底）
/// 与 Meta.Domains（同域 ≥2 登记 mod）给每行挂 ics（red/gray，click=cf）；MC_CF {guid} →
/// MC_CF_DATA {guid,cards[]}（L1 cards + L3 同域卡，面板照原型 cfHtml 渲染）。快照 ics 与
/// 弹窗卡片共用 EntriesFor/DomainMates，杜绝两处口径漂移。
/// 线程纪律：全部 cfg 读写与分发都在 Ticker 主线程（Pump）；outbox 回调线程只入队；
/// 仓库 fetch/下载在后台 Task，结果经 RepoQueue 回主线程收割（PendingWriter 主线程唯一写者）。
/// </summary>
internal static class ConfigBridge
{
    // ---- 插件 ConfigFile 缓存（会话内不变；找不到也缓存 null 防重复扫） ----
    private static readonly Dictionary<string, ConfigFile> CfgCache = new();

    // ---- 消息入队（Eval 回调线程）→ Pump（主线程） ----
    private static readonly ConcurrentQueue<string> Inbox = new();

    // ---- Save 防抖：脏 ConfigFile → 原 SaveOnConfigSet；首写起 3s 节流落盘 ----
    private static readonly Dictionary<ConfigFile, bool> Dirty = new();
    private static float _dirtyAge = -1f; // -1 = 空闲
    private const float FlushInterval = 3f;

    // ---- v2 任务 7：待生效清单（pending.json）汇总——行内 ⟳ + 底部待生效条数据 ----
    // 全部主线程（Pump/BuildSnapshot 上下文）；mtime 节流读（Pump 每 2s 查一次，
    // 变了才重读），BuildSnapshot 前强制重读一次（MC_OPEN/MC_TOGGLE 均走快照）。
    private static List<McPending.Op> _pendOps = new();
    private static List<string> _pendOrder = new(); // v2 任务 9：pending.order（排序面/待生效摘要消费；空=无覆盖）
    private static List<string> _bootOrder; // T10：本进程首拍读到的 order（=启动清理器本靴实际应用的覆盖；null=尚未捕获）
    private static long _pendStamp = -1;   // pending.json LastWriteTimeUtc.Ticks（不存在=0）；-1=未读过
    private static string _pendSig;        // 摘要签名（变化才推送）；null=首拍未同步（只同步不推，MC_OPEN 会补推）
    private static float _pendAcc;         // Pump 2s 节拍累加器

    // ---- v2 任务 13：一键重启（MC_RESTART）——拉起成功后延迟一拍再退出，让面板 toast 先上屏 ----
    private static float _restartQuitAge = -1f; // -1=空闲；>=0=已请求重启，累加到 RestartQuitDelay 即 Application.Quit
    private static bool _restartRequested;      // 已拉起分离等待链（进程内只许一条，防连点双拉起/双开）
    private const float RestartQuitDelay = 1.6f;

    // ---- v2 任务 8：仓库页状态（全部主线程持有/变更；后台任务只经 RepoQueue 回传） ----
    private sealed class RepoUi
    {
        public int Kind;                          // 1=fetch 完成 2=下载进度 3=单仓链结果 4=链收尾 5=fetch 异常收尾（T9 顺手修）
        public List<RepoRow> Rows;                // 1
        public Dictionary<string, RepoClient.UpdateInfo> Infos; // 1
        public string RepoKey;                    // 2/3
        public int Pct;                           // 2
        public string Stage;                      // 2: down|check|unzip|write
        public bool Ok;                           // 3
        public string Err;                        // 3（失败原因）
        public RepoRow Row;                       // 3 ok（主线程 AddUpdate 用）
        public RepoClient.UpdateInfo Info;        // 3 ok
        public string Staged;                     // 3 ok（staging 内 dll 绝对路径）
    }

    private static readonly ConcurrentQueue<RepoUi> RepoQueue = new();
    private static List<RepoRow> _repoRows;      // 最近决策快照；null=从未查过（fetch 期间保持旧值，完成才整体换）
    private static readonly Dictionary<string, RepoClient.UpdateInfo> _repoInfos = new(StringComparer.Ordinal); // repoKey → fetch 结果（下载链直用，不二次 fetch）
    private static long _repoLastCheck;          // 上次完成一轮检查的 Unix 毫秒；0=从未
    private static bool _repoFetching;           // 后台 fetch 在跑（面板行"检查中…"）
    private static bool _repoChain;              // 下载链串行闸（全部更新=循环单链，绝不并发）
    private static string _dlKey;                // 当前下载链仓键；null=空闲（推送体 dl 字段）
    private static int _dlPct;
    private static string _dlStage;
    private const long RepoCacheMs = 10 * 60 * 1000;   // 打开自动查：10 分钟会话级缓存（控制器裁定 2）
    private const long RepoCooldownMs = 60 * 1000;     // 手动刷新：60 秒冷却
    private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static readonly string EvidencePath =
        System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_evidence.txt");

    // ============ 对外契约（任务 7/13/14 消费） ============

    /// <summary>全量快照：{self:{ver}, mods:[ModInfo], tp:[ModInfo]}（tp=第三方 Tier 0 通用页，任务 14）。
    /// v2 任务 7：快照前强制重读 pending.json，归属到的 mod 行挂 ⟳ 待生效徽标（ics:[{k:"amber",tip}]）。</summary>
    internal static Snapshot BuildSnapshot()
    {
        RefreshPending(true);
        var pendByRow = PendingTipsByRow(); // guid（小写）→ 行内 ⟳ tip
        var repoPend = RepoPendingGuids();  // v2 任务 8：有待生效 install/replace 意图的 guid（仓库行/侧栏徽标判定）
        var cfRows = InstalledCfRows();     // v2 任务 11：冲突体检投影（一次枚举 Plugins，全行复用）
        var snap = new Snapshot { Self = new SelfInfo { Ver = ModCenterPlugin.Version } };
        foreach (var mm in Meta.Mods)
        {
            var cfg = CfgOf(mm.Guid);
            var mi = new ModInfo
            {
                Guid = mm.Guid, Name = mm.Name, En = mm.En, Desc = mm.Desc,
                // 插件不在链上（dll 级缺失）视为停用；在场以该 mod 自己的 General.Enabled 为准
                // （任务 13 真值源；entry 缺失=旧版 dll，按 Bind 默认 true 兜底）
                Enabled = cfg != null && ReadModEnabled(mm, cfg, out _)
            };
            foreach (var mg in mm.Groups)
            {
                var gi = new GroupInfo { Title = mg.Title };
                foreach (var it in mg.Items) gi.Items.Add(BuildItem(cfg, it));
                mi.Groups.Add(gi);
            }
            if (mm.OvrHost != null) mi.Ovr = ReadOvr(cfg, mm.OvrHost);
            if (mm.BagOvrHost != null) mi.BagOvr = ReadBagOvr(cfg, mm.BagOvrHost);
            // v2 任务 11：冲突徽标置最前（L1 红/灰 + L3 同域灰；点击 cf 拉详情弹窗）
            // v2 任务 12：L2 碰撞橙标插在 L1 与 L3 之间（同 cf 弹窗复用）；停用行徽标弱化（M7）
            // v2 任务 8：随后仓库态（↑蓝可更新/↓橙可降级/↑+⊘版本告警，点击 upd 弹确认窗）
            // + 待生效 ⟳（T7）。已有待生效意图的行不再给可动作徽标（裁定 3：⟳ 出现、↑/↓ 消失）。
            var ics = CfIcsFor(cfRows, mm.Guid);
            if (!mi.Enabled) WeakenCfIcs(ics); // T11 M7：套件停用（General.Enabled=false）→ cf 徽标弱化+tip 前缀
            var repoIcs = RepoIcsOf(mm.Guid, repoPend);
            if (repoIcs != null) ics.AddRange(repoIcs);
            if (pendByRow.TryGetValue(mm.Guid, out var pendTip))
                ics.Add(new IcInfo { K = "amber", Tip = pendTip }); // ⟳ 纯状态（无 click）
            if (ics.Count > 0) mi.Ics = ics;
            snap.Mods.Add(mi);
        }
        BuildTp(snap, pendByRow, cfRows); // 任务 14：Tier 0 第三方通用页
        return snap;
    }

    /// <summary>
    /// 写一项：guid 可用完整 GUID 或短名（en，大小写不敏感，如 'RobotSpeed'）；
    /// key 可为主键、任一 writeKeys 或组合展示键（联动项）。类型按 entry.SettingType 转换
    /// （float/int/bool/string/enum），联动项 writeKeys 逐键等值写；写前标脏（关该文件自动落盘）。
    /// 任务 14：guid 解析不到 Meta 时落第三方通用路由（只写对方 cfg 实际存在的键）。
    /// </summary>
    internal static bool TrySet(string guidOrName, string section, string key, string raw)
    {
        var mm = Resolve(guidOrName);
        if (mm == null)
        {
            var tp = FindTp(guidOrName);
            if (tp != null) return TrySetTp(tp, section, key, raw);
            Log("set 拒绝：未知插件 " + guidOrName); return false;
        }
        var it = FindItem(mm, section, key);
        if (it == null) { Log($"set 拒绝：{mm.En} 无登记项 {section}.{key}（只写元数据表声明过的键）"); return false; }
        var cfg = CfgOf(mm.Guid);
        if (cfg == null) { Log("set 拒绝：" + mm.En + " 不在链上"); return false; }

        var first = Entry(cfg, it.Section, it.WKeys[0]);
        if (first == null) { Log($"set 拒绝：{mm.En} {it.Section}.{it.WKeys[0]} entry 不存在（任务 8 待新增？）"); return false; }
        if (!ConvertValue(raw, first.SettingType, out var val)) { Log($"set 拒绝：'{raw}' 无法转为 {first.SettingType.Name}"); return false; }

        MarkDirty(cfg); // 先关自动落盘再写（滑条连写防抖前提，DESIGN §4.3-2）
        foreach (var wk in it.WKeys)
        {
            var e = Entry(cfg, it.Section, wk);
            if (e == null) { Log($"set 中断：{it.Section}.{wk} entry 不存在"); return false; }
            e.BoxedValue = val; // 同值写被 BepInEx 短路（不发 SettingChanged，§4.3-1）
        }
        Log($"set {mm.En} {it.K} = {raw}");
        return true;
    }

    /// <summary>
    /// 启停（任务 13 实装）：套件直写目标 mod 自己的 <c>General.Enabled</c>（节/键按
    /// MetaMod.EnabledSection/EnabledKey，默认 General:Enabled）。写入即触发该 mod 现役
    /// SettingChanged→Rerun 链（各 mod Ticker 首行 gate 消费：热关闭=按原值缓存还原并停写，
    /// 重开=重新落笔），Save 走统一防抖。旧的本插件 cfg [Disabled] 名单机制已移除（避免双轨，
    /// 快照真值源=各 mod entry 本身）。
    /// 任务 14：guid 解析不到 Meta 时走第三方分支——回用 T6 时代 [Disabled] 名单标记
    /// （重启级语义；任务 16 起换"退出时 dll 改名 .disabled"真禁用，名单届时退役）。
    /// </summary>
    internal static void SetModEnabled(string guidOrName, bool enabled)
    {
        var mm = Resolve(guidOrName);
        if (mm == null)
        {
            var tp = FindTp(guidOrName);
            if (tp != null) { SetTpEnabled(tp, enabled); return; }
            if (DllDisabler.TrySetAbsent(guidOrName, enabled)) return; // 任务 16：缺席行（dll 已禁用）启用路由
            Log("toggle 拒绝：未知插件 " + guidOrName); return;
        }
        var cfg = CfgOf(mm.Guid);
        if (cfg == null) { Log("toggle 拒绝：" + mm.En + " 不在链上（dll 缺失？）"); return; }
        var en = Entry(cfg, mm.EnabledSection, mm.EnabledKey);
        if (en == null)
        { Log($"toggle 拒绝：{mm.En} 无 {mm.EnabledSection}.{mm.EnabledKey} entry（旧版 dll？）"); return; }
        if (!ConvertValue(enabled ? "true" : "false", en.SettingType, out var val))
        { Log($"toggle 拒绝：{mm.En} {mm.EnabledSection}.{mm.EnabledKey} 非 bool（{en.SettingType.Name}）"); return; }
        MarkDirty(cfg); // 先关自动落盘再写（统一防抖，同 TrySet）
        en.BoxedValue = val; // 同值写被 BepInEx 短路（不发 SettingChanged，§4.3-1）；值变化即触发目标 mod Rerun
        Log($"toggle {mm.En} → {(enabled ? "启用" : "停用")}（直写 {mm.Guid} {mm.EnabledSection}.{mm.EnabledKey}）");
    }

    /// <summary>读某 mod 的启停 entry（true/false）；entry 缺失=旧版 dll，按 Bind 默认 true 兜底。</summary>
    private static bool ReadModEnabled(MetaMod mm, ConfigFile cfg, out ConfigEntryBase entry)
    {
        entry = Entry(cfg, mm.EnabledSection, mm.EnabledKey);
        if (entry == null) return true;
        try
        {
            return Convert.ToBoolean(entry.BoxedValue, CultureInfo.InvariantCulture);
        }
        catch { return true; }
    }

    // ============ 快照细节 ============

    private static ItemInfo BuildItem(ConfigFile cfg, MetaItem it)
    {
        var e1 = cfg != null ? Entry(cfg, it.Section, it.WKeys[0]) : null;
        var ii = new ItemInfo { K = it.K, L = it.Label, B = it.Badge };
        if (e1 != null)
        {
            ii.T = TypeName(e1.SettingType);
            ii.V = e1.BoxedValue;
            ApplyRange(ii, e1); // 范围/白名单反射合入（元数据不写死为准）
        }
        else
        {
            ii.T = it.Type;   // entry 缺失（如 RoleUnlock Plan.* 待任务 8）：全走元数据兜底
            ii.V = it.Def;
        }
        if (ii.Min == null) ii.Min = it.Min;
        if (ii.Max == null) ii.Max = it.Max;
        ii.St = it.St; // 步进无反射来源，永远兜底
        if (ii.Opts == null && it.Opts != null) ii.Opts = it.Opts.ToList();
        // string 型 entry 无选项来源时按文本框渲染（任务 14；套件现役 string 项均带 Opts→sel 不受影响）
        if (ii.T == "sel" && ii.Opts == null && e1 != null && e1.SettingType == typeof(string)) ii.T = "str";
        ii.Note = it.Note ?? (it.Linked ? LinkedNote(cfg, it) : null);
        return ii;
    }

    /// <summary>范围/白名单反射合入（AcceptableValueRange/List 是 closed generic，走 GetType 反射；
    /// 任务 15 起 ThirdParty（Tier 1 清单页）共用——清单 min/max 覆盖在调用方合入后追加）。</summary>
    internal static void ApplyRange(ItemInfo ii, ConfigEntryBase e)
    {
        var av = e.Description != null ? e.Description.AcceptableValues : null;
        if (av != null)
        {
            var t = av.GetType();
            var pMin = t.GetProperty("MinValue");
            var pMax = t.GetProperty("MaxValue");
            if (pMin != null && pMax != null)
            {
                ii.Min = ToD(pMin.GetValue(av));
                ii.Max = ToD(pMax.GetValue(av));
            }
            else if (t.GetProperty("AcceptableValues") is { } pl && pl.GetValue(av) is System.Collections.IEnumerable en)
            {
                ii.Opts = en.Cast<object>()
                    .Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToList();
            }
        }
        if (ii.Opts == null && e.SettingType.IsEnum)
            ii.Opts = Enum.GetNames(e.SettingType).ToList();
    }

    /// <summary>联动项动态注记：当前 宽w×高h（v 展示宽度键值；宽高默认不一致的照实展示，拖动后同步）。</summary>
    private static string LinkedNote(ConfigFile cfg, MetaItem it)
    {
        string Fmt(ConfigEntryBase e) => e != null
            ? System.Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture)
            : "?";
        var w = cfg != null ? Entry(cfg, it.Section, it.WKeys[0]) : null;
        var h = cfg != null ? Entry(cfg, it.Section, it.WKeys[1]) : null;
        return "当前 宽" + Fmt(w) + "×高" + Fmt(h) + (it.LinkedSuffix ?? "");
    }

    private static List<OvrInfo> ReadOvr(ConfigFile cfg, MetaItem host)
    {
        var list = new List<OvrInfo>();
        var s = cfg != null ? Entry(cfg, host.Section, host.Key)?.BoxedValue as string : null;
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var part in s.Split(','))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var ix = seg.IndexOf(':');
            if (ix <= 0) continue;
            if (int.TryParse(seg.Substring(0, ix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                int.TryParse(seg.Substring(ix + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                list.Add(new OvrInfo { Id = id, Ms = ms });
        }
        return list;
    }

    /// <summary>bagOvr 清单解析（T20，柜子容量 个性化 Global.BagOverrides）："id:WxH,id:WxH" → [{id,w,h}]。
    /// 与 CabinetSize SizeEngine.ParseOverrides 同格式同容错（坏条目静默跳过）；写路径=面板整串序列化走常规 TrySet（绑定 string entry）。</summary>
    private static List<BagOvrInfo> ReadBagOvr(ConfigFile cfg, MetaItem host)
    {
        var list = new List<BagOvrInfo>();
        var s = cfg != null ? Entry(cfg, host.Section, host.Key)?.BoxedValue as string : null;
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var part in s.Split(','))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var ix = seg.IndexOf(':');
            if (ix <= 0) continue;
            if (!int.TryParse(seg.Substring(0, ix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0) continue;
            var dims = seg.Substring(ix + 1).Trim();
            var x = dims.IndexOf('x'); if (x < 0) x = dims.IndexOf('X');
            if (x <= 0) continue;
            if (!int.TryParse(dims.Substring(0, x), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
                !int.TryParse(dims.Substring(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) continue;
            list.Add(new BagOvrInfo { Id = id, W = w, H = h });
        }
        return list;
    }

    private static double? ToD(object o) =>
        o == null ? (double?)null : System.Convert.ToDouble(o, CultureInfo.InvariantCulture);

    internal static string TypeName(Type t)
    {
        if (t == typeof(float) || t == typeof(double)) return "float";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
            return "int";
        if (t == typeof(bool)) return "bool";
        return "sel"; // enum / string / 其他 → 下拉
    }

    // ============ Tier 0 第三方通用页（任务 14，接入协议 docs/modcenter-integration-guide.md §二） ============

    // 第一方 GUID 前缀：套件 9 mod（Meta 已登记）/ ModCenter 自身 / 探针三件 / CabinetBurden、
    // EndlessGuard 等 Meta 未登记的本仓 mod 全在此族——一律不进第三方分组。
    private const string FirstPartyPrefix = "com.local.survivallog.";
    // 探针豁免名（BepInPlugin.Name 含其一即豁免；GUID 前缀已覆盖现役三件，此为改名/换族双保险）
    private static readonly string[] TpProbeNames = { "ActionProbe", "ViewportProbe", "ConfigDumpProbe" };

    /// <summary>
    /// 枚举 Chainloader 全插件 → 未登记且未豁免 → 通用页：有 cfg entry 逐条生成 Item
    /// （section 分组沿用对方 cfg 节名；徽标一律 cfg；Description→note）；无 entry →
    /// 仅启停形态（空 groups + desc 说明，同灾前加速页）。按 Name 排序稳定输出。
    /// </summary>
    private static void BuildTp(Snapshot snap, Dictionary<string, string> pendByRow, List<CfRow> cfRows)
    {
        Dictionary<string, PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch { }
        if (plugins == null) return;
        BepInEx.PluginInfo[] list;
        try { list = plugins.Values.OrderBy(p => p.Metadata?.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception e) { Log("warn tp 枚举失败: " + e.Message); return; }
        foreach (var info in list)
        {
            try
            {
                var md = info?.Metadata;
                if (md == null || string.IsNullOrEmpty(md.GUID) || info.Instance == null) continue;
                if (TpExempt(md.GUID, md.Name)) continue;
                snap.Tp.Add(BuildTpMod(info, pendByRow, cfRows));
            }
            catch (Exception e) { Log("warn tp 构建跳过 " + (info?.Metadata?.GUID ?? "?") + ": " + e.Message); }
        }
        // 任务 16：dll 已禁用（.disabled 在场）→ 未加载 → 链上枚举不可见；补缺席行供玩家重新启用
        foreach (var dllRel in DllDisabler.AbsentDisabled())
        {
            var nm = dllRel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? dllRel.Substring(0, dllRel.Length - 4) : dllRel;
            snap.Tp.Add(new ModInfo
            {
                Guid = dllRel, // 未加载无 GUID，用 dll 相对名作路由键（DllDisabler.TrySetAbsent 消费）
                Name = nm,
                En = nm,
                Enabled = false,
                Absent = true,
                Desc = "第三方 mod：dll 已禁用（当前未加载）。此处启用将恢复 dll 文件，下次启动正常加载。",
            });
        }
        if (snap.Tp.Count > 0)
            Log("tp 快照: " + snap.Tp.Count + " 个第三方条目（" + string.Join(", ", snap.Tp.Select(m => m.En)) + "）");
    }

    /// <summary>豁免判定：Meta 登记（套件 9）/ ModCenter 自身 / 第一方 GUID 族 / 探针名。</summary>
    private static bool TpExempt(string guid, string name)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            foreach (var mm in Meta.Mods)
                if (string.Equals(mm.Guid, guid, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase)) return true;
            if (guid.StartsWith(FirstPartyPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrEmpty(name))
            foreach (var p in TpProbeNames)
                if (name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>
    /// 逐插件构建第三方页：先按 Tier 0 通用页（枚举 cfg entry → 节名分组/键名标签/徽标 cfg/
    /// Description→note/AcceptableValueRange 反射，任务 14）；随后交任务 15 清单合并层
    /// <see cref="ThirdParty.Apply"/>——插件程序集内嵌 modcenter.json（mcSpec 1）有效时
    /// display/summary/groups/label/badge/min-max-step/order 全量覆盖为中文定制页
    /// （清单没列的键不出现；清单键不在 cfg → 该项隐藏），无效时保持 Tier 0 形态。
    /// 任务 16：启停真值源=DllDisabler [DisabledTp] marker 取反（下次启动由启动清理器改名不加载，T4）；
    /// Tier 2 软停能力标记（约定类型在场或清单 softDisable）与 QueryStates 状态回报随后合入。
    /// </summary>
    private static ModInfo BuildTpMod(BepInEx.PluginInfo info, Dictionary<string, string> pendByRow, List<CfRow> cfRows)
    {
        var md = info.Metadata;
        var dllRel = DllDisabler.DllRelOf(info);
        var mi = new ModInfo
        {
            Guid = md.GUID,
            Name = md.Name,          // Tier 0：显示名 = 插件自报名（无中文定制，Tier 1 清单才有）
            En = md.Name,
            Enabled = dllRel == null || !DllDisabler.MarkerOf(dllRel), // 任务 16：[DisabledTp] marker 取反
            Desc = "第三方 mod（Tier 0 自动识别）：通用页由其 cfg 自动生成；写入即触发其 SettingChanged，生效视该 mod。",
        };
        // v2 任务 11：冲突徽标（L1 命中=红/灰；L3 同域=灰）——GUID 优先、dll 名兜底在 CfRows 内解析
        var cfIcs = CfIcsFor(cfRows, md.GUID);
        if (cfIcs.Count > 0) mi.Ics = cfIcs;
        if (pendByRow.TryGetValue(md.GUID, out var pendTip)) // v2 任务 7：GUID 命名 cfg 意图归属到 tp 行
        {
            mi.Ics ??= new List<IcInfo>();
            mi.Ics.Add(new IcInfo { K = "amber", Tip = pendTip });
        }
        ConfigFile cfg = null;
        try { if (info.Instance is BasePlugin bp) cfg = bp.Config; } catch { }

        if (ThirdParty.Apply(info, mi, cfg))
        {
            ApplyStates(mi, ThirdParty.QueryStatesOf(info)); // Tier 2 状态回报（清单定制页组之后）
            mi.Soft = ThirdParty.SoftOf(info); // 两层语义标记（面板横幅/toast 措辞分流）
            return mi;
        }
        var defs = new List<ConfigDefinition>();
        if (cfg != null) { try { defs.AddRange(cfg.Keys); } catch { } } // Keys=现役 API（ConfigDefinitions 已过时）

        // section 分组沿用对方 cfg 节名（首现顺序）；空文件 = 仅启停形态（空 groups）
        var groups = new Dictionary<string, GroupInfo>();
        foreach (var def in defs)
        {
            if (def == null || string.IsNullOrEmpty(def.Key)) continue;
            ConfigEntryBase e = null;
            try { e = cfg[def]; } catch { }
            if (e == null) continue;
            if (!groups.TryGetValue(def.Section, out var g))
            {
                g = new GroupInfo { Title = def.Section };
                groups[def.Section] = g;
                mi.Groups.Add(g);
            }
            g.Items.Add(BuildTpItem(def, e));
        }
        if (mi.Groups.Count == 0)
            mi.Desc = "第三方 mod（Tier 0 自动识别）：无公开 cfg 配置，仅支持启用/停用（重启级）。";
        ApplyStates(mi, ThirdParty.QueryStatesOf(info));
        mi.Soft = ThirdParty.SoftOf(info);
        return mi;
    }

    /// <summary>Tier 2 状态回报合入（任务 16）：Key "Section:Key" → 定位项 K "Section.Key" 置灰
    /// （ItemInfo.Fail=Reason）；空 Key=整 mod 级（Desc 追加警示）。State 只认 ok，非 ok 一律 failed。</summary>
    private static void ApplyStates(ModInfo mi, List<ThirdParty.McState> states)
    {
        if (states == null) return;
        foreach (var st in states)
        {
            if (string.IsNullOrEmpty(st.Key))
            {
                if (!string.Equals(st.State, "ok", StringComparison.OrdinalIgnoreCase))
                    mi.Desc += "\n⚠ " + (string.IsNullOrEmpty(st.Reason) ? "状态异常：" + st.State : st.Reason);
                continue;
            }
            var want = st.Key.Replace(':', '.');
            foreach (var g in mi.Groups)
                foreach (var it in g.Items)
                    if (string.Equals(it.K, want, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(st.State, "ok", StringComparison.OrdinalIgnoreCase))
                        it.Fail = string.IsNullOrEmpty(st.Reason) ? "状态：" + st.State : st.Reason;
        }
    }

    /// <summary>逐 entry→Item：键名即标签（英文）、k=节.键、徽标一律 cfg、Description→note、
    /// AcceptableValueRange/List 反射范围/选项（无范围的 string→文本框 str）、步进兜底 整型1/浮点0.1。</summary>
    private static ItemInfo BuildTpItem(ConfigDefinition def, ConfigEntryBase e)
    {
        var ii = new ItemInfo
        {
            K = def.Section + "." + def.Key,
            L = def.Key,
            B = "cfg",
            T = TypeName(e.SettingType),
            V = e.BoxedValue,
            Note = e.Description?.Description,
        };
        ApplyRange(ii, e);
        if (ii.T == "sel" && ii.Opts == null && e.SettingType == typeof(string)) ii.T = "str";
        if (ii.St == null && ii.Min != null) ii.St = ii.T == "int" ? 1 : 0.1; // tp 无元数据步进来源
        return ii;
    }

    /// <summary>第三方启停（任务 16 分流，指南 §五）：套件→直写 General.Enabled（上方主流程）；
    /// 第三方→Tier 2 软停回调优先（OnEnabledChanged 返回 true=行为已立即停止），清单 softDisable
    /// 次之（自实现 gate）；两者都无=重启级。三层统一写 [DisabledTp] marker（下次启动由启动清理器
    /// 改名 .disabled 不加载，T4——Tier 2 行的"下次启动不加载"由此兜底），区别只在面板文案语义。</summary>
    private static void SetTpEnabled(BepInEx.PluginInfo tp, bool enabled)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) { Log("toggle 拒绝：ModCenter 宿主未就绪"); return; }
        var dllRel = DllDisabler.DllRelOf(tp);
        if (string.IsNullOrEmpty(dllRel)) { Log("toggle 拒绝：插件 Location 缺失 " + tp.Metadata.GUID); return; }
        var name = tp.Metadata.Name;
        if (enabled)
        {
            var restored = ThirdParty.FireEnabledChanged(tp, true); // Tier 2 恢复回调（无契约/降级=false 静默）
            DllDisabler.Enable(dllRel);
            Log($"toggle(tp) {name} → 启用（Tier2 回调 {(restored ? "已恢复" : "无/降级")}；marker 清除）");
        }
        else
        {
            var soft = ThirdParty.FireEnabledChanged(tp, false);
            var viaManifest = ThirdParty.SoftDisableOf(tp.Metadata.GUID);
            DllDisabler.Disable(dllRel);
            Log($"toggle(tp) {name} → 停用（"
                + (soft ? "Tier2 软停回调已处理，行为立即停止"
                        : viaManifest ? "清单 softDisable：对方自实现 gate（面板按软停语义展示）"
                                      : "重启级：本会话仍在运行，下次启动清理器改名 .disabled")
                + $"；marker [{dllRel}]=true）");
        }
    }

    /// <summary>第三方插件解析（完整 GUID / 插件名 / GUID 尾段，大小写不敏感；仅未豁免者）。</summary>
    private static BepInEx.PluginInfo FindTp(string guidOrName)
    {
        if (string.IsNullOrEmpty(guidOrName)) return null;
        Dictionary<string, PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch { }
        if (plugins == null) return null;
        foreach (var info in plugins.Values)
        {
            var md = info?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            if (TpExempt(md.GUID, md.Name)) continue;
            if (string.Equals(md.GUID, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
            if (string.Equals(md.Name, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
            var tail = md.GUID.Substring(md.GUID.LastIndexOf('.') + 1);
            if (string.Equals(tail, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
        }
        return null;
    }

    /// <summary>第三方通用写：按 section/key 直取对方 entry（无登记表——只写实际存在的键，
    /// 指南 §二核心原则；面板发整展示键 k 原串时剥一次节前缀兜底）。写入触发其 ClampValue+
    /// SettingChanged 链（对方订阅即热响应），Save 走统一防抖。</summary>
    private static bool TrySetTp(BepInEx.PluginInfo tp, string section, string key, string raw)
    {
        ConfigFile cfg = null;
        try { if (tp.Instance is BasePlugin bp) cfg = bp.Config; } catch { }
        if (cfg == null) { Log("set 拒绝：" + tp.Metadata.Name + " 无 ConfigFile"); return false; }
        var e = Entry(cfg, section, key);
        if (e == null && !string.IsNullOrEmpty(section) && key != null
            && key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase))
            e = Entry(cfg, section, key.Substring(section.Length + 1));
        if (e == null) { Log($"set 拒绝：{tp.Metadata.Name} 无 entry {section}.{key}（只写实际存在的键）"); return false; }
        if (!ConvertValue(raw, e.SettingType, out var val)) { Log($"set 拒绝：'{raw}' 无法转为 {e.SettingType.Name}"); return false; }
        MarkDirty(cfg);
        e.BoxedValue = val;
        Log($"set(tp) {tp.Metadata.Name} {section}.{key} = {raw}");
        return true;
    }

    // ============ 底层查找 ============

    private static ConfigFile CfgOf(string guid)
    {
        if (CfgCache.TryGetValue(guid, out var c)) return c;
        try
        {
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            var info = plugins?.Values.FirstOrDefault(p => p.Metadata.GUID == guid);
            if (info?.Instance is BasePlugin bp && bp.Config != null) c = bp.Config;
        }
        catch { }
        CfgCache[guid] = c;
        return c;
    }

    internal static ConfigEntryBase Entry(ConfigFile cfg, string section, string key)
    {
        try { return cfg[new ConfigDefinition(section, key)]; }
        catch { return null; }
    }

    /// <summary>guid 或短名解析（完整 GUID / En / GUID 尾段，大小写不敏感；'RobotSpeed'、'robotspeed' 均可）。</summary>
    private static MetaMod Resolve(string guidOrName)
    {
        if (string.IsNullOrEmpty(guidOrName)) return null;
        foreach (var mm in Meta.Mods)
        {
            if (string.Equals(mm.Guid, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
            if (string.Equals(mm.En, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
            var tail = mm.Guid.Substring(mm.Guid.LastIndexOf('.') + 1);
            if (string.Equals(tail, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
        }
        return null;
    }

    private static MetaItem FindItem(MetaMod mm, string section, string key)
    {
        foreach (var g in mm.Groups)
            foreach (var it in g.Items)
                if (Match(it, section, key)) return it;
        if (mm.OvrHost != null && Match(mm.OvrHost, section, key)) return mm.OvrHost;
        if (mm.BagOvrHost != null && Match(mm.BagOvrHost, section, key)) return mm.BagOvrHost; // T20 个性化清单整串写（面板 key=完整展示键 "Global.BagOverrides"，命中 K 分支）
        return null;
    }

    private static bool Match(MetaItem it, string section, string key)
    {
        // T17：完整展示键逐字命中 = 最强匹配，节名前置校验不再否决——面板 routeOf 对
        // "节.键"做首点切分，点分节名（CabinetSize v3 的 Tier.Small 等）会被切成错节
        // （"Tier.Small.Width"→section="Tier"），旧序（节守卫在 K 判之前）该类写必拒。
        // K 全串在 mod 内唯一（含联动"节.键A + 键B"），前置放行安全；短键/ovr 分支仍走
        // 节守卫（ovrCommit 发 key='Overrides'，只可能命中 it.Key 分支，行为不变）。
        if (string.Equals(it.K, key, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(it.Section, section, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(it.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
        return it.WKeys.Any(wk => string.Equals(wk, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ConvertValue(string raw, Type t, out object val)
    {
        val = null;
        if (raw == null) return false;
        try
        {
            if (t == typeof(float))
            {
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                val = f; return true;
            }
            if (t == typeof(double))
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
                val = d; return true;
            }
            if (t == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
                val = i; return true;
            }
            if (t == typeof(bool))
            {
                if (!bool.TryParse(raw, out var b)) return false;
                val = b; return true;
            }
            if (t == typeof(string)) { val = raw; return true; }
            if (t.IsEnum) { val = Enum.Parse(t, raw, true); return true; }
            // 其余整型兜底
            if (t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort))
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return false;
                val = System.Convert.ChangeType(l, t, CultureInfo.InvariantCulture); return true;
            }
            return false;
        }
        catch { return false; }
    }

    // ============ 待生效清单汇总（v2 任务 7：行内 ⟳ + MC_PENDING_SUMMARY 推送） ============

    /// <summary>读 pending.json 到 <see cref="_pendOps"/>（主线程）。force=true 无条件读（BuildSnapshot 前）；
    /// 否则 mtime 未变跳过（Pump 2s 节拍）。文件不存在=空清单（stamp 0）；坏文件按空清单展示并 Warn。</summary>
    private static void RefreshPending(bool force)
    {
        long stamp = 0;
        try
        {
            if (System.IO.File.Exists(McPending.File))
                stamp = System.IO.File.GetLastWriteTimeUtc(McPending.File).Ticks;
        }
        catch { }
        if (!force && stamp == _pendStamp) return;
        _pendStamp = stamp;
        try
        {
            _pendOps = McPending.Load(McPending.File);
            _pendOrder = McPending.LoadOrder(McPending.File); // v2 任务 9：order 与 ops 同文件同拍读
            // T10（T9 改进项）：首拍 order = 启动清理器本靴实际应用的覆盖（此后任何写入都不改变
            // "已应用"这一事实；仅用于待生效条中性档判定，不参与排序面校验）。
            _bootOrder ??= new List<string>(_pendOrder);
        }
        catch (Exception e)
        {
            Log("warn pending.json 解析失败（按空清单展示）: " + e.Message);
            _pendOps = new List<McPending.Op>();
            _pendOrder = new List<string>();
            _bootOrder ??= new List<string>();
        }
    }

    /// <summary>待生效分组：按归属行（guid）合并 ops——同 mod 的 dll+cfg 两条 delete 合成一档
    /// "卸载 X"；归属不到行（第三方 dll 路径等）按目标文件自成一组，仍进底部条摘要。</summary>
    private sealed class PendGroup
    {
        public string RowKey;            // 归属行键（guid 小写）；null=不归属任何行
        public string Name;              // 显示名（Meta 中文名 / GUID 尾段 / 文件名）
        public readonly List<string> Verbs = new(); // 动作词（有序去重）
        public string FixedLabel;        // 整档固定标签（v2 任务 9：order 覆盖="加载顺序调整"，绕过动词拼装）
        public bool Neutral;             // v2 任务 10（T9 改进项）：中性档——本靴已应用（如 order 生效中），不计入待生效计数
    }

    private static readonly string[] PendVerbOrder = { "卸载", "安装", "更新", "停用", "启用" };

    private static List<PendGroup> PendGroups()
    {
        var groups = new List<PendGroup>();
        var byKey = new Dictionary<string, PendGroup>(StringComparer.Ordinal);
        foreach (var op in _pendOps)
        {
            var rowKey = PendRowKeyOf(op.Target);
            var gkey = rowKey ?? "#" + (op.Target ?? "").Replace('/', '\\').ToLowerInvariant();
            if (!byKey.TryGetValue(gkey, out var g))
            {
                g = new PendGroup
                {
                    RowKey = rowKey,
                    Name = rowKey != null ? PendDisplayName(rowKey) : PendFileName(op.Target),
                };
                byKey[gkey] = g;
                groups.Add(g);
            }
            var verb = op.Type switch
            {
                McPending.TypeDelete => "卸载",
                McPending.TypeInstall => "安装",
                McPending.TypeReplace => "更新",
                McPending.TypeDisable => "停用",
                McPending.TypeEnable => "启用",
                _ => null,
            };
            if (verb != null && !g.Verbs.Contains(verb)) g.Verbs.Add(verb);
        }
        // v2 任务 9：order 覆盖档——RowKey=null（全局非单 mod：行内 ⟳ 永不挂行，底部条承载）；
        // order==默认序（GUID 字母序）不进摘要——"恢复默认存字母序"与删除 order 字段等效（裁定 1/5）。
        // v2 任务 10（T9 改进项）：覆盖在场但本靴已应用（首拍有效序相同，或 pending.json mtime
        // 早于本进程启动）→ 挂中性标签"自定义加载序生效中"（不计入待生效计数，面板弱化样式）；
        // 未重启维持原"加载顺序调整"计入计数。
        if (_pendOrder.Count > 0 && !OrderEqualsDefault(_pendOrder))
        {
            var applied = OrderAppliedThisBoot();
            groups.Add(new PendGroup
            {
                RowKey = null,
                FixedLabel = applied ? "自定义加载序生效中" : "加载顺序调整",
                Neutral = applied,
            });
        }
        return groups;
    }

    /// <summary>order 覆盖是否本靴已应用（T9 改进项，任务 10 实装）：
    /// ①首拍 order 与现值对"本靴仍将生效的 mod 集"产生的有效加载序相同 → 已应用。比原件更准：
    /// 卸载确认会从 order 移除该 mod（记录清理），其余 mod 的相对序并未变；面板任何 pending
    /// 写入抬 mtime 也不影响"本靴已应用"事实——避免 T10 主链路的必现误报；②裁定原口径：
    /// pending.json LastWriteTimeUtc 早于当前进程启动时间（Process.GetCurrentProcess().StartTime）。
    /// StartTime 读取异常按未应用兜底（保守方向：宁可多提示重启）。</summary>
    private static bool OrderAppliedThisBoot()
    {
        try
        {
            if (_bootOrder != null && _pendOrder.Count > 0 && SameEffectiveOrder(_bootOrder, _pendOrder))
                return true;
        }
        catch { }
        try
        {
            if (!System.IO.File.Exists(McPending.File)) return false;
            var startUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            return System.IO.File.GetLastWriteTimeUtc(McPending.File) < startUtc;
        }
        catch { return false; }
    }

    /// <summary>两份 order 覆盖对"当前仍会加载的 mod 集"产生的有效加载序是否一致（清理器
    /// OrderReorder / MC_ORDER_GET 同语义：命中项按覆盖序前置、其余按默认序接后；待删除意图的
    /// mod 排除——它们下次启动不再存在，其 order 条目被移除不改变其余 mod 的相对序）。</summary>
    private static bool SameEffectiveOrder(List<string> a, List<string> b)
    {
        var del = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in _pendOps)
            if (op.Type == McPending.TypeDelete)
            {
                var g = PendRowKeyOf(op.Target);
                if (!string.IsNullOrEmpty(g)) del.Add(g);
            }
        var avail = new List<string>();
        var availSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in OrderableGuids()) // 已字母序（默认序）
            if (!del.Contains(g) && availSet.Add(g)) avail.Add(g);
        return EffectiveOrder(a, avail, availSet).SequenceEqual(
            EffectiveOrder(b, avail, availSet), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>有效加载序：order 命中且在场项去重前置 + 其余按默认序（与清理器/排序面同口径）。</summary>
    private static List<string> EffectiveOrder(List<string> order, List<string> avail,
        HashSet<string> availSet)
    {
        var res = new List<string>(avail.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in order)
            if (!string.IsNullOrEmpty(g) && availSet.Contains(g) && seen.Add(g)) res.Add(g);
        foreach (var g in avail)
            if (seen.Add(g)) res.Add(g);
        return res;
    }

    /// <summary>组标签（底部待生效条 + 行内 ⟳ tip 复用）：动词+名（多动词按既定序 "、" 连接）。</summary>
    private static string PendLabel(PendGroup g)
    {
        if (g.FixedLabel != null) return g.FixedLabel; // v2 任务 9：order 整档固定标签
        var ordered = PendVerbOrder.Where(g.Verbs.Contains).ToList();
        if (ordered.Count == 0) ordered = g.Verbs.ToList();
        return string.Join("、", ordered.Select(v => v + " " + g.Name));
    }

    /// <summary>行归属：Target（BepInEx 根相对）→ guid（小写）。套件 dll（plugins/SurvivalLog.&lt;仓键&gt;.dll，
    /// RepoDecision.DllRelOf 同款布局）与 GUID 命名 cfg（config/&lt;guid&gt;.cfg，T4 实证命名）可归属；
    /// 第三方 dll 按链上插件的 DllRelOf 反查 GUID（T10：卸载的 dll+cfg 两条 delete 才能合成一档
    /// "卸载 X"，否则 dll 组与 cfg 组各报一条）；反查不到（已卸载/未加载）留空只进底部条摘要。</summary>
    private static string PendRowKeyOf(string target)
    {
        if (string.IsNullOrEmpty(target)) return null;
        var t = target.Replace('/', '\\').Trim().TrimStart('\\').ToLowerInvariant();
        const string plugins = "plugins\\", config = "config\\";
        if (t.StartsWith(plugins, StringComparison.Ordinal))
        {
            var f = t.Substring(plugins.Length);
            if (!f.EndsWith(".dll", StringComparison.Ordinal)) return null;
            var stem = f.Substring(0, f.Length - 4);
            if (stem.StartsWith("survivallog.", StringComparison.Ordinal))
                return "com.local.survivallog." + stem.Substring("survivallog.".Length);
            // T10：第三方 dll → 链上插件 GUID（DllRelOf 口径，忽略大小写/分隔符差异）
            foreach (var p in LoadedPlugins())
            {
                var rel = DllDisabler.DllRelOf(p);
                if (string.IsNullOrEmpty(rel)) continue;
                if (string.Equals(rel.Replace('/', '\\').ToLowerInvariant(), f, StringComparison.Ordinal))
                    return p.Metadata?.GUID?.ToLowerInvariant();
            }
            return null;
        }
        if (t.StartsWith(config, StringComparison.Ordinal) && t.EndsWith(".cfg", StringComparison.Ordinal))
        {
            var stem = t.Substring(config.Length, t.Length - config.Length - 4);
            return stem.IndexOf('.') == -1 ? null : stem; // GUID 命名 cfg（含点）；无点=旧式文件名不归属
        }
        return null;
    }

    /// <summary>guid → 显示名：Meta 九表中文名优先，其次链上插件自报名，最后 GUID 尾段（如 modcenter）。</summary>
    private static string PendDisplayName(string guidLower)
    {
        foreach (var mm in Meta.Mods)
            if (string.Equals(mm.Guid, guidLower, StringComparison.OrdinalIgnoreCase)) return mm.Name;
        if (string.Equals(guidLower, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase))
            return "Mod 管理中心"; // 与 HandleOrderGet 的自身显示名一致
        foreach (var p in LoadedPlugins())
            if (string.Equals(p?.Metadata?.GUID, guidLower, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(p.Metadata?.Name)) return p.Metadata.Name;
        var ix = guidLower.LastIndexOf('.');
        return ix == -1 ? guidLower : guidLower.Substring(ix + 1);
    }

    /// <summary>目标文件名（去扩展名）——归属不到行时的组名。先剥 .disabled 再剥扩展名
    /// （T10：缺席行卸载目标是 <c>X.dll.disabled</c>，组名应为 "X" 而非 "X.dll"）。</summary>
    private static string PendFileName(string target)
    {
        if (string.IsNullOrEmpty(target)) return "?";
        var n = target.Replace('/', '\\');
        var ix = n.LastIndexOf('\\');
        if (ix != -1) n = n.Substring(ix + 1);
        if (n.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            n = n.Substring(0, n.Length - ".disabled".Length);
        ix = n.LastIndexOf('.');
        return ix > 0 ? n.Substring(0, ix) : n;
    }

    /// <summary>guid（任意大小写）→ 行内 ⟳ tip 文案（"待生效：…（重启后应用）"）。</summary>
    private static Dictionary<string, string> PendingTipsByRow()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in PendGroups())
            if (g.RowKey != null)
                map[g.RowKey] = "待生效：" + PendLabel(g) + "（重启后应用）";
        return map;
    }

    /// <summary>摘要签名（条数+标签串）——变化才推送。含中性档（"生效中"↔"加载顺序调整"切换
    /// 也是摘要变化，须触发推送）。</summary>
    private static string PendingSig()
    {
        var gs = PendGroups();
        return gs.Count.ToString(CultureInfo.InvariantCulture) + "\u0001"
               + string.Join("\u0001", gs.Select(PendLabel));
    }

    /// <summary>推 MC_PENDING_SUMMARY（__mcEvent）：{type,n,labels[],applied?}。n/labels 只计
    /// 待生效档（中性档不计入，任务 10 T9 改进项）；applied=中性档标签（有才带，面板弱化呈现）。
    /// 条数&gt;0 或 applied 在场面板显底部待生效条。</summary>
    private static void PushPendingSummary()
    {
        var gs = PendGroups();
        var real = gs.Where(g => !g.Neutral).ToList();
        var applied = gs.FirstOrDefault(g => g.Neutral);
        var sb = new StringBuilder(128);
        sb.Append("{\"type\":\"MC_PENDING_SUMMARY\",\"n\":")
          .Append(real.Count.ToString(CultureInfo.InvariantCulture)).Append(",\"labels\":[");
        for (var i = 0; i < real.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(PendLabel(real[i])));
        }
        sb.Append(']');
        if (applied != null) sb.Append(",\"applied\":").Append(JsonW.Str(applied.FixedLabel));
        sb.Append('}');
        PushEvent(sb.ToString());
    }

    // ============ 待生效条动作（v2 任务 13：撤销全部待生效 / 一键重启） ============

    /// <summary>
    /// MC_PENDING_UNDO（v2 任务 13）：撤销全部待生效操作——pending.json 整体移除（ops 清空；
    /// **order 一并清空**，裁定=撤销语义包含本会话刚保存的加载顺序，"回到默认序"与删除 order
    /// 字段等效，见 T9 裁定 1/5）。随后强制重读 + 同拍推新快照与摘要（行内 ⟳ / 底条一起消失）。
    /// 删除失败只 toast 报错、不清空内存态（不假装成功）；幂等（文件不在场=成功）。
    /// 暂存（staging）不做运行期清理：下次启动清理器的目录 sweep（Ruling 12）回收，避免运行期
    /// 与后台下载链抢文件。
    /// </summary>
    private static void HandlePendingUndo()
    {
        try
        {
            if (System.IO.File.Exists(McPending.File))
                System.IO.File.Delete(McPending.File);
            // 原子写中断的 .tmp 孤儿同清（不是意图介质，留着只会误导排障）
            var tmp = McPending.File + ".tmp";
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
        catch (Exception e)
        {
            Log("warn 撤销待生效失败: " + e.Message);
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("撤销失败")
                + ",\"small\":" + JsonW.Str("pending.json 无法删除：" + e.Message) + "}");
            return;
        }
        RefreshPending(true); // 内存态对齐磁盘（文件已不在=空清单/空 order）
        _pendSig = PendingSig();
        PushRender(BuildSnapshot().ToJson()); // 行内 ⟳ 同拍消失（BuildSnapshot 内部再强制重读）
        PushPendingSummary();                 // 底条隐藏（n=0 且无 applied）
        PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("已撤销全部待生效操作")
            + ",\"small\":" + JsonW.Str("加载顺序一并恢复默认（字母序）；暂存文件由下次启动清理器回收") + "}");
    }

    /// <summary>
    /// MC_RESTART（v2 任务 13）：一键重启——先把脏 cfg 落盘，再拉分离 PowerShell
    /// （RestartHelper：等本进程 PID 退出 → 9 秒 Steam 延迟补偿 → steam://rungameid/&lt;appid&gt;）。
    /// 拉起成功只代表"已请求重启"（分离进程启动成功 ≠ 重启成功），toast 措辞按此诚实表述；
    /// 随后由 Pump 在 <see cref="RestartQuitDelay"/> 秒后调 Application.Quit（让 toast 先上屏）。
    /// 拉起失败 → toast 报错且**不退出**（不假装成功）。重复命令（连点/双发）只保留第一条
    /// 等待链，后续回"重启已请求"提示（不再起第二条分离进程）。
    /// </summary>
    private static void HandleRestart()
    {
        if (_restartRequested)
        {
            // 连点/双发：等待链已拉起，不再起第二条（防退出后 Steam 被拉两次）
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("重启已请求")
                + ",\"small\":" + JsonW.Str("分离进程已在等待本进程退出；游戏随后自动关闭并由 Steam 重开") + "}");
            return;
        }
        try { FlushDirty(); }
        catch (Exception e) { Log("warn 一键重启：前置落盘异常: " + e.Message); }
        if (!RestartHelper.Request())
        {
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("无法请求重启")
                + ",\"small\":" + JsonW.Str("分离进程拉起失败（PowerShell 不可用？）——请手动退出游戏后重新启动") + "}");
            return;
        }
        _restartRequested = true;
        _restartQuitAge = 0f;
        PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("正在重启游戏…")
            + ",\"small\":" + JsonW.Str("已请求重启：本进程退出后经 Steam 重拉（约 10 秒）") + "}");
    }

    // ============ 套件仓库页（v2 任务 8：拉取/检查节流 + 下载确认链） ============

    /// <summary>guid → 待生效 install/replace/delete 意图集合（小写 guid；仓库行/侧栏徽标与动作闸共用）。
    /// v2 任务 10（T8 带出）：delete 类 op（卸载意图）同样进集合——待卸载的行 ⟳ 挂上、↑/↓ 徽标
    /// 被吃掉（裁定 3 同款语义）、仓库行动作被闸（已要删的 mod 不该再更新）。</summary>
    private static HashSet<string> RepoPendingGuids()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in _pendOps)
        {
            if (op.Type != McPending.TypeInstall && op.Type != McPending.TypeReplace
                && op.Type != McPending.TypeDelete) continue;
            var g = PendRowKeyOf(op.Target);
            if (g != null) set.Add(g);
        }
        return set;
    }

    /// <summary>侧栏仓库态徽标（↑/↓/↑+⊘；BuildSnapshot 消费）。无远端（未查/检查失败）或已有
    /// 待生效意图的行返回 null（后者只显 ⟳，裁定 3）。⊘ 与 ↓ 同为 orange+upd，G="kbw" 消歧。</summary>
    private static List<IcInfo> RepoIcsOf(string guid, HashSet<string> repoPend)
    {
        if (_repoRows == null) return null;
        RepoRow row = null;
        foreach (var r in _repoRows)
            if (string.Equals(r.Guid, guid, StringComparison.OrdinalIgnoreCase)) { row = r; break; }
        if (row == null || string.IsNullOrEmpty(row.Remote)) return null;
        if (repoPend.Contains(guid.ToLowerInvariant())) return null;
        switch (row.State)
        {
            case RepoState.Update:
                return new List<IcInfo> { new() { K = "blue", Tip = $"可更新到 {row.Remote} · 点击确认", Click = "upd" } };
            case RepoState.Downgrade:
                return new List<IcInfo>
                {
                    new() { K = "orange", Tip = $"远端 {row.Remote} 低于本地 {row.Local} · 点击确认（版本回退，风险自担）", Click = "upd" },
                };
            case RepoState.Warn:
                return new List<IcInfo>
                {
                    new() { K = "blue", Tip = $"可更新到 {row.Remote}（有版本告警）· 点击确认", Click = "upd" },
                    new() { K = "orange", Tip = row.Why + " · 是否继续由你决定 · 点击查看", Click = "upd", G = "kbw" },
                };
            default:
                return null; // Ok=同版本无动作面；Install=未装（侧栏本就无此行）
        }
    }

    /// <summary>MC_REPO_GET{force}（仓库视图打开拉取）/ MC_CHK（手动"检查更新"）共用入口。
    /// 节流（裁定 2）：fetch 在跑→只推现状；打开（force=false）10 分钟缓存内推缓存；手动 60 秒
    /// 冷却内推数据+toast"刚检查过"；否则起一轮后台 fetch（12 仓顺序，匿名限额预算内）。</summary>
    private static void HandleRepoGet(bool force)
    {
        if (_repoFetching) { PushRepoData(); return; }
        if (_repoRows != null)
        {
            var age = NowMs - _repoLastCheck;
            if (age < 0) age = 0;
            if (!force && age < RepoCacheMs) { PushRepoData(); return; }
            if (force && age < RepoCooldownMs)
            {
                PushRepoData();
                PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("刚检查过")
                    + ",\"small\":" + JsonW.Str("60 秒内不重复查询（GitHub 匿名限额 60 次/小时，一轮 12 仓）") + "}");
                return;
            }
        }
        StartRepoFetch();
    }

    /// <summary>起一轮后台 fetch：本地快照/游戏基线主线程取好（IL2CPP/文件通道契约），
    /// _repoFetching 置位即推 checking 快照（fetch 期间 _repoRows 保持旧值，后台改自己的副本，
    /// 完成后主线程整体替换——零跨线程行读写）。异常路径经 Kind 5 收尾复位检查锁（T8 遗留修①）。</summary>
    private static void StartRepoFetch()
    {
        var rows = RepoDecision.SnapshotLocal(); // 主线程（IL2CPPChainloader 契约）
        var local = GameVersion.ReadCached() ?? GameVersion.ReadLocal(); // 文件通道线程无关，结果缓存
        _repoFetching = true;
        PushRepoData();
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var infos = new Dictionary<string, RepoClient.UpdateInfo>(StringComparer.Ordinal);
                foreach (var r in rows) // 顺序温和（一轮 12 次 API 请求，不连环多轮）
                {
                    var info = await RepoClient.FetchLatest(r.RepoKey);
                    if (info == null) { r.Remote = null; r.Gamekb = null; } // 失败留痕已在 RepoClient warn
                    else { r.Remote = info.Version; r.Gamekb = info.Gamekb; infos[r.RepoKey] = info; }
                }
                RepoDecision.Build(rows, local); // 纯函数，后台可调
                RepoQueue.Enqueue(new RepoUi { Kind = 1, Rows = rows, Infos = infos });
            }
            catch (Exception e) // T8 遗留修①：异常也收尾——Kind 5 回主线程复位 _repoFetching（防永久"检查中"）
            {
                Log("warn repo fetch 异常: " + e.Message);
                RepoQueue.Enqueue(new RepoUi { Kind = 5, Err = e.GetType().Name + ": " + e.Message });
            }
        });
    }

    /// <summary>确认更新/安装（MC_UPD/MC_INST {repoKey}）与全部更新（MC_UPDALL，Update 态整批
    /// 循环单链——Warn 需逐仓红字确认、Install 非更新语义，均不入批）。下载串行闸 _repoChain。</summary>
    private static void HandleRepoUpd(string repoKey, bool all)
    {
        if (_repoChain)
        {
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("已有下载在进行")
                + ",\"small\":" + JsonW.Str("更新链串行执行（限流+带宽），完成后再试") + "}");
            return;
        }
        if (_repoFetching)
        {
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("正在检查更新")
                + ",\"small\":" + JsonW.Str("检查完成后即可更新") + "}");
            return;
        }
        if (_repoRows == null)
        {
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("尚未检查更新")
                + ",\"small\":" + JsonW.Str("先点\"检查更新\"获取仓库版本") + "}");
            return;
        }
        var pend = RepoPendingGuids();
        List<RepoRow> targets;
        if (all)
        {
            targets = new List<RepoRow>();
            foreach (var r in _repoRows)
                if (r.State == RepoState.Update && RepoActionable(r, pend, out _)) targets.Add(r);
            if (targets.Count == 0)
            {
                PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("没有可整批更新的项")
                    + ",\"small\":" + JsonW.Str("版本告警与降级项需逐仓确认") + "}");
                return;
            }
        }
        else
        {
            RepoRow row = null;
            foreach (var r in _repoRows)
                if (string.Equals(r.RepoKey, repoKey, StringComparison.OrdinalIgnoreCase)) { row = r; break; }
            if (row == null) { Log("repo upd 拒绝：未知仓键 " + repoKey); return; }
            if (!RepoActionable(row, pend, out var why))
            {
                PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str(row.Name + " 当前无可执行动作")
                    + ",\"small\":" + JsonW.Str(why) + "}");
                return;
            }
            targets = new List<RepoRow> { row };
        }
        _repoChain = true;
        var jobs = new List<(RepoRow row, RepoClient.UpdateInfo info)>();
        foreach (var t in targets) jobs.Add((t, _repoInfos[t.RepoKey]));
        Log("repo 下载链启动: " + string.Join(", ", jobs.ConvertAll(j => j.row.RepoKey + " " + j.row.Local + "→" + j.info.Version)));
        System.Threading.Tasks.Task.Run(() => RunRepoChainAsync(jobs));
    }

    /// <summary>行可动作判定：fetch 成功（Remote/信息在）且无待生效意图且态属 Update/Downgrade/Warn/Install。</summary>
    private static bool RepoActionable(RepoRow r, HashSet<string> pend, out string why)
    {
        why = null;
        if (string.IsNullOrEmpty(r.Remote)) { why = "上次检查该仓失败（Remote 不可用）"; return false; }
        if (!_repoInfos.ContainsKey(r.RepoKey)) { why = "仓库数据已过期，请重新检查更新"; return false; }
        if (pend.Contains(r.Guid.ToLowerInvariant())) { why = "已有待生效意图（重启后应用）"; return false; }
        if (r.State is not (RepoState.Update or RepoState.Downgrade or RepoState.Warn or RepoState.Install))
        { why = "已是最新"; return false; }
        return true;
    }

    /// <summary>下载链（后台，串行）：下载（进度 250ms/1% 节流入队）→ zip MD5 对 update.json（失败
    /// 清 staging 不写 pending，裁定 3）→ 解包 BepInEx/plugins/*.dll 暂存（zip 随即清理）→
    /// kind3 回主线程 PendingWriter.AddUpdate。单仓失败不阻断后续仓（各自报错）。</summary>
    private static async System.Threading.Tasks.Task RunRepoChainAsync(List<(RepoRow row, RepoClient.UpdateInfo info)> jobs)
    {
        try
        {
            foreach (var (row, info) in jobs)
            {
                string zipPath = null;
                string stagedDll = null; // T8 遗留修②：链中途失败连已解包暂存 dll 一并清理（不留半成品/孤儿暂存）
                try
                {
                    RepoQueue.Enqueue(new RepoUi { Kind = 2, RepoKey = row.RepoKey, Pct = 0, Stage = "down" });
                    long lastPush = 0;
                    var lastPct = -1;
                    zipPath = await RepoClient.DownloadAsset(info, (done, total) =>
                    {
                        if (total <= 0) return; // 服务器未报 Content-Length：不推百分比（面板显示"下载中…"）
                        var pct = (int)(done * 100 / total);
                        var now = Environment.TickCount64;
                        if (pct == lastPct || (pct < 100 && now - lastPush < 250)) return;
                        lastPush = now; lastPct = pct;
                        RepoQueue.Enqueue(new RepoUi { Kind = 2, RepoKey = row.RepoKey, Pct = pct, Stage = "down" });
                    });
                    RepoQueue.Enqueue(new RepoUi { Kind = 2, RepoKey = row.RepoKey, Pct = 100, Stage = "check" });
                    var md5 = RepoClient.Md5Of(zipPath);
                    if (!string.Equals(md5, info.Md5, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"MD5 校验失败（期望 {info.Md5} 实得 {md5}），暂存已清理");
                    RepoQueue.Enqueue(new RepoUi { Kind = 2, RepoKey = row.RepoKey, Pct = 100, Stage = "unzip" });
                    var dll = ExtractPluginDll(zipPath);
                    if (dll == null) throw new InvalidDataException("zip 内未找到 BepInEx/plugins/*.dll（打包布局异常）");
                    stagedDll = dll;
                    try { System.IO.File.Delete(zipPath); } catch { } // dll 已暂存，zip 即刻清理
                    zipPath = null;
                    RepoQueue.Enqueue(new RepoUi { Kind = 2, RepoKey = row.RepoKey, Pct = 100, Stage = "write" });
                    RepoQueue.Enqueue(new RepoUi { Kind = 3, RepoKey = row.RepoKey, Ok = true, Row = row, Info = info, Staged = dll });
                    stagedDll = null; // 已交主线程 AddUpdate 消费（写清单成功后暂存留存=清理器执行语义）
                }
                catch (Exception e)
                {
                    if (zipPath != null) { try { System.IO.File.Delete(zipPath); } catch { } } // 失败清 staging
                    if (stagedDll != null) { try { System.IO.File.Delete(stagedDll); } catch { } } // T8 遗留修②
                    RepoQueue.Enqueue(new RepoUi { Kind = 3, RepoKey = row.RepoKey, Ok = false, Err = e.Message });
                }
            }
        }
        finally { RepoQueue.Enqueue(new RepoUi { Kind = 4 }); }
    }

    /// <summary>解包 zip 内套件 dll（release.ps1 布局：BepInEx/plugins/SurvivalLog.&lt;mod&gt;.dll
    /// + BepInEx/config/ + README）→ 落 staging 同名暂存（覆盖旧残留）。未找到返回 null。</summary>
    private static string ExtractPluginDll(string zipPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            var n = (e.FullName ?? "").Replace('\\', '/');
            if (n.Length == 0 || !n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var lower = n.ToLowerInvariant();
            const string prefix = "bepinex/plugins/";
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (lower.IndexOf('/', prefix.Length) != -1) continue; // 只取 plugins 直下（防子目录夹带）
            var dest = Path.Combine(McPending.StagingDir, n.Substring(prefix.Length));
            // 流式解出（不用 ExtractToFile 扩展——它住在 System.IO.Compression.FileSystem，
            // 游戏运行时在場性未实证；ZipArchive/Open 为 System.IO.Compression 本体，net6 内置）。
            // T8 遗留修②（T9 落地）：解包中途异常把半写 dest 一并删除再抛（不留残缺 dll 冒充暂存）。
            using (var src = e.Open())
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                try { src.CopyTo(dst); }
                catch { try { System.IO.File.Delete(dest); } catch { } throw; }
            }
            return dest;
        }
        return null;
    }

    /// <summary>后台队列收割（Pump 主线程）：fetch 完成=换快照+推数据+侧栏徽标快照；
    /// 进度=推 MC_DL；单仓成功=主线程 AddUpdate+快照刷新（⟳ 出现/↑↓ 消失）；失败=MC_DL err+toast；
    /// 链收尾=释放串行闸+推数据。</summary>
    private static void RepoHarvest(RepoUi ui)
    {
        switch (ui.Kind)
        {
            case 1:
                _repoRows = ui.Rows;
                _repoInfos.Clear();
                if (ui.Infos != null)
                    foreach (var kv in ui.Infos) _repoInfos[kv.Key] = kv.Value;
                _repoLastCheck = NowMs;
                _repoFetching = false;
                PushRender(BuildSnapshot().ToJson()); // 侧栏 ↑/↓/⊘ 徽标（快照协议承载）
                PushRepoData();
                break;
            case 2:
                _dlKey = ui.RepoKey; _dlPct = ui.Pct; _dlStage = ui.Stage;
                PushEvent("{\"type\":\"MC_DL\",\"repoKey\":" + JsonW.Str(ui.RepoKey)
                    + ",\"pct\":" + ui.Pct.ToString(CultureInfo.InvariantCulture)
                    + ",\"stage\":" + JsonW.Str(ui.Stage) + "}");
                break;
            case 3:
                if (ui.Ok)
                {
                    var name = ui.Row.Name;
                    var to = ui.Info.Version;
                    try
                    {
                        PendingWriter.AddUpdate(ui.Row, ui.Info, ui.Staged); // 主线程唯一写者
                        PushEvent("{\"type\":\"MC_DL\",\"repoKey\":" + JsonW.Str(ui.RepoKey)
                            + ",\"pct\":100,\"stage\":\"ok\"}");
                        PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str(name + " 已加入待生效")
                            + ",\"small\":" + JsonW.Str("已下载并 MD5 校验 · " + (string.IsNullOrEmpty(ui.Row.Local) ? "安装 " : "更新 ") + name + " → " + to + " · 重启后由启动清理器原子替换") + "}");
                        RefreshPending(true);
                        _pendSig = PendingSig();
                        PushPendingSummary();
                        PushRender(BuildSnapshot().ToJson()); // 行内 ⟳ 出现、↑/↓ 消失（裁定 3）
                        PushRepoData();
                    }
                    catch (Exception e)
                    {
                        Log("warn AddUpdate 失败(" + ui.RepoKey + "): " + e.Message);
                        PushEvent("{\"type\":\"MC_DL\",\"repoKey\":" + JsonW.Str(ui.RepoKey)
                            + ",\"pct\":100,\"stage\":\"err\",\"err\":" + JsonW.Str("写入待生效清单失败：" + e.Message) + "}");
                    }
                }
                else
                {
                    PushEvent("{\"type\":\"MC_DL\",\"repoKey\":" + JsonW.Str(ui.RepoKey)
                        + ",\"pct\":100,\"stage\":\"err\",\"err\":" + JsonW.Str(ui.Err ?? "未知错误") + "}");
                    PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("更新失败（未写入待生效）")
                        + ",\"small\":" + JsonW.Str((ui.Err ?? "未知错误") + " · 暂存已清理") + "}");
                }
                break;
            case 4:
                _repoChain = false;
                _dlKey = null; _dlPct = 0; _dlStage = null;
                PushRepoData();
                break;
            case 5: // T8 遗留修①（T9 落地）：fetch 后台异常收尾——复位检查锁（旧 _repoRows 保留自愈），推数据+toast
                _repoFetching = false;
                PushRepoData();
                PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("检查更新失败")
                    + ",\"small\":" + JsonW.Str((ui.Err ?? "未知异常") + " · 已保留上次数据，可稍后重试") + "}");
                break;
        }
    }

    /// <summary>推 MC_REPO_DATA（__mcEvent 通道）：rows（含 pending 意图标记）+ lastCheck + checking
    /// + 当前下载链状态（面板重开恢复行内/弹窗进度态）。</summary>
    private static void PushRepoData()
    {
        var d = new RepoData
        {
            LastCheck = _repoLastCheck,
            Checking = _repoFetching,
            Mirror = string.IsNullOrEmpty(ModCenterPlugin.NetworkMirrorPrefix?.Value) ? "直连" : "镜像",
        };
        if (_repoRows != null)
        {
            RefreshPending(false); // mtime 未变零开销；变了才有新意图
            var pend = RepoPendingGuids();
            foreach (var r in _repoRows)
                d.Rows.Add(new RepoRowInfo
                {
                    Name = r.Name, Status = r.Status, Local = r.Local, Remote = r.Remote,
                    Why = r.Why, RepoKey = r.RepoKey, Guid = r.Guid, Gamekb = r.Gamekb,
                    VerNote = r.VerNote, Pending = pend.Contains(r.Guid.ToLowerInvariant()),
                });
        }
        if (_dlKey != null) { d.DlKey = _dlKey; d.DlPct = _dlPct; d.DlStage = _dlStage; }
        PushEvent(d.ToJson());
    }

    // ============ 加载排序模式（v2 任务 9：MC_ORDER_GET / MC_ORDER_SAVE） ============

    /// <summary>可排序插件全集（GUID）：当前 Chainloader 插件去掉探针族（TpProbeNames——面板
    /// 不可见的开发探针，原型 MODS 同样不含），按 GUID 字母序（=默认加载序，原型注释定稿）。
    /// 套件 9 + ModCenter 自身 + 第三方全在内（含 Meta 未登记的第一方 mod——排序面对其可见）。</summary>
    private static List<string> OrderableGuids()
    {
        var list = new List<string>();
        foreach (var info in LoadedPlugins())
        {
            var md = info?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            if (IsProbeName(md.Name)) continue;
            list.Add(md.GUID);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static List<BepInEx.PluginInfo> LoadedPlugins()
    {
        var list = new List<BepInEx.PluginInfo>();
        try
        {
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            if (plugins != null) list.AddRange(plugins.Values);
        }
        catch { }
        return list;
    }

    private static bool IsProbeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var p in TpProbeNames)
            if (name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>order 是否等于默认序（条目集与序都同，大小写不敏感）——摘要档抑制判定用。</summary>
    private static bool OrderEqualsDefault(List<string> order)
    {
        var def = OrderableGuids();
        if (order.Count != def.Count) return false;
        for (var i = 0; i < order.Count; i++)
            if (!string.Equals(order[i], def[i], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>MC_ORDER_GET：推 MC_ORDER_DATA。guids=生效序（pending.order 命中项按序前置 +
    /// 其余按 GUID 字母序——与清理器 OrderReorder "命中前置、其余原相对序" 语义一致，链上基线
    /// 即字母序）；rows=同序显示元数据 {guid,name,ver}（name=Meta 中文名优先，ModCenter 自身给
    /// 面板名，其余插件自报名；ver=插件版本——快照 ModInfo 不带版本，行内版本列数据源）；
    /// def=默认序（面板"恢复默认"本地重排用，裁定 1：不另做协议）。</summary>
    private static void HandleOrderGet()
    {
        var def = OrderableGuids();
        RefreshPending(false);
        var effective = new List<string>(def.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defSet = new HashSet<string>(def, StringComparer.OrdinalIgnoreCase);
        foreach (var g in _pendOrder) // 幽灵 GUID/重复项丢弃（清理器同语义）
            if (!string.IsNullOrEmpty(g) && seen.Add(g) && defSet.Contains(g)) effective.Add(g);
        foreach (var g in def)
            if (!seen.Contains(g)) effective.Add(g);

        var nameOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mm in Meta.Mods) nameOf[mm.Guid] = mm.Name;
        var plugins = LoadedPlugins();

        var sb = new StringBuilder(2048);
        sb.Append("{\"type\":\"MC_ORDER_DATA\",\"guids\":[");
        for (var i = 0; i < effective.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(effective[i]));
        }
        sb.Append("],\"rows\":[");
        for (var i = 0; i < effective.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var g = effective[i];
            var name = ResolveOrderRowName(g, nameOf, plugins);
            var ver = string.Empty;
            foreach (var p in plugins)
                if (string.Equals(p?.Metadata?.GUID, g, StringComparison.OrdinalIgnoreCase))
                { try { ver = p.Metadata.Version?.ToString() ?? string.Empty; } catch { } break; }
            sb.Append("{\"guid\":").Append(JsonW.Str(g))
              .Append(",\"name\":").Append(JsonW.Str(name))
              .Append(",\"ver\":").Append(JsonW.Str(ver)).Append('}');
        }
        sb.Append("],\"def\":[");
        for (var i = 0; i < def.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(def[i]));
        }
        sb.Append("]}");
        PushEvent(sb.ToString());
        Log("order data 推送: " + effective.Count + " 项（默认 " + def.Count + "，覆盖 "
            + _pendOrder.Count + " 条）");
    }

    private static string ResolveOrderRowName(string guid, Dictionary<string, string> metaNames,
        List<BepInEx.PluginInfo> plugins)
    {
        if (metaNames.TryGetValue(guid, out var n) && !string.IsNullOrEmpty(n)) return n; // Meta 中文名
        if (string.Equals(guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase)) return "Mod 管理中心";
        foreach (var p in plugins)
            if (string.Equals(p?.Metadata?.GUID, guid, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(p.Metadata?.Name)) return p.Metadata.Name;
        var ix = guid.LastIndexOf('.');
        return ix == -1 ? guid : guid.Substring(ix + 1); // GUID 尾段兜底
    }

    /// <summary>MC_ORDER_SAVE {guids[]}：校验（白名单=当前 Plugins 键；元素不重不漏=与可排序集
    /// 精确相等——探针族不入排序面故不在期望集）→ PendingWriter.SetOrder 整替（ops 原样保留）→
    /// 摘要即时推（order==默认序时摘要档自动缺席）→ MC_ORDER_OK；任何不符整单拒绝 MC_ORDER_FAIL。
    /// v2 任务 10（T9 带出②）：体内兜底 try/catch——任何异常也回 MC_ORDER_FAIL（否则面板
    /// ordSaving 在途永不复位，保存钮卡死）。</summary>
    private static void HandleOrderSave(JsonElement el)
    {
        try { HandleOrderSaveCore(el); }
        catch (Exception e)
        {
            Log("warn order save 异常: " + e.GetType().Name + ": " + e.Message);
            PushEvent("{\"type\":\"MC_ORDER_FAIL\",\"err\":" + JsonW.Str("内部异常：" + e.Message) + "}");
        }
    }

    private static void HandleOrderSaveCore(JsonElement el)
    {
        var guids = new List<string>();
        if (el.TryGetProperty("guids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var g in arr.EnumerateArray())
                if (g.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(g.GetString()))
                    guids.Add(g.GetString());

        var def = OrderableGuids();
        var defSet = new HashSet<string>(def, StringComparer.OrdinalIgnoreCase);
        string why = null;
        if (guids.Count == 0) why = "空顺序表";
        else if (guids.Count != def.Count)
            why = "条数不符（收 " + guids.Count.ToString(CultureInfo.InvariantCulture)
                  + " 项，期望 " + def.Count.ToString(CultureInfo.InvariantCulture) + "）";
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in guids)
            {
                if (!seen.Add(g)) { why = "重复项 " + g; break; }
                if (!defSet.Contains(g)) { why = "白名单外/未知插件 " + g; break; }
            }
        }
        if (why != null)
        {
            Log("order save 拒绝：" + why);
            PushEvent("{\"type\":\"MC_ORDER_FAIL\",\"err\":" + JsonW.Str(why) + "}");
            return;
        }
        PendingWriter.SetOrder(guids); // 主线程唯一写者；ops 原样保留（T6 契约）
        Log("order save：顺序覆盖整替 " + guids.Count.ToString(CultureInfo.InvariantCulture) + " 项"
            + (OrderEqualsDefault(guids) ? "（=默认序，等效无覆盖）" : ""));
        RefreshPending(true);
        _pendSig = PendingSig();
        PushPendingSummary();
        PushEvent("{\"type\":\"MC_ORDER_OK\"}");
    }

    // ============ 彻底卸载（v2 任务 10：MC_UNINST / MC_UNINST_DO） ============

    /// <summary>卸载明细（MC_UNINST_DATA / MC_UNINST_DO 共用构造）。</summary>
    private sealed class UninstInfo
    {
        public string Name;         // 显示名（Meta 中文名 / 插件自报名 / dll 名去扩展）
        public string DllBaseRel;   // plugins 目录相对名（DllDisabler 口径；null=解析失败）
        public bool DisabledTwin;   // plugins 下同名 .disabled 在场（residue/缺席行——一并列入删除）
        public string CfgRel;       // cfg 的 BepInEx 根相对路径（'/' 分隔）；null=无/不在场
        public bool Self;           // ModCenter 自身（面板 __self 行——弹窗特别文案分支）
        public bool Loaded;         // 在链上（marker 清除走 DllDisabler.Enable）
        public bool Tp;             // 第三方（有 [DisabledTp] marker 记录；Meta 套件无 marker 不建孤儿键）
        public bool Absent;         // 缺席行（dll 已是 .disabled 未加载——明细只列 .disabled，不列不存在的裸 dll）
        public string Note;         // 补充说明（缺席行 cfg 无法定位等）；null=无
    }

    /// <summary>guid → 卸载明细。四路由：①ModCenter 自身（特别文案分支）②Meta 套件③链上第三方
    /// （FindTp——探针/MCCleaner 豁免不可达，防呆）④缺席行（guid=dll 相对名，DllDisabler.
    /// AbsentDisabled——dll 已禁用未加载）。cfg 真值源 = PluginInfo.Instance as BasePlugin →
    /// Config.ConfigFilePath（v1 §4.1③ 实证写法；T4 实证 cfg 按 GUID 命名，不自拼），不在场不列。</summary>
    private static UninstInfo BuildUninst(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        // ① ModCenter 自身
        if (string.Equals(guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase))
        {
            var self = new UninstInfo { Name = "Mod 管理中心", Self = true, Loaded = true };
            var info = PluginOfGuid(ModCenterPlugin.Guid);
            self.DllBaseRel = info != null ? DllDisabler.DllRelOf(info) : "SurvivalLog.ModCenter.dll";
            try
            {
                var p = ModCenterPlugin.Instance?.Config?.ConfigFilePath;
                if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) self.CfgRel = ToRootRel(p);
            }
            catch { }
            return self;
        }

        // ②③ Meta 套件 / 链上第三方（FindTp 已豁免探针与第一方；MCCleaner 是 patcher 不在 Plugins，不可达）
        BepInEx.PluginInfo pi = null;
        var mm = Resolve(guid);
        if (mm != null) pi = PluginOfGuid(mm.Guid);
        if (pi == null) pi = FindTp(guid);
        if (pi != null)
        {
            var u = new UninstInfo
            {
                Name = mm != null ? mm.Name : (pi.Metadata?.Name ?? guid),
                DllBaseRel = DllDisabler.DllRelOf(pi),
                Loaded = true,
                Tp = mm == null, // 第三方才有 [DisabledTp] marker；Meta 套件不建孤儿键
            };
            if (u.DllBaseRel != null)
                u.DisabledTwin = System.IO.File.Exists(Path.Combine(Paths.PluginPath, u.DllBaseRel + ".disabled"));
            try
            {
                if (pi.Instance is BasePlugin bp && bp.Config != null
                    && !string.IsNullOrEmpty(bp.Config.ConfigFilePath)
                    && System.IO.File.Exists(bp.Config.ConfigFilePath))
                    u.CfgRel = ToRootRel(bp.Config.ConfigFilePath);
            }
            catch { }
            return u;
        }

        // ④ 缺席行（dll 已禁用未加载；guid = dll 相对名）
        foreach (var rel in DllDisabler.AbsentDisabled())
            if (string.Equals(rel, guid, StringComparison.OrdinalIgnoreCase))
                return new UninstInfo
                {
                    Name = rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? rel.Substring(0, rel.Length - 4) : rel,
                    DllBaseRel = rel,
                    DisabledTwin = true, // 缺席行本义：dll 现名即 .disabled
                    CfgRel = null,
                    Loaded = false,
                    Tp = true,
                    Absent = true,
                    Note = "该 mod 已停用（dll 未加载），其 cfg（如有）无法自动定位——确认后请自行检查 "
                           + @"BepInEx\config 下对应 <GUID>.cfg。",
                };
        return null;
    }

    /// <summary>MC_UNINST {guid}：构造明细回 MC_UNINST_DATA {guid,name,self,files[],note?}
    /// （files=BepInEx 根相对'/'路径，面板渲染 BepInEx\ 前缀）。不在链上/解析失败→MC_TOAST 拒绝。</summary>
    private static void HandleUninst(string guid)
    {
        var u = BuildUninst(guid);
        if (u == null || string.IsNullOrEmpty(u.DllBaseRel))
        {
            Log("uninst 拒绝：未知/不在链上 " + (guid ?? "?"));
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("无法构造卸载明细")
                + ",\"small\":" + JsonW.Str("插件不在链上（guid=" + (guid ?? "?") + "），详见 LogOutput") + "}");
            return;
        }
        // 明细只列实际在场的目标：缺席行的裸 dll 已不存在（现名 .disabled），列它等于谎报——
        // 只列 .disabled；常规行列裸 dll（+ 同在场 .disabled 冗余件，如有）。
        var files = new List<string>();
        if (!u.Absent) files.Add(UninstDllOp(u));
        if (u.DisabledTwin) files.Add(UninstDllOp(u) + ".disabled");
        if (u.CfgRel != null) files.Add(u.CfgRel);
        var sb = new StringBuilder(256);
        sb.Append("{\"type\":\"MC_UNINST_DATA\",\"guid\":").Append(JsonW.Str(guid))
          .Append(",\"name\":").Append(JsonW.Str(u.Name))
          .Append(",\"self\":").Append(u.Self ? "true" : "false")
          .Append(",\"files\":[");
        for (var i = 0; i < files.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(files[i]));
        }
        sb.Append(']');
        if (u.Note != null) sb.Append(",\"note\":").Append(JsonW.Str(u.Note));
        sb.Append('}');
        PushEvent(sb.ToString());
        Log("uninst 明细: " + u.Name + " → " + string.Join(", ", files));
    }

    /// <summary>MC_UNINST_DO {guid}：确认卸载——PendingWriter.AddDelete(dll+cfg)（缺席行只写
    /// .disabled 一条——裸 dll 不存在）→ marker 清除（第三方在链上走 DllDisabler.Enable：dll 即将被删，反向改名
    /// 副作用无害、marker 留 false 即可；缺席行走 Forget：只清 marker 不改名，避免 .disabled 复活；
    /// Meta 套件无 marker 不建孤儿键；Self 跳过）→ order 记录清掉该 guid（弹窗"排序记录"承诺）→
    /// FlushDirty（意图即时落盘，强杀免疫）→ 快照/摘要/仓库数据刷新（行内 ⟳、仓库 pending 标记）。
    /// 防重：已有同目标 delete 意图→幂等跳过（toast 提示，不二次写）。</summary>
    private static void HandleUninstDo(string guid)
    {
        var u = BuildUninst(guid);
        if (u == null || string.IsNullOrEmpty(u.DllBaseRel))
        {
            Log("uninst-do 拒绝：未知/不在链上 " + (guid ?? "?"));
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str("卸载失败")
                + ",\"small\":" + JsonW.Str("插件不在链上（guid=" + (guid ?? "?") + "），详见 LogOutput") + "}");
            return;
        }

        RefreshPending(true);
        var dllOp = UninstDllOp(u);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormOpRel(dllOp) };
        if (u.DisabledTwin) targets.Add(NormOpRel(dllOp + ".disabled"));
        if (u.CfgRel != null) targets.Add(NormOpRel(u.CfgRel));
        foreach (var op in _pendOps)
            if (op.Type == McPending.TypeDelete && targets.Contains(NormOpRel(op.Target)))
            {
                PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str(u.Name + " 已有待生效卸载意图")
                    + ",\"small\":" + JsonW.Str("重启游戏后由启动清理器彻底删除（底部待生效条可查看）") + "}");
                return;
            }

        try
        {
            // 缺席行只有 .disabled 在场（裸 dll 不存在）——只写这一条，避免空转 op 在底部条
            // 里多报一项、与弹窗"将删除"清单不一致。
            if (u.Absent)
            {
                PendingWriter.AddDelete(dllOp + ".disabled", null);
            }
            else
            {
                PendingWriter.AddDelete(dllOp, u.CfgRel); // 主线程唯一写者（原子写）
                if (u.DisabledTwin) PendingWriter.AddDelete(dllOp + ".disabled", null);
            }
            if (u.Tp && u.Loaded) DllDisabler.Enable(u.DllBaseRel); // 在链上第三方：marker 清除（裁定路径）
            else if (u.Tp) DllDisabler.Forget(u.DllBaseRel);       // 缺席行：只清 marker，不反向改名
            // 排序记录清理：pending.order 里该 guid 的条目一并移除（否则留幽灵引用；弹窗
            // "ModCenter 停用 / 排序记录（如有）"承诺的"排序记录"即此。清理器虽会丢幽灵，
            // 但记录本体不该残留）。
            if (_pendOrder.Count > 0
                && _pendOrder.Any(o => string.Equals(o, guid, StringComparison.OrdinalIgnoreCase)))
            {
                var kept = _pendOrder
                    .Where(o => !string.Equals(o, guid, StringComparison.OrdinalIgnoreCase)).ToList();
                PendingWriter.SetOrder(kept); // ops 原样保留，order 整替
                Log("uninst 排序记录移除: " + guid + "（order " + _pendOrder.Count + " → " + kept.Count + "）");
            }
            FlushDirty(); // marker 等 cfg 写入立即落盘（不留 3s 防抖窗口——确认即持久）
            Log("uninst " + u.Name + " → delete "
                + (u.Absent
                    ? dllOp + ".disabled"
                    : dllOp + (u.CfgRel != null ? " + " + u.CfgRel : "")
                      + (u.DisabledTwin ? " + " + dllOp + ".disabled" : "")));
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str(u.Name + " 已加入待生效")
                + ",\"small\":" + JsonW.Str("重启游戏后由启动清理器彻底删除（dll + cfg + 停用记录）——强杀亦不丢失") + "}");
        }
        catch (Exception e)
        {
            Log("warn uninst-do 写入失败(" + u.Name + "): " + e.Message);
            PushEvent("{\"type\":\"MC_TOAST\",\"main\":" + JsonW.Str(u.Name + " 卸载写入失败")
                + ",\"small\":" + JsonW.Str(e.Message + "（详见 LogOutput）——未写入待生效清单") + "}");
        }
        RefreshPending(true);
        _pendSig = PendingSig();
        PushPendingSummary();
        PushRender(BuildSnapshot().ToJson()); // 行内 ⟳（cfg/dll 意图归属行）
        PushRepoData();                       // 仓库行 pending 标记（RepoPendingGuids 已含 delete）
    }

    /// <summary>dll op 目标（BepInEx 根相对 'plugins/…'，与清理器 ResolveRel/待生效清单同口径）。</summary>
    private static string UninstDllOp(UninstInfo u) => "plugins/" + u.DllBaseRel.Replace('\\', '/');

    /// <summary>op 路径归一（清理器 NormRel 同款：'/'→'\'，OrdinalIgnoreCase 比对用）。</summary>
    private static string NormOpRel(string rel) => (rel ?? "").Trim().Replace('/', '\\');

    /// <summary>guid → 链上插件（LoadedPlugins 线性找，OrdinalIgnoreCase）。</summary>
    private static BepInEx.PluginInfo PluginOfGuid(string guid)
    {
        foreach (var p in LoadedPlugins())
            if (string.Equals(p?.Metadata?.GUID, guid, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    /// <summary>绝对路径 → BepInEx 根相对（'/' 分隔；越界/解析失败返回 null）。</summary>
    private static string ToRootRel(string abs)
    {
        try
        {
            var rel = Path.GetRelativePath(Paths.BepInExRootPath, abs).Replace('\\', '/');
            if (rel.Length == 0 || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return null;
            return rel;
        }
        catch { return null; }
    }

    // ============ 冲突体检（v2 任务 11：L1 已知冲突库 / L3 功能域标注） ============

    /// <summary>已装插件的一轮体检投影：GUID/显示名/dll 文件名/L1 命中（GUID 优先→dll 兜底）/功能域。
    /// 数据源=IL2CPPChainloader.Instance.Plugins（与 BuildSnapshot 同源）+ Meta 两张静态表。
    /// 快照 ics 与 MC_CF 卡片共用本投影，保证行内图标与弹窗内容永远同源。</summary>
    private sealed class CfRow
    {
        public string Guid;
        public string Name;
        public ConflictEntry Entry; // L1 命中（null=未收录）
        public string Domain;       // L3 功能域（null=未登记）
    }

    private static List<CfRow> InstalledCfRows()
    {
        var list = new List<CfRow>();
        foreach (var p in LoadedPlugins())
        {
            var md = p?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            var dllRel = DllDisabler.DllRelOf(p);
            var dllName = string.IsNullOrEmpty(dllRel) ? null : Path.GetFileName(dllRel);
            var mm = MetaOfGuid(md.GUID);
            list.Add(new CfRow
            {
                Guid = md.GUID,
                // T11 M5：CabinetBurden/ModCenter（Meta 九表外）走附加中文名表，L3 who 不再露英文自报名
                Name = mm != null ? mm.Name
                    : (Meta.ExtraNameOf(md.GUID) ?? (string.IsNullOrEmpty(md.Name) ? md.GUID : md.Name)),
                Entry = Meta.MatchL1(md.GUID, dllName),
                Domain = Meta.DomainOf(md.GUID),
            });
        }
        return list;
    }

    /// <summary>Meta 套件精确 GUID 查找（不用 Resolve 的 En/尾段宽松匹配——第三方 GUID 尾段
    /// 撞名（如某 mod 尾段恰为 BackpackExpand）时不得被误当套件行做冲突扩散）。</summary>
    private static MetaMod MetaOfGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var mm in Meta.Mods)
            if (string.Equals(mm.Guid, guid, StringComparison.OrdinalIgnoreCase)) return mm;
        return null;
    }

    /// <summary>该行应展示的 L1 条目：①本行自身命中（GUID→dll 兜底，见 Meta.MatchL1）；
    /// ②套件行被已装冲突源波及（条目的 Who 含本行 En，且该冲突源确在链上）。按色级
    /// red→orange→gray 稳定排序（OrderBy 稳定，同色级保持表序）。</summary>
    private static List<ConflictEntry> EntriesFor(CfRow row, List<CfRow> all)
    {
        var list = new List<ConflictEntry>();
        if (row.Entry != null) list.Add(row.Entry);
        var mm = MetaOfGuid(row.Guid);
        if (mm != null)
        {
            foreach (var e in Meta.KnownConflicts)
            {
                if (ReferenceEquals(e, row.Entry)) continue;
                var affected = false;
                foreach (var w in e.Who)
                    if (string.Equals(w, mm.En, StringComparison.OrdinalIgnoreCase)) { affected = true; break; }
                if (!affected) continue;
                var sourceInstalled = all.Any(x => ReferenceEquals(x.Entry, e));
                if (sourceInstalled) list.Add(e); // 冲突源未装 = 无冲突（不跨行误报）
            }
        }
        return list.OrderBy(e => e.Level == "red" ? 0 : (e.Level == "orange" ? 1 : 2)).ToList();
    }

    /// <summary>同域已装集合（按显示名稳定排序）；未登记域=空表。</summary>
    private static List<CfRow> DomainMates(CfRow row, List<CfRow> all)
    {
        var mates = new List<CfRow>();
        if (row.Domain == null) return mates;
        foreach (var x in all)
            if (x.Domain != null && string.Equals(x.Domain, row.Domain, StringComparison.Ordinal))
                mates.Add(x);
        return mates.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>行内冲突 ics（BuildSnapshot/BuildTp 共用）：L1 每条一图标（red/gray，click=cf），
    /// 随后 L2 碰撞橙标（v2 任务 12；每 mod 聚合一枚，click=cf），末尾 L3 同域灰（域内已装 ≥2 且
    /// 均为域表登记项——第三方未登记不计入，防盲目误报）。
    /// T11 M3：L1 已有卡片（红/灰任一）的 mod 跳过 L3 灰标——避免"红+灰/双灰"噪音（expandedstorage
    /// 只出 L1 灰卡）。未加载行（套件 dll 缺失/缺席行）不产冲突标。无命中=空表。</summary>
    private static List<IcInfo> CfIcsFor(List<CfRow> all, string guid)
    {
        var list = new List<IcInfo>();
        if (string.IsNullOrEmpty(guid)) return list;
        CfRow row = null;
        foreach (var r in all)
            if (string.Equals(r.Guid, guid, StringComparison.OrdinalIgnoreCase)) { row = r; break; }
        if (row == null) return list;
        var entries = EntriesFor(row, all);
        foreach (var e in entries) list.Add(EntryIc(e));
        // v2 任务 12：L2 运行时碰撞（只统计可归属已装 mod 的组；未知来源只进扫描汇总，裁定 3）
        var l2 = PatchScan.IcsFor(guid);
        if (l2 != null) list.Add(l2);
        if (row.Domain != null && entries.Count == 0) // T11 M3：L1 已有卡片 → 不再叠 L3 灰标
        {
            var mates = DomainMates(row, all);
            if (mates.Count >= 2) list.Add(DomainIc(row, mates));
        }
        return list;
    }

    /// <summary>T11 M7：停用套件行的冲突/碰撞/同域徽标保留但弱化（面板降透明度）+ tip 前缀
    /// "（已停用）"——保留信息量又不误导。裁定 C（Ruling 15）：停用语义=入口隐藏/General.Enabled
    /// 关闭（套件行为停写），**已挂 Harmony 补丁不从账本移除**；因此徽标保留、只标停用态。</summary>
    private static void WeakenCfIcs(List<IcInfo> ics)
    {
        foreach (var ic in ics)
            if (ic.Click == "cf")
            {
                ic.W = true;
                ic.Tip = "（已停用）" + ic.Tip;
            }
    }

    /// <summary>行内 L1 图标：tip=原因摘要（Title）+ 对象 + 建议数（控制器裁定 2；完整原因在弹窗）。</summary>
    private static IcInfo EntryIc(ConflictEntry e)
    {
        var who = new List<string>();
        foreach (var w in e.Who) who.Add(Meta.DisplayNameOf(w));
        var kind = e.Level == "red" ? "冲突" : (e.Level == "orange" ? "碰撞" : "同域");
        return new IcInfo
        {
            K = e.Level,
            Click = "cf",
            Tip = kind + " · " + e.Title + " · 对象 " + string.Join("、", who) + " · 处置建议 " + e.Tips.Length + " 条",
        };
    }

    /// <summary>行内 L3 同域图标：tip 列出同域其他已装 mod（不含本行）。</summary>
    private static IcInfo DomainIc(CfRow row, List<CfRow> mates)
    {
        var others = new List<string>();
        foreach (var x in mates)
            if (!string.Equals(x.Guid, row.Guid, StringComparison.OrdinalIgnoreCase)) others.Add(x.Name);
        return new IcInfo
        {
            K = "gray",
            Click = "cf",
            Tip = "同域共存（" + row.Domain + "功能域）· 与 " + string.Join("、", others) + " · 仅标注不判定 · 点击查看",
        };
    }

    /// <summary>MC_CF {guid} → MC_CF_DATA {guid,cards[]}：卡片顺序与行内 ics 同源（L1 色级序 →
    /// L2 碰撞卡（v2 任务 12）→ L3 同域卡；T11 M3：L1 已有卡片则不出 L3 卡）。未在链上/缺席行 →
    /// 空 cards（面板给空态壳，不误报）。</summary>
    private static void HandleCf(string guid)
    {
        var rows = InstalledCfRows();
        CfRow row = null;
        if (!string.IsNullOrEmpty(guid))
            foreach (var r in rows)
                if (string.Equals(r.Guid, guid, StringComparison.OrdinalIgnoreCase)) { row = r; break; }
        var data = new CfData { Guid = guid };
        if (row != null)
        {
            var entries = EntriesFor(row, rows);
            foreach (var e in entries) data.Cards.Add(EntryCard(e));
            foreach (var c in PatchScan.CollisionsFor(row.Guid)) data.Cards.Add(PatchCard(c)); // T12 L2
            if (row.Domain != null && entries.Count == 0) // T11 M3：L1 已有卡片 → 不再叠 L3 灰卡
            {
                var mates = DomainMates(row, rows);
                if (mates.Count >= 2) data.Cards.Add(DomainCard(row, mates));
            }
        }
        PushEvent(data.ToJson());
        Log("cf " + guid + " → " + data.Cards.Count + " 卡片" + (row == null ? "（不在链上/缺席行）" : ""));
    }

    private static CfCardInfo EntryCard(ConflictEntry e)
    {
        var c = new CfCardInfo
        {
            Lv = e.Level,
            Tag = Meta.TagOf(e.Level),
            Title = e.Title,
            Reason = e.Reason,
        };
        foreach (var w in e.Who) c.Who.Add(Meta.DisplayNameOf(w));
        c.Tips.AddRange(e.Tips);
        return c;
    }

    /// <summary>L2 碰撞卡（v2 任务 12）：lv=orange / tag=碰撞；title=方法名；reason 按控制器
    /// 裁定 3 文案（"对同一游戏方法 X 挂了补丁，结果取决于加载顺序（后写覆盖前写）"）；tips=
    /// 停用一方 / 调序（仅缓解写序类冲突，风险自负）。who=owner 显示串（未知来源带标注）。</summary>
    private static CfCardInfo PatchCard(PatchCollision c)
    {
        var card = new CfCardInfo
        {
            Lv = "orange",
            Tag = Meta.TagOf("orange"),
            Title = "运行时补丁碰撞 · " + c.Method,
            Reason = "对同一游戏方法 " + c.Method + " 挂了 " + c.Owners.Count + " 个补丁（共 " + c.PatchCount
                   + " 个补丁实例），结果取决于加载顺序（后写覆盖前写）。",
        };
        card.Who.AddRange(c.Who());
        card.Tips.Add("停用其中一方");
        card.Tips.Add("尝试调整加载顺序（仅缓解写序类冲突，风险自负）");
        return card;
    }

    /// <summary>L3 同域卡（原型 cfHtml 的灰卡同构；who=域内全部已装登记 mod）。文案为通用标注，
    /// 不含判定结论——spec §4.4"只标注不判定"。tips 照原型同域卡两句。</summary>
    private static CfCardInfo DomainCard(CfRow row, List<CfRow> mates)
    {
        var c = new CfCardInfo
        {
            Lv = "gray",
            Tag = Meta.TagOf("gray"),
            Title = "同域共存（功能域标注）",
            Reason = "同属「" + row.Domain + "」功能域：多个 mod 可能改写同一类游戏数据，"
                   + "机制可正交互补、也可能数值叠加超出预期。功能域标注只用于提示同域共存，不做冲突判定，自行取舍。",
        };
        foreach (var x in mates) c.Who.Add(x.Name);
        c.Tips.Add("保留一方");
        c.Tips.Add("维持现状（接受叠加结果）");
        return c;
    }

    /// <summary>MC_SCAN（v2 任务 12）：手动刷新 L2 补丁碰撞——重扫（主线程）→ 推新快照
    /// （行内橙标随之重算）+ MC_SCAN_DATA（面板汇总行与汇总弹窗）。</summary>
    private static void HandlePatchScan()
    {
        PatchScan.Refresh();
        PushRender(BuildSnapshot().ToJson());
        PushScanData();
    }

    /// <summary>推 MC_SCAN_DATA（最近一轮结果；扫描异常也推——面板显示错误态而不装死）。</summary>
    private static void PushScanData()
    {
        try { PushEvent(PatchScan.BuildData().ToJson()); }
        catch (Exception e) { Log("warn MC_SCAN_DATA 推送失败: " + e.Message); }
    }

    // ============ 消息泵（主线程，Ticker 每帧调） ============

    internal static void EnqueueOutboxJson(string json) => Inbox.Enqueue(json);

    internal static void Pump(float dt)
    {
        try
        {
            while (Inbox.TryDequeue(out var json)) HandleOutbox(json);
            if (Dirty.Count > 0)
            {
                _dirtyAge += dt;
                if (_dirtyAge >= FlushInterval) FlushDirty();
            }
            // v2 任务 7：pending.json 2s 节拍巡检（mtime 变才重读；摘要变化才推送；
            // 首拍只同步签名不推——面板未开，MC_OPEN 分支必补推一轮初始态）
            _pendAcc += dt;
            if (_pendAcc >= 2f)
            {
                _pendAcc = 0f;
                RefreshPending(false);
                var sig = PendingSig();
                if (_pendSig == null) _pendSig = sig;
                else if (sig != _pendSig)
                {
                    _pendSig = sig;
                    PushPendingSummary();
                }
            }
            // v2 任务 8：仓库后台队列收割（fetch 完成/下载进度/单仓结果/链收尾 → 推送或落 pending）
            while (RepoQueue.TryDequeue(out var ui)) RepoHarvest(ui);
            // v2 任务 13：一键重启已请求——延迟到点再请求引擎退出（让"正在重启游戏…"toast 先上屏；
            // 分离进程按本 PID 等我们退出，见 RestartHelper）
            if (_restartQuitAge >= 0f)
            {
                _restartQuitAge += dt;
                if (_restartQuitAge >= RestartQuitDelay)
                {
                    _restartQuitAge = -1f;
                    RestartHelper.QuitGame();
                }
            }
        }
        catch (Exception e) { Log("warn 桥泵异常: " + e.Message); }
    }

    private static void HandleOutbox(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                // 逐条 try（任务 7，DESIGN §6.2 旧债收口）：批内消息互相独立，
                // 单条异常只弃该条——不弃同批后续（面板滑条+关闭常同批到达）。
                try
                {
                    var type = GetStr(el, "type");
                    switch (type)
                    {
                        case "MC_OPEN":
                            FocusTick.NotifyOpen(); // 任务 8：面板开⇒键盘门交给网页（FocusTick 0.3s 沿触发）
                            // v2 任务 12：L2 补丁碰撞实测（主线程轻量；打开即扫 + MC_SCAN 手动，绝不后台轮询）
                            PatchScan.Refresh();
                            PushRender(BuildSnapshot().ToJson());
                            PushScanData();
                            // v2 任务 7：开门即推待生效摘要（面板底部条初始态；BuildSnapshot 内已刷新 _pendOps）
                            _pendSig = PendingSig();
                            PushPendingSummary();
                            // 任务 4：v1"上次停用未完成"残留 toast 已删——改名执行迁启动清理器，无残留概念
                            break;
                        case "MC_CLOSE": // 面板关闭：键盘门双还原（任务 8）+ 立即落盘全部脏 cfg（防抖收口）
                            FocusTick.NotifyClose();
                            FlushDirty();
                            break;
                        case "MC_SET":
                            HandleSet(GetStr(el, "guid"), GetStr(el, "section"), GetStr(el, "key"), el);
                            break;
                        case "MC_TOGGLE_MOD":
                            HandleToggle(GetStr(el, "guid"), el);
                            break;
                        // v2 任务 8：仓库页命令（面板 → C#；fetch/下载在后台 Task，收割推 MC_REPO_DATA/MC_DL）
                        case "MC_REPO_GET":
                            HandleRepoGet(el.TryGetProperty("force", out var f)
                                && (f.ValueKind == JsonValueKind.True
                                    || (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var fb) && fb)));
                            break;
                        case "MC_CHK":
                            HandleRepoGet(true); // 手动"检查更新"（60 秒冷却）
                            break;
                        case "MC_UPD":
                            HandleRepoUpd(GetStr(el, "repoKey"), all: false);
                            break;
                        case "MC_INST":
                            HandleRepoUpd(GetStr(el, "repoKey"), all: false);
                            break;
                        case "MC_UPDALL":
                            HandleRepoUpd(null, all: true);
                            break;
                        // v2 任务 9：加载排序模式（进模式拉序 / 保存顺序覆盖——校验+整替在专门节）
                        case "MC_ORDER_GET":
                            HandleOrderGet();
                            break;
                        case "MC_ORDER_SAVE":
                            HandleOrderSave(el);
                            break;
                        // v2 任务 10：彻底卸载（拉卸载明细弹窗 / 确认写 delete 意图——专门节）
                        case "MC_UNINST":
                            HandleUninst(GetStr(el, "guid"));
                            break;
                        case "MC_UNINST_DO":
                            HandleUninstDo(GetStr(el, "guid"));
                            break;
                        // v2 任务 11：冲突详情（行内红/灰图标点击 → 回 MC_CF_DATA{guid,cards[]}）
                        case "MC_CF":
                            HandleCf(GetStr(el, "guid"));
                            break;
                        // v2 任务 12：L2 补丁碰撞手动刷新（重扫 → 快照橙标 + MC_SCAN_DATA 汇总）
                        case "MC_SCAN":
                            HandlePatchScan();
                            break;
                        // v2 任务 13：底部待生效条两钮（撤销全部待生效 / 一键重启）
                        case "MC_PENDING_UNDO":
                            HandlePendingUndo();
                            break;
                        case "MC_RESTART":
                            HandleRestart();
                            break;
                        case "MC_EVIDENCE": // 验证辅助（只读）：写证据文件供装机断言（见 DESIGN §6.4）
                            WriteEvidence(GetStr(el, "tag"));
                            break;
                        default:
                            if (!string.IsNullOrEmpty(type)) Log("忽略未知消息类型: " + type);
                            break;
                    }
                }
                catch (Exception e) { Log("warn 单条消息处理异常(" + (GetStr(el, "type") ?? "?") + "): " + e.Message); }
            }
        }
        catch (Exception e) { Log("warn 消息解析异常: " + e.Message); }
    }

    private static void HandleSet(string guid, string section, string key, JsonElement el)
    {
        var mm = Resolve(guid);
        var it = mm != null ? FindItem(mm, section, key) : null;
        var raw = el.TryGetProperty("value", out var v) ? RawOf(v) : null;
        var ok = TrySet(guid, section, key, raw);
        // 任务 14：第三方无登记项——回执 k=展示键（面板 key 发整串 k 已含节前缀，勿重复拼接）、
        // 徽标一律 cfg；任务 15：Tier 1 清单项回执徽标取清单声明（归一面板词汇，缺省 cfg）
        var k = it != null ? it.K : DisplayK(section, key);
        var badge = it != null ? it.Badge : (ThirdParty.BadgeOf(guid, DisplayK(section, key)) ?? "cfg");
        if (ok)
            PushEvent("{\"type\":\"MC_SET_OK\",\"k\":" + JsonW.Str(k) + ",\"badge\":" + JsonW.Str(badge) + "}");
        else
            PushEvent("{\"type\":\"MC_SET_FAIL\",\"k\":" + JsonW.Str(k)
                + ",\"err\":\"write rejected（详见 LogOutput）\"}");
    }

    /// <summary>展示键拼装：面板 key 一律发整展示键 k 原串（已含节前缀）——已带前缀直接用，否则补全。</summary>
    private static string DisplayK(string section, string key)
    {
        if (!string.IsNullOrEmpty(section) && key != null
            && key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase)) return key;
        return section + "." + key;
    }

    private static void HandleToggle(string guid, JsonElement el)
    {
        var enabled = el.TryGetProperty("enabled", out var v)
            && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));
        SetModEnabled(guid, enabled);
        PushRender(BuildSnapshot().ToJson()); // 回新快照（左列状态点/横幅即时反映）
    }

    private static string RawOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText()
    };

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---- C# → JS 推送（Root 上下文；panel.js 存根先存 __mcLastSnapshot/__mcLastEvent，任务 7 长成渲染） ----

    private static void PushRender(string snapshotJson) => PushJs("window.__mcRender&&window.__mcRender(" + snapshotJson + ")");

    private static void PushEvent(string eventJson) => PushJs("window.__mcEvent&&window.__mcEvent(" + eventJson + ")");

    private static void PushJs(string js)
    {
        var web = WebBridge.GetWebView();
        if (web != null) WebBridge.Exec(web, js);
    }

    // ============ Save 防抖 ============

    internal static void MarkDirty(ConfigFile f)
    {
        if (!Dirty.ContainsKey(f))
        {
            Dirty[f] = f.SaveOnConfigSet;
            f.SaveOnConfigSet = false; // 滑条连写不再每写落盘（DESIGN §4.3-2 防抖前提）
        }
        if (_dirtyAge < 0f) _dirtyAge = 0f; // 首写起 3s 节流（写入不重置计时，长拖动也按 3s 批落盘）
    }

    internal static void FlushDirty()
    {
        if (Dirty.Count == 0) { _dirtyAge = -1f; return; }
        foreach (var kv in Dirty)
        {
            try { kv.Key.Save(); }
            catch (Exception e) { Log("warn cfg Save 失败: " + e.Message); }
            try { kv.Key.SaveOnConfigSet = kv.Value; }
            catch { }
        }
        Log($"cfg 落盘（防抖收口）: {Dirty.Count} 个文件");
        Dirty.Clear();
        _dirtyAge = -1f;
    }

    // ============ 验证辅助（只读；装机断言用，正常玩家不触发） ============

    // 断言锚点行（任务 13 热还原断言口径，与各 mod DESIGN 判别字段同源）：
    // 动作：9075 上厕所(QA) / 9098,9099,9100,9130,9131 机器人动作(RS；9130 与 QA 共管)
    //       / 1611 正在种植(QA BT) / 1615 浇水(QA) / 1915 空调吹风(QA)
    private static readonly int[] EvActionIds = { 9075, 9098, 9099, 9100, 9130, 9131, 1611, 1615, 1915 };
    // 包行：1/8/11 背包(BP) / 1061,1063 货舱(RS) / 110001,110009 柜子+材料架(CE尺寸)
    //       / 1004,115000 冰箱包(CE尺寸) / 3002,1023 冰箱包+门口袋(FC+CE载具)
    private static readonly int[] EvBagIds = { 1, 8, 11, 1061, 1063, 110001, 110009, 1004, 115000, 3002, 1023 };
    // FridgeChill 受管包（其 DefaultBagIds 同源；ColdRate 在 Config_Furniture 上按 BagId 过滤）
    private static readonly int[] EvFridgeBags = { 1004, 115000, 115001, 115005, 3002 };

    /// <summary>
    /// 写证据文件（任务 13 扩为多表断言快照，全部只读）：RobotSpeed 移速倍率 entry 值 +
    /// Config_RobotAttribute.MoveSpeed 全行 + 判别动作行 During + 判别包行 Size/Burden +
    /// 冰箱家具 ColdRate 统计（受管 16 行中已被压低行数/最小值）+ Talent 受限行数与花瓶 325
    /// 等级 + 储物柜白名单集合计数。装机断言在 toggle 前后各拍一轮对照（表/trace/cfg 三源之一）。
    /// </summary>
    private static void WriteEvidence(string tag)
    {
        try
        {
            var cfg = CfgOf("com.local.survivallog.robotspeed");
            var e = cfg != null ? Entry(cfg, "机器人提速", "移速倍率") : null;
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] tag={tag}")
              .Append(" rs=" + (e != null ? System.Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture) : "?"))
              .Append(" ms=" + BridgeProbe.Table())
              .Append(" acts=" + EvActions())
              .Append(" bags=" + EvBags())
              .Append(" cold=" + EvCold())
              .Append(" talent=" + EvTalent())
              .Append(" ce=" + EvCeWhitelist());
            System.IO.File.AppendAllText(EvidencePath, sb + "\n");
            Log("evidence: " + sb);
        }
        catch (Exception ex) { Log("warn evidence 失败: " + ex.Message); }
    }

    private static GameCore.HotUpdate.ConfigManager EvCm()
    {
        if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
            return null;
        return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
    }

    private static string EvActions()
    {
        try
        {
            var d = EvCm()?._Config_Action_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            var sb = new StringBuilder();
            foreach (var id in EvActionIds)
            {
                sb.Append(id).Append('=');
                try { sb.Append(d.TryGetValue(id, out var a) && a != null ? a.During.ToString("0.#", CultureInfo.InvariantCulture) : "-").Append(' '); }
                catch { sb.Append("? "); }
            }
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    private static string EvBags()
    {
        try
        {
            var d = EvCm()?._Config_Bag_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            var sb = new StringBuilder();
            foreach (var id in EvBagIds)
            {
                sb.Append(id).Append('=');
                try
                {
                    if (!d.TryGetValue(id, out var b) || b == null) { sb.Append("- "); continue; }
                    var s = b.Size;
                    sb.Append(s != null && s.Count >= 2 ? s[0] + "x" + s[1] : "?").Append('/');
                    sb.Append(b.Burden).Append(' ');
                }
                catch { sb.Append("? "); }
            }
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>冰箱家具 ColdRate 统计：受管行数中已低于原生 0.2 的行数与最小值（FC 生效=16/16@0.1，还原=0/16@0.2）。</summary>
    private static string EvCold()
    {
        try
        {
            var d = EvCm()?._Config_Furniture_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            int total = 0, chilled = 0;
            float min = float.MaxValue;
            var bags = new HashSet<int>(EvFridgeBags);
            foreach (var kv in d)
            {
                var f = kv.Value;
                if (f == null) continue;
                try
                {
                    if (f.ColdRate <= 0f || !bags.Contains(f.BagId)) continue;
                    total++;
                    if (f.ColdRate < 0.199f) chilled++;
                    if (f.ColdRate < min) min = f.ColdRate;
                }
                catch { }
            }
            return total == 0 ? "0" : $"{chilled}/{total}@{min.ToString("0.###", CultureInfo.InvariantCulture)}";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>Talent 受限行数（PlayerSelectId 非空=官方 123；ClearPool 生效=77）+ 花瓶 325 craft/show 等级。</summary>
    private static string EvTalent()
    {
        try
        {
            var cm = EvCm();
            if (cm == null) return "未就绪";
            var td = cm._Config_Talent_Dict;
            int restricted = -1;
            if (td != null && td.Count > 0)
            {
                restricted = 0;
                foreach (var kv in td)
                {
                    var r = kv.Value;
                    if (r == null) continue;
                    try { var p = r.PlayerSelectId; if (p != null && p.Count > 0) restricted++; }
                    catch { }
                }
            }
            var pd = cm._Config_ProductionList_Dict;
            string vase = "?";
            if (pd != null && pd.TryGetValue(325, out var v) && v != null)
            {
                try { vase = v.craft_level + "/" + v.show_level; } catch { }
            }
            return $"restricted={restricted} vase={vase}";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>储物柜白名单集合计数（官方 1；CabinetEverywhere 注入后 710）。</summary>
    private static string EvCeWhitelist()
    {
        try
        {
            var set = GameCore.HotUpdate.ToolCabinetConfig.CraftSourceFurnitureIds;
            return set != null ? set.Count.ToString(CultureInfo.InvariantCulture) : "null";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}
