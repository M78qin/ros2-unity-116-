using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 工业风按钮：五态（Normal/Hover/Pressed/Selected/Disabled）+ 四种语义（Default/Primary/Danger/Plain）。
    /// 禁用时必须给出原因（Tooltip 行），避免"点了没反应"。
    /// 三期：颜色全部改为读活动主题（深色/浅色），并提供 ApplyTheme() 供主题切换时重刷。
    /// </summary>
    public sealed class TwinUIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public enum Kind
        {
            Default = 0,
            Primary = 1,
            Danger = 2,
            Plain = 3
        }

        [SerializeField] private Image background;
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image accentBar;
        [SerializeField] private Kind kind = Kind.Default;
        [SerializeField] private bool selected;
        [SerializeField] private bool armed;
        [SerializeField] private bool hovered;
        [SerializeField] private bool pressed;
        [SerializeField] private string disabledReason = string.Empty;
        [SerializeField] private bool emphasiseSelected = true;

        [Header("悬停渐变（可选组件，只改顶点色不改材质）")]
        [Tooltip("指向背景 Image 上的 TwinUIGradientHover；留空时自动从背景 Image 获取。")]
        [SerializeField] private TwinUIGradientHover hoverGradient;
        [Tooltip("按下时的轻微缩放倍数（仅在有渐变组件且设置开启时生效）。")]
        [SerializeField, Range(0.9f, 1f)] private float pressedScale = 0.985f;

        private Vector3 _restScale = Vector3.one;
        private bool _scaledByUs;

        public Button Button => button;
        public TMP_Text Label => label;
        public Kind ButtonKind => kind;
        public bool DisabledReasonPresent => !string.IsNullOrEmpty(disabledReason);
        public bool IsHovered => hovered;
        public bool IsPressed => pressed;
        public Image Background => background;
        public TwinUIGradientHover Gradient => hoverGradient;

        public void Setup(Image bg, Button btn, TMP_Text txt, Kind k, Image bar)
        {
            background = bg;
            button = btn;
            label = txt;
            kind = k;
            accentBar = bar;
            hoverGradient = bg == null ? null : bg.GetComponent<TwinUIGradientHover>();
            Apply();
        }

        public void ApplyTheme()
        {
            Apply();
        }

        public TwinUIButton SetLabel(string value)
        {
            if (label != null)
            {
                label.text = value;
            }

            return this;
        }

        public TwinUIButton SetSelected(bool value)
        {
            selected = value;
            Apply();
            return this;
        }

        public TwinUIButton SetEmphasiseSelected(bool value)
        {
            emphasiseSelected = value;
            Apply();
            return this;
        }

        public TwinUIButton SetArmed(bool value)
        {
            armed = value;
            Apply();
            return this;
        }

        public TwinUIButton SetKind(Kind value)
        {
            kind = value;
            Apply();
            return this;
        }

        public TwinUIButton SetInteractable(bool value, string reason = null)
        {
            disabledReason = value ? string.Empty : (reason ?? "不可用");
            if (button != null)
            {
                button.interactable = value;
            }

            Apply();
            return this;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            Apply();
        }

        private void Apply()
        {
            if (background == null)
            {
                return;
            }

            TwinUIPalette p = TwinUITheme.Current;
            bool enabled = button == null || button.interactable;
            Color fill;
            Color edge;
            Color text;

            if (!enabled)
            {
                fill = p.BtnDisabledFill;
                edge = p.BtnEdge;
                text = p.BtnDisabledText;
            }
            else if (armed)
            {
                fill = p.BtnArmedFill;
                edge = p.Crit;
                text = p.CritText;
            }
            else if (kind == Kind.Primary)
            {
                fill = pressed ? p.BtnPrimaryFillPressed
                    : hovered ? p.BtnPrimaryFillHover : p.BtnPrimaryFill;
                edge = p.Accent;
                text = p.BtnPrimaryText;
            }
            else if (kind == Kind.Danger)
            {
                fill = pressed ? p.BtnDangerFillPressed
                    : hovered ? p.BtnDangerFillHover : p.BtnDangerFill;
                edge = p.Crit;
                text = p.BtnDangerText;
            }
            else if (kind == Kind.Plain)
            {
                fill = pressed ? p.BtnFillPressed : hovered ? p.BtnFillHover : TwinUIStyle.Transparent;
                edge = TwinUIStyle.Transparent;
                text = hovered ? p.Text : p.Muted;
            }
            else
            {
                fill = pressed ? p.BtnFillPressed : hovered ? p.BtnFillHover : p.BtnFill;
                edge = hovered ? p.BtnEdgeHover : p.BtnEdge;
                text = p.BtnText;
            }

            if (selected && enabled && emphasiseSelected)
            {
                fill = p.BtnSelectedFill;
                edge = p.BtnSelectedEdge;
                text = p.BtnSelectedText;
            }

            background.color = fill;
            TwinUIStyle.Outline(background.gameObject, edge);

            if (label != null)
            {
                label.color = text;
            }

            if (accentBar != null)
            {
                accentBar.color = selected && enabled ? p.Accent : TwinUIStyle.Transparent;
            }

            var tag = background.GetComponent<TwinUIToneTag>();
            if (tag != null && tag.Role != TwinUIToneRole.None)
            {
                // 按钮自管颜色，取消标签，避免主题重刷时被覆盖成静态色。
                tag.Role = TwinUIToneRole.None;
            }

            ApplyHoverGradient(fill, p.Accent);
        }

        /// <summary>
        /// 有渐变组件时驱动顶点色渐变与按下轻微缩放；无组件时保持纯色（旧行为）。
        /// 注意：只改顶点色与本地缩放，不改 Image.color，因此 UI/12 的令牌/对比度审计不受影响。
        /// </summary>
        private void ApplyHoverGradient(Color fill, Color accent)
        {
            if (hoverGradient == null && background != null)
            {
                hoverGradient = background.GetComponent<TwinUIGradientHover>();
            }

            bool gradientOn = hoverGradient != null && hoverGradient.EnabledBySettings;
            if (hoverGradient != null)
            {
                hoverGradient.SetState(fill, accent, hovered, pressed);
            }

            if (gradientOn && pressed)
            {
                if (!_scaledByUs)
                {
                    _restScale = transform.localScale;
                    _scaledByUs = true;
                }

                transform.localScale = new Vector3(_restScale.x * pressedScale, _restScale.y * pressedScale, _restScale.z);
            }
            else if (_scaledByUs)
            {
                transform.localScale = _restScale;
                _scaledByUs = false;
            }
        }
    }
}