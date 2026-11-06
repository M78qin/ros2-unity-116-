// Routes the four teaching modes of Doosan-DT (漫游 / 训练 / 拆解 / 数据).
//
// Design notes (why it is built this way):
// * It only ever calls SetActive on objects that already existed in the scene. The five legacy
//   canvases keep their own render mode / scale / world placement - re-parenting them into one
//   canvas would visibly move them (2 Overlay + 1 ScreenSpaceCamera + 2 WorldSpace at 0.003 and
//   0.82 scale), so the unified panel is an *entry* layer on top, not a replacement.
// * It never writes back into the legacy Canvas_I toggles. The toggles keep their own persistent
//   onClick calls; the router only reads the resulting state back through DetectMode(). That
//   avoids a feedback loop between the new tab bar and the old toggles.
// * applyOnStart defaults to false: entering Play must not silently deactivate the disassembly or
//   training roots, because the E1-E4 sessions run on the same scene. The tab bar highlights the
//   mode that is currently active instead of forcing one.
using System;
using UnityEngine;

namespace DigitalTwin
{
    public enum TwinDemoMode
    {
        Roaming = 0,
        Training = 1,
        Disassembly = 2,
        Data = 3,
    }

    [Serializable]
    public sealed class TwinDemoModeTargets
    {
        [Tooltip("20_漫游与训练/FunctionModule/Roaming_M")]
        public GameObject roamingRoot;

        [Tooltip("20_漫游与训练/FunctionModule/Dis-Training_M")]
        public GameObject trainingRoot;

        [Tooltip("00_数字孪生机械臂/10_电池包拆解系统/VR拆解")]
        public GameObject disassemblyRoot;

        [Tooltip("30_界面与交互/Canvas/Canvas_1 P, Canvas_1_V")]
        public GameObject[] roamingPanels = new GameObject[0];

        [Tooltip("30_界面与交互/Canvas/Canvas_I, Canvas_I软著用标题")]
        public GameObject[] trainingPanels = new GameObject[0];

        [Tooltip("30_界面与交互/Canvas/User_ui")]
        public GameObject[] disassemblyPanels = new GameObject[0];

        [Tooltip("Panels that belong to the data tab (chart card lives in the shell, so usually empty).")]
        public GameObject[] dataPanels = new GameObject[0];
    }

    [DisallowMultipleComponent]
    public sealed class TwinDemoModeRouter : MonoBehaviour
    {
        [SerializeField] private TwinDemoModeTargets targets = new TwinDemoModeTargets();
        [SerializeField] private TwinDemoMode currentMode = TwinDemoMode.Roaming;
        [SerializeField] private bool applyOnStart;

        public TwinDemoMode CurrentMode => currentMode;
        public TwinDemoModeTargets Targets => targets;

        public event Action<TwinDemoMode> ModeChanged;

        private void Start()
        {
            if (applyOnStart)
            {
                EnterMode(currentMode);
            }
            else
            {
                currentMode = DetectMode();
                ModeChanged?.Invoke(currentMode);
            }
        }

        /// <summary>Apply a mode: activate exactly one teaching mode's objects.</summary>
        public void EnterMode(TwinDemoMode mode)
        {
            SetActiveIfNotNull(targets.roamingRoot, mode == TwinDemoMode.Roaming);
            SetActiveIfNotNull(targets.trainingRoot, mode == TwinDemoMode.Training);
            SetActiveIfNotNull(targets.disassemblyRoot, mode == TwinDemoMode.Disassembly);

            SetGroup(targets.roamingPanels, mode == TwinDemoMode.Roaming);
            SetGroup(targets.trainingPanels, mode == TwinDemoMode.Training);
            SetGroup(targets.disassemblyPanels, mode == TwinDemoMode.Disassembly);
            SetGroup(targets.dataPanels, mode == TwinDemoMode.Data);

            if (currentMode != mode)
            {
                currentMode = mode;
            }

            ModeChanged?.Invoke(mode);
            Debug.Log("[UI Router] mode = " + mode);
        }

        /// <summary>Read the mode that the scene is currently in (used for the initial tab highlight).</summary>
        public TwinDemoMode DetectMode()
        {
            if (targets.disassemblyRoot != null && targets.disassemblyRoot.activeSelf)
            {
                return TwinDemoMode.Disassembly;
            }

            if (targets.trainingRoot != null && targets.trainingRoot.activeSelf)
            {
                return TwinDemoMode.Training;
            }

            if (targets.roamingRoot != null && targets.roamingRoot.activeSelf)
            {
                return TwinDemoMode.Roaming;
            }

            return TwinDemoMode.Roaming;
        }

        public void SetTargets(TwinDemoModeTargets value)
        {
            targets = value ?? new TwinDemoModeTargets();
        }

        public void SetCurrentModeWithoutApplying(TwinDemoMode mode)
        {
            currentMode = mode;
        }

        private static void SetActiveIfNotNull(GameObject target, bool active)
        {
            if (target == null || target.activeSelf == active)
            {
                return;
            }

            try
            {
                target.SetActive(active);
            }
            catch (System.Exception error)
            {
                // A child's Awake can throw (verified case: QuickOutline.Awake on an EVB mesh whose
                // Read/Write is disabled throws out of SetActive). A UI tab switch must not die
                // because one decoration script on the activated root is broken, so the failure is
                // recorded and the switch continues.
                Debug.LogWarning("[UI Router] SetActive(" + active + ") failed on " + target.name + ": "
                                 + error.GetType().Name + ": " + error.Message, target);
            }
        }

        private static void SetGroup(GameObject[] group, bool active)
        {
            if (group == null)
            {
                return;
            }

            foreach (GameObject go in group)
            {
                SetActiveIfNotNull(go, active);
            }
        }
    }
}