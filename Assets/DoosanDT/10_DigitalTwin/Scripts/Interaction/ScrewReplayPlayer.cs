// ScrewReplayPlayer.cs —— 「Screw 拆解」：按 1:1 时间轴回放 E5 真机螺钉拆解片段，并实时驱动
// 实体机械臂 + 推孪生数据总线（关节角/速度、TCP 位姿、六维力-力矩、25 N 力阈值告警）。
//
// 数据源：docs/evidence/e5_screw_segment.json（由真实 CSV 提取，逐样本来自实测，禁止合成）。
// 该 JSON 会同步复制到 Assets/DoosanDT/10_DigitalTwin/Config/e5_screw_segment.json。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DigitalTwin.Interaction
{
    public sealed class ScrewReplayPlayer : MonoBehaviour
    {
        public const float ForceAlarmNewton = 25f;
        public const string RelativePath = "DoosanDT/10_DigitalTwin/Config/e5_screw_segment.json";

        [Serializable]
        public struct Sample
        {
            public float T;
            public float[] Q;      // deg
            public float[] Dq;     // deg/s
            public float[] F;      // N
            public float[] M;      // Nm
        }

        public bool Playing { get; private set; }
        public bool Loaded { get; private set; }
        public string LoadError { get; private set; } = string.Empty;
        public string SourceJointCsv { get; private set; } = string.Empty;
        public string SourceForceCsv { get; private set; } = string.Empty;
        public string SegmentNote { get; private set; } = string.Empty;
        public float SampleRateHz { get; private set; } = 100f;
        public int SampleCount => _samples.Count;
        public float Duration { get; private set; }
        public float Progress { get; private set; }
        public float PeakForceNorm { get; private set; }
        public float PeakFz { get; private set; }
        public bool ForceAlarm { get; private set; }
        public int AlarmFrames { get; private set; }
        public int PublishCount { get; private set; }
        public float JointTravelMaxDeg { get; private set; }
        public bool HasForceChannel { get; private set; }
        public int CurrentIndex { get; private set; }

        private readonly List<Sample> _samples = new List<Sample>();
        private Coroutine _routine;
        private RobotModelController _model;
        private DigitalTwinRuntime _runtime;
        private Transform _tcp;
        private float _t;
        private float[] _lastQ;
        private float[] _blendFrom;
        /// <summary>开场从当前实体姿态平滑过渡到 E5 首帧的时间（秒）；这段是插值衔接，不属于实测轨迹。</summary>
        [SerializeField] private float blendInSeconds = 0.5f;

        public bool Load()
        {
            if (Loaded) return true;
            LoadError = string.Empty;
            _samples.Clear();

            string path = Path.Combine(Application.dataPath, RelativePath);
            if (!File.Exists(path))
            {
                LoadError = "missing file: " + path;
                return false;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));

                JObject source = root["source"] as JObject;
                SourceJointCsv = source != null ? (string)source["jointCsv"] ?? string.Empty : string.Empty;
                SourceForceCsv = source != null ? (string)source["forceCsv"] ?? string.Empty : string.Empty;
                SegmentNote = source != null ? (string)source["note"] ?? string.Empty : string.Empty;

                JToken rate = root["sampleRateHz"];
                SampleRateHz = rate != null ? rate.Value<float>() : 100f;
                if (SampleRateHz < 1f) SampleRateHz = 100f;

                JArray arr = root["samples"] as JArray;
                if (arr == null || arr.Count < 2)
                {
                    LoadError = "samples < 2";
                    return false;
                }

                foreach (JToken s in arr)
                {
                    var smp = new Sample
                    {
                        T = s["t"] != null ? s["t"].Value<float>() : -1f,
                        Q = ToFloatArray(s["q"]),
                        Dq = ToFloatArray(s["dq"]),
                        F = ToFloatArray(s["f"]),
                        M = ToFloatArray(s["m"])
                    };
                    if (smp.Q == null || smp.Q.Length < 6) continue;
                    if (smp.T < 0f) smp.T = _samples.Count / SampleRateHz;
                    _samples.Add(smp);
                }

                if (_samples.Count < 2)
                {
                    LoadError = "valid samples < 2";
                    return false;
                }

                Duration = _samples[_samples.Count - 1].T;
                HasForceChannel = _samples[_samples.Count / 2].F != null;

                // 统计：每轴总行程 + 力峰值
                var minQ = new float[6];
                var maxQ = new float[6];
                for (int i = 0; i < 6; i++) { minQ[i] = float.MaxValue; maxQ[i] = float.MinValue; }
                foreach (Sample smp in _samples)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        minQ[i] = Mathf.Min(minQ[i], smp.Q[i]);
                        maxQ[i] = Mathf.Max(maxQ[i], smp.Q[i]);
                    }
                    if (smp.F != null && smp.F.Length >= 3)
                    {
                        float norm = Mathf.Sqrt(smp.F[0] * smp.F[0] + smp.F[1] * smp.F[1] + smp.F[2] * smp.F[2]);
                        PeakForceNorm = Mathf.Max(PeakForceNorm, norm);
                        PeakFz = Mathf.Max(PeakFz, Mathf.Abs(smp.F[2]));
                        if (norm > ForceAlarmNewton) AlarmFrames++;
                    }
                }
                for (int i = 0; i < 6; i++) JointTravelMaxDeg = Mathf.Max(JointTravelMaxDeg, maxQ[i] - minQ[i]);

                Loaded = true;
                Debug.Log("[REPLAY] loaded screw segment: samples=" + _samples.Count +
                          " rate=" + SampleRateHz.ToString("F0") + "Hz dur=" + Duration.ToString("F2") + "s" +
                          " force=" + HasForceChannel + " peakF=" + PeakForceNorm.ToString("F2") + "N" +
                          " travel=" + JointTravelMaxDeg.ToString("F2") + "°");
                return true;
            }
            catch (Exception e)
            {
                LoadError = e.GetType().Name + ": " + e.Message;
                Debug.LogError("[REPLAY] load failed: " + LoadError);
                return false;
            }
        }

        private static float[] ToFloatArray(JToken token)
        {
            JArray a = token as JArray;
            if (a == null) return null;
            var result = new float[a.Count];
            for (int i = 0; i < a.Count; i++) result[i] = a[i].Value<float>();
            return result;
        }

        public bool Play(RobotModelController model, DigitalTwinRuntime runtime, Transform tcp)
        {
            if (Playing) return false;
            if (!Load()) return false;
            _model = model;
            _runtime = runtime;
            _tcp = tcp;
            if (_model == null) { LoadError = "model null"; return false; }

            _t = 0f;
            Progress = 0f;
            PeakForceNorm = 0f;
            PeakFz = 0f;
            AlarmFrames = 0;
            ForceAlarm = false;
            PublishCount = 0;
            CurrentIndex = 0;
            _lastQ = _model.ReadCurrentDisplayDegrees();
            _blendFrom = _lastQ != null ? (float[])_lastQ.Clone() : null;
            _routine = StartCoroutine(CoPlay());
            return true;
        }

        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            if (Playing)
            {
                Debug.Log("[REPLAY] stopped at t=" + _t.ToString("F2") + "s idx=" + CurrentIndex +
                          " publishes=" + PublishCount);
            }
            Playing = false;
        }

        private IEnumerator CoPlay()
        {
            Playing = true;
            Debug.Log("[REPLAY] play start " + Duration.ToString("F2") + "s / " + _samples.Count + " samples");
            while (_t < Duration)
            {
                _t += Time.unscaledDeltaTime;
                float t = Mathf.Min(_t, Duration);
                Sample smp = Evaluate(t, out float frac);
                CurrentIndex = Mathf.Clamp(Mathf.RoundToInt(t * SampleRateHz), 0, _samples.Count - 1);

                if (_model != null)
                {
                    float[] apply = smp.Q;
                    if (_blendFrom != null && blendInSeconds > 0f && t < blendInSeconds && smp.Q != null)
                    {
                        // 开场衔接：从"当前实体姿态"过渡到 E5 首帧（这 0.5 s 是插值，不是实测轨迹）
                        float k = Mathf.Clamp01(t / blendInSeconds);
                        apply = new float[smp.Q.Length];
                        for (int i = 0; i < apply.Length; i++)
                            apply[i] = Mathf.Lerp(_blendFrom[Mathf.Min(i, _blendFrom.Length - 1)], smp.Q[i], k);
                    }
                    _model.ApplyJointDisplayDegrees(apply, false, RobotModelController.PoseSource.CommandPreview, true, false);
                    _lastQ = apply;
                }

                PublishFrame(smp);
                Progress = Duration > 0f ? Mathf.Clamp01(t / Duration) : 1f;
                yield return null;
            }

            // 收尾：停在末样本
            Sample last = _samples[_samples.Count - 1];
            if (_model != null)
                _model.ApplyJointDisplayDegrees(last.Q, false, RobotModelController.PoseSource.CommandPreview, true, true);
            PublishFrame(last);
            Progress = 1f;
            Playing = false;
            _routine = null;
            Debug.Log("[REPLAY] play done publishes=" + PublishCount + " peakF=" + PeakForceNorm.ToString("F2") +
                      "N peakFz=" + PeakFz.ToString("F2") + "N alarms=" + AlarmFrames);
        }

        private Sample Evaluate(float t, out float frac)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(t * SampleRateHz), 0, _samples.Count - 1);
            int j = Mathf.Min(i + 1, _samples.Count - 1);
            float t0 = _samples[i].T;
            float t1 = _samples[j].T;
            frac = t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            if (i == j || frac <= 0f) return _samples[i];

            Sample a = _samples[i];
            Sample b = _samples[j];
            var r = new Sample
            {
                T = t,
                Q = LerpArray(a.Q, b.Q, frac),
                Dq = LerpArray(a.Dq, b.Dq, frac),
                F = LerpArray(a.F, b.F, frac),
                M = LerpArray(a.M, b.M, frac)
            };
            return r;
        }

        private static float[] LerpArray(float[] a, float[] b, float f)
        {
            if (a == null || b == null || a.Length != b.Length) return a;
            var r = new float[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = Mathf.Lerp(a[i], b[i], f);
            return r;
        }

        private void PublishFrame(Sample smp)
        {
            if (_runtime == null) return;

            var frame = new RobotStateFrame
            {
                SourceName = "E5-REPLAY",
                Mode = "Replay",
                MotionState = "REPLAY",
                Channel = "e5_screw",
                SequenceId = PublishCount,
                HasSourceIdentity = true,
                HasTcpPose = false,
                Quality = FrameQuality.Normal
            };

            if (smp.Q != null && smp.Q.Length >= 6)
            {
                var qRad = new float[smp.Q.Length];
                for (int i = 0; i < smp.Q.Length; i++) qRad[i] = smp.Q[i] * Mathf.Deg2Rad;
                frame.JointPositionRad = qRad;
            }
            if (smp.Dq != null && smp.Dq.Length >= 6)
            {
                var dqRad = new float[smp.Dq.Length];
                for (int i = 0; i < smp.Dq.Length; i++) dqRad[i] = smp.Dq[i] * Mathf.Deg2Rad;
                frame.JointVelocityRad = dqRad;
            }
            if (smp.F != null && smp.F.Length >= 3)
            {
                float norm = Mathf.Sqrt(smp.F[0] * smp.F[0] + smp.F[1] * smp.F[1] + smp.F[2] * smp.F[2]);
                PeakForceNorm = Mathf.Max(PeakForceNorm, norm);
                PeakFz = Mathf.Max(PeakFz, Mathf.Abs(smp.F[2]));
                bool alarm = norm > ForceAlarmNewton;
                if (alarm) AlarmFrames++;
                ForceAlarm = alarm;
                frame.ForceVector = new[]
                {
                    smp.F[0], smp.F[1], smp.F[2],
                    smp.M != null && smp.M.Length >= 3 ? smp.M[0] : 0f,
                    smp.M != null && smp.M.Length >= 3 ? smp.M[1] : 0f,
                    smp.M != null && smp.M.Length >= 3 ? smp.M[2] : 0f
                };
            }

            if (_tcp != null && smp.Q != null && smp.Q.Length >= 6 && _model != null)
            {
                if (_model.EvaluateTcpPoseDegrees(smp.Q, _tcp, Vector3.zero, Vector3.zero, out Vector3 pos, out Quaternion rot))
                {
                    frame.TcpPositionMeters = pos;
                    frame.TcpRotation = rot;
                    frame.HasTcpPose = true;
                }
            }

            _runtime.PublishFrame(frame);
            PublishCount++;
        }

        public float[] LastJoints => _lastQ;
    }
}