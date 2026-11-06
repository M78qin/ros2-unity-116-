// Play 模式末端法兰（TCP）拖动 IK 手柄 —— A2 级别 1：本地 ghost 预览，不下发。
//
// 组成：
//   - 手柄：TCP 处的球体 + 三轴短线 + TCP→目标引导线（颜色/大小/显隐可 Inspector 调，默认 R3 青/琥珀）；
//   - 拖动：左键长按命中手柄后沿相机平面拖动；X/Y/Z（或 1/2/3）锁定单轴；Shift 精调 0.2×；
//     Esc 取消并回到 live feedback；R 或 HUD 按钮「回当前」复位到当前 TCP；
//   - ghost 预览：SetTargetPose -> RobotIkController.TrySolve(applyPreview:true) -> TwinCommandController.UpdatePlanTarget；
//   - HUD：目标位姿 / 当前误差 mm / deg / 迭代次数 / IK 状态 / dispatch 状态；
//   - 安全护栏：单帧位移上限（默认 5 mm）、工作空间 AABB（默认围绕初始 TCP ±0.5 m）、
//     关节限位（复用 RobotModelController.ClampDisplayDeg）、不可达时手柄变红 + HUD 提示；
//   - 硬开关 allowRobotDispatch=false（默认）：不调用 EnterExecute、不发起任何可能下发的命令。
//     allowRobotDispatch=false 时连 EnterPlan（会发 SET_MODE mode2_ctrl）也跳过，只做本地 ghost 预览；
//     UpdatePlanTarget 只是本地目标缓存（PlanningControlService._targetJointRad），不产生网络下发。
//
// 本组件只做运行期（Play）显示与交互；不修改数据链脚本、不修改 TwinRuntimeProfile。
// 测试入口：BeginDragForTest / DragToForTest / DragDirectForTest / EndDragForTest /
//           ResetToCurrentForTest / CancelForTest / SetAxisLockForTest / SetWorkspaceHalfExtentsForTest。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DigitalTwin.Interaction
{
    [DisallowMultipleComponent]
    public sealed class TwinPlayIkHandle : MonoBehaviour
    {
        private const string LogTag = "[IK]";

        [Header("总开关")]
        [Tooltip("Play 模式 IK 手柄总开关；关闭后不绘制、不响应拖动、不求解。")]
        [SerializeField] private bool enableHandle = true;
        [Tooltip("是否绘制 TCP 球体与三轴线。")]
        [SerializeField] private bool showHandleVisual = true;
        [Tooltip("是否显示左上角 HUD（目标位姿/误差/状态/dispatch）。")]
        [SerializeField] private bool showHud = true;

        [Header("引用（Builder 自动解析，可手动指定）")]
        [Tooltip("桌面玩家相机（Camera.main 兜底）。")]
        [SerializeField] private Camera playCamera;
        [Tooltip("场景中的 RobotIkController（base_link 上）。")]
        [SerializeField] private RobotIkController ikController;
        [Tooltip("场景中的 TwinCommandController（ghost 目标缓存）。")]
        [SerializeField] private TwinCommandController commandController;

        [Header("手柄外观（R3 主题：青 / 琥珀）")]
        [Tooltip("空闲色（默认青）。")]
        [SerializeField] private Color idleColor = new Color(0.13f, 0.85f, 0.94f, 1f);
        [Tooltip("鼠标悬停色（更亮青）。")]
        [SerializeField] private Color hoverColor = new Color(0.55f, 0.95f, 1f, 1f);
        [Tooltip("拖动中色（默认琥珀）。")]
        [SerializeField] private Color dragColor = new Color(0.96f, 0.65f, 0.14f, 1f);
        [Tooltip("不可达 / 关节越界色（红）。")]
        [SerializeField] private Color unreachableColor = new Color(1f, 0.22f, 0.2f, 1f);
        [Tooltip("TCP→目标引导线颜色。")]
        [SerializeField] private Color guideLineColor = new Color(0.96f, 0.65f, 0.14f, 0.65f);
        [Tooltip("X 轴短线颜色。")]
        [SerializeField] private Color axisXColor = new Color(0.96f, 0.65f, 0.14f, 1f);
        [Tooltip("Y 轴短线颜色。")]
        [SerializeField] private Color axisYColor = new Color(0.13f, 0.77f, 0.37f, 1f);
        [Tooltip("Z 轴短线颜色。")]
        [SerializeField] private Color axisZColor = new Color(0.13f, 0.85f, 0.94f, 1f);
        [Tooltip("球体半径（米）。")]
        [SerializeField, Range(0.01f, 0.5f)] private float handleRadius = 0.05f;
        [Tooltip("三轴短线长度（米）。")]
        [SerializeField, Range(0.02f, 1f)] private float axisLength = 0.16f;
        [Tooltip("三轴短线线宽（米）。")]
        [SerializeField, Range(0.001f, 0.05f)] private float axisLineWidth = 0.009f;
        [Tooltip("手柄是否始终绘制在最前（关闭深度测试），便于被机器人遮挡时仍可见。")]
        [SerializeField] private bool alwaysOnTop = true;
        [Tooltip("是否绘制三轴短线。")]
        [SerializeField] private bool showAxisLines = true;

        [Header("拖动 / 安全护栏")]
        [Tooltip("单帧（单次求解步）最大位移，单位 m。默认 5 mm。")]
        [SerializeField, Range(0.0005f, 0.05f)] private float maxStepMeters = 0.005f;
        [Tooltip("按住 Shift 的精调倍率（默认 0.2×）。")]
        [SerializeField, Range(0.05f, 1f)] private float fineScale = 0.2f;
        [Tooltip("工作空间 AABB 半尺寸（围绕初始 TCP，默认 ±0.5 m）。")]
        [SerializeField] private Vector3 workspaceHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);
        [Tooltip("拖动时求解频率上限（Hz）；拖动以外的整定/测试走立即求解。")]
        [SerializeField, Range(1f, 60f)] private float solveRateHz = 20f;
        [Tooltip("位置误差超过该值即判定不可达（手柄变红 + HUD 提示），单位 m。")]
        [SerializeField, Range(0.002f, 0.2f)] private float unreachableErrorMeters = 0.01f;
        [Tooltip("手柄拾取射线使用的 LayerMask（默认全部层）。")]
        [SerializeField] private LayerMask handleHitLayers = ~0;

        [Header("下发硬开关（默认关闭）")]
        [Tooltip("false = 只做本地 ghost 预览：任何路径都不调用 EnterExecute / 不发起 MOVE_JOINT；" +
                 "同时跳过 EnterPlan（会发 SET_MODE mode2_ctrl）。A2 级别 1 保持 false。")]
        [SerializeField] private bool allowRobotDispatch = false;

        // ---- 运行期状态 ----
        private bool _initialized;
        private bool _initWarned;
        private float _nextInitRetryTime;
        private bool _hasPendingTarget;
        private bool _dragging;
        private bool _testDragActive;

        // P1b-2：拖动期间通过 TwinUiInputArbiter 独占输入（Camera 等低优先级消费者让路）
        private bool _ownsInputArbiter;
        private bool _hovered;
        private int _axisLock = -1; // -1=none, 0=X, 1=Y, 2=Z

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Vector3 _initialTcpPosition;
        private Quaternion _initialTcpRotation = Quaternion.identity;
        private Vector3 _workspaceCenter;

        private Vector3 _dragStartTargetPosition;
        private Quaternion _dragStartTargetRotation = Quaternion.identity;
        private Vector3 _dragPlaneNormal = Vector3.forward;
        private Vector3 _dragPlanePoint;
        private Vector3 _grabOffset;
        private float _nextSolveTime;

        private float _lastStepMeters;
        private bool _workspaceClamped;
        private bool _solveDirty;
        private bool _reachable = true;
        private bool _jointLimitsOk = true;
        private bool _statusWarningLogged;
        private bool _planSkipLogged;

        private float[] _lastSolution = Array.Empty<float>();
        private bool _hasSolution;
        private string _statusText = "Idle";

        // ---- 下发/plan 计数（自检证据） ----
        private int _enterExecuteCalls;
        private int _planEnterAttempts;
        private int _planUpdates;
        private int _cancelPlanCalls;
        private int _solveCalls;
        private int _rejectedJointLimit;
        private int _unreachableEvents;

        // ---- 指针/按键 ----
        private bool _leftHeldPrev;
        private Vector2 _pointerPosition;
        private bool _leftHeld;

        // ---- 视觉对象 ----
        private bool _visualsCreated;
        private Transform _handleRoot;
        private GameObject _sphereGo;
        private MeshRenderer _sphereRenderer;
        private Material _sphereMaterial;
        private LineRenderer[] _axisLines;
        private LineRenderer _guideLine;
        private Material _lineMaterial;
        private Transform _axisRoot;

        // ---- HUD ----
        private bool _hudCreated;
        private GameObject _hudCanvasRoot;
        private Canvas _hudCanvas;
        private TMP_Text _hudText;
        private Image _hudPanel;
        private Button _resetButton;
        private Button _cancelButton;

        // ================================================================== 属性（自检/外部读取）

        public bool EnableHandle => enableHandle;
        public bool AllowRobotDispatch => allowRobotDispatch;
        public bool Initialized => _initialized;
        public bool Dragging => _dragging;
        public bool OwnsInputArbiter => _ownsInputArbiter;
        public bool HasPendingTarget => _hasPendingTarget;
        public int AxisLock => _axisLock;
        public bool Reachable => _reachable;
        public bool JointLimitsOk => _jointLimitsOk;
        public bool WorkspaceClamped => _workspaceClamped;
        public float LastStepMeters => _lastStepMeters;
        public Vector3 InitialTcpPosition => _initialTcpPosition;
        public Vector3 TargetPosition => _targetPosition;
        public Quaternion TargetRotation => _targetRotation;
        public bool HandleVisualVisible => showHandleVisual && _visualsCreated && _sphereGo != null && _sphereGo.activeSelf;
        public bool HandleSphereVisibleToCamera => _sphereRenderer != null && _sphereRenderer.isVisible;
        public bool HandleSphereExists => _sphereRenderer != null;
        public GameObject HandleSphereObject => _sphereGo;
        public Camera HandleCamera => playCamera;
        public Vector3 HandleWorldPosition => _sphereGo != null ? _sphereGo.transform.position : _targetPosition;
        public Color CurrentHandleColor { get; private set; } = new Color(0.13f, 0.85f, 0.94f, 1f);
        public Color IdleColorValue => idleColor;
        public Color DragColorValue => dragColor;
        public Color UnreachableColorValue => unreachableColor;
        public string IkStatusText => _statusText;
        public float PositionErrorMeters { get; private set; }
        public float RotationErrorDeg { get; private set; }
        public int Iterations { get; private set; }
        public bool HasSolution => _hasSolution;
        public string HudTextValue => _hudText == null ? string.Empty : _hudText.text;
        public bool HudCreated => _hudCreated;
        public GameObject HudCanvasRoot => _hudCanvasRoot;
        public int EnterExecuteCalls => _enterExecuteCalls;
        public int PlanEnterAttempts => _planEnterAttempts;
        public int PlanUpdates => _planUpdates;
        public int CancelPlanCalls => _cancelPlanCalls;
        public int SolveCalls => _solveCalls;
        public int RejectedJointLimitCount => _rejectedJointLimit;
        public int UnreachableCount => _unreachableEvents;

        /// <summary>所有可能下发到真实/仿真机器人的 TwinCommandController 调用计数（自检断言用）。</summary>
        public int DispatchCalls => _enterExecuteCalls + _planEnterAttempts + _cancelPlanCalls;

        public float[] LastSolutionDegreesCopy()
        {
            float[] copy = new float[_lastSolution.Length];
            Array.Copy(_lastSolution, copy, copy.Length);
            return copy;
        }

        public float[] CurrentJointDegreesCopy()
        {
            RobotModelController model = ikController != null ? ikController.RobotModel : null;
            return model == null ? Array.Empty<float>() : model.ReadCurrentDisplayDegrees();
        }

        public Vector3 HandleScreenPosition
        {
            get
            {
                if (playCamera == null)
                {
                    return Vector3.zero;
                }

                return playCamera.WorldToScreenPoint(HandleWorldPosition);
            }
        }

        public bool TryGetCurrentTcpPose(out Vector3 position, out Quaternion rotation)
        {
            if (ikController != null && ikController.TryGetCurrentTcpPose(out position, out rotation))
            {
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        // ================================================================== 生命周期

        /// <summary>Builder 用：写入相机/IK/命令引用（Edit 模式调用后由 Builder 保存场景）。</summary>
        public void ConfigureForEditor(Camera camera, RobotIkController ik, TwinCommandController command)
        {
            playCamera = camera;
            ikController = ik;
            commandController = command;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            TryInitialize();
        }

        private void OnDestroy()
        {
            ReleaseInputOwnership();

            if (_hudCanvas != null)
            {
                Destroy(_hudCanvas.gameObject);
            }

            if (_handleRoot != null)
            {
                Destroy(_handleRoot.gameObject);
            }

            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }
        }

        /// <summary>拖动开始：独占输入（优先级 IkHandle 高于 Camera/SceneRay）。</summary>
        private void AcquireInputOwnership()
        {
            if (_ownsInputArbiter)
            {
                return;
            }

            _ownsInputArbiter = TwinUiInput.Begin(TwinUiInputOwner.IkHandle);
        }

        /// <summary>拖动结束/取消/复位：释放输入所有权（重复调用安全）。</summary>
        private void ReleaseInputOwnership()
        {
            if (!_ownsInputArbiter)
            {
                return;
            }

            TwinUiInput.End(TwinUiInputOwner.IkHandle);
            _ownsInputArbiter = false;
        }

        private void ResolveReferences()
        {
            if (playCamera == null)
            {
                playCamera = Camera.main;
            }

            if (ikController == null)
            {
                ikController = FindObjectOfType<RobotIkController>();
            }

            if (commandController == null)
            {
                commandController = FindObjectOfType<TwinCommandController>();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !enableHandle)
            {
                return;
            }

            if (!_initialized)
            {
                if (Time.unscaledTime >= _nextInitRetryTime)
                {
                    _nextInitRetryTime = Time.unscaledTime + 0.5f;
                    TryInitialize();
                }

                if (!_initialized)
                {
                    return;
                }
            }

            ReadPointerAndKeys(out Vector2 pointer, out bool leftDown, out bool leftHeld,
                out bool shiftHeld, out bool escapePressed, out bool resetPressed, out int axisKey);

            _pointerPosition = pointer;
            _leftHeld = leftHeld;

            if (!_testDragActive)
            {
                HandleHotkeys(pointer, leftDown, leftHeld, shiftHeld, escapePressed, resetPressed, axisKey);
            }

            if (!_dragging && !_hasPendingTarget)
            {
                TrackIdleTarget();
            }

            _leftHeldPrev = leftHeld;

            UpdateVisuals();
            UpdateHud();
        }

        // ================================================================== 初始化

        private bool TryInitialize()
        {
            ResolveReferences();
            if (!enableHandle)
            {
                return false;
            }

            if (playCamera == null || ikController == null)
            {
                if (!_initWarned)
                {
                    _initWarned = true;
                    Debug.LogWarning(LogTag + " init pending: camera=" + (playCamera != null)
                        + " ikController=" + (ikController != null) + " (will retry)");
                }

                return false;
            }

            if (!ikController.TryGetCurrentTcpPose(out Vector3 tcpPos, out Quaternion tcpRot))
            {
                return false;
            }

            if (!IsFinite(tcpPos))
            {
                return false;
            }

            EnsureVisuals();
            EnsureHud();

            _targetPosition = tcpPos;
            _targetRotation = tcpRot;
            _initialTcpPosition = tcpPos;
            _initialTcpRotation = tcpRot;
            _workspaceCenter = tcpPos;
            ikController.SetTargetPose(tcpPos, tcpRot);
            ikController.ResetTargetToTcp();

            _initialized = true;
            _hasPendingTarget = false;
            _reachable = true;
            _statusText = ikController.LastStatus.ToString();

            RobotModelController model = ikController.RobotModel;
            string tcpLabel = ikController.GetEffectiveTcpLabel();
            Debug.Log(LogTag + " initialized: camera=" + playCamera.name
                + " ik=" + (model != null ? model.gameObject.name : "<null>")
                + " tcp=" + tcpLabel
                + " tcpPos=" + FormatVector(tcpPos)
                + " joints=" + (model != null ? model.JointCount : 0)
                + " allowRobotDispatch=" + allowRobotDispatch
                + " dispatch=" + (allowRobotDispatch ? "armed" : "disabled (local preview only)")
                + " maxStep=" + maxStepMeters.ToString("F4") + "m"
                + " workspace=±" + FormatVector(workspaceHalfExtents) + "m");

            UpdateVisuals();
            UpdateHud();
            return true;
        }

        private void TrackIdleTarget()
        {
            if (!ikController.TryGetCurrentTcpPose(out Vector3 pos, out Quaternion rot))
            {
                return;
            }

            if (Vector3.Distance(pos, _targetPosition) > 0.0000001f || Quaternion.Angle(rot, _targetRotation) > 0.001f)
            {
                _targetPosition = pos;
                _targetRotation = rot;
                _workspaceCenter = pos;
                ikController.SetTargetPose(pos, rot);
                ResetTargetStatusToIdle();
            }
        }

        /// <summary>没有活动目标时把可达/状态复位（避免上一次不可达/超时残留在 HUD 与手柄颜色上）。</summary>
        private void ResetTargetStatusToIdle()
        {
            _reachable = true;
            _jointLimitsOk = true;
            _statusWarningLogged = false;
            _statusText = "Idle";
        }

        // ================================================================== 输入

        private void ReadPointerAndKeys(out Vector2 pointer, out bool leftDown, out bool leftHeld,
            out bool shiftHeld, out bool escapePressed, out bool resetPressed, out int axisKey)
        {
            pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            leftDown = false;
            leftHeld = false;
            shiftHeld = false;
            escapePressed = false;
            resetPressed = false;
            axisKey = -3;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                pointer = mouse.position.ReadValue();
                leftDown = mouse.leftButton.wasPressedThisFrame;
                leftHeld = mouse.leftButton.isPressed;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                shiftHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                escapePressed = keyboard.escapeKey.wasPressedThisFrame;
                resetPressed = keyboard.rKey.wasPressedThisFrame;
                if (keyboard.xKey.wasPressedThisFrame || keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                {
                    axisKey = 0;
                }
                else if (keyboard.yKey.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                {
                    axisKey = 1;
                }
                else if (keyboard.zKey.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                {
                    axisKey = 2;
                }
            }
#else
            pointer = Input.mousePosition;
            leftDown = Input.GetMouseButtonDown(0);
            leftHeld = Input.GetMouseButton(0);
            shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            escapePressed = Input.GetKeyDown(KeyCode.Escape);
            resetPressed = Input.GetKeyDown(KeyCode.R);
            if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Alpha1)) axisKey = 0;
            else if (Input.GetKeyDown(KeyCode.Y) || Input.GetKeyDown(KeyCode.Alpha2)) axisKey = 1;
            else if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Alpha3)) axisKey = 2;
#endif
        }

        private void HandleHotkeys(Vector2 pointer, bool leftDown, bool leftHeld, bool shiftHeld,
            bool escapePressed, bool resetPressed, int axisKey)
        {
            if (escapePressed)
            {
                CancelAndReset("Esc");
                return;
            }

            if (resetPressed)
            {
                ResetToCurrent("R");
                return;
            }

            if (axisKey >= 0)
            {
                _axisLock = _axisLock == axisKey ? -1 : axisKey;
                Debug.Log(LogTag + " axis lock -> " + AxisLockName(_axisLock));
                return;
            }

            if (_dragging)
            {
                if (!leftHeld)
                {
                    EndDragInternal("left button released");
                    return;
                }

                if (TryProjectPointerToPlane(pointer, out Vector3 planePoint))
                {
                    Vector3 desired = ApplyAxisLock(planePoint + _grabOffset);
                    ApplyTargetStep(desired, shiftHeld, false, false);
                }

                return;
            }

            _hovered = leftHeld || (playCamera != null && IsPointerOnHandle(pointer));
            if (leftDown && !_leftHeldPrev && !IsPointerOverUi(pointer) && IsPointerOnHandle(pointer))
            {
                BeginDragFromPointer(pointer);
            }
        }

        private bool BeginDragFromPointer(Vector2 pointer)
        {
            _dragPlaneNormal = -playCamera.transform.forward;
            _dragPlanePoint = _targetPosition;
            if (!TryProjectPointerToPlane(pointer, out Vector3 grabPoint))
            {
                Debug.Log(LogTag + " BeginDrag aborted: pointer ray misses drag plane");
                return false;
            }

            _grabOffset = _targetPosition - grabPoint;
            _dragStartTargetPosition = _targetPosition;
            _dragStartTargetRotation = _targetRotation;
            _axisLock = -1;
            _dragging = true;
            _testDragActive = false;
            _hasPendingTarget = true;
            _planSkipLogged = false;
            _statusWarningLogged = false;
            AcquireInputOwnership();
            EnsurePlanModeForPreview();
            Debug.Log(LogTag + " drag begin: target=" + FormatVector(_targetPosition)
                + " planeNormal=" + FormatVector(_dragPlaneNormal));
            return true;
        }

        private void BeginDragInternalForTest()
        {
            _dragPlaneNormal = playCamera != null ? -playCamera.transform.forward : Vector3.forward;
            _dragPlanePoint = _targetPosition;
            _dragStartTargetPosition = _targetPosition;
            _dragStartTargetRotation = _targetRotation;
            _grabOffset = Vector3.zero;
            _axisLock = -1;
            _dragging = true;
            _testDragActive = true;
            _hasPendingTarget = true;
            _planSkipLogged = false;
            _statusWarningLogged = false;
            AcquireInputOwnership();
            EnsurePlanModeForPreview();
        }

        private bool TryProjectPointerToPlane(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = _targetPosition;
            if (playCamera == null)
            {
                return false;
            }

            Ray ray = playCamera.ScreenPointToRay(screenPoint);
            Plane plane = new Plane(_dragPlaneNormal, _dragPlanePoint);
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        private Vector3 ApplyAxisLock(Vector3 desired)
        {
            if (_axisLock < 0)
            {
                return desired;
            }

            Vector3 axis = _axisLock == 0 ? Vector3.right : (_axisLock == 1 ? Vector3.up : Vector3.forward);
            Vector3 delta = desired - _dragStartTargetPosition;
            return _dragStartTargetPosition + axis * Vector3.Dot(delta, axis);
        }

        // ================================================================== 拖动 / ghost 预览

        private void ApplyTargetStep(Vector3 desired, bool fine, bool bypassStepLimit, bool immediateSolve)
        {
            Vector3 previous = _targetPosition;
            Vector3 delta = desired - previous;
            float limit = maxStepMeters * (fine ? fineScale : 1f);
            if (bypassStepLimit || limit <= 0f)
            {
                limit = float.MaxValue;
            }

            float distance = delta.magnitude;
            if (distance > limit)
            {
                delta *= limit / distance;
            }

            Vector3 next = previous + delta;
            _workspaceClamped = false;
            Vector3 min = _workspaceCenter - workspaceHalfExtents;
            Vector3 max = _workspaceCenter + workspaceHalfExtents;
            Vector3 clamped = new Vector3(
                Mathf.Clamp(next.x, min.x, max.x),
                Mathf.Clamp(next.y, min.y, max.y),
                Mathf.Clamp(next.z, min.z, max.z));
            if ((clamped - next).sqrMagnitude > 1e-12f)
            {
                _workspaceClamped = true;
            }

            next = clamped;
            _lastStepMeters = Vector3.Distance(previous, next);
            _targetPosition = next;
            _hasPendingTarget = true;

            if (immediateSolve || Time.unscaledTime >= _nextSolveTime)
            {
                _nextSolveTime = Time.unscaledTime + 1f / Mathf.Max(1f, solveRateHz);
                _solveDirty = false;
                SolveNow();
            }
            else
            {
                // 交互拖动按 solveRateHz 限频；释放鼠标时补一次求解，保证 ghost 与目标一致。
                _solveDirty = true;
            }
        }

        private void SolveNow()
        {
            if (ikController == null)
            {
                _reachable = false;
                _statusText = "MissingRobotIkController";
                return;
            }

            _solveCalls++;
            try
            {
                ikController.SetTargetPose(_targetPosition, _targetRotation);
                ikController.TrySolve(true);
            }
            catch (Exception e)
            {
                _reachable = false;
                _statusText = "SolveException: " + e.GetType().Name;
                Debug.LogWarning(LogTag + " TrySolve exception: " + e.GetType().Name + ": " + e.Message);
                return;
            }

            _statusText = ikController.LastStatus.ToString();
            PositionErrorMeters = ikController.LastPositionError;
            RotationErrorDeg = ikController.LastRotationErrorDeg;
            Iterations = ikController.LastIterations;
            _lastSolution = ikController.GetLastSolutionDegreesCopy();
            _hasSolution = ikController.HasLastSolution && _lastSolution.Length > 0;
            _jointLimitsOk = CheckJointLimits(_lastSolution);

            if (!_jointLimitsOk)
            {
                _rejectedJointLimit++;
                _reachable = false;
                _statusText = "JointLimitRejected (" + _statusText + ")";
                if (!_statusWarningLogged)
                {
                    _statusWarningLogged = true;
                    Debug.LogWarning(LogTag + " reject target: solution exceeds joint limits");
                }
            }
            else if (_hasSolution)
            {
                // ghost 目标缓存：PlanningControlService 内部数组，不发网络命令。
                if (commandController != null)
                {
                    try
                    {
                        commandController.UpdatePlanTarget(ToRobotRadians(_lastSolution));
                        _planUpdates++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(LogTag + " UpdatePlanTarget exception: " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
            else
            {
                _reachable = false;
                _statusText = "NoSolution (" + _statusText + ")";
            }

            if (_reachable && (float.IsNaN(PositionErrorMeters) || float.IsInfinity(PositionErrorMeters)
                || PositionErrorMeters > unreachableErrorMeters))
            {
                _reachable = false;
            }

            if (!_reachable)
            {
                _unreachableEvents++;
                if (!_statusWarningLogged)
                {
                    _statusWarningLogged = true;
                    Debug.LogWarning(LogTag + " target unreachable: status=" + _statusText
                        + " posErr=" + (PositionErrorMeters * 1000f).ToString("F2") + "mm"
                        + " iter=" + Iterations);
                }
            }
            else
            {
                _statusWarningLogged = false;
            }
        }

        private bool CheckJointLimits(float[] solutionDeg)
        {
            RobotModelController model = ikController != null ? ikController.RobotModel : null;
            if (model == null || solutionDeg == null || solutionDeg.Length == 0)
            {
                return false;
            }

            int count = Mathf.Min(solutionDeg.Length, model.JointCount);
            for (int i = 0; i < count; i++)
            {
                Vector2 limits = model.GetJointLimitsDeg(i);
                if (solutionDeg[i] < limits.x - 0.01f || solutionDeg[i] > limits.y + 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        private float[] ToRobotRadians(float[] displayDeg)
        {
            RobotModelController model = ikController != null ? ikController.RobotModel : null;
            if (displayDeg == null)
            {
                return Array.Empty<float>();
            }

            float[] rad = new float[displayDeg.Length];
            for (int i = 0; i < displayDeg.Length; i++)
            {
                rad[i] = model != null ? model.ConvertDisplayDegToRobotRad(i, displayDeg[i]) : displayDeg[i] * Mathf.Deg2Rad;
            }

            return rad;
        }

        private void EnsurePlanModeForPreview()
        {
            if (commandController == null)
            {
                return;
            }

            if (commandController.CurrentMode == TwinMode.Plan)
            {
                return;
            }

            if (!allowRobotDispatch)
            {
                if (!_planSkipLogged)
                {
                    _planSkipLogged = true;
                    Debug.Log(LogTag + " local preview only: allowRobotDispatch=false"
                        + " -> skip EnterPlan (would send SET_MODE mode2_ctrl); ghost target cache still updated");
                }

                return;
            }

            _planEnterAttempts++;
            bool entered;
            try
            {
                entered = commandController.EnterPlan();
            }
            catch (Exception e)
            {
                entered = false;
                Debug.LogWarning(LogTag + " EnterPlan exception: " + e.GetType().Name + ": " + e.Message);
            }

            if (!entered)
            {
                Debug.LogWarning(LogTag + " EnterPlan failed (status=" + commandController.LastPlanningStatus
                    + " error=" + commandController.LastPlanningError + ") -> local preview only");
            }
        }

        private void EndDragInternal(string reason)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            _testDragActive = false;
            ReleaseInputOwnership();
            if (_solveDirty)
            {
                _solveDirty = false;
                SolveNow();
            }

            Debug.Log(LogTag + " drag end (" + reason + "): target=" + FormatVector(_targetPosition)
                + " posErr=" + (PositionErrorMeters * 1000f).ToString("F2") + "mm"
                + " iter=" + Iterations + " status=" + _statusText);
            UpdateVisuals();
            UpdateHud();
        }

        private void CancelAndReset(string source)
        {
            _dragging = false;
            _testDragActive = false;
            _hasPendingTarget = false;
            _axisLock = -1;
            ReleaseInputOwnership();

            if (ikController != null)
            {
                ikController.ResetTargetToTcp();
                ikController.ReleaseBackToLiveFeedback();
            }

            bool shouldCancelPlan = commandController != null
                && (allowRobotDispatch || commandController.CurrentMode == TwinMode.Plan);
            if (shouldCancelPlan)
            {
                _cancelPlanCalls++;
                try
                {
                    commandController.CancelPlan();
                }
                catch (Exception e)
                {
                    Debug.LogWarning(LogTag + " CancelPlan exception: " + e.GetType().Name + ": " + e.Message);
                }
            }
            else
            {
                Debug.Log(LogTag + " cancel: CancelPlan not called"
                    + " (not in Plan mode and allowRobotDispatch=false); ReleaseBackToLiveFeedback done");
            }

            if (ikController != null && ikController.TryGetCurrentTcpPose(out Vector3 pos, out Quaternion rot))
            {
                _targetPosition = pos;
                _targetRotation = rot;
                _workspaceCenter = pos;
                ikController.SetTargetPose(pos, rot);
            }

            ResetTargetStatusToIdle();
            Debug.Log(LogTag + " cancel(" + source + "): target reset to current TCP=" + FormatVector(_targetPosition));
            UpdateVisuals();
            UpdateHud();
        }

        private void ResetToCurrent(string source)
        {
            _dragging = false;
            _testDragActive = false;
            _hasPendingTarget = false;
            _axisLock = -1;
            ReleaseInputOwnership();
            if (ikController != null)
            {
                ikController.ResetTargetToTcp();
                ikController.ReleaseBackToLiveFeedback();
                if (ikController.TryGetCurrentTcpPose(out Vector3 pos, out Quaternion rot))
                {
                    _targetPosition = pos;
                    _targetRotation = rot;
                    _workspaceCenter = pos;
                    ikController.SetTargetPose(pos, rot);
                }
            }

            ResetTargetStatusToIdle();
            Debug.Log(LogTag + " reset(" + source + "): back to current TCP=" + FormatVector(_targetPosition));
            UpdateVisuals();
            UpdateHud();
        }

        // ================================================================== 测试入口（UI/18 自检用）

        public bool BeginDragForTest()
        {
            if (!_initialized)
            {
                TryInitialize();
            }

            if (!_initialized)
            {
                return false;
            }

            if (!_dragging)
            {
                BeginDragInternalForTest();
            }

            return true;
        }

        public void DragToForTest(Vector3 worldDesired)
        {
            if (!BeginDragForTest())
            {
                return;
            }

            Vector3 desired = ApplyAxisLock(worldDesired);
            ApplyTargetStep(desired, false, false, true);
            UpdateVisuals();
            UpdateHud();
        }

        /// <summary>测试用：绕过单帧位移上限（仍然受工作空间 AABB 与关节限位约束）。</summary>
        public void DragDirectForTest(Vector3 worldDesired)
        {
            if (!BeginDragForTest())
            {
                return;
            }

            Vector3 desired = ApplyAxisLock(worldDesired);
            ApplyTargetStep(desired, false, true, true);
            UpdateVisuals();
            UpdateHud();
        }

        public void EndDragForTest()
        {
            EndDragInternal("test");
        }

        public void ResetToCurrentForTest()
        {
            ResetToCurrent("test");
        }

        public void CancelForTest()
        {
            CancelAndReset("test");
        }

        public void SetAxisLockForTest(int axis)
        {
            _axisLock = Mathf.Clamp(axis, -1, 2);
        }

        public void SetWorkspaceHalfExtentsForTest(Vector3 halfExtents)
        {
            workspaceHalfExtents = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(halfExtents.x)),
                Mathf.Max(0.01f, Mathf.Abs(halfExtents.y)),
                Mathf.Max(0.01f, Mathf.Abs(halfExtents.z)));
        }

        public Vector3 WorkspaceHalfExtentsValue => workspaceHalfExtents;
        public Vector3 WorkspaceCenter => _workspaceCenter;
        public float MaxStepMeters => maxStepMeters;

        // ================================================================== 视觉

        private void EnsureVisuals()
        {
            if (_visualsCreated)
            {
                return;
            }

            _visualsCreated = true;
            if (!showHandleVisual)
            {
                return;
            }

            try
            {
                _handleRoot = new GameObject("TwinIkHandle_Runtime").transform;
                _handleRoot.SetParent(null, true);

                _sphereGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereGo.name = "TwinIkHandleSphere";
                _sphereGo.transform.SetParent(_handleRoot, true);
                _sphereGo.transform.localScale = Vector3.one * (handleRadius * 2f);
                Collider sphereCollider = _sphereGo.GetComponent<Collider>();
                if (sphereCollider != null)
                {
                    sphereCollider.isTrigger = true;
                }

                _sphereMaterial = CreateSphereMaterial(idleColor);
                _sphereRenderer = _sphereGo.GetComponent<MeshRenderer>();
                if (_sphereRenderer != null)
                {
                    _sphereRenderer.sharedMaterial = _sphereMaterial;
                    _sphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _sphereRenderer.receiveShadows = false;
                    _sphereRenderer.lightProbeUsage = LightProbeUsage.Off;
                    _sphereRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                _lineMaterial = CreateLineMaterial(alwaysOnTop);
                _axisRoot = new GameObject("TwinIkHandleAxes").transform;
                _axisRoot.SetParent(_handleRoot, true);

                _axisLines = new LineRenderer[3];
                Color[] axisColors = { axisXColor, axisYColor, axisZColor };
                string[] axisNames = { "AxisX", "AxisY", "AxisZ" };
                for (int i = 0; i < 3; i++)
                {
                    _axisLines[i] = CreateLine(axisNames[i], _lineMaterial, axisColors[i], axisLineWidth, 2);
                }

                _guideLine = CreateLine("TcpToTargetGuide", _lineMaterial, guideLineColor, axisLineWidth * 0.6f, 2);
                Debug.Log(LogTag + " visuals created: sphere=" + (int)(handleRadius * 2000f) + "mm"
                    + " axes=" + (showAxisLines ? 3 : 0) + " axisLen=" + (axisLength * 1000f).ToString("F0") + "mm");
            }
            catch (Exception e)
            {
                _visualsCreated = false;
                Debug.LogWarning(LogTag + " visual create failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private LineRenderer CreateLine(string name, Material material, Color color, float width, int positionCount)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_handleRoot, true);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.positionCount = positionCount;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = false;
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
            return line;
        }

        private void UpdateVisuals()
        {
            if (!_visualsCreated || _sphereGo == null)
            {
                return;
            }

            bool visible = enableHandle && showHandleVisual && _initialized;
            _sphereGo.SetActive(visible);
            if (_axisRoot != null)
            {
                _axisRoot.gameObject.SetActive(visible && showAxisLines);
            }

            if (_guideLine != null)
            {
                _guideLine.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            Color color = ComputeHandleColor();
            CurrentHandleColor = color;
            _sphereGo.transform.position = _targetPosition;
            _sphereGo.transform.localScale = Vector3.one * (handleRadius * 2f);

            if (_sphereMaterial != null)
            {
                SetMaterialColor(_sphereMaterial, color);
            }

            if (showAxisLines && _axisLines != null)
            {
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                Color[] baseColors = { axisXColor, axisYColor, axisZColor };
                for (int i = 0; i < _axisLines.Length; i++)
                {
                    LineRenderer line = _axisLines[i];
                    if (line == null)
                    {
                        continue;
                    }

                    float length = axisLength;
                    if (_axisLock == i)
                    {
                        length *= 1.35f;
                    }

                    line.SetPosition(0, _targetPosition);
                    line.SetPosition(1, _targetPosition + axes[i] * length);
                    Color axisColor = baseColors[i];
                    if (_axisLock == i)
                    {
                        axisColor = Color.Lerp(axisColor, Color.white, 0.55f);
                    }

                    line.startColor = axisColor;
                    line.endColor = axisColor;
                    line.startWidth = axisLineWidth * (_axisLock == i ? 2.2f : 1f);
                    line.endWidth = line.startWidth;
                }
            }

            if (_guideLine != null)
            {
                Vector3 tcp = _targetPosition;
                if (ikController != null && ikController.TryGetCurrentTcpPose(out Vector3 current, out _))
                {
                    tcp = current;
                }

                _guideLine.SetPosition(0, tcp);
                _guideLine.SetPosition(1, _targetPosition);
                Color guide = guideLineColor;
                guide.a = _reachable ? guideLineColor.a : 0.9f;
                if (!_reachable)
                {
                    guide = unreachableColor;
                }

                _guideLine.startColor = guide;
                _guideLine.endColor = guide;
            }
        }

        private Color ComputeHandleColor()
        {
            if (!_reachable || !_jointLimitsOk)
            {
                return unreachableColor;
            }

            if (_dragging)
            {
                return dragColor;
            }

            if (_hovered)
            {
                return hoverColor;
            }

            return idleColor;
        }

        // ================================================================== HUD

        private void EnsureHud()
        {
            if (_hudCreated || !showHud)
            {
                return;
            }

            bool hasTmp = TMP_Settings.instance != null;
            if (!hasTmp)
            {
                Debug.LogWarning(LogTag + " HUD skipped: TMP_Settings missing");
                return;
            }

            try
            {
                var canvasGo = new GameObject("TwinIkHandleHudCanvas", typeof(Canvas), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                _hudCanvasRoot = canvasGo;
                _hudCanvas = canvasGo.GetComponent<Canvas>();
                _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _hudCanvas.sortingOrder = 32010;

                var panelGo = new GameObject("TwinIkHandleHudPanel", typeof(RectTransform), typeof(Image));
                panelGo.transform.SetParent(canvasGo.transform, false);
                var panelRt = panelGo.GetComponent<RectTransform>();
                panelRt.anchorMin = new Vector2(0f, 1f);
                panelRt.anchorMax = new Vector2(0f, 1f);
                panelRt.pivot = new Vector2(0f, 1f);
                panelRt.anchoredPosition = new Vector2(20f, -20f);
                panelRt.sizeDelta = new Vector2(620f, 248f);
                _hudPanel = panelGo.GetComponent<Image>();
                _hudPanel.color = new Color(0.03f, 0.09f, 0.11f, 0.78f);
                _hudPanel.raycastTarget = false;

                var textGo = new GameObject("TwinIkHandleHudText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(panelGo.transform, false);
                var textRt = textGo.GetComponent<RectTransform>();
                textRt.anchorMin = new Vector2(0f, 1f);
                textRt.anchorMax = new Vector2(1f, 1f);
                textRt.pivot = new Vector2(0f, 1f);
                textRt.anchoredPosition = new Vector2(12f, -10f);
                textRt.sizeDelta = new Vector2(-24f, 176f);
                _hudText = textGo.GetComponent<TMP_Text>();
                _hudText.font = DigitalTwin.UI.TwinUIStyle.GetFont();
                _hudText.fontSize = 15f;
                _hudText.color = new Color(0.9f, 0.95f, 1f, 1f);
                _hudText.alignment = TextAlignmentOptions.TopLeft;
                _hudText.raycastTarget = false;
                _hudText.text = "IK HUD 初始化…";

                _resetButton = CreateHudButton(panelGo.transform, "回当前 (R)", new Vector2(12f, 10f),
                    new Color(0.05f, 0.28f, 0.35f, 0.95f), ResetToCurrentFromButton);
                _cancelButton = CreateHudButton(panelGo.transform, "取消 (Esc)", new Vector2(132f, 10f),
                    new Color(0.36f, 0.14f, 0.10f, 0.95f), CancelFromButton);

                _hudCreated = true;
            }
            catch (Exception e)
            {
                _hudCreated = false;
                Debug.LogWarning(LogTag + " HUD create failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private Button CreateHudButton(Transform parent, string label, Vector2 anchoredPosition, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("TwinIkButton_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(116f, 30f);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var labelText = labelGo.GetComponent<TMP_Text>();
            labelText.font = DigitalTwin.UI.TwinUIStyle.GetFont();
            labelText.fontSize = 14f;
            labelText.color = new Color(0.92f, 0.98f, 1f, 1f);
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.raycastTarget = false;
            labelText.text = label;
            return button;
        }

        private void ResetToCurrentFromButton()
        {
            ResetToCurrent("HUD button");
        }

        private void CancelFromButton()
        {
            CancelAndReset("HUD button");
        }

        private void UpdateHud()
        {
            if (!_hudCreated || _hudText == null)
            {
                return;
            }

            Vector3 tcp = _targetPosition;
            if (ikController != null && ikController.TryGetCurrentTcpPose(out Vector3 currentTcp, out _))
            {
                tcp = currentTcp;
            }

            string dispatch = allowRobotDispatch
                ? "dispatch=armed (this build never calls EnterExecute)"
                : "dispatch=disabled (local preview only)";
            string axis = _axisLock < 0 ? "None" : AxisLockName(_axisLock);
            string reach = _reachable ? "ok" : "UNREACHABLE";
            string status = _jointLimitsOk ? _statusText : ("JointLimitRejected/" + _statusText);

            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine("[IK] 末端法兰拖动预览（本地 ghost，不下发）");
            sb.AppendLine(dispatch + "   allowRobotDispatch=" + allowRobotDispatch);
            sb.AppendLine("cur  " + FormatVector(tcp) + "   tgt " + FormatVector(_targetPosition));
            sb.AppendLine("Δpos " + (PositionErrorMeters * 1000f).ToString("F2") + " mm   Δrot "
                + RotationErrorDeg.ToString("F2") + " deg   iter " + Iterations + "   status " + status);
            sb.AppendLine("reach " + reach + "   axis " + axis + "   step "
                + (maxStepMeters * 1000f).ToString("F1") + " mm   lastStep "
                + (_lastStepMeters * 1000f).ToString("F2") + " mm   boundsClamp=" + (_workspaceClamped ? "1" : "0"));
            sb.AppendLine("ghost updates " + _planUpdates + "   enterExecute " + _enterExecuteCalls
                + "   dispatchCalls " + DispatchCalls + "   planEnter " + _planEnterAttempts);
            sb.AppendLine("LMB 拖动 | X/Y/Z 或 1/2/3 锁轴 | Shift 精调 | R 回当前 | Esc 取消");

            _hudText.text = sb.ToString();
            Color textColor = _reachable ? new Color(0.9f, 0.95f, 1f, 1f) : new Color(1f, 0.55f, 0.5f, 1f);
            _hudText.color = textColor;
        }

        // ================================================================== 指针/UI 工具

        private bool IsPointerOnHandle(Vector2 screenPoint)
        {
            if (playCamera == null || _handleRoot == null)
            {
                return false;
            }

            Ray ray = playCamera.ScreenPointToRay(screenPoint);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f, handleHitLayers.value, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (collider.transform == _handleRoot || collider.transform.IsChildOf(_handleRoot))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointerOverUi(Vector2 screenPoint)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var data = new PointerEventData(eventSystem) { position = screenPoint };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(data, results);
            return results.Count > 0;
        }

        // ================================================================== 材质 / 工具

        private static Material CreateLineMaterial(bool onTop)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_ZTest"))
            {
                material.SetInt("_ZTest", onTop ? (int)CompareFunction.Always : (int)CompareFunction.LessEqual);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", 0);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            material.renderQueue = onTop ? 4004 : 3000;
            return material;
        }

        private static Material CreateSphereMaterial(Color color)
        {
            // 优先用内置 Hidden/Internal-Colored：它是本工程里唯一确定支持 ZTest Always 的着色器
            // （URP/Unlit 没有 _ZTest 属性，无法置顶；手柄球心正好在法兰上，会被机器人本体挡住）。
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            SetMaterialColor(material, color);
            if (material.HasProperty("_ZTest"))
            {
                material.SetInt("_ZTest", (int)CompareFunction.Always);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", 0);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt("_SrcBlend", (int)BlendMode.One);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt("_DstBlend", (int)BlendMode.Zero);
            }

            material.renderQueue = 4002;
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static string AxisLockName(int axis)
        {
            switch (axis)
            {
                case 0:
                    return "X";
                case 1:
                    return "Y";
                case 2:
                    return "Z";
                default:
                    return "None";
            }
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
        }
    }
}