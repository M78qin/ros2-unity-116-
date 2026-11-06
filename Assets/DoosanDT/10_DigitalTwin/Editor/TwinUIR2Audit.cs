// 只读审计：层级 / 页签 / 调色板 / 文本字号 / 按钮最小高度 / 对比度 / 颜色令牌 / 阈值标签 / 数字字形等宽。
// 菜单：DigitalTwin/UI/12 Audit UI Layout & Tokens (read-only)
// 输出：Logs/ui_r2_audit.txt（兼容旧路径）与 Logs/ui_r3_audit.txt（三期）
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIR2Audit
    {
        private const string RootPath = "30_界面与交互/UI_二期面板";
        private const float MinButtonHeight = 40f;
        private const float MinContrast = 4.5f;

        [MenuItem("DigitalTwin/UI/12 Audit UI Layout & Tokens (read-only)", false, 12)]
        public static void Audit()
        {
            var sb = new StringBuilder();
            var sceneAtStart = SceneManager.GetActiveScene();
            bool dirtyBefore = sceneAtStart.IsValid() && sceneAtStart.isDirty;
            TwinUIThemeMode originalTheme = TwinUITheme.Mode;
            sb.AppendLine("Twin UI audit (R2 + R3 industrial)  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("theme=" + originalTheme + " density=default");
            sb.AppendLine("auditMode=read-only (does not switch theme; contrast computed from palettes)");
            sb.AppendLine("sceneDirtyAtStart=" + dirtyBefore);
            var root = GameObject.Find(RootPath);
            if (root == null)
            {
                sb.AppendLine("RESULT: FAIL - root missing (run UI/1)");
                Write(sb);
                return;
            }

            int failures = 0;
            var settings = TwinUIR3Builder.LoadSettings(false);

            // 渲染一遍工业数据台（回放源，只读 csv），让"未订阅/状态词/阈值标签"等文案真正生成，
            // 再统计文本。这一步只走既有 pipeline/view 代码，不进入 Play，也不保存场景。
            var industrialRoot = root.transform.Find(TwinUIR3Builder.IndustrialName);
            var host = industrialRoot == null ? null : industrialRoot.GetComponent<TwinUiIndustrialHost>();
            var pageHost = root.GetComponent<TwinUIPageHost>();
            bool renderPass = false;
            if (host != null && settings != null)
            {
                try
                {
                    host.ConfigureForEditor(settings, TwinUiSourceKind.Replay, settings.replayCsvPath);
                    for (int i = 0; i < 10; i++)
                    {
                        host.StepReplayAndRender(0.2);
                    }

                    for (int sub = 0; sub < 3; sub++)
                    {
                        host.SetSubPage(sub);
                        host.RefreshNow();
                    }

                    host.SetSubPage(0);
                    host.RefreshNow();
                    if (pageHost != null)
                    {
                        pageHost.Show(2);
                    }

                    renderPass = true;
                }
                catch (System.Exception e)
                {
                    sb.AppendLine("renderPass ERROR: " + e.GetType().Name + ": " + e.Message);
                    failures++;
                }
            }

            sb.AppendLine("renderPass=" + (renderPass ? "Replay(industrial)" : "skipped"));
            var transforms = root.GetComponentsInChildren<Transform>(true);
            sb.AppendLine("rootTransforms=" + transforms.Length + " activeSelf=" + root.activeSelf);

            foreach (Transform child in root.transform)
            {
                sb.AppendLine("TOP " + child.name + " active=" + child.gameObject.activeSelf
                    + " size=" + Size(child as RectTransform) + " children=" + child.childCount);
            }

            var ops = root.transform.Find("TwinOpsRoot");
            if (ops != null)
            {
                sb.AppendLine("-- TwinOps pages --");
                foreach (string name in new[] { "PageOverview", "PageTrend", "PageControl", "PageRecord" })
                {
                    var p = ops.Find(name);
                    sb.AppendLine("PAGE " + name + " exists=" + (p != null) + " size=" + Size(p as RectTransform));
                }
            }

            var industrial = root.transform.Find("IndustrialRoot");
            sb.AppendLine("-- Industrial (R3) --");
            if (industrial == null)
            {
                sb.AppendLine("MISSING IndustrialRoot (run UI/2)");
                failures++;
            }
            else
            {
                string[] pages = { "PageData", "PageEvents", "PageSettings" };
                foreach (string name in pages)
                {
                    var p = industrial.Find(name);
                    sb.AppendLine("R3PAGE " + name + " exists=" + (p != null) + " size=" + Size(p as RectTransform));
                    if (p == null)
                    {
                        failures++;
                    }
                }

                var nav = industrial.Find("SubNav");
                sb.AppendLine("R3 SubNav children=" + (nav == null ? 0 : nav.childCount));
                var chip = industrial.Find("StatusChip");
                if (chip == null)
                {
                    sb.AppendLine("R3 StatusChip=MISSING");
                    failures++;
                }
                else
                {
                    var crt = chip as RectTransform;
                    sb.AppendLine("R3 StatusChip active=" + chip.gameObject.activeSelf + " size=" + Size(crt)
                        + " anchored=" + (crt != null ? crt.anchoredPosition.ToString() : "n/a")
                        + " canvasRoot=" + Size(root.transform as RectTransform)
                        + " industrialRect=" + Size(industrial as RectTransform));
                    var chipText = chip.Find("Text");
                    var tmp = chipText == null ? null : chipText.GetComponent<TMP_Text>();
                    sb.AppendLine("R3 StatusChip text='" + (tmp == null ? "<missing>" : tmp.text) + "' color="
                        + (tmp == null ? "n/a" : (tmp.color.r.ToString("F2") + "," + tmp.color.g.ToString("F2") + ","
                            + tmp.color.b.ToString("F2"))));
                }
                var ctxBar = root.transform.Find("ContextBar");
                sb.AppendLine("R3 ContextBar=" + (ctxBar != null) + " size=" + Size(ctxBar as RectTransform));
                if (ctxBar == null)
                {
                    failures++;
                }
            }

            // ---- 按钮最小高度 ----
            var buttons = root.GetComponentsInChildren<TwinUIButton>(true);
            float minButton = float.MaxValue;
            int tooSmall = 0;
            foreach (var b in buttons)
            {
                var rt = b.transform as RectTransform;
                if (rt == null)
                {
                    continue;
                }

                float h = rt.rect.height;
                if (h > 0f && h < minButton)
                {
                    minButton = h;
                }

                if (h > 0f && h < MinButtonHeight)
                {
                    tooSmall++;
                    sb.AppendLine("SMALL_BUTTON " + PathOf(b.transform, root.transform) + " h=" + Mathf.RoundToInt(h));
                }
            }

            sb.AppendLine("buttons=" + buttons.Length + " minHeight=" + (minButton == float.MaxValue ? 0f : minButton)
                + " below40=" + tooSmall);
            if (buttons.Length == 0 || minButton < MinButtonHeight)
            {
                failures++;
            }

            // ---- 调色板 ----
            var images = root.GetComponentsInChildren<Image>(true);
            var counts = new Dictionary<string, int>();
            foreach (var img in images)
            {
                string key = Hex(img.color);
                counts[key] = counts.TryGetValue(key, out int v) ? v + 1 : 1;
            }

            sb.AppendLine("-- palette (Image.color) distinct=" + counts.Count + " --");
            foreach (var pair in counts)
            {
                sb.AppendLine("COLOR " + pair.Key + " x" + pair.Value);
            }

            // ---- 文本字号 ----
            var texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            var sizes = new SortedDictionary<float, int>();
            foreach (var t in texts)
            {
                if (t.fontSize < 1f)
                {
                    continue;
                }

                sizes[t.fontSize] = sizes.TryGetValue(t.fontSize, out int v) ? v + 1 : 1;
            }

            sb.AppendLine("texts=" + texts.Length + " fontSizes=" + sizes.Count);
            foreach (var pair in sizes)
            {
                sb.AppendLine("SIZE " + pair.Key + " x" + pair.Value);
            }

            // ---- 颜色令牌齐全性（深色 + 浅色 + P2.5 深海三套）----
            int tokens = 0;
            tokens += CheckToken(sb, "dark", TwinUIPalette.Dark());
            tokens += CheckToken(sb, "light", TwinUIPalette.Light());
            tokens += CheckToken(sb, "ocean", TwinUIPalette.Ocean());
            sb.AppendLine("colorTokens=" + tokens + " (3 palettes x 25 structural + 6 series; +5 P2.5 字段)");
            if (tokens < 93)
            {
                failures++;
            }

            // ---- 对比度（深色正文/状态字 vs 卡片底 + 页面底色）----
            // A49 修复：直接从调色板取色计算，不调用 TwinUITheme.SetMode（避免审计临时改色把场景弄脏）
            foreach (var theme in new[]
            {
                TwinUIThemeMode.Dark, TwinUIThemeMode.Light, TwinUIThemeMode.Ocean
            })
            {
                var p = theme == TwinUIThemeMode.Dark ? TwinUIPalette.Dark()
                    : theme == TwinUIThemeMode.Light ? TwinUIPalette.Light()
                    : TwinUIPalette.Ocean();
                var checks = new[]
                {
                    new KeyValuePair<string, Color>("Text/Card", p.Text),
                    new KeyValuePair<string, Color>("Muted/Card", p.Muted),
                    new KeyValuePair<string, Color>("Accent/Card", p.Accent),
                    new KeyValuePair<string, Color>("Ok/Card", p.Ok),
                    new KeyValuePair<string, Color>("Warn/Card", p.Warn),
                    new KeyValuePair<string, Color>("CritText/Card", p.CritText),
                    new KeyValuePair<string, Color>("Text/Bg", p.Text)
                };

                sb.AppendLine("-- contrast " + theme + " --");
                foreach (var pair in checks)
                {
                    Color background = pair.Key == "Text/Bg" ? p.Bg : p.Card;
                    float ratio = TwinUIStyle.Contrast(pair.Value, background);
                    bool ok = ratio >= MinContrast;
                    sb.AppendLine("CONTRAST " + theme + " " + pair.Key + "=" + ratio.ToString("F2") + (ok ? " ok" : " FAIL"));
                    if (!ok)
                    {
                        failures++;
                    }
                }
            }

            // ---- 数字字形等宽（等宽数字的实测证据）----
            float spread = MeasureDigitSpread();
            sb.AppendLine("digitAdvanceSpread=" + spread.ToString("F5") + " (0 means tabular/monospace digits)");
            if (spread > 0.01f)
            {
                failures++;
            }

            // ---- 阈值标签 / 非颜色冗余 / 曲线目标线与阈值带 ----
            int thresholdTexts = 0;
            int calibratedNotes = 0;
            int stateWords = 0;
            int notSubscribedTexts = 0;
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t.text))
                {
                    continue;
                }

                if (t.text.Contains("阈值") || t.text.Contains("额定"))
                {
                    thresholdTexts++;
                }

                if (t.text.Contains("待校准") || t.text.Contains("未标定") || t.text.Contains("未校准"))
                {
                    calibratedNotes++;
                }

                if (t.text.Contains("预警") || t.text.Contains("超限") || t.text.Contains("正常"))
                {
                    stateWords++;
                }

                if (t.text.Contains("未订阅"))
                {
                    notSubscribedTexts++;
                }
            }

            sb.AppendLine("thresholdTexts=" + thresholdTexts + " caliberationNotes=" + calibratedNotes
                + " stateWords=" + stateWords + " unsubscribedTexts=" + notSubscribedTexts);
            if (thresholdTexts < 5 || calibratedNotes < 2 || stateWords < 3)
            {
                failures++;
            }

            var charts = root.GetComponentsInChildren<TwinTelemetryChart>(true);
            int withTarget = 0;
            int withBand = 0;
            foreach (var c in charts)
            {
                if (c.ShowTargetLine)
                {
                    withTarget++;
                }

                if (c.ShowThresholdBand)
                {
                    withBand++;
                }
            }

            sb.AppendLine("charts=" + charts.Length + " withTargetLine=" + withTarget + " withThresholdBand=" + withBand);
            if (withTarget < 1 || withBand < 1)
            {
                failures++;
            }

            // ---- 阈值组 ----
            if (settings == null)
            {
                sb.AppendLine("settings=MISSING (run UI/3)");
                failures++;
            }
            else
            {
                sb.AppendLine("settings thresholds: " + settings.thresholds.Describe());
                sb.AppendLine("settings uiHz=" + settings.uiRefreshHz + " exportHz=" + settings.snapshotExportHz
                    + " theme=" + settings.themeMode + " source=" + settings.defaultSource);
                if (!settings.thresholds.calibrated)
                {
                    sb.AppendLine("thresholdStatus=待校准");
                }
            }

            if (host != null)
            {
                host.SetSource(TwinUiSourceKind.Runtime);
            }

            if (pageHost != null)
            {
                pageHost.Show(0);
            }

            // A49：审计结束主题必须回到进入时的主题，且不得让场景变脏。
            if (TwinUITheme.Mode != originalTheme)
            {
                TwinUITheme.SetMode(originalTheme);
            }

            if (settings != null && settings.themeMode != TwinUITheme.Mode)
            {
                sb.AppendLine("THEME_MISMATCH settings.themeMode=" + settings.themeMode
                    + " effective=" + TwinUITheme.Mode);
                failures++;
            }

            bool dirtyNow = sceneAtStart.IsValid() && sceneAtStart.isDirty;
            sb.AppendLine("restoredTheme=" + TwinUITheme.Mode + " sceneDirty=" + dirtyNow
                + (dirtyBefore ? " (dirtyAtStart=true)" : string.Empty));
            if (!dirtyBefore && dirtyNow)
            {
                sb.AppendLine("SIDE_EFFECT audit dirtied the scene (A49 regression)");
                failures++;
            }

            sb.AppendLine("failures=" + failures);

            sb.AppendLine(failures == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Write(sb);
        }

        private static int CheckToken(StringBuilder sb, string label, TwinUIPalette p)
        {
            var tokens = new List<KeyValuePair<string, Color>>
            {
                new KeyValuePair<string, Color>("Bg", p.Bg),
                new KeyValuePair<string, Color>("Panel", p.Panel),
                new KeyValuePair<string, Color>("Card", p.Card),
                new KeyValuePair<string, Color>("CardAlt", p.CardAlt),
                new KeyValuePair<string, Color>("Border", p.Border),
                new KeyValuePair<string, Color>("Text", p.Text),
                new KeyValuePair<string, Color>("Muted", p.Muted),
                new KeyValuePair<string, Color>("Accent", p.Accent),
                new KeyValuePair<string, Color>("Accent2", p.Accent2),
                new KeyValuePair<string, Color>("Ok", p.Ok),
                new KeyValuePair<string, Color>("Warn", p.Warn),
                new KeyValuePair<string, Color>("Crit", p.Crit),
                new KeyValuePair<string, Color>("CritText", p.CritText),
                new KeyValuePair<string, Color>("Info", p.Info),
                new KeyValuePair<string, Color>("Disabled", p.Disabled),
                new KeyValuePair<string, Color>("Glow", p.Glow),
                new KeyValuePair<string, Color>("PlotBg", p.PlotBg),
                new KeyValuePair<string, Color>("Grid", p.Grid),
                new KeyValuePair<string, Color>("RowAlt", p.RowAlt),
                new KeyValuePair<string, Color>("BtnSelectedEdge", p.BtnSelectedEdge),
                // P2.5 追加（V3 令牌）
                new KeyValuePair<string, Color>("Layer", p.Layer),
                new KeyValuePair<string, Color>("Layer2", p.Layer2),
                new KeyValuePair<string, Color>("LineStrong", p.LineStrong),
                new KeyValuePair<string, Color>("Strong", p.Strong),
                new KeyValuePair<string, Color>("Shadow", p.Shadow)
            };
            if (p.UiScale <= 0f)
            {
                sb.AppendLine("TOKENS " + label + " uiScale=INVALID");
            }

            var sb2 = new StringBuilder();
            int valid = 0;
            foreach (var t in tokens)
            {
                bool ok = t.Value != Color.magenta && t.Value != Color.clear;
                if (ok)
                {
                    valid++;
                }
                else
                {
                    sb2.Append(t.Key).Append(' ');
                }
            }

            int series = 0;
            if (p.Series != null)
            {
                for (int i = 0; i < p.Series.Length; i++)
                {
                    if (p.Series[i] != Color.magenta)
                    {
                        series++;
                    }
                }
            }

            sb.AppendLine("TOKENS " + label + " structural=" + valid + "/" + tokens.Count + " series=" + series
                + (sb2.Length > 0 ? " missing=" + sb2 : string.Empty));
            return valid + series;
        }

        /// <summary>实测 0-9 十个数字字形的 advance 差异：spread=0 说明数字等宽（可做表格对齐）。</summary>
        private static float MeasureDigitSpread()
        {
            var font = TwinUIStyle.GetFont();
            if (font == null)
            {
                return -1f;
            }

            var go = new GameObject("__digit_measure", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var t = go.GetComponent<TextMeshProUGUI>();
                t.font = font;
                t.fontSize = 100f;
                t.enableWordWrapping = false;
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int d = 0; d <= 9; d++)
                {
                    Vector2 size = t.GetPreferredValues(d.ToString(), 2000f, 2000f);
                    if (size.x < min) min = size.x;
                    if (size.x > max) max = size.x;
                }

                return max - min;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null && p != root)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }

            return sb.ToString();
        }

        private static string Size(RectTransform rt)
        {
            return rt == null ? "n/a" : Mathf.RoundToInt(rt.rect.width) + "x" + Mathf.RoundToInt(rt.rect.height);
        }

        private static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c).Substring(0, 6).ToUpperInvariant()
                + (c.a < 0.99f ? "@" + Mathf.RoundToInt(c.a * 100) : string.Empty);
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ui_r2_audit.txt", sb.ToString());
            File.WriteAllText("Logs/ui_r3_audit.txt", sb.ToString());
            Debug.Log("[UI audit] written Logs/ui_r2_audit.txt + Logs/ui_r3_audit.txt");
        }
    }
}