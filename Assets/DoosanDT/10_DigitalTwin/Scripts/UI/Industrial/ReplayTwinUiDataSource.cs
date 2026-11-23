// 回放数据源：读 DART_R-data 的 PaperLogs/<runId>/samples.csv，或同工程的 RT 原始 CSV（rt_raw_*），
// 把真实会话按时间轴喂给 UI。**同一入口、按表头自动识别 schema**，不建第二套系统。
//
// 支持的两种真实文件（都在 E_TJ 真机采集链路里实际存在，合同要求同一入口）：
//   1) samples.csv（34 列，Unity 侧事件流）：event/run_id/.../status/input_j1..6_rad/
//      target_j1..6_display_deg/actual_j1..6_display_deg/actual_j1..6_valid
//      —— 无 TCP / 六维力 / 关节力矩 / 速度；这些能力位一律不置位，界面显示"未订阅"。
//   2) rt_raw_*.csv（聚合器逐样本快照）：**40 列**（2026-07-26 E1 基线，到 singularity 结束）
//      或 **46 列**（2026-11-03/04，末尾多 run_id/source_id/source_session_id/source_seq/
//      joints_valid/controller_time_valid）。解析**按表头**做，缺列一律标 UNKNOWN/未提供，不按列号硬编码。
//
// 诚实边界（写进快照 Notes，逐条可复核）：
//   * 不推断坐标系/Euler 约定：tcp_a/b/c 的轴序与内外旋在原始数据里没有依据 → UNKNOWN。
//   * mm / ° / N / N·m **原样显示**，不做单位换算（尤其不做 mm→m、deg→rad→deg 往返）。
//   * status 列真解析（accepted/identity_mismatch/source_changed/out_of_order + 未知值），
//     无该列 → "未提供"，绝不硬标正常。旧代码的 "Normal (status=accepted)" 硬编码已删除。
//   * 身份混用（文件内多个 run_id）时速率**不可用**：两套帧会被一起计数（实测 179.66 Hz 的假高值）。
//   * 4 个时钟域不混：回放时间轴按 monotonic_ns → ros_time_ns → controller_time 顺序择一，
//     选择依据写进 Clocks.TimeAxisReason 与 Notes；controller_time 在 11-03/04 会话里几乎全重复，不可当轴。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>回放文件 schema（按表头识别，不按列号）。</summary>
    public enum TwinUiReplaySchema
    {
        None = 0,
        Samples34 = 1,
        Raw40 = 2,
        Raw46 = 3
    }

    public sealed class ReplayTwinUiDataSource : ITwinUiDataSource
    {
        private const int JointCount = 6;
        private const int KindReceive = 0;
        private const int KindApply = 1;
        private const int KindModel = 2;
        private const int KindOther = 3;

        private struct Row
        {
            public long T;        // 回放时间轴（ns，按 Clocks.TimeAxis 选出的单一轴）
            public long Recv;     // samples=unity_receive_ns；raw=ros_time_ns
            public int Seq;
            public int Kind;
            public float[] JointRad;
            public float[] TargetDeg;
            public float[] ActualDeg;
            public bool HasTarget;

            // ---- rt_raw 原始通道（缺列 = null，不补 0）----
            public float[] JointDeg;
            public float[] JointVelDeg;
            public float[] TcpMm;         // [x,y,z]
            public float[] TcpAbcDeg;     // [a,b,c]（轴序/内外旋 UNKNOWN）
            public float[] Force;         // [fx,fy,fz] N
            public float[] Moment;        // [tx,ty,tz] N·m
            public float[] JointTorqueNm; // [j1..j6] N·m
            public string[] DeviceRaw;    // [robot_mode,robot_state,control_mode,solution_space,singularity] 原值
            public int JointsValid;       // -1 未提供 / 0 False / 1 True
            public int ControllerTimeValid;
            public float ControllerTime;
            public long RosNs;
            public long HostWallNs;
            public long MonotonicNs;
            public string StatusRaw;
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
        private int _loopOverride = -1; // -1 未覆盖 / 0 关 / 1 开（自检用来定位末帧，不改设置资产）
        private bool _hold;             // 暂停**回放游标**推进（生产暂停与自检共用同一个执行点）
        // fix1 B(8)：循环回绕证据。回绕后游标必须回到 0，否则游标停在末行、循环事实上停摆；
        // 同时把"回绕"记下来交给 feed/卡片清曲线（禁止跨周期伪连续）。
        private bool _loopWrapPending;
        private float _lastWall = -1f;
        private TwinUiSnapshot _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, string.Empty);
        private readonly TwinUiExperimentContext _context = new TwinUiExperimentContext();

        // ---- schema / 身份 / 时钟（A 支新增，全部只读暴露给 UI 与自检）----
        public TwinUiReplaySchema Schema { get; private set; } = TwinUiReplaySchema.None;
        public string SchemaLabel => Schema == TwinUiReplaySchema.Samples34 ? "samples.csv(34 列)"
            : Schema == TwinUiReplaySchema.Raw46 ? "rt_raw(46 列)"
            : Schema == TwinUiReplaySchema.Raw40 ? "rt_raw(40 列)"
            : "未识别";
        public bool StatusColumnProvided { get; private set; }
        public bool HeaderOnly { get; private set; }
        public bool IdentityUnique { get; private set; } = true;
        public string SourceRunId { get; private set; } = string.Empty;
        public string SourceSessionId { get; private set; } = string.Empty;
        public string DirectoryName { get; private set; } = string.Empty;
        public bool DirectoryRunIdMismatch { get; private set; }
        public int DistinctRunIds { get; private set; }
        public string TimeAxis { get; private set; } = "none";
        public string TimeAxisReason { get; private set; } = string.Empty;
        public string QualityContradiction { get; private set; } = string.Empty;
        public string ParseNote { get; private set; } = string.Empty;

        /// <summary>A-3：既有硬编码**目标**采样率（历史/目标元数据，非实测）。</summary>
        public const double TargetSampleHzConstant = 100d;

        // ---- A-3 频率口径（两个口径分开，禁止混排）--------------------------------
        public int FileReceiveRows { get; private set; }
        public double FileSpanSeconds { get; private set; }
        public double FileReceiveHz => FileReceiveRows > 1 && FileSpanSeconds > 0.001d
            ? (FileReceiveRows - 1) / FileSpanSeconds
            : 0d;
        public int WindowReceiveRows { get; private set; }
        public double WindowReceiveSpanSeconds { get; private set; }
        public double WindowSeconds => _windowSeconds;

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

        /// <summary>当前快照实际采用的样本下标（0 基；-1 = 尚未推进到第一帧）。逐值对照据此回查原始行。</summary>
        public int CursorIndex => _rows != null && _cursor > 0 ? _cursor - 1 : -1;

        /// <summary>当前回放时钟（ns，时间轴口径）。</summary>
        public long PlaybackClockNs => (long)_clockNs;
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
            HeaderOnly = false;
            Schema = TwinUiReplaySchema.None;
            StatusColumnProvided = false;
            IdentityUnique = true;
            DistinctRunIds = 0;
            SourceRunId = string.Empty;
            SourceSessionId = string.Empty;
            DirectoryRunIdMismatch = false;
            QualityContradiction = string.Empty;
            ParseNote = string.Empty;
            TimeAxis = "none";
            TimeAxisReason = string.Empty;

            if (string.IsNullOrEmpty(_csvPath) || !File.Exists(_csvPath))
            {
                LoadError = "csv not found: " + _csvPath;
                StatusLine = "replay: " + LoadError;
                Debug.LogWarning("[Twin UI replay] " + LoadError);
                return;
            }

            // 目录名要在 Parse 里就可用（身份错配判定 = 目录名 vs 行内 run_id）
            _runDir = Path.GetDirectoryName(csvPath) ?? string.Empty;

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

            _runDir = Path.GetDirectoryName(csvPath) ?? string.Empty;
            LoadContext(_runDir);

            if (_rows.Length == 0)
            {
                // 表头合法但 0 数据行：这是真实常见情况（35 个会话里 13 个），
                // 必须显示"无数据"，不是"加载失败"，更不能沿用旧源伪造有数据。
                if (Schema != TwinUiReplaySchema.None)
                {
                    HeaderOnly = true;
                    LoadError = "no data rows";
                    StatusLine = "replay: 无数据（表头合法，0 数据行）schema=" + SchemaLabel;
                }
                else
                {
                    LoadError = "no data rows";
                    StatusLine = "replay: no data rows";
                }

                return;
            }

            _startNs = _rows[0].T;
            _endNs = _rows[_rows.Length - 1].T;
            _clockNs = _startNs;
            _cursor = 0;
            _lastWall = -1f;
            ComputeFileReceiveRate();
            StatusLine = "replay: schema=" + SchemaLabel + " rows=" + _rows.Length
                + " dur=" + TotalSeconds.ToString("F1") + "s"
                + " axis=" + TimeAxis
                + " window=" + _windowSeconds.ToString("F1") + "s"
                + " fileHz=" + FileReceiveHz.ToString("F2") + "(" + FileReceiveRows + " recv)"
                + (IdentityUnique ? string.Empty : " [身份混用→速率不可用]");
            Debug.Log("[Twin UI replay] loaded schema=" + SchemaLabel + " rows=" + _rows.Length
                + " dur=" + TotalSeconds.ToString("F1") + "s axis=" + TimeAxis
                + " fileRecvRows=" + FileReceiveRows + " fileRecvHz=" + FileReceiveHz.ToString("F2")
                + " identityUnique=" + IdentityUnique + " runIds=" + DistinctRunIds
                + " src=" + csvPath);
        }

        private void ComputeFileReceiveRate()
        {
            FileReceiveRows = 0;
            FileSpanSeconds = 0d;
            long first = 0;
            long last = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i].Kind != KindReceive || _rows[i].T == 0)
                {
                    continue;
                }

                if (FileReceiveRows == 0)
                {
                    first = _rows[i].T;
                }

                last = _rows[i].T;
                FileReceiveRows++;
            }

            if (FileReceiveRows > 1)
            {
                FileSpanSeconds = (last - first) / 1e9;
            }
        }

        // ------------------------------------------------------------ 解析

        private void Parse(string csvPath)
        {
            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 1)
            {
                return;
            }

            string[] header = SplitCsv(lines[0]);
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                index[header[i].Trim()] = i;
            }

            StatusColumnProvided = index.ContainsKey("status");
            bool isRaw = index.ContainsKey("sample_seq") && index.ContainsKey("controller_time")
                         && index.ContainsKey("j1_deg");
            bool isSamples = index.ContainsKey("event") && index.ContainsKey("input_j1_rad");
            if (!isRaw && !isSamples)
            {
                throw new InvalidDataException("unrecognized csv header（既不是 samples.csv 也不是 rt_raw）: "
                    + string.Join(",", header, 0, Mathf.Min(8, header.Length)));
            }

            if (isRaw)
            {
                Schema = index.ContainsKey("run_id") && index.ContainsKey("source_session_id")
                    ? TwinUiReplaySchema.Raw46
                    : TwinUiReplaySchema.Raw40;
                ParseRaw(lines, index);
            }
            else
            {
                Schema = TwinUiReplaySchema.Samples34;
                ParseSamples(lines, index);
            }

            ArchiveIdentity();

            var list = _rows;
            Array.Sort(list, (a, b) => a.T.CompareTo(b.T));
            _rows = list;
        }

        /// <summary>身份归档：文件内 run_id 是否唯一、目录名是否与行内 run_id 一致。</summary>
        private void ArchiveIdentity()
        {
            DirectoryName = string.IsNullOrEmpty(_runDir)
                ? string.Empty
                : Path.GetFileName(_runDir.TrimEnd(Path.DirectorySeparatorChar));
            // rt_raw 副本通常在 <session>/raw/ 下：身份比对要用**会话目录**名，而不是 "raw"
            if (string.Equals(DirectoryName, "raw", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(_runDir.TrimEnd(Path.DirectorySeparatorChar));
                DirectoryName = string.IsNullOrEmpty(parent)
                    ? DirectoryName
                    : Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar));
            }
            // run_id 在解析阶段收集（raw46 逐行、samples 逐行）
            HashSet<string> ids = _runIdSet;
            DistinctRunIds = ids.Count;
            if (ids.Count == 1)
            {
                foreach (string v in ids)
                {
                    SourceRunId = v;
                }
            }
            else if (ids.Count > 1)
            {
                SourceRunId = string.Join("|", new List<string>(ids).ToArray());
            }

            IdentityUnique = ids.Count <= 1;
            // 目录名与行内 run_id 不一致 = 身份错配（570 行异常会话即此类）。
            // night30：比对只走本类唯一的归一化实现（NormalizeRunIdentity / SameRunIdentity）——
            //   既有目录命名约定是 "session_<run_id>"，先去掉该约定前缀再比，正常 RT 原始件不再被误标；
            //   真正不同的 run_id（如 20261104_204838_488192_E4_B vs 20261104_204642_850475_E4_B）仍然算错配。
            if (!string.IsNullOrEmpty(DirectoryName) && ids.Count == 1 && !string.IsNullOrEmpty(SourceRunId))
            {
                DirectoryRunIdMismatch = !SameRunIdentity(DirectoryName, SourceRunId);
                if (DirectoryRunIdMismatch)
                {
                    Debug.LogWarning("[Twin UI replay] 身份错配：目录 " + DirectoryName
                        + "（归一化 " + NormalizeRunIdentity(DirectoryName) + "）≠ 行内 run_id " + SourceRunId);
                }
            }

            if (!IdentityUnique)
            {
                ParseNote += "身份混用：" + ids.Count + " 个 run_id（速率不可用，重复计数污染）；";
            }
            else if (DirectoryRunIdMismatch)
            {
                ParseNote += "身份错配：目录 " + DirectoryName + " ≠ 行内 run_id " + SourceRunId + "；";
            }
        }

        private readonly HashSet<string> _runIdSet = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 会话目录前缀约定。历史会话目录名是 "session_&lt;run_id&gt;"（如 session_20261103_135616_529090_E4_B）。
        /// </summary>
        public const string SessionDirectoryPrefix = "session_";

        /// <summary>
        /// night30：run 身份的**唯一**归一化实现（目录名 vs 行内 run_id 的比较都走这里）。
        /// 只做两件事：Trim；去掉目录约定前缀 "session_"（可重复，但不能去掉成空串）。
        /// 不做模糊匹配、不做大小写折叠——身份必须逐字符可比。
        /// </summary>
        public static string NormalizeRunIdentity(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string s = value.Trim();
            while (s.Length > SessionDirectoryPrefix.Length
                   && s.StartsWith(SessionDirectoryPrefix, StringComparison.Ordinal))
            {
                s = s.Substring(SessionDirectoryPrefix.Length);
            }

            return s;
        }

        /// <summary>
        /// night30：两个名字是否指同一个 run 身份。归一化后逐字符比较（区分大小写）；任一为空 = 不可判同。
        /// 例：("session_20261103_135616_529090_E4_B", "20261103_135616_529090_E4_B") → true；
        ///     ("20261104_204838_488192_E4_B", "20261104_204642_850475_E4_B") → false（真实错配必须保留）。
        /// </summary>
        public static bool SameRunIdentity(string a, string b)
        {
            string na = NormalizeRunIdentity(a);
            string nb = NormalizeRunIdentity(b);
            if (na.Length == 0 || nb.Length == 0)
            {
                return false;
            }

            return string.Equals(na, nb, StringComparison.Ordinal);
        }

        private void ParseSamples(string[] lines, Dictionary<string, int> index)
        {
            int iEvent = Find(index, "event");
            int iSeq = Find(index, "local_receive_seq");
            int iRecv = Find(index, "unity_receive_ns");
            int iEv = Find(index, "unity_event_ns");
            int iStatus = Find(index, "status");
            int iRunId = Find(index, "run_id");
            int iSession = Find(index, "source_session_id");
            var iJoint = new int[JointCount];
            var iTarget = new int[JointCount];
            var iActual = new int[JointCount];
            var iValid = new int[JointCount];
            for (int j = 0; j < JointCount; j++)
            {
                iJoint[j] = Find(index, "input_j" + (j + 1) + "_rad");
                iTarget[j] = Find(index, "target_j" + (j + 1) + "_display_deg");
                iActual[j] = Find(index, "actual_j" + (j + 1) + "_display_deg");
                iValid[j] = Find(index, "actual_j" + (j + 1) + "_valid");
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
                    ActualDeg = new float[JointCount],
                    StatusRaw = iStatus >= 0 ? ParseString(f, iStatus) : null,
                    JointsValid = -1,
                    ControllerTimeValid = -1
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

                if (iValid[0] >= 0)
                {
                    bool anyProvided = false;
                    bool allTrue = true;
                    for (int j = 0; j < JointCount; j++)
                    {
                        string t = ParseString(f, iValid[j]);
                        if (string.IsNullOrEmpty(t))
                        {
                            continue;
                        }

                        anyProvided = true;
                        if (!(t == "True" || t == "true" || t == "1"))
                        {
                            allTrue = false;
                        }
                    }

                    if (anyProvided)
                    {
                        row.JointsValid = allTrue ? 1 : 0;
                    }
                }

                string rid = iRunId >= 0 ? ParseString(f, iRunId) : string.Empty;
                if (!string.IsNullOrEmpty(rid))
                {
                    _runIdSet.Add(rid);
                }

                if (string.IsNullOrEmpty(SourceSessionId) && iSession >= 0)
                {
                    SourceSessionId = ParseString(f, iSession);
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

            _rows = list.ToArray();
            TimeAxis = "unity_event_ns";
            TimeAxisReason = "samples.csv 只有 Unity 事件时间戳（unity_event_ns）；无 controller/ros/monotonic 列";
        }

        private void ParseRaw(string[] lines, Dictionary<string, int> index)
        {
            int iSeq = Find(index, "sample_seq");
            int iCtl = Find(index, "controller_time");
            int iRos = Find(index, "ros_time_ns");
            int iWall = Find(index, "host_wall_ns");
            int iMono = Find(index, "monotonic_ns");
            int iRunId = Find(index, "run_id");
            int iSession = Find(index, "source_session_id");
            var iJoint = new int[JointCount];
            var iVel = new int[JointCount];
            var iTorque = new int[JointCount];
            for (int j = 0; j < JointCount; j++)
            {
                iJoint[j] = Find(index, "j" + (j + 1) + "_deg");
                iVel[j] = Find(index, "j" + (j + 1) + "_vel_deg_s");
                iTorque[j] = Find(index, "joint_torque_" + (j + 1) + "_nm");
            }

            int iTcpX = Find(index, "tcp_x_mm");
            int iTcpY = Find(index, "tcp_y_mm");
            int iTcpZ = Find(index, "tcp_z_mm");
            int iTcpA = Find(index, "tcp_a_deg");
            int iTcpB = Find(index, "tcp_b_deg");
            int iTcpC = Find(index, "tcp_c_deg");
            int iFx = Find(index, "ext_force_fx_n");
            int iFy = Find(index, "ext_force_fy_n");
            int iFz = Find(index, "ext_force_fz_n");
            int iTx = Find(index, "ext_force_tx_nm");
            int iTy = Find(index, "ext_force_ty_nm");
            int iTz = Find(index, "ext_force_tz_nm");
            int iRm = Find(index, "robot_mode");
            int iRs = Find(index, "robot_state");
            int iCm = Find(index, "control_mode");
            int iSs = Find(index, "solution_space");
            int iSing = Find(index, "singularity");
            int iJv = Find(index, "joints_valid");
            int iCtv = Find(index, "controller_time_valid");

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
                    Seq = (int)ParseLong(f, iSeq),
                    Kind = KindReceive,
                    ControllerTime = ParseFloat(f, iCtl),
                    RosNs = ParseLong(f, iRos),
                    HostWallNs = ParseLong(f, iWall),
                    MonotonicNs = ParseLong(f, iMono),
                    JointsValid = ParseTriState(f, iJv),
                    ControllerTimeValid = ParseTriState(f, iCtv),
                    StatusRaw = null // rt_raw 无 status 列
                };

                row.JointDeg = ParseArray(f, iJoint, JointCount);
                row.JointVelDeg = ParseArray(f, iVel, JointCount);
                row.JointTorqueNm = ParseArray(f, iTorque, JointCount);
                row.TcpMm = ParseArray(f, new[] { iTcpX, iTcpY, iTcpZ }, 3);
                row.TcpAbcDeg = ParseArray(f, new[] { iTcpA, iTcpB, iTcpC }, 3);
                row.Force = ParseArray(f, new[] { iFx, iFy, iFz }, 3);
                row.Moment = ParseArray(f, new[] { iTx, iTy, iTz }, 3);
                row.DeviceRaw = new string[5];
                row.DeviceRaw[0] = DeviceToken(f, iRm);
                row.DeviceRaw[1] = DeviceToken(f, iRs);
                row.DeviceRaw[2] = DeviceToken(f, iCm);
                row.DeviceRaw[3] = DeviceToken(f, iSs);
                row.DeviceRaw[4] = DeviceToken(f, iSing);

                string rid = iRunId >= 0 ? ParseString(f, iRunId) : string.Empty;
                if (!string.IsNullOrEmpty(rid))
                {
                    _runIdSet.Add(rid);
                }

                if (string.IsNullOrEmpty(SourceSessionId) && iSession >= 0)
                {
                    SourceSessionId = ParseString(f, iSession);
                }

                list.Add(row);
            }

            _rows = list.ToArray();
            SelectTimeAxis();
        }

        /// <summary>
        /// 选回放时间轴：monotonic_ns（单调、不随 NTP 跳变）→ ros_time_ns → controller_time。
        /// 只接受"跨度 > 0 且非递减"的轴；controller_time 在 11-03/04 会话里几乎全重复，会被自动跳过。
        /// </summary>
        private void SelectTimeAxis()
        {
            if (_rows.Length == 0)
            {
                TimeAxis = "none";
                return;
            }

            string[] names = { "monotonic_ns", "ros_time_ns", "controller_time" };
            var getters = new Func<Row, long>[]
            {
                r => r.MonotonicNs,
                r => r.RosNs,
                r => (long)Math.Round(r.ControllerTime * 1e9)
            };

            for (int k = 0; k < names.Length; k++)
            {
                var g = getters[k];
                long first = g(_rows[0]);
                long last = g(_rows[_rows.Length - 1]);
                bool nonZero = first != 0 || last != 0;
                bool nonDecreasing = true;
                long prev = long.MinValue;
                int counted = 0;
                for (int i = 0; i < _rows.Length; i++)
                {
                    long v = g(_rows[i]);
                    if (v == 0)
                    {
                        continue;
                    }

                    counted++;
                    if (prev != long.MinValue && v < prev)
                    {
                        nonDecreasing = false;
                        break;
                    }

                    prev = v;
                }

                long span = last - first;
                if (nonZero && nonDecreasing && counted >= 2 && span > 0)
                {
                    for (int i = 0; i < _rows.Length; i++)
                    {
                        long v = g(_rows[i]);
                        _rows[i].T = v != 0 ? v : (first + i);
                    }

                    TimeAxis = names[k];
                    TimeAxisReason = k == 0
                        ? "monotonic_ns 非递减且跨度>0（宿主单调钟，不随 NTP 跳变）→ 首选"
                        : k == 1
                            ? "monotonic_ns 不可用（全 0/跳变/跨度=0），回退 ros_time_ns"
                            : "monotonic_ns 与 ros_time_ns 都不可用，回退 controller_time（按会话标注可信度）";
                    return;
                }
            }

            // 全部不可用：退化为行号轴，保证可回放但明确标注
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i].T = i * 10000000L; // 10 ms/行（100 Hz 名义），仅用于排序/推进
            }

            TimeAxis = "row_index";
            TimeAxisReason = "四时钟列均不可用（全 0/跳变/跨度=0），退化为行号轴（10 ms/行，非实测时间）";
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
                    // fix1 B(8)：旧实现只回绕时钟、不重置游标 → 回绕后没有任何新样本，
                    // "循环"实际表现为停在末帧。这里同时把游标放回 0 并记一次回绕。
                    _cursor = 0;
                    LoopWraps++;
                    LoopWrapAtNs = _startNs + (long)(_clockNs - _startNs);
                    _loopWrapPending = true;
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

        /// <summary>暂停/恢复**回放游标与回放时钟**推进（不写设置资产；生产默认不暂停）。</summary>
        public void SetHold(bool hold)
        {
            _hold = hold;
            _lastWall = Time.realtimeSinceStartup;
        }

        /// <summary>当前是否 hold（自检日志用）。</summary>
        public bool Hold => _hold;

        /// <summary>
        /// fix1 B(5)：生产暂停状态（与 <see cref="Hold"/> 同一执行点，只是语义名）。
        /// 暂停时 <see cref="Refresh"/> 不再推进时钟/游标，但快照仍按当前游标重建（读数不跳）。
        /// </summary>
        public bool Paused => _hold;

        /// <summary>设置生产暂停（唯一执行点由 TwinUiReplayFeed.Update 每帧同步）。</summary>
        public void SetPaused(bool paused)
        {
            if (_hold == paused)
            {
                return;
            }

            SetHold(paused);
        }

        /// <summary>本会话已发生的循环回绕次数（fix1 B(8) 证据；0 = 未回绕）。</summary>
        public int LoopWraps { get; private set; }

        /// <summary>最近一次回绕后的回放时间轴位置（ns，仅诊断用）。</summary>
        public long LoopWrapAtNs { get; private set; }

        /// <summary>
        /// 取走一次"刚刚发生回绕"标记（一次性）：feed 用它通知曲线分段/清缓冲，
        /// 保证曲线不跨周期连线。没有回绕返回 false。
        /// </summary>
        public bool ConsumeLoopWrap()
        {
            if (!_loopWrapPending)
            {
                return false;
            }

            _loopWrapPending = false;
            return true;
        }

        /// <summary>自检用：临时强制循环开/关（不写设置资产）。</summary>
        public void ForceLoop(bool loop)
        {
            _loopOverride = loop ? 1 : 0;
            _loop = loop;
        }

        /// <summary>解除 ForceLoop，恢复 settings.replayLoop。</summary>
        public void ReleaseLoop()
        {
            _loopOverride = -1;
        }

        /// <summary>当前是否循环（自检日志用）。</summary>
        public bool LoopEnabled => _loop;

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
                _loop = _loopOverride >= 0 ? _loopOverride == 1 : settings.replayLoop;
            }
            else if (_loopOverride >= 0)
            {
                _loop = _loopOverride == 1;
            }

            if (!Loaded)
            {
                _snapshot = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, TwinUITextR3.CtxReplay);
                _snapshot.Link.SourceName = "replay";
                _snapshot.Link.SourceKindLabel = Label;
                _snapshot.Notes = HeaderOnly
                    ? "无数据：文件表头合法但 0 数据行（schema=" + SchemaLabel + "）" + DescriptionSuffix()
                    : LoadError;
                _snapshot.Experiment = _context;
                _snapshot.SourceRunId = SourceRunId;
                _snapshot.SourceSessionId = SourceSessionId;
                _snapshot.IdentityUnique = IdentityUnique;
                _snapshot.IdentityNote = IdentityUnique ? string.Empty
                    : "文件内混用 " + DistinctRunIds + " 个 run_id";
                _snapshot.Clocks = BuildClocks();
                _snapshot.Status = TwinUiStatusInfo.From(string.Empty);
                _snapshot.Status.NotProvidedReason = HeaderOnly
                    ? "0 数据行"
                    : (StatusColumnProvided ? "未读取" : "无 status 列");
                _snapshot.Capabilities = StatusColumnProvided ? TwinUiCapability.StatusColumn : 0;
                _snapshot.Link.Quality = TwinUITextR3.NoData;
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
                if (dt > 0d && !_hold)
                {
                    Advance(dt);
                }
            }

            _snapshot = Build(settings);
        }

        private string DescriptionSuffix()
        {
            return string.IsNullOrEmpty(QualityContradiction) ? string.Empty : "；" + QualityContradiction;
        }

        private TwinUiSnapshot Build(TwinUiSettings settings)
        {
            var snap = TwinUiSnapshot.Empty(TwinUiSourceKind.Replay, TwinUITextR3.CtxReplay);
            snap.Experiment = _context;
            snap.Link.SourceName = "replay";
            snap.Link.SourceKindLabel = Label;
            snap.Link.Connected = true;
            snap.Link.HasFrame = _cursor > 0;
            snap.Link.UiHz = settings != null ? settings.uiRefreshHz : 0d;
            snap.Link.TargetSampleHz = TargetSampleHzConstant;
            snap.Link.TargetApplyHz = settings != null ? settings.robotApplyRateHz : 60d;
            snap.SourceRunId = SourceRunId;
            snap.SourceSessionId = SourceSessionId;
            snap.IdentityUnique = IdentityUnique;
            snap.IdentityNote = IdentityUnique
                ? (DirectoryRunIdMismatch ? "目录名≠行内 run_id" : string.Empty)
                : "文件内混用 " + DistinctRunIds + " 个 run_id";

            snap.Clocks = BuildClocks();
            snap.Status = TwinUiStatusInfo.From(Schema == TwinUiReplaySchema.Samples34 ? CurrentStatusRaw() : null);
            if (!snap.Status.Provided && !StatusColumnProvided)
            {
                snap.Status.NotProvidedReason = "无 status 列";
            }
            else if (!snap.Status.Provided)
            {
                snap.Status.NotProvidedReason = "本帧无值";
            }
            if (snap.Status.Provided)
            {
                snap.Capabilities |= TwinUiCapability.StatusColumn;
            }

            WindowReceiveRows = 0;
            WindowReceiveSpanSeconds = 0d;

            if (_cursor == 0)
            {
                snap.Link.Quality = TwinUITextR3.NoData;
                snap.Link.ClockSync = TwinUITextR3.CtxNotProvided;
                snap.Notes = BuildNotes(snap, "回放尚未推进到第一帧");
                return snap;
            }

            int end = Mathf.Min(_cursor, _rows.Length) - 1;
            var endRow = _rows[end];
            long lastT = endRow.T;
            long windowStart = lastT - (long)(_windowSeconds * 1e9);

            if (Schema == TwinUiReplaySchema.Samples34)
            {
                BuildSamplesMetrics(snap, windowStart, endRow);
            }
            else
            {
                BuildRawMetrics(snap, windowStart);
            }

            // ---- 通道映射（两种 schema 共用；缺通道不置位、不补 0）----
            if (Schema == TwinUiReplaySchema.Samples34)
            {
                MapSamplesChannels(snap, endRow);
            }
            else
            {
                MapRawChannels(snap, endRow);
            }

            snap.Link.Quality = StatusColumnProvided
                ? snap.Status.Display
                : TwinUITextR3.CtxNotProvided + "(无 status 列)";
            snap.Link.ClockSync = IdentityUnique
                ? TwinUITextR3.CtxNotProvided + "(四时钟域见 Notes)"
                : "速率不可用：身份混用";

            StatusLine = "replay: " + SchemaLabel + " rows=" + _rows.Length + " cursor=" + _cursor
                + " t=" + PlaybackSeconds.ToString("F1") + "s/" + TotalSeconds.ToString("F1") + "s"
                + " axis=" + TimeAxis
                + " sampleHz=" + snap.Link.SampleHz.ToString("F1")
                + (IdentityUnique ? string.Empty : " [身份混用]");
            snap.Notes = BuildNotes(snap, null);
            return snap;
        }

        private TwinUiClockDomains BuildClocks()
        {
            var c = new TwinUiClockDomains();
            c.TimeAxis = TimeAxis;
            c.TimeAxisReason = TimeAxisReason;
            if (Schema == TwinUiReplaySchema.Samples34)
            {
                c.Note = "samples.csv：只有 Unity 事件时间戳；无 controller/ros/monotonic/墙钟列";
                return c;
            }

            double ctlFirst = double.NaN, ctlLast = double.NaN;
            long rosFirst = 0, rosLast = 0, wallFirst = 0, wallLast = 0, monoFirst = 0, monoLast = 0;
            var ctlSet = new HashSet<double>();
            int ctlCount = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                var r = _rows[i];
                if (!float.IsNaN(r.ControllerTime) && r.ControllerTime > 0f)
                {
                    if (ctlCount == 0)
                    {
                        ctlFirst = r.ControllerTime;
                    }

                    ctlLast = r.ControllerTime;
                    ctlCount++;
                    ctlSet.Add(Math.Round(r.ControllerTime, 7));
                }

                if (r.RosNs != 0)
                {
                    if (rosFirst == 0)
                    {
                        rosFirst = r.RosNs;
                    }

                    rosLast = r.RosNs;
                }

                if (r.HostWallNs != 0)
                {
                    if (wallFirst == 0)
                    {
                        wallFirst = r.HostWallNs;
                    }

                    wallLast = r.HostWallNs;
                }

                if (r.MonotonicNs != 0)
                {
                    if (monoFirst == 0)
                    {
                        monoFirst = r.MonotonicNs;
                    }

                    monoLast = r.MonotonicNs;
                }
            }

            c.HasControllerTime = ctlCount > 0;
            c.HasRosTime = rosFirst != 0;
            c.HasHostWall = wallFirst != 0;
            c.HasMonotonic = monoFirst != 0;
            c.ControllerTimeDistinct = ctlSet.Count;
            c.ControllerTimeSpanSeconds = ctlCount > 1 ? ctlLast - ctlFirst : 0d;
            c.RosSpanSeconds = rosFirst != 0 ? (rosLast - rosFirst) / 1e9 : 0d;
            c.HostWallSpanSeconds = wallFirst != 0 ? (wallLast - wallFirst) / 1e9 : 0d;
            c.MonotonicSpanSeconds = monoFirst != 0 ? (monoLast - monoFirst) / 1e9 : 0d;
            c.Note = "controller_time: " + (c.ControllerTimeDistinct == 1
                ? "全文件仅 1 个值（几乎全重复）→ 不可当时间轴"
                : c.ControllerTimeSpanSeconds > 0.001d
                    ? "跨度 " + c.ControllerTimeSpanSeconds.ToString("F3") + "s / " + c.ControllerTimeDistinct + " 个不同值"
                    : "跨度为 0 → 不可当时间轴");
            return c;
        }

        private string CurrentStatusRaw()
        {
            int end = Mathf.Min(Mathf.Max(_cursor, 1), _rows.Length) - 1;
            return _rows[end].StatusRaw;
        }

        // ------------------------------------------------------------ 指标（两种 schema 分开口径）

        private void BuildSamplesMetrics(TwinUiSnapshot snap, long windowStart, Row endRow)
        {
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

            for (int i = _cursor - 1; i >= 0; i--)
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
            WindowReceiveRows = receiveCount;
            WindowReceiveSpanSeconds = receiveCount > 1 ? (lastReceiveT - firstReceiveT) / 1e9 : 0d;
            double applySpan = Math.Max(0.001d, (lastApplyT - firstApplyT) / 1e9);
            snap.Link.ApplyHz = applyCount > 1 ? (applyCount - 1) / applySpan : 0d;
            snap.Link.ReceiveToApplyMs = Median(latencies);
            snap.Link.JitterP99Ms = JitterP99(intervals);
            snap.Link.DroppedFrames = dropped;
            snap.Link.OutOfOrderFrames = 0;
            snap.Link.QueueLength = 0;
            snap.Link.FreshnessMs = Math.Max(0d, (_clockNs - lastReceiveRecv) / 1e6d);

            // 身份混用时速率不可用（两套帧被一起计数 → 179.66 Hz 这类假高值）
            if (!IdentityUnique)
            {
                snap.Link.SampleHz = 0d;
                snap.Link.ApplyHz = 0d;
            }
        }

        private void BuildRawMetrics(TwinUiSnapshot snap, long windowStart)
        {
            int count = 0;
            long firstT = 0;
            long lastT = 0;
            var intervals = new List<float>(4096);
            long prevT = 0;
            long dropped = 0;
            int lastSeq = int.MinValue;

            for (int i = _cursor - 1; i >= 0; i--)
            {
                var row = _rows[i];
                if (row.T < windowStart)
                {
                    break;
                }

                count++;
                if (lastT == 0)
                {
                    lastT = row.T;
                }

                firstT = row.T;
                if (prevT != 0)
                {
                    intervals.Add((prevT - row.T) / 1e6f);
                }

                prevT = row.T;
                if (lastSeq != int.MinValue && row.Seq > lastSeq + 1)
                {
                    dropped += row.Seq - lastSeq - 1;
                }

                if (row.Seq > lastSeq)
                {
                    lastSeq = row.Seq;
                }
            }

            double spanSec = Math.Max(0.001d, (lastT - firstT) / 1e9);
            snap.Link.SampleHz = count > 1 ? (count - 1) / spanSec : 0d;
            WindowReceiveRows = count;
            WindowReceiveSpanSeconds = count > 1 ? (lastT - firstT) / 1e9 : 0d;
            snap.Link.ApplyHz = 0d;   // 原始 CSV 无 apply 事件 → 不适用（缺失，不补）
            snap.Link.ReceiveToApplyMs = 0d;
            snap.Link.JitterP99Ms = JitterP99(intervals);
            snap.Link.DroppedFrames = dropped;
            snap.Link.OutOfOrderFrames = 0;
            snap.Link.QueueLength = 0;
            snap.Link.FreshnessMs = 0d; // 回放推进的是录制时间轴，不是"数据新鲜度"
            if (!IdentityUnique)
            {
                snap.Link.SampleHz = 0d;
            }
        }

        // ------------------------------------------------------------ 通道映射

        private void MapSamplesChannels(TwinUiSnapshot snap, Row endRow)
        {
            // 关节位置：取最后一个 receive 行（rad → deg 是**显示需要**的一次换算；
            // A 支为 raw 另开 JointPosDeg 直通路径，避免 deg→rad→deg 往返误差）
            for (int i = _cursor - 1; i >= 0 && i > _cursor - 400; i--)
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

            // 有效位：samples.csv 的 actual_jN_valid（不是 joints_valid，但语义同类，注明来源）
            if (endRow.JointsValid >= 0)
            {
                snap.Validity.JointsValidProvided = true;
                snap.Validity.JointsValid = endRow.JointsValid == 1;
                snap.Capabilities |= TwinUiCapability.Validity;
            }

            // 能力位：samples.csv 没有这些通道 → 不置位（界面显示未订阅）
            snap.RawTcp.HasRawTcp = false;
            snap.Force.HasForce = false;
            snap.Force.HasMoment = false;
            snap.Device.Present = false;
        }

        private void MapRawChannels(TwinUiSnapshot snap, Row endRow)
        {
            if (endRow.JointDeg != null)
            {
                snap.Joints.PositionDeg = (float[])endRow.JointDeg.Clone();
                snap.Capabilities |= TwinUiCapability.JointPosition;
            }

            if (endRow.JointVelDeg != null)
            {
                snap.Joints.VelocityDegPerSec = (float[])endRow.JointVelDeg.Clone();
                snap.Capabilities |= TwinUiCapability.JointVelocity;
            }

            if (endRow.JointTorqueNm != null)
            {
                snap.Joints.TorqueNm = (float[])endRow.JointTorqueNm.Clone();
                snap.Capabilities |= TwinUiCapability.JointTorque;
            }

            if (endRow.TcpMm != null || endRow.TcpAbcDeg != null)
            {
                snap.RawTcp.HasRawTcp = true;
                snap.RawTcp.PositionMm = endRow.TcpMm ?? new float[0];
                snap.RawTcp.AbcDeg = endRow.TcpAbcDeg ?? new float[0];
                snap.RawTcp.Frame = "UNKNOWN";
                snap.RawTcp.EulerConvention = "UNKNOWN";
                snap.RawTcp.SourceColumns = "tcp_x_mm,tcp_y_mm,tcp_z_mm,tcp_a_deg,tcp_b_deg,tcp_c_deg";
                snap.Capabilities |= TwinUiCapability.TcpPose | TwinUiCapability.RawTcpComponents;
            }

            if (endRow.Force != null)
            {
                snap.Force.HasForce = true;
                snap.Force.Force = (float[])endRow.Force.Clone();
                snap.Capabilities |= TwinUiCapability.Force;
            }

            if (endRow.Moment != null)
            {
                snap.Force.HasMoment = true;
                snap.Force.Moment = (float[])endRow.Moment.Clone();
                snap.Capabilities |= TwinUiCapability.Force;
            }

            if (endRow.DeviceRaw != null)
            {
                snap.Device.Present = true;
                snap.Device.RobotMode = endRow.DeviceRaw[0];
                snap.Device.RobotState = endRow.DeviceRaw[1];
                snap.Device.ControlMode = endRow.DeviceRaw[2];
                snap.Device.SolutionSpace = endRow.DeviceRaw[3];
                snap.Device.Singularity = endRow.DeviceRaw[4];
                snap.Device.Semantics = "UNKNOWN"; // 数值语义映射无原始依据，禁止编造
                snap.Capabilities |= TwinUiCapability.DeviceState;
            }

            if (endRow.JointsValid >= 0 || endRow.ControllerTimeValid >= 0)
            {
                snap.Validity.JointsValidProvided = endRow.JointsValid >= 0;
                snap.Validity.JointsValid = endRow.JointsValid == 1;
                snap.Validity.ControllerTimeValidProvided = endRow.ControllerTimeValid >= 0;
                snap.Validity.ControllerTimeValid = endRow.ControllerTimeValid == 1;
                snap.Capabilities |= TwinUiCapability.Validity;
            }

            snap.Joints.HasTarget = false; // rt_raw 无目标/误差通道
        }

        private string BuildNotes(TwinUiSnapshot snap, string prefix)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix))
            {
                sb.Append(prefix).Append("；");
            }

            sb.Append("schema=").Append(SchemaLabel);
            sb.Append("；回放轴=").Append(TimeAxis).Append("（").Append(TimeAxisReason).Append("）");
            sb.Append("；status=").Append(snap.Status.Display);
            sb.Append("；TCP 坐标系/欧拉约定=UNKNOWN（只显示原始分量，不推断）");
            sb.Append("；mm/°/N/N·m 原样，无单位换算");

            var missing = new List<string>();
            if (!TwinUiCapability.Has(snap.Capabilities, TwinUiCapability.JointVelocity))
            {
                missing.Add("关节速度");
            }

            if (!TwinUiCapability.Has(snap.Capabilities, TwinUiCapability.JointTorque))
            {
                missing.Add("关节力矩");
            }

            if (!TwinUiCapability.Has(snap.Capabilities, TwinUiCapability.Force))
            {
                missing.Add("六维力/力矩");
            }

            if (!snap.RawTcp.HasRawTcp)
            {
                missing.Add("TCP");
            }

            if (!snap.Device.Present)
            {
                missing.Add("设备状态");
            }

            if (!snap.Validity.JointsValidProvided)
            {
                missing.Add("joints_valid");
            }

            if (missing.Count > 0)
            {
                sb.Append("；本文件无：").Append(string.Join("/", missing.ToArray())).Append("（显示未订阅，不补 0）");
            }

            if (snap.Clocks.HasControllerTime)
            {
                sb.Append("；controller_time：").Append(snap.Clocks.ControllerTimeDistinct).Append(" 个不同值/跨度 ")
                    .Append(snap.Clocks.ControllerTimeSpanSeconds.ToString("F3")).Append("s");
            }

            if (!IdentityUnique)
            {
                sb.Append("；身份混用 ").Append(DistinctRunIds).Append(" 个 run_id → 速率不可用");
            }
            else if (DirectoryRunIdMismatch)
            {
                sb.Append("；身份错配：目录 ").Append(DirectoryName).Append(" ≠ 行内 ").Append(SourceRunId);
            }

            if (!string.IsNullOrEmpty(QualityContradiction))
            {
                sb.Append("；").Append(QualityContradiction);
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------ 上下文（runId/profile/quality）

        private void LoadContext(string runDir)
        {
            string runId = DirectoryName;
            if (string.IsNullOrEmpty(runId))
            {
                runId = string.IsNullOrEmpty(runDir)
                    ? string.Empty
                    : Path.GetFileName(runDir.TrimEnd(Path.DirectorySeparatorChar));
            }

            _context.RunId = string.IsNullOrEmpty(runId) ? TwinUITextR3.CtxNotProvided : runId;
            _context.Profile = TwinUITextR3.CtxNotProvided;
            _context.Condition = ConditionFromRunId(runId);
            _context.PayloadKg = TwinUITextR3.CtxNotProvided;
            _context.TcpDefinition = TwinUITextR3.CtxNotProvided;
            _context.SessionId = string.IsNullOrEmpty(SourceSessionId) ? _context.RunId : SourceSessionId;
            _context.SelfCheckStatus = "未校验";

            // rt_raw 副本通常在 <session>/raw/ 下，profile/quality 在上一层会话目录
            string sessionDir = runDir;
            if (string.Equals(Path.GetFileName(runDir.TrimEnd(Path.DirectorySeparatorChar)), "raw",
                    StringComparison.OrdinalIgnoreCase))
            {
                sessionDir = Path.GetDirectoryName(runDir) ?? runDir;
            }

            string profilePath = Path.Combine(sessionDir, "profile.json");
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

            string qualityPath = Path.Combine(sessionDir, "quality.json");
            if (File.Exists(qualityPath))
            {
                string json = File.ReadAllText(qualityPath);
                string flushed = JsonString(json, "flushed");
                string rows = JsonNumber(json, "written_rows");
                _context.SelfCheckStatus = "flushed=" + (flushed ?? "?") + " / written_rows=" + (rows ?? "?");
                _context.SelfCheckPassed = flushed == "true";

                // 0 数据行 + quality 声明 written_rows>0 = 真矛盾（35 会话里 9 个）。
                // 注意：正常会话的 written_rows 与 samples.csv 行数本来就不等（19421 vs 10778），
                // 所以只在"本文件 0 数据行"时才判矛盾。
                long declared;
                if (RowCount == 0 && long.TryParse(rows, NumberStyles.Integer, CultureInfo.InvariantCulture, out declared)
                    && declared > 0)
                {
                    QualityContradiction = "quality.json 声明 written_rows=" + declared
                        + "/flushed=" + (flushed ?? "?") + "，但本文件 0 数据行 → 矛盾（以文件为准：无数据）";
                }
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

        // ------------------------------------------------------------ 基础解析

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

        /// <summary>
        /// RFC4180 风格拆分：支持双引号包裹与 "" 转义（旧实现用 line.Split(',')，
        /// 遇到带引号/逗号的字段会错列）。
        /// </summary>
        private static string[] SplitCsv(string line)
        {
            if (line.IndexOf('"') < 0)
            {
                return line.Split(',');
            }

            var fields = new List<string>(64);
            var sb = new StringBuilder(64);
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields.ToArray();
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

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                return float.NaN; // NaN/Inf/非数字一律按缺失处理（不补 0）
            }

            return float.IsNaN(v) || float.IsInfinity(v) ? float.NaN : v;
        }

        /// <summary>整组解析：任一值缺失/非有限 → 整组视为缺失（避免半组假数据）。</summary>
        private static float[] ParseArray(string[] fields, int[] indices, int count)
        {
            if (indices == null || indices.Length < count)
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                if (indices[i] < 0)
                {
                    return null;
                }
            }

            var arr = new float[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = ParseFloat(fields, indices[i]);
                if (float.IsNaN(arr[i]))
                {
                    return null;
                }
            }

            return arr;
        }

        private static string DeviceToken(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return "UNKNOWN";
            }

            string s = fields[index].Trim().Trim('"');
            return s.Length == 0 ? "UNKNOWN" : s;
        }

        /// <summary>-1 = 列不存在/空；0 = False；1 = True。</summary>
        private static int ParseTriState(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return -1;
            }

            string s = fields[index].Trim().Trim('"');
            if (s.Length == 0)
            {
                return -1;
            }

            if (s == "True" || s == "true" || s == "1")
            {
                return 1;
            }

            if (s == "False" || s == "false" || s == "0")
            {
                return 0;
            }

            return -1;
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