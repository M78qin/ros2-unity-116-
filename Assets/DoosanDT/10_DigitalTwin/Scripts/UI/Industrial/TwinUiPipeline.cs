// 阈值求值与告警流水线：把 TwinUiSnapshot 变成"每项的色温 + 告警列表 + 事件边沿"。
// 只做只读求值：不写数据链、不发命令。阈值来自 TwinUiSettings.thresholds（全部"待校准"）。
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiPipeline
    {
        public const string KeySample = "SAMPLE_HZ";
        public const string KeyApply = "APPLY_HZ";
        public const string KeyFresh = "FRESHNESS";
        public const string KeyLatency = "RECV_APPLY";
        public const string KeyJitter = "JITTER_P99";
        public const string KeyDrop = "DROPPED";
        public const string KeyClock = "CLOCK_SYNC";
        public const string KeyQuality = "FRAME_QUALITY";
        public const string KeyJointError = "JOINT_ERROR";
        public const string KeyTorque = "JOINT_TORQUE";
        public const string KeyForce = "WRENCH";
        public const string KeyPose = "TCP_POSE";
        public const string KeyData = "DATA_PRESENCE";

        private readonly Dictionary<string, TwinUITone> _tones = new Dictionary<string, TwinUITone>(16);
        private readonly Dictionary<string, TwinUITone> _previous = new Dictionary<string, TwinUITone>(16);
        private readonly List<TwinUiAlert> _alerts = new List<TwinUiAlert>(16);
        private bool _primed;

        public TwinUiSettings Settings { get; private set; }
        public ITwinUiDataSource Source { get; private set; }
        public TwinUiEventBus Events { get; private set; }
        public TwinUiSnapshotExport Export { get; private set; }
        public TwinUiSnapshot Snapshot { get; private set; }
        public IReadOnlyList<TwinUiAlert> Alerts => _alerts;
        public int RefreshCount { get; private set; }
        public float LastRefreshSeconds { get; private set; }

        public TwinUiThresholds Thresholds =>
            Settings != null ? Settings.thresholds : new TwinUiThresholds();

        public void Bind(TwinUiSettings settings, ITwinUiDataSource source, TwinUiEventBus events,
            TwinUiSnapshotExport export)
        {
            Settings = settings;
            Source = source;
            Events = events;
            Export = export;
            if (Export != null)
            {
                Export.Configure(settings);
            }

            Snapshot = source != null ? source.Snapshot : TwinUiSnapshot.Empty(TwinUiSourceKind.None, string.Empty);
            _tones.Clear();
            _previous.Clear();
            _alerts.Clear();
            _primed = false;
        }

        public void SetSource(ITwinUiDataSource source, string reason)
        {
            Source = source;
            _previous.Clear();
            _primed = false;
            if (Events != null)
            {
                Events.Publish("session", TwinUITone.Ok, "SOURCE_CHANGE",
                    "source -> " + (source != null ? source.Label : "none") + " (" + reason + ")");
            }
        }

        public TwinUITone ToneOf(string key)
        {
            return _tones.TryGetValue(key, out var tone) ? tone : TwinUITone.Unknown;
        }

        public void Refresh()
        {
            if (Source == null)
            {
                Snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.None, string.Empty);
                EvaluateEmpty();
                return;
            }

            Source.Refresh(Settings);
            Snapshot = Source.Snapshot ?? TwinUiSnapshot.Empty(Source.Kind, string.Empty);
            Snapshot.Touch();
            Evaluate(Snapshot);
            RefreshCount++;
            LastRefreshSeconds = Time.realtimeSinceStartup;

            if (Export != null)
            {
                Export.MaybeWrite(Snapshot, Settings, Time.realtimeSinceStartup);
            }
        }

        public void StepAndRefresh(double seconds)
        {
            var replay = Source as ReplayTwinUiDataSource;
            if (replay != null)
            {
                replay.Advance(seconds);
            }

            Refresh();
        }

        private void EvaluateEmpty()
        {
            _tones.Clear();
            _alerts.Clear();
            foreach (string key in new[] { KeySample, KeyApply, KeyFresh, KeyLatency, KeyJitter, KeyDrop, KeyClock, KeyQuality, KeyJointError, KeyTorque, KeyForce, KeyPose, KeyData })
            {
                _tones[key] = TwinUITone.Unknown;
            }

            _alerts.Add(TwinUiAlert.Make(TwinUITone.Unknown, "NO_SOURCE", "无数据源", "-"));
        }

        private void Evaluate(TwinUiSnapshot s)
        {
            var t = Thresholds;
            var link = s.Link;
            _tones.Clear();
            _alerts.Clear();

            _tones[KeyData] = link.HasFrame ? TwinUITone.Ok : TwinUITone.Unknown;
            _tones[KeySample] = link.HasFrame && link.SampleHz > 0d ? t.SampleHz(link.SampleHz) : TwinUITone.Unknown;
            _tones[KeyApply] = link.HasFrame && link.ApplyHz > 0d ? t.ApplyHz(link.ApplyHz) : TwinUITone.Unknown;
            _tones[KeyFresh] = link.HasFrame ? t.Freshness(link.FreshnessMs) : TwinUITone.Unknown;
            _tones[KeyLatency] = link.HasFrame && link.ReceiveToApplyMs > 0d
                ? t.ReceiveToApply(link.ReceiveToApplyMs) : TwinUITone.Unknown;
            _tones[KeyJitter] = link.HasFrame ? t.Jitter(link.JitterP99Ms) : TwinUITone.Unknown;
            _tones[KeyDrop] = link.HasFrame ? TwinUiThresholds.ZeroIsBest(link.DroppedFrames, 0, 5) : TwinUITone.Unknown;
            _tones[KeyClock] = ToneForClock(link.ClockSync, s);
            _tones[KeyQuality] = ToneForQuality(link.Quality);

            // 关节位置误差：取最大绝对值
            double maxErr = 0d;
            bool hasErr = false;
            if (s.Joints.ErrorDeg != null && s.Joints.ErrorDeg.Length > 0)
            {
                for (int i = 0; i < s.Joints.ErrorDeg.Length; i++)
                {
                    double v = Mathf.Abs(s.Joints.ErrorDeg[i]);
                    if (!hasErr || v > maxErr)
                    {
                        maxErr = v;
                    }

                    hasErr = true;
                }
            }

            _tones[KeyJointError] = hasErr ? t.PositionError(maxErr) : TwinUITone.Unknown;

            double maxRatio = 0d;
            bool hasTorque = false;
            if (s.Joints.TorqueNm != null && s.Joints.TorqueNm.Length > 0 && t.torqueRatedNm > 0.001d)
            {
                for (int i = 0; i < s.Joints.TorqueNm.Length; i++)
                {
                    double ratio = Mathf.Abs(s.Joints.TorqueNm[i]) / t.torqueRatedNm;
                    if (!hasTorque || ratio > maxRatio)
                    {
                        maxRatio = ratio;
                    }

                    hasTorque = true;
                }
            }

            _tones[KeyTorque] = hasTorque ? t.TorqueRatio(maxRatio) : TwinUITone.Unknown;

            double maxForce = 0d;
            bool hasForce = false;
            if (s.Force.HasForce && s.Force.Force != null)
            {
                for (int i = 0; i < s.Force.Force.Length && i < 3; i++)
                {
                    double v = Mathf.Abs(s.Force.Force[i]);
                    if (!hasForce || v > maxForce)
                    {
                        maxForce = v;
                    }

                    hasForce = true;
                }
            }

            _tones[KeyForce] = hasForce ? t.Force(maxForce) : TwinUITone.Unknown;

            double maxPoseErr = 0d;
            bool hasPoseErr = false;
            if (s.Pose.HasPose && s.Pose.HasTarget && s.Pose.ErrorMeters != null)
            {
                for (int i = 0; i < s.Pose.ErrorMeters.Length; i++)
                {
                    double v = Mathf.Abs(s.Pose.ErrorMeters[i]) * 1000d; // mm
                    if (!hasPoseErr || v > maxPoseErr)
                    {
                        maxPoseErr = v;
                    }

                    hasPoseErr = true;
                }
            }

            _tones[KeyPose] = hasPoseErr ? TwinUITone.Ok : TwinUITone.Unknown;

            BuildAlerts(s, t, maxErr, hasErr, maxRatio, hasTorque, maxForce, hasForce);
            DetectEdges(s);
            _primed = true;
        }

        private static TwinUITone ToneForClock(string clock, TwinUiSnapshot s)
        {
            if (string.IsNullOrEmpty(clock) || clock == "Unknown" || clock == TwinUITextR3.CtxNotProvided
                || clock == TwinUITextR3.NoData)
            {
                return TwinUITone.Unknown;
            }

            if (clock == "Unsynced")
            {
                return TwinUITone.Warn;
            }

            return clock == "Synced" ? TwinUITone.Ok : TwinUITone.Unknown;
        }

        private static TwinUITone ToneForQuality(string quality)
        {
            if (string.IsNullOrEmpty(quality))
            {
                return TwinUITone.Unknown;
            }

            if (quality.StartsWith("Normal"))
            {
                return TwinUITone.Ok;
            }

            return quality == "Lost" ? TwinUITone.Crit : TwinUITone.Warn;
        }

        private void BuildAlerts(TwinUiSnapshot s, TwinUiThresholds t, double maxErr, bool hasErr,
            double maxRatio, bool hasTorque, double maxForce, bool hasForce)
        {
            AddIf(KeyData, s.Link.HasFrame ? TwinUITone.Ok : TwinUITone.Unknown,
                "NO_FRAME", "无实时帧", "连接/帧龄判据");
            AddIf(KeySample, _tones[KeySample], "SAMPLE_HZ",
                "采样 " + s.Link.SampleHz.ToString("F1") + " Hz", "黄<" + t.sampleHzWarn + " / 红<" + t.sampleHzCrit);
            AddIf(KeyApply, _tones[KeyApply], "APPLY_HZ",
                "应用 " + s.Link.ApplyHz.ToString("F1") + " Hz", "黄<" + t.applyHzWarn + " / 红<" + t.applyHzCrit);
            AddIf(KeyFresh, _tones[KeyFresh], "FRESHNESS",
                "新鲜度 " + s.Link.FreshnessMs.ToString("F0") + " ms", "黄>" + t.freshnessWarnMs + " / 红>" + t.freshnessCritMs);
            AddIf(KeyLatency, _tones[KeyLatency], "RECV_APPLY",
                "接收->应用 " + s.Link.ReceiveToApplyMs.ToString("F1") + " ms",
                "黄>" + t.receiveToApplyWarnMs + " / 红>" + t.receiveToApplyCritMs);
            AddIf(KeyJitter, _tones[KeyJitter], "JITTER_P99",
                "抖动 p99 " + s.Link.JitterP99Ms.ToString("F2") + " ms", "黄>" + t.jitterP99WarnMs + " / 红>" + t.jitterP99CritMs);
            AddIf(KeyDrop, _tones[KeyDrop], "DROPPED",
                "丢帧 " + s.Link.DroppedFrames, "黄>0 / 红>5");
            AddIf(KeyClock, _tones[KeyClock], "CLOCK_SYNC", "时钟 " + s.Link.ClockSync, "同步=SYNCED");
            AddIf(KeyQuality, _tones[KeyQuality], "FRAME_QUALITY", "帧质量 " + s.Link.Quality, "Normal 为正常");

            if (hasErr)
            {
                AddIf(KeyJointError, _tones[KeyJointError], "JOINT_ERROR",
                    "关节误差最大 " + maxErr.ToString("F2") + " °",
                    "黄>" + t.positionErrorWarnDeg + " / 红>" + t.positionErrorCritDeg);
            }
            else
            {
                _alerts.Add(TwinUiAlert.Make(TwinUITone.Unknown, "JOINT_ERROR", "关节误差 未订阅", "CSV/帧无目标角"));
            }

            if (hasTorque)
            {
                AddIf(KeyTorque, _tones[KeyTorque], "JOINT_TORQUE",
                    "力矩最大 " + (maxRatio * 100d).ToString("F0") + "% 额定",
                    "黄>" + (t.torqueWarnRatio * 100d) + "% / 红>" + (t.torqueCritRatio * 100d) + "%");
            }
            else
            {
                _alerts.Add(TwinUiAlert.Make(TwinUITone.Unknown, "JOINT_TORQUE", "关节力矩 未订阅", "帧无力矩通道"));
            }

            if (hasForce)
            {
                AddIf(KeyForce, _tones[KeyForce], "WRENCH",
                    "力最大 " + maxForce.ToString("F1") + " N", "黄>" + t.forceWarnN + " / 红>" + t.forceCritN);
            }
            else
            {
                _alerts.Add(TwinUiAlert.Make(TwinUITone.Unknown, "WRENCH", "六维力 未订阅", "帧无力通道"));
            }

            if (!s.Pose.HasPose)
            {
                _alerts.Add(TwinUiAlert.Make(TwinUITone.Unknown, "TCP_POSE", "TCP 位姿 未订阅", "帧无 HasTcpPose"));
            }
        }

        private void AddIf(string key, TwinUITone tone, string code, string text, string basis)
        {
            if (tone == TwinUITone.Ok || tone == TwinUITone.Unknown)
            {
                return;
            }

            _alerts.Add(TwinUiAlert.Make(tone, code, text, basis));
        }

        private void DetectEdges(TwinUiSnapshot s)
        {
            bool changed = false;
            foreach (var pair in _tones)
            {
                TwinUITone prev = _previous.TryGetValue(pair.Key, out var p) ? p : TwinUITone.Unknown;
                TwinUITone now = pair.Value;
                if (now == prev)
                {
                    continue;
                }

                changed = true;
                if (!_primed)
                {
                    continue;
                }

                if (now == TwinUITone.Warn || now == TwinUITone.Crit)
                {
                    Publish("alert", now, pair.Key, DescribeKey(pair.Key, s) + " -> " + TwinUITheme.ToneLabel(now));
                }
                else if (now == TwinUITone.Ok && (prev == TwinUITone.Warn || prev == TwinUITone.Crit))
                {
                    Publish("recover", TwinUITone.Ok, pair.Key, DescribeKey(pair.Key, s) + " -> 恢复正常");
                }
            }

            if (changed || !_primed)
            {
                _previous.Clear();
                foreach (var pair in _tones)
                {
                    _previous[pair.Key] = pair.Value;
                }
            }

            if (!_primed && Events != null)
            {
                Events.Publish("session", TwinUITone.Ok, "PIPELINE_READY",
                    "snapshot " + s.ToCompactLine());
            }
        }

        private void Publish(string kind, TwinUITone tone, string code, string text)
        {
            if (Events != null)
            {
                Events.Publish(kind, tone, code, text);
            }
        }

        private string DescribeKey(string key, TwinUiSnapshot s)
        {
            var link = s.Link;
            switch (key)
            {
                case KeySample: return "采样频率 " + link.SampleHz.ToString("F1") + " Hz";
                case KeyApply: return "应用频率 " + link.ApplyHz.ToString("F1") + " Hz";
                case KeyFresh: return "新鲜度 " + link.FreshnessMs.ToString("F0") + " ms";
                case KeyLatency: return "接收->应用 " + link.ReceiveToApplyMs.ToString("F1") + " ms";
                case KeyJitter: return "抖动 p99 " + link.JitterP99Ms.ToString("F2") + " ms";
                case KeyDrop: return "丢帧 " + link.DroppedFrames;
                case KeyClock: return "时钟 " + link.ClockSync;
                case KeyQuality: return "帧质量 " + link.Quality;
                case KeyJointError: return "关节误差超限";
                case KeyTorque: return "关节力矩超限";
                case KeyForce: return "六维力超限";
                case KeyPose: return "TCP 位姿";
                default: return key;
            }
        }
    }
}