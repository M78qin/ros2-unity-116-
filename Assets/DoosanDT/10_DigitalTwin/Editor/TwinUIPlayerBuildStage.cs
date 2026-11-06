// TwinUIPlayerBuildStage.cs —— Player Build 性能实测（P6-2 / task-15）
// UI/42 Build Player (perf)：Windows Standalone（Development Build 关闭）→ Builds/p6_perf/，写构建日志（不改旧构建）。
// UI/43 Capture Player 60s：启动刚构建的 exe（1920×1080 窗口）采样 60 s，读取 PerformanceProfiler CSV，
//   计算 fps 均值/p50/p95、帧时间、内存、渲染器/三角面，原始 CSV 复制到证据目录。
// UI/45 Editor Perf 60s：Editor Play 下用同一 PerformanceProfiler 快照采样 60 s，产出与 Build 同口径的对比数据。
// 纪律：构建/运行失败一律记 BLOCKED + 日志，不写任何估算数字。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIPlayerBuildStage
    {
        private const string Tag = "[PERF]";
        private const string BuildDir = @"Builds\p6_perf";
        private const string ExeName = "TwinUI_P6_Perf.exe";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine";
        private const string PerfDir = EvidenceRoot + @"\perf";

        public static string ExePath => Path.Combine(Directory.GetCurrentDirectory(), BuildDir, ExeName);

        [MenuItem("DigitalTwin/UI/42 Build Player (perf)", false, 420)]
        public static void BuildPlayer()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning(Tag + " UI/42 skipped: play mode");
                return;
            }

            // 构建场景顺序：Doosan-DT 放第一个（Player 启动加载 index 0），避免启动到空的 SampleScene。
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .OrderBy(p => p.IndexOf("Doosan-DT", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ToArray();
            if (scenes.Length == 0)
            {
                WriteLog("UI/42 BLOCKED: no enabled scenes in Build Settings");
                Debug.LogError(Tag + " UI/42 BLOCKED: no enabled scenes");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
            Directory.CreateDirectory(PerfDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            var sb = new StringBuilder();
            sb.AppendLine("UI/42 Build Player (perf)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("exe=" + ExePath);
            sb.AppendLine("scenes=" + string.Join(",", scenes));
            sb.AppendLine("options=None (Development Build off; URP default quality)");
            sb.AppendLine("company=" + PlayerSettings.companyName + " product=" + PlayerSettings.productName);
            sb.AppendLine("persistentDataPath(editor)=" + Application.persistentDataPath);
            var sw = Stopwatch.StartNew();
            BuildReport report = null;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                sb.AppendLine("BLOCKED exception=" + e.GetType().Name + ": " + e.Message);
                WriteLog(sb.ToString());
                Debug.LogError(Tag + " UI/42 BLOCKED exception=" + e.Message);
                return;
            }
            finally
            {
                sw.Stop();
            }

            if (report == null)
            {
                sb.AppendLine("BLOCKED report=null");
                WriteLog(sb.ToString());
                Debug.LogError(Tag + " UI/42 BLOCKED report=null");
                return;
            }

            var summary = report.summary;
            sb.AppendLine("result=" + summary.result);
            sb.AppendLine("totalSize=" + summary.totalSize + " B");
            sb.AppendLine("totalTime=" + summary.totalTime);
            sb.AppendLine("elapsed=" + sw.Elapsed.TotalSeconds.ToString("F1") + " s");
            sb.AppendLine("errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings);
            bool ok = summary.result == BuildResult.Succeeded && File.Exists(ExePath);
            if (ok)
            {
                var fi = new FileInfo(ExePath);
                sb.AppendLine("exeExists=True size=" + fi.Length + " B mtime=" + fi.LastWriteTime);
            }
            else
            {
                sb.AppendLine("BLOCKED build result=" + summary.result + " exeExists=" + File.Exists(ExePath));
            }

            WriteLog(sb.ToString());
            if (ok)
            {
                Debug.Log(Tag + " build result=PASS exe=" + ExePath + " size=" + summary.totalSize
                    + " seconds=" + sw.Elapsed.TotalSeconds.ToString("F1"));
            }
            else
            {
                Debug.LogError(Tag + " build result=BLOCKED (" + summary.result + ")");
            }
        }

        private static void WriteLog(string text)
        {
            try
            {
                Directory.CreateDirectory("Logs");
                Directory.CreateDirectory(PerfDir);
                File.WriteAllText("Logs/ui42_build.txt", text, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(PerfDir, "build_log.txt"), text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " write log failed: " + e.Message);
            }
        }
    }

    /// <summary>UI/45：Editor Play 60 s 同口径采样（PerformanceProfiler.TryGetSnapshot）。</summary>
    public static class TwinUIEditorPerfStage
    {
        public const string PendingKey = "TwinUIEditorPerf.Pending";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine\perf";
        private const string LogPath = "Logs/ui45_editor_perf.txt";
        private const float DurationSeconds = 60f;
        private static readonly List<float> Fps = new List<float>();
        private static readonly List<float> FrameMs = new List<float>();
        private static readonly List<float> MemEngine = new List<float>();
        private static readonly List<float> MemManaged = new List<float>();
        private static readonly List<float> MemProcess = new List<float>();
        private static readonly List<float> Renderers = new List<float>();
        private static readonly List<float> Triangles = new List<float>();
        private static float _startAt;
        private static float _warmupUntil;
        private static float _nextSample;
        private static bool _armed;
        private static int _originalVSync = -1;

        [MenuItem("DigitalTwin/UI/45 Editor Perf 60s (auto-exit)", false, 450)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[PERF] UI/45 skipped: play mode");
                return;
            }

            SessionState.SetBool(PendingKey, true);
            Debug.Log("[PERF] UI/45 armed, entering play mode (60s editor sampling)");
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (!_armed)
            {
                var perf = UnityEngine.Object.FindObjectOfType<PerformanceProfiler>();
                if (perf == null || !perf.TryGetSnapshot(out var snap) || !snap.valid)
                {
                    _startAt = Time.realtimeSinceStartup;
                    _armed = Time.realtimeSinceStartup > 0f;
                    return;
                }

                _armed = true;
                _originalVSync = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 1; // 与 Standalone 默认（vSync=1）对齐，避免 Editor 200fps vs Build 60fps 的伪差异
                _startAt = Time.realtimeSinceStartup;
                _warmupUntil = _startAt + 3f;
                _nextSample = _startAt;
                Debug.Log("[PERF] UI/45 sampling start (profiler=" + perf.gameObject.name
                    + " vsync=" + QualitySettings.vSyncCount + ")");
            }

            float now = Time.realtimeSinceStartup;
            if (now - _startAt < DurationSeconds)
            {
                if (now < _nextSample)
                {
                    return;
                }

                _nextSample = now + 0.5f;
                if (now < _warmupUntil)
                {
                    return;
                }

                var profiler = UnityEngine.Object.FindObjectOfType<PerformanceProfiler>();
                if (profiler != null && profiler.TryGetSnapshot(out var s) && s.valid)
                {
                    if (s.fps < 1f)
                    {
                        return;
                    }

                    Fps.Add(s.fps);
                    FrameMs.Add(1000f / Mathf.Max(1f, s.fps)); // 口径：帧时间由 profiler 平滑 FPS 推导，避免瞬时 dt 抖动
                    MemEngine.Add(s.engineAllocatedMB);
                    MemManaged.Add(s.managedMB);
                    MemProcess.Add(s.processWorkingSetMB);
                    Renderers.Add(s.activeRenderers);
                    Triangles.Add((float)s.activeTriangles);
                }

                return;
            }

            Finish();
        }

        private static void Finish()
        {
            SessionState.SetBool(PendingKey, false);
            _armed = false;
            var sb = new StringBuilder();
            sb.AppendLine("UI/45 Editor Play 60s perf (PerformanceProfiler snapshot @0.5s)");
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("durationSeconds=" + DurationSeconds + " samples=" + Fps.Count
                + " vSync=1 (与 Standalone 默认对齐)");
            QualitySettings.vSyncCount = _originalVSync >= 0 ? _originalVSync : 0;
            AppendStats(sb);
            try
            {
                Directory.CreateDirectory("Logs");
                Directory.CreateDirectory(EvidenceRoot);
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(EvidenceRoot, "editor_60s_stats.txt"), sb.ToString(),
                    new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PERF] UI/45 write failed: " + e.Message);
            }

            Debug.Log("[PERF] UI/45 editor60s(vsync=1) samples=" + Fps.Count + " " + StatsLine());
            EditorApplication.isPlaying = false;
        }

        private static void AppendStats(StringBuilder sb)
        {
            sb.AppendLine("fpsMean=" + Mean(Fps).ToString("F2") + " fpsP50=" + Percentile(Fps, 50f).ToString("F2")
                + " fpsP95=" + Percentile(Fps, 95f).ToString("F2") + " fpsMin=" + (Fps.Count > 0 ? Fps.Min() : 0f).ToString("F2"));
            sb.AppendLine("frameMsMean=" + Mean(FrameMs).ToString("F2") + " (derived 1000/fps; profiler 瞬时 dt 不用于结论)");
            sb.AppendLine("engineMBMean=" + Mean(MemEngine).ToString("F2") + " managedMBMean="
                + Mean(MemManaged).ToString("F2") + " processMBMean=" + Mean(MemProcess).ToString("F2"));
            sb.AppendLine("renderersMean=" + Mean(Renderers).ToString("F1") + " trianglesMean="
                + Mean(Triangles).ToString("F0"));
        }

        private static string StatsLine()
        {
            return "fpsMean=" + Mean(Fps).ToString("F2") + " p50=" + Percentile(Fps, 50f).ToString("F2")
                + " p95=" + Percentile(Fps, 95f).ToString("F2") + " frameMsMean=" + Mean(FrameMs).ToString("F2");
        }

        internal static float Mean(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            return (float)(sum / values.Count);
        }

        internal static float Percentile(List<float> values, float p)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            var sorted = new List<float>(values);
            sorted.Sort();
            int idx = Mathf.Clamp(Mathf.CeilToInt(p / 100f * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[idx];
        }
    }

/// <summary>UI/43：启动刚构建的 Player，采 60 s（读 PerformanceProfiler CSV），失败即 BLOCKED + 日志。</summary>
public static class TwinUIPlayerPerfStage
{
    private const string Tag = "[PERF]";
    private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine\perf";
    private const string LogPath = "Logs/ui43_player_perf.txt";
    private const float RunSeconds = 70f;
    private static Process _process;
    private static float _startAt;
    private static bool _running;
    private static int _phase;

    [MenuItem("DigitalTwin/UI/43 Capture Player Perf 60s", false, 430)]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning(Tag + " UI/43 skipped: play mode");
            return;
        }

        string exe = TwinUIPlayerBuildStage.ExePath;
        if (!File.Exists(exe))
        {
            WriteBlocked("exe missing: " + exe);
            return;
        }

        Directory.CreateDirectory(EvidenceRoot);
        Directory.CreateDirectory(Path.Combine(EvidenceRoot, "raw"));
        string playerLog = Path.Combine(EvidenceRoot, "player_output.log");
        try
        {
            var args = "-screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile \""
                + playerLog + "\"";
            _process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        }
        catch (Exception e)
        {
            WriteBlocked("launch exception: " + e.Message);
            return;
        }

        if (_process == null)
        {
            WriteBlocked("process start returned null");
            return;
        }

        _running = true;
        _phase = 1;
        _startAt = (float)EditorApplication.timeSinceStartup;
        Debug.Log(Tag + " UI/43 player started pid=" + _process.Id + " exe=" + exe
            + " samplingSeconds=" + RunSeconds);
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    private static void Update()
    {
        if (!_running)
        {
            return;
        }

        float now = (float)EditorApplication.timeSinceStartup;
        if (_phase == 1)
        {
            if (now - _startAt < RunSeconds)
            {
                return;
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " kill failed: " + e.Message);
            }

            _phase = 2;
            _startAt = now;
            return;
        }

        if (now - _startAt < 2f)
        {
            return;
        }

        _running = false;
        Finish();
    }

    private static void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("UI/43 Build Player 60s perf（PerformanceProfiler CSV，窗口 1920×1080）");
        sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
            PlayerSettings.companyName, PlayerSettings.productName);
        string csvRoot = Path.Combine(dataDir, "PerformanceLogs");
        sb.AppendLine("exe=" + TwinUIPlayerBuildStage.ExePath);
        sb.AppendLine("playerDataRoot=" + dataDir);
        sb.AppendLine("csvRoot=" + csvRoot);

        List<string> rows = null;
        // 场景里 PerformanceProfiler.customLogPath 可能配置到工程外（本场景为 D:\Unity Projects\数据记录），
        // 因此先查 persistentDataPath，再回退到 player_output.log 里打印的实际 CSV 路径。
        string csvPath = FindNewestCsv(csvRoot);
        if (csvPath == null)
        {
            csvPath = FindCsvFromPlayerLog(Path.Combine(EvidenceRoot, "player_output.log"));
        }

        if (csvPath != null)
        {
            sb.AppendLine("csv=" + csvPath);
            try
            {
                rows = File.ReadAllLines(csvPath).ToList();
                string copy = Path.Combine(EvidenceRoot, "raw", "PerfLog_build.csv");
                File.Copy(csvPath, copy, true);
                sb.AppendLine("csvCopied=" + copy + " bytes=" + new FileInfo(csvPath).Length);
            }
            catch (Exception e)
            {
                sb.AppendLine("BLOCKED csv read failed: " + e.Message);
            }
        }
        else
        {
            sb.AppendLine("BLOCKED no csv found under " + csvRoot);
        }

        if (rows == null || rows.Count < 2)
        {
            sb.AppendLine("BLOCKED insufficient csv rows");
            Write(sb.ToString());
            Debug.LogError(Tag + " UI/43 BLOCKED: no player CSV");
            return;
        }

        try
        {
            var header = SplitCsv(rows[0]);
            int colFps = IndexOf(header, "FPS");
            int colFrame = IndexOf(header, "帧时长(ms)");
            int colEngine = IndexOf(header, "引擎已分配内存(MB)");
            int colManaged = IndexOf(header, "管理堆(MB)");
            int colProcess = IndexOf(header, "进程工作集(MB)");
            int colRenderers = IndexOf(header, "活跃渲染器");
            int colTriangles = IndexOf(header, "活跃三角面");
            var fps = new List<float>();
            var frame = new List<float>();
            var engine = new List<float>();
            var managed = new List<float>();
            var process = new List<float>();
            var renderers = new List<float>();
            var triangles = new List<float>();
            for (int i = 1; i < rows.Count; i++)
            {
                var cols = SplitCsv(rows[i]);
                if (colFps >= 0 && colFps < cols.Count && float.TryParse(cols[colFps], out float f))
                {
                    fps.Add(f);
                }

                if (colFrame >= 0 && colFrame < cols.Count && float.TryParse(cols[colFrame], out float fm))
                {
                    frame.Add(fm);
                }

                if (colEngine >= 0 && colEngine < cols.Count && float.TryParse(cols[colEngine], out float m1))
                {
                    engine.Add(m1);
                }

                if (colManaged >= 0 && colManaged < cols.Count && float.TryParse(cols[colManaged], out float m2))
                {
                    managed.Add(m2);
                }

                if (colProcess >= 0 && colProcess < cols.Count && float.TryParse(cols[colProcess], out float m3))
                {
                    process.Add(m3);
                }

                if (colRenderers >= 0 && colRenderers < cols.Count && float.TryParse(cols[colRenderers], out float r1))
                {
                    renderers.Add(r1);
                }

                if (colTriangles >= 0 && colTriangles < cols.Count && float.TryParse(cols[colTriangles], out float t1))
                {
                    triangles.Add(t1);
                }
            }

            sb.AppendLine("csvRows=" + (rows.Count - 1) + " parsedSamples=" + fps.Count);
            sb.AppendLine("fpsMean=" + TwinUIEditorPerfStage.Mean(fps).ToString("F2")
                + " fpsP50=" + TwinUIEditorPerfStage.Percentile(fps, 50f).ToString("F2")
                + " fpsP95=" + TwinUIEditorPerfStage.Percentile(fps, 95f).ToString("F2")
                + " fpsMin=" + (fps.Count > 0 ? fps.Min() : 0f).ToString("F2"));
            sb.AppendLine("frameMsMean=" + TwinUIEditorPerfStage.Mean(frame).ToString("F2"));
            sb.AppendLine("engineMBMean=" + TwinUIEditorPerfStage.Mean(engine).ToString("F2")
                + " managedMBMean=" + TwinUIEditorPerfStage.Mean(managed).ToString("F2")
                + " processMBMean=" + TwinUIEditorPerfStage.Mean(process).ToString("F2"));
            sb.AppendLine("renderersMean=" + TwinUIEditorPerfStage.Mean(renderers).ToString("F1")
                + " trianglesMean=" + TwinUIEditorPerfStage.Mean(triangles).ToString("F0"));
            Write(sb.ToString());
            Debug.Log(Tag + " UI/43 build60s samples=" + fps.Count + " fpsMean="
                + TwinUIEditorPerfStage.Mean(fps).ToString("F2") + " p50="
                + TwinUIEditorPerfStage.Percentile(fps, 50f).ToString("F2") + " p95="
                + TwinUIEditorPerfStage.Percentile(fps, 95f).ToString("F2"));
        }
        catch (Exception e)
        {
            sb.AppendLine("BLOCKED parse exception: " + e.Message);
            Write(sb.ToString());
            Debug.LogError(Tag + " UI/43 BLOCKED: " + e.Message);
        }
    }

    private static void WriteBlocked(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("UI/43 BLOCKED " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("reason=" + reason);
        Write(sb.ToString());
        Debug.LogError(Tag + " UI/43 BLOCKED: " + reason);
    }

    private static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory("Logs");
            Directory.CreateDirectory(EvidenceRoot);
            File.WriteAllText(LogPath, text, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(EvidenceRoot, "player_60s_stats.txt"), text, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning(Tag + " UI/43 write failed: " + e.Message);
        }
    }

    private static string FindCsvFromPlayerLog(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return null;
            }

            foreach (var line in File.ReadAllLines(logPath))
            {
                int idx = line.IndexOf("CSV 已创建：", StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }

                string path = line.Substring(idx + "CSV 已创建：".Length).Trim();
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static string FindNewestCsv(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.GetFiles(root, "*.csv", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (c == ',' && !quoted)
            {
                result.Add(sb.ToString().Trim());
                sb.Length = 0;
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString().Trim());
        return result;
    }

    private static int IndexOf(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (header[i].IndexOf(name, StringComparison.Ordinal) >= 0)
            {
                return i;
            }
        }

        return -1;
    }
}
}