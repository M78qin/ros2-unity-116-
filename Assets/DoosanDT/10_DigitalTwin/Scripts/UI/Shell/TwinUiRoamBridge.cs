// TwinUiRoamBridge.cs —— 漫游键鼠完善（P9，仅 V2）
// 做了三件事：
//   ① WASD 不再要求"按住右键"（旧场景 requireRightMouseForMove=1，导致很多人按 WASD 没反应）；视角仍用右键。
//   ② 新增 F = 聚焦当前射线命中的零件；R = 复位视角（复用 rig.RestoreInitialPose）。
//   ③ 把"当前速度 / 位姿 / 键位"暴露给主面板的漫游卡片与任务栏提示。
// 约束：不改相机参数、不改输入仲裁；只在 V2 下生效，Legacy 模式完全不介入。
using DigitalTwin.Interaction;
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiRoamBridge : MonoBehaviour
    {
        public const string Tag = "[ROAM]";

        private TwinPlayCameraRig _rig;
        private TwinPlayRayVisual _ray;
        private int _focusCount;
        private int _resetCount;
        private string _lastAction = "就绪";
        private float _lastActionAt;

        public TwinPlayCameraRig Rig => _rig;
        public int FocusCount => _focusCount;
        public int ResetCount => _resetCount;
        public string LastAction => _lastAction;
        public bool WasdWithoutRightMouse => _rig != null && !_rig.RequireRightMouseForMove;
        public float CurrentSpeed => _rig != null ? _rig.CurrentSpeed : 0f;
        public string PoseLabel => _rig != null
            ? "yaw " + _rig.Yaw.ToString("F0") + "° / pitch " + _rig.Pitch.ToString("F0") + "°"
            : TwinUITextR3.Placeholder;

        public void Configure()
        {
            _rig = Object.FindObjectOfType<TwinPlayCameraRig>();
            _ray = Object.FindObjectOfType<TwinPlayRayVisual>();
            if (_rig != null && !TwinUiShellMode.IsV2)
            {
                return;
            }

            EnableWasdWithoutRightMouse(true);
            Debug.Log(Tag + " ready rig=" + (_rig != null) + " ray=" + (_ray != null)
                + " wasdWithoutRmb=" + WasdWithoutRightMouse);
        }

        /// <summary>WASD 是否需要按住右键；V2 默认 false（按键即走）。</summary>
        public void EnableWasdWithoutRightMouse(bool enabled)
        {
            if (_rig != null)
            {
                _rig.SetRequireRightMouseForMove(!enabled);
            }
        }

        /// <summary>F：把相机拉到当前射线命中的零件前方并对准它。</summary>
        public bool FocusSelection()
        {
            if (_rig == null)
            {
                return false;
            }

            if (_ray == null)
            {
                _ray = Object.FindObjectOfType<TwinPlayRayVisual>();
            }

            if (_ray == null || !_ray.HasWorldHit)
            {
                Note("未命中零件：把鼠标指到零件上再按 F");
                return false;
            }

            RaycastHit hit = _ray.CurrentHit;
            _rig.FocusOnPoint(hit.point);
            _focusCount++;
            Note("已聚焦：" + (hit.collider != null ? hit.collider.name : hit.point.ToString("F2")));
            return true;
        }

        /// <summary>R：复位视角到进入 Play 时的位姿。</summary>
        public bool ResetView()
        {
            if (_rig == null)
            {
                return false;
            }

            _rig.RestoreInitialPose();
            _resetCount++;
            Note("已复位视角");
            return true;
        }

        private void Note(string text)
        {
            _lastAction = text;
            _lastActionAt = Time.unscaledTime;
            Debug.Log(Tag + " " + text);
        }

        private void Update()
        {
            if (!TwinUiShellMode.IsV2 || _rig == null)
            {
                return;
            }

            if (Time.unscaledTime - _lastActionAt > 6f && _lastAction != "就绪")
            {
                _lastAction = "就绪";
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            bool focus = keyboard.fKey.wasPressedThisFrame;
            bool reset = keyboard.rKey.wasPressedThisFrame;
#else
            bool focus = Input.GetKeyDown(KeyCode.F);
            bool reset = Input.GetKeyDown(KeyCode.R);
#endif
            if (focus)
            {
                FocusSelection();
            }

            if (reset)
            {
                ResetView();
            }
        }

        public string Describe()
        {
            return "roam rig=" + (_rig != null)
                + (WasdWithoutRightMouse ? " wasd=no-rmb" : " wasd=rmb")
                + " speed=" + CurrentSpeed.ToString("F2")
                + " pose=" + PoseLabel
                + " focus=" + _focusCount + " reset=" + _resetCount
                + " last=" + _lastAction;
        }
    }
}