// Play 交互层构建器 + 自动自检（R3，新增；不改既有 UI/1/2/3/4/5/12/15/16 行为）。
//
// 菜单：
//   DigitalTwin/UI/6  Attach Play Interaction Rig
//       给当前 active 相机挂 TwinPlayCameraRig + TwinPlayRayVisual，写入命中 LayerMask
//       （Default/场景层，排除 UI(5) 与 Ignore Raycast(2)），给 UI 按钮背景加
//       TwinUIGradientHover（强度/开关取自 TwinUiSettings.asset），保存场景。
//       打印固定格式：[PLAY] transforms=... mask=... result=PASS/FAIL
//
//   DigitalTwin/UI/17 Play Interaction Check (auto-exit)
//       Edit 下自备（若组件缺失先自动挂）→ 自动 Enter Play → 等待场景稳定 →
//       逐项实测并打印实测值：W 位移、右键拖拽 yaw、三点射线变化、hover 顶点色差、
//       按下色/缩放、命中层射线颜色、UI 命中时世界射线 false → 截图 5 张 →
//       自动退出 Play → 写 logs/ui_play_check.txt 与证据目录副本，输出 RESULT: PASS/FAIL。
//
// 说明：本工具只做"运行期行为证据"，不改数据链脚本、不改 TwinRuntimeProfile。
// 截图走 ScreenCapture（Game 视图，含 uGUI 叠加）；若 Game 视图不可抓取则回退到
// 相机 RenderTexture（仅 3D，不含 Overlay UI），并在日志中如实标注 fallback。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUIPlayBuilder
    {
        public const string UiRootPath = "30_界面与交互/UI_二期面板";
        public const string EvidenceDir = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-04_ui_play";
        public const string ShotDir = EvidenceDir + @"\shots";
        public const string EvidenceLogDir = EvidenceDir + @"\logs";

        private const string LogPath = "Logs/ui_play_check.txt";
        private const string ArmedKey = "DigitalTwin.PlayCheck.Armed";
        private const string PhaseKey = "DigitalTwin.PlayCheck.Phase";
        private const string PhaseStartKey = "DigitalTwin.PlayCheck.PhaseStart";

        private const double SettleSeconds = 2.5d;
        private const double ShotWaitSeconds = 0.9d;
        private const double HardTimeoutSeconds = 45d;

        private static readonly string[] ShotNames =
        {
            "play17_s01_after_walk.png",
            "play17_s02_hit_object.png",
            "play17_s03_ui_hover_gradient.png",
            "play17_s04_ui_pressed.png",
            "play17_s05_miss_sky.png"
        };

        private static readonly StringBuilder Lines = new StringBuilder();
        private static readonly List<string> Failures = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static bool _subscribed;
        private static bool _headerWritten;
        private static double _playStartedAt = -1d;
        private static int _phase;

        private static Camera _camera;
        private static TwinPlayCameraRig _rig;
        private static TwinPlayRayVisual _ray;
        private static TwinUIButton _button;
        private static Vector2 _scenePoint;
        private static Vector2 _missPoint;
        private static Vector2 _uiPoint;
        private static string _sceneHitName = "<none>";
        private static Vector3 _startPosition;
        private static float _startYaw;

        static TwinUIPlayBuilder()
        {
            EditorApplication.update += Tick;
        }

        // ------------------------------------------------------------------ UI/6

        [MenuItem("DigitalTwin/UI/6 Attach Play Interaction Rig", false, 6)]
        public static void AttachPlayInteractionRigMenu()
        {
            string summary;
            bool ok = AttachCore(true, out summary);
            if (ok)
            {
                Debug.Log("[PLAY] " + summary);
            }
            else
            {
                Debug.LogError("[PLAY] " + summary);
            }
        }

        /// <summary>挂载/校准 Play 交互层；saveScene=true 时保存当前场景。幂等。</summary>
        private static bool AttachCore(bool saveScene, out string summary)
        {
            var failures = new List<string>();
            string cameraPath;
            Camera camera = ResolveActiveCamera(out cameraPath);
            if (camera == null)
            {
                summary = "transforms=0 mask=0 result=FAIL (active camera missing)";
                return false;
            }

            int mask;
            string maskDesc;
            mask = BuildHitMask(out maskDesc);

            bool changed = false;
            TwinPlayCameraRig rig = camera.GetComponent<TwinPlayCameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<TwinPlayCameraRig>();
                changed = true;
            }

            if (rig.ActiveCamera != camera || rig.MoveRoot != camera.transform)
            {
                rig.Configure(camera, camera.transform, 2f);
                changed = true;
            }

            TwinPlayRayVisual ray = camera.GetComponent<TwinPlayRayVisual>();
            if (ray == null)
            {
                ray = camera.gameObject.AddComponent<TwinPlayRayVisual>();
                changed = true;
            }

            if (ray.RayCamera != camera || ray.HitLayers.value != mask || !ray.ShowHud)
            {
                ray.ConfigureForEditor(camera, mask, true);
                changed = true;
            }

            TwinUiSettings settings = TwinUIR3Builder.LoadSettings(true);
            if (settings == null)
            {
                failures.Add("TwinUiSettings missing");
            }

            int buttonCount = 0;
            int gradientCount = 0;
            GameObject uiRoot = GameObject.Find(UiRootPath);
            if (uiRoot == null)
            {
                failures.Add("UI root missing");
            }
            else if (settings != null)
            {
                TwinUIButton[] buttons = uiRoot.GetComponentsInChildren<TwinUIButton>(true);
                for (int i = 0; i < buttons.Length; i++)
                {
                    TwinUIButton button = buttons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    buttonCount++;
                    Image background = button.Background;
                    if (background == null)
                    {
                        background = button.GetComponent<Image>();
                    }

                    if (background == null)
                    {
                        continue;
                    }

                    TwinUIGradientHover gradient = background.GetComponent<TwinUIGradientHover>();
                    if (gradient == null)
                    {
                        gradient = background.gameObject.AddComponent<TwinUIGradientHover>();
                        changed = true;
                    }

                    if (gradient.EnabledBySettings != settings.enableHoverGradient
                        || Mathf.Abs(gradient.Strength - settings.hoverGradientStrength) > 0.0001f)
                    {
                        changed = true;
                    }

                    gradient.EnabledBySettings = settings.enableHoverGradient;
                    gradient.Strength = settings.hoverGradientStrength;
                    button.ApplyTheme();
                    gradientCount++;
                }
            }

            Scene scene = SceneManager.GetActiveScene();
            if (saveScene && (changed || scene.isDirty))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                try
                {
                    EditorSceneManager.SaveScene(scene);
                }
                catch (Exception e)
                {
                    failures.Add("scene save failed: " + e.GetType().Name + ": " + e.Message);
                }
            }

            bool pass = failures.Count == 0 && rig != null && ray != null;
            var sb = new StringBuilder();
            sb.Append("transforms=").Append(rig != null && ray != null ? 2 : (rig != null || ray != null ? 1 : 0));
            sb.Append(" mask=").Append(mask);
            sb.Append(" camera=").Append(cameraPath);
            sb.Append(" gradientButtons=").Append(gradientCount).Append("/").Append(buttonCount);
            if (settings != null)
            {
                sb.Append(" hoverGradient=").Append(settings.enableHoverGradient ? "on" : "off")
                    .Append("/").Append(settings.hoverGradientStrength.ToString("F2"));
            }

            sb.Append(" layers=[").Append(maskDesc).Append("]");
            sb.Append(" saved=").Append(saveScene && (changed || scene.isDirty) ? "yes" : "no");
            sb.Append(" result=").Append(pass ? "PASS" : "FAIL");
            if (failures.Count > 0)
            {
                sb.Append(" failures=[").Append(string.Join("; ", failures.ToArray())).Append("]");
            }

            summary = sb.ToString();
            return pass;
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

        private static int BuildHitMask(out string description)
        {
            // P1b-1 射线 mask 收敛：仅 Default(0)，与 TwinPlayRayVisual.hitLayers 默认值同源。
            // 本场景所有可命中对象都在 layer 0（layer 1/4 无对象，2=IgnoreRaycast、5=UI 永不进 3D 射线），
            // 因此 mask 由 19 收敛到 1 不改变命中行为，只去掉无意义的层扫描。
            description = "0:Default; excluded=1:TransparentFX,2:IgnoreRaycast,4:Water,5:UI";
            return 1 << 0;
        }

        // ------------------------------------------------------------------ UI/17

        [MenuItem("DigitalTwin/UI/17 Play Interaction Check (auto-exit)", false, 17)]
        public static void StartPlayCheck()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[PLAY-CHECK] already in play mode; run UI/15/17 after exiting");
                return;
            }

            // Edit 下准备：组件/渐变/mask 缺失则补齐并保存场景。
            string summary;
            if (!AttachCore(true, out summary))
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, "preparing failed: " + summary + Environment.NewLine + "RESULT: FAIL" + Environment.NewLine);
                CopyLogToEvidence();
                Debug.LogError("[PLAY-CHECK] preparing FAIL: " + summary);
                return;
            }

            Lines.Clear();
            Failures.Clear();
            Errors.Clear();
            _headerWritten = false;
            _playStartedAt = -1d;
            _camera = null;
            _rig = null;
            _ray = null;
            _button = null;
            _scenePoint = _missPoint = _uiPoint = Vector2.zero;
            _sceneHitName = "<none>";

            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, "preparing play interaction check  "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                + "[prep] " + summary + Environment.NewLine);
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetFloat(PhaseStartKey, -1f);
            Debug.Log("[PLAY-CHECK] armed, entering play mode; prep: " + summary);
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
                        RunMotionChecks();
                        SetPhase(2, now);
                        return;

                    case 2:
                        RunRayChecks();
                        RunButtonGradientChecks();
                        SetPhase(3, now);
                        return;

                    case 3:
                    case 4:
                    case 5:
                    case 6:
                        if (elapsed < ShotWaitSeconds)
                        {
                            return;
                        }

                        RunShotPhase(_phase - 2);
                        SetPhase(_phase + 1, now);
                        return;

                    case 7:
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
        }

        // ------------------------------------------------------------------ 阶段实现

        private static void RunCollect()
        {
            if (!_headerWritten)
            {
                _headerWritten = true;
                Lines.AppendLine("== Play Interaction Check (UI/17)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Lines.AppendLine("unity=" + Application.unityVersion + " screen=" + Screen.width + "x" + Screen.height
                    + " isPlaying=" + Application.isPlaying);
                Lines.AppendLine("scene=" + SceneManager.GetActiveScene().name);
            }

            _camera = Camera.main;
            _rig = UnityEngine.Object.FindObjectOfType<TwinPlayCameraRig>();
            _ray = UnityEngine.Object.FindObjectOfType<TwinPlayRayVisual>();

            string cameraPath = _camera != null ? TwinSceneLookup.PathOf(_camera.transform) : "<missing>";
            Lines.AppendLine("[env] camera=" + cameraPath + " rig=" + (_rig != null) + " ray=" + (_ray != null));
            LogItem("[0a] active 相机与两个交互组件",
                "camera=" + cameraPath + " rig=" + (_rig != null) + " ray=" + (_ray != null),
                _camera != null && _rig != null && _ray != null);

            if (_rig != null)
            {
                _startPosition = _rig.RigPosition;
                _startYaw = WorldYaw();
                Lines.AppendLine("[env] startPos=" + Format(_startPosition) + " startYaw=" + _startYaw.ToString("F2")
                    + " moveSpeed=" + _rig.MoveSpeed.ToString("F2"));
            }

            if (_ray != null)
            {
                Lines.AppendLine("[env] ray mask=" + _ray.HitLayers.value + " maxDist=" + _ray.MaxDistance.ToString("F1")
                    + " hud=" + _ray.HudCreated + " alwaysOnTop=" + _ray.AlwaysOnTop);
                LogItem("[0d] 轻量 HUD（命中对象名+世界坐标+像素）", "created=" + _ray.HudCreated, _ray.HudCreated);
            }

            Component smartRaycaster = FindComponentByTypeName(_camera != null ? _camera.gameObject : null, "SmartRaycaster");
            bool smartDisabled = smartRaycaster == null || !((Behaviour)smartRaycaster).enabled;
            LogItem("[0b] 同相机 SmartRaycaster 已禁用（避免双控）",
                "found=" + (smartRaycaster != null) + " enabled="
                + (smartRaycaster == null ? "n/a" : ((Behaviour)smartRaycaster).enabled.ToString()),
                smartDisabled);

            LogItem("[0e] EventSystem 存在", "current=" + (EventSystem.current != null), EventSystem.current != null);

            int buttons = 0;
            int gradientButtons = 0;
            GameObject uiRoot = GameObject.Find(UiRootPath);
            if (uiRoot != null)
            {
                TwinUIButton[] all = uiRoot.GetComponentsInChildren<TwinUIButton>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null)
                    {
                        continue;
                    }

                    buttons++;
                    if (all[i].Gradient != null)
                    {
                        gradientButtons++;
                        if (_button == null && all[i].gameObject.activeInHierarchy && all[i].Background != null)
                        {
                            _button = all[i];
                        }
                    }
                }
            }

            Lines.AppendLine("[env] buttons=" + buttons + " withGradient=" + gradientButtons + " chosenButton="
                + (_button != null ? _button.gameObject.name : "<none>"));
            LogItem("[0c] 按钮已挂 TwinUIGradientHover", "buttons=" + buttons + " withGradient=" + gradientButtons,
                gradientButtons > 0 && _button != null);

            bool hasScene = TryFindScenePoint(out _scenePoint, out _sceneHitName);
            LogItem("[0f] 可命中场景点采样", "point=" + Format(_scenePoint) + " target=" + _sceneHitName, hasScene);

            bool hasMiss = TryFindMissPoint(out _missPoint);
            LogItem("[0g] 未命中（天空）采样点", "point=" + Format(_missPoint), hasMiss);

            bool hasUi = TryFindUiPoint(out _uiPoint);
            LogItem("[0h] UI 采样点（按钮中心）", "point=" + Format(_uiPoint)
                + " overUi=" + (hasUi ? IsUiAt(_uiPoint).ToString() : "n/a"), hasUi);
        }

        private static void RunMotionChecks()
        {
            if (_rig == null)
            {
                AddFailure("[1] rig missing, motion checks skipped");
                return;
            }

            // [1] 右键 + W 前进 1.2 s（60 × 0.02 s，确定性注入）
            Vector3 from = _rig.RigPosition;
            var forwardInput = new TwinPlayCameraRig.TwinPlayInput
            {
                moveAxis = new Vector2(0f, 2f),
                rightMouseHeld = true
            };
            for (int i = 0; i < 60; i++)
            {
                _rig.StepForTest(forwardInput, 0.02f);
            }

            Vector3 to = _rig.RigPosition;
            float moved = Vector3.Distance(from, to);
            Lines.AppendLine("[motion] W: from=" + Format(from) + " to=" + Format(to)
                + " dist=" + moved.ToString("F3") + " m (moveSpeed=" + _rig.MoveSpeed.ToString("F2") + ")");
            LogItem("[1] 右键+W 1.2s 位移 > 0.05 m", "dist=" + moved.ToString("F3") + " m", moved > 0.05f);

            // 走完立刻抓一张：与 baseline_before_play.png 同机位/同朝向下对比，可肉眼看位移
            PrepareShot(0, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, false);

            // [1b] 俯仰限位：持续抬头应被 clamp 在 +85°
            var pitchUp = new TwinPlayCameraRig.TwinPlayInput
            {
                rightMouseHeld = true,
                lookDelta = new Vector2(0f, 2500f)
            };
            for (int i = 0; i < 80; i++)
            {
                _rig.StepForTest(pitchUp, 0.02f);
            }

            float clampedPitch = _rig.Pitch;
            LogItem("[1b] 俯仰限位（|pitch| <= 85.5°）", "pitch=" + clampedPitch.ToString("F2") + " deg",
                Mathf.Abs(clampedPitch) <= 85.5f);

            // [2] 右键拖拽 yaw
            float yawBefore = WorldYaw();
            var dragInput = new TwinPlayCameraRig.TwinPlayInput
            {
                rightMouseHeld = true,
                lookDelta = new Vector2(320f, -120f)
            };
            for (int i = 0; i < 40; i++)
            {
                _rig.StepForTest(dragInput, 0.02f);
            }

            float yawAfter = WorldYaw();
            float yawDelta = Mathf.DeltaAngle(yawBefore, yawAfter);
            Lines.AppendLine("[motion] drag: yaw " + yawBefore.ToString("F2") + " -> " + yawAfter.ToString("F2")
                + " delta=" + yawDelta.ToString("F2") + " deg; rigYaw=" + _rig.Yaw.ToString("F2")
                + " targetYaw=" + _rig.TargetYaw.ToString("F2"));
            LogItem("[2] 右键拖拽后 yaw 变化 > 0.5°", "delta=" + yawDelta.ToString("F2") + " deg",
                Mathf.Abs(yawDelta) > 0.5f);

            // [2b] 滚轮调 moveSpeed
            float speedBefore = _rig.MoveSpeed;
            var scrollInput = new TwinPlayCameraRig.TwinPlayInput { scrollNotches = 2f };
            _rig.StepForTest(scrollInput, 0.02f);
            float speedAfter = _rig.MoveSpeed;
            LogItem("[2b] 滚轮调整 moveSpeed", "speed " + speedBefore.ToString("F2") + " -> " + speedAfter.ToString("F2")
                + " m/s", speedAfter > speedBefore + 0.01f);

            // 俯仰/拖拽会把镜头抬到接近垂直，还原初始位姿，便于后续射线与截图（位移/角度证据已在上面记录）
            _rig.RestoreInitialPose();
            Lines.AppendLine("[restore] pose back to start: pos=" + Format(_rig.RigPosition) + " yaw="
                + WorldYaw().ToString("F2") + " pitch=" + _rig.Pitch.ToString("F2"));
        }

        private static void RunRayChecks()
        {
            if (_ray == null)
            {
                AddFailure("[3] ray visual missing, ray checks skipped");
                return;
            }

            _ray.SetPressedOverride(false);

            // 相机位姿已还原到初始，重新采样场景/天空点（此前运动测试改变了视角）
            string sceneName2;
            bool hasScene2 = TryFindScenePoint(out _scenePoint, out sceneName2);
            bool hasMiss2 = TryFindMissPoint(out _missPoint);
            Lines.AppendLine("[ray] resample after restore: scene="
                + (hasScene2 ? Format(_scenePoint) + "->" + sceneName2 : "<none>")
                + " miss=" + (hasMiss2 ? Format(_missPoint) : "<none>"));

            // [3] 三个不同屏幕点的射线终点应不同（LineRenderer positions 不同）
            var samplePoints = new List<Vector2>();
            AppendIfDistinct(samplePoints, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            AppendIfDistinct(samplePoints, _scenePoint);
            AppendIfDistinct(samplePoints, _missPoint);
            AppendIfDistinct(samplePoints, new Vector2(Screen.width * 0.30f, Screen.height * 0.40f));
            AppendIfDistinct(samplePoints, new Vector2(Screen.width * 0.70f, Screen.height * 0.55f));

            var samples = new List<Vector3>();
            var usedPoints = new List<Vector2>();
            for (int i = 0; i < samplePoints.Count && samples.Count < 3; i++)
            {
                _ray.SetPointerOverride(samplePoints[i]);
                _ray.RefreshNow();
                if (!_ray.IsPointerOverUi && _ray.LinePositionCount >= 2)
                {
                    samples.Add(_ray.GetLinePosition(1));
                    usedPoints.Add(samplePoints[i]);
                }
            }

            float maxPairDelta = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                for (int j = i + 1; j < samples.Count; j++)
                {
                    maxPairDelta = Mathf.Max(maxPairDelta, Vector3.Distance(samples[i], samples[j]));
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < samples.Count; i++)
            {
                sb.Append("p").Append(i).Append("=").Append(Format(usedPoints[i]))
                    .Append("->end").Append(Format(samples[i])).Append(" ");
            }

            Lines.AppendLine("[ray] samples=" + samples.Count + " " + sb.ToString() + "maxPairDelta="
                + maxPairDelta.ToString("F3"));
            LogItem("[3] 三点鼠标位置 → LineRenderer 位置不同", "maxPairDelta=" + maxPairDelta.ToString("F3")
                + " m, samples=" + samples.Count + "/3", samples.Count >= 3 && maxPairDelta > 0.01f);

            // [3b] 未命中（天空）→ 未命中色
            _ray.SetPointerOverride(_missPoint);
            _ray.RefreshNow();
            RaycastHit missHit;
            bool missOverUi;
            bool missWorldHit = _ray.TryGetHit(out missHit, out missOverUi);
            Color missColor = _ray.CurrentLineColor;
            float missColorDelta = ColorDiff(missColor, _ray.MissColor);
            Lines.AppendLine("[ray] missPoint=" + Format(_missPoint) + " worldHit=" + missWorldHit
                + " overUi=" + missOverUi + " lineColor=" + Hex(missColor)
                + " expectedMissColor=" + Hex(_ray.MissColor) + " colorDelta=" + missColorDelta.ToString("F4"));
            LogItem("[3b] 未命中（天空）→ 未命中色", "worldHit=" + missWorldHit + " overUi=" + missOverUi
                + " colorDelta=" + missColorDelta.ToString("F4"),
                !missWorldHit && !missOverUi && missColorDelta < 0.02f);

            // [4] 命中层：射线变命中色 + world hit = true
            _ray.SetPointerOverride(_scenePoint);
            _ray.RefreshNow();
            RaycastHit hit;
            bool overUi;
            bool worldHit = _ray.TryGetHit(out hit, out overUi);
            Color sceneColor = _ray.CurrentLineColor;
            float sceneColorDelta = ColorDiff(sceneColor, _ray.HitObjectColor);
            int hitLayer = worldHit ? hit.collider.gameObject.layer : -1;
            Lines.AppendLine("[ray] scenePoint=" + Format(_scenePoint) + " worldHit=" + worldHit
                + " overUi=" + overUi + " hit=" + (worldHit ? hit.collider.name : "<none>")
                + " layer=" + hitLayer + " lineColor=" + Hex(sceneColor) + " expectedHitColor=" + Hex(_ray.HitObjectColor)
                + " colorDelta=" + sceneColorDelta.ToString("F4") + " worldRayActive=" + _ray.WorldRayActive);
            LogItem("[4] 命中场景层 → 射线为命中色且 layer 非 UI",
                "hit=" + (worldHit ? hit.collider.name : "<none>") + " layer=" + hitLayer
                + " colorDelta=" + sceneColorDelta.ToString("F4"),
                worldHit && !overUi && hitLayer != 5 && sceneColorDelta < 0.02f && _ray.WorldRayActive);

            // [4b] 按下色不同
            _ray.SetPressedOverride(true);
            _ray.RefreshNow();
            Color pressedColor = _ray.CurrentLineColor;
            float pressedDelta = ColorDiff(pressedColor, _ray.PressedColor);
            _ray.SetPressedOverride(false);
            _ray.RefreshNow();
            LogItem("[4b] 按下时射线为按下色", "pressed=" + Hex(pressedColor) + " expected=" + Hex(_ray.PressedColor)
                + " delta=" + pressedDelta.ToString("F4"), pressedDelta < 0.02f);

            // [5] UI 命中：世界射线关闭 + TryGetHit world=false
            _ray.SetPointerOverride(_uiPoint);
            _ray.RefreshNow();
            RaycastHit uiHit;
            bool uiOverUi;
            bool uiWorldHit = _ray.TryGetHit(out uiHit, out uiOverUi);
            Color uiColor = _ray.CurrentLineColor;
            float uiColorDelta = ColorDiff(uiColor, _ray.HitUiColor);
            Lines.AppendLine("[ray] uiPoint=" + Format(_uiPoint) + " overUi=" + uiOverUi + " worldHit=" + uiWorldHit
                + " worldRayActive=" + _ray.WorldRayActive + " lineColor=" + Hex(uiColor)
                + " expectedUiColor=" + Hex(_ray.HitUiColor) + " colorDelta=" + uiColorDelta.ToString("F4")
                + " hud=" + _ray.HudTextValue);
            LogItem("[5] UI 命中 → 世界射线 false + UI 色",
                "overUi=" + uiOverUi + " worldHit=" + uiWorldHit + " worldRayActive=" + _ray.WorldRayActive
                + " colorDelta=" + uiColorDelta.ToString("F4"),
                uiOverUi && !uiWorldHit && !_ray.WorldRayActive && uiColorDelta < 0.02f);

            _ray.SetPointerOverride(_scenePoint);
            _ray.SetPressedOverride(false);
            _ray.RefreshNow();
        }

        private static void RunButtonGradientChecks()
        {
            if (_button == null)
            {
                AddFailure("[6] no TwinUIButton with TwinUIGradientHover found");
                return;
            }

            TwinUIGradientHover gradient = _button.Gradient;
            if (gradient == null)
            {
                AddFailure("[6] gradient component missing on " + _button.gameObject.name);
                return;
            }

            _button.ApplyTheme();
            Canvas.ForceUpdateCanvases();
            float idleSpread = gradient.LastSpread;
            Color idleTop = gradient.LastTopColor;
            LogItem("[6a] 非 hover 为纯色（顶点色差≈0）", "spread=" + idleSpread.ToString("F4")
                + " top=" + Hex(idleTop) + " active=" + gradient.LastGradientActive, idleSpread < 0.001f);

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                AddFailure("[6] EventSystem missing, cannot inject pointer events");
                return;
            }

            var pointer = new PointerEventData(eventSystem)
            {
                position = _uiPoint,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };

            ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Canvas.ForceUpdateCanvases();
            float hoverSpread = gradient.LastSpread;
            Color hoverTop = gradient.LastTopColor;
            Color hoverBottom = gradient.LastBottomColor;
            float topBottomDiff = ColorDiff(hoverTop, hoverBottom);
            LogItem("[7] 按钮 hover 顶点色差 > 0.02", "spread=" + hoverSpread.ToString("F4")
                + " top=" + Hex(hoverTop) + " bottom=" + Hex(hoverBottom)
                + " top-bottom=" + topBottomDiff.ToString("F4") + " active=" + gradient.LastGradientActive,
                hoverSpread > 0.02f && topBottomDiff > 0.02f && gradient.LastGradientActive);

            Color hoverImageColor = _button.Background != null ? _button.Background.color : Color.white;

            ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            Canvas.ForceUpdateCanvases();
            Color pressedTop = gradient.LastTopColor;
            float pressDiff = ColorDiff(pressedTop, hoverTop);
            float scale = _button.transform.localScale.x;
            Color pressedImageColor = _button.Background != null ? _button.Background.color : Color.white;
            LogItem("[8] 按下更亮 + 轻微缩放", "hoverTop=" + Hex(hoverTop) + " pressedTop=" + Hex(pressedTop)
                + " diff=" + pressDiff.ToString("F4") + " scale=" + scale.ToString("F4")
                + " hoverFill=" + Hex(hoverImageColor) + " pressedFill=" + Hex(pressedImageColor)
                + " fillDiff=" + ColorDiff(hoverImageColor, pressedImageColor).ToString("F4"),
                pressDiff > 0.01f && scale < 0.999f);

            ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Canvas.ForceUpdateCanvases();
            Lines.AppendLine("[button] restored scale=" + _button.transform.localScale.x.ToString("F3")
                + " hovered=" + _button.IsHovered + " pressed=" + _button.IsPressed);
        }

        private static void RunShotPhase(int shotIndex)
        {
            switch (shotIndex)
            {
                case 0:
                    PrepareShot(0, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, false);
                    break;
                case 1:
                    PrepareShot(1, _scenePoint, false, false);
                    break;
                case 2:
                    PrepareShot(2, _uiPoint, true, false);
                    break;
                case 3:
                    PrepareShot(3, _uiPoint, true, true);
                    break;
                default:
                    PrepareShot(4, _missPoint, false, false);
                    break;
            }
        }

        private static void PrepareShot(int shotIndex, Vector2 pointer, bool hoverButton, bool pressButton)
        {
            if (_ray != null)
            {
                _ray.SetPointerOverride(pointer);
                _ray.SetPressedOverride(false);
                _ray.RefreshNow();
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && _button != null)
            {
                var data = new PointerEventData(eventSystem)
                {
                    position = pointer,
                    button = PointerEventData.InputButton.Left,
                    clickCount = 1
                };

                ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerExitHandler);
                if (hoverButton)
                {
                    ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerEnterHandler);
                }

                if (pressButton)
                {
                    ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerDownHandler);
                }
            }

            Canvas.ForceUpdateCanvases();
            string path = Path.Combine(ShotDir, ShotNames[shotIndex]);
            Directory.CreateDirectory(ShotDir);
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                Lines.AppendLine("[shot] " + ShotNames[shotIndex] + " requested pointer=" + Format(pointer)
                    + " hover=" + hoverButton + " press=" + pressButton
                    + " lineColor=" + (_ray != null ? Hex(_ray.CurrentLineColor) : "n/a")
                    + " hud=" + (_ray != null ? _ray.HudTextValue : "n/a"));
            }
            catch (Exception e)
            {
                AddFailure("[shot] " + ShotNames[shotIndex] + " capture failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ 结束

        private static void Finish()
        {
            if (_subscribed)
            {
                Application.logMessageReceived -= OnLog;
                _subscribed = false;
            }

            // 截图核对（截图在帧末落盘，可能略滞后；这里先等最近一帧）
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
                    // 回退：相机 RenderTexture（仅 3D，不含 Overlay UI），文件名加 _rtfallback
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

            LogItem("[9] 截图 >= 4 张（Game 视图，含 UI）", "primaryOk=" + primaryOk + "/" + ShotNames.Length
                + " details=" + shotInfo.ToString(), primaryOk >= 4);

            int relevantErrors = 0;
            for (int i = 0; i < Errors.Count; i++)
            {
                string e = Errors[i];
                if (e.Contains("SmartRaycaster") || e.Contains("TwinPlayCameraRig") || e.Contains("TwinPlayRayVisual")
                    || e.Contains("TwinUIGradientHover") || e.Contains("TwinUIButton")
                    || e.Contains("NullReferenceException") || e.Contains("MissingReferenceException"))
                {
                    relevantErrors++;
                }
            }

            LogItem("[10] 运行期无相关 Error/Exception", "totalErrors=" + Errors.Count
                + " relevant=" + relevantErrors, relevantErrors == 0);
            for (int i = 0; i < Errors.Count && i < 10; i++)
            {
                Lines.AppendLine("[error] " + Errors[i]);
            }

            // 清理运行期状态
            try
            {
                if (_ray != null)
                {
                    _ray.ClearOverrides();
                }

                if (_button != null && EventSystem.current != null)
                {
                    var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                    ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(_button.gameObject, data, ExecuteEvents.pointerExitHandler);
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
            Debug.Log("[PLAY-CHECK] RESULT: " + (pass ? "PASS" : "FAIL") + " items=" + Failures.Count
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
                Debug.LogWarning("[PLAY-CHECK] rt fallback failed: " + e.Message);
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
                    File.Copy(source, Path.Combine(EvidenceLogDir, "ui_play_check.txt"), true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PLAY-CHECK] evidence copy failed: " + e.Message);
            }
        }

        private static float WorldYaw()
        {
            Transform t = _rig != null ? _rig.transform : (_camera != null ? _camera.transform : null);
            return t == null ? 0f : t.eulerAngles.y;
        }

        private static Component FindComponentByTypeName(GameObject go, string typeName)
        {
            if (go == null)
            {
                return null;
            }

            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name == typeName)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static bool IsUiAt(Vector2 screenPoint)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            var data = new PointerEventData(EventSystem.current) { position = screenPoint };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count > 0;
        }

        private static bool TryRaycastWorld(Vector2 screenPoint, out RaycastHit hit)
        {
            hit = default;
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return false;
            }

            LayerMask mask = _ray != null ? _ray.HitLayers : (1 << 0); // P1b-1 与 BuildHitMask 默认值一致
            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out hit, 200f, mask.value, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return hit.distance > 0.2f;
        }

        private static bool TryFindScenePoint(out Vector2 point, out string targetName)
        {
            point = Vector2.zero;
            targetName = "<none>";
            var candidates = new List<Vector2>();
            for (int i = 1; i <= 6; i++)
            {
                for (int j = 1; j <= 6; j++)
                {
                    candidates.Add(new Vector2(Screen.width * (0.1f + 0.13f * i), Screen.height * (0.15f + 0.12f * j)));
                }
            }

            candidates.Add(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsUiAt(candidates[i]))
                {
                    continue;
                }

                RaycastHit hit;
                if (TryRaycastWorld(candidates[i], out hit))
                {
                    point = candidates[i];
                    targetName = hit.collider.name + " (layer " + hit.collider.gameObject.layer + ")";
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindMissPoint(out Vector2 point)
        {
            point = Vector2.zero;
            var candidates = new List<Vector2>
            {
                new Vector2(Screen.width * 0.5f, Screen.height * 0.96f),
                new Vector2(Screen.width * 0.9f, Screen.height * 0.9f),
                new Vector2(Screen.width * 0.1f, Screen.height * 0.9f),
                new Vector2(Screen.width * 0.96f, Screen.height * 0.5f)
            };

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsUiAt(candidates[i]))
                {
                    continue;
                }

                RaycastHit hit;
                if (!TryRaycastWorld(candidates[i], out hit))
                {
                    point = candidates[i];
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindUiPoint(out Vector2 point)
        {
            point = Vector2.zero;
            if (_button == null || _button.Background == null)
            {
                return false;
            }

            RectTransform rect = _button.Background.rectTransform;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            point = RectTransformUtility.WorldToScreenPoint(uiCamera, rect.TransformPoint(rect.rect.center));
            return IsUiAt(point);
        }

        private static float ColorDiff(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }

        private static void AppendIfDistinct(List<Vector2> points, Vector2 candidate)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(points[i], candidate) < 12f)
                {
                    return;
                }
            }

            points.Add(candidate);
        }

        private static string Format(Vector2 p)
        {
            return "(" + p.x.ToString("F0") + "," + p.y.ToString("F0") + ")";
        }

        private static string Format(Vector3 p)
        {
            return "(" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2") + ")";
        }

        private static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }
    }
}