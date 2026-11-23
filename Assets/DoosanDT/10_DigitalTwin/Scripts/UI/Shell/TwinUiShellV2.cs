// TwinUiShellV2.cs —— 浏览器式工作台外壳（P7/P8，运行时构建，零场景改动）
// 结构：UiWindowContent
//         ├ Header（沿用旧顶栏：标题 + KPI chip + 急停 + 时钟，作为窗口标题栏）
//         └ ShellV2Root
//              ├ WorkspaceTabs  一级页签（5 个，窄窗自动两行）
//              ├ Toolbar        面包屑 + 预设按钮 + 复位
//              └ Viewport       ScrollRect（纵向），页内容再长也不裁切
// 设计目标：用"页签 + 卡片网格 + 可拆出浮窗 + 任务栏"取代旧的三列固定面板；
//           全部运行时构建，场景只保留早已序列化的两个宿主组件 → 场景哈希可保持不变。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiShellV2 : MonoBehaviour
    {
        public const float TabHeight = TwinUIStyle.TabBarV2Height;
        public const float ToolbarHeight = TwinUIStyle.ToolbarV2Height;
        public const float MinTabWidth = 86f;
        public const float MinTabWidthCompact = 68f;
        public const float TabGap = 4f;
        public const float ButtonInset = 104f;

        private TwinUiWindowHost _window;
        private RectTransform _content;
        private RectTransform _header;
        private RectTransform _root;
        private RectTransform _tabs;
        private RectTransform _toolbar;
        private RectTransform _viewport;
        private RectTransform _pageContent;
        private ScrollRect _scroll;
        private TMP_Text _crumb;
        private TwinUIButton _resetButton;
        private TwinUiPageView _page;
        private TwinUiSurfaceHost _surface;
        private TwinUiTaskbar _taskbar;
        private TwinUiScatteredUiBridge _scatteredBridge;
        private TwinUiRoamBridge _roamBridge;
        private TwinUiMinimizeFab _fab;
        private TwinUiVrAdapter _vrAdapter;
        private TwinUiDisplayWatcher _displayWatcher;

        private readonly List<TwinUIButton> _tabButtons = new List<TwinUIButton>();
        private readonly List<TwinUIButton> _presetButtons = new List<TwinUIButton>();
        private readonly List<string> _presetIds = new List<string>();

        private float _lastWidth;
        private float _lastHeight;
        private int _tabsRows = 1;
        private int _layoutCount;

        public TwinUiSurfaceHost Surface => _surface;
        public TwinUiTaskbar Taskbar => _taskbar;
        public TwinUiScatteredUiBridge ScatteredBridge => _scatteredBridge;
        public TwinUiRoamBridge RoamBridge => _roamBridge;
        public TwinUiMinimizeFab Fab => _fab;
        public TwinUiVrAdapter VrAdapter => _vrAdapter;
        public TwinUiDisplayWatcher DisplayWatcher => _displayWatcher;
        public TwinUiPageView Page => _page;
        public RectTransform TabsRect => _tabs;
        public RectTransform ToolbarRect => _toolbar;
        public RectTransform HeaderRect => _header;
        public RectTransform ViewportRect => _viewport;
        public int TabCount => _tabButtons.Count;
        public int TabsRows => _tabsRows;
        public float ViewportWidth => _viewport != null ? _viewport.rect.width : 0f;
        public float ViewportHeight => _viewport != null ? _viewport.rect.height : 0f;
        public float LastWidth => _lastWidth;
        public float LastHeight => _lastHeight;
        public int LayoutCount => _layoutCount;
        public float TabsBlockHeight => _tabsRows * TabHeight;
        public string CurrentTab => _surface != null ? _surface.Tab : string.Empty;
        public string Preset => _surface != null ? _surface.Preset : string.Empty;

        public static TwinUiShellV2 Install(TwinUiWindowHost host)
        {
            if (host == null || host.Content == null)
            {
                return null;
            }

            var existing = host.Content.Find("ShellV2Root");
            if (existing != null)
            {
                var found = existing.GetComponentInParent<TwinUiShellV2>();
                if (found != null)
                {
                    return found;
                }
            }

            var go = new GameObject("TwinUiShellV2");
            go.transform.SetParent(host.transform, false);
            var shell = go.AddComponent<TwinUiShellV2>();
            shell.Build(host);
            return shell;
        }

        private void Build(TwinUiWindowHost host)
        {
            _window = host;
            _content = host.Content;
            _header = _content != null ? _content.Find("Header") as RectTransform : null;

            HideLegacyContainers();
            BuildRoot();
            BuildTabs();
            BuildToolbar();
            BuildViewport();

            _page = new TwinUiPageView();
            _page.Build(_pageContent);

            var surfaceGo = new GameObject("TwinUiSurfaceHost");
            surfaceGo.transform.SetParent(transform, false);
            _surface = surfaceGo.AddComponent<TwinUiSurfaceHost>();
            TwinUiWidgetCatalog.RegisterAll(_surface);
            _surface.Configure(host, _page);
            _surface.StateChanged += OnSurfaceChanged;
            _surface.PageHeightChanged += OnPageHeightChanged;

            _taskbar = TwinUiTaskbar.Create(host.Canvas, host, _surface);
            if (_taskbar != null)
            {
                host.SetBottomInset(TwinUiTaskbar.Height);
                _taskbar.ApplyLayout(host.CanvasRect != null ? host.CanvasRect.rect.width : 1920f);
            }

            TwinUiResponsiveLayout.V2PageLayoutHook = OnResponsiveLayout;
            var responsive = GetComponentInParent<TwinUiResponsiveLayout>();
            if (responsive != null)
            {
                responsive.V2RightInset = ButtonInset;
            }

            // P9：散落 UI 收编 + 漫游完善 + 最小化悬浮球
            var bridgeGo = new GameObject("TwinUiScatteredUiBridge");
            bridgeGo.transform.SetParent(transform, false);
            _scatteredBridge = bridgeGo.AddComponent<TwinUiScatteredUiBridge>();
            _scatteredBridge.Configure();

            var roamGo = new GameObject("TwinUiRoamBridge");
            roamGo.transform.SetParent(transform, false);
            _roamBridge = roamGo.AddComponent<TwinUiRoamBridge>();
            _roamBridge.Configure();

            _fab = TwinUiMinimizeFab.Create(host.Canvas, host);

            // P9.5：VR 适配骨架 + 多显示器/分辨率监听（VR 无头显 → NOT TESTED）
            _vrAdapter = TwinUiVrAdapter.Create(host);
            _displayWatcher = TwinUiDisplayWatcher.Create(host, _fab);

            host.VisibilityChanged += OnMainVisibilityChanged;
            TwinUITheme.Changed += OnThemeChanged;

            _surface.BootstrapPage();
            RefreshTabSelection();
            Debug.Log("[SHELLV2] built modules=" + _surface.ModuleCount + " tabs=" + _tabButtons.Count
                + " taskbar=" + (_taskbar != null) + " content=" + (_content != null)
                + " header=" + (_header != null));
        }

        private void HideLegacyContainers()
        {
            string[] legacy = { "TabBar", "ContextBar", "MissionRoot", "IndustrialRoot", "TwinOpsRoot" };
            for (int i = 0; i < legacy.Length; i++)
            {
                var t = _content != null ? _content.Find(legacy[i]) : null;
                if (t != null)
                {
                    t.gameObject.SetActive(false);
                }
            }

            // 旧页签宿主会 SetActive(MissionRoot) 等，必须停用，否则会与 V2 抢可见性。
            var pageHost = FindObjectOfType<TwinUIPageHost>();
            if (pageHost != null)
            {
                pageHost.enabled = false;
            }
        }

        private void BuildRoot()
        {
            _root = TwinUIStyle.Frame(_content, "ShellV2Root");
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            _root.SetAsLastSibling();
        }

        private void BuildTabs()
        {
            _tabs = TwinUIStyle.Frame(_root, "WorkspaceTabs");
            _tabs.anchorMin = new Vector2(0f, 1f);
            _tabs.anchorMax = new Vector2(1f, 1f);
            _tabs.pivot = new Vector2(0.5f, 1f);
            _tabs.anchoredPosition = Vector2.zero;
            _tabs.sizeDelta = new Vector2(0f, TabHeight);

            var all = TwinUiWorkspaceRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var ws = all[i];
                string id = ws.Id;
                var btn = TwinUIStyle.Button(_tabs, "Tab_" + id, ws.Title, TwinUIButton.Kind.Plain,
                    Vector2.zero, new Vector2(MinTabWidth, TabHeight));
                if (btn.Label != null)
                {
                    btn.Label.fontSize = TwinUIStyle.FontBodySm;
                    btn.Label.overflowMode = TextOverflowModes.Ellipsis;
                }

                btn.Button.onClick.AddListener(() => ShowTab(id));
                _tabButtons.Add(btn);
            }
        }

        private void BuildToolbar()
        {
            _toolbar = TwinUIStyle.Frame(_root, "Toolbar");
            _toolbar.anchorMin = new Vector2(0f, 1f);
            _toolbar.anchorMax = new Vector2(1f, 1f);
            _toolbar.pivot = new Vector2(0.5f, 1f);
            _toolbar.anchoredPosition = Vector2.zero;
            _toolbar.sizeDelta = new Vector2(0f, ToolbarHeight);

            var bg = TwinUIStyle.Panel(_toolbar, "ToolbarBg", TwinUIStyle.LayerColor, false);
            TwinUIStyle.Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            var line = TwinUIStyle.Panel(_toolbar, "ToolbarLine", TwinUIStyle.Alpha(TwinUIStyle.BorderColor, 0.8f));
            line.raycastTarget = false;
            var lrt = line.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 1f);

            _crumb = TwinUIStyle.Text(_toolbar, "Crumb", "⌂", TwinUIStyle.FontUnit, TwinUIStyle.QuietColor);
            _crumb.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _crumb.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _crumb.rectTransform.pivot = new Vector2(0f, 0.5f);
            _crumb.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            _crumb.rectTransform.sizeDelta = new Vector2(360f, 16f);

            var all = TwinUiLayoutPresets.All;
            for (int i = 0; i < all.Count; i++)
            {
                var preset = all[i];
                string id = preset.Id;
                var btn = TwinUIStyle.Button(_toolbar, "Preset_" + id, preset.Label, TwinUIButton.Kind.Plain,
                    Vector2.zero, new Vector2(52f, 22f));
                if (btn.Label != null)
                {
                    btn.Label.fontSize = TwinUIStyle.FontUnit;
                }

                btn.Button.onClick.AddListener(() => ApplyPreset(id));
                _presetButtons.Add(btn);
                _presetIds.Add(id);
            }

            _resetButton = TwinUIStyle.Button(_toolbar, "BtnResetLayout", "复位", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(44f, 22f), ResetLayout);
            if (_resetButton.Label != null)
            {
                _resetButton.Label.fontSize = TwinUIStyle.FontUnit;
            }

            TwinUiRadii.Apply(_toolbar);
        }

        private void BuildViewport()
        {
            var viewportGo = new GameObject("PageViewport", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            _viewport = viewportGo.GetComponent<RectTransform>();
            _viewport.SetParent(_root, false);
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.pivot = new Vector2(0.5f, 0.5f);
            _viewport.offsetMin = Vector2.zero;
            _viewport.offsetMax = Vector2.zero;

            var bg = viewportGo.GetComponent<Image>();
            bg.color = TwinUIStyle.BgColor;
            bg.raycastTarget = true;
            TwinUITheme.Tag(viewportGo, bg.color);
            var mask = viewportGo.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            var contentGo = new GameObject("PageContent", typeof(RectTransform));
            _pageContent = contentGo.GetComponent<RectTransform>();
            _pageContent.SetParent(_viewport, false);
            _pageContent.anchorMin = new Vector2(0f, 1f);
            _pageContent.anchorMax = new Vector2(1f, 1f);
            _pageContent.pivot = new Vector2(0.5f, 1f);
            _pageContent.anchoredPosition = Vector2.zero;
            _pageContent.sizeDelta = new Vector2(0f, 1000f);

            _scroll = viewportGo.GetComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _pageContent;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 36f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
        }

        // ------------------------------------------------------------- 布局

        private void OnResponsiveLayout(float width, float height)
        {
            ApplyLayout(width, height);
        }

        /// <summary>内容宽高 → 页签/工具栏/页面区尺寸。幂等。</summary>
        public void ApplyLayout(float contentWidth, float contentHeight)
        {
            if (_root == null)
            {
                return;
            }

            _layoutCount++;
            _lastWidth = contentWidth;
            _lastHeight = contentHeight;
            // Header remains a sibling of ShellV2Root. Reserve its actual bottom
            // edge, not just its height: the legacy header also has a top margin.
            float headerInset = 0f;
            if (_header != null && _header.gameObject.activeSelf)
            {
                var corners = new Vector3[4];
                _header.GetWorldCorners(corners);
                float bottom = _content.InverseTransformPoint(corners[0]).y;
                headerInset = Mathf.Max(0f, _content.rect.yMax - bottom + 8f);
            }
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = new Vector2(0f, -headerInset);
            float w = Mathf.Max(320f, contentWidth);
            float areaH = Mathf.Max(200f, contentHeight - headerInset);

            // tabs：按可用宽度决定一行/两行
            float pad = TwinUIStyle.WorkbenchPadX;
            float avail = Mathf.Max(320f, w - pad * 2f);
            float minTab = w < 860f ? MinTabWidthCompact : MinTabWidth;
            int n = Mathf.Max(1, _tabButtons.Count);
            bool twoRows = avail < n * minTab + TabGap * (n - 1);
            int rows = twoRows ? 2 : 1;
            int perRow = rows == 1 ? n : Mathf.CeilToInt(n / 2f);
            float tabW = Mathf.Max(48f, (avail - TabGap * (perRow - 1)) / perRow);
            _tabsRows = rows;
            LayoutTabs(tabW, rows, perRow);

            float tabsH = rows * TabHeight;
            var trt = _tabs;
            TwinUIStyle.Row(trt, pad, 0f, avail, tabsH);

            var tlb = _toolbar;
            TwinUIStyle.Row(tlb, pad, tabsH, avail, ToolbarHeight);
            LayoutToolbar(avail);

            float viewportTop = tabsH + ToolbarHeight;
            float viewportH = Mathf.Max(120f, areaH - viewportTop - 6f);
            TwinUIStyle.Row(_viewport, 0f, viewportTop, w, viewportH);

            _scroll.verticalNormalizedPosition = _scroll.verticalNormalizedPosition;

            if (_surface != null)
            {
                _surface.ApplyPageLayout(w, viewportH);
            }
        }

        private void LayoutTabs(float tabW, int rows, int perRow)
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                int row = rows == 1 ? 0 : i / perRow;
                int col = rows == 1 ? i : i % perRow;
                var btn = _tabButtons[i];
                var rt = (RectTransform)btn.transform;
                TwinUIStyle.Row(rt, col * (tabW + TabGap), row * TabHeight, tabW, TabHeight);
            }
        }

        private void LayoutToolbar(float width)
        {
            const float buttonY = 5f;
            float x = width;
            if (_resetButton != null)
            {
                x -= 44f;
                TwinUIStyle.Row((RectTransform)_resetButton.transform, x, buttonY, 44f, 22f);
                x -= 4f;
            }

            for (int i = _presetButtons.Count - 1; i >= 0; i--)
            {
                x -= 52f;
                TwinUIStyle.Row((RectTransform)_presetButtons[i].transform, x, buttonY, 52f, 22f);
                x -= 4f;
            }

            if (_crumb != null)
            {
                _crumb.rectTransform.sizeDelta = new Vector2(Mathf.Max(120f, x - 8f), 16f);
                _crumb.color = TwinUIStyle.QuietColor;
            }
        }

        private void OnPageHeightChanged(float height)
        {
            if (_pageContent == null)
            {
                return;
            }

            _pageContent.sizeDelta = new Vector2(0f, Mathf.Max(height, 10f));
        }

        /// <summary>
        /// fix1：自检/截图用——把页内容滚到归一化位置（1 = 顶部，0 = 底部）。
        /// 只改 ScrollRect 的滚动位置，不改布局、不改数据；用来给"首屏看不到的核心卡片"取证。
        /// </summary>
        public void SetPageScrollNormalized(float value)
        {
            if (_scroll == null)
            {
                return;
            }

            _scroll.verticalNormalizedPosition = Mathf.Clamp01(value);
        }

        /// <summary>当前页内容滚动位置（1 = 顶部，0 = 底部；无 ScrollRect 返回 -1）。</summary>
        public float PageScrollNormalized => _scroll != null ? _scroll.verticalNormalizedPosition : -1f;

        // ------------------------------------------------------------- 交互

        private void ShowTab(string tab)
        {
            if (_surface == null)
            {
                return;
            }

            _surface.SetTab(tab);
            RefreshTabSelection();
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        private void ApplyPreset(string id)
        {
            if (_surface == null)
            {
                return;
            }

            _surface.ApplyPreset(id);
            RefreshTabSelection();
            RefreshPresetSelection();
            ApplyLayout(_lastWidth, _lastHeight);
        }

        private void ResetLayout()
        {
            if (_surface != null)
            {
                _surface.ResetForCheck();
            }

            RefreshTabSelection();
            RefreshPresetSelection();
            ApplyLayout(_lastWidth, _lastHeight);
        }

        private void RefreshTabSelection()
        {
            string tab = _surface != null ? _surface.Tab : string.Empty;
            var all = TwinUiWorkspaceRegistry.All;
            for (int i = 0; i < _tabButtons.Count && i < all.Count; i++)
            {
                _tabButtons[i].SetSelected(all[i].Id == tab);
            }

            UpdateCrumb();
        }

        private void RefreshPresetSelection()
        {
            string preset = _surface != null ? _surface.Preset : string.Empty;
            for (int i = 0; i < _presetButtons.Count && i < _presetIds.Count; i++)
            {
                _presetButtons[i].SetSelected(_presetIds[i] == preset);
            }
        }

        private void UpdateCrumb()
        {
            if (_crumb == null || _surface == null)
            {
                return;
            }

            var ws = _surface.CurrentWorkspace;
            string label = ws != null ? ws.Title : _surface.Tab;
            string sub = string.IsNullOrEmpty(_surface.Sub) || _surface.Sub == "all"
                ? string.Empty
                : " / " + _surface.Sub;
            _crumb.text = "⌂ 工作台 / " + label + sub;
            _crumb.color = TwinUIStyle.QuietColor;
        }

        private void OnSurfaceChanged()
        {
            if (_taskbar != null)
            {
                _taskbar.Rebuild();
            }

            RefreshTabSelection();
            RefreshPresetSelection();
            UpdateCrumb();
        }

        private void OnMainVisibilityChanged(bool visible)
        {
            if (_taskbar != null)
            {
                _taskbar.Rebuild();
            }
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            if (_root == null)
            {
                return;
            }

            if (_page != null)
            {
                _page.ApplyThemeColors();
            }

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].ApplyTheme();
            }

            for (int i = 0; i < _presetButtons.Count; i++)
            {
                _presetButtons[i].ApplyTheme();
            }

            if (_resetButton != null)
            {
                _resetButton.ApplyTheme();
            }

            if (_taskbar != null)
            {
                _taskbar.ApplyTheme();
            }

            if (_fab != null)
            {
                _fab.ApplyTheme();
            }

            var bg = _viewport != null ? _viewport.GetComponent<Image>() : null;
            if (bg != null)
            {
                bg.color = TwinUIStyle.BgColor;
            }

            UpdateCrumb();
        }

        private void OnDestroy()
        {
            TwinUITheme.Changed -= OnThemeChanged;
            TwinUiResponsiveLayout.V2PageLayoutHook = null;
            if (_surface != null)
            {
                _surface.StateChanged -= OnSurfaceChanged;
                _surface.PageHeightChanged -= OnPageHeightChanged;
            }

            if (_window != null)
            {
                _window.VisibilityChanged -= OnMainVisibilityChanged;
            }
        }

        public string Describe()
        {
            return "shellV2 tabs=" + _tabButtons.Count + " rows=" + _tabsRows
                + " tab=" + CurrentTab + " preset=" + Preset
                + " width=" + _lastWidth.ToString("F0") + " height=" + _lastHeight.ToString("F0")
                + " viewport=" + ViewportWidth.ToString("F0") + "x" + ViewportHeight.ToString("F0")
                + " layouts=" + _layoutCount
                + " surface=[" + (_surface != null ? _surface.Describe() : "null") + "]";
        }
    }
}
