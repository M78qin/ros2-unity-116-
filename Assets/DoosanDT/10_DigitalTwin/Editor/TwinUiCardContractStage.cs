// TwinUiCardContractStage.cs -- P11 step 2: card extension contract + measurement invalidation + layout stability (UI/57)
// Scope: reads existing runtime objects, adds ONE temporary P11Probe child under a card view (destroyed before exit),
//        does not modify the scene, business data, PlayerPrefs (backed up/restored), ROS2 or command paths.
// Evidence: geometry / state / call counts only. It cannot replace the human visual sign-off.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiCardContractStage
    {
        const string Armed = "P11.CardContract";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui57";
        const string SHOTS = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\shots";
        static readonly string[] Keys =
        {
            "TwinUI.Shell.v2", "TwinUI.Window.Layout.v1", "TwinUI.Modules.v1", "TwinUI.Fab.v1"
        };

        static readonly float[] Sweep = { 320f, 480f, 760f, 1000f, 1400f };

        // R2 测量隔离：XR affordance 组件只关一次（进程内），并在日志里留证。
        static bool xrIsolated;
        static StringBuilder log;
        static StringBuilder console;
        static int step, fails, infos;
        static double next, deadline;
        static TwinUiWindowHost window;
        static TwinUiSurfaceHost surface;

        static float basePageHeight;
        static uint baseSig;
        static int baseApply, baseRelayout;
        static TwinUiModule alarmModule;
        static bool pageSettled;
        static RectTransform probe;
        static float staleMeasure;
        static float originalMeasure;
        static readonly Dictionary<string, string> baseCards = new Dictionary<string, string>(StringComparer.Ordinal);

        static TwinUiCardContractStage()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Restore;
        }

        [MenuItem("DigitalTwin/UI/57 Card Contract + Invalidation (auto-exit)")]
        public static void Run()
        {
            foreach (string key in Keys)
            {
                SessionState.SetBool(Armed + key + ".exists", PlayerPrefs.HasKey(key));
                SessionState.SetString(Armed + key, PlayerPrefs.GetString(key, ""));
            }

            SessionState.SetBool(Armed, true);
            SessionState.SetBool(Armed + ".started", true);
            step = 0;
            log = null;
            pageSettled = false;
            console = new StringBuilder();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            deadline = EditorApplication.timeSinceStartup + 150;
            Directory.CreateDirectory(Root + "/logs");
            File.WriteAllText(Root + "/logs/ui57_running.flag", DateTime.Now.ToString("O"));
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

            if (log == null)
            {
                // After a domain reload the in-memory state is gone. If this run was not started
                // from the menu in this load, drop the armed flag instead of emitting a bogus cycle.
                if (!SessionState.GetBool(Armed + ".started", false))
                {
                    SessionState.SetBool(Armed, false);
                    EditorApplication.update -= Tick;
                    return;
                }

                log = new StringBuilder();
                if (console == null)
                {
                    console = new StringBuilder();
                }

                step = 0;
                fails = 0;
                infos = 0;
                deadline = EditorApplication.timeSinceStartup + 150;
                next = EditorApplication.timeSinceStartup + 3;
            }

            if (EditorApplication.timeSinceStartup > deadline)
            {
                Check("watchdog", false, "150s timeout at step=" + step);
                Finish();
                return;
            }

            if (EditorApplication.timeSinceStartup < next)
            {
                return;
            }

            // Console 的 Error Pause 会在启动期错误（已知 XRI AffordanceSystem NRE）时暂停 Play；自检自行恢复。
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
            surface = TwinUiSurfaceHost.Instance;
            // R2 A-5：不能只认静态 Instance（域重载窗口里可能还是 null），且失败要留现场：
            // 本轮回归第一次就是这句静默 return 把 150 s 全吃掉，日志只剩 watchdog。
            if (surface == null)
            {
                surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            }

            if (window == null || surface == null || surface.Page == null || !surface.IsConfigured)
            {
                log.AppendLine("INFO waiting boot | window=" + (window != null)
                    + " surface=" + (surface != null)
                    + " page=" + (surface != null && surface.Page != null)
                    + " configured=" + (surface != null && surface.IsConfigured)
                    + " playing=" + EditorApplication.isPlaying
                    + " paused=" + EditorApplication.isPaused
                    + " t=" + (EditorApplication.timeSinceStartup - (deadline - 150)).ToString("F1"));
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                Check("exception at step " + step, false, e.ToString());
                Finish();
            }
        }

        static void Step()
        {
            switch (step)
            {
                case 0:
                    surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                    SetWidth(1200);
                    break;
                case 1:
                    Contract();
                    ResponsiveMeasureChecks();
                    break;
                case 2:
                    surface.SetTab(TwinUiWorkspaceRegistry.Overview, false);
                    SetWidth(1200);
                    if (!pageSettled)
                    {
                        // 基线必须取"页面已稳定"之后的状态：告警卡的首次行数建立
                        // （binder 首个快照到达）本身会递增一次 LayoutVersion，如果
                        // 落在基线之后就会被误计成"两次失效重排"。先主动刷新一次告警卡，
                        // 再多等一拍让失效轮询消化它，然后才取基线。
                        var alarmSettle = surface.Find("alarm");
                        if (alarmSettle != null)
                        {
                            alarmSettle.Refresh();
                        }

                        pageSettled = true;
                        step--;
                        break;
                    }

                    Baseline();
                    alarmModule = surface.Find("alarm");
                    if (alarmModule == null || alarmModule.View == null || surface.CardFor("alarm") == null)
                    {
                        Check("alarm card available", false, "module/card missing");
                        break;
                    }

                    // 未加探针时的体高（回退后必须回到这个值）
                    originalMeasure = TwinUiContentFit.LegacyCardHeight(ContentWidth(), alarmModule);
                    probe = MakeProbe(alarmModule.View);
                    break;
                case 3:
                    alarmModule.InvalidateContentLayout();
                    break;
                case 4:
                    {
                        float grown = TwinUiContentFit.LegacyCardHeight(ContentWidth(), alarmModule);
                        Check("invalidation grows measured body", grown > originalMeasure + 8f,
                            "grown=" + F(grown) + " original=" + F(originalMeasure));
                        Check("invalidation triggers exactly one relayout",
                            surface.InvalidationRelayoutCount - baseRelayout == 1,
                            "delta=" + (surface.InvalidationRelayoutCount - baseRelayout));
                        Check("page height follows content", surface.Page.ContentHeight > basePageHeight + 8f,
                            "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
                        Diag("grown");
                        // Destroy 延迟到帧末：下一步再失效，否则会把还活着的探针又量进去
                        if (probe != null)
                        {
                            UnityEngine.Object.Destroy(probe.gameObject);
                            probe = null;
                        }
                    }
                    break;
                case 5:
                    alarmModule.InvalidateContentLayout();
                    break;
                case 6:
                    Diag("revert settle");
                    {
                        float back = TwinUiContentFit.LegacyCardHeight(ContentWidth(), alarmModule);
                        Check("measure restores after revert", Mathf.Abs(back - originalMeasure) <= 1f,
                            "back=" + F(back) + " original=" + F(originalMeasure));
                        Check("page height restores after revert",
                            Mathf.Abs(surface.Page.ContentHeight - basePageHeight) <= 1f,
                            "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
                        baseApply = surface.Page.ApplyCount;
                        baseRelayout = surface.InvalidationRelayoutCount;
                    }
                    break;
                case 7:
                    break;
                case 8:
                    Check("idle no page relayout", surface.Page.ApplyCount == baseApply,
                        "apply=" + surface.Page.ApplyCount + " base=" + baseApply);
                    Check("idle no invalidation relayout", surface.InvalidationRelayoutCount == baseRelayout,
                        "relayout=" + surface.InvalidationRelayoutCount + " base=" + baseRelayout);
                    Check("idle layout geometry stable", SameLayout(), "signature drifted, see layout diff lines if FAIL");
                    // 同一页宽连排两次：不是硬编码 1200，避免把"窗口边框 1-2 px"当成漂移
                    surface.ApplyPageLayout(surface.PageWidth, surface.PageHeight);
                    surface.ApplyPageLayout(surface.PageWidth, surface.PageHeight);
                    break;
                case 9:
                    Check("repeat apply identical height", Mathf.Abs(surface.Page.ContentHeight - basePageHeight) <= 1f,
                        "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
                    Check("repeat apply identical geometry", SameLayout(), "layout signature mismatch, see layout diff lines if FAIL");
                    Check("repeat apply counts twice", surface.Page.ApplyCount == baseApply + 2,
                        "apply=" + surface.Page.ApplyCount + " expected=" + (baseApply + 2));
                    SetWidth(1600);
                    break;
                case 10:
                    SetWidth(1200);
                    break;
                case 11:
                    CycleCheck(1);
                    break;
                case 12:
                    SetWidth(1600);
                    break;
                case 13:
                    SetWidth(1200);
                    break;
                case 14:
                    CycleCheck(2);
                    break;
                case 15:
                    SetWidth(1600);
                    break;
                case 16:
                    SetWidth(1200);
                    break;
                case 17:
                    CycleCheck(3);
                    break;
                case 18:
                    surface.Detach("joints");
                    break;
                case 19:
                    surface.DockBack("joints");
                    break;
                case 20:
                    Check("card/floating round trip height",
                        Mathf.Abs(surface.Page.ContentHeight - basePageHeight) <= 2f,
                        "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
                    Check("card/floating round trip geometry", SameLayout(), "layout signature mismatch, see layout diff lines if FAIL");
                    surface.SetTab(TwinUiWorkspaceRegistry.Control, false);
                    break;
                case 21:
                    surface.SetTab(TwinUiWorkspaceRegistry.Overview, false);
                    break;
                case 22:
                    Check("tab round trip geometry", SameLayout(), "layout signature mismatch, see layout diff lines if FAIL");
                    Check("tab round trip height", Mathf.Abs(surface.Page.ContentHeight - basePageHeight) <= 2f,
                        "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
                    SetWidth(720);
                    break;
                case 23:
                    CompactChecks();
                    break;
                case 24:
                    SetWidth(1200);
                    break;
                case 25:
                    Shot("ui57_1200");
                    Finish();
                    return;
            }

            bool longWait = step == 7;
            step++;
            next = EditorApplication.timeSinceStartup + (longWait ? 2f : .6f);
        }

        static void Diag(string tag)
        {
            var card = surface.CardFor("alarm");
            float cardHeight = card != null && card.Root != null ? card.Root.sizeDelta.y : -1f;
            float viewHeight = alarmModule != null && alarmModule.View != null ? alarmModule.View.rect.height : -1f;
            float measured = alarmModule != null && alarmModule.View != null
                ? TwinUiContentFit.LegacyCardHeight(ContentWidth(), alarmModule)
                : -1f;
            Info("diag " + tag, "pageH=" + F(surface.Page.ContentHeight) + " alarmCardH=" + F(cardHeight)
                + " alarmViewH=" + F(viewHeight) + " legacyMeasure=" + F(measured)
                + " apply=" + surface.Page.ApplyCount + " relayout=" + surface.InvalidationRelayoutCount
                + " probeAlive=" + (probe != null));
        }

        static void CycleCheck(int index)
        {
            Check("width cycle " + index + " return height",
                Mathf.Abs(surface.Page.ContentHeight - basePageHeight) <= 1f,
                "page=" + F(surface.Page.ContentHeight) + " base=" + F(basePageHeight));
            Check("width cycle " + index + " return geometry", SameLayout(), "layout signature mismatch, see layout diff lines if FAIL");
            Check("width cycle " + index + " no unexpected invalidation",
                surface.InvalidationRelayoutCount == baseRelayout,
                "relayout=" + surface.InvalidationRelayoutCount + " base=" + baseRelayout);
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
            Canvas.ForceUpdateCanvases();
        }

        static float ContentWidth()
        {
            return TwinUiLayoutContract.ContentWidth(
                TwinUiLayoutContract.ColumnWidth(surface.Page.ContentWidth, surface.Page.Columns));
        }

        static void Baseline()
        {
            Canvas.ForceUpdateCanvases();
            basePageHeight = surface.Page.ContentHeight;
            baseSig = LayoutSig();
            baseApply = surface.Page.ApplyCount;
            baseRelayout = surface.InvalidationRelayoutCount;
            baseCards.Clear();
            foreach (var card in surface.Page.Cards)
            {
                if (card == null || card.Root == null)
                {
                    continue;
                }

                baseCards[card.ModuleId] = Geo(card);
            }

            Info("baseline", "tab=" + surface.Tab + " pageH=" + F(basePageHeight)
                + " C=" + F(surface.Page.ContentWidth) + " cols=" + surface.Page.Columns
                + " cards=" + surface.Page.CardCount + " apply=" + baseApply
                + " invalidationRelayout=" + baseRelayout + " modules=" + surface.ModuleCount
                + " sig=" + baseSig.ToString("X8"));
        }

        static string Geo(TwinUiCardView card)
        {
            Rect rt = card.Root.rect;
            string view = "-";
            var module = surface.Find(card.ModuleId);
            if (module != null && module.View != null)
            {
                Rect v = module.View.rect;
                view = F(module.View.anchoredPosition.x) + "," + F(module.View.anchoredPosition.y)
                    + "," + F(v.width) + "x" + F(v.height);
            }

            return "card(" + F(card.Root.anchoredPosition.x) + "," + F(card.Root.anchoredPosition.y)
                + "," + F(rt.width) + "x" + F(rt.height) + ") view(" + view + ") scale=" + F(card.CurrentScale);
        }

        /// <summary>Layout equality with an explicit per-card diff so a failure is diagnosable, not just red.</summary>
        static bool SameLayout()
        {
            bool same = LayoutSig() == baseSig;
            if (same)
            {
                return true;
            }

            foreach (var pair in baseCards)
            {
                var card = surface.CardFor(pair.Key);
                if (card == null || card.Root == null)
                {
                    Info("layout diff", pair.Key + " card missing now");
                    continue;
                }

                string now = Geo(card);
                if (now != pair.Value)
                {
                    Info("layout diff", pair.Key + " base=" + pair.Value + " now=" + now);
                }
            }

            return false;
        }

        // ------------------------------------------------------------ extension contract

        static void Contract()
        {
            var modules = surface.Modules;
            Check("catalog registers modules", modules.Count >= 19, "count=" + modules.Count);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            for (int w = 0; w < TwinUiWorkspaceRegistry.All.Count; w++)
            {
                string[] widgets = TwinUiWorkspaceRegistry.All[w].WidgetIds ?? new string[0];
                for (int i = 0; i < widgets.Length; i++)
                {
                    placed.Add(widgets[i]);
                }
            }

            int responsive = 0, wide = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                string tag = "module " + m.Id;
                Check(tag + " id unique/non-empty", !string.IsNullOrEmpty(m.Id) && ids.Add(m.Id), "id=" + m.Id);
                Check(tag + " id has no whitespace", m.Id != null && m.Id.IndexOf(' ') < 0, "id=" + m.Id);
                Check(tag + " title", !string.IsNullOrEmpty(m.Title), "title=" + m.Title);
                Check(tag + " card size", m.CardSize.x >= 200f && m.CardSize.y >= 120f
                    && m.CardSize.y <= TwinUiLayoutContract.MaxCardHeight, "card=" + m.CardSize);
                Check(tag + " min size <= size",
                    m.CardMinSize.x <= m.CardSize.x && m.CardMinSize.y <= m.CardSize.y,
                    "min=" + m.CardMinSize + " card=" + m.CardSize);
                Check(tag + " placed in some workspace", placed.Contains(m.Id), "id=" + m.Id);

                if (!string.IsNullOrEmpty(m.WorkspaceId))
                {
                    var ws = TwinUiWorkspaceRegistry.Find(m.WorkspaceId);
                    Check(tag + " workspace resolves", ws != null, "workspace=" + m.WorkspaceId);
                    if (ws != null && !string.IsNullOrEmpty(m.Group))
                    {
                        bool known = false;
                        for (int s = 0; s < ws.SubNavIds.Length; s++)
                        {
                            known |= ws.SubNavIds[s] == m.Group;
                        }

                        Check(tag + " group in subnav", known,
                            "group=" + m.Group + " subnav=" + string.Join(",", ws.SubNavIds));
                    }
                }

                if (m.ResponsiveContent)
                {
                    responsive++;
                    wide += m.WideContent ? 1 : 0;
                }
            }

            Info("catalog", "modules=" + modules.Count + " responsive=" + responsive
                + " wide=" + wide + " placed=" + placed.Count);

            var missing = new List<string>();
            for (int w = 0; w < TwinUiWorkspaceRegistry.All.Count; w++)
            {
                foreach (string id in TwinUiWorkspaceRegistry.All[w].WidgetIds)
                {
                    if (surface.Find(id) == null)
                    {
                        missing.Add(TwinUiWorkspaceRegistry.All[w].Id + ":" + id);
                    }
                }
            }

            Check("workspace widget ids all registered", missing.Count == 0, string.Join(",", missing));
        }

        static void ResponsiveMeasureChecks()
        {
            var modules = surface.Modules;
            int built = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (!m.ResponsiveContent)
                {
                    continue;
                }

                if (m.View == null)
                {
                    Info("responsive measure skipped", m.Id + " (view not built on this page)");
                    continue;
                }

                built++;
                float first = m.MeasureContentHeight(600f, TwinUiModuleSurface.Card);
                float second = m.MeasureContentHeight(600f, TwinUiModuleSurface.Card);
                Check("measure pure " + m.Id, Mathf.Abs(first - second) <= .01f, "a=" + F(first) + " b=" + F(second));
                Check("measure finite " + m.Id,
                    !float.IsNaN(first) && !float.IsInfinity(first) && first > 0f, "v=" + F(first));

                m.ApplyContentLayout(600f, TwinUiModuleSurface.Card);
                Canvas.ForceUpdateCanvases();
                uint s1 = Sig(m.View);
                m.ApplyContentLayout(600f, TwinUiModuleSurface.Card);
                Canvas.ForceUpdateCanvases();
                uint s2 = Sig(m.View);
                Check("apply layout idempotent " + m.Id, s1 == s2, s1.ToString("X8") + " vs " + s2.ToString("X8"));

                float h = TwinUiLayoutContract.CardHeight(m, TwinUiLayoutContract.ContentWidth(560f),
                    TwinUiModuleSurface.Card);
                Check("card height in range " + m.Id,
                    h >= TwinUiLayoutContract.MinCardHeight - .5f && h <= TwinUiLayoutContract.MaxCardHeight + .5f,
                    "h=" + F(h));

                var sb = new StringBuilder();
                for (int k = 0; k < Sweep.Length; k++)
                {
                    float v = m.MeasureContentHeight(Sweep[k], TwinUiModuleSurface.Card);
                    sb.Append((int)Sweep[k]).Append("->").Append(F(v)).Append("  ");
                    Check("sweep finite " + m.Id + "@" + (int)Sweep[k],
                        !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f && v <= 4096f, "h=" + F(v));
                }

                Info("measure sweep", m.Id + " : " + sb);
                m.ApplyContentLayout(600f, TwinUiModuleSurface.Card);
            }

            Check("responsive modules measured", built >= 3, "built=" + built);
        }

        static void CompactChecks()
        {
            var page = surface.Page;
            var viewport = (RectTransform)page.Root.parent.parent;
            Check("720 one column", page.Columns == 1, "cols=" + page.Columns + " C=" + F(page.ContentWidth));
            Check("720 C matches contract",
                Mathf.Abs(page.ContentWidth - TwinUiLayoutContract.C(page.Root.rect.width)) <= 1f,
                "C=" + F(page.ContentWidth) + " pageW=" + F(page.Root.rect.width));

            foreach (var card in page.Cards)
            {
                if (!card.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Rect b = Bounds(card.Root, viewport);
                Check("720 card inside viewport " + card.ModuleId,
                    b.xMin >= viewport.rect.xMin - 1f && b.xMax <= viewport.rect.xMax + 1f,
                    "card=" + b + " viewport=" + viewport.rect);
                Info("720 card", card.ModuleId + " visual=" + F(b.width) + " scale=" + F(card.CurrentScale)
                    + " design=" + card.DesignSize);
            }

            Shot("ui57_720");
        }

        // ------------------------------------------------------------ helpers

        static RectTransform MakeProbe(RectTransform parent)
        {
            var go = new GameObject("P11Probe", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(40f, 10f);
            rt.anchoredPosition = new Vector2(4f, -(parent.rect.height + 80f));
            return rt;
        }

        static Rect Bounds(RectTransform rt, RectTransform space)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity), hi = -lo;
            foreach (var corner in corners)
            {
                Vector2 p = space.InverseTransformPoint(corner);
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }

            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        /// <summary>Deep signature: used only for immediate before/after comparisons inside one view.</summary>
        static uint Sig(Transform root)
        {
            uint h = 2166136261u;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                h = Mix(h, rt.gameObject.activeSelf ? 1f : 0f);
                h = Mix(h, rt.anchoredPosition.x);
                h = Mix(h, rt.anchoredPosition.y);
                h = Mix(h, rt.sizeDelta.x);
                h = Mix(h, rt.sizeDelta.y);
                h = Mix(h, rt.anchorMin.x);
                h = Mix(h, rt.anchorMin.y);
                h = Mix(h, rt.anchorMax.x);
                h = Mix(h, rt.anchorMax.y);
                h = Mix(h, rt.localScale.x);
            }

            return h;
        }

        /// <summary>
        /// Layout signature: page height/columns/C + every card root rect + its content view rect.
        /// Business content (chart points, sparkline, text reflow) is excluded on purpose: it changes
        /// with data, not with layout. Layout drift must be judged on the layout layer only.
        /// </summary>
        static uint LayoutSig()
        {
            var page = surface.Page;
            uint h = 2166136261u;
            h = Mix(h, page.ContentHeight);
            h = Mix(h, page.ContentWidth);
            h = Mix(h, page.Columns);
            var cards = page.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null)
                {
                    continue;
                }

                h = Mix(h, card.gameObject.activeSelf ? 1f : 0f);
                var rt = card.Root;
                if (rt == null)
                {
                    continue;
                }

                h = Mix(h, rt.anchoredPosition.x);
                h = Mix(h, rt.anchoredPosition.y);
                h = Mix(h, rt.sizeDelta.x);
                h = Mix(h, rt.sizeDelta.y);
                h = Mix(h, rt.localScale.x);
                var module = surface.Find(card.ModuleId);
                var view = module != null ? module.View : null;
                if (view != null)
                {
                    h = Mix(h, view.anchoredPosition.x);
                    h = Mix(h, view.anchoredPosition.y);
                    h = Mix(h, view.sizeDelta.x);
                    h = Mix(h, view.sizeDelta.y);
                }
            }

            return h;
        }

        static uint Mix(uint h, float v)
        {
            int q = Mathf.RoundToInt(v * 100f);
            h ^= (uint)q;
            h *= 16777619u;
            return h;
        }

        static string F(float v)
        {
            return v.ToString("F2");
        }

        static void Shot(string name)
        {
            Directory.CreateDirectory(SHOTS);
            // R2 A-4：屏幕级截图必须把真实屏幕尺寸写进文件名，防止再用窗口宽度冒充档位。
            // 注意：CaptureScreenshotAsTexture 在 EditorApplication.update 里返回 null（必须等 end-of-frame），
            // 这里用异步 CaptureScreenshot（写盘尺寸=真实屏幕），文件名带真实尺寸；像素由事后 IHDR 复核。
            string real = name + "_screen" + Screen.width + "x" + Screen.height + ".png";
            ScreenCapture.CaptureScreenshot(SHOTS + "/" + real);
            Info("screenshot", real + " at " + Screen.width + "x" + Screen.height);
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                fails++;
            }

            log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        static void Info(string name, string detail)
        {
            infos++;
            log.AppendLine("INFO " + name + " | " + detail);
        }

        static void Finish()
        {
            int uiExceptions = 0, total = 0;
            var lines = new List<string>();
            string rawConsole = console != null ? console.ToString() : string.Empty;
            foreach (string raw in rawConsole.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                lines.Add(line);
                total++;
                if (line.IndexOf("DigitalTwin.UI", StringComparison.Ordinal) >= 0)
                {
                    uiExceptions++;
                }
            }

            log.AppendLine("INFO console errors total=" + total + " uiStack=" + uiExceptions);
            log.AppendLine("INFO console lines (verbatim, for review):");
            foreach (string line in lines)
            {
                log.AppendLine("  " + line);
            }

            Check("no exception from UI code", uiExceptions == 0, "uiStack=" + uiExceptions);
            log.AppendLine("RESULT=" + (fails == 0 ? "PASS" : "FAIL") + " failures=" + fails + " infos=" + infos);
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui57_card_contract.txt", log.ToString());
                string flag = Root + "/logs/ui57_running.flag";
                if (File.Exists(flag))
                {
                    File.Delete(flag);
                }
            }
            finally
            {
                SessionState.SetBool(Armed, false);
                SessionState.SetBool(Armed + ".started", false);
                SessionState.SetBool(Armed + ".restore", true);
                EditorApplication.isPlaying = false;
            }
        }
    }
}