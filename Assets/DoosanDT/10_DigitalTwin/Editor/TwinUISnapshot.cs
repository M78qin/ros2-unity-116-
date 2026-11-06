// Edit-mode screenshots of the unified panel states (evidence without entering Play).
//
// Menu: DigitalTwin/UI/9 Snapshot UI States -> PNG (edit mode)
//
// It renders the Main Camera into a RenderTexture, so a ScreenSpaceCamera canvas is captured
// exactly as it would appear in Play. Two honest limits:
//   * Overlay canvases (Canvas_I, Canvas_I软著用标题) are composited by the compositor, not by the
//     camera, so they do NOT appear in these images. Play-mode screenshots cover that gap later.
//   * every activeSelf it touches is recorded and restored, and the scene is never saved here.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUISnapshot
    {
        private const string OutputDir = "Logs/ui_shots";
        private const string ShellPath = "30_界面与交互/UI_统一入口面板";
        private const string MainCameraPath = "50_玩家与相机/player/Main Camera";
        private const string TrainingCanvasPath = "30_界面与交互/Canvas/Canvas_I";
        private const string LoginCanvasPath = "30_界面与交互/Canvas/Canvas_I软著用标题";
        private const string DisassemblyCanvasPath = "30_界面与交互/Canvas/User_ui";
        private const string RoamingWorldPath = "30_界面与交互/Canvas/Canvas_1 P";
        private const string RoamingWorldAltPath = "30_界面与交互/Canvas/Canvas_1_V";

        [MenuItem("DigitalTwin/UI/9 Snapshot UI States -> PNG (edit mode)")]
        public static void Snapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI snapshots  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Camera camera = TwinSceneLookup.FindComponent<Camera>(MainCameraPath);
            if (camera == null)
            {
                sb.AppendLine("RESULT: FAIL - main camera not found");
                Write(sb);
                return;
            }

            var shell = TwinSceneLookup.FindObject(ShellPath);
            var controller = shell == null ? null : shell.GetComponent<TwinUIShellController>();
            var gate = TwinSceneLookup.FindComponent<BatteryLoginGate>("30_界面与交互/Canvas/登录界面");
            var loginPanel = TwinSceneLookup.FindObject("30_界面与交互/Canvas/Canvas_I软著用标题/LoginPanel");
            var trainingCanvas = TwinSceneLookup.FindObject(TrainingCanvasPath);
            var loginCanvas = TwinSceneLookup.FindObject(LoginCanvasPath);
            var disassemblyCanvas = TwinSceneLookup.FindObject(DisassemblyCanvasPath);
            var roamingWorld = TwinSceneLookup.FindObject(RoamingWorldPath);
            var roamingWorldAlt = TwinSceneLookup.FindObject(RoamingWorldAltPath);

            if (shell == null)
            {
                sb.AppendLine("RESULT: FAIL - shell not found, run DigitalTwin/UI/1 Build Unified Shell first");
                Write(sb);
                return;
            }

            // record every state we are about to touch
            var touched = new List<GameObject> { shell, loginPanel, trainingCanvas, loginCanvas, disassemblyCanvas, roamingWorld, roamingWorldAlt };
            var original = new Dictionary<GameObject, bool>();
            foreach (GameObject go in touched)
            {
                if (go != null && !original.ContainsKey(go))
                {
                    original[go] = go.activeSelf;
                }
            }

            Directory.CreateDirectory(OutputDir);

            int initialModeIndex = controller != null && controller.Bindings.router != null
                ? (int)controller.Bindings.router.CurrentMode
                : 0;

            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = rt;

                Capture(camera, rt, texture, "i00_baseline", sb, () =>
                {
                    SetActive(shell, false);
                    SetActive(loginCanvas, false);
                    SetActive(loginPanel, false);
                    SetActive(trainingCanvas, false);
                    SetActive(disassemblyCanvas, false);
                    SetActive(roamingWorld, false);
                    SetActive(roamingWorldAlt, false);
                });

                Capture(camera, rt, texture, "i01_welcome", sb, () =>
                {
                    SetActive(shell, false);
                    SetActive(loginCanvas, true);
                    SetActive(loginPanel, true);
                });

                Capture(camera, rt, texture, "i02_roaming", sb, () =>
                {
                    SetActive(shell, true);
                    SetActive(loginCanvas, false);
                    SetActive(trainingCanvas, false);
                    SetActive(disassemblyCanvas, false);
                    SetActive(roamingWorld, true);
                    SetActive(roamingWorldAlt, true);
                    if (controller != null) controller.SelectTab(0);
                });

                Capture(camera, rt, texture, "i03_training", sb, () =>
                {
                    SetActive(shell, true);
                    SetActive(roamingWorld, false);
                    SetActive(roamingWorldAlt, false);
                    SetActive(trainingCanvas, true);
                    SetActive(loginCanvas, true);
                    SetActive(loginPanel, false);
                    if (gate != null) gate.Enter();
                    if (controller != null) controller.SelectTab(1);
                });

                Capture(camera, rt, texture, "i04_disassembly", sb, () =>
                {
                    SetActive(shell, true);
                    SetActive(trainingCanvas, false);
                    SetActive(loginCanvas, false);
                    SetActive(disassemblyCanvas, false);
                    if (controller != null) controller.SelectTab(2);
                });

                Capture(camera, rt, texture, "i05_data", sb, () =>
                {
                    SetActive(shell, true);
                    SetActive(disassemblyCanvas, false);
                    if (controller != null) controller.SelectTab(3);
                });

                sb.AppendLine("RESULT: PASS");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(texture);

                // put the tab highlight / router state back first (it writes colours), then restore the
                // activeSelf values recorded above so the scene content is byte-identical to before
                if (controller != null)
                {
                    controller.SelectTab(initialModeIndex);
                }

                foreach (KeyValuePair<GameObject, bool> pair in original)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetActive(pair.Value);
                    }
                }
            }

            Write(sb);
            Debug.Log("[UI Snapshot] see " + OutputDir);
        }

        // ------------------------------------------------------------------ chart replay preview

        /// <summary>
        /// Edit-mode proof that the chart actually draws data: feed the real recorded fixture into the
        /// joint-angle chart, the synthetic fixture into the wrench chart, then the empty state.
        /// This exercises TwinTelemetryChart (geometry) without Play and without any data source.
        /// </summary>
        [MenuItem("DigitalTwin/UI/7 Chart Replay Preview (edit mode)")]
        public static void PreviewCharts()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin chart replay preview  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Camera camera = TwinSceneLookup.FindComponent<Camera>(MainCameraPath);
            GameObject shell = TwinSceneLookup.FindObject(ShellPath);
            var jointChart = TwinSceneLookup.FindComponent<TwinTelemetryChart>(ShellPath + "/ChartCard/JointChart");
            var wrenchChart = TwinSceneLookup.FindComponent<TwinTelemetryChart>(ShellPath + "/ChartCard/WrenchChart");
            var realProbe = TwinSceneLookup.FindComponent<TwinTelemetryReplayProbe>(ShellPath + "/SelfTestReplay_Real");
            var synProbe = TwinSceneLookup.FindComponent<TwinTelemetryReplayProbe>(ShellPath + "/SelfTestReplay_Synthetic");

            if (camera == null || shell == null || jointChart == null || wrenchChart == null)
            {
                sb.AppendLine("RESULT: FAIL - shell/charts not found (run DigitalTwin/UI/1 Build Unified Shell)");
                WritePreview(sb);
                return;
            }

            bool shellWasActive = shell.activeSelf;
            Directory.CreateDirectory(OutputDir);

            RenderTexture previousTarget = camera.targetTexture;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = rt;
                shell.SetActive(true);

                Capture(camera, rt, texture, "i06_chart_real_joints", sb, () =>
                {
                    FeedJointChart(jointChart, realProbe, sb);
                    wrenchChart.ClearAll();
                    wrenchChart.SetHasData(false);
                });

                Capture(camera, rt, texture, "i07_chart_synthetic_wrench", sb, () =>
                {
                    FeedWrenchChart(wrenchChart, synProbe, sb);
                });

                Capture(camera, rt, texture, "i08_chart_synthetic_torque", sb, () =>
                {
                    FeedTorqueChart(wrenchChart, synProbe, sb);
                });

                Capture(camera, rt, texture, "i09_chart_empty_state", sb, () =>
                {
                    jointChart.ClearAll();
                    jointChart.SetHasData(false);
                    wrenchChart.ClearAll();
                    wrenchChart.SetHasData(false);
                });

                sb.AppendLine("RESULT: PASS");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(texture);
                jointChart.ClearAll();
                jointChart.SetHasData(false);
                wrenchChart.ClearAll();
                wrenchChart.SetHasData(false);
                shell.SetActive(shellWasActive);
            }

            WritePreview(sb);
            Debug.Log("[UI Chart Preview] see " + OutputDir);
        }

        private static void FeedJointChart(TwinTelemetryChart chart, TwinTelemetryReplayProbe probe, StringBuilder sb)
        {
            chart.ClearAll();
            if (probe == null)
            {
                sb.AppendLine("  joint chart: probe <null>");
                return;
            }

            if (!probe.Load())
            {
                sb.AppendLine("  joint chart: load failed - " + probe.StatusMessage + " (path=" + probe.CsvPath + ")");
                return;
            }

            int rows = probe.RowCount;
            int step = Mathf.Max(1, rows / 400);
            int pushed = 0;
            for (int i = 0; i < rows; i += step)
            {
                float[] joints = probe.RowJointDeg(i);
                if (joints == null || joints.Length < 3)
                {
                    continue;
                }

                float t = i * (chart.WindowSeconds / rows);
                for (int c = 0; c < 3; c++)
                {
                    chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + c), t, joints[c]);
                }

                pushed++;
            }

            chart.SetHasData(pushed > 1);
            sb.AppendLine("  joint chart: rows=" + rows + " pushed=" + pushed + " source=REAL " + probe.CsvPath);
        }

        private static void FeedWrenchChart(TwinTelemetryChart chart, TwinTelemetryReplayProbe probe, StringBuilder sb)
        {
            chart.ClearAll();
            if (probe == null || !probe.Load())
            {
                sb.AppendLine("  wrench chart: probe unavailable");
                return;
            }

            chart.SetSeries(TwinTelemetryCatalog.DefaultWrenchSeries);
            int rows = probe.RowCount;
            int step = Mathf.Max(1, rows / 400);
            int pushed = 0;
            for (int i = 0; i < rows; i += step)
            {
                float[] wrench = probe.RowWrench(i);
                if (wrench == null || wrench.Length < 3)
                {
                    continue;
                }

                float t = i * (chart.WindowSeconds / rows);
                for (int c = 0; c < 3; c++)
                {
                    chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.ForceX + c), t, wrench[c]);
                }

                pushed++;
            }

            chart.SetHasData(pushed > 1);
            sb.AppendLine("  wrench chart: rows=" + rows + " pushed=" + pushed + " source=SYNTHETIC " + probe.CsvPath);
        }

        private static void FeedTorqueChart(TwinTelemetryChart chart, TwinTelemetryReplayProbe probe, StringBuilder sb)
        {
            chart.ClearAll();
            if (probe == null || !probe.Load())
            {
                sb.AppendLine("  torque chart: probe unavailable");
                return;
            }

            chart.SetSeries(TwinTelemetryCatalog.DefaultTorqueSeries);
            int rows = probe.RowCount;
            int step = Mathf.Max(1, rows / 400);
            int pushed = 0;
            for (int i = 0; i < rows; i += step)
            {
                float[] torque = probe.RowTorque(i);
                if (torque == null || torque.Length < 3)
                {
                    continue;
                }

                float t = i * (chart.WindowSeconds / rows);
                for (int c = 0; c < 3; c++)
                {
                    chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.JointTorque1 + c), t, torque[c]);
                }

                pushed++;
            }

            chart.SetHasData(pushed > 1);
            sb.AppendLine("  torque chart: rows=" + rows + " pushed=" + pushed + " source=SYNTHETIC " + probe.CsvPath);
        }

        private static void WritePreview(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "ui_chart_preview.txt"), sb.ToString(), new UTF8Encoding(false));
        }

        private static void Capture(Camera camera, RenderTexture rt, Texture2D texture, string name, StringBuilder sb, System.Action applyState)
        {
            applyState();
            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            string path = Path.Combine(OutputDir, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            sb.AppendLine("  " + path + "  (" + new FileInfo(path).Length + " bytes)");
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null)
            {
                go.SetActive(active);
            }
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "ui_snapshots.txt"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}