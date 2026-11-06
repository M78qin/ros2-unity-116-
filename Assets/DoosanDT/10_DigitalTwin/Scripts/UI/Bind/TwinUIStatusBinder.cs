using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 二期 UI 的只读状态绑定层：以 5 Hz 采样 DigitalTwinRuntime 的公开状态，
    /// 汇总成视图模型（含告警），供顶栏与两个主面板读取。
    /// 约束：只读——不新增总线字段、不改 Ros2Bridge / TwinRecorder / 数据链。
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
        [SerializeField] private float refreshHz = 5f;

        private readonly List<Alarm> _alarms = new List<Alarm>(8);
        private Snapshot _snapshot;
        private float _nextSample;
        private float _fps = 60f;

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
            var s = new Snapshot { Fps = _fps };
            _alarms.Clear();

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

        private void BuildAlarms(ref Snapshot s)
        {
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
                _alarms.Add(new Alarm { Text = "帧质量 " + s.Quality, Level = Severity.Warn });
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