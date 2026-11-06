// Menu: DigitalTwin/UI/6 Play Smoke (UI only)      and      DigitalTwin/UI/6b Abort Play
//
// Why an editor-side orchestrator: the smoke test has to prove three negative facts as well as the
// positive one, and only the editor can observe them.
//   positive : the shell can be walked welcome -> roaming -> training -> disassembly -> data in Play
//   negative 1: no new file appears under the PerformanceProfiler csv directory (D:\Unity Projects\数据记录)
//   negative 2: no ROS2 transport / recorder starts (profile autoStart*=0)
//   negative 3: the saved scene is byte-identical before and after the smoke (no accidental edits)
//
// The arm flag and the pre-state live in SessionState, not in statics, because entering Play mode
// reloads the domain: statics would be wiped and the run would silently do nothing (observed once).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUIPlaySmoke
    {
        private const string LogPath = "Logs/ui_play_smoke_editor.txt";
        private const string PerfMonitorPath = "00_数字孪生机械臂/10_电池包拆解系统/VR拆解/PerformanceMonitor";
        private const string CsvDirectory = @"D:\Unity Projects\数据记录";

        private const string KeyArmed = "DigitalTwin.UIPlaySmoke.Armed";
        private const string KeyMonitorWasActive = "DigitalTwin.UIPlaySmoke.MonitorWasActive";
        private const string KeyScenePath = "DigitalTwin.UIPlaySmoke.ScenePath";
        private const string KeySceneHash = "DigitalTwin.UIPlaySmoke.SceneHash";
        private const string KeyStartedAt = "DigitalTwin.UIPlaySmoke.StartedAt";

        static TwinUIPlaySmoke()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("DigitalTwin/UI/6b Abort Play")]
        public static void AbortPlay()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                Debug.Log("[UI Play Smoke] abort requested");
            }
            else
            {
                Debug.Log("[UI Play Smoke] not in Play mode");
            }
        }

        /// <summary>Recovery path: an aborted smoke can leave the profiler object deactivated.</summary>
        [MenuItem("DigitalTwin/UI/6c Restore PerformanceMonitor")]
        public static void RestorePerfMonitor()
        {
            GameObject monitor = TwinSceneLookup.FindObject(PerfMonitorPath);
            if (monitor == null)
            {
                Debug.LogWarning("[UI Play Smoke] PerformanceMonitor not found");
                return;
            }

            bool changed = !monitor.activeSelf;
            monitor.SetActive(true);
            if (changed)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }

            Debug.Log("[UI Play Smoke] PerformanceMonitor active=true (changed=" + changed + ")");
        }
[MenuItem("DigitalTwin/UI/6 Play Smoke (UI only)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[UI Play Smoke] already in Play mode, ignoring");
                return;
            }

            Directory.CreateDirectory("Logs");
            DateTime startedAt = DateTime.Now;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                // Play would otherwise run against unsaved edits; save so the "before" hash is real.
                EditorSceneManager.SaveScene(scene);
            }

            string scenePath = scene.path;
            string sceneHashBefore = HashFile(scenePath);

            GameObject monitor = TwinSceneLookup.FindObject(PerfMonitorPath);
            bool monitorWasActive = monitor != null && monitor.activeSelf;
            if (monitor != null)
            {
                monitor.SetActive(false);
            }

            var sb = new StringBuilder();
            sb.AppendLine("Twin UI play-mode smoke (editor side)  " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("  scene=" + scenePath);
            sb.AppendLine("  sceneSha256Before=" + sceneHashBefore);
            sb.AppendLine("  PerformanceMonitor=" + (monitor == null ? "<not found>" : PerfMonitorPath)
                          + " deactivatedForSmoke (was active=" + monitorWasActive + ")");
            sb.AppendLine("  csvDirectoryBefore=" + DescribeCsvDirectory());
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));

            SessionState.SetBool(KeyArmed, true);
            SessionState.SetBool(KeyMonitorWasActive, monitorWasActive);
            SessionState.SetString(KeyScenePath, scenePath);
            SessionState.SetString(KeySceneHash, sceneHashBefore);
            SessionState.SetString(KeyStartedAt, startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(KeyArmed, false))
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                var host = new GameObject("UI_PLAY_SMOKE_DRIVER");
                host.hideFlags = HideFlags.DontSave;
                host.AddComponent<TwinUIPlaySmokeDriver>();
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            SessionState.SetBool(KeyArmed, false);

            bool monitorWasActive = SessionState.GetBool(KeyMonitorWasActive, true);
            string scenePath = SessionState.GetString(KeyScenePath, SceneManager.GetActiveScene().path);
            string sceneHashBefore = SessionState.GetString(KeySceneHash, "<unknown>");
            string startedAt = SessionState.GetString(KeyStartedAt, "<unknown>");

            // restore the profiler object exactly as it was
            GameObject monitor = TwinSceneLookup.FindObject(PerfMonitorPath);
            if (monitor != null)
            {
                monitor.SetActive(monitorWasActive);
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            string sceneHashAfter = HashFile(scenePath);
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI play-mode smoke (editor side)  started " + startedAt);
            sb.AppendLine("  leftPlayMode=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("  scene=" + scenePath);
            sb.AppendLine("  sceneSha256Before=" + sceneHashBefore);
            sb.AppendLine("  sceneSha256After =" + sceneHashAfter);
            sb.AppendLine("  sceneUnchanged=" + string.Equals(sceneHashBefore, sceneHashAfter, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine("  PerformanceMonitor restored to active=" + monitorWasActive);
            sb.AppendLine("  csvDirectoryAfter=" + DescribeCsvDirectory());

            string shots = Path.Combine("Logs", "ui_shots");
            if (Directory.Exists(shots))
            {
                foreach (string shot in Directory.GetFiles(shots, "play_p*.png"))
                {
                    sb.AppendLine("  screenshot " + shot + " bytes=" + new FileInfo(shot).Length);
                }
            }

            string driverLog = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "ui_play_smoke.txt");
            if (File.Exists(driverLog))
            {
                sb.AppendLine();
                sb.AppendLine("---- runtime driver log ----");
                sb.AppendLine(File.ReadAllText(driverLog, Encoding.UTF8));
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("driver log missing: the driver never reached the end of its script");
            }

            Write(sb);
            Debug.Log("[UI Play Smoke] editor log: " + LogPath);
        }

        private static string DescribeCsvDirectory()
        {
            if (!Directory.Exists(CsvDirectory))
            {
                return "<absent>";
            }

            var files = new List<string>(Directory.GetFiles(CsvDirectory, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.Append("count=" + files.Count + " newest=");
            DateTime newest = DateTime.MinValue;
            string newestPath = "-";
            foreach (string file in files)
            {
                DateTime stamp = File.GetLastWriteTime(file);
                if (stamp > newest)
                {
                    newest = stamp;
                    newestPath = file;
                }
            }

            sb.Append(newestPath + " @" + newest.ToString("yyyy-MM-dd HH:mm:ss"));
            return sb.ToString();
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }
            }
            catch (Exception error)
            {
                return "<hash failed: " + error.GetType().Name + ">";
            }
        }

        private static void Write(StringBuilder sb)
        {
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}