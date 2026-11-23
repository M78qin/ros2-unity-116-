// TwinPlanContract.cs - Unity-side wire format for the offline planning contract.
//
// Scope: serialization/deserialization and state constants only.  This file
// never publishes and never calls a motion API.  The ROS2 side is
// ros2_unity_bridge/trajectory_contract.py (see ros2/PROTOCOL.md).
//
// Batch B (DSH 2026-11-08): staged patch, not applied.  Lead applies after the
// UI regression window and compiles in Unity.
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    public static class TwinPlanState
    {
        public const string Planned = "PLANNED";
        public const string Accepted = "ACCEPTED";
        public const string Executing = "EXECUTING";
        public const string Reached = "REACHED";
        public const string Cancelled = "CANCELLED";
        public const string Failed = "FAILED";
        public const string Unknown = "UNKNOWN";

        public const string ArrivalUnknown = "UNKNOWN";
        public const string ArrivalReached = "REACHED";
        public const string ArrivalCancelled = "CANCELLED";
        public const string ArrivalFailed = "FAILED";

        public const string FeedbackOk = "OK";
        public const string FeedbackUnknown = "UNKNOWN";
        public const string FeedbackLost = "LOST";

        public const string SchemaVersion = "dt_plan_contract_v1";
        public const string JointSpaceFrame = "joint_space";

        public static bool IsTerminal(string state)
        {
            return state == Reached || state == Cancelled || state == Failed;
        }
    }

    [Serializable]
    public sealed class TwinPlanStatusDto
    {
        public string schema_version = string.Empty;
        public string type = string.Empty;
        public string request_id = string.Empty;
        public string plan_id = string.Empty;
        public string state = TwinPlanState.Unknown;
        public string reason = string.Empty;
        public string arrival = TwinPlanState.ArrivalUnknown;
        public string feedback_quality = TwinPlanState.FeedbackUnknown;
        public int executed_waypoints;
        public int total_waypoints;
        public float duration_s;
        public bool dry_run = true;
        public string source = string.Empty;
        public string plan_digest = string.Empty;
    }

    public static class TwinPlanContract
    {
        public const string CommandPlan = "plan";
        public const string CommandAccept = "accept";
        public const string CommandStart = "start";
        public const string CommandCancel = "cancel";
        public const string CommandStatus = "status";

        /// <summary>Build a plan request. Every value is caller-supplied (no machine defaults).</summary>
        public static string BuildPlanRequestJson(
            string requestId,
            float[] seedJointRad,
            float[] targetJointRad,
            float[] limitMinDeg,
            float[] limitMaxDeg,
            float endpointToleranceDeg,
            bool dryRun,
            string source)
        {
            RequireSix(seedJointRad, "seedJointRad");
            RequireSix(targetJointRad, "targetJointRad");
            RequireSix(limitMinDeg, "limitMinDeg");
            RequireSix(limitMaxDeg, "limitMaxDeg");
            if (string.IsNullOrEmpty(requestId))
            {
                throw new ArgumentException("requestId is required", "requestId");
            }

            if (endpointToleranceDeg <= 0f || float.IsNaN(endpointToleranceDeg) ||
                float.IsInfinity(endpointToleranceDeg))
            {
                throw new ArgumentException("endpointToleranceDeg must be explicit and positive",
                    "endpointToleranceDeg");
            }

            var sb = new StringBuilder(384);
            sb.Append('{');
            AppendString(sb, "schema_version", TwinPlanState.SchemaVersion).Append(',');
            AppendString(sb, "command", CommandPlan).Append(',');
            AppendString(sb, "request_id", requestId).Append(',');
            AppendString(sb, "frame", TwinPlanState.JointSpaceFrame).Append(',');
            sb.Append("\"target\":{\"kind\":\"joints\",\"joints_rad\":");
            AppendFloatArray(sb, targetJointRad);
            sb.Append("},");
            sb.Append("\"seed_joints_rad\":");
            AppendFloatArray(sb, seedJointRad);
            sb.Append(',');
            sb.Append("\"joint_limits_deg\":[");
            for (int i = 0; i < 6; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('[').Append(FormatFloat(limitMinDeg[i])).Append(',')
                    .Append(FormatFloat(limitMaxDeg[i])).Append(']');
            }

            sb.Append("],");
            sb.Append("\"dry_run\":").Append(dryRun ? "true" : "false").Append(',');
            sb.Append("\"endpoint_tolerance_deg\":").Append(FormatFloat(endpointToleranceDeg)).Append(',');
            AppendString(sb, "source", string.IsNullOrEmpty(source) ? "explicit" : source);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Build an accept/start/cancel command. Executes nothing by itself.</summary>
        public static string BuildCommandJson(
            string command,
            string planId,
            bool dryRun,
            float[] currentJointRad)
        {
            if (command != CommandAccept && command != CommandStart &&
                command != CommandCancel && command != CommandStatus)
            {
                throw new ArgumentException("unsupported plan command: " + command, "command");
            }

            if (command != CommandStatus && string.IsNullOrEmpty(planId))
            {
                throw new ArgumentException("planId is required", "planId");
            }

            var sb = new StringBuilder(320);
            sb.Append('{');
            AppendString(sb, "schema_version", TwinPlanState.SchemaVersion).Append(',');
            AppendString(sb, "command", command).Append(',');
            AppendString(sb, "plan_id", planId ?? string.Empty);
            if (command == CommandAccept)
            {
                RequireSix(currentJointRad, "currentJointRad");
                sb.Append(",\"current_joints_rad\":");
                AppendFloatArray(sb, currentJointRad);
                sb.Append(",\"dry_run\":").Append(dryRun ? "true" : "false");
            }

            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParseStatus(string json, out TwinPlanStatusDto status, out string error)
        {
            status = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty plan status";
                return false;
            }

            try
            {
                status = JsonUtility.FromJson<TwinPlanStatusDto>(json);
                if (status == null)
                {
                    error = "plan status parsed to null";
                    return false;
                }

                if (string.IsNullOrEmpty(status.state))
                {
                    status.state = TwinPlanState.Unknown;
                }

                return true;
            }
            catch (Exception ex)
            {
                status = null;
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void RequireSix(float[] values, string name)
        {
            if (values == null || values.Length != 6)
            {
                throw new ArgumentException(name + " must have 6 elements", name);
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    throw new ArgumentException(name + "[" + i + "] is not finite", name);
                }
            }
        }

        private static StringBuilder AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(Escape(key)).Append("\":\"").Append(Escape(value ?? string.Empty)).Append('"');
            return sb;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void AppendFloatArray(StringBuilder sb, float[] values)
        {
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(FormatFloat(values[i]));
            }

            sb.Append(']');
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}