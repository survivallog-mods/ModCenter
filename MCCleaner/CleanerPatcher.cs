using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Preloader.Core.Patching;

namespace MCCleaner;

/// <summary>
/// MC 启动清理器（ModCenter v2 生命周期组件一）：BepInEx patcher 阶段的纯前置执行器——
/// 无任何 Patch 方法，仅借 Initialize 在启动最早阶段执行动作（探针 E1 已实证可行）。
/// 单槽基座级组件：始终以 BepInEx\patchers\MCCleaner.dll 单份在场，游戏内通道不更新自身，
/// 见 spec §4.7（双槽同装已被 CLR 程序集身份合并证伪，Ruling 8）。
/// 本任务（骨架转正）只做生命周期自证；后续任务在 Initialize 末尾追加各执行段
/// （pending 执行器 / 加载序改挂 / 停用迁移）。
/// 自证文件（硬规则，不依赖 LogOutput.log）：BepInEx\MCCleaner_loaded.txt
/// </summary>
[PatcherPluginInfo("com.local.survivallog.mccleaner", "MC Cleaner", "1.0.0")]
public class CleanerPatcher : BasePatcher
{
    /// <summary>自证文件：BepInEx 根目录下 MCCleaner_loaded.txt</summary>
    private static string EvidencePath =>
        Path.Combine(Paths.BepInExRootPath, "MCCleaner_loaded.txt");

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

    public override void Initialize()
    {
        // 生命周期自证：时间戳 + 本 dll 实际加载位置（Assembly.Location 原值）
        Ev("Initialize reached, dll=" + typeof(CleanerPatcher).Assembly.Location);

        // ---- 后续任务在此之后追加执行段（pending 执行器 / 加载序改挂 / 停用迁移）----
    }
}
