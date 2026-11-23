// TwinUiCardStage.cs —— P8 卡片框架自检（UI/48）+ 视觉/令牌审计与全页截图（UI/49）
// UI/48：卡片拆出 → 浮窗（单实例、内容视图换父节点）→ 最大化/还原 → 最小化/恢复 → 多浮窗 z 序
//         → 图钉 → 页切换保浮窗 → Esc 路径关闭 → 归位 → 存档往返 → 截图。全部 Play 内实测。
// UI/49：令牌/字阶/圆角一致性 + 五页 1440x900 截图 + 主题切换（Ocean→Light→Ocean）不抛异常。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiCardStage
    {
        private const string Tag = "[CARD]";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_card";

        [MenuItem("DigitalTwin/UI/48 Card Framework Check (auto-exit)", false, 480)]
        public static void RunCardCheck()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/48 skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                TwinUiShellMode.Set(TwinUiShellKind.V2);
            }

            TwinUiCardCheck.Reset();
            SessionState.SetBool(TwinUiCardCheck.PendingKey, true);
            Debug.Log(Tag + " UI/48 armed; log=" + TwinUiCardCheck.LogPath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("DigitalTwin/UI/49 Visual & Token Audit (auto-exit)", false, 490)]
        public static void RunVisualAudit()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/49 skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                TwinUiShellMode.Set(TwinUiShellKind.V2);
            }

            TwinUiVisualAudit.Reset();
            SessionState.SetBool(TwinUiVisualAudit.PendingKey, true);
            Debug.Log(Tag + " UI/49 armed; log=" + TwinUiVisualAudit.LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiCardCheck
    {
        public const string PendingKey = "TwinUiCardCheck.Pending";
        public const string LogPath = "Logs/ui_card_check.txt";
        private const string EvidenceShots = TwinUiCardStage.EvidenceRoot + @"\shots";
        private const string EvidenceLogs = TwinUiCardStage.EvidenceRoot + @"\logs";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly Queue<System.Action> ShotActions = new Queue<System.Action>();
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static int _logErrors;
        private static int _logExceptions;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            ShotActions.Clear();
            _window = null;
            _shell = null;
            _surface = null;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _logErrors = 0;
            _logExceptions = 0;
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
                Start();
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (ShotActions.Count > 0)
            {
                System.Action shot = ShotActions.Dequeue();
                shot();
                _nextAt = Time.realtimeSinceStartup + 0.55f;
                if (ShotActions.Count == 0)
                {
                    Step();
                }

                return;
            }

            if (Time.realtimeSinceStartup - _startAt > 90f)
            {
                Add("timeout", "check exceeded 90s", false);
                Finish();
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
            _shell = _window != null ? _window.ShellV2 : null;
            _surface = _shell != null ? _shell.Surface : null;
            Debug.Log("[CARD] start window=" + (_window != null) + " shell=" + (_shell != null)
                + " surface=" + (_surface != null));
            if (_surface != null)
            {
                // 前一次自检/使用可能留下浮窗与 prefs；先归零，保证断言可重复。
                _surface.ResetForCheck();
                Debug.Log("[CARD] normalized at start: floating=" + _surface.FloatingCount
                    + " tab=" + _surface.Tab);
            }
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
                    StepDetachOne();
                    break;
                case 3:
                    StepSingleInstance();
                    break;
                case 4:
                    StepMaximizeFloating();
                    break;
                case 5:
                    StepMinimizeFloating();
                    break;
                case 6:
                    StepMultiFloating();
                    break;
                case 7:
                    StepPinAndEscape();
                    break;
                case 8:
                    StepDockBack();
                    break;
                case 9:
                    StepPrefsRoundTrip();
                    break;
                case 10:
                    StepShots();
                    break;
                case 11:
                    Finish();
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private static int ExpectedCards(string workspaceId)
        {
            var ws = TwinUiWorkspaceRegistry.Find(workspaceId);
            return ws != null ? ws.WidgetIds.Length : 0;
        }

        private static void StepReady()        {
            bool ok = _window != null && _shell != null && _surface != null;
            Add("ready", "window=" + (_window != null) + " shell=" + (_shell != null)
                + " surface=" + (_surface != null), ok);
            if (!ok)
            {
                Finish();
                return;
            }

            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            Add("twin page cards", "cards=" + _surface.Page.CardCount + " floating=" + _surface.FloatingCount,
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Twin) && _surface.FloatingCount == 0);
            Add("card shell exists", "card=sync:" + (_surface.CardFor("sync") != null),
                _surface.CardFor("sync") != null);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepDetachOne()
        {
            _surface.Detach("sync");
            var module = _surface.Find("sync");
            var win = _surface.FloatingWindowFor("sync");
            bool ok = _surface.FloatingCount == 1 && win != null && module != null
                && module.State == TwinUiModuleState.Floating;
            Add("detach sync -> floating", "floating=" + _surface.FloatingCount
                + " " + _surface.DescribeModule("sync"), ok);

            bool parentOk = win != null && module != null && module.View != null
                && module.View.parent == win.Content;
            Add("content view reparented to window", "viewParent="
                + (module != null && module.View != null && module.View.parent != null
                    ? module.View.parent.name : "null"), parentOk);

            var card = _surface.CardFor("sync");
            bool placeholder = card != null && card.Content != null && !card.Content.gameObject.activeSelf;
            Add("card placeholder shown", "cardContentActive="
                + (card != null && card.Content != null ? card.Content.gameObject.activeSelf.ToString() : "null"),
                placeholder);

            Rect r = win != null ? win.WindowRect : Rect.zero;
            Rect canvas = _window.CanvasRect.rect;
            Add("floating rect inside canvas", _window.DescribeRect(r),
                r.x >= -1f && r.y >= -1f && r.xMax <= canvas.width + 1f && r.yMax <= canvas.height + 1f);

            Add("taskbar entry count", "entries=" + (_shell.Taskbar != null ? _shell.Taskbar.EntryCount : -1),
                _shell.Taskbar != null && _shell.Taskbar.EntryCount == 1);
            Add("attach once", "state=" + (module != null ? module.State.ToString() : "null"),
                module != null && module.State == TwinUiModuleState.Floating);
            _nextAt = Time.realtimeSinceStartup + 0.35f;
        }

        private static void StepSingleInstance()
        {
            _surface.Detach("sync");
            Add("detach again keeps single instance", "floating=" + _surface.FloatingCount,
                _surface.FloatingCount == 1);
            var module = _surface.Find("sync");
            int views = 0;
            if (module != null && module.View != null)
            {
                views = 1;
            }

            Add("single content view", "views=" + views + " view="
                + (module != null && module.View != null ? module.View.name : "null"), views == 1);
            _nextAt = Time.realtimeSinceStartup + 0.25f;
        }

        private static void StepMaximizeFloating()
        {
            _surface.ToggleMaximizeFloating("sync");
            var win = _surface.FloatingWindowFor("sync");
            Rect r = win != null ? win.WindowRect : Rect.zero;
            Rect canvas = _window.CanvasRect.rect;
            bool ok = _surface.IsFloatingMaximized("sync")
                && Mathf.Abs(r.width - (canvas.width - 24f)) < 4f
                && Mathf.Abs(r.height - (canvas.height - 24f)) < 4f;
            Add("floating maximize", "max=" + _surface.IsFloatingMaximized("sync") + " rect="
                + _window.DescribeRect(r), ok);
            _surface.ToggleMaximizeFloating("sync");
            var win2 = _surface.FloatingWindowFor("sync");
            Add("floating restore", "max=" + _surface.IsFloatingMaximized("sync") + " rect="
                + (win2 != null ? _window.DescribeRect(win2.WindowRect) : "null"),
                !_surface.IsFloatingMaximized("sync"));
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepMinimizeFloating()
        {
            _surface.MinimizeFloating("sync");
            var win = _surface.FloatingWindowFor("sync");
            bool ok = _surface.IsMinimizedFloating("sync") && win != null && !win.gameObject.activeSelf;
            Add("floating minimize hides window", "minimized=" + _surface.IsMinimizedFloating("sync")
                + " active=" + (win != null ? win.gameObject.activeSelf.ToString() : "null"), ok);
            Add("taskbar shows minimized", "entries=" + (_shell.Taskbar != null ? _shell.Taskbar.EntryCount : -1),
                _shell.Taskbar != null && _shell.Taskbar.EntryCount == 1);
            _surface.RestoreFloating("sync");
            var win2 = _surface.FloatingWindowFor("sync");
            Add("floating restore", "minimized=" + _surface.IsMinimizedFloating("sync")
                + " active=" + (win2 != null ? win2.gameObject.activeSelf.ToString() : "null"),
                !_surface.IsMinimizedFloating("sync") && win2 != null && win2.gameObject.activeSelf);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepMultiFloating()
        {
            _surface.Detach("joints");
            _surface.Detach("torque");
            Add("multi floating count", "floating=" + _surface.FloatingCount, _surface.FloatingCount == 3);
            Add("taskbar entries=3", "entries=" + (_shell.Taskbar != null ? _shell.Taskbar.EntryCount : -1),
                _shell.Taskbar != null && _shell.Taskbar.EntryCount == 3);
            Add("top floating z-order", "top=" + _surface.TopFloatingId(), _surface.TopFloatingId() == "torque");
            _surface.BringToFront("sync");
            Add("bring to front", "top=" + _surface.TopFloatingId(), _surface.TopFloatingId() == "sync");
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepPinAndEscape()
        {
            _surface.TogglePin("sync");
            Add("pin floating", "pinned=" + _surface.IsPinned("sync"), _surface.IsPinned("sync"));
            _surface.TogglePin("sync");
            Add("unpin floating", "pinned=" + _surface.IsPinned("sync"), !_surface.IsPinned("sync"));

            int before = _surface.FloatingCount;
            bool closed = _surface.CloseTopFloatingForCheck();
            Add("escape closes top floating", "before=" + before + " after=" + _surface.FloatingCount
                + " closed=" + closed, closed && _surface.FloatingCount == before - 1);

            _surface.SetTab(TwinUiWorkspaceRegistry.Control);
            Add("page switch keeps floating", "tab=" + _surface.Tab + " floating=" + _surface.FloatingCount,
                _surface.FloatingCount == before - 1);
            Add("control page cards", "cards=" + _surface.Page.CardCount,
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Control));
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            Add("back to twin cards", "cards=" + _surface.Page.CardCount,
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Twin) - (before - 1));
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepDockBack()
        {
            var ids = new List<string>(_surface.FloatingIds);
            for (int i = 0; i < ids.Count; i++)
            {
                _surface.DockBack(ids[i]);
            }

            Add("dock back all", "floating=" + _surface.FloatingCount, _surface.FloatingCount == 0);
            Add("taskbar empties", "entries=" + (_shell.Taskbar != null ? _shell.Taskbar.EntryCount : -1),
                _shell.Taskbar != null && _shell.Taskbar.EntryCount == 0);
            var module = _surface.Find("sync");
            var card = _surface.CardFor("sync");
            bool parentOk = module != null && module.View != null && card != null
                && module.View.parent == card.Content && card.Content.gameObject.activeSelf;
            Add("view back in card", "parent="
                + (module != null && module.View != null && module.View.parent != null
                    ? module.View.parent.name : "null") + " cardContentActive="
                + (card != null && card.Content != null ? card.Content.gameObject.activeSelf.ToString() : "null"),
                parentOk);
            Add("module state card", "state=" + (module != null ? module.State.ToString() : "null"),
                module != null && module.State == TwinUiModuleState.Card);
            Add("twin cards restored", "cards=" + _surface.Page.CardCount,
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Twin));
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepPrefsRoundTrip()
        {
            _surface.Detach("wrench");
            _surface.SavePrefs();
            var data = TwinUiShellPrefs.Load();
            bool found = false;
            for (int i = 0; i < data.widgets.Count; i++)
            {
                if (data.widgets[i].id == "wrench" && data.widgets[i].state == (int)TwinUiModuleState.Floating)
                {
                    found = true;
                }
            }

            Add("prefs store floating state", "widgets=" + data.widgets.Count + " wrenchFloating=" + found, found);
            _surface.DockBack("wrench");
            _surface.ResetForCheck();
            Add("reset after prefs test", "floating=" + _surface.FloatingCount + " tab=" + _surface.Tab,
                _surface.FloatingCount == 0 && _surface.Tab == TwinUiWorkspaceRegistry.Overview);
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void StepShots()
        {
            _window.ApplyLayout(new Rect(16f, 16f, 1440f, 900f), false, false);
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            ShotActions.Clear();
            ShotActions.Enqueue(() =>
            {
                _surface.Detach("sync");
                Capture("192_cards_single_floating.png");
            });
            ShotActions.Enqueue(() =>
            {
                _surface.Detach("joints");
                _surface.Detach("torque");
                Capture("193_cards_three_floating.png");
            });
            ShotActions.Enqueue(() =>
            {
                _surface.ToggleMaximizeFloating("torque");
                Capture("194_cards_floating_maximized.png");
            });
            ShotActions.Enqueue(() =>
            {
                _surface.ToggleMaximizeFloating("torque");
                _surface.DockBack("torque");
                _surface.DockBack("joints");
                _surface.DockBack("sync");
                Capture("195_cards_docked_back.png");
            });
            _nextAt = Time.realtimeSinceStartup;
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
                Debug.LogWarning("[CARD] screenshot failed: " + e.Message);
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[CARD] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
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

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Card Framework Check (UI/48)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            foreach (var item in Items)
            {
                sb.AppendLine("[CARD] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0 && Items.Count >= 25;
            sb.AppendLine("[CARD] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui48_card_check.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CARD] log write failed: " + e.Message);
            }

            Debug.Log("[CARD] finish result=" + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails);
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

                Debug.Log("[CARD] [logfail] " + type + " " + condition);
            }
        }
    }

    public static class TwinUiVisualAudit
    {
        public const string PendingKey = "TwinUiVisualAudit.Pending";
        public const string LogPath = "Logs/ui_visual_audit.txt";
        private const string EvidenceShots = TwinUiCardStage.EvidenceRoot + @"\shots";
        private const string EvidenceLogs = TwinUiCardStage.EvidenceRoot + @"\logs";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly Queue<string> Queue = new Queue<string>();
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static float _nextAt;
        private static float _startAt;
        private static bool _started;
        private static bool _finished;
        private static bool _captured;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            Queue.Clear();
            _window = null;
            _shell = null;
            _surface = null;
            _started = false;
            _finished = false;
            _captured = false;
            _nextAt = 0f;
            _startAt = 0f;
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
                _nextAt = _startAt + 0.6f;
                _window.ApplyLayout(new Rect(16f, 16f, 1440f, 900f), false, false);
                PreparePageQueue();
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (Queue.Count > 0)
            {
                NextShot();
                return;
            }

            if (!_captured)
            {
                _captured = true;
                AuditTokens();
                AuditTypeScale();
                AuditThemes();
                _nextAt = Time.realtimeSinceStartup + 0.4f;
                return;
            }

            Finish();
        }

        private static void PreparePageQueue()
        {
            Queue.Enqueue("196_page_overview_1440.png|" + TwinUiWorkspaceRegistry.Overview);
            Queue.Enqueue("197_page_twin_1440.png|" + TwinUiWorkspaceRegistry.Twin);
            Queue.Enqueue("198_page_control_1440.png|" + TwinUiWorkspaceRegistry.Control);
            Queue.Enqueue("199_page_mission_1440.png|" + TwinUiWorkspaceRegistry.Mission);
            Queue.Enqueue("200_page_experiment_1440.png|" + TwinUiWorkspaceRegistry.Experiment);
        }

        private static void NextShot()
        {
            string[] parts = Queue.Dequeue().Split('|');
            _surface.SetTab(parts[1]);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                string path = Path.Combine(EvidenceShots, parts[0]);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VISUAL] screenshot failed: " + e.Message);
            }

            _nextAt = Time.realtimeSinceStartup + 0.55f;
        }

        private static void AuditTokens()
        {
            var palette = TwinUITheme.Current;
            Add("theme is ocean", "mode=" + TwinUITheme.Mode, TwinUITheme.Mode == TwinUIThemeMode.Ocean);
            Add("card token", "card=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.CardColor),
                TwinUIStyle.CardColor == palette.Card);
            Add("panel3 token added", "panel3=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.Panel3Color),
                TwinUIStyle.Panel3Color == palette.Panel3);
            Add("quiet token added", "quiet=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.QuietColor),
                TwinUIStyle.QuietColor == palette.Quiet);

            int cards = 0;
            int rounded = 0;
            var images = _window.Content.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null || img.gameObject.name != "CardBg")
                {
                    continue;
                }

                cards++;
                if (img.sprite == TwinUiRadii.CardSprite)
                {
                    rounded++;
                }
            }

            Add("all cards use card radius sprite", "cards=" + cards + " rounded=" + rounded,
                cards >= 3 && rounded == cards);
            Add("card count on page", "cards=" + _surface.Page.CardCount, _surface.Page.CardCount > 0);
        }

        private static void AuditTypeScale()
        {
            var texts = _window.Content.GetComponentsInChildren<TMP_Text>(true);
            int tooSmall = 0;
            int checkedCount = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }

                checkedCount++;
                if (t.fontSize < 10.5f)
                {
                    tooSmall++;
                    Debug.Log("[VISUAL] small text: " + t.name + " " + t.fontSize + " '" + t.text + "'");
                }
            }

            Add("no text below 10.5px", "texts=" + checkedCount + " tooSmall=" + tooSmall, tooSmall == 0);
        }

        private static void AuditThemes()
        {
            int before = TwinUITheme.Reapply();
            Add("theme reapply", "count=" + before, before > 0);
            TwinUITheme.SetMode(TwinUIThemeMode.Light);
            Add("light mode switch", "mode=" + TwinUITheme.Mode, TwinUITheme.Mode == TwinUIThemeMode.Light);
            Add("light card token", "card=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.CardColor),
                TwinUIStyle.CardColor != TwinUIStyle.BgColor);
            TwinUITheme.SetMode(TwinUIThemeMode.Ocean);
            Add("ocean mode restored", "mode=" + TwinUITheme.Mode, TwinUITheme.Mode == TwinUIThemeMode.Ocean);
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[VISUAL] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
        }

        private static void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            SessionState.SetBool(PendingKey, false);
            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Visual & Token Audit (UI/49)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            foreach (var item in Items)
            {
                sb.AppendLine("[VISUAL] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && Items.Count >= 10;
            sb.AppendLine("[VISUAL] check items=" + Items.Count + " fails=" + fails
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui49_visual_audit.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VISUAL] log write failed: " + e.Message);
            }

            Debug.Log("[VISUAL] finish result=" + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            };
        }
    }
}