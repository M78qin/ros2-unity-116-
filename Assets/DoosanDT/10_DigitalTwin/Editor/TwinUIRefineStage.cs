// TwinUIRefineStage.cs —— P6 精修自检（task-15）：UI/41 跨窗拖拽停靠 + UI/44 Wide 备用图
// UI/41 Dock-Drag Check (auto-exit)：
//   ①悬浮窗拖到主窗上 → 停靠预览出现 → 松手变 Docked 且 dock 卡片可见；
//   ②dock 卡片拖出主窗 → Floating（≥480×330）且 rect 在画布内；③拖回 → Docked；
//   ④拖动期间走 TwinUiInputArbiter（WindowDrag），UI 射线语义与 UI/17 一致；⑤totalErrors=0；4 张截图；自动退 Play。
// UI/44 Capture Wide Screenshot (auto-exit)：窗口 1600 宽（内容 1598 → Wide 档）+ 性能模块 Docked 紧凑，
//   出 shots/thesis_fig4_15_ui_wide_1600.png（仅备用，不替换论文现图）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIRefineStage
    {
        private const string Tag = "[DOCK]";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string WideShot = EvidenceShots + @"\thesis_fig4_15_ui_wide_1600.png";

        [MenuItem("DigitalTwin/UI/41 Dock-Drag Check (auto-exit)", false, 410)]
        public static void RunDockCheck()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/41 skipped: already in play mode");
                return;
            }

            TwinUiDockCheck.Reset();
            SessionState.SetBool(TwinUiDockCheck.PendingKey, true);
            Debug.Log(Tag + " UI/41 armed, entering play mode; log=" + TwinUiDockCheck.LogPath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("DigitalTwin/UI/44 Capture Wide Screenshot (auto-exit)", false, 440)]
        public static void RunWideCapture()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/44 skipped: already in play mode");
                return;
            }

            TwinUiWideCapture.Reset();
            SessionState.SetBool(TwinUiWideCapture.PendingKey, true);
            Debug.Log(Tag + " UI/44 armed, entering play mode; shot=" + WideShot);
            EditorApplication.isPlaying = true;
        }
    }

    /// <summary>UI/41：跨窗拖拽停靠断言（复用真实 Frame 拖拽 API 与 TwinUiInput 仲裁）。</summary>
    public static class TwinUiDockCheck
    {
        public const string PendingKey = "TwinUiDockCheck.Pending";
        public const string LogPath = "Logs/ui_dock_check.txt";
        public const string JsonPath = "Logs/ui_dock_check.json";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string EvidenceLogs = EvidenceRoot + @"\logs";
        private const string PerfId = TwinUiPerformanceModule.ModuleId;

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
        private static TwinUiModuleHost _host;
        private static TwinUiWindowHost _window;
        private static TwinUiPerformanceModule _perf;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static bool _subscribed;
        private static int _logErrors;
        private static int _logExceptions;
        private static Rect _outsideRect;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            _host = null;
            _window = null;
            _perf = null;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _subscribed = false;
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
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (!_started)
            {
                Start();
                return;
            }

            if (_finished || EditorApplication.timeSinceStartup < _nextAt)
            {
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Add("[x] 自检异常", e.GetType().Name + ": " + e.Message, false);
                Finish();
            }
        }

        private static void Start()
        {
            if (_startAt <= 0f)
            {
                _startAt = Time.realtimeSinceStartup;
            }

            if (!_subscribed)
            {
                Application.logMessageReceived += OnLog;
                _subscribed = true;
            }

            _host = TwinUiModuleHost.Instance;
            if (_host == null)
            {
                _host = UnityEngine.Object.FindObjectOfType<TwinUiModuleHost>();
            }

            if (_host == null || !_host.IsConfigured)
            {
                _nextAt = (float)EditorApplication.timeSinceStartup + 0.25f;
                if (Time.realtimeSinceStartup - _startAt > 30f)
                {
                    Add("[0] 模块宿主就绪", "host=" + (_host != null), false);
                    Finish();
                }

                return;
            }

            _window = _host.WindowHost;
            _perf = _host.Find(PerfId) as TwinUiPerformanceModule;
            Add("[0] 宿主/性能模块就绪",
                "host=True modules=" + _host.Modules.Count + " perf=" + (_perf != null)
                + " window=" + DescribeRect(_window.WindowRect) + " canvas=" + _window.CanvasSize.x + "x"
                + _window.CanvasSize.y,
                _perf != null && _window != null);
            _started = true;
            _step = 0;
            _nextAt = (float)EditorApplication.timeSinceStartup + 0.4f;
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0:
                    for (int i = 0; i < _host.Modules.Count; i++)
                    {
                        _host.SetState(_host.Modules[i].Id, TwinUiModuleState.Hidden);
                    }

                    _host.SetState(PerfId, TwinUiModuleState.Docked);
                    Next(1, 0.7f);
                    return;
                case 1:
                    _host.SetState(PerfId, TwinUiModuleState.Floating);
                    Next(2, 0.6f);
                    return;
                case 2:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    _outsideRect = new Rect(60f, 140f, 520f, 364f);
                    window.SetRectForCheck(_outsideRect);
                    _outsideRect = window.WindowRect;
                    Add("[1] 悬浮窗先放到主窗左侧空白区（不触发停靠）",
                        "rect=" + DescribeRect(_outsideRect) + " overMain=" + window.OverMainWindow
                        + " hint=" + _host.DockPreviewActive,
                        !window.OverMainWindow && !_host.DockPreviewActive);
                    Next(3, 0.55f);
                    return;
                }
                case 3:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    var frame = window.Frame;
                    var band = frame.TitleBand.center;
                    bool began = frame.TryBeginDragAt(band);
                    Add("[2] 用真实 Frame 拖拽路径开始拖（TwinUiInput.WindowDrag）",
                        "began=" + began + " owner=" + TwinUiInput.CurrentOwner + " state=" + _host.GetState(PerfId)
                        + " bandCenter=" + band.x.ToString("F1") + "," + band.y.ToString("F1"),
                        began && TwinUiInput.CurrentOwner == TwinUiInputOwner.WindowDrag
                        && _host.GetState(PerfId) == TwinUiModuleState.Floating);
                    Capture("ui41_s01_floating_outside.png");
                    Next(4, 0.45f);
                    return;
                }
                case 4:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    var frame = window.Frame;
                    Vector2 target = _window.WindowRect.center;
                    Vector2 from = window.WindowRect.center;
                    frame.DragBy(target - from);
                    Add("[3] 拖到主窗上 → 停靠预览出现（仍为 Floating）",
                        "rect=" + DescribeRect(window.WindowRect) + " overMain=" + window.OverMainWindow
                        + " preview=" + _host.DockPreviewActive + " hintCount=" + _host.DockHintCount
                        + " state=" + _host.GetState(PerfId),
                        window.OverMainWindow && _host.DockPreviewActive
                        && _host.GetState(PerfId) == TwinUiModuleState.Floating);
                    Capture("ui41_s02_dock_preview.png");
                    Next(5, 0.45f);
                    return;
                }
                case 5:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    window.Frame.EndDragForTest();
                    Next(6, 0.7f);
                    return;
                }
                case 6:
                {
                    var card = DockCard(PerfId);
                    Add("[4] 松手 → 变 Docked，dock 卡片可见，预览关闭",
                        "state=" + _host.GetState(PerfId) + " floating=" + _host.FloatingCount
                        + " cardActive=" + (card != null && card.gameObject.activeSelf)
                        + " preview=" + _host.DockPreviewActive + " owner=" + TwinUiInput.CurrentOwner
                        + " drops=" + _host.DockDropCount,
                        _host.GetState(PerfId) == TwinUiModuleState.Docked && _host.FloatingCount == 0
                        && card != null && card.gameObject.activeSelf && !_host.DockPreviewActive
                        && TwinUiInput.CurrentOwner == TwinUiInputOwner.None);
                    Capture("ui41_s03_docked_back.png");
                    Next(7, 0.45f);
                    return;
                }
                case 7:
                {
                    var drag = DockDragComponent(PerfId);
                    bool began = drag != null && drag.BeginDragForCheck();
                    _host.UpdateDockCardDrag(new Vector2(-500f, 80f));
                    Add("[5] dock 卡片拖出：拖拽幽灵显示、仲裁归 WindowDrag、落点主窗外无预览",
                        "began=" + began + " ghost=" + _host.DockDragGhostActive
                        + " owner=" + TwinUiInput.CurrentOwner
                        + " insideMain=" + _host.LastDockDropInsideMain
                        + " preview=" + _host.DockPreviewActive,
                        began && _host.DockDragGhostActive
                        && TwinUiInput.CurrentOwner == TwinUiInputOwner.WindowDrag
                        && !_host.LastDockDropInsideMain && !_host.DockPreviewActive);
                    Capture("ui41_s04_card_drag_out.png");
                    Next(8, 0.45f);
                    return;
                }
                case 8:
                {
                    var drag = DockDragComponent(PerfId);
                    drag.EndDragForCheck();
                    Next(9, 0.75f);
                    return;
                }
                case 9:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    Rect r = window != null ? window.WindowRect : Rect.zero;
                    float cw = _window.CanvasSize.x;
                    float ch = _window.CanvasSize.y;
                    bool inCanvas = window != null && r.x >= -1f && r.y >= -1f && r.xMax <= cw + 1f
                        && r.yMax <= ch + 1f;
                    Add("[6] 卡片拖出主窗松手 → Floating（≥480×330）且 rect 在画布内",
                        "state=" + _host.GetState(PerfId) + " floating=" + _host.FloatingCount
                        + " rect=" + DescribeRect(r) + " min=" + TwinUiModuleWindow.DragOutMinWidth + "x"
                        + TwinUiModuleWindow.DragOutMinHeight + " inCanvas=" + inCanvas
                        + " ghost=" + _host.DockDragGhostActive,
                        _host.GetState(PerfId) == TwinUiModuleState.Floating
                        && _host.FloatingCount == 1 && r.width >= TwinUiModuleWindow.DragOutMinWidth - 0.5f
                        && r.height >= TwinUiModuleWindow.DragOutMinHeight - 0.5f && inCanvas
                        && !_host.DockDragGhostActive);
                    Capture("ui41_s05_card_float_out.png");
                    Next(10, 0.45f);
                    return;
                }
                case 10:
                {
                    var window = _host.FloatingWindowFor(PerfId);
                    var frame = window.Frame;
                    bool began = frame.TryBeginDragAt(frame.TitleBand.center);
                    Vector2 delta = _window.WindowRect.center - window.WindowRect.center;
                    frame.DragBy(delta);
                    bool preview = _host.DockPreviewActive;
                    frame.EndDragForTest();
                    Add("[7] 拖回主窗 → Docked（第二次停靠）",
                        "began=" + began + " delta=" + delta.x.ToString("F0") + "," + delta.y.ToString("F0")
                        + " previewDuringDrag=" + preview + " stateAfter=" + _host.GetState(PerfId)
                        + " drops=" + _host.DockDropCount,
                        began && preview && _host.GetState(PerfId) == TwinUiModuleState.Docked
                        && _host.FloatingCount == 0 && _host.DockDropCount >= 2);
                    Next(11, 0.6f);
                    return;
                }
                case 11:
                {
                    bool inside = TryHit(_window.Content, new Vector2(_window.Content.rect.width * 0.5f,
                        -_window.Content.rect.height * 0.5f), out int insideHits);
                    bool outside = TryHit(_window.CanvasRect, new Vector2(
                        _window.WindowRect.x - 100f - _window.CanvasSize.x * 0.5f, 0f), out int outsideHits);
                    Add("[8] UI 射线语义（与 UI/17 一致）：窗内 hit>0、窗外 hit=0",
                        "insideHits=" + insideHits + " outsideHits=" + outsideHits
                        + " isPointerOverGameObject=" + EventSystem.current.IsPointerOverGameObject()
                        + " owner=" + TwinUiInput.CurrentOwner,
                        inside && insideHits > 0 && outside && outsideHits == 0
                        && TwinUiInput.CurrentOwner == TwinUiInputOwner.None);
                    Next(12, 0.45f);
                    return;
                }
                case 12:
                {
                    for (int i = 0; i < _host.Modules.Count; i++)
                    {
                        _host.SetState(_host.Modules[i].Id, TwinUiModuleState.Hidden);
                    }

                    Add("[9] 清理：全 Hidden、无预览/幽灵/悬浮窗",
                        "hidden=" + HiddenCount() + " preview=" + _host.DockPreviewActive
                        + " ghost=" + _host.DockDragGhostActive + " floating=" + _host.FloatingCount
                        + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions,
                        HiddenCount() == _host.Modules.Count && !_host.DockPreviewActive
                        && !_host.DockDragGhostActive && _host.FloatingCount == 0
                        && _logErrors == 0 && _logExceptions == 0);
                    Finish();
                    return;
                }
            }
        }

        private static void Next(int step, float delay)
        {
            _step = step;
            _nextAt = (float)EditorApplication.timeSinceStartup + delay;
        }

        private static int HiddenCount()
        {
            int n = 0;
            for (int i = 0; i < _host.Modules.Count; i++)
            {
                if (_host.Modules[i].State == TwinUiModuleState.Hidden)
                {
                    n++;
                }
            }

            return n;
        }

        private static RectTransform DockCard(string id)
        {
            if (_host.DockRoot == null)
            {
                return null;
            }

            return _host.DockRoot.Find("Cards/Card_" + id) as RectTransform;
        }

        private static TwinUiDockCardDrag DockDragComponent(string id)
        {
            var card = DockCard(id);
            return card != null ? card.GetComponent<TwinUiDockCardDrag>() : null;
        }

        private static bool TryHit(RectTransform space, Vector2 local, out int hits)
        {
            hits = -1;
            if (space == null || EventSystem.current == null)
            {
                return false;
            }

            Vector3 world = space.TransformPoint(new Vector3(local.x, local.y, 0f));
            Camera cam = _window.Canvas != null ? _window.Canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            var ped = new PointerEventData(EventSystem.current) { position = screen };
            Hits.Clear();
            EventSystem.current.RaycastAll(ped, Hits);
            hits = Hits.Count;
            return true;
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
                Debug.LogWarning(TwinUIRefineStage2.Tag + " screenshot failed: " + e.Message);
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

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Dock-Drag Check (UI/41)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("scene=" + SceneManager.GetActiveScene().path + " hash=" + SceneHash());
            foreach (var item in Items)
            {
                sb.AppendLine("[DOCK] [item] " + item.Label + " | " + item.Detail
                    + " | result=" + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0;
            sb.AppendLine("[DOCK] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui41_check.txt"), true);
                var json = new StringBuilder();
                json.Append("{\"items\":").Append(Items.Count).Append(",\"fails\":").Append(fails)
                    .Append(",\"logErrors\":").Append(_logErrors)
                    .Append(",\"logExceptions\":").Append(_logExceptions)
                    .Append(",\"shots\":").Append(Shots.Count)
                    .Append(",\"result\":\"").Append(ok ? "PASS" : "FAIL").Append("\"}");
                File.WriteAllText(JsonPath, json.ToString(), new UTF8Encoding(false));
                File.Copy(JsonPath, Path.Combine(EvidenceLogs, "ui41_check.json"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning(TwinUIRefineStage2.Tag + " log write failed: " + e.Message);
            }

            Debug.Log("[DOCK] UI/41 " + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails + " log=" + LogPath);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            if (!ok)
            {
                Debug.LogWarning("[DOCK] FAIL " + label + " | " + detail);
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _logErrors++;
            }
            else if (type == LogType.Exception)
            {
                _logExceptions++;
            }
        }

        private static string SceneHash()
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(SceneManager.GetActiveScene().path))
                {
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
                }
            }
            catch (Exception e)
            {
                return "ERR:" + e.Message;
            }
        }

        private static string DescribeRect(Rect r)
        {
            return "(" + r.x.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.y.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.width.ToString("F1", CultureInfo.InvariantCulture) + "x"
                + r.height.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>UI/44：Wide 档备用截图（1600 宽；仅备用，不替换论文现图）。</summary>
    public static class TwinUiWideCapture
    {
        public const string PendingKey = "TwinUiWideCapture.Pending";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p6_refine";
        private const string ShotPath = EvidenceRoot + @"\shots\thesis_fig4_15_ui_wide_1600.png";
        private const string MetaPath = EvidenceRoot + @"\logs\ui44_wide_shot.txt";
        private const string PerfId = TwinUiPerformanceModule.ModuleId;

        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static TwinUiModuleHost _host;
        private static TwinUiWindowHost _window;

        public static void Reset()
        {
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _host = null;
            _window = null;
        }

        [InitializeOnLoadMethod]
        private static void HookUpdate()
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

            if (!_started)
            {
                Start();
                return;
            }

            if (_finished || EditorApplication.timeSinceStartup < _nextAt)
            {
                return;
            }

            Step();
        }

        private static void Start()
        {
            if (_startAt <= 0f)
            {
                _startAt = Time.realtimeSinceStartup;
            }

            _host = TwinUiModuleHost.Instance;
            if (_host == null || !_host.IsConfigured)
            {
                _nextAt = (float)EditorApplication.timeSinceStartup + 0.25f;
                if (Time.realtimeSinceStartup - _startAt > 30f)
                {
                    Debug.LogError("[SHOT] UI/44 FAIL: module host not ready");
                    Finish(false, "host not ready");
                }

                return;
            }

            _window = _host.WindowHost;
            _started = true;
            _step = 0;
            _nextAt = (float)EditorApplication.timeSinceStartup + 0.5f;
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0:
                    for (int i = 0; i < _host.Modules.Count; i++)
                    {
                        _host.SetState(_host.Modules[i].Id, TwinUiModuleState.Hidden);
                    }

                    _host.CloseMenu();
                    _host.DockCardWidthOverride = 0f;
                    _window.SetWidthForCheck(1600f);
                    Next(1, 0.9f);
                    return;
                case 1:
                    _host.SetState(PerfId, TwinUiModuleState.Docked);
                    _host.SetExpanded(PerfId, false);
                    _host.CloseMenu();
                    Cursor.visible = false;
                    Next(2, 0.9f);
                    return;
                case 2:
                    Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
                    ScreenCapture.CaptureScreenshot(ShotPath);
                    Next(3, 0.8f);
                    return;
                case 3:
                    WriteMetaAndFinish();
                    return;
            }
        }

        private static void Next(int step, float delay)
        {
            _step = step;
            _nextAt = (float)EditorApplication.timeSinceStartup + delay;
        }

        private static void WriteMetaAndFinish()
        {
            bool exists = File.Exists(ShotPath);
            long size = exists ? new FileInfo(ShotPath).Length : 0;
            Rect rect = _window.WindowRect;
            var responsive = _window.Responsive;
            var sb = new StringBuilder();
            sb.AppendLine("UI/44 wide screenshot metadata（备用图，不替换论文现图）");
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("shot=" + ShotPath);
            sb.AppendLine("shotExists=" + exists + " bytes=" + size);
            sb.AppendLine("screen=" + Screen.width + "x" + Screen.height);
            sb.AppendLine("windowRect=" + rect.x.ToString("F1") + "," + rect.y.ToString("F1") + ","
                + rect.width.ToString("F1") + "x" + rect.height.ToString("F1"));
            sb.AppendLine("content=" + _window.Content.rect.width.ToString("F1") + "x"
                + _window.Content.rect.height.ToString("F1"));
            sb.AppendLine("contentScale=" + _window.ContentScale.x.ToString("F3"));
            sb.AppendLine("breakpoint=" + (responsive != null ? responsive.Current.ToString() : "n/a"));
            sb.AppendLine("theme=" + TwinUITheme.Mode);
            sb.AppendLine("moduleStates=" + StateOf("performance") + "," + StateOf("datalink") + ","
                + StateOf("task"));
            sb.AppendLine("perfExpanded=" + "false" + " cursorVisible=false menuOpen=" + _host.MenuOpen);
            string text = sb.ToString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MetaPath));
                File.WriteAllText(MetaPath, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHOT] UI/44 meta write failed: " + e.Message);
            }

            bool wide = responsive != null && responsive.Current == TwinUiBreakpoint.Wide;
            bool ok = exists && size > 100000 && Screen.width == 1920 && Screen.height == 1080 && wide
                && _host.GetState(PerfId) == TwinUiModuleState.Docked && !_host.MenuOpen;
            Debug.Log("[SHOT] UI/44 " + (ok ? "PASS" : "FAIL") + " shot=" + ShotPath
                + " bytes=" + size + " screen=" + Screen.width + "x" + Screen.height
                + " windowRect=" + rect.x.ToString("F1") + "," + rect.y.ToString("F1") + ","
                + rect.width.ToString("F1") + "x" + rect.height.ToString("F1")
                + " breakpoint=" + (responsive != null ? responsive.Current.ToString() : "n/a")
                + " perfState=" + _host.GetState(PerfId));
            Finish(ok, text);
        }

        private static string StateOf(string id)
        {
            return id + "=" + _host.GetState(id);
        }

        private static void Finish(bool ok, string detail)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            SessionState.SetBool(PendingKey, false);
            Cursor.visible = true;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }
    }

    internal static class TwinUIRefineStage2
    {
        public const string Tag = "[DOCK]";
    }
}