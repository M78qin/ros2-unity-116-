// Wire-compatible with ros2_unity_interfaces/msg/TwinJointSample.msg.
// Uses the installed ROS-TCP-Connector MessageGeneration API.
using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
namespace RosMessageTypes.Ros2UnityInterfaces
{
    [Serializable]
    public class TwinJointSampleMsg : Message
    {
        public const string k_RosMessageName = "ros2_unity_interfaces/TwinJointSample";
        public override string RosMessageName => k_RosMessageName;
        public string run_id;
        public string source_id;
        public string source_session_id;
        public ulong source_seq;
        public long acquisition_ros_ns;
        public long acquisition_wall_ns;
        public long acquisition_monotonic_ns;
        public double controller_time;
        public bool controller_time_valid;
        public bool joints_valid;
        public bool raw_enqueued;
        public RosMessageTypes.Sensor.JointStateMsg joints;
        public TwinJointSampleMsg() { run_id=""; source_id=""; source_session_id=""; joints=new RosMessageTypes.Sensor.JointStateMsg(); }
        public static TwinJointSampleMsg Deserialize(MessageDeserializer d) => new TwinJointSampleMsg(d);
        private TwinJointSampleMsg(MessageDeserializer d)
        {
            d.Read(out run_id);
            d.Read(out source_id);
            d.Read(out source_session_id);
            d.Read(out source_seq);
            d.Read(out acquisition_ros_ns);
            d.Read(out acquisition_wall_ns);
            d.Read(out acquisition_monotonic_ns);
            d.Read(out controller_time);
            d.Read(out controller_time_valid);
            d.Read(out joints_valid);
            d.Read(out raw_enqueued);
            joints=RosMessageTypes.Sensor.JointStateMsg.Deserialize(d);
        }
        public override void SerializeTo(MessageSerializer s)
        {
            s.Write(run_id);
            s.Write(source_id);
            s.Write(source_session_id);
            s.Write(source_seq);
            s.Write(acquisition_ros_ns);
            s.Write(acquisition_wall_ns);
            s.Write(acquisition_monotonic_ns);
            s.Write(controller_time);
            s.Write(controller_time_valid);
            s.Write(joints_valid);
            s.Write(raw_enqueued);
            s.Write(joints);
        }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register() { MessageRegistry.Register(k_RosMessageName,Deserialize); }
    }
}
