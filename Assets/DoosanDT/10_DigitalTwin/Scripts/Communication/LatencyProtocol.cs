using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Small, side-effect-free contract helper for the paper-grade ROS2↔Unity latency probe.
    /// The t0/t1/t2 monotonic clocks belong to different processes and must not be subtracted
    /// across hosts; they are retained for traceability alongside the wall-clock timestamps.
    /// </summary>
    public static class LatencyProtocol
    {
        [Serializable]
        private sealed class PingJson
        {
            public string protocol = string.Empty;
            public string kind = string.Empty;
            public long seq;
            public long t0_wall_ms;
            public long t0_mono_ns;
            public string session_id = string.Empty;
        }

        public readonly struct Ping
        {
            public readonly long Sequence;
            public readonly long T0WallMs;
            public readonly long T0MonoNs;
            public readonly string SessionId;

            public Ping(long sequence, long t0WallMs, long t0MonoNs, string sessionId)
            {
                Sequence = sequence;
                T0WallMs = t0WallMs;
                T0MonoNs = t0MonoNs;
                SessionId = sessionId ?? string.Empty;
            }
        }

        public static bool TryParsePing(string json, out Ping ping, out string error)
        {
            ping = default;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "latency ping payload is empty.";
                return false;
            }

            try
            {
                PingJson parsed = JsonUtility.FromJson<PingJson>(json);
                if (parsed == null || parsed.protocol != "dt_latency_v1" || parsed.kind != "ping" ||
                    parsed.seq < 0 || parsed.t0_wall_ms <= 0 || parsed.t0_mono_ns < 0)
                {
                    error = "latency ping requires protocol=dt_latency_v1, kind=ping, seq>=0, t0_wall_ms>0, and t0_mono_ns>=0.";
                    return false;
                }

                ping = new Ping(parsed.seq, parsed.t0_wall_ms, parsed.t0_mono_ns, parsed.session_id);
                return true;
            }
            catch (Exception ex)
            {
                error = "latency ping JSON parse failed: " + ex.Message;
                return false;
            }
        }

        public static string BuildPongJson(Ping ping, long t1WallMs, long t1MonoNs, long t2WallMs, long t2MonoNs)
        {
            StringBuilder b = new StringBuilder(224);
            b.Append('{');
            AppendString(b, "protocol", "dt_latency_v1");
            AppendString(b, "kind", "pong");
            AppendNumber(b, "seq", ping.Sequence);
            AppendString(b, "session_id", ping.SessionId);
            AppendNumber(b, "t0_wall_ms", ping.T0WallMs);
            AppendNumber(b, "t0_mono_ns", ping.T0MonoNs);
            AppendNumber(b, "t1_wall_ms", t1WallMs);
            AppendNumber(b, "t1_mono_ns", t1MonoNs);
            AppendNumber(b, "t2_wall_ms", t2WallMs);
            AppendNumber(b, "t2_mono_ns", t2MonoNs, false);
            b.Append('}');
            return b.ToString();
        }

        private static void AppendNumber(StringBuilder b, string name, long value, bool comma = true)
        {
            b.Append('"').Append(name).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));
            if (comma) b.Append(',');
        }

        private static void AppendString(StringBuilder b, string name, string value)
        {
            b.Append('"').Append(name).Append("\":\"")
                .Append(Escape(value)).Append("\",");
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
