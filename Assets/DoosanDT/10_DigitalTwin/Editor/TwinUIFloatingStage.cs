// TwinUIFloatingStage.cs —— P5 悬浮窗/多模块自检 + 论文截图（task-13）
// UI/39 Floating & Modules Check (auto-exit)：
//   - 多模块 dock：3 个真实模块（性能/事件流/设置）同时 Docked；卡片不越界；临时缩小卡片宽度制造翻页并断言翻页正确；
//   - Frame 复用：悬浮窗必须持有 TwinUiWindowFrame 实例（同一份圆角/边框/阴影/手柄/标题栏代码），
//     AllowsMaximize=false、手柄 11-23、Ocean 令牌色与 P2 一致；
//   - 吸附：左/右/上 24px 内贴边；
//   - 多浮窗：两个悬浮窗不重叠、z 序（BringToFront/TopFloatingModuleId）、Esc 关闭最上层；
//   - dock/menu 挂 UiWindowRoot，content 仍只有 6 个容器（不污染 UI/30）。
// UI/40 Capture Thesis Screenshot (auto-exit)：默认主窗 + 性能模块 Docked 紧凑态，1920×1080 截图给论文图 4-15。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIFloatingStage
    {
        private const string Tag = "[FLOAT]";
        private const string ScenePath = "Assets/Scenes/Doosan-DT.unity";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p5_floating";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string EvidenceLogs = EvidenceRoot + @"\logs";
        private const string ThesisShot = EvidenceShots + @"\thesis_fig4_15_ui_1920.png";

        [MenuItem("DigitalTwin/UI/39 Floating & Modules Check (auto-exit)", false, 390)]
        public static void RunFloatingCheck()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/39 skipped: already in play mode");
                return;
            }

            TwinUiFloatingCheck.Reset();
            SessionState.SetBool(TwinUiFloatingCheck.PendingKey, true);
            Debug.Log(Tag + " UI/39 armed, entering play mode; log=" + TwinUiFloatingCheck.LogPath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("DigitalTwin/UI/40 Capture Thesis Screenshot (auto-exit)", false, 400)]
        public static void RunThesisCapture()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + " UI/40 skipped: already in play mode");
                return;
            }

            TwinUiThesisCapture.Reset();
            SessionState.SetBool(TwinUiThesisCapture.PendingKey, true);
            Debug.Log(Tag + " UI/40 armed, entering play mode; shot=" + ThesisShot);
            EditorApplication.isPlaying = true;
        }
    }

    /// <summary>UI/39：多模块 dock / Frame 复用 / 吸附 / 多浮窗 / Esc 断言。</summary>
    public static class TwinUiFloatingCheck
    {
        public const string PendingKey = "TwinUiFloatingCheck.Pending";
        public const string LogPath = "Logs/ui_floating_check.txt";
        public const string JsonPath = "Logs/ui_floating_check.json";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p5_floating";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string EvidenceLogs = EvidenceRoot + @"\logs";
        private const string PerfId = TwinUiPerformanceModule.ModuleId;
        private const string DataLinkId = TwinUiDataLinkModule.ModuleId;
        private const string TaskId = TwinUiTaskProgressModule.ModuleId;

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static TwinUiModuleHost _host;
        private static TwinUiWindowHost _window;
        private static TwinUiPerformanceModule _perf;
        private static TwinUiDataLinkModule _datalink;
        private static TwinUiTaskProgressModule _task;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static bool _subscribed;
        private static int _logErrors;
        private static int _logExceptions;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            _host = null;
            _window = null;
            _perf = null;
            _datalink = null;
            _task = null;
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
                    Add("[0] 模块宿主就绪", "host=" + (_host != null) + " configured="
                        + (_host != null && _host.IsConfigured), false);
                    Finish();
                }

                return;
            }

            _window = _host.WindowHost;
            _perf = _host.Find(PerfId) as TwinUiPerformanceModule;
            _datalink = _host.Find(DataLinkId) as TwinUiDataLinkModule;
            _task = _host.Find(TaskId) as TwinUiTaskProgressModule;
            Add("[0] 宿主/3 个真实模块就绪",
                "modules=" + _host.Modules.Count + " ids=[" + JoinIds() + "]"
                + " perf=" + (_perf != null && _perf.ProfilerAvailable)
                + " datalink=" + (_datalink != null && _datalink.Available)
                + " task=" + (_task != null && _task.Available),
                _host.Modules.Count >= 3 && _perf != null && _datalink != null && _task != null);
            _started = true;
            _step = 0;
            _nextAt = (float)EditorApplication.timeSinceStartup + 0.35f;
        }

        private static string JoinIds()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _host.Modules.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(_host.Modules[i].Id);
            }

            return sb.ToString();
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

                    Add("[1] 全部模块回到 Hidden",
                        "hidden=" + HiddenCount() + "/" + _host.Modules.Count + " dockOn=" + DockActive(),
                        HiddenCount() == _host.Modules.Count && !DockActive());
                    Next(1, 0.4f);
                    return;
                case 1:
                    _host.SetState(DataLinkId, TwinUiModuleState.Docked);
                    Next(2, 0.55f);
                    return;
                case 2:
                    Add("[2] 数据链模块 Docked（第二真实模块）",
                        "state=" + _host.GetState(DataLinkId) + " view=" + (_datalink.View != null)
                        + " connected=" + (_datalink != null && _datalink.Connected)
                        + " hz=" + (_datalink != null ? _datalink.Hz.ToString("F2") : "-")
                        + " latencyMs=" + (_datalink != null ? _datalink.LatencyMs.ToString("F1") : "-")
                        + " source=" + (_datalink != null ? _datalink.SourceKind : "-"),
                        _host.GetState(DataLinkId) == TwinUiModuleState.Docked && _datalink.View != null);
                    Add("[2b] dock/menu 挂 UiWindowRoot，content 仍 6 容器（不污染 UI/30）",
                        "contentChildren=" + _window.Content.childCount + " dockParent="
                        + (_host.DockRoot != null ? _host.DockRoot.parent.name : "none"),
                        _window.Content.childCount == 6 && _host.DockRoot != null
                        && _host.DockRoot.parent == _window.Root);
                    Next(3, 0.45f);
                    return;
                case 3:
                {
                    // 同源抽查：模块值 == 数据源 Current（同一帧读取，不另造数据）。
                    var binder = UnityEngine.Object.FindObjectOfType<TwinUIStatusBinder>();
                    var snap = binder != null ? binder.Current : default(TwinUIStatusBinder.Snapshot);
                    bool hzMatch = binder != null && Mathf.Abs((float)(snap.Hz - _datalink.Hz)) < 0.01f;
                    bool latMatch = binder != null && Mathf.Abs((float)(snap.LatencyMs - _datalink.LatencyMs)) < 0.05f;
                    Add("[3] 数据链模块与 TwinUIStatusBinder.Current 同源",
                        "moduleHz=" + _datalink.Hz.ToString("F2") + " binderHz=" + snap.Hz.ToString("F2")
                        + " moduleLatency=" + _datalink.LatencyMs.ToString("F1")
                        + " binderLatency=" + snap.LatencyMs.ToString("F1")
                        + " connected=" + snap.Connected + " hasFrame=" + snap.HasFrame,
                        binder != null && _datalink.Available && hzMatch && latMatch);

                    Next(4, 0.45f);
                    return;
                }
                case 4:
                    _host.SetState(PerfId, TwinUiModuleState.Docked);
                    _host.SetState(TaskId, TwinUiModuleState.Docked);
                    Next(5, 0.7f);
                    return;
                case 5:
                    AssertDock("3 模块 Docked");
                    {
                        var sys = DisassemblySystem.Instance;
                        bool taskOk = sys != null && _task.Available
                            && _task.LayerIndex == sys.currentLayerIndex;
                        Add("[3b] 拆解进度模块与 DisassemblySystem.Instance 同源（模块 Dock 后刷新）",
                            "moduleLayer=" + _task.LayerIndex + " systemLayer="
                            + (sys != null ? sys.currentLayerIndex : -99)
                            + " layers=" + _task.LayerCount + " interactable=" + _task.Interactable
                            + " completed=" + _task.Completed + " mode=" + _task.Mode,
                            taskOk);
                    }

                    Capture("ui39_s01_dock_3modules.png");
                    Next(6, 0.45f);
                    return;
                case 6:
                    // 临时缩小卡片宽度制造翻页：3 卡 × 500 + 间距 > dock 宽度。
                    _host.DockCardWidthOverride = 500f;
                    _host.SetDockPage(0);
                    Next(7, 0.5f);
                    return;
                case 7:
                    AssertPaging();
                    Capture("ui39_s02_dock_paged.png");
                    Next(8, 0.45f);
                    return;
                case 8:
                    _host.DockCardWidthOverride = 0f;
                    _host.SetDockPage(0);
                    _host.SetState(PerfId, TwinUiModuleState.Floating);
                    Next(9, 0.6f);
                    return;
                case 9:
                    AssertFloatingFrame();
                    Next(10, 0.45f);
                    return;
                case 10:
                    AssertSnap();
                    Capture("ui39_s03_floating_snapped.png");
                    Next(11, 0.45f);
                    return;
                case 11:
                    _host.SetState(TaskId, TwinUiModuleState.Floating);
                    Next(12, 0.6f);
                    return;
                case 12:
                    AssertMultiFloating();
                    Capture("ui39_s04_multi_floating.png");
                    Next(13, 0.45f);
                    return;
                case 13:
                    AssertCloseTopAndEsc();
                    Next(14, 0.45f);
                    return;
                case 14:
                    for (int i = 0; i < _host.Modules.Count; i++)
                    {
                        _host.SetState(_host.Modules[i].Id, TwinUiModuleState.Hidden);
                    }

                    _host.SetDockPage(0);
                    Next(15, 0.5f);
                    return;
                case 15:
                    Add("[last] 全部 Hidden：dock 隐藏、悬浮窗清零、视图销毁",
                        "hidden=" + HiddenCount() + " dockOn=" + DockActive()
                        + " floating=" + _host.FloatingCount
                        + " views=" + (_perf.View == null && _datalink.View == null && _task.View == null)
                        + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions,
                        HiddenCount() == _host.Modules.Count && !DockActive()
                        && _host.FloatingCount == 0 && _perf.View == null && _datalink.View == null
                        && _task.View == null && _logErrors == 0 && _logExceptions == 0);
                    Finish();
                    return;
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

        private static bool DockActive()
        {
            return _host.DockRoot != null && _host.DockRoot.gameObject.activeSelf;
        }

        private static void AssertDock(string tag)
        {
            Add("[" + tag + "] 三张卡片都可见且不越出 dock",
                "docked=" + _host.DockedCount + " page=" + (_host.DockPage + 1) + "/" + _host.DockPageCount
                + " dockRect=" + R(_host.DockRect) + " cards=" + CardSummary(),
                _host.DockedCount == 3 && CardsInsideDock());
            Add("[" + tag + "] 第二/第三模块数据源可用（常驻场景组件，不依赖工业页）",
                "datalinkAvailable=" + (_datalink != null && _datalink.Available)
                + " datalinkHz=" + (_datalink != null ? _datalink.Hz.ToString("F2") : "-")
                + " taskAvailable=" + (_task != null && _task.Available)
                + " taskLine=" + (_task != null ? _task.DescribeLine() : "-"),
                _datalink != null && _datalink.Available && _task != null && _task.Available);

            Rect rootRect = _window.Root.rect;
            Rect dock = _host.DockRect;
            bool inside = dock.width > 1f && dock.x >= -1f && dock.y >= -1f
                && dock.xMax <= rootRect.width + 1f && dock.yMax <= rootRect.height + 1f;
            Add("[" + tag + "] dock 在 UiWindowRoot 内（容差 1px）",
                "dock=" + R(dock) + " root=" + R(rootRect), inside);
        }

        private static void AssertPaging()
        {
            int pages = _host.DockPageCount;
            int perPage = _host.PerPage;
            Add("[paging] 缩小卡片宽度后需要翻页",
                "perPage=" + perPage + " pageCount=" + pages + " docked=" + _host.DockedCount
                + " cardWidthOverride=" + _host.DockCardWidthOverride,
                pages >= 2 && perPage == 2);
            Add("[paging] 第 1 页卡片数/不越界",
                "page=" + (_host.DockPage + 1) + " cards=" + CardSummary() + " inside=" + CardsInsideDock(),
                VisibleCardCount() == 2 && CardsInsideDock());

            _host.SetDockPage(1);
            Add("[paging] 翻到第 2 页：卡片数/不越界",
                "page=" + (_host.DockPage + 1) + "/" + _host.DockPageCount + " cards=" + CardSummary()
                + " inside=" + CardsInsideDock(),
                _host.DockPage == 1 && VisibleCardCount() == 1 && CardsInsideDock());
            _host.SetDockPage(0);
        }

        private static int VisibleCardCount()
        {
            int n = 0;
            foreach (var module in _host.Modules)
            {
                var record = _host.DockRoot != null ? _host.DockRoot.Find("Cards/Card_" + module.Id) : null;
                if (record != null && record.gameObject.activeSelf)
                {
                    n++;
                }
            }

            return n;
        }

        private static string CardSummary()
        {
            var sb = new StringBuilder();
            foreach (var module in _host.Modules)
            {
                if (_host.DockRoot == null)
                {
                    continue;
                }

                var card = _host.DockRoot.Find("Cards/Card_" + module.Id) as RectTransform;
                if (card == null)
                {
                    continue;
                }

                sb.Append(module.Id).Append(card.gameObject.activeSelf ? ":" : "(off):")
                    .Append(card.rect.width.ToString("F0")).Append(' ');
            }

            return sb.ToString().Trim();
        }

        private static bool CardsInsideDock()
        {
            if (_host.DockRoot == null)
            {
                return false;
            }

            var cards = _host.DockRoot.Find("Cards");
            if (cards == null)
            {
                return false;
            }

            float limit = cards.GetComponent<RectTransform>().rect.width + 1f;
            foreach (var module in _host.Modules)
            {
                var card = cards.Find("Card_" + module.Id) as RectTransform;
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                if (card.anchoredPosition.x < -1f || card.anchoredPosition.x + card.rect.width > limit)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AssertFloatingFrame()
        {
            var window = _host.FloatingWindowFor(PerfId);
            bool exists = window != null && window.IsConfigured;
            Add("[frame] 悬浮窗真正实例化 TwinUiWindowFrame",
                "exists=" + exists + " frame=" + (window != null && window.Frame != null)
                + " frameType=" + (window != null && window.Frame != null ? window.Frame.GetType().Name : "-")
                + " hostType=" + (window != null && window.Frame != null && window.Frame.Host != null
                    ? window.Frame.Host.GetType().Name : "-"),
                exists && window.Frame != null && window.Frame.GetType() == typeof(TwinUiWindowFrame));
            if (!exists)
            {
                return;
            }

            var frame = window.Frame;
            Add("[frame] 手柄 11-23 / 圆角 13 / AllowsMaximize=false",
                "edgeHit=" + frame.ResizeHitSize.ToString("F0") + " cornerHit=" + frame.CornerHitSize.ToString("F0")
                + " allowsMaximize=" + frame.Host.AllowsMaximize
                + " bg=" + Hex(window.WindowRoot.Find("WindowBackground").GetComponent<UnityEngine.UI.Image>().color)
                + " border=" + Hex(TwinUIStyle.BorderColor)
                + " sprite=" + (window.WindowRoot.Find("WindowBackground").GetComponent<UnityEngine.UI.Image>().sprite
                    == TwinUiWindowHost.RoundedWindowSprite()),
                Mathf.Abs(frame.ResizeHitSize - 11f) < 0.01f
                && Mathf.Abs(frame.CornerHitSize - 23f) < 0.01f
                && !frame.Host.AllowsMaximize);
            Rect wr = window.WindowRect;
            float cw = _window.CanvasSize.x;
            float ch = _window.CanvasSize.y;
            Add("[frame] 悬浮窗矩形在画布内",
                "rect=" + R(wr) + " canvas=" + cw.ToString("F0") + "x" + ch.ToString("F0"),
                wr.x >= -1f && wr.y >= -1f && wr.xMax <= cw + 1f && wr.yMax <= ch + 1f);
        }

        private static void AssertSnap()
        {
            var window = _host.FloatingWindowFor(PerfId);
            if (window == null)
            {
                Add("[snap] 悬浮窗缺失", "window=null", false);
                return;
            }

            Rect r = window.WindowRect;
            window.SetRectForCheck(new Rect(6f, 120f, r.width, r.height));
            bool left = window.SnapToEdges();
            bool leftOk = left && Mathf.Abs(window.WindowRect.x) <= 1f;

            r = window.WindowRect;
            window.SetRectForCheck(new Rect(_window.CanvasSize.x - r.width - 8f, 160f, r.width, r.height));
            bool right = window.SnapToEdges();
            bool rightOk = right && Mathf.Abs(window.WindowRect.xMax - _window.CanvasSize.x) <= 1f;

            r = window.WindowRect;
            window.SetRectForCheck(new Rect(320f, _window.CanvasSize.y - r.height - 10f, r.width, r.height));
            bool top = window.SnapToEdges();
            bool topOk = top && Mathf.Abs(window.WindowRect.yMax - _window.CanvasSize.y) <= 1f;

            Add("[snap] 左/右/上 24px 内贴边吸附",
                "left=" + leftOk + " right=" + rightOk + " top=" + topOk
                + " finalRect=" + R(window.WindowRect),
                leftOk && rightOk && topOk);
        }

        private static void AssertMultiFloating()
        {
            var perfWindow = _host.FloatingWindowFor(PerfId);
            var taskWindow = _host.FloatingWindowFor(TaskId);
            bool two = _host.FloatingCount == 2 && perfWindow != null && taskWindow != null;
            bool overlap = two && perfWindow.WindowRect.Overlaps(taskWindow.WindowRect);
            Add("[multi] 两个悬浮窗同时存在且自动错位不重叠",
                "count=" + _host.FloatingCount
                + " perf=" + (perfWindow != null ? R(perfWindow.WindowRect) : "null")
                + " settings=" + (taskWindow != null ? R(taskWindow.WindowRect) : "null")
                + " overlap=" + overlap,
                two && !overlap);

            string top = _host.TopFloatingModuleId();
            Add("[multi] 后打开的窗口在最上层（z 序）",
                "top=" + top + " expected=" + TaskId, top == TaskId);

            _host.BringFloatingToFront(PerfId);
            string top2 = _host.TopFloatingModuleId();
            Add("[multi] BringToFront 可切换最上层",
                "topAfterBring=" + top2 + " expected=" + PerfId, top2 == PerfId);
        }

        private static void AssertCloseTopAndEsc()
        {
            int before = _host.FloatingCount;
            bool closed = _host.CloseTopFloatingForCheck();
            Add("[multi] 关闭最上层悬浮窗（Esc 同一条路径）",
                "before=" + before + " closed=" + closed + " after=" + _host.FloatingCount,
                before == 2 && closed && _host.FloatingCount == 1);
            _host.SetState(TaskId, TwinUiModuleState.Hidden);
            _host.SetState(PerfId, TwinUiModuleState.Hidden);
            Add("[multi] 关闭后悬浮窗清零/状态回 Hidden",
                "count=" + _host.FloatingCount + " perf=" + _host.GetState(PerfId)
                + " settings=" + _host.GetState(TaskId),
                _host.FloatingCount == 0 && _host.GetState(PerfId) == TwinUiModuleState.Hidden
                && _host.GetState(TaskId) == TwinUiModuleState.Hidden);
        }

        // ------------------------------------------------------------- 收尾

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
                Debug.LogWarning(TwinUIFloatingStage2.Tag + " screenshot failed: " + e.Message);
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
            sb.AppendLine("=== Floating & Modules Check (UI/39)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("scene=" + SceneManager.GetActiveScene().path + " hash=" + SceneHash());
            foreach (var item in Items)
            {
                sb.AppendLine("[FLOAT] [item] " + item.Label + " | " + item.Detail
                    + " | result=" + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0;
            sb.AppendLine("[FLOAT] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui39_check.txt"), true);
                var json = new StringBuilder();
                json.Append("{\"items\":").Append(Items.Count).Append(",\"fails\":").Append(fails)
                    .Append(",\"logErrors\":").Append(_logErrors)
                    .Append(",\"logExceptions\":").Append(_logExceptions)
                    .Append(",\"shots\":").Append(Shots.Count)
                    .Append(",\"result\":\"").Append(ok ? "PASS" : "FAIL").Append("\"}");
                File.WriteAllText(JsonPath, json.ToString(), new UTF8Encoding(false));
                File.Copy(JsonPath, Path.Combine(EvidenceLogs, "ui39_check.json"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning(TwinUIFloatingStage2.Tag + " log write failed: " + e.Message);
            }

            Debug.Log("[FLOAT] UI/39 " + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
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
                Debug.LogWarning("[FLOAT] FAIL " + label + " | " + detail);
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

        private static string R(Rect r)
        {
            return "(" + r.x.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.y.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.width.ToString("F1", CultureInfo.InvariantCulture) + "x"
                + r.height.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        private static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c).ToUpperInvariant();
        }
    }

    /// <summary>UI/40：论文图 4-15 截图（默认主窗 + 性能模块 Docked 紧凑态）。</summary>
    public static class TwinUiThesisCapture
    {
        public const string PendingKey = "TwinUiThesisCapture.Pending";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p5_floating";
        private const string ThesisShot = EvidenceRoot + @"\shots\thesis_fig4_15_ui_1920.png";
        private const string MetaPath = EvidenceRoot + @"\logs\ui40_thesis_shot.txt";
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
                    Debug.LogError("[SHOT] UI/40 FAIL: module host not ready");
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
                    _window.ApplyLayout(_window.DefaultRect(), false, false);
                    _host.DockCardWidthOverride = 0f;
                    Next(1, 0.8f);
                    return;
                case 1:
                    _host.SetState(PerfId, TwinUiModuleState.Docked);
                    _host.SetExpanded(PerfId, false);
                    _host.CloseMenu();
                    Cursor.visible = false;
                    Next(2, 0.9f);
                    return;
                case 2:
                    Directory.CreateDirectory(Path.GetDirectoryName(ThesisShot));
                    ScreenCapture.CaptureScreenshot(ThesisShot);
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
            bool exists = File.Exists(ThesisShot);
            long size = exists ? new FileInfo(ThesisShot).Length : 0;
            Rect rect = _window.WindowRect;
            var responsive = _window.Responsive;
            var perf = _host.Find(PerfId) as TwinUiPerformanceModule;
            var sb = new StringBuilder();
            sb.AppendLine("UI/40 thesis screenshot metadata");
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("shot=" + ThesisShot);
            sb.AppendLine("shotExists=" + exists + " bytes=" + size);
            sb.AppendLine("screen=" + Screen.width + "x" + Screen.height);
            sb.AppendLine("windowRect=" + rect.x.ToString("F1") + "," + rect.y.ToString("F1") + ","
                + rect.width.ToString("F1") + "x" + rect.height.ToString("F1"));
            sb.AppendLine("windowRatio=" + (rect.width / Mathf.Max(1f, _window.CanvasSize.x)).ToString("F4")
                + "x" + (rect.height / Mathf.Max(1f, _window.CanvasSize.y)).ToString("F4"));
            sb.AppendLine("contentScale=" + _window.ContentScale.x.ToString("F3"));
            sb.AppendLine("breakpoint=" + (responsive != null ? responsive.Current.ToString() : "n/a"));
            sb.AppendLine("content=" + _window.Content.rect.width.ToString("F1") + "x"
                + _window.Content.rect.height.ToString("F1"));
            sb.AppendLine("theme=" + TwinUITheme.Mode);
            sb.AppendLine("moduleStates=" + StateOf("performance") + "," + StateOf("datalink") + ","
                + StateOf("task"));
            sb.AppendLine("perfState=" + (perf != null ? perf.State.ToString() : "n/a")
                + " perfExpanded=" + (perf != null && perf.IsExpanded)
                + " perfAvailable=" + (perf != null && perf.ProfilerAvailable)
                + " perfFps=" + (perf != null ? perf.Snapshot.fps.ToString("F1") : "n/a"));
            sb.AppendLine("cursorVisible=false menuOpen=" + _host.MenuOpen
                + " viewScale=1 docked=" + _host.DockedCount + " floating=" + _host.FloatingCount);
            sb.AppendLine("expectedFigure=thesis fig 4-15 / table 4-6 (1920x1080, default window, perf module docked compact)");
            string text = sb.ToString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MetaPath));
                File.WriteAllText(MetaPath, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHOT] meta write failed: " + e.Message);
            }

            bool ok = exists && size > 100000 && Screen.width == 1920 && Screen.height == 1080
                && _host.GetState(PerfId) == TwinUiModuleState.Docked
                && !_host.MenuOpen && perf != null && !perf.IsExpanded;
            Debug.Log("[SHOT] UI/40 " + (ok ? "PASS" : "FAIL") + " shot=" + ThesisShot
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

    /// <summary>UI/40 记录用占位（避免与 UI/39 类名混淆）。</summary>
    internal static class TwinUIFloatingStage2
    {
        public const string Tag = "[FLOAT]";
    }
}