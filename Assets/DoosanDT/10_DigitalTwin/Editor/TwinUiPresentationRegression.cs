using System;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    // Integration regression: drive the real window, then inspect transformed bounds.
    [InitializeOnLoad]
    public static class TwinUiPresentationRegression
    {
        const string Armed = "Codex.UiPresentationRegression";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui56";
        const string SHOTS = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\shots";
        static readonly string[] Keys = { "TwinUI.Shell.v2", "TwinUI.Window.Layout.v1", "TwinUI.Modules.v1" };
        static readonly int[] Widths = { 720, 900, 1200, 1600 };
        // R2 测量隔离：XR affordance 组件只关一次（进程内），并在日志里留证。
        static bool xrIsolated;
        static StringBuilder log;
        static int step, fails;
        static double next, deadline;
        static TwinUiWindowHost window;
        static TwinUiShellV2 shell;
        static TwinUiSurfaceHost surface;
        static int viewId, floatingBefore;
        static TwinUiPresentationRegression() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Restore; }

        [MenuItem("DigitalTwin/UI/56 Presentation Regression (auto-exit)")]
        public static void Run()
        {
            foreach (string key in Keys)
            {
                SessionState.SetBool(Armed + key + ".exists", PlayerPrefs.HasKey(key));
                SessionState.SetString(Armed + key, PlayerPrefs.GetString(key, ""));
            }
            SessionState.SetBool(Armed, true);
            SessionState.SetString(Armed + ".phase", File.Exists(Root + "/green.flag") ? "green" : "red");
            step = 0; log = null;
            deadline = EditorApplication.timeSinceStartup + 90;
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
        }

        static void Restore(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Armed + ".restore", false)) return;
            foreach (string key in Keys)
                if (SessionState.GetBool(Armed + key + ".exists", false)) PlayerPrefs.SetString(key, SessionState.GetString(Armed + key, ""));
                else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            SessionState.SetBool(Armed + ".restore", false);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            // The project keeps Error Pause on; the known startup XRI AffordanceSystem NRE
            // pauses Play and then this stage observes zero frames. Clear it every tick (same
            // guard UI/57 and UI/58 already use) so the regression keeps running.
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
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            if (log == null)
            {
                log = new StringBuilder(); step = 0; fails = 0;
                deadline = EditorApplication.timeSinceStartup + 90;
                next = EditorApplication.timeSinceStartup + 3;
            }
            if (EditorApplication.timeSinceStartup > deadline) { Check("watchdog", false, "90s"); Finish(); return; }
            if (EditorApplication.timeSinceStartup < next) return;
            window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            shell = UnityEngine.Object.FindObjectOfType<TwinUiShellV2>();
            surface = TwinUiSurfaceHost.Instance;
            // R2 A-5：不能只认静态 Instance——域重载窗口里它可能还是 null，旧实现会静默等到
            // watchdog（"90s"）却没有"卡在哪一步"的现场（UI/57 本轮就是这样超时的）。
            if (surface == null)
            {
                surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            }

            if (window == null || shell == null || surface == null || surface.Page == null)
            {
                log.AppendLine("INFO waiting boot | window=" + (window != null) + " shell=" + (shell != null)
                    + " surface=" + (surface != null) + " page=" + (surface != null && surface.Page != null)
                    + " playing=" + EditorApplication.isPlaying
                    + " paused=" + EditorApplication.isPaused
                    + " t=" + (EditorApplication.timeSinceStartup - (deadline - 90)).ToString("F1"));
                return;
            }
            try
            {
                int index = step / 2;
                if (index >= Widths.Length) { Extra(); next = EditorApplication.timeSinceStartup + .8; return; }
                if (step % 2 == 0)
                {
                    surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                    Rect r = window.WindowRect; r.width = Widths[index]; r.height = 850; r.x = 16; r.y = 80;
                    window.ApplyLayout(r, false, false);
                }
                else Measure(Widths[index]);
                step++; next = EditorApplication.timeSinceStartup + .8;
            }
            catch (Exception e) { Check("exception", false, e.ToString()); Finish(); }
        }

        static void Extra()
        {
            var module = surface.Find("joints");
            if (step == 8) { viewId = module.View.GetInstanceID(); floatingBefore = surface.FloatingCount; surface.Detach("joints"); }
            if (step == 9 || step == 11)
            {
                var windows = UnityEngine.Object.FindObjectsOfType<TwinUiModuleWindow>();
                foreach (var win in windows)
                    if (win.ModuleId == "joints") win.ApplyWindowRect(new Rect(900, 250, step == 9 ? 800 : 500, 420));
            }
            if (step == 10 || step == 12)
            {
                Check("floating same instance", module.View.GetInstanceID() == viewId && surface.FloatingCount == floatingBefore + 1, "view=" + viewId);
                Check("floating no scaler", module.View.GetComponent<TwinUiDesignScaler>() == null && module.View.localScale == Vector3.one, "scale=" + module.View.localScale);
                var chart = module.View.GetComponentInChildren<TwinUiLineChart>();
                Check("floating chart uses height", chart != null && chart.rectTransform.rect.height >= module.View.rect.height * .5f,
                    "chart=" + (chart != null ? chart.rectTransform.rect.height : 0) + " body=" + module.View.rect.height);
                float maxFont = 0;
                foreach (var t in module.View.GetComponentsInChildren<TMPro.TMP_Text>()) maxFont = Mathf.Max(maxFont, t.fontSize);
                Check("floating readable font", maxFont >= 18f, "maxFont=" + maxFont);
                ScreenCapture.CaptureScreenshot(Root + "/shots/floating_" + step + ".png");
            }
            if (step == 13) surface.DockBack("joints");
            if (step == 14)
            {
                Check("dock same instance", module.View.GetInstanceID() == viewId && surface.FloatingCount == floatingBefore, "returned");
                surface.SetTab(TwinUiWorkspaceRegistry.Control, false);
                var r = window.WindowRect; r.width = 1200; window.ApplyLayout(r, false, false);
            }
            if (step == 15) Measure(1201); // distinct filename for control page
            if (step == 16) { var r = window.WindowRect; r.width = 720; window.ApplyLayout(r, false, false); }
            if (step == 17) Measure(721);
            if (step == 18) surface.Detach("authority");
            if (step == 19)
                foreach (var win in UnityEngine.Object.FindObjectsOfType<TwinUiModuleWindow>())
                    if (win.ModuleId == "authority") win.ApplyWindowRect(new Rect(850, 240, 900, 560));
            if (step == 20)
            {
                var authority = surface.Find("authority");
                float maxFont = 0;
                foreach (var t in authority.View.GetComponentsInChildren<TMPro.TMP_Text>()) maxFont = Mathf.Max(maxFont, t.fontSize);
                Check("legacy floating readable", maxFont >= 20 && authority.View.localScale == Vector3.one, "maxFont=" + maxFont);
                ScreenCapture.CaptureScreenshot(Root + "/shots/floating_authority.png");
            }
            if (step == 21) surface.DockBack("authority");
            if (step == 22 || step == 24 || step == 26)
            {
                surface.SetTab(step == 22 ? TwinUiWorkspaceRegistry.Overview : step == 24 ? TwinUiWorkspaceRegistry.Mission : TwinUiWorkspaceRegistry.Experiment, false);
                var r = window.WindowRect; r.width = 1200; window.ApplyLayout(r, false, false);
            }
            if (step == 23 || step == 25 || step == 27) Measure(1202 + (step - 23) / 2);
            if (step >= 28) { Finish(); return; }
            step++;
        }

        static Rect Bounds(RectTransform rt, RectTransform space)
        {
            Vector3[] corners = new Vector3[4]; rt.GetWorldCorners(corners);
            Vector2 lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity), hi = -lo;
            foreach (var corner in corners) { Vector2 p = space.InverseTransformPoint(corner); lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        static void Measure(int requested)
        {
            Canvas.ForceUpdateCanvases();
            var page = surface.Page;
            var parent = (RectTransform)page.Root.parent;
            Rect p = Bounds(page.Root, parent);
            Check("page width", Mathf.Abs(page.Root.rect.width - parent.rect.width) <= 1, "request=" + requested + " page=" + page.Root.rect + " parent=" + parent.rect + " bounds=" + p);
            Check("page origin", Mathf.Abs(p.xMin - parent.rect.xMin) <= 1, p.ToString());
            var header = window.Content.Find("Header") as RectTransform;
            var tabs = window.Content.Find("ShellV2Root/WorkspaceTabs") as RectTransform;
            if (header != null && tabs != null)
            {
                Rect h = Bounds(header, window.Content), t = Bounds(tabs, window.Content);
                Check("header/tabs separate", t.yMax <= h.yMin + 1, "header=" + h + " tabs=" + t);
                foreach (Transform child in header)
                {
                    var rt = child as RectTransform;
                    if (rt == null || !child.gameObject.activeInHierarchy) continue;
                    Rect b = Bounds(rt, header);
                    Check("header child " + child.name, b.xMin >= header.rect.xMin - 1 && b.xMax <= header.rect.xMax + 1, b.ToString());
                }
            }
            var viewport = (RectTransform)parent.parent;
            Rect viewportRect = viewport.rect;
            if (requested == 720)
            {
                var ray = UnityEngine.Object.FindObjectOfType<DigitalTwin.Interaction.TwinPlayRayVisual>();
                Check("ray available", ray != null, "desktop pointer");
                if (ray != null)
                {
                    ray.SetPointerOverride(new Vector2(Screen.width * .85f, Screen.height * .6f)); ray.RefreshNow();
                    var line = ray.transform.Find("TwinPlayRayLine").GetComponent<LineRenderer>();
                    var cam = ray.RayCamera;
                    float depth = cam.transform.InverseTransformPoint(line.GetPosition(0)).z;
                    float pixels = cam.WorldToScreenPoint(line.GetPosition(0) + cam.transform.right * line.startWidth).x
                        - cam.WorldToScreenPoint(line.GetPosition(0)).x;
                    Check("ray begins past near plane", depth >= cam.nearClipPlane + .02f, "depth=" + depth + " near=" + cam.nearClipPlane);
                    Check("ray bounded screen width", pixels <= 4f, "pixels=" + pixels);
                    ray.ClearOverrides();
                }
            }
            foreach (var card in page.Cards)
            {
                if (!card.gameObject.activeInHierarchy) continue;
                Rect b = Bounds(card.Root, viewport);
                float cellWidth = page.Columns == 1 ? page.ContentWidth : (page.ContentWidth - 12f) / 2f;
                var entryModule = surface.Find(card.ModuleId);
                float allocated = entryModule.WideContent && page.Columns == 2 ? page.ContentWidth : cellWidth;
                Check("card fills column " + card.ModuleId, Mathf.Abs(b.width - allocated) <= 1f, "actual=" + b.width + " expected=" + allocated);
                Check("card horizontal " + card.ModuleId, b.xMin >= viewportRect.xMin - 1 && b.xMax <= viewportRect.xMax + 1, "card=" + b + " viewport=" + viewportRect);
                var title = card.Root.Find("CardHeader/Title").GetComponent<TMPro.TMP_Text>();
                title.ForceMeshUpdate();
                Check("title visible " + card.ModuleId, title.textInfo.characterCount > 0 && !title.isTextOverflowing,
                    "text=" + title.text + " chars=" + title.textInfo.characterCount + " overflow=" + title.isTextOverflowing + " rect=" + title.rectTransform.rect + " preferredH=" + title.preferredHeight);
                if (entryModule.View != null && entryModule.View.gameObject.activeInHierarchy)
                    foreach (var text in entryModule.View.GetComponentsInChildren<TMPro.TMP_Text>())
                    {
                        Rect tb = Bounds(text.rectTransform, entryModule.View);
                        Check("body bottom " + card.ModuleId + "/" + text.name,
                            tb.yMin >= entryModule.View.rect.yMin - 1f, "text=" + tb + " body=" + entryModule.View.rect);
                    }
            }
            if (page.Columns == 2)
            {
                TwinUiCardView joints = null, tcp = null;
                foreach (var card in page.Cards) { if (card.ModuleId == "joints") joints = card; if (card.ModuleId == "tcp") tcp = card; }
                if (joints != null && tcp != null)
                    Check("joint/tcp first row", Mathf.Abs(Bounds(joints.Root, viewport).yMax - Bounds(tcp.Root, viewport).yMax) <= 1, "pairing");
            }
            log.AppendLine("screen=" + Screen.width + "x" + Screen.height + " window=" + window.WindowRect + " viewport=" + viewport.rect + " C=" + page.ContentWidth + " cols=" + page.Columns);
            Directory.CreateDirectory(SHOTS);
            // R2 A-4：屏幕级截图，文件名带真实屏幕尺寸（不是档位证据）。
            // CaptureScreenshotAsTexture 在 EditorApplication.update 里返回 null → 用异步 CaptureScreenshot，
            // 文件名带真实尺寸；像素由事后 IHDR 复核（不靠文件名推断）。
            string realName = SessionState.GetString(Armed + ".phase", "run") + "_" + requested
                + "_screen" + Screen.width + "x" + Screen.height + ".png";
            ScreenCapture.CaptureScreenshot(SHOTS + "/" + realName);
        }
        static void Check(string name, bool ok, string detail) { if (!ok) fails++; log.AppendLine((ok ? "PASS " : "FAIL ") + name + " " + detail); }
        static void Finish()
        {
            log.AppendLine("RESULT=" + (fails == 0 ? "PASS" : "FAIL") + " failures=" + fails);
            Directory.CreateDirectory(Root);
            File.WriteAllText(Root + "/" + SessionState.GetString(Armed + ".phase", "run") + ".txt", log.ToString());
            SessionState.SetBool(Armed, false); SessionState.SetBool(Armed + ".restore", true);
            EditorApplication.isPlaying = false;
        }
    }
}
