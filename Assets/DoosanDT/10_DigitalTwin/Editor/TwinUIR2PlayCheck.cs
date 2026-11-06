// Play 模式自检（自动进入、自动退出）：切页 / 工业台三子页 / UI 刷新频率 30→60 实测 / 事件与快照落盘 / 异常计数。
// 菜单：DigitalTwin/UI/15 Play Check R2 (auto-exit)
// 说明：这是"运行期行为"证据；进入 Play 后本工具自身 tick 检测，完成即 isPlaying=false，不长时间占用编辑器。
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUIR2PlayCheck
    {
        private const string Armed = "DigitalTwin.R2PlayCheck.Armed";
        private const string Started = "DigitalTwin.R2PlayCheck.Started";
        private const string PhaseKey = "DigitalTwin.R2PlayCheck.Phase";
        private const string PhaseStartKey = "DigitalTwin.R2PlayCheck.PhaseStart";
        private const string CountKey = "DigitalTwin.R2PlayCheck.Count";
        private const string TimeKey = "DigitalTwin.R2PlayCheck.Time";
        private const string OriginalHzKey = "DigitalTwin.R2PlayCheck.OriginalHz";
        private const string LogPath = "Logs/ui_r2_play.txt";
        private const string RootPath = "30_界面与交互/UI_二期面板";

        // 各阶段的最短等待（秒）：进入 Play 后先让场景跑稳，再各留 2.5 s 采样窗口。
        private const double SettleSeconds = 1.5d;
        private const double MeasureSeconds = 2.5d;

        private static readonly StringBuilder Lines = new StringBuilder();
        private static readonly List<string> Errors = new List<string>();
        private static readonly List<string> HzFailures = new List<string>();
        private static bool _subscribed;
        private static int _phase;

        static TwinUIR2PlayCheck()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("DigitalTwin/UI/15 Play Check R2 (auto-exit)", false, 15)]
        public static void StartCheck()
        {
            SessionState.SetBool(Armed, true);
            SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetFloat(PhaseStartKey, -1f);
            SessionState.SetFloat(OriginalHzKey, -1f);
            Lines.Clear();
            Lines.AppendLine("R2 + R3 play check  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Errors.Clear();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, Lines.ToString());
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (!_subscribed)
            {
                Application.logMessageReceived += OnLog;
                _subscribed = true;
            }

            _phase = SessionState.GetInt(PhaseKey, 0);
            float start = SessionState.GetFloat(PhaseStartKey, -1f);
            double now = EditorApplication.timeSinceStartup;
            if (start < 0f)
            {
                // 刚进入 Play：以此刻为阶段起点，避免用菜单点击时间导致三阶段连发。
                start = (float)now;
                SessionState.SetFloat(PhaseStartKey, start);
            }

            double elapsed = now - start;
            try
            {
                switch (_phase)
                {
                    case 0:
                        if (elapsed < SettleSeconds)
                        {
                            return;
                        }

                        RunInitial();
                        SessionState.SetInt(PhaseKey, 1);
                        SessionState.SetFloat(PhaseStartKey, (float)now);
                        break;
                    case 1:
                        if (elapsed < MeasureSeconds)
                        {
                            return;
                        }

                        MeasureAndSwitch(30f, 60f);
                        SessionState.SetInt(PhaseKey, 2);
                        SessionState.SetFloat(PhaseStartKey, (float)now);
                        break;
                    default:
                        if (elapsed < MeasureSeconds)
                        {
                            return;
                        }

                        MeasureAndFinish(60f);
                        SessionState.SetBool(Armed, false);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Lines.AppendLine("EXCEPTION in check phase " + _phase + ": " + e);
                SessionState.SetBool(Armed, false);
                Finish("FAIL - check tool exception");
            }
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                Errors.Add(type + ": " + message);
            }
        }

        private static void RunInitial()
        {
            var root = GameObject.Find(RootPath);
            Lines.AppendLine("root=" + (root != null));
            if (root == null)
            {
                SessionState.SetBool(Armed, false);
                Finish("FAIL - root missing");
                return;
            }

            var pageHost = root.GetComponent<TwinUIPageHost>();
            var binder = root.GetComponent<TwinUIStatusBinder>();
            var ops = root.transform.Find("TwinOpsRoot");
            var opsView = ops == null ? null : ops.GetComponent<TwinUITwinOpsView>();
            var industrial = root.transform.Find(TwinUIR3Builder.IndustrialName);
            var host = industrial == null ? null : industrial.GetComponent<TwinUiIndustrialHost>();
            Lines.AppendLine("pageHost=" + (pageHost != null) + " binder=" + (binder != null)
                + " opsView=" + (opsView != null) + " industrialHost=" + (host != null));

            if (pageHost == null || host == null)
            {
                SessionState.SetBool(Armed, false);
                Finish("FAIL - pageHost/industrialHost missing (run UI/1 + UI/2)");
                return;
            }

            pageHost.Show(0);
            Lines.AppendLine("Show(0) mission=" + Active(root.transform, "MissionRoot")
                + " ops=" + Active(root.transform, "TwinOpsRoot") + " industrial=" + SelfActive(industrial));
            pageHost.Show(1);
            Lines.AppendLine("Show(1) mission=" + Active(root.transform, "MissionRoot")
                + " ops=" + Active(root.transform, "TwinOpsRoot") + " industrial=" + SelfActive(industrial));

            if (opsView != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    opsView.SetSub(i);
                }

                opsView.SetSub(0);
            }

            if (binder != null)
            {
                binder.Sample();
                var s = binder.Current;
                Lines.AppendLine("binder snapshot hasFrame=" + s.HasFrame + " connected=" + s.Connected
                    + " source=" + s.SourceKind + " alarms=" + s.AlarmCount + " fps=" + s.Fps.ToString("F1")
                    + " control=" + s.ControlMode);
            }

            pageHost.Show(2);
            Lines.AppendLine("Show(2) industrial=" + Active(root.transform, TwinUIR3Builder.IndustrialName)
                + " subPages=" + Active(industrial, "PageData") + "," + Active(industrial, "PageEvents") + ","
                + Active(industrial, "PageSettings"));

            for (int i = 0; i < 3; i++)
            {
                host.SetSubPage(i);
                Lines.AppendLine("Industrial sub(" + i + ") -> " + Active(industrial, "PageData") + ","
                    + Active(industrial, "PageEvents") + "," + Active(industrial, "PageSettings"));
            }

            host.SetSubPage(0);
            Lines.AppendLine("industrial configuredUiHz=" + host.ConfiguredUiHz
                + " refreshCount0=" + host.RefreshCount + " observedHz=" + host.ObservedHz.ToString("F1"));

            // 先切到 30 Hz 作为第一段实测目标（settings 默认 10，直接测会测错档）
            SessionState.SetFloat(OriginalHzKey, (float)host.ConfiguredUiHz);
            bool to30 = host.SetUiRefreshHz(30f);
            Lines.AppendLine("uiHz baseline -> 30 accepted=" + to30 + " configured=" + host.ConfiguredUiHz);

            // 记录基准，进入 Phase 1 后按 30 Hz 实测
            SessionState.SetInt(CountKey, host.RefreshCount);
            SessionState.SetFloat(TimeKey, (float)EditorApplication.timeSinceStartup);
            Lines.AppendLine("phase0 done at t=" + EditorApplication.timeSinceStartup.ToString("F2"));
        }

        private static void MeasureAndSwitch(float expected, float next)
        {
            var host = Object.FindObjectOfType<TwinUiIndustrialHost>();
            if (host == null)
            {
                Lines.AppendLine("MeasureAndSwitch: host missing");
                return;
            }

            int count = SessionState.GetInt(CountKey, 0);
            float t0 = SessionState.GetFloat(TimeKey, 0f);
            double now = EditorApplication.timeSinceStartup;
            double span = now - t0;
            ReportHz("uiHz-30", expected, host.RefreshCount - count, span, host.ObservedHz);

            bool changed = host.SetUiRefreshHz(next);
            Lines.AppendLine("SetUiRefreshHz(" + next.ToString("F0") + ") accepted=" + changed
                + " configured=" + host.ConfiguredUiHz);

            SessionState.SetInt(CountKey, host.RefreshCount);
            SessionState.SetFloat(TimeKey, (float)EditorApplication.timeSinceStartup);
        }

        private static void ReportHz(string tag, double expected, int refreshes, double span, double hostHz)
        {
            double observed = span > 0.2d ? refreshes / span : 0d;
            double ratio = expected > 0d ? observed / expected : 0d;
            bool ok = span >= MeasureSeconds * 0.8d && ratio >= 0.70d && ratio <= 1.35d;
            Lines.AppendLine(tag + " expected=" + expected.ToString("F0") + "Hz observed=" + observed.ToString("F2")
                + "Hz ratio=" + ratio.ToString("F2") + " refreshes=" + refreshes
                + " spanS=" + span.ToString("F2") + " hostObserved=" + hostHz.ToString("F1")
                + " within_tol=" + ok);
            if (!ok)
            {
                HzFailures.Add(tag + ":observed=" + observed.ToString("F2") + " expected=" + expected.ToString("F0")
                    + " span=" + span.ToString("F2"));
            }
        }

        private static void MeasureAndFinish(float expected)
        {
            var host = Object.FindObjectOfType<TwinUiIndustrialHost>();
            var root = GameObject.Find(RootPath);
            if (host != null)
            {
                int count = SessionState.GetInt(CountKey, 0);
                float t0 = SessionState.GetFloat(TimeKey, 0f);
                double now = EditorApplication.timeSinceStartup;
                double span = now - t0;
                ReportHz("uiHz-60", expected, host.RefreshCount - count, span, host.ObservedHz);

                string eventPath = host.Events != null ? host.Events.ResolvePath() : null;
                var events = eventPath != null && File.Exists(eventPath) ? File.ReadAllLines(eventPath) : new string[0];
                int hzLines = 0;
                foreach (string line in events)
                {
                    if (line.Contains("uiHz -> "))
                    {
                        hzLines++;
                    }
                }

                Lines.AppendLine("eventsFile=" + (eventPath != null && File.Exists(eventPath))
                    + " lines=" + events.Length + " uiHzEventLines=" + hzLines
                    + " writeFailures=" + (host.Events != null ? (int)host.Events.WriteFailures : -1));

                string snapPath = host.Export != null ? host.Export.ResolvePath() : null;
                string head = string.Empty;
                if (snapPath != null && File.Exists(snapPath))
                {
                    var bytes = File.ReadAllText(snapPath);
                    head = bytes.Length > 0 ? bytes.Substring(0, Mathf.Min(120, bytes.Length)).Replace("\n", " ") : string.Empty;
                }

                Lines.AppendLine("snapshotFile=" + (snapPath != null && File.Exists(snapPath))
                    + " writeCount=" + (host.Export != null ? (int)host.Export.WriteCount : -1)
                    + " bytes=" + (host.Export != null ? host.Export.LastBytes : -1) + " head=" + head);

                if (root != null)
                {
                    var pageHost = root.GetComponent<TwinUIPageHost>();
                    if (pageHost != null)
                    {
                        pageHost.Show(0);
                    }
                }
            }
            else
            {
                Lines.AppendLine("MeasureAndFinish: host missing");
            }

            Lines.AppendLine("exceptions=" + Errors.Count);
            foreach (string e in Errors)
            {
                Lines.AppendLine("  " + e);
            }

            if (HzFailures.Count > 0)
            {
                Lines.AppendLine("hzCheckFailures=" + HzFailures.Count);
                foreach (string h in HzFailures)
                {
                    Lines.AppendLine("  " + h);
                }
            }
            else
            {
                Lines.AppendLine("hzCheckFailures=0 (both 30 Hz and 60 Hz within +/-30% of target)");
            }

            // 还原原始 uiHz，避免 Play 期间改动的 settings 值被误持久化
            if (host != null)
            {
                float original = SessionState.GetFloat(OriginalHzKey, -1f);
                if (original > 0f)
                {
                    host.SetUiRefreshHz(original);
                    Lines.AppendLine("uiHz restored to " + original.ToString("F0"));
                }
            }

            SessionState.SetBool(Armed, false);
            Finish(Errors.Count == 0 && HzFailures.Count == 0 ? "PASS" : "FAIL");
        }

        private static void Finish(string verdict)
        {
            Lines.AppendLine("RESULT: " + verdict);
            Directory.CreateDirectory("Logs");
            File.AppendAllText(LogPath, Lines.ToString());
            try
            {
                string dir = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-04_ui_industrial\logs";
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ui_play_check.txt"), Lines.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UI play] evidence copy failed: " + e.Message);
            }

            Debug.Log("[UI play] " + verdict);
            Application.logMessageReceived -= OnLog;
            _subscribed = false;
            EditorApplication.isPlaying = false;
        }

        private static string SelfActive(Transform t)
        {
            return t == null ? "missing" : (t.gameObject.activeSelf ? "1" : "0");
        }

        private static string Active(Transform root, string name)
        {
            if (root == null)
            {
                return "no-root";
            }

            var t = root.Find(name);
            return t == null ? "missing" : (t.gameObject.activeSelf ? "1" : "0");
        }
    }
}