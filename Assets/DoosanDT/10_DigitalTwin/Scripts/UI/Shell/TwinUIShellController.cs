// Unified entry panel for Doosan-DT: one header/tab/status shell on top of the five legacy
// canvases, which stay untouched as floating panels.
//
// Scope of responsibility:
//   * tabs -> TwinDemoModeRouter (roaming / training / disassembly / data)
//   * a status strip that reports connection / recording / data source honestly
//   * nothing else: no data writes, no command sending, no modification of the legacy canvases.
//
// The shell deliberately polls (0.5 s) instead of subscribing to runtime events: DigitalTwinRuntime
// exposes no change event, and adding one would mean touching the data-chain class.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    [Serializable]
    public sealed class TwinUIShellBindings
    {
        public GameObject shellRoot;
        public TwinDemoModeRouter router;
        public BatteryLoginGate gate;

        public Button[] tabButtons = new Button[0];
        public TMP_Text[] tabLabels = new TMP_Text[0];
        public Image[] tabFills = new Image[0];

        public TMP_Text titleText;
        public TMP_Text hintText;
        public TMP_Text modeText;
        public TMP_Text sourceText;
        public TMP_Text statusText;
        public TMP_Text recordText;

        public DigitalTwinRuntime runtime;
        public TwinRecorder recorder;

        public int defaultTabIndex;
        public Color activeTabColor = new Color(0.16f, 0.55f, 0.95f, 1f);
        public Color inactiveTabColor = new Color(0.20f, 0.24f, 0.30f, 1f);
        public Color activeTabTextColor = Color.white;
        public Color inactiveTabTextColor = new Color(0.78f, 0.82f, 0.88f, 1f);
    }

    [DisallowMultipleComponent]
    public sealed class TwinUIShellController : MonoBehaviour
    {
        [SerializeField] private TwinUIShellBindings bindings = new TwinUIShellBindings();
        [SerializeField] private float statusRefreshHz = 2f;

        private float _statusAccumulator;
        private int _selectedTab = -1;

        public int SelectedTab => _selectedTab;

        private void Awake()
        {
            BindTabsIfNeeded();
            if (bindings.router != null)
            {
                bindings.router.ModeChanged += OnModeChanged;
            }
        }

        private void OnDestroy()
        {
            if (bindings.router != null)
            {
                bindings.router.ModeChanged -= OnModeChanged;
            }
        }

        private void Start()
        {
            if (bindings.titleText != null)
            {
                bindings.titleText.text = TwinUIShellText.ProductTitle;
            }

            // highlight whatever the scene is already in; do not force a mode on Play start
            int current = bindings.router != null ? (int)bindings.router.CurrentMode : bindings.defaultTabIndex;
            RenderTabs(current);
            ApplyHint(current);
            RefreshStatus();
        }

        private void Update()
        {
            float interval = 1f / Mathf.Max(0.25f, statusRefreshHz);
            _statusAccumulator += Time.unscaledDeltaTime;
            if (_statusAccumulator < interval)
            {
                return;
            }

            _statusAccumulator = 0f;
            RefreshStatus();
        }

        /// <summary>Used by the welcome page: reveal the shell on its default tab.</summary>
        public void ShowDefaultTab()
        {
            SelectTab(bindings.defaultTabIndex);
        }

        /// <summary>Switch teaching mode (index matches TwinDemoMode order).</summary>
        public void SelectTab(int index)
        {
            int count = bindings.tabButtons == null ? 4 : Mathf.Max(1, bindings.tabButtons.Length);
            index = Mathf.Clamp(index, 0, count - 1);
            _selectedTab = index;

            var mode = (TwinDemoMode)index;
            if (bindings.router != null)
            {
                bindings.router.EnterMode(mode);
            }

            // render unconditionally: in edit mode Awake never runs, so the router event has no
            // subscriber and relying on ModeChanged would leave the tab highlight stale
            RenderTabs(index);
            ApplyHint(index);
            RefreshStatus();
        }

        private void OnModeChanged(TwinDemoMode mode)
        {
            RenderTabs((int)mode);
            ApplyHint((int)mode);
            RefreshStatus();
        }

        private void BindTabsIfNeeded()
        {
            if (bindings.tabButtons == null)
            {
                return;
            }

            for (int i = 0; i < bindings.tabButtons.Length; i++)
            {
                Button button = bindings.tabButtons[i];
                if (button == null || button.onClick.GetPersistentEventCount() > 0)
                {
                    continue;
                }

                int captured = i;
                button.onClick.AddListener(() => SelectTab(captured));
            }
        }

        private void RenderTabs(int activeIndex)
        {
            _selectedTab = activeIndex;

            for (int i = 0; i < (bindings.tabButtons == null ? 0 : bindings.tabButtons.Length); i++)
            {
                bool isActive = i == activeIndex;
                if (bindings.tabFills != null && i < bindings.tabFills.Length && bindings.tabFills[i] != null)
                {
                    bindings.tabFills[i].color = isActive ? bindings.activeTabColor : bindings.inactiveTabColor;
                }

                if (bindings.tabLabels != null && i < bindings.tabLabels.Length && bindings.tabLabels[i] != null)
                {
                    bindings.tabLabels[i].color = isActive ? bindings.activeTabTextColor : bindings.inactiveTabTextColor;
                }
            }
        }

        private void ApplyHint(int index)
        {
            if (bindings.hintText == null)
            {
                return;
            }

            switch ((TwinDemoMode)index)
            {
                case TwinDemoMode.Training:
                    bindings.hintText.text = TwinUIShellText.HintTraining;
                    break;
                case TwinDemoMode.Disassembly:
                    bindings.hintText.text = TwinUIShellText.HintDisassembly;
                    break;
                case TwinDemoMode.Data:
                    bindings.hintText.text = TwinUIShellText.HintData;
                    break;
                default:
                    bindings.hintText.text = TwinUIShellText.HintRoaming;
                    break;
            }
        }

        private void RefreshStatus()
        {
            bool connected = false;
            bool hasFrame = false;

            if (bindings.runtime != null)
            {
                StateSnapshot snapshot = bindings.runtime.LatestSnapshot;
                hasFrame = snapshot != null && snapshot.HasFrame;
                connected = hasFrame;
            }

            if (bindings.statusText != null)
            {
                bindings.statusText.text = TwinUIShellText.StatusPrefix + ": "
                    + (connected ? TwinUIShellText.StatusConnected : TwinUIShellText.StatusDisconnected);
            }

            if (bindings.modeText != null)
            {
                bindings.modeText.text = TwinUIShellText.ModePrefix + ": " + ModeLabel(_selectedTab);
            }

            if (bindings.sourceText != null)
            {
                string source = TwinUIShellText.SourceNone;
                if (bindings.runtime != null && bindings.runtime.ActiveSourceKind != RuntimeSourceKind.None)
                {
                    source = bindings.runtime.ActiveSourceKind.ToString().ToUpperInvariant();
                }

                bindings.sourceText.text = TwinUIShellText.SourcePrefix + ": " + source;
            }

            if (bindings.recordText != null)
            {
                bool recording = bindings.recorder != null && bindings.recorder.CsvReady && bindings.recorder.PendingCount > 0;
                bindings.recordText.text = recording ? TwinUIShellText.RecordingOn : TwinUIShellText.RecordingOff;
            }
        }

        private static string ModeLabel(int index)
        {
            switch ((TwinDemoMode)index)
            {
                case TwinDemoMode.Training: return TwinUIShellText.TabTraining;
                case TwinDemoMode.Disassembly: return TwinUIShellText.TabDisassembly;
                case TwinDemoMode.Data: return TwinUIShellText.TabData;
                default: return TwinUIShellText.TabRoaming;
            }
        }

        // ------------------------------------------------------------ editor wiring

        public void Bind(TwinUIShellBindings value)
        {
            bindings = value ?? new TwinUIShellBindings();
            BindTabsIfNeeded();
        }

        public TwinUIShellBindings Bindings => bindings;
    }
}