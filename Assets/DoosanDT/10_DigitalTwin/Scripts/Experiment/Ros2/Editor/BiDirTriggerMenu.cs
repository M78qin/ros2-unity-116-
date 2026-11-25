using System;
using DigitalTwin;
using UnityEditor;
using UnityEngine;

namespace DoosanDT.Experiment.Ros2
{
    /// <summary>
    /// E-BiDir trigger menu: publishes 3 timestamped markers 8 s apart so the
    /// VM-side executor can run Home -> Safty_1 -> Home per trigger and stamp
    /// receive / motion-start / motion-end. No motion is commanded from Unity.
    /// </summary>
    public static class BiDirTriggerMenu
    {
        private const int Runs = 3;
        private const double IntervalSec = 8.0;

        private static int _sent;
        private static int _seq;
        private static double _nextAt;
        private static bool _armed;
        private static Ros2Bridge _bridge;

        [MenuItem("DigitalTwin/Experiment/E-BiDir Trigger x3", false, 900)]
        public static void TriggerX3()
        {
            _bridge = UnityEngine.Object.FindObjectOfType<Ros2Bridge>();
            if (_bridge == null)
            {
                Debug.LogError("[BiDir] Ros2Bridge not found in scene.");
                return;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[BiDir] Not in Play Mode: Connect/Link first, then trigger.");
                return;
            }
            _sent = 0;
            _seq = 0;
            _armed = true;
            _nextAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[BiDir] armed: 3 triggers, 8 s apart");
        }

        [MenuItem("DigitalTwin/Experiment/E-BiDir Trigger Single", false, 901)]
        public static void TriggerOnce()
        {
            var bridge = UnityEngine.Object.FindObjectOfType<Ros2Bridge>();
            if (bridge == null)
            {
                Debug.LogError("[BiDir] Ros2Bridge not found in scene.");
                return;
            }
            Send(bridge, ++_seq);
        }

        private static void Tick()
        {
            if (!_armed)
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextAt)
            {
                return;
            }
            if (_bridge == null)
            {
                _armed = false;
                Debug.LogError("[BiDir] bridge lost; abort");
                return;
            }
            _sent++;
            Send(_bridge, _sent);
            if (_sent >= Runs)
            {
                _armed = false;
                EditorApplication.update -= Tick;
                Debug.Log("[BiDir] all 3 triggers sent");
                return;
            }
            _nextAt = EditorApplication.timeSinceStartup + IntervalSec;
        }

        private static void Send(Ros2Bridge bridge, int seq)
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string json = "{\"seq\":" + seq + ",\"t_unity_ms\":" + ms + "}";
            bool ok = bridge.PublishBiDirTrigger(json);
            Debug.Log("[BiDir] publish seq=" + seq + " t_unity_ms=" + ms + " ok=" + ok);
        }
    }
}