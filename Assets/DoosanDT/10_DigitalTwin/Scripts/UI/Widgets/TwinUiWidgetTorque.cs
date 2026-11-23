// TwinUiWidgetTorque.cs —— 关节力矩卡片（P10 响应式三档：Compact / Normal / Expanded）
// 数据源不变：TwinUIStatusBinder.Current.JointTorque（N·m，5Hz 只读快照）。
// 采样不变：Refresh 内 4 Hz（0.25 s）节流，每关节 96 点缓冲（≈24 s）。
// 布局：响应式三档，无 localScale；只按 contentWidth 重排已存在子节点（RectTransform / SetActive），
//       图表内部节点由 TwinUiLineChart 自绘，不在此处改动。无帧只显示 —，禁止补 0。
// 另：WideContent=true —— 两列布局时本卡跨满整行，优先拿 Expanded 档。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetTorque : TwinUiModule
    {
        public const string ModuleId = "torque";

        private const int JointCount = 6;
        // R2 T6：缓冲按时间戳裁剪；20 Hz × 60 s = 1200 点，留余量。
        private const int BufferCapacity = 2048;
        private const float MinSampleInterval = 1f / 30f;

        // 本批（回放 UI）：图表控制行（冻结 / 通道开关）与时间窗（10/20/60 s，页面级可选；见 Joints 同款实现）。
        private const float ControlRowHeight = 20f;
        private const float ControlRowGap = 2f;
        private const float FreezeButtonWidth = 56f;
        private const float ChannelButtonWidth = 34f;
        private const float ChannelButtonStep = 36f;
        private const int MaxWindowPoints = 240; // 旧点数窗上限（R2 起改用时间戳窗，仅保留给兼容路径）

        private TwinUIButton _freezeButton;
        private TwinUIButton[] _channelButtons;
        private bool _frozenShown;

        // A4：源分段号；A2：当前时间窗（秒）。两者都只影响显示。
        private int _segment = -1;
        private float _windowSeconds = -1f;

        private TwinUIStatusBinder _binder;
        private TwinUiLineChart _chart;
        private RectTransform _chartRect;
        private RectTransform _badgeRoot;
        private RectTransform _summaryRect;
        private TwinUiCardParts.Badge _badge;
        private TMP_Text _summary;
        private TMP_Text[] _cellValues;
        private RectTransform[] _cellRoots;
        private RectTransform[] _cellDots;
        private RectTransform[] _cellLabels;
        private readonly TwinUiLineChartBuffer[] _buffers = new TwinUiLineChartBuffer[JointCount];
        private readonly List<float>[] _seriesValues = new List<float>[JointCount];
        // R2 T6：与 _seriesValues 平行的时间戳（秒），交给图表按时间戳裁剪。
        private readonly List<float>[] _seriesTimes = new List<float>[JointCount];
        private readonly List<float> _empty = new List<float>(0);

        private float _lastSampleAt = -1f;
        private bool _streaming;

        public override string Id => ModuleId;
        public override string Title => "关节力矩";
        public override string Kicker => "TORQUE";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override string Group => "torque";
        public override Vector2 CardSize => new Vector2(360f, 248f);

        public override bool ResponsiveContent => true;
        public override bool WideContent => true;

        /// <summary>最近一次排版采用的密度档（供 UI/54 自检）。</summary>
        public TwinUiCardMode LastMode { get; private set; }

        /// <summary>最近一次排版使用的内容宽（供 UI/54 自检）。</summary>
        public float LastContentWidth { get; private set; }

        public override float MeasureContentHeight(float contentWidth, TwinUiModuleSurface surface)
        {
            float w = Mathf.Max(80f, contentWidth);
            TwinUiLayoutContract.SampleChartMetrics(TwinUiLayoutContract.ModeFor(w),
                out float chartHeight, out int columns, out float rowStep);
            int rows = Mathf.CeilToInt(JointCount / (float)columns);
            float extra = surface == TwinUiModuleSurface.Floating ? 0f : ControlRowHeight + ControlRowGap;
            float chartTop = TwinUiLayoutContract.SampleChartTop + extra;
            float height = chartTop + chartHeight
                + TwinUiLayoutContract.SampleChartGap + rows * rowStep;
            return float.IsNaN(height) || float.IsInfinity(height) || height < 0f ? 0f : height;
        }

        public override void ApplyContentLayout(float contentWidth, TwinUiModuleSurface surface)
        {
            if (_chartRect == null || _badgeRoot == null || _summaryRect == null || _cellRoots == null)
            {
                return;
            }

            float w = Mathf.Max(80f, contentWidth);
            TwinUiCardMode mode = TwinUiLayoutContract.ModeFor(w);
            if (mode == LastMode && Mathf.Abs(w - LastContentWidth) < 0.01f)
            {
                return;
            }

            LastMode = mode;
            LastContentWidth = w;

            TwinUiLayoutContract.SampleChartMetrics(mode, out float chartHeight, out int columns, out float rowStep);

            // 顶部：徽标 + 摘要（Compact 隐藏摘要）。
            float badgeWidth = w * 0.45f;
            TwinUIStyle.Row(_badgeRoot, 0f, 0f, badgeWidth, TwinUiLayoutContract.SampleBadgeHeight);
            if (_badge.Text != null)
            {
                TwinUIStyle.Row(_badge.Text.rectTransform, 10f, 0f, Mathf.Max(40f, badgeWidth - 10f),
                    TwinUiLayoutContract.SampleBadgeHeight);
            }

            TwinUIStyle.Row(_summaryRect, w * 0.48f, 0f, w * 0.52f, 16f);
            _summary.gameObject.SetActive(mode != TwinUiCardMode.Compact);

            // 控制行：冻结显示 + 六通道开关（只切显示，不停数据源/记录）。浮窗只用于放大查看，
            // 不再插入控制行（保持"图表占浮窗高度一半以上"的呈现契约，UI/56）。
            bool showControls = surface != TwinUiModuleSurface.Floating;
            float controlTop = TwinUiLayoutContract.SampleChartTop;
            if (_freezeButton != null)
            {
                _freezeButton.gameObject.SetActive(showControls);
                if (showControls)
                {
                    TwinUIStyle.Row((RectTransform)_freezeButton.transform, 0f, controlTop, FreezeButtonWidth,
                        ControlRowHeight - 2f);
                }
            }

            if (_channelButtons != null)
            {
                for (int i = 0; i < _channelButtons.Length; i++)
                {
                    if (_channelButtons[i] == null)
                    {
                        continue;
                    }

                    _channelButtons[i].gameObject.SetActive(showControls);
                    if (!showControls)
                    {
                        continue;
                    }

                    TwinUIStyle.Row((RectTransform)_channelButtons[i].transform,
                        FreezeButtonWidth + 6f + i * ChannelButtonStep, controlTop, ChannelButtonWidth,
                        ControlRowHeight - 2f);
                }
            }

            // 图表：只改外层 Rect；内部图例/网格/折线由控件自绘。
            float chartTop = TwinUiLayoutContract.SampleChartTop + (showControls ? ControlRowHeight + ControlRowGap : 0f);
            TwinUIStyle.Row(_chartRect, 0f, chartTop, w, chartHeight);

            // 数值网格：cols × rows，格内 圆点 / 标签 / 右对齐数值。
            float cellWidth = (w - (columns - 1) * TwinUiLayoutContract.SampleCellGap) / columns;
            float cellHeight = rowStep - 2f;
            float labelWidth = TwinUiLayoutContract.SampleCellValueOffset - TwinUiLayoutContract.SampleCellLabelWidth;
            float cellTop = chartTop + chartHeight + TwinUiLayoutContract.SampleChartGap;

            for (int i = 0; i < JointCount; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float x = column * (cellWidth + TwinUiLayoutContract.SampleCellGap);
                float y = cellTop + row * rowStep;

                TwinUIStyle.Row(_cellRoots[i], x, y, cellWidth, cellHeight);
                TwinUIStyle.Row(_cellDots[i], 2f, Mathf.Max(0f, (cellHeight - 6f) * 0.5f), 6f, 6f);
                TwinUIStyle.Row(_cellLabels[i], 12f, 0f, labelWidth, cellHeight);
                TwinUIStyle.Row(_cellValues[i].rectTransform, TwinUiLayoutContract.SampleCellValueOffset, 0f,
                    Mathf.Max(1f, cellWidth - TwinUiLayoutContract.SampleCellValueOffset), cellHeight);
            }
        }

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetTorque");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);

            float w = Mathf.Max(80f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w * 0.45f);
            _badgeRoot = _badge.Text != null ? _badge.Text.transform.parent as RectTransform : null;

            _summary = TwinUiCardParts.Caption(content,
                "最大 |τ| " + TwinUITextR3.Placeholder, w * 0.48f, 0f, w * 0.52f, 16f);
            _summary.alignment = TextAlignmentOptions.Right;
            _summary.enableWordWrapping = false;
            _summary.color = TwinUIStyle.QuietColor;
            _summaryRect = _summary.rectTransform;

            BuildControls(content);

            _chart = BuildChart(content, TwinUITextR3.UnitNm);
            _chartRect = _chart.rectTransform;
            ApplyTimeWindow(true);

            _cellValues = new TMP_Text[JointCount];
            _cellRoots = new RectTransform[JointCount];
            _cellDots = new RectTransform[JointCount];
            _cellLabels = new RectTransform[JointCount];
            for (int i = 0; i < JointCount; i++)
            {
                _cellValues[i] = BuildCell(content, i, "J" + (i + 1),
                    out _cellRoots[i], out _cellDots[i], out _cellLabels[i]);
            }

            ResetBuffers();
            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);

            // 首帧排版：Build 可重入，强制走一次全量排布。
            LastContentWidth = -1f;
            ApplyContentLayout(w, TwinUiModuleSurface.Card);
            ApplyFreezeVisual();
            return root;
        }

        public override void Refresh()
        {
            if (_chart == null || _cellValues == null)
            {
                return;
            }

            if (_frozenShown != TwinUiViewFreeze.IsFrozen)
            {
                ApplyFreezeVisual();
            }

            // 查看冻结：只停"显示"刷新；数据源/回放钟/TwinRecorder 继续走。
            // A4/T7 修复：结构性更新（换源清缓冲/换窗换裁剪）必须在冻结判断之前执行，理由见 Joints。
            ApplySourceSegment();
            ApplyTimeWindow(false);

            if (TwinUiViewFreeze.IsFrozen)
            {
                return;
            }

            if (_binder == null)
            {
                _binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 StatusBinder");
                _summary.text = "最大 |τ| " + TwinUITextR3.Placeholder;
                SetCells(null);
                ClearChart();
                _streaming = false;
                return;
            }

            var snap = _binder.Current;
            float[] torque = snap.JointTorque;
            bool hasFrame = snap.HasFrame && torque != null && torque.Length > 0;
            bool complete = hasFrame && torque.Length >= JointCount;

            TwinUITone tone = !hasFrame ? TwinUITone.Unknown : complete ? TwinUITone.Ok : TwinUITone.Warn;
            string state = !hasFrame
                ? (snap.Connected ? "无帧" : TwinUITextR3.NotConnected)
                : complete ? "已连接" : "数据不完整";
            _badge.Set(hasFrame ? tone : (snap.Connected ? TwinUITone.Warn : TwinUITone.Unknown), state);

            if (!complete)
            {
                if (_streaming)
                {
                    ResetBuffers();
                    _streaming = false;
                }

                ClearChart();
                SetCells(hasFrame ? torque : null);
                _summary.text = "最大 |τ| " + TwinUITextR3.Placeholder;
                if (_chart != null)
                {
                    _chart.SetReadout(string.Empty);
                }

                return;
            }

            if (!_streaming)
            {
                ResetBuffers();
                _streaming = true;
                _lastSampleAt = -1f;
            }

            float now = Time.unscaledTime;
            // R2 T5/T6：采样间隔跟随**显示刷新率**（不是固定 0.25 s），时间戳裁剪才有一致的时间跨度。
            // 0.9 系数：宿主刷新节拍与这里同源（都取 DisplayHz），不留容差会因抖动丢一半样本。
            float interval = SampleIntervalSeconds * 0.9f;
            if (_lastSampleAt < 0f || now - _lastSampleAt >= interval)
            {
                _lastSampleAt = now;
                _chartPushCount++;
                for (int i = 0; i < JointCount; i++)
                {
                    // R2 T6：缓冲只按容量保留，不在写入时按窗口裁剪（理由见 Joints 同名注释）。
                    _buffers[i].Push(torque[i], now);
                }
            }

            float maxAbs = 0f;
            for (int i = 0; i < JointCount; i++)
            {
                _seriesValues[i].Clear();
                _seriesTimes[i].Clear();
                _buffers[i].CopyTo(_seriesValues[i]);
                _buffers[i].CopyTimesTo(_seriesTimes[i]);
                _chart.SetSeries(i, _seriesValues[i], _seriesTimes[i]);

                float abs = Mathf.Abs(torque[i]);
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                }
            }

            SetCells(torque);
            _summary.text = "最大 |τ| " + maxAbs.ToString("F2") + TwinUITextR3.UnitNm;

            // A2：曲线末点数值读数（与六格数值同源，都是本帧快照，不补 0）。
            _chart.SetReadout("末点 J1 " + torque[0].ToString("F2") + TwinUITextR3.UnitNm);
        }

        /// <summary>A4：源切换（Segment 变化）→ 清空本卡历史，曲线不许跨源连线。</summary>
        private void ApplySourceSegment()
        {
            int segment = TwinUiSourceSwitch.Segment;
            if (segment == _segment)
            {
                return;
            }

            _segment = segment;
            ResetBuffers();
            _streaming = false;
            _lastSampleAt = -1f;
            ClearChart();
            if (_chart != null)
            {
                _chart.SetSourceSegment(segment);
                _chart.SetReadout(string.Empty);
            }
        }

        /// <summary>A2：时间窗（10/20/60 s）跟随页面选择；只改绘制窗口，不改采样与记录。</summary>
        private void ApplyTimeWindow(bool force)
        {
            float seconds = TwinUiSourceSwitch.TimeWindowSeconds;
            if (!force && Mathf.Abs(seconds - _windowSeconds) < 0.01f)
            {
                return;
            }

            _windowSeconds = seconds;
            // R2 T6：按时间戳裁剪（旧 points = seconds / 0.25f 的固定点数已删，理由见 Joints 同名注释）。
            if (_chart != null)
            {
                _chart.SetPointWindow(0);
                _chart.SetTimeWindowSeconds(seconds);
                _chart.SetWindowLabel(seconds.ToString("F0") + " s 窗");
                _chart.SetTimeAxis(seconds);
            }
        }

        // ---- R2 T5/T6：可机读的采样/绘制计数（供 UI/61 断言） ----
        private int _chartPushCount;

        /// <summary>本卡向曲线缓冲压入样本的次数（跟随显示刷新率，不是帧数）。</summary>
        public int ChartPushCount => _chartPushCount;

        /// <summary>当前采样间隔（秒）= 1 / 显示刷新率。</summary>
        public float SampleIntervalSeconds => Mathf.Max(MinSampleInterval, 1f / Mathf.Max(1f, TwinUiSourceSwitch.DisplayHz));

        /// <summary>第一通道缓冲里的有效点数（裁剪后）。</summary>
        public int BufferPointCount => _buffers[0] != null ? _buffers[0].Count : 0;

        /// <summary>第一通道缓冲的首末时间戳跨度（秒）——T6 的核心断言量。</summary>
        public float BufferSpanSeconds => _buffers[0] == null || _buffers[0].Count < 2
            ? 0f
            : _buffers[0].NewestTime - _buffers[0].OldestTime;

        /// <summary>第一通道缓冲里最旧样本的时间戳（Time.unscaledTime 口径；空=0）。</summary>
        public float BufferOldestTime => _buffers[0] != null ? _buffers[0].OldestTime : 0f;

        /// <summary>第一通道缓冲里最新样本的时间戳（Time.unscaledTime 口径；空=0）。</summary>
        public float BufferNewestTime => _buffers[0] != null ? _buffers[0].NewestTime : 0f;

        /// <summary>曲线控件当前按时间戳裁剪后的实际跨度（秒）。</summary>
        public double ChartDrawnSpanSeconds => _chart != null ? _chart.DrawnSpanSeconds : 0d;

        /// <summary>曲线控件当前实际绘制点数（第一通道）。</summary>
        public int ChartDrawnPoints => _chart != null ? _chart.DrawnPoints(0) : 0;

        private void BuildControls(Transform parent)
        {
            _freezeButton = TwinUIStyle.Button(parent, "Freeze", "冻结", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(FreezeButtonWidth, ControlRowHeight - 2f), ToggleFreeze);
            if (_freezeButton.Label != null)
            {
                _freezeButton.Label.fontSize = TwinUIStyle.FontUnit;
            }

            _channelButtons = new TwinUIButton[JointCount];
            for (int i = 0; i < JointCount; i++)
            {
                int index = i;
                var b = TwinUIStyle.Button(parent, "Ch" + (i + 1), "J" + (i + 1), TwinUIButton.Kind.Plain,
                    Vector2.zero, new Vector2(ChannelButtonWidth, ControlRowHeight - 2f), () => ToggleChannel(index));
                if (b.Label != null)
                {
                    b.Label.fontSize = TwinUIStyle.FontUnit;
                }

                b.SetSelected(true);
                _channelButtons[i] = b;
            }
        }

        private void ToggleFreeze()
        {
            TwinUiViewFreeze.Toggle();
            ApplyFreezeVisual();
        }

        private void ToggleChannel(int index)
        {
            if (_chart == null)
            {
                return;
            }

            bool visible = !_chart.IsSeriesVisible(index);
            _chart.SetSeriesVisible(index, visible);
            if (_channelButtons != null && index >= 0 && index < _channelButtons.Length
                && _channelButtons[index] != null)
            {
                _channelButtons[index].SetSelected(visible);
            }
        }

        /// <summary>冻结/解冻的按钮与图例文案（不碰任何数据路径）。</summary>
        private void ApplyFreezeVisual()
        {
            bool frozen = TwinUiViewFreeze.IsFrozen;
            _frozenShown = frozen;
            if (_freezeButton != null)
            {
                if (_freezeButton.Label != null)
                {
                    _freezeButton.Label.text = frozen ? "解冻" : "冻结";
                }

                _freezeButton.SetSelected(frozen);
            }

            if (_chart != null)
            {
                _chart.SetFrozen(frozen);
            }
        }

        private TwinUiLineChart BuildChart(Transform parent, string unit)
        {
            var go = new GameObject("Chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(TwinUiLineChart));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            TwinUIStyle.Row(rt, 0f, TwinUiLayoutContract.SampleChartTop, 336f, 96f);

            var chart = go.GetComponent<TwinUiLineChart>();
            chart.raycastTarget = false;
            chart.Configure(JointCount, true);
            chart.SetUnit(unit);
            chart.SetThresholds(float.NaN, float.NaN);
            for (int i = 0; i < JointCount; i++)
            {
                chart.SetSeriesLabel(i, "J" + (i + 1));
            }

            return chart;
        }

        private static TMP_Text BuildCell(Transform parent, int index, string label,
            out RectTransform cellRoot, out RectTransform dot, out RectTransform caption)
        {
            var cell = TwinUIStyle.Frame(parent, "Cell" + index);
            cellRoot = cell;

            var dotImage = TwinUIStyle.Dot(cell, "Dot", Vector2.zero, 6f, SeriesColor(index));
            dot = dotImage.rectTransform;

            var labelText = TwinUIStyle.Text(cell, "Label", label, TwinUIStyle.FontUnit, TwinUIStyle.MutedColor);
            caption = labelText.rectTransform;

            var value = TwinUIStyle.Mono(cell, "Value", TwinUITextR3.Placeholder, TwinUIStyle.FontBodySm,
                TwinUIStyle.TextColor, TextAlignmentOptions.Right);
            TwinUIStyle.Row(value.rectTransform, TwinUiLayoutContract.SampleCellValueOffset, 0f, 120f, 20f);
            return value;
        }

        private void SetCells(float[] torque)
        {
            for (int i = 0; i < JointCount; i++)
            {
                bool valid = torque != null && i < torque.Length;
                _cellValues[i].text = valid
                    ? torque[i].ToString("F2") + TwinUITextR3.UnitNm
                    : TwinUITextR3.Placeholder;
                _cellValues[i].color = valid ? TwinUIStyle.TextColor : TwinUIStyle.QuietColor;
            }
        }

        private void ClearChart()
        {
            for (int i = 0; i < JointCount; i++)
            {
                _chart.SetSeries(i, _empty);
            }
        }

        private void ResetBuffers()
        {
            for (int i = 0; i < JointCount; i++)
            {
                _buffers[i] = new TwinUiLineChartBuffer(BufferCapacity);
                if (_seriesValues[i] == null)
                {
                    _seriesValues[i] = new List<float>(BufferCapacity);
                }
                else
                {
                    _seriesValues[i].Clear();
                }

                if (_seriesTimes[i] == null)
                {
                    _seriesTimes[i] = new List<float>(BufferCapacity);
                }
                else
                {
                    _seriesTimes[i].Clear();
                }
            }
        }

        private static Color SeriesColor(int index)
        {
            Color[] colors = TwinUIStyle.SeriesColors;
            if (colors == null || colors.Length == 0)
            {
                return TwinUIStyle.MutedColor;
            }

            return colors[index % colors.Length];
        }
    }
}