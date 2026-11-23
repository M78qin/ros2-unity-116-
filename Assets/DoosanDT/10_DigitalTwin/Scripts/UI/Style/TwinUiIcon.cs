// TwinUiIcon.cs —— 程序化矢量图标（P10.2）
// 目标：标题栏/工具条按钮不再依赖中文字形或外部图片，用 32×32 纯 alpha 遮罩 Sprite 表达六个符号。
// 做法：照 TwinUiRadii 的既有模式（Texture2D + SetPixels32 + Sprite.Create），RGB 全白、alpha 为形状遮罩，
//       颜色完全交给 Image.color（主题切换/悬停只需改 color，不重建贴图）。
// 约束：零外部资源、无字体依赖、无 shader；只用 UnityEngine / UnityEngine.UI / UnityEngine.EventSystems。
// 注意：TwinUiRadii.Apply(root) 会把"带 TwinUIButton 的 Image"换成圆角控件 Sprite，
//       TwinUIButton.Apply() 又会把 Plain 按钮的 fill 改成 Transparent —— 两者都会让图标消失，
//       因此 CreateIconButton 会挂一个极轻量的 TwinUiIconKeeper，仅在检测到 sprite/color 被外部改动时修回。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>程序化图标库：同一 glyph 只生成一次 Sprite，之后按引用复用。</summary>
    public static class TwinUiIcon
    {
        /// <summary>图标语义。数值即缓存下标，不要重排。</summary>
        public enum Glyph
        {
            Detach = 0,
            Pin = 1,
            Minimize = 2,
            Maximize = 3,
            Restore = 4,
            Close = 5,

            /// <summary>取消悬浮 / 恢复停靠：图钉 + 斜杠。</summary>
            PinOff = 6
        }

        private const int TextureSize = 32;
        private const float SpritePixelsPerUnit = 100f;
        private static readonly Sprite[] Cache = new Sprite[7];

        /// <summary>取（或首次生成）glyph 的白色遮罩 Sprite；非法值回退 Detach。</summary>
        public static Sprite Get(Glyph glyph)
        {
            int index = (int)glyph;
            if (index < 0 || index >= Cache.Length)
            {
                index = (int)Glyph.Detach;
            }

            Sprite sprite = Cache[index];
            if (sprite == null)
            {
                sprite = Build((Glyph)index);
                Cache[index] = sprite;
            }

            return sprite;
        }

        /// <summary>把 Image 换成图标（Simple + 保持比例）；颜色由 Image.color 决定，不在这里改。</summary>
        public static void SetGlyph(Image image, Glyph glyph)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = Get(glyph);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>
        /// 造一个无字图标按钮：命中区 = size×size（按钮自身 Image），Label 隐藏。
        /// 位置默认在父级左上角，调用方用 TwinUIStyle.Place / Row 再定位。
        /// </summary>
        public static TwinUIButton CreateIconButton(Transform parent, string name, Glyph glyph, float size,
            UnityAction onClick)
        {
            TwinUIButton view = TwinUIStyle.Button(parent, name, string.Empty, TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(size, size), onClick);
            if (view == null)
            {
                return null;
            }

            Image image = view.Background;
            if (image == null)
            {
                return view;
            }

            SetGlyph(image, glyph);
            if (view.Label != null)
            {
                // 无字按钮：保留节点但隐藏，避免 TMP 空文本占位/参与布局。
                view.Label.text = string.Empty;
                view.Label.gameObject.SetActive(false);
            }

            TwinUiIconKeeper keeper = image.gameObject.GetComponent<TwinUiIconKeeper>();
            if (keeper == null)
            {
                keeper = image.gameObject.AddComponent<TwinUiIconKeeper>();
            }

            keeper.Configure(glyph);
            return view;
        }

        /// <summary>可选：覆盖默认三态色（默认 Muted → Text，按下 Accent 跟随主题）。</summary>
        public static void SetTint(TwinUIButton view, Color normal, Color hot)
        {
            if (view == null || view.Background == null)
            {
                return;
            }

            TwinUiIconKeeper keeper = view.Background.GetComponent<TwinUiIconKeeper>();
            if (keeper != null)
            {
                keeper.SetTint(normal, hot);
            }
        }

        /// <summary>切换既有图标按钮的 glyph（最大↔还原、图钉↔取消图钉）；不重建按钮。</summary>
        public static void SetButtonGlyph(TwinUIButton view, Glyph glyph)
        {
            if (view == null || view.Background == null)
            {
                return;
            }

            Image image = view.Background;
            SetGlyph(image, glyph);
            TwinUiIconKeeper keeper = image.GetComponent<TwinUiIconKeeper>();
            if (keeper == null)
            {
                keeper = image.gameObject.AddComponent<TwinUiIconKeeper>();
            }

            keeper.Configure(glyph);
            if (view.Label != null)
            {
                view.Label.text = string.Empty;
                view.Label.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- 生成

        private static Sprite Build(Glyph glyph)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "UiIcon_" + glyph + "_Tex";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float alpha = Mathf.Clamp01(Coverage(glyph, x + 0.5f, y + 0.5f));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "UiIcon_" + glyph;
            return sprite;
        }

        /// <summary>逐 glyph 合成覆盖率（0..1）：形状用像素/距离场判定，边缘留 0.5px 抗锯齿。</summary>
        private static float Coverage(Glyph glyph, float px, float py)
        {
            switch (glyph)
            {
                case Glyph.Detach:
                {
                    // 左上角"窗口框"轮廓 + 指向右上角的出框箭头。
                    float m = Outline(px, py, 3.5f, 3.5f, 18.5f, 18.5f, 1.0f);
                    m = Mathf.Max(m, Stroke(px, py, 15.5f, 15.5f, 27.5f, 27.5f, 1.0f));
                    m = Mathf.Max(m, Stroke(px, py, 27.5f, 27.5f, 20.5f, 27.5f, 1.0f));
                    m = Mathf.Max(m, Stroke(px, py, 27.5f, 27.5f, 27.5f, 20.5f, 1.0f));
                    return m;
                }

                case Glyph.Pin:
                {
                    // 圆头 + 收窄的尖尾 + 一条横向钉座。
                    float m = Cover(SdCircle(px, py, 16f, 23.2f, 4.2f));
                    m = Mathf.Max(m, Stroke(px, py, 12.7f, 19.4f, 16f, 8.0f, 1.1f));
                    m = Mathf.Max(m, Stroke(px, py, 19.3f, 19.4f, 16f, 8.0f, 1.1f));
                    m = Mathf.Max(m, Stroke(px, py, 9f, 13.8f, 23f, 13.8f, 0.85f));
                    return m;
                }

                case Glyph.Minimize:
                {
                    // 一条水平粗线。
                    return Stroke(px, py, 8f, 16f, 24f, 16f, 1.6f);
                }

                case Glyph.Maximize:
                {
                    // 方框细轮廓 + 四角加粗短边（8 段，2 个方向 × 4 个角）。
                    float m = Outline(px, py, 5f, 5f, 27f, 27f, 0.9f);
                    m = Mathf.Max(m, Stroke(px, py, 5f, 27f, 11f, 27f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 5f, 27f, 5f, 21f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 27f, 27f, 21f, 27f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 27f, 27f, 27f, 21f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 5f, 5f, 11f, 5f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 5f, 5f, 5f, 11f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 27f, 5f, 21f, 5f, 1.6f));
                    m = Mathf.Max(m, Stroke(px, py, 27f, 5f, 27f, 11f, 1.6f));
                    return m;
                }

                case Glyph.Restore:
                {
                    // 两个错位方框轮廓：后框偏左上、前框偏右下。
                    float m = Outline(px, py, 4.5f, 10.5f, 20.5f, 27.5f, 1.0f);
                    m = Mathf.Max(m, Outline(px, py, 11.5f, 4.5f, 27.5f, 21.5f, 1.0f));
                    return m;
                }

                case Glyph.Close:
                {
                    // 两条 45° 交叉线。
                    float m = Stroke(px, py, 8f, 8f, 24f, 24f, 1.6f);
                    m = Mathf.Max(m, Stroke(px, py, 8f, 24f, 24f, 8f, 1.6f));
                    return m;
                }

                case Glyph.PinOff:
                {
                    // 与 Pin 同形，再叠一条右下方向的斜杠（取消悬浮/恢复停靠）。
                    float m = Cover(SdCircle(px, py, 16f, 23.2f, 4.2f));
                    m = Mathf.Max(m, Stroke(px, py, 12.7f, 19.4f, 16f, 8.0f, 1.1f));
                    m = Mathf.Max(m, Stroke(px, py, 19.3f, 19.4f, 16f, 8.0f, 1.1f));
                    m = Mathf.Max(m, Stroke(px, py, 9f, 13.8f, 23f, 13.8f, 0.85f));
                    m = Mathf.Max(m, Stroke(px, py, 7f, 26f, 26f, 7f, 1.5f));
                    return m;
                }

                default:
                    return 0f;
            }
        }

        // ---------------------------------------------------------------- 距离场基元

        private static float Cover(float signedDistance)
        {
            return Mathf.Clamp01(0.5f - signedDistance);
        }

        /// <summary>矩形填充的有符号距离（内部为负）。</summary>
        private static float SdRect(float px, float py, float x0, float y0, float x1, float y1)
        {
            float cx = (x0 + x1) * 0.5f;
            float cy = (y0 + y1) * 0.5f;
            float hx = Mathf.Abs(x1 - x0) * 0.5f;
            float hy = Mathf.Abs(y1 - y0) * 0.5f;
            float dx = Mathf.Abs(px - cx) - hx;
            float dy = Mathf.Abs(py - cy) - hy;
            float ox = Mathf.Max(dx, 0f);
            float oy = Mathf.Max(dy, 0f);
            float outside = Mathf.Sqrt(ox * ox + oy * oy);
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        /// <summary>矩形线条（描边）：|sdRect| - halfStroke。</summary>
        private static float Outline(float px, float py, float x0, float y0, float x1, float y1, float halfStroke)
        {
            return Cover(Mathf.Abs(SdRect(px, py, x0, y0, x1, y1)) - halfStroke);
        }

        /// <summary>线段（胶囊）：点到线段距离 - 半宽。</summary>
        private static float Stroke(float px, float py, float ax, float ay, float bx, float by, float halfWidth)
        {
            return Cover(SdSegment(px, py, ax, ay, bx, by) - halfWidth);
        }

        private static float SdSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax;
            float ey = by - ay;
            float wx = px - ax;
            float wy = py - ay;
            float len2 = ex * ex + ey * ey;
            float t = len2 > 1e-6f ? Mathf.Clamp01((wx * ex + wy * ey) / len2) : 0f;
            float dx = wx - ex * t;
            float dy = wy - ey * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float SdCircle(float px, float py, float cx, float cy, float radius)
        {
            float dx = px - cx;
            float dy = py - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }
    }

    /// <summary>
    /// 内部小工具：让"按钮自身 Image = 图标"这条路线在既有样式系统下稳定可见。
    /// 原因有两个：① TwinUIButton 的 Plain 填充在非悬停态是 Transparent，直接把 Image 当图标会看不见；
    /// ② TwinUiRadii.Apply(root) 会把按钮 Image 换成圆角 Sprite。这里只在检测到漂移时修回，不每帧写值。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TwinUiIconKeeper : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image _image;
        private TwinUiIcon.Glyph _glyph = TwinUiIcon.Glyph.Detach;
        private bool _hot;
        private bool _pressed;
        private bool _customTint;
        private Color _normal;
        private Color _hotColor;

        internal void Configure(TwinUiIcon.Glyph glyph)
        {
            _glyph = glyph;
            EnsureImage();
            Apply();
        }

        internal void SetTint(Color normal, Color hot)
        {
            _normal = normal;
            _hotColor = hot;
            _customTint = true;
            EnsureImage();
            Apply();
        }

        private void OnEnable()
        {
            EnsureImage();
            Apply();
        }

        private void LateUpdate()
        {
            // 只在被外部改脏（sprite 被圆角化 / color 被按钮状态机覆盖 / 主题切换）时赋值。
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hot = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hot = false;
            _pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            Apply();
        }

        private void EnsureImage()
        {
            if (_image == null)
            {
                _image = GetComponent<Image>();
            }
        }

        private void Apply()
        {
            EnsureImage();
            if (_image == null)
            {
                return;
            }

            Sprite want = TwinUiIcon.Get(_glyph);
            if (_image.sprite != want)
            {
                TwinUiIcon.SetGlyph(_image, _glyph);
            }

            Color normal = _customTint ? _normal : TwinUIStyle.MutedColor;
            Color hot = _customTint ? _hotColor : TwinUIStyle.TextColor;
            Color pressed = _customTint ? _hotColor : TwinUIStyle.AccentColor;
            Color wantColor = _pressed ? pressed : (_hot ? hot : normal);
            if (_image.color != wantColor)
            {
                _image.color = wantColor;
            }
        }
    }
}