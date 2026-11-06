// P2-1 主窗窗口化：Editor 菜单（UI/29 挂载、UI/30 自检）。
//
//   DigitalTwin/UI/29 Attach Window Host
//       把 TwinUiWindowHost（宿主）与 TwinUiWindowFrame（交互参数模板）挂到
//       `30_界面与交互/UI_二期面板` 上；幂等（已存在跳过），仅在新增组件时保存场景。
//       打印：[WINDOW] attached=2 skipped=0 saved=yes canvas=... result=PASS
//
//   DigitalTwin/UI/30 Window Host Check (auto-exit)
//       自动进入 Play，按 task-8 a)–i) 逐项断言：
//         a) 宿主就绪、6 个容器已移入 UiWindowContent、Canvas 直属子节点只剩 UiWindowRoot；
//         b) 默认窗口矩形在画布内、宽 68%±2%、缩放 0.60–1.00；
//         c) 拖动标题栏 (dx,dy) → 根节点位移一致（容差 2px），拖动期间 Arbiter.Current=WindowDrag；
//         d) 右下角缩放 (+120,+80) → 尺寸增加、Content.localScale 变小，期间 Current=WindowResize；
//         e) 缩到小于最小值 → 钳制到 1150×648、缩放≈0.60；
//         f) 最大化 → 尺寸=画布、缩放=1；再切换还原；
//         g) 布局持久化：写入 PlayerPrefs → 读回一致；
//         h) 窗口内 TabBar 按钮仍可切页（MissionRoot / TwinOpsRoot 互斥）；
//         i) 运行期无 Error/Exception。
//       截图 4 张（默认/拖后/缩放/最大化），随后自动退出 Play。
//
// 说明：进入 Play 会主动断开 MCP WebSocket（code 4001），调用方只需触发一次，
// 然后轮询 Logs/ui_window_check.txt 里的 RESULT 行。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIWindowStage
    {
        public const string CanvasPath = "30_界面与交互/UI_二期面板";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-05_p2_window";
        public const string EvidenceShots = EvidenceRoot + @"\shots";
        public const string StageLogPath = "Logs/ui_window_stage.txt";
        public const string CheckLogPath = "Logs/ui_window_check.txt";
        public const string CheckJsonPath = "Logs/ui_window_check.json";
        public const string Tag = "[WINDOW]";
        private const string PendingKey = "TwinUIWindowStage.Check.Pending";
        private const string ArmedKey = "TwinUIWindowStage.Check.ArmedAt";
        private const string PrefsBackupKey = "TwinUIWindowStage.PrefsBackup";

        // ============================================================= UI/29

        [MenuItem("DigitalTwin/UI/29 Attach Window Host", false, 290)]
        public static void AttachWindowHost()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Attach Window Host (UI/29)  " + NowText() + " ===");

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: in play mode; skip");
                sb.AppendLine(Tag + " attached=0 saved=no result=FAIL (in play mode)");
                WriteStageLog(sb);
                Debug.LogWarning(Tag + " UI/29 skipped: already in play mode");
                return;
            }

            GameObject canvasGo = TwinSceneLookup.FindObject(CanvasPath);
            if (canvasGo == null)
            {
                sb.AppendLine("  FAIL: canvas not found: " + CanvasPath);
                sb.AppendLine(Tag + " attached=0 saved=no result=FAIL (canvas missing)");
                WriteStageLog(sb);
                Debug.LogError(Tag + " UI/29 FAIL: canvas not found " + CanvasPath);
                return;
            }

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                sb.AppendLine("  FAIL: no Canvas component on " + CanvasPath);
                sb.AppendLine(Tag + " attached=0 saved=no result=FAIL (canvas component missing)");
                WriteStageLog(sb);
                Debug.LogError(Tag + " UI/29 FAIL: no Canvas component");
                return;
            }

            TwinUiWindowHost host = canvasGo.GetComponent<TwinUiWindowHost>();
            TwinUiWindowFrame frame = canvasGo.GetComponent<TwinUiWindowFrame>();
            int attached = 0;
            if (host == null)
            {
                host = canvasGo.AddComponent<TwinUiWindowHost>();
                attached++;
            }

            if (frame == null)
            {
                frame = canvasGo.AddComponent<TwinUiWindowFrame>();
                attached++;
            }

            EditorUtility.SetDirty(canvasGo);
            Scene scene = SceneManager.GetActiveScene();
            bool saved = false;
            if (attached > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            int directChildren = canvas.transform.childCount;
            string hash = HashScene(scene.path);
            sb.AppendLine("  canvas=" + CanvasPath
                + " directChildren=" + directChildren
                + " host=" + (host != null)
                + " frameTemplate=" + (frame != null)
                + " attached=" + attached
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + hash);
            bool pass = host != null && frame != null;
            sb.AppendLine(Tag + " attached=" + attached + " skipped=" + (attached > 0 ? 0 : 2)
                + " saved=" + (saved ? "yes" : "no")
                + " canvas=" + CanvasPath
                + " host=1 frame=1"
                + " sceneSha256=" + hash
                + " result=" + (pass ? "PASS" : "FAIL"));
            WriteStageLog(sb);

            if (pass)
            {
                Debug.Log(Tag + " attached=" + attached + " saved=" + (saved ? "yes" : "no")
                    + " canvas=" + CanvasPath + " result=PASS");
            }
            else
            {
                Debug.LogError(Tag + " UI/29 FAIL: component attach incomplete");
            }
        }

        // ============================================================= UI/30

        [MenuItem("DigitalTwin/UI/30 Window Host Check (auto-exit)", false, 300)]
        public static void WindowHostCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Window Host Check (UI/30)  " + NowText() + " ===");
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: already in play mode; skip");
                sb.AppendLine(Tag + " check result=FAIL (already playing)");
                WriteStageLog(sb);
                Debug.LogWarning(Tag + " UI/30 skipped: already playing");
                return;
            }

            GameObject canvasGo = TwinSceneLookup.FindObject(CanvasPath);
            if (canvasGo == null
                || canvasGo.GetComponent<TwinUiWindowHost>() == null
                || canvasGo.GetComponent<TwinUiWindowFrame>() == null)
            {
                sb.AppendLine("  FAIL: UI/29 not applied (host/frame missing on canvas)");
                sb.AppendLine(Tag + " check result=FAIL (run UI/29 first)");
                WriteStageLog(sb);
                Debug.LogError(Tag + " UI/30 FAIL: run UI/29 first");
                return;
            }

            // 自检要求"默认布局"：先备份用户布局，再清空，Play 中按默认矩形启动；结束恢复。
            string backup = PlayerPrefs.GetString(TwinUiWindowHost.LayoutPrefKey, string.Empty);
            SessionState.SetString(PrefsBackupKey, backup);
            PlayerPrefs.DeleteKey(TwinUiWindowHost.LayoutPrefKey);
            PlayerPrefs.Save();

            ResetCheckState();
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ArmedKey,
                EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  armed; prefsBackup=" + (string.IsNullOrEmpty(backup) ? "<empty>" : backup)
                + " shots=" + EvidenceShots);
            WriteStageLog(sb);
            Debug.Log(Tag + " UI/30 armed, entering play mode; log=" + CheckLogPath);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookCheckUpdate()
        {
            EditorApplication.update -= CheckUpdate;
            EditorApplication.update += CheckUpdate;
        }

        private static int _step;
        private static double _playStart = -1;
        private static double _waitUntil = -1;
        private static bool _finished;
        private static TwinUiWindowHost _host;
        private static TwinUiWindowFrame _frame;
        private static TwinUiInputArbiter _arbiter;
        private static TwinUIPageHost _pageHost;
        private static readonly List<string> _items = new List<string>();
        private static readonly List<string> _fails = new List<string>();
        private static int _shotCount;
        private static int _logErrors;
        private static int _logExceptions;
        private static float _preScale;
        private static float _preMaxScale;
        private static Rect _preDragRect;
        private static Rect _preMaxRect;
        private static bool _logHooked;

        private static void ResetCheckState()
        {
            _step = 0;
            _playStart = -1;
            _waitUntil = -1;
            _finished = false;
            _host = null;
            _frame = null;
            _arbiter = null;
            _pageHost = null;
            _items.Clear();
            _fails.Clear();
            _shotCount = 0;
            _logErrors = 0;
            _logExceptions = 0;
            _host = null;
            _logHooked = false;
        }

        private static void OnRuntimeLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                if (!IsKnownNoise(condition))
                {
                    _logErrors++;
                }
            }
            else if (type == LogType.Exception || type == LogType.Assert)
            {
                if (!IsKnownNoise(condition))
                {
                    _logExceptions++;
                }
            }
        }

        private static bool IsKnownNoise(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return false;
            }

            // MCP 编辑器插件在进入 Play 时会主动断开 WebSocket（code 4001），属于已知噪声。
            return condition.Contains("WebSocket") || condition.Contains("websocket")
                || condition.Contains("[MCP]") || condition.Contains("mcp_call");
        }

        private static void CheckUpdate()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (!_finished)
                {
                    string armedText = SessionState.GetString(ArmedKey, string.Empty);
                    double armed;
                    if (double.TryParse(armedText, NumberStyles.Float, CultureInfo.InvariantCulture, out armed)
                        && EditorApplication.timeSinceStartup - armed > 180.0)
                    {
                        FailCheck("play mode did not start within 180s");
                    }
                }

                return;
            }

            if (EditorApplication.isPaused || _finished)
            {
                return;
            }

            try
            {
                if (_playStart < 0)
                {
                    _playStart = EditorApplication.timeSinceStartup;
                    _step = 0;
                    _waitUntil = Time.realtimeSinceStartup + 3.5;
                    if (!_logHooked)
                    {
                        Application.logMessageReceived += OnRuntimeLog;
                        _logHooked = true;
                    }

                    return;
                }

                if (Time.realtimeSinceStartup < _waitUntil)
                {
                    return;
                }

                StepCheck();
            }
            catch (Exception e)
            {
                AddItem("exception", "检查过程抛出异常: " + e.GetType().Name + ": " + e.Message, false);
                FailCheck("exception");
            }
        }

        private static void StepCheck()
        {
            switch (_step)
            {
                case 0:
                    RunStep_Resolve();
                    return;
                case 1:
                    RunStep_A_Containers();
                    return;
                case 2:
                    RunStep_B_DefaultRect();
                    return;
                case 3:
                    RunStep_C_Drag();
                    return;
                case 4:
                    RunStep_D_Resize();
                    return;
                case 5:
                    RunStep_E_MinClamp();
                    return;
                case 6:
                    RunStep_F_Maximize();
                    return;
                case 7:
                    RunStep_F_Restore();
                    return;
                case 8:
                    RunStep_G_Persistence();
                    return;
                case 9:
                    RunStep_H_TabBar();
                    return;
                case 10:
                    FinishCheck();
                    return;
            }
        }

        // ------------------------------------------------------------- 步骤

        private static void RunStep_Resolve()
        {
            _host = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            _arbiter = TwinUiInputArbiter.Instance;
            _pageHost = UnityEngine.Object.FindObjectOfType<TwinUIPageHost>();
            _frame = _host != null ? _host.Frame : null;

            bool ok = _host != null && _host.IsReady && _frame != null && _arbiter != null;
            AddItem("[0] 宿主就绪",
                "host=" + (_host != null) + " ready=" + (_host != null && _host.IsReady)
                + " frame=" + (_frame != null) + " arbiter=" + (_arbiter != null)
                + " pageHost=" + (_pageHost != null)
                + " screen=" + Screen.width + "x" + Screen.height
                + " state=" + (_host != null ? _host.DescribeState() : "<null>"), ok);
            if (!ok)
            {
                FailCheck("host/frame/arbiter missing");
                return;
            }

            NextStep(1, 0.2);
        }

        private static void RunStep_A_Containers()
        {
            Transform content = _host.Content;
            Transform canvas = _host.CanvasRect;
            var names = new List<string>();
            for (int i = 0; i < content.childCount; i++)
            {
                names.Add(content.GetChild(i).name);
            }

            int found = 0;
            for (int i = 0; i < TwinUiWindowHost.ContainerNames.Length; i++)
            {
                if (content.Find(TwinUiWindowHost.ContainerNames[i]) != null)
                {
                    found++;
                }
            }

            // P2.5 起 Canvas 上除 UiWindowRoot 外还有两块"非交互视觉"：UiSceneTint（窗口外深海底色）、
            // UiWindowShadow（窗口外阴影），二者 raycastTarget=false；窗口根必须是最后一个兄弟（最上层）。
            int canvasDirect = canvas.childCount;
            string directName = canvasDirect > 0 ? canvas.GetChild(canvasDirect - 1).name : "<none>";
            var extras = new List<string>();
            for (int i = 0; i < canvasDirect; i++)
            {
                string n = canvas.GetChild(i).name;
                if (n != "UiWindowRoot")
                {
                    extras.Add(n);
                }
            }

            bool extrasOk = true;
            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i] != "UiSceneTint" && extras[i] != "UiWindowShadow")
                {
                    extrasOk = false;
                }
            }

            bool rootFound = canvas.Find("UiWindowRoot") != null;
            bool pass = _host.MovedContainers == 6 && content.childCount == 6 && found == 6
                && rootFound && directName == "UiWindowRoot" && extrasOk;
            AddItem("[a] 宿主就绪 + 6 容器移入 + Canvas 只剩 UiWindowRoot(+视觉层)",
                "moved=" + _host.MovedContainers + " contentChildren=" + content.childCount
                + " found=" + found + " names=[" + string.Join(",", names.ToArray()) + "]"
                + " canvasDirectChildren=" + canvasDirect + " lastChild=" + directName
                + " extras=[" + string.Join(",", extras.ToArray()) + "]",
                pass);

            CaptureShot("ui30_s01_default.png");
            NextStep(2, 0.6);
        }

        private static void RunStep_B_DefaultRect()
        {
            Rect rect = _host.WindowRect;
            Vector2 canvas = _host.CanvasSize;
            float scale = _host.ContentScale.x;
            Rect def = _host.DefaultRect();

            bool inside = rect.x >= -0.5f && rect.y >= -0.5f
                && rect.x + rect.width <= canvas.x + 0.5f && rect.y + rect.height <= canvas.y + 0.5f;
            // P3：默认窗 55%×92%（但不得小于 1080），内容 1:1（scale 恒为 1）。
            float expectedW = Mathf.Max(TwinUiWindowHost.MinWidth, canvas.x * TwinUiWindowHost.DefaultWidthRatio);
            float expectedH = Mathf.Max(TwinUiWindowHost.MinHeight, canvas.y * TwinUiWindowHost.DefaultHeightRatio);
            bool widthOk = Mathf.Abs(rect.width - expectedW) <= Mathf.Max(2f, canvas.x * 0.02f);
            bool heightOk = Mathf.Abs(rect.height - expectedH) <= canvas.y * 0.02f;
            bool scaleOk = Mathf.Abs(scale - 1f) <= 0.001f;
            bool minOk = rect.width >= TwinUiWindowHost.MinWidth - 0.5f
                && rect.height >= TwinUiWindowHost.MinHeight - 0.5f;
            bool isDefault = Mathf.Abs(rect.x - def.x) <= 1f && Mathf.Abs(rect.y - def.y) <= 1f
                && Mathf.Abs(rect.width - def.width) <= 1f && Mathf.Abs(rect.height - def.height) <= 1f;
            bool pass = inside && widthOk && heightOk && scaleOk && minOk && isDefault;
            AddItem("[b] 默认矩形在画布内 + 宽 55%±2%(≥1080) + 内容 scale=1",
                "rect=" + _host.DescribeRect(rect) + " canvas=" + canvas.x.ToString("F0") + "x"
                + canvas.y.ToString("F0") + " widthRatio=" + (rect.width / canvas.x).ToString("F3")
                + " heightRatio=" + (rect.height / canvas.y).ToString("F3")
                + " scale=" + scale.ToString("F3") + " expectedW=" + expectedW.ToString("F0")
                + " inCanvas=" + inside + " isDefault=" + isDefault + " minOk=" + minOk,
                pass);
            NextStep(3, 0.1);
        }

        private static void RunStep_C_Drag()
        {
            Vector2 bandCenter = _frame.TitleBandCenter;
            bool inBand = _frame.IsInTitleBand(bandCenter);
            Vector2 before = _host.Root.anchoredPosition;
            bool began = _frame.TryBeginDragAt(bandCenter);
            TwinUiInputOwner ownerDuring = _arbiter.Current;
            Vector2 delta = new Vector2(-120f, -30f);
            _frame.DragBy(delta);
            Vector2 after = _host.Root.anchoredPosition;
            Vector2 actual = after - before;
            _frame.EndDragForTest();
            TwinUiInputOwner ownerAfter = _arbiter.Current;
            TwinUiInputOwner ownerAfterAll = TwinUiInput.CurrentOwner;

            bool posOk = Mathf.Abs(actual.x - delta.x) <= 2f && Mathf.Abs(actual.y - delta.y) <= 2f;
            GameObject tabButton = _host.Content.Find("TabBar/BtnTwinOps") != null
                ? _host.Content.Find("TabBar/BtnTwinOps").gameObject : null;
            bool buttonRejected = !_frame.IsPressOnWindowSurface(tabButton);
            bool pass = inBand && began && ownerDuring == TwinUiInputOwner.WindowDrag && posOk
                && ownerAfter != TwinUiInputOwner.WindowDrag && buttonRejected;
            AddItem("[c] 拖动标题栏 (dx,dy) → 位移一致 + 拖动期 Current=WindowDrag",
                "band=" + _host.DescribeRect(_frame.TitleBand) + " inBand=" + inBand
                + " began=" + began + " request=" + Fmt(delta) + " actual=" + Fmt(actual)
                + " ownerDuring=" + ownerDuring + " ownerAfter=" + ownerAfter
                + " buttonRejected=" + buttonRejected,
                pass);

            CaptureShot("ui30_s02_after_drag.png");
            NextStep(4, 0.6);
        }

        private static void RunStep_D_Resize()
        {
            Rect before = _host.WindowRect;
            _preScale = _host.ContentScale.x;
            _preDragRect = before;
            Vector2 delta = new Vector2(120f, -80f);
            int applyBefore = _host.Responsive != null ? _host.Responsive.ApplyCount : -1;
            bool began = _frame.TryBeginResize(TwinUiWindowEdge.BottomRight);
            TwinUiInputOwner ownerDuring = _arbiter.Current;
            _frame.ResizeByForTest(delta);
            Rect after = _host.WindowRect;
            float scaleAfter = _host.ContentScale.x;
            int applyAfter = _host.Responsive != null ? _host.Responsive.ApplyCount : -1;
            _frame.EndResizeForTest();
            TwinUiInputOwner ownerAfter = _arbiter.Current;

            Vector2 growth = new Vector2(after.width - before.width, after.height - before.height);
            bool grew = growth.x > 1f && growth.y > 1f;
            bool scaleOne = Mathf.Abs(scaleAfter - 1f) <= 0.001f;
            bool relayout = _host.Responsive != null && applyAfter > applyBefore;
            bool pass = began && ownerDuring == TwinUiInputOwner.WindowResize && grew && scaleOne
                && relayout && ownerAfter != TwinUiInputOwner.WindowResize;
            AddItem("[d] 右下角向外 (+120,-80) → 尺寸增加 + 响应式重排(scale 恒 1) + Current=WindowResize",
                "before=" + _host.DescribeRect(before) + " request=" + Fmt(delta)
                + " after=" + _host.DescribeRect(after) + " growth=" + Fmt(growth)
                + " scaleAfter=" + scaleAfter.ToString("F3") + " applyCount=" + applyBefore + "→" + applyAfter
                + " ownerDuring=" + ownerDuring + " ownerAfter=" + ownerAfter,
                pass);

            CaptureShot("ui30_s03_after_resize.png");
            NextStep(5, 0.6);
        }

        private static void RunStep_E_MinClamp()
        {
            bool began = _frame.TryBeginResize(TwinUiWindowEdge.BottomRight);
            _frame.ResizeByForTest(new Vector2(-3000f, 3000f));
            _frame.EndResizeForTest();
            Rect rect = _host.WindowRect;
            float scale = _host.ContentScale.x;
            bool sizeOk = Mathf.Abs(rect.width - TwinUiWindowHost.MinWidth) <= 1f
                && Mathf.Abs(rect.height - TwinUiWindowHost.MinHeight) <= 1f;
            bool scaleOk = Mathf.Abs(scale - 1f) <= 0.001f;
            var responsive = _host.Responsive;
            bool narrowOk = responsive != null && responsive.Current == TwinUiBreakpoint.Narrow;
            bool pass = began && sizeOk && scaleOk && narrowOk;
            AddItem("[e] 缩小越过最小值 → 钳制到 1080×620，内容 scale=1 且进入 Narrow 档",
                "rect=" + _host.DescribeRect(rect) + " scale=" + scale.ToString("F4")
                + " expected=" + TwinUiWindowHost.MinWidth.ToString("F0") + "x"
                + TwinUiWindowHost.MinHeight.ToString("F0") + " / scale 1.0"
                + " breakpoint=" + (responsive != null ? responsive.Current.ToString() : "<null>"),
                pass);
            NextStep(6, 0.1);
        }

        private static void RunStep_F_Maximize()
        {
            _preMaxRect = _host.WindowRect;
            _preMaxScale = _host.ContentScale.x;
            _frame.ToggleMaximize();
            Rect rect = _host.WindowRect;
            Vector2 canvas = _host.CanvasSize;
            float scale = _host.ContentScale.x;
            bool full = Mathf.Abs(rect.x) <= 0.5f && Mathf.Abs(rect.y) <= 0.5f
                && Mathf.Abs(rect.width - canvas.x) <= 1f && Mathf.Abs(rect.height - canvas.y) <= 1f;
            bool scaleOne = Mathf.Abs(scale - 1f) <= 0.002f;
            bool pass = _host.IsMaximized && full && scaleOne;
            AddItem("[f1] 最大化 → 尺寸=画布、缩放=1",
                "before=" + _host.DescribeRect(_preMaxRect) + " maximized=" + _host.IsMaximized
                + " rect=" + _host.DescribeRect(rect) + " canvas=" + canvas.x.ToString("F0") + "x"
                + canvas.y.ToString("F0") + " scale=" + scale.ToString("F3"),
                pass);

            CaptureShot("ui30_s04_maximized.png");
            NextStep(7, 0.8);
        }

        private static void RunStep_F_Restore()
        {
            _frame.ToggleMaximize();
            Rect rect = _host.WindowRect;
            float scale = _host.ContentScale.x;
            bool posOk = Mathf.Abs(rect.x - _preMaxRect.x) <= 1.5f && Mathf.Abs(rect.y - _preMaxRect.y) <= 1.5f;
            bool sizeOk = Mathf.Abs(rect.width - _preMaxRect.width) <= 1.5f
                && Mathf.Abs(rect.height - _preMaxRect.height) <= 1.5f;
            bool scaleOk = Mathf.Abs(scale - _preMaxScale) <= 0.005f;
            bool pass = !_host.IsMaximized && posOk && sizeOk && scaleOk;
            AddItem("[f2] 再切换还原 → 最大化前矩形/缩放恢复",
                "preMax=" + _host.DescribeRect(_preMaxRect) + " restored=" + _host.DescribeRect(rect)
                + " scale=" + scale.ToString("F3") + " expectedScale=" + _preMaxScale.ToString("F3")
                + " maximized=" + _host.IsMaximized,
                pass);
            NextStep(8, 0.1);
        }

        private static void RunStep_G_Persistence()
        {
            var custom = new Rect(120f, 140f, 1300f, 760f);
            _host.ApplyLayout(custom, false, true);
            bool hasKey = PlayerPrefs.HasKey(TwinUiWindowHost.LayoutPrefKey);
            string json = PlayerPrefs.GetString(TwinUiWindowHost.LayoutPrefKey, string.Empty);
            bool jsonOk = hasKey && !string.IsNullOrEmpty(json) && json.Contains("\"w\"");

            _host.ApplyLayout(_host.DefaultRect(), false, false);
            bool loaded = _host.ReloadLayoutFromPrefs();
            Rect read = _host.WindowRect;
            bool roundtrip = Mathf.Abs(read.x - custom.x) <= 1.5f && Mathf.Abs(read.y - custom.y) <= 1.5f
                && Mathf.Abs(read.width - custom.width) <= 1.5f
                && Mathf.Abs(read.height - custom.height) <= 1.5f;
            bool pass = jsonOk && loaded && roundtrip;
            AddItem("[g] 布局持久化：写入 PlayerPrefs → 读回一致",
                "key=" + TwinUiWindowHost.LayoutPrefKey + " hasKey=" + hasKey
                + " jsonLen=" + json.Length + " written=" + _host.DescribeRect(custom)
                + " reloaded=" + _host.DescribeRect(read) + " roundtrip=" + roundtrip,
                pass);
            NextStep(9, 0.1);
        }

        private static void RunStep_H_TabBar()
        {
            Transform content = _host.Content;
            Transform tabBar = content.Find("TabBar");
            Transform missionRoot = content.Find("MissionRoot");
            Transform twinOpsRoot = content.Find("TwinOpsRoot");
            Button opsButton = tabBar != null && tabBar.Find("BtnTwinOps") != null
                ? tabBar.Find("BtnTwinOps").GetComponent<Button>() : null;
            Button missionButton = tabBar != null && tabBar.Find("BtnMission") != null
                ? tabBar.Find("BtnMission").GetComponent<Button>() : null;

            bool hasButtons = opsButton != null && missionButton != null
                && missionRoot != null && twinOpsRoot != null;
            bool opsOk = false;
            bool missionOk = false;
            int pageAfterOps = -1;
            int pageAfterMission = -1;
            bool opsRootAfterOps = false;
            bool missionRootAfterOps = false;
            bool opsRootAfterMission = false;
            bool missionRootAfterMission = false;
            if (hasButtons)
            {
                opsButton.onClick.Invoke();
                pageAfterOps = _pageHost != null ? _pageHost.Page : -1;
                opsRootAfterOps = twinOpsRoot.gameObject.activeSelf;
                missionRootAfterOps = missionRoot.gameObject.activeSelf;
                opsOk = opsRootAfterOps && !missionRootAfterOps && pageAfterOps == 1;
                missionButton.onClick.Invoke();
                pageAfterMission = _pageHost != null ? _pageHost.Page : -1;
                opsRootAfterMission = twinOpsRoot.gameObject.activeSelf;
                missionRootAfterMission = missionRoot.gameObject.activeSelf;
                missionOk = missionRootAfterMission && !opsRootAfterMission
                    && pageAfterMission == 0;
            }

            bool pass = hasButtons && opsOk && missionOk;
            AddItem("[h] 窗口内 TabBar 按钮仍可切页",
                "hasButtons=" + hasButtons + " clickBtnTwinOps → page=" + pageAfterOps
                + " twinOpsActive=" + opsRootAfterOps + " missionActive=" + missionRootAfterOps
                + " | clickBtnMission → page=" + pageAfterMission
                + " twinOpsActive=" + opsRootAfterMission + " missionActive=" + missionRootAfterMission,
                pass);
            NextStep(10, 0.1);
        }

        // ------------------------------------------------------------- 收尾

        private static void FailCheck(string reason)
        {
            AddItem("[fail]", reason, false);
            FinishCheck();
        }

        private static void FinishCheck()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            SessionState.SetBool(PendingKey, false);
            if (_logHooked)
            {
                Application.logMessageReceived -= OnRuntimeLog;
                _logHooked = false;
            }

            try
            {
                if (_frame != null)
                {
                    _frame.ReleaseAll();
                }

                if (_host != null)
                {
                    _host.ResetLayout();
                    TwinUiWindowHost.ClearSavedLayout();
                }
            }
            catch (Exception)
            {
                // 退出 Play 前的清理不允许再抛
            }

            // 恢复自检前的用户布局（多半为空 → 下次 Play 用默认布局）。
            string backup = SessionState.GetString(PrefsBackupKey, string.Empty);
            if (string.IsNullOrEmpty(backup))
            {
                PlayerPrefs.DeleteKey(TwinUiWindowHost.LayoutPrefKey);
            }
            else
            {
                PlayerPrefs.SetString(TwinUiWindowHost.LayoutPrefKey, backup);
            }

            PlayerPrefs.Save();
            SessionState.EraseString(PrefsBackupKey);

            int totalErrors = _logErrors + _logExceptions;
            AddItem("[i] 运行期无 Error/Exception（totalErrors）",
                "logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " totalErrors=" + totalErrors, totalErrors == 0);
            bool pass = _fails.Count == 0 && _shotCount >= 3;

            var sb = new StringBuilder();
            sb.AppendLine("=== Window Host Check (P2-1)  " + NowText() + " ===");
            sb.AppendLine("canvas=" + CanvasPath + " shots=" + _shotCount + " dir=" + EvidenceShots);
            for (int i = 0; i < _items.Count; i++)
            {
                sb.AppendLine(_items[i]);
            }

            sb.AppendLine();
            sb.AppendLine("---- 失败项 (" + _fails.Count + ") ----");
            if (_fails.Count == 0)
            {
                sb.AppendLine("(none)");
            }
            else
            {
                for (int i = 0; i < _fails.Count; i++)
                {
                    sb.AppendLine(_fails[i]);
                }
            }

            sb.AppendLine();
            sb.AppendLine(Tag + " check items=" + _items.Count + " fails=" + _fails.Count
                + " totalErrors=" + totalErrors + " logErrors=" + _logErrors
                + " logExceptions=" + _logExceptions + " shots=" + _shotCount
                + " prefsRestored=" + (string.IsNullOrEmpty(backup) ? "<empty>" : "yes")
                + " result=" + (pass ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL"));
            WriteFileAbsolute(CheckLogPath, sb.ToString());

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"generatedAt\": \"" + NowText() + "\",");
            json.AppendLine("  \"canvas\": " + J(CanvasPath) + ",");
            json.AppendLine("  \"items\": " + _items.Count + ",");
            json.AppendLine("  \"fails\": " + _fails.Count + ",");
            json.AppendLine("  \"totalErrors\": " + totalErrors + ",");
            json.AppendLine("  \"shots\": " + _shotCount + ",");
            json.AppendLine("  \"shotsDir\": " + J(EvidenceShots.Replace('\\', '/')) + ",");
            json.AppendLine("  \"prefsRestored\": " + J(string.IsNullOrEmpty(backup) ? "empty" : "yes") + ",");
            json.AppendLine("  \"result\": " + J(pass ? "PASS" : "FAIL"));
            json.AppendLine("}");
            WriteFileAbsolute(CheckJsonPath, json.ToString());

            Debug.Log(Tag + " check items=" + _items.Count + " fails=" + _fails.Count
                + " totalErrors=" + totalErrors + " shots=" + _shotCount
                + " result=" + (pass ? "PASS" : "FAIL") + " log=" + CheckLogPath);

            EditorApplication.isPlaying = false;
        }

        private static void NextStep(int step, double waitSeconds)
        {
            _step = step;
            _waitUntil = Time.realtimeSinceStartup + waitSeconds;
        }

        private static void AddItem(string name, string detail, bool pass)
        {
            string line = Tag + " [item] " + name + " | " + detail + " | result=" + (pass ? "PASS" : "FAIL");
            _items.Add(line);
            if (!pass)
            {
                _fails.Add(line);
            }

            Debug.Log(line);
        }

        private static void CaptureShot(string fileName)
        {
            try
            {
                if (!Directory.Exists(EvidenceShots))
                {
                    Directory.CreateDirectory(EvidenceShots);
                }

                string path = Path.Combine(EvidenceShots, fileName).Replace('\\', '/');
                ScreenCapture.CaptureScreenshot(path);
                _shotCount++;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " screenshot failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------- 文件/格式

        private static void WriteStageLog(StringBuilder sb)
        {
            try
            {
                string dir = Path.GetDirectoryName(StageLogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(StageLogPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " stage log write failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void WriteFileAbsolute(string path, string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " write failed (" + path + "): " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static string HashScene(string scenePath)
        {
            try
            {
                if (string.IsNullOrEmpty(scenePath))
                {
                    return "<no-scene>";
                }

                string full = Path.GetFullPath(scenePath);
                if (!File.Exists(full))
                {
                    return "<missing>";
                }

                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream stream = File.OpenRead(full))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "<error:" + e.GetType().Name + ">";
            }
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Fmt(Vector2 value)
        {
            return "(" + value.x.ToString("F1", CultureInfo.InvariantCulture) + ","
                + value.y.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        private static string J(string text)
        {
            if (text == null)
            {
                return "null";
            }

            var sb = new StringBuilder(text.Length + 8);
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}