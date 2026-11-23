// TwinUiWidgetSession.cs —— 会话与录制卡片（P8，实验数据页）
// 数据源：TwinUIStatusBinder.Current（Recording/SessionId/CsvReady/Pending/RecDropped/CsvDir）。
// 硬约束：UI 只是显示；开关窗口/页面不会启动或停止正式采集。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetSession : TwinUiModule
    {
        public const string ModuleId = "session";

        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _pending;
        private TwinUiCardParts.Kv _recording;
        private TwinUiCardParts.Kv _session;
        private TwinUiCardParts.Kv _csv;
        private TwinUiCardParts.Kv _dropped;
        private TMP_Text _dir;

        public override string Id => ModuleId;
        public override string Title => "会话与录制";
        public override string Kicker => "SESSION";
        public override string Subtitle => "TwinRecorder";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Experiment;
        public override Vector2 CardSize => new Vector2(340f, 226f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 200f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetSession");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _pending = TwinUiCardParts.StatBlock(root, "待写帧", 0f, 22f, w);
            _recording = TwinUiCardParts.Row(root, 80f, "录制状态", w);
            _session = TwinUiCardParts.Row(root, 104f, "会话 ID", w);
            _csv = TwinUiCardParts.Row(root, 128f, "CSV 就绪", w);
            _dropped = TwinUiCardParts.Row(root, 152f, "丢样", w);
            _dir = TwinUiCardParts.Caption(root, string.Empty, 0f, 176f, w, 34f);

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            return root;
        }

        public override void Refresh()
        {
            if (_binder == null)
            {
                _binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 StatusBinder");
                _pending.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _recording.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _session.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _csv.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _dropped.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _dir.text = string.Empty;
                return;
            }

            var snap = _binder.Current;
            _badge.Set(snap.Recording ? TwinUITone.Ok : TwinUITone.Unknown,
                snap.Recording ? "录制中" : "未录制");
            _pending.Set(snap.Pending.ToString(), snap.Pending > 0 ? TwinUITone.Warn : TwinUITone.Ok);
            _recording.Set(snap.Recording ? "录制中" : "未录制", snap.Recording ? TwinUITone.Ok : TwinUITone.Unknown);
            _session.Set(string.IsNullOrEmpty(snap.SessionId) ? TwinUITextR3.Placeholder : snap.SessionId,
                snap.Recording ? TwinUITone.Ok : TwinUITone.Unknown);
            _csv.Set(snap.CsvReady ? "已就绪" : "未就绪", snap.CsvReady ? TwinUITone.Ok : TwinUITone.Unknown);
            _dropped.Set(snap.RecDropped.ToString(), snap.RecDropped > 0 ? TwinUITone.Crit : TwinUITone.Ok);
            _dir.text = string.IsNullOrEmpty(snap.CsvDir) ? "CSV 目录：" + TwinUITextR3.Placeholder
                : "CSV 目录：" + snap.CsvDir;
            _dir.color = TwinUIStyle.QuietColor;
        }
    }
}