#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DisassemblyUiScenePreview
{
    private const string PrefKey = "DisassemblyUiScenePreview.Enabled";
    private static bool _enabled = EditorPrefs.GetBool(PrefKey, true);

    static DisassemblyUiScenePreview()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    [MenuItem("Tools/Disassembly UI/Toggle Scene Preview")]
    private static void TogglePreview()
    {
        _enabled = !_enabled;
        EditorPrefs.SetBool(PrefKey, _enabled);
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Disassembly UI/Toggle Scene Preview", true)]
    private static bool TogglePreviewValidate()
    {
        Menu.SetChecked("Tools/Disassembly UI/Toggle Scene Preview", _enabled);
        return true;
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (!_enabled || sceneView == null) return;

        Handles.BeginGUI();
        try
        {
            DrawSelectionPreview(sceneView);
            DrawSystemPreview(sceneView);
            DrawHint(sceneView);
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    private static void DrawSelectionPreview(SceneView sceneView)
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) return;

        SmartRaycaster raycaster = selected.GetComponentInParent<SmartRaycaster>();
        if (raycaster != null)
        {
            DrawRaycasterPreview(sceneView, selected, raycaster);
            return;
        }

        if (!TryGetWorldBounds(selected, out Bounds bounds))
            bounds = new Bounds(selected.transform.position, Vector3.one * 0.12f);

        Camera cam = sceneView.camera;
        if (cam == null) return;

        Vector3 worldAnchor = bounds.center + Vector3.up * Mathf.Max(bounds.extents.y, 0.03f);
        Vector3 screen = cam.WorldToScreenPoint(worldAnchor);
        if (screen.z <= 0f) return;

        Vector2 anchor = new Vector2(screen.x, sceneView.position.height - screen.y);
        Rect rect = new Rect(anchor.x + 18f, anchor.y - 82f, 320f, 76f);
        rect = RuntimeUiTheme.ClampToArea(rect, sceneView.position.width, sceneView.position.height, 10f);

        Vector2 lineStart = anchor;
        Vector2 lineEnd = new Vector2(rect.x, rect.center.y);
        RuntimeUiTheme.DrawLine(lineStart, lineEnd, new Color(0.18f, 0.82f, 1f, 1f), 2.5f);
        RuntimeUiTheme.DrawPanel(rect, new Color(0.05f, 0.06f, 0.08f, 0.96f), new Color(1f, 1f, 1f, 0.12f), 1f, 4f, new Color(0.18f, 0.82f, 1f, 1f));

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(18, Color.white, false, FontStyle.Bold);
        var subStyle = RuntimeUiTheme.CreateLabelStyle(12, new Color(0.86f, 0.90f, 0.95f, 1f), true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 100f, 22f), selected.name, titleStyle);
        RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - 76f, rect.y + 8f, 64f, 20f), "预览", pillStyle, new Color(0.18f, 0.82f, 1f, 0.95f), new Color(0.28f, 0.95f, 1f, 1f));

        string layerName = LayerMask.LayerToName(selected.layer);
        string line = string.IsNullOrEmpty(layerName) ? "Layer: Default" : $"Layer: {layerName}";
        GUI.Label(new Rect(rect.x + 12f, rect.y + 33f, rect.width - 24f, 18f), line, subStyle);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 49f, rect.width - 24f, 18f), "Scene 视图预览模式", subStyle);
    }

    private static void DrawRaycasterPreview(SceneView sceneView, GameObject selected, SmartRaycaster raycaster)
    {
        if (!TryGetWorldBounds(selected, out Bounds bounds))
            bounds = new Bounds(selected.transform.position, Vector3.one * 0.12f);

        Camera cam = sceneView.camera;
        if (cam == null) return;

        Vector3 worldAnchor = bounds.center + Vector3.up * Mathf.Max(bounds.extents.y, 0.03f);
        Vector3 screen = cam.WorldToScreenPoint(worldAnchor);
        if (screen.z <= 0f) return;

        Vector2 anchor = new Vector2(screen.x, sceneView.position.height - screen.y);

        if (raycaster.hoverInfoSettings.showFloatingHoverTag)
        {
            float subtitleHeight = raycaster.hoverInfoSettings.hoverInfoShowDetailed ? 34f : 20f;
            float height = Mathf.Max(raycaster.hoverInfoSettings.hoverTagMinHeight, raycaster.hoverInfoSettings.hoverTagTitleFontSize + subtitleHeight + 18f);

            Rect rect = new Rect(
                anchor.x + raycaster.hoverInfoSettings.hoverTagScreenOffset.x,
                anchor.y + raycaster.hoverInfoSettings.hoverTagScreenOffset.y - height,
                raycaster.hoverInfoSettings.hoverTagWidth,
                height);
            rect = RuntimeUiTheme.ClampToArea(rect, sceneView.position.width, sceneView.position.height, 10f);

            Vector2 lineStart = anchor;
            Vector2 lineEnd = new Vector2(rect.x, rect.center.y);
            RuntimeUiTheme.DrawLine(lineStart, lineEnd, raycaster.rayShadowColor, raycaster.rayScreenThickness + 2f);
            RuntimeUiTheme.DrawLine(lineStart, lineEnd, raycaster.hoverInfoSettings.hoverTagAccentColor, raycaster.rayScreenThickness);
            RuntimeUiTheme.DrawPanel(rect, raycaster.hoverInfoSettings.hoverTagBackgroundColor, raycaster.hoverInfoSettings.hoverTagBorderColor, 1f, 3f, raycaster.hoverInfoSettings.hoverTagAccentColor);

            var titleStyle = RuntimeUiTheme.CreateLabelStyle(raycaster.hoverInfoSettings.hoverTagTitleFontSize, raycaster.hoverInfoSettings.hoverTagTitleColor, false, FontStyle.Bold);
            var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(raycaster.hoverInfoSettings.hoverTagSubtitleFontSize, raycaster.hoverInfoSettings.hoverTagSubtitleColor, true, FontStyle.Normal);
            var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, raycaster.hoverInfoSettings.hoverTagTitleFontSize + 4f);
            Rect subtitleRect = new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 34f);

            GUI.Label(titleRect, selected.name, titleStyle);
            RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - 84f, rect.y + 7f, 72f, 20f), "Preview", pillStyle, new Color(0.18f, 0.82f, 1f, 0.95f), new Color(0.28f, 0.95f, 1f, 1f));

            string mode = $"Mode: {raycaster.controlMode}";
            string rayLine = raycaster.useScreenSpaceRayVisual ? "Ray: Screen-space" : "Ray: LineRenderer";
            string toggleLine = $"Tag: {(raycaster.hoverInfoSettings.showFloatingHoverTag ? "On" : "Off")} | Panel: {(raycaster.hoverInfoSettings.showHoverInfoPanel && raycaster.hoverInfoSettings.hoverInfoVisible ? "On" : "Off")}";

            GUI.Label(subtitleRect, mode, subtitleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 18f), rayLine, subtitleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 60f, rect.width - 24f, 18f), toggleLine, subtitleStyle);
        }

        if (raycaster.hoverInfoSettings.showHoverInfoPanel && raycaster.hoverInfoSettings.hoverInfoVisible)
        {
            Rect rect = raycaster.hoverInfoSettings.hoverInfoRect;
            rect.width = Mathf.Max(300f, rect.width);
            rect.height = raycaster.hoverInfoSettings.hoverInfoShowDetailed ? Mathf.Max(194f, rect.height) : Mathf.Max(156f, rect.height);
            rect = RuntimeUiTheme.ClampToArea(rect, sceneView.position.width, sceneView.position.height, 10f);

            Color cardBg = new Color(0.05f, 0.06f, 0.08f, 0.96f);
            Color cardBorder = new Color(1f, 1f, 1f, 0.12f);
            Color cardAccent = raycaster.hoverInfoSettings.hoverTagAccentColor;
            RuntimeUiTheme.DrawPanel(rect, cardBg, cardBorder, 1f, 4f, cardAccent);

            int titleSize = Mathf.Max(16, raycaster.hoverInfoSettings.hoverInfoFontSize);
            var titleStyle = RuntimeUiTheme.CreateLabelStyle(titleSize, Color.white, false, FontStyle.Bold);
            var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(raycaster.hoverInfoSettings.hoverTagSubtitleFontSize, raycaster.hoverInfoSettings.hoverTagSubtitleColor, true, FontStyle.Normal);
            var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
            var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, raycaster.hoverInfoSettings.hoverInfoColor, true, FontStyle.Bold);
            var pillStyle = RuntimeUiTheme.CreateLabelStyle(12, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), "零件信息预览", titleStyle);
            RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - 90f, rect.y + 8f, 78f, 22f), "Scene", pillStyle, new Color(0.18f, 0.82f, 1f, 0.95f), new Color(0.28f, 0.95f, 1f, 1f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 18f), selected.name, subtitleStyle);
            RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 52f, rect.width - 24f, 1f), cardBorder);

            float y = rect.y + 60f;
            float rowHeight = 20f;
            RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "模式", raycaster.controlMode.ToString(), labelStyle, valueStyle);
            y += rowHeight;
            RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "屏幕射线", raycaster.useScreenSpaceRayVisual ? "开启" : "关闭", labelStyle, valueStyle);
            y += rowHeight;
            RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "悬浮标签", raycaster.hoverInfoSettings.showFloatingHoverTag ? "开启" : "关闭", labelStyle, valueStyle);
            y += rowHeight;

            if (raycaster.hoverInfoSettings.hoverInfoShowDetailed)
            {
                RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "详细信息", "开启", labelStyle, valueStyle);
                y += rowHeight;
                RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "点击反馈", raycaster.enableClickColorFeedback ? "开启" : "关闭", labelStyle, valueStyle);
                y += rowHeight;
            }
            else
            {
                RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "详细信息", "关闭", labelStyle, valueStyle);
                y += rowHeight;
            }

            GUI.Label(new Rect(rect.x + 12f, rect.y + rect.height - 24f, rect.width - 24f, 18f), $"Tag宽度 {raycaster.hoverInfoSettings.hoverTagWidth:0.##} | Panel 字体 {raycaster.hoverInfoSettings.hoverInfoFontSize}", subtitleStyle);
            RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + rect.height - 5f, 60f, 2f), cardAccent);
        }
    }

    private static void DrawSystemPreview(SceneView sceneView)
    {
        DisassemblySystem system = Object.FindObjectOfType<DisassemblySystem>();
        Rect rect = new Rect(12f, 12f, 380f, 164f);
        rect = RuntimeUiTheme.ClampToArea(rect, sceneView.position.width, sceneView.position.height, 10f);

        Color bg = new Color(0.05f, 0.06f, 0.07f, 0.94f);
        Color border = new Color(1f, 1f, 1f, 0.12f);
        Color accent = new Color(0.90f, 0.65f, 0.18f, 1f);
        RuntimeUiTheme.DrawPanel(rect, bg, border, 1f, 4f, accent);

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(17, Color.white, false, FontStyle.Bold);
        var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, Color.white, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), "拆解系统预览", titleStyle);
        RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - 84f, rect.y + 8f, 72f, 20f), "Scene", pillStyle, accent, new Color(0.28f, 0.95f, 1f, 1f));
        RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, 1f), border);

        string mode = system != null ? system.detectionMode.ToString() : "No System";
        string layerCount = system != null ? system.layers.Count.ToString() : "0";
        string helper = system != null ? "Editor preview only" : "Drag DisassemblySystem into scene root";

        float y = rect.y + 42f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, 22f), "模式", mode, labelStyle, valueStyle);
        y += 22f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, 22f), "层数量", layerCount, labelStyle, valueStyle);
        y += 22f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, 22f), "说明", helper, labelStyle, valueStyle);
    }

    private static void DrawHint(SceneView sceneView)
    {
        Rect rect = new Rect(sceneView.position.width - 280f, sceneView.position.height - 44f, 260f, 24f);
        rect = RuntimeUiTheme.ClampToArea(rect, sceneView.position.width, sceneView.position.height, 10f);

        var style = RuntimeUiTheme.CreateLabelStyle(11, new Color(0.82f, 0.86f, 0.90f, 1f), false, FontStyle.Normal);
        RuntimeUiTheme.DrawPanel(rect, new Color(0.02f, 0.03f, 0.04f, 0.76f), new Color(1f, 1f, 1f, 0.08f), 1f, 2f, new Color(0.18f, 0.82f, 1f, 1f));
        GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, rect.height - 8f), "Tools/Disassembly UI/Toggle Scene Preview", style);
    }

    private static bool TryGetWorldBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        if (go == null) return false;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        foreach (Renderer rend in renderers)
        {
            if (rend == null) continue;
            if (!hasBounds)
            {
                bounds = rend.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rend.bounds);
            }
        }

        if (hasBounds) return true;

        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
        foreach (Collider col in colliders)
        {
            if (col == null) continue;
            if (!hasBounds)
            {
                bounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return hasBounds;
    }
}
#endif