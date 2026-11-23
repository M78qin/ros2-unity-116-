// TwinUiV2CaptureStage.cs —— V2 工作台 1:1 截图（UI/65a BEFORE / UI/65b AFTER）
// 目的：给 UI 视觉验收提供真正 1920x1080 / 1600x900 / 1280x720 的渲染图，而不是被缩放的 Game 视图。
// 约束：只改主窗矩形 / 页签 / GameView 尺寸；不保存布局、不删除用户存档（开始快照、结束恢复）。
// 输出：<EvidenceRoot>\shots\<phase>\{res}_{overview|twin|float}.png + meta.json
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiV2CaptureStage
    {
        public const string PendingKey = "TwinUiV2Capture.Pending";
        public const string PhaseKey = "TwinUiV2Capture.Phase";
        public const string EvidenceRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-17_ui_fix";

        [MenuItem("DigitalTwin/UI/65c Refresh Assets (import new scripts)", false, 653)]
        public static void RefreshAssets()
        {
            AssetDatabase.Refresh();
            Debug.Log("[SHOT65] AssetDatabase.Refresh done");
        }

        [MenuItem("DigitalTwin/UI/65z Dump GameView API", false, 652)]
        public static void DumpGameViewApi()
        {
            var sb = new StringBuilder();
            try
            {
                Type t = Type.GetType("UnityEditor.GameView,UnityEditor");
                sb.AppendLine("GameView type=" + (t != null ? t.FullName : "null"));
                if (t != null)
                {
                    foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static))
                    {
                        sb.AppendLine("PROP " + p.PropertyType.Name + " " + p.Name
                            + " get=" + p.CanRead + " set=" + p.CanWrite);
                    }

                    foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static))
                    {
                        sb.AppendLine("FIELD " + f.FieldType.Name + " " + f.Name);
                    }

                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (m.Name.IndexOf("size", StringComparison.OrdinalIgnoreCase) < 0
                            && m.Name.IndexOf("resolution", StringComparison.OrdinalIgnoreCase) < 0
                            && m.Name.IndexOf("render", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        sb.AppendLine("METHOD " + m);
                    }
                }

                Type gst = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                sb.AppendLine("GameViewSizes=" + (gst != null ? gst.FullName : "null"));
                if (gst != null)
                {
                    foreach (MethodInfo m in gst.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static))
                    {
                        sb.AppendLine("GVS METHOD " + m);
                    }
                }

                Type ggt = Type.GetType("UnityEditor.GameViewSizeGroup,UnityEditor");
                sb.AppendLine("GameViewSizeGroup=" + (ggt != null ? ggt.FullName : "null"));
                if (ggt != null)
                {
                    foreach (MethodInfo m in ggt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static))
                    {
                        sb.AppendLine("GVG METHOD " + m);
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("ERR " + e);
            }

            string path = Path.Combine(TwinUiV2CaptureStage.EvidenceRoot, "logs", "gameview_api.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[SHOT65] dumped GameView API to " + path);
        }

        [MenuItem("DigitalTwin/UI/65a Capture BEFORE (auto-exit)", false, 650)]
        public static void RunBefore()
        {
            Run("before");
        }

        [MenuItem("DigitalTwin/UI/65b Capture AFTER (auto-exit)", false, 651)]
        public static void RunAfter()
        {
            Run("after");
        }

        private static void Run(string phase)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SHOT65] skipped: already in play mode");
                return;
            }

            if (TwinUiShellMode.Kind != TwinUiShellKind.V2)
            {
                TwinUiShellMode.Set(TwinUiShellKind.V2);
                Debug.LogWarning("[SHOT65] shell mode was not V2; switched to V2 for capture.");
            }

            TwinUiV2Capture.Reset();
            SessionState.SetString(PhaseKey, phase);
            SessionState.SetBool(PendingKey, true);
            Debug.Log("[SHOT65] armed phase=" + phase + " root=" + EvidenceRoot);
            EditorApplication.isPlaying = true;
        }
    }

    public static class TwinUiV2Capture
    {
        private struct Target
        {
            public int Width;
            public int Height;
        }

        private static readonly Target[] Targets =
        {
            new Target { Width = 1920, Height = 1080 },
            new Target { Width = 1600, Height = 900 },
            new Target { Width = 1280, Height = 720 }
        };

        private static readonly List<string> Shots = new List<string>();
        private static readonly StringBuilder Meta = new StringBuilder();
        private static TwinUiWindowHost _window;
        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static int _index;
        private static int _step;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static float _startAt;
        private static string _phase = "before";
        private static string _outDir;
        private static int _logErrors;
        private static int _logExceptions;

        private static int _origSizeIndex = -1;
        private static Rect _origWindowRect;
        private static bool _origMaximized;
        private static string _prefShell;
        private static string _prefWindow;
        private static int _prefMode;
        private static bool _prefHadShell;
        private static bool _prefHadWindow;
        private static bool _prefHadMode;

        public static void Reset()
        {
            Shots.Clear();
            Meta.Length = 0;
            _window = null;
            _shell = null;
            _surface = null;
            _index = 0;
            _step = 0;
            _nextAt = 0f;
            _started = false;
            _finished = false;
            _startAt = 0f;
            _logErrors = 0;
            _logExceptions = 0;
            _origSizeIndex = -1;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(TwinUiV2CaptureStage.PendingKey, false) || !EditorApplication.isPlaying || _finished)
            {
                return;
            }

            if (!_started)
            {
                Start();
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _startAt > 150f)
            {
                Meta.AppendLine("timeout=true");
                Finish();
                return;
            }

            Step();
        }

        private static void Start()
        {
            _started = true;
            _startAt = Time.realtimeSinceStartup;
            _nextAt = _startAt + 0.8f;
            _phase = SessionState.GetString(TwinUiV2CaptureStage.PhaseKey, "before");
            _outDir = Path.Combine(TwinUiV2CaptureStage.EvidenceRoot, "shots", _phase);
            Directory.CreateDirectory(_outDir);

            Application.logMessageReceived += OnLog;
            _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            _shell = _window != null ? _window.ShellV2 : null;
            _surface = _shell != null ? _shell.Surface : null;

            SnapshotPrefs();
            SnapshotGameView();

            if (_window != null)
            {
                _origWindowRect = _window.WindowRect;
                _origMaximized = _window.IsMaximized;
            }

            if (_surface != null)
            {
                _surface.ResetForCheck();
            }

            Meta.AppendLine("scene=" + SceneManager.GetActiveScene().path);
            Meta.AppendLine("phase=" + _phase);
            Meta.AppendLine("start=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Meta.AppendLine("shell=" + (_shell != null) + " surface=" + (_surface != null));
            Meta.AppendLine("gameView=" + Screen.width + "x" + Screen.height);
            Debug.Log("[SHOT65] start phase=" + _phase + " window=" + (_window != null) + " shell=" + (_shell != null));
        }

        private static void Step()
        {
            if (_window == null || _shell == null || _surface == null)
            {
                Meta.AppendLine("missingRuntime=true");
                Finish();
                return;
            }

            if (_index >= Targets.Length)
            {
                Finish();
                return;
            }

            Target t = Targets[_index];
            switch (_step)
            {
                case 0:
                    TrySetGameViewSize(t.Width, t.Height);
                    _nextAt = Time.realtimeSinceStartup + 0.5f;
                    _step = 1;
                    return;
                case 1:
                    if (Screen.width != t.Width || Screen.height != t.Height)
                    {
                        // 编辑器里少数 GameView 实现不响应尺寸选择；再用 SetResolution 兜底并记录。
                        Screen.SetResolution(t.Width, t.Height, false);
                        Meta.AppendLine("fallbackSetResolution=" + t.Width + "x" + t.Height);
                    }

                    _window.ApplyLayout(new Rect(0f, 0f, _window.CanvasRect.rect.width,
                        _window.CanvasRect.rect.height - _window.BottomInset), true, false);
                    _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
                    _shell.ApplyLayout(_window.Content.rect.width, _window.Content.rect.height);
                    _nextAt = Time.realtimeSinceStartup + 0.65f;
                    _step = 2;
                    return;
                case 2:
                    Capture(t, "overview");
                    _nextAt = Time.realtimeSinceStartup + 0.55f;
                    _step = 3;
                    return;
                case 3:
                    _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
                    _nextAt = Time.realtimeSinceStartup + 0.45f;
                    _step = 4;
                    return;
                case 4:
                    Capture(t, "twin");
                    _nextAt = Time.realtimeSinceStartup + 0.45f;
                    _step = 5;
                    return;
                case 5:
                    _surface.Detach("joints", "capture");
                    _nextAt = Time.realtimeSinceStartup + 0.5f;
                    _step = 6;
                    return;
                case 6:
                    Capture(t, "float");
                    _nextAt = Time.realtimeSinceStartup + 0.45f;
                    _step = 7;
                    return;
                case 7:
                    _surface.DockBack("joints");
                    _surface.SetTab(TwinUiWorkspaceRegistry.Overview);
                    _index++;
                    _step = 0;
                    _nextAt = Time.realtimeSinceStartup + 0.4f;
                    return;
            }
        }

        private static void Capture(Target t, string tag)
        {
            try
            {
                string path = Path.Combine(_outDir, t.Width + "x" + t.Height + "_" + tag + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
                Meta.AppendLine("shot=" + path + " tag=" + tag + " requested=" + t.Width + "x" + t.Height
                    + " screen=" + Screen.width + "x" + Screen.height
                    + " window=" + _window.WindowRect.width.ToString("F0") + "x"
                    + _window.WindowRect.height.ToString("F0")
                    + " floating=" + _surface.FloatingCount + " tab=" + _surface.Tab
                    + " cards=" + _surface.Page.CardCount + " cols=" + _surface.Page.Columns
                    + " breakpoint=" + (_window.Responsive != null ? _window.Responsive.Current.ToString() : "n/a"));
                Debug.Log("[SHOT65] shot " + path + " screen=" + Screen.width + "x" + Screen.height);
            }
            catch (Exception e)
            {
                Meta.AppendLine("shotError=" + tag + ":" + e.Message);
                Debug.LogWarning("[SHOT65] capture failed " + tag + ": " + e.Message);
            }
        }

        private static void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(TwinUiV2CaptureStage.PendingKey, false);

            try
            {
                if (_window != null)
                {
                    _window.ApplyLayout(_origWindowRect, _origMaximized, false);
                }
            }
            catch (Exception e)
            {
                Meta.AppendLine("restoreWindowError=" + e.Message);
            }

            RestoreGameView();
            RestorePrefs();

            Meta.AppendLine("shots=" + Shots.Count);
            Meta.AppendLine("logErrors=" + _logErrors + " logExceptions=" + _logExceptions);
            Meta.AppendLine("end=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                string metaPath = Path.Combine(_outDir, "meta.txt");
                File.WriteAllText(metaPath, Meta.ToString(), new UTF8Encoding(false));
                string json = "{\"phase\":\"" + _phase + "\",\"shots\":" + Shots.Count
                    + ",\"logErrors\":" + _logErrors + ",\"logExceptions\":" + _logExceptions + "}";
                File.WriteAllText(Path.Combine(_outDir, "meta.json"), json, new UTF8Encoding(false));
                Debug.Log("[SHOT65] done phase=" + _phase + " shots=" + Shots.Count + " meta=" + metaPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHOT65] meta write failed: " + e.Message);
            }

            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        // ---------------------------------------------------------------- GameView 分辨率

        private static void SnapshotGameView()
        {
            try
            {
                var gameView = FindGameView();
                if (gameView == null)
                {
                    return;
                }

                var prop = gameView.GetType().GetProperty("selectedSizeIndex",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? gameView.GetType().GetProperty("sizeSelection",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    _origSizeIndex = (int)prop.GetValue(gameView, null);
                }
            }
            catch (Exception e)
            {
                Meta.AppendLine("snapshotGameViewError=" + e.Message);
            }
        }

        private static void RestoreGameView()
        {
            if (_origSizeIndex < 0)
            {
                return;
            }

            try
            {
                var gameView = FindGameView();
                if (gameView == null)
                {
                    return;
                }

                var prop = gameView.GetType().GetProperty("selectedSizeIndex",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? gameView.GetType().GetProperty("sizeSelection",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(gameView, _origSizeIndex, null);
                    gameView.Repaint();
                }
            }
            catch (Exception e)
            {
                Meta.AppendLine("restoreGameViewError=" + e.Message);
            }
        }

        private static EditorWindow FindGameView()
        {
            Type type = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (type == null)
            {
                return null;
            }

            return EditorWindow.GetWindow(type, false, "Game", false);
        }

        /// <summary>把 Game 视图切到固定分辨率；成功返回 true。</summary>
        private static bool TrySetGameViewSize(int width, int height)
        {
            try
            {
                EditorWindow gameView = FindGameView();
                Type sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                Type sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                Type sizeEnumType = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
                Type groupEnumType = Type.GetType("UnityEditor.GameViewSizeGroupType,UnityEditor");
                if (gameView == null || sizesType == null || sizeType == null || sizeEnumType == null
                    || groupEnumType == null)
                {
                    Meta.AppendLine("gameViewApiMissing=true");
                    return false;
                }

                object sizes = sizesType.GetProperty("instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null, null);
                if (sizes == null)
                {
                    Meta.AppendLine("gameViewSizesInstanceMissing=true");
                    sizes = ScriptableObject.CreateInstance(sizesType);
                }

                MethodInfo getGroup = sizesType.GetMethod("GetGroup",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getGroup == null)
                {
                    return false;
                }

                ParameterInfo[] pars = getGroup.GetParameters();
                object groupKey = Enum.ToObject(pars[0].ParameterType, 0); // Standalone
                object group = getGroup.Invoke(sizes, new[] { groupKey });
                if (group == null)
                {
                    return false;
                }

                Type groupType = group.GetType();
                MethodInfo getTotal = groupType.GetMethod("GetTotalCount",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getSize = groupType.GetMethod("GetGameViewSize",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                int total = (int)getTotal.Invoke(group, null);
                var sb = new StringBuilder();
                for (int i = 0; i < total; i++)
                {
                    object size = getSize.Invoke(group, new object[] { i });
                    int w = (int)size.GetType().GetProperty("width").GetValue(size, null);
                    int h = (int)size.GetType().GetProperty("height").GetValue(size, null);
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(w).Append('x').Append(h);
                    if (w == width && h == height)
                    {
                        Meta.AppendLine("gameViewSizes=" + sb);
                        return ApplySizeSelection(gameView, i);
                    }
                }

                Meta.AppendLine("gameViewSizes(before add)=" + sb);

                ConstructorInfo ctor = sizeType.GetConstructor(new[]
                {
                    sizeEnumType, typeof(int), typeof(int), typeof(string)
                });
                if (ctor == null)
                {
                    return false;
                }

                object fixedResolution = Enum.Parse(sizeEnumType, "FixedResolution");
                object custom = ctor.Invoke(new object[] { fixedResolution, width, height, width + "x" + height });
                groupType.GetMethod("AddCustomSize",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(group, new[] { custom });
                MethodInfo save = sizesType.GetMethod("SaveToHDD",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                save?.Invoke(sizes, null);
                total = (int)getTotal.Invoke(group, null);
                MethodInfo getCustom = groupType.GetMethod("GetCustomCount",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                int customCount = getCustom != null ? (int)getCustom.Invoke(group, null) : -1;
                Meta.AppendLine("gameViewAddedSize=" + width + "x" + height + " index=" + (total - 1)
                    + " total=" + total + " custom=" + customCount);
                return ApplySizeSelection(gameView, total - 1);
            }
            catch (Exception e)
            {
                Meta.AppendLine("setGameViewSizeError=" + width + "x" + height + ":" + e.Message);
                return false;
            }
        }

        private static bool ApplySizeSelection(EditorWindow gameView, int index)
        {
            string[] names = { "selectedSizeIndex", "sizeSelection" };
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo prop = gameView.GetType().GetProperty(names[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite)
                {
                    continue;
                }

                prop.SetValue(gameView, index, null);
                gameView.Repaint();
                Meta.AppendLine("gameViewSelection(" + names[i] + ")=" + index);
                return true;
            }

            // 兜底：某些版本只暴露 SizeSelection(int) 方法。
            MethodInfo method = gameView.GetType().GetMethod("SizeSelection",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                ParameterInfo[] pars = method.GetParameters();
                if (pars.Length == 1)
                {
                    method.Invoke(gameView, new[] { Convert.ChangeType(index, pars[0].ParameterType) });
                    gameView.Repaint();
                    Meta.AppendLine("gameViewSelection(method)=" + index);
                    return true;
                }
            }

            Meta.AppendLine("gameViewSelectionFailed=no writable property");
            return false;
        }

        // ---------------------------------------------------------------- 存档快照

        private static void SnapshotPrefs()
        {
            _prefHadShell = PlayerPrefs.HasKey(TwinUiShellPrefs.Key);
            _prefShell = PlayerPrefs.GetString(TwinUiShellPrefs.Key, string.Empty);

            const string windowKey = "TwinUI.Window.Layout.v1";
            _prefHadWindow = PlayerPrefs.HasKey(windowKey);
            _prefWindow = PlayerPrefs.GetString(windowKey, string.Empty);

            _prefHadMode = PlayerPrefs.HasKey(TwinUiShellMode.PrefsKey);
            _prefMode = PlayerPrefs.GetInt(TwinUiShellMode.PrefsKey, (int)TwinUiShellKind.V2);
        }

        private static void RestorePrefs()
        {
            try
            {
                if (_prefHadShell)
                {
                    PlayerPrefs.SetString(TwinUiShellPrefs.Key, _prefShell);
                }
                else
                {
                    PlayerPrefs.DeleteKey(TwinUiShellPrefs.Key);
                }

                const string windowKey = "TwinUI.Window.Layout.v1";
                if (_prefHadWindow)
                {
                    PlayerPrefs.SetString(windowKey, _prefWindow);
                }
                else
                {
                    PlayerPrefs.DeleteKey(windowKey);
                }

                if (_prefHadMode)
                {
                    PlayerPrefs.SetInt(TwinUiShellMode.PrefsKey, _prefMode);
                }
                else
                {
                    PlayerPrefs.DeleteKey(TwinUiShellMode.PrefsKey);
                }

                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Meta.AppendLine("restorePrefsError=" + e.Message);
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _logErrors++;
                Meta.AppendLine("logError=" + condition);
            }
            else if (type == LogType.Exception)
            {
                _logExceptions++;
                Meta.AppendLine("logException=" + condition);
            }
        }
    }
}