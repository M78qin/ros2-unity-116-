# 场景版本登记（论文图必须能对回某一版）

规则：每次里程碑把 `Assets/Scenes/Doosan-DT.unity` + 同名 `.meta` 复制到
`E:\new_2026-\unity\project\_backup\scenes\<日期>_<标签>\`，此处登记 SHA256；**场景不依赖 git**（`Assets/Scenes/` 被 `.gitignore` 忽略）。

| 版本 | 时间 | 标签 | 场景文件 SHA256 | 备份位置 | 内容摘要 |
|---|---|---|---|---|---|
| v2026-11-03.3 | 17:09 | 融合版（Round 3 建完、**用户手工调整前**） | `6001F4C9DABF24AF67D099BB8D2BEDE1D1DE8B5C8C3DCEE236FA67C5ACFF4EAB` | `_backup\scenes\2026-11-03_pre_visual\` | 8 组整理完成、模型已归位。**不含**用户 17:09 之后的手工改动（用户改了组名、加了 `-bkup113` 备份组、把电池包放进了机械臂组，并把归档根命名为 `~`），因此**不能**用它回退到 20:07 现场 —— 要回到 20:07 现场请用 v2026-11-03.4 的主场景 + 归档场景，把 `Doosan-DT_Archive.unity` 的根拖回主场景 |
| v2026-11-03.3u | 20:07 | 融合版（用户手工调整后，**未单独备份**） | 无（`UNKNOWN`：该状态未落盘备份） | — | 9 根 / 6,154 对象 / 12.17 MB；含 `~`(3,963) + 2 个 `-bkup113` 组。此状态的对象全部保留在 `Doosan-DT_Archive.unity` 中，可通过合并还原 |
| **v2026-11-03.4** | 20:19 | 视觉减负版（当前） | `FD931603A882E919B3078FC59AB3EEE9334DA6EDC305B4D9F26A65EF324321A9` | `_backup\scenes\2026-11-03_post_visual\` | 7 根 / 1,476 对象 / 3.26 MB；备份树拆到 `Doosan-DT_Archive.unity`；Outline 5 启用；天空+主光+补光+ACES；URP 改 HDR 分级 |

| **v2026-11-03.5** | 21:55 | 界面收口版（当前） | `7F5638BD3A0A23826F16FBC8C5E8261500A21C2BF9E6255C067B5F28F053A572` | `_backup\scenes\2026-11-03_ui_shell\` | 1,521 对象 / 3.59 MB；新增 `30_界面与交互/UI_统一入口面板`（4 页签 + 图表卡 + 控制卡 + 2 个回放探针）、`登录界面/Gaze Interactor` 丢失脚本清零、`Ros2ExperimentPanel` 3 个 channel bool 置 true。**场景状态仅供 UI 图使用**，未跑实验 |

| **v2026-11-03.6** | 22:19 | UI 版 + 删 `User_ui`（当前） | `9C604D15E8CF5A3F6F47582007DAC71D7DD38F70A11A0AD6405FC5C819A50D50` | `_backup\scenes\2026-11-03_ui_userui_removed\` | 1,519 对象 / 3,581,127 B；删除遗留 `30_界面与交互/Canvas/User_ui`（0 外部引用）；审计 11 Canvas / 34 Button / 39 调用 / 0 断链、缺失脚本 0。UI 图请用本版 |

## 关联文件哈希

| 文件 | SHA256 | 说明 |
|---|---|---|
| `Assets/Scenes/Doosan-DT_Archive.unity`（v2026-11-03.4 时） | `C143225EEA51BA379CB014BB6DE7ABA91BA6035CF8FB5E410C0F365C0BBDE812` | 9.91 MB；承接 `~`(3,962) + `10_电池包拆解系统-bkup113`(100) + `00_数字孪生机械臂-bkup113`(619) |

## 项目级资产（随 git 追踪，无需另存，但要记改了什么）

| 资产 | 本版状态 |
|---|---|
| `Assets/DoosanDT/10_DigitalTwin/Art/Materials/DoosanDT_IndustrialSky.mat` | 新增（程序化工业天空） |
| `Assets/DoosanDT/10_DigitalTwin/Art/Config/DoosanDT_VisualProfile.asset` | 新增（ACES/Bloom/对比/暗角） |
| `Assets/Settings/URP-HighFidelity.asset` | **修改**：`m_ColorGradingMode` 0 → 1 |
| `Assets/DoosanDT/10_DigitalTwin/Editor/DoosanDTPolish.cs` | 新增（6 个菜单：Outline/Extract/Static/Stats/Lighting/Snapshot） |
| `Assets/DoosanDT/10_DigitalTwin/Editor/DoosanDTSceneVerifier.cs` | 修改（新增 `9 Snapshot Each Group`） |

## v2026-11-03.5 新增/修改的资产（界面批次）

| 资产 | 状态 | 说明 |
|---|---|---|
| `Scripts/UI/Shell/BatteryLoginGate.cs` | 新增 | 入口页门（非登录）：进入/退出、隐藏账号口令 |
| `Scripts/UI/Shell/TwinDemoModeRouter.cs` | 新增 | 4 模式路由，只切 `activeSelf`，`SetActive` 带 try/catch |
| `Scripts/UI/Shell/TwinUIShellController.cs` | 新增 | 页签/标题/状态轮询（2 Hz） |
| `Scripts/UI/Shell/TwinUIShellText.cs` | 新增 | 全部 UI 文案唯一来源（受静态字形集约束） |
| `Scripts/UI/Shell/TwinCommandPanelView.cs` | 新增 | 双向控制呈现层（干运行护栏，不下发真机） |
| `Scripts/UI/Chart/TwinTelemetryChannel.cs` `...RingBuffer.cs` `...Chart.cs` `...ChartBinder.cs` `...ReplayProbe.cs` | 新增 | 手写折线图与回放自检（无第三方图表库） |
| `Scripts/UI/SelfTest/TwinUIPlaySmokeDriver.cs` | 新增 | Play 冒烟驱动（只读夹具、截图、退出） |
| `Editor/TwinUIWiringAudit.cs` `TwinUIFontCheck.cs` `TwinUIFixMissingScripts.cs` `TwinUIShellBuilder.cs` `TwinUISnapshot.cs` `TwinUIApplyForceChannels.cs` `TwinUIPlaySmoke.cs` `TwinSceneLookup.cs` | 新增 | 审计/构建/截图/通道/冒烟工具（全部幂等，不弹对话框） |
| `Logs/ui_selftest_motion.csv` | 新增 | **真实**录制会话逐字节拷贝（869 行，`sha256=026926D9…462B`）|
| `Logs/ui_selftest_force.csv` | 新增 | **合成**夹具（600 行，只用于证明图表能画，见 `Logs/SYNTHETIC_TEST.txt`）|
| `Assets/Scenes/Doosan-DT.unity` | 修改 | 新增入口面板、修丢失脚本、开 3 个 channel bool |
| `Assets/DoosanDT/10_DigitalTwin/Editor/TwinUIRemoveLegacyCanvas.cs` | 新增 | 菜单 `DigitalTwin/UI/5`：删 `User_ui`，删前做引用扫描（排除父子层级链）|
| `Assets/DoosanDT/10_DigitalTwin/Editor/TwinUIWiringAudit.cs` 等 | 新增 | 见 `10_AGENT/UI_ARCHITECTURE.md` §9 |
| `Assets/DoosanDT/20_BatteryDisassembly/Others/QuickOutline/Scripts/Outline.cs` | **修改** | `LoadSmoothNormals()` 跳过 Read/Write 关闭的网格（原先抛异常打断训练/拆解页激活）；场景文件未因此改变 |

## 回滚步骤（30 秒）

```powershell
$b='E:\new_2026-\unity\project\_backup\scenes\2026-11-03_post_visual'
Copy-Item "$b\Doosan-DT.unity"      'E:\new_2026-\unity\project\Unity_Ros_Try1\Assets\Scenes\' -Force
Copy-Item "$b\Doosan-DT.unity.meta" 'E:\new_2026-\unity\project\Unity_Ros_Try1\Assets\Scenes\' -Force
# Unity 里重新打开场景；URP 分级若要一起回滚，把 m_ColorGradingMode 改回 0
```
（回滚前先确认 Unity 已关闭该场景或先 Save，避免编辑器内存覆盖磁盘。）

## v2026-11-03.7（2026-11-03 23:02）UI 二期双主面板

| 项 | 值 |
|---|---|
| 场景 | `Assets/Scenes/Doosan-DT.unity` 4,650,214 B / 1,887 GameObject |
| SHA256 | `F795E210CEAEAD812E2DCDB99528E3FDEC85F859530A5073A7C34B2C8E1D5B37` |
| 备份 | `_backup\scenes\2026-11-03_r2_shell`（场景 + meta）；上一版 `2026-11-03_r2_pre_build` 为 v2026-11-03.6 |
| 新增 | `UI_二期面板`（370 Transform），`Scripts/UI/{Style,Bind,R2}/…` 与 `Editor/TwinUIR2*.cs` |
| 停用 | `30_界面与交互/UI_统一入口面板`（SetActive(false)，未删除） |
| 数据链 | 未改动（mtime 与哈希见批次 MANIFEST） |
| 回滚 | 复制 `_backup\scenes\2026-11-03_r2_shell\Doosan-DT.unity` 覆盖 `Assets\Scenes\` 后刷新 |
| 影响 | 显示开销自变量改变（UI 对象 368 个），E4 对比需记版本号 |


## v2026-11-03.8（2026-11-03 23:12）UI 配色改蓝白

| 项 | 值 |
|---|---|
| 场景 | 4,648,721 B / 1,887 GameObject |
| SHA256 | `289B6888D886F6B00ABF3E8DA7BE5CB9D0D9044DDFB9A7012C7E3CB18588E9F7` |
| 备份 | `_backup\scenes\2026-11-03_r2_bluewhite` |
| 变更 | 令牌改白底深字 + `#3F5AF0` 主色；按钮浅底五态；图表白底与 6 序列新色 |
| 复核 | `Logs/ui_r2_audit.txt`：颜色 13 种全部令牌、21 按钮 40px、对比度 4.83–15.80 |
| 回滚 | 恢复 `_backup\scenes\2026-11-03_r2_shell`（深色版 v2026-11-03.7） |

## v2026-11-04.0 — R3 工业驾驶舱（Unity UI）
- 场景 `Doosan-DT.unity`：`289B6888D886F6B0…` → `AE063339DA36B4A1…`，6,670,660 B；备份 `_backup/scenes/2026-11-04_r3_industrial`。
- 新增工业数据台（数据/事件/设置子页）、上下文条、深色/浅色双主题、阈值带与目标虚线、UI Hz 设置（5/10/20/30/60）、快照 JSON 与事件 JSONL 导出、Replay/Mock 数据源。
- 验收：`UI/12` `failures=0`、`UI/16` `missingDistinct=0`、`UI/14` 13 张截图、`UI/15` `exceptions=0`。
- 边界：真机力矩/位姿/时钟字段未联通（E4-B/C 阻塞），阈值未标定。

## v2026-11-04.1 — Play 漫游交互层（A1）
- 场景：`AE063339…` → `52063677…`（6,713,561 B），备份 `_backup/scenes/2026-11-04_play_interaction`。
- 新增 Play 相机 rig、屏幕射线+HUD、按钮 hover 渐变；菜单 `UI/6`、`UI/17`。
- 验收：`UI/6` PASS；`UI/17` 13 项 PASS（2 次一致）；`UI/12`/`UI/16` 回归 PASS；数据链与 `TwinRuntimeProfile.asset` 哈希不变。
- 边界：输入为组件 API 注入；未在真实外设/显示器目视确认。

## v2026-11-04.2 — 拖动末端法兰 IK ghost 预览（A2 级别 1）
- 场景：`52063677…` → `F4AF5927…`（6,715,395 B），备份 `_backup/scenes/2026-11-04_play_ik`。
- 新增 `TwinPlayIkHandle.cs`、`TwinUIIkBuilder.cs`；菜单 `UI/7`、`UI/18`。
- 验收：`UI/18` 25 项 PASS（拖动 10 mm、误差 1.458 mm、零下发、不可达红、6 截图、0 异常）；`UI/12`/`UI/16`/`UI/17` 回归 PASS。
- 边界：未接真机、位置-only IK、输入为 API 注入。
