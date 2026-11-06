# Doosan-DT.unity 场景说明（层级 / 状态 / 怎么用）

生成时间：2026-11-03 16:38　重建方式：`DigitalTwin/Doosan-DT/1 Build Merged Scene`（幂等）
自检报告：`<project>/Logs/doosan_dt_verify.txt`　构建日志：`<project>/Logs/doosan_dt_build.txt`

## 1. 总规模

| 指标 | 值 |
|---|---|
| 根对象（顶层组） | 8 |
| GameObject / Transform | **5,434** |
| 激活对象（含子级） | 1,471 |
| 归档组内对象 | 3,963（整组 `SetActive(false)`） |
| 相机 / AudioListener / EventSystem | 3（激活 **1**） / 3（激活 **1**） / 2（激活 **1**） |
| 缺失脚本 | **3**（源工程即缺，见 §7） |
| 场景体量 | 11.9 MB |

## 2. 8 个顶层组（按你的功能划分）

```
Doosan-DT.unity
├── 00_数字孪生机械臂        519 对象 (激活 135)   ← 真实机械臂 + ROS2 + 记录
├── 10_电池包拆解系统         99 对象 (激活  99)   ← 拆解 rig
├── 20_漫游与训练            391 对象 (激活  98)   ← 漫游/训练/Timeline/爆炸演示
├── 30_界面与交互             86 对象 (激活   5)   ← 所有 Canvas + EventSystem + XR 交互管理
├── 40_环境与灯光            307 对象 (激活 145)   ← 实验室场景 + 静态机器人 + 灯光
├── 50_玩家与相机             67 对象 (激活   3)   ← 桌面玩家(相机+移动) / VR rig(备用)
├── 90_全局设置                2 对象              ← URP Global Volume
└── ~_归档停用             3,963 对象 (整组停用)   ← 备份/历史副本，见 §6
```

### 00_数字孪生机械臂
`4.24-Try`（世界坐标 `(2.67, -0.44, -2.30)`）内含：
- `ROS2`（`Ros2Bridge`）— Unity↔ROS2 端点
- `DigitalTwin_System`（`DigitalTwinRuntime`）— 主控
- `DART`（`DartStudioBridge`）
- `h2515-1 (4)` **← 主用模型**（`UrdfRobot` 结构：base_link → link_1..6，`UrdfJointRevolute`×?，关节名必须是 `joint_1..joint_6`）
- `H2516-Ghost`、`h2515-1 (5)`、`h2515-1 (6)`：备用/幽灵模型（各自 `m_IsActive` 原始状态保留，`(6)` 在别处 `(0.50,0.78,0.98)`）
- 另两个根：`Ros2Experimen`（`Ros2ExperimentPanel` 实验面板）、`DataLogging_System`（`TwinRecorder` 记录器）

### 10_电池包拆解系统
`VR拆解`（世界 `(-7.44, 0.75, -12.54)`）内含：
- `Dis_1`（`DisassemblySystem`，**全场景唯一激活**的拆解系统）
  - `Lay1`（`LayerGroupConfig`）→ `1-2` + `pri1/screws`（**72 颗螺钉**，`DisassemblyPart`）
  - `Lay2`（`LayerGroupConfig`）→ `pri1/pri2` 零件（纯网格）
  - `Lay3`（`LayerGroupConfig`）→ `3-1_casing_bottom` + `pri1/3-1_batter_module1` + `3-2_battery_module2`
- `PerformanceMonitor`（`PerformanceProfiler`，0.5 s 区间均值写 CSV）

> 只有 5 个零件显式挂了 `DisassemblyPart`，其余由 `LayerGroupConfig.autoCreateParts` 在运行时自动补挂——**不要手工加组件**。
> 包围盒中心 `(-6.03, 1.30, -13.08)`，尺寸 `1.10 × 0.27 × 0.80`，离地约 1.17 m（在放置台上）。

### 20_漫游与训练
- `FunctionModule`
  - `Dis-Training_M`（默认 `m_IsActive=0`）：`EVB (1)`（`ExplosionControllerXR`）、`放置台 _2`、`TimeLine_control`（PlayableDirector 训练时间轴）
  - `Roaming_M`：`爆炸用`（PlayableDirector 爆炸）/ `复原用`（关闭）
- `爆炸用 (1)`：整组爆炸演示实例（`(-6.01, 0.50, -13.04)`，含 `EVB` 电池总装配体）
- 包围盒 `2.14 × 0.93 × 6.37`，中心 `(-6.46, 0.97, -10.36)`

### 30_界面与交互
- `Canvas`（世界 `(-4.25, 1.60, -7.69)`）下 5 个子 Canvas，**按设计默认关闭**，由脚本在运行时按功能打开：
  - `Canvas_I`（主界面：`Panel` + `Text (TMP)`）
  - `Canvas_1 P`（`Panel_1`/`Panel_2`）
  - `Canvas_1_V`（视频：`Video_play1`，`VideoPlayer` 在此）
  - `User_ui`（`Button`，`TC` 脚本）
  - `Canvas_I软著用标题`（`LoginPanel`）
  - `登录界面`（缺失脚本，见 §7）
- `EventSystem`（激活，唯一）
- `XR Interaction Manager`（XRI 交互管理器，唯一）
- `EventSystem (DT_停用)`：来自孪生场景的重复事件系统，已停用（Unity 只允许一个 EventSystem）

### 40_环境与灯光
- `EVB_DS `（名字尾部有空格）：实验室布景
  - `Doosan_Robot`（静态机器人，**仅视觉，不带控制脚本**）
  - `Doosan_Robot_a_Tool`（夹爪/工具装配，`(-7.95, 0.76, -10.11)`）
  - `Lab`（实验台、`floor`、`TeleportationArea`、装饰灯、校徽 Area Light）
  - 3 个面板 Canvas：`Doosan_Robot/panel2_R`、`Doosan_Robot_a_Tool/panel2_R (1)`、`Lab/panel1`（各自带 `p1`/`v1` 子 Canvas）
- `Directional Light`（激活，EVB 原场景的）+ 另有一盏孪生场景的平行光**已停用**（避免双主光过曝）
- 包围盒 `10.00 × 4.05 × 15.01`，中心 `(-5.04, 2.03, -7.58)`，地面 y≈0

### 50_玩家与相机
- `player`（世界 `(-4.83, 2.65, -10.76)`）
  - `Main Camera`（**唯一激活相机**，tag `MainCamera`，`CameraController` + `SmartRaycaster` + `AudioListener`）← 桌面拆解射线入口
  - `XR Origin (XR Rig)`（默认关闭，含 `Locomotion`/`Camera Offset`/左右手柄，`Gaze Interactor` 脚本缺失）
- `Main Camera`（孪生场景相机，`(3.66,1.49,-0.41)`，已停用；代码不依赖它，随时可切回）

### 90_全局设置
- `Global Volume`（URP 后处理）

## 3. 布局：两个"岛"

| 岛 | 范围 | 内容 |
|---|---|---|
| 实验室岛 | x≈[-10, 0]，z≈[-15, 0] | 40 环境 + 10 拆解 + 20 训练 + 50 玩家 + 30 UI |
| 孪生岛 | x≈[0.1, 2.8]，z≈[-2.4, 1.1] | 00 机械臂（紧贴实验室 +x 侧外墙） |

两岛地面都在 y≈0，走过去约 10 m。若你希望把机械臂搬进实验室（或把拆解台搬过去），只改 `00_/10_` 组的世界坐标即可，组内相对关系不受影响。

## 4. 相机与交互入口

- **桌面模式（默认，可直接用）**：`player/Main Camera`（激活）+ `SmartRaycaster`（鼠标左键射线）+ `PlayerController`（WASD，挂在 `player` 上）。
- **VR 模式（备用）**：把 `player/Main Camera` 与 `player/XR Origin (XR Rig)` 的激活状态对调；相机脚本不需要改。
- 孪生场景的相机 `50_玩家与相机/Main Camera` 默认停用；**它和代码无耦合**（运行时脚本不用 `Camera.main`，已核实），需要它的视角时直接打开即可。

## 5. 与其它三个场景的关系

| 场景 | 作用 | 状态 |
|---|---|---|
| `Doosan-DT.unity` | **主场景**（本次合并） | 已在 Build Settings 第 2 位 |
| `SampleScene.unity` | 第四章原始场景 | 未改动，MD5 基线 `A2B60A4B…` |
| `EVB_Chapter4.unity` | 第一阶段融合（SampleScene + 电池模型） | 保留，未改动 |
| `MainScene.unity` | EVB 原生拆解场景 | 保留，作参考/回退 |

## 6. `~_归档停用` 里是什么（3,963 对象，整组停用）

| 子项 | 对象数 | 来源 | 原用途 |
|---|---|---|---|
| `测试2_自动` | 1,277 | EVB | 自动流程历史副本（最大死重） |
| `~` | 907 | 孪生 | 旧 `~` 备份根（纯模型 h2515 系列、ROS_Manager_backup） |
| `4.24-Try _bk519` | 515 | 孪生 | 机器人备份件 |
| `4.24-Try (1)` | 387 | 孪生 | 机器人备份件 |
| `VR拆解_多零件` | 362 | EVB | 拆解方案 C |
| `VR拆解_传统设置_Test` | 357 | EVB | 拆解方案 B |
| `H2515_DigitalTwinRuntimeCanvas` | 147 | 孪生 | 旧运行时 HUD Canvas |
| `dartstudio_E` | 4 | 孪生 | 空壳 |
| `GameObject` | 3 | EVB | 空占位 |
| `UI_System` / `Planning_Control_System` / `其他` | 1+1+1 | 孪生/EVB | 空壳 |

> 归档组整体 `SetActive(false)`：不渲染、不执行、不参与 `FindObjectOfType`，但**仍占场景文件体积与加载时间**。确认不再需要后，可在 Hierarchy 里右键删除（建议先备份场景文件）。

## 7. 三个真缺失脚本（源工程即缺）

| 路径 | GUID | 影响 |
|---|---|---|
| `30_界面与交互/Canvas/登录界面` | `cc8bcb5c…` | 登录 UI 无逻辑（EVB 无登录门禁） |
| `50_玩家与相机/player/XR Origin (XR Rig)/Camera Offset/Gaze Interactor` | `2f9cae76…` | VR 眼动注视交互 |
| `~_归档停用/测试2_自动/自动脚本` | `57fed000…` | 归档内容，无影响 |

## 8. 已知继承缺陷（不是本次合并造成）

1. 6 个 EVB 脚本是 **GBK 编码**（`ExplosionControllerXR`/`HighResScreenshot`/`PlayerController`/`TC`/`TimerController`/`VideoController`），运行时中文字符串乱码。
2. `ExplodablePartXR.cs` 含 138 个 U+FFFD（源文件已损坏）。
3. `BatteryLoginUIBuilder.cs` 缺 `#if UNITY_EDITOR` 守卫 → Player 构建会报 CS0246。
4. QuickOutline 着色器未打 URP tag（内联管线写法），URP 下描边效果未验证。
5. `20_漫游与训练` 里 `Dis-Training_M` 默认关闭、`30_界面与交互` 里大部分 Canvas 默认关闭——**这是原设计（运行时由脚本按功能打开）**，不是整理导致的丢失。