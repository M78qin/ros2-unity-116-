// TwinUiSurfaceHost.cs —— V2 卡片/浮窗/任务栏的表面管理器（P8）
// 职责（单一写者）：模块注册表 + 三态（Card / Floating / Hidden）+ 单实例内容视图 +
//                   页内卡片托管 + 浮窗生命周期 + 布局持久化（TwinUI.Shell.v2）。
// 关键设计：
//   * 一个模块只有**一份内容视图**：卡片态挂在卡片内容区，拆出时 SetParent 到浮窗内容区，
//     归位时再挂回来。不重建、不重复订阅、不重复绘制。
//   * 浮窗复用 TwinUiModuleWindow（同一套 TwinUiWindowFrame 外壳），V2 起支持最大化。
//   * 数据只读：本类不订阅 ROS2 / 不写 CSV / 不下发命令。
//   * Legacy 模式（TwinUiShellMode.Legacy）下本类不创建，旧 TwinUiModuleHost 行为完全不变。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiSurfaceHost : MonoBehaviour
    {
        public const string Tag = "[SURFACE]";
        public const float RefreshInterval = 0.25f;

        public static TwinUiSurfaceHost Instance { get; private set; }

        private readonly Dictionary<string, TwinUiModule> _modules = new Dictionary<string, TwinUiModule>(StringComparer.Ordinal);
        private readonly List<TwinUiModule> _moduleOrder = new List<TwinUiModule>();
        private readonly Dictionary<string, TwinUiCardView> _cards = new Dictionary<string, TwinUiCardView>(StringComparer.Ordinal);
        private readonly Dictionary<string, TwinUiModuleWindow> _floating = new Dictionary<string, TwinUiModuleWindow>(StringComparer.Ordinal);
        private readonly HashSet<string> _minimized = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _pinned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _attached = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _stacking = new List<string>();
        private readonly Dictionary<string, TwinUiWidgetPref> _prefs = new Dictionary<string, TwinUiWidgetPref>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _savedFloatingStates = new Dictionary<string, int>(StringComparer.Ordinal);
        private RectTransform _floatingLayer;

        private TwinUiWindowHost _window;
        private TwinUiPageView _page;
        private TwinUiShellLayoutData _data;
        private string _tab = TwinUiWorkspaceRegistry.Overview;
        private string _sub = string.Empty;
        private float _pageWidth = 1080f;
        private float _pageHeight = 900f;
        private float _refreshTimer;
        private int _refreshCount;
        private int _detachCount;
        private int _dockBackCount;
        private int _invalidationRelayoutCount;

        // 本批（回放 UI）：数字孪生页顶部连接状态条（源类型/连接/新鲜度/更新时间）。
        // 只读：binder 的快照；不订阅、不写记录。0.5 s 刷新一次，文本变化不触发重排。
        private const float StatusInterval = 0.5f;
        private TwinUIStatusBinder _statusBinder;
        private float _statusBinderScanAt;
        private float _statusNextAt;
        private int _statusUpdates;

        // P11：内容测量失效轮询（模块 LayoutVersion vs 上次排版时记录的值）。
        // 只比 int，不遍历控件树；变化时才重排一次页面。
        private readonly Dictionary<string, int> _layoutVersions = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _configured;

        public event Action StateChanged;

        /// <summary>页面内容总高度变化（TwinUiShellV2 用它设置 ScrollRect 内容尺寸）。</summary>
        public event Action<float> PageHeightChanged;

        public string Tab => _tab;
        public string Sub => _sub;
        public string Preset => _data != null ? _data.preset : TwinUiLayoutPresets.Default;
        public TwinUiWorkspace CurrentWorkspace => TwinUiWorkspaceRegistry.Find(_tab);
        public IReadOnlyList<TwinUiModule> Modules => _moduleOrder;
        public int ModuleCount => _moduleOrder.Count;
        public int CardCount => _cards.Count;
        public int FloatingCount => _floating.Count;
        public RectTransform FloatingLayer => _floatingLayer;
        public int SavedFloatingCount => _savedFloatingStates.Count;
        public int MinimizedCount => _minimized.Count;
        public int PinnedCount => _pinned.Count;
        public int RefreshCount => _refreshCount;
        public int DetachCount => _detachCount;
        public int DockBackCount => _dockBackCount;

        /// <summary>P11：因内容测量失效而触发的整页重排次数（自检证据；空闲时应为 0 增量）。</summary>
        public int InvalidationRelayoutCount => _invalidationRelayoutCount;

        /// <summary>连接状态条更新次数（可见状态变化；自检证据）。</summary>
        public int StatusUpdateCount => _statusUpdates;

        public string ConnectionStatusText => _page != null ? _page.ConnectionStatusText : string.Empty;

        public bool ConnectionStatusVisible => _page != null && _page.ConnectionStatusVisible;
        public bool IsConfigured => _configured;
        public TwinUiPageView Page => _page;
        public RectTransform PageRoot => _page != null ? _page.Root : null;
        public float PageHeight => _pageHeight;
        public float PageWidth => _pageWidth;

        public static TwinUiModule FindModule(string id)
        {
            return Instance != null ? Instance.Find(id) : null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_data != null)
            {
                SavePrefs();
            }
        }

        // ------------------------------------------------------------- 装配

        public void Configure(TwinUiWindowHost window, TwinUiPageView page)
        {
            _window = window;
            _page = page;
            _data = TwinUiShellPrefs.Load();
            _tab = TwinUiWorkspaceRegistry.Find(_data.tab) != null ? _data.tab : TwinUiWorkspaceRegistry.Overview;
            _sub = TwinUiPageView.DefaultSub(TwinUiWorkspaceRegistry.Find(_tab));
            for (int i = 0; i < _data.widgets.Count; i++)
            {
                var p = _data.widgets[i];
                if (p != null && !string.IsNullOrEmpty(p.id))
                {
                    _prefs[p.id] = p;
                    if (p.state == (int)TwinUiModuleState.Floating)
                    {
                        // P3：启动默认全部停靠。只记住"上次是浮窗"的状态，供显式恢复；
                        // 本次启动不自动拆出任何卡片。
                        _savedFloatingStates[p.id] = p.state;
                        p.state = (int)TwinUiModuleState.Card;
                    }

                    if (p.pinned)
                    {
                        _pinned.Add(p.id);
                    }
                }
            }

            EnsureFloatingLayer();
            _configured = true;
            _layoutVersions.Clear();
            _invalidationRelayoutCount = 0;
            TwinUiViewFreeze.Reset();
            _statusBinder = null;
            _statusBinderScanAt = 0f;
            _statusNextAt = 0f;
            _statusUpdates = 0;
            Debug.Log(Tag + " configured tab=" + _tab + " modules=" + _moduleOrder.Count
                + " savedWidgets=" + _prefs.Count + " savedFloating=" + _savedFloatingStates.Count
                + " autoRestore=off migrated=" + TwinUiShellPrefs.LastMigratedFromV1
                + " loadFailed=" + TwinUiShellPrefs.LastLoadFailed);
        }

        /// <summary>浮窗层：主窗之上、任务栏/悬浮球之下；由 Configure 创建一次。</summary>
        private void EnsureFloatingLayer()
        {
            if (_floatingLayer != null || _window == null || _window.CanvasRect == null)
            {
                return;
            }

            var go = new GameObject("UiFloatingLayer", typeof(RectTransform));
            _floatingLayer = go.GetComponent<RectTransform>();
            _floatingLayer.SetParent(_window.CanvasRect, false);
            _floatingLayer.anchorMin = Vector2.zero;
            _floatingLayer.anchorMax = Vector2.one;
            _floatingLayer.pivot = new Vector2(0.5f, 0.5f);
            _floatingLayer.offsetMin = Vector2.zero;
            _floatingLayer.offsetMax = Vector2.zero;
            _floatingLayer.SetAsLastSibling();
            TwinUiTooltip.HideAll();
        }

        public void Register(TwinUiModule module)
        {
            if (module == null || string.IsNullOrEmpty(module.Id) || _modules.ContainsKey(module.Id))
            {
                return;
            }

            _modules[module.Id] = module;
            _moduleOrder.Add(module);
            module.State = TwinUiModuleState.Hidden;
            module.Surface = TwinUiModuleSurface.Card;
        }

        public TwinUiModule Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            TwinUiModule module;
            return _modules.TryGetValue(id, out module) ? module : null;
        }

        // ------------------------------------------------------------- 页面

        /// <summary>启动时按存档恢复页签；浮窗默认不恢复（P3：所有卡片默认停靠在主工作台）。</summary>
        public void BootstrapPage()
        {
            RebuildPage();
            Debug.Log(Tag + " bootstrap tab=" + _tab + " floating=" + _floating.Count
                + " savedFloating=" + _savedFloatingStates.Count + " autoRestore=off"
                + " cards=" + _cards.Count + " pageH=" + _pageHeight.ToString("F0"));
        }

        /// <summary>
        /// 显式恢复"上次的浮窗布局"（只由菜单/工具栏的明确动作调用，不在启动时自动执行）。
        /// 返回恢复的浮窗数。
        /// </summary>
        public int RestoreLastFloatingLayout()
        {
            var ids = new List<string>(_savedFloatingStates.Keys);
            int restored = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var module = Find(ids[i]);
                if (module != null && module.State != TwinUiModuleState.Floating)
                {
                    Detach(ids[i], "restore-saved");
                    restored++;
                }
            }

            Debug.Log(Tag + " restoreSavedFloating requested=" + ids.Count + " restored=" + restored);
            return restored;
        }

        public void SetTab(string tab, bool save = true)
        {
            var ws = TwinUiWorkspaceRegistry.Find(tab);
            if (ws == null)
            {
                ws = TwinUiWorkspaceRegistry.Find(TwinUiWorkspaceRegistry.Overview);
            }

            if (ws == null)
            {
                return;
            }

            bool changed = _tab != ws.Id;
            _tab = ws.Id;
            // 切页本身已触发整页重排；清空版本基线，避免把"离页期间"发生的
            // LayoutVersion 变化在回页时再计一次失效重排（会在 UI/57 造成 delta=2）。
            _layoutVersions.Clear();
            _sub = TwinUiPageView.DefaultSub(ws);
            RebuildPage();
            if (changed)
            {
                DetachAttachedNotOnPage();
            }

            if (save)
            {
                SavePrefs();
            }

            StateChanged?.Invoke();
            Debug.Log(Tag + " tab=" + _tab + " cards=" + _cards.Count + " pageH=" + _pageHeight.ToString("F0"));
        }

        public void SetSub(string sub)
        {
            _sub = string.IsNullOrEmpty(sub) ? _sub : sub;
            RebuildPage();
            SavePrefs();
            StateChanged?.Invoke();
        }

        private void RebuildPage()
        {
            if (_page == null)
            {
                return;
            }

            var ws = TwinUiWorkspaceRegistry.Find(_tab);
            if (ws == null)
            {
                return;
            }

            _page.SetWorkspace(ws, _sub, SetSub);
            _page.ClearCards();

            foreach (var card in _cards.Values)
            {
                if (card != null)
                {
                    card.gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < ws.WidgetIds.Length; i++)
            {
                string id = ws.WidgetIds[i];
                var module = Find(id);
                if (module == null)
                {
                    continue;
                }

                if (_hidden.Contains(id))
                {
                    continue;
                }

                var card = EnsureCard(module);
                bool floating = module.State == TwinUiModuleState.Floating;
                card.SetPlaceholder(floating, "已在浮窗中显示；点击图钉或关闭浮窗即回到此卡位");
                card.SetFloatingState(floating);
                if (floating)
                {
                    continue;
                }

                module.State = TwinUiModuleState.Card;
                module.Surface = TwinUiModuleSurface.Card;
                _page.AttachCard(module, card);
                EnsureAttached(module);
            }

            ApplyPageLayout(_pageWidth, _pageHeight);
        }

        /// <summary>按可用宽高重排页面；由 TwinUiShellV2 在窗口尺寸变化时调用。</summary>
        public void ApplyPageLayout(float width, float height)
        {
            if (_page == null)
            {
                return;
            }

            _pageWidth = Mathf.Max(320f, width);
            _page.Apply(_pageWidth);
            _pageHeight = _page.ContentHeight;
            PageHeightChanged?.Invoke(Mathf.Max(_pageHeight, height));
        }

        // ------------------------------------------------------------- 卡片

        private TwinUiCardView EnsureCard(TwinUiModule module)
        {
            TwinUiCardView card;
            if (_cards.TryGetValue(module.Id, out card) && card != null)
            {
                return card;
            }

            card = TwinUiCardView.Build(_page.CardArea, module, ToggleFloating, Detach);
            if (_window != null && _window.Root != null)
            {
                card.SetMainWindowRect(_window.Root);
            }
            _cards[module.Id] = card;
            if (module.View == null)
            {
                module.Surface = TwinUiModuleSurface.Card;
                var view = module.Build(card.Content, TwinUiModuleSurface.Card);
                if (view != null)
                {
                    ResetToCardLayout(view);
                }
            }
            else if (module.View.parent != card.Content)
            {
                module.View.SetParent(card.Content, false);
                ResetToCardLayout(module.View);
            }

            module.State = TwinUiModuleState.Card;
            return card;
        }

        private void EnsureAttached(TwinUiModule module)
        {
            if (module == null || !_attached.Add(module.Id))
            {
                return;
            }

            module.OnAttach();
            module.HostAttachCount++;
        }

        private void EnsureDetached(string id)
        {
            if (!_attached.Remove(id))
            {
                return;
            }

            var module = Find(id);
            if (module != null)
            {
                module.OnDetach();
            }
        }

        private void DetachAttachedNotOnPage()
        {
            var ws = TwinUiWorkspaceRegistry.Find(_tab);
            var buffer = new List<string>();
            foreach (string id in _attached)
            {
                bool onPage = false;
                if (ws != null)
                {
                    for (int i = 0; i < ws.WidgetIds.Length; i++)
                    {
                        if (ws.WidgetIds[i] == id)
                        {
                            onPage = true;
                            break;
                        }
                    }
                }

                if (!onPage)
                {
                    buffer.Add(id);
                }
            }

            for (int i = 0; i < buffer.Count; i++)
            {
                EnsureDetached(buffer[i]);
            }
        }

        // ------------------------------------------------------------- 浮窗

        /// <summary>卡片 → 浮窗（同一份内容视图换父节点）。</summary>
        public void Detach(string id)
        {
            Detach(id, "legacy");
        }

        /// <summary>卡片 → 浮窗；reason 只用于日志（pin/preset/capture/test），不参与业务。</summary>
        public void Detach(string id, string reason)
        {
            var module = Find(id);
            if (module == null || !module.Detachable)
            {
                return;
            }

            if (module.State == TwinUiModuleState.Floating)
            {
                BringToFront(id);
                return;
            }

            var card = EnsureCard(module);
            Rect rect = FloatingRectFor(module);
            var win = CreateFloatingWindow(module, rect);
            if (win == null)
            {
                return;
            }

            RectTransform view = module.View;
            if (view == null)
            {
                module.Surface = TwinUiModuleSurface.Floating;
                view = module.Build(win.Content, TwinUiModuleSurface.Floating);
            }
            else
            {
                view.SetParent(win.Content, false);
                module.Surface = TwinUiModuleSurface.Floating;
            }

            if (view != null)
            {
                TwinUiContentFit.Attach(module);
                var fit = view.GetComponent<TwinUiContentFit>();
                if (fit != null) fit.Invalidate();
                // P10：响应式模块在浮窗里拉伸并按宽度重排（禁止整卡缩放）；Legacy 模块保持原缩放路径。
                if (module.ResponsiveContent || fit != null)
                {
                    var legacyScaler = view.GetComponent<TwinUiDesignScaler>();
                    if (legacyScaler != null)
                    {
                        Destroy(legacyScaler);
                    }

                    view.localScale = Vector3.one;
                    TwinUIStyle.Stretch(view, TwinUiCardView.ContentPadX, TwinUiCardView.ContentPadX,
                        TwinUiCardView.ContentPadX, TwinUiCardView.ContentPadX);
                    module.ApplyContentLayout(FloatingContentWidth(module), TwinUiModuleSurface.Floating);
                }
                else
                {
                    // 浮窗里按设计尺寸等比缩放并居中（卡片态由 Grid 统一缩放）。
                    var scaler = view.GetComponent<TwinUiDesignScaler>();
                    if (scaler == null)
                    {
                        scaler = view.gameObject.AddComponent<TwinUiDesignScaler>();
                    }

                    scaler.Configure(new Vector2(TwinUiCardGrid.DesignWidth, Mathf.Max(160f, module.CardSize.y)),
                        win.Content);
                }
            }

            module.State = TwinUiModuleState.Floating;
            module.Surface = TwinUiModuleSurface.Floating;
            module.OnSurfaceChanged(TwinUiModuleSurface.Floating);
            card.SetPlaceholder(true, "已在浮窗中显示；关闭浮窗即回到此卡位");
            EnsureAttached(module);
            _hidden.Remove(id);
            _detachCount++;
            _stacking.Remove(id);
            _stacking.Add(id);
            _minimized.Remove(id);
            win.gameObject.SetActive(true);
            win.BringToFront();
            ReapplyPageLayout();
            SavePrefs();
            StateChanged?.Invoke();
            Debug.Log(Tag + " detach id=" + id + " reason=" + (reason ?? "unknown")
                + " rect=" + DescribeRect(win.WindowRect)
                + " floating=" + _floating.Count);
        }

        /// <summary>浮窗 → 卡片（关闭浮窗 = 归位，不销毁内容）。</summary>
        public void DockBack(string id)
        {
            TwinUiModuleWindow win;
            if (!_floating.TryGetValue(id, out win))
            {
                return;
            }

            var module = Find(id);
            _floating.Remove(id);
            _minimized.Remove(id);
            _stacking.Remove(id);
            if (win != null)
            {
                win.RectCommitted -= OnFloatingRectCommitted;
            }

            if (module != null)
            {
                var card = EnsureCard(module);
                if (module.View != null)
                {
                    module.View.SetParent(card.Content, false);
                    ResetToCardLayout(module.View);
                }

                card.SetPlaceholder(false, null);
                card.SetFloatingState(false);
                module.State = TwinUiModuleState.Card;
                module.Surface = TwinUiModuleSurface.Card;
                module.OnSurfaceChanged(TwinUiModuleSurface.Card);
            }

            if (win != null)
            {
                Destroy(win.gameObject);
            }

            _dockBackCount++;
            RebuildPage();
            SavePrefs();
            StateChanged?.Invoke();
            Debug.Log(Tag + " dockBack id=" + id + " floating=" + _floating.Count);
        }

        public void MinimizeFloating(string id)
        {
            TwinUiModuleWindow win;
            if (!_floating.TryGetValue(id, out win) || win == null)
            {
                return;
            }

            _minimized.Add(id);
            win.gameObject.SetActive(false);
            SavePrefs();
            StateChanged?.Invoke();
            Debug.Log(Tag + " minimizeFloating id=" + id + " minimized=" + _minimized.Count);
        }

        public void RestoreFloating(string id)
        {
            TwinUiModuleWindow win;
            if (!_floating.TryGetValue(id, out win) || win == null)
            {
                return;
            }

            _minimized.Remove(id);
            win.gameObject.SetActive(true);
            win.BringToFront();
            BringToFront(id);
            SavePrefs();
            StateChanged?.Invoke();
        }

        public bool IsMinimizedFloating(string id)
        {
            return _minimized.Contains(id);
        }

        public bool IsPinned(string id)
        {
            return _pinned.Contains(id);
        }

        public void TogglePin(string id)
        {
            if (!_pinned.Add(id))
            {
                _pinned.Remove(id);
            }

            SavePrefs();
            StateChanged?.Invoke();
        }

        /// <summary>图钉唯一入口：停靠 → 独立悬浮；悬浮 → 恢复停靠。只影响当前卡片。</summary>
        public void ToggleFloating(string id)
        {
            var module = Find(id);
            if (module == null || !module.Detachable)
            {
                return;
            }

            if (module.State == TwinUiModuleState.Floating)
            {
                DockBack(id);
            }
            else
            {
                Detach(id, "pin");
            }
        }

        public bool IsFloating(string id)
        {
            var module = Find(id);
            return module != null && module.State == TwinUiModuleState.Floating;
        }

        public void ToggleMaximizeFloating(string id)
        {
            TwinUiModuleWindow win;
            if (_floating.TryGetValue(id, out win) && win != null)
            {
                win.ToggleMaximizedWindow();
            }
        }

        public bool IsFloatingMaximized(string id)
        {
            TwinUiModuleWindow win;
            return _floating.TryGetValue(id, out win) && win != null && win.IsMaximized;
        }

        public void BringToFront(string id)
        {
            TwinUiModuleWindow win;
            if (!_floating.TryGetValue(id, out win) || win == null)
            {
                return;
            }

            win.BringToFront();
            _stacking.Remove(id);
            _stacking.Add(id);
            StateChanged?.Invoke();
        }

        public string TopFloatingId()
        {
            for (int i = _stacking.Count - 1; i >= 0; i--)
            {
                if (_floating.ContainsKey(_stacking[i]))
                {
                    return _stacking[i];
                }
            }

            return string.Empty;
        }

        public TwinUiModuleWindow FloatingWindowFor(string id)
        {
            TwinUiModuleWindow win;
            return _floating.TryGetValue(id, out win) ? win : null;
        }

        /// <summary>自检用：与 Esc 同一路径关闭最上层未固定浮窗（不依赖物理按键注入）。</summary>
        public bool CloseTopFloatingForCheck()
        {
            string top = TopFloatingId();
            if (string.IsNullOrEmpty(top))
            {
                return false;
            }

            Debug.Log(Tag + " Esc-close top=" + top);
            DockBack(top);
            return true;
        }

        /// <summary>自检用：取卡片外壳（未创建返回 null）。</summary>
        public TwinUiCardView CardFor(string id)
        {
            TwinUiCardView card;
            return _cards.TryGetValue(id, out card) ? card : null;
        }

        /// <summary>自检用：模块状态文本。</summary>
        public string DescribeModule(string id)
        {
            var m = Find(id);
            if (m == null)
            {
                return id + "=missing";
            }

            var card = CardFor(id);
            return id + " state=" + m.State + " surface=" + m.Surface
                + " view=" + (m.View != null ? m.View.name : "null")
                + " viewParent=" + (m.View != null && m.View.parent != null ? m.View.parent.name : "null")
                + " cardVisible=" + (card != null && card.gameObject.activeSelf);
        }

        public IReadOnlyList<string> FloatingIds => _stacking;

        private TwinUiModuleWindow CreateFloatingWindow(TwinUiModule module, Rect rect)
        {
            if (_window == null)
            {
                return null;
            }

            var go = new GameObject("UiModuleWindow_" + module.Id, typeof(RectTransform));
            var win = go.AddComponent<TwinUiModuleWindow>();
            string id = module.Id;
            win.AllowMaximize = true;
            win.Configure(id, module.Title, _window, _window.Frame, rect, true,
                () => _window.WindowRect,
                over => win.SetHint(over),
                () => DockBack(id),
                () => DockBack(id),
                _floatingLayer,
                module.Closable);
            win.RectCommitted += OnFloatingRectCommitted;
            _floating[id] = win;
            return win;
        }

        private void OnFloatingRectCommitted(TwinUiModuleWindow win)
        {
            if (win == null)
            {
                return;
            }

            var pref = PrefsFor(win.ModuleId);
            Rect r = win.WindowRect;
            pref.x = r.x;
            pref.y = r.y;
            pref.w = r.width;
            pref.h = r.height;
            SavePrefs();

            // P10：响应式模块在浮窗尺寸变化后按新宽度重排（拉伸锚点只管外框，内层需重算）。
            var module = Find(win.ModuleId);
            if (module != null && module.View != null)
            {
                var fit = module.View.GetComponent<TwinUiContentFit>();
                if (fit != null) fit.Invalidate();
            }
            if (module != null && module.ResponsiveContent && module.View != null)
            {
                module.ApplyContentLayout(FloatingContentWidth(module), TwinUiModuleSurface.Floating);
            }
        }

        /// <summary>浮窗内容区宽（浮窗宽 - 2×内容内边距）；未布局时用模块设计宽兜底。</summary>
        private float FloatingContentWidth(TwinUiModule module)
        {
            if (module != null)
            {
                TwinUiModuleWindow win;
                if (_floating.TryGetValue(module.Id, out win) && win != null && win.Content != null
                    && win.Content.rect.width > 40f)
                {
                    return Mathf.Max(80f, win.Content.rect.width - 2f * TwinUiCardView.ContentPadX);
                }
            }

            float design = module != null && module.CardSize.x > 1f ? module.CardSize.x : 320f;
            return Mathf.Max(80f, design - 2f * TwinUiCardView.ContentPadX);
        }

        private Rect FloatingRectFor(TwinUiModule module)
        {
            var pref = PrefsFor(module.Id);
            var canvas = _window != null && _window.CanvasRect != null
                ? _window.CanvasRect.rect
                : new Rect(0f, 0f, 1920f, 1080f);
            float w = Mathf.Clamp(pref.w > 1f ? pref.w : module.CardSize.x * 1.5f, 300f, canvas.width);
            float h = Mathf.Clamp(pref.h > 1f ? pref.h : Mathf.Max(220f, module.CardSize.y * 1.4f), 180f,
                canvas.height);
            float x = pref.x > 1f ? pref.x : Mathf.Max(24f, canvas.width - w - 40f);
            float y = pref.y > 1f ? pref.y : Mathf.Max(24f, canvas.height - h - 200f);

            // 级联错位，避免多个浮窗完全重叠。
            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool overlaps = false;
                foreach (var pair in _floating)
                {
                    if (pair.Value != null && pair.Value.WindowRect.Overlaps(new Rect(x, y, w, h)))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    break;
                }

                x += 34f;
                y -= 28f;
            }

            return new Rect(x, y, w, h);
        }

        // ------------------------------------------------------------- 预设

        public void ApplyPreset(string name)
        {
            var preset = TwinUiLayoutPresets.Find(name);
            if (preset == null)
            {
                return;
            }

            // P3：预设只切页签 + 主窗尺寸，不再一键拆出多张卡，也不动用户已开的浮窗。
            _data.preset = preset.Id;
            if (_window != null && _window.CanvasRect != null)
            {
                Rect canvas = _window.CanvasRect.rect;
                float w = Mathf.Clamp(canvas.width * preset.WidthRatio, TwinUiWindowHost.MinWidth, canvas.width);
                float h = Mathf.Clamp(canvas.height * preset.HeightRatio, TwinUiWindowHost.MinHeight, canvas.height);
                _window.ApplyLayout(new Rect(canvas.width - w - 16f, (canvas.height - h) * 0.5f, w, h), false);
            }

            SetTab(preset.Tab);

            SavePrefs();
            StateChanged?.Invoke();
            Debug.Log(Tag + " preset=" + preset.Id + " tab=" + _tab + " floating=" + _floating.Count);
        }

        // ------------------------------------------------------------- 刷新

        /// <summary>
        /// P11：轮询当前页各模块的 LayoutVersion；有变化才重排一次整页。
        /// 只做 int 比较，不遍历控件树、不重建视图；空闲时零重排。
        /// </summary>
        private void PollContentInvalidation()
        {
            if (_page == null)
            {
                return;
            }

            var cards = _page.Cards;
            bool changed = false;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                var module = Find(card.ModuleId);
                if (module == null || module.State != TwinUiModuleState.Card)
                {
                    continue;
                }

                int known;
                if (!_layoutVersions.TryGetValue(module.Id, out known))
                {
                    _layoutVersions[module.Id] = module.LayoutVersion;
                    continue;
                }

                if (known == module.LayoutVersion)
                {
                    continue;
                }

                _layoutVersions[module.Id] = module.LayoutVersion;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _invalidationRelayoutCount++;
            ReapplyPageLayout();
        }

        /// <summary>
        /// 数字孪生页顶部连接状态条：源类型 / 连接 / 新鲜度 / 更新时间（回放时附 runId 与进度）。
        /// 只在可见状态变化时重排一次；文本/时间变化不触发重排（保证空闲零重排）。
        /// </summary>
        private void UpdateConnectionStatus()
        {
            if (_page == null)
            {
                return;
            }

            if (Time.unscaledTime < _statusNextAt)
            {
                return;
            }

            _statusNextAt = Time.unscaledTime + StatusInterval;

            string text = string.Empty;
            TwinUITone tone = TwinUITone.Unknown;
            if (_tab == TwinUiWorkspaceRegistry.Twin)
            {
                BuildConnectionStatus(out text, out tone);
            }

            if (_page.SetConnectionStatus(text, tone))
            {
                _statusUpdates++;
                ReapplyPageLayout();
            }

            // 本批（A1/A3）：数据源控制条文字/选中态随状态刷新（只改文字，不触发重排）。
            _page.RefreshSourceBar();
        }

        private void BuildConnectionStatus(out string text, out TwinUITone tone)
        {
            text = string.Empty;
            tone = TwinUITone.Unknown;

            if (Time.unscaledTime >= _statusBinderScanAt || _statusBinder == null)
            {
                _statusBinderScanAt = Time.unscaledTime + 3f;
                _statusBinder = FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_statusBinder == null)
            {
                text = "数据源 未绑定（无 TwinUIStatusBinder）";
                tone = TwinUITone.Unknown;
                return;
            }

            var s = _statusBinder.Current;
            string kind = SourceKindLabel(s);
            string conn = s.Connected ? "已连接" : (s.HasFrame ? "有帧/未连接" : "未连接");
            string fresh;
            if (!s.HasFrame)
            {
                fresh = "无帧";
            }
            else if (s.AgeMs > 1000d)
            {
                fresh = "陈旧 " + (s.AgeMs / 1000d).ToString("F2") + " s";
            }
            else
            {
                fresh = "新鲜 " + (s.AgeMs / 1000d).ToString("F2") + " s";
            }

            string update = string.IsNullOrEmpty(s.LastUpdateLocal) ? "-" : s.LastUpdateLocal;
            string detail = s.IsReplay
                ? "run " + s.SourceName + " " + (s.ReplayProgress01 * 100d).ToString("F0") + "%"
                : (string.IsNullOrEmpty(s.SourceName) ? "-" : s.SourceName);

            text = "源 " + kind + " · " + conn + " · " + fresh + " · 更新 " + update + " · " + detail;
            if (TwinUiViewFreeze.IsFrozen)
            {
                text += " · 显示冻结（记录继续）";
            }

            if (!s.Connected)
            {
                tone = TwinUITone.Unknown;
            }
            else if (s.HasFrame && s.AgeMs > 1000d)
            {
                tone = TwinUITone.Crit;
            }
            else if (s.HasFrame && s.AgeMs > 200d)
            {
                tone = TwinUITone.Warn;
            }
            else
            {
                tone = s.HasFrame ? TwinUITone.Ok : TwinUITone.Warn;
            }
        }

        private static string SourceKindLabel(TwinUIStatusBinder.Snapshot s)
        {
            if (s.IsReplay)
            {
                return "回放";
            }

            switch (s.SourceKind)
            {
                case "Ros2":
                    return "实时(ROS2)";
                case "DartStudio":
                    return "实时(DART)";
                case "Mock":
                    return "测试(MOCK)";
                case "Runtime":
                    return "实时";
                case "Replay":
                    return "回放";
                default:
                    return string.IsNullOrEmpty(s.SourceKind) ? "未连接" : s.SourceKind;
            }
        }

        private void Update()
        {
            if (!_configured || !TwinUiShellMode.IsV2)
            {
                return;
            }

            PollContentInvalidation();
            UpdateConnectionStatus();

            if (_floating.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
            {
                string top = TopFloatingId();
                if (!string.IsNullOrEmpty(top) && !_pinned.Contains(top))
                {
                    DockBack(top);
                    return;
                }
            }

            _refreshTimer += Time.unscaledDeltaTime;
            // R2 T5/T6：卡片刷新节拍跟随**显示刷新率**（DisplayHz），不再固定 4 Hz。
            // 否则 20 Hz 显示设置下卡片仍只刷 4 Hz，曲线点数不会随 Hz 变化，时间窗就无法证明
            // "同一窗口、不同 Hz ⇒ 跨度相同、点数不同"。上限 0.25 s（即最低 4 Hz，保持旧默认节奏的下界）。
            float interval = Mathf.Clamp(1f / Mathf.Max(1f, TwinUiSourceSwitch.DisplayHz),
                1f / 30f, RefreshInterval);
            if (_refreshTimer < interval)
            {
                return;
            }

            _refreshTimer = 0f;
            _refreshCount++;
            for (int i = 0; i < _moduleOrder.Count; i++)
            {
                var module = _moduleOrder[i];
                if (module.State == TwinUiModuleState.Hidden)
                {
                    continue;
                }

                if (module.State == TwinUiModuleState.Floating && _minimized.Contains(module.Id))
                {
                    continue;
                }

                module.Refresh();
            }
        }

        // ------------------------------------------------------------- 持久化

        public void SavePrefs()
        {
            if (_data == null)
            {
                return;
            }

            _data.tab = _tab;
            _data.sub = _sub;
            _data.widgets.Clear();
            _data.stacking.Clear();
            for (int i = 0; i < _moduleOrder.Count; i++)
            {
                var module = _moduleOrder[i];
                var pref = PrefsFor(module.Id);
                pref.state = (int)module.State;
                pref.pinned = _pinned.Contains(module.Id);
                pref.minimized = _minimized.Contains(module.Id);
                pref.order = i;
                _data.widgets.Add(pref);
            }

            for (int i = 0; i < _stacking.Count; i++)
            {
                if (_floating.ContainsKey(_stacking[i]))
                {
                    _data.stacking.Add(_stacking[i]);
                }
            }

            TwinUiShellPrefs.Save(_data);
        }

        private TwinUiWidgetPref PrefsFor(string id)
        {
            TwinUiWidgetPref pref;
            if (_prefs.TryGetValue(id, out pref) && pref != null)
            {
                return pref;
            }

            pref = new TwinUiWidgetPref { id = id, state = (int)TwinUiModuleState.Card };
            _prefs[id] = pref;
            return pref;
        }

        /// <summary>自检用：清空存档并回到默认页。</summary>
        public void ResetForCheck()
        {
            TwinUiShellPrefs.Clear();
            _prefs.Clear();
            _savedFloatingStates.Clear();
            _hidden.Clear();
            _minimized.Clear();
            _pinned.Clear();
            _data = new TwinUiShellLayoutData();
            var open = new List<string>(_floating.Keys);
            for (int i = 0; i < open.Count; i++)
            {
                DockBack(open[i]);
            }

            SetTab(TwinUiWorkspaceRegistry.Overview);
        }

        public string Describe()
        {
            return "surface tab=" + _tab + " sub=" + _sub + " preset=" + Preset
                + " modules=" + _moduleOrder.Count + " cards=" + _cards.Count
                + " floating=" + _floating.Count + " minimized=" + _minimized.Count
                + " pinned=" + _pinned.Count + " refresh=" + _refreshCount
                + " detach=" + _detachCount + " dockBack=" + _dockBackCount
                + " pageH=" + _pageHeight.ToString("F0") + " pageW=" + _pageWidth.ToString("F0");
        }

        /// <summary>把浮窗里的缩放内容还原成"卡片内容区拉伸"布局。</summary>
        private static void ResetToCardLayout(RectTransform view)
        {
            if (view == null)
            {
                return;
            }

            var scaler = view.GetComponent<TwinUiDesignScaler>();
            if (scaler != null)
            {
                Destroy(scaler);
            }

            view.localScale = Vector3.one;
            TwinUIStyle.Stretch(view, 0f, 0f, 0f, 0f);
        }

        private void ReapplyPageLayout()
        {
            ApplyPageLayout(_pageWidth, _pageHeight);
        }

        private static string DescribeRect(Rect r)
        {
            return "(" + r.x.ToString("F0") + "," + r.y.ToString("F0") + ","
                + r.width.ToString("F0") + "x" + r.height.ToString("F0") + ")";
        }
    }
}
