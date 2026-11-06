using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace DigitalTwin
{
    public enum Ros2PanelOperatingMode
    {
        Idle = 0,
        Control = 1,
    }

    [DisallowMultipleComponent]
    public sealed class Ros2ExperimentPanel : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private Ros2Bridge ros2Bridge;
        [SerializeField] private TwinCommandController commandController;
        [SerializeField] private TwinPaperRecorder paperRecorder;

        [Header("ROS2 Topics")]
        [SerializeField, Tooltip("真机 DRFL motion 时用 joint_states_rt（来自 actual_joint_position）")]
        private string jointStateTopic = "/dsr01/joint_states_rt";
        [SerializeField] private string moveJointTopic = "/dt/cmd/move_joint";
        [SerializeField] private string haltTopic = "/dt/cmd/halt";
        [SerializeField] private string setModeTopic = "/dt/cmd/set_mode";
        [SerializeField] private string modeStatusTopic = "/dt/status/mode";
        [SerializeField] private string motionStatusTopic = "/dt/status/motion";
        [SerializeField] private string sessionStatusTopic = "/dt/status/session";
        [SerializeField, Tooltip("论文级时延测量：ROS2 → Unity 的 String JSON ping。")]
        private string latencyPingTopic = "/dt/latency/ping";
        [SerializeField, Tooltip("论文级时延测量：Unity → ROS2 的 String JSON pong。")]
        private string latencyPongTopic = "/dt/latency/pong";
        [SerializeField] private string motionCmdTopic = "/dt/cmd/motion";
        [SerializeField] private bool enableWrenchTopic;
        [SerializeField] private string wrenchTopic = "/wrench";
        [SerializeField] private bool enableTcpPoseTopic;
        [SerializeField] private string tcpPoseTopic = "/tcp_pose";

        [Header("Paper Session")]
        [SerializeField] private string experimentId = "ros2_moveit_unity_20260525";
        [SerializeField] private string sourceId = "unity_ros2_panel";
        [SerializeField] private int phaseId;
        [SerializeField] private int segmentId = 1;
        [SerializeField] private bool streamEnabled;
        [SerializeField] private bool recordEnabled;
        [SerializeField] private bool recordPending;

        [Header("Channels / Rates")]
        [SerializeField, Tooltip("ROS2 bridge 主 CSV 目标频率；真机建议 100，与 joint_states 对齐。")]
        private float ros2MainHz = 100f;
        [SerializeField, Tooltip("ROS2 总控速度上限 (%)，Unity speedPercent 不得超过此值（由 /dt/status/session 回传）。")]
        private float ros2MasterSpeedPercent = 15f;
        [SerializeField, Tooltip("单轴最大关节增量 (deg)，与 WSL2 max_joint_delta_deg 一致。")]
        private float ros2MaxJointDeltaDeg = 10f;
        [SerializeField] private bool paperGradeChannels = true;
        [SerializeField] private bool jointPosition = true;
        [SerializeField] private bool jointVelocity = true;
        [SerializeField] private bool jointEffort = true;
        [SerializeField] private bool actualTcpPose = true;
        [SerializeField] private bool actualFlangePose;
        [SerializeField] private bool externalTcpForce = true;
        [SerializeField] private bool externalJointTorque = true;
        [SerializeField] private bool actualJointTorque;
        [SerializeField] private bool actualMotorTorque;
        [SerializeField] private bool rawForceTorque;
        [SerializeField] private bool targetJointPosition;
        [SerializeField] private bool targetTcpPosition;
        [SerializeField] private bool robotMode = true;
        [SerializeField] private bool robotState = true;
        [SerializeField] private bool controlMode = true;
        [SerializeField] private bool jointTemperature;
        [SerializeField] private bool solutionSpace;
        [SerializeField] private bool operationSpeedRate;

        [Header("Operating Mode")]
        [SerializeField] private Ros2PanelOperatingMode operatingMode = Ros2PanelOperatingMode.Idle;

        [Header("Preset Joint Commands")]
        [SerializeField, Tooltip("Unity 请求速度 (%)，不得超过 ROS2 master_speed_percent。")]
        private float speedPercent = 10f;
        [SerializeField, Tooltip("Capture 后 A = 相对 Home，J1 + 此角度 (deg)")]
        private float presetJ1DeltaADeg = 10f;
        [FormerlySerializedAs("presetJ6DeltaBDeg")]
        [FormerlySerializedAs("presetJ2DeltaBDeg")]
        [SerializeField, Tooltip("Capture 后 B = 相对 Home，J5 + 此角度 (deg)")]
        private float presetJ5DeltaBDeg = 10f;
        [SerializeField] private string capturedPoseSummary = string.Empty;
        [SerializeField, Tooltip("真机推荐：/dt/cmd/motion → Doosan move_joint（相对/绝对）。")]
        private bool useDoosanMotionCmd = true;
        [SerializeField, Tooltip("关闭则走 TwinCommandController / FJT 路径。")]
        private bool publishDirectlyToRos2 = true;
        [SerializeField] private float[] homeTargetDeg = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };
        [SerializeField] private float[] presetTargetADeg = new float[] { 5f, 0f, 0f, 0f, 0f, 0f };
        [SerializeField] private float[] presetTargetBDeg = new float[] { -5f, 0f, 0f, 0f, 0f, 0f };

        [Header("Temporary J1/J5 Debug Safety (2026-07-26)")]
        [SerializeField, Tooltip("仅本次 Play 会话使用。未武装时 Home/A/B 均不会发布；退出 Play 后恢复未武装。")]
        private bool temporaryJ1J5DebugArmed;
        [SerializeField, Tooltip("本次调试的 Unity 命令速度上限 (%)。不改变控制柜的操作速度设置。")]
        private float temporaryDebugSpeedCapPercent = 10f;

        [Header("Runtime Status")]
        [SerializeField] private string sessionId = string.Empty;
        [SerializeField] private string lastAction = "none";
        [SerializeField] private string lastStatus = "none";
        [SerializeField] private string lastError = string.Empty;

        public bool Chapter4Enabled => ros2Bridge != null && ros2Bridge.Chapter4Enabled;
        public bool Chapter4Recording => ros2Bridge != null && ros2Bridge.Chapter4Recording;
        public bool Chapter4Flushed => ros2Bridge != null && ros2Bridge.Chapter4Flushed;
        public string Chapter4Directory => ros2Bridge == null ? string.Empty : ros2Bridge.Chapter4Directory;
        public string Chapter4Error => ros2Bridge == null ? string.Empty : ros2Bridge.Chapter4Error;
        public bool Ros2Running => ros2Bridge != null && ros2Bridge.IsRunning;
        public bool Ros2LinkConnected => ros2Bridge != null && ros2Bridge.IsLinkConnected;
        public bool Ros2Connected => ros2Bridge != null && ros2Bridge.IsConnected;
        public string ActiveSessionId => Chapter4Enabled ? ros2Bridge.Chapter4RunId : sessionId;
        public bool StreamEnabled => streamEnabled;
        public bool RecordEnabled => recordEnabled;
        public bool RecordPending => recordPending;
        public bool Ros2RecordEnabled => ros2Bridge != null && ros2Bridge.Ros2RecordEnabled;
        public Ros2PanelOperatingMode OperatingMode => operatingMode;
        public bool IsControlMode => operatingMode == Ros2PanelOperatingMode.Control;
        public string LastAction => lastAction;
        public string LastStatus => lastStatus;
        public string LastError => lastError;
        public string CapturedPoseSummary => capturedPoseSummary;
        public float Ros2FrameRateHz => ros2Bridge == null ? 0f : ros2Bridge.FrameRateHz;
        public long Ros2DroppedFrames => ros2Bridge == null ? 0L : ros2Bridge.DroppedFrames;
        public bool IsTemporaryJ1J5DebugArmed => temporaryJ1J5DebugArmed;

        private void Reset()
        {
            ResolveBindings();
            ApplyRos2Defaults();
        }

        private void Awake()
        {
            ResolveBindings();
            operatingMode = Ros2PanelOperatingMode.Idle;
            // This is deliberately a session-only safety interlock.  A saved scene/profile
            // must never reopen in a state that can issue a real-robot command.
            temporaryJ1J5DebugArmed = false;
        }

        private void Update()
        {
            SyncMotionLimitsFromRos2();
            if(Chapter4Enabled)
            {
                recordPending=false;
                recordEnabled=Chapter4Recording;
                return;
            }
            if (!recordPending || ros2Bridge == null)
            {
                return;
            }

            if (!ros2Bridge.Ros2RecordEnabled)
            {
                return;
            }

            recordPending = false;
            recordEnabled = true;
            paperRecorder?.SetSessionRecordEnabled(true, segmentId);
            ConfigurePaper("RECORD_STARTED", BuildChannelNotes());
            Remember("START_RECORD", true, "RECORDING", string.Empty);
        }

        private void OnValidate()
        {
            ros2MainHz = Mathf.Clamp(ros2MainHz, 1f, 200f);
            speedPercent = Mathf.Clamp(speedPercent, 0.1f, 60f);
            temporaryDebugSpeedCapPercent = Mathf.Clamp(temporaryDebugSpeedCapPercent, 0.1f, 10f);
            EnsureTargetLength(ref homeTargetDeg);
            EnsureTargetLength(ref presetTargetADeg);
            EnsureTargetLength(ref presetTargetBDeg);
        }

        public void ResolveBindings()
        {
            if (runtime == null) runtime = GetComponent<DigitalTwinRuntime>();
            if (runtime == null) runtime = FindObjectOfType<DigitalTwinRuntime>();
            if (ros2Bridge == null) ros2Bridge = GetComponent<Ros2Bridge>();
            if (ros2Bridge == null && runtime != null) ros2Bridge = runtime.Ros2Bridge;
            if (ros2Bridge == null) ros2Bridge = FindObjectOfType<Ros2Bridge>();
            if (commandController == null) commandController = GetComponent<TwinCommandController>();
            if (commandController == null) commandController = FindObjectOfType<TwinCommandController>();
            if (paperRecorder == null && runtime != null) paperRecorder = runtime.PaperRecorder;
            if (paperRecorder == null) paperRecorder = GetComponent<TwinPaperRecorder>();
            if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
        }

        public void ApplyRos2Defaults()
        {
            jointStateTopic = "/dsr01/joint_states_rt";
            moveJointTopic = "/dt/cmd/move_joint";
            haltTopic = "/dt/cmd/halt";
            setModeTopic = "/dt/cmd/set_mode";
            modeStatusTopic = "/dt/status/mode";
            motionStatusTopic = "/dt/status/motion";
            latencyPingTopic = "/dt/latency/ping";
            latencyPongTopic = "/dt/latency/pong";
            enableWrenchTopic = false;
            enableTcpPoseTopic = false;
            ros2MainHz = 100f;
            ApplyPaperGradeChannelDefaults();
        }

        public void ApplyPaperGradeChannelDefaults()
        {
            if (!paperGradeChannels)
            {
                return;
            }

            Ros2TelemetryChannels all = Ros2TelemetryChannels.PaperGradeAll();
            jointPosition = all.jointPosition;
            jointVelocity = all.jointVelocity;
            jointEffort = all.jointEffort;
            actualTcpPose = all.actualTcpPose;
            actualFlangePose = all.actualFlangePose;
            externalTcpForce = all.externalTcpForce;
            externalJointTorque = all.externalJointTorque;
            actualJointTorque = all.actualJointTorque;
            actualMotorTorque = all.actualMotorTorque;
            rawForceTorque = all.rawForceTorque;
            targetJointPosition = all.targetJointPosition;
            targetTcpPosition = all.targetTcpPosition;
            robotMode = all.robotMode;
            robotState = all.robotState;
            controlMode = all.controlMode;
            jointTemperature = all.jointTemperature;
            solutionSpace = all.solutionSpace;
            operationSpeedRate = all.operationSpeedRate;
        }

        public Ros2TelemetryChannels BuildTelemetryChannels()
        {
            if (paperGradeChannels)
            {
                return Ros2TelemetryChannels.PaperGradeAll();
            }

            return new Ros2TelemetryChannels
            {
                jointPosition = jointPosition,
                jointVelocity = jointVelocity,
                jointEffort = jointEffort,
                actualTcpPose = actualTcpPose,
                actualFlangePose = actualFlangePose,
                externalTcpForce = externalTcpForce,
                externalJointTorque = externalJointTorque,
                actualJointTorque = actualJointTorque,
                actualMotorTorque = actualMotorTorque,
                rawForceTorque = rawForceTorque,
                targetJointPosition = targetJointPosition,
                targetTcpPosition = targetTcpPosition,
                robotMode = robotMode,
                robotState = robotState,
                controlMode = controlMode,
                jointTemperature = jointTemperature,
                solutionSpace = solutionSpace,
                operationSpeedRate = operationSpeedRate,
            };
        }

        public RobotCommandResult ConnectRos2()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("CONNECT_ROS2", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ApplyTopicSettings();
            if (ros2Bridge.IsRunning && ros2Bridge.IsLinkConnected)
            {
                return Remember("CONNECT_ROS2", true, "LINKED", "TCP linked (no telemetry yet).");
            }

            bool ok = ros2Bridge.ConnectLink();
            return Remember("CONNECT_ROS2", ok, ok ? "LINKED" : "ERROR", ros2Bridge.LastError);
        }

        public RobotCommandResult DisconnectRos2()
        {
            ResolveBindings();
            if(Chapter4Enabled) operatingMode = Ros2PanelOperatingMode.Idle;
            else SetIdleMode();
            ros2Bridge?.Disconnect();
            streamEnabled = false;
            recordEnabled = false;
            recordPending = false;
            return Remember("DISCONNECT_ROS2", true, "DISCONNECTED", string.Empty);
        }

        public RobotCommandResult SetIdleMode()
        {
            operatingMode = Ros2PanelOperatingMode.Idle;
            ros2Bridge?.SetMode("idle");
            ConfigurePaper("IDLE_MODE", string.Empty);
            return Remember("IDLE_MODE", true, "STANDBY",
                "Receive-only; channels/stream/record OK; motion blocked.");
        }

        public RobotCommandResult SetControlMode()
        {
            if (!Ros2LinkConnected && !(ros2Bridge != null && ros2Bridge.IsRunning))
            {
                return Remember("CONTROL_MODE", false, "BLOCKED", "Connect ROS2 first.");
            }

            operatingMode = Ros2PanelOperatingMode.Control;
            ros2Bridge?.SetMode("control");
            ConfigurePaper("CONTROL_MODE", string.Empty);
            return Remember("CONTROL_MODE", true, "CONTROL", "Motion commands enabled.");
        }

        public RobotCommandResult ApplyTopicSettings()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("APPLY_TOPICS", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ros2Bridge.ApplyTopicConfiguration(
                jointStateTopic,
                moveJointTopic,
                haltTopic,
                setModeTopic,
                modeStatusTopic,
                motionStatusTopic,
                enableWrenchTopic,
                wrenchTopic,
                enableTcpPoseTopic,
                tcpPoseTopic);
            ros2Bridge.ApplySessionStatusTopic(sessionStatusTopic);
            ros2Bridge.ApplyLatencyTopics(latencyPingTopic, latencyPongTopic);
            return Remember("APPLY_TOPICS", true, "APPLIED", string.Empty);
        }

        public RobotCommandResult CreateSession()
        {
            ResolveBindings();
            if(Chapter4Enabled)
                return Remember("CREATE_SESSION", false, "BLOCKED", "Chapter4 uses its frozen run profile; load a new profile for a new run.");
            if (string.IsNullOrWhiteSpace(experimentId))
            {
                experimentId = "ros2_moveit_unity";
            }

            sessionId = experimentId + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            phaseId = 0;
            segmentId = Mathf.Max(1, segmentId);
            streamEnabled = false;
            recordEnabled = false;
            recordPending = false;
            PublishSession("create_session");
            ConfigurePaper("SESSION_CREATED", BuildChannelNotes());
            return Remember("CREATE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult CloseSession()
        {
            if(Chapter4Enabled) return StopStream();
            if (recordEnabled || recordPending) StopPaperRecord();
            if (streamEnabled) StopStream();
            PublishSession("close_session");
            paperRecorder?.CloseSession();
            sessionId = string.Empty;
            return Remember("CLOSE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult ApplyChannelsAndRates()
        {
            if(Chapter4Enabled)
                return Remember("APPLY_CHANNELS", false, "BLOCKED", "Chapter4 channels and apply rate come from the frozen experiment configuration.");
            PublishSession("set_channels", includeChannels: true);
            ConfigurePaper("CHANNELS_APPLIED", BuildChannelNotes());
            return Remember("APPLY_CHANNELS", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StartStream()
        {
            if(Chapter4Enabled)
            {
                try
                {
                    // The frozen Chapter4 run is already loaded. Do not create a
                    // second legacy adapter session or replace its configuration.
                    streamEnabled = ros2Bridge.StartTelemetryStream(BuildTelemetryChannels());
                    recordEnabled = streamEnabled && Chapter4Recording;
                    recordPending = false;
                    return Remember("START_STREAM", streamEnabled,
                        streamEnabled ? "RECORDING" : "ERROR", ros2Bridge.LastError);
                }
                catch(Exception e)
                {
                    streamEnabled=false; recordEnabled=false; recordPending=false;
                    return Remember("START_STREAM", false, "ERROR", e.Message);
                }
            }
            EnsureSession();
            if (ros2Bridge == null)
            {
                return Remember("START_STREAM", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            if (!ros2Bridge.IsRunning)
            {
                ConnectRos2();
            }

            ros2Bridge.StartTelemetryStream(BuildTelemetryChannels());
            PublishSession("start_stream", includeChannels: true);
            streamEnabled = true;
            paperRecorder?.SetSessionStreamEnabled(true);
            ConfigurePaper("STREAM_STARTED", BuildChannelNotes());
            return Remember("START_STREAM", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopStream()
        {
            streamEnabled = false;
            ros2Bridge?.StopTelemetryStream();
            if(Chapter4Enabled)
            {
                recordEnabled=false; recordPending=false;
                bool ok=Chapter4Flushed && string.IsNullOrEmpty(Chapter4Error);
                return Remember("STOP_STREAM", ok, ok ? "FLUSHED" : "INCOMPLETE", Chapter4Error);
            }
            PublishSession("stop_stream");
            paperRecorder?.SetSessionStreamEnabled(false);
            if (recordEnabled || recordPending) StopPaperRecord();
            ConfigurePaper("STREAM_STOPPED", string.Empty);
            return Remember("STOP_STREAM", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StartPaperRecord()
        {
            if(Chapter4Enabled)
                return Remember("START_RECORD", Chapter4Recording,
                    Chapter4Recording ? "RECORDING" : "BLOCKED",
                    Chapter4Recording ? string.Empty : "Load a new Chapter4 run profile before recording.");
            EnsureSession();
            if (!streamEnabled)
            {
                StartStream();
            }

            segmentId = Mathf.Max(1, segmentId);
            recordEnabled = false;
            recordPending = true;
            PublishSession("start_record", includeChannels: true);

            if (ros2Bridge != null && ros2Bridge.Ros2RecordEnabled)
            {
                recordPending = false;
                recordEnabled = true;
                paperRecorder?.SetSessionRecordEnabled(true, segmentId);
                ConfigurePaper("RECORD_STARTED", BuildChannelNotes());
                return Remember("START_RECORD", true, "RECORDING", string.Empty);
            }

            ConfigurePaper("RECORD_REQUESTED", BuildChannelNotes());
            return Remember("START_RECORD", true, "PENDING", "Waiting for ROS2 record_enabled.");
        }

        public RobotCommandResult StopPaperRecord()
        {
            if(Chapter4Enabled) return StopStream();
            recordPending = false;
            recordEnabled = false;
            PublishSession("stop_record");
            paperRecorder?.SetSessionRecordEnabled(false, segmentId);
            ConfigurePaper("RECORD_STOPPED", string.Empty);
            return Remember("STOP_RECORD", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopRecordAndStream()
        {
            StopPaperRecord();
            return StopStream();
        }

        public RobotCommandResult CaptureCurrentPoseAsPresets()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("CAPTURE_POSE", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            if (!streamEnabled && !ros2Bridge.TelemetryStreamEnabled)
            {
                return Remember("CAPTURE_POSE", false, "BLOCKED",
                    "Start data stream first so /joint_states is live.");
            }

            if (!ros2Bridge.TryGetLatestJointPositionsDeg(out float[] currentDeg, out double ageMs))
            {
                return Remember("CAPTURE_POSE", false, "ERROR", "No recent joint_states.");
            }

            if (ageMs > 500d)
            {
                return Remember("CAPTURE_POSE", false, "STALE",
                    $"joint_states age {ageMs:0}ms > 500ms.");
            }

            homeTargetDeg = CopyJointArray(currentDeg);
            presetTargetADeg = CopyJointArray(currentDeg);
            presetTargetADeg[0] += presetJ1DeltaADeg;
            presetTargetBDeg = CopyJointArray(currentDeg);
            presetTargetBDeg[4] += presetJ5DeltaBDeg;
            capturedPoseSummary = FormatTargetDeg(homeTargetDeg);
            Ros2CapturedPosePersistence.Save(homeTargetDeg, presetTargetADeg, presetTargetBDeg, capturedPoseSummary);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            ConfigurePaper("POSE_CAPTURED", BuildPresetNotesJson());
            return Remember("CAPTURE_POSE", true, "CAPTURED", capturedPoseSummary);
        }

        /// <summary>退出 Play 后把 Capture 写回 Inspector 序列化字段（由 Editor 调用）。</summary>
        public void ApplyPersistedCaptureToEditMode()
        {
            if (!Ros2CapturedPosePersistence.HasCapture)
            {
                return;
            }

            homeTargetDeg = CopyJointArray(Ros2CapturedPosePersistence.HomeDeg);
            presetTargetADeg = CopyJointArray(Ros2CapturedPosePersistence.PresetADeg);
            presetTargetBDeg = CopyJointArray(Ros2CapturedPosePersistence.PresetBDeg);
            capturedPoseSummary = Ros2CapturedPosePersistence.Summary ?? string.Empty;
        }

        public RobotCommandResult SendHome() => SendJointTarget("MOVE_HOME", homeTargetDeg, null);
        public RobotCommandResult SendPresetA() =>
            SendJointTarget("MOVE_PRESET_A", presetTargetADeg, BuildRelativeDelta(presetJ1DeltaADeg, 0));
        public RobotCommandResult SendPresetB() =>
            SendJointTarget("MOVE_PRESET_B", presetTargetBDeg, BuildRelativeDelta(0, presetJ5DeltaBDeg));

        /// <summary>
        /// Arms the temporary J1/J5-only test profile for the current Play session.
        /// It intentionally does not switch to Control mode or send a robot command.
        /// </summary>
        public RobotCommandResult ArmTemporaryJ1J5DebugProfile()
        {
            temporaryJ1J5DebugArmed = true;
            temporaryDebugSpeedCapPercent = 10f;
            speedPercent = temporaryDebugSpeedCapPercent;
            useDoosanMotionCmd = true;
            publishDirectlyToRos2 = true;
            return Remember("ARM_J1_J5_DEBUG", true, "STANDBY",
                "J1/J5 only; A=relative J1, B=relative J5; Unity speed cap=10%; Home is blocked.");
        }

        /// <summary>Relocks all Home/A/B motion without disconnecting telemetry.</summary>
        public RobotCommandResult DisarmTemporaryJ1J5DebugProfile()
        {
            temporaryJ1J5DebugArmed = false;
            return Remember("DISARM_J1_J5_DEBUG", true, "STANDBY", "Home/A/B motion relocked.");
        }

        public RobotCommandResult Halt()
        {
            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            RobotCommandResult result;
            if (publishDirectlyToRos2)
            {
                result = ros2Bridge == null
                    ? new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.")
                    : ros2Bridge.SendHalt();
            }
            else
            {
                result = commandController == null
                    ? new RobotCommandResult(false, false, "ERROR", "TwinCommandController is unavailable.")
                    : commandController.EmergencyStop();
            }

            LogPaperCommand("HALT", string.Empty, result, sendWallMs, sendNs, string.Empty);
            return Remember("HALT", result);
        }

        public RobotCommandResult MarkEvent(string eventType = "ROS2_PANEL_MARK")
        {
            ConfigurePaper(eventType, BuildChannelNotes());
            return Remember(eventType, true, "LOCAL", string.Empty);
        }

        private void PublishSession(string action, bool includeChannels = false)
        {
            if (Chapter4Enabled || ros2Bridge == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            StringBuilder json = new StringBuilder(256);
            json.Append("{\"action\":\"").Append(Escape(action)).Append("\"");
            json.Append(",\"session_id\":\"").Append(Escape(sessionId)).Append("\"");
            json.Append(",\"experiment_id\":\"").Append(Escape(experimentId)).Append("\"");
            json.Append(",\"source_id\":\"").Append(Escape(sourceId)).Append("\"");
            if (includeChannels)
            {
                json.Append(",\"channels\":").Append(BuildChannelNotes());
            }

            if (action == "create_session" || action == "set_motion_limits")
            {
                json.Append(",\"motion_limits\":{");
                json.Append("\"master_speed_percent\":")
                    .Append(ros2MasterSpeedPercent.ToString(CultureInfo.InvariantCulture));
                json.Append(",\"max_joint_delta_deg\":")
                    .Append(ros2MaxJointDeltaDeg.ToString(CultureInfo.InvariantCulture));
                json.Append(",\"motion_backend\":\"doosan_move_joint\"}");
            }

            json.Append('}');
            ros2Bridge.PublishSessionCommand(json.ToString());
        }

        private static float[] BuildRelativeDelta(float j1Deg, float j5Deg)
        {
            // Doosan order is J1..J6.  Only J1 (index 0) and J5 (index 4) may be non-zero.
            return new float[] { j1Deg, 0f, 0f, 0f, j5Deg, 0f };
        }

        private float EffectiveSpeedPercent()
        {
            float master = ros2Bridge != null ? ros2Bridge.MasterSpeedPercent : ros2MasterSpeedPercent;
            if (master <= 0f)
            {
                master = ros2MasterSpeedPercent;
            }

            float requested = Mathf.Min(speedPercent, master);
            return temporaryJ1J5DebugArmed
                ? Mathf.Min(requested, temporaryDebugSpeedCapPercent)
                : requested;
        }

        private void SyncMotionLimitsFromRos2()
        {
            if (ros2Bridge == null)
            {
                return;
            }

            Ros2SessionStatus status = ros2Bridge.LatestSessionStatus;
            if (status == null)
            {
                return;
            }

            if (status.master_speed_percent > 0f)
            {
                ros2MasterSpeedPercent = status.master_speed_percent;
            }

            if (status.max_joint_delta_deg > 0f)
            {
                ros2MaxJointDeltaDeg = status.max_joint_delta_deg;
            }
        }

        private RobotCommandResult SendJointTarget(string action, float[] targetDeg, float[] relativeDeltaDeg)
        {
            RobotCommandResult debugSafety = ValidateTemporaryJ1J5DebugMotion(action, relativeDeltaDeg);
            if (!debugSafety.Success)
            {
                LogPaperCommand(action, string.Empty, debugSafety, SystemClock.UtcUnixMs(), SystemClock.NowNs(),
                    relativeDeltaDeg == null ? FormatTargetDeg(targetDeg) : "rel_deg=" + FormatTargetDeg(relativeDeltaDeg));
                return Remember(action, debugSafety);
            }

            if (!IsControlMode)
            {
                RobotCommandResult blocked = new RobotCommandResult(
                    false, false, "BLOCKED", "Idle mode: switch to Control mode to send motion.");
                LogPaperCommand(action, string.Empty, blocked, SystemClock.UtcUnixMs(), SystemClock.NowNs(),
                    FormatTargetDeg(targetDeg));
                return Remember(action, blocked);
            }

            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (publishDirectlyToRos2)
            {
                if (ros2Bridge == null)
                {
                    RobotCommandResult missingBridge = new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand(action, string.Empty, missingBridge, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
                    return Remember(action, missingBridge);
                }

                float vel = EffectiveSpeedPercent();
                RobotCommandResult rosResult;
                if (useDoosanMotionCmd)
                {
                    if (relativeDeltaDeg != null && action != "MOVE_HOME")
                    {
                        rosResult = ros2Bridge.SendDoosanMotionRelative(relativeDeltaDeg, vel);
                    }
                    else
                    {
                        rosResult = ros2Bridge.SendDoosanMotionAbsolute(targetDeg, vel);
                    }
                }
                else
                {
                    rosResult = ros2Bridge.SendMoveJointRad(DegreesToRadians(targetDeg), vel);
                }

                string summary = relativeDeltaDeg != null && action != "MOVE_HOME"
                    ? "rel_deg=" + FormatTargetDeg(relativeDeltaDeg)
                    : FormatTargetDeg(targetDeg);
                LogPaperCommand(action, string.Empty, rosResult, sendWallMs, sendNs, summary);
                return Remember(action, rosResult);
            }

            if (commandController == null)
            {
                RobotCommandResult missing = new RobotCommandResult(false, false, "ERROR", "TwinCommandController is unavailable.");
                LogPaperCommand(action, string.Empty, missing, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
                return Remember(action, missing);
            }

            float[] targetRad = DegreesToRadians(targetDeg);
            RobotCommandResult result = commandController.SendMoveJoint(targetRad, EffectiveSpeedPercent());
            LogPaperCommand(action, string.Empty, result, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
            return Remember(action, result);
        }

        private RobotCommandResult ValidateTemporaryJ1J5DebugMotion(string action, float[] relativeDeltaDeg)
        {
            if (!temporaryJ1J5DebugArmed)
            {
                return new RobotCommandResult(false, false, "BLOCKED",
                    "J1/J5 temporary debug profile is not armed. Telemetry remains available; motion is locked.");
            }

            if (!useDoosanMotionCmd || !publishDirectlyToRos2)
            {
                return new RobotCommandResult(false, false, "BLOCKED",
                    "J1/J5 debug requires direct /dt/cmd/motion (Doosan adapter); FJT and TwinCommandController routes are locked.");
            }

            if (action == "MOVE_HOME" || relativeDeltaDeg == null || relativeDeltaDeg.Length != 6)
            {
                return new RobotCommandResult(false, false, "BLOCKED",
                    "Home/absolute targets are disabled for this test. Use relative preset A (J1) or B (J5) only.");
            }

            const float epsilonDeg = 0.0001f;
            bool onlyA = action == "MOVE_PRESET_A" &&
                         Mathf.Abs(relativeDeltaDeg[1]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[2]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[3]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[4]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[5]) <= epsilonDeg;
            bool onlyB = action == "MOVE_PRESET_B" &&
                         Mathf.Abs(relativeDeltaDeg[0]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[1]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[2]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[3]) <= epsilonDeg &&
                         Mathf.Abs(relativeDeltaDeg[5]) <= epsilonDeg;
            if (!onlyA && !onlyB)
            {
                return new RobotCommandResult(false, false, "BLOCKED",
                    "Temporary profile permits only A=J1 relative or B=J5 relative; J2/J3/J4/J6 must be zero.");
            }

            return new RobotCommandResult(true, false, "AUTHORIZED", string.Empty);
        }

        private void EnsureSession()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                CreateSession();
            }
        }

        private void ConfigurePaper(string eventType, string notes)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            bool effectiveStream = streamEnabled || paperRecorder.StreamEnabled;
            bool effectiveRecord = recordEnabled || paperRecorder.RecordEnabled;
            paperRecorder.ConfigureSession(
                experimentId, sessionId, phaseId, segmentId,
                effectiveStream, effectiveRecord, eventType, notes);
        }

        private void LogPaperCommand(string action, string reqId, RobotCommandResult result, long sendWallMs, long sendNs, string targetSummary)
        {
            if (paperRecorder == null || !paperRecorder.IsRecording)
            {
                return;
            }

            paperRecorder.RecordCommand(
                action, "ROS2", reqId, string.Empty, result,
                sendWallMs, sendNs, SystemClock.UtcUnixMs(), SystemClock.NowNs(), targetSummary);
        }

        private RobotCommandResult Remember(string action, RobotCommandResult result)
        {
            lastAction = string.IsNullOrEmpty(action) ? "none" : action;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            return result;
        }

        private RobotCommandResult Remember(string action, bool success, string status, string error)
        {
            return Remember(action, new RobotCommandResult(success, false, status, error));
        }

        private string BuildChannelNotes()
        {
            return BuildTelemetryChannels().ToJsonNotes();
        }

        private string BuildPresetNotesJson()
        {
            return "{\"home\":" + BuildTargetJson(homeTargetDeg)
                   + ",\"A\":" + BuildTargetJson(presetTargetADeg)
                   + ",\"B\":" + BuildTargetJson(presetTargetBDeg) + "}";
        }

        private static string BuildTargetJson(float[] targetDeg)
        {
            if (targetDeg == null || targetDeg.Length == 0)
            {
                return "[]";
            }

            string[] parts = new string[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                parts[i] = targetDeg[i].ToString("0.###", CultureInfo.InvariantCulture);
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private static float[] CopyJointArray(float[] source)
        {
            float[] copy = new float[6];
            if (source == null)
            {
                return copy;
            }

            Array.Copy(source, copy, Math.Min(source.Length, copy.Length));
            return copy;
        }

        private static float[] DegreesToRadians(float[] targetDeg)
        {
            if (targetDeg == null) return Array.Empty<float>();
            float[] targetRad = new float[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                targetRad[i] = targetDeg[i] * Mathf.Deg2Rad;
            }
            return targetRad;
        }

        private static string FormatTargetDeg(float[] targetDeg)
        {
            if (targetDeg == null || targetDeg.Length == 0) return string.Empty;
            string[] parts = new string[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                parts[i] = targetDeg[i].ToString("0.###", CultureInfo.InvariantCulture);
            }
            return string.Join(";", parts);
        }

        private static void EnsureTargetLength(ref float[] target)
        {
            if (target != null && target.Length == 6) return;
            float[] old = target;
            target = new float[6];
            if (old != null) Array.Copy(old, target, Mathf.Min(old.Length, target.Length));
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
