// TwinUiWidgetPerformance.cs —— 性能监测卡片（P8，总览页）
// 数据源：既有 PerformanceProfiler.TryGetSnapshot（只读，口径与 CSV 不变）。
// 显示接管：卡片显示时把旧 IMGUI 面板关掉（与原 TwinUiPerformanceModule 同一约定），隐藏时还原用户原值。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetPerformance : TwinUiModule
    {
        public const string ModuleId = "performance";
        public const float HistoryInterval = 0.25f;
        public const int HistoryLength = 60;

        private PerformanceProfiler _profiler;
        private PerformanceProfiler.PerfSnapshot _snapshot;
        private bool _originalVisible;
        private bool _hasOriginal;
        private float _lastHistoryAt = -1f;
        private readonly float[] _fpsHistory = new float[HistoryLength];
        private int _historyCount;
        private int _historyHead;

        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _fps;
        private TwinUiCardParts.Kv _frame;
        private TwinUiCardParts.Kv _engine;
        private TwinUiCardParts.Kv _managed;
        private TwinUiCardParts.Kv _renderers;
        private TMP_Text _spark;

        public override string Id => ModuleId;
        public override string Title => "性能监测";
        public override string Kicker => "PERF";
        public override string Subtitle => "PerformanceProfiler";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(340f, 226f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 200f);

        public bool ProfilerAvailable => _profiler != null;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetPerformance");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _fps = TwinUiCardParts.StatBlock(root, "帧率", 0f, 22f, w * 0.5f);
            _frame = TwinUiCardParts.Row(root, 80f, "帧时间", w);
            _engine = TwinUiCardParts.Row(root, 104f, "引擎内存", w);
            _managed = TwinUiCardParts.Row(root, 128f, "托管内存", w);
            _renderers = TwinUiCardParts.Row(root, 152f, "活跃渲染器", w);

            // 迷你趋势用文字条（P8 不引入新图元；曲线卡在 P9 统一做）。
            _spark = TwinUiCardParts.Caption(root, string.Empty, 0f, 178f, w, 18f);
            _spark.color = TwinUIStyle.QuietColor;
            return root;
        }

        public override void OnAttach()
        {
            _profiler = _profiler != null ? _profiler : Object.FindObjectOfType<PerformanceProfiler>();
            if (_profiler == null)
            {
                return;
            }

            if (!_hasOriginal)
            {
                _originalVisible = _profiler.IsOnScreenUIVisible();
                _hasOriginal = true;
            }

            _profiler.SetOnScreenUI(false, "widget.attach");
        }

        public override void OnDetach()
        {
            // P9：V2 里旧 IMGUI 面板已被 TwinUiScatteredUiBridge 永久收编，翻页/归位时不再恢复它，
            // 避免"切走总览就弹回左上角性能窗"这种散落 UI 回流。
            if (TwinUiShellMode.IsV2)
            {
                return;
            }

            if (_profiler != null && _hasOriginal)
            {
                _profiler.SetOnScreenUI(_originalVisible, "widget.detach");
            }
        }

        public override void Refresh()
        {
            if (_profiler == null)
            {
                _profiler = Object.FindObjectOfType<PerformanceProfiler>();
            }

            if (_profiler == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 PerformanceProfiler");
                _fps.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _frame.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _engine.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _managed.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _renderers.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _spark.text = string.Empty;
                return;
            }

            if (!_profiler.TryGetSnapshot(out var snapshot))
            {
                snapshot = new PerformanceProfiler.PerfSnapshot { valid = false };
            }

            _snapshot = snapshot;
            bool valid = _snapshot.valid;
            float fps = _snapshot.fps;
            TwinUITone tone = !valid ? TwinUITone.Unknown
                : fps < 30f ? TwinUITone.Crit
                : fps < 50f ? TwinUITone.Warn : TwinUITone.Ok;
            _badge.Set(tone, valid ? "采样正常" : "无采样");
            _fps.Set(valid ? fps.ToString("F1") : TwinUITextR3.Placeholder, tone, " fps");
            _frame.Set(valid ? _snapshot.frameMs.ToString("F2") + TwinUITextR3.UnitMs : TwinUITextR3.Placeholder,
                tone);
            _engine.Set(valid ? _snapshot.engineAllocatedMB.ToString("F1") + " MB" : TwinUITextR3.Placeholder,
                valid ? TwinUITone.Ok : TwinUITone.Unknown);
            _managed.Set(valid ? _snapshot.managedMB.ToString("F1") + " MB" : TwinUITextR3.Placeholder,
                valid ? TwinUITone.Ok : TwinUITone.Unknown);
            _renderers.Set(valid ? _snapshot.activeRenderers.ToString() : TwinUITextR3.Placeholder,
                valid ? TwinUITone.Ok : TwinUITone.Unknown);

            if (_lastHistoryAt < 0f || Time.unscaledTime - _lastHistoryAt >= HistoryInterval)
            {
                _lastHistoryAt = Time.unscaledTime;
                _fpsHistory[_historyHead] = valid ? fps : 0f;
                _historyHead = (_historyHead + 1) % HistoryLength;
                _historyCount = Mathf.Min(HistoryLength, _historyCount + 1);
            }

            if (_historyCount > 0)
            {
                float min = float.MaxValue;
                float max = float.MinValue;
                float sum = 0f;
                for (int i = 0; i < _historyCount; i++)
                {
                    float v = _fpsHistory[i];
                    min = Mathf.Min(min, v);
                    max = Mathf.Max(max, v);
                    sum += v;
                }

                _spark.text = "最近 " + (_historyCount * HistoryInterval).ToString("F0") + " s  fps  min "
                    + min.ToString("F0") + " / avg " + (sum / _historyCount).ToString("F0") + " / max "
                    + max.ToString("F0");
            }
        }
    }
}