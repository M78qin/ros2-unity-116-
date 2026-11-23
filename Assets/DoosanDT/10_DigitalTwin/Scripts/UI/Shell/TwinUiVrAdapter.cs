// TwinUiVrAdapter.cs —— VR「先适配」骨架 + 状态上报（P9.5）
// 定位：只做适配骨架与状态上报，不重写 VR UI。
// 行为：
//   * 桌面模式（XRSettings.isDeviceActive == false）：**直接 return，不改变任何画布/窗口设置**（保证 UI/47–52 自检不受影响）；
//   * VR 激活时：保存画布快照（renderMode / worldCamera / planeDistance / near / far / pixelPerfect / sortingOrder / targetDisplay），
//     若画布无相机则保持原渲染模式；有相机则切 ScreenSpaceCamera；窗口宽度限制到画布宽 85%，字号不放大；
//   * 退出 VR：恢复画布快照 + 进入 VR 前的窗口矩形。
// 无头显 → 运行时结果只能标 NOT TESTED（编辑器里 isDeviceActive=false 属预期）。
using System;
using UnityEngine;
using UnityEngine.XR;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiVrAdapter : MonoBehaviour
    {
        public const float PollInterval = 0.5f;
        public const float VrWindowWidthRatio = 0.85f;
        public const string Tag = "[VR]";
        public const string ModeDesktop = "桌面";
        public const string ModeVrUnverified = "VR(未验证)";

        private struct CanvasSnapshot
        {
            public bool valid;
            public RenderMode renderMode;
            public Camera worldCamera;
            public float planeDistance;
            public bool pixelPerfect;
            public bool overridePixelPerfect;
            public int sortingOrder;
            public int targetDisplay;
        }

        private TwinUiWindowHost _host;
        private Canvas _canvas;
        private CanvasSnapshot _snapshot;
        private Rect _preVrWindowRect;
        private bool _preVrMaximized;
        private bool _vrActive;
        private bool _configured;
        private bool _hasVrApplied;
        private string _deviceName = string.Empty;
        private float _nextPollTime;
        private int _enterCount;
        private int _exitCount;
        private int _windowLimitCount;

        public bool IsVrActive => _vrActive;
        public string DeviceName => _deviceName;
        public string ModeLabel => _vrActive ? ModeVrUnverified : ModeDesktop;
        public bool IsConfigured => _configured;
        public TwinUiWindowHost Host => _host;
        public int EnterCount => _enterCount;
        public int ExitCount => _exitCount;
        public int WindowLimitCount => _windowLimitCount;
        public bool HasVrApplied => _hasVrApplied;
        public string FontScaleNote => "1.0（不放大；与桌面字号一致）";

        /// <summary>工厂方法（可选；Lead 也可直接 AddComponent + Configure）。</summary>
        public static TwinUiVrAdapter Create(TwinUiWindowHost host)
        {
            var go = new GameObject("UiVrAdapter", typeof(RectTransform));
            var adapter = go.AddComponent<TwinUiVrAdapter>();
            adapter.Configure(host);
            return adapter;
        }

        /// <summary>注入主窗宿主；只在运行时读取 XR 状态，不修改任何桌面设置。</summary>
        public void Configure(TwinUiWindowHost host)
        {
            _host = host;
            _canvas = host != null ? host.Canvas : null;
            _deviceName = SafeLoadedDeviceName();
            _nextPollTime = Time.unscaledTime + PollInterval;
            _configured = true;
            Debug.Log(Tag + " adapter start " + Describe());
        }

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            if (Time.unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + PollInterval;

            bool active = SafeVrActive();
            string device = SafeLoadedDeviceName();
            _deviceName = device;
            if (active == _vrActive)
            {
                return;
            }

            _vrActive = active;
            if (active)
            {
                EnterVr(device);
            }
            else
            {
                ExitVr(device);
            }
        }

        private void OnDisable()
        {
            // 安全兜底：若 VR 分支曾改过画布，退出时恢复；桌面模式没有应用过则什么都不做。
            if (_hasVrApplied)
            {
                RestoreCanvasSnapshot();
                if (_host != null && _preVrWindowRect.width > 1f && _preVrWindowRect.height > 1f)
                {
                    try
                    {
                        _host.ApplyLayout(_preVrWindowRect, _preVrMaximized, false);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(Tag + " disable restore window failed: " + e.Message);
                    }
                }

                _hasVrApplied = false;
            }
        }

        // ------------------------------------------------------------- VR 进入 / 退出

        private void EnterVr(string device)
        {
            _enterCount++;
            // 再次确认：只有 XR 设备真的激活时才允许改画布（桌面路径绝不进入）。
            if (!SafeVrActive())
            {
                _vrActive = false;
                return;
            }

            if (_host == null)
            {
                Debug.LogWarning(Tag + " VR active but host is null; keep desktop state");
                return;
            }

            _canvas = _canvas != null ? _canvas : _host.Canvas;
            if (_canvas == null)
            {
                Debug.LogWarning(Tag + " VR active but canvas is null; keep desktop canvas");
                return;
            }

            SnapshotCanvas();
            _preVrWindowRect = _host.WindowRect;
            _preVrMaximized = _host.IsMaximized;

            Camera cam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;
            if (cam != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = cam;
                _canvas.planeDistance = Mathf.Clamp(_canvas.planeDistance > 0f ? _canvas.planeDistance : 1f,
                    0.05f, 100f);
                _hasVrApplied = true;
            }
            else
            {
                // 没有相机：按要求"保持"原渲染模式，只记录状态。
                Debug.LogWarning(Tag + " VR active but no camera found; keep renderMode=" + _canvas.renderMode);
            }

            // 窗口宽度限制到画布 85%（只缩宽，不改高度/锚点/字号）。
            float canvasWidth = SafeCanvasWidth();
            if (canvasWidth > 1f)
            {
                float limitWidth = canvasWidth * VrWindowWidthRatio;
                var rect = _host.WindowRect;
                if (rect.width > limitWidth + 1f)
                {
                    _host.ApplyLayout(new Rect(rect.x, rect.y, limitWidth, rect.height), _host.IsMaximized, false);
                    _windowLimitCount++;
                }
            }

            _hasVrApplied = true;
            Debug.Log(Tag + " enter device=" + device + " renderMode=" + _canvas.renderMode
                + " worldCamera=" + (_canvas.worldCamera != null ? _canvas.worldCamera.name : "none")
                + " windowWidthMax=" + (SafeCanvasWidth() * VrWindowWidthRatio).ToString("F0")
                + " fontScale=1.0 状态=" + ModeVrUnverified);
        }

        private void ExitVr(string device)
        {
            _exitCount++;
            if (_canvas != null)
            {
                RestoreCanvasSnapshot();
            }

            if (_host != null && _preVrWindowRect.width > 1f && _preVrWindowRect.height > 1f)
            {
                try
                {
                    _host.ApplyLayout(_preVrWindowRect, _preVrMaximized, false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + " restore window rect failed: " + e.Message);
                }
            }

            _hasVrApplied = false;
            Debug.Log(Tag + " exit device=" + device + " restore renderMode="
                + (_canvas != null ? _canvas.renderMode.ToString() : "no-canvas")
                + " window=" + (_host != null ? _host.WindowRect.width.ToString("F0") : "-")
                + " 状态=" + ModeDesktop);
        }

        private void SnapshotCanvas()
        {
            _snapshot = new CanvasSnapshot
            {
                valid = true,
                renderMode = _canvas.renderMode,
                worldCamera = _canvas.worldCamera,
                planeDistance = _canvas.planeDistance,
                pixelPerfect = _canvas.pixelPerfect,
                overridePixelPerfect = _canvas.overridePixelPerfect,
                sortingOrder = _canvas.sortingOrder,
                targetDisplay = _canvas.targetDisplay
            };
        }

        private void RestoreCanvasSnapshot()
        {
            if (!_snapshot.valid || _canvas == null)
            {
                return;
            }

            try
            {
                _canvas.renderMode = _snapshot.renderMode;
                _canvas.worldCamera = _snapshot.worldCamera;
                _canvas.planeDistance = _snapshot.planeDistance;
                _canvas.pixelPerfect = _snapshot.pixelPerfect;
                _canvas.overridePixelPerfect = _snapshot.overridePixelPerfect;
                _canvas.sortingOrder = _snapshot.sortingOrder;
                _canvas.targetDisplay = _snapshot.targetDisplay;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " canvas restore failed: " + e.Message);
            }
            finally
            {
                _snapshot.valid = false;
            }
        }

        private float SafeCanvasWidth()
        {
            try
            {
                if (_host != null)
                {
                    return _host.CanvasSize.x;
                }

                return _canvas != null ? _canvas.pixelRect.width : 0f;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        // ------------------------------------------------------------- 状态查询 / 安全读取

        /// <summary>一行可读状态（自检/日志用）。</summary>
        public string Describe()
        {
            return "vr=" + (_vrActive ? "active" : "inactive")
                + " device=" + (_deviceName.Length > 0 ? _deviceName : "(none)")
                + " mode=" + ModeLabel
                + " canvas=" + (_canvas != null ? _canvas.renderMode.ToString() : "none")
                + " canvasCamera=" + (_canvas != null && _canvas.worldCamera != null
                    ? _canvas.worldCamera.name : "none")
                + " fontScale=" + FontScaleNote
                + " enter=" + _enterCount + " exit=" + _exitCount
                + " windowLimits=" + _windowLimitCount
                + " verified=" + (_enterCount > 0 ? "运行时" : "NOT TESTED(无头显)");
        }

        private static bool SafeVrActive()
        {
            try
            {
                return XRSettings.isDeviceActive;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SafeLoadedDeviceName()
        {
            try
            {
                string name = XRSettings.loadedDeviceName;
                return string.IsNullOrEmpty(name) ? string.Empty : name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}