# UI 架构（统一入口面板 · 图表 · 双向控制呈现层）

> 适用场景：`Assets/Scenes/Doosan-DT.unity`；代码在 `Assets/DoosanDT/10_DigitalTwin/Scripts/UI/`（运行期）与
> `Assets/DoosanDT/10_DigitalTwin/Editor/`（构建/审计工具）。
> 本文只写**已核对**的事实；每条结论后标 `VERIFIED / IMPLEMENTED_UNTESTED / PLANNED / UNKNOWN`。

## 1. 为什么要有这层

接手时现场是 5 个散落的 Canvas（`Canvas_1 P`、`Canvas_1_V`、`Canvas_I`、`Canvas_I软著用标题`、`User_ui`），
互相没有入口关系，且各自缩放/渲染模式不同；`登录界面` 与 `Gaze Interactor` 各挂 1 个丢失脚本。
同时 `TwinRuntimeProfile.asset` 的 `enableRuntimeUI: 0` 会让运行期把旧 HUD（`UI_System` 上的 `TwinUIController`）
**禁用**，所以旧面板在 Play 里根本不会出现。

因此新增一个**场景内常驻的入口面板**（不是运行时 HUD），由它统一承载"漫游 / 训练 / 拆解 / 数据"四页、
力-力矩曲线、双向控制呈现；旧 Canvas 不改渲染模式、不改缩放、不重挂父级，只由路由控制 `activeSelf`。
`enableRuntimeUI` **保持 0 不动**，避免影响 E4 实验条件。`VERIFIED`（见 §7 证据）

## 2. 现场清点（12 个 Canvas）

| Canvas | 渲染模式 | 排序 | 缩放 | 子对象 | 本轮处理 |
|---|---|---|---|---|---|
| `30_界面与交互/Canvas/Canvas_1 P` | WorldSpace | 0 | 0.003 | Panel_1/Panel_2（各含关闭按钮） | 由"漫游"页路由（`roamingPanels`）|
| `30_界面与交互/Canvas/Canvas_1_V` | WorldSpace | 0 | 0.82 | Video_play1（视频播放控制） | 由"训练"页路由（`trainingPanels`）|
| `30_界面与交互/Canvas/Canvas_I` | ScreenSpaceOverlay | 1 | 1 | Panel + Text(TMP) | 由"漫游"页路由 |
| `30_界面与交互/Canvas/Canvas_I软著用标题` | ScreenSpaceOverlay | 1 | 1 | Panel + LoginPanel（欢迎/入口）| 由 `BatteryLoginGate` 控制 |
| `30_界面与交互/Canvas/User_ui` | ScreenSpaceCamera | 2 | — | Button（`TC`）+ `CanvasScaler/GraphicRaycaster/TrackedDeviceGraphicRaycaster` | **已删除**（2026-11-03 22:19，菜单 `UI/5`；删前引用扫描 0 外部引用）|
| `30_界面与交互/UI_统一入口面板` | ScreenSpaceCamera | 100 | 由 Canvas 驱动 | 顶栏/标签栏/提示条/图表卡/控制卡 | **本轮新增** |
| 其余 6 个 | 6 个分布在 `10_电池包拆解系统/VR拆解` 等 | — | — | 拆解相关 | 未改动 |

计数口径：删除 `User_ui` **前** 12 Canvas / 35 Button / 40 调用；**删除后** 11 Canvas / 34 Button / 39 调用 / `brokenCalls=0`
（`Logs/ui_wiring_audit.txt`，2026-11-03 22:19 复测）。引用旧数字时请注明"删前/删后"。

`VERIFIED`：`Logs/ui_wiring_audit.txt`（2026-11-03 21:36、22:19 两次）。其余 4 个旧 Canvas 仍 `both-raycasters-warning`
（`GraphicRaycaster` + `TrackedDeviceGraphicRaycaster` 同时挂），仍保持原状。

## 3. 统一入口面板结构

```
30_界面与交互/UI_统一入口面板            [Canvas, CanvasScaler, GraphicRaycaster,
                                          TwinUIShellController, TwinDemoModeRouter]
├─ TopBar          TitleText + StatusRow(StatusText / ModeText / SourceText / RecordText)
├─ TabBar          4 × Button(132×40) : 漫游 / 训练 / 拆解 / 数据
├─ HintBar         当前页提示（TwinUIShellText.ModeHints）
├─ ChartCard       Header + JointLabel + JointChart(446×236) + JointStatus + JointRange
│                  + WrenchLabel + WrenchChart(446×236) + WrenchStatus + WrenchRange
├─ CommandCard     Header + CommandStatus + CommandDetail
│                  + 5 × Button : 预览 / 校验 / 确认 / 执行 / 急停
├─ SelfTestReplay_Real        (inactive) TwinTelemetryReplayProbe → Logs/ui_selftest_motion.csv
└─ SelfTestReplay_Synthetic   (inactive) TwinTelemetryReplayProbe → Logs/ui_selftest_force.csv
```

`CanvasScaler`：1920×1080 / match 0.5；世界相机 = `50_玩家与相机/player/Main Camera`；`planeDistance = 1`；
`sortingOrder = 100`（压在旧 Canvas 之上）。`VERIFIED`（`Logs/ui_build_shell.txt` + 场景 YAML）

## 4. 运行期组件职责（`Scripts/UI/`）

| 组件 | 职责 | 关键约束 |
|---|---|---|
| `BatteryLoginGate` | 欢迎/入口页：`Enter()` 隐藏面板 → 激活入口面板 → 显示默认页；`Quit()` 退出 | **不是登录**：不收集账号口令，`WelcomeHint` 明示"入口页面：不收集账号与口令"；`hideInputsOnEnable` 默认隐藏账号/口令输入框 |
| `TwinDemoModeRouter` | 4 个模式 `Roaming/Training/Disassembly/Data`，`EnterMode()` 精确激活一组根/面板 | 不写回旧 Canvas 的 Toggle；`applyOnStart=false`；目标为空时跳过；`SetActive` 有 try/catch（见 §6） |
| `TwinUIShellController` | 标题/页签高亮/提示/状态轮询（2 Hz）；`SelectTab(i)` → 路由 + 立即渲染页签 | 页签按钮已持久化 `AddIntPersistentListener(onClick, SelectTab, i)`，运行期不再重复绑定 |
| `TwinTelemetryChart` | `MaskableGraphic` 手写折线图：窗口 20 s、900 点环、自动量程、网格、逐序列颜色 | 无第三方图表库；`OnPopulateMesh` 直接生成四边形 |
| `TwinTelemetryRingBuffer` | 定容环形缓冲，单调时间守卫、按时间裁剪 | — |
| `TwinTelemetryChartBinder` | 20 Hz 采样 `DigitalTwinRuntime.TryGetLatestFrame()` 或回放探针 → 推入图表 | 力/力矩缺失时显示"未订阅"，**绝不画零**；`staleSeconds=1.5` → "无数据源" |
| `TwinTelemetryReplayProbe` | 只读 CSV 回放（动态列名映射），默认 inactive | 只读文件，不写盘、不发流 |
| `TwinCommandPanelView` | 双向控制**呈现层**：显示 模式/干运行/急停/最近结果/规划状态 | 见 §5 |
| `TwinUIShellText` | 全部 UI 文案唯一来源 | 受 TMP 静态字形集约束，见 §6 |

`VERIFIED`：以上脚本编译 0 error；`UI/3 Audit` 报告 `buttonComponents=35 / totalCalls=40 / brokenCalls=0 /
missingScriptInstances=0`。

## 5. 双向控制呈现层的边界（重要）

按钮语义（**都不新增下发路径**）：

| 按钮 | 实际动作 | 说明 |
|---|---|---|
| 预览 | `TwinCommandController.EnterPlan()` | 只进规划态，不下发 |
| 校验 | 只读：显示 `CanSendRealMove / EnableBidirectionalControl / EnableRealRobotCommand / EnableDryRun` | 九项校验由安全链在"执行"时自动做 |
| 确认 | 本地武装开关（二次点击） | 防误触 |
| 执行 | `controller.SendMoveJoint(当前帧关节角, speed)` | 目标=当前位姿 ⇒ **零位移**；`SendMoveJoint` 内部先过 `SafetyCheckService.ValidateMoveJoint`，Profile `enableDryRun=1 / enableRealRobotCommand=0` ⇒ 只打 `DRY_RUN MOVE_JOINT` 日志 |
| 急停 | `controller.SetEmergencyStopped(true/false)` | 只置本地停止标记，不发送 |

护栏：若 `EnableRealRobotCommand && CanSendRealMove`（将来真机开启），本面板**主动拒绝执行**并提示
"呈现层不下发真机指令"。`IMPLEMENTED_UNTESTED` → 本轮 Play 冒烟已点到"执行"并显示 `DRY_RUN`（见 §7 的
`play_p06/p07`），但没有真机通路可测，故真机分支保持 `PLANNED`。

## 6. 已知缺陷与不做的事

1. ~~**`User_ui` 不纳入路由**~~ **已废弃（2026-11-03 22:19 删除）**：它只含 1 个 Button(`TC`)、`ScreenSpaceCamera` + `planeDistance 100`，
   按 0.0054 估算实际约 18×9 px，不是可用页面。用户决定删除。执行 `UI/5 Remove Legacy User_ui Canvas`：
   删前做 SerializedProperty 引用扫描（排除父子层级链）→ `referencesFound=0`；删除后 `missingScriptInstances=0`、
   `brokenCalls=0`；场景 1,521 → **1,519 对象**，SHA256 `9C604D15…`。`VERIFIED`（`Logs/ui_remove_userui.txt`）
2. **训练/拆解页曾无法在 Play 下激活（已修，2026-11-03 22:15）**：`QuickOutline/Outline.Awake →
   LoadSmoothNormals()` 对电池包模型（导入设置 Read/Write 关闭）调 `vertices/normals/SetUVs`，抛
   `Not allowed to access vertices/normals/uv4`，异常沿 `SetActive` 冒出并中断激活。
   修法：在 `LoadSmoothNormals()` 里对 `!mesh.isReadable` 的网格**跳过**（计数并打一条 warning），不再触碰顶点数据；
   **没有**改任何模型的导入设置（避免顶点数据常驻内存翻倍）。修复后四页在 Play 下全部走通（`play_p08–p11`）。`VERIFIED`
   - 仍存在**非致命**警告：电池包大量 `XRSimpleInteractable` 报 "collider already registered"（重复碰撞体），不影响激活。`REPORTED_ONLY`
   - 新观察：**训练页的旧视频控制面板（`Canvas_1_V`）挡住右侧图表/控制卡**（`play_p08/p09` 控制卡区域落墨 0.60，正常 0.11）；
     拆解页有旧 Overlay 画布叠在页签上（tab2 像素 (130,161,205) 而非纯高亮 (35,128,204)）——属旧 Canvas 布局问题，未修。`REPORTED_ONLY`
3. **不重挂旧 Canvas**：不改 renderMode、不改 scale、不改父级、不删旧 Toggle。只切 `activeSelf`。
   理由：`Canvas_1 P` 与 `VR拆解` 的世界坐标/缩放是手工摆好的；`Canvas_I P` 的 Panel 尺寸异常（size −6×227），重排会连带改观感。
4. **TMP 静态字形集**：`NotoSansSC-UI SDF.asset` 为 `Static`、4096²、1368 字形，运行期不能加字。
   因此"欢迎"二字不可用（`欢`/`迎` 不在集内），入口页文案改为"入口页面：不收集账号与口令"。
   菜单 `UI/8 Check UI Font Coverage` 当前 **PASS**（`checked=41 stringsWithMissingGlyphs=0`）。`VERIFIED`
5. **不改 Profile**：`enableRuntimeUI:0`、`autoStartDartTransport:0`、`autoStartRos2Transport:0`、
   `autoStartRecordingWriter:0` 全部保持；新增面板不读这个开关，所以不受其影响。`VERIFIED`
6. **不碰数据链**：`TwinRecorder / Ros2Bridge / DisassemblySystem` 行为与字段未改；`Ros2ExperimentPanel`
   仅改了 3 个 bool 字段（§8）。

## 7. 证据索引（全部可复算）

| 证据 | 路径 | 结论 |
|---|---|---|
| 缺失脚本修复 | `Logs/ui_fix_missing.txt` | 17 处改动，`missingScriptInstances=0`，RESULT PASS `VERIFIED` |
| 字体覆盖 | `Logs/ui_font_check.txt` | `checked=41 missing=0` PASS `VERIFIED` |
| 接线审计 | `Logs/ui_wiring_audit.txt` | 12 Canvas / 35 Button / 40 调用 / 0 断链 `VERIFIED` |
| 面板构建 | `Logs/ui_build_shell.txt` | 幂等重建 + 保存场景，RESULT PASS `VERIFIED` |
| 编辑期状态截图 | `Logs/ui_shots/i00..i05*.png` + `Logs/ui_snapshots.txt` | 四页各 1 张，页签高亮随 `SelectTab` 移动 `VERIFIED` |
| 图表绘制证明 | `Logs/ui_shots/i06..i09*.png` + `Logs/ui_chart_preview.txt` | 真实回放/合成夹具/空态三态，`chart_card` 落墨 0.165→0.204→0.258 `VERIFIED` |
| Play 冒烟（四页） | `Logs/ui_play_smoke_editor.txt` + `Logs/ui_play_smoke.txt` + `Logs/ui_shots/play_p01..p11*.png` | `BASE RESULT: PASS` + `EXTENDED RESULT: PASS`；`sceneUnchanged=True`、`csvDirectory=<absent>`、Profile 三个 autoStart=False、`PerformanceMonitor restored` `VERIFIED` |
| 力/力矩通道 | `Logs/ui_force_channels.txt` | 3 个 channel bool 由 False→True 并保存场景 `VERIFIED`（topic 是否真发布见 §8）|
| 旧 Canvas 清理 | `Logs/ui_remove_userui.txt` | 删 `User_ui`：`referencesFound=0`、`afterDeleteExists=False`、`result=PASS` `VERIFIED` |

复算脚本（本机 Python + Pillow）：`E_TJ\agent_runs\dsh_2026-11-03_ui\_tools\`
`inspect_ui_shots.py` / `inspect_tabs.py` / `inspect_chart.py` / `inspect_play_shots.py` / `make_selftest_csv.py`。

## 8. 力/力矩通路的现状（写论文前必读）

- `Ros2Bridge.ApplyRtCache` 只从 `external_tcp_force` 填 `ForceVector`，只从 `external_joint_torque` 填 `JointTorqueNm`
  （`Ros2Bridge.cs:1573-1600`）。`actual_joint_torque` 只进缓存，**不进** `RobotStateFrame`。`VERIFIED`（源码）
- 本轮把 `Ros2ExperimentPanel` 的 `externalTcpForce / externalJointTorque / actualJointTorque` 三个 bool 置 true
  （`UI/4 Apply Force Channels`），于是 `Ros2Bridge` 会订阅对应 topic。`VERIFIED`（场景字段+日志）
- **但**：`external_tcp_force` 在 E1 bag 中可见（100 Hz）；`external_joint_torque` 是否由 DSR 控制器发布
  **未验证（UNKNOWN）**。因此"力矩曲线"在真机上可能长期显示"未订阅"——这不是 UI bug，是数据源缺失。
- 研究影响：E4 的"显示开销"自变量由 `force_channels=off` 变为 `on`，历史 E4_B 会话与新会话不可直接对比，
  实验台账里必须写明。

## 9. 菜单（新增，全部幂等）

| 菜单 | 用途 | 产物 |
|---|---|---|
| `DigitalTwin/UI/1 Build Unified Shell` | 重建入口面板（会删除并重建 `UI_统一入口面板`），保存场景 | `Logs/ui_build_shell.txt` |
| `DigitalTwin/UI/2 Fix Missing Scripts` | 清 `登录界面`/`Gaze Interactor` 丢失脚本、补 `BatteryLoginGate`、改文案/字体 | `Logs/ui_fix_missing.txt` |
| `DigitalTwin/UI/3 Audit UI Wiring (read-only)` | Canvas/EventSystem/UnityEvent/空引用/字体/运行开关审计 | `Logs/ui_wiring_audit.txt` |
| `DigitalTwin/UI/4 Apply Force Channels` | 置 3 个 channel bool 并保存场景 | `Logs/ui_force_channels.txt` |
| `DigitalTwin/UI/5 Remove Legacy User_ui Canvas (destructive)` | 删 `User_ui`：先引用扫描，0 引用才删，然后保存场景 | `Logs/ui_remove_userui.txt` |
| `DigitalTwin/UI/6 Play Smoke (UI only)` | 进 Play 走欢迎→漫游→数据→控制卡，`ScreenCapture` 出图后自动退出 | `Logs/ui_play_smoke*.txt` + `Logs/ui_shots/play_p*.png` |
| `DigitalTwin/UI/6b Abort Play` | 冒烟中断时强制退出 Play | — |
| `DigitalTwin/UI/6c Restore PerformanceMonitor` | 冒烟会临时停用 `PerformanceProfiler`，中断后用它恢复 | — |
| `DigitalTwin/UI/7 Chart Replay Preview (edit mode)` | 编辑期把夹具灌进图表并出图（不需进 Play） | `Logs/ui_chart_preview.txt` + `i06..i09*.png` |
| `DigitalTwin/UI/8 Check UI Font Coverage (read-only)` | 文案 × TMP 静态字形集检查 | `Logs/ui_font_check.txt` |
| `DigitalTwin/UI/9 Snapshot UI States (edit mode)` | 5 个状态固定机位渲染 | `Logs/ui_snapshots.txt` + `i00..i05*.png` |

## 10. 下一步（按优先级）

1. ~~修 `QuickOutline` 不可读 Mesh~~ **已完成（2026-11-03）**：在 `Outline.LoadSmoothNormals` 里跳过
   `!isReadable` 网格，未改模型导入设置；四页 Play 走通（`play_p08–p11`）。
2. `external_joint_torque` 实测（连 ROS2 的 E1/E2 会话里抓 `/dsr01/rt_topic/external_joint_torque` 是否有数据）。
3. `User_ui` 的归属决策（删 / 改成真正的拆解页 / 并入图表卡）。
4. 强制把 `Canvas_I P`、`Canvas_I软著` 的 Panel 尺寸异常（−6×227、0×0）清一遍，避免遮挡。

## 11. 二期：双主面板（R2，2026-11-03 23:00）

按用户决策把 UI 拆成两个主面板并统一工业化视觉：**A 实训任务台（漫游/训练向导）**、**B 孪生监控台（状态监控 + 双向控制 + 记录）**，
共享一条常驻顶栏（数据源/帧率/时延/帧龄/告警/记录/急停/时钟）。

- 新根：`30_界面与交互/UI_二期面板`（370 Transform，ScreenSpaceCamera，order 100）；旧 `UI_统一入口面板` 仅停用保留。
- 新增运行时组件：`UI/Style/{TwinUIStyle,TwinUIButton,TwinUITextR2}.cs`、`UI/Bind/TwinUIStatusBinder.cs`、
  `UI/R2/{TwinUIPageHost,TwinUIHeaderView,TwinUIMissionView,TwinUITwinOpsView}.cs`。
- 新增编辑器：`TwinUIR2Builder`（UI/1、UI/10、UI/11）、`TwinUIR2Audit`（UI/12）、`TwinUIR2Snapshot`（UI/14）、
  `TwinUIR2PlayCheck`（UI/15）、`TwinUIR2GlyphCheck`（UI/16）。
- 设计令牌、按钮五态、面板尺寸、绑定字段与验收数字见 `UI_DESIGN_SYSTEM.md`。
- 仍未做：控制页 jog 行按授权要求保持禁用（原因文案在界面里）；曲线页在无数据源时只画网格并显示空态；
  3D 侧孪生可视化（力矢量/轨迹/告警高亮）留给独立会话，按「代码并行、场景串行」规则接入。

## 12. R3 数据契约与 AI 可读接口（2026-11-04）

### 12.1 契约
`Scripts/UI/Industrial/TwinUiSnapshot.cs`：`SchemaVersion` + `SourceKind`（运行时/回放/演示）+ `Capabilities` 位标 + `Link`（Connected/HasFrame/SampleHz/ApplyHz/UiHz/FreshnessMs/ReceiveToApplyMs/JitterP99Ms/DroppedFrames/OutOfOrderFrames/ClockSync）+ `Joints[]`（PositionDeg/VelocityDegPerSec/TorqueNm/TargetDeg/ErrorDeg/LimitState）+ `Pose`（XYZ/RPY/HasPose）+ `Wrench` + `Experiment`（RunId/Profile/Condition/PayloadKg/TcpDefinition/Session/SelfCheck）。

### 12.2 数据源与管线
`ITwinUiDataSource` 三实现：`RuntimeTwinUiDataSource`（真实帧）、`ReplayTwinUiDataSource`（读 `PaperLogs/<runId>/samples.csv`）、`MockTwinUiDataSource`（强制 `MOCK` 水印）。`TwinUiPipeline` 统一做 5 Hz 快照聚合；`TwinUiEventBus` 追加写 `Logs/ui_events.jsonl`；`TwinUiSnapshotExport` 5 Hz 写 `Logs/ui_snapshot.json`。

### 12.3 不变式（写死）
1. UI 只读数据链；不写 `TwinRecorder/Ros2Bridge/DigitalTwinRuntime` 字段。
2. 命令必须经 `SafetyCheckService → TwinCommandController`，UI 不直连 driver。
3. 速率/阈值/主题只影响显示与判读，不影响采集。
4. 能力位缺失显示「未订阅」，不得补 0。
5. 外部 AI 只消费 `ui_snapshot.json` / `ui_events.jsonl`，不直连 Unity 进程。

### 12.4 扩展点
新增页面实现 `ITwinUiPage` 并在 `TwinUIPageHost` 注册；新增数据源实现 `ITwinUiDataSource`；告警规则集中在 `TwinAlarmRules`；构建菜单 `UI/2`（R3）、`UI/3`（复核）、`UI/4`（截图）、`UI/5`（设置/重置）。

## 13. 交互层架构（2026-11-04，A1）

```
鼠标/键盘 ──> TwinPlayCameraRig（镜头）         [相机对象]
            └> TwinPlayRayVisual（屏幕射线）──> ITwinPointerHit ──> 上层交互（IK 手柄 / 3D 物体）
EventSystem（InputSystemUIInputModule）──> uGUI 按钮 ──> TwinUIButton + TwinUIGradientHover
```

不变式：
1. 交互层只读 `RobotStateFrame` 与 `TwinUiSnapshot`，不写采集/数据链。
2. 任何"动机械臂"的动作必须走 `TwinCommandController.EnterPlan → UpdatePlanTarget → EnterExecute`，且经 `SafetyCheckService`；A1 不涉及下发。
3. UI 命中优先于世界射线（`IsPointerOverGameObject`），避免点按钮时误触场景。
4. 相机控制与射线是两个独立组件，可单独关闭；VR 路径（`XR Origin` 未激活）不受影响。
5. 场景级挂载由 builder 完成：`UI/6 Attach Play Interaction Rig`；自检 `UI/17`；IK 手柄预留 `UI/7`/`UI/18`。

### 13.1 IK 手柄（A2，2026-11-05）

```
ITwinPointerHit（射线命中）
   └─> TwinPlayIkHandle：手柄命中/拖动/轴锁定/护栏
         ├─> RobotIkController.SetTargetPose → TrySolve（DLS，位置-only）
         ├─> TwinCommandController.UpdatePlanTarget（本地 ghost，不下发）
         └─> HUD：cur/tgt/误差/iter/status/dispatch=disabled
```

数据流只写"本地目标 + ghost 关节"，`EnterExecute` 未被调用（`dispatchCalls=0`/`enterExecuteCalls=0` 有日志断言）。
真机下发属于 A40：必须 A35（Unity ROS-TCP 收帧）修复后，走 `EnterPlan→EnterExecute→SafetyCheckService→ROS move_joint`，并单独排期实验。

## 14. 窗口化与模块化架构（P2–P6，2026-11-06 生效）

> 本节是 `Scripts/UI/Window/` 与 `Scripts/UI/Modules/` 的现状说明；菜单与证据见 §14.4 / §14.5。

### 14.1 窗口层（P2）
- `TwinUiWindowHost`：主窗宿主。负责 `UiWindowRoot`（窗口矩形，可拖动/八向缩放/最大化/最小尺寸钳制）与 `UiWindowContent`（设计尺寸 1920×1080，pivot 左上）的映射；布局与状态持久化到 `PlayerPrefs`（`TwinUI.Window.Layout.v1`）。
- `TwinUiWindowFrame`：窗口外壳与交互（标题栏、边框、圆角 13、外阴影、活动窗强调描边、八向手柄：边 11px / 角 23×23、最大化按钮、拖动/缩放句柄），全部尺寸与颜色读 `TwinUIStyle`/`TwinUITheme` 令牌。
- `ITwinUiFrameHost`（P5）：Frame 只依赖该接口（`MoveBy/ResizeBy/Get/SetBounds/OnFrameDragBegin|End|ResizeEnd/SaveLayout/ToggleMaximized/AllowsMaximize`）。主窗 `TwinUiWindowHost` 与悬浮模块窗 `TwinUiModuleWindow.FrameHostAdapter` 各自实现 → 同一份外壳代码两处复用，不再各写一套。
- 窗口生命周期、Play 进场/退场见 `TwinUiWindowHost.LateUpdate` 的守卫与 `UI/29` 安装、`UI/30` 自检。

### 14.2 响应式布局（P3）
- `TwinUiResponsiveLayout`：按窗口内容宽度三档重排，**取消 scale-to-fit**（`UiWindowContent.localScale=1`）。
  - `Wide ≥1500`：Steps 420 / Detail 610 / Eval 552 三列；Header 全 chip。
  - `Medium 1100–1499`：Steps 320 / Detail 自适应 / Eval 收成右侧抽屉（552 宽，右贴齐）。
  - `Narrow <1100`：Steps 300 / Detail 自适应 / Eval 抽屉叠加 Detail（宽 = Detail）。
  - Header 溢出收起（Medium 起隐藏 `KpiSource/KpiAge/RecBadge/ModeChip`；Narrow 仅 Title+KpiLatency+KpiRate+EstopChip）。
- 只改锚点/`sizeDelta`/`anchoredPosition`，不重建对象、不移层级；档位切换幂等。

### 14.3 模块框架（P4–P6）
- `TwinUiModule`（契约）：`Id/Title/DefaultSlot/CompactSize/ExpandSize` + `Build/Refresh/OnAttach/OnDetach/OnExpandChanged/DestroyView`。
- `TwinUiModuleHost`：注册表 + 三态 `Hidden / Docked / Floating`；`PlayerPrefs` key `TwinUI.Modules.v1`；Header「模块」菜单、底部 dock 条（`UiModuleDock`，支持多卡分页）、悬浮窗字典与 z 序、`BringToFront`、Esc 关闭、24px 贴边吸附、`UiDockPreview` 停靠预览与 `UiDockDragGhost` 拖出幽灵。
- `TwinUiModuleWindow`：悬浮模块窗，内部**实例化 `TwinUiWindowFrame`**（P5），通过 `FrameHostAdapter` 实现 `ITwinUiFrameHost`。
- 现有真实模块：`TwinUiPerformanceModule`（只读 `PerformanceProfiler.TryGetSnapshot`，含 60 s FPS 折线 `TwinUiFpsSparkline`）、`TwinUiDataLinkModule`（`TwinUIStatusBinder.Current`：连接/Hz/延迟/抖动/告警）、`TwinUiTaskProgressModule`（`DisassemblySystem.Instance`：层/可交互件/完成态）。
- 旧 IMGUI 性能面板（`20_BatteryDisassembly/Scripts/DB/PerformanceProfiler.cs` 的 `showOnScreenUI`）在模块激活时被置 false、隐藏时恢复原值——旧代码保留、未删除。

### 14.4 输入与主题集成
- 所有拖动/缩放/停靠走 `TwinUiInputArbiter`（`WindowDrag=4`、`WindowResize=5`）；不新起输入路径。
- 主题：`TwinUIThemeMode.Dark/Light/Ocean`，`TwinUITheme` 为唯一调色板来源；`TwinUIToneTag` 组件负责角色→颜色重刷；`TwinUIThemeApplier` 在 Play 引导（P3 已把场景内嵌 MonoScript 引用修复为资产引用，`UI/36`）。
- 场景启动主题由 `TwinUiWindowHost.bootTheme` 兜底（防 applier 未实例化）。

### 14.5 菜单与证据
- 安装/自检：`UI/29/30`（窗口）、`UI/31/32/33`（Ocean 主题）、`UI/35`（响应式 57 项）、`UI/37/38`（模块 20 项）、`UI/39`（浮窗/多模块 22 项）、`UI/41`（跨窗拖靠 10 项）、`UI/44`（Wide 图）、`UI/45`（Editor 60 s 性能）。
- 证据目录：`dsh_2026-11-05_p2_window`、`dsh_2026-11-06_p3_responsive`、`dsh_2026-11-06_p4_modules`、`dsh_2026-11-06_p5_floating`、`dsh_2026-11-06_p6_refine`。
- 场景基线 `9F907809…`（6,976,838 B）；备份 `_backup/scenes/2026-11-06_p4_modules_final/`。
- 边界：窗口/模块自检均为 Play 内组件 API 断言，真实鼠标手感与人眼观感 UNKNOWN；吸附为 24px 阈值判定非磁吸动画。
