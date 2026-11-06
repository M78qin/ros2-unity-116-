using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// UI 设计令牌（三期：工业级数字孪生平台，深色默认 + 浅色可切换）。
    /// 所有面板/按钮/文字必须从这里取色和尺寸，便于统一调整与审计（菜单 UI/12）。
    /// 三期改动：颜色改为读 TwinUITheme.Current（属性而非 readonly 字段），
    /// 并在创建图元时打 TwinUIToneTag 角色标签，使主题切换无需重建层级。
    /// 发光令牌（Glow / AccentGlow）只用于状态点与选中态，正文禁止使用。
    /// </summary>
    public static class TwinUIStyle
    {
        // ---- 颜色令牌（读活动主题）---------------------------------------
        private static TwinUIPalette P => TwinUITheme.Current;

        public static Color BgColor => P.Bg;
        public static Color PanelColor => P.Panel;
        public static Color CardColor => P.Card;
        public static Color CardAltColor => P.CardAlt;
        public static Color BorderColor => P.Border;
        public static Color TextColor => P.Text;
        public static Color MutedColor => P.Muted;
        public static Color AccentColor => P.Accent;
        public static Color Accent2Color => P.Accent2;
        public static Color OkColor => P.Ok;
        public static Color WarnColor => P.Warn;
        public static Color CritColor => P.Crit;
        public static Color CritTextColor => P.CritText;
        public static Color InfoColor => P.Info;
        public static Color DisabledColor => P.Disabled;
        public static Color GlowColor => P.Glow;
        public static Color PlotBgColor => P.PlotBg;
        public static Color GridColor => P.Grid;
        public static Color RowAltColor => P.RowAlt;
        public static Color LayerColor => P.Layer;
        public static Color Layer2Color => P.Layer2;
        public static Color LineStrongColor => P.LineStrong;
        public static Color StrongColor => P.Strong;
        public static Color ShadowColor => P.Shadow;
        public static float UiScale => P.UiScale <= 0f ? 1f : P.UiScale;
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        public static Color[] SeriesColors => P.Series;

        // ---- 尺寸令牌（8px 栅格）------------------------------------------
        public const float Space = 8f;
        public const float Pad = 12f;
        public const float Radius = 4f;

        // V3.1 圆角分档（原型 13 / 11 / 8）：窗口外壳用 RadiusWindow，卡片/控件分档留给 P3 的 builder 重排，
        // 本轮只落地窗口外壳，避免改动既有布局几何。
        public const float RadiusWindow = 13f;
        public const float RadiusCard = 11f;
        public const float RadiusControl = 8f;

        // V3.1 八向缩放手柄命中区：边 11px、角 23×23（原型 CSS 实测值）。
        public const float HandleEdgeSize = 11f;
        public const float HandleCornerSize = 23f;
        public const float HandleHoverLine = 2f;

        // 原型标题栏 41px（CSS px，非设计单位）；窗口标题栏在本工程为 Header 容器（56 设计单位），
        // 这里只作为"原型参考值"登记，实际高度取 Header 缩放后的矩形。
        public const float WindowTitleBarReference = 41f;
        public const float ButtonHeight = 40f;
        public const float ButtonHeightVr = 48f;
        public const float HeaderHeight = 56f;
        public const float ContextBarHeight = 44f;

        public const float FontTitle = 24f;
        public const float FontSection = 18f;
        public const float FontBody = 16f;
        public const float FontCaption = 13f;
        public const float FontMono = 16f;

        // ---- 基础件 ------------------------------------------------------

        public static Color Hex(string hex)
        {
            return TwinUITheme.Hex(hex);
        }

        public static Color Alpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>正文/标签字体：TMP 默认静态 SDF（NotoSansSC-UI，含 1368 字形）。</summary>
        public static TMP_FontAsset GetFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }

        /// <summary>
        /// 数值字体。工程未引入第三方等宽字体；实测当前静态 SDF 的 10 个数字字形
        /// HorizontalAdvance 完全相等（39.953125，spread=0.0），即数字本身是等宽的，
        /// 配合固定小数位与定宽列即可得到表格对齐（见 UI/12 审计的 digitAdvanceSpread）。
        /// </summary>
        public static TMP_FontAsset GetMonoFont()
        {
            return GetFont();
        }

        public static RectTransform Frame(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            TwinUITheme.Tag(go, color);
            return img;
        }

        public static Image Card(Transform parent, string name, Color color)
        {
            var img = Panel(parent, name, color);
            Outline(img.gameObject, BorderColor);
            return img;
        }

        public static void Outline(GameObject go, Color color)
        {
            var o = go.GetComponent<UnityEngine.UI.Outline>();
            if (o == null)
            {
                o = go.AddComponent<UnityEngine.UI.Outline>();
            }

            o.effectColor = color;
            o.effectDistance = new Vector2(1f, -1f);
            o.useGraphicAlpha = false;
        }

        public static TMP_Text Text(Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = GetFont();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            TwinUITheme.Tag(go, color);
            return t;
        }

        /// <summary>定宽数值文本（等宽数字 + 右对齐），用于"当前/目标/误差"列。</summary>
        public static TMP_Text Mono(Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Right)
        {
            var t = Text(parent, name, content, size, color, align);
            t.font = GetMonoFont();
            return t;
        }

        public static Image Dot(Transform parent, string name, Vector2 pos, float size = 10f, Color? color = null)
        {
            var img = Panel(parent, name, color ?? MutedColor);
            Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                pos, new Vector2(size, size));
            return img;
        }

        /// <summary>按锚点放置（anchorMin=anchorMax，pivot=anchor，pos 为相对锚点偏移）。</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>左上角定位的一行（常用于卡片内竖向排布）。</summary>
        public static void Row(RectTransform rt, float x, float y, float w, float h)
        {
            Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
        }

        public static TwinUIButton Button(Transform parent, string name, string label, TwinUIButton.Kind kind,
            Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TwinUIButton));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);

            var bg = go.GetComponent<Image>();
            bg.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = bg;

            var view = go.GetComponent<TwinUIButton>();
            var txt = Text(rt, "Label", label, FontBody, TextColor, TextAlignmentOptions.Center);
            Stretch(txt.rectTransform, 8f, 0f, 8f, 0f);
            view.Setup(bg, btn, txt, kind, null);
            return view;
        }

        public static TwinUIButton Button(Transform parent, string name, string label, TwinUIButton.Kind kind,
            Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var b = Button(parent, name, label, kind, pos, size);
            b.Button.onClick.AddListener(action);
            return b;
        }

        /// <summary>WCAG 相对亮度对比度，用于审计（正文 ≥4.5，大字 ≥3）。</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            if (la < lb)
            {
                float t = la;
                la = lb;
                lb = t;
            }

            return (la + 0.05f) / (lb + 0.05f);
        }

        public static float RelativeLuminance(Color c)
        {
            float r = Channel(c.r);
            float g = Channel(c.g);
            float b = Channel(c.b);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        private static float Channel(float v)
        {
            return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>状态点：只有状态使用发光，正文不用。</summary>
        public static Image StatusDot(Transform parent, string name, float size, Color tone)
        {
            var img = Panel(parent, name, tone);
            var outline = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Alpha(tone, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
            if (size > 0f)
            {
                img.rectTransform.sizeDelta = new Vector2(size, size);
            }

            return img;
        }
    }
}