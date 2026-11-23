// TwinUiCardDragOut.cs —— 卡片标题栏"拖出成浮窗"（P9.5）
// 职责边界：只做手势识别 + 视觉反馈。不复制卡片、不移动卡片（位置由 TwinUiCardGrid 管）、
//           不创建浮窗 —— 拆出必须走 onDragOut → TwinUiSurfaceHost.Detach（与「拆出」按钮同一路径）。
// 输入：只读 PointerEventData（Input System / Legacy 由 uGUI 统一），不读 Input.*，不新增任何订阅。
// 判定口径：按下点必须落在卡片标题栏（"CardHeader"，36 设计像素 × 卡片缩放，缩放由标题栏 RectTransform 自带）；
//           拖动中卡片整体 alpha 0.6（CanvasGroup，不复制卡片）；
//           松手：指针离开主窗矩形 且 累计位移 ≥ MinDragPixels(120 屏幕像素) → onDragOut(moduleId)；
//           否则只还原 alpha，不拆出（主窗内松手一律不拆，即便拖了很远）。
using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiCardDragOut : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>拖出判定：累计位移下限（屏幕像素）；防止 1px 越界抖动把卡片误拆出。</summary>
        public const float MinDragPixels = 120f;

        /// <summary>拖动中的"幽灵"透明度（只改 CanvasGroup，不复制卡片）。</summary>
        public const float GhostAlpha = 0.6f;

        // 节点名与 TwinUiCardView.BuildChrome 里的命名保持一致（标题栏/拆出按钮）。
        private const string HeaderNodeName = "CardHeader";
        private const string DetachButtonNodeName = "BtnDetach";

        private string _moduleId = string.Empty;
        private RectTransform _mainWindowRect;
        private System.Action<string> _onDragOut;

        private RectTransform _cardRect;
        private RectTransform _headerRect;
        private RectTransform _detachRect;
        private Canvas _canvas;
        private CanvasGroup _group;
        private float _alphaBefore = 1f;
        private bool _ghostActive;

        private bool _dragging;
        private Vector2 _startScreen;
        private int _dragOutCount;

        public bool Dragging => _dragging;

        public int DragOutCount => _dragOutCount;

        public string ModuleId => _moduleId;

        /// <summary>已注入的主窗矩形（debug/自检用）；为空时会在首次判定时沿父链找 TwinUiWindowFrame。</summary>
        public RectTransform MainWindowRect => _mainWindowRect;

        /// <summary>由 TwinUiCardView 构建时调用；mainWindowRect 可传 null（宿主稍后 SetMainWindowRect，或自动沿父链解析）。</summary>
        public void Configure(string moduleId, RectTransform mainWindowRect, System.Action<string> onDragOut)
        {
            _moduleId = moduleId ?? string.Empty;
            _mainWindowRect = mainWindowRect;
            _onDragOut = onDragOut;
            _cardRect = transform as RectTransform;
        }

        /// <summary>宿主注入主窗矩形（TwinUiWindowHost.Root）。注入优先于自动解析。</summary>
        public void SetMainWindowRect(RectTransform rect)
        {
            _mainWindowRect = rect;
        }

        // ------------------------------------------------------------- uGUI 指针路径

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            TryBeginDrag(eventData.position, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // 只做视觉反馈（BeginDrag 时已把卡片调成半透明）；卡片位置不在这里移动。
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            FinishDrag(eventData != null ? eventData.position : _startScreen, eventData);
        }

        // ------------------------------------------------------------- 判定

        private bool TryBeginDrag(Vector2 screenPoint, PointerEventData eventData)
        {
            if (_dragging || _onDragOut == null || !CanDetach())
            {
                return false;
            }

            // 「拆出」按钮或其它交互控件上的按下不进入拖拽（否则按钮点击会被抢成拖出）。
            if (IsOnBlockedControl(screenPoint, eventData))
            {
                return false;
            }

            if (!IsInHeader(screenPoint, eventData))
            {
                return false;
            }

            _startScreen = screenPoint;
            BeginGhost();
            _dragging = true;
            return true;
        }

        /// <summary>返回是否真的触发了拆出。</summary>
        private bool FinishDrag(Vector2 screenPoint, PointerEventData eventData)
        {
            if (!_dragging)
            {
                return false;
            }

            float travel = (screenPoint - _startScreen).magnitude;
            bool outside = !IsInsideMainWindow(screenPoint, eventData);

            _dragging = false;
            RestoreGhost(); // 先还原 alpha 再回调：拆出后卡片会被 SetPlaceholder 接管。

            if (!outside || travel < MinDragPixels || _onDragOut == null)
            {
                return false;
            }

            _dragOutCount++;
            _onDragOut(_moduleId);
            return true;
        }

        private bool CanDetach()
        {
            TwinUiModule module = TwinUiSurfaceHost.FindModule(_moduleId);
            if (module == null)
            {
                return true;
            }

            return module.Detachable && module.State != TwinUiModuleState.Floating;
        }

        /// <summary>标题栏命中：优先用真实的"CardHeader" RectTransform（自带卡片缩放/锚点），回退到本地坐标带高 36 设计像素。</summary>
        private bool IsInHeader(Vector2 screenPoint, PointerEventData eventData)
        {
            Camera cam = ResolveCamera(eventData);
            RectTransform header = ResolveHeader();
            if (header != null)
            {
                return RectTransformUtility.RectangleContainsScreenPoint(header, screenPoint, cam);
            }

            EnsureCardRect();
            if (_cardRect == null)
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_cardRect, screenPoint, cam, out Vector2 local))
            {
                return false;
            }

            Rect r = _cardRect.rect;
            // local 已是设计单位（localScale 在换算中被消掉），所以这里直接用 36 设计像素。
            float band = TwinUiCardView.HeaderHeight;
            return local.x >= r.xMin && local.x <= r.xMax && local.y <= r.yMax && local.y >= r.yMax - band;
        }

        private bool IsOnBlockedControl(Vector2 screenPoint, PointerEventData eventData)
        {
            // 按下目标本身是按钮/滑条（含 TwinUIButton 外壳）→ 不拖出。
            if (eventData != null && eventData.pointerPress != null)
            {
                if (eventData.pointerPress.GetComponentInParent<UnityEngine.UI.Selectable>() != null)
                {
                    return true;
                }

                if (eventData.pointerPress.GetComponentInParent<TwinUIButton>() != null)
                {
                    return true;
                }
            }

            RectTransform detachButton = ResolveDetachButton();
            return detachButton != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(detachButton, screenPoint, ResolveCamera(eventData));
        }

        /// <summary>指针是否仍在主窗矩形内（RectangleContainsScreenPoint 已处理画布/相机/缩放）。拿不到主窗时保守返回 true。</summary>
        private bool IsInsideMainWindow(Vector2 screenPoint, PointerEventData eventData)
        {
            RectTransform win = ResolveMainWindowRect();
            if (win == null)
            {
                return true;
            }

            return RectTransformUtility.RectangleContainsScreenPoint(win, screenPoint, ResolveCamera(eventData));
        }

        // ------------------------------------------------------------- 解析

        private RectTransform ResolveMainWindowRect()
        {
            if (_mainWindowRect != null)
            {
                return _mainWindowRect;
            }

            // 卡片位于主窗内容根之下；TwinUiWindowFrame 挂在主窗 window.Root 上，其 RectTransform 即 WindowRect。
            TwinUiWindowFrame frame = GetComponentInParent<TwinUiWindowFrame>();
            if (frame != null)
            {
                _mainWindowRect = frame.GetComponent<RectTransform>();
            }

            return _mainWindowRect;
        }

        private RectTransform ResolveHeader()
        {
            EnsureCardRect();
            if (_headerRect == null && _cardRect != null)
            {
                _headerRect = _cardRect.Find(HeaderNodeName) as RectTransform;
            }

            return _headerRect;
        }

        private RectTransform ResolveDetachButton()
        {
            EnsureCardRect();
            if (_detachRect == null && _cardRect != null)
            {
                _detachRect = _cardRect.Find(DetachButtonNodeName) as RectTransform;
            }

            return _detachRect;
        }

        private void EnsureCardRect()
        {
            if (_cardRect == null)
            {
                _cardRect = transform as RectTransform;
            }
        }

        private Camera ResolveCamera(PointerEventData eventData)
        {
            if (eventData != null && eventData.pressEventCamera != null)
            {
                return eventData.pressEventCamera;
            }

            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            return _canvas != null ? _canvas.worldCamera : null;
        }

        // ------------------------------------------------------------- 幽灵视觉

        private void BeginGhost()
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null)
                {
                    _group = gameObject.AddComponent<CanvasGroup>();
                }
            }

            _alphaBefore = _group.alpha;
            _ghostActive = true;
            if (_alphaBefore > GhostAlpha)
            {
                _group.alpha = GhostAlpha;
            }
        }

        private void RestoreGhost()
        {
            if (!_ghostActive)
            {
                return;
            }

            _ghostActive = false;
            if (_group != null)
            {
                _group.alpha = _alphaBefore;
            }
        }

        private void OnDisable()
        {
            _dragging = false;
            RestoreGhost();
        }

        // ------------------------------------------------------------- 自检注入（与真实指针同一代码路径，不需要真实鼠标）

        public bool BeginDragForCheck(Vector2 screenPoint)
        {
            return TryBeginDrag(screenPoint, null);
        }

        public bool EndDragForCheck(Vector2 screenPoint)
        {
            return FinishDrag(screenPoint, null);
        }
    }
}