// 工业数据台的宿主：把数据源 / 事件总线 / 快照导出 / 三个子页 / 主题 / 实验上下文条接起来。
//
// 关键约束：
//   - 只读既有数据链；不调用任何命令接口。
//   - 界面刷新频率由 TwinUiSettings.uiRefreshHz 驱动（5/10/20/30/60），运行时改立即生效。
//   - 编辑态（UI/14 截图）也走同一套 pipeline / view 代码：ConfigureForEditor + RefreshNow，
//     避免"截图是另写一套渲染代码"的证据失真。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiIndustrialHost : MonoBehaviour
    {
        [SerializeField] private TwinUiSettings settings;
        [SerializeField] private TwinUIStatusBinder binder;
        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private TwinUiContextBarView contextBar;
        [SerializeField] private TwinUiDataView dataView;
        [SerializeField] private TwinUiEventStreamView eventView;
        [SerializeField] private TwinUiSettingsView settingsView;
        [SerializeField] private GameObject[] pages = new GameObject[3];
        [SerializeField] private TwinUIButton[] subButtons = new TwinUIButton[3];
        [SerializeField] private bool autoStart = true;

        private readonly TwinUiPipeline _pipeline = new TwinUiPipeline();
        private TwinUiEventBus _events;
        private TwinUiSnapshotExport _export = new TwinUiSnapshotExport();
        private int _sub;
        private float _next;
        private float _observedWindowStart;
        private int _observedCount;
        private double _observedHz;
        private bool _started;

        public TwinUiPipeline Pipeline => _pipeline;
        public TwinUiSettings Settings => settings;
        public TwinUiEventBus Events => _events;
        public TwinUiSnapshotExport Export => _export;
        public int SubPage => _sub;
        public int RefreshCount => _pipeline.RefreshCount;
        public double ObservedHz => _observedHz;
        public double ConfiguredUiHz => settings != null ? settings.uiRefreshHz : 0d;

        public void Configure(TwinUiSettings settingsRef, TwinUIStatusBinder binderRef, DigitalTwinRuntime runtimeRef,
            TwinUiContextBarView ctx, TwinUiDataView data, TwinUiEventStreamView events, TwinUiSettingsView settingsUi)
        {
            settings = settingsRef;
            binder = binderRef;
            runtime = runtimeRef;
            contextBar = ctx;
            dataView = data;
            eventView = events;
            settingsView = settingsUi;
        }

        public void SetPages(GameObject[] pageObjects, TwinUIButton[] buttons)
        {
            pages = pageObjects;
            subButtons = buttons;
        }

        private void Awake()
        {
            if (autoStart && Application.isPlaying)
            {
                StartHost();
            }
        }

        private void Start()
        {
            if (autoStart && !_started)
            {
                StartHost();
            }
        }

        public void StartHost()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (settings == null)
            {
                Debug.LogWarning("[Twin UI] host started without settings asset; using in-memory defaults");
                settings = ScriptableObject.CreateInstance<TwinUiSettings>();
            }

            if (binder == null)
            {
                binder = GetComponentInParent<TwinUIStatusBinder>();
            }

            if (runtime == null)
            {
                binder = binder != null ? binder : FindObjectOfType<TwinUIStatusBinder>();
                runtime = binder != null ? binder.Runtime : null;
            }

            _events = new TwinUiEventBus(settings.eventLogPath);
            _events.Publish("session", TwinUITone.Ok, "UI_START",
                "industrial panel start; uiHz=" + settings.uiRefreshHz.ToString("F0")
                + " source=" + settings.defaultSource + " theme=" + settings.themeMode);
            TwinUITheme.SetMode(settings.themeMode);

            RebuildSource(settings.defaultSource, "startup");
            WireSubNav();
            ShowSub(0);
            RefreshNow();
        }

        public void RefreshNow()
        {
            _pipeline.Refresh();
            RenderAll();
        }

        private TMP_Text _chipText;
        private Image _chipDot;

        public void RenderAll()
        {
            var snapshot = _pipeline.Snapshot;
            RenderStatusChip(snapshot);
            if (contextBar != null)
            {
                contextBar.Render(snapshot, settings);
            }

            if (dataView != null && dataView.gameObject.activeInHierarchy)
            {
                dataView.Render(_pipeline, settings);
            }

            if (eventView != null && eventView.gameObject.activeInHierarchy)
            {
                eventView.Render(_pipeline, _events, _export, settings);
            }

            if (settingsView != null && settingsView.gameObject.activeInHierarchy)
            {
                settingsView.Render(settings, _pipeline);
            }
        }

        private void RenderStatusChip(TwinUiSnapshot snapshot)
        {
            if (_chipText == null)
            {
                var t = transform.Find("StatusChip/Text");
                _chipText = t == null ? null : t.GetComponent<TMP_Text>();
                var d = transform.Find("StatusChip/Dot");
                _chipDot = d == null ? null : d.GetComponent<Image>();
            }

            if (_chipText == null)
            {
                return;
            }

            int warn = 0;
            int crit = 0;
            var alerts = _pipeline.Alerts;
            for (int i = 0; i < alerts.Count; i++)
            {
                if (alerts[i].Tone >= (int)TwinUITone.Crit)
                {
                    crit++;
                }
                else if (alerts[i].Tone == (int)TwinUITone.Warn)
                {
                    warn++;
                }
            }

            bool hasFrame = snapshot != null && snapshot.Link.HasFrame;
            string text;
            Color color;
            Color dot;
            if (crit > 0)
            {
                text = "超限 " + crit;
                color = TwinUIStyle.CritTextColor;
                dot = TwinUIStyle.CritColor;
            }
            else if (warn > 0)
            {
                text = "预警 " + warn;
                color = TwinUIStyle.WarnColor;
                dot = TwinUIStyle.WarnColor;
            }
            else if (!hasFrame)
            {
                text = TwinUITextR3.NoData;
                color = TwinUIStyle.MutedColor;
                dot = TwinUIStyle.MutedColor;
            }
            else
            {
                text = "状态 正常";
                color = TwinUIStyle.OkColor;
                dot = TwinUIStyle.OkColor;
            }

            if (_chipText.text != text)
            {
                _chipText.text = text;
            }

            _chipText.color = color;
            if (_chipDot != null)
            {
                _chipDot.color = dot;
            }
        }

        private void Update()
        {
            if (!_started)
            {
                return;
            }

            if (settings == null)
            {
                return;
            }

            // 频率以 settings 为准：改 settings 后下一个采样点就按新频率走。
            float hz = Mathf.Max(1f, settings.uiRefreshHz);
            if (Time.unscaledTime < _next)
            {
                return;
            }

            _next = Time.unscaledTime + 1f / hz;
            RefreshNow();

            _observedCount++;
            float now = Time.realtimeSinceStartup;
            if (_observedWindowStart <= 0f)
            {
                _observedWindowStart = now;
            }
            else
            {
                double span = now - _observedWindowStart;
                if (span >= 1d)
                {
                    _observedHz = _observedCount / span;
                    _observedCount = 0;
                    _observedWindowStart = now;
                }
            }
        }

        private void WireSubNav()
        {
            if (subButtons == null)
            {
                return;
            }

            for (int i = 0; i < subButtons.Length; i++)
            {
                int index = i;
                var b = subButtons[i];
                if (b != null && b.Button != null)
                {
                    b.Button.onClick.RemoveAllListeners();
                    b.Button.onClick.AddListener(() => ShowSub(index));
                }
            }
        }

        public void ShowSub(int index)
        {
            _sub = Mathf.Clamp(index, 0, 2);
            if (pages != null)
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    if (pages[i] != null)
                    {
                        pages[i].SetActive(i == _sub);
                    }
                }
            }

            if (subButtons != null)
            {
                for (int i = 0; i < subButtons.Length; i++)
                {
                    if (subButtons[i] != null)
                    {
                        subButtons[i].SetSelected(i == _sub);
                    }
                }
            }

            if (_started)
            {
                RenderAll();
            }
        }

        // ------------------------------------------------------------ 设置入口

        public bool SetUiRefreshHz(float hz)
        {
            if (settings == null)
            {
                return false;
            }

            bool changed = settings.SetUiRefreshHz(hz);
            if (changed)
            {
                _pipeline.Refresh();
                RenderAll();
                if (_events != null)
                {
                    _events.Publish("settings", TwinUITone.Ok, "UI_HZ",
                        "uiHz -> " + hz.ToString("F0") + " (takes effect immediately)");
                }
            }

            return changed;
        }

        public void SetTheme(TwinUIThemeMode mode)
        {
            if (settings != null)
            {
                settings.SetTheme(mode);
            }

            TwinUITheme.SetMode(mode);
            TwinUITheme.Reapply();
            _pipeline.Refresh();
            RenderAll();
            if (_events != null)
            {
                _events.Publish("settings", TwinUITone.Ok, "THEME", "theme -> " + mode);
            }
        }

        public void SetDensity(TwinUIDensity density)
        {
            if (settings != null)
            {
                settings.SetDensity(density);
            }

            if (_events != null)
            {
                _events.Publish("settings", TwinUITone.Ok, "DENSITY", "density -> " + density);
            }
        }

        public bool SetSource(TwinUiSourceKind kind)
        {
            if (settings == null)
            {
                return false;
            }

            settings.SetSource(kind);
            RebuildSource(kind, "manual");
            _pipeline.Refresh();
            RenderAll();
            return true;
        }

        public void ToggleEventPause()
        {
            if (_events != null)
            {
                _events.Paused = !_events.Paused;
                _events.Publish("settings", TwinUITone.Ok, "EVENT_PAUSE",
                    "events paused=" + _events.Paused);
            }
        }

        public bool SaveSettings()
        {
            return settings != null && settings.PersistToAsset();
        }

        private void RebuildSource(TwinUiSourceKind kind, string reason)
        {
            ITwinUiDataSource source;
            switch (kind)
            {
                case TwinUiSourceKind.Replay:
                    source = new ReplayTwinUiDataSource(settings.replayCsvPath);
                    var replay = (ReplayTwinUiDataSource)source;
                    if (!replay.Loaded)
                    {
                        Debug.LogWarning("[Twin UI] replay source not loaded: " + replay.LoadError
                            + " (UI will show 未订阅/无数据)");
                    }

                    break;
                case TwinUiSourceKind.Mock:
                    source = new MockTwinUiDataSource();
                    Debug.LogWarning("[Twin UI] MOCK source active - UI shows MOCK watermark, not measured data");
                    break;
                default:
                    source = new RuntimeTwinUiDataSource(binder, runtime);
                    break;
            }

            if (_events == null)
            {
                _events = new TwinUiEventBus(settings.eventLogPath);
            }

            _pipeline.Bind(settings, source, _events, _export);
            if (reason != "startup")
            {
                _pipeline.SetSource(source, reason);
            }
        }

        // ------------------------------------------------------------ 编辑态入口（UI/14 截图用）

        /// <summary>编辑态接管：指定数据源并立即渲染一次（不依赖 Play 模式）。</summary>
        public void ConfigureForEditor(TwinUiSettings settingsRef, TwinUiSourceKind kind, string replayPath = null)
        {
            settings = settingsRef;
            _started = true;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TwinUiSettings>();
            }

            if (replayPath != null)
            {
                settings.replayCsvPath = replayPath;
            }

            if (binder == null)
            {
                binder = GetComponentInParent<TwinUIStatusBinder>();
            }

            if (runtime == null && binder != null)
            {
                runtime = binder.Runtime;
            }

            if (_events == null)
            {
                _events = new TwinUiEventBus(settings.eventLogPath);
            }

            TwinUITheme.SetMode(settings.themeMode);
            RebuildSource(kind, "editor");
            if (_events != null)
            {
                _events.Publish("session", TwinUITone.Ok, "EDITOR_PREVIEW",
                    "edit-mode snapshot run: source=" + kind + " (no Play mode, no scene save)");
            }

            WireSubNav();
            RefreshNow();
        }

        /// <summary>编辑态推进回放（秒）并重绘，用于生成有历史曲线的截图。</summary>
        public void StepReplayAndRender(double seconds)
        {
            var replay = _pipeline.Source as ReplayTwinUiDataSource;
            if (replay != null)
            {
                _pipeline.StepAndRefresh(seconds);
            }
            else
            {
                RefreshNow();
            }

            RenderAll();
        }

        public void SetSubPage(int index)
        {
            ShowSub(index);
        }

        public void ApplyThemeNow()
        {
            TwinUITheme.SetMode(settings != null ? settings.themeMode : TwinUITheme.Mode);
            var appliers = GetComponentsInParent<TwinUIThemeApplier>(true);
            for (int i = 0; i < appliers.Length; i++)
            {
                appliers[i].Apply();
            }

            var own = GetComponentsInChildren<TwinUIThemeApplier>(true);
            for (int i = 0; i < own.Length; i++)
            {
                own[i].Apply();
            }

            var rootApplier = transform.parent != null ? transform.parent.GetComponent<TwinUIThemeApplier>() : null;
            if (rootApplier != null)
            {
                rootApplier.Apply();
            }

            RenderAll();
        }
    }
}