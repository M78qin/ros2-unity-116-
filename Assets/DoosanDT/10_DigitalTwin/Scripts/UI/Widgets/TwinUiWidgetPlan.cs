// TwinUiWidgetPlan.cs —— 规划与执行卡片（P8）
// 数据源：TwinCommandController 的既有只读状态——LastPlanningStatus / LastPlanningError / LastResult。
// 约定：只读展示，不触发规划、不下发命令；空字符串一律显示 —（不猜测、不补 0）。
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetPlan : TwinUiModule
    {
        public const string ModuleId = "plan";

        private const string NoControllerText = "未挂载";
        private const string NotPlannedText = "尚未规划";

        private TwinCommandController _controller;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _planStatus;
        private TwinUiCardParts.Kv _planError;
        private TwinUiCardParts.Kv _result;
        private TwinUiCardParts.Kv _resultNote;
        private TwinUiCardParts.Kv _resultError;

        public override string Id => ModuleId;

        public override string Title => "规划与执行";

        public override string Kicker => "PLAN";

        public override string Subtitle => "只读";

        public override string WorkspaceId => TwinUiWorkspaceRegistry.Control;

        public override Vector2 CardSize => new Vector2(320f, 200f);

        public override Vector2 FloatingMinSize => new Vector2(300f, 190f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetPlan");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(200f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);
            _planStatus = TwinUiCardParts.Row(content, 24f, "规划状态", w);
            _planError = TwinUiCardParts.Row(content, 46f, "规划错误", w);
            TwinUiCardParts.Divider(content, 70f, w);
            _result = TwinUiCardParts.Row(content, 76f, "执行结果", w);
            _resultNote = TwinUiCardParts.Row(content, 98f, "结果说明", w);
            _resultError = TwinUiCardParts.Row(content, 120f, "错误信息", w);

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            ClearRows();
            return root;
        }

        public override void Refresh()
        {
            var point = Object.FindObjectOfType<PointMotionSession>();
            if(point != null) {
                _badge.Set(point.Connected ? TwinUITone.Warn : TwinUITone.Unknown, "模拟反馈 · 真机禁用");
                _planStatus.Set(point.State, TwinUITone.Warn);
                _planError.Set("路径未验证", TwinUITone.Warn);
                _result.Set(point.State == "REACHED" ? "模拟到位" : point.State, TwinUITone.Warn);
                _resultNote.Set(point.Reason, TwinUITone.Unknown);
                _resultError.Set("v"+point.Version+" · "+(point.Digest.Length>12?point.Digest.Substring(0,12):"未确认"),TwinUITone.Unknown);
                return;
            }

            if (_controller == null)
            {
                _controller = Object.FindObjectOfType<TwinCommandController>();
            }

            if (_controller == null)
            {
                _badge.Set(TwinUITone.Unknown, NoControllerText);
                ClearRows();
                return;
            }

            string planStatus = _controller.LastPlanningStatus;
            bool hasPlan = !string.IsNullOrEmpty(planStatus);
            _planStatus.Set(hasPlan ? planStatus : NotPlannedText,
                hasPlan ? PlanTone(planStatus) : TwinUITone.Unknown);

            string planError = _controller.LastPlanningError;
            bool hasPlanError = !string.IsNullOrEmpty(planError);
            _planError.Set(hasPlanError ? planError : TwinUITextR3.Placeholder,
                hasPlanError ? TwinUITone.Crit : TwinUITone.Unknown);

            RobotCommandResult last = _controller.LastResult;
            // 徽标优先级：DryRun → Warn（干运行不等于真机执行）；Success → Ok；其余 Unknown。
            TwinUITone resultTone = last.DryRun ? TwinUITone.Warn
                : last.Success ? TwinUITone.Ok : TwinUITone.Unknown;
            _badge.Set(resultTone, BadgeText(last));

            _result.Set(ResultText(last), resultTone);

            string resultNote = last.Status;
            _resultNote.Set(!string.IsNullOrEmpty(resultNote) ? resultNote : TwinUITextR3.Placeholder,
                string.IsNullOrEmpty(resultNote) ? TwinUITone.Unknown : TwinUITone.Ok);

            string resultError = last.ErrorMessage;
            _resultError.Set(!string.IsNullOrEmpty(resultError) ? resultError : TwinUITextR3.Placeholder,
                string.IsNullOrEmpty(resultError) ? TwinUITone.Unknown : TwinUITone.Crit);
        }

        private void ClearRows()
        {
            _planStatus.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _planError.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _result.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _resultNote.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _resultError.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
        }

        private static TwinUITone PlanTone(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return TwinUITone.Unknown;
            }

            string s = status.ToUpperInvariant();
            if (s.Contains("FAIL") || s.Contains("ERR") || s.Contains("REJECT") || s.Contains("BLOCK"))
            {
                return TwinUITone.Crit;
            }

            return TwinUITone.Ok;
        }

        private static string BadgeText(RobotCommandResult last)
        {
            if (last.DryRun)
            {
                return "干运行";
            }

            if (last.Success)
            {
                return "执行成功";
            }

            return string.IsNullOrEmpty(last.Status) && string.IsNullOrEmpty(last.ErrorMessage)
                ? "无执行记录" : "执行失败";
        }

        private static string ResultText(RobotCommandResult last)
        {
            if (last.DryRun)
            {
                return "干运行（未下发）";
            }

            if (last.Success)
            {
                return "成功";
            }

            if (string.IsNullOrEmpty(last.Status) && string.IsNullOrEmpty(last.ErrorMessage))
            {
                return TwinUITextR3.Placeholder;
            }

            return "失败";
        }
    }
}