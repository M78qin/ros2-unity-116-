// TwinUiRadii.cs —— 卡片/控件圆角（P6-3 / task-15，补 P2.5 遗留）
// 目标：把 RadiusCard=11 / RadiusControl=8 应用到既有卡片与按钮；只改 Image.sprite/type/pixelsPerUnitMultiplier，
//       不改布局/锚点/尺寸/颜色。实现方式：程序化生成 9-slice 圆角 Sprite（无外部资源、无 shader 依赖）。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public static class TwinUiRadii
    {
        public const float RadiusCard = 11f;
        public const float RadiusControl = 8f;
        public const string CardSpriteName = "UiRoundedCard11";
        public const string ControlSpriteName = "UiRoundedControl8";

        private const int TextureSize = 64;
        private static Sprite _cardSprite;
        private static Sprite _controlSprite;
        private static readonly List<Image> Buffer = new List<Image>();

        public static int AppliedCards { get; private set; }
        public static int AppliedControls { get; private set; }
        public static int LastApplied => AppliedCards + AppliedControls;
        public static int LastScanned { get; private set; }

        public static Sprite CardSprite => _cardSprite != null ? _cardSprite : (_cardSprite = Build(RadiusCard, CardSpriteName));
        public static Sprite ControlSprite => _controlSprite != null ? _controlSprite : (_controlSprite = Build(RadiusControl, ControlSpriteName));

        public static void ResetStats()
        {
            AppliedCards = 0;
            AppliedControls = 0;
            LastScanned = 0;
        }

        /// <summary>把圆角应用到 root 下所有"卡片/按钮"Image；幂等（已应用的跳过）。</summary>
        public static void Apply(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Buffer.Clear();
            root.GetComponentsInChildren(true, Buffer);
            LastScanned = Buffer.Count;
            for (int i = 0; i < Buffer.Count; i++)
            {
                var img = Buffer[i];
                if (img == null || img.rectTransform == null)
                {
                    continue;
                }

                if (img.rectTransform.rect.width < 24f || img.rectTransform.rect.height < 24f)
                {
                    continue;
                }

                bool isButton = img.GetComponent<TwinUIButton>() != null
                    || img.GetComponentInParent<TwinUIButton>() != null;
                if (isButton)
                {
                    if (img.sprite != ControlSprite)
                    {
                        img.sprite = ControlSprite;
                        img.type = Image.Type.Sliced;
                        img.pixelsPerUnitMultiplier = 1f;
                        AppliedControls++;
                    }

                    continue;
                }

                var tag = img.GetComponent<TwinUIToneTag>();
                if (tag == null)
                {
                    continue;
                }

                if (tag.Role != TwinUIToneRole.Panel && tag.Role != TwinUIToneRole.Card
                    && tag.Role != TwinUIToneRole.CardAlt && tag.Role != TwinUIToneRole.Layer
                    && tag.Role != TwinUIToneRole.Layer2)
                {
                    continue;
                }

                if (img.sprite != CardSprite)
                {
                    img.sprite = CardSprite;
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = 1f;
                    AppliedCards++;
                }
            }
        }

        private static Sprite Build(float radius, string name)
        {
            int size = TextureSize;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = name + "_Tex";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float inside = Mathf.Clamp01(r - dist + 1f);
                    byte a = (byte)Mathf.RoundToInt(inside * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            int border = Mathf.CeilToInt(r) + 1;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            return sprite;
        }
    }
}