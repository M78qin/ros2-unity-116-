// TwinUiWidgetTask.cs —— 拆解进度卡片（P8，总览/拆解实训共用）
// 数据源：既有 DisassemblySystem.Instance（只读：当前层索引 / 层数 / 可交互零件 / 系统完成 / 检测模式）。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetTask : TwinUiModule
    {
        public const string ModuleId = "task";

        private DisassemblySystem _system;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _layer;
        private TwinUiCardParts.Bar _bar;
        private TMP_Text _barCaption;
        private TwinUiCardParts.Kv _interactable;
        private TwinUiCardParts.Kv _completed;
        private TwinUiCardParts.Kv _mode;

        public override string Id => ModuleId;
        public override string Title => "拆解进度";
        public override string Kicker => "DISASSEMBLY";
        public override string Subtitle => "DisassemblySystem";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(320f, 220f);
        public override Vector2 FloatingMinSize => new Vector2(280f, 190f);

        public bool Available => _system != null;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetTask");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _layer = TwinUiCardParts.StatBlock(root, "当前层级", 0f, 22f, w * 0.56f);
            _bar = TwinUiCardParts.Progress(root, 78f, w, 5f);
            _barCaption = TwinUiCardParts.Caption(root, string.Empty, 0f, 86f, w, 16f);
            _interactable = TwinUiCardParts.Row(root, 108f, "可交互零件", w);
            _completed = TwinUiCardParts.Row(root, 132f, "系统完成", w);
            _mode = TwinUiCardParts.Row(root, 156f, "检测模式", w);
            return root;
        }

        public override void Refresh()
        {
            if (_system == null)
            {
                _system = DisassemblySystem.Instance;
            }

            if (_system == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 DisassemblySystem");
                _layer.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _bar.Set(0f, TwinUITone.Unknown, TwinUITextR3.NotAvailable);
                _barCaption.text = TwinUITextR3.NotAvailable;
                _interactable.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _completed.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _mode.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            int layerCount = _system.layers != null ? _system.layers.Count : 0;
            int index = _system.currentLayerIndex;
            bool completed = _system.isSystemCompleted;
            float ratio = completed ? 1f : (layerCount <= 0 ? 0f : Mathf.Clamp01((index + 0f) / layerCount));
            int interactable = 0;
            try
            {
                var parts = _system.GetCurrentInteractableParts();
                interactable = parts != null ? parts.Count : 0;
            }
            catch (System.Exception)
            {
                interactable = 0;
            }

            TwinUITone tone = completed ? TwinUITone.Ok : interactable > 0 ? TwinUITone.Ok : TwinUITone.Warn;
            _badge.Set(tone, completed ? "已完成" : interactable > 0 ? "可拆解" : "等待解锁");
            _layer.Set(layerCount > 0 ? (index + 1) + " / " + layerCount : TwinUITextR3.Placeholder,
                completed ? TwinUITone.Ok : TwinUITone.Unknown);
            _bar.Set(ratio, tone);
            _barCaption.text = "整体进度 " + Mathf.RoundToInt(ratio * 100f) + " %（按当前层/总层数估算）";
            _interactable.Set(interactable.ToString(), interactable > 0 ? TwinUITone.Ok : TwinUITone.Unknown);
            _completed.Set(completed ? "是" : "否", completed ? TwinUITone.Ok : TwinUITone.Warn);
            _mode.Set(_system.detectionMode.ToString(), TwinUITone.Ok);
        }
    }
}