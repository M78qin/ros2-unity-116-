using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// MCP / CLI hooks for ExpWindowLiteRecorder (Play-mode Start/Stop; Edit-mode Ensure).
    /// Path resolution order: env EXP_UNITY_CSV → marker file → menu arg via SessionState.
    /// </summary>
    public static class ExpWindowLiteControl
    {
        private const string MarkerPath =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\exp_unity_csv.path";
        private const string ApplyHzPath =
            @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\exp_unity_apply_hz.txt";
        private const string SessionKey = "DoosanLab.ExpWindow.CsvPath";
        private const string HostObjectName = "ROS2";

        [MenuItem("DigitalTwin/ExpWindow/0 Ensure LiteRecorder on ROS2", false, 800)]
        public static void EnsureComponent()
        {
            GameObject go = GameObject.Find(HostObjectName);
            if (go == null)
            {
                Debug.LogError("[ExpWindow] GameObject 'ROS2' not found in active scene.");
                return;
            }

            var bridge = go.GetComponent<Ros2Bridge>();
            if (bridge == null)
            {
                Debug.LogError("[ExpWindow] Ros2Bridge missing on ROS2.");
                return;
            }

            var rec = go.GetComponent<ExpWindowLiteRecorder>();
            if (rec == null)
            {
                rec = Undo.AddComponent<ExpWindowLiteRecorder>(go);
                Debug.Log("[ExpWindow] Added ExpWindowLiteRecorder on ROS2.");
            }
            else
            {
                Debug.Log("[ExpWindow] ExpWindowLiteRecorder already present.");
            }

            var so = new SerializedObject(rec);
            var bridgeProp = so.FindProperty("bridge");
            if (bridgeProp != null && bridgeProp.objectReferenceValue == null)
            {
                bridgeProp.objectReferenceValue = bridge;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(go);
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    go.scene);
            }
        }

        [MenuItem("DigitalTwin/ExpWindow/1 Start Window (marker/env)", false, 801)]
        public static void StartWindowMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[ExpWindow] StartWindow requires Play Mode.");
                return;
            }

            string path = ResolveCsvPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogError("[ExpWindow] No CSV path. Set EXP_UNITY_CSV or write " + MarkerPath);
                return;
            }

            EnsureComponent();
            var rec = FindRecorder();
            if (rec == null)
            {
                Debug.LogError("[ExpWindow] ExpWindowLiteRecorder not found after Ensure.");
                return;
            }

            rec.ApplyHz = ResolveApplyHz();
            bool ok = rec.StartWindow(path);
            Debug.Log(ok
                ? "[ExpWindow] StartWindow OK applyHz=" + rec.ApplyHz + " → " + path
                : "[ExpWindow] StartWindow FAILED → " + path);
        }

        private static int ResolveApplyHz()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("EXP_UNITY_APPLY_HZ");
                if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env.Trim(), out int fromEnv))
                {
                    return Math.Max(0, fromEnv);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (File.Exists(ApplyHzPath) &&
                    int.TryParse(File.ReadAllText(ApplyHzPath).Trim(), out int fromFile))
                {
                    return Math.Max(0, fromFile);
                }
            }
            catch { /* ignore */ }

            return 0;
        }

        [MenuItem("DigitalTwin/ExpWindow/2 Stop Window", false, 802)]
        public static void StopWindowMenu()
        {
            var rec = FindRecorder();
            if (rec == null)
            {
                Debug.LogWarning("[ExpWindow] StopWindow: recorder not found.");
                return;
            }

            long rows = rec.RowsWritten;
            string outPath = rec.OutputPath;
            rec.StopWindow();
            Debug.Log("[ExpWindow] StopWindow rows=" + rows + " path=" + outPath);
        }

        [MenuItem("DigitalTwin/ExpWindow/3 Report Status", false, 803)]
        public static void ReportStatus()
        {
            var rec = FindRecorder();
            var bridge = UnityEngine.Object.FindObjectOfType<Ros2Bridge>();
            string msg = "[ExpWindow] playing=" + Application.isPlaying
                + " recorder=" + (rec != null)
                + " recording=" + (rec != null && rec.IsRecording)
                + " rows=" + (rec != null ? rec.RowsWritten : 0)
                + " bridge=" + (bridge != null)
                + " lastSeq=" + (bridge != null ? bridge.LastSeq : -1)
                + " hz=" + (bridge != null ? bridge.FrameRateHz.ToString("0.#") : "-");
            if (bridge != null && bridge.TryGetLastLatencyEcho(out long es, out double rtt, out _))
            {
                msg += " echoSeq=" + es + " rttMs=" + rtt.ToString("0.###");
            }
            Debug.Log(msg);
        }

        [MenuItem("DigitalTwin/ExpWindow/9 Save Scene", false, 809)]
        public static void SaveSceneMenu()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[ExpWindow] Save Scene skipped in Play Mode.");
                return;
            }

            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[ExpWindow] Scene saved.");
        }

        private static ExpWindowLiteRecorder FindRecorder()
        {
            GameObject go = GameObject.Find(HostObjectName);
            if (go != null)
            {
                var rec = go.GetComponent<ExpWindowLiteRecorder>();
                if (rec != null) return rec;
            }
            return UnityEngine.Object.FindObjectOfType<ExpWindowLiteRecorder>();
        }

        private static string ResolveCsvPath()
        {
            string fromSession = SessionState.GetString(SessionKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(fromSession)) return fromSession.Trim();

            try
            {
                string env = Environment.GetEnvironmentVariable("EXP_UNITY_CSV");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            }
            catch { /* ignore */ }

            try
            {
                if (File.Exists(MarkerPath))
                {
                    string line = File.ReadAllText(MarkerPath).Trim();
                    if (!string.IsNullOrWhiteSpace(line)) return line;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ExpWindow] marker read failed: " + ex.Message);
            }

            return string.Empty;
        }
    }
}
