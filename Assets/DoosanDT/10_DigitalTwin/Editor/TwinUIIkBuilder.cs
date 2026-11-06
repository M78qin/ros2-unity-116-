// Play 模式末端法兰拖动（IK ghost 预览）构建器 + 自动自检（A2 级别 1，新增文件）。
//
// 菜单：
//   DigitalTwin/UI/7  Attach IK Handle
//       给当前 active 相机挂 TwinPlayIkHandle，自动解析 RobotIkController / RobotModelController /
//       TwinCommandController 引用；把 RobotIkController 调成本地预览所需参数
//       （enableIk=true, enableRuntimeIk=true, autoSolveOnDrag=false, tolerance=1.5 mm），保存场景。
//       打印固定格式：[IK] attached=1 ... result=PASS/FAIL。幂等：无变化时 saved=no。
//
//   DigitalTwin/UI/18 Play Ik Handle Check (auto-exit)
//       Edit 下幂等自备（缺组件先挂）→ 自动 Enter Play → 稳定 2.5 s → 相机对准 TCP 并靠近
//       → 用组件测试 API 拖动 +10 mm → 断言 ghost 关节变化 / 位置误差 <2 mm / dispatch=0 / HUD 文本
//       → 轴锁、单帧 5 mm 上限、AABB、不可达变红、Esc/按钮回当前 → 截图 5 张 → 自动 Exit Play
//       → 写 Logs/ui_ik_check.txt + 证据目录副本，打印 [IK-CHECK] RESULT: PASS/FAIL。
//
// 说明：本工具只做"运行期行为证据"；不改数据链脚本、不改 TwinRuntimeProfile；
//       相机瞄准/靠近只作用于 Play 会话（退出 Play 自动还原），不保存进场景。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DigitalTwin.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUIIkBuilder
    {
        public const string EvidenceDir = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-04_ui_play\ik";
        public const string ShotDir = EvidenceDir + @"\shots";
        public const string EvidenceLogDir = EvidenceDir + @"\logs";

        private const string LogPath = "Logs/ui_ik_check.txt";
        private const string TracePath = "Logs/ui_ik_check_trace.txt";
        private const string ArmedKey = "DigitalTwin.IkCheck.Armed";
        private const string PhaseKey = "DigitalTwin.IkCheck.Phase";
        private const string PhaseStartKey = "DigitalTwin.IkCheck.PhaseStart";
        private const string LogTag = "[IK]";
        private const string CheckTag = "[IK-CHECK]";

        private const double SettleSeconds = 2.5d;
        private const double ShotWaitSeconds = 0.9d;
        private const double HardTimeoutSeconds = 90d;

        private static readonly string[] ShotNames =
        {
            "ik18_s01_handle_idle.png",
            "ik18_s02_dragging_handle.png",
            "ik18_s03_after_drag_ghost.png",
            "ik18_s04_unreachable_red.png",
            "ik18_s05_cancelled_idle.png",
            "ik18_s06_3d_only_ui_hidden.png"
        };

        private static readonly StringBuilder Lines = new StringBuilder();
        private static readonly List<string> Failures = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static bool _subscribed;
        private static bool _headerWritten;
        private static double _playStartedAt = -1d;
        private static int _phase;

        private static Camera _camera;
        private static TwinPlayIkHandle _handle;
        private static RobotIkController _ik;
        private static RobotModelController _model;
        private static TwinCommandController _command;
        private static TwinPlayCameraRig _rig;

        private static Vector3 _tcp0;
        private static float[] _joints0 = Array.Empty<float>();
        private static float _lookSensitivity = 0.12f;
        private static bool _aimDone;
        private static double _aimStartedAt = -1d;

        private static Vector3 _dragBase;
        private static Vector3 _dragTarget;
        private static Color _dragColorDuring;
        private static string _dragHudDuring = string.Empty;
        private static float _dragStepMax;
        private static bool _unreachableOk;
        private static bool _cancelOk;

        static TwinUIIkBuilder()
        {
            EditorApplication.update += Tick;
        }

        // ================================================================== UI/7

        [MenuItem("DigitalTwin/UI/7 Attach IK Handle", false, 7)]
        public static void AttachIkHandleMenu()
        {
            string summary;
            bool ok = AttachCore(true, out summary);
            if (ok)
            {
                Debug.Log(LogTag + " " + summary);
            }
            else
            {
                Debug.LogError(LogTag + " " + summary);
            }
        }

        /// <summary>挂载/校准 Play IK 手柄；saveScene=true 时保存当前场景。幂等。</summary>
        private static bool AttachCore(bool saveScene, out string summary)
        {
            var failures = new List<string>();
            Camera camera = ResolveActiveCamera(out string cameraPath);
            if (camera == null)
            {
                summary = "attached=0 result=FAIL (active camera missing)";
                return false;
            }

            RobotIkController ik = FindSceneComponent<RobotIkController>();
            if (ik == null)
            {
                failures.Add("RobotIkController missing");
            }

            RobotModelController model = ik != null ? ik.RobotModel : FindSceneComponent<RobotModelController>();
            if (model == null)
            {
                failures.Add("RobotModelController missing");
            }

            TwinCommandController command = FindSceneComponent<TwinCommandController>();
            if (command == null)
            {
                failures.Add("TwinCommandController missing (ghost target cache unavailable)");
            }

            bool changed = false;
            TwinPlayIkHandle handle = camera.GetComponent<TwinPlayIkHandle>();
            int attached = 0;
            if (handle == null)
            {
                handle = camera.gameObject.AddComponent<TwinPlayIkHandle>();
                changed = true;
            }

            if (handle != null)
            {
                attached = 1;
                changed |= ConfigureHandle(handle, camera, ik, command);
            }

            if (ik != null)
            {
                changed |= ConfigureIkController(ik);
            }

            int handleCount = CountSceneComponents<TwinPlayIkHandle>();

            Scene scene = SceneManager.GetActiveScene();
            bool saved = false;
            if (saveScene && (changed || scene.isDirty))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                try
                {
                    EditorSceneManager.SaveScene(scene);
                    saved = true;
                }
                catch (Exception e)
                {
                    failures.Add("scene save failed: " + e.GetType().Name + ": " + e.Message);
                }
            }

            bool pass = failures.Count == 0 && handle != null && ik != null && model != null && command != null;

            var sb = new StringBuilder();
            sb.Append("attached=").Append(attached);
            sb.Append(" camera=").Append(cameraPath);
            sb.Append(" handle=").Append(TwinSceneLookup.PathOf(handle != null ? handle.transform : null));
            sb.Append(" ik=").Append(ik != null ? TwinSceneLookup.PathOf(ik.transform) : "<missing>");
            sb.Append(" robotModel=").Append(model != null ? TwinSceneLookup.PathOf(model.transform) : "<missing>");
            sb.Append(" command=").Append(command != null ? TwinSceneLookup.PathOf(command.transform) : "<missing>");
            if (ik != null)
            {
                sb.Append(" enableIk=").Append(ik.EnableIk);
                sb.Append(" runtimeIk=").Append(ik.EnableRuntimeIk);
                sb.Append(" autoSolve=").Append(ik.AutoSolveOnDrag);
            }

            if (handle != null)
            {
                SerializedObject hso = new SerializedObject(handle);
                SerializedProperty dispatch = hso.FindProperty("allowRobotDispatch");
                SerializedProperty tolerance = hso.FindProperty("maxStepMeters");
                sb.Append(" allowDispatch=").Append(dispatch != null && dispatch.boolValue);
                sb.Append(" maxStep=").Append(tolerance != null ? tolerance.floatValue.ToString("F4") : "?");
            }

            sb.Append(" handles=").Append(handleCount);
            sb.Append(" saved=").Append(saved ? "yes" : "no");
            sb.Append(" result=").Append(pass ? "PASS" : "FAIL");
            if (failures.Count > 0)
            {
                sb.Append(" failures=[").Append(string.Join("; ", failures.ToArray())).Append("]");
            }

            summary = sb.ToString();
            return pass;
        }

        private static bool ConfigureHandle(TwinPlayIkHandle handle, Camera camera, RobotIkController ik, TwinCommandController command)
        {
            var so = new SerializedObject(handle);
            bool changed = false;
            changed |= SetObjectReference(so, "playCamera", camera);
            changed |= SetObjectReference(so, "ikController", ik);
            changed |= SetObjectReference(so, "commandController", command);
            changed |= SetSerializedBool(so, "enableHandle", true);
            changed |= SetSerializedBool(so, "showHandleVisual", true);
            changed |= SetSerializedBool(so, "showHud", true);
            changed |= SetSerializedBool(so, "allowRobotDispatch", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (changed)
            {
                EditorUtility.SetDirty(handle);
            }

            return changed;
        }

        private static bool ConfigureIkController(RobotIkController ik)
        {
            var so = new SerializedObject(ik);
            bool changed = false;
            changed |= SetSerializedBool(so, "enableIk", true);
            changed |= SetSerializedBool(so, "enableRuntimeIk", true);
            changed |= SetSerializedBool(so, "previewSolutionOnModel", true);
            changed |= SetSerializedBool(so, "applyBestEffortWhenUnsolved", true);
            changed |= SetSerializedBool(so, "autoSolveOnDrag", false);
            changed |= SetSerializedFloat(so, "positionToleranceMeters", 0.0015f);
            changed |= SetSerializedFloat(so, "maxSolveTimeMs", 10f);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (changed)
            {
                EditorUtility.SetDirty(ik);
            }

            return changed;
        }

        private static bool SetObjectReference(SerializedObject so, string propertyName, UnityEngine.Object target)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            if (property.objectReferenceValue == target)
            {
                return false;
            }

            property.objectReferenceValue = target;
            return true;
        }

        private static bool SetSerializedBool(SerializedObject so, string propertyName, bool value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            {
                return false;
            }

            if (property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            return true;
        }

        private static bool SetSerializedFloat(SerializedObject so, string propertyName, float value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
            {
                return false;
            }

            if (Mathf.Abs(property.floatValue - value) < 0.0000001f)
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }

        private static Camera ResolveActiveCamera(out string path)
        {
            path = "<none>";
            Camera camera = Camera.main;
            if (camera != null && (!camera.gameObject.activeInHierarchy || !camera.enabled))
            {
                camera = null;
            }

            if (camera == null)
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera candidate = cameras[i];
                    if (candidate == null || !candidate.gameObject.activeInHierarchy || !candidate.enabled)
                    {
                        continue;
                    }

                    camera = candidate;
                    break;
                }
            }

            if (camera != null)
            {
                path = TwinSceneLookup.PathOf(camera.transform);
            }

            return camera;
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            T[] found = UnityEngine.Object.FindObjectsOfType<T>(true);
            T fallback = null;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = found[i];
                }

                if (found[i].gameObject.activeInHierarchy)
                {
                    return found[i];
                }
            }

            return fallback;
        }

        private static int CountSceneComponents<T>() where T : Component
        {
            T[] found = UnityEngine.Object.FindObjectsOfType<T>(true);
            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        // ================================================================== UI/18

        [MenuItem("DigitalTwin/UI/18 Play Ik Handle Check (auto-exit)", false, 18)]
        public static void StartIkCheck()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(CheckTag + " already in/entering play mode; run UI/18 again after exiting");
                return;
            }

            if (SessionState.GetBool(ArmedKey, false))
            {
                Debug.LogWarning(CheckTag + " stale armed flag cleared (previous run did not finish)");
                Trace("stale armed flag cleared");
                SessionState.SetBool(ArmedKey, false);
            }

            string summary;
            if (!AttachCore(true, out summary))
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, "preparing failed: " + summary + Environment.NewLine + "RESULT: FAIL" + Environment.NewLine);
                CopyLogToEvidence();
                Debug.LogError(CheckTag + " preparing FAIL: " + summary);
                return;
            }

            Lines.Clear();
            Failures.Clear();
            Errors.Clear();
            _headerWritten = false;
            _playStartedAt = -1d;
            _phase = 0;
            _camera = null;
            _handle = null;
            _ik = null;
            _model = null;
            _command = null;
            _rig = null;
            _aimDone = false;
            _aimStartedAt = -1d;
            _unreachableOk = false;
            _cancelOk = false;
            _dragHudDuring = string.Empty;

            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, "preparing play IK handle check  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                + "[prep] " + summary + Environment.NewLine);
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetFloat(PhaseStartKey, -1f);
            Trace("StartIkCheck armed, entering play");
            Debug.Log(CheckTag + " armed, entering play mode; prep: " + summary);
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ArmedKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (!_subscribed)
            {
                Application.logMessageReceived += OnLog;
                _subscribed = true;
            }

            double now = EditorApplication.timeSinceStartup;
            float startFloat = SessionState.GetFloat(PhaseStartKey, -1f);
            double start = startFloat < 0f ? -1d : startFloat;
            if (_playStartedAt < 0d)
            {
                _playStartedAt = now;
                Trace("first tick in play, phase=" + _phase);
            }

            if (now - _playStartedAt > HardTimeoutSeconds)
            {
                AddFailure("hard timeout " + HardTimeoutSeconds + "s reached in phase " + _phase);
                Finish();
                return;
            }

            if (start < 0d)
            {
                start = now;
                SessionState.SetFloat(PhaseStartKey, (float)start);
            }

            double elapsed = now - start;
            _phase = SessionState.GetInt(PhaseKey, 0);

            try
            {
                switch (_phase)
                {
                    case 0:
                        if (elapsed < SettleSeconds)
                        {
                            return;
                        }

                        RunCollect();
                        SetPhase(1, now);
                        return;

                    case 1:
                        RunAim();
                        if (_aimDone)
                        {
                            PrepareShot(ShotNames[0], "idle handle + HUD");
                            SetPhase(2, now);
                        }

                        return;

                    case 2:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        RunMainDrag();
                        PrepareShot(ShotNames[1], "dragging +10mm (handle amber)");
                        SetPhase(3, now);
                        return;

                    case 3:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        EndMainDragAndAssert();
                        PrepareShot(ShotNames[2], "after drag (ghost pose changed)");
                        SetPhase(4, now);
                        return;

                    case 4:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        RunGuardTests();
                        ReaimAtHandle();
                        SetPhase(44, now);
                        return;

                    case 44:
                        if (elapsed < 0.35d)
                        {
                            return;
                        }

                        PrepareShot(ShotNames[3], "unreachable target (handle red)");
                        SetPhase(40, now);
                        return;

                    case 40:
                        if (elapsed < 0.35d)
                        {
                            return;
                        }

                        HideUiFor3dShot();
                        SetPhase(41, now);
                        return;

                    case 41:
                        ReaimAtHandle();
                        SetPhase(43, now);
                        return;

                    case 43:
                        if (elapsed < 0.35d)
                        {
                            return;
                        }

                        PrepareShot(ShotNames[5], "3D only (fullscreen UI hidden for one shot)");
                        SetPhase(42, now);
                        return;

                    case 42:
                        RestoreUiAfter3dShot();
                        SetPhase(5, now);
                        return;

                    case 5:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        RunCancelAndButtonTest();
                        ReaimAtHandle();
                        SetPhase(50, now);
                        return;

                    case 50:
                        if (elapsed < 0.35d)
                        {
                            return;
                        }

                        PrepareShot(ShotNames[4], "cancelled / reset to current TCP");
                        SetPhase(6, now);
                        return;

                    case 6:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        Finish();
                        return;

                    default:
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                AddFailure("exception in phase " + _phase + ": " + e.GetType().Name + ": " + e.Message);
                Lines.AppendLine("stack: " + e.StackTrace);
                Finish();
            }
        }

        private static void SetPhase(int phase, double now)
        {
            SessionState.SetInt(PhaseKey, phase);
            SessionState.SetFloat(PhaseStartKey, (float)now);
            Trace("phase " + phase);
        }

        private static void Trace(string message)
        {
            try
            {
                Directory.CreateDirectory("Logs");
                File.AppendAllText(TracePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message
                    + "  isPlaying=" + EditorApplication.isPlaying + Environment.NewLine);
            }
            catch
            {
                // trace 失败不影响自检
            }
        }

        // ------------------------------------------------------------------ phase 0

        private static void RunCollect()
        {
            if (!_headerWritten)
            {
                _headerWritten = true;
                Lines.AppendLine("== Play IK Handle Check (UI/18)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Lines.AppendLine("unity=" + Application.unityVersion + " screen=" + Screen.width + "x" + Screen.height
                    + " isPlaying=" + Application.isPlaying);
                Lines.AppendLine("scene=" + SceneManager.GetActiveScene().name);
            }

            _camera = Camera.main;
            _handle = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();
            _ik = UnityEngine.Object.FindObjectOfType<RobotIkController>();
            _command = UnityEngine.Object.FindObjectOfType<TwinCommandController>();
            _rig = UnityEngine.Object.FindObjectOfType<TwinPlayCameraRig>();
            _model = _ik != null ? _ik.RobotModel : UnityEngine.Object.FindObjectOfType<RobotModelController>();

            string cameraPath = _camera != null ? TwinSceneLookup.PathOf(_camera.transform) : "<missing>";
            Lines.AppendLine("[env] camera=" + cameraPath + " handle=" + (_handle != null) + " ik=" + (_ik != null)
                + " model=" + (_model != null) + " command=" + (_command != null) + " rig=" + (_rig != null));

            bool handleOnCamera = _handle != null && _camera != null && _handle.transform == _camera.transform;
            LogItem("[0a] active 相机 + TwinPlayIkHandle 已挂",
                "camera=" + cameraPath + " handle=" + (_handle != null) + " onCamera=" + handleOnCamera,
                _camera != null && _handle != null && handleOnCamera);

            bool ikConfigured = _ik != null && _ik.EnableIk && _ik.EnableRuntimeIk && !_ik.AutoSolveOnDrag;
            LogItem("[0b] RobotIkController 已开启运行期 IK（本地预览）",
                "enableIk=" + (_ik != null && _ik.EnableIk) + " runtimeIk=" + (_ik != null && _ik.EnableRuntimeIk)
                + " autoSolveOnDrag=" + (_ik != null && _ik.AutoSolveOnDrag)
                + " tolerance=" + (ToleranceOf(_ik)).ToString("F4") + " m", ikConfigured);

            if (_ik == null)
            {
                AddFailure("[0b] RobotIkController missing");
                return;
            }

            // 无 ROS 数据时：先回到 live feedback，再读本地 TCP 起始位。
            _ik.ReleaseBackToLiveFeedback();
            if (!_ik.TryGetCurrentTcpPose(out Vector3 tcp, out _))
            {
                AddFailure("[0c] cannot read local TCP pose");
                return;
            }

            _tcp0 = tcp;
            _joints0 = _model != null ? _model.ReadCurrentDisplayDegrees() : Array.Empty<float>();
            Lines.AppendLine("[env] tcp0=" + Fmt(tcp) + " joints0=[" + FmtArray(_joints0) + "]"
                + " authority=" + (_model != null ? _model.CurrentControlAuthority.ToString() : "<null>"));

            bool handleReady = _handle != null && _handle.Initialized && _handle.HudCreated;
            LogItem("[0c] 手柄/HUD 已初始化（无 ROS 本地 TCP 起始位）",
                "initialized=" + (_handle != null && _handle.Initialized) + " hud=" + (_handle != null && _handle.HudCreated)
                + " tcp0=" + Fmt(tcp) + " handlePos=" + (_handle != null ? Fmt(_handle.HandleWorldPosition) : "<null>"),
                handleReady);

            bool visible = _handle != null && _handle.HandleVisualVisible;
            Vector3 screenPos = _handle != null ? _handle.HandleScreenPosition : Vector3.zero;
            bool onScreen = screenPos.z > 0f && screenPos.x > 0f && screenPos.x < Screen.width
                && screenPos.y > 0f && screenPos.y < Screen.height;
            Lines.AppendLine("[env] handle visible=" + visible + " screen=" + Fmt2(screenPos)
                + " onScreen(before aim)=" + onScreen + "（aim 后复查，见 [0d]）");

            string hud = _handle != null ? _handle.HudTextValue : string.Empty;
            LogItem("[0e] HUD 含 dispatch=disabled（本地预览）",
                "hudContains=" + hud.Contains("dispatch=disabled") + " allowDispatch="
                + (_handle != null && _handle.AllowRobotDispatch), hud.Contains("dispatch=disabled"));

            _lookSensitivity = GetPrivateFloat(_rig, "lookSensitivity", 0.12f);
            Lines.AppendLine("[env] rig yaw=" + (_rig != null ? _rig.Yaw.ToString("F2") : "n/a")
                + " pitch=" + (_rig != null ? _rig.Pitch.ToString("F2") : "n/a")
                + " pos=" + (_rig != null ? Fmt(_rig.RigPosition) : "n/a")
                + " lookSensitivity=" + _lookSensitivity.ToString("F3"));

            _aimStartedAt = EditorApplication.timeSinceStartup;
            Trace("collect done: tcp0=" + Fmt(tcp) + " hud=" + ((_handle != null && !string.IsNullOrEmpty(_handle.HudTextValue)) ? "yes" : "no"));
        }

        // ------------------------------------------------------------------ phase 1：相机对准 TCP 并靠近

        private static void RunAim()
        {
            if (_aimDone)
            {
                return;
            }

            if (_rig == null || _handle == null)
            {
                _aimDone = true;
                AddFailure("[aim] camera rig or handle missing; screenshots may not frame TCP");
                return;
            }

            if (EditorApplication.timeSinceStartup - _aimStartedAt > 25d)
            {
                _aimDone = true;
                Lines.AppendLine("[aim] TIMEOUT after 25s; proceed with current camera pose");
                return;
            }

            Vector3 camPos = _rig.RigPosition;
            Vector3 toTcp = _tcp0 - camPos;
            float flatDistance = new Vector2(toTcp.x, toTcp.z).magnitude;
            float distance = Mathf.Max(0.001f, toTcp.magnitude);
            float desiredYaw = Mathf.Atan2(toTcp.x, toTcp.z) * Mathf.Rad2Deg;
            float desiredPitch = Mathf.Asin(Mathf.Clamp(-toTcp.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            float yawError = Mathf.DeltaAngle(_rig.Yaw, desiredYaw);
            float pitchError = Mathf.DeltaAngle(_rig.Pitch, desiredPitch);

            var input = new TwinPlayCameraRig.TwinPlayInput();
            if (Mathf.Abs(yawError) > 1.5f || Mathf.Abs(pitchError) > 1.5f)
            {
                SetPrivateField(_rig, "_targetYaw", desiredYaw);
                SetPrivateField(_rig, "_targetPitch", desiredPitch);
                _rig.StepForTest(input, 0.02f);
                return;
            }

            input.rightMouseHeld = true;
            if (flatDistance > 4.0f)
            {
                input.moveAxis = new Vector2(0f, 1f);
                if (_rig.MoveSpeed < 8f)
                {
                    input.scrollNotches = 12f;
                }

                _rig.StepForTest(input, 0.02f);
                return;
            }

            _aimDone = true;
            Lines.AppendLine("[aim] done: cameraPos=" + Fmt(_rig.RigPosition) + " yaw=" + _rig.Yaw.ToString("F2")
                + " pitch=" + _rig.Pitch.ToString("F2") + " flatDistToTcp=" + flatDistance.ToString("F2")
                + " speed=" + _rig.MoveSpeed.ToString("F2"));
            LogItem("[0f] 相机已对准 TCP（Play 会话内，不保存场景）",
                "flatDist=" + flatDistance.ToString("F2") + " m yaw=" + _rig.Yaw.ToString("F2"),
                flatDistance <= 4.5f && Mathf.Abs(Mathf.DeltaAngle(_rig.Yaw, desiredYaw)) < 3f);

            Vector3 shotScreen = _handle.HandleScreenPosition;
            bool shotOnScreen = shotScreen.z > 0f && shotScreen.x > 0f && shotScreen.x < Screen.width
                && shotScreen.y > 0f && shotScreen.y < Screen.height;
            LogItem("[0d] 手柄球体/三轴可见且在屏幕内（对准后）",
                "visible=" + _handle.HandleVisualVisible + " screen=" + Fmt2(shotScreen) + " onScreen=" + shotOnScreen,
                _handle.HandleVisualVisible && shotOnScreen);

            LogRenderDiagnostics();
        }

        // ------------------------------------------------------------------ phase 2：主拖动 +10 mm

        private static void RunMainDrag()
        {
            if (_handle == null)
            {
                AddFailure("[1] handle missing, drag test skipped");
                return;
            }

            _handle.ResetToCurrentForTest();
            bool began = _handle.BeginDragForTest();
            LogItem("[1a] BeginDragForTest（组件测试入口）", "began=" + began, began);
            if (!began)
            {
                return;
            }

            _dragBase = _handle.TargetPosition;
            float maxStep = _handle.MaxStepMeters;
            Lines.AppendLine("[drag] base=" + Fmt(_dragBase) + " targetBase=" + Fmt(_dragBase)
                + " maxStep=" + (maxStep * 1000f).ToString("F2") + "mm");

            // 10 × 1 mm，每步都走 SetTargetPose -> TrySolve -> UpdatePlanTarget（ghost）
            for (int i = 1; i <= 10; i++)
            {
                _handle.DragToForTest(_dragBase + Vector3.right * (0.001f * i));
                _dragStepMax = Mathf.Max(_dragStepMax, _handle.LastStepMeters);
            }

            _dragTarget = _handle.TargetPosition;
            _dragColorDuring = _handle.CurrentHandleColor;
            _dragHudDuring = _handle.HudTextValue;

            Trace("drag done: moved=" + ((_dragTarget - _dragBase).magnitude * 1000f).ToString("F2") + "mm"
                + " err=" + (_handle.PositionErrorMeters * 1000f).ToString("F2") + "mm"
                + " status=" + _handle.IkStatusText);

            float moved = Vector3.Distance(_dragBase, _dragTarget);
            Lines.AppendLine("[drag] base=" + Fmt(_dragBase) + " target=" + Fmt(_dragTarget)
                + " moved=" + (moved * 1000f).ToString("F2") + "mm"
                + " maxStepObserved=" + (_dragStepMax * 1000f).ToString("F2") + "mm"
                + " posErr=" + (_handle.PositionErrorMeters * 1000f).ToString("F2") + "mm"
                + " iter=" + _handle.Iterations + " status=" + _handle.IkStatusText
                + " handleColor=#" + ColorUtility.ToHtmlStringRGB(_dragColorDuring));
        }

        private static void EndMainDragAndAssert()
        {
            if (_handle == null)
            {
                return;
            }

            _handle.EndDragForTest();

            float moved = Vector3.Distance(_dragBase, _dragTarget);
            LogItem("[1] 模拟拖动 +10 mm：目标位移 10±1 mm",
                "moved=" + (moved * 1000f).ToString("F2") + "mm", Mathf.Abs(moved - 0.010f) <= 0.001f);

            LogItem("[1b] 单帧位移不超过 5 mm 护栏",
                "maxStepObserved=" + (_dragStepMax * 1000f).ToString("F2") + "mm (limit "
                + (_handle.MaxStepMeters * 1000f).ToString("F2") + "mm)",
                _dragStepMax <= _handle.MaxStepMeters + 0.0001f);

            float[] solution = _handle.LastSolutionDegreesCopy();
            float jointDelta = JointDelta(_joints0, solution);
            bool solutionChanged = solution.Length > 0 && jointDelta > 0.05f;
            LogItem("[2] ghost 关节发生变化（GetLastSolutionDegreesCopy 非空且与初值不同）",
                "solutionLen=" + solution.Length + " jointMaxDelta=" + jointDelta.ToString("F3") + "deg"
                + " hasSolution=" + _handle.HasSolution, solutionChanged);

            float errorMeters = _handle.PositionErrorMeters;
            bool tcpRead = _handle.TryGetCurrentTcpPose(out Vector3 tcpNow, out _);
            float tcpToTarget = tcpRead ? Vector3.Distance(tcpNow, _dragTarget) : float.PositiveInfinity;
            LogItem("[3] 位置误差 < 2 mm（目标 vs 当前 TCP）",
                "lastPosErr=" + (errorMeters * 1000f).ToString("F3") + "mm"
                + " tcpToTarget=" + (tcpToTarget * 1000f).ToString("F3") + "mm"
                + " status=" + _handle.IkStatusText + " iter=" + _handle.Iterations,
                errorMeters < 0.002f && tcpRead && tcpToTarget < 0.0025f);

            bool limitsOk = _handle.JointLimitsOk && JointsWithinLimits(solution);
            LogItem("[4] 关节限位未被突破（复用 RobotModelController 限位）",
                "componentLimitsOk=" + _handle.JointLimitsOk + " recomputed=" + JointsWithinLimits(solution),
                limitsOk);

            LogItem("[5] 未下发：EnterExecute=0 且 dispatchCalls=0",
                "allowRobotDispatch=" + _handle.AllowRobotDispatch + " enterExecuteCalls=" + _handle.EnterExecuteCalls
                + " dispatchCalls=" + _handle.DispatchCalls + " planEnter=" + _handle.PlanEnterAttempts
                + " planUpdates=" + _handle.PlanUpdates,
                !_handle.AllowRobotDispatch && _handle.EnterExecuteCalls == 0 && _handle.DispatchCalls == 0);

            LogItem("[6] HUD 文本含 dispatch=disabled",
                "hudContains=" + _handle.HudTextValue.Contains("dispatch=disabled"), _handle.HudTextValue.Contains("dispatch=disabled"));

            float dragColorDiff = ColorDiff(_dragColorDuring, _handle.DragColorValue);
            LogItem("[7] 拖动中手柄为琥珀色（dragColor）",
                "during=#" + ColorUtility.ToHtmlStringRGB(_dragColorDuring) + " expected=#"
                + ColorUtility.ToHtmlStringRGB(_handle.DragColorValue) + " delta=" + dragColorDiff.ToString("F4"),
                dragColorDiff < 0.05f);

            bool hudNumberSeen = _dragHudDuring.Contains("Δpos") && _dragHudDuring.Contains("iter");
            LogItem("[8] 拖动 HUD 实时显示 Δpos/iter", "hudHasDelta=" + hudNumberSeen, hudNumberSeen);

            Lines.AppendLine("[drag] hud during drag first lines: "
                + FirstLines(_dragHudDuring, 3));
            Trace("main drag assertions done failures=" + Failures.Count);
        }

        // ------------------------------------------------------------------ phase 4：护栏测试

        private static void RunGuardTests()
        {
            if (_handle == null || _model == null)
            {
                AddFailure("[9] handle/model missing, guard tests skipped");
                return;
            }

            // [9] 单帧位移上限：一次要求 +100 mm，实际只走 5 mm
            _handle.ResetToCurrentForTest();
            Vector3 stepBase = _handle.TargetPosition;
            _handle.BeginDragForTest();
            _handle.DragToForTest(stepBase + Vector3.right * 0.1f);
            float stepObserved = _handle.LastStepMeters;
            Vector3 stepMoved = _handle.TargetPosition - stepBase;
            LogItem("[9] 单帧位移上限 5 mm（要求 +100 mm 只走 5 mm）",
                "requested=100.00mm observed=" + (stepObserved * 1000f).ToString("F2") + "mm"
                + " targetMoved=" + (stepMoved.magnitude * 1000f).ToString("F2") + "mm",
                Mathf.Abs(stepObserved - 0.005f) < 0.0005f && stepMoved.magnitude <= 0.0055f);
            _handle.EndDragForTest();
            _handle.ResetToCurrentForTest();

            // [10] X 轴锁定：斜向输入只产生 X 位移
            Vector3 axisBase = _handle.TargetPosition;
            _handle.BeginDragForTest();
            _handle.SetAxisLockForTest(0);
            for (int i = 1; i <= 10; i++)
            {
                _handle.DragToForTest(axisBase + new Vector3(0.001f * i, 0.001f * i, 0f));
            }

            Vector3 axisMoved = _handle.TargetPosition - axisBase;
            bool axisOk = Mathf.Abs(axisMoved.y) < 0.00001f && Mathf.Abs(axisMoved.z) < 0.00001f && axisMoved.x > 0.004f;
            LogItem("[10] X 轴锁定：斜向输入只沿 X 移动",
                "moved=" + Fmt4(axisMoved) + " axisLock=" + _handle.AxisLock, axisOk);
            _handle.EndDragForTest();
            _handle.SetAxisLockForTest(-1);
            _handle.ResetToCurrentForTest();

            // [11] 工作空间 AABB：要求 +10 m，被 clamp 在 ±0.5 m 内
            Vector3 boundsBase = _handle.TargetPosition;
            _handle.BeginDragForTest();
            _handle.DragDirectForTest(boundsBase + Vector3.right * 10f);
            Vector3 boundsTarget = _handle.TargetPosition;
            Vector3 center = _handle.WorkspaceCenter;
            Vector3 half = _handle.WorkspaceHalfExtentsValue;
            bool inBounds = Mathf.Abs(boundsTarget.x - center.x) <= half.x + 0.001f
                && Mathf.Abs(boundsTarget.y - center.y) <= half.y + 0.001f
                && Mathf.Abs(boundsTarget.z - center.z) <= half.z + 0.001f;
            LogItem("[11] 工作空间 AABB 限制（±" + half.x.ToString("F2") + " m）",
                "requested=+10000mm target=" + Fmt(boundsTarget) + " center=" + Fmt(center)
                + " clamped=" + _handle.WorkspaceClamped, inBounds && _handle.WorkspaceClamped);
            _handle.EndDragForTest();
            _handle.ResetToCurrentForTest();

            // [12] 不可达：临时放大 AABB，向上 1.9 m（超出臂展）→ 手柄变红 + HUD UNREACHABLE
            _handle.SetWorkspaceHalfExtentsForTest(new Vector3(2.5f, 2.5f, 2.5f));
            _handle.ResetToCurrentForTest();
            Vector3 unreachableBase = _handle.TargetPosition;
            _handle.BeginDragForTest();
            _handle.DragDirectForTest(unreachableBase + Vector3.up * 1.9f);
            bool unreachable = !_handle.Reachable;
            float redDiff = ColorDiff(_handle.CurrentHandleColor, _handle.UnreachableColorValue);
            string hud = _handle.HudTextValue;
            _unreachableOk = unreachable && redDiff < 0.05f && hud.Contains("UNREACHABLE");
            LogItem("[12] 不可达：手柄变红 + HUD 提示",
                "reachable=" + _handle.Reachable + " status=" + _handle.IkStatusText
                + " posErr=" + (_handle.PositionErrorMeters * 1000f).ToString("F1") + "mm"
                + " color=#" + ColorUtility.ToHtmlStringRGB(_handle.CurrentHandleColor)
                + " expectedRed=#" + ColorUtility.ToHtmlStringRGB(_handle.UnreachableColorValue)
                + " colorDelta=" + redDiff.ToString("F4") + " hudUnreachable=" + hud.Contains("UNREACHABLE"),
                _unreachableOk);
            _handle.EndDragForTest();

            // 清理：恢复默认 AABB / 目标 / home 姿态（仅 Play 会话内）
            _handle.SetWorkspaceHalfExtentsForTest(new Vector3(0.5f, 0.5f, 0.5f));
            _handle.ResetToCurrentForTest();
            try
            {
                _model.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
                _model.ApplyHome0Pose(false, RobotModelController.PoseSource.Home, true);
                _model.ReleaseToLiveFeedback();
                Lines.AppendLine("[cleanup] home pose re-applied for screenshots (Play-only, no dispatch)");
            }
            catch (Exception e)
            {
                Lines.AppendLine("[cleanup] home pose restore skipped: " + e.Message);
            }

            _handle.ResetToCurrentForTest();

            // 截图用：保留「不可达→红色手柄 + HUD UNREACHABLE」状态（仅 Play 会话内，phase 5 取消时复位）
            _handle.SetWorkspaceHalfExtentsForTest(new Vector3(2.5f, 2.5f, 2.5f));
            _handle.ResetToCurrentForTest();
            _handle.BeginDragForTest();
            _handle.DragDirectForTest(_handle.TargetPosition + Vector3.up * 1.9f);
            _handle.EndDragForTest();

            Trace("guard tests done failures=" + Failures.Count);
        }

        private static void RunCancelAndButtonTest()
        {
            if (_handle == null || _model == null)
            {
                AddFailure("[13] handle/model missing, cancel test skipped");
                return;
            }

            _handle.SetWorkspaceHalfExtentsForTest(new Vector3(0.5f, 0.5f, 0.5f));
            _handle.CancelForTest();
            bool released = _model.CurrentControlAuthority == RobotModelController.ControlAuthority.LiveFeedback;
            int cancelPlanCalls = _handle.CancelPlanCalls;
            _cancelOk = released && _handle.DispatchCalls == 0;
            LogItem("[13] Esc/取消：ReleaseBackToLiveFeedback + 不下发",
                "authority=" + _model.CurrentControlAuthority + " cancelPlanCalls=" + cancelPlanCalls
                + " dispatchCalls=" + _handle.DispatchCalls + " enterExecuteCalls=" + _handle.EnterExecuteCalls,
                _cancelOk);

            // HUD「回当前」按钮：至少 2 个按钮，点第一个（回当前）应把目标复位到当前 TCP
            Button[] buttons = _handle.GetComponentsInChildren<Button>(true);
            Button resetButton = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                TMP_Text label = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (label != null && label.text.Contains("回当前"))
                {
                    resetButton = buttons[i];
                    break;
                }
            }

            bool buttonOk = false;
            if (resetButton != null)
            {
                Vector3 before = _handle.TargetPosition;
                resetButton.onClick.Invoke();
                bool tcpRead = _handle.TryGetCurrentTcpPose(out Vector3 tcpNow, out _);
                float distance = tcpRead ? Vector3.Distance(tcpNow, _handle.TargetPosition) : float.PositiveInfinity;
                buttonOk = tcpRead && distance < 0.0005f;
                LogItem("[14] HUD「回当前」按钮可用",
                    "buttons=" + buttons.Length + " before=" + Fmt(before) + " tcp=" + Fmt(tcpNow)
                    + " target=" + Fmt(_handle.TargetPosition) + " dist=" + (distance * 1000f).ToString("F3") + "mm",
                    buttonOk && buttons.Length >= 2);
            }
            else
            {
                LogItem("[14] HUD「回当前」按钮可用", "resetButton=<not found> buttons=" + buttons.Length, false);
            }

            bool idleColor = ColorDiff(_handle.CurrentHandleColor, _handle.IdleColorValue) < 0.05f;
            string hud = _handle.HudTextValue;
            LogItem("[15] 复位后手柄回到空闲青色且 HUD 不再 UNREACHABLE",
                "color=#" + ColorUtility.ToHtmlStringRGB(_handle.CurrentHandleColor)
                + " expected=#" + ColorUtility.ToHtmlStringRGB(_handle.IdleColorValue)
                + " hudUnreachable=" + hud.Contains("UNREACHABLE") + " dispatch=" + hud.Contains("dispatch=disabled"),
                idleColor && !hud.Contains("UNREACHABLE") && hud.Contains("dispatch=disabled"));
        }

        // ------------------------------------------------------------------ 截图 / 结束

        private static readonly System.Collections.Generic.List<GameObject> HiddenUi = new System.Collections.Generic.List<GameObject>();

        private static void HideUiFor3dShot()
        {
            HiddenUi.Clear();
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            GameObject hudCanvas = _handle != null ? _handle.HudCanvasRoot : null;
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !canvas.isActiveAndEnabled || canvas.gameObject == hudCanvas)
                {
                    continue;
                }

                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    || canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    canvas.gameObject.SetActive(false);
                    HiddenUi.Add(canvas.gameObject);
                }
            }

            Lines.AppendLine("[3d] 为单张 3D 截图临时隐藏全屏 UI canvas=" + HiddenUi.Count + "（保留 HUD，Play 会话内，截图后立即恢复）");
            Trace("hidden UI canvases=" + HiddenUi.Count);
        }

        private static void RestoreUiAfter3dShot()
        {
            if (HiddenUi.Count == 0)
            {
                return;
            }

            int restored = 0;
            for (int i = 0; i < HiddenUi.Count; i++)
            {
                if (HiddenUi[i] != null)
                {
                    HiddenUi[i].SetActive(true);
                    restored++;
                }
            }

            Lines.AppendLine("[3d] 全屏 UI canvas 已恢复=" + restored);
            Trace("restored UI canvases=" + restored);
            HiddenUi.Clear();
        }

        private static void ReaimAtHandle()
        {
            if (_rig == null || _handle == null)
            {
                return;
            }

            Vector3 toTarget = _handle.TargetPosition - _rig.RigPosition;
            float distance = Mathf.Max(0.001f, toTarget.magnitude);
            SetPrivateField(_rig, "_targetYaw", Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg);
            SetPrivateField(_rig, "_targetPitch",
                Mathf.Asin(Mathf.Clamp(-toTarget.y / distance, -1f, 1f)) * Mathf.Rad2Deg);
            _rig.StepForTest(new TwinPlayCameraRig.TwinPlayInput(), 0.02f);
            Lines.AppendLine("[aim] re-aim at handle target=" + Fmt(_handle.TargetPosition)
                + " yaw=" + _rig.Yaw.ToString("F2") + " pitch=" + _rig.Pitch.ToString("F2")
                + " handleScreen=" + Fmt2(_handle.HandleScreenPosition));
        }

        private static void LogRenderDiagnostics()
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                AddFailure("[0g] 相机缺失，无法做 3D 渲染诊断");
                return;
            }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            int inView = 0;
            int robotInView = 0;
            Transform robotRoot = _model != null ? _model.transform : null;
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds))
                {
                    continue;
                }

                inView++;
                if (robotRoot != null && r.transform.IsChildOf(robotRoot))
                {
                    robotInView++;
                }
            }

            bool sphereVisible = _handle != null && _handle.HandleSphereVisibleToCamera;
            Lines.AppendLine("[render] camera=" + camera.name + " active=" + camera.isActiveAndEnabled
                + " pixel=" + camera.pixelWidth + "x" + camera.pixelHeight
                + " targetTexture=" + (camera.targetTexture != null ? camera.targetTexture.name : "null")
                + " cullingMask=0x" + camera.cullingMask.ToString("X8")
                + " renderersInFrustum=" + inView + " robotRenderersInFrustum=" + robotInView
                + " handleSphereRendererVisibleLastFrame=" + sphereVisible
                + " handleSphereExists=" + (_handle != null && _handle.HandleSphereExists));
            Trace("render diag: inView=" + inView + " robotInView=" + robotInView + " sphereVisible=" + sphereVisible);

            if (_handle != null && _handle.HandleSphereExists)
            {
                var sphere = _handle.HandleSphereObject;
                if (sphere != null)
                {
                    var mr = sphere.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                    {
                        Lines.AppendLine("[render] sphere pos=" + Fmt(sphere.transform.position)
                            + " scale=" + Fmt(sphere.transform.lossyScale)
                            + " layer=" + sphere.layer
                            + " enabled=" + mr.enabled
                            + " shader=" + mr.sharedMaterial.shader.name
                            + " color=" + ColorUtility.ToHtmlStringRGB(mr.sharedMaterial.color)
                            + " isVisible=" + mr.isVisible);
                    }
                }
            }
        }

        private static void PrepareShot(string fileName, string note)
        {
            Canvas.ForceUpdateCanvases();
            string path = Path.Combine(ShotDir, fileName);
            Directory.CreateDirectory(ShotDir);
            Vector3 screenPos = _handle != null ? _handle.HandleScreenPosition : Vector3.zero;
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                string rtPath = Path.Combine(ShotDir, Path.GetFileNameWithoutExtension(fileName) + "_rt.png");
                bool rtOk = CaptureCameraFallback(rtPath);
                long gameBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                long rtBytes = File.Exists(rtPath) ? new FileInfo(rtPath).Length : 0;
                Lines.AppendLine("[shot] " + fileName + "  note=" + note
                    + " handleScreen=" + Fmt2(screenPos)
                    + " gameBytes=" + gameBytes + " rtBytes=" + rtBytes + " rtOk=" + rtOk
                    + " handleColor=" + (_handle != null ? "#" + ColorUtility.ToHtmlStringRGB(_handle.CurrentHandleColor) : "n/a")
                    + " hud=" + (_handle != null ? FirstLines(_handle.HudTextValue, 2) : "n/a"));
            }
            catch (Exception e)
            {
                AddFailure("[shot] " + fileName + " capture failed: " + e.Message);
            }
        }

        private static void Finish()
        {
            RestoreUiAfter3dShot();
            if (_subscribed)
            {
                Application.logMessageReceived -= OnLog;
                _subscribed = false;
            }

            int primaryOk = 0;
            var shotInfo = new StringBuilder();
            for (int i = 0; i < ShotNames.Length; i++)
            {
                string path = Path.Combine(ShotDir, ShotNames[i]);
                long length = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (length > 5000)
                {
                    primaryOk++;
                    shotInfo.Append(ShotNames[i]).Append("=").Append(length).Append("B ");
                }
                else
                {
                    string fallback = Path.Combine(ShotDir, Path.GetFileNameWithoutExtension(ShotNames[i]) + "_rtfallback.png");
                    if (CaptureCameraFallback(fallback))
                    {
                        long fallbackLength = new FileInfo(fallback).Length;
                        shotInfo.Append(ShotNames[i]).Append("=MISSING->rtfallback(").Append(fallbackLength).Append("B) ");
                    }
                    else
                    {
                        shotInfo.Append(ShotNames[i]).Append("=MISSING ");
                    }
                }
            }

            LogItem("[16] 截图 >= 3 张（Game 视图，含 HUD）", "primaryOk=" + primaryOk + "/" + ShotNames.Length
                + " details=" + shotInfo.ToString(), primaryOk >= 3);

            int relevantErrors = 0;
            for (int i = 0; i < Errors.Count; i++)
            {
                string e = Errors[i];
                if (e.Contains("TwinPlayIkHandle") || e.Contains("RobotIkController") || e.Contains("TwinCommandController")
                    || e.Contains("NullReferenceException") || e.Contains("MissingReferenceException")
                    || e.Contains("ArgumentException") || e.Contains("TwinIkHandle"))
                {
                    relevantErrors++;
                }
            }

            LogItem("[17] 运行期无相关 Error/Exception", "totalErrors=" + Errors.Count
                + " relevant=" + relevantErrors, relevantErrors == 0);
            for (int i = 0; i < Errors.Count && i < 10; i++)
            {
                Lines.AppendLine("[error] " + Errors[i]);
            }

            // 收尾：把拖动/求解状态释放，保证退出 Play 后场景无残留（Play 退出本身会还原场景）。
            try
            {
                if (_handle != null)
                {
                    _handle.CancelForTest();
                }

                if (_model != null)
                {
                    _model.ReleaseToLiveFeedback();
                }
            }
            catch (Exception e)
            {
                Lines.AppendLine("[cleanup] " + e.Message);
            }

            bool pass = Failures.Count == 0;
            Lines.AppendLine();
            Lines.AppendLine("---- 失败项 (" + Failures.Count + ") ----");
            if (Failures.Count == 0)
            {
                Lines.AppendLine("(none)");
            }
            else
            {
                for (int i = 0; i < Failures.Count; i++)
                {
                    Lines.AppendLine("- " + Failures[i]);
                }
            }

            Lines.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, Lines.ToString());
            CopyLogToEvidence();
            Debug.Log(CheckTag + " RESULT: " + (pass ? "PASS" : "FAIL") + " failures=" + Failures.Count
                + " log=" + LogPath + " shots=" + ShotDir);

            SessionState.SetBool(ArmedKey, false);
            SessionState.SetInt(PhaseKey, -1);
            SessionState.SetFloat(PhaseStartKey, -1f);
            EditorApplication.isPlaying = false;
        }

        private static bool CaptureCameraFallback(string path)
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return false;
            }

            RenderTexture previous = camera.targetTexture;
            RenderTexture active = RenderTexture.active;
            RenderTexture rt = null;
            Texture2D texture = null;
            try
            {
                int width = Mathf.Max(320, camera.pixelWidth);
                int height = Mathf.Max(180, camera.pixelHeight);
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return File.Exists(path) && new FileInfo(path).Length > 1000;
            }
            catch (Exception e)
            {
                Debug.LogWarning(CheckTag + " rt fallback failed: " + e.Message);
                return false;
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        // ------------------------------------------------------------------ 工具

        private static void LogItem(string name, string detail, bool pass)
        {
            Lines.AppendLine("[item] " + name + " | " + detail + " | result=" + (pass ? "PASS" : "FAIL"));
            if (!pass)
            {
                AddFailure(name + " (" + detail + ")");
            }
        }

        private static void AddFailure(string message)
        {
            Failures.Add(message);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                if (condition != null && condition.Length > 500)
                {
                    condition = condition.Substring(0, 500);
                }

                Errors.Add(condition ?? "<null>");
            }
        }

        private static void CopyLogToEvidence()
        {
            try
            {
                Directory.CreateDirectory(EvidenceLogDir);
                string source = Path.Combine(Directory.GetCurrentDirectory(), LogPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(EvidenceLogDir, "ui_ik_check.txt"), true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(CheckTag + " evidence copy failed: " + e.Message);
            }
        }

        private static float ToleranceOf(RobotIkController ik)
        {
            if (ik == null)
            {
                return -1f;
            }

            SerializedObject so = new SerializedObject(ik);
            SerializedProperty p = so.FindProperty("positionToleranceMeters");
            return p != null ? p.floatValue : -1f;
        }

        private static bool JointsWithinLimits(float[] solution)
        {
            if (_model == null || solution == null || solution.Length == 0)
            {
                return false;
            }

            int count = Mathf.Min(solution.Length, _model.JointCount);
            for (int i = 0; i < count; i++)
            {
                Vector2 limits = _model.GetJointLimitsDeg(i);
                if (solution[i] < limits.x - 0.01f || solution[i] > limits.y + 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float JointDelta(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0)
            {
                return 0f;
            }

            int count = Mathf.Min(a.Length, b.Length);
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(a[i] - b[i]));
            }

            return max;
        }

        private static float GetPrivateFloat(object target, string fieldName, float fallback)
        {
            if (target == null)
            {
                return fallback;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(float))
            {
                return fallback;
            }

            object value = field.GetValue(target);
            return value is float f ? f : fallback;
        }

        private static void SetPrivateField(object target, string fieldName, float value)
        {
            if (target == null)
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
            }
        }

        private static float ColorDiff(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
        }

        private static string Fmt2(Vector3 v)
        {
            return "(" + v.x.ToString("F0") + "," + v.y.ToString("F0") + ",z" + v.z.ToString("F1") + ")";
        }

        private static string Fmt4(Vector3 v)
        {
            return "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + "," + v.z.ToString("F4") + ")";
        }

        private static string FmtArray(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "<empty>";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(values[i].ToString("F2"));
            }

            return sb.ToString();
        }

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "<empty>";
            }

            string[] parts = text.Replace("\r", string.Empty).Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length && i < count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(parts[i].Trim());
            }

            return sb.ToString();
        }
    }
}