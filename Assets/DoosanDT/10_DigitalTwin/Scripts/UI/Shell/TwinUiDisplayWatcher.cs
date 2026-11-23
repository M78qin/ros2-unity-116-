// TwinUiDisplayWatcher.cs —— 分辨率 / 多显示器变化监视（P9.5）
// 职责：每 0.5 s 检查 Screen 与 Display.main 的系统分辨率是否变化；变化时重新钳制窗口矩形 +
//       让悬浮球 RefreshLayout()，并打印一行 [DISPLAY] resolution a×b -> c×d。
// 约束：不自动创建（Lead 在 TwinUiShellV2 里装配）；只读状态；不改场景/Prefab。
using System;
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiDisplayWatcher : MonoBehaviour
    {
        public const float PollInterval = 0.5f;
        public const string Tag = "[DISPLAY]";

        private TwinUiWindowHost _host;
        private TwinUiMinimizeFab _fab;
        private float _nextPollTime;
        private int _screenWidth;
        private int _screenHeight;
        private int _displayWidth;
        private int _displayHeight;
        private int _displayCount;
        private int _changeCount;
        private Vector2Int _lastResolution;
        private bool _configured;

        /// <summary>分辨率变化次数（自 Configure 起）。</summary>
        public int ChangeCount => _changeCount;

        /// <summary>最近一次检测到的 Screen 分辨率。</summary>
        public Vector2Int LastResolution => _lastResolution;

        /// <summary>当前显示器数量（Display.displays.Length；编辑器里拿不到时返回 0）。</summary>
        public int CurrentDisplayCount
        {
            get { return SafeDisplayCount(); }
        }

        public bool IsConfigured => _configured;
        public TwinUiWindowHost Host => _host;
        public TwinUiMinimizeFab Fab => _fab;

        /// <summary>工厂方法（可选；Lead 也可直接 AddComponent + Configure）。</summary>
        public static TwinUiDisplayWatcher Create(TwinUiWindowHost host, TwinUiMinimizeFab fab)
        {
            var go = new GameObject("UiDisplayWatcher", typeof(RectTransform));
            var watcher = go.AddComponent<TwinUiDisplayWatcher>();
            watcher.Configure(host, fab);
            return watcher;
        }

        /// <summary>注入依赖并建立基线快照；幂等（重复调用会重建基线，不清零 ChangeCount）。</summary>
        public void Configure(TwinUiWindowHost host, TwinUiMinimizeFab fab)
        {
            _host = host;
            _fab = fab;
            CaptureBaseline();
            _nextPollTime = Time.unscaledTime + PollInterval;
            _configured = _host != null;
            Debug.Log(Tag + " watcher start " + DescribeBaseline());
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
            Poll();
        }

        private void Poll()
        {
            int width = Screen.width;
            int height = Screen.height;
            int displayWidth = SafeDisplayMainWidth();
            int displayHeight = SafeDisplayMainHeight();
            int displayCount = SafeDisplayCount();

            bool changed = width != _screenWidth || height != _screenHeight
                || displayWidth != _displayWidth || displayHeight != _displayHeight
                || displayCount != _displayCount;
            if (!changed)
            {
                return;
            }

            _changeCount++;
            Debug.Log(Tag + " resolution " + _screenWidth + "×" + _screenHeight + " -> " + width + "×" + height
                + " display=" + _displayWidth + "×" + _displayHeight + " -> " + displayWidth + "×" + displayHeight
                + " displays=" + _displayCount + " -> " + displayCount
                + " changes=" + _changeCount);

            _screenWidth = width;
            _screenHeight = height;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;
            _displayCount = displayCount;
            _lastResolution = new Vector2Int(width, height);

            // 1) 窗口矩形重新钳制（防分辨率缩小后窗口跑到屏外）。
            if (_host != null)
            {
                try
                {
                    _host.ApplyLayout(_host.WindowRect, _host.IsMaximized);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + " host.ApplyLayout failed: " + e.Message);
                }
            }

            // 2) 悬浮球按新画布重新钳位。
            if (_fab != null)
            {
                try
                {
                    _fab.RefreshLayout();
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + " fab.RefreshLayout failed: " + e.Message);
                }
            }
        }

        private void CaptureBaseline()
        {
            _screenWidth = SafeScreenWidth();
            _screenHeight = SafeScreenHeight();
            _displayWidth = SafeDisplayMainWidth();
            _displayHeight = SafeDisplayMainHeight();
            _displayCount = SafeDisplayCount();
            _lastResolution = new Vector2Int(_screenWidth, _screenHeight);
        }

        /// <summary>一行可读状态（自检/日志用）。</summary>
        public string Describe()
        {
            return "display screen=" + SafeScreenWidth() + "x" + SafeScreenHeight()
                + " last=" + _lastResolution.x + "x" + _lastResolution.y
                + " system=" + SafeDisplayMainWidth() + "x" + SafeDisplayMainHeight()
                + " displays=" + CurrentDisplayCount
                + " changes=" + _changeCount;
        }

        private string DescribeBaseline()
        {
            return "baseline screen=" + _screenWidth + "x" + _screenHeight
                + " system=" + _displayWidth + "x" + _displayHeight
                + " displays=" + _displayCount + " poll=" + PollInterval + "s";
        }

        // ------------------------------------------------------------- 安全读取（编辑器/无头环境不抛异常）

        private static int SafeScreenWidth()
        {
            try
            {
                return Screen.width;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int SafeScreenHeight()
        {
            try
            {
                return Screen.height;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static Display SafeMainDisplay()
        {
            try
            {
                return Display.main;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int SafeDisplayMainWidth()
        {
            var display = SafeMainDisplay();
            try
            {
                return display != null ? display.systemWidth : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int SafeDisplayMainHeight()
        {
            var display = SafeMainDisplay();
            try
            {
                return display != null ? display.systemHeight : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int SafeDisplayCount()
        {
            try
            {
                var displays = Display.displays;
                return displays != null ? displays.Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}