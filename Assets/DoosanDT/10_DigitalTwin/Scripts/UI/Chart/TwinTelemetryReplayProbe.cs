// Self-test only: replays a recorded `unity_receive.csv` into the chart.
//
// This component exists so the chart can be proven to draw real curves WITHOUT connecting ROS2,
// without starting a stream and without recording anything. It is deliberately a separate object
// (inactive by default, never referenced by DigitalTwinRuntime), so it can never alter the
// experiment data path:
//   * it only *reads* a CSV file,
//   * it only pushes samples into TwinTelemetryChart through TwinTelemetryChartBinder,
//   * it never writes a file, never publishes a command, never touches the recorder.
//
// Columns are resolved from the header, so both the paper-recorder layout
// (seq, joint_*_rad, joint_*_torque_nm, force_x..torque_z) and the Chapter4 layout parse.
// Rows with an empty joint block are skipped and counted in the report.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DigitalTwin
{
    public sealed class TwinTelemetryReplayProbe : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private string csvPath = string.Empty;
        [SerializeField] private bool loadOnEnable;

        [Header("Playback")]
        [SerializeField] private float replayHz = 20f;
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool loop;
        [SerializeField] private bool playOnEnable;

        [Header("Flags for the evidence log")]
        [SerializeField] private bool hasJointAngle;
        [SerializeField] private bool hasWrench;
        [SerializeField] private bool hasJointTorque;
        [SerializeField] private int loadedRows;
        [SerializeField] private int skippedRows;

        public sealed class SampleRow
        {
            public float Time;
            public float[] JointDeg;
            public float[] Wrench;
            public float[] Torque;
        }

        private readonly List<SampleRow> _rows = new List<SampleRow>();
        private int _cursor;
        private float _clock;
        private bool _loaded;

        public bool IsPlaying { get; private set; }
        public bool Loaded => _loaded;
        public int RowCount => _rows.Count;
        public bool HasWrenchData => hasWrench;
        public bool HasJointTorqueData => hasJointTorque;
        public bool HasJointAngleData => hasJointAngle;
        public string CsvPath => csvPath;

        public string StatusMessage { get; private set; } = "not loaded";

        private void OnEnable()
        {
            if (loadOnEnable && !_loaded)
            {
                Load();
            }

            if (playOnEnable && _loaded)
            {
                Play();
            }
        }

        /// <summary>Parse the CSV. Safe to call repeatedly; returns false on any read error.</summary>
        public bool Load()
        {
            _rows.Clear();
            _cursor = 0;
            _clock = 0f;
            _loaded = false;
            loadedRows = 0;
            skippedRows = 0;

            if (string.IsNullOrEmpty(csvPath))
            {
                StatusMessage = "csv path is empty";
                return false;
            }

            if (!File.Exists(csvPath))
            {
                StatusMessage = "csv not found: " + csvPath;
                return false;
            }

            try
            {
                using (StreamReader reader = new StreamReader(csvPath))
                {
                    string headerLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(headerLine))
                    {
                        StatusMessage = "csv is empty";
                        return false;
                    }

                    string[] header = headerLine.Split(',');
                    Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < header.Length; i++)
                    {
                        string key = header[i].Trim();
                        if (!index.ContainsKey(key))
                        {
                            index[key] = i;
                        }
                    }

                    int[] jointCols = new int[6];
                    int[] torqueCols = new int[6];
                    int[] wrenchCols = { -1, -1, -1, -1, -1, -1 };
                    string[] wrenchNames = { "force_x", "force_y", "force_z", "torque_x", "torque_y", "torque_z" };

                    bool jointsMapped = true;
                    for (int i = 0; i < 6; i++)
                    {
                        jointCols[i] = ColumnOf(index, "joint_" + (i + 1) + "_rad");
                        torqueCols[i] = ColumnOf(index, "joint_" + (i + 1) + "_torque_nm");
                        if (jointCols[i] < 0)
                        {
                            jointsMapped = false;
                        }
                    }

                    bool wrenchMapped = true;
                    for (int i = 0; i < 6; i++)
                    {
                        wrenchCols[i] = ColumnOf(index, wrenchNames[i]);
                        if (wrenchCols[i] < 0)
                        {
                            wrenchMapped = false;
                        }
                    }

                    if (!jointsMapped)
                    {
                        StatusMessage = "header is missing joint_*_rad columns";
                        return false;
                    }

                    int timeCol = ColumnOf(index, "unity_receive_wall_ms");
                    if (timeCol < 0)
                    {
                        timeCol = ColumnOf(index, "unity_receive_ns");
                    }

                    if (timeCol < 0)
                    {
                        timeCol = ColumnOf(index, "source_ts_ms");
                    }

                    float firstTime = 0f;
                    bool hasFirst = false;
                    int lineNumber = 0;

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (line.Length == 0)
                        {
                            continue;
                        }

                        string[] parts = line.Split(',');
                        if (parts.Length <= jointCols[5])
                        {
                            skippedRows++;
                            continue;
                        }

                        var row = new SampleRow { JointDeg = new float[6] };
                        bool ok = true;
                        for (int i = 0; i < 6; i++)
                        {
                            if (!TryParse(parts[jointCols[i]], out float rad))
                            {
                                ok = false;
                                break;
                            }

                            row.JointDeg[i] = rad * Mathf.Rad2Deg;
                        }

                        if (!ok)
                        {
                            skippedRows++;
                            continue;
                        }

                        if (wrenchMapped && wrenchCols[5] < parts.Length)
                        {
                            var wrench = new float[6];
                            bool all = true;
                            for (int i = 0; i < 6; i++)
                            {
                                if (wrenchCols[i] >= parts.Length || !TryParse(parts[wrenchCols[i]], out float v))
                                {
                                    all = false;
                                    break;
                                }

                                wrench[i] = v;
                            }

                            if (all)
                            {
                                row.Wrench = wrench;
                                hasWrench = true;
                            }
                        }

                        var torque = new float[6];
                        bool allTorque = true;
                        for (int i = 0; i < 6; i++)
                        {
                            if (torqueCols[i] < 0 || torqueCols[i] >= parts.Length || !TryParse(parts[torqueCols[i]], out float v))
                            {
                                allTorque = false;
                                break;
                            }

                            torque[i] = v;
                        }

                        if (allTorque)
                        {
                            row.Torque = torque;
                            hasJointTorque = true;
                        }

                        float t = 0f;
                        if (timeCol >= 0 && timeCol < parts.Length)
                        {
                            double raw;
                            if (double.TryParse(parts[timeCol], NumberStyles.Float, CultureInfo.InvariantCulture, out raw))
                            {
                                bool ns = header[timeCol].IndexOf("_ns", StringComparison.OrdinalIgnoreCase) >= 0;
                                t = (float)(ns ? raw / 1e9 : raw / 1000.0);
                            }
                        }

                        if (!hasFirst)
                        {
                            firstTime = t;
                            hasFirst = true;
                        }

                        row.Time = t - firstTime;
                        _rows.Add(row);
                        loadedRows++;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "csv error: " + ex.Message;
                _rows.Clear();
                return false;
            }

            hasJointAngle = _rows.Count > 0;
            _loaded = _rows.Count > 0;
            StatusMessage = _loaded
                ? "loaded " + _rows.Count + " rows (skipped " + skippedRows + ")"
                : "no usable rows";
            return _loaded;
        }

        public void Play()
        {
            if (!_loaded && !Load())
            {
                return;
            }

            IsPlaying = true;
            _clock = 0f;
            _cursor = 0;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Rewind()
        {
            _cursor = 0;
            _clock = 0f;
        }

        public void SetCsvPath(string path)
        {
            csvPath = path;
            _loaded = false;
        }

        public void SetReplayHz(float hz)
        {
            replayHz = Mathf.Max(0.5f, hz);
        }

        // ---------------------------------------------------------- row accessors
        // Used by the editor preview and the Play self-test driver, which walk the fixture rows
        // directly instead of waiting for the playhead.

        public float RowTime(int index)
        {
            return index >= 0 && index < _rows.Count ? _rows[index].Time : 0f;
        }

        public float[] RowJointDeg(int index)
        {
            return index >= 0 && index < _rows.Count ? _rows[index].JointDeg : null;
        }

        public float[] RowWrench(int index)
        {
            return index >= 0 && index < _rows.Count ? _rows[index].Wrench : null;
        }

        public float[] RowTorque(int index)
        {
            return index >= 0 && index < _rows.Count ? _rows[index].Torque : null;
        }

        /// <summary>The most recent row reached by the playhead (null before playback starts).</summary>
        public SampleRow CurrentRow
        {
            get
            {
                if (_rows.Count == 0)
                {
                    return null;
                }

                int i = Mathf.Clamp(_cursor - 1, 0, _rows.Count - 1);
                return _rows[i];
            }
        }

        private void Update()
        {
            if (!IsPlaying || _rows.Count == 0)
            {
                return;
            }

            _clock += Time.unscaledDeltaTime * Mathf.Max(0.01f, speed);
            float sampleInterval = 1f / Mathf.Max(0.5f, replayHz);
            int guard = 0;
            while (_cursor < _rows.Count && _rows[_cursor].Time <= _clock && guard++ < 256)
            {
                _cursor++;
            }

            if (_cursor >= _rows.Count)
            {
                if (loop)
                {
                    _cursor = 0;
                    _clock = 0f;
                }
                else
                {
                    IsPlaying = false;
                    StatusMessage = "finished at row " + _rows.Count;
                }
            }
        }

        private static int ColumnOf(Dictionary<string, int> index, string name)
        {
            return index.TryGetValue(name, out int value) ? value : -1;
        }

        private static bool TryParse(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}