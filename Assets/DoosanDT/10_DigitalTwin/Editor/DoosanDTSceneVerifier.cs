// Read-only verifier for the merged "Doosan-DT" scene (EDIT MODE ONLY - no Play, no streams).
// Menu: DigitalTwin/Doosan-DT/2 Verify Merged Scene (read-only) -> <project>/Logs/doosan_dt_verify.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using DigitalTwin;

namespace DigitalTwin.EditorTools
{
    public static class DoosanDTSceneVerifier
    {
        [MenuItem("DigitalTwin/Doosan-DT/2 Verify Merged Scene (read-only)")]
        public static void Verify()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("Doosan-DT scene verification  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("scene=" + scene.path + "  dirty=" + scene.isDirty + "  loadedScenes=" + SceneManager.sceneCount);
            sb.AppendLine();

            var roots = scene.GetRootGameObjects();
            int total = roots.Sum(r => r.GetComponentsInChildren<Transform>(true).Length);
            sb.AppendLine("roots=" + roots.Length + "  gameObjects(Transform)=" + total);
            sb.AppendLine("rootNames=" + string.Join(" | ", roots.Select(r => r.name)));
            sb.AppendLine();

            sb.AppendLine("== per group ==");
            foreach (var r in roots)
            {
                var all = r.GetComponentsInChildren<Transform>(true).Length;
                var active = r.GetComponentsInChildren<Transform>(false).Length;
                var rends = r.GetComponentsInChildren<Renderer>(false);
                var b = new Bounds(r.transform.position, Vector3.zero);
                bool has = false;
                foreach (var rend in rends)
                {
                    if (!has) { b = rend.bounds; has = true; } else b.Encapsulate(rend.bounds);
                }
                sb.AppendFormat("  {0,-16} active={1,-5}(all {2,-5}) groupActive={3,-5} renderers={4,-4} bounds={5}",
                    r.name, active, all, r.activeSelf, rends.Length, has ? b.center.ToString("F2") + " size " + b.size.ToString("F2") : "none");
                sb.AppendLine();
            }
            sb.AppendLine();

            sb.AppendLine("== duplicates / singletons ==");
            var cams = UnityEngine.Object.FindObjectsOfType<Camera>(true);
            sb.AppendLine("  Cameras total=" + cams.Length + " active=" + cams.Count(c => c.isActiveAndEnabled));
            foreach (var c in cams) sb.AppendLine("     " + (c.isActiveAndEnabled ? "[A]" : "[-]") + " " + c.name + " tag=" + c.tag + " depth=" + c.depth + " pos=" + c.transform.position.ToString("F2"));
            var listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
            sb.AppendLine("  AudioListeners total=" + listeners.Length + " active=" + listeners.Count(l => l.isActiveAndEnabled));
            var es = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
            sb.AppendLine("  EventSystems total=" + es.Length + " active=" + es.Count(e => e.isActiveAndEnabled));
            var graphers = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>(true);
            sb.AppendLine("  GraphicRaycasters total=" + graphers.Length + " active=" + graphers.Count(g => g.isActiveAndEnabled));
            var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
            sb.AppendLine("  Lights total=" + lights.Length + " active=" + lights.Count(l => l.isActiveAndEnabled) + " directionalActive=" + lights.Count(l => l.isActiveAndEnabled && l.type == LightType.Directional));
            sb.AppendLine();

            sb.AppendLine("== key behaviour counts (active | all) ==");
            Report<DisassemblySystem>(sb);
            Report<LayerGroupConfig>(sb);
            Report<DisassemblyPart>(sb);
            Report<PerformanceProfiler>(sb);
            Report<SmartRaycaster>(sb);
            Report<DigitalTwinRuntime>(sb);
            Report<TwinRecorder>(sb);
            Report<Ros2Bridge>(sb);
            Report<TwinUIController>(sb);
            Report<Ros2ExperimentPanel>(sb);
            sb.AppendLine();

            sb.AppendLine("== missing scripts ==");
            int missing = 0;
            foreach (var r in roots)
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    var comps = t.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] == null)
                        {
                            missing++;
                            sb.AppendLine("  MISSING on " + Path2(t) + " (component slot " + i + ")");
                        }
                    }
                }
            sb.AppendLine("  missingScriptInstances=" + missing);
            sb.AppendLine();

            sb.AppendLine("== tags ==");
            var tags = new Dictionary<string, int>();
            foreach (var r in roots) foreach (var t in r.GetComponentsInChildren<Transform>(true))
                tags[t.gameObject.tag] = tags.TryGetValue(t.gameObject.tag, out var n) ? n + 1 : 1;
            foreach (var kv in tags.OrderByDescending(k => k.Value)) sb.AppendLine("  " + kv.Key + " = " + kv.Value);
            sb.AppendLine();

            sb.AppendLine("== layers ==");
            var layers = new Dictionary<int, int>();
            foreach (var r in roots) foreach (var t in r.GetComponentsInChildren<Transform>(true))
                layers[t.gameObject.layer] = layers.TryGetValue(t.gameObject.layer, out var n) ? n + 1 : 1;
            foreach (var kv in layers.OrderByDescending(k => k.Value)) sb.AppendLine("  " + kv.Key + " (" + LayerMask.LayerToName(kv.Key) + ") = " + kv.Value);
            sb.AppendLine();

            sb.AppendLine("== archive group ==");
            var arch = roots.FirstOrDefault(r => r.name.StartsWith("~"));
            if (arch != null)
            {
                sb.AppendLine("  " + arch.name + " activeSelf=" + arch.activeSelf +
                              " objects=" + arch.GetComponentsInChildren<Transform>(true).Length +
                              " activeBelow=" + arch.GetComponentsInChildren<Transform>(false).Length);
                foreach (Transform c in arch.transform)
                    sb.AppendLine("     - " + c.name + " objects=" + c.GetComponentsInChildren<Transform>(true).Length);
            }

            var path = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs", "doosan_dt_verify.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[DoosanDT] verify report -> " + path);
        }

        [MenuItem("DigitalTwin/Doosan-DT/3 Resolve Script GUIDs (diagnostic)")]
        public static void ResolveGuids()
        {
            var sb = new StringBuilder("GUID resolution probe " + DateTime.Now + "\n");
            sb.AppendLine();
            sb.AppendLine("== distinct m_Script guids used by MonoBehaviours in scene ==");
            var seen = new Dictionary<string, int>();
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                var so = new SerializedObject(mb);
                var sp = so.FindProperty("m_Script");
                string guid;
                if (sp != null && sp.objectReferenceValue != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sp.objectReferenceValue, out guid, out long _);
                }
                else
                {
                    guid = "<MISSING>";
                }
                if (string.IsNullOrEmpty(guid)) guid = "<EMPTY-GUID>";
                seen[guid] = seen.TryGetValue(guid, out var n) ? n + 1 : 1;
            }
            foreach (var kv in seen.OrderByDescending(k => k.Value))
                sb.AppendLine("  " + kv.Key + " x" + kv.Value + "  path=" +
                              (kv.Key.StartsWith("<") ? "-" : AssetDatabase.GUIDToAssetPath(kv.Key)));
            var path = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs", "doosan_dt_guids.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[DoosanDT] guid probe -> " + path);
        }

        [MenuItem("DigitalTwin/Doosan-DT/9 Snapshot Each Group -> PNG")]
        public static void SnapshotGroups()
        {
            var scene = SceneManager.GetActiveScene();
            int n = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name.StartsWith("~")) continue;                 // skip the archive
                var rends = r.GetComponentsInChildren<Renderer>(false);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                if (b.size.sqrMagnitude < 0.0001f) continue;
                EVBFusionSnapshot.SnapshotBounds(b, r.name);
                n++;
            }
            Debug.Log("[DoosanDT] group snapshots written: " + n);
        }

        static void Report<T>(StringBuilder sb) where T : Behaviour
        {
            var all = UnityEngine.Object.FindObjectsOfType<T>(true);
            sb.AppendFormat("  {0,-28} active={1,-4} all={2}", typeof(T).Name, all.Count(c => c.isActiveAndEnabled), all.Length);
            sb.AppendLine();
        }

        static string Path2(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}