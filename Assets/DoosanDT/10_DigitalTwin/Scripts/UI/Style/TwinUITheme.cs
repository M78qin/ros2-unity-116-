// 三期（工业级数字孪生）主题层：深色为默认，浅色保留为可切换项。
//
// 设计约束：
// 1) 所有颜色仍由 TwinUIStyle 暴露，页面/按钮/视图只读令牌，不写死 HEX。
// 2) 主题切换必须能作用到“已经建好的界面”，因此 TwinUIStyle 在创建图元时给 Graphic 打
//    TwinUIToneTag（角色标签），TwinUIThemeApplier 在切换时按角色重刷颜色，重建层级不是必需的。
// 3) 发光（Glow）只允许用于状态点/选中态，正文不使用发光，避免“到处霓虹”的假工业感。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DigitalTwin.UI
{
    public enum TwinUIThemeMode
    {
        Dark = 0,
        Light = 1,

        // P2.5：V3.1 原型默认主题（深海科技蓝）。枚举值追加在末尾，避免破坏已序列化的数值。
        Ocean = 2
    }

    public enum TwinUIDensity
    {
        Comfortable = 0,
        Compact = 1
    }

    /// <summary>语义色温：正常 / 预警 / 超限 / 未知（未知表示“未订阅或无数据”，绝不等于 0）。</summary>
    public enum TwinUITone
    {
        Unknown = 0,
        Ok = 1,
        Warn = 2,
        Crit = 3
    }

    /// <summary>图元角色：主题重刷时按角色取色。</summary>
    public enum TwinUIToneRole
    {
        None = 0,
        Bg,
        Panel,
        Card,
        CardAlt,
        Border,
        Text,
        Muted,
        Accent,
        Accent2,
        Ok,
        Warn,
        Crit,
        CritText,
        Info,
        Disabled,
        PlotBg,
        Grid,
        RowAlt,
        Glow,
        Series0,
        Series1,
        Series2,
        Series3,
        Series4,
        Series5,

        // P2.5 追加（放在末尾，保持既有 Role 数值不变）：窗口外壳/深海层级专用角色。
        Layer,
        Layer2,
        LineStrong,
        Strong,
        Shadow
    }

    public struct TwinUIPalette
    {
        public TwinUIThemeMode Mode;
        public Color Bg;
        public Color Panel;
        public Color Card;
        public Color CardAlt;
        public Color Border;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color Accent2;
        public Color Ok;
        public Color Warn;
        public Color Crit;
        public Color CritText;
        public Color Info;
        public Color Disabled;
        public Color Glow;
        public Color PlotBg;
        public Color Grid;
        public Color RowAlt;

        // P2.5 追加：V3.1 原型 --ui-layer / --ui-layer2 / --ui-lineStrong / --ui-strong / --ui-shadow
        // 与 --ui-scale。Deep/Light 填等价值，保证旧主题行为不变。
        public Color Layer;
        public Color Layer2;
        public Color LineStrong;
        public Color Strong;
        public Color Shadow;
        public float UiScale;

        // 按钮五态（Normal/Hover/Pressed/Selected/Disabled）
        public Color BtnFill;
        public Color BtnFillHover;
        public Color BtnFillPressed;
        public Color BtnEdge;
        public Color BtnEdgeHover;
        public Color BtnText;
        public Color BtnPrimaryFill;
        public Color BtnPrimaryFillHover;
        public Color BtnPrimaryFillPressed;
        public Color BtnPrimaryText;
        public Color BtnDangerFill;
        public Color BtnDangerFillHover;
        public Color BtnDangerFillPressed;
        public Color BtnDangerText;
        public Color BtnSelectedFill;
        public Color BtnSelectedEdge;
        public Color BtnSelectedText;
        public Color BtnArmedFill;
        public Color BtnDisabledFill;
        public Color BtnDisabledText;

        public Color[] Series;

        public static TwinUIPalette Dark()
        {
            var p = new TwinUIPalette
            {
                Mode = TwinUIThemeMode.Dark,
                Bg = TwinUITheme.Hex("#0A1018"),
                Panel = TwinUITheme.Hex("#111A26"),
                Card = TwinUITheme.Hex("#16222F"),
                CardAlt = TwinUITheme.Hex("#1B2A3A"),
                Border = TwinUITheme.Hex("#22303F"),
                Text = TwinUITheme.Hex("#E6EDF5"),
                Muted = TwinUITheme.Hex("#9BAEC2"),
                Accent = TwinUITheme.Hex("#22D3EE"),
                Accent2 = TwinUITheme.Hex("#3B82F6"),
                Ok = TwinUITheme.Hex("#22C55E"),
                Warn = TwinUITheme.Hex("#F5A524"),
                Crit = TwinUITheme.Hex("#EF4444"),
                CritText = TwinUITheme.Hex("#FF8080"),
                Info = TwinUITheme.Hex("#3B82F6"),
                Disabled = TwinUITheme.Hex("#5A6B7D"),
                Glow = new Color(0.133f, 0.827f, 0.933f, 0.28f),
                PlotBg = TwinUITheme.Hex("#0D1620"),
                Grid = TwinUITheme.Hex("#22303F"),
                RowAlt = TwinUITheme.Hex("#1B2A3A"),
                // P2.5 新字段：旧主题的等价值（Layer=Panel、Layer2=CardAlt、LineStrong=BtnEdgeHover、Strong=Accent）
                Layer = TwinUITheme.Hex("#111A26"),
                Layer2 = TwinUITheme.Hex("#1B2A3A"),
                LineStrong = TwinUITheme.Hex("#3E5A73"),
                Strong = TwinUITheme.Hex("#22D3EE"),
                Shadow = new Color(0f, 0f, 0f, 0.62f),
                UiScale = 1f,
                BtnFill = TwinUITheme.Hex("#1B2A3A"),
                BtnFillHover = TwinUITheme.Hex("#24384C"),
                BtnFillPressed = TwinUITheme.Hex("#2D465E"),
                BtnEdge = TwinUITheme.Hex("#2B3C4E"),
                BtnEdgeHover = TwinUITheme.Hex("#3E5A73"),
                BtnText = TwinUITheme.Hex("#DCE7F2"),
                BtnPrimaryFill = TwinUITheme.Hex("#0E7490"),
                BtnPrimaryFillHover = TwinUITheme.Hex("#0891B2"),
                BtnPrimaryFillPressed = TwinUITheme.Hex("#22D3EE"),
                BtnPrimaryText = TwinUITheme.Hex("#EAFBFF"),
                BtnDangerFill = TwinUITheme.Hex("#7F1D1D"),
                BtnDangerFillHover = TwinUITheme.Hex("#991B1B"),
                BtnDangerFillPressed = TwinUITheme.Hex("#B91C1C"),
                BtnDangerText = TwinUITheme.Hex("#FFE4E4"),
                BtnSelectedFill = TwinUITheme.Hex("#0E3B4A"),
                BtnSelectedEdge = TwinUITheme.Hex("#22D3EE"),
                BtnSelectedText = TwinUITheme.Hex("#EAFBFF"),
                BtnArmedFill = TwinUITheme.Hex("#3A1D1D"),
                BtnDisabledFill = TwinUITheme.Hex("#151F2A"),
                BtnDisabledText = TwinUITheme.Hex("#5A6B7D"),
                Series = new[]
                {
                    TwinUITheme.Hex("#22D3EE"), TwinUITheme.Hex("#3B82F6"), TwinUITheme.Hex("#F5A524"),
                    TwinUITheme.Hex("#EF4444"), TwinUITheme.Hex("#22C55E"), TwinUITheme.Hex("#A78BFA")
                }
            };
            return p;
        }

        public static TwinUIPalette Light()
        {
            var p = new TwinUIPalette
            {
                Mode = TwinUIThemeMode.Light,
                Bg = TwinUITheme.Hex("#F4F6FB"),
                Panel = TwinUITheme.Hex("#FFFFFF"),
                Card = TwinUITheme.Hex("#FFFFFF"),
                CardAlt = TwinUITheme.Hex("#F2F5FA"),
                Border = TwinUITheme.Hex("#DFE5EF"),
                Text = TwinUITheme.Hex("#1F2328"),
                Muted = TwinUITheme.Hex("#5A6779"),
                Accent = TwinUITheme.Hex("#3F5AF0"),
                Accent2 = TwinUITheme.Hex("#2563EB"),
                Ok = TwinUITheme.Hex("#15803D"),
                Warn = TwinUITheme.Hex("#B45309"),
                Crit = TwinUITheme.Hex("#DC2626"),
                CritText = TwinUITheme.Hex("#B91C1C"),
                Info = TwinUITheme.Hex("#2563EB"),
                Disabled = TwinUITheme.Hex("#8B95A5"),
                Glow = new Color(0.247f, 0.353f, 0.941f, 0.14f),
                PlotBg = TwinUITheme.Hex("#FFFFFF"),
                Grid = TwinUITheme.Hex("#D9E1EE"),
                RowAlt = TwinUITheme.Hex("#F2F5FA"),
                Layer = TwinUITheme.Hex("#FFFFFF"),
                Layer2 = TwinUITheme.Hex("#F2F5FA"),
                LineStrong = TwinUITheme.Hex("#C9D3E3"),
                Strong = TwinUITheme.Hex("#3F5AF0"),
                Shadow = new Color(0f, 0f, 0.02f, 0.18f),
                UiScale = 1f,
                BtnFill = TwinUITheme.Hex("#FFFFFF"),
                BtnFillHover = TwinUITheme.Hex("#F2F5FA"),
                BtnFillPressed = TwinUITheme.Hex("#E8EDFF"),
                BtnEdge = TwinUITheme.Hex("#DFE5EF"),
                BtnEdgeHover = TwinUITheme.Hex("#C9D3E3"),
                BtnText = TwinUITheme.Hex("#1F2328"),
                BtnPrimaryFill = TwinUITheme.Hex("#3F5AF0"),
                BtnPrimaryFillHover = TwinUITheme.Hex("#4B63F2"),
                BtnPrimaryFillPressed = TwinUITheme.Hex("#3349D8"),
                BtnPrimaryText = TwinUITheme.Hex("#FFFFFF"),
                BtnDangerFill = TwinUITheme.Hex("#DC2626"),
                BtnDangerFillHover = TwinUITheme.Hex("#C21F1F"),
                BtnDangerFillPressed = TwinUITheme.Hex("#A81A1A"),
                BtnDangerText = TwinUITheme.Hex("#FFFFFF"),
                BtnSelectedFill = TwinUITheme.Hex("#E8EDFF"),
                BtnSelectedEdge = TwinUITheme.Hex("#3F5AF0"),
                BtnSelectedText = TwinUITheme.Hex("#1B2A8A"),
                BtnArmedFill = TwinUITheme.Hex("#FDECEC"),
                BtnDisabledFill = TwinUITheme.Hex("#F0F2F5"),
                BtnDisabledText = TwinUITheme.Hex("#8B95A5"),
                Series = new[]
                {
                    TwinUITheme.Hex("#2563EB"), TwinUITheme.Hex("#15803D"), TwinUITheme.Hex("#B45309"),
                    TwinUITheme.Hex("#DC2626"), TwinUITheme.Hex("#7C3AED"), TwinUITheme.Hex("#0E7490")
                }
            };
            return p;
        }

        /// <summary>
        /// V3.1 原型默认主题 Ocean（深海科技蓝）。逐字段映射自
        /// `agent_runs/dsh_2026-11-05_v3_spec/excerpts/tokens.json` 的 `:root` 块（L48）：
        ///   --ui-bg #071827 / --ui-shell #102637 / --ui-layer #0e2535 / --ui-layer2 #173345
        ///   --ui-card #132e40 / --ui-line #2b5268 / --ui-lineStrong #42738d / --ui-text #e7f5ff
        ///   --ui-soft #99bbca / --ui-accent #39c5e5 / --ui-accent2 #2d7cbb / --ui-strong #53d4e6
        ///   --ui-chart #47dce7 / --ui-shadow 0 20px 64px #000a / --ui-good #40d0a0
        ///   --ui-warn #f0bf63 / --ui-bad #fa717a
        /// 按钮五态/图表 Series 原型没有直接变量：由 Layer/Layer2/Line/LineStrong/Accent/Strong
        /// 组合推导（不照抄原型里硬编码的曲线色 #5bdfec/#f1bd7b/#8fa8f9）。
        /// </summary>
        public static TwinUIPalette Ocean()
        {
            var p = new TwinUIPalette
            {
                Mode = TwinUIThemeMode.Ocean,
                Bg = TwinUITheme.Hex("#071827"),
                Panel = TwinUITheme.Hex("#102637"),
                Layer = TwinUITheme.Hex("#0E2535"),
                Layer2 = TwinUITheme.Hex("#173345"),
                Card = TwinUITheme.Hex("#132E40"),
                CardAlt = TwinUITheme.Hex("#173345"),
                Border = TwinUITheme.Hex("#2B5268"),
                LineStrong = TwinUITheme.Hex("#42738D"),
                Text = TwinUITheme.Hex("#E7F5FF"),
                Muted = TwinUITheme.Hex("#99BBCA"),
                Accent = TwinUITheme.Hex("#39C5E5"),
                Accent2 = TwinUITheme.Hex("#2D7CBB"),
                Strong = TwinUITheme.Hex("#53D4E6"),
                Ok = TwinUITheme.Hex("#40D0A0"),
                Warn = TwinUITheme.Hex("#F0BF63"),
                Crit = TwinUITheme.Hex("#FA717A"),
                CritText = TwinUITheme.Hex("#FFB3B8"),
                Info = TwinUITheme.Hex("#2D7CBB"),
                Disabled = TwinUITheme.Hex("#5E7C8C"),
                Glow = new Color(0.224f, 0.831f, 0.902f, 0.28f),
                Shadow = new Color(0f, 0f, 0f, 0.64f),
                UiScale = 1f,
                PlotBg = TwinUITheme.Hex("#0A1E2D"),
                Grid = TwinUITheme.Hex("#23455A"),
                RowAlt = TwinUITheme.Hex("#132E40"),
                BtnFill = TwinUITheme.Hex("#173345"),
                BtnFillHover = TwinUITheme.Hex("#1E4257"),
                BtnFillPressed = TwinUITheme.Hex("#26506A"),
                BtnEdge = TwinUITheme.Hex("#2B5268"),
                BtnEdgeHover = TwinUITheme.Hex("#42738D"),
                BtnText = TwinUITheme.Hex("#DCEFF7"),
                BtnPrimaryFill = TwinUITheme.Hex("#2D7CBB"),
                BtnPrimaryFillHover = TwinUITheme.Hex("#39A9CE"),
                BtnPrimaryFillPressed = TwinUITheme.Hex("#53D4E6"),
                BtnPrimaryText = TwinUITheme.Hex("#EAF9FF"),
                BtnDangerFill = TwinUITheme.Hex("#8F2F38"),
                BtnDangerFillHover = TwinUITheme.Hex("#B03A45"),
                BtnDangerFillPressed = TwinUITheme.Hex("#D14A56"),
                BtnDangerText = TwinUITheme.Hex("#FFE4E4"),
                BtnSelectedFill = TwinUITheme.Hex("#14465C"),
                BtnSelectedEdge = TwinUITheme.Hex("#39C5E5"),
                BtnSelectedText = TwinUITheme.Hex("#EAF9FF"),
                BtnArmedFill = TwinUITheme.Hex("#3A2028"),
                BtnDisabledFill = TwinUITheme.Hex("#0F2433"),
                BtnDisabledText = TwinUITheme.Hex("#5E7C8C"),
                Series = new[]
                {
                    TwinUITheme.Hex("#47DCE7"), TwinUITheme.Hex("#2D7CBB"), TwinUITheme.Hex("#F0BF63"),
                    TwinUITheme.Hex("#FA717A"), TwinUITheme.Hex("#40D0A0"), TwinUITheme.Hex("#8FA8F9")
                }
            };
            return p;
        }

        public static TwinUIPalette ForMode(TwinUIThemeMode mode)
        {
            switch (mode)
            {
                case TwinUIThemeMode.Light: return Light();
                case TwinUIThemeMode.Ocean: return Ocean();
                default: return Dark();
            }
        }
    }

    /// <summary>主题单例：默认深色；浅色为可选切换项。切换后由 TwinUIThemeApplier 重刷已建图元。</summary>
    public static class TwinUITheme
    {
        private static TwinUIThemeMode _mode = TwinUIThemeMode.Dark;
        private static TwinUIPalette _palette = TwinUIPalette.Dark();
        private static readonly List<TwinUIThemeApplier> Appliers = new List<TwinUIThemeApplier>(4);

        public static event System.Action<TwinUIThemeMode> Changed;

        public static TwinUIThemeMode Mode => _mode;
        public static TwinUIPalette Current => _palette;
        public static bool IsDark => _mode == TwinUIThemeMode.Dark;
        public static bool IsOcean => _mode == TwinUIThemeMode.Ocean;
        public static string ModeLabel => _mode == TwinUIThemeMode.Dark ? "深色"
            : _mode == TwinUIThemeMode.Light ? "浅色" : "深海";

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static void SetMode(TwinUIThemeMode mode)
        {
            _mode = mode;
            _palette = TwinUIPalette.ForMode(mode);
            var handler = Changed;
            if (handler != null)
            {
                handler(mode);
            }
        }

        public static TwinUIThemeMode Toggle()
        {
            SetMode(_mode == TwinUIThemeMode.Dark ? TwinUIThemeMode.Light : TwinUIThemeMode.Dark);
            return _mode;
        }

        public static void Register(TwinUIThemeApplier applier)
        {
            if (applier != null && !Appliers.Contains(applier))
            {
                Appliers.Add(applier);
            }
        }

        public static void Unregister(TwinUIThemeApplier applier)
        {
            Appliers.Remove(applier);
        }

        /// <summary>立即重刷所有已注册的 applier，返回被重刷的图元数。</summary>
        public static int Reapply()
        {
            int total = 0;
            for (int i = Appliers.Count - 1; i >= 0; i--)
            {
                var a = Appliers[i];
                if (a == null)
                {
                    Appliers.RemoveAt(i);
                    continue;
                }

                total += a.Apply();
            }

            return total;
        }

        public static Color ToneColor(TwinUITone tone)
        {
            switch (tone)
            {
                case TwinUITone.Ok: return _palette.Ok;
                case TwinUITone.Warn: return _palette.Warn;
                case TwinUITone.Crit: return _palette.Crit;
                default: return _palette.Muted;
            }
        }

        public static Color ToneTextColor(TwinUITone tone)
        {
            switch (tone)
            {
                case TwinUITone.Ok: return _palette.Ok;
                case TwinUITone.Warn: return _palette.Warn;
                case TwinUITone.Crit: return _palette.CritText;
                default: return _palette.Muted;
            }
        }

        /// <summary>非颜色冗余：超限不只靠颜色，同时给字符标记与文字。
        /// 只用 ASCII 标记，避免静态字形集缺字。</summary>
        public static string ToneMark(TwinUITone tone)
        {
            switch (tone)
            {
                case TwinUITone.Ok: return "OK";
                case TwinUITone.Warn: return "!";
                case TwinUITone.Crit: return "X";
                default: return "-";
            }
        }

        public static string ToneLabel(TwinUITone tone)
        {
            switch (tone)
            {
                case TwinUITone.Ok: return "正常";
                case TwinUITone.Warn: return "预警";
                case TwinUITone.Crit: return "超限";
                default: return "未订阅";
            }
        }

        public static TwinUIToneRole RoleOfColor(Color color)
        {
            if (color.a <= 0.001f)
            {
                return TwinUIToneRole.None;
            }

            TwinUIThemeMode saved = _mode;
            TwinUIPalette[] palettes =
            {
                TwinUIPalette.Dark(), TwinUIPalette.Light(), TwinUIPalette.Ocean()
            };
            TwinUIToneRole role = TwinUIToneRole.None;
            for (int i = 0; i < palettes.Length && role == TwinUIToneRole.None; i++)
            {
                role = Match(palettes[i], color);
            }

            _mode = saved;
            return role;
        }

        private static TwinUIToneRole Match(TwinUIPalette p, Color c)
        {
            if (Near(c, p.Layer)) return TwinUIToneRole.Layer;
            if (Near(c, p.Layer2)) return TwinUIToneRole.Layer2;
            if (Near(c, p.LineStrong)) return TwinUIToneRole.LineStrong;
            if (Near(c, p.Strong)) return TwinUIToneRole.Strong;
            if (Near(c, p.Shadow)) return TwinUIToneRole.Shadow;
            if (Near(c, p.Bg)) return TwinUIToneRole.Bg;
            if (Near(c, p.Panel)) return TwinUIToneRole.Panel;
            if (Near(c, p.Card)) return TwinUIToneRole.Card;
            if (Near(c, p.CardAlt)) return TwinUIToneRole.CardAlt;
            if (Near(c, p.Border)) return TwinUIToneRole.Border;
            if (Near(c, p.Text)) return TwinUIToneRole.Text;
            if (Near(c, p.Muted)) return TwinUIToneRole.Muted;
            if (Near(c, p.Accent)) return TwinUIToneRole.Accent;
            if (Near(c, p.Accent2)) return TwinUIToneRole.Accent2;
            if (Near(c, p.Ok)) return TwinUIToneRole.Ok;
            if (Near(c, p.Warn)) return TwinUIToneRole.Warn;
            if (Near(c, p.Crit)) return TwinUIToneRole.Crit;
            if (Near(c, p.CritText)) return TwinUIToneRole.CritText;
            if (Near(c, p.Info)) return TwinUIToneRole.Info;
            if (Near(c, p.Disabled)) return TwinUIToneRole.Disabled;
            if (Near(c, p.PlotBg)) return TwinUIToneRole.PlotBg;
            if (Near(c, p.Grid)) return TwinUIToneRole.Grid;
            if (Near(c, p.RowAlt)) return TwinUIToneRole.RowAlt;
            if (p.Series != null)
            {
                for (int i = 0; i < p.Series.Length && i < 6; i++)
                {
                    if (Near(c, p.Series[i]))
                    {
                        return (TwinUIToneRole)((int)TwinUIToneRole.Series0 + i);
                    }
                }
            }

            return TwinUIToneRole.None;
        }

        private static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 0.005f && Mathf.Abs(a.g - b.g) <= 0.005f
                && Mathf.Abs(a.b - b.b) <= 0.005f && Mathf.Abs(a.a - b.a) <= 0.005f;
        }

        public static Color ColorForRole(TwinUIToneRole role)
        {
            TwinUIPalette p = _palette;
            switch (role)
            {
                case TwinUIToneRole.Bg: return p.Bg;
                case TwinUIToneRole.Panel: return p.Panel;
                case TwinUIToneRole.Card: return p.Card;
                case TwinUIToneRole.CardAlt: return p.CardAlt;
                case TwinUIToneRole.Border: return p.Border;
                case TwinUIToneRole.Text: return p.Text;
                case TwinUIToneRole.Muted: return p.Muted;
                case TwinUIToneRole.Accent: return p.Accent;
                case TwinUIToneRole.Accent2: return p.Accent2;
                case TwinUIToneRole.Ok: return p.Ok;
                case TwinUIToneRole.Warn: return p.Warn;
                case TwinUIToneRole.Crit: return p.Crit;
                case TwinUIToneRole.CritText: return p.CritText;
                case TwinUIToneRole.Info: return p.Info;
                case TwinUIToneRole.Disabled: return p.Disabled;
                case TwinUIToneRole.PlotBg: return p.PlotBg;
                case TwinUIToneRole.Grid: return p.Grid;
                case TwinUIToneRole.RowAlt: return p.RowAlt;
                case TwinUIToneRole.Layer: return p.Layer;
                case TwinUIToneRole.Layer2: return p.Layer2;
                case TwinUIToneRole.LineStrong: return p.LineStrong;
                case TwinUIToneRole.Strong: return p.Strong;
                case TwinUIToneRole.Shadow: return p.Shadow;
                case TwinUIToneRole.Glow: return p.Glow;
                case TwinUIToneRole.Series0: return SeriesAt(0);
                case TwinUIToneRole.Series1: return SeriesAt(1);
                case TwinUIToneRole.Series2: return SeriesAt(2);
                case TwinUIToneRole.Series3: return SeriesAt(3);
                case TwinUIToneRole.Series4: return SeriesAt(4);
                case TwinUIToneRole.Series5: return SeriesAt(5);
                default: return Color.clear;
            }
        }

        private static Color SeriesAt(int index)
        {
            if (_palette.Series == null || index < 0 || index >= _palette.Series.Length)
            {
                return Color.clear;
            }

            return _palette.Series[index];
        }

        /// <summary>给图元打角色标签（幂等）。颜色无法归属令牌时不打标签。</summary>
        public static TwinUIToneTag Tag(GameObject go, Color color)
        {
            if (go == null)
            {
                return null;
            }

            TwinUIToneRole role = RoleOfColor(color);
            if (role == TwinUIToneRole.None)
            {
                return null;
            }

            var tag = go.GetComponent<TwinUIToneTag>();
            if (tag == null)
            {
                tag = go.AddComponent<TwinUIToneTag>();
            }

            tag.Role = role;
            return tag;
        }
    }
}
