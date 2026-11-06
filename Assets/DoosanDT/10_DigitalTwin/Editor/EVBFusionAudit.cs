using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// Fusion-session helper (2026-11-03). READ-ONLY audits of the EVB content migrated from
    /// VR_EVB_DL into this project, plus one idempotent placement helper.
    /// No Play mode, no ROS, no recording, no motion.
    /// </summary>
    public static class EVBFusionAudit
    {
        private static readonly string[] MigratedRoots =
        {
            "Assets/DoosanDT/20_BatteryDisassembly/Assets", "Assets/DoosanDT/20_BatteryDisassembly/Scripts", "Assets/DoosanDT/20_BatteryDisassembly/Fonts", "Assets/DoosanDT/20_BatteryDisassembly/Shaders",
            "Assets/DoosanDT/20_BatteryDisassembly/Animation", "Assets/DoosanDT/20_BatteryDisassembly/Editor", "Assets/DoosanDT/20_BatteryDisassembly/Others"
        };

        private static string ReportPath
        {
            get
            {
                string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                return Path.Combine(projectRoot, "Logs", "evb_fusion_audit.txt");
            }
        }

        [MenuItem("DigitalTwin/Fusion/1 Audit Migrated EVB Assets (read-only)")]
        public static void AuditMigratedAssets()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EVB fusion asset audit  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("project=" + Directory.GetParent(Application.dataPath)!.FullName);
            sb.AppendLine("roots=" + string.Join(",", MigratedRoots));
            sb.AppendLine();

            int missingScripts = 0, prefabCount = 0, nullShader = 0, builtinShader = 0;
            var missingDetail = new List<string>();
            var shaderDetail = new List<string>();
            var tagSet = new HashSet<string>(InternalEditorUtility.tags);
            var unknownTags = new HashSet<string>();
            var layerSet = new SortedDictionary<int, int>();

            // ---- prefabs ----
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", MigratedRoots);
            prefabCount = prefabGuids.Length;
            sb.AppendLine("== prefabs ==");
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine("  LOAD_FAIL " + path);
                    continue;
                }
                var comps = prefab.GetComponentsInChildren<Component>(true);
                int nulls = 0;
                foreach (var c in comps)
                {
                    if (c == null) nulls++;
                }
                var all = prefab.GetComponentsInChildren<Transform>(true);
                foreach (var t in all)
                {
                    if (!tagSet.Contains(t.gameObject.tag)) unknownTags.Add(t.gameObject.tag);
                    if (t.gameObject.layer != 0)
                    {
                        if (!layerSet.ContainsKey(t.gameObject.layer)) layerSet[t.gameObject.layer] = 0;
                        layerSet[t.gameObject.layer]++;
                    }
                }
                missingScripts += nulls;
                if (nulls > 0)
                {
                    missingDetail.Add(string.Format("  {0} : missingScriptInstances={1} objects={2}", path, nulls, all.Length));
                }
                sb.AppendLine(string.Format("  {0} | objects={1} missingScript={2}", path, all.Length, nulls));
            }

            // ---- materials / shaders ----
            sb.AppendLine();
            sb.AppendLine("== materials ==");
            string[] matGuids = AssetDatabase.FindAssets("t:Material", MigratedRoots);
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    sb.AppendLine("  LOAD_FAIL " + path);
                    continue;
                }
                bool bad = mat.shader == null;
                bool builtin = !bad && (mat.shader.name == "Standard" || mat.shader.name == "Standard (Specular setup)"
                                        || mat.shader.name == "Legacy Shaders/Diffuse");
                if (bad) nullShader++;
                if (builtin) builtinShader++;
                if (bad || builtin)
                {
                    shaderDetail.Add(string.Format("  {0} : shader={1}{2}", path, bad ? "<NULL>" : mat.shader.name, builtin ? " (built-in, URP will render pink)" : ""));
                }
            }
            sb.AppendLine(string.Format("  materials={0} nullShader={1} builtinShader={2}", matGuids.Length, nullShader, builtinShader));

            // ---- texture / font / model counts ----
            sb.AppendLine();
            sb.AppendLine("== other imported types ==");
            foreach (var t in new[] { "Texture", "Font", "Model", "Shader", "AnimationClip", "MonoScript" })
            {
                sb.AppendLine(string.Format("  {0}={1}", t, AssetDatabase.FindAssets("t:" + t, MigratedRoots).Length));
            }

            // ---- tag/layer usage ----
            sb.AppendLine();
            sb.AppendLine("== tags/layers used by migrated prefabs ==");
            sb.AppendLine("  unknownTags=" + (unknownTags.Count == 0 ? "<none>" : string.Join(",", unknownTags)));
            foreach (var kv in layerSet) sb.AppendLine(string.Format("  layer[{0}] usedByObjects={1}", kv.Key, kv.Value));

            // ---- summary ----
            sb.AppendLine();
            sb.AppendLine("== missing-script detail ==");
            if (missingDetail.Count == 0) sb.AppendLine("  <none>");
            foreach (var line in missingDetail) sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("== shader detail ==");
            if (shaderDetail.Count == 0) sb.AppendLine("  <none>");
            foreach (var line in shaderDetail) sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine(string.Format("SUMMARY prefabs={0} missingScriptInstances={1} materials={2} nullShader={3} builtinShader={4}",
                prefabCount, missingScripts, matGuids.Length, nullShader, builtinShader));

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[EVB Fusion] asset audit written to " + ReportPath + "\n" + sb.ToString());
        }

        [MenuItem("DigitalTwin/Fusion/2 Audit Current Scene (read-only)")]
        public static void AuditCurrentScene()
        {
            var sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("EVB fusion scene audit  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("scene=" + scene.path + " dirty=" + scene.isDirty + " loadedScenes=" + SceneManager.loadedSceneCount);
            sb.AppendLine();

            int roots = 0, gos = 0, missing = 0;
            var rootNames = new List<string>();
            var missingObjects = new List<string>();
            var evbRoots = new List<string>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                roots++;
                rootNames.Add(go.name);
                var all = go.GetComponentsInChildren<Transform>(true);
                gos += all.Length;
                if (go.name.StartsWith("EVB", StringComparison.OrdinalIgnoreCase)) evbRoots.Add(go.name);
                foreach (var t in all)
                {
                    var comps = t.GetComponents<Component>();
                    foreach (var c in comps)
                    {
                        if (c == null)
                        {
                            missing++;
                            missingObjects.Add(t.gameObject.name + " @ " + GetPath(t));
                        }
                    }
                }
            }
            sb.AppendLine("roots=" + roots + " gameObjects=" + gos);
            sb.AppendLine("rootNames=" + string.Join(" | ", rootNames));
            sb.AppendLine("evbRoots=" + (evbRoots.Count == 0 ? "<none>" : string.Join(" | ", evbRoots)));
            sb.AppendLine("missingScriptInstances=" + missing);
            foreach (var m in missingObjects) sb.AppendLine("  MISSING " + m);

            // DigitalTwin machinery presence
            sb.AppendLine();
            sb.AppendLine("== chapter4 machinery ==");
            foreach (var typeName in new[] { "TwinRecorder", "TwinUIController", "DigitalTwinRuntime", "DartStudioBridge", "Ros2Bridge", "TwinCommandController", "TwinExperimentTracker", "Ros2ExperimentPanel" })
            {
                int count = 0;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    var all = go.GetComponentsInChildren<Transform>(true);
                    foreach (var t in all)
                    {
                        foreach (var c in t.GetComponents<Component>())
                        {
                            if (c != null && c.GetType().Name == typeName) count++;
                        }
                    }
                }
                sb.AppendLine("  " + typeName + "=" + count);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[EVB Fusion] scene audit written to " + ReportPath + "\n" + sb.ToString());
        }

        [MenuItem("DigitalTwin/Fusion/4 Report Root Bounds (read-only)")]
        public static void ReportRootBounds()
        {
            var sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("EVB fusion bounds report  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("scene=" + scene.path);

            var rootBounds = new Dictionary<string, Bounds>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds b = default;
                bool has = false;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!has) continue;
                rootBounds[go.name] = b;
                sb.AppendLine(string.Format("  ROOT {0,-32} center={1} size={2} minY={3:F3}", go.name, Fmt(b.center), Fmt(b.size), b.min.y));
            }

            // pairwise overlap with the fusion content
            Bounds evb;
            if (rootBounds.TryGetValue("EVB_Fusion", out evb))
            {
                sb.AppendLine();
                sb.AppendLine("== overlap test vs EVB_Fusion (positive margin means intersecting) ==");
                foreach (var kv in rootBounds)
                {
                    if (kv.Key == "EVB_Fusion") continue;
                    var a = kv.Value;
                    bool x = a.min.x < evb.max.x && a.max.x > evb.min.x;
                    bool y = a.min.y < evb.max.y && a.max.y > evb.min.y;
                    bool z = a.min.z < evb.max.z && a.max.z > evb.min.z;
                    sb.AppendLine(string.Format("  {0,-32} intersects={1}", kv.Key, x && y && z));
                }
                // EVB children bounds (top level, useful for eyeballing)
                sb.AppendLine();
                sb.AppendLine("== EVB_Fusion top-level children ==");
                var evbRoot = FindRoot("EVB_Fusion");
                if (evbRoot != null)
                {
                    foreach (Transform child in evbRoot.transform)
                    {
                        Bounds cb;
                        if (TryBounds(child.gameObject, out cb))
                        {
                            sb.AppendLine(string.Format("  {0,-24} center={1} size={2} minY={3:F3}", child.name, Fmt(cb.center), Fmt(cb.size), cb.min.y));
                        }
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[EVB Fusion] bounds report written to " + ReportPath + "\n" + sb.ToString());
        }

        private static GameObject FindRoot(string name)
        {
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == name) return go;
            }
            return null;
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool has = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }

        private static string Fmt(Vector3 v)
        {
            return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        }

        [MenuItem("DigitalTwin/Fusion/3 Place EVB Battery Root (idempotent)")]
        public static void PlaceEvbRoot()
        {
            const string rootName = "EVB_Fusion";
            const string prefabPath = "Assets/DoosanDT/20_BatteryDisassembly/Assets/Prefabs/爆炸用.prefab";
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == rootName)
                {
                    Debug.Log("[EVB Fusion] root already exists, nothing done: " + rootName);
                    return;
                }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[EVB Fusion] prefab not found: " + prefabPath);
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = rootName;
            instance.transform.position = new Vector3(0.55f, 0f, 0.35f);
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            instance.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(instance, "Place EVB battery");
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[EVB Fusion] placed " + prefabPath + " as root '" + rootName + "' at " + instance.transform.position);
        }

        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }
    }
}