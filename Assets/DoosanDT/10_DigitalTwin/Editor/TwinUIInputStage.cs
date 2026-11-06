// P1b-2：输入仲裁落地菜单（Editor only，幂等）
//
//   DigitalTwin/UI/26 Attach Input Arbiter
//       把运行期组件 TwinUiInputArbiter 挂到 50_玩家与相机/player/Main Camera；
//       已存在则跳过（attached=0 skipped=1 saved=no）。
//
//   DigitalTwin/UI/27 Cleanup Duplicate EventSystem
//       把 inactive 的 "EventSystem (DT_禁用)" 重命名为 "EventSystem_DISABLED_DO_NOT_ENABLE"
//       并确保 activeSelf=false；幂等（重命名只做一次）。
//
//   DigitalTwin/UI/28 Input Arbitration Check (auto-exit)
//       自动进入 Play，用组件 API 注入逐项验证输入仲裁：
//         a) 无所有者 + 右键拖拽 → yaw 变化 > 1°；
//         b) Begin(Modal) 后右键拖拽 → yaw 变化 = 0；
//         c) 指针在 UI 上右键拖拽 → yaw 变化 = 0；
//         d) IK 拖动中（Begin(IkHandle)）右键拖拽 → yaw 变化 = 0 且手柄仍在拖；
//         e) End 全部后相机恢复可动 → yaw 变化 > 1°；
//         f) 高优先级释放后 Allows(Camera) 恢复 true。
//       逐项打印 [item] ... result=PASS/FAIL，写 Logs/ui_input_check.txt / .json，
//       截 2–3 张图到证据目录，随后自动退出 Play。
//
// 说明：检查全程不保存场景；进入 Play 会主动断开 MCP WebSocket（code 4001），
// 调用方只需触发一次，然后轮询 Logs/ui_input_check.txt 的 RESULT 行。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalTwin.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIInputStage
    {
        public const string CameraPath = "50_玩家与相机/player/Main Camera";
        public const string DuplicateEventSystemName = "EventSystem (DT_禁用)";
        public const string RenamedEventSystemName = "EventSystem_DISABLED_DO_NOT_ENABLE";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-05_p1b_input";
        public const string EvidenceShots = EvidenceRoot + @"\shots\ui28";
        public const string StageLogPath = "Logs/ui_input_stage.txt";
        public const string CheckLogPath = "Logs/ui_input_check.txt";
        public const string CheckJsonPath = "Logs/ui_input_check.json";
        public const string ResultTag = "[INPUT]";
        private const string PendingKey = "TwinUIInputStage.Check.Pending";
        private const string ArmedKey = "TwinUIInputStage.Check.ArmedAt";

        // ============================================================= UI/26

        [MenuItem("DigitalTwin/UI/26 Attach Input Arbiter", false, 260)]
        public static void AttachInputArbiter()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Attach Input Arbiter (UI/26)  " + NowText() + " ===");

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: in play mode; skip");
                sb.AppendLine(ResultTag + " attached=0 saved=no result=FAIL (in play mode)");
                WriteStageLog(sb);
                Debug.LogWarning(ResultTag + " UI/26 skipped: already in play mode");
                return;
            }

            GameObject go = TwinSceneLookup.FindObject(CameraPath);
            if (go == null)
            {
                sb.AppendLine("  FAIL: camera object not found: " + CameraPath);
                sb.AppendLine(ResultTag + " attached=0 saved=no result=FAIL (camera missing)");
                WriteStageLog(sb);
                Debug.LogError(ResultTag + " UI/26 FAIL: camera not found " + CameraPath);
                return;
            }

            TwinUiInputArbiter existing = go.GetComponent<TwinUiInputArbiter>();
            int attached = 0;
            if (existing == null)
            {
                existing = go.AddComponent<TwinUiInputArbiter>();
                EditorUtility.SetDirty(go);
                attached = 1;
            }

            Scene scene = SceneManager.GetActiveScene();
            bool saved = false;
            if (attached > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            int arbiterComponents = CountComponents<TwinUiInputArbiter>();
            sb.AppendLine("  camera=" + CameraPath);
            sb.AppendLine("  attached=" + attached + " skipped=" + (attached > 0 ? 0 : 1)
                + " arbiterComponentsInScene=" + arbiterComponents
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + HashScene(scene.path));
            bool pass = attached == 1 || (attached == 0 && existing != null);
            sb.AppendLine(ResultTag + " attached=" + attached + " skipped=" + (attached > 0 ? 0 : 1)
                + " saved=" + (saved ? "yes" : "no")
                + " camera=" + CameraPath
                + " arbiterComponents=" + arbiterComponents
                + " sceneSha256=" + HashScene(scene.path)
                + " result=" + (pass ? "PASS" : "FAIL"));
            WriteStageLog(sb);

            if (pass)
            {
                Debug.Log(ResultTag + " attached=" + attached + " saved=" + (saved ? "yes" : "no")
                    + " camera=" + CameraPath + " result=PASS");
            }
            else
            {
                Debug.LogError(ResultTag + " UI/26 FAIL: unexpected state");
            }
        }

        // ============================================================= UI/27

        [MenuItem("DigitalTwin/UI/27 Cleanup Duplicate EventSystem", false, 270)]
        public static void CleanupDuplicateEventSystem()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Cleanup Duplicate EventSystem (UI/27)  " + NowText() + " ===");

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: in play mode; skip");
                sb.AppendLine(ResultTag + " duplicate=0 renamed=0 saved=no result=FAIL (in play mode)");
                WriteStageLog(sb);
                Debug.LogWarning(ResultTag + " UI/27 skipped: already in play mode");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            EventSystem[] all = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
            GameObject duplicate = null;
            int activeCount = 0;
            for (int i = 0; i < all.Length; i++)
            {
                EventSystem es = all[i];
                if (es == null)
                {
                    continue;
                }

                if (es.isActiveAndEnabled)
                {
                    activeCount++;
                }
            }

            // 重复 EventSystem 的识别：优先按历史名字（含已重命名），否则取"非 EventSystem 且 inactive"的那一个。
            // 场景 YAML 里的名字是 "EventSystem (DT_停用)"（历史遗留），任务书写成"禁用"，这里两种都兼容。
            for (int i = 0; i < all.Length; i++)
            {
                EventSystem es = all[i];
                if (es == null)
                {
                    continue;
                }

                string goName = es.gameObject.name;
                if (goName == DuplicateEventSystemName
                    || goName == DuplicateEventSystemName.Replace("禁用", "停用")
                    || goName == RenamedEventSystemName)
                {
                    duplicate = es.gameObject;
                    break;
                }
            }

            if (duplicate == null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    EventSystem es = all[i];
                    if (es == null)
                    {
                        continue;
                    }

                    GameObject go = es.gameObject;
                    if (!go.activeSelf && go.name != "EventSystem"
                        && go.name != RenamedEventSystemName
                        && go.name != DuplicateEventSystemName)
                    {
                        duplicate = go;
                        break;
                    }
                }
            }

            int renamed = 0;
            bool changed = false;
            bool duplicateWasActive = duplicate != null && duplicate.activeSelf;
            if (duplicate != null)
            {
                if (duplicate.activeSelf)
                {
                    duplicate.SetActive(false);
                    changed = true;
                }

                if (duplicate.name != RenamedEventSystemName)
                {
                    duplicate.name = RenamedEventSystemName;
                    changed = true;
                    renamed = 1;
                }

                EditorUtility.SetDirty(duplicate);
            }

            bool saved = false;
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = EditorSceneManager.SaveScene(scene);
            }

            bool activeAfter = duplicate != null && duplicate.activeSelf;

            sb.AppendLine("  eventSystems=" + all.Length + " activeBefore=" + activeCount + " duplicateName=" + (duplicate != null ? duplicate.name : "<none>")
                + " duplicateFound=" + (duplicate != null)
                + " duplicateWasActive=" + duplicateWasActive);
            sb.AppendLine("  renamed=" + renamed + " activeAfter=" + activeAfter
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + HashScene(scene.path));
            bool pass = duplicate != null && !activeAfter;
            sb.AppendLine(ResultTag + " duplicate=" + (duplicate != null ? 1 : 0)
                + " renamed=" + renamed
                + " activeBefore=" + activeCount
                + " activeAfter=" + activeAfter
                + " saved=" + (saved ? "yes" : "no")
                + " sceneSha256=" + HashScene(scene.path)
                + " result=" + (pass ? "PASS" : "FAIL"));
            WriteStageLog(sb);

            if (pass)
            {
                Debug.Log(ResultTag + " duplicate=" + (duplicate != null ? 1 : 0)
                    + " renamed=" + renamed + " activeAfter=" + activeAfter
                    + " saved=" + (saved ? "yes" : "no") + " result=PASS");
            }
            else
            {
                Debug.LogError(ResultTag + " UI/27 FAIL: duplicate EventSystem not found or still active");
            }
        }

        // ============================================================= UI/28

        [MenuItem("DigitalTwin/UI/28 Input Arbitration Check (auto-exit)", false, 280)]
        public static void InputArbitrationCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Input Arbitration Check (UI/28)  " + NowText() + " ===");
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.AppendLine("  WARN: already in play mode; skip");
                sb.AppendLine(ResultTag + " check result=FAIL (already playing)");
                WriteStageLog(sb);
                Debug.LogWarning(ResultTag + " UI/28 skipped: already playing");
                return;
            }

            ResetCheckState();
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ArmedKey,
                EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            sb.AppendLine("  armed; entering play, injecting camera/IkHandle/Modal owners, shots=" + EvidenceShots);
            WriteStageLog(sb);
            Debug.Log(ResultTag + " UI/28 armed, entering play mode; log=" + CheckLogPath);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookCheckUpdate()
        {
            EditorApplication.update -= CheckUpdate;
            EditorApplication.update += CheckUpdate;
        }

        private static int _phase = -1;
        private static double _playStart = -1;
        private static double _phaseStart = -1;
        private static bool _finished;
        private static TwinPlayCameraRig _rig;
        private static TwinPlayRayVisual _ray;
        private static TwinPlayIkHandle _ik;
        private static TwinUiInputArbiter _arbiter;
        private static Vector2 _uiPoint;
        private static readonly List<string> _items = new List<string>();
        private static readonly List<string> _fails = new List<string>();
        private static int _shotCount;
        private static string _exceptionText = string.Empty;
        private static int _soakFrames;

        private static void ResetCheckState()
        {
            _phase = -1;
            _playStart = -1;
            _phaseStart = -1;
            _finished = false;
            _rig = null;
            _ray = null;
            _ik = null;
            _arbiter = null;
            _items.Clear();
            _fails.Clear();
            _shotCount = 0;
            _exceptionText = string.Empty;
            _soakFrames = 0;
        }

        private static void CheckUpdate()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (!_finished)
                {
                    string armedText = SessionState.GetString(ArmedKey, string.Empty);
                    double armed;
                    if (double.TryParse(armedText, NumberStyles.Float, CultureInfo.InvariantCulture, out armed)
                        && EditorApplication.timeSinceStartup - armed > 150.0)
                    {
                        FailCheck("play mode did not start within 150s");
                    }
                }

                return;
            }

            if (EditorApplication.isPaused || _finished)
            {
                return;
            }

            try
            {
                if (_playStart < 0)
                {
                    _playStart = Time.realtimeSinceStartup;
                    _phase = 0;
                    _phaseStart = _playStart;
                    return;
                }

                StepCheck();
            }
            catch (Exception e)
            {
                _exceptionText = e.GetType().Name + ": " + e.Message;
                AddItem("exception", "检查过程抛出异常: " + _exceptionText, false);
                FailCheck("exception");
            }
        }

        private static void StepCheck()
        {
            double now = Time.realtimeSinceStartup;

            switch (_phase)
            {
                case 0:
                {
                    // 等 3 s 让机器人/相机 rig/IK 初始化完成
                    if (now - _playStart < 3.0)
                    {
                        return;
                    }

                    _rig = UnityEngine.Object.FindObjectOfType<TwinPlayCameraRig>();
                    _ray = UnityEngine.Object.FindObjectOfType<TwinPlayRayVisual>();
                    _ik = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();
                    _arbiter = TwinUiInputArbiter.Instance;
                    bool ok = _rig != null && _ray != null && _ik != null && _arbiter != null;
                    AddItem("[0] 运行期组件就绪",
                        "rig=" + (_rig != null) + " ray=" + (_ray != null) + " ik=" + (_ik != null)
                        + " arbiter=" + (_arbiter != null) + " camera=" + CameraPathOf(_rig), ok);
                    if (!ok)
                    {
                        FailCheck("missing runtime components");
                        return;
                    }

                    _arbiter.EndAll();
                    _rig.ClearPointerOverride();
                    _rig.RestoreInitialPose();
                    _uiPoint = FindUiPoint();
                    AddItem("[0b] UI 采样点",
                        "uiPoint=(" + Mathf.RoundToInt(_uiPoint.x) + "," + Mathf.RoundToInt(_uiPoint.y)
                        + ") overUi=" + _rig.IsPointerOverUiAt(_uiPoint)
                        + " stack=" + _arbiter.DescribeStack(),
                        _rig.IsPointerOverUiAt(_uiPoint));
                    NextPhase(1);
                    return;
                }

                case 1:
                {
                    // a) 无所有者 + 右键拖拽 → yaw 变化 > 1°
                    _arbiter.EndAll();
                    _rig.ClearPointerOverride();
                    float delta = ProbeDragYawDelta();
                    AddItem("[a] 无所有者时右键拖拽 → 相机可动",
                        "targetDelta=" + F(delta, 3) + " yawDelta=" + F(_lastYawDelta, 3)
                        + " (阈值 >1) allowsCamera=" + _arbiter.Allows(TwinUiInputOwner.Camera),
                        delta > 1f);
                    CaptureShot("ui28_s02_baseline_drag.png");
                    NextPhase(2);
                    return;
                }

                case 2:
                {
                    // b) Begin(Modal) 后右键拖拽 → yaw 变化 = 0
                    _arbiter.EndAll();
                    InjectHold(false, 2);
                    bool beganModal = _arbiter.Begin(TwinUiInputOwner.Modal);
                    float delta = ProbeDragYawDelta();
                    bool currentModal = _arbiter.Current == TwinUiInputOwner.Modal;
                    bool deniesCamera = !_arbiter.Allows(TwinUiInputOwner.Camera);
                    _arbiter.End(TwinUiInputOwner.Modal);
                    AddItem("[b] Begin(Modal) 后右键拖拽 → 相机被拒",
                        "began=" + beganModal + " currentWasModal=" + currentModal
                        + " deniesCamera=" + deniesCamera
                        + " targetDelta=" + F(delta, 3) + " yawDelta=" + F(_lastYawDelta, 3) + " (期望 0)",
                        beganModal && currentModal && deniesCamera && delta < 0.001f);
                    AddItem("[f1] Modal 释放后 Allows(Camera) 恢复",
                        "allowsCamera=" + _arbiter.Allows(TwinUiInputOwner.Camera)
                        + " depth=" + _arbiter.Depth,
                        _arbiter.Allows(TwinUiInputOwner.Camera));
                    NextPhase(3);
                    return;
                }

                case 3:
                {
                    // c) 指针在 UI 上时右键拖拽 → yaw 变化 = 0
                    _arbiter.EndAll();
                    InjectHold(false, 2);
                    _rig.SetPointerOverride(_uiPoint);
                    bool overUi = _rig.IsPointerOverUiAt(_uiPoint);
                    float delta = ProbeDragYawDelta();
                    _rig.ClearPointerOverride();
                    AddItem("[c] 指针在 UI 上 ±0 拖拽 → 相机被拒",
                        "uiPoint=(" + Mathf.RoundToInt(_uiPoint.x) + "," + Mathf.RoundToInt(_uiPoint.y)
                        + ") overUi=" + overUi + " targetDelta=" + F(delta, 3) + " yawDelta=" + F(_lastYawDelta, 3) + " (期望 0)",
                        overUi && delta < 0.001f);
                    NextPhase(4);
                    return;
                }

                case 4:
                {
                    // d) IK 拖动中右键拖拽 → yaw 变化 = 0，且手柄仍在拖
                    _arbiter.EndAll();
                    _rig.ClearPointerOverride();
                    InjectHold(false, 2);
                    bool began = _ik.BeginDragForTest();
                    bool draggingBefore = _ik.Dragging;
                    bool ownsIk = _arbiter.Current == TwinUiInputOwner.IkHandle;
                    bool deniesCamera = !_arbiter.Allows(TwinUiInputOwner.Camera);
                    float delta = ProbeDragYawDelta();
                    bool draggingAfter = _ik.Dragging;
                    AddItem("[d] IK 拖动中右键拖拽 → 相机被拒且手柄仍在拖",
                        "began=" + began + " draggingBefore=" + draggingBefore
                        + " draggingAfter=" + draggingAfter + " owner=" + _arbiter.Current
                        + " deniesCamera=" + deniesCamera
                        + " ikOwnsArbiter=" + _ik.OwnsInputArbiter
                        + " targetDelta=" + F(delta, 3) + " yawDelta=" + F(_lastYawDelta, 3) + " (期望 0)",
                        began && draggingBefore && draggingAfter && ownsIk && deniesCamera && delta < 0.001f);
                    CaptureShot("ui28_s03_ik_drag.png");
                    NextPhase(5);
                    return;
                }

                case 5:
                {
                    // d2) IK 结束 → 释放 IkHandle 所有权
                    _ik.EndDragForTest();
                    InjectHold(false, 2);
                    bool released = _arbiter.Current != TwinUiInputOwner.IkHandle && !_ik.Dragging;
                    AddItem("[d2] IK 结束后释放所有权",
                        "dragging=" + _ik.Dragging + " current=" + _arbiter.Current
                        + " ikOwnsArbiter=" + _ik.OwnsInputArbiter
                        + " allowsCamera=" + _arbiter.Allows(TwinUiInputOwner.Camera),
                        released && _arbiter.Allows(TwinUiInputOwner.Camera));
                    NextPhase(6);
                    return;
                }

                case 6:
                {
                    // e) End 全部后相机恢复可动 → yaw 变化 > 1°
                    _arbiter.EndAll();
                    float delta = ProbeDragYawDelta();
                    AddItem("[e] End 全部后相机恢复可动",
                        "targetDelta=" + F(delta, 3) + " yawDelta=" + F(_lastYawDelta, 3) + " (阈值 >1)",
                        delta > 1f);
                    NextPhase(7);
                    return;
                }

                case 7:
                {
                    // f) 高优先级释放后 Allows(Camera) 恢复 true
                    _arbiter.EndAll();
                    bool allowsCamera = _arbiter.Allows(TwinUiInputOwner.Camera);
                    bool allowsSceneRay = _arbiter.Allows(TwinUiInputOwner.SceneRay);
                    bool rayNotBlocked = !_ray.IsBlockedByHigherPriorityInput;
                    AddItem("[f] 高优先级释放后 Allows(Camera)/射线遮挡查询",
                        "allowsCamera=" + allowsCamera + " allowsSceneRay=" + allowsSceneRay
                        + " rayBlocked=" + _ray.IsBlockedByHigherPriorityInput
                        + " depth=" + _arbiter.Depth + " current=" + _arbiter.Current,
                        allowsCamera && allowsSceneRay && rayNotBlocked);
                    CaptureShot("ui28_s04_after_release.png");
                    NextPhase(8);
                    return;
                }

                case 8:
                {
                    // 等截图落盘，然后收尾
                    if (now - _phaseStart < 0.8)
                    {
                        return;
                    }

                    FinishCheck();
                    return;
                }
            }
        }

        // ------------------------------------------------------------- 注入 / 工具

        private static void InjectHold(bool rightHeld, int frames)
        {
            InjectFrames(rightHeld, 0f, frames);
        }

        private static void InjectDrag(float lookX, int frames)
        {
            InjectFrames(true, lookX, frames);
        }

        private static void InjectFrames(bool rightHeld, float lookX, int frames)
        {
            if (_rig == null)
            {
                return;
            }

            var input = new TwinPlayCameraRig.TwinPlayInput
            {
                rightMouseHeld = rightHeld,
                middleMouseHeld = false,
                lookDelta = new Vector2(lookX, 0f),
                scrollNotches = 0f
            };

            for (int i = 0; i < frames; i++)
            {
                _rig.StepForTest(input, 0.02f);
            }
        }

        private const float DragLookX = 60f;
        private static float _lastTargetDelta;
        private static float _lastYawDelta;

        /// <summary>
        /// 复位相机后注入一次"按下 + 拖拽"序列，返回 TargetYaw 的变化量（确定值，不受旋转平滑影响）。
        /// 同时记录实际 Yaw 变化量到 _lastYawDelta，便于日志对照。
        /// </summary>
        private static float ProbeDragYawDelta()
        {
            _rig.RestoreInitialPose();
            InjectFrames(false, 0f, 2);
            float target0 = _rig.TargetYaw;
            float yaw0 = _rig.Yaw;
            InjectFrames(true, 0f, 2);            // 按下边沿：低优先级消费者在此申请所有权
            InjectFrames(true, DragLookX, 12);    // 拖拽 12 帧
            float target1 = _rig.TargetYaw;
            float yaw1 = _rig.Yaw;
            InjectFrames(false, 0f, 2);           // 释放边沿
            _lastTargetDelta = Mathf.Abs(Mathf.DeltaAngle(target0, target1));
            _lastYawDelta = Mathf.Abs(Mathf.DeltaAngle(yaw0, yaw1));
            return _lastTargetDelta;
        }

        private static void NextPhase(int phase)
        {
            _phase = phase;
            _phaseStart = Time.realtimeSinceStartup;
        }

        private static Vector2 FindUiPoint()
        {
            if (EventSystem.current == null)
            {
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            for (int y = Screen.height - 30; y >= 30; y -= 36)
            {
                for (int x = 30; x < Screen.width - 30; x += 36)
                {
                    var data = new PointerEventData(EventSystem.current) { position = new Vector2(x, y) };
                    var results = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(data, results);
                    if (results.Count > 0 && results[0].gameObject != null)
                    {
                        return new Vector2(x, y);
                    }
                }
            }

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        private static string CameraPathOf(TwinPlayCameraRig rig)
        {
            if (rig == null || rig.ActiveCamera == null)
            {
                return "<null>";
            }

            return TwinSceneLookup.PathOf(rig.ActiveCamera.transform);
        }

        private static void AddItem(string name, string detail, bool pass)
        {
            string line = "[item] " + name + " | " + detail + " | result=" + (pass ? "PASS" : "FAIL");
            _items.Add(line);
            if (!pass)
            {
                _fails.Add(line);
            }

            Debug.Log(ResultTag + " " + line);
        }

        private static void CaptureShot(string fileName)
        {
            try
            {
                if (!Directory.Exists(EvidenceShots))
                {
                    Directory.CreateDirectory(EvidenceShots);
                }

                string path = Path.Combine(EvidenceShots, fileName).Replace('\\', '/');
                ScreenCapture.CaptureScreenshot(path);
                _shotCount++;
            }
            catch (Exception e)
            {
                Debug.LogWarning(ResultTag + " screenshot failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------- 收尾

        private static void FailCheck(string reason)
        {
            AddItem("[fail]", reason, false);
            FinishCheck();
        }

        private static void FinishCheck()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            SessionState.SetBool(PendingKey, false);

            try
            {
                if (_arbiter != null)
                {
                    _arbiter.EndAll();
                }

                if (_rig != null)
                {
                    _rig.ClearPointerOverride();
                }
            }
            catch (Exception)
            {
                // 退出 Play 前的清理不允许再抛
            }

            bool pass = _fails.Count == 0 && _shotCount >= 2;

            var sb = new StringBuilder();
            sb.AppendLine("=== Input Arbitration Check (P1b-2)  " + NowText() + " ===");
            sb.AppendLine("scene=camera path=" + CameraPath + " shots=" + _shotCount + " dir=" + EvidenceShots);
            for (int i = 0; i < _items.Count; i++)
            {
                sb.AppendLine(_items[i]);
            }

            sb.AppendLine();
            sb.AppendLine("---- 失败项 (" + _fails.Count + ") ----");
            if (_fails.Count == 0)
            {
                sb.AppendLine("(none)");
            }
            else
            {
                for (int i = 0; i < _fails.Count; i++)
                {
                    sb.AppendLine(_fails[i]);
                }
            }

            sb.AppendLine();
            sb.AppendLine(ResultTag + " check items=" + _items.Count
                + " fails=" + _fails.Count
                + " totalErrors=" + (_fails.Count + (_exceptionText.Length > 0 ? 1 : 0))
                + " shots=" + _shotCount
                + " exception=" + (_exceptionText.Length > 0 ? _exceptionText : "(none)")
                + " result=" + (pass ? "PASS" : "FAIL"));
            sb.AppendLine("RESULT: " + (pass ? "PASS" : "FAIL"));
            string text = sb.ToString();
            WriteFileAbsolute(CheckLogPath, text);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"generatedAt\": \"" + NowText() + "\",");
            json.AppendLine("  \"cameraPath\": " + J(CameraPath) + ",");
            json.AppendLine("  \"items\": " + _items.Count + ",");
            json.AppendLine("  \"fails\": " + _fails.Count + ",");
            json.AppendLine("  \"totalErrors\": " + (_fails.Count + (_exceptionText.Length > 0 ? 1 : 0)) + ",");
            json.AppendLine("  \"shots\": " + _shotCount + ",");
            json.AppendLine("  \"shotsDir\": " + J(EvidenceShots.Replace('\\', '/')) + ",");
            json.AppendLine("  \"exception\": " + J(_exceptionText) + ",");
            json.AppendLine("  \"result\": " + J(pass ? "PASS" : "FAIL"));
            json.AppendLine("}");
            WriteFileAbsolute(CheckJsonPath, json.ToString());

            Debug.Log(ResultTag + " check items=" + _items.Count + " fails=" + _fails.Count
                + " totalErrors=" + (_fails.Count + (_exceptionText.Length > 0 ? 1 : 0))
                + " shots=" + _shotCount + " result=" + (pass ? "PASS" : "FAIL") + " log=" + CheckLogPath);

            EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------- 文件/格式

        private static void WriteStageLog(StringBuilder sb)
        {
            try
            {
                string dir = Path.GetDirectoryName(StageLogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(StageLogPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(ResultTag + " stage log write failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void WriteFileAbsolute(string path, string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning(ResultTag + " write failed (" + path + "): " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static int CountComponents<T>() where T : Component
        {
            T[] found = UnityEngine.Object.FindObjectsOfType<T>(true);
            return found == null ? 0 : found.Length;
        }

        private static string HashScene(string scenePath)
        {
            try
            {
                if (string.IsNullOrEmpty(scenePath))
                {
                    return "<no-scene>";
                }

                string full = Path.GetFullPath(scenePath);
                if (!File.Exists(full))
                {
                    return "<missing>";
                }

                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream stream = File.OpenRead(full))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "<error:" + e.GetType().Name + ">";
            }
        }

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string F(float value, int digits)
        {
            return value.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        private static string J(string text)
        {
            if (text == null)
            {
                return "null";
            }

            var sb = new StringBuilder(text.Length + 8);
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static void WriteFileAbsolute(string path, StringBuilder text)
        {
            WriteFileAbsolute(path, text.ToString());
        }
    }
}