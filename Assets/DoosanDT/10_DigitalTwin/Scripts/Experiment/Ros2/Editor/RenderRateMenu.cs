using DoosanDT.UI.Runtime;
using UnityEditor;
using UnityEngine;

namespace DoosanDT.Experiment.Ros2
{
    /// <summary>
    /// Editor-side switches for the runtime render-rate panel, so the four
    /// gears can be exercised in Play Mode without clicking the IMGUI panel.
    /// </summary>
    public static class RenderRateMenu
    {
        [MenuItem("DigitalTwin/Experiment/Render Rate/30 Hz", false, 950)]
        public static void Rate30() { Apply(30); }

        [MenuItem("DigitalTwin/Experiment/Render Rate/60 Hz", false, 951)]
        public static void Rate60() { Apply(60); }

        [MenuItem("DigitalTwin/Experiment/Render Rate/90 Hz", false, 952)]
        public static void Rate90() { Apply(90); }

        [MenuItem("DigitalTwin/Experiment/Render Rate/Unlimited", false, 953)]
        public static void RateUnlimited() { Apply(-1); }

        [MenuItem("DigitalTwin/Experiment/Render Rate/Log FPS", false, 954)]
        public static void LogFps()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RenderRate] not in Play Mode");
                return;
            }
            Debug.Log("[RenderRate] measured FPS=" + RenderRatePanel.CurrentFps.ToString("F1") +
                      " target=" + Application.targetFrameRate +
                      " vSync=" + QualitySettings.vSyncCount);
        }

        private static void Apply(int hz)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RenderRate] not in Play Mode; target set anyway");
            }
            RenderRatePanel.SetRate(hz);
        }
    }
}