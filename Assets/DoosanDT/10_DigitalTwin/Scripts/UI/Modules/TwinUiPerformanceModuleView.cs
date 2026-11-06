// TwinUiPerformanceModuleView.cs —— 性能模块视图（P4 / task-12）
// 紧凑态：一行 KPI（FPS / 帧 ms / 内存）；展开态：全部指标 + 最近 60 s FPS 折线 + 导出 CSV。
// 全部控件走 TwinUIStyle/TwinUITheme 令牌；数据来自 PerformanceProfiler.TryGetSnapshot。
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiPerformanceModuleView
    {
        private sealed class MetricRow
        {
            public string Label;
            public Func<PerformanceProfiler.PerfSnapshot, string> Format;
            public TMP_Text Value;
        }

        private RectTransform _root;
        private TwinUiPerformanceModule _module;
        private bool _floating;
        private bool _expanded;
        private TMP_Text _fpsText;
        private TMP_Text _frameText;
        private TMP_Text _memoryText;
        private TMP_Text _statusText;
        private TMP_Text _scopeText;
        private TMP_Text _exportHint;
        private Image _statusDot;
        private TwinUiFpsSparkline _sparkline;
        private readonly List<MetricRow> _rows = new List<MetricRow>();

        public bool IsExpanded => _expanded;
        public bool IsFloating => _floating;
        public string FpsDisplay => _fpsText != null ? _fpsText.text : string.Empty;
        public string FrameDisplay => _frameText != null ? _frameText.text : string.Empty;
        public string MemoryDisplay => _memoryText != null ? _memoryText.text : string.Empty;
        public TMP_Text FpsLabel => _fpsText;
        public TMP_Text FrameLabel => _frameText;
        public TMP_Text MemoryLabel => _memoryText;
        public TwinUiFpsSparkline Sparkline => _sparkline;
        public int MetricRowCount => _rows.Count;

        public void Build(RectTransform root, TwinUiPerformanceModule module, bool floating, bool expanded)
        {
            _root = root;
            _module = module;
            _floating = floating;
            _expanded = expanded;
            Clear();

            if (!module.ProfilerAvailable)
            {
                BuildMissing();
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

            Build(_root, _module, _floating, expanded);
        }

        private void Clear()
        {
            _rows.Clear();
            _fpsText = null;
            _frameText = null;
            _memoryText = null;
            _statusText = null;
            _scopeText = null;
            _exportHint = null;
            _statusDot = null;
            _sparkline = null;
            if (_root == null)
            {
                return;
            }

            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_root.GetChild(i).gameObject);
            }
        }

        // ------------------------------------------------------------- 未挂载

        private void BuildMissing()
        {
            var title = TwinUIStyle.Text(_root, "MissingTitle", "性能监测 · 未挂载", 14f,
                TwinUIStyle.WarnColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -8f), new Vector2(280f, 22f));

            var note = TwinUIStyle.Text(_root, "MissingNote",
                "场景中未找到 PerformanceProfiler；本模块只显示真实采样，不构造假数据。", 11f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -34f), new Vector2(360f, 40f));

            _statusText = note;
        }

        // ------------------------------------------------------------- 紧凑态

        private void BuildCompact()
        {
            const float pad = 12f;
            _fpsText = AddKpi(pad, 0f, 74f, "FPS", out var fpsLabel);
            fpsLabel.text = "FPS";
            _frameText = AddKpi(pad + 92f, 0f, 92f, "FRAME MS", out _);
            _memoryText = AddKpi(pad + 196f, 0f, 120f, "MEM MB", out _);

            _statusDot = TwinUIStyle.Panel(_root, "StatusDot", TwinUIStyle.OkColor, false);
            TwinUIStyle.Place(_statusDot.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-58f, 6f), new Vector2(8f, 8f));
            _statusText = TwinUIStyle.Text(_root, "Status", "LIVE", 10f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Place(_statusText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f, 6f), new Vector2(44f, 14f));

            var hint = TwinUIStyle.Text(_root, "Hint", "展开可看全部指标与 60 s 曲线", 9f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Right);
            TwinUIStyle.Place(hint.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f, -8f), new Vector2(200f, 12f));
        }

        private TMP_Text AddKpi(float x, float y, float width, string title, out TMP_Text label)
        {
            var head = TwinUIStyle.Text(_root, "Kpi_" + title, title, 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y - 6f), new Vector2(width, 12f));
            var value = TwinUIStyle.Mono(_root, "Val_" + title, "--", 19f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y - 18f), new Vector2(width + 10f, 26f));
            label = head;
            return value;
        }

        // ------------------------------------------------------------- 展开态

        private void BuildExpanded()
        {
            _scopeText = TwinUIStyle.Text(_root, "Scope", "数据源：PerformanceProfiler（旧拆解系统，只读）", 11f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(_scopeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -6f), new Vector2(420f, 16f));

            _fpsText = AddKpi(12f, 22f, 74f, "FPS", out _);
            _frameText = AddKpi(104f, 22f, 92f, "FRAME MS", out _);
            _memoryText = AddKpi(208f, 22f, 120f, "MEM MB", out _);

            _statusDot = TwinUIStyle.Panel(_root, "StatusDot", TwinUIStyle.OkColor, false);
            TwinUIStyle.Place(_statusDot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-58f, -28f), new Vector2(8f, 8f));
            _statusText = TwinUIStyle.Text(_root, "Status", "LIVE", 10f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Right);
            TwinUIStyle.Place(_statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-10f, -25f), new Vector2(44f, 14f));

            var chartGo = TwinUIStyle.Frame(_root, "FpsChart");
            chartGo.anchorMin = new Vector2(0f, 1f);
            chartGo.anchorMax = new Vector2(1f, 1f);
            chartGo.pivot = new Vector2(0.5f, 1f);
            chartGo.offsetMin = new Vector2(12f, 0f);
            chartGo.offsetMax = new Vector2(-12f, 0f);
            chartGo.sizeDelta = new Vector2(-24f, 74f);
            chartGo.anchoredPosition = new Vector2(0f, -78f);
            chartGo.gameObject.AddComponent<CanvasRenderer>();
            _sparkline = chartGo.gameObject.AddComponent<TwinUiFpsSparkline>();
            _sparkline.ApplyTheme();

            var chartHint = TwinUIStyle.Text(_root, "ChartHint", "最近 60 s FPS（0.25 s 采样）", 9f,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            TwinUIStyle.Place(chartHint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -152f), new Vector2(220f, 12f));

            AddMetric(0, "活跃渲染器", s => s.activeRenderers.ToString("N0"));
            AddMetric(1, "活跃三角面", s => s.activeTriangles.ToString("N0"));
            AddMetric(2, "活跃碰撞体", s => s.activeColliders.ToString("N0"));
            AddMetric(3, "零件（剩余/总）", s => s.remainingParts.ToString("N0") + " / " + s.totalParts.ToString("N0"));
            AddMetric(4, "当前层进度", s => (s.currentLayerProgress * 100f).ToString("F1") + "%");
            AddMetric(5, "层数量 / 模式", s => s.layerCount + " / " + (string.IsNullOrEmpty(s.currentMode) ? "-" : s.currentMode));

            _exportHint = TwinUIStyle.Text(_root, "ExportHint", "CSV: -", 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(_exportHint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(108f, 8f), new Vector2(340f, 12f));

            var export = TwinUIStyle.Button(_root, "BtnExportCsv", "导出 CSV", TwinUIButton.Kind.Default,
                new Vector2(0f, 0f), new Vector2(92f, 24f), _module.ExportCsv);
            TwinUIStyle.Place((RectTransform)export.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 6f), new Vector2(92f, 24f));
        }

        private void AddMetric(int index, string label, Func<PerformanceProfiler.PerfSnapshot, string> format)
        {
            int column = index % 2;
            int row = index / 2;
            float x = 12f + column * 220f;
            float y = -170f - row * 22f;

            var head = TwinUIStyle.Text(_root, "M_" + label, label, 9f, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(130f, 16f));

            var value = TwinUIStyle.Mono(_root, "V_" + label, "--", 11f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Left);
            TwinUIStyle.Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x + 132f, y), new Vector2(84f, 16f));

            _rows.Add(new MetricRow { Label = label, Format = format, Value = value });
        }

        // ------------------------------------------------------------- 刷新

        public void Refresh(TwinUiPerformanceModule module)
        {
            if (_root == null)
            {
                return;
            }

            if (!module.ProfilerAvailable)
            {
                return;
            }

            var s = module.Snapshot;
            bool valid = s.valid;
            if (_fpsText != null)
            {
                _fpsText.text = valid ? s.fps.ToString("F1") : "--";
            }

            if (_frameText != null)
            {
                _frameText.text = valid ? s.frameMs.ToString("F2") : "--";
            }

            if (_memoryText != null)
            {
                _memoryText.text = valid ? s.processWorkingSetMB.ToString("F0") : "--";
            }

            if (_statusText != null)
            {
                _statusText.text = valid ? "LIVE" : "WAIT";
            }

            if (_statusDot != null && valid)
            {
                _statusDot.color = s.fps >= 55f ? TwinUIStyle.OkColor
                    : s.fps >= 30f ? TwinUIStyle.WarnColor
                    : TwinUIStyle.CritColor;
            }

            if (_exportHint != null)
            {
                _exportHint.text = string.IsNullOrEmpty(s.csvPath) ? "CSV: -" : "CSV: " + Path.GetFileName(s.csvPath);
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Value != null)
                {
                    row.Value.text = valid ? row.Format(s) : "--";
                }
            }

            if (_sparkline != null)
            {
                _sparkline.SetValues(module.FpsHistory);
            }
        }
    }
}