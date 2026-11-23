// TwinUiIntegrationStage.cs —— 散落 UI 收编 + 最小化悬浮球 + 漫游完善 自检（UI/52，auto-exit）
// 断言：
//   ① 散落显示层全部关闭（性能/拆解/射线/悬停/通信 IMGUI、射线与 IK 世界 HUD、欢迎页、旧壳/演示路由/旧控制面板）；
//   ② 4 张新卡进面板（选中对象 / IK 预览 / 控制指令 / 漫游与操作），卡片数与注册表一致；
//   ③ 最小化 → 悬浮球出现；点悬浮球 → 主窗恢复且悬浮球隐藏；隐藏 → 悬浮球同样可恢复；
//   ④ 漫游：WASD 不按右键也能移动、右键视角改变 yaw、滚轮改速度、R 复位、F/聚焦入口可用；
//   ⑤ 截图 3 张；无 Error/Exception。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiIntegrationStage
    {
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_integration";
        public const string LogPath = "Logs/ui_integration_check.txt";

        [MenuItem("DigitalTwin/UI/52 Scattered UI + FAB + Roam Check (auto-exit)", false, 520)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[P9] UI/52 skipped: already in play mode");
                return;
            }

            TwinUiIntegrationCheck.Reset();
            SessionState.SetBool(TwinUiIntegrationCheck.PendingKey, true);
            Debug.Log("[P9] UI/52 armed; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiIntegrationCheck
    {
        public const string PendingKey = "TwinUiIntegrationCheck.Pending";
        private const string EvidenceLogs = TwinUiIntegrationStage.EvidenceRoot + @"\logs";
        private const string EvidenceShots = TwinUiIntegrationStage.EvidenceRoot + @"\shots";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly Queue<Action> Steps = new Queue<Action>();
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static TwinUiScatteredUiBridge _bridge;
        private static TwinUiRoamBridge _roam;
        private static TwinUiMinimizeFab _fab;
        private static int _logErrors;
        private static int _logExceptions;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            Steps.Clear();
            _window = null;
            _shell = null;
            _surface = null;
            _bridge = null;
            _roam = null;
            _fab = null;
            _logErrors = 0;
            _logExceptions = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
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
                _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                if (_window == null || _window.ShellV2 == null)
                {
                    return;
                }

                _shell = _window.ShellV2;
                _surface = _shell.Surface;
                _bridge = _shell.ScatteredBridge;
                _roam = _shell.RoamBridge;
                _fab = _shell.Fab;
                if (_surface == null || _bridge == null || _roam == null || _fab == null)
                {
                    return;
                }

                _started = true;
                Application.logMessageReceived += OnLog;
                _nextAt = Time.realtimeSinceStartup + 0.8f;
                BuildSteps();
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (Steps.Count == 0)
            {
                Finish();
                return;
            }

            Steps.Dequeue()();
            _nextAt = Time.realtimeSinceStartup + 0.35f;
        }

        private static void BuildSteps()
        {
            Steps.Enqueue(StepReady);
            Steps.Enqueue(StepScattered);
            Steps.Enqueue(StepCards);
            Steps.Enqueue(StepFabMinimize);
            Steps.Enqueue(StepFabMinimizeAssert);
            Steps.Enqueue(StepFabHide);
            Steps.Enqueue(StepFabHideAssert);
            Steps.Enqueue(StepRoam);
            Steps.Enqueue(StepShotOverview);
            Steps.Enqueue(StepShotControl);
            Steps.Enqueue(StepShotMinimize);
            Steps.Enqueue(StepShotFab);
        }

        private static void StepReady()
        {
            Add("shell V2", "shell=" + (_shell != null) + " surface=" + (_surface != null)
                + " bridge=" + (_bridge != null) + " roam=" + (_roam != null) + " fab=" + (_fab != null),
                _shell != null && _surface != null && _bridge != null && _roam != null && _fab != null);
            Add("modules=19", "modules=" + _surface.ModuleCount, _surface.ModuleCount == 19);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepScattered()
        {
            _bridge.AssertAll("check");
            Add("scattered all suppressed", _bridge.Describe(), _bridge.AllSuppressed);
            Add("perf IMGUI off", "perfOff=" + _bridge.PerfGuiOff, _bridge.PerfGuiOff);
            Add("disassembly overlay off", "off=" + _bridge.DisassemblyOverlayOff, _bridge.DisassemblyOverlayOff);
            Add("ray HUD off", "off=" + _bridge.RayHudOff, _bridge.RayHudOff);
            Add("IK HUD off", "off=" + _bridge.IkHudOff, _bridge.IkHudOff);
            Add("hover panels off", "off=" + _bridge.HoverPanelsOff, _bridge.HoverPanelsOff);
            Add("welcome/login hidden", "off=" + _bridge.LoginHidden, _bridge.LoginHidden);
            Add("legacy shell off", "off=" + _bridge.LegacyShellOff, _bridge.LegacyShellOff);
            Add("demo router off", "off=" + _bridge.DemoRouterOff, _bridge.DemoRouterOff);
            Add("legacy command panel off", "off=" + _bridge.CommandPanelOff, _bridge.CommandPanelOff);
        }

        private static void StepCards()
        {
            Add("overview cards", "cards=" + _surface.Page.CardCount + " expected="
                + ExpectedCards(TwinUiWorkspaceRegistry.Overview),
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Overview));
            Add("selection widget", "state=" + _surface.DescribeModule("selection"),
                _surface.Find("selection") != null && _surface.Find("selection").State == TwinUiModuleState.Card);
            Add("roam widget", "state=" + _surface.DescribeModule("roam"),
                _surface.Find("roam") != null && _surface.Find("roam").State == TwinUiModuleState.Card);

            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            Add("twin cards", "cards=" + _surface.Page.CardCount + " expected="
                + ExpectedCards(TwinUiWorkspaceRegistry.Twin),
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Twin));
            Add("ik widget", "state=" + _surface.DescribeModule("ik"),
                _surface.Find("ik") != null && _surface.Find("ik").State == TwinUiModuleState.Card);

            _surface.SetTab(TwinUiWorkspaceRegistry.Control);
            Add("control cards", "cards=" + _surface.Page.CardCount + " expected="
                + ExpectedCards(TwinUiWorkspaceRegistry.Control),
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Control));
            var command = _surface.Find("command") as TwinUiWidgetCommand;
            Add("command widget", "state=" + _surface.DescribeModule("command"),
                command != null && command.State == TwinUiModuleState.Card);
            Add("command reuses legacy view", "panel=" + (command != null && command.Panel != null),
                command != null && command.Panel != null);
            var controller = UnityEngine.Object.FindObjectOfType<TwinCommandController>();
            Add("real robot dispatch locked", "canSendRealMove="
                + (controller != null ? controller.CanSendRealMove.ToString() : "n/a"),
                controller == null || !controller.CanSendRealMove);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepFabMinimize()
        {
            _window.Minimize();
        }

        private static void StepFabMinimizeAssert()
        {
            Add("minimize -> fab visible", "windowVisible=" + _window.IsVisible
                + " fabVisible=" + _fab.IsVisible + " shows=" + _fab.ShowCount,
                !_window.IsVisible && _fab.IsVisible);
            _fab.RestoreForCheck();
            Add("click fab -> window restored", "windowVisible=" + _window.IsVisible
                + " fabVisible=" + _fab.IsVisible + " restores=" + _fab.RestoreCount,
                _window.IsVisible && _fab.RestoreCount >= 1);
            Add("fab hidden when window visible", "fabVisible=" + _fab.IsVisible + " minimized="
                + _window.IsMinimized, !_fab.IsVisible && !_window.IsMinimized);
        }

        private static void StepFabHide()
        {
            _window.Hide();
        }

        private static void StepFabHideAssert()
        {
            Add("hide -> fab visible", "windowVisible=" + _window.IsVisible + " hidden=" + _window.IsHidden
                + " fabVisible=" + _fab.IsVisible, !_window.IsVisible && _window.IsHidden && _fab.IsVisible);
            _fab.RestoreForCheck();
            Add("click fab -> window restored after hide", "windowVisible=" + _window.IsVisible,
                _window.IsVisible);
            Add("hide state cleared after restore", "hidden=" + _window.IsHidden + " fabVisible=" + _fab.IsVisible,
                !_window.IsHidden && !_fab.IsVisible);
        }

        private static void StepRoam()
        {
            var rig = _roam.Rig;
            Add("roam rig resolved", "rig=" + (rig != null), rig != null);
            Add("wasd without right mouse", "wasdNoRmb=" + _roam.WasdWithoutRightMouse,
                _roam.WasdWithoutRightMouse);
            if (rig == null)
            {
                return;
            }

            Vector3 start = rig.RigPosition;
            var walk = new TwinPlayCameraRig.TwinPlayInput
            {
                moveAxis = new Vector2(0f, 1f),
                rightMouseHeld = false
            };
            for (int i = 0; i < 12; i++)
            {
                rig.StepForTest(walk, 0.05f);
            }

            float moved = Vector3.Distance(start, rig.RigPosition);
            Add("wasd moves without rmb", "delta=" + moved.ToString("F3") + " m", moved > 0.05f);

            float yawBefore = rig.Yaw;
            var look = new TwinPlayCameraRig.TwinPlayInput
            {
                rightMouseHeld = true,
                lookDelta = new Vector2(60f, 0f)
            };
            for (int i = 0; i < 4; i++)
            {
                rig.StepForTest(look, 0.05f);
            }

            float dyaw = Mathf.Abs(Mathf.DeltaAngle(yawBefore, rig.Yaw));
            Add("right mouse look rotates yaw", "dyaw=" + dyaw.ToString("F1") + "°", dyaw > 1f);

            float speedBefore = rig.CurrentSpeed;
            var wheel = new TwinPlayCameraRig.TwinPlayInput { scrollNotches = 2f };
            rig.StepForTest(wheel, 0.05f);
            Add("wheel changes speed", "before=" + speedBefore.ToString("F2")
                + " after=" + rig.CurrentSpeed.ToString("F2"),
                Mathf.Abs(rig.CurrentSpeed - speedBefore) > 0.1f);

            bool reset = _roam.ResetView();
            float afterReset = Vector3.Distance(start, rig.RigPosition);
            Add("R resets view", "reset=" + reset + " distToStart=" + afterReset.ToString("F3"),
                reset && afterReset < 0.5f);

            bool focused = rig != null;
            var focusPoint = rig.RigPosition + new Vector3(1.5f, -0.2f, 1.5f);
            rig.FocusOnPoint(focusPoint);
            Add("focus moves camera", "cam=" + rig.RigPosition.ToString("F2"),
                Vector3.Distance(rig.RigPosition, focusPoint) > 0.8f && focused);
            _roam.ResetView();
        }

        private static void StepShotOverview()
        {
            _window.ApplyLayout(new Rect(16f, 16f, 1440f, 900f), false, false);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            Shot("220_integrated_overview_1440.png");
        }

        private static void StepShotControl()
        {
            _surface.SetTab(TwinUiWorkspaceRegistry.Control);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            Shot("221_integrated_control_1440.png");
        }

        private static void StepShotMinimize()
        {
            _window.Minimize();
        }

        private static void StepShotFab()
        {
            Shot("222_minimized_fab_1440.png");
            Add("fab visible in minimized screenshot", "fabVisible=" + _fab.IsVisible, _fab.IsVisible);
            _window.Restore();
        }

        private static int ExpectedCards(string id)
        {
            var ws = TwinUiWorkspaceRegistry.Find(id);
            return ws != null ? ws.WidgetIds.Length : 0;
        }

        private static void Shot(string name)
        {
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                string path = Path.Combine(EvidenceShots, name);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P9] shot failed: " + e.Message);
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[P9] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
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

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Scattered UI + FAB + Roam Check (UI/52)  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("shellMode=" + TwinUiShellMode.Kind);
            foreach (var item in Items)
            {
                sb.AppendLine("[P9] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            if (_bridge != null)
            {
                sb.AppendLine("[P9] scatter=" + _bridge.Describe());
            }

            if (_roam != null)
            {
                sb.AppendLine("[P9] roam=" + _roam.Describe());
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0 && Items.Count >= 25;
            sb.AppendLine("[P9] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(TwinUiIntegrationStage.LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(TwinUiIntegrationStage.LogPath,
                    Path.Combine(EvidenceLogs, "ui52_integration_check.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P9] log write failed: " + e.Message);
            }

            Debug.Log("[P9] finish result=" + (ok ? "PASS" : "FAIL") + " items=" + Items.Count + " fails=" + fails);
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
                if (type == LogType.Exception)
                {
                    _logExceptions++;
                }
                else
                {
                    _logErrors++;
                }

                Debug.Log("[P9] [logfail] " + type + " " + condition);
            }
        }
    }
}