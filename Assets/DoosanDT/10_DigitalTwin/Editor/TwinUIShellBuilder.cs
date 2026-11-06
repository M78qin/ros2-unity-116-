// Builds (or rebuilds) the unified entry panel of Doosan-DT.
//
// Menu: DigitalTwin/UI/1 Build Unified Shell        (idempotent: the previous "UI_统一入口面板" is
//                                                    destroyed first, nothing else is touched)
//
// Layout contract
//   Canvas (ScreenSpaceCamera on Main Camera, sortingOrder 100)
//     TopBar      : product title + status strip (connection / mode / source / recording)
//     TabBar      : 漫游 / 训练 / 拆解 / 数据
//     HintBar     : one line describing the current teaching mode
//     ChartCard   : 关节角 curve + 力/力矩 curve (see TwinTelemetryChartBinder)
//     CommandCard : container for the bidirectional-control view (filled by P5)
//
// The five legacy canvases are NOT re-parented and their render modes are NOT changed. They are
// only referenced by TwinDemoModeRouter as routing targets, which is the whole point of the
// "entry panel on top of existing floating panels" decision.
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIShellBuilder
    {
        private const string LogPath = "Logs/ui_build_shell.txt";
        private const string ShellRootPath = "30_界面与交互/UI_统一入口面板";
        private const string CanvasGroupPath = "30_界面与交互";
        private const string MainCameraPath = "50_玩家与相机/player/Main Camera";
        private const string LoginPanelPath = "30_界面与交互/Canvas/Canvas_I软著用标题/LoginPanel";
        private const string WelcomePath = "30_界面与交互/Canvas/登录界面";

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.11f, 0.92f);
        private static readonly Color CardColor = new Color(0.10f, 0.13f, 0.17f, 0.96f);
        private static readonly Color TextColor = new Color(0.92f, 0.94f, 0.98f, 1f);
        private static readonly Color DimTextColor = new Color(0.68f, 0.73f, 0.80f, 1f);
        private static readonly Color AccentColor = new Color(0.16f, 0.55f, 0.95f, 1f);

        [MenuItem("DigitalTwin/UI/1 Build Unified Shell")]
        public static void Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI shell build  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                sb.AppendLine("RESULT: FAIL - no scene loaded");
                Write(sb);
                return;
            }

            sb.AppendLine("scene=" + scene.path);

            TMP_FontAsset font = TwinUIFontCheck.LoadScFont();
            if (font == null)
            {
                sb.AppendLine("RESULT: FAIL - SC font asset missing, refusing to build tofu UI");
                Write(sb);
                return;
            }

            GameObject canvasGroup = TwinSceneLookup.FindObject(CanvasGroupPath);
            if (canvasGroup == null)
            {
                sb.AppendLine("RESULT: FAIL - group not found: " + CanvasGroupPath);
                Write(sb);
                return;
            }

            // idempotent rebuild
            GameObject previous = TwinSceneLookup.FindObject(ShellRootPath);
            if (previous != null)
            {
                sb.AppendLine("destroying previous shell: " + ShellRootPath);
                Object.DestroyImmediate(previous);
            }

            Camera mainCamera = TwinSceneLookup.FindComponent<Camera>(MainCameraPath);
            sb.AppendLine("main camera=" + (mainCamera == null ? "<null>" : TwinSceneLookup.PathOf(mainCamera.transform)));

            // ------------------------------------------------------------ canvas
            var shell = new GameObject("UI_统一入口面板", typeof(RectTransform));
            shell.transform.SetParent(canvasGroup.transform, false);
            var canvas = shell.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCamera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;

            var scaler = shell.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            shell.AddComponent<GraphicRaycaster>();
            shell.AddComponent<TwinUIShellController>();
            var router = shell.AddComponent<TwinDemoModeRouter>();

            // ------------------------------------------------------------ top bar
            Image topBar = NewPanel(shell.transform, "TopBar", PanelColor);
            SetRect(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-24f, 56f));

            TMP_Text titleText = NewText(topBar.transform, "TitleText", TwinUIShellText.ProductTitle, 26f, TextColor, TextAlignmentOptions.Left, font);
            SetRect(titleText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(640f, 40f));

            Image statusRow = NewPanel(topBar.transform, "StatusRow", new Color(0f, 0f, 0f, 0f));
            SetRect(statusRow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-12f, 0f), new Vector2(980f, 36f));
            var statusLayout = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            statusLayout.childAlignment = TextAnchor.MiddleRight;
            statusLayout.spacing = 22f;
            statusLayout.childControlWidth = true;
            statusLayout.childControlHeight = true;
            statusLayout.childForceExpandWidth = false;
            statusLayout.childForceExpandHeight = false;
            statusLayout.padding = new RectOffset(0, 0, 0, 0);

            TMP_Text statusText = NewStatusText(statusRow.transform, "StatusText", font, 320f);
            TMP_Text modeText = NewStatusText(statusRow.transform, "ModeText", font, 200f);
            TMP_Text sourceText = NewStatusText(statusRow.transform, "SourceText", font, 300f);
            TMP_Text recordText = NewStatusText(statusRow.transform, "RecordText", font, 160f);

            // ------------------------------------------------------------ tab bar
            Image tabBar = NewPanel(shell.transform, "TabBar", new Color(0f, 0f, 0f, 0f));
            SetRect(tabBar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -76f), new Vector2(620f, 44f));
            var tabLayout = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.childAlignment = TextAnchor.MiddleLeft;
            tabLayout.spacing = 8f;
            tabLayout.childControlWidth = false;
            tabLayout.childControlHeight = false;
            tabLayout.childForceExpandWidth = false;
            tabLayout.childForceExpandHeight = false;

            string[] tabLabels = { TwinUIShellText.TabRoaming, TwinUIShellText.TabTraining, TwinUIShellText.TabDisassembly, TwinUIShellText.TabData };
            var tabButtons = new Button[tabLabels.Length];
            var tabLabelTexts = new TMP_Text[tabLabels.Length];
            var tabFills = new Image[tabLabels.Length];
            for (int i = 0; i < tabLabels.Length; i++)
            {
                tabButtons[i] = NewButton(tabBar.transform, "Tab_" + tabLabels[i], tabLabels[i], font, out tabFills[i], out tabLabelTexts[i]);
                var element = tabButtons[i].gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 132f;
                element.preferredHeight = 40f;
                SetRect(tabButtons[i].GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(132f, 40f));
            }

            // ------------------------------------------------------------ hint bar
            TMP_Text hintText = NewText(shell.transform, "HintBar", TwinUIShellText.HintRoaming, 20f, DimTextColor, TextAlignmentOptions.Left, font);
            SetRect(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(900f, 30f));

            // ------------------------------------------------------------ chart card
            Image chartCard = NewPanel(shell.transform, "ChartCard", CardColor);
            SetRect(chartCard.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -76f), new Vector2(470f, 620f));

            TMP_Text chartHeader = NewText(chartCard.transform, "Header", TwinUIShellText.ChartTitle, 22f, TextColor, TextAlignmentOptions.Left, font);
            SetRect(chartHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-24f, 32f));

            TwinTelemetryChart jointChart = NewChart(chartCard.transform, "JointChart", new Vector2(0f, 1f), new Vector2(12f, -66f), new Vector2(446f, 236f));
            TMP_Text jointLabel = NewText(chartCard.transform, "JointLabel", TwinUIShellText.ChartUnitJoint, 16f, TextColor, TextAlignmentOptions.Left, font);
            SetRect(jointLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(-24f, 22f));
            TMP_Text jointStatus = NewText(chartCard.transform, "JointStatus", TwinUIShellText.ChartNoData, 16f, DimTextColor, TextAlignmentOptions.Left, font);
            SetRect(jointStatus.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -308f), new Vector2(-24f, 22f));
            TMP_Text jointRange = NewText(chartCard.transform, "JointRange", "--", 14f, DimTextColor, TextAlignmentOptions.Right, font);
            SetRect(jointRange.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -308f), new Vector2(-24f, 22f));

            TwinTelemetryChart wrenchChart = NewChart(chartCard.transform, "WrenchChart", new Vector2(0f, 1f), new Vector2(12f, -338f), new Vector2(446f, 236f));
            TMP_Text wrenchLabel = NewText(chartCard.transform, "WrenchLabel", TwinUIShellText.ChartUnitForce, 16f, TextColor, TextAlignmentOptions.Left, font);
            SetRect(wrenchLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -312f), new Vector2(-24f, 22f));
            TMP_Text wrenchStatus = NewText(chartCard.transform, "WrenchStatus", TwinUIShellText.ChartNoForce, 16f, DimTextColor, TextAlignmentOptions.Left, font);
            SetRect(wrenchStatus.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -580f), new Vector2(-24f, 22f));
            TMP_Text wrenchRange = NewText(chartCard.transform, "WrenchRange", "--", 14f, DimTextColor, TextAlignmentOptions.Right, font);
            SetRect(wrenchRange.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -580f), new Vector2(-24f, 22f));

            // ------------------------------------------------------------ command card
            Image commandCard = NewPanel(shell.transform, "CommandCard", CardColor);
            SetRect(commandCard.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -712f), new Vector2(470f, 260f));

            TMP_Text commandHeader = NewText(commandCard.transform, "Header", TwinUIShellText.CommandTitle, 22f, TextColor, TextAlignmentOptions.Left, font);
            SetRect(commandHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-24f, 32f));

            TMP_Text commandStatus = NewText(commandCard.transform, "CommandStatus", TwinUIShellText.CommandIdle, 17f, DimTextColor, TextAlignmentOptions.TopLeft, font);
            SetRect(commandStatus.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(-24f, 116f));

            TMP_Text commandDetail = NewText(commandCard.transform, "CommandDetail", TwinUIShellText.CommandIdle, 15f, DimTextColor, TextAlignmentOptions.TopLeft, font);
            SetRect(commandDetail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -168f), new Vector2(-24f, 44f));

            var commandButtons = new Button[5];
            string[] commandLabels =
            {
                TwinUIShellText.CommandPreview, TwinUIShellText.CommandCheck, TwinUIShellText.CommandConfirm,
                TwinUIShellText.CommandExecute, TwinUIShellText.CommandHalt,
            };
            for (int i = 0; i < commandLabels.Length; i++)
            {
                Image unusedFill;
                TMP_Text unusedLabel;
                commandButtons[i] = NewButton(commandCard.transform, "Cmd_" + commandLabels[i], commandLabels[i], font, out unusedFill, out unusedLabel);
                SetRect(commandButtons[i].GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(12f + i * 90f, 12f), new Vector2(84f, 36f));
            }

            TwinCommandController commandController = ResolveCommandController();
            var commandView = commandCard.gameObject.AddComponent<TwinCommandPanelView>();
            commandView.Bind(ResolveRuntime(), commandController, commandStatus, commandDetail,
                commandButtons[0], commandButtons[1], commandButtons[2], commandButtons[3], commandButtons[4]);
            UnityEventTools.AddVoidPersistentListener(commandButtons[0].onClick, commandView.PreviewPlan);
            UnityEventTools.AddVoidPersistentListener(commandButtons[1].onClick, commandView.CheckSafety);
            UnityEventTools.AddVoidPersistentListener(commandButtons[2].onClick, commandView.ArmConfirm);
            UnityEventTools.AddVoidPersistentListener(commandButtons[3].onClick, commandView.ExecuteGuarded);
            UnityEventTools.AddVoidPersistentListener(commandButtons[4].onClick, commandView.ToggleHalt);

            // ------------------------------------------------------------ replay self-test stubs
            // two probes, both inactive by default, both only READING a csv file:
            //   SelfTestReplay_Real      -> real recorded session (joint angles vary, no force columns)
            //   SelfTestReplay_Synthetic -> generated wrench/torque trajectory (labelled synthetic)
            GameObject realReplay = TwinSceneLookup.FindObject(ShellRootPath + "/SelfTestReplay_Real");
            if (realReplay == null)
            {
                realReplay = CreateProbeObject(shell.transform, "SelfTestReplay_Real", MotionCsvPath);
            }

            GameObject syntheticReplay = TwinSceneLookup.FindObject(ShellRootPath + "/SelfTestReplay_Synthetic");
            if (syntheticReplay == null)
            {
                syntheticReplay = CreateProbeObject(shell.transform, "SelfTestReplay_Synthetic", ForceCsvPath);
            }

            var jointBinder = jointChart.gameObject.AddComponent<TwinTelemetryChartBinder>();
            jointBinder.Configure(ResolveRuntime(), realReplay.GetComponent<TwinTelemetryReplayProbe>(), jointChart,
                TwinTelemetryChartBinder.ChartGroup.JointAngle, null, jointStatus, jointRange, TwinUIShellText.ChartUnitJoint);

            var wrenchBinder = wrenchChart.gameObject.AddComponent<TwinTelemetryChartBinder>();
            wrenchBinder.Configure(ResolveRuntime(), syntheticReplay.GetComponent<TwinTelemetryReplayProbe>(), wrenchChart,
                TwinTelemetryChartBinder.ChartGroup.Wrench, null, wrenchStatus, wrenchRange, TwinUIShellText.ChartUnitForce);

            // ------------------------------------------------------------ router targets
            var targets = new TwinDemoModeTargets
            {
                roamingRoot = TwinSceneLookup.FindObject("20_漫游与训练/FunctionModule/Roaming_M"),
                trainingRoot = TwinSceneLookup.FindObject("20_漫游与训练/FunctionModule/Dis-Training_M"),
                disassemblyRoot = TwinSceneLookup.FindObject("00_数字孪生机械臂/10_电池包拆解系统/VR拆解"),
                roamingPanels = new[]
                {
                    TwinSceneLookup.FindObject("30_界面与交互/Canvas/Canvas_1 P"),
                    TwinSceneLookup.FindObject("30_界面与交互/Canvas/Canvas_1_V"),
                },
                trainingPanels = new[]
                {
                    TwinSceneLookup.FindObject("30_界面与交互/Canvas/Canvas_I"),
                    TwinSceneLookup.FindObject("30_界面与交互/Canvas/Canvas_I软著用标题"),
                },
                // The 5th legacy canvas (User_ui) is intentionally NOT routed: it holds a single
                // stray Button and renders at scale 0.0054 with planeDistance 100, i.e. roughly
                // 18x9 px on screen - enabling it would show a dot, not a page. Turning it into a
                // real 拆解 page needs a product decision, so it is reported instead of faked.
                disassemblyPanels = new GameObject[0],
            };
            router.SetTargets(targets);
            router.SetCurrentModeWithoutApplying(TwinDemoMode.Roaming);

            // ------------------------------------------------------------ bindings + welcome gate
            var bindings = new TwinUIShellBindings
            {
                shellRoot = shell,
                router = router,
                gate = null,
                tabButtons = tabButtons,
                tabLabels = tabLabelTexts,
                tabFills = tabFills,
                titleText = titleText,
                hintText = hintText,
                modeText = modeText,
                sourceText = sourceText,
                statusText = statusText,
                recordText = recordText,
                runtime = ResolveRuntime(),
                recorder = ResolveRecorder(),
                defaultTabIndex = 0,
                activeTabColor = AccentColor,
                inactiveTabColor = new Color(0.18f, 0.22f, 0.28f, 1f),
            };

            var controller = shell.GetComponent<TwinUIShellController>();
            controller.Bind(bindings);

            // persist the tab -> controller calls so they are visible in the wiring audit and do not
            // depend on a runtime AddListener (the editor-time AddListener is not serialized)
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null || tabButtons[i].onClick.GetPersistentEventCount() > 0)
                {
                    continue;
                }

                UnityEventTools.AddIntPersistentListener(tabButtons[i].onClick, controller.SelectTab, i);
                EditorUtility.SetDirty(tabButtons[i]);
                sb.AppendLine("  tab call: " + tabButtons[i].name + " -> SelectTab(" + i + ")");
            }

            GameObject welcome = TwinSceneLookup.FindObject(WelcomePath);
            BatteryLoginGate gate = welcome == null ? null : welcome.GetComponent<BatteryLoginGate>();
            if (gate != null)
            {
                gate.SetShellRoot(shell);
                EditorUtility.SetDirty(gate);
            }

            EnsureWelcomeHint(font);

            sb.AppendLine("shell built at " + ShellRootPath);
            sb.AppendLine("  tabBar buttons=" + tabButtons.Length);
            sb.AppendLine("  router.roamingRoot=" + Describe(targets.roamingRoot));
            sb.AppendLine("  router.trainingRoot=" + Describe(targets.trainingRoot));
            sb.AppendLine("  router.disassemblyRoot=" + Describe(targets.disassemblyRoot));
            sb.AppendLine("  router.roamingPanels=" + targets.roamingPanels.Length + " trainingPanels=" + targets.trainingPanels.Length + " disassemblyPanels=" + targets.disassemblyPanels.Length);
            sb.AppendLine("  NOT routed: 30_界面与交互/Canvas/User_ui (single stray Button, scale 0.0054, planeDistance 100 - needs a product decision)");
            sb.AppendLine("  runtime=" + Describe(bindings.runtime) + " recorder=" + Describe(bindings.recorder));
            sb.AppendLine("  gate wired=" + (gate != null) + " shellRoot=" + (gate != null && gate.gameObject != null));
            sb.AppendLine("  commandController=" + (commandController == null ? "<null>" : TwinSceneLookup.PathOf(commandController.transform)));
            sb.AppendLine("  selftest real csv=" + MotionCsvPath + " exists=" + File.Exists(MotionCsvPath));
            sb.AppendLine("  selftest synthetic csv=" + ForceCsvPath + " exists=" + File.Exists(ForceCsvPath));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine("scene saved");
            sb.AppendLine("RESULT: PASS");
            Write(sb);
            Debug.Log("[UI Shell] built - see " + LogPath);
        }

        // ------------------------------------------------------------------ helpers

        private static string ForceCsvPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ui_selftest_force.csv")).Replace('\\', '/');

        private static string MotionCsvPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ui_selftest_motion.csv")).Replace('\\', '/');

        private static GameObject CreateProbeObject(Transform parent, string name, string csv)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var probe = go.AddComponent<TwinTelemetryReplayProbe>();
            probe.SetCsvPath(csv);
            probe.SetReplayHz(20f);
            go.SetActive(false);
            return go;
        }

        private static DigitalTwinRuntime ResolveRuntime()
        {
            Object[] found = Object.FindObjectsOfType<DigitalTwinRuntime>(true);
            return found.Length == 0 ? null : (DigitalTwinRuntime)found[0];
        }

        private static TwinRecorder ResolveRecorder()
        {
            Object[] found = Object.FindObjectsOfType<TwinRecorder>(true);
            return found.Length == 0 ? null : (TwinRecorder)found[0];
        }

        private static void EnsureWelcomeHint(TMP_FontAsset font)
        {
            GameObject loginPanel = TwinSceneLookup.FindObject(LoginPanelPath);
            if (loginPanel == null)
            {
                return;
            }

            Transform existing = loginPanel.transform.Find("WelcomeHint");
            if (existing != null)
            {
                return;
            }

            TMP_Text hint = NewText(loginPanel.transform, "WelcomeHint", TwinUIShellText.WelcomeHint, 16f, DimTextColor, TextAlignmentOptions.Center, font);
            SetRect(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -86f), new Vector2(420f, 24f));

            GameObject welcome = TwinSceneLookup.FindObject(WelcomePath);
            BatteryLoginGate gate = welcome == null ? null : welcome.GetComponent<BatteryLoginGate>();
            if (gate != null)
            {
                var so = new SerializedObject(gate);
                SerializedProperty hintProp = so.FindProperty("hintText");
                if (hintProp != null)
                {
                    hintProp.objectReferenceValue = hint;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(gate);
            }
        }

        private static TwinTelemetryChart NewChart(Transform parent, string name, Vector2 anchor, Vector2 topLeftOffset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var chart = go.AddComponent<TwinTelemetryChart>();
            chart.raycastTarget = false;
            SetRect(go.GetComponent<RectTransform>(), anchor, anchor, anchor, topLeftOffset, size);
            return chart;
        }

        private static TMP_Text NewStatusText(Transform parent, string name, TMP_FontAsset font, float preferredWidth)
        {
            TMP_Text text = NewText(parent, name, "-", 17f, DimTextColor, TextAlignmentOptions.MidlineRight, font);
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = 30f;
            return text;
        }

        private static Image NewPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0.01f;
            return image;
        }

        private static TMP_Text NewText(Transform parent, string name, string value, float size, Color color, TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            if (font != null)
            {
                text.font = font;
            }

            return text;
        }

        private static Button NewButton(Transform parent, string name, string label, TMP_FontAsset font, out Image fill, out TMP_Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            fill = image;

            labelText = NewText(go.transform, "Label", label, 18f, TextColor, TextAlignmentOptions.Center, font);
            SetRect(labelText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            labelText.rectTransform.offsetMin = new Vector2(4f, 2f);
            labelText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private static TwinCommandController ResolveCommandController()
        {
            var candidates = Resources.FindObjectsOfTypeAll<TwinCommandController>();
            foreach (var candidate in candidates)
            {
                if (candidate != null && candidate.gameObject.scene.IsValid())
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void SetRect(RectTransform
 rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static string Describe(Object target)
        {
            return target == null ? "<null>" : TwinSceneLookup.PathOf(((target as Component) != null ? ((Component)target).transform : ((GameObject)target).transform));
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}