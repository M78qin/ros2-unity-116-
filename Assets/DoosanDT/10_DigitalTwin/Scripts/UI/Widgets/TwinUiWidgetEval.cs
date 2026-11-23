// TwinUiWidgetEval.cs —— 训练评估卡片（P8）
// 数据源：TwinUiMissionModel.Instance（与步骤引导共用同一份会话状态）。
// 诚实边界：**本地自评**——得分 = 完成度百分比取整，不是评分系统、不是考核结果；错误次数无数据源，显示 —。
// 约定：只实现 Build/Refresh，不加 Update；不做数据订阅；缺数据一律 —，禁止编造 0。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetEval : TwinUiModule
    {
        public const string ModuleId = "eval";

        private const string LocalSelfEvalText = "本地自评";

        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _score;
        private TwinUiCardParts.Kv _timer;
        private TwinUiCardParts.Kv _progress;
        private TwinUiCardParts.Kv _errors;
        private TMP_Text _note;

        public override string Id => ModuleId;

        public override string Title => "训练评估";

        public override string Kicker => "SELF EVAL";

        public override string Subtitle => "本地自评";

        public override string WorkspaceId => TwinUiWorkspaceRegistry.Mission;

        public override Vector2 CardSize => new Vector2(300f, 210f);

        public override Vector2 FloatingMinSize => new Vector2(280f, 200f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetEval");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(200f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);
            _score = TwinUiCardParts.StatBlock(content, TwinUITextR2.EvalScore, 0f, 18f, w);
            _timer = TwinUiCardParts.Row(content, 74f, TwinUITextR2.EvalTimer, w);
            _progress = TwinUiCardParts.Row(content, 96f, TwinUITextR2.EvalProgress, w);
            _errors = TwinUiCardParts.Row(content, 118f, TwinUITextR2.EvalErrors, w);
            _note = TwinUiCardParts.Caption(content, "本地自评，非评分系统", 0f, 142f, w, 14f);
            _note.fontSize = TwinUIStyle.FontUnit;

            _badge.Set(TwinUITone.Unknown, LocalSelfEvalText);
            _score.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            ClearRows();
            return root;
        }

        public override void Refresh()
        {
            var model = TwinUiMissionModel.Instance;
            if (model == null)
            {
                _badge.Set(TwinUITone.Unknown, "未接入步骤模型");
                _score.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                ClearRows();
                return;
            }

            float progress = Mathf.Clamp01(model.Progress01);
            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            _badge.Set(percent >= 100 ? TwinUITone.Ok : TwinUITone.Unknown, LocalSelfEvalText);
            _score.Set(percent.ToString(), TwinUITone.Ok, TwinUITextR3.UnitPercent.Trim());

            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(model.ElapsedSeconds));
            _timer.Set((totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00"),
                TwinUITone.Ok);

            _progress.Set(percent + TwinUITextR3.UnitPercent, TwinUITone.Ok);

            // 错误次数：当前没有错误计数数据源，只能显示 —（不得写 0 冒充"零错误"）。
            _errors.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
        }

        private void ClearRows()
        {
            _timer.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _progress.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _errors.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
        }
    }
}