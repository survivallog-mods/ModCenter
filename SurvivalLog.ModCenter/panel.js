/* ModCenter 面板 JS（内嵌资源 SurvivalLog.ModCenter.panel.js）。
 * 任务 7 = 完整面板（原型 v4 移植：docs/superpowers/specs/2026-10-24-modcenter-prototype.html
 * 的 <style> 面板段 + renderSide/renderCfg/ctl/toast/toggle/ovr 逻辑；场景模拟与 demo-bar 不迁）。
 * v2 生命周期任务 7 = 图标徽标系统 + 待生效条（原型 2026-10-28-modcenter-v2-prototype.html
 * 移植，样式值照抄）：mod 行内 ics 容器（mod.ics=[{k,tip,click?,g?}]，k=red|orange|gray|
 * blue|amber 18px 圆片七形，悬停 title 全文，click 路由 cf=冲突详情弹窗/upd=更新确认弹窗壳，
 * 无 click=纯状态如 ⟳ 待生效）；底部待生效条（__mcEvent MC_PENDING_SUMMARY{n,labels}，n>0
 * 显示；v2 任务 13 撤销/立即重启两钮接真）；弹窗=面板内浮层（.mc-modal-bg/.mc-modal 同款，
 * ESC/点底关闭，弹窗开着时 ESC 先关弹窗再关面板）。ics 缺省时行渲染与 v1 完全一致
 * （快照协议向后兼容）。
 * v2 生命周期任务 8 = 套件仓库页 + 更新确认链（同原型仓库段照抄）：侧栏入口卡
 * （.__mc-repo-entry 计数徽标）→ 主区仓库视图（.__mc-repo-row 五态：更新 pri/降级 org/
 * 安装 blue/告警 org/✓最新；Remote=null=红字"检查失败"绝不显示"已是最新"；← 返回/
 * 检查更新 blue/全部更新 pri）；真数据更新确认弹窗（版本对比 kv + 校验/生效说明 +
 * D7 降级红字 + D8 版本告警红字 + 橙"降级到 X"/琥珀"更新到 X"主钮）→ 确认后 MC_UPD/MC_INST
 * → 弹窗内联进度条（__mcEvent MC_DL{repoKey,pct,stage} 就地更新，弹窗不重建）；MC_REPO_DATA
 * （rows+lastCheck+checking+dl）驱动视图与入口卡计数；⊘ 图标消歧 g:"kbw"。
 * v2 生命周期任务 9 = 加载排序模式（原型排序段照抄）：侧栏底部按钮切换（ST.ord 面板内状态，
 * 不新开键盘门——与 MC_OPEN/MC_CLOSE 共存）；排序模式侧栏平铺全列表（不分分组，按当前
 * order 序：行=拖柄 ⋮⋮+序号+名+版本+冲突 chip（显示无 click，裁定 4；⟳/↑↓/⊘ 不显示）），
 * HTML5 DnD（dragstart 记源/dragover 上下半区琥珀插入线/drop 重排，事件委托挂 side 免重挂）
 * + 键盘替代（行 tabindex=0 聚焦 ↑↓ 移动，WCAG 2.2 AA）；工具条（说明+恢复默认+保存顺序）；
 * MC_ORDER_GET 拉序（MC_ORDER_DATA{guids[],rows[],def[]}）→ 本地重排 → MC_ORDER_SAVE{guids[]}
 * → MC_ORDER_OK 退出排序模式+toast / MC_ORDER_FAIL 整单拒绝 toast。
 * v2 生命周期任务 10 = 彻底卸载（原型 delHtml 照抄）：详情页维护动作区"卸载…"按钮 →
 * MC_UNINST{guid} → MC_UNINST_DATA{guid,name,self,files[],note?} 渲染确认弹窗（⚠ 红+名称；
 * "将删除".files 三行红字（dll/cfg/记录说明）；"不受影响"；取消/确认卸载 danger 钮）→ 确认
 * MC_UNINST_DO{guid} → 关弹窗（结果 toast/快照由 C# 回推：行内 ⟳ + 底条"卸载 X"）；ModCenter
 * 自身（__self 页按钮）走特别文案分支（面板本体消失+清理器保留说明）。带出：T9 三小件
 * （ORD.src 改存 guid / ordbar 两钮未加载禁用 / HandleOrderSave 兜底在 C# 侧）+ 待生效条
 * 中性档（MC_PENDING_SUMMARY.applied="自定义加载序生效中"——不计入计数、弱化样式）。
 * v2 生命周期任务 11 = 冲突详情弹窗（原型 cfHtml 照抄）：行内 ics 的 click:"cf"（⚠红冲突 /
 * ⇄橙碰撞 / ◎灰同域）点击 → 占位壳 + MC_CF{guid} → MC_CF_DATA{guid,cards:[{lv,tag,title,
 * who[],reason,tips[]}]} → 每条冲突一卡片（h3 图标+"mod名 · 标签" / 副标题 / 冲突对象·原因·
 * 处置建议三 sec；多卡片间分隔线）+ 固定风险文案 + 关闭/停用此 mod/卸载此 mod… 三钮
 * （停用走 MC_TOGGLE_MOD 既有启停链、卸载…转 T10 卸载确认弹窗）；C# 未回包 5s 超时兜底。
 * v2 生命周期任务 12 = L2 补丁碰撞实测：侧栏仓库卡下方"补丁碰撞 N 组"汇总行（MC_SCAN_DATA
 * 驱动；点击弹汇总窗，列出全部碰撞组含未知来源；框架侧多 owner 组标注"框架侧 N 组未计入"
 * 单独披露、不铺行——Ruling 14②；含"重新扫描"→ MC_SCAN）＋ 行内聚合橙标 ⇄
 * （C# 每 mod 一枚，click:"cf" 复用冲突详情弹窗的橙卡片；停用行 w:true → __mc-weak 弱化）；
 * 灰卡（lv=gray）标题用"同域 mod"（T11 M4）；T11 M2/M3/M5/M6 为 C# 侧表/文案修正。
 * v2 生命周期任务 13 = 立即重启助手：底部待生效条两钮接真——"撤销"→ MC_PENDING_UNDO
 * （C# 移除 pending.json：ops 与 order 一并清，回到默认加载序）→ 快照+摘要+toast 由 C# 回推；
 * "立即重启"→ 确认弹窗（面板内浮层，文案含"将退出游戏并在约 10 秒后自动重启以应用 N 项
 * 待生效操作。未保存的对局进度会丢失。"，取消/确认重启）→ 确认后 MC_RESTART → C# 拉分离
 * 进程并延迟退出本进程；结果 toast 一律 C# 回推（"正在重启游戏…"=已请求重启，拉起失败报错
 * 且游戏不退出——不假装成功）。
 *
 * 注入落点：Root 文档（C# WebBridge.ExecuteJavaScript 上下文）。__McInstalled 守卫幂等。
 * 全局契约（C# Ticker / MenuEntryInjector 对齐）：__McInstalled / __mcOutbox / __mcDrain /
 * __mcOpen(hostDoc) / __mcClose / __mcEsc(e) / __mcRender(snapshot) / __mcEvent(event)。
 * 调试镜像：window.__mcLastSnapshot / __mcLastEvent（T6 存根语义保留，CDP 断言用）。
 *
 * 面板 DOM 挂「入口按钮所在 iframe 文档」（host doc，T5 结论：Root 无 clickThrough）。
 * 点击可达性：宿主页 webui-core clickThrough 清单 = .unity-interactive, button, input,
 * select, a, [data-interactive]——因此左列 mod 行（div）显式带 data-interactive，其余
 * 可点元素一律 button/input/select（原型里的 span 开关改为 button，样式全量自带）。
 *
 * 数据流（T6 契约）：收 __mcRender(snapshot) 全量重绘（无 diff，官方模式）；
 * snapshot = {self:{ver}, mods:[ModInfo], tp:[ModInfo]}，
 * ModInfo={guid,name,en,desc,enabled,groups:[{title,items:[Item]}],ovr?:[{id,ms}]}，
 * Item={k,l,t,v,min?,max?,st?,b,note?,opts?}。发消息 push __mcOutbox：
 * MC_SET{guid,section,key,value}（section/key 从展示键 k 按首个 '.' 拆分——C# Match
 * 接受真实节名+主键/组合展示键/任一实写键）/ MC_TOGGLE_MOD{guid,enabled} / MC_OPEN{host}
 * / MC_CLOSE{host?,why?}（why='host-doc-gone' 为任务 8 孤儿看门狗补发）。ovr 清单序列化
 * （"id:ms,id:ms" 整串写 其他.Overrides）由本文件承担。
 *
 * 全部元素 id/class 与样式 __mc 前缀且选择器锚定 #__mc_overlay（零泄漏）；不碰官方
 * tabs 数组与页面数据（CabinetEverywhere C6 决策同款）。
 */
(function () {
  if (window.__McInstalled) return;
  window.__McInstalled = true;

  /* ---- outbox：JS → C# 单向信箱（C# 每 2s __mcDrain 取走） ---- */
  window.__mcOutbox = [];
  window.__mcDrain = function () { return window.__mcOutbox.splice(0); };

  var BADGE = {
    now:    { t: '\u26a1 \u7acb\u5373\u751f\u6548', cls: '__mc-b-now' },      // ⚡ 立即生效
    reload: { t: '\ud83d\udcc1 \u8bfb\u6863\u540e', cls: '__mc-b-reload' },   // 📁 读档后
    reboot: { t: '\ud83d\udd04 \u91cd\u542f\u540e', cls: '__mc-b-reboot' },   // 🔄 重启后
    cfg:    { t: '\u2699 \u5df2\u5199\u5165 cfg',  cls: '__mc-b-cfg' },       // ⚙ 已写入 cfg
  };
  var TOAST_SMALL = {
    now:    '\u5df2\u5199\u5165 cfg \u00b7 \u26a1 \u7acb\u5373\u751f\u6548\uff08\u7ea65\u79d2\u5185\uff09',
    reload: '\u5df2\u5199\u5165 cfg \u00b7 \u8868\u503c\u5373\u65f6\u6539 \u00b7 \ud83d\udcc1 \u8bfb\u6863\u540e\u5bf9\u65e2\u6709\u5bb9\u5668\u4f53\u73b0',
    cfg:    '\u5df2\u5199\u5165\u8be5 mod cfg \u00b7 \u751f\u6548\u65f6\u673a\u53d6\u51b3\u4e8e\u8be5 mod \u662f\u5426\u76d1\u542c\u914d\u7f6e\u53d8\u66f4',
    reboot: '\u5df2\u5199\u5165 \u00b7 \ud83d\udd04 \u91cd\u542f\u540e\u751f\u6548',
  };
  var SELF_NAME = 'Mod \u7ba1\u7406\u4e2d\u5fc3'; // Mod 管理中心
  var SELF_EN = 'ModCenter\uff08\u672c\u9762\u677f\uff09';
  var OVR_SECTION = '\u5176\u4ed6', OVR_KEY = 'Overrides'; // 与 C# Meta.OvrSection/OvrKey 同源

  /* ---- 状态：host/root=宿主文档与面板根；snap=最近快照；sel='__self'|guid ----
   * v2 任务 7：pend=最近 MC_PENDING_SUMMARY（底部待生效条数据，跨开关面板保留）；
   * pendEl/pendTx/mbg/mdl=待生效条与浮层弹窗元素（随面板建/拆）。
   * v2 任务 8：repo=仓库视图开着；repoData=最近 MC_REPO_DATA（跨开关保留）；dl=当前下载链
   * {repoKey,pct,stage}；mdlRepoKey=更新确认弹窗归属仓（MC_DL 进度归属判定）。
   * v2 任务 9：ord=排序模式开着（面板内状态，跨开关保留同 repo）；order=本地工作序
   * （guids[]，进排序模式拉 MC_ORDER_DATA 填充；未保存的本地重排跨开关面板保留）；
   * ordRows=最近 MC_ORDER_DATA（含 rows 显示元数据与 def 默认序）；ordSaving=保存
   * 回执（MC_ORDER_OK/FAIL）在途防连点。ORD.src=DnD 源行 guid（拖拽会话期；T10 带出①）。 */
  var S = { host: null, root: null, side: null, cfg: null, sub: null, toastEl: null, pendEl: null, pendTx: null, mbg: null, mdl: null, mdlRepoKey: null };
  var ST = { snap: null, sel: null, pend: null, repoData: null, dl: null, chkWaiting: false, ord: false, order: null, ordRows: null, ordSaving: false, uninstGuid: null, cfGuid: null, scan: null, patchOpen: false, scanWait: false };
  var ORD = { src: null }; // v2 任务 9：拖拽源 guid（null=无拖拽会话；T10 带出①：改存 guid 防拖拽中键盘重排错位）
  var TH = Object.create(null); // 滑条 100ms 节流句柄表
  var uninstTimer = null;       // v2 任务 10：MC_UNINST_DATA 等待超时句柄
  var cfTimer = null;           // v2 任务 11：MC_CF_DATA 等待超时句柄
  var SELF_GUID = 'com.local.survivallog.modcenter'; // __self 页卸载按钮的目标 guid（C# ModCenterPlugin.Guid 同源）

  var CSS = ''
    + '#__mc_overlay{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(5,6,10,.8);}'
    + '#__mc_overlay *{box-sizing:border-box;margin:0;padding:0;}'
    /* 面板框 */
    + '#__mc_overlay #__mc_panel{width:92%;height:88%;background:#171a23;border:1px solid #2a2f3d;border-radius:14px;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 18px 70px rgba(0,0,0,.75);font-family:"Segoe UI","Microsoft YaHei",system-ui,sans-serif;color:#e8e6e0;user-select:none;}'
    + '#__mc_overlay .__mc-head{display:flex;align-items:center;gap:12px;padding:13px 18px;border-bottom:1px solid #2a2f3d;background:#1d212c;}'
    + '#__mc_overlay .__mc-title{font-size:17px;letter-spacing:2px;font-weight:600;}'
    + '#__mc_overlay .__mc-sub{font-size:12px;color:#6b7180;}'
    + '#__mc_overlay .__mc-close{margin-left:auto;width:32px;height:32px;border-radius:8px;border:1px solid #2a2f3d;background:transparent;color:#9aa0ad;font-size:15px;cursor:pointer;line-height:1;font-family:inherit;}'
    + '#__mc_overlay .__mc-close:hover{border-color:#f87171;color:#f87171;}'
    /* 主体：左列 + 右区 */
    + '#__mc_overlay .__mc-body{flex:1;display:flex;min-height:0;}'
    + '#__mc_overlay .__mc-side{width:280px;flex-shrink:0;border-right:1px solid #2a2f3d;overflow-y:auto;padding:8px;display:flex;flex-direction:column;}' /* Ruling 18：原型定稿 280px */
    + '#__mc_overlay .__mc-side>*{flex-shrink:0;}' /* 子项不压缩（溢出走滚动，防行被压扁） */
    + '#__mc_overlay .__mc-side .__mc-cat{font-size:11px;color:#6b7180;letter-spacing:2px;padding:8px 10px 4px;}'
    + '#__mc_overlay .__mc-mod-row{display:flex;align-items:center;gap:6px;padding:9px 10px;border-radius:8px;cursor:pointer;border:1px solid transparent;}' /* Ruling 18：原型定稿 gap 6px */
    + '#__mc_overlay .__mc-mod-row:hover{background:rgba(255,255,255,.03);}'
    + '#__mc_overlay .__mc-mod-row.__mc-sel{background:rgba(232,163,61,.13);border-color:rgba(232,163,61,.27);}'
    + '#__mc_overlay .__mc-mod-row .__mc-st{width:8px;height:8px;border-radius:50%;background:#4ade80;flex-shrink:0;}'
    + '#__mc_overlay .__mc-mod-row.__mc-off .__mc-st{background:#565d6d;}'
    + '#__mc_overlay .__mc-mod-row .__mc-nm{font-size:13.5px;flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}'
    + '#__mc_overlay .__mc-mod-row.__mc-off .__mc-nm,#__mc_overlay .__mc-mod-row.__mc-lock .__mc-nm{color:#6b7180;}'
    + '#__mc_overlay .__mc-tp-empty{padding:6px 10px;font-size:12px;color:#6b7180;}'
    /* 迷你开关（button；原型 span 开关改为 button 以进 clickThrough 清单） */
    + '#__mc_overlay .__mc-mini-sw{width:30px;height:17px;border-radius:9px;background:#565d6d;position:relative;cursor:pointer;flex-shrink:0;transition:.15s;border:0;appearance:none;outline:none;}'
    + '#__mc_overlay .__mc-mini-sw::after{content:"";position:absolute;left:2px;top:2px;width:13px;height:13px;border-radius:50%;background:#cfd3dc;transition:.15s;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-on{background:#4ade80;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-on::after{left:15px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big{width:46px;height:25px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big::after{width:20px;height:20px;top:2px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big.__mc-on::after{left:23px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-lock{opacity:.35;cursor:not-allowed;pointer-events:none;}'
    /* 右区 */
    + '#__mc_overlay .__mc-cfg{flex:1;overflow-y:auto;padding:16px 22px 30px;min-width:0;}'
    + '#__mc_overlay .__mc-mod-title{display:flex;align-items:center;gap:12px;}'
    + '#__mc_overlay .__mc-mod-title h2{font-size:18px;font-weight:600;}'
    + '#__mc_overlay .__mc-mod-title .__mc-en{font-size:12px;color:#6b7180;font-weight:400;}'
    + '#__mc_overlay .__mc-mod-desc{margin:8px 0 4px;font-size:13px;color:#9aa0ad;line-height:1.6;}'
    + '#__mc_overlay .__mc-stop-banner{margin:10px 0 0;padding:9px 14px;border-radius:8px;font-size:12.5px;line-height:1.6;background:rgba(127,29,29,.2);border:1px solid rgba(248,113,113,.27);color:#fca5a5;display:none;}'
    + '#__mc_overlay .__mc-stop-banner.__mc-show{display:block;}'
    + '#__mc_overlay .__mc-grp{margin-top:20px;}'
    + '#__mc_overlay .__mc-grp>h3{font-size:12.5px;color:#e8a33d;letter-spacing:2px;padding-bottom:7px;border-bottom:1px solid #2a2f3d;margin-bottom:4px;font-weight:500;}'
    + '#__mc_overlay .__mc-item{display:flex;align-items:center;gap:14px;padding:10px 4px;border-bottom:1px dashed rgba(255,255,255,.06);}'
    + '#__mc_overlay .__mc-item .__mc-info{flex:1;min-width:0;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-lb{font-size:13.5px;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-ck{font-size:11px;color:#6b7180;font-family:Consolas,monospace;margin-top:2px;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-ck.__mc-note{color:#8a90a0;}'
    + '#__mc_overlay .__mc-item.__mc-failed .__mc-ctl{opacity:.35;pointer-events:none;}' /* 任务 16：failed 项置灰锁控件 */
    + '#__mc_overlay .__mc-item.__mc-missing .__mc-ctl{opacity:.35;pointer-events:none;}' /* 必修 7：未安装（dll 不在场）页面控件只读 */
    + '#__mc_overlay .__mc-item .__mc-info .__mc-ck.__mc-fail-note{color:#f87171;}'
    + '#__mc_overlay .__mc-item .__mc-ctl{width:min(300px,42%);flex-shrink:0;display:flex;align-items:center;gap:10px;justify-content:flex-end;}'
    + '#__mc_overlay .__mc-item .__mc-badge{width:96px;flex-shrink:0;text-align:right;font-size:11.5px;}'
    + '#__mc_overlay .__mc-b-now{color:#4ade80;}#__mc_overlay .__mc-b-reload{color:#60a5fa;}'
    + '#__mc_overlay .__mc-b-reboot{color:#f87171;}#__mc_overlay .__mc-b-cfg{color:#b8a1e3;}'
    /* 覆盖清单 */
    + '#__mc_overlay .__mc-ovr-add{display:flex;gap:8px;align-items:center;padding:10px 4px 6px;}'
    + '#__mc_overlay .__mc-ovr-add input{width:120px;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:5px 9px;font-size:13px;font-family:Consolas,monospace;}'
    + '#__mc_overlay .__mc-ovr-add button{background:#0f1219;border:1px solid #e8a33d;color:#e8a33d;border-radius:6px;padding:5px 14px;font-size:13px;cursor:pointer;font-family:inherit;}'
    + '#__mc_overlay .__mc-ovr-add button:hover{background:rgba(232,163,61,.13);}'
    + '#__mc_overlay .__mc-del{width:26px;height:26px;border-radius:6px;border:1px solid #2a2f3d;background:transparent;color:#6b7180;font-size:12px;cursor:pointer;flex-shrink:0;font-family:inherit;line-height:1;}'
    + '#__mc_overlay .__mc-del:hover{border-color:#f87171;color:#f87171;}'
    + '#__mc_overlay .__mc-ovr-empty{padding:12px 4px;font-size:12.5px;color:#6b7180;}'
    /* 控件 */
    /* 实机 UI 三修·问题2：滑条显式轨道/滑钮。游戏 WebView（CEF）不画 accent-color 依赖的
     * 原生默认轨道（只见圆钮，拖动才显示）——appearance 归零 + 自绘 4px 轨道（#2a2f3d）
     * 与 13px 圆钮（accent #e8a33d，margin-top:-4.5px 在 4px 轨道上垂直居中）；
     * -moz 系双保险。容器高度 16px 保点击热区。 */
    + '#__mc_overlay input[type=range]{-webkit-appearance:none;appearance:none;flex:1;accent-color:#e8a33d;background:transparent;height:16px;}'
    + '#__mc_overlay input[type=range]::-webkit-slider-runnable-track{height:4px;background:#2a2f3d;border-radius:2px;}'
    + '#__mc_overlay input[type=range]::-webkit-slider-thumb{-webkit-appearance:none;width:13px;height:13px;border-radius:50%;background:#e8a33d;margin-top:-4.5px;border:0;}'
    + '#__mc_overlay input[type=range]::-moz-range-track{height:4px;background:#2a2f3d;border-radius:2px;}'
    + '#__mc_overlay input[type=range]::-moz-range-thumb{width:13px;height:13px;border-radius:50%;background:#e8a33d;border:0;}'
    + '#__mc_overlay input[type=text]{flex:1;min-width:0;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:4px 9px;font-size:13px;text-align:left;font-family:inherit;}'
    /* T20 numbox（纯文本数值框）——必须置于 input[type=text] 通用规则之后且显式压回
     * flex/对齐（同为 type=text，层叠序否则被通用规则拉伸成 flex:1 左对齐）。 */
    + '#__mc_overlay .__mc-numbox{flex:0 0 auto;width:62px;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:4px 6px;font-size:13px;text-align:center;font-family:inherit;}'
    + '#__mc_overlay .__mc-bov-lb{font-size:11px;color:#6b7180;flex-shrink:0;}'
    + '#__mc_overlay input[type=text]{flex:1;min-width:0;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:4px 9px;font-size:13px;text-align:left;font-family:inherit;}'
    /* 问题3 后续：ovr 新增框改 type=text 后会被上面通用 text 规则 flex:1 拉伸——
     * 等特异性后位覆盖回紧凑定宽（120px 来自 .__mc-ovr-add input 规则）。 */
    + '#__mc_overlay .__mc-ovr-add input{flex:0 0 auto;}'
    + '#__mc_overlay input:focus,#__mc_overlay select:focus{outline:1px solid #e8a33d;}'
    + '#__mc_overlay select{background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:5px 8px;font-size:12.5px;font-family:inherit;max-width:170px;}'
    /* 底部图例 */
    + '#__mc_overlay .__mc-foot{display:flex;align-items:center;gap:18px;padding:9px 18px;border-top:1px solid #2a2f3d;background:#1d212c;font-size:12px;color:#6b7180;}'
    + '#__mc_overlay .__mc-foot .__mc-lg{display:flex;gap:14px;}'
    + '#__mc_overlay .__mc-foot .__mc-foot-note{margin-left:auto;}'
    /* toast（挂在 overlay 内右下角；z-index:10=原型 .mc-toast 层序——浮层弹窗(9)/待生效条(5)之上） */
    + '#__mc_overlay .__mc-toast{position:absolute;right:18px;bottom:16px;background:rgba(15,18,25,.94);border:1px solid #4ade80;color:#e8e6e0;border-radius:10px;padding:10px 16px;font-size:13px;display:none;box-shadow:0 8px 30px rgba(0,0,0,.63);line-height:1.5;max-width:60%;z-index:10;}'
    + '#__mc_overlay .__mc-toast.__mc-show{display:block;}'
    + '#__mc_overlay .__mc-toast.__mc-err{border-color:#f87171;}'
    + '#__mc_overlay .__mc-toast small{color:#9aa0ad;display:block;font-size:11.5px;}'
    + '#__mc_overlay .__mc-loading{padding:40px 0;text-align:center;color:#6b7180;font-size:13px;letter-spacing:1px;}'
    /* ---- v2 任务 7：行内图标徽标（原型 .mc-ics/.mc-ic 及色级，样式值照抄） ----
     * 18px 圆片：⚠红冲突/⇄橙碰撞/◎灰同域/↑蓝可更新/↓橙可降级/⊘橙版本告警/⟳琥珀待生效。
     * span 非 button：图标落在 mod 行（div+data-interactive）矩形内，点击可达性由行承载。 */
    + '#__mc_overlay .__mc-ics{display:flex;align-items:center;gap:3px;flex-shrink:0;}'
    + '#__mc_overlay .__mc-ic{width:18px;height:18px;border-radius:50%;display:inline-flex;align-items:center;justify-content:center;font-size:9.5px;line-height:1;border:1px solid;cursor:pointer;font-family:"Segoe UI Symbol",inherit;position:relative;}'
    + '#__mc_overlay .__mc-ic:hover{filter:brightness(1.25);}'
    + '#__mc_overlay .__mc-ic.__mc-red{color:#f87171;border-color:rgba(248,113,113,.45);background:rgba(248,113,113,.1);}'
    + '#__mc_overlay .__mc-ic.__mc-orange{color:#fbbf24;border-color:rgba(251,191,36,.45);background:rgba(251,191,36,.08);}'
    + '#__mc_overlay .__mc-ic.__mc-gray{color:#8a90a0;border-color:rgba(138,144,160,.4);background:rgba(138,144,160,.08);}'
    + '#__mc_overlay .__mc-ic.__mc-blue{color:#60a5fa;border-color:rgba(96,165,250,.5);background:rgba(96,165,250,.1);}'
    + '#__mc_overlay .__mc-ic.__mc-amber{color:#e8a33d;border-color:rgba(232,163,61,.5);background:rgba(232,163,61,.1);cursor:default;}'
    /* v2 任务 12（T11 M7）：停用行的冲突/碰撞/同域徽标保留信息量但弱化（透明度降低，tip 已带"（已停用）"） */
    + '#__mc_overlay .__mc-ic.__mc-weak{opacity:.45;}'
    /* ---- v2 任务 7：通用按钮（原型 .btn/.pri/.mini；弹窗与待生效条共用，T8 仓库页复用） ---- */
    + '#__mc_overlay .__mc-btn{background:#0f1219;border:1px solid #2a2f3d;color:#9aa0ad;border-radius:7px;padding:6px 14px;font-size:12.5px;cursor:pointer;font-family:inherit;}'
    + '#__mc_overlay .__mc-btn:hover{border-color:#e8a33d;color:#e8a33d;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-pri{border-color:rgba(232,163,61,.5);color:#e8a33d;background:rgba(232,163,61,.1);}'
    + '#__mc_overlay .__mc-btn.__mc-btn-pri:hover{background:rgba(232,163,61,.18);}'
    + '#__mc_overlay .__mc-btn:disabled{opacity:.4;cursor:not-allowed;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-mini{padding:3px 10px;font-size:11.5px;}'
    /* ---- v2 任务 7：底部待生效条（原型 .mc-pending；面板内绝对定位，压在底栏图例上） ---- */
    + '#__mc_overlay .__mc-pending{position:absolute;left:0;right:0;bottom:0;display:none;align-items:center;gap:12px;padding:9px 18px;background:#1d212c;border-top:1px solid rgba(232,163,61,.4);font-size:12.5px;z-index:5;}'
    + '#__mc_overlay .__mc-pending.__mc-show{display:flex;}'
    + '#__mc_overlay .__mc-sp{flex:1;}'
    /* ---- v2 任务 7：面板内浮层弹窗（原型 .mc-modal-bg/.mc-modal；inset:0 展开为四向 0 长属性，
     * 计算值相同——旧 CEF 不识别 inset 简写） ---- */
    + '#__mc_overlay .__mc-modal-bg{position:absolute;left:0;top:0;right:0;bottom:0;background:rgba(5,6,10,.66);display:none;align-items:center;justify-content:center;z-index:9;}'
    + '#__mc_overlay .__mc-modal-bg.__mc-show{display:flex;}'
    + '#__mc_overlay .__mc-modal{width:520px;max-width:92%;max-height:86%;overflow-y:auto;background:#1d212c;border:1px solid #2a2f3d;border-radius:12px;padding:18px 20px;box-shadow:0 22px 80px rgba(0,0,0,.8);}'
    + '#__mc_overlay .__mc-modal h3{font-size:15.5px;margin-bottom:4px;display:flex;align-items:center;gap:8px;}'
    + '#__mc_overlay .__mc-m-sub{font-size:11.5px;color:#6b7180;margin-bottom:12px;}'
    + '#__mc_overlay .__mc-m-sec{margin-top:12px;}'
    + '#__mc_overlay .__mc-m-h{font-size:11.5px;color:#e8a33d;letter-spacing:2px;margin-bottom:6px;}'
    + '#__mc_overlay .__mc-m-txt{font-size:12.5px;color:#9aa0ad;line-height:1.75;}'
    + '#__mc_overlay .__mc-m-acts{display:flex;gap:8px;flex-wrap:wrap;margin-top:14px;}'
    /* ---- v2 任务 8：套件仓库页（原型 .mc-repo-entry/.mc-repo-top/.mc-repo-row/.mc-rep-hd/
     * .mc-risk/.mc-ok/.btn.blue/.btn.org/.mc-modal .files——样式值照抄，类名 __mc 化；
     * 行内 .sp 复用 T7 .__mc-sp{flex:1}。kv=弹窗版本对比行（原型 .mc-kv/.v）；
     * dlbar=下载进度条（T8 新增，无原型对应：琥珀填充+0.2s 过渡）。 ---- */
    + '#__mc_overlay .__mc-repo-entry{margin:4px 6px 8px;padding:10px 12px;border-radius:10px;border:1px solid rgba(96,165,250,.3);background:rgba(96,165,250,.07);cursor:pointer;display:flex;align-items:center;gap:10px;}'
    + '#__mc_overlay .__mc-repo-entry:hover{background:rgba(96,165,250,.12);}'
    + '#__mc_overlay .__mc-repo-entry .__mc-re-tt{font-size:12.5px;color:#60a5fa;}'
    + '#__mc_overlay .__mc-repo-entry .__mc-re-dd{font-size:11px;color:#6b7180;margin-top:3px;display:flex;align-items:center;gap:5px;}'
    /* ---- T16：启动清理组件缺失告警（壳 MCShell.dll 不在 patchers；更新/卸载/排序/停用失效） ---- */
    + '#__mc_overlay .__mc-shellwarn{margin:4px 6px 8px;padding:9px 12px;border-radius:9px;border:1px solid rgba(248,113,113,.45);background:rgba(248,113,113,.1);color:#f87171;font-size:11.5px;line-height:1.55;}'
    /* ---- v2 任务 12：L2 补丁碰撞汇总行（仓库入口卡下方；点击弹汇总窗） ---- */
    + '#__mc_overlay .__mc-patch-entry{margin:0 6px 8px;padding:8px 12px;border-radius:10px;border:1px solid rgba(251,191,36,.28);background:rgba(251,191,36,.05);cursor:pointer;display:flex;align-items:center;gap:8px;}'
    + '#__mc_overlay .__mc-patch-entry:hover{background:rgba(251,191,36,.1);}'
    + '#__mc_overlay .__mc-patch-entry .__mc-pe-tt{font-size:12px;color:#fbbf24;}'
    + '#__mc_overlay .__mc-patch-entry .__mc-pe-dd{font-size:10.5px;color:#6b7180;margin-top:2px;}'
    + '#__mc_overlay .__mc-scan-grp{border-top:1px solid #2a2f3d;padding-top:9px;margin-top:9px;}'
    + '#__mc_overlay .__mc-scan-grp:first-child{border-top:0;padding-top:0;margin-top:0;}'
    + '#__mc_overlay .__mc-scan-m{font-family:Consolas,monospace;font-size:12.5px;color:#e8e6e0;}'
    + '#__mc_overlay .__mc-scan-o{font-size:11.5px;color:#9aa0ad;margin-top:3px;line-height:1.6;}'
    + '#__mc_overlay .__mc-scan-n{font-size:10.5px;color:#6b7180;margin-top:2px;}'
    + '#__mc_overlay .__mc-repo-top{display:flex;align-items:center;gap:10px;margin-bottom:12px;}'
    + '#__mc_overlay .__mc-repo-top .__mc-lc{margin-left:auto;font-size:11px;color:#6b7180;}'
    + '#__mc_overlay .__mc-repo-row{display:flex;align-items:center;gap:12px;padding:9px 12px;border-radius:9px;border:1px solid transparent;}'
    + '#__mc_overlay .__mc-repo-row:hover{background:rgba(255,255,255,.03);}'
    + '#__mc_overlay .__mc-repo-row .__mc-rn{width:170px;font-size:13.5px;flex-shrink:0;}'
    + '#__mc_overlay .__mc-repo-row .__mc-cols{font-family:Consolas,monospace;font-size:12px;color:#9aa0ad;flex-shrink:0;}'
    + '#__mc_overlay .__mc-repo-row .__mc-cols b{color:#4ade80;font-weight:400;}'
    + '#__mc_overlay .__mc-repo-row .__mc-cols i{color:#6b7180;font-style:normal;}'
    + '#__mc_overlay .__mc-repo-row .__mc-cols i.__mc-failtx{color:#f87171;}' /* 检查失败（Remote=null 专用，绝不显示"已是最新"） */
    + '#__mc_overlay .__mc-repo-row .__mc-why{font-size:11px;color:#f87171;}'
    + '#__mc_overlay .__mc-repo-row .__mc-vernote{font-size:10.5px;color:#8a90a0;cursor:default;}' /* 必修 6：D8 未校验注记行内可见 */
    + '#__mc_overlay .__mc-rep-hd{display:flex;gap:14px;padding:6px 12px;font-size:11px;color:#6b7180;letter-spacing:1px;}'
    + '#__mc_overlay .__mc-ok{color:#4ade80;font-size:11.5px;}'
    + '#__mc_overlay .__mc-risk{margin-top:10px;padding:8px 12px;border-radius:8px;font-size:11.5px;line-height:1.6;background:rgba(127,29,29,.18);border:1px solid rgba(248,113,113,.25);color:#fca5a5;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-blue{border-color:rgba(96,165,250,.45);color:#60a5fa;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-blue:hover{background:rgba(96,165,250,.1);}'
    + '#__mc_overlay .__mc-btn.__mc-btn-org{border-color:rgba(251,191,36,.5);color:#fbbf24;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-org:hover{background:rgba(251,191,36,.1);}'
    + '#__mc_overlay .__mc-modal .__mc-files{font-family:Consolas,monospace;font-size:11.5px;color:#9aa0ad;line-height:1.9;}' /* T7 遗留：原型 .mc-modal .files（T10 卸载明细复用） */
    + '#__mc_overlay .__mc-modal .__mc-files .__mc-file-del{color:#f87171;}' /* T10 fix I-1：文件行独立类名，避开 .__mc-del 的 26px 按钮盒样式 */
    + '#__mc_overlay .__mc-kv .__mc-kk{color:#9aa0ad;width:88px;flex-shrink:0;}' /* T10 维护动作行标签（原型 .mc-kv .k 同值） */
    + '#__mc_overlay .__mc-maint{margin-top:20px;}' /* T10 维护动作区（视觉同 .__mc-grp 段；类名隔离保夹具 .__mc-grp=config 组语义） */
    + '#__mc_overlay .__mc-maint>h3{font-size:12.5px;color:#e8a33d;letter-spacing:2px;padding-bottom:7px;border-bottom:1px solid #2a2f3d;margin-bottom:4px;font-weight:500;}'
    + '#__mc_overlay .__mc-btn.__mc-btn-danger{border-color:rgba(248,113,113,.4);color:#f87171;}' /* T10 卸载确认主钮（原型 .btn.danger） */
    + '#__mc_overlay .__mc-btn.__mc-btn-danger:hover{background:rgba(248,113,113,.1);}'
    + '#__mc_overlay .__mc-pending.__mc-weak{border-top-color:rgba(107,113,128,.4);}' /* T10：仅中性档（生效中）时的弱化样式 */
    + '#__mc_overlay .__mc-kv{display:flex;align-items:center;gap:10px;padding:7px 0;font-size:13px;flex-wrap:wrap;}' /* 弹窗版本对比行（原型 .mc-kv/.v；padding 同原型） */
    + '#__mc_overlay .__mc-kv .__mc-kvv{font-family:Consolas,monospace;font-size:12.5px;}'
    + '#__mc_overlay .__mc-dlbar{height:6px;border-radius:3px;background:#0f1219;border:1px solid #2a2f3d;overflow:hidden;margin-top:8px;}'
    + '#__mc_overlay .__mc-dlbar i{display:block;height:100%;background:#e8a33d;width:0;transition:width .2s;}'
    /* ---- v2 任务 9：加载排序模式（原型 .mc-ordbar/.mc-side-foot/.mc-grip/.mc-row.dragging/
     * .drop-above/.drop-below/.ver——样式值照抄，类名 __mc 化；插入线=琥珀 box-shadow 上下缘） ---- */
    + '#__mc_overlay .__mc-ordbar{margin:4px 6px 8px;padding:9px 12px;border-radius:10px;border:1px dashed rgba(232,163,61,.45);font-size:11px;color:#9aa0ad;line-height:1.7;}'
    + '#__mc_overlay .__mc-ordbar .__mc-ord-acts{display:flex;gap:6px;margin-top:7px;}'
    + '#__mc_overlay .__mc-ordbar .__mc-ord-prot{margin-top:6px;color:#fbbf24;line-height:1.6;}' /* 必修 9：硬依赖保护披露 */
    + '#__mc_overlay .__mc-side-foot{margin-top:auto;padding:8px 6px 4px;border-top:1px solid #2a2f3d;}'
    + '#__mc_overlay .__mc-side-foot button{width:100%;background:#0f1219;border:1px solid #2a2f3d;color:#9aa0ad;border-radius:8px;padding:7px 0;font-size:12px;cursor:pointer;font-family:inherit;}'
    + '#__mc_overlay .__mc-side-foot button:hover{border-color:#e8a33d;color:#e8a33d;}'
    + '#__mc_overlay .__mc-grip{color:#4b5261;font-size:11px;letter-spacing:-2px;cursor:grab;flex-shrink:0;padding:0 2px;}'
    + '#__mc_overlay .__mc-mod-row.__mc-ord-row.__mc-dragging{opacity:.35;}'
    + '#__mc_overlay .__mc-mod-row.__mc-ord-row.__mc-dragging .__mc-grip{cursor:grabbing;}'
    + '#__mc_overlay .__mc-mod-row.__mc-ord-row.__mc-drop-above{box-shadow:0 -2px 0 0 #e8a33d;}'
    + '#__mc_overlay .__mc-mod-row.__mc-ord-row.__mc-drop-below{box-shadow:0 2px 0 0 #e8a33d;}'
    + '#__mc_overlay .__mc-ver{font-size:10.5px;color:#6b7180;font-family:Consolas,monospace;flex-shrink:0;}' /* 序号+版本两用（原型 .ver） */
    + '#__mc_overlay .__mc-side::-webkit-scrollbar,#__mc_overlay .__mc-cfg::-webkit-scrollbar{width:8px;}'
    + '#__mc_overlay .__mc-side::-webkit-scrollbar-thumb,#__mc_overlay .__mc-cfg::-webkit-scrollbar-thumb{background:#2a2f3d;border-radius:4px;}';

  function ensureStyle(doc) {
    if (doc.getElementById('__mc_panel_style')) return;
    var st = doc.createElement('style');
    st.id = '__mc_panel_style';
    st.textContent = CSS;
    (doc.head || doc.documentElement).appendChild(st);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function fmt(v) { var n = +v; return isNaN(n) ? String(v) : String(Math.round(n * 1000) / 1000); }
  function badgeOf(b) { return BADGE[b] || BADGE.cfg; }

  /* ================= 面板骨架 ================= */

  function build(doc) {
    ensureStyle(doc);
    var ov = doc.createElement('div');
    ov.id = '__mc_overlay';
    ov.setAttribute('data-interactive', ''); // 全屏矩形进 clickThrough 同步 → 点击落 WebView 且不穿透
    var pn = doc.createElement('div');
    pn.id = '__mc_panel';
    pn.style.position = 'relative'; // toast 锚点

    var head = doc.createElement('div'); head.className = '__mc-head';
    var t = doc.createElement('div'); t.className = '__mc-title'; t.textContent = 'MOD \u7ba1\u7406\u4e2d\u5fc3';
    var sub = doc.createElement('div'); sub.className = '__mc-sub'; sub.textContent = '\u6539\u52a8\u5b9e\u65f6\u5199\u5165 cfg \u00b7 \u65e0\u9700\u91cd\u542f';
    var x = doc.createElement('button');
    x.type = 'button'; x.className = '__mc-close'; x.setAttribute('aria-label', '\u5173\u95ed\u9762\u677f'); x.textContent = '\u2715';
    x.addEventListener('click', function (e) { e.stopPropagation(); window.__mcClose(); });
    /* v3：头部「检查更新」= 全局唯一检查入口（60 秒冷却在 C#）。点击置 chkWaiting，
     * MC_REPO_DATA 到达时在 __mcEvent 拼汇总 toast；冷却内 C# 只推数据（自己再补 toast）。 */
    var chk = doc.createElement('button');
    chk.type = 'button'; chk.className = '__mc-btn'; chk.textContent = '\u68c0\u67e5\u66f4\u65b0';
    chk.style.marginLeft = 'auto';
    chk.addEventListener('click', function (e) {
      e.stopPropagation();
      if (ST.chkWaiting) return;
      ST.chkWaiting = true;
      window.__mcOutbox.push({ type: 'MC_CHK' });
      toastRaw('\u6b63\u5728\u68c0\u67e5\u66f4\u65b0\u2026', '');
    });
    x.style.marginLeft = '0';
    head.appendChild(t); head.appendChild(sub); head.appendChild(chk); head.appendChild(x);

    var body = doc.createElement('div'); body.className = '__mc-body';
    var side = doc.createElement('div'); side.className = '__mc-side'; side.id = '__mc_side';
    var cfg = doc.createElement('div'); cfg.className = '__mc-cfg'; cfg.id = '__mc_cfg';
    body.appendChild(side); body.appendChild(cfg);

    var foot = doc.createElement('div'); foot.className = '__mc-foot';
    foot.innerHTML = '<span class="__mc-lg">'
      + '<span class="__mc-b-now">\u26a1 \u7acb\u5373\u751f\u6548\uff08\u22645\u79d2\uff09</span>'
      + '<span class="__mc-b-reload">\ud83d\udcc1 \u8bfb\u6863\u540e\u751f\u6548</span>'
      + '<span class="__mc-b-reboot">\ud83d\udd04 \u91cd\u542f\u540e\u751f\u6548</span>'
      + '<span class="__mc-b-cfg">\u2699 \u5df2\u5199\u5165 cfg\uff08\u751f\u6548\u89c6\u8be5 mod\uff09</span>'
      + '</span><span class="__mc-foot-note">\u5957\u4ef6\u505c\u7528 = \u7acb\u5373\u8f6f\u505c + \u4e0b\u6b21\u542f\u52a8\u4e0d\u52a0\u8f7d\uff1b\u7b2c\u4e09\u65b9\u505c\u7528 = \u91cd\u542f\u7ea7</span>';

    var toastEl = doc.createElement('div');
    toastEl.className = '__mc-toast'; toastEl.id = '__mc_toast';

    /* v2 任务 7：底部待生效条（原型 #pend 同构；v2 任务 13 撤销/立即重启两钮接真） */
    var pendEl = doc.createElement('div');
    pendEl.className = '__mc-pending'; pendEl.id = '__mc_pending';
    pendEl.innerHTML = '<span aria-hidden="true">\u27f3</span>'
      + '<span class="__mc-pend-tx" id="__mc_pend_tx"></span><span class="__mc-sp"></span>'
      + '<button type="button" class="__mc-btn __mc-btn-mini" data-x="undo">\u64a4\u9500</button>'
      + '<button type="button" class="__mc-btn __mc-btn-pri __mc-btn-mini" data-x="reboot">\u7acb\u5373\u91cd\u542f</button>';
    /* v2 任务 13：两钮动作——撤销=MC_PENDING_UNDO（C# 清 pending.json 后推快照+摘要+toast）；
     * 立即重启=确认弹窗（openRebootModal）→ MC_RESTART。面板不做乐观提示（结果一律等 C# 回推，
     * 拉起失败不退出——不假装成功）。 */
    pendEl.addEventListener('click', function (e) {
      var b = e.target && e.target.closest ? e.target.closest('[data-x]') : null;
      if (!b) return;
      var x = b.getAttribute('data-x');
      if (x === 'undo') { window.__mcOutbox.push({ type: 'MC_PENDING_UNDO' }); return; }
      if (x === 'reboot') openRebootModal();
    });

    /* v2 任务 7：面板内浮层弹窗（裁定 1：绝对定位+半透明底，不用 window.alert；ESC/点底关闭） */
    var mbg = doc.createElement('div');
    mbg.className = '__mc-modal-bg'; mbg.id = '__mc_modal_bg';
    var mdl = doc.createElement('div');
    mdl.className = '__mc-modal'; mdl.id = '__mc_modal';
    mbg.appendChild(mdl);
    mbg.addEventListener('click', function (e) { if (e.target === mbg) closeModal(); });

    pn.appendChild(head); pn.appendChild(body); pn.appendChild(foot);
    pn.appendChild(pendEl); pn.appendChild(mbg); pn.appendChild(toastEl);
    ov.appendChild(pn);
    /* 点遮罩空白处关闭（面板本体不关） */
    ov.addEventListener('click', function (e) { if (e.target === ov) window.__mcClose(); });

    wireSide(side);
    wireCfg(cfg);

    S.side = side; S.cfg = cfg; S.sub = sub; S.toastEl = toastEl;
    S.pendEl = pendEl; S.pendTx = pendEl.querySelector('#__mc_pend_tx'); S.mbg = mbg; S.mdl = mdl;
    return ov;
  }

  /* ================= 数据访问 ================= */

  function mods() { return ST.snap ? ST.snap.mods : []; }
  function tps() { return ST.snap ? ST.snap.tp : []; }
  function findMod(guid) {
    var i, ms = mods();
    for (i = 0; i < ms.length; i++) if (ms[i].guid === guid) return ms[i];
    var ts = tps();
    for (i = 0; i < ts.length; i++) if (ts[i].guid === guid) return ts[i];
    return null;
  }
  function isTpMod(m) { return tps().indexOf(m) !== -1; }
  function curMod() {
    if (ST.sel === '__self') return null;
    var m = ST.sel ? findMod(ST.sel) : null;
    return m || mods()[0] || null;
  }
  function itemOf(m, g, i) { return (m.groups[g] && m.groups[g].items[i]) || null; }
  function clampItem(it, v) {
    if (typeof it.min === 'number' && v < it.min) v = it.min;
    if (typeof it.max === 'number' && v > it.max) v = it.max;
    return v;
  }

  /* ================= 左列 ================= */

  function swBtn(on, attrs, big) {
    return '<button type="button" class="__mc-mini-sw' + (big ? ' __mc-big' : '') + (on ? ' __mc-on' : '') + '"'
      + attrs + ' aria-checked="' + !!on + '"></button>';
  }

  /* ================= v2 任务 7：行内图标徽标 ================= */

  /* 字形推断：g 显式优先；orange 需 click 消歧（cf=⇄碰撞 / upd=↓降级）；其余按色级。
   * （↓ 与 ⊘ 同为 orange+upd——喂 ⊘ 时 DTO 带 g:"kbw"（T8 裁定 4 的消歧别名，此处映射为
   * ⊘ 字形；也兼容直接给字形符的旧口径），见 Payload.IcInfo） */
  function icGlyph(ic) {
    if (ic.g) return ic.g === 'kbw' ? '\u2298' : ic.g; // kbw → ⊘ 版本告警
    if (ic.k === 'orange') return ic.click === 'cf' ? '\u21c4' : '\u2193'; // ⇄ / ↓
    if (ic.k === 'red') return '\u26a0';       // ⚠ 冲突
    if (ic.k === 'gray') return '\u25ce';      // ◎ 同域
    if (ic.k === 'blue') return '\u2191';      // ↑ 可更新
    if (ic.k === 'amber') return '\u27f3';     // ⟳ 待生效（纯状态，无 click）
    return '\u26a0';
  }

  /* mod 行 ics 容器（原型 appendRow 的 '<span class="mc-ics">' 段）：空/缺省 ics 返回 ''
   * —— 行渲染与 v1 完全一致（快照协议向后兼容硬门槛）。v2 任务 12：w:true（停用行）→ __mc-weak。 */
  function icsHtml(m) {
    var ics = m && m.ics;
    if (!ics || !ics.length) return '';
    var h = '<span class="__mc-ics">';
    for (var i = 0; i < ics.length; i++) {
      var ic = ics[i];
      if (!ic || !ic.k || !ic.tip) continue;
      h += '<span class="__mc-ic __mc-' + esc(ic.k) + (ic.w ? ' __mc-weak' : '') + '" data-guid="' + esc(m.guid) + '"'
        + (ic.click ? ' data-click="' + esc(ic.click) + '"' : '')
        + ' title="' + esc(ic.tip) + '" aria-label="' + esc(ic.tip) + '">'
        + esc(icGlyph(ic)) + '</span>';
    }
    return h + '</span>';
  }

  /* ---- v2 任务 8：仓库数据访问（repoCounts 供头部检查完成汇总 toast 用；v3 无独立仓库页） ---- */

  function repoCounts() {
    var d = ST.repoData, c = { upd: 0, inst: 0, warn: 0 };
    if (!d || !d.rows) return c;
    for (var i = 0; i < d.rows.length; i++) {
      var r = d.rows[i];
      if (r.pending) continue; // 待生效行不再计入可更新
      if (r.status === 'update') c.upd++;
      else if (r.status === 'install') c.inst++;
      else if (r.status === 'warn') c.warn++;
    }
    return c;
  }

  /* T16（I-2）：启动清理组件缺失横幅——快照 shellWarn 驱动；侧栏/详情页/仓库视图顶部共用。 */
  function shellWarnHtml() {
    var w = ST.snap && ST.snap.shellWarn;
    if (!w) return '';
    return '<div class="__mc-shellwarn" data-interactive="1">\u26a0 ' + esc(w) + '</div>';
  }

  /* v3：仓库入口卡/openRepoView/补丁碰撞汇总行已随独立仓库页退役（spec §5.3/D5）——
   * 顶部「检查更新」+ 底部「补丁碰撞 N 组」接管两个入口。 */

  function renderSide() {
    if (!S.side) return;
    if (ST.ord) { renderSideOrder(); return; } // v2 任务 9：排序模式=侧栏完整变体分支
    var ms = mods(), ts = tps();
    var h = shellWarnHtml(); // T16 I-2：缺壳告警置顶
    h += '<div class="__mc-cat">\u5957\u4ef6 MOD\uff08' + ms.length + '\uff09</div>';
    for (var i = 0; i < ms.length; i++) {
      var m = ms[i];
      h += '<div class="__mc-mod-row' + (m.enabled ? '' : ' __mc-off')
        + (ST.sel === m.guid ? ' __mc-sel' : '') + '" data-interactive data-guid="' + esc(m.guid) + '">'
        + '<span class="__mc-st"></span><span class="__mc-nm">' + esc(m.name) + '</span>'
        + icsHtml(m)
        /* 必修 7：dll 不在场（未安装）→ 开关锁定，去仓库安装走详情页入口 */
        + (m.missing
          ? '<span class="__mc-mini-sw __mc-lock" title="\u672a\u5b89\u88c5\uff1a\u8bf7\u5230\u300c\u5957\u4ef6\u4ed3\u5e93\u300d\u5b89\u88c5"></span>'
          : swBtn(m.enabled, ' data-act="toggle" data-guid="' + esc(m.guid) + '"')) + '</div>';
    }
    h += '<div class="__mc-cat">\u9762\u677f\u81ea\u8eab</div>';
    h += '<div class="__mc-mod-row __mc-lock' + (ST.sel === '__self' ? ' __mc-sel' : '')
      + '" data-interactive data-guid="__self">'
      + '<span class="__mc-st" style="background:#e8a33d;"></span><span class="__mc-nm">' + esc(SELF_NAME) + '</span>'
      + '<span class="__mc-mini-sw __mc-lock" title="\u9632\u5446\uff1a\u9762\u677f\u4e0d\u53ef\u505c\u7528\u81ea\u8eab"></span></div>';
    h += '<div class="__mc-cat">\u7b2c\u4e09\u65b9 MOD\uff08\u901a\u7528 / \u58f0\u660e\u5f0f\uff09</div>';
    if (ts.length === 0) {
      h += '<div class="__mc-tp-empty">\uff08\u6682\u65e0\u7b2c\u4e09\u65b9 mod \u63a5\u5165\uff09</div>';
    } else {
      for (var j = 0; j < ts.length; j++) {
        var t = ts[j];
        h += '<div class="__mc-mod-row' + (t.enabled ? '' : ' __mc-off')
          + (ST.sel === t.guid ? ' __mc-sel' : '') + '" data-interactive data-guid="' + esc(t.guid) + '">'
          + '<span class="__mc-st"></span><span class="__mc-nm">' + esc(t.name) + '</span>'
          + icsHtml(t)
          + swBtn(t.enabled, ' data-act="toggle" data-guid="' + esc(t.guid) + '"') + '</div>';
      }
    }
    h += sideFootHtml(false); // v2 任务 9：底部"加载排序模式"入口（两模式常驻，原型同款）
    S.side.innerHTML = h;
  }

  /* ---- v2 任务 9：排序模式侧栏（原型排序段照抄） ---- */

  /* 底部按钮（常驻两模式；文字随模式切换——原型 fb.textContent 同款） */
  function sideFootHtml(ordMode) {
    return '<div class="__mc-side-foot"><button type="button" data-act="ordtoggle">'
      + (ordMode ? '\u9000\u51fa\u6392\u5e8f\u6a21\u5f0f' : '\u52a0\u8f7d\u6392\u5e8f\u6a21\u5f0f')
      + '</button></div>';
  }

  /* 行显示元数据：名/启停态取快照行（mods∪tp，中文定制名），版本取 C# rows（快照 ModInfo
   * 不带版本——MC_ORDER_DATA.rows 是行内版本列唯一数据源）；快照外的行（ModCenter 自身等）
   * 名也回退 rows。 */
  function ordInfoOf(guid) {
    var name = guid, ver = '', enabled = true;
    var rows = ST.ordRows && ST.ordRows.rows;
    if (rows) for (var i = 0; i < rows.length; i++)
      if (rows[i] && rows[i].guid === guid) { name = rows[i].name || guid; ver = rows[i].ver || ''; break; }
    var m = findMod(guid);
    if (m) { name = m.name; enabled = m.enabled !== false; }
    return { m: m, name: name, ver: ver, enabled: enabled };
  }

  /* 排序行 ics：只留冲突 chip（click==="cf"：⚠红/⇄橙/◎灰），显示但无 click（裁定 4——
   * cursor:default、不弹窗不选行）；⟳/↑/↓/⊘ 一律不显示（order 是全局意图，不挂行）。 */
  function ordIcsHtml(info) {
    var ics = info.m && info.m.ics;
    if (!ics || !ics.length) return '';
    var h = '';
    for (var i = 0; i < ics.length; i++) {
      var ic = ics[i];
      if (!ic || !ic.k || !ic.tip || ic.click !== 'cf') continue;
      h += '<span class="__mc-ic __mc-' + esc(ic.k) + (ic.w ? ' __mc-weak' : '') + '" style="cursor:default" title="' + esc(ic.tip)
        + '" aria-label="' + esc(ic.tip) + '">' + esc(icGlyph(ic)) + '</span>';
    }
    return h ? '<span class="__mc-ics">' + h + '</span>' : '';
  }

  function renderSideOrder() {
    /* T10 带出③（T9）：MC_ORDER_DATA 未到（ST.order 空）时两钮禁用——防"恢复默认/保存顺序"
     * 在无数据态空转；数据到达（MC_ORDER_DATA 分支重渲染）自动解禁。 */
    var dis = ST.order ? '' : ' disabled';
    /* 必修 9：清理器 order_protected.txt 披露（MC_ORDER_DATA.prot=[{g,d}]）——按 guid 聚合
     * "被硬依赖保护 N 项：名称（依赖 xxx）"；无文件/空表不显示。 */
    var prot = (ST.ordRows && ST.ordRows.prot) || [];
    var protHtml = '';
    if (prot.length) {
      var byG = {}, orderG = [];
      for (var pi = 0; pi < prot.length; pi++) {
        var pg = prot[pi] && prot[pi].g, pd = prot[pi] && prot[pi].d;
        if (!pg || !pd) continue;
        if (!byG[pg]) { byG[pg] = []; orderG.push(pg); }
        if (byG[pg].indexOf(pd) === -1) byG[pg].push(pd);
      }
      if (orderG.length) {
        var parts = [];
        for (var oi = 0; oi < orderG.length; oi++) {
          var g0 = orderG[oi];
          var nm = ordInfoOf(g0).name;
          var deps = byG[g0].map(function (x) { return ordInfoOf(x).name; }).join('\u3001');
          parts.push(nm + '\uff08\u4f9d\u8d56 ' + deps + '\uff09');
        }
        protHtml = '<div class="__mc-ord-prot">\u88ab\u786c\u4f9d\u8d56\u4fdd\u62a4 ' + orderG.length
          + ' \u9879\uff1a' + esc(parts.join('\u3001')) + '</div>';
      }
    }
    var h = shellWarnHtml() + '<div class="__mc-ordbar">'
      + '<b style="color:#e8a33d;font-weight:500">\u6392\u5e8f\u6a21\u5f0f</b> \u2014 \u62d6\u62fd\u884c\u8c03\u6574\u987a\u5e8f\uff1b\u9009\u4e2d\u884c\u540e\u4e5f\u53ef\u7528 \u2191 \u2193 \u952e\u79fb\u52a8\uff08\u65e0\u969c\u788d\u66ff\u4ee3\uff09\u3002\u786c\u4f9d\u8d56\u94fe\u6761\u76ee\u81ea\u52a8\u4fdd\u6301\u4f9d\u8d56\u5e8f\u3002'
      + protHtml
      + '<div class="__mc-ord-acts">'
      + '<button type="button" class="__mc-btn __mc-btn-mini" data-act="ordreset"' + dis + '>\u6062\u590d\u9ed8\u8ba4</button>'
      + '<button type="button" class="__mc-btn __mc-btn-pri __mc-btn-mini" data-act="ordsave"' + dis + '>\u4fdd\u5b58\u987a\u5e8f</button>'
      + '</div></div>';
    h += '<div class="__mc-cat">\u52a0\u8f7d\u987a\u5e8f\uff08\u9ed8\u8ba4 = \u6a21\u7ec4 ID \u5b57\u6bcd\u5e8f\uff09</div>';
    if (!ST.order) {
      h += '<div class="__mc-loading">\u6b63\u5728\u8bfb\u53d6\u52a0\u8f7d\u987a\u5e8f\u2026</div>';
    } else {
      for (var i = 0; i < ST.order.length; i++) {
        var guid = ST.order[i], info = ordInfoOf(guid), n = i + 1;
        h += '<div class="__mc-mod-row __mc-ord-row' + (info.enabled ? '' : ' __mc-off')
          + '" data-interactive data-guid="' + esc(guid) + '" tabindex="0" draggable="true">'
          + '<span class="__mc-grip" aria-hidden="true">\u22ee\u22ee</span>'
          + '<span class="__mc-ver">' + (n < 10 ? '0' : '') + n + '</span>'
          + '<span class="__mc-nm">' + esc(info.name) + '</span>'
          + '<span class="__mc-ver">' + esc(info.ver) + '</span>'
          + '<span class="__mc-sp"></span>'
          + ordIcsHtml(info)
          + '</div>';
      }
    }
    h += sideFootHtml(true);
    S.side.innerHTML = h;
  }

  /* 排序模式进出：进=关仓库视图+清本地序+拉 MC_ORDER_GET（MC_ORDER_DATA 到达填 ST.order）；
   * 出=丢弃本地未保存重排。排序模式是面板内状态——不动键盘门（裁定 2）。 */
  function ordToggle() {
    if (ST.ord) {
      ST.ord = false; ST.order = null; ST.ordSaving = false; ORD.src = null;
      renderSide(); renderCfg();
      return;
    }
    ST.ord = true; ST.order = null; ST.ordSaving = false; ORD.src = null;
    renderSide(); renderCfg();
    window.__mcOutbox.push({ type: 'MC_ORDER_GET' });
  }

  /* 恢复默认：纯本地重排（def=GUID 字母序，MC_ORDER_DATA 随序下发）——保存仍走"保存顺序"
   *（裁定 1：存字母序与"删除 order 字段"等效，不另做协议）。 */
  function ordReset() {
    if (!ST.ordRows || !ST.ordRows.def || !ST.ordRows.def.length) return;
    ST.order = ST.ordRows.def.slice();
    renderSide();
    toastRaw('\u5df2\u6062\u590d\u9ed8\u8ba4\u987a\u5e8f\uff08ID \u5b57\u6bcd\u5e8f\uff09',
      '\u5c1a\u672a\u4fdd\u5b58\u2014\u2014\u70b9\u201c\u4fdd\u5b58\u987a\u5e8f\u201d\u540e\u5199\u5165\u5f85\u751f\u6548\u6e05\u5355');
  }

  function ordSave() {
    if (!ST.order || !ST.order.length || ST.ordSaving) return;
    ST.ordSaving = true; // 回执（OK/FAIL）前防连点
    window.__mcOutbox.push({ type: 'MC_ORDER_SAVE', guids: ST.order.slice() });
    toastRaw('\u6b63\u5728\u4fdd\u5b58\u52a0\u8f7d\u987a\u5e8f\u2026', '');
  }

  function ordIndexOf(guid) {
    if (!ST.order) return -1;
    for (var i = 0; i < ST.order.length; i++) if (ST.order[i] === guid) return i;
    return -1;
  }

  /* 重排纯函数（原型 reorder 同款）：from 抽出插到 to，越界/原位不动；重渲染由调用方。 */
  function ordReorderLocal(from, to) {
    if (!ST.order) return;
    if (from === to || from < 0 || to < 0 || from >= ST.order.length) return;
    var g = ST.order.splice(from, 1)[0];
    if (to > ST.order.length) to = ST.order.length;
    ST.order.splice(to, 0, g);
    renderSide();
  }

  function ordClearDrops() {
    if (!S.side) return;
    var rows = S.side.querySelectorAll('.__mc-ord-row');
    for (var i = 0; i < rows.length; i++) {
      rows[i].classList.remove('__mc-drop-above');
      rows[i].classList.remove('__mc-drop-below');
    }
  }

  function ordFocusRow(i) {
    if (!S.side) return;
    var rows = S.side.querySelectorAll('.__mc-ord-row');
    if (rows[i]) rows[i].focus();
  }

  function wireSide(side) {
    side.addEventListener('click', function (e) {
      /* v2 任务 9：排序模式动作（底按钮进出/工具条恢复默认/保存顺序）——先于行/图标分支 */
      var oa = e.target.closest
        ? e.target.closest('[data-act="ordtoggle"],[data-act="ordreset"],[data-act="ordsave"]') : null;
      if (oa) {
        e.stopPropagation();
        var a = oa.getAttribute('data-act');
        if (a === 'ordtoggle') ordToggle();
        else if (a === 'ordreset') ordReset();
        else if (a === 'ordsave') ordSave();
        return;
      }
      var sw = e.target.closest ? e.target.closest('[data-act="toggle"]') : null;
      if (sw) { e.stopPropagation(); toggleMod(sw.getAttribute('data-guid')); return; }
      /* v2 任务 7：图标点击路由（原型 wireSide 同款 stopPropagation——不选行）。
       * cf=冲突详情弹窗（T11 前兜底文案）/ upd=更新确认弹窗（T8 真数据版：按 guid 对照
       * 最近 MC_REPO_DATA；无仓库数据/检查失败回退 T7 兜底壳）；
       * 无 data-click（⟳ 纯状态，cursor:default）不弹窗不选行（点 ⟳ 无弹窗）。 */
      var ic = e.target.closest ? e.target.closest('.__mc-ic') : null;
      if (ic) {
        e.stopPropagation();
        var click = ic.getAttribute('data-click');
        if (!click) return;
        var m = findMod(ic.getAttribute('data-guid'));
        if (!m) return;
        if (click === 'cf') openCfModal(m);
        else if (click === 'upd') openUpdModal(m);
        return;
      }
      var row = e.target.closest ? e.target.closest('.__mc-mod-row') : null;
      /* v2 任务 9（裁定 4）：排序模式行点击不选行（不进 mod 详情/不弹窗） */
      if (row) {
        if (ST.ord) return;
        ST.sel = row.getAttribute('data-guid'); renderSide(); renderCfg();
      }
    });

    /* ---- v2 任务 9：拖拽（HTML5 DnD）+ 键盘替代（↑↓，WCAG 2.2 AA）----
     * 事件委托挂 side（renderSide innerHTML 重建后无需逐行重挂——原型 wireDnD 逐行挂，
     * 委托为等价实现）；仅 .__mc-ord-row 参与（draggable 只在排序行上置位）。
     * dataTransfer 空判：合成事件（CDP 测试）不带 dataTransfer 不炸，真拖拽路径不受影响。 */
    side.addEventListener('dragstart', function (e) {
      if (!ST.ord || !ST.order) return;
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      if (!row) return;
      ORD.src = row.getAttribute('data-guid'); // T10 带出①（T9）：存 guid 非 下标——拖拽会话中键盘重排会使下标错位
      if (e.dataTransfer) {
        try { e.dataTransfer.setData('text/plain', String(ORD.src)); } catch (_) {}
        e.dataTransfer.effectAllowed = 'move';
      }
      row.classList.add('__mc-dragging');
    });
    side.addEventListener('dragend', function (e) {
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      if (row) row.classList.remove('__mc-dragging');
      ordClearDrops();
      ORD.src = null;
    });
    side.addEventListener('dragover', function (e) {
      if (!ST.ord || ORD.src == null || !ST.order) return;
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      if (!row) return;
      e.preventDefault();
      if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
      ordClearDrops();
      var r = row.getBoundingClientRect();
      row.classList.add(e.clientY < r.top + r.height / 2 ? '__mc-drop-above' : '__mc-drop-below');
    });
    side.addEventListener('dragleave', function (e) {
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      if (row) { row.classList.remove('__mc-drop-above'); row.classList.remove('__mc-drop-below'); }
    });
    side.addEventListener('drop', function (e) {
      if (!ST.ord || ORD.src == null || !ST.order) return;
      e.preventDefault();
      ordClearDrops();
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      var srcGuid = ORD.src;
      ORD.src = null;
      if (!row || srcGuid == null) return;
      var src = ordIndexOf(srcGuid); // T10 带出①：drop 时按 guid 现算下标（会话中键盘重排已反映）
      if (src < 0) return;
      var r = row.getBoundingClientRect();
      var below = e.clientY >= r.top + r.height / 2;
      var to = ordIndexOf(row.getAttribute('data-guid')) + (below ? 1 : 0);
      if (to > src) to--; // 目标行在源之后：抽出源后整体前移一位（原型同款）
      ordReorderLocal(src, to);
    });
    side.addEventListener('keydown', function (e) {
      if (!ST.ord || !ST.order) return;
      if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
      var row = e.target.closest ? e.target.closest('.__mc-ord-row') : null;
      if (!row) return;
      e.preventDefault();
      var i = ordIndexOf(row.getAttribute('data-guid'));
      if (i < 0) return;
      var to = e.key === 'ArrowUp' ? i - 1 : i + 1;
      ordReorderLocal(i, to);
      ordFocusRow(to); // 重渲染后焦点跟到移动后的行（原型 focusRow 同款）
    });
  }

  /* ================= 右区 ================= */

  function ctlHtml(it, gi, ii) {
    if (it.t === 'bool') {
      return swBtn(!!it.v, ' data-act="set" data-g="' + gi + '" data-i="' + ii + '"', true);
    }
    if (it.t === 'sel') {
      var opts = (it.opts && it.opts.length) ? it.opts : [it.v];
      var o = '';
      for (var i = 0; i < opts.length; i++)
        o += '<option value="' + esc(opts[i]) + '"' + (opts[i] === it.v ? ' selected' : '') + '>' + esc(opts[i]) + '</option>';
      return '<select data-act="set" data-g="' + gi + '" data-i="' + ii + '">' + o + '</select>';
    }
    if (it.t === 'str') { // 任务 14：无选项 string 项（Tier 0 第三方）→ 文本框，change 提交
      return '<input type="text" value="' + esc(it.v) + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">';
    }
    /* Fix Round 1（评审 P2）：数值项 min/max 双全才出 滑条+数值框；无范围（tp 无
     * AcceptableValueRange 的 int/float）不再兜底捏造 0..100 滑条——界外真值（如 500）
     * 会被滑条钳到 100 显示、一拖即把 500 静默改成 ≤100。无范围/半范围=纯数值框
     * （指南 §二"数值无范围→数值框"），且 min/max 属性仅按实际存在的一端设置，
     * 界外值照实显示；钳制语义不变（clampItem 只按数据侧 min/max 钳）。 */
    /* T20（作者令：数值输入框一律纯文本、去原生加减钮、只允许数字）：type=text + inputmode=numeric
     * + data-num=int|float（wireCfg 输入清洗：int 剥非数字、float 再收敛到一个小数点；沿用实机 UI
     * 三修·问题3 的 ovr-add ID 框同款先例——游戏 WebView 的 number 框带原生加减钮遮挡/误触）。
     * min/max/step 属性不再需要（浏览器只对 number 生效）；钳制仍走数据侧 clampItem。 */
    var step = (it.st != null) ? it.st : 0.1;
    var hasMin = typeof it.min === 'number', hasMax = typeof it.max === 'number';
    var num = '<input type="text" class="__mc-numbox" inputmode="numeric"'
      + ' data-num="' + (it.t === 'float' ? 'float' : 'int') + '"'
      + ' value="' + fmt(it.v)
      + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">';
    if (!hasMin || !hasMax) return num;
    return '<input type="range" min="' + it.min + '" max="' + it.max + '" step="' + step + '" value="' + fmt(it.v)
      + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">' + num;
  }

  function itemHtml(m, it, gi, ii) {
    var bd = badgeOf(it.b);
    /* 任务 16：Tier 2 failed 状态（QueryStates 回报）→ 项置灰锁控件 + 红字原因（指南 §4.1）；
     * 必修 7：未安装页面（m.missing）整页控件只读（dll 不在场，写入必被拒）。 */
    return '<div class="__mc-item' + (it.fail ? ' __mc-failed' : '') + (m.missing ? ' __mc-missing' : '') + '">'
      + '<div class="__mc-info">'
      + '<div class="__mc-lb">' + esc(it.l) + '</div>'
      + '<div class="__mc-ck">' + esc(it.k) + '</div>'
      + (it.note ? '<div class="__mc-ck __mc-note">' + esc(it.note) + '</div>' : '')
      + (it.fail ? '<div class="__mc-ck __mc-fail-note">\u26a0 ' + esc(it.fail) + '</div>' : '')
      + '</div>'
      + '<div class="__mc-ctl">' + ctlHtml(it, gi, ii) + '</div>'
      + '<div class="__mc-badge ' + bd.cls + '">' + esc(bd.t) + '</div>'
      + '</div>';
  }

  function ovrHtml(m) {
    var h = '<div class="__mc-grp"><h3>\u52a8\u4f5c\u8986\u76d6\uff08\u5355\u4e2a\u52a8\u4f5c\u5355\u72ec\u8c03\uff09</h3>'
      + '<div class="__mc-ovr-add">'
      /* 实机 UI 三修·问题3：number 框在游戏 WebView 带原生加减钮（遮挡/误触）——改纯文本框
       * + inputmode=numeric（软键盘数字提示）；min 属性去除。parseInt 校验与防重复逻辑
       * 不变（wireCfg ovr-add 分支：!isNaN(id) && id>0 && !m.ovr.some(...)）。 */
      + '<input type="text" id="__mc_ovr_new" placeholder="\u52a8\u4f5c ID" inputmode="numeric">'
      + '<button type="button" data-act="ovr-add">\uff0b \u65b0\u589e\u8986\u76d6</button>'
      + '</div>';
    var ovr = m.ovr || [];
    if (!ovr.length) {
      h += '<div class="__mc-ovr-empty">\u6682\u65e0\u8986\u76d6\u3002\u65b0\u589e\u540e\u5728\u6b64\u5217\u51fa\uff0c\u6ed1\u6761\u5373\u65f6\u8c03\u6574\u3002</div>';
    } else {
      for (var idx = 0; idx < ovr.length; idx++) {
        h += '<div class="__mc-item">'
          + '<div class="__mc-info">'
          + '<div class="__mc-lb">\u52a8\u4f5c #' + ovr[idx].id + '</div>'
          + '<div class="__mc-ck">' + OVR_SECTION + '.' + OVR_KEY + ' \u00b7 ' + ovr[idx].id + ':' + ovr[idx].ms + '</div>'
          + '</div>'
          + '<div class="__mc-ctl">'
          + '<input type="range" min="0" max="5000" step="50" value="' + ovr[idx].ms + '" data-act="ovr-set" data-idx="' + idx + '">'
          + '<input type="text" class="__mc-numbox" inputmode="numeric" data-num="int" value="' + ovr[idx].ms + '" data-act="ovr-set" data-idx="' + idx + '">'
          + '</div>'
          + '<div class="__mc-badge __mc-b-now">\u26a1 \u7acb\u5373\u751f\u6548</div>'
          + '<button type="button" class="__mc-del" data-act="ovr-del" data-idx="' + idx + '" title="\u5220\u9664\u6b64\u8986\u76d6">\u2715</button>'
          + '</div>';
      }
    }
    return h + '</div>';
  }

  /* ---- T20 个性化柜子清单（柜子容量 Global.BagOverrides ↔ bagOvr:[{id,w,h}]）----
   * 与动作覆盖清单同构（宿主项整串序列化提交），差异=每行两个纯文本数值框（宽/高，
   * data-num=int 清洗 + 0..40/0..30 钳制）而非滑条；新增行默认 16x12。 */
  var BOV_SECTION = 'Global', BOV_KEY = 'BagOverrides';
  function serializeBov(m) {
    return (m.bagOvr || []).slice().sort(function (a, b) { return a.id - b.id; })
      .map(function (o) { return o.id + ':' + o.w + 'x' + o.h; }).join(',');
  }
  function bovCommit(m) {
    window.__mcOutbox.push({ type: 'MC_SET', guid: m.guid, section: BOV_SECTION, key: BOV_SECTION + '.' + BOV_KEY, value: serializeBov(m) });
  }
  function bagOvrHtml(m) {
    var h = '<div class="__mc-grp"><h3>\u4e2a\u6027\u5316\u4fee\u6539\uff1a\u4efb\u610f\u67dc\u5b50\u5355\u72ec\u8c03\uff08\u4f18\u5148\u7ea7\u6700\u9ad8\uff09</h3>'
      + '<div class="__mc-ovr-add">'
      + '<input type="text" id="__mc_bov_new" placeholder="\u67dc\u5b50 ID" inputmode="numeric" data-num="int">'
      + '<button type="button" data-act="bov-add">\uff0b \u65b0\u589e\u67dc\u5b50</button>'
      + '</div>';
    var bov = m.bagOvr || [];
    if (!bov.length) {
      h += '<div class="__mc-ovr-empty">\u6682\u65e0\u4e2a\u6027\u5316\u6761\u76ee\u3002\u6309 BagId \u65b0\u589e\u4efb\u610f\u67dc\u5b50\uff08\u542b\u7eb8\u7bb1/\u673a\u5668\u69fd\u7b49\u9ed1\u540d\u5355\u67dc\uff09\uff0c\u4f18\u5148\u7ea7\u9ad8\u4e8e\u4e0a\u65b9\u4e13\u5c5e\u4e0e\u901a\u7528\u3002</div>';
    } else {
      for (var idx = 0; idx < bov.length; idx++) {
        h += '<div class="__mc-item">'
          + '<div class="__mc-info">'
          + '<div class="__mc-lb">\u67dc\u5b50 #' + bov[idx].id + '</div>'
          + '<div class="__mc-ck">' + BOV_SECTION + '.' + BOV_KEY + ' \u00b7 ' + bov[idx].id + ':' + bov[idx].w + 'x' + bov[idx].h + '</div>'
          + '</div>'
          + '<div class="__mc-ctl">'
          + '<span class="__mc-bov-lb">\u5bbd</span>'
          + '<input type="text" class="__mc-numbox" inputmode="numeric" data-num="int" value="' + bov[idx].w + '" data-act="bov-set" data-idx="' + idx + '" data-k="w" title="\u5bbd\u5ea6\uff080..40\uff09">'
          + '<span class="__mc-bov-lb">\u9ad8</span>'
          + '<input type="text" class="__mc-numbox" inputmode="numeric" data-num="int" value="' + bov[idx].h + '" data-act="bov-set" data-idx="' + idx + '" data-k="h" title="\u9ad8\u5ea6\uff080..30\uff09">'
          + '</div>'
          + '<div class="__mc-badge __mc-b-now">\u26a1 \u7acb\u5373\u751f\u6548</div>'
          + '<button type="button" class="__mc-del" data-act="bov-del" data-idx="' + idx + '" title="\u5220\u9664\u6b64\u67dc\u5b50">\u2715</button>'
          + '</div>';
      }
    }
    return h + '</div>';
  }

  /* ================= v2 任务 10：卸载动作区 + 确认弹窗（原型 delHtml 照抄） ================= */

  /* 维护动作区（原型 detailHtml 的"维护动作"行，视觉同 .mc-grp 段）：kv 布局 + danger"卸载…"钮。
   * 停用/启用=左列开关（v1 已在），此处只承载卸载入口。**独立 __mc-maint 类不挂 __mc-grp**：
   * .__mc-grp 在回归夹具里语义="Meta 配置组（+ovr/bagOvr 扩展块）"，动作区不是配置组——
   * 类名隔离使夹具组数断言零改动（先例：ovr/bagOvr 需夹具 +1 同步，本区不走该路）。 */
  /* v3 维护区（详情页底部）：更新/安装按钮按仓库态出现；卸载常驻（未安装不显示）。
   * 按钮不进左侧列表——更新入口只有列表行内图标与此处（spec §5.3/D2）。 */
  function maintHtml(m) {
    var r = isTpMod(m) ? null : repoRowOfGuid(m.guid);
    var h = '<div class="__mc-maint"><h3>\u7ef4\u62a4</h3>';
    if (r && r.pending) {
      h += '<div class="__mc-kv"><span class="__mc-kk">\u66f4\u65b0</span>'
        + '<span style="color:#e8a33d;font-size:12px">\u27f3 \u5f85\u751f\u6548 \u00b7 \u91cd\u542f\u6e38\u620f\u540e\u5e94\u7528</span></div>';
    } else if (r && r.remote != null && (r.status === 'update' || r.status === 'downgrade' || r.status === 'install' || r.local == null)) {
      var isDown = r.status === 'downgrade';
      var isInst = r.local == null || r.status === 'install';
      var label = isDown ? '\u964d\u7ea7\u5230 ' + r.remote : (isInst ? '\u5b89\u88c5 ' + r.remote : '\u66f4\u65b0\u5230 ' + r.remote);
      var cls = isDown ? '__mc-btn-org' : (isInst ? '__mc-btn-blue' : '__mc-btn-pri');
      var ver = isInst ? '\u672a\u5b89\u88c5 \u2192 ' + r.remote : r.local + ' \u2192 ' + r.remote;
      h += '<div class="__mc-kv"><span class="__mc-kk">\u66f4\u65b0</span>'
        + '<button type="button" class="__mc-btn ' + cls + ' __mc-btn-mini" data-ract="upd" data-repo="' + esc(r.repoKey) + '">' + esc(label) + '</button>'
        + '<span style="font-size:11px;color:#6b7180">' + esc(ver) + '</span></div>';
    }
    if (!m.missing) {
      h += '<div class="__mc-kv"><span class="__mc-kk">\u5378\u8f7d</span>'
        + '<button type="button" class="__mc-btn __mc-btn-danger __mc-btn-mini" data-act="uninst" data-guid="' + esc(m.guid) + '">\u5378\u8f7d\u2026</button>'
        + '<span style="font-size:11px;color:#6b7180">\u5220\u6587\u4ef6\uff0c\u91cd\u542f\u540e\u751f\u6548</span></div>';
    }
    return h + '</div>';
  }

  /* __self 页专用：只有卸载入口（v3 精简 hint）。 */
  function uninstActHtml(guid) {
    return '<div class="__mc-maint"><h3>\u7ef4\u62a4</h3>'
      + '<div class="__mc-kv"><span class="__mc-kk">\u5378\u8f7d</span>'
      + '<button type="button" class="__mc-btn __mc-btn-danger __mc-btn-mini" data-act="uninst" data-guid="' + esc(guid) + '">\u5378\u8f7d\u2026</button>'
      + '<span style="font-size:11px;color:#6b7180">\u9700\u91cd\u542f\u4e24\u6b21</span></div></div>';
  }

  /* 确认弹窗（v3 精简）：⚠ 红片+名称 / 一句执行说明（存档与其他 mod 不受影响并入）/
   * "将删除".files 真值明细红字（BepInEx\ 前缀反斜杠呈现）+ 固定行"停用 / 排序记录（如有）"/
   * note 补充可省 / self 特别红字（两靴 + 引导壳保留）/ 取消 + danger"确认卸载"。 */
  function uninstModalHtml(d) {
    var h = '<h3>' + chipHtml('red', '\u26a0') + '\u5378\u8f7d \u00b7 ' + esc(d.name) + '</h3>'
      + '<div class="__mc-m-sub">\u5c06\u5220\u9664\u4ee5\u4e0b\u5185\u5bb9\uff0c\u91cd\u542f\u6e38\u620f\u540e\u6267\u884c\u3002\u5b58\u6863\u548c\u5176\u4ed6 mod \u4e0d\u53d7\u5f71\u54cd\u3002</div>'
      + '<div class="__mc-files">';
    var fs = d.files || [];
    for (var i = 0; i < fs.length; i++)
      h += '<div class="__mc-file-del">BepInEx\\' + esc(String(fs[i]).replace(/\//g, '\\')) + '</div>';
    h += '<div class="__mc-file-del">\u505c\u7528 / \u6392\u5e8f\u8bb0\u5f55\uff08\u5982\u6709\uff09</div></div>';
    if (d.note) h += '<div class="__mc-m-sec"><div class="__mc-m-txt" style="font-size:11px;color:#8a90a0">\u24d8 ' + esc(d.note) + '</div></div>';
    if (d.self) h += '<div class="__mc-risk">\u26a0 \u8fd9\u662f\u9762\u677f\u81ea\u5df1\uff1a\u8981\u91cd\u542f\u4e24\u6b21\u624d\u80fd\u5220\u5e72\u51c0\uff08\u7b2c\u4e00\u6b21\u6253\u6807\u8bb0\uff0c\u7b2c\u4e8c\u6b21\u5220\u6587\u4ef6\u3001\u5165\u53e3\u6d88\u5931\uff09\u3002\u5f15\u5bfc\u58f3\u5c5e\u57fa\u5ea7\u7ec4\u4ef6\u4f1a\u4fdd\u7559\u3002</div>';
    h += '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u53d6\u6d88</button>'
      + '<button type="button" class="__mc-btn __mc-btn-danger" data-mact="douninst" data-guid="' + esc(d.guid) + '">\u786e\u8ba4\u5378\u8f7d</button></div>';
    return h;
  }

  /* 明细等待壳（MC_UNINST → MC_UNINST_DATA 一拍 drain 周期内的占位） */
  function uninstLoadingHtml(name) {
    return '<h3>' + chipHtml('red', '\u26a0') + '\u5378\u8f7d \u00b7 ' + esc(name || '') + '</h3>'
      + '<div class="__mc-m-sub">\u6b63\u5728\u8bfb\u53d6\u5378\u8f7d\u660e\u7ec6\u2026</div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u53d6\u6d88</button></div>';
  }

  function uninstNameOf(guid) {
    if (guid === SELF_GUID) return SELF_NAME;
    var m = findMod(guid);
    return m ? m.name : (guid || '');
  }

  function clearUninstTimer() {
    if (uninstTimer != null) { clearTimeout(uninstTimer); uninstTimer = null; }
  }

  /* "卸载…"点击：占位弹窗 + MC_UNINST 入信箱 + 5s 超时兜底（C# 拒绝路径回 MC_TOAST，超时
   * 只处理消息丢失场景）。结果一律等 C# 回推（MC_UNINST_DATA 渲染真明细 / MC_TOAST 报错），
   * 面板不做乐观 toast。 */
  function openUninstModal(guid) {
    ST.uninstGuid = guid;
    openModal(uninstLoadingHtml(uninstNameOf(guid)));
    window.__mcOutbox.push({ type: 'MC_UNINST', guid: guid });
    clearUninstTimer();
    uninstTimer = setTimeout(function () {
      uninstTimer = null;
      if (ST.uninstGuid !== guid) return;
      ST.uninstGuid = null;
      if (modalOpen() && S.mdl) setModal('<h3>' + chipHtml('red', '\u26a0') + '\u5378\u8f7d \u00b7 ' + esc(uninstNameOf(guid)) + '</h3>'
        + '<div class="__mc-m-sub">\u660e\u7ec6\u8bfb\u53d6\u8d85\u65f6\uff08C# \u672a\u56de\u5305\uff0c\u8be6\u89c1 LogOutput\uff09\u3002</div>'
        + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>');
    }, 5000);
  }

  /* MC_UNINST_DATA 到达：占位壳换真明细 + 挂"确认卸载"钮（关弹窗并发 MC_UNINST_DO——
   * 结果 toast/快照由 C# 回推，行内 ⟳ 与底条"卸载 X"随快照出现）。 */
  function onUninstData(ev) {
    if (ST.uninstGuid == null || ev.guid !== ST.uninstGuid) return; // 非当前请求（陈旧包）
    ST.uninstGuid = null;
    clearUninstTimer();
    if (!modalOpen() || !S.mdl) return; // 用户已关弹窗：只清状态
    setModal(uninstModalHtml(ev));
    var go = S.mdl.querySelector('[data-mact="douninst"]');
    if (!go) return;
    go.addEventListener('click', function (e) {
      e.stopPropagation();
      window.__mcOutbox.push({ type: 'MC_UNINST_DO', guid: ev.guid });
      closeModal();
    });
  }

  /* ================= v2 任务 8：主区仓库视图 ================= */

  function repoRowOf(key) {
    var d = ST.repoData;
    if (!d || !d.rows || !key) return null;
    for (var i = 0; i < d.rows.length; i++)
      if (d.rows[i].repoKey === key) return d.rows[i];
    return null;
  }
  function repoRowOfGuid(guid) {
    var d = ST.repoData;
    if (!d || !d.rows || !guid) return null;
    for (var i = 0; i < d.rows.length; i++)
      if (d.rows[i].guid === guid) return d.rows[i];
    return null;
  }

  function dlStageText(st, pct) {
    if (st === 'down') return '\u6b63\u5728\u4e0b\u8f7d\u2026 ' + (pct || 0) + '%';
    if (st === 'check') return '\u6821\u9a8c\u4e2d\u2026';
    if (st === 'unzip') return '\u89e3\u5305\u4e2d\u2026';
    if (st === 'write') return '\u5199\u5165\u5f85\u751f\u6548\u2026';
    return '\u5904\u7406\u4e2d\u2026';
  }

  /* v3：仓库视图渲染（repoViewHtml/repoRowHtml）随独立仓库页退役——行内图标与详情「维护」
   * 区按钮共用 openRepoModal 确认链。 */

  function renderCfg() {
    if (!S.cfg) return;
    if (!ST.snap) {
      S.cfg.innerHTML = '<div class="__mc-loading">\u6b63\u5728\u8bfb\u53d6 mod \u914d\u7f6e\u2026</div>';
      return;
    }
    if (ST.sel === '__self' || !mods().length) {
      S.cfg.innerHTML = shellWarnHtml() + '<div class="__mc-mod-title"><h2>' + esc(SELF_NAME) + '</h2>'
        + '<span class="__mc-en">' + esc(SELF_EN) + '</span></div>'
        + '<div class="__mc-mod-desc">\u9762\u677f\u81ea\u8eab\u3002\u4e0d\u53ef\u505c\u7528\uff1b\u5982\u9700\u79fb\u9664\uff0c\u7528\u4e0b\u65b9\u5378\u8f7d\u3002</div>'
        + uninstActHtml(SELF_GUID);
      return;
    }
    var m = curMod();
    if (!m) return;
    var tp = isTpMod(m);
    /* 停用横幅（v3 精简）：第三方三档（缺席/软停/重启级）；套件停用；未安装。 */
    var banner = tp
      ? (m.absent
        ? '\u5df2\u505c\u7528\uff1a\u5f53\u524d\u672a\u52a0\u8f7d\u3002\u542f\u7528\u540e\u4e0b\u6b21\u542f\u52a8\u6062\u590d\u3002'
        : (m.soft
          ? '\u5df2\u505c\u7528\uff1a\u884c\u4e3a\u5df2\u505c\u6b62\uff0c\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d\u3002'
          : '\u5df2\u505c\u7528\uff1a\u672c\u6b21\u4ecd\u5728\u8fd0\u884c\uff0c\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d\u3002'))
      : (m.missing
        ? '\u672a\u5b89\u88c5\uff1a\u6b64\u9875\u53ea\u8bfb\u3002\u53ef\u5728\u4e0b\u65b9\u300c\u7ef4\u62a4\u300d\u5b89\u88c5\u3002'
        : '\u5df2\u505c\u7528\uff1a\u6539\u52a8\u4ecd\u4f1a\u4fdd\u5b58\uff0c\u91cd\u65b0\u542f\u7528\u540e\u751f\u6548\u3002');
    var h = shellWarnHtml() + '<div class="__mc-mod-title"><h2>' + esc(m.name) + '</h2><span class="__mc-en">' + esc(m.en) + '</span></div>'
      + '<div class="__mc-mod-desc">' + esc(m.desc) + '</div>'
      + '<div class="__mc-stop-banner' + (m.enabled ? '' : ' __mc-show') + '">' + banner + '</div>';
    for (var gi = 0; gi < m.groups.length; gi++) {
      h += '<div class="__mc-grp"><h3>' + esc(m.groups[gi].title) + '</h3>';
      for (var ii = 0; ii < m.groups[gi].items.length; ii++)
        h += itemHtml(m, m.groups[gi].items[ii], gi, ii);
      h += '</div>';
    }
    if (m.groups.length === 0) {
      h += '<div class="__mc-grp"><h3>\u914d\u7f6e</h3>'
        + '<div class="__mc-ovr-empty">\u8be5 mod \u53ea\u6709\u542f\u7528 / \u505c\u7528\uff0c\u6ca1\u6709\u5176\u4ed6\u8bbe\u7f6e\u3002</div></div>';
    }
    if (m.ovr) h += ovrHtml(m);
    if (m.bagOvr) h += bagOvrHtml(m);
    h += maintHtml(m); // v3：更新/安装按钮 + 卸载入口（spec §5.3；入口不进左侧列表）
    S.cfg.innerHTML = h;
  }

  /* ================= toast ================= */

  function toast(main, badge, err, errDetail) {
    if (!S.toastEl || !S.toastEl.isConnected) return;
    var small = err
      ? '\u5199\u5165\u88ab\u62d2\u7edd' + (errDetail ? '\uff1a' + errDetail : '') + '\uff08\u8be6\u89c1 LogOutput\uff09'
      : (TOAST_SMALL[badge] || TOAST_SMALL.reboot);
    S.toastEl.className = '__mc-toast __mc-show' + (err ? ' __mc-err' : '');
    S.toastEl.innerHTML = '<span>' + esc(main) + '</span><small>' + esc(small) + '</small>';
    clearTimeout(toast._h);
    toast._h = setTimeout(function () {
      if (S.toastEl) S.toastEl.className = '__mc-toast';
    }, 2600);
  }

  /* 任务 16：原文 toast（第三方启停两档语义 / C# MC_TOAST 残留提示）——小字直接给文案 */
  function toastRaw(main, small) {
    if (!S.toastEl || !S.toastEl.isConnected) return;
    S.toastEl.className = '__mc-toast __mc-show';
    S.toastEl.innerHTML = '<span>' + esc(main) + '</span><small>' + esc(small || '') + '</small>';
    clearTimeout(toast._h);
    toast._h = setTimeout(function () {
      if (S.toastEl) S.toastEl.className = '__mc-toast';
    }, 3400);
  }

  /* ================= v2 任务 7：浮层弹窗 + 底部待生效条 ================= */

  /* 弹窗头装饰图标（原型 h3 内联款：cursor:default 不可点） */
  function chipHtml(k, glyph) {
    return '<span class="__mc-ic __mc-' + k + '" style="cursor:default" aria-hidden="true">' + glyph + '</span>';
  }

  /* ---- v2 任务 11：冲突详情弹窗（原型 cfHtml 照抄）----
   * 每条冲突一卡片：h3=图标+"mod名 · 标签"（tag 冲突/碰撞/同域）；.sub=数据侧 title；
   * 三 sec：冲突对象（who 加粗）/ 原因（reason）/ 处置建议（tips 编号列表）；
   * 多卡片间 border-top 分隔线；底部固定风险文案（原型原文）+ 三动作钮。 */
  function cfCardHtml(m, c, i) {
    var lv = c.lv || 'gray';
    var h = '';
    if (i > 0) h += '<div style="border-top:1px solid #2a2f3d;margin-top:14px"></div>';
    h += '<h3>' + chipHtml(lv, icGlyph({ k: lv, click: 'cf' })) + esc(m.name) + ' \u00b7 ' + esc(c.tag || '')
      + '</h3><div class="__mc-m-sub">' + esc(c.title || '') + '</div>'
      /* T11 M4：灰卡（L3 同域 + L1 灰卡）标题用"同域 mod"，红/橙仍为"冲突对象" */
      + '<div class="__mc-m-sec"><div class="__mc-m-h">' + (lv === 'gray' ? '\u540c\u57df mod' : '\u51b2\u7a81\u5bf9\u8c61') + '</div>'
      + '<div class="__mc-m-txt"><b style="color:#e8e6e0">' + esc((c.who || []).join('\u3001')) + '</b></div></div>'
      + '<div class="__mc-m-sec"><div class="__mc-m-h">\u539f\u56e0</div>'
      + '<div class="__mc-m-txt">' + esc(c.reason || '') + '</div></div>'
      + '<div class="__mc-m-sec"><div class="__mc-m-h">\u5904\u7f6e\u5efa\u8bae\uff08\u81ea\u62c5\u9009\u62e9\uff09</div>'
      + '<div class="__mc-m-txt">';
    var tips = c.tips || [];
    for (var j = 0; j < tips.length; j++) h += (j > 0 ? '<br>' : '') + (j + 1) + '. ' + esc(tips[j]);
    return h + '</div></div>';
  }

  function cfModalHtml(m, cards) {
    var h = '';
    for (var i = 0; i < cards.length; i++) h += cfCardHtml(m, cards[i], i);
    h += '<div class="__mc-risk">\u8c03\u6574\u52a0\u8f7d\u987a\u5e8f\u53ea\u80fd\u89c4\u907f\u90e8\u5206\u51b2\u7a81\uff08\u5199\u5e8f\u7c7b\uff09\uff0c\u4e0d\u80fd\u89e3\u51b3\u6570\u503c\u53e0\u52a0\u7c7b\u51b2\u7a81\u3002</div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button>'
      + '<button type="button" class="__mc-btn __mc-btn-mini" data-mact="cfoff" data-guid="' + esc(m.guid) + '">\u505c\u7528\u6b64 mod</button>'
      /* Ruling 20：冲突/碰撞详情弹窗的"去调序"入口——关闭弹窗 → 进排序模式 */
      + '<button type="button" class="__mc-btn __mc-btn-pri __mc-btn-mini" data-mact="cforder">\u53bb\u8c03\u5e8f</button>'
      + '<button type="button" class="__mc-btn __mc-btn-danger __mc-btn-mini" data-mact="cfdel" data-guid="' + esc(m.guid) + '">\u5378\u8f7d\u6b64 mod\u2026</button></div>';
    return h;
  }

  /* 等待壳（MC_CF → MC_CF_DATA 一拍 drain 周期内的占位） */
  function cfLoadingHtml(name) {
    return '<h3>' + chipHtml('gray', '\u25ce') + '\u51b2\u7a81\u8be6\u60c5 \u00b7 ' + esc(name) + '</h3>'
      + '<div class="__mc-m-sub">\u6b63\u5728\u8bfb\u53d6\u51b2\u7a81\u8be6\u60c5\u2026</div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
  }

  /* 空态壳（C# 回 cards:[] —— 一般不会到达：ics 由同一数据源产出） */
  function cfEmptyHtml(name) {
    return '<h3>' + chipHtml('gray', '\u25ce') + '\u51b2\u7a81\u8be6\u60c5 \u00b7 ' + esc(name) + '</h3>'
      + '<div class="__mc-m-sub">\u672a\u53d1\u73b0\u51b2\u7a81\u8be6\u60c5\u3002</div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
  }

  function clearCfTimer() {
    if (cfTimer != null) { clearTimeout(cfTimer); cfTimer = null; }
  }

  /* cf 图标点击：占位弹窗 + MC_CF 入 outbox + 5s 超时兜底（结果一律等 MC_CF_DATA 真数据）。 */
  function openCfModal(m) {
    ST.cfGuid = m.guid;
    openModal(cfLoadingHtml(m.name));
    window.__mcOutbox.push({ type: 'MC_CF', guid: m.guid });
    clearCfTimer();
    cfTimer = setTimeout(function () {
      cfTimer = null;
      if (ST.cfGuid !== m.guid) return;
      ST.cfGuid = null;
      if (!modalOpen() || !S.mdl) return;
      setModal('<h3>' + chipHtml('red', '\u26a0') + '\u51b2\u7a81\u8be6\u60c5 \u00b7 ' + esc(m.name) + '</h3>'
        + '<div class="__mc-m-sub">\u8be6\u60c5\u8bfb\u53d6\u8d85\u65f6\uff08C# \u672a\u56de\u5305\uff0c\u8be6\u89c1 LogOutput\uff09\u3002</div>'
        + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>');
    }, 5000);
  }

  /* "停用此 mod"（弹窗内）：复用既有启停链 MC_TOGGLE_MOD（SetModEnabled 同一条路；
   * MC_SET 只写 Meta 登记配置项，General.Enabled 不在 Meta 表内）。 */
  function cfDisable(guid) {
    var m = findMod(guid);
    if (m && m.enabled) { m.enabled = false; renderSide(); renderCfg(); }
    window.__mcOutbox.push({ type: 'MC_TOGGLE_MOD', guid: guid, enabled: false });
    closeModal();
    var name = m ? m.name : guid;
    if (m && isTpMod(m))
      toastRaw(name + ' \u5df2\u6807\u8bb0\u505c\u7528\uff08\u91cd\u542f\u7ea7\uff09',
        '\u672c\u6b21\u4f1a\u8bdd\u4ecd\u5728\u8fd0\u884c \u00b7 \u4e0b\u6b21\u542f\u52a8\u540e\u4e0d\u518d\u52a0\u8f7d');
    else
      toast(name + ' \u5df2\u505c\u7528\uff1a\u884c\u4e3a\u7acb\u5373\u8fd8\u539f\uff0c\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d', 'now');
  }

  /* MC_CF_DATA 到达：占位壳换真卡片 + 挂停用/卸载动作（陈旧包/已关弹窗只清状态）。 */
  function onCfData(ev) {
    if (ST.cfGuid == null || ev.guid !== ST.cfGuid) return;
    ST.cfGuid = null;
    clearCfTimer();
    if (!modalOpen() || !S.mdl) return;
    var m = findMod(ev.guid);
    var name = m ? m.name : ev.guid;
    var cards = ev.cards || [];
    if (!cards.length) { setModal(cfEmptyHtml(name)); return; }
    setModal(cfModalHtml({ guid: ev.guid, name: name }, cards));
    var off = S.mdl.querySelector('[data-mact="cfoff"]');
    if (off) off.addEventListener('click', function (e) { e.stopPropagation(); cfDisable(ev.guid); });
    var del = S.mdl.querySelector('[data-mact="cfdel"]');
    if (del) del.addEventListener('click', function (e) { e.stopPropagation(); openUninstModal(ev.guid); });
    /* Ruling 20："去调序"——关闭弹窗并进入排序模式（未在排序模式才拉取） */
    var ord = S.mdl.querySelector('[data-mact="cforder"]');
    if (ord) ord.addEventListener('click', function (e) {
      e.stopPropagation();
      closeModal();
      if (!ST.ord) ordToggle();
    });
  }

  /* ---- v2 任务 12：L2 补丁碰撞汇总弹窗（MC_SCAN_DATA 全量：可归属组 + 未知来源组）----
   * 侧栏汇总行点击打开；"重新扫描"发 MC_SCAN（C# 主线程重扫 → 新快照 + MC_SCAN_DATA），
   * 数据到达时若弹窗仍开着就地重绘（不关窗）。行内橙标只挂可归属组——本弹窗是全量视图。 */
  function openPatchModal() {
    ST.patchOpen = true;
    openModal(patchModalHtml());
    bindPatchModal();
  }

  function bindPatchModal() {
    if (!S.mdl) return;
    var rs = S.mdl.querySelector('[data-pact="rescan"]');
    if (rs) rs.addEventListener('click', function (e) {
      e.stopPropagation();
      if (ST.scanWait) return;
      ST.scanWait = true;
      window.__mcOutbox.push({ type: 'MC_SCAN' });
      setModal(patchModalHtml()); // 就地转"扫描中…"态
      bindPatchModal();
    });
  }

  function patchModalHtml() {
    var s = ST.scan;
    var h = '<h3>' + chipHtml('orange', '\u21c4') + '\u8865\u4e01\u78b0\u649e\uff08\u8fd0\u884c\u65f6\u5b9e\u6d4b\uff09</h3>';
    if (!s) {
      return h + '<div class="__mc-m-sub">\u6b63\u5728\u8bfb\u53d6\u626b\u63cf\u7ed3\u679c\u2026</div>'
        + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
    }
    if (s.error) {
      h += '<div class="__mc-risk">\u626b\u63cf\u5f02\u5e38\uff1a' + esc(s.error) + '</div>';
    } else {
      h += '<div class="__mc-m-sub">\u626b\u63cf\u4e8e ' + esc(scanTimeText(s.at)) + ' \u00b7 \u5171 ' + (s.total || 0)
        + ' \u7ec4\uff08\u53ef\u5f52\u5c5e ' + (s.routed || 0) + ' / \u672a\u77e5\u6765\u6e90 ' + (s.unknown || 0)
        + '\uff09'
        + ((s.framework || 0) > 0 ? ' \u00b7 \u6846\u67b6\u4fa7 ' + s.framework + ' \u7ec4\u672a\u8ba1\u5165' : '')
        + ' \u00b7 \u6e38\u620f\u4fa7 ' + (s.game || 0) + ' \u4e2a\u65b9\u6cd5\uff08\u5df2\u6302\u8865\u4e01\u603b\u6570 '
        + (s.patched || 0) + '\uff09\u00b7 ' + (s.ms || 0) + 'ms</div>'
        + '<div class="__mc-m-sec"><div class="__mc-m-h">\u78b0\u649e\u7ec4\uff08\u540c\u4e00\u6e38\u620f\u65b9\u6cd5 \u22652 \u4e2a\u8865\u4e01 owner\uff09</div>';
      var items = s.items || [];
      if (!items.length) h += '<div class="__mc-m-txt">\u672a\u53d1\u73b0\u8865\u4e01\u78b0\u649e\uff08\u540c\u65b9\u6cd5\u591a owner\uff09\u7ec4\u3002</div>';
      for (var i = 0; i < items.length; i++) {
        var it = items[i] || {};
        h += '<div class="__mc-scan-grp"><div class="__mc-scan-m">' + esc(it.method || '?')
          + (it.unknown ? ' <span style="color:#8a90a0;font-size:10.5px">[\u672a\u77e5\u6765\u6e90]</span>' : '') + '</div>'
          + '<div class="__mc-scan-o">' + esc((it.owners || []).join(' \u00b7 ')) + '</div>'
          + '<div class="__mc-scan-n">\u8865\u4e01\u5b9e\u4f8b ' + (it.patches || 0) + ' \u4e2a \u00b7 '
          + (it.unknown ? '\u4e0d\u53ef\u5f52\u5c5e\uff08\u4ec5\u7edf\u8ba1\uff09' : '\u53ef\u5f52\u5c5e\u5df2\u88c5 mod') + '</div></div>';
      }
      h += '</div>';
      // 裁定 A（Ruling 14②）：框架侧多 owner 组不铺行/不计入碰撞总数，但汇总必须披露
      var fw = s.frameItems || [];
      if (fw.length) {
        h += '<div class="__mc-m-sec"><div class="__mc-m-h">\u6846\u67b6\u4fa7\u672a\u8ba1\u5165\uff08' + (s.framework || fw.length)
          + ' \u7ec4\uff0c\u975e\u6e38\u620f\u65b9\u6cd5\uff09</div>'
          + '<div class="__mc-m-txt" style="font-size:11.5px">\u6846\u67b6/\u5f15\u64ce\u65b9\u6cd5\u4e0a\u7684\u591a owner \u5171\u6302\uff1a\u771f\u5b9e\u5b58\u5728\uff08\u542b\u5df2\u88c5 mod\uff09\uff0c\u4f46\u4e0d\u53c2\u4e0e\u6e38\u620f\u903b\u8f91\u78b0\u649e\u5224\u5b9a\uff0c\u4e0d\u4ea7\u751f\u884c\u5185\u6a59\u6807\u3002</div>';
        for (var fi = 0; fi < fw.length; fi++) {
          var fit = fw[fi] || {};
          h += '<div class="__mc-scan-grp"><div class="__mc-scan-m">' + esc(fit.method || '?') + '</div>'
            + '<div class="__mc-scan-o">' + esc((fit.owners || []).join(' \u00b7 ')) + '</div>'
            + '<div class="__mc-scan-n">\u8865\u4e01\u5b9e\u4f8b ' + (fit.patches || 0) + ' \u4e2a \u00b7 \u4ec5\u62ab\u9732\uff08\u672a\u8ba1\u5165\u78b0\u649e\u7ec4\uff09</div></div>';
        }
        h += '</div>';
      }
      h += '<div class="__mc-risk">\u540c\u4e00\u65b9\u6cd5\u591a\u4e2a\u8865\u4e01\u7684\u7ed3\u679c\u53d6\u51b3\u4e8e\u52a0\u8f7d\u987a\u5e8f\uff08\u540e\u5199\u8986\u76d6\u524d\u5199\uff09\uff1b\u8c03\u6574\u52a0\u8f7d\u987a\u5e8f\u4ec5\u7f13\u89e3\u5199\u5e8f\u7c7b\u51b2\u7a81\uff0c\u4e0d\u80fd\u89e3\u51b3\u6570\u503c\u53e0\u52a0\u7c7b\u51b2\u7a81\uff0c\u98ce\u9669\u81ea\u8d1f\u3002</div>'
        + '<div class="__mc-m-sub" style="margin-top:8px">\u8303\u56f4\uff1a' + esc(s.scope || '') + '\uff08\u6846\u67b6/\u5f15\u64ce/\u63d2\u4ef6\u81ea\u8eab\u65b9\u6cd5\u4e0d\u8ba1\u5165\u78b0\u649e\u7ec4\u6570\uff09</div>';
    }
    h += '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-pact="rescan"'
      + (ST.scanWait ? ' disabled' : '') + '>' + (ST.scanWait ? '\u626b\u63cf\u4e2d\u2026' : '\u91cd\u65b0\u626b\u63cf') + '</button>'
      + '<button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
    return h;
  }

  /* ---- v2 任务 8：更新确认弹窗（v3 精简：版本对比一行 + 告警红字（仅异常时）+ 一句生效说明）----
   * D7 降级红字 / D8 版本告警红字（均不拦截）；主按钮：降级=橙"降级到 X" / 安装=蓝"安装 X" /
   * 更新=琥珀"更新到 X"。 */
  function repoVerbOf(r) {
    if (r.status === 'downgrade') return '\u964d\u7ea7';
    if (r.local == null || r.status === 'install') return '\u5b89\u88c5';
    return '\u66f4\u65b0';
  }

  function updModalRealHtml(r) {
    var isDown = r.status === 'downgrade', isWarn = r.status === 'warn';
    var isInst = r.local == null || r.status === 'install';
    var chip = isDown ? chipHtml('orange', '\u2193') : chipHtml('blue', '\u2191');
    var verb = repoVerbOf(r);
    var h = '<h3>' + chip + verb + ' \u00b7 ' + esc(r.name) + '</h3>'
      + '<div class="__mc-kv" style="padding:2px 0 0"><span class="__mc-kvv">' + esc(r.local || '\u672a\u5b89\u88c5') + '</span>'
      + '<span style="color:#6b7180">\u2192</span>'
      + '<span class="__mc-kvv" style="color:' + (isDown ? '#fbbf24' : '#60a5fa') + ';font-size:14px">' + esc(r.remote) + '</span></div>';
    if (isDown) h += '<div class="__mc-risk">\u26a0 \u672c\u5730 ' + esc(r.local) + ' \u6bd4\u8fdc\u7aef\u65b0\u3002\u65e7\u7248\u53ef\u80fd\u548c\u5b58\u6863\u3001\u5176\u4ed6 mod \u4e0d\u5339\u914d\uff0c\u786e\u5b9a\u8981\u964d\u7ea7\u5417\uff1f</div>';
    else if (isWarn) h += '<div class="__mc-risk">\u26a0 ' + esc(r.why || '\u8be5\u7248\u672c\u53ef\u80fd\u4e0d\u9002\u914d\u5f53\u524d\u6e38\u620f') + '\uff0c\u6709\u95ea\u9000\u98ce\u9669\uff0c\u786e\u5b9a\u8981\u7ee7\u7eed\u5417\uff1f</div>';
    else if (r.kbw && r.kbwhy) h += '<div class="__mc-risk">\u26a0 ' + esc(r.kbwhy) + '\uff0c\u6709\u95ea\u9000\u98ce\u9669\uff0c\u786e\u5b9a\u8981\u7ee7\u7eed\u5417\uff1f</div>';
    if (r.verNote) h += '<div class="__mc-m-sec"><div class="__mc-m-txt" style="font-size:11px;color:#8a90a0">\u24d8 \u672a\u80fd\u6821\u9a8c\u6e38\u620f\u7248\u672c</div></div>';
    h += '<div class="__mc-m-sec"><div class="__mc-m-txt">\u4e0b\u8f7d\u5e76\u6821\u9a8c\u540e\u6682\u5b58\uff0c<b style="color:#e8e6e0">\u91cd\u542f\u6e38\u620f</b>\u65f6\u81ea\u52a8\u66ff\u6362\uff08\u4e0d\u4fdd\u7559\u65e7\u7248\uff09\u3002</div></div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u53d6\u6d88</button>'
      + '<button type="button" class="__mc-btn ' + (isDown ? '__mc-btn-org' : (isInst ? '__mc-btn-blue' : '__mc-btn-pri'))
      + '" data-mact="doupd" data-repo="' + esc(r.repoKey) + '">'
      + esc(verb === '\u5b89\u88c5' ? '\u5b89\u88c5 ' : verb + '\u5230 ') + esc(r.remote) + '</button></div>';
    return h;
  }

  /* 确认后的进度态弹窗（MC_DL 就地更新，不重建弹窗；v3 精简） */
  function updModalDlHtml(r, dl) {
    var chip = r.status === 'downgrade' ? chipHtml('orange', '\u2193') : chipHtml('blue', '\u2191');
    var st = dl ? dl.stage : 'down';
    var pct = dl && typeof dl.pct === 'number' ? dl.pct : 0;
    return '<h3>' + chip + repoVerbOf(r) + ' \u00b7 ' + esc(r.name) + '</h3>'
      + '<div class="__mc-m-sec"><div class="__mc-m-h">\u8fdb\u884c\u4e2d</div>'
      + '<div class="__mc-m-txt" id="__mc_dltx">' + esc(dlStageText(st, pct)) + '</div>'
      + '<div class="__mc-dlbar"><i id="__mc_dlfill" style="width:' + (st === 'down' ? pct : 100) + '%"></i></div></div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u9690\u85cf</button></div>';
  }

  function updModalDoneHtml(r) {
    return '<h3>' + chipHtml('amber', '\u27f3') + '\u5df2\u5c31\u7eea \u00b7 ' + esc(r.name) + '</h3>'
      + '<div class="__mc-m-sec"><div class="__mc-m-txt">\u5df2\u4e0b\u8f7d\u5e76\u6821\u9a8c\u3002<b style="color:#e8e6e0">\u91cd\u542f\u6e38\u620f</b>\u540e\u81ea\u52a8\u66ff\u6362\u3002</div></div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-pri __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
  }

  function updModalErrHtml(r, err) {
    return '<h3>' + chipHtml('red', '\u26a0') + repoVerbOf(r) + '\u5931\u8d25 \u00b7 ' + esc(r.name) + '</h3>'
      + '<div class="__mc-risk">' + esc(err || '\u672a\u77e5\u9519\u8bef') + '</div>'
      + '<div class="__mc-m-sec"><div class="__mc-m-txt">\u6ca1\u6709\u505a\u4efb\u4f55\u66f4\u6539\uff0c\u7a0d\u540e\u53ef\u91cd\u8bd5\u3002</div></div>'
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u5173\u95ed</button></div>';
  }

  /* 侧栏 ↑/↓/⊘ 图标 → 确认弹窗（按 guid 对照最近 MC_REPO_DATA；v3：无数据回退 toast） */
  function openUpdModal(m) {
    var r = m ? repoRowOfGuid(m.guid) : null;
    if (r && r.remote != null && !r.pending) openRepoModal(r);
    else toastRaw('\u6682\u65e0\u66f4\u65b0\u4fe1\u606f', '\u70b9\u53f3\u4e0a\u300c\u68c0\u67e5\u66f4\u65b0\u300d\u5148\u67e5\u4e00\u6b21');
  }

  /* 仓库行/图标共用的确认弹窗：确认 → MC_UPD/MC_INST 入 outbox → 弹窗转进度态 */
  function openRepoModal(r) {
    openModal(updModalRealHtml(r));
    if (!S.mdl) return;
    S.mdlRepoKey = r.repoKey;
    var go = S.mdl.querySelector('[data-mact="doupd"]');
    if (!go) return;
    go.addEventListener('click', function (e) {
      e.stopPropagation();
      var row = repoRowOf(S.mdlRepoKey);
      if (!row) return;
      window.__mcOutbox.push({
        type: (row.local == null || row.status === 'install') ? 'MC_INST' : 'MC_UPD',
        repoKey: row.repoKey,
      });
      if (setModal(updModalDlHtml(row, null))) {
        var tx = S.mdl.querySelector('#__mc_dltx');
        if (tx) tx.textContent = '\u51c6\u5907\u4e0b\u8f7d\u2026';
      }
    });
  }

  function setModal(html) {
    if (!S.mdl || !S.mbg || !S.mbg.isConnected) return false;
    S.mdl.innerHTML = html;
    var closes = S.mdl.querySelectorAll('[data-mact="close"]');
    for (var i = 0; i < closes.length; i++)
      closes[i].addEventListener('click', function (e) { e.stopPropagation(); closeModal(); });
    return true;
  }

  function openModal(html) {
    if (!setModal(html)) return;
    S.mdlRepoKey = null; // 通用弹窗清归属（openRepoModal 随后置回；防陈旧 MC_DL 改写其他弹窗）
    S.mbg.className = '__mc-modal-bg __mc-show';
  }

  function closeModal() {
    if (S.mbg) S.mbg.className = '__mc-modal-bg';
    ST.cfGuid = null; clearCfTimer(); // v2 任务 11：关弹窗=放弃未到的冲突详情（防陈旧回包误渲染）
    ST.patchOpen = false; ST.scanWait = false; // v2 任务 12：碰撞汇总弹窗态随关清
  }

  function modalOpen() {
    return !!(S.mbg && S.mbg.className.indexOf('__mc-show') !== -1);
  }

  /* 底部待生效条（MC_PENDING_SUMMARY{n,labels,applied?}；n>0 或 applied 在场显示——原型
   * renderPending 同款文案。T10（T9 改进项）：applied=中性档（"自定义加载序生效中"=本靴已
   * 应用）——不计入待生效计数、弱化样式（__mc-weak 灰上缘）、不缀"重启后应用"）。 */
  function renderPending() {
    if (!S.pendEl || !S.pendEl.isConnected) return;
    var p = ST.pend;
    var labels = p && p.labels ? p.labels : [];
    var applied = p && p.applied ? p.applied : null;
    if (!labels.length && !applied) {
      S.pendEl.className = '__mc-pending';
      return;
    }
    S.pendEl.className = '__mc-pending __mc-show' + (labels.length ? '' : ' __mc-weak');
    if (!S.pendTx) return;
    var h = '';
    if (labels.length)
      h += esc('\u5f85\u751f\u6548 ' + labels.length + ' \u9879\uff08' + labels.join(' \u00b7 ') + '\uff09\u00b7 \u91cd\u542f\u6e38\u620f\u540e\u5e94\u7528');
    if (applied)
      h += (h ? ' \u00b7 ' : '') + '<span style="color:#8a90a0">' + esc(applied) + '</span>';
    S.pendTx.innerHTML = h;
  }

  /* v2 任务 13：一键重启确认弹窗（原型无此弹窗，文案自定；brief 逐字口径
   * "将退出游戏并在约 10 秒后自动重启以应用 N 项待生效操作。未保存的对局进度会丢失。"）。
   * 确认 → MC_RESTART 入 outbox 并关弹窗；结果 toast 由 C# 回推（拉起成功="正在重启游戏…"、
   * 失败=报错且游戏不退出）。n=0（仅中性档"生效中"）时文案去掉"N 项待生效操作"，
   * 仍允许重启（应用加载序/重置现场）。 */
  function openRebootModal() {
    var n = ST.pend && ST.pend.n ? ST.pend.n : 0;
    openModal('<h3>' + chipHtml('amber', '\u21bb') + '\u7acb\u5373\u91cd\u542f</h3>'
      + '<div class="__mc-m-sub">\u9000\u51fa\u6e38\u620f\uff0c\u7ea6 10 \u79d2\u540e\u81ea\u52a8\u56de\u6765\u3002</div>'
      + '<div class="__mc-risk">\u26a0 \u672a\u4fdd\u5b58\u7684\u5bf9\u5c40\u8fdb\u5ea6\u4f1a\u4e22\u5931\u3002</div>'
      + (n > 0 ? '<div class="__mc-m-sec"><div class="__mc-m-txt">\u91cd\u542f\u540e\u81ea\u52a8\u5e94\u7528 ' + n + ' \u9879\u5f85\u751f\u6548\u64cd\u4f5c\u3002</div></div>' : '')
      + '<div class="__mc-m-acts"><button type="button" class="__mc-btn __mc-btn-mini" data-mact="close">\u53d6\u6d88</button>'
      + '<button type="button" class="__mc-btn __mc-btn-pri" data-mact="doreboot">\u786e\u8ba4\u91cd\u542f</button></div>');
    if (!S.mdl) return;
    var go = S.mdl.querySelector('[data-mact="doreboot"]');
    if (!go) return;
    go.addEventListener('click', function (e) {
      e.stopPropagation();
      window.__mcOutbox.push({ type: 'MC_RESTART' });
      closeModal();
    });
  }

  /* ================= 消息发送 ================= */

  function routeOf(k) {
    var i = String(k).indexOf('.');
    return i === -1 ? ['', k] : [String(k).slice(0, i), String(k).slice(i + 1)];
  }
  /* C1 修复（评审 Fix Round 1）：key 一律发**完整展示键 k 原串**（"节.键" / 联动"节.键A + 键B"），
   * section 仍发解析出的节前缀（供 C# Match 的节名前置校验）。此前剥前缀后的联动 key
   * "WidthMult + HeightMult" 在 C# Match 三候选（Key/WKeys/组合 K）全不命中——背包+柜子
   * 4 个联动滑条 MC_SET 必败。整串发后命中 it.K 分支（组合展示键逐字相等）；
   * 非联动项整串 = it.K 同样命中；ovr 走 ovrCommit 固定 section='其他'/key='Overrides'（it.Key 分支）。 */
  function sendSet(m, it, value) {
    var r = routeOf(it.k);
    window.__mcOutbox.push({ type: 'MC_SET', guid: m.guid, section: r[0], key: it.k, value: value });
  }
  function serializeOvr(m) {
    return (m.ovr || []).slice().sort(function (a, b) { return a.id - b.id; })
      .map(function (o) { return o.id + ':' + o.ms; }).join(',');
  }
  function ovrCommit(m) {
    window.__mcOutbox.push({ type: 'MC_SET', guid: m.guid, section: OVR_SECTION, key: OVR_KEY, value: serializeOvr(m) });
  }

  /* 100ms 节流（滑条连发防消息风暴）：立即发首条，100ms 窗口内后续合并为末值补发 */
  function throttleSend(id, fn) {
    var now = Date.now();
    var t = TH[id] || (TH[id] = { last: 0, timer: null, pending: null });
    if (now - t.last >= 100) {
      t.last = now; t.pending = null; fn();
    } else {
      t.pending = fn;
      if (t.timer == null) {
        t.timer = setTimeout(function () {
          var p = TH[id]; if (!p) return;
          p.timer = null; p.last = Date.now();
          var f = p.pending; p.pending = null;
          if (f) f();
        }, 100 - (now - t.last));
      }
    }
  }

  function toggleMod(guid) {
    if (guid === '__self') return; // 防呆：面板不可停用自身
    var m = findMod(guid);
    if (!m) return;
    m.enabled = !m.enabled;
    renderSide(); renderCfg();
    window.__mcOutbox.push({ type: 'MC_TOGGLE_MOD', guid: guid, enabled: m.enabled });
    var tp = isTpMod(m);
    if (tp) {
      /* 任务 16：第三方两档语义（指南 §五）——软停（Tier 2 回调/清单 softDisable）vs 重启级；
       * 缺席行（dll 已禁用）只有启用方向。小字原文给足生效时机。 */
      if (m.enabled) {
        toastRaw(m.name + ' \u5df2\u6062\u590d\u542f\u7528',
          m.absent ? 'dll \u5df2\u6062\u590d \u00b7 \u4e0b\u6b21\u542f\u52a8\u6b63\u5e38\u52a0\u8f7d'
                   : (m.soft ? '\u884c\u4e3a\u5df2\u6062\u590d\uff08\u8f6f\u505c\uff09 \u00b7 \u4e0b\u6b21\u542f\u52a8\u6b63\u5e38\u52a0\u8f7d'
                              : '\u4e0b\u6b21\u542f\u52a8\u6b63\u5e38\u52a0\u8f7d\uff08\u672c\u6b21\u4f1a\u8bdd\u4ecd\u5728\u8fd0\u884c\uff09'));
      } else if (m.soft) {
        toastRaw(m.name + ' \u5df2\u505c\u7528\uff08\u8f6f\u505c\uff09',
          '\u884c\u4e3a\u5df2\u7acb\u5373\u505c\u6b62 \u00b7 \u4e0b\u6b21\u542f\u52a8\u540e\u4e0d\u518d\u52a0\u8f7d');
      } else {
        toastRaw(m.name + ' \u5df2\u6807\u8bb0\u505c\u7528\uff08\u91cd\u542f\u7ea7\uff09',
          '\u672c\u6b21\u4f1a\u8bdd\u4ecd\u5728\u8fd0\u884c \u00b7 \u4e0b\u6b21\u542f\u52a8\u540e\u4e0d\u518d\u52a0\u8f7d');
      }
      return;
    }
    toast(m.name + (m.enabled
      ? ' \u5df2\u542f\u7528\uff1a\u6062\u590d\u751f\u6548\uff08\u22645\u79d2\uff09'
      : ' \u5df2\u505c\u7528\uff1a\u884c\u4e3a\u7acb\u5373\u8fd8\u539f\uff0c\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d'),
      'now');
  }

  /* ================= 右区交互 ================= */

  function syncSibling(el, v) {
    var ctl = el.closest('.__mc-ctl');
    if (!ctl) return;
    var all = ctl.querySelectorAll('input');
    for (var i = 0; i < all.length; i++) if (all[i] !== el) all[i].value = fmt(v);
  }

  function wireCfg(cfg) {
    cfg.addEventListener('click', function (e) {
      var t = e.target;
      if (!t.closest) return;
      /* v3：仓库动作只保留 行内 更新·降级·安装 → 确认弹窗（back/chk/updall/repogoto 随
       * 仓库页退役；检查更新在头部按钮）。 */
      var ra = t.closest('[data-ract]');
      if (ra) {
        var ract = ra.getAttribute('data-ract');
        if (ract === 'upd' || ract === 'inst') {
          var rr = repoRowOf(ra.getAttribute('data-repo'));
          if (rr) openRepoModal(rr);
        }
        return;
      }
      /* v2 任务 10：卸载…按钮 → 拉明细确认弹窗（__self 页 curMod 为 null——须在 curMod
       * 判空之前；停用/启用动作不在此区，仍走左列开关）。 */
      var un = t.closest('[data-act="uninst"]');
      if (un) { e.stopPropagation(); openUninstModal(un.getAttribute('data-guid')); return; }
      var m = curMod();
      if (!m) return;
      /* bool 开关 */
      var sw = t.closest('.__mc-mini-sw[data-act="set"]');
      if (sw) {
        var it = itemOf(m, +sw.getAttribute('data-g'), +sw.getAttribute('data-i'));
        if (!it) return;
        it.v = !it.v;
        sw.className = '__mc-mini-sw __mc-big' + (it.v ? ' __mc-on' : '');
        sw.setAttribute('aria-checked', String(!!it.v));
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + (it.v ? '\u5f00' : '\u5173'), it.b);
        return;
      }
      /* 覆盖清单：新增 */
      var add = t.closest('[data-act="ovr-add"]');
      if (add) {
        if (!m.ovr) return;
        var inp = S.cfg.querySelector('#__mc_ovr_new');
        var id = inp ? parseInt(inp.value, 10) : NaN;
        if (!isNaN(id) && id > 0 && !m.ovr.some(function (o) { return o.id === id; })) {
          m.ovr.push({ id: id, ms: 1000 });
          renderCfg();
          ovrCommit(m);
          toast(m.name + ' \u00b7 \u65b0\u589e\u8986\u76d6 #' + id + '\uff08\u9ed8\u8ba4 1000ms\uff09', 'now');
        }
        return;
      }
      /* 覆盖清单：删除 */
      var del = t.closest('[data-act="ovr-del"]');
      if (del) {
        if (!m.ovr) return;
        var idx = +del.getAttribute('data-idx');
        if (idx >= 0 && idx < m.ovr.length) {
          var gone = m.ovr[idx].id;
          m.ovr.splice(idx, 1);
          renderCfg();
          ovrCommit(m);
          toast(m.name + ' \u00b7 \u5df2\u5220\u9664\u8986\u76d6 #' + gone, 'now');
        }
        return;
      }
      /* T20 个性化柜子清单：新增 */
      var badd = t.closest('[data-act="bov-add"]');
      if (badd) {
        if (!m.bagOvr) return;
        var binp = S.cfg.querySelector('#__mc_bov_new');
        var bid = binp ? parseInt(binp.value, 10) : NaN;
        if (!isNaN(bid) && bid > 0 && !(m.bagOvr).some(function (o) { return o.id === bid; })) {
          m.bagOvr.push({ id: bid, w: 16, h: 12 });
          renderCfg();
          bovCommit(m);
          toast(m.name + ' \u00b7 \u65b0\u589e\u67dc\u5b50 #' + bid + '\uff08\u9ed8\u8ba4 16x12\uff0c\u4f18\u5148\u7ea7\u6700\u9ad8\uff09', 'now');
        }
        return;
      }
      /* T20 个性化柜子清单：删除 */
      var bdel = t.closest('[data-act="bov-del"]');
      if (bdel) {
        if (!m.bagOvr) return;
        var bidx = +bdel.getAttribute('data-idx');
        if (bidx >= 0 && bidx < m.bagOvr.length) {
          var bgone = m.bagOvr[bidx].id;
          m.bagOvr.splice(bidx, 1);
          renderCfg();
          bovCommit(m);
          toast(m.name + ' \u00b7 \u5df2\u5220\u9664\u67dc\u5b50 #' + bgone + '\uff08\u56de\u843d\u5230\u4e13\u5c5e/\u901a\u7528\uff09', 'now');
        }
      }
    });

    cfg.addEventListener('input', function (e) {
      var el = e.target;
      if (!el.getAttribute) return;
      /* T20 数值框仅数字清洗（data-num=int|float；type=text 无原生加减钮）：int 剥非数字，
       * float 容恰一个小数点。放在 data-act 判定之前——清单新增 ID 框（无 data-act）同受辖。 */
      var dnum = el.getAttribute('data-num');
      if (dnum) {
        var sv = (el.value || '').replace(/[^\d.]/g, '');
        if (dnum === 'int') sv = sv.replace(/\./g, '');
        else { var sp = sv.split('.'); sv = sp.length > 1 ? sp[0] + '.' + sp.slice(1).join('') : sv; }
        if (sv !== el.value) el.value = sv;
      }
      var act = el.getAttribute('data-act');
      if (!act) return;
      var m = curMod();
      if (!m) return;
      /* 覆盖清单滑条/数值 */
      if (act === 'ovr-set') {
        if (!m.ovr) return;
        var o = m.ovr[+el.getAttribute('data-idx')];
        if (!o) return;
        var ov = parseFloat(el.value);
        if (isNaN(ov)) return;
        o.ms = Math.round(Math.max(0, Math.min(5000, ov)));
        syncSibling(el, o.ms);
        var ck = el.closest('.__mc-item');
        if (ck) {
          var ckEl = ck.querySelector('.__mc-ck');
          if (ckEl) ckEl.textContent = OVR_SECTION + '.' + OVR_KEY + ' \u00b7 ' + o.id + ':' + o.ms;
        }
        throttleSend('ovr' + el.getAttribute('data-idx'), function () { ovrCommit(m); });
        toast(m.name + ' \u00b7 \u8986\u76d6 #' + o.id + ' \u2192 ' + o.ms + 'ms', 'now');
        return;
      }
      /* T20 个性化柜子清单：宽/高纯文本框（int 清洗已在上；0..40/0..30 钳制） */
      if (act === 'bov-set') {
        if (!m.bagOvr) return;
        var bo = m.bagOvr[+el.getAttribute('data-idx')];
        if (!bo) return;
        var bk = el.getAttribute('data-k');
        var bv = parseFloat(el.value);
        if (isNaN(bv)) return;
        bv = Math.round(Math.max(0, Math.min(bk === 'w' ? 40 : 30, bv)));
        bo[bk] = bv;
        var bck = el.closest('.__mc-item');
        if (bck) {
          var bckEl = bck.querySelector('.__mc-ck');
          if (bckEl) bckEl.textContent = BOV_SECTION + '.' + BOV_KEY + ' \u00b7 ' + bo.id + ':' + bo.w + 'x' + bo.h;
        }
        if (String(bv) !== el.value) el.value = String(bv); // 钳制值回写显示
        throttleSend('bov' + el.getAttribute('data-idx') + bk, function () { bovCommit(m); });
        toast(m.name + ' \u00b7 \u67dc\u5b50 #' + bo.id + ' ' + (bk === 'w' ? '\u5bbd' : '\u9ad8') + ' \u2192 ' + bv, 'now');
        return;
      }
      if (act !== 'set') return;
      if (el.tagName === 'SELECT') return; // 下拉在 change 提交
      if (el.type !== 'range' && !el.getAttribute('data-num')) return;
      var it = itemOf(m, +el.getAttribute('data-g'), +el.getAttribute('data-i'));
      if (!it) return;
      var v = parseFloat(el.value);
      if (isNaN(v)) return; // 数值框清空中途不发
      v = clampItem(it, v);
      if (it.t === 'int') v = Math.round(v);
      it.v = v;
      syncSibling(el, v);
      throttleSend('g' + el.getAttribute('data-g') + 'i' + el.getAttribute('data-i'), function () { sendSet(m, it, it.v); });
      toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + fmt(v), it.b);
    });

    cfg.addEventListener('change', function (e) {
      var el = e.target;
      if (!el.getAttribute) return;
      var act = el.getAttribute('data-act');
      if (act !== 'set') return;
      var m = curMod();
      if (!m) return;
      var it = itemOf(m, +el.getAttribute('data-g'), +el.getAttribute('data-i'));
      if (!it) return;
      if (el.tagName === 'SELECT') {
        it.v = el.value;
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + el.value, it.b);
      } else if (el.getAttribute('data-num')) {
        // T20 numbox（纯文本数值框）：提交时归位显示值（input 阶段已按钳制值发送）
        el.value = fmt(it.v);
      } else if (el.tagName === 'INPUT' && el.type === 'text') {
        // 任务 14：str 文本框——change（失焦/回车）提交整串，不在逐键 input 发送
        it.v = el.value;
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + el.value, it.b);
      }
    });
  }

  /* ================= C# → JS 推送 ================= */

  function renderAll() {
    if (!S.root || !S.root.isConnected) return;
    if (S.sub && ST.snap && ST.snap.self) S.sub.textContent = 'v' + ST.snap.self.ver + ' \u00b7 \u6539\u52a8\u5b9e\u65f6\u5199\u5165 cfg \u00b7 \u65e0\u9700\u91cd\u542f';
    renderSide();
    renderCfg();
    renderPending(); // v2 任务 7：面板重开时按已存摘要恢复底部待生效条
  }

  window.__mcRender = function (snapshot) {
    window.__mcLastSnapshot = snapshot; // 调试/CDP 断言镜像（T6 存根语义保留）
    window.__mcSnapSeq = (window.__mcSnapSeq || 0) + 1; // 推送序号（CDP 判"新快照到达"用）
    ST.snap = snapshot;
    if (ST.sel == null && snapshot && snapshot.mods && snapshot.mods.length) ST.sel = snapshot.mods[0].guid;
    renderAll();
  };

  /* ---- v2 任务 8：MC_DL 进度 → 仓库行按钮就地态 + 弹窗进度条（弹窗不重建，裁定 6） ---- */
  function onDlEvent(ev) {
    ST.dl = { repoKey: ev.repoKey, pct: ev.pct, stage: ev.stage };
    if (S.mdlRepoKey === ev.repoKey && modalOpen() && S.mdl) {
      if (ev.stage === 'ok') {
        setModal(updModalDoneHtml(repoRowOf(ev.repoKey) || { name: ev.repoKey, repoKey: ev.repoKey }));
      } else if (ev.stage === 'err') {
        setModal(updModalErrHtml(repoRowOf(ev.repoKey) || { name: ev.repoKey, repoKey: ev.repoKey }, ev.err));
      } else {
        var fill = S.mdl.querySelector('#__mc_dlfill');
        var tx = S.mdl.querySelector('#__mc_dltx');
        if (fill) fill.style.width = (ev.stage === 'down' ? ev.pct : 100) + '%';
        if (tx) tx.textContent = dlStageText(ev.stage, ev.pct);
      }
    }
  }

  window.__mcEvent = function (event) {
    window.__mcLastEvent = event;
    if (event && event.type === 'MC_SET_FAIL') {
      toast('\u2715 ' + (event.k || '?'), null, true, event.err);
    }
    /* MC_SET_OK 不二次 toast：改值瞬间已按徽标 toast，OK 回执仅落 __mcLastEvent 供断言 */
    /* 任务 16：C# 推送的原文 toast（强杀残留提示一轮；MC_OPEN 后紧跟到达） */
    if (event && event.type === 'MC_TOAST') {
      toastRaw(event.main || '', event.small || '');
      /* v2 任务 10：卸载明细等待期间到达的 toast = C# 拒绝路径（如"不在链上"）——撤掉
       * 等待壳，避免 5s 后显示误导性的"明细读取超时"。 */
      if (ST.uninstGuid != null) {
        ST.uninstGuid = null; clearUninstTimer();
        if (modalOpen()) closeModal();
      }
    }
    /* v2 任务 7：待生效清单摘要（pending.json 泵 2s 巡检推送；面板关着时只存 ST.pend，
     * 重开面板 renderAll 恢复底部条） */
    if (event && event.type === 'MC_PENDING_SUMMARY') {
      ST.pend = event;
      renderPending();
    }
    /* v2 任务 8：仓库快照（MC_REPO_DATA）——入口卡计数 + 仓库视图重绘；弹窗是 cfg 的
     * 兄弟节点不受 renderCfg 影响（弹窗开着绝不重建，进度条由 MC_DL 就地更新——裁定 6） */
    if (event && event.type === 'MC_REPO_DATA') {
      ST.repoData = event;
      /* T8 遗留修③（评审转交）：dl 缺位即下载链空闲——无条件复位（checking 只描述 fetch，
       * 不再作为保留陈旧进度的理由；自愈语义=REPO_DATA 对 dl 态权威）。 */
      if (event.dl) ST.dl = { repoKey: event.dl.repoKey, pct: event.dl.pct, stage: event.dl.stage };
      else ST.dl = null;
      renderSide();
      /* v3：头部「检查更新」在等结果 → 拼汇总 toast（spec D8；C# 零改动）。 */
      if (ST.chkWaiting) {
        ST.chkWaiting = false;
        var c = repoCounts(), parts = [];
        if (c.upd) parts.push(c.upd + ' \u4e2a\u53ef\u66f4\u65b0');
        if (c.inst) parts.push(c.inst + ' \u4e2a\u53ef\u5b89\u88c5');
        if (c.warn) parts.push(c.warn + ' \u4e2a\u7248\u672c\u5f85\u786e\u8ba4');
        toastRaw(parts.length ? '\u68c0\u67e5\u5b8c\u6210\uff1a' + parts.join('\u3001') : '\u68c0\u67e5\u5b8c\u6210\uff1a\u90fd\u662f\u6700\u65b0\u7248',
          parts.length ? '\u5217\u8868\u91cc\u5df2\u7528\u56fe\u6807\u6807\u51fa\uff0c\u70b9\u56fe\u6807\u6216\u8fdb\u8be6\u60c5\u5904\u7406' : '');
      }
    }
    /* v2 任务 8：下载链进度/结果（MC_DL：down|check|unzip|write|ok|err） */
    if (event && event.type === 'MC_DL') onDlEvent(event);
    /* v2 任务 10：卸载明细（MC_UNINST_DATA）→ 占位壳换真值确认弹窗 */
    if (event && event.type === 'MC_UNINST_DATA') onUninstData(event);
    /* v2 任务 11：冲突详情（MC_CF_DATA）→ 占位壳换真卡片（L1 卡片 + L3 同域卡） */
    if (event && event.type === 'MC_CF_DATA') onCfData(event);
    /* v2 任务 12：L2 补丁碰撞扫描数据（MC_SCAN_DATA）→ 侧栏汇总行 + 汇总弹窗就地重绘 */
    if (event && event.type === 'MC_SCAN_DATA') {
      ST.scan = event;
      var wasWait = ST.scanWait;
      ST.scanWait = false;
      renderSide();
      if (ST.patchOpen) { setModal(patchModalHtml()); bindPatchModal(); }
      if (wasWait) toastRaw(event.error ? '\u626b\u63cf\u5931\u8d25' : ('\u626b\u63cf\u5b8c\u6210\uff1a\u78b0\u649e ' + (event.total || 0) + ' \u7ec4'),
        event.error || ('\u53ef\u5f52\u5c5e ' + (event.routed || 0) + ' \u7ec4 \u00b7 \u672a\u77e5\u6765\u6e90 ' + (event.unknown || 0) + ' \u7ec4'
          + ((event.framework || 0) > 0 ? ' \u00b7 \u6846\u67b6\u4fa7 ' + event.framework + ' \u7ec4\u672a\u8ba1\u5165' : '')
          + ' \u00b7 ' + (event.ms || 0) + 'ms'));
    }
    /* v2 任务 9：排序数据（MC_ORDER_DATA：guids=生效序 / rows=显示元数据 / def=默认序）。
     * 面板开着且在排序模式才重绘；数据本体存 ST.ordRows（重进排序模式前可参照）。 */
    if (event && event.type === 'MC_ORDER_DATA') {
      ST.ordRows = event;
      if (ST.ord) { ST.order = event.guids ? event.guids.slice() : []; renderSide(); }
    }
    /* v2 任务 9（裁定 5）：保存成功 → 退出排序模式 + toast；待生效条"加载顺序调整"由
     * C# 同批推的 MC_PENDING_SUMMARY 呈现；行内无 ⟳（order 全局非单 mod，不挂行）。 */
    if (event && event.type === 'MC_ORDER_OK') {
      ST.ordSaving = false;
      if (ST.ord) { ST.ord = false; ST.order = null; renderSide(); renderCfg(); }
      toastRaw('\u987a\u5e8f\u5df2\u4fdd\u5b58',
        '\u91cd\u542f\u6e38\u620f\u540e\u7531\u542f\u52a8\u6e05\u7406\u5668\u5e94\u7528\uff08\u5e95\u90e8\u5f85\u751f\u6548\u6761\u53ef\u67e5\u770b\uff09');
    }
    /* v2 任务 9：整单拒绝（校验失败）——留在排序模式，本地调整不丢可重试 */
    if (event && event.type === 'MC_ORDER_FAIL') {
      ST.ordSaving = false;
      toastRaw('\u4fdd\u5b58\u987a\u5e8f\u88ab\u62d2\u7edd',
        (event.err || '\u6821\u9a8c\u5931\u8d25') + '\uff08\u8be6\u89c1 LogOutput\uff09\uff1b\u672a\u5199\u5165\uff0c\u53ef\u91cd\u8bd5');
    }
  };

  /* ================= 开关 ================= */

  window.__mcOpen = function (hostDoc) {
    if (S.root && S.root.isConnected) return; // 已开
    var doc = (hostDoc && hostDoc.body) ? hostDoc : S.host;
    if (!doc || !doc.body) return;
    S.host = doc;
    S.root = build(doc);
    doc.body.appendChild(S.root);
    renderAll(); // 已有快照则立即出内容，否则"正在读取…"占位
    window.__mcOutbox.push({ type: 'MC_OPEN', host: doc.location.pathname });
  };
  window.__mcClose = function () {
    if (!S.root) return;
    var el = S.root;
    S.root = null; S.side = S.cfg = S.sub = S.toastEl = null;
    S.pendEl = null; S.pendTx = null; S.mbg = null; S.mdl = null; S.mdlRepoKey = null; // v2 任务 7/8：弹窗/待生效条随面板拆
    ST.uninstGuid = null; clearUninstTimer(); // v2 任务 10：卸载明细等待随面板拆终止
    ST.cfGuid = null; clearCfTimer();         // v2 任务 11：冲突详情等待随面板拆终止
    ST.patchOpen = false; ST.scanWait = false; // v2 任务 12：碰撞汇总弹窗态随面板拆清
    ORD.src = null; // v2 任务 9：面板拆=拖拽会话终止（重开面板需重新 dragstart）
    if (el.isConnected && el.parentNode) el.parentNode.removeChild(el);
    /* 关闭即清节流句柄（评审备忘 3）：撤销未触发的 trailing 补发，面板关闭后不再发消息 */
    for (var id in TH) if (TH[id] && TH[id].timer != null) clearTimeout(TH[id].timer);
    TH = Object.create(null);
    window.__mcOutbox.push({ type: 'MC_CLOSE' });
  };
  window.__mcEsc = function (e) {
    if (e && e.key !== 'Escape') return;
    if (!S.root) return;
    /* v2 任务 7（裁定 1）：弹窗开着时 ESC 先关弹窗（不关面板）；再按才关面板 */
    if (modalOpen()) { closeModal(); return; }
    window.__mcClose();
  };

  /* Root 文档自身的 ESC（焦点在 Root 时兜底；host doc 的 ESC 由 MenuEntryInjector wireKey 转发；
   * OutSetting 场景 ESC 会被 Unity 截走——✕/点遮罩是保底关闭路径（T5 结论），ESC 仅增强 */
  document.addEventListener('keydown', function (e) { window.__mcEsc(e); });

  /* ---- 面板孤儿看门狗（任务 8）：本脚本活在 Root 上下文，宿主 iframe 消亡不影响本定时器。
   * 宿主文档被游戏重建/导航（如暂停菜单被 Unity 侧 ESC 收起）时面板 DOM 随之消亡，但 iframe
   * 内外都发不出 MC_CLOSE——由 Root 侧每 1s 检查：面板开着的根突然失连 ⇒ 补发 MC_CLOSE
   * （C# 键盘门双还原 + 脏 cfg 落盘收口，FocusTick.cs 消费；层整体消亡另有 C# 侧兜底）。 */
  setInterval(function () {
    if (!S.root) return;
    /* T17 修复（实锤）：本引擎（Vuplex/CEF）里被移除 iframe 的孤儿文档内元素 isConnected 恒
     * true（与 DOM 规范不符，实测 removed 后仍 true），旧判据对宿主 iframe 被移除永远判活——
     * 看门狗不触发，键盘门漏开（enterworld 实证：开面板点继续，kb 恒 on）。改走宿主文档归属
     * 判定：doc.defaultView.frameElement 在 iframe 被移除或文档被导航后即为 null（两死法均
     * 实测），frameElement.isConnected（Root 文档内元素）语义正常。 */
    var alive = false;
    try {
      alive = !!(S.root.isConnected && S.host && S.host.defaultView
        && S.host.defaultView.frameElement && S.host.defaultView.frameElement.isConnected);
    } catch (e) { alive = false; }
    if (alive) return;
    S.root = null; S.side = S.cfg = S.sub = S.toastEl = null;
    S.pendEl = null; S.pendTx = null; S.mbg = null; S.mdl = null; S.mdlRepoKey = null; // v2 任务 7/8
    ST.uninstGuid = null; clearUninstTimer(); // v2 任务 10
    ST.cfGuid = null; clearCfTimer();         // v2 任务 11
    ST.patchOpen = false; ST.scanWait = false; // v2 任务 12
    ORD.src = null; // v2 任务 9
    for (var id in TH) if (TH[id] && TH[id].timer != null) clearTimeout(TH[id].timer);
    TH = Object.create(null);
    window.__mcOutbox.push({ type: 'MC_CLOSE', why: 'host-doc-gone' });
  }, 1000);
})();
