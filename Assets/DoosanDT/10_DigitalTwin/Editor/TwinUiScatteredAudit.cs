// TwinUiScatteredAudit.cs —— 散落 UI 盘点 + 漫游能力盘点（UI/51，只读，auto-exit）
// 目的：进入 V2 Play 后，列出"主面板之外还显示着什么"（Canvas / IMGUI / 世界 HUD / 欢迎页 / 旧壳页面），
//       以及漫游相机当前键鼠能力与状态；输出 Logs/ui_scattered_audit.txt 供下一步整合与回归对照。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiScatteredAudit
    {
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_scattered";
        public const string LogPath = "Logs/ui_scattered_audit.txt";

        [MenuItem("DigitalTwin/UI/51 Scattered UI & Roam Audit (auto-exit)", false, 510)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SCATTER] UI/51 skipped: already in play mode");
                return;
            }

            TwinUiScatteredCheck.Reset();
            SessionState.SetBool(TwinUiScatteredCheck.PendingKey, true);
            Debug.Log("[SCATTER] UI/51 armed; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiScatteredCheck
    {
        public const string PendingKey = "TwinUiScatteredCheck.Pending";
        private const string EvidenceLogs = TwinUiScatteredAudit.EvidenceRoot + @"\logs";
        private const string EvidenceShots = TwinUiScatteredAudit.EvidenceRoot + @"\shots";

        private static readonly StringBuilder Sb = new StringBuilder();
        private static float _startAt;
        private static bool _started;
        private static bool _finished;
        private static float _shotAt;
        private static int _shotPhase;
        private static int _logErrors;

        public static void Reset()
        {
            Sb.Clear();
            _started = false;
            _finished = false;
            _startAt = 0f;
            _shotAt = 0f;
            _shotPhase = 0;
            _logErrors = 0;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying || _finished)
            {
                return;
            }

            if (!_started)
            {
                if (UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>() == null)
                {
                    return;
                }

                _started = true;
                _startAt = Time.realtimeSinceStartup;
                _shotAt = _startAt + 1.2f;
                Application.logMessageReceived += OnLog;
                Sb.AppendLine("=== Scattered UI & Roam Audit (UI/51)  "
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                Sb.AppendLine("shellMode=" + TwinUiShellMode.Kind
                    + " unity=" + Application.unityVersion + " resolution=" + Screen.width + "x" + Screen.height);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_shotPhase == 0 && now >= _shotAt)
            {
                Capture("210_scattered_before_overview_1280.png");
                DumpAll();
                _shotPhase = 1;
                _shotAt = now + 0.8f;
                return;
            }

            if (_shotPhase == 1 && now >= _shotAt)
            {
                Capture("211_scattered_before_hidden.png");
                _shotPhase = 2;
                _shotAt = now + 0.3f;
                return;
            }

            if (_shotPhase == 2 && now >= _shotAt)
            {
                Finish();
            }
        }

        private static void DumpAll()
        {
            DumpCanvases();
            DumpImguiPanels();
            DumpLegacyShell();
            DumpRoam();
            DumpV2();
        }

        private static void DumpCanvases()
        {
            Sb.AppendLine();
            Sb.AppendLine("## Canvas 清单（含运行时创建的 HUD Canvas）");
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i];
                if (c == null)
                {
                    continue;
                }

                var scaler = c.GetComponent<CanvasScaler>();
                Sb.AppendLine("- [" + (c.isActiveAndEnabled ? "ACTIVE" : "off") + "] root=" + c.name
                    + " mode=" + c.renderMode
                    + " order=" + c.sortingOrder
                    + " scaler=" + (scaler != null ? scaler.uiScaleMode.ToString() : "none")
                    + " parent=" + (c.transform.parent != null ? c.transform.parent.name : "-")
                    + " path=" + Path(c.transform));
            }
        }

        private static void DumpImguiPanels()
        {
            Sb.AppendLine();
            Sb.AppendLine("## IMGUI / 世界 HUD 面板状态");
            var perf = UnityEngine.Object.FindObjectOfType<PerformanceProfiler>();
            Sb.AppendLine("- PerformanceProfiler found=" + (perf != null)
                + (perf != null ? " onScreenUI=" + perf.IsOnScreenUIVisible() : string.Empty));

            var dis = DisassemblySystem.Instance;
            Sb.AppendLine("- DisassemblySystem found=" + (dis != null)
                + (dis != null ? " debugVisible=" + dis.debugSettings != null ? dis.debugSettings.debugVisible.ToString() : "unknown" : string.Empty));

            var ray = UnityEngine.Object.FindObjectOfType<TwinPlayRayVisual>();
            Sb.AppendLine("- TwinPlayRayVisual found=" + (ray != null)
                + (ray != null ? " hud=" + ray.ShowHud + " hudCreated=" + ray.HudCreated
                    + " text=\"" + Short(ray.HudTextValue, 90) + "\"" : string.Empty));

            var ik = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();
            Sb.AppendLine("- TwinPlayIkHandle found=" + (ik != null));

            var dart = UnityEngine.Object.FindObjectOfType<DartStudioBridge>();
            Sb.AppendLine("- DartStudioBridge found=" + (dart != null));

            var smart = UnityEngine.Object.FindObjectOfType<SmartRaycaster>();
            Sb.AppendLine("- SmartRaycaster found=" + (smart != null)
                + (smart != null ? " enabled=" + smart.enabled + " active=" + smart.gameObject.activeInHierarchy : string.Empty));
        }

        private static void DumpLegacyShell()
        {
            Sb.AppendLine();
            Sb.AppendLine("## 旧欢迎页 / 旧壳");
            var gate = UnityEngine.Object.FindObjectOfType<BatteryLoginGate>();
            Sb.AppendLine("- BatteryLoginGate found=" + (gate != null)
                + (gate != null ? " hasEntered=" + gate.HasEntered + " active=" + gate.gameObject.activeInHierarchy : string.Empty));
            var shell = UnityEngine.Object.FindObjectOfType<TwinUIShellController>();
            Sb.AppendLine("- TwinUIShellController found=" + (shell != null)
                + (shell != null ? " active=" + shell.gameObject.activeInHierarchy : string.Empty));
            var router = UnityEngine.Object.FindObjectOfType<TwinDemoModeRouter>();
            Sb.AppendLine("- TwinDemoModeRouter found=" + (router != null));
            var cmd = UnityEngine.Object.FindObjectOfType<TwinCommandPanelView>();
            Sb.AppendLine("- TwinCommandPanelView found=" + (cmd != null)
                + (cmd != null ? " activeInHierarchy=" + cmd.gameObject.activeInHierarchy
                    + " enabled=" + cmd.enabled + " path=" + Path(cmd.transform) : string.Empty));

            string[] legacyRoots = { "Canvas", "Canvas_1 P", "Canvas_1_V", "Canvas_I", "Canvas_I软著用标题", "UI_统一入口面板" };
            var all = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < legacyRoots.Length; i++)
            {
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j] != null && all[j].name == legacyRoots[i])
                    {
                        Sb.AppendLine("- legacy root \"" + legacyRoots[i] + "\" activeSelf="
                            + all[j].gameObject.activeSelf + " activeInHierarchy="
                            + all[j].gameObject.activeInHierarchy);
                        break;
                    }
                }
            }
        }

        private static void DumpRoam()
        {
            Sb.AppendLine();
            Sb.AppendLine("## 漫游（键鼠）能力与状态");
            var rig = UnityEngine.Object.FindObjectOfType<TwinPlayCameraRig>();
            if (rig == null)
            {
                Sb.AppendLine("- TwinPlayCameraRig NOT FOUND");
                return;
            }

            Sb.AppendLine("- rig active=" + rig.gameObject.activeInHierarchy + " enabled=" + rig.enabled
                + " resolved=" + rig.IsResolved + " ownsArbiter=" + rig.OwnsInputArbiter
                + " respectArbiter=" + rig.RespectInputArbiter);
            Sb.AppendLine("- camera=" + (rig.ActiveCamera != null ? rig.ActiveCamera.name : "null")
                + " moveRoot=" + (rig.MoveRoot != null ? rig.MoveRoot.name : "null"));
            Sb.AppendLine("- speed current=" + rig.CurrentSpeed.ToString("F2")
                + " base=" + rig.MoveSpeed.ToString("F2"));
            Sb.AppendLine("- pose yaw=" + rig.Yaw.ToString("F1") + " pitch=" + rig.Pitch.ToString("F1")
                + " pos=" + rig.RigPosition.ToString("F2"));
            Sb.AppendLine("- rightMouseHeld=" + rig.IsRightMouseHeld);
            Sb.AppendLine("- disabledConflicting=" + rig.DisabledControllers);
        }

        private static void DumpV2()
        {
            Sb.AppendLine();
            Sb.AppendLine("## V2 主面板状态");
            var host = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            var shell = host != null ? host.ShellV2 : null;
            var surface = shell != null ? shell.Surface : null;
            Sb.AppendLine("- host=" + (host != null) + " windowVisible=" + (host != null && host.IsVisible)
                + " minimized=" + (host != null && host.IsMinimized) + " hidden=" + (host != null && host.IsHidden));
            Sb.AppendLine("- shellV2=" + (shell != null)
                + (shell != null ? " tab=" + shell.CurrentTab + " tabs=" + shell.TabCount
                    + " viewport=" + shell.ViewportWidth.ToString("F0") + "x" + shell.ViewportHeight.ToString("F0") : string.Empty));
            Sb.AppendLine("- surface=" + (surface != null)
                + (surface != null ? " cards=" + surface.CardCount + " floating=" + surface.FloatingCount
                    + " modules=" + surface.ModuleCount : string.Empty));
            Sb.AppendLine("- taskbar=" + (shell != null && shell.Taskbar != null));
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static void Capture(string name)
        {
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(EvidenceShots, name));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SCATTER] shot failed: " + e.Message);
            }
        }

        private static void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(PendingKey, false);
            Sb.AppendLine();
            Sb.AppendLine("[SCATTER] logErrors=" + _logErrors + " result=" + (_logErrors == 0 ? "PASS" : "FAIL"));
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(TwinUiScatteredAudit.LogPath, Sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(TwinUiScatteredAudit.LogPath, System.IO.Path.Combine(EvidenceLogs, "ui51_scattered_audit.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SCATTER] write failed: " + e.Message);
            }

            Debug.Log("[SCATTER] finished log=" + TwinUiScatteredAudit.LogPath);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            };
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _logErrors++;
                Sb.AppendLine("[SCATTER] [logfail] " + type + " " + condition);
            }
        }
    }
}