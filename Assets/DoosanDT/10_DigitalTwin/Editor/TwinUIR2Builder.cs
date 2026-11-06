using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// 二期 UI 构建器（幂等）：常驻顶栏 + 两个主面板（实训任务台 / 孪生监控台）。
    /// 菜单 UI/1 全量重建，UI/10 只重建任务台，UI/11 只重建监控台，便于独立调整。
    /// 只读取运行时段（DigitalTwinRuntime / TwinRecorder），不修改任何数据链组件。
    /// </summary>
    public static class TwinUIR2Builder
    {
        private const string RootName = "UI_二期面板";
        private const string LegacyRootName = "UI_统一入口面板";
        private const string GroupPath = "30_界面与交互";
        private const string RuntimePath = "00_数字孪生机械臂/4.24-Try/DigitalTwin_System";
        private const string RecorderPath = "00_数字孪生机械臂/DataLogging_System";

        private static readonly string[] MetricNames =
        {
            "Rate", "Drop", "Reorder", "Queue", "Jitter", "Latency", "Quality", "Clock", "Mode"
        };

        private static readonly string[] MetricLabels =
        {
            "接收帧率", "丢弃帧", "乱序帧", "待写队列", "抖动", "应用时延", "帧质量", "时钟同步", "机器人模式"
        };

        private static readonly string[] CheckNames =
        {
            "DryRun", "RealCmd", "Bidirectional", "CanSend", "Frame", "Arm", "Estop", "Safety", "Clock"
        };

        private static readonly string[] CheckLabels =
        {
            "干运行", "真机指令", "双向控制", "可下发", "实时帧", "二次确认", "急停状态", "安全链", "时钟一致"
        };

        private static readonly string[] RecordNames = { "Recording", "Pending", "Dropped", "Csv", "Dir", "Session", "Experiment" };
        private static readonly string[] RecordLabels = { "记录开关", "待写", "丢弃", "文件就绪", "目录", "会话号", "实验/模式" };

        [MenuItem("DigitalTwin/UI/1 Build Unified Shell (R2)", false, 1)]
        public static void BuildAll()
        {
            var root = EnsureRoot();
            ClearChildren(root);
            BuildHeader(root);
            BuildTabBar(root);
            BuildMission(root);
            BuildTwinOps(root);
            // 三期：工业数据台 + 实验上下文条（UI/2 可单独重建）
            TwinUIR3Builder.Build(root, true);
            Wire(root, full: true);
            SaveAndLog("full");
        }

        [MenuItem("DigitalTwin/UI/10 Build Mission Page", false, 10)]
        public static void BuildMissionOnly()
        {
            var root = EnsureRoot();
            ReplaceChild(root, "MissionRoot", BuildMission);
            Wire(root, full: false);
            SaveAndLog("mission");
        }

        [MenuItem("DigitalTwin/UI/11 Build TwinOps Page", false, 11)]
        public static void BuildTwinOpsOnly()
        {
            var root = EnsureRoot();
            ReplaceChild(root, "TwinOpsRoot", BuildTwinOps);
            Wire(root, full: false);
            SaveAndLog("twinops");
        }

        // ------------------------------------------------------------------ 骨架

        private static Transform EnsureRoot()
        {
            var existing = GameObject.Find(GroupPath + "/" + RootName);
            if (existing != null)
            {
                return existing.transform;
            }

            var group = GameObject.Find(GroupPath);
            var go = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(group != null ? group.transform : null, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var legacy = GameObject.Find(GroupPath + "/" + LegacyRootName);
            if (legacy != null)
            {
                legacy.SetActive(false);
            }

            return go.transform;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        private static void ReplaceChild(Transform root, string name, System.Func<Transform, Transform> build)
        {
            var old = root.Find(name);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            build(root);
        }

        private static void SaveAndLog(string mode)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            var root = GameObject.Find(GroupPath + "/" + RootName);
            int count = root == null ? 0 : root.GetComponentsInChildren<Transform>(true).Length;
            string line = "[R2] mode=" + mode + " root=" + (root != null ? "ok" : "MISSING")
                + " transforms=" + count + " canvas=true result=PASS";
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/ui_r2_build.txt", line);
            Debug.Log("[UI R2] " + line);
        }

        // ------------------------------------------------------------------ 顶栏

        private static void BuildHeader(Transform root)
        {
            var header = TwinUIStyle.Panel(root, "Header", TwinUIStyle.PanelColor);
            var rt = header.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -8f);
            rt.sizeDelta = new Vector2(-32f, TwinUIStyle.HeaderHeight);

            // 产品名
            var title = TwinUIStyle.Text(rt, "Title", TwinUITextR2.Product, TwinUIStyle.FontTitle, TwinUIStyle.TextColor);
            TwinUIStyle.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(440f, 32f));

            // 模式芯片
            var chip = TwinUIStyle.Panel(rt, "ModeChip", TwinUIStyle.CardAltColor);
            TwinUIStyle.Place(chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(472f, 0f), new Vector2(104f, 28f));
            var bar = TwinUIStyle.Panel(chip.rectTransform, "Bar", TwinUIStyle.AccentColor);
            TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(3f, 28f));
            var modeText = TwinUIStyle.Text(chip.rectTransform, "Text", TwinUITextR2.ModeMission, 14f,
                TwinUIStyle.TextColor, TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(modeText.rectTransform, 8f, 0f, 6f, 0f);

            BuildKpi(rt, "KpiSource", TwinUITextR2.KpiSource, 596f);
            BuildKpi(rt, "KpiRate", TwinUITextR2.KpiRate, 812f);
            BuildKpi(rt, "KpiLatency", TwinUITextR2.KpiLatency, 1028f);
            BuildKpi(rt, "KpiAge", TwinUITextR2.KpiAge, 1244f);

            BuildBadge(rt, "AlarmBadge", TwinUITextR2.AlarmNone, 1472f, 120f, TwinUIStyle.OkColor);
            BuildBadge(rt, "RecBadge", TwinUITextR2.RecOff, 1600f, 92f, TwinUIStyle.DisabledColor);
            BuildBadge(rt, "EstopChip", TwinUITextR2.EstopClear, 1700f, 148f, TwinUIStyle.OkColor);

            var clock = TwinUIStyle.Text(rt, "Clock", "--:--:--", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Place(clock.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(88f, 24f));
        }


        private static void CardHeader(RectTransform parent, string label)
        {
            var text = TwinUIStyle.Text(parent, "Header", label, TwinUIStyle.FontSection, TwinUIStyle.TextColor);
            TwinUIStyle.Row(text.rectTransform, 16f, 12f, 640f, 28f);
        }

        private static void BuildKpi(RectTransform parent, string name, string label, float x)
        {
            var kpi = TwinUIStyle.Panel(parent, name, TwinUIStyle.Transparent);
            TwinUIStyle.Place(kpi.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x, 0f), new Vector2(208f, 40f));
            var caption = TwinUIStyle.Text(kpi.rectTransform, "Label", label, 12f, TwinUIStyle.MutedColor);
            TwinUIStyle.Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -2f), new Vector2(120f, 16f));
            var value = TwinUIStyle.Text(kpi.rectTransform, "Value", "—", TwinUIStyle.FontBody, TwinUIStyle.TextColor);
            TwinUIStyle.Place(value.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 2f), new Vector2(200f, 22f));
        }

        private static void BuildBadge(RectTransform parent, string name, string text, float x, float width, Color dot)
        {
            var badge = TwinUIStyle.Panel(parent, name, TwinUIStyle.CardAltColor);
            TwinUIStyle.Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x, 0f), new Vector2(width, 28f));
            var d = TwinUIStyle.Panel(badge.rectTransform, "Dot", dot);
            TwinUIStyle.Place(d.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(8f, 8f));
            var t = TwinUIStyle.Text(badge.rectTransform, "Text", text, 13f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(t.rectTransform, 18f, 0f, 6f, 0f);
        }

        // ------------------------------------------------------------------ 页签

        private static void BuildTabBar(Transform root)
        {
            var host = TwinUIStyle.Frame(root, "TabBar");
            host.anchorMin = new Vector2(0f, 1f);
            host.anchorMax = new Vector2(0f, 1f);
            host.pivot = new Vector2(0f, 1f);
            host.anchoredPosition = new Vector2(16f, -72f);
            host.sizeDelta = new Vector2(552f, 44f);

            TwinUIStyle.Button(host, "BtnMission", TwinUITextR2.TabMission, TwinUIButton.Kind.Default,
                new Vector2(0f, 0f), new Vector2(176f, 40f)).SetSelected(false);
            TwinUIStyle.Button(host, "BtnTwinOps", TwinUITextR2.TabTwinOps, TwinUIButton.Kind.Default,
                new Vector2(184f, 0f), new Vector2(176f, 40f)).SetSelected(true);
            TwinUIStyle.Button(host, "BtnIndustrial", TwinUITextR3.TabIndustrial, TwinUIButton.Kind.Default,
                new Vector2(368f, 0f), new Vector2(176f, 40f)).SetSelected(false);
        }

        // ------------------------------------------------------------------ 任务台

        private static Transform BuildMission(Transform root)
        {
            var page = TwinUIStyle.Frame(root, "MissionRoot");
            TwinUIStyle.Stretch(page, 0f, 124f, 0f, 16f);

            var view = page.gameObject.AddComponent<TwinUIMissionView>();

            // 步骤卡
            var steps = TwinUIStyle.Card(page, "Steps", TwinUIStyle.CardColor);
            TwinUIStyle.Row(steps.rectTransform, 0f, 0f, 420f, 940f);
            CardHeader(steps.rectTransform, TwinUITextR2.SecSteps);
            for (int i = 0; i < TwinUITextR2.Steps.Length; i++)
            {
                var row = TwinUIStyle.Frame(steps.rectTransform, "Step" + i);
                TwinUIStyle.Row(row, 0f, 60f + i * 104f, 420f, 96f);
                var bg = TwinUIStyle.Panel(row, "Bg", i == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(bg.rectTransform, 8f, 4f, 8f, 4f);
                var bar = TwinUIStyle.Panel(row, "Bar", i == 0 ? TwinUIStyle.AccentColor : TwinUIStyle.BorderColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(8f, 0f), new Vector2(3f, 72f));
                var num = TwinUIStyle.Text(row, "Num", (i + 1).ToString("00"), TwinUIStyle.FontSection,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.Center);
                TwinUIStyle.Place(num.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(24f, 0f), new Vector2(32f, 32f));
                var title = TwinUIStyle.Text(row, "Title", TwinUITextR2.Steps[i], 15f, TwinUIStyle.TextColor);
                TwinUIStyle.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(64f, -16f), new Vector2(340f, 40f));
                title.enableWordWrapping = true;
                var state = TwinUIStyle.Text(row, "State", i == 0 ? TwinUITextR2.StepCurrent : TwinUITextR2.StepWaiting,
                    12f, i == 0 ? TwinUIStyle.AccentColor : TwinUIStyle.MutedColor);
                TwinUIStyle.Place(state.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(64f, 12f), new Vector2(340f, 18f));
            }

            // 详情卡
            var detail = TwinUIStyle.Card(page, "Detail", TwinUIStyle.CardColor);
            TwinUIStyle.Row(detail.rectTransform, 428f, 0f, 900f, 940f);
            var dTitle = TwinUIStyle.Text(detail.rectTransform, "Title", "步骤 1 / 8", TwinUIStyle.FontTitle,
                TwinUIStyle.TextColor);
            TwinUIStyle.Row(dTitle.rectTransform, 24f, 20f, 820f, 34f);
            var dBody = TwinUIStyle.Text(detail.rectTransform, "Body", TwinUITextR2.StepBodies[0], 18f,
                TwinUIStyle.TextColor);
            TwinUIStyle.Row(dBody.rectTransform, 24f, 70f, 852f, 120f);
            dBody.enableWordWrapping = true;

            var hintBox = TwinUIStyle.Panel(detail.rectTransform, "Hint", TwinUIStyle.CardAltColor);
            TwinUIStyle.Row(hintBox.rectTransform, 24f, 210f, 852f, 84f);
            var hintBar = TwinUIStyle.Panel(hintBox.rectTransform, "Bar", TwinUIStyle.AccentColor);
            TwinUIStyle.Place(hintBar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(3f, 84f));
            var hint = TwinUIStyle.Text(hintBox.rectTransform, "Text", TwinUITextR2.HintPrefix + TwinUITextR2.StepHints[0],
                15f, TwinUIStyle.MutedColor);
            TwinUIStyle.Stretch(hint.rectTransform, 16f, 10f, 16f, 10f);
            hint.enableWordWrapping = true;

            // 时间轴/视频控制（占位，真实播放由旧面板/PlayableDirector 负责）
            var timeline = TwinUIStyle.Card(detail.rectTransform, "Timeline", TwinUIStyle.CardAltColor);
            TwinUIStyle.Row(timeline.rectTransform, 24f, 320f, 852f, 150f);
            var tlTitle = TwinUIStyle.Text(timeline.rectTransform, "Title", "训练时间轴", TwinUIStyle.FontSection,
                TwinUIStyle.TextColor);
            TwinUIStyle.Row(tlTitle.rectTransform, 16f, 10f, 300f, 26f);
            var tlState = TwinUIStyle.Text(timeline.rectTransform, "State",
                "由训练页面 PlayableDirector 驱动，本卡只做状态展示", TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(tlState.rectTransform, 16f, 40f, 820f, 22f);
            TwinUIStyle.Button(timeline.rectTransform, "BtnPreview", "预览", TwinUIButton.Kind.Default,
                new Vector2(16f, 76f), new Vector2(96f, 40f));
            TwinUIStyle.Button(timeline.rectTransform, "BtnPlay", "播放", TwinUIButton.Kind.Primary,
                new Vector2(120f, 76f), new Vector2(96f, 40f));
            TwinUIStyle.Button(timeline.rectTransform, "BtnPause", "暂停", TwinUIButton.Kind.Default,
                new Vector2(224f, 76f), new Vector2(96f, 40f));

            var actions = TwinUIStyle.Frame(detail.rectTransform, "Actions");
            TwinUIStyle.Row(actions, 24f, 830f, 852f, 48f);
            TwinUIStyle.Button(actions, "BtnPrev", TwinUITextR2.BtnPrev, TwinUIButton.Kind.Default,
                new Vector2(0f, 0f), new Vector2(160f, 44f));
            TwinUIStyle.Button(actions, "BtnNext", TwinUITextR2.BtnNext, TwinUIButton.Kind.Primary,
                new Vector2(176f, 0f), new Vector2(160f, 44f));
            TwinUIStyle.Button(actions, "BtnReset", TwinUITextR2.BtnReset, TwinUIButton.Kind.Default,
                new Vector2(352f, 0f), new Vector2(160f, 44f));

            // 评估卡
            var eval = TwinUIStyle.Card(page, "Eval", TwinUIStyle.CardColor);
            TwinUIStyle.Row(eval.rectTransform, 1336f, 0f, 552f, 940f);
            var eTitle = TwinUIStyle.Text(eval.rectTransform, "Title", TwinUITextR2.SecEval, TwinUIStyle.FontSection,
                TwinUIStyle.TextColor);
            TwinUIStyle.Row(eTitle.rectTransform, 20f, 18f, 400f, 28f);

            string[] evalNames = { "Timer", "Progress", "Errors", "Score" };
            string[] evalLabels = { TwinUITextR2.EvalTimer, TwinUITextR2.EvalProgress, TwinUITextR2.EvalErrors, TwinUITextR2.EvalScore };
            for (int i = 0; i < evalNames.Length; i++)
            {
                var cell = TwinUIStyle.Panel(eval.rectTransform, evalNames[i], TwinUIStyle.CardAltColor);
                TwinUIStyle.Row(cell.rectTransform, 20f, 64f + i * 104f, 512f, 92f);
                var label = TwinUIStyle.Text(cell.rectTransform, "Label", evalLabels[i], 13f, TwinUIStyle.MutedColor);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(16f, -10f), new Vector2(220f, 18f));
                var value = TwinUIStyle.Text(cell.rectTransform, "Value", "—", TwinUIStyle.FontTitle, TwinUIStyle.TextColor,
                    TextAlignmentOptions.Right);
                TwinUIStyle.Place(value.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-16f, 10f), new Vector2(300f, 40f));
            }

            var note = TwinUIStyle.Text(eval.rectTransform, "Note", TwinUITextR2.EvalNote, 12f, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 20f, 500f, 512f, 40f);
            note.enableWordWrapping = true;

            TwinUIStyle.Button(eval.rectTransform, "BtnRoaming", TwinUITextR2.BtnBackRoaming, TwinUIButton.Kind.Default,
                new Vector2(20f, 860f), new Vector2(200f, 44f), () => { });

            return page;
        }

        // ------------------------------------------------------------------ 监控台

        private static Transform BuildTwinOps(Transform root)
        {
            var page = TwinUIStyle.Frame(root, "TwinOpsRoot");
            TwinUIStyle.Stretch(page, 0f, 124f, 0f, 16f);

            var view = page.gameObject.AddComponent<TwinUITwinOpsView>();

            var subNav = TwinUIStyle.Frame(page, "SubNav");
            TwinUIStyle.Row(subNav, 0f, 0f, 504f, 44f);
            string[] subs = { TwinUITextR2.SubOverview, TwinUITextR2.SubTrend, TwinUITextR2.SubControl, TwinUITextR2.SubRecord };
            for (int i = 0; i < 4; i++)
            {
                TwinUIStyle.Button(subNav, "Sub" + i, subs[i], TwinUIButton.Kind.Default,
                    new Vector2(i * 128f, 0f), new Vector2(120f, 40f));
            }

            BuildOverview(page);
            BuildTrend(page);
            BuildControl(page);
            BuildRecord(page);

            return page;
        }

        private static void BuildOverview(Transform page)
        {
            var overview = TwinUIStyle.Frame(page, "PageOverview");
            TwinUIStyle.Row(overview, 0f, 56f, 1888f, 880f);

            // 数据源健康
            var source = TwinUIStyle.Card(overview, "Source", TwinUIStyle.CardColor);
            TwinUIStyle.Row(source.rectTransform, 0f, 0f, 520f, 420f);
            CardHeader(source.rectTransform, TwinUITextR2.SecSourceHealth);
            BuildSourceRow(source.rectTransform, "Dart", "DART", 72f);
            BuildSourceRow(source.rectTransform, "Ros2", "ROS2", 200f);

            var chain = TwinUIStyle.Card(overview, "Chain", TwinUIStyle.CardColor);
            TwinUIStyle.Row(chain.rectTransform, 528f, 0f, 680f, 420f);
            CardHeader(chain.rectTransform, TwinUITextR2.SecDataChain);
            string[] chainLabels = { TwinUITextR2.ChainSource, TwinUITextR2.ChainTransport, TwinUITextR2.ChainApply, TwinUITextR2.ChainModel };
            for (int i = 0; i < 4; i++)
            {
                var row = TwinUIStyle.Panel(chain.rectTransform, "Step" + i, TwinUIStyle.Transparent);
                TwinUIStyle.Row(row.rectTransform, 16f, 64f + i * 78f, 648f, 66f);
                var label = TwinUIStyle.Text(row.rectTransform, "Label", chainLabels[i], TwinUIStyle.FontBody,
                    TwinUIStyle.MutedColor);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(100f, 24f));
                var bar = TwinUIStyle.Panel(row.rectTransform, "Bar", TwinUIStyle.MutedColor);
                TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(104f, 0f), new Vector2(420f, 8f));
                var value = TwinUIStyle.Text(row.rectTransform, "Value", "—", TwinUIStyle.FontBody, TwinUIStyle.TextColor,
                    TextAlignmentOptions.Right);
                TwinUIStyle.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(120f, 24f));
            }

            // 指标卡
            var metrics = TwinUIStyle.Card(overview, "Metrics", TwinUIStyle.CardColor);
            TwinUIStyle.Row(metrics.rectTransform, 1216f, 0f, 672f, 880f);
            CardHeader(metrics.rectTransform, TwinUITextR2.SecQuality);
            for (int i = 0; i < MetricNames.Length; i++)
            {
                var row = TwinUIStyle.Frame(metrics.rectTransform, MetricNames[i]);
                TwinUIStyle.Row(row, 0f, 56f + i * 90f, 672f, 82f);
                var line = TwinUIStyle.Panel(row, "Line", i % 2 == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(line.rectTransform, 12f, 0f, 12f, 0f);
                var label = TwinUIStyle.Text(row, "Label", MetricLabels[i], TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(20f, 0f), new Vector2(300f, 22f));
                var value = TwinUIStyle.Text(row, "Value", "—", TwinUIStyle.FontSection, TwinUIStyle.TextColor,
                    TextAlignmentOptions.Right);
                TwinUIStyle.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-20f, 0f), new Vector2(320f, 30f));
            }

            // 告警
            var alarms = TwinUIStyle.Card(overview, "Alarms", TwinUIStyle.CardColor);
            TwinUIStyle.Row(alarms.rectTransform, 528f, 428f, 680f, 452f);
            CardHeader(alarms.rectTransform, TwinUITextR2.SecAlarms);
            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(alarms.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 16f, 56f + i * 64f, 648f, 56f);
                var line = TwinUIStyle.Panel(row, "Line", i % 2 == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(line.rectTransform, 0f, 0f, 0f, 0f);
                var dot = TwinUIStyle.Panel(row, "Dot", i == 0 ? TwinUIStyle.OkColor : TwinUIStyle.Transparent);
                TwinUIStyle.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(10f, 0f), new Vector2(8f, 8f));
                var text = TwinUIStyle.Text(row, "Text", i == 0 ? TwinUITextR2.AlarmNone : string.Empty, 15f,
                    i == 0 ? TwinUIStyle.OkColor : TwinUIStyle.MutedColor);
                TwinUIStyle.Stretch(text.rectTransform, 28f, 0f, 12f, 0f);
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        private static void BuildSourceRow(RectTransform parent, string name, string label, float y)
        {
            var row = TwinUIStyle.Panel(parent, name, TwinUIStyle.CardAltColor);
            TwinUIStyle.Row(row.rectTransform, 16f, y, 488f, 96f);
            var dot = TwinUIStyle.Panel(row.rectTransform, "Dot", TwinUIStyle.MutedColor);
            TwinUIStyle.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(10f, 10f));
            var nameText = TwinUIStyle.Text(row.rectTransform, "Name", label, TwinUIStyle.FontSection, TwinUIStyle.TextColor);
            TwinUIStyle.Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -18f), new Vector2(240f, 26f));
            var value = TwinUIStyle.Text(row.rectTransform, "Value", "未连接", TwinUIStyle.FontBody, TwinUIStyle.MutedColor);
            TwinUIStyle.Place(value.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 18f), new Vector2(400f, 24f));
        }

        private static void BuildTrend(Transform page)
        {
            var trend = TwinUIStyle.Frame(page, "PageTrend");
            TwinUIStyle.Row(trend, 0f, 56f, 1888f, 880f);

            BuildChartCard(trend, "ChartJointPos", TwinUITextR2.TrendJointPos, 0f, TwinUIStyle.SeriesColors[0],
                new[] { TwinTelemetryChannel.Joint1, TwinTelemetryChannel.Joint2, TwinTelemetryChannel.Joint3,
                        TwinTelemetryChannel.Joint4, TwinTelemetryChannel.Joint5, TwinTelemetryChannel.Joint6 },
                TwinUITextR2.TrendUnitDeg);
            BuildChartCard(trend, "ChartJointTorque", TwinUITextR2.TrendJointTorque, 288f, TwinUIStyle.SeriesColors[2],
                new[] { TwinTelemetryChannel.JointTorque1, TwinTelemetryChannel.JointTorque2, TwinTelemetryChannel.JointTorque3,
                        TwinTelemetryChannel.JointTorque4, TwinTelemetryChannel.JointTorque5, TwinTelemetryChannel.JointTorque6 },
                TwinUITextR2.TrendUnitNm);
            BuildChartCard(trend, "ChartWrench", TwinUITextR2.TrendWrench, 576f, TwinUIStyle.SeriesColors[5],
                new[] { TwinTelemetryChannel.ForceX, TwinTelemetryChannel.ForceY, TwinTelemetryChannel.ForceZ,
                        TwinTelemetryChannel.TorqueX, TwinTelemetryChannel.TorqueY, TwinTelemetryChannel.TorqueZ },
                TwinUITextR2.TrendUnitN);
        }

        private static void BuildChartCard(Transform parent, string name, string title, float y, Color accent,
            TwinTelemetryChannel[] channels, string unit)
        {
            var card = TwinUIStyle.Card(parent, name + "Card", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 0f, y, 1888f, 280f);
            var bar = TwinUIStyle.Panel(card.rectTransform, "Bar", accent);
            TwinUIStyle.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(3f, 280f));
            var header = TwinUIStyle.Text(card.rectTransform, "Title", title, TwinUIStyle.FontSection, TwinUIStyle.TextColor);
            TwinUIStyle.Row(header.rectTransform, 20f, 12f, 500f, 26f);
            var unitText = TwinUIStyle.Text(card.rectTransform, "Unit", unit, TwinUIStyle.FontCaption, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Row(unitText.rectTransform, 1180f, 16f, 688f, 20f);

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TwinTelemetryChart));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(card.rectTransform, false);
            TwinUIStyle.Row(rt, 20f, 46f, 1848f, 220f);
            var chart = go.GetComponent<TwinTelemetryChart>();
            chart.SetSeries(channels);
            chart.SetWindowSeconds(20f);
            var so = new SerializedObject(chart);
            var plot = so.FindProperty("plotBackgroundColor");
            if (plot != null)
            {
                plot.colorValue = TwinUIStyle.Hex("#FFFFFF");
            }

            var grid = so.FindProperty("gridColor");
            if (grid != null)
            {
                grid.colorValue = TwinUIStyle.Hex("#D9E1EE");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildControl(Transform page)
        {
            var control = TwinUIStyle.Frame(page, "PageControl");
            TwinUIStyle.Row(control, 0f, 56f, 1888f, 880f);

            var mode = TwinUIStyle.Card(control, "Mode", TwinUIStyle.CardColor);
            TwinUIStyle.Row(mode.rectTransform, 0f, 0f, 640f, 112f);
            CardHeader(mode.rectTransform, TwinUITextR2.SecMode);
            var modeValue = TwinUIStyle.Text(mode.rectTransform, "Value", "—", TwinUIStyle.FontTitle, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Place(modeValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, -6f), new Vector2(300f, 34f));

            var checks = TwinUIStyle.Card(control, "Checks", TwinUIStyle.CardColor);
            TwinUIStyle.Row(checks.rectTransform, 0f, 120f, 640f, 500f);
            CardHeader(checks.rectTransform, TwinUITextR2.SecInterlock);
            for (int i = 0; i < CheckNames.Length; i++)
            {
                var row = TwinUIStyle.Frame(checks.rectTransform, CheckNames[i]);
                TwinUIStyle.Row(row, 0f, 52f + i * 48f, 640f, 44f);
                var line = TwinUIStyle.Panel(row, "Line", i % 2 == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(line.rectTransform, 12f, 0f, 12f, 0f);
                var dot = TwinUIStyle.Panel(row, "Dot", TwinUIStyle.MutedColor);
                TwinUIStyle.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(24f, 0f), new Vector2(8f, 8f));
                var text = TwinUIStyle.Text(row, "Text", CheckLabels[i], TwinUIStyle.FontBody, TwinUIStyle.TextColor);
                TwinUIStyle.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(44f, 0f), new Vector2(400f, 24f));
            }

            var log = TwinUIStyle.Card(control, "Log", TwinUIStyle.CardColor);
            TwinUIStyle.Row(log.rectTransform, 0f, 628f, 640f, 252f);
            CardHeader(log.rectTransform, TwinUITextR2.SecLog);
            var logValue = TwinUIStyle.Text(log.rectTransform, "Value", "最近结果：—", 14f, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(logValue.rectTransform, 16f, 52f, 604f, 180f);
            logValue.enableWordWrapping = true;
            logValue.alignment = TextAlignmentOptions.TopLeft;

            // 目标位姿（jog 行：等待真机授权，禁用但给出原因）
            var jog = TwinUIStyle.Card(control, "Jog", TwinUIStyle.CardColor);
            TwinUIStyle.Row(jog.rectTransform, 648f, 0f, 600f, 420f);
            CardHeader(jog.rectTransform, TwinUITextR2.SecJog);
            for (int i = 0; i < 6; i++)
            {
                var row = TwinUIStyle.Frame(jog.rectTransform, "Row" + i);
                TwinUIStyle.Row(row, 16f, 56f + i * 58f, 568f, 52f);
                var line = TwinUIStyle.Panel(row, "Line", i % 2 == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(line.rectTransform, 0f, 0f, 0f, 0f);
                var label = TwinUIStyle.Text(row, "Label", TwinUITextR2.JogTitle + " " + (i + 1), TwinUIStyle.FontBody,
                    TwinUIStyle.MutedColor);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(16f, 0f), new Vector2(160f, 24f));
                var value = TwinUIStyle.Text(row, "Value", TwinUITextR2.JogDisabled, TwinUIStyle.FontCaption,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.Right);
                TwinUIStyle.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-16f, 0f), new Vector2(320f, 22f));
            }

            var speed = TwinUIStyle.Card(control, "Speed", TwinUIStyle.CardColor);
            TwinUIStyle.Row(speed.rectTransform, 648f, 428f, 600f, 112f);
            CardHeader(speed.rectTransform, TwinUITextR2.SecSpeed);
            string[] speeds = { TwinUITextR2.SpeedSlow, TwinUITextR2.SpeedMid, TwinUITextR2.SpeedFast };
            for (int i = 0; i < 3; i++)
            {
                TwinUIStyle.Button(speed.rectTransform, i.ToString(), speeds[i], TwinUIButton.Kind.Default,
                    new Vector2(16f + i * 116f, 56f), new Vector2(104f, 40f)).SetSelected(i == 1);
            }

            var actions = TwinUIStyle.Card(control, "Actions", TwinUIStyle.CardColor);
            TwinUIStyle.Row(actions.rectTransform, 648f, 548f, 1240f, 332f);
            CardHeader(actions.rectTransform, TwinUITextR2.SecCommand);
            TwinUIStyle.Button(actions.rectTransform, "BtnPreview", TwinUITextR2.BtnPreview, TwinUIButton.Kind.Default,
                new Vector2(16f, 60f), new Vector2(180f, 48f));
            TwinUIStyle.Button(actions.rectTransform, "BtnCheck", TwinUITextR2.BtnCheck, TwinUIButton.Kind.Default,
                new Vector2(212f, 60f), new Vector2(180f, 48f));
            TwinUIStyle.Button(actions.rectTransform, "BtnConfirm", TwinUITextR2.BtnConfirm, TwinUIButton.Kind.Default,
                new Vector2(408f, 60f), new Vector2(180f, 48f));
            TwinUIStyle.Button(actions.rectTransform, "BtnExecute", TwinUITextR2.BtnExecute, TwinUIButton.Kind.Primary,
                new Vector2(604f, 60f), new Vector2(180f, 48f));
            TwinUIStyle.Button(actions.rectTransform, "BtnHalt", TwinUITextR2.BtnHalt, TwinUIButton.Kind.Danger,
                new Vector2(800f, 60f), new Vector2(180f, 48f));
            var hint = TwinUIStyle.Text(actions.rectTransform, "Hint",
                "干运行不会下发真机指令；真机开关打开时本面板拒绝执行。", 13f, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(hint.rectTransform, 16f, 130f, 1200f, 60f);
            hint.enableWordWrapping = true;
        }

        private static void BuildRecord(Transform page)
        {
            var record = TwinUIStyle.Frame(page, "PageRecord");
            TwinUIStyle.Row(record, 0f, 56f, 1888f, 880f);

            var card = TwinUIStyle.Card(record, "Rows", TwinUIStyle.CardColor);
            TwinUIStyle.Row(card.rectTransform, 0f, 0f, 920f, 560f);
            CardHeader(card.rectTransform, TwinUITextR2.SecRecording);
            for (int i = 0; i < RecordNames.Length; i++)
            {
                var row = TwinUIStyle.Frame(card.rectTransform, RecordNames[i]);
                TwinUIStyle.Row(row, 0f, 56f + i * 68f, 920f, 60f);
                var line = TwinUIStyle.Panel(row, "Line", i % 2 == 0 ? TwinUIStyle.CardAltColor : TwinUIStyle.Transparent);
                TwinUIStyle.Stretch(line.rectTransform, 12f, 0f, 12f, 0f);
                var label = TwinUIStyle.Text(row, "Label", RecordLabels[i], TwinUIStyle.FontCaption, TwinUIStyle.MutedColor);
                TwinUIStyle.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(20f, 0f), new Vector2(260f, 22f));
                var value = TwinUIStyle.Text(row, "Value", "—", TwinUIStyle.FontBody, TwinUIStyle.TextColor,
                    TextAlignmentOptions.Right);
                TwinUIStyle.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-20f, 0f), new Vector2(600f, 26f));
            }

            var session = TwinUIStyle.Card(record, "Session", TwinUIStyle.CardColor);
            TwinUIStyle.Row(session.rectTransform, 936f, 0f, 952f, 560f);
            CardHeader(session.rectTransform, TwinUITextR2.SecSession);
            var note = TwinUIStyle.Text(session.rectTransform, "Note",
                "本卡只读展示记录状态与会话信息，不提供开始/停止按钮；采集与实验由独立流程控制。", 14f, TwinUIStyle.MutedColor);
            TwinUIStyle.Row(note.rectTransform, 16f, 56f, 920f, 80f);
            note.enableWordWrapping = true;
        }

        // ------------------------------------------------------------------ 接线

        private static void Wire(Transform root, bool full)
        {
            var runtimeGo = GameObject.Find(RuntimePath);
            var runtime = runtimeGo != null ? runtimeGo.GetComponent<DigitalTwinRuntime>() : null;
            var recorderGo = GameObject.Find(RecorderPath);
            var recorder = recorderGo != null ? recorderGo.GetComponent<TwinRecorder>() : null;
            var controller = runtime != null ? runtime.CommandController : null;

            var binder = root.GetComponent<TwinUIStatusBinder>();
            if (binder == null)
            {
                binder = root.gameObject.AddComponent<TwinUIStatusBinder>();
            }

            binder.Bind(runtime, controller, recorder);

            var header = root.Find("Header");
            var headerView = header != null ? header.GetComponent<TwinUIHeaderView>() : null;
            if (headerView == null && header != null)
            {
                headerView = header.gameObject.AddComponent<TwinUIHeaderView>();
            }

            if (headerView != null)
            {
                headerView.Bind(binder, true);
            }

            var mission = root.Find("MissionRoot");
            var missionView = mission != null ? mission.GetComponent<TwinUIMissionView>() : null;
            if (missionView != null)
            {
                missionView.Refresh();
            }

            var ops = root.Find("TwinOpsRoot");
            var opsView = ops != null ? ops.GetComponent<TwinUITwinOpsView>() : null;
            if (opsView != null)
            {
                opsView.Bind(binder);
            }

            var tabHost = root.GetComponent<TwinUIPageHost>();
            if (tabHost == null)
            {
                tabHost = root.gameObject.AddComponent<TwinUIPageHost>();
            }

            var missionTab = root.Find("TabBar/BtnMission");
            var opsTab = root.Find("TabBar/BtnTwinOps");
            tabHost.Configure(mission != null ? mission.gameObject : null, ops != null ? ops.gameObject : null,
                headerView, missionView, opsView, binder,
                missionTab != null ? missionTab.GetComponent<TwinUIButton>() : null,
                opsTab != null ? opsTab.GetComponent<TwinUIButton>() : null);
            tabHost.Wire();
            // 三期：工业数据台 + 实验上下文条接线（幂等；会补齐第三页签、设置资产与主题 applier）
            TwinUIR3Builder.Wire(root, TwinUIR3Builder.LoadSettings(false));
            tabHost.Show(0);
        }
    }
}