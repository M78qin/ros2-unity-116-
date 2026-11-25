using System.Collections.Generic;
using DigitalTwin;
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 二期 UI 的只读状态绑定层：以 5 Hz 采样 DigitalTwinRuntime 的公开状态，
    /// 汇总成视图模型（含告警），供顶栏与两个主面板读取。
    /// 约束：只读——不新增总线字段、不改 TwinRecorder / 数据链写入；
    /// E5 仅叠读 Ros2Bridge.LastGateStatus → 既有 Mode/告警/Chart 槽。
    /// </summary>
    public sealed class TwinUIStatusBinder : MonoBehaviour
    {
        public enum Severity
        {
            Info = 0,
            Warn = 1,
            Crit = 2
        }

        public struct Alarm
        {
            public string Text;
            public Severity Level;
        }

        public struct Snapshot
        {
            public bool HasFrame;
            public bool Connected;
            public string SourceKind;
            public string SourceName;
            // 本批（回放 UI）：源身份/更新时间的只读展示位；不参与数据链写入。
            public string SourceDisplay;      // 实时 / 回放 / 测试 / 未连接
            public string SourceDetail;       // runId / 备注（回放：samples.csv 的会话）
            public bool IsReplay;
            public string LastUpdateLocal;    // HH:mm:ss（本机，仅显示）
            public double ReplayProgress01;   // 回放进度 0..1（非回放恒为 0）
            public string LastError;
            public double Hz;
            public double AgeMs;
            public double LatencyMs;
            public double JitterMs;
            public long Dropped;
            public long OutOfOrder;
            public int Queue;
            public string Quality;
            public string Clock;
            public string Mode;
            public string Motion;
            public string MotionError;
            public float[] JointPos;
            public float[] JointVel;
            public float[] JointTorque;
            public float[] Force;
            // ---- A 支（阶段一 A）回放通道：全部来自同一份 TwinUiSnapshot，缺通道为空 ----
            /// <summary>回放关节角（deg，**原样**；有值时卡片优先用它，避免 deg→rad→deg 往返误差）。</summary>
            public float[] JointPosDeg;
            /// <summary>回放关节速度（deg/s 原样）。</summary>
            public float[] JointVelDeg;
            /// <summary>回放六维力/力矩：6 个值 [fx,fy,fz,tx,ty,tz]（N / N·m 原样）。</summary>
            public float[] Wrench;
            public bool WrenchComplete;
            /// <summary>回放 TCP **原始分量**：位置 mm、姿态 a/b/c deg（坐标系/欧拉约定 UNKNOWN）。</summary>
            public float[] TcpMm;
            public float[] TcpAbcDeg;
            public bool HasRawTcp;
            public string RawTcpConvention;
            /// <summary>status 列真解析结果（原值 + 是否正常 + 是否提供了该列）。</summary>
            public string StatusText;
            public bool StatusNormal;
            public bool StatusProvided;
            /// <summary>设备状态原值（robot_mode/robot_state/control_mode/solution_space/singularity），语义 UNKNOWN。</summary>
            public string DeviceText;
            public bool DevicePresent;
            /// <summary>有效位文本（joints_valid/controller_time_valid）。</summary>
            public string ValidityText;
            /// <summary>回放时间轴与时钟域说明。</summary>
            public string ClockAxisText;
            /// <summary>回放源身份不一致/混用说明（空 = 身份单一且一致）。</summary>
            public string IdentityWarning;
            public string ControlMode;
            public bool Estop;
            public bool DryRun;
            public bool Bidirectional;
            public bool RealCommand;
            public bool CanSend;
            public string LastStatus;
            public string LastCommandError;
            public bool Recording;
            public int Pending;
            public long RecDropped;
            public bool CsvReady;
            public string SessionId;
            public string CsvDir;
            public float Fps;
            public int AlarmCount;
            public int Step; // 预留
        }

        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private TwinCommandController controller;
        [SerializeField] private TwinRecorder recorder;
        [SerializeField] private float refreshHz = 10f;  // E5: Canvas/KPI 10–15 Hz (CSV stays record-rate)

        private readonly List<Alarm> _alarms = new List<Alarm>(8);
        private Snapshot _snapshot;
        private float _nextSample;
        private float _fps = 60f;

        // 本批（A3/A4/A6）：显示刷新计数、源分段跟踪。
        private int _sampleCount;
        private int _lastSegment = -1;

        /// <summary>已采样（显示刷新）次数；冻结显示时仍按显示刷新率增长（A6 证据）。</summary>
        public int SampleCount => _sampleCount;

        /// <summary>当前显示刷新率（Hz）。与 CSV 记录频率是两件事（A3）。</summary>
        public float RefreshHz => refreshHz;

        /// <summary>改显示刷新率（只改界面节流；不改回放钟/记录/CSV 频率）。</summary>
        public void SetRefreshHz(float hz)
        {
            float value = Mathf.Clamp(hz, 0.5f, 30f);
            if (Mathf.Abs(value - refreshHz) < 0.01f)
            {
                return;
            }

            refreshHz = value;
            _nextSample = 0f;
        }

        /// <summary>源分段号（来自 TwinUiSourceSwitch；每次源切换 +1）。</summary>
        public int SourceSegment => TwinUiSourceSwitch.Segment;

        // 回放覆盖：仅当场景里存在显式创建的 TwinUiReplayFeed（离线回放/截图驱动）时生效。
        // 实时源照旧走原采样路径；这里不建连接、不写文件、不下发命令。
        private TwinUiReplayFeed _replayFeed;
        private float _replayFeedScanAt;
        private bool _replayActive;

        /// <summary>当前快照是否来自离线回放覆盖（供状态条/自检判断）。</summary>
        public bool ReplayOverrideActive => _replayActive;

        // 编辑态预览（只由 Editor 工具调用，例如 UI/14 截图用回放数据驱动同一套绑定代码）。
        // Play 模式下永远不激活：不确定预览时 Current 仍读实时采样结果。
        private bool _previewActive;
        private Snapshot _preview;
        private readonly List<Alarm> _previewAlarms = new List<Alarm>(8);

        public Snapshot Current => _previewActive ? _preview : _snapshot;
        public IReadOnlyList<Alarm> Alarms => _previewActive ? _previewAlarms : _alarms;
        public bool PreviewActive => _previewActive;

        /// <summary>编辑态注入一个只读预览快照（不写回数据链，不参与 Play 运行）。</summary>
        public void SetEditorPreview(Snapshot preview, List<Alarm> alarms)
        {
            _preview = preview;
            _previewAlarms.Clear();
            if (alarms != null)
            {
                _previewAlarms.AddRange(alarms);
            }

            _previewActive = true;
        }

        public void ClearEditorPreview()
        {
            _previewActive = false;
            _previewAlarms.Clear();
        }
        public bool Bound => runtime != null;
        public DigitalTwinRuntime Runtime => runtime;
        public TwinCommandController Controller => controller != null
            ? controller
            : (runtime != null ? runtime.CommandController : null);
        public TwinRecorder Recorder => recorder != null ? recorder : (runtime != null ? runtime.Recorder : null);

        public void Bind(DigitalTwinRuntime runtimeRef, TwinCommandController controllerRef, TwinRecorder recorderRef)
        {
            runtime = runtimeRef;
            controller = controllerRef != null ? controllerRef : (runtimeRef != null ? runtimeRef.CommandController : null);
            recorder = recorderRef != null ? recorderRef : (runtimeRef != null ? runtimeRef.Recorder : null);
            Sample();
        }

        private void Update()
        {
            TwinUiSourceSwitch.EnsureInit();

            // A3：显示刷新率由 TwinUiSourceSwitch 统一驱动（与 CSV 记录频率分开）。
            if (Mathf.Abs(TwinUiSourceSwitch.DisplayHz - refreshHz) > 0.01f)
            {
                SetRefreshHz(TwinUiSourceSwitch.DisplayHz);
            }

            // A4：源切换 → 丢掉上一条源的回放 feed 与旧快照，新源从第一帧重新开始。
            if (_lastSegment != TwinUiSourceSwitch.Segment)
            {
                _lastSegment = TwinUiSourceSwitch.Segment;
                _replayFeed = null;
                _replayFeedScanAt = 0f;
                _snapshot = new Snapshot { SourceKind = "-", SourceDisplay = string.Empty, LastStatus = "源切换" };
                _nextSample = 0f;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
            {
                _fps = Mathf.Lerp(_fps, 1f / dt, 0.1f);
            }

            if (Time.unscaledTime < _nextSample)
            {
                return;
            }

            _nextSample = Time.unscaledTime + 1f / Mathf.Max(0.5f, refreshHz);
            Sample();
        }

        public void Sample()
        {
            _sampleCount++;
            var s = new Snapshot { Fps = _fps };
            _alarms.Clear();

            TwinUiSnapshot replay;
            if (TryReplayOverride(out replay))
            {
                _replayActive = true;
                ApplyReplay(ref s, replay);
                BuildAlarms(ref s);
                s.AlarmCount = _alarms.Count;
                _snapshot = s;
                return;
            }

            _replayActive = false;

            if (runtime == null)
            {
                s.SourceKind = "-";
                s.SourceName = "-";
                s.Quality = "-";
                s.Clock = "-";
                s.ControlMode = "-";
                s.LastStatus = "未绑定数据源";
                _snapshot = s;
                _alarms.Add(new Alarm { Text = "数据源未绑定", Level = Severity.Crit });
                s.AlarmCount = _alarms.Count;
                _snapshot = s;
                return;
            }

            s.SourceKind = runtime.ActiveSourceKind.ToString();
            var health = runtime.ActiveSourceHealth;
            s.Connected = health.IsConnected;
            s.SourceName = string.IsNullOrEmpty(health.SourceName) ? s.SourceKind : health.SourceName;
            s.LastError = health.LastError;
            s.Hz = health.EstimatedHz;
            s.AgeMs = health.LastFrameAgeMs;
            s.Dropped = health.DroppedFrames;

            var metrics = runtime.LatestMetrics;
            s.LatencyMs = metrics.ReceiveToApplyMs;
            s.JitterMs = metrics.JitterMs;
            s.OutOfOrder = metrics.OutOfOrderCount;
            s.Queue = metrics.RecordQueueLength;

            RobotStateFrame frame;
            s.HasFrame = runtime.TryGetLatestFrame(out frame);
            if (s.HasFrame)
            {
                s.Quality = frame.Quality.ToString();
                s.Clock = frame.ClockSyncStatus.ToString();
                s.Mode = string.IsNullOrEmpty(frame.Mode) ? "-" : frame.Mode;
                s.Motion = string.IsNullOrEmpty(frame.MotionState) ? "-" : frame.MotionState;
                s.MotionError = frame.MotionError ?? string.Empty;
                s.JointPos = frame.JointPositionRad;
                s.JointVel = frame.JointVelocityRad;
                s.JointTorque = frame.JointTorqueNm;
                s.Force = frame.ForceVector;
            }
            else
            {
                s.Quality = "无数据";
                s.Clock = "-";
                s.Mode = "-";
                s.Motion = "-";
                s.MotionError = string.Empty;
            }

            var c = controller;
            if (c == null)
            {
                c = runtime.CommandController;
            }

            if (c != null)
            {
                s.ControlMode = c.CurrentMode.ToString();
                s.Estop = c.IsEmergencyStopped;
                s.DryRun = c.EnableDryRun;
                s.Bidirectional = c.EnableBidirectionalControl;
                s.RealCommand = c.EnableRealRobotCommand;
                s.CanSend = c.CanSendRealMove;
                s.LastStatus = string.IsNullOrEmpty(c.LastResult.Status) ? "-" : c.LastResult.Status;
                s.LastCommandError = c.LastResult.ErrorMessage ?? string.Empty;
            }
            else
            {
                s.ControlMode = "-";
                s.LastStatus = "未找到控制组件";
            }

            // E5：把 /dt/status/gate_trace 闸门状态叠到既有 Mode/Status/告警槽（不新建 Canvas）。
            ApplyGateOverlay(ref s);

            var rec = recorder;
            if (rec == null)
            {
                rec = runtime.Recorder;
            }

            if (rec != null)
            {
                s.Recording = rec.IsRecording;
                s.Pending = rec.PendingCount;
                s.RecDropped = rec.DroppedFrameCount;
                s.CsvReady = rec.CsvReady;
                s.SessionId = rec.SessionId;
                s.CsvDir = rec.GetStorageDirectory();
            }

            BuildAlarms(ref s);
            s.AlarmCount = _alarms.Count;
            _snapshot = s;
        }

        // ------------------------------------------------------------ 离线回放覆盖（只读）

        private bool TryReplayOverride(out TwinUiSnapshot snapshot)
        {
            snapshot = null;

            // R2 A-1：回放覆盖的判定**只认 TwinUiSourceSwitch.IsReplay**（它由 TwinUiReplayFeed.Active
            // 且 Loaded 派生）。旧实现自己扫 Active，遇到"失败加载留下的空 feed"或"直连 Ensure 的舞台"
            // 时会和底栏 SourceBar 出现两套真值。
            if (!TwinUiSourceSwitch.IsReplay)
            {
                _replayFeed = null;
                return false;
            }

            TwinUiReplayFeed feed = TwinUiReplayFeed.Active;
            if (feed == null || !feed.Attached)
            {
                _replayFeed = null;
                return false;
            }

            _replayFeed = feed;
            _replayFeedScanAt = Time.unscaledTime + 2f;
            snapshot = feed.Snapshot;
            return snapshot != null;
        }

        /// <summary>当前回放覆盖用的 feed（没有则 null）；供自检复用同一引用，避免再扫一遍。</summary>
        public TwinUiReplayFeed ReplayFeed => _replayFeed;

        /// <summary>
        /// 把回放快照（TwinUiSnapshot，工业页同一份数据契约）映射成 V2 卡片的只读快照。
        /// 关节角在 CSV 里是 rad，卡片约定 JointPos 也是 rad，这里不做单位换算之外的加工；
        /// 缺失通道（力矩/六维力/TCP）保持空数组/空值，卡片自然显示"未订阅"，禁止补 0。
        /// </summary>
        private void ApplyReplay(ref Snapshot s, TwinUiSnapshot r)
        {
            s.SourceKind = "Replay";
            s.SourceDisplay = "回放";
            s.IsReplay = true;
            s.SourceName = r.Experiment != null && !string.IsNullOrEmpty(r.Experiment.RunId)
                ? r.Experiment.RunId
                : "samples.csv";
            s.SourceDetail = s.SourceName;
            s.Connected = r.Link != null && r.Link.Connected;
            s.HasFrame = r.Link != null && r.Link.HasFrame;
            s.Hz = r.Link != null ? r.Link.SampleHz : 0d;
            s.AgeMs = r.Link != null ? r.Link.FreshnessMs : 0d;
            s.LatencyMs = r.Link != null ? r.Link.ReceiveToApplyMs : 0d;
            s.JitterMs = r.Link != null ? r.Link.JitterP99Ms : 0d;
            s.Dropped = r.Link != null ? r.Link.DroppedFrames : 0L;
            s.OutOfOrder = r.Link != null ? r.Link.OutOfOrderFrames : 0L;
            s.Queue = r.Link != null ? r.Link.QueueLength : 0;
            s.Quality = r.Link != null && !string.IsNullOrEmpty(r.Link.Quality) ? r.Link.Quality : "无数据";
            s.Clock = r.Link != null && !string.IsNullOrEmpty(r.Link.ClockSync) ? r.Link.ClockSync : "-";
            s.Mode = "回放";
            s.Motion = "-";
            s.MotionError = string.Empty;
            s.ControlMode = "-";
            s.LastStatus = "回放（只读；不连 ROS、不写记录）";
            s.LastCommandError = string.Empty;
            s.Recording = false;
            s.CsvReady = false;
            s.Pending = 0;
            s.RecDropped = 0;
            s.SessionId = s.SourceName;
            s.CsvDir = string.Empty;
            s.LastUpdateLocal = !string.IsNullOrEmpty(r.ProducedAtLocal) && r.ProducedAtLocal.Length >= 8
                ? r.ProducedAtLocal.Substring(r.ProducedAtLocal.Length - 8)
                : System.DateTime.Now.ToString("HH:mm:ss");
            s.ReplayProgress01 = _replayFeed != null ? _replayFeed.Progress01 : 0d;

            if (r.Joints != null && r.Joints.PositionDeg != null && r.Joints.PositionDeg.Length > 0)
            {
                var pos = new float[r.Joints.PositionDeg.Length];
                for (int i = 0; i < pos.Length; i++)
                {
                    pos[i] = r.Joints.PositionDeg[i] * Mathf.Deg2Rad;
                }

                s.JointPos = pos;
            }

            if (r.Joints != null && r.Joints.TorqueNm != null && r.Joints.TorqueNm.Length > 0)
            {
                s.JointTorque = (float[])r.Joints.TorqueNm.Clone();
            }

            if (r.Force != null && r.Force.HasForce && r.Force.Force != null && r.Force.Force.Length > 0)
            {
                s.Force = (float[])r.Force.Force.Clone();
            }

            // ---- A 支：回放通道映射（缺通道保持空，不补 0）----
            if (r.Joints != null && r.Joints.PositionDeg != null && r.Joints.PositionDeg.Length > 0)
            {
                s.JointPosDeg = (float[])r.Joints.PositionDeg.Clone();
            }

            if (r.Joints != null && r.Joints.VelocityDegPerSec != null && r.Joints.VelocityDegPerSec.Length > 0)
            {
                s.JointVelDeg = (float[])r.Joints.VelocityDegPerSec.Clone();
            }

            if (r.Force != null && r.Force.HasForce && r.Force.Force != null && r.Force.Force.Length >= 3)
            {
                bool hasMoment = r.Force.HasMoment && r.Force.Moment != null && r.Force.Moment.Length >= 3;
                int n = hasMoment ? 6 : 3;
                var wrench = new float[n];
                for (int i = 0; i < 3; i++)
                {
                    wrench[i] = r.Force.Force[i];
                }

                if (hasMoment)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        wrench[3 + i] = r.Force.Moment[i];
                    }
                }

                s.Wrench = wrench;
                s.WrenchComplete = hasMoment;
            }

            if (r.RawTcp != null && r.RawTcp.HasRawTcp)
            {
                s.HasRawTcp = true;
                s.TcpMm = r.RawTcp.PositionMm != null && r.RawTcp.PositionMm.Length >= 3
                    ? (float[])r.RawTcp.PositionMm.Clone()
                    : new float[0];
                s.TcpAbcDeg = r.RawTcp.AbcDeg != null && r.RawTcp.AbcDeg.Length >= 3
                    ? (float[])r.RawTcp.AbcDeg.Clone()
                    : new float[0];
                s.RawTcpConvention = "坐标/欧拉=" + r.RawTcp.Frame + "/" + r.RawTcp.EulerConvention;
            }

            if (r.Status != null)
            {
                s.StatusProvided = r.Status.Provided;
                s.StatusNormal = r.Status.IsNormal;
                s.StatusText = r.Status.Display;
            }

            if (r.Device != null && r.Device.Present)
            {
                s.DevicePresent = true;
                s.DeviceText = "dev " + r.Device.ToCompact() + " · 语义 UNKNOWN";
            }

            if (r.Validity != null)
            {
                s.ValidityText = r.Validity.ToText();
            }

            if (r.Clocks != null)
            {
                s.ClockAxisText = "轴 " + r.Clocks.TimeAxis;
            }

            if (!string.IsNullOrEmpty(r.IdentityNote))
            {
                s.IdentityWarning = r.IdentityNote;
            }
        }

        private void ApplyGateOverlay(ref Snapshot s)
        {
            Ros2Bridge bridge = null;
            if (runtime != null)
            {
                bridge = runtime.Ros2Bridge;
            }

            if (bridge == null)
            {
                bridge = FindObjectOfType<Ros2Bridge>();
            }

            if (bridge == null)
            {
                return;
            }

            string gate = bridge.LastGateStatus;
            if (string.IsNullOrWhiteSpace(gate))
            {
                return;
            }

            // Mode KPI 子卡牌：显示闸门态；工艺状态用 StatusText。
            s.Mode = gate;
            s.StatusText = gate;
            s.StatusProvided = true;
            bool pause = gate == "TORQUE_LIMIT_PAUSE" || gate == "SAFE_Z_REJECT" || gate == "HALT";
            s.StatusNormal = !pause &&
                             gate != "REJECTED" &&
                             !gate.StartsWith("REJECTED", System.StringComparison.Ordinal);
            if (pause || !s.StatusNormal)
            {
                string reason = bridge.LastGateReason;
                s.MotionError = string.IsNullOrEmpty(reason) ? gate : gate + " | " + reason;
            }

            // 若帧里力矩/力为空，用 bridge 缓存补齐，供 ChartTorque / ChartWrench 刷新。
            if ((s.Force == null || s.Force.Length < 6) && bridge.LatestForceVector != null && bridge.LatestForceVector.Length >= 6)
            {
                s.Force = bridge.LatestForceVector;
            }
        }

        private void BuildAlarms(ref Snapshot s)
        {
            // 闸门高亮：优先 Crit，落在既有告警列表槽位（TwinUITwinOpsView alarm texts）。
            if (s.StatusProvided && !string.IsNullOrEmpty(s.StatusText))
            {
                if (s.StatusText == "TORQUE_LIMIT_PAUSE")
                {
                    _alarms.Add(new Alarm { Text = "力/力矩超限软停 " + (s.MotionError ?? ""), Level = Severity.Crit });
                }
                else if (s.StatusText == "SAFE_Z_REJECT")
                {
                    _alarms.Add(new Alarm { Text = "SAFE-Z 拦截 Z<1030 " + (s.MotionError ?? ""), Level = Severity.Crit });
                }
            }

            if (!s.Connected)
            {
                _alarms.Add(new Alarm { Text = "数据源未连接", Level = Severity.Crit });
            }
            else if (s.AgeMs > 2000)
            {
                _alarms.Add(new Alarm { Text = "数据陈旧 " + s.AgeMs.ToString("F0") + " ms", Level = Severity.Crit });
            }
            else if (s.AgeMs > 500)
            {
                _alarms.Add(new Alarm { Text = "数据延迟 " + s.AgeMs.ToString("F0") + " ms", Level = Severity.Warn });
            }

            if (s.Estop)
            {
                _alarms.Add(new Alarm { Text = "急停已触发", Level = Severity.Crit });
            }

            if (s.LatencyMs > 200)
            {
                _alarms.Add(new Alarm { Text = "应用时延 " + s.LatencyMs.ToString("F0") + " ms", Level = Severity.Crit });
            }
            else if (s.LatencyMs > 80)
            {
                _alarms.Add(new Alarm { Text = "应用时延 " + s.LatencyMs.ToString("F0") + " ms", Level = Severity.Warn });
            }

            if (s.Dropped > 0)
            {
                _alarms.Add(new Alarm { Text = "丢弃帧 " + s.Dropped, Level = Severity.Warn });
            }

            if (s.OutOfOrder > 0)
            {
                _alarms.Add(new Alarm { Text = "乱序帧 " + s.OutOfOrder, Level = Severity.Warn });
            }

            if (s.JitterMs > 20)
            {
                _alarms.Add(new Alarm { Text = "抖动 " + s.JitterMs.ToString("F1") + " ms", Level = Severity.Warn });
            }

            if (s.Quality != null && s.Quality != "Normal" && s.Quality != "无数据")
            {
                // A 支：status 非正常是**阻断级**信息（identity_mismatch 曾被固定标正常），
                // 用 Crit 级别而不是普通 Warn，并原样带出 status 值。
                if (s.StatusProvided && !s.StatusNormal)
                {
                    _alarms.Add(new Alarm { Text = s.StatusText, Level = Severity.Crit });
                }
                else
                {
                    _alarms.Add(new Alarm { Text = "帧质量 " + s.Quality, Level = Severity.Warn });
                }
            }

            if (s.Clock == "Unsynced")
            {
                _alarms.Add(new Alarm { Text = "时钟未同步", Level = Severity.Info });
            }

            if (!string.IsNullOrEmpty(s.MotionError))
            {
                _alarms.Add(new Alarm { Text = "运动错误 " + s.MotionError, Level = Severity.Warn });
            }
        }
    }
}