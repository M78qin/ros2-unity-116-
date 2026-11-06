// Verifies that every character this batch writes into the UI exists in the project's STATIC
// Chinese atlas before it reaches the scene.
//
// Why this exists: Art/Fonts/NotoSansSC-UI SDF.asset uses AtlasPopulationMode.Static with 1368
// glyphs generated from the characters used when the project was authored. Dynamic glyph addition
// is not available, and rebuilding the 34 MB atlas is out of scope. A character outside the set
// would silently render as tofu in every screenshot and in the thesis figures. So the copy is
// checked here, and the wording is changed instead of shipping a broken glyph.
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIFontCheck
    {
        public const string ScFontPath = "Assets/DoosanDT/10_DigitalTwin/Art/Fonts/NotoSansSC-UI SDF.asset";
        private const string LogPath = "Logs/ui_font_check.txt";

        public static TMP_FontAsset LoadScFont()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ScFontPath);
        }

        [MenuItem("DigitalTwin/UI/8 Check UI Font Coverage (read-only)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI font coverage check  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("font asset: " + ScFontPath);

            TMP_FontAsset font = LoadScFont();
            if (font == null)
            {
                sb.AppendLine("RESULT: FAIL - font asset not found");
                Write(sb);
                return;
            }

            sb.AppendLine("atlas: " + font.atlasWidth + "x" + font.atlasHeight
                + "  mode=" + font.atlasPopulationMode
                + "  glyphs=" + (font.characterTable == null ? 0 : font.characterTable.Count));
            sb.AppendLine();

            int checkedStrings = 0;
            int missingStrings = 0;
            var missingChars = new SortedSet<char>();

            foreach (string text in TwinUIShellText.All)
            {
                checkedStrings++;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                string missing = MissingChars(font, text);
                if (missing.Length == 0)
                {
                    sb.AppendLine("  OK      " + text);
                    continue;
                }

                missingStrings++;
                foreach (char c in missing)
                {
                    missingChars.Add(c);
                }

                sb.AppendLine("  MISSING " + text + "  -> [" + missing + "]");
            }

            // also verify the strings the legacy login panel will carry after the fix
            foreach (string text in new[] { "请输入账号", "请输入密码", "用户登录", "退役动力电池VR拆解层级化碰撞检测系统" })
            {
                checkedStrings++;
                string missing = MissingChars(font, text);
                if (missing.Length == 0)
                {
                    sb.AppendLine("  OK      " + text);
                    continue;
                }

                missingStrings++;
                foreach (char c in missing)
                {
                    missingChars.Add(c);
                }

                sb.AppendLine("  MISSING " + text + "  -> [" + missing + "]");
            }

            sb.AppendLine();
            sb.AppendLine("checked=" + checkedStrings + "  stringsWithMissingGlyphs=" + missingStrings);
            sb.AppendLine("missingCharSet=" + (missingChars.Count == 0 ? "-" : string.Join("", missingChars)));
            sb.AppendLine("RESULT: " + (missingStrings == 0 ? "PASS" : "FAIL"));
            Write(sb);

            if (missingStrings == 0)
            {
                Debug.Log("[UI Font] PASS - all UI copy is inside the static glyph set");
            }
            else
            {
                Debug.LogError("[UI Font] FAIL - " + missingStrings + " strings contain characters missing from the atlas; see " + LogPath);
            }
        }

        public static string MissingChars(TMP_FontAsset font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var missing = new StringBuilder();
            foreach (char c in text)
            {
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t')
                {
                    continue;
                }

                if (!font.HasCharacter(c))
                {
                    missing.Append(c);
                }
            }

            return missing.ToString();
        }

        public static bool IsInsideGlyphSet(TMP_FontAsset font, string text)
        {
            return MissingChars(font, text).Length == 0;
        }

        /// <summary>Assign the project SC font to a TMP text when it is currently on a non-SC font.</summary>
        public static bool EnsureScFont(TMP_Text text, out string report)
        {
            report = null;
            if (text == null)
            {
                return false;
            }

            TMP_FontAsset font = LoadScFont();
            if (font == null)
            {
                report = "SC font asset not found";
                return false;
            }

            if (text.font == font)
            {
                return false;
            }

            string before = text.font == null ? "<null>" : text.font.name;
            text.font = font;
            EditorUtility.SetDirty(text);
            report = TwinSceneLookup.PathOf(text.transform) + ": font " + before + " -> " + font.name;
            return true;
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}