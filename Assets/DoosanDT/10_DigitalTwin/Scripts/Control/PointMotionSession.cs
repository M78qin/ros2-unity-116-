using System;
using DigitalTwin.Interaction;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

namespace DigitalTwin
{
    // Point intent coordinator for the existing control card. Hardware transport is deliberately
    // unavailable in this batch; request bodies are shared with the vendor-service adapter.
    //
    // 停止职责归属（2026-11-18 P0-F1）：本组件独占的 127.0.0.1:10003 模拟通道（域 87）**不承担停止职责**。
    // 该通道只服务模拟夹具的 motion/status（/dt/cmd/motion、/dt/status/motion、/dt_sim/current），
    // 不注册、也不发布 /dt/cmd/halt。所有停止请求统一走主 Ros2Bridge（127.0.0.1:10002，
    // std_msgs/Bool(true) -> /dt/cmd/halt），避免同一话题出现第二个消息类型与跨域停止。
    public sealed class PointMotionSession : MonoBehaviour
    {
        public enum Motion { MoveJoint, MoveJointx, MoveLine }
        public Motion Mode { get; private set; }
        public double[] Target { get; private set; } = new double[6];
        public string State { get; private set; } = "DISCONNECTED";
        public string Reason { get; private set; } = "仅隔离模拟；真机出口禁用";
        public string Digest { get; private set; } = "";
        public bool Authorized { get; private set; }
        public bool Connected => _ros != null && _ros.RosIPAddress == "127.0.0.1"
            && _ros.RosPort == 10003 && _ros.HasConnectionThread && Time.unscaledTime - _lastState < 1f;
        public bool Confirmed => State == "CONFIRMED" && Authorized && Connected;
        public bool Estopped { get; private set; }
        // 停止证据（P0-F1）：最后一次经主通道发 halt 的结果，便于 UI/测试区分"本地阻断"与"远端请求"。
        public bool LastHaltPublished { get; private set; }
        public string LastHaltStatus { get; private set; } = "NOT_REQUESTED";
        /// <summary>隔离的 10003/域87 通道是否为停止路径。恒为 false，供测试断言。</summary>
        public bool IsolationChannelCarriesHalt => false;
        public double[] CurrentJoints { get; private set; }
        public double[] CurrentTcp { get; private set; }
        public int Version { get; private set; }
        public string RequestId { get; private set; } = Guid.NewGuid().ToString("N");
        /// <summary>真正调用 /dt/cmd/motion 发布的次数（只在 Publish 时自增，消除 DispatchCalls 歧义）。</summary>
        public int PublishedMotionCmdCount { get; private set; }
        /// <summary>被第 4 章工艺/工具门禁拦下的次数。</summary>
        public int BlockedByProcessGateCount { get; private set; }
        /// <summary>最近一次工艺门禁结论（只读，供 UI/证据）。</summary>
        public ProcessGateResult LastProcessGate => ProcessStepGate.LastResult;
        public bool LastProcessGateHasResult => ProcessStepGate.HasResult;
        private ROSConnection _ros;
        private float _lastState = -100f;
        private JObject _preparedBody;
        private float _preparedAt;
        private bool _registered;
        private string _sourceSession = "";
        private long _sequence;
        private bool TransportAvailable => _ros != null && _ros.RosIPAddress == "127.0.0.1" && _ros.RosPort == 10003 && _ros.HasConnectionThread;
        public event Action Changed;

        public bool ConnectSimulation()
        {
            var command = FindObjectOfType<TwinCommandController>();
            if (command != null && command.EnableRealRobotCommand)
                return Fail("Profile 已启用真机，拒绝切换模拟连接");
            if (_ros == null) {
                // Keep the pre-existing global telemetry connection untouched.
                var transport = new GameObject("PointMotionSimulationTransport");
                transport.transform.SetParent(transform, false);
                _ros = transport.AddComponent<ROSConnection>();
                _ros.ConnectOnStart = false;
                _ros.ShowHud = false;
                _ros.TFTopics = new string[0];
            }
            _ros.ConnectOnStart = false;
            if (!_registered)
            {
                _ros.RegisterPublisher<StringMsg>("/dt/cmd/motion");
                // 刻意不注册 /dt/cmd/halt：该隔离通道不承担停止职责（见类头注释）。
                _ros.Subscribe<StringMsg>("/dt/status/motion", OnStatus);
                _ros.Subscribe<StringMsg>("/dt_sim/current", OnCurrent);
                _registered = true;
            }
            _ros.RosIPAddress = "127.0.0.1";
            _ros.RosPort = 10003;
            if (!_ros.HasConnectionThread) _ros.Connect();
            SetState("CONNECTING", "127.0.0.1:10003 / 域87；等待模拟身份与新鲜状态");
            return true;
        }

        public void SetMode(Motion mode)
        {
            Invalidate("运动方式已改变"); Mode = mode;
            LoadCurrent();
        }
        public bool SetTarget(double[] values)
        {
            if (values == null || values.Length != 6) return Fail("目标必须包含6个有限数值");
            foreach (double v in values) if (double.IsNaN(v) || double.IsInfinity(v)) return Fail("非有限目标");
            Invalidate("目标已改变，旧确认失效"); Target = (double[])values.Clone(); return true;
        }
        public bool LoadCurrent()
        {
            var current = Mode == Motion.MoveJoint ? CurrentJoints : CurrentTcp;
            if (!Connected || current == null) return Fail("缺少新鲜模拟状态");
            return SetTarget(current);
        }
        public bool LoadIk()
        {
            if (Mode != Motion.MoveJoint) return Fail("TCP世界坐标→厂商姿态约定待核实；可用关节IK解");
            var ik = FindObjectOfType<RobotIkController>();
            if (ik == null || !ik.TrySolve(true) || !ik.HasLastSolution) return Fail("Unity IK 未得到可用解");
            float[] q = ik.GetLastSolutionDegreesCopy();
            return SetTarget(Array.ConvertAll(q, x => (double)x));
        }
        public void Preview()
        {
            if (Mode != Motion.MoveJoint) { SetState("PREVIEW", "TCP目标已保留；厂商插补路径未验证"); return; }
            var model = FindObjectOfType<RobotModelController>();
            if(model == null || !Guard()) { Fail("缺少本地模型或新鲜模拟状态"); return; }
            for(int i=0;i<6;i++) {
                Vector2 limit=model.GetJointLimitsDeg(i);
                if(Target[i]<limit.x || Target[i]>limit.y) { Fail("目标超过Unity模型关节限位"); return; }
            }
            model.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
            bool shown=model.ApplyJointDisplayDegrees(Array.ConvertAll(Target,x=>(float)x),false,
                RobotModelController.PoseSource.CommandPreview,true,true);
            SetState(shown?"PREVIEW":"BLOCKED", shown?"本地关节目标预览；路径未验证；未发送运动命令":"模型拒绝预览");
        }
        public bool Prepare()
        {
            Invalidate("重新校验");
            if (!Guard()) return false;
            if (!CheckProcessGate()) return false;
            Version++;
            var body = new JObject { ["cmd"] = Mode == Motion.MoveJoint ? "move_joint" : Mode == Motion.MoveJointx ? "move_jointx" : "move_line",
                ["mode"] = 0, ["tcp"] = "SIM_TCP", ["tool"] = "SIM_TOOL" };
            body[Mode == Motion.MoveJoint ? "target_deg" : "target_tcp_mm_deg"] = new JArray(Target);
            body["vel"] = Mode == Motion.MoveLine ? (JToken)new JArray(10.0, 5.0) : new JValue(5.0);
            body["acc"] = Mode == Motion.MoveLine ? (JToken)new JArray(20.0, 10.0) : new JValue(10.0);
            if (Mode != Motion.MoveJoint) body["ref"] = 0;
            if (Mode == Motion.MoveJointx) body["sol"] = 0;
            _preparedBody = (JObject)body.DeepClone(); _preparedAt = Time.unscaledTime;
            RequestId = Guid.NewGuid().ToString("N");
            Send(new JObject { ["op"] = "prepare", ["req_id"] = RequestId, ["version"] = Version, ["command"] = body });
            SetState("CHECKING", "等待ROS2公共校验；模拟参数非现场批准值"); return true;
        }
        public void SetAuthorization(bool value)
        {
            Authorized = value;
            if (!value) Invalidate("已撤销授权");
            Changed?.Invoke();
        }
        public bool Confirm()
        {
            if (!Guard() || !Authorized || State != "PLANNED" || string.IsNullOrEmpty(Digest))
                return Fail("先完成校验和本次模拟授权");
            Send(Identity("confirm", true)); return true;
        }
        public bool Execute()
        {
            if (!Guard() || !Confirmed || _preparedBody == null || Time.unscaledTime - _preparedAt > 120f)
                return Fail("确认已失效或缺少授权");
            if (!CheckProcessGate()) return false;
            Send(Identity("execute", false)); SetState("SENDING", "等待厂商类型模拟服务受理");
            Authorized = false; return true;
        }

        // ── 第 4 章工艺/工具门禁（Ghost 目标 → 工艺顺序 / 工具属性 / 预标定点位绑定）──────
        // 位置：Guard() 之后、真正构造/发送报文之前。被拒时置 BLOCKED 且不发布任何运动报文。

        /// <summary>按"当前 Ghost 目标 + 当前 TCP 通道"评估工艺门禁；被拒则置 BLOCKED。</summary>
        public bool CheckProcessGate()
        {
            return CheckProcessGate(ResolveGateTargetWorld(), ProcessStepGate.DefaultChannel);
        }

        /// <summary>显式评估（测试/预检用）：被拒时置 BLOCKED、计数 +1、且不发布。</summary>
        public bool CheckProcessGate(Vector3 worldPos, ProcessToolChannel channel)
        {
            ProcessGateResult result;
            if (ProcessStepGate.EvaluateTarget(worldPos, channel, out result))
            {
                return true;
            }

            BlockedByProcessGateCount++;
            SetState("BLOCKED", "工艺门禁拒绝（" + result.ReasonCode + "）：" + result.Message);
            return false;
        }

        /// <summary>Ghost 目标世界坐标：优先取 IK 手柄的 ghost 目标，其次用 MoveJoint 目标做 FK。</summary>
        private Vector3 ResolveGateTargetWorld()
        {
            var handle = FindObjectOfType<TwinPlayIkHandle>();
            if (handle != null && handle.Initialized)
            {
                return handle.TargetPosition;
            }

            if (Mode == Motion.MoveJoint && Target != null && Target.Length == 6)
            {
                var model = FindObjectOfType<RobotModelController>();
                var switcher = FindObjectOfType<ToolTcpSwitcher>();
                Transform tcp = switcher != null ? switcher.ActiveTcpTransform : null;
                if (model != null && tcp != null)
                {
                    var q = Array.ConvertAll(Target, x => (float)x);
                    Vector3 pos;
                    Quaternion rot;
                    if (model.EvaluateTcpPoseDegrees(q, tcp, Vector3.zero, Vector3.zero, out pos, out rot))
                    {
                        return pos;
                    }
                }
            }

            return Vector3.zero;
        }
        public void Cancel(bool estop = false)
        {
            if (estop) Estopped = true;
            // P0-F1: 停止只走主 Ros2Bridge（10002 / std_msgs.Bool），不在 10003/域87 上发 halt。
            bool sent = false;
            string status = "NO_BRIDGE";
            var bridge = FindObjectOfType<Ros2Bridge>();
            if (bridge != null)
            {
                RobotCommandResult halt = bridge.SendHalt();
                sent = halt.Success;
                status = halt.Status + (string.IsNullOrEmpty(halt.ErrorMessage) ? string.Empty : "|" + halt.ErrorMessage);
            }
            LastHaltPublished = sent;
            LastHaltStatus = status;
            Authorized = false; Digest = ""; _preparedBody = null;
            FindObjectOfType<TwinCommandController>()?.CancelPlan();
            RequestId = Guid.NewGuid().ToString("N");
            SetState(estop ? "ESTOP_REQUESTED" : "CANCEL_REQUESTED",
                sent
                    ? "主通道(10002/Bool)停止请求已发送：" + status + "；远端停止未确认，非物理停止证明"
                    : "主通道停止发送失败(" + status + ")；仅本地阻断，服务端状态未知");
        }
        public void Invalidate(string reason)
        {
            if (_preparedBody != null && TransportAvailable) Send(new JObject { ["op"] = "cancel" });
            Digest = ""; _preparedBody = null;
            Authorized = false; RequestId = Guid.NewGuid().ToString("N");
            SetState("EDITING", reason);
        }
        private JObject Identity(string op, bool auth) => new JObject { ["op"] = op, ["req_id"] = RequestId,
            ["version"] = Version, ["target_digest"] = Digest, ["authorized"] = auth && Authorized };
        private bool Guard()
        {
            var c = FindObjectOfType<TwinCommandController>();
            if (Estopped || (c != null && (c.IsEmergencyStopped || c.EnableRealRobotCommand))) return Fail("急停或真机配置阻断");
            return Connected || Fail("模拟通信断开或状态超过1秒");
        }
        private void Send(JObject p)
        {
            _ros.Publish("/dt/cmd/motion", new StringMsg(p.ToString(Newtonsoft.Json.Formatting.None)));
            PublishedMotionCmdCount++;
        }
        private bool Fail(string reason) { SetState("BLOCKED", reason); return false; }
        private void SetState(string state, string reason) { State = state; Reason = reason; Changed?.Invoke(); }
        private void OnCurrent(StringMsg message)
        {
            try {
                var p = JObject.Parse(message.data);
                if ((string)p["source"] != "SIMULATED" || (bool?)p["hardware_enabled"] != false) return;
                string session = (string)p["source_session_id"];
                long seq = (long?)p["sequence"] ?? 0;
                if (string.IsNullOrEmpty(session) || seq <= 0) return;
                if (_sourceSession != session) {
                    Invalidate("模拟器会话改变，旧确认已撤销"); _sourceSession = session; _sequence = 0;
                }
                if (seq <= _sequence) return;
                double[] q=p["joints_deg"].ToObject<double[]>(), tcp=p["tcp_mm_deg"].ToObject<double[]>();
                if (q.Length != 6 || tcp.Length != 6) return;
                foreach(double x in q) if(double.IsNaN(x)||double.IsInfinity(x)) return;
                foreach(double x in tcp) if(double.IsNaN(x)||double.IsInfinity(x)) return;
                CurrentJoints=q; CurrentTcp=tcp; _sequence=seq;
                _lastState = Time.unscaledTime;
                if (State == "CONNECTING" || State == "EDITING") SetState("READY", "模拟接口夹具在线；关节/TCP未建立FK一致性；真机禁用");
            } catch (Exception e) { Fail("模拟状态解析失败：" + e.Message); }
        }
        private void OnStatus(StringMsg message)
        {
            try {
                var p = JObject.Parse(message.data);
                if ((string)p["source"] != "SIMULATED" || (bool?)p["hardware_enabled"] != false) return;
                if ((string)p["source_session_id"] != _sourceSession || (string)p["req_id"] != RequestId
                    || (int?)p["version"] != Version || _preparedBody == null) return;
                if ((string)p["state"] == "PLANNED") {
                    if ((int?)p["version"] != Version || _preparedBody == null) return;
                    Digest = (string)p["target_digest"];
                } else if ((string)p["state"] != "REJECTED" &&
                    (string.IsNullOrEmpty(Digest) || (string)p["target_digest"] != Digest)) return;
                SetState((string)p["state"], (string)p["reason"]);
            } catch (Exception e) { Fail("模拟反馈解析失败：" + e.Message); }
        }
        private void Update()
        {
            if (_preparedBody != null && (!Connected || Time.unscaledTime - _preparedAt > 120f))
                Invalidate("通信或计划已过期");
        }
    }
}
