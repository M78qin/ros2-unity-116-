// 三期数据源抽象与两个实现（Runtime / Mock）；Replay 见 ReplayTwinUiDataSource.cs。
//
// 约束：三实现都只读既有数据链，不修改 Ros2Bridge / TwinRecorder / DigitalTwinRuntime / SafetyCheckService，
// 也不产生新的下发路径。Mock 必须自带 MOCK 水印，任何视图都要显示它。
using System;
using UnityEngine;

namespace DigitalTwin.UI
{
    public interface ITwinUiDataSource
    {
        TwinUiSourceKind Kind { get; }
        string Label { get; }
        bool IsMock { get; }
        bool IsReplay { get; }

        /// <summary>一行人类可读的状态（进快照/审计日志）。</summary>
        string StatusLine { get; }

        TwinUiSnapshot Snapshot { get; }

        /// <summary>按 settings 采样/推进一次。</summary>
        void Refresh(TwinUiSettings settings);

        void Reset();
    }

    /// <summary>真实帧数据源：读 TwinUIStatusBinder（只读快照）+ 最新帧的目标角与 TCP。</summary>
    public sealed class RuntimeTwinUiDataSource : ITwinUiDataSource
    {
        private readonly TwinUIStatusBinder _binder;
        private readonly DigitalTwinRuntime _runtime;
        private TwinUiSnapshot _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Runtime, string.Empty);

        public RuntimeTwinUiDataSource(TwinUIStatusBinder binder, DigitalTwinRuntime runtime)
        {
            _binder = binder;
            _runtime = runtime;
            Label = TwinUITextR3.CtxRuntime;
        }

        public TwinUiSourceKind Kind => TwinUiSourceKind.Runtime;
        public string Label { get; private set; }
        public bool IsMock => false;
        public bool IsReplay => false;
        public string StatusLine { get; private set; } = "runtime: waiting for frames";
        public TwinUiSnapshot Snapshot => _snapshot;

        public void Reset()
        {
            _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Runtime, string.Empty);
        }

        public void Refresh(TwinUiSettings settings)
        {
            if (_binder == null)
            {
                _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Runtime, string.Empty);
                _snapshot.Link.SourceName = TwinUITextR3.NoSource;
                _snapshot.Link.SourceKindLabel = Label;
                StatusLine = "runtime: no binder";
                return;
            }

            _binder.Sample();
            var b = _binder.Current;
            var snap = TwinUiSnapshot.Empty(TwinUiSourceKind.Runtime, string.Empty);
            snap.Link.SourceName = b.SourceName;
            snap.Link.SourceKindLabel = Label;
            snap.Link.Connected = b.Connected;
            snap.Link.HasFrame = b.HasFrame;
            snap.Link.SampleHz = b.Hz;
            snap.Link.ApplyHz = _runtime != null && _runtime.LatestMetrics != null
                ? _runtime.LatestMetrics.UpdateRateHz : 0d;
            snap.Link.UiHz = settings != null ? settings.uiRefreshHz : 0d;
            snap.Link.FreshnessMs = b.AgeMs;
            snap.Link.ReceiveToApplyMs = b.LatencyMs;
            snap.Link.JitterP99Ms = b.JitterMs;
            snap.Link.DroppedFrames = b.Dropped;
            snap.Link.OutOfOrderFrames = b.OutOfOrder;
            snap.Link.QueueLength = b.Queue;
            snap.Link.ClockSync = string.IsNullOrEmpty(b.Clock) ? "Unknown" : b.Clock;
            snap.Link.Quality = string.IsNullOrEmpty(b.Quality) ? "Unknown" : b.Quality;
            snap.Link.TargetSampleHz = 100d;
            snap.Link.TargetApplyHz = settings != null ? settings.robotApplyRateHz : 60d;

            RobotStateFrame frame = default;
            bool hasFrame = _runtime != null && _runtime.TryGetLatestFrame(out frame);
            int caps = 0;
            if (hasFrame)
            {
                if (frame.JointPositionRad != null && frame.JointPositionRad.Length > 0)
                {
                    caps |= TwinUiCapability.JointPosition;
                    var pos = new float[frame.JointPositionRad.Length];
                    for (int i = 0; i < pos.Length; i++)
                    {
                        pos[i] = frame.JointPositionRad[i] * Mathf.Rad2Deg;
                    }

                    snap.Joints.PositionDeg = pos;
                }

                if (frame.JointVelocityRad != null && frame.JointVelocityRad.Length > 0)
                {
                    caps |= TwinUiCapability.JointVelocity;
                    var vel = new float[frame.JointVelocityRad.Length];
                    for (int i = 0; i < vel.Length; i++)
                    {
                        vel[i] = frame.JointVelocityRad[i] * Mathf.Rad2Deg;
                    }

                    snap.Joints.VelocityDegPerSec = vel;
                }

                if (frame.JointTorqueNm != null && frame.JointTorqueNm.Length > 0)
                {
                    caps |= TwinUiCapability.JointTorque;
                    snap.Joints.TorqueNm = Copy(frame.JointTorqueNm);
                }

                if (frame.ForceVector != null && frame.ForceVector.Length > 0)
                {
                    caps |= TwinUiCapability.Force;
                    snap.Force.HasForce = true;
                    snap.Force.Force = Copy(frame.ForceVector);
                    snap.Force.Target = new float[frame.ForceVector.Length];
                    snap.Force.Error = Copy(frame.ForceVector);
                }

                if (frame.ModelTargetDisplayDeg != null && frame.ModelTargetDisplayDeg.Length > 0)
                {
                    snap.Joints.HasTarget = true;
                    snap.Joints.TargetDeg = Copy(frame.ModelTargetDisplayDeg);
                    snap.Joints.ErrorDeg = Diff(snap.Joints.PositionDeg, frame.ModelTargetDisplayDeg);
                }

                if (frame.HasTcpPose)
                {
                    caps |= TwinUiCapability.TcpPose;
                    snap.Pose.HasPose = true;
                    var p = frame.TcpPositionMeters;
                    var e = frame.TcpRotation.eulerAngles;
                    snap.Pose.PositionMeters = new[] { p.x, p.y, p.z };
                    snap.Pose.RpyDeg = new[] { Wrap(e.x), Wrap(e.y), Wrap(e.z) };
                    snap.Pose.HasTarget = false;
                }

                if (frame.ClockSyncStatus == TwinClockSyncStatus.Synced)
                {
                    caps |= TwinUiCapability.ClockSync;
                }
            }

            snap.Capabilities = caps;
            snap.Experiment = TwinUiExperimentContextFactory.FromRuntime(_runtime, _binder);
            _snapshot = snap;
            StatusLine = "runtime: connected=" + b.Connected + " hasFrame=" + b.HasFrame
                + " hz=" + b.Hz.ToString("F1") + " age=" + b.AgeMs.ToString("F0") + "ms";
        }

        internal static float[] Copy(float[] src)
        {
            if (src == null)
            {
                return new float[0];
            }

            var dst = new float[src.Length];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        private static float[] Diff(float[] a, float[] b)
        {
            if (a == null || b == null)
            {
                return new float[0];
            }

            int n = Mathf.Min(a.Length, b.Length);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                d[i] = a[i] - b[i];
            }

            return d;
        }

        private static float Wrap(float deg)
        {
            deg %= 360f;
            if (deg > 180f)
            {
                deg -= 360f;
            }

            return deg;
        }
    }

    /// <summary>演示数据源：合成正弦轨迹。必须打 MOCK 水印，且绝不能被当作现场数据。</summary>
    public sealed class MockTwinUiDataSource : ITwinUiDataSource
    {
        private double _t;
        private TwinUiSnapshot _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Mock, TwinUITextR3.MockWatermark);
        private readonly System.Random _random = new System.Random(20261104);

        public TwinUiSourceKind Kind => TwinUiSourceKind.Mock;
        public string Label => TwinUITextR3.CtxMock;
        public bool IsMock => true;
        public bool IsReplay => false;
        public string StatusLine => "mock: synthetic sine (not measured data)";
        public TwinUiSnapshot Snapshot => _snapshot;

        public void Reset()
        {
            _t = 0d;
            _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Mock, TwinUITextR3.MockWatermark);
        }

        public void Refresh(TwinUiSettings settings)
        {
            double dt = 1d / 30d;
            _t += dt;
            var snap = TwinUiSnapshot.Empty(TwinUiSourceKind.Mock, TwinUITextR3.MockWatermark);
            snap.Notes = "synthetic demo; never cite as measurement";
            snap.Link.Connected = true;
            snap.Link.HasFrame = true;
            snap.Link.SourceName = "MOCK";
            snap.Link.SourceKindLabel = Label;
            // 100 Hz 采样、30 Hz 应用，抖动/时延按演示值给（不作为任何结论依据）
            snap.Link.SampleHz = 99.2 + Math.Sin(_t * 0.7) * 1.3;
            snap.Link.ApplyHz = 29.6 + Math.Sin(_t * 0.4) * 0.6;
            snap.Link.UiHz = settings != null ? settings.uiRefreshHz : 10d;
            snap.Link.FreshnessMs = 42d + 12d * Math.Sin(_t * 3d);
            snap.Link.ReceiveToApplyMs = 4.1 + 0.8 * Math.Sin(_t * 5d);
            snap.Link.JitterP99Ms = 1.35 + 0.4 * Math.Sin(_t * 1.7d);
            snap.Link.DroppedFrames = 0;
            snap.Link.OutOfOrderFrames = 0;
            snap.Link.QueueLength = 0;
            snap.Link.ClockSync = "Synced";
            snap.Link.Quality = "Normal";
            snap.Link.TargetSampleHz = 100d;
            snap.Link.TargetApplyHz = settings != null ? settings.robotApplyRateHz : 60d;

            var pos = new float[6];
            var target = new float[6];
            var vel = new float[6];
            var torque = new float[6];
            for (int i = 0; i < 6; i++)
            {
                double amp = 18d + i * 4d;
                double phase = _t * (0.8d + i * 0.12d) + i;
                target[i] = (float)(amp * Math.Sin(phase));
                pos[i] = (float)(amp * Math.Sin(phase - 0.035d));
                vel[i] = (float)(amp * Math.Cos(phase) * 0.8d);
                torque[i] = (float)(Math.Sin(phase * 1.3d) * (6d + i));
            }

            snap.Joints.HasTarget = true;
            snap.Joints.PositionDeg = pos;
            snap.Joints.TargetDeg = target;
            snap.Joints.VelocityDegPerSec = vel;
            snap.Joints.TorqueNm = torque;
            snap.Joints.ErrorDeg = new float[6];
            for (int i = 0; i < 6; i++)
            {
                snap.Joints.ErrorDeg[i] = pos[i] - target[i];
            }

            snap.Pose.HasPose = true;
            snap.Pose.HasTarget = true;
            float px = (float)(0.52 + 0.06 * Math.Sin(_t * 0.5d));
            float py = (float)(-0.08 + 0.04 * Math.Cos(_t * 0.6d));
            float pz = (float)(0.31 + 0.03 * Math.Sin(_t * 0.9d));
            snap.Pose.PositionMeters = new[] { px, py, pz };
            snap.Pose.RpyDeg = new[] { (float)(1.2 * Math.Sin(_t)), (float)(179.4 + 0.6 * Math.Cos(_t)), (float)(-0.8 * Math.Sin(_t * 1.4d)) };
            snap.Pose.TargetMeters = new[] { px + 0.0015f, py - 0.0009f, pz + 0.0007f };
            snap.Pose.TargetRpyDeg = new[] { 0.4f, 179.8f, -0.2f };
            snap.Pose.ErrorMeters = new float[3];
            snap.Pose.ErrorRpyDeg = new float[3];
            for (int i = 0; i < 3; i++)
            {
                snap.Pose.ErrorMeters[i] = snap.Pose.PositionMeters[i] - snap.Pose.TargetMeters[i];
                snap.Pose.ErrorRpyDeg[i] = snap.Pose.RpyDeg[i] - snap.Pose.TargetRpyDeg[i];
            }

            snap.Force.HasForce = true;
            snap.Force.Force = new[]
            {
                (float)(2.4 * Math.Sin(_t * 1.1d)) + 4f, (float)(1.6 * Math.Cos(_t * 0.9d)) - 2f,
                (float)(18d + 6d * Math.Sin(_t * 0.7d)), (float)(0.4 * Math.Sin(_t * 1.5d)),
                (float)(0.3 * Math.Cos(_t * 1.2d)), (float)(0.2 * Math.Sin(_t * 0.8d))
            };
            snap.Force.Target = new float[6];
            snap.Force.Error = new float[6];
            for (int i = 0; i < 6; i++)
            {
                snap.Force.Error[i] = snap.Force.Force[i] - snap.Force.Target[i];
            }

            snap.Capabilities = TwinUiCapability.JointPosition | TwinUiCapability.JointVelocity
                | TwinUiCapability.JointTorque | TwinUiCapability.Force | TwinUiCapability.TcpPose
                | TwinUiCapability.ClockSync;
            snap.Experiment = new TwinUiExperimentContext
            {
                RunId = "MOCK-DEMO",
                Profile = "mock",
                Condition = "demo",
                PayloadKg = TwinUITextR3.CtxNotProvided,
                TcpDefinition = "mock-tcp",
                SessionId = "mock-session",
                SelfCheckStatus = "MOCK",
                SelfCheckPassed = false
            };
            _snapshot = snap;
        }
    }

    /// <summary>实验上下文：运行时从既有组件只读取值；回放从 run 目录的 profile.json 取值。</summary>
    public static class TwinUiExperimentContextFactory
    {
        public static TwinUiExperimentContext FromRuntime(DigitalTwinRuntime runtime, TwinUIStatusBinder binder)
        {
            var ctx = new TwinUiExperimentContext
            {
                RunId = TwinUITextR3.CtxNotProvided,
                Profile = TwinUITextR3.CtxNotProvided,
                Condition = TwinUITextR3.CtxNotProvided,
                PayloadKg = TwinUITextR3.CtxNotProvided,
                TcpDefinition = TwinUITextR3.CtxNotProvided,
                SessionId = TwinUITextR3.CtxNotProvided,
                SelfCheckStatus = TwinUITextR3.CtxNotProvided
            };

            bool hasFrame = false;
            RobotStateFrame frame = null;
            if (runtime != null && runtime.TryGetLatestFrame(out frame))
            {
                hasFrame = true;
            }

            if (hasFrame && frame != null)
            {
                if (!string.IsNullOrEmpty(frame.RunId))
                {
                    ctx.RunId = frame.RunId;
                }

                if (!string.IsNullOrEmpty(frame.ExperimentId))
                {
                    ctx.Condition = frame.ExperimentId;
                }

                if (!string.IsNullOrEmpty(frame.SourceSessionId))
                {
                    ctx.SessionId = frame.SourceSessionId;
                }
            }

            if (binder != null)
            {
                var b = binder.Current;
                if (string.IsNullOrEmpty(ctx.SessionId) && !string.IsNullOrEmpty(b.SessionId))
                {
                    ctx.SessionId = b.SessionId;
                }

                ctx.SelfCheckStatus = b.Recording ? "RECORDING" : (b.CsvReady ? "CSV-READY" : "IDLE");
                ctx.SelfCheckPassed = b.CsvReady;
            }

            return ctx;
        }
    }
}