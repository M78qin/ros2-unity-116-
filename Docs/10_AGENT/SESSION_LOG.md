# 会话日志（只追加，最新在上）

格式：每条 = 时间 / 操作者 / 目标 / 做了什么 / 证据 / 结论状态 / 遗留。

---

> **同步状态（2026-11-06 15:2x）**：本条日志与以下文档同步维护 ——
> `TASKS.md`（A44–A54 状态）、`P0_AUDIT_2026-11-05.md`（§13–§20 纠正与各批追加）、
> `Docs/10_AGENT/DOC_SYNC_2026-11-06.md`（文档/证据/备份同步清单，含哈希）、
> `src/进度记录/2026-11-06_UI重构进度总表.md`（P1a–P6 台账）、`src/进度记录/2026-11-03_UI面板重构.md`（批次追加）、
> `src/_index/Codex审计导航.md`（入口）、`agent_runs/dsh_2026-11-03_ui/evidence_register.csv`（75 行）。
> 场景当前基线：`9F907809DD8B5325021C087C0F6B3B6FB158857CF2FCF6F17725116D572DCD45`（6,976,838 B）。

---

## 2026-11-06 15:13 · DSH-Agent · P6 UI 精修完成（拖拽停靠 + Build 性能 + 圆角 + Wide 图）

- `UI/41` 10/10：悬浮窗拖到主窗出现停靠预览→松手 Docked；dock 卡片拖出→Floating(≥480×330)→拖回 Docked；走 `TwinUiInput.WindowDrag`，不新起输入路径。
- `UI/42` 构建成功（0 errors/3 warnings，2 分 37 s）→ `Builds/p6_perf/TwinUI_P6_Perf.exe`；先修 `BatteryLoginUIBuilder.cs` 的 `UnityEditor` 误用（`#if UNITY_EDITOR` 包裹，备份留档）。
- Player 60 s 实测：fps 60.04/60.10/60.10（vSync 60 封顶）、帧时间 16.65 ms、引擎内存 133.94 MB、托管 13.12 MB、渲染器 144；Editor 基线 203.47/204.50/211.91、4.92 ms、476/622 MB；口径差异已写明（不可横比）。
- `UI/33` 追加圆角断言 `cards=110 controls=39 badType=0` PASS；`UI/44` 出 1600 宽 Wide 备用图。
- 回归 `UI/12/16/17/18/28/30/33/35/38/39/41` 全 PASS；场景未变 `9F907809DD8B5325021C087C0F6B3B6FB158857CF2FCF6F17725116D572DCD45`。
- 事故：14:41 编辑器自动写盘致场景短暂 `F41FFB9A…`（5 个 TMP 字体色），已留档并从 P4 基线恢复到 `9F907809…`。

## 2026-11-06 14:25 · DSH-Agent · 论文图 4-15 换新面板截图（A52 完成）

- 图 4-15 由 1600×900 占位图换成 `UI/40` 的 1920×1080 Ocean 窗口化截图（sha256 `1e634204…`，516,203 B）；旧图备份 `03_图/out/_backup/fig4_15_ui_panel_1600x900_2026-11-04.png`。
- 图注按事实重写（Ocean、窗口化、1920×1080、占屏 56.25%×92.0%、contentScale=1.000、性能模块停靠 dock）；md 中"占位图/占位截图/V3"清零；按图为准删掉 3 处与图不符的描述（五项指标数值区/阈值带/"未订阅"文案、回放夹具绑定、三列宽屏暗示）。
- docx 重出：`801a4810…`（1,487,602 B）→ `04ce8775…`（1,712,578 B）；渲染 24 页不变、图与图注同页（p15 y878–1322 / 图注 y1342–1360）、几何 0 问题；`final_check` 82 PASS、`audit_docx_layout` 25/25、`validate_claim_csv` / `verify_chapter4_claims` PASS；CJK 11,991 → 11,986。
- 同步：`figure_spec.md`、`claim_evidence.csv`（C-FIG-01 备注 + C-UI 行注记，figure_id/结论未改）、写作说明 §0/§3 第 9 项标完成、自审记录；`assemble_chapter4.py` 删除两条注入占位文字的 PATCH。
- 修构建 bug：图注识别规则 ≤90 字 → ≤260 字且不含句号（否则新图注被排成正文）。
- 边界：截图未人工目视；Editor Play 非 Player Build；渲染器非 Word，分页以 Word 为准。

## 2026-11-06 13:56 · DSH-Agent · P5 浮窗收口 + 多模块 + 论文截图

- Frame host 抽象：`ITwinUiFrameHost` + `TwinUiWindowFrame.Configure(host adapter)`，悬浮窗真正复用 P2 外壳（手柄 11/23、圆角 13、Ocean 令牌、遮罩/阴影一致）；P4 自实现手柄组件删除。
- 多模块 dock：`performance / datalink(events) / task(settings)` 3 个真实模块，默认 3 卡单页，收窄后 `perPage=2/pageCount=2` 翻页；dock/menu 挂 `UiWindowRoot` 不污染 `UI/30` content 断言。
- 吸附/多浮窗：24px 左/右/上吸附、双窗错位不重叠、z 序 + `BringToFront`、Esc 关最上层后回 Hidden。
- `UI/39` 22/22 PASS；`UI/38` 20/20 PASS；回归 `UI/12/16/17/18/28/30/33/35` PASS（`UI/18` 首跑 1 项为鼠标 hover 手柄的偶发差异，复跑 PASS，两次留档）。
- `UI/40` 产出论文图：`thesis_fig4_15_ui_1920.png`（1920×1080，窗口 1080×993.6 = 56.25%×92%，contentScale=1，breakpoint=Narrow，Ocean，性能模块停靠紧凑）。
- 场景逐字节未变 `9F907809DD8B5325021C087C0F6B3B6FB158857CF2FCF6F17725116D572DCD45`（6,976,838 B），diff=0；无需新备份。
- 边界：吸附非磁吸动画、无跨窗拖靠、截图未目视。

## 2026-11-06 13:19 · DSH-Agent · P4 模块框架 + 性能监测模块完成

- 用户需求：把旧第三章拆解系统的性能监测 UI（`PerformanceProfiler` IMGUI 面板）集成进新主面板，做成可分离模块（Docked/Floating），供后续模块化设计复用。
- 交付：`Scripts/UI/Modules/` 7 个文件（`TwinUiModule`、`TwinUiModuleHost`、`TwinUiModuleWindow`、`TwinUiModuleResizeHandle`、`TwinUiPerformanceModule`、`TwinUiPerformanceModuleView`、`TwinUiFpsSparkline`）+ `Editor/TwinUIModuleStage.cs`（`UI/37/38`）+ `PerformanceProfiler` 只读 API 扩展。
- `UI/38` 20/20 PASS：三态往返、Docked 不越界（1048 宽内容区 1078）、悬浮窗可拖可缩（480×340→520×364）、手柄 11/23、圆角 13、数据同源（`viewFps=186.00=profFps`）、旧面板接管/还原、`totalErrors=0`；8 项回归 PASS（含 UI/35 57 项）。
- 场景 `EAFD0A7A…` → `9F907809DD8B5325021C087C0F6B3B6FB158857CF2FCF6F17725116D572DCD45`（6,976,838 B），vs P3 仅 +1 组件；备份 `_backup/scenes/2026-11-06_p4_modules_final/`；Edit 模式 `isDirty=false`。
- 已知边界：悬浮窗未直接复用 `TwinUiWindowFrame` 组件（强绑定 host）；一批自检真问题已修（域重载 themeGuard、FpsChart CanvasRenderer、采样节流、UI/30 断言污染等）。
- 下一步：P5 浮窗体系收口（Frame host 抽象、多模块 dock/吸附、第二模块）+ 论文图 4-15 换新面板截图。

## 2026-11-06 12:44 · DSH-Agent · 宿主两次中断后恢复（Unity 重启）

- 12:09、12:43 前后宿主会话两次被中断，Unity 编辑器进程随之退出；MCP 8091 关闭。
- 12:44 用 `E:\new_2026-\unity\editor\2022.3.62f3c1\Editor\Unity.exe -projectPath E:\new_2026-\unity\project\Unity_Ros_Try1` 重新拉起（pid 24960），8091 恢复；`get_scene_info` = `Doosan-DT.unity` 已加载、`isDirty=false`。
- 场景仍是 P3 终态 `EAFD0A7A…`（6,976,443 B），备份 `_backup/scenes/2026-11-06_p3_responsive_final/` 已补建。
- P4（task-12）重启时尚未落盘任何代码/证据，已按"先写文件 + 增量 PROGRESS.md"要求重新开工，避免再次中断丢进度。

## 2026-11-06 12:08 · DSH-Agent · P3 响应式重排完成（去缩放 + 三档断点 + A49/applier 修复）

- 去 scale-to-fit：`UiWindowContent.localScale=1`，窗口默认 `(824,43,1080×994)`，内容 1:1；三档 `Wide≥1500 / Medium 1100–1499 / Narrow<1100`。
- `UI/35` **57 项全 PASS**（fails=0, shots=5）：1600 档 420/610/552 + Eval 内联 + Header 折 ModeChip 后 7 chips；1300 档 320/970 + Eval 552 抽屉；1000 档 300/690 + Eval 抽屉=Detail 宽。三档 bbox 全 `overflow=False`、TMP 字号 24/18/18 不变、`localScale=1.000`、抽屉可开可关。
- `[AUDIT] UI/12 restoredTheme=Ocean sceneDirty=False`、前后场景哈希不变（A49 修复验证）；`[REPAIR] scanned=3053 repaired=1271`（applier×2 + toneTag×1269），二次 `repaired=0` 幂等；`[THEME] appliers=2 applierAssetScript=True themeGuard=ok`（根因已修）。
- 场景 `8A4CB02B…` → `EAFD0A7A0C02552B06628ED44BC0969510E0D8601CD7ED3D09F82E9EE0365C65`（6,976,443 B）；块 10674→10669（removed=5 内嵌 MonoScript）、modified=1295；无 GameObject 增删/层级变更。备份 `_backup/scenes/2026-11-06_p3_responsive_final/`。
- 回归：`UI/12/16/17/18/28/30/33` 全 PASS（UI/18 21 项、UI/30 11 项、UI/28 10 项）。
- 异常登记：12:01:52 一次场景+设置写盘（紧邻 UI/30 自检，机制未定位），diff 为等价重排，已列为待查。
- 下一步：P4 模块框架 + 性能监测模块（task-12）已开工。

## 2026-11-06 11:36 · DSH-Agent · P2.5 Ocean 换肤完成（中断恢复后收尾）

- 11-05 22:43 的 P2.5 被中断（留下半成品编译错误 + 已应用但未验证的场景）；11-06 10:47 Unity 重启（pid 3100），复核确认编译 0 error、场景 `02701C5C…` 已带 Ocean 颜色（275 个 MonoBehaviour 改色），续跑补齐验证。
- 修掉中断残留：`UI/33` 域重载后静态字段 NRE（曾刷爆日志需强杀）、断言参数错位假失败、按钮/文本选取规则、`TwinUIToneTag` 覆盖不足（125/788 → 补 557 个到 682/788）。
- `UI/33` PASS：标题栏 Layer2 `#173345`、窗口底 Layer `#0E2535`、边框 Border `#2B5268`、卡片 `#132E40`、正文 `#E7F5FF`、Accent `#39C5E5`、PlotBg `#0A1E2D`、Grid `#23455A`、阴影 `#000000@64`、描边 `#53D4E6@85`、按钮常态/悬停/按下 `#173345/#1E4257/#26506A`、手柄 hover Accent@95 全部实测一致。
- 回归 `UI/12/16/17/18/28` 全 PASS，五次前后场景哈希不变；`UI/12` 未再复现 Light 污染。
- 场景 `C4E3F232…` → `8A4CB02BAD52165C9E8F6E99E4A8214FB0E28C5B0852322CC8B3ABA055532273`（6,918,569 B）；`TwinUiSettings.themeMode=2`；备份 `_backup/scenes/2026-11-06_p25_ocean_final/` 与 `2026-11-06_p25_ocean_applied/`。
- 发现的历史缺陷：`TwinUIThemeApplier` 的 `m_Script` 为场景内嵌 MonoScript（无 guid），Play 不实例化；改由 `TwinUiWindowHost.bootTheme` 引导，根因待修。
- 边界：只换肤未重排（窗口仍 68% + scale 0.68），与原型 41% + 重排的差距留给 P3。

## 2026-11-05 22:30 · DSH-Agent · P2 窗口化完成 + V3 原型规格提取

- **P2 完成**：新增 `Scripts/UI/Window/TwinUiWindowHost.cs`、`TwinUiWindowFrame.cs`、`Editor/TwinUIWindowStage.cs`；`UI/29` 挂载（`attached=2 saved=yes`）、`UI/30` 自检 **11 项全 PASS**：默认窗口 1306×994 右停靠、拖动位移一致、8 向缩放（+120,+80）、最小钳制 1150×648（scale 0.599）、最大化 1920×1080（scale 1.000）往返、`PlayerPrefs` 持久化 roundtrip、TabBar 切页、`totalErrors=0`。首轮 3 项 FAIL（高度方向符号、最小高度、还原缩放）已修复并留档 `ui30_run1_FAIL.txt`。
- 场景：`E1FF34F8…` → `C4E3F232CE4B46DC11BC2534D68182B53512EE80341616800840037B02541339`（6,716,778 B）；YAML +2 个 MonoBehaviour（host/frame），modified=1（组件列表）；备份 `_backup/scenes/2026-11-05_p2_window`。
- **V3 规格提取（并行线 ui-web，只读）**：`Docs/10_AGENT/V3_UI_SPEC_2026-11-05.md`（494 行）：39 个 `TwinUIPalette` 字段逐字段 hex 映射、建议新增 `Layer/Layer2/LineStrong/Strong` + `Shadow` + `UiScale`；主窗实测 (1106,82,790,710)=41.15%×65.74%，手柄边 11px/角 23×23；30 条差距（缺 12/不同 13/已有 5）。证据 `agent_runs/dsh_2026-11-05_v3_spec/`（5 张 HTML 渲染截图，已声明非 Unity 实测）。
- **判断纠正（重要）**：用户认为"面板 UI 没变、不像原型"——属实。不是色差问题，而是 **41% 窗口 + 内容重排** 与 **68% 窗口 + 整体 scale 0.68** 的形态差异；据此把 P2.5 限定为视觉层，矩形与响应式重排进 P3。
- 下一步：P2.5 Ocean 换肤（`UI/31-33`，teammate `ui-interaction-b`），并行 thesis-scaffold 做第 4 章 docx 渲染复核 + 图 4-15 占位标记。

## 2026-11-05 22:08 · DSH-Agent · P1b-2 输入仲裁 + 死 EventSystem 清理

- 新增 `Scripts/Interaction/TwinUiInputArbiter.cs`（302 行，`6FE7391D…`）：栈式所有权，数值即优先级 `None(-1)<Camera(0)<SceneRay(1)<IkHandle(2)<UiPointer(3)<WindowDrag(4)<WindowResize(5)<Modal(6)`；`Begin/End/Allows` 无分配、无 LINQ、无 Update；Play 缺失自动创建。
- 接入：`TwinPlayCameraRig`（右键/中键持有 Camera、`respectInputArbiter` 可关）、`TwinPlayIkHandle`（拖动持有 IkHandle）、`TwinPlayRayVisual`（只读遮挡查询，命中逻辑未改）。
- 新增 `Editor/TwinUIInputStage.cs`（906 行，`4C7AD6CC…`）：`UI/26` 挂载仲裁器（`attached=1 saved=yes`）、`UI/27` 清理死 EventSystem（实名为 `EventSystem (DT_停用)`，改名 `EventSystem_DISABLED_DO_NOT_ENABLE`、确保 inactive）、`UI/28` 仲裁自检。
- `UI/28` 10/10 PASS：无所有者时 yaw Δ75.329°；`Begin(Modal)` 后 0.000°；指针在 UI 上 0.000°；IK 拖动中 0.000° 且手柄仍拖；释放后恢复 75.329°；`totalErrors=0`；截图 3 张。首跑 FAIL（0.477° 平滑残差）已改判定方式，`ui28_run1_FAIL.txt` 留档。
- 场景：`8409E9530D4304D9D6502667F0B5188AC633DD317069EF0C5C40668677D152BA`（6,715,384 B）→ `E1FF34F8395F8660D781724DF80184ECC56DB82A4B2B23B1B6E6D889CDD5B004`（6,715,804 B）；块 10,104→10,105（+1 仲裁器组件），modified=4（组件列表、rig 新字段、EventSystem 改名、计时文本 00:03→00:00 运行期预览文本）。
- 回归：`UI/12` failures=0；`UI/16` missingDistinct=0；`UI/17` 22 项 PASS/`totalErrors=0`；`UI/18` 21 项 PASS/`dispatchCalls=0`；编译 new_errors=0（9 行 error CS 为 P1b-1 开发期历史）。
- A 线证据保护：跑 `UI/18` 前把 `dsh_2026-11-04_ui_play\ik` 整目录（107 文件）备份并事后还原；新运行另存 `logs/ui18_overwrite/`；未伪造 run5。
证据：`agent_runs/dsh_2026-11-05_p1b_input/`（README 199 行）。

## 2026-11-05 21:45 · DSH-Agent · P1b-1 唯一渲染主相机 + 射线 mask + 性能基线

- **P0-1 纠正（重要）**：P0 审计原判"两个 active 相机双渲 + Camera.main 歧义"**不成立**——`XR Origin (XR Rig)` 的 `m_IsActive: 0`，XR 相机 `activeInHierarchy=False`，实际 `Camera.allCameras.Length=1`、`Camera.main` 唯一。原判因只查 `activeSelf` 未查父节点，降级为 REPORTED_ONLY 并已纠正（P0 文档 §13）。
- 新增 `Editor/TwinUICameraStage.cs`（861 行，`B4203E7B…`）：`UI/24` 相机审计（只读）、`UI/25` 应用（禁用 XR/遗留相机组件 `Camera.enabled=0`，防回归；幂等）、`UI/20` Editor 侧性能基线。
- 射线 mask：`TwinPlayRayVisual` 默认 `~((1<<2)|(1<<5))` → `1<<0`；`TwinUIPlayBuilder` 的 `UI/6`/`UI/17` 同步；场景 layer 0:2344 / 2:10 / 5:97 → 19→1 等价（1296 探针命中 684 相同，机器人命中 37/9 对象）；`UI/17` mask=1 实跑 22 项 PASS、`colorDelta=0`。
- 场景：`5EC7EB2CAB026BC921953EFFBEE6AF08AA5E166A16FD7811E88FD89EA8706937` → `8409E9530D4304D9D6502667F0B5188AC633DD317069EF0C5C40668677D152BA`（6,715,384 B）；YAML 10104 块 0 增删，modified=3（2×Camera enabled、1×m_Bits）。
- 回归：`UI/12` failures=0；`UI/16` missingDistinct=0；`UI/17` 22 项 PASS/0 失败/`totalErrors=0`；`UI/18` 21 项 PASS/0 失败/`totalErrors=0`（TCP 1.458 mm、dispatchCalls=0）；`Assets/Refresh` new_errors=0。
- 性能基线（Editor Play，单会话 3 s）：541 帧，fps 180.31 / p50 185.88 / p95 200.35，相机 1，`ProfilerRecorder` 不可用；**仅 Editor 侧，不代表 Player Build**；与 `UI/15` 的 60Hz→50.30Hz 不同口径，待重测。
- 事故（如实）：① 首次 `UI/25` 保存被 P1a 残留主题状态污染（`C3BD9C40…`），已回滚重做并留证；② `UI/18` 覆盖 A 线 `ui_play/ik` 固定名证据（run5 无法恢复），已另存并写 NOTE。
证据：`agent_runs/dsh_2026-11-05_p1b_camera/`（README 230 行）。

## 2026-11-05 21:20 · DSH-Agent · P1a 遗留 UI 舞台化批1

teammate `ui-interaction-b` 完成 P1a（同一 Unity 单写入者，全程 Edit 模式收尾）。
- 备份先行：`_backup/2026-11-05_p1/`（场景 `ED7DC59C96E6879540E0E33395EAA7C840F0533670069F7DA1956D6CE6C03362`、TagManager、TwinUiSettings、TwinRuntimeProfile、`ui_components_inventory.json` 65,383 B、`manifest.txt` 逐文件 sha256）。
- 新增 `Assets\DoosanDT\10_DigitalTwin\Editor\TwinUILegacyStage.cs`（23,392 B，sha256 `3E39D1C7…500D463`）：`UI/21` 盘点、`UI/22` 禁用、`UI/23` 恢复，均幂等、Play 下拒绝执行。
- 实测：`UI/22` 第一次 `disabled=3 activeCanvas=1 saved=yes`；第二次 `disabled=0 saved=no`（幂等）；`UI/23` `restored=3 activeCanvas=4 saved=yes`；最终再 `UI/22` 回到 activeCanvas=1。撤销名单：`panel1`、`panel2_R`、`panel2_R (1)`（均 WorldSpace，只置 activeSelf=false，未删除、未移动）。
- 场景：`ED7DC59C96E6879540E0E33395EAA7C840F0533670069F7DA1956D6CE6C03362`（6,709,527 B）→ `5EC7EB2CAB026BC921953EFFBEE6AF08AA5E166A16FD7811E88FD89EA8706937`（6,715,385 B）；YAML 10104→10104 块无增删，588 修改块中仅 3 个 GameObject 的 m_IsActive 1→0，其余为 Unity 保存时补写 `Role: 0` 噪声。
- 回归：`UI/12` failures=0；`UI/16` missingDistinct=0（texts=531/checkedChars=3515）；`UI/17` 禁用前后各 22 项全 PASS、`totalErrors=0`、截图 5/5；`Assets/Refresh` new_errors=0。
- 受保护文件未变：TagManager `B6C6AE19…`、TwinUiSettings `5335D6B1…`、TwinRuntimeProfile `64503D36…`、数据链 5 脚本哈希不变。
批次：`agent_runs/dsh_2026-11-05_p1_legacy/`（README 18936 B + logs + shots edit2/play_before5/play_after5）。
注意：禁用≠删除；Play 仍被 `UI_二期面板` 全屏遮挡 3D（P2 窗口化解决）；相机/Layer/EventSystem/InputArbiter 未动（P1b）。

## 2026-11-05 21:00 · DSH-Agent · P0 现状审查（AUDIT+PLAN）

按外部 Skill `参考ai/unity-dt-ui-workspace/SKILL.md`（602 行）执行 P0 审计，**未改代码/场景/资产**（仅备份重存后场景）。
- 事实：Unity 2022.3.62f3c1；URP 14.0.12；uGUI 1.0.0/TMP 3.0.9；XRI 3.4.0/OpenXR 1.14.3；场景 2451 GO / 10,104 对象 / 13 Canvas（4 active）/ 324 Image / 576 TMP / 38 按钮 / 6 图表。
- 关键问题：**2 个 active MainCamera 全场景双渲且 Camera.main 歧义**；全屏 UI 遮挡 3D（A2 截图需隐藏 2 个 Canvas）；无窗口化/浮窗/八向缩放；输入所有权未统一；刷新与绘图无预算（60 Hz 档实测 50.3 Hz）；数据语义未贯穿；实验 UI 两套并行；**955 个失联组件**（710 无 guid + 245 引用 12 个不存在 guid）；场景于 20:42:34 被外部重存（`F4AF5927…`→`ED7DC59C…`，对象增删 0，586 块丢掉陈旧 Role 字段）。
- 建议架构：现有 `TwinUiSnapshot`/`TwinUiPipeline`/R3 shell 上增量加 `TwinUiWindowHost` + `TwinUiInputArbiter` + `UiBudget` + `TwinUiValueState`；不重写、不换 UI Toolkit、不新建平行架构。
- 阶段：P0 完成；P1 舞台/输入收敛（2–3 人日）→ P2 主窗窗口化 → P3 浮窗/响应式 → P4 数据语义+性能 → P5 控制状态机 → P6 实验工作空间（数据侧并行）→ P7 VR（本轮不排期）。
- 用户决策：Q1 P1 优先；Q2 遗留资产"先禁用→观察→删除"；Q3 本轮只做桌面+接口兼容。
证据：`agent_runs/dsh_2026-11-05_p0_audit/`（README + 8 个只读解析脚本 + logs）。

## 2026-11-05 18:10 · DSH-Agent · 拖动末端法兰 IK ghost 预览（A2 级别 1）

teammate `ui-interaction-b` 完成 A2：Play 模式左键拖动末端法兰 → IK 解算 → ghost 预览，**全程零下发**。
- 新增：`Scripts/Interaction/TwinPlayIkHandle.cs`(1549 行)、`Editor/TwinUIIkBuilder.cs`(1555 行，菜单 `UI/7`、`UI/18`)；场景仅由 `UI/7` 保存（含 4 个 IK 参数 enableIk=1/autoSolveOnDrag=0/tol=0.0015/maxSolveTimeMs=10）。
- `UI/18`（run5，25 条 item 全 PASS）：拖动 +10 mm → moved=10.00mm；单帧护栏 maxStep=1.00mm（限 5）；ghost jointMaxDelta=29.294°；位置误差 1.458 mm（<2，iter=4，Solved）；零下发 `allowRobotDispatch=False/dispatchCalls=0/enterExecuteCalls=0`；+100 mm 请求只走 5.00 mm；X 轴锁定 moved=(0.01,0,0)；AABB ±0.5 m clamped=True；不可达 #FF3833 / posErr=1984.6 mm；取消回 LiveFeedback、回当前 dist=0.000 mm；截图 6/6；exceptions=0。
- 回归：`UI/12` failures=0、`UI/16` missingDistinct=0、`UI/17` 22 项 PASS、`Assets/Refresh` 0 error。
- 场景：`52063677…` → `F4AF5927…`（6,715,395 B），备份 `_backup/scenes/2026-11-04_play_ik`；数据链 5 脚本 + `TwinRuntimeProfile.asset` 哈希不变。
- 边界：输入为组件测试 API 注入（非物理鼠标）→ 手感 UNKNOWN；未接真机、真机联动 UNKNOWN；IK 为位置-only，精度依赖模型 TCP 参数；截图仅 PIL 统计未目视。真机下发门槛见 TASKS A40（需 A35 修复 + 安全评审 + 用户授权）。
批次：`agent_runs/dsh_2026-11-04_ui_play/ik/`。

## 2026-11-04 23:00 · DSH-Agent · 第四章写作工作区（B 线，新会话预备）

teammate `thesis-scaffold` 建成 `src/四章/写作_2026-11/` 并完成环境、样图、技能、claim 表（56/56 自检 PASS）。
- 目录：`00_计划/KICKOFF.md`(24 KB，故事线/素材/四步流水线/10 条证据纪律/4.1–4.7 分节计划/可粘贴首轮提示)、`01_文献/README.md`(30 篇 C01–C10/E01–E20 台账)、`02_证据/claim_evidence.csv`(25 行×9 列：VERIFIED 14/PLANNED 5/UNKNOWN 3/REPORTED_ONLY 3)、`03_图/{figure_spec.md,scripts,out}`、`04_稿`、`05_审阅`、`06_工具/venv`。
- 环境：Python 3.12.14 + matplotlib 3.9.2（离线轮子装成，pip 在线"能算不能退"，见 ENV_REPORT.md）。
- 样图：`fig_e1_interval.png` 448,192 B/300 dpi/2149×1784，sha256 `27f589b4…b4fef964`；SVG 200,549 B；图注含 WSL 路径+sha256+"pilot 切片，非正式实验"。
- 技能：`anti-defensive-writing`（上游 Adkid-Zephyr/anti-defensive-writing-Skill，5088★，MIT，经 gh-proxy 取得，改写为本论文五级证据口径，SOURCE.md 记来源）、`thesis-figure-pipeline`（自建）。
- **口径修正**：分析窗以 `manifest.yaml` 的 `analysis_start_ns/analysis_end_ns`（20.000 s）为准，非"末 2000 行"；按时间窗 p50/p95/p99/max 与官方 `summary.json` max|Δ|=0。
- 边界：正式实验全 PLANNED（正文禁写"三次重复±标准差"）；E1 三次 REPORTED_ONLY；E4-B/C UNKNOWN；正文属下一阶段。
批次：`agent_runs/dsh_2026-11-04_thesis/`（README 18.6 KB + 28 份日志 + data_snapshot 哈希互证）。

## 2026-11-04 22:40 · DSH-Agent · Play 漫游交互层（A1，Unity 桌面操作）

teammate `ui-interaction-a` 完成 Play 模式桌面交互层，全部由 builder 保存场景（未手改 YAML），数据链零改动。
- 新增：`TwinPlayCameraRig.cs`(458 行，右键+WASD/QE、滚轮调速、俯仰限位、平滑)、`TwinPlayRayVisual.cs`(627 行，屏幕射线+命中点+HUD+四色可调+UI 命中关闭世界射线)、`TwinUIGradientHover.cs`(174 行，按钮 hover 顶点渐变)、`TwinUIPlayBuilder.cs`(1081 行，菜单 `UI/6`、`UI/17`)。
- 改造：`TwinUiSettings.cs`(+enableHoverGradient/hoverGradientStrength)、`TwinUIButton.cs`(hover 渐变 + 按下 0.985 缩放)。
- `UI/6`：`transforms=2 mask=19 gradientButtons=38/38 saved=yes PASS`；`UI/17` 独立 2 次 13 项全 PASS：W 位移 2.260 m、右键拖拽 yaw +36.81°、俯仰 clamp -85.00°、滚轮 2.00→3.00 m/s、三点射线终点 maxPairDelta 55.653 m、命中/天空/按下/UI 四态线色 Δ=0、hover 顶点色差 0.2251、按下 Δ=0.1652/scale 0.9850、截图 5/5、totalErrors=0。
- 回归：`UI/12` PASS（failures=0，最低对比度 5.02）、`UI/16` PASS（checkedChars=3515, missingDistinct=0）。
- 场景：`AE063339…` → `52063677…`（6,713,561 B），备份 `_backup/scenes/2026-11-04_play_interaction`。
- 边界：输入为组件 API 注入（非物理外设）→ 真实手感 UNKNOWN；截图仅 PIL 统计未目视；重跑 UI/1/2/3/4 重建面板后需重跑 UI/6（渐变是场景级挂载）。
批次：`agent_runs/dsh_2026-11-04_ui_play/`。

## 2026-11-04 22:00 · DSH-Agent · R3 工业数据台（Unity UI 升级，C 线）

teammate `ui-industrial` 完成 R3：深色工业驾驶舱 + 论文级数据采集面板 + AI 可读数据契约，全部由 builder 生成（未手改场景）。
- 构建：`UI/1` `transforms=932 PASS`、`UI/10` `932 PASS`、`UI/11` `932 PASS`、`UI/2` `transforms=535 settings=ok uiHz=10 theme=Dark PASS`。
- 审计 `UI/12`：`failures=0`；按钮 38 个 minHeight=40；色令牌 52（deep20/series6 + light20/series6）；对比度 deep 6.63–16.18、light 5.02–15.80；`digitAdvanceSpread=0`；4 图表含目标虚线 1、阈值带 1；`thresholdTexts=5 stateWords=16 unsubscribedTexts=57`。
- 字形 `UI/16`：`missingDistinct=0`(1819)、`runtimeMissingDistinct=0`(1526)。
- Replay `UI/14`：读 `20261103_135616_529090_E4_B\samples.csv`(10778 行/29.8 s) → `sampleHz=100.1 applyHz=28.0 recv→apply=3.99 ms freshness=5.2 ms jitterP99=13.04 ms caps=1`，13 张 1600×900 截图。
- Play `UI/15`：`exceptions=0`；UI Hz 30→实测 28.73(0.96)、60→50.30(0.84)；事件 109 行（5 条 uiHz）、快照写 24 次；退出后 uiHz 复 10。
- 场景：`289B6888D886F6B0…` → `AE063339DA36B4A1…`（6,670,660 B），旧版备份 `_backup/scenes/2026-11-04_r3_industrial`。
- 边界：回放数据无 torque/force/TCP/clock 字段 → 显示「未订阅」，不冒充真机；阈值「待校准」；60 Hz 档实测约 50 Hz；未在真实大屏目视。
批次：`agent_runs/dsh_2026-11-04_ui_industrial/`。

## 2026-11-04 21:05 · DSH-Agent · E1–E5 短窗采集（B 线，ROS 侧）

真机已启动（`/dsr01/rt_topic/state` 实测 100.0 Hz），按用户"每项 ≤30 s、注意安全"要求新建 6 份短窗 profile 并完成 5 个会话：
`E1s`、`E4-A`、`E2s`、`E3s`、`E5-0`（窗口 25/25/25/25/28 s），ROS 侧 RT 原始行 2536–2839、到达 99.8–100.0 Hz、queue_dropped=0。
动作仅单轴 ±5°、vel=acc=10、`move_joint` 前后各 1 次且均 success=True，关节回到 HOME（Δ<0.001°）。
**E4-B / E4-C 未取得**：Unity `samples.csv` 0 行（唯一判据），`UnityEndpoint10002` 连续出现 `Broken pipe / No more data available`，Unity 侧 `Reader 102 exception`；已用 7 个会话复现，判定 Unity ROS-TCP 连接被中止，需单独修复。本批全部 `formal_experiment_validated=false`，不得进结果表。
批次：`agent_runs/dsh_2026-11-04_e1e5/`（README/MANIFEST/profiles/_tools）。

## 2026-11-04 20:15 · DSH-Agent · UI 网页原型交付（A 线）

teammate `ui-web` 完成纯静态原型：`agent_runs/dsh_2026-11-04_ui_web/`，本地 `http://127.0.0.1:8777/` 实测 200。
5 屏（任务台/概览/曲线/控制/记录）× 3 变体（A 现行/B 高密度/C 任务导向）× 蓝白/深色，密度、字号、状态（已连接/告警/未连接）开关齐备；
32 张 1600×900 截图（`shots/`，PIL 抽检：浅色底 255/255/255、深色底 22/29/43，MD5 互异），`DECISIONS.md` 15 条结论（D01–D15）+ 刻意不做清单。
自查 44 项中 42 通过，2 项已知：favicon 404、C 变体"当前步骤"初始值文案不一致（未修，记为待办）。
所有数值为模拟数据，**未连 ROS2、未驱动真机、未回写 Unity**。

## 2026-11-04 19:45–20:10 · DSH-Agent · 双线开工：UI 网页原型 + E5-0 空载基线准备

用户要求：①UI 完善改到网页里先看效果，别占本会话；②机器人已上电（空载、TCP 默认法兰），先把数据记好，后续与 E5 负载实验对应；③一切以毕业论文为目标，注意真机安全。

A 线（UI 网页原型）：派常驻 teammate `ui-web` 在 `agent_runs/dsh_2026-11-04_ui_web/` 做纯静态原型（5 屏 × 3 变体 × 2 配色，本地 `http://127.0.0.1:8777/`，Edge headless 截图 + `DECISIONS.md`），Unity 侧本轮零改动。

B 线（E5-0 空载基线）：新建 WSL profile `chapter4_E5_0kg_1104.yaml`（`tool_id=none / payload_kg=0.0 / tcp=默认法兰 / allowed_joints=[joint_1,joint_5] / velocity=acc=10 / max_delta=5 / stage=pilot / mapping_reviewed=false`），`chapter4.py check` 通过（sha 前 16 `FB72FDA59E8C2CEE`）；
`chapter4.py prepare` 试跑通过，得到 `session_20261104_195124_926033_E5_B`（**仅管道验证，0 采样，不当数据**）。
交接卡 `agent_runs/dsh_2026-11-04_e5_empty/HANDOFF_实验会话.md` 含安全闸、三终端命令、Unity 2b→Load→Stream→Stop→Self-Check 顺序、J1/J5 ±5° 目标六元组与节奏。
`_tools/UNITY_LOCK.txt` 约定：E5 采集窗口内 Unity 菜单归实验会话持有，本会话只读。

证据现状：profile + 交接卡已落盘；**尚无真机采样**，任何结果待实验会话回报后按 Self-Check PASS 登记。

## 2026-11-03 23:15 · DSH-Agent · 今日收尾（用户明天再推进）

今日完成：一期 UI 收口（v2026-11-03.6）→ R2 双主面板 + 工业令牌（v2026-11-03.7）→ 蓝白配色 + 评分规则 + 3D 任务卡（v2026-11-03.8）。
当前场景 `289B6888…18588E9F7`（4,648,721 B，1,887 对象），备份 `_backup\scenes\2026-11-03_r2_bluewhite`；
旧 `UI_统一入口面板` 仍为停用观察（A26，观察期至 2026-11-17）。

明日第一件事（按顺序）：
1. 拍板评分三项（`T_ref` 来源 / 提示是否允许 / 是否改"违规次数"）→ 然后接评分代码：提示计数、违规判定、安全维度。
2. 3D 会话产出交回后，本会话执行 `UI/14 Build TwinViz` 统一挂载并保存（持场景锁）。
3. 机器人开机后重跑 `UI/15 Play Check R2`，补"真机数值刷新/告警联动"证据，再决定是否解除 jog 禁用（A25）。

今日未做/未宣称：真机数据下 UI 数值刷新（IMPLEMENTED_UNTESTED）、评分代码（PLANNED）、3D 可视化（PLANNED）、教学效果实验（未设计）。

## 2026-11-03 23:05–23:13 · DSH-Agent · UI 配色改蓝白 + 评分规则 + 3D 任务卡

做了什么：按用户要求把 R2 面板从深色改为 DeepSeek 风格蓝白（白卡 + 蓝主色 + 红/绿/黄语义色），
同步改按钮五态、图表画布与 6 条序列配色；重跑构建/审计/截图，对比度全部 ≥4.5；
新增 `Docs/10_AGENT/TRAINING_SCORING.md`（100 分过程评分规则 + 论文用法 + 3 个待拍板项）；
新增 `_tools/3D会话任务卡_TwinViz.md`（写作用域、场景锁、只读接口、验收口径）。

证据：`Logs/ui_r2_audit.txt`（contrast 15.80/4.97/5.34/4.83，21 按钮 40px）、`Logs/ui_shots_r2/s00..s04` 重出、
场景 v2026-11-03.8（`289B6888…18588E9F7`，备份 `_backup\scenes\2026-11-03_r2_bluewhite`）。

结论状态：配色与布局 VERIFIED；评分规则与 3D 卡片为设计文档（PLANNED，未实现）；评分代码接线待下一轮。

## 2026-11-03 22:33–23:02 · DSH-Agent · UI 二期：双主面板 + 工业视觉系统

目标：按用户决策把 UI 拆成「实训任务台 / 孪生监控台」两个主面板，统一工业 HMI 视觉，并保持数据链只读。

做了什么：新增设计令牌与按钮组件（`TwinUIStyle/TwinUIButton/TwinUITextR2`）、只读状态绑定层（`TwinUIStatusBinder`，5 Hz，含告警分级）、
双面板宿主与三个视图（`TwinUIPageHost/TwinUIHeaderView/TwinUIMissionView/TwinUITwinOpsView`）；新增构建器与四个检查工具
（UI/1 全量、UI/10 任务台、UI/11 监控台、UI/12 审计、UI/14 截图、UI/15 Play 自检、UI/16 字形）。
场景保存为 v2026-11-03.7（1,887 对象），旧 `UI_统一入口面板` 仅停用保留。

证据：`Logs/ui_r2_build.txt`（368 transforms PASS）、`Logs/ui_r2_audit.txt`（21 按钮 minHeight=40，17 个颜色全部令牌）、
`Logs/ui_r2_glyphs.txt`（168 文本/752 字符/0 缺字 PASS）、`Logs/ui_r2_play.txt`（两页 8 子页切换 PASS，exceptions=0）、
`Logs/ui_shots_r2/s00..s04`（5 张互异）、`Logs/ui_r2_snapshot.txt`。

结论状态：编辑态构建/截图/审计 = VERIFIED；Play 模式页面切换与异常数 = VERIFIED（无 ROS2，空态属预期）；
真机数据下的数值刷新 = IMPLEMENTED_UNTESTED；3D 孪生可视化 = PLANNED（独立会话）。

遗留：控制页 jog 仍禁用（待真机授权）；曲线页无数据时为空态；旧 R1 面板保留 2 周后决定删除；Docs 其余文件仅增量追加未重排。

## 2026-11-03 22:12–22:22 · DSH-Agent · 删除遗留 Canvas `User_ui`

**决策**：用户拍板删除 `30_界面与交互/Canvas/User_ui`（只含 1 个 Button，`ScreenSpaceCamera` + planeDistance 100，估值约 18×9 px，非可用页面）。

**做法**：新增菜单 `DigitalTwin/UI/5 Remove Legacy User_ui Canvas (destructive)`（`Editor/TwinUIRemoveLegacyCanvas.cs`）：
先对全场景组件做 SerializedProperty 引用扫描（**排除父子层级链**，这是第一次误报的原因），
`referencesFound=0` 才 `DestroyImmediate` + 保存场景；日志记录子树结构，便于事后复原。

**结果**：`referencesFound=0` → `afterDeleteExists=False` → `result=PASS`。删除后复测审计：
`missingScriptInstances=0`、`buttonComponents=34`、`totalCalls=39`、`brokenCalls=0`；
场景 **1,521 → 1,519 对象**，3,588,920 → 3,581,127 B，
SHA256 `7F5638BD…` → **`9C604D15E8CF5A3F6F47582007DAC71D7DD38F70A11A0AD6405FC5C819A50D50`**（v2026-11-03.6）。`VERIFIED`

**证据**：`Logs/ui_remove_userui.txt`、`Logs/ui_wiring_audit.txt`（22:19 复测）；备份 `_backup\scenes\2026-11-03_ui_userui_removed\`
与删前态 `_backup\scenes\2026-11-03_pre_userui_delete\`。
- **工具链坑（P2 发现）**：`UI/12` 审计后不还原 Light 主题且留 dirty；后续 attach 菜单 `isDirty→SaveScene` 会把 153 个颜色块写成 Light（磁盘曾 `42783A78…`）。已用基线备份 + Refresh + 重跑 `UI/29` 复原 `C4E3F232…`；已登记 A49（高优先）修 `UI/12` 并给 attach 路径加主题一致性断言。跑 `UI/12` 后必须校验场景哈希。
- P2 `[item][d]` 期望方向写反：窗口变大时 `scale=min(w/1920,h/1080)` 应变大（0.680→0.743）；实测按公式并已标注，缩小方向由 `[e]` 的 0.599 覆盖。

---

## 2026-11-03 22:05–22:20 · DSH-Agent · 补充：Outline 守卫修复 → 四页在 Play 全部走通

**背景**：上一节记的"训练/拆解页在 Play 下走不完"，根因是 `QuickOutline/Outline.cs → LoadSmoothNormals()`：
电池包模型导入设置关掉了 Read/Write，`mesh.vertices / normals / SetUVs(3,…)` 抛
`Not allowed to access vertices/normals/uv4`，异常沿 `SetActive(true)` 冒到调用方，激活中断。之前只加了 try/catch 兜底，页面没走通。

**改了什么**（1 个文件：`Assets/DoosanDT/20_BatteryDisassembly/Others/QuickOutline/Scripts/Outline.cs`）：
`LoadSmoothNormals()` 的两个循环（`MeshFilter` / `SkinnedMeshRenderer`）先判 `sharedMesh == null || !sharedMesh.isReadable`，
命中则计数跳过；末尾有跳过时打一条 `[Outline] skipped N mesh(es) with Read/Write disabled …` 警告。
**没有**改模型导入设置（避免顶点数据常驻内存翻倍），也没改 Outline 渲染逻辑。场景文件未因此改变。

**结果**：Play 冒烟扩成两阶段 —— `BASE RESULT: PASS`（UI-only）+ `EXTENDED RESULT: PASS`（四页全走），
11 张 1920×1080：`play_p01–p07`（欢迎/进入/漫游/数据/控制卡）+ `play_p08/p09`（训练）+ `play_p10/p11`（拆解）。
退出后 `sceneUnchanged=True`（`7F5638BD…`）、`D:\Unity Projects\数据记录` 仍不存在、`PerformanceMonitor` 恢复 active=True。`VERIFIED`

**页签归属的像素证据**：tab 中心色 p08 tab1 `(35,128,204)` 高亮；p10 时 tab1 回落 `(34,42,56)`、tab2 变 `(130,161,205)`
（被旧 Overlay 画布混色）；p05/p07 tab3 高亮。四页选中态与点击顺序一致。

**新发现（未修，`REPORTED_ONLY`）**：训练页旧视频控制面板 `Canvas_1_V` 挡住右侧卡片（控制卡区域落墨 0.60，正常 0.11）；
拆解页有旧 Overlay 画布叠在入口面板之上。属旧 Canvas 布局，改动需动旧资产，留待决策。

**证据**：`Logs/ui_play_smoke.txt`（`EXTENDED RESULT`）、`ui_play_smoke_editor.txt`、`Logs/ui_shots/play_p08–p11*.png`；
批次 `dsh_2026-11-03_ui`（UI-11/UI-12）。

---

## 2026-11-03 20:50–22:05 · DSH-Agent · 界面收口（统一入口面板 + 图表 + 双向控制呈现层）

**目标**：修 2 处丢失脚本；把 5 个散 Canvas 收成"漫游/训练/拆解/数据"四页统一入口；补力-力矩曲线与双向控制呈现层；
重挂 EVB 拆解与 UI 的事件引用。**不改**机器人结构、数据链、Profile、旧 Canvas 渲染模式与缩放。

**做了什么**

1. `UI/2 Fix Missing Scripts`：清掉 `30_界面与交互/Canvas/登录界面` 与 `50_玩家与相机/.../Gaze Interactor`
   各 1 个丢失脚本，补 `BatteryLoginGate`，绑 `LoginButton→Enter()`、`CancelButton→Quit()`，
   隐藏账号/口令输入，6 个 CJK TMP 换成 `NotoSansSC-UI SDF`。`missingScriptInstances=0`。`VERIFIED`
2. `UI/8 Font Check`：UI 文案 41 条 × TMP 静态字形集（4096²、1368 字形、`Static`）→ 0 缺失。`VERIFIED`
   （注：`欢`/`迎` 不在字形集内，入口页文案因此改为"入口页面：不收集账号与口令"）
3. `UI/1 Build Unified Shell`：新建 ScreenSpaceCamera 入口面板（1920×1080 / match 0.5 / order 100），
   含顶栏、4 页签、提示条、图表卡（两张 `TwinTelemetryChart`）、控制卡（5 按钮）、两个 inactive 回放探针；
   路由目标解析到 `20_漫游与训练/FunctionModule/Roaming_M`、`.../Dis-Training_M`、
   `00_数字孪生机械臂/10_电池包拆解系统/VR拆解` + 4 个旧 Canvas。幂等重建并保存场景。`VERIFIED`
4. `UI/3 Audit`：12 Canvas / 35 Button / 40 个 UnityEvent 调用 / **0 断链** / 0 丢失脚本。`VERIFIED`
5. `UI/7 Chart Replay Preview`：真实录制会话（869 行、关节角变化）灌关节图，合成夹具（600 行、力/力矩）灌力/力矩图，
   空态对比。图表区落墨率 0.165（空）→ 0.204（力）→ 0.258（力矩），三态像素可区分。`VERIFIED`
6. `UI/4 Apply Force Channels`：`Ros2ExperimentPanel` 的 `externalTcpForce/externalJointTorque/actualJointTorque`
   由 False→True 并保存场景（`Ros2Bridge` 才会订阅对应 topic）。`VERIFIED`（字段）；topic 是否发布见下方"必须说清"。
7. `UI/6 Play Smoke`：进 Play 走 欢迎→进入→漫游(回放)→数据(回放)→控制卡干运行，`ScreenCapture` 7 张 1920×1080；
   退出后 `sceneUnchanged=True`、`D:\Unity Projects\数据记录` 仍不存在、Profile 三个 autoStart 全 False、
   `PerformanceMonitor` 恢复 active=True。`VERIFIED`

**必须说清的三点**

- **训练/拆解两页在 Play 下没有走完**：激活 `Dis-Training_M` / `VR拆解` 会触发电池包模型上大量
  `XRSimpleInteractable` 的 "collider already registered" 警告，以及 `QuickOutline/Outline.Awake` 在**不可读 Mesh**
  上抛 `Not allowed to access vertices/normals/uv4`，冒烟两次都在这一步中断（路由已加 try/catch 保证页签不死）。
  这两页目前只有**编辑期渲染**证据（`i03/i04`），Play 完整走查 `IMPLEMENTED_UNTESTED`。缺陷本身属既有工程问题。`REPORTED_ONLY`
- **力/力矩通路只做对了一半**：`RobotStateFrame.ForceVector` 来自 `external_tcp_force`（E1 bag 里确有 100 Hz），
  但 `JointTorqueNm` **只**来自 `external_joint_torque`，该 topic 是否由控制器发布 **UNKNOWN**。
  `actual_joint_torque` 只进缓存、不进 frame。所以"力矩曲线"在真机上可能长期显示"未订阅"——那是数据源问题，不是 UI bug。
- **`User_ui` 没有纳入路由**：它只含 1 个 Button，缩放 0.0054 + planeDistance 100 ⇒ 实际约 18×9 px，
  不是可用页面。本轮只报告，等产品决策。

**研究影响**：E4 的"显示开销"自变量由 `force_channels=off` 变成 `on`，历史 E4_B 会话与新会话**不可直接对比**，
实验台账必须记这一条。

**证据**：`Logs/ui_fix_missing.txt`、`ui_font_check.txt`、`ui_wiring_audit.txt`、`ui_build_shell.txt`、
`ui_force_channels.txt`、`ui_chart_preview.txt`、`ui_snapshots.txt`、`ui_play_smoke_editor.txt`、`ui_play_smoke.txt`；
截图 `Logs/ui_shots/i00–i09*.png`（编辑期）与 `play_p01–p07*.png`（Play）；
副本 `E_TJ\agent_runs\dsh_2026-11-03_ui\_evidence\`。

**场景**：`v2026-11-03.5`，SHA256 `7F5638BD3A0A23826F16FBC8C5E8261500A21C2BF9E6255C067B5F28F053A572`，
备份 `_backup\scenes\2026-11-03_ui_shell\`。

**遗留**：修 `QuickOutline` 不可读 Mesh；实测 `external_joint_torque`；`User_ui` 归属；`Canvas_I P`/`Canvas_I软著`
的 Panel 尺寸异常（−6×227、0×0）；旧 Canvas 同时挂 `GraphicRaycaster` 与 `TrackedDeviceGraphicRaycaster`（未处理）。

---

## 2026-11-03 20:07–20:25 · DSH-Agent · 视觉与性能第一轮（工业级观感 + 减负）

**目标**：在不加新几何、不跑实验的前提下，把 `Doosan-DT.unity` 收拾到"可拍图 + 不掉帧"的基线：清冗余对象、收 Outline、补天空/主光/后处理、出同机位对比图、留下可追溯数字。

**前置事实（重要）**：本轮开始时场景已被用户手工调整过 —— 归档根名为 `~`（3,963 对象）、新增 `00_数字孪生机械臂-bkup113`（619）与 `10_电池包拆解系统-bkup113`（100，停用）两个备份组、电池包已放进 `00_数字孪生机械臂`（619 = 519 + 100），机器人整体移到了试验台位置。本轮所有判断以这份现场结构为准，**没有**按旧记录的组名假设行事。`REPORTED_ONLY`（来自 `Logs/doosan_dt_verify.txt` 20:09:21）。

**做了什么**

1. `7 Scene Stats` 记录基线（20:07:55）：6,154 对象 / 3,070 渲染器（活 282）/ 三角面 11,823,679（活 984,136）/ 场景 12.17 MB / Outline 2,153 个组件（**仅 38 个启用**）/ 贴图 127 张 约 221.9 MB。`VERIFIED`
2. `6 Mark Environment Static`：`40_环境与灯光` 下 227 个渲染器打 GI/Batching/Occlusion/Reflection 静态标记。`VERIFIED`（日志 `STATIC marked renderers=227`）
3. `4 Optimize Outline`：两版规则后定稿为"只看对象自身是否带 `DisassemblyPart / LayerGroupConfig / ExplodablePartXR`"，不向祖先扩散。中间版本误把"祖先是层组容器"当作交互件，一度重开 2,070 个 Outline，已回退。`VERIFIED`（最终 `total=2153 kept=92 disabled=1978`；拆备份后场内存量 `outlineComponents=162 enabled=5`）
4. `5 Extract Archive Group`：把 `~`(3,962) + `10_电池包拆解系统-bkup113`(100) + `00_数字孪生机械臂-bkup113`(619) 拆到 `Assets/Scenes/Doosan-DT_Archive.unity`（9.91 MB）。拆分前做引用扫描：活着的 `DigitalTwinRuntime` 有 2 处引用指向备份树里的 `UI_System`，因此把 `UI_System` 留在主场景的停用中转根 `~_外部引用保留`（2 对象），其余搬走。`VERIFIED`（`EXTRACT keepReferenced refs=2`、`EXTRACT roots=[~=3962]`）
5. `8 Apply Lighting Setup`：程序化工业天空（`Art/Materials/DoosanDT_IndustrialSky.mat`）、主平行光（(50,-30,0)/1.2/暖白/软阴影/Mixed）、另一盏平行光降为 0.25 冷色 Baked 补光、2 盏顶部点补光、ACES+Bloom+对比/饱和+暗角后处理（`Art/Config/DoosanDT_VisualProfile.asset`），并把 URP 资产 `m_ColorGradingMode` 由 LDR(0) 改为 HDR(1)。`VERIFIED`（日志 `LIGHTING applied ... colorGrading=HDR`）
6. `9 Snapshot Views`：同参数 1920×1080 三机位 `robot / workcell / wide`，装灯前存 `_before`、装灯后存 `_after`。`VERIFIED`（6 张 PNG，见 `30_VISUAL/BASELINE.md`）

**前后数字（主场景）**

| 指标 | 前（20:07） | 后（20:19） | 变化 |
|---|---|---|---|
| Transform 对象 | 6,154 | 1,476 | −76% |
| 根节点 | 9 | 7 | −2 |
| MeshRenderer 总数（活） | 3,070（282） | 749（282） | −76% |
| 三角面 总 / 活 | 11,823,679 / 984,136 | 2,884,871 / 984,136 | 总 −76%，活 **不变** |
| Outline 组件（启用） | 2,153（38） | 162（5） | −92% / −87% |
| 场景文件 | 12.17 MB（加静态标记后 13.17） | **3.26 MB** | −75% |
| 灯光 / Volume | 4 / 1 | 6（活 4）/ 1 | 补光 +2 |

**结论与必须说清的两点**（防止论文/汇报写错）

- **活的三角面数没有下降**（984,136 → 984,136）。本轮收益是对象数、场景体积、加载/内存、组件更新开销与备份隔离，**不是** GPU 像素/三角形负载。要降 GPU 负载必须走 LOD/减面/烘焙，属推迟项（见 `TASKS.md`）。
- Outline 的"原先 2,076 个开着"是错误前提：真实基线只有 38 个启用。已在本文修正，后续引用以 38 → 5 为准。

**证据**：`Logs/doosan_dt_polish.txt`、`Logs/doosan_dt_verify.txt`、`Logs/doosan_dt_view_{robot,workcell,wide}_{before,after}.png`；副本在 `E:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-03_fusion\_evidence\round3_visual\`。

**校验**：Console 0 error（`VERIFIED`）；`2 Verify Merged Scene` 通过，缺失脚本 3 → 2（`登录界面`、`Gaze Interactor` 两个为历史遗留，另一个随 `~` 归档进备份场景）；`DisassemblySystem 1/1`、`Ros2Bridge 1/1`、`TwinRecorder 2/2` 全在（`VERIFIED`，读 `Logs/doosan_dt_verify.txt`）。

**遗留**

- 迁移过程中曾把整个 `~` 误挂进中转根（一次失败尝试），已修正为只留 `UI_System`；当前 `~_外部引用保留` 仅 2 对象，属预期。
- 场景内仍有重复：Camera 3（活 1）、EventSystem 2（活 1）、GraphicRaycaster 13（活 2）、平行光 2 盏都启用（一盏已降亮度改 Baked，等烘焙后再决定是否关掉）。`REPORTED_ONLY`，未处理。
- 备份场景 9.91 MB 未进 git（`Assets/Scenes/` 被忽略），需按 `VERSION_LOG.md` 另行备份。
- 场景未进 Play 验证过一次真实帧表现；VR 头显内观感需用户在头显/Game 视图确认。

---
