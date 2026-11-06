// TwinUiModuleHost.cs —— 模块框架宿主（P5 / task-13）
// P4 基础上新增：多模块 dock 分页（≥3 卡片）、多悬浮窗（z 序/级联错位/Esc 关闭）、24px 贴边吸附、
//              悬浮窗真正复用 TwinUiWindowFrame（P5 Frame host 抽象）。
// 职责：注册表 + 三态 Hidden/Docked/Floating + 状态持久化（TwinUI.Modules.v1）+ Header「模块」入口
//       + 底部 dock 条（UiModuleDock，挂 UiWindowRoot，不污染 content 断言）+ 悬浮窗生命周期。
// 运行时创建全部 UI，不写场景；场景里只挂本组件（UI/37 安装）。
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiModuleHost : MonoBehaviour
    {
        public const string PrefsKey = "TwinUI.Modules.v1";
        public const string Tag = "[MODULE]";
        public const float DockCardWidth = 250f;
        public const float DockGap = 8f;
        public const float DockPagerHeight = 20f;

        [Serializable]
        public class PersistedModule
        {
            public string id;
            public int state;
            public bool expanded;
            public int order;
            public float x;
            public float y;
            public float w;
            public float h;
        }

        [Serializable]
        public class PersistedState
        {
            public List<PersistedModule> modules = new List<PersistedModule>();
            public int dockPage;
        }

        private sealed class ModuleRecord
        {
            public TwinUiModule Module;
            public PersistedModule Prefs;
            public RectTransform DockCard;
            public RectTransform DockBody;
            public TwinUIButton ExpandButton;
        }

        private readonly List<ModuleRecord> _records = new List<ModuleRecord>();
        private readonly List<TwinUiModule> _modules = new List<TwinUiModule>();
        private readonly Dictionary<string, TwinUiModuleWindow> _floatingWindows =
            new Dictionary<string, TwinUiModuleWindow>(StringComparer.Ordinal);
        private TwinUiWindowHost _window;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _content;
        private RectTransform _header;
        private RectTransform _dockRoot;
        private RectTransform _dockCards;
        private RectTransform _dockPager;
        private TMP_Text _dockPageLabel;
        private TwinUIButton _dockPrev;
        private TwinUIButton _dockNext;
        private TwinUIButton _headerEntry;
        private RectTransform _menuRoot;
        private TwinUiModuleWindow _floatingWindow;
        private TwinUiModule _floatingModule;
        private bool _configured;
        private bool _builtChrome;
        private float _waitAt;
        private float _refreshTimer;
        private int _refreshCount;
        private int _stateChangeCount;
        private int _dockPage;
        private float _dockCardWidthOverride;
        private RectTransform _dockPreview;
        private TMP_Text _dockPreviewLabel;
        private RectTransform _dockGhost;
        private string _dockDragModuleId;
        private Vector2 _dockDragStartCanvas;
        private Vector2 _dockDragDeltaCanvas;
        private bool _lastDropInsideMain = true;
        private int _dockHintCount;
        private int _dockDropCount;
        private int _dockCardDragCount;

        /// <summary>自检用：临时改 dock 卡片宽度，制造"超出宽度需要翻页"的场景；0=用默认 250。</summary>
        public float DockCardWidthOverride
        {
            get => _dockCardWidthOverride;
            set
            {
                _dockCardWidthOverride = Mathf.Max(0f, value);
            }
        }

        private float CardWidth => _dockCardWidthOverride > 1f ? _dockCardWidthOverride : DockCardWidth;
        private PersistedState _prefs = new PersistedState();

        public static TwinUiModuleHost Instance { get; private set; }

        public TwinUiWindowHost WindowHost => _window;
        public IReadOnlyList<TwinUiModule> Modules => _modules;
        public RectTransform DockRoot => _dockRoot;
        public RectTransform HeaderEntryRect => _headerEntry != null
            ? (RectTransform)_headerEntry.transform
            : null;

        /// <summary>最近一次打开的悬浮窗（P4 兼容用；多浮窗请用 <see cref="FloatingWindowFor"/>）。</summary>
        public TwinUiModuleWindow FloatingWindow => _floatingWindow;
        public TwinUiModule FloatingModule => _floatingModule;
        public int FloatingCount => _floatingWindows.Count;
        public int DockPage => _dockPage;
        public int DockPageCount => Mathf.Max(1, Mathf.CeilToInt(DockedCount / (float)PerPage));
        public int DockedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _records.Count; i++)
                {
                    if (_records[i].Module.State == TwinUiModuleState.Docked)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public int PerPage
        {
            get
            {
                float width = _dockRoot != null && _dockRoot.rect.width > 1f
                    ? _dockRoot.rect.width - 32f
                    : 1000f;
                return Mathf.Max(1, Mathf.FloorToInt((width + DockGap) / (CardWidth + DockGap)));
            }
        }

        public bool MenuOpen => _menuRoot != null && _menuRoot.gameObject.activeSelf;
        public bool DockPreviewActive => _dockPreview != null && _dockPreview.gameObject.activeSelf;
        public bool DockDragGhostActive => _dockGhost != null && _dockGhost.gameObject.activeSelf;
        public int DockHintCount => _dockHintCount;
        public int DockDropCount => _dockDropCount;
        public int DockCardDragCount => _dockCardDragCount;
        public bool LastDockDropInsideMain => _lastDropInsideMain;
        public string DockDragModuleId => _dockDragModuleId;
        public bool IsConfigured => _configured;
        public int RefreshCount => _refreshCount;
        public int StateChangeCount => _stateChangeCount;

        public Rect DockRect
        {
            get
            {
                if (_dockRoot == null || !_dockRoot.gameObject.activeInHierarchy || _window == null)
                {
                    return Rect.zero;
                }

                var corners = new Vector3[4];
                _dockRoot.GetWorldCorners(corners);
                Rect rr = _window.Root.rect;
                Vector3 bl = _window.Root.InverseTransformPoint(corners[0]);
                Vector3 tr = _window.Root.InverseTransformPoint(corners[2]);
                return Rect.MinMaxRect(bl.x - rr.xMin, bl.y - rr.yMin, tr.x - rr.xMin, tr.y - rr.yMin);
            }
        }

        // ------------------------------------------------------------- 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(Tag + " duplicate module host, disabled on " + name);
                enabled = false;
                return;
            }

            Instance = this;
            if (!Application.isPlaying)
            {
                return;
            }

            LoadPrefs();
            TwinUITheme.Changed += OnThemeChanged;
        }

        private void OnDestroy()
        {
            TwinUITheme.Changed -= OnThemeChanged;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!_configured)
            {
                if (Time.unscaledTime < _waitAt)
                {
                    return;
                }

                _waitAt = Time.unscaledTime + 0.2f;
                TryConfigure();
                return;
            }

            // Esc 关闭最上层悬浮窗（取 sibling index 最大者）。
            if (_floatingWindows.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
            {
                string top = TopFloatingModuleId();
                if (!string.IsNullOrEmpty(top))
                {
                    Debug.Log(Tag + " Esc close floating id=" + top);
                    SetState(top, TwinUiModuleState.Hidden);
                }
            }

            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer < 0.25f)
            {
                return;
            }

            _refreshTimer = 0f;
            _refreshCount++;
            for (int i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                if (module.State == TwinUiModuleState.Hidden)
                {
                    continue;
                }

                module.Refresh();
            }
        }

        private void OnApplicationQuit()
        {
            SavePrefs();
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            if (_dockRoot != null)
            {
                var bg = _dockRoot.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = TwinUIStyle.Alpha(TwinUIStyle.PanelColor, 0.96f);
                }
            }

            if (_menuRoot != null)
            {
                var bg = _menuRoot.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = TwinUIStyle.Alpha(TwinUIStyle.Layer2Color, 0.98f);
                }
            }

            if (_headerEntry != null)
            {
                _headerEntry.ApplyTheme();
            }

            foreach (var pair in _floatingWindows)
            {
                if (pair.Value != null)
                {
                    pair.Value.ApplyTheme();
                }
            }
        }

        private void TryConfigure()
        {
            if (_window == null)
            {
                _window = GetComponent<TwinUiWindowHost>();
                if (_window == null)
                {
                    _window = FindObjectOfType<TwinUiWindowHost>();
                }
            }

            if (_window == null || !_window.IsReady || _window.Content == null || _window.CanvasRect == null)
            {
                return;
            }

            _canvas = _window.Canvas;
            _canvasRect = _window.CanvasRect;
            _content = _window.Content;
            _configured = true;
            Debug.Log(Tag + " host ready canvas=" + (_canvas != null ? _canvas.name : "null")
                + " content=" + _content.rect.width.ToString("F1") + "x" + _content.rect.height.ToString("F1")
                + " prefs=" + PlayerPrefs.GetString(PrefsKey, "<empty>").Length + " chars");

            RegisterBuiltInModules();
            BuildChrome();
            ApplySavedStates();
            ApplyDock();
        }

        // ------------------------------------------------------------- 注册表

        private void RegisterBuiltInModules()
        {
            var perf = FindObjectOfType<PerformanceProfiler>();
            var binder = FindObjectOfType<TwinUIStatusBinder>();
            Register(new TwinUiPerformanceModule(perf));
            Register(new TwinUiDataLinkModule(binder));
            Register(new TwinUiTaskProgressModule());
            Debug.Log(Tag + " builtin modules=" + _modules.Count + " perf=" + (perf != null)
                + " binder=" + (binder != null)
                + " disassembly=" + (DisassemblySystem.Instance != null)
                + " ids=[" + JoinIds() + "]");
        }

        private string JoinIds()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _modules.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(_modules[i].Id);
            }

            return sb.ToString();
        }

        public void Register(TwinUiModule module)
        {
            if (module == null || string.IsNullOrEmpty(module.Id) || Find(module.Id) != null)
            {
                return;
            }

            var prefs = FindPrefs(module.Id);
            if (prefs == null)
            {
                prefs = new PersistedModule
                {
                    id = module.Id,
                    state = (int)TwinUiModuleState.Hidden,
                    expanded = false,
                    order = _records.Count,
                    w = module.ExpandSize.x,
                    h = module.ExpandSize.y
                };
                _prefs.modules.Add(prefs);
            }

            module.State = (TwinUiModuleState)Mathf.Clamp(prefs.state, 0, 2);
            module.IsExpanded = prefs.expanded;
            _records.Add(new ModuleRecord { Module = module, Prefs = prefs });
            _modules.Add(module);
        }

        public TwinUiModule Find(string id)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i].Id == id)
                {
                    return _modules[i];
                }
            }

            return null;
        }

        private ModuleRecord Record(string id)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Module.Id == id)
                {
                    return _records[i];
                }
            }

            return null;
        }

        private PersistedModule FindPrefs(string id)
        {
            for (int i = 0; i < _prefs.modules.Count; i++)
            {
                if (_prefs.modules[i].id == id)
                {
                    return _prefs.modules[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------- 状态机

        public TwinUiModuleState GetState(string id)
        {
            var module = Find(id);
            return module != null ? module.State : TwinUiModuleState.Hidden;
        }

        public bool IsExpanded(string id)
        {
            var module = Find(id);
            return module != null && module.IsExpanded;
        }

        public void SetExpanded(string id, bool expanded)
        {
            var record = Record(id);
            if (record == null)
            {
                return;
            }

            record.Module.IsExpanded = expanded;
            record.Prefs.expanded = expanded;
            if (record.ExpandButton != null)
            {
                record.ExpandButton.SetLabel(expanded ? "收起" : "展开");
            }

            record.Module.OnExpandChanged(expanded);
            if (record.Module.State == TwinUiModuleState.Docked)
            {
                ApplyDock();
                record.Module.Refresh();
            }
            else if (record.Module.State == TwinUiModuleState.Floating)
            {
                var window = FloatingWindowFor(id);
                if (window != null)
                {
                    window.SetExpanded(expanded);
                    float w = expanded ? record.Module.ExpandSize.x : record.Module.CompactSize.x;
                    float h = expanded ? record.Module.ExpandSize.y : record.Module.CompactSize.y;
                    window.SetRectForCheck(new Rect(window.WindowRect.x, window.WindowRect.y,
                        Mathf.Max(w, TwinUiModuleWindow.MinWidth), Mathf.Max(h, TwinUiModuleWindow.MinHeight)));
                }
            }

            SavePrefs();
        }

        public void SetState(string id, TwinUiModuleState state)
        {
            var record = Record(id);
            if (record == null)
            {
                Debug.LogWarning(Tag + " unknown module id=" + id);
                return;
            }

            if (record.Module.State == state)
            {
                if (state == TwinUiModuleState.Floating)
                {
                    BringFloatingToFront(id);
                }

                return;
            }

            if (record.Module.State != TwinUiModuleState.Hidden)
            {
                record.Module.OnDetach();
                record.Module.DestroyView();
            }

            if (record.Module.State == TwinUiModuleState.Floating)
            {
                CloseFloating(id);
            }

            record.Module.State = state;
            record.Prefs.state = (int)state;
            _stateChangeCount++;
            CloseMenu();
            ShowDockPreview(id, false);

            switch (state)
            {
                case TwinUiModuleState.Docked:
                {
                    record.Module.IsExpanded = record.Prefs.expanded;
                    BuildDockCard(record);
                    record.Module.View = record.Module.Build(record.DockBody, false);
                    StretchView(record.Module.View);
                    TwinUiRadii.Apply(record.Module.View);
                    record.Module.OnAttach();
                    record.Module.Refresh();
                    ApplyDock();
                    break;
                }

                case TwinUiModuleState.Floating:
                {
                    record.Module.IsExpanded = true;
                    var rect = CascadeRect(record);
                    OpenFloating(record, rect);
                    var window = FloatingWindowFor(id);
                    if (window != null)
                    {
                        record.Module.View = record.Module.Build(window.Content, true);
                        StretchView(record.Module.View);
                        TwinUiRadii.Apply(record.Module.View);
                    }

                    record.Module.OnAttach();
                    record.Module.Refresh();
                    break;
                }

                default:
                    ApplyDock();
                    break;
            }

            SavePrefs();
            Debug.Log(Tag + " state id=" + id + " -> " + state + " expanded=" + record.Module.IsExpanded
                + " docked=" + DockedCount + " floating=" + FloatingCount
                + " dockPage=" + _dockPage + "/" + DockPageCount);
        }

        private static void StretchView(RectTransform view)
        {
            if (view == null)
            {
                return;
            }

            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.pivot = new Vector2(0.5f, 0.5f);
            view.offsetMin = Vector2.zero;
            view.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------- 多浮窗

        public TwinUiModuleWindow FloatingWindowFor(string id)
        {
            TwinUiModuleWindow window;
            return _floatingWindows.TryGetValue(id, out window) ? window : null;
        }

        public string TopFloatingModuleId()
        {
            string top = null;
            int best = -1;
            foreach (var pair in _floatingWindows)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                int index = pair.Value.transform.GetSiblingIndex();
                if (index > best)
                {
                    best = index;
                    top = pair.Key;
                }
            }

            return top;
        }

        public void BringFloatingToFront(string id)
        {
            var window = FloatingWindowFor(id);
            if (window != null)
            {
                window.BringToFront();
                _floatingWindow = window;
                _floatingModule = Find(id);
            }
        }

        private Rect CascadeRect(ModuleRecord record)
        {
            float w = Mathf.Max(record.Module.ExpandSize.x, TwinUiModuleWindow.MinWidth);
            float h = Mathf.Max(record.Module.ExpandSize.y, TwinUiModuleWindow.MinHeight);
            Rect rect;
            if (record.Prefs.w > 1f && record.Prefs.h > 1f && (record.Prefs.x > 1f || record.Prefs.y > 1f))
            {
                rect = new Rect(record.Prefs.x, record.Prefs.y, record.Prefs.w, record.Prefs.h);
            }
            else
            {
                rect = new Rect((_canvasRect.rect.width - w) * 0.5f, (_canvasRect.rect.height - h) * 0.5f, w, h);
            }

            // 与已打开悬浮窗重叠时级联错位，避免完全叠在一起。
            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool overlaps = false;
                foreach (var pair in _floatingWindows)
                {
                    if (pair.Value != null && pair.Value.WindowRect.Overlaps(rect))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    break;
                }

                rect = new Rect(rect.x + 34f, rect.y - 28f, rect.width, rect.height);
            }

            return rect;
        }

        private void OpenFloating(ModuleRecord record, Rect rect)
        {
            CloseFloating(record.Module.Id);
            var go = new GameObject("UiModuleWindow_" + record.Module.Id, typeof(RectTransform));
            var window = go.AddComponent<TwinUiModuleWindow>();
            string id = record.Module.Id;
            window.Configure(id, record.Module.Title, _window, _window.Frame, rect, record.Module.IsExpanded,
                () => _window.WindowRect,
                over => ShowDockPreview(id, over),
                () =>
                {
                    _dockDropCount++;
                    SetState(id, TwinUiModuleState.Docked);
                },
                () => SetState(id, TwinUiModuleState.Hidden));
            window.RectCommitted += OnFloatingRectCommitted;
            window.BringToFront();
            _floatingWindows[id] = window;
            _floatingWindow = window;
            _floatingModule = record.Module;
            Debug.Log(Tag + " floating open id=" + id + " rect=" + DescribeRect(rect)
                + " count=" + _floatingWindows.Count);
        }

        private void OnFloatingRectCommitted(TwinUiModuleWindow window)
        {
            if (window == null)
            {
                return;
            }

            var prefs = FindPrefs(window.ModuleId);
            if (prefs == null)
            {
                return;
            }

            Rect rect = window.WindowRect;
            prefs.x = rect.x;
            prefs.y = rect.y;
            prefs.w = rect.width;
            prefs.h = rect.height;
            SavePrefs();
        }

        private void CloseFloating(string id)
        {
            TwinUiModuleWindow window;
            if (!_floatingWindows.TryGetValue(id, out window))
            {
                return;
            }

            if (window != null)
            {
                _floatingWindows.Remove(id);
                window.RectCommitted -= OnFloatingRectCommitted;
                if (_floatingWindow == window)
                {
                    _floatingWindow = null;
                    _floatingModule = null;
                }

                Destroy(window.gameObject);
            }
            else
            {
                _floatingWindows.Remove(id);
            }
        }

        /// <summary>自检用：关闭最上层悬浮窗（Esc 同一条路径）。</summary>
        public bool CloseTopFloatingForCheck()
        {
            string top = TopFloatingModuleId();
            if (string.IsNullOrEmpty(top))
            {
                return false;
            }

            SetState(top, TwinUiModuleState.Hidden);
            return true;
        }

        // ------------------------------------------------------------- Dock

        // ------------------------------------------------------------- 跨窗拖拽停靠（P6-1）

        public bool CanDragDockCard(string id)
        {
            var record = Record(id);
            return record != null && record.Module.State == TwinUiModuleState.Docked;
        }

        /// <summary>悬浮窗拖动中：主窗停靠预览（半透明高亮 + "松手停靠"）。</summary>
        public void ShowDockPreview(string id, bool show)
        {
            if (show)
            {
                if (_dockPreview == null)
                {
                    BuildDockPreview();
                }

                var module = Find(id);
                if (_dockPreviewLabel != null)
                {
                    _dockPreviewLabel.text = "松手停靠 · " + (module != null ? module.Title : id);
                }

                if (!_dockPreview.gameObject.activeSelf)
                {
                    _dockPreview.gameObject.SetActive(true);
                    _dockHintCount++;
                }

                _dockPreview.SetAsLastSibling();
            }
            else if (_dockPreview != null && _dockPreview.gameObject.activeSelf)
            {
                _dockPreview.gameObject.SetActive(false);
            }
        }

        private void BuildDockPreview()
        {
            var go = TwinUIStyle.Frame(_window.Root, "UiDockPreview");
            var bg = go.gameObject.AddComponent<Image>();
            bg.color = TwinUIStyle.Alpha(TwinUIStyle.AccentColor, 0.14f);
            bg.sprite = TwinUiWindowHost.RoundedWindowSprite();
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.raycastTarget = false;
            TwinUIStyle.Outline(go.gameObject, TwinUIStyle.Alpha(TwinUIStyle.AccentColor, 0.75f));
            go.anchorMin = Vector2.zero;
            go.anchorMax = Vector2.one;
            go.offsetMin = new Vector2(16f, 16f);
            go.offsetMax = new Vector2(-16f, -16f);
            _dockPreview = go;
            _dockPreviewLabel = TwinUIStyle.Text(go, "Label", "松手停靠", 16f, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Place(_dockPreviewLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(420f, 24f));
            go.gameObject.SetActive(false);
        }

        /// <summary>dock 卡片开始拖出：显示拖拽幽灵（复用 WindowDrag 仲裁层）。</summary>
        public void BeginDockCardDrag(string id)
        {
            var record = Record(id);
            if (record == null)
            {
                return;
            }

            _dockDragModuleId = id;
            _dockDragDeltaCanvas = Vector2.zero;
            _dockDragStartCanvas = CardCanvasCenter(record);
            _dockCardDragCount++;
            EnsureDockGhost();
            PlaceDockGhost(new Rect(_dockDragStartCanvas.x - TwinUiModuleWindow.DragOutMinWidth * 0.5f,
                _dockDragStartCanvas.y - TwinUiModuleWindow.DragOutMinHeight * 0.5f,
                TwinUiModuleWindow.DragOutMinWidth, TwinUiModuleWindow.DragOutMinHeight));
            _dockGhost.SetAsLastSibling();
            _dockGhost.gameObject.SetActive(true);
            Debug.Log(Tag + " dock card drag begin id=" + id + " center=" + DescribeRect(
                new Rect(_dockDragStartCanvas.x, _dockDragStartCanvas.y, 0f, 0f)));
        }

        public void UpdateDockCardDrag(Vector2 deltaCanvas)
        {
            if (string.IsNullOrEmpty(_dockDragModuleId) || _dockGhost == null)
            {
                return;
            }

            _dockDragDeltaCanvas = deltaCanvas;
            float w = _dockGhost.rect.width;
            float h = _dockGhost.rect.height;
            var ghostRect = new Rect(_dockDragStartCanvas.x + deltaCanvas.x - w * 0.5f,
                _dockDragStartCanvas.y + deltaCanvas.y - h * 0.5f, w, h);
            PlaceDockGhost(ghostRect);
            _lastDropInsideMain = RootLocalRect.Contains(ghostRect.center);
            ShowDockPreview(_dockDragModuleId, _lastDropInsideMain);
        }

        /// <summary>松手：落点仍在主窗内 → 保持 Docked；拖出 → 转 Floating（≥480×330）。</summary>
        public void EndDockCardDrag()
        {
            string id = _dockDragModuleId;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            Rect ghost = _dockGhost != null ? GhostCanvasRect() : Rect.zero;
            bool inside = _lastDropInsideMain;
            _dockDragModuleId = null;
            if (_dockGhost != null)
            {
                _dockGhost.gameObject.SetActive(false);
            }

            ShowDockPreview(id, false);
            Debug.Log(Tag + " dock card drag end id=" + id + " insideMain=" + inside + " ghost=" + DescribeRect(ghost));
            if (inside)
            {
                return;
            }

            _dockDropCount++;
            SetState(id, TwinUiModuleState.Floating);
            var window = FloatingWindowFor(id);
            if (window != null)
            {
                window.SetRectForCheck(new Rect(ghost.x, ghost.y,
                    Mathf.Max(TwinUiModuleWindow.DragOutMinWidth, ghost.width),
                    Mathf.Max(TwinUiModuleWindow.DragOutMinHeight, ghost.height)));
            }
        }

        private void EnsureDockGhost()
        {
            if (_dockGhost != null)
            {
                return;
            }

            var go = TwinUIStyle.Frame(_window.Root, "UiDockDragGhost");
            var bg = go.gameObject.AddComponent<Image>();
            bg.color = TwinUIStyle.Alpha(TwinUIStyle.PanelColor, 0.82f);
            bg.sprite = TwinUiWindowHost.RoundedWindowSprite();
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.raycastTarget = false;
            TwinUIStyle.Outline(go.gameObject, TwinUIStyle.Alpha(TwinUIStyle.AccentColor, 0.85f));
            go.anchorMin = go.anchorMax = new Vector2(0f, 1f);
            go.pivot = new Vector2(0f, 1f);
            go.sizeDelta = new Vector2(TwinUiModuleWindow.DragOutMinWidth, TwinUiModuleWindow.DragOutMinHeight);
            var label = TwinUIStyle.Text(go, "Label", "拖出到主窗内松手停靠", 13f, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440f, 24f));
            _dockGhost = go;
            go.gameObject.SetActive(false);
        }

        private Rect RootLocalRect => _window.Root.rect;

        private Vector2 CardCanvasCenter(ModuleRecord record)
        {
            if (record.DockCard == null)
            {
                return new Vector2(240f, 120f);
            }

            Rect rr = _window.Root.rect;
            Vector3 world = record.DockCard.TransformPoint(record.DockCard.rect.center);
            Vector3 local = _window.Root.InverseTransformPoint(world);
            return new Vector2(local.x - rr.xMin, local.y - rr.yMin);
        }

        private void PlaceDockGhost(Rect rootLocalRect)
        {
            if (_dockGhost == null)
            {
                return;
            }

            float rootH = _window.Root.rect.height;
            _dockGhost.anchoredPosition = new Vector2(rootLocalRect.x,
                -(rootH - rootLocalRect.y - rootLocalRect.height));
            _dockGhost.sizeDelta = new Vector2(rootLocalRect.width, rootLocalRect.height);
        }

        /// <summary>幽灵当前矩形（根局部 → 画布坐标；根锚点即主窗左下角）。</summary>
        private Rect GhostCanvasRect()
        {
            if (_dockGhost == null)
            {
                return Rect.zero;
            }

            float rootH = _window.Root.rect.height;
            Vector2 pos = _dockGhost.anchoredPosition;
            float x = pos.x;
            float y = rootH - _dockGhost.rect.height + pos.y;
            return new Rect(_window.WindowRect.x + x, _window.WindowRect.y + y,
                _dockGhost.rect.width, _dockGhost.rect.height);
        }
        private void ApplyDock()
        {
            bool anyDocked = false;
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Module.State == TwinUiModuleState.Docked)
                {
                    anyDocked = true;
                    break;
                }
            }

            if (_dockRoot == null)
            {
                if (!anyDocked)
                {
                    return;
                }

                BuildDockRoot();
            }

            _dockRoot.gameObject.SetActive(anyDocked);
            if (!anyDocked)
            {
                return;
            }

            int perPage = PerPage;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(DockedCount / (float)perPage));
            _dockPage = Mathf.Clamp(_dockPage, 0, pageCount - 1);
            _prefs.dockPage = _dockPage;

            var pageItems = new List<ModuleRecord>();
            int dockedIndex = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var module = _records[i].Module;
                if (module.State != TwinUiModuleState.Docked)
                {
                    continue;
                }

                int page = dockedIndex / perPage;
                if (page == _dockPage)
                {
                    pageItems.Add(_records[i]);
                }

                dockedIndex++;
            }

            bool pagerVisible = pageCount > 1;
            float needed = 0f;
            for (int i = 0; i < pageItems.Count; i++)
            {
                var module = pageItems[i].Module;
                needed = Mathf.Max(needed, module.IsExpanded ? module.ExpandSize.y : module.CompactSize.y);
            }

            float pagerSpace = pagerVisible ? DockPagerHeight + 4f : 0f;
            float height = Mathf.Clamp(needed + 12f + pagerSpace, 56f,
                Mathf.Max(64f, _window.Root.rect.height * 0.6f));
            var rt = _dockRoot;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(16f, 16f);
            rt.offsetMax = new Vector2(-16f, 16f + height);

            if (_dockPager != null)
            {
                _dockPager.gameObject.SetActive(pagerVisible);
            }

            if (_dockCards != null)
            {
                _dockCards.offsetMin = new Vector2(0f, pagerVisible ? DockPagerHeight + 2f : 2f);
                _dockCards.offsetMax = Vector2.zero;
            }

            float gap = DockGap;
            for (int i = 0; i < _records.Count; i++)
            {
                var card = _records[i].DockCard;
                if (card != null && !pageItems.Contains(_records[i]))
                {
                    card.gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < pageItems.Count; i++)
            {
                var card = pageItems[i].DockCard;
                if (card == null)
                {
                    continue;
                }

                card.gameObject.SetActive(true);
                card.SetAsLastSibling();
                card.anchorMin = new Vector2(0f, 0f);
                card.anchorMax = new Vector2(0f, 1f);
                card.pivot = new Vector2(0f, 0.5f);
                card.anchoredPosition = new Vector2(8f + i * (CardWidth + gap), 0f);
                card.sizeDelta = new Vector2(CardWidth, -(pagerVisible ? DockPagerHeight + 6f : 6f));
            }

            if (pagerVisible)
            {
                UpdatePager(pageCount);
            }
        }

        private void UpdatePager(int pageCount)
        {
            if (_dockPageLabel != null)
            {
                _dockPageLabel.text = (_dockPage + 1) + " / " + pageCount;
            }

            if (_dockPrev != null)
            {
                _dockPrev.SetInteractable(_dockPage > 0);
            }

            if (_dockNext != null)
            {
                _dockNext.SetInteractable(_dockPage < pageCount - 1);
            }
        }

        public void SetDockPage(int page)
        {
            _dockPage = Mathf.Max(0, page);
            _prefs.dockPage = _dockPage;
            ApplyDock();
            SavePrefs();
            Debug.Log(Tag + " dock page=" + _dockPage + "/" + DockPageCount);
        }

        private void BuildDockRoot()
        {
            var rootGo = TwinUIStyle.Frame(_window.Root, "UiModuleDock");
            var bg = rootGo.gameObject.AddComponent<Image>();
            bg.color = TwinUIStyle.Alpha(TwinUIStyle.PanelColor, 0.96f);
            bg.sprite = TwinUiWindowHost.RoundedWindowSprite();
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            TwinUIStyle.Outline(rootGo.gameObject, TwinUIStyle.BorderColor);
            _dockRoot = rootGo;
            rootGo.SetAsLastSibling();

            var cards = TwinUIStyle.Frame(_dockRoot, "Cards");
            cards.anchorMin = Vector2.zero;
            cards.anchorMax = Vector2.one;
            cards.pivot = new Vector2(0.5f, 0.5f);
            cards.offsetMin = new Vector2(0f, 2f);
            cards.offsetMax = Vector2.zero;
            _dockCards = cards;

            var pager = TwinUIStyle.Frame(_dockRoot, "Pager");
            pager.anchorMin = new Vector2(1f, 0f);
            pager.anchorMax = new Vector2(1f, 0f);
            pager.pivot = new Vector2(1f, 0f);
            pager.anchoredPosition = new Vector2(-8f, 3f);
            pager.sizeDelta = new Vector2(150f, DockPagerHeight - 4f);
            _dockPager = pager;

            _dockPrev = TwinUIStyle.Button(pager, "BtnPrev", "‹", TwinUIButton.Kind.Plain,
                new Vector2(0f, 0f), new Vector2(26f, DockPagerHeight - 4f),
                () => SetDockPage(_dockPage - 1));
            PlacePagerButton(_dockPrev, 0f);
            _dockPageLabel = TwinUIStyle.Mono(pager, "PageLabel", "1 / 1", 10f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Place(_dockPageLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(30f, 0f), new Vector2(86f, DockPagerHeight - 4f));
            _dockNext = TwinUIStyle.Button(pager, "BtnNext", "›", TwinUIButton.Kind.Plain,
                new Vector2(0f, 0f), new Vector2(26f, DockPagerHeight - 4f),
                () => SetDockPage(_dockPage + 1));
            PlacePagerButton(_dockNext, 120f);

            TwinUiRadii.Apply(_dockRoot);
            Debug.Log(Tag + " dock root created (with pager) radius cards=" + TwinUiRadii.AppliedCards
                + " controls=" + TwinUiRadii.AppliedControls);
        }

        private static void PlacePagerButton(TwinUIButton button, float x)
        {
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(26f, DockPagerHeight - 4f);
        }

        private void BuildDockCard(ModuleRecord record)
        {
            if (_dockRoot == null)
            {
                BuildDockRoot();
            }

            if (record.DockCard == null)
            {
                record.DockCard = TwinUIStyle.Frame(_dockCards, "Card_" + record.Module.Id);

                var label = TwinUIStyle.Text(record.DockCard, "Title", record.Module.Title, 12f,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f, -4f), new Vector2(150f, 16f));

                record.ExpandButton = TwinUIStyle.Button(record.DockCard, "BtnExpand",
                    record.Module.IsExpanded ? "收起" : "展开", TwinUIButton.Kind.Default,
                    new Vector2(0f, 0f), new Vector2(46f, 20f),
                    () => SetExpanded(record.Module.Id, !record.Module.IsExpanded));
                var btnRt = (RectTransform)record.ExpandButton.transform;
                btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 1f);
                btnRt.pivot = new Vector2(1f, 1f);
                btnRt.anchoredPosition = new Vector2(-6f, -2f);

                var closeBtn = TwinUIStyle.Button(record.DockCard, "BtnHide", "×",
                    TwinUIButton.Kind.Default, new Vector2(0f, 0f), new Vector2(20f, 20f),
                    () => SetState(record.Module.Id, TwinUiModuleState.Hidden));
                var closeRt = (RectTransform)closeBtn.transform;
                closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 1f);
                closeRt.pivot = new Vector2(1f, 1f);
                closeRt.anchoredPosition = new Vector2(-56f, -2f);

                record.DockBody = TwinUIStyle.Frame(record.DockCard, "Body");
                record.DockBody.anchorMin = Vector2.zero;
                record.DockBody.anchorMax = Vector2.one;
                record.DockBody.pivot = new Vector2(0.5f, 0.5f);
                record.DockBody.offsetMin = new Vector2(4f, 4f);
                record.DockBody.offsetMax = new Vector2(-4f, -22f);
            }

            if (record.DockCard.Find("DragSurface") == null)
            {
                var surface = TwinUIStyle.Panel(record.DockCard, "DragSurface", TwinUIStyle.Transparent, true);
                surface.rectTransform.anchorMin = Vector2.zero;
                surface.rectTransform.anchorMax = Vector2.one;
                surface.rectTransform.offsetMin = Vector2.zero;
                surface.rectTransform.offsetMax = Vector2.zero;
                surface.transform.SetAsFirstSibling();
            }

            if (record.DockCard.GetComponent<TwinUiDockCardDrag>() == null)
            {
                record.DockCard.gameObject.AddComponent<TwinUiDockCardDrag>()
                    .Configure(this, record.Module.Id, record.DockCard);
            }

            if (record.ExpandButton != null)
            {
                record.ExpandButton.SetLabel(record.Module.IsExpanded ? "收起" : "展开");
            }
        }

        // ------------------------------------------------------------- Header 入口 / 菜单

        private void BuildChrome()
        {
            if (_builtChrome || _content == null)
            {
                return;
            }

            _header = _content.Find("Header") as RectTransform;
            if (_header != null)
            {
                _headerEntry = TwinUIStyle.Button(_header, "BtnModules", "模块", TwinUIButton.Kind.Default,
                    Vector2.zero, new Vector2(88f, 28f), ToggleMenu);
                var rt = (RectTransform)_headerEntry.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-104f, 0f);
            }
            else
            {
                Debug.LogWarning(Tag + " Header not found; module entry button skipped");
            }

            BuildMenu();
            _builtChrome = true;
        }

        private void BuildMenu()
        {
            if (_menuRoot != null)
            {
                return;
            }

            var go = TwinUIStyle.Frame(_window.Root, "UiModuleMenu");
            var bg = go.gameObject.AddComponent<Image>();
            bg.color = TwinUIStyle.Alpha(TwinUIStyle.Layer2Color, 0.98f);
            bg.sprite = TwinUiWindowHost.RoundedWindowSprite();
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            TwinUIStyle.Outline(go.gameObject, TwinUIStyle.BorderColor);
            go.anchorMin = go.anchorMax = new Vector2(1f, 1f);
            go.pivot = new Vector2(1f, 1f);
            go.sizeDelta = new Vector2(310f, 44f + _modules.Count * 32f);
            go.anchoredPosition = new Vector2(-16f, -70f);
            go.SetAsLastSibling();
            _menuRoot = go;

            var title = TwinUIStyle.Text(go, "Title", "模块", 13f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -6f), new Vector2(160f, 20f));

            var hint = TwinUIStyle.Text(go, "Hint", "停靠=主窗 dock / 悬浮=独立窗口（可拖动缩放）", 10f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -22f), new Vector2(280f, 14f));

            for (int i = 0; i < _records.Count; i++)
            {
                BuildMenuRow(_records[i], 36f + i * 32f);
            }

            _menuRoot.gameObject.SetActive(false);
        }

        private void BuildMenuRow(ModuleRecord record, float y)
        {
            var row = TwinUIStyle.Frame(_menuRoot, "Row_" + record.Module.Id);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-16f, 28f);
            row.anchoredPosition = new Vector2(0f, -y);

            var label = TwinUIStyle.Text(row, "Label", record.Module.Title, 12f,
                TwinUIStyle.TextColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(2f, 0f), new Vector2(118f, 18f));

            AddMenuButton(row, "停靠", new Vector2(120f, 0f), 44f,
                () => SetState(record.Module.Id, TwinUiModuleState.Docked));
            AddMenuButton(row, "悬浮", new Vector2(168f, 0f), 44f,
                () => SetState(record.Module.Id, TwinUiModuleState.Floating));
            AddMenuButton(row, "隐藏", new Vector2(216f, 0f), 44f,
                () => SetState(record.Module.Id, TwinUiModuleState.Hidden));
        }

        private static void AddMenuButton(RectTransform row, string label, Vector2 pos, float width,
            UnityEngine.Events.UnityAction action)
        {
            var button = TwinUIStyle.Button(row, "Btn" + label, label, TwinUIButton.Kind.Default, pos,
                new Vector2(width, 24f), action);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
        }

        public void ToggleMenu()
        {
            if (_menuRoot == null)
            {
                return;
            }

            bool open = !_menuRoot.gameObject.activeSelf;
            _menuRoot.gameObject.SetActive(open);
            if (open)
            {
                _menuRoot.SetAsLastSibling();
                if (_headerEntry != null)
                {
                    _headerEntry.SetSelected(true);
                }
            }
            else if (_headerEntry != null)
            {
                _headerEntry.SetSelected(false);
            }
        }

        public void CloseMenu()
        {
            if (_menuRoot != null && _menuRoot.gameObject.activeSelf)
            {
                _menuRoot.gameObject.SetActive(false);
                if (_headerEntry != null)
                {
                    _headerEntry.SetSelected(false);
                }
            }
        }

        // ------------------------------------------------------------- 持久化

        private void LoadPrefs()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                _prefs = new PersistedState();
                return;
            }

            try
            {
                _prefs = JsonUtility.FromJson<PersistedState>(json) ?? new PersistedState();
                if (_prefs.modules == null)
                {
                    _prefs.modules = new List<PersistedModule>();
                }

                _dockPage = Mathf.Max(0, _prefs.dockPage);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " prefs parse failed: " + e.Message);
                _prefs = new PersistedState();
            }
        }

        public void SavePrefs()
        {
            try
            {
                foreach (var pair in _floatingWindows)
                {
                    if (pair.Value == null)
                    {
                        continue;
                    }

                    var prefs = FindPrefs(pair.Value.ModuleId);
                    if (prefs != null)
                    {
                        var rect = pair.Value.WindowRect;
                        prefs.x = rect.x;
                        prefs.y = rect.y;
                        prefs.w = rect.width;
                        prefs.h = rect.height;
                    }
                }

                _prefs.dockPage = _dockPage;
                PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_prefs));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " prefs save failed: " + e.Message);
            }
        }

        private void ApplySavedStates()
        {
            for (int i = 0; i < _records.Count; i++)
            {
                var record = _records[i];
                if (record.Module.State == TwinUiModuleState.Hidden)
                {
                    continue;
                }

                var state = record.Module.State;
                record.Module.State = TwinUiModuleState.Hidden;
                SetState(record.Module.Id, state);
            }
        }

        /// <summary>自检用：全部模块回到 Hidden（不保存）。</summary>
        public void ResetPrefsForCheck()
        {
            _prefs = new PersistedState();
            _dockPage = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var record = _records[i];
                if (record.Module.State == TwinUiModuleState.Floating)
                {
                    CloseFloating(record.Module.Id);
                }

                if (record.Module.State != TwinUiModuleState.Hidden)
                {
                    record.Module.OnDetach();
                    record.Module.DestroyView();
                }

                record.Module.State = TwinUiModuleState.Hidden;
                record.Module.IsExpanded = false;
                record.Prefs = new PersistedModule
                {
                    id = record.Module.Id,
                    state = (int)TwinUiModuleState.Hidden,
                    expanded = false,
                    order = i,
                    w = record.Module.ExpandSize.x,
                    h = record.Module.ExpandSize.y
                };
                _prefs.modules.Add(record.Prefs);
            }

            ApplyDock();
            SavePrefs();
        }

        private static string DescribeRect(Rect r)
        {
            return "(" + r.x.ToString("F1") + "," + r.y.ToString("F1") + ","
                + r.width.ToString("F1") + "x" + r.height.ToString("F1") + ")";
        }
    }
}