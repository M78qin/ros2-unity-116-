// TwinUiDataLinkModule.cs —— 数据链状态模块（P5 / task-13，第二个真实模块）
// 背景：原计划"实验事件流/设置"依赖 TwinUiIndustrialHost，而该宿主只在"工业数据台"页首次显示时 StartHost，
//       默认主视图（实训任务台）下不可用 → 会长期显示"未挂载"，不适合作为常驻模块。
// 本模块改用始终在场景中的 TwinUIStatusBinder.Current（数据链实时快照：连接/Hz/延迟/抖动/源/告警），
// 数据源真实、只读；binder 缺失时显示"未挂载"，不构造假数据。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiDataLinkModule : TwinUiModule
    {
        public const string ModuleId = "datalink";

        private readonly TwinUIStatusBinder _injected;
        private TwinUIStatusBinder _binder;
        private TwinUiDataLinkView _view;
        private bool _connected;
        private bool _hasFrame;
        private double _hz;
        private double _ageMs;
        private double _latencyMs;
        private double _jitterMs;
        private long _dropped;
        private string _sourceKind = "-";
        private string _sourceName = "-";
        private string _lastError = string.Empty;
        private string _mode = "-";
        private int _alarms;

        public TwinUiDataLinkModule(TwinUIStatusBinder binder)
        {
            _injected = binder;
            _binder = binder;
        }

        public override string Id => ModuleId;
        public override string Title => "数据链状态";
        public override string Subtitle => Available ? "TwinUIStatusBinder" : "未挂载";
        public override Vector2 CompactSize => new Vector2(250f, 92f);
        public override Vector2 ExpandSize => new Vector2(480f, 330f);

        public bool Available => _binder != null;
        public bool Connected => _connected;
        public bool HasFrame => _hasFrame;
        public double Hz => _hz;
        public double AgeMs => _ageMs;
        public double LatencyMs => _latencyMs;
        public double JitterMs => _jitterMs;
        public long Dropped => _dropped;
        public int AlarmCount => _alarms;
        public string SourceKind => _sourceKind;
        public string Mode => _mode;
        public string SourceName => _sourceName;
        public string LastError => _lastError;
        public TwinUiDataLinkView View2 => _view;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "DataLinkModule");
            View = root;
            _view = new TwinUiDataLinkView();
            _view.Build(root, this, IsExpanded);
            return root;
        }

        public override void OnExpandChanged(bool expanded)
        {
            if (_view != null)
            {
                _view.SetExpanded(expanded);
            }
        }

        public override void Refresh()
        {
            if (_binder == null)
            {
                _binder = _injected != null ? _injected : Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder == null)
            {
                _connected = false;
                _hasFrame = false;
                _hz = 0;
                _ageMs = 0;
                _latencyMs = 0;
                _jitterMs = 0;
                _dropped = 0;
                _sourceKind = "-";
                _sourceName = "-";
                _lastError = "binder 未挂载";
                _mode = "-";
                _alarms = 0;
            }
            else
            {
                var s = _binder.Current;
                _connected = s.Connected;
                _hasFrame = s.HasFrame;
                _hz = s.Hz;
                _ageMs = s.AgeMs;
                _latencyMs = s.LatencyMs;
                _jitterMs = s.JitterMs;
                _dropped = s.Dropped;
                _sourceKind = string.IsNullOrEmpty(s.SourceKind) ? "-" : s.SourceKind;
                _sourceName = string.IsNullOrEmpty(s.SourceName) ? "-" : s.SourceName;
                _lastError = s.LastError ?? string.Empty;
                _mode = string.IsNullOrEmpty(s.Mode) ? "-" : s.Mode;
                _alarms = _binder.Alarms != null ? _binder.Alarms.Count : 0;
            }

            if (_view != null)
            {
                _view.Refresh(this);
            }
        }

        public override void DestroyView()
        {
            _view = null;
            base.DestroyView();
        }
    }

    /// <summary>数据链视图：紧凑态（连接点 + Hz + 延迟）；展开态（源/帧龄/抖动/丢弃/告警）。</summary>
    public sealed class TwinUiDataLinkView
    {
        private RectTransform _root;
        private TwinUiDataLinkModule _module;
        private bool _expanded;
        private UnityEngine.UI.Image _dot;
        private TMP_Text _stateText;
        private TMP_Text _hzText;
        private TMP_Text _latencyText;
        private TMP_Text _detailText;

        public string StateDisplay => _stateText != null ? _stateText.text : string.Empty;
        public string HzDisplay => _hzText != null ? _hzText.text : string.Empty;
        public string DetailDisplay => _detailText != null ? _detailText.text : string.Empty;

        public void Build(RectTransform root, TwinUiDataLinkModule module, bool expanded)
        {
            _root = root;
            _module = module;
            _expanded = expanded;
            Clear();

            if (!module.Available)
            {
                var missing = TwinUIStyle.Text(_root, "Missing", "数据链状态 · 未挂载（场景中无 TwinUIStatusBinder）",
                    12f, TwinUIStyle.WarnColor, TextAlignmentOptions.Left);
                TwinUIStyle.Place(missing.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(10f, -10f), new Vector2(380f, 20f));
                return;
            }

            if (expanded)
            {
                BuildExpanded();
            }
            else
            {
                BuildCompact();
            }

            Refresh(module);
        }

        public void SetExpanded(bool expanded)
        {
            if (_expanded == expanded || _root == null)
            {
                return;
            }

            Build(_root, _module, expanded);
        }

        private void Clear()
        {
            _dot = null;
            _stateText = null;
            _hzText = null;
            _latencyText = null;
            _detailText = null;
            if (_root == null)
            {
                return;
            }

            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(_root.GetChild(i).gameObject);
            }
        }

        private void BuildCompact()
        {
            _dot = TwinUIStyle.Panel(_root, "Dot", TwinUIStyle.MutedColor, false);
            TwinUIStyle.Place(_dot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -14f), new Vector2(8f, 8f));

            _stateText = TwinUIStyle.Text(_root, "State", "-", 11f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_stateText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -8f), new Vector2(160f, 18f));

            _hzText = TwinUIStyle.Mono(_root, "Hz", "-", 19f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_hzText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -30f), new Vector2(120f, 26f));

            _latencyText = TwinUIStyle.Mono(_root, "Latency", "-", 12f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_latencyText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -58f), new Vector2(220f, 18f));
        }

        private void BuildExpanded()
        {
            var source = TwinUIStyle.Text(_root, "Source", "数据源：TwinUIStatusBinder.Current（数据链实时快照，只读）", 10f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(source.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -5f), new Vector2(430f, 14f));

            _dot = TwinUIStyle.Panel(_root, "Dot", TwinUIStyle.MutedColor, false);
            TwinUIStyle.Place(_dot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -30f), new Vector2(9f, 9f));
            _stateText = TwinUIStyle.Text(_root, "State", "-", 12f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_stateText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -24f), new Vector2(240f, 18f));

            _hzText = TwinUIStyle.Mono(_root, "Hz", "-", 17f, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_hzText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -46f), new Vector2(150f, 24f));

            _detailText = TwinUIStyle.Mono(_root, "Detail", "-", 10f, TwinUIStyle.TextColor,
                TextAlignmentOptions.TopLeft);
            _detailText.enableWordWrapping = false;
            TwinUIStyle.Place(_detailText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -76f), new Vector2(440f, 130f));

            _latencyText = TwinUIStyle.Text(_root, "Foot", "延迟/抖动/丢弃/告警均取自同一快照；无数据时显示原始占位符 '-'",
                9f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(_latencyText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -212f), new Vector2(440f, 14f));
        }

        public void Refresh(TwinUiDataLinkModule module)
        {
            if (_root == null || !module.Available)
            {
                return;
            }

            if (_dot != null)
            {
                _dot.color = module.Connected && module.HasFrame
                    ? TwinUIStyle.OkColor
                    : module.Connected ? TwinUIStyle.WarnColor : TwinUIStyle.CritColor;
            }

            if (_stateText != null)
            {
                _stateText.text = module.Connected
                    ? (module.HasFrame ? "已连接 · 有帧 " + module.SourceKind : "已连接 · 等待帧")
                    : "未连接 / " + module.SourceKind;
            }

            if (_hzText != null)
            {
                _hzText.text = module.Hz.ToString("F1") + " Hz";
            }

            if (_latencyText != null && !_expanded)
            {
                _latencyText.text = "延迟 " + module.LatencyMs.ToString("F1") + " ms · 抖动 "
                    + module.JitterMs.ToString("F1") + " ms";
            }

            if (_detailText != null)
            {
                _detailText.text =
                    "source=" + module.SourceKind + " / " + module.SourceName + "\n" +
                    "hz=" + module.Hz.ToString("F2") + "  age=" + module.AgeMs.ToString("F1") + "ms  latency="
                    + module.LatencyMs.ToString("F1") + "ms  jitter=" + module.JitterMs.ToString("F1") + "ms\n" +
                    "dropped=" + module.Dropped + "  alarms=" + module.AlarmCount + "  mode=" + module.Mode + "\n" +
                    "lastError=" + (string.IsNullOrEmpty(module.LastError) ? "(none)" : module.LastError);
            }
        }
    }
}