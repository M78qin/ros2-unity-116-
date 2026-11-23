// TwinUiClickBehaviorStage.cs -- R2 A-2：走**真实点击路径**的 UI 行为测试（菜单 DigitalTwin/UI/61）
//
// 为什么需要它：首轮交付里 A1/A3/A4 只证明了"入口存在、回调绑定到 TwinUiSourceSwitch"，
// 没有点过一次按钮（首轮 ui/README.md 自述"点击链路=已实现未点验"），结果截图里出现了
// "顶部状态条=回放 / 底部 SourceBar=实时" 的矛盾。本阶段用 EventSystem 的真实点击路径
// （ExecuteEvents.Execute<IPointerClickHandler>，与鼠标点击同一个派发链）驱动
// 场景里**真实存在**的 TwinUiPageView 实例，覆盖 T1-T8。
//
// 边界（硬约束）：
//   * 只点 SourceBar 上的显示/源按钮；不点"连接/开始数据流/执行"，不连 ROS/DART，不写记录器。
//   * 不改场景资产（退出 Play 前不动任何序列化对象），不改 ProjectSettings。
//   * 输出只写 R2 批次 ui/testout/ui61/**（日志）与 ui/shots/**（截图）。
//   * 唯一允许绕过按钮的入口：T4 的"坏 CSV 路径"（按钮无法表达路径）与 T8 的"1 接收行 CSV"，
//     两者都在报告里显式说明并给出理由。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiClickBehaviorStage
    {
        const string Armed = "A.ClickBehavior";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui61";
        const string Shots = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\shots";

        // T4：真实存在的 0 行历史会话（561 B，samples.csv 只有表头）。
        // 它的存在本身就说明"目录里有 samples.csv"不等于"可加载"，正是事务式 rollback 的场景。
        const string BadCsv = @"D:\Unity Projects\DART_R-data\PaperLogs\20261104_204642_850475_E4_B\samples.csv";

        // T8 / A-3 第 5 条：只有 1 个接收行的 CSV（不够算速率，用于证明显示"缺失"而不是 0）。
        // 理由：现场会话里没有 1 行的样本（561 B 的是 0 行），只能构造；构造方式=真实首行原样复制。
        const string OneRowCsv = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testdata\one_receive_row.csv";

        static readonly string[] ButtonNames =
        {
            "SrcLive", "SrcReplay", "SrcNext", "Hz5", "Hz10", "Hz20", "Win10", "Win20", "Win60"
        };

        static readonly float[] HzList = { 5f, 10f, 20f };

        static StringBuilder log;
        static int phase;
        static int failures;
        static double next;
        static double deadline;
        static int tries;

        static TwinUiWindowHost window;
        static TwinUiSurfaceHost surface;
        static TwinUIStatusBinder binder;
        static TwinUiPageView page;
        static TwinUiWidgetJoints joints;
        static TwinUiWidgetTorque torqueCard;
        static TwinUiWidgetWrench wrenchCard;
        static TwinUiLineChart chart;

        static readonly Dictionary<string, GameObject> buttons = new Dictionary<string, GameObject>();

        // T1/T2
        static string liveSrcText;
        static string liveRecText;
        static string liveTopText;

        // T3
        static string runA;
        static int segmentBeforeNext;
        static int attachBeforeNext;
        static double switchAt;
        static double hzWindowImmediate;
        static double hzFileImmediate;
        static int rowsFileImmediate;
        static string runB;
        static int segmentB;

        // T4
        static string t4CsvBefore;
        static int t4ActiveIdBefore;
        static double t4HzBefore;
        static int t4SnapBefore;
        static bool t4Return;
        static string t4Error;

        // T5
        static int t5Index;
        static int sampleBase;
        static int pushBase;
        static int snapBase;
        static int frameBase;
        static double timeBase;
        static double recHzBase;
        static double fileHzBase;
        static double playBase;
        static double totalSeconds;

        // T6
        static int t6Stage;
        static double t6FillStart;
        static double span5;
        static int pts5;
        static double span20;
        static int pts20;

        // T7
        static int t7SnapBefore;
        static int t7SegmentBefore;
        static string t7RunBefore;
        static double t7SwitchAt;

        // A-4
        static readonly StringBuilder axisReport = new StringBuilder();

        static TwinUiClickBehaviorStage()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("DigitalTwin/UI/61 Source Click Behavior T1-T8 (auto-exit)", false, 610)]
        public static void Run()
        {
            Directory.CreateDirectory(Root + "/logs");
            log = new StringBuilder();
            log.AppendLine("# UI/61 真实点击路径行为测试 T1-T8（R2 A-2）");
            log.AppendLine("started=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            ResetState();
            SessionState.SetBool(Armed, true);
            File.WriteAllText(Root + "/logs/ui61_running.flag", DateTime.Now.ToString("O"));
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static void ResetState()
        {
            phase = 0;
            failures = 0;
            tries = 0;
            next = 0d;
            deadline = EditorApplication.timeSinceStartup + 420d;
            window = null;
            surface = null;
            binder = null;
            page = null;
            joints = null;
            torqueCard = null;
            wrenchCard = null;
            chart = null;
            buttons.Clear();
            liveSrcText = null;
            liveRecText = null;
            liveTopText = null;
            runA = null;
            segmentBeforeNext = -1;
            attachBeforeNext = -1;
            switchAt = 0d;
            hzWindowImmediate = -1d;
            hzFileImmediate = -1d;
            rowsFileImmediate = -1;
            runB = null;
            segmentB = -1;
            t4CsvBefore = null;
            t4ActiveIdBefore = -1;
            t4HzBefore = -1d;
            t4SnapBefore = -1;
            t4Return = false;
            t4Error = string.Empty;
            t5Index = 0;
            sampleBase = 0;
            pushBase = 0;
            snapBase = 0;
            frameBase = 0;
            timeBase = 0d;
            recHzBase = 0d;
            playBase = 0d;
            totalSeconds = 0d;
            fileHzBase = 0d;
            t6Stage = 0;
            t6FillStart = 0d;
            span5 = 0d;
            pts5 = 0;
            span20 = 0d;
            pts20 = 0;
            t7SnapBefore = 0;
            t7SegmentBefore = -1;
            t7RunBefore = null;
            t7SwitchAt = 0d;
            axisReport.Clear();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Armed, false);
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || EditorApplication.isCompiling)
            {
                return;
            }

            if (log == null)
            {
                // 进入 Play 会触发域重载：静态字段被清空，SessionState 里的 Armed 还在。
                // 必须重建 log/状态，否则每个 Check 都会 NRE（首轮 UI/60 就是这么把编辑器刷爆的）。
                log = new StringBuilder();
                log.AppendLine("# UI/61（resumed after domain reload）");
                ResetState();
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now > deadline)
            {
                Check("watchdog", false, "420 s at phase=" + phase);
                Finish();
                return;
            }

            if (now < next)
            {
                return;
            }

            try
            {
                Step(now);
            }
            catch (Exception e)
            {
                Check("exception phase=" + phase, false, e.ToString());
                Finish();
            }
        }

        // ---------------------------------------------------------------- 状态机

        static void Step(double now)
        {
            switch (phase)
            {
                case 0:
                    if (!Refs())
                    {
                        Retry(0.5d, "boot refs");
                        return;
                    }

                    BootChecks();
                    TwinUiSourceSwitch.SetLive();
                    Log("boot done: live baseline forced (isReplay=" + TwinUiSourceSwitch.IsReplay + ")");
                    Next(0.6d);
                    return;

                case 1: // T1：点「回放」
                    liveSrcText = page.SourceLineText;
                    liveRecText = page.RecordLineText;
                    liveTopText = surface.ConnectionStatusText;
                    Log("T1 before click: src=" + liveSrcText + " | rec=" + liveRecText + " | top=" + liveTopText);
                    DoClick("SrcReplay");
                    Next(1.4d);
                    return;

                case 2: // T1 断言
                    if (!TwinUiSourceSwitch.IsReplay || TwinUiReplayFeed.Active == null)
                    {
                        Retry(0.5d, "T1 replay ready");
                        return;
                    }

                    T1Assert();
                    Next(0.3d);
                    return;

                case 3: // T2：点「实时」
                    DoClick("SrcLive");
                    Next(0.9d);
                    return;

                case 4: // T2 断言
                    if (TwinUiSourceSwitch.IsReplay)
                    {
                        Retry(0.4d, "T2 live ready");
                        return;
                    }

                    T2Assert();
                    DoClick("SrcReplay"); // T3 起点：先回到回放（会话 A）
                    Next(1.2d);
                    return;

                case 5: // T3 断言 A，点「换回放」
                    if (!TwinUiSourceSwitch.IsReplay || TwinUiReplayFeed.Active == null)
                    {
                        Retry(0.5d, "T3 replay A ready");
                        return;
                    }

                    T3Prep();
                    switchAt = Time.unscaledTime;
                    DoClick("SrcNext");
                    // 同帧立即读：此刻 feed 刚 attach，尚未 Update → 窗口速率应为 0/缺失，
                    // 而整文件速率应已有值（A-3 第 3 条"加载完成即可给出整文件值"）。
                    hzWindowImmediate = TwinUiSourceSwitch.RecordHzWindow;
                    hzFileImmediate = TwinUiSourceSwitch.RecordHzFile;
                    rowsFileImmediate = TwinUiSourceSwitch.RecordRowsFile;
                    runB = TwinUiSourceSwitch.CurrentRunId;
                    segmentB = TwinUiSourceSwitch.Segment;
                    Log("T3 immediate after Next: run=" + runB + " segment=" + segmentB
                        + " recordHzWindow=" + F(hzWindowImmediate) + " recordHzFile=" + F(hzFileImmediate)
                        + " rowsFile=" + rowsFileImmediate + " err=" + TwinUiSourceSwitch.LastError);
                    Next(2.5d);
                    return;

                case 6: // T3 断言 B（等新源真正出点再断言）
                    if (joints == null || joints.BufferPointCount < 2)
                    {
                        Retry(0.5d, "T3 new-run samples");
                        return;
                    }

                    T3Assert();
                    T4Prep();
                    Next(0.2d);
                    return;

                case 7: // T4：走生产入口 SetReplay(坏 CSV)
                    t4Return = TwinUiSourceSwitch.SetReplay(BadCsv);
                    t4Error = TwinUiSourceSwitch.LastError;
                    Log("T4 bad csv -> return=" + t4Return + " lastError=" + t4Error);
                    Next(0.9d);
                    return;

                case 8: // T4 断言 → T5 起点（5 Hz）
                    T4Assert();
                    t5Index = 0;
                    DoClick(HzButtonName(HzList[0]));
                    Next(0.45d);
                    return;

                case 9: // T5 @5Hz 基线
                    T5Base();
                    Next(3.0d);
                    return;

                case 10: // T5 @5Hz 计算 → 10 Hz
                    if (!T5Measure())
                    {
                        return;
                    }

                    t5Index = 1;
                    DoClick(HzButtonName(HzList[1]));
                    Next(0.45d);
                    return;

                case 11: // T5 @10Hz 基线
                    T5Base();
                    Next(3.0d);
                    return;

                case 12: // T5 @10Hz 计算 → 20 Hz
                    if (!T5Measure())
                    {
                        return;
                    }

                    t5Index = 2;
                    DoClick(HzButtonName(HzList[2]));
                    Next(0.45d);
                    return;

                case 13: // T5 @20Hz 基线
                    T5Base();
                    Next(3.0d);
                    return;

                case 14: // T5 @20Hz 计算 → T6 起点（5 Hz + 10 s 窗）
                    if (!T5Measure())
                    {
                        return;
                    }

                    DoClick(HzButtonName(5f));
                    DoClick("Win10");
                    Next(0.3d);
                    return;

                case 15: // T6：两段**纯相位**填样（时间戳裁剪 + 点数随 Hz 变）
                    switch (t6Stage)
                    {
                        case 0: // 纯 5 Hz 填 10 s 窗
                            DoClick("Win10");
                            DoClick(HzButtonName(5f));
                            ReattachSameRun("T6 5Hz phase");
                            t6FillStart = Time.unscaledTime;
                            t6Stage = 1;
                            Wait(10.7d);
                            return;

                        case 1: // 读 5 Hz 相位
                            span5 = chart != null ? chart.DrawnSpanSeconds : 0d;
                            pts5 = chart != null ? chart.DrawnPoints(0) : 0;
                            Check("T6 10s window @5Hz (pure phase) span", Mathf.Abs((float)span5 - 10f) <= 1.5f,
                                "span=" + F(span5) + "s points=" + pts5
                                + " chartWindow=" + chart.TimeWindowSeconds + " pointWindow=" + chart.WindowPoints);
                            DoClick(HzButtonName(20f));
                            ReattachSameRun("T6 20Hz phase");
                            t6FillStart = Time.unscaledTime;
                            t6Stage = 2;
                            Wait(10.7d);
                            return;

                        case 2: // 读 20 Hz 相位 + 核心对比
                            span20 = chart != null ? chart.DrawnSpanSeconds : 0d;
                            pts20 = chart != null ? chart.DrawnPoints(0) : 0;
                            Check("T6 10s window @20Hz (pure phase) span", Mathf.Abs((float)span20 - 10f) <= 1.5f,
                                "span=" + F(span20) + "s points=" + pts20);
                            ClickResult("Win10@5Hz vs Win10@20Hz",
                                "同一时间跨度(±1.5 s)、点数≈4 倍（两段纯相位各自填满 10 s）",
                                "span5=" + F(span5) + "s(" + pts5 + "pts) span20=" + F(span20) + "s(" + pts20 + "pts)"
                                + " ratio=" + (pts5 > 0 ? (pts20 / (double)pts5).ToString("F2") : "n/a"),
                                Mathf.Abs((float)(span20 - span5)) <= 1.5f && pts5 > 0 && pts20 >= pts5 * 2.5);
                            DoClick(HzButtonName(5f));
                            DoClick("Win20");
                            t6Stage = 3;
                            Wait(1.2d);
                            return;

                        case 3: // 20 s 窗：等连续采样历史 >= 21 s
                            if (Time.unscaledTime - t6FillStart < 21d)
                            {
                                Retry(1.0d, "win20 history " + F(Time.unscaledTime - t6FillStart) + "s");
                                return;
                            }

                            T6Window("20", 20f, 3f);
                            DoClick("Win60");
                            t6Stage = 4;
                            Wait(1.2d);
                            return;

                        default: // 60 s 窗：等历史 >= 61 s
                            if (Time.unscaledTime - t6FillStart < 61d)
                            {
                                Retry(2.0d, "win60 history " + F(Time.unscaledTime - t6FillStart) + "s");
                                return;
                            }

                            T6Window("60", 60f, 9f);
                            DoClick("Win20");
                            // 进入 T7：先冻结（记录冻结时刻的会话级计数），下一阶段再换源
                            TwinUiViewFreeze.Set(true);
                            t7SnapBefore = TwinUiReplayFeed.TotalSnapshots;
                            t7SegmentBefore = TwinUiSourceSwitch.Segment;
                            t7RunBefore = TwinUiSourceSwitch.CurrentRunId;
                            Next(1.4d);
                            return;
                    }

                case 16: // T7：冻结期间点「换回放」
                    ClickResult("Freeze", "冻结只停显示；不换源时计数继续增长",
                        "frozen=" + TwinUiViewFreeze.IsFrozen
                        + " totalSnapshots=" + TwinUiReplayFeed.TotalSnapshots
                        + " (before freeze=" + t7SnapBefore + ")",
                        TwinUiViewFreeze.IsFrozen && TwinUiReplayFeed.TotalSnapshots > t7SnapBefore);
                    t7SnapBefore = TwinUiReplayFeed.TotalSnapshots;
                    t7SwitchAt = Time.unscaledTime;
                    DoClick("SrcNext");
                    Next(1.4d);
                    return;

                case 17: // T7 断言（冻结下切源）→ 解冻
                    T7AssertFrozen();
                    TwinUiViewFreeze.Set(false);
                    Next(1.4d);
                    return;

                case 18: // T7 断言（解冻后不残留旧帧）→ T8（1 接收行 CSV）
                    T7AssertAfterUnfreeze();
                    bool ok = TwinUiSourceSwitch.SetReplay(OneRowCsv);
                    Log("T8 one-row csv -> return=" + ok + " err=" + TwinUiSourceSwitch.LastError);
                    Next(1.0d);
                    return;

                case 19: // T8 + A-4
                    T8Assert();
                    A4Assert();
                    Shot("ui61_axis_click_" + Screen.width + "x" + Screen.height + "_r2.png");
                    Next(0.6d);
                    return;

                case 20:
                    Finish();
                    return;

                default:
                    Finish();
                    return;
            }
        }

        // ---------------------------------------------------------------- 各段断言

        static void BootChecks()
        {
            // R2 测量隔离：XRI affordance provider 每帧 NRE + ~6 MB/min 日志，会污染帧率类断言。
            // 只在本次 Play 进程内禁用，不改场景、不保存。
            string xriNames;
            int xriClosed = TwinUiTestIsolation.DisableXrAffordances(out xriNames);
            log.AppendLine("INFO XRI affordance providers disabled=" + xriClosed + " | " + xriNames);
            log.AppendLine("INFO console log at isolation: " + TwinUiTestIsolation.ConsoleLogInfo());
            GateChecks();
            Check("EventSystem present", EventSystem.current != null,
                EventSystem.current != null ? EventSystem.current.name : "null");
            Check("SourceBar visible", page.SourceBarVisible, page.SourceBarVisible.ToString());

            var missing = new List<string>();
            for (int i = 0; i < ButtonNames.Length; i++)
            {
                GameObject go = Find(page.Root, ButtonNames[i]);
                if (go == null)
                {
                    missing.Add(ButtonNames[i]);
                }
                else
                {
                    buttons[ButtonNames[i]] = go;
                }
            }

            Check("SourceBar 9 buttons found by name", missing.Count == 0,
                "missing=" + string.Join(",", missing.ToArray()));

            // 真实监听证据：生产代码用运行时 AddListener（TwinUIStyle.cs:267），
            // 因此 GetPersistentEventCount()=0 是**预期**，不能据此判定"没有监听"。
            var sb = new StringBuilder();
            foreach (string name in ButtonNames)
            {
                GameObject go = null;
                if (!buttons.TryGetValue(name, out go) || go == null)
                {
                    continue;
                }

                var b = go.GetComponent<Button>();
                if (b == null)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                sb.Append(name).Append(":persistent=").Append(b.onClick.GetPersistentEventCount())
                    .Append(",runtime=").Append(RuntimeListenerCount(b));
            }

            Log("button listeners | " + sb);
            Check("all buttons have a runtime listener", AllButtonsHaveRuntimeListener(), sb.ToString());
            Check("buttons are the same objects as PageView fields",
                buttons["SrcLive"].Equals(page.LiveButton.gameObject)
                && buttons["SrcReplay"].Equals(page.ReplayButton.gameObject)
                && buttons["SrcNext"].Equals(page.NextReplayButton.gameObject),
                "SrcLive/SrcReplay/SrcNext identity");
        }

        /// <summary>R2 Play 门禁的**运行时**核查（进 Play 后、任何点击之前）：不自动连、不自动动、不写记录。</summary>
        static void GateChecks()
        {
            var controller = UnityEngine.Object.FindObjectOfType<TwinCommandController>();
            var ros2 = UnityEngine.Object.FindObjectOfType<Ros2Bridge>();
            var dart = UnityEngine.Object.FindObjectOfType<DartStudioBridge>();
            var rec = UnityEngine.Object.FindObjectOfType<TwinRecorder>();
            var paper = UnityEngine.Object.FindObjectOfType<TwinPaperRecorder>();

            bool bidi = controller != null && controller.EnableBidirectionalControl;
            bool real = controller != null && controller.EnableRealRobotCommand;
            bool dry = controller != null && controller.EnableDryRun;
            bool canSend = controller != null && controller.CanSendRealMove;
            bool ros2Up = ros2 != null && (ros2.IsConnected || ros2.IsRunning);
            bool dartUp = dart != null && (dart.IsConnected || dart.IsRunning);
            bool recOn = rec != null && rec.IsRecording;
            bool paperOn = paper != null && paper.IsRecording;

            log.AppendLine("GATE runtime | bidirectional=" + bidi + " realRobot=" + real + " dryRun=" + dry
                + " canSendRealMove=" + canSend + " ros2(connected|running)=" + ros2Up
                + " dart(connected|running)=" + dartUp
                + " recorder=" + recOn + " paperRecorder=" + paperOn
                + " ros2Queue=" + (ros2 != null ? ros2.QueuedFrameCount : -1));
            Check("GATE no bidirectional control", !bidi, "enableBidirectionalControl=" + bidi);
            Check("GATE no real robot command", !real, "enableRealRobotCommand=" + real);
            Check("GATE dry-run on", dry, "enableDryRun=" + dry);
            Check("GATE cannot send real move", !canSend, "CanSendRealMove=" + canSend);
            Check("GATE ROS2 not connected/running", !ros2Up, "connected|running=" + ros2Up);
            Check("GATE DART not connected/running", !dartUp, "connected|running=" + dartUp);
            Check("GATE recorders not running", !recOn && !paperOn,
                "twincorder=" + recOn + " paper=" + paperOn);
        }

        static void T1Assert()
        {
            page.RefreshSourceBar();
            bool isReplay = TwinUiSourceSwitch.IsReplay;
            var feed = TwinUiReplayFeed.Active;
            string src = page.SourceLineText;
            string rec = page.RecordLineText;
            string top = surface.ConnectionStatusText;

            ClickResult("SrcReplay",
                "IsReplay=true; Active!=null; SourceBar 含「回放 · <runId>」; 记录有数值; 顶部与底栏同源",
                "isReplay=" + isReplay + " active=" + (feed != null) + " loaded=" + (feed != null && feed.Loaded)
                + " runId=" + TwinUiSourceSwitch.CurrentRunId + " src=" + src + " rec=" + rec + " top=" + top,
                isReplay && feed != null && feed.Loaded
                && src.Contains("回放 · " + TwinUiSourceSwitch.CurrentRunId)
                && rec.StartsWith("记录 ") && rec.Contains("Hz")
                && top.Contains("回放") && src.Contains("回放"));

            Check("T1 top status is 回放", top.Contains("回放"), top);
            Check("T1 source bar is 回放 (same truth as top)", src.Contains("回放"), src);
            Check("T1 RecordHz has a number (window/file)", rec.Contains("Hz") && !rec.Contains("0.0 Hz（整文件）"), rec);
            Check("T1 replay button selected", page.ReplayButton != null && page.ReplayButton.Selected,
                "replaySelected=" + (page.ReplayButton != null && page.ReplayButton.Selected));
            Check("T1 live button not selected", page.LiveButton == null || !page.LiveButton.Selected,
                "liveSelected=" + (page.LiveButton != null && page.LiveButton.Selected));
            Check("T1 no 实时（无回放） text while replay", !src.Contains("实时（无回放"), src);
            Log("T1 after click: src=" + src + " | rec=" + rec + " | file=" + page.RecordFileText + " | top=" + top);
        }

        static void T2Assert()
        {
            page.RefreshSourceBar();
            var feed = TwinUiReplayFeed.Active;
            string src = page.SourceLineText;
            string rec = page.RecordLineText;
            string top = surface.ConnectionStatusText;
            int buffer = joints != null ? joints.BufferPointCount : -1;

            ClickResult("SrcLive",
                "Active==null; SourceBar 回实时文案; 记录=—; 曲线缓冲已清",
                "isReplay=" + TwinUiSourceSwitch.IsReplay + " active=" + (feed != null)
                + " src=" + src + " rec=" + rec + " buffer=" + buffer + " top=" + top,
                !TwinUiSourceSwitch.IsReplay && feed == null
                && src.Contains("实时（无回放") && rec.Contains("记录 —")
                && top.Contains("未连接") && buffer == 0);

            Check("T2 source bar back to live", src.Contains("实时（无回放"), src);
            Check("T2 record line back to —", rec.Contains("记录 —"), rec);
            Check("T2 top status not replay", !top.Contains("回放"), top);
            Check("T2 chart buffer cleared", buffer == 0, "bufferPoints=" + buffer);
            Check("T2 live button selected", page.LiveButton != null && page.LiveButton.Selected,
                "liveSelected=" + (page.LiveButton != null && page.LiveButton.Selected));
            Log("T2 after click: src=" + src + " | rec=" + rec + " | top=" + top);
        }

        static void T3Prep()
        {
            page.RefreshSourceBar();
            runA = TwinUiSourceSwitch.CurrentRunId;
            segmentBeforeNext = TwinUiSourceSwitch.Segment;
            attachBeforeNext = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.AttachCount : -1;
            totalSeconds = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.TotalSeconds : 0d;
            Log("T3 run A=" + runA + " segment=" + segmentBeforeNext + " attach=" + attachBeforeNext
                + " totalSeconds=" + F(totalSeconds) + " rows=" + (TwinUiReplayFeed.Active != null
                    ? TwinUiReplayFeed.Active.RowCount : -1));
        }

        static void T3Assert()
        {
            page.RefreshSourceBar();
            var feed = TwinUiReplayFeed.Active;
            int attachNow = feed != null ? feed.AttachCount : -1;
            float oldest = joints != null ? joints.BufferOldestTime : 0f;
            string src = page.SourceLineText;

            ClickResult("SrcNext",
                "runId 变化; Segment+1; 缓冲清空; 新 run 首帧不早于切换时刻",
                "runA=" + runA + " runB=" + runB + " segment " + segmentBeforeNext + "->" + segmentB
                + " attach " + attachBeforeNext + "->" + attachNow
                + " bufferOldest=" + F(oldest) + " switchAt=" + F(switchAt)
                + " src=" + src,
                !string.IsNullOrEmpty(runB) && runB != runA
                && segmentB == segmentBeforeNext + 1
                && attachNow == attachBeforeNext + 1
                && oldest >= switchAt - 0.05f);

            Check("T3 runId changed", runB != runA, runA + " -> " + runB);
            Check("T3 segment +1", segmentB == segmentBeforeNext + 1,
                segmentBeforeNext + " -> " + segmentB);
            Check("T3 no old-run frames in buffer", oldest >= switchAt - 0.05f,
                "bufferOldest=" + F(oldest) + " switchAt=" + F(switchAt));

            // A-3 第 3 条：加载完成即可给出整文件值（Refresh 之前/之后对照）
            double hzWindowNow = TwinUiSourceSwitch.RecordHzWindow;
            double hzFileNow = TwinUiSourceSwitch.RecordHzFile;
            ClickResult("RecordHz(load vs refresh)",
                "加载完成即给整文件值；窗口值需一次 Refresh 后才有",
                "立即: window=" + F(hzWindowImmediate) + " file=" + F(hzFileImmediate)
                + " rowsFile=" + rowsFileImmediate
                + " | 1.6s 后: window=" + F(hzWindowNow) + " file=" + F(hzFileNow)
                + " scope=" + TwinUiSourceSwitch.RecordHzScope,
                hzFileNow > 1d && hzWindowNow > 1d);
            Log("A-3 semantics: " + TwinUiSourceSwitch.RecordHzSemantics
                + " | TargetSampleHz(TARGET)=" + TwinUiSourceSwitch.TargetSampleHz
                + " | rowsFile=" + TwinUiSourceSwitch.RecordRowsFile
                + " spanFile=" + F(TwinUiSourceSwitch.RecordSpanFileSeconds));
        }

        static void T4Prep()
        {
            t4CsvBefore = TwinUiSourceSwitch.CurrentCsv;
            t4ActiveIdBefore = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.GetInstanceID() : 0;
            t4HzBefore = TwinUiSourceSwitch.RecordHz;
            t4SnapBefore = SnapCount();
            Log("T4 before: csv=" + t4CsvBefore + " activeId=" + t4ActiveIdBefore
                + " recordHz=" + F(t4HzBefore) + " snapshots=" + t4SnapBefore);
        }

        static void T4Assert()
        {
            page.RefreshSourceBar();
            var feed = TwinUiReplayFeed.Active;
            int activeIdNow = feed != null ? feed.GetInstanceID() : 0;
            double hzNow = TwinUiSourceSwitch.RecordHz;
            int snapNow = SnapCount();
            string src = page.SourceLineText;

            ClickResult("SetReplay(坏CSV)", "返回 false + LastError 非空 + 原源逐项不变且仍在推进",
                "return=" + t4Return + " lastError=" + t4Error
                + " csv " + t4CsvBefore + "->" + TwinUiSourceSwitch.CurrentCsv
                + " activeId " + t4ActiveIdBefore + "->" + activeIdNow
                + " recordHz " + F(t4HzBefore) + "->" + F(hzNow)
                + " snapshots " + t4SnapBefore + "->" + snapNow
                + " isReplay=" + TwinUiSourceSwitch.IsReplay,
                !t4Return && !string.IsNullOrEmpty(t4Error)
                && TwinUiSourceSwitch.CurrentCsv == t4CsvBefore
                && activeIdNow == t4ActiveIdBefore
                && Math.Abs(hzNow - t4HzBefore) <= Math.Max(0.5d, t4HzBefore * 0.05d)
                && snapNow > t4SnapBefore
                && TwinUiSourceSwitch.IsReplay);

            Check("T4 current csv unchanged", TwinUiSourceSwitch.CurrentCsv == t4CsvBefore,
                t4CsvBefore + " -> " + TwinUiSourceSwitch.CurrentCsv);
            Check("T4 same feed instance (old source not replaced)", activeIdNow == t4ActiveIdBefore,
                t4ActiveIdBefore + " -> " + activeIdNow);
            Check("T4 record rate within measurement noise (±3%)", Math.Abs(hzNow - t4HzBefore) <= Math.Max(0.5d, t4HzBefore * 0.03d),
                F(t4HzBefore) + " -> " + F(hzNow) + " (窗口实测率本身有 ±3% 噪声；整文件值=" + F(TwinUiSourceSwitch.RecordHzFile) + ")");
            Check("T4 replay still advancing", snapNow > t4SnapBefore,
                t4SnapBefore + " -> " + snapNow);
            Check("T4 source bar still shows replay", src.Contains("回放 · "), src);
        }

        static void T5Base()
        {
            sampleBase = binder.SampleCount;
            pushBase = joints != null ? joints.ChartPushCount : 0;
            snapBase = SnapCount();
            frameBase = Time.frameCount;
            timeBase = Time.unscaledTime;
            recHzBase = TwinUiSourceSwitch.RecordHz;
            // A-3 口径：窗口实测率本身有 ±3% 噪声（同一段 CSV 实测 99.7~102.9），
            // "显示 Hz 不影响记录率"必须用**稳定的整文件值**判定，窗口值只作为噪声一并打印。
            fileHzBase = TwinUiSourceSwitch.RecordHzFile;
            playBase = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.PlaybackSeconds : 0d;
            Log("T5 base @" + F(TwinUiSourceSwitch.DisplayHz) + "Hz sample=" + sampleBase
                + " push=" + pushBase + " snapshot=" + snapBase + " frame=" + frameBase
                + " recordHz窗口=" + F(recHzBase) + " recordHz整文件=" + F(fileHzBase)
                + " playback=" + F(playBase));
        }

        static bool T5Measure()
        {
            double elapsed = Time.unscaledTime - timeBase;
            if (elapsed < 2.5d)
            {
                Retry(0.3d, "T5 elapsed " + F(elapsed) + "s");
                return false;
            }

            float hz = TwinUiSourceSwitch.DisplayHz;
            int dSample = binder.SampleCount - sampleBase;
            int dPush = (joints != null ? joints.ChartPushCount : 0) - pushBase;
            int dSnap = SnapCount() - snapBase;
            int dFrame = Time.frameCount - frameBase;
            double sampleRate = dSample / elapsed;
            double pushRate = dPush / elapsed;
            double snapRate = dSnap / elapsed;
            double frameRate = dFrame / elapsed;

            double playNow = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.PlaybackSeconds : 0d;
            double dPlay = playNow - playBase;
            if (dPlay < 0d && totalSeconds > 0d)
            {
                dPlay += totalSeconds; // 回放 loop 回卷
            }

            double playRate = dPlay / elapsed;
            double recNow = TwinUiSourceSwitch.RecordHz;
            double fileNow = TwinUiSourceSwitch.RecordHzFile;
            double sigma = dSample > 0 ? Math.Sqrt(dSample) / elapsed : 0d;

            // 判据（Lead 复核后调整）：速率落在 [0.8×目标, 1.2×目标] 且样本数 n≥10。
            // 1.3 s 窗口只有 6 次刷新时 4.60/s 属统计不足，不能据此判 FAIL。
            bool rateOk = sampleRate >= hz * 0.8d && sampleRate <= hz * 1.2d && dSample >= 10;

            ClickResult("Hz" + hz.ToString("F0"),
                "真实刷新落在 [0.8,1.2]×" + hz.ToString("F0") + "/s 且 n≥10；整文件记录率与回放钟速度不变",
                "elapsed=" + F(elapsed) + "s n=" + dSample + " sampleRate=" + F(sampleRate)
                + "/s (±1σ " + F(sigma) + ") pushRate=" + F(pushRate) + "/s"
                + " snapshotRate(帧级)=" + F(snapRate) + "/s frameRate=" + F(frameRate)
                + " recordHz整文件 " + F(fileHzBase) + "->" + F(fileNow)
                + " recordHz窗口 " + F(recHzBase) + "->" + F(recNow) + "（噪声带，不作等值断言）"
                + " playRate=" + F(playRate),
                rateOk && dPush > 0 && Math.Abs(fileNow - fileHzBase) <= 0.5d
                && Math.Abs(playRate - 1d) <= 0.2d);

            Check("T5 SnapshotCount is frame-level, not refresh count @" + hz.ToString("F0") + "Hz",
                snapRate > Math.Max(5d, hz * 1.5d),
                "snapshotRate=" + F(snapRate) + "/s vs sampleRate=" + F(sampleRate) + "/s");
            Check("T5 display Hz does not change record rate (整文件口径) @" + hz.ToString("F0") + "Hz",
                Math.Abs(fileNow - fileHzBase) <= 0.5d, F(fileHzBase) + " -> " + F(fileNow));
            Check("T5 display Hz does not change replay clock @" + hz.ToString("F0") + "Hz",
                Math.Abs(playRate - 1d) <= 0.2d, "playRate=" + F(playRate));
            return true;
        }

        /// <summary>
        /// 同源重挂（`SetReplay(当前 CSV)`）——只为**清空曲线缓冲**制造纯相位填样，
        /// 不参与任何"点击是否生效"的断言（点击断言只走 ExecuteEvents 真实点击）。
        /// </summary>
        static void ReattachSameRun(string tag)
        {
            string csv = TwinUiSourceSwitch.CurrentCsv;
            bool ok = TwinUiSourceSwitch.SetReplay(csv);
            Log("reattach same run (" + tag + ") ok=" + ok + " csv=" + csv
                + " segment=" + TwinUiSourceSwitch.Segment);
        }

        static void T6Window(string label, float seconds, float tolerance)
        {
            int points = chart != null ? chart.DrawnPoints(0) : 0;
            double span = chart != null ? chart.DrawnSpanSeconds : 0d;
            double history = t6FillStart > 0d ? Time.unscaledTime - t6FillStart : 0d;
            Check("T6 " + label + "s window span≈window (timestamp trim)",
                Mathf.Abs((float)(span - seconds)) <= tolerance
                && chart.TimeWindowSeconds == seconds && chart.WindowPoints == 0,
                "span=" + F(span) + "s target=" + F(seconds) + "s tol=±" + F(tolerance)
                + " history=" + F(history) + "s points=" + points
                + " chartWindow=" + chart.TimeWindowSeconds + " pointWindow=" + chart.WindowPoints);
        }

        static void T7AssertFrozen()
        {
            int snapNow = TwinUiReplayFeed.TotalSnapshots;
            float oldest = joints != null ? joints.BufferOldestTime : 0f;
            int buffer = joints != null ? joints.BufferPointCount : -1;
            string runNow = TwinUiSourceSwitch.CurrentRunId;
            int segmentNow = TwinUiSourceSwitch.Segment;
            // 容差方向：要求"缓冲最旧样本时间戳 **不早于** 切换时刻"，容差 0.05 s（容纳同帧采样）。
            bool noOldFrames = buffer == 0 || oldest >= t7SwitchAt - 0.05f;

            ClickResult("冻结 + SrcNext",
                "Segment+1；runId 变化；旧帧清空（oldest ≥ switchAt−0.05s 或 buffer=0）；会话级计数继续增长",
                "segment " + t7SegmentBefore + "->" + segmentNow
                + " run " + t7RunBefore + "->" + runNow
                + " buffer=" + buffer + " oldest=" + F(oldest) + " switchAt=" + F(t7SwitchAt)
                + " direction=oldest>=switchAt-0.05 noOldFrames=" + noOldFrames
                + " totalSnapshots(session) " + t7SnapBefore + "->" + snapNow,
                segmentNow == t7SegmentBefore + 1 && runNow != t7RunBefore
                && noOldFrames && snapNow > t7SnapBefore);

            Check("T7 frozen display still records (session-level snapshots grow)", snapNow > t7SnapBefore,
                t7SnapBefore + " -> " + snapNow);
            Check("T7 buffer cleared on switch while frozen", noOldFrames,
                "buffer=" + buffer + " oldest=" + F(oldest) + " switchAt=" + F(t7SwitchAt)
                + " (oldest≥switchAt−0.05)");
        }

        static void T7AssertAfterUnfreeze()
        {
            float oldest = joints != null ? joints.BufferOldestTime : 0f;
            int points = chart != null ? chart.DrawnPoints(0) : 0;
            ClickResult("Unfreeze",
                "解冻后重新出点，且不出现切换前的旧帧",
                "points=" + points + " oldest=" + F(oldest) + " switchAt=" + F(t7SwitchAt)
                + " (oldest≥switchAt−0.05)",
                points > 1 && oldest >= t7SwitchAt - 0.05f);
        }

        static void T8Assert()
        {
            page.RefreshSourceBar();
            string rec = page.RecordLineText;
            string src = page.SourceLineText;
            string torque = ValuesOf(torqueCard);
            string wrench = ValuesOf(wrenchCard);
            bool torqueMissing = AllValuesEqual(torqueCard, TwinUITextR3.Placeholder);
            bool wrenchMissing = AllValuesEqual(wrenchCard, TwinUITextR3.Placeholder);
            int torqueBuffer = torqueCard != null ? torqueCard.BufferPointCount : -1;
            int wrenchPoints = wrenchCard != null && wrenchCard.View != null
                ? CountSeriesPoints(wrenchCard) : -1;

            ClickResult("SetReplay(1 接收行 CSV)",
                "速率不可算时显示缺失（不是 0）；缺失通道显示 —，缓冲无 0 占位",
                "recordHzWindow=" + F(TwinUiSourceSwitch.RecordHzWindow)
                + " recordHzFile=" + F(TwinUiSourceSwitch.RecordHzFile)
                + " rowsFile=" + TwinUiSourceSwitch.RecordRowsFile
                + " rec=\"" + rec + "\" src=\"" + src + "\""
                + " torque=\"" + torque + "\" torqueBuffer=" + torqueBuffer
                + " wrench=\"" + wrench + "\" wrenchPoints=" + wrenchPoints,
                src.Contains("不足 2 接收行") && !rec.Contains("0.0 Hz")
                && torqueMissing && wrenchMissing && torqueBuffer == 0 && wrenchPoints <= 0);

            Check("T8 missing rate shows 缺失, not 0", src.Contains("不足 2 接收行"), src);
            Check("T8 torque missing shows placeholder", torqueMissing, torque);
            Check("T8 wrench missing shows placeholder", wrenchMissing, wrench);
            Check("T8 no zero placeholder in torque buffer", torqueBuffer == 0, "buffer=" + torqueBuffer);
        }

        static void A4Assert()
        {
            if (chart == null)
            {
                Check("A-4 chart present", false, "chart=null");
                return;
            }

            string left = chart.AxisLeftLabel;
            string right = chart.AxisRightLabel;
            Color fg = chart.AxisLabelColor;
            Color bg = chart.PlotBackdropColor;
            Color card = TwinUIStyle.CardColor;
            // 图表自己用 PlotBg(alpha 0.9) 铺满绘图区 → 轴标实际压在 "PlotBg over Card" 的混色上。
            Color blended = new Color(
                bg.r * bg.a + card.r * (1f - bg.a),
                bg.g * bg.a + card.g * (1f - bg.a),
                bg.b * bg.a + card.b * (1f - bg.a),
                1f);
            float cPlot = TwinUIStyle.Contrast(fg, blended);
            float cCard = TwinUIStyle.Contrast(fg, card);
            float fontSize = chart.AxisLabelFontSizeValue;

            bool hasAscii = chart.AxisFontHasAsciiHyphen;
            bool hasMinus = chart.AxisFontHasMinusSign;
            bool asciiLabel = left.Length > 0 && left[0] == '-' && left.IndexOf('\u2212') < 0;

            axisReport.AppendLine("A-4 axis | left=\"" + left + "\" right=\"" + right + "\""
                + " fontSize=" + F(fontSize)
                + " fg=" + ColorHex(fg)
                + " backdrop(PlotBg*0.9 over Card)=" + ColorHex(blended)
                + " contrast=" + cPlot.ToString("F2") + ":1"
                + " (vs Card=" + cCard.ToString("F2") + ":1)"
                + " fontHasU+2212=" + hasMinus + " fontHasAsciiHyphen=" + hasAscii
                + " labelStartsWithAsciiHyphen=" + asciiLabel);
            Log(axisReport.ToString().TrimEnd());

            Check("A-4 axis label uses ASCII hyphen (no U+2212 缺字形)", asciiLabel,
                "left=\"" + left + "\"");
            Check("A-4 axis label is readable text (not empty/onset)", left.Contains(" s") && right.Contains("0 s"),
                "left=\"" + left + "\" right=\"" + right + "\"");
            Check("A-4 axis contrast >= 4.5:1", cPlot >= 4.5f,
                "contrast=" + cPlot.ToString("F2") + ":1 (card=" + cCard.ToString("F2") + ":1)");
            Check("A-4 ASCII hyphen exists in font atlas", hasAscii, "hasAsciiHyphen=" + hasAscii);
            ClickResult("AxisT0/T1", "-N s / 0 s，ASCII '-'，对比度≥4.5",
                "left=\"" + left + "\" right=\"" + right + "\" contrast=" + cPlot.ToString("F2") + ":1"
                + " size=" + F(fontSize),
                asciiLabel && cPlot >= 4.5f);
        }

        // ---------------------------------------------------------------- 辅助

        static bool Refs()
        {
            window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            surface = TwinUiSurfaceHost.Instance;
            if (surface == null)
            {
                surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            }

            if (binder == null)
            {
                binder = UnityEngine.Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (surface == null || surface.Page == null || binder == null || window == null)
            {
                return false;
            }

            page = surface.Page;
            surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
            joints = surface.Find("joints") as TwinUiWidgetJoints;
            torqueCard = surface.Find("torque") as TwinUiWidgetTorque;
            wrenchCard = surface.Find("wrench") as TwinUiWidgetWrench;
            chart = joints != null && joints.View != null
                ? joints.View.GetComponentInChildren<TwinUiLineChart>(true)
                : null;
            return page != null && joints != null && chart != null;
        }

        static int SnapCount()
        {
            return TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.SnapshotCount : 0;
        }

        static string HzButtonName(float hz)
        {
            return "Hz" + hz.ToString("F0");
        }

        static void DoClick(string buttonName)
        {
            GameObject go = null;
            if (!buttons.TryGetValue(buttonName, out go) || go == null)
            {
                Check("click target found: " + buttonName, false, "button missing");
                return;
            }

            if (EventSystem.current == null)
            {
                Check("EventSystem for click: " + buttonName, false, "EventSystem.current == null");
                return;
            }

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            ExecuteEvents.Execute<IPointerClickHandler>(go, data, ExecuteEvents.pointerClickHandler);
            log.AppendLine("CLICK dispatch " + buttonName + " | handler=" + go.name
                + " interactable=" + (go.GetComponent<Button>() != null && go.GetComponent<Button>().interactable));
            Flush();
        }

        static GameObject Find(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                {
                    return all[i].gameObject;
                }
            }

            return null;
        }

        static int RuntimeListenerCount(Button button)
        {
            try
            {
                FieldInfo callsField = typeof(UnityEventBase).GetField("m_Calls",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (callsField == null)
                {
                    return -1;
                }

                object calls = callsField.GetValue(button.onClick);
                if (calls == null)
                {
                    return -1;
                }

                FieldInfo runtimeField = calls.GetType().GetField("m_RuntimeCalls",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (runtimeField == null)
                {
                    return -1;
                }

                var list = runtimeField.GetValue(calls) as IList;
                return list == null ? -1 : list.Count;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        static bool AllButtonsHaveRuntimeListener()
        {
            int checkedCount = 0;
            foreach (string name in ButtonNames)
            {
                GameObject go = null;
                if (!buttons.TryGetValue(name, out go) || go == null)
                {
                    return false;
                }

                var b = go.GetComponent<Button>();
                if (b == null)
                {
                    return false;
                }

                int runtime = RuntimeListenerCount(b);
                if (runtime == 0)
                {
                    return false;
                }

                checkedCount++;
            }

            return checkedCount == ButtonNames.Length;
        }

        static string ValuesOf(TwinUiModule module)
        {
            var sb = new StringBuilder();
            if (module == null || module.View == null)
            {
                return "no view";
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "Value" && texts[i].gameObject.activeInHierarchy)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('|');
                    }

                    sb.Append(texts[i].text);
                }
            }

            return sb.ToString();
        }

        static bool AllValuesEqual(TwinUiModule module, string expected)
        {
            int found = 0;
            if (module == null || module.View == null)
            {
                return false;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "Value" && texts[i].gameObject.activeInHierarchy)
                {
                    found++;
                    if (texts[i].text.Trim() != expected)
                    {
                        return false;
                    }
                }
            }

            return found > 0;
        }

        static int CountSeriesPoints(TwinUiModule module)
        {
            if (module == null || module.View == null)
            {
                return 0;
            }

            var charts = module.View.GetComponentsInChildren<TwinUiLineChart>(true);
            int total = 0;
            for (int i = 0; i < charts.Length; i++)
            {
                total += charts[i].PointCount(0);
            }

            return total;
        }

        static string ColorHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c) + "(a=" + c.a.ToString("F2") + ")";
        }

        static string F(double v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }

        static void Retry(double delay, string what)
        {
            tries++;
            if (tries > 60)
            {
                Check("wait timeout: " + what, false, "tries=" + tries);
                Finish();
                return;
            }

            Log("waiting (" + what + ") try=" + tries);
            next = EditorApplication.timeSinceStartup + delay;
        }

        static void Next(double delay)
        {
            tries = 0;
            phase++;
            next = EditorApplication.timeSinceStartup + delay;
            Flush();
        }

        /// <summary>阶段内部等待：只推迟下一次 Tick，**不推进 phase**（T6 的分段填样需要）。</summary>
        static void Wait(double delay)
        {
            next = EditorApplication.timeSinceStartup + delay;
            Flush();
        }

        static void Log(string message)
        {
            log.AppendLine("INFO " + message);
            Flush();
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                failures++;
            }

            log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
            Flush();
        }

        static void ClickResult(string button, string expected, string actual, bool pass)
        {
            string line = "[CLICK] " + button + " 期望=" + expected + " 实际=" + actual
                + " 结论=" + (pass ? "PASS" : "FAIL");
            log.AppendLine(line);
            Debug.Log(line);
            if (!pass)
            {
                failures++;
            }

            Flush();
        }

        static void Shot(string name)
        {
            Directory.CreateDirectory(Shots);
            ScreenCapture.CaptureScreenshot(Shots + "/" + name);
            log.AppendLine("SHOT " + name + " screen=" + Screen.width + "x" + Screen.height);
            Flush();
        }

        static void Flush()
        {
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui61_click_behavior.txt", log.ToString());
            }
            catch (Exception)
            {
                // 落盘失败不能反过来打断测试；Finish 会再写一次。
            }
        }

        static void Finish()
        {
            try
            {
                log.AppendLine("RESULT=" + (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures);
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui61_click_behavior.txt", log.ToString());
            }
            finally
            {
                SessionState.SetBool(Armed, false);
                EditorApplication.isPlaying = false;
            }
        }
    }
}