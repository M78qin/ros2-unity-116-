// TwinUiCardParts.cs —— 卡片内部共用积木（P8）
// 设计目标：所有卡片用同一套"数值块 / 键值行 / 状态徽标 / 进度条 / 空态"，避免每张卡自己发明间距与字阶
//           （这正是旧页"每页各自排版"的来源）。
// 约定：坐标一律以卡片内容区左上角为原点、y 向下为正（TwinUIStyle.Row 语义）；宽度为内容区可用宽度。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public static class TwinUiCardParts
    {
        public const float StatHeight = 54f;
        public const float RowHeight = 22f;

        /// <summary>大数值块：小标签 + 22px 数值 + 单位。</summary>
        public struct Stat
        {
            public TMP_Text Label;
            public TMP_Text Value;
            public TMP_Text Unit;

            public void Set(string value, TwinUITone tone = TwinUITone.Ok, string unitOverride = null)
            {
                if (Value != null)
                {
                    Value.text = value ?? TwinUITextR3.Placeholder;
                    Value.color = tone == TwinUITone.Unknown
                        ? TwinUIStyle.QuietColor
                        : TwinUITheme.ToneTextColor(tone);
                }

                if (Unit != null && unitOverride != null)
                {
                    Unit.text = unitOverride;
                }
            }

            public void SetUnit(string unit)
            {
                if (Unit != null)
                {
                    Unit.text = unit ?? string.Empty;
                }
            }
        }

        /// <summary>键值行：左侧 11px 说明，右侧 12px 只读值（右对齐、等宽数字）。</summary>
        public struct Kv
        {
            public TMP_Text Label;
            public TMP_Text Value;

            public void Set(string value, TwinUITone tone = TwinUITone.Ok)
            {
                if (Value != null)
                {
                    Value.text = string.IsNullOrEmpty(value) ? TwinUITextR3.Placeholder : value;
                    Value.color = tone == TwinUITone.Unknown
                        ? TwinUIStyle.QuietColor
                        : (tone == TwinUITone.Ok ? TwinUIStyle.TextColor : TwinUITheme.ToneTextColor(tone));
                }
            }
        }

        /// <summary>状态徽标：圆点 + 文字（颜色 + 文字双通道，不只靠颜色）。</summary>
        public struct Badge
        {
            public Image Dot;
            public TMP_Text Text;

            public void Set(TwinUITone tone, string text)
            {
                if (Dot != null)
                {
                    Dot.color = TwinUITheme.ToneColor(tone);
                }

                if (Text != null)
                {
                    Text.text = text ?? TwinUITextR3.Placeholder;
                    Text.color = TwinUITheme.ToneTextColor(tone);
                }
            }
        }

        /// <summary>水平进度条（0..1）。</summary>
        public struct Bar
        {
            public Image Track;
            public Image Fill;
            public TMP_Text Caption;

            public void Set(float ratio, TwinUITone tone = TwinUITone.Ok, string caption = null)
            {
                float r = Mathf.Clamp01(ratio);
                if (Fill != null)
                {
                    var rt = Fill.rectTransform;
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(Mathf.Max(0.0001f, r), 1f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    Fill.color = TwinUITheme.ToneColor(tone);
                }

                if (Caption != null && caption != null)
                {
                    Caption.text = caption;
                }
            }
        }

        public static Stat StatBlock(Transform parent, string label, float x, float y, float width)
        {
            var root = TwinUIStyle.Frame(parent, "Stat");
            TwinUIStyle.Row(root, x, y, width, StatHeight);

            var caption = TwinUIStyle.Text(root, "Label", label, TwinUIStyle.FontUnit, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(caption.rectTransform, 0f, 0f, width, 14f);

            var value = TwinUIStyle.Mono(root, "Value", TwinUITextR3.Placeholder, TwinUIStyle.FontStat,
                TwinUIStyle.TextColor, TextAlignmentOptions.Left);
            value.enableWordWrapping = false;
            TwinUIStyle.Row(value.rectTransform, 0f, 15f, width, 26f);

            var unit = TwinUIStyle.Text(root, "Unit", string.Empty, TwinUIStyle.FontUnit, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(unit.rectTransform, 0f, 40f, width, 12f);

            return new Stat { Label = caption, Value = value, Unit = unit };
        }

        public static Kv Row(Transform parent, float y, string label, float width)
        {
            var root = TwinUIStyle.Frame(parent, "Kv");
            TwinUIStyle.Row(root, 0f, y, width, RowHeight);

            var caption = TwinUIStyle.Text(root, "Label", label, TwinUIStyle.FontBodySm, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(caption.rectTransform, 0f, 0f, width * 0.45f, RowHeight);

            var value = TwinUIStyle.Mono(root, "Value", TwinUITextR3.Placeholder, TwinUIStyle.FontBodySm,
                TwinUIStyle.TextColor, TextAlignmentOptions.Right);
            TwinUIStyle.Row(value.rectTransform, width * 0.45f, 0f, width * 0.55f, RowHeight);

            return new Kv { Label = caption, Value = value };
        }

        public static Badge BadgeRow(Transform parent, float x, float y, float width)
        {
            var root = TwinUIStyle.Frame(parent, "Badge");
            TwinUIStyle.Row(root, x, y, width, 16f);

            var dot = TwinUIStyle.StatusDot(root, "Dot", 6f, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(dot.rectTransform, 0f, 5f, 6f, 6f);

            var text = TwinUIStyle.Text(root, "Text", string.Empty, TwinUIStyle.FontUnit, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(text.rectTransform, 10f, 0f, Mathf.Max(40f, width - 10f), 16f);

            return new Badge { Dot = dot, Text = text };
        }

        public static Bar Progress(Transform parent, float y, float width, float height = 4f)
        {
            var root = TwinUIStyle.Frame(parent, "Progress");
            TwinUIStyle.Row(root, 0f, y, width, height);

            var track = TwinUIStyle.Panel(root, "Track", TwinUIStyle.Layer2Color);
            track.raycastTarget = false;
            TwinUIStyle.Row(track.rectTransform, 0f, 0f, width, height);

            var fill = TwinUIStyle.Panel(root, "Fill", TwinUIStyle.OkColor);
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0.0001f, 1f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            return new Bar { Track = track, Fill = fill };
        }

        public static TMP_Text Caption(Transform parent, string text, float x, float y, float width, float height = 16f)
        {
            var t = TwinUIStyle.Text(parent, "Caption", text, TwinUIStyle.FontBodySm, TwinUIStyle.MutedColor);
            t.enableWordWrapping = true;
            TwinUIStyle.Row(t.rectTransform, x, y, width, height);
            return t;
        }

        public static TMP_Text Section(Transform parent, string text, float y, float width)
        {
            var t = TwinUIStyle.Text(parent, "Section", text, TwinUIStyle.FontUnit, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(t.rectTransform, 0f, y, width, 14f);
            return t;
        }

        public static void Divider(Transform parent, float y, float width)
        {
            var line = TwinUIStyle.Panel(parent, "Divider", TwinUIStyle.Alpha(TwinUIStyle.BorderColor, 0.7f));
            line.raycastTarget = false;
            TwinUIStyle.Row(line.rectTransform, 0f, y, width, 1f);
        }

        /// <summary>空态/未接入：统一文案与颜色，禁止用假数据填充。</summary>
        public static TMP_Text Empty(Transform parent, string text)
        {
            var t = TwinUIStyle.Text(parent, "EmptyState",
                string.IsNullOrEmpty(text) ? TwinUITextR3.NoSource : text,
                TwinUIStyle.FontBodySm, TwinUIStyle.QuietColor);
            t.enableWordWrapping = true;
            TwinUIStyle.Row(t.rectTransform, 0f, 0f, 200f, 40f);
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(200f, 40f);
            return t;
        }
    }
}