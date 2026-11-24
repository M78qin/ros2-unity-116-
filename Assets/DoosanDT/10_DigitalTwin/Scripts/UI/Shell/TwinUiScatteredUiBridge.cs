// TwinUiScatteredUiBridge.cs —— 散落 UI 收编器（P9，仅在 V2 模式运行）
// 问题：V2 主面板之外还有一批"各自为政"的显示层：性能/拆解/射线/悬停/通信 4 个 IMGUI 窗、
//       射线与 IK 的世界 HUD、欢迎页（BatteryLoginGate）、旧壳控制器（TwinUIShellController）、
//       演示模式路由（TwinDemoModeRouter）与旧控制面板（TwinCommandPanelView）。
// 做法：V2 里统一关掉这些**独立显示**（数据链/采样/控制安全门一律不动），把它们的信息搬进主面板卡片：
//       数据链卡、性能卡、告警/会话卡、选中对象卡、IK 预览卡、控制指令卡、漫游操作卡。
// 安全边界：本类只改"显示开关"与"组件 enabled"，不改采样频率、不停止录制、不下发命令；Legacy 模式完全不介入。
using System;
using DigitalTwin;
using DigitalTwin.Interaction;
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiScatteredUiBridge : MonoBehaviour
    {
        public const string Tag = "[SCATTER]";
        public const float ReassertInterval = 0.5f;

        private PerformanceProfiler _perf;
        private DisassemblySystem _disassembly;
        private SmartRaycaster _raycaster;
        private DartStudioBridge _dart;
        private TwinPlayRayVisual _rayVisual;
        private TwinPlayIkHandle _ikHandle;
        private BatteryLoginGate _loginGate;
        private TwinUIShellController _legacyShell;
        private TwinDemoModeRouter _demoRouter;
        private TwinCommandPanelView _commandPanel;
        private GameObject _loginPanel;
        private float _nextAssert;
        private int _suppressCount;
        private int _reassertCount;

        public int SuppressCount => _suppressCount;
        public int ReassertCount => _reassertCount;
        public bool PerfGuiOff => _perf == null || !_perf.IsOnScreenUIVisible();
        public bool DisassemblyOverlayOff => _disassembly == null
            || (!_disassembly.showDebugOverlay
                && (_disassembly.debugSettings == null || !_disassembly.debugSettings.debugVisible));
        public bool RayHudOff => _rayVisual == null || !_rayVisual.ShowHud;
        public bool IkHudOff => _ikHandle == null || !_ikHandle.HudCreated || _ikHandle.HudCanvasRoot == null
            || !_ikHandle.HudCanvasRoot.activeSelf;
        public bool HoverPanelsOff => _raycaster == null
            || (!_raycaster.hoverInfoSettings.showHoverInfoPanel
                && !_raycaster.hoverInfoSettings.showFloatingHoverTag);
        public bool LoginHidden => _loginGate == null
            || (!_loginGate.enabled
                && (_loginPanel == null || !_loginPanel.activeSelf));
        public bool LegacyShellOff => _legacyShell == null || !_legacyShell.enabled;
        public bool DemoRouterOff => _demoRouter == null || !_demoRouter.enabled;
        public bool CommandPanelOff => _commandPanel == null || !_commandPanel.enabled;
        public bool AllSuppressed => PerfGuiOff && DisassemblyOverlayOff && RayHudOff && IkHudOff
            && HoverPanelsOff && LoginHidden && LegacyShellOff && DemoRouterOff && CommandPanelOff;

        public TwinPlayRayVisual RayVisual => _rayVisual;
        public TwinPlayIkHandle IkHandle => _ikHandle;
        public SmartRaycaster Raycaster => _raycaster;
        public TwinCommandPanelView LegacyCommandPanel => _commandPanel;

        public void Configure()
        {
            ResolveAll();
            AssertAll("boot");
        }

        private void ResolveAll()
        {
            _perf = UnityEngine.Object.FindObjectOfType<PerformanceProfiler>();
            _disassembly = DisassemblySystem.Instance != null
                ? DisassemblySystem.Instance
                : UnityEngine.Object.FindObjectOfType<DisassemblySystem>();
            _raycaster = UnityEngine.Object.FindObjectOfType<SmartRaycaster>();
            _dart = UnityEngine.Object.FindObjectOfType<DartStudioBridge>();
            _rayVisual = UnityEngine.Object.FindObjectOfType<TwinPlayRayVisual>();
            _ikHandle = UnityEngine.Object.FindObjectOfType<TwinPlayIkHandle>();
            _loginGate = UnityEngine.Object.FindObjectOfType<BatteryLoginGate>();
            _legacyShell = UnityEngine.Object.FindObjectOfType<TwinUIShellController>();
            _demoRouter = UnityEngine.Object.FindObjectOfType<TwinDemoModeRouter>();
            _commandPanel = UnityEngine.Object.FindObjectOfType<TwinCommandPanelView>();

            var all = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "LoginPanel")
                {
                    _loginPanel = all[i].gameObject;
                    break;
                }
            }

            Debug.Log(Tag + " resolved perf=" + (_perf != null) + " disassembly=" + (_disassembly != null)
                + " raycaster=" + (_raycaster != null) + " dart=" + (_dart != null)
                + " rayVisual=" + (_rayVisual != null) + " ik=" + (_ikHandle != null)
                + " loginGate=" + (_loginGate != null) + " loginPanel=" + (_loginPanel != null)
                + " legacyShell=" + (_legacyShell != null) + " demoRouter=" + (_demoRouter != null)
                + " commandPanel=" + (_commandPanel != null));
        }

        private void Update()
        {
            if (!TwinUiShellMode.IsV2)
            {
                return;
            }

            if (Time.unscaledTime < _nextAssert)
            {
                return;
            }

            _nextAssert = Time.unscaledTime + ReassertInterval;
            AssertAll("tick");
        }

        /// <summary>把散落显示层压回"关闭"状态；幂等，变化才计数/打日志。</summary>
        public void AssertAll(string reason)
        {
            int before = _suppressCount;
            _reassertCount++;

            if (_perf != null && _perf.IsOnScreenUIVisible())
            {
                _perf.SetOnScreenUI(false, "v2 bridge:" + reason);
                _suppressCount++;
            }

            if (_disassembly != null)
            {
                if (_disassembly.showDebugOverlay)
                {
                    _disassembly.showDebugOverlay = false;
                    _suppressCount++;
                }

                if (_disassembly.debugSettings != null && _disassembly.debugSettings.debugVisible)
                {
                    _disassembly.debugSettings.debugVisible = false;
                    _suppressCount++;
                }
            }

            if (_raycaster != null)
            {
                var hover = _raycaster.hoverInfoSettings;
                if (hover != null)
                {
                    if (hover.showHoverInfoPanel)
                    {
                        hover.showHoverInfoPanel = false;
                        _suppressCount++;
                    }

                    if (hover.showFloatingHoverTag)
                    {
                        hover.showFloatingHoverTag = false;
                        _suppressCount++;
                    }
                }
            }

            if (_dart != null && _dart.IsDebugGuiVisible)
            {
                _dart.SetDebugGuiVisible(false);
                _suppressCount++;
            }

            if (_rayVisual != null && _rayVisual.ShowHud)
            {
                _rayVisual.SetHudEnabled(false);
                _suppressCount++;
            }

            if (_ikHandle != null && _ikHandle.HudCanvasRoot != null && _ikHandle.HudCanvasRoot.activeSelf)
            {
                _ikHandle.SetHudEnabled(false);
                _suppressCount++;
            }

            if (_loginPanel != null && _loginPanel.activeSelf)
            {
                _loginPanel.SetActive(false);
                _suppressCount++;
            }

            if (_loginGate != null && _loginGate.enabled)
            {
                _loginGate.enabled = false;
                _suppressCount++;
            }

            if (_legacyShell != null && _legacyShell.enabled)
            {
                _legacyShell.enabled = false;
                _suppressCount++;
            }

            if (_demoRouter != null && _demoRouter.enabled)
            {
                _demoRouter.enabled = false;
                _suppressCount++;
            }

            if (_commandPanel != null && _commandPanel.enabled)
            {
                _commandPanel.enabled = false;
                _suppressCount++;
            }

            if (_suppressCount != before)
            {
                Debug.Log(Tag + " suppressed delta=" + (_suppressCount - before) + " total=" + _suppressCount
                    + " reason=" + reason + " all=" + AllSuppressed);
            }
        }

        public string Describe()
        {
            return "scatter perfOff=" + PerfGuiOff + " disassemblyOff=" + DisassemblyOverlayOff
                + " rayHudOff=" + RayHudOff + " ikHudOff=" + IkHudOff + " hoverOff=" + HoverPanelsOff
                + " loginHidden=" + LoginHidden + " legacyShellOff=" + LegacyShellOff
                + " demoRouterOff=" + DemoRouterOff + " commandPanelOff=" + CommandPanelOff
                + " suppressed=" + _suppressCount + " reassert=" + _reassertCount
                + " all=" + AllSuppressed;
        }
    }
}