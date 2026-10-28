using System;
using System.Diagnostics;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 分离进程启动器（T4 自 DllDisabler 迁入，Ruling 4 保留）：通用 EncodedCommand PowerShell
/// 分离进程启动器——脚本经 base64(UTF-16LE) 编码传 <c>-EncodedCommand</c>，彻底规避
/// 引号/反斜杠的命令行转义坑（脚本仍应保持 ASCII-only 纪律）。v1 停用改名的退出分离进程
/// 机械退役后本类暂无调用方；保留供 T10（自卸载收尾：本进程退出后删自身槽）与 T13
/// （一键重启：本进程退出后重启游戏）复用。
/// </summary>
internal static class RestartHelper
{
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
