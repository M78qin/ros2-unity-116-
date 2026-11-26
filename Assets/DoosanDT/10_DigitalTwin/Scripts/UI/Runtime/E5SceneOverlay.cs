using System;
using System.Collections.Generic;
using System.IO;
using DigitalTwin;
using UnityEngine;

namespace DoosanDT.UI.Runtime
{
    /// <summary>
    /// E5 scene overlay: one-click Home_VR_DT preview pose + glowing waypoint markers.
    ///
    /// 2026-11-26 修订（点位与真机对齐）：
    ///   标记不再用"Home 锚点 + 标定 TCP 增量"的近似换算，而是**直接对三组真机关节角做正运动学**
    ///   （`RobotModelController.EvaluateTcpPoseDegrees`，TCP = `Spindle_TCP`），
    ///   因此标记位置 ≡ 该位姿下批头尖端的 FK 落点，回放/切位姿时误差为 0（仅浮点误差）。
    ///   关节角来源：`DoosanDT/10_DigitalTwin/Config/unity_scene_calibration_20261125.json` 的 `q_deg`
    ///   （E-Calib 真机实测，单位度）：Home_VR_DT (0,0,90,0,0,180)、
    ///   Safty_1 (-15.8607,21.6713,73.48,-0.6,-5.16,180.5903)、
    ///   Screw_10_hover (-15.8593,22.2034,77.602,-0.3483,-9.8043,180.343)。
    ///
    /// 纯本地/视觉：从不发布机器人命令。
    /// </summary>
    public sealed class E5SceneOverlay : MonoBehaviour
    {
        private static E5SceneOverlay _instance;

        private static readonly float[] HomeDeg = { 0f, 0f, 90f, 0f, 0f, 180f };

        /// <summary>标定 JSON 缺失时的兜底（与 unity_scene_calibration_20261125.json 的 q_deg 一致）。</summary>
        private static readonly (string name, float[] q)[] FallbackPoses =
        {
            ("Home_VR_DT", new[] { 0f, 0f, 90f, 0f, 0f, 180f }),
            ("Safty_1", new[] { -15.8607f, 21.6713f, 73.4800f, -0.6000f, -5.1600f, 180.5903f }),
            ("Screw_10", new[] { -15.8593f, 22.2034f, 77.6020f, -0.3483f, -9.8043f, 180.3430f })
        };

        // ---- 标注尺寸（工程标注尺度：球 ⌀14 mm / 环 R22 mm / 细线 / 低不透明度）----
        private const float SphereDiameter = 0.014f;
        private const float RingRadius = 0.022f;
        private const float RingWidth = 0.0016f;
        private const float SphereAlpha = 0.60f;
        private const float RingAlpha = 0.42f;
        private const float LabelCharacterSize = 0.0065f;
        private const int RingSegments = 56;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("[E5SceneOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<E5SceneOverlay>();
        }

        private RobotModelController _robot;
        private Transform _spindleTcp;
        private GameObject _markerRoot;

        /// <summary>左下角原生「场景标定」灰框：默认关闭，功能已并入二期面板卡片。</summary>
        public static bool ShowPanel = false;

        /// <summary>点位标注（Home / Safty_1 / Screw_10）显隐。</summary>
        public bool MarkersVisible => _markersVisible;

        public void SetMarkersVisible(bool value)
        {
            _markersVisible = value;
            if (_markerRoot != null) _markerRoot.SetActive(value);
        }

        public void ToggleMarkers() => SetMarkersVisible(!MarkersVisible);

        /// <summary>供二期面板卡片调用：应用 Home_VR_DT 预览姿态（仅本地视觉）。</summary>
        public void ApplyHomePose()
        {
            if (_robot == null) _robot = FindActiveRobot();
            ApplyHomePreview();
        }

        private bool _markersVisible = true;
        private bool _poseApplied;
        private float _bootAt;
        private string _hint = "仅本地视觉预览，不下发真机指令";

        [Serializable]
        private class CalibPoint
        {
            public string name;
            public float[] q_deg;
            public float[] commanded;
            public float[] tcp_mm_deg;
        }

        [Serializable]
        private class CalibFile
        {
            public CalibPoint[] points;
        }

        private void Start()
        {
            _bootAt = Time.unscaledTime;
            _robot = FindActiveRobot();
            BuildMarkers();
        }

        private void Update()
        {
            if (!_poseApplied && Time.unscaledTime - _bootAt > 0.5f)
            {
                if (_robot == null)
                {
                    _robot = FindActiveRobot();
                }
                if (_robot != null)
                {
                    ApplyHomePreview();
                    _poseApplied = true;
                }
                else if (Time.unscaledTime - _bootAt > 5f)
                {
                    _poseApplied = true;
                }
            }
        }

        private float[] ResolveHomeDeg()
        {
            if (HomeDeg != null && HomeDeg.Length > 0) return HomeDeg;
            float[] fallback = _robot != null ? _robot.StartupPoseDeg : null;
            if (fallback != null && fallback.Length > 0) return fallback;
            return new float[] { 0f, 0f, 90f, 0f, 0f, 180f };
        }

        private void ApplyHomePreview()
        {
            try
            {
                float[] pose = ResolveHomeDeg();
                _robot.SetRuntimeHome0Degrees(pose);
                var previous = _robot.CurrentControlAuthority;
                _robot.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
                bool ok = _robot.ApplyJointDegrees(pose,
                    RobotModelController.PoseSource.CommandPreview, false);
                _robot.SetControlAuthority(previous);
                _hint = ok ? "本地姿态已复位 Home_VR_DT（仅视觉）" : "姿态复位被拒绝（查看控制台）";
                Debug.Log("[E5Overlay] Home_VR_DT applied=" + ok + " pose=[" +
                          string.Join(", ", System.Array.ConvertAll(pose, v => v.ToString("0.0"))) +
                          "] (local preview only)");
            }
            catch (Exception ex)
            {
                _hint = "姿态复位异常: " + ex.Message;
                Debug.LogWarning("[E5Overlay] home apply failed: " + ex.Message);
            }
        }

        // ============================================================ 标记构建

        private void BuildMarkers()
        {
            if (_robot == null) _robot = FindActiveRobot();
            if (_robot == null)
            {
                Debug.LogWarning("[E5Overlay] no RobotModelController -> markers skipped");
                return;
            }

            _spindleTcp = FindChildByName(_robot.transform, "Spindle_TCP");
            if (_spindleTcp == null)
            {
                Debug.LogWarning("[E5Overlay] Spindle_TCP missing under " + _robot.gameObject.name + " -> markers skipped");
                return;
            }

            List<(string name, float[] q)> poses = LoadPoses();

            // 世界坐标系下的标注根：环面保持水平，不随 base_link 的本地旋转倾斜。
            _markerRoot = new GameObject("E5WaypointMarkers");
            _markerRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            int made = 0;
            foreach (var p in poses)
            {
                if (!_robot.EvaluateTcpPoseDegrees(p.q, _spindleTcp, Vector3.zero, Vector3.zero,
                        out Vector3 world, out _))
                {
                    Debug.LogWarning("[E5Overlay] FK failed for " + p.name);
                    continue;
                }

                Color c = p.name == "Home_VR_DT" ? new Color(0.20f, 0.90f, 0.45f)
                        : p.name == "Safty_1" ? new Color(0.25f, 0.65f, 1f)
                        : new Color(1f, 0.55f, 0.15f);
                Vector3 labelOffset = p.name == "Safty_1" ? new Vector3(0.045f, 0.030f, 0f)
                                    : p.name == "Screw_10" ? new Vector3(-0.045f, -0.030f, 0f)
                                    : new Vector3(0.040f, 0.028f, 0f);
                MakeMarker(p.name, world, c, labelOffset);
                made++;
                Debug.Log("[E5Overlay] marker " + p.name.PadRight(12) + " FK=" +
                          world.ToString("F4") + "  q=[" + Join(p.q) + "]");
            }

            Debug.Log("[E5Overlay] waypoint markers=" + made + " (FK of Spindle_TCP, world-space)");
            if (_markerRoot != null) _markerRoot.SetActive(_markersVisible);
        }

        private void MakeMarker(string label, Vector3 world, Color color, Vector3 labelOffset)
        {
            Transform host = _markerRoot.transform;

            // ---- 中心球（⌀14 mm，半透明）----
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "WP_" + label;
            sphere.transform.SetParent(host, false);
            sphere.transform.position = world;
            sphere.transform.localScale = Vector3.one * SphereDiameter;
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var smr = sphere.GetComponent<MeshRenderer>();
            if (smr != null)
            {
                smr.sharedMaterial = TranslucentMaterial(color, SphereAlpha);
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
            }

            // ---- 光环（细线圆，低不透明度）----
            var ringGo = new GameObject("RING_" + label);
            ringGo.transform.SetParent(host, false);
            ringGo.transform.position = world;
            var lr = ringGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = RingSegments;
            lr.startWidth = RingWidth;
            lr.endWidth = RingWidth;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.generateLightingData = false;
            Color ringColor = color;
            ringColor.a = RingAlpha;
            lr.sharedMaterial = LineMaterial();
            lr.startColor = ringColor;
            lr.endColor = ringColor;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = (i / (float)RingSegments) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * RingRadius, 0f, Mathf.Sin(a) * RingRadius));
            }

            // ---- 悬浮文字（小字号 + 错位）----
            var textGo = new GameObject("LBL_" + label);
            textGo.transform.SetParent(host, false);
            textGo.transform.position = world + labelOffset;
            var tm = textGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = LabelCharacterSize;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            Color textColor = color;
            textColor.a = 0.95f;
            tm.color = textColor;
            var tmr = textGo.GetComponent<MeshRenderer>();
            if (tmr != null)
            {
                tmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tmr.receiveShadows = false;
            }
        }

        // ============================================================ 位姿来源

        private static List<(string name, float[] q)> LoadPoses()
        {
            var list = new List<(string, float[])>();
            string path = Path.Combine(Application.dataPath,
                "DoosanDT/10_DigitalTwin/Config/unity_scene_calibration_20261125.json");
            if (File.Exists(path))
            {
                try
                {
                    CalibFile file = JsonUtility.FromJson<CalibFile>(File.ReadAllText(path));
                    if (file != null && file.points != null)
                    {
                        foreach (var p in file.points)
                        {
                            if (p == null) continue;
                            string key = p.name == "Screw_10_hover" ? "Screw_10" : p.name;
                            if (key != "Home_VR_DT" && key != "Safty_1" && key != "Screw_10") continue;
                            float[] q = (p.q_deg != null && p.q_deg.Length >= 6) ? p.q_deg : p.commanded;
                            if (q == null || q.Length < 6) continue;
                            if (FindIndex(list, key) >= 0) continue;
                            list.Add((key, q));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[E5Overlay] calibration parse failed: " + ex.Message);
                }
            }

            if (list.Count == 0)
            {
                foreach (var f in FallbackPoses) list.Add(f);
                Debug.LogWarning("[E5Overlay] calibration missing/incomplete -> hardcoded q_deg fallback");
            }

            // 固定顺序：Home → Safty_1 → Screw_10
            var ordered = new List<(string, float[])>();
            foreach (var nm in new[] { "Home_VR_DT", "Safty_1", "Screw_10" })
            {
                int i = FindIndex(list, nm);
                if (i >= 0) ordered.Add(list[i]);
            }
            return ordered;
        }

        private static int FindIndex(List<(string name, float[] q)> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].name == name) return i;
            return -1;
        }

        private static string Join(float[] v)
        {
            if (v == null) return "null";
            var s = new string[v.Length];
            for (int i = 0; i < v.Length; i++) s[i] = v[i].ToString("F3");
            return string.Join(", ", s);
        }

        // ============================================================ 材质 / 查找

        private static Material TranslucentMaterial(Color c, float alpha)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { hideFlags = HideFlags.DontSave };
            Color cc = c;
            cc.a = alpha;
            if (m.HasProperty("_Color")) m.SetColor("_Color", cc);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cc);
            m.renderQueue = 3000;
            return m;
        }

        private static Material LineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>找"在役臂"的 RobotModelController：以它子树里有 Spindle_TCP 为准（场景有 4 个 base_link 副本）。</summary>
        private static RobotModelController FindActiveRobot()
        {
            RobotModelController best = null;
            foreach (var m in UnityEngine.Object.FindObjectsOfType<RobotModelController>(true))
            {
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                if (FindChildByName(m.transform, "Spindle_TCP") == null) continue;
                if (best == null) best = m;
            }
            if (best != null) return best;
            return UnityEngine.Object.FindObjectOfType<RobotModelController>();
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

        // ============================================================ 旧 OnGUI（默认关闭）

        private void OnGUI()
        {
            if (!ShowPanel) return;
            const int w = 216;
            int y = Screen.height - 120 - 16 - 118;
            var area = new Rect(16, y, w, 118);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 8, area.y + 4, w - 16, 18), "场景标定");
            if (GUI.Button(new Rect(area.x + 8, area.y + 26, w - 16, 24), "初始姿态 (Home_VR_DT)"))
            {
                if (_robot == null)
                {
                    _robot = FindActiveRobot();
                }
                if (_robot != null)
                {
                    ApplyHomePreview();
                }
            }
            if (GUI.Button(new Rect(area.x + 8, area.y + 54, w - 16, 24),
                           "点位标注: " + (_markersVisible ? "开" : "关")))
            {
                SetMarkersVisible(!_markersVisible);
            }
            GUI.Label(new Rect(area.x + 8, area.y + 82, w - 16, 16),
                      _hint, new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true });
        }
    }
}