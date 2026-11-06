// 数值/状态格式化：所有"未订阅 / 无数据 / 无连接"的判定集中在这里，
// 避免视图各自把缺失数据写成 0（这正是论文级数据采集最忌讳的假数据）。
using UnityEngine;

namespace DigitalTwin.UI
{
    public static class TwinUiFormat
    {
        public static string Dash = TwinUITextR3.EmDash;

        /// <summary>能力缺失 → 未订阅；能力存在但无帧 → 无数据。</summary>
        public static string Missing(bool capability, bool hasFrame)
        {
            if (!capability)
            {
                return TwinUITextR3.NotSubscribed;
            }

            return hasFrame ? TwinUITextR3.NoData : TwinUITextR3.NoData;
        }

        public static string Hz(double value, bool valid)
        {
            return valid && value > 0.0001d ? value.ToString("F1") : Dash;
        }

        public static string Ms(double value, int digits, bool valid)
        {
            if (!valid || value < 0d)
            {
                return Dash;
            }

            return value.ToString(digits == 0 ? "F0" : digits == 1 ? "F1" : "F2");
        }

        public static string Deg(float value, int digits)
        {
            return value.ToString(digits == 1 ? "F1" : digits == 2 ? "F2" : "F3");
        }

        public static string Signed(double value, string unit, int digits)
        {
            string text = value.ToString(digits == 1 ? "F1" : digits == 2 ? "F2" : "F3");
            if (value > 0d)
            {
                text = "+" + text;
            }

            return unit == null ? text : text + unit;
        }

        public static string Count(long value)
        {
            return value <= 0 ? "0" : value.ToString();
        }

        public static string Ratio(double value)
        {
            return (value * 100d).ToString("F0") + TwinUITextR3.UnitPercent;
        }

        public static Color ToneColor(TwinUITone tone)
        {
            return TwinUITheme.ToneColor(tone);
        }

        public static Color ToneTextColor(TwinUITone tone)
        {
            return TwinUITheme.ToneTextColor(tone);
        }

        public static string StateText(TwinUITone tone)
        {
            return TwinUITheme.ToneMark(tone) + " " + TwinUITheme.ToneLabel(tone);
        }

        /// <summary>带非颜色冗余的状态串：如 "! 预警"、"X 超限"、"- 未订阅"。</summary>
        public static string StateFor(TwinUITone tone, bool capability, bool hasFrame)
        {
            if (!capability)
            {
                return "? " + TwinUITextR3.NotSubscribed;
            }

            if (!hasFrame)
            {
                return "- " + TwinUITextR3.NoData;
            }

            return StateText(tone);
        }
    }
}