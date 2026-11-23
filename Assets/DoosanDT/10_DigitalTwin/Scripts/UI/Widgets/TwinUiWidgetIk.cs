// TwinUiWidgetIk.cs —— IK 预览手柄卡片（P9）
// 把原先散落在左上角的 IK HUD 收进主面板：状态文本 / 可达 / 关节限位 / 工作空间 / 位置与旋转误差 / 迭代次数 / 目标点。
// 只读；是否允许真机下发仍由 TwinPlayIkHandle.AllowRobotDispatch 与 TwinCommandController 安全链决定，本卡不触发任何执行。
using DigitalTwin.Interaction;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetIk : TwinUiModule
    {
        public const string ModuleId = "ik";

        private TwinPlayIkHandle _handle;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _status;
        private TwinUiCardParts.Kv _reachable;
        private TwinUiCardParts.Kv _limits;
        private TwinUiCardParts.Kv _workspace;
        private TwinUiCardParts.Kv _posErr;
        private TwinUiCardParts.Kv _rotErr;
        private TwinUiCardParts.Kv _iterations;
        private TMP_Text _note;

        public override string Id => ModuleId;
        public override string Title => "IK 预览";
        public override string Kicker => "IK GHOST";
        public override string Subtitle => "TwinPlayIkHandle";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override Vector2 CardSize => new Vector2(330f, 262f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 240f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetIk");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _status = TwinUiCardParts.Row(root, 24f, "手柄状态", w);
            _reachable = TwinUiCardParts.Row(root, 48f, "可达性", w);
            _limits = TwinUiCardParts.Row(root, 72f, "关节限位", w);
            _workspace = TwinUiCardParts.Row(root, 96f, "工作空间", w);
            _posErr = TwinUiCardParts.Row(root, 120f, "位置误差", w);
            _rotErr = TwinUiCardParts.Row(root, 144f, "旋转误差", w);
            _iterations = TwinUiCardParts.Row(root, 168f, "迭代次数", w);
            _note = TwinUiCardParts.Caption(root,
                "仅预览（Ghost）；真机下发锁定，执行需独立授权与安全评审", 0f, 196f, w, 34f);
            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            return root;
        }

        public override void Refresh()
        {
            if (_handle == null)
            {
                _handle = Object.FindObjectOfType<TwinPlayIkHandle>();
            }

            if (_handle == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 TwinPlayIkHandle");
                _status.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _reachable.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _limits.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _workspace.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _posErr.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _rotErr.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _iterations.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            bool dragging = _handle.Dragging;
            bool reachable = _handle.Reachable;
            bool limitsOk = _handle.JointLimitsOk;
            bool clamped = _handle.WorkspaceClamped;
            TwinUITone tone = !reachable || !limitsOk ? TwinUITone.Crit : clamped ? TwinUITone.Warn : TwinUITone.Ok;
            _badge.Set(dragging ? TwinUITone.Ok : TwinUITone.Unknown, dragging ? "拖动中" : "待机");

            _status.Set(string.IsNullOrEmpty(_handle.IkStatusText) ? TwinUITextR3.Placeholder : _handle.IkStatusText,
                tone);
            _reachable.Set(reachable ? "可达" : "不可达", reachable ? TwinUITone.Ok : TwinUITone.Crit);
            _limits.Set(limitsOk ? "在限位内" : "超限位", limitsOk ? TwinUITone.Ok : TwinUITone.Crit);
            _workspace.Set(clamped ? "已钳制" : "在范围内", clamped ? TwinUITone.Warn : TwinUITone.Ok);
            _posErr.Set((_handle.PositionErrorMeters * 1000f).ToString("F1") + " mm",
                _handle.PositionErrorMeters > 0.02f ? TwinUITone.Warn : TwinUITone.Ok);
            _rotErr.Set(_handle.RotationErrorDeg.ToString("F2") + " °",
                _handle.RotationErrorDeg > 5f ? TwinUITone.Warn : TwinUITone.Ok);
            _iterations.Set(_handle.Iterations.ToString(), TwinUITone.Ok);
            _note.color = _handle.AllowRobotDispatch ? TwinUITheme.ToneTextColor(TwinUITone.Crit) : TwinUIStyle.QuietColor;
        }
    }
}