// TwinPlanRequestBridge.cs - Unity-side entry points for the offline planning
// contract (plan / accept / start / cancel + status display).
//
// Hard boundaries for batch B (DSH 2026-11-08):
//   * This component NEVER calls SendMoveJoint / SendDoosanMotion* / Halt.
//   * A drag preview can be planned (source="drag") but accept/start refuse it.
//   * Accept requires an observed PLANNED status for the same plan_id; start
//     requires ACCEPTED; both are additionally enforced by the ROS2 contract.
//   * No value is defaulted: joint limits, seed joints, endpoint tolerance and
//     dry_run are all supplied by the caller.
//
// Staged patch: not applied.  Lead applies after the UI regression window.
using System;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinPlanRequestBridge : MonoBehaviour
    {
        [SerializeField, Tooltip("目标 Ros2Bridge；留空时自动查找。")]
        private Ros2Bridge ros2Bridge;

        [SerializeField, Tooltip("允许自动查找场景中的 Ros2Bridge。")]
        private bool autoResolveBridge = true;

        [SerializeField, Tooltip("打印规划请求/状态变化日志（只读，不触发运动）。")]
        private bool logPlanEvents = true;

        private TwinPlanStatusDto _status;
        private string _statusJson = string.Empty;
        private string _statusError = string.Empty;
        private long _lastStatusReceiveNs;
        private string _lastRequestId = string.Empty;
        private string _lastPlanId = string.Empty;
        private string _lastError = string.Empty;

        public TwinPlanStatusDto LastStatus => _status;
        public string LastStatusJson => _statusJson;
        public string LastError => _lastError;
        public string StatusError => _statusError;
        public string LastRequestId => _lastRequestId;
        public string LastPlanId => _lastPlanId;
        public bool HasStatus => _status != null;

        public bool IsBridgeReady => ros2Bridge != null && ros2Bridge.IsConnected;

        private void Awake()
        {
            ResolveBridge();
        }

        private void Update()
        {
            if (ros2Bridge == null && autoResolveBridge)
            {
                ResolveBridge();
            }

            if (ros2Bridge == null)
            {
                return;
            }

            long receiveNs = ros2Bridge.PlanStatusReceiveNs;
            if (receiveNs != _lastStatusReceiveNs)
            {
                _lastStatusReceiveNs = receiveNs;
                RefreshStatus();
            }
        }

        /// <summary>Publish a plan request. Returns request_id, or empty on refusal.</summary>
        public string RequestPlanJoints(
            float[] seedJointRad,
            float[] targetJointRad,
            float[] limitMinDeg,
            float[] limitMaxDeg,
            float endpointToleranceDeg,
            bool dryRun,
            string source)
        {
            ResolveBridge();
            if (ros2Bridge == null)
            {
                return Reject("Ros2Bridge is unavailable.");
            }

            string requestId = Guid.NewGuid().ToString("N");
            string json;
            try
            {
                json = TwinPlanContract.BuildPlanRequestJson(
                    requestId, seedJointRad, targetJointRad, limitMinDeg, limitMaxDeg,
                    endpointToleranceDeg, dryRun, source);
            }
            catch (ArgumentException ex)
            {
                return Reject(ex.Message);
            }

            if (!ros2Bridge.PublishPlanRequest(json))
            {
                return Reject("PublishPlanRequest failed: " + ros2Bridge.LastError);
            }

            _lastRequestId = requestId;
            _lastError = string.Empty;
            if (logPlanEvents)
            {
                Debug.Log("[PlanContract] request " + requestId + " dry_run=" + dryRun +
                          " source=" + (string.IsNullOrEmpty(source) ? "explicit" : source), this);
            }

            return requestId;
        }

        /// <summary>Plan a drag preview. Executing a drag preview is refused by contract.</summary>
        public string SubmitDragPreview(
            float[] seedJointRad,
            float[] targetJointRad,
            float[] limitMinDeg,
            float[] limitMaxDeg,
            float endpointToleranceDeg)
        {
            string requestId = RequestPlanJoints(seedJointRad, targetJointRad, limitMinDeg,
                limitMaxDeg, endpointToleranceDeg, true, "drag");
            if (!string.IsNullOrEmpty(requestId) && logPlanEvents)
            {
                Debug.Log("[PlanContract] drag preview planned only; execution stays disabled.", this);
            }

            return requestId;
        }

        /// <summary>Accept a PLANNED plan. Still only advances the offline contract state.</summary>
        public bool RequestAccept(string planId, float[] currentJointRad, bool dryRun)
        {
            if (!HasStatus)
            {
                return RejectBool("No plan status received yet.");
            }

            if (!string.IsNullOrEmpty(planId) && _status.plan_id != planId)
            {
                return RejectBool("Plan id mismatch: latest=" + _status.plan_id);
            }

            if (_status.state != TwinPlanState.Planned)
            {
                return RejectBool("Plan is not PLANNED (state=" + _status.state + ").");
            }

            if (dryRun)
            {
                return RejectBool("dry_run_execution_disabled");
            }

            if (!PublishCommand(TwinPlanContract.CommandAccept, planId, dryRun, currentJointRad))
            {
                return false;
            }

            _lastPlanId = planId;
            return true;
        }

        public bool RequestStart(string planId)
        {
            if (!HasStatus)
            {
                return RejectBool("No plan status received yet.");
            }

            if (_status.state != TwinPlanState.Accepted)
            {
                return RejectBool("Plan is not ACCEPTED (state=" + _status.state + ").");
            }

            if (!string.IsNullOrEmpty(planId) && _status.plan_id != planId)
            {
                return RejectBool("Plan id mismatch: latest=" + _status.plan_id);
            }

            return PublishCommand(TwinPlanContract.CommandStart, planId, false, null);
        }

        public bool RequestCancel(string planId, string reason)
        {
            if (!HasStatus)
            {
                return RejectBool("No plan status received yet.");
            }

            if (TwinPlanState.IsTerminal(_status.state))
            {
                return RejectBool("Plan is already terminal (state=" + _status.state + ").");
            }

            return PublishCommand(TwinPlanContract.CommandCancel, planId, false, null, reason);
        }

        /// <summary>Parse the latest raw status JSON from Ros2Bridge. No network, no motion.</summary>
        public void RefreshStatus()
        {
            if (ros2Bridge == null)
            {
                return;
            }

            string json = ros2Bridge.LatestPlanStatusJson;
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            _statusJson = json;
            if (TwinPlanContract.TryParseStatus(json, out TwinPlanStatusDto parsed, out string error))
            {
                _status = parsed;
                _statusError = string.Empty;
                if (!string.IsNullOrEmpty(parsed.plan_id))
                {
                    _lastPlanId = parsed.plan_id;
                }

                if (logPlanEvents)
                {
                    Debug.Log("[PlanContract] status " + parsed.plan_id + " " + parsed.state +
                              " arrival=" + parsed.arrival + " feedback=" + parsed.feedback_quality +
                              " reason=" + parsed.reason, this);
                }
            }
            else
            {
                _statusError = error;
                if (logPlanEvents)
                {
                    Debug.LogWarning("[PlanContract] status parse failed: " + error, this);
                }
            }
        }

        private bool PublishCommand(string command, string planId, bool dryRun,
            float[] currentJointRad, string reason = null)
        {
            ResolveBridge();
            if (ros2Bridge == null)
            {
                return RejectBool("Ros2Bridge is unavailable.");
            }

            string json;
            try
            {
                json = TwinPlanContract.BuildCommandJson(command, planId, dryRun, currentJointRad);
            }
            catch (ArgumentException ex)
            {
                return RejectBool(ex.Message);
            }

            if (!string.IsNullOrEmpty(reason))
            {
                // cancel reason is optional in the contract; keep it explicit when supplied.
                json = json.Substring(0, json.Length - 1) + ",\"reason\":\"" +
                       reason.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
            }

            if (!ros2Bridge.PublishPlanCommand(json))
            {
                return RejectBool("PublishPlanCommand failed: " + ros2Bridge.LastError);
            }

            _lastError = string.Empty;
            if (logPlanEvents)
            {
                Debug.Log("[PlanContract] command " + command + " plan=" + planId, this);
            }

            return true;
        }

        private void ResolveBridge()
        {
            if (ros2Bridge != null)
            {
                return;
            }

            if (!autoResolveBridge)
            {
                return;
            }

            ros2Bridge = GetComponent<Ros2Bridge>();
            if (ros2Bridge == null)
            {
                ros2Bridge = FindObjectOfType<Ros2Bridge>();
            }
        }

        private string Reject(string message)
        {
            _lastError = message;
            Debug.LogWarning("[PlanContract] rejected: " + message, this);
            return string.Empty;
        }

        private bool RejectBool(string message)
        {
            _lastError = message;
            if (logPlanEvents)
            {
                Debug.LogWarning("[PlanContract] rejected: " + message, this);
            }

            return false;
        }
    }
}