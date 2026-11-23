// TwinUiOfflineSoakStage.cs —— P11 ③：离线性能 / 交互开销 / 短时长稳自检（UI/58、UI/58b）
// 边界：只驱动既有运行时窗口与页签（等价于用户点页签/拖窗口），不连 ROS2、不下发指令、
//       不写盘到工程内、不改场景/资产；结束无条件退出 Play 并还原 Prefs。
// 口径：编辑器中 EditorApplication.update 的帧间隔 ≠ Player 构建帧时间，也不含 VR 双眼开销；
//       本工具只给"同环境前后可比"的相对结论，不能当作设备端性能承诺。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiOfflineSoakStage
    {
        const string Armed = "P11.OfflineSoak";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui58";
        static readonly string[] Keys =
        {
            "TwinUI.Shell.v2", "TwinUI.Window.Layout.v1", "TwinUI.Modules.v1", "TwinUI.Fab.v1"
        };

        enum Phase { Settle = 0, Idle = 1, Interaction = 2, Soak = 3, Done = 4 }

        static readonly Dictionary<Phase, List<float>> frames = new Dictionary<Phase, List<float>>();
        static readonly StringBuilder log = new StringBuilder();
        // R2 测量隔离：XR affordance 组件只关一次（进程内），并在日志里留证。
        static bool xrIsolated;
        static readonly StringBuilder samples = new StringBuilder();
        static StringBuilder console;
        static TwinUiWindowHost window;
        static TwinUiSurfaceHost surface;
        static Phase phase = Phase.Settle;
        static double launched, phaseStart, prev, lastSample;
        static int soakSeconds = 180;
        static double lastPlayAttempt;
        static int stepFails;
        static float idleMemStart, idleMemEnd, soakMemStart, soakMemEnd;
        static int applyAtIdleStart, applyAtIdleEnd, applyAtInteractionStart, applyAtInteractionEnd;
        static int relayoutAtInteractionStart, relayoutAtInteractionEnd;
        static int renderers, gcAtStart;
        static float interactionWidth = 1200f;
        static double nextAction;
        // R2 A-5：等 shell 就绪期间的诊断节流（每 10 s 打一行，失败时能看出卡在哪一步）。
        static double lastWaitLog;
        static int bootWaitTicks;

        static TwinUiOfflineSoakStage()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Restore;
        }

        [MenuItem("DigitalTwin/UI/58 Offline Performance + Soak 180s (auto-exit)")]
        public static void Run()
        {
            Start(180);
        }

        [MenuItem("DigitalTwin/UI/58b Offline Soak 900s (auto-exit)")]
        public static void RunLong()
        {
            Start(900);
        }

        static void Start(int soak)
        {
            foreach (string key in Keys)
            {
                SessionState.SetBool(Armed + key + ".exists", PlayerPrefs.HasKey(key));
                SessionState.SetString(Armed + key, PlayerPrefs.GetString(key, ""));
            }

            SessionState.SetBool(Armed, true);
            SessionState.SetBool(Armed + ".started", true);
            SessionState.SetBool(Armed + ".playRequested", false);
            SessionState.SetInt(Armed + ".soak", soak);
            frames.Clear();
            log.Clear();
            samples.Clear();
            console = new StringBuilder();
            phase = Phase.Settle;
            stepFails = 0;
            lastPlayAttempt = 0;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                if (console == null)
                {
                    console = new StringBuilder();
                }

                console.AppendLine(type + ": " + condition);
            }
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            Application.logMessageReceived -= OnLog;
            if (!SessionState.GetBool(Armed + ".restore", false))
            {
                return;
            }

            foreach (string key in Keys)
            {
                if (SessionState.GetBool(Armed + key + ".exists", false))
                {
                    PlayerPrefs.SetString(key, SessionState.GetString(Armed + key, ""));
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            PlayerPrefs.Save();
            SessionState.SetBool(Armed + ".restore", false);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (launched <= 0)
            {
                // Domain reload wiped the in-memory state: only continue if the menu started this run.
                if (!SessionState.GetBool(Armed + ".started", false))
                {
                    SessionState.SetBool(Armed, false);
                    EditorApplication.update -= Tick;
                    return;
                }

                if (console == null)
                {
                    console = new StringBuilder();
                }

                // 域重载会把静态字段清零；启动时刻存 SessionState，避免"重载一次就重新计时"。
                // R2 A-5 修复：旧实现会**无条件**采用 SessionState 里的旧 launched，
                // 而 Finish() 从不清这个键 → 上一轮的时间戳把本轮的 120 s 预算直接吃掉，
                // 报错文字仍是 "UI shell not configured within 120s"（看起来像"是真的慢"）。
                // 现在只接受"不超过 30 s 前"的存值（正常域重载 < 10 s；跨轮残留必然远超）。
                double stored;
                string saved = SessionState.GetString(Armed + ".launched", string.Empty);
                bool reused;
                if (double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out stored)
                    && stored > 0 && stored <= now && now - stored <= 30d)
                {
                    launched = stored;
                    reused = true;
                }
                else
                {
                    if (stored > 0f || !string.IsNullOrEmpty(saved))
                    {
                        log.AppendLine("INFO stale launched rejected | stored="
                            + (string.IsNullOrEmpty(saved) ? "<empty>" : saved)
                            + " now=" + now.ToString("R", CultureInfo.InvariantCulture));
                    }

                    launched = now;
                    reused = false;
                    SessionState.SetString(Armed + ".launched",
                        now.ToString("R", CultureInfo.InvariantCulture));
                }

                log.AppendLine("INFO launched=" + launched.ToString("F2", CultureInfo.InvariantCulture)
                    + " reusedSessionState=" + reused
                    + " now=" + now.ToString("F2", CultureInfo.InvariantCulture));

                phaseStart = now;
                prev = now;
                lastSample = now;
                soakSeconds = Mathf.Max(30, SessionState.GetInt(Armed + ".soak", 180));
                nextAction = now + 8;
                log.AppendLine("# P11 UI/58 offline performance + soak");
                log.AppendLine("soak_seconds=" + soakSeconds);
                samples.AppendLine("t,phase,frame_ms,mem_mb,mono_mb,gc0,apply,relayout");
                return;
            }

            // Console 的 Error Pause 会在启动期错误（本工程已知 XRI AffordanceSystem NRE）时把 Play 暂停，
            // 暂停后帧不动、shell 也不会继续装配；自检必须自己恢复运行。
            // R2 测量隔离（Lead 18:09 现场告警）：场景里的 XRI affordance provider 每帧抛 NRE、
            // Editor.log 约 6 MB/min，污染就绪/性能测量。这里只在本次 Play 进程内禁用它们，
            // 不改场景资产、退出不保存；关闭清单与之后的日志增速都写进本阶段日志。
            if (!xrIsolated)
            {
                xrIsolated = true;
                string xriNames;
                int xriClosed = TwinUiTestIsolation.DisableXrAffordances(out xriNames);
                if (log != null)
                {
                    log.AppendLine("INFO XRI affordance providers disabled=" + xriClosed + " | " + xriNames);
                    log.AppendLine("INFO console log at isolation: " + TwinUiTestIsolation.ConsoleLogInfo());
                }
            }
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            // R2 A-5：不能只认静态 Instance——域重载窗口里它可能还是 null（UI/59、UI/60 早已有兜底）。
            surface = TwinUiSurfaceHost.Instance;
            if (surface == null)
            {
                surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            }

            // Play 可能还没真正开始（上一次运行刚退出）或被编辑器延迟：只请求一次进入，不要每 5 s 反复
            // 触发（反复 = 反复域重载 = 计时器被重置，900 s 会被拖成无意义的空转）。
            if (!EditorApplication.isPlaying)
            {
                if (!SessionState.GetBool(Armed + ".playRequested", false))
                {
                    SessionState.SetBool(Armed + ".playRequested", true);
                    EditorApplication.isPlaying = true;
                }
                else if (now - launched > 120)
                {
                    Check("runtime ready", false, "play mode did not start within 120s");
                    Finish();
                }

                return;
            }

            if (window == null || surface == null || surface.Page == null || !surface.IsConfigured)
            {
                // R2 A-5：失败时要有"卡在哪一步"的现场，而不是只留一句超时。
                bootWaitTicks++;
                if (now - lastWaitLog >= 10d)
                {
                    lastWaitLog = now;
                    log.AppendLine("INFO waiting shell | t=" + (now - launched).ToString("F1")
                        + "s window=" + (window != null) + " surface=" + (surface != null)
                        + " page=" + (surface != null && surface.Page != null)
                        + " configured=" + (surface != null && surface.IsConfigured)
                        + " playing=" + EditorApplication.isPlaying
                        + " paused=" + EditorApplication.isPaused
                        + " ticks=" + bootWaitTicks);
                }

                if (now - launched > 120)
                {
                    Check("runtime ready", false, "UI shell not configured within 120s");
                    Finish();
                }

                return;
            }

            // ---- 帧采样
            float dt = (float)(now - prev);
            prev = now;
            if (phase == Phase.Settle)
            {
                if (now - phaseStart >= 8)
                {
                    EnterPhase(Phase.Idle, now);
                }

                return;
            }

            if (phase == Phase.Idle && now - phaseStart >= 20)
            {
                idleMemEnd = MemMb();
                applyAtIdleEnd = surface.Page.ApplyCount;
                EnterPhase(Phase.Interaction, now);
            }
            else if (phase == Phase.Interaction && now - phaseStart >= 30)
            {
                applyAtInteractionEnd = surface.Page.ApplyCount;
                relayoutAtInteractionEnd = surface.InvalidationRelayoutCount;
                EnterPhase(Phase.Soak, now);
            }
            else if (phase == Phase.Soak && now - phaseStart >= soakSeconds)
            {
                soakMemEnd = MemMb();
                EnterPhase(Phase.Done, now);
                Report();
                Finish();
                return;
            }

            if (dt > 0f && dt < .5f)
            {
                List<float> list;
                if (!frames.TryGetValue(phase, out list))
                {
                    list = new List<float>(8192);
                    frames[phase] = list;
                }

                list.Add(dt * 1000f);
            }

            if (phase == Phase.Interaction)
            {
                Interact(now);
            }
            else if (phase == Phase.Soak && now - nextAction >= 0)
            {
                nextAction = now + 10;
                surface.SetTab(surface.Tab == TwinUiWorkspaceRegistry.Twin
                    ? TwinUiWorkspaceRegistry.Control
                    : TwinUiWorkspaceRegistry.Twin, false);
            }

            if (now - lastSample >= 1)
            {
                lastSample = now;
                samples.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F1},{1},{2:F2},{3:F1},{4:F1},{5},{6},{7}",
                    now - launched, phase, dt * 1000f, MemMb(), MonoMb(), GC.CollectionCount(0),
                    surface.Page.ApplyCount, surface.InvalidationRelayoutCount));
            }
        }

        static void EnterPhase(Phase next, double now)
        {
            var list = new List<float>(8192);
            frames[next] = list;
            phase = next;
            phaseStart = now;
            if (next == Phase.Idle)
            {
                surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                SetWidth(1200f);
                idleMemStart = MemMb();
                applyAtIdleStart = surface.Page.ApplyCount;
                gcAtStart = GC.CollectionCount(0);
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
                log.AppendLine("INFO phase idle start: tab=" + surface.Tab + " C=" + F(surface.Page.ContentWidth)
                    + " cols=" + surface.Page.Columns + " cards=" + surface.Page.CardCount
                    + " renderers=" + renderers + " mem_mb=" + F(idleMemStart));
                nextAction = now;
            }
            else if (next == Phase.Interaction)
            {
                applyAtInteractionStart = surface.Page.ApplyCount;
                relayoutAtInteractionStart = surface.InvalidationRelayoutCount;
                log.AppendLine("INFO phase interaction start: apply=" + applyAtInteractionStart
                    + " relayout=" + relayoutAtInteractionStart);
                nextAction = now;
            }
            else if (next == Phase.Soak)
            {
                soakMemStart = MemMb();
                log.AppendLine("INFO phase soak start: " + soakSeconds + "s mem_mb=" + F(soakMemStart)
                    + " apply=" + surface.Page.ApplyCount);
                nextAction = now;
            }
        }

        static void Interact(double now)
        {
            if (now < nextAction)
            {
                return;
            }

            nextAction = now + .5;
            // 每 0.5 s 一次交互：2 s 周期切页签、4 s 周期拆出/归位、6 s 周期改宽
            int tick = Mathf.FloorToInt((float)(now - phaseStart) / .5f);
            if (tick % 4 == 0)
            {
                int index = (tick / 4) % 3;
                surface.SetTab(index == 0 ? TwinUiWorkspaceRegistry.Twin
                    : index == 1 ? TwinUiWorkspaceRegistry.Control
                    : TwinUiWorkspaceRegistry.Mission, false);
            }

            if (tick % 8 == 3)
            {
                surface.Detach("joints");
            }
            else if (tick % 8 == 7)
            {
                surface.DockBack("joints");
            }

            if (tick % 12 == 5)
            {
                interactionWidth = interactionWidth > 1300f ? 1200f : 1600f;
                SetWidth(interactionWidth);
            }
            else if (tick % 12 == 11)
            {
                SetWidth(1200f);
            }
        }

        static void SetWidth(float width)
        {
            if (window == null)
            {
                return;
            }

            Rect r = window.WindowRect;
            r.width = width;
            r.height = 820f;
            r.x = 16f;
            r.y = 80f;
            window.ApplyLayout(r, false, false);
        }

        static float MemMb()
        {
            return Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
        }

        static float MonoMb()
        {
            return Profiler.GetMonoUsedSizeLong() / 1048576f;
        }

        static void Report()
        {
            Check("idle p95 frame <= 33.3ms", P95(Phase.Idle) <= 33.3f, "p95=" + F(P95(Phase.Idle)));
            Check("interaction p95 frame <= 50ms", P95(Phase.Interaction) <= 50f, "p95=" + F(P95(Phase.Interaction)));
            Check("soak p95 frame <= 33.3ms", P95(Phase.Soak) <= 33.3f, "p95=" + F(P95(Phase.Soak)));
            Check("soak memory growth <= 64MB", soakMemEnd - soakMemStart <= 64f,
                "start=" + F(soakMemStart) + " end=" + F(soakMemEnd));
            int interactionApplies = applyAtInteractionEnd - applyAtInteractionStart;
            int interactionFrames = frames.ContainsKey(Phase.Interaction) ? frames[Phase.Interaction].Count : 0;
            Check("interaction relayout bounded", interactionApplies <= interactionFrames / 10 + 40,
                "apply=" + interactionApplies + " frames=" + interactionFrames
                + " relayout=" + (relayoutAtInteractionEnd - relayoutAtInteractionStart));
            Check("idle minimal relayout", applyAtIdleEnd - applyAtIdleStart <= 2,
                "apply=" + (applyAtIdleEnd - applyAtIdleStart));

            var stats = new StringBuilder();
            stats.AppendLine("| 阶段 | 帧数 | P50 ms | P95 ms | 最大 ms |");
            stats.AppendLine("|---|---|---|---|---|");
            AppendRow(stats, "idle(20s)", Phase.Idle);
            AppendRow(stats, "interaction(30s)", Phase.Interaction);
            AppendRow(stats, "soak(" + soakSeconds + "s)", Phase.Soak);
            log.AppendLine(stats.ToString());
            log.AppendLine("INFO memory idle start=" + F(idleMemStart) + " end=" + F(idleMemEnd)
                + " soak start=" + F(soakMemStart) + " end=" + F(soakMemEnd)
                + " renderers=" + renderers + " gc0_start=" + gcAtStart + " gc0_end=" + GC.CollectionCount(0));
            log.AppendLine("INFO interaction apply=" + interactionApplies
                + " relayout=" + (relayoutAtInteractionEnd - relayoutAtInteractionStart)
                + " frames=" + interactionFrames);
            log.AppendLine("INFO editor=" + Application.unityVersion + " screen=" + Screen.width + "x" + Screen.height
                + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            log.AppendLine("NOTE 编辑器帧间隔不等价 Player/VR 帧时间；本表只做同环境相对比较。");
        }

        static void AppendRow(StringBuilder sb, string name, Phase p)
        {
            List<float> list;
            if (!frames.TryGetValue(p, out list) || list.Count == 0)
            {
                sb.AppendLine("| " + name + " | 0 | - | - | - |");
                return;
            }

            var copy = new List<float>(list);
            copy.Sort();
            sb.AppendLine("| " + name + " | " + copy.Count + " | " + F(Percentile(copy, .5f)) + " | "
                + F(Percentile(copy, .95f)) + " | " + F(copy[copy.Count - 1]) + " |");
        }

        static float Percentile(List<float> sorted, float q)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[index];
        }

        static float P95(Phase p)
        {
            List<float> list;
            if (!frames.TryGetValue(p, out list) || list.Count == 0)
            {
                return float.PositiveInfinity;
            }

            var copy = new List<float>(list);
            copy.Sort();
            return Percentile(copy, .95f);
        }

        static string F(float v)
        {
            return v.ToString("F2");
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                stepFails++;
            }

            log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        static void Finish()
        {
            int uiExceptions = 0, total = 0;
            foreach (string raw in (console != null ? console.ToString() : string.Empty).Split('\n'))
            {
                if (raw.Trim().Length == 0)
                {
                    continue;
                }

                total++;
                if (raw.IndexOf("DigitalTwin.UI", StringComparison.Ordinal) >= 0)
                {
                    uiExceptions++;
                }
            }

            log.AppendLine("INFO console errors total=" + total + " uiStack=" + uiExceptions);
            Check("no exception from UI code", uiExceptions == 0, "uiStack=" + uiExceptions);
            log.AppendLine("RESULT=" + (stepFails == 0 ? "PASS" : "FAIL") + " failures=" + stepFails);
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                // 文件名带 soak 时长：180 s 与 900 s 互不覆盖
                File.WriteAllText(Root + "/logs/ui58_perf_soak_" + soakSeconds + ".txt", log.ToString());
                File.WriteAllText(Root + "/logs/ui58_samples_" + soakSeconds + ".csv", samples.ToString());
            }
            finally
            {
                SessionState.SetBool(Armed, false);
                SessionState.SetBool(Armed + ".started", false);
                SessionState.SetBool(Armed + ".restore", true);
                // R2 A-5：清掉本轮的启动时刻，避免下一轮把旧时间戳当成本轮预算（跨轮污染）。
                SessionState.EraseString(Armed + ".launched");
                EditorApplication.isPlaying = false;
            }
        }
    }
}