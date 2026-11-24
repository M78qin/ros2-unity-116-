using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Triggered lightweight CSV for E1–E5. Idle = zero disk writes.
    /// Writes <b>exactly one row per new recv_seq</b> (ROS callback → queue → main-thread drain).
    /// Primary join key is upstream ROS <c>header.stamp</c> (<c>stamp_sec</c>/<c>stamp_nanosec</c>/<c>stamp_ns</c>),
    /// matching VM <c>joints.csv</c>. Local <c>seq</c> is a window row ordinal only (not a sync key).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExpWindowLiteRecorder : MonoBehaviour
    {
        private const string Header =
            "seq,recv_seq,stamp_sec,stamp_nanosec,stamp_ns,unity_mono_ms,rtt_ms," +
            "q_virtual_1,q_virtual_2,q_virtual_3,q_virtual_4,q_virtual_5,q_virtual_6";

        private readonly struct PendingSample
        {
            public readonly long RecvSeq;
            public readonly long MonoNs;
            public readonly int StampSec;
            public readonly uint StampNanosec;
            public readonly float[] QDeg;

            public PendingSample(long recvSeq, long monoNs, int stampSec, uint stampNanosec, float[] qDeg)
            {
                RecvSeq = recvSeq;
                MonoNs = monoNs;
                StampSec = stampSec;
                StampNanosec = stampNanosec;
                QDeg = qDeg;
            }
        }

        [SerializeField] private Ros2Bridge bridge;
        [SerializeField] private string defaultOutputPath = "";
        [SerializeField, Tooltip("0 = write every new recv_seq; 30/60 = E4 apply-rate subsample")]
        private int applyHz = 0;

        private StreamWriter _writer;
        private bool _recording;
        private long _windowSeq;
        private long _lastWrittenRecvSeq = -1;
        private long _lastWrittenStampNs = -1;
        private double _lastRttMs = double.NaN;
        private long _lastRttEchoSeq = -1;
        private long _lastWriteMonoNs;
        private long _minWriteIntervalNs;
        private readonly ConcurrentQueue<PendingSample> _pending = new ConcurrentQueue<PendingSample>();
        private readonly StringBuilder _line = new StringBuilder(256);
        private readonly object _ioLock = new object();
        private Action<long, float[], long, int, uint> _sampleHandler;

        public int ApplyHz
        {
            get => applyHz;
            set
            {
                applyHz = Math.Max(0, value);
                _minWriteIntervalNs = applyHz > 0 ? (1_000_000_000L / applyHz) : 0;
            }
        }

        public bool IsRecording => _recording;
        public string OutputPath { get; private set; } = string.Empty;
        public long RowsWritten { get; private set; }
        public long DuplicateSkipped { get; private set; }

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<Ros2Bridge>() ?? FindObjectOfType<Ros2Bridge>();
            }
        }

        private void OnEnable()
        {
            BindBridge();
        }

        private void OnDisable()
        {
            UnbindBridge();
            StopWindow();
        }

        private void OnDestroy()
        {
            UnbindBridge();
            StopWindow();
        }

        private void BindBridge()
        {
            if (bridge == null)
            {
                bridge = GetComponent<Ros2Bridge>() ?? FindObjectOfType<Ros2Bridge>();
            }
            if (bridge == null || _sampleHandler != null)
            {
                return;
            }
            _sampleHandler = OnJointSample;
            bridge.JointSampleAccepted += _sampleHandler;
        }

        private void UnbindBridge()
        {
            if (bridge != null && _sampleHandler != null)
            {
                bridge.JointSampleAccepted -= _sampleHandler;
            }
            _sampleHandler = null;
        }

        public bool StartWindow(string absoluteCsvPath = null)
        {
            string path = string.IsNullOrWhiteSpace(absoluteCsvPath) ? defaultOutputPath : absoluteCsvPath.Trim();
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                Debug.LogError("[ExpWindowLiteRecorder] absolute output path required.");
                return false;
            }

            BindBridge();
            lock (_ioLock)
            {
                if (_recording)
                {
                    StopWindowUnlocked();
                }

                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    while (_pending.TryDequeue(out _)) { }

                    _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
                    _writer.WriteLine(Header);
                    OutputPath = path;
                    _windowSeq = 0;
                    _lastWrittenRecvSeq = -1;
                    _lastWrittenStampNs = -1;
                    _lastWriteMonoNs = 0;
                    _minWriteIntervalNs = applyHz > 0 ? (1_000_000_000L / applyHz) : 0;
                    RowsWritten = 0;
                    DuplicateSkipped = 0;
                    _lastRttMs = double.NaN;
                    _lastRttEchoSeq = -1;
                    _recording = true;
                    Debug.Log("[ExpWindowLiteRecorder] StartWindow applyHz=" + applyHz + " → " + path);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError("[ExpWindowLiteRecorder] StartWindow failed: " + ex.Message);
                    _writer = null;
                    _recording = false;
                    return false;
                }
            }
        }

        public void StopWindow()
        {
            lock (_ioLock)
            {
                // Drain remaining samples before close.
                DrainPendingUnlocked();
                StopWindowUnlocked();
            }
        }

        private void StopWindowUnlocked()
        {
            _recording = false;
            if (_writer != null)
            {
                try { _writer.Flush(); _writer.Dispose(); }
                catch { /* idempotent */ }
                _writer = null;
            }
        }

        private void OnJointSample(long recvSeq, float[] qDeg, long monoNs, int stampSec, uint stampNanosec)
        {
            if (!_recording || recvSeq < 0 || qDeg == null || qDeg.Length < 6)
            {
                return;
            }

            float[] copy = new float[6];
            Array.Copy(qDeg, copy, 6);
            _pending.Enqueue(new PendingSample(recvSeq, monoNs, stampSec, stampNanosec, copy));
        }

        private void Update()
        {
            if (!_recording)
            {
                return;
            }

            if (bridge != null &&
                bridge.TryGetLastLatencyEcho(out long echoSeq, out double rttMs, out _) &&
                echoSeq != _lastRttEchoSeq && !double.IsNaN(rttMs))
            {
                _lastRttEchoSeq = echoSeq;
                _lastRttMs = rttMs;
            }

            lock (_ioLock)
            {
                DrainPendingUnlocked();
            }
        }

        private void DrainPendingUnlocked()
        {
            if (_writer == null)
            {
                return;
            }

            int drained = 0;
            while (_pending.TryDequeue(out PendingSample sample))
            {
                long stampNs = (long)sample.StampSec * 1_000_000_000L + sample.StampNanosec;

                // Prefer stamp_ns uniqueness when available; else recv_seq.
                if (stampNs > 0)
                {
                    if (stampNs <= _lastWrittenStampNs)
                    {
                        DuplicateSkipped++;
                        continue;
                    }
                }
                else if (sample.RecvSeq <= _lastWrittenRecvSeq)
                {
                    DuplicateSkipped++;
                    continue;
                }

                // E4 apply-rate subsample: keep latest sample in interval, drop extras.
                if (_minWriteIntervalNs > 0 && _lastWriteMonoNs > 0 &&
                    sample.MonoNs - _lastWriteMonoNs < _minWriteIntervalNs)
                {
                    DuplicateSkipped++;
                    continue;
                }

                _lastWrittenRecvSeq = sample.RecvSeq;
                if (stampNs > 0)
                {
                    _lastWrittenStampNs = stampNs;
                }
                _lastWriteMonoNs = sample.MonoNs > 0 ? sample.MonoNs : _lastWriteMonoNs;
                _windowSeq++;
                long monoMs = sample.MonoNs > 0 ? sample.MonoNs / 1_000_000L
                    : (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
                WriteRowUnlocked(
                    _windowSeq,
                    sample.RecvSeq,
                    sample.StampSec,
                    sample.StampNanosec,
                    stampNs,
                    monoMs,
                    _lastRttMs,
                    sample.QDeg);
                drained++;
                if (drained >= 512)
                {
                    break; // keep frame budget; next Update continues
                }
            }

            if (drained > 0)
            {
                try { _writer.Flush(); }
                catch { /* keep recording */ }
            }
        }

        private void WriteRowUnlocked(
            long seq,
            long recvSeq,
            int stampSec,
            uint stampNanosec,
            long stampNs,
            long monoMs,
            double rttMs,
            float[] q)
        {
            _line.Length = 0;
            _line.Append(seq.ToString(CultureInfo.InvariantCulture)).Append(',');
            _line.Append(recvSeq.ToString(CultureInfo.InvariantCulture)).Append(',');
            _line.Append(stampSec.ToString(CultureInfo.InvariantCulture)).Append(',');
            _line.Append(stampNanosec.ToString(CultureInfo.InvariantCulture)).Append(',');
            _line.Append(stampNs.ToString(CultureInfo.InvariantCulture)).Append(',');
            _line.Append(monoMs.ToString(CultureInfo.InvariantCulture)).Append(',');
            if (double.IsNaN(rttMs) || double.IsInfinity(rttMs))
            {
                _line.Append(',');
            }
            else
            {
                _line.Append(rttMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            }

            for (int i = 0; i < 6; i++)
            {
                float v = (q != null && i < q.Length) ? q[i] : 0f;
                _line.Append(v.ToString("0.######", CultureInfo.InvariantCulture));
                if (i < 5) _line.Append(',');
            }

            try
            {
                _writer.WriteLine(_line.ToString());
                RowsWritten++;
            }
            catch (Exception ex)
            {
                Debug.LogError("[ExpWindowLiteRecorder] write failed: " + ex.Message);
                StopWindowUnlocked();
            }
        }
    }
}
