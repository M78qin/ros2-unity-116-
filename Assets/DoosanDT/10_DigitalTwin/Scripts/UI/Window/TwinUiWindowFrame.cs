// P2-1 主窗窗口化：窗口交互帧（标题栏拖动 / 8 向 6px 缩放 / 双击与 F11 最大化 / 最大化按钮）。
//
// 与 TwinUiWindowHost 的分工：
//   * Host 负责层级重组、窗口矩形、scale-to-fit、持久化；
//   * Frame 负责指针交互与窗口边缘控件，并把拖动/缩放期间的所有权交给 TwinUiInputArbiter
//     （WindowDrag / WindowResize 高于 Camera / SceneRay，避免拖窗口时相机跟着转）。
//
// 拖动只从"标题栏空白区"开始：按下目标必须是窗口根节点本身（pointerPress == 本组件所在 GO）。
// 点在 Header 的芯片/按钮上时 pointerPress 是按钮，本组件直接拒绝，保证"点按钮不会被当成拖窗口"。
//
// 真实鼠标路径与自检注入共用同一套内部状态机：TryBeginDragAt/DragBy/EndDragForTest、
// TryBeginResize/ResizeByForTest/EndResizeForTest 都是组件 API，UI/30 用它来自动断言。
using System.Collections.Generic;
using DigitalTwin.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiWindowFrame : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler
    {
        [Header("边缘缩放命中宽度（像素，画布单位）")]
        [SerializeField] private float resizeHitSize = 6f;

        [Header("标题栏最小高度（Header 缺失时的兜底）")]
        [SerializeField] private float titleBarFallbackHeight = 40f;

        [Header("最大化/还原按钮尺寸")]
        [SerializeField] private Vector2 maxButtonSize = new Vector2(42f, 22f);

        [Header("角落缩放命中区（V3 原型 23×23；边为 11px）")]
        [SerializeField] private float cornerHitSize = 23f;

        private ITwinUiFrameHost _host;
        private RectTransform _rt;
        private Canvas _canvas;
        private RectTransform _header;
        private TwinUIButton _maxButton;
        private TMP_Text _maxLabel;

        private int _mode; // 0=idle, 1=drag, 2=resize
        private TwinUiWindowEdge _resizeEdge;
        private Vector2 _lastScreen;

        public ITwinUiFrameHost Host => _host;
        public bool IsDragging => _mode == 1;
        public bool IsResizing => _mode == 2;
        public bool IsIdle => _mode == 0;
        public TwinUiWindowEdge ResizeEdge => _resizeEdge;

        // ------------------------------------------------------------- 装配

        /// <summary>由 Host 在运行时调用：装配到 UiWindowRoot 上，并复制场景里模板组件的参数。</summary>
        public void Configure(ITwinUiFrameHost host, TwinUiWindowFrame template)
        {
            _host = host;
            _rt = transform as RectTransform;
            _canvas = host != null && host.Canvas != null ? host.Canvas : GetComponentInParent<Canvas>();
            if (template != null && template != this)
            {
                resizeHitSize = template.resizeHitSize;
                titleBarFallbackHeight = template.titleBarFallbackHeight;
                maxButtonSize = template.maxButtonSize;
                cornerHitSize = template.cornerHitSize;
            }

            _header = host != null && host.Content != null
                ? host.Content.Find("Header") as RectTransform
                : null;

            BuildResizeHandles();
            BuildMaxButton();
            BuildWindowChrome();
            TwinUITheme.Changed += OnThemeChanged;
            _mode = 0;
            RefreshMaxButton();
        }

        private void Update()
        {
            if (_host == null || !_host.IsReady)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f11Key.wasPressedThisFrame && _host != null && _host.AllowsMaximize)
            {
                ToggleMaximize();
            }
#else
            if (Input.GetKeyDown(KeyCode.F11) && _host != null && _host.AllowsMaximize)
            {
                ToggleMaximize();
            }
#endif
        }

        private void OnDisable()
        {
            TwinUITheme.Changed -= OnThemeChanged;
            ReleaseAll();
        }

        private void OnDestroy()
        {
            TwinUITheme.Changed -= OnThemeChanged;
            // 悬浮窗关闭时销毁伴随的 Canvas 外阴影，避免孤儿对象（主窗 Destroy 时同样适用）。
            if (_shadow != null)
            {
                Destroy(_shadow.gameObject);
                _shadow = null;
            }
        }

        // ------------------------------------------------------------- 布局回调

        /// <summary>Host 每次应用窗口矩形后调用；边缘控件全部按锚点自适应，这里只刷新按钮文案。</summary>
        public void OnHostLayout(Rect rect, float contentScale)
        {
            UpdateWindowChrome(rect, contentScale);
            RefreshMaxButton();
        }

        public void RefreshMaxButton()
        {
            bool maximized = _host != null && _host.IsMaximized;
            if (_maxLabel != null)
            {
                _maxLabel.text = maximized ? "REST" : "MAX";
            }

            if (_maxButton != null)
            {
                _maxButton.SetSelected(maximized);
            }
        }

        // ------------------------------------------------------------- 标题栏几何

        /// <summary>标题栏在窗口根节点本地坐标下的矩形（= Header 容器缩放后的区域）。</summary>
        public Rect TitleBand
        {
            get
            {
                if (_rt == null)
                {
                    return Rect.zero;
                }

                if (_header == null)
                {
                    Rect rect = _rt.rect;
                    return new Rect(rect.xMin, rect.yMax - titleBarFallbackHeight, rect.width, titleBarFallbackHeight);
                }

                var corners = new Vector3[4];
                _header.GetWorldCorners(corners);
                float xMin = float.MaxValue;
                float yMin = float.MaxValue;
                float xMax = float.MinValue;
                float yMax = float.MinValue;
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 local = _rt.InverseTransformPoint(corners[i]);
                    xMin = Mathf.Min(xMin, local.x);
                    yMin = Mathf.Min(yMin, local.y);
                    xMax = Mathf.Max(xMax, local.x);
                    yMax = Mathf.Max(yMax, local.y);
                }

                Rect band = _rt.rect;
                return Rect.MinMaxRect(
                    Mathf.Max(xMin, band.xMin),
                    Mathf.Max(yMin, band.yMin),
                    Mathf.Min(xMax, band.xMax),
                    Mathf.Min(yMax, band.yMax));
            }
        }

        public Vector2 TitleBandCenter => TitleBand.center;

        public bool IsInTitleBand(Vector2 rootLocal)
        {
            Rect band = TitleBand;
            if (band.width < 1f || band.height < 1f)
            {
                return false;
            }

            return rootLocal.x >= band.xMin && rootLocal.x <= band.xMax
                && rootLocal.y >= band.yMin && rootLocal.y <= band.yMax;
        }

        /// <summary>按下目标是否是窗口表面本身（空白处）；点在按钮/芯片上返回 false。</summary>
        public bool IsPressOnWindowSurface(GameObject pressed)
        {
            return pressed != null && pressed == gameObject;
        }

        // ------------------------------------------------------------- 拖动（真实指针）

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_host == null || !_host.IsReady || !_host.AllowsDrag || eventData == null)
            {
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            // 只有"按在窗口空白表面"的拖拽才算拖窗口；按在 Header 芯片/按钮上不算。
            if (!IsPressOnWindowSurface(eventData.pointerPress))
            {
                return;
            }

            Vector2 local;
            if (!TryScreenToRootLocal(eventData.position, out local))
            {
                return;
            }

            if (!IsInTitleBand(local) || _host.IsMaximized)
            {
                return;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowDrag))
            {
                return;
            }

            _mode = 1;
            _lastScreen = eventData.position;
            _host.OnFrameDragBegin();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_mode != 1 || eventData == null || _host == null)
            {
                return;
            }

            float scaleFactor = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            Vector2 delta = (eventData.position - _lastScreen) / scaleFactor;
            _lastScreen = eventData.position;
            _host.MoveBy(delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_mode == 1)
            {
                FinishDrag();
            }
            else if (_mode == 2)
            {
                FinishResize();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_host == null || !_host.IsReady || !_host.AllowsMaximize || eventData == null || eventData.clickCount < 2)
            {
                return;
            }

            if (!IsPressOnWindowSurface(eventData.pointerPress))
            {
                return;
            }

            Vector2 local;
            if (!TryScreenToRootLocal(eventData.position, out local) || !IsInTitleBand(local))
            {
                return;
            }

            ToggleMaximize();
        }

        private bool TryScreenToRootLocal(Vector2 screenPoint, out Vector2 local)
        {
            local = Vector2.zero;
            if (_rt == null)
            {
                return false;
            }

            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screenPoint, cam, out local);
        }

        // ------------------------------------------------------------- 拖动/缩放 API（自检注入同一条路径）

        public bool TryBeginDragAt(Vector2 rootLocal)
        {
            if (_host == null || !_host.IsReady || !_host.AllowsDrag || _host.IsMaximized)
            {
                return false;
            }

            if (!IsInTitleBand(rootLocal))
            {
                return false;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowDrag))
            {
                return false;
            }

            _mode = 1;
            _lastScreen = Vector2.zero;
            _host.OnFrameDragBegin();
            return true;
        }

        public void DragBy(Vector2 deltaCanvas)
        {
            if (_mode != 1 || _host == null)
            {
                return;
            }

            _host.MoveBy(deltaCanvas);
        }

        public void EndDragForTest()
        {
            if (_mode == 1)
            {
                FinishDrag();
            }
        }

        public bool TryBeginResize(TwinUiWindowEdge edge)
        {
            if (_host == null || !_host.IsReady || edge == TwinUiWindowEdge.None)
            {
                return false;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowResize))
            {
                return false;
            }

            _mode = 2;
            _resizeEdge = edge;
            _lastScreen = Vector2.zero;
            return true;
        }

        public void ResizeByForTest(Vector2 deltaCanvas)
        {
            if (_mode != 2 || _host == null)
            {
                return;
            }

            _host.ResizeBy(_resizeEdge, deltaCanvas);
        }

        public void EndResizeForTest()
        {
            if (_mode == 2)
            {
                FinishResize();
            }
        }

        // 8 向命中条的真实指针路径（TwinUiWindowResizeHandle 转发到这里）。

        public void BeginResizeFromPointer(TwinUiWindowEdge edge, PointerEventData eventData)
        {
            if (_host == null || !_host.IsReady || edge == TwinUiWindowEdge.None)
            {
                return;
            }

            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowResize))
            {
                return;
            }

            _mode = 2;
            _resizeEdge = edge;
            _lastScreen = eventData != null ? eventData.position : Vector2.zero;
        }

        public void DragResizeFromPointer(PointerEventData eventData)
        {
            if (_mode != 2 || eventData == null || _host == null)
            {
                return;
            }

            float scaleFactor = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            Vector2 delta = (eventData.position - _lastScreen) / scaleFactor;
            _lastScreen = eventData.position;
            _host.ResizeBy(_resizeEdge, delta);
        }

        public void EndResizeFromPointer()
        {
            if (_mode == 2)
            {
                FinishResize();
            }
        }

        private void FinishDrag()
        {
            _mode = 0;
            TwinUiInput.End(TwinUiInputOwner.WindowDrag);
            if (_host != null)
            {
                _host.OnFrameDragEnd();
                _host.SaveLayout();
            }
        }

        private void FinishResize()
        {
            _mode = 0;
            TwinUiInput.End(TwinUiInputOwner.WindowResize);
            if (_host != null)
            {
                _host.OnFrameResizeEnd();
                _host.SaveLayout();
            }
        }

        /// <summary>退出/禁用时兜底释放所有权，避免仲裁器栈残留。</summary>
        public void ReleaseAll()
        {
            if (_mode == 1)
            {
                TwinUiInput.End(TwinUiInputOwner.WindowDrag);
            }
            else if (_mode == 2)
            {
                TwinUiInput.End(TwinUiInputOwner.WindowResize);
            }

            _mode = 0;
        }

        // ------------------------------------------------------------- 最大化

        public void ToggleMaximize()
        {
            if (_host == null || !_host.IsReady || !_host.AllowsMaximize)
            {
                return;
            }

            _host.ToggleMaximized();
            RefreshMaxButton();
        }

        // ------------------------------------------------------------- 窗口外壳视觉（P2.5）

        private Image _titleBand;
        private Image _shadow;
        private UnityEngine.UI.Outline _accentOutline;
        private readonly List<TwinUiWindowResizeHandle> _handles = new List<TwinUiWindowResizeHandle>();

        /// <summary>P2.5 换肤入口：刷新标题栏底/外阴影/活动窗 accent 描边/手柄 hover 色。</summary>
        public void ApplyTheme()
        {
            if (_titleBand != null)
            {
                _titleBand.color = TwinUIStyle.Layer2Color;
            }

            if (_shadow != null)
            {
                _shadow.color = TwinUIStyle.ShadowColor;
            }

            if (_accentOutline != null)
            {
                _accentOutline.effectColor = TwinUIStyle.Alpha(TwinUIStyle.StrongColor, 0.85f);
            }

            for (int i = 0; i < _handles.Count; i++)
            {
                if (_handles[i] != null)
                {
                    _handles[i].RefreshHighlightColor();
                }
            }

            if (_maxButton != null)
            {
                _maxButton.ApplyTheme();
                _maxButton.SetSelected(_host != null && _host.IsMaximized);
            }
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            ApplyTheme();
        }

        /// <summary>UI/31 用在 Edit 里把模板手柄尺寸对齐 V3（边 11 / 角 23）。</summary>
        public void SetChromeMetrics(float edge, float corner)
        {
            resizeHitSize = Mathf.Max(4f, edge);
            cornerHitSize = Mathf.Max(4f, corner);
        }

        public float ResizeHitSize => resizeHitSize;
        public float CornerHitSize => cornerHitSize;

        private void BuildWindowChrome()
        {
            if (_rt == null)
            {
                return;
            }

            // 活动窗 accent 描边：1px Outline 顶点效果（不新增对象，不挡射线）。
            var background = _rt.Find("WindowBackground") as RectTransform;
            if (background != null)
            {
                var img = background.GetComponent<Image>();
                if (img != null)
                {
                    _accentOutline = background.GetComponent<UnityEngine.UI.Outline>();
                    if (_accentOutline == null)
                    {
                        _accentOutline = background.gameObject.AddComponent<UnityEngine.UI.Outline>();
                    }

                    _accentOutline.effectDistance = new Vector2(1f, -1f);
                    _accentOutline.useGraphicAlpha = false;
                }
            }

            // 标题栏底：Root 的第 3 个子节点（Background/Backdrop 之后、Content 之前），被 Header 覆盖。
            if (_titleBand == null)
            {
                _titleBand = TwinUIStyle.Panel(_rt, "WindowTitleBar", TwinUIStyle.Layer2Color);
                _titleBand.raycastTarget = false;
                var rt = _titleBand.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, titleBarFallbackHeight);
                rt.SetSiblingIndex(Mathf.Min(2, _rt.childCount - 1));
            }

            // 外阴影：半透明圆角 Image，挂在 Canvas 上、作为 UiWindowRoot 的前一个兄弟（不参与射线）。
            if (_shadow == null && _host != null && _host.CanvasRect != null)
            {
                _shadow = TwinUIStyle.Panel(_host.CanvasRect, "UiWindowShadow", TwinUIStyle.ShadowColor);
                _shadow.raycastTarget = false;
                _shadow.sprite = TwinUiWindowHost.RoundedWindowSprite();
                _shadow.type = Image.Type.Sliced;
                _shadow.pixelsPerUnitMultiplier = 1f;
                var srt = _shadow.rectTransform;
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = Vector2.zero;
                srt.pivot = Vector2.zero;
                srt.SetSiblingIndex(Mathf.Max(0, _rt.GetSiblingIndex()));
            }

            ApplyTheme();
        }

        private void UpdateWindowChrome(Rect rect, float contentScale)
        {
            if (_titleBand != null)
            {
                float headerHeight = _header != null ? _header.rect.height * contentScale : titleBarFallbackHeight;
                _titleBand.rectTransform.sizeDelta = new Vector2(0f, Mathf.Max(1f, headerHeight));
            }

            if (_shadow != null)
            {
                bool show = _host != null && !_host.IsMaximized;
                _shadow.gameObject.SetActive(show);
                if (show)
                {
                    var srt = _shadow.rectTransform;
                    srt.anchoredPosition = new Vector2(rect.x - 18f, rect.y - 26f);
                    srt.sizeDelta = new Vector2(rect.width + 36f, rect.height + 48f);
                    srt.SetSiblingIndex(Mathf.Max(0, _rt.GetSiblingIndex() - 1));
                }
            }
        }

        // ------------------------------------------------------------- 边缘控件

        private void BuildResizeHandles()
        {
            if (_rt == null)
            {
                return;
            }

            CreateHandle("ResizeLeft", TwinUiWindowEdge.Left, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(resizeHitSize, 0f), Vector2.zero);
            CreateHandle("ResizeRight", TwinUiWindowEdge.Right, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(resizeHitSize, 0f), Vector2.zero);
            CreateHandle("ResizeTop", TwinUiWindowEdge.Top, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, resizeHitSize), Vector2.zero);
            CreateHandle("ResizeBottom", TwinUiWindowEdge.Bottom, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, resizeHitSize), Vector2.zero);
            CreateHandle("ResizeTopLeft", TwinUiWindowEdge.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(cornerHitSize, cornerHitSize), Vector2.zero);
            CreateHandle("ResizeTopRight", TwinUiWindowEdge.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(cornerHitSize, cornerHitSize), Vector2.zero);
            CreateHandle("ResizeBottomLeft", TwinUiWindowEdge.BottomLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(cornerHitSize, cornerHitSize), Vector2.zero);
            CreateHandle("ResizeBottomRight", TwinUiWindowEdge.BottomRight, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(cornerHitSize, cornerHitSize), Vector2.zero);
        }

        private void CreateHandle(string name, TwinUiWindowEdge edge, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_rt, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var img = go.GetComponent<Image>();
            img.color = TwinUIStyle.Transparent;
            img.raycastTarget = true;

            var highlights = BuildHandleHighlights(rt, edge);
            var handle = go.AddComponent<TwinUiWindowResizeHandle>();
            handle.Configure(this, edge, highlights);
            _handles.Add(handle);
        }

        /// <summary>hover 高亮：2px accent 线（边一条；角为 L 形两条），初始 alpha=0 不可见。</summary>
        private List<Image> BuildHandleHighlights(RectTransform parent, TwinUiWindowEdge edge)
        {
            var list = new List<Image>();
            float line = TwinUIStyle.HandleHoverLine;
            switch (edge)
            {
                case TwinUiWindowEdge.Left:
                    list.Add(AddStrip(parent, "Hl", new Vector2(1f, 0f), new Vector2(1f, 1f),
                        new Vector2(1f, 0.5f), new Vector2(line, -2f * cornerHitSize), Vector2.zero));
                    break;
                case TwinUiWindowEdge.Right:
                    list.Add(AddStrip(parent, "Hl", new Vector2(0f, 0f), new Vector2(0f, 1f),
                        new Vector2(0f, 0.5f), new Vector2(line, -2f * cornerHitSize), Vector2.zero));
                    break;
                case TwinUiWindowEdge.Top:
                    list.Add(AddStrip(parent, "Hl", new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0.5f, 0f), new Vector2(-2f * cornerHitSize, line), Vector2.zero));
                    break;
                case TwinUiWindowEdge.Bottom:
                    list.Add(AddStrip(parent, "Hl", new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(0.5f, 1f), new Vector2(-2f * cornerHitSize, line), Vector2.zero));
                    break;
                case TwinUiWindowEdge.TopLeft:
                    list.Add(AddStrip(parent, "HlV", new Vector2(1f, 0f), new Vector2(1f, 1f),
                        new Vector2(1f, 0.5f), new Vector2(line, 0f), Vector2.zero));
                    list.Add(AddStrip(parent, "HlH", new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0.5f, 0f), new Vector2(0f, line), Vector2.zero));
                    break;
                case TwinUiWindowEdge.TopRight:
                    list.Add(AddStrip(parent, "HlV", new Vector2(0f, 0f), new Vector2(0f, 1f),
                        new Vector2(0f, 0.5f), new Vector2(line, 0f), Vector2.zero));
                    list.Add(AddStrip(parent, "HlH", new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0.5f, 0f), new Vector2(0f, line), Vector2.zero));
                    break;
                case TwinUiWindowEdge.BottomLeft:
                    list.Add(AddStrip(parent, "HlV", new Vector2(1f, 0f), new Vector2(1f, 1f),
                        new Vector2(1f, 0.5f), new Vector2(line, 0f), Vector2.zero));
                    list.Add(AddStrip(parent, "HlH", new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(0.5f, 1f), new Vector2(0f, line), Vector2.zero));
                    break;
                case TwinUiWindowEdge.BottomRight:
                    list.Add(AddStrip(parent, "HlV", new Vector2(0f, 0f), new Vector2(0f, 1f),
                        new Vector2(0f, 0.5f), new Vector2(line, 0f), Vector2.zero));
                    list.Add(AddStrip(parent, "HlH", new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(0.5f, 1f), new Vector2(0f, line), Vector2.zero));
                    break;
            }

            return list;
        }

        private static Image AddStrip(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            var img = go.GetComponent<Image>();
            img.color = TwinUIStyle.Alpha(TwinUIStyle.AccentColor, 0f);
            img.raycastTarget = false;
            return img;
        }

        private void BuildMaxButton()
        {
            if (_rt == null || (_host != null && !_host.AllowsMaximize))
            {
                return;
            }

            var go = new GameObject("BtnWindowMax", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TwinUIButton));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_rt, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-8f, -6f);
            rt.sizeDelta = maxButtonSize;

            var img = go.GetComponent<Image>();
            img.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = img;

            var view = go.GetComponent<TwinUIButton>();
            TMP_Text label = TwinUIStyle.Text(rt, "Label", "MAX", 11f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(label.rectTransform, 2f, 0f, 2f, 0f);
            view.Setup(img, button, label, TwinUIButton.Kind.Plain, null);
            button.onClick.AddListener(ToggleMaximize);

            _maxButton = view;
            _maxLabel = label;
        }
    }

    /// <summary>窗口 8 向缩放命中条：透明 Image + 拖拽事件，转发给 TwinUiWindowFrame。</summary>
    [DisallowMultipleComponent]
    public sealed class TwinUiWindowResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private TwinUiWindowEdge edge = TwinUiWindowEdge.None;

        private TwinUiWindowFrame _frame;
        private readonly List<Image> _highlights = new List<Image>();
        private bool _hovered;

        public TwinUiWindowEdge Edge => edge;
        public bool Hovered => _hovered;

        public void Configure(TwinUiWindowFrame frame, TwinUiWindowEdge value, List<Image> highlights)
        {
            _frame = frame;
            edge = value;
            _highlights.Clear();
            if (highlights != null)
            {
                _highlights.AddRange(highlights);
            }

            RefreshHighlightColor();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshHighlightColor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            RefreshHighlightColor();
        }

        /// <summary>hover 时显示 2px accent 高亮线；颜色每次从当前主题令牌取，换肤后自动跟随。</summary>
        public void RefreshHighlightColor()
        {
            Color c = TwinUIStyle.Alpha(TwinUIStyle.AccentColor, _hovered ? 0.95f : 0f);
            for (int i = 0; i < _highlights.Count; i++)
            {
                if (_highlights[i] != null)
                {
                    _highlights[i].color = c;
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_frame != null)
            {
                _frame.BeginResizeFromPointer(edge, eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_frame != null)
            {
                _frame.DragResizeFromPointer(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_frame != null)
            {
                _frame.EndResizeFromPointer();
            }
        }
    }
}