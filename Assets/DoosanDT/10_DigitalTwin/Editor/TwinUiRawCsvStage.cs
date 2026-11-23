// TwinUiRawCsvStage.cs —— A 支（阶段一 A）逐值对照 Play 自检：UI/62
//
// 目的：用**生产入口**（TwinUiSourceSwitch.SetReplay → TwinUiReplayFeed → ReplayTwinUiDataSource）
//       把真实 RT 原始 CSV（40/46 列）与 samples.csv 喂进同一套卡片，然后在 **Play 中真实读取
//       TMP_Text.text**，逐字段落盘：原始行（由 Python 按 cursorRow 回查）→ 快照值 → UI 文本。
//
// 覆盖：
//   * rt_raw 46 列（含身份/有效位列）与 40 列（无身份/有效位列，缺列标 UNKNOWN/未提供）；
//   * samples.csv 正常候选（status=accepted）与异常身份输入（identity_mismatch + 目录≠行内 run_id）；
//   * 表头合法但 0 数据行（samples.csv 561 B）→ 必须显示"无数据"，不是沿用旧源；
//   * 合成样例（a/testdata/SYNTHETIC_*，**只验通路**，不进真实证据表）：动态数值 + 缺通道；
//   * 坏 CSV 的 rollback（旧源逐项不变）。
//
// 边界：只读 CSV；不连 ROS/DART、不写记录、不下发命令、不改场景。输出只写本批 a/logs。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiRawCsvStage
    {
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a";
        const string Armed = "Phase1abc.Ui62RawCsv";
        const string WslRaw = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\cursor_2026-11-08_thesis_evidence\02_evidence\wsl_sessions\full";
        const string Paper = @"D:\Unity Projects\DART_R-data\PaperLogs";

        struct Case
        {
            public string Id;
            public string Path;
            public float[] Seeks;
            public string Expect;

            public Case(string id, string path, float[] seeks, string expect)
            {
                Id = id;
                Path = path;
                Seeks = seeks;
                Expect = expect;
            }
        }

        static Case[] _cases;
        static int _caseIndex;
        static int _seekIndex;
        static bool _xrIsolated;
        static StringBuilder _tsv;
        static StringBuilder _log;
        static float _next;
        static double _deadline;
        static int _fails;
        static int _step;
        static string _stamp;
        static string _tsvPath;
        static string _txtPath;

        static TwinUiWindowHost _window;
        static TwinUiShellV2 _shell;
        static TwinUiSurfaceHost _surface;

        static TwinUiRawCsvStage()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("DigitalTwin/UI/62 Raw CSV Values (auto-exit)", false, 620)]
        public static void Run()
        {
            _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _tsv = new StringBuilder();
            _log = new StringBuilder();
            _fails = 0;
            _step = 0;
            _caseIndex = 0;
            _seekIndex = 0;
            _deadline = EditorApplication.timeSinceStartup + 300d;
            SessionState.SetBool(Armed, true);
            SessionState.SetBool(Armed + ".started", true);
            Directory.CreateDirectory(Root + "/logs");
            TwinUiViewFreeze.Reset();
            Log("START ui62 stamp=" + _stamp);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static Case[] BuildCases()
        {
            string raw = WslRaw + @"\session_20261103_135616_529090_E4_B\raw\rt_raw_20261103_135625.csv";
            string rawE1 = WslRaw + @"\session_20261104_202820_614701_E1_A\raw\rt_raw_20261104_202825.csv";
            string raw40 = @"\\wsl.localhost\Ubuntu-22.04\home\zyq\ros2_ws\src\docs\ros2-unity\logs"
                + @"\session_20260726_180656_E1_static_001_100hz\rt_raw_20260726_180710.csv";
            return new[]
            {
                new Case("RT46_20261103_135616_529090_E4_B", raw, new[] { 0f, 0.5f, 1f },
                    "46列/全部通道/静态重复样本/status未提供"),
                new Case("RT46_20261104_202820_614701_E1_A", rawE1, new[] { 0f, 1f },
                    "46列/第二会话/身份单一"),
                new Case("RT40_20260726_180656_E1_static_001", raw40, new[] { 0f, 0.5f, 1f },
                    "40列/无身份与有效位列/关节静态+力力矩噪声"),
                new Case("SAMPLES_NORMAL_20261103_135616_529090_E4_B",
                    Paper + @"\20261103_135616_529090_E4_B\samples.csv", new[] { 0f, 0.5f, 1f },
                    "accepted/无TCP力力矩/关节rad"),
                new Case("SAMPLES_MISMATCH_20261104_204838_488192_E4_B",
                    Paper + @"\20261104_204838_488192_E4_B\samples.csv", new[] { 0f, 0.5f, 1f },
                    "identity_mismatch/目录≠行内run_id/必须非正常"),
                new Case("SAMPLES_HEADER_ONLY_20261029_215430_937229_E4_B",
                    Paper + @"\20261029_215430_937229_E4_B\samples.csv", new[] { 0f },
                    "0数据行/quality矛盾/必须显示无数据"),
                new Case("SYNTHETIC_dynamic_46col",
                    Root + @"\testdata\SYNTHETIC_dynamic_46col.csv", new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
                    "合成样例：数值随游标变化（只验通路，不进真实证据）"),
                new Case("SYNTHETIC_missing_channels_46col",
                    Root + @"\testdata\SYNTHETIC_missing_channels_46col.csv", new[] { 0f, 1f },
                    "合成样例：TCP/力/力矩列全空 → 未订阅，不补0"),
                new Case("SYNTHETIC_nan_inf_46col",
                    Root + @"\testdata\SYNTHETIC_nan_inf_46col.csv", new[] { 0f, 0.5f },
                    "合成样例：NaN/Inf/空 字段 → 整组按缺失（未订阅），不补0"),
            };
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !Application.isPlaying || EditorApplication.isCompiling)
            {
                return;
            }

            if (!_xrIsolated)
            {
                _xrIsolated = true;
                string names;
                int closed = TwinUiTestIsolation.DisableXrAffordances(out names);
                if (_log != null)
                {
                    _log.AppendLine("INFO XRI providers disabled=" + closed + " | " + names);
                }
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (_log == null)
            {
                if (!SessionState.GetBool(Armed + ".started", false))
                {
                    SessionState.SetBool(Armed, false);
                    return;
                }

                _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _tsv = new StringBuilder();
                _log = new StringBuilder();
                _fails = 0;
                _step = 0;
                _caseIndex = 0;
                _seekIndex = 0;
                _deadline = EditorApplication.timeSinceStartup + 300d;
                Log("resumed after domain reload");
            }

            if (_deadline <= 0d)
            {
                _deadline = EditorApplication.timeSinceStartup + 300d;
            }

            if (EditorApplication.timeSinceStartup > _deadline)
            {
                Fail("watchdog", "300 s at step=" + _step + " case=" + _caseIndex);
                Finish();
                return;
            }

            if (EditorApplication.timeSinceStartup < _next)
            {
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Fail("exception step " + _step, e.ToString());
                Finish();
            }
        }

        static void Step()
        {
            var cases = _cases ?? (_cases = BuildCases());

            if (_step == 0)
            {
                _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                _shell = UnityEngine.Object.FindObjectOfType<TwinUiShellV2>();
                _surface = TwinUiSurfaceHost.Instance;
                if (_surface == null)
                {
                    _surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
                }

                if (_window == null || _shell == null || _surface == null || _surface.Page == null)
                {
                    Retry("boot refs");
                    return;
                }

                Rect r = _window.WindowRect;
                r.width = 1600f;
                r.height = 900f;
                r.x = 16f;
                r.y = 60f;
                _window.ApplyLayout(r, false, false);
                _surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                _step = 1;
                _caseIndex = 0;
                _seekIndex = 0;
                Next(1.0d);
                return;
            }

            if (_step == 1)
            {
                if (_caseIndex >= cases.Length)
                {
                    RollbackCheck();
                    return;
                }

                var c = cases[_caseIndex];
                // 空会话用 allowEmpty 显式入口：默认 SetReplay 会把 0 数据行当"不可切换"（R2 语义）。
                bool allowEmpty = c.Id.StartsWith("SAMPLES_HEADER_ONLY", StringComparison.Ordinal);
                bool ok = TwinUiSourceSwitch.SetReplay(c.Path, allowEmpty);
                var feed = TwinUiReplayFeed.Active;
                // 逐值对照要能停在**末帧**：自检期间关掉循环（只改本数据源实例，不写设置资产）。
                if (feed != null && feed.Source != null)
                {
                    feed.Source.ForceLoop(false);
                }

                string src = feed != null && feed.Source != null ? feed.Source.SchemaLabel : "none";
                Log("CASE " + c.Id + " attach=" + (ok ? "ok" : "FAIL")
                    + " schema=" + src
                    + " rows=" + (feed != null ? feed.RowCount : -1)
                    + " headerOnly=" + (feed != null && feed.HeaderOnly)
                    + " expect=" + c.Expect
                    + " err=" + TwinUiSourceSwitch.LastError);
                TsV("CASE", c.Id, "-", "attach", ok ? "ok" : "FAIL");
                TsV("CASE", c.Id, "-", "schema", src);
                TsV("CASE", c.Id, "-", "rows", (feed != null ? feed.RowCount : -1).ToString());
                TsV("CASE", c.Id, "-", "path", c.Path);
                TsV("CASE", c.Id, "-", "expect", c.Expect);

                if (feed != null && feed.Source != null)
                {
                    var s = feed.Source;
                    TsV("META", c.Id, "-", "schemaLabel", s.SchemaLabel);
                    TsV("META", c.Id, "-", "timeAxis", s.TimeAxis);
                    TsV("META", c.Id, "-", "timeAxisReason", s.TimeAxisReason);
                    TsV("META", c.Id, "-", "identityUnique", s.IdentityUnique ? "1" : "0");
                    TsV("META", c.Id, "-", "distinctRunIds", s.DistinctRunIds.ToString());
                    TsV("META", c.Id, "-", "runId", s.SourceRunId);
                    TsV("META", c.Id, "-", "sourceSessionId", s.SourceSessionId);
                    TsV("META", c.Id, "-", "directory", s.DirectoryName);
                    TsV("META", c.Id, "-", "dirRunIdMismatch", s.DirectoryRunIdMismatch ? "1" : "0");
                    TsV("META", c.Id, "-", "headerOnly", s.HeaderOnly ? "1" : "0");
                    TsV("META", c.Id, "-", "qualityContradiction", s.QualityContradiction);
                    TsV("META", c.Id, "-", "statusColumnProvided", s.StatusColumnProvided ? "1" : "0");
                }

                _seekIndex = 0;
                _step = 2;
                Next(0.8d);
                return;
            }

            if (_step == 2)
            {
                var c = cases[_caseIndex];
                var feed = TwinUiReplayFeed.Active;
                if (feed == null || feed.Source == null)
                {
                    _caseIndex++;
                    _step = 1;
                    Next(0.2d);
                    return;
                }

                if (_seekIndex >= c.Seeks.Length)
                {
                    _caseIndex++;
                    _seekIndex = 0;
                    _step = 1;
                    Next(0.5d);
                    return;
                }

                feed.Source.SeekFraction(c.Seeks[_seekIndex]);
                // 逐值对照要求快照与卡片文本来自**同一游标**：seek 后 hold 住游标，
                // 等 binder(5Hz) 与卡片都收敛到同一帧再读（先解 hold，读前再 seek 一次）。
                feed.Source.SetHold(true);
                _step = 3;
                Next(0.9d); // 至少跨过 3 次 5 Hz binder 采样
                return;
            }

            if (_step == 3)
            {
                var c = cases[_caseIndex];
                var feed = TwinUiReplayFeed.Active;
                float f = c.Seeks[_seekIndex];
                string sample = "s" + _seekIndex + "_f" + f.ToString("F2");
                // 边界：seek(0) 后若游标仍为 -1（有数据行却没推进到第一帧），再 seek 一次并多等一帧，
                // 否则会拿"空快照"去和原始行对照（会伪造成 FAIL）。
                if (feed != null && feed.Source != null && feed.Source.CursorIndex < 0
                    && feed.RowCount > 0 && !_reseek)
                {
                    _reseek = true;
                    feed.Source.SetHold(false);
                    feed.Source.SeekFraction(f <= 0f ? 0.0002f : f);
                    feed.Source.SetHold(true);
                    Log("reseek " + c.Id + " f=" + f + " rows=" + feed.RowCount);
                    Next(0.7d);
                    return;
                }

                _reseek = false;
                DumpCase(c, feed, sample, f);
                if (feed.Source != null)
                {
                    feed.Source.SetHold(false);
                }

                _seekIndex++;
                _step = 2;
                Next(0.25d);
                return;
            }

            Finish();
        }

        static void RollbackCheck()
        {
            var feed = TwinUiReplayFeed.Active;
            string beforeCsv = TwinUiSourceSwitch.CurrentCsv;
            int beforeRows = feed != null ? feed.RowCount : -1;
            bool beforeReplay = TwinUiSourceSwitch.IsReplay;
            string beforeRun = TwinUiSourceSwitch.CurrentRunId;

            bool ok = TwinUiSourceSwitch.SetReplay(@"Z:\no_such_dir\SYNTHETIC_bad.csv");
            string afterCsv = TwinUiSourceSwitch.CurrentCsv;
            int afterRows = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.RowCount : -1;
            bool afterReplay = TwinUiSourceSwitch.IsReplay;
            string afterRun = TwinUiSourceSwitch.CurrentRunId;

            Check("rollback rejected bad csv", !ok, "ok=" + ok + " err=" + TwinUiSourceSwitch.LastError);
            Check("rollback keeps old source", beforeCsv == afterCsv && beforeRows == afterRows
                && beforeReplay == afterReplay && beforeRun == afterRun,
                "before=" + beforeRun + "/" + beforeRows + " after=" + afterRun + "/" + afterRows);
            Finish();
        }

        static void DumpCase(Case c, TwinUiReplayFeed feed, string sample, float seek)
        {
            var src = feed.Source;
            var snap = feed.Snapshot;
            int row = src.CursorIndex;
            Log("SAMPLE " + c.Id + " " + sample + " cursorRow=" + row + " axis=" + src.TimeAxis
                + " schema=" + src.SchemaLabel + " progress=" + feed.Progress01.ToString("F3")
                + " loop=" + (src.LoopEnabled ? "on" : "off"));
            TsV("SAMPLE", c.Id, sample, "cursorRow", row.ToString());
            TsV("SAMPLE", c.Id, sample, "loop", src.LoopEnabled ? "on" : "off");
            TsV("SAMPLE", c.Id, sample, "seek", seek.ToString("F3"));
            TsV("SAMPLE", c.Id, sample, "clockNs", src.PlaybackClockNs.ToString());
            if (snap == null)
            {
                Fail("snapshot null", c.Id);
                return;
            }

            TsV("SNAP", c.Id, sample, "schema", src.SchemaLabel);
            TsV("SNAP", c.Id, sample, "statusDisplay", snap.Status.Display);
            TsV("SNAP", c.Id, sample, "statusProvided", snap.Status.Provided ? "1" : "0");
            TsV("SNAP", c.Id, sample, "statusNormal", snap.Status.IsNormal ? "1" : "0");
            TsV("SNAP", c.Id, sample, "quality", snap.Link.Quality);
            TsV("SNAP", c.Id, sample, "caps", TwinUiCapability.ExtendedBits(snap.Capabilities));
            TsV("SNAP", c.Id, sample, "sampleHz", snap.Link.SampleHz.ToString("F3"));
            TsV("SNAP", c.Id, sample, "identityUnique", snap.IdentityUnique ? "1" : "0");
            TsV("SNAP", c.Id, sample, "identityNote", snap.IdentityNote);
            TsV("SNAP", c.Id, sample, "notes", snap.Notes);
            TsV("SNAP", c.Id, sample, "device", snap.Device.Present ? snap.Device.ToCompact() : "未提供");
            TsV("SNAP", c.Id, sample, "deviceSemantics", snap.Device.Semantics);
            TsV("SNAP", c.Id, sample, "validity", snap.Validity.ToText());
            TsV("SNAP", c.Id, sample, "clockDomains", snap.Clocks.ToText());
            TsV("SNAP", c.Id, sample, "timeAxis", snap.Clocks.TimeAxis);
            TsV("SNAP", c.Id, sample, "tcpConvention",
                snap.RawTcp.Frame + "/" + snap.RawTcp.EulerConvention);
            TsV("SNAP", c.Id, sample, "jointsHasTarget", snap.Joints.HasTarget ? "1" : "0");

            Array("j_deg", c.Id, sample, snap.Joints.PositionDeg);
            Array("j_vel_deg_s", c.Id, sample, snap.Joints.VelocityDegPerSec);
            Array("joint_torque_nm", c.Id, sample, snap.Joints.TorqueNm);
            Array("tcp_mm", c.Id, sample, snap.RawTcp.PositionMm);
            Array("tcp_abc_deg", c.Id, sample, snap.RawTcp.AbcDeg);
            Array("force_n", c.Id, sample, snap.Force.HasForce ? snap.Force.Force : null);
            Array("moment_nm", c.Id, sample, snap.Force.HasMoment ? snap.Force.Moment : null);
            Array("target_deg", c.Id, sample, snap.Joints.TargetDeg);
            Array("error_deg", c.Id, sample, snap.Joints.ErrorDeg);

            // 真实 UI 文本（TMP_Text.text），逐卡逐节点
            DumpTexts(c.Id, sample, "joints");
            DumpTexts(c.Id, sample, "tcp");
            DumpTexts(c.Id, sample, "wrench");
            DumpTexts(c.Id, sample, "torque");
            DumpTexts(c.Id, sample, "sync");
        }

        static void Array(string name, string caseId, string sample, float[] values)
        {
            if (values == null || values.Length == 0)
            {
                TsV("ARR", caseId, sample, name, "");
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            TsV("ARR", caseId, sample, name, sb.ToString());
        }

        static void DumpTexts(string caseId, string sample, string cardId)
        {
            if (_surface == null || _surface.Page == null)
            {
                return;
            }

            var module = _surface.Find(cardId);
            if (module == null || module.View == null)
            {
                return;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    continue;
                }

                TsV("TXT", caseId, sample, cardId + "|" + NodePath(t.transform) + "|" + i,
                    (t.text ?? string.Empty).Replace("\n", " "));
            }
        }

        static string NodePath(Transform t)
        {
            var parts = new List<string>(4);
            Transform cur = t;
            for (int depth = 0; depth < 4 && cur != null; depth++)
            {
                parts.Insert(0, cur.name);
                cur = cur.parent;
            }

            return string.Join("/", parts.ToArray());
        }

        static int _tries;
        static bool _reseek;

        static void Retry(string what)
        {
            _tries++;
            if (_tries > 60)
            {
                Fail("wait timeout", what);
                Finish();
                return;
            }

            Log("waiting (" + what + ") try=" + _tries);
            _next = (float)EditorApplication.timeSinceStartup + 0.5f;
        }

        static void Next(double delay)
        {
            _next = (float)(EditorApplication.timeSinceStartup + delay);
        }

        static void Finish()
        {
            if (_tsv != null)
            {
                Directory.CreateDirectory(Root + "/logs");
                _tsvPath = Root + "/logs/UI62_" + _stamp + ".tsv";
                _txtPath = Root + "/logs/UI62_" + _stamp + ".txt";
                File.WriteAllText(_tsvPath, _tsv.ToString());
                if (_log != null)
                {
                    _log.AppendLine("TSV=" + _tsvPath);
                    _log.AppendLine("RESULT=" + (_fails == 0 ? "PASS" : "FAIL") + " failures=" + _fails);
                    File.WriteAllText(_txtPath, _log.ToString());
                }
            }

            SessionState.SetBool(Armed, false);
            TwinUiViewFreeze.Reset();
            EditorApplication.isPlaying = false;
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                _fails++;
            }

            _log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        static void Fail(string name, string detail)
        {
            Check(name, false, detail);
        }

        static void Log(string message)
        {
            if (_log != null)
            {
                _log.AppendLine("INFO " + message);
            }
        }

        static void TsV(string kind, string caseId, string sample, string key, string value)
        {
            if (_tsv == null)
            {
                return;
            }

            _tsv.Append(kind).Append('|').Append(caseId).Append('|').Append(sample)
                .Append('|').Append(key).Append('|').Append(value ?? string.Empty).Append('\n');
        }
    }
}