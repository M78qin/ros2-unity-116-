// 三期数据契约（版本化）：UI 与外部消费者（AI 管理端 / 论文分析脚本）只认这个结构的 JSON，
// 不直连 Unity，也不改数据链。schemaVersion 变更必须同时改 TwinUiSnapshotExport 的文件头与 README。
//
// 2026-11-09 A 支（阶段一 A）：schemaVersion 1 -> 2，新增 RT 原始 CSV（rt_raw_*，40/46 列）通道：
//   RawTcp（mm/°原始分量，坐标系与欧拉约定 UNKNOWN）、Force.Moment（N·m）、Device（设备状态原值+UNKNOWN）、
//   Validity（joints_valid/controller_time_valid）、Status（status 列真解析）、Clocks（四时钟域不混）。
//   兼容：旧字段一个未删；旧消费者继续可读，新增字段缺省为"未提供/空数组"。
//
// 设计要点：
// 1) 能力位（HasTcpPose/HasTorque/HasForce/HasVelocity）与数值分开：能力缺失时数值必须是 null/空，
//    界面显示"未订阅"，绝不显示假 0。
// 2) 每个"当前/目标/误差"三元组都用等长数组（6 关节 / 3 位置 / 3 姿态 / 6 力），缺能力时为空数组。
// 3) 所有时间戳用 UTC ISO8601 字符串，跨机比较只做帧龄，不做单向时延。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    public enum TwinUiSourceKind
    {
        None = 0,
        Runtime = 1,
        Replay = 2,
        Mock = 3
    }

    public static class TwinUiCapability
    {
        public const int JointPosition = 1 << 0;
        public const int JointVelocity = 1 << 1;
        public const int JointTorque = 1 << 2;
        public const int Force = 1 << 3;
        public const int TcpPose = 1 << 4;
        public const int ClockSync = 1 << 5;
        // A 支新增（按实际通道置位，缺通道不置位）
        public const int DeviceState = 1 << 6;
        public const int Validity = 1 << 7;
        public const int StatusColumn = 1 << 8;
        public const int RawTcpComponents = 1 << 9;

        public static bool Has(int flags, int bit)
        {
            return (flags & bit) != 0;
        }

        /// <summary>扩展能力位文本（日志/自检用；不改旧 Bits 的输出，避免旧断言基线漂移）。</summary>
        public static string ExtendedBits(int flags)
        {
            return Bits(Has(flags, TcpPose), Has(flags, JointTorque), Has(flags, Force),
                       Has(flags, JointVelocity))
                + ";rawTcp=" + (Has(flags, RawTcpComponents) ? 1 : 0)
                + ";device=" + (Has(flags, DeviceState) ? 1 : 0)
                + ";validity=" + (Has(flags, Validity) ? 1 : 0)
                + ";status=" + (Has(flags, StatusColumn) ? 1 : 0);
        }

        public static string Bits(bool tcp, bool torque, bool force, bool velocity)
        {
            return "tcp=" + (tcp ? 1 : 0) + ";torque=" + (torque ? 1 : 0)
                + ";force=" + (force ? 1 : 0) + ";velocity=" + (velocity ? 1 : 0);
        }
    }

    [Serializable]
    public sealed class TwinUiLinkMetrics
    {
        public bool Connected;
        public bool HasFrame;
        public string SourceName = string.Empty;
        public string SourceKindLabel = string.Empty;
        public double SampleHz;
        public double ApplyHz;
        public double UiHz;
        public double FreshnessMs;
        public double ReceiveToApplyMs;
        public double JitterP99Ms;
        public long DroppedFrames;
        public long OutOfOrderFrames;
        public int QueueLength;
        public string ClockSync = string.Empty;
        public string Quality = string.Empty;
        public double TargetSampleHz;
        public double TargetApplyHz;

        public string ToSummary()
        {
            return "sampleHz=" + SampleHz.ToString("F1") + " applyHz=" + ApplyHz.ToString("F1")
                + " freshnessMs=" + FreshnessMs.ToString("F1") + " recvApplyMs=" + ReceiveToApplyMs.ToString("F1")
                + " jitterP99Ms=" + JitterP99Ms.ToString("F2") + " dropped=" + DroppedFrames
                + " clock=" + ClockSync + " quality=" + Quality;
        }
    }

    [Serializable]
    public sealed class TwinUiJointState
    {
        public bool HasTarget;
        public float[] PositionDeg = new float[0];
        public float[] VelocityDegPerSec = new float[0];
        public float[] TorqueNm = new float[0];
        public float[] TargetDeg = new float[0];
        public float[] ErrorDeg = new float[0];

        public int Count => PositionDeg == null ? 0 : PositionDeg.Length;
    }

    [Serializable]
    public sealed class TwinUiPoseState
    {
        public bool HasPose;
        public bool HasTarget;
        public float[] PositionMeters = new float[0];
        public float[] RpyDeg = new float[0];
        public float[] TargetMeters = new float[0];
        public float[] TargetRpyDeg = new float[0];
        public float[] ErrorMeters = new float[0];
        public float[] ErrorRpyDeg = new float[0];
    }

    [Serializable]
    public sealed class TwinUiForceState
    {
        public bool HasForce;
        public float[] Force = new float[0];
        public float[] Target = new float[0];
        public float[] Error = new float[0];

        /// <summary>A 支：六维力/力矩的力矩三轴（N·m，ext_force_tx/ty/tz_nm 原样）。</summary>
        public bool HasMoment;
        public float[] Moment = new float[0];
    }

    /// <summary>
    /// A 支：TCP **原始分量**。mm / ° 原样显示，不做 m 换算；
    /// 坐标系（BASE/TOOL/...）、欧拉轴序与内外旋在原始数据里**没有依据**，一律 UNKNOWN，禁止推断。
    /// </summary>
    [Serializable]
    public sealed class TwinUiRawTcpState
    {
        public bool HasRawTcp;
        public float[] PositionMm = new float[0];
        public float[] AbcDeg = new float[0];
        public string Frame = "UNKNOWN";
        public string EulerConvention = "UNKNOWN";
        public string SourceColumns = string.Empty;
        public string Note = "原始分量原样显示；坐标系/欧拉轴序/内外旋无原始依据 = UNKNOWN";

        public bool Complete => PositionMm != null && PositionMm.Length >= 3
                                && AbcDeg != null && AbcDeg.Length >= 3;
    }

    /// <summary>
    /// A 支：设备状态。只保留**原始数值**（robot_mode/robot_state/control_mode/
    /// solution_space/singularity），数值语义映射无原始依据 → Semantics 固定 UNKNOWN，禁止编造。
    /// </summary>
    [Serializable]
    public sealed class TwinUiDeviceState
    {
        public bool Present;
        public string RobotMode = "UNKNOWN";
        public string RobotState = "UNKNOWN";
        public string ControlMode = "UNKNOWN";
        public string SolutionSpace = "UNKNOWN";
        public string Singularity = "UNKNOWN";
        public string Semantics = "UNKNOWN";

        public string ToCompact()
        {
            return "rm=" + RobotMode + " rs=" + RobotState + " cm=" + ControlMode
                + " ss=" + SolutionSpace + " sg=" + Singularity;
        }
    }

    /// <summary>A 支：有效位。Provided=false 表示源文件没有该列（不等于 False）。</summary>
    [Serializable]
    public sealed class TwinUiValidity
    {
        public bool JointsValidProvided;
        public bool JointsValid;
        public bool ControllerTimeValidProvided;
        public bool ControllerTimeValid;

        public string ToText()
        {
            return "Jv=" + (JointsValidProvided ? (JointsValid ? "True" : "False") : "未提供")
                + " CTv=" + (ControllerTimeValidProvided ? (ControllerTimeValid ? "True" : "False") : "未提供");
        }
    }

    /// <summary>
    /// A 支：`status` 列真解析结果。分类只做**原值到语义**的明确映射：
    /// accepted→正常；identity_mismatch / source_changed / out_of_order→非正常；
    /// 未知值→UNKNOWN（原样显示，绝不归入正常）；无该列→未提供（不标正常）。
    /// </summary>
    [Serializable]
    public sealed class TwinUiStatusInfo
    {
        public bool Provided;
        public string Raw = string.Empty;
        public string Classification = "NotProvided";
        public bool IsNormal;
        /// <summary>"未提供"的原因：无该列 / 0 数据行（不能把"0 数据行"说成"无 status 列"）。</summary>
        public string NotProvidedReason = "无 status 列";

        public string Display
        {
            get
            {
                if (!Provided)
                {
                    return TwinUITextR3.CtxNotProvided + "(" + NotProvidedReason + ")";
                }

                if (IsNormal)
                {
                    return "正常(status=" + Raw + ")";
                }

                if (Classification == "UnknownValue")
                {
                    return "UNKNOWN(status=" + Raw + ")";
                }

                return "异常(status=" + Raw + ")";
            }
        }

        public static TwinUiStatusInfo From(string raw)
        {
            var s = new TwinUiStatusInfo();
            if (string.IsNullOrEmpty(raw))
            {
                return s;
            }

            s.Provided = true;
            s.Raw = raw;
            switch (raw)
            {
                case "accepted":
                    s.Classification = "Accepted";
                    s.IsNormal = true;
                    break;
                case "identity_mismatch":
                    s.Classification = "IdentityMismatch";
                    break;
                case "source_changed":
                    s.Classification = "SourceChanged";
                    break;
                case "out_of_order":
                    s.Classification = "OutOfOrder";
                    break;
                default:
                    s.Classification = "UnknownValue";
                    break;
            }

            return s;
        }
    }

    /// <summary>
    /// A 支：四个时钟域**不混**。回放时间轴用 monotonic_ns（宿主单调钟，不随 NTP 跳变），
    /// 缺失时回退 ros_time_ns，再回退 controller_time（并注明）。controller_time 是否可用要看会话。
    /// </summary>
    [Serializable]
    public sealed class TwinUiClockDomains
    {
        public bool HasControllerTime;
        public bool HasRosTime;
        public bool HasHostWall;
        public bool HasMonotonic;
        public double ControllerTimeSpanSeconds;
        public long ControllerTimeDistinct;
        public double RosSpanSeconds;
        public double HostWallSpanSeconds;
        public double MonotonicSpanSeconds;
        public string TimeAxis = "monotonic_ns";
        public string TimeAxisReason = string.Empty;
        public string Note = string.Empty;

        /// <summary>
        /// 文本口径：**没有该列就写"未提供"，绝不补 0**（samples.csv 只有 unity_event_ns，
        /// 不能显示 mono/ros/wall/ctl = 0.000s，那等于伪造数据）。
        /// </summary>
        public string ToText()
        {
            return "axis=" + TimeAxis
                + " mono=" + (HasMonotonic ? MonotonicSpanSeconds.ToString("F3") + "s" : TwinUITextR3.CtxNotProvided)
                + " ros=" + (HasRosTime ? RosSpanSeconds.ToString("F3") + "s" : TwinUITextR3.CtxNotProvided)
                + " wall=" + (HasHostWall ? HostWallSpanSeconds.ToString("F3") + "s" : TwinUITextR3.CtxNotProvided)
                + " ctl=" + (HasControllerTime
                    ? ControllerTimeSpanSeconds.ToString("F3") + "s/" + ControllerTimeDistinct + " 值"
                    : TwinUITextR3.CtxNotProvided);
        }
    }

    [Serializable]
    public sealed class TwinUiExperimentContext
    {
        public string RunId = string.Empty;
        public string Profile = string.Empty;
        public string Condition = string.Empty;
        public string PayloadKg = string.Empty;
        public string TcpDefinition = string.Empty;
        public string SessionId = string.Empty;
        public string SelfCheckStatus = string.Empty;
        public bool SelfCheckPassed;
    }

    [Serializable]
    public sealed class TwinUiAlert
    {
        public string TimeUtc = string.Empty;
        public string Level = "Info";     // Info / Warn / Crit
        public string Code = string.Empty;
        public string Text = string.Empty;
        public string Basis = string.Empty; // 判据/阈值原文，便于论文引用
        public int Tone;                    // 0 Unknown / 1 Ok / 2 Warn / 3 Crit

        public static TwinUiAlert Make(TwinUITone tone, string code, string text, string basis)
        {
            return new TwinUiAlert
            {
                TimeUtc = DateTime.UtcNow.ToString("O"),
                Level = tone == TwinUITone.Crit ? "Crit" : tone == TwinUITone.Warn ? "Warn" : tone == TwinUITone.Ok ? "Ok" : "Info",
                Code = code ?? string.Empty,
                Text = text ?? string.Empty,
                Basis = basis ?? string.Empty,
                Tone = (int)tone
            };
        }
    }

    [Serializable]
    public sealed class TwinUiSnapshot
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;
        public string Producer = "Unity_Ros_Try1/DigitalTwin.UI";
        public string ProducedAtUtc = string.Empty;
        public string ProducedAtLocal = string.Empty;

        public TwinUiSourceKind SourceKind = TwinUiSourceKind.None;
        public string SourceLabel = string.Empty;
        public bool IsMock;
        public bool IsReplay;
        public string Watermark = string.Empty;
        public string Notes = string.Empty;

        public int Capabilities;
        public TwinUiLinkMetrics Link = new TwinUiLinkMetrics();
        public TwinUiJointState Joints = new TwinUiJointState();
        public TwinUiPoseState Pose = new TwinUiPoseState();
        public TwinUiForceState Force = new TwinUiForceState();
        public TwinUiExperimentContext Experiment = new TwinUiExperimentContext();
        public List<TwinUiAlert> Alerts = new List<TwinUiAlert>();

        // ---- A 支（schemaVersion 2）新增：RT 原始 CSV 通道 + 状态/有效位/时钟域 ----
        public TwinUiRawTcpState RawTcp = new TwinUiRawTcpState();
        public TwinUiDeviceState Device = new TwinUiDeviceState();
        public TwinUiValidity Validity = new TwinUiValidity();
        public TwinUiStatusInfo Status = new TwinUiStatusInfo();
        public TwinUiClockDomains Clocks = new TwinUiClockDomains();

        /// <summary>回放源身份（run_id）与"目录名是否与行内 run_id 一致"的可复核标记。</summary>
        public string SourceRunId = string.Empty;
        public string SourceSessionId = string.Empty;
        public bool IdentityUnique = true;
        public string IdentityNote = string.Empty;

        public bool HasAnyData => Link.HasFrame;

        public static TwinUiSnapshot Empty(TwinUiSourceKind kind, string watermark)
        {
            var s = new TwinUiSnapshot
            {
                SourceKind = kind,
                SourceLabel = kind == TwinUiSourceKind.Replay ? TwinUITextR3.CtxReplay
                    : kind == TwinUiSourceKind.Mock ? TwinUITextR3.CtxMock
                    : kind == TwinUiSourceKind.Runtime ? TwinUITextR3.CtxRuntime
                    : TwinUITextR3.NoSource,
                IsMock = kind == TwinUiSourceKind.Mock,
                IsReplay = kind == TwinUiSourceKind.Replay,
                Watermark = watermark ?? string.Empty,
                ProducedAtUtc = DateTime.UtcNow.ToString("O"),
                ProducedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            s.Link.SourceKindLabel = s.SourceLabel;
            return s;
        }

        public void Touch()
        {
            SchemaVersion = CurrentSchemaVersion;
            ProducedAtUtc = DateTime.UtcNow.ToString("O");
            ProducedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public string ToJson(bool pretty)
        {
            return JsonUtility.ToJson(this, pretty);
        }

        public string ToCompactLine()
        {
            var link = Link;
            return "schema=" + SchemaVersion + " src=" + SourceKind + " hasFrame=" + (link.HasFrame ? 1 : 0)
                + " " + link.ToSummary() + " caps=" + TwinUiCapability.Bits(
                    TwinUiCapability.Has(Capabilities, TwinUiCapability.TcpPose),
                    TwinUiCapability.Has(Capabilities, TwinUiCapability.JointTorque),
                    TwinUiCapability.Has(Capabilities, TwinUiCapability.Force),
                    TwinUiCapability.Has(Capabilities, TwinUiCapability.JointVelocity))
                + " alerts=" + (Alerts == null ? 0 : Alerts.Count);
        }
    }
}