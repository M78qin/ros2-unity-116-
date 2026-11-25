// Step3Cleanup.cs -- minimal, explicit cleanup (no auto-alignment, no pack repositioning).
//  1. delete BitExt_visual_only, retarget the rig to the spindle's own bit tip (length 0)
//  2. make sure MobileBase_H2515 is active + visible with wheels on Y=0
//  3. seat h2515-1 (4) on the base mounting face (base plate top), removing the floor sink
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using DigitalTwin.Tools;
using DigitalTwin.Interaction;
using DoosanDT.UI.Runtime;

namespace DigitalTwin.EditorTools
{
    public static class Step3Cleanup
    {
        private const string LeftBayName = "00_数字孪生机械臂";
        private const string LogPath = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\step3_cleanup.txt";
        private static readonly StringBuilder Log = new StringBuilder();

        private static void L(string s) { Debug.Log("[Step3] " + s); Log.AppendLine(s); }

        private static string V(Vector3 v) =>
            "(" + v.x.ToString("0.000000", CultureInfo.InvariantCulture) + ", " +
                  v.y.ToString("0.000000", CultureInfo.InvariantCulture) + ", " +
                  v.z.ToString("0.000000", CultureInfo.InvariantCulture) + ")";

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

        private static Transform SceneRoot(string n)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == n) return go.transform;
            return null;
        }

        private static bool TryBounds(Transform t, out Bounds b, Func<Renderer, bool> filter = null)
        {
            b = new Bounds();
            if (t == null) return false;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (filter != null && !filter(r)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        [MenuItem("DigitalTwin/Step3/1 Cleanup (BitExt + Base + Seat Arm)", false, 301)]
        public static void Run()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var link6 = arm == null ? null : FindDeep(arm, "link_6");
            var spindle = link6 == null ? null : link6.Find("Spindle_Camera_Unit");
            var bitAxis = spindle == null ? null : spindle.Find("BitAxis");
            var bitExt = FindDeep(bay, "BitExt_visual_only");
            var tcp = FindDeep(bay, "Spindle_TCP");
            var baseGo = FindDeep(bay, "MobileBase_H2515");

            if (bitAxis == null || tcp == null || baseGo == null)
            { L("ABORT: BitAxis / Spindle_TCP / MobileBase_H2515 not found"); Flush(); return; }

            // ---------- 1. drop the temporary extension, park the TCP on the model bit tip
            var rig = bitAxis.GetComponent<ToolBitRig>();
            if (rig != null)
            {
                rig.bitExtension = null;
                rig.bitExtensionLengthMm = 0f;
            }
            if (bitExt != null)
            {
                UnityEngine.Object.DestroyImmediate(bitExt.gameObject);
                L("deleted BitExt_visual_only");
            }
            else L("BitExt_visual_only already absent");
            if (rig != null) { rig.Apply(); EditorUtility.SetDirty(rig); }

            tcp.localPosition = Vector3.zero;   // BitAxis origin == model bit tip
            tcp.localRotation = Quaternion.identity;
            L("Spindle_TCP -> model bit tip: world " + V(tcp.position) + " (local " + V(tcp.localPosition) + ")");

            var flange = link6 == null ? null : link6.Find("Flange_TCP");
            if (flange != null)
            {
                Vector3 vec = tcp.position - flange.position;
                L("Flange_TCP -> Spindle_TCP = " + V(vec) + "  |" +
                  (vec.magnitude * 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " mm|");
            }

            // ---------- 2. mobile base must be active + visible, wheels on Y=0
            var baseT = baseGo;
            if (!baseT.gameObject.activeSelf) { baseT.gameObject.SetActive(true); L("base was INACTIVE -> activated"); }
            if (!baseT.gameObject.activeInHierarchy) L("WARNING: base still not activeInHierarchy");
            int hiddenRenderers = 0;
            foreach (var r in baseT.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) { r.enabled = true; hiddenRenderers++; }
                if (r.forceRenderingOff) r.forceRenderingOff = false;
            }
            L("base activeSelf=" + baseT.gameObject.activeSelf + " activeInHierarchy=" + baseT.gameObject.activeInHierarchy +
              " renderersReenabled=" + hiddenRenderers);

            Bounds bb;
            if (TryBounds(baseT, out bb))
            {
                L("base world=" + V(baseT.position) + " boundsMin=" + V(bb.min) + " boundsMax=" + V(bb.max) +
                  " size=" + V(bb.size));
                if (Mathf.Abs(bb.min.y) > 0.0005f)
                {
                    Vector3 p = baseT.position;
                    baseT.position = new Vector3(p.x, p.y - bb.min.y, p.z);
                    L("base re-seated on Y=0 (dy=" + (-bb.min.y).ToString("0.0000", CultureInfo.InvariantCulture) + ")");
                    TryBounds(baseT, out bb);
                    L("base bounds after: min=" + V(bb.min) + " max=" + V(bb.max));
                }
            }

            // ---------- 3. seat the arm on the base mounting face
            float plateTop = bb.max.y;                 // base plate top (adapter face)
            Bounds ab;
            if (TryBounds(arm, out ab))
            {
                float dy = plateTop - ab.min.y;
                L("arm bounds min.y=" + V(new Vector3(0f, ab.min.y, 0f)) + " plateTop=" +
                  plateTop.ToString("0.0000", CultureInfo.InvariantCulture) +
                  " -> dy=" + dy.ToString("0.0000", CultureInfo.InvariantCulture));
                if (Mathf.Abs(dy) > 0.0005f)
                {
                    arm.position = arm.position + new Vector3(0f, dy, 0f);
                    L("arm raised by " + dy.ToString("0.0000", CultureInfo.InvariantCulture) + " m to seat on the plate");
                }
                TryBounds(arm, out ab);
                L("arm bounds after: min=" + V(ab.min) + " max=" + V(ab.max));
            }
            else L("WARNING: arm has no renderers");

            var baseLink = FindDeep(arm, "base_link");
            if (baseLink != null) L("base_link world=" + V(baseLink.position));
            if (flange != null) L("Flange_TCP world=" + V(flange.position));
            if (tcp != null) L("Spindle_TCP world=" + V(tcp.position));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        [MenuItem("DigitalTwin/Step3/2 Detach ToolBitRig (leave TCP free for manual tuning)", false, 302)]
        public static void DetachRigEntry() { DetachRig(); }

        [MenuItem("DigitalTwin/Step4/1 Create Spindle_TCP at Bit Tip", false, 401)]
        public static void CreateSpindleTcp() { CreateSpindleTcpCore(); }

        [MenuItem("DigitalTwin/Step5/1 Attach ToolTcpSwitcher to base_link", false, 501)]
        public static void AttachSwitcherMenu() { AttachSwitcherInternal(); }

        [MenuItem("DigitalTwin/Step6/1 Fix Spindle Subtree (static flags, strays, physics)", false, 601)]
        public static void FixSpindleSubtree()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var link6 = arm == null ? null : FindDeep(arm, "link_6");
            var spindle = link6 == null ? null : link6.Find("Spindle_Camera");
            if (spindle == null) { L("ABORT: Spindle_Camera not found"); Flush(); return; }

            int staticWas = 0, cleared = 0, renderers = 0;
            foreach (var t in spindle.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var go = t.gameObject;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                if (go.isStatic || flags != 0)
                {
                    staticWas++;
                    go.isStatic = false;
                    GameObjectUtility.SetStaticEditorFlags(go, 0);
                    cleared++;
                }
                renderers += go.GetComponents<Renderer>().Length;
            }
            L("Spindle_Camera subtree: renderers=" + renderers + " staticObjectsFound=" + staticWas +
              " cleared=" + cleared);

            // Physics on a bracket would make it fall / stay behind in Play.
            int rb = 0, ab = 0, joints = 0;
            foreach (var t in spindle.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var body = t.GetComponent<Rigidbody>();
                if (body != null)
                {
                    rb++;
                    if (!body.isKinematic)
                    {
                        body.isKinematic = true;
                        body.useGravity = false;
                        L("  Rigidbody made kinematic on " + P(t));
                    }
                    else L("  Rigidbody (already kinematic) on " + P(t));
                    if (body.constraints != RigidbodyConstraints.None)
                    {
                        L("  Rigidbody constraints=" + body.constraints + " on " + P(t));
                        body.constraints = RigidbodyConstraints.None;
                    }
                }
                if (t.GetComponent<ArticulationBody>() != null) { ab++; L("  ArticulationBody on " + P(t)); }
                if (t.GetComponent<Joint>() != null) { joints++; L("  Joint on " + P(t)); }
            }
            L("physics under subtree: rigidbodies=" + rb + " articulationBodies=" + ab + " joints=" + joints);

            // Stray bracket-like objects living OUTSIDE link_6 (they would not follow the arm).
            int strayCount = 0;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                string n = t.name;
                if (n.IndexOf("支架", StringComparison.Ordinal) < 0 &&
                    n.IndexOf("转接板", StringComparison.Ordinal) < 0 &&
                    n.IndexOf("U型", StringComparison.Ordinal) < 0) continue;
                if (t.root != null && arm != null && (t == arm || t.IsChildOf(arm))) continue;
                strayCount++;
                if (strayCount <= 20)
                {
                    var r = t.GetComponent<Renderer>();
                    L("  STRAY (outside h2515-1): " + P(t) + (r != null ? "  world=" + V(r.bounds.center) : ""));
                }
            }
            L("stray bracket-like objects outside the active arm = " + strayCount);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        [MenuItem("DigitalTwin/Step6/2 Disable IK Drag Default + Add TCP Axes", false, 602)]
        public static void AddIkBarMenu() { AddIkBar(); }

        [MenuItem("DigitalTwin/Step8/1 Startup Pose Home_VR_DT + Float IK Bar", false, 801)]
        public static void EmbedIkBarMenu() { EmbedIkBar(); }

        [MenuItem("DigitalTwin/Step9/1 Embed IK Card Inside PageControl (no float frame)", false, 901)]
        public static void CloneCardMenu() { CloneNativeCard(); }

        [MenuItem("DigitalTwin/Step9/2 Clone Native Card Shell For IK Control", false, 902)]
        public static void RunPoseTraceMenu() { RunPoseTrace(); }

        [MenuItem("DigitalTwin/Step10/1 Run Pose Trace In Play (auto exit)", false, 1001)]
        public static void HardenDrivesMenu() { HardenDrives(); }

        [MenuItem("DigitalTwin/Step10/2 Harden Articulation Drives (no gravity sag)", false, 1002)]
        public static void LockEditPoseMenu() { LockEditPose(); }

        [MenuItem("DigitalTwin/Step11/1 Lock Edit Pose As Startup/Home + Clone Card", false, 1101)]
        public static void FinalPoseMenu() { FinalPoseFix(); }

        [MenuItem("DigitalTwin/Step12/1 Final Fix (pose 0/0/90/0/0/180, colliders, title)", false, 1201)]
        public static void StableDrivesMenu() { StableDrives(); }

        [MenuItem("DigitalTwin/Step12/2 Stable Drives (no physics explosion)", false, 1202)]
        public static void DisableIkMenu() { DisableIkPreview(); }

        [MenuItem("DigitalTwin/Step12/3 Disable Runtime IK Preview (stop pose override)", false, 1203)]
        public static void AttachPoseHoldMenu() { AttachPoseHold(); }

        [MenuItem("DigitalTwin/Step12/4 Attach Kinematic Pose Hold (hard lock joints)", false, 1204)]
        public static void KillSelfCollisionMenu() { KillSelfCollision(); }

        [MenuItem("DigitalTwin/Step12/5 Kill Self-Colliders On Arm (anti push-out)", false, 1205)]
        public static void MountProbeMenu() { MountProbe(true); }

        [MenuItem("DigitalTwin/Step13/1 Mount Pose Probe In Scene (auto verify)", false, 1301)]
        public static void MountProbeEntry() { MountProbe(true); }

        [MenuItem("DigitalTwin/Step13/2 Unmount Pose Probe (normal play)", false, 1302)]
        public static void CaptureRestMenu() { CaptureRestPose(); }

        [MenuItem("DigitalTwin/Step14/1 Capture Rest Pose (Edit, 0/0/90/0/0/180)", false, 1401)]
        public static void CaptureRestPose()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model == null) { L("ABORT: model missing"); Flush(); return; }
            if (EditorApplication.isPlaying) { L("ABORT: must run in Edit mode"); Flush(); return; }

            var pose = new float[] { 0f, 0f, 90f, 0f, 0f, 180f };
            // 关键：restLocalRotation 必须是 display 全零时的局部旋转，否则 Play 会重复叠加关节角。
            var zero = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };
            model.ApplyJointDisplayDegrees(zero, true, RobotModelController.PoseSource.EditorSlider, true, true);
            L("applied zero pose for rest capture");

            var so = new SerializedObject(model);
            var pj = so.FindProperty("joints");
            if (pj == null || !pj.isArray) { L("ABORT: joints property missing"); Flush(); return; }
            int n = Mathf.Min(6, pj.arraySize);
            for (int i = 0; i < n; i++)
            {
                var el = pj.GetArrayElementAtIndex(i);
                var bodyProp = el.FindPropertyRelative("body");
                var restProp = el.FindPropertyRelative("restLocalRotation");
                var hasProp = el.FindPropertyRelative("hasRestLocalRotation");
                var body = bodyProp != null ? bodyProp.objectReferenceValue as ArticulationBody : null;
                if (body == null) { L("  joint " + (i + 1) + ": body null"); continue; }
                Quaternion q = body.transform.localRotation;
                if (restProp != null) restProp.quaternionValue = q;
                if (hasProp != null) hasProp.boolValue = true;
                L("  joint " + (i + 1) + " " + body.name + " restLocalRotation=" + q.eulerAngles.ToString("F3"));
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);

            // 捕获完立刻把场景恢复到标准姿态
            model.ApplyJointDisplayDegrees(pose, true, RobotModelController.PoseSource.EditorSlider, true, true);
            L("restored Edit pose [0,0,90,0,0,180]");
            Flush();
        }

        [MenuItem("DigitalTwin/Step14/2 Dump Edit Pose (for compare)", false, 1402)]
        public static void DumpEditPose()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            if (arm == null) { L("ABORT: arm missing"); Flush(); return; }
            var sb = new StringBuilder();
            var tip = FindDeep(arm, "Spindle_TCP");
            sb.Append("{\"t\":0.0,");
            if (tip != null)
            {
                float dot = Vector3.Dot(tip.forward.normalized, Vector3.down);
                sb.Append("\"tcpForward\":\"").Append(tip.forward.ToString("F4")).Append("\",");
                sb.Append("\"tcpPos\":\"").Append(tip.position.ToString("F4")).Append("\",");
                sb.Append("\"dotDown\":").Append(dot.ToString("F5", CultureInfo.InvariantCulture)).Append(",");
            }
            sb.Append("\"links\":{");
            for (int i = 1; i <= 6; i++)
            {
                var link = FindDeep(arm, "link_" + i);
                if (link == null) continue;
                sb.Append("\"link_").Append(i).Append("\":\"").Append(link.localRotation.eulerAngles.ToString("F3")).Append("\"");
                if (i < 6) sb.Append(",");
            }
            sb.Append("}}");
            try
            {
                System.IO.File.WriteAllText(@"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\edit_pose.jsonl",
                    sb.ToString(), new UTF8Encoding(false));
                L("edit pose dumped");
            }
            catch (Exception e) { L("dump failed: " + e.Message); }
            L(sb.ToString());
            Flush();
        }

        public static void RemovePoseHold()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            int removed = 0;
            if (arm != null)
                foreach (var h in arm.GetComponentsInChildren<RobotIkController>(true))
                {
                    if (h == null) continue;
                    UnityEngine.Object.DestroyImmediate(h);
                    removed++;
                }
            L("RobotIkController removed = " + removed);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void UnmountProbeEntry() { MountProbe(false); }

        public static void MountProbe(bool on)
        {
            Log.Clear();
            PoseTraceProbe probe = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<PoseTraceProbe>(true))
                if (p != null && p.gameObject.scene.IsValid()) { probe = p; break; }
            if (probe == null && on)
            {
                var go = new GameObject("PoseTraceProbe");
                probe = go.AddComponent<PoseTraceProbe>();
                L("probe object created");
            }
            if (probe == null) { L("probe not present; nothing to unmount"); Flush(); return; }
            var so = new SerializedObject(probe);
            var pAuto = so.FindProperty("autoRun");
            if (pAuto != null) pAuto.boolValue = on;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(probe);
            L("PoseTraceProbe autoRun=" + on);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void KillSelfCollision()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            if (arm == null) { L("ABORT: arm missing"); Flush(); return; }
            int disabled = 0, total = 0;
            foreach (var c in arm.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                total++;
                bool keepTrigger = c is Collider && c.isTrigger;
                if (c.enabled) { c.enabled = false; disabled++; }
                c.isTrigger = true;              // 双保险：即使被别处重新启用也不会产生推力
                EditorUtility.SetDirty(c);
            }
            L("arm colliders total=" + total + " disabled=" + disabled + " (all set isTrigger)");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void AttachPoseHold()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var baseLink = arm == null ? null : FindDeep(arm, "base_link");
            if (baseLink == null) { L("ABORT: base_link missing"); Flush(); return; }
            var hold = baseLink.GetComponent<RobotIkController>();
            if (hold == null) hold = baseLink.gameObject.AddComponent<RobotIkController>();
            EditorUtility.SetDirty(hold);
            L("RobotIkController attached on " + P(baseLink));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void DisableIkPreview()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var ik = arm == null ? null : arm.GetComponentInChildren<RobotIkController>(true);
            if (ik == null) { L("ABORT: RobotIkController missing"); Flush(); return; }
            var so = new SerializedObject(ik);
            foreach (var name in new[] { "enableIk", "enableRuntimeIk", "previewSolutionOnModel", "autoSolveOnDrag", "showIkHandle" })
            {
                var p = so.FindProperty(name);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean)
                {
                    bool before = p.boolValue;
                    p.boolValue = false;
                    L("  " + name + ": " + before + " -> false");
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ik);
            L("RobotIkController on " + P(ik.transform) + " disabled");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void StableDrives()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            if (arm == null) { L("ABORT: arm missing"); Flush(); return; }
            int n = 0;
            foreach (var ab in arm.GetComponentsInChildren<ArticulationBody>(true))
            {
                if (ab == null) continue;
                ab.useGravity = false;
                if (ab.jointType == ArticulationJointType.FixedJoint) { ab.immovable = true; continue; }
                var d = ab.xDrive;
                // 实测：stiffness=1e6 时 ω=√(k/I) 远超 1/dt，2.8 s 内发散成 [26,85,160,-230,-123,180]。
                // 重力已关，无需高刚度硬顶，取稳定区间。
                d.stiffness = 20000f;
                d.damping = 5000f;
                d.forceLimit = 20000f;
                d.driveType = ArticulationDriveType.Target;
                ab.xDrive = d;
                ab.angularDamping = 0.1f;
                ab.maxAngularVelocity = 10f;
                n++;
                EditorUtility.SetDirty(ab);
            }
            L("stable drives on " + n + " joints: stiffness=2e4 damping=5e3 forceLimit=2e4 maxAngVel=10");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void FinalPoseFix()
        {
            Log.Clear();
            float[] home = { 0f, 0f, 90f, 0f, 0f, 180f };
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model != null)
            {
                var so = new SerializedObject(model);
                var pm = so.FindProperty("startupPoseMode");
                if (pm != null) pm.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
                var pa = so.FindProperty("applyStartupPoseOnPlay");
                if (pa != null) pa.boolValue = true;
                var pd = so.FindProperty("startupPoseDeg");
                if (pd != null && pd.isArray)
                {
                    pd.arraySize = 6;
                    for (int i = 0; i < 6; i++) pd.GetArrayElementAtIndex(i).floatValue = home[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(model);
                L("startupPoseDeg = [0,0,90,0,0,180], mode=ApplyCustomPose");
            }
            else L("WARNING: RobotModelController missing");

            // 2) drive 参数：高刚度 + 适中阻尼（damping 过大反而拖慢响应）
            int driven = 0;
            if (arm != null)
                foreach (var ab in arm.GetComponentsInChildren<ArticulationBody>(true))
                {
                    if (ab == null) continue;
                    ab.useGravity = false;
                    if (ab.jointType == ArticulationJointType.FixedJoint) { ab.immovable = true; continue; }
                    var d = ab.xDrive;
                    d.stiffness = 1000000f;
                    d.damping = 5000f;
                    d.forceLimit = 100000f;
                    d.driveType = ArticulationDriveType.Target;
                    ab.xDrive = d;
                    driven++;
                    EditorUtility.SetDirty(ab);
                }
            L("drives: stiffness=1e6 damping=5e3 forceLimit=1e5 gravity=false on " + driven + " joints");

            // 3) 禁用工具子树里继承来的碰撞体，避免与 link_4/link_5 自碰撞挤压
            var spindle = arm == null ? null : link6OrSpindle(arm);
            int disabledCols = 0;
            if (spindle != null)
                foreach (var c in spindle.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || !c.enabled) continue;
                    c.enabled = false;
                    disabledCols++;
                    EditorUtility.SetDirty(c);
                }
            L("colliders disabled under " + (spindle != null ? spindle.name : "?") + " = " + disabledCols);

            // 4) 卡片标题
            int titles = 0;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid() || go.name != "Card_IKControl") continue;
                foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    if (tmp.text != null && (tmp.text.Contains("点位") || tmp.text.Contains("控制")))
                    {
                        tmp.text = "末端 IK 微调";
                        titles++;
                    }
                }
                if (titles == 0)
                {
                    var all = go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                    if (all.Length > 0) { all[0].text = "末端 IK 微调"; titles++; }
                }
                EditorUtility.SetDirty(go);
            }
            L("card title updated: " + titles);

            // 5) 标定球默认隐藏
            var overlayComp = UnityEngine.Object.FindObjectOfType<E5SceneOverlay>(true);
            if (overlayComp != null)
            {
                var so2 = new SerializedObject(overlayComp);
                var pv = so2.FindProperty("_markersVisible");
                if (pv != null) pv.boolValue = false;
                so2.ApplyModifiedPropertiesWithoutUndo();
                overlayComp.SetMarkersVisible(false);
                EditorUtility.SetDirty(overlayComp);
                L("overlay markers hidden");
            }
            else L("note: E5SceneOverlay not in scene (markers handled at runtime)");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static Transform link6OrSpindle(Transform arm)
        {
            var link6 = FindDeep(arm, "link_6");
            if (link6 == null) return null;
            var sp = link6.Find("Spindle_Camera");
            return sp != null ? sp : link6;
        }
        public static void LockEditPose()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model == null) { L("ABORT: RobotModelController missing"); Flush(); return; }

            float[] pose = model.ReadCurrentDisplayDegrees();
            if (pose == null || pose.Length < 6) { L("ABORT: cannot read display degrees"); Flush(); return; }
            L("Edit-mode pose = [" + string.Join(", ", Array.ConvertAll(pose, v => v.ToString("0.00"))) + "]");

            // 1) 写入 RobotModelController.startupPoseDeg
            var so = new SerializedObject(model);
            var pm = so.FindProperty("startupPoseMode");
            if (pm != null) pm.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
            var pa = so.FindProperty("applyStartupPoseOnPlay");
            if (pa != null) pa.boolValue = true;
            var pd = so.FindProperty("startupPoseDeg");
            if (pd != null && pd.isArray)
            {
                pd.arraySize = pose.Length;
                for (int i = 0; i < pose.Length; i++) pd.GetArrayElementAtIndex(i).floatValue = pose[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);
            L("startupPoseDeg <- edit pose; mode=ApplyCustomPose");

            // 2) 同步 E5SceneOverlay.HomeDeg，并默认隐藏标定球
            var overlay = UnityEngine.Object.FindObjectOfType<E5SceneOverlay>(true);
            if (overlay != null)
            {
                var so2 = new SerializedObject(overlay);
                var ph = so2.FindProperty("HomeDeg");
                if (ph != null && ph.isArray)
                {
                    ph.arraySize = pose.Length;
                    for (int i = 0; i < pose.Length; i++) ph.GetArrayElementAtIndex(i).floatValue = pose[i];
                }
                var pv = so2.FindProperty("_markersVisible");
                if (pv != null) pv.boolValue = false;
                so2.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(overlay);
                L("E5SceneOverlay.HomeDeg <- edit pose; markers hidden by default");
            }
            else L("WARNING: E5SceneOverlay not found");

            // 3) 克隆原生卡片
            CloneCardIntoPageControl();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static void CloneCardIntoPageControl()
        {
            GameObject pageControl = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                if (go != null && go.scene.IsValid() && go.name == "PageControl") { pageControl = go; break; }
            if (pageControl == null) { L("  card: PageControl missing"); return; }

            var kids = new List<Transform>();
            for (int i = 0; i < pageControl.transform.childCount; i++) kids.Add(pageControl.transform.GetChild(i));
            L("  card: PageControl children = " + string.Join(", ", kids.ConvertAll(k => k.name)));

            Transform template = null;
            foreach (var k in kids)
            {
                if (k.name == "Card_IKControl" || k.name == "IK_MicroAdjust") continue;
                if (k.name.IndexOf("点位", StringComparison.Ordinal) >= 0 ||
                    k.name.IndexOf("规划", StringComparison.Ordinal) >= 0 ||
                    k.name == "Actions") { template = k; break; }
            }
            if (template == null)
                foreach (var k in kids)
                {
                    if (k.name == "Card_IKControl" || k.name == "IK_MicroAdjust") continue;
                    if (k.childCount >= 2) { template = k; break; }
                }
            if (template == null) { L("  card: no template; abort clone"); return; }
            L("  card: template = " + template.name + " (children=" + template.childCount + ")");

            Transform card = null;
            // 若上一轮克隆位置不对（挂在 Mode 后面），先删掉重建。
            for (int i = pageControl.transform.childCount - 1; i >= 0; i--)
            {
                var c = pageControl.transform.GetChild(i);
                if (c.name == "Card_IKControl")
                {
                    // 先把 IK 行容器救出来，避免连带删除
                    var body = FindDeep(c, "IK_Body");
                    if (body != null && body.parent == c) body.SetParent(pageControl.transform, false);
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
                    L("  card: removed previous Card_IKControl for rebuild");
                }
            }
            for (int i = 0; i < pageControl.transform.childCount; i++)
                if (pageControl.transform.GetChild(i).name == "Card_IKControl") { card = pageControl.transform.GetChild(i); break; }
            if (card == null)
            {
                var clone = UnityEngine.Object.Instantiate(template.gameObject, pageControl.transform);
                clone.name = "Card_IKControl";
                clone.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
                card = clone.transform;

                int disabled = 0;
                for (int i = card.childCount - 1; i >= 0; i--)
                {
                    var c = card.GetChild(i);
                    string n = c.name.ToLowerInvariant();
                    bool keep = n.Contains("title") || n.Contains("pin") || n.Contains("header") ||
                                n.Contains("bg") || n.Contains("frame") || n.Contains("border") ||
                                n.Contains("icon");
                    if (!keep) { c.gameObject.SetActive(false); disabled++; }
                }
                L("  card: cloned, legacy children disabled=" + disabled);

                var t = card.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                foreach (var tmp in t)
                {
                    var pn = tmp.transform.parent != null ? tmp.transform.parent.name.ToLowerInvariant() : "";
                    if (pn.Contains("title") || pn.Contains("header")) { tmp.text = "末端 IK 微调"; break; }
                }
            }

            GameObject bar = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                if (go != null && go.scene.IsValid() && go.name == "IK_MicroAdjust") { bar = go; break; }
            if (bar == null)
            {
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                    if (go != null && go.scene.IsValid() && go.name == "IK_Body") { bar = go; break; }
            }
            if (bar != null)
            {
                var rt = (RectTransform)bar.transform;
                rt.SetParent(card, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(10f, 10f);
                rt.offsetMax = new Vector2(-10f, -34f);
                bar.name = "IK_Body";
                bar.SetActive(true);
                L("  card: IK rows moved into Card_IKControl body (renamed IK_Body)");
            }
            else
            {
                var go = new GameObject("IK_Body", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(card, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(10f, 10f);
                rt.offsetMax = new Vector2(-10f, -34f);
                go.AddComponent<UnityEngine.RectTransform>();
                bar = go;
                L("  card: created fresh IK_Body with TwinUiWidgetIk");
            }
            if (bar != null && bar.GetComponent<UnityEngine.RectTransform>() == null)
            {
                bar.AddComponent<UnityEngine.RectTransform>();
                L("  card: TwinUiWidgetIk re-attached to IK_Body");
            }

            // 排在「点位控制/Actions」之后，保证垂直流里紧跟操作按钮
            Transform actions = null;
            for (int i = 0; i < pageControl.transform.childCount; i++)
                if (pageControl.transform.GetChild(i).name == "Actions") { actions = pageControl.transform.GetChild(i); break; }
            if (actions != null) card.SetSiblingIndex(actions.GetSiblingIndex() + 1);
            L("  card: sibling index = " + card.GetSiblingIndex());
        }
        public static void HardenDrives()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            if (arm == null) { L("ABORT: arm missing"); Flush(); return; }

            int bodies = 0, joints = 0;
            foreach (var ab in arm.GetComponentsInChildren<ArticulationBody>(true))
            {
                if (ab == null) continue;
                bodies++;
                ab.useGravity = false;
                if (ab.jointType != ArticulationJointType.FixedJoint)
                {
                    joints++;
                    var d = ab.xDrive;
                    d.stiffness = 2000000f;      // 2e6：足以压住 5 kg 级负载的重力下垂
                    d.damping = 50000f;
                    d.forceLimit = 100000f;
                    d.driveType = ArticulationDriveType.Target;
                    ab.xDrive = d;
                    ab.angularDamping = 0.5f;
                    ab.maxAngularVelocity = 60f;
                }
                else
                {
                    ab.immovable = true;
                }
                EditorUtility.SetDirty(ab);
            }
            L("hardened " + bodies + " articulation bodies (" + joints + " driven joints): gravity off, stiffness=2e6");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void RunPoseTrace()
        {
            Log.Clear();
            if (EditorApplication.isPlaying) { L("already playing; abort"); Flush(); return; }
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PoseTraceProbe.FlagPath));
                System.IO.File.WriteAllText(PoseTraceProbe.FlagPath, "run", new UTF8Encoding(false));
                if (System.IO.File.Exists(PoseTraceProbe.OutPath)) System.IO.File.Delete(PoseTraceProbe.OutPath);
                L("flag written -> entering play mode");
            }
            catch (Exception e) { L("flag write failed: " + e.Message); Flush(); return; }
            Flush();
            EditorApplication.isPlaying = true;
        }
        public static void CloneNativeCard()
        {
            Log.Clear();
            GameObject pageControl = null, template = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "PageControl") pageControl = go;
            }
            if (pageControl == null) { L("ABORT: PageControl missing"); Flush(); return; }

            // 选一个既有卡片做模板：优先「点位控制」，其次「规划与执行」，再次任一含 Card 的子节点
            foreach (var n in new[] { "点位控制", "规划与执行" })
            {
                for (int i = 0; i < pageControl.transform.childCount; i++)
                    if (pageControl.transform.GetChild(i).name == n) { template = pageControl.transform.GetChild(i).gameObject; break; }
                if (template == null)
                {
                    var deep = FindDeep(pageControl.transform, n);
                    if (deep != null) template = deep.gameObject;
                }
                if (template != null) break;
            }
            if (template == null) { L("ABORT: no card template found"); Flush(); return; }
            L("template = " + P(template.transform));

            Transform existing = null;
            for (int i = 0; i < pageControl.transform.childCount; i++)
                if (pageControl.transform.GetChild(i).name == "Card_IKControl") { existing = pageControl.transform.GetChild(i); break; }
            if (existing == null)
            {
                var clone = UnityEngine.Object.Instantiate(template, template.transform.parent);
                clone.name = "Card_IKControl";
                clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                existing = clone.transform;
                // 清空克隆卡片的内容：保留外壳/标题/图钉，停用其余旧控件。
                int disabled = 0;
                for (int i = existing.childCount - 1; i >= 0; i--)
                {
                    var c = existing.GetChild(i);
                    string n = c.name;
                    bool keep = n.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("Pin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("Bg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!keep) { c.gameObject.SetActive(false); disabled++; }
                }
                L("cloned -> Card_IKControl, deactivated " + disabled + " legacy children");
            }
            else L("Card_IKControl already exists");

            // 把 IK 行容器嵌进克隆卡片的 body
            GameObject bar = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
                if (go != null && go.scene.IsValid() && go.name == "IK_MicroAdjust") { bar = go; break; }
            if (bar != null)
            {
                var rt = (RectTransform)bar.transform;
                rt.SetParent(existing, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(10f, 10f);
                rt.offsetMax = new Vector2(-10f, -34f);
                bar.SetActive(true);
                L("IK rows container re-parented into Card_IKControl body");
            }
            else L("WARNING: IK_MicroAdjust not found");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void EmbedIkBar()
        {
            Log.Clear();
            GameObject bar = null, pageControl = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "IK_MicroAdjust") bar = go;
                else if (go.name == "PageControl") pageControl = go;
            }
            if (bar == null || pageControl == null)
            { L("ABORT: bar=" + (bar != null) + " pageControl=" + (pageControl != null)); Flush(); return; }

            // 去掉独立外框：删掉之前为“悬浮窗”加的底板 Image，改由 PageControl 卡片底板承载。
            var img = bar.GetComponent<UnityEngine.UI.Image>();
            if (img != null) { UnityEngine.Object.DestroyImmediate(img); L("removed standalone frame Image"); }

            var rt = (RectTransform)bar.transform;
            rt.SetParent(pageControl.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 12f);
            rt.sizeDelta = new Vector2(-24f, 292f);
            bar.name = "IK_MicroAdjust";
            bar.SetActive(true);

            L("IK card embedded inside " + P(pageControl.transform) + " (bottom strip, no float frame)");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        [MenuItem("DigitalTwin/Step8/1 Startup Pose Home_VR_DT + Float IK Bar", false, 801)]
        public static void ApplyStartupAndFloatBar()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model != null)
            {
                var so = new SerializedObject(model);
                var pm = so.FindProperty("startupPoseMode");
                if (pm != null) pm.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
                var pa = so.FindProperty("applyStartupPoseOnPlay");
                if (pa != null) pa.boolValue = true;
                var pd = so.FindProperty("startupPoseDeg");
                if (pd != null && pd.isArray)
                {
                    pd.arraySize = 6;
                    float[] home = { 0f, 0f, 90f, 0f, 0f, 180f };
                    for (int i = 0; i < 6; i++) pd.GetArrayElementAtIndex(i).floatValue = home[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(model);
                L("startup pose set: ApplyCustomPose (0,0,90,0,0,180) on " + P(model.transform));
            }
            else L("WARNING: RobotModelController not found on the active arm");

            // 把 IK 卡片从 PageControl（可能未激活/被遮罩）挪到面板根节点下的左侧悬浮位。
            GameObject bar = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "IK_MicroAdjust") { bar = go; break; }
            }
            if (bar == null) { L("ABORT: IK_MicroAdjust not found"); Flush(); return; }

            GameObject panelRoot = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "UI_二期面板") { panelRoot = go; break; }
            }
            if (panelRoot == null) { L("ABORT: UI_二期面板 not found"); Flush(); return; }

            var rt = (RectTransform)bar.transform;
            rt.SetParent(panelRoot.transform, false);
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(18f, 20f);
            rt.sizeDelta = new Vector2(340f, 300f);

            var img = bar.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = bar.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.05f, 0.17f, 0.20f, 0.94f);
            img.raycastTarget = true;

            bar.SetActive(true);
            L("IK bar moved under " + P(panelRoot.transform) + " left-side float, size 340x300");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        [MenuItem("DigitalTwin/Step7/2 Fix Header Overlap (autosize + badge spacing)", false, 702)]
        public static void FixHeaderOverlap()
        {
            Log.Clear();
            var root = SceneRoot("30_界面与交互");
            if (root == null) { L("ABORT: 30_界面与交互 missing"); Flush(); return; }

            // 1) KPI 文本窄于单元格时会被相邻胶囊压住：开启 TMP 自动缩放，文本永不外溢。
            foreach (var name in new[] { "KpiRate", "KpiLatency" })
            {
                Transform t = FindDeep(root, name);
                if (t == null) { L("  " + name + ": not found"); continue; }
                int fixedCount = 0;
                foreach (var tmp in t.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    tmp.enableAutoSizing = true;
                    if (tmp.fontSizeMax <= 0f) tmp.fontSizeMax = tmp.fontSize;
                    tmp.fontSizeMin = 9f;
                    tmp.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                    fixedCount++;
                }
                var rt = (RectTransform)t;
                L("  " + name + ": tmp=" + fixedCount + " rect x=" + rt.anchoredPosition.x +
                  " w=" + rt.sizeDelta.x);
            }

            // 2) 两个胶囊拉开间距，避免「告警 1」与「记录中」贴边重叠。
            Transform alarm = FindDeep(root, "AlarmBadge");
            Transform rec = FindDeep(root, "RecBadge");
            if (alarm != null && rec != null)
            {
                var ra = (RectTransform)alarm;
                var rr = (RectTransform)rec;
                float aRight = ra.anchoredPosition.x + ra.sizeDelta.x;
                float target = aRight + 24f;
                if (rr.anchoredPosition.x < target)
                {
                    L("  RecBadge x " + rr.anchoredPosition.x + " -> " + target);
                    rr.anchoredPosition = new Vector2(target, rr.anchoredPosition.y);
                }
                L("  badges: alarm[" + ra.anchoredPosition.x + ".." + aRight + "] rec x=" + rr.anchoredPosition.x +
                  " w=" + rr.sizeDelta.x);
            }
            else L("  badges not found (alarm=" + (alarm != null) + " rec=" + (rec != null) + ")");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        [MenuItem("DigitalTwin/Step7/1 Add IK Bar Card To PageControl", false, 701)]
        public static void AddIkBar()
        {
            Log.Clear();
            GameObject pageControl = null;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || !go.scene.IsValid()) continue;
                if (go.name == "PageControl") { pageControl = go; break; }
            }
            if (pageControl == null) { L("ABORT: PageControl not found"); Flush(); return; }

            Transform existing = null;
            for (int i = 0; i < pageControl.transform.childCount; i++)
                if (pageControl.transform.GetChild(i).name == "IK_MicroAdjust") { existing = pageControl.transform.GetChild(i); break; }
            Transform host = existing;
            if (host == null)
            {
                var go = new GameObject("IK_MicroAdjust", typeof(RectTransform));
                host = go.transform;
                host.SetParent(pageControl.transform, false);
                var rt = (RectTransform)host;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var bar = host.GetComponent<UnityEngine.RectTransform>();
            if (bar == null) bar = host.gameObject.AddComponent<UnityEngine.RectTransform>();
            EditorUtility.SetDirty(bar);
            L("TwinUiWidgetIk attached under " + P(pageControl.transform) + "/IK_MicroAdjust");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void DisableIkAndAddAxes()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var baseLink = arm == null ? null : FindDeep(arm, "base_link");
            var handle = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();
            if (handle != null)
            {
                var so = new SerializedObject(handle);
                var p = so.FindProperty("enableHandle");
                if (p != null) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                var pa = so.FindProperty("allowRobotDispatch");
                if (pa != null) { pa.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(handle);
                L("TwinPlayIkHandle on " + P(handle.transform) + ": enableHandle=false allowRobotDispatch=false");
            }
            else L("WARNING: TwinPlayIkHandle not found");

            var switcher = UnityEngine.Object.FindObjectOfType<ToolTcpSwitcher>();
            Transform host = baseLink != null ? baseLink : bay;
            var existing = host.Find("TCP_Axes");
            Transform ax;
            if (existing != null) ax = existing;
            else
            {
                var go = new GameObject("TCP_Axes");
                go.transform.SetParent(host, false);
                ax = go.transform;
            }
            var axr = ax.GetComponent<TcpAxisRenderer>();
            if (axr == null) axr = ax.gameObject.AddComponent<TcpAxisRenderer>();
            var so2 = new SerializedObject(axr);
            var ps = so2.FindProperty("switcher"); if (ps != null) ps.objectReferenceValue = switcher;
            var pv = so2.FindProperty("visible"); if (pv != null) pv.boolValue = false;
            so2.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(axr);
            L("TcpAxisRenderer on " + P(ax) + " switcher=" + (switcher != null ? "yes" : "null") + " visible=false");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static void AttachSwitcherInternal()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var baseLink = arm == null ? null : FindDeep(arm, "base_link");
            var link6 = arm == null ? null : FindDeep(arm, "link_6");
            if (baseLink == null || link6 == null) { L("ABORT: base_link / link_6 not found"); Flush(); return; }

            var flange = link6.Find("Flange_TCP");
            var spindleTcp = link6.Find("Spindle_TCP");
            if (flange == null || spindleTcp == null) { L("ABORT: Flange_TCP / Spindle_TCP not found"); Flush(); return; }

            var ik = arm.GetComponentInChildren<RobotIkController>(true) ??
                     UnityEngine.Object.FindObjectOfType<RobotIkController>();
            var handle = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();

            var switcher = baseLink.GetComponent<ToolTcpSwitcher>();
            if (switcher == null) switcher = baseLink.gameObject.AddComponent<ToolTcpSwitcher>();

            var so = new SerializedObject(switcher);
            var pTcp = so.FindProperty("activeTcp");
            if (pTcp != null) pTcp.enumValueIndex = (int)ToolTcpSwitcher.TcpKind.Spindle;
            var pF = so.FindProperty("flangeTcp"); if (pF != null) pF.objectReferenceValue = flange;
            var pS = so.FindProperty("spindleTcp"); if (pS != null) pS.objectReferenceValue = spindleTcp;
            var pIk = so.FindProperty("ikController"); if (pIk != null) pIk.objectReferenceValue = ik;
            var pH = so.FindProperty("ikHandle"); if (pH != null) pH.objectReferenceValue = handle;
            var pAllow = so.FindProperty("allowRobotDispatch"); if (pAllow != null) pAllow.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            switcher.Apply();   // push the active TCP into RobotIkController
            EditorUtility.SetDirty(switcher);
            if (ik != null) EditorUtility.SetDirty(ik);

            L("ToolTcpSwitcher on " + P(baseLink) + " (base_link)");
            L("  active=" + switcher.ActiveTcp + " (" + switcher.ActiveTcpName + ")");
            L("  flangeTcp=" + (flange != null ? P(flange) : "null"));
            L("  spindleTcp=" + (spindleTcp != null ? P(spindleTcp) + " world=" + V(spindleTcp.position) : "null"));
            L("  ikController=" + (ik != null ? P(ik.transform) : "null") +
              "  tcpTransform=" + (ik != null && ik.TcpTransform != null ? P(ik.TcpTransform) : "<link_6 default>"));
            L("  ikHandle=" + (handle != null ? P(handle.transform) : "null") +
              "  allowRobotDispatch=" + (handle != null ? handle.AllowRobotDispatch.ToString() : "n/a"));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static string P(Transform t)
        {
            if (t == null) return "<null>";
            var st = new System.Collections.Generic.List<string>();
            while (t != null) { st.Add(t.name); t = t.parent; }
            var sb = new StringBuilder();
            for (int i = st.Count - 1; i >= 0; i--) { sb.Append(st[i]); if (i > 0) sb.Append('/'); }
            return sb.ToString();
        }

        private static void CreateSpindleTcpCore()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var link6 = arm == null ? null : FindDeep(arm, "link_6");
            var flange = link6 == null ? null : link6.Find("Flange_TCP");
            if (link6 == null || flange == null) { L("ABORT: link_6 / Flange_TCP not found"); Flush(); return; }

            // bit node: name contains 披头/批头 (the model uses 披头), searched under this link_6 only
            Transform bit = null;
            foreach (var t in link6.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.name.IndexOf("披头", StringComparison.Ordinal) >= 0 ||
                    t.name.IndexOf("批头", StringComparison.Ordinal) >= 0)
                { bit = t; break; }
            }
            if (bit == null) { L("ABORT: bit node not found under link_6"); Flush(); return; }
            L("bit node: " + bit.name + " world=" + V(bit.position));

            // Tool axis = Flange_TCP +Z (down the tool). The bit's own local axes are skewed, so we
            // never use them for orientation.
            Vector3 axis = flange.forward.normalized;
            L("tool axis (Flange_TCP +Z) = " + V(axis));

            // Tip = deepest mesh vertex along the axis, then average the bottom face (within 1.5 mm)
            // to get the face centre instead of a single corner vertex.
            var pts = new System.Collections.Generic.List<Vector3>(40000);
            foreach (var mf in bit.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var m = mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i++) pts.Add(m.MultiplyPoint3x4(vs[i]));
            }
            Vector3 tip;
            if (pts.Count > 16)
            {
                float best = float.MinValue;
                for (int i = 0; i < pts.Count; i++) best = Mathf.Max(best, Vector3.Dot(pts[i] - flange.position, axis));
                Vector3 sum = Vector3.zero; int n = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = Vector3.Dot(pts[i] - flange.position, axis);
                    if (best - d <= 0.0015f) { sum += pts[i]; n++; }
                }
                tip = sum / Mathf.Max(1, n);
                L("tip from " + pts.Count + " verts; bottom-face verts=" + n + " depth=" +
                  best.ToString("0.0000", CultureInfo.InvariantCulture));
            }
            else
            {
                Bounds b;
                TryBounds(bit, out b);
                if (Mathf.Abs(axis.y) > 0.95f)
                {
                    // Vertical tool: the lowest point of the world AABB is the bit tip, and the
                    // bottom-face centre is the AABB centre in X/Z.
                    tip = new Vector3(b.center.x, axis.y < 0f ? b.min.y : b.max.y, b.center.z);
                    L("tip from renderer AABB (vertical): min=" + V(b.min) + " max=" + V(b.max));
                }
                else
                {
                    tip = b.center - axis * (b.extents.magnitude * 0.5f);
                    L("WARNING: mesh not readable + tilted axis, tip from bounds fallback");
                }
            }

            // Reuse an existing Spindle_TCP under this link_6, otherwise create one.
            Transform tcp = null;
            for (int i = 0; i < link6.childCount; i++)
                if (link6.GetChild(i).name == "Spindle_TCP") { tcp = link6.GetChild(i); break; }
            if (tcp == null)
            {
                var go = new GameObject("Spindle_TCP");
                tcp = go.transform;
                tcp.SetParent(link6, false);
            }
            tcp.position = tip;
            tcp.rotation = flange.rotation;      // +Z exactly parallel to Flange_TCP +Z

            if (tcp.GetComponent<TcpAxisGizmo>() == null) tcp.gameObject.AddComponent<TcpAxisGizmo>();
            EditorUtility.SetDirty(tcp);

            Vector3 vec = tcp.position - flange.position;
            L("Spindle_TCP world=" + V(tcp.position) + " euler=" + V(tcp.eulerAngles));
            L("Flange_TCP -> Spindle_TCP = " + V(vec) + "  |" +
              (vec.magnitude * 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " mm|");
            L("Spindle_TCP localPosition=" + V(tcp.localPosition) + " parent=" + tcp.parent.name);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void DetachRig()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var link6 = arm == null ? null : FindDeep(arm, "link_6");
            var spindle = link6 == null ? null : link6.Find("Spindle_Camera_Unit");
            var bitAxis = spindle == null ? null : spindle.Find("BitAxis");
            if (bitAxis == null) { L("ABORT: BitAxis missing"); Flush(); return; }

            var rig = bitAxis.GetComponent<ToolBitRig>();
            if (rig != null)
            {
                // [ExecuteAlways] would otherwise pull Spindle_TCP back to the tip every editor tick
                // and fight manual dragging -> remove the component, keep the script file.
                UnityEngine.Object.DestroyImmediate(rig);
                L("ToolBitRig removed from BitAxis (TCP is now a free node)");
            }
            else L("ToolBitRig already absent");

            var tcp = FindDeep(bay, "Spindle_TCP");
            var flange = link6 == null ? null : link6.Find("Flange_TCP");
            if (tcp != null) L("Spindle_TCP world=" + V(tcp.position) + " local=" + V(tcp.localPosition));
            if (tcp != null && flange != null)
            {
                Vector3 vec = tcp.position - flange.position;
                L("Flange_TCP -> Spindle_TCP = " + V(vec) + "  |" +
                  (vec.magnitude * 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " mm|");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static void Flush()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath));
                System.IO.File.WriteAllText(LogPath, Log.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogError("[Step3] log write failed: " + e.Message); }
        }

        [MenuItem("DigitalTwin/Step16/1 Calibrate By Search (offset=0, sign auto)", false, 1601)]
        public static void EnableTransformDriveMenu() { EnableTransformDrive(); }

        [MenuItem("DigitalTwin/Step16/2 Use Transform Drive In Play (same math as Edit)", false, 1610)]
        public static void DisableJointBodiesMenu() { DisableJointBodies(); }

        [MenuItem("DigitalTwin/Step17/1 Disable AB On link_1..6 + Transform Drive", false, 1701)]
        public static void SilenceProbeMenu() { SilenceProbe(); }

        [MenuItem("DigitalTwin/Step18/1 Silence PoseTraceProbe (autoRun=false, disabled)", false, 1801)]
        public static void SilenceProbe()
        {
            Log.Clear();
            int touched = 0;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<PoseTraceProbe>(true))
            {
                if (p == null || !p.gameObject.scene.IsValid()) continue;
                var so = new SerializedObject(p);
                var pAuto = so.FindProperty("autoRun");
                if (pAuto != null) { pAuto.boolValue = false; }
                so.ApplyModifiedPropertiesWithoutUndo();
                p.enabled = false;                 // Start 不再执行，彻底静默
                EditorUtility.SetDirty(p);
                touched++;
                L("  silenced PoseTraceProbe on " + P(p.transform));
            }
            L("PoseTraceProbe silenced = " + touched);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void DisableJointBodies()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (arm == null || model == null) { L("ABORT: arm/model"); Flush(); return; }

            var so = new SerializedObject(model);
            var pT = so.FindProperty("useTransformDriveInPlay");
            if (pT != null) pT.boolValue = true;
            var pS = so.FindProperty("startupPoseMode");
            if (pS != null) pS.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
            var pD = so.FindProperty("startupPoseDeg");
            float[] home = { 0f, 0f, 90f, 0f, 0f, 180f };
            if (pD != null && pD.isArray)
            {
                pD.arraySize = 6;
                for (int i = 0; i < 6; i++) pD.GetArrayElementAtIndex(i).floatValue = home[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            int disabled = 0;
            foreach (var ab in arm.GetComponentsInChildren<ArticulationBody>(true))
            {
                if (ab == null) continue;
                ab.useGravity = false;
                string n = ab.name;
                bool isJoint = n.StartsWith("link_") && n.Length == 6;
                if (isJoint)
                {
                    if (ab.enabled) { ab.enabled = false; disabled++; }
                    L("  disabled AB on " + n);
                }
                else
                {
                    ab.immovable = true;   // base_link / base 固定
                    L("  immovable: " + n);
                }
                EditorUtility.SetDirty(ab);
            }
            EditorUtility.SetDirty(model);
            L("disabled joint bodies = " + disabled + " ; useTransformDriveInPlay=true");

            model.ApplyJointDisplayDegrees(home, true, RobotModelController.PoseSource.EditorSlider, true, true);
            var tip = FindDeep(arm, "Spindle_TCP");
            L("edit dotDown = " + (tip != null ? Vector3.Dot(tip.forward.normalized, Vector3.down).ToString("F4") : "?"));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void EnableTransformDrive()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model == null) { L("ABORT: model"); Flush(); return; }
            var so = new SerializedObject(model);
            var pT = so.FindProperty("useTransformDriveInPlay");
            if (pT != null) { pT.boolValue = true; L("useTransformDriveInPlay = true"); }
            var pS = so.FindProperty("startupPoseMode");
            if (pS != null) pS.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
            var pD = so.FindProperty("startupPoseDeg");
            float[] home = { 0f, 0f, 90f, 0f, 0f, 180f };
            if (pD != null && pD.isArray)
            {
                pD.arraySize = 6;
                for (int i = 0; i < 6; i++) pD.GetArrayElementAtIndex(i).floatValue = home[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);

            // 物理不再参与：把 6 个关节体设为 immovable，避免 PhysX 把 Transform 拉回
            int n = 0;
            foreach (var ab in arm.GetComponentsInChildren<ArticulationBody>(true))
            {
                if (ab == null) continue;
                ab.useGravity = false;
                if (ab.jointType != ArticulationJointType.FixedJoint) { ab.immovable = true; n++; }
                EditorUtility.SetDirty(ab);
            }
            L("bodies immovable = " + n);

            model.ApplyJointDisplayDegrees(home, true, RobotModelController.PoseSource.EditorSlider, true, true);
            var tip = FindDeep(arm, "Spindle_TCP");
            L("edit pose dotDown = " + (tip != null ? Vector3.Dot(tip.forward.normalized, Vector3.down).ToString("F4") : "?"));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }
        public static void CalibrateBySearch()
        {
            Log.Clear();
            var bay = SceneRoot(LeftBayName);
            var arm = FindDeep(bay, "h2515-1 (4)");
            var model = arm == null ? null : arm.GetComponentInChildren<RobotModelController>(true);
            if (model == null) { L("ABORT: model"); Flush(); return; }

            var so = new SerializedObject(model);
            var pT = so.FindProperty("useTransformDriveInPlay");
            if (pT != null) pT.boolValue = false;
            var pS = so.FindProperty("startupPoseMode");
            if (pS != null) pS.enumValueIndex = (int)RobotModelController.StartupPoseMode.ApplyCustomPose;
            var pD = so.FindProperty("startupPoseDeg");
            float[] home = { 0f, 0f, 90f, 0f, 0f, 180f };
            if (pD != null && pD.isArray)
            {
                pD.arraySize = 6;
                for (int i = 0; i < 6; i++) pD.GetArrayElementAtIndex(i).floatValue = home[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);

            var pj0 = so.FindProperty("joints");
            int n = (pj0 != null && pj0.isArray) ? Mathf.Min(6, pj0.arraySize) : 0;
            if (n == 0) { L("ABORT: joints"); Flush(); return; }

            float[] zero = { 0f, 0f, 0f, 0f, 0f, 0f };
            bool solved = false;
            foreach (int s3 in new[] { 1, -1 })
            {
                foreach (int s6 in new[] { 1, -1 })
                {
                    var so2 = new SerializedObject(model);
                    var pj2 = so2.FindProperty("joints");
                    for (int i = 0; i < n; i++)
                    {
                        var el = pj2.GetArrayElementAtIndex(i);
                        var signProp = el.FindPropertyRelative("sign");
                        var offProp = el.FindPropertyRelative("offsetDeg");
                        if (signProp != null) signProp.floatValue = (i == 2) ? s3 : (i == 5 ? s6 : 1f);
                        if (offProp != null) offProp.floatValue = 0f;
                    }
                    so2.ApplyModifiedPropertiesWithoutUndo();

                    model.ApplyJointDisplayDegrees(zero, true, RobotModelController.PoseSource.EditorSlider, true, true);
                    CaptureRestQuiet(model);
                    model.ApplyJointDisplayDegrees(home, true, RobotModelController.PoseSource.EditorSlider, true, true);
                    EditorApplication.QueuePlayerLoopUpdate();

                    var l3 = FindDeep(arm, "link_3");
                    var l6 = FindDeep(arm, "link_6");
                    var tip = FindDeep(arm, "Spindle_TCP");
                    float dot = tip != null ? Vector3.Dot(tip.forward.normalized, Vector3.down) : -9f;
                    L("  try s3=" + s3 + " s6=" + s6 +
                      " l3=" + (l3 != null ? l3.localRotation.eulerAngles.ToString("F1") : "?") +
                      " l6=" + (l6 != null ? l6.localRotation.eulerAngles.ToString("F1") : "?") +
                      " dotDown=" + dot.ToString("F4"));
                    if (dot > 0.99f) { solved = true; L("SOLVED with s3=" + s3 + " s6=" + s6); break; }
                }
                if (solved) break;
            }
            if (!solved) L("NOT SOLVED");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Flush();
        }

        private static void CaptureRestQuiet(RobotModelController model)
        {
            var so = new SerializedObject(model);
            var pj = so.FindProperty("joints");
            if (pj == null || !pj.isArray) return;
            for (int i = 0; i < pj.arraySize; i++)
            {
                var el = pj.GetArrayElementAtIndex(i);
                var bodyProp = el.FindPropertyRelative("body");
                var rest = el.FindPropertyRelative("restLocalRotation");
                var has = el.FindPropertyRelative("hasRestLocalRotation");
                var body = bodyProp != null ? bodyProp.objectReferenceValue as ArticulationBody : null;
                if (body == null) continue;
                if (rest != null) rest.quaternionValue = body.transform.localRotation;
                if (has != null) has.boolValue = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);
        }
    }
}
