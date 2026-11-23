// TwinUiModuleWindow.cs —— 模块悬浮窗（P5 / task-13）
// P4 时本类自己实现外壳；P5 起改为**真正实例化 TwinUiWindowFrame**（P2 同一份代码）：
//   圆角/边框/外阴影/标题栏/八向手柄 11-23/活动窗描边/最大化按钮（悬浮窗禁用）全部复用；
//   本类只负责：窗口矩形适配器（ITwinUiFrameHost）、标题文字、停靠/关闭按钮、贴边吸附、主题色。
using System;
using DigitalTwin.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>模块悬浮窗：与主窗共用 TwinUiWindowFrame；支持拖动、八向缩放、停靠、关闭、24px 贴边吸附。</summary>
    [DisallowMultipleComponent]
    public sealed class TwinUiModuleWindow : MonoBehaviour
    {
        public const float SnapThreshold = 24f;
        public const float DragOutMinWidth = 480f;
        public const float DragOutMinHeight = 330f;
        public const float MinWidth = 260f;
        public const float MinHeight = 150f;

        // P4 兼容常量（UI/38 自检引用；P5 起手柄/圆角实际由 TwinUiWindowFrame 的 chrome 参数决定，
        // 这里给出与 P2 外壳一致的期望值 11/23/13，供断言对照）。
        public const float EdgeHit = 11f;
        public const float CornerHit = 23f;
        public const float Radius = 13f;

        private sealed class FrameHostAdapter : ITwinUiFrameHost
        {
            private readonly TwinUiModuleWindow _owner;
            private Rect _rect;

            public FrameHostAdapter(TwinUiModuleWindow owner, Rect rect)
            {
                _owner = owner;
                _rect = rect;
            }

            public bool IsReady => true;
            public bool AllowsDrag => !_owner.IsMaximized;
            public bool AllowsMaximize => _owner.AllowMaximize;
            public bool AllowsMinimize => false;
            public bool AllowsHide => false;
            public bool FrameOwnsTitleButtons => false;
            public Canvas Canvas => _owner._canvas;
            public RectTransform CanvasRect => _owner._canvasRect;
            public RectTransform Content => null;
            public Rect WindowRect => _rect;
            public bool IsMaximized => _owner.IsMaximized;

            public void Minimize()
            {
            }

            public void Hide()
            {
            }

            public Vector2 MoveBy(Vector2 deltaCanvas)
            {
                if (_owner.IsMaximized)
                {
                    return Vector2.zero;
                }

                var r = WindowRect;
                SetBounds(new Rect(r.x + deltaCanvas.x, r.y + deltaCanvas.y, r.width, r.height));
                _owner.NotifyDragMove();
                return new Vector2(_rect.x - r.x, _rect.y - r.y);
            }

            public Vector2 ResizeBy(TwinUiWindowEdge edge, Vector2 deltaCanvas)
            {
                if (_owner.IsMaximized)
                {
                    return Vector2.zero;
                }

                Rect r = _rect;
                float x = r.x;
                float y = r.y;
                float w = r.width;
                float h = r.height;
                switch (edge)
                {
                    case TwinUiWindowEdge.Left:
                        x += deltaCanvas.x;
                        w -= deltaCanvas.x;
                        break;
                    case TwinUiWindowEdge.Right:
                        w += deltaCanvas.x;
                        break;
                    case TwinUiWindowEdge.Top:
                        h += deltaCanvas.y;
                        break;
                    case TwinUiWindowEdge.Bottom:
                        y += deltaCanvas.y;
                        h -= deltaCanvas.y;
                        break;
                    case TwinUiWindowEdge.TopLeft:
                        x += deltaCanvas.x;
                        w -= deltaCanvas.x;
                        h += deltaCanvas.y;
                        break;
                    case TwinUiWindowEdge.TopRight:
                        w += deltaCanvas.x;
                        h += deltaCanvas.y;
                        break;
                    case TwinUiWindowEdge.BottomLeft:
                        x += deltaCanvas.x;
                        w -= deltaCanvas.x;
                        y += deltaCanvas.y;
                        h -= deltaCanvas.y;
                        break;
                    case TwinUiWindowEdge.BottomRight:
                        w += deltaCanvas.x;
                        y += deltaCanvas.y;
                        h -= deltaCanvas.y;
                        break;
                }

                if (w < MinWidth)
                {
                    if (edge == TwinUiWindowEdge.Left || edge == TwinUiWindowEdge.TopLeft
                        || edge == TwinUiWindowEdge.BottomLeft)
                    {
                        x -= MinWidth - w;
                    }

                    w = MinWidth;
                }

                if (h < MinHeight)
                {
                    if (edge == TwinUiWindowEdge.Bottom || edge == TwinUiWindowEdge.BottomLeft
                        || edge == TwinUiWindowEdge.BottomRight)
                    {
                        y -= MinHeight - h;
                    }

                    h = MinHeight;
                }

                SetBounds(new Rect(x, y, w, h));
                _owner.NotifyDragMove();
                return new Vector2(_rect.width - r.width, _rect.height - r.height);
            }

            public void SetBounds(Rect rect)
            {
                _rect = _owner.ClampRect(rect);
                _owner.ApplyWindowRect(_rect);
            }

            public void SaveLayout()
            {
                _owner.RectCommitted?.Invoke(_owner);
            }

            public void ToggleMaximized()
            {
                _owner.ToggleMaximizedWindow();
            }

            public void OnFrameDragBegin()
            {
                _owner.BringToFront();
            }

            public void OnFrameDragEnd()
            {
                _owner.HandleDragEnd();
            }

            public void OnFrameResizeEnd()
            {
            }
        }

        private RectTransform _wrapper;
        private RectTransform _window;
        private RectTransform _content;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private TwinUiWindowFrame _frame;
        private FrameHostAdapter _adapter;
        private Image _bg;
        private Image _backdrop;
        private TMP_Text _title;
        private TwinUIButton _dockButton;
        private TwinUIButton _closeButton;
        private RectTransform _controls;
        private bool _closable;
        private string _moduleId;
        private bool _configured;
        private bool _snapped;
        private bool _expanded;
        private Func<Rect> _mainRectProvider;
        private Action<bool> _dockHintChanged;
        private bool _hintActive;
        private Action _onDockAction;
        private Action _onCloseAction;

        [Header("最大化（V2 起卡片浮窗可放大到画布）")]
        [SerializeField] private bool allowMaximize = true;

        private bool _maximized;
        private Rect _restoreRect;
        private TwinUIButton _maxButton;
        private TMP_Text _maxLabel;

        public bool AllowMaximize
        {
            get => allowMaximize;
            set => allowMaximize = value;
        }

        public bool IsMaximized => _maximized;

        public TwinUIButton MaxButton => _maxButton;

        public Rect WindowRect => _adapter != null ? _adapter.WindowRect : Rect.zero;
        public RectTransform WindowRoot => _window;
        public RectTransform Content => _content;
        public TwinUiWindowFrame Frame => _frame;
        public string ModuleId => _moduleId;
        public bool IsConfigured => _configured;
        public bool IsSnapped => _snapped;
        public bool HintActive => _hintActive;
        public bool OverMainWindow => MainRectContainsCenter();
        public bool IsExpanded => _expanded;
        public Vector2 MinSize => new Vector2(MinWidth, MinHeight);
        public event Action DockRequested;
        public event Action CloseRequested;
        public event Action<TwinUiModuleWindow> RectCommitted;

        // ------------------------------------------------------------- 装配

        public void Configure(string moduleId, string title, ITwinUiFrameHost mainHost, TwinUiWindowFrame template,
            Rect rect, bool expanded, Func<Rect> mainRectProvider, Action<bool> dockHintChanged,
            Action onDock, Action onClose, RectTransform layer = null, bool closable = false)
        {
            _moduleId = moduleId;
            _expanded = expanded;
            _closable = closable;
            _mainRectProvider = mainRectProvider;
            _dockHintChanged = dockHintChanged;
            _onDockAction = onDock;
            _onCloseAction = onClose;
            _canvasRect = mainHost != null ? mainHost.CanvasRect : GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
            _canvas = mainHost != null ? mainHost.Canvas : GetComponentInParent<Canvas>();

            _wrapper = (RectTransform)transform;
            _wrapper.SetParent(layer != null ? layer : _canvasRect, false);
            _wrapper.anchorMin = Vector2.zero;
            _wrapper.anchorMax = Vector2.one;
            _wrapper.offsetMin = Vector2.zero;
            _wrapper.offsetMax = Vector2.zero;
            _wrapper.pivot = new Vector2(0.5f, 0.5f);

            _window = TwinUIStyle.Frame(_wrapper, "Window");
            _window.anchorMin = _window.anchorMax = new Vector2(0f, 1f);
            _window.pivot = new Vector2(0f, 1f);

            Sprite rounded = TwinUiWindowHost.RoundedWindowSprite();
            _bg = TwinUIStyle.Panel(_window, "WindowBackground", TwinUIStyle.BorderColor, true);
            _bg.sprite = rounded;
            _bg.type = Image.Type.Sliced;
            _bg.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_bg.rectTransform, 0f, 0f, 0f, 0f);

            _backdrop = TwinUIStyle.Panel(_window, "WindowBackdrop", TwinUIStyle.LayerColor, false);
            _backdrop.sprite = rounded;
            _backdrop.type = Image.Type.Sliced;
            _backdrop.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_backdrop.rectTransform, 1f, 1f, 1f, 1f);

            var mask = _window.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            _content = TwinUIStyle.Frame(_window, "UiModuleContent");
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 0.5f);
            _content.offsetMin = new Vector2(1f, 1f);
            _content.offsetMax = new Vector2(-1f, -41f);

            _adapter = new FrameHostAdapter(this, rect);
            _frame = _window.gameObject.AddComponent<TwinUiWindowFrame>();
            _frame.Configure(_adapter, template);
            _frame.SetChromeMetrics(11f, 23f);

            _title = TwinUIStyle.Text(_window, "ModuleTitle", title, 14f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            TwinUIStyle.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -9f), new Vector2(240f, 22f));

            // P2：浮窗标题栏 = 独立水平布局容器（最大化 / 图钉 / 可选关闭），全部 28×28 图标。
            _controls = TwinUiCardChrome.BuildCluster(_window, "WindowControls");
            _controls.anchoredPosition = new Vector2(-TwinUIStyle.ControlRightMargin, -6f);
            _maxButton = TwinUiCardChrome.AddIcon(_controls, "BtnMaximize", TwinUiIcon.Glyph.Maximize,
                "最大化浮窗", ToggleMaximizedWindow);
            _dockButton = TwinUiCardChrome.AddIcon(_controls, "BtnDock", TwinUiIcon.Glyph.PinOff,
                "恢复停靠（回到卡片）", () =>
                {
                    if (_onDockAction != null)
                    {
                        _onDockAction();
                    }
                    else
                    {
                        DockRequested?.Invoke();
                    }
                });

            if (closable)
            {
                _closeButton = TwinUiCardChrome.AddIcon(_controls, "BtnClose", TwinUiIcon.Glyph.Close,
                    "关闭卡片（保留数据）", () =>
                    {
                        if (_onCloseAction != null)
                        {
                            _onCloseAction();
                        }
                        else
                        {
                            CloseRequested?.Invoke();
                        }
                    });
            }

            TwinUITheme.Changed += OnThemeChanged;
            ApplyTheme();
            TwinUiRadii.Apply(_window);
            _adapter.SetBounds(rect);
            _configured = true;
        }

        private void OnDestroy()
        {
            TwinUITheme.Changed -= OnThemeChanged;
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            if (_bg != null)
            {
                _bg.color = TwinUIStyle.BorderColor;
            }

            if (_backdrop != null)
            {
                _backdrop.color = TwinUIStyle.LayerColor;
            }

            if (_title != null)
            {
                _title.color = TwinUIStyle.TextColor;
            }

            if (_dockButton != null)
            {
                _dockButton.ApplyTheme();
            }

            if (_closeButton != null)
            {
                _closeButton.ApplyTheme();
            }

            if (_maxButton != null)
            {
                _maxButton.ApplyTheme();
            }

            if (_frame != null)
            {
                _frame.ApplyTheme();
            }
        }

        // ------------------------------------------------------------- 几何

        /// <summary>把矩形容器放到画布坐标（左下角原点）并通知 Frame 刷新外壳。</summary>
        public void ApplyWindowRect(Rect rect)
        {
            if (_window == null || _canvasRect == null)
            {
                return;
            }

            float canvasH = _canvasRect.rect.height;
            _window.anchoredPosition = new Vector2(rect.x, -(canvasH - rect.y - rect.height));
            _window.sizeDelta = new Vector2(rect.width, rect.height);
            if (_title != null)
            {
                _title.rectTransform.sizeDelta = new Vector2(Mathf.Max(80f, rect.width - 106f), 22f);
            }

            if (_frame != null)
            {
                _frame.OnHostLayout(rect, 1f);
            }
        }

        public Rect ClampRect(Rect rect)
        {
            float canvasW = _canvasRect != null ? _canvasRect.rect.width : 1920f;
            float canvasH = _canvasRect != null ? _canvasRect.rect.height : 1080f;
            float w = Mathf.Clamp(rect.width, MinWidth, Mathf.Max(MinWidth, canvasW));
            float h = Mathf.Clamp(rect.height, MinHeight, Mathf.Max(MinHeight, canvasH));
            float x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, canvasW - w));
            float y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, canvasH - h));
            return new Rect(x, y, w, h);
        }

        public void MoveBy(Vector2 deltaCanvas)
        {
            if (_adapter == null)
            {
                return;
            }

            _adapter.MoveBy(deltaCanvas);
            _snapped = false;
            _adapter.SaveLayout();
        }

        public void ResizeBy(TwinUiWindowEdge edge, Vector2 deltaCanvas)
        {
            if (_adapter == null)
            {
                return;
            }

            _adapter.ResizeBy(edge, deltaCanvas);
            _adapter.SaveLayout();
        }

        // ------------------------------------------------------------- 最大化（V2）

        // 最大化按钮在 Configure 的控件集群里创建（28×28 图标），此处不再单独建。

        /// <summary>放大到画布（留 12px 边距），再点还原；放大时禁止拖动/缩放。</summary>
        public void ToggleMaximizedWindow()
        {
            if (!allowMaximize || _adapter == null || _canvasRect == null)
            {
                return;
            }

            if (_maximized)
            {
                _maximized = false;
                Rect back = _restoreRect;
                back.width = Mathf.Max(MinWidth, back.width);
                back.height = Mathf.Max(MinHeight, back.height);
                _adapter.SetBounds(back);
            }
            else
            {
                _restoreRect = _adapter.WindowRect;
                _maximized = true;
                Rect c = _canvasRect.rect;
                _adapter.SetBounds(new Rect(12f, 12f, Mathf.Max(MinWidth, c.width - 24f),
                    Mathf.Max(MinHeight, c.height - 24f)));
            }

            UpdateMaxButton();
            _adapter.SaveLayout();
        }

        private void UpdateMaxButton()
        {
            if (_maxButton != null)
            {
                TwinUiCardChrome.SetGlyph(_maxButton,
                    _maximized ? TwinUiIcon.Glyph.Restore : TwinUiIcon.Glyph.Maximize,
                    _maximized ? "还原浮窗" : "最大化浮窗");
            }

            if (_maxLabel != null)
            {
                _maxLabel.text = _maximized ? "还原" : "最大";
            }
        }

        /// <summary>贴边吸附：左/右/上 24px 内贴齐；返回是否吸附。</summary>
        public bool SnapToEdges()
        {
            if (_adapter == null || _canvasRect == null)
            {
                return false;
            }

            Rect r = _adapter.WindowRect;
            float canvasW = _canvasRect.rect.width;
            float canvasH = _canvasRect.rect.height;
            float x = r.x;
            float y = r.y;
            bool snapped = false;
            if (r.x <= SnapThreshold)
            {
                x = 0f;
                snapped = true;
            }
            else if (canvasW - r.xMax <= SnapThreshold)
            {
                x = canvasW - r.width;
                snapped = true;
            }

            if (canvasH - r.yMax <= SnapThreshold)
            {
                y = canvasH - r.height;
                snapped = true;
            }

            if (snapped)
            {
                _adapter.SetBounds(new Rect(x, y, r.width, r.height));
                _adapter.SaveLayout();
            }

            _snapped = snapped;
            return snapped;
        }

        // ------------------------------------------------------------- 跨窗拖拽停靠（P6-1）

        /// <summary>窗口中心是否落在主窗矩形内（停靠判定口径：中心点 + 主窗 rect；非像素级落点）。</summary>
        public bool MainRectContainsCenter()
        {
            if (_mainRectProvider == null)
            {
                return false;
            }

            Rect main = _mainRectProvider();
            if (main.width < 1f || main.height < 1f)
            {
                return false;
            }

            Rect r = WindowRect;
            return main.Contains(new Vector2(r.center.x, r.center.y));
        }

        /// <summary>拖拽中：刷新停靠预览（仅状态变化时回调，避免每帧重建 UI）。</summary>
        public void NotifyDragMove()
        {
            bool over = MainRectContainsCenter();
            if (over == _hintActive)
            {
                return;
            }

            _hintActive = over;
            _dockHintChanged?.Invoke(over);
        }

        /// <summary>拖拽结束：优先判定"拖到主窗上"→ 请求 Docked；否则走贴边吸附。</summary>
        public void HandleDragEnd()
        {
            bool over = MainRectContainsCenter();
            SetHint(false);
            if (over)
            {
                if (_onDockAction != null)
                {
                    _onDockAction();
                }
                else
                {
                    DockRequested?.Invoke();
                }

                return;
            }

            SnapToEdges();
        }

        public void SetHint(bool active)
        {
            if (_hintActive == active)
            {
                return;
            }

            _hintActive = active;
            _dockHintChanged?.Invoke(active);
        }

        public void BringToFront()
        {
            if (_wrapper != null)
            {
                _wrapper.SetAsLastSibling();
            }
        }

        public void SetTitle(string value)
        {
            if (_title != null)
            {
                _title.text = value;
            }
        }

        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
        }

        /// <summary>自检用：直接设置矩形（走同一条 Clamp/Apply 路径）。</summary>
        public void SetRectForCheck(Rect rect)
        {
            if (_adapter != null)
            {
                _adapter.SetBounds(rect);
            }
        }
    }
}