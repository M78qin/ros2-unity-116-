// TwinUiWidgetSync.cs —— 虚实同步卡片（P8）
// 数据源：TwinUIStatusBinder.Current（唯一 5Hz 只读快照）——链路频率 / 新鲜度 / 时延 / 抖动 / 丢帧 / 时钟 / 帧质量。
// 约定（所有 V2 卡片照此写）：
//   * 只实现 TwinUiModule；内容根四边拉伸，内部用锚点分列（不依赖卡片宽度回调）；
//   * 颜色/字阶只取 TwinUIStyle 令牌；状态必须"颜色 + 文字"双通道；
//   * 没有数据源就显示 未接入/无数据，禁止填 0。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetSync : TwinUiModule
    {
        public const string ModuleId = "sync";

        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _hz;
        private TwinUiCardParts.Kv _fresh;
        private TwinUiCardParts.Kv _latency;
        private TwinUiCardParts.Kv _jitter;
        private TwinUiCardParts.Kv _dropped;
        private TwinUiCardParts.Kv _clock;
        private TMP_Text _quality;

        public override string Id => ModuleId;
        public override string Title => "虚实同步";
        public override string Kicker => "SYNC";
        public override string Subtitle => "StatusBinder";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override string Group => "sync";
        public override Vector2 CardSize => new Vector2(320f, 224f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 210f);

        public bool Available => _binder != null && _binder.Bound;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetSync");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);
            _hz = TwinUiCardParts.StatBlock(content, "采样频率", 0f, 22f, w * 0.55f);
            _quality = TwinUiCardParts.Caption(content, TwinUITextR3.NotAvailable, w * 0.55f, 22f, w * 0.45f);
            _quality.alignment = TextAlignmentOptions.Right;

            float rowsTop = 84f;
            _fresh = TwinUiCardParts.Row(content, rowsTop, "新鲜度", w);
            _latency = TwinUiCardParts.Row(content, rowsTop + 24f, "应用时延", w);
            _jitter = TwinUiCardParts.Row(content, rowsTop + 48f, "抖动 p99", w);
            _dropped = TwinUiCardParts.Row(content, rowsTop + 72f, "丢帧", w);
            _clock = TwinUiCardParts.Row(content, rowsTop + 96f, "时钟 / 质量", w);

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
                _quality.text = TwinUITextR3.NotAvailable;
                _fresh.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _latency.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _jitter.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _dropped.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _clock.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            var snap = _binder.Current;
            bool has = snap.HasFrame;
            // A 支：status 非正常时徽标转 Crit（颜色 + 文字双通道），不再一律"已连接"。
            TwinUITone badgeTone = !has ? TwinUITone.Unknown
                : (!snap.StatusProvided || snap.StatusNormal) ? TwinUITone.Ok : TwinUITone.Crit;
            string badgeText = has
                ? (snap.StatusProvided
                    ? (snap.StatusNormal ? "已连接" : snap.StatusText)
                    : (snap.Connected ? "已连接" : "无帧"))
                : TwinUITextR3.NotConnected;
            _badge.Set(badgeTone, badgeText);

            _hz.Set(has ? snap.Hz.ToString("F1") : TwinUITextR3.Placeholder,
                has ? TwinUITone.Ok : TwinUITone.Unknown, TwinUITextR3.UnitHz);

            _quality.text = has
                ? (string.IsNullOrEmpty(snap.Quality) ? TwinUITextR3.Placeholder : snap.Quality)
                : TwinUITextR3.NotAvailable;
            _quality.color = has ? TwinUIStyle.MutedColor : TwinUIStyle.QuietColor;

            TwinUITone freshTone = !has ? TwinUITone.Unknown
                : snap.AgeMs > 500 ? TwinUITone.Crit
                : snap.AgeMs > 150 ? TwinUITone.Warn : TwinUITone.Ok;
            _fresh.Set(has ? snap.AgeMs.ToString("F0") + TwinUITextR3.UnitMs : TwinUITextR3.Placeholder, freshTone);

            TwinUITone latTone = !has ? TwinUITone.Unknown
                : snap.LatencyMs > 200 ? TwinUITone.Crit
                : snap.LatencyMs > 80 ? TwinUITone.Warn : TwinUITone.Ok;
            _latency.Set(has ? snap.LatencyMs.ToString("F1") + TwinUITextR3.UnitMs : TwinUITextR3.Placeholder, latTone);

            _jitter.Set(has ? snap.JitterMs.ToString("F1") + TwinUITextR3.UnitMs : TwinUITextR3.Placeholder,
                has ? TwinUITone.Ok : TwinUITone.Unknown);

            TwinUITone dropTone = !has ? TwinUITone.Unknown : snap.Dropped > 0 ? TwinUITone.Warn : TwinUITone.Ok;
            _dropped.Set(has ? snap.Dropped + " / 队 " + snap.Queue : TwinUITextR3.Placeholder, dropTone);

            string clock = has ? snap.Clock : string.Empty;
            // A 支：回放时"时钟"一行显示**回放时间轴**与有效位（四时钟域不混，详见 Notes）；
            // 实时路径仍是原 ClockSync 文本。
            if (has && !string.IsNullOrEmpty(snap.ClockAxisText))
            {
                clock = snap.ClockAxisText;
                if (!string.IsNullOrEmpty(snap.ValidityText))
                {
                    clock = clock + " · " + snap.ValidityText;
                }
            }

            _clock.Set(has ? (string.IsNullOrEmpty(clock) ? TwinUITextR3.Placeholder : clock)
                : TwinUITextR3.Placeholder, has ? TwinUITone.Ok : TwinUITone.Unknown);
        }
    }
}