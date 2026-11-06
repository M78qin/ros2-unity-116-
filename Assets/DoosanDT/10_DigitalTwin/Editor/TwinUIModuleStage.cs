// TwinUIModuleStage.cs —— P4 模块框架安装 + 性能模块自检（task-12）
// UI/37 Install Module Framework：把 TwinUiModuleHost 挂到主面板 Canvas 下（幂等，保存场景）。
// UI/38 Performance Module Check (auto-exit)：Play 下验证 Hidden→Docked→Floating→Hidden 三态往返、
//       Docked 不越界、悬浮窗可拖可缩、数据与 PerformanceProfiler 同源、旧 IMGUI 面板随状态切换、totalErrors=0。
// 证据：Logs/ui_module_stage.txt / Logs/ui_module_check.txt(.json) + Evidence 目录副本。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIModuleStage
    {
        private const string CanvasPath = "30_界面与交互/UI_二期面板";
        private const string ScenePath = "Assets/Scenes/Doosan-DT.unity";
        private const string StageLogPath = "Logs/ui_module_stage.txt";
        private const string CheckLogPath = "Logs/ui_module_check.txt";
        private const string CheckJsonPath = "Logs/ui_module_check.json";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p4_modules";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string EvidenceLogs = EvidenceRoot + @"\logs";

        // ------------------------------------------------------------- UI/37 安装

        [MenuItem("DigitalTwin/UI/37 Install Module Framework", false, 370)]
        public static void Install()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning(TwinUiModuleHost.Tag + " UI/37 skipped: play mode");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                Debug.LogError(TwinUiModuleHost.Tag + " UI/37 aborted: active scene=" + scene.path);
                return;
            }

            var canvas = FindByPath(CanvasPath);
            if (canvas == null)
            {
                Debug.LogError(TwinUiModuleHost.Tag + " UI/37 aborted: canvas not found " + CanvasPath);
                return;
            }

            int before = canvas.GetComponents<TwinUiModuleHost>().Length;
            var host = canvas.GetComponent<TwinUiModuleHost>();
            if (host == null)
            {
                host = canvas.AddComponent<TwinUiModuleHost>();
            }

            int after = canvas.GetComponents<TwinUiModuleHost>().Length;
            bool added = before == 0 && after == 1;
            // 组件可能只存在于内存（上一次运行在保存前被守卫拦下），因此"是否需要保存"看 added / 场景脏 /
            // 场景文件里是否已有该脚本 guid（Unity 某些路径下 isDirty 不可靠，用文件内容兜底）。
            string scriptGuid = AssetDatabase.AssetPathToGUID(
                "Assets/DoosanDT/10_DigitalTwin/Scripts/UI/Modules/TwinUiModuleHost.cs");
            bool persisted = !string.IsNullOrEmpty(scriptGuid)
                && File.Exists(ScenePath)
                && File.ReadAllText(ScenePath).Contains(scriptGuid);
            bool needSave = added || scene.isDirty || !persisted;

            // 域重载后 TwinUITheme 静态会重置为默认 Dark；UI/37 同时承担"把静态主题同步到设置资产"的角色
            // （只改静态模式、不写场景；即使无需保存也同步），这样后续只读审计（UI/12）不会因工具链漂移误报。
            TwinUIThemeStage.SyncThemeFromSettings(out string syncDetail);
            if (needSave)
            {
                if (!TwinUIThemeStage.ThemeMatchesSettings(out string themeGuard))
                {
                    Debug.LogError(TwinUiModuleHost.Tag + " install save aborted: " + themeGuard
                        + " sync=" + syncDetail);
                    AppendStage("UI/37 aborted themeGuard=" + themeGuard + " sync=" + syncDetail);
                    return;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
                string hash = FileHash(ScenePath);
                Debug.Log(TwinUiModuleHost.Tag + " install canvas=" + CanvasPath + " attached=" + after
                    + " added=" + added + " saved=" + (saved ? "yes" : "no") + " themeGuard=ok sceneSha256=" + hash
                    + " result=PASS");
                AppendStage("UI/37 Install Module Framework " + NowText());
                AppendStage("  canvas=" + CanvasPath + " componentsBefore=" + before + " componentsAfter=" + after
                    + " added=" + added);
                AppendStage("  saved=" + (saved ? "yes" : "no") + " themeGuard=ok sceneSha256=" + hash
                    + " result=PASS");
            }
            else
            {
                string hash = FileHash(ScenePath);
                Debug.Log(TwinUiModuleHost.Tag + " install canvas=" + CanvasPath + " attached=" + after
                    + " skipped=yes saved=no sceneSha256=" + hash + " result=PASS");
                AppendStage("UI/37 Install Module Framework " + NowText());
                AppendStage("  canvas=" + CanvasPath + " componentsBefore=" + before + " componentsAfter=" + after
                    + " skipped=yes saved=no sceneSha256=" + hash + " result=PASS");
            }
        }

        // ------------------------------------------------------------- 定位工具

        private static GameObject FindByPath(string path)
        {
            string[] parts = path.Split('/');
            GameObject current = null;
            foreach (var scene in SceneManager.GetAllScenes())
            {
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == parts[0])
                    {
                        current = root;
                        break;
                    }
                }

                if (current != null)
                {
                    break;
                }
            }

            if (current == null)
            {
                return null;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                var next = current.transform.Find(parts[i]);
                if (next == null)
                {
                    return null;
                }

                current = next.gameObject;
            }

            return current;
        }

        private static string FileHash(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
                }
            }
            catch (Exception e)
            {
                return "ERR:" + e.Message;
            }
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static void AppendStage(string line)
        {
            try
            {
                Directory.CreateDirectory("Logs");
                File.AppendAllText(StageLogPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(TwinUiModuleHost.Tag + " stage log failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------- UI/38 自检

        [MenuItem("DigitalTwin/UI/38 Performance Module Check (auto-exit)", false, 380)]
        public static void RunCheck()
        {
            TwinUIModuleCheck.Run();
        }
    }

    /// <summary>UI/38：Play 下三态往返 + 数据同源 + 悬浮窗拖缩 + 旧面板接管断言。</summary>
    public static class TwinUIModuleCheck
    {
        private const string PendingKey = "TwinUIModuleCheck.Pending";
        private const string PerfId = TwinUiPerformanceModule.ModuleId;
        private const string LogPath = "Logs/ui_module_check.txt";
        private const string JsonPath = "Logs/ui_module_check.json";
        private const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p4_modules";
        private const string EvidenceShots = EvidenceRoot + @"\shots";
        private const string EvidenceLogs = EvidenceRoot + @"\logs";

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static TwinUiModuleHost _host;
        private static TwinUiPerformanceModule _module;
        private static PerformanceProfiler _profiler;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static bool _subscribed;
        private static int _logErrors;
        private static int _logExceptions;
        private static bool _originalPanelVisible;
        private static int _stateFlipCount;

        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(TwinUiModuleHost.Tag + " UI/38 skipped: already in play mode");
                return;
            }

            ResetState();
            SessionState.SetBool(PendingKey, true);
            Debug.Log(TwinUiModuleHost.Tag + " UI/38 armed, entering play mode; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookUpdate()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void ResetState()
        {
            Items.Clear();
            Shots.Clear();
            _host = null;
            _module = null;
            _profiler = null;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _subscribed = false;
            _logErrors = 0;
            _logExceptions = 0;
            _stateFlipCount = 0;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (!_started)
            {
                Start();
                return;
            }

            if (_finished || EditorApplication.timeSinceStartup < _nextAt)
            {
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                AddItem("[x] 自检异常", e.GetType().Name + ": " + e.Message, false);
                Finish();
            }
        }

        private static void Start()
        {
            if (_startAt <= 0f)
            {
                _startAt = Time.realtimeSinceStartup;
            }

            if (!_subscribed)
            {
                Application.logMessageReceived += OnLog;
                _subscribed = true;
            }

            _host = TwinUiModuleHost.Instance;
            if (_host == null)
            {
                _host = UnityEngine.Object.FindObjectOfType<TwinUiModuleHost>();
            }

            if (_host == null || !_host.IsConfigured)
            {
                _nextAt = (float)EditorApplication.timeSinceStartup + 0.25f;
                if (Time.realtimeSinceStartup - _startAt > 25f)
                {
                    AddItem("[0] 模块宿主就绪", "host=" + (_host != null) + " configured="
                        + (_host != null && _host.IsConfigured), false);
                    Finish();
                }

                return;
            }

            _module = _host.Find(PerfId) as TwinUiPerformanceModule;
            _profiler = UnityEngine.Object.FindObjectOfType<PerformanceProfiler>();
            if (_profiler != null)
            {
                _originalPanelVisible = _profiler.IsOnScreenUIVisible();
            }

            AddItem("[0] 模块宿主/注册表就绪",
                "host=True configured=True modules=" + _host.Modules.Count
                + " performance=" + (_module != null) + " profiler=" + (_profiler != null)
                + " oldPanelVisible=" + _originalPanelVisible
                + " headerEntry=" + (_host.HeaderEntryRect != null),
                _module != null && _profiler != null && _host.HeaderEntryRect != null);
            _started = true;
            _step = 0;
            _nextAt = (float)EditorApplication.timeSinceStartup + 0.35f;
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0:
                    _host.SetState(PerfId, TwinUiModuleState.Hidden);
                    AssertState("初始 Hidden", TwinUiModuleState.Hidden, false, 0);
                    Next(1, 0.55f);
                    return;
                case 1:
                    _host.SetState(PerfId, TwinUiModuleState.Docked);
                    Next(2, 0.65f);
                    return;
                case 2:
                    AssertDocked("紧凑");
                    CaptureShot("ui38_s01_docked_compact.png");
                    Next(3, 0.5f);
                    return;
                case 3:
                    _host.SetExpanded(PerfId, true);
                    Next(4, 0.65f);
                    return;
                case 4:
                    AssertDocked("展开");
                    AssertSameSource();
                    CaptureShot("ui38_s02_docked_expanded.png");
                    Next(5, 0.5f);
                    return;
                case 5:
                    _host.SetState(PerfId, TwinUiModuleState.Floating);
                    Next(6, 0.7f);
                    return;
                case 6:
                    AssertFloating();
                    CaptureShot("ui38_s03_floating.png");
                    Next(7, 0.5f);
                    return;
                case 7:
                    _host.SetState(PerfId, TwinUiModuleState.Hidden);
                    Next(8, 0.55f);
                    return;
                case 8:
                    AssertHidden();
                    Next(9, 0.5f);
                    return;
                case 9:
                    AssertMenu();
                    Next(10, 0.4f);
                    return;
                case 10:
                    RestoreAndFinish();
                    return;
            }
        }

        private static void Next(int step, float delay)
        {
            _step = step;
            _nextAt = (float)EditorApplication.timeSinceStartup + delay;
        }

        // ------------------------------------------------------------- 断言

        private static void AssertState(string label, TwinUiModuleState expected, bool dockActive, int unused)
        {
            AddItem("[" + label + "] 状态=" + _module.State,
                "expected=" + expected + " module=" + _module.State + " dockActive="
                + (_host.DockRoot != null && _host.DockRoot.gameObject.activeSelf), _module.State == expected);
        }

        private static void AssertDocked(string tag)
        {
            var stateNow = _module.State;
            bool dockActive = _host.DockRoot != null && _host.DockRoot.gameObject.activeSelf;
            var dockRect = _host.DockRect;
            Rect contentRect = _host.WindowHost.Content.rect;
            bool inside = dockRect.width > 1f && dockRect.x >= -1f && dockRect.y >= -1f
                && dockRect.xMax <= contentRect.width + 1f && dockRect.yMax <= contentRect.height + 1f;
            AddItem("[" + tag + "] Docked 状态 + dock 条可见",
                "state=" + stateNow + " dockActive=" + dockActive + " dockRect=" + DescribeRect(dockRect),
                stateNow == TwinUiModuleState.Docked && dockActive);
            AddItem("[" + tag + "] dock 不越出主窗内容区（容差 1px）",
                "dock=" + DescribeRect(dockRect) + " content=" + DescribeRect(contentRect)
                + " inside=" + inside, inside);

            var view = _module.View2;
            AddItem("[" + tag + "] 模块视图/数据行存在",
                "view=" + (_module.View != null) + " view2=" + (view != null)
                + " fps=" + (view != null ? view.FpsDisplay : "-")
                + " rows=" + (view != null ? view.MetricRowCount : -1)
                + " expanded=" + _module.IsExpanded,
                _module.View != null && view != null);

            bool oldPanelOff = !_profiler.IsOnScreenUIVisible();
            AddItem("[" + tag + "] 旧 IMGUI 面板被模块接管（showOnScreenUI=false）",
                "profilerShowOnScreenUI=" + _profiler.showOnScreenUI + " panelVisible=" + _profiler.IsOnScreenUIVisible()
                + " expectedOff=True", oldPanelOff);

            if (_module.IsExpanded)
            {
                AddItem("[" + tag + "] 展开态：60 s FPS 折线 + 全指标行",
                    "sparkline=" + (view != null && view.Sparkline != null)
                    + " points=" + (view != null && view.Sparkline != null ? view.Sparkline.PointCount : -1)
                    + " lastValues=" + (view != null && view.Sparkline != null ? view.Sparkline.LastValueCount : -1)
                    + " history=" + _module.FpsHistory.Count
                    + " rows=" + (view != null ? view.MetricRowCount : -1),
                    view != null && view.Sparkline != null && view.MetricRowCount >= 6
                    && view.Sparkline.PointCount > 0);
            }
        }

        private static void AssertSameSource()
        {
            // 视图文本由 module.Refresh 用「同一帧的 module.Snapshot」写入；先核对视图 == 模块快照，
            // 再核对 profiler 的实时值与模块快照同源（FPS 容差 1.0，帧时间波动大只做范围合理性）。
            var moduleSnap = _module.Snapshot;
            var freshSnap = default(PerformanceProfiler.PerfSnapshot);
            bool freshOk = _profiler != null && _profiler.TryGetSnapshot(out freshSnap) && freshSnap.valid;
            float shownFps = ParseFloat(_module.View2 != null ? _module.View2.FpsDisplay : null);
            float shownFrame = ParseFloat(_module.View2 != null ? _module.View2.FrameDisplay : null);
            bool viewMatchesModule = moduleSnap.valid
                && Mathf.Abs(shownFps - moduleSnap.fps) <= 0.25f
                && Mathf.Abs(shownFrame - moduleSnap.frameMs) <= 0.05f;
            bool profilerSameFps = freshOk && Mathf.Abs(freshSnap.fps - moduleSnap.fps) <= 1.0f;
            bool frameSane = moduleSnap.frameMs > 0f && moduleSnap.frameMs < 200f
                && freshSnap.frameMs > 0f && freshSnap.frameMs < 200f;
            AddItem("[data] 视图 FPS/帧 ms 与 PerformanceProfiler 同源（同一刷新帧快照 + profiler 实时抽查）",
                "viewFps=" + shownFps.ToString("F2") + " moduleFps=" + moduleSnap.fps.ToString("F2")
                + " profFps=" + freshSnap.fps.ToString("F2") + " | viewFrame=" + shownFrame.ToString("F2")
                + " moduleFrame=" + moduleSnap.frameMs.ToString("F2") + " profFrame=" + freshSnap.frameMs.ToString("F2")
                + " | viewMatch=" + viewMatchesModule + " profilerFpsMatch=" + profilerSameFps
                + " frameSane=" + frameSane,
                moduleSnap.valid && viewMatchesModule && profilerSameFps && frameSane);
        }

        private static void AssertFloating()
        {
            var window = _host.FloatingWindow;
            bool exists = window != null && window.IsConfigured;
            var wr = exists ? window.WindowRect : Rect.zero;
            float canvasW = _host.WindowHost.CanvasSize.x;
            float canvasH = _host.WindowHost.CanvasSize.y;
            bool inside = exists && wr.x >= -1f && wr.y >= -1f && wr.xMax <= canvasW + 1f
                && wr.yMax <= canvasH + 1f;
            AddItem("[floating] 独立悬浮窗已创建且在画布内",
                "exists=" + exists + " rect=" + DescribeRect(wr) + " canvas=" + canvasW.ToString("F0")
                + "x" + canvasH.ToString("F0") + " inside=" + inside
                + " moduleState=" + _module.State,
                exists && inside && _module.State == TwinUiModuleState.Floating);

            if (!exists)
            {
                return;
            }

            // 拖动测试前把窗口放到画布中部，避免上一次运行持久化的贴边/顶部吸附导致位移被钳制。
            var wr0 = window.WindowRect;
            window.SetRectForCheck(new Rect(240f, 240f, wr0.width, wr0.height));
            var before = window.WindowRect;
            window.MoveBy(new Vector2(60f, 40f));
            var moved = window.WindowRect;
            bool dragOk = Mathf.Abs(moved.x - (before.x + 60f)) <= 1f
                && Mathf.Abs(moved.y - (before.y + 40f)) <= 1f;
            AddItem("[floating] 可拖动（MoveBy 同一条交互路径）",
                "before=" + DescribeRect(before) + " after=" + DescribeRect(moved), dragOk);

            window.ResizeBy(TwinUiWindowEdge.BottomRight, new Vector2(40f, -24f));
            var resized = window.WindowRect;
            bool resizeOk = resized.width >= before.width + 38f && resized.height >= before.height + 22f;
            AddItem("[floating] 可缩放（右下角 23px 手柄同一条 ResizeBy 路径）",
                "before=" + DescribeRect(before) + " after=" + DescribeRect(resized), resizeOk);
            AddItem("[floating] 手柄几何复用 P2（边 11px / 角 23px、圆角 13、Ocean 令牌）",
                "edgeHit=" + TwinUiModuleWindow.EdgeHit + " cornerHit=" + TwinUiModuleWindow.CornerHit
                + " radius=" + TwinUiModuleWindow.Radius
                + " bg=" + Hex(TwinUIStyle.PanelColor) + " border=" + Hex(TwinUIStyle.LineStrongColor),
                TwinUiModuleWindow.EdgeHit == 11f && TwinUiModuleWindow.CornerHit == 23f
                && TwinUiModuleWindow.Radius == 13f);
        }

        private static void AssertHidden()
        {
            bool floatingGone = _host.FloatingWindow == null;
            bool dockOff = _host.DockRoot == null || !_host.DockRoot.gameObject.activeSelf;
            bool viewGone = _module.View == null;
            AddItem("[hidden] 三态回到 Hidden：悬浮窗销毁、dock 隐藏、视图销毁",
                "state=" + _module.State + " floatingGone=" + floatingGone + " dockOff=" + dockOff
                + " viewGone=" + viewGone,
                _module.State == TwinUiModuleState.Hidden && floatingGone && dockOff && viewGone);
            // 以"模块首次 OnAttach 时记录的原始值"为准（自检 Start 的读取可能早于 PerformanceProfiler.Start，存在时序竞争）。
            bool moduleOriginal = _module != null ? _module.OriginalPanelVisible : true;
            bool restored = _profiler == null || _profiler.IsOnScreenUIVisible() == moduleOriginal;
            AddItem("[hidden] 旧 IMGUI 面板恢复用户原值",
                "checkStartOriginal=" + _originalPanelVisible + " moduleOriginal=" + moduleOriginal
                + " now=" + (_profiler != null ? _profiler.IsOnScreenUIVisible() : false),
                restored);
            AddItem("[hidden] 状态变更计数/刷新计数（确认宿主真的在运行）",
                "stateChanges=" + _host.StateChangeCount + " refreshes=" + _host.RefreshCount,
                _host.StateChangeCount >= 3 && _host.RefreshCount > 0);
        }

        private static void AssertMenu()
        {
            bool before = _host.MenuOpen;
            _host.ToggleMenu();
            bool open = _host.MenuOpen;
            _host.CloseMenu();
            bool closed = !_host.MenuOpen;
            AddItem("[menu] Header「模块」入口可开合菜单",
                "before=" + before + " opened=" + open + " closed=" + closed,
                !before && open && closed);
        }

        private static void RestoreAndFinish()
        {
            _host.SetState(PerfId, TwinUiModuleState.Hidden);
            _host.SetExpanded(PerfId, false);
            _host.SavePrefs();
            Finish();
        }

        // ------------------------------------------------------------- 收尾

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
            sb.AppendLine("=== Module Framework / Performance Module Check (UI/38)  " + NowText() + " ===");
            sb.AppendLine("scene=" + ScenePathForLog() + " hash=" + FileHashForLog());
            foreach (var item in Items)
            {
                sb.AppendLine("[MODULE] [item] " + item.Label + " | " + item.Detail
                    + " | result=" + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0;
            sb.AppendLine("[MODULE] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(LogPath, Path.Combine(EvidenceLogs, "ui38_check.txt"), true);

                var json = new StringBuilder();
                json.Append("{\"items\":").Append(Items.Count).Append(",\"fails\":").Append(fails)
                    .Append(",\"logErrors\":").Append(_logErrors)
                    .Append(",\"logExceptions\":").Append(_logExceptions)
                    .Append(",\"shots\":").Append(Shots.Count)
                    .Append(",\"result\":\"").Append(ok ? "PASS" : "FAIL").Append("\"}");
                File.WriteAllText(JsonPath, json.ToString(), new UTF8Encoding(false));
                File.Copy(JsonPath, Path.Combine(EvidenceLogs, "ui38_check.json"), true);

                Directory.CreateDirectory(EvidenceShots);
                for (int i = 0; i < Shots.Count; i++)
                {
                    if (File.Exists(Shots[i]))
                    {
                        string dst = Path.Combine(EvidenceShots, Path.GetFileName(Shots[i]));
                        if (Path.GetFullPath(Shots[i]) != Path.GetFullPath(dst))
                        {
                            File.Copy(Shots[i], dst, true);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(TwinUiModuleHost.Tag + " evidence write failed: " + e.Message);
            }

            Debug.Log("[MODULE] UI/38 " + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails + " log=" + LogPath);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static void CaptureShot(string name)
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
                Debug.LogWarning(TwinUiModuleHost.Tag + " screenshot failed: " + e.Message);
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _logErrors++;
            }
            else if (type == LogType.Exception)
            {
                _logExceptions++;
            }
        }

        private static void AddItem(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            if (!ok)
            {
                Debug.LogWarning("[MODULE] FAIL " + label + " | " + detail);
            }
        }

        // ------------------------------------------------------------- 小工具

        private static float ParseFloat(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : -1f;
        }

        private static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        private static string DescribeRect(Rect r)
        {
            return "(" + r.x.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.y.ToString("F1", CultureInfo.InvariantCulture) + ","
                + r.width.ToString("F1", CultureInfo.InvariantCulture) + "x"
                + r.height.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string ScenePathForLog()
        {
            return SceneManager.GetActiveScene().path;
        }

        private static string FileHashForLog()
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(ScenePathForLog()))
                {
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
                }
            }
            catch (Exception e)
            {
                return "ERR:" + e.Message;
            }
        }
    }
}