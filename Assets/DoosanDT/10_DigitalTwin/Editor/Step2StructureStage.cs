// Step2StructureStage.cs -- Part I + II structure work for the left-front bay.
// I.  BitExt_visual_only + Spindle_TCP -> coaxial "BitAxis" under Spindle_Camera_Unit (+ ToolBitRig).
// II. BatteryPack_PivotRoot at the pack bounding-box top-centre, Dis_1 re-parented under it.
//     Plus station hierarchy tidy. NOTHING here changes world visual positions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using DigitalTwin.Tools;

namespace DigitalTwin.EditorTools
{
    public static class Step2StructureStage
    {
        private const string ReportPath = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\step2_structure_report.json";
        private const string LeftBayName = "00_数字孪生机械臂";
        private const string VrName = "VR拆解";
        private const string PackName = "10_电池包拆解系统";

        private static readonly StringBuilder Json = new StringBuilder();

        private static void L(string s) { Debug.Log("[Step2] " + s); }

        private static string V(Vector3 v) =>
            "[" + v.x.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                  v.y.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                  v.z.ToString("0.######", CultureInfo.InvariantCulture) + "]";

        private static string F(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);

        private static string P(Transform t)
        {
            if (t == null) return "<null>";
            var st = new List<string>();
            while (t != null) { st.Add(t.name); t = t.parent; }
            var sb = new StringBuilder();
            for (int i = st.Count - 1; i >= 0; i--) { sb.Append(st[i]); if (i > 0) sb.Append('/'); }
            return sb.ToString();
        }

        private static Transform FindIn(Transform root, params string[] names)
        {
            if (root == null) return null;
            Transform cur = root;
            foreach (var n in names)
            {
                Transform next = null;
                for (int i = 0; i < cur.childCount; i++)
                    if (cur.GetChild(i).name == n) { next = cur.GetChild(i); break; }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        private static Transform SceneRoot(string n)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == n) return go.transform;
            return null;
        }

        private static bool IsUnder(Transform t, string ancestorName)
        {
            while (t != null)
            {
                if (t.name == ancestorName) return true;
                t = t.parent;
            }
            return false;
        }

        private static bool TryBoundsExcluding(Transform t, string excludedName, out Bounds b)
        {
            b = new Bounds();
            if (t == null) return false;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (IsUnder(r.transform, excludedName)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private static bool TryBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            if (t == null) return false;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private static Transform Link6
        {
            get
            {
                var t = FindIn(FindIn(FindIn(SceneRoot(LeftBayName), "4.24-Try", "h2515-1 (4)"), "base_link"));
                if (t == null) return null;
                for (int i = 1; i <= 6; i++)
                {
                    t = FindIn(t, "link_" + i);
                    if (t == null) return null;
                }
                return t;
            }
        }

        private static void MarkDirty() =>
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        // ---------------------------------------------------------------- I. tool axis

        [MenuItem("DigitalTwin/Step2/1 Rebuild Tool Axis + TCP Rig", false, 201)]
        public static void RebuildToolAxis()
        {
            Json.Append("{\n  \"stage\": \"toolAxis\",\n");
            var link6 = Link6;
            var spindle = link6 == null ? null : FindIn(link6, "Spindle_Camera_Unit");
            var flangeTcp = link6 == null ? null : FindIn(link6, "Flange_TCP");
            // They may already live under BitAxis after a previous run -- search the whole subtree.
            var oldBit = link6 == null ? null : FindDeep(link6, "BitExt_visual_only");
            var oldTcp = link6 == null ? null : FindDeep(link6, "Spindle_TCP");
            if (spindle == null || flangeTcp == null || oldBit == null || oldTcp == null)
            {
                L("ABORT: link_6 / Spindle_Camera_Unit / Flange_TCP / BitExt_visual_only / Spindle_TCP not found");
                return;
            }

            Vector3 flangePos = flangeTcp.position;

            // Bit tip = lowest point of the spindle model along world down (the assembly hangs
            // downwards from the flange). The generated extension is excluded so repeated runs
            // cannot feed back into the tip search.
            Bounds sb;
            if (!TryBoundsExcluding(spindle, "BitExt_visual_only", out sb))
            { L("ABORT: no spindle renderers"); return; }
            var core = FindChildContains(spindle, "EME38");
            Bounds cb;
            float axisX = sb.center.x, axisZ = sb.center.z;
            if (core != null && TryBounds(core, out cb)) { axisX = cb.center.x; axisZ = cb.center.z; }
            Vector3 tip = new Vector3(axisX, sb.min.y, axisZ);
            Vector3 axisDir = Vector3.down;
            float span = (sb.max.y - sb.min.y) * 1000f;

            // "BitAxis" sits on the bit axis at the model tip; local +Z = down the tool.
            var existingAxis = FindIn(spindle, "BitAxis");
            Transform axis = existingAxis;
            if (axis == null)
            {
                var go = new GameObject("BitAxis");
                go.transform.SetParent(spindle, true);
                axis = go.transform;
            }
            axis.position = tip;
            axis.rotation = Quaternion.LookRotation(axisDir, Vector3.up);   // local +Z = tool axis
            axis.localScale = Vector3.one;

            // Re-parent the visual extension + TCP, keeping the current look identical.
            oldBit.SetParent(axis, false);
            oldTcp.SetParent(axis, false);

            var rig = axis.GetComponent<ToolBitRig>();
            if (rig == null) rig = axis.gameObject.AddComponent<ToolBitRig>();
            rig.flangeTcp = flangeTcp;
            rig.bitExtension = oldBit;
            rig.spindleTcp = oldTcp;

            // Current visual: extension reaches the 287.92 mm spec from a 124.41 mm model nose.
            float modelNose = Mathf.Abs(Vector3.Dot(axis.position - flangePos, axisDir)) * 1000f;
            float keep = Mathf.Max(0f, rig.bitExtensionLengthMm);
            L("tool axis dir = " + V(axisDir) + "  model bit tip = " + V(tip) +
              "  (vertex span " + F(span) + " mm, nose-to-flange " + F(modelNose) +
              " mm, extension kept " + F(keep) + " mm)");
            rig.Apply();
            EditorUtility.SetDirty(rig);
            MarkDirty();

            Json.Append("  \"bitAxisWorld\": ").Append(V(axis.position)).Append(",\n");
            Json.Append("  \"bitAxisRotation\": ").Append(V(axis.eulerAngles)).Append(",\n");
            Json.Append("  \"modelTipToFlangeMm\": ").Append(F(rig.FlangeToTcpDistanceMm - keep)).Append(",\n");
            Json.Append("  \"extensionMm\": ").Append(F(keep)).Append(",\n");
            Json.Append("  \"flangeToTcpMm\": ").Append(F(rig.FlangeToTcpDistanceMm)).Append(",\n");
            Json.Append("  \"flangeToTcpVector\": ").Append(V(rig.FlangeToTcpWorld)).Append(",\n");
            Json.Append("  \"spindleTcpWorld\": ").Append(V(oldTcp.position)).Append('\n').Append("},\n");
            L("Flange_TCP -> Spindle_TCP = " + V(rig.FlangeToTcpWorld) + "  (|" + F(rig.FlangeToTcpDistanceMm) + "| mm)");
        }

        private static Transform FindChildContains(Transform root, string needle)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name.IndexOf(needle, StringComparison.Ordinal) >= 0) return c;
            }
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // ---------------------------------------------------------------- II. pack pivot

        [MenuItem("DigitalTwin/Step2/2 Rebuild BatteryPack Pivot", false, 202)]
        public static void RebuildPackPivot()
        {
            var vr = FindIn(SceneRoot(LeftBayName), PackName, VrName);
            var dis = vr == null ? null : FindIn(vr, "Dis_1");
            if (vr == null || dis == null) { L("ABORT: VR拆解 / Dis_1 not found"); return; }

            Bounds b;
            if (!TryBounds(dis, out b)) { L("ABORT: Dis_1 has no renderer bounds"); return; }

            Vector3 before = dis.position;
            var layersBefore = CollectLayerWorldPositions(dis);

            // Pivot = top-cover centre of the pack bounding box.
            Vector3 pivotWorld = new Vector3(b.center.x, b.max.y, b.center.z);
            var pivot = FindIn(vr, "BatteryPack_PivotRoot") ?? vr.Find("BatteryPack_PivotRoot");
            Transform pr;
            if (pivot != null) pr = pivot;
            else
            {
                var go = new GameObject("BatteryPack_PivotRoot");
                go.transform.SetParent(vr, false);
                pr = go.transform;
            }
            pr.position = pivotWorld;
            pr.rotation = Quaternion.identity;
            pr.localScale = Vector3.one;

            if (dis.parent != pr) dis.SetParent(pr, true);   // keep world transform
            if ((dis.position - before).sqrMagnitude > 1e-12f)
            {
                dis.position = before;   // hard guard: pivot re-parent must not move the pack
                L("point guard: Dis_1 world restored after re-parent");
            }

            var layersAfter = CollectLayerWorldPositions(dis);
            bool layersStable = layersBefore.Count == layersAfter.Count;
            if (layersStable)
            {
                foreach (var kv in layersBefore)
                {
                    Vector3 after;
                    if (!layersAfter.TryGetValue(kv.Key, out after) ||
                        (kv.Value - after).sqrMagnitude > 1e-8f)
                    {
                        layersStable = false;
                        break;
                    }
                }
            }

            var ds = dis.GetComponent<DisassemblySystem>();
            int layerCount = ds != null ? ds.layers.Count : -1;
            bool refsOk = ds != null;
            if (ds != null)
            {
                for (int i = 0; i < ds.layers.Count; i++)
                {
                    var l = ds.layers[i];
                    if (l == null) { refsOk = false; break; }
                    if (!l.transform.IsChildOf(dis)) { refsOk = false; break; }
                }
            }

            EditorUtility.SetDirty(pr);
            MarkDirty();

            string[] dirs = { };
            var lay1 = FindIn(dis, "Lay1");
            var lg = lay1 == null ? null : lay1.GetComponent<LayerGroupConfig>();
            string sepDir = lg != null ? V(lg.separationDirection) : "n/a";

            Json.Append("  \"packPivot\": {\n");
            Json.Append("    \"world\": ").Append(V(pivotWorld)).Append(",\n");
            Json.Append("    \"packBoundsCenter\": ").Append(V(b.center)).Append(",\n");
            Json.Append("    \"packBoundsMax\": ").Append(V(b.max)).Append(",\n");
            Json.Append("    \"dis1WorldBefore\": ").Append(V(before)).Append(",\n");
            Json.Append("    \"dis1WorldAfter\": ").Append(V(dis.position)).Append(",\n");
            Json.Append("    \"layersStable\": ").Append(layersStable ? "true" : "false").Append(",\n");
            Json.Append("    \"disassemblySystemRefsOk\": ").Append(refsOk ? "true" : "false").Append(",\n");
            Json.Append("    \"layerCount\": ").Append(layerCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            Json.Append("    \"lay1SeparationDirection\": \"").Append(sepDir).Append("\"\n");
            Json.Append("  },\n");
            L("point Pivot placed at " + V(pivotWorld) + " layersStable=" + layersStable + " refsOk=" + refsOk + " layers=" + layerCount);
        }

        private static Dictionary<string, Vector3> CollectLayerWorldPositions(Transform dis)
        {
            var map = new Dictionary<string, Vector3>();
            for (int i = 0; i < dis.childCount; i++)
            {
                var c = dis.GetChild(i);
                map[c.name] = c.position;
            }
            return map;
        }

        // ---------------------------------------------------------------- III. tidy

        [MenuItem("DigitalTwin/Step2/3 Tidy Station Hierarchy", false, 203)]
        public static void TidyStation()
        {
            var bay = SceneRoot(LeftBayName);
            if (bay == null) { L("ABORT: bay root missing"); return; }
            var baseLink = FindIn(bay, "4.24-Try", "h2515-1 (4)", "base_link");
            if (baseLink == null) { L("ABORT: base_link missing"); return; }

            var station = bay.Find("LeftBay_Station");
            Transform st;
            if (station != null) st = station;
            else
            {
                var go = new GameObject("LeftBay_Station");
                go.transform.SetParent(bay, false);
                st = go.transform;
            }
            st.position = new Vector3(baseLink.position.x, 0f, baseLink.position.z);
            st.rotation = Quaternion.identity;
            st.localScale = Vector3.one;

            int moved = 0;
            foreach (var name in new[] { "MobileBase_H2515", "MountRiser_136mm" })
            {
                var t = bay.Find(name);
                if (t == null) continue;
                Vector3 pw = t.position; Quaternion rw = t.rotation; Vector3 sw = t.lossyScale;
                t.SetParent(st, true);
                // Hard guard: re-assert the recorded world transform so the visual never moves.
                t.position = pw; t.rotation = rw;
                moved++;
                L("moved " + name + " under LeftBay_Station (world kept " + V(pw) + ")");
            }

            // Report the resulting hierarchy for the record.
            var sb = new StringBuilder();
            Dump(bay, 0, sb);
            var outPath = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\step2_hierarchy.txt";
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            L("hierarchy dumped -> " + outPath + " (moved=" + moved + ")");

            Json.Append("  \"station\": {\"world\": ").Append(V(st.position))
                .Append(", \"moved\": ").Append(moved.ToString(CultureInfo.InvariantCulture)).Append("},\n");
            MarkDirty();
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 6) return;
            sb.Append(new string(' ', depth * 2)).Append(t.name)
              .Append("  w=").Append(V(t.position)).Append('\n');
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                // stop early on huge model subtrees
                if (c.name.StartsWith("link_") || c.name.StartsWith("Spindle") || c.name.StartsWith("Dis_1"))
                {
                    sb.Append(new string(' ', (depth + 1) * 2)).Append(c.name)
                      .Append("  w=").Append(V(c.position)).Append("  [subtree]\n");
                    continue;
                }
                Dump(c, depth + 1, sb);
            }
        }

        [MenuItem("DigitalTwin/Step2/9 Write Structure Report", false, 209)]
        public static void WriteReport()
        {
            Json.Append("  \"generatedUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\"\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, Json.ToString(), new UTF8Encoding(false));
            L("structure report -> " + ReportPath);
        }

        // ---------------------------------------------------------------- IV. offline interlock run

        [MenuItem("DigitalTwin/Step2/4 Run Offline Interlock Verify (Play)", false, 204)]
        public static void RunOfflineVerify()
        {
            if (EditorApplication.isPlaying)
            {
                L("already in play mode; abort");
                return;
            }
            var vr = FindIn(SceneRoot(LeftBayName), PackName, VrName);
            if (vr == null) { L("ABORT: VR拆解 missing"); return; }

            var bridge = UnityEngine.Object.FindObjectOfType<PermissionStateBridge>();
            if (bridge == null)
            {
                var go = new GameObject("PermissionStateBridge");
                go.transform.SetParent(vr, false);
                go.AddComponent<PermissionStateBridge>();
                EditorUtility.SetDirty(go);
                MarkDirty();
                L("PermissionStateBridge created under " + P(vr));
            }
            else L("PermissionStateBridge already present on " + P(bridge.transform));

            Directory.CreateDirectory(Path.GetDirectoryName(OfflineInterlockVerify.FlagPath));
            File.WriteAllText(OfflineInterlockVerify.FlagPath, "run", new UTF8Encoding(false));
            L("flag written; entering play mode");
            EditorApplication.isPlaying = true;
        }
    }
}
