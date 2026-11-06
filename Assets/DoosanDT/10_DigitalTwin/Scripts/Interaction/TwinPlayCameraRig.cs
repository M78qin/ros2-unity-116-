// Play 模式桌面漫游相机：右键长按 + WASD 平移（沿相机水平朝向）、Q/E 世界上下、
// Shift 加速 / Ctrl 减速、滚轮实时调基准速度、上下俯仰限位、位置/旋转平滑、右键时锁定鼠标。
//
// 为什么要自己实现：本场景 active 相机（50_玩家与相机/player/Main Camera）上已挂
// SmartRaycaster(controlMode=DesktopMouseKeyboard)，它自带一套 WASD+右键视角逻辑。
// 两套逻辑同时跑会互相打架（同一帧两次改 transform，表现为抖动/漂移），因此本组件在
// Start 时把同物体上的 SmartRaycaster / GameCameraRayController 禁用（可配开关），
// 由本组件独占桌面漫游输入；VR 路径（XR Origin / controlMode=VRController）不受影响。
//
// 可测性：Update() 只负责读输入，真正的位移/旋转全部走 Step(input, dt)，
// 因此 UI/17 自检可以直接调用 StepForTest() 逐帧注入确定性输入并量测位移与 yaw 变化，
// 不必依赖真实鼠标键盘。
using System;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DigitalTwin.Interaction
{
    [DisallowMultipleComponent]
    public sealed class TwinPlayCameraRig : MonoBehaviour
    {
        /// <summary>一帧的输入快照。测试可手工填充后喂给 StepForTest。</summary>
        [Serializable]
        public struct TwinPlayInput
        {
            [Tooltip("水平移动：x=右/A、D 方向，y=前/W、S 方向（相机水平投影）")]
            public Vector2 moveAxis;
            [Tooltip("世界上下：+1 向上（E），-1 向下（Q）")]
            public float verticalAxis;
            [Tooltip("Shift 加速")]
            public bool fastModifier;
            [Tooltip("Ctrl 减速")]
            public bool slowModifier;
            [Tooltip("右键是否按住（视角与移动的前置条件）")]
            public bool rightMouseHeld;
            [Tooltip("中键是否按住（P1b-2：同样申请相机所有权，但不单独触发视角旋转）")]
            public bool middleMouseHeld;
            [Tooltip("本帧鼠标增量（像素）")]
            public Vector2 lookDelta;
            [Tooltip("本帧滚轮格数（+1 一格向上）")]
            public float scrollNotches;
        }

        [Header("目标（留空自动解析）")]
        [Tooltip("被驱动的相机；留空时优先取 Camera.main，再退化为本物体上的 Camera。")]
        [SerializeField] private Camera desktopCamera;
        [Tooltip("平移根节点；留空时直接移动相机自身（本场景即相机自身）。")]
        [SerializeField] private Transform moveRoot;
        [Tooltip("Start 时禁用同物体上的 SmartRaycaster / GameCameraRayController，避免双控。")]
        [SerializeField] private bool disableConflictingControllers = true;
        [Tooltip("P1b-2：通过 TwinUiInputArbiter 申请/让出相机输入所有权（关闭则退回旧行为）。")]
        [SerializeField] private bool respectInputArbiter = true;

        [Header("平移速度")]
        [Tooltip("基准平移速度（米/秒），滚轮可实时增减。")]
        [SerializeField, Range(0.1f, 20f)] private float moveSpeed = 2f;
        [Tooltip("按住 Shift 的加速倍率。")]
        [SerializeField, Range(1f, 10f)] private float fastMultiplier = 4f;
        [Tooltip("按住 Ctrl 的减速倍率。")]
        [SerializeField, Range(0.05f, 1f)] private float slowMultiplier = 0.25f;
        [Tooltip("滚轮一格改变的基准速度（米/秒）。")]
        [SerializeField, Range(0.05f, 2f)] private float scrollSpeedStep = 0.5f;
        [Tooltip("基准速度下限（米/秒）。")]
        [SerializeField, Range(0.05f, 10f)] private float minMoveSpeed = 0.2f;
        [Tooltip("基准速度上限（米/秒）。")]
        [SerializeField, Range(0.5f, 40f)] private float maxMoveSpeed = 20f;
        [Tooltip("必须按住右键才允许 WASD/QE 平移（默认开，接近 Scene 视图手感）。")]
        [SerializeField] private bool requireRightMouseForMove = true;

        [Header("视角")]
        [Tooltip("右键拖拽的鼠标灵敏度（度/像素）。")]
        [SerializeField, Range(0.01f, 1f)] private float lookSensitivity = 0.12f;
        [Tooltip("必须按住右键才允许拖拽转视角。")]
        [SerializeField] private bool requireRightMouseForLook = true;
        [Tooltip("俯仰下限（度，负值为低头）。")]
        [SerializeField, Range(-89f, 0f)] private float pitchMin = -85f;
        [Tooltip("俯仰上限（度，正值为抬头）。")]
        [SerializeField, Range(0f, 89f)] private float pitchMax = 85f;

        [Header("平滑")]
        [Tooltip("位置平滑时间（秒），0 表示不平滑。")]
        [SerializeField, Range(0f, 0.5f)] private float positionSmoothTime = 0.08f;
        [Tooltip("旋转平滑时间（秒），0 表示不平滑。")]
        [SerializeField, Range(0f, 0.5f)] private float rotationSmoothTime = 0.04f;

        [Header("鼠标锁")]
        [Tooltip("右键拖拽期间锁定并隐藏鼠标指针。")]
        [SerializeField] private bool lockCursorWhileLooking = true;

        [Header("运行时状态（只读）")]
        [Tooltip("当前基准速度（米/秒）。")]
        [SerializeField] private float currentSpeed = 2f;
        [Tooltip("当前偏航角（度）。")]
        [SerializeField] private float currentYaw;
        [Tooltip("当前俯仰角（度）。")]
        [SerializeField] private float currentPitch;
        [Tooltip("本组件已禁用掉的冲突组件名（空=没有冲突组件）。")]
        [SerializeField] private string disabledControllers = string.Empty;

        private bool _resolved;
        private Vector3 _targetPosition;
        private Vector3 _smoothVelocity;
        private float _targetYaw;
        private float _targetPitch;
        private float _roll;
        private bool _rightMouseHeld;
        private bool _cursorLockedByUs;
        private bool _loggedResolution;

        // P1b-2 输入仲裁：相机当前是否持有所有权 / 上一帧是否按住（用于 Begin/End 边沿判断）
        private bool _ownsCameraInput;
        private bool _holdPrev;
        private Vector2? _pointerOverride;

        // 初始位姿（用于自检结束时还原现场）
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private bool _initialCaptured;

        public Camera ActiveCamera => desktopCamera;
        public Transform MoveRoot => moveRoot;
        public bool IsResolved => _resolved;
        public bool IsRightMouseHeld => _rightMouseHeld;
        public bool OwnsInputArbiter => _ownsCameraInput;
        public bool RespectInputArbiter => respectInputArbiter;

        /// <summary>自检注入：固定指针屏幕坐标（影响"指针是否在 UI 上"的判定；null=用真实指针）。</summary>
        public void SetPointerOverride(Vector2? screenPosition)
        {
            _pointerOverride = screenPosition;
        }

        public void ClearPointerOverride()
        {
            _pointerOverride = null;
        }

        /// <summary>EventSystem 判定给定屏幕坐标是否在 UI 上（自检用，带少量一次性分配）。</summary>
        public bool IsPointerOverUiAt(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            var data = new PointerEventData(EventSystem.current) { position = screenPosition };
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject != null)
                {
                    return true;
                }
            }

            return false;
        }
        public float CurrentSpeed => currentSpeed;
        public float Yaw => currentYaw;
        public float TargetYaw => _targetYaw;
        public float Pitch => currentPitch;
        public float MoveSpeed => moveSpeed;
        public string DisabledControllers => disabledControllers;
        public bool HasDisabledConflictingController => !string.IsNullOrEmpty(disabledControllers);
        public Vector3 RigPosition => moveRoot != null ? moveRoot.position : transform.position;

        /// <summary>当前输入下的目标平面速度（米/秒），供 HUD/自检参考。</summary>
        public float PlannedSpeed
        {
            get
            {
                float mult = 1f;
                if (InputFast) mult *= fastMultiplier;
                else if (InputSlow) mult *= slowMultiplier;
                return moveSpeed * mult;
            }
        }

        /// <summary>本帧输入是否按住了加速键（仅用于 PlannedSpeed 展示）。</summary>
        public bool InputFast { get; private set; }
        /// <summary>本帧输入是否按住了减速键（仅用于 PlannedSpeed 展示）。</summary>
        public bool InputSlow { get; private set; }

        private void Awake()
        {
            EnsureResolved();
        }

        private void Start()
        {
            EnsureResolved();
            if (_resolved && !_initialCaptured)
            {
                _initialPosition = RigPosition;
                _initialRotation = transform.rotation;
                _initialCaptured = true;
            }

            DisableConflictingControllers();
            if (_resolved)
            {
                Debug.Log("[PLAY-CAM] rig ready camera=" + CameraPath() + " speed=" + moveSpeed.ToString("F2")
                    + " disabled=" + (string.IsNullOrEmpty(disabledControllers) ? "(none)" : disabledControllers));
            }
        }

        private void Update()
        {
            if (!_resolved)
            {
                EnsureResolved();
            }

            if (!_resolved)
            {
                return;
            }

            TwinPlayInput input = ReadInput();
            Step(input, Time.unscaledDeltaTime, true);
        }

        /// <summary>健壮解析：相机 / 移动根 / 初始 yaw-pitch-roll。可重复调用。</summary>
        public void EnsureResolved()
        {
            if (_resolved && desktopCamera != null && moveRoot != null)
            {
                return;
            }

            Camera cam = desktopCamera;
            if (cam == null)
            {
                cam = GetComponent<Camera>();
            }

            if (cam == null)
            {
                cam = Camera.main;
            }

            if (cam == null)
            {
                if (!_loggedResolution)
                {
                    Debug.LogWarning("[PLAY-CAM] no camera resolved (desktopCamera/Camera.main 都为空)");
                    _loggedResolution = true;
                }

                return;
            }

            desktopCamera = cam;
            if (moveRoot == null)
            {
                moveRoot = cam.transform;
            }

            Vector3 euler = cam.transform.eulerAngles;
            currentYaw = _targetYaw = euler.y;
            currentPitch = _targetPitch = NormalizeAngle(euler.x);
            _roll = NormalizeAngle(euler.z);
            _targetPosition = moveRoot.position;
            _smoothVelocity = Vector3.zero;
            _resolved = true;
            _loggedResolution = true;
        }

        /// <summary>编辑器构建器调用：绑定相机与移动根并写入默认速度。</summary>
        public void Configure(Camera camera, Transform root, float speed = 2f)
        {
            desktopCamera = camera;
            moveRoot = root != null ? root : (camera != null ? camera.transform : null);
            moveSpeed = Mathf.Clamp(speed, minMoveSpeed, maxMoveSpeed);
            currentSpeed = moveSpeed;
            _resolved = false;
            EnsureResolved();
            currentSpeed = moveSpeed;
        }

        /// <summary>把相机还原到 Start 时记录的位姿（自检/演示复位用）。</summary>
        public void RestoreInitialPose()
        {
            if (!_initialCaptured || moveRoot == null)
            {
                return;
            }

            moveRoot.position = _initialPosition;
            transform.rotation = _initialRotation;
            Vector3 euler = _initialRotation.eulerAngles;
            currentYaw = _targetYaw = euler.y;
            currentPitch = _targetPitch = NormalizeAngle(euler.x);
            _roll = NormalizeAngle(euler.z);
            _targetPosition = _initialPosition;
            _smoothVelocity = Vector3.zero;
            ApplyRotation();
        }

        /// <summary>测试/自动化入口：直接按确定性输入推进一帧（与 Update 走同一条 Step 通路）。</summary>
        public void StepForTest(TwinPlayInput input, float deltaTime)
        {
            EnsureResolved();
            if (!_resolved)
            {
                return;
            }

            Step(input, Mathf.Max(0.001f, deltaTime), false);
        }

        /// <summary>核心步进：更新视角、位置平滑与当前速度。manageCursor=false 时不动系统鼠标锁。</summary>
        public void Step(TwinPlayInput input, float deltaTime, bool manageCursor)
        {
            if (!_resolved || deltaTime <= 0f)
            {
                return;
            }

            InputFast = input.fastModifier;
            InputSlow = input.slowModifier;
            _rightMouseHeld = input.rightMouseHeld;

            // P1b-2 输入仲裁：
            //   1) 右键/中键按住 → 申请 Camera 所有权；松开 → 释放；
            //   2) 指针在 UI 上时不进入相机所有权（EventSystem 判定优先）；
            //   3) 视角/平移前先问 Allows(Camera)，被 IK/UI/窗口/模态层占用时让路。
            bool hold = input.rightMouseHeld || input.middleMouseHeld;
            bool pointerOverUi = IsPointerOverUiForStep(manageCursor);
            TwinUiInputArbiter arbiter = respectInputArbiter ? TwinUiInputArbiter.Instance : null;

            if (hold && !_holdPrev)
            {
                _ownsCameraInput = !pointerOverUi && arbiter != null && arbiter.Begin(TwinUiInputOwner.Camera);
            }
            else if (!hold && _holdPrev)
            {
                if (_ownsCameraInput && arbiter != null)
                {
                    arbiter.End(TwinUiInputOwner.Camera);
                }

                _ownsCameraInput = false;
            }

            _holdPrev = hold;

            bool inputAllowed = !pointerOverUi
                && (arbiter == null || arbiter.Allows(TwinUiInputOwner.Camera));

            bool looking = inputAllowed && (!requireRightMouseForLook || input.rightMouseHeld);
            if (looking && input.lookDelta.sqrMagnitude > 0f)
            {
                _targetYaw += input.lookDelta.x * lookSensitivity;
                _targetPitch = Mathf.Clamp(_targetPitch - input.lookDelta.y * lookSensitivity, pitchMin, pitchMax);
            }

            if (input.scrollNotches != 0f)
            {
                moveSpeed = Mathf.Clamp(moveSpeed + input.scrollNotches * scrollSpeedStep, minMoveSpeed, maxMoveSpeed);
                currentSpeed = moveSpeed;
            }

            if (manageCursor)
            {
                UpdateCursor(input.rightMouseHeld);
            }

            // 旋转平滑
            if (rotationSmoothTime <= 0.0001f)
            {
                currentYaw = _targetYaw;
                currentPitch = _targetPitch;
            }
            else
            {
                float k = 1f - Mathf.Exp(-deltaTime / rotationSmoothTime);
                currentYaw = Mathf.LerpAngle(currentYaw, _targetYaw, k);
                currentPitch = Mathf.Lerp(currentPitch, _targetPitch, k);
            }

            ApplyRotation();

            // 平移：WASD 沿相机水平投影，Q/E 沿世界上下
            bool canMove = inputAllowed && (!requireRightMouseForMove || input.rightMouseHeld);
            if (canMove)
            {
                float mult = 1f;
                if (input.fastModifier)
                {
                    mult *= fastMultiplier;
                }
                else if (input.slowModifier)
                {
                    mult *= slowMultiplier;
                }

                Vector2 axis = input.moveAxis;
                if (axis.sqrMagnitude > 1f)
                {
                    axis.Normalize();
                }

                if (axis.sqrMagnitude > 0f || Mathf.Abs(input.verticalAxis) > 0f)
                {
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f)
                    {
                        forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
                    }

                    forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 velocity = (forward * axis.y + right * axis.x) * (moveSpeed * mult)
                        + Vector3.up * (input.verticalAxis * moveSpeed * mult);
                    _targetPosition += velocity * deltaTime;
                }
            }

            if (moveRoot != null)
            {
                if (positionSmoothTime <= 0.0001f)
                {
                    moveRoot.position = _targetPosition;
                }
                else
                {
                    moveRoot.position = Vector3.SmoothDamp(moveRoot.position, _targetPosition, ref _smoothVelocity,
                        positionSmoothTime, Mathf.Infinity, deltaTime);
                }
            }

            currentSpeed = moveSpeed;
        }

        private void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, _roll);
        }

        /// <summary>本帧指针是否在 UI 上：注入坐标优先；真实 Update 用 EventSystem 指针状态；测试注入路径默认不拦截。</summary>
        private bool IsPointerOverUiForStep(bool manageCursor)
        {
            if (_pointerOverride.HasValue)
            {
                return IsPointerOverUiAt(_pointerOverride.Value);
            }

            if (!manageCursor)
            {
                return false;
            }

            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void UpdateCursor(bool rightHeld)
        {
            if (!lockCursorWhileLooking)
            {
                return;
            }

            if (rightHeld && !_cursorLockedByUs)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _cursorLockedByUs = true;
            }
            else if (!rightHeld && _cursorLockedByUs)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorLockedByUs = false;
            }
        }

        private void OnDisable()
        {
            if (_ownsCameraInput)
            {
                TwinUiInputArbiter arbiter = TwinUiInputArbiter.Instance;
                if (arbiter != null)
                {
                    arbiter.End(TwinUiInputOwner.Camera);
                }

                _ownsCameraInput = false;
            }

            _holdPrev = false;

            if (_cursorLockedByUs)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorLockedByUs = false;
            }
        }

        private void DisableConflictingControllers()
        {
            if (!disableConflictingControllers)
            {
                return;
            }

            disabledControllers = string.Empty;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this)
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (typeName != "SmartRaycaster" && typeName != "GameCameraRayController")
                {
                    continue;
                }

                if (!behaviour.enabled)
                {
                    continue;
                }

                behaviour.enabled = false;
                disabledControllers += (disabledControllers.Length == 0 ? string.Empty : ",") + typeName;
                Debug.Log("[PLAY-CAM] disabled conflicting controller: " + typeName + " on " + CameraPath());
            }
        }

        private string CameraPath()
        {
            Transform current = (desktopCamera != null ? desktopCamera.transform : transform);
            var parts = new System.Collections.Generic.List<string>();
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }

            if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        private TwinPlayInput ReadInput()
        {
            var input = new TwinPlayInput();
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed)
                {
                    input.moveAxis.y += 1f;
                }

                if (keyboard.sKey.isPressed)
                {
                    input.moveAxis.y -= 1f;
                }

                if (keyboard.dKey.isPressed)
                {
                    input.moveAxis.x += 1f;
                }

                if (keyboard.aKey.isPressed)
                {
                    input.moveAxis.x -= 1f;
                }

                if (keyboard.eKey.isPressed)
                {
                    input.verticalAxis += 1f;
                }

                if (keyboard.qKey.isPressed)
                {
                    input.verticalAxis -= 1f;
                }

                input.fastModifier = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                input.slowModifier = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                input.rightMouseHeld = mouse.rightButton.isPressed;
                input.middleMouseHeld = mouse.middleButton.isPressed;
                input.lookDelta = mouse.delta.ReadValue();
                input.scrollNotches = mouse.scroll.ReadValue().y / 120f;
            }
#else
            if (Input.GetKey(KeyCode.W)) input.moveAxis.y += 1f;
            if (Input.GetKey(KeyCode.S)) input.moveAxis.y -= 1f;
            if (Input.GetKey(KeyCode.D)) input.moveAxis.x += 1f;
            if (Input.GetKey(KeyCode.A)) input.moveAxis.x -= 1f;
            if (Input.GetKey(KeyCode.E)) input.verticalAxis += 1f;
            if (Input.GetKey(KeyCode.Q)) input.verticalAxis -= 1f;
            input.fastModifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            input.slowModifier = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            input.rightMouseHeld = Input.GetMouseButton(1);
            input.middleMouseHeld = Input.GetMouseButton(2);
            input.lookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            input.scrollNotches = Input.mouseScrollDelta.y;
#endif
            return input;
        }
    }
}