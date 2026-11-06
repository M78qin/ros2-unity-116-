# 任务台账

状态口径：`DONE` 已验证 / `DOING` 进行中 / `TODO` 待做 / `BLOCKED` 等条件 / `DROP` 明确不做。

## A. 已交付（本轮及前序）

| # | 任务 | 状态 | 证据 |
|---|---|---|---|
| A1 | VR_EVB_DL 资源迁移进主工程（Round 1） | DONE | `dsh_2026-11-03_fusion/README.md` §6–§9 |
| A2 | 拆解场景 + 电池模型并入主工程（Round 2） | DONE | 同上 §10 |
| A3 | 建 `Doosan-DT` 场景、8 组整理、`Assets/DoosanDT/` 重组、旧物进 `90_Archive~`（Round 3） | DONE | `Logs/doosan_dt_build.txt`、`doosan_dt_verify.txt` |
| A4 | 场景减负：备份树拆分（4,682 对象 → 独立场景） | DONE | `SESSION_LOG.md` 2026-11-03 20:07 条 |
| A5 | Outline 收口（2,153 组件 → 只留 5 个启用） | DONE | `Logs/doosan_dt_polish.txt` |
| A6 | 环境静态标记（227 渲染器） | DONE | 同上 |
| A7 | 天空 + 主光 + 补光 + ACES 后处理 + URP HDR 分级 | DONE | 同上 `LIGHTING applied` |
| A8 | 同机位 before/after 截图（3 组 6 张） | DONE | `Logs/doosan_dt_view_*.png` |
| A9 | `Docs/` 记录体系（本目录） | DONE | `Docs/00_INDEX.md` |
| A10 | 界面缺失脚本清零（`登录界面` / `Gaze Interactor`） | DONE | `Logs/ui_fix_missing.txt`（`missingScriptInstances=0`）|
| A11 | 统一入口面板（漫游/训练/拆解/数据 + 图表卡 + 控制卡） | DONE | `Logs/ui_build_shell.txt`、`Docs/10_AGENT/UI_ARCHITECTURE.md` |
| A12 | 力-力矩曲线（手写 `MaskableGraphic`，无第三方库） | DONE | `Logs/ui_chart_preview.txt`、`i06–i09*.png` |
| A13 | 双向控制呈现层（预览/校验/确认/执行/急停，干运行护栏） | DONE | `Logs/ui_play_smoke.txt`（`DRY_RUN`）|
| A14 | 界面接线审计（删 `User_ui` 前 12 Canvas / 35 Button / 40 调用；删后 11 / 34 / 39，0 断链） | DONE | `Logs/ui_wiring_audit.txt`（21:36 与 22:19 两次）|
| A15 | UI 文案 × 静态字形集检查 | DONE | `Logs/ui_font_check.txt`（41 条 0 缺失）|
| A16 | Play-mode UI 冒烟：欢迎→进入→漫游→数据→控制卡→训练→拆解，11 张 1920×1080 | DONE | `Logs/ui_play_smoke_editor.txt`（`sceneUnchanged=True`、CSV 目录仍不存在；`BASE + EXTENDED RESULT: PASS`）|
| A17 | 删除遗留 Canvas `User_ui`（引用扫描 0 引用后删，场景 1,519 对象） | DONE | `Logs/ui_remove_userui.txt`（`result=PASS`）|

## E. 界面后续（2026-11-03 收口时新增）

| # | 任务 | 状态 | 说明 / 前置 |
|---|---|---|---|
| E1 | 修 `QuickOutline` 在不可读 Mesh 上抛错 | **DONE**（2026-11-03） | 已在 `LoadSmoothNormals()` 跳过 `!isReadable` 网格，未改模型导入设置；四页 Play 走通 |
| E2 | 训练/拆解页 Play 完整走查 | **DONE**（2026-11-03） | `play_p08/p09`（训练）、`play_p10/p11`（拆解）；`EXTENDED RESULT: PASS` |
| E3 | 实测 `/dsr01/rt_topic/external_joint_torque` 是否有数据 | TODO | 决定力矩曲线在真机上是"有曲线"还是"长期未订阅"；需连 ROS2 会话（单独授权）|
| E4 | `User_ui` Canvas 归属决策 | **DONE**（2026-11-03，删除） | 用户决定删除；`UI/5` 执行，见 A17 |
| E5 | 旧 Canvas 双 Raycaster 清理 | TODO | 5 个旧 Canvas 同时挂 `GraphicRaycaster` + `TrackedDeviceGraphicRaycaster`，可能重复命中 |
| E6 | `Canvas_I P` / `Canvas_I软著` Panel 尺寸异常 | TODO | sizeDelta −6×227、0×0，易造成遮挡；属旧资产 |
| E7 | 真机分支护栏复核 | PLANNED | 面板在 `EnableRealRobotCommand && CanSendRealMove` 时主动拒绝执行；真机通路开启前需复核一次 |

## B. 视觉/性能后续（按性价比排序）

| # | 任务 | 状态 | 说明 / 前置 |
|---|---|---|---|
| B1 | 烘焙 Lightmap（环境 + 工作站静态件） | TODO | 场景已打静态标记，可直接 `Generate Lighting`；预计 1–2 h 机器时间。**要你先拍板**：当前是实时混合光照，烘焙后反射/色彩会变，需重拍 `_after` 图 |
| B2 | Reflection Probe（试验台区域 1 个 Box Probe） | TODO | 无探针时金属/桌面反射只靠天空盒，观感偏"塑料"；加 1 个探针成本低、见效快 |
| B3 | Light Probe Group（机械臂/电池包周围 8–12 点） | TODO | 烘焙后动态物体（机器人）才不显得"浮"在暗环境里 |
| B4 | 机械臂材质 PBR 化（现仅 2 个材质） | TODO | 30 个 `.dae` 共用 2 材质；需补金属/粗糙度贴图或统一调参，属"观感提升"非必需 |
| B5 | 电池包材质整理（33 个材质 / 35 张贴图） | TODO | 8 MB 的 UI `.tif` 与遗留 specular/displacement 图可考虑剔除，省内存 |
| B6 | 环境精简：重复桌椅上合并/删减 | TODO | 需你确认哪些是"画面需要"、哪些是"占地" |
| B7 | 场景重复项清理（Camera 3→1、EventSystem 2→1、平行光 2→1） | TODO | 影响极小，属整洁度；改前需确认没有脚本按名字找 `Main Camera` |
| B8 | LOD / Occlusion Culling / GPU Instancing 检查 | DROP（本轮） | 收益主要在 GPU；论文不强依赖，等你确认"帧率不达标"再做 |
| B9 | HDRI 环境贴图替换程序化天空 | DROP（默认） | 需下载外部 HDRI（许可/体积），程序化天空已够用；你要"电影级"再议 |
| B10 | SSAO / 屏幕空间反射 | DROP（默认） | 桌面级显卡够，但 VR 双眼 × 分辨率下性价比低 |

## C. 论文相关（本轮唯一有论文价值的是 C2）

| # | 任务 | 状态 | 说明 |
|---|---|---|---|
| C1 | 图 4-x 场景/工作站截图替换现有初稿图 | TODO | 用 `Logs/doosan_dt_view_*_after.png`；需在图注写清"Unity 编辑模式渲染，非 VR 实时截图" |
| C2 | 实验 C2-opt：漫游路径帧时间 P50/P95 前后对比 | BLOCKED | 需你授权进 Play 只读采样（不连 ROS、不控机器人）；采样脚本可复用 `PerformanceProfiler`，但必须只用编辑器内采样，不能碰数据链 |
| C3 | §2.4.1 面数/工时口径复核 | TODO | 论文写"整包约 130k 三角面"，当前场景电池包实际面数需单独统计后再写；现只有工程统计总量 |

## D. 明确不做（本轮边界）

- 不新增工业工作站几何（用户 2026-11-03 明确）。
- 不跑机器人动作、不调 `/dt/cmd/*`、不采集数据、不连 ROS2、不改 `VR_EVB_DL`。
- 不覆盖历史批次记录（`dsh_2026-10-29 / 11-02 / 11-03`）。

## E. 待你决策的三件事

1. **B1 烘焙**要不要现在做？做的话本轮 `_after` 图会作废重拍（约 2 h 机器时间）。
2. **B2/B3 探针**是否一起做？成本约 20 min，观感提升明显。
3. **B6 环境删减**要不要做？需要你指认哪些桌椅/柜子可以去掉。

## R2 二期面板（2026-11-03 追加）

| 编号 | 任务 | 状态 |
|---|---|---|
| A18 | 设计令牌 + 按钮组件 + 文案集中管理 | DONE（`TwinUIStyle/TwinUIButton/TwinUITextR2`） |
| A19 | 只读状态绑定层（5 Hz、告警分级） | DONE（`TwinUIStatusBinder`） |
| A20 | 常驻顶栏 + 两个主面板骨架 | DONE（`UI/1` 全量构建，368 Transform） |
| A21 | 监控台四子页（概览/曲线/控制/记录） | DONE（Play 切换 PASS） |
| A22 | 任务台（步骤/详情/评估/时间轴占位） | DONE（本地计时自评，界面已注明未接评分系统） |
| A23 | 令牌审计 + 字形校验 + Play 自检工具 | DONE（UI/12、UI/15、UI/16） |
| A24 | 3D 孪生可视化（力矢量/TCP 轨迹/告警高亮/幽灵对比） | OPEN（独立会话，代码并行、场景串行） |
| A25 | 真机授权后的 jog 与真机下发流程评审 | OPEN（安全评审前置） |
| A26 | 旧 `UI_统一入口面板` 去留（停用观察 2 周） | OPEN（观察期至 2026-11-17） |

| A27 | 评分规则设计与实现（四维度 100 分 + 一票否决） | 规则 DONE（`TRAINING_SCORING.md`）；代码实现 OPEN |
| A28 | UI 配色改蓝白（DeepSeek 风格） | DONE（v2026-11-03.8，对比度 ≥4.5） |

| A29 | UI 网页原型（5 屏 × 3 变体 × 2 配色 + 决策表） | DONE（`dsh_2026-11-04_ui_web`，8777，32 张截图，15 条决策） |
| A30 | E5-0 空载基线（静止 + J1/J5 ±5° ×3） | PREPARED（profile + 交接卡就绪，待实验会话执行） |
| A31 | E5-L 带负载配对实验 | OPEN（payload_kg / 夹爪 / TCP 偏移待定） |

| A32 | E1–E5 短窗采集（pilot 切片，ROS 侧） | DONE（5 会话，见 `dsh_2026-11-04_e1e5`） |
| A33 | E4-B/C Unity 端收帧修复（Endpoint 连接中止） | BLOCKED（samples.csv 0 行，待单连接修复） |
| A34 | Unity UI 工业级升级（深色+数据层+快照导出） | DONE（R3，`dsh_2026-11-04_ui_industrial`，13 截图，UI/12·14·15·16 全 PASS） |

| A35 | Unity ROS-TCP 收帧修复（E4-B/C 前置） | BLOCKED（`samples.csv` 0 行，Endpoint Broken pipe，见 `dsh_2026-11-04_e1e5/UNITY_PATH_FAILURE.md`） |
| A36 | 阈值现场标定（力矩/六维力/额定值） | TODO（当前全部 calibrated=false） |
| A37 | 真机数据下复核 UI 力矩/位姿/时钟字段映射与单位符号 | TODO（需 E4-B/C 修复后执行） |

| A38 | Play 漫游交互层（相机/射线/按钮渐变，`UI/6`+`UI/17`） | DONE（13 项全 PASS，`dsh_2026-11-04_ui_play`） |
| A39 | Play 拖动末端法兰 IK ghost 预览（`UI/7`+`UI/18`） | DONE（25 项 PASS，零下发，`dsh_2026-11-04_ui_play/ik`） |
| A40 | 真机下发（EnterExecute）门槛：A35 修复 + 安全评审 + 用户授权 | BLOCKED（默认只做本地预览，`allowRobotDispatch=false`） |

| A41 | 第四章写作工作区/技能/claim 表/图管线（`src/四章/写作_2026-11`） | DONE（56/56 自检 PASS，`dsh_2026-11-04_thesis`） |
| A42 | 第四节正文（4.2 架构→4.3 数据链→4.4 交互/UI→4.5 安全→4.6 实验→4.7 小结） | TODO（新会话，按 KICKOFF 流水线） |
| A43 | 正式重复实验（E2 五姿态×3、E3 ≥3、E4 三条件×3、E5 负载） | PLANNED（需 A35 修复 + 用户排期） |

| A44 | P0 现状审查与架构建议（AUDIT+PLAN，未改代码） | DONE（`Docs/10_AGENT/P0_AUDIT_2026-11-05.md`，证据 `agent_runs/dsh_2026-11-05_p0_audit/`） |
| A45b-1 | P1b-1 唯一渲染主相机（防回归）+ 射线 mask 19→1 + `UI/20` 性能基线 | DONE（场景 `8409E953…`，`UI/12/16/17/18` PASS，`dsh_2026-11-05_p1b_camera`） |
| A45b-2 | P1b-2 `TwinUiInputArbiter` + 死 EventSystem 清理 + `UI/28` 仲裁自检 | DONE（场景 `E1FF34F8…`；`UI/28` 10/10 PASS；`UI/12/16/17/18` 回归 PASS；`dsh_2026-11-05_p1b_input`） |
| A46 | P2 主窗窗口化（宿主/拖动/八向缩放/最小尺寸/最大化/布局持久化，`UI/29-30`） | DONE（场景 `C4E3F232CE4B46DC11BC2534D68182B53512EE80341616800840037B02541339`；`UI/30` 11 项全 PASS；`dsh_2026-11-05_p2_window`） |
| A46b | P2.5 Ocean 深海科技蓝换肤 + 窗口外壳视觉（`UI/31-33`） | DONE（`UI/33` PASS + 5 回归 PASS；场景 `8A4CB02BAD52165C9E8F6E99E4A8214FB0E28C5B0852322CC8B3ABA055532273`；`dsh_2026-11-05_p25_ocean`） |
| A46c | P3 响应式重排（去 scale-to-fit、主窗 1080 宽、三档断点、Eval 抽屉、`UI/35` 57 项） | DONE（`UI/35` 57 项全 PASS；7 项回归 PASS；场景 `EAFD0A7A0C02552B06628ED44BC0969510E0D8601CD7ED3D09F82E9EE0365C65`；`dsh_2026-11-06_p3_responsive`） |
| A50 | P4 UI 模块框架 + 性能监测模块（旧 `PerformanceProfiler` 集成，Hidden/Docked/Floating） | DONE（`UI/38` 20/20 PASS；8 回归 PASS；场景 `9F907809DD8B5325021C087C0F6B3B6FB158857CF2FCF6F17725116D572DCD45`；`dsh_2026-11-06_p4_modules`） |
| A51 | P5 浮窗体系收口（Frame host 抽象复用、多模块 dock 分页、贴边吸附、第二/三模块、论文截图） | DONE（`UI/39` 22/22 PASS、`UI/40` 截图产出；场景未改 `9F907809…`；`dsh_2026-11-06_p5_floating`） |
| A52 | 论文图 4-15 替换（`UI/40` 截图）+ 图注/占位表述清理 + docx 重渲染复核 | DONE（docx `04ce8775…` 1,712,578 B、24 页、图注同页、几何 0 问题、`final_check` 82 PASS；`render_2026-11-06`） |
| A53 | P6 UI 精修：跨窗拖拽停靠 `UI/41`、Player Build 性能 `UI/42/43/45`、卡片/控件圆角、Wide 备用图 `UI/44` | DONE（`UI/41` 10/10；Build 成功、Player 60 s：fps 60.04/帧 16.65 ms/引擎 133.94 MB/托管 13.12 MB/渲染器 144；`UI/33` `cards=110 controls=39 badType=0`；场景未变 `9F907809…`；`dsh_2026-11-06_p6_refine`） |
| A49 | 修 `UI/12` 审计副作用（临时切 Light 后不还原主题、留下 dirty 场景 → 后续 attach 保存污染场景） | DONE（P3 完成：`restoredTheme=Ocean sceneDirty=False`、前后哈希不变） |
| A54 | 论文性能章/表补充 Build vs Editor 同口径对照（60.04/16.65 ms/133.94+13.12 MB vs 203.47/4.92 ms/476+623 MB，注明 vSync 与编辑器开销） | TODO（负责人 thesis-scaffold；等用户确认是否写入第 4 章 4.6 节） |
| A48 | V3 原型规格提取 + 差距矩阵（只读） | DONE（`Docs/10_AGENT/V3_UI_SPEC_2026-11-05.md` 494 行；30 条差距；39 字段映射；`dsh_2026-11-05_v3_spec`） |
| A45 | P1a 遗留 Canvas 禁用批1（`UI/21/22/23` + 回归） | DONE（13 Canvas→active 1；场景 `5EC7EB2C…`；`dsh_2026-11-05_p1_legacy`） |
| A47 | P6 数据侧并行线（A35 收帧修复 + 录制器唯一化） | BLOCKED（A35 根因未定位；独立写入域） |

