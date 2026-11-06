# 论文图 ↔ 场景版本对照

用途：初稿/定稿里每张"系统界面/场景"图，都能追到具体场景版本、机位、生成方式，避免"图换了但正文没说清"。

| 图号（建议） | 用途 | 场景 | 版本 | 文件 | 生成方式 | 图注建议 |
|---|---|---|---|---|---|---|
| 图 2-x | 工程整体面貌/技术路线配图 | Doosan-DT | v2026-11-03.4 | `Logs/doosan_dt_view_wide_after.png` | 编辑器 Camera.Render，1920×1080，fov 55 | "基于 URP 的退役动力电池拆解数字孪生场景（Unity 编辑器渲染，非 VR 实拍）" |
| 图 3-x | 拆解工位/顺序约束现场 | Doosan-DT | v2026-11-03.4 | `Logs/doosan_dt_view_workcell_after.png` | 同上，fov 45 | "电池包拆解工位与机械臂布局（数字孪生端）" |
| 图 4-x | 双端联动/机械臂与电池包 | Doosan-DT | v2026-11-03.4 | `Logs/doosan_dt_view_robot_after.png` | 同上，fov 40 | "数字孪生端机械臂与电池包模型（H2515 + 电池包）" |
| 图 4-y | VR 端实拍 | VR_EVB_DL / 头显 | 待定 | 待采集 | 头显截图/串流 | 需另做，编辑器渲染不能冒充 VR 实拍 |
| 图 4-1 | 统一入口面板（漫游页，含力-力矩曲线） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/play_p07_final.png` | Play 模式 `ScreenCapture`，1920×1080 | "系统统一入口界面（Play 模式实拍；未连 ROS2、未录制，曲线为回放自检数据）" |
| 图 4-2 | 数据页（曲线 + 状态栏） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/play_p05_data_replay.png` | 同上 | "力/力矩与关节角曲线界面（回放夹具，非实验数据）" |
| 图 4-3 | 双向控制呈现层（干运行） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/play_p06_command_dryrun.png` | 同上 | "双向控制界面（预览/校验/确认/执行/急停；干运行，未下发真机指令）" |
| 图 4-4 | 四页切换（编辑期固定机位） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/i02–i05*.png` | 编辑器 `Camera.Render`，1600×900 | "入口面板四页状态（编辑器渲染；`i01` 欢迎页为 Overlay，相机渲染不含，需用 Play 截图代替）" |
| 图 4-5 | 训练页（Play 实拍） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/play_p08_training.png` | Play 模式 `ScreenCapture`，1920×1080 | "训练页：漫游/训练内容激活（Play 模式实拍；未连 ROS2、未录制）" |
| 图 4-6 | 拆解页（Play 实拍） | Doosan-DT | v2026-11-03.5 | `Logs/ui_shots/play_p10_disassembly.png` | 同上 | "拆解页：电池包拆解系统激活（Play 模式实拍；未连 ROS2、未录制）" |

## 纪律

1. 正文若写"实时帧率/延迟"，必须用实验数据（E1–E5），**不能**引用本目录截图。
2. "Unity 编辑器渲染"与"VR 运行时截图"必须区分标注；本目录所有 PNG 均为前者。
3. 换图就更新本表版本号 + `VERSION_LOG.md` 哈希，否则图无法追溯到场景状态。
4. `_before` 系列只作内部对比（汇报"改进前后"可用），正式论文默认用 `_after`。

## 当前图与初稿的差异（写正文时注意）

- 初稿 §2.4.1 写的"SolidWorks→STL→Blender 减面→FBX→URP"流程、面数（下壳体 25,248→9,534、整包约 130k）来自**报告值**，本轮未复核实际模型面数；引用时保留"报告值"口径或先统计再改。
- 初稿里若出现旧场景截图（EVB_Chapter4 / SampleScene），与本版场景不一致，需替换或加注"旧版本"。
- `登录界面`、`Gaze Interactor` 两处缺失脚本已在 v2026-11-03.5 修掉（`Logs/ui_fix_missing.txt`），
  v2026-11-03.4 及更早的截图若拍到这两处，属旧状态。
- UI 图（图 4-1…4-4）里曲线是**回放自检数据**（真实录制会话 + 明确标注的合成夹具），不得写成实验测量结果；
  叉车/机械臂姿态在图 4-1 中为场景默认位姿，未驱动。

## R2 截图（2026-11-03，编辑态摄像头渲染，未连 ROS2）

| 编号 | 文件 | 内容 |
|---|---|---|
| 图 4-7 | `_evidence/R2_screens/s00_mission.png` | 实训任务台：步骤列表 / 当前步骤 / 评估卡 |
| 图 4-8 | `_evidence/R2_screens/s01_twinops_overview.png` | 孪生监控台·概览：数据源健康、数据链四段、帧质量、告警 |
| 图 4-9 | `_evidence/R2_screens/s02_twinops_trend.png` | 孪生监控台·曲线：三张空态曲线卡（无数据源，未画假数据） |
| 图 4-10 | `_evidence/R2_screens/s03_twinops_control.png` | 孪生监控台·控制：安全校验九项、指令按钮、jog 禁用原因 |
| 图 4-11 | `_evidence/R2_screens/s04_twinops_record.png` | 孪生监控台·记录：记录状态/会话信息（只读） |

说明：均为编辑态 `Camera.Render()` 输出（Overlay 画布不在图内），分辨率 1600×900；画面里 `—`/`未连接` 是未连 ROS2 的真实空态。
23:12 已按蓝白版重出（同一路径覆盖，旧深色版可在 `_backup\scenes\2026-11-03_r2_shell` 对应状态下复现）。
