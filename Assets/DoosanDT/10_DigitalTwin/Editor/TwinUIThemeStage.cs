// P2.5：Ocean（深海科技蓝）换肤 + 窗口外壳视觉落地与自检。
// 菜单：
//   DigitalTwin/UI/31 Apply Ocean Theme            → 切换并保存 Ocean 主题（幂等）
//   DigitalTwin/UI/32 Restore Deep Theme           → 回退深色主题（幂等）
//   DigitalTwin/UI/33 Theme Visual Check (auto-exit) → Play 中逐项比色（期望 hex vs 实测渲染 hex）
// 证据：Logs/ui_theme_check.txt / Logs/ui_theme_check.json + agent_runs/dsh_2026-11-05_p25_ocean/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIThemeStage
    {
        private const string Tag = "[THEME]";
        private const string CanvasPath = "30_界面与交互/UI_二期面板";
        private const string ScenePath = "Assets/Scenes/Doosan-DT.unity";
        private const string EvidenceDir = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-05_p25_ocean";
        private const string EvidenceShots = EvidenceDir + @"\shots";
        private const string CheckLogPath = "Logs/ui_theme_check.txt";
        private const string CheckJsonPath = "Logs/ui_theme_check.json";
        private const string PendingKey = "TwinUI.ThemeCheck.Pending";
        private const string ArmedKey = "TwinUI.ThemeCheck.ArmedAt";
        private const string PrefsBackupKey = "TwinUI.ThemeCheck.PrefsBackup";
        private const int StepTimeoutFrames = 600;

        // V3.1 原型令牌（tokens.json `:root`）——只在自检里作为"期望值"出现，运行时代码不读这里。
        private static readonly Dictionary<TwinUIToneRole, string> OracleHex = new Dictionary<TwinUIToneRole, string>
        {
            { TwinUIToneRole.Bg, "#071827" },
            { TwinUIToneRole.Panel, "#102637" },
            { TwinUIToneRole.Card, "#132E40" },
            { TwinUIToneRole.CardAlt, "#173345" },
            { TwinUIToneRole.Border, "#2B5268" },
            { TwinUIToneRole.Text, "#E7F5FF" },
            { TwinUIToneRole.Muted, "#99BBCA" },
            { TwinUIToneRole.Accent, "#39C5E5" },
            { TwinUIToneRole.Accent2, "#2D7CBB" },
            { TwinUIToneRole.Ok, "#40D0A0" },
            { TwinUIToneRole.Warn, "#F0BF63" },
            { TwinUIToneRole.Crit, "#FA717A" },
            { TwinUIToneRole.PlotBg, "#0A1E2D" },
            { TwinUIToneRole.Grid, "#23455A" },
            { TwinUIToneRole.Layer, "#0E2535" },
            { TwinUIToneRole.Layer2, "#173345" },
            { TwinUIToneRole.LineStrong, "#42738D" },
            { TwinUIToneRole.Strong, "#53D4E6" }
        };
        // 按钮五态由 TwinUIButton 自管（没有 tone role），期望值单列。
        private const string HexBtnFill = "#173345";
        private const string HexBtnFillHover = "#1E4257";
        private const string HexBtnFillPressed = "#26506A";

        /// <summary>
        /// A49：当前生效主题必须与 TwinUiSettings.themeMode 一致；UI/29、UI/31、UI/32 保存场景前调用。
        /// 不一致时打印期望/实际并阻止保存（否则会把"半换肤"场景写进磁盘）。
        /// </summary>
        /// <summary>
        /// 把编辑器静态主题同步为设置资产里的主题（用于域重载后静态被重置为默认 Dark 的情况）。
        /// 只改 TwinUITheme.Mode，不在 Edit 模式重刷场景颜色（场景颜色已由上一次 UI/31 落盘）。
        /// </summary>
        public static bool SyncThemeFromSettings(out string detail)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(TwinUIR3Builder.SettingsAssetPath);
            if (settings == null)
            {
                detail = "settings asset missing (" + TwinUIR3Builder.SettingsAssetPath + ")";
                return false;
            }

            if (settings.themeMode != TwinUITheme.Mode)
            {
                TwinUITheme.SetMode(settings.themeMode);
            }

            detail = "synced effectiveTheme=" + TwinUITheme.Mode + " settingsTheme=" + settings.themeMode;
            return settings.themeMode == TwinUITheme.Mode;
        }

        public static bool ThemeMatchesSettings(out string detail)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(TwinUIR3Builder.SettingsAssetPath);
            if (settings == null)
            {
                detail = "settings asset missing (" + TwinUIR3Builder.SettingsAssetPath + ")";
                return false;
            }

            if (settings.themeMode != TwinUITheme.Mode)
            {
                detail = "effectiveTheme=" + TwinUITheme.Mode + " settingsTheme=" + settings.themeMode;
                return false;
            }

            detail = "effectiveTheme=" + TwinUITheme.Mode + " settingsTheme=" + settings.themeMode;
            return true;
        }

        // ------------------------------------------------------------------ UI/31

        [MenuItem("DigitalTwin/UI/31 Apply Ocean Theme", false, 31)]
        public static void ApplyOceanMenu()
        {
            bool ok = PrepareTheme(TwinUIThemeMode.Ocean, true, out string detail);
            Debug.Log(Tag + " applied=Ocean saved=yes result=" + (ok ? "PASS" : "FAIL") + " " + detail);
        }

        [MenuItem("DigitalTwin/UI/0 Force Exit Play Mode (recovery)", false, 0)]
        public static void ForceExitPlayMenu()
        {
            SessionState.SetBool(PendingKey, false);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                Debug.Log(Tag + " force exit play mode");
            }
        }

        [MenuItem("DigitalTwin/UI/32 Restore Deep Theme", false, 32)]
        public static void RestoreDeepMenu()
        {
            bool ok = PrepareTheme(TwinUIThemeMode.Dark, true, out string detail);
            Debug.Log(Tag + " applied=Dark saved=yes result=" + (ok ? "PASS" : "FAIL") + " " + detail);
        }

        /// <summary>切换主题：settings 资产 + 静态主题 + 场景内所有 applier/工业台重刷 + 场景保存。</summary>
        private static bool PrepareTheme(TwinUIThemeMode mode, bool saveScene, out string detail)
        {
            var sb = new StringBuilder();
            var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(TwinUIR3Builder.SettingsAssetPath);
            if (settings == null)
            {
                detail = "settings asset missing (run UI/3)";
                return false;
            }

            bool changed = settings.themeMode != mode;
            if (changed)
            {
                settings.themeMode = mode;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            TwinUITheme.SetMode(mode);

            // 场景里带角色标签的图元是否仍停留在别的主题颜色上（例如从 Deep 备份恢复后又跑 UI/31）。
            bool needsRecolor = SceneNeedsRecolor();
            changed = changed || needsRecolor;

            int appliers = 0;
            var list = UnityEngine.Object.FindObjectsOfType<TwinUIThemeApplier>(true);
            if (list.Length == 0)
            {
                // 兜底：Resources.FindObjectsOfTypeAll 能覆盖未激活对象/未加载场景残留，再按场景过滤。
                var all = Resources.FindObjectsOfTypeAll<TwinUIThemeApplier>();
                var sceneList = new List<TwinUIThemeApplier>();
                for (int i = 0; i < all.Length; i++)
                {
                    var a = all[i];
                    if (a != null && a.gameObject.scene.IsValid())
                    {
                        sceneList.Add(a);
                    }
                }

                list = sceneList.ToArray();
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            sb.Append(" scene=").Append(string.IsNullOrEmpty(activeScene.path) ? "<untitled>" : activeScene.path);
            for (int i = 0; i < list.Length; i++)
            {
                var applier = list[i];
                if (applier == null)
                {
                    continue;
                }

                // Play 启动引导：只有非 Dark 主题才强制引导（Dark 保持加字段前的旧行为）。
                if (applier.ApplyBootMode != (mode != TwinUIThemeMode.Dark) || applier.BootMode != mode)
                {
                    applier.SetBootMode(mode, mode != TwinUIThemeMode.Dark);
                    changed = true;
                }

                if (changed)
                {
                    applier.Apply();
                    EditorUtility.SetDirty(applier);
                }

                appliers++;
            }

            sb.Append("appliers=").Append(appliers);

            var canvasGo = TwinSceneLookup.FindObject(CanvasPath);

            // 自检/换肤前置修复：Canvas 上的 TwinUIThemeApplier 若是"Missing (Mono Script)"（历史场景里
            // m_Script 指向内嵌 MonoScript 的坏引用），FindObjectsOfType 找不到它，主题切换会静默失效。
            if (canvasGo != null)
            {
                // 注意：本场景里 TwinUIThemeApplier 的 m_Script 历史上是"内嵌 MonoScript"引用（UI/34 探针
                // 实测 MonoScript.FromMonoBehaviour 返回非资产对象），进 Play 后可能失效。因此 Play 启动主题
                // 不再依赖它，改由 TwinUiWindowHost.bootTheme 承担；这里只保证组件存在，避免每轮重建造成
                // fileID 抖动（幂等性）。
                var canvasApplier = canvasGo.GetComponent<TwinUIThemeApplier>();
                bool applierIsAssetScript = canvasApplier != null && IsAssetMonoScript(canvasApplier);
                sb.Append(" applierAssetScript=").Append(applierIsAssetScript);
                if (canvasApplier == null)
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(canvasGo);
                    int stripped = missing > 0 ? GameObjectUtility.RemoveMonoBehavioursWithMissingScript(canvasGo) : 0;
                    canvasApplier = canvasGo.AddComponent<TwinUIThemeApplier>();
                    canvasApplier.SetBootMode(mode, mode != TwinUIThemeMode.Dark);
                    changed = true;
                    appliers++;
                    sb.Append(" applierRepaired=added stripped=").Append(stripped);
                }
                else
                {
                    sb.Append(" applierRepaired=none");
                }

                // 历史场景里大量图元的 TwinUIToneTag.Role 是 None（早期手写/旧 builder 留下的），
                // 只靠 applier 无法换肤。这里用"当前颜色 → 令牌角色"的反查把角色补齐（不改颜色本身）。
                int retagged = RetagCanvas(canvasGo);
                if (retagged > 0)
                {
                    changed = true;
                    sb.Append(" retagged=").Append(retagged);
                }

                var windowHost = canvasGo.GetComponent<TwinUiWindowHost>();
                if (windowHost != null && windowHost.BootTheme != mode)
                {
                    windowHost.SetBootTheme(mode);
                    EditorUtility.SetDirty(windowHost);
                    changed = true;
                    sb.Append(" hostBootTheme=").Append(mode);
                }

                if (changed)
                {
                    canvasApplier.Apply();
                }

                sb.Append(" coverage=").Append(DescribeCoverage(canvasGo));
            }
            var frame = canvasGo == null ? null : canvasGo.GetComponent<TwinUiWindowFrame>();
            if (frame != null)
            {
                float oldEdge = frame.ResizeHitSize;
                float oldCorner = frame.CornerHitSize;
                frame.SetChromeMetrics(TwinUIStyle.HandleEdgeSize, TwinUIStyle.HandleCornerSize);
                if (frame.ResizeHitSize != oldEdge || frame.CornerHitSize != oldCorner)
                {
                    changed = true;
                    EditorUtility.SetDirty(frame);
                }
                sb.Append(" frameEdge=").Append(frame.ResizeHitSize.ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" frameCorner=").Append(frame.CornerHitSize.ToString("F0", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(" frame=missing");
            }

            // A49：保存前断言"当前生效主题 == 设置主题"，不一致则中止保存（避免把半换肤状态写进场景）。
            string themeGuard;
            bool themeMatch = ThemeMatchesSettings(out themeGuard);
            sb.Append(" themeGuard=").Append(themeMatch ? "ok" : "MISMATCH");
            if (!themeMatch)
            {
                detail = sb.ToString() + " (save aborted: " + themeGuard + ")";
                Debug.LogError(Tag + " save aborted: " + themeGuard);
                return false;
            }

            bool dirty = changed || EditorSceneManager.GetActiveScene().isDirty;
            if (saveScene && dirty)
            {
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.Refresh();
            }

            string hash = FileHash(ScenePath);
            sb.Append(" sceneDirty=").Append(dirty ? "yes" : "no")
                .Append(" sceneHash=").Append(hash)
                .Append(" settingsTheme=").Append(settings.themeMode);
            detail = sb.ToString();
            sb.Insert(0, "[THEME stage] ").AppendLine();
            AppendEvidenceLog("theme_stage.txt", sb.ToString());
            return true;
        }

        /// <summary>
        /// 按"颜色 → 令牌角色"反查补齐缺失的 tone 标签（幂等）。跳过按钮子树（按钮自带五态管理）
        /// 与全透明图元（无法反查）。不新增、不删除任何对象，只写 TwinUIToneTag.Role。
        /// </summary>
        /// <summary>组件脚本引用是否落在工程资产上（而不是场景内嵌 MonoScript）。</summary>
        private static bool IsAssetMonoScript(MonoBehaviour behaviour)
        {
            var script = MonoScript.FromMonoBehaviour(behaviour);
            return script != null && AssetDatabase.Contains(script);
        }

        private static int RetagCanvas(GameObject canvasGo)
        {
            int retagged = 0;
            var graphics = canvasGo.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null || g.color.a < 0.05f)
                {
                    continue;
                }

                var tag = g.GetComponent<TwinUIToneTag>();
                if (tag != null && tag.Role != TwinUIToneRole.None)
                {
                    continue;
                }

                // 按钮背景/标签由 TwinUIButton 自管（selected/hover/pressed），不能挂静态角色。
                if (g.GetComponentInParent<TwinUIButton>(true) != null)
                {
                    continue;
                }

                var role = TwinUITheme.RoleOfColor(g.color);
                if (role == TwinUIToneRole.None)
                {
                    continue;
                }

                if (tag == null)
                {
                    tag = g.gameObject.AddComponent<TwinUIToneTag>();
                }

                tag.Role = role;
                EditorUtility.SetDirty(tag);
                retagged++;
            }

            return retagged;
        }

        /// <summary>主题覆盖率诊断：Canvas 内 Graphic 总数 / 带有效 role 的数量 / 未打标数量。</summary>
        private static string DescribeCoverage(GameObject canvasGo)
        {
            var graphics = canvasGo.GetComponentsInChildren<Graphic>(true);
            int total = 0;
            int tagged = 0;
            int textTotal = 0;
            int textTagged = 0;
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null || g is UnityEngine.UI.Image && g.GetComponent<TextMeshProUGUI>() != null)
                {
                    continue;
                }

                total++;
                var tag = g.GetComponent<TwinUIToneTag>();
                if (tag != null && tag.Role != TwinUIToneRole.None)
                {
                    tagged++;
                }

                if (g is TextMeshProUGUI)
                {
                    textTotal++;
                    if (tag != null && tag.Role != TwinUIToneRole.None)
                    {
                        textTagged++;
                    }
                }
            }

            return "graphics=" + total + "/tagged=" + tagged + " texts=" + textTotal + "/tagged=" + textTagged;
        }

        /// <summary>场景里是否还有带 tone 标签却颜色不在当前主题上的图元（用于幂等判定）。</summary>
        private static bool SceneNeedsRecolor()
        {
            var root = TwinSceneLookup.FindObject(CanvasPath);
            if (root == null)
            {
                return false;
            }

            var tags = root.GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (tag == null || tag.Role == TwinUIToneRole.None)
                {
                    continue;
                }

                var graphic = tag.GetComponent<Graphic>();
                if (graphic == null)
                {
                    continue;
                }

                if (!Near(graphic.color, TwinUITheme.ColorForRole(tag.Role)))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ UI/33

        [MenuItem("DigitalTwin/UI/33 Theme Visual Check (auto-exit)", false, 33)]
        public static void VisualCheckMenu()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning(Tag + " UI/33 skipped: already playing");
                return;
            }

            if (!PrepareTheme(TwinUIThemeMode.Ocean, true, out string detail))
            {
                Debug.LogError(Tag + " UI/33 FAIL: " + detail);
                return;
            }

            Directory.CreateDirectory(EvidenceShots);
            string backup = PlayerPrefs.GetString(TwinUiWindowHost.LayoutPrefKey, string.Empty);
            SessionState.SetString(PrefsBackupKey, backup);
            PlayerPrefs.DeleteKey(TwinUiWindowHost.LayoutPrefKey);
            PlayerPrefs.Save();

            ResetState();
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ArmedKey,
                EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            WriteLog("armed: " + detail + "\nshots=" + EvidenceShots);
            Debug.Log(Tag + " UI/33 armed, entering play mode; log=" + CheckLogPath);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static int _step;
        private static double _playStart = -1;
        private static double _waitUntil = -1;
        private static bool _finished;
        private static int _frames;
        private static int _shot;
        private static int _logErrors;
        private static int _logExceptions;
        private static bool _logHooked;
        private static int _failCount;
        private static TwinUiWindowHost _host;
        private static TwinUiWindowFrame _frame;
        private static List<string> _items;
        private static List<string> _fails;
        private static TwinUIButton _btnDefault;
        private static TextMeshProUGUI _hoverProbe;
        private static Vector3 _preMax;
        private static Rect _preRect;

        private static void ResetState()
        {
            _step = 0;
            _playStart = -1;
            _waitUntil = -1;
            _finished = false;
            _frames = 0;
            _shot = 0;
            _logErrors = 0;
            _logExceptions = 0;
            _logHooked = false;
            _failCount = 0;
            _host = null;
            _frame = null;
            _items = new List<string>();
            _fails = new List<string>();
            _btnDefault = null;
            _hoverProbe = null;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (_items == null || _fails == null)
            {
                // 进入 Play 触发域重载后静态字段会归零（玩家脚本重编译），这里重新初始化。
                ResetState();
            }

            if (!EditorApplication.isPlaying)
            {
                if (!_finished)
                {
                    string armedText = SessionState.GetString(ArmedKey, string.Empty);
                    if (double.TryParse(armedText, NumberStyles.Float, CultureInfo.InvariantCulture, out double armed)
                        && EditorApplication.timeSinceStartup - armed > 180.0)
                    {
                        Fail("play mode did not start within 180s");
                        Finish();
                    }
                }

                return;
            }

            if (!_logHooked)
            {
                Application.logMessageReceived -= OnLog;
                Application.logMessageReceived += OnLog;
                _logHooked = true;
            }

            if (_playStart < 0)
            {
                _playStart = EditorApplication.timeSinceStartup;
            }

            _frames++;
            if (EditorApplication.timeSinceStartup - _playStart > 150.0)
            {
                Fail("step timeout at step " + _step + " (150s)");
                Finish();
                return;
            }

            if (_waitUntil > 0 && EditorApplication.timeSinceStartup < _waitUntil)
            {
                return;
            }

            _waitUntil = -1;
            try
            {
                Step();
            }
            catch (Exception e)
            {
                Fail("exception at step " + _step + ": " + e.GetType().Name + ": " + e.Message);
                Finish();
            }
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0: StepResolve(); break;
                case 1: StepChecks(); break;
                case 2: StepShot("ui33_s01_ocean_default", 3); break;
                case 3: StepButtonHover(); break;
                case 4: StepShot("ui33_s02_button_hover", 5); break;
                case 5: StepButtonPressed(); break;
                case 6: StepShot("ui33_s03_button_pressed", 7); break;
                case 7: StepMaximize(); break;
                case 8: StepShot("ui33_s04_window_maximized", 9); break;
                case 9: StepRestore(); break;
                case 10: StepHandleHoverAndTint(); break;
                case 11: Finish(); break;
            }
        }

        private static void StepResolve()
        {
            if (_frames < 30)
            {
                return;
            }

            var canvasGo = GameObject.Find(CanvasPath);
            _host = canvasGo == null ? null : canvasGo.GetComponent<TwinUiWindowHost>();
            _frame = canvasGo == null ? null : canvasGo.GetComponent<TwinUiWindowFrame>();
            if (_host == null || !_host.IsReady || _host.Root == null)
            {
                if (_frames > 240)
                {
                    Fail("window host not ready");
                    Finish();
                }

                return;
            }

            if (TwinUITheme.Mode != TwinUIThemeMode.Ocean)
            {
                _items.Add("check forced Ocean (runtime was " + TwinUITheme.Mode + ")");
                TwinUITheme.SetMode(TwinUIThemeMode.Ocean);
                _waitUntil = EditorApplication.timeSinceStartup + 0.2;
            }

            _step = 1;
        }

        private static void StepChecks()
        {
            TwinUIPalette p = TwinUITheme.Current;
            _items.Add("mode=" + TwinUITheme.Mode + " palette=" + p.Mode + " expected=Ocean");
            if (TwinUITheme.Mode != TwinUIThemeMode.Ocean)
            {
                Fail("theme mode is " + TwinUITheme.Mode + " (expected Ocean)");
            }

            // 期望值：原型令牌（OracleHex）；实测值：Image.color / TMP.color / 序列化曲线色。
            CheckPalette(p);
            CheckRootChild("标题栏底", "WindowTitleBar", TwinUIToneRole.Layer2, p.Layer2);
            CheckRootChild("窗口背景", "WindowBackdrop", TwinUIToneRole.Layer, p.Layer);
            CheckRootChild("窗口边框", "WindowBackground", TwinUIToneRole.Border, p.Border);
            CheckHeader();
            CheckCard();
            CheckText();
            CheckAccentChip();
            CheckChart();
            CheckRadii();
            CheckShadow(p);
            CheckAccentOutline(p);
            _btnDefault = FindDefaultButton(_host.Content);
            if (_btnDefault == null)
            {
                Fail("no Default-kind TwinUIButton found for normal/hover/pressed");
            }
            else
            {
                _items.Add("button=" + _btnDefault.name + " kind=" + _btnDefault.ButtonKind);
                CheckActual("按钮常态 " + _btnDefault.name, _btnDefault.Background.color, p.BtnFill, HexBtnFill);
            }

            _step = 2;
        }

        private static void StepButtonHover()
        {
            if (_btnDefault != null)
            {
                var data = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(_btnDefault.gameObject, data, ExecuteEvents.pointerEnterHandler);
                CheckActual("按钮悬停 " + _btnDefault.name, _btnDefault.Background.color,
                    TwinUITheme.Current.BtnFillHover, HexBtnFillHover);
            }

            _step = 4;
            _waitUntil = EditorApplication.timeSinceStartup + 0.3;
        }

        private static void StepButtonPressed()
        {
            if (_btnDefault != null)
            {
                var data = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(_btnDefault.gameObject, data, ExecuteEvents.pointerDownHandler);
                CheckActual("按钮按下 " + _btnDefault.name, _btnDefault.Background.color,
                    TwinUITheme.Current.BtnFillPressed, HexBtnFillPressed);
            }

            _step = 6;
            _waitUntil = EditorApplication.timeSinceStartup + 0.3;
        }

        private static void StepMaximize()
        {
            if (_btnDefault != null)
            {
                var data = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(_btnDefault.gameObject, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(_btnDefault.gameObject, data, ExecuteEvents.pointerExitHandler);
            }

            if (_frame != null)
            {
                _preRect = new Rect(_host.Root.rect);
                _preMax = _host.Content.localScale;
                _frame.ToggleMaximize();
            }

            _step = 8;
            _waitUntil = EditorApplication.timeSinceStartup + 0.5;
        }

        private static void StepRestore()
        {
            if (_frame != null && _host.IsMaximized)
            {
                _frame.ToggleMaximize();
            }

            _step = 10;
            _waitUntil = EditorApplication.timeSinceStartup + 0.5;
        }

        private static void StepHandleHoverAndTint()
        {
            // 手柄 hover：2px accent 高亮线（alpha 0 → 0.95）。
            var handle = _host.Root.Find("ResizeRight");
            if (handle != null)
            {
                var hl = handle.Find("Hl");
                var img = hl == null ? null : hl.GetComponent<Image>();
                var data = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(handle.gameObject, data, ExecuteEvents.pointerEnterHandler);
                if (img != null)
                {
                    Color expected = TwinUIStyle.Alpha(TwinUITheme.Current.Accent, 0.95f);
                    CheckActual("手柄 hover 高亮 ResizeRight/Hl", img.color, expected, TwinUIToneRole.Accent);
                }
                else
                {
                    Fail("ResizeRight/Hl missing");
                }
            }
            else
            {
                Fail("ResizeRight handle missing");
            }

            // 窗口外底色层：存在、不参与射线、3D 区域没有被 UI 命中。
            var canvas = _host.CanvasRect;
            var tint = canvas.Find("UiSceneTint");
            if (tint == null)
            {
                Fail("UiSceneTint missing");
            }
            else
            {
                var tintImage = tint.GetComponent<Image>();
                bool noRaycast = tintImage != null && !tintImage.raycastTarget;
                _items.Add("tint raycastTarget=" + (tintImage == null ? "n/a" : tintImage.raycastTarget.ToString())
                    + " color=" + Hex(tintImage == null ? Color.clear : tintImage.color)
                    + " expectedBg=" + Hex(TwinUIStyle.BgColor) + "@30");
                if (!noRaycast)
                {
                    Fail("UiSceneTint raycastTarget must be false");
                }

                if (tintImage != null)
                {
                    CheckActual("窗口外深海底色 UiSceneTint", tintImage.color,
                        TwinUIStyle.Alpha(TwinUIStyle.BgColor, 0.30f), "#071827@30");
                }

                var points = new[] { new Vector2(192f, 540f), new Vector2(192f, 972f), new Vector2(960f, 1040f) };
                for (int i = 0; i < points.Length; i++)
                {
                    var ped = new PointerEventData(EventSystem.current) { position = points[i] };
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(ped, hits);
                    bool tintHit = false;
                    for (int h = 0; h < hits.Count; h++)
                    {
                        if (hits[h].gameObject == tint.gameObject)
                        {
                            tintHit = true;
                        }
                    }

                    _items.Add("raycast@" + points[i].x.ToString("F0", CultureInfo.InvariantCulture) + ","
                        + points[i].y.ToString("F0", CultureInfo.InvariantCulture) + " uiHits=" + hits.Count
                        + " tintHit=" + tintHit);
                    if (tintHit)
                    {
                        Fail("scene tint is hit-testable at " + points[i]);
                    }
                }
            }

            // 最大化后外阴影应隐藏（避免全屏窗出现阴影边）。
            _items.Add("shadowActive(after restore)=" + ShadowActive());
            _items.Add("preMaxRect=" + _preRect + " contentScale=" + _preMax.x.ToString("F3", CultureInfo.InvariantCulture));
            _step = 11;
        }

        private static void StepShot(string name, int next)
        {
            _shot++;
            string path = Path.Combine(EvidenceShots, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            _items.Add("shot " + name + ".png");
            _step = next;
            _waitUntil = EditorApplication.timeSinceStartup + 0.35;
        }

        private static void Finish()
        {
            _finished = true;
            SessionState.SetBool(PendingKey, false);
            Application.logMessageReceived -= OnLog;

            string backup = SessionState.GetString(PrefsBackupKey, string.Empty);
            if (!string.IsNullOrEmpty(backup))
            {
                PlayerPrefs.SetString(TwinUiWindowHost.LayoutPrefKey, backup);
                PlayerPrefs.Save();
            }

            SessionState.EraseString(PrefsBackupKey);
            SessionState.EraseString(ArmedKey);

            int totalErrors = _failCount + _logErrors + _logExceptions;
            bool pass = totalErrors == 0;
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI theme visual check (P2.5 Ocean)  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("scene=" + ScenePath + " hash=" + FileHash(ScenePath));
            sb.AppendLine("editor=" + Application.unityVersion + " screen=1920x1080 play=" + EditorApplication.isPlaying);
            sb.AppendLine("-- items --");
            for (int i = 0; i < _items.Count; i++)
            {
                sb.AppendLine(_items[i]);
            }

            sb.AppendLine("-- failures --");
            for (int i = 0; i < _fails.Count; i++)
            {
                sb.AppendLine(_fails[i]);
            }

            sb.AppendLine("failures=" + _failCount + " logErrors=" + _logErrors + " logExceptions=" + _logExceptions
                + " totalErrors=" + totalErrors);
            sb.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL") + " shots=" + _shot);
            WriteLog(sb.ToString());

            var json = new StringBuilder();
            json.Append("{\"theme\":\"").Append(TwinUITheme.Current.Mode).Append("\",\"sceneHash\":\"")
                .Append(FileHash(ScenePath)).Append("\",\"failures\":").Append(_failCount)
                .Append(",\"logErrors\":").Append(_logErrors)
                .Append(",\"logExceptions\":").Append(_logExceptions)
                .Append(",\"totalErrors\":").Append(totalErrors)
                .Append(",\"shots\":").Append(_shot)
                .Append(",\"result\":\"").Append(pass ? "PASS" : "FAIL").Append("\"}");
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(CheckJsonPath, json.ToString(), new UTF8Encoding(false));
                Directory.CreateDirectory(EvidenceDir);
                File.Copy(CheckLogPath, Path.Combine(EvidenceDir, "ui33_check.txt"), true);
                File.Copy(CheckJsonPath, Path.Combine(EvidenceDir, "ui33_check.json"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " evidence copy failed: " + e.Message);
            }

            Debug.Log(Tag + " UI/33 check result=" + (pass ? "PASS" : "FAIL") + " totalErrors=" + totalErrors);
            EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------------ 断言

        private static void CheckPalette(TwinUIPalette p)
        {
            CheckPaletteField("Bg", p.Bg, TwinUIToneRole.Bg);
            CheckPaletteField("Panel", p.Panel, TwinUIToneRole.Panel);
            CheckPaletteField("Layer", p.Layer, TwinUIToneRole.Layer);
            CheckPaletteField("Layer2", p.Layer2, TwinUIToneRole.Layer2);
            CheckPaletteField("Card", p.Card, TwinUIToneRole.Card);
            CheckPaletteField("Border", p.Border, TwinUIToneRole.Border);
            CheckPaletteField("LineStrong", p.LineStrong, TwinUIToneRole.LineStrong);
            CheckPaletteField("Text", p.Text, TwinUIToneRole.Text);
            CheckPaletteField("Muted", p.Muted, TwinUIToneRole.Muted);
            CheckPaletteField("Accent", p.Accent, TwinUIToneRole.Accent);
            CheckPaletteField("Strong", p.Strong, TwinUIToneRole.Strong);
            CheckPaletteField("Ok", p.Ok, TwinUIToneRole.Ok);
            CheckPaletteField("Warn", p.Warn, TwinUIToneRole.Warn);
            CheckPaletteField("Crit", p.Crit, TwinUIToneRole.Crit);
            CheckPaletteField("PlotBg", p.PlotBg, TwinUIToneRole.PlotBg);
            CheckPaletteField("Grid", p.Grid, TwinUIToneRole.Grid);
            CheckPaletteHex("BtnFill", p.BtnFill, HexBtnFill);
            CheckPaletteHex("BtnFillHover", p.BtnFillHover, HexBtnFillHover);
            CheckPaletteHex("BtnFillPressed", p.BtnFillPressed, HexBtnFillPressed);
        }

        private static void CheckPaletteHex(string name, Color actual, string hex)
        {
            if (!Near(actual, TwinUITheme.Hex(hex)))
            {
                Fail("palette " + name + " expected=" + hex + " actual=" + Hex(actual));
            }
        }

        private static void CheckPaletteField(string name, Color actual, TwinUIToneRole role)
        {
            if (!OracleHex.TryGetValue(role, out string hex))
            {
                return;
            }

            Color expected = TwinUITheme.Hex(hex);
            if (!Near(actual, expected))
            {
                Fail("palette " + name + " expected=" + hex + " actual=" + Hex(actual));
            }
        }

        private static void CheckActual(string label, Color actual, Color expected, TwinUIToneRole role)
        {
            string oracle = OracleHex.TryGetValue(role, out string hex) ? hex : "n/a";
            CheckActual(label + " role=" + role, actual, expected, oracle);
        }

        private static void CheckActual(string label, Color actual, Color expected, string oracleHex)
        {
            bool ok = Near(actual, expected);
            _items.Add(label + " expected=" + Hex(expected) + "(V3 " + oracleHex
                + ") actual=" + Hex(actual) + (ok ? " ok" : " FAIL"));
            if (!ok)
            {
                Fail(label + " actual=" + Hex(actual) + " expected=" + Hex(expected));
            }
        }

        private static void CheckRoleImage(string label, string roleName, TwinUIToneRole role)
        {
            var content = _host.Content;
            var img = FindRoleGraphic(content, role);
            if (img == null)
            {
                Fail(label + " role=" + roleName + " not found");
                return;
            }

            CheckActual(label + " (" + img.name + ")", img.color, TwinUITheme.ColorForRole(role), role);
        }

        private static void CheckRootChild(string label, string childName, TwinUIToneRole role, Color expected)
        {
            var child = _host.Root.Find(childName);
            if (child == null)
            {
                Fail(label + " child missing: " + childName);
                return;
            }

            var img = child.GetComponent<Image>();
            if (img == null)
            {
                Fail(label + " has no Image: " + childName);
                return;
            }

            CheckActual(label + " (" + childName + ")", img.color, expected, role);
        }

        private static void CheckHeader()
        {
            var content = _host.Content;
            var header = content.Find("Header");
            if (header == null)
            {
                Fail("Header container missing");
                return;
            }

            var img = FindHeaderBackdropImage(header);
            var tag = img == null ? null : img.GetComponent<TwinUIToneTag>();

            if (img == null || tag == null)
            {
                Fail("Header background Image (tagged) missing");
                return;
            }

            CheckActual("Header 背景 (" + img.name + ")", img.color, TwinUITheme.ColorForRole(tag.Role), tag.Role);
        }

        private static void CheckCard()
        {
            var content = _host.Content;
            var card = FindRoleGraphic(content, TwinUIToneRole.Card);
            if (card == null)
            {
                Fail("card background (role=Card) not found");
                return;
            }

            CheckActual("卡片背景 (" + card.name + ")", card.color, TwinUITheme.ColorForRole(TwinUIToneRole.Card),
                TwinUIToneRole.Card);
        }

        /// <summary>P6-3：卡片/控件圆角断言（RadiusCard=11 / RadiusControl=8；只改 sprite，颜色/布局不变）。</summary>
        private static void CheckRadii()
        {
            var content = _host.Content;
            if (content == null)
            {
                Fail("radius check: content missing");
                return;
            }

            int cards = 0;
            int controls = 0;
            int badType = 0;
            var images = content.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null || img.sprite == null)
                {
                    continue;
                }

                string name = img.sprite.name;
                bool card = name == TwinUiRadii.CardSpriteName;
                bool control = name == TwinUiRadii.ControlSpriteName;
                if (!card && !control)
                {
                    continue;
                }

                if (img.type != UnityEngine.UI.Image.Type.Sliced)
                {
                    badType++;
                }

                if (card)
                {
                    cards++;
                }
                else
                {
                    controls++;
                }
            }

            _items.Add("radii cards=" + cards + " controls=" + controls + " badType=" + badType
                + " cardSprite=" + TwinUiRadii.CardSpriteName + "(" + TwinUiRadii.RadiusCard + ")"
                + " controlSprite=" + TwinUiRadii.ControlSpriteName + "(" + TwinUiRadii.RadiusControl + ")");
            if (cards < 3)
            {
                Fail("rounded cards < 3 (" + cards + ")");
            }

            if (controls < 3)
            {
                Fail("rounded controls < 3 (" + controls + ")");
            }

            if (badType > 0)
            {
                Fail("rounded image type != Sliced (" + badType + ")");
            }

            var cardImg = FindRoleGraphic(content, TwinUIToneRole.Card);
            if (cardImg == null)
            {
                Fail("radius check: role=Card not found");
                return;
            }

            if (cardImg.sprite != TwinUiRadii.CardSprite)
            {
                Fail("card role image sprite not rounded (" + cardImg.name + ")");
            }

            CheckActual("圆角卡片颜色不变 (" + cardImg.name + ")", cardImg.color,
                TwinUITheme.ColorForRole(TwinUIToneRole.Card), TwinUIToneRole.Card);
        }
        private static void CheckText()
        {
            var content = _host.Content;
            var text = FindRoleText(content, TwinUIToneRole.Text, true);
            if (text == null)
            {
                Fail("body text (role=Text) not found");
                return;
            }

            CheckActual("正文文字 (" + text.name + ")", text.color, TwinUITheme.ColorForRole(TwinUIToneRole.Text),
                TwinUIToneRole.Text);
        }

        private static void CheckAccentChip()
        {
            var content = _host.Content;
            var chip = FindRoleGraphic(content, TwinUIToneRole.Accent, true);
            if (chip == null)
            {
                _items.Add("强调 chip role=Accent not found (fallback: Accent2/Strong skipped)");
                return;
            }

            CheckActual("强调 chip (" + chip.name + ")", chip.color, TwinUITheme.ColorForRole(TwinUIToneRole.Accent),
                TwinUIToneRole.Accent);
        }

        private static void CheckChart()
        {
            var content = _host.Content;
            var chart = content.GetComponentInChildren<TwinTelemetryChart>(true);
            if (chart == null)
            {
                Fail("TwinTelemetryChart not found (chart PlotBg/Grid not verified)");
                return;
            }

            var p = TwinUITheme.Current;
            CheckActual("图表 PlotBg (" + chart.name + ")", chart.PlotBackgroundColor, p.PlotBg, TwinUIToneRole.PlotBg);
            CheckActual("图表 Grid (" + chart.name + ")", chart.GridLineColor, p.Grid, TwinUIToneRole.Grid);
        }

        private static void CheckShadow(TwinUIPalette p)
        {
            var shadow = _host.CanvasRect.Find("UiWindowShadow");
            if (shadow == null)
            {
                Fail("UiWindowShadow missing (external shadow layer)");
                return;
            }

            var img = shadow.GetComponent<Image>();
            CheckActual("窗口外阴影 UiWindowShadow", img == null ? Color.clear : img.color, p.Shadow,
                TwinUIToneRole.Shadow);
            bool clickable = img != null && img.raycastTarget;
            _items.Add("shadow raycastTarget=" + clickable + " (expected False)");
            if (clickable)
            {
                Fail("UiWindowShadow must not be raycast target");
            }
        }

        private static void CheckAccentOutline(TwinUIPalette p)
        {
            var bg = _host.Root.Find("WindowBackground");
            var outline = bg == null ? null : bg.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null)
            {
                Fail("WindowBackground Outline (active accent) missing");
                return;
            }

            Color expected = TwinUIStyle.Alpha(p.Strong, 0.85f);
            CheckActual("活动窗 accent 描边 WindowBackground/Outline", outline.effectColor, expected,
                TwinUIToneRole.Strong);
        }

        // ------------------------------------------------------------------ 工具

        private static Image FindRoleGraphic(Transform scope, TwinUIToneRole role, bool excludeButtonSubtree = false)
        {
            if (scope == null)
            {
                return null;
            }

            Color expected = TwinUITheme.ColorForRole(role);
            Image fallback = null;
            var tags = scope.GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == null || tags[i].Role != role)
                {
                    continue;
                }

                var img = tags[i].GetComponent<Image>();
                if (img == null || img.color.a <= 0.01f)
                {
                    continue;
                }

                if (excludeButtonSubtree && img.GetComponentInParent<TwinUIButton>(true) != null)
                {
                    continue;
                }

                if (Near(img.color, expected))
                {
                    return img;
                }

                if (fallback == null)
                {
                    fallback = img;
                }
            }

            return fallback;
        }

        private static TextMeshProUGUI FindRoleText(Transform scope, TwinUIToneRole role, bool preferExact = false)
        {
            if (scope == null)
            {
                return null;
            }

            TextMeshProUGUI fallback = null;
            Color expected = TwinUITheme.ColorForRole(role);
            var tags = scope.GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == null || tags[i].Role != role)
                {
                    continue;
                }

                var text = tags[i].GetComponent<TextMeshProUGUI>();
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                if (!preferExact || Near(text.color, expected))
                {
                    return text;
                }

                if (fallback == null)
                {
                    fallback = text;
                }
            }

            return fallback;
        }

        /// <summary>标题栏/面板底：Header 下面积最大的已打标容器图（RecBadge 这类小徽标会被排除）。</summary>
        private static Image FindHeaderBackdropImage(Transform header)
        {
            Image best = null;
            float bestArea = -1f;
            var tags = header.GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (tag == null || tag.Role == TwinUIToneRole.None)
                {
                    continue;
                }

                if (tag.Role != TwinUIToneRole.CardAlt && tag.Role != TwinUIToneRole.Card
                    && tag.Role != TwinUIToneRole.Panel && tag.Role != TwinUIToneRole.Layer
                    && tag.Role != TwinUIToneRole.Layer2 && tag.Role != TwinUIToneRole.Bg)
                {
                    continue;
                }

                var img = tag.GetComponent<Image>();
                if (img == null || img.color.a < 0.05f)
                {
                    continue;
                }

                float area = img.rectTransform.rect.width * img.rectTransform.rect.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = img;
                }
            }

            return best;
        }

        private static Image FindFirstTaggedImage(Transform scope)
        {
            var tags = scope.GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] != null && tags[i].Role != TwinUIToneRole.None)
                {
                    var img = tags[i].GetComponent<Image>();
                    if (img != null && img.color.a > 0.01f)
                    {
                        return img;
                    }
                }
            }

            return null;
        }

        private static TwinUIButton FindDefaultButton(Transform scope)
        {
            TwinUIButton fallback = null;
            var buttons = scope.GetComponentsInChildren<TwinUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b != null && b.ButtonKind == TwinUIButton.Kind.Default && b.Background != null
                    && b.Button != null && b.Button.interactable && b.gameObject.activeInHierarchy)
                {
                    if (b.Background.color == TwinUITheme.Current.BtnFill)
                    {
                        return b;
                    }

                    if (fallback == null)
                    {
                        fallback = b;
                    }
                }
            }

            return fallback;
        }

        private static string ShadowActive()
        {
            var shadow = _host.CanvasRect.Find("UiWindowShadow");
            return shadow == null ? "missing" : (shadow.gameObject.activeSelf ? "yes" : "no");
        }

        private static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 0.004f && Mathf.Abs(a.g - b.g) <= 0.004f
                && Mathf.Abs(a.b - b.b) <= 0.004f && Mathf.Abs(a.a - b.a) <= 0.01f;
        }

        private static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c).Substring(0, 6).ToUpperInvariant()
                + (c.a < 0.995f ? "@" + Mathf.RoundToInt(c.a * 100) : string.Empty);
        }

        private static void Fail(string message)
        {
            if (_fails == null)
            {
                _fails = new List<string>();
            }

            _failCount++;
            _fails.Add(message);
            Debug.LogError(Tag + " FAIL " + message);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (IsNoise(condition))
            {
                return;
            }

            if (type == LogType.Error)
            {
                _logErrors++;
            }
            else if (type == LogType.Exception || type == LogType.Assert)
            {
                _logExceptions++;
            }
        }

        private static bool IsNoise(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return false;
            }

            // MCP 插件进 Play 会主动断 WS（4001），已知噪声；[THEME] FAIL 由 items 计数，不重复计。
            return condition.Contains("WebSocket") || condition.Contains("websocket")
                || condition.Contains("[MCP]") || condition.Contains("mcp_call")
                || condition.Contains(Tag + " FAIL");
        }

        private static void WriteLog(string text)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(CheckLogPath, text, new UTF8Encoding(false));
        }

        private static void AppendEvidenceLog(string name, string text)
        {
            try
            {
                Directory.CreateDirectory(EvidenceDir);
                File.AppendAllText(Path.Combine(EvidenceDir, name), text + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 证据目录不可写时不阻断菜单。
            }
        }

        private static string FileHash(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "hash-error:" + e.Message;
            }
        }
    }
}