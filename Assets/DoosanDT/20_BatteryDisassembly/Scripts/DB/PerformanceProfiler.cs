using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 性能监测脚本
/// 用于拆解实验的实时数据采样、屏幕显示和 CSV 导出。
/// 数据尽量来源于当前场景的真实运行状态，而不是预设值。
/// </summary>
public class PerformanceProfiler : MonoBehaviour
{
    private struct MetricEntry
    {
        public readonly string Label;
        public readonly string Value;
        public readonly string Detail;

        public MetricEntry(string label, string value, string detail)
        {
            Label = label;
            Value = value;
            Detail = detail;
        }
    }

    [Header("监测范围")]
    [Tooltip("统计当前实验场景的根节点。留空时可自动回退到 DisassemblySystem 根物体。")]
    public Transform targetParent;
    [Tooltip("当 targetParent 为空时，自动使用 DisassemblySystem.transform 作为统计根节点")]
    public bool autoUseSystemRoot = true;

    [Header("采样与导出")]
    [Tooltip("实时统计刷新间隔（秒）")]
    public float sampleInterval = 0.5f;
    [Tooltip("CSV 写入间隔（秒）")]
    public float logInterval = 0.5f;
    [Tooltip("CSV 文件保存目录（留空则使用 Application.persistentDataPath/PerformanceLogs）")]
    public string customLogPath = "";
    [InspectorName("按时间建目录")]
    [Tooltip("是否在导出目录外再按时间创建子目录，便于分组保存每次实验")]
    public bool appendTimestampFolder = true;
    [InspectorName("导出文件名")]
    [Tooltip("CSV 导出文件名，可自定义；留空时默认使用 PerfLog.csv")]
    public string csvFileName = "PerfLog";
    [InspectorName("文件名追加日期时间")]
    [Tooltip("是否在导出文件名后附加时间戳；关闭后文件名示例：PerfLog.csv，开启后示例：PerfLog_20260331_210307.csv")]
    public bool appendTimestampToFileName = false;

    [Header("采集项开关")]
    public bool recordFPS = true;
    public bool recordFrameTime = true;
    public bool recordEngineMemory = true;
    public bool recordManagedMemory = true;
    public bool recordProcessMemory = true;
    public bool recordActiveColliders = true;
    public bool recordColliderBreakdown = true;
    public bool recordActiveRenderers = true;
    public bool recordActiveTriangles = true;
    public bool recordActiveVertices = true;
    public bool recordPartCounts = true;
    public bool recordLayerProgress = true;

    [System.Serializable]
    public class UISettings
    {
        [Tooltip("性能面板位置与大小")]
        public Rect uiRect = new Rect(10, 10, 460, 320);
        [Tooltip("勾选后，自动贴合屏幕边缘")]
        public bool uiClampToScreen = true;
        [Tooltip("面板标题")]
        public string uiTitle = "性能监测";
        [Tooltip("面板副标题")]
        public string uiSubtitle = "Runtime experimental dashboard";
        [Tooltip("性能面板统一缩放")]
        public float uiScale = 1f;
        [Tooltip("显示/隐藏性能面板的快捷键")]
        public KeyCode toggleVisibilityKey = KeyCode.U;
        [Tooltip("标题字号")]
        public int uiTitleFontSize = 20;
        [Tooltip("正文标签字号")]
        public int uiBodyFontSize = 13;
        [Tooltip("指标值字号")]
        public int uiValueFontSize = 18;
        [Tooltip("脚注字号")]
        public int uiFootnoteFontSize = 11;
        [Tooltip("每行显示多少个指标卡片")]
        public int metricColumns = 2;
        [Tooltip("面板内边距")]
        public float uiPadding = 12f;
        [Tooltip("模块间距")]
        public float uiSectionGap = 10f;
        [Tooltip("指标卡片间距")]
        public float uiMetricGap = 8f;
        [Tooltip("指标卡片高度")]
        public float metricCardHeight = 58f;
        public Color uiBackgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.96f);
        public Color uiBorderColor = new Color(1f, 1f, 1f, 0.12f);
        public Color uiAccentColor = new Color(0.15f, 0.85f, 1f, 1f);
        public Color uiAccentMutedColor = new Color(0.15f, 0.85f, 1f, 0.22f);
        public Color uiTitleColor = Color.white;
        public Color uiTextColor = new Color(0.86f, 0.90f, 0.95f, 1f);
        public Color uiMutedColor = new Color(0.70f, 0.75f, 0.80f, 1f);
        public Color uiValueColor = Color.white;
        public Color uiWarningColor = new Color(0.95f, 0.72f, 0.20f, 1f);
        [Tooltip("是否显示数据源说明")]
        public bool showSourceNote = true;
        [Tooltip("是否显示 CSV 导出提示")]
        public bool showCSVHint = true;
    }

    [Header("屏幕 UI")]
    public bool showOnScreenUI = true;
    public UISettings uiSettings = new UISettings();

    private float _fps;
    private float _frameMs;
    private float _engineAllocatedMB;
    private float _engineReservedMB;
    private float _engineUnusedReservedMB;
    private float _managedMB;
    private float _processWorkingSetMB;
    private int _activeColliders;
    private int _activeBoxColliders;
    private int _activeMeshColliders;
    private int _activeOtherColliders;
    private int _activeRenderers;
    private int _activeVertices;
    private long _activeTriangles;
    private int _totalParts;
    private int _remainingParts;
    private int _interactableParts;
    private int _currentLayerTotalParts;
    private int _layerCount;
    private int _priorityGroupCount;
    private float _currentLayerProgress;
    private string _currentMode = "未知";
    private string _currentLayerPhase = "N/A";
    private string _currentLayerSummary = "N/A";
    private string _scopeName = "N/A";
    private float _fpsAccum;
    private int _fpsFrames;
    private float _fpsTimer;
    private float _statsTimer;
    private float _logTimer;

    private StreamWriter _writer;
    private bool _fileReady;
    private string _csvPath = string.Empty;
    private string _lastCustomLogPath = string.Empty;
    private string _lastCsvFileName = string.Empty;
    private bool _lastAppendTimestampFolder;
    private bool _lastAppendTimestampToFileName;

    // 可折叠面板
    private CollapsiblePanel _performancePanel;
    private bool _panelInitialized = false;

    private void Start()
    {
        ClampInspectorValues();
        InitCSV();
        _logTimer = logInterval;

        // 初始化可折叠面板
        _performancePanel = new CollapsiblePanel(uiSettings.uiTitle, uiSettings.uiRect);
        ApplyPanelSettings();
        SetPanelVisibility(showOnScreenUI);
        _panelInitialized = true;
    }

    private void OnValidate()
    {
        ClampInspectorValues();
        if (_performancePanel != null)
            ApplyPanelSettings();
    }

    private void Update()
    {
        if (Input.GetKeyDown(uiSettings.toggleVisibilityKey))
            SetPanelVisibility(!IsPanelVisible());

        if (_performancePanel != null)
        {
            uiSettings.uiRect = _performancePanel.position;
            uiSettings.uiScale = _performancePanel.scale;
            showOnScreenUI = _performancePanel.isVisible;
        }

        RefreshCsvOutputIfNeeded();

        float dt = Time.unscaledDeltaTime;
        _frameMs = dt * 1000f;

        if (recordFPS)
        {
            _fpsAccum += dt > 0f ? 1f / dt : 0f;
            _fpsFrames++;
            _fpsTimer += dt;
            if (_fpsTimer >= Mathf.Max(0.05f, sampleInterval))
            {
                _fps = _fpsFrames > 0 ? _fpsAccum / _fpsFrames : 0f;
                _fpsAccum = 0f;
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }
        }

        _statsTimer += dt;
        if (_statsTimer >= Mathf.Max(0.05f, sampleInterval))
        {
            CollectSceneStats();
            _statsTimer = 0f;
        }

        _logTimer -= Time.deltaTime;
        if (_logTimer <= 0f && _fileReady)
        {
            WriteRow();
            _logTimer = Mathf.Max(0.05f, logInterval);
        }
    }

    private void ClampInspectorValues()
    {
        sampleInterval = Mathf.Max(0.05f, sampleInterval);
        logInterval = Mathf.Max(0.05f, logInterval);
        csvFileName = string.IsNullOrWhiteSpace(csvFileName) ? "PerfLog" : csvFileName.Trim();
        uiSettings.metricColumns = Mathf.Clamp(uiSettings.metricColumns, 1, 3);
        uiSettings.uiScale = Mathf.Clamp(uiSettings.uiScale, 0.5f, 2.5f);
        uiSettings.metricCardHeight = Mathf.Max(42f, uiSettings.metricCardHeight);
        uiSettings.uiPadding = Mathf.Max(0f, uiSettings.uiPadding);
        uiSettings.uiSectionGap = Mathf.Max(0f, uiSettings.uiSectionGap);
        uiSettings.uiMetricGap = Mathf.Max(0f, uiSettings.uiMetricGap);
    }

    private void CollectSceneStats()
    {
        _activeColliders = 0;
        _activeBoxColliders = 0;
        _activeMeshColliders = 0;
        _activeOtherColliders = 0;
        _activeRenderers = 0;
        _activeVertices = 0;
        _activeTriangles = 0;
        _totalParts = 0;
        _remainingParts = 0;
        _interactableParts = 0;
        _currentLayerTotalParts = 0;
        _layerCount = 0;
        _priorityGroupCount = 0;
        _currentLayerProgress = 0f;
        _currentMode = "无系统";
        _currentLayerPhase = "N/A";
        _currentLayerSummary = "N/A";
        _scopeName = "N/A";

        DisassemblySystem sys = DisassemblySystem.Instance;
        Transform scopeRoot = ResolveScopeRoot(sys);
        if (scopeRoot != null)
            _scopeName = scopeRoot.name;

        if (sys == null)
        {
            CaptureGlobalMemoryStats();
            return;
        }

        _currentMode = sys.detectionMode switch
        {
            DisassemblySystem.CollisionMode.Coarse => "粗模式(Coarse)",
            DisassemblySystem.CollisionMode.Fine => "精模式(Fine)",
            DisassemblySystem.CollisionMode.Hybrid => "混合模式(Hybrid)",
            _ => sys.detectionMode.ToString()
        };

        if (sys.layers != null)
            _layerCount = sys.layers.Count;

        LayerGroupConfig currentLayer = null;
        if (sys.currentLayerIndex >= 0 && sys.currentLayerIndex < sys.layers.Count)
            currentLayer = sys.layers[sys.currentLayerIndex];

        if (currentLayer != null)
        {
            _currentLayerPhase = currentLayer.GetCurrentPhaseDescription();
            _currentLayerSummary = currentLayer.GetDebugSummary();
            _currentLayerTotalParts = currentLayer.allParts != null ? currentLayer.allParts.Count : 0;
            _remainingParts = currentLayer.GetRemainingPartCount();
            _interactableParts = currentLayer.GetCurrentInteractableCount();
            _priorityGroupCount = CountValidPriorityGroups(currentLayer);
            if (_currentLayerTotalParts > 0)
                _currentLayerProgress = Mathf.Clamp01(1f - (float)_remainingParts / _currentLayerTotalParts);
        }
        else if (sys.isSystemCompleted)
        {
            _currentLayerPhase = "全部完成";
            _currentLayerSummary = "系统已完成";
            _currentLayerProgress = 1f;
        }

        if (scopeRoot != null)
        {
            DisassemblyPart[] allParts = scopeRoot.GetComponentsInChildren<DisassemblyPart>(true);
            _totalParts = allParts != null ? allParts.Length : 0;
        }

        if (recordPartCounts || recordActiveColliders || recordActiveRenderers || recordActiveTriangles || recordActiveVertices)
        {
            List<DisassemblyPart> sampleParts = currentLayer != null ? currentLayer.GetCurrentInteractableParts() : new List<DisassemblyPart>();
            CollectInteractableStats(sampleParts, scopeRoot);
        }

        CaptureGlobalMemoryStats();
    }

    private void CollectInteractableStats(List<DisassemblyPart> sampleParts, Transform scopeRoot)
    {
        if (sampleParts == null) return;

        for (int i = 0; i < sampleParts.Count; i++)
        {
            DisassemblyPart part = sampleParts[i];
            if (part == null || part.isCompleted) continue;
            if (scopeRoot != null && !IsUnderTarget(part.transform, scopeRoot)) continue;

            if (recordActiveColliders || recordColliderBreakdown)
            {
                Collider[] colliders = part.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < colliders.Length; c++)
                {
                    Collider col = colliders[c];
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                    _activeColliders++;
                    if (col is BoxCollider) _activeBoxColliders++;
                    else if (col is MeshCollider) _activeMeshColliders++;
                    else _activeOtherColliders++;
                }
            }

            if (recordActiveRenderers)
            {
                Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend != null && rend.enabled && rend.gameObject.activeInHierarchy)
                        _activeRenderers++;
                }
            }

            if (recordActiveTriangles || recordActiveVertices)
            {
                MeshFilter[] filters = part.GetComponentsInChildren<MeshFilter>(true);
                for (int f = 0; f < filters.Length; f++)
                {
                    MeshFilter mf = filters[f];
                    if (mf == null || !mf.gameObject.activeInHierarchy) continue;
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null) continue;

                    if (recordActiveVertices)
                        _activeVertices += mesh.vertexCount;

                    if (recordActiveTriangles)
                    {
                        if (!mesh.isReadable)
                            continue;

                        long triangles = 0;
                        for (int s = 0; s < mesh.subMeshCount; s++)
                            triangles += (long)mesh.GetIndexCount(s) / 3L;
                        _activeTriangles += triangles;
                    }
                }
            }
        }
    }

    private void CaptureGlobalMemoryStats()
    {
        if (recordEngineMemory)
        {
            _engineAllocatedMB = Profiler.GetTotalAllocatedMemoryLong() / 1024f / 1024f;
            _engineReservedMB = Profiler.GetTotalReservedMemoryLong() / 1024f / 1024f;
            _engineUnusedReservedMB = Profiler.GetTotalUnusedReservedMemoryLong() / 1024f / 1024f;
        }

        if (recordManagedMemory)
            _managedMB = System.GC.GetTotalMemory(false) / 1024f / 1024f;

        if (recordProcessMemory)
        {
            try
            {
                _processWorkingSetMB = (float)System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024f / 1024f;
            }
            catch
            {
                _processWorkingSetMB = 0f;
            }
        }
    }

    private Transform ResolveScopeRoot(DisassemblySystem sys)
    {
        if (targetParent != null)
            return targetParent;

        if (!autoUseSystemRoot)
            return null;

        return sys != null ? sys.transform : null;
    }

    private bool IsUnderTarget(Transform t, Transform targetRoot)
    {
        while (t != null)
        {
            if (t == targetRoot) return true;
            t = t.parent;
        }
        return false;
    }

    private int CountValidPriorityGroups(LayerGroupConfig layer)
    {
        if (layer == null || layer.priorityGroups == null) return 0;
        int count = 0;
        for (int i = 0; i < layer.priorityGroups.Count; i++)
        {
            LayerGroupConfig.PriorityGroup group = layer.priorityGroups[i];
            if (group != null && group.IsValid()) count++;
        }
        return count;
    }

    private List<MetricEntry> BuildMetricEntries()
    {
        List<MetricEntry> entries = new List<MetricEntry>();

        if (recordFPS)
            entries.Add(new MetricEntry("FPS", _fps.ToString("F1"), "实时帧率"));
        if (recordFrameTime)
            entries.Add(new MetricEntry("帧时长", $"{_frameMs:F2} ms", "当前帧"));
        if (recordEngineMemory)
            entries.Add(new MetricEntry("引擎内存", $"{_engineAllocatedMB:F1} MB", "Profiler 分配"));
        if (recordManagedMemory)
            entries.Add(new MetricEntry("管理堆", $"{_managedMB:F1} MB", "GC 可见"));
        if (recordProcessMemory)
            entries.Add(new MetricEntry("进程工作集", $"{_processWorkingSetMB:F1} MB", "系统进程占用"));
        if (recordActiveColliders)
            entries.Add(new MetricEntry("活跃碰撞体", $"{_activeColliders:N0}", "当前层可交互零件"));
        if (recordColliderBreakdown)
            entries.Add(new MetricEntry("Box / Mesh", $"{_activeBoxColliders:N0} / {_activeMeshColliders:N0}", "碰撞体类型"));
        if (recordActiveRenderers)
            entries.Add(new MetricEntry("活跃渲染器", $"{_activeRenderers:N0}", "开启状态 Renderer"));
        if (recordActiveTriangles)
            entries.Add(new MetricEntry("活跃三角面", $"{_activeTriangles:N0}", "仅统计可读 Mesh"));
        if (recordActiveVertices)
            entries.Add(new MetricEntry("活跃顶点", $"{_activeVertices:N0}", "当前层几何规模"));
        if (recordPartCounts)
        {
            entries.Add(new MetricEntry("零件总数", $"{_totalParts:N0}", $"统计根：{_scopeName}"));
            entries.Add(new MetricEntry("当前层零件", $"{_currentLayerTotalParts:N0}", "层级规模"));
            entries.Add(new MetricEntry("剩余零件", $"{_remainingParts:N0}", "未拆解"));
            entries.Add(new MetricEntry("可交互零件", $"{_interactableParts:N0}", "当前阶段"));
        }
        if (recordLayerProgress)
        {
            entries.Add(new MetricEntry("层进度", $"{_currentLayerProgress * 100f:F1}%", "当前层完成率"));
            entries.Add(new MetricEntry("优先组数", $"{_priorityGroupCount:N0}", "有效优先组"));
            entries.Add(new MetricEntry("层数量", $"{_layerCount:N0}", "系统总层数"));
        }

        return entries;
    }

    // ==============================================================
        // 供主面板模块（TwinUiPerformanceModule）只读接入的最小 API（P4 / task-12）
        // 约束：不改采集口径、不改 CSV 字段/格式、不改默认字段值；只暴露既有内部采样值。
        // ==============================================================

        /// <summary>性能快照：字段直接来自本脚本既有采样（_fps/_frameMs/_engine*/_managedMB/...）。</summary>
        public struct PerfSnapshot
        {
            public bool valid;
            public float fps;
            public float frameMs;
            public float engineAllocatedMB;
            public float engineReservedMB;
            public float engineUnusedReservedMB;
            public float managedMB;
            public float processWorkingSetMB;
            public int activeColliders;
            public int activeBoxColliders;
            public int activeMeshColliders;
            public int activeOtherColliders;
            public int activeRenderers;
            public int activeVertices;
            public long activeTriangles;
            public int totalParts;
            public int remainingParts;
            public int interactableParts;
            public int layerCount;
            public float currentLayerProgress;
            public string currentMode;
            public string scopeName;
            public string layerSummary;
            public float sampledAt;
            public string csvPath;
        }

        /// <summary>读取最近一次采样的只读快照；Start 未完成时返回 false。</summary>
        public bool TryGetSnapshot(out PerfSnapshot snapshot)
        {
            snapshot = new PerfSnapshot
            {
                valid = _panelInitialized || _fileReady,
                fps = _fps,
                frameMs = _frameMs,
                engineAllocatedMB = _engineAllocatedMB,
                engineReservedMB = _engineReservedMB,
                engineUnusedReservedMB = _engineUnusedReservedMB,
                managedMB = _managedMB,
                processWorkingSetMB = _processWorkingSetMB,
                activeColliders = _activeColliders,
                activeBoxColliders = _activeBoxColliders,
                activeMeshColliders = _activeMeshColliders,
                activeOtherColliders = _activeOtherColliders,
                activeRenderers = _activeRenderers,
                activeVertices = _activeVertices,
                activeTriangles = _activeTriangles,
                totalParts = _totalParts,
                remainingParts = _remainingParts,
                interactableParts = _interactableParts,
                layerCount = _layerCount,
                currentLayerProgress = _currentLayerProgress,
                currentMode = _currentMode,
                scopeName = _scopeName,
                layerSummary = _currentLayerSummary,
                sampledAt = Time.unscaledTime,
                csvPath = _csvPath
            };
            return snapshot.valid;
        }

        /// <summary>旧 IMGUI 面板当前是否显示（模块接管用）。</summary>
        public bool IsOnScreenUIVisible()
        {
            return IsPanelVisible();
        }

        /// <summary>
        /// 模块接管显示时调用：显式设置旧 IMGUI 面板可见性（Update 里 showOnScreenUI 会被面板状态同步，
        /// 因此不能只改字段，必须走同一个 SetPanelVisibility 入口）。
        /// </summary>
        public void SetOnScreenUI(bool visible, string reason)
        {
            if (IsPanelVisible() == visible)
            {
                return;
            }

            SetPanelVisibility(visible);
            Debug.Log("[性能监测] showOnScreenUI=" + visible + " reason=" + (reason ?? "none")
                + " panel=" + (_performancePanel != null));
        }

        /// <summary>当前 CSV 文件路径（InitCSV 后可用）。</summary>
        public string CsvFilePath => _csvPath;

        /// <summary>按原格式立即补写一行 CSV 并 flush（导出按钮用，不重写格式）。</summary>
        public bool FlushCsvNow()
        {
            if (!_fileReady || _writer == null)
            {
                return false;
            }

            WriteRow();
            return true;
        }

        private void OnGUI()
    {
        if (_panelInitialized)
        {
            _performancePanel.Draw((contentRect) => {
                DrawAcademicPanelContent(contentRect);
            });
        }
        else if (!showOnScreenUI)
        {
            return;
        }
        else
        {
            DrawAcademicPanel();
        }
    }

    private void DrawAcademicPanel()
    {
        Rect rect = uiSettings.uiRect;
        rect.width = Mathf.Max(rect.width, 420f);

        List<MetricEntry> metrics = BuildMetricEntries();
        int columns = Mathf.Clamp(uiSettings.metricColumns, 1, 3);
        int rows = metrics.Count == 0 ? 0 : Mathf.CeilToInt(metrics.Count / (float)columns);
        float headerHeight = uiSettings.uiPadding * 2f + uiSettings.uiTitleFontSize + 18f + uiSettings.uiBodyFontSize * 3f + 14f;
        float metricHeight = rows > 0 ? rows * uiSettings.metricCardHeight + Mathf.Max(0, rows - 1) * uiSettings.uiMetricGap : 0f;
        float footerHeight = uiSettings.showSourceNote ? uiSettings.uiFootnoteFontSize + 8f : uiSettings.uiFootnoteFontSize;
        rect.height = Mathf.Max(rect.height, headerHeight + metricHeight + footerHeight + uiSettings.uiSectionGap * 2f);

        if (uiSettings.uiClampToScreen)
            rect = RuntimeUiTheme.ClampToScreen(rect, 10f);

        RuntimeUiTheme.DrawPanel(rect, uiSettings.uiBackgroundColor, uiSettings.uiBorderColor, 1f, 4f, uiSettings.uiAccentColor);

        float x = rect.x + uiSettings.uiPadding;
        float y = rect.y + uiSettings.uiPadding;
        float contentWidth = rect.width - uiSettings.uiPadding * 2f;

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiTitleFontSize, uiSettings.uiTitleColor, false, FontStyle.Bold);
        var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiBodyFontSize, uiSettings.uiTextColor, true, FontStyle.Normal);
        var rowValueStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiBodyFontSize + 1, uiSettings.uiValueColor, true, FontStyle.Bold);
        var mutedStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiFootnoteFontSize, uiSettings.uiMutedColor, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, uiSettings.uiBackgroundColor, false, FontStyle.Bold);

        GUI.Label(new Rect(x, y, contentWidth - 90f, uiSettings.uiTitleFontSize + 4f), uiSettings.uiTitle, titleStyle);
        RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - uiSettings.uiPadding - 82f, y + 1f, 82f, 20f), recordFPS ? "LIVE" : "STATIC", pillStyle, uiSettings.uiAccentColor, uiSettings.uiAccentColor);
        y += uiSettings.uiTitleFontSize + 6f;
        GUI.Label(new Rect(x, y, contentWidth, uiSettings.uiBodyFontSize + 4f), uiSettings.uiSubtitle, subtitleStyle);
        y += uiSettings.uiBodyFontSize + 10f;
        RuntimeUiTheme.DrawSolidRect(new Rect(x, y, contentWidth, 1f), uiSettings.uiBorderColor);
        y += uiSettings.uiSectionGap;

        float rowHeight = 20f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(x, y, contentWidth, rowHeight), "模式", _currentMode, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(x, y, contentWidth, rowHeight), "统计根", _scopeName, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(x, y, contentWidth, rowHeight), "当前阶段", _currentLayerPhase, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(x, y, contentWidth, rowHeight), "导出间隔", $"{logInterval:0.##} s", subtitleStyle, rowValueStyle, 84f);
        y += rowHeight + uiSettings.uiSectionGap;

        DrawMetricGrid(new Rect(x, y, contentWidth, rect.height - (y - rect.y) - footerHeight - uiSettings.uiPadding), metrics);

        if (uiSettings.showSourceNote || uiSettings.showCSVHint)
        {
            string note = BuildSourceNote();
            string hint = uiSettings.showCSVHint ? "CSV 已同步记录，可用于后续论文统计分析" : string.Empty;
            string footer = uiSettings.showSourceNote && uiSettings.showCSVHint ? $"{note} | {hint}" : (uiSettings.showSourceNote ? note : hint);
            GUI.Label(new Rect(x, rect.yMax - uiSettings.uiPadding - uiSettings.uiFootnoteFontSize - 2f, contentWidth, uiSettings.uiFootnoteFontSize + 4f), footer, mutedStyle);
        }
    }

    private void DrawAcademicPanelContent(Rect contentRect)
    {
        List<MetricEntry> metrics = BuildMetricEntries();
        int columns = Mathf.Clamp(uiSettings.metricColumns, 1, 3);

        float y = 0f;
        float contentWidth = contentRect.width;

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiTitleFontSize, uiSettings.uiTitleColor, false, FontStyle.Bold);
        var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiBodyFontSize, uiSettings.uiTextColor, true, FontStyle.Normal);
        var rowValueStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiBodyFontSize + 1, uiSettings.uiValueColor, true, FontStyle.Bold);
        var mutedStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiFootnoteFontSize, uiSettings.uiMutedColor, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, uiSettings.uiBackgroundColor, false, FontStyle.Bold);

        GUI.Label(new Rect(contentRect.x, y, contentWidth - 90f, uiSettings.uiTitleFontSize + 4f), uiSettings.uiSubtitle, subtitleStyle);
        y += uiSettings.uiBodyFontSize + 10f;
        RuntimeUiTheme.DrawSolidRect(new Rect(contentRect.x, y, contentWidth, 1f), uiSettings.uiBorderColor);
        y += uiSettings.uiSectionGap;

        float rowHeight = 20f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentWidth, rowHeight), "模式", _currentMode, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentWidth, rowHeight), "统计根", _scopeName, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentWidth, rowHeight), "当前阶段", _currentLayerPhase, subtitleStyle, rowValueStyle, 84f);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentWidth, rowHeight), "导出间隔", $"{logInterval:0.##} s", subtitleStyle, rowValueStyle, 84f);
        y += rowHeight + uiSettings.uiSectionGap;

        // 绘制指标网格
        float metricGridHeight = contentRect.height - y - 30f; // 预留底部空间
        DrawMetricGrid(new Rect(contentRect.x, y, contentWidth, metricGridHeight), metrics);

        if (uiSettings.showSourceNote || uiSettings.showCSVHint)
        {
            string note = BuildSourceNote();
            string hint = uiSettings.showCSVHint ? "CSV 已同步记录，可用于后续论文统计分析" : string.Empty;
            string footer = uiSettings.showSourceNote && uiSettings.showCSVHint ? $"{note} | {hint}" : (uiSettings.showSourceNote ? note : hint);
            GUI.Label(new Rect(contentRect.x, contentRect.y + contentRect.height - 20f, contentWidth, uiSettings.uiFootnoteFontSize + 4f), footer, mutedStyle);
        }
    }

    private string BuildSourceNote()
    {
        string scope = string.IsNullOrEmpty(_scopeName) ? "未设置统计根节点" : _scopeName;
        string triangleNote = recordActiveTriangles ? "三角面仅统计可读 Mesh" : "三角面未启用";
        return $"数据源: {scope} | {triangleNote}";
    }

    private void DrawMetricGrid(Rect rect, List<MetricEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;

        int columns = Mathf.Clamp(uiSettings.metricColumns, 1, 3);
        float cardWidth = (rect.width - (columns - 1) * uiSettings.uiMetricGap) / columns;

        for (int i = 0; i < entries.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;
            Rect cardRect = new Rect(
                rect.x + col * (cardWidth + uiSettings.uiMetricGap),
                rect.y + row * (uiSettings.metricCardHeight + uiSettings.uiMetricGap),
                cardWidth,
                uiSettings.metricCardHeight);

            DrawMetricCard(cardRect, entries[i]);
        }
    }

    private void DrawMetricCard(Rect rect, MetricEntry entry)
    {
        Color fill = new Color(uiSettings.uiBackgroundColor.r + 0.02f, uiSettings.uiBackgroundColor.g + 0.02f, uiSettings.uiBackgroundColor.b + 0.03f, 0.95f);
        RuntimeUiTheme.DrawPanel(rect, fill, uiSettings.uiBorderColor, 1f, 2f, uiSettings.uiAccentMutedColor);

        var labelStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiBodyFontSize, uiSettings.uiMutedColor, false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiValueFontSize, uiSettings.uiValueColor, false, FontStyle.Bold);
        var detailStyle = RuntimeUiTheme.CreateLabelStyle(uiSettings.uiFootnoteFontSize, uiSettings.uiTextColor, true, FontStyle.Normal);

        GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 16f), entry.Label, labelStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 22f, rect.width - 20f, uiSettings.uiValueFontSize + 6f), entry.Value, valueStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + rect.height - 18f, rect.width - 20f, 14f), entry.Detail, detailStyle);
    }

    private void InitCSV()
    {
        try
        {
            string dir = BuildCsvDirectory();

            Directory.CreateDirectory(dir);

            string path = BuildUniqueCsvPath(dir);
            _csvPath = path;

            _writer = new StreamWriter(path, false, Encoding.UTF8);
            _writer.WriteLine(
                "时间戳,统计根,当前模式,层阶段,当前层索引,层总数,零件总数,当前层零件,剩余零件,可交互零件,层进度,FPS,帧时长(ms),引擎已分配内存(MB),引擎已保留内存(MB),引擎未使用保留内存(MB),管理堆(MB),进程工作集(MB),活跃碰撞体,Box碰撞体,Mesh碰撞体,其他碰撞体,活跃渲染器,活跃三角面,活跃顶点,层摘要");
            _writer.Flush();
            _fileReady = true;
            Debug.Log($"[性能监测] CSV 已创建：{path}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[性能监测] CSV 创建失败：{ex.Message}");
        }
        finally
        {
            CacheCsvOutputSettings();
        }
    }

    private void WriteRow()
    {
        if (_writer == null) return;

        try
        {
            StringBuilder sb = new StringBuilder(1024);
            AppendCsvField(sb, System.DateTime.Now.ToString("HH:mm:ss.fff"));
            AppendCsvField(sb, _scopeName);
            AppendCsvField(sb, _currentMode);
            AppendCsvField(sb, _currentLayerPhase);
            AppendCsvField(sb, DisassemblySystem.Instance != null ? DisassemblySystem.Instance.currentLayerIndex.ToString() : "-1");
            AppendCsvField(sb, _layerCount.ToString());
            AppendCsvField(sb, _totalParts.ToString());
            AppendCsvField(sb, _currentLayerTotalParts.ToString());
            AppendCsvField(sb, _remainingParts.ToString());
            AppendCsvField(sb, _interactableParts.ToString());
            AppendCsvField(sb, _currentLayerProgress.ToString("F3"));
            AppendCsvField(sb, _fps.ToString("F1"));
            AppendCsvField(sb, _frameMs.ToString("F2"));
            AppendCsvField(sb, _engineAllocatedMB.ToString("F2"));
            AppendCsvField(sb, _engineReservedMB.ToString("F2"));
            AppendCsvField(sb, _engineUnusedReservedMB.ToString("F2"));
            AppendCsvField(sb, _managedMB.ToString("F2"));
            AppendCsvField(sb, _processWorkingSetMB.ToString("F2"));
            AppendCsvField(sb, _activeColliders.ToString());
            AppendCsvField(sb, _activeBoxColliders.ToString());
            AppendCsvField(sb, _activeMeshColliders.ToString());
            AppendCsvField(sb, _activeOtherColliders.ToString());
            AppendCsvField(sb, _activeRenderers.ToString());
            AppendCsvField(sb, _activeTriangles.ToString());
            AppendCsvField(sb, _activeVertices.ToString());
            AppendCsvField(sb, _currentLayerSummary);

            _writer.WriteLine(sb.ToString());
            _writer.Flush();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[性能监测] 写入失败：{ex.Message}");
        }
    }

    private void AppendCsvField(StringBuilder sb, string value)
    {
        if (sb.Length > 0) sb.Append(',');
        if (value == null) value = string.Empty;
        sb.Append('"');
        sb.Append(value.Replace("\"", "\"\""));
        sb.Append('"');
    }

    private void ApplyPanelSettings()
    {
        if (_performancePanel == null) return;

        _performancePanel.title = uiSettings.uiTitle;
        _performancePanel.position = uiSettings.uiRect;
        _performancePanel.scale = uiSettings.uiScale;
    }

    private void SetPanelVisibility(bool visible)
    {
        showOnScreenUI = visible;
        if (_performancePanel != null)
            _performancePanel.isVisible = visible;
    }

    private bool IsPanelVisible()
    {
        return _performancePanel != null ? _performancePanel.isVisible : showOnScreenUI;
    }

    private void RefreshCsvOutputIfNeeded()
    {
        if (!Application.isPlaying) return;
        if (!HasCsvOutputSettingsChanged()) return;

        CloseWriter();
        InitCSV();
        _logTimer = Mathf.Max(0.05f, logInterval);
    }

    private bool HasCsvOutputSettingsChanged()
    {
        return !string.Equals(_lastCustomLogPath, customLogPath ?? string.Empty) ||
               !string.Equals(_lastCsvFileName, csvFileName ?? string.Empty) ||
               _lastAppendTimestampFolder != appendTimestampFolder ||
               _lastAppendTimestampToFileName != appendTimestampToFileName;
    }

    private void CacheCsvOutputSettings()
    {
        _lastCustomLogPath = customLogPath ?? string.Empty;
        _lastCsvFileName = csvFileName ?? string.Empty;
        _lastAppendTimestampFolder = appendTimestampFolder;
        _lastAppendTimestampToFileName = appendTimestampToFileName;
    }

    private string BuildCsvDirectory()
    {
        string dir = string.IsNullOrEmpty(customLogPath)
            ? Path.Combine(Application.persistentDataPath, "PerformanceLogs")
            : customLogPath;

        if (appendTimestampFolder)
            dir = Path.Combine(dir, System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        return dir;
    }

    private string BuildCsvFileName()
    {
        string rawFileName = string.IsNullOrWhiteSpace(csvFileName) ? "PerfLog" : csvFileName.Trim();
        string extension = Path.GetExtension(rawFileName);
        string baseName = Path.GetFileNameWithoutExtension(rawFileName);
        string safeBaseName = SanitizeFileName(baseName);
        string timestampSuffix = appendTimestampToFileName
            ? $"_{System.DateTime.Now:yyyyMMdd_HHmmss}"
            : string.Empty;

        if (string.IsNullOrEmpty(extension))
            extension = ".csv";

        return $"{safeBaseName}{timestampSuffix}{extension}";
    }

    private string BuildUniqueCsvPath(string directory)
    {
        string fileName = BuildCsvFileName();
        string extension = Path.GetExtension(fileName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
            return path;

        int suffix = 1;
        while (true)
        {
            string candidatePath = Path.Combine(directory, $"{baseName}_{suffix}{extension}");
            if (!File.Exists(candidatePath))
                return candidatePath;
            suffix++;
        }
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "PerfLog";

        string safeName = fileName;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(safeName) ? "PerfLog" : safeName;
    }

    private void OnDestroy()
    {
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        CloseWriter();
    }

    private void CloseWriter()
    {
        if (_writer == null) return;

        try
        {
            _writer.Flush();
            _writer.Close();
        }
        finally
        {
            _writer = null;
            _fileReady = false;
        }
    }
}
