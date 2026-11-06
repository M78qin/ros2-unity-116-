// Editor-only builder for the merged "Doosan-DT" scene.
// Menu: DigitalTwin/Doosan-DT/1 Build Merged Scene
//
// What it does (all in one editor session, so every serialized cross-reference survives):
//   1. Creates Assets/Scenes/Doosan-DT.unity (empty scene).
//   2. Opens SampleScene + MainScene ADDITIVELY and moves their root objects into Doosan-DT
//      via SceneManager.MoveGameObjectToScene (objects keep their fileIDs => references intact).
//   3. Re-parents roots/children into functional groups.
//   4. Deactivates duplicates (second camera / second EventSystem / second directional light)
//      and parks every backup subtree under "~_归档停用" which is itself inactive.
//   5. Saves the scene, registers it in Build Settings, writes an audit report to <project>/Logs.
//
// Idempotent: rebuilding deletes the previous Doosan-DT.unity first.
// It NEVER touches SampleScene.unity / MainScene.unity / EVB_Chapter4.unity on disk.
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

namespace DigitalTwin.EditorTools
{
    public static class DoosanDTSceneBuilder
    {
        const string TargetScenePath = "Assets/Scenes/Doosan-DT.unity";
        const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        const string EvbScenePath = "Assets/Scenes/MainScene.unity";

        // group keys
        const string G00 = "00_数字孪生机械臂";
        const string G10 = "10_电池包拆解系统";
        const string G20 = "20_漫游与训练";
        const string G30 = "30_界面与交互";
        const string G40 = "40_环境与灯光";
        const string G50 = "50_玩家与相机";
        const string G90 = "90_全局设置";
        const string GBK = "~_归档停用";

        static readonly string[] GroupOrder = { G00, G10, G20, G30, G40, G50, G90, GBK };

        // (source scene path, root object name, target group)
        static readonly (string scene, string name, string group)[] RootMap =
        {
            // ---- digital twin (SampleScene) ----
            (SampleScenePath, "4.24-Try",                 G00),
            (SampleScenePath, "Ros2Experimen",            G00),
            (SampleScenePath, "DataLogging_System",       G00),
            (SampleScenePath, "Planning_Control_System",  GBK),
            (SampleScenePath, "Main Camera",              G50),
            (SampleScenePath, "Directional Light",        G40),
            (SampleScenePath, "EventSystem",              G30),
            (SampleScenePath, "UI_System",                GBK),
            (SampleScenePath, "H2515_DigitalTwinRuntimeCanvas", GBK),
            (SampleScenePath, "4.24-Try (1)",             GBK),
            (SampleScenePath, "4.24-Try _bk519",          GBK),
            (SampleScenePath, "~",                        GBK),
            (SampleScenePath, "dartstudio_E",             GBK),

            // ---- EVB main scene roots ----
            (EvbScenePath, "player",    G50),
            (EvbScenePath, "VR拆解",     G10),
        };

        // children of the EVB "其他" root get split by function
        static readonly (string name, string group)[] EvbOtherChildrenMap =
        {
            ("FunctionModule",        G20),
            ("爆炸用 (1)",             G20),
            ("Canvas",                G30),
            ("EventSystem",           G30),
            ("XR Interaction Manager",G30),
            ("EVB_DS ",               G40),
            ("Directional Light",     G40),
            ("Global Volume",         G90),
            ("GameObject",            GBK),
            ("测试2_自动",             GBK),
            ("VR拆解_传统设置_Test",    GBK),
            ("VR拆解_多零件",           GBK),
        };

        [MenuItem("DigitalTwin/Doosan-DT/1 Build Merged Scene", false, 1)]
        public static void Build()
        {
            var log = new StringBuilder();
            void L(string s) { log.AppendLine(s); Debug.Log("[DoosanDT] " + s); }

            try
            {
                L("=== Build Doosan-DT : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

                // 0) make sure nothing is lost: save whatever is open
                EditorSceneManager.SaveOpenScenes();

                // 1) fresh target scene (Single mode unloads the currently open scene)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) != null)
                {
                    AssetDatabase.DeleteAsset(TargetScenePath);
                    L("deleted previous " + TargetScenePath);
                }
                var merged = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(merged, TargetScenePath))
                {
                    L("ERROR: could not save new scene");
                    Flush(log);
                    return;
                }
                merged = EditorSceneManager.GetSceneByPath(TargetScenePath);
                L("created " + TargetScenePath);

                // 2) groups
                var groups = new Dictionary<string, GameObject>();
                foreach (var g in GroupOrder)
                {
                    var go = new GameObject(g);
                    SceneManager.MoveGameObjectToScene(go, merged);
                    groups[g] = go;
                }
                L("groups created: " + string.Join(", ", GroupOrder));

                // 3) pull roots from both scenes
                var movedByScene = new Dictionary<string, List<string>>();
                foreach (var scenePath in new[] { SampleScenePath, EvbScenePath })
                {
                    var src = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    if (!src.IsValid())
                    {
                        L("ERROR: could not open " + scenePath);
                        continue;
                    }
                    var roots = src.GetRootGameObjects();
                    L("--- " + Path.GetFileName(scenePath) + " roots=" + roots.Length);
                    var names = new List<string>();
                    foreach (var root in roots)
                    {
                        var raw = root.name;
                        var key = raw.TrimEnd();
                        names.Add(raw + "(children=" + root.transform.childCount + ")");

                        if (Path.GetFileName(scenePath) == "MainScene.unity" && key == "其他")
                        {
                            // split 其他 by child
                            var kids = new List<Transform>();
                            foreach (Transform c in root.transform) kids.Add(c);
                            foreach (var child in kids)
                            {
                                var ck = child.name.TrimEnd();
                                string grp = null;
                                foreach (var m in EvbOtherChildrenMap)
                                    if (m.name.TrimEnd() == ck) { grp = m.group; break; }
                                grp = grp ?? GBK;
                                child.SetParent(groups[grp].transform, true);
                                L("    其他/" + child.name + " -> " + grp);
                            }
                            SceneManager.MoveGameObjectToScene(root, merged);
                            root.transform.SetParent(groups[GBK].transform, true);
                            L("    其他 (emptied) -> " + GBK);
                            continue;
                        }

                        string target = null;
                        foreach (var m in RootMap)
                            if (m.scene == scenePath && m.name.TrimEnd() == key) { target = m.group; break; }
                        target = target ?? GBK;
                        SceneManager.MoveGameObjectToScene(root, merged);
                        root.transform.SetParent(groups[target].transform, true);
                        L("    " + raw + " -> " + target);
                    }
                    movedByScene[scenePath] = names;
                    EditorSceneManager.CloseScene(src, true);
                }

                // 4) duplicate handling
                // keep the EVB player camera; deactivate the twin camera (no runtime code depends on it)
                var twinCam = groups[G50].GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(c => c.gameObject.name == "Main Camera" && c.gameObject.transform.parent == groups[G50].transform);
                if (twinCam != null) { twinCam.gameObject.SetActive(false); L("deactivated duplicate twin Main Camera"); }

                // keep the EVB EventSystem; deactivate the twin one
                var twinEs = groups[G30].GetComponentsInChildren<EventSystem>(true)
                    .FirstOrDefault(e => e.gameObject.transform.parent == groups[G30].transform);
                if (twinEs != null) { twinEs.gameObject.name = "EventSystem (DT_停用)"; twinEs.gameObject.SetActive(false); L("deactivated duplicate twin EventSystem"); }

                // keep EVB lighting; deactivate the twin directional light
                var twinLight = groups[G40].GetComponentsInChildren<Light>(true)
                    .FirstOrDefault(l => l.gameObject.transform.parent == groups[G40].transform && l.type == LightType.Directional);
                if (twinLight != null) { twinLight.gameObject.SetActive(false); L("deactivated duplicate twin Directional Light"); }

                groups[GBK].SetActive(false);
                L("group " + GBK + " set inactive");

                // 5) save + build settings
                EditorSceneManager.MarkSceneDirty(merged);
                EditorSceneManager.SaveScene(merged, TargetScenePath);

                var list = EditorBuildSettings.scenes.ToList();
                if (!list.Any(s => s.path == TargetScenePath))
                {
                    list.Add(new EditorBuildSettingsScene(TargetScenePath, true));
                    EditorBuildSettings.scenes = list.ToArray();
                    L("added to Build Settings");
                }

                // 6) report
                int total = merged.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Transform>(true).Length);
                L("scene objects (Transform count) = " + total);
                L("");
                L("=== hierarchy (depth <= 3) ===");
                foreach (var r in merged.GetRootGameObjects())
                {
                    L(Describe(r.transform, 0, 3));
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                L("DONE");
                Flush(log);
            }
            catch (Exception e)
            {
                L("EXCEPTION: " + e);
                Flush(log);
                throw;
            }
        }

        static string Describe(Transform t, int depth, int maxDepth)
        {
            var sb = new StringBuilder();
            sb.Append(new string(' ', depth * 4)).Append(t.gameObject.name).Append("  [").Append(t.gameObject.activeSelf ? "A" : "-").Append(']');
            int n = t.GetComponents<Component>().Count(c => c != null && !(c is Transform));
            sb.Append("  components=").Append(n);
            sb.Append(" pos=").Append(t.position.ToString("F2"));
            sb.AppendLine();
            if (depth < maxDepth)
                foreach (Transform c in t)
                    sb.Append(Describe(c, depth + 1, maxDepth));
            return sb.ToString();
        }

        static void Flush(StringBuilder log)
        {
            try
            {
                var root = Directory.GetParent(Application.dataPath).FullName;
                var path = Path.Combine(root, "Logs", "doosan_dt_build.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[DoosanDT] could not write log: " + e.Message); }
        }
    }
}