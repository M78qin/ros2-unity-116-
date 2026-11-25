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

        private void ApplyHomePreview()
        {
            try
            {
                _robot.SetRuntimeHome0Degrees(HomeDeg);
                bool ok;
                var previous = _robot.CurrentControlAuthority;
                // A preview pose is only accepted under CommandPreview authority;
                // restore the previous authority afterwards so live feedback resumes.
                _robot.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
                ok = _robot.ApplyJointDegrees(HomeDeg,
                    RobotModelController.PoseSource.CommandPreview, false);
                _robot.SetControlAuthority(previous);
                _hint = ok ? "本地姿态已复位 Home_VR_DT（仅视觉）" : "姿态复位被拒绝（查看控制台）";
                Debug.Log("[E5Overlay] Home_VR_DT applied=" + ok + " (local preview only)");
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
                MakeMarker(p.name, p.tcp_mm_deg, c);
                made++;
            }

            // Real screw contact plane (same x/y as the hover point, z = 1032.5 mm).
            foreach (var p in file.points)
            {
                if (p != null && p.name == "Screw_10_hover" && p.tcp_mm_deg != null && p.tcp_mm_deg.Length >= 3)
                {
                    var contact = new float[] { p.tcp_mm_deg[0], p.tcp_mm_deg[1], 1032.5f };
                    MakeMarker("Contact_1032.5", contact, new Color(1f, 0.2f, 0.25f));
                    made++;
                    break;
                }
            }
            Debug.Log("[E5Overlay] waypoint markers=" + made + " parent=" +
                      (baseLink != null ? baseLink.name : "world"));
        }

        private void MakeMarker(string label, float[] tcpMm, Color color)
        {
            // ROS base frame (x forward, y left, z up) -> Unity (x, z, -y), metres.
            var local = new Vector3(tcpMm[0] * 0.001f, tcpMm[2] * 0.001f, -tcpMm[1] * 0.001f);
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
            textGo.transform.localPosition = local + new Vector3(0f, 0.06f, 0f);
            var tm = textGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.012f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
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