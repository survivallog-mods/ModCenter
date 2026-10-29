using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;

namespace MCShell;

/// <summary>
/// MC 壳（ModCenter v2 生命周期组件一，T16 单 dll 化）：**永久冻结**的 BepInEx patcher 引导壳。
/// 不承载任何清理逻辑（全部在 <c>plugins\SurvivalLog.ModCenter.dll</c> 的 CleanupRunner 内）——
/// 清理逻辑随 ModCenter 本体发版，游戏内更新 ModCenter 即更新清理逻辑，版本线只有一条。
///
/// 本壳的接口（改动这些 = 必须随安装器更新壳）：
/// 1. <c>BepInEx\ModCenter\uninstalled.flag</c>（墓碑）：boot N 由 CleanupRunner 写；boot N+1
///    本壳在加载前删 <c>SurvivalLog.ModCenter.dll</c> + 自身 cfg + <c>BepInEx\ModCenter\</c>
///    目录（含 flag），**不加载**本体后退出——物理原因：ModCenter.dll 在本靴被本壳 LoadFrom 后
///    被 CLR 锁定，无法自删，必须推迟到"本壳尚未加载它"的下一靴。
/// 2. <c>BepInEx\ModCenter\selfupdate.flag</c>（自更新预置）：面板更新 ModCenter 时写
///    （staged 绝对路径 / staged md5 / target 绝对路径，三行）；boot N+1 本壳在 LoadFrom
///    **之前**替换本体，当靴即加载新程序集（同程序集身份不允许同靴内被 Chainloader 重新
///    LoadFrom，故自更新不能由 CleanupRunner 本靴落盘）。缺失/损坏 = 忽略并留证据。
/// 3. <c>SurvivalLog.ModCenter.CleanupRunner.Run()</c>：public static void，无参数，无返回值。
///
/// 任何异常全部自吞并写证据 <c>BepInEx\MCShell_loaded.txt</c>，绝不阻断游戏启动。
/// 版本号 1.0.0 只是 [PatcherPluginInfo] 的占位值，**不是独立版本线**（壳永不变更）。
/// </summary>
[PatcherPluginInfo("com.local.survivallog.mcshell", "MC Shell", "1.0.0")]
public class ShellPatcher : BasePatcher
{
    private const string PluginDllName = "SurvivalLog.ModCenter.dll";
    private const string PluginGuidCfgName = "com.local.survivallog.modcenter.cfg";
    private const string RunnerTypeName = "SurvivalLog.ModCenter.CleanupRunner";

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static ManualLogSource _log;

    private static string EvidencePath =>
        Path.Combine(Paths.BepInExRootPath, "MCShell_loaded.txt");

    private static string UninstalledFlagPath =>
        Path.Combine(Paths.BepInExRootPath, "ModCenter", "uninstalled.flag");

    private static string SelfUpdateFlagPath =>
        Path.Combine(Paths.BepInExRootPath, "ModCenter", "selfupdate.flag");

    private static string PluginRoot =>
        string.IsNullOrEmpty(Paths.PluginPath)
            ? Path.Combine(Paths.BepInExRootPath, "plugins") : Paths.PluginPath;

    private static string ConfigDir
    {
        get
        {
            var d = Paths.ConfigPath;
            return string.IsNullOrEmpty(d) || !Directory.Exists(d)
                ? Path.Combine(Paths.BepInExRootPath, "config") : d;
        }
    }

    private static void Ev(string line)
    {
        try
        {
            File.AppendAllText(EvidencePath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " [SHELL] " + line + "\n", Utf8NoBom);
        }
        catch { }
    }

    /// <summary>LogOutput 可见性（证据文件是硬规则；日志只辅助排障，失败忽略）。</summary>
    private static void L(string msg)
    {
        try
        {
            _log ??= Logger.CreateLogSource("MCShell");
            _log.LogInfo(msg);
        }
        catch { }
    }

    public override void Initialize()
    {
        Ev("Initialize reached, dll=" + typeof(ShellPatcher).Assembly.Location);
        L("Initialize reached");

        // 1) 墓碑分支优先：卸载完成靴——删本体+cfg+目录，不加载 ModCenter.dll。
        try
        {
            if (File.Exists(UninstalledFlagPath)) { HandleSelfUninstall(); return; }
        }
        catch (Exception e)
        {
            Ev("[SELFUNINST] FAIL " + e.GetType().Name + ": " + e.Message);
            L("self-uninstall FAIL: " + e.Message);
            return; // 不加载：用户意图=卸载；残件由下一靴墓碑重试
        }

        // 2) 自更新预置（可选）：在 LoadFrom 之前替换本体，保证本靴加载新程序集。
        try
        {
            if (File.Exists(SelfUpdateFlagPath)) ApplySelfUpdate();
        }
        catch (Exception e)
        {
            Ev("[SELFUPDATE] FAIL " + e.GetType().Name + ": " + e.Message);
            L("self-update FAIL: " + e.Message); // 失败继续加载旧 dll（游戏照常启动）
        }

        // 3) 定位本体 → LoadFrom → 反射调 CleanupRunner.Run()
        try
        {
            var dll = LocatePluginDll();
            if (dll == null) { Ev("[LOAD] SKIP 未找到 " + PluginDllName + "（未安装/已禁用）"); L("ModCenter.dll not found"); }
            else RunCleanup(dll);
        }
        catch (Exception e)
        {
            Ev("[LOAD] FAIL " + e.GetType().Name + ": " + e.Message);
            L("cleanup runner FAIL: " + e.Message);
        }
    }

    /// <summary>plugins 根 + 递归兜底；只认裸 dll（.disabled = 玩家停用，不加载）。</summary>
    private static string LocatePluginDll()
    {
        var root = PluginRoot;
        var exact = Path.Combine(root, PluginDllName);
        if (File.Exists(exact)) return exact;
        if (!Directory.Exists(root)) return null;
        try
        {
            foreach (var f in Directory.GetFiles(root, PluginDllName, SearchOption.AllDirectories))
                if (Path.GetFileName(f).Equals(PluginDllName, StringComparison.OrdinalIgnoreCase))
                    return f;
        }
        catch { }
        return null;
    }

    private static void RunCleanup(string dllPath)
    {
        var asm = Assembly.LoadFrom(dllPath);
        var t = asm.GetType(RunnerTypeName, false);
        if (t == null) { Ev("[LOAD] FAIL 类型未找到 " + RunnerTypeName + "（旧版 ModCenter？）"); return; }
        var m = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        if (m == null) { Ev("[LOAD] FAIL 方法未找到 " + RunnerTypeName + ".Run"); return; }
        m.Invoke(null, null);
        Ev("[LOAD] CleanupRunner.Run OK v" + asm.GetName().Version + " dll=" + dllPath);
        L("CleanupRunner.Run OK v" + asm.GetName().Version);
    }

    /// <summary>
    /// 墓碑靴：删 plugins（递归）下的裸 dll 与 .disabled → 删自身 cfg → 清 BepInEx\ModCenter\
    /// 目录（含 flag）。删 dll 失败（非预期：本靴未 LoadFrom）则保留 flag/目录待下一靴重试。
    /// </summary>
    private static void HandleSelfUninstall()
    {
        string stamp = "";
        string cfgFromFlag = "";
        try
        {
            var lines = File.ReadAllLines(UninstalledFlagPath);
            if (lines.Length > 0) stamp = lines[0].Trim();
            if (lines.Length > 1) cfgFromFlag = lines[1].Trim();
        }
        catch { }

        var removed = 0;
        var failed = 0;
        try
        {
            if (Directory.Exists(PluginRoot))
                foreach (var f in Directory.GetFiles(PluginRoot, PluginDllName + "*", SearchOption.AllDirectories))
                {
                    var n = Path.GetFileName(f);
                    if (!n.Equals(PluginDllName, StringComparison.OrdinalIgnoreCase)
                        && !n.Equals(PluginDllName + ".disabled", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(f); removed++; }
                    catch (Exception e) { failed++; Ev("[SELFUNINST] delete FAIL " + f + ": " + e.Message); }
                }
        }
        catch (Exception e) { Ev("[SELFUNINST] scan FAIL: " + e.Message); failed++; }

        if (failed > 0)
        {
            Ev("[SELFUNINST] boot-N+1 ABORT 本体删除失败 " + failed + " 处（保留墓碑与目录，下一靴重试）");
            L("self-uninstall aborted: dll delete failed");
            return;
        }

        var cfgDeleted = 0;
        var beRoot = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd('\\') + "\\";
        foreach (var cfg in new[]
                 {
                     cfgFromFlag,
                     Path.Combine(ConfigDir, PluginGuidCfgName),
                 })
        {
            if (string.IsNullOrEmpty(cfg)) continue;
            // 墓碑可能被篡改/损坏：只删 BepInEx 根下、文件名为本插件 GUID cfg 的路径。
            try
            {
                var full = Path.GetFullPath(cfg);
                if (!full.StartsWith(beRoot, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(full).Equals(PluginGuidCfgName, StringComparison.OrdinalIgnoreCase))
                {
                    Ev("[SELFUNINST] SKIP cfg 路径不合法: " + cfg);
                    continue;
                }
                if (File.Exists(full)) { File.Delete(full); cfgDeleted++; }
            }
            catch (Exception e) { Ev("[SELFUNINST] cfg delete FAIL " + cfg + ": " + e.Message); }
        }

        var dir = Path.Combine(Paths.BepInExRootPath, "ModCenter");
        var dirGone = false;
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            dirGone = !Directory.Exists(dir);
        }
        catch (Exception e) { Ev("[SELFUNINST] dir delete FAIL " + dir + ": " + e.Message); }

        Ev("[SELFUNINST] boot-N+1 OK 墓碑(" + stamp + ") 完成：删本体 " + removed
           + " 个、cfg " + cfgDeleted + " 个、ModCenter 目录 " + (dirGone ? "已清" : "清理失败")
           + "；本靴不加载 ModCenter.dll（面板/入口消失）");
        L("self-uninstall done: dll=" + removed + " cfg=" + cfgDeleted + " dirGone=" + dirGone);
    }

    /// <summary>
    /// 自更新预置靴：读三行（staged / md5 / target）→ 校验（target 必须在 plugins 下且文件名正确、
    /// staged 存在且 md5 一致）→ 本体尚未被本壳加载，可直接 File.Replace/File.Move 落位 →
    /// 清 flag。失败保留 flag 留证据（游戏照常加载旧 dll）。
    /// </summary>
    private static void ApplySelfUpdate()
    {
        var lines = File.ReadAllLines(SelfUpdateFlagPath);
        if (lines.Length < 3)
        {
            Ev("[SELFUPDATE] SKIP 预置格式非法（" + lines.Length + " 行）——删除后按正常流程加载");
            try { File.Delete(SelfUpdateFlagPath); } catch { }
            return;
        }
        var staged = lines[0].Trim();
        var md5 = lines[1].Trim();
        var target = lines[2].Trim();

        var rootFull = Path.GetFullPath(PluginRoot).TrimEnd('\\') + "\\";
        string targetFull;
        try { targetFull = Path.GetFullPath(target); }
        catch { Ev("[SELFUPDATE] SKIP target 非法: " + target); return; }

        var nameOk = Path.GetFileName(targetFull).Equals(PluginDllName, StringComparison.OrdinalIgnoreCase);
        if (!nameOk || !targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            Ev("[SELFUPDATE] SKIP target 越界/文件名不符: " + target);
            try { File.Delete(SelfUpdateFlagPath); } catch { }
            return;
        }

        // 手工删除本体（或安装器已重装到别处）→ 尊重现状，不复活。
        if (!File.Exists(targetFull))
        {
            Ev("[SELFUPDATE] SKIP 本体不在场（手动删除/安装器变更）: " + targetFull);
            try { File.Delete(SelfUpdateFlagPath); } catch { }
            return;
        }

        if (!File.Exists(staged))
        {
            Ev("[SELFUPDATE] SKIP staged 不在场（保留预置待补）: " + staged);
            return;
        }

        string actual;
        try { actual = Md5Of(staged); }
        catch (Exception e) { Ev("[SELFUPDATE] SKIP staged 读取失败: " + e.Message); return; }
        if (!string.Equals(actual, md5, StringComparison.OrdinalIgnoreCase))
        {
            Ev("[SELFUPDATE] SKIP staged md5 不符（期望 " + md5 + " 实得 " + actual + "）——保留预置待重下");
            return;
        }

        // 已应用（安装器/上次壳已替换）→ 只清 flag。
        try
        {
            if (string.Equals(Md5Of(targetFull), md5, StringComparison.OrdinalIgnoreCase))
            {
                Ev("[SELFUPDATE] 已在场（target md5 一致，幂等）");
                File.Delete(SelfUpdateFlagPath);
                return;
            }
        }
        catch { }

        var dir = Path.GetDirectoryName(targetFull);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        try
        {
            // 本体本靴尚未 LoadFrom（本分支在 RunCleanup 之前）→ 无锁，可原子替换。
            File.Replace(staged, targetFull, null);
        }
        catch
        {
            // 同卷 File.Replace 失败（极少数文件系统/权限）→ 退化为覆盖复制 + 删 staged。
            File.Copy(staged, targetFull, true);
            try { File.Delete(staged); } catch { }
        }
        try { File.Delete(SelfUpdateFlagPath); } catch { }
        Ev("[SELFUPDATE] OK 加载前替换本体 " + targetFull + "（md5=" + md5 + "）→ 本靴即加载新程序集");
        L("self-update applied: " + targetFull);
    }

    private static string Md5Of(string path)
    {
        using var md5 = MD5.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(fs)).ToLowerInvariant();
    }
}
