// TwinUiWidgetAuthority.cs —— 控制权限卡片（P8）
// 数据源：TwinCommandController（既有安全链）——CurrentMode / IsEmergencyStopped / 三个使能开关 / CanSendRealMove。
// 约定（与 TwinUiWidgetSync 一致）：
//   * 只实现 TwinUiModule；内容根四边拉伸，内部用 Row 自上而下排布；
//   * 颜色/字阶只取 TwinUIStyle 令牌；状态"颜色 + 文字"双通道；
//   * **纯只读**：不调用 EnterExecute / SendMoveJoint 等任何下发入口，控制器缺失显示 未挂载/—，不造数。
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetAuthority : TwinUiModule
    {
        public const string ModuleId = "authority";

        private const string LockedText = "执行锁定";
        private const string NoControllerText = "未挂载";

        private TwinCommandController _controller;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _mode;
        private TwinUiCardParts.Kv _estop;
        private TwinUiCardParts.Kv _bidirectional;
        private TwinUiCardParts.Kv _realRobot;
        private TwinUiCardParts.Kv _dryRun;
        private TwinUiCardParts.Kv _gate;

        public override string Id => ModuleId;

        public override string Title => "控制权限";

        public override string Kicker => "AUTHORITY";

        public override string Subtitle => "只读";

        public override string WorkspaceId => TwinUiWorkspaceRegistry.Control;

        public override Vector2 CardSize => new Vector2(320f, 214f);

        public override Vector2 FloatingMinSize => new Vector2(300f, 200f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetAuthority");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(200f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);
            _mode = TwinUiCardParts.Row(content, 24f, "模式", w);
            _estop = TwinUiCardParts.Row(content, 46f, "急停", w);
            _bidirectional = TwinUiCardParts.Row(content, 68f, "双向使能", w);
            _realRobot = TwinUiCardParts.Row(content, 90f, "真机使能", w);
            _dryRun = TwinUiCardParts.Row(content, 112f, "DryRun", w);
            _gate = TwinUiCardParts.Row(content, 134f, "可下发真机", w);

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            ClearRows();
            return root;
        }

        public override void Refresh()
        {
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

            bool canMove = _controller.CanSendRealMove;
            bool estop = _controller.IsEmergencyStopped;

            // 权限总览：可下发才允许显示"允许"，否则一律"执行锁定"（Warn；急停情况在急停行单独标红）。
            _badge.Set(canMove ? TwinUITone.Ok : TwinUITone.Warn, canMove ? "允许真机下发" : LockedText);

            var mode = _controller.CurrentMode;
            TwinUITone modeTone = mode == TwinMode.Execute ? TwinUITone.Warn
                : mode == TwinMode.Plan ? TwinUITone.Ok : TwinUITone.Unknown;
            _mode.Set(mode.ToString(), modeTone);

            _estop.Set(estop ? "已急停" : "正常", estop ? TwinUITone.Crit : TwinUITone.Ok);
            _bidirectional.Set(OnOff(_controller.EnableBidirectionalControl),
                _controller.EnableBidirectionalControl ? TwinUITone.Ok : TwinUITone.Warn);
            _realRobot.Set(OnOff(_controller.EnableRealRobotCommand),
                _controller.EnableRealRobotCommand ? TwinUITone.Ok : TwinUITone.Warn);
            _dryRun.Set(OnOff(_controller.EnableDryRun),
                _controller.EnableDryRun ? TwinUITone.Ok : TwinUITone.Warn);
            _gate.Set(canMove ? "允许" : "锁定（不发送）", canMove ? TwinUITone.Ok : TwinUITone.Warn);
        }

        private void ClearRows()
        {
            _mode.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _estop.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _bidirectional.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _realRobot.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _dryRun.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            _gate.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
        }

        private static string OnOff(bool value)
        {
            return value ? "开" : "关";
        }
    }
}