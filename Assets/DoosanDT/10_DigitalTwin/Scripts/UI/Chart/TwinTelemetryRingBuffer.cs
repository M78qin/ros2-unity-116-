// Fixed-capacity ring buffer for one chart series, indexed oldest -> newest.
//
// Time-based trimming is deliberate: the UI polls at a variable frame rate, so a frame-count
// based window would silently change its duration whenever the editor drops frames. The chart
// therefore stores (time, value) pairs and trims by time.
using UnityEngine;

namespace DigitalTwin
{
    public sealed class TwinTelemetryRingBuffer
    {
        private readonly float[] _times;
        private readonly float[] _values;
        private int _start;
        private int _count;

        public TwinTelemetryRingBuffer(int capacity)
        {
            int cap = Mathf.Max(8, capacity);
            _times = new float[cap];
            _values = new float[cap];
        }

        public int Count => _count;
        public int Capacity => _times.Length;

        public bool IsEmpty => _count == 0;

        public float OldestTime => _count == 0 ? 0f : _times[_start];
        public float NewestTime => _count == 0 ? 0f : _times[Index(_count - 1)];

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public void Push(float time, float value)
        {
            if (_count > 0 && time < NewestTime)
            {
                // Out-of-order sample: keep monotonic time so Polyline() stays sane.
                time = NewestTime;
            }

            int slot;
            if (_count < _times.Length)
            {
                slot = Index(_count);
                _count++;
            }
            else
            {
                slot = _start;
                _start = (_start + 1) % _times.Length;
            }

            _times[slot] = time;
            _values[slot] = value;
        }

        public float TimeAt(int index)
        {
            return _times[Index(index)];
        }

        public float ValueAt(int index)
        {
            return _values[Index(index)];
        }

        /// <summary>Drop samples older than minTime (keeps the window sliding).</summary>
        public void TrimBefore(float minTime)
        {
            while (_count > 0 && _times[_start] < minTime)
            {
                _start = (_start + 1) % _times.Length;
                _count--;
            }
        }

        public bool TryGetRange(out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (_count == 0)
            {
                return false;
            }

            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < _count; i++)
            {
                float v = _values[Index(i)];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            return true;
        }

        private int Index(int logicalIndex)
        {
            return (_start + logicalIndex) % _times.Length;
        }
    }
}