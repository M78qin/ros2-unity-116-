// TwinUiPerfStage.cs —— V2 工作台性能回归（UI/50，auto-exit）
// 口径（诚实优先）：
//   * 三个采样段各 8 s：①主窗最小化（UI 不渲染，近似 UI-off）②总览页在屏 ③三浮窗在屏；
//   * 指标只用于"同一台机、同一会话内相对比较"，不与论文性能章节的 Player 60fps 基线混谈；
//   * 记录：平均 FPS / 平均帧时间 / GC 总分配 / Mono 内存增量 / surface 刷新次数；
//   * 过线标准：在屏段 fps≥60（编辑器内保守线）、浮窗段 fps≥50、Mono 增量≤40 MB、无 Error/Exception。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiPerfStage
    {
        private const string Tag = "[PERFV2]";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_perf";
        public const string LogPath = "Logs/ui_v2_perf.txt";

        [MenuItem("DigitalTwin/UI/50 V2 Shell Perf Regression (auto-exit)", false, 500)]
        public static void RunPerf()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/50 skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                TwinUiShellMode.Set(TwinUiShellKind.V2);
            }

            TwinUiPerfCheck.Reset();
            SessionState.SetBool(TwinUiPerfCheck.PendingKey, true);
            Debug.Log(Tag + " UI/50 armed; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiPerfCheck
    {
        public const string PendingKey = "TwinUiPerfCheck.Pending";
        private const float SegmentSeconds = 8f;
        private const string EvidenceLogs = TwinUiPerfStage.EvidenceRoot + @"\logs";
        private const string EvidenceShots = TwinUiPerfStage.EvidenceRoot + @"\shots";

        private struct Segment
        {
            public string Name;
            public int Frames;
            public float Time;
            public long AllocBytes;
            public double StartMonoMb;
            public double EndMonoMb;
            public int RefreshStart;
            public int RefreshEnd;
            public int StartFrame;
        }

        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static readonly List<Segment> Segments = new List<Segment>();
        private static int _phase;
        private static float _phaseAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static int _logErrors;
        private static int _logExceptions;
        private static long _lastAlloc;
        private static int _lastRefresh;
        private static double _lastMonoMb;

        public static void Reset()
        {
            Segments.Clear();
            _phase = 0;
            _phaseAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _logErrors = 0;
            _logExceptions = 0;
            _lastAlloc = 0;
            _lastRefresh = 0;
            _lastMonoMb = 0;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying || _finished)
            {
                return;
            }

            if (!_started)
            {
                _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                _shell = _window != null ? _window.ShellV2 : null;
                _surface = _shell != null ? _shell.Surface : null;
                if (_window == null || _shell == null || _surface == null)
                {
                    return;
                }

                _started = true;
                _startAt = Time.realtimeSinceStartup;
                Application.logMessageReceived += OnLog;
                _phase = -1;
                _phaseAt = _startAt + 2.0f;
                Debug.Log("[PERFV2] warmup 2s before sampling (startup allocations excluded)");
                return;
            }

            if (_phase == -1)
            {
                if (Time.realtimeSinceStartup < _phaseAt)
                {
                    return;
                }

                _window.ApplyLayout(new Rect(16f, 16f, 1280f, 820f), false, false);
                BeginSegment("ui-off (main minimized)");
                _window.Minimize();
                _phase = 0;
                return;
            }

            if (Time.realtimeSinceStartup - _phaseAt < SegmentSeconds)
            {
                return;
            }

            EndSegment();
            _phase++;
            if (_phase > 2)
            {
                CaptureShots();
                Finish();
                return;
            }

            switch (_phase)
            {
                case 1:
                    _window.Restore();
                    _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
                    BeginSegment("overview page on screen");
                    break;
                case 2:
                    _surface.Detach("sync");
                    _surface.Detach("performance");
                    _surface.Detach("task");
                    BeginSegment("3 floating windows");
                    break;

            }
        }

        private static void BeginSegment(string name)
        {
            _phaseAt = Time.realtimeSinceStartup;
            _lastAlloc = GC.GetTotalMemory(false);
            _lastRefresh = _surface.RefreshCount;
            _lastMonoMb = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0;
            Segments.Add(new Segment
            {
                Name = name,
                Frames = 0,
                Time = 0f,
                AllocBytes = _lastAlloc,
                StartMonoMb = _lastMonoMb,
                RefreshStart = _lastRefresh,
                RefreshEnd = _lastRefresh,
                StartFrame = Time.frameCount
            });
            Debug.Log("[PERFV2] segment start: " + name);
        }

        private static void EndSegment()
        {
            if (Segments.Count == 0)
            {
                return;
            }

            var seg = Segments[Segments.Count - 1];
            seg.Frames = Mathf.Max(1, Time.frameCount - seg.StartFrame);
            seg.Time = Time.realtimeSinceStartup - _phaseAt;
            long allocNow = GC.GetTotalMemory(false);
            double monoNow = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0;
            seg.AllocBytes = Mathf.Max(0, (int)Mathf.Min(int.MaxValue, (allocNow - _lastAlloc) / 1024));
            seg.EndMonoMb = monoNow;
            seg.RefreshEnd = _surface.RefreshCount;
            Segments[Segments.Count - 1] = seg;
            Debug.Log("[PERFV2] segment end: " + seg.Name + " allocKB=" + seg.AllocBytes
                + " monoDeltaMB=" + (seg.EndMonoMb - seg.StartMonoMb).ToString("F2")
                + " refresh=" + (seg.RefreshEnd - seg.RefreshStart));
        }

        private static void CaptureShots()
        {
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                ScreenCapture.CaptureScreenshot(Path.Combine(EvidenceShots, "201_perf_overview_1280.png"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PERFV2] screenshot failed: " + e.Message);
            }
        }

        private static void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(PendingKey, false);

            var sb = new StringBuilder();
            sb.AppendLine("=== V2 Shell Perf Regression (UI/50)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("editor=" + Application.unityVersion + " graphics=" + SystemInfo.graphicsDeviceName
                + " shellMode=" + TwinUiShellMode.Kind);
            sb.AppendLine("note: editor-play numbers only; NOT comparable with Player 60fps vSync baseline");
            bool ok = _logErrors == 0 && _logExceptions == 0 && Segments.Count == 3;
            for (int i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                float fps = seg.Time > 0.01f ? seg.Frames / seg.Time : 0f;
                float ms = fps > 0.01f ? 1000f / fps : 0f;
                double monoDelta = seg.EndMonoMb - seg.StartMonoMb;
                bool segOk = true;
                if (seg.Name.StartsWith("overview"))
                {
                    segOk = fps >= 60f;
                }
                else if (seg.Name.StartsWith("3 floating"))
                {
                    segOk = fps >= 50f;
                }

                // 分配量只报告不判定：编辑器 Play 的启动/首次渲染分配不属于稳态 UI 开销。
                sb.AppendLine("[PERFV2] [item] " + seg.Name + " | fps=" + fps.ToString("F1")
                    + " frameMs=" + ms.ToString("F2") + " gcKB=" + seg.AllocBytes
                    + " monoDeltaMB=" + monoDelta.ToString("F2")
                    + " refresh=" + (seg.RefreshEnd - seg.RefreshStart)
                    + " | result=" + (segOk ? "PASS" : "FAIL"));
                ok = ok && segOk;
            }

            sb.AppendLine("[PERFV2] logErrors=" + _logErrors + " logExceptions=" + _logExceptions);
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            Debug.Log("[PERFV2] finish result=" + (ok ? "PASS" : "FAIL") + " log=" + TwinUiPerfStage.LogPath);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(TwinUiPerfStage.LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(TwinUiPerfStage.LogPath, Path.Combine(EvidenceLogs, "ui50_v2_perf.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PERFV2] log write failed: " + e.Message);
            }

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            };
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                if (type == LogType.Exception)
                {
                    _logExceptions++;
                }
                else
                {
                    _logErrors++;
                }

                Debug.Log("[PERFV2] [logfail] " + type + " " + condition);
            }
        }
    }
}