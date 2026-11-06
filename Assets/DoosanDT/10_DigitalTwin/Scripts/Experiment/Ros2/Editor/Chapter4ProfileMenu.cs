using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    /// <summary>
    /// Chapter4 paper-run menu. Exposes Ros2Bridge.LoadChapter4Profile() as menu items so an
    /// automation client (MCP execute_menu_item) can stage a formal Chapter4 run without the
    /// inspector. The profile path is read from <project>/Chapter4AutoPilot/profile_path.txt
    /// (single line, absolute path) so no private serialized field has to be touched by hand.
    ///
    /// All actions are Play-Mode only and none of them commands robot motion.
    /// </summary>
    public static class Chapter4ProfileMenu
    {
        private const string ControlDirName = "Chapter4AutoPilot";
        private const string ProfilePathFileName = "profile_path.txt";

        private static string ControlDir
        {
            get { return Path.Combine(Directory.GetCurrentDirectory(), ControlDirName); }
        }

        private static Ros2ExperimentPanel FindPanel()
        {
            Ros2ExperimentPanel panel = UnityEngine.Object.FindObjectOfType<Ros2ExperimentPanel>();
            if (panel == null)
            {
                Debug.LogError("[CH4] Ros2ExperimentPanel not found in loaded scenes.");
            }
            return panel;
        }

        private static Ros2Bridge FindBridge(Ros2ExperimentPanel panel)
        {
            if (panel == null) return null;
            Ros2Bridge bridge = panel.GetComponent<Ros2Bridge>();
            if (bridge == null) bridge = UnityEngine.Object.FindObjectOfType<Ros2Bridge>();
            if (bridge == null) Debug.LogError("[CH4] Ros2Bridge not found.");
            return bridge;
        }

        private static bool SetProfilePath(Ros2Bridge bridge, string path)
        {
            FieldInfo field = typeof(Ros2Bridge).GetField(
                "chapter4ProfilePath", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError("[CH4] field chapter4ProfilePath not found (renamed upstream?).");
                return false;
            }
            field.SetValue(bridge, path);
            return true;
        }

        [MenuItem("DigitalTwin/Chapter4/1 Load Profile (path from file)", true)]
        private static bool PlayModeOnlyLoad() { return Application.isPlaying; }

        [MenuItem("DigitalTwin/Chapter4/1 Load Profile (path from file)")]
        public static void LoadProfile()
        {
            Ros2ExperimentPanel panel = FindPanel();
            Ros2Bridge bridge = FindBridge(panel);
            if (bridge == null) return;

            string controlFile = Path.Combine(ControlDir, ProfilePathFileName);
            if (!File.Exists(controlFile))
            {
                Debug.LogError("[CH4 Load] missing control file: " + controlFile);
                return;
            }
            string profilePath = File.ReadAllText(controlFile).Trim();
            if (string.IsNullOrEmpty(profilePath))
            {
                Debug.LogError("[CH4 Load] empty profile path in " + controlFile);
                return;
            }
            if (!File.Exists(profilePath))
            {
                Debug.LogError("[CH4 Load] profile file not found: " + profilePath);
                return;
            }
            if (!SetProfilePath(bridge, profilePath)) return;

            try
            {
                bridge.LoadChapter4Profile();
                Debug.Log("[CH4 Load] ok=True profile=" + profilePath +
                          " runId=" + bridge.Chapter4RunId +
                          " chapter4=" + bridge.Chapter4Enabled +
                          " recording=" + bridge.Chapter4Recording +
                          " dir=" + bridge.Chapter4Directory);
            }
            catch (Exception e)
            {
                Debug.LogError("[CH4 Load] failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        [MenuItem("DigitalTwin/Chapter4/2 Stop Stream", true)]
        private static bool PlayModeOnlyStop() { return Application.isPlaying; }

        [MenuItem("DigitalTwin/Chapter4/2 Stop Stream")]
        public static void StopStream()
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;
            RobotCommandResult result = panel.StopStream();
            Debug.Log("[CH4 Stop] ok=" + result.Success + " status=" + result.Status +
                      " err=" + result.ErrorMessage + " stream=" + panel.StreamEnabled);
        }

        [MenuItem("DigitalTwin/Chapter4/3 Log State", true)]
        private static bool PlayModeOnlyLog() { return Application.isPlaying; }

        [MenuItem("DigitalTwin/Chapter4/3 Log State")]
        public static void LogState()
        {
            Ros2ExperimentPanel panel = FindPanel();
            Ros2Bridge bridge = FindBridge(panel);
            if (bridge == null) return;
            Debug.Log("[CH4 State] chapter4=" + bridge.Chapter4Enabled +
                      " runId=" + bridge.Chapter4RunId +
                      " recording=" + bridge.Chapter4Recording +
                      " flushed=" + bridge.Chapter4Flushed +
                      " dir=" + bridge.Chapter4Directory +
                      " err=" + bridge.Chapter4Error +
                      " | link=" + panel.Ros2LinkConnected +
                      " data=" + panel.Ros2Connected +
                      " stream=" + panel.StreamEnabled +
                      " record=" + panel.RecordEnabled +
                      " hz=" + panel.Ros2FrameRateHz.ToString("F1") +
                      " dropped=" + panel.Ros2DroppedFrames +
                      " session=" + panel.ActiveSessionId);
        }
    }
}