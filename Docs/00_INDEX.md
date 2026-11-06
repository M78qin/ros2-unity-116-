# Doosan-DT 工程记录总索引（唯一入口）

> 谁接手都先读这一页；细节在 `10_AGENT/`、`20_SCENE/`、`30_VISUAL/`、`50_THESIS/`。
> 本目录在 Unity 工程根（`E:\new_2026-\unity\project\Unity_Ros_Try1\Docs`），**Unity 不导入、无 .meta**，只做人和 AI 的记录，不影响工程资源。

## 1. 当前状态（2026-11-03 22:05）

| 项 | 值 |
|---|---|
| 主场景 | `Assets/Scenes/Doosan-DT.unity`（**1,519 对象 / 3.58 MB**，删除遗留 `User_ui` 后） |
| 备份场景 | `Assets/Scenes/Doosan-DT_Archive.unity`（9.91 MB：原 `~` + 两个 `-bkup113` 备份组） |
| 视觉/性能整理 | 已完成第一轮（Outline 收口、备份拆分、环境静态标记、灯光/天空/ACES） |
| 界面 | 新增统一入口面板（漫游/训练/拆解/数据 + 力-力矩曲线 + 双向控制呈现层）；缺失脚本 0；见 `10_AGENT/UI_ARCHITECTURE.md` |
| 未做（明确推迟） | Lightmap 烘焙、Reflection Probe、Light Probe、机械臂/电池包 PBR、LOD/Occlusion、HDRI |
| 实验状态 | **进过 Play，但仅 UI 冒烟**（不连 ROS：`autoStart*=0`；不采集：`autoStartRecordingWriter=0`，`D:\Unity Projects\数据记录` 冒烟后仍不存在） |
| 场景版本 | `v2026-11-03.6`（UI 版 + 删除 `User_ui`，SHA256 见 `50_THESIS/VERSION_LOG.md`） |

## 2. 一页导航

| 想知道什么 | 去哪里 |
|---|---|
| 每次会话做了什么、证据在哪 | `10_AGENT/SESSION_LOG.md` |
| 还差什么、下一步做什么 | `10_AGENT/TASKS.md` |
| 代码结构、命名空间、红线 | `10_AGENT/ARCHITECTURE.md` |
| 界面怎么组织、图表与控制卡怎么接线、UI 缺陷 | `10_AGENT/UI_ARCHITECTURE.md` |
| 场景层级怎么组织的、怎么用 | `20_SCENE/SCENE_GUIDE.md`（+ 同目录 `PROJECT_MAP.md`、`AI_SYNC.md`） |
| 视觉/性能前后数字 | `30_VISUAL/BASELINE.md` |
| 灯光/材质/管线参数（可复现） | `30_VISUAL/PARAMS.md` |
| 论文图对应哪个场景版本 | `50_THESIS/FIG_INDEX.md` + `VERSION_LOG.md` |
| 旧记录、旧流水、旧协议 | `90_Archive~/` |

## 3. 铁律（改工程前必读）

1. **不动机器人结构**：URDF 层级、`joint_1..joint_6` 命名、关节原点/轴向、实际尺寸与坐标关系，一律不改。
2. **不动数据链**：`TwinRecorder` / `Ros2Bridge` / `DisassemblySystem` 的行为与字段不改；视觉改动不得进入它们的代码路径。
3. **不跑实验**：不进 Play 做实验、不调 `/dt/cmd/*`、不采集；性能采样需用户单独授权且只读。
4. **不改 `VR_EVB_DL`**（只读参考工程）。
5. **场景不进 git**（`.gitignore` 忽略 `Assets/Scenes/`）：每次里程碑必须把 `.unity` 复制到 `E:\new_2026-\unity\project\_backup\scenes\<日期>_<标签>\` 并在 `VERSION_LOG.md` 记 SHA256。
6. 编辑器菜单 `DigitalTwin/Doosan-DT/*` 全部幂等、可重复执行；`1 Build` 会重建场景，**不要在有手工改动时执行**。

## 4. 常用菜单

| 菜单 | 用途 |
|---|---|
| `DigitalTwin/Doosan-DT/2 Verify Merged Scene (read-only)` | 只读自检（对象/组件/缺失脚本/重复项）→ `Logs/doosan_dt_verify.txt` |
| `DigitalTwin/Doosan-DT/3 Resolve Script GUIDs` | 诊断"疑似缺失 GUID"（包内资产无 .meta，属正常） |
| `DigitalTwin/Doosan-DT/4 Optimize Outline (perf)` | QuickOutline 收口（只留自身带拆解组件的对象） |
| `DigitalTwin/Doosan-DT/5 Extract Archive Group` | 把 `~` / `-bkup113` 备份根拆到 `Doosan-DT_Archive.unity`（自动保留被外部引用的对象） |
| `DigitalTwin/Doosan-DT/6 Mark Environment Static` | 环境静态标记（GI/Batching/Occlusion/Reflection） |
| `DigitalTwin/Doosan-DT/7 Scene Stats` | 对象/渲染器/三角面/材质/贴图/Outline 统计 → `Logs/doosan_dt_polish.txt` |
| `DigitalTwin/Doosan-DT/8 Apply Lighting Setup` | 工业程序化天空 + 主光 + 2 补光 + ACES 后处理 + URP HDR 分级 |
| `DigitalTwin/Doosan-DT/9 Snapshot Views` | 固定三机位截图（`Logs/doosan_dt_view_*`；首次运行存 `_before`） |

### 4.1 界面菜单（2026-11-03 新增，全部幂等）

| 菜单 | 用途 |
|---|---|
| `DigitalTwin/UI/1 Build Unified Shell` | 重建 `30_界面与交互/UI_统一入口面板` 并保存场景 |
| `DigitalTwin/UI/2 Fix Missing Scripts` | 清 `登录界面`/`Gaze Interactor` 丢失脚本、补 `BatteryLoginGate`、改文案与字体 |
| `DigitalTwin/UI/3 Audit UI Wiring (read-only)` | Canvas/EventSystem/UnityEvent/空引用/字体/运行开关审计 |
| `DigitalTwin/UI/4 Apply Force Channels` | 打开 `externalTcpForce/externalJointTorque/actualJointTorque` 三个 bool 并保存 |
| `DigitalTwin/UI/5 Remove Legacy User_ui Canvas (destructive)` | 删遗留 `User_ui`（先引用扫描，0 引用才删）|
| `DigitalTwin/UI/6 Play Smoke (UI only)` | 进 Play 走 UI → 截图 → 自动退出；`6b Abort Play`、`6c Restore PerformanceMonitor` 为中断恢复 |
| `DigitalTwin/UI/7 Chart Replay Preview (edit mode)` | 编辑期灌夹具出图，不进 Play |
| `DigitalTwin/UI/8 Check UI Font Coverage (read-only)` | 文案 × TMP 静态字形集 |
| `DigitalTwin/UI/9 Snapshot UI States (edit mode)` | 5 个界面状态固定机位渲染 |

产物统一在 `Logs/ui_*.txt` 与 `Logs/ui_shots/`；细节见 `10_AGENT/UI_ARCHITECTURE.md`。

## 5. 维护规矩

- 每轮收尾：追加 `SESSION_LOG.md` → 更新 `TASKS.md` → 更新本页"当前状态" → 需要时更新 `VERSION_LOG.md`。
- 只追加不覆盖历史；旧内容进 `90_Archive~/`，文件名带日期，不建 `_v2/_final` 副本。
- 每条结论标证据状态：`VERIFIED` / `REPORTED_ONLY` / `IMPLEMENTED_UNTESTED` / `PLANNED` / `UNKNOWN`。
- 论文图登记到 `FIG_INDEX.md`：图号 → 场景 + 版本 + 机位/参数 + 证据路径。

## R2 双主面板（2026-11-03 23:00 追加）

- 场景 **v2026-11-03.7**：1,887 GameObject / 4,650,214 B / SHA256 `F795E210CEAEAD812E2DCDB99528E3FDEC85F859530A5073A7C34B2C8E1D5B37`；备份 `_backup\scenes\2026-11-03_r2_shell`
- 新根 `30_界面与交互/UI_二期面板`：常驻顶栏 + 实训任务台 + 孪生监控台（概览/曲线/控制/记录）；旧 `UI_统一入口面板` 停用保留
- 新增菜单：`UI/1 (R2)` 全量重建、`UI/10` 任务台、`UI/11` 监控台、`UI/12` 令牌审计、`UI/14` 截图、`UI/15` Play 自检、`UI/16` 字形校验
- 验收：`missingDistinct=0`、`buttons=21 minHeight=40 below40=0`、`exceptions=0 RESULT: PASS`、5 张截图互异
- 设计规范见 `10_AGENT/UI_DESIGN_SYSTEM.md`，架构补充见 `10_AGENT/UI_ARCHITECTURE.md §11`

- **2026-11-03 23:12 配色更新**：R2 面板改为蓝白（白卡 + `#3F5AF0` 主色 + 红绿黄语义色）；场景升到 v2026-11-03.8 `289B6888…18588E9F7`；评分规则见 `10_AGENT/TRAINING_SCORING.md`。

- **2026-11-03 23:15 今日收尾**：v2026-11-03.8 蓝白版已保存；明天先做 ①评分三项拍板 + 评分代码接线 ②3D 会话产出挂载（UI/14 Build TwinViz）③开机后重跑 UI/15 补真机证据。详见 `10_AGENT/SESSION_LOG.md` 顶部收尾条。

- **2026-11-04 19:5x 双线开工**：UI 完善转到网页原型（`agent_runs/dsh_2026-11-04_ui_web/`，本地 8777，不改 Unity）；E5-0 空载基线准备就绪（profile `chapter4_E5_0kg_1104.yaml` + 交接卡，待实验会话采集）。

## 2026-11-04 · R3 工业数据台（A34）
- 批次：`agent_runs/dsh_2026-11-04_ui_industrial/`（README、evidence_table.csv、changed_files.txt、13 截图、logs）
- 设计规范：`Docs/10_AGENT/UI_DESIGN_SYSTEM.md` §9；架构：`UI_ARCHITECTURE.md` §12
- 场景：`AE063339DA36B4A1BD75DCFF2155391CF3FAF63F4E15751707709D5DCE7E2669`（备份 `_backup/scenes/2026-11-04_r3_industrial`）
- 未完成：A35 Unity ROS-TCP 收帧修复（E4-B/C 阻塞）、A36 阈值标定、A37 真机字段复核

## 2026-11-04 · Play 漫游交互层（A38）
- 批次：`agent_runs/dsh_2026-11-04_ui_play/`（README、changed_files.txt、logs、shots）
- 用法：右键长按+WASD/QE 移动、滚轮调速；射线颜色/线宽/开关在相机 Inspector 的 `TwinPlayRayVisual`；按钮渐变强度在 `TwinUiSettings.hoverGradientStrength`
- 场景：`52063677A43AC0DCDDF3256226549B95A1582699F6EEAE1677CBAC6FA5346B51`（备份 `_backup/scenes/2026-11-04_play_interaction`）
- 菜单：`DigitalTwin/UI/6 Attach Play Interaction Rig`、`DigitalTwin/UI/17 Play Interaction Check`

## 2026-11-04 · 第四章写作工作区（A41，新会话入口）
- 工作区：`F:\new_2026-\zyq_ST_use\E_TJ\src\四章\写作_2026-11\`（KICKOFF、文献、claim 表、图管线、venv）
- 技能：`.agents/skills/anti-defensive-writing/`（上游 MIT 改写）、`.agents/skills/thesis-figure-pipeline/`（自建）
- 自检：`agent_runs/dsh_2026-11-04_thesis/`（56/56 PASS；分析窗口径修正，max|Δ|=0）
- 下一阶段：A42 分节正文；A43 正式重复实验仍 PLANNED

## 2026-11-05 · 拖动末端法兰 IK ghost 预览（A39）
- 批次：`agent_runs/dsh_2026-11-04_ui_play/ik/`（README、changed_files.txt、logs、6 截图）
- 用法：Play 后对准末端手柄，左键长按拖动；X/Y/Z 锁定轴、Shift 精调、Esc 取消、R 回当前；HUD 显示 `dispatch=disabled`
- 场景：`F4AF5927DE4EB51E9B1231D77FAD1F29D6334504DF700F4D8EF91E77F43D1F2C`（备份 `_backup/scenes/2026-11-04_play_ik`）
- 菜单：`DigitalTwin/UI/7 Attach IK Handle`、`DigitalTwin/UI/18 Play Ik Handle Check`
