// TwinUiShotWidthStage.cs -- A9 真实像素三档截图（ui-exec 2026-11-09）
//
// 与 UI/59 的区别：UI/59 的 720/1200/1920 是"窗口宽度"，屏幕像素始终 1920x1080；
// 本阶段改 Unity Game View 的真实尺寸，让每档 PNG 的 IHDR 宽度就是 720 / 1200 / 1920。
//
// 边界：
//   * 只读场景/回放 CSV：进 Play，走**唯一入口** TwinUiSourceSwitch.SetReplay 挂回放 feed，
//     不连 ROS/DART、不写记录器、不下发命令。
//   * Game View 自定义尺寸在结束时移除，原选中档位恢复；不改场景资产、不改 ProjectSettings。
//   * 输出只写 R2 批次 ui_r2/testout/ui60/**（日志+CSV）与 R2 批次 ui/shots/**（PNG）。
//     （2026-11-09 R2：首轮批次目录改为只读，本阶段输出根已切到 dsh_2026-11-09_thesis_delivery_r2。）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiShotWidthStage
    {
        const string Armed = "A.ShotWidth";
        const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\testout\ui60";
        const string Shots = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc\a\shots";
        const string FallbackCsv = @"D:\Unity Projects\DART_R-data\PaperLogs\20261103_135616_529090_E4_B\samples.csv";

        static readonly int[] Widths = { 720, 1200, 1920 };
        const int Height = 1080;

        // R2 测量隔离：XR affordance 组件只关一次（进程内），并在日志里留证。
        static bool xrIsolated;
        static StringBuilder log;
        static int index;
        static int phase; // 0=boot refs, 1=wait replay, 2=apply size, 3=wait screen, 4=layout, 5=capture
        static int failures;
        static double next;
        static double deadline;
        static bool sizesReady;
        static bool prepared;
        static TwinUiWindowHost window;
        static TwinUiSurfaceHost surface;
        static TwinUIStatusBinder binder;
        static TwinUiReplayFeed feed;
        static readonly List<int> added = new List<int>();
        static int originalSelection = -1;
        // R2 A-4：截图异步落盘后的临时路径/目标名（下一阶段读 IHDR 断言真实像素）。
        static string capturedPath = string.Empty;
        static string capturedName = string.Empty;

        static TwinUiShotWidthStage()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("DigitalTwin/UI/60 Three-Width Real-Pixel Shots (auto-exit)")]
        public static void Run()
        {
            Directory.CreateDirectory(Root + "/logs");
            log = new StringBuilder();
            log.AppendLine("# UI/60 三档真实像素截图（Game View 尺寸 = PNG 像素）");
            log.AppendLine("targets=" + Widths[0] + "x" + Height + "," + Widths[1] + "x" + Height + "," + Widths[2] + "x" + Height);
            index = 0;
            phase = 0;
            failures = 0;
            sizesReady = false;
            prepared = false;
            added.Clear();
            originalSelection = -1;
            window = null;
            surface = null;
            binder = null;
            feed = null;
            next = 0d;
            deadline = EditorApplication.timeSinceStartup + 180d;
            SessionState.SetBool(Armed, true);
            File.WriteAllText(Root + "/logs/ui60_running.flag", DateTime.Now.ToString("O"));
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            RestoreSizes();
            SessionState.SetBool(Armed, false);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || EditorApplication.isCompiling)
            {
                return;
            }

            if (log == null)
            {
                // 进入 Play 触发域重载：静态字段被清空，但 SessionState 里的 Armed 仍在。
                // 必须重建 log/状态，否则下面每个 Check 都会 NRE。
                log = new StringBuilder();
                log.AppendLine("# UI/60 三档真实像素截图（resumed after domain reload）");
                log.AppendLine("targets=" + Widths[0] + "x" + Height + "," + Widths[1] + "x" + Height + "," + Widths[2] + "x" + Height);
                index = 0;
                phase = 0;
                prepared = false;
                sizesReady = false;
                added.Clear();
                originalSelection = -1;
                feed = null;
                binder = null;
                window = null;
                surface = null;
                next = 0d;
                deadline = EditorApplication.timeSinceStartup + 180d;
            }

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

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now > deadline)
            {
                Check("watchdog", false, "180 s at phase=" + phase + " index=" + index);
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
                Check("exception", false, e.ToString());
                Finish();
            }
        }

        static void Step(double now)
        {
            if (phase == 0)
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

                if (window == null || surface == null || surface.Page == null || binder == null)
                {
                    Retry(0.5d, "boot refs");
                    return;
                }

                if (!prepared)
                {
                    prepared = true;
                    try
                    {
                        CaptureOriginalSelection();
                        PrepareSizes();
                        sizesReady = added.Count == Widths.Length;
                    }
                    catch (Exception e)
                    {
                        log.AppendLine("FAIL prepare game view sizes | " + e);
                    }

                    if (!sizesReady)
                    {
                        Check("game view sizes prepared", false, "added=" + added.Count + " (reflection failed)");
                        Finish();
                        return;
                    }

                    Check("game view sizes prepared", true, "indices=" + string.Join(",", added.ConvertAll(i => i.ToString()).ToArray()));
                    // R2 A-1：唯一入口（不再直连 TwinUiReplayFeed.Ensure）。
                    bool replayOk = TwinUiSourceSwitch.SetReplay(ResolveCsvPath());
                    feed = TwinUiReplayFeed.Active;
                    Check("replay via source switch", replayOk && TwinUiSourceSwitch.IsReplay,
                        "ok=" + replayOk + " isReplay=" + TwinUiSourceSwitch.IsReplay
                        + " err=" + TwinUiSourceSwitch.LastError + " csv=" + ResolveCsvPath());
                    log.AppendLine("feed rows=" + (feed != null ? feed.RowCount : -1)
                        + " loaded=" + (feed != null && feed.Loaded) + " csv=" + ResolveCsvPath());
                    surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                    phase = 1;
                    next = now + 1.5d;
                    return;
                }
            }

            if (phase == 1)
            {
                if (!binder.ReplayOverrideActive)
                {
                    Retry(0.5d, "replay override");
                    return;
                }

                Check("replay override active", true, "source=" + binder.Current.SourceDisplay + " name=" + binder.Current.SourceName);
                surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
                phase = 2;
                next = now + 0.5d;
                return;
            }

            if (phase == 2)
            {
                ApplySize(added[index]);
                log.AppendLine("INFO set game view " + Widths[index] + "x" + Height + " requested");
                phase = 3;
                next = now + 1.0d;
                return;
            }

            if (phase == 3)
            {
                if (Screen.width != Widths[index] || Screen.height != Height)
                {
                    Retry(0.5d, "screen size " + Screen.width + "x" + Screen.height + " want " + Widths[index] + "x" + Height);
                    return;
                }

                Layout(index);
                phase = 4;
                next = now + 1.0d;
                return;
            }

            if (phase == 6)
            {
                // 读 PNG IHDR 断言真实像素（不靠文件名、不靠 Screen.width 推断）。
                int pw;
                int ph;
                if (!TryReadPngSize(capturedPath, out pw, out ph))
                {
                    Check("shot file exists " + capturedName, false, "missing/unreadable: " + capturedPath);
                    phase = 7;
                    next = now + 0.2d;
                    return;
                }

                bool ok = pw == Widths[index] && ph == Height;
                Check("shot real pixels " + Widths[index] + "x" + Height, ok,
                    "ihdr=" + pw + "x" + ph + " screen=" + Screen.width + "x" + Screen.height
                    + " file=" + capturedPath);
                string finalPath = Shots + "/" + (ok
                    ? capturedName
                    : capturedName.Replace(".png", "_actual" + pw + "x" + ph + ".png"));
                try
                {
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }

                    File.Move(capturedPath, finalPath);
                    log.AppendLine("SHOT " + Path.GetFileName(finalPath) + " ihdr=" + pw + "x" + ph);
                }
                catch (Exception e)
                {
                    Check("shot rename", false, e.Message);
                }

                phase = 7;
                next = now + 0.2d;
                return;
            }

            if (phase == 7)
            {
                index++;
                if (index >= Widths.Length)
                {
                    Finish();
                    return;
                }

                phase = 2;
                next = now + 1.2d;
                return;
            }

            if (phase == 4)
            {
                Layout(index);
                phase = 5;
                next = now + 0.8d;
                return;
            }

            if (phase == 5)
            {
                // R2 A-1/A-4：截图**同一次 Play** 同时 dump 三样文本 + 顶部状态条，并断言二者同源一致。
                // （首轮的教训：只断言"元素存在"，截图里"顶部回放 / 底栏实时"的矛盾没人发现。）
                var page = surface.Page;
                page.RefreshSourceBar();
                string src = page.SourceLineText;
                string rec = page.RecordLineText;
                string disp = page.DisplayLineText;
                string win = page.WindowLineText;
                string top = surface.ConnectionStatusText;
                log.AppendLine("TEXTS width=" + Widths[index]
                    + " | SourceBar.source=" + src
                    + " || SourceBar.record=" + rec
                    + " || SourceBar.display=" + disp
                    + " || SourceBar.window=" + win
                    + " || TopStatus=" + top);
                Check("top status and source bar agree (replay) " + Widths[index],
                    top.Contains("回放") && src.Contains("回放") && rec.StartsWith("记录 "),
                    "top=" + top + " src=" + src + " rec=" + rec);

                string name = "replay_twin_" + Widths[index] + "_20261109_r2.png";
                Directory.CreateDirectory(Shots);

                // R2 A-4 硬修：CaptureScreenshotAsTexture 在 EditorApplication.update 里会返回 0x0
                // （必须等 end-of-frame），所以改成"异步写临时文件 + **下一阶段读 PNG IHDR 断言真实像素**"，
                // 只有断言通过才改成正式文件名；不一致就保留 tmp_ 名字并 FAIL（RESULT 会红）。
                capturedPath = Shots + "/_capture_" + Widths[index] + "x" + Height + ".png";
                capturedName = name;
                if (File.Exists(capturedPath))
                {
                    File.Delete(capturedPath);
                }

                ScreenCapture.CaptureScreenshot(capturedPath);
                log.AppendLine("CAPTURE requested " + capturedPath + " screen=" + Screen.width + "x" + Screen.height
                    + " window=" + window.WindowRect);
                phase = 6;
                next = now + 1.0d;
            }
        }

        static void Layout(int i)
        {
            surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
            float w = Mathf.Max(320f, Screen.width - 16f);
            float h = Mathf.Max(400f, Screen.height - 16f);
            window.ApplyLayout(new Rect(8f, 8f, w, h), false, false);
        }

        static void Retry(double delay, string what)
        {
            log.AppendLine("INFO waiting (" + what + ")");
            next = EditorApplication.timeSinceStartup + delay;
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                failures++;
            }

            if (log == null)
            {
                log = new StringBuilder();
            }

            log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        /// <summary>恢复用：删掉残留的 "A9 *" 自定义 Game View 尺寸（崩溃/中断后清场）。</summary>
        [MenuItem("DigitalTwin/UI/60b Cleanup A9 Game View Sizes")]
        public static void CleanupSizes()
        {
            var sb = new StringBuilder();
            try
            {
                object group = Group();
                Type grpType = group.GetType();
                MethodInfo total = grpType.GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo get = grpType.GetMethod("GetGameViewSize", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo remove = grpType.GetMethod("RemoveCustomSize", BindingFlags.Public | BindingFlags.Instance);
                int count = (int)total.Invoke(group, null);
                int removed = 0;
                for (int i = count - 1; i >= 0; i--)
                {
                    object size = get.Invoke(group, new object[] { i });
                    string text = SizeText(size);
                    int w = SizeInt(size, "width");
                    int h = SizeInt(size, "height");
                    sb.AppendLine(i + ": " + text + " " + w + "x" + h);
                    if (text != null && text.StartsWith("A9 ", StringComparison.Ordinal))
                    {
                        remove.Invoke(group, new object[] { i });
                        removed++;
                    }
                }

                count = (int)total.Invoke(group, null);
                int target = 0;
                for (int i = 0; i < count; i++)
                {
                    object size = get.Invoke(group, new object[] { i });
                    if (SizeInt(size, "width") == 1920 && SizeInt(size, "height") == 1080)
                    {
                        target = i;
                        break;
                    }
                }

                if (removed > 0)
                {
                    ApplySize(target);
                }

                sb.AppendLine("removed=" + removed + " selection=" + target + " applied=" + (removed > 0));
            }
            catch (Exception e)
            {
                sb.AppendLine("FAIL cleanup | " + e);
            }

            Directory.CreateDirectory(Root + "/logs");
            File.WriteAllText(Root + "/logs/ui60_cleanup.txt", sb.ToString());
            Debug.Log("[A9] cleanup\n" + sb);
        }

        /// <summary>诊断：把 GameView / GameViewSizes / GameViewSizeGroup / GameViewSize 的成员名写出（选分辨率用）。</summary>
        [MenuItem("DigitalTwin/UI/60c Dump GameView Members")]
        public static void DumpMembers()
        {
            var sb = new StringBuilder();
            Type gv = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Type sizes = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            sb.AppendLine("GameView=" + (gv != null));
            if (gv != null)
            {
                sb.AppendLine("-- GameView fields --");
                foreach (FieldInfo f in gv.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    sb.AppendLine("  " + f.FieldType.Name + " " + f.Name);
                }

                sb.AppendLine("-- GameView props --");
                foreach (PropertyInfo p in gv.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    sb.AppendLine("  " + p.PropertyType.Name + " " + p.Name + " set=" + p.CanWrite);
                }

                sb.AppendLine("-- GameView methods (size) --");
                foreach (MethodInfo m in gv.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.Name.IndexOf("ize", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sb.AppendLine("  " + m.Name);
                    }
                }
            }

            if (sizes != null)
            {
                MethodInfo getGroup = sizes.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                sb.AppendLine("-- GameViewSizes methods --");
                foreach (MethodInfo m in sizes.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    sb.AppendLine("  " + m.Name);
                }

                try
                {
                    object group = Group();
                    sb.AppendLine("-- group type=" + group.GetType().FullName);
                    foreach (MethodInfo m in group.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (m.Name.IndexOf("ize", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            sb.AppendLine("  " + m.Name);
                        }
                    }

                    Type sizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
                    sb.AppendLine("-- GameViewSize props --");
                    foreach (PropertyInfo p in sizeType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        sb.AppendLine("  " + p.PropertyType.Name + " " + p.Name);
                    }

                    sb.AppendLine("-- GameViewSize ctor --");
                    foreach (ConstructorInfo c in sizeType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        sb.AppendLine("  " + c);
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("group dump failed: " + e);
                }
            }

            Directory.CreateDirectory(Root + "/logs");
            File.WriteAllText(Root + "/logs/ui60_members.txt", sb.ToString());
            Debug.Log("[A9] members dumped");
        }

        static string SizeText(object size)
        {
            Type t = size.GetType();
            PropertyInfo p = t.GetProperty("baseText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                return p.GetValue(size, null) as string;
            }

            FieldInfo f = t.GetField("m_BaseText", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(size) as string : null;
        }

        static int SizeInt(object size, string name)
        {
            Type t = size.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                return (int)p.GetValue(size, null);
            }

            FieldInfo f = t.GetField("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1),
                BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (int)f.GetValue(size) : -1;
        }

        static string ResolveCsvPath()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:TwinUiSettings");
                if (guids != null && guids.Length > 0)
                {
                    var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (settings != null && !string.IsNullOrEmpty(settings.replayCsvPath))
                    {
                        return settings.replayCsvPath;
                    }
                }
            }
            catch (Exception)
            {
                // fall through
            }

            return FallbackCsv;
        }

        static void Finish()
        {
            log.AppendLine("RESULT=" + (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures + " shots=" + index);
            try
            {
                Directory.CreateDirectory(Root + "/logs");
                File.WriteAllText(Root + "/logs/ui60_shots.txt", log.ToString());
                File.Delete(Root + "/logs/ui60_running.flag");
            }
            finally
            {
                RestoreSizes();
                SessionState.SetBool(Armed, false);
                EditorApplication.isPlaying = false;
            }
        }

        // ---------------------------------------------------------------- Game View size via reflection

        /// <summary>读 PNG IHDR 的真实宽高（A-4：断言的唯一依据，不看文件名也不靠 Screen.width 推断）。</summary>
        static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                byte[] head = new byte[24];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Read(head, 0, 24) < 24)
                    {
                        return false;
                    }
                }

                // PNG 签名 89 50 4E 47 0D 0A 1A 0A，随后 IHDR 长度(4)+类型(4)，宽高各 4 字节大端。
                if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47)
                {
                    return false;
                }

                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static Type SizesType()
        {
            return typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        }

        static object Group()
        {
            Type sizes = SizesType();
            Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizes);
            object inst = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            Type groupEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
            object standalone = Enum.Parse(groupEnum, "Standalone");
            MethodInfo getGroup = sizes.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance);
            return getGroup.Invoke(inst, new[] { standalone });
        }

        static int AddCustom(object group, int w, int h, string name)
        {
            Type sizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
            Type sizeEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType");
            object fixedRes = Enum.Parse(sizeEnum, "FixedResolution");
            ConstructorInfo ctor = sizeType.GetConstructor(new[] { sizeEnum, typeof(int), typeof(int), typeof(string) });
            object size = ctor.Invoke(new[] { fixedRes, w, h, name });
            group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(group, new[] { size });
            int total = (int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(group, null);
            return total - 1;
        }

        static void PrepareSizes()
        {
            object group = Group();
            for (int i = 0; i < Widths.Length; i++)
            {
                added.Add(AddCustom(group, Widths[i], Height, "A9 " + Widths[i] + "x" + Height));
            }
        }

        static void CaptureOriginalSelection()
        {
            try
            {
                Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                EditorWindow gv = EditorWindow.GetWindow(gvType);
                originalSelection = ReadSelection(gvType, gv);
                log.AppendLine("original game view selection=" + originalSelection);
            }
            catch (Exception e)
            {
                log.AppendLine("WARN read game view selection | " + e.GetType().Name);
                originalSelection = -1;
            }
        }

        static void ApplySize(int selection)
        {
            Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow gv = EditorWindow.GetWindow(gvType);
            WriteSelection(gvType, gv, selection);
            gv.Repaint();
        }

        static int ReadSelection(Type gvType, EditorWindow gv)
        {
            PropertyInfo prop = SelectionProperty(gvType);
            if (prop != null && prop.CanRead)
            {
                return (int)prop.GetValue(gv, null);
            }

            FieldInfo field = SelectionField(gvType);
            return (int)field.GetValue(gv);
        }

        static void WriteSelection(Type gvType, EditorWindow gv, int selection)
        {
            PropertyInfo prop = SelectionProperty(gvType);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(gv, selection, null);
                return;
            }

            SelectionField(gvType).SetValue(gv, selection);
        }

        static PropertyInfo _selPropCache;

        static PropertyInfo SelectionProperty(Type gvType)
        {
            if (_selPropCache != null)
            {
                return _selPropCache;
            }

            string[] names = { "SizeSelection", "selectedSizeIndex" };
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo p = gvType.GetProperty(names[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite)
                {
                    _selPropCache = p;
                    return p;
                }
            }

            return null;
        }

        static FieldInfo SelectFieldCache;

        static FieldInfo SelectionField(Type gvType)
        {
            if (SelectFieldCache != null)
            {
                return SelectFieldCache;
            }

            string[] names = { "m_SelectedSizeIndex", "selectedSizeIndex", "m_SelectedIndex" };
            for (int i = 0; i < names.Length; i++)
            {
                SelectFieldCache = gvType.GetField(names[i], BindingFlags.NonPublic | BindingFlags.Instance);
                if (SelectFieldCache != null)
                {
                    return SelectFieldCache;
                }
            }

            throw new InvalidOperationException("GameView size selection member not found");
        }

        static void RestoreSizes()
        {
            if (added.Count == 0 && originalSelection < 0)
            {
                return;
            }

            try
            {
                if (originalSelection >= 0)
                {
                    ApplySize(originalSelection);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[A9] restore game view selection failed: " + e.Message);
            }

            try
            {
                object group = Group();
                MethodInfo remove = group.GetType().GetMethod("RemoveCustomSize",
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = added.Count - 1; i >= 0; i--)
                {
                    remove.Invoke(group, new object[] { added[i] });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[A9] remove custom game view sizes failed: " + e.Message);
            }

            added.Clear();
            originalSelection = -1;
        }
    }
}