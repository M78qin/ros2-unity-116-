# AI 接手速查（Doosan-DT / Unity_Ros_Try1）

> 目标：新会话 5 分钟内知道「工程在哪、怎么连、能改什么、别碰什么、证据在哪」。

## 1. 位置与端口

| 项 | 值 |
|---|---|
| Unity 工程 | `E:\new_2026-\unity\project\Unity_Ros_Try1`（2022.3.62f1，URP 14.0.12） |
| 主场景 | `Assets/Scenes/Doosan-DT.unity`（Build Settings 第 2 位；第 1 位是 `SampleScene`） |
| MCP 端口 | 本工程 **8091**；EVB 源工程（`VR_EVB_DL`）**8090** |
| MCP 客户端 | `node <E_TJ>\agent_runs\dsh_2026-11-03_fusion\_tools\mcp_call_port.js <method> "@<params.json>" --port=8091` |
| 写盘前必做 | `get_scene_info` 确认 `scenePath`，避免在错误端口上写坏另一个工程 |

PowerShell 传参注意：JSON 参数必须写成文件再 `@"path"` 传入（PS 5.1 会吃掉引号）。

## 2. 必守约束（用户明确要求）

1. **不跑实验、不动机械臂**：不调用 `/dt/cmd/*`，不进 Play 模式，不触发 ROS2 收发。
2. **不采集数据**：不启动 `TwinRecorder` 记录、不生成 `samples.csv`/`model_mapping.csv` 新数据。
3. **不改 `VR_EVB_DL` 源工程**（只读参考）。
4. **不覆盖既有批次记录**：`agent_runs/dsh_2026-10-29`、`dsh_2026-11-02`、`dsh_2026-11-03` 里的文件只增不改。
5. 机器人对象的名字/父级/关节名（`joint_1..joint_6`）**不能改**——记录器与 ROS 校验依赖它们。

## 3. 一分钟自检（只读）

```
菜单 DigitalTwin/Doosan-DT/2 Verify Merged Scene (read-only)
→ <project>/Logs/doosan_dt_verify.txt
```
健康基线（2026-11-03 16:46）：
- `roots=8  gameObjects=5434`
- 激活相机 1 台、AudioListener 1 个、EventSystem 1 个
- `DisassemblySystem active=1`、`LayerGroupConfig active=3`、`DigitalTwinRuntime active=1`、`Ros2Bridge active=1`、`TwinRecorder active=2`
- `missingScriptInstances=3`（且路径就是 SCENE_GUIDE §7 那三个）
- `~_归档停用 activeSelf=False`

其它只读工具：
- `DigitalTwin/Fusion/1 Audit Migrated EVB Assets`（预制体缺失脚本/材质 shader）
- `DigitalTwin/Fusion/2 Audit Current Scene`（场景对象数/机制计数）
- `DigitalTwin/Fusion/4 Report Root Bounds`（各根包围盒）
- `DigitalTwin/Fusion/9 Snapshot Target -> PNG`（编辑模式截图，不播放）
- `DigitalTwin/Doosan-DT/3 Resolve Script GUIDs`（"缺脚本"疑似时先跑它：包内 GUID 不是缺失）

## 4. 当前状态（2026-11-03 17:00）

- `Doosan-DT.unity` 已建好并自检通过，**未进 Play 验证**（约束所限）。
- 资产已按功能重组进 `Assets/DoosanDT/`，旧脚本/杂项在 `Assets/DoosanDT/90_Archive~`（Unity 忽略）。
- 三个历史场景保留未动：`SampleScene`、`EVB_Chapter4`、`MainScene`。
- 编辑器当前停在 `Doosan-DT`（已保存，dirty=False）。

## 5. 待办（下一会话可做）

1. **Play 模式联调**（需用户授权）：验证拆解射线、Layer 顺序、UI 按钮开合、Timeline 爆炸/复原。
2. **UI 制作**：训练/漫游面板的按钮与视频播放（`Canvas_1_V/Video_play1` 已有 VideoPlayer）。
3. `20_漫游与训练/Dis-Training_M` 默认关闭是原设计，若要在 Doosan-DT 里直接演示，需确认由哪个脚本/按钮打开。
4. 归档组瘦身：确认 `~_归档停用` 里 3,963 对象无用 → 删除（先备份场景）。
5. 继承缺陷修复（需授权改 EVB 源码）：GBK 转 UTF-8、`BatteryLoginUIBuilder` 加宏守卫、QuickOutline URP 适配。
6. 布局：把 `00_数字孪生机械臂` 移进实验室岛（或反之），只改组的世界坐标。

## 6. 证据与记录在哪

| 内容 | 路径 |
|---|---|
| 本批批次目录 | `<E_TJ>\agent_runs\dsh_2026-11-03_fusion\`（README 有 §1–§9 全过程） |
| 层级清单（EVB MainScene） | `_reports\EVB_MainScene_层级清单.md` |
| 项目级同步记录 | `_reports\项目同步_拆解场景与依赖.md` |
| 合并场景说明（本工程内） | `Assets\DoosanDT\00_Docs\SCENE_GUIDE.md` |
| 构建/自检/GUID 探针日志 | `<project>\Logs\doosan_dt_build.txt`、`doosan_dt_verify.txt`、`doosan_dt_guids.txt` |
| 资产搬迁清单 | `_evidence\assets_reorg_*.json`（含每个目录的 GUID 与原/新路径） |
| 迁移哈希清单 | `_evidence\migrated_files_manifest_20261103.csv` |
| 进度记录 | `<E_TJ>\src\进度记录\2026-11-03_Unity融合落地.md` |
| 审计导航 | `<E_TJ>\src\_index\Codex审计导航.md` |

## 7. 常见坑

| 现象 | 原因 | 处理 |
|---|---|---|
| 菜单执行失败 "no menu named …" | 脚本还没编译完 / 菜单名记错（带后缀） | 先 `Assets/Refresh`，等 20–30 s；菜单全名见 PROJECT_MAP §4 |
| 文本里搜不到某个 GUID | 该资产在 `Packages/` 内（包内资产无 `.meta`，GUID 由路径生成） | 用 `Doosan-DT/3 Resolve Script GUIDs` 在 Unity 内验证 |
| 场景里相机/EventSystem 好像重复 | 保留了两套（一套停用）以便随时切换 | 看 `50_玩家与相机` 下哪个 `Main Camera` 是 `[A]`；`30_界面与交互` 里 `EventSystem (DT_停用)` 不要启用 |
| 拆解零件 Inspector 里没有 `DisassemblyPart` | `LayerGroupConfig.autoCreateParts` 运行时补挂 | 正常，不要手工加 |
| 视频/训练面板"不见了" | 对应 Canvas 默认关闭，运行时由脚本打开 | 正常 |
| 移动资产后场景引用断了 | 只移了资产没移 `.meta` | 必须连 `.meta` 一起移（GUID 不变） |