// 三期构建器：工业数据台（论文级最小数据采集集 / 事件流 / 实验设置）+ 实验上下文条。
//
// 幂等：所有子层级都是"先删后建"，重复执行安全。
// 菜单：UI/2 只重建工业数据台 + 上下文条；UI/1（R2 全量）会顺带调用本类 Build。
// UI/3 确保 TwinUiSettings.asset 存在；UI/4 在编辑态切换深色/浅色并写回设置。
using System.IO;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIR3Builder
    {
        public const string GroupPath = "30_界面与交互";
        public const string RootName = "UI_二期面板";
        public const string IndustrialName = "IndustrialRoot";
        public const string ContextBarName = "ContextBar";
        public const string SettingsAssetPath = "Assets/DoosanDT/10_DigitalTwin/Config/TwinUiSettings.asset";
        public const string RuntimePath = "00_数字孪生机械臂/4.24-Try/DigitalTwin_System";

        private const float PageWidth = 1888f;

        [MenuItem("DigitalTwin/UI/2 Build Industrial Panel (R3)", false, 2)]
        public static void BuildOnly()
        {
            var root = GameObject.Find(GroupPath + "/" + RootName);
            if (root == null)
            {
                Debug.LogError("[UI R3] root missing: run UI/1 Build Unified Shell (R2) first");
                return;
            }

            Build(root.transform, true);
            SaveAndLog("industrial");
        }

        [MenuItem("DigitalTwin/UI/3 Ensure TwinUiSettings Asset", false, 3)]
        public static void EnsureSettingsAssetMenu()
        {
            var settings = LoadSettings(true);
            Debug.Log("[UI R3] settings asset " + (settings != null ? "ready: " + AssetDatabase.GetAssetPath(settings)
                : "MISSING") + " uiHz=" + (settings != null ? settings.uiRefreshHz : 0f)
                + " theme=" + (settings != null ? settings.themeMode : TwinUIThemeMode.Dark));
        }

        [MenuItem("DigitalTwin/UI/5 Reset TwinUiSettings Defaults", false, 5)]
        public static void ResetSettingsMenu()
        {
            var settings = LoadSettings(true);
            if (settings == null)
            {
                Debug.LogError("[UI R3] settings asset missing: run UI/3");
                return;
            }

            float oldHz = settings.uiRefreshHz;
            settings.ResetToDefaults();
            settings.PersistToAsset();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            TwinUITheme.SetMode(settings.themeMode);

            var root = GameObject.Find(GroupPath + "/" + RootName);
            if (root != null)
            {
                var applier = root.GetComponent<TwinUIThemeApplier>();
                if (applier != null)
                {
                    applier.Apply();
                }

                var host = root.GetComponentInChildren<TwinUiIndustrialHost>(true);
                if (host != null)
                {
                    host.ApplyThemeNow();
                }

                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
            }

            Debug.Log("[UI R3] settings reset: uiHz=" + oldHz.ToString("F0") + "->" + settings.uiRefreshHz.ToString("F0")
                + " theme=" + settings.themeMode + " density=" + settings.density
                + " source=" + settings.defaultSource + " exportHz=" + settings.snapshotExportHz.ToString("F0"));
        }

        [MenuItem("DigitalTwin/UI/4 Toggle Theme (edit mode, persists)", false, 4)]
        public static void ToggleThemeMenu()
        {
            var settings = LoadSettings(true);
            if (settings == null)
            {
                Debug.LogError("[UI R3] settings asset missing: run UI/3");
                return;
            }

            var mode = TwinUITheme.Toggle();
            settings.themeMode = mode;
            settings.PersistToAsset();

            var root = GameObject.Find(GroupPath + "/" + RootName);
            if (root != null)
            {
                var applier = root.GetComponent<TwinUIThemeApplier>();
                if (applier == null)
                {
                    applier = root.gameObject.AddComponent<TwinUIThemeApplier>();
                }

                int count = applier.Apply();
                int hostCount = 0;
                var host = root.GetComponentInChildren<TwinUiIndustrialHost>(true);
                if (host != null)
                {
                    host.ApplyThemeNow();
                    hostCount = 1;
                }

                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[UI R3] theme=" + mode + " applied to " + count + " graphics, host=" + hostCount);
            }
        }

        // ------------------------------------------------------------------ 构建入口

        public static Transform Build(Transform root, bool includeContextBar)
        {
            var settings = LoadSettings(true);
            if (settings != null)
            {
                TwinUITheme.SetMode(settings.themeMode);
            }

            if (includeContextBar)
            {
                BuildContextBar(root);
            }

            EnsureTabButton(root);

            var old = root.Find(IndustrialName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var industrial = BuildIndustrial(root, settings);
            Wire(root, settings);
            return industrial;
        }

        private static void EnsureTabButton(Transform root)
        {
            var tabBar = root.Find("TabBar");
            if (tabBar == null)
            {
                return;
            }

            var existing = tabBar.Find("BtnIndustrial");
            if (existing != null)
            {
                var rt = existing.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition = new Vector2(368f, 0f);
                    rt.sizeDelta = new Vector2(176f, 40f);
                }

                return;
            }

            TwinUIStyle.Button(tabBar, "BtnIndustrial", TwinUITextR3.TabIndustrial, TwinUIButton.Kind.Default,
                new Vector2(368f, 0f), new Vector2(176f, 40f)).SetSelected(false);
        }

        // ------------------------------------------------------------------ 上下文条

        private static void BuildContextBar(Transform root)
        {
            var old = root.Find(ContextBarName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var bar = TwinUIStyle.Panel(root, ContextBarName, TwinUIStyle.PanelColor);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(568f, -72f);
            rt.sizeDelta = new Vector2(1320f, TwinUIStyle.ContextBarHeight);
            TwinUIStyle.Outline(bar.gameObject, TwinUIStyle.BorderColor);

            string[] names = { "Run", "Profile", "Condition", "Payload", "Tcp", "Session", "SelfCheck" };
            string[] labels =
            {
                TwinUITextR3.CtxRun, TwinUITextR3.CtxProfile, TwinUITextR3.CtxCondition,
                TwinUITextR3.CtxPayload, TwinUITextR3.CtxTcp, TwinUITextR3.CtxSession, TwinUITextR3.CtxSelfCheck
            };

            for (int i = 0; i < names.Length; i++)
            {
                var chip = TwinUIStyle.Panel(rt, "Chip_" + names[i], TwinUIStyle.CardAltColor);
                TwinUIStyle.Row(chip.rectTransform, i * 146f, 2f, 140f, 40f);

                if (names[i] == "SelfCheck")
                {
                    var dot = TwinUIStyle.Panel(chip.rectTransform, "Dot", TwinUIStyle.OkColor);
                    TwinUIStyle.Place(dot.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                        new Vector2(6f, -9f), new Vector2(6f, 6f));
                }

                var label = TwinUIStyle.Text(chip.rectTransform, "Label", labels[i], 10f, TwinUIStyle.MutedColor);
                TwinUIStyle.Row(label.rectTransform, 7f, 3f, 128f, 13f);

                var value = TwinUIStyle.Text(chip.rectTransform, "Value", TwinUITextR3.CtxNotProvided, 12f,
                    TwinUIStyle.TextColor);
                value.enableWordWrapping = false;
                value.overflowMode = TextOverflowModes.Ellipsis;
                TwinUIStyle.Row(value.rectTransform, 7f, 18f, 128f, 18f);
            }

            var sourceTag = TwinUIStyle.Panel(rt, "SourceTag", TwinUIStyle.CardAltColor);
            TwinUIStyle.Row(sourceTag.rectTransform, 1030f, 8f, 150f, 28f);
            var tagDot = TwinUIStyle.Panel(sourceTag.rectTransform, "Dot", TwinUIStyle.AccentColor);
            TwinUIStyle.Place(tagDot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(6f, 0f), new Vector2(8f, 8f));
            var tagText = TwinUIStyle.Text(sourceTag.rectTransform, "Text", TwinUITextR3.CtxRuntime, 12f,
                TwinUIStyle.AccentColor, TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(tagText.rectTransform, 14f, 0f, 4f, 0f);

            var watermark = TwinUIStyle.Text(rt, "Watermark", string.Empty, 12f, TwinUIStyle.WarnColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Row(watermark.rectTransform, 1188f, 12f, 124f, 20f);
        }

        // ------------------------------------------------------------------ 工业数据台

        private static Transform BuildIndustrial(Transform root, TwinUiSettings settings)
        {
            var pageRoot = TwinUIStyle.Frame(root, IndustrialName);
            TwinUIStyle.Stretch(pageRoot, 0f, 124f, 0f, 16f);

            var host = pageRoot.gameObject.AddComponent<TwinUiIndustrialHost>();

            // 子页导航
            var nav = TwinUIStyle.Frame(pageRoot, "SubNav");
            TwinUIStyle.Row(nav, 0f, 0f, 520f, 44f);
            string[] subLabels = { TwinUITextR3.SubData, TwinUITextR3.SubEvents, TwinUITextR3.SubSettings };
            var subButtons = new TwinUIButton[3];
            for (int i = 0; i < subLabels.Length; i++)
            {
                subButtons[i] = TwinUIStyle.Button(nav, "Sub" + i, subLabels[i], TwinUIButton.Kind.Default,
                    new Vector2(i * 168f, 0f), new Vector2(160f, 40f));
                subButtons[i].SetSelected(i == 0);
            }

            // 全局状态芯片：整台面板的健康度一眼可见（同时是告警态截图的主要视觉证据）
            var chip = TwinUIStyle.Panel(pageRoot, "StatusChip", TwinUIStyle.CardAltColor);
            TwinUIStyle.Row(chip.rectTransform, PageWidth - 300f, 2f, 300f, 40f);
            TwinUIStyle.Outline(chip.gameObject, TwinUIStyle.BorderColor);
            var chipDot = TwinUIStyle.Panel(chip.rectTransform, "Dot", TwinUIStyle.OkColor);
            TwinUIStyle.Place(chipDot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(14f, 0f), new Vector2(10f, 10f));
            var chipText = TwinUIStyle.Text(chip.rectTransform, "Text", "状态 正常", 15f, TwinUIStyle.OkColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(chipText.rectTransform, 24f, 0f, 8f, 0f);

            var pages = new GameObject[3];
            pages[0] = BuildDataPage(pageRoot).gameObject;
            pages[1] = BuildEventPage(pageRoot).gameObject;
            pages[2] = BuildSettingsPage(pageRoot, settings).gameObject;

            var dataView = pages[0].GetComponent<TwinUiDataView>();
            var eventView = pages[1].GetComponent<TwinUiEventStreamView>();
            var settingsView = pages[2].GetComponent<TwinUiSettingsView>();

            var ctx = root.Find(ContextBarName);
            var ctxView = ctx == null ? null : ctx.GetComponent<TwinUiContextBarView>();
            if (ctx != null && ctxView == null)
            {
                ctxView = ctx.gameObject.AddComponent<TwinUiContextBarView>();
            }

            host.SetPages(pages, subButtons);
            host.Configure(settings, null, null, ctxView, dataView, eventView, settingsView);
            return pageRoot;
        }

        private static Transform BuildDataPage(Transform root)
        {
            var page = TwinUIStyle.Frame(root, "PageData");
            TwinUIStyle.Row(page, 0f, 56f, PageWidth, 880f);
            page.gameObject.AddComponent<TwinUiDataView>();

            BuildLinkCard(page);
            BuildJointCard(page);
            BuildWrenchCard(page);
            BuildTorqueCard(page);
            BuildPoseCard(page);
            BuildChartCard(page);
            return page;
        }

        private static void BuildLinkCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "LinkCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 0f, 0f, 620f, 560f);
            CardTitle(card.rectTransform, TwinUITextR3.SecLink, 596f);

            ColumnHeader(card.rectTransform, 42f,
                new[] { TwinUITextR3.ColItem, TwinUITextR3.ColCurrent, TwinUITextR3.ColTarget, TwinUITextR3.ColError, TwinUITextR3.ColState },
                new[] { 16f, 190f, 292f, 396f, 484f },
                new[] { 170f, 100f, 96f, 84f, 104f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Right, TextAlignmentOptions.Right,
                    TextAlignmentOptions.Right, TextAlignmentOptions.Left });

            for (int i = 0; i < 9; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 68f + i * 52f, 596f, 46f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                var bar = TwinUIStyle.Panel(row, "Bar", TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(3f, 26f));

                Cell(row, "Label", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 16f, 170f);
                Cell(row, "Current", "—", 15f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 190f, 100f, true);
                Cell(row, "Target", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 292f, 96f, true);
                Cell(row, "Error", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 396f, 84f, true);
                Cell(row, "State", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 484f, 104f);
            }

            var note = TwinUIStyle.Text(card.rectTransform, "Note", "—", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 536f, 588f, 18f);
        }

        private static void BuildJointCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "JointCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 628f, 0f, 620f, 560f);
            CardTitle(card.rectTransform, TwinUITextR3.SecJoint, 596f);

            ColumnHeader(card.rectTransform, 42f,
                new[] { TwinUITextR3.ColJoint, TwinUITextR3.ColCurrent, TwinUITextR3.ColTarget, TwinUITextR3.ColError, TwinUITextR3.ColState },
                new[] { 16f, 92f, 206f, 320f, 438f },
                new[] { 64f, 110f, 110f, 110f, 150f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Right, TextAlignmentOptions.Right,
                    TextAlignmentOptions.Right, TextAlignmentOptions.Left });

            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 68f + i * 72f, 596f, 64f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                var bar = TwinUIStyle.Panel(row, "Bar", TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(3f, 40f));

                Cell(row, "Label", TwinUITextR3.JointLabels[i], 15f, TwinUIStyle.MutedColor,
                    TextAlignmentOptions.Left, 16f, 64f, false, 16f);
                Cell(row, "Current", "—", 16f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 92f, 110f, true, 16f);
                Cell(row, "Target", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 206f, 110f, true, 16f);
                Cell(row, "Error", "—", 15f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 320f, 110f, true, 16f);
                Cell(row, "State", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 438f, 150f, false, 16f);
            }

            var note = TwinUIStyle.Text(card.rectTransform, "Note", "误差 = 实际 - 目标；目标来自 ModelTargetDisplayDeg", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 506f, 588f, 18f);
        }

        private static void BuildWrenchCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "WrenchCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 1256f, 0f, 632f, 560f);
            CardTitle(card.rectTransform, TwinUITextR3.SecWrench, 600f);

            ColumnHeader(card.rectTransform, 42f,
                new[] { TwinUITextR3.ColJoint, TwinUITextR3.ColCurrent, TwinUITextR3.ColTarget, TwinUITextR3.ColError, TwinUITextR3.ColState },
                new[] { 16f, 92f, 206f, 320f, 446f },
                new[] { 64f, 110f, 110f, 120f, 150f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Right, TextAlignmentOptions.Right,
                    TextAlignmentOptions.Right, TextAlignmentOptions.Left });

            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 68f + i * 72f, 600f, 64f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                var bar = TwinUIStyle.Panel(row, "Bar", TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(3f, 40f));

                Cell(row, "Label", TwinUITextR3.WrenchLabels[i], 15f, TwinUIStyle.MutedColor,
                    TextAlignmentOptions.Left, 16f, 64f, false, 16f);
                Cell(row, "Current", "—", 16f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 92f, 110f, true, 16f);
                Cell(row, "Target", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 206f, 110f, true, 16f);
                Cell(row, "Error", "—", 15f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 320f, 120f, true, 16f);
                Cell(row, "State", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 446f, 150f, false, 16f);
            }

            var note = TwinUIStyle.Text(card.rectTransform, "Note", "—", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 506f, 600f, 18f);
        }

        private static void BuildTorqueCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "TorqueCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 0f, 568f, 620f, 312f);
            CardTitle(card.rectTransform, TwinUITextR3.SecTorque, 596f);

            ColumnHeader(card.rectTransform, 36f,
                new[] { TwinUITextR3.ColJoint, TwinUITextR3.ColVelocity, TwinUITextR3.ColTorque, TwinUITextR3.ColState },
                new[] { 16f, 88f, 236f, 394f },
                new[] { 60f, 140f, 150f, 200f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Right, TextAlignmentOptions.Right,
                    TextAlignmentOptions.Left });

            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 60f + i * 38f, 596f, 34f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 1f, 0f, 1f);
                }

                var bar = TwinUIStyle.Panel(row, "Bar", TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(3f, 22f));

                Cell(row, "Label", TwinUITextR3.JointLabels[i], 14f, TwinUIStyle.MutedColor,
                    TextAlignmentOptions.Left, 16f, 60f, false, 3f);
                Cell(row, "Velocity", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 88f, 140f, true, 3f);
                Cell(row, "Torque", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 236f, 150f, true, 3f);
                Cell(row, "State", "—", 12f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 394f, 200f, false, 3f);
            }

            var note = TwinUIStyle.Text(card.rectTransform, "Note", "—", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 292f, 596f, 16f);
        }

        private static void BuildPoseCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "PoseCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 628f, 568f, 632f, 312f);
            CardTitle(card.rectTransform, TwinUITextR3.SecPose, 600f);

            ColumnHeader(card.rectTransform, 36f,
                new[] { TwinUITextR3.ColJoint, TwinUITextR3.ColCurrent, TwinUITextR3.ColTarget, TwinUITextR3.ColError, TwinUITextR3.ColState },
                new[] { 16f, 88f, 226f, 364f, 500f },
                new[] { 60f, 130f, 130f, 130f, 116f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Right, TextAlignmentOptions.Right,
                    TextAlignmentOptions.Right, TextAlignmentOptions.Left });

            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 60f + i * 38f, 600f, 34f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 1f, 0f, 1f);
                }

                var bar = TwinUIStyle.Panel(row, "Bar", TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(3f, 22f));

                Cell(row, "Label", TwinUITextR3.PoseLabels[i], 14f, TwinUIStyle.MutedColor,
                    TextAlignmentOptions.Left, 16f, 60f, false, 3f);
                Cell(row, "Current", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 88f, 130f, true, 3f);
                Cell(row, "Target", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 226f, 130f, true, 3f);
                Cell(row, "Error", "—", 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Right, 364f, 130f, true, 3f);
                Cell(row, "State", "—", 12f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 500f, 116f, false, 3f);
            }
        }

        private static void BuildChartCard(Transform page)
        {
            var card = TwinUIStyle.Card(page, "ChartCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 1268f, 568f, 620f, 312f);
            CardTitle(card.rectTransform, TwinUITextR3.SecChart, 596f);

            var go = new GameObject("Chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(TwinTelemetryChart));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(card.rectTransform, false);
            TwinUIStyle.Row(rt, 16f, 40f, 588f, 210f);
            var chart = go.GetComponent<TwinTelemetryChart>();
            chart.SetSeries(new[]
            {
                TwinTelemetryChannel.Joint1, TwinTelemetryChannel.Joint2, TwinTelemetryChannel.Joint3,
                TwinTelemetryChannel.Joint4, TwinTelemetryChannel.Joint5, TwinTelemetryChannel.Joint6
            });
            chart.SetWindowSeconds(20f);
            chart.SetTargetLine(true, 0f);
            chart.SetThresholdBand(true, 0.5f, 1.0f, true, 0f);
            chart.ApplyTheme();

            var note = TwinUIStyle.Text(card.rectTransform, "Note", TwinUITextR3.ChartNote, TwinUIStyle.FontCaption,
                TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 254f, 588f, 20f);
        }

        // ------------------------------------------------------------------ 事件流页

        private static Transform BuildEventPage(Transform root)
        {
            var page = TwinUIStyle.Frame(root, "PageEvents");
            TwinUIStyle.Row(page, 0f, 56f, PageWidth, 880f);
            page.gameObject.AddComponent<TwinUiEventStreamView>();

            var list = TwinUIStyle.Card(page, "EventList", TwinUIStyle.CardColor);
            TwinUIStyle.Row(list.rectTransform, 0f, 0f, 1250f, 880f);
            CardTitle(list.rectTransform, TwinUITextR3.SecEventStream, 1222f);
            ColumnHeader(list.rectTransform, 44f,
                new[] { TwinUITextR3.ColTime, TwinUITextR3.ColLevel, TwinUITextR3.ColCode, TwinUITextR3.ColText },
                new[] { 16f, 132f, 214f, 388f },
                new[] { 110f, 78f, 168f, 836f },
                new[] { TextAlignmentOptions.Left, TextAlignmentOptions.Left, TextAlignmentOptions.Left,
                    TextAlignmentOptions.Left });

            for (int i = 0; i < 14; i++)
            {
                var row = TwinUIStyle.Frame(list.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 68f + i * 54f, 1222f, 50f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                Cell(row, "Time", string.Empty, 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 16f, 110f, true, 9f);
                Cell(row, "Level", string.Empty, 12f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 132f, 78f, false, 9f);
                Cell(row, "Code", string.Empty, 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 214f, 168f, false, 9f);
                Cell(row, "Text", string.Empty, 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Left, 388f, 830f, false, 9f);
            }

            var summary = TwinUIStyle.Card(page, "SummaryCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(summary.rectTransform, 1258f, 0f, 630f, 880f);
            CardTitle(summary.rectTransform, TwinUITextR3.SecAlertSummary, 600f);

            for (int i = 0; i < 12; i++)
            {
                var row = TwinUIStyle.Frame(summary.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 12f, 68f + i * 60f, 606f, 56f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                Cell(row, "Label", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 16f, 300f, false, 13f);
                Cell(row, "Value", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Right, 320f, 274f, true, 13f);
            }

            var note = TwinUIStyle.Text(summary.rectTransform, "Note", "—", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 800f, 600f, 40f);
            note.enableWordWrapping = true;
            return page;
        }

        // ------------------------------------------------------------------ 设置页

        private static Transform BuildSettingsPage(Transform root, TwinUiSettings settings)
        {
            var page = TwinUIStyle.Frame(root, "PageSettings");
            TwinUIStyle.Row(page, 0f, 56f, PageWidth, 880f);
            page.gameObject.AddComponent<TwinUiSettingsView>();

            // 刷新频率
            var refresh = TwinUIStyle.Card(page, "RefreshCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(refresh.rectTransform, 0f, 0f, 620f, 420f);
            CardTitle(refresh.rectTransform, TwinUITextR3.SecRefresh, 596f);
            for (int i = 0; i < TwinUITextR3.UiHzOptions.Length; i++)
            {
                float hz = TwinUITextR3.UiHzOptions[i];
                var b = TwinUIStyle.Button(refresh.rectTransform, "Hz" + hz.ToString("F0"), hz.ToString("F0"),
                    TwinUIButton.Kind.Default, new Vector2(16f + i * 116f, 56f), new Vector2(100f, 40f));
                b.SetEmphasiseSelected(true);
            }

            BuildInfoRow(refresh.rectTransform, "Info0", TwinUITextR3.RowUiHzValue, 112f);
            BuildInfoRow(refresh.rectTransform, "Info1", TwinUITextR3.RowExportHz, 148f);
            BuildInfoRow(refresh.rectTransform, "Info2", TwinUITextR3.RowRobotApplyHz, 184f);

            var save = TwinUIStyle.Button(refresh.rectTransform, "BtnSave", TwinUITextR3.BtnSave,
                TwinUIButton.Kind.Primary, new Vector2(16f, 236f), new Vector2(220f, 44f));
            save.SetEmphasiseSelected(false);

            var saveState = TwinUIStyle.Text(refresh.rectTransform, "SaveState", "—", TwinUIStyle.FontCaption,
                TwinUIStyle.MutedColor);
            TwinUIStyle.Row(saveState.rectTransform, 16f, 290f, 588f, 40f);
            saveState.enableWordWrapping = true;

            // 主题与密度
            var theme = TwinUIStyle.Card(page, "ThemeCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(theme.rectTransform, 628f, 0f, 620f, 420f);
            CardTitle(theme.rectTransform, TwinUITextR3.SecTheme, 596f);
            TwinUIStyle.Button(theme.rectTransform, "BtnThemeDark", TwinUITextR3.BtnThemeDark,
                TwinUIButton.Kind.Default, new Vector2(16f, 56f), new Vector2(180f, 44f));
            TwinUIStyle.Button(theme.rectTransform, "BtnThemeLight", TwinUITextR3.BtnThemeLight,
                TwinUIButton.Kind.Default, new Vector2(206f, 56f), new Vector2(180f, 44f));
            TwinUIStyle.Button(theme.rectTransform, "BtnDensityComfort", TwinUITextR3.BtnDensityComfort,
                TwinUIButton.Kind.Default, new Vector2(16f, 116f), new Vector2(140f, 40f));
            TwinUIStyle.Button(theme.rectTransform, "BtnDensityCompact", TwinUITextR3.BtnDensityCompact,
                TwinUIButton.Kind.Default, new Vector2(166f, 116f), new Vector2(140f, 40f));
            BuildInfoRow(theme.rectTransform, "Info0", TwinUITextR3.RowThemeValue, 176f);
            BuildInfoRow(theme.rectTransform, "Info1", TwinUITextR3.RowDensityValue, 212f);

            var themeNote = TwinUIStyle.Text(theme.rectTransform, "Note",
                "浅色保留为可切换项；发光仅用于状态/选中", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(themeNote.rectTransform, 16f, 258f, 588f, 40f);
            themeNote.enableWordWrapping = true;

            // 数据源
            var source = TwinUIStyle.Card(page, "SourceCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(source.rectTransform, 1256f, 0f, 632f, 420f);
            CardTitle(source.rectTransform, TwinUITextR3.SecSource, 600f);
            TwinUIStyle.Button(source.rectTransform, "BtnSourceRuntime", TwinUITextR3.BtnSourceRuntime,
                TwinUIButton.Kind.Default, new Vector2(16f, 56f), new Vector2(150f, 44f));
            TwinUIStyle.Button(source.rectTransform, "BtnSourceReplay", TwinUITextR3.BtnSourceReplay,
                TwinUIButton.Kind.Default, new Vector2(176f, 56f), new Vector2(150f, 44f));
            TwinUIStyle.Button(source.rectTransform, "BtnSourceMock", TwinUITextR3.BtnSourceMock,
                TwinUIButton.Kind.Danger, new Vector2(336f, 56f), new Vector2(150f, 44f));
            BuildInfoRow(source.rectTransform, "Info0", TwinUITextR3.RowSourceValue, 116f);
            BuildInfoRow(source.rectTransform, "Info1", TwinUITextR3.RowReplayPath, 152f, 56f);
            var sourceNote = TwinUIStyle.Text(source.rectTransform, "Note",
                "回放只读 PaperLogs/*/samples.csv；演示源必须显示 MOCK 水印", TwinUIStyle.FontCaption,
                TwinUIStyle.MutedColor);
            TwinUIStyle.Row(sourceNote.rectTransform, 16f, 216f, 600f, 36f);
            sourceNote.enableWordWrapping = true;

            // 阈值组
            var threshold = TwinUIStyle.Card(page, "ThresholdCard", TwinUIStyle.CardColor);
            TwinUIStyle.Row(threshold.rectTransform, 0f, 428f, PageWidth, 452f);
            CardTitle(threshold.rectTransform, TwinUITextR3.SecThresholds, PageWidth - 32f);
            var headA = TwinUIStyle.Text(threshold.rectTransform, "HeadA", "阈值项", 12f, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(headA.rectTransform, 12f, 40f, 320f, 16f);
            var headB = TwinUIStyle.Text(threshold.rectTransform, "HeadB", "黄 / 红 门限（待校准）", 12f,
                TwinUIStyle.WarnColor);
            TwinUIStyle.Row(headB.rectTransform, 340f, 40f, 300f, 16f);

            for (int i = 0; i < 12; i++)
            {
                int col = i / 6;
                int line = i % 6;
                float x = col == 0 ? 16f : 950f;
                var row = TwinUIStyle.Frame(threshold.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, x, 56f + line * 56f, 620f, 52f);
                if ((i & 1) == 0)
                {
                    var bg = TwinUIStyle.Panel(row, "Bg", TwinUIStyle.CardAltColor);
                    TwinUIStyle.Stretch(bg.rectTransform, 0f, 2f, 0f, 2f);
                }

                Cell(row, "Label", "—", 14f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left, 12f, 320f, false, 10f);
                Cell(row, "Value", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Left, 340f, 264f, true, 10f);
            }

            var note = TwinUIStyle.Text(threshold.rectTransform, "Note", TwinUITextR3.NoteCalibrated,
                TwinUIStyle.FontCaption, TwinUIStyle.WarnColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 396f, 1200f, 22f);
            return page;
        }

        // ------------------------------------------------------------------ 小工具

        private static void CardTitle(RectTransform card, string text, float width)
        {
            var title = TwinUIStyle.Text(card, "Title", text, TwinUIStyle.FontSection, TwinUIStyle.TextColor);
            TwinUIStyle.Row(title.rectTransform, 16f, 12f, width, 28f);
        }

        private static void ColumnHeader(RectTransform card, float y, string[] labels, float[] xs, float[] widths,
            TextAlignmentOptions[] aligns)
        {
            var head = TwinUIStyle.Frame(card, "ColHead");
            TwinUIStyle.Row(head, 12f, y, widths[0] + xs[xs.Length - 1] + 8f, 20f);
            for (int i = 0; i < labels.Length; i++)
            {
                var t = TwinUIStyle.Text(head, "C" + i, labels[i], 12f, TwinUIStyle.MutedColor, aligns[i]);
                TwinUIStyle.Row(t.rectTransform, xs[i] - 12f, 0f, widths[i], 20f);
            }
        }

        private static TMP_Text Cell(RectTransform row, string name, string text, float size, Color color,
            TextAlignmentOptions align, float x, float width, bool mono = false, float y = 7f)
        {
            var t = mono
                ? TwinUIStyle.Mono(row, name, text, size, color, align)
                : TwinUIStyle.Text(row, name, text, size, color, align);
            TwinUIStyle.Row(t.rectTransform, x, y, width, 32f);
            return t;
        }

        private static void BuildInfoRow(RectTransform card, string name, string label, float y, float height = 32f)
        {
            var row = TwinUIStyle.Frame(card, name);
            TwinUIStyle.Row(row, 16f, y, 588f, height);
            var l = TwinUIStyle.Text(row, "Label", label, 13f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Row(l.rectTransform, 0f, 5f, 240f, 22f);
            var v = TwinUIStyle.Text(row, "Value", "—", 14f, TwinUIStyle.TextColor, TextAlignmentOptions.Right);
            TwinUIStyle.Row(v.rectTransform, 244f, 5f, 344f, 22f);
            v.enableWordWrapping = height > 40f;
        }

        // ------------------------------------------------------------------ 接线

        public static TwinUiSettings LoadSettings(bool createIfMissing)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TwinUiSettings>(SettingsAssetPath);
            if (settings != null)
            {
                return settings;
            }

            if (!createIfMissing)
            {
                return null;
            }

            string dir = Path.GetDirectoryName(SettingsAssetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            settings = ScriptableObject.CreateInstance<TwinUiSettings>();
            settings.thresholds.ResetToDefaults();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UI R3] created settings asset: " + SettingsAssetPath);
            return settings;
        }

        public static void Wire(Transform root, TwinUiSettings settings)
        {
            if (settings == null)
            {
                settings = LoadSettings(true);
            }

            var binder = root.GetComponent<TwinUIStatusBinder>();
            var runtimeGo = GameObject.Find(RuntimePath);
            var runtime = runtimeGo != null ? runtimeGo.GetComponent<DigitalTwinRuntime>() : null;

            if (binder == null)
            {
                binder = root.gameObject.AddComponent<TwinUIStatusBinder>();
            }

            binder.Bind(runtime, runtime != null ? runtime.CommandController : null,
                runtime != null ? runtime.Recorder : null);

            var applier = root.GetComponent<TwinUIThemeApplier>();
            if (applier == null)
            {
                applier = root.gameObject.AddComponent<TwinUIThemeApplier>();
            }

            if (TwinUITheme.Mode != (settings != null ? settings.themeMode : TwinUIThemeMode.Dark))
            {
                TwinUITheme.SetMode(settings != null ? settings.themeMode : TwinUIThemeMode.Dark);
            }

            applier.Apply();

            var industrial = root.Find(IndustrialName);
            if (industrial == null)
            {
                return;
            }

            var host = industrial.GetComponent<TwinUiIndustrialHost>();
            var ctx = root.Find(ContextBarName);
            var ctxView = ctx == null ? null : ctx.GetComponent<TwinUiContextBarView>();
            var dataView = industrial.Find("PageData") != null
                ? industrial.Find("PageData").GetComponent<TwinUiDataView>() : null;
            var eventView = industrial.Find("PageEvents") != null
                ? industrial.Find("PageEvents").GetComponent<TwinUiEventStreamView>() : null;
            var settingsView = industrial.Find("PageSettings") != null
                ? industrial.Find("PageSettings").GetComponent<TwinUiSettingsView>() : null;

            if (host != null)
            {
                host.Configure(settings, binder, runtime, ctxView, dataView, eventView, settingsView);
                var pages = new GameObject[3];
                var subs = new TwinUIButton[3];
                string[] pageNames = { "PageData", "PageEvents", "PageSettings" };
                for (int i = 0; i < 3; i++)
                {
                    var p = industrial.Find(pageNames[i]);
                    pages[i] = p == null ? null : p.gameObject;
                    var s = industrial.Find("SubNav/Sub" + i);
                    subs[i] = s == null ? null : s.GetComponent<TwinUIButton>();
                }

                host.SetPages(pages, subs);
                host.ShowSub(0);
            }

            var settingsUi = industrial.GetComponentInChildren<TwinUiSettingsView>(true);
            if (settingsUi != null)
            {
                settingsUi.Bind(settings, host);
            }

            var tabHost = root.GetComponent<TwinUIPageHost>();
            if (tabHost != null)
            {
                var mission = root.Find("MissionRoot");
                var ops = root.Find("TwinOpsRoot");
                var header = root.Find("Header");
                var headerView = header == null ? null : header.GetComponent<TwinUIHeaderView>();
                var missionView = mission == null ? null : mission.GetComponent<TwinUIMissionView>();
                var opsView = ops == null ? null : ops.GetComponent<TwinUITwinOpsView>();
                var missionTab = root.Find("TabBar/BtnMission");
                var opsTab = root.Find("TabBar/BtnTwinOps");
                var industrialTab = root.Find("TabBar/BtnIndustrial");
                tabHost.Configure(mission == null ? null : mission.gameObject,
                    ops == null ? null : ops.gameObject,
                    industrial == null ? null : industrial.gameObject,
                    headerView, missionView, opsView, host, binder,
                    missionTab == null ? null : missionTab.GetComponent<TwinUIButton>(),
                    opsTab == null ? null : opsTab.GetComponent<TwinUIButton>(),
                    industrialTab == null ? null : industrialTab.GetComponent<TwinUIButton>());
                tabHost.Wire();
            }
        }

        private static void SaveAndLog(string mode)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            var root = GameObject.Find(GroupPath + "/" + RootName);
            var industrial = root == null ? null : root.transform.Find(IndustrialName);
            int count = industrial == null ? 0 : industrial.GetComponentsInChildren<Transform>(true).Length;
            var settings = LoadSettings(false);
            string line = "[R3] mode=" + mode + " industrial=" + (industrial != null ? "ok" : "MISSING")
                + " transforms=" + count + " settings=" + (settings != null ? "ok" : "missing")
                + " uiHz=" + (settings != null ? settings.uiRefreshHz : 0f)
                + " theme=" + (settings != null ? settings.themeMode : TwinUIThemeMode.Dark)
                + " result=PASS";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ui_r3_build.txt", line);
            Debug.Log("[UI R3] " + line);
        }
    }
}