using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.Experiment.Ros2.EditorTools
{
    /// <summary>
    /// 只做 Play 模式进出与状态上报，供 MCP 驱动的实验会话在不碰鼠标的情况下控制编辑器。
    /// 不改变任何运行期逻辑，不写入场景/资产。
    /// </summary>
    public static class Chapter4PlayControl
    {
        [MenuItem("DigitalTwin/Chapter4/14 Enter Play Mode")]
        public static void EnterPlay()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[CH4 Play] already in play mode");
                return;
            }
            EditorApplication.isPlaying = true;
            Debug.Log("[CH4 Play] enter play mode requested");
        }

        [MenuItem("DigitalTwin/Chapter4/15 Exit Play Mode")]
        public static void ExitPlay()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.Log("[CH4 Play] not in play mode");
                return;
            }
            EditorApplication.isPlaying = false;
            Debug.Log("[CH4 Play] exit play mode requested");
        }

        [MenuItem("DigitalTwin/Chapter4/16 Report Play State")]
        public static void ReportState()
        {
            string scene = SceneManager.GetActiveScene().name;
            Debug.Log($"[CH4 Play] isPlaying={EditorApplication.isPlaying} isPaused={EditorApplication.isPaused} isCompiling={EditorApplication.isCompiling} scene={scene}");
        }
    }
}