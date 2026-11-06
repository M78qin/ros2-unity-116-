// TwinUiTaskProgressModule.cs —— 拆解任务进度模块（P5 / task-13，第三个真实模块）
// 数据源：旧拆解系统 DisassemblySystem.Instance（当前层索引/层数/系统完成/可交互零件/检测模式），只读。
// 与性能模块（采样性能）、数据链模块（链路质量）互补，证明模块框架可承载不同业务数据。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiTaskProgressModule : TwinUiModule
    {
        public const string ModuleId = "task";

        private TwinUiTaskProgressView _view;
        private DisassemblySystem _system;
        private int _layerIndex = -1;
        private int _layerCount;
        private int _interactable;
        private bool _completed;
        private string _mode = "-";

        public override string Id => ModuleId;
        public override string Title => "拆解进度";
        public override string Subtitle => Available ? "DisassemblySystem" : "未挂载";
        public override Vector2 CompactSize => new Vector2(250f, 92f);
        public override Vector2 ExpandSize => new Vector2(480f, 330f);

        public bool Available => _system != null;
        public int LayerIndex => _layerIndex;
        public int LayerCount => _layerCount;
        public int Interactable => _interactable;
        public bool Completed => _completed;
        public string Mode => _mode;
        public TwinUiTaskProgressView View2 => _view;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "TaskProgressModule");
            View = root;
            _view = new TwinUiTaskProgressView();
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
            if (_system == null)
            {
                _system = DisassemblySystem.Instance;
            }

            if (_system == null)
            {
                _layerIndex = -1;
                _layerCount = 0;
                _interactable = 0;
                _completed = false;
                _mode = "-";
            }
            else
            {
                _layerIndex = _system.currentLayerIndex;
                _layerCount = _system.layers != null ? _system.layers.Count : 0;
                _completed = _system.isSystemCompleted;
                _mode = _system.detectionMode.ToString();
                try
                {
                    var parts = _system.GetCurrentInteractableParts();
                    _interactable = parts != null ? parts.Count : 0;
                }
                catch (System.Exception)
                {
                    _interactable = 0;
                }
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

        public string DescribeLine()
        {
            if (!Available)
            {
                return "未挂载";
            }

            return "layer=" + (_layerIndex + 1) + "/" + _layerCount + " interactable=" + _interactable
                + " completed=" + _completed + " mode=" + _mode;
        }
    }

    /// <summary>任务进度视图：紧凑态（层 x/y + 可交互零件数）；展开态完整字段。</summary>
    public sealed class TwinUiTaskProgressView
    {
        private RectTransform _root;
        private TwinUiTaskProgressModule _module;
        private bool _expanded;
        private TMP_Text _layerText;
        private TMP_Text _interactableText;
        private TMP_Text _detailText;

        public string LayerDisplay => _layerText != null ? _layerText.text : string.Empty;
        public string InteractableDisplay => _interactableText != null ? _interactableText.text : string.Empty;
        public string DetailDisplay => _detailText != null ? _detailText.text : string.Empty;

        public void Build(RectTransform root, TwinUiTaskProgressModule module, bool expanded)
        {
            _root = root;
            _module = module;
            _expanded = expanded;
            Clear();

            if (!module.Available)
            {
                var missing = TwinUIStyle.Text(_root, "Missing", "拆解进度 · 未挂载（场景中无 DisassemblySystem）",
                    12f, TwinUIStyle.WarnColor, TextAlignmentOptions.Left);
                TwinUIStyle.Place(missing.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(10f, -10f), new Vector2(360f, 20f));
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
            _layerText = null;
            _interactableText = null;
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
            var head = TwinUIStyle.Text(_root, "Head", "当前层 / 可交互零件", 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -6f), new Vector2(200f, 12f));

            _layerText = TwinUIStyle.Mono(_root, "Layer", "-", 17f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_layerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -20f), new Vector2(130f, 22f));

            _interactableText = TwinUIStyle.Mono(_root, "Interactable", "-", 17f, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_interactableText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(140f, -20f), new Vector2(90f, 22f));

            var foot = TwinUIStyle.Text(_root, "Foot", "只读：DisassemblySystem 运行时状态", 9f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(foot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -46f), new Vector2(230f, 12f));
        }

        private void BuildExpanded()
        {
            var source = TwinUIStyle.Text(_root, "Source", "数据源：DisassemblySystem.Instance（旧拆解系统运行时状态，只读）",
                10f, TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(source.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -5f), new Vector2(440f, 14f));

            var head = TwinUIStyle.Text(_root, "Head", "当前层", 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -22f), new Vector2(120f, 12f));
            _layerText = TwinUIStyle.Mono(_root, "Layer", "-", 18f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_layerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -34f), new Vector2(160f, 24f));

            var head2 = TwinUIStyle.Text(_root, "Head2", "可交互零件", 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(head2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(200f, -22f), new Vector2(120f, 12f));
            _interactableText = TwinUIStyle.Mono(_root, "Interactable", "-", 18f, TwinUIStyle.AccentColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_interactableText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(200f, -34f), new Vector2(120f, 24f));

            _detailText = TwinUIStyle.Mono(_root, "Detail", "-", 11f, TwinUIStyle.TextColor,
                TextAlignmentOptions.TopLeft);
            TwinUIStyle.Place(_detailText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -70f), new Vector2(440f, 90f));
        }

        public void Refresh(TwinUiTaskProgressModule module)
        {
            if (_root == null || !module.Available)
            {
                return;
            }

            if (_layerText != null)
            {
                _layerText.text = (module.LayerIndex >= 0 ? (module.LayerIndex + 1).ToString() : "-")
                    + " / " + module.LayerCount;
            }

            if (_interactableText != null)
            {
                _interactableText.text = module.Interactable.ToString();
            }

            if (_detailText != null)
            {
                _detailText.text = "layerIndex=" + module.LayerIndex + "  layers=" + module.LayerCount
                    + "  interactableParts=" + module.Interactable + "\n"
                    + "systemCompleted=" + module.Completed + "  detectionMode=" + module.Mode + "\n"
                    + "（旧拆解系统 debug overlay 仍保留，本模块只读其状态）";
            }
        }
    }
}