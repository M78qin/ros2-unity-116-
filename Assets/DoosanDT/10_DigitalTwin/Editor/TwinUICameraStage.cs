//
// TwinUICameraStage.cs —— P1b-1：唯一渲染主相机 + 射线 mask 收敛 + 性能基线
//
// 菜单：
//   DigitalTwin/UI/24 Camera Audit (read-only)
//       只读审计：场景内全部 Camera 组件（含 inactive）、Camera.allCameras、Camera.main 实际解析路径、
//       每个 Canvas 的 targetCamera、以及 "仅 Default 层" 的射线探针（地板/机器人可命中性；
//       与 mask=19 对比命中数量）。写 Logs/ui_camera_audit.json，不改场景。
//   DigitalTwin/UI/25 Apply Single Main Camera
//       幂等：只把重复相机（XR Origin 相机）的 Camera 组件 enabled=false，
//       不删 GameObject、不动 XR Rig 子物体、不动 Canvas；保留 50_玩家与相机/player/Main Camera。
//       同时把保留相机上 TwinPlayRayVisual 的 hitLayers 持久化为 RayMask（仅 Default），
//       使"场景保存"只发生一次（本菜单），避免后续 UI/17 因 mask 不一致再次保存场景。
//       断言 activeAfter=1 且 Camera.main 指向保留相机；失败则不保存并打印 result=FAIL（可回退）。
//   DigitalTwin/UI/20 Play Performance Baseline (auto-exit)
//       进入 Play，跳过 1 s 预热后逐帧采 Time.unscaledDeltaTime ≥3 s，
//       输出 fps 均值/p50/p95、Camera.allCameras.Length、屏幕分辨率、可用时 FrameTiming/ProfilerRecorder，
//       截 1 张 Game 视图图，写 Logs/ui_perf_baseline.json，自动退出 Play。仅 Editor 侧基线。
//
// 约束：不改数据链 / TwinRuntimeProfile / TwinUiSettings / TagManager / Layer / EventSystem；
//       不删对象；不跑 UI/1/2/3/4/7；只经 UI/25 保存场景。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUICameraStage
    {
        private const string LogPath = "Logs/ui_camera_stage.txt";
        private const string AuditPath = "Logs/ui_camera_audit.json";
        private const string PerfPath = "Logs/ui_perf_baseline.json";
        private const string ResultTag = "[CAM]";

        /// <summary>保留的唯一渲染主相机（A1 rig/ray/IK 与 UI_二期面板都挂在它上面）。</summary>
        private const string KeptCameraPath = "50_玩家与相机/player/Main Camera";

        /// <summary>射线 mask：仅 Default 层。必须与 TwinPlayRayVisual.hitLayers 默认值、
        /// TwinUIPlayBuilder.BuildHitMask 返回值保持一致（三处同值）。</summary>
        public const int RayMask = 1 << 0;

        private const string PerfEvidenceShots =
            @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-05_p1b_camera\shots";

        private const string PerfPendingKey = "TwinUICameraStage.Perf.Pending";
        private const string PerfArmedKey = "TwinUICameraStage.Perf.ArmedAt";

        // ------------------------------------------------------------------ UI/24

        [MenuItem("DigitalTwin/UI/24 Camera Audit (read-only)", false, 240)]
        public static void Audit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Camera Audit (P1b-1, read-only)  " + NowText() + " ===");
            sb.AppendLine("unity=" + Application.unityVersion + " scene=" + SceneManager.GetActiveScene().name
                + " rayMask(probe)=" + RayMask + " (" + BitNames(RayMask) + ")");

            Scene scene = SceneManager.GetActiveScene();
            List<Camera> cams = SceneCameras(scene);
            int activeEnabled = 0;
            var jsonCameras = new List<string>();

            foreach (Camera cam in cams)
            {
                string path = PathOf(cam);
                bool goActive = cam.gameObject.activeInHierarchy;
                bool compEnabled = cam.enabled;
                bool active = goActive && compEnabled;
                if (active)
                {
                    activeEnabled++;
                }

                string urp = "none";
                try
                {
                    if (cam.GetComponent("UniversalAdditionalCameraData") != null)
                    {
                        urp = "UniversalAdditionalCameraData";
                    }
                }
                catch
                {
                    urp = "unknown";
                }

                sb.AppendLine("  cam path=" + path
                    + " goActive=" + goActive + " enabled=" + compEnabled + " isActive=" + active
                    + " depth=" + cam.depth.ToString(CultureInfo.InvariantCulture)
                    + " mask=" + cam.cullingMask + " (0x" + cam.cullingMask.ToString("X8") + " " + BitNames(cam.cullingMask) + ")"
                    + " tag=" + cam.tag
                    + " ortho=" + cam.orthographic
                    + " targetTex=" + (cam.targetTexture == null ? "-" : cam.targetTexture.name)
                    + " urpData=" + urp);

                jsonCameras.Add("{"
                    + "\"path\":" + J(path)
                    + ",\"gameObjectActive\":" + B(goActive)
                    + ",\"componentEnabled\":" + B(compEnabled)
                    + ",\"isActive\":" + B(active)
                    + ",\"depth\":" + N(cam.depth, 2)
                    + ",\"cullingMask\":" + N(cam.cullingMask)
                    + ",\"cullingMaskHex\":" + J("0x" + cam.cullingMask.ToString("X8"))
                    + ",\"tag\":" + J(cam.tag)
                    + ",\"orthographic\":" + B(cam.orthographic)
                    + ",\"targetTexture\":" + J(cam.targetTexture == null ? "" : cam.targetTexture.name)
                    + ",\"urpData\":" + J(urp)
                    + "}");
            }

            sb.AppendLine("  activeCameras=" + activeEnabled + " sceneCameraComponents=" + cams.Count);

            Camera[] all = Camera.allCameras;
            sb.AppendLine("  Camera.allCameras.Length=" + all.Length);
            var jsonAll = new List<string>();
            foreach (Camera cam in all)
            {
                string path = PathOf(cam);
                sb.AppendLine("    allCameras: " + path + " depth=" + cam.depth.ToString(CultureInfo.InvariantCulture)
                    + " mask=" + cam.cullingMask + " main=" + (cam == Camera.main));
                jsonAll.Add(J(path));
            }

            Camera main = Camera.main;
            string mainPath = main == null ? "<null>" : PathOf(main);
            string mainTagOk = main != null && main.CompareTag("MainCamera") ? "True" : "False";
            sb.AppendLine("  Camera.main=" + mainPath + " tagMainCamera=" + mainTagOk
                + " (解析规则：场景中第一个 enabled 且 tag=MainCamera 的相机)");

            // Canvas → targetCamera
            List<Canvas> canvases = SceneCanvases(scene);
            var jsonCanvases = new List<string>();
            foreach (Canvas canvas in canvases)
            {
                string target = canvas.worldCamera == null ? "" : PathOf(canvas.worldCamera);
                sb.AppendLine("  canvas name=" + canvas.name + " path=" + PathOf(canvas.transform)
                    + " active=" + canvas.gameObject.activeInHierarchy
                    + " mode=" + canvas.renderMode + " targetCamera=" + (target.Length == 0 ? "<none>" : target));
                jsonCanvases.Add("{"
                    + "\"name\":" + J(canvas.name)
                    + ",\"path\":" + J(PathOf(canvas.transform))
                    + ",\"activeInHierarchy\":" + B(canvas.gameObject.activeInHierarchy)
                    + ",\"renderMode\":" + J(canvas.renderMode.ToString())
                    + ",\"targetCamera\":" + J(target)
                    + "}");
            }

            // 射线探针：mask=1 vs mask=19 的命中对比（只读，不改场景）
            string probeSummary;
            string probeJson;
            ProbeRayMask(main, out probeSummary, out probeJson);
            sb.AppendLine("  " + probeSummary);

            bool pass = cams.Count > 0 && main != null;
            sb.AppendLine(ResultTag + " audit cameras=" + cams.Count + " active=" + activeEnabled
                + " allCameras=" + all.Length + " main=" + mainPath + " canvases=" + canvases.Count
                + " result=" + (pass ? "PASS" : "FAIL"));

            WriteLog(sb);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"generatedAt\": " + J(NowText()) + ",");
            json.AppendLine("  \"unity\": " + J(Application.unityVersion) + ",");
            json.AppendLine("  \"scene\": " + J(scene.name) + ",");
            json.AppendLine("  \"probeRayMask\": " + N(RayMask) + ",");
            json.AppendLine("  \"probeRayMaskNames\": " + J(BitNames(RayMask)) + ",");
            json.AppendLine("  \"sceneCameraComponents\": " + N(cams.Count) + ",");
            json.AppendLine("  \"activeCameras\": " + N(activeEnabled) + ",");
            json.AppendLine("  \"allCameras\": [ " + string.Join(", ", jsonAll.ToArray()) + " ],");
            json.AppendLine("  \"cameraMain\": " + J(mainPath) + ",");
            json.AppendLine("  \"cameras\": [");
            json.AppendLine("    " + string.Join("," + Environment.NewLine + "    ", jsonCameras.ToArray()));
            json.AppendLine("  ],");
            json.AppendLine("  \"canvases\": [");
            json.AppendLine("    " + string.Join("," + Environment.NewLine + "    ", jsonCanvases.ToArray()));
            json.AppendLine("  ],");
            json.AppendLine("  " + probeJson);
            json.AppendLine("}");
            WriteAllText(AuditPath, json.ToString());

            Debug.Log(ResultTag + " audit cameras=" + cams.Count + " active=" + activeEnabled
                + " allCameras=" + all.Length + " main=" + mainPath
                + " file=" + AuditPath + " result=" + (pass ? "PASS" : "FAIL"));
        }

        // ------------------------------------------------------------------ UI/25

        [MenuItem("DigitalTwin/UI/25 Apply Single Main Camera", false, 250)]
        public static void ApplySingleMainCamera()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Apply Single Main Camera (P1b-1)  " + NowText() + " ===");

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine(ResultTag + " WARN play mode active; skip (exit play first)");
                sb.AppendLine(ResultTag + " result=FAIL");
                WriteLog(sb);
                Debug.LogWarning(ResultTag + " play mode active; apply skipped");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            List<Camera> cams = SceneCameras(scene);
            int activeBefore = 0;
            foreach (Camera c in cams)
            {
                if (c.gameObject.activeInHierarchy && c.enabled)
                {
                    activeBefore++;
                }
            }

            Camera kept = TwinSceneLookup.FindComponent<Camera>(KeptCameraPath);
            if (kept == null)
            {
                sb.AppendLine(ResultTag + " FAIL kept camera missing: " + KeptCameraPath);
                WriteLog(sb);
                Debug.LogError(ResultTag + " FAIL kept camera missing: " + KeptCameraPath);
                return;
            }

            bool changed = false;
            var disabled = new List<string>();
            if (!kept.enabled)
            {
                kept.enabled = true;
                EditorUtility.SetDirty(kept);
                changed = true;
                sb.AppendLine("  enable kept camera: " + KeptCameraPath);
            }

            foreach (Camera cam in cams)
            {
                if (cam == kept)
                {
                    continue;
                }

                string path = PathOf(cam);
                bool goActive = cam.gameObject.activeInHierarchy;
                bool enabledBefore = cam.enabled;
                if (cam.enabled)
                {
                    // 只禁 Camera 组件，不删 GameObject、不动 XR Rig 子物体。
                    // 说明：XR 相机当前位于 inactive 的 XR Origin (XR Rig) 下（activeInHierarchy=False），
                    // 所以“active 渲染相机”本来只有 1 个；这里仍把它的 Camera 组件禁用，
                    // 避免以后 XR Rig 被激活时又变成双渲染。
                    cam.enabled = false;
                    EditorUtility.SetDirty(cam);
                    changed = true;
                    disabled.Add(path);
                    sb.AppendLine("  disable duplicate camera component: path=" + path
                        + " enabledBefore=True enabledAfter=False gameObjectActive=" + goActive);
                }
                else
                {
                    sb.AppendLine("  leave untouched: path=" + path + " goActive=" + goActive
                        + " enabled=" + enabledBefore + " (already disabled)");
                }
            }

            // mask 收敛的持久化（与脚本默认值同源；保证 UI/25 是唯一场景保存点）
            bool maskSynced = false;
            TwinPlayRayVisual ray = kept.GetComponent<TwinPlayRayVisual>();
            if (ray == null)
            {
                sb.AppendLine("  ray: " + KeptCameraPath + " 上没有 TwinPlayRayVisual（由 UI/6/UI/17 负责挂载），mask 未写场景");
            }
            else if (ray.HitLayers.value != RayMask)
            {
                sb.AppendLine("  ray mask: persisted " + ray.HitLayers.value + " -> " + RayMask
                    + " (" + BitNames(RayMask) + ")");
                ray.ConfigureForEditor(kept, RayMask, true);
                EditorUtility.SetDirty(ray);
                changed = true;
                maskSynced = true;
            }
            else
            {
                sb.AppendLine("  ray mask: already " + RayMask + " (" + BitNames(RayMask) + ")");
            }

            int activeAfter = 0;
            foreach (Camera c in SceneCameras(scene))
            {
                if (c.gameObject.activeInHierarchy && c.enabled)
                {
                    activeAfter++;
                }
            }

            Camera main = Camera.main;
            string mainPath = main == null ? "<null>" : PathOf(main);
            bool ok = activeAfter == 1
                && Camera.allCameras.Length == 1
                && main != null
                && string.Equals(mainPath, KeptCameraPath, StringComparison.Ordinal);

            bool saved = false;
            string sceneHash = "-";
            if (changed && ok)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                bool saveOk = true;
                try
                {
                    saveOk = EditorSceneManager.SaveScene(scene);
                }
                catch (Exception e)
                {
                    saveOk = false;
                    sb.AppendLine("  scene save failed: " + e.GetType().Name + ": " + e.Message);
                }

                saved = saveOk;
                sceneHash = HashSceneFile();
                sb.AppendLine("  saved scene=" + scene.name + " saveOk=" + saveOk + " sceneSha256=" + sceneHash);
            }
            else if (!changed)
            {
                sb.AppendLine("  no change; scene not saved (idempotent)");
            }
            else
            {
                sb.AppendLine("  assertion failed; scene NOT saved (state can be reverted by rerunning or by restoring backup)");
            }

            sb.AppendLine(ResultTag + " activeBefore=" + activeBefore + " activeAfter=" + activeAfter
                + " main=" + mainPath + " saved=" + (saved ? "yes" : "no")
                + " disabled=" + disabled.Count + " maskSynced=" + (maskSynced ? "yes" : "no")
                + " sceneSha256=" + sceneHash
                + " result=" + (ok ? "PASS" : "FAIL"));
            WriteLog(sb);

            if (ok)
            {
                Debug.Log(ResultTag + " activeBefore=" + activeBefore + " activeAfter=" + activeAfter
                    + " main=" + mainPath + " saved=" + (saved ? "yes" : "no")
                    + " disabled=" + disabled.Count + " maskSynced=" + (maskSynced ? "yes" : "no")
                    + " sceneSha256=" + sceneHash + " result=PASS");
            }
            else
            {
                Debug.LogError(ResultTag + " activeBefore=" + activeBefore + " activeAfter=" + activeAfter
                    + " main=" + mainPath + " saved=no disabled=" + disabled.Count
                    + " result=FAIL (Camera.main/allCameras assertion failed)");
            }
        }

        // ------------------------------------------------------------------ UI/20

        [MenuItem("DigitalTwin/UI/20 Play Performance Baseline (auto-exit)", false, 200)]
        public static void PlayPerformanceBaseline()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Play Performance Baseline (P1b-1, editor-side)  " + NowText() + " ===");
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine(ResultTag + " WARN already in play mode; skip");
                sb.AppendLine(ResultTag + " result=FAIL");
                WriteLog(sb);
                Debug.LogWarning(ResultTag + " perf baseline skipped: already playing");
                return;
            }

            SessionState.SetBool(PerfPendingKey, true);
            SessionState.SetString(PerfArmedKey, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  armed; entering play mode, will sample >=3s after 1s warmup, screenshot, then exit");
            WriteLog(sb);
            Debug.Log("[PERF] armed, entering play mode; will sample >=3s, shot=" + PerfEvidenceShots
                + " file=" + PerfPath);

            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookPerfUpdate()
        {
            EditorApplication.update -= PerfUpdate;
            EditorApplication.update += PerfUpdate;
        }

        private static readonly List<float> PerfDt = new List<float>();
        private static readonly List<double> PerfWall = new List<double>();
        private static double PerfSampleStart = -1;
        private static double PerfCollectStart = -1;
        private static bool PerfFinished;

        private static void PerfUpdate()
        {
            if (!SessionState.GetBool(PerfPendingKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                string armedText = SessionState.GetString(PerfArmedKey, "");
                double armed;
                if (double.TryParse(armedText, NumberStyles.Float, CultureInfo.InvariantCulture, out armed)
                    && EditorApplication.timeSinceStartup - armed > 120.0)
                {
                    FinishPerf(null, "play mode did not start within 120s");
                }

                return;
            }

            if (EditorApplication.isPaused || PerfFinished)
            {
                return;
            }

            if (PerfSampleStart < 0)
            {
                PerfSampleStart = Time.realtimeSinceStartup;
                PerfCollectStart = -1;
                PerfDt.Clear();
                PerfWall.Clear();
                return;
            }

            // 1 s 预热，避免首帧/资源上传干扰
            if (Time.realtimeSinceStartup - PerfSampleStart < 1.0)
            {
                return;
            }

            if (PerfCollectStart < 0)
            {
                PerfCollectStart = Time.realtimeSinceStartup;
            }

            PerfDt.Add(Time.unscaledDeltaTime);
            PerfWall.Add(Time.realtimeSinceStartup);

            double elapsed = Time.realtimeSinceStartup - PerfCollectStart;
            if (elapsed >= 3.0 && PerfDt.Count >= 30)
            {
                FinishPerf(PerfDt.ToArray(), null);
            }
            else if (elapsed >= 8.0)
            {
                // 兜底：极端低帧率时 8 s 内也收尾（帧数不足则如实标 FAIL）
                FinishPerf(PerfDt.ToArray(), "only " + PerfDt.Count + " frames in " + elapsed.ToString("F2") + "s");
            }
        }

        private static void FinishPerf(float[] dts, string error)
        {
            if (PerfFinished)
            {
                return;
            }

            PerfFinished = true;
            SessionState.SetBool(PerfPendingKey, false);

            var sb = new StringBuilder();
            sb.AppendLine("=== Play Performance Baseline result  " + NowText() + " ===");
            string shotPath = Path.Combine(PerfEvidenceShots, "perf_baseline_game.png").Replace('\\', '/');
            string shotNote = "";

            try
            {
                if (!Directory.Exists(PerfEvidenceShots))
                {
                    Directory.CreateDirectory(PerfEvidenceShots);
                }

                ScreenCapture.CaptureScreenshot(shotPath);
                shotNote = shotPath;
            }
            catch (Exception e)
            {
                shotNote = "capture failed: " + e.GetType().Name;
            }

            int cameras = Camera.allCameras.Length;
            int screenW = Screen.width;
            int screenH = Screen.height;
            int dispW = Display.main.systemWidth;
            int dispH = Display.main.systemHeight;
            string pipeline = "<none>";
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                pipeline = rp == null ? "<builtin>" : rp.GetType().Name;
            }
            catch
            {
                pipeline = "unknown";
            }

            string frameTimingNote = "unavailable";
            double FrameTimingAvgMs = -1;
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                var timings = new FrameTiming[1];
                uint got = FrameTimingManager.GetLatestTimings(1, timings);
                if (got > 0)
                {
                    FrameTimingAvgMs = timings[0].cpuFrameTime;
                    frameTimingNote = "cpuFrameTime=" + FrameTimingAvgMs.ToString("F2", CultureInfo.InvariantCulture) + "ms";
                }
            }
            catch (Exception e)
            {
                frameTimingNote = "unavailable (" + e.GetType().Name + ")";
            }

            long batches = -1;
            long setPass = -1;
            string profilerNote = "unavailable";
            try
            {
                using (var recBatches = Unity.Profiling.ProfilerRecorder.StartNew(
                    Unity.Profiling.ProfilerCategory.Render, "Batches"))
                using (var recSetPass = Unity.Profiling.ProfilerRecorder.StartNew(
                    Unity.Profiling.ProfilerCategory.Render, "SetPass Calls"))
                {
                    if (recBatches.Valid)
                    {
                        batches = recBatches.LastValue;
                    }

                    if (recSetPass.Valid)
                    {
                        setPass = recSetPass.LastValue;
                    }

                    profilerNote = "valid=" + recBatches.Valid + "/" + recSetPass.Valid;
                }
            }
            catch (Exception e)
            {
                profilerNote = "unavailable (" + e.GetType().Name + ")";
            }

            bool pass = error == null && dts != null && dts.Length >= 30;
            double meanFps = 0;
            double p50 = 0;
            double p95 = 0;
            double seconds = 0;
            double wallSeconds = 0;
            int frames = dts == null ? 0 : dts.Length;
            if (frames > 0)
            {
                double sum = 0;
                var fps = new List<double>(frames);
                for (int i = 0; i < frames; i++)
                {
                    float dt = dts[i];
                    sum += dt;
                    fps.Add(dt > 0.00001f ? 1.0 / dt : 0);
                }

                seconds = sum;
                if (PerfWall.Count >= 2)
                {
                    wallSeconds = PerfWall[PerfWall.Count - 1] - PerfWall[0];
                }
                else if (PerfCollectStart > 0)
                {
                    wallSeconds = Time.realtimeSinceStartup - PerfCollectStart;
                }

                // 均值用 wall clock（EditorApplication.update 与 player loop 的 tick 频率可能不一致，
                // 逐帧 dt 之和只能作参考），分位数用逐帧 1/dt。
                meanFps = wallSeconds > 0.0001 ? frames / wallSeconds : frames / sum;
                fps.Sort();
                p50 = fps[(int)Math.Floor((fps.Count - 1) * 0.50)];
                p95 = fps[(int)Math.Floor((fps.Count - 1) * 0.95)];
            }

            sb.AppendLine("  frames=" + frames + " wallSeconds=" + wallSeconds.ToString("F3", CultureInfo.InvariantCulture) + " sumFrameDeltaSeconds=" + seconds.ToString("F3", CultureInfo.InvariantCulture)
                + " meanFps=" + meanFps.ToString("F2", CultureInfo.InvariantCulture)
                + " p50=" + p50.ToString("F2", CultureInfo.InvariantCulture)
                + " p95=" + p95.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("  Camera.allCameras.Length=" + cameras);
            sb.AppendLine("  screen=" + screenW + "x" + screenH + " displaySystem=" + dispW + "x" + dispH
                + " pipeline=" + pipeline);
            sb.AppendLine("  frameTiming=" + frameTimingNote);
            sb.AppendLine("  profilerRecorder=" + profilerNote + " batches=" + batches + " setPassCalls=" + setPass);
            sb.AppendLine("  screenshot=" + shotNote);
            if (error != null)
            {
                sb.AppendLine("  error=" + error);
            }

            sb.AppendLine("[PERF] fps_mean=" + meanFps.ToString("F2", CultureInfo.InvariantCulture)
                + " fps_p50=" + p50.ToString("F2", CultureInfo.InvariantCulture)
                + " fps_p95=" + p95.ToString("F2", CultureInfo.InvariantCulture)
                + " frames=" + frames + " wallSeconds=" + wallSeconds.ToString("F3", CultureInfo.InvariantCulture)
                + " cameras=" + cameras + " screen=" + screenW + "x" + screenH
                + " batches=" + batches + " setpass=" + setPass
                + " result=" + (pass ? "PASS" : "FAIL"));
            WriteLog(sb);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"generatedAt\": " + J(NowText()) + ",");
            json.AppendLine("  \"scope\": \"editor-side play mode baseline; NOT a player build measurement\",");
            json.AppendLine("  \"frameCount\": " + N(frames) + ",");
            json.AppendLine("  \"elapsedWallSeconds\": " + N(wallSeconds, 3) + ",");
            json.AppendLine("  \"sumFrameDeltaSeconds\": " + N(seconds, 3) + ",");
            json.AppendLine("  \"fpsMean\": " + N(meanFps, 2) + ",");
            json.AppendLine("  \"fpsP50\": " + N(p50, 2) + ",");
            json.AppendLine("  \"fpsP95\": " + N(p95, 2) + ",");
            json.AppendLine("  \"percentileMethod\": " + J("nearest-rank on per-frame 1/dt, after 1s warmup") + ",");
            json.AppendLine("  \"cameraAllCamerasLength\": " + N(cameras) + ",");
            json.AppendLine("  \"screenWidth\": " + N(screenW) + ",");
            json.AppendLine("  \"screenHeight\": " + N(screenH) + ",");
            json.AppendLine("  \"displaySystemWidth\": " + N(dispW) + ",");
            json.AppendLine("  \"displaySystemHeight\": " + N(dispH) + ",");
            json.AppendLine("  \"renderPipeline\": " + J(pipeline) + ",");
            json.AppendLine("  \"frameTiming\": " + J(frameTimingNote) + ",");
            json.AppendLine("  \"profilerRecorder\": " + J(profilerNote) + ",");
            json.AppendLine("  \"batches\": " + N(batches) + ",");
            json.AppendLine("  \"setPassCalls\": " + N(setPass) + ",");
            json.AppendLine("  \"screenshot\": " + J(shotNote) + ",");
            json.AppendLine("  \"error\": " + J(error == null ? "" : error) + ",");
            json.AppendLine("  \"result\": " + J(pass ? "PASS" : "FAIL"));
            json.AppendLine("}");
            WriteAllText(PerfPath, json.ToString());

            Debug.Log("[PERF] fps_mean=" + meanFps.ToString("F2", CultureInfo.InvariantCulture)
                + " p50=" + p50.ToString("F2", CultureInfo.InvariantCulture)
                + " p95=" + p95.ToString("F2", CultureInfo.InvariantCulture)
                + " cameras=" + cameras + " result=" + (pass ? "PASS" : "FAIL")
                + " file=" + PerfPath);

            EditorApplication.isPlaying = false;
            PerfDt.Clear();
            PerfSampleStart = -1;
            PerfCollectStart = -1;
            PerfFinished = false;
        }

        // ------------------------------------------------------------------ helpers

        private static List<Camera> SceneCameras(Scene scene)
        {
            var list = new List<Camera>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<Camera>(true));
            }

            list.Sort((a, b) => string.CompareOrdinal(PathOf(a), PathOf(b)));
            return list;
        }

        private static List<Canvas> SceneCanvases(Scene scene)
        {
            var list = new List<Canvas>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<Canvas>(true));
            }

            list.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
            return list;
        }

        private static string PathOf(Component c)
        {
            return c == null || c.transform == null ? "<null>" : TwinSceneLookup.PathOf(c.transform);
        }

        private static void ProbeRayMask(Camera camera, out string summary, out string json)
        {
            if (camera == null)
            {
                summary = "ray probe skipped (Camera.main is null)";
                json = "\"rayProbe\": { \"skipped\": true }";
                return;
            }

            int w = camera.pixelWidth;
            int h = camera.pixelHeight;
            const int cols = 48;
            const int rows = 27;
            int hitsMask1 = 0;
            int hitsMask19 = 0;
            int robotHits = 0;
            var names = new HashSet<string>();
            var robotNames = new HashSet<string>();
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var sp = new Vector3((x + 0.5f) * w / cols, (y + 0.5f) * h / rows, 0);
                    Ray ray = camera.ScreenPointToRay(sp);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 200f, RayMask, QueryTriggerInteraction.Ignore))
                    {
                        hitsMask1++;
                        names.Add(hit.collider.gameObject.name);
                        string path = PathOf(hit.collider.transform);
                        if (path.IndexOf("Doosan_Robot", StringComparison.OrdinalIgnoreCase) >= 0
                            || hit.collider.gameObject.name.IndexOf("H2515", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            robotHits++;
                            robotNames.Add(hit.collider.gameObject.name);
                        }
                    }

                    if (Physics.Raycast(ray, out hit, 200f, 19, QueryTriggerInteraction.Ignore))
                    {
                        hitsMask19++;
                    }
                }
            }

            int samples = cols * rows;
            summary = "rayProbe mask=" + RayMask + " (" + BitNames(RayMask) + ") rays=" + samples
                + " hits=" + hitsMask1 + " distinct=" + names.Count
                + " robotHits=" + robotHits + " (mask19 hits=" + hitsMask19 + ", same=" + (hitsMask1 == hitsMask19) + ")";
            json = "\"rayProbe\": { \"mask\": " + N(RayMask)
                + ", \"maskNames\": " + J(BitNames(RayMask))
                + ", \"rays\": " + N(samples)
                + ", \"hits\": " + N(hitsMask1)
                + ", \"distinctHitObjects\": " + N(names.Count)
                + ", \"robotHits\": " + N(robotHits)
                + ", \"robotDistinct\": " + N(robotNames.Count)
                + ", \"mask19Hits\": " + N(hitsMask19)
                + ", \"sameAsMask19\": " + B(hitsMask1 == hitsMask19)
                + " }";
        }

        private static string BitNames(int mask)
        {
            var parts = new List<string>();
            for (int layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) == 0)
                {
                    continue;
                }

                string name = LayerMask.LayerToName(layer);
                parts.Add(layer + ":" + (string.IsNullOrEmpty(name) ? "Layer" + layer : name));
            }

            return string.Join(",", parts.ToArray());
        }

        private static string HashSceneFile()
        {
            try
            {
                string path = SceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return "-";
                }

                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                return "-";
            }
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string J(string s)
        {
            if (s == null)
            {
                return "null";
            }

            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }

            return sb.Append("\"").ToString();
        }

        private static string N(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static string N(double v, int digits)
        {
            return v.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        private static string B(bool v)
        {
            return v ? "true" : "false";
        }

        private static void WriteLog(StringBuilder sb)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(LogPath, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
                // ignore log write failure; console output still exists
            }
        }

        private static void WriteAllText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}