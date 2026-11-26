// ProcessWaypoints.cs —— 三点位（Home_VR_DT / Safty_1 / Screw_10）标定关节角 + Spindle_TCP FK 缓存。
//
// 数据源与 E5SceneOverlay 一致：`Assets/DoosanDT/10_DigitalTwin/Config/unity_scene_calibration_20261125.json`
// 的 `q_deg`（真机 E-Calib 实测，度）；缺文件时退回硬编码（与 JSON 逐位相同）。
//
// 用途：Ghost 拖拽的双阈值滞回磁吸（ProcessStepGate 只管工艺，不管几何），以及「执行」时的精确目标。
// 只读：不改任何模型、不发任何消息。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DigitalTwin
{
    public static class ProcessWaypoints
    {
        public struct Waypoint
        {
            public string Name;
            public float[] JointDeg;     // 真机标定关节角（度）
            public Vector3 WorldPos;     // 该位姿下 Spindle_TCP 的 FK 世界坐标
        }

        public const string CalibRelativePath = "DoosanDT/10_DigitalTwin/Config/unity_scene_calibration_20261125.json";

        private static readonly (string name, float[] q)[] Fallback =
        {
            ("Home_VR_DT", new[] { 0f, 0f, 90f, 0f, 0f, 180f }),
            ("Safty_1", new[] { -15.8607f, 21.6713f, 73.4800f, -0.6000f, -5.1600f, 180.5903f }),
            ("Screw_10", new[] { -15.8593f, 22.2034f, 77.6020f, -0.3483f, -9.8043f, 180.3430f })
        };

        private static readonly List<Waypoint> Items = new List<Waypoint>();

        public static IReadOnlyList<Waypoint> All => Items;
        public static bool Ready { get; private set; }

        /// <summary>加载标定角并算出三个 FK 落点（幂等；模型/TCP 变化时可用 force 重算）。</summary>
        public static bool Ensure(RobotModelController model, bool force = false)
        {
            if (Ready && !force) return Items.Count > 0;
            Items.Clear();
            Ready = false;

            if (model == null) return false;

            Transform tcp = FindChildByName(model.transform, "Spindle_TCP");
            if (tcp == null) tcp = model.GetDefaultTcpTransform();
            if (tcp == null) return false;

            foreach (var pair in LoadPoses())
            {
                Vector3 pos;
                Quaternion rot;
                if (!model.EvaluateTcpPoseDegrees(pair.q, tcp, Vector3.zero, Vector3.zero, out pos, out rot)) continue;
                Items.Add(new Waypoint { Name = pair.name, JointDeg = (float[])pair.q.Clone(), WorldPos = pos });
            }

            Ready = Items.Count > 0;
            Debug.Log("[WAYPOINT] loaded " + Items.Count + " waypoints:" +
                      (Items.Count > 0 ? " " + Items[0].Name + " …" : string.Empty));
            return Ready;
        }

        public static bool TryGet(string name, out Waypoint waypoint)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Name == name) { waypoint = Items[i]; return true; }
            }
            waypoint = default(Waypoint);
            return false;
        }

        /// <summary>最近标定点（返回距离，米）。</summary>
        public static bool TryFindNearest(Vector3 worldPos, out Waypoint waypoint, out float distance)
        {
            waypoint = default(Waypoint);
            distance = float.MaxValue;
            bool any = false;
            for (int i = 0; i < Items.Count; i++)
            {
                float d = Vector3.Distance(worldPos, Items[i].WorldPos);
                if (d < distance) { distance = d; waypoint = Items[i]; any = true; }
            }
            return any;
        }

        private static List<(string name, float[] q)> LoadPoses()
        {
            var list = new List<(string, float[])>();
            string path = Path.Combine(Application.dataPath, CalibRelativePath);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    int p = 0;
                    while (true)
                    {
                        int n = json.IndexOf("\"name\"", p, StringComparison.Ordinal);
                        if (n < 0) break;
                        int colon = json.IndexOf(':', n);
                        int q1 = json.IndexOf('"', colon);
                        int q2 = json.IndexOf('"', q1 + 1);
                        if (q1 < 0 || q2 < 0) break;
                        string nm = json.Substring(q1 + 1, q2 - q1 - 1);

                        int qk = json.IndexOf("\"q_deg\"", q2, StringComparison.Ordinal);
                        int nextName = json.IndexOf("\"name\"", q2, StringComparison.Ordinal);
                        if (qk >= 0 && (nextName < 0 || qk < nextName))
                        {
                            int lb = json.IndexOf('[', qk);
                            int rb = json.IndexOf(']', lb);
                            if (lb > 0 && rb > lb)
                            {
                                string[] parts = json.Substring(lb + 1, rb - lb - 1).Split(',');
                                var v = new float[parts.Length];
                                for (int k = 0; k < parts.Length; k++)
                                    float.TryParse(parts[k].Trim(), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out v[k]);
                                string key = nm == "Screw_10_hover" ? "Screw_10" : nm;
                                if (v.Length >= 6 && (key == "Home_VR_DT" || key == "Safty_1" || key == "Screw_10") &&
                                    FindIndex(list, key) < 0)
                                    list.Add((key, v));
                            }
                        }
                        p = q2;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[WAYPOINT] calibration parse failed: " + e.Message);
                }
            }

            if (list.Count < 3)
            {
                list.Clear();
                foreach (var f in Fallback) list.Add(f);
                Debug.LogWarning("[WAYPOINT] calibration incomplete -> hardcoded q_deg fallback");
            }
            return list;
        }

        private static int FindIndex(List<(string name, float[] q)> list, string name)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].name == name) return i;
            return -1;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindChildByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}