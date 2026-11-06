using System;
using System.Collections.Generic;
using System.Globalization;

namespace DigitalTwin
{
    [Serializable]
    public sealed class Ros2TelemetryChannels
    {
        public bool jointPosition = true;
        public bool jointVelocity = true;
        public bool jointEffort = true;
        public bool actualTcpPose = true;
        public bool actualTcpVelocity = true;
        public bool actualFlangePose = true;
        public bool actualFlangeVelocity = true;
        public bool externalTcpForce = true;
        public bool externalJointTorque = true;
        public bool actualJointTorque = true;
        public bool actualMotorTorque = true;
        public bool rawJointTorque = true;
        public bool rawForceTorque = true;
        public bool gravityTorque = true;
        public bool targetJointPosition = true;
        public bool targetJointVelocity = true;
        public bool targetTcpPosition = true;
        public bool targetTcpVelocity = true;
        public bool targetMotorTorque = true;
        public bool goalJointPosition = true;
        public bool goalTcpPosition = true;
        public bool targetJointAcceleration = true;
        public bool robotMode = true;
        public bool robotState = true;
        public bool controlMode = true;
        public bool jointTemperature = true;
        public bool solutionSpace = true;
        public bool operationSpeedRate = true;
        public bool singularity = true;
        public bool actualJointPositionAbs = true;
        public bool actualJointVelocityAbs = true;

        public Dictionary<string, bool> ToRosChannelMap()
        {
            return new Dictionary<string, bool>
            {
                ["joint_position"] = jointPosition,
                ["joint_velocity"] = jointVelocity,
                ["joint_effort"] = jointEffort,
                ["actual_tcp_position"] = actualTcpPose,
                ["actual_tcp_velocity"] = actualTcpVelocity,
                ["actual_flange_position"] = actualFlangePose,
                ["actual_flange_velocity"] = actualFlangeVelocity,
                ["external_tcp_force"] = externalTcpForce,
                ["external_joint_torque"] = externalJointTorque,
                ["actual_joint_torque"] = actualJointTorque,
                ["actual_motor_torque"] = actualMotorTorque,
                ["raw_joint_torque"] = rawJointTorque,
                ["raw_force_torque"] = rawForceTorque,
                ["gravity_torque"] = gravityTorque,
                ["target_joint_position"] = targetJointPosition,
                ["target_joint_velocity"] = targetJointVelocity,
                ["target_tcp_position"] = targetTcpPosition,
                ["target_tcp_velocity"] = targetTcpVelocity,
                ["target_motor_torque"] = targetMotorTorque,
                ["goal_joint_position"] = goalJointPosition,
                ["goal_tcp_position"] = goalTcpPosition,
                ["target_joint_acceleration"] = targetJointAcceleration,
                ["robot_mode"] = robotMode,
                ["robot_state"] = robotState,
                ["control_mode"] = controlMode,
                ["joint_temperature"] = jointTemperature,
                ["solution_space"] = solutionSpace,
                ["operation_speed_rate"] = operationSpeedRate,
                ["singularity"] = singularity,
                ["actual_joint_position_abs"] = actualJointPositionAbs,
                ["actual_joint_velocity_abs"] = actualJointVelocityAbs,
            };
        }

        /// <summary>论文级：与 WSL2 channels.yaml 可采集项对齐（矩阵/IO 仍由 ROS2 侧记录）。</summary>
        public static Ros2TelemetryChannels PaperGradeAll()
        {
            return new Ros2TelemetryChannels();
        }

        public static Ros2TelemetryChannels FromPanel(
            bool joints, bool vel, bool eff, bool force, bool tcp, bool extra)
        {
            var ch = PaperGradeAll();
            ch.jointPosition = joints;
            ch.jointVelocity = vel;
            ch.jointEffort = eff;
            ch.externalTcpForce = force;
            ch.externalJointTorque = force;
            ch.actualTcpPose = tcp;
            if (!extra)
            {
                ch.actualJointTorque = false;
                ch.actualMotorTorque = false;
                ch.rawForceTorque = false;
                ch.targetJointPosition = false;
                ch.goalJointPosition = false;
            }

            return ch;
        }

        public string ToJsonNotes()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, bool> kv in ToRosChannelMap())
            {
                parts.Add("\"" + kv.Key + "\":" + (kv.Value ? "true" : "false"));
            }

            return "{" + string.Join(",", parts) + "}";
        }
    }
}
