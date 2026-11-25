using UnityEngine;

namespace DoosanDT.UI.Runtime
{
    /// <summary>
    /// Runtime render-rate switch: 30 / 60 / 90 / unlimited, VSync off.
    /// Docked bottom-right so it never covers the top KPI bar or the left step
    /// strip; collapsible to a single small tab.
    /// Shows the measured render FPS (distinct from the ROS stream rate shown
    /// in the top KpiRate, which is prefixed "ROS:").
    /// </summary>
    public sealed class RenderRatePanel : MonoBehaviour
    {
        private static RenderRatePanel _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("[RenderRatePanel]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RenderRatePanel>();
            SetRate(90);
        }

        private const int W = 216;
        private const int H = 104;
        private const int Margin = 16;

        private bool _collapsed;
        private float _fps;
        private int _frames;
        private float _t0;
        private float _nextLog;
        private int _lastTarget = int.MinValue;

        private void Update()
        {
            _frames++;
            float now = Time.unscaledTime;
            if (now - _t0 >= 1f)
            {
                _fps = _frames / (now - _t0);
                _frames = 0;
                _t0 = now;
            }

            // Keep VSync off and log measured FPS every 2 s for verification.
            if (QualitySettings.vSyncCount != 0)
            {
                QualitySettings.vSyncCount = 0;
            }
            if (_lastTarget != Application.targetFrameRate)
            {
                _lastTarget = Application.targetFrameRate;
                Debug.Log("[RenderRate] target=" + _lastTarget + " (targetFrameRate=" +
                          Application.targetFrameRate + ", vSync=" + QualitySettings.vSyncCount + ")");
            }
            if (now >= _nextLog)
            {
                _nextLog = now + 2f;
                Debug.Log("[RenderRate] fps=" + _fps.ToString("F1") +
                          " target=" + _lastTarget + " vSync=" + QualitySettings.vSyncCount);
            }
        }

        private void OnGUI()
        {
            // Shell window occupies (790,65,1114x950); the free corner is the
            // bottom-left stripe (x < 790), so dock there to avoid the KPI bar,
            // the step rail and the live viewport.
            var area = new Rect(Margin,
                                Screen.height - (H + Margin),
                                W, H);
            if (_collapsed)
            {
                if (GUI.Button(new Rect(area.x, area.y + H - 26, 74, 22), "FPS " + _fps.ToString("F0")))
                {
                    _collapsed = false;
                }
                return;
            }

            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 8, area.y + 4, W - 16, 18),
                      "FPS: " + _fps.ToString("F1") + "   (render)");
            GUI.Label(new Rect(area.x + 8, area.y + 22, W - 16, 16),
                      "ROS 流率见顶栏 KpiRate", new GUIStyle(GUI.skin.label) { fontSize = 10 });

            int y = (int)area.y + 40;
            int bw = (W - 24) / 3;
            if (GUI.Button(new Rect(area.x + 8, y, bw, 22), "30"))
            {
                SetRate(30);
            }
            if (GUI.Button(new Rect(area.x + 8 + bw + 4, y, bw, 22), "60"))
            {
                SetRate(60);
            }
            if (GUI.Button(new Rect(area.x + 8 + 2 * (bw + 4), y, bw, 22), "90"))
            {
                SetRate(90);
            }
            if (GUI.Button(new Rect(area.x + 8, y + 26, W - 16, 20), "Unlimited (VSync off)"))
            {
                SetRate(-1);
            }
            if (GUI.Button(new Rect(area.x + W - 24, area.y + 2, 20, 18), "–"))
            {
                _collapsed = true;
            }
        }

        /// <summary>Sets the render target rate (hz &lt;= 0 means unlimited).</summary>
        public static void SetRate(int hz)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = hz;
            Debug.Log("[RenderRate] targetFrameRate=" + hz + " vSync=0");
        }

        /// <summary>Current measured render FPS (for tools/menus).</summary>
        public static float CurrentFps
        {
            get { return _instance != null ? _instance._fps : 0f; }
        }
    }
}