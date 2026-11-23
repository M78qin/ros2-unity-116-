// TwinUiCardView.cs —— 页内卡片外壳（P8）
// 设计目标：卡片自己不知道业务，只提供"标题 + 状态 + 拆出按钮 + 内容区"；内容由 TwinUiModule 填。
// 复用：颜色/圆角/字阶全部走 TwinUIStyle 令牌；主题切换按角色重刷，不在卡片里硬编码颜色。
// 交互：hover 只换 1px 描边色（不换底色，避免与角色标签冲突）；"拆出"按钮由宿主回调。
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const string NodeNamePrefix = "Card_";
        public const float HeaderHeight = 36f;
        public const float FooterViewHeight = 0f;

        /// <summary>卡片边框总高：标题栏 36 + 上间隙 8 + 下内边 10（P10 契约，卡高 = 54 + 内容高）。</summary>
        public const float ChromeHeight = 54f;

        /// <summary>内容区左右内边距（P10 契约，内容宽 = 卡宽 - 24）。</summary>
        public const float ContentPadX = 12f;

        public string ModuleId { get; private set; }

        public RectTransform Content { get; private set; }

        public TwinUIButton DetachButton { get; private set; }

        /// <summary>"拆出"请求（拖出与宿主自检用；不等于图钉点击）。</summary>
        [System.NonSerialized] public System.Action<string> DetachRequest;

        /// <summary>图钉请求：宿主的 ToggleFloating（Card↔Floating）；与 DetachRequest 分离。</summary>
        [System.NonSerialized] public System.Action<string> ToggleRequest;

        /// <summary>标题栏"拖出成浮窗"手势组件（P9.5）；宿主可用 SetMainWindowRect 注入主窗矩形。</summary>
        public TwinUiCardDragOut DragOut { get; private set; }

        /// <summary>宿主注入主窗矩形（TwinUiWindowHost.Root），用于"指针是否离开主窗"的判定。</summary>
        public void SetMainWindowRect(RectTransform rect)
        {
            if (DragOut != null)
            {
                DragOut.SetMainWindowRect(rect);
            }
        }

        private RectTransform _root;
        private Image _bg;
        private UnityEngine.UI.Outline _outline;
        private float _designWidth = TwinUiCardGrid.DesignWidth;
        private Image _statusDot;
        private TMP_Text _statusText;
        private TMP_Text _title;
        private TMP_Text _kicker;
        private TwinUIButton _pinButton;
        private bool _floating;

        /// <summary>构建卡片外壳。onToggle 为空时隐藏图钉（不可悬浮的卡片）。</summary>
        public static TwinUiCardView Build(Transform parent, TwinUiModule module,
            UnityEngine.Events.UnityAction<string> onToggle, UnityEngine.Events.UnityAction<string> onDetach)
        {
            var root = TwinUIStyle.Frame(parent, NodeNamePrefix + module.Id);
            var view = root.gameObject.AddComponent<TwinUiCardView>();
            view._root = root;
            view.ModuleId = module.Id;
            view.BuildChrome(module, onToggle, onDetach);
            return view;
        }

        private void BuildChrome(TwinUiModule module, UnityEngine.Events.UnityAction<string> onToggle,
            UnityEngine.Events.UnityAction<string> onDetach)
        {
            _designWidth = Mathf.Max(200f, module.CardSize.x);
            _bg = TwinUIStyle.Panel(_root, "CardBg", TwinUIStyle.CardColor, true);
            _bg.sprite = TwinUiRadii.CardSprite;
            _bg.type = Image.Type.Sliced;
            _bg.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_bg.rectTransform, 0f, 0f, 0f, 0f);
            TwinUIStyle.Outline(_bg.gameObject, TwinUIStyle.BorderColor);
            _outline = _bg.GetComponent<UnityEngine.UI.Outline>();

            var header = TwinUIStyle.Frame(_root, "CardHeader");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, HeaderHeight);

            string kicker = string.IsNullOrEmpty(module.Kicker) ? string.Empty : module.Kicker.ToUpperInvariant();
            _kicker = TwinUIStyle.Text(header, "Kicker", kicker, TwinUIStyle.FontKicker, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(_kicker.rectTransform, 12f, 4f, 220f, 12f);
            _kicker.gameObject.SetActive(false);

            _title = TwinUIStyle.Text(header, "Title", module.Title, TwinUIStyle.FontCardTitle, TwinUIStyle.TextColor);
            _title.overflowMode = TextOverflowModes.Ellipsis;
            // TMP Chinese line height exceeds the old 16px box (ellipsis rendered
            // zero characters). Give the primary title one full, readable line.
            _title.fontStyle = FontStyles.Bold;
            _title.enableWordWrapping = false;
            _title.rectTransform.anchorMin = new Vector2(0f, 1f);
            _title.rectTransform.anchorMax = new Vector2(1f, 1f);
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _title.rectTransform.anchoredPosition = new Vector2(12f, -6f);
            _title.rectTransform.sizeDelta = new Vector2(-140f, 24f);

            _statusDot = TwinUIStyle.StatusDot(header, "StatusDot", 6f, TwinUIStyle.QuietColor);
            TwinUIStyle.Place(_statusDot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -15f), new Vector2(6f, 6f));

            _statusText = TwinUIStyle.Text(header, "StatusText", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.QuietColor, TextAlignmentOptions.Right);
            TwinUIStyle.Place(_statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f, -11f), new Vector2(96f, 14f));

            var divider = TwinUIStyle.Panel(_root, "CardDivider", TwinUIStyle.Alpha(TwinUIStyle.BorderColor, 0.7f));
            divider.raycastTarget = false;
            TwinUIStyle.Place(divider.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -HeaderHeight), new Vector2(-24f, 1f));
            var drt = divider.rectTransform;
            drt.anchorMin = new Vector2(0f, 1f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.anchoredPosition = new Vector2(0f, -HeaderHeight);
            drt.sizeDelta = new Vector2(-24f, 1f);

            Content = TwinUIStyle.Frame(_root, "CardContent");
            TwinUIStyle.Stretch(Content, 12f, HeaderHeight + 8f, 12f, 10f);

            if (module.Detachable && onToggle != null)
            {
                // P2：图钉 = 独立悬浮 / 恢复停靠；控件区是独立水平布局容器。
                RectTransform cluster = TwinUiCardChrome.BuildCluster(_root, "CardControls");
                cluster.anchoredPosition = new Vector2(-TwinUIStyle.ControlRightMargin, -4f);
                _pinButton = TwinUiCardChrome.AddIcon(cluster, "BtnDetach", TwinUiIcon.Glyph.Pin,
                    "独立悬浮（拆出为浮窗）", () => onToggle(module.Id));
                DetachButton = _pinButton;

                // 状态文字让位给图标按钮（按钮宽 28 + 左上内缩）。
                TwinUIStyle.Place(_statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-40f, -11f), new Vector2(80f, 14f));
            }

            TwinUiRadii.Apply(_root);
            ToggleRequest = onToggle == null ? null : (System.Action<string>)(id => onToggle(id));
            DetachRequest = onDetach == null ? null : (System.Action<string>)(id => onDetach(id));
            // 验收补充（2026-11-17）：停靠卡标题栏拖动不再触发拆出；独立悬浮只由图钉触发。
            // 浮窗自身的拖动由 TwinUiWindowFrame 负责，不受影响。
            DragOut = _root.gameObject.AddComponent<TwinUiCardDragOut>();
            DragOut.Configure(module.Id, null, null);
            DragOut.enabled = false;
            ApplyThemeColors();
        }

        /// <summary>切换卡片处于停靠/悬浮（图钉 glyph 与 Tooltip 同步；占位卡也可点回）。</summary>
        public void SetFloatingState(bool floating)
        {
            _floating = floating;
            if (_pinButton == null)
            {
                return;
            }

            TwinUiCardChrome.SetGlyph(_pinButton,
                floating ? TwinUiIcon.Glyph.PinOff : TwinUiIcon.Glyph.Pin,
                floating ? "恢复停靠（回到卡片）" : "独立悬浮（拆出为浮窗）");
        }

        public bool IsFloatingState => _floating;

        /// <summary>当前卡片整体缩放（由 TwinUiCardGrid 按列宽算，测试用）。</summary>
        public float CurrentScale => _root != null ? _root.localScale.x : 1f;

        /// <summary>卡片设计尺寸（卡片根节点恒为设计尺寸，视觉大小由 localScale 决定）。</summary>
        public Vector2 DesignSize => _root != null ? _root.sizeDelta : Vector2.zero;

        private TMP_Text _placeholder;

        /// <summary>卡片被拆出为浮窗时，卡位显示"已拆出"提示（内容视图已搬到浮窗里，不重复实例）。</summary>
        public void SetPlaceholder(bool active, string text)
        {
            SetFloatingState(active);
            if (!active)
            {
                if (_placeholder != null)
                {
                    _placeholder.gameObject.SetActive(false);
                }

                if (Content != null)
                {
                    Content.gameObject.SetActive(true);
                }

                return;
            }

            if (Content != null)
            {
                Content.gameObject.SetActive(false);
            }

            if (_placeholder == null)
            {
                _placeholder = TwinUIStyle.Text(_root, "DetachedHint", text, TwinUIStyle.FontBodySm,
                    TwinUIStyle.QuietColor, TextAlignmentOptions.Center);
                _placeholder.enableWordWrapping = true;
                TwinUIStyle.Stretch(_placeholder.rectTransform, 12f, HeaderHeight + 4f, 12f, 10f);
            }

            _placeholder.text = text;
            _placeholder.gameObject.SetActive(true);
        }

        public void SetStatus(TwinUITone tone, string text)
        {
            if (_statusDot != null)
            {
                _statusDot.color = TwinUITheme.ToneColor(tone);
            }

            if (_statusText != null)
            {
                _statusText.text = text ?? string.Empty;
                _statusText.color = TwinUITheme.ToneTextColor(tone);
            }
        }

        public void SetTitleSuffix(string suffix)
        {
            if (_title == null)
            {
                return;
            }

            var module = TwinUiSurfaceHost.FindModule(ModuleId);
            string baseTitle = module != null ? module.Title : ModuleId;
            _title.text = string.IsNullOrEmpty(suffix) ? baseTitle : baseTitle + " · " + suffix;
        }

        public void ApplyThemeColors()
        {
            if (_bg != null)
            {
                _bg.color = TwinUIStyle.CardColor;
            }

            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.BorderColor;
            }

            if (_kicker != null)
            {
                _kicker.color = TwinUIStyle.QuietColor;
            }

            if (_title != null)
            {
                _title.color = TwinUIStyle.TextColor;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.AccentColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.BorderColor;
            }
        }

        public Rect Rect => _root != null ? _root.rect : Rect.zero;

        public RectTransform Root => _root;
    }
}
