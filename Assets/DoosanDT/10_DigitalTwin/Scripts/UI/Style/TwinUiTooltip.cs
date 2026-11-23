// TwinUiTooltip.cs —— 轻量悬停提示（P10.2）
// 目标：图标按钮（无文字）需要可读说明，但不引入第二套 UI 框架：全局只留一个 "UiTooltip" 节点，
//       350ms 延时出现、跟随指针、夹在根 Canvas 内；同一时刻只显示一条。
// 复用：颜色/字号全部走 TwinUIStyle 令牌；圆角走 TwinUiRadii.ControlSprite；边框用 TwinUIStyle.Outline（项目既有 1px 边框写法）。
// 输入：只读 PointerEventData；指针位置在 Legacy Input 可用时读 Input.mousePosition，否则回退到进入时的事件坐标。
// 约束：只用 UnityEngine / UnityEngine.UI / TMPro / UnityEngine.EventSystems；不碰场景与资产。
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>挂在 host（通常是图标按钮）上的悬停提示；同一 host 只会有一个组件。</summary>
    [DisallowMultipleComponent]
    public sealed class TwinUiTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const string NodeName = "UiTooltip";

        /// <summary>悬停多久才显示（秒）。</summary>
        public const float ShowDelay = 0.35f;

        /// <summary>Tip 最大宽度（设计像素）。</summary>
        public const float MaxWidth = 220f;

        /// <summary>Tip 固定行高（设计像素，11px 字一行）。</summary>
        public const float PanelHeight = 22f;

        private const float PadX = 7f;
        private const float CursorOffsetX = 14f;
        private const float CursorOffsetY = 18f;
        private const float EdgeMargin = 6f;
        private const float MinTextWidth = 8f;

        // 全局单例：同一时刻只显示一条 Tip（多 host 共用同一个节点）。
        private static TwinUiTooltip _visible;
        private static GameObject _node;
        private static RectTransform _nodeRect;
        private static Image _bg;
        private static TMP_Text _label;

        private string _text = string.Empty;
        private bool _hovering;
        private float _hoverAt;
        private Vector2 _lastScreen;

        public string Text => _text;

        public bool Visible => _visible == this;

        public static bool AnyVisible => _visible != null;

        /// <summary>给 host 挂一个悬停提示（重复调用只更新文本）。host 为 null 时返回 null。</summary>
        public static TwinUiTooltip Attach(GameObject host, string text)
        {
            if (host == null)
            {
                return null;
            }

            TwinUiTooltip tip = host.GetComponent<TwinUiTooltip>();
            if (tip == null)
            {
                tip = host.AddComponent<TwinUiTooltip>();
            }

            tip.SetText(text);
            return tip;
        }

        /// <summary>更新文本；正在显示时立刻重新排版。</summary>
        public void SetText(string text)
        {
            _text = text ?? string.Empty;
            if (Visible)
            {
                if (string.IsNullOrEmpty(_text))
                {
                    HideAll();
                    return;
                }

                RefreshLayout();
                Follow();
            }
        }

        /// <summary>立即隐藏本 host 的提示（不影响其它 host 的悬停状态）。</summary>
        public void Hide()
        {
            _hovering = false;
            _hoverAt = 0f;
            if (_visible == this)
            {
                HideAll();
            }
        }

        /// <summary>隐藏单例节点（技能/场景切换时可直接调用）。</summary>
        public static void HideAll()
        {
            _visible = null;
            if (_node != null)
            {
                _node.SetActive(false);
            }
        }

        // ------------------------------------------------------------- 指针

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_text))
            {
                return;
            }

            _lastScreen = eventData != null ? eventData.position : _lastScreen;
            _hovering = true;
            _hoverAt = Time.unscaledTime;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hide();
        }

        private void Update()
        {
            if (!_hovering)
            {
                return;
            }

            if (_visible == this)
            {
                Follow();
                return;
            }

            if (Time.unscaledTime - _hoverAt < ShowDelay)
            {
                return;
            }

            Show();
        }

        private void OnDisable()
        {
            _hovering = false;
            _hoverAt = 0f;
            if (_visible == this)
            {
                HideAll();
            }
        }

        // ------------------------------------------------------------- 显示

        private void Show()
        {
            if (string.IsNullOrEmpty(_text))
            {
                return;
            }

            Canvas canvas = ResolveCanvas();
            if (canvas == null || !EnsureNode(canvas))
            {
                return;
            }

            if (_visible != null && _visible != this)
            {
                _visible.Hide();
            }

            _visible = this;
            _node.SetActive(true);
            _node.transform.SetAsLastSibling();
            RefreshLayout();
            Follow();
        }

        private Canvas ResolveCanvas()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            return canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        }

        private static Camera ResolveCamera()
        {
            if (_node == null)
            {
                return null;
            }

            Canvas canvas = _node.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary>首次创建（或换 Canvas 时重挂）全局单例节点：面板 = 节点自身，子节点只有一行 TMP 文本。</summary>
        private static bool EnsureNode(Canvas canvas)
        {
            Transform parent = canvas != null ? canvas.transform : null;
            if (parent == null)
            {
                return false;
            }

            if (_node != null)
            {
                if (_node.transform.parent != parent)
                {
                    _node.transform.SetParent(parent, false);
                }

                return true;
            }

            _node = new GameObject(NodeName, typeof(RectTransform), typeof(Image));
            _node.transform.SetParent(parent, false);
            _nodeRect = (RectTransform)_node.transform;
            _nodeRect.anchorMin = new Vector2(0.5f, 0.5f);
            _nodeRect.anchorMax = new Vector2(0.5f, 0.5f);
            _nodeRect.pivot = new Vector2(0f, 1f);
            _nodeRect.anchoredPosition = Vector2.zero;
            _nodeRect.sizeDelta = new Vector2(120f, PanelHeight);

            _bg = _node.GetComponent<Image>();
            _bg.raycastTarget = false;
            _bg.color = TwinUIStyle.Panel3Color;
            _bg.sprite = TwinUiRadii.ControlSprite;
            _bg.type = Image.Type.Sliced;
            _bg.pixelsPerUnitMultiplier = 1f;
            TwinUITheme.Tag(_node, TwinUIStyle.Panel3Color);
            TwinUIStyle.Outline(_node, TwinUIStyle.BorderColor);

            _label = TwinUIStyle.Text(_node.transform, "Label", string.Empty,
                TwinUIStyle.FontUnit, TwinUIStyle.TextColor);
            _label.enableWordWrapping = false;
            _label.overflowMode = TextOverflowModes.Ellipsis;
            _label.alignment = TextAlignmentOptions.Left;
            TwinUIStyle.Stretch(_label.rectTransform, PadX, 0f, PadX, 0f);

            _node.SetActive(false);
            return true;
        }

        private void RefreshLayout()
        {
            if (_nodeRect == null || _label == null)
            {
                return;
            }

            _label.text = _text;
            float maxText = Mathf.Max(MinTextWidth, MaxWidth - PadX * 2f);
            float width = _label.GetPreferredValues(_text, maxText, 0f).x;
            width = Mathf.Clamp(width, MinTextWidth, maxText);
            _nodeRect.sizeDelta = new Vector2(width + PadX * 2f, PanelHeight);
        }

        private void Follow()
        {
            if (_nodeRect == null)
            {
                return;
            }

            var parentRect = _nodeRect.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, ReadPointer(),
                    ResolveCamera(), out Vector2 local))
            {
                return;
            }

            Rect area = parentRect.rect;
            float width = _nodeRect.sizeDelta.x;
            float height = _nodeRect.sizeDelta.y;
            float x = Mathf.Clamp(local.x + CursorOffsetX, area.xMin + EdgeMargin, area.xMax - EdgeMargin - width);
            float y = Mathf.Clamp(local.y - CursorOffsetY, area.yMin + EdgeMargin + height, area.yMax - EdgeMargin);
            _nodeRect.anchoredPosition = new Vector2(x, y);
        }

        private Vector2 ReadPointer()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            Vector2 mouse = Input.mousePosition;
            if (mouse.sqrMagnitude > 0.01f)
            {
                _lastScreen = mouse;
            }
#endif
            return _lastScreen;
        }
    }
}