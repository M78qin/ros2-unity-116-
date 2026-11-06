// TwinUILegacyStage.cs
// P1a 遗留 UI 舞台化（批次 1）：盘点 / 禁用 / 恢复 3 个遗留 WorldSpace Canvas。
//
// 菜单：
//   DigitalTwin/UI/21 Legacy UI Inventory                —— 只读扫描全部 Canvas，写 Logs/ui_legacy_inventory.json
//   DigitalTwin/UI/22 Disable Legacy Canvases (batch1)   —— 按精确名字禁用 panel1 / panel2_R / panel2_R (1)，保存场景（幂等）
//   DigitalTwin/UI/23 Restore Legacy Canvases            —— 恢复上述 3 个 Canvas，保存场景（回滚用）
//
// 约束：只改这 3 个对象的 activeSelf；不删对象、不动相机/Layer/EventSystem/TwinUiSettings/数据链。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUILegacyStage
    {
        private const string LogPath = "Logs/ui_legacy_stage.txt";
        private const string InventoryPath = "Logs/ui_legacy_inventory.json";
        private const string ResultTag = "[LEGACY]";

        /// <summary>批次 1 要撤下舞台的 3 个遗留 Canvas（精确名字，含空格/括号）。</summary>
        private static readonly string[] LegacyCanvasNames =
        {
            "panel1",
            "panel2_R",
            "panel2_R (1)"
        };

        // ---------------------------------------------------------------- menu 21

        [MenuItem("DigitalTwin/UI/21 Legacy UI Inventory", false, 210)]
        public static void Inventory()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Legacy UI Inventory (UI/21)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine(ResultTag + " WARN skip: play mode active (isPlaying=" + EditorApplication.isPlaying + ")");
                sb.AppendLine("RESULT: FAIL");
                WriteLog(sb);
                Debug.LogWarning(ResultTag + " inventory skipped: play mode active");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            List<CanvasInfo> canvases = ScanCanvases(scene, out int scannedRoots);
            int activeSelf = canvases.Count(c => c.ActiveSelf);
            int activeInHierarchy = canvases.Count(c => c.ActiveInHierarchy);
            int missingSubtree = canvases.Sum(c => c.MissingScriptCount);
            int legacyFound = LegacyCanvasNames.Count(n => canvases.Any(c => c.Name == n));

            string json = BuildInventoryJson(scene, canvases, scannedRoots, activeSelf, activeInHierarchy, missingSubtree);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(InventoryPath, json, new UTF8Encoding(false));

            sb.AppendLine(ResultTag + " canvases=" + canvases.Count
                + " active=" + activeSelf
                + " activeInHierarchy=" + activeInHierarchy
                + " legacyTargetsFound=" + legacyFound
                + " missingScripts=" + missingSubtree
                + " roots=" + scannedRoots
                + " scene=" + scene.name);
            foreach (CanvasInfo info in canvases)
            {
                sb.AppendLine(ResultTag + " canvas name=" + info.Name
                    + " active=" + info.ActiveSelf
                    + " activeInHierarchy=" + info.ActiveInHierarchy
                    + " renderMode=" + info.RenderMode
                    + " camera=" + (string.IsNullOrEmpty(info.TargetCameraPath) ? "(none)" : info.TargetCameraPath)
                    + " children=" + info.ChildCount
                    + " missingScripts=" + info.MissingScriptCount
                    + " path=" + info.HierarchyPath);
            }
            foreach (string name in LegacyCanvasNames)
            {
                CanvasInfo match = canvases.FirstOrDefault(c => c.Name == name);
                sb.AppendLine(ResultTag + " target name=" + name
                    + " found=" + (match != null)
                    + (match != null ? " active=" + match.ActiveSelf + " path=" + match.HierarchyPath : string.Empty));
            }
            sb.AppendLine(ResultTag + " file=" + InventoryPath + " bytes=" + new FileInfo(InventoryPath).Length);
            sb.AppendLine("RESULT: PASS");
            WriteLog(sb);
            Debug.Log(ResultTag + " canvases=" + canvases.Count
                + " active=" + activeSelf
                + " legacyTargetsFound=" + legacyFound
                + " missingScripts=" + missingSubtree
                + " file=" + InventoryPath
                + " result=PASS");
        }

        // ---------------------------------------------------------------- menu 22

        [MenuItem("DigitalTwin/UI/22 Disable Legacy Canvases (batch1)", false, 211)]
        public static void DisableLegacy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Disable Legacy Canvases (UI/22)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine(ResultTag + " WARN skip: play mode active (isPlaying=" + EditorApplication.isPlaying + ")");
                sb.AppendLine("RESULT: FAIL");
                WriteLog(sb);
                Debug.LogWarning(ResultTag + " disable skipped: play mode active");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            List<CanvasInfo> canvases = ScanCanvases(scene, out _);
            int beforeActive = canvases.Count(c => c.ActiveSelf);
            sb.AppendLine(ResultTag + " before activeCanvas=" + beforeActive + " active=[" + JoinActiveNames(canvases) + "]");

            var missing = new List<string>();
            int changed = 0;
            var toDisable = new List<GameObject>();
            foreach (string name in LegacyCanvasNames)
            {
                List<CanvasInfo> matches = canvases.Where(c => c.Name == name).ToList();
                if (matches.Count == 0)
                {
                    missing.Add(name);
                    sb.AppendLine(ResultTag + " WARN missing name=" + name);
                    continue;
                }

                foreach (CanvasInfo match in matches)
                {
                    GameObject go = match.GameObject;
                    if (go == null)
                    {
                        missing.Add(name);
                        continue;
                    }

                    bool wasActive = go.activeSelf;
                    if (wasActive)
                    {
                        toDisable.Add(go);
                    }

                    sb.AppendLine(ResultTag + " target name=" + name
                        + " path=" + match.HierarchyPath
                        + " activeBefore=" + wasActive
                        + " renderMode=" + match.RenderMode);
                }
            }

            foreach (GameObject go in toDisable)
            {
                go.SetActive(false);
                changed++;
                EditorUtility.SetDirty(go);
            }

            List<CanvasInfo> after = ScanCanvases(scene, out _);
            int afterActive = after.Count(c => c.ActiveSelf);
            sb.AppendLine(ResultTag + " after activeCanvas=" + afterActive + " active=[" + JoinActiveNames(after) + "]");
            foreach (CanvasInfo info in after.Where(c => LegacyCanvasNames.Contains(c.Name)))
            {
                sb.AppendLine(ResultTag + " target name=" + info.Name
                    + " path=" + info.HierarchyPath
                    + " activeAfter=" + info.ActiveSelf);
            }

            bool saved = false;
            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            string scenePath = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            string sha = changed > 0 ? HashSceneFile(scenePath) : "(unsaved)";
            sb.AppendLine(ResultTag + " requested=" + LegacyCanvasNames.Length
                + " disabled=" + changed
                + " missing=" + missing.Count
                + " activeCanvas=" + afterActive
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + sha);

            bool pass = missing.Count == 0;
            sb.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL"));
            if (missing.Count > 0)
            {
                sb.AppendLine(ResultTag + " missingNames=[" + string.Join(",", missing) + "]");
            }

            WriteLog(sb);
            Debug.Log(ResultTag + " requested=" + LegacyCanvasNames.Length
                + " disabled=" + changed
                + " activeCanvas=" + afterActive
                + " saved=" + (saved ? "yes" : "no")
                + " missing=" + missing.Count
                + " result=" + (pass ? "PASS" : "FAIL"));
        }

        // ---------------------------------------------------------------- menu 23

        [MenuItem("DigitalTwin/UI/23 Restore Legacy Canvases", false, 212)]
        public static void RestoreLegacy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Restore Legacy Canvases (UI/23)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine(ResultTag + " WARN skip: play mode active (isPlaying=" + EditorApplication.isPlaying + ")");
                sb.AppendLine("RESULT: FAIL");
                WriteLog(sb);
                Debug.LogWarning(ResultTag + " restore skipped: play mode active");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            List<CanvasInfo> canvases = ScanCanvases(scene, out _);
            int beforeActive = canvases.Count(c => c.ActiveSelf);
            sb.AppendLine(ResultTag + " before activeCanvas=" + beforeActive + " active=[" + JoinActiveNames(canvases) + "]");

            var missing = new List<string>();
            int changed = 0;
            var toEnable = new List<GameObject>();
            foreach (string name in LegacyCanvasNames)
            {
                List<CanvasInfo> matches = canvases.Where(c => c.Name == name).ToList();
                if (matches.Count == 0)
                {
                    missing.Add(name);
                    sb.AppendLine(ResultTag + " WARN missing name=" + name);
                    continue;
                }

                foreach (CanvasInfo match in matches)
                {
                    GameObject go = match.GameObject;
                    if (go == null)
                    {
                        missing.Add(name);
                        continue;
                    }

                    bool wasActive = go.activeSelf;
                    if (!wasActive)
                    {
                        toEnable.Add(go);
                    }

                    sb.AppendLine(ResultTag + " target name=" + name
                        + " path=" + match.HierarchyPath
                        + " activeBefore=" + wasActive);
                }
            }

            foreach (GameObject go in toEnable)
            {
                go.SetActive(true);
                changed++;
                EditorUtility.SetDirty(go);
            }

            List<CanvasInfo> after = ScanCanvases(scene, out _);
            int afterActive = after.Count(c => c.ActiveSelf);
            sb.AppendLine(ResultTag + " after activeCanvas=" + afterActive + " active=[" + JoinActiveNames(after) + "]");
            foreach (CanvasInfo info in after.Where(c => LegacyCanvasNames.Contains(c.Name)))
            {
                sb.AppendLine(ResultTag + " target name=" + info.Name
                    + " path=" + info.HierarchyPath
                    + " activeAfter=" + info.ActiveSelf);
            }

            bool saved = false;
            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            string scenePath = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            string sha = changed > 0 ? HashSceneFile(scenePath) : "(unsaved)";
            sb.AppendLine(ResultTag + " requested=" + LegacyCanvasNames.Length
                + " restored=" + changed
                + " missing=" + missing.Count
                + " activeCanvas=" + afterActive
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + sha);

            bool pass = missing.Count == 0;
            sb.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL"));
            if (missing.Count > 0)
            {
                sb.AppendLine(ResultTag + " missingNames=[" + string.Join(",", missing) + "]");
            }

            WriteLog(sb);
            Debug.Log(ResultTag + " requested=" + LegacyCanvasNames.Length
                + " restored=" + changed
                + " activeCanvas=" + afterActive
                + " saved=" + (saved ? "yes" : "no")
                + " missing=" + missing.Count
                + " result=" + (pass ? "PASS" : "FAIL"));
        }

        // ---------------------------------------------------------------- scan

        private sealed class CanvasInfo
        {
            public string Name;
            public string HierarchyPath;
            public bool ActiveSelf;
            public bool ActiveInHierarchy;
            public string RenderMode;
            public string TargetCameraPath;
            public int ChildCount;
            public int MissingScriptCount;
            public List<string> ComponentTypes;
            public GameObject GameObject;
        }

        private static List<CanvasInfo> ScanCanvases(Scene scene, out int scannedRoots)
        {
            var result = new List<CanvasInfo>();
            GameObject[] roots = scene.GetRootGameObjects();
            scannedRoots = roots.Length;

            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas == null || canvas.gameObject == null)
                    {
                        continue;
                    }

                    var info = new CanvasInfo
                    {
                        Name = canvas.gameObject.name,
                        HierarchyPath = BuildHierarchyPath(canvas.transform),
                        ActiveSelf = canvas.gameObject.activeSelf,
                        ActiveInHierarchy = canvas.gameObject.activeInHierarchy,
                        RenderMode = canvas.renderMode.ToString(),
                        TargetCameraPath = canvas.worldCamera != null
                            ? BuildHierarchyPath(canvas.worldCamera.transform)
                            : string.Empty,
                        ChildCount = canvas.transform.childCount,
                        MissingScriptCount = CountMissingScripts(canvas.gameObject),
                        ComponentTypes = canvas.gameObject.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name)
                            .Distinct()
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToList(),
                        GameObject = canvas.gameObject
                    };
                    result.Add(info);
                }
            }

            return result
                .OrderBy(c => c.HierarchyPath, StringComparer.Ordinal)
                .ToList();
        }

        private static int CountMissingScripts(GameObject root)
        {
            int missing = 0;
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    missing++;
                }
            }

            return missing;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "(null)";
            }

            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string JoinActiveNames(List<CanvasInfo> canvases)
        {
            return string.Join(",", canvases.Where(c => c.ActiveSelf).Select(c => c.Name).ToArray());
        }

        private static string HashSceneFile(string scenePath)
        {
            try
            {
                string full = Path.GetFullPath(scenePath);
                if (!File.Exists(full))
                {
                    return "(missing:" + scenePath + ")";
                }

                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream stream = File.OpenRead(full))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                    {
                        sb.Append(b.ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "(hash-error:" + e.GetType().Name + ")";
            }
        }

        // ---------------------------------------------------------------- json

        private static string BuildInventoryJson(
            Scene scene,
            List<CanvasInfo> canvases,
            int scannedRoots,
            int activeSelf,
            int activeInHierarchy,
            int missingSubtree)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"generatedAt\": \"" + Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "\",");
            sb.AppendLine("  \"unityVersion\": \"" + Escape(Application.unityVersion) + "\",");
            sb.AppendLine("  \"scene\": \"" + Escape(scene.name) + "\",");
            sb.AppendLine("  \"scenePath\": \"" + Escape(scene.path) + "\",");
            sb.AppendLine("  \"scannedRoots\": " + scannedRoots + ",");
            sb.AppendLine("  \"summary\": {");
            sb.AppendLine("    \"canvases\": " + canvases.Count + ",");
            sb.AppendLine("    \"activeSelf\": " + activeSelf + ",");
            sb.AppendLine("    \"activeInHierarchy\": " + activeInHierarchy + ",");
            sb.AppendLine("    \"missingScriptsInCanvasSubtrees\": " + missingSubtree + ",");
            sb.AppendLine("    \"legacyBatch1Targets\": " + LegacyCanvasNames.Length + ",");
            sb.AppendLine("    \"legacyBatch1Found\": " + LegacyCanvasNames.Count(n => canvases.Any(c => c.Name == n)));
            sb.AppendLine("  },");
            sb.AppendLine("  \"canvases\": [");
            for (int i = 0; i < canvases.Count; i++)
            {
                CanvasInfo c = canvases[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"name\": \"" + Escape(c.Name) + "\",");
                sb.AppendLine("      \"hierarchyPath\": \"" + Escape(c.HierarchyPath) + "\",");
                sb.AppendLine("      \"active\": " + (c.ActiveSelf ? "true" : "false") + ",");
                sb.AppendLine("      \"activeInHierarchy\": " + (c.ActiveInHierarchy ? "true" : "false") + ",");
                sb.AppendLine("      \"renderMode\": \"" + Escape(c.RenderMode) + "\",");
                sb.AppendLine("      \"targetCamera\": \"" + Escape(c.TargetCameraPath) + "\",");
                sb.AppendLine("      \"childCount\": " + c.ChildCount + ",");
                sb.AppendLine("      \"missingScriptCount\": " + c.MissingScriptCount + ",");
                sb.AppendLine("      \"componentTypes\": [" + string.Join(",", c.ComponentTypes.Select(t => "\"" + Escape(t) + "\"").ToArray()) + "]");
                sb.Append("    }");
                sb.AppendLine(i == canvases.Count - 1 ? string.Empty : ",");
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"legacyBatch1\": [");
            for (int i = 0; i < LegacyCanvasNames.Length; i++)
            {
                string name = LegacyCanvasNames[i];
                CanvasInfo match = canvases.FirstOrDefault(c => c.Name == name);
                sb.AppendLine("    {");
                sb.AppendLine("      \"name\": \"" + Escape(name) + "\",");
                sb.AppendLine("      \"found\": " + (match != null ? "true" : "false") + ",");
                sb.AppendLine("      \"hierarchyPath\": \"" + Escape(match != null ? match.HierarchyPath : string.Empty) + "\",");
                sb.AppendLine("      \"active\": " + (match != null && match.ActiveSelf ? "true" : "false"));
                sb.Append("    }");
                sb.AppendLine(i == LegacyCanvasNames.Length - 1 ? string.Empty : ",");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- log

        private static void WriteLog(StringBuilder sb)
        {
            try
            {
                Directory.CreateDirectory("Logs");
                File.AppendAllText(LogPath, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LEGACY] log write failed: " + e.Message);
            }
        }
    }
}