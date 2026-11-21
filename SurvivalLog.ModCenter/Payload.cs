using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 面板数据契约（任务 6 定稿，任务 7 UI 逐字消费，签名不得偏差）：
/// Snapshot = {self:{ver,ics?}, mods:[ModInfo], tp:[ModInfo]}（tp 第三方通用页，任务 14 填，本任务空数组占位；
/// self.ics v2.0.4 起=面板自身行仓库徽标，可选字段）；
/// ModInfo = {guid,name,en,ver?,desc,enabled,tier?,groups:[{title,items:[Item]}]}（ver v2.0.6 起=插件版本，可选），ovr 类 mod（动作加速）另挂 ovr:[{id,ms}]
/// 与 acts:[{id,n,c,ms}]（v2.0.7 动作目录下拉，QuickActionCatalog.txt 单源；null/空不输出）
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
    public string ShellWarn; // T16：启动清理组件（patchers\MCShell.dll）缺失告警；空不输出

    public string ToJson()
    {
        var sb = new StringBuilder(8192);
        sb.Append("{\"self\":{\"ver\":").Append(JsonW.Str(Self?.Ver));
        if (Self?.Ics != null && Self.Ics.Count > 0) // v2.0.4：自身行仓库徽标（空不输出——老 panel.js 无感）
        {
            sb.Append(",\"ics\":[");
            for (var i = 0; i < Self.Ics.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Self.Ics[i].Write(sb);
            }
            sb.Append(']');
        }
        sb.Append("},\"mods\":[");
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
        sb.Append(']');
        if (!string.IsNullOrEmpty(ShellWarn)) sb.Append(",\"shellWarn\":").Append(JsonW.Str(ShellWarn));
        sb.Append('}');
        return sb.ToString();
    }
}

public sealed class SelfInfo
{
    public string Ver; // ModCenter 版本（csproj 单源 <Version>；由 ModCenterPlugin.Version 注入）
    public List<IcInfo> Ics; // v2.0.4：面板自身行徽标（仓库态 ↑黄/⚠橙——仓库页退役后自更新的唯一入口）；null/空不输出（协议向后兼容）
}

public sealed class ModInfo
{
    public string Guid;  // BepInEx 插件 GUID（面板回传 MC_SET/MC_TOGGLE_MOD 用它路由）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（原型 id 的对应物，兼容短名路由）
    public string Ver;   // 插件版本（v2.0.6：详情页标题行显示；未安装/读不到 null 不输出）
    public string Desc;  // 一句话功能简介
    public bool Enabled; // 启停状态（套件=各 mod General.Enabled；第三方=[DisabledTp] marker 取反，任务 16）
    public bool? Soft;   // 第三方软停能力（任务 16）：Tier 2 约定在场或清单 softDisable→true（套件 null 不输出）
    public bool? Absent; // 第三方缺席行（任务 16）：dll 已禁用未加载（面板补行供再启用；套件 null 不输出）
    public bool Missing; // 套件行 cfg==null（dll 不在场=已卸载/未安装）——面板呈现"未安装/去仓库安装"态（终审必修 7）
    public int? Tier;    // 第三方接入档（任务 15）：null/省略 = Tier 0 通用页；1 = 清单定制页（fixture/面板判别）
    public bool? NoToggle; // mcSpec 2（spec 2026-11-15 §1）：清单 enabled:"none" 无总开关——面板行开关锁定态；null/省略不输出
    public string UsageJson; // mcSpec 2：使用说明预序列化 JSON（形状 {"i":..,"secs":[{"h":..,"ls":[..]}]}，JsonW.UsageJsonOf 构造）；null 不输出（panel USAGE 表兜底过渡，阶段 3 退役）
    public List<GroupInfo> Groups = new();
    public List<OvrInfo> Ovr; // 仅动作加速挂（其他.Overrides 解析结果）；null 不输出该字段
    public List<ActInfo> Acts; // 仅动作加速挂（QuickActionCatalog.txt 动作目录，v2.0.7 面板下拉）；null 不输出（旧 dll 无文件→面板回退 ID 手输）
    public List<BagOvrInfo> BagOvr; // 仅柜子容量挂（个性化 Global.BagOverrides 解析结果，T20）；null 不输出该字段
    public List<IcInfo> Ics; // 行内图标徽标（v2 任务 7）：null/空表不输出该字段（快照协议向后兼容——老 panel.js 无感）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"guid\":").Append(JsonW.Str(Guid))
          .Append(",\"name\":").Append(JsonW.Str(Name))
          .Append(",\"en\":").Append(JsonW.Str(En));
        if (!string.IsNullOrEmpty(Ver)) sb.Append(",\"ver\":").Append(JsonW.Str(Ver)); // v2.0.6：详情页标题行版本（空不输出）
        sb.Append(",\"desc\":").Append(JsonW.Str(Desc))
          .Append(",\"enabled\":").Append(Enabled ? "true" : "false");
        if (Soft == true) sb.Append(",\"soft\":true");       // 任务 16：软停语义标记（仅第三方输出）
        if (Absent == true) sb.Append(",\"absent\":true");   // 任务 16：dll 已禁用缺席行（仅第三方输出）
        if (NoToggle == true) sb.Append(",\"noToggle\":true"); // mcSpec 2：无总开关（面板锁定行开关）
        if (Missing) sb.Append(",\"missing\":true");         // 终审必修 7：套件行 dll 不在场（未安装态）
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
        if (Acts != null && Acts.Count > 0) // v2.0.7：动作目录（QuickAction 产出，下拉按分类显示动作名；空/缺文件不输出——面板回退 ID 手输）
        {
            sb.Append(",\"acts\":[");
            for (var i = 0; i < Acts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Acts[i].Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"n\":").Append(JsonW.Str(Acts[i].N))
                  .Append(",\"c\":").Append(Acts[i].C.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"ms\":").Append(Acts[i].Ms.ToString(CultureInfo.InvariantCulture)).Append('}');
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
        if (!string.IsNullOrEmpty(UsageJson)) sb.Append(",\"usage\":").Append(UsageJson); // mcSpec 2：使用说明（预序列化）
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
    public string VisK; // 条件显示宿主项完整展示键（如 "General.Difficulty"；null=恒显示）
    public string VisV; // 宿主项当前值==VisV 才显示（string 比较，panel 侧即时判定）
    public List<CabInfo> Cabs; // mcSpec 2：ucb 柜子白名单（清单 cabs 透传，面板下拉数据源）；null/空不输出

    /// <summary>ucb 柜子白名单行（mcSpec 2，spec 2026-11-15 §1）：id=BagId、name=显示名、native=原生行数。</summary>
    public sealed class CabInfo
    {
        public long Id;
        public string Name;
        public double Native;
    }

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
        if (VisK != null) sb.Append(",\"vk\":").Append(JsonW.Str(VisK)).Append(",\"vv\":").Append(JsonW.Str(VisV));
        if (Cabs != null && Cabs.Count > 0) // mcSpec 2：ucb 白名单（空不输出——旧面板无感）
        {
            sb.Append(",\"cabs\":[");
            for (var i = 0; i < Cabs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Cabs[i].Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"name\":").Append(JsonW.Str(Cabs[i].Name))
                  .Append(",\"native\":").Append(JsonW.Num(Cabs[i].Native)).Append('}');
            }
            sb.Append(']');
        }
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

/// <summary>动作目录行（v2.0.7，动作加速详情页下拉）：id=动作 ID（ovr 写入值）、n=动作显示名、
/// c=分类 1..6（面板按类型分组下拉；6=野外与其他=引用轴漏网的 Other 玩家动作）、ms=原耗时
/// 毫秒（选项标签展示用）。来源 QuickActionCatalog.txt（QuickAction v1.8.0+ 写，守卫口径单源）；
/// null/空=面板回退 ID 手输。</summary>
public sealed class ActInfo
{
    public int Id;
    public string N;
    public int C;
    public int Ms;
}

/// <summary>柜子容量个性化清单行（T20）：id=BagId，w/h=宽高（"id:WxH" 序列化）。</summary>
public sealed class BagOvrInfo
{
    public int Id;
    public int W;
    public int H;
}

/// <summary>
/// 行内图标徽标（v4 三标记体系，作者拍板）：⚠红 18px=确定不可用（L1 严重冲突）、
/// ⚠橙 15px 小号=其余一切告警（L2 碰撞/L3 叠加/版本不符）、↑黄 18px=仅有新版本；
/// ↓可降级/⊘版本告警/⟳待生效/蓝色↑ 全部退役（待生效只经底部待生效条呈现）。
/// 未安装不挂标（实机反馈修正：主动卸载不是故障，行内锁定开关已表达）。
/// k=色级（小写 red|orange|yellow；gray/blue 仅旧版兼容）；tip=悬停 title 全文（白话）；
/// click=点击路由（cf=冲突详情弹窗 / upd=更新确认弹窗；null/空=纯状态不输出）。
/// 冲突族由 T11/T12 喂，更新/告警由 T8 喂。
/// </summary>
public sealed class IcInfo
{
    public string K;     // red | orange | yellow（小写；gray/blue legacy）
    public string Tip;   // 悬停 title 全文（白话：怎么了+会怎样+点了干嘛）
    public string Click; // cf | upd；null/空 不输出（纯状态）
    public bool? W;      // v2 任务 12（T11 M7）：行已停用 → 徽标弱化（面板降透明度+tip 已加"（已停用）"）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"k\":").Append(JsonW.Str(K))
          .Append(",\"tip\":").Append(JsonW.Str(Tip));
        if (!string.IsNullOrEmpty(Click)) sb.Append(",\"click\":").Append(JsonW.Str(Click));
        if (W == true) sb.Append(",\"w\":true");
        sb.Append('}');
    }
}

/// <summary>
/// 仓库页行 DTO（v2 生命周期任务 8）：<see cref="RepoDecision.RepoRow"/> 的面板投影——
/// Status 固定小写五值（v4.1：downgrade 不再产出——本地比 release tag 高=Ok 静默，降级
/// 全面退役；字段保留仅为历史快照兼容）；Remote=null = fetch 失败（面板"检查失败"，快照
/// checking=true 时显示"检查中…"）；Pending=该行已有待生效 install/replace 意图（可动作
/// 徽标同步消失——控制器裁定 3；v4 行内不再有 ⟳，仅底条呈现）。Gamekb/VerNote 空不输出。
/// </summary>
public sealed class RepoRowInfo
{
    public string Name;    // 显示名（= 仓键）
    public string Status;  // ok|update|downgrade|warn|install（小写）
    public string Local;   // 本地版本；null = 未安装
    public string Remote;  // 远端版本；null = fetch 失败
    public string Why;     // warn 原因；其余态空（v4.1：downgrade 提示随降级态退役）
    public string RepoKey; // 仓键（MC_UPD/MC_INST 路由用）
    public string Guid;    // 套件插件 GUID
    public string Gamekb;  // 远端 gamekb 快照名；空不输出
    public string VerNote; // 未校验游戏版本注记（D8）；空不输出
    public bool Kbw;       // D8 终审必修 6：同版本 Ok 行 gamekb 不符（行内 ⚠橙 + 行级原因；v4.1 本地更高不参与）
    public string KbWhy;   // 上述告警原因全文；空不输出
    public bool Pending;   // 已有待生效 install/replace 意图
    public int? HttpStatus; // T16/D：仅 403（GitHub 限流）输出，供行内专门文案
    public string Notes;   // v2.2.0：远端版本发版说明（update.json.notes，CHANGELOG 节原文）；空不输出（旧源无字段，面板降级）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"name\":").Append(JsonW.Str(Name))
          .Append(",\"status\":").Append(JsonW.Str(Status))
          // Local/Remote 的 null 必须落成 JSON null（历史实现写成 ""，面板 r.remote==null 判据
          // 永不命中——fetch 失败行会误显空远端 + 重装按钮，终审必修 8 行级"检查失败"依赖此修正）
          .Append(",\"local\":").Append(Local == null ? "null" : JsonW.Str(Local))
          .Append(",\"remote\":").Append(Remote == null ? "null" : JsonW.Str(Remote))
          .Append(",\"why\":").Append(JsonW.Str(Why))
          .Append(",\"repoKey\":").Append(JsonW.Str(RepoKey))
          .Append(",\"guid\":").Append(JsonW.Str(Guid));
        if (!string.IsNullOrEmpty(Gamekb)) sb.Append(",\"gamekb\":").Append(JsonW.Str(Gamekb));
        if (!string.IsNullOrEmpty(VerNote)) sb.Append(",\"verNote\":").Append(JsonW.Str(VerNote));
        if (Kbw) sb.Append(",\"kbw\":true");
        if (!string.IsNullOrEmpty(KbWhy)) sb.Append(",\"kbwhy\":").Append(JsonW.Str(KbWhy));
        if (Pending) sb.Append(",\"pending\":true");
        if (HttpStatus == 403) sb.Append(",\"httpStatus\":403"); // T16/D：GitHub 限流专门提示
        if (!string.IsNullOrEmpty(Notes)) sb.Append(",\"notes\":").Append(JsonW.Str(Notes)); // v2.2.0：JsonW.Str 转义 "/\//换行/制表/控制字符
        sb.Append('}');
    }
}

/// <summary>可用更新列表事件（v2.2.0，MC_UPDATES_READY 推送体）：手动检查完成后存在
/// Update 态行时推——面板弹勾选列表（发版说明可展开 + 一键更新所选 MC_UPD_SELECTED）。
/// 行复用 <see cref="RepoRowInfo"/>（name/local/remote/notes 已含）。启动预检不推（只标行）。</summary>
public sealed class UpdatesReady
{
    public List<RepoRowInfo> Rows = new();

    public string ToJson()
    {
        var sb = new StringBuilder(1024);
        sb.Append("{\"type\":\"MC_UPDATES_READY\",\"rows\":[");
        var first = true;
        foreach (var r in Rows)
        {
            if (!first) sb.Append(',');
            first = false;
            r.Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
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
    public bool FailAll;    // 终审必修 8：本轮检查全失败/fetch 异常（入口卡"检查失败（点击重试）"）
    public bool RateLimited; // T16/D：上一轮含 HTTP 403（入口卡/行内"GitHub 限流，请稍后再试"）
    public string Mirror;   // "直连" / "镜像"（Network.MirrorPrefix 非空）
    public List<RepoRowInfo> Rows = new();
    public string DlKey;    // 当前下载链仓键；null = 空闲
    public int DlPct;       // 0..100
    public string DlStage;  // down|check|unzip|write

    public string ToJson()
    {
        var sb = new StringBuilder(4096);
        sb.Append("{\"type\":\"MC_REPO_DATA\",\"lastCheck\":").Append(LastCheck.ToString(CultureInfo.InvariantCulture))
          .Append(",\"checking\":").Append(Checking ? "true" : "false");
        if (FailAll) sb.Append(",\"failAll\":true");
        if (RateLimited) sb.Append(",\"rateLimited\":true");
        sb.Append(",\"mirror\":").Append(JsonW.Str(Mirror))
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
/// 版本台账数据（spec 2026-11-21 §6 版本自由选择，MC_VER_GET {repoKey} → MC_VER_DATA 推送体，
/// __mcEvent 通道）：current=本地版本（_repoRows 该仓 Local；null=未安装，JSON null 同
/// <see cref="RepoRowInfo"/>.Local 口径），versions=台账全集（<see cref="RepoClient.FetchVersions"/>
/// 原序返回，发布侧最新在前；date/notes 可选字段缺失=null，面板降级不显示）。序列化手写
/// StringBuilder（AOT 零反射纪律，同 <see cref="RepoData"/>）。
/// </summary>
public sealed class VerListData
{
    public string RepoKey;
    public string Current;  // 本地版本；null = 未安装
    public List<VerRowInfo> Versions = new();

    public string ToJson()
    {
        var sb = new StringBuilder(2048);
        sb.Append("{\"type\":\"MC_VER_DATA\",\"repoKey\":").Append(JsonW.Str(RepoKey))
          .Append(",\"current\":").Append(Current == null ? "null" : JsonW.Str(Current))
          .Append(",\"versions\":[");
        for (var i = 0; i < Versions.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Versions[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>版本台账行（MC_VER_DATA.versions 元素）：v=版本号（台账主键，解析侧强制非空）、
/// date=发布日期、notes=该版发版说明（可选字段缺失=null，面板降级不显示）。</summary>
public sealed class VerRowInfo
{
    public string V;
    public string Date;
    public string Notes;

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"v\":").Append(JsonW.Str(V))
          .Append(",\"date\":").Append(Date == null ? "null" : JsonW.Str(Date))
          .Append(",\"notes\":").Append(Notes == null ? "null" : JsonW.Str(Notes))
          .Append('}');
    }
}

/// <summary>
/// 冲突详情卡片（v2 任务 11，MC_CF_DATA.cards 元素；面板照原型 cfHtml 渲染）：
/// lv=色级（red|orange|gray，决定图标色/字形）、tag=标签（冲突|碰撞|同域）、title=卡片副标题、
/// who=冲突对象（显示名，面板以"、"连接加粗）、reason=原因全文、tips=处置建议（面板编号列表）、
/// err=true 仅运行异常卡（任务 15；面板挂错误日志入口，其余卡不输出该字段）。
/// </summary>
public sealed class CfCardInfo
{
    public string Lv;
    public string Tag;
    public string Title;
    public List<string> Who = new();
    public string Reason;
    public List<string> Tips = new();
    public bool Err; // 任务 15：运行异常卡（面板据此挂"查看错误日志"入口，任务 16）；仅 true 输出

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
        if (Err) sb.Append("],\"err\":true}"); // false 不输出，保持旧 payload 字节不变
        else sb.Append("]}");
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
/// 错误日志面板数据（任务 16，MC_ERR_GET → MC_ERR_DATA 推送体，__mcEvent 通道）：
/// rows=<see cref="ErrorLog.Keys"/> 投影（会话级、无历史），gameVer=本地游戏版本人读串
///（面板复制环境头用；通道失败空串）。序列化手写 StringBuilder（AOT 零反射纪律，同
/// <see cref="RepoData"/>）。
/// </summary>
public sealed class ErrData
{
    public string GameVer;
    public List<ErrRowInfo> Rows = new();

    public string ToJson()
    {
        var sb = new StringBuilder(2048);
        sb.Append("{\"type\":\"MC_ERR_DATA\",\"gameVer\":").Append(JsonW.Str(GameVer))
          .Append(",\"rows\":[");
        for (var i = 0; i < Rows.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Rows[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

/// <summary>错误面板行（MC_ERR_DATA.rows 元素）：guid=插件 GUID 或 _unattributed（name=未归属）；
/// name/ver 由 ConfigBridge.BuildErrData 装配（_repoRows 优先→SuiteRoster→链上插件元数据）；
/// suite=SuiteRoster 命中（未归属 false——面板归三方组）；count=折叠后记录条数（非 Repeat 总和）；
/// last=最新一条 HH:mm:ss；entries=记录 newest-first（≤50，ErrorLog 容量上限已保证）。</summary>
public sealed class ErrRowInfo
{
    public string Guid;
    public string Name;
    public string Ver;
    public bool Suite;
    public int Count;
    public string Last;
    public List<ErrEntryInfo> Entries = new();

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"guid\":").Append(JsonW.Str(Guid))
          .Append(",\"name\":").Append(JsonW.Str(Name))
          .Append(",\"ver\":").Append(JsonW.Str(Ver))
          .Append(",\"suite\":").Append(Suite ? "true" : "false")
          .Append(",\"count\":").Append(Count.ToString(CultureInfo.InvariantCulture))
          .Append(",\"last\":").Append(JsonW.Str(Last))
          .Append(",\"entries\":[");
        for (var i = 0; i < Entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Entries[i].Write(sb);
        }
        sb.Append("]}");
    }
}

/// <summary>一条错误记录（MC_ERR_DATA.entries 元素）：t=HH:mm:ss、lv=Error/Fatal、
/// src=日志源名、msg=全文首行、stack=堆栈段（可空）、rep=连续重复次数（面板 &gt;1 才内联显示）。</summary>
public sealed class ErrEntryInfo
{
    public string T;
    public string Lv;
    public string Src;
    public string Msg;
    public string Stack;
    public int Rep;

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"t\":").Append(JsonW.Str(T))
          .Append(",\"lv\":").Append(JsonW.Str(Lv))
          .Append(",\"src\":").Append(JsonW.Str(Src))
          .Append(",\"msg\":").Append(JsonW.Str(Msg))
          .Append(",\"stack\":").Append(JsonW.Str(Stack))
          .Append(",\"rep\":").Append(Rep.ToString(CultureInfo.InvariantCulture))
          .Append('}');
    }
}

/// <summary>
/// L2 补丁碰撞扫描数据（v2 任务 12，MC_SCAN_DATA 推送体，__mcEvent 通道）：
/// at=扫描时间（Unix 毫秒）、patched=已挂补丁方法总数、game=游戏侧已挂补丁方法数（含单 owner；
/// fix I-1 口径）、total=游戏侧碰撞组数（routed 可归属 + unknown 全未知来源）、framework=框架侧
/// 多 owner 组数（裁定 A：披露但不计入 total/不铺行）、ms=扫描耗时、scope=过滤口径文案、
/// error=扫描异常。items=游戏侧碰撞组；frameItems=框架侧多 owner 组（仅汇总披露）。
/// </summary>
public sealed class PatchScanData
{
    public long At;
    public int Patched;
    public int Game;
    public int Total;
    public int Routed;
    public int Unknown;
    public int Framework;
    public long Ms;
    public string Scope;
    public string Error;
    public List<PatchScanItem> Items = new();
    public List<PatchScanItem> FrameworkItems = new();

    public string ToJson()
    {
        var sb = new StringBuilder(3072);
        sb.Append("{\"type\":\"MC_SCAN_DATA\",\"at\":").Append(At.ToString(CultureInfo.InvariantCulture))
          .Append(",\"patched\":").Append(Patched.ToString(CultureInfo.InvariantCulture))
          .Append(",\"game\":").Append(Game.ToString(CultureInfo.InvariantCulture))
          .Append(",\"total\":").Append(Total.ToString(CultureInfo.InvariantCulture))
          .Append(",\"routed\":").Append(Routed.ToString(CultureInfo.InvariantCulture))
          .Append(",\"unknown\":").Append(Unknown.ToString(CultureInfo.InvariantCulture))
          .Append(",\"framework\":").Append(Framework.ToString(CultureInfo.InvariantCulture))
          .Append(",\"ms\":").Append(Ms.ToString(CultureInfo.InvariantCulture))
          .Append(",\"scope\":").Append(JsonW.Str(Scope));
        if (!string.IsNullOrEmpty(Error)) sb.Append(",\"error\":").Append(JsonW.Str(Error));
        sb.Append(",\"items\":[");
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Items[i].Write(sb);
        }
        sb.Append("],\"frameItems\":[");
        for (var i = 0; i < FrameworkItems.Count; i++)
        {
            if (i > 0) sb.Append(',');
            FrameworkItems[i].Write(sb);
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
        /// <summary>mcSpec 2 使用说明预序列化：{"i":intro,"secs":[{"h":heading,"ls":[bullets]}]}（形状镜像 panel USAGE 表
        /// 内部结构，Task 5 面板仅换数据源）；intro/分段缺失逐级省略字段；全空返回 null（ModInfo.UsageJson 不输出）。</summary>
        internal static string UsageJsonOf(string intro, IEnumerable<(string Heading, IEnumerable<string> Bullets)> secs)
        {
            var hasIntro = !string.IsNullOrEmpty(intro);
            var hasSecs = false;
            if (secs != null)
                foreach (var _ in secs) { hasSecs = true; break; }
            if (!hasIntro && !hasSecs) return null;
            var sb = new StringBuilder(128);
            sb.Append('{');
            if (hasIntro) sb.Append("\"i\":").Append(Str(intro));
            if (hasSecs)
            {
                if (hasIntro) sb.Append(',');
                sb.Append("\"secs\":[");
                var first = true;
                foreach (var (h, ls) in secs)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{");
                    if (!string.IsNullOrEmpty(h)) sb.Append("\"h\":").Append(Str(h));
                    if (ls != null)
                    {
                        var any = false;
                        foreach (var _ in ls) { any = true; break; }
                        if (any)
                        {
                            if (!string.IsNullOrEmpty(h)) sb.Append(',');
                            sb.Append("\"ls\":[");
                            var f2 = true;
                            foreach (var b in ls)
                            {
                                if (!f2) sb.Append(',');
                                f2 = false;
                                sb.Append(Str(b));
                            }
                            sb.Append(']');
                        }
                    }
                    sb.Append('}');
                }
                sb.Append(']');
            }
            sb.Append('}');
            return sb.ToString();
        }

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
