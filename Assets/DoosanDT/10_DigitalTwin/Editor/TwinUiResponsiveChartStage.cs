// TwinUiResponsiveChartStage.cs —— P9.5 自检（UI/53，auto-exit）
// 覆盖用户 2026-11-07 的三点新要求：
//   ① 卡片"网页式"缩放：720=1 列 / 900=2 列 / 1200=3 列 / 1600=4 列，卡片整体等比缩放并居中、行内对齐；
//   ② 卡片内多色折线：力矩卡与关节角卡各有一张 6 序列 TwinUiLineChart；
//   ③ 系统入口卡（原欢迎页）+ 控制指令硬锁 + 卡片拖出成浮窗 + 悬浮球吸附字段 + VR/多显示器适配状态。
// 结果写 Logs/ui_responsive_chart_check.txt，并复制到证据目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiResponsiveChartStage
    {
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui_responsive_chart";
        public const string LogPath = "Logs/ui_responsive_chart_check.txt";

        [MenuItem("DigitalTwin/UI/53 Responsive Cards + Charts + Gate Check (auto-exit)", false, 530)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[P95] UI/53 skipped: already in play mode");
                return;
            }

            TwinUiResponsiveChartCheck.Reset();
            SessionState.SetBool(TwinUiResponsiveChartCheck.PendingKey, true);
            Debug.Log("[P95] UI/53 armed; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiResponsiveChartCheck
    {
        public const string PendingKey = "TwinUiResponsiveChartCheck.Pending";
        private const string EvidenceLogs = TwinUiResponsiveChartStage.EvidenceRoot + @"\logs";
        private const string EvidenceShots = TwinUiResponsiveChartStage.EvidenceRoot + @"\shots";

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
                if (_window == null || _window.ShellV2 == null || _window.ShellV2.Surface == null)
                {
                    return;
                }

                _shell = _window.ShellV2;
                _surface = _shell.Surface;
                _surface.ResetForCheck();
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
            _nextAt = Time.realtimeSinceStartup + 0.3f;
        }

        private static void BuildSteps()
        {
            Steps.Enqueue(StepReady);
            Steps.Enqueue(() => StepZoom(720f, 480f, 1));
            // P10：列数改为 1/2 列契约（滞回 820/780），原 3/4 列断言废弃；缩放断言移入 UI/54。
            Steps.Enqueue(() => StepZoom(900f, 700f, 2));
            Steps.Enqueue(() => StepZoom(1200f, 800f, 2));
            Steps.Enqueue(() => StepZoom(1600f, 900f, 2));
            Steps.Enqueue(StepCharts);
            Steps.Enqueue(StepWelcome);
            Steps.Enqueue(StepGate);
            Steps.Enqueue(StepDragOut);
            Steps.Enqueue(StepAdapters);
            Steps.Enqueue(StepShot720);
            Steps.Enqueue(StepShot900);
            Steps.Enqueue(StepShot1200);
            Steps.Enqueue(StepShot1600);
            Steps.Enqueue(StepShotTwin);
            Steps.Enqueue(StepShotControl);
        }

        private static void StepReady()
        {
            Add("shell V2", "shell=" + (_shell != null) + " surface=" + (_surface != null),
                _shell != null && _surface != null);
            Add("modules=19", "modules=" + _surface.ModuleCount, _surface.ModuleCount == 19);
            Add("grid design width", "designW=" + TwinUiCardGrid.DesignWidth,
                Mathf.Abs(TwinUiCardGrid.DesignWidth - 360f) < 0.1f);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepZoom(float w, float h, int expectedColumns)
        {
            _window.ApplyLayout(new Rect(16f, 16f, w, h), false, false);
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);

            var page = _surface.Page;
            float c = page.ContentWidth;
            int columns = page.Columns;
            Add("zoom " + w.ToString("F0") + " columns", "C=" + c.ToString("F0")
                + " columns=" + columns + " expected=" + expectedColumns,
                columns == expectedColumns && Mathf.Abs(c - TwinUiLayoutContract.C(_surface.PageWidth)) <= 1f);

            var cards = new List<TwinUiCardView>();
            for (int i = 0; i < page.Cards.Count; i++)
            {
                if (page.Cards[i] != null && page.Cards[i].gameObject.activeSelf)
                {
                    cards.Add(page.Cards[i]);
                }
            }

            if (cards.Count == 0)
            {
                Add("zoom " + w.ToString("F0") + " cards", "no visible cards", false);
                return;
            }

            float colWidth = TwinUiLayoutContract.ColumnWidth(c, columns);
            int responsive = 0;
            int legacy = 0;
            int violations = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var module = TwinUiSurfaceHost.FindModule(cards[i].ModuleId);
                RectTransform rt = cards[i].Root;
                float visualW = rt.rect.width * rt.localScale.x;
                if (module != null && module.ResponsiveContent)
                {
                    responsive++;
                    if (Mathf.Abs(rt.localScale.x - 1f) > 0.001f)
                    {
                        violations++;
                    }

                    if (Mathf.Abs(visualW - colWidth) > 1f)
                    {
                        violations++;
                    }
                }
                else
                {
                    legacy++;
                    float s = rt.localScale.x;
                    if (s < TwinUiCardGrid.MinScale - 0.001f || s > TwinUiCardGrid.MaxScale + 0.001f)
                    {
                        violations++;
                    }
                }
            }

            Add("zoom " + w.ToString("F0") + " card width", "colW=" + colWidth.ToString("F0")
                + " responsive=" + responsive + " legacy=" + legacy + " violations=" + violations,
                violations == 0);

            int firstRowCount = 0;
            float firstY = cards[0].Root.anchoredPosition.y;
            for (int i = 0; i < cards.Count; i++)
            {
                if (Mathf.Abs(cards[i].Root.anchoredPosition.y - firstY) < 0.5f)
                {
                    firstRowCount++;
                }
            }

            Add("zoom " + w.ToString("F0") + " row packing", "firstRow=" + firstRowCount
                + " columns=" + columns, firstRowCount == Mathf.Min(columns, cards.Count));

            float rowStartX = cards[0].Root.anchoredPosition.x - colWidth * 0.5f;
            Add("zoom " + w.ToString("F0") + " row centered", "rowStartX=" + rowStartX.ToString("F1"),
                rowStartX >= -2f);
        }

        private static void StepCharts()
        {
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            var torque = _surface.Find("torque");
            var joints = _surface.Find("joints");
            var torqueChart = torque != null && torque.View != null
                ? torque.View.GetComponentInChildren<TwinUiLineChart>(true) : null;
            var jointsChart = joints != null && joints.View != null
                ? joints.View.GetComponentInChildren<TwinUiLineChart>(true) : null;
            Add("torque chart exists", "chart=" + (torqueChart != null), torqueChart != null);
            Add("torque chart series=6", "series=" + (torqueChart != null ? torqueChart.SeriesCount : -1),
                torqueChart != null && torqueChart.SeriesCount == 6);
            Add("joints chart exists", "chart=" + (jointsChart != null), jointsChart != null);
            Add("joints chart series=6", "series=" + (jointsChart != null ? jointsChart.SeriesCount : -1),
                jointsChart != null && jointsChart.SeriesCount == 6);
        }

        private static void StepWelcome()
        {
            _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            var welcome = _surface.Find("welcome");
            Add("welcome card exists", "welcome=" + (welcome != null), welcome != null);
            bool hasTitle = false;
            if (welcome != null && welcome.View != null)
            {
                var texts = welcome.View.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].text != null
                        && texts[i].text.Contains("退役动力电池"))
                    {
                        hasTitle = true;
                        break;
                    }
                }
            }

            Add("welcome shows project title", "hasTitle=" + hasTitle, hasTitle);
            Add("overview cards include welcome", "cards=" + _surface.Page.CardCount
                + " expected=" + ExpectedCards(TwinUiWorkspaceRegistry.Overview),
                _surface.Page.CardCount == ExpectedCards(TwinUiWorkspaceRegistry.Overview));
        }

        private static void StepGate()
        {
            _surface.SetTab(TwinUiWorkspaceRegistry.Control);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            var command = _surface.Find("command") as TwinUiWidgetCommand;
            Add("command card", "found=" + (command != null), command != null);
            Add("gate locked by default", TwinUiCommandGate.Describe(), !TwinUiCommandGate.Authorized);
            string reason;
            bool can = TwinUiCommandGate.CanExecute(out reason);
            Add("execute blocked before authorize", "can=" + can + " reason=" + reason, !can);
            if (command != null)
            {
                Add("execute button disabled", "interactable=" + command.ExecuteInteractable,
                    !command.ExecuteInteractable);
            }

            TwinUiCommandGate.Authorize();
            bool canAfter = TwinUiCommandGate.CanExecute(out reason);
            Add("authorize alone not enough", "can=" + canAfter + " reason=" + reason,
                !canAfter && !string.IsNullOrEmpty(reason));
            var runtime = UnityEngine.Object.FindObjectOfType<DigitalTwinRuntime>();
            Add("profile real command off", "profileAllows="
                + TwinUiCommandGate.ProfileAllowsRealCommand(runtime),
                !TwinUiCommandGate.ProfileAllowsRealCommand(runtime));
            TwinUiCommandGate.Revoke();
            Add("gate revoked", TwinUiCommandGate.Describe(), !TwinUiCommandGate.Authorized);
        }

        private static void StepDragOut()
        {
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            var card = _surface.CardFor("sync");
            Add("card drag handle", "dragOut=" + (card != null && card.DragOut != null),
                card != null && card.DragOut != null);
            if (card == null || card.DragOut == null)
            {
                return;
            }

            var canvas = _window.Canvas;
            Camera cam = canvas != null ? canvas.worldCamera : null;
            Vector2 headerScreen = RectTransformUtility.WorldToScreenPoint(cam,
                card.Root.TransformPoint(new Vector3(0f, -18f, 0f)));
            Rect windowRect = _window.Root.rect;
            Vector2 windowScreen = RectTransformUtility.WorldToScreenPoint(cam,
                _window.Root.TransformPoint(windowRect.min));
            Vector2 outside = new Vector2(windowScreen.x - 80f, windowScreen.y + 40f);

            int before = _surface.FloatingCount;
            bool began = card.DragOut.BeginDragForCheck(headerScreen);
            card.DragOut.EndDragForCheck(outside);
            Add("drag out begins", "began=" + began + " count=" + card.DragOut.DragOutCount, began);
            Add("drag out creates floating", "before=" + before + " after=" + _surface.FloatingCount,
                _surface.FloatingCount == before + 1);
            _surface.DockBack("sync");
        }

        private static void StepAdapters()
        {
            Add("fab present", "fab=" + (_shell.Fab != null) + " snapping="
                + (_shell.Fab != null && _shell.Fab.Snapping), _shell.Fab != null);
            Add("fab snap config", "threshold=" + TwinUiMinimizeFab.SnapThreshold
                + " duration=" + TwinUiMinimizeFab.SnapDuration,
                TwinUiMinimizeFab.SnapThreshold > 0f && TwinUiMinimizeFab.SnapDuration > 0f);
            Add("display watcher", "watcher=" + (_shell.DisplayWatcher != null)
                + (_shell.DisplayWatcher != null ? " " + _shell.DisplayWatcher.Describe() : string.Empty),
                _shell.DisplayWatcher != null && _shell.DisplayWatcher.IsConfigured);
            Add("vr adapter (desktop)", "adapter=" + (_shell.VrAdapter != null)
                + (_shell.VrAdapter != null ? " " + _shell.VrAdapter.Describe() : string.Empty),
                _shell.VrAdapter != null && !_shell.VrAdapter.IsVrActive);
        }

        private static void ShotStep(string name, float w, float h, string tab)
        {
            _window.ApplyLayout(new Rect(16f, 16f, w, h), false, false);
            _surface.SetTab(tab);
            _shell.ApplyLayout(_shell.LastWidth, _shell.LastHeight);
            Shot(name);
        }

        private static void StepShot720()
        {
            ShotStep("230_cards_720_one_column.png", 720f, 480f, TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepShot900()
        {
            ShotStep("231_cards_900_two_columns.png", 900f, 700f, TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepShot1200()
        {
            ShotStep("232_cards_1200_three_columns.png", 1200f, 800f, TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepShot1600()
        {
            ShotStep("233_cards_1600_four_columns.png", 1600f, 900f, TwinUiWorkspaceRegistry.Overview);
        }

        private static void StepShotTwin()
        {
            ShotStep("234_twin_charts_1600.png", 1600f, 900f, TwinUiWorkspaceRegistry.Twin);
        }

        private static void StepShotControl()
        {
            ShotStep("235_control_gate_1200.png", 1200f, 800f, TwinUiWorkspaceRegistry.Control);
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
                Debug.LogWarning("[P95] shot failed: " + e.Message);
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[P95] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
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
            sb.AppendLine("=== Responsive Cards + Charts + Gate Check (UI/53)  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("shellMode=" + TwinUiShellMode.Kind + " designWidth=" + TwinUiCardGrid.DesignWidth
                + " breakpoints=760/1160/1560 scale=" + TwinUiCardGrid.MinScale + ".." + TwinUiCardGrid.MaxScale);
            foreach (var item in Items)
            {
                sb.AppendLine("[P95] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0 && Items.Count >= 25;
            sb.AppendLine("[P95] items=" + Items.Count + " fails=" + fails + " logErrors=" + _logErrors
                + " logExceptions=" + _logExceptions + " shots=" + Shots.Count
                + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(TwinUiResponsiveChartStage.LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(TwinUiResponsiveChartStage.LogPath,
                    Path.Combine(EvidenceLogs, "ui53_responsive_chart_check.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P95] log write failed: " + e.Message);
            }

            Debug.Log("[P95] finish result=" + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails);
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

                Debug.Log("[P95] [logfail] " + type + " " + condition);
            }
        }
    }
}