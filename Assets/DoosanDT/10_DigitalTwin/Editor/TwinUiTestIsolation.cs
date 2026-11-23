// TwinUiTestIsolation.cs -- R2 测试隔离与外壳装配诊断（Editor，2026-11-09）
//
// 背景（Lead 现场告警 18:09）：
//   本项目场景里的 XR Interaction Toolkit 3.4.0 affordance 组件在 Play 下每帧抛
//   NullReferenceException（BaseAsyncAffordanceStateReceiver.HandleTween），
//   Editor.log 以 ~6 MB/min 增长（18:09 时已达 117 MB），会吃掉帧时间与磁盘 I/O，
//   污染 UI/57/56/58 的性能与就绪类测量。**这是既有问题，不是本轮引入，也不改 PackageCache。**
//
// 本文件只做两件事（都只在 Play 进程内临时生效，不碰场景资产、不保存）：
//   1. DisableXrAffordances()：把那批 provider/receiver 组件 enabled=false，并返回清单；
//      调用方（UI/56/57/58/59/60/61）在 Play 启动时调用一次，日志里记"关了哪些、几时关的"。
//   2. UI/62：外壳装配诊断（surface/page/shell 计数与 null 状态），用于 A-5 根因取证。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiTestIsolation
    {
        /// <summary>
        /// 关闭场景里的 XRI affordance provider / receiver（只在本次 Play 进程内），
        /// 用于让性能与就绪类测量摆脱"每帧 NRE + 6 MB/min 日志"的污染。
        /// 返回：关闭数量；names 输出被关闭对象的层级路径（最多 12 条）。
        /// </summary>
        public static int DisableXrAffordances(out string names)
        {
            var closed = new List<string>(16);
            var sb = new StringBuilder();
            var all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var mb = all[i];
                if (mb == null)
                {
                    continue;
                }

                string typeName = mb.GetType().Name;
                string fullName = mb.GetType().FullName ?? typeName;
                bool hit = fullName.IndexOf("AffordanceStateProvider", StringComparison.Ordinal) >= 0
                    || fullName.IndexOf("AsyncAffordanceStateReceiver", StringComparison.Ordinal) >= 0
                    || fullName.IndexOf("AffordanceStateReceiver", StringComparison.Ordinal) >= 0;
                if (!hit)
                {
                    continue;
                }

                if (!mb.enabled)
                {
                    continue;
                }

                mb.enabled = false;
                closed.Add(typeName + "@" + PathOf(mb.transform));
                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                if (closed.Count <= 12)
                {
                    sb.Append(typeName).Append('@').Append(PathOf(mb.transform));
                }
            }

            names = sb.Length == 0 ? "(none)" : sb.ToString();
            return closed.Count;
        }

        /// <summary>Unity 控制台日志文件路径（Application.consoleLogPath）与当前大小（MB）。</summary>
        public static string ConsoleLogInfo()
        {
            string path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return "consoleLogPath=<none>";
            }

            var fi = new FileInfo(path);
            return path + " | " + (fi.Length / 1048576d).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>从启动点到现在的 Editor/Player 日志增长（MB/min），用于证据限定。</summary>
        public static double ConsoleLogMbPerMinute(string pathAtStart, long bytesAtStart, double seconds)
        {
            string path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || seconds < 1d)
            {
                return 0d;
            }

            string full = Path.GetFullPath(path);
            if (!string.IsNullOrEmpty(pathAtStart)
                && !string.Equals(Path.GetFullPath(pathAtStart), full, StringComparison.OrdinalIgnoreCase))
            {
                return 0d;
            }

            long now = new FileInfo(path).Length;
            return (now - bytesAtStart) / 1048576d / (seconds / 60d);
        }

        public static long ConsoleLogBytes()
        {
            string path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return 0L;
            }

            return new FileInfo(path).Length;
        }

        public static string ConsoleLogPath()
        {
            return Application.consoleLogPath ?? string.Empty;
        }

        private static string PathOf(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------ UI/62 外壳装配诊断

        [MenuItem("DigitalTwin/UI/62 Dump Shell Page Wiring (auto-exit)", false, 620)]
        public static void DumpWiring()
        {
            TwinUiShellWiringDumpStage.Start();
        }
    }

    /// <summary>
    /// UI/62：进 Play，等外壳装配落定，dump "surface/page/shell 各几个、谁 null"，
    /// 然后退出 Play。专用于 A-5 根因取证（UI/57 曾出现 configured=true 但 page=null）。
    /// </summary>
    [InitializeOnLoad]
    public static class TwinUiShellWiringDumpStage
    {
        const string Armed = "A.ShellWiringDump";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui62";

        static StringBuilder log;
        static double next;
        static double deadline;
        static int phase;
        static bool xrClosed;
        static string logPathAtStart;
        static long logBytesAtStart;
        static double startedAt;

        static TwinUiShellWiringDumpStage()
        {
            EditorApplication.update += Tick;
        }

        public static void Start()
        {
            Directory.CreateDirectory(Root + "/logs");
            log = new StringBuilder();
            log.AppendLine("# UI/62 外壳装配诊断");
            log.AppendLine("started=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            phase = 0;
            xrClosed = false;
            next = 0d;
            deadline = EditorApplication.timeSinceStartup + 120d;
            startedAt = EditorApplication.timeSinceStartup;
            logPathAtStart = TwinUiTestIsolation.ConsoleLogPath();
            logBytesAtStart = TwinUiTestIsolation.ConsoleLogBytes();
            Debug.Log("[UI62] console log at start: " + TwinUiTestIsolation.ConsoleLogInfo());
            SessionState.SetBool(Armed, true);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || EditorApplication.isCompiling)
            {
                return;
            }

            if (log == null)
            {
                log = new StringBuilder();
                log.AppendLine("# UI/62（resumed after domain reload）");
                phase = 0;
                xrClosed = false;
                next = 0d;
                deadline = EditorApplication.timeSinceStartup + 120d;
                startedAt = EditorApplication.timeSinceStartup;
                logPathAtStart = TwinUiTestIsolation.ConsoleLogPath();
                logBytesAtStart = TwinUiTestIsolation.ConsoleLogBytes();
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now > deadline)
            {
                log.AppendLine("FAIL watchdog | phase=" + phase);
                Finish();
                return;
            }

            if (now < next)
            {
                return;
            }

            if (!xrClosed)
            {
                xrClosed = true;
                string names;
                int closed = TwinUiTestIsolation.DisableXrAffordances(out names);
                log.AppendLine("XRI closed=" + closed + " | " + names);
                log.AppendLine("console log: " + TwinUiTestIsolation.ConsoleLogInfo());
                Flush();
                next = now + 4d;
                return;
            }

            if (phase == 0)
            {
                phase = 1;
                dump();
                Flush();
                next = now + 3d;
                return;
            }

            double mins = (now - startedAt) / 60d;
            double rate = TwinUiTestIsolation.ConsoleLogMbPerMinute(logPathAtStart, logBytesAtStart, now - startedAt);
            log.AppendLine("console log growth = " + rate.ToString("F1", CultureInfo.InvariantCulture)
                + " MB/min over " + (mins * 60d).ToString("F0", CultureInfo.InvariantCulture) + " s"
                + " | now " + TwinUiTestIsolation.ConsoleLogInfo());
            dump();
            log.AppendLine("RESULT=PASS");
            Finish();
        }

        static void dump()
        {
            var surfaces = UnityEngine.Object.FindObjectsOfType<TwinUiSurfaceHost>(true);
            log.AppendLine("surfaces=" + surfaces.Length + " Instance=" + (TwinUiSurfaceHost.Instance != null));
            for (int i = 0; i < surfaces.Length; i++)
            {
                var s = surfaces[i];
                log.AppendLine("  surface[" + i + "] id=" + s.GetInstanceID()
                    + " go=" + (s.gameObject != null ? s.gameObject.name : "<null>")
                    + " active=" + (s.gameObject != null && s.gameObject.activeInHierarchy)
                    + " configured=" + s.IsConfigured
                    + " page=" + (s.Page != null)
                    + " isInstance=" + ReferenceEquals(s, TwinUiSurfaceHost.Instance)
                    + " modules=" + s.ModuleCount);
            }

            var shells = UnityEngine.Object.FindObjectsOfType<TwinUiShellV2>(true);
            log.AppendLine("shells=" + shells.Length + " IsV2=" + TwinUiShellMode.IsV2);
            for (int i = 0; i < shells.Length; i++)
            {
                var sh = shells[i];
                log.AppendLine("  shell[" + i + "] go=" + sh.gameObject.name
                    + " active=" + sh.gameObject.activeInHierarchy
                    + " page=" + (sh.Page != null)
                    + " tabs=" + sh.TabCount
                    + " tab=" + sh.CurrentTab);
            }

            var windows = UnityEngine.Object.FindObjectsOfType<TwinUiWindowHost>(true);
            log.AppendLine("windowHosts=" + windows.Length
                + " content=" + (windows.Length > 0 && windows[0].Content != null));
        }

        static void Flush()
        {
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui62_wiring_dump.txt", log.ToString());
            }
            catch (Exception)
            {
            }
        }

        static void Finish()
        {
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui62_wiring_dump.txt", log.ToString());
            }
            finally
            {
                SessionState.SetBool(Armed, false);
                EditorApplication.isPlaying = false;
            }
        }
    }

    }