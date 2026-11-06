// Read-only UI wiring audit for Doosan-DT (EDIT MODE ONLY - no Play, no ROS, no recording).
// Menu: DigitalTwin/UI/3 Audit UI Wiring (read-only) -> <project>/Logs/ui_wiring_audit.txt
//
// Why: "missing scripts" and "UnityEvent broken links" were already clean in the previous
// batch, yet the EVB disassembly / UI chain still does not drive anything at runtime. The
// real gap is *serialized component field references* being null (SmartRaycaster highlight
// material, video panel, timer text, ...). This audit lists exactly those, before and after
// the rewiring step, so the change is provable instead of asserted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using DigitalTwin;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIWiringAudit
    {
        private const string LogPath = "Logs/ui_wiring_audit.txt";

        [MenuItem("DigitalTwin/UI/3 Audit UI Wiring (read-only)")]
        public static void Audit()
        {
            var sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine("Twin UI wiring audit  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("scene=" + scene.path + "  dirty=" + scene.isDirty);
            sb.AppendLine();

            GameObject[] roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            sb.AppendLine("roots=" + roots.Length + "  gameObjects=" + all.Count);

            WriteCanvasSection(sb, all);
            WriteLegacyCanvasChildrenSection(sb);
            WriteEventSystemSection(sb, all);
            WriteMissingScriptSection(sb, all);
            WriteUnityEventSection(sb, all);
            WriteFieldNullSection(sb, all);
            WriteTmpFontSection(sb, all);
            WriteRuntimeToggleSection(sb, all);

            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[UI Audit] written to " + Path.GetFullPath(LogPath) + " (" + all.Count + " objects)");
        }

        // ---------------------------------------------------------------- sections

        private static void WriteCanvasSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== canvases ==");
            var canvases = all.Select(g => g.GetComponent<Canvas>()).Where(c => c != null)
                              .OrderBy(c => PathOf(c.transform)).ToList();
            sb.AppendLine("  total=" + canvases.Count);
            foreach (var cam in all.Select(g => g.GetComponent<Camera>()).Where(c => c != null && c.isActiveAndEnabled))
            {
                sb.AppendFormat("  camera {0} cullingMask=0x{1:X8} depth={2}", PathOf(cam.transform), cam.cullingMask, cam.depth);
                sb.AppendLine();
            }
            foreach (var c in canvases)
            {
                var gr = c.GetComponent<GraphicRaycaster>();
                var td = c.GetComponent<TrackedDeviceGraphicRaycaster>();
                var scaler = c.GetComponent<CanvasScaler>();
                sb.AppendFormat("  {0} | enabled={1} activeSelf={2} inHierarchy={3} layer={4}({5}) | mode={6} order={7} cam={8} | scaler={9} graphicRaycaster={10} trackedDeviceRaycaster={11}{12}",
                    PathOf(c.transform), c.enabled, c.gameObject.activeSelf, c.isActiveAndEnabled,
                    c.gameObject.layer, LayerMask.LayerToName(c.gameObject.layer),
                    c.renderMode, c.sortingOrder, c.worldCamera == null ? "null" : c.worldCamera.name,
                    scaler != null, gr != null && gr.enabled, td != null && td.enabled,
                    (gr != null && td != null && gr.enabled && td.enabled) ? "  <-- BOTH RAYCASTERS ON" : "");
                sb.AppendLine();
                var rt = c.transform as RectTransform;
                if (rt != null)
                {
                    sb.AppendFormat("      size={0} anchored={1} localPos={2} localScale={3} rot={4}",
                        rt.sizeDelta, rt.anchoredPosition, rt.localPosition, rt.localScale, rt.localRotation.eulerAngles);
                    sb.AppendLine();
                }
            }
        }

        private static void WriteLegacyCanvasChildrenSection(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("== legacy canvas contents (why a canvas can be active but empty) ==");
            string[] paths =
            {
                "30_界面与交互/Canvas/Canvas_1 P",
                "30_界面与交互/Canvas/Canvas_1_V",
                "30_界面与交互/Canvas/Canvas_I",
                "30_界面与交互/Canvas/Canvas_I软著用标题",
                "30_界面与交互/Canvas/User_ui",
                "30_界面与交互/Canvas/登录界面",
            };

            foreach (string path in paths)
            {
                GameObject go = TwinSceneLookup.FindObject(path);
                if (go == null)
                {
                    sb.AppendLine("  " + path + " : <not found>");
                    continue;
                }

                var rt = go.transform as RectTransform;
                sb.AppendFormat("  {0} activeSelf={1} childCount={2}{3}",
                    path, go.activeSelf, go.transform.childCount,
                    rt == null ? string.Empty : " scale=(" + rt.localScale.x.ToString("0.####") + ", " + rt.localScale.y.ToString("0.####") + ", " + rt.localScale.z.ToString("0.####") + ") size=" + rt.sizeDelta);
                sb.AppendLine();

                int shown = 0;
                foreach (Transform child in go.transform)
                {
                    if (shown++ >= 12)
                    {
                        sb.AppendLine("      ... (" + go.transform.childCount + " children)");
                        break;
                    }

                    sb.AppendFormat("      - {0} active={1} size={2} scale={3}",
                        child.name, child.gameObject.activeSelf,
                        (child as RectTransform) == null ? "-" : ((RectTransform)child).sizeDelta.ToString(),
                        child.localScale.ToString("0.###"));
                    sb.AppendLine();
                }
            }
        }

        private static void WriteEventSystemSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== event systems / input modules ==");
            int i = 0;
            foreach (var es in all.Select(g => g.GetComponent<EventSystem>()).Where(e => e != null))
            {
                i++;
                sb.AppendFormat("  [{0}] {1} activeSelf={2} inHierarchy={3}", i, PathOf(es.transform), es.gameObject.activeSelf, es.isActiveAndEnabled);
                sb.AppendLine();
                foreach (var mb in es.GetComponents<BaseInputModule>())
                {
                    sb.AppendFormat("        module={0} enabled={1}", mb.GetType().FullName, mb.enabled);
                    sb.AppendLine();
                }
            }

            int xim = all.Count(g => g.GetComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>() != null);
            sb.AppendLine("  XRInteractionManager instances=" + xim);
        }

        private static void WriteMissingScriptSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== missing scripts ==");
            int count = 0;
            foreach (var go in all)
            {
                var comps = go.GetComponents<Component>();
                for (int s = 0; s < comps.Length; s++)
                {
                    if (comps[s] != null)
                    {
                        continue;
                    }

                    count++;
                    sb.AppendFormat("  MISSING {0} slot={1}", PathOf(go.transform), s);
                    sb.AppendLine();
                }
            }

            sb.AppendLine("  missingScriptInstances=" + count);
        }

        private static void WriteUnityEventSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== persistent UnityEvent calls (all components) ==");
            int total = 0;
            int broken = 0;
            int buttons = all.Sum(g => g.GetComponents<Button>().Length);
            int toggles = all.Sum(g => g.GetComponents<Toggle>().Length);
            sb.AppendLine("  buttonComponents=" + buttons + "  toggleComponents=" + toggles);
            foreach (var go in all)
            {
                foreach (var mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null)
                    {
                        continue;
                    }

                    total += DumpPersistentCalls(sb, mb, PathOf(go.transform), ref broken);
                }
            }

            sb.AppendLine("  totalCalls=" + total + "  brokenCalls=" + broken);
        }

        // Iterates every serialized property of the component and dumps each persistent-call array
        // (m_Calls). This is naming-agnostic: uGUI exposes m_OnClick / m_OnValueChanged / ... and the
        // SerializedProperty path differs between package versions, so a hard-coded path loses calls.
        private static int DumpPersistentCalls(StringBuilder sb, MonoBehaviour mb, string ownerPath, ref int broken)
        {
            var so = new SerializedObject(mb);
            int n = 0;
            SerializedProperty prop = so.GetIterator();
            // Next(true): walk hidden/child properties too. NextVisible would skip the UnityEvent
            // internals (they are not shown in the Inspector), which is why a first attempt counted 0.
            bool enterChildren = true;
            while (prop.Next(enterChildren))
            {
                enterChildren = true;
                if (prop.name != "m_Calls" || !prop.isArray)
                {
                    continue;
                }

                string eventPath = prop.propertyPath.Replace(".m_PersistentCalls.m_Calls", string.Empty);
                for (int i = 0; i < prop.arraySize; i++)
                {
                    SerializedProperty call = prop.GetArrayElementAtIndex(i);
                    n++;
                    var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                    string method = call.FindPropertyRelative("m_MethodName").stringValue;
                    int mode = call.FindPropertyRelative("m_Mode").enumValueIndex;
                    bool ok = target != null && !string.IsNullOrEmpty(method);
                    if (!ok)
                    {
                        broken++;
                    }

                    Transform targetTransform = null;
                    if (target is Component comp)
                    {
                        targetTransform = comp.transform;
                    }
                    else if (target is GameObject goTarget)
                    {
                        targetTransform = goTarget.transform;
                    }

                    sb.AppendFormat("  {0} :: {1}({2}) -> {3}.{4} (mode={5}) {6}",
                        ownerPath, mb.GetType().Name, eventPath,
                        target == null ? "<NULL>" : PathOf(targetTransform),
                        method, mode, ok ? string.Empty : "<-- BROKEN");
                    sb.AppendLine();
                }
            }

            return n;
        }

        private static void WriteFieldNullSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== serialized object-reference fields (null report) ==");
            sb.AppendLine("  scope: components in the UI / disassembly / telemetry chain; count = null object references");
            AppendFields<SmartRaycaster>(sb, all);
            AppendFields<DisassemblySystem>(sb, all);
            AppendFields<LayerGroupConfig>(sb, all);
            AppendFields<DisassemblyPart>(sb, all);
            AppendFields<PerformanceProfiler>(sb, all);
            AppendFields<ExplosionControllerXR>(sb, all);
            AppendFields<VideoController>(sb, all);
            AppendFields<TimerController>(sb, all);
            AppendFields<TC>(sb, all);
            AppendFields<TwinUIController>(sb, all);
            AppendFields<Ros2ExperimentPanel>(sb, all);
            AppendFields<DigitalTwinRuntime>(sb, all);
            AppendFields<TwinRecorder>(sb, all);
        }

        private static void AppendFields<T>(StringBuilder sb, List<GameObject> all) where T : Component
        {
            var found = all.Select(g => g.GetComponent<T>()).Where(c => c != null).ToList();
            if (found.Count == 0)
            {
                sb.AppendLine("  " + typeof(T).Name + ": (none in scene)");
                return;
            }

            foreach (var comp in found)
            {
                var so = new SerializedObject(comp);
                var nulls = new List<string>();
                SerializedProperty prop = so.GetIterator();
                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (prop.objectReferenceValue == null)
                    {
                        // instanceID != 0 while the resolved value is null == the target asset/object is gone
                        nulls.Add(prop.displayName + (prop.objectReferenceInstanceIDValue != 0 ? "(MISSING)" : string.Empty));
                    }
                }

                sb.AppendFormat("  {0} [{1}] nullFields({2}): {3}",
                    typeof(T).Name, PathOf(comp.transform), nulls.Count,
                    nulls.Count == 0 ? "-" : string.Join(", ", nulls.Take(40)));
                sb.AppendLine();
            }
        }

        private static void WriteTmpFontSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== TMP text / font ==");
            int total = 0;
            int cjkWithoutFont = 0;
            foreach (var go in all)
            {
                foreach (var t in go.GetComponents<TMP_Text>())
                {
                    total++;
                    string text = t.text ?? string.Empty;
                    bool cjk = ContainsCjk(text);
                    string fontName = t.font == null ? "<NULL FONT>" : t.font.name;
                    bool suspicious = cjk && t.font != null && !t.font.name.StartsWith("NotoSansSC", StringComparison.Ordinal);
                    if (cjk && t.font == null)
                    {
                        cjkWithoutFont++;
                    }

                    if (cjk || suspicious)
                    {
                        sb.AppendFormat("  {0} | font={1} cjk={2}{3} | text=\"{4}\"",
                            PathOf(t.transform), fontName, cjk,
                            suspicious ? "  <-- CJK ON NON-SC FONT" : string.Empty,
                            Shorten(text));
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("  tmpTextCount=" + total + "  cjkWithoutFont=" + cjkWithoutFont);
            var def = TMP_Settings.defaultFontAsset;
            sb.AppendLine("  TMP defaultFont=" + (def == null ? "<NULL>" : def.name));
        }

        private static void WriteRuntimeToggleSection(StringBuilder sb, List<GameObject> all)
        {
            sb.AppendLine();
            sb.AppendLine("== runtime UI toggles (why the HUD may not show) ==");
            var runtime = all.Select(g => g.GetComponent<DigitalTwinRuntime>()).FirstOrDefault(c => c != null);
            if (runtime == null)
            {
                sb.AppendLine("  DigitalTwinRuntime: (none)");
            }
            else
            {
                var so = new SerializedObject(runtime);
                sb.AppendFormat("  DigitalTwinRuntime@{0} enableRuntimeUi={1} uiRefreshRateHz={2}",
                    PathOf(runtime.transform),
                    so.FindProperty("enableRuntimeUi")?.boolValue,
                    so.FindProperty("uiRefreshRateHz")?.floatValue);
                sb.AppendLine();
                var ui = so.FindProperty("uiController")?.objectReferenceValue;
                sb.AppendFormat("  uiController={0}", ui == null ? "<NULL>" : PathOf(((TwinUIController)ui).transform) + " activeSelf=" + ((TwinUIController)ui).gameObject.activeSelf);
                sb.AppendLine();
                var profile = so.FindProperty("profile")?.objectReferenceValue;
                if (profile != null)
                {
                    var pso = new SerializedObject(profile);
                    sb.AppendFormat("  profile={0} enableRuntimeUI={1}", profile.name, pso.FindProperty("enableRuntimeUI")?.boolValue);
                    sb.AppendLine();
                }
            }

            var perf = all.Select(g => g.GetComponent<PerformanceProfiler>()).Where(c => c != null).ToList();
            foreach (var p in perf)
            {
                sb.AppendFormat("  PerformanceProfiler@{0} activeSelf={1} (its Start() creates a CSV unconditionally)", PathOf(p.transform), p.gameObject.activeSelf);
                sb.AppendLine();
            }

            var panels = all.Select(g => g.GetComponent<Ros2ExperimentPanel>()).Where(c => c != null).ToList();
            foreach (var p in panels)
            {
                var so = new SerializedObject(p);
                sb.AppendLine("  Ros2ExperimentPanel@" + PathOf(p.transform) + " telemetry channel flags (BuildTelemetryChannels source):");
                SerializedProperty it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.propertyType == SerializedPropertyType.Boolean &&
                        (it.name.Contains("joint", StringComparison.OrdinalIgnoreCase) ||
                         it.name.Contains("force", StringComparison.OrdinalIgnoreCase) ||
                         it.name.Contains("torque", StringComparison.OrdinalIgnoreCase) ||
                         it.name.Contains("tcp", StringComparison.OrdinalIgnoreCase) ||
                         it.name.Contains("wrench", StringComparison.OrdinalIgnoreCase) ||
                         it.name.Contains("robot", StringComparison.OrdinalIgnoreCase)))
                    {
                        sb.AppendLine("      " + it.name + "=" + it.boolValue);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static string PathOf(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            var stack = new List<string>();
            while (t != null)
            {
                stack.Add(t.name);
                t = t.parent;
            }

            stack.Reverse();
            return string.Join("/", stack);
        }

        private static bool ContainsCjk(string s)
        {
            foreach (char c in s)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Shorten(string s)
        {
            s = (s ?? string.Empty).Replace("\n", "\\n").Replace("\r", string.Empty);
            return s.Length <= 40 ? s : s.Substring(0, 40) + "...";
        }
    }
}