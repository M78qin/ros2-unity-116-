// 中文 UI 字体静态子集资产：只读源字体，生成 1 个 SDF 资产 + 可选切换 TMP 默认字体。
// 新增于 2026-11-03（DSH dsh_2026-11-03 批次）；不修改任何既有脚本/场景。
//
// 背景（实读结论）：
//   Assets/TextMesh Pro/Fonts/宋体 SDF.asset 是 AtlasPopulationMode=Dynamic、只预烘了 26 个字符，
//   且仅作为 TMP_Settings 的全局 fallback；运行时每遇一个新汉字都要插图集，会产生帧尖峰。
// 本脚本：把 1238 个"项目实际用字"一次性烘进静态图集，用作主字体；宋体 SDF 仍留作 fallback。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DigitalTwin.EditorTools
{
    public static class Chapter4TmpFontAsset
    {
        private const string FontFolder = "Assets/DoosanDT/10_DigitalTwin/Art/Fonts";
        private const string SourceFontPath = FontFolder + "/NotoSansSC-UI.otf";
        private const string CharsetPath = FontFolder + "/charset_ui.txt";
        private const string OutputPath = FontFolder + "/NotoSansSC-UI SDF.asset";
        private const string FallbackPath = "Assets/TextMesh Pro/Fonts/宋体 SDF.asset";
        private const string LegacyDefaultPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        private const int SamplingPointSize = 72;
        private const int AtlasPadding = 5;
        private const int AtlasWidth = 4096;
        private const int AtlasHeight = 4096;

        [MenuItem("DigitalTwin/Chapter4/10 Build SC Static Font Asset", false, 901)]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (font == null)
            {
                Debug.LogError("[CH4 Font] BUILD_FAIL 源字体缺失: " + SourceFontPath + "（先让 Unity 导入该 otf）");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
            {
                Debug.LogWarning("[CH4 Font] BUILD_SKIP 已存在同名资产，不覆盖: " + OutputPath);
                return;
            }

            var charsetAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CharsetPath);
            if (charsetAsset == null)
            {
                Debug.LogError("[CH4 Font] BUILD_FAIL 字表缺失: " + CharsetPath);
                return;
            }
            string charset = charsetAsset.text;
            if (string.IsNullOrEmpty(charset))
            {
                Debug.LogError("[CH4 Font] BUILD_FAIL 字表为空: " + CharsetPath);
                return;
            }

            // 先以 Dynamic 建，才能批量烘焙；烘完切 Static，运行时不再插图集。
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasWidth, AtlasHeight, AtlasPopulationMode.Dynamic, false);
            if (fontAsset == null)
            {
                Debug.LogError("[CH4 Font] BUILD_FAIL CreateFontAsset 返回 null（字体格式或 FontEngine 初始化问题）");
                return;
            }

            string missing;
            fontAsset.TryAddCharacters(charset, out missing, false);
            int glyphCount = fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0;

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            if (fallback != null)
            {
                fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            }

            AssetDatabase.CreateAsset(fontAsset, OutputPath);
            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null) AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }
            if (fontAsset.material != null) AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int atlasCount = fontAsset.atlasTextures != null ? fontAsset.atlasTextures.Length : 0;
            int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
            Debug.Log(string.Format(
                "[CH4 Font] BUILD_OK path={0} glyphs={1} atlasCount={2} missing={3} atlas={4}x{5} sampling={6} padding={7} mode={8} fallback={9}",
                OutputPath, glyphCount, atlasCount, missingCount, AtlasWidth, AtlasHeight,
                SamplingPointSize, AtlasPadding, fontAsset.atlasPopulationMode,
                fallback != null ? "宋体 SDF" : "NONE"));
            if (missingCount > 0)
            {
                const int preview = 40;
                Debug.LogWarning("[CH4 Font] 缺字前 " + preview + " 个: " + missing.Substring(0, Mathf.Min(preview, missing.Length)));
            }
        }

        [MenuItem("DigitalTwin/Chapter4/11 Set SC Font As TMP Default", false, 902)]
        public static void SetAsDefault()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);
            if (asset == null)
            {
                Debug.LogError("[CH4 Font] SET_FAIL 资产不存在，先跑 10 Build: " + OutputPath);
                return;
            }
            var settings = TMP_Settings.instance;
            if (settings == null)
            {
                Debug.LogError("[CH4 Font] SET_FAIL TMP_Settings.instance 为 null（TMP Essential Resources 未导入？）");
                return;
            }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop == null)
            {
                Debug.LogError("[CH4 Font] SET_FAIL 找不到 m_defaultFontAsset 序列化字段");
                return;
            }
            var oldAsset = prop.objectReferenceValue as TMP_FontAsset;
            string oldGuid = "<none>";
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(oldAsset, out oldGuid, out long oldLocalId);

            prop.objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("[CH4 Font] SET_OK TMP 默认字体 -> " + OutputPath + "（原默认 guid=" + oldGuid + "）");
        }

        [MenuItem("DigitalTwin/Chapter4/12 Restore TMP Default Font", false, 903)]
        public static void RestoreDefault()
        {
            var legacy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyDefaultPath);
            if (legacy == null)
            {
                Debug.LogError("[CH4 Font] RESTORE_FAIL 找不到原默认资产: " + LegacyDefaultPath);
                return;
            }
            var settings = TMP_Settings.instance;
            if (settings == null) { Debug.LogError("[CH4 Font] RESTORE_FAIL TMP_Settings.instance 为 null"); return; }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            prop.objectReferenceValue = legacy;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[CH4 Font] RESTORE_OK TMP 默认字体 -> LiberationSans SDF");
        }

        [MenuItem("DigitalTwin/Chapter4/13 Report TMP Font State", false, 904)]
        public static void Report()
        {
            var settings = TMP_Settings.instance;
            var def = settings != null ? TMP_Settings.defaultFontAsset : null;
            var built = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);

            string defGuid = "<none>";
            if (def != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(def, out defGuid, out long _);

            Debug.Log(string.Format(
                "[CH4 Font] STATE default={0} guid={1} | built={2} glyphs={3} mode={4} | 宋体fallbackAsset={5}",
                def != null ? def.name : "<null>", defGuid,
                built != null ? "present" : "MISSING",
                built != null && built.characterTable != null ? built.characterTable.Count : 0,
                built != null ? built.atlasPopulationMode.ToString() : "-",
                fallback != null ? "present" : "MISSING"));
        }
    }
}
#endif