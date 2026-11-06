// 三期数据契约（版本化）：UI 与外部消费者（AI 管理端 / 论文分析脚本）只认这个结构的 JSON，
// 不直连 Unity，也不改数据链。schemaVersion 变更必须同时改 TwinUiSnapshotExport 的文件头与 README。
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

        public static bool Has(int flags, int bit)
        {
            return (flags & bit) != 0;
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
        public const int CurrentSchemaVersion = 1;

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