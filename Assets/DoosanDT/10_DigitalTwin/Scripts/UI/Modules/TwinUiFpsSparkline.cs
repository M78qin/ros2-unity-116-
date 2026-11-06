// TwinUiFpsSparkline.cs —— 轻量 FPS 折线（全球面自绘，不依赖图库；P4 / task-12）
// 只用于性能模块的最近 60 s FPS 曲线；颜色读 Ocean 令牌（Ok/Warn/Crit + Grid/PlotBg）。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiFpsSparkline : MaskableGraphic
    {
        private readonly List<float> _values = new List<float>();
        private float _min;
        private float _max = 120f;

        public int PointCount => _values.Count;
        public int LastValueCount { get; private set; }

        public void SetValues(IReadOnlyList<float> values)
        {
            _values.Clear();
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    _values.Add(values[i]);
                }
            }

            LastValueCount = _values.Count;
            Recalculate();
        }

        private void Recalculate()
        {
            _min = 0f;
            _max = 120f;
            for (int i = 0; i < _values.Count; i++)
            {
                _max = Mathf.Max(_max, _values[i]);
            }

            _max = Mathf.Ceil(_max / 30f) * 30f;
            SetVerticesDirty();
        }

        public void ApplyTheme()
        {
            color = TwinUIStyle.Alpha(TwinUIStyle.AccentColor, 0.9f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;

            // 背景 + 水平网格（0 / 50% / 100%）
            AddQuad(vh, new Rect(r.x, r.y, r.width, r.height), TwinUIStyle.Alpha(TwinUIStyle.PlotBgColor, 0.9f));
            for (int g = 0; g <= 2; g++)
            {
                float t = g / 2f;
                float y = Mathf.Lerp(r.yMin, r.yMax, t);
                AddQuad(vh, new Rect(r.x, y, r.width, 1f), TwinUIStyle.Alpha(TwinUIStyle.GridColor, 0.7f));
            }

            if (_values.Count < 2)
            {
                return;
            }

            float step = r.width / Mathf.Max(1, _values.Count - 1);
            Color lineColor = TwinUIStyle.OkColor;
            float avg = 0f;
            for (int i = 0; i < _values.Count; i++)
            {
                avg += _values[i];
            }

            avg /= _values.Count;
            if (avg < 30f)
            {
                lineColor = TwinUIStyle.CritColor;
            }
            else if (avg < 55f)
            {
                lineColor = TwinUIStyle.WarnColor;
            }

            Vector2 prev = Vector2.zero;
            for (int i = 0; i < _values.Count; i++)
            {
                float t = Mathf.InverseLerp(_min, _max, Mathf.Clamp(_values[i], _min, _max));
                var point = new Vector2(r.xMin + i * step, Mathf.Lerp(r.yMin, r.yMax, t));
                if (i > 0)
                {
                    AddLine(vh, prev, point, 2f, lineColor);
                }

                prev = point;
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

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            int start = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero);
            vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero);
            vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}