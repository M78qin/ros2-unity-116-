using System;
using UnityEngine;

namespace DigitalTwin
{
    public enum TwinMode
    {
        Sync = 0,
        Plan = 1,
        Execute = 2
    }

    public enum RobotCommandType
    {
        MoveJoint = 0,
        Halt = 1,
        Pause = 2,
        Resume = 3
    }

    // P0-F2/F3（2026-11-18）：远端停止确认状态。它与本地闩锁是两个独立事实：
    //   None      = 没有发出停止请求（本地闩锁可能是被 SetEmergencyStopped(true) 直接置位的）；
    //   Pending   = HALT 已投递到 /dt/cmd/halt，等待 ROS2 回执；
    //   Confirmed = ROS2 在本次 HALT 之后回过停止类状态（HALT_REQUESTED/STOPPED/...）；
    //   Failed    = HALT 没能投递出去（桥未启用/异常），远端状态未知。
    // 说明：Confirmed 只证明"远端受理/已请求停止"，不构成物理停止证明。
    public enum RemoteStopState
    {
        None = 0,
        Pending = 1,
        Confirmed = 2,
        Failed = 3
    }

    public readonly struct RobotCommandResult
    {
        public readonly bool Success;
        public readonly bool DryRun;
        public readonly string Status;
        public readonly string ErrorMessage;

        public RobotCommandResult(bool success, bool dryRun, string status, string errorMessage)
        {
            Success = success;
            DryRun = dryRun;
            Status = status;
            ErrorMessage = errorMessage;
        }
    }

    [DisallowMultipleComponent]
    public sealed class TwinCommandController : MonoBehaviour
    {
        [Header("Mode / 模式")]
        [SerializeField, Tooltip("Unity 端当前控制状态。Sync=只跟随真实反馈；Plan=调整目标；Execute=允许发送 MOVE_JOINT。")]
        private TwinMode currentMode = TwinMode.Sync;

        [SerializeField, Tooltip("是否允许进入 Execute。通常由 Profile 的 enableBidirectionalControl 自动写入。")]
        private bool allowExecuteMode;

        [Header("Safety / 安全")]
        [SerializeField, Tooltip("双向控制总开关。关闭时 UI 可看数据，但不会发送 MOVE_JOINT。")]
        private bool enableBidirectionalControl;

        [SerializeField, Tooltip("真实机械臂命令开关。关闭时 MOVE_JOINT/HALT 默认只 dry-run，不会发真实 TCP。")]
        private bool enableRealRobotCommand;

        [SerializeField, Tooltip("干跑模式。开启时只打印将要发送的命令，不控制真实机械臂。第一次联调建议保持开启。")]
        private bool enableDryRun = true;

        [SerializeField, Tooltip("急停锁存状态。开启后 MOVE_JOINT 会被阻止，直到脚本或 Inspector 清除。")]
        private bool emergencyStopped;

        // ---- P0-F2/F3 运行时状态（不序列化，不改变场景默认值） ----
        private RemoteStopState _remoteStop = RemoteStopState.None;
        private string _remoteStopDetail = "尚未请求远端停止";
        private long _haltRequestNs;
        private long _haltAckNs;
        private long _haltAttempts;

        public RemoteStopState RemoteStop => _remoteStop;
        public string RemoteStopDetail => _remoteStopDetail;
        public long HaltRequestNs => _haltRequestNs;
        public long HaltAckNs => _haltAckNs;
        public long HaltAttemptCount => _haltAttempts;
        /// <summary>本地闩锁 + 远端确认同时成立时才允许解除急停。</summary>
        public bool CanClearEmergencyStop => emergencyStopped && _remoteStop == RemoteStopState.Confirmed;



        private readonly PlanningControlService _planning = new PlanningControlService();
        private readonly SafetyCheckService _safety = new SafetyCheckService();
        private readonly DigitalTwinModeService _localModeService = new DigitalTwinModeService();
        private DartStudioBridge _bridge;
        private Ros2Bridge _ros2Bridge;
        private DigitalTwinRuntime _runtime;
        private DigitalTwinModeService _modeService;
        private TwinPaperRecorder _paperRecorder;

        public TwinMode CurrentMode => currentMode;
        public bool IsEmergencyStopped => emergencyStopped;
        public bool EnableBidirectionalControl => enableBidirectionalControl;
        public bool EnableRealRobotCommand => enableRealRobotCommand;
        public bool EnableDryRun => enableDryRun;
        public bool CanSendRealMove => enableBidirectionalControl && enableRealRobotCommand && !enableDryRun && !emergencyStopped;
        public RobotStateFrame PlanningStartFrame => _planning.PlanningStartFrame;
        public string LastPlanningStatus => _planning.LastStatus;
        public string LastPlanningError => _planning.LastError;
        public RobotCommandResult LastResult { get; private set; }

        public void Initialize(DigitalTwinRuntime runtime)
        {
            _runtime = runtime;
            _bridge = runtime == null ? GetComponent<DartStudioBridge>() : runtime.DartBridge;
            if (_bridge == null)
            {
                _bridge = FindObjectOfType<DartStudioBridge>();
            }
            _ros2Bridge = runtime == null ? GetComponent<Ros2Bridge>() : runtime.Ros2Bridge;
            if (_ros2Bridge == null)
            {
                _ros2Bridge = FindObjectOfType<Ros2Bridge>();
            }
            _modeService = runtime == null ? _localModeService : runtime.ModeService;
            _modeService.Initialize(runtime == null ? null : runtime.Settings);
            _safety.Initialize(runtime);
            _planning.Initialize(runtime, this, _modeService);
            _paperRecorder = runtime == null ? FindObjectOfType<TwinPaperRecorder>() : runtime.PaperRecorder;
            ApplySettings(runtime == null ? null : runtime.Settings);
            // P0-F3: 远端停止回执只从主 Ros2Bridge 的 /dt/status/motion 通道进来。
            if (_ros2Bridge != null)
            {
                _ros2Bridge.MotionStatusChanged -= NotifyRemoteMotionState;
                _ros2Bridge.MotionStatusChanged += NotifyRemoteMotionState;
            }
        }

        private void OnDestroy()
        {
            if (_ros2Bridge != null)
            {
                _ros2Bridge.MotionStatusChanged -= NotifyRemoteMotionState;
            }
        }

        public bool EnterSync()
        {
            _planning.ReleasePreviewToLive();
            currentMode = TwinMode.Sync;
            _modeService?.EnterMirror();
            RobotCommandResult result = SendModeCommand("idle_stream");
            SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
            return true;
        }

        public bool EnterPlan()
        {
            return _planning.EnterPlan();
        }

        public bool EnterExecute()
        {
            // P0-T3: 闩锁期间一律不得进入 Execute（此前只由 SendMoveJoint 的安全链兜底，
            // 直接调 EnterExecute 可以绕过闩锁）。
            if (emergencyStopped)
            {
                SetResult(false, enableDryRun || !enableRealRobotCommand, "BLOCKED",
                    "Emergency stop is latched; remote stop confirmation is required before Execute.");
                return false;
            }

            if (!allowExecuteMode)
            {
                SetResult(false, enableDryRun || !enableRealRobotCommand, "BLOCKED", "Enable Bidirectional Control before Execute.");
                return false;
            }

            currentMode = TwinMode.Execute;
            _modeService?.EnterExecute();
            return true;
        }

        public RobotCommandResult StartMode1Test()
        {
            bool dryRun = enableDryRun || !enableRealRobotCommand;
            if (!enableBidirectionalControl || !enableRealRobotCommand || enableDryRun)
            {
                return SetResult(false, dryRun, "BLOCKED", "Mode1 test moves the real robot. Enable bidirectional control, real robot command, and disable dry-run first.");
            }

            _planning.ReleasePreviewToLive();
            SetLocalMode(TwinMode.Sync);
            RobotCommandResult result = SendModeCommand("mode1_test");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        public RobotCommandResult EnterDartControlMode()
        {
            SetLocalMode(TwinMode.Plan);
            RobotCommandResult result = SendModeCommand("mode2_ctrl");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        public RobotCommandResult StopDartTask()
        {
            SetLocalMode(TwinMode.Sync);
            RobotCommandResult result = SendModeCommand("idle_stream");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        /// <summary>
        /// 直接置位/请求清除本地闩锁。
        /// 置位：只锁本地，不发远端停止（要发远端请用 EmergencyStop()）。
        /// 清除（P0-F3）：必须已有远端停止确认，否则拒绝清除并保持闩锁。
        /// </summary>
        public void SetEmergencyStopped(bool stopped)
        {
            if (!stopped)
            {
                string reason;
                if (!TryClearEmergencyStop(out reason))
                {
                    Debug.LogWarning("[TwinCommandController] 拒绝解除急停：" + reason, this);
                }

                return;
            }

            emergencyStopped = true;
            _remoteStop = RemoteStopState.None;
            _remoteStopDetail = "本地闩锁已置位，但未发送 HALT；远端未被请求停止";
        }

        /// <summary>
        /// P0-F3：解除闩锁的唯一入口。只有「本地在闩锁 + 远端已确认停止」同时成立才返回 true。
        /// </summary>
        public bool TryClearEmergencyStop(out string reason)
        {
            if (!emergencyStopped)
            {
                reason = "当前未处于急停闩锁";
                return true;
            }

            if (_remoteStop != RemoteStopState.Confirmed)
            {
                reason = "远端未确认停止（" + _remoteStop + "：" + _remoteStopDetail + "）";
                return false;
            }

            emergencyStopped = false;
            reason = "OK";
            _remoteStop = RemoteStopState.None;
            _remoteStopDetail = "远端已确认停止，闩锁已解除（非物理停止证明）";
            return true;
        }

        /// <summary>
        /// P0-F3：远端停止回执入口。由 Ros2Bridge.MotionStatusChanged 调用（也可被测试直接调用，
        /// 与生产路径同一个入口）。只有在本次 HALT 之后到达、且属于停止类状态的回执才算确认。
        /// </summary>
        public void NotifyRemoteMotionState(string motionState, long receiveNs)
        {
            if (!emergencyStopped)
            {
                return;
            }

            if (_remoteStop == RemoteStopState.Confirmed)
            {
                return;
            }

            string state = NormalizeMotionState(motionState);
            if (receiveNs < _haltRequestNs)
            {
                _remoteStopDetail = "丢弃过期回执 state=" + state + "（早于本次 HALT）";
                return;
            }

            if (!IsRemoteStopAck(state))
            {
                if (_remoteStop == RemoteStopState.Pending)
                {
                    _remoteStopDetail = "远端状态=" + state + "，不是停止确认，闩锁保持";
                }

                return;
            }

            _remoteStop = RemoteStopState.Confirmed;
            _haltAckNs = receiveNs;
            long ackMs = (receiveNs - _haltRequestNs) / 1000000L;
            _remoteStopDetail = "远端回执 " + state + "（HALT 后 " + ackMs + " ms）；表示远端已受理停止请求，不是物理停止证明";
        }

        /// <summary>远端停止确认接受的 motion 状态集合（adapter 当前只发 HALT_REQUESTED）。</summary>
        private static bool IsRemoteStopAck(string state)
        {
            switch (state)
            {
                case "HALT_REQUESTED":
                case "HALTED":
                case "STOPPED":
                case "MOVE_STOPPED":
                case "STANDBY":
                    return true;
                default:
                    return false;
            }
        }

        private static string NormalizeMotionState(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "(empty)";
            }

            string text = raw.Trim();
            int split = text.IndexOf('|');
            if (split >= 0)
            {
                text = text.Substring(0, split).Trim();
            }

            return text.ToUpperInvariant();
        }

        private void ApplyHaltSendOutcome(RobotCommandResult result)
        {
            if (result.Success)
            {
                _remoteStop = RemoteStopState.Pending;
                _remoteStopDetail = "HALT 已投递到 /dt/cmd/halt（std_msgs/Bool），等待 /dt/status/motion 回执";
                return;
            }

            _remoteStop = RemoteStopState.Failed;
            _remoteStopDetail = "HALT 未投递成功（" + result.Status +
                                (string.IsNullOrEmpty(result.ErrorMessage) ? string.Empty : "：" + result.ErrorMessage) +
                                "）；远端状态未知，闩锁保持";
        }

        public void SetLocalMode(TwinMode mode)
        {
            currentMode = mode;
            switch (mode)
            {
                case TwinMode.Sync:
                    _modeService?.EnterMirror();
                    break;
                case TwinMode.Plan:
                    _modeService?.EnterPlan();
                    break;
                case TwinMode.Execute:
                    _modeService?.EnterExecute();
                    break;
            }
        }

        public void UpdatePlanTarget(float[] targetJointRad)
        {
            _planning.UpdateGhostTarget(targetJointRad);
        }

        public RobotCommandResult ExecuteTarget(float[] targetJointRad, float speedPercent)
        {
            return _planning.ExecuteTarget(targetJointRad, speedPercent);
        }

        public void CancelPlan()
        {
            _planning.CancelPlan();
        }

        public RobotCommandResult SendMoveJoint(float[] targetJointRad, float speedPercent)
        {
            bool dryRun = enableDryRun || !enableRealRobotCommand;
            SafetyCheckResult safety = _safety.ValidateMoveJoint(targetJointRad, this, _planning.PlanningStartFrame);
            if (!safety.Passed)
            {
                return SetResult(false, safety.DryRun, safety.Status, safety.Message);
            }
            dryRun = safety.DryRun;

            if (dryRun)
            {
                Debug.Log($"DRY_RUN MOVE_JOINT: {BuildMoveJointJson(targetJointRad)}", this);
                RobotCommandResult dryRunResult = SetResult(true, true, "DRY_RUN", string.Empty);
                LogPaperCommand("MOVE_JOINT", "LOCAL_DRY_RUN", BuildMoveJointJson(targetJointRad), dryRunResult, FormatTargetRad(targetJointRad), 0, 0);
                return dryRunResult;
            }

            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult rosUnavailableResult = SetResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand("MOVE_JOINT", "ROS2", string.Empty, rosUnavailableResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                    return rosUnavailableResult;
                }

                float[] targetDegRos = new float[targetJointRad.Length];
                for (int i = 0; i < targetDegRos.Length; i++) targetDegRos[i] = targetJointRad[i] * Mathf.Rad2Deg;
                RobotCommandResult rosResult = _ros2Bridge.SendDoosanMotionAbsolute(targetDegRos, speedPercent);
                RobotCommandResult rosCommandResult = SetResult(rosResult.Success, rosResult.DryRun, rosResult.Status, rosResult.ErrorMessage);
                LogPaperCommand("MOVE_JOINT", "ROS2", string.Empty, rosCommandResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                return rosCommandResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult dartUnavailableResult = SetResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    LogPaperCommand("MOVE_JOINT", "DartStudio", string.Empty, dartUnavailableResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                    return dartUnavailableResult;
                }

                float[] targetDeg = new float[targetJointRad.Length];
                for (int i = 0; i < targetJointRad.Length; i++)
                {
                    targetDeg[i] = targetJointRad[i] * Mathf.Rad2Deg;
                }

                RobotCommandResult bridgeResult = _bridge.SendMoveJoint(targetDeg);
                RobotCommandResult dartCommandResult = SetResult(bridgeResult.Success, bridgeResult.DryRun, bridgeResult.Status, bridgeResult.ErrorMessage);
                LogPaperCommand("MOVE_JOINT", "DartStudio", DartTcpCommandBuilder.BuildMoveJoint(targetDeg), dartCommandResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                return dartCommandResult;
            }

            RobotCommandResult noRoute = SetResult(false, false, "ERROR", "No active command source is available.");
            LogPaperCommand("MOVE_JOINT", "None", string.Empty, noRoute, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
            return noRoute;
        }

        public RobotCommandResult EmergencyStop()
        {
            emergencyStopped = true;
            _haltAttempts++;
            _haltRequestNs = SystemClock.NowNs();
            _remoteStop = RemoteStopState.Pending;
            _remoteStopDetail = "HALT 请求进行中";

            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult rosUnavailableResult = SetResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    ApplyHaltSendOutcome(rosUnavailableResult);
                    LogPaperCommand("HALT", "ROS2", string.Empty, rosUnavailableResult, string.Empty, sendWallMs, sendNs);
                    return rosUnavailableResult;
                }

                RobotCommandResult rosResult = _ros2Bridge.SendHalt();
                RobotCommandResult rosHaltResult = SetResult(rosResult.Success, rosResult.DryRun, rosResult.Status, rosResult.ErrorMessage);
                ApplyHaltSendOutcome(rosHaltResult);
                LogPaperCommand("HALT", "ROS2", string.Empty, rosHaltResult, string.Empty, sendWallMs, sendNs);
                return rosHaltResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult dartUnavailableResult = SetResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    ApplyHaltSendOutcome(dartUnavailableResult);
                    LogPaperCommand("HALT", "DartStudio", string.Empty, dartUnavailableResult, string.Empty, sendWallMs, sendNs);
                    return dartUnavailableResult;
                }

                // HALT is allowed to bypass dry-run because it only asks source side to stop motion.
                RobotCommandResult bridgeResult = _bridge.SendHalt();
                RobotCommandResult dartHaltResult = SetResult(bridgeResult.Success, bridgeResult.DryRun, bridgeResult.Status, bridgeResult.ErrorMessage);
                ApplyHaltSendOutcome(dartHaltResult);
                LogPaperCommand("HALT", "DartStudio", DartTcpCommandBuilder.BuildHalt(), dartHaltResult, string.Empty, sendWallMs, sendNs);
                return dartHaltResult;
            }

            RobotCommandResult noRoute = SetResult(false, false, "ERROR", "No active command source is available.");
            ApplyHaltSendOutcome(noRoute);
            LogPaperCommand("HALT", "None", string.Empty, noRoute, string.Empty, sendWallMs, sendNs);
            return noRoute;
        }

        private void ApplySettings(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            enableBidirectionalControl = settings.EnableBidirectionalControl;
            enableRealRobotCommand = settings.EnableRealRobotCommand;
            enableDryRun = settings.EnableDryRun;
            allowExecuteMode = settings.EnableBidirectionalControl;
        }

        private int ResolveJointCount()
        {
            RobotSignalSchema schema = _runtime == null ? null : _runtime.Schema;
            if (schema != null && schema.JointCount > 0)
            {
                return schema.JointCount;
            }

            return 6;
        }

        private string BuildMoveJointJson(float[] targetJointRad)
        {
            if (targetJointRad == null || targetJointRad.Length == 0)
            {
                return string.Empty;
            }

            float[] targetDeg = new float[targetJointRad.Length];
            for (int i = 0; i < targetJointRad.Length; i++)
            {
                targetDeg[i] = targetJointRad[i] * Mathf.Rad2Deg;
            }

            return DartTcpCommandBuilder.BuildMoveJoint(targetDeg);
        }

        private RobotCommandResult SetResult(bool success, bool dryRun, string status, string error)
        {
            LastResult = new RobotCommandResult(success, dryRun, status, error);
            return LastResult;
        }

        private void LogPaperCommand(string action, string route, string commandJson, RobotCommandResult result, string targetSummary, long sendWallMs, long sendNs)
        {
            if (_paperRecorder == null || !_paperRecorder.IsRecording)
            {
                return;
            }

            if (sendWallMs <= 0) sendWallMs = SystemClock.UtcUnixMs();
            if (sendNs <= 0) sendNs = SystemClock.NowNs();
            _paperRecorder.RecordCommand(
                action,
                route,
                string.Empty,
                commandJson,
                result,
                sendWallMs,
                sendNs,
                SystemClock.UtcUnixMs(),
                SystemClock.NowNs(),
                targetSummary);
        }

        private static string FormatTargetRad(float[] targetJointRad)
        {
            if (targetJointRad == null || targetJointRad.Length == 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder b = new System.Text.StringBuilder(targetJointRad.Length * 12);
            for (int i = 0; i < targetJointRad.Length; i++)
            {
                if (i > 0) b.Append(';');
                b.Append(targetJointRad[i].ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
            }

            return b.ToString();
        }

        private RobotCommandResult SendModeCommand(string mode)
        {
            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult result = new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand("SET_MODE", "ROS2", string.Empty, result, mode, sendWallMs, sendNs);
                    return result;
                }

                RobotCommandResult rosResult = _ros2Bridge.SetMode(mode);
                LogPaperCommand("SET_MODE", "ROS2", string.Empty, rosResult, mode, sendWallMs, sendNs);
                return rosResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult result = new RobotCommandResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    LogPaperCommand("SET_MODE", "DartStudio", string.Empty, result, mode, sendWallMs, sendNs);
                    return result;
                }

                string json = DartTcpCommandBuilder.BuildSetMode(mode);
                RobotCommandResult bridgeResult = _bridge.SetMode(mode);
                LogPaperCommand("SET_MODE", "DartStudio", json, bridgeResult, mode, sendWallMs, sendNs);
                return bridgeResult;
            }

            RobotCommandResult noRoute = new RobotCommandResult(false, false, "ERROR", "No active command source is available.");
            LogPaperCommand("SET_MODE", "None", string.Empty, noRoute, mode, sendWallMs, sendNs);
            return noRoute;
        }

        private CommandRoute ResolveCommandRoute()
        {
            if (_runtime != null)
            {
                if (_runtime.ActiveSourceKind == RuntimeSourceKind.Ros2)
                {
                    return CommandRoute.Ros2;
                }

                if (_runtime.ActiveSourceKind == RuntimeSourceKind.DartStudio)
                {
                    return CommandRoute.DartStudio;
                }
            }

            if (_ros2Bridge != null)
            {
                return CommandRoute.Ros2;
            }

            if (_bridge != null)
            {
                return CommandRoute.DartStudio;
            }

            return CommandRoute.None;
        }

        private enum CommandRoute
        {
            None = 0,
            DartStudio = 1,
            Ros2 = 2
        }

    }
}
