using System;
using System.IO;
using DigitalTwin;
using UnityEngine;

namespace DoosanDT.UI.Runtime
{
    /// <summary>
    /// E5 scene overlay: one-click Home_VR_DT preview pose + glowing waypoint
    /// markers read from the measured calibration JSON. Purely local/visual:
    /// it never publishes a robot command.
    /// </summary>
    public sealed class E5SceneOverlay : MonoBehaviour
    {
        private static E5SceneOverlay _instance;

        private static readonly float[] HomeDeg = { 0f, 0f, 90f, 0f, 0f, 180f };

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
        private GameObject _markerRoot;

        /// <summary>左下角原生「场景标定」灰框：默认关闭，功能已并入二期面板卡片。</summary>
        public static bool ShowPanel = false;

        /// <summary>点位标注（Home / Safty_1 / Screw_10_hover / Contact_1032.5）显隐。</summary>
        public bool MarkersVisible
        {
            get { return _markersVisible; }
        }

        public void SetMarkersVisible(bool value)
        {
            _markersVisible = value;
            if (_markerRoot != null) _markerRoot.SetActive(value);
        }

        public void ToggleMarkers() => SetMarkersVisible(!MarkersVisible);

        /// <summary>供二期面板卡片调用：应用 Home_VR_DT 预览姿态（仅本地视觉）。</summary>
        public void ApplyHomePose()
        {
            if (_robot == null) _robot = FindObjectOfType<RobotModelController>();
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
            _robot = FindObjectOfType<RobotModelController>();
            BuildMarkers();
        }

        private void Update()
        {
            // Apply the Home pose once, shortly after the scene has initialised.
            if (!_poseApplied && Time.unscaledTime - _bootAt > 0.5f)
            {
                if (_robot == null)
                {
                    _robot = FindObjectOfType<RobotModelController>();
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
            bool usable = HomeDeg != null && HomeDeg.Length > 0;
            if (usable)
            {
                for (int i = 0; i < HomeDeg.Length; i++)
                    if (Mathf.Abs(HomeDeg[i]) > 0.0001f) { usable = true; break; }
                    else if (i == HomeDeg.Length - 1) usable = false;
            }
            if (usable) return HomeDeg;
            float[] fallback = _robot != null ? _robot.StartupPoseDeg : null;
            if (fallback != null && fallback.Length > 0) return fallback;
            return new float[] { 0f, 0f, 90f, 0f, 0f, 180f };
        }

        private void ApplyHomePreview()
        {
            try
            {
                // 真凶修复：HomeDeg 若未在场景里配置（null/全零），过去会把机械臂按 URDF 零位
                // 折叠；这里统一回落到 RobotModelController 的 Home_VR_DT = (0,0,90,0,0,180)。
                float[] pose = ResolveHomeDeg();
                _robot.SetRuntimeHome0Degrees(pose);
                bool ok;
                var previous = _robot.CurrentControlAuthority;
                // A preview pose is only accepted under CommandPreview authority;
                // restore the previous authority afterwards so live feedback resumes.
                _robot.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
                ok = _robot.ApplyJointDegrees(pose,
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

        private void BuildMarkers()
        {
            string path = Path.Combine(Application.dataPath,
                "DoosanDT/10_DigitalTwin/Config/unity_scene_calibration_20261125.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[E5Overlay] calibration json missing: " + path);
                return;
            }
            CalibFile file;
            try
            {
                file = JsonUtility.FromJson<CalibFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[E5Overlay] calibration parse failed: " + ex.Message);
                return;
            }
            if (file == null || file.points == null || file.points.Length == 0)
            {
                Debug.LogWarning("[E5Overlay] calibration has no points");
                return;
            }

            Transform baseLink = FindBaseLink();
            _markerRoot = new GameObject("E5WaypointMarkers");
            if (baseLink != null)
            {
                _markerRoot.transform.SetParent(baseLink, false);
            }

            // Anchor the waypoint set to the CURRENT tool tip: Home_VR_DT is drawn where Spindle_TCP
            // actually is under the (0,0,90,0,0,180)° startup pose, and every other point keeps its
            // calibrated offset relative to Home. No absolute ROS-frame coordinate is used, so the
            // markers can never drift onto the pack surface again.
            Vector3 homeLocal = Vector3.zero;
            float[] homeCalib = null;
            if (baseLink != null)
            {
                foreach (var p in file.points)
                {
                    if (p != null && p.name == "Home_VR_DT" && p.tcp_mm_deg != null && p.tcp_mm_deg.Length >= 3)
                    {
                        homeCalib = p.tcp_mm_deg;
                        break;
                    }
                }
                Transform tip = FindTip(baseLink);
                if (tip != null)
                    homeLocal = baseLink.InverseTransformPoint(tip.position);
                else
                    homeLocal = homeCalib != null ? RosMmToUnityLocal(homeCalib) : Vector3.zero;
                Debug.Log("[E5Overlay] Home anchor = " + homeLocal + " (tip=" +
                          (tip != null ? tip.name : "fallback calib") + ")");
            }

            int made = 0;
            foreach (var p in file.points)
            {
                if (p == null || p.tcp_mm_deg == null || p.tcp_mm_deg.Length < 3)
                {
                    continue;
                }
                if (p.name != "Home_VR_DT" && p.name != "Safty_1" && p.name != "Screw_10_hover")
                {
                    continue;
                }
                Color c = p.name == "Home_VR_DT" ? new Color(0.2f, 0.9f, 0.45f)
                        : p.name == "Safty_1" ? new Color(0.25f, 0.65f, 1f)
                        : new Color(1f, 0.55f, 0.15f);
                MakeMarker(p.name, p.tcp_mm_deg, c, homeLocal, homeCalib, false);
                made++;
            }

            // Real screw contact plane (same x/y as the hover point, z = 1032.5 mm).
            foreach (var p in file.points)
            {
                if (p != null && p.name == "Screw_10_hover" && p.tcp_mm_deg != null && p.tcp_mm_deg.Length >= 3)
                {
                    var contact = new float[] { p.tcp_mm_deg[0], p.tcp_mm_deg[1], 1032.5f };
                    MakeMarker("Contact_1032.5", contact, new Color(1f, 0.2f, 0.25f), homeLocal, homeCalib, true);
                    made++;
                    break;
                }
            }
            Debug.Log("[E5Overlay] waypoint markers=" + made + " parent=" +
                      (baseLink != null ? baseLink.name : "world"));
            // 标定球默认隐藏，只有点击面板「点位标注」按钮时才显示。
            if (_markerRoot != null) _markerRoot.SetActive(_markersVisible);
        }

        private static Vector3 RosMmToUnityLocal(float[] tcpMm)
        {
            // ROS base frame (x forward, y left, z up) -> Unity (x, z, -y), metres.
            return new Vector3(tcpMm[0] * 0.001f, tcpMm[2] * 0.001f, -tcpMm[1] * 0.001f);
        }

        /// <summary>Real screw top: centre of the screws group, at its top plane.</summary>
        private static Transform FindTip(Transform baseLink)
        {
            Transform hit = FindByName(baseLink.root, "Spindle_TCP");
            if (hit != null) return hit;
            return FindByName(baseLink.root, "Flange_TCP");
        }

        /// <summary>Real screw top: centre of the screws group, at its top plane.</summary>
        private static bool TryFindScrewTop(Transform baseLink, out Vector3 world)
        {
            world = Vector3.zero;
            Transform t = baseLink;
            Transform root = baseLink != null ? baseLink.root : null;
            Transform screws = FindByName(root, "screws");
            if (screws == null) return false;
            Bounds b = new Bounds();
            bool any = false;
            foreach (var r in screws.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return false;
            world = new Vector3(b.center.x, b.max.y, b.center.z);
            return true;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private void MakeMarker(string label, float[] tcpMm, Color color, Vector3 homeLocal, float[] homeCalib, bool staggerRight)
        {
            // ROS base frame (x forward, y left, z up) -> Unity (x, z, -y), metres.
            // Position = Home anchor (current tool tip) + calibrated offset relative to Home.
            Vector3 rel = homeCalib != null
                ? RosMmToUnityLocal(tcpMm) - RosMmToUnityLocal(homeCalib)
                : RosMmToUnityLocal(tcpMm);
            var local = homeLocal + rel;
            Transform host = _markerRoot.transform;
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "WP_" + label;
            disc.transform.SetParent(host, false);
            disc.transform.localPosition = local;
            disc.transform.localScale = new Vector3(0.06f, 0.006f, 0.06f);
            var col = disc.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
            var mr = disc.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material = Emissive(color);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var textGo = new GameObject("LBL_" + label);
            textGo.transform.SetParent(host, false);
            // Stagger labels of nearly-coincident points so they do not overlap.
            float side = staggerRight ? 0.14f : -0.14f;
            textGo.transform.localPosition = local + new Vector3(side, 0.06f, 0f);
            var tm = textGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.012f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
        }

        private static Material Emissive(Color c)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            var m = new Material(shader);
            m.color = c;
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", c);
            }
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2.2f);
            }
            return m;
        }

        private static Transform FindBaseLink()
        {
            foreach (var t in FindObjectsOfType<Transform>())
            {
                if (t.name == "base_link")
                {
                    return t;
                }
            }
            return null;
        }

        private void OnGUI()
        {
            if (!ShowPanel) return;
            const int w = 216;
            int y = Screen.height - 120 - 16 - 118; // above RenderRatePanel
            var area = new Rect(16, y, w, 118);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 8, area.y + 4, w - 16, 18), "场景标定");
            if (GUI.Button(new Rect(area.x + 8, area.y + 26, w - 16, 24), "初始姿态 (Home_VR_DT)"))
            {
                if (_robot == null)
                {
                    _robot = FindObjectOfType<RobotModelController>();
                }
                if (_robot != null)
                {
                    ApplyHomePreview();
                }
            }
            if (GUI.Button(new Rect(area.x + 8, area.y + 54, w - 16, 24),
                           "点位标注: " + (_markersVisible ? "开" : "关")))
            {
                _markersVisible = !_markersVisible;
                if (_markerRoot != null)
                {
                    _markerRoot.SetActive(_markersVisible);
                }
            }
            GUI.Label(new Rect(area.x + 8, area.y + 82, w - 16, 16),
                      _hint, new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true });
        }
    }
}