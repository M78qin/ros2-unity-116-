// Read-only glyph coverage check: scene texts + all runtime string constants must exist in the static atlas.
// Menu: DigitalTwin/UI/16 Check R2 Glyphs (read-only)
// 覆盖：R2 三段（任务/Monitor）+ 三期工业数据台 / 上下文条 / 设置面板的静态文案常量，
// 以及运行时会拼出来的数字、单位、状态词（避免"截图时才发现缺字"）。
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIR2GlyphCheck
    {
        private const string RootPath = "30_界面与交互/UI_二期面板";

        [MenuItem("DigitalTwin/UI/16 Check R2 Glyphs (read-only)", false, 16)]
        public static void Check()
        {
            var root = GameObject.Find(RootPath);
            var sb = new StringBuilder();
            sb.AppendLine("glyph check (R2 + R3)  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (root == null)
            {
                sb.AppendLine("RESULT: FAIL - root missing");
                Write(sb);
                return;
            }

            var texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            var missing = new SortedDictionary<char, List<string>>();
            int checkedStrings = 0;
            int checkedChars = 0;

            foreach (var text in texts)
            {
                string value = text.text;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                checkedStrings++;
                var font = text.font != null ? text.font : TMP_Settings.defaultFontAsset;
                if (font == null)
                {
                    missing['?'] = new List<string> { "NO_FONT:" + text.name };
                    continue;
                }

                foreach (char c in value)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        continue;
                    }

                    checkedChars++;
                    if (!font.HasCharacter(c))
                    {
                        if (!missing.TryGetValue(c, out var owners))
                        {
                            owners = new List<string>();
                            missing[c] = owners;
                        }

                        string label = text.transform.parent == null ? text.name : text.transform.parent.name + "/" + text.name;
                        if (!owners.Contains(label))
                        {
                            owners.Add(label);
                        }
                    }
                }
            }

            // 运行时常量：反射 TwinUITextR3 / TwinUITextR2 的所有 public static string，外加格式化样例。
            var runtimeStrings = CollectRuntimeStrings();
            var runtimeMissing = new SortedDictionary<char, List<string>>();
            int runtimeChecked = 0;
            foreach (var pair in runtimeStrings)
            {
                foreach (char c in pair.Value)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        continue;
                    }

                    runtimeChecked++;
                    if (!TwinUIStyle.GetFont().HasCharacter(c))
                    {
                        if (!runtimeMissing.TryGetValue(c, out var owners))
                        {
                            owners = new List<string>();
                            runtimeMissing[c] = owners;
                        }

                        if (!owners.Contains(pair.Key))
                        {
                            owners.Add(pair.Key);
                        }
                    }
                }
            }

            sb.AppendLine("texts=" + texts.Length + " checkedStrings=" + checkedStrings + " checkedChars=" + checkedChars);
            sb.AppendLine("missingDistinct=" + missing.Count);
            foreach (var pair in missing)
            {
                sb.AppendLine("MISSING U+" + ((int)pair.Key).ToString("X4") + " '" + pair.Key + "' in "
                    + string.Join(", ", pair.Value.ToArray()));
            }

            sb.AppendLine("runtimeConstStrings=" + runtimeStrings.Count + " runtimeCheckedChars=" + runtimeChecked
                + " runtimeMissingDistinct=" + runtimeMissing.Count);
            foreach (var pair in runtimeMissing)
            {
                sb.AppendLine("RUNTIME_MISSING U+" + ((int)pair.Key).ToString("X4") + " '" + pair.Key + "' in "
                    + string.Join(", ", pair.Value.ToArray()));
            }

            int total = missing.Count + runtimeMissing.Count;
            sb.AppendLine(total == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Write(sb);
        }

        /// <summary>收集运行时会出现的字符串：文案常量 + 格式化结果（数字/单位/状态词）。</summary>
        private static Dictionary<string, string> CollectRuntimeStrings()
        {
            var list = new Dictionary<string, string>();
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            foreach (var type in new[] { typeof(TwinUITextR2), typeof(TwinUITextR3) })
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (field.FieldType == typeof(string))
                    {
                        list[type.Name + "." + field.Name] = (string)field.GetValue(null);
                    }
                    else if (field.FieldType == typeof(string[]))
                    {
                        var arr = (string[])field.GetValue(null);
                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Length; i++)
                            {
                                list[type.Name + "." + field.Name + "[" + i + "]"] = arr[i];
                            }
                        }
                    }
                }
            }

            var samples = new Dictionary<string, string>
            {
                { "fmt.hz", TwinUiFormat.Hz(99.9, true) + TwinUITextR3.UnitHz },
                { "fmt.ms", TwinUiFormat.Ms(3.73, 2, true) + TwinUITextR3.UnitMs },
                { "fmt.deg", TwinUiFormat.Deg(-123.45f, 2) + TwinUITextR3.UnitDeg },
                { "fmt.degps", TwinUiFormat.Deg(12.34f, 2) + TwinUITextR3.UnitDegPerSec },
                { "fmt.signed", TwinUiFormat.Signed(1.23, " °", 2) },
                { "fmt.ratio", TwinUiFormat.Ratio(0.421) },
                { "fmt.count", TwinUiFormat.Count(12) },
                { "fmt.state.ok", TwinUiFormat.StateFor(TwinUITone.Ok, true, true) },
                { "fmt.state.warn", TwinUiFormat.StateFor(TwinUITone.Warn, true, true) },
                { "fmt.state.crit", TwinUiFormat.StateFor(TwinUITone.Crit, true, true) },
                { "fmt.state.unknown", TwinUiFormat.StateFor(TwinUITone.Unknown, false, false) },
                { "fmt.missing", TwinUiFormat.Missing(true, true) },
                { "fmt.dash", TwinUiFormat.Dash },
                { "fmt.nm", "12.34" + TwinUITextR3.UnitNm },
                { "fmt.n", "12.34" + TwinUITextR3.UnitN },
                { "fmt.meter", "0.1234" + TwinUITextR3.UnitMeter },
                { "threshold.describe", TwinUiThresholds.CreateDefault().Describe() },
                { "tone.mark.ok", TwinUITheme.ToneMark(TwinUITone.Ok) },
                { "tone.mark.warn", TwinUITheme.ToneMark(TwinUITone.Warn) },
                { "tone.mark.crit", TwinUITheme.ToneMark(TwinUITone.Crit) },
                { "tone.label.ok", TwinUITheme.ToneLabel(TwinUITone.Ok) },
                { "tone.label.warn", TwinUITheme.ToneLabel(TwinUITone.Warn) },
                { "tone.label.crit", TwinUITheme.ToneLabel(TwinUITone.Crit) },
                { "tone.label.unknown", TwinUITheme.ToneLabel(TwinUITone.Unknown) }
            };

            foreach (var pair in samples)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    list[pair.Key] = pair.Value;
                }
            }

            return list;
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ui_r2_glyphs.txt", sb.ToString());
            File.WriteAllText("Logs/ui_r3_glyphs.txt", sb.ToString());
            Debug.Log("[UI glyphs] written Logs/ui_r2_glyphs.txt + Logs/ui_r3_glyphs.txt");
        }
    }
}