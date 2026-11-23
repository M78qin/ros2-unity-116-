using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    // Adapts existing content geometry without scaling the GameObject or rebuilding
    // its business view. Responsive modules retain their own width-layout method.
    [DisallowMultipleComponent]
    public sealed class TwinUiContentFit : MonoBehaviour
    {
        struct Node
        {
            public RectTransform Rect;
            public Vector2 Min, Max, Position, Size;
            public float Left, Right;
        }
        struct TextSize { public TMP_Text Text; public float Size; }
        readonly List<Node> nodes = new List<Node>();
        readonly List<TextSize> texts = new List<TextSize>();
        TwinUiModule module;
        RectTransform root;
        float lastWidth = -1, lastHeight = -1;
        float legacyHeight = -1;
        TwinUiModuleSurface lastSurface;
        int lastVersion = -1;

        public static float CardFactor(float width, TwinUiModule module)
        {
            // P4：主面板卡片按列宽铺满，但字号/行距不随卡宽放大（字号统一）；
            // 高度保持设计体高，多余宽度由锚点拉伸，不产生“字大字小”混排。
            return 1f;
        }
        public static float LegacyCardHeight(float width, TwinUiModule module)
        {
            var fit = module.View != null ? module.View.GetComponent<TwinUiContentFit>() : null;
            return 54f + (fit != null ? fit.MeasureLegacyBody() : Mathf.Max(96f, module.CardSize.y));
        }
        public static void Attach(TwinUiModule module)
        {
            if (module == null || module.View == null) return;
            var fit = module.View.GetComponent<TwinUiContentFit>();
            if (fit == null) fit = module.View.gameObject.AddComponent<TwinUiContentFit>();
            fit.module = module; fit.root = module.View;
        }
        public void Restore()
        {
            foreach (var n in nodes)
            {
                if (n.Rect == null) continue;
                n.Rect.anchorMin = n.Min; n.Rect.anchorMax = n.Max;
                n.Rect.anchoredPosition = n.Position; n.Rect.sizeDelta = n.Size;
            }
            foreach (var t in texts) if (t.Text != null) t.Text.fontSize = t.Size;
        }
        public void Invalidate()
        {
            Restore();
            // The owner may run a native layout immediately after this call.
            // Do not restore the previous-width snapshot over that fresh layout.
            nodes.Clear(); texts.Clear(); lastWidth = -1; lastVersion = -1;
            // P11：内容结构变化（行数/空态/区块显隐）与宽度无关，必须让旧模板的
            // 实测体高缓存一起失效，否则只有换宽才重测、同宽下卡片高度永远停在旧值。
            legacyHeight = -1;
        }
        float MeasureLegacyBody()
        {
            if (legacyHeight > 0) return legacyHeight;
            Vector2 size = root.sizeDelta, position = root.anchoredPosition;
            float measured = Mathf.Max(96f, module.CardSize.y - 54f);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(200f, module.CardSize.x - 24f));
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, measured);
            var corners = new Vector3[4];
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == root) continue;
                rt.GetWorldCorners(corners);
                measured = Mathf.Max(measured, root.rect.yMax - root.InverseTransformPoint(corners[0]).y);
            }
            root.sizeDelta = size; root.anchoredPosition = position;
            legacyHeight = measured + 12f;
            return legacyHeight;
        }
        void LateUpdate()
        {
            if (module == null || root == null || root.rect.width < 40 || root.rect.height < 40) return;
            float width = root.rect.width, height = root.rect.height;
            int version = module.LayoutVersion;
            bool contentChanged = lastVersion != version;
            if (!contentChanged && Mathf.Abs(lastWidth - width) < .1f
                && Mathf.Abs(lastHeight - height) < .1f && lastSurface == module.Surface) return;
            // 结构变化时丢弃实测体高缓存并重测一次；纯尺寸变化沿用缓存。
            if (contentChanged) legacyHeight = -1;
            Restore();
            bool responsive = module.ResponsiveContent;
            bool floating = module.Surface == TwinUiModuleSurface.Floating;
            if (responsive) module.ApplyContentLayout(width, module.Surface);
            // Use the height allocated by the row/window, including a taller
            // adjacent card, rather than leaving the TCP contents tiny at the top.
            float designWidth = responsive ? width : Mathf.Max(200f, module.CardSize.x - 24f);
            float designHeight = responsive ? module.MeasureContentHeight(width, module.Surface)
                : MeasureLegacyBody();
            Vector2 size = root.sizeDelta, position = root.anchoredPosition;
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, designWidth);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, designHeight);
            nodes.Clear(); texts.Clear();
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == root) continue;
                var parent = rt.parent as RectTransform;
                if (parent == null || parent.rect.width < 1) continue;
                float left = rt.localPosition.x + rt.rect.xMin - parent.rect.xMin;
                nodes.Add(new Node { Rect = rt, Min = rt.anchorMin, Max = rt.anchorMax,
                    Position = rt.anchoredPosition, Size = rt.sizeDelta,
                    Left = left / parent.rect.width, Right = (left + rt.rect.width) / parent.rect.width });
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) texts.Add(new TextSize { Text = t, Size = t.fontSize });
            root.sizeDelta = size; root.anchoredPosition = position;
            float geometry = Mathf.Max(.9f, height / Mathf.Max(1f, designHeight));
            // P4：卡片表面不放大字号（统一 13/22/11 字阶）；浮窗内容允许 1.35× 以填满高度。
            float fontFactor = Mathf.Clamp(geometry, 1f, floating ? 1.35f : 1f);
            foreach (var n in nodes)
            {
                // Small status dots/icons retain their aspect instead of becoming
                // wide rectangles when the template expands horizontally.
                if (n.Size.x > 0f && n.Size.x <= 28f && n.Rect.GetComponent<TMP_Text>() == null)
                {
                    float pivotX = Mathf.Lerp(n.Left, n.Right, n.Rect.pivot.x);
                    n.Rect.anchorMin = new Vector2(pivotX, n.Min.y);
                    n.Rect.anchorMax = new Vector2(pivotX, n.Max.y);
                    n.Rect.anchoredPosition = new Vector2(0f, n.Position.y * geometry);
                    float iconSize = n.Size.x * Mathf.Min(fontFactor, 1.4f);
                    n.Rect.sizeDelta = new Vector2(iconSize, n.Size.y * Mathf.Min(fontFactor, 1.4f));
                    continue;
                }
                n.Rect.anchorMin = new Vector2(n.Left, n.Min.y);
                n.Rect.anchorMax = new Vector2(n.Right, n.Max.y);
                n.Rect.anchoredPosition = new Vector2(0f, n.Position.y * geometry);
                n.Rect.sizeDelta = new Vector2(0f, n.Size.y * geometry);
            }
            foreach (var t in texts) if (t.Text != null) t.Text.fontSize = Mathf.Min(28f, t.Size * fontFactor);
            lastWidth = width; lastHeight = height; lastSurface = module.Surface; lastVersion = version;
        }
        void OnDisable() { /* Keep geometry and caches intact across visibility changes. */ }
    }
}
