# 二期 UI 设计系统（工业 HMI / 数字孪生面板）

生成时间：2026-11-03 23:00　构建器：`Assets/DoosanDT/10_DigitalTwin/Editor/TwinUIR2Builder.cs`
场景：`Doosan-DT.unity` v2026-11-03.8（1,887 GameObject，4,648,721 B，SHA256 `289B6888…18588E9F7`）
配色：§1 为初版深色令牌，**当前生效的是 §8 蓝白版**（2026-11-03 23:12 起）

## 1. 令牌（`Scripts/UI/Style/TwinUIStyle.cs`，全部面板/按钮/文字只从这里取值）

| 令牌 | 值 | 用途 |
|---|---|---|
| `BgColor` | `#0B0F14` | 全屏底 |
| `PanelColor` | `#121A22` | 顶栏/面板 |
| `CardColor` | `#18222D` | 卡片 |
| `CardAltColor` | `#1E2937` | 行底纹/次级卡 |
| `BorderColor` | `#27323F` | 1px 描边（`UI.Outline`） |
| `TextColor` / `MutedColor` | `#E8EEF5` / `#93A2B3` | 主文 / 次要文 |
| `AccentColor` | `#2E9BD6` | 选中、主按钮、焦点（全局唯一主色） |
| `OkColor` / `WarnColor` / `CritColor` / `InfoColor` | `#2ECC71` / `#F2A33C` / `#E5484D` / `#4C8DFF` | 状态语义 |
| `DisabledColor` | `#5B6B7C` | 禁用文字（必须配禁用原因） |
| `SeriesColors[6]` | `#4C8DFF #2ECC71 #F2A33C #E5484D #A78BFA #22D3EE` | 曲线序列 |

尺寸：8px 栅格、面板内边距 12、圆角 4、描边 1px；按钮高 40（VR 校验 ≥48）。
字号阶梯：24 标题 / 18 区块 / 16 正文与数据 / 13 说明 / 12 微标签；数值右对齐等宽。

## 2. 按钮（`Scripts/UI/Style/TwinUIButton.cs`）

五态 Normal / Hover / Pressed / Selected / Disabled，四种语义 Default / Primary / Danger / Plain。
Selected 用「强调色描边 + 3px 左条」；Danger 红底红边；Armed（二次确认）3 秒自动回落。
未接线按钮其 `Label` 不为空；禁用时必须给 reason（jog 行示例：`需真机授权后启用`）。

## 3. 面板结构（`UI_二期面板`，ScreenSpaceCamera，1920×1080，sortingOrder 100）

- 常驻 Header（1888×56）：产品名 / 模式芯片 / 4 个 KPI（数据源·帧率·时延·帧龄）/ 告警徽标 / 记录徽标 / 急停徽标 / 时钟
- 页签 TabBar：实训任务台、孪生监控台
- 任务台 MissionRoot（1920×940）：Steps 420 / Detail 900 / Eval 552
- 监控台 TwinOpsRoot：SubNav（概览/曲线/控制/记录）+ 四个子页各 1888×880
  - 概览：Source 520 / Chain 680 / Metrics 672 / Alarms 680×452
  - 曲线：三张 1888×280 图表卡（关节角 / 关节力矩 / 六轴力力矩），20 Hz 采样、20 s 窗口
  - 控制：Mode 640×112 / Checks 640×500 / Log 640×252 / Jog 600×420 / Speed 600×112 / Actions 1240×332
  - 记录：Rows 920×560 + Session 952×560（只读，不提供开始/停止）

## 4. 数据绑定（`Scripts/UI/Bind/TwinUIStatusBinder.cs`，5 Hz，只读）

来源：`DigitalTwinRuntime.ActiveSourceHealth / LatestMetrics / TryGetLatestFrame / CommandController / Recorder`。
告警规则：未连接 / 帧龄 >2 s（>0.5 s 警告）/ 应用时延 >200 ms（>80 ms 警告）/ 丢弃帧 / 乱序 / 抖动 >20 ms /
帧质量非 Normal / 时钟未同步 / 运动错误 / 急停。无数据时显示「未连接 / 未订阅 / 无数据」，绝不显示 0。

## 5. 菜单

| 菜单 | 作用 |
|---|---|
| `UI/1 Build Unified Shell (R2)` | 全量重建顶栏 + 两个主面板 |
| `UI/10 Build Mission Page` | 只重建任务台（保留监控台） |
| `UI/11 Build TwinOps Page` | 只重建监控台（保留任务台） |
| `UI/12 Audit R2 Layout & Tokens` | 只读审计：层级/尺寸/调色板/字号/按钮高度/对比度 |
| `UI/14 Snapshot R2 States` | 编辑态截图 `Logs/ui_shots_r2/s00..s04` |
| `UI/15 Play Check R2` | 进 Play 切换两页 8 个子页、采样 binder、收集异常后自动退出 |
| `UI/16 Check R2 Glyphs` | 逐字校验静态字形集（当前 168 文本 / 752 字符 / 0 缺字） |

## 6. 本轮验证（编辑态 + 运行态）

- 构建：`transforms=368 canvas=true result=PASS`
- 审计：`buttons=21 minHeight=40 below40=0 noLabel=0`；Image 颜色 distinct=17（全部令牌，无游离色）；字号集合 `{12,13,14,15,16,18,24}`
- 对比度：Text/Card 13.77、Muted/Card 6.17、Accent/Card 5.19、Crit/Card 4.11（红仅用于告警文字/徽标，按大字阈值使用）
- 字形：`missingDistinct=0 RESULT: PASS`
- Play：两页切换 + 子页 1/0/0/0 → 0/1/0/0 → 0/0/1/0 → 0/0/0/1，`exceptions=0 RESULT: PASS`；快照 `hasFrame=False connected=False source=Ros2 alarms=1 fps=54 control=Sync`（**未连 ROS2，属预期空态**）
- 截图：`s00_mission`、`s01_twinops_overview`、`s02_twinops_trend`、`s03_twinops_control`、`s04_twinops_record` 五张互不相同（MD5 均不同）

## 7. 边界

未改数据链（`Ros2Bridge/TwinRecorder/DigitalTwinRuntime/SafetyCheckService/TwinCommandController` 行为与字段）、
未改 `TwinRuntimeProfile`、未连 ROS2、未采集、未录制、未跑实验；旧 `UI_统一入口面板` 仅 `SetActive(false)` 保留。
趋势子页本轮更名「曲线」，避开静态字形集缺字（`趋` 不在集内）。


## 8. 配色切换：蓝白版（2026-11-03 23:12，覆盖 §1 的颜色值）

按用户要求改为 DeepSeek 风格蓝白配色，红/绿/黄只用于状态与危险操作（灰阶仍是主体）。

| 令牌 | 新值 | 用途 |
|---|---|---|
| `BgColor` | `#F4F6FB` | 页面底 |
| `PanelColor` / `CardColor` | `#FFFFFF` | 顶栏 / 卡片 |
| `CardAltColor` | `#F2F5FA` | 行底纹 |
| `BorderColor` | `#DFE5EF` | 1px 描边 |
| `TextColor` / `MutedColor` | `#1F2328` / `#667085` | 主文 / 次要文 |
| `AccentColor` | `#3F5AF0` | 选中、主按钮（深一档保证白底可读；品牌蓝 `#4D6BFE` 用于大面积填充时的近似色） |
| `OkColor` / `WarnColor` / `CritColor` / `InfoColor` | `#15803D` / `#B45309` / `#DC2626` / `#2563EB` | 绿 / 黄 / 红 / 蓝 |
| `DisabledColor` | `#9AA4B2` | 禁用 |
| `SeriesColors` | `#4D6BFE #15803D #B45309 #DC2626 #7C3AED #0E7490` | 曲线 6 序列（浅底可读） |

按钮同步改为浅底：默认白底深字、主按钮蓝底白字、危险红底白字、选中浅蓝底 `#E8EDFF`、禁用浅灰 `#F0F2F5`；
图表画布背景白、网格 `#D9E1EE`，序列颜色在 `TwinTelemetryChannel.cs` 内同步替换。

复核（`Logs/ui_r2_audit.txt` 23:12）：颜色 distinct=13 全部令牌；`buttons=21 minHeight=40 below40=0`；
对比度 Text/Card 15.80、Muted/Card 4.97、Accent/Card 5.34、Crit/Card 4.83（全部 ≥4.5）。

场景：v2026-11-03.8，4,648,721 B，SHA256 `289B6888D886F6B00ABF3E8DA7BE5CB9D0D9044DDFB9A7012C7E3CB18588E9F7`。

## 9. R3 工业驾驶舱（当前生效，2026-11-04）

> 本节替换 §8 作为**当前生效**配色；§1、§8 保留为历史。Unity 侧实现见 `Scripts/UI/Style/TwinUITheme.cs`（505 行）与 `TwinUiSettings.asset`。

### 9.1 深色令牌（默认主题 `Dark`）
| 用途 | 值 |
|---|---|
| 画布底 | `#0A1018` |
| 面板 | `#111A26` |
| 玻璃卡片 | `#16222F` |
| 分隔线 | `#223047` |
| 主文字 | `#E6EDF7` |
| 次文字 | `#93A4BC` |
| 主色（青） | `#22D3EE` |
| 强调蓝 | `#3B82F6` |
| 正常 | `#22C55E` |
| 注意 | `#F5A524` |
| 危险 | `#EF4444` |
| 禁用 | `#4B5A70` |

### 9.2 浅色回退主题（`Light`，投屏/论文插图）
白底 `#F7F9FC`、卡片 `#FFFFFF`、主文字 `#1F2328`、次文字 `#667085`、主色 `#0E7490`、危险 `#DC2626`。

### 9.3 排版与组件规则
- 8 档字号：10/12/13/14/15/16/18/24；数值列等宽（`digitAdvanceSpread=0`，用 TMP 自带 NotoSansSC，不引第三方字体）。
- 按钮最小高度 40 px；五态（常态/悬停/按下/选中/禁用）+ primary/danger/armed。
- 状态色只用于状态；超限同时出现图标+文字，不只靠颜色（色盲友好）。
- 能力位缺失显示「未订阅 / 无数据源 / —」，**禁止显示 0**。

### 9.4 阈值初值（全部 `calibrated=false`，待现场标定）
采样 Hz <95 黄 / <80 红；应用 Hz <25 / <15；新鲜度 >200 ms / >1000 ms；receive→apply >20 ms / >50 ms；jitter p99 >2 ms / >5 ms；位置误差 >0.5° / >1.0°；关节力矩 60% / 85% 额定；六维力 20 N / 50 N。

### 9.5 验收口径（已实跑）
`UI/12` 审计 `failures=0`；`UI/16` `missingDistinct=0`；`UI/14` Replay 13 张截图；`UI/15` Play `exceptions=0` 且 UI Hz 30/60 实测 28.73/50.30 Hz。见 `agent_runs/dsh_2026-11-04_ui_industrial/`。

## 10. Play 交互层（2026-11-04 A1，当前生效）

镜头的"Scene 视图手感"与鼠标射线由 `Scripts/Interaction/` 下两个组件提供，全部字段 Inspector 可调（中文 Tooltip）：

### 10.1 相机（TwinPlayCameraRig）
右键长按 + WASD 平移（按世界水平面）、Q/E 世界上下、Shift×4 加速、Ctrl×0.25 减速、滚轮改 moveSpeed（默认 2 m/s，范围可调）、俯仰限位 ±85°、位置平滑、右键时锁鼠标。`Start()` 自动禁用同相机上的 `SmartRaycaster`，避免双控。

### 10.2 射线（TwinPlayRayVisual）
- 视觉：LineRenderer 起止线宽、四态色（未命中 `#21D9F0`、命中物体 `#21C45E`、命中 UI `#F5A624`、按下 `#FFF299`）、是否置顶、命中点标记大小/显隐、最大距离、LayerMask、总开关。
- 命中 UI 时世界射线关闭（`worldRayActive=false`），避免误触 3D 物体。
- HUD（可关）：命中对象名 + 世界坐标 + 屏幕像素；遮罩层透明不挡操作。
- 接口 `ITwinPointerHit.TryGetHit(out RaycastHit, out bool overUi)` 供上层（IK 手柄）复用。

### 10.3 按钮渐变（TwinUIGradientHover）
`BaseMeshEffect` 只改顶点色、不动材质：非 hover 纯色（spread≈0）；hover 基色→强调色线性渐变（默认强度 0.35，`TwinUiSettings.hoverGradientStrength` 可调）；按下更亮 + 缩放 0.985。开关 `TwinUiSettings.enableHoverGradient`。
注意：渐变按按钮实例挂载，重跑 `UI/1`/`UI/2` 等重建面板后需重跑 `UI/6`。

### 10.4 验收口径
`UI/17 Play Interaction Check (auto-exit)`：位移、拖拽 yaw、俯仰 clamp、滚轮调速、三点射线差异、四态线色、hover 顶点色差、按下色/缩放、截图数、运行期异常，逐项打印实测值。

## 11. 拖动末端法兰（IK ghost 预览，2026-11-05 A2）

- 手柄：TCP 处球体 + 三轴短线；空闲青色 `#21D9F0`、拖动琥珀 `#F5A624`、不可达红 `#FF3833`；尺寸/显隐 Inspector 可调。
- 操作：左键长按手柄拖动（默认相机平面）；X/Y/Z 锁定单轴；Shift 精调；Esc 取消；`R`/「回当前」复位到当前 TCP。
- HUD：当前/目标位置、位置误差 mm、迭代次数、IK 状态、`dispatch=disabled (local preview only)`；不可达时显示 UNREACHABLE。
- 护栏：单帧位移 ≤5 mm（实例可调）、工作空间 AABB ±0.5 m、关节限位（复用 RobotModelController）、位置-only IK（tol 0.0015 m / 10 ms）。
- **下发边界**：`allowRobotDispatch=false` 硬开关，默认任何路径不调用 `EnterExecute`；真机下发需 A35 修复 + 安全评审 + 用户授权（TASKS A40）。

## 12. Ocean 深海科技蓝 + 窗口外壳（P2.5–P6，当前默认主题）

> 本节覆盖 §9.1 的 Dark 值作为**当前默认**；`Dark`/`Light` 仍保留可回退（`UI/32` 回滚 Dark）。

### 12.1 调色板（`TwinUIPalette.Ocean()`，唯一来源 `TwinUITheme.cs`）
| 令牌 | Ocean | 用途 |
|---|---|---|
| `Bg` | `#071827` | 窗外国深海底色（α0.30 混合）、图表底 |
| `Panel` | `#102637` | 面板 |
| `Layer` | `#0E2535` | 窗口内容底 `WindowBackdrop` |
| `Layer2` | `#173345` | 标题栏底 / `CardAlt` |
| `Card` | `#132E40` | 卡片 |
| `Border` | `#2B5268` | 1px 窗口边框 / 常规描边 |
| `LineStrong` | `#42738D` | 强分隔线 / 按钮 hover 边 |
| `Text` | `#E7F5FF` | 正文 |
| `Muted` | `#99BBCA` | 次级文字 |
| `Accent` | `#39C5E5` | 强调 / 手柄 hover / 选中边 |
| `Accent2` | `#2D7CBB` | 次级强调 |
| `Strong` | `#53D4E6` | 活动窗强调描边（α0.85） |
| `Shadow` | `#000000@64` | 窗口外阴影 |
| `Ok / Warn / Crit / CritText` | `#40D0A0 / #F0BF63 / #FA717A / #FFB3B8` | 状态色 |
| `PlotBg / Grid` | `#0A1E2D / #23455A` | 图表底/网格 |

### 12.2 尺寸与圆角（`TwinUIStyle` 常量，全部令牌化）
- 窗口圆角 `RadiusWindow=13`；卡片圆角 `RadiusCard=11`；控件圆角 `RadiusControl=8`（P6 用九宫格应用到既有卡片/按钮）。
- 八向手柄：边 **11px**、角 **23×23px**、hover 强调线 2px；窗口边框 1px；外阴影 `Shadow`。
- 字体：标题 24 / 数值 18 / 正文 18 / 小字（自检抽样口径见 UI/33）。

### 12.3 窗口几何（P3 现状）
- 默认窗口 `(824.0, 43.2, 1080 × 993.6)` = 屏幕 **56.25% × 92.0%**，右停靠、边距 16；最小 1080×620；最大化 1920×1080。
- 内容 `localScale=1.000`（无缩放），内容区 1078×991.6 → 默认落在 **Narrow 档**（<1100）。
- 三档断点：`Wide ≥1500` / `Medium 1100–1499` / `Narrow <1100`（内容宽口径）。

### 12.4 模块卡片
- dock 卡片：默认 250 宽一行 KPI（FPS/帧 ms/内存），展开 352 高 6 行指标 + 60 s 折线；3 卡默认单页，收窄自动分页（`perPage=2`）。
- 悬浮模块窗：最小 480×330，标题栏拖动、八向缩放、24px 贴边吸附、`AllowsMaximize=false`。

### 12.5 验收口径
- `UI/33` 主题逐项（期望 hex vs 实测 hex + `radii cards=110 controls=39 badType=0`）、`UI/30` 窗口 11 项、`UI/35` 响应式 57 项、`UI/38` 模块 20 项、`UI/39` 浮窗 22 项、`UI/41` 拖靠 10 项——全部 Play 内实测。
- 轻量主题的 WCAG 对比度已知风险见 `V3_UI_SPEC_2026-11-05.md`（浅色 5 处 <4.5），Ocean 为主题默认、Light 仅回退。
