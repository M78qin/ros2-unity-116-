# Experiment/Recording — 论文级记录

> 上级：[Experiment/README.md](../README.md) · **CSV 列**：[INTERFACES.md §9](../../../INTERFACES.md#9-记录输出) · 契约：[Contracts/README.md](../Contracts/README.md)

## 当前 Chapter4 记录

`Chapter4Recorder.cs` 是现有 ROS2 同源记录器，与下方 legacy TwinPaperRecorder 并列保留。它记录 samples.csv 的 receive/apply/model、render_frames.csv、profile/模型映射快照以及 quality.json；WSL `chapter4.py analyze` 离线分析。

在既有面板停止数据流即可结束 Chapter4 并落盘，不必退出 Play；下次会话先载入新的 profile。写盘失败/队列丢弃保持可见；没有质量文件不等于零错误。目录为 Windows PaperLogs/run_id。具体字段见 [接口总表 §14](../../../INTERFACES.md#14-当前-ros2-chapter4-契约2026-10-26-接续)。

## 历史记录职责

`IExperimentRecorder` 实现：**只写 raw facts**，指标由 Python `analyze.py` 离线计算。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinPaperRecorder.cs` | `unity_paper_v2` schema；receive/apply/event CSV；manifest |

## 输出文件

```text
PaperLogs/<date>/<experiment_id>/<session_id>/
  session_manifest.json
  manifest.json
  unity_receive.csv
  unity_apply.csv
  unity_event.csv    # 含 COMMAND_ECHO
```

默认根目录：`TwinExperimentProfile.paperStorageRootDirectory`

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| CSV 列 / schema 版本 | `TwinPaperRecorder.SchemaVersion` + `analyze.py` |
| receive vs apply 记录开关 | Profile `paperRecordReceiveFrames` / `paperRecordApplyFrames` |
| 命令 echo 事件 | `ExperimentSessionController.LogCommandEcho` |
| event notes JSON 逗号问题 | `TwinPaperRecorder` 写入转义（PROGRESS 待修） |

## 硬规则

- 不在 Unity 算 p95 RTT 等论文指标（Tracker 已降级为 marker）
- 改列必改 analyzer
