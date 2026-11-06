> 镜像副本：源头 `Assets/DoosanDT/00_Docs/PROJECT_MAP.md`（在 Unity 工程内便于随工程走）；改动请改源头再同步到本文件。

# Doosan-DT 工程目录地图（AI / 人 接手先读这个）

更新：2026-11-03 16:50　工程：`E:\new_2026-\unity\project\Unity_Ros_Try1`（Unity 2022.3.62f1，URP 14.0.12）

## 1. 一句话

这是「第四章数字孪生机械臂 + EVB 电池包拆解实训」的**统一工程**：场景 `Doosan-DT.unity` 把两套内容按功能分成 8 个顶层组；工程资产按功能收进 `Assets/DoosanDT/` 若干编号目录；旧/停用内容收进 `90_Archive~`（Unity 不导入、不编译）。

## 2. 目录结构（只列本工程自有资产）

```
Assets/
├── DoosanDT/                          ← 本工程自有资产（全部在这里）
│   ├── 00_Docs/                       ← 文档：PROJECT_MAP / SCENE_GUIDE / AI_SYNC
│   ├── 10_DigitalTwin/                ← 第四章数字孪生
│   │   ├── Scripts/                   ← 运行时代码（含 GameCameraRayController.cs）
│   │   ├── Editor/                    ← 编辑器工具（审计/截图/建场景/自检）
│   │   ├── Art/ Config/               ← 美术与配置
│   │   └── RosMessages/               ← ROS2 消息 C# 类型
│   ├── 20_BatteryDisassembly/         ← EVB 电池包拆解（从 VR_EVB_DL 迁入）
│   │   ├── Assets/    ← 模型/材质/预制体/贴图（原 EVB_DS）
│   │   ├── Scripts/   ← 拆解逻辑（原 EVB_Scripts：DB/dis/other）
│   │   ├── Fonts/ Shaders/ Animation/ Editor/ Others/
│   ├── 30_RobotModels/                ← 机器人模型资源（Doosan_H2515/、h2515.prefab）
│   └── 90_Archive~/                   ← 归档（Unity 忽略：以 ~ 结尾）
│       ├── OldScripts/Not_USE/        ← 旧脚本（H2515RobotController/JointTester）
│       ├── OldScripts/EX-UES/
│       └── .obsidian/ EmptyLogs/
├── Scenes/                            ← 场景（保留在这里：Unity 约定 + Build Settings）
│   ├── Doosan-DT.unity                ← 【主场景】本次合并产物
│   ├── SampleScene.unity              ← 第四章原始场景（未改动，MD5 基线）
│   ├── EVB_Chapter4.unity             ← 第一阶段融合场景（SampleScene + EVB 电池模型）
│   └── MainScene.unity                ← EVB 原生拆解场景（2943 对象，参考/回退用）
├── Resources/                         ← 保留（ROSConnectionPrefab、GeometryCompassSettings）
├── Settings/ XR/ XRI/ Samples/ TextMesh Pro/   ← 保留（URP/XR 包模板与默认资源，不要搬）
└── UniversalRenderPipelineGlobalSettings.asset ← 保留在根（URP 约定）
```

**规则**

1. 新写的东西 → 放进 `Assets/DoosanDT/` 对应编号目录；不要往工程根散放。
2. `Assets/Settings|XR|XRI|Samples|TextMesh Pro|Resources` 是包/模板/约定目录，**不要搬动**（搬了会触发重导入与引用风险）。
3. 停用/过期内容 → 移进 `90_Archive~`（**目录名必须以 `~` 结尾**，Unity 才会忽略它；它不会出现在 Project 窗口，也不会被编译）。
4. 移动资产必须**连 `.meta` 一起移**（GUID 不变 → 场景/预制体引用不断）。已验证：本次搬迁后场景自检缺失脚本数不变。
5. 机器人侧对象**名字/父级/关节名不能改**（`model_mapping.csv` 的 `body_path` 与 `joint_1..6` 依赖它）。

## 3. 依赖包（关键）

| 包 | 版本 | 用途 |
|---|---|---|
| `com.unity.robotics.ros-tcp-connector` | 见 manifest | Unity ↔ ROS2 TCP |
| `com.unity.robotics.urdf-importer` | 见 manifest | 机器人 URDF 组件（`UrdfJoint*`/`UrdfLink`…）；**包内资产无 .meta，GUID 由包路径确定性生成**——排查「疑似缺失 GUID」时先想到它 |
| `com.unity.xr.interaction.toolkit` | 3.4.0 | XRI 交互（EVB 场景用） |
| `com.unity.render-pipelines.universal` | 14.0.12 | URP |
| `com.unity.inputsystem` | — | 新输入（`activeInputHandler=2` Both） |
| `com.unity.textmeshpro` | 3.0.9 | TMP |
| `mcp-unity` (file:) | 1.3.0 | 编辑器 MCP，端口 **8091**（EVB 源工程是 8090） |

## 4. 编辑器工具菜单（`DigitalTwin/`）

| 菜单 | 作用 | 写盘 |
|---|---|---|
| `Doosan-DT/1 Build Merged Scene` | 重建 `Doosan-DT.unity`（幂等：先删再建） | 场景 + BuildSettings |
| `Doosan-DT/2 Verify Merged Scene` | 只读自检：分组对象数/包围盒/重复相机与事件系统/单例计数/缺失脚本/tag/layer | `Logs/doosan_dt_verify.txt` |
| `Doosan-DT/3 Resolve Script GUIDs` | 诊断：场景内每个 `m_Script` GUID → 资产路径 | `Logs/doosan_dt_guids.txt` |
| `Fusion/1 Audit Migrated EVB Assets` | 只读：迁移来的预制体缺失脚本/材质 shader | `Logs/evb_fusion_audit.txt` |
| `Fusion/2 Audit Current Scene` | 只读：当前场景对象数/缺失脚本/第四章机制计数 | 同上 |
| `Fusion/4 Report Root Bounds` | 只读：各根包围盒（判断是否重叠/悬空） | 同上 |
| `Fusion/9 Snapshot Target -> PNG` | 编辑模式截图（不播放、不采集） | `Logs/fusion_snapshot_*.png` |

> 约定：这些工具**只读或幂等**，不进入 Play、不启动 ROS、不采集数据。

## 5. 排查「缺失脚本」的正确姿势

Unity 里看到 `Missing (Mono Script)` 时：
1. 先跑 `Doosan-DT/2 Verify Merged Scene` 拿到数量与路径。
2. 再跑 `Doosan-DT/3 Resolve Script GUIDs`：若 GUID 指向 `Packages/...`，说明是包内脚本，**不是缺失**（包内资产没有 .meta 文件，用文本搜索找不到，属正常）。
3. 目前工程内已知真缺失 **3 个**：`登录界面`、`测试2_自动/自动脚本`、`Gaze Interactor`（EVB 源工程本来就没有，无源码可恢复）。

## 6. 变更历史（本工程）

| 时间 | 变更 |
|---|---|
| 2026-11-03 14:30 | EVB 资产迁入（270 文件，7 目录，MD5 全等） |
| 2026-11-03 15:19 | 建 `EVB_Chapter4.unity`（SampleScene + `EVB_Fusion` 电池模型） |
| 2026-11-03 16:20 | 同步 EVB `MainScene.unity`（2943 对象） |
| 2026-11-03 16:38 | 建 `Doosan-DT.unity`（8 功能组，5,434 对象） |
| 2026-11-03 16:46 | 资产按功能重组进 `Assets/DoosanDT/`，旧内容归档到 `90_Archive~` |
