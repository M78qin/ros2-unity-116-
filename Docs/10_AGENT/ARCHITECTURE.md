# 代码与场景架构（改代码前先看这一页）

## 1. 目录职责

```
Unity_Ros_Try1/
├─ Assets/
│  ├─ Scenes/                 Doosan-DT.unity（主场景，不进 git）
│  │                          Doosan-DT_Archive.unity（备份树，不进 git）
│  │                          SampleScene / MainScene / EVB_Chapter4（历史，勿动）
│  ├─ DoosanDT/
│  │  ├─ 00_Docs/            工程内说明（PROJECT_MAP / SCENE_GUIDE / AI_SYNC）
│  │  ├─ 10_DigitalTwin/     数字孪生核心：Scripts 18 个模块 + Editor 工具 + Art 资产
│  │  ├─ 20_BatteryDisassembly/ 拆解系统脚本与模型（EVB 迁移来）
│  │  ├─ 30_RobotModels/     Doosan H2515 URDF/DAE
│  │  └─ 90_Archive~/        Unity 忽略（`~` 后缀），旧脚本/旧预制体
│  ├─ Settings/              URP-HighFidelity.asset（全工程共用）
│  └─ Resources, XR, XRI, TextMesh Pro, Samples  包/框架资产
├─ Docs/                     ← 人+AI 记录（本文所在，Unity 不导入）
└─ Logs/                     菜单工具输出（verify / polish / guids / 截图）
```

## 2. 命名空间与分层

| 命名空间 | 位置 | 职责 | 允许依赖 |
|---|---|---|---|
| `DigitalTwin` | `10_DigitalTwin/Scripts/**` | 运行时：双端同步、录制、漫游、UI、机器人绑定 | UnityEngine、ROS-TCP-Connector |
| `DigitalTwin.EditorTools` | `10_DigitalTwin/Editor/**` | 编辑器：建场景、自检、整理、截图 | UnityEditor、`DigitalTwin` |
| 全局（无命名空间） | `20_BatteryDisassembly/**` | EVB 既有：`DisassemblySystem`、`LayerGroupConfig`、`DisassemblyPart`、`ExplodablePartXR`、`Outline`、`SmartRaycaster`、`PerformanceProfiler` | UnityEngine |

**红线**：`DigitalTwin` 不许引用 `DigitalTwin.EditorTools`；编辑器脚本必须放 `Editor/` 且用 `#if UNITY_EDITOR` 保护（历史遗留 `BatteryLoginUIBuilder.cs` 缺保护，未修）。EVB 全局类名与 UI 类冲突（`Outline`），跨包引用要写全名 `UnityEngine.UI.Outline` / `QuickOutline.Outline`。

## 3. 场景层级契约（`Doosan-DT.unity`）

```
00_数字孪生机械臂     机器人 + 电池包（本轮用户手工放一起）；数字孪生主体
20_漫游与训练         传送/漫游点、训练步骤对象
30_界面与交互         Canvas / EventSystem / 射线交互
40_环境与灯光         实验室预制体（EVB_DS）、主光、天空相关
50_玩家与相机         XR Origin、Main Camera
90_全局设置           Global Volume、Fill_Light_A/B
~_外部引用保留        停用；只放被活脚本引用、不能随备份搬走的对象（当前=UI_System）
```

规则：
- **层级名是契约**：菜单工具按根名查找（`40_环境与灯光`、`90_全局设置` 等），改名要同步改 `DoosanDTPolish.cs` 常量。
- 备份/停用组一律命名 `~` 开头或 `-bkup<日期>` 结尾，且 `activeSelf=false`，`5 Extract Archive Group` 靠命名识别。
- 进 `40_环境与灯光` 的静态件必须打 StaticEditorFlags，否则后续烘焙不参与。

## 4. 关键运行时组件（不许乱动）

| 组件 | 作用 | 改动约束 |
|---|---|---|
| `Ros2Bridge` | 与 ROS2 的 TCP 连接、话题收发 | 字段/话题名/频率都是实验口径，改则 E1–E5 全部重测 |
| `TwinRecorder` | 记录双端时间戳与位姿到 CSV | 采样与写盘队列直接影响丢包统计，不动 |
| `DisassemblySystem` | 拆解流程状态机 | 拆解顺序是论文第 3 章结论，不动逻辑 |
| `PerformanceProfiler` | 帧时间采样 | 只读采样，可复用；不得改成自动启动 |
| `SmartRaycaster` | XR 射线拾取 | 与 XRI 3.4 版本耦合 |
| `Outline`(QuickOutline) | 高亮 | 只做视觉，已收口到 5 个启用 |

## 5. 编辑器工具（`10_DigitalTwin/Editor/`）

| 文件 | 菜单 | 幂等性 |
|---|---|---|
| `DoosanDTSceneBuilder.cs` | `1 Build Merged Scene` | ❌ 会重建场景，**有手工改动时禁止执行** |
| `DoosanDTSceneVerifier.cs` | `2 Verify` / `3 Resolve GUIDs` / `9 Snapshot Each Group` | ✅ 只读（9 只截图） |
| `DoosanDTPolish.cs` | `4 Outline` / `5 Extract Archive` / `6 Static` / `7 Stats` / `8 Lighting` / `9 Snapshot Views` | ✅ 全部幂等 |
| `EVBFusionAudit.cs` | `Fusion/1–4` | ✅ 只读审计 |
| `EVBFusionSnapshot.cs` | `Fusion/9` | ✅ 只截图 |

**硬约束**：编辑器工具里**禁止** `EditorUtility.DisplayDialog` —— 会阻塞主线程导致 MCP 超时（2026-11-03 已踩坑）。统一用 `DoosanDTPolish.Dialog()` 只写日志。

## 6. 工程级设置（改前记录原值）

| 项 | 当前值 | 位置 |
|---|---|---|
| 渲染管线 | URP 14.0.12，`Assets/Settings/URP-HighFidelity.asset` | Graphics/Quality |
| Color Grading Mode | **HDR(1)**（2026-11-03 由 LDR 改） | URP asset `m_ColorGradingMode` |
| HDR / MSAA | 开 / 4x | URP asset |
| 主光阴影 | 4096、4 cascade、Soft | URP asset |
| 质量档 | `High Fidelity`（pixelLightCount 2、shadowDistance 40） | ProjectSettings/Quality |
| 后处理 | 场景 Global Volume → `Art/Config/DoosanDT_VisualProfile.asset`（ACES） | 场景 `90_全局设置` |
| 天空 | `Art/Materials/DoosanDT_IndustrialSky.mat`（Skybox/Procedural） | RenderSettings |

## 7. 已知历史缺陷（未修，改动前心里有数）

1. 6 个 EVB 脚本为 GBK 编码（Unity 可读，但 diff/检索乱码）。
2. `ExplodablePartXR.cs` 含 138 个 U+FFFD 替换字符（历史转码损坏），逻辑未验证。
3. `BatteryLoginUIBuilder.cs` 在 Assets 下却直接用 `UnityEditor`，缺 `#if UNITY_EDITOR`（打包会挂）。
4. QuickOutline 的 shader 缺 URP 标签，`Outline` 在 URP 下渲染依赖 fallback。
5. 2 个真实缺失脚本：`30_界面与交互/Canvas/登录界面`、`50_玩家与相机/.../Gaze Interactor`（slot 4）。
6. `urdf-importer` 包资产无 `.meta`，GUID 是 Unity 现编的 —— 搜索"缺失 GUID"出现的 12 条是**误报**，不要修。

## 8. UI 层（v2026-11-03.5 新增，细节见 `UI_ARCHITECTURE.md`）

| 目录 | 内容 | 红线 |
|---|---|---|
| `Scripts/UI/Shell/` | 入口门、4 模式路由、面板控制器、文案表、双向控制呈现层 | 只切 `activeSelf`，不改旧 Canvas 渲染模式/缩放/父级；不新增指令下发路径 |
| `Scripts/UI/Chart/` | 手写折线图、环形缓冲、通道目录、数据绑定、CSV 回放探针 | 缺失数据必须显示"未订阅/无数据源"，**不得用 0 填充** |
| `Scripts/UI/SelfTest/` | Play 冒烟驱动（只读夹具、截图、自动退出） | 不连 ROS2、不写 CSV、不进实验台账 |
| `Editor/Twin*.cs` | 构建/修复/审计/字体/通道/截图/冒烟工具 | 幂等、不弹 `DisplayDialog`、只写 `Logs/` |

数据来源：`DigitalTwinRuntime.TryGetLatestFrame()`（`RobotStateFrame`）或回放探针；**不新增状态总线字段**。
`TwinRuntimeProfile.enableRuntimeUI` 保持 0（旧 HUD 仍禁用），新面板是场景对象、不经该开关。`VERIFIED`