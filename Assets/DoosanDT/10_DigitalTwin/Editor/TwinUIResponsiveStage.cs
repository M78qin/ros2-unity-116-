// P3 响应式重排编辑器入口：
//   UI/36 Repair Components (anti-embedded-script)  —— 把场景里 m_Script 指向"内嵌 MonoScript"的组件
//        重新指向真正的脚本资产，修根因（P2.5 发现 TwinUIThemeApplier 进 Play 不实例化）。
//   UI/37 Layout Dump (diagnostic)                —— 导出窗口内容层级几何，供响应式断点设计/证据。
//   UI/35 Responsive Layout Check (auto-exit)     —— 三档断点自检（Play 下逐项断言，自动退出）。
//
// 约束：只改组件引用与窗口内容布局（锚点/sizeDelta/anchoredPosition），不重建/不删除 GameObject。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIResponsiveStage
    {
        public const string CanvasPath = "30_界面与交互/UI_二期面板";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-06_p3_responsive";
        public const string EvidenceShots = EvidenceRoot + @"\shots";
        public const string StageLogPath = "Logs/p3_responsive_stage.txt";
        public const string DumpPath = "Logs/p3_layout_dump.txt";
        public const string CheckLogPath = "Logs/ui_responsive_check.txt";
        public const string CheckJsonPath = "Logs/ui_responsive_check.json";
        public const string Tag = "[RESPONSIVE]";
        public const string RepairTag = "[REPAIR]";

        private const string PendingKey = "TwinUIResponsiveStage.Pending";
        private const string ArmedKey = "TwinUIResponsiveStage.ArmedAt";
        private const string PrefsBackupKey = "TwinUIResponsiveStage.PrefsBackup";

        // ============================================================= UI/36

        [MenuItem("DigitalTwin/UI/36 Repair Components (anti-embedded-script)", false, 360)]
        public static void RepairComponents()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Repair Components (UI/36)  " + NowText() + " ===");

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: in play mode; skip");
                sb.AppendLine(RepairTag + " scanned=0 repaired=0 skipped=0 result=FAIL (in play mode)");
                WriteStageLog(sb);
                Debug.LogWarning(RepairTag + " UI/36 skipped: in play mode");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            string hashBefore = HashScene(scene.path);
            int scanned = 0;
            int repaired = 0;
            int skippedMissing = 0;
            int skippedNoAsset = 0;
            var repairedTypes = new Dictionary<string, int>();

            var behaviours = new List<MonoBehaviour>();
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                behaviours.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
            }

            var assetCache = new Dictionary<Type, MonoScript>();
            foreach (var mb in behaviours)
            {
                if (mb == null)
                {
                    skippedMissing++;
                    continue;
                }

                scanned++;
                var script = MonoScript.FromMonoBehaviour(mb);
                if (script != null && AssetDatabase.Contains(script))
                {
                    continue;
                }

                if (script == null)
                {
                    skippedMissing++;
                    continue;
                }

                Type type = mb.GetType();
                MonoScript assetScript;
                if (!assetCache.TryGetValue(type, out assetScript) || assetScript == null)
                {
                    assetScript = FindAssetScript(type);
                    assetCache[type] = assetScript;
                }

                if (assetScript == null)
                {
                    skippedNoAsset++;
                    sb.AppendLine("  noAssetScript class=" + type.FullName + " go=" + PathOf(mb.transform));
                    continue;
                }

                var so = new SerializedObject(mb);
                var scriptProp = so.FindProperty("m_Script");
                if (scriptProp == null)
                {
                    skippedNoAsset++;
                    continue;
                }

                scriptProp.objectReferenceValue = assetScript;
                so.ApplyModifiedPropertiesWithoutUndo();
                repaired++;
                string key = type.Name + " <- " + AssetDatabase.GetAssetPath(assetScript);
                repairedTypes[key] = repairedTypes.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            string hashAfterRepair = HashScene(scene.path);
            bool saved = false;
            bool dirty = scene.isDirty || hashAfterRepair != hashBefore || repaired > 0;
            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            string hashAfter = HashScene(scene.path);
            sb.AppendLine("  sceneBefore=" + hashBefore);
            sb.AppendLine("  sceneAfter =" + hashAfter);
            sb.AppendLine("  sceneDirty(beforeSave)=" + dirty + " saved=" + (saved ? "yes" : "no"));
            foreach (var pair in repairedTypes)
            {
                sb.AppendLine("  repaired " + pair.Value + " x " + pair.Key);
            }

            sb.AppendLine(RepairTag + " scanned=" + scanned + " repaired=" + repaired
                + " skipped=" + (skippedMissing + skippedNoAsset)
                + " skippedMissing=" + skippedMissing
                + " skippedNoAsset=" + skippedNoAsset
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + hashAfter
                + " result=" + (skippedMissing == 0 && repaired > 0 ? "PASS" : (repaired == 0 ? "PASS" : "FAIL")));
            WriteStageLog(sb);
            Debug.Log(RepairTag + " scanned=" + scanned + " repaired=" + repaired
                + " skipped=" + (skippedMissing + skippedNoAsset)
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + hashAfter);
        }

        private static string PathOf(Transform t)
        {
            if (t == null)
            {
                return "null";
            }

            string path = t.name;
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }

        private static MonoScript FindAssetScript(Type type)
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/DoosanDT" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                {
                    return script;
                }
            }

            return null;
        }

        // ============================================================= UI/37

        [MenuItem("DigitalTwin/UI/37 Layout Dump (diagnostic)", false, 370)]
        public static void DumpLayout()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("=== Layout Dump (UI/37)  " + NowText() + " ===");
            sb.AppendLine("scene=" + scene.path + " hash=" + HashScene(scene.path));
            sb.AppendLine("model=anchoredPos/size in local space; rect in local space; world=canvas space");

            GameObject canvasGo = GameObject.Find(CanvasPath);
            if (canvasGo == null)
            {
                sb.AppendLine("FAIL: canvas missing " + CanvasPath);
                WriteDump(sb);
                return;
            }

            var canvasRect = canvasGo.transform as RectTransform;
            sb.AppendLine("canvasRect=" + RectText(canvasRect));
            DumpRect(sb, canvasRect, 0, 4);

            var host = canvasGo.GetComponent<TwinUiWindowHost>();
            sb.AppendLine("host=" + (host != null ? "present" : "missing") + " play=" + Application.isPlaying);
            if (host != null && host.IsReady)
            {
                sb.AppendLine("windowRect=" + host.DescribeRect(host.WindowRect));
            }

            WriteDump(sb);
            Debug.Log("[P3] layout dump written " + DumpPath);
        }

        private static void DumpRect(StringBuilder sb, RectTransform rt, int depth, int maxDepth)
        {
            if (rt == null || depth > maxDepth)
            {
                return;
            }

            string indent = new string(' ', depth * 2);
            var img = rt.GetComponent<Image>();
            var tmp = rt.GetComponent<TextMeshProUGUI>();
            string extra = "";
            if (img != null && img.color.a > 0.01f)
            {
                extra += " image=" + "#" + ColorUtility.ToHtmlStringRGBA(img.color).Substring(0, 6);
            }

            if (tmp != null)
            {
                string t = string.IsNullOrEmpty(tmp.text) ? "<empty>" : tmp.text.Replace("\n", " ");
                if (t.Length > 24)
                {
                    t = t.Substring(0, 24) + "...";
                }

                extra += " tmp=\"" + t + "\" size=" + tmp.fontSize.ToString("F0");
            }

            string comps = "";
            foreach (var c in rt.GetComponents<Component>())
            {
                if (c == null)
                {
                    comps += " MISSING_SCRIPT";
                    continue;
                }

                string name = c.GetType().Name;
                if (name == "RectTransform" || name == "CanvasRenderer")
                {
                    continue;
                }

                comps += " " + name;
            }

            sb.AppendLine(indent + PathName(rt) + " [" + RectText(rt) + "]"
                + " a=(" + Fmt(rt.anchorMin) + ".." + Fmt(rt.anchorMax) + ") p=" + Fmt(rt.pivot)
                + " pos=" + Fmt(rt.anchoredPosition) + " sd=" + Fmt(rt.sizeDelta)
                + " active=" + rt.gameObject.activeSelf + " scale=" + Fmt(rt.localScale)
                + extra + " comps:" + comps);

            for (int i = 0; i < rt.childCount; i++)
            {
                DumpRect(sb, rt.GetChild(i) as RectTransform, depth + 1, maxDepth);
            }
        }

        private static string PathName(RectTransform rt)
        {
            string path = rt.name;
            var p = rt.parent;
            int guard = 0;
            while (p != null && guard++ < 6)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }

        private static string RectText(RectTransform rt)
        {
            if (rt == null)
            {
                return "null";
            }

            Rect r = rt.rect;
            Vector2 ap = rt.anchoredPosition;
            return "rect(" + r.x.ToString("F1") + "," + r.y.ToString("F1") + ","
                + r.width.ToString("F1") + "x" + r.height.ToString("F1") + ")@"
                + ap.x.ToString("F1") + "," + ap.y.ToString("F1");
        }

        private static string Fmt(Vector2 v)
        {
            return v.x.ToString("F1") + "," + v.y.ToString("F1");
        }

        private static string Fmt(Vector3 v)
        {
            return v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2");
        }

        // ============================================================= 通用

        public static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static string HashScene(string scenePath)
        {
            try
            {
                string full = Path.GetFullPath(scenePath);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(full))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                    {
                        sb.Append(b.ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "ERR:" + e.GetType().Name;
            }
        }

        public static void WriteStageLog(StringBuilder sb)
        {
            AppendLine(StageLogPath, sb);
        }

        public static void WriteDump(StringBuilder sb)
        {
            AppendLine(DumpPath, sb);
        }

        private static void AppendLine(string relPath, StringBuilder sb)
        {
            try
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), relPath);
                string dir = Path.GetDirectoryName(full);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(full, sb.ToString() + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P3] log write failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// UI/35 响应式三档自检（Play 下自动跑：1600 / 1300 / 1000 宽，逐项断言，自动退出 Play）。
    /// 断言：包围盒不越出窗口（容差 1px）、MissionRoot 列宽/抽屉规则、TMP 字号不变、
    ///       localScale=1、UI 命中语义不回退、运行期无 Error/Exception。
    /// </summary>
    public static class TwinUIResponsiveCheck
    {
        private const string PendingKey = "TwinUIResponsiveCheck.Pending";
        private const string ArmedKey = "TwinUIResponsiveCheck.ArmedAt";

        private static readonly float[] Widths = { 1600f, 1300f, 1000f };

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly List<RaycastResult> RayHits = new List<RaycastResult>();
        private static TMP_Text[] _fontProbes;
        private static float[] _fontSizes;
        private static TwinUiWindowHost _host;
        private static TwinUiResponsiveLayout _layout;
        private static Rect _initialRect;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static int _logErrors;
        private static int _logExceptions;
        private static float _startAt;
        private static bool _subscribed;

        [MenuItem("DigitalTwin/UI/35 Responsive Layout Check (auto-exit)", false, 350)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[RESPONSIVE] UI/35 skipped: already in play mode");
                return;
            }

            ResetState();
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ArmedKey, DateTime.Now.ToString("R", CultureInfo.InvariantCulture));
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            Debug.Log("[RESPONSIVE] UI/35 armed, entering play mode; log=" + TwinUIResponsiveStage.CheckLogPath);
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
            _startAt = 0f;
            _subscribed = false;
            _host = null;
            _layout = null;
            _fontProbes = null;
            _fontSizes = null;
            _logErrors = 0;
            _logExceptions = 0;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
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

            _host = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            if (_host == null || !_host.IsReady || _host.Responsive == null)
            {
                _nextAt = (float)EditorApplication.timeSinceStartup + 0.25f;
                if (Time.realtimeSinceStartup - _startAt > 20f)
                {
                    AddItem("[0] 宿主/响应式引擎就绪", "host=" + (_host != null)
                        + " ready=" + (_host != null && _host.IsReady)
                        + " responsive=" + (_host != null && _host.Responsive != null), false);
                    Finish();
                }

                return;
            }

            _layout = _host.Responsive;
            _initialRect = _host.WindowRect;
            _fontProbes = new[]
            {
                FindText("Header/Title"),
                FindText("MissionRoot/Detail/Body"),
                FindText("MissionRoot/Detail/Timeline/Title")
            };
            _fontSizes = new float[_fontProbes.Length];
            for (int i = 0; i < _fontProbes.Length; i++)
            {
                _fontSizes[i] = _fontProbes[i] != null ? _fontProbes[i].fontSize : -1f;
            }

            AddItem("[0] 宿主/响应式引擎就绪",
                "host=True ready=True responsive=True content=" + Fmt2(_host.Content.rect.width) + "x"
                + Fmt2(_host.Content.rect.height) + " contentScale=" + _host.ContentScale.x.ToString("F3")
                + " initialRect=" + DescribeRect(_initialRect) + " stepWidths=1600/1300/1000",
                Mathf.Abs(_host.ContentScale.x - 1f) <= 0.001f && Mathf.Abs(_host.Content.localScale.x - 1f) <= 0.001f);
            AddItem("[0b] 3 个 TMP 字号基线（后续三档不得变化）",
                "title=" + _fontSizes[0].ToString("F0") + " body=" + _fontSizes[1].ToString("F0")
                + " timeline=" + _fontSizes[2].ToString("F0"),
                _fontProbes[0] != null && _fontProbes[1] != null && _fontProbes[2] != null);
            _started = true;
            _step = 0;
            _nextAt = (float)EditorApplication.timeSinceStartup + 0.3f;
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0:
                    _host.SetWidthForCheck(Widths[0]);
                    Next(1, 0.55f);
                    return;
                case 1:
                    AssertWidth(Widths[0], "s01_1600");
                    Next(2, 0.6f);
                    return;
                case 2:
                    _host.SetWidthForCheck(Widths[1]);
                    Next(3, 0.55f);
                    return;
                case 3:
                    AssertWidth(Widths[1], "s02_1300");
                    Next(4, 0.6f);
                    return;
                case 4:
                    _host.SetWidthForCheck(Widths[2]);
                    Next(5, 0.55f);
                    return;
                case 5:
                    AssertWidth(Widths[2], "s03_1000");
                    Next(6, 0.6f);
                    return;
                case 6:
                    RestoreAndFinish();
                    return;
            }
        }

        private static void Next(int step, float delaySec)
        {
            _step = step;
            _nextAt = (float)EditorApplication.timeSinceStartup + delaySec;
        }

        // ------------------------------------------------------------- 单档断言

        private static void AssertWidth(float windowWidth, string shotTag)
        {
            float cw = _host.Content.rect.width;
            float ch = _host.Content.rect.height;
            var bp = _layout.Current;
            var expectedBp = TwinUiResponsiveLayout.Classify(cw);

            AddItem("[" + windowWidth.ToString("F0") + "] 断点分类",
                "window=" + Fmt2(windowWidth) + " content=" + Fmt2(cw) + "x" + Fmt2(ch)
                + " bp=" + bp + " expected=" + expectedBp, bp == expectedBp);

            // 列宽 / 抽屉规则
            float stepsW = _layout.StepsRect != null ? _layout.StepsRect.rect.width : -1f;
            float detailW = _layout.DetailRect != null ? _layout.DetailRect.rect.width : -1f;
            float expectSteps = bp == TwinUiBreakpoint.Wide ? TwinUiResponsiveLayout.StepsWide
                : bp == TwinUiBreakpoint.Medium ? TwinUiResponsiveLayout.StepsMedium
                : TwinUiResponsiveLayout.StepsNarrow;
            float expectDetail = bp == TwinUiBreakpoint.Wide
                ? Mathf.Max(TwinUiResponsiveLayout.MinDetailWidth, cw - 420f - 552f - 16f)
                : bp == TwinUiBreakpoint.Medium ? cw - TwinUiResponsiveLayout.StepsMedium - 8f
                : cw - TwinUiResponsiveLayout.StepsNarrow - 8f;
            bool stepsOk = Mathf.Abs(stepsW - expectSteps) <= 1f;
            bool detailOk = Mathf.Abs(detailW - expectDetail) <= 1f;
            AddItem("[" + windowWidth.ToString("F0") + "] MissionRoot 列宽",
                "bp=" + bp + " steps=" + Fmt2(stepsW) + "/" + Fmt2(expectSteps)
                + " detail=" + Fmt2(detailW) + "/" + Fmt2(expectDetail), stepsOk && detailOk);

            bool evalExpectedVisible = bp == TwinUiBreakpoint.Wide;
            bool evalVisible = _layout.EvalRect != null && _layout.EvalRect.gameObject.activeSelf;
            AddItem("[" + windowWidth.ToString("F0") + "] Eval 初始状态（Wide 内联，Medium/Narrow 抽屉默认收起）",
                "evalVisible=" + evalVisible + " expected=" + evalExpectedVisible, evalVisible == evalExpectedVisible);

            bool toggleExpected = bp != TwinUiBreakpoint.Wide;
            bool toggleVisible = _layout.DrawerToggleRect != null && _layout.DrawerToggleRect.gameObject.activeSelf;
            AddItem("[" + windowWidth.ToString("F0") + "] 抽屉开关按钮可见性",
                "toggle=" + toggleVisible + " expected=" + toggleExpected, toggleVisible == toggleExpected);

            // Header 规则
            AssertHeader(bp, windowWidth);

            // 包围盒
            AssertBounds("Header", FindRect("Header"), cw, ch, true);
            AssertBounds("TabBar", FindRect("TabBar"), cw, ch, true);
            AssertBounds("ContextBar", FindRect("ContextBar"), cw, ch, true);
            AssertBounds("Steps(卡片)", FindRect("MissionRoot/Steps"), cw, ch, true);
            AssertBounds("Detail(卡片)", FindRect("MissionRoot/Detail"), cw, ch, true);
            AssertBounds("Eval(卡片)", FindRect("MissionRoot/Eval"), cw, ch, true);
            AssertBounds("Timeline(卡片)", FindRect("MissionRoot/Detail/Timeline"), cw, ch, true);

            // 字号 / 缩放
            AssertFonts();
            AddItem("[" + windowWidth.ToString("F0") + "] localScale=1（无缩放兜底）",
                "content=" + Fmt3(_host.Content.localScale) + " steps=" + Fmt3(_layout.StepsRect.localScale)
                + " detail=" + Fmt3(_layout.DetailRect.localScale)
                + " eval=" + Fmt3(_layout.EvalRect.localScale),
                IsOne(_host.Content.localScale) && IsOne(_layout.StepsRect.localScale)
                && IsOne(_layout.DetailRect.localScale) && IsOne(_layout.EvalRect.localScale));

            // 抽屉开/关（Medium/Narrow）
            if (bp != TwinUiBreakpoint.Wide)
            {
                _layout.SetDrawerOpen(true);
                bool opened = _layout.EvalRect.gameObject.activeSelf;
                bool widthRule;
                if (bp == TwinUiBreakpoint.Medium)
                {
                    float expectedEvalW = Mathf.Min(TwinUiResponsiveLayout.EvalDrawerMaxWidth, detailW);
                    widthRule = Mathf.Abs(_layout.EvalRect.rect.width - expectedEvalW) <= 1f
                        && Mathf.Abs(_layout.EvalRect.anchoredPosition.x + _layout.EvalRect.rect.width - cw) <= 1.5f;
                }
                else
                {
                    widthRule = Mathf.Abs(_layout.EvalRect.rect.width - detailW) <= 1f
                        && Mathf.Abs(_layout.EvalRect.anchoredPosition.x - _layout.DetailRect.anchoredPosition.x) <= 1f;
                }

                AddItem("[" + windowWidth.ToString("F0") + "] Eval 抽屉可打开且符合规则",
                    "open=" + opened + " evalW=" + Fmt2(_layout.EvalRect.rect.width)
                    + " evalX=" + Fmt2(_layout.EvalRect.anchoredPosition.x)
                    + " detailW=" + Fmt2(detailW) + " widthRule=" + widthRule, opened && widthRule);
                AssertBounds("Eval(抽屉打开)", _layout.EvalRect, cw, ch, true);
                CaptureShot(shotTag + "_drawer.png");
                _layout.SetDrawerOpen(false);
                AddItem("[" + windowWidth.ToString("F0") + "] Eval 抽屉可关闭",
                    "active=" + _layout.EvalRect.gameObject.activeSelf, !_layout.EvalRect.gameObject.activeSelf);
            }

            // UI 命中语义
            AssertRaycast(windowWidth, cw, ch);

            CaptureShot(shotTag + ".png");
            AddItem("[" + windowWidth.ToString("F0") + "] 运行期无 Error/Exception",
                "logErrors=" + _logErrors + " logExceptions=" + _logExceptions,
                _logErrors == 0 && _logExceptions == 0);
        }

        private static void AssertHeader(TwinUiBreakpoint bp, float windowWidth)
        {
            string[] all =
            {
                "ModeChip", "KpiSource", "KpiRate", "KpiLatency", "KpiAge", "AlarmBadge", "RecBadge", "EstopChip"
            };
            var active = new List<string>();
            var inactive = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                var rt = FindRect("Header/" + all[i]);
                bool on = rt != null && rt.gameObject.activeSelf;
                if (on)
                {
                    active.Add(all[i]);
                }
                else
                {
                    inactive.Add(all[i]);
                }
            }

            bool titleOn = (FindRect("Header/Title")?.gameObject.activeSelf) ?? false;
            var clock = FindRect("Header/Clock");
            bool clockOn = clock != null && clock.gameObject.activeSelf;
            bool ok;
            string expected;
            switch (bp)
            {
                case TwinUiBreakpoint.Medium:
                    ok = !inactive.Contains("KpiRate") && active.Contains("KpiRate")
                        && active.Contains("KpiLatency") && active.Contains("EstopChip")
                        && active.Contains("AlarmBadge") && !active.Contains("ModeChip")
                        && !active.Contains("KpiAge") && !active.Contains("RecBadge") && titleOn && clockOn;
                    expected = "Title+Rate+Latency+Alarm+Estop+Clock; hide ModeChip/KpiSource/KpiAge/RecBadge";
                    break;
                case TwinUiBreakpoint.Narrow:
                    ok = active.Count == 3 && active.Contains("KpiLatency") && active.Contains("KpiRate")
                        && active.Contains("EstopChip") && titleOn && !clockOn;
                    expected = "Title+Latency+Rate+Estop only (Clock hidden)";
                    break;
                default:
                    ok = active.Count >= 6 && active.Contains("EstopChip") && titleOn;
                    expected = "all chips inline (fit-fold fallback allowed: ModeChip/KpiAge first)";
                    break;
            }

            AddItem("[" + windowWidth.ToString("F0") + "] Header chip 规则",
                "active=[" + string.Join(",", active.ToArray()) + "] inactive=["
                + string.Join(",", inactive.ToArray()) + "] title=" + titleOn + " clock=" + clockOn
                + " expected=" + expected, ok);
        }

        private static void AssertBounds(string label, RectTransform target, float contentWidth, float contentHeight,
            bool mustExist)
        {
            if (target == null)
            {
                AddItem("[bbox] " + label, "missing", !mustExist);
                return;
            }

            if (!target.gameObject.activeInHierarchy)
            {
                AddItem("[bbox] " + label, "inactive(按规则跳过)", true);
                return;
            }

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = _host.Content.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            const float tol = 1f;
            bool ok = minX >= -tol && maxX <= contentWidth + tol
                && minY >= -contentHeight - tol && maxY <= tol;
            AddItem("[bbox] " + label + " 不越出窗口",
                "local=(" + Fmt2(minX) + "," + Fmt2(minY) + ")..(" + Fmt2(maxX) + "," + Fmt2(maxY) + ")"
                + " content=" + Fmt2(contentWidth) + "x" + Fmt2(contentHeight)
                + " overflow=" + (!ok), ok);
        }

        private static void AssertFonts()
        {
            var sizes = new List<string>();
            bool ok = true;
            for (int i = 0; i < _fontProbes.Length; i++)
            {
                var probe = _fontProbes[i];
                if (probe == null)
                {
                    ok = false;
                    sizes.Add("missing");
                    continue;
                }

                bool same = Mathf.Abs(probe.fontSize - _fontSizes[i]) <= 0.01f;
                bool hasSize = probe.rectTransform.rect.width > 4f && probe.rectTransform.rect.height > 4f;
                ok = ok && same && hasSize;
                sizes.Add(probe.name + ":" + probe.fontSize.ToString("F1") + "/" + _fontSizes[i].ToString("F1")
                    + (hasSize ? "" : " ZERO_RECT"));
            }

            AddItem("[font] 3 个 TMP 字号未变且未被裁到 0 尺寸", string.Join(" | ", sizes.ToArray()), ok);
        }

        private static void AssertRaycast(float windowWidth, float contentWidth, float contentHeight)
        {
            if (EventSystem.current == null)
            {
                AddItem("[" + windowWidth.ToString("F0") + "] UI 命中（EventSystem 缺失）", "EventSystem=null", false);
                return;
            }

            bool insideOk = TryUiHit(_host.Content, new Vector2(contentWidth * 0.5f, -contentHeight * 0.5f), out int insideHits);
            bool outsideOk = true;
            int outsideHits = -1;
            Rect wr = _host.WindowRect;
            if (wr.x > 80f)
            {
                float canvasLocalX = wr.x - _host.CanvasSize.x * 0.5f - 60f;
                outsideOk = TryUiHit(_host.CanvasRect, new Vector2(canvasLocalX, 0f), out outsideHits);
                outsideOk = outsideOk && outsideHits == 0;
            }

            AddItem("[" + windowWidth.ToString("F0") + "] UI 命中语义：窗口内 hit>0、窗口外 hit=0（与 UI/17 overUi 一致）",
                "insideHits=" + insideHits + " outsideHits=" + outsideHits + " windowRect=" + DescribeRect(wr),
                insideOk && insideHits > 0 && outsideOk);
        }

        private static bool TryUiHit(RectTransform space, Vector2 local, out int hits)
        {
            hits = -1;
            if (space == null)
            {
                return false;
            }

            Vector3 world = space.TransformPoint(new Vector3(local.x, local.y, 0f));
            Camera cam = _host.Canvas != null ? _host.Canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            var ped = new PointerEventData(EventSystem.current) { position = screen };
            RayHits.Clear();
            EventSystem.current.RaycastAll(ped, RayHits);
            hits = RayHits.Count;
            return true;
        }

        // ------------------------------------------------------------- 收尾

        private static void RestoreAndFinish()
        {
            _host.ApplyLayout(_initialRect, false, false);
            bool restored = Mathf.Abs(_host.WindowRect.width - _initialRect.width) <= 1f
                && Mathf.Abs(_host.WindowRect.height - _initialRect.height) <= 1f;
            AddItem("[restore] 恢复自检前窗口矩形",
                "before=" + DescribeRect(_initialRect) + " now=" + DescribeRect(_host.WindowRect)
                + " bp=" + _layout.Current, restored);
            Finish();
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
            sb.AppendLine("=== Responsive Layout Check (UI/35)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("widths=1600/1300/1000 tolerance=1px contentScale=1");
            foreach (var item in Items)
            {
                sb.AppendLine("[RESPONSIVE] [item] " + item.Label + " | " + item.Detail
                    + " | result=" + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            bool ok = fails == 0 && _logErrors == 0 && _logExceptions == 0;
            sb.AppendLine("[RESPONSIVE] check items=" + Items.Count + " fails=" + fails
                + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " shots=" + Shots.Count + " result=" + (ok ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));

            string logPath = Path.Combine(Directory.GetCurrentDirectory(), TwinUIResponsiveStage.CheckLogPath);
            string dir = Path.GetDirectoryName(logPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(false));

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"items\": [");
            for (int i = 0; i < Items.Count; i++)
            {
                json.Append("    {\"label\": \"").Append(Escape(Items[i].Label)).Append("\", \"ok\": ")
                    .Append(Items[i].Ok ? "true" : "false").Append(", \"detail\": \"")
                    .Append(Escape(Items[i].Detail)).Append("\"}");
                json.AppendLine(i + 1 < Items.Count ? "," : string.Empty);
            }

            json.AppendLine("  ],");
            json.AppendLine("  \"fails\": " + fails + ",");
            json.AppendLine("  \"logErrors\": " + _logErrors + ",");
            json.AppendLine("  \"logExceptions\": " + _logExceptions + ",");
            json.AppendLine("  \"result\": \"" + (ok ? "PASS" : "FAIL") + "\"");
            json.AppendLine("}");
            string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), TwinUIResponsiveStage.CheckJsonPath);
            File.WriteAllText(jsonPath, json.ToString(), new UTF8Encoding(false));
            CopyEvidence(logPath, jsonPath);

            Debug.Log("[RESPONSIVE] UI/35 " + (ok ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails + " log=" + TwinUIResponsiveStage.CheckLogPath);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static void CopyEvidence(string logPath, string jsonPath)
        {
            try
            {
                if (!Directory.Exists(TwinUIResponsiveStage.EvidenceRoot))
                {
                    Directory.CreateDirectory(TwinUIResponsiveStage.EvidenceRoot);
                }

                string logsDir = Path.Combine(TwinUIResponsiveStage.EvidenceRoot, "logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                File.Copy(logPath, Path.Combine(logsDir, "ui35_check.txt"), true);
                File.Copy(jsonPath, Path.Combine(logsDir, "ui35_check.json"), true);
                for (int i = 0; i < Shots.Count; i++)
                {
                    if (File.Exists(Shots[i]))
                    {
                        File.Copy(Shots[i], Path.Combine(TwinUIResponsiveStage.EvidenceShots,
                            Path.GetFileName(Shots[i])), true);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RESPONSIVE] copy evidence failed: " + e.Message);
            }
        }

        private static void CaptureShot(string name)
        {
            try
            {
                if (!Directory.Exists(TwinUIResponsiveStage.EvidenceShots))
                {
                    Directory.CreateDirectory(TwinUIResponsiveStage.EvidenceShots);
                }

                string path = Path.Combine(TwinUIResponsiveStage.EvidenceShots, "ui35_" + name);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RESPONSIVE] screenshot failed: " + e.Message);
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
                Debug.LogWarning("[RESPONSIVE] FAIL " + label + " | " + detail);
            }
        }

        // ------------------------------------------------------------- 查找/格式化

        private static TMP_Text FindText(string path)
        {
            var rt = FindRect(path);
            return rt != null ? rt.GetComponent<TMP_Text>() : null;
        }

        private static RectTransform FindRect(string path)
        {
            return _host != null && _host.Content != null ? _host.Content.Find(path) as RectTransform : null;
        }

        private static bool IsOne(Vector3 v)
        {
            return Mathf.Abs(v.x - 1f) <= 0.001f && Mathf.Abs(v.y - 1f) <= 0.001f && Mathf.Abs(v.z - 1f) <= 0.001f;
        }

        private static string Fmt2(float v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string Fmt3(Vector3 v)
        {
            return v.x.ToString("F3", CultureInfo.InvariantCulture) + ","
                + v.y.ToString("F3", CultureInfo.InvariantCulture) + ","
                + v.z.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string DescribeRect(Rect r)
        {
            return "(" + Fmt2(r.x) + "," + Fmt2(r.y) + "," + Fmt2(r.width) + "x" + Fmt2(r.height) + ")";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }
    }
}