using System;
using DigitalTwin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoosanDT.Experiment.Ros2
{
    /// <summary>
    /// Edit-Mode helpers for the E5 scene: apply Home_VR_DT as the default
    /// pose (so Play never snaps from all-zero) and persist the scene.
    /// </summary>
    public static class E5PoseMenu
    {
        private static readonly float[] HomeDeg = { 0f, 0f, 90f, 0f, 0f, 180f };

        [MenuItem("DigitalTwin/Experiment/Apply Home_VR_DT Pose (Edit Mode)", false, 970)]
        public static void ApplyHomePoseEditMode()
        {
            var robot = UnityEngine.Object.FindObjectOfType<RobotModelController>();
            if (robot == null)
            {
                Debug.LogError("[E5Pose] RobotModelController not found in scene");
                return;
            }
            try
            {
                robot.SetRuntimeHome0Degrees(HomeDeg);
                bool ok = robot.ApplyJointDisplayDegreesForEditMode(HomeDeg);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log("[E5Pose] edit-mode Home_VR_DT applied=" + ok);
            }
            catch (Exception ex)
            {
                Debug.LogError("[E5Pose] apply failed: " + ex.Message);
            }
        }

        [MenuItem("DigitalTwin/Experiment/Save Scene", false, 971)]
        public static void SaveScene()
        {
            bool ok = EditorSceneManager.SaveOpenScenes();
            Debug.Log("[E5Pose] SaveOpenScenes=" + ok);
        }
    }
}