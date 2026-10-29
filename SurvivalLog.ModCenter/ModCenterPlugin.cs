using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ModCenter;

/// <summary>
/// MOD 管理中心（宿主插件）：主菜单/ESC 菜单入口 + WebUI 面板 + 套件各 mod 开关桥。
/// 三层架构与迭代史见 DESIGN.md；菜单锚点单源见本目录 ANCHORS.md（T1，改挂钩点须同步）。
/// 任务 3：宿主入口 + Ticker 心跳 + 日志门/自证通道；任务 4：反射桥探针（BridgeProbe，cfg
/// Debug.Probe 门控，结论固化 DESIGN.md「反射桥 API 实证记录」）；任务 5：注入骨架（WebBridge
/// 通道 + MenuEntryInjector 双锚点入口 + panel.js 面板壳 + outbox 轮询）；任务 6：配置桥
/// （Meta 九表 + ConfigBridge 快照/写/消息协议/Save 防抖，面板 UI 任务 7 移植）；任务 8：
/// 键盘门复用（FocusTick——面板开 SetKeyboardEnabled+IME On，关双还原，MC_OPEN/MC_CLOSE 驱动）。
/// v2 生命周期任务 5：网络层 RepoClient（GitHub release 客户端：直连+可配镜像+快速失败，
/// cfg Network 节三键）+ Debug.NetSelfTest 自诊断探针（NetProbe，证据 ModCenter_netspeed.txt）。
/// v2 生命周期任务 6：决策层 RepoDecision（12 仓五态 + gamekb 告警通道 D7/D8 只告警不拦截）+
/// GameVersion 本地游戏版本通道（appmanifest buildid 定案）+ PendingWriter（pending 意图主线程
/// 唯一写者）+ Debug.RepoSelfTest 探针（RepoProbe，证据 ModCenter_reposelftest.txt / ModCenter_gamever.txt）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ModCenterPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.modcenter";
    public const string Name = "ModCenter";
    public const string Version = "1.7.0"; // 版本号 csproj 单源（<Version>），此处与 csproj 同步

    public static ModCenterPlugin Instance { get; private set; }

    // 日志源快照：后续任务统一经 Log(msg) 静态门输出，不自持 ManualLogSource
    // （字段不能与 Log(string) 方法同名，故命名 Logger；new 显式遮蔽 BasePlugin.Log 实例属性）
    internal static ManualLogSource Logger;

    // 任务 4：反射桥诊断探针开关（默认关）。true 时启动 30s 后由 Ticker 主线程跑一轮，
    // 证据落 BepInEx/ModCenter_probe.txt；结论（确切 API/类型/坑）见 DESIGN.md。
    internal static ConfigEntry<bool> DebugProbe;

    // v2 任务 5：网络层 cfg（Network 节，RepoClient 消费）。镜像前缀默认空=直连（选型结论见
    // DESIGN §6.11：本机直连可达，gh-proxy.com 双端点可用作示例；ghproxy.net/ghfast.top 只代理
    // 下载不代理 api.github.com，不能作本键取值）。
    internal static ConfigEntry<string> NetworkMirrorPrefix;
    internal static ConfigEntry<int> NetworkCheckTimeoutMs;    // 默认 5000
    internal static ConfigEntry<int> NetworkDownloadTimeoutMs; // 默认 60000

    // v2 任务 5：网络自诊断探针开关（默认关，仿 Debug.Probe）。true 时启动 10s 后对
    // survivallog-mods/RobotSpeed 跑一轮 fetch+下载+MD5+清理（后台 Task，Ticker 主线程收割），
    // 证据落 BepInEx/ModCenter_netspeed.txt（硬规则 9：不依赖 LogOutput）。
    internal static ConfigEntry<bool> DebugNetSelfTest;

    // v2 任务 6：仓库决策层自诊断探针开关（默认关，仿 NetSelfTest）。true 时启动 10s 后跑一轮：
    // 主线程 gamekb 通道四候选 dump（证据 ModCenter_gamever.txt）+ 12 仓 fetch → 五态决策
    // （真基线 / 强制通道失败 / 合成分支自证三档）+ PendingWriter 合并语义（临时文件，验后即删）。
    // 全程一轮 12 次 API 请求（匿名限流预算内），证据落 BepInEx/ModCenter_reposelftest.txt。
    internal static ConfigEntry<bool> DebugRepoSelfTest;

    // v2 任务 12：L2 补丁碰撞探针开关（默认关，仿 NetSelfTest）。true 时启动 10s 后主线程 dump
    // Harmony.GetAllPatchedMethods 全量清单 + 套件已挂方法 PatchInfo 逐字段/多反射路径，
    // 并跑一轮 PatchScan.Scan() dry-run；证据落 BepInEx/ModCenter_patchscan.txt（硬规则 9）。
    internal static ConfigEntry<bool> DebugPatchScan;

    public override void Load()
    {
        Instance = this;
        Logger = base.Log;

        DebugProbe = Config.Bind("Debug", "Probe", false,
            "反射桥 API 诊断探针（默认关，仅供开发验证）。true 时启动 30 秒后对 RobotSpeed『机器人提速/移速倍率』\n" +
            "执行一轮：枚举插件 → 取 ConfigFile → 读原值 → 写 3 → 等 5 秒验内存表热重写（真原值 2.4×3=7.2）→ 写回原值并落盘。\n" +
            "证据追加到 BepInEx/ModCenter_probe.txt。玩家长开无意义，验完请改回 false。");

        // v2 任务 5：网络层三键（值语义见 RepoClient 头注；绑定在此、静态门读取）
        NetworkMirrorPrefix = Config.Bind("Network", "MirrorPrefix", "",
            "GitHub 访问镜像前缀（默认空 = 直连）。非空时形如 'https://gh-proxy.com'，\n" +
            "对 api.github.com 与 github.com 的 release 资产 URL 生效（<前缀>/https://github.com/... 形态）。\n" +
            "直连可用就不要填；镜像服务会迁移失效，换了只改这里不改代码。");
        NetworkCheckTimeoutMs = Config.Bind("Network", "CheckTimeoutMs", 5000,
            "检查更新（latest 元数据 + update.json 两跳）的单次超时，毫秒。超时即判失败降级，不重试。");
        NetworkDownloadTimeoutMs = Config.Bind("Network", "DownloadTimeoutMs", 60000,
            "下载 zip 资产的超时，毫秒。超时即失败降级（半成品 .part 会被清理），不重试。");

        DebugNetSelfTest = Config.Bind("Debug", "NetSelfTest", false,
            "网络自诊断探针（默认关，仅供开发验证）。true 时启动 10 秒后对 survivallog-mods/RobotSpeed\n" +
            "跑一轮：fetch latest（版本/md5/gamekb + 耗时）→ 下载 zip 到 ModCenter\\staging → 校验 MD5 → 删 zip 不留残留。\n" +
            "全程后台线程（不卡面板/主线程），证据写 BepInEx/ModCenter_netspeed.txt。验完请改回 false。");

        DebugRepoSelfTest = Config.Bind("Debug", "RepoSelfTest", false,
            "仓库决策层自诊断探针（默认关，仅供开发验证）。true 时启动 10 秒后跑一轮（仅此一轮）：\n" +
            "①游戏版本通道四候选 dump（证据 ModCenter_gamever.txt）→ ②后台 fetch 全部 12 仓 → 五态决策\n" +
            "（真基线轮 + 强制通道失败轮 + 合成分支自证轮）→ ③PendingWriter 合并语义对临时文件自证（验后即删）。\n" +
            "证据写 BepInEx/ModCenter_reposelftest.txt。一轮消耗 12 次 GitHub 匿名 API 请求，验完请改回 false。");

        DebugPatchScan = Config.Bind("Debug", "PatchScan", false,
            "L2 补丁碰撞探针（默认关，仅供开发验证）。true 时启动 10 秒后跑一轮（仅此一轮）：\n" +
            "Harmony.GetAllPatchedMethods 全量清单 + 套件已挂方法 PatchInfo 逐字段 dump（owners/补丁方法声明类/多反射路径）\n" +
            "+ 面板扫描器 dry-run（碰撞组数与耗时）。证据写 BepInEx/ModCenter_patchscan.txt。验完请改回 false。");

        FocusTick.Reset(); // 任务 8：清上一轮键盘门自证文件（每轮运行从空文件开始）

        DllDisabler.ScanOnLoad(); // 任务 4/T16：[DisabledTp] 对照（改名执行已迁启动清理器——此处仅预 Bind 保键 + 清理器未跑时的软停重放登记）

        // 任务 16：退出双钩子（幂等互备）——MonoBehaviour OnApplicationQuit 消息 + Application.quitting 事件。
        // 后者在某些退出路径（如引擎内部请求退出）可能先到/独到；两钩同函数，_quitDone 守卫只跑一次。
        try
        {
            UnityEngine.Application.add_quitting(
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                    new Action(ModCenterTicker.RunQuitHooks)));
        }
        catch (Exception e) { Log("warn quitting 事件订阅降级（OnApplicationQuit 消息单钩兜底）: " + e.Message); }

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModCenterTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ModCenterTicker>();
        var go = new GameObject("ModCenter.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ModCenterTicker>();

        Log("loaded");
        Trace($"loaded v{Version}（配置桥：Meta 九表+快照/写/消息协议/Save 防抖；面板 UI 待任务 7）");
        SurvivalLog.ModShared.WelcomeTip.Install(Logger);
    }

    /// <summary>静态日志门：全部面板/桥接代码的统一输出口。</summary>
    public static new void Log(string msg) => Logger?.LogMessage("[ModCenter] " + msg);

    /// <summary>
    /// 自证文件（硬规则 9：不依赖 LogOutput.log，强杀不落盘）。落在游戏 BepInEx 根目录
    /// （本机即 E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt），
    /// 用 Paths.BepInExRootPath 而非写死盘符——本 mod 随套件发布到其他玩家机器也要能写。
    /// </summary>
    internal static void Trace(string line)
    {
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_loaded.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n");
        }
        catch { }
    }
}

/// <summary>
/// 宿主心跳：每帧跑配置桥泵（任务 6：outbox 消息分发 + Save 防抖计时，主线程），每 2s（unscaled，
/// 主菜单/暂停均照走）跑注入巡检（任务 5：WebBridge.GetWebView → MenuEntryInjector.Tick：
/// 入口+面板幂等安装 + 面板 outbox 轮询）。另挂任务 4 反射桥探针泵（ProbePump，Debug.Probe 门控）。
/// </summary>
internal sealed class ModCenterTicker : MonoBehaviour
{
    public ModCenterTicker(IntPtr ptr) : base(ptr) { }
    public ModCenterTicker() { }

    private float _acc;
    private float _kbAcc; // 任务 8：键盘门巡检节拍（0.3s，unscaled——暂停菜单打开也要照走）
    private bool _residueSoftDone; // 任务 16：残留软停重放只跑一次（首帧=Chainloader 全插件就绪后）

    // 任务 4：退出钩子只剩落盘——停用 dll 改名已迁启动清理器（MCCleaner；v1 退出分离进程机械退役）。
    // 落盘仍关键：[DisabledTp] 意图必须上盘，下次启动清理器才能读到（MC_CLOSE 已即时 Flush，
    // 此处兜底防抖残留）。强杀（taskkill/断电）漏过本钩也一样成立——只要意图已落盘
    // （面板关闭即 Flush），下次启动清理器先于加载执行改名。
    private static bool _quitDone; // 双钩子（MonoBehaviour 消息 + Application.quitting）幂等守卫
    internal static void RunQuitHooks()
    {
        if (_quitDone) return;
        _quitDone = true;
        try { ConfigBridge.FlushDirty(); } catch (Exception e) { ModCenterPlugin.Log("warn quit 落盘异常: " + e.Message); }
    }

    private void OnApplicationQuit() => RunQuitHooks();

    // 探针状态机：-1 未启用/已结束；0 等 30s（A 枚举+读+写）；1 等 5s（B 验表+写回）；2 等 3s（C 终验+Save）
    private int _probePhase;
    private float _probeAge;

    // v2 任务 5：网络探针状态机：-1 未启用/已收割；0 等 10s；1 后台任务在跑等收割（Ticker 主线程只查旗）
    private int _netPhase;
    private float _netAge;

    // v2 任务 6：决策层探针状态机（同款三拍）：-1 未启用/已收割；0 等 10s；1 后台任务在跑等收割
    private int _repoPhase;
    private float _repoAge;

    // v2 任务 12：L2 补丁碰撞探针状态机：-1 未启用/已收割；0 等 10s；1 主线程 dump 完等收割
    private int _patchPhase;
    private float _patchAge;

    public void Update()
    {
        ProbePump();
        NetProbePump();
        RepoProbePump();
        PatchScanPump();

        if (!_residueSoftDone)
        {
            _residueSoftDone = true;
            try { DllDisabler.ReapplySoft(); } // 任务 16：残留 Tier 2 软停重放（首帧=全插件已 Load）
            catch (Exception e) { ModCenterPlugin.Log("warn residue 软停重放异常: " + e.Message); }
        }

        // 任务 6：配置桥泵（面板消息分发 + Save 防抖计时）——每帧、主线程（cfg 读写线程纪律，DESIGN §4.2）
        ConfigBridge.Pump(Time.unscaledDeltaTime);

        // 任务 8：键盘门巡检（PlanPick FocusTick 同款 0.3s 快通道；MC_OPEN/MC_CLOSE 消息沿
        // 由 Pump 置位，本口只做沿触发交接/还原——SetKeyboardEnabled + IME，异常只 Warn）
        _kbAcc += Time.unscaledDeltaTime;
        if (_kbAcc >= 0.3f)
        {
            _kbAcc = 0f;
            try { FocusTick.Tick(); }
            catch (Exception e) { ModCenterPlugin.Log("warn 键盘门巡检异常: " + e.Message); }
        }

        _acc += Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;

        // 任务 5：注入巡检（入口+面板幂等自愈）+ outbox 轮询；WebView 未就绪（主菜单前/重建中）静默跳过本轮
        try
        {
            var web = WebBridge.GetWebView();
            if (web != null)
                MenuEntryInjector.Tick(web);
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log("warn 注入心跳异常: " + e.Message);
        }
    }

    /// <summary>探针泵：全部阶段都在本 Update（Unity 主线程）执行，与 RobotSpeed Ticker 同线程上下文。</summary>
    private void ProbePump()
    {
        if (_probePhase < 0) return;
        var gate = ModCenterPlugin.DebugProbe;
        if (gate == null || !gate.Value) { _probePhase = -1; return; }

        _probeAge += Time.unscaledDeltaTime;
        switch (_probePhase)
        {
            case 0 when _probeAge >= 30f:
                _probeAge = 0f; BridgeProbe.PhaseA(); _probePhase = 1; break;
            case 1 when _probeAge >= 5f:
                _probeAge = 0f; BridgeProbe.PhaseB(); _probePhase = 2; break;
            case 2 when _probeAge >= 3f:
                BridgeProbe.PhaseC(); _probePhase = -1; break;
        }
    }

    /// <summary>
    /// 网络探针泵（v2 任务 5）：等 10s 起 NetProbe（主线程只清证据文件+拉后台 Task——HttpClient
    /// 调用全部后台线程，裁定 1），之后每帧查完成旗收割打印（ConfigBridge.Pump 同款"后台写共享
    /// 状态+证据文件，Ticker 主线程收割"模式）。一轮即终态（-1），探针默认关。
    /// </summary>
    private void NetProbePump()
    {
        if (_netPhase < 0) return;
        var gate = ModCenterPlugin.DebugNetSelfTest;
        if (gate == null || !gate.Value) { _netPhase = -1; return; }

        switch (_netPhase)
        {
            case 0:
                _netAge += Time.unscaledDeltaTime;
                if (_netAge < 10f) return;
                _netPhase = 1;
                NetProbe.Start();
                break;
            case 1:
                if (NetProbe.Done)
                {
                    ModCenterPlugin.Log("netspeed 探针收割: " + NetProbe.Summary);
                    _netPhase = -1;
                }
                break;
        }
    }

    /// <summary>
    /// 决策层探针泵（v2 任务 6，NetProbePump 同款三拍）：等 10s 起 RepoProbe——主线程段跑
    /// gamekb 通道候选 dump（interop 候选须主线程）+ 本地插件快照（IL2CPPChainloader 主线程），
    /// 再拉后台 Task 做 12 仓 fetch + 三档决策自证；Done 旗由本泵收割打印。一轮即终态。
    /// </summary>
    private void RepoProbePump()
    {
        if (_repoPhase < 0) return;
        var gate = ModCenterPlugin.DebugRepoSelfTest;
        if (gate == null || !gate.Value) { _repoPhase = -1; return; }

        switch (_repoPhase)
        {
            case 0:
                _repoAge += Time.unscaledDeltaTime;
                if (_repoAge < 10f) return;
                _repoPhase = 1;
                RepoProbe.Start();
                break;
            case 1:
                if (RepoProbe.Done)
                {
                    ModCenterPlugin.Log("reposelftest 探针收割: " + RepoProbe.Summary);
                    _repoPhase = -1;
                }
                break;
        }
    }

    /// <summary>
    /// L2 补丁碰撞探针泵（v2 任务 12，NetProbePump 同款）：等 10s 起 PatchScanProbe——Harmony 账本
    /// 全部主线程读（裁定 1），dump 与 dry-run 同步跑完置 Done 旗，本泵收割打印。一轮即终态。
    /// </summary>
    private void PatchScanPump()
    {
        if (_patchPhase < 0) return;
        var gate = ModCenterPlugin.DebugPatchScan;
        if (gate == null || !gate.Value) { _patchPhase = -1; return; }

        switch (_patchPhase)
        {
            case 0:
                _patchAge += Time.unscaledDeltaTime;
                if (_patchAge < 10f) return;
                _patchPhase = 1;
                PatchScanProbe.Start();
                break;
            case 1:
                if (PatchScanProbe.Done)
                {
                    ModCenterPlugin.Log("patchscan 探针收割: " + PatchScanProbe.Summary);
                    _patchPhase = -1;
                }
                break;
        }
    }
}

/// <summary>
/// 任务 4 反射桥 API 实证探针（cfg Debug.Probe 门控，一轮三拍：A 写 3 → B 验表/写回 → C 终验落盘）。
/// 靶：RobotSpeed『机器人提速/移速倍率』（float，默认 2）。表当前值 MoveSpeed=4.8（三机型 2003/2004/2005）
/// = 真原值 2.4 × 现倍率 2（DESIGN §4.3-3：gamekb 的 4.8 是套件生效中 dump，勿当原始基线）；
/// 探针写 3→表 7.2（2.4×3）；写回 2→表回 4.8。全部证据追加写 BepInEx/ModCenter_probe.txt。
/// 确切可用 API/类型转换/线程上下文/踩坑结论固化于 DESIGN.md「反射桥 API 实证记录」，任务 6 逐字消费。
/// </summary>
internal static class BridgeProbe
{
    private const string TargetGuid = "com.local.survivallog.robotspeed";
    private const string Section = "机器人提速";
    private const string Key = "移速倍率";

    private static ConfigFile _cfg;
    private static ConfigEntry<float> _entry;
    private static float _orig;

    private static string ProbePath => System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_probe.txt");

    private static void P(string line)
    {
        try { System.IO.File.AppendAllText(ProbePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        ModCenterPlugin.Log("probe " + line);
    }

    /// <summary>A（Load 后 30s）：枚举 Chainloader 插件 → 取 RobotSpeed ConfigFile → 读原值 → 写 3f。</summary>
    public static void PhaseA()
    {
        try
        {
            try { if (System.IO.File.Exists(ProbePath)) System.IO.File.Delete(ProbePath); } catch { }

            // 第一环：本机构 BepInEx（IL2CPP BE 构型）无静态 Chainloader.PluginInfos，
            // 实例字典在 BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins（Dictionary<string, PluginInfo>）
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            if (plugins == null) { P("A FAIL：IL2CPPChainloader.Instance/Plugins 为 null"); return; }
            P($"A 枚举到 {plugins.Count} 个插件: {string.Join(", ", plugins.Values.Select(p => p.Metadata.GUID))}");

            var info = plugins.Values.FirstOrDefault(p => p.Metadata.GUID == TargetGuid);
            if (!(info?.Instance is BasePlugin bp)) { P("A FAIL：RobotSpeed 实例未找到或非 BasePlugin"); return; }

            // 第二环：BasePlugin.Config 是 public getter 的公共属性，强类型直取、零反射
            _cfg = bp.Config;

            // 第三环：泛型 TryGetEntry(section, key, out ConfigEntry<T>)（T 必须与 Bind 时一致，float）
            if (!_cfg.TryGetEntry(Section, Key, out _entry)) { P($"A FAIL：TryGetEntry({Section}/{Key}) 未命中"); return; }

            _orig = _entry.Value;
            P($"A entry 读原值 Value={_orig:0.###}（期望 2）；SaveOnConfigSet={_cfg.SaveOnConfigSet}");
            P($"A 写前表快照: {Table()}");

            // 第四环：typed Value 写 → ConfigFile.SettingChanged → RobotSpeed.Rerun=true（0.5s 内重写表）
            _entry.Value = 3f;
            P("A 已写 Value=3（期望 RobotSpeed Ticker ≤0.5s 把表 MoveSpeed 重写为 2.4×3=7.2）");
        }
        catch (Exception e) { P("A 异常: " + e); }
    }

    /// <summary>B（写 3 后 5s）：验 entry 与内存表已被目标 mod 热重写，随后写回原值。</summary>
    public static void PhaseB()
    {
        if (_entry == null) { P("B SKIP：A 阶段未取得 entry"); return; }
        try
        {
            P($"B 5s 后 entry.Value={_entry.Value:0.###}（期望 3）");
            P($"B 5s 后表快照: {Table()}（期望三行 7.2 = 热重写实证）");
            _entry.Value = _orig;
            P($"B 已写回原值 {_orig:0.###}（期望 Ticker ≤0.5s 内表回 2.4×2=4.8）");
        }
        catch (Exception e) { P("B 异常: " + e); }
    }

    /// <summary>C（写回后 3s）：终验 entry/表已还原，主动 Save 落盘防脏值残留。</summary>
    public static void PhaseC()
    {
        if (_entry == null) { P("C SKIP：A 阶段未取得 entry"); return; }
        try
        {
            P($"C 终验 entry.Value={_entry.Value:0.###}（期望 2）");
            P($"C 终验表快照: {Table()}（期望三行 4.8 已还原）");
            _cfg.Save(); // SaveOnConfigSet=true 时每写即存，此处兜底确保盘上=原值
            P("C ConfigFile.Save 已调；探针 DONE");
        }
        catch (Exception e) { P("C 异常: " + e); }
    }

    /// <summary>读内存表 Config_RobotAttribute 全行 MoveSpeed（读法与 RobotSpeedPlugin 现役代码一致；
    /// 任务 6 起供 ConfigBridge 装机证据复用，internal）。</summary>
    internal static string Table()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return "ConfigManager 未就绪";
            var cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
            var dict = cm._Config_RobotAttribute_Dict;
            if (dict == null || dict.Count == 0) return "Config_RobotAttribute 空";
            var sb = new StringBuilder();
            foreach (var kv in dict)
                sb.Append(kv.Key).Append('(').Append(kv.Value.Comment).Append(")MoveSpeed=")
                  .Append(kv.Value.MoveSpeed.ToString("0.###")).Append(' ');
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "表读异常: " + e.Message; }
    }
}

/// <summary>
/// v2 任务 5 网络自诊断探针（cfg Debug.NetSelfTest 门控，Ticker 10s 后拉起，一轮即终态）。
/// 靶：survivallog-mods/RobotSpeed（真实在线仓）。后台 Task 跑：fetch latest（版本/md5/gamekb
/// + 耗时）→ 下载 zip 到 StagingDir → MD5 对 update.json → **删 zip 不留残留**；失败场景
/// （如 MirrorPrefix 指向不可达地址）记录失败耗时（应秒级 &lt; CheckTimeout 上限）。
/// 证据追加 BepInEx/ModCenter_netspeed.txt（硬规则 9）；Ticker 主线程只收割 Done 旗打印。
/// </summary>
internal static class NetProbe
{
    private const string TargetRepo = "RobotSpeed";

    internal static volatile bool Done;
    internal static string Summary = "";

    private static string EvidencePath => System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_netspeed.txt");

    private static void P(string line)
    {
        try { System.IO.File.AppendAllText(EvidencePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        ModCenterPlugin.Log("netspeed " + line);
    }

    /// <summary>Ticker 主线程调：清旧证据 → 写头行 → 拉后台 Task（HTTP 全在后台，裁定 1）。</summary>
    public static void Start()
    {
        Done = false;
        Summary = "";
        try { if (System.IO.File.Exists(EvidencePath)) System.IO.File.Delete(EvidencePath); } catch { }
        var mirror = ModCenterPlugin.NetworkMirrorPrefix?.Value ?? "";
        var check = ModCenterPlugin.NetworkCheckTimeoutMs?.Value ?? 5000;
        var dl = ModCenterPlugin.NetworkDownloadTimeoutMs?.Value ?? 60000;
        P($"probe start: target=survivallog-mods/{TargetRepo} mirror='{mirror}' checkTimeout={check}ms downloadTimeout={dl}ms");
        System.Threading.Tasks.Task.Run(RunAsync);
    }

    private static async System.Threading.Tasks.Task RunAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // ① fetch：latest → update.json → 五字段（失败=null 秒级返回，不抛）
            var info = await RepoClient.FetchLatest(TargetRepo);
            var fetchMs = watch.ElapsedMilliseconds;
            if (info == null)
            {
                P($"FAIL fetch=null（网络/解析/资产缺失降级）耗时 {fetchMs}ms");
                Summary = $"fetch FAIL {fetchMs}ms（探针一轮终态）";
                return;
            }
            P($"fetch OK {fetchMs}ms: mod={info.Mod} version={info.Version} md5={info.Md5} gamekb={info.Gamekb}");
            P($"fetch zip 资产: {info.Asset} ← {info.AssetUrl}");

            // ② 下载到 staging 临时名（progress 25% 台阶落一行证据）
            var lastStep = 0;
            var path = await RepoClient.DownloadAsset(info, (done, total) =>
            {
                if (total <= 0) return;
                var step = (int)(done * 100 / total) / 25;
                if (step <= lastStep) return;
                lastStep = step;
                P($"download 进度 {done}/{total}B ({done * 100 / total}%)");
            });
            var dlMs = watch.ElapsedMilliseconds - fetchMs;
            var size = new System.IO.FileInfo(path).Length;

            // ③ MD5 对 update.json（下载完整性实证）
            var md5 = RepoClient.Md5Of(path);
            var match = string.Equals(md5, info.Md5, StringComparison.OrdinalIgnoreCase);
            P($"download OK {dlMs}ms {size}B md5={md5} match={(match ? "PASS" : "MISMATCH 期望 " + info.Md5)}");

            // ④ 测完即删（staging 不留测试残留）
            System.IO.File.Delete(path);
            P($"staging 清理: {System.IO.Path.GetFileName(path)} 已删");

            Summary = match
                ? $"OK fetch {fetchMs}ms + download {dlMs}ms {size}B md5 PASS（zip 已清）"
                : $"FAIL md5 MISMATCH（zip 已清）";
        }
        catch (Exception e)
        {
            P($"FAIL {e.GetType().Name}: {e.Message} @{watch.ElapsedMilliseconds}ms");
            Summary = $"FAIL {e.GetType().Name} {watch.ElapsedMilliseconds}ms（探针一轮终态）";
        }
        finally { Done = true; }
    }
}

/// <summary>
/// v2 任务 6 仓库决策层自诊断探针（cfg Debug.RepoSelfTest 门控，Ticker 10s 后拉起，一轮即终态）。
/// 主线程段：gamekb 通道四候选 dump（GameVersion.ProbeRead → ModCenter_gamever.txt）+ 12 仓本地
/// 插件快照（SnapshotLocal）。后台段（HTTP 全在后台，裁定 1）：一轮 12 仓 fetch（顺序温和，
/// 匿名限流预算内）→ 三档决策自证——A 真仓真基线 / B 强制通道失败（D8 Step 4：不误报 Warn +
/// "未校验"标注）/ C 合成分支自证（五态各至少一行，走同一 Build 代码路径，不碰本机枚举与网络）
/// → D PendingWriter 合并语义对临时文件自证（真 pending.json 绝不写入假意图）。证据
/// BepInEx/ModCenter_reposelftest.txt（硬规则 9）；Ticker 主线程只收割 Done 旗打印。
/// </summary>
internal static class RepoProbe
{
    internal static volatile bool Done;
    internal static string Summary = "";

    private static GameVersion.Local _local;   // 主线程读好，后台只读
    private static List<RepoRow> _rows;        // 主线程构造（SnapshotLocal），后台附 Remote/Gamekb

    private static string EvidencePath => System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_reposelftest.txt");

    private static void P(string line)
    {
        try { System.IO.File.AppendAllText(EvidencePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        ModCenterPlugin.Log("reposelftest " + line);
    }

    /// <summary>Ticker 主线程调：清旧证据 → 写头行 → gamekb 通道探针（主线程 interop）→ 本地快照 → 拉后台 Task。</summary>
    public static void Start()
    {
        Done = false;
        Summary = "";
        try { if (System.IO.File.Exists(EvidencePath)) System.IO.File.Delete(EvidencePath); } catch { }
        var mirror = ModCenterPlugin.NetworkMirrorPrefix?.Value ?? "";
        var check = ModCenterPlugin.NetworkCheckTimeoutMs?.Value ?? 5000;
        P($"probe start: {RepoDecision.RepoKeys.Length} 仓 mirror='{mirror}' checkTimeout={check}ms（一轮 12 次 API 请求）");

        // ① gamekb 通道探针（主线程：interop 候选）；证据双写 gamever.txt + 本文件摘要
        _local = GameVersion.ProbeRead(line => P("gamever | " + line));
        P($"本地基线: baseline='{_local.Baseline}' display='{_local.Display}' channel='{_local.Channel}' note='{_local.Note}'");

        // ② 本地插件快照（主线程：IL2CPPChainloader）
        _rows = RepoDecision.SnapshotLocal();
        foreach (var r in _rows)
            P($"local {r.RepoKey}: {(r.Local ?? "未安装")} (guid={r.Guid})");

        System.Threading.Tasks.Task.Run(RunAsync);
    }

    private static async System.Threading.Tasks.Task RunAsync()
    {
        var pass = true;
        try
        {
            // ---- fetch 轮（一轮 12 仓，顺序执行——温和限流，不连环多轮）----
            var fetchFail = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var r in _rows)
            {
                var info = await RepoClient.FetchLatest(r.RepoKey);
                if (info == null)
                {
                    r.Remote = null; r.Gamekb = null; fetchFail++;
                    P($"fetch FAIL {r.RepoKey}（warn 已留 LogOutput；决策降级 Remote=null 不虚报）");
                }
                else
                {
                    r.Remote = info.Version; r.Gamekb = info.Gamekb;
                    P($"fetch OK {r.RepoKey}: remote={info.Version} gamekb={info.Gamekb}");
                }
            }
            P($"fetch 轮完: {watch.ElapsedMilliseconds}ms 12 仓 fail={fetchFail}");

            // ---- A 真仓真基线 ----
            P("--- A 真仓真基线（本机 12 行） ---");
            RepoDecision.Build(_rows, _local);
            foreach (var r in _rows) P(RowLine(r));
            var dist = Distribution(_rows);
            P($"A 分布: {dist}");

            // ---- B 通道失败降级（Step 4：强制 GameVersion 返 null → 不误报 Warn + "未校验"标注）----
            P("--- B 通道失败降级（基线强制 null，D8 Step 4） ---");
            var unverified = GameVersion.Local.Unverified("探针强制通道失败（Step 4 降级实测）");
            RepoDecision.Build(_rows, unverified);
            foreach (var r in _rows) P(RowLine(r));
            var bWarn = _rows.Count(r => r.State == RepoState.Warn);
            var bNotes = _rows.Count(r => !string.IsNullOrEmpty(r.VerNote));
            var bFetchOk = _rows.Count(r => !string.IsNullOrEmpty(r.Remote) && !string.IsNullOrEmpty(r.Gamekb));
            var bPass = bWarn == 0 && bNotes == bFetchOk && bFetchOk > 0;
            P($"B 断言: warn={bWarn}（期望 0 不误报） 未校验标注={bNotes} 行（期望 = fetch 成功且带 gamekb 的 {bFetchOk} 行） → {(bPass ? "PASS" : "FAIL")}");
            pass &= bPass;

            // ---- C 合成分支自证（合成输入走同一 Build 路径；不碰本机枚举/网络）----
            P("--- C 分支自证（合成输入，非本机枚举/非网络；五态各至少一行） ---");
            var gk = _rows.Find(x => !string.IsNullOrEmpty(x.Gamekb))?.Gamekb ?? "2026-10-23_26157732";
            var gkBad = "2000-01-01_1"; // 与任何真 buildid 都不等的合成快照名
            var cases = new List<SynthCase>
            {
                Synth("未装+基线匹配",         null,    "1.7.0", gk,     RepoState.Install),
                Synth("旧版+基线匹配",         "1.0.0", "1.7.0", gk,     RepoState.Update),
                Synth("同版+基线匹配",         "1.7.0", "1.7.0", gk,     RepoState.Ok),
                Synth("本地高（D7）",          "9.9.9", "1.7.0", gk,     RepoState.Downgrade),
                Synth("未装+基线不匹配（D8）",  null,    "1.7.0", gkBad,  RepoState.Warn),
                Synth("旧版+基线不匹配（D8）",  "1.0.0", "1.7.0", gkBad,  RepoState.Warn),
                Synth("快照名形态异常",         "1.0.0", "1.7.0", "2000-01-01_pre1", RepoState.Warn),
                Synth("fetch 失败路径",        "1.7.0", null,    null,   RepoState.Ok),
            };
            // 基线不可用时期望翻转：不匹配两行不再 Warn（保持 Install/Update + 未校验标注）
            var cPass = true;
            if (_local.Baseline != null)
            {
                RepoDecision.Build(cases.ConvertAll(c => c.Row), _local);
                cPass &= AssertRows(cases);
            }
            else
            {
                foreach (var c in cases)
                {
                    if (c.Expected == RepoState.Warn)
                        c.Expected = c.Row.Local == null ? RepoState.Install : RepoState.Update; // 通道不可用：不判 Warn
                }
                RepoDecision.Build(cases.ConvertAll(c => c.Row), unverified);
                cPass &= AssertRows(cases);
            }
            // 追加：基线不可用专项（Install/Update + 未校验标注，不误报 Warn）
            var cases2 = new List<SynthCase>
            {
                Synth("未装+不匹配+通道失败", null,    "1.7.0", gkBad, RepoState.Install),
                Synth("旧版+不匹配+通道失败", "1.0.0", "1.7.0", gkBad, RepoState.Update),
            };
            RepoDecision.Build(cases2.ConvertAll(c => c.Row), unverified);
            foreach (var c in cases2)
            {
                var ok = c.Row.State == c.Expected && string.IsNullOrEmpty(c.Row.Why) && !string.IsNullOrEmpty(c.Row.VerNote);
                P($"{SynthLine(c)} → 期望 {c.Expected}+未校验标注 {(ok ? "PASS" : "FAIL")}");
                cPass &= ok;
            }
            var cRows = cases.ConvertAll(c => c.Row);
            var cDist = Distribution(cRows);
            var fiveOk = CountState(cRows, RepoState.Ok) >= 1 && CountState(cRows, RepoState.Update) >= 1 &&
                         CountState(cRows, RepoState.Downgrade) >= 1 && CountState(cRows, RepoState.Warn) >= 1 &&
                         CountState(cRows, RepoState.Install) >= 1;
            P($"C 五态覆盖: {cDist} → {(fiveOk ? "PASS" : "FAIL（基线不可用机器上期望翻转，见上）")}");
            cPass &= _local.Baseline != null && fiveOk;
            pass &= cPass;

            // ---- D PendingWriter 合并语义（临时文件；真 pending.json 绝不写入假意图）----
            P("--- D PendingWriter 合并语义（临时文件自证，验后即删） ---");
            var tmp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(McPending.File), "selftest-pending.json");
            var dOk = false;
            try
            {
                try { System.IO.File.Delete(tmp); } catch { }
                var ops1 = new List<McPending.Op>
                {
                    new() { Type = McPending.TypeDelete,  Target = "plugins/a.dll" },
                    new() { Type = McPending.TypeReplace, Target = "plugins/b.dll", Staged = "ModCenter/staging/b.zip", Md5 = "00" },
                };
                McPending.Save(tmp, ops1, new List<string> { "g1", "g2" }); // 旧清单（他人 ops + T3 order）
                PendingWriter.AppendOps(tmp, new List<McPending.Op>
                {
                    new() { Type = McPending.TypeInstall, Target = "plugins/c.dll", Staged = "ModCenter/staging/c.zip", Md5 = "01" },
                });
                var back = McPending.Load(tmp);
                var order = McPending.LoadOrder(tmp);
                dOk = back.Count == 3
                      && back[0].Target == "plugins/a.dll" && back[1].Target == "plugins/b.dll" // 旧在前
                      && back[2].Target == "plugins/c.dll"                                     // 新在后
                      && back[2].Type == McPending.TypeInstall && back[2].Md5 == "01"
                      && order.Count == 2 && order[0] == "g1";                                  // order 不丢
                P($"D 断言: 追加后 ops={back.Count} 条（旧2前新1后） order={order.Count} 条（g1,g2 保留） → {(dOk ? "PASS" : "FAIL")}");
                pass &= dOk;
            }
            finally
            {
                try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { } // 痕迹清
                P("D 清理: selftest-pending.json 已删（真 pending.json 全程零触碰）");
            }

            Summary = $"DONE A[{dist}] fetchFail={fetchFail} | B 通道降级 {(bPass ? "PASS" : "FAIL")}（warn=0 未校验={bNotes}行） | " +
                      $"C 五态分支 {(cPass ? "PASS" : "FAIL")} | D 合并语义 {(dOk ? "PASS" : "FAIL")}（探针一轮终态）";
        }
        catch (Exception e)
        {
            P($"FAIL {e.GetType().Name}: {e.Message}");
            Summary = $"FAIL {e.GetType().Name} {e.Message}（探针一轮终态）";
        }
        finally { Done = true; }
    }

    /// <summary>合成用例（C 档专用）：公共 RepoRow + 用例名/期望态（RepoRow 冻结密封， wrapper 旁挂）。</summary>
    private sealed class SynthCase
    {
        public RepoRow Row;
        public string Name;
        public RepoState Expected;
    }

    private static SynthCase Synth(string caseName, string local, string remote, string gamekb, RepoState expected) => new()
    {
        Name = caseName,
        Row = new RepoRow
        {
            RepoKey = "Synth", Name = "Synth", Guid = "com.local.survivallog.synth",
            Local = local, Remote = remote, Gamekb = gamekb,
        },
        Expected = expected,
    };

    private static bool AssertRows(List<SynthCase> cases)
    {
        var ok = true;
        foreach (var c in cases)
        {
            var good = c.Row.State == c.Expected;
            P($"{SynthLine(c)} → 期望 {c.Expected} {(good ? "PASS" : "FAIL")}");
            ok &= good;
        }
        return ok;
    }

    private static string SynthLine(SynthCase c)
    {
        var r = c.Row;
        return $"C [{c.Name}] local='{r.Local ?? "null"}' remote='{r.Remote ?? "null"}' gamekb='{r.Gamekb ?? "null"}' → state={r.Status} why='{r.Why}' vernote='{r.VerNote}'";
    }

    private static string RowLine(RepoRow r) =>
        $"A/B {r.RepoKey}: local={r.Local ?? "未装"} remote={r.Remote ?? "fetch失败"} gamekb={r.Gamekb ?? "-"} → {r.Status}" +
        (string.IsNullOrEmpty(r.Why) ? "" : $" why='{r.Why}'") +
        (string.IsNullOrEmpty(r.VerNote) ? "" : $" vernote='{r.VerNote}'");

    private static string Distribution(List<RepoRow> rows) =>
        $"ok={CountState(rows, RepoState.Ok)} update={CountState(rows, RepoState.Update)} " +
        $"downgrade={CountState(rows, RepoState.Downgrade)} warn={CountState(rows, RepoState.Warn)} " +
        $"install={CountState(rows, RepoState.Install)}";

    private static int CountState(List<RepoRow> rows, RepoState s)
    {
        var n = 0;
        foreach (var r in rows)
            if (r.State == s) n++;
        return n;
    }
}
