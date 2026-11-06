// 回放数据源：读 DART_R-data 的 PaperLogs/<runId>/samples.csv，把真实会话按时间轴喂给 UI。
//
// 用途：没有真机时验证"绑定 / 阈值 / 图表 / 事件流"是否按预期工作；它不能证明现场观感。
// 诚实边界（写进快照 Notes）：
//   - samples.csv 只有 event(receive/apply/model) + 关节 rad + 目标显示角 + 实际显示角 + 有效位，
//     没有力矩 / 六维力 / TCP 位姿，因此这些能力位一律为 0，界面显示"未订阅"。
//   - 时钟同步域信息不在 CSV 里，ClockSync 标"未提供"。
//   - 频率/抖动/时延/丢帧都由 CSV 自身时间戳与 seq 重算，窗口见 settings.replayWindowSeconds。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class ReplayTwinUiDataSource : ITwinUiDataSource
    {
        private const int JointCount = 6;
        private const int KindReceive = 0;
        private const int KindApply = 1;
        private const int KindModel = 2;
        private const int KindOther = 3;

        private struct Row
        {
            public long T;        // unity_event_ns
            public long Recv;     // unity_receive_ns
            public int Seq;
            public int Kind;
            public float[] JointRad;
            public float[] TargetDeg;
            public float[] ActualDeg;
            public bool HasTarget;
        }

        private Row[] _rows = new Row[0];
        private readonly Dictionary<int, long> _seqToRecv = new Dictionary<int, long>(4096);
        private string _csvPath = string.Empty;
        private string _runDir = string.Empty;
        private long _startNs;
        private long _endNs;
        private double _clockNs;
        private int _cursor;
        private double _windowSeconds = 2d;
        private float _speed = 1f;
        private bool _loop = true;
        private float _lastWall = -1f;
        private TwinUiSnapshot _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, string.Empty);
        private readonly TwinUiExperimentContext _context = new TwinUiExperimentContext();

        public ReplayTwinUiDataSource(string csvPath)
        {
            Load(csvPath);
        }

        public TwinUiSourceKind Kind => TwinUiSourceKind.Replay;
        public string Label => TwinUITextR3.CtxReplay;
        public bool IsMock => false;
        public bool IsReplay => true;
        public string StatusLine { get; private set; } = "replay: not loaded";
        public TwinUiSnapshot Snapshot => _snapshot;
        public string CsvPath => _csvPath;
        public int RowCount => _rows == null ? 0 : _rows.Length;
        public bool Loaded => _rows != null && _rows.Length > 0;
        public string LoadError { get; private set; } = string.Empty;
        public double TotalSeconds => _rows == null || _rows.Length == 0 ? 0d : (_endNs - _startNs) / 1e9;
        public double PlaybackSeconds => (_clockNs - _startNs) / 1e9;
        public double Progress01 => TotalSeconds <= 0d ? 0d : Mathf.Clamp01((float)(PlaybackSeconds / TotalSeconds));

        public void Reset()
        {
            _cursor = 0;
            _clockNs = _startNs;
            _lastWall = -1f;
            _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, TwinUITextR3.CtxReplay);
        }

        public void Load(string csvPath)
        {
            _csvPath = csvPath ?? string.Empty;
            _rows = new Row[0];
            _seqToRecv.Clear();
            LoadError = string.Empty;

            if (string.IsNullOrEmpty(_csvPath) || !File.Exists(_csvPath))
            {
                LoadError = "csv not found: " + _csvPath;
                StatusLine = "replay: " + LoadError;
                Debug.LogWarning("[Twin UI replay] " + LoadError);
                return;
            }

            try
            {
                Parse(csvPath);
            }
            catch (Exception ex)
            {
                LoadError = ex.GetType().Name + ": " + ex.Message;
                StatusLine = "replay: parse failed " + LoadError;
                Debug.LogWarning("[Twin UI replay] parse failed: " + ex);
                return;
            }

            if (_rows.Length == 0)
            {
                LoadError = "no data rows";
                StatusLine = "replay: no data rows";
                return;
            }

            _startNs = _rows[0].T;
            _endNs = _rows[_rows.Length - 1].T;
            _clockNs = _startNs;
            _cursor = 0;
            _lastWall = -1f;
            _runDir = Path.GetDirectoryName(csvPath) ?? string.Empty;
            LoadContext(_runDir);
            StatusLine = "replay: rows=" + _rows.Length + " dur=" + TotalSeconds.ToString("F1") + "s"
                + " window=" + _windowSeconds.ToString("F1") + "s";
            Debug.Log("[Twin UI replay] loaded rows=" + _rows.Length + " dur=" + TotalSeconds.ToString("F1")
                + "s src=" + csvPath);
        }

        private void Parse(string csvPath)
        {
            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2)
            {
                return;
            }

            string[] header = SplitCsv(lines[0]);
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                index[header[i].Trim()] = i;
            }

            int iEvent = Find(index, "event");
            int iSeq = Find(index, "local_receive_seq");
            int iRecv = Find(index, "unity_receive_ns");
            int iEv = Find(index, "unity_event_ns");
            var iJoint = new int[JointCount];
            var iTarget = new int[JointCount];
            var iActual = new int[JointCount];
            for (int j = 0; j < JointCount; j++)
            {
                iJoint[j] = Find(index, "input_j" + (j + 1) + "_rad");
                iTarget[j] = Find(index, "target_j" + (j + 1) + "_display_deg");
                iActual[j] = Find(index, "actual_j" + (j + 1) + "_display_deg");
            }

            var list = new List<Row>(lines.Length);
            for (int line = 1; line < lines.Length; line++)
            {
                string raw = lines[line];
                if (string.IsNullOrEmpty(raw) || raw.Length < 8)
                {
                    continue;
                }

                string[] f = SplitCsv(raw);
                var row = new Row
                {
                    T = ParseLong(f, iEv),
                    Recv = ParseLong(f, iRecv),
                    Seq = (int)ParseLong(f, iSeq),
                    Kind = KindOf(ParseString(f, iEvent)),
                    JointRad = new float[JointCount],
                    TargetDeg = new float[JointCount],
                    ActualDeg = new float[JointCount]
                };

                bool anyJoint = false;
                bool anyTarget = false;
                for (int j = 0; j < JointCount; j++)
                {
                    row.JointRad[j] = ParseFloat(f, iJoint[j]);
                    row.TargetDeg[j] = ParseFloat(f, iTarget[j]);
                    row.ActualDeg[j] = ParseFloat(f, iActual[j]);
                    if (!float.IsNaN(row.JointRad[j]))
                    {
                        anyJoint = true;
                    }

                    if (!float.IsNaN(row.TargetDeg[j]))
                    {
                        anyTarget = true;
                    }
                }

                row.HasTarget = anyTarget;
                if (!anyJoint)
                {
                    row.JointRad = null;
                }

                if (!anyTarget)
                {
                    row.TargetDeg = null;
                }

                if (row.T != 0 && row.Kind == KindReceive && !_seqToRecv.ContainsKey(row.Seq))
                {
                    _seqToRecv[row.Seq] = row.Recv;
                }

                list.Add(row);
            }

            list.Sort((a, b) => a.T.CompareTo(b.T));
            _rows = list.ToArray();
        }

        private static int KindOf(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return KindOther;
            }

            if (value == "receive")
            {
                return KindReceive;
            }

            if (value == "apply")
            {
                return KindApply;
            }

            return value == "model" ? KindModel : KindOther;
        }

        private static string[] SplitCsv(string line)
        {
            return line.Split(',');
        }

        private static int Find(Dictionary<string, int> index, string key)
        {
            return index.TryGetValue(key, out int v) ? v : -1;
        }

        private static string ParseString(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return string.Empty;
            }

            return fields[index].Trim().Trim('"');
        }

        private static long ParseLong(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return 0L;
            }

            long.TryParse(fields[index].Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v);
            return v;
        }

        private static float ParseFloat(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return float.NaN;
            }

            string raw = fields[index].Trim().Trim('"');
            if (raw.Length == 0)
            {
                return float.NaN;
            }

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
        }

        private void LoadContext(string runDir)
        {
            string runId = string.IsNullOrEmpty(runDir) ? string.Empty : Path.GetFileName(runDir.TrimEnd(Path.DirectorySeparatorChar));
            _context.RunId = string.IsNullOrEmpty(runId) ? TwinUITextR3.CtxNotProvided : runId;
            _context.Profile = TwinUITextR3.CtxNotProvided;
            _context.Condition = ConditionFromRunId(runId);
            _context.PayloadKg = TwinUITextR3.CtxNotProvided;
            _context.TcpDefinition = TwinUITextR3.CtxNotProvided;
            _context.SessionId = _context.RunId;
            _context.SelfCheckStatus = "未校验";

            string profilePath = Path.Combine(runDir, "profile.json");
            if (File.Exists(profilePath))
            {
                string json = File.ReadAllText(profilePath);
                string mode = JsonString(json, "sourceMode");
                if (!string.IsNullOrEmpty(mode))
                {
                    _context.Profile = mode;
                }

                string applyHz = JsonNumber(json, "applyHz");
                if (!string.IsNullOrEmpty(applyHz))
                {
                    _context.Profile = _context.Profile + " / applyHz " + applyHz;
                }
            }

            string qualityPath = Path.Combine(runDir, "quality.json");
            if (File.Exists(qualityPath))
            {
                string json = File.ReadAllText(qualityPath);
                string flushed = JsonString(json, "flushed");
                string rows = JsonNumber(json, "written_rows");
                _context.SelfCheckStatus = "flushed=" + (flushed ?? "?") + " / written_rows=" + (rows ?? "?");
                _context.SelfCheckPassed = flushed == "true";
            }
        }

        private static string ConditionFromRunId(string runId)
        {
            if (string.IsNullOrEmpty(runId))
            {
                return TwinUITextR3.CtxNotProvided;
            }

            string[] parts = runId.Split('_');
            int n = parts.Length;
            if (n >= 2)
            {
                return parts[n - 2] + "_" + parts[n - 1];
            }

            return runId;
        }

        private static string JsonString(string json, string key)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', i);
            if (colon < 0)
            {
                return null;
            }

            int q1 = json.IndexOf('"', colon);
            if (q1 < 0)
            {
                return null;
            }

            int q2 = json.IndexOf('"', q1 + 1);
            return q2 > q1 ? json.Substring(q1 + 1, q2 - q1 - 1) : null;
        }

        private static string JsonNumber(string json, string key)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', i);
            if (colon < 0)
            {
                return null;
            }

            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
            {
                start++;
            }

            int end = start;
            while (end < json.Length && "0123456789.-+eE".IndexOf(json[end]) >= 0)
            {
                end++;
            }

            return end > start ? json.Substring(start, end - start) : null;
        }

        // ------------------------------------------------------------ 播放

        /// <summary>手动推进播放时钟（编辑态截图用，不依赖 EditorApplication.update）。</summary>
        public void Advance(double seconds)
        {
            if (!Loaded)
            {
                return;
            }

            double deltaNs = Math.Max(0d, seconds) * 1e9 * _speed;
            _clockNs += deltaNs;
            if (_clockNs > _endNs)
            {
                if (_loop && TotalSeconds > 0d)
                {
                    double span = _endNs - _startNs;
                    _clockNs = _startNs + (_clockNs - _startNs) % span;
                }
                else
                {
                    _clockNs = _endNs;
                }
            }

            while (_cursor < _rows.Length && _rows[_cursor].T <= _clockNs)
            {
                _cursor++;
            }
        }

        public void SeekFraction(double fraction)
        {
            if (!Loaded)
            {
                return;
            }

            _clockNs = _startNs + (_endNs - _startNs) * Mathf.Clamp01((float)fraction);
            _cursor = 0;
            while (_cursor < _rows.Length && _rows[_cursor].T <= _clockNs)
            {
                _cursor++;
            }
        }

        public void Refresh(TwinUiSettings settings)
        {
            if (settings != null)
            {
                _windowSeconds = Mathf.Max(0.2f, settings.replayWindowSeconds);
                _speed = Mathf.Max(0.05f, settings.replaySpeed);
                _loop = settings.replayLoop;
            }

            if (!Loaded)
            {
                _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, TwinUITextR3.CtxReplay);
                _snapshot.Link.SourceName = "replay";
                _snapshot.Link.SourceKindLabel = Label;
                _snapshot.Notes = LoadError;
                _snapshot.Experiment = _context;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_lastWall < 0f)
            {
                _lastWall = now;
            }
            else
            {
                double dt = Math.Max(0d, now - _lastWall);
                _lastWall = now;
                if (dt > 0d)
                {
                    Advance(dt);
                }
            }

            _snapshot = Build(settings);
        }

        private TwinUiSnapshot Build(TwinUiSettings settings)
        {
            var snap = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, TwinUITextR3.CtxReplay);
            snap.Notes = "replay of samples.csv; torque/force/tcp are not present in the CSV and stay unsubscribed";
            snap.Experiment = _context;
            snap.Link.SourceName = "replay";
            snap.Link.SourceKindLabel = Label;
            snap.Link.Connected = true;
            snap.Link.HasFrame = _cursor > 0;
            snap.Link.UiHz = settings != null ? settings.uiRefreshHz : 0d;
            snap.Link.TargetSampleHz = 100d;
            snap.Link.TargetApplyHz = settings != null ? settings.robotApplyRateHz : 60d;

            if (_cursor == 0)
            {
                snap.Link.Quality = TwinUITextR3.NoData;
                snap.Link.ClockSync = TwinUITextR3.CtxNotProvided;
                return snap;
            }

            int end = Mathf.Min(_cursor, _rows.Length) - 1;
            long lastT = _rows[end].T;
            long windowStart = lastT - (long)(_windowSeconds * 1e9);

            int receiveCount = 0;
            long firstReceiveT = 0;
            long lastReceiveT = 0;
            long lastReceiveRecv = 0;
            int applyCount = 0;
            long firstApplyT = 0;
            long lastApplyT = 0;
            long dropped = 0;
            int lastSeq = int.MinValue;
            var intervals = new List<float>(512);
            var latencies = new List<float>(256);
            long prevReceiveT = 0;

            for (int i = end; i >= 0; i--)
            {
                var row = _rows[i];
                if (row.T < windowStart)
                {
                    break;
                }

                if (row.Kind == KindReceive)
                {
                    receiveCount++;
                    if (lastReceiveT == 0)
                    {
                        lastReceiveT = row.T;
                        lastReceiveRecv = row.Recv;
                    }

                    firstReceiveT = row.T;
                    if (prevReceiveT != 0)
                    {
                        intervals.Add((prevReceiveT - row.T) / 1e6f);
                    }

                    prevReceiveT = row.T;
                    if (lastSeq != int.MinValue && row.Seq > lastSeq + 1)
                    {
                        dropped += row.Seq - lastSeq - 1;
                    }

                    if (row.Seq > lastSeq)
                    {
                        lastSeq = row.Seq;
                    }
                }
                else if (row.Kind == KindApply)
                {
                    applyCount++;
                    if (lastApplyT == 0)
                    {
                        lastApplyT = row.T;
                    }

                    firstApplyT = row.T;
                    if (_seqToRecv.TryGetValue(row.Seq, out long recvNs) && recvNs > 0 && row.T > recvNs)
                    {
                        latencies.Add((row.T - recvNs) / 1e6f);
                    }
                }
            }

            double spanSec = Math.Max(0.001d, (lastReceiveT - firstReceiveT) / 1e9);
            snap.Link.SampleHz = receiveCount > 1 ? (receiveCount - 1) / spanSec : 0d;
            double applySpan = Math.Max(0.001d, (lastApplyT - firstApplyT) / 1e9);
            snap.Link.ApplyHz = applyCount > 1 ? (applyCount - 1) / applySpan : 0d;
            snap.Link.ReceiveToApplyMs = Median(latencies);
            snap.Link.JitterP99Ms = JitterP99(intervals);
            snap.Link.DroppedFrames = dropped;
            snap.Link.OutOfOrderFrames = 0;
            snap.Link.QueueLength = 0;
            snap.Link.ClockSync = TwinUITextR3.CtxNotProvided;
            snap.Link.Quality = "Normal (status=accepted)";
            // 回放里"新鲜度"= 播放游标与该窗口最新样本的间隔（持续播放时应接近 0）
            snap.Link.FreshnessMs = Math.Max(0d, (_clockNs - lastReceiveRecv) / 1e6d);

            // 关节：位置取最后一个 receive 行，目标/误差取最后一个 model 行（CSV 里 model 行才带目标显示角）
            for (int i = end; i >= 0 && i > end - 400; i--)
            {
                var row = _rows[i];
                if (row.JointRad != null && snap.Joints.PositionDeg.Length == 0)
                {
                    var pos = new float[JointCount];
                    for (int j = 0; j < JointCount; j++)
                    {
                        pos[j] = row.JointRad[j] * Mathf.Rad2Deg;
                    }

                    snap.Joints.PositionDeg = pos;
                    snap.Capabilities |= TwinUiCapability.JointPosition;
                }

                if (row.Kind == KindModel && row.TargetDeg != null && row.ActualDeg != null
                    && snap.Joints.TargetDeg.Length == 0)
                {
                    snap.Joints.HasTarget = true;
                    snap.Joints.TargetDeg = (float[])row.TargetDeg.Clone();
                    snap.Joints.ErrorDeg = new float[JointCount];
                    for (int j = 0; j < JointCount; j++)
                    {
                        snap.Joints.ErrorDeg[j] = row.ActualDeg[j] - row.TargetDeg[j];
                    }
                }

                if (snap.Joints.PositionDeg.Length > 0 && snap.Joints.TargetDeg.Length > 0)
                {
                    break;
                }
            }

            if (snap.Joints.PositionDeg.Length == 0)
            {
                snap.Joints.HasTarget = false;
            }

            StatusLine = "replay: rows=" + _rows.Length + " cursor=" + _cursor
                + " t=" + PlaybackSeconds.ToString("F1") + "s/" + TotalSeconds.ToString("F1") + "s"
                + " sampleHz=" + snap.Link.SampleHz.ToString("F1")
                + " applyHz=" + snap.Link.ApplyHz.ToString("F1");
            return snap;
        }

        private static double Median(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0d;
            }

            values.Sort();
            int mid = values.Count / 2;
            if ((values.Count & 1) == 0)
            {
                return (values[mid - 1] + values[mid]) * 0.5d;
            }

            return values[mid];
        }

        private static double JitterP99(List<float> intervals)
        {
            if (intervals == null || intervals.Count < 8)
            {
                return 0d;
            }

            double med = Median(new List<float>(intervals));
            var deviations = new List<float>(intervals.Count);
            for (int i = 0; i < intervals.Count; i++)
            {
                deviations.Add(Mathf.Abs((float)(intervals[i] - med)));
            }

            deviations.Sort();
            int idx = Mathf.Clamp(Mathf.RoundToInt(deviations.Count * 0.99f) - 1, 0, deviations.Count - 1);
            return deviations[idx];
        }
    }
}