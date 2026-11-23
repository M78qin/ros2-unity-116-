// TwinUiLineChart.cs —— 通用多序列折线图（P9.5）+ 环缓冲（同一文件两个类型）
//
// 定位：卡片内"力矩 / 同步 / 时延"等小面积曲线的统一实现。全部几何由 MaskableGraphic.OnPopulateMesh
//       自绘（背景 / 网格 / 阈值虚线 / 折线），不依赖第三方图库、不订阅任何数据源。
//
// 设计取舍（写给调用方）：
//   * 空态策略：本控件**自带空态**——当所有序列点数都 < 2 时，在绘图区居中显示
//     "无数据（等待数据源）"（11px Quiet 色）。因此卡片不需要再叠一层空态文案；
//     但若卡片想自己显示（例如带链接/按钮的空态），可把本控件的空态文本设为空字符串隐藏它。
//   * 坐标范围：autoRange=true 时按"当前所有序列"的 min/max 两端各留 8% 余量；
//     autoRange=false 时固定用 Configure 传入的 minY/maxY。阈值线只有落在可视范围内才画。
//   * 阈值线只画虚线（warn 琥珀 / crit 红），不做底色带、不做发光——小卡片里避免糊成一片。
//   * 图例：左上角一行富文本（每条线用其序列色写 label），末尾附单位与阈值数值（Quiet 色）；
//     没有任何 label/unit/threshold 时整行隐藏。
//   * 性能：顶点缓冲复用字段 `_points`，不在 OnPopulateMesh 里 new 大数组；SetSeries 仅在有变化时
//     SetVerticesDirty()。序列上限 6、每条上限 120 点（超出保留"最新的 120 点"）。
//   * 主题：所有颜色都从 TwinUIStyle 令牌取（PlotBg/Grid/Series 等），切主题后调 ApplyTheme() 即可，
//     不重建 GameObject。
//
// 硬约束：不 using UnityEditor、不订阅数据、不读写文件、不引用 DigitalTwin.Runtime。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 轻量多序列折线图（自绘，适配卡片宽度；
    /// 建议挂在 360 宽的内容区里，高度由调用方给，比如 96–140）。
    /// </summary>
    public sealed class TwinUiLineChart : MaskableGraphic
    {
        /// <summary>最多序列数（与 TwinUIStyle.SeriesColors 长度对齐）。</summary>
        public const int MaxSeries = 6;

        /// <summary>每条序列最多保留的点数（超出保留最新的）。R2 T6：20 Hz × 60 s 窗口需要 ≥1200 点。</summary>
        public const int MaxPointsPerSeries = 2048;

        /// <summary>折线宽度（px）。</summary>
        public const float LineWidth = 1.6f;

        /// <summary>R2 A-4：时间轴标签字号（11 → 12，并把标签框加宽到 96 防裁）。</summary>
        public const float AxisLabelFontSize = 12f;

        private const string EmptyTextDefault = "无数据（等待数据源）";
        private const float DashLength = 4f;
        private const float DashGap = 3f;
        private const float RangePaddingRatio = 0.08f;
        private const float LabelPadding = 6f;
        private const float PlotTopInset = 24f;
        private const float PlotBottomInset = 34f;

        /// <summary>
        /// fix1 B(4)：数值 Y 轴左侧预留宽度（px）。有数据时才占位，没数据不画轴、不留空带，
        /// 避免"没有数据却画一条 0..1 的假轴"。
        /// </summary>
        public const float AxisYGutter = 44f;

        private readonly List<float>[] _series = new List<float>[MaxSeries];
        // R2 T6：与 _series 平行的样本时间戳（秒，调用方给的相对时间；null = 未提供，退回按点数裁剪）。
        private readonly List<float>[] _seriesTimes = new List<float>[MaxSeries];
        private readonly string[] _labels = new string[MaxSeries];
        private readonly List<Vector3> _points = new List<Vector3>(MaxPointsPerSeries);

        // 本批（回放 UI）：通道开关 / 时间窗 / 冻结显示标记。都只影响"画什么"，不碰数据来源。
        private readonly bool[] _visible = new bool[MaxSeries];
        private bool _visibleInit;
        private int _pointWindow;
        private double _timeWindowSeconds;
        private string _windowLabel = string.Empty;
        private bool _frozen;

        // 本批（A2/A4）：时间坐标语义 + 数值读数 + 源分段号。
        // 横轴按"相对时间(s)，右端 = 现在"绘制；读数显示末点数值（不猜、不补 0）。
        private double _axisSeconds;
        private int _segment;
        private string _readoutText = string.Empty;
        private TMP_Text _axisT0;
        private TMP_Text _axisT1;
        private TMP_Text _readout;

        // fix1 B(4)：数值纵轴（max / mid / min + 单位）。只在有数据时显示；
        // 数值来自**当前窗口内实际绘制的点**算出的量程，缺数据一律隐藏（不虚造 0/1 轴）。
        private TMP_Text _axisYMax;
        private TMP_Text _axisYMid;
        private TMP_Text _axisYMin;
        private TMP_Text _axisUnit;
        private bool _yAxisOn;
        private float _lastRangeMin;
        private float _lastRangeMax;

        private int _seriesCount = 1;
        private bool _autoRange = true;
        private float _fixedMin;
        private float _fixedMax = 1f;
        private float _warnThreshold = float.NaN;
        private float _critThreshold = float.NaN;
        private string _unit = string.Empty;
        private string _emptyText = EmptyTextDefault;

        private TMP_Text _legend;
        private TMP_Text _empty;

        /// <summary>配置的序列数（1..MaxSeries）。</summary>
        public int SeriesCount => _seriesCount;

        /// <summary>本轮实际绘制出来的线数（点数 ≥ 2 的序列数），供自检/审计用。</summary>
        public int LastDrawSeries { get; private set; }

        /// <summary>当前自动量程是否启用。</summary>
        public bool AutoRange => _autoRange;

        /// <summary>空态文案（设为空字符串即隐藏控件自带空态）。</summary>
        public string EmptyText
        {
            get => _emptyText;
            set
            {
                _emptyText = value ?? string.Empty;
                RefreshOverlay();
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// 配置序列数与量程。seriesCount 会被夹到 1..MaxSeries；
        /// autoRange=true 时忽略 minY/maxY，按数据 min/max 自动留 8% 余量。
        /// </summary>
        public void Configure(int seriesCount, bool autoRange = true, float minY = 0f, float maxY = 1f)
        {
            _seriesCount = Mathf.Clamp(seriesCount, 1, MaxSeries);
            _autoRange = autoRange;
            _fixedMin = minY;
            _fixedMax = maxY > minY ? maxY : minY + 1f;

            // 图表不参与交互：默认不接收射线，避免挡住卡片上的按钮/拖拽。
            raycastTarget = false;

            for (int i = 0; i < MaxSeries; i++)
            {
                if (_series[i] == null)
                {
                    _series[i] = new List<float>(MaxPointsPerSeries);
                }
                else if (i >= _seriesCount)
                {
                    _series[i].Clear();
                }

                if (_seriesTimes[i] == null)
                {
                    _seriesTimes[i] = new List<float>(MaxPointsPerSeries);
                }
                else if (i >= _seriesCount)
                {
                    _seriesTimes[i].Clear();
                }

                if (!_visibleInit)
                {
                    _visible[i] = true;
                }
            }

            _visibleInit = true;

            EnsureOverlay();
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>写入一条序列的数据（拷贝进内部缓冲，调用方可以复用同一个 List）。</summary>
        public void SetSeries(int index, IReadOnlyList<float> values)
        {
            SetSeries(index, values, null);
        }

        /// <summary>
        /// 写入一条序列的 (值, 时间戳)。时间戳用于 R2 T6 的**按时间戳裁剪**：
        /// 设置了 <see cref="SetTimeWindowSeconds"/> 后，只画"最新时间 − 窗口 ≤ t"的点，
        /// 因此同一窗口在高刷新率下点数更多、时间跨度不变（不是固定点数）。
        /// times 为 null 或长度不一致时退回按点数裁剪（兼容旧调用方）。
        /// </summary>
        public void SetSeries(int index, IReadOnlyList<float> values, IReadOnlyList<float> times)
        {
            if (index < 0 || index >= MaxSeries)
            {
                return;
            }

            if (_series[index] == null)
            {
                _series[index] = new List<float>(MaxPointsPerSeries);
            }

            var target = _series[index];
            int srcCount = values == null ? 0 : values.Count;
            int keep = Mathf.Min(srcCount, MaxPointsPerSeries);
            int start = srcCount - keep;

            bool changed = target.Count != keep;
            if (!changed)
            {
                for (int i = 0; i < keep; i++)
                {
                    if (!Mathf.Approximately(target[i], values[start + i]))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            target.Clear();
            for (int i = 0; i < keep; i++)
            {
                target.Add(values[start + i]);
            }

            var timeTarget = _seriesTimes[index];
            if (timeTarget == null)
            {
                timeTarget = new List<float>(MaxPointsPerSeries);
                _seriesTimes[index] = timeTarget;
            }

            timeTarget.Clear();
            bool hasTimes = times != null && times.Count == srcCount;
            for (int i = 0; i < keep; i++)
            {
                timeTarget.Add(hasTimes ? times[start + i] : i);
            }

            if (!hasTimes)
            {
                // 没有时间戳的调用方：清空并列时间，draw 时退回按点数裁剪。
                timeTarget.Clear();
            }

            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>设置序列名（用于左上角图例；传空字符串即不显示该条）。</summary>
        public void SetSeriesLabel(int index, string label)
        {
            if (index < 0 || index >= MaxSeries)
            {
                return;
            }

            string value = label ?? string.Empty;
            if (_labels[index] == value)
            {
                return;
            }

            _labels[index] = value;
            RefreshOverlay();
        }

        /// <summary>设置单位（显示在图例末尾，如 " N·m"）。</summary>
        public void SetUnit(string unit)
        {
            string value = unit ?? string.Empty;
            if (_unit == value)
            {
                return;
            }

            _unit = value;
            RefreshOverlay();
        }

        /// <summary>设置告警阈值：warn 黄虚线、crit 红虚线；float.NaN 表示不画该条。</summary>
        public void SetThresholds(float warn, float crit)
        {
            bool sameWarn = (float.IsNaN(_warnThreshold) && float.IsNaN(warn))
                || Mathf.Approximately(_warnThreshold, warn);
            bool sameCrit = (float.IsNaN(_critThreshold) && float.IsNaN(crit))
                || Mathf.Approximately(_critThreshold, crit);
            if (sameWarn && sameCrit)
            {
                return;
            }

            _warnThreshold = warn;
            _critThreshold = crit;
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>按当前主题重取颜色（背景/网格/折线/图例/空态），供主题切换调用。</summary>
        public void ApplyTheme()
        {
            color = Color.white;
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>读取某条序列的点数（越界返回 0）。</summary>
        public int PointCount(int index)
        {
            if (index < 0 || index >= MaxSeries || _series[index] == null)
            {
                return 0;
            }

            return _series[index].Count;
        }

        // ---- 通道开关 / 时间窗 / 冻结显示（本批新增：只改绘制，不改数据来源） ----

        /// <summary>某条通道当前是否显示（默认全部显示）。</summary>
        public bool IsSeriesVisible(int index)
        {
            return index >= 0 && index < MaxSeries && _visible[index];
        }

        /// <summary>当前可见通道数。</summary>
        public int VisibleSeriesCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _seriesCount && i < MaxSeries; i++)
                {
                    if (_visible[i])
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>打开/关闭一条通道（只影响绘制与图例；数据仍按调用方节奏写入）。</summary>
        public void SetSeriesVisible(int index, bool visible)
        {
            if (index < 0 || index >= MaxSeries || _visible[index] == visible)
            {
                return;
            }

            _visible[index] = visible;
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>时间窗（按点数）：只画每条序列最新的 N 点；0 = 全部（上限 MaxPointsPerSeries）。</summary>
        public int WindowPoints => _pointWindow;

        public void SetPointWindow(int points)
        {
            int value = Mathf.Clamp(points, 0, MaxPointsPerSeries);
            if (_pointWindow == value)
            {
                return;
            }

            _pointWindow = value;
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>图例里的时间窗说明（如 "20 s 窗"）；空 = 不显示。</summary>
        public void SetWindowLabel(string label)
        {
            string value = label ?? string.Empty;
            if (_windowLabel == value)
            {
                return;
            }

            _windowLabel = value;
            RefreshOverlay();
        }

        /// <summary>冻结显示标记：调用方停止 SetSeries 时置 true，图例追加"冻结显示"。</summary>
        public bool Frozen => _frozen;

        // ---- R2 T6：按时间戳裁剪（不是固定点数） ----

        /// <summary>按时间戳裁剪的窗口（秒）；0 = 不使用（退回 <see cref="SetPointWindow"/> 的点数裁剪）。</summary>
        public double TimeWindowSeconds => _timeWindowSeconds;

        /// <summary>
        /// 设置按时间戳裁剪的时间窗（秒）。0 关闭。开启后 draw 只取"最新时间 − 窗口"之后的样本，
        /// 因此 5 Hz 与 20 Hz 在同一窗口下**时间跨度相同、点数不同**（固定点数会随 Hz 改变真实跨度）。
        /// </summary>
        public void SetTimeWindowSeconds(double seconds)
        {
            double value = seconds <= 0.5d ? 0d : seconds;
            if (Mathf.Abs((float)(_timeWindowSeconds - value)) < 0.01f)
            {
                return;
            }

            _timeWindowSeconds = value;
            RefreshOverlay();
            SetVerticesDirty();
        }

        /// <summary>某条序列当前**实际绘制**的点数（已按时间窗/点数窗裁剪）。</summary>
        public int DrawnPoints(int index)
        {
            if (index < 0 || index >= MaxSeries || _series[index] == null)
            {
                return 0;
            }

            int count = _series[index].Count;
            return count - ComputeDrawStart(index, count);
        }

        /// <summary>
        /// 第一条 ≥2 绘制点的可见序列的实际时间跨度（秒）；没有时间戳/不足 2 点返回 0。
        /// 供 UI/61 T6 断言"同窗口不同 Hz 下跨度相同"。
        /// </summary>
        public double DrawnSpanSeconds
        {
            get
            {
                for (int s = 0; s < _seriesCount && s < MaxSeries; s++)
                {
                    if (!_visible[s] || _series[s] == null || _seriesTimes[s] == null)
                    {
                        continue;
                    }

                    int count = _series[s].Count;
                    int start = ComputeDrawStart(s, count);
                    if (count - start < 2 || _seriesTimes[s].Count != count)
                    {
                        continue;
                    }

                    return _seriesTimes[s][count - 1] - _seriesTimes[s][start];
                }

                return 0d;
            }
        }

        /// <summary>某条序列当前绘制区间的最旧/最新时间戳（秒）；无时间戳返回 false。</summary>
        public bool TryGetDrawnTimeRange(int index, out float oldest, out float newest)
        {
            oldest = 0f;
            newest = 0f;
            if (index < 0 || index >= MaxSeries || _series[index] == null || _seriesTimes[index] == null)
            {
                return false;
            }

            int count = _series[index].Count;
            int start = ComputeDrawStart(index, count);
            if (_seriesTimes[index].Count != count || count - start < 1)
            {
                return false;
            }

            oldest = _seriesTimes[index][start];
            newest = _seriesTimes[index][count - 1];
            return true;
        }

        /// <summary>算绘制起点：优先按时间戳（二分），否则按点数窗。</summary>
        private int ComputeDrawStart(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var times = _seriesTimes[index];
            if (_timeWindowSeconds > 0.5d && times != null && times.Count == count)
            {
                float newest = times[count - 1];
                float minTime = newest - (float)_timeWindowSeconds;
                int lo = 0;
                int hi = count - 1;
                int found = count;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (times[mid] >= minTime)
                    {
                        found = mid;
                        hi = mid - 1;
                    }
                    else
                    {
                        lo = mid + 1;
                    }
                }

                return found;
            }

            if (_pointWindow > 0 && count > _pointWindow)
            {
                return count - _pointWindow;
            }

            return 0;
        }

        public void SetFrozen(bool frozen)
        {
            if (_frozen == frozen)
            {
                return;
            }

            _frozen = frozen;
            RefreshOverlay();
        }

        // ---- 时间坐标 / 数值读数 / 源分段（A2/A4：只改绘制与文字） ----

        /// <summary>横轴语义说明（供自检/审计引用）。</summary>
        public string TimeAxisSemantics => "横轴=相对时间(s)，右端=现在";

        /// <summary>当前时间窗（秒）；0 = 未设置（不画时间轴标签）。</summary>
        public double AxisSeconds => _axisSeconds;

        public string AxisLeftLabel => _axisT0 != null ? _axisT0.text : string.Empty;

        public string AxisRightLabel => _axisT1 != null ? _axisT1.text : string.Empty;

        /// <summary>时间轴标签当前颜色（A-4 对比度实测用）。</summary>
        public Color AxisLabelColor => _axisT0 != null ? _axisT0.color : Color.clear;

        /// <summary>时间轴标签当前字号（pts）。</summary>
        public float AxisLabelFontSizeValue => _axisT0 != null ? _axisT0.fontSize : 0f;

        /// <summary>时间轴标签左侧字形覆盖率：'−'(U+2212) 与 '-' (U+002D) 是否在图集里（A-4 证据）。</summary>
        public bool AxisFontHasMinusSign => FontHasChar('\u2212');

        public bool AxisFontHasAsciiHyphen => FontHasChar('\u002D');

        // ---- fix1 B(4)：数值纵轴（自检/审计可读） ----

        /// <summary>数值纵轴当前是否显示（有数据才有）。</summary>
        public bool YAxisVisible => _yAxisOn;

        /// <summary>纵轴单位原文（例如 "N·m" / "deg"；空 = 未设置）。</summary>
        public string YAxisUnitText => _axisUnit != null ? _axisUnit.text : string.Empty;

        /// <summary>纵轴顶端刻度文本（缺数据时为空）。</summary>
        public string YAxisMaxText => _yAxisOn && _axisYMax != null ? _axisYMax.text : string.Empty;

        /// <summary>纵轴中间刻度文本（缺数据时为空）。</summary>
        public string YAxisMidText => _yAxisOn && _axisYMid != null ? _axisYMid.text : string.Empty;

        /// <summary>纵轴底端刻度文本（缺数据时为空）。</summary>
        public string YAxisMinText => _yAxisOn && _axisYMin != null ? _axisYMin.text : string.Empty;

        /// <summary>最近一次绘制采用的量程下界（与纵轴底端刻度同源）。</summary>
        public float YAxisRangeMin => _lastRangeMin;

        /// <summary>最近一次绘制采用的量程上界（与纵轴顶端刻度同源）。</summary>
        public float YAxisRangeMax => _lastRangeMax;

        private bool FontHasChar(char c)
        {
            TMP_FontAsset font = _axisT0 != null ? _axisT0.font : TwinUIStyle.GetFont();
            return font != null && font.HasCharacter(c);
        }

        /// <summary>图背景色（A-4 对比度实测用：标签画在 PlotBg 0.9 的图表背景上）。</summary>
        public Color PlotBackdropColor => TwinUIStyle.Alpha(TwinUIStyle.PlotBgColor, 0.9f);

        /// <summary>末点数值读数（空 = 未设置）。</summary>
        public string ReadoutText => _readoutText;

        /// <summary>源分段号（源切换时由调用方递增；仅用于标注）。</summary>
        public int SourceSegment => _segment;

        /// <summary>某序列末点数值；越界/空返回 float.NaN（调用方自行决定显示占位）。</summary>
        public float LatestValue(int index)
        {
            if (index < 0 || index >= MaxSeries || _series[index] == null || _series[index].Count == 0)
            {
                return float.NaN;
            }

            return _series[index][_series[index].Count - 1];
        }

        /// <summary>设置横轴时间窗（秒）：左端标签 "−N s"，右端 "现在"。</summary>
        public void SetTimeAxis(double windowSeconds)
        {
            if (Mathf.Abs((float)(_axisSeconds - windowSeconds)) < 0.01f)
            {
                return;
            }

            _axisSeconds = windowSeconds;
            RefreshAxis();
        }

        /// <summary>设置末点数值读数（例如 "末点 J1 12.3°"）。空字符串隐藏。</summary>
        public void SetReadout(string text)
        {
            string value = text ?? string.Empty;
            if (_readoutText == value)
            {
                return;
            }

            _readoutText = value;
            RefreshAxis();
        }

        /// <summary>源切换分段号：切换后曲线从空开始，并标注分段号（禁止跨源伪连续）。</summary>
        public void SetSourceSegment(int segment)
        {
            if (_segment == segment)
            {
                return;
            }

            _segment = segment;
            RefreshOverlay();
        }

        // ---- 覆盖层（图例 / 空态）：只在 Build 后一次性创建，后续只改文本与颜色 ----

        private void EnsureOverlay()
        {
            if (_legend == null)
            {
                _legend = TwinUIStyle.Text(transform, "Legend", string.Empty, TwinUIStyle.FontUnit,
                    TwinUIStyle.QuietColor);
                _legend.enableWordWrapping = false;
                _legend.richText = true;
                var lrt = _legend.rectTransform;
                lrt.anchorMin = new Vector2(0f, 1f);
                lrt.anchorMax = new Vector2(1f, 1f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -LabelPadding + 2f);
                lrt.sizeDelta = new Vector2(-LabelPadding * 2f, 14f);
                _legend.alignment = TextAlignmentOptions.TopLeft;
            }

            if (_empty == null)
            {
                _empty = TwinUIStyle.Text(transform, "Empty", EmptyTextDefault, TwinUIStyle.FontUnit,
                    TwinUIStyle.QuietColor, TextAlignmentOptions.Center);
                _empty.enableWordWrapping = false;
                var ert = _empty.rectTransform;
                ert.anchorMin = new Vector2(0.5f, 0.5f);
                ert.anchorMax = new Vector2(0.5f, 0.5f);
                ert.pivot = new Vector2(0.5f, 0.5f);
                ert.sizeDelta = new Vector2(200f, 16f);
                ert.anchoredPosition = Vector2.zero;
            }

            // 时间坐标语义（A2）：左下 "-N s"，右下 "0 s"；末点数值读数在右下第二行。
            // R2 A-4：旧实现写 "−"（U+2212 MINUS SIGN），默认字体 NotoSansSC-UI SDF 是 Static 图集
            //（1368 字形，无 U+2212），TMP 渲染成缺字形方框 → 屏幕上就是 "□20 s"。
            // 修法：改用 ASCII '-'（U+002D，图集内确实有），颜色由 Quiet 提到 Muted（对比度 ~7:1），
            // 字号 11 → 12；标签框加宽到 96 保证 "-60 s" 不被裁。
            if (_axisT0 == null)
            {
                _axisT0 = TwinUIStyle.Text(transform, "AxisT0", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.BottomLeft);
                TwinUIStyle.Place(_axisT0.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(4f, 2f), new Vector2(96f, 14f));
            }

            if (_axisT1 == null)
            {
                _axisT1 = TwinUIStyle.Text(transform, "AxisT1", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.BottomRight);
                TwinUIStyle.Place(_axisT1.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-4f, 2f), new Vector2(96f, 14f));
            }

            if (_readout == null)
            {
                _readout = TwinUIStyle.Text(transform, "Readout", string.Empty, TwinUIStyle.FontUnit,
                    TwinUIStyle.TextColor, TextAlignmentOptions.BottomRight);
                _readout.richText = false;
                TwinUIStyle.Place(_readout.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-4f, 17f), new Vector2(160f, 12f));
            }

            // fix1 B(4)：数值纵轴三档刻度 + 单位。默认隐藏，只有"真的有数据"时由 RefreshYAxis 打开，
            // 因此缺数据时不会留下一条 0..1 的假纵轴。
            float yLabelW = AxisYGutter - 4f;
            if (_axisUnit == null)
            {
                _axisUnit = TwinUIStyle.Text(transform, "AxisYUnit", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.TopLeft);
                _axisUnit.enableWordWrapping = false;
                TwinUIStyle.Place(_axisUnit.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(2f, -16f), new Vector2(yLabelW, 12f));
            }

            if (_axisYMax == null)
            {
                _axisYMax = TwinUIStyle.Text(transform, "AxisYMax", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.TopLeft);
                _axisYMax.enableWordWrapping = false;
                TwinUIStyle.Place(_axisYMax.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(2f, -30f), new Vector2(yLabelW, 12f));
            }

            if (_axisYMid == null)
            {
                _axisYMid = TwinUIStyle.Text(transform, "AxisYMid", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
                _axisYMid.enableWordWrapping = false;
                TwinUIStyle.Place(_axisYMid.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(2f, 0f), new Vector2(yLabelW, 12f));
            }

            if (_axisYMin == null)
            {
                _axisYMin = TwinUIStyle.Text(transform, "AxisYMin", string.Empty, AxisLabelFontSize,
                    TwinUIStyle.MutedColor, TextAlignmentOptions.BottomLeft);
                _axisYMin.enableWordWrapping = false;
                TwinUIStyle.Place(_axisYMin.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(2f, 2f), new Vector2(yLabelW, 12f));
            }

            ApplyOverlayInsets();
            RefreshAxis();
        }

        /// <summary>时间轴标签 / 末点读数的文字与显隐（不改数据、不触发重排）。</summary>
        private void RefreshAxis()
        {
            if (_axisT0 == null || _axisT1 == null || _readout == null)
            {
                return;
            }

            bool hasAxis = _axisSeconds > 0.5d;
            _axisT0.gameObject.SetActive(hasAxis);
            _axisT1.gameObject.SetActive(hasAxis);
            if (hasAxis)
            {
                // R2 A-4：ASCII '-'（U+002D）+ 右端 "0 s"（旧的两个字符都是 U+2212/中文，前者缺字形）。
                _axisT0.text = "-" + _axisSeconds.ToString("F0") + " s";
                _axisT1.text = "0 s";
            }

            bool hasReadout = !string.IsNullOrEmpty(_readoutText);
            _readout.gameObject.SetActive(hasReadout);
            if (hasReadout)
            {
                _readout.text = _readoutText;
            }
        }

        /// <summary>
        /// fix1 B(4)：Y 轴占用左侧 gutter 时，把图例左内边距与横轴左标签一起右移，
        /// 保证"纵轴数字"与"图例/时间标签"不重叠（这是布局层面的不重叠，不靠肉眼）。
        /// </summary>
        private void ApplyOverlayInsets()
        {
            float inset = _yAxisOn ? AxisYGutter + 4f : LabelPadding;
            if (_legend != null)
            {
                var lrt = _legend.rectTransform;
                lrt.offsetMin = new Vector2(inset, lrt.offsetMin.y);
            }

            if (_axisT0 != null)
            {
                _axisT0.rectTransform.anchoredPosition = new Vector2(_yAxisOn ? AxisYGutter + 4f : 4f, 2f);
            }
            // Keep labels outside the data rectangle; extremal samples must not draw through them.
            if (_axisUnit != null) _axisUnit.rectTransform.anchoredPosition = new Vector2(2f, -2f);
            if (_axisYMax != null) _axisYMax.rectTransform.anchoredPosition = new Vector2(2f, -PlotTopInset);
            if (_axisYMin != null) _axisYMin.rectTransform.anchoredPosition = new Vector2(2f, PlotBottomInset);
            if (_axisYMid != null) _axisYMid.rectTransform.anchoredPosition = new Vector2(2f, (PlotBottomInset - PlotTopInset) * 0.5f);
        }

        /// <summary>
        /// fix1 B(4)：刷新纵轴三档刻度。值域来自**当前窗口内实际绘制的样本**（与折线同一 min/max），
        /// 没有数据 → 三个刻度与单位全部隐藏，绝不补 0 或退化成固定 0..1。
        /// </summary>
        private void RefreshYAxis(float min, float max)
        {
            if (_axisYMax == null || _axisYMid == null || _axisYMin == null)
            {
                return;
            }

            bool on = _yAxisOn && max > min;
            _axisYMax.gameObject.SetActive(on);
            _axisYMid.gameObject.SetActive(on);
            _axisYMin.gameObject.SetActive(on);

            string unit = (_unit ?? string.Empty).Trim();
            if (_axisUnit != null)
            {
                _axisUnit.gameObject.SetActive(on && unit.Length > 0);
                if (on)
                {
                    SetAxisText(_axisUnit, unit);
                }
            }

            if (!on)
            {
                return;
            }

            SetAxisText(_axisYMax, FormatAxisValue(max));
            SetAxisText(_axisYMid, FormatAxisValue((min + max) * 0.5f));
            SetAxisText(_axisYMin, FormatAxisValue(min));
        }

        private static void SetAxisText(TMP_Text text, string value)
        {
            if (text != null && text.text != value)
            {
                text.text = value;
            }
        }

        /// <summary>纵轴刻度数字格式：按量级选小数位，避免 "0.000" 这种看不出差别的刻度。</summary>
        public static string FormatAxisValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "--";
            }

            float a = Mathf.Abs(value);
            if (a >= 1000f) return value.ToString("F0");
            if (a >= 100f) return value.ToString("F1");
            if (a >= 10f) return value.ToString("F2");
            if (a >= 1f) return value.ToString("F3");
            if (a >= 0.01f) return value.ToString("F4");
            return value.ToString("F5");
        }

        private void RefreshOverlay()
        {
            EnsureOverlay();
            if (_legend == null)
            {
                return;
            }

            var sb = new System.Text.StringBuilder(96);
            for (int i = 0; i < _seriesCount && i < MaxSeries; i++)
            {
                string label = _labels[i];
                if (string.IsNullOrEmpty(label) || _series[i] == null)
                {
                    continue;
                }

                if (!_visible[i])
                {
                    if (sb.Length > 0)
                    {
                        sb.Append("  ");
                    }

                    sb.Append("<color=#6E7884>×").Append(label).Append("</color>");
                    continue;
                }

                if (_series[i].Count < 2)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("  ");
                }

                var c = SeriesColor(i);
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>')
                    .Append(label).Append("</color>");
            }

            if (!string.IsNullOrEmpty(_unit))
            {
                if (sb.Length > 0)
                {
                    sb.Append("  · ");
                }

                sb.Append(_unit.Trim());
            }

            if (_pointWindow > 0 && !string.IsNullOrEmpty(_windowLabel))
            {
                if (sb.Length > 0)
                {
                    sb.Append("  · ");
                }

                sb.Append(_windowLabel);
            }

            if (!float.IsNaN(_warnThreshold) || !float.IsNaN(_critThreshold))
            {
                if (sb.Length > 0)
                {
                    sb.Append("  · ");
                }

                sb.Append("阈值 ");
                sb.Append(float.IsNaN(_warnThreshold) ? "—" : _warnThreshold.ToString("F1"));
                sb.Append('/');
                sb.Append(float.IsNaN(_critThreshold) ? "—" : _critThreshold.ToString("F1"));
            }

            if (_frozen)
            {
                if (sb.Length > 0)
                {
                    sb.Append("  · ");
                }

                sb.Append("冻结显示");
            }

            // A4：源切换后标注分段号，说明"这条线只属于当前源"。
            if (_segment > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append("  · ");
                }

                sb.Append("分段 #").Append(_segment);
            }

            _legend.text = sb.ToString();
            _legend.color = TwinUIStyle.QuietColor;
            _legend.gameObject.SetActive(sb.Length > 0);

            if (_empty != null)
            {
                bool anyData = false;
                bool anyVisibleData = false;
                for (int i = 0; i < _seriesCount && i < MaxSeries; i++)
                {
                    if (_series[i] != null && _series[i].Count >= 2)
                    {
                        anyData = true;
                        if (_visible[i])
                        {
                            anyVisibleData = true;
                            break;
                        }
                    }
                }

                _empty.text = _emptyText;
                _empty.color = TwinUIStyle.QuietColor;
                // 通道被全部关闭时不谎报"无数据"；只有真的没有数据才显示空态。
                _empty.gameObject.SetActive(!anyVisibleData && !anyData && !string.IsNullOrEmpty(_emptyText));
                if (!anyVisibleData && anyData && !string.IsNullOrEmpty(_emptyText))
                {
                    _empty.text = "通道已全部关闭";
                    _empty.gameObject.SetActive(true);
                }
            }
        }

        private static Color SeriesColor(int index)
        {
            var colors = TwinUIStyle.SeriesColors;
            if (colors == null || colors.Length == 0)
            {
                return TwinUIStyle.AccentColor;
            }

            return colors[index % colors.Length];
        }

        // ---- 绘制 ----

        private void LateUpdate()
        {
            bool hasDrawable = HasDrawableData();
            if (_yAxisOn != hasDrawable)
            {
                _yAxisOn = hasDrawable;
                ApplyOverlayInsets();
                SetVerticesDirty();
            }
            float min, max;
            if (_autoRange) ComputeRange(out min, out max);
            else { min = _fixedMin; max = _fixedMax; }
            RefreshYAxis(min, max);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            LastDrawSeries = 0;

            Rect r = rectTransform.rect;
            if (r.width <= 1f || r.height <= 1f)
            {
                return;
            }

            // fix1 B(4)：只有"真的有可绘制数据"才画数值纵轴并让出左侧 gutter；
            // 没有数据时绘图区仍是整块 rect（不虚造 0..1 轴、不留空带）。
            bool hasDrawable = HasDrawableData();
            // Text/layout changes are deferred to LateUpdate, outside Canvas rebuild.

            Rect plot = r;
            if (_yAxisOn)
            {
                float gutter = Mathf.Min(AxisYGutter, r.width * 0.35f);
                plot = new Rect(r.xMin + gutter, r.yMin, Mathf.Max(1f, r.width - gutter), r.height);
            }
            plot = new Rect(plot.xMin, plot.yMin + PlotBottomInset, plot.width,
                Mathf.Max(1f, plot.height - PlotTopInset - PlotBottomInset));

            // 背景（PlotBg 0.9 铺满 rect；纵轴刻度画在同一底色上）
            AddQuad(vh, r, TwinUIStyle.Alpha(TwinUIStyle.PlotBgColor, 0.9f));

            // 量程
            float min;
            float max;
            if (_autoRange)
            {
                ComputeRange(out min, out max);
            }
            else
            {
                min = _fixedMin;
                max = _fixedMax;
            }

            _lastRangeMin = min;
            _lastRangeMax = max;
            // The mesh callback only emits geometry; LateUpdate owns text changes.

            // 低对比水平网格 3 条（0 / 50% / 100%）
            Color grid = TwinUIStyle.Alpha(TwinUIStyle.GridColor, 0.55f);
            for (int g = 0; g <= 2; g++)
            {
                float ty = Mathf.Clamp01(g / 2f);
                float y = Mathf.Lerp(plot.yMin, plot.yMax, ty);
                AddQuad(vh, new Rect(plot.xMin, y, plot.width, 1f), grid);
            }

            // 阈值虚线（1px）
            if (!float.IsNaN(_warnThreshold))
            {
                DrawThreshold(vh, plot, min, max, _warnThreshold, TwinUIStyle.WarnColor);
            }

            if (!float.IsNaN(_critThreshold))
            {
                DrawThreshold(vh, plot, min, max, _critThreshold, TwinUIStyle.CritColor);
            }

            // 折线
            float stepSpan = plot.width;
            for (int s = 0; s < _seriesCount && s < MaxSeries; s++)
            {
                if (!_visible[s])
                {
                    continue;
                }

                var values = _series[s];
                if (values == null || values.Count < 2)
                {
                    continue;
                }

                Color line = SeriesColor(s);
                _points.Clear();
                int count = values.Count;
                int start = ComputeDrawStart(s, count);
                int shown = count - start;
                for (int i = start; i < count; i++)
                {
                    float tx = shown <= 1 ? 0f : (float)(i - start) / (shown - 1);
                    float ty = Mathf.InverseLerp(min, max, Mathf.Clamp(values[i], min, max));
                    _points.Add(new Vector3(plot.xMin + tx * stepSpan, Mathf.Lerp(plot.yMin, plot.yMax, ty), 0f));
                }

                for (int i = 1; i < _points.Count; i++)
                {
                    AddLine(vh, _points[i - 1], _points[i], LineWidth, line);
                }

                LastDrawSeries++;
            }

            // 外框（1px，低对比）
            Color border = TwinUIStyle.Alpha(TwinUIStyle.BorderColor, 0.5f);
            AddQuad(vh, new Rect(plot.xMin, plot.yMax - 1f, plot.width, 1f), border);
            AddQuad(vh, new Rect(plot.xMin, plot.yMin, plot.width, 1f), border);
            AddQuad(vh, new Rect(plot.xMin, plot.yMin, 1f, plot.height), border);
            AddQuad(vh, new Rect(plot.xMax - 1f, plot.yMin, 1f, plot.height), border);
        }

        /// <summary>是否有至少一条可见序列具备可绘制的点（≥2）。纵轴显隐与空态文案都以它为准。</summary>
        private bool HasDrawableData()
        {
            for (int s = 0; s < _seriesCount && s < MaxSeries; s++)
            {
                if (!_visible[s])
                {
                    continue;
                }

                var values = _series[s];
                if (values != null && values.Count >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private void ComputeRange(out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            for (int s = 0; s < _seriesCount && s < MaxSeries; s++)
            {
                if (!_visible[s])
                {
                    continue;
                }

                var values = _series[s];
                if (values == null || values.Count == 0)
                {
                    continue;
                }

                // R2 T6：量程只看**窗口内**的点（窗口外的旧样本不得拉宽 Y 轴）。
                int rangeStart = ComputeDrawStart(s, values.Count);
                for (int i = rangeStart; i < values.Count; i++)
                {
                    float v = values[i];
                    if (float.IsNaN(v) || float.IsInfinity(v))
                    {
                        continue;
                    }

                    if (v < min)
                    {
                        min = v;
                    }

                    if (v > max)
                    {
                        max = v;
                    }
                }
            }

            if (float.IsInfinity(min) || float.IsInfinity(max))
            {
                min = 0f;
                max = _fixedMax > 0f ? _fixedMax : 1f;
                return;
            }

            if (Mathf.Approximately(min, max))
            {
                float pad = Mathf.Max(0.5f, Mathf.Abs(min) * 0.1f);
                min -= pad;
                max += pad;
                return;
            }

            float span = max - min;
            min -= span * RangePaddingRatio;
            max += span * RangePaddingRatio;
        }

        private static void DrawThreshold(VertexHelper vh, Rect r, float min, float max, float value, Color color)
        {
            if (value < min || value > max || Mathf.Approximately(max, min))
            {
                return;
            }

            float t = Mathf.Clamp01((value - min) / (max - min));
            float y = Mathf.Lerp(r.yMin, r.yMax, t);
            Color c = TwinUIStyle.Alpha(color, 0.9f);
            float x = r.xMin;
            while (x < r.xMax)
            {
                float dash = Mathf.Min(DashLength, r.xMax - x);
                AddQuad(vh, new Rect(x, y, dash, 1f), c);
                x += DashLength + DashGap;
            }
        }

        private static void AddQuad(VertexHelper vh, Rect rect, Color color)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void AddLine(VertexHelper vh, Vector3 a, Vector3 b, float width, Color color)
        {
            Vector2 dir = (b - a);
            if (dir.sqrMagnitude < 1e-8f)
            {
                return;
            }

            dir = dir.normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x - normal.x, a.y - normal.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(b.x - normal.x, b.y - normal.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(b.x + normal.x, b.y + normal.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(a.x + normal.x, a.y + normal.y, 0f), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    /// <summary>
    /// 定长环缓冲：给卡片保存"最近 N 个采样"。满后 Push 覆盖最旧值；
    /// CopyTo 按"从最旧到最新"的顺序输出，可直接交给 TwinUiLineChart.SetSeries。
    /// 空缓冲时 Last 返回 0f —— 调用方应先用 Count 判断是否真的有数据，不要把 0 当真实值显示。
    /// </summary>
    public sealed class TwinUiLineChartBuffer
    {
        private readonly float[] _data;
        private readonly float[] _times;
        private int _head;
        private int _count;

        public TwinUiLineChartBuffer(int capacity)
        {
            int cap = Mathf.Max(1, capacity);
            _data = new float[cap];
            _times = new float[cap];
        }

        /// <summary>容量（构造时给的值，最小 1）。</summary>
        public int Capacity => _data.Length;

        /// <summary>当前有效点数（≤ Capacity）。</summary>
        public int Count => _count;

        /// <summary>最旧样本时间戳（秒）；空缓冲返回 0。</summary>
        public float OldestTime => _count == 0 ? 0f : _times[_head];

        /// <summary>最新样本时间戳（秒）；空缓冲返回 0。</summary>
        public float NewestTime => _count == 0 ? 0f : _times[(_head + _count - 1) % _times.Length];

        /// <summary>第 i 个（最旧→最新）样本时间戳。</summary>
        public float TimeAt(int index)
        {
            return _times[(_head + index) % _times.Length];
        }

        /// <summary>丢掉早于 minTime 的样本（R2 T6：按时间戳裁剪，不是固定点数）。</summary>
        public void TrimBefore(float minTime)
        {
            while (_count > 0 && _times[_head] < minTime)
            {
                _head = (_head + 1) % _times.Length;
                _count--;
            }
        }

        /// <summary>最新一个值；空缓冲返回 0f（见类型注释）。</summary>
        public float Last
        {
            get
            {
                if (_count == 0)
                {
                    return 0f;
                }

                return _data[(_head + _count - 1) % _data.Length];
            }
        }

        /// <summary>压入一个采样；满则覆盖最旧。</summary>
        public void Push(float v)
        {
            Push(v, _count == 0 ? 0f : NewestTime);
        }

        /// <summary>压入一个带时间戳（秒）的采样；满则覆盖最旧。时间戳非单调时夹到最新时间。</summary>
        public void Push(float v, float time)
        {
            if (_count > 0 && time < NewestTime)
            {
                time = NewestTime;
            }

            if (_count < _data.Length)
            {
                int slot = (_head + _count) % _data.Length;
                _data[slot] = v;
                _times[slot] = time;
                _count++;
                return;
            }

            _data[_head] = v;
            _times[_head] = time;
            _head = (_head + 1) % _data.Length;
        }

        /// <summary>按最旧→最新的顺序拷出（dst 会先被清空）。</summary>
        public void CopyTo(List<float> dst)
        {
            if (dst == null)
            {
                return;
            }

            dst.Clear();
            for (int i = 0; i < _count; i++)
            {
                dst.Add(_data[(_head + i) % _data.Length]);
            }
        }

        /// <summary>按最旧→最新的顺序拷出时间戳（与 CopyTo 一一对应）。</summary>
        public void CopyTimesTo(List<float> dst)
        {
            if (dst == null)
            {
                return;
            }

            dst.Clear();
            for (int i = 0; i < _count; i++)
            {
                dst.Add(_times[(_head + i) % _times.Length]);
            }
        }

        /// <summary>清空（保留容量）。</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// 全局"查看冻结"开关（本批回放 UI）：暂停卡片的显示刷新，**不**停止数据源/回放钟/TwinRecorder。
    /// 放在本文件是为了不新增文件/不引入新的程序集边界；卡片轮询 IsFrozen 即可。
    /// </summary>
    public static class TwinUiViewFreeze
    {
        public static bool IsFrozen { get; private set; }

        /// <summary>状态变化回调（卡片可用来立刻重画按钮文案；也可轮询）。</summary>
        public static event System.Action<bool> Changed;

        public static void Set(bool frozen)
        {
            if (IsFrozen == frozen)
            {
                return;
            }

            IsFrozen = frozen;
            var handler = Changed;
            if (handler != null)
            {
                handler(frozen);
            }
        }

        public static bool Toggle()
        {
            Set(!IsFrozen);
            return IsFrozen;
        }

        public static void Reset()
        {
            Set(false);
        }
    }
}
