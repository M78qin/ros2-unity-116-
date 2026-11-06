// Minimal uGUI time-series chart (no third-party package in this project).
//
// Why a custom MaskableGraphic instead of a texture/camera: it costs one mesh rebuild per frame
// at most, it works in edit mode (so an editor screenshot is real evidence), and it needs no
// extra render pass or material - which matters because the UI panel must not distort the
// performance numbers the thesis reports.
//
// 三期新增：目标虚线 + 阈值色带（黄/红），供"每项显示当前/目标/误差 + 超限阈值"的论文级最小集使用。
// 色带只是把 settings 里的阈值画出来，不代表数据合格与否的结论。
//
// The chart draws geometry only. "no data source" text is owned by the view so that a missing
// force topic can never be mistaken for a flat zero line.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TwinTelemetryChart : MaskableGraphic
    {
        public enum BandMode
        {
            Upper = 0,
            Symmetric = 1
        }

        [Header("Window")]
        [SerializeField] private float windowSeconds = 20f;

        [Header("Range (autoRange=false uses these)")]
        [SerializeField] private bool autoRange = true;
        [SerializeField] private float fixedMin = -180f;
        [SerializeField] private float fixedMax = 180f;
        [SerializeField] private float rangePadding = 0.12f;

        [Header("Style")]
        [SerializeField] private float lineThickness = 2f;
        [SerializeField] private int capacity = 900;
        [SerializeField] private int horizontalGridLines = 4;
        [SerializeField] private int verticalGridLines = 5;
        [SerializeField] private Color plotBackgroundColor = new Color(0.05f, 0.09f, 0.13f, 1f);
        [SerializeField] private Color gridColor = new Color(0.13f, 0.19f, 0.25f, 1f);

        [Header("Target line")]
        [SerializeField] private bool showTargetLine;
        [SerializeField] private float targetValue;
        [SerializeField] private Color targetColor = new Color(0.83f, 0.90f, 0.96f, 0.85f);
        [SerializeField] private float targetDashOn = 6f;
        [SerializeField] private float targetDashOff = 5f;

        [Header("Threshold band")]
        [SerializeField] private bool showThresholdBand;
        [SerializeField] private BandMode bandMode = BandMode.Symmetric;
        [SerializeField] private float bandWarnLevel;
        [SerializeField] private float bandCritLevel;
        [SerializeField] private Color bandWarnColor = new Color(0.96f, 0.65f, 0.14f, 0.16f);
        [SerializeField] private Color bandCritColor = new Color(0.94f, 0.27f, 0.27f, 0.18f);

        [Header("Series")]
        [SerializeField] private TwinTelemetryChannel[] series =
        {
            TwinTelemetryChannel.Joint1, TwinTelemetryChannel.Joint2, TwinTelemetryChannel.Joint3,
        };

        private readonly Dictionary<TwinTelemetryChannel, TwinTelemetryRingBuffer> _buffers =
            new Dictionary<TwinTelemetryChannel, TwinTelemetryRingBuffer>();

        private bool _hasData;
        private float _newestTime;
        private float _rangeMin;
        private float _rangeMax;

        public float WindowSeconds => windowSeconds;
        public bool HasData => _hasData;
        public float RangeMin => _rangeMin;
        public float RangeMax => _rangeMax;
        public TwinTelemetryChannel[] Series => series;
        public bool ShowTargetLine => showTargetLine;

        // P2.5 主题自检读取用（UI/33 期望/实测比色）。
        public Color PlotBackgroundColor => plotBackgroundColor;
        public Color GridLineColor => gridColor;
        public float TargetValue => targetValue;
        public bool ShowThresholdBand => showThresholdBand;

        public void SetSeries(TwinTelemetryChannel[] channels)
        {
            series = channels ?? new TwinTelemetryChannel[0];
            RebuildBuffers();
            SetVerticesDirty();
        }

        public void SetWindowSeconds(float seconds)
        {
            windowSeconds = Mathf.Max(1f, seconds);
            SetVerticesDirty();
        }

        public void SetTargetLine(bool show, float value)
        {
            showTargetLine = show;
            targetValue = value;
            SetVerticesDirty();
        }

        public void SetThresholdBand(bool show, float warn, float crit, bool symmetric, float target)
        {
            showThresholdBand = show && warn > 0f && crit > warn;
            bandWarnLevel = warn;
            bandCritLevel = crit;
            bandMode = symmetric ? BandMode.Symmetric : BandMode.Upper;
            if (symmetric)
            {
                targetValue = target;
            }

            SetVerticesDirty();
        }

        /// <summary>主题切换时重刷图表配色（绘制参数来自主题令牌，而不是场景里冻死的颜色）。</summary>
        public void ApplyTheme()
        {
            var p = DigitalTwin.UI.TwinUITheme.Current;
            plotBackgroundColor = p.PlotBg;
            gridColor = p.Grid;
            targetColor = DigitalTwin.UI.TwinUIStyle.Alpha(p.Text, 0.55f);
            var warn = p.Warn;
            warn.a = 0.16f;
            bandWarnColor = warn;
            var crit = p.Crit;
            crit.a = 0.18f;
            bandCritColor = crit;
            SetVerticesDirty();
        }

        public void Push(TwinTelemetryChannel channel, float time, float value)
        {
            if (!_buffers.TryGetValue(channel, out TwinTelemetryRingBuffer buffer))
            {
                buffer = new TwinTelemetryRingBuffer(capacity);
                _buffers[channel] = buffer;
            }

            buffer.Push(time, value);
            if (time > _newestTime)
            {
                _newestTime = time;
            }

            _hasData = true;
            SetVerticesDirty();
        }

        public void SetHasData(bool value)
        {
            if (_hasData == value)
            {
                return;
            }

            _hasData = value;
            SetVerticesDirty();
        }

        public void ClearAll()
        {
            foreach (KeyValuePair<TwinTelemetryChannel, TwinTelemetryRingBuffer> pair in _buffers)
            {
                pair.Value.Clear();
            }

            _hasData = false;
            _newestTime = 0f;
            SetVerticesDirty();
        }

        private void RebuildBuffers()
        {
            foreach (TwinTelemetryChannel channel in series)
            {
                if (!_buffers.ContainsKey(channel))
                {
                    _buffers[channel] = new TwinTelemetryRingBuffer(capacity);
                }
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            RebuildBuffers();
            if (plotBackgroundColor.a < 0.99f)
            {
                plotBackgroundColor.a = 1f;
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 4f || rect.height <= 4f)
            {
                return;
            }

            Rect plot = new Rect(rect.xMin + 2f, rect.yMin + 2f, rect.width - 4f, rect.height - 4f);

            AddQuad(vh, plot, plotBackgroundColor);

            float tMin = _newestTime - windowSeconds;
            float rMin;
            float rMax;
            ResolveRange(tMin, out rMin, out rMax);

            if (showThresholdBand)
            {
                DrawThresholdBand(vh, plot, rMin, rMax);
            }

            int hLines = Mathf.Max(0, horizontalGridLines);
            for (int i = 0; i <= hLines; i++)
            {
                float y = Mathf.Lerp(plot.yMin, plot.yMax, hLines == 0 ? 0f : i / (float)hLines);
                AddQuad(vh, new Rect(plot.xMin, y - 0.5f, plot.width, 1f), gridColor);
            }

            int vLines = Mathf.Max(0, verticalGridLines);
            for (int i = 0; i <= vLines; i++)
            {
                float x = Mathf.Lerp(plot.xMin, plot.xMax, vLines == 0 ? 0f : i / (float)vLines);
                AddQuad(vh, new Rect(x - 0.5f, plot.yMin, 1f, plot.height), gridColor);
            }

            if (showTargetLine)
            {
                DrawDashedLine(vh, plot, rMin, rMax, targetValue, targetColor);
            }

            if (!_hasData || series == null || series.Length == 0)
            {
                return;
            }

            float halfThickness = Mathf.Max(0.5f, lineThickness * 0.5f);
            foreach (TwinTelemetryChannel channel in series)
            {
                if (!_buffers.TryGetValue(channel, out TwinTelemetryRingBuffer buffer) || buffer.IsEmpty)
                {
                    continue;
                }

                Color color = TwinTelemetryCatalog.Get(channel).Color;
                buffer.TrimBefore(tMin);

                int count = buffer.Count;
                if (count < 2)
                {
                    continue;
                }

                int step = Mathf.Max(1, Mathf.CeilToInt(count / 700f));
                float tMax = tMin + windowSeconds;
                Vector2 previous = Vector2.zero;
                bool hasPrevious = false;
                for (int i = 0; i < count; i += step)
                {
                    Vector2 point = MapPoint(buffer.TimeAt(i), buffer.ValueAt(i), tMin, tMax, rMin, rMax, plot);
                    if (hasPrevious)
                    {
                        AddSegment(vh, previous, point, halfThickness, color);
                    }

                    previous = point;
                    hasPrevious = true;
                }
            }
        }

        private float YForValue(Rect plot, float rMin, float rMax, float value)
        {
            float yNorm = Mathf.InverseLerp(rMin, Mathf.Max(rMin + 0.0001f, rMax), value);
            return Mathf.Lerp(plot.yMin, plot.yMax, yNorm);
        }

        private void DrawThresholdBand(VertexHelper vh, Rect plot, float rMin, float rMax)
        {
            if (bandMode == BandMode.Upper)
            {
                float warnY = YForValue(plot, rMin, rMax, bandWarnLevel);
                float critY = YForValue(plot, rMin, rMax, bandCritLevel);
                AddQuad(vh, new Rect(plot.xMin, warnY, plot.width, Mathf.Max(0f, critY - warnY)), bandWarnColor);
                AddQuad(vh, new Rect(plot.xMin, critY, plot.width, Mathf.Max(0f, plot.yMax - critY)), bandCritColor);
                return;
            }

            float upperWarn = YForValue(plot, rMin, rMax, targetValue + bandWarnLevel);
            float upperCrit = YForValue(plot, rMin, rMax, targetValue + bandCritLevel);
            float lowerWarn = YForValue(plot, rMin, rMax, targetValue - bandWarnLevel);
            float lowerCrit = YForValue(plot, rMin, rMax, targetValue - bandCritLevel);

            // 超限区（|err| > crit）
            AddQuad(vh, new Rect(plot.xMin, upperCrit, plot.width, Mathf.Max(0f, plot.yMax - upperCrit)), bandCritColor);
            AddQuad(vh, new Rect(plot.xMin, plot.yMin, plot.width, Mathf.Max(0f, lowerCrit - plot.yMin)), bandCritColor);
            // 预警区（warn < |err| <= crit）
            AddQuad(vh, new Rect(plot.xMin, upperWarn, plot.width, Mathf.Max(0f, upperCrit - upperWarn)), bandWarnColor);
            AddQuad(vh, new Rect(plot.xMin, lowerCrit, plot.width, Mathf.Max(0f, lowerWarn - lowerCrit)), bandWarnColor);
        }

        private void DrawDashedLine(VertexHelper vh, Rect plot, float rMin, float rMax, float value, Color color)
        {
            float y = YForValue(plot, rMin, rMax, value);
            if (y < plot.yMin || y > plot.yMax)
            {
                return;
            }

            float step = Mathf.Max(2f, targetDashOn + targetDashOff);
            float x = plot.xMin;
            while (x < plot.xMax)
            {
                float width = Mathf.Min(targetDashOn, plot.xMax - x);
                if (width > 0f)
                {
                    AddQuad(vh, new Rect(x, y - 1f, width, 2f), color);
                }

                x += step;
            }
        }

        private void ResolveRange(float tMin, out float min, out float max)
        {
            if (!autoRange)
            {
                min = fixedMin;
                max = fixedMax;
                _rangeMin = min;
                _rangeMax = max;
                return;
            }

            min = float.MaxValue;
            max = float.MinValue;
            bool any = false;
            foreach (KeyValuePair<TwinTelemetryChannel, TwinTelemetryRingBuffer> pair in _buffers)
            {
                if (series == null || System.Array.IndexOf(series, pair.Key) < 0)
                {
                    continue;
                }

                TwinTelemetryRingBuffer buffer = pair.Value;
                for (int i = 0; i < buffer.Count; i++)
                {
                    if (buffer.TimeAt(i) < tMin)
                    {
                        continue;
                    }

                    float v = buffer.ValueAt(i);
                    if (v < min) min = v;
                    if (v > max) max = v;
                    any = true;
                }
            }

            if (!any)
            {
                min = fixedMin;
                max = fixedMax;
            }
            else
            {
                // 阈值带/目标线也要落在可视范围内，否则色带会画在框外看不见。
                if (showTargetLine)
                {
                    min = Mathf.Min(min, targetValue);
                    max = Mathf.Max(max, targetValue);
                }

                if (showThresholdBand)
                {
                    float span = bandMode == BandMode.Symmetric
                        ? targetValue + bandCritLevel : bandCritLevel;
                    float low = bandMode == BandMode.Symmetric
                        ? targetValue - bandCritLevel : Mathf.Min(min, bandWarnLevel);
                    min = Mathf.Min(min, low);
                    max = Mathf.Max(max, span);
                }
            }

            if (Mathf.Approximately(min, max))
            {
                min -= 1f;
                max += 1f;
            }
            else
            {
                float pad = (max - min) * Mathf.Max(0f, rangePadding);
                min -= pad;
                max += pad;
            }

            _rangeMin = min;
            _rangeMax = max;
        }

        private static Vector2 MapPoint(float time, float value, float tMin, float tMax, float rMin, float rMax, Rect plot)
        {
            float xNorm = Mathf.InverseLerp(tMin, Mathf.Max(tMin + 0.0001f, tMax), time);
            float yNorm = Mathf.InverseLerp(rMin, Mathf.Max(rMin + 0.0001f, rMax), value);
            float x = Mathf.Lerp(plot.xMin, plot.xMax, xNorm);
            float y = Mathf.Lerp(plot.yMin, plot.yMax, yNorm);
            return new Vector2(
                Mathf.Clamp(x, plot.xMin + 0.5f, plot.xMax - 0.5f),
                Mathf.Clamp(y, plot.yMin + 0.5f, plot.yMax - 0.5f));
        }

        private static void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, float halfThickness, Color color)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector2 normal = new Vector2(-dir.y, dir.x).normalized * halfThickness;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            v.position = a - normal;
            vh.AddVert(v);
            v.position = a + normal;
            vh.AddVert(v);
            v.position = b + normal;
            vh.AddVert(v);
            v.position = b - normal;
            vh.AddVert(v);

            int baseIndex = vh.currentVertCount - 4;
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }

        private static void AddQuad(VertexHelper vh, Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            int baseIndex = vh.currentVertCount;
            v.position = new Vector2(rect.xMin, rect.yMin);
            vh.AddVert(v);
            v.position = new Vector2(rect.xMin, rect.yMax);
            vh.AddVert(v);
            v.position = new Vector2(rect.xMax, rect.yMax);
            vh.AddVert(v);
            v.position = new Vector2(rect.xMax, rect.yMin);
            vh.AddVert(v);

            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }
    }
}