using System.IO;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Lab interop: if marker exists, auto Plan-B Connect/Channels/Stream (+ optional ExpWindow).
    /// Marker: F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\unity_auto_connect.flag
    /// </summary>
    public static class E5LiveAutoConnectBootstrap
    {
        private const string MarkerPath =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\unity_auto_connect.flag";
        private const string CsvMarkerPath =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\exp_unity_csv.path";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Application.isPlaying) return;
            if (!File.Exists(MarkerPath)) return;
            var go = new GameObject("~E5LiveAutoConnect");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<E5LiveAutoConnectRunner>();
        }

        private sealed class E5LiveAutoConnectRunner : MonoBehaviour
        {
            private float _t0;
            private bool _done;

            private void Start() => _t0 = Time.realtimeSinceStartup;

            private void Update()
            {
                if (_done) return;
                // wait bindings/runtime wake
                if (Time.realtimeSinceStartup - _t0 < 1.2f) return;
                _done = true;
                RunOnce();
            }

            private void RunOnce()
            {
                var panel = Object.FindObjectOfType<Ros2ExperimentPanel>();
                if (panel == null)
                {
                    Debug.LogError("[E5LiveAutoConnect] Ros2ExperimentPanel missing.");
                    return;
                }

                panel.ResolveBindings();
                var c = panel.ConnectRos2();
                Debug.Log("[E5LiveAutoConnect] ConnectRos2 ok=" + c.Success + " status=" + c.Status + " err=" + c.ErrorMessage);

                var ch = panel.ApplyChannelsAndRates();
                Debug.Log("[E5LiveAutoConnect] ApplyChannels ok=" + ch.Success + " status=" + ch.Status);

                var st = panel.StartStream();
                Debug.Log("[E5LiveAutoConnect] StartStream ok=" + st.Success + " status=" + st.Status + " err=" + st.ErrorMessage);

                var bridge = Object.FindObjectOfType<Ros2Bridge>();
                if (bridge != null)
                {
                    Debug.Log("[E5LiveAutoConnect] bridge running=" + bridge.IsRunning
                              + " link=" + bridge.IsLinkConnected
                              + " err=" + bridge.LastError);
                }

                var rec = Object.FindObjectOfType<ExpWindowLiteRecorder>();
                if (rec != null && File.Exists(CsvMarkerPath))
                {
                    var path = File.ReadAllText(CsvMarkerPath).Trim();
                    if (!string.IsNullOrEmpty(path))
                    {
                        bool ok = rec.StartWindow(path);
                        Debug.Log("[E5LiveAutoConnect] ExpWindow ok=" + ok + " → " + path);
                    }
                }
            }
        }
    }
}
