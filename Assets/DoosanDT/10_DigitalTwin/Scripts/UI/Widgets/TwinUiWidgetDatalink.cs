// TwinUiWidgetDatalink.cs —— 数据链卡片（P8，总览/数字孪生/实验数据共用）
// 数据源：TwinUIStatusBinder.Current（唯一只读快照）。只展示链路质量与来源，不触发任何连接动作。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetDatalink : TwinUiModule
    {
        public const string ModuleId = "datalink";

        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _hz;
        private TwinUiCardParts.Kv _source;
        private TwinUiCardParts.Kv _quality;
        private TwinUiCardParts.Kv _latency;
        private TwinUiCardParts.Kv _frames;
        private TMP_Text _error;

        public override string Id => ModuleId;
        public override string Title => "数据链";
        public override string Kicker => "DATA LINK";
        public override string Subtitle => "StatusBinder";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(340f, 226f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 210f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetDatalink");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _hz = TwinUiCardParts.StatBlock(root, "链路频率", 0f, 22f, w);
            _source = TwinUiCardParts.Row(root, 80f, "数据源", w);
            _quality = TwinUiCardParts.Row(root, 104f, "帧质量", w);
            _latency = TwinUiCardParts.Row(root, 128f, "应用时延", w);
            _frames = TwinUiCardParts.Row(root, 152f, "丢帧 / 队列", w);
            _error = TwinUiCardParts.Caption(root, string.Empty, 0f, 176f, w, 30f);

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
                _hz.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _quality.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _source.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _latency.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _frames.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _error.text = string.Empty;
                return;
            }

            var snap = _binder.Current;
            bool has = snap.HasFrame;
            bool connected = snap.Connected;
            _badge.Set(!connected ? TwinUITone.Unknown : has ? TwinUITone.Ok : TwinUITone.Warn,
                !connected ? TwinUITextR3.NotConnected : has ? "数据正常" : "等待首帧");

            _hz.Set(has ? snap.Hz.ToString("F1") : TwinUITextR3.Placeholder,
                has ? TwinUITone.Ok : TwinUITone.Unknown, TwinUITextR3.UnitHz);

            _quality.Set(string.IsNullOrEmpty(snap.Quality) ? TwinUITextR3.Placeholder : snap.Quality,
                has ? TwinUITone.Ok : TwinUITone.Unknown);

            string source = string.IsNullOrEmpty(snap.SourceKind) ? TwinUITextR3.Placeholder : snap.SourceKind;
            if (!string.IsNullOrEmpty(snap.SourceName))
            {
                source += " · " + snap.SourceName;
            }

            _source.Set(source, has ? TwinUITone.Ok : TwinUITone.Unknown);

            TwinUITone latTone = !has ? TwinUITone.Unknown
                : snap.LatencyMs > 200 ? TwinUITone.Crit
                : snap.LatencyMs > 80 ? TwinUITone.Warn : TwinUITone.Ok;
            _latency.Set(has ? snap.LatencyMs.ToString("F1") + TwinUITextR3.UnitMs : TwinUITextR3.Placeholder,
                latTone);

            TwinUITone frameTone = !has ? TwinUITone.Unknown : snap.Dropped > 0 ? TwinUITone.Warn : TwinUITone.Ok;
            _frames.Set(has ? snap.Dropped + " / " + snap.Queue : TwinUITextR3.Placeholder, frameTone);

            bool hasError = !string.IsNullOrEmpty(snap.LastError);
            _error.text = hasError ? "最近错误：" + snap.LastError : string.Empty;
            _error.color = TwinUITheme.ToneTextColor(TwinUITone.Warn);
        }
    }
}