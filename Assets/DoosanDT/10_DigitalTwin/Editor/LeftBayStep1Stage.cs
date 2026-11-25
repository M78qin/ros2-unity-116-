// LeftBayStep1Stage.cs -- Step 1 stage operations for the LEFT-FRONT bay only
// (00_数字孪生机械臂 / 4.24-Try / h2515-1 (4) and its battery-pack sibling).
// Guarantee: never modifies 20_漫游与训练 or 40_环境与灯光 (reference read only).
// Phase 0 = measure only (no scene change), Phase 1 = apply, Phase 2 = verify.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class LeftBayStep1Stage
    {
        private const string ReportDir = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run";
        private const string ReportPath = ReportDir + @"\step1_stage_report.json";

        // Real-robot flange points (BASE frame, mm) derived from E-Calib:
        //   flange = tcp - 287.92 mm * toolZ  (toolZ ~ BASE +Z for all three poses)
        private static readonly Vector3 FlangeHomeBase = new Vector3(1010.4919f, 9.8981f, 915.8948f);
        private static readonly Vector3 FlangeSaftyBase = new Vector3(1242.1681f, -342.2076f, 806.6299f);
        private static readonly Vector3 FlangeScrew10Base = new Vector3(1242.2419f, -342.1917f, 754.0512f);

        private static readonly float[] QHome = { 0f, 0f, 90f, 0f, 0f, 180f };
        private static readonly float[] QSafty = { -15.86f, 21.67f, 73.48f, -0.6f, -5.16f, 180.59f };
        private static readonly float[] QScrew10 = { -15.8592557f, 22.2033715f, 77.6020029f, -0.3482830f, -9.8043079f, 180.3430348f };

        private const string LeftBayName = "00_数字孪生机械臂";
        private const string DisplayBayName = "40_环境与灯光";
        private const string RoamBayName = "20_漫游与训练";
        private const string PackName = "10_电池包拆解系统";
        private const string VrName = "VR拆解";
        private const string TableName = "放置台";
        private const string SpindleName = "主轴与相机";

        // ------------------------------------------------------------------ utils

        private static string P(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder();
            var stack = new List<string>();
            while (t != null) { stack.Add(t.name); t = t.parent; }
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                sb.Append(stack[i]);
                if (i > 0) sb.Append('/');
            }
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
                {
                    if (cur.GetChild(i).name == n) { next = cur.GetChild(i); break; }
                }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        private static Transform SceneRoot(string name)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) return go.transform;
            return null;
        }

        private static Transform LeftBayRoot { get { return SceneRoot(LeftBayName); } }
        private static Transform DisplayRoot { get { return SceneRoot(DisplayBayName); } }
        private static Transform RoamRoot { get { return SceneRoot(RoamBayName); } }

        private static Transform ArmRoot
        {
            get
            {
                var b = LeftBayRoot;
                return b == null ? null : FindIn(b, "4.24-Try", "h2515-1 (4)");
            }
        }

        private static Transform BaseLink
        {
            get { return ArmRoot == null ? null : FindIn(ArmRoot, "base_link"); }
        }

        private static Transform Link6
        {
            get
            {
                Transform t = BaseLink;
                if (t == null) return null;
                for (int i = 1; i <= 6; i++)
                {
                    t = FindIn(t, "link_" + i);
                    if (t == null) return null;
                }
                return t;
            }
        }

        private static Transform PackRoot
        {
            get
            {
                var b = LeftBayRoot;
                return b == null ? null : FindIn(b, PackName);
            }
        }

        private static RobotModelController Model
        {
            get { return BaseLink == null ? null : BaseLink.GetComponent<RobotModelController>(); }
        }

        private static bool TryBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            if (t == null) return false;
            var rs = t.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            foreach (var r in rs)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private static void W(StringBuilder sb, int indent, string key, string value)
        {
            sb.Append(' ', indent * 2).Append('"').Append(key).Append("\": ").Append(value).Append(",\n");
        }

        private static string V(Vector3 v)
        {
            return "[" + v.x.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        v.y.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        v.z.ToString("0.######", CultureInfo.InvariantCulture) + "]";
        }

        private static string Q(Quaternion q)
        {
            return "[" + q.x.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        q.y.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        q.z.ToString("0.######", CultureInfo.InvariantCulture) + "," +
                        q.w.ToString("0.######", CultureInfo.InvariantCulture) + "]";
        }

        private static string F(float f)
        {
            return f.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void Frame(Vector3 a, Vector3 b, Vector3 c,
                                  out Vector3 e1, out Vector3 e2, out Vector3 e3, out Vector3 centroid)
        {
            e1 = (b - a).normalized;
            Vector3 ac = c - a;
            Vector3 e2r = ac - e1 * Vector3.Dot(ac, e1);
            e2 = e2r.normalized;
            e3 = Vector3.Cross(e1, e2);
            centroid = (a + b + c) / 3f;
        }

        private static Matrix4x4 FrameMatrix(Vector3 e1, Vector3 e2, Vector3 e3)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, e1);
            m.SetColumn(1, e2);
            m.SetColumn(2, e3);
            return m;
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";
        }

        // ------------------------------------------------------------------ phase 0

        [MenuItem("DigitalTwin/Step1/0 Report Left Bay Geometry", false, 101)]
        public static void ReportGeometry()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            W(sb, 1, "generatedUtc", JsonStr(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
            W(sb, 1, "stage", "\"measure-only\"");

            Transform baseLink = BaseLink, link6 = Link6, arm = ArmRoot, pack = PackRoot;

            sb.Append("  \"anchors\": {\n");
            if (baseLink != null)
            {
                W(sb, 2, "baseLinkPath", JsonStr(P(baseLink)));
                W(sb, 2, "baseLinkPos", V(baseLink.position));
                W(sb, 2, "baseLinkRot", Q(baseLink.rotation));
                W(sb, 2, "baseLinkLossyScale", V(baseLink.lossyScale));
            }
            if (link6 != null)
            {
                W(sb, 2, "link6Path", JsonStr(P(link6)));
                W(sb, 2, "link6Pos", V(link6.position));
                W(sb, 2, "link6Rot", Q(link6.rotation));
            }
            if (arm != null)
            {
                W(sb, 2, "armRootPos", V(arm.position));
                W(sb, 2, "armRootRot", Q(arm.rotation));
                W(sb, 2, "armRootLossyScale", V(arm.lossyScale));
            }
            if (pack != null)
            {
                W(sb, 2, "packRootPath", JsonStr(P(pack)));
                W(sb, 2, "packRootPos", V(pack.position));
                W(sb, 2, "packRootRot", Q(pack.rotation));
            }
            sb.Append("  },\n");

            var model = Model;
            sb.Append("  \"robotModel\": {\n");
            if (model != null)
            {
                var cur = model.ReadCurrentDisplayDegrees();
                var qs = new StringBuilder("[");
                for (int i = 0; i < cur.Length; i++)
                {
                    if (i > 0) qs.Append(',');
                    qs.Append(F(cur[i]));
                }
                qs.Append(']');
                W(sb, 2, "jointCount", model.JointCount.ToString(CultureInfo.InvariantCulture));
                W(sb, 2, "currentDisplayDeg6", qs.ToString());
                W(sb, 2, "controlAuthority", ((int)model.CurrentControlAuthority).ToString(CultureInfo.InvariantCulture));
                W(sb, 2, "startupPoseMode", ((int)model.CurrentStartupPoseMode).ToString(CultureInfo.InvariantCulture));
            }
            else W(sb, 2, "error", "\"RobotModelController not found on base_link\"");
            sb.Append("  },\n");

            sb.Append("  \"fkProbes\": [\n");
            if (model != null && link6 != null && baseLink != null)
            {
                float[] original = model.ReadCurrentDisplayDegrees();
                string[] names = { "Home_VR_DT", "Safty_1", "Screw_10_hover" };
                float[][] qset = { QHome, QSafty, QScrew10 };
                Vector3[] baseFlange = { FlangeHomeBase, FlangeSaftyBase, FlangeScrew10Base };
                var worldPos = new Vector3[3];
                for (int i = 0; i < 3; i++)
                {
                    model.ApplyJointDegrees(qset[i], RobotModelController.PoseSource.Slider, true);
                    Physics.SyncTransforms();
                    worldPos[i] = link6.position;
                    Vector3 local = baseLink.InverseTransformPoint(link6.position);
                    sb.Append("    {");
                    sb.Append("\"name\":").Append(JsonStr(names[i])).Append(',');
                    sb.Append("\"commandedDeg\":[").Append(F(qset[i][0])).Append(',').Append(F(qset[i][1])).Append(',')
                      .Append(F(qset[i][2])).Append(',').Append(F(qset[i][3])).Append(',').Append(F(qset[i][4])).Append(',')
                      .Append(F(qset[i][5])).Append("],");
                    sb.Append("\"link6World\":").Append(V(worldPos[i])).Append(',');
                    sb.Append("\"link6InBaseLinkLocal\":").Append(V(local)).Append(',');
                    sb.Append("\"flangeBaseMm\":").Append(V(baseFlange[i]));
                    sb.Append("},\n");
                }
                model.ApplyJointDegrees(original, RobotModelController.PoseSource.Slider, true);
                Physics.SyncTransforms();

                Vector3 e1b, e2b, e3b, cb;
                Frame(baseFlange[0], baseFlange[1], baseFlange[2], out e1b, out e2b, out e3b, out cb);
                Vector3 e1w, e2w, e3w, cw;
                Frame(worldPos[0], worldPos[1], worldPos[2], out e1w, out e2w, out e3w, out cw);
                Matrix4x4 mb = FrameMatrix(e1b, e2b, e3b);
                Matrix4x4 mw = FrameMatrix(e1w, e2w, e3w);
                Matrix4x4 R = mw * mb.transpose;
                Vector3 t = cw - R.MultiplyPoint3x4(cb);
                var res = new StringBuilder("[");
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pred = R.MultiplyPoint3x4(baseFlange[i]) + t;
                    if (i > 0) res.Append(',');
                    res.Append(F((pred - worldPos[i]).magnitude * 1000f));
                }
                res.Append(']');

                Vector3 screwContactBase = new Vector3(1242.2356f, -342.1925f, 1032.5f);
                Vector3 screwHoverBase = new Vector3(1242.2356f, -342.1925f, 1041.9712f);
                sb.Append("  ],\n");
                sb.Append("  \"baseToUnity\": {\n");
                W(sb, 2, "Rcol0", V(R.GetColumn(0)));
                W(sb, 2, "Rcol1", V(R.GetColumn(1)));
                W(sb, 2, "Rcol2", V(R.GetColumn(2)));
                W(sb, 2, "t", V(t));
                W(sb, 2, "residualMm", res.ToString());
                W(sb, 2, "screwContactWorld", V(R.MultiplyPoint3x4(screwContactBase) + t));
                W(sb, 2, "screwHoverWorld", V(R.MultiplyPoint3x4(screwHoverBase) + t));
                W(sb, 2, "packYawDegWorld", F(R.rotation.eulerAngles.y));
                sb.Append("  },\n");
            }
            else
            {
                sb.Append("  ],\n  \"baseToUnity\": null,\n");
            }

            sb.Append("  \"pack\": {\n");
            var vr = pack == null ? null : FindIn(pack, VrName);
            var dis = vr == null ? null : FindIn(vr, "Dis_1");
            var table = vr == null ? null : FindIn(vr, TableName);
            Bounds pb;
            if (dis != null && TryBounds(dis, out pb))
            {
                W(sb, 2, "dis1WorldBoundsCenter", V(pb.center));
                W(sb, 2, "dis1WorldBoundsSize", V(pb.size));
                W(sb, 2, "dis1WorldBoundsMin", V(pb.min));
                W(sb, 2, "dis1WorldBoundsMax", V(pb.max));
            }
            if (table != null)
            {
                Bounds tb;
                bool ok = TryBounds(table, out tb);
                W(sb, 2, "tablePath", JsonStr(P(table)));
                W(sb, 2, "tablePos", V(table.position));
                W(sb, 2, "tableLossyScale", V(table.lossyScale));
                if (ok)
                {
                    W(sb, 2, "tableBoundsMin", V(tb.min));
                    W(sb, 2, "tableBoundsMax", V(tb.max));
                    W(sb, 2, "tableBoundsSize", V(tb.size));
                }
            }
            var lay1 = dis == null ? null : FindIn(dis, "Lay1");
            var pri1 = lay1 == null ? null : FindIn(lay1, "pri1");
            var screws = pri1 == null ? null : FindIn(pri1, "screws");
            if (screws != null)
            {
                W(sb, 2, "screwsPath", JsonStr(P(screws)));
                W(sb, 2, "screwsChildCount", screws.childCount.ToString(CultureInfo.InvariantCulture));
                W(sb, 2, "screwsPos", V(screws.position));
                W(sb, 2, "screwsLossyScale", V(screws.lossyScale));
                Bounds sb2;
                if (TryBounds(screws, out sb2))
                {
                    W(sb, 2, "screwsBoundsCenter", V(sb2.center));
                    W(sb, 2, "screwsBoundsSize", V(sb2.size));
                    W(sb, 2, "screwsBoundsMin", V(sb2.min));
                    W(sb, 2, "screwsBoundsMax", V(sb2.max));
                }
                sb.Append("    \"screwChildren\": [\n");
                var distinct = new List<Vector3>();
                for (int i = 0; i < screws.childCount; i++)
                {
                    var ch = screws.GetChild(i);
                    Bounds cb2;
                    bool ok = TryBounds(ch, out cb2);
                    Vector3 c = ok ? cb2.center : ch.position;
                    bool dup = false;
                    foreach (var d in distinct)
                        if ((d - c).sqrMagnitude < 1e-8f) { dup = true; break; }
                    if (!dup) distinct.Add(c);
                    sb.Append("      {\"i\":").Append(i)
                      .Append(",\"name\":").Append(JsonStr(ch.name))
                      .Append(",\"worldPos\":").Append(V(ch.position))
                      .Append(",\"boundsCenter\":").Append(ok ? V(cb2.center) : "null")
                      .Append(",\"boundsSize\":").Append(ok ? V(cb2.size) : "null")
                      .Append(",\"meshCount\":").Append(ch.GetComponentsInChildren<MeshFilter>(true).Length.ToString(CultureInfo.InvariantCulture))
                      .Append("},\n");
                }
                sb.Append("      {\"distinctWorldCenters\":").Append(distinct.Count.ToString(CultureInfo.InvariantCulture)).Append("}\n");
                sb.Append("    ],\n");
            }
            sb.Append("  },\n");

            sb.Append("  \"displaySpindle\": {\n");
            var mec = DisplayRoot == null ? null : FindIn(DisplayRoot, "EVB_DS ", "Mec_Parts");
            var spindle = mec == null ? null : FindIn(mec, SpindleName);
            if (spindle != null)
            {
                Bounds spb;
                bool ok = TryBounds(spindle, out spb);
                W(sb, 2, "path", JsonStr(P(spindle)));
                W(sb, 2, "pos", V(spindle.position));
                W(sb, 2, "rot", Q(spindle.rotation));
                W(sb, 2, "lossyScale", V(spindle.lossyScale));
                if (ok)
                {
                    W(sb, 2, "boundsCenter", V(spb.center));
                    W(sb, 2, "boundsSize", V(spb.size));
                }
                sb.Append("    \"children\": [\n");
                for (int i = 0; i < spindle.childCount; i++)
                {
                    var ch = spindle.GetChild(i);
                    Bounds cb3;
                    bool ok2 = TryBounds(ch, out cb3);
                    sb.Append("      {\"name\":").Append(JsonStr(ch.name))
                      .Append(",\"pos\":").Append(V(ch.position))
                      .Append(",\"boundsCenter\":").Append(ok2 ? V(cb3.center) : "null")
                      .Append(",\"boundsSize\":").Append(ok2 ? V(cb3.size) : "null")
                      .Append(",\"meshFilters\":").Append(ch.GetComponentsInChildren<MeshFilter>(true).Length.ToString(CultureInfo.InvariantCulture))
                      .Append("},\n");
                }
                sb.Append("      {}\n    ]\n");
            }
            else W(sb, 2, "error", "\"display spindle not found\"");
            sb.Append("  },\n");

            sb.Append("  \"assets\": {\n");
            W(sb, 2, "robotBaseDirExists", Directory.Exists(@"E:\new_2026-\unity\project\Unity_Ros_Try1\Assets\DoosanDT\Models\RobotBase") ? "true" : "false");
            W(sb, 2, "spindleFbxExists", File.Exists(@"E:\new_2026-\unity\project\Unity_Ros_Try1\Assets\DoosanDT\20_BatteryDisassembly\Assets\Models\主轴与相机.fbx") ? "true" : "false");
            W(sb, 2, "desktopPackExists", Directory.Exists(@"C:\Users\Administrator\Desktop\Doosan_H2515_MobileBase_UnityPack-v2") ? "true" : "false");
            W(sb, 2, "roamingRootExists", RoamRoot != null ? "true" : "false");
            W(sb, 2, "displayRootExists", DisplayRoot != null ? "true" : "false");
            sb.Append("  }\n");
            sb.Append("}\n");

            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Step1] measure report -> " + ReportPath);
            Debug.Log("[Step1] base_link=" + (baseLink != null ? V(baseLink.position) : "?") +
                      " link6=" + (link6 != null ? V(link6.position) : "?"));
        }
    }
}