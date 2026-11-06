// Enables the two telemetry channels the chart needs, and records the change.
//
// Menu: DigitalTwin/UI/4 Apply Force Channels
//
// Background (verified in the previous batch):
//   * Ros2Bridge.ApplyRtCache reads external_tcp_force -> RobotStateFrame.ForceVector[6] and
//     external_joint_torque -> RobotStateFrame.JointTorqueNm[6] (Ros2Bridge.cs:1573-1600).
//   * Ros2ExperimentPanel.BuildTelemetryChannels() only returns those keys when the matching bool
//     fields are set, and the scene has them all false, so the frame never carried force/torque.
//   * The bag from E1 proves /dsr01/rt_topic/external_tcp_force is published at 100 Hz; the
//     external_joint_torque topic is NOT proven yet (only actual_joint_torque appears in the bag).
//
// This menu only flips SCENE CONFIGURATION. It does not touch Ros2Bridge code, does not start a
// stream, does not connect ROS2. actualJointTorque is enabled too, so a future session can compare
// it in the raw CSV, but note that the frame's JointTorqueNm is fed by external_joint_torque only.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIApplyForceChannels
    {
        private const string LogPath = "Logs/ui_force_channels.txt";
        private const string PanelPath = "00_数字孪生机械臂/Ros2Experimen";

        [MenuItem("DigitalTwin/UI/4 Apply Force Channels")]
        public static void Apply()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI force/torque channel enable  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Ros2ExperimentPanel panel = TwinSceneLookup.FindComponent<Ros2ExperimentPanel>(PanelPath);
            if (panel == null)
            {
                sb.AppendLine("RESULT: FAIL - Ros2ExperimentPanel not found at " + PanelPath);
                Write(sb);
                return;
            }

            var so = new SerializedObject(panel);
            string[] fields = { "externalTcpForce", "externalJointTorque", "actualJointTorque" };
            int changed = 0;
            foreach (string field in fields)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    sb.AppendLine("  " + field + " : property not found");
                    continue;
                }

                sb.AppendLine("  " + field + ": " + property.boolValue + " -> true");
                if (!property.boolValue)
                {
                    property.boolValue = true;
                    changed++;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);

            sb.AppendLine();
            sb.AppendLine("effect:");
            sb.AppendLine("  Ros2Bridge subscribes /dsr01/rt_topic/external_tcp_force      (Float32MultiArray)");
            sb.AppendLine("  Ros2Bridge subscribes /dsr01/rt_topic/external_joint_torque   (Float32MultiArray)");
            sb.AppendLine("  Ros2Bridge subscribes /dsr01/rt_topic/actual_joint_torque     (cached, not mapped to the frame)");
            sb.AppendLine("  RobotStateFrame.Flags gets HasForce / HasJointTorque -> the UI chart stops saying 未订阅");
            sb.AppendLine();
            sb.AppendLine("research impact (must be recorded in the experiment ledger):");
            sb.AppendLine("  E4 treatment variable 'display overhead' changes: force_channels=on");
            sb.AppendLine("  historical E4_B sessions were run with force_channels=off and are not directly comparable");
            sb.AppendLine("  external_joint_torque publication by the DSR controller is UNPROVEN (UNKNOWN) - if the topic is");
            sb.AppendLine("  silent, frame.JointTorqueNm stays empty and the UI keeps reporting 'not subscribed'");

            if (changed > 0)
            {
                Scene scene = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                sb.AppendLine();
                sb.AppendLine("scene saved, fields changed=" + changed);
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("no change required (channels already enabled)");
            }

            sb.AppendLine("RESULT: PASS");
            Write(sb);
            Debug.Log("[UI Channels] see " + LogPath);
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}