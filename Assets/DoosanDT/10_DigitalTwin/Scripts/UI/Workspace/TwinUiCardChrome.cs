// TwinUiCardChrome.cs —— 子卡片/浮窗标题栏统一图标控件集群（P2）
// 目标：控件区是一个独立的水平布局容器（HorizontalLayoutGroup + ContentSizeFitter），
//       按钮统一 28×28、间距 4、右缩 8，全部带中文 Tooltip；禁止用汉字当控制按钮。
// 约束：不持有业务状态；按钮点击只回调宿主（ToggleFloating / Maximize / Close）。
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public static class TwinUiCardChrome
    {
        /// <summary>造一个右上角控件集群；父节点通常是卡片根或浮窗根。</summary>
        public static RectTransform BuildCluster(Transform parent, string name)
        {
            RectTransform rt = TwinUIStyle.Frame(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-TwinUIStyle.ControlRightMargin, -6f);
            rt.sizeDelta = new Vector2(Width(2), TwinUIStyle.ControlButtonSize);

            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = TwinUIStyle.ControlButtonGap;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        /// <summary>集群内追加一个 28×28 图标按钮（带 Tooltip 与统一热区）。</summary>
        public static TwinUIButton AddIcon(RectTransform cluster, string name, TwinUiIcon.Glyph glyph,
            string tooltip, UnityAction onClick)
        {
            if (cluster == null)
            {
                return null;
            }

            TwinUIButton button = TwinUiIcon.CreateIconButton(cluster, name, glyph,
                TwinUIStyle.ControlButtonSize, onClick);
            if (button == null)
            {
                return null;
            }

            var element = button.gameObject.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = button.gameObject.AddComponent<LayoutElement>();
            }

            element.minWidth = TwinUIStyle.ControlButtonSize;
            element.preferredWidth = TwinUIStyle.ControlButtonSize;
            element.minHeight = TwinUIStyle.ControlButtonSize;
            element.preferredHeight = TwinUIStyle.ControlButtonSize;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            TwinUiTooltip.Attach(button.gameObject, tooltip);
            TwinUiIcon.SetTint(button, TwinUIStyle.MutedColor, TwinUIStyle.TextColor);
            return button;
        }

        /// <summary>切 glyph 与 Tooltip（图钉↔取消图钉、最大↔还原）。</summary>
        public static void SetGlyph(TwinUIButton button, TwinUiIcon.Glyph glyph, string tooltip)
        {
            if (button == null)
            {
                return;
            }

            TwinUiIcon.SetButtonGlyph(button, glyph);
            TwinUiTooltip.Attach(button.gameObject, tooltip);
        }

        /// <summary>集群在给定按钮数下的设计宽度（用于标题让位）。</summary>
        public static float Width(int count)
        {
            if (count <= 0)
            {
                return 0f;
            }

            return count * TwinUIStyle.ControlButtonSize
                + (count - 1) * TwinUIStyle.ControlButtonGap
                + TwinUIStyle.ControlRightMargin;
        }
    }
}