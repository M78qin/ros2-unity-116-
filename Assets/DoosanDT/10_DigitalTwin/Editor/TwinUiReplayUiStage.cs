// TwinUiReplayUiStage.cs —— 大论文批次 A/UI 回放：Play 态离线回放 UI 自检（UI/59）
//
// 目的（可复核的证据，不是"看起来像"）：
//   1) 数字孪生页六核心卡片齐全（joints/tcp/torque/wrench/sync/session）；
//   2) 页顶连接状态条可见，且写明"回放"、runId、更新时间；
//   3) joints 曲线：6 通道 + 图例 + 20 s 时间窗；
//   4) "查看冻结"只停显示：冻结期间回放钟继续前进、图表点数与数值冻结；
//   5) 缺数不填 0：samples.csv 没有力矩/六维力 → 卡片显示"—"/"未订阅"；
//   6) 真实窗口（1920×1080 屏）+ 720/1200 宽度截图。
//
// 边界：只读 CSV（既有 ReplayTwinUiDataSource），不连 ROS/DART、不写记录、不下发命令、不改场景。
// 输出目录 = 本批证据目录（ui/logs、ui/shots），失败也写盘。
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
    public static class TwinUiReplayUiStage
    {
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui59";
        const string SHOTS = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\shots";
        const string Armed = "Thesis.Ui59ReplayUi";
        const string FallbackCsv =
            @"D:\Unity Projects\DART_R-data\PaperLogs\20261103_135616_529090_E4_B\samples.csv";
        static readonly string[] CoreCards = { "joints", "tcp", "torque", "wrench", "sync", "session" };

        // R2 测量隔离：XR affordance 组件只关一次（进程内），并在日志里留证。
        static bool xrIsolated;
        static StringBuilder log;
        static int step;
        static int fails;
        static double next;
        static double deadline;
        static int tries;

        static TwinUiWindowHost window;
        static TwinUiShellV2 shell;
        static TwinUiSurfaceHost surface;
        static TwinUIStatusBinder binder;
        static TwinUiReplayFeed feed;
        static string csvPath = string.Empty;

        static int frozenPoints;
        static double frozenProgress;
        static string frozenCell = string.Empty;
        static string frozenStatus = string.Empty;

        static TwinUiReplayUiStage()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("DigitalTwin/UI/59 Replay UI Check (auto-exit)")]
        public static void Run()
        {
            csvPath = ResolveCsvPath();
            log = new StringBuilder();
            step = 0;
            fails = 0;
            tries = 0;
            deadline = EditorApplication.timeSinceStartup + 180d;
            SessionState.SetBool(Armed, true);
            SessionState.SetBool(Armed + ".started", true);
            Directory.CreateDirectory(Root + "/logs");
            Directory.CreateDirectory(SHOTS);
            TwinUiViewFreeze.Reset();
            Log("START csv=" + csvPath);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static string ResolveCsvPath()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:TwinUiSettings");
                if (guids != null && guids.Length > 0)
                {
                    var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (settings != null && !string.IsNullOrEmpty(settings.replayCsvPath))
                    {
                        return settings.replayCsvPath;
                    }
                }
            }
            catch (Exception e)
            {
                Log("settings read failed: " + e.GetType().Name);
            }

            return FallbackCsv;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !Application.isPlaying || EditorApplication.isCompiling)
            {
                return;
            }

            // 已知 Error Pause：启动期 XRI NRE 会暂停 Play；自检自行清掉（同 UI/57、UI/58）。
            // R2 测量隔离（Lead 18:09 现场告警）：场景里的 XRI affordance provider 每帧抛 NRE、
            // Editor.log 约 6 MB/min，污染就绪/性能测量。这里只在本次 Play 进程内禁用它们，
            // 不改场景资产、退出不保存；关闭清单与之后的日志增速都写进本阶段日志。
            if (!xrIsolated)
            {
                xrIsolated = true;
                string xriNames;
                int xriClosed = TwinUiTestIsolation.DisableXrAffordances(out xriNames);
                if (log != null)
                {
                    log.AppendLine("INFO XRI affordance providers disabled=" + xriClosed + " | " + xriNames);
                    log.AppendLine("INFO console log at isolation: " + TwinUiTestIsolation.ConsoleLogInfo());
                }
            }
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (log == null)
            {
                // 进入 Play 会触发 domain reload，静态字段被清空但 SessionState 仍在。
                // 必须在这里重建状态（含 deadline），否则 deadline=0 会让本工具在每帧误判超时。
                if (!SessionState.GetBool(Armed + ".started", false))
                {
                    SessionState.SetBool(Armed, false);
                    return;
                }

                log = new StringBuilder();
                step = 0;
                fails = 0;
                tries = 0;
                deadline = EditorApplication.timeSinceStartup + 180d;
                next = 0d;
                csvPath = ResolveCsvPath();
                TwinUiViewFreeze.Reset();
                Log("resumed after domain reload csv=" + csvPath);
            }

            if (deadline <= 0d)
            {
                deadline = EditorApplication.timeSinceStartup + 180d;
            }

            if (EditorApplication.timeSinceStartup > deadline)
            {
                Check("watchdog", false, "180 s at step=" + step);
                Finish();
                return;
            }

            if (EditorApplication.timeSinceStartup < next)
            {
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Check("exception step " + step, false, e.ToString());
                Finish();
            }
        }

        static void Step()
        {
            if (step == 0)
            {
                window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                shell = UnityEngine.Object.FindObjectOfType<TwinUiShellV2>();
                surface = TwinUiSurfaceHost.Instance;
                if (surface == null)
                {
                    // 进入 Play 会做 domain reload：静态 Instance 被清空，但场景对象仍在。
                    surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
                }
                if (window == null || shell == null || surface == null || surface.Page == null)
                {
                    Retry(0.5d, "boot refs");
                    return;
                }

                if (binder == null)
                {
                    binder = UnityEngine.Object.FindObjectOfType<TwinUIStatusBinder>();
                }

                Rect r = window.WindowRect;
                r.width = 1600f;
                r.height = 900f;
                r.x = 16f;
                r.y = 60f;
                window.ApplyLayout(r, false, false);
                surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                // R2 A-1：回放绝不能只挂 feed 而绕过 TwinUiSourceSwitch（旧版直连 Ensure 就是
// "顶部回放 / 底栏实时"矛盾的直接入口）。这里改走唯一入口，并把真值派生结果打出来。
                bool replayOk = TwinUiSourceSwitch.SetReplay(csvPath);
                feed = TwinUiReplayFeed.Active;
                Log("source switch replay ok=" + replayOk + " lastError=" + TwinUiSourceSwitch.LastError);
                Log("feed created rows=" + (feed != null ? feed.RowCount : -1)
                    + " loaded=" + (feed != null && feed.Loaded)
                    + " err=" + (feed != null ? feed.LoadError : "feed=null")
                    + " isReplay=" + TwinUiSourceSwitch.IsReplay
                    + " runId=" + TwinUiSourceSwitch.CurrentRunId
                    + " recordHz(窗口=" + TwinUiSourceSwitch.RecordHzWindow.ToString("F1")
                    + " 整文件=" + TwinUiSourceSwitch.RecordHzFile.ToString("F1")
                    + " scope=" + TwinUiSourceSwitch.RecordHzScope + ")");
                Next(1.5d);
                return;
            }

            if (step == 1)
            {
                if (binder == null || !binder.ReplayOverrideActive)
                {
                    Retry(0.5d, "binder replay override");
                    return;
                }

                var s = binder.Current;
                var joints = surface.Find("joints");
                var chart = joints != null && joints.View != null
                    ? joints.View.GetComponentInChildren<TwinUiLineChart>()
                    : null;
                if (!s.HasFrame || chart == null || chart.PointCount(0) < 5)
                {
                    Retry(0.5d, "replay frames");
                    return;
                }

                Log("replay live: source=" + s.SourceDisplay + " name=" + s.SourceName
                    + " progress=" + s.ReplayProgress01.ToString("F3")
                    + " points=" + chart.PointCount(0) + " ageMs=" + s.AgeMs.ToString("F1"));
                Shot("replay_twin_1920");
                Next(0.6d);
                return;
            }

            if (step == 2)
            {
                // 六核心卡片齐全（twin 页 WidgetIds 生效）
                var page = surface.Page;
                var present = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < page.Cards.Count; i++)
                {
                    var c = page.Cards[i];
                    if (c != null && c.gameObject.activeInHierarchy)
                    {
                        present.Add(c.ModuleId);
                    }
                }

                var missing = new List<string>();
                for (int i = 0; i < CoreCards.Length; i++)
                {
                    if (!present.Contains(CoreCards[i]))
                    {
                        missing.Add(CoreCards[i]);
                    }
                }

                Check("six core cards visible", missing.Count == 0,
                    "missing=" + string.Join(",", missing) + " visibleCards=" + present.Count);

                // 顶部连接状态条
                string status = surface.ConnectionStatusText;
                Check("connection strip visible", surface.ConnectionStatusVisible, "text=" + status);
                Check("connection strip says replay", status.Contains("回放"), status);
                Check("connection strip has run id", status.Contains(binder.Current.SourceName), status);
                Check("connection strip has update time", status.Contains("更新 "), status);
                Log("status = " + status);

                // joints 曲线：6 通道 + 图例 + 20 s 时间窗
                var joints = surface.Find("joints");
                var chart = joints.View.GetComponentInChildren<TwinUiLineChart>();
                Check("chart 6 channels", chart.SeriesCount == 6, "series=" + chart.SeriesCount);
                Check("chart legend drawn", chart.LastDrawSeries >= 5, "draw=" + chart.LastDrawSeries);
                // R2 T6：时间窗语义从"固定点数"改成"按时间戳裁剪"，此断言随之从
                // window==80 改为"时间窗秒数=20 且控件处于时间戳模式（点数窗=0）"。
                Check("chart time window 20s (timestamp mode)",
                    Mathf.Abs((float)chart.TimeWindowSeconds - 20f) < 0.01f && chart.WindowPoints == 0,
                    "windowSeconds=" + chart.TimeWindowSeconds + " pointWindow=" + chart.WindowPoints
                    + " drawn=" + chart.DrawnPoints(0) + " span=" + chart.DrawnSpanSeconds.ToString("F2"));

                // 缺数不填 0：replay CSV 无关节力矩 / 六维力
                var torque = surface.Find("torque");
                var wrench = surface.Find("wrench");
                Check("torque missing shows placeholder not zero",
                    AllValuesEqual(torque, TwinUITextR3.Placeholder),
                    ValuesOf(torque));
                Check("wrench missing shows placeholder not zero",
                    AllValuesEqual(wrench, TwinUITextR3.Placeholder),
                    ValuesOf(wrench));

                // 关节角有真实回放数值（不是占位符）
                Check("joints has replay values", !AllValuesEqual(joints, TwinUITextR3.Placeholder), ValuesOf(joints));

                // 会话卡：回放不写记录
                var session = surface.Find("session");
                Check("session not recording in replay", ValuesContainText(session, "未录制"), ValuesOf(session));

                // 回放/实时源身份
                Check("source identity is replay", binder.Current.IsReplay && binder.Current.SourceKind == "Replay",
                    binder.Current.SourceKind + "/" + binder.Current.SourceDisplay);

                Next(0.4d);
                return;
            }

            if (step == 3)
            {
                // 冻结显示
                var joints = surface.Find("joints");
                var chart = joints.View.GetComponentInChildren<TwinUiLineChart>();
                frozenPoints = chart.PointCount(0);
                frozenProgress = feed.PlaybackSeconds;
                frozenCell = FirstValue(joints);
                frozenStatus = surface.ConnectionStatusText;
                TwinUiViewFreeze.Set(true);
                Log("freeze ON points=" + frozenPoints + " playback=" + frozenProgress.ToString("F2")
                    + " cell=" + frozenCell);
                Next(2.5d);
                return;
            }

            if (step == 4)
            {
                var joints = surface.Find("joints");
                var chart = joints.View.GetComponentInChildren<TwinUiLineChart>();
                double progressNow = feed.PlaybackSeconds;
                int pointsNow = chart.PointCount(0);
                string cellNow = FirstValue(joints);
                string statusNow = surface.ConnectionStatusText;
                Shot("replay_frozen_1920");

                Check("freeze keeps replay clock running", progressNow > frozenProgress + 0.5d,
                    "before=" + frozenProgress.ToString("F2") + " after=" + progressNow.ToString("F2"));
                Check("freeze stops chart display", pointsNow == frozenPoints,
                    "before=" + frozenPoints + " after=" + pointsNow);
                Check("freeze stops value display", cellNow == frozenCell, "before=" + frozenCell + " after=" + cellNow);
                Check("freeze announced in status", statusNow.Contains("显示冻结"), statusNow);

                TwinUiViewFreeze.Set(false);
                Next(2.0d);
                return;
            }

            if (step == 5)
            {
                var joints = surface.Find("joints");
                var chart = joints.View.GetComponentInChildren<TwinUiLineChart>();
                Check("unfreeze resumes chart display", chart.PointCount(0) > frozenPoints,
                    "before=" + frozenPoints + " after=" + chart.PointCount(0));

                // 通道开关：关掉 J3，图例可见通道数跟随
                chart.SetSeriesVisible(2, false);
                Check("channel toggle off", !chart.IsSeriesVisible(2) && chart.VisibleSeriesCount == 5,
                    "visible=" + chart.VisibleSeriesCount);
                Shot("replay_channel_off_1920");
                chart.SetSeriesVisible(2, true);
                Check("channel toggle back", chart.VisibleSeriesCount == 6, "visible=" + chart.VisibleSeriesCount);
                Shot("replay_live_1920");
                Next(1.0d);
                return;
            }

            if (step == 6)
            {
                Rect r = window.WindowRect;
                r.width = 1200f;
                r.height = 900f;
                window.ApplyLayout(r, false, false);
                Next(0.8d);
                return;
            }

            if (step == 7)
            {
                Shot("replay_twin_1200");
                Rect r = window.WindowRect;
                r.width = 720f;
                window.ApplyLayout(r, false, false);
                Next(0.8d);
                return;
            }

            if (step == 8)
            {
                Shot("replay_twin_720");
                Info("screen", Screen.width + "x" + Screen.height);
                Finish();
                return;
            }

            Finish();
        }

        // ------------------------------------------------------------ helpers

        static void Retry(double delay, string what)
        {
            tries++;
            if (tries > 40)
            {
                Check("wait timeout: " + what, false, "tries=" + tries);
                Finish();
                return;
            }

            Log("waiting (" + what + ") try=" + tries);
            next = EditorApplication.timeSinceStartup + delay;
        }

        static void Next(double delay)
        {
            tries = 0;
            step++;
            next = EditorApplication.timeSinceStartup + delay;
        }

        static string FirstValue(TwinUiModule module)
        {
            if (module == null || module.View == null)
            {
                return string.Empty;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "Value")
                {
                    return texts[i].text;
                }
            }

            return string.Empty;
        }

        static string ValuesOf(TwinUiModule module)
        {
            var sb = new StringBuilder();
            if (module == null || module.View == null)
            {
                return "no view";
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "Value" && texts[i].gameObject.activeInHierarchy)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('|');
                    }

                    sb.Append(texts[i].text);
                }
            }

            return sb.ToString();
        }

        static bool AllValuesEqual(TwinUiModule module, string expected)
        {
            int found = 0;
            if (module == null || module.View == null)
            {
                return false;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || texts[i].name != "Value" || !texts[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                found++;
                if (texts[i].text != expected)
                {
                    return false;
                }
            }

            return found > 0;
        }

        static bool ValuesContainText(TwinUiModule module, string needle)
        {
            if (module == null || module.View == null)
            {
                return false;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].text != null && texts[i].text.Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }

        static void Shot(string name)
        {
            Directory.CreateDirectory(SHOTS);
            // R2 A-4：本阶段的截图是**屏幕级**（只改窗口宽度，屏幕恒 1920x1080），所以文件名必须
            // 带真实屏幕尺寸，不能沿用 "replay_twin_720" 这种按窗口宽度命名的旧写法（会被误读成档位证据）。
            // 真档位截图（720/1200/1920 真实像素 + IHDR 断言）由 UI/60 产出。
            // 注意：CaptureScreenshotAsTexture 在 EditorApplication.update 里返回 null（必须等 end-of-frame），
            // 因此用异步 CaptureScreenshot（写盘尺寸=真实屏幕），像素由事后 IHDR 复核。
            string real = name + "_screen" + Screen.width + "x" + Screen.height + ".png";
            ScreenCapture.CaptureScreenshot(SHOTS + "/" + real);
            Info("screenshot", real + " at " + Screen.width + "x" + Screen.height
                + " (screen-level; 档位截图见 UI/60)");
        }

        static void Finish()
        {
            if (log != null)
            {
                log.AppendLine("RESULT=" + (fails == 0 ? "PASS" : "FAIL") + " failures=" + fails);
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui59_replay_ui.txt", log.ToString());
            }

            SessionState.SetBool(Armed, false);
            TwinUiViewFreeze.Reset();
            EditorApplication.isPlaying = false;
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                fails++;
            }

            log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        static void Info(string key, string value)
        {
            log.AppendLine("INFO " + key + " | " + value);
        }

        static void Log(string message)
        {
            log.AppendLine("INFO " + message);
        }
    }
}