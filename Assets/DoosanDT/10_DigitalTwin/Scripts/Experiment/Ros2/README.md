# Experiment/Ros2 — ROS2 实验面板

> 上级：[Experiment/README.md](../README.md) · **ROS 线缆**：[INTERFACES.md §5](../../../INTERFACES.md#5-ros2-线缆协议ros2bridge)

## 职责

ROS2 论文实验的 **Inspector 驱动面板**：连接、Topic 配置、Paper Session、预设关节命令。

## 关键文件

| 文件 | 作用 |
|------|------|
| `Ros2ExperimentPanel.cs` | Connect/Disconnect；Topic；CreateSession；Move Home/A/B；Halt |
| `Editor/Ros2ExperimentPanelEditor.cs` | Inspector UI |

## Chapter4 模式接续（2026-10-26）

沿用本面板开始/停止数据流和记录按钮。加载 Chapter4 profile 后，不创建第二个 legacy 会话；停止会结束 Chapter4 记录并显示 FLUSHED 或 INCOMPLETE，以及文件目录/错误。结束后必须加载新 profile 才能再开始。该分支只管理遥测和文件，不发送模式/机器人停止命令。实际控制操作仍遵守既有独立控制门槛。

## 默认 Topic（`ApplyRos2Defaults`）

```text
/dsr01/joint_states_rt
/dt/cmd/motion, /dt/cmd/move_joint, /dt/cmd/halt, /dt/cmd/set_mode
/dt/status/mode, /dt/status/motion
/dt/latency/ping, /dt/latency/pong
```

## 论文级 Unity 往返时延协议

该协议只测量 ROS2↔Unity 通讯路径；不触发运动、轨迹、模式或停止命令。`Ros2Bridge`
订阅 `/dt/latency/ping` 的 `std_msgs/String`，收到后立即记录 Unity `t1`，将 pong
排入队列，并只在 Unity 主线程发布 `/dt/latency/pong`，发布前记录 `t2`。

```json
// ping (ROS2 -> Unity)
{"seq":17,"t0_wall_ms":1785030000123,"t0_mono_ns":123456789,"session_id":"e1_20260726"}

// pong (Unity -> ROS2)
{"seq":17,"session_id":"e1_20260726","t0_wall_ms":1785030000123,"t0_mono_ns":123456789,"t1_wall_ms":1785030000127,"t1_mono_ns":456789012,"t2_wall_ms":1785030000128,"t2_mono_ns":457100000}
```

字段语义：`t0` 为 ROS2 发 ping 时的 wall/monotonic 时间，`t1` 为 Unity 收到回调时的
wall/monotonic 时间，`t2` 为 Unity 主线程发布 pong 前的 wall/monotonic 时间。不同进程的
`*_mono_ns` 不作相减；wall-clock RTT/单程估计需要实验中说明时钟条件。Unity 仅报告自身可比的
`t2_mono_ns - t1_mono_ns` 队列等待时间。

当 `TwinPaperRecorder` 已有活动 session 时，Unity 写入事件：

- `LATENCY_PONG_PUBLISHED`：`notes` 为 JSON，含 `protocol`、`status`、`ping_topic`、`pong_topic`、`seq`、`session_id`、`t0_*`、`t1_*`、`t2_*`、`unity_queue_to_publish_ms`、`pong_json`、`error`。
- `LATENCY_PONG_NOT_PUBLISHED`：同一 schema，`status` 为 `NOT_CONNECTED` 或 `ERROR`，保留原因。

在 ROS2 面板的 **ROS2 Topics → 论文级时延 Ping / Pong** 保持默认路径即可；点击“应用 Topic”后生效。

## 2026-07-26 临时真机调试：J1 / J5 only

本轮沿用既有 `Ros2ExperimentPanel → Ros2Bridge → /dt/cmd/motion → Doosan adapter`
链路，**不使用** `/dt/cmd/move_joint → FJT/MoveIt` 真机执行。

1. 真机连接、控制柜 10% 操作速度、ROS2 driver/endpoint 仅由操作者按已验证命令手动启动。
2. Unity 进入 Play，先 `连接 ROS2`、开启数据流，确认 `/dsr01/joint_states_rt` 有实时状态。
3. 在 Inspector 的“本次真机调试安全锁（J1 / J5）”明确点击“本次会话武装 J1/J5”；该操作不发送运动命令，也不切换控制模式。
4. 采集当前位姿后，仅可在 Control 模式发送：A=`relative J1 + Δ`、B=`relative J5 + Δ`。JSON 的 `delta_deg` 为 `[J1,0,0,0,J5,0]`；Unity 命令速度上限为 10%。
5. `Home`、绝对目标、FJT/TwinCommandController 路径均会被该会话锁拦截。退出 Play 或点击“重新锁定 Home/A/B”后，A/B 也立即锁定。

该锁只是 Unity 端调试互锁，不能替代控制柜、实验室安全区或 ROS2 adapter 的轴白名单与速度限制。

## 场景挂载

```text
Ros2Experimen → Ros2ExperimentPanel
4.24-Try/ROS2 → Ros2Bridge
```

## `Ros2ExperimentPanel` 公开 API

| 方法 | 说明 |
|------|------|
| `ResolveBindings` / `ApplyRos2Defaults` | 绑定与默认 Topic |
| `ConnectRos2` / `DisconnectRos2` | `Ros2Bridge.Connect/Disconnect` |
| `ApplyTopicSettings` | `ConfigureTopics` + telemetry |
| `CreateSession` / `CloseSession` | Paper session 元数据 |
| `ApplyChannelsAndRates` | 通道（逻辑同 Session，走 panel 配置） |
| `StartStream` / `StopStream` | 流标记 + recorder |
| `StartPaperRecord` / `StopPaperRecord` / `StopRecordAndStream` | Paper CSV |
| `ArmTemporaryJ1J5DebugProfile` / `DisarmTemporaryJ1J5DebugProfile` | 本次 Play 会话的 J1/J5 安全互锁 |
| `SendHome` / `SendPresetA` / `SendPresetB` | 本轮中仅 A(J1 相对) / B(J5 相对) 可发；Home 被锁定 |
| `Halt` | 急停 |
| `MarkEvent` | 事件标记 |

属性：`Ros2Running`, `Ros2Connected`, `Ros2FrameRateHz`, `StreamEnabled`, `RecordEnabled`

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| Topic 名/遥测开关 | `Ros2ExperimentPanel` + `Ros2Bridge.ConfigureTopics` |
| 直接发 ROS vs 走 CommandController | `publishDirectlyToRos2` |
| Paper 记录会话 | `paperRecorder.ConfigureSession` |
| 主实验是否启用 ROS2 | `TwinSystemProfile.enableRos2Source`（默认 false） |

## 已知缺口

- 双源 Dart+ROS2 同时开时 Runtime 强制 ROS2 为 active source
- 真机 ROS2 端到端需单独验证（与 Dart 路径独立）
# 第四章入口更新（2026-10-16）

当前第四章规程和字段契约集中保存于 WSL：`/home/zyq/ros2_ws/src/docs/ros2-unity/chapter4/README.md` 与同目录 `contracts.md`。此处仅保留定位，本文件原有实验说明作为历史实现参考。

新增 `TwinJointSample` 模式通过 Ros2Bridge 的 `chapter4ProfilePath` 载入会话配置；使用统一源身份、独占 ROS 状态源、30/60 Hz 应用、独立接收/应用/模型实际读数和逐渲染帧记录。记录器在 `Experiment/Recording/Chapter4Recorder.cs`。配置由 WSL `chapter4 prepare` 生成，需核对实际 Windows 输出目录。C# 编译已通过，场景加载和 Play 联调仍待现场验证。
