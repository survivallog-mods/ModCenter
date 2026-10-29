using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 面板数据契约（任务 6 定稿，任务 7 UI 逐字消费，签名不得偏差）：
/// Snapshot = {self:{ver}, mods:[ModInfo], tp:[ModInfo]}（tp 第三方通用页，任务 14 填，本任务空数组占位）；
/// ModInfo = {guid,name,en,desc,enabled,tier?,groups:[{title,items:[Item]}]}，ovr 类 mod（动作加速）另挂 ovr:[{id,ms}]
/// （tier? 仅第三方 Tier 1 清单定制页输出 1，任务 15；省略 = Tier 0）；
/// Item = {k,l,t,v,min?,max?,st?,b,note?,opts?}——与原型 v4 MODS 数组同名同义
/// （docs/superpowers/specs/2026-10-24-modcenter-prototype.html）。
/// 序列化手写 StringBuilder（AOT 零反射；套件 JSON 纪律同 EndlessGuard「零外部依赖」，
/// 入向解析用 JsonDocument——EndlessGuard ScavengePlans.cs 同款实绩 API）。
/// </summary>
public sealed class Snapshot
{
    public SelfInfo Self;
    public List<ModInfo> Mods = new();
    public List<ModInfo> Tp = new(); // 第三方 Tier 0/1（任务 14）

    public string ToJson()
    {
        var sb = new StringBuilder(8192);
        sb.Append("{\"self\":{\"ver\":").Append(JsonW.Str(Self?.Ver)).Append("},\"mods\":[");
        for (var i = 0; i < Mods.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Mods[i].Write(sb);
        }
        sb.Append("],\"tp\":[");
        for (var i = 0; i < Tp.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Tp[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

public sealed class SelfInfo
{
    public string Ver; // ModCenter 版本（跟 suite version.json，作者定号）
}

public sealed class ModInfo
{
    public string Guid;  // BepInEx 插件 GUID（面板回传 MC_SET/MC_TOGGLE_MOD 用它路由）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（原型 id 的对应物，兼容短名路由）
    public string Desc;  // 一句话功能简介
    public bool Enabled; // 启停状态（套件=各 mod General.Enabled；第三方=[DisabledTp] marker 取反，任务 16）
    public bool? Soft;   // 第三方软停能力（任务 16）：Tier 2 约定在场或清单 softDisable→true（套件 null 不输出）
    public bool? Absent; // 第三方缺席行（任务 16）：dll 已禁用未加载（面板补行供再启用；套件 null 不输出）
    public int? Tier;    // 第三方接入档（任务 15）：null/省略 = Tier 0 通用页；1 = 清单定制页（fixture/面板判别）
    public List<GroupInfo> Groups = new();
    public List<OvrInfo> Ovr; // 仅动作加速挂（其他.Overrides 解析结果）；null 不输出该字段
    public List<BagOvrInfo> BagOvr; // 仅柜子容量挂（个性化 Global.BagOverrides 解析结果，T20）；null 不输出该字段
    public List<IcInfo> Ics; // 行内图标徽标（v2 任务 7）：null/空表不输出该字段（快照协议向后兼容——老 panel.js 无感）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"guid\":").Append(JsonW.Str(Guid))
          .Append(",\"name\":").Append(JsonW.Str(Name))
          .Append(",\"en\":").Append(JsonW.Str(En))
          .Append(",\"desc\":").Append(JsonW.Str(Desc))
          .Append(",\"enabled\":").Append(Enabled ? "true" : "false");
        if (Soft == true) sb.Append(",\"soft\":true");       // 任务 16：软停语义标记（仅第三方输出）
        if (Absent == true) sb.Append(",\"absent\":true");   // 任务 16：dll 已禁用缺席行（仅第三方输出）
        if (Tier != null) sb.Append(",\"tier\":").Append(Tier.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"groups\":[");
        for (var i = 0; i < Groups.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Groups[i].Write(sb);
        }
        sb.Append(']');
        if (Ovr != null)
        {
            sb.Append(",\"ovr\":[");
            for (var i = 0; i < Ovr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Ovr[i].Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"ms\":").Append(Ovr[i].Ms.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
        }
        if (BagOvr != null) // T20：柜子容量个性化清单（{id,w,h}；面板 bagOvrHtml 消费）
        {
            sb.Append(",\"bagOvr\":[");
            for (var i = 0; i < BagOvr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(BagOvr[i].Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"w\":").Append(BagOvr[i].W.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"h\":").Append(BagOvr[i].H.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
        }
        if (Ics != null && Ics.Count > 0) // v2 任务 7：行内图标徽标（空表不输出——老面板/夹具断言零影响）
        {
            sb.Append(",\"ics\":[");
            for (var i = 0; i < Ics.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Ics[i].Write(sb);
            }
            sb.Append(']');
        }
        sb.Append('}');
    }
}

public sealed class GroupInfo
{
    public string Title; // 分组标题（显示用；≠cfg 节名）
    public List<ItemInfo> Items = new();

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"title\":").Append(JsonW.Str(Title)).Append(",\"items\":[");
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Items[i].Write(sb);
        }
        sb.Append("]}");
    }
}

public sealed class ItemInfo
{
    public string K;     // cfg 地址展示串（联动项 = "节.键A + 键B"，展示用；写路由以 Meta.WriteKeys 为准）
    public string L;     // 面板中文名
    public string T;     // float / int / bool / sel
    public object V;     // 当前值（快照时从 ConfigEntry.BoxedValue 读；entry 缺失用 Meta 兜底）
    public double? Min;  // 范围：优先反射 AcceptableValueRange，缺失用 Meta 兜底（bool/sel 为 null 不输出）
    public double? Max;
    public double? St;  // 滑条步进：AcceptableValue 不携带步进，永远 Meta 兜底
    public string B;     // 徽标：now(⚡≤5s) / reload(📁读档后) / reboot(🔄重启后) / cfg(⚙)
    public string Fail;  // Tier 2 状态回报 failed（任务 16）：非空=该项置灰展示原因（QueryStates）
    public string Note;  // 补充说明（联动项含动态"当前 宽w×高h"）
    public List<string> Opts; // sel 选项（enum 名/白名单值/Meta 兜底）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"k\":").Append(JsonW.Str(K))
          .Append(",\"l\":").Append(JsonW.Str(L))
          .Append(",\"t\":").Append(JsonW.Str(T))
          .Append(",\"v\":").Append(WriteValue());
        if (Min != null) sb.Append(",\"min\":").Append(JsonW.Num(Min.Value));
        if (Max != null) sb.Append(",\"max\":").Append(JsonW.Num(Max.Value));
        if (St != null) sb.Append(",\"st\":").Append(JsonW.Num(St.Value));
        sb.Append(",\"b\":").Append(JsonW.Str(B));
        if (Fail != null) sb.Append(",\"fail\":").Append(JsonW.Str(Fail)); // Tier 2 failed 状态（任务 16）
        if (Note != null) sb.Append(",\"note\":").Append(JsonW.Str(Note));
        if (Opts != null && Opts.Count > 0)
        {
            sb.Append(",\"opts\":[");
            for (var i = 0; i < Opts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonW.Str(Opts[i]));
            }
            sb.Append(']');
        }
        sb.Append('}');
    }

    private string WriteValue()
    {
        if (V == null) return "null";
        switch (T)
        {
            case "bool":
                return (V is bool b) && b ? "true" : "false";
            case "float":
            case "int":
                return JsonW.Num(System.Convert.ToDouble(V, CultureInfo.InvariantCulture));
            default: // sel（string/enum）
                return JsonW.Str(System.Convert.ToString(V, CultureInfo.InvariantCulture));
        }
    }
}

public sealed class OvrInfo
{
    public int Id; // 动作 ID
    public int Ms; // 毫秒
}

/// <summary>柜子容量个性化清单行（T20）：id=BagId，w/h=宽高（"id:WxH" 序列化）。</summary>
public sealed class BagOvrInfo
{
    public int Id;
    public int W;
    public int H;
}

/// <summary>
/// 行内图标徽标（v2 生命周期任务 7，七形：⚠红冲突/⇄橙碰撞/◎灰同域/↑蓝可更新/↓橙可降级/⊘橙版本告警/⟳琥珀待生效）。
/// k=色级（小写 red|orange|gray|blue|amber）；tip=悬停 title 全文；click=点击路由
/// （cf=冲突详情弹窗 / upd=更新确认弹窗；null/空=纯状态不输出，如 ⟳）；g=可选字形符——
/// ↓与⊘同为 orange+upd，无 g 无法消歧（缺省按 k+click 推断：red→⚠ / orange+cf→⇄ / gray→◎ /
/// blue→↑ / orange+upd→↓ / amber→⟳；T8 喂 ⊘ 时带 g:"kbw"，panel.js 将其映射为 ⊘ 字形）。
/// T7 只接真数据 ⟳ 待生效（pending.json）；冲突形由 T11 喂，更新/降级/告警三形由 T8 喂。
/// </summary>
public sealed class IcInfo
{
    public string K;     // red | orange | gray | blue | amber（小写）
    public string Tip;   // 悬停 title 全文（原型：悬停 title 全文）
    public string Click; // cf | upd；null/空 不输出（纯状态）
    public string G;     // 可选字形符（↓/⊘ 消歧）；null 不输出
    public bool? W;      // v2 任务 12（T11 M7）：行已停用 → 徽标弱化（面板降透明度+tip 已加"（已停用）"）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"k\":").Append(JsonW.Str(K))
          .Append(",\"tip\":").Append(JsonW.Str(Tip));
        if (!string.IsNullOrEmpty(Click)) sb.Append(",\"click\":").Append(JsonW.Str(Click));
        if (!string.IsNullOrEmpty(G)) sb.Append(",\"g\":").Append(JsonW.Str(G));
        if (W == true) sb.Append(",\"w\":true");
        sb.Append('}');
    }
}

/// <summary>
/// 仓库页行 DTO（v2 生命周期任务 8）：<see cref="RepoDecision.RepoRow"/> 的面板投影——
/// Status 固定小写五值；Remote=null = fetch 失败（面板"检查失败"，快照 checking=true 时
/// 显示"检查中…"）；Pending=该行已有待生效 install/replace 意图（⟳ 替代动作按钮，
/// 行内 ↑/↓ 同步消失——控制器裁定 3）。Gamekb/VerNote 空不输出。
/// </summary>
public sealed class RepoRowInfo
{
    public string Name;    // 显示名（= 仓键）
    public string Status;  // ok|update|downgrade|warn|install（小写）
    public string Local;   // 本地版本；null = 未安装
    public string Remote;  // 远端版本；null = fetch 失败
    public string Why;     // warn 原因 / downgrade 提示；其余态空
    public string RepoKey; // 仓键（MC_UPD/MC_INST 路由用）
    public string Guid;    // 套件插件 GUID
    public string Gamekb;  // 远端 gamekb 快照名；空不输出
    public string VerNote; // 未校验游戏版本注记（D8）；空不输出
    public bool Pending;   // 已有待生效 install/replace 意图

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"name\":").Append(JsonW.Str(Name))
          .Append(",\"status\":").Append(JsonW.Str(Status))
          .Append(",\"local\":").Append(JsonW.Str(Local))
          .Append(",\"remote\":").Append(JsonW.Str(Remote))
          .Append(",\"why\":").Append(JsonW.Str(Why))
          .Append(",\"repoKey\":").Append(JsonW.Str(RepoKey))
          .Append(",\"guid\":").Append(JsonW.Str(Guid));
        if (!string.IsNullOrEmpty(Gamekb)) sb.Append(",\"gamekb\":").Append(JsonW.Str(Gamekb));
        if (!string.IsNullOrEmpty(VerNote)) sb.Append(",\"verNote\":").Append(JsonW.Str(VerNote));
        if (Pending) sb.Append(",\"pending\":true");
        sb.Append('}');
    }
}

/// <summary>
/// 仓库页快照（任务 8，MC_REPO_DATA 推送体，__mcEvent 通道）：rows=12 行五态投影 +
/// lastCheck（Unix 毫秒，0=从未）+ checking（后台 fetch 在跑）+ dl（当前下载链状态，
/// null=空闲——面板重开时恢复行内/弹窗进度态）。序列化手写 StringBuilder（AOT 零反射纪律）。
/// </summary>
public sealed class RepoData
{
    public long LastCheck;  // 上次完成一轮检查的 Unix 毫秒；0 = 从未
    public bool Checking;   // 后台 fetch 在跑
    public string Mirror;   // "直连" / "镜像"（Network.MirrorPrefix 非空）
    public List<RepoRowInfo> Rows = new();
    public string DlKey;    // 当前下载链仓键；null = 空闲
    public int DlPct;       // 0..100
    public string DlStage;  // down|check|unzip|write

    public string ToJson()
    {
        var sb = new StringBuilder(4096);
        sb.Append("{\"type\":\"MC_REPO_DATA\",\"lastCheck\":").Append(LastCheck.ToString(CultureInfo.InvariantCulture))
          .Append(",\"checking\":").Append(Checking ? "true" : "false")
          .Append(",\"mirror\":").Append(JsonW.Str(Mirror))
          .Append(",\"dl\":");
        if (DlKey == null) sb.Append("null");
        else sb.Append("{\"repoKey\":").Append(JsonW.Str(DlKey))
               .Append(",\"pct\":").Append(DlPct.ToString(CultureInfo.InvariantCulture))
               .Append(",\"stage\":").Append(JsonW.Str(DlStage)).Append('}');
        sb.Append(",\"rows\":[");
        for (var i = 0; i < Rows.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Rows[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>
/// 冲突详情卡片（v2 任务 11，MC_CF_DATA.cards 元素；面板照原型 cfHtml 渲染）：
/// lv=色级（red|orange|gray，决定图标色/字形）、tag=标签（冲突|碰撞|同域）、title=卡片副标题、
/// who=冲突对象（显示名，面板以"、"连接加粗）、reason=原因全文、tips=处置建议（面板编号列表）。
/// </summary>
public sealed class CfCardInfo
{
    public string Lv;
    public string Tag;
    public string Title;
    public List<string> Who = new();
    public string Reason;
    public List<string> Tips = new();

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"lv\":").Append(JsonW.Str(Lv))
          .Append(",\"tag\":").Append(JsonW.Str(Tag))
          .Append(",\"title\":").Append(JsonW.Str(Title))
          .Append(",\"who\":[");
        for (var i = 0; i < Who.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(Who[i]));
        }
        sb.Append("],\"reason\":").Append(JsonW.Str(Reason))
          .Append(",\"tips\":[");
        for (var i = 0; i < Tips.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(Tips[i]));
        }
        sb.Append("]}");
    }
}

/// <summary>
/// 冲突详情回包（v2 任务 11，MC_CF {guid} → MC_CF_DATA {guid,cards[]}）：cards 顺序=面板卡片顺序
/// （L1 命中按色级 red→orange→gray 稳定排序；L3 同域卡最后）。空表=无冲突（面板给空态壳）。
/// </summary>
public sealed class CfData
{
    public string Guid;
    public List<CfCardInfo> Cards = new();

    public string ToJson()
    {
        var sb = new StringBuilder(1024);
        sb.Append("{\"type\":\"MC_CF_DATA\",\"guid\":").Append(JsonW.Str(Guid))
          .Append(",\"cards\":[");
        for (var i = 0; i < Cards.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Cards[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>
/// L2 补丁碰撞扫描数据（v2 任务 12，MC_SCAN_DATA 推送体，__mcEvent 通道）：
/// at=扫描时间（Unix 毫秒）、patched=已挂补丁方法总数、game=游戏侧方法数、total=碰撞组数
/// （routed 可归属 + unknown 全未知来源）、ms=扫描耗时、scope=过滤口径文案、error=扫描异常。
/// items=全部碰撞组（可归属在前，未知来源在后；行内橙标只挂可归属组，未知来源只进本汇总）。
/// </summary>
public sealed class PatchScanData
{
    public long At;
    public int Patched;
    public int Game;
    public int Total;
    public int Routed;
    public int Unknown;
    public long Ms;
    public string Scope;
    public string Error;
    public List<PatchScanItem> Items = new();

    public string ToJson()
    {
        var sb = new StringBuilder(3072);
        sb.Append("{\"type\":\"MC_SCAN_DATA\",\"at\":").Append(At.ToString(CultureInfo.InvariantCulture))
          .Append(",\"patched\":").Append(Patched.ToString(CultureInfo.InvariantCulture))
          .Append(",\"game\":").Append(Game.ToString(CultureInfo.InvariantCulture))
          .Append(",\"total\":").Append(Total.ToString(CultureInfo.InvariantCulture))
          .Append(",\"routed\":").Append(Routed.ToString(CultureInfo.InvariantCulture))
          .Append(",\"unknown\":").Append(Unknown.ToString(CultureInfo.InvariantCulture))
          .Append(",\"ms\":").Append(Ms.ToString(CultureInfo.InvariantCulture))
          .Append(",\"scope\":").Append(JsonW.Str(Scope));
        if (!string.IsNullOrEmpty(Error)) sb.Append(",\"error\":").Append(JsonW.Str(Error));
        sb.Append(",\"items\":[");
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Items[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>一条碰撞组的面板投影：method=短类型名.方法名、patches=补丁实例数、
/// unknown=全部 owner 未知来源、owners=owner 显示串（含"未知来源（id）"标注）。</summary>
public sealed class PatchScanItem
{
    public string Method;
    public int Patches;
    public bool Unknown;
    public List<string> Owners = new();

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"method\":").Append(JsonW.Str(Method))
          .Append(",\"patches\":").Append(Patches.ToString(CultureInfo.InvariantCulture));
        if (Unknown) sb.Append(",\"unknown\":true");
        sb.Append(",\"owners\":[");
        for (var i = 0; i < Owners.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonW.Str(Owners[i]));
        }
        sb.Append("]}");
    }
}

/// <summary>手写 JSON 片段工具（转义 + 不变文化数字）。</summary>
internal static class JsonW
{
    internal static string Str(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    internal static string Num(double? d) =>
        d == null ? "" : d.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
