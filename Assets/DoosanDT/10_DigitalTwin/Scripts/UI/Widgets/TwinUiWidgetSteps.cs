// TwinUiWidgetSteps.cs —— 步骤引导卡片（P8）
// 数据源：TwinUiMissionModel.Instance（会话级本地步骤状态）+ 既有 TwinUITextR2 的步骤文案。
// 诚实边界：这是**本地演示流程**，不是考核流程、不代表真实拆解评分；卡片内固定标注。
// 约定：只实现 Build/Refresh，不加 Update；按钮只操作本地模型并立即重绘，不下发任何命令。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetSteps : TwinUiModule
    {
        public const string ModuleId = "steps";

        /// <summary>可见步骤行数（当前步始终落在窗口内）。</summary>
        private const int VisibleRows = 6;

        private readonly TMP_Text[] _rowNames = new TMP_Text[VisibleRows];
        private readonly TMP_Text[] _rowStates = new TMP_Text[VisibleRows];
        private readonly UnityEngine.UI.Image[] _rowDots = new UnityEngine.UI.Image[VisibleRows];
        private readonly RectTransform[] _rowRoots = new RectTransform[VisibleRows];
        private TMP_Text _windowNote;
        private TMP_Text _body;
        private TMP_Text _hint;

        public override string Id => ModuleId;

        public override string Title => "步骤引导";

        public override string Kicker => "STEPS";

        public override string Subtitle => "本地流程";

        public override string WorkspaceId => TwinUiWorkspaceRegistry.Mission;

        public override Vector2 CardSize => new Vector2(380f, 300f);

        public override Vector2 FloatingMinSize => new Vector2(340f, 280f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetSteps");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(240f, CardSize.x - 24f);

            for (int i = 0; i < VisibleRows; i++)
            {
                var row = TwinUIStyle.Frame(content, "StepRow" + i);
                TwinUIStyle.Row(row, 0f, i * 22f, w, 22f);
                _rowRoots[i] = row;

                var dot = TwinUIStyle.StatusDot(row, "Dot", 6f, TwinUIStyle.QuietColor);
                TwinUIStyle.Row(dot.rectTransform, 0f, 8f, 6f, 6f);
                _rowDots[i] = dot;

                var index = TwinUIStyle.Mono(row, "Index", (i + 1).ToString(), TwinUIStyle.FontUnit,
                    TwinUIStyle.QuietColor, TextAlignmentOptions.Left);
                TwinUIStyle.Row(index.rectTransform, 12f, 2f, 20f, 18f);

                var name = TwinUIStyle.Text(row, "Name", string.Empty, TwinUIStyle.FontBodySm,
                    TwinUIStyle.MutedColor);
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                TwinUIStyle.Row(name.rectTransform, 34f, 2f, Mathf.Max(80f, w - 34f - 60f), 18f);
                _rowNames[i] = name;

                var state = TwinUIStyle.Text(row, "State", string.Empty, TwinUIStyle.FontUnit,
                    TwinUIStyle.QuietColor, TextAlignmentOptions.Right);
                TwinUIStyle.Row(state.rectTransform, Mathf.Max(120f, w - 60f), 2f, 60f, 18f);
                _rowStates[i] = state;
            }

            _windowNote = TwinUiCardParts.Caption(content, string.Empty, 0f, 134f, w, 14f);
            _windowNote.fontSize = TwinUIStyle.FontUnit;

            TwinUiCardParts.Divider(content, 152f, w);

            _body = TwinUiCardParts.Caption(content, string.Empty, 0f, 158f, w, 42f);
            _hint = TwinUiCardParts.Caption(content, string.Empty, 0f, 202f, w, 16f);

            TwinUIStyle.Button(content, "BtnPrev", TwinUITextR2.BtnPrev, TwinUIButton.Kind.Plain,
                new Vector2(0f, -222f), new Vector2(110f, 22f), OnPrev).Label.fontSize = TwinUIStyle.FontUnit;
            TwinUIStyle.Button(content, "BtnNext", TwinUITextR2.BtnNext, TwinUIButton.Kind.Default,
                new Vector2(122f, -222f), new Vector2(110f, 22f), OnNext).Label.fontSize = TwinUIStyle.FontUnit;
            // 底栏文案按需求写"复位"（既有常量 BtnReset 是"重置"，语义不同，避免误用）。
            TwinUIStyle.Button(content, "BtnReset", "复位", TwinUIButton.Kind.Plain,
                new Vector2(244f, -222f), new Vector2(110f, 22f), OnReset).Label.fontSize = TwinUIStyle.FontUnit;

            _body.text = TwinUITextR3.Placeholder;
            _hint.text = TwinUITextR3.Placeholder;
            return root;
        }

        public override void Refresh()
        {
            if (View == null)
            {
                return;
            }

            var model = TwinUiMissionModel.Instance;
            if (model == null)
            {
                _windowNote.text = "未接入步骤模型";
                _windowNote.color = TwinUIStyle.QuietColor;
                _body.text = TwinUITextR3.Placeholder;
                _hint.text = TwinUITextR3.Placeholder;
                for (int i = 0; i < VisibleRows; i++)
                {
                    _rowRoots[i].gameObject.SetActive(false);
                }

                return;
            }

            int count = Mathf.Min(model.StepCount, TwinUITextR2.Steps.Length);
            int current = Mathf.Clamp(model.Current, 0, Mathf.Max(0, count - 1));

            // 步骤多于可见行时，窗口跟随当前步滚动（不依赖 Update）。
            int first = Mathf.Clamp(current - VisibleRows / 2, 0, Mathf.Max(0, count - VisibleRows));

            for (int i = 0; i < VisibleRows; i++)
            {
                int step = first + i;
                bool visible = step < count;
                _rowRoots[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                bool done = model.Done(step);
                bool active = step == current;

                _rowNames[i].text = TwinUITextR2.Steps[step];
                if (active)
                {
                    _rowNames[i].color = TwinUIStyle.AccentColor;
                    _rowNames[i].fontStyle = FontStyles.Bold;
                    _rowStates[i].text = TwinUITextR2.StepCurrent;
                    _rowStates[i].color = TwinUIStyle.AccentColor;
                    _rowDots[i].color = TwinUIStyle.AccentColor;
                }
                else if (done)
                {
                    _rowNames[i].color = TwinUIStyle.OkColor;
                    _rowNames[i].fontStyle = FontStyles.Normal;
                    _rowStates[i].text = TwinUITextR2.StepDone;
                    _rowStates[i].color = TwinUIStyle.OkColor;
                    _rowDots[i].color = TwinUIStyle.OkColor;
                }
                else
                {
                    _rowNames[i].color = TwinUIStyle.MutedColor;
                    _rowNames[i].fontStyle = FontStyles.Normal;
                    _rowStates[i].text = TwinUITextR2.StepWaiting;
                    _rowStates[i].color = TwinUIStyle.QuietColor;
                    _rowDots[i].color = TwinUIStyle.QuietColor;
                }
            }

            string window = count > VisibleRows
                ? "第 " + (first + 1) + "–" + Mathf.Min(count, first + VisibleRows) + " / 共 " + count + " 步"
                : "共 " + count + " 步";
            _windowNote.text = window + " · 本地演示流程（非考核）";
            _windowNote.color = TwinUIStyle.QuietColor;

            _body.text = BodyOf(current, count);
            _hint.text = TwinUITextR2.HintPrefix + HintOf(current, count);
        }

        private static string BodyOf(int index, int count)
        {
            if (index >= 0 && index < count && index < TwinUITextR2.StepBodies.Length)
            {
                return TwinUITextR2.StepBodies[index];
            }

            return TwinUITextR3.Placeholder;
        }

        private static string HintOf(int index, int count)
        {
            if (index >= 0 && index < count && index < TwinUITextR2.StepHints.Length)
            {
                return TwinUITextR2.StepHints[index];
            }

            return TwinUITextR3.Placeholder;
        }

        private void OnPrev()
        {
            TwinUiMissionModel.Instance.Prev();
            Refresh();
        }

        private void OnNext()
        {
            TwinUiMissionModel.Instance.Next();
            Refresh();
        }

        private void OnReset()
        {
            TwinUiMissionModel.Instance.Reset();
            Refresh();
        }
    }
}