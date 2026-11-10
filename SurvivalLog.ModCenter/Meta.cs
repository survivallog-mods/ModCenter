using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>元数据表条目：一个 mod 的面板页定义（spec §7 v4 表逐行转写）。</summary>
internal sealed class MetaMod
{
    public string Guid;  // BepInEx 插件 GUID（路由主键）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（短名路由兼容）
    public string Desc;  // 一句话简介
    public List<MetaGroup> Groups = new();
    public MetaItem OvrHost; // ovr 清单宿主项（动作加速 其他.Overrides；不进 groups items，可写）
    public MetaItem BagOvrHost = null; // bagOvr 清单宿主项（柜子容量 个性化 Global.BagOverrides，T20；不进 groups items，可写。2026-11-07 CabinetSize 退役后暂无使用者，字段保留兼容 ConfigBridge/panel.js 数据契约）

    // 启停键（任务 13）：该 mod 总开关在【其自己 cfg】里的真实节/键——SetModEnabled 直写
    // 此 entry（默认 General:Enabled，与 T9-T12 九 mod 的 Bind 一致；个别 mod 键名不同时覆盖）。
    public string EnabledSection = "General";
    public string EnabledKey = "Enabled";
}

internal sealed class MetaGroup
{
    public string Title;   // 分组标题（显示；≠cfg 节名）
    public List<MetaItem> Items = new();
}

internal sealed class MetaItem
{
    public string Section;   // cfg 真实节名
    public string Key;       // 主键（路由/兜底锚点；联动项 = 宽度键）
    public string[] WriteKeys; // 实写键数组（联动=宽高两键等值写；null=单键 [Key]）
    public string Label;     // 面板中文名（l）
    public string Badge;     // 生效徽标（b）：now / reload / reboot / cfg
    public string Type;      // float/int/bool/sel（entry 在场时以反射 SettingType 为准，此处仅兜底）
    public object Def;       // v 兜底：entry 尚不存在时用（如 RoleUnlock Plan.* 两键待任务 8 落地）
    public double? Min, Max; // 范围兜底：AcceptableValueRange 缺失时用（对照 spec §7 表）
    public double? St;       // 滑条步进（AcceptableValue 不携带步进，永远用兜底）
    public string[] Opts;    // sel 选项兜底（string 型无 AcceptableValueList，如 UiFit.Mode）
    public string Note;      // 静态补充说明
    public string LinkedSuffix; // 联动项动态 note 后缀（前缀"当前 宽w×高h"由桥现场拼）

    public bool Linked => WriteKeys != null && WriteKeys.Length > 1;

    /// <summary>实写键（永不 null：未声明联动 = 单键 [Key]；TrySet/快照统一走此口）。</summary>
    public string[] WKeys => WriteKeys != null && WriteKeys.Length > 0 ? WriteKeys : new[] { Key };

    /// <summary>展示键 k（仅显示；写路由以 Section+WriteKeys 为准）。联动 = "节.键A + 键B"（原型 v4 同款）。</summary>
    public string K => Linked
        ? Section + "." + WriteKeys[0] + " + " + WriteKeys[1]
        : Section + "." + Key;
}

/// <summary>
/// L1 已知冲突库条目（v2 任务 11；spec 2026-10-28-lifecycle-v2 §4.4 首批逐字照用）。
/// 匹配键=GUID 优先、dll 文件名兜底（大小写不敏感、仅比较文件名、.dll 后缀可省）；
/// 匹配逻辑纯函数见 <see cref="Meta.MatchL1"/>。Level=red|orange（v3 两级：gray 退役，功能叠加
/// 并入 orange + 显式字形；v3.1 字形统一小号黄叹号 !）。TipShort=行内图标短提示（逐字）；Tag=弹窗卡级标签（逐字：
/// 严重冲突/功能叠加）。Who=冲突对象（Meta.En 内部名；卡片渲染时转"中文名（En）"）。
/// 表驱动：新增/撤收录只动表，不动逻辑。
/// </summary>
internal sealed class ConflictEntry
{
    public string MatchGuid; // BepInEx 插件 GUID（主匹配键）
    public string MatchDll;  // dll 文件名（兜底匹配键；仅文件名，可带/不带 .dll）
    public string Level;     // red | orange（v3 两级）
    public string TipShort;  // 行内图标短提示（v3 逐字；完整结论在弹窗卡片）
    public string Tag;       // 弹窗卡级标签（v3 逐字；仅 L1 表内条目使用）
    public string Title;     // 卡片结论一句话（弹窗副标题）
    public string[] Who;     // 冲突对象（Meta.En 内部名；未登记原样展示）
    public string Reason;    // 原因（弹窗"原因"段逐字；可空=面板不渲染该段）
    public string[] Tips;    // 处置建议（弹窗"怎么办"编号列表）
}

/// <summary>
/// 元数据表（任务 6）：套件 mod 页面定义唯一蓝本 = spec §7 v4 九张表 + EndlessGuard/CabinetBurden 后续补登
/// （docs/superpowers/specs/2026-10-24-modcenter-design.md §7；结构对照原型 v4 MODS 数组）。
/// 纪律（作者 v3 评审 + spec §5.4）：**默认值与范围不写死为准**——快照时从
/// ConfigEntry.DefaultValue/AcceptableValueRange 反射合入；此处的 Def/Min/Max 仅在
/// AcceptableValueRange 缺失或 entry 尚不存在时兜底（数值照 spec §7 表抄，防漂移口径见 DESIGN §6）。
/// 两处对 spec 的实证修正（实证优先，硬规则 12）：
/// ① UiFit.Mode 合法值是 Scroll/Zoom/Off（ViewportFit.cs 描述明示；spec §7 表 "Fit" 为笔误）；
/// ② RoleUnlock Plan.ClearPool / Plan.VaseRecipe 两个 entry 任务 8 才新增，先按 spec 默认 true 兜底展示。
/// </summary>
internal static class Meta
{
    internal const string OvrSection = "其他";
    internal const string OvrKey = "Overrides";

    internal static readonly List<MetaMod> Mods = new()
    {
        new MetaMod
        {
            Guid = "com.local.survivallog.fridgechill",
            Name = "保鲜增强", En = "FridgeChill",
            Desc = "冰箱/保鲜类容器食材保鲜时长倍率；天赋保鲜上限可调。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "保鲜",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Chill", Key = "KeepMult",
                            Label = "保鲜时长倍率", Badge = "now", Type = "float",
                            Def = 2, Min = 1, Max = 50, St = 0.5
                        },
                        new MetaItem
                        {
                            Section = "Chill", Key = "MaxTotalMult",
                            Label = "天赋保鲜上限倍率", Badge = "now", Type = "float",
                            Def = 100, Min = 1, Max = 500, St = 1,
                            Note = "启动时字节补丁安装失败则置灰"
                        }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.quickaction",
            Name = "动作加速", En = "QuickAction",
            Desc = "全游戏动作耗时按分类统一加速（毫秒，0=官方原值）；单个动作可加覆盖单独调。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "分类耗时（毫秒，0=官方原值）",
                    Items =
                    {
                        MsItem("物品使用Ms", "物品使用"),
                        MsItem("家具功能Ms", "家具功能"),
                        MsItem("房屋维护Ms", "房屋维护"),
                        MsItem("杂项交互Ms", "杂项交互"),
                        MsItem("娱乐锻炼Ms", "娱乐锻炼")
                    }
                }
            },
            // 动作覆盖清单宿主：string "id:ms,id:ms" ↔ ovr:[{id,ms}]（解析在 ConfigBridge）
            OvrHost = new MetaItem
            {
                Section = OvrSection, Key = OvrKey, Label = "动作覆盖清单",
                Badge = "now", Type = "sel", Def = ""
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.cabineteverywhere",
            Name = "更好的储物柜", En = "CabinetEverywhere",
            Desc = "全容器作为制作/烹饪/酿造等取料源直达；柜子页签标签筛选与场景固定视图。高度修改：24 包逐包可调（0=恢复原生），宽度恒 12；缩高放不下的物品掉在脚下。",
            Groups =
            {
                // 高度修改批（2026-11-07，spec 2026-11-07-bag-height-design §4.1 逐字）：
                // 15 键 [Rail]，全部默认 0=恢复原生；Type=int 反射桥直写 SettingChanged→帧级生效。
                new MetaGroup
                {
                    Title = "高度修改（宽度恒 12，0=恢复原生）",
                    Items =
                    {
                        new MetaItem { Section = "Rail", Key = "BackpackHeight", Label = "背包高度", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "普通/登山/仓管背包共 9 包统一行数（原生 18）；天赋加行照常" },
                        new MetaItem { Section = "Rail", Key = "Height_110002", Label = "小型置物架", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 8" },
                        new MetaItem { Section = "Rail", Key = "Height_110001", Label = "中型置物架", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "65 家具共用（含工具柜70006/烧烤架）；原生 10" },
                        new MetaItem { Section = "Rail", Key = "Height_110000", Label = "大型置物架", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 10" },
                        new MetaItem { Section = "Rail", Key = "Height_110008", Label = "超大型置物架", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 12" },
                        new MetaItem { Section = "Rail", Key = "Height_10007", Label = "工具柜", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "家具 406；原生 7" },
                        new MetaItem { Section = "Rail", Key = "Height_1004", Label = "双门冰箱", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "12 家具共用（冰柜/食堂冰箱/冷饮柜/医用冷藏柜等）；原生 7" },
                        new MetaItem { Section = "Rail", Key = "Height_115000", Label = "双开门冰箱", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 7" },
                        new MetaItem { Section = "Rail", Key = "Height_115001", Label = "冰柜", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "5 家具共用（含豪华版双门冰箱）；原生 10" },
                        new MetaItem { Section = "Rail", Key = "Height_115005", Label = "大容量冰柜", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 10" },
                        new MetaItem { Section = "Rail", Key = "Height_1021", Label = "自行车车筐", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 4" },
                        new MetaItem { Section = "Rail", Key = "Height_1022", Label = "小汽车后备箱", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "原生 8" },
                        new MetaItem { Section = "Rail", Key = "Height_1023", Label = "SUV后备箱", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "与门口塑料袋共用；原生 10" },
                        new MetaItem { Section = "Rail", Key = "DrawerHeight", Label = "工作台抽屉", Badge = "now", Type = "int", Def = 0, Min = 0, Max = 50, St = 1, Note = "基准行数：三档=H/H+1/H+2（升级加行保留）；原生链 5/6/7" },
                        new MetaItem { Section = "Rail", Key = "MaxHeight", Label = "高度上限", Badge = "now", Type = "int", Def = 50, Min = 0, Max = 200, St = 1, Note = "逐包夹持；0=不封顶自担" }
                    }
                },
                // 负重批（2026-11-07 作者令）：背包负重调整新增 + 原独立 mod CabinetBurden 并回；
                // Burden 是 BagComponent.OnInit 快照——改值/切换后需重进存档对已存在容器/已穿背包生效（Badge=reload）。
                new MetaGroup
                {
                    Title = "负重（重进存档后生效）",
                    Items =
                    {
                        new MetaItem { Section = "Burden", Key = "BackpackBurden", Label = "背包负重（kg）", Badge = "reload", Type = "int", Def = 0, Min = 0, Max = 1000, St = 1, Note = "0=官方原值；9 个可穿背包统一（原生 9.6~12.5kg），天赋加成照常叠加" },
                        new MetaItem { Section = "Burden", Key = "CabinetUnburden", Label = "柜子负重解放", Badge = "reload", Type = "bool", Def = true, Note = "true=储物家具负重清零（负重条整行隐藏，等同冰箱）；false=恢复官方原值" }
                    }
                }
            }
            // Access.*/Filter.*/Scenario.* 保留生效不暴露（v4 定稿不变）
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.unlockhub",
            Name = "解锁中心", En = "UnlockHub",
            Desc = "通用解锁合集：角色专属内容逐项解锁 + 交易解锁（货架买后即刷）+ 探索解锁（每日一次探索点不限次）+ 重生解锁（大重生机会基本不限次）+ 属性上限解锁（饱腹/心态/精力/生命封顶 500→可调）；关闭即还原官方门槛。v1.8.0 前名「角色解锁 RoleUnlock」。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "藤蔓与花种子",
                    Items =
                    {
                        new MetaItem { Section = "Vine", Key = "UnlockVine", Label = "解锁藤蔓（爬山虎）", Badge = "now", Type = "bool", Def = true },
                        // SkipDefenseGate 已随 2026-10-24 作者定案从插件移除（藤蔓种子防御门槛永远保留官方规则），注册表同步删除
                        new MetaItem { Section = "Vine", Key = "UnlockFlowerSeeds", Label = "解锁花种子", Badge = "now", Type = "bool", Def = true }
                    }
                },
                new MetaGroup
                {
                    Title = "配方与规划",
                    Items =
                    {
                        new MetaItem { Section = "Defense", Key = "ShareDefenseRecipes", Label = "解锁防御系角色专属配方（全角色共享）", Badge = "now", Type = "bool", Def = true },
                        // 以下两 entry 任务 8 新增（spec §8-1「RoleUnlock 两项补开关」）；落地前快照按 spec 默认 true 兜底
                        new MetaItem { Section = "Plan", Key = "ClearPool", Label = "解锁角色专属生存规划（46 项功能类）", Badge = "now", Type = "bool", Def = true },
                        new MetaItem { Section = "Plan", Key = "VaseRecipe", Label = "解锁花瓶配方", Badge = "now", Type = "bool", Def = true }
                    }
                },
                new MetaGroup
                {
                    Title = "交易解锁",
                    Items =
                    {
                        // v1.8.0 新增（2026-11-02）：postfix 删货架快照，重开交易点走官方 DrawActiveShelf 全新上架
                        new MetaItem { Section = "Trade", Key = "UnlockTrade", Label = "交易解锁（货架买完即刷新上架）", Badge = "now", Type = "bool", Def = true }
                    }
                },
                new MetaGroup
                {
                    Title = "探索解锁",
                    Items =
                    {
                        // v1.9.0 新增（2026-11-08）：清空 Explore_DailyOnceMaps.Params3，每日一次探索点（科技公司）不限次
                        new MetaItem { Section = "Explore", Key = "UnlockDailyOnce", Label = "探索解锁（每日一次探索点不限次）", Badge = "now", Type = "bool", Def = true }
                    }
                },
                new MetaGroup
                {
                    Title = "重生解锁",
                    Items =
                    {
                        // v1.11.0 新增（2026-11-10）：postfix 改 RebirthMaxChances 读取结果 999，每次取值实时走 cfg → now
                        new MetaItem { Section = "Rebirth", Key = "UnlockChances", Label = "重生解锁（大重生机会基本不限次）", Badge = "now", Type = "bool", Def = true,
                            Note = "开启期间重生的次数照常记进存档，关掉后超官方上限的档回到无机会" }
                    }
                },
                new MetaGroup
                {
                    Title = "属性上限解锁",
                    Items =
                    {
                        // v1.12.0 新增（2026-11-10）：AttributeStandard 101/102/103/105 封顶 500→CapValue；
                        // Attr.Max 进档时从表快照（同 Burden 口径）→ 已在场角色过图重进后生效，Badge=reload
                        new MetaItem { Section = "Attribute", Key = "UnlockCap", Label = "属性上限解锁（饱腹/心态/精力/生命）", Badge = "reload", Type = "bool", Def = true,
                            Note = "只解除封顶不白送数值；已在场角色过图重进后生效（新档即时）" },
                        new MetaItem { Section = "Attribute", Key = "CapValue", Label = "属性封顶值", Badge = "reload", Type = "int", Def = 999, Min = 500, Max = 9999, St = 1,
                            Note = "只在属性上限解锁开启时生效；改小后已堆高的上限要过图重进才压回" }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.robotspeed",
            Name = "机器人提速", En = "RobotSpeed",
            Desc = "机器人移速/充电/续航/耗电/动作时长/货舱六维倍率 + 与手动动作对齐。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "机器人提速",
                    Items =
                    {
                        new MetaItem { Section = "机器人提速", Key = "动作速度对齐手动", Label = "动作速度对齐手动", Badge = "now", Type = "bool", Def = true },
                        MultItem("移速倍率"),
                        MultItem("充电速率倍率"),
                        MultItem("续航倍率"),
                        MultItem("耗电降低倍率"),
                        MultItem("动作时长倍率"),
                        new MetaItem
                        {
                            Section = "机器人提速", Key = "货舱格数倍率", Label = "货舱格数倍率",
                            Badge = "now", Type = "float", Def = 2, Min = 0.5, Max = 10, St = 0.5,
                            Note = "面板重开即见"
                        }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.planpick",
            Name = "自选生存规划", En = "PlanPick",
            Desc = "生存规划改自选：解锁日全表候选 + 搜索 + 键盘；整数百分比加成。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "花费",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Cost", Key = "CostMultiplierPercent", Label = "加成百分比（100=原值）",
                            Badge = "now", Type = "int", Def = 200, Min = 100, Max = 300, St = 5
                            // 范围不用兜底：PlanPick Bind 自带 AcceptableValueRange<int>(100,300)，快照反射合入
                        }
                    }
                },
                new MetaGroup
                {
                    Title = "列表",
                    Items =
                    {
                        new MetaItem { Section = "List", Key = "ShowLocked", Label = "展示未解锁规划（置灰+原因）", Badge = "now", Type = "bool", Def = true },
                        new MetaItem { Section = "List", Key = "ShowUnpooled", Label = "展示不入池规划", Badge = "now", Type = "bool", Def = true }
                    }
                }
            }
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.savemgr",
            Name = "存档管理", En = "SaveMgr",
            Desc = "每日结算自动存档点（间隔可设）+ 局内 ESC 菜单「立即存档/读取存档」。存档点与官方存档同列同管（× 删除），零隐藏副本。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "设置",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "AutoBackup", Key = "AutoBackup", Label = "启用自动存档",
                            Badge = "now", Type = "bool", Def = true
                        },
                        new MetaItem
                        {
                            Section = "AutoBackup", Key = "IntervalDays", Label = "自动存档间隔天数",
                            Badge = "now", Type = "int", Def = 5, Min = 0, Max = 30, St = 1,
                            Note = "0=关闭；1=每天；N=每 N 天的结算各备一份"
                        },

                    }
                }
            }
            // 2026-10-31 作者二次拍板：面板只 3 项（启用自动存档/间隔天数/保留上限），其余全删；
            // 存档/回档改游戏内实体按钮（SaveMgr MenuButtons：主菜单菜单列「一键回档」+ 局内
            // ESC 菜单「立即存档」，ModCenter MenuEntryInjector 同款注入模式）。
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.predisasterspeed",
            Name = "灾前加速", En = "PreDisasterSpeed",
            Desc = "灾变倒计时前的官方速度条增强（热键变档 + 真三倍校正）。仅支持启用/停用。",
            Groups = { } // 仅启停（顶部开关即全部，映射 General.Enabled；TrueThreeX/Hotkeys 不暴露）
        },
        new MetaMod
        {
            Guid = "com.local.survivallog.endlessguard",
            Name = "囤鼠桃园", En = "EndlessGuard",
            Desc = "无尽模式永不沦陷：拦新沦陷 + 恢复已沦陷的交易点/探险图/营地 + 抹掉沦陷倒计时；附 11 条拾遗扩展规划（D100-D300）。",
            Groups =
            {
                new MetaGroup
                {
                    Title = "沦陷防护",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Fall", Key = "PreventNewFalls", Label = "拦截新沦陷", Badge = "now", Type = "bool", Def = true,
                            Note = "关闭即恢复官方沦陷调度（阶段点名表还原需重启）"
                        },
                        new MetaItem
                        {
                            Section = "Fall", Key = "RestoreFallenPoints", Label = "恢复已沦陷点位", Badge = "reload", Type = "bool", Def = true,
                            Note = "读档时恢复交易点/探险图/营地（走过撤离剧情的营地除外）；物资份数始终按官方探索物资难度"
                        }
                    }
                },
                new MetaGroup
                {
                    Title = "生存规划扩展",
                    Items =
                    {
                        new MetaItem
                        {
                            Section = "Plans", Key = "ExtendScavengePlans", Label = "拾遗扩展 11 条", Badge = "reboot", Type = "bool", Def = true,
                            Note = "超市拾遗·六~十/医院踏勘·三~五/学校摸排·三~五（D100-D300 每 20 天一条）；关闭后重启生效，本局已注入内容保留"
                        }
                    }
                }
            }
        }
    };

    private static MetaItem MsItem(string key, string label) => new()
    {
        Section = "分类修改", Key = key, Label = label,
        Badge = "now", Type = "int", Def = 500, Min = 0, Max = 5000, St = 50
    };

    private static MetaItem MultItem(string key) => new()
    {
        Section = "机器人提速", Key = key, Label = key,
        Badge = "now", Type = "float", Def = 2, Min = 0.5, Max = 10, St = 0.5
    };

    // ============ L1 已知冲突库（v2 任务 11；spec §4.4 首批逐字） ============

    /// <summary>
    /// 首批两条（第三条 karinasprite 按 spec §4.4 明确排除，见表尾注释）。字段口径见
    /// <see cref="ConflictEntry"/>。新增条目只改本表；快照 ics/弹窗卡片都由本表派生
    /// （BuildSnapshot → <see cref="MatchL1"/>；MC_CF → 同表反向查对象）。
    /// </summary>
    internal static readonly List<ConflictEntry> KnownConflicts = new()
    {
        new ConflictEntry
        {
            MatchGuid = "com.jangfullmoon.storagecapacityexpansion",
            MatchDll = "StorageCapacityExpansion.dll",
            Level = "red",
            TipShort = "严重冲突：与「储物扩容」只能留一个，点看详情",
            Tag = "严重冲突",
            Title = "和第三方「储物扩容」只能留一个：同时开启会互相覆盖柜子和背包的容量数值。",
            Who = new[] { "CabinetSize", "BackpackExpand", "CabinetEverywhere" },
            Reason = "「储物扩容」改写 73 件柜子的内部数据并强改数值，与套件的修改相乘叠加（×1.5 ×1.5 = ×2.25）。",
            Tips = new[] { "停用或卸载「储物扩容」（推荐）", "或者停用上面任意一个套件 mod", "调加载顺序没有用" }
        },
        new ConflictEntry
        {
            MatchGuid = "survivallog.expandedstorage",
            MatchDll = "ExpandedStorage",
            Level = "orange",
            TipShort = "功能叠加：与套件背包扩容效果叠加，点看详情",
            Tag = "功能叠加",
            Title = "这几个 mod 都在改储物数值，效果会叠加，可能超出预期。",
            Who = new[] { "BackpackExpand" },
            Reason = "",
            Tips = new[] { "介意叠加就只留一个" }
        }
        // 明确排除（spec §4.4 末条，勿因"同作者/同包名族"误加）：
        // com.jangfullmoon.karinasprite / KarinaSprite.dll —— 纯立绘替换、零游戏钩子，
        // 反汇编鉴定档案已排除；收录只会产生误报。
    };

    // ============ L3 功能域标注（v2 任务 11；Guid → 域） ============

    /// <summary>
    /// 套件成员 GUID 集（= 组织 12 仓，与 Domains 表"12 套件 mod"注释块同源）。
    /// v3.3 冲突裁定：套件↔套件默认兼容（同组织协同设计），只有涉及套件外 mod 的
    /// 碰撞/叠加才报告——判定入口 <see cref="IsSuiteGuid"/>。
    /// </summary>
    internal static readonly string[] SuiteGuids =
    {
        "com.local.survivallog.backpackexpand",
        "com.local.survivallog.cabinetburden",
        "com.local.survivallog.cabineteverywhere",
        "com.local.survivallog.cabinetsize",
        "com.local.survivallog.endlessguard",
        "com.local.survivallog.fridgechill",
        "com.local.survivallog.modcenter",
        "com.local.survivallog.planpick",
        "com.local.survivallog.predisasterspeed",
        "com.local.survivallog.quickaction",
        "com.local.survivallog.robotspeed",
        "com.local.survivallog.roleunlock",
        "com.local.survivallog.unlockhub",
    };

    /// <summary>是否套件成员（OrdinalIgnoreCase；空值=false）。未知来源（guid=null）不算套件。</summary>
    internal static bool IsSuiteGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return false;
        foreach (var g in SuiteGuids)
            if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 功能域标注表：12 套件 mod 全覆盖 + 已收录第三方（进 L1 名录后才有类型；其余第三方不登记，
    /// 防"同域"盲目误报）。只用于"同域共存"灰标，只标注不判定。同域灰标的产出条件（控制器裁定）：
    /// 域内已装 ≥2 个且至少一个在本表登记——即只把"已登记且已装"的 mod 计入域内计数。
    /// 排除项 karinasprite 与 L1 一致不登记（纯立绘替换零钩子）。
    /// v2 任务 12（T11 M2）：非 L1 第三方（bettertrade/statsui）登记删除——spec §4.4"第三方仅在
    /// 进入 L1 名录后带类型"，未收录者不参与域计数（防跨域误报）。
    /// </summary>
    internal static readonly Dictionary<string, string> Domains = new(StringComparer.OrdinalIgnoreCase)
    {
        // 12 套件 mod（= 组织 12 仓；CabinetBurden 已补登 Meta 表，ModCenter 自身走附加名表）
        ["com.local.survivallog.backpackexpand"] = "储物",
        ["com.local.survivallog.cabinetburden"] = "储物",
        ["com.local.survivallog.cabineteverywhere"] = "储物",
        ["com.local.survivallog.cabinetsize"] = "储物",
        ["com.local.survivallog.endlessguard"] = "其他",
        ["com.local.survivallog.fridgechill"] = "储物",
        ["com.local.survivallog.modcenter"] = "面板",
        ["com.local.survivallog.planpick"] = "规划",
        ["com.local.survivallog.predisasterspeed"] = "速度",
        ["com.local.survivallog.quickaction"] = "速度",
        ["com.local.survivallog.robotspeed"] = "机器人",
        ["com.local.survivallog.roleunlock"] = "角色",
        // v1.8.0 更名（2026-11-02）：RoleUnlock → UnlockHub；旧 guid 保留登记（旧版玩家未升级期间仍算套件成员）
        ["com.local.survivallog.unlockhub"] = "解锁",
        // 已收录第三方（L1 名录）
        ["com.jangfullmoon.storagecapacityexpansion"] = "储物",
        ["survivallog.expandedstorage"] = "储物"
        // 排除：com.jangfullmoon.karinasprite —— 纯立绘替换零钩子（spec §4.4 档案已排除），与 L1 同口径不登记。
    };

    /// <summary>
    /// 非 Meta 九表的套件成员显示名（T11 M5）：L3 who / L2 owner / L1 冲突对象里出现时给中文名
    /// （此前 CabinetBurden/ModCenter 只能显示插件自报英文名）。只影响显示，不参与路由/匹配。
    /// </summary>
    private sealed class ExtraName
    {
        public string Guid;
        public string En;
        public string Name;
    }

    private static readonly ExtraName[] ExtraNames =
    {
        new() { Guid = "com.local.survivallog.cabinetburden", En = "CabinetBurden", Name = "柜子负重解放" },
        new() { Guid = "com.local.survivallog.modcenter", En = "ModCenter", Name = "Mod 管理中心" },
    };

    /// <summary>非 Meta 九表套件成员的中文名（GUID 精确；null=未登记）。</summary>
    internal static string ExtraNameOf(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var e in ExtraNames)
            if (string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase)) return e.Name;
        return null;
    }

    /// <summary>
    /// L1 匹配（GUID 优先、dll 文件名兜底）。两条路径各自全表扫描：先 GUID 全表，命中即返；
    /// 再 dll 文件名全表（去目录、去 .dll 后缀、OrdinalIgnoreCase）。dllName 可传绝对路径。
    /// 纯函数（无 BepInEx 依赖）——装载期外可由独立自检程序直接调用（T11 单元自证）。
    /// </summary>
    internal static ConflictEntry MatchL1(string guid, string dllName)
    {
        if (!string.IsNullOrEmpty(guid))
            foreach (var e in KnownConflicts)
                if (!string.IsNullOrEmpty(e.MatchGuid)
                    && string.Equals(guid, e.MatchGuid, StringComparison.OrdinalIgnoreCase))
                    return e;
        var dll = DllBaseName(dllName);
        if (dll.Length > 0)
            foreach (var e in KnownConflicts)
            {
                var want = DllBaseName(e.MatchDll);
                if (want.Length > 0 && string.Equals(dll, want, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        return null;
    }

    /// <summary>dll 文件名基准：取最后一段路径、剥 .dll 后缀（大小写不敏感）；空入参 = 空串。</summary>
    internal static string DllBaseName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var i = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        var name = i >= 0 ? path.Substring(i + 1) : path;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        return name;
    }

    /// <summary>GUID → 功能域；未登记 = null（L3 灰标不产出）。</summary>
    internal static string DomainOf(string guid) =>
        !string.IsNullOrEmpty(guid) && Domains.TryGetValue(guid, out var d) ? d : null;

    /// <summary>冲突对象显示名：Who 里的 Meta.En → "中文名（En）"（如 柜子容量（CabinetSize））；
    /// 非 Meta 九表套件成员（CabinetBurden/ModCenter，T11 M5）走附加表；未登记原样返回
    /// （表外对象也能读）。括号口径：让 spec 内部名逐字可核 + 面板可读。</summary>
    internal static string DisplayNameOf(string enOrGuid)
    {
        if (string.IsNullOrEmpty(enOrGuid)) return "";
        foreach (var mm in Mods)
            if (string.Equals(mm.En, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mm.Guid, enOrGuid, StringComparison.OrdinalIgnoreCase))
                return mm.Name + "（" + enOrGuid + "）";
        foreach (var e in ExtraNames)
            if (string.Equals(e.En, enOrGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Guid, enOrGuid, StringComparison.OrdinalIgnoreCase))
                return e.Name + "（" + enOrGuid + "）";
        return enOrGuid;
    }

    /// <summary>色级 → 卡片标签（红=冲突 / 橙=碰撞 / 灰=同域）。</summary>
    internal static string TagOf(string level) =>
        level == "red" ? "冲突" : (level == "orange" ? "碰撞" : "同域");
}
