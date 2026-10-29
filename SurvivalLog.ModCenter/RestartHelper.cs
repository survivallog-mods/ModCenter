using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 分离进程启动器 + 一键重启助手（T4 自 DllDisabler 迁入 <see cref="StartDetachedPS"/>；
/// v2 任务 13 实装 <see cref="Request"/>）。
///
/// <para>分离进程机械：脚本经 base64(UTF-16LE) 编码传 <c>-EncodedCommand</c>，彻底规避
/// 引号/反斜杠的命令行转义坑（脚本仍应保持 ASCII-only 纪律）；<c>UseShellExecute=false</c>
/// + <c>CreateNoWindow</c> 起隐藏窗口的 powershell 子进程，不等待、不属于任何 Job，
/// 父进程（游戏）退出后它继续存活——这正是"等本进程退出后再动手"的前提。</para>
///
/// <para>一键重启链（v2 任务 13，spec §4.1）：面板"立即重启"确认 → <see cref="Request"/> 拉起
/// 分离脚本 →（C# 侧延迟一拍让面板 toast 上屏）<see cref="QuitGame"/> 请求引擎退出 → 分离脚本
/// **按 PID 精确等本进程退出**（不用进程名：同机可能有别的 SurvivalLog 实例/残留）→
/// <see cref="SteamDelaySeconds"/> 秒延迟（仓库硬规则：Steam 对刚杀实例有延迟，taskkill 后
/// 需隔 8~10 秒）→ <c>Start-Process 'steam://rungameid/4164790'</c>（必须走 steam 协议，
/// 直接起 exe 会被 Steamworks 转拉导致插件哑火）。</para>
///
/// <para>诚实边界（brief T13-4）：分离进程启动成功 ≠ 重启成功。脚本等不到本进程退出
/// （上限 <see cref="WaitExitSeconds"/> 秒）时**直接放弃、不重拉**（避免双实例），且脚本自身
/// 不写任何证据文件（测试痕迹零残留）。</para>
/// </summary>
internal static class RestartHelper
{
    /// <summary>Steam App ID（与 <see cref="GameVersion.SteamAppId"/> 同源，勿另写字面量）。</summary>
    internal const int SteamAppId = GameVersion.SteamAppId;

    /// <summary>等本进程退出的上限（秒）。超时=放弃重拉（宁可让玩家手动处理，不起双实例）。</summary>
    internal const int WaitExitSeconds = 180;

    /// <summary>本进程退出后、Steam 重拉前的延迟（秒）。仓库硬规则："taskkill 后重启需隔 8~10 秒"。</summary>
    internal const int SteamDelaySeconds = 9;

    /// <summary>
    /// 请求一键重启：拉起分离 PowerShell（等本进程 PID 退出 → 延迟 → steam 协议重拉）。
    /// 返回 true = 分离进程已启动（"已请求重启"，不代表重启一定成功）；false = 拉起失败
    /// （PowerShell 不可用等，调用方须诚实报错且**不退出**）。
    /// </summary>
    internal static bool Request()
    {
        int pid;
        try { pid = Process.GetCurrentProcess().Id; }
        catch (Exception e)
        {
            ModCenterPlugin.Log("warn 一键重启：读取本进程 PID 失败: " + e.Message);
            return false;
        }
        return StartDetachedPS(BuildScript(pid)) != null;
    }

    /// <summary>
    /// 分离重启脚本（ASCII-only；EncodedCommand 传，故单引号/换行均安全）。
    /// 结构：①按 PID 轮询本进程是否退出（1s 一拍，上限 <see cref="WaitExitSeconds"/>）→
    /// ②确认退出后 sleep <see cref="SteamDelaySeconds"/> 秒 → ③steam 协议重拉。
    /// 超时未退出 → 不重拉直接退出脚本（防双实例）。
    /// </summary>
    internal static string BuildScript(int pid)
    {
        var sb = new StringBuilder(512);
        sb.Append("$ErrorActionPreference='SilentlyContinue'\n");
        sb.Append("$tpid=").Append(pid.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("$gone=$false\n");
        sb.Append("$end=(Get-Date).AddSeconds(")
          .Append(WaitExitSeconds.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        sb.Append("while((Get-Date) -lt $end){\n");
        sb.Append("  $p=Get-Process -Id $tpid -ErrorAction SilentlyContinue\n");
        sb.Append("  if(-not $p){$gone=$true;break}\n");
        sb.Append("  Start-Sleep -Milliseconds 1000\n");
        sb.Append("}\n");
        sb.Append("if($gone){\n");
        sb.Append("  Start-Sleep -Seconds ")
          .Append(SteamDelaySeconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("  Start-Process 'steam://rungameid/")
          .Append(SteamAppId.ToString(CultureInfo.InvariantCulture)).Append("'\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>请求引擎退出（主线程调用——Ticker/Pump 上下文）。分离脚本正按本 PID 等待，
    /// 本调用返回后游戏进入正常退出流程（OnApplicationQuit/ConfigBridge 落盘钩照常跑）。</summary>
    internal static void QuitGame()
    {
        try
        {
            ModCenterPlugin.Log("一键重启：请求引擎退出（Application.Quit），分离进程将等待本 PID 结束后重拉");
            UnityEngine.Application.Quit();
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log("warn 一键重启：Application.Quit 调用异常: " + e.Message);
        }
    }

    /// <summary>拉起隐藏分离 powershell 执行脚本（不等待、不挂靠本进程生命周期）；
    /// 失败记 warn 返回 null（调用方自行决定兜底）。</summary>
    internal static Process StartDetachedPS(string psScript)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -WindowStyle Hidden -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript)),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return Process.Start(psi);
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log("warn 分离进程启动失败: " + e.Message);
            return null;
        }
    }
}
