// TwinUiWidgetRoamGuide.cs —— 漫游与操作卡片（P9）
// 把"键鼠怎么用"从任务栏一行小字升级成一张卡：键位表 + 当前速度/位姿 + 最近动作 + 两个按钮（复位视角 / 聚焦选中）。
// 与 TwinUiRoamBridge 配套；真正移动由 TwinPlayCameraRig 完成，本卡只显示与调用它的公开入口。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetRoamGuide : TwinUiModule
    {
        public const string ModuleId = "roam";

        private static readonly string[] Keys =
        {
            "W/A/S/D  前后左右（无需按住右键）",
            "Q / E    下降 / 上升",
            "Shift / Ctrl  加速 / 减速",
            "右键拖动  转视角（Esc 释放鼠标）",
            "滚轮  调节移动速度",
            "F  聚焦当前选中零件 · R  复位视角"
        };

        private TwinUiRoamBridge _roam;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _speed;
        private TwinUiCardParts.Kv _pose;
        private TwinUiCardParts.Kv _last;
        private TMP_Text _keys;
        private TwinUIButton _focusButton;
        private TwinUIButton _resetButton;

        public override string Id => ModuleId;
        public override string Title => "漫游与操作";
        public override string Kicker => "NAVIGATION";
        public override string Subtitle => "TwinPlayCameraRig";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(360f, 294f);
        public override Vector2 FloatingMinSize => new Vector2(320f, 270f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetRoamGuide");
            View = root;
            float w = Mathf.Max(200f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _speed = TwinUiCardParts.StatBlock(root, "移动速度", 0f, 22f, w * 0.45f);
            _pose = TwinUiCardParts.Row(root, 24f, "相机位姿", w);
            _last = TwinUiCardParts.Row(root, 46f, "最近动作", w);

            _keys = TwO(root, string.Join("\n", Keys), 0f, 74f, w, 118f);

            _focusButton = TwinUIStyle.Button(root, "BtnFocus", "聚焦选中", TwinUIButton.Kind.Default,
                Vector2.zero, new Vector2(96f, 22f), OnFocus);
            TwinUIStyle.Row((RectTransform)_focusButton.transform, 0f, 196f, 96f, 22f);
            if (_focusButton.Label != null)
            {
                _focusButton.Label.fontSize = TwinUIStyle.FontUnit;
            }

            _resetButton = TwinUIStyle.Button(root, "BtnResetView", "复位视角", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(96f, 22f), OnReset);
            TwinUIStyle.Row((RectTransform)_resetButton.transform, 100f, 196f, 96f, 22f);
            if (_resetButton.Label != null)
            {
                _resetButton.Label.fontSize = TwinUIStyle.FontUnit;
            }

            TwinUiCardParts.Caption(root, "移动速度也可用滚轮调；速度值来自相机装配体当前值", 0f, 222f, w, 28f);
            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            return root;
        }

        private static TMP_Text TwO(Transform parent, string text, float x, float y, float w, float h)
        {
            var t = TwinUIStyle.Text(parent, "Keys", text, TwinUIStyle.FontUnit, TwinUIStyle.MutedColor);
            t.enableWordWrapping = true;
            t.lineSpacing = 4f;
            TwinUIStyle.Row(t.rectTransform, x, y, w, h);
            return t;
        }

        private void OnFocus()
        {
            if (_roam == null)
            {
                _roam = Object.FindObjectOfType<TwinUiRoamBridge>();
            }

            if (_roam != null)
            {
                _roam.FocusSelection();
            }
        }

        private void OnReset()
        {
            if (_roam == null)
            {
                _roam = Object.FindObjectOfType<TwinUiRoamBridge>();
            }

            if (_roam != null)
            {
                _roam.ResetView();
            }
        }

        public override void Refresh()
        {
            if (_roam == null)
            {
                _roam = Object.FindObjectOfType<TwinUiRoamBridge>();
            }

            if (_roam == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 TwinUiRoamBridge");
                _speed.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _pose.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _last.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            _badge.Set(_roam.WasdWithoutRightMouse ? TwinUITone.Ok : TwinUITone.Warn,
                _roam.WasdWithoutRightMouse ? "WASD 可直行" : "WASD 需按住右键");
            _speed.Set(_roam.CurrentSpeed.ToString("F2"), TwinUITone.Ok, " m/s");
            _pose.Set(_roam.PoseLabel, TwinUITone.Ok);
            _last.Set(_roam.LastAction, TwinUITone.Ok);
        }
    }
}