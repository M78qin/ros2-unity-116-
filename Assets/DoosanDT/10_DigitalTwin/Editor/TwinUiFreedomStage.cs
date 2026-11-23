// TwinUiFreedomStage.cs —— P7 窗口自由度自检（UI/46 开关 / UI/47 自检）
// UI/46：只写 PlayerPrefs 的 shell 模式开关（V2/Legacy/清除），幂等，零场景改动。
// UI/47（auto-exit）：进 Play 后断言
//   ① V2 外壳安装（5 页签 + 任务栏 + 14 卡片）；② 默认页卡片数与页高；
//   ③ 720x480 / 900 / 1200 / 1600 四档尺寸：断点、页宽、页高、页签行数；
//   ④ 最大化高度让出任务栏；⑤ 最小化/隐藏/恢复；⑥ 五页签切换与卡片数；⑦ 预设与存档往返；
//   ⑧ 截图 6 张；⑨ totalErrors=0。结果写 Logs/ui_freedom_check.txt 并复制到证据目录。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiFreedomStage
    {
        private const string Tag = "[FREEDOM]";

        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_freedom";

        [MenuItem("DigitalTwin/UI/46 Shell Mode: V2 (browser workbench)", false, 460)]
        public static void UseV2()
        {
            TwinUiShellMode.Set(TwinUiShellKind.V2);
            Debug.Log(Tag + " shell mode -> V2 (saved). Enter Play to apply; rollback with UI/46b.");
        }

        [MenuItem("DigitalTwin/UI/46b Shell Mode: Legacy (P2-P6 window)", false, 461)]
        public static void UseLegacy()
        {
            TwinUiShellMode.Set(TwinUiShellKind.Legacy);
            Debug.Log(Tag + " shell mode -> Legacy (saved). Enter Play to apply.");
        }

        [MenuItem("DigitalTwin/UI/46c Shell Mode: Clear Saved", false, 462)]
        public static void ClearMode()
        {
            TwinUiShellMode.ClearSaved();
            TwinUiShellPrefs.Clear();
            Debug.Log(Tag + " shell mode + shell prefs cleared, default=" + TwinUiShellMode.DefaultKind);
        }

        [MenuItem("DigitalTwin/UI/47 Window Freedom Check (auto-exit)", false, 470)]
        public static void RunFreedomCheck()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/47 skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                Debug.LogWarning(Tag + " UI/47 requires shell mode V2; switching now (UI/46).");
                TwinUiShellMode.Set(TwinUiShellKind.V2);
            }

            TwinUiFreedomCheck.Reset();
            SessionState.SetBool(TwinUiFreedomCheck.PendingKey, true);
            Debug.Log(Tag + " UI/47 armed, entering play; log=" + TwinUiFreedomCheck.LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiFreedomCheck
    {
        public const string PendingKey = "TwinUiFreedomCheck.Pending";
        public const string LogPath = "Logs/ui_freedom_check.txt";
        public const string JsonPath = "Logs/ui_freedom_check.json";
        private const string EvidenceShots = TwinUiFreedomStage.EvidenceRoot + @"\shots";
        private const string EvidenceLogs = TwinUiFreedomStage.EvidenceRoot + @"\logs";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly Queue<string> ShotQueue = new Queue<string>();
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static TwinUiResponsiveLayout _responsive;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static int _logErrors;
        private static int _logExceptions;
        private static string _prefShell;
        private static bool _prefHadShell;
        private static string _prefWindow;
        private static bool _prefHadWindow;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            ShotQueue.Clear();
            _window = null;
            _shell = null;
            _surface = null;
            _responsive = null;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _logErrors = 0;
            _logExceptions = 0;
        }

        [InitializeOnLoadMethod]
        private static void HookUpdate()
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
                Start();
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _startAt > 120f)
            {
                Add("timeout", "check exceeded 120s", false);
                Finish();
                return;
            }

            if (ShotQueue.Count > 0)
            {
                ScreenShotNext();
                return;
            }

            Step();
        }

        private static void Start()
        {
            _started = true;
            _startAt = Time.realtimeSinceStartup;
            _nextAt = _startAt + 0.6f;
            Application.logMessageReceived += OnLog;
            _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            _responsive = UnityEngine.Object.FindObjectOfType<TwinUiResponsiveLayout>();
            _shell = _window != null ? _window.ShellV2 : null;
            _surface = _shell != null ? _shell.Surface : null;
            _prefHadShell = PlayerPrefs.HasKey(TwinUiShellPrefs.Key);
            _prefShell = PlayerPrefs.GetString(TwinUiShellPrefs.Key, string.Empty);
            _prefHadWindow = PlayerPrefs.HasKey("TwinUI.Window.Layout.v1");
            _prefWindow = PlayerPrefs.GetString("TwinUI.Window.Layout.v1", string.Empty);
            Debug.Log("[FREEDOM] start window=" + (_window != null) + " shell=" + (_shell != null)
                + " surface=" + (_surface != null) + " responsive=" + (_responsive != null));
        }

        private static void Step()
        {
            _step++;
            switch (_step)
            {
                case 1:
                    StepReady();
                    break;
                case 2:
                    StepDefaultPage();
                    break;
                case 3:
                    StepResize(720f, 480f, TwinUiBreakpoint.Compact);
                    break;
                case 4:
                    StepResize(900f, 700f, TwinUiBreakpoint.Narrow);
                    break;
                case 5:
                    StepResize(1200f, 800f, TwinUiBreakpoint.Medium);
                    break;
                case 6:
                    StepResize(1600f, 900f, TwinUiBreakpoint.Wide);
                    break;
                case 7:
                    StepMaximize();
                    break;
                case 8:
                    StepMinimizeHide();
                    break;
                case 9:
                    StepTabs();
                    break;
                case 10:
                    StepPrefsAndPresets();
                    break;
                case 11:
                    StepShots();
                    break;
                case 12:
                    Finish();
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private static void StepReady()
        {
            bool ok = _window != null && _shell != null && _surface != null && _responsive != null;
            Add("shell installed", "window=" + (_window != null) + " shell=" + (_shell != null)
                + " surface=" + (_surface != null) + " responsive=" + (_responsive != null), ok);
            if (!ok)
            {
                Finish();
                return;
            }

            Add("shell tabs=5", "tabs=" + _shell.TabCount, _shell.TabCount == 5);
            Add("taskbar exists", "taskbar=" + (_shell.Taskbar != null), _shell.Taskbar != null);
            Add("modules=19", "modules=" + _surface.ModuleCount, _surface.ModuleCount == 19);
            // P3：启动默认全部停靠（在归零之前断言，避免被 ResetForCheck 掩盖）。
            Add("startup floating=0", "floating=" + _surface.FloatingCount
                + " saved=" + _surface.SavedFloatingCount, _surface.FloatingCount == 0);
            // 上一轮使用/自检可能留下页签或浮窗 prefs；先归零，保证"默认页"断言可重复。
            _surface.ResetForCheck();
            Add("normalized at start", "tab=" + _surface.Tab + " floating=" + _surface.FloatingCount,
                _surface.Tab == TwinUiWorkspaceRegistry.Overview && _surface.FloatingCount == 0);
            Add("min size lowered", "min=" + TwinUiWindowHost.MinWidth + "x" + TwinUiWindowHost.MinHeight,
                TwinUiWindowHost.MinWidth <= 720f && TwinUiWindowHost.MinHeight <= 480f);
            Add("taskbar inset", "inset=" + _window.BottomInset, _window.BottomInset > 20f);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepDefaultPage()
        {
            Add("default tab", "tab=" + _surface.Tab, _surface.Tab == TwinUiWorkspaceRegistry.Overview);
            var ws = TwinUiWorkspaceRegistry.Find(TwinUiWorkspaceRegistry.Overview);
            int expected = ws != null ? ws.WidgetIds.Length : -1;
            Add("overview cards", "cards=" + _surface.Page.CardCount + " expected=" + expected,
                _surface.Page.CardCount == expected);
            Add("page height > 0", "pageH=" + _surface.PageHeight.ToString("F0"), _surface.PageHeight > 200f);
            Add("page title", "title=" + _surface.Page.TitleText, _surface.Page.TitleText.Length > 0);
            _nextAt = Time.realtimeSinceStartup + 0.2f;
        }

        private static void StepResize(float w, float h, TwinUiBreakpoint expected)
        {
            _window.ApplyLayout(new Rect(16f, 16f, w, h), false, false);
            Add("resize " + w.ToString("F0") + "x" + h.ToString("F0") + " tier",
                "tier=" + _responsive.Current + " expected=" + expected
                + " contentW=" + _responsive.LastWidth.ToString("F0"), _responsive.Current == expected);
            Add("resize " + w.ToString("F0") + " viewport width",
                "viewport=" + _shell.ViewportWidth.ToString("F0")
                + " content=" + _responsive.LastWidth.ToString("F0"),
                Mathf.Abs(_shell.ViewportWidth - _responsive.LastWidth) < 3f);
            Add("resize " + w.ToString("F0") + " viewport height",
                "viewportH=" + _shell.ViewportHeight.ToString("F0"), _shell.ViewportHeight > 100f);
            Add("resize " + w.ToString("F0") + " page width fits",
                "pageW=" + _surface.PageWidth.ToString("F0")
                + " viewportW=" + _shell.ViewportWidth.ToString("F0"),
                _surface.PageWidth <= _shell.ViewportWidth + 2f);
            Add("resize " + w.ToString("F0") + " page height",
                "pageH=" + _surface.PageHeight.ToString("F0"), _surface.PageHeight >= 200f);
            Add("resize " + w.ToString("F0") + " tabs rows",
                "rows=" + _shell.TabsRows, _shell.TabsRows >= 1);
            _nextAt = Time.realtimeSinceStartup + 0.35f;
        }

        private static void StepMaximize()
        {
            float canvasH = _window.CanvasRect != null ? _window.CanvasRect.rect.height : 1080f;
            float expectedH = canvasH - _window.BottomInset;
            _window.SetMaximized(true);
            Rect r = _window.WindowRect;
            Add("maximize height excludes taskbar", "rect=" + _window.DescribeRect(r)
                + " expectedH=" + expectedH.ToString("F0") + " canvasH=" + canvasH.ToString("F0"),
                Mathf.Abs(r.height - expectedH) < 3f);
            Add("maximize width = canvas", "w=" + r.width.ToString("F0"),
                Mathf.Abs(r.width - _window.CanvasRect.rect.width) < 3f);
            _window.SetMaximized(false);
            Add("restore after maximize", "rect=" + _window.DescribeRect(_window.WindowRect),
                _window.WindowRect.width < _window.CanvasRect.rect.width - 2f);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepMinimizeHide()
        {
            _window.Minimize();
            Add("minimize hides window", "visible=" + _window.IsVisible + " minimized=" + _window.IsMinimized,
                !_window.IsVisible && _window.IsMinimized);
            _window.Restore();
            Add("restore after minimize", "visible=" + _window.IsVisible, _window.IsVisible);
            _window.Hide();
            Add("hide hides window", "visible=" + _window.IsVisible + " hidden=" + _window.IsHidden,
                !_window.IsVisible && _window.IsHidden);
            _window.Restore();
            Add("restore after hide", "visible=" + _window.IsVisible, _window.IsVisible);
            _nextAt = Time.realtimeSinceStartup + 0.2f;
        }

        private static void StepTabs()
        {
            var all = TwinUiWorkspaceRegistry.All;
            int first = Items.Count;
            for (int i = 0; i < all.Count; i++)
            {
                var ws = all[i];
                _surface.SetTab(ws.Id);
                Add("tab " + ws.Id, "tab=" + _shell.CurrentTab + " cards=" + _surface.Page.CardCount
                    + " expected=" + ws.WidgetIds.Length,
                    _surface.Tab == ws.Id && _shell.CurrentTab == ws.Id
                    && _surface.Page.CardCount == ws.WidgetIds.Length);
                Add("tab " + ws.Id + " no auto float", "floating=" + _surface.FloatingCount,
                    _surface.FloatingCount == 0);
                Add("page title " + ws.Id, "title=" + _surface.Page.TitleText,
                    _surface.Page.TitleText == ws.Title);
            }

            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            Add("tabs checked", "count=" + (Items.Count - first), Items.Count - first >= 10);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepPrefsAndPresets()
        {
            _surface.ApplyPreset(TwinUiLayoutPresets.Monitor);
            var data = TwinUiShellPrefs.Load();
            Add("preset applied", "preset=" + _surface.Preset + " tab=" + _surface.Tab,
                _surface.Preset == TwinUiLayoutPresets.Monitor && _surface.Tab == TwinUiWorkspaceRegistry.Twin);
            Add("prefs persisted", "savedTab=" + data.tab + " savedPreset=" + data.preset
                + " widgets=" + data.widgets.Count,
                data.tab == TwinUiWorkspaceRegistry.Twin && data.preset == TwinUiLayoutPresets.Monitor
                && data.widgets.Count >= 14);
            Add("preset floating windows", "floating=" + _surface.FloatingCount,
                _surface.FloatingCount == 0);
            _surface.ApplyPreset(TwinUiLayoutPresets.Default);
            Add("preset default restores", "tab=" + _surface.Tab + " floating=" + _surface.FloatingCount,
                _surface.Tab == TwinUiWorkspaceRegistry.Overview && _surface.FloatingCount == 0);
            _surface.ResetForCheck();
            Add("reset for check", "tab=" + _surface.Tab, _surface.Tab == TwinUiWorkspaceRegistry.Overview);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepShots()
        {
            ShotQueue.Clear();
            QueueShot("186_freedom_compact_720.png", 720f, 480f, TwinUiWorkspaceRegistry.Overview);
            QueueShot("187_freedom_narrow_900.png", 900f, 700f, TwinUiWorkspaceRegistry.Overview);
            QueueShot("188_freedom_medium_1200.png", 1200f, 800f, TwinUiWorkspaceRegistry.Overview);
            QueueShot("189_freedom_wide_1600.png", 1600f, 900f, TwinUiWorkspaceRegistry.Overview);
            QueueShot("190_freedom_twin_1600.png", 1600f, 900f, TwinUiWorkspaceRegistry.Twin);
            QueueShot("191_freedom_control_1200.png", 1200f, 800f, TwinUiWorkspaceRegistry.Control);
            _nextAt = Time.realtimeSinceStartup;
        }

        private static void QueueShot(string name, float w, float h, string tab)
        {
            ShotQueue.Enqueue(name + "|" + w.ToString(CultureInfo.InvariantCulture) + "|"
                + h.ToString(CultureInfo.InvariantCulture) + "|" + tab);
        }

        private static void ScreenShotNext()
        {
            string[] parts = ShotQueue.Dequeue().Split('|');
            float w = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float h = float.Parse(parts[2], CultureInfo.InvariantCulture);
            _window.ApplyLayout(new Rect(16f, 16f, w, h), false, false);
            _surface.SetTab(parts[3]);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            Capture(parts[0]);
            _nextAt = Time.realtimeSinceStartup + 0.55f;
        }

        private static void Capture(string name)
        {
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                string path = Path.Combine(EvidenceShots, name);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FREEDOM] screenshot failed: " + e.Message);
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[FREEDOM] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
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
            RestorePrefs();

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Window Freedom Check (UI/47)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("scene=" + SceneManager.GetActiveScene().path + " hash=" + SceneHash());
            sb.AppendLine("shellMode=" + TwinUiShellMode.Kind
                + " min=" + TwinUiWindowHost.MinWidth + "x" + TwinUiWindowHost.MinHeight
                + " defaultRatio=" + TwinUiWindowHost.DefaultWidthRatio + "/" + TwinUiWindowHost.DefaultHeightRatio);
            foreach (var item in Items)
            {
                sb.AppendLine("[FREEDOM] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0 && Items.Count >= 30;
            sb.AppendLine("[FREEDOM] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui47_freedom_check.txt"), true);
                string json = "{\"items\":" + Items.Count + ",\"fails\":" + fails
                    + ",\"logErrors\":" + _logErrors + ",\"logExceptions\":" + _logExceptions
                    + ",\"shots\":" + Shots.Count + ",\"result\":\"" + (ok ? "PASS" : "FAIL") + "\"}";
                File.WriteAllText(JsonPath, json, new UTF8Encoding(false));
                File.Copy(JsonPath, Path.Combine(EvidenceLogs, "ui47_freedom_check.json"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FREEDOM] log write failed: " + e.Message);
            }

            Debug.Log("[FREEDOM] finish result=" + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails + " log=" + LogPath);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            };
        }

        private static void RestorePrefs()
        {
            try
            {
                if (_prefHadShell)
                {
                    PlayerPrefs.SetString(TwinUiShellPrefs.Key, _prefShell);
                }
                else
                {
                    PlayerPrefs.DeleteKey(TwinUiShellPrefs.Key);
                }

                if (_prefHadWindow)
                {
                    PlayerPrefs.SetString("TwinUI.Window.Layout.v1", _prefWindow);
                }
                else
                {
                    PlayerPrefs.DeleteKey("TwinUI.Window.Layout.v1");
                }

                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FREEDOM] restore prefs failed: " + e.Message);
            }
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

                Debug.Log("[FREEDOM] [logfail] " + type + " " + condition);
            }
        }

        private static string SceneHash()
        {
            try
            {
                string path = SceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(path))
                {
                    return "unknown";
                }

                using (var sha = SHA256.Create())
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
                }
            }
            catch (Exception)
            {
                return "hash-error";
            }
        }
    }
}