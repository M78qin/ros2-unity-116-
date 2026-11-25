// LeftBayStep1Apply.cs -- Step 1 apply for the LEFT-FRONT bay only.
// Touches: 00_数字孪生机械臂 (arm + pack), 30_界面与交互/UI_二期面板 (refresh rate only),
// Assets/DoosanDT/Models/RobotBase (new), 主轴与相机.fbx import settings.
// NEVER modifies or hides 20_漫游与训练 or 40_环境与灯光 (the display bay is read-only input).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class LeftBayStep1Apply
    {
        private const string ReportPath = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\step1_apply_report.json";
        private const string Root = @"E:\new_2026-\unity\project\Unity_Ros_Try1\";
        private const string BaseDirAsset = "Assets/DoosanDT/Models/RobotBase";
        private const string BaseFbxAsset = BaseDirAsset + "/Doosan_H2515_MobileBase.fbx";
        private const string DesktopPack = @"C:\Users\Administrator\Desktop\Doosan_H2515_MobileBase_UnityPack-v2";
        private const string SpindleFbxAsset = "Assets/DoosanDT/20_BatteryDisassembly/Assets/Models/主轴与相机.fbx";

        private const string LeftBayName = "00_数字孪生机械臂";
        private const string PackName = "10_电池包拆解系统";
        private const string VrName = "VR拆解";
        private const string TableName = "放置台";
        private const string SpindleName = "主轴与相机";
        private const string DisplayBayName = "40_环境与灯光";
        private const string RoamBayName = "20_漫游与训练";

        // Real tool length: flange -> S_C_Tcp
        private const float ToolLengthM = 0.28792f;
        // Desired hover gap above the screw head
        private const float HoverGapM = 0.0095f;
        // Mounting plane of the mobile base (from the vendor README)
        private const float BaseMountPlaneY = 0.148f;

        private static readonly StringBuilder Log = new StringBuilder();
        private static readonly StringBuilder Json = new StringBuilder();

        private static void L(string s)
        {
            Log.AppendLine(s);
            Debug.Log("[Step1] " + s);
        }

        private static void J(int indent, string key, string value, bool last = false)
        {
            Json.Append(' ', indent * 2).Append('"').Append(key).Append("\": ").Append(value);
            Json.Append(last ? "\n" : ",\n");
        }

        private static string V(Vector3 v)
        {
            return "[" + v.x.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        v.y.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        v.z.ToString("0.######", CultureInfo.InvariantCulture) + "]";
        }

        private static string F(float f)
        {
            return f.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string JS(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";
        }

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
            foreach (string n in names)
            {
                Transform next = null;
                for (int i = 0; i < cur.childCount; i++)
                    if (cur.GetChild(i).name == n) { next = cur.GetChild(i); break; }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        private static Transform SceneRoot(string name)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go.transform;
            return null;
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

        private static Transform LeftBay { get { return SceneRoot(LeftBayName); } }
        private static Transform Arm { get { return FindIn(LeftBay, "4.24-Try", "h2515-1 (4)"); } }
        private static Transform BaseLink { get { return FindIn(Arm, "base_link"); } }
        private static Transform Pack { get { return FindIn(LeftBay, PackName, VrName); } }
        private static Transform Table { get { return FindIn(Pack, TableName); } }

        private static RobotModelController Model
        {
            get { return BaseLink == null ? null : BaseLink.GetComponent<RobotModelController>(); }
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

        private static Material MakeUrpMaterial(string assetPath, Texture2D tex, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(mat, assetPath);
            return mat;
        }

        // ------------------------------------------------------------------ main

        [MenuItem("DigitalTwin/Step1/1 Apply Left Bay Setup", false, 102)]
        public static void Apply()
        {
            var t0 = DateTime.UtcNow;
            Json.Append("{\n");
            J(1, "stage", "\"apply\"");
            J(1, "generatedUtc", JS(t0.ToString("o", CultureInfo.InvariantCulture)));

            // guard: the display bay must stay untouched
            var roamBefore = SnapshotRoot(RoamBayName);
            var displayBefore = SnapshotRoot(DisplayBayName);

            var arm = Arm;
            var baseLink = BaseLink;
            var model = Model;
            var vr = Pack;
            var table = Table;
            if (arm == null || baseLink == null || model == null || vr == null || table == null)
            {
                L("ABORT: required nodes missing (arm/base_link/model/VR拆解/放置台)");
                WriteReport(false, "missing nodes");
                return;
            }

            // Idempotence guard: Apply moves the arm/pack, so running it twice would stack offsets.
            // Reset path = Step1/9 Reload Scene From Disk, then Apply.
            if (FindIn(LeftBay, "MobileBase_H2515") != null)
            {
                L("ABORT: left bay already contains MobileBase_H2515 (Step 1 applied).");
                L("       Use DigitalTwin/Step1/9 Reload Scene From Disk first if you want to redo it.");
                WriteReport(false, "already applied");
                return;
            }

            // ---- 1. import settings + mobile base assets
            ImportSettings();
            bool baseOk = ImportMobileBase();

            // ---- 2. place mobile base + riser
            Json.Append("  \"mobileBase\": {\n");
            if (baseOk)
            {
                var baseInstance = PlaceMobileBase(baseLink);
                Json.Append("  },\n");
                // riser is created later, after the final arm height is known
                Json.Append("  \"riser\": ");
            }
            else Json.Append("  },\n  \"riser\": null,\n");

            // ---- 3. pose the arm at the real Screw_10 hover joint angles
            var qScrew10 = new[] { -15.8592557f, 22.2033715f, 77.6020029f, -0.3482830f, -9.8043079f, 180.3430348f };
            model.ApplyJointDegrees(qScrew10, RobotModelController.PoseSource.Slider, true);
            Physics.SyncTransforms();
            L("arm posed at Screw_10_hover q=" + string.Join(",", Array.ConvertAll(qScrew10, F)));

            var link6 = FindIn(BaseLink, "link_1", "link_2", "link_3", "link_4", "link_5", "link_6");
            if (link6 == null) { L("ABORT: link_6 not found"); WriteReport(false, "no link6"); return; }

            // ---- 4. mount spindle + camera under link_6
            var spindle = MountSpindle(link6);

            // ---- 5. TCP nodes + gizmo
            var flangeTcp = MakeTcpNode(link6, "Flange_TCP", Vector3.zero, Quaternion.Euler(180f, 0f, 0f));
            var spindleTcp = MakeTcpNode(flangeTcp, "Spindle_TCP", new Vector3(0f, 0f, ToolLengthM), Quaternion.identity);

            // ---- 6. horizontal + vertical alignment of the pack (single anchor)
            var screws = FindIn(FindIn(FindIn(FindIn(vr, "Dis_1"), "Lay1"), "pri1"), "screws");
            Vector3 tip = spindleTcp.position;
            int screwIdx = -1;
            Vector3 screwCenter = Vector3.zero, screwSize = Vector3.zero;
            if (screws != null)
            {
                // Single anchor = a CORNER screw of the top cover (Screw_10 sits on the pack corner).
                // Among the four corner-most screws pick the one closest to the current tool axis,
                // so the pack only needs a small horizontal nudge.
                var centers = new List<Vector3>();
                var sizes = new List<Vector3>();
                var indices = new List<int>();
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < screws.childCount; i++)
                {
                    Bounds cb;
                    if (!TryBounds(screws.GetChild(i), out cb)) continue;
                    centers.Add(cb.center); sizes.Add(cb.size); indices.Add(i);
                    minX = Mathf.Min(minX, cb.center.x); maxX = Mathf.Max(maxX, cb.center.x);
                    minZ = Mathf.Min(minZ, cb.center.z); maxZ = Mathf.Max(maxZ, cb.center.z);
                }
                var corners = new[] {
                    new Vector2(minX, minZ), new Vector2(minX, maxZ),
                    new Vector2(maxX, minZ), new Vector2(maxX, maxZ) };
                float bestCornerDist = float.MaxValue;
                int bestLocal = -1;
                foreach (var c in corners)
                {
                    int localBest = -1; float dCorner = float.MaxValue;
                    for (int k = 0; k < centers.Count; k++)
                    {
                        float d = (centers[k].x - c.x) * (centers[k].x - c.x) + (centers[k].z - c.y) * (centers[k].z - c.y);
                        if (d < dCorner) { dCorner = d; localBest = k; }
                    }
                    if (localBest < 0) continue;
                    float dTip = (centers[localBest].x - tip.x) * (centers[localBest].x - tip.x) +
                                 (centers[localBest].z - tip.z) * (centers[localBest].z - tip.z);
                    if (dTip < bestCornerDist) { bestCornerDist = dTip; bestLocal = localBest; }
                }
                if (bestLocal < 0) { L("ABORT: corner screw not resolved"); WriteReport(false, "no corner screw"); return; }
                screwIdx = indices[bestLocal];
                screwCenter = centers[bestLocal];
                screwSize = sizes[bestLocal];
                L("anchor = corner screw #" + screwIdx + " at " + V(screwCenter) +
                  " (horizontal distance to tool axis " + F(Mathf.Sqrt(bestCornerDist) * 1000f) + " mm)");
            }
            if (screwIdx < 0) { L("ABORT: no screw bounds"); WriteReport(false, "no screws"); return; }

            // table feet to Y=0  =>  pack/table drop by the table's current min Y
            Bounds tb;
            float tableMinY = TryBounds(table, out tb) ? tb.min.y : 0f;
            Vector3 delta = new Vector3(tip.x - screwCenter.x, -tableMinY, tip.z - screwCenter.z);
            vr.position += delta;
            Physics.SyncTransforms();
            L("pack/table moved by " + V(delta) + " (tableMinY was " + F(tableMinY) + ")");

            // vertical: put screw head exactly HoverGap under Spindle_TCP by moving the arm
            Bounds sbAfter;
            TryBounds(screws.GetChild(screwIdx), out sbAfter);
            float screwTop = sbAfter.center.y + sbAfter.size.y * 0.5f;
            float tipY = spindleTcp.position.y;
            float wantTipY = screwTop + HoverGapM;
            float dyArm = wantTipY - tipY;
            arm.position += new Vector3(0f, dyArm, 0f);
            Physics.SyncTransforms();
            L("arm raised/lowered by " + F(dyArm) + " m to close the hover gap");

            // ---- 7. riser between base mounting plane and base_link
            float baseLinkY = BaseLink.position.y;
            float riserH = baseLinkY - BaseMountPlaneY;
            var riser = MakeRiser(BaseLink.position, riserH);

            // ---- 8. bit extension (visual bridge between model nose and spec S_C_Tcp)
            var bitExt = MakeBitExtension(link6, spindleTcp, spindle);

            // ---- 9. collision mode + UI refresh
            var dis1 = FindIn(FindIn(LeftBay, PackName, VrName), "Dis_1");
            var ds = dis1 == null ? null : dis1.GetComponent<DisassemblySystem>();
            if (ds != null) { ds.detectionMode = DisassemblySystem.CollisionMode.Hybrid; EditorUtility.SetDirty(ds); }
            int uiChanged = UnifyUiRefresh(10f);

            // ---- 10. keep the calibrated pose visible in Play mode
            // (startupPose=ApplyZeroPose would snap the arm back to zero on Play)
            var so = new SerializedObject(model);
            var prop = so.FindProperty("startupPoseMode");
            if (prop != null) { prop.enumValueIndex = 0; so.ApplyModifiedPropertiesWithoutUndo(); } // 0 = KeepCurrentEditorPose

            MarkSceneDirty();

            // ---- report
            WriteFinalReport(baseLink, link6, flangeTcp, spindleTcp, riser, bitExt, spindle, screws, screwIdx,
                             delta, dyArm, riserH, uiChanged, ds);

            var roamAfter = SnapshotRoot(RoamBayName);
            var displayAfter = SnapshotRoot(DisplayBayName);
            bool untouched = roamBefore == roamAfter && displayBefore == displayAfter;
            L("display/roaming bay untouched: " + untouched);

            File.WriteAllText(ReportPath, Json.ToString() + "}\n", new UTF8Encoding(false));
            L("apply report -> " + ReportPath);
            L("DONE");
        }

        // ------------------------------------------------------------------ steps

        private static void ImportSettings()
        {
            var mi = AssetImporter.GetAtPath(SpindleFbxAsset) as ModelImporter;
            if (mi == null) { L("spindle FBX importer not found"); return; }
            bool changed = false;
            if (mi.importCameras) { mi.importCameras = false; changed = true; }
            if (!mi.isReadable) { mi.isReadable = true; changed = true; }
            if (changed) { mi.SaveAndReimport(); L("spindle FBX: importCameras=false, isReadable=true"); }
            else L("spindle FBX already importCameras=false / isReadable=true");
        }

        private static bool ImportMobileBase()
        {
            try
            {
                if (!Directory.Exists(Path.Combine(Root, BaseDirAsset)))
                {
                    Directory.CreateDirectory(Path.Combine(Root, BaseDirAsset));
                    Directory.CreateDirectory(Path.Combine(Root, BaseDirAsset, "textures"));
                }
                string srcFbx = Path.Combine(DesktopPack, "Doosan_H2515_MobileBase.fbx");
                string dstFbx = Path.Combine(Root, BaseDirAsset, "Doosan_H2515_MobileBase.fbx");
                if (!File.Exists(dstFbx)) File.Copy(srcFbx, dstFbx);
                foreach (var tex in Directory.GetFiles(Path.Combine(DesktopPack, "textures"), "*.png"))
                {
                    string d = Path.Combine(Root, BaseDirAsset, "textures", Path.GetFileName(tex));
                    if (!File.Exists(d)) File.Copy(tex, d);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var mi = AssetImporter.GetAtPath(BaseFbxAsset) as ModelImporter;
                if (mi != null)
                {
                    mi.importCameras = false;
                    mi.importLights = false;
                    mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    mi.SaveAndReimport();
                }
                L("mobile base imported -> " + BaseFbxAsset);
                return AssetDatabase.LoadAssetAtPath<GameObject>(BaseFbxAsset) != null;
            }
            catch (Exception e)
            {
                L("mobile base import FAILED: " + e.Message);
                return false;
            }
        }

        private static GameObject PlaceMobileBase(Transform baseLink)
        {
            // The vendor ASCII FBX imports as an empty GameObject in 2022.3, so the mesh is
            // rebuilt from the FBX geometry arrays (see RobotBaseFbxImporter).
            string info;
            var go = RobotBaseFbxImporter.BuildInstance(out info);
            if (go == null) { L("mobile base build FAILED: " + info); return null; }
            go.transform.SetParent(SceneRoot(LeftBayName), true);
            go.transform.position = new Vector3(baseLink.position.x, 0f, baseLink.position.z);
            go.transform.rotation = Quaternion.Euler(0f, baseLink.eulerAngles.y, 0f);
            go.transform.localScale = Vector3.one;
            Bounds bb;
            TryBounds(go.transform, out bb);
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            J(1, "path", JS(P(go.transform)));
            J(1, "worldPos", V(go.transform.position));
            J(1, "boundsMin", V(bb.min));
            J(1, "boundsMax", V(bb.max));
            J(1, "boundsSize", V(bb.size));
            J(1, "childCount", go.transform.childCount.ToString(CultureInfo.InvariantCulture));
            J(1, "rendererCount", renderers.Length.ToString(CultureInfo.InvariantCulture));
            J(1, "materialSlots", renderers.Length > 0 ? renderers[0].sharedMaterials.Length.ToString(CultureInfo.InvariantCulture) : "0");
            J(1, "meshInfo", JS(info), true);
            L("mobile base placed at " + V(go.transform.position) + " bounds=" + V(bb.size) +
              " children=" + go.transform.childCount + " renderers=" + renderers.Length + " " + info);
            return go;
        }

        private static Transform MountSpindle(Transform link6)
        {
            var displayBay = SceneRoot(DisplayBayName);
            var src = FindIn(displayBay, "EVB_DS ", "Mec_Parts", SpindleName);
            if (src == null) { L("display spindle not found; skip mount"); return null; }

            // Mounting plate lookup is tolerant: child naming/duplication suffixes vary.
            // NOTE: this FBX keeps geometry inside the meshes (every child transform sits at the
            // model origin), so the mount point must come from the renderer BOUNDS, not .position.
            Transform plate = FindChildContains(src, "转接板2") ?? FindChildContains(src, "转接板");
            Bounds plateB;
            Vector3 plateWorld = (plate != null && TryBounds(plate, out plateB)) ? plateB.center : src.position;
            Vector3 plateLocal = src.InverseTransformPoint(plateWorld);
            L("spindle mounting plate: " + (plate != null ? plate.name : "NOT FOUND (fallback to model origin)") +
              " world=" + V(plateWorld) + " local=" + V(plateLocal));

            var copy = UnityEngine.Object.Instantiate(src.gameObject);
            copy.name = "Spindle_Camera_Unit";
            var ct = copy.transform;
            ct.SetParent(link6, true);
            ct.localScale = src.lossyScale;

            // orient: model "down the tool" (world -Y in the display) -> flanged outward axis of link_6
            // The display unit stands with its long axis along world Y and the tool pointing down.
            // Keep that posture and only level the tool axis onto link_6's outward (+ flange) axis.
            Quaternion rDisplay = src.rotation;
            Vector3 outAxis = link6.rotation * Vector3.back;      // link_6 local -Z = flange outward
            Quaternion r1 = Quaternion.FromToRotation(Vector3.down, outAxis);
            ct.rotation = r1 * rDisplay;

            // place: mounting plate centre on the flange centre
            Vector3 plateNow = ct.TransformPoint(plateLocal);
            ct.position += link6.position - plateNow;

            Physics.SyncTransforms();
            Bounds b;
            TryBounds(ct, out b);
            L("spindle mounted: pos=" + V(ct.position) + " toolAxis=" + V(outAxis) + " bounds=" + V(b.size));
            return ct;
        }

        private static Transform MakeTcpNode(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one;
            go.AddComponent<DigitalTwin.Tools.TcpAxisGizmo>();
            return go.transform;
        }

        private static GameObject MakeRiser(Vector3 baseLinkPos, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "MountRiser_" + Mathf.RoundToInt(h * 1000f) + "mm";
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.transform.position = new Vector3(baseLinkPos.x, BaseMountPlaneY + h * 0.5f, baseLinkPos.z);
            go.transform.rotation = Quaternion.identity;
            float radius = 0.11f;
            go.transform.localScale = new Vector3(radius * 2f, h * 0.5f, radius * 2f);

            var matPath = BaseDirAsset + "/Materials/M_Step1_Riser.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath) ?? MakeUrpMaterial(matPath, null, new Color(0.62f, 0.63f, 0.65f));
            go.GetComponent<Renderer>().sharedMaterial = mat;

            Json.Append("{\n");
            J(2, "name", JS(go.name));
            J(2, "heightM", F(h));
            J(2, "pos", V(go.transform.position));
            J(2, "topY", F(BaseMountPlaneY + h));
            J(2, "radiusM", F(radius), true);
            Json.Append("  },\n");
            L("riser " + F(h) + " m under base_link; top=" + F(BaseMountPlaneY + h));
            return go;
        }

        private static GameObject MakeBitExtension(Transform link6, Transform spindleTcp, Transform spindle)
        {
            if (spindle == null) return null;
            Vector3 outAxis = link6.rotation * Vector3.back;
            Vector3 flange = link6.position;

            // Model nose = the point that reaches FARTHEST along the outward axis (the bit tip),
            // i.e. the maximum projection, not the minimum (the motor body sits behind the plate).
            float maxProj = float.MinValue;
            Vector3 nose = spindleTcp.position;
            foreach (var r in spindle.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    float pr = Vector3.Dot(c - flange, outAxis);
                    if (pr > maxProj) { maxProj = pr; nose = c; }
                }
            }

            float target = ToolLengthM;
            float need = target - maxProj;
            Json.Append("  \"toolChain\": {\n");
            J(2, "modelNoseDistMm", F(maxProj * 1000f));
            J(2, "specTcpDistMm", F(target * 1000f));
            J(2, "deltaMm", F((target - maxProj) * 1000f));
            if (need > 0.002f)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "BitExt_visual_only";
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                go.transform.SetParent(link6, true);
                go.transform.position = nose + outAxis * (need * 0.5f);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, outAxis);
                go.transform.localScale = new Vector3(0.008f, need * 0.5f, 0.008f);
                var matPath = BaseDirAsset + "/Materials/M_Step1_Bit.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath) ?? MakeUrpMaterial(matPath, null, new Color(0.25f, 0.26f, 0.27f));
                go.GetComponent<Renderer>().sharedMaterial = mat;
                J(2, "bitExtension", JS(go.name));
                J(2, "bitLengthMm", F(need * 1000f), true);
                L("bit extension added: " + F(need * 1000f) + " mm to reach S_C_Tcp");
            }
            else
            {
                J(2, "bitExtension", "null", true);
            }
            Json.Append("  },\n");
            return null;
        }

        private static int UnifyUiRefresh(float hz)
        {
            var ui = SceneRoot("30_界面与交互");
            var panel = ui == null ? null : FindIn(ui, "UI_二期面板");
            if (panel == null) return 0;
            int n = 0;
            foreach (var mb in panel.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                foreach (var name in new[] { "refreshHz", "chartHz", "statusRefreshHz" })
                {
                    var p = so.FindProperty(name);
                    if (p == null || p.propertyType != SerializedPropertyType.Float) continue;
                    if (Mathf.Abs(p.floatValue - hz) < 0.001f) continue;
                    p.floatValue = hz;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mb);
                    n++;
                }
            }
            L("UI refresh fields set to " + hz + " Hz: " + n);
            return n;
        }

        private static string SnapshotRoot(string name)
        {
            var t = SceneRoot(name);
            if (t == null) return "<none>";
            var sb = new StringBuilder();
            sb.Append(t.name).Append(t.position.ToString("F4"));
            foreach (var r in t.GetComponentsInChildren<Transform>(true)) sb.Append(r.name).Append(r.position.ToString("F4"));
            return sb.ToString();
        }

        private static void MarkSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private static void WriteFinalReport(Transform baseLink, Transform link6, Transform flangeTcp, Transform spindleTcp,
                                             GameObject riser, GameObject bitExt, Transform spindle,
                                             Transform screws, int screwIdx, Vector3 delta, float dyArm, float riserH,
                                             int uiChanged, DisassemblySystem ds)
        {
            Json.Append("  \"final\": {\n");
            J(2, "baseLinkWorld", V(baseLink.position));
            J(2, "link6World", V(link6.position));
            J(2, "flangeTcpWorld", V(flangeTcp.position));
            J(2, "spindleTcpWorld", V(spindleTcp.position));
            J(2, "spindleOffsetMm", F(Vector3.Distance(flangeTcp.position, spindleTcp.position) * 1000f));
            if (spindle != null) { Bounds b; TryBounds(spindle, out b); J(2, "spindleBoundsSize", V(b.size)); }
            Bounds tb; TryBounds(Table, out tb);
            J(2, "tableBoundsMin", V(tb.min));
            J(2, "tableTopY", F(tb.max.y));
            Bounds pb; TryBounds(FindIn(FindIn(Pack, "Dis_1"), "Lay1"), out pb);
            J(2, "packTopY", F(pb.max.y));
            if (screws != null && screwIdx >= 0)
            {
                var sc = screws.GetChild(screwIdx);
                Bounds sb; TryBounds(sc, out sb);
                J(2, "anchorScrewIndex", screwIdx.ToString(CultureInfo.InvariantCulture));
                J(2, "anchorScrewName", JS(sc.name));
                J(2, "anchorScrewTopWorld", V(new Vector3(sb.center.x, sb.center.y + sb.size.y * 0.5f, sb.center.z)));
                J(2, "anchorGapMm", F((spindleTcp.position.y - (sb.center.y + sb.size.y * 0.5f)) * 1000f));
            }
            J(2, "packDeltaWorld", V(delta));
            J(2, "armLiftM", F(dyArm));
            J(2, "riserHeightM", F(riserH));
            J(2, "detectionMode", ds != null ? ds.detectionMode.ToString() : "null");
            J(2, "uiRefreshFieldsChanged", uiChanged.ToString(CultureInfo.InvariantCulture), true);
            Json.Append("  }\n");

            L("FINAL base_link=" + V(baseLink.position));
            L("FINAL link6=" + V(link6.position));
            L("FINAL Spindle_TCP=" + V(spindleTcp.position));
            L("FINAL screw->tip gap=" + F((spindleTcp.position.y -
                (TryBounds(screws.GetChild(screwIdx), out var sb2) ? sb2.center.y + sb2.size.y * 0.5f : 0f)) * 1000f) + " mm");
        }

        private static void WriteReport(bool ok, string note)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, Json.ToString() + "\n}\n", new UTF8Encoding(false));
            L("report written (" + ok + "): " + note);
        }

        // ------------------------------------------------------------------ verify

        [MenuItem("DigitalTwin/Step1/9 Reload Scene From Disk (discard unsaved)", false, 199)]
        public static void ReloadFromDisk()
        {
            // NewScene(Single) drops unsaved in-memory changes without a save prompt;
            // the on-disk .unity file is the pristine pre-apply state.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/Doosan-DT.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            L("scene reloaded from disk (unsaved changes discarded)");
        }

        [MenuItem("DigitalTwin/Step1/7 Repair Base FBX Import (ModelImporter)", false, 197)]
        public static void TidyHierarchy()
        {
            var bay = LeftBay;
            if (bay == null) { L("tidy: bay root missing"); return; }
            int moved = 0;
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.transform == bay.transform) continue;
                if (!go.name.StartsWith("MountRiser_", StringComparison.Ordinal)) continue;
                go.transform.SetParent(bay, true); // keep world transform
                moved++;
                L("tidy: parented " + go.name + " under " + bay.name);
            }
            L("tidy: moved=" + moved);
            MarkSceneDirty();
        }

        [MenuItem("DigitalTwin/Step1/7b Repair Base FBX Import (ModelImporter)", false, 197)]
        public static void RepairBaseImport()
        {
            string abs = Path.Combine(Root, BaseFbxAsset);
            string meta = abs + ".meta";
            for (int pass = 0; pass < 2; pass++)
            {
                var mi = AssetImporter.GetAtPath(BaseFbxAsset) as ModelImporter;
                if (mi != null)
                {
                    mi.importCameras = false;
                    mi.importLights = false;
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                    L("base importer OK (ModelImporter); sub-assets=" + AssetDatabase.LoadAllAssetsAtPath(BaseFbxAsset).Length);
                    return;
                }
                L("base importer is not ModelImporter (pass " + pass + ") -> recreate meta");
                if (File.Exists(meta)) File.Delete(meta);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            // last resort: delete the asset and copy the source again
            AssetDatabase.DeleteAsset(BaseFbxAsset);
            File.Copy(Path.Combine(DesktopPack, "Doosan_H2515_MobileBase.fbx"), abs, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mi2 = AssetImporter.GetAtPath(BaseFbxAsset) as ModelImporter;
            if (mi2 != null) { mi2.importCameras = false; mi2.isReadable = true; mi2.SaveAndReimport(); }
            L("base re-copied; importer=" + (mi2 != null ? "ModelImporter" : "STILL WRONG") +
              " sub-assets=" + AssetDatabase.LoadAllAssetsAtPath(BaseFbxAsset).Length);
        }

        [MenuItem("DigitalTwin/Step1/8 Diagnose Base Asset and Spindle Children", false, 198)]
        public static void Diagnose()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(BaseFbxAsset);
            L("base sub-assets: " + all.Length);
            foreach (var o in all)
            {
                if (o == null) continue;
                L("   " + o.GetType().Name + " : " + o.name);
            }
            var mi = AssetImporter.GetAtPath(BaseFbxAsset) as ModelImporter;
            if (mi != null)
                L("importer: globalScale=" + mi.globalScale + " useFileUnits=" + mi.useFileUnits +
                  " importCameras=" + mi.importCameras + " isReadable=" + mi.isReadable +
                  " materialImportMode=" + mi.materialImportMode + " meshCompression=" + mi.meshCompression);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(BaseFbxAsset);
            L("base root: " + (root == null ? "NULL" : root.name + " children=" + root.transform.childCount +
                               " comps=" + root.GetComponents<Component>().Length));
            if (root != null)
                foreach (Transform c in root.transform)
                    L("   child: " + c.name + " comps=" + c.GetComponents<Component>().Length);

            var displayBay = SceneRoot(DisplayBayName);
            var src = FindIn(displayBay, "EVB_DS ", "Mec_Parts", SpindleName);
            if (src == null) { L("spindle source not found"); return; }
            L("spindle source children (" + src.childCount + "):");
            for (int i = 0; i < src.childCount; i++)
            {
                var c = src.GetChild(i);
                L("   [" + i + "] '" + c.name + "' meshFilters=" + c.GetComponentsInChildren<MeshFilter>(true).Length);
            }
        }

        [MenuItem("DigitalTwin/Step1/2 Verify Left Bay", false, 103)]
        public static void Verify()
        {
            var link6 = FindIn(BaseLink, "link_1", "link_2", "link_3", "link_4", "link_5", "link_6");
            var flange = link6 == null ? null : FindIn(link6, "Flange_TCP");
            var tip = flange == null ? null : FindIn(flange, "Spindle_TCP");
            L("verify: base_link=" + (BaseLink != null ? V(BaseLink.position) : "?"));
            L("verify: link6=" + (link6 != null ? V(link6.position) : "?"));
            L("verify: Flange_TCP=" + (flange != null ? V(flange.position) : "?"));
            L("verify: Spindle_TCP=" + (tip != null ? V(tip.position) : "?"));
            if (flange != null && tip != null)
                L("verify: |Flange_TCP-Spindle_TCP|=" + F(Vector3.Distance(flange.position, tip.position) * 1000f) + " mm");
            Bounds tb; if (Table != null && TryBounds(Table, out tb)) L("verify: table minY=" + F(tb.min.y));
            var screws = FindIn(FindIn(FindIn(FindIn(Pack, "Dis_1"), "Lay1"), "pri1"), "screws");
            if (screws != null && tip != null)
            {
                float best = float.MaxValue; int idx = -1; Bounds bestB = new Bounds();
                for (int i = 0; i < screws.childCount; i++)
                {
                    Bounds b; if (!TryBounds(screws.GetChild(i), out b)) continue;
                    float d = (b.center.x - tip.position.x) * (b.center.x - tip.position.x) + (b.center.z - tip.position.z) * (b.center.z - tip.position.z);
                    if (d < best) { best = d; idx = i; bestB = b; }
                }
                float top = bestB.center.y + bestB.size.y * 0.5f;
                L("verify: nearest screw #" + idx + " horizontal dist=" + F(Mathf.Sqrt(best) * 1000f) + " mm, gap=" + F((tip.position.y - top) * 1000f) + " mm");
            }
        }
    }
}