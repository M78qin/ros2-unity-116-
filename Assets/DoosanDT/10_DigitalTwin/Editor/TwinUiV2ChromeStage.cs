// TwinUiV2ChromeStage.cs —— P1-P4 V2 工作台自检（UI/64，auto-exit）
// 覆盖：启动默认全停靠、切页不弹窗、单卡图钉、单实例、主窗尺寸独立、最大化独立、控件 28×28 不重叠、
//       顶栏三行边界/中心线、卡片铺满列/无 localScale、无裁切、数据刷新不停。
// 原则：只读业务对象，不新增订阅；测试前快照 PlayerPrefs，测试后恢复。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiV2ChromeStage
    {
        public const string PendingKey = "TwinUiV2Chrome.Pending";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-17_ui_fix";

        [MenuItem("DigitalTwin/UI/64 V2 Card Chrome + Float/Dock Check (auto-exit)", false, 640)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[CHROME64] skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                TwinUiShellMode.Set(TwinUiShellKind.V2);
            }

            TwinUiV2ChromeCheck.Reset();
            SessionState.SetBool(PendingKey, true);
            Debug.Log("[CHROME64] armed; log=" + TwinUiV2ChromeCheck.LogPath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("DigitalTwin/UI/64b Restore Last Floating Layout (explicit)", false, 641)]
        public static void RestoreFloating()
        {
            var surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            if (surface == null)
            {
                Debug.LogWarning("[CHROME64] no surface for restore");
                return;
            }

            int n = surface.RestoreLastFloatingLayout();
            Debug.Log("[CHROME64] explicit restore floating=" + n);
        }
    }

    public static class TwinUiV2ChromeCheck
    {
        public const string LogPath = "Logs/ui64_chrome_float.txt";
        public const string JsonPath = "Logs/ui64_chrome_float.json";
        private const string EvidenceLogs = TwinUiV2ChromeStage.EvidenceRoot + @"\logs";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly int[] Widths = { 1920, 1600, 1280, 900, 720 };
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static int _widthIndex;
        private static int _tabIndex;
        private static int _floatTabIndex;
        private static Rect _floatRectAtTabStart;
        private static int _floatViewInstanceAtTabStart;
        private static int _floatAttachAtTabStart;
        private static int _refreshAtFloatTabStart;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static int _logErrors;
        private static int _logExceptions;
        private static int _refreshAtStart;
        private static int _viewInstance = -1;
        private static int _attachCountAtStart;
        private static Rect _floatRectBeforeResize;
        private static Rect _origWindowRect;
        private static string _prefShell;
        private static bool _prefHadShell;
        private static string _prefWindow;
        private static bool _prefHadWindow;

        public static void Reset()
        {
            Items.Clear();
            _window = null;
            _shell = null;
            _surface = null;
            _widthIndex = 0;
            _tabIndex = 0;
            _floatTabIndex = 0;
            _floatRectAtTabStart = new Rect();
            _floatViewInstanceAtTabStart = -1;
            _floatAttachAtTabStart = -1;
            _refreshAtFloatTabStart = 0;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _logErrors = 0;
            _logExceptions = 0;
            _refreshAtStart = 0;
            _viewInstance = -1;
            _attachCountAtStart = 0;
            _origWindowRect = new Rect();
            _prefShell = null;
            _prefHadShell = false;
            _prefWindow = null;
            _prefHadWindow = false;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(TwinUiV2ChromeStage.PendingKey, false) || !EditorApplication.isPlaying
                || _finished)
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

            if (Time.realtimeSinceStartup - _startAt > 150f)
            {
                Add("timeout", "check exceeded 150s", false);
                Finish();
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Add("exception", e.GetType().Name + ": " + e.Message, false);
                Finish();
            }
        }

        private static void Start()
        {
            _started = true;
            _startAt = Time.realtimeSinceStartup;
            _nextAt = _startAt + 0.7f;
            Application.logMessageReceived += OnLog;
            _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            _shell = _window != null ? _window.ShellV2 : null;
            _surface = _shell != null ? _shell.Surface : null;
            SnapshotPrefs();
            if (_window != null)
            {
                _origWindowRect = _window.WindowRect;
            }

            if (_surface == null || _shell == null || _window == null)
            {
                Add("runtime ready", "window=" + (_window != null) + " shell=" + (_shell != null)
                    + " surface=" + (_surface != null), false);
                Finish();
                return;
            }

            // P3 关键：在 ResetForCheck 之前断言"启动默认全部停靠"。
            Add("startup floating=0", "floating=" + _surface.FloatingCount
                + " saved=" + _surface.SavedFloatingCount + " windows=" + CountWindowNodes(),
                _surface.FloatingCount == 0 && CountWindowNodes() == 0);
            Add("startup all cards docked", "modules=" + _surface.ModuleCount, AllModulesDocked());

            _refreshAtStart = _surface.RefreshCount;
            DumpHeader();
            _surface.ResetForCheck();
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            _nextAt = Time.realtimeSinceStartup + 0.6f;
            _step = 1;
        }

        private static void Step()
        {
            switch (_step)
            {
                case 1:
                    if (_widthIndex >= Widths.Length)
                    {
                        _step = 2;
                        _nextAt = Time.realtimeSinceStartup + 0.2f;
                        return;
                    }

                    ApplyWidth(Widths[_widthIndex]);
                    _nextAt = Time.realtimeSinceStartup + 0.45f;
                    _step = 11;
                    return;
                case 11:
                    CheckWidth(Widths[_widthIndex]);
                    _widthIndex++;
                    _step = 1;
                    _nextAt = Time.realtimeSinceStartup + 0.2f;
                    return;
                case 2:
                    // 逐页签切换：卡片数正确、浮窗数不增、无新窗口。
                    if (_tabIndex >= TwinUiWorkspaceRegistry.All.Count)
                    {
                        _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
                        _step = 3;
                        _nextAt = Time.realtimeSinceStartup + 0.4f;
                        return;
                    }

                    var ws = TwinUiWorkspaceRegistry.All[_tabIndex];
                    _surface.SetTab(ws.Id);
                    _nextAt = Time.realtimeSinceStartup + 0.4f;
                    _step = 21;
                    return;
                case 21:
                    CheckTabs();
                    _tabIndex++;
                    _step = 2;
                    _nextAt = Time.realtimeSinceStartup + 0.2f;
                    return;
                case 3:
                    _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
                    var module = _surface.Find("joints");
                    _viewInstance = module != null && module.View != null ? module.View.GetInstanceID() : -1;
                    _attachCountAtStart = module != null ? module.HostAttachCount : -1;
                    _surface.ToggleFloating("joints");
                    _nextAt = Time.realtimeSinceStartup + 0.5f;
                    _step = 31;
                    return;
                case 31:
                    CheckSingleFloat();
                    var floatWin = _surface.FloatingWindowFor("joints");
                    var floatModule = _surface.Find("joints");
                    _floatRectAtTabStart = floatWin != null ? floatWin.WindowRect : Rect.zero;
                    _floatViewInstanceAtTabStart = floatModule != null && floatModule.View != null
                        ? floatModule.View.GetInstanceID() : -1;
                    _floatAttachAtTabStart = floatModule != null ? floatModule.HostAttachCount : -1;
                    _refreshAtFloatTabStart = _surface.RefreshCount;
                    _floatTabIndex = 0;
                    _nextAt = Time.realtimeSinceStartup + 0.2f;
                    _step = 311;
                    return;
                case 311:
                    if (_floatTabIndex >= TwinUiWorkspaceRegistry.All.Count)
                    {
                        _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
                        _nextAt = Time.realtimeSinceStartup + 0.35f;
                        _step = 32;
                        return;
                    }

                    // 浮窗保持时依次切换全部页签；只切页，不碰浮窗。
                    _surface.SetTab(TwinUiWorkspaceRegistry.All[_floatTabIndex].Id);
                    _nextAt = Time.realtimeSinceStartup + 0.35f;
                    _step = 312;
                    return;
                case 312:
                    CheckFloatingAcrossTab(_floatTabIndex);
                    _floatTabIndex++;
                    _nextAt = Time.realtimeSinceStartup + 0.2f;
                    _step = 311;
                    return;
                case 32:
                    CheckFloatControls();
                    _nextAt = Time.realtimeSinceStartup + 0.3f;
                    _step = 33;
                    return;
                case 33:
                    CheckFloatMaximize();
                    _nextAt = Time.realtimeSinceStartup + 0.3f;
                    _step = 34;
                    return;
                case 34:
                    CheckMainResizeIndependence();
                    _nextAt = Time.realtimeSinceStartup + 0.3f;
                    _step = 35;
                    return;
                case 35:
                    CheckDockBack();
                    _nextAt = Time.realtimeSinceStartup + 0.5f;
                    _step = 36;
                    return;
                case 36:
                    CheckNoAutoRestore();
                    CheckDataStillRefreshing();
                    Finish();
                    return;
            }
        }

        // ---------------------------------------------------------------- 顶栏 / 卡片几何

        private static void ApplyWidth(int width)
        {
            Rect canvas = _window.CanvasRect.rect;
            float h = Mathf.Min(canvas.height - _window.BottomInset - 24f, width * 9f / 16f);
            h = Mathf.Max(480f, h);
            _window.ApplyLayout(new Rect(16f, 16f, width, h), false, false);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            _shell.ApplyLayout(_window.Content.rect.width, _window.Content.rect.height);
        }

        private static void CheckWidth(int width)
        {
            var content = _window.Content;
            Rect header = WorldRect(_shell.HeaderRect, content);
            Rect tabs = WorldRect(_shell.TabsRect, content);
            Rect toolbar = WorldRect(_shell.ToolbarRect, content);
            float left = TwinUIStyle.WorkbenchPadX;
            float right = _window.Content.rect.width - TwinUIStyle.WorkbenchPadX;
            Add("W" + width + " header/tabs left", "header=" + F(header.xMin) + " tabs=" + F(tabs.xMin)
                + " toolbar=" + F(toolbar.xMin) + " expected=" + F(left),
                Near(header.xMin, left) && Near(tabs.xMin, left) && Near(toolbar.xMin, left));
            Add("W" + width + " header/tabs right", "header=" + F(header.xMax) + " tabs=" + F(tabs.xMax)
                + " toolbar=" + F(toolbar.xMax) + " expected=" + F(right),
                Near(header.xMax, right) && Near(tabs.xMax, right) && Near(toolbar.xMax, right));

            var title = _shell.HeaderRect != null ? _shell.HeaderRect.Find("Title") as RectTransform : null;
            var maxBtn = _window.Root != null ? _window.Root.Find("BtnWindowMax") as RectTransform : null;
            if (title != null && maxBtn != null)
            {
                Rect t = WorldRect(title, content);
                Rect b = WorldRect(maxBtn, content);
                Add("W" + width + " title/button center", "titleY=" + F(t.center.y) + " buttonY=" + F(b.center.y),
                    Mathf.Abs(t.center.y - b.center.y) <= 1.5f);
            }
            else
            {
                Add("W" + width + " title/window buttons", "title=" + (title != null) + " max=" + (maxBtn != null),
                    title != null && maxBtn != null);
            }

            // chips 与主窗右上按钮不重叠（不同高度带也必须成立）。
            var chips = new[] { "KpiSource", "AlarmBadge", "RecBadge", "EstopChip" };
            bool noOverlap = true;
            if (_shell.HeaderRect != null)
            {
                foreach (var chipName in chips)
                {
                    var chip = _shell.HeaderRect.Find(chipName) as RectTransform;
                    if (chip == null || !chip.gameObject.activeSelf)
                    {
                        continue;
                    }

                    Rect c = WorldRect(chip, content);
                    foreach (var btnName in new[] { "BtnWindowMax", "BtnWindowMin", "BtnWindowHide" })
                    {
                        var btn = _window.Root != null ? _window.Root.Find(btnName) as RectTransform : null;
                        if (btn == null)
                        {
                            continue;
                        }

                        Rect b = WorldRect(btn, content);
                        if (c.Overlaps(b))
                        {
                            noOverlap = false;
                            Add("W" + width + " chip overlaps " + btnName,
                                "chip=" + chipName + " " + R(c) + " btn=" + R(b), false);
                        }
                    }
                }
            }

            Add("W" + width + " chips/window buttons no overlap", "ok=" + noOverlap, noOverlap);

            // 卡片铺满列宽，localScale=1；宽卡跨列。
            float cWidth = _surface.Page.ContentWidth;
            int cols = _surface.Page.Columns;
            float expectedW = TwinUiLayoutContract.WideWidth(cWidth);
            int checkedCards = 0;
            int widthBad = 0;
            int scaleBad = 0;
            float expectedCol = TwinUiLayoutContract.ColumnWidth(cWidth, cols);
            foreach (var card in _surface.Page.Cards)
            {
                if (card == null || !card.gameObject.activeSelf || card.Root == null)
                {
                    continue;
                }

                checkedCards++;
                var module = _surface.Find(card.ModuleId);
                float want = module != null && module.WideContent && cols >= 2 ? expectedW : expectedCol;
                if (Mathf.Abs(card.Root.rect.width - want) > 1.5f)
                {
                    widthBad++;
                }

                if (Mathf.Abs(card.Root.localScale.x - 1f) > 0.001f
                    || Mathf.Abs(card.Root.localScale.y - 1f) > 0.001f)
                {
                    scaleBad++;
                }
            }

            Add("W" + width + " cards", "visible=" + checkedCards + " cols=" + cols, checkedCards > 0 && cols <= 2);
            Add("W" + width + " card width fills column", "bad=" + widthBad + " expectedCol=" + F(expectedCol),
                widthBad == 0);
            Add("W" + width + " card localScale=1", "bad=" + scaleBad, scaleBad == 0);

            CheckAllCardControls("W" + width);
            if (width == 1920)
            {
                DumpHeader();
                CheckCardDragOutDisabled();
            }
        }

        // ---------------------------------------------------------------- 页签 / 控件

        private static void CheckTabs()
        {
            int expected = _surface.CurrentWorkspace != null ? _surface.CurrentWorkspace.WidgetIds.Length : -1;
            Add("tab " + _surface.Tab + " cards", "cards=" + _surface.Page.CardCount + " expected=" + expected,
                _surface.Page.CardCount == expected);
            Add("tab " + _surface.Tab + " floating unchanged", "floating=" + _surface.FloatingCount
                + " windows=" + CountWindowNodes(),
                _surface.FloatingCount == 0 && CountWindowNodes() == 0);
        }

        private static void CheckAllCardControls(string tag)
        {
            int checkedCount = 0;
            int bad = 0;
            foreach (var card in _surface.Page.Cards)
            {
                if (card == null || !card.gameObject.activeSelf || card.Root == null)
                {
                    continue;
                }

                var cluster = card.Root.Find("CardControls") as RectTransform;
                if (cluster == null)
                {
                    continue;
                }

                checkedCount++;
                if (!CheckCluster(cluster, tag + " " + card.ModuleId))
                {
                    bad++;
                }

                var title = card.Root.Find("CardHeader/Title") as RectTransform;
                if (title != null)
                {
                    Rect titleRect = WorldRect(title, card.Root);
                    Rect clusterRect = WorldRect(cluster, card.Root);
                    // title 的右边界必须让开控件集群（世界坐标换算到卡片根）。
                    if (titleRect.xMax > clusterRect.xMin + 0.5f)
                    {
                        bad++;
                        Add(tag + " " + card.ModuleId + " title overlaps controls",
                            "title=" + R(titleRect) + " controls=" + R(clusterRect), false);
                    }
                }
            }

            Add(tag + " card controls", "checked=" + checkedCount + " bad=" + bad,
                checkedCount > 0 && bad == 0);
        }

        private static void CheckCardDragOutDisabled()
        {
            int checkedCards = 0;
            int bad = 0;
            foreach (var card in _surface.Page.Cards)
            {
                if (card == null || !card.gameObject.activeSelf || card.Root == null)
                {
                    continue;
                }

                checkedCards++;
                var drag = card.GetComponent<TwinUiCardDragOut>();
                bool disabled = drag == null || !drag.enabled;
                bool gesture = drag != null && drag.BeginDragForCheck(Vector2.zero);
                if (!disabled || gesture)
                {
                    bad++;
                    Add("card drag-out disabled " + card.ModuleId,
                        "component=" + (drag != null) + " enabled=" + (drag != null && drag.enabled)
                        + " gesture=" + gesture, false);
                }
            }

            Add("dock card title drag-out disabled", "checked=" + checkedCards + " bad=" + bad,
                checkedCards > 0 && bad == 0);
        }

        private static bool CheckCluster(RectTransform cluster, string tag)
        {
            int n = cluster.childCount;
            if (n <= 0)
            {
                Add(tag + " controls empty", "children=0", false);
                return false;
            }

            bool ok = true;
            var rects = new List<Rect>();
            for (int i = 0; i < n; i++)
            {
                var child = cluster.GetChild(i) as RectTransform;
                if (child == null)
                {
                    continue;
                }

                var button = child.GetComponent<TwinUIButton>();
                var tip = child.GetComponent<TwinUiTooltip>();
                float size = child.rect.width;
                bool sizeOk = Mathf.Abs(child.rect.width - TwinUIStyle.ControlButtonSize) <= 0.6f
                    && Mathf.Abs(child.rect.height - TwinUIStyle.ControlButtonSize) <= 0.6f;
                bool tipOk = tip != null && !string.IsNullOrEmpty(tip.Text);
                if (!sizeOk || button == null || !tipOk)
                {
                    ok = false;
                    Add(tag + " control " + child.name,
                        "size=" + F(size) + " button=" + (button != null) + " tip=" + (tipOk ? "ok" : "missing"),
                        false);
                }

                rects.Add(WorldRect(child, cluster));
            }

            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (rects[i].Overlaps(rects[j]))
                    {
                        ok = false;
                        Add(tag + " control overlap", "i=" + i + " j=" + j, false);
                    }
                }
            }

            // 间距 4px（允许 0.75 容差）。
            for (int i = 1; i < rects.Count; i++)
            {
                float gap = rects[i].xMin - rects[i - 1].xMax;
                if (Mathf.Abs(gap - TwinUIStyle.ControlButtonGap) > 0.75f)
                {
                    ok = false;
                    Add(tag + " control gap", "gap=" + F(gap), false);
                }
            }

            return ok;
        }

        // ---------------------------------------------------------------- 悬浮 / 停靠

        private static void CheckSingleFloat()
        {
            var module = _surface.Find("joints");
            var win = _surface.FloatingWindowFor("joints");
            Add("pin one card -> one float", "floating=" + _surface.FloatingCount, _surface.FloatingCount == 1);
            Add("float state", "state=" + (module != null ? module.State.ToString() : "null"),
                module != null && module.State == TwinUiModuleState.Floating);
            Add("float window exists", "win=" + (win != null) + " layer=" + (_surface.FloatingLayer != null)
                + " parent=" + (win != null && win.transform.parent != null
                    ? win.transform.parent.name : "null"),
                win != null && win.IsConfigured && _surface.FloatingLayer != null
                && win.transform.parent == _surface.FloatingLayer);
            Add("single content view (no clone)", "instance=" + (module != null && module.View != null
                    ? module.View.GetInstanceID() : 0) + " expected=" + _viewInstance,
                module != null && module.View != null && module.View.GetInstanceID() == _viewInstance);
            Add("attach once (no resubscribe)", "attach=" + (module != null ? module.HostAttachCount : -1)
                + " expected=" + _attachCountAtStart,
                module != null && module.HostAttachCount == _attachCountAtStart);

            int othersFloating = 0;
            foreach (var m in _surface.Modules)
            {
                if (m.Id != "joints" && m.State == TwinUiModuleState.Floating)
                {
                    othersFloating++;
                }
            }

            Add("other cards stay docked", "othersFloating=" + othersFloating, othersFloating == 0);
        }

        private static void CheckFloatingAcrossTab(int index)
        {
            string tab = _surface.Tab;
            var module = _surface.Find("joints");
            var win = _surface.FloatingWindowFor("joints");
            Rect now = win != null ? win.WindowRect : Rect.zero;
            Add("float tab " + tab + " window count", "floating=" + _surface.FloatingCount
                + " windows=" + CountWindowNodes(), _surface.FloatingCount == 1 && CountWindowNodes() == 1);
            Add("float tab " + tab + " rect unchanged", "before=" + R(_floatRectAtTabStart) + " now=" + R(now),
                win != null && Near(now.x, _floatRectAtTabStart.x) && Near(now.y, _floatRectAtTabStart.y)
                && Near(now.width, _floatRectAtTabStart.width)
                && Near(now.height, _floatRectAtTabStart.height));
            Add("float tab " + tab + " view/attach unchanged", "instance="
                + (module != null && module.View != null ? module.View.GetInstanceID() : 0)
                + " attach=" + (module != null ? module.HostAttachCount : -1),
                module != null && module.View != null
                && module.View.GetInstanceID() == _floatViewInstanceAtTabStart
                && module.HostAttachCount == _floatAttachAtTabStart);
            Add("float tab " + tab + " state", "state=" + (module != null ? module.State.ToString() : "null"),
                module != null && module.State == TwinUiModuleState.Floating);
            Add("float tab " + tab + " data refresh", "refresh=" + _surface.RefreshCount
                + " start=" + _refreshAtFloatTabStart, _surface.RefreshCount > _refreshAtFloatTabStart);
            Add("float tab " + tab + " page cards", "cards=" + _surface.Page.CardCount,
                _surface.Page.CardCount > 0);
        }

        private static void CheckFloatControls()
        {
            var win = _surface.FloatingWindowFor("joints");
            if (win == null || win.WindowRoot == null)
            {
                Add("float controls", "win missing", false);
                return;
            }

            var cluster = win.WindowRoot.Find("WindowControls") as RectTransform;
            Add("float controls cluster", "cluster=" + (cluster != null) + " children="
                    + (cluster != null ? cluster.childCount : -1),
                cluster != null && cluster.childCount == 2 && CheckCluster(cluster, "float"));
            var title = win.WindowRoot.Find("ModuleTitle") as RectTransform;
            if (title != null && cluster != null)
            {
                Rect tr = WorldRect(title, win.WindowRoot);
                Rect cr = WorldRect(cluster, win.WindowRoot);
                Add("float title clears controls", "title=" + R(tr) + " controls=" + R(cr), tr.xMax <= cr.xMin + 0.5f);
            }

            var maxBtn = cluster != null ? cluster.Find("BtnMaximize") as RectTransform : null;
            var pinBtn = cluster != null ? cluster.Find("BtnDock") as RectTransform : null;
            Add("float has max + pin", "max=" + (maxBtn != null) + " pin=" + (pinBtn != null),
                maxBtn != null && pinBtn != null);
        }

        private static void CheckFloatMaximize()
        {
            var win = _surface.FloatingWindowFor("joints");
            Rect before = win != null ? win.WindowRect : Rect.zero;
            _surface.ToggleMaximizeFloating("joints");
            var afterWin = _surface.FloatingWindowFor("joints");
            Rect after = afterWin != null ? afterWin.WindowRect : Rect.zero;
            bool maximized = _surface.IsFloatingMaximized("joints");
            Add("float maximize changes rect only", "max=" + maximized + " before=" + R(before) + " after=" + R(after)
                + " floating=" + _surface.FloatingCount,
                maximized && _surface.FloatingCount == 1 && after.width > before.width + 1f);
            var module = _surface.Find("joints");
            Add("float maximize keeps state", "state=" + (module != null ? module.State.ToString() : "null"),
                module != null && module.State == TwinUiModuleState.Floating);
            _surface.ToggleMaximizeFloating("joints");
            var restored = _surface.FloatingWindowFor("joints");
            Add("float restore", "max=" + _surface.IsFloatingMaximized("joints"),
                !_surface.IsFloatingMaximized("joints") && restored != null);
        }

        private static void CheckMainResizeIndependence()
        {
            var win = _surface.FloatingWindowFor("joints");
            if (win == null)
            {
                Add("main resize independence", "win missing", false);
                return;
            }

            _floatRectBeforeResize = win.WindowRect;
            Rect canvas = _window.CanvasRect.rect;
            _window.ApplyLayout(new Rect(16f, 16f, Mathf.Min(1280f, canvas.width - 32f),
                Mathf.Min(720f, canvas.height - _window.BottomInset - 24f)), false, false);
            _nextAt = Time.realtimeSinceStartup + 0.35f;
            // 下一帧再比较（ApplyLayout 立即生效，这里给响应式一次机会改动主窗，不应触及浮窗）。
            var after = _surface.FloatingWindowFor("joints");
            Rect now = after != null ? after.WindowRect : Rect.zero;
            Add("main resize keeps float rect", "before=" + R(_floatRectBeforeResize) + " after=" + R(now),
                after != null && Near(now.x, _floatRectBeforeResize.x)
                && Near(now.y, _floatRectBeforeResize.y)
                && Near(now.width, _floatRectBeforeResize.width)
                && Near(now.height, _floatRectBeforeResize.height));

            _window.Minimize();
            Add("main minimize keeps float active", "visible=" + (win.gameObject.activeSelf)
                + " floating=" + _surface.FloatingCount,
                win.gameObject.activeSelf && _surface.FloatingCount == 1);
            _window.Restore();
        }

        private static void CheckDockBack()
        {
            var module = _surface.Find("joints");
            var card = _surface.CardFor("joints");
            int attachBeforeDock = module != null ? module.HostAttachCount : -1;
            _surface.ToggleFloating("joints");
            Add("pin-off docks back", "floating=" + _surface.FloatingCount + " state="
                + (module != null ? module.State.ToString() : "null"),
                _surface.FloatingCount == 0 && module != null && module.State == TwinUiModuleState.Card);
            Add("docked view returns to card", "parent=" + (module != null && module.View != null
                    && module.View.parent != null ? module.View.parent.name : "null"),
                module != null && module.View != null && card != null && module.View.parent == card.Content);
            Add("dock keeps single view", "instance=" + (module != null && module.View != null
                    ? module.View.GetInstanceID() : 0) + " expected=" + _viewInstance,
                module != null && module.View != null && module.View.GetInstanceID() == _viewInstance);
            int attachDelta = module != null ? module.HostAttachCount - attachBeforeDock : -99;
            Add("dock no duplicate subscription", "attach=" + (module != null ? module.HostAttachCount : -1)
                + " beforeDock=" + attachBeforeDock + " delta=" + attachDelta,
                module != null && attachDelta >= 0 && attachDelta <= 1);
            Add("dock card visible", "card=" + (card != null && card.gameObject.activeSelf)
                + " content=" + (card != null && card.Content.gameObject.activeSelf),
                card != null && card.gameObject.activeSelf && card.Content.gameObject.activeSelf);
        }

        private static void CheckNoAutoRestore()
        {
            // 再次设置页签，仍不应自动弹出（P3 的自检闭环）。
            _surface.SetTab(TwinUiWorkspaceRegistry.Experiment);
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            Add("tab switch still no float", "floating=" + _surface.FloatingCount
                + " windows=" + CountWindowNodes(),
                _surface.FloatingCount == 0 && CountWindowNodes() == 0);
        }

        private static void CheckDataStillRefreshing()
        {
            Add("data refresh continues", "refresh=" + _surface.RefreshCount + " start=" + _refreshAtStart,
                _surface.RefreshCount > _refreshAtStart);
            var binders = UnityEngine.Object.FindObjectsOfType<TwinUIStatusBinder>();
            Add("status binder single", "count=" + binders.Length, binders.Length == 1);
            int views = 0;
            foreach (var m in _surface.Modules)
            {
                if (m.View != null)
                {
                    views++;
                }
            }

            Add("module views bounded", "views=" + views + " modules=" + _surface.ModuleCount,
                views <= _surface.ModuleCount && views > 0);
        }

        // ---------------------------------------------------------------- 工具

        private static void DumpHeader()
        {
            try
            {
                var header = _shell != null ? _shell.HeaderRect : null;
                if (header == null)
                {
                    return;
                }

                var sb = new StringBuilder();
                foreach (Transform chip in header)
                {
                    var crt = chip as RectTransform;
                    sb.AppendLine("CHIP " + chip.name + " active=" + chip.gameObject.activeSelf
                        + " rect=" + (crt != null ? crt.rect.ToString() : "n/a")
                        + " pos=" + (crt != null ? crt.anchoredPosition.ToString() : "n/a")
                        + " size=" + (crt != null ? crt.sizeDelta.ToString() : "n/a"));
                    var own = chip.GetComponent<TMP_Text>();
                    if (own != null)
                    {
                        sb.AppendLine("  OWN chars=" + (own.textInfo != null ? own.textInfo.characterCount : -1)
                            + " text='" + own.text + "' font=" + own.fontSize.ToString("F1"));
                    }
                    foreach (Transform child in chip)
                    {
                        var t = child.GetComponent<TMP_Text>();
                        if (t != null)
                        {
                            if (t.textInfo != null && t.textInfo.characterCount == 0
                                && !string.IsNullOrEmpty(t.text))
                            {
                                t.gameObject.SetActive(false);
                                t.gameObject.SetActive(true);
                            }

                            t.SetAllDirty();
                            t.ForceMeshUpdate();
                            int beforeLiteral = t.textInfo != null ? t.textInfo.characterCount : -1;
                            bool fontHas = t.font != null && t.font.HasCharacter('数');
                            int fontChars = t.font != null && t.font.characterTable != null
                                ? t.font.characterTable.Count : -1;
                            t.font = TwinUIStyle.GetFont();
                            t.ForceMeshUpdate();
                            int afterFont = t.textInfo != null ? t.textInfo.characterCount : -1;
                            string original = t.text;
                            t.text = "测试";
                            t.ForceMeshUpdate();
                            int afterLiteral = t.textInfo != null ? t.textInfo.characterCount : -1;
                            t.text = original;
                            t.ForceMeshUpdate();
                            if (beforeLiteral == 0)
                            {
                                sb.AppendLine("LABELPROBE before=" + beforeLiteral + " literal=" + afterLiteral
                                    + " afterFont=" + afterFont + " fontHas=" + fontHas
                                    + " fontChars=" + fontChars
                                    + " len=" + (original != null ? original.Length : -1)
                                    + " codes=" + Codes(original));
                            }
                        }

                        var rt = child as RectTransform;
                        sb.AppendLine("  CHILD " + child.name + " active=" + child.gameObject.activeSelf
                            + " tmp=" + (t != null) + " text='" + (t != null ? t.text : string.Empty) + "'"
                            + " enabled=" + (t != null && t.enabled)
                            + " activeEnabled=" + (t != null && t.isActiveAndEnabled)
                            + " font=" + (t != null && t.font != null)
                            + " chars=" + (t != null && t.textInfo != null ? t.textInfo.characterCount : -1)
                            + " wrap=" + (t != null && t.enableWordWrapping)
                            + " overflow=" + (t != null ? t.overflowMode.ToString() : "-")
                            + " maxVis=" + (t != null ? t.maxVisibleCharacters : -1)
                            + " firstVis=" + (t != null ? t.firstVisibleCharacter : -1)
                            + " autoSize=" + (t != null && t.enableAutoSizing)
                            + " cull=" + (t != null && t.canvasRenderer != null && t.canvasRenderer.cull)
                            + " alpha=" + (t != null && t.canvasRenderer != null
                                ? t.canvasRenderer.GetAlpha().ToString("F2") : "-")
                            + " localScale=" + (rt != null ? rt.localScale.ToString() : "-")
                            + " fontName=" + (t != null && t.font != null ? t.font.name : "-")
                            + " awake=" + (t != null ? IsTmpAwake(t).ToString() : "-")
                            + " renderedW=" + (t != null ? t.renderedWidth.ToString("F1") : "-")
                            + " font=" + (t != null ? t.fontSize.ToString("F1") : "-")
                            + " color=" + (t != null ? t.color.ToString() : "-")
                            + " rect=" + (rt != null ? rt.rect.ToString() : "n/a")
                            + " pos=" + (rt != null ? rt.anchoredPosition.ToString() : "n/a"));
                    }
                }

                Directory.CreateDirectory("Logs");
                // 探针：同一字体下新建一段"数据源"，判断是字体缺字还是该 TMP 对象本身不生成。
                var probeGo = new GameObject("ProbeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
                probeGo.transform.SetParent(header, false);
                var probe = probeGo.GetComponent<TextMeshProUGUI>();
                probe.font = TwinUIStyle.GetFont();
                probe.fontSize = 11f;
                probe.text = "数据源";
                probe.ForceMeshUpdate();
                sb.AppendLine("PROBE chars=" + probe.textInfo.characterCount
                    + " has数=" + probe.font.HasCharacter('数')
                    + " has据=" + probe.font.HasCharacter('据')
                    + " has源=" + probe.font.HasCharacter('源')
                    + " font=" + (probe.font != null ? probe.font.name : "null")
                    + " material=" + (probe.fontSharedMaterial != null));
                foreach (float fs in new[] { 11f, 12f, 13f, 14f, 16f, 18f })
                {
                    probe.fontSize = fs;
                    probe.ForceMeshUpdate();
                    sb.AppendLine("  PROBE fs=" + fs + " chars=" + probe.textInfo.characterCount);
                }
                UnityEngine.Object.Destroy(probeGo);
                File.WriteAllText("Logs/ui64_header_dump.txt", sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CHROME64] header dump failed: " + e.Message);
            }
        }

        private static bool IsTmpAwake(TMP_Text text)
        {
            try
            {
                for (Type t = text.GetType(); t != null; t = t.BaseType)
                {
                    var f = t.GetField("m_isAwake",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null)
                    {
                        return (bool)f.GetValue(text);
                    }
                }
            }
            catch (Exception)
            {
            }

            return true;
        }

        private static string Codes(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "empty";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < s.Length && i < 12; i++)
            {
                sb.Append(((int)s[i]).ToString("X4")).Append(' ');
            }

            return sb.ToString();
        }

        private static bool AllModulesDocked()
        {
            foreach (var m in _surface.Modules)
            {
                if (m.State == TwinUiModuleState.Floating)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountWindowNodes()
        {
            if (_window == null || _window.CanvasRect == null)
            {
                return -1;
            }

            int n = 0;
            foreach (var t in _window.CanvasRect.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("UiModuleWindow_", StringComparison.Ordinal))
                {
                    n++;
                }
            }

            return n;
        }

        private static Rect WorldRect(RectTransform rt, RectTransform space)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 min = space.InverseTransformPoint(corners[0]);
            Vector3 max = space.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        private static bool Near(float a, float b)
        {
            return Mathf.Abs(a - b) <= 1.0f;
        }

        private static string F(float v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string R(Rect r)
        {
            return "(" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "x" + F(r.height) + ")";
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            if (!ok)
            {
                Debug.LogWarning("[CHROME64] FAIL " + label + " | " + detail);
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

        private static void SnapshotPrefs()
        {
            _prefHadShell = PlayerPrefs.HasKey(TwinUiShellPrefs.Key);
            _prefShell = PlayerPrefs.GetString(TwinUiShellPrefs.Key, string.Empty);
            _prefHadWindow = PlayerPrefs.HasKey("TwinUI.Window.Layout.v1");
            _prefWindow = PlayerPrefs.GetString("TwinUI.Window.Layout.v1", string.Empty);
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
                Debug.LogWarning("[CHROME64] restore prefs failed: " + e.Message);
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
            SessionState.SetBool(TwinUiV2ChromeStage.PendingKey, false);
            try
            {
                if (_window != null && _origWindowRect.width > 1f)
                {
                    _window.ApplyLayout(_origWindowRect, false, false);
                }
            }
            catch (Exception)
            {
            }

            RestorePrefs();

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== V2 Card Chrome + Float/Dock (UI/64)  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("scene=" + SceneManager.GetActiveScene().path
                + " shellMode=" + TwinUiShellMode.Kind);
            foreach (var item in Items)
            {
                sb.AppendLine("[CHROME64] [item] " + item.Label + " | " + item.Detail
                    + " | result=" + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0;
            sb.AppendLine("[CHROME64] items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui64_chrome_float.txt"), true);
                string json = "{\"items\":" + Items.Count + ",\"fails\":" + fails
                    + ",\"logErrors\":" + _logErrors + ",\"logExceptions\":" + _logExceptions
                    + ",\"result\":\"" + (ok ? "PASS" : "FAIL") + "\"}";
                File.WriteAllText(JsonPath, json, new UTF8Encoding(false));
                File.Copy(JsonPath, Path.Combine(EvidenceLogs, "ui64_chrome_float.json"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CHROME64] log write failed: " + e.Message);
            }

            Debug.Log("[CHROME64] " + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails + " log=" + LogPath);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }
    }
}