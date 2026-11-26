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

        [Header("Ghost 半透明预演（MoveIt2 风格）")]
        [Tooltip("开启后：IK 拖拽只驱动半透明 Ghost 骨架，实体静止；点「执行」才按真机速度阻尼跟随。")]
        [SerializeField] private bool ghostPreviewEnabled = true;
        [Tooltip("实体执行时的关节角速度上限（度/秒）。取自 E5 真机 RT 实测 dq 的保守值，可调。")]
        [SerializeField, Range(1f, 120f)] private float maxJointSpeedDegPerSec = 20f;
        [Tooltip("Ghost 拖拽阻尼时间常数（秒）。越小越跟手，越大越沉稳。")]
        [SerializeField, Range(0.01f, 0.5f)] private float ghostSmoothingTau = 0.08f;
        [Tooltip("是否启用三点位（Home_VR_DT / Safty_1 / Screw_10）双阈值滞回磁吸。")]
        [SerializeField] private bool ghostSnapEnabled = true;
        [Tooltip("吸合半径（米）：手柄目标进入该半径即吸附到该标定点精确关节角。")]
        [SerializeField, Range(0.005f, 0.15f)] private float snapEnterRadius = 0.045f;
        [Tooltip("挣脱半径（米）：拖离超过该半径才解除吸附，避免边界抖动/粘死。")]
        [SerializeField, Range(0.01f, 0.3f)] private float snapExitRadius = 0.075f;
        [Tooltip("执行到位的关节角容差（度/轴）。")]
        [SerializeField, Range(0.01f, 1f)] private float executeToleranceDeg = 0.05f;

        private GhostArm _ghost;
        private float[] _ghostJoints = new float[6];
        private float[] _snapJointDeg;
        private string _snapName = string.Empty;
        private bool _snapActive;
        private bool _snapArmed;
        private bool _executing;
        private float _execProgress;
        private float _execWorstSpeed;
        private float _execPeakErrDeg;
        private Coroutine _execRoutine;

        [Header("VR_D 实训约束：电池包顶面安全网格面")]
        [Tooltip("VR_D = ON 时显示安全网格面并启用「安全高度 + 工艺门禁」两级拦截；OFF = 自由模式。")]
        [SerializeField] private bool vrdEnabled;
        [Tooltip("网格面高度微调（毫米），相对电池包顶面基准 Y。正 = 抬高。")]
        [SerializeField, Range(-50f, 50f)] private float vrdPlaneOffsetMm;
        [Tooltip("电池包顶面基准 Y（实测 1.2631 m）。")]
        [SerializeField] private float vrdPlaneBaseY = VrdSafetyPlane.DefaultPlaneY;
        [Tooltip("表面禁区判定的下压容差（米）：TCP 低于 planeY - 该值即判定 BELOW_SAFETY_PLANE。")]
        [SerializeField] private float vrdBelowTolerance = 0.002f;

        private VrdSafetyPlane _plane;
        private ScrewReplayPlayer _replay;
        private bool _vrdBlocked;
        private string _vrdReason = "OFF";
        private string _vrdMessage = string.Empty;
        private float _ghostOpacity = 1f;
        private readonly float[] _vrdGhostTcp = new float[6];

        /// <summary>三轴显示/锁轴使用的参考系。</summary>
        public enum AxisFrameMode
        {
            /// <summary>ROS REP-103 风格：X 朝外（基座正前）、Y 朝左、Z 朝上。默认。</summary>
            RosWorldZUp = 0,
            /// <summary>工具轴风格：X 沿 TCP 刀轴朝外、Z 为工具"上"、右手三指。</summary>
            TcpToolFrame = 1
        }

        [Header("手柄外观（R3 主题：青 / 琥珀）")]
        [Tooltip("空闲色（默认青）。")]
        [SerializeField] private Color idleColor = new Color(0.13f, 0.85f, 0.94f, 1f);
        [Tooltip("鼠标悬停色（更亮青）。")]
        [SerializeField] private Color hoverColor = new Color(0.55f, 0.95f, 1f, 1f);
        [Tooltip("拖动中色（默认琥珀）。")]
        [SerializeField] private Color dragColor = new Color(0.96f, 0.65f, 0.14f, 1f);
        [Tooltip("不可达 / 关节越界色（红）。")]
        [SerializeField] private Color unreachableColor = new Color(1f, 0.22f, 0.2f, 1f);
        [Tooltip("顶到 ±30°/轴 限位时的提示色（琥珀，非故障）。")]
        [SerializeField] private Color limitStopColor = new Color(1f, 0.80f, 0.28f, 1f);
        [Tooltip("TCP→目标引导线颜色。")]
        [SerializeField] private Color guideLineColor = new Color(0.96f, 0.65f, 0.14f, 0.65f);
        [Tooltip("X 轴短线颜色（朝外，标准红）。")]
        [SerializeField] private Color axisXColor = new Color(0.92f, 0.20f, 0.20f, 1f);
        [Tooltip("Y 轴短线颜色（朝左，标准绿）。")]
        [SerializeField] private Color axisYColor = new Color(0.30f, 0.85f, 0.32f, 1f);
        [Tooltip("Z 轴短线颜色（朝上，标准蓝）。")]
        [SerializeField] private Color axisZColor = new Color(0.28f, 0.52f, 0.98f, 1f);
        [Tooltip("球体半径（米）。constantScreenSize=false 时使用。")]
        [SerializeField, Range(0.004f, 0.1f)] private float handleRadius = 0.02f;
        [Tooltip("三轴短线长度（米）。constantScreenSize=false 时使用。")]
        [SerializeField, Range(0.02f, 0.5f)] private float axisLength = 0.11f;
        [Tooltip("三轴短线线宽（米）。")]
        [SerializeField, Range(0.001f, 0.03f)] private float axisLineWidth = 0.006f;

        [Header("手柄尺寸：屏幕恒定（推荐）")]
        [Tooltip("勾选后球与三轴随相机距离缩放 —— 近看不巨大、远看不消失（与 Unity 原生 gizmo 一致）。" +
                 "关闭则使用上面的固定米制尺寸。")]
        [SerializeField] private bool constantScreenSize = true;
        [Tooltip("球的屏幕半径（像素，以 1080p 高度为基准）。默认 8 px ≈ 直径 16 px。")]
        [SerializeField, Range(4f, 40f)] private float handleScreenRadiusPixels = 8f;
        [Tooltip("三轴的屏幕长度（像素，以 1080p 高度为基准）。")]
        [SerializeField, Range(20f, 160f)] private float axisScreenLengthPixels = 55f;
        [Tooltip("三轴参考系：RosWorldZUp = X 朝外 / Z 朝上（推荐）；TcpToolFrame = X 沿刀轴。")]
        [SerializeField] private AxisFrameMode axisFrameMode = AxisFrameMode.RosWorldZUp;
        [Tooltip("拾取半径相对球半径的放大倍数（手感用，不影响显示大小）。")]
        [SerializeField, Range(1f, 4f)] private float pickRadiusScale = 1.6f;
        [Tooltip("拾取最小屏幕半径（像素）。球再小也点得中。")]
        [SerializeField, Range(4f, 40f)] private float minPickPixels = 14f;
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
        // ±jointLimitDeg/轴 硬停止：解被关节限幅截断时，把 ghost 目标回贴到实际 TCP，避免目标跑飞后判成"不可达"。
        private bool _limitStop;
        private float _worstClampDeg;
        private float _renderRadius = 0.02f;
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
        public Color LimitStopColorValue => limitStopColor;
        public bool LimitStop => _limitStop;
        public float WorstClampDeg => _worstClampDeg;
        public float HandleRadiusValue => handleRadius;
        public float AxisLengthValue => axisLength;
        public bool ConstantScreenSize => constantScreenSize;
        /// <summary>当前实际渲染的世界半径（米）。</summary>
        public float RenderRadiusValue => _renderRadius;
        /// <summary>当前球在屏幕上的像素半径（自检用）。</summary>
        public float ScreenRadiusPixels => _renderRadius * PixelsPerMeter(_targetPosition);
        public AxisFrameMode AxisFrame => axisFrameMode;
        public Vector3 AxisFrameX { get { CaptureHandleFrame(); return _frameX; } }
        public Vector3 AxisFrameY { get { CaptureHandleFrame(); return _frameY; } }
        public Vector3 AxisFrameZ { get { CaptureHandleFrame(); return _frameZ; } }
        /// <summary>屏幕点是否落在手柄拾取体内（自检/回归用，等价于左键命中判定）。</summary>
        public bool IsPointerOnHandleForTest(Vector3 screenPoint) => IsPointerOnHandle(new Vector2(screenPoint.x, screenPoint.y));
        public string IkStatusText => _statusText;
        public float PositionErrorMeters { get; private set; }
        public float RotationErrorDeg { get; private set; }
        public int Iterations { get; private set; }
        public bool HasSolution => _hasSolution;
        public string HudTextValue => _hudText == null ? string.Empty : _hudText.text;
        public bool HudCreated => _hudCreated;
        public GameObject HudCanvasRoot => _hudCanvasRoot;

        /// <summary>
        /// 双 TCP 切换（ToolTcpSwitcher）后重新捕获当前 TCP：ghost 手柄、初始位姿与工作空间
        /// 随之更新。只做本地预览，不调用任何 EnterPlan/EnterExecute，也不下发指令。
        /// </summary>
        public void RecaptureTcp()
        {
            if (ikController == null) return;
            if (!ikController.TryGetCurrentTcpPose(out Vector3 pos, out Quaternion rot)) return;
            if (!IsFinite(pos)) return;

            _targetPosition = pos;
            _targetRotation = rot;
            _initialTcpPosition = pos;
            _initialTcpRotation = rot;
            _workspaceCenter = pos;
            ikController.SetTargetPose(pos, rot);
            ikController.ResetTargetToTcp();
            _hasPendingTarget = false;
            _snapActive = false;
            _snapArmed = false;
            _snapName = string.Empty;
            _snapJointDeg = null;
            InvalidateHandleFrame();
            CaptureHandleFrame();
            EnsureVisuals();
            EnsureHud();
            Debug.Log("[PLAY-IK] recaptured TCP after switch: " + FormatVector(pos));
        }

        /// <summary>
        /// 运行时开关拖拽球。默认关闭（进 Play 不自动激活，防误触）。
        /// 关闭时隐藏球体并中止拖拽，不改变已求解的姿态。
        /// </summary>
        /// <summary>联动 IK 求解开关：开启拖拽才允许求解，关闭/复位立即休眠（不影响 Transform 姿态驱动）。</summary>
        private void SetDriveSuppressed(bool value)
        {
            if (ikController == null) ikController = FindObjectOfType<RobotIkController>();
            var m = ikController != null ? ikController.RobotModel : null;
            if (m != null) m.SetTransformDriveSuppressed(value);
        }

        private void SetIkSolverActive(bool value)
        {
            if (ikController == null) ikController = UnityEngine.Object.FindObjectOfType<RobotIkController>();
            if (ikController != null) ikController.SetSolverActive(value);
        }

        public void SetIkDragEnabled(bool value)
        {
            enableHandle = value;
            if (!value)
            {
                _dragging = false;
                _hasPendingTarget = false;
                StopGhostExecution(false);
                if (_ghost != null) _ghost.Hide();
                _snapActive = false;
                _snapName = string.Empty;
                if (_sphereGo != null) _sphereGo.SetActive(false);
                if (_handleRoot != null) _handleRoot.gameObject.SetActive(false);
                // 关闭拖拽 = IK 彻底休眠，常态（真机/回放）零干扰。
                SetIkSolverActive(false);
                SetDriveSuppressed(false);
            }
            else
            {
                if (_handleRoot != null) _handleRoot.gameObject.SetActive(true);
                if (_sphereGo != null) _sphereGo.SetActive(true);
                // 以“此刻的姿态”作为 ±30° 限幅基准，避免开启瞬间就被大角度解带飞。
                if (ikController != null) ikController.CaptureJointReference();
                // Ghost 与实体当前姿态完全重合（q_entity），随后拖动只动 Ghost。
                EnsureGhost();
                RecaptureTcp();
                SetIkSolverActive(true);
                SetDriveSuppressed(true);
                // 立刻求解一次：让 手柄状态 从 `Disabled`（求解器休眠时的 LastStatus）切到真实状态，
                // 否则卡片会一直显示上一次的旧文案（用户看到 `手柄状态: Disabled` 的根因）。
                SolveNow();
                Debug.Log(LogTag + " drag ON → status=" + _statusText + " ghost=" +
                          (_ghost != null ? (_ghost.Visible ? "visible" : "hidden") : "null"));
            }
            Debug.Log("[PLAY-IK] drag enabled=" + value);
        }

        public void ToggleIkDrag() => SetIkDragEnabled(!enableHandle);

        /// <summary>一键回到安全姿态：零位 + 清空拖拽目标（仅本地预览，不下发）。</summary>
        public void RestoreSafePose()
        {
            _dragging = false;
            _hasPendingTarget = false;
            // 执行途中被复位：立即中止跟随（下面会把实体安全送回 Home_VR_DT）。
            StopGhostExecution(false);
            StopScrewReplay();
            _snapActive = false;
            _snapArmed = false;
            _snapName = string.Empty;
            if (_ghost != null) _ghost.Hide();
            if (_sphereGo != null) _sphereGo.SetActive(false);
            // 复位即让 IK 休眠，避免残留解继续驱动模型。
            SetIkSolverActive(false);
            if (ikController != null) ikController.ClearPendingSolution();
            SetDriveSuppressed(false);
            RobotModelController model = ikController != null ? ikController.RobotModel : null;
            if (model != null)
            {
                // 复位到 Home_VR_DT(0,0,90,0,0,180)，不是全零位（零位会折叠“打死结”）。
                model.ApplyStartupPose(false, true);
                // 双保险：清 IK 残留 + 锁定 Home_VR_DT 0.8s
                if (ikController != null) ikController.ClearPendingSolution();
                model.LockPose(new float[] { 0f, 0f, 90f, 0f, 0f, 180f }, 0.8f);
                Debug.Log("[PLAY-IK] safe pose restored (Home_VR_DT, locked 0.8s)");
            }
            else
            {
                Debug.LogWarning("[PLAY-IK] safe pose skipped: robot model missing");
            }
            if (ikController != null)
            {
                ikController.CaptureJointReference();
                ikController.ResetTargetToTcp();
            }
            RecaptureTcp();
        }

        /// <summary>P9：关闭/开启 IK 手柄 HUD（V2 把它集成进"IK 预览"卡片，故默认关闭）。</summary>
        public void SetHudEnabled(bool value)
        {
            showHud = value;
            if (_hudCanvasRoot != null)
            {
                _hudCanvasRoot.SetActive(value);
            }

            Debug.Log("[PLAY-IK] hud=" + value);
        }
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

            // Ghost 预演动画必须逐帧推进（求解只有 20 Hz）：把解/磁吸角按速度上限 + 阻尼插值
            // 平滑推向 Ghost 骨架；实体保持静止，直到点「执行」。
            UpdateVrd();
            UpdateGhostFromSolution();
            UpdateGhostOpacity();

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
            _limitStop = false;
            _statusText = ikController.LastStatus.ToString();
            InvalidateHandleFrame();
            CaptureHandleFrame();

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
            _limitStop = false;
            _worstClampDeg = 0f;
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
            _limitStop = false;
            InvalidateHandleFrame();
            CaptureHandleFrame();
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
            _limitStop = false;
            InvalidateHandleFrame();
            CaptureHandleFrame();
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

            CaptureHandleFrame();
            Vector3 axis = _axisLock == 0 ? _frameX : (_axisLock == 1 ? _frameY : _frameZ);
            Vector3 delta = desired - _dragStartTargetPosition;
            return _dragStartTargetPosition + axis * Vector3.Dot(delta, axis);
        }

        // ---- 手柄三轴参考系（显示与锁轴共用同一套轴，保证"看到的轴 = 拖动的方向"）----
        private Vector3 _frameX = Vector3.right;
        private Vector3 _frameY = Vector3.up;
        private Vector3 _frameZ = Vector3.forward;
        private bool _frameCaptured;

        /// <summary>
        /// 计算并缓存三轴参考系（首次/复位/TCP 切换时重算；拖动期间保持不变，避免锁轴方向漂移）。
        /// RosWorldZUp：X = 基座→TCP 的水平朝外方向，Z = 世界上方，Y = 左手侧（屏幕表现对齐 ROS REP-103）。
        /// </summary>
        private void CaptureHandleFrame()
        {
            if (_frameCaptured)
            {
                return;
            }

            _frameCaptured = true;

            if (axisFrameMode == AxisFrameMode.TcpToolFrame)
            {
                // 工具系：X 沿刀轴朝外，Z 为工具"上"，Y 补足。
                Vector3 x = _targetRotation * Vector3.forward;
                if (x.sqrMagnitude < 1e-6f)
                {
                    x = Vector3.forward;
                }

                x.Normalize();
                Vector3 z = Vector3.ProjectOnPlane(_targetRotation * Vector3.up, x);
                if (z.sqrMagnitude < 1e-6f)
                {
                    z = Vector3.ProjectOnPlane(Vector3.up, x);
                }

                if (z.sqrMagnitude < 1e-6f)
                {
                    z = Vector3.Cross(x, Vector3.right);
                }

                z.Normalize();
                _frameX = x;
                _frameZ = z;
                _frameY = Vector3.Cross(_frameX, _frameZ).normalized;
                return;
            }

            // RosWorldZUp（默认）
            Vector3 outward = Vector3.zero;
            if (ikController != null)
            {
                outward = _initialTcpPosition - ikController.transform.position;
            }

            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f && ikController != null)
            {
                outward = ikController.transform.forward;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 1e-6f && playCamera != null)
            {
                outward = playCamera.transform.forward;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 1e-6f)
            {
                outward = Vector3.forward;
            }

            _frameX = outward.normalized;      // 朝外
            _frameZ = Vector3.up;              // 朝上
            _frameY = Vector3.Cross(_frameX, _frameZ).normalized; // 左手侧
        }

        private void InvalidateHandleFrame()
        {
            _frameCaptured = false;
        }

        // ---- 渲染尺寸（屏幕恒定或固定米制）----

        /// <summary>当前球体世界半径（米）：屏幕恒定时按相机距离换算，并夹在合理范围内。</summary>
        private float RenderRadius()
        {
            if (!constantScreenSize)
            {
                return Mathf.Max(0.001f, handleRadius);
            }

            float ppm = Mathf.Max(1f, PixelsPerMeter(_targetPosition));
            float px = Mathf.Max(2f, handleScreenRadiusPixels * (Screen.height / 1080f));
            return Mathf.Clamp(px / ppm, 0.005f, 0.09f);
        }

        /// <summary>当前三轴世界长度（米）。</summary>
        private float RenderAxisLength()
        {
            if (!constantScreenSize)
            {
                return Mathf.Max(0.005f, axisLength);
            }

            float ppm = Mathf.Max(1f, PixelsPerMeter(_targetPosition));
            float px = Mathf.Max(4f, axisScreenLengthPixels * (Screen.height / 1080f));
            return Mathf.Clamp(px / ppm, 0.01f, 0.35f);
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
                // Ghost 预演：只求解，不把解写回实体（实体在「执行」前保持静止）。
                ikController.TrySolve(!GhostOnlyMode);
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
            _limitStop = ikController.LastSolutionClamped;
            _worstClampDeg = ikController.LastSolutionClampDeg;

            // ±jointLimitDeg/轴 硬停止（手感）：解被逐轴限幅截断时，把 ghost 目标回贴到"实际到达的 TCP"。
            // 否则目标会带着鼠标继续跑飞 → 误差超阈 → 被误判成"不可达/红球"，用户观感就是"拖不动"。
            if (_limitStop && _hasSolution &&
                ikController.TryGetSolutionTcpPose(out Vector3 reachedPos, out _) && IsFinite(reachedPos))
            {
                _targetPosition = reachedPos;
                _statusText = "JointLimitStop(±" + ikController.JointLimitDeg.ToString("0.#") + "°/轴)";
            }

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

            if (_limitStop)
            {
                // 顶到 ±30°/轴 限位不是故障：保持可达、给琥珀提示色。
                _reachable = true;
            }
            else if (_reachable && (float.IsNaN(PositionErrorMeters) || float.IsInfinity(PositionErrorMeters)
                || PositionErrorMeters > unreachableErrorMeters))
            {
                _reachable = false;
            }

            // 第 4 章工艺/工具门禁：每次 ghost 目标变化都做一次只读评估（不发布、不改模型）。
            // 命中非法工艺步骤或工具不匹配时 LastResult.Allowed=false，卡片显示红字原因；
            // 命中螺钉区则绑定预标定点位（Screw_10 / E5_SCREW_DISASSEMBLY）供 Ghost 预览与派发使用。
            EvaluateProcessGate();


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

        /// <summary>
        /// 自检入口：用屏幕坐标走一遍**真实拾取 + 起拖**路径（UI 遮挡 → 命中 → 起拖），
        /// 不读鼠标硬件。回归"开启 IK 后点不中手柄"这类问题就靠它。
        /// </summary>
        public bool BeginDragAtScreenPointForTest(Vector2 screenPoint)
        {
            if (!_initialized)
            {
                TryInitialize();
            }

            if (!_initialized)
            {
                return false;
            }

            if (_dragging)
            {
                return true;
            }

            if (IsPointerOverUi(screenPoint))
            {
                Debug.Log(LogTag + " test pick blocked by interactive UI at " + screenPoint);
                return false;
            }

            if (!IsPointerOnHandle(screenPoint))
            {
                Debug.Log(LogTag + " test pick miss at " + screenPoint + " handleScreen=" + (Vector2)HandleScreenPosition);
                return false;
            }

            return BeginDragFromPointer(screenPoint);
        }

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

        // ==================== VR_D 实训约束：电池包顶面安全网格面 + 两级拦截 ====================

        public bool VrdEnabled => vrdEnabled;
        public bool VrdBlocked => vrdEnabled && _vrdBlocked;
        public string VrdReasonCode => _vrdReason;
        public string VrdMessage => _vrdMessage;
        public float VrdPlaneOffsetMm => vrdPlaneOffsetMm;
        public float VrdPlaneWorldY => _plane != null ? _plane.CurrentWorldY
            : vrdPlaneBaseY + vrdPlaneOffsetMm * 0.001f;
        public VrdSafetyPlane SafetyPlane => _plane;
        public ScrewReplayPlayer Replay => _replay;
        public bool ScrewReplayPlaying => _replay != null && _replay.Playing;
        public float ScrewReplayProgress => _replay != null ? _replay.Progress : 0f;
        public float GhostOpacity => _ghostOpacity;

        /// <summary>VR_D 开关（自由模式 ⇄ 实训约束模式）。</summary>
        public void SetVrdEnabled(bool value)
        {
            vrdEnabled = value;
            EnsureVrdPlane();
            if (_plane != null)
            {
                if (value) _plane.Show();
                else _plane.Hide();
            }
            _vrdBlocked = false;
            _vrdReason = value ? "OK" : "OFF";
            _vrdMessage = value ? "VR_D 约束已开启" : "自由模式（无表面禁区）";
            Debug.Log(LogTag + " VR_D=" + value + " planeY=" + VrdPlaneWorldY.ToString("F4") + "m");
        }

        /// <summary>网格面高度步进（毫米）。</summary>
        public void NudgeVrdPlaneOffset(int deltaMm)
        {
            vrdPlaneOffsetMm = Mathf.Clamp(vrdPlaneOffsetMm + deltaMm, -50f, 50f);
            EnsureVrdPlane();
            if (_plane != null) _plane.SetOffsetMm(vrdPlaneOffsetMm);
            Debug.Log(LogTag + " plane offset=" + vrdPlaneOffsetMm.ToString("+0.0;-0.0") + "mm → Y=" +
                      VrdPlaneWorldY.ToString("F4") + "m");
        }

        private void EnsureVrdPlane()
        {
            if (_plane != null) return;
            Vector2 center = VrdSafetyPlane.DefaultCenterXz;
            Vector2 size = VrdSafetyPlane.DefaultSizeXz;
            if (TryGetDis1Bounds(out Bounds b))
            {
                center = new Vector2(b.center.x, b.center.z);
                size = new Vector2(b.size.x + VrdSafetyPlane.DefaultMarginMeters * 2f,
                                   b.size.z + VrdSafetyPlane.DefaultMarginMeters * 2f);
            }
            _plane = VrdSafetyPlane.Create(vrdPlaneBaseY, center, size);
            _plane.SetOffsetMm(vrdPlaneOffsetMm);
            if (vrdEnabled) _plane.Show();
        }

        private static bool TryGetDis1Bounds(out Bounds bounds)
        {
            bounds = default(Bounds);
            GameObject go = GameObject.Find("Dis_1");
            if (go == null) return false;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>每帧刷新 VR_D 两级判定：① 安全高度（最高优先） ② 表面之上的工艺/工具门禁。</summary>
        private void UpdateVrd()
        {
            if (!vrdEnabled)
            {
                _vrdBlocked = false;
                _vrdReason = "OFF";
                _vrdMessage = "自由模式（无表面禁区）";
                if (_plane != null && _plane.Visible) _plane.Hide();
                return;
            }

            EnsureVrdPlane();
            if (_plane == null)
            {
                _vrdBlocked = false;
                _vrdReason = "PLANE_UNAVAILABLE";
                return;
            }
            if (!_plane.Visible) _plane.Show();

            float planeY = _plane.CurrentWorldY;
            float tipY = GhostTcpWorldY();
            bool below = tipY < planeY - vrdBelowTolerance;

            if (below)
            {
                _vrdBlocked = true;
                _vrdReason = "BELOW_SAFETY_PLANE";
                _vrdMessage = "低于电池包安全平面，禁止下压";
            }
            else if (ProcessStepGate.HasResult && !ProcessStepGate.LastResult.Allowed)
            {
                _vrdBlocked = true;
                _vrdReason = ProcessStepGate.LastResult.ReasonCode;
                _vrdMessage = ProcessStepGate.LastResult.Message;
            }
            else
            {
                _vrdBlocked = false;
                _vrdReason = "OK";
                _vrdMessage = _snapActive ? ("安全 · 已吸附 " + _snapName) : "安全 · 可执行";
            }
            _plane.SetWarn(_vrdBlocked);
        }

        private float GhostTcpWorldY()
        {
            if (_ghost != null && _ghost.SpindleTcp != null) return _ghost.SpindleTcp.position.y;
            if (ikController != null && ikController.TryGetCurrentTcpPose(out Vector3 p, out _)) return p.y;
            return float.MaxValue;
        }

        /// <summary>重叠消隐：Ghost 与实体完全重合且未拖动时淡出，消除 Z-fighting 斑纹；一拖动立即淡入。</summary>
        private void UpdateGhostOpacity()
        {
            if (_ghost == null || !_ghost.Visible) return;
            float sep = 0f;
            RobotModelController model = Model;
            if (model != null)
            {
                float[] q = model.ReadCurrentDisplayDegrees();
                if (q != null)
                {
                    for (int i = 0; i < _ghostJoints.Length && i < q.Length; i++)
                        sep = Mathf.Max(sep, Mathf.Abs(_ghostJoints[i] - q[i]));
                }
            }
            bool coincident = !_dragging && !_snapActive && sep < 0.05f;
            float target = coincident ? 0f : 1f;
            _ghostOpacity = Mathf.MoveTowards(_ghostOpacity, target, Time.unscaledDeltaTime / 0.12f);
            _ghost.SetOpacity(_ghostOpacity);
        }

        /// <summary>实体当前 6 轴与 Screw_10 标定角的最大偏差（度）；&lt;0 表示拿不到标定点。</summary>
        public float EntityToScrew10MaxErrDeg()
        {
            RobotModelController model = Model;
            if (model == null) return -1f;
            ProcessWaypoints.Waypoint w;
            if (!ProcessWaypoints.TryGet("Screw_10", out w)) return -1f;
            float[] q = model.ReadCurrentDisplayDegrees();
            if (q == null) return -1f;
            float m = 0f;
            for (int i = 0; i < 6 && i < q.Length && i < w.JointDeg.Length; i++)
                m = Mathf.Max(m, Mathf.Abs(q[i] - w.JointDeg[i]));
            return m;
        }

        /// <summary>Screw 拆解启动门槛：VR_D 开 + 实体到位 Screw_10(&lt;0.5°) + 工艺门禁 OK。</summary>
        public bool CanStartScrewReplay(out string reason)
        {
            if (!vrdEnabled) { reason = "请先开启 VR_D"; return false; }
            if (ScrewReplayPlaying) { reason = "回放进行中"; return false; }
            float err = EntityToScrew10MaxErrDeg();
            if (err < 0f) { reason = "未找到 Screw_10 标定点"; return false; }
            if (err >= 0.5f)
            {
                reason = "请先开启 VR_D 并将实体执行到位至 Screw_10（当前偏差 " + err.ToString("F2") + "°）";
                return false;
            }
            if (!ProcessStepGate.HasResult || !ProcessStepGate.LastResult.Allowed)
            {
                reason = "工艺门禁未放行（" + _vrdReason + "）";
                return false;
            }
            reason = "OK";
            return true;
        }

        public bool StartScrewReplay()
        {
            string reason;
            if (!CanStartScrewReplay(out reason))
            {
                _vrdMessage = reason;
                Debug.LogWarning(LogTag + " screw replay rejected: " + reason);
                return false;
            }

            RobotModelController model = Model;
            if (model == null) { _vrdMessage = "机械臂模型缺失"; return false; }

            StopGhostExecution(false);
            if (_ghost != null) _ghost.Hide();

            if (_replay == null) _replay = gameObject.AddComponent<ScrewReplayPlayer>();
            DigitalTwinRuntime runtime = FindObjectOfType<DigitalTwinRuntime>();
            Transform tcp = ikController != null ? ikController.TcpTransform : null;
            if (!_replay.Play(model, runtime, tcp))
            {
                _vrdMessage = "回放数据加载失败：" + _replay.LoadError;
                Debug.LogError(LogTag + " screw replay load failed: " + _replay.LoadError);
                return false;
            }

            _vrdMessage = "E5 拆解回放中…";
            Debug.Log(LogTag + " screw replay started: samples=" + _replay.SampleCount +
                      " dur=" + _replay.Duration.ToString("F2") + "s force=" + _replay.HasForceChannel);
            return true;
        }

        public void StopScrewReplay()
        {
            if (_replay == null) return;
            _replay.Stop();
            RobotModelController model = Model;
            if (model != null)
            {
                float[] q = model.ReadCurrentDisplayDegrees();
                if (q != null)
                {
                    for (int i = 0; i < _ghostJoints.Length && i < q.Length; i++) _ghostJoints[i] = q[i];
                    if (_ghost != null) _ghost.SetJoints(_ghostJoints);
                }
            }
        }

        // ==================== Ghost 半透明预演 / 三点位磁吸 / 阻尼执行（MoveIt2 风格）====================

        /// <summary>Ghost 预演是否生效：拖动只动 Ghost，实体静止（「执行」时才动实体）。</summary>
        public bool GhostOnlyMode => ghostPreviewEnabled && enableHandle && _ghost != null && _ghost.Visible;

        public GhostArm Ghost => _ghost;
        public bool GhostVisible => _ghost != null && _ghost.Visible;
        public bool GhostPreviewEnabled => ghostPreviewEnabled;
        public bool SnapActive => _snapActive;
        public string SnapWaypointName => _snapName;
        public bool GhostExecuting => _executing;
        public float GhostExecProgress => _execProgress;
        public float GhostExecWorstSpeedDegPerSec => _execWorstSpeed;
        public float MaxJointSpeedDegPerSec => maxJointSpeedDegPerSec;
        public float GhostSmoothingTau => ghostSmoothingTau;
        public float SnapEnterRadius => snapEnterRadius;
        public float SnapExitRadius => snapExitRadius;
        public float ExecuteToleranceDeg => executeToleranceDeg;
        public bool GhostSnapEnabled => ghostSnapEnabled;

        public float[] GhostJointDegreesCopy() => (float[])_ghostJoints.Clone();

        public float[] EntityJointDegreesCopy()
        {
            RobotModelController model = Model;
            return model != null ? model.ReadCurrentDisplayDegrees() : null;
        }

        public void SetGhostSnapEnabled(bool value)
        {
            ghostSnapEnabled = value;
            if (!value) { _snapActive = false; _snapName = string.Empty; _snapJointDeg = null; }
        }

        public void SetMaxJointSpeedDegPerSec(float value)
        {
            maxJointSpeedDegPerSec = Mathf.Clamp(value, 1f, 120f);
        }

        /// <summary>编程设置手柄目标（等价于把球拖到该点）；供"去点位"与自动验收使用。</summary>
        public void SetTargetPosition(Vector3 position)
        {
            if (!IsFinite(position)) return;
            // 走既有拖动管线：步长不限（直接到位）+ 立即求解，并保留工作空间钳制与限位硬停止。
            ApplyTargetStep(position, false, true, true);
        }

        private RobotModelController Model
        {
            get
            {
                if (ikController == null) return null;
                if (ikController.RobotModel != null) return ikController.RobotModel;
                return ikController.GetComponent<RobotModelController>();
            }
        }

        /// <summary>创建/重置 Ghost：与实体当前 6 轴完全重合，随后拖动只动 Ghost。</summary>
        private void EnsureGhost()
        {
            if (!ghostPreviewEnabled) return;
            RobotModelController model = Model;
            if (model == null) return;

            try
            {
                if (_ghost == null)
                {
                    _ghost = GhostArm.Create(model, null);
                    if (_ghost == null)
                    {
                        Debug.LogWarning(LogTag + " ghost arm create failed (Clone returned null)");
                        return;
                    }
                    ProcessWaypoints.Ensure(model);
                }
            }
            catch (System.Exception e)
            {
                _ghost = null;
                Debug.LogError(LogTag + " ghost arm create threw: " + e.GetType().Name + ": " + e.Message +
                               "\n" + e.StackTrace);
                return;
            }

            float[] q = model.ReadCurrentDisplayDegrees();
            if (q != null)
            {
                for (int i = 0; i < _ghostJoints.Length && i < q.Length; i++) _ghostJoints[i] = q[i];
            }

            _snapActive = false;
            _snapArmed = false;
            _snapName = string.Empty;
            _snapJointDeg = null;
            _ghost.SetJoints(_ghostJoints);
            _ghost.Show();
            _ghost.SetTint(GhostArm.Tint.Normal);
            Debug.Log(LogTag + " ghost armed (q_entity=[" + FormatDeg(_ghostJoints) + "])");
        }

        /// <summary>每帧把 IK 解（或磁吸标定角）按速度上限 + 一阶阻尼推给 Ghost。</summary>
        private void UpdateGhostFromSolution()
        {
            if (!ghostPreviewEnabled || _executing) return;
            if (_ghost == null || !_ghost.Visible) return;
            RobotModelController model = Model;
            if (model == null) return;

            // ① 三点位双阈值滞回磁吸（以手柄目标位置判定，与鼠标一致）
            //    注意：刚开 IK 时手柄目标恰好在实体 TCP 上（≈ Home_VR_DT），若不设"武装"条件，
            //    一开启就会立刻吸附到 Home 并把 Ghost 钉死 → 用户看到"拖不动/没分离"。
            //    所以必须先真正拖离起点超过吸合半径，磁吸才会生效。
            if (ProcessWaypoints.All.Count == 0) ProcessWaypoints.Ensure(model);
            if (!_snapArmed && Vector3.Distance(_targetPosition, _initialTcpPosition) > snapEnterRadius)
            {
                _snapArmed = true;
            }
            if (ghostSnapEnabled && _snapArmed)
            {
                ProcessWaypoints.Waypoint near;
                float dist;
                bool found = ProcessWaypoints.TryFindNearest(_targetPosition, out near, out dist);
                float threshold = _snapActive ? snapExitRadius : snapEnterRadius;
                if (found && dist <= threshold)
                {
                    if (!_snapActive || _snapName != near.Name)
                    {
                        Debug.Log(LogTag + " snap ON → " + near.Name + " d=" + dist.ToString("F3") + "m (enter " +
                                  snapEnterRadius.ToString("F3") + ")");
                    }
                    _snapActive = true;
                    _snapName = near.Name;
                    _snapJointDeg = near.JointDeg;
                }
                else if (_snapActive)
                {
                    Debug.Log(LogTag + " snap OFF (d=" + dist.ToString("F3") + "m > exit " +
                              threshold.ToString("F3") + "m)");
                    _snapActive = false;
                    _snapName = string.Empty;
                    _snapJointDeg = null;
                }
            }
            else
            {
                _snapActive = false;
                _snapName = string.Empty;
                _snapJointDeg = null;
            }

            // ② 目标关节角：吸附 → 精确标定角；否则 → IK 解
            float[] desired = _snapActive ? _snapJointDeg : (_hasSolution ? _lastSolution : null);
            if (desired == null) return;

            // ③ 速度上限 + 一阶阻尼插值（消除鼠标瞬跳）
            float dt = Mathf.Max(1f / 240f, Time.unscaledDeltaTime);
            float maxStep = maxJointSpeedDegPerSec * dt;
            float k = ghostSmoothingTau <= 0.0001f ? 1f : 1f - Mathf.Exp(-dt / ghostSmoothingTau);
            int n = Mathf.Min(_ghostJoints.Length, desired.Length);
            for (int i = 0; i < n; i++)
            {
                float delta = (desired[i] - _ghostJoints[i]) * k;
                _ghostJoints[i] += Mathf.Clamp(delta, -maxStep, maxStep);
            }
            _ghost.SetJoints(_ghostJoints);

            // ④ 着色：吸附青绿 / 门禁拦截红 / 自由蓝
            bool blocked = (vrdEnabled && _vrdBlocked) ||
                           (ProcessStepGate.HasResult && !ProcessStepGate.LastResult.Allowed);
            _ghost.SetTint(blocked ? GhostArm.Tint.Blocked
                : _snapActive ? GhostArm.Tint.Snap : GhostArm.Tint.Normal);
        }

        /// <summary>点击「执行 (Ghost→实体)」：实体按真机参考速度 + 阻尼加减速平滑移动到 Ghost 姿态。</summary>
        public bool ExecuteGhostToEntity()
        {
            if (_executing) return false;
            RobotModelController model = Model;
            if (model == null || _ghost == null || !_ghost.Visible) return false;
            if (!_hasSolution && !_snapActive) return false;
            if (vrdEnabled && _vrdBlocked)
            {
                Debug.LogWarning(LogTag + " execute blocked by VR_D(" + _vrdReason + "): " + _vrdMessage);
                return false;
            }
            if (_execRoutine != null) StopCoroutine(_execRoutine);
            _execRoutine = StartCoroutine(CoExecuteGhostToEntity());
            return true;
        }

        /// <summary>中止跟随；restoreHome=true 时顺带安全回到 Home_VR_DT（复位/急停用）。</summary>
        public void StopGhostExecution(bool restoreHome = false)
        {
            if (_execRoutine != null)
            {
                StopCoroutine(_execRoutine);
                _execRoutine = null;
            }
            if (_executing)
            {
                Debug.Log(LogTag + " ghost execution aborted" + (restoreHome ? " → Home_VR_DT" : string.Empty));
            }
            _executing = false;
            _execProgress = 0f;
            if (restoreHome) RestoreSafePose();
        }

        private System.Collections.IEnumerator CoExecuteGhostToEntity()
        {
            RobotModelController model = Model;
            if (model == null) yield break;

            float[] from = model.ReadCurrentDisplayDegrees();
            if (from == null || from.Length < 6) yield break;

            float[] to = (float[])_ghostJoints.Clone();
            float worstDelta = 0f;
            for (int i = 0; i < 6; i++) worstDelta = Mathf.Max(worstDelta, Mathf.Abs(to[i] - from[i]));

            float limit = ikController != null ? ikController.JointLimitDeg : 30f;
            if (worstDelta > limit + 0.01f)
            {
                Debug.LogWarning(LogTag + " execute rejected: |Δq|=" + worstDelta.ToString("F2") +
                                 "° exceeds ±" + limit.ToString("F0") + "° window");
                _executing = false;
                yield break;
            }

            _executing = true;
            _execProgress = 0f;
            _execWorstSpeed = 0f;
            _execPeakErrDeg = Mathf.Max(0.001f, worstDelta);

            var authority = model.CurrentControlAuthority;
            model.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);

            float[] cur = (float[])from.Clone();
            float startedAt = Time.unscaledTime;
            float rampIn = 0f;

            while (_executing)
            {
                float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
                rampIn = Mathf.MoveTowards(rampIn, 1f, dt / 0.25f);

                float remain = 0f;
                for (int i = 0; i < 6; i++) remain = Mathf.Max(remain, Mathf.Abs(to[i] - cur[i]));
                if (remain <= executeToleranceDeg) break;

                // S 型：起步 0.25 s 渐入 + 末段 4° 渐出，全程不超过参考速度上限。
                float taper = Mathf.Clamp01(remain / 4f);
                float factor = Mathf.Clamp01(0.25f + 0.75f * rampIn) * (0.30f + 0.70f * taper);
                float step = maxJointSpeedDegPerSec * dt * factor;

                for (int i = 0; i < 6; i++)
                {
                    float d = to[i] - cur[i];
                    cur[i] += Mathf.Clamp(d, -step, step);
                }

                model.ApplyJointDisplayDegrees(cur, false, RobotModelController.PoseSource.CommandPreview, true, false);
                _execWorstSpeed = Mathf.Max(_execWorstSpeed, step / dt);
                _execProgress = Mathf.Clamp01(1f - remain / _execPeakErrDeg);
                yield return null;
            }

            // 精确落位 + 刷新 q_ref，便于连续下一段微调
            for (int i = 0; i < 6; i++) cur[i] = to[i];
            model.ApplyJointDisplayDegrees(cur, false, RobotModelController.PoseSource.CommandPreview, true, true);
            model.SetControlAuthority(authority);
            if (ikController != null) ikController.CaptureJointReference();

            _executing = false;
            _execProgress = 1f;
            _execRoutine = null;
            Debug.Log(LogTag + " ghost executed → entity: worstSpeed=" + _execWorstSpeed.ToString("F2") +
                      "°/s, elapsed=" + (Time.unscaledTime - startedAt).ToString("F2") + "s");
        }

        private static string FormatDeg(float[] q)
        {
            if (q == null) return "null";
            var parts = new string[q.Length];
            for (int i = 0; i < q.Length; i++) parts[i] = q[i].ToString("F2");
            return string.Join(", ", parts);
        }

        // ---- 第 4 章工艺/工具门禁（只读镜像，供卡片显示；判定在 ProcessStepGate）----
        /// <summary>最近一次工艺门禁是否放行。</summary>
        public bool ProcessGateAllowed => ProcessStepGate.LastResult.Allowed;
        public string ProcessGateReasonCode => ProcessStepGate.LastResult.ReasonCode;
        public string ProcessGateMessage => ProcessStepGate.LastResult.Message;
        public string ProcessGateWaypoint => ProcessStepGate.LastResult.MatchedWaypointName;
        public string ProcessGateProgramId => ProcessStepGate.LastResult.MatchedProgramId;

        private string _lastGateSignature = string.Empty;

        /// <summary>ghost 目标变化时评估一次工艺门禁（只读：不发布、不改模型、不写 DB 状态）。</summary>
        private void EvaluateProcessGate()
        {
            ProcessGateResult gate;
            ProcessStepGate.EvaluateTarget(_targetPosition, ProcessStepGate.DefaultChannel, out gate);

            string signature = (gate.Allowed ? "ALLOW" : "BLOCK") + "|" + gate.ReasonCode + "|" +
                               gate.TargetPartName + "|" + gate.MatchedWaypointName;
            if (signature == _lastGateSignature)
            {
                return;
            }

            _lastGateSignature = signature;
            if (gate.Allowed)
            {
                Debug.Log(LogTag + " process gate " + gate.ToLine());
            }
            else
            {
                Debug.LogWarning(LogTag + " process gate " + gate.ToLine());
            }
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
            float renderRadius = RenderRadius();
            _renderRadius = renderRadius;
            _sphereGo.transform.position = _targetPosition;
            _sphereGo.transform.localScale = Vector3.one * (renderRadius * 2f);

            if (_sphereMaterial != null)
            {
                SetMaterialColor(_sphereMaterial, color);
            }

            if (showAxisLines && _axisLines != null)
            {
                CaptureHandleFrame();
                Vector3[] axes = { _frameX, _frameY, _frameZ };
                Color[] baseColors = { axisXColor, axisYColor, axisZColor };
                float renderAxis = RenderAxisLength();
                float widthScale = Mathf.Clamp(renderRadius / 0.02f, 0.4f, 3f);
                for (int i = 0; i < _axisLines.Length; i++)
                {
                    LineRenderer line = _axisLines[i];
                    if (line == null)
                    {
                        continue;
                    }

                    float length = renderAxis;
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
                    line.startWidth = axisLineWidth * widthScale * (_axisLock == i ? 2.2f : 1f);
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

            if (_limitStop)
            {
                return limitStopColor;
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
            sb.AppendLine("三轴 " + (axisFrameMode == AxisFrameMode.RosWorldZUp ? "X朝外/Y朝左/Z朝上" : "X刀轴/Y右/Z上")
                + "   ball=" + ScreenRadiusPixels.ToString("F0") + "px/" + (_renderRadius * 2000f).ToString("F0") + "mm"
                + "   limit=±" + (ikController != null ? ikController.JointLimitDeg.ToString("0.#") : "30") + "°/轴"
                + (_limitStop ? "   ★已顶到限位" : string.Empty));
            sb.AppendLine("ghost updates " + _planUpdates + "   enterExecute " + _enterExecuteCalls
                + "   dispatchCalls " + DispatchCalls + "   planEnter " + _planEnterAttempts);
            sb.AppendLine("LMB 拖动 | X/Y/Z 或 1/2/3 锁轴 | Shift 精调 | R 回当前 | Esc 取消");

            _hudText.text = sb.ToString();
            Color textColor = _reachable ? new Color(0.9f, 0.95f, 1f, 1f) : new Color(1f, 0.55f, 0.5f, 1f);
            _hudText.color = textColor;
        }

        // ================================================================== 指针/UI 工具

        /// <summary>
        /// 拾取判断：射线到球心距离 ≤ 拾取半径即可抓（不依赖 Collider）。
        /// 旧实现走 Physics.RaycastAll 且跳过 isTrigger —— 而手柄球本身是 trigger，
        /// 于是永远返回 false（"开启 IK 后拖不动"的根因）。
        /// 现在纯解析判定：球再小也按 minPickPixels 保证点得中。
        /// </summary>
        private bool IsPointerOnHandle(Vector2 screenPoint)
        {
            if (playCamera == null || _sphereGo == null || !_sphereGo.activeSelf)
            {
                return false;
            }

            Vector3 center = _sphereGo.transform.position;
            float renderRadius = RenderRadius();
            float worldRadius = Mathf.Max(0.0005f, renderRadius) * Mathf.Max(1f, pickRadiusScale);
            float minWorldRadius = minPickPixels / Mathf.Max(1f, PixelsPerMeter(center));
            float pickRadius = Mathf.Max(worldRadius, minWorldRadius);

            Ray ray = playCamera.ScreenPointToRay(screenPoint);
            Vector3 toCenter = center - ray.origin;
            float along = Vector3.Dot(toCenter, ray.direction);
            if (along <= 0f)
            {
                return false;
            }

            Vector3 closest = ray.origin + ray.direction * along;
            return (closest - center).sqrMagnitude <= pickRadius * pickRadius;
        }

        /// <summary>世界点所在处的屏幕像素/米（透视按视锥算，正交按 size 算）。</summary>
        private float PixelsPerMeter(Vector3 worldPoint)
        {
            if (playCamera == null || Screen.height <= 0)
            {
                return 1000f;
            }

            if (playCamera.orthographic)
            {
                return Screen.height / Mathf.Max(0.0001f, playCamera.orthographicSize * 2f);
            }

            float distance = Mathf.Max(0.01f, Vector3.Dot(worldPoint - playCamera.transform.position, playCamera.transform.forward));
            float worldHeight = 2f * distance * Mathf.Tan(playCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return Screen.height / Mathf.Max(0.0001f, worldHeight);
        }

        /// <summary>
        /// 只把"可交互控件"（按钮/滑条/折叠头等 Selectable）算作 UI 遮挡。
        /// 全屏背景 Image 不再吞掉射线 —— 否则页面打开时手柄永远点不到。
        /// </summary>
        private bool IsPointerOverUi(Vector2 screenPoint)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var data = new PointerEventData(eventSystem) { position = screenPoint };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(data, results);
            for (int i = 0; i < results.Count; i++)
            {
                GameObject hit = results[i].gameObject;
                if (hit == null)
                {
                    continue;
                }

                if (hit.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }

                if (_hudCanvasRoot != null && hit.transform.IsChildOf(_hudCanvasRoot.transform))
                {
                    return true;
                }
            }

            return false;
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