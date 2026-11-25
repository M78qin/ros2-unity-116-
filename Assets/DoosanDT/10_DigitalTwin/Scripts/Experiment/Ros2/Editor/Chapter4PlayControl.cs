using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.Experiment.Ros2.EditorTools
{
    /// <summary>
    /// Enter/exit Play Mode, plus an optional marker-driven Plan-B Link+Stream kickoff.
    /// Once Play starts the MCP websocket drops, so the Plan-B menus are executed locally
    /// from EnteredPlayMode instead of from the external client.
    /// </summary>
    public static class Chapter4PlayControl
    {
        private const string AutoLinkMarker =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\unity_auto_connect.flag";
        private const string CsvMarker =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\exp_unity_csv.path";

        private static bool _armedAutoLink;

        [InitializeOnLoadMethod]
        private static void HookPlayMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!_armedAutoLink && !File.Exists(AutoLinkMarker)) return;
            _armedAutoLink = false;
            EditorApplication.delayCall += RunAutoLinkMenus;
        }

        private static void RunAutoLinkMenus()
        {
            try
            {
                Debug.Log("[CH4 Play] auto Plan-B Link+Stream via menu");
                EditorApplication.ExecuteMenuItem("DigitalTwin/ROS2 Plan B/Run Steps 1-5 (Link+Stream)");
                if (File.Exists(CsvMarker))
                {
                    EditorApplication.ExecuteMenuItem("DigitalTwin/ExpWindow/0 Ensure LiteRecorder on ROS2");
                    EditorApplication.ExecuteMenuItem("DigitalTwin/ExpWindow/1 Start Window (marker/env)");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[CH4 Play] auto link failed: " + ex.Message);
            }
        }

        [MenuItem("DigitalTwin/Chapter4/14 Enter Play Mode")]
        public static void EnterPlay()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[CH4 Play] already in play mode");
                return;
            }
            _armedAutoLink = File.Exists(AutoLinkMarker);
            EditorApplication.isPlaying = true;
            Debug.Log("[CH4 Play] enter play mode requested armedAutoLink=" + _armedAutoLink);
        }

        [MenuItem("DigitalTwin/Chapter4/15 Exit Play Mode")]
        public static void ExitPlay()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.Log("[CH4 Play] not in play mode");
                return;
            }
            EditorApplication.isPlaying = false;
            Debug.Log("[CH4 Play] exit play mode requested");
        }

        [MenuItem("DigitalTwin/Chapter4/16 Report Play State")]
        public static void ReportState()
        {
            string scene = SceneManager.GetActiveScene().name;
            Debug.Log($"[CH4 Play] isPlaying={EditorApplication.isPlaying} isPaused={EditorApplication.isPaused} isCompiling={EditorApplication.isCompiling} scene={scene}");
        }
    }
}