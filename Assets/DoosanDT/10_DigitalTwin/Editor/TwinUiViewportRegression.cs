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
    public static class TwinUiViewportRegression
    {
        const string Armed = "Codex.UiViewportRegression";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui55";
        static readonly string[] Keys = { "TwinUI.Shell.v2", "TwinUI.Window.Layout.v1", "TwinUI.Modules.v1" };
        static readonly int[] Widths = { 720, 900, 1200, 1600 };
        static StringBuilder log;
        static int step, fails;
        static double next, deadline;
        static TwinUiWindowHost window;
        static TwinUiShellV2 shell;
        static TwinUiSurfaceHost surface;
        static int viewId, floatingBefore;
        static TwinUiViewportRegression() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Restore; }

        [MenuItem("DigitalTwin/UI/55 Viewport Integration Regression (auto-exit)")]
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
            if (window == null || shell == null || surface == null || surface.Page == null) return;
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
            if (step >= 18) { Finish(); return; }
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
            foreach (var card in page.Cards)
            {
                if (!card.gameObject.activeInHierarchy) continue;
                Rect b = Bounds(card.Root, viewport);
                Check("card horizontal " + card.ModuleId, b.xMin >= viewportRect.xMin - 1 && b.xMax <= viewportRect.xMax + 1, "card=" + b + " viewport=" + viewportRect);
                var title = card.Root.Find("CardHeader/Title").GetComponent<TMPro.TMP_Text>();
                title.ForceMeshUpdate();
                Check("title visible " + card.ModuleId, title.textInfo.characterCount > 0 && !title.isTextOverflowing,
                    "text=" + title.text + " chars=" + title.textInfo.characterCount + " overflow=" + title.isTextOverflowing + " rect=" + title.rectTransform.rect + " preferredH=" + title.preferredHeight);
            }
            if (page.Columns == 2)
            {
                TwinUiCardView joints = null, tcp = null;
                foreach (var card in page.Cards) { if (card.ModuleId == "joints") joints = card; if (card.ModuleId == "tcp") tcp = card; }
                if (joints != null && tcp != null)
                    Check("joint/tcp first row", Mathf.Abs(Bounds(joints.Root, viewport).yMax - Bounds(tcp.Root, viewport).yMax) <= 1, "pairing");
            }
            log.AppendLine("screen=" + Screen.width + "x" + Screen.height + " window=" + window.WindowRect + " viewport=" + viewport.rect + " C=" + page.ContentWidth + " cols=" + page.Columns);
            Directory.CreateDirectory(Root + "/shots");
            ScreenCapture.CaptureScreenshot(Root + "/shots/" + SessionState.GetString(Armed + ".phase", "run") + "_" + requested + ".png");
        }
        static void Check(string name, bool ok, string detail) { if (!ok) fails++; log.AppendLine((ok ? "PASS " : "FAIL ") + name + " " + detail); }
        static void Finish()
        {
            log.AppendLine("RESULT=" + (fails == 0 ? "PASS" : "FAIL") + " failures=" + fails);
            Directory.CreateDirectory(Root + "/logs");
            File.WriteAllText(Root + "/logs/" + SessionState.GetString(Armed + ".phase", "run") + ".txt", log.ToString());
            SessionState.SetBool(Armed, false); SessionState.SetBool(Armed + ".restore", true);
            EditorApplication.isPlaying = false;
        }
    }
}
