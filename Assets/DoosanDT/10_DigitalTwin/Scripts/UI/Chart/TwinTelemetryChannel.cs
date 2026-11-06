// Channel catalog for the telemetry chart.
//
// Units follow the thesis conventions: joint angles in degrees (the runtime frame stores radians,
// the binder converts), joint torque in N.m, six-axis force/torque in N / N.m. Nothing here
// simulates data: a channel that has no source must be shown as "no source", never as a flat 0.
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    public enum TwinTelemetryChannel
    {
        Joint1 = 0,
        Joint2 = 1,
        Joint3 = 2,
        Joint4 = 3,
        Joint5 = 4,
        Joint6 = 5,

        ForceX = 10,
        ForceY = 11,
        ForceZ = 12,
        TorqueX = 13,
        TorqueY = 14,
        TorqueZ = 15,

        JointTorque1 = 20,
        JointTorque2 = 21,
        JointTorque3 = 22,
        JointTorque4 = 23,
        JointTorque5 = 24,
        JointTorque6 = 25,
    }

    public enum TwinTelemetryGroup
    {
        JointAngle = 0,
        Wrench = 1,
        JointTorque = 2,
    }

    public sealed class TwinTelemetryChannelInfo
    {
        public TwinTelemetryChannel Channel;
        public TwinTelemetryGroup Group;
        public string Label;
        public string Unit;
        public Color Color;

        public bool IsJointAngle => Group == TwinTelemetryGroup.JointAngle;
        public bool IsWrench => Group == TwinTelemetryGroup.Wrench;
        public bool IsJointTorque => Group == TwinTelemetryGroup.JointTorque;
    }

    public static class TwinTelemetryCatalog
    {
        // 三期：曲线颜色跟主题走（深色用高亮青/蓝/琥珀；浅色用深蓝/绿/棕），不再写死。
        private static Color SeriesColor(int index)
        {
            Color[] series = DigitalTwin.UI.TwinUITheme.Current.Series;
            if (series != null && series.Length > 0)
            {
                return series[((index % series.Length) + series.Length) % series.Length];
            }

            return new Color(0.302f, 0.420f, 0.996f);
        }

        private static readonly Color[] JointColors =
        {
            new Color(0.302f, 0.420f, 0.996f), // J1 #4D6BFE
            new Color(0.082f, 0.502f, 0.239f), // J2 #15803D
            new Color(0.706f, 0.325f, 0.035f), // J3 #B45309
            new Color(0.863f, 0.149f, 0.149f), // J4 #DC2626
            new Color(0.486f, 0.227f, 0.929f), // J5 #7C3AED
            new Color(0.055f, 0.455f, 0.565f), // J6 #0E7490
        };

        private static readonly Color[] WrenchColors =
        {
            new Color(0.302f, 0.420f, 0.996f), // Fx #4D6BFE
            new Color(0.082f, 0.502f, 0.239f), // Fy #15803D
            new Color(0.706f, 0.325f, 0.035f), // Fz #B45309
            new Color(0.863f, 0.149f, 0.149f), // Tx #DC2626
            new Color(0.486f, 0.227f, 0.929f), // Ty #7C3AED
            new Color(0.055f, 0.455f, 0.565f), // Tz #0E7490
        };

        public static readonly TwinTelemetryChannel[] DefaultJointAngleSeries =
        {
            TwinTelemetryChannel.Joint1, TwinTelemetryChannel.Joint2, TwinTelemetryChannel.Joint3,
        };

        public static readonly TwinTelemetryChannel[] DefaultWrenchSeries =
        {
            TwinTelemetryChannel.ForceX, TwinTelemetryChannel.ForceY, TwinTelemetryChannel.ForceZ,
        };

        public static readonly TwinTelemetryChannel[] DefaultTorqueSeries =
        {
            TwinTelemetryChannel.JointTorque1, TwinTelemetryChannel.JointTorque2, TwinTelemetryChannel.JointTorque3,
        };

        public static TwinTelemetryChannelInfo Get(TwinTelemetryChannel channel)
        {
            int index = (int)channel;
            if (channel >= TwinTelemetryChannel.Joint1 && channel <= TwinTelemetryChannel.Joint6)
            {
                return new TwinTelemetryChannelInfo
                {
                    Channel = channel,
                    Group = TwinTelemetryGroup.JointAngle,
                    Label = "J" + (index + 1),
                    Unit = "deg",
                    Color = SeriesColor(index),
                };
            }

            if (channel >= TwinTelemetryChannel.ForceX && channel <= TwinTelemetryChannel.TorqueZ)
            {
                int i = index - (int)TwinTelemetryChannel.ForceX;
                return new TwinTelemetryChannelInfo
                {
                    Channel = channel,
                    Group = TwinTelemetryGroup.Wrench,
                    Label = WrenchLabel(i),
                    Unit = i < 3 ? "N" : "N.m",
                    Color = SeriesColor(i),
                };
            }

            int t = index - (int)TwinTelemetryChannel.JointTorque1;
            return new TwinTelemetryChannelInfo
            {
                Channel = channel,
                Group = TwinTelemetryGroup.JointTorque,
                Label = "T" + (t + 1),
                Unit = "N.m",
                Color = SeriesColor(t) * 0.85f,
            };
        }

        private static string WrenchLabel(int i)
        {
            switch (i)
            {
                case 0: return "Fx";
                case 1: return "Fy";
                case 2: return "Fz";
                case 3: return "Tx";
                case 4: return "Ty";
                default: return "Tz";
            }
        }

        public static IEnumerable<TwinTelemetryChannel> Enumerate(TwinTelemetryGroup group)
        {
            switch (group)
            {
                case TwinTelemetryGroup.JointAngle:
                    for (int i = 0; i < 6; i++) yield return (TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i);
                    break;
                case TwinTelemetryGroup.Wrench:
                    for (int i = 0; i < 6; i++) yield return (TwinTelemetryChannel)((int)TwinTelemetryChannel.ForceX + i);
                    break;
                default:
                    for (int i = 0; i < 6; i++) yield return (TwinTelemetryChannel)((int)TwinTelemetryChannel.JointTorque1 + i);
                    break;
            }
        }
    }
}