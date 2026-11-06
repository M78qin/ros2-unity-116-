using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 中央管控脚本 - 挂载于场景最高父物体（如 Dis_1）
/// 负责层级有序解锁、全局碰撞模式切换、零件交互权限校验
/// </summary>
public class DisassemblySystem : MonoBehaviour
{
    public static DisassemblySystem Instance { get; private set; }

    public enum CollisionMode
    {
        Coarse, // 仅粗碰撞（Box）
        Fine,   // 仅精碰撞（Mesh）
        Hybrid  // 混合：无接触用Box，有接触切Mesh
    }

    [Header("全局设置")]
    [Tooltip("碰撞检测模式：Coarse=仅Box | Fine=仅Mesh | Hybrid=动态切换")]
    public CollisionMode detectionMode = CollisionMode.Hybrid;
    [Tooltip("若 layers 为空或开启自动收集，将按 Hierarchy 顺序自动收集子层")]
    public bool autoCollectLayersByHierarchy = true;
    [Tooltip("当某层没有任何可交互物体时，自动跳过到下一层")]
    public bool skipEmptyOrStalledLayers = true;
    [Tooltip("打印运行阶段日志（调试用）")]
    public bool verboseLog = true;

    [System.Serializable]
    public class DebugSettings
    {
        [Tooltip("调试信息面板位置与大小")]
        public Rect debugRect = new Rect(10, 10, 380, 280);
        [Tooltip("调试信息字体大小")]
        public int debugFontSize = 14;
        [Tooltip("调试信息字体颜色")]
        public Color debugColor = Color.white;
        [Tooltip("调试信息背景颜色")]
        public Color debugBgColor = new Color(0.08f, 0.08f, 0.10f, 0.94f);
        [Tooltip("调试信息边框颜色")]
        public Color debugBorderColor = new Color(1f, 1f, 1f, 0.14f);
        [Tooltip("调试信息强调色")]
        public Color debugAccentColor = new Color(0.15f, 0.85f, 1f, 1f);
        [Tooltip("切换调试信息的按键")]
        public KeyCode debugToggleKey = KeyCode.F;
        [Tooltip("调试信息默认是否可见")]
        public bool debugVisible = false;
    }

    [Header("Debug（调试）")]
    [Tooltip("是否在屏幕上显示调试信息")]
    public bool showDebugOverlay = true;
    public DebugSettings debugSettings = new DebugSettings();

    [Header("层配置列表（按拆解顺序拖入）")]
    public List<LayerGroupConfig> layers = new List<LayerGroupConfig>();

    [Header("调试信息")]
    [SerializeField, Tooltip("运行时显示当前激活层索引（只读）")]
    private int _currentLayerIndex = -1;
    public int currentLayerIndex => _currentLayerIndex;
    public bool isSystemCompleted { get; private set; } = false;

    // ── 内部状态 ──────────────────────────────────────────
    private bool _initialized = false;

    // 可折叠面板
    private CollapsiblePanel _debugPanel;
    private bool _panelInitialized = false;

    // ── Unity 生命周期 ────────────────────────────────────

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            return;
        }

        if (Instance != this)
            Destroy(gameObject);
    }

    private void Start()
    {
        InitializeRuntime();

        // 初始化可折叠面板
        _debugPanel = new CollapsiblePanel("调试信息", debugSettings.debugRect);
        _debugPanel.isVisible = debugSettings.debugVisible;
        _panelInitialized = true;
    }

    private void Update()
    {
        if (showDebugOverlay && Input.GetKeyDown(debugSettings.debugToggleKey))
        {
            if (_panelInitialized)
                _debugPanel.ToggleVisibility();
            else
                debugSettings.debugVisible = !debugSettings.debugVisible;
        }
    }

    [ContextMenu("Rebuild And Refresh")]
    public void InitializeRuntime()
    {
        BuildLayerListIfNeeded();
        InitializeAllLayers();
        _initialized = true;
        RefreshSystemState();
    }

    // ── 核心：刷新整个系统状态 ────────────────────────────

    /// <summary>
    /// 刷新层级激活状态。每当零件完成拆解、或需要重新评估时调用。
    /// </summary>
    public void RefreshSystemState()
    {
        if (!_initialized) return;

        for (int i = 0; i < layers.Count; i++)
            if (layers[i] != null)
                layers[i].SetLayerActiveState(false, detectionMode);

        _currentLayerIndex = FindNextActiveLayerIndex();
        if (_currentLayerIndex < 0)
        {
            isSystemCompleted = true;
            if (verboseLog) Debug.Log("[系统] 所有层级已拆解完成");
            return;
        }

        isSystemCompleted = false;
        LayerGroupConfig activeLayer = layers[_currentLayerIndex];
        if (activeLayer != null)
            activeLayer.SetLayerActiveState(true, detectionMode);

        if (verboseLog)
        {
            string layerName = activeLayer != null ? activeLayer.name : "NULL";
            Debug.Log($"[系统刷新] 当前激活层：{layerName}（索引 {_currentLayerIndex}）| 模式：{detectionMode}");
            if (activeLayer != null)
                Debug.Log($"[系统状态] {activeLayer.GetDebugSummary()}");
        }
    }

    // ── 公共接口 ──────────────────────────────────────────

    /// <summary>
    /// 校验零件是否处于当前可交互状态（由 SmartRaycaster 调用）
    /// </summary>
    public bool CheckInteractability(DisassemblyPart part)
    {
        if (part == null || !_initialized || _currentLayerIndex < 0 || _currentLayerIndex >= layers.Count)
            return false;

        var currentLayer = layers[_currentLayerIndex];
        if (currentLayer == null) return false;

        // 必须是当前层的子物体
        if (!currentLayer.ContainsPart(part)) return false;

        return currentLayer.IsPartInteractable(part);
    }

    public List<DisassemblyPart> GetCurrentInteractableParts()
    {
        if (!_initialized || _currentLayerIndex < 0 || _currentLayerIndex >= layers.Count)
            return new List<DisassemblyPart>();

        LayerGroupConfig layer = layers[_currentLayerIndex];
        return layer != null ? layer.GetCurrentInteractableParts() : new List<DisassemblyPart>();
    }

    /// <summary>
    /// Layer 内部状态变化回调（例如当前组完成、整层完成）
    /// </summary>
    public void NotifyLayerStateMaybeChanged(LayerGroupConfig layer)
    {
        if (!_initialized) return;
        if (layer == null) return;
        RefreshSystemState();
    }

    /// <summary>
    /// 在 Inspector 中切换碰撞模式后立即生效（Editor 调试用）
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && _initialized)
            RefreshSystemState();
    }

    // ── 内部工具 ──────────────────────────────────────────

    private void BuildLayerListIfNeeded()
    {
        layers ??= new List<LayerGroupConfig>();

        bool shouldAutoCollect = autoCollectLayersByHierarchy || layers.Count == 0;
        if (shouldAutoCollect)
        {
            layers = GetComponentsInChildren<LayerGroupConfig>(true)
                .OrderBy(layer => GetHierarchyOrder(layer.transform))
                .ToList();
            return;
        }

        layers = layers.Where(layer => layer != null).ToList();
    }

    private void InitializeAllLayers()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] == null) continue;
            layers[i].InitializeForRuntime(this, i);
        }
    }

    private int FindNextActiveLayerIndex()
    {
        int startIndex = _currentLayerIndex < 0 ? 0 : _currentLayerIndex;
        for (int i = startIndex; i < layers.Count; i++)
        {
            LayerGroupConfig layer = layers[i];
            if (layer == null) continue;

            if (layer.isLayerCompleted)
                continue;

            if (layer.TryResolveEmptyStage())
            {
                if (layer.isLayerCompleted)
                    continue;
            }

            if (layer.GetCurrentInteractableCount() > 0)
                return i;

            if (skipEmptyOrStalledLayers)
            {
                if (verboseLog)
                    Debug.Log($"[系统跳过] {layer.name} 没有可交互物体，自动跳到下一层");
                layer.ForceCompleteLayer("no interactable parts");
                continue;
            }

            return i;
        }
        return -1;
    }

    private int GetHierarchyOrder(Transform t)
    {
        int order = t.GetSiblingIndex();
        Transform parent = t.parent;
        int depth = 0;
        while (parent != null && parent != transform)
        {
            order += parent.GetSiblingIndex() * (int)Mathf.Pow(100, ++depth);
            parent = parent.parent;
        }
        return order;
    }

    private void OnGUI()
    {
        if (!showDebugOverlay) return;

        if (_panelInitialized)
        {
            _debugPanel.Draw((contentRect) => {
                DrawDebugPanelContent(contentRect);
            });
        }
        else if (debugSettings.debugVisible)
        {
            DrawDebugPanel();
        }
    }

    private void DrawDebugPanel()
    {
        string modeText = detectionMode switch
        {
            CollisionMode.Coarse => "Coarse",
            CollisionMode.Fine => "Fine",
            CollisionMode.Hybrid => "Hybrid",
            _ => detectionMode.ToString()
        };

        string phaseText = "No active layer";
        string summaryText = "No summary";

        if (_initialized && _currentLayerIndex >= 0 && _currentLayerIndex < layers.Count)
        {
            LayerGroupConfig layer = layers[_currentLayerIndex];
            if (layer != null)
            {
                phaseText = layer.GetCurrentPhaseDescription();
                summaryText = layer.GetDebugSummary();
            }
        }

        Rect rect = debugSettings.debugRect;
        rect.width = Mathf.Max(380f, rect.width);
        rect.height = Mathf.Max(180f, rect.height);
        rect = RuntimeUiTheme.ClampToScreen(rect, 10f);

        RuntimeUiTheme.DrawPanel(rect, debugSettings.debugBgColor, debugSettings.debugBorderColor, 1f, 4f, debugSettings.debugAccentColor);

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(17, Color.white, false, FontStyle.Bold);
        var bodyStyle = RuntimeUiTheme.CreateLabelStyle(debugSettings.debugFontSize, debugSettings.debugColor, true, FontStyle.Normal);
        var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, Color.white, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(12, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), "拆解系统", titleStyle);
        Rect pillRect = new Rect(rect.x + rect.width - 94f, rect.y + 8f, 82f, 22f);
        RuntimeUiTheme.DrawPill(pillRect, isSystemCompleted ? "Completed" : "Active", pillStyle, debugSettings.debugAccentColor, new Color(0.28f, 0.95f, 1f, 1f));
        RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, 1f), debugSettings.debugBorderColor);

        float y = rect.y + 42f;
        float rowHeight = 22f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "模式", modeText, labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "当前层", _currentLayerIndex < 0 ? "无" : $"{_currentLayerIndex}", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "阶段", phaseText, labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "状态", isSystemCompleted ? "系统已完成" : "运行中", labelStyle, valueStyle);
        y += rowHeight;

        GUI.Label(new Rect(rect.x + 12f, y + 2f, rect.width - 24f, 18f), summaryText, bodyStyle);
    }

    private void DrawDebugPanelContent(Rect contentRect)
    {
        string modeText = detectionMode switch
        {
            CollisionMode.Coarse => "Coarse",
            CollisionMode.Fine => "Fine",
            CollisionMode.Hybrid => "Hybrid",
            _ => detectionMode.ToString()
        };

        string phaseText = "No active layer";
        string summaryText = "No summary";

        if (_initialized && _currentLayerIndex >= 0 && _currentLayerIndex < layers.Count)
        {
            LayerGroupConfig layer = layers[_currentLayerIndex];
            if (layer != null)
            {
                phaseText = layer.GetCurrentPhaseDescription();
                summaryText = layer.GetDebugSummary();
            }
        }

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(17, Color.white, false, FontStyle.Bold);
        var bodyStyle = RuntimeUiTheme.CreateLabelStyle(debugSettings.debugFontSize, debugSettings.debugColor, true, FontStyle.Normal);
        var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, Color.white, true, FontStyle.Normal);

        GUI.Label(new Rect(contentRect.x, 0f, contentRect.width, 24f), "拆解系统", titleStyle);

        float y = 28f;
        float rowHeight = 22f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "模式", modeText, labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "当前层", _currentLayerIndex < 0 ? "无" : $"{_currentLayerIndex}", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "阶段", phaseText, labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "状态", isSystemCompleted ? "系统已完成" : "运行中", labelStyle, valueStyle);
        y += rowHeight;

        GUI.Label(new Rect(contentRect.x, y + 2f, contentRect.width, 18f), summaryText, bodyStyle);
    }
}