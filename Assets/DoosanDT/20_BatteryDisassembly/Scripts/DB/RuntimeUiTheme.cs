using UnityEngine;

/// <summary>
/// Small helper for drawing consistent runtime IMGUI panels.
/// </summary>
public static class RuntimeUiTheme
{
    private static Texture2D _whiteTexture;

    private static Texture2D WhiteTexture
    {
        get
        {
            if (_whiteTexture == null)
                _whiteTexture = Texture2D.whiteTexture;
            return _whiteTexture;
        }
    }

    public static GUIStyle CreateLabelStyle(int fontSize, Color textColor, bool wordWrap = true, FontStyle fontStyle = FontStyle.Normal)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            wordWrap = wordWrap,
            richText = true,
            normal = { textColor = textColor }
        };
    }

    public static void DrawPanel(Rect rect, Color background, Color border, float borderThickness = 1f, float accentHeight = 3f, Color? accent = null)
    {
        DrawSolidRect(rect, background);
        DrawSolidRect(new Rect(rect.x, rect.y, rect.width, borderThickness), border);
        DrawSolidRect(new Rect(rect.x, rect.yMax - borderThickness, rect.width, borderThickness), border);
        DrawSolidRect(new Rect(rect.x, rect.y, borderThickness, rect.height), border);
        DrawSolidRect(new Rect(rect.xMax - borderThickness, rect.y, borderThickness, rect.height), border);

        if (accent.HasValue)
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, accentHeight), accent.Value);
    }

    public static void DrawPill(Rect rect, string text, GUIStyle textStyle, Color fill, Color border, float borderThickness = 1f)
    {
        DrawSolidRect(rect, fill);
        DrawSolidRect(new Rect(rect.x, rect.y, rect.width, borderThickness), border);
        DrawSolidRect(new Rect(rect.x, rect.yMax - borderThickness, rect.width, borderThickness), border);
        DrawSolidRect(new Rect(rect.x, rect.y, borderThickness, rect.height), border);
        DrawSolidRect(new Rect(rect.xMax - borderThickness, rect.y, borderThickness, rect.height), border);

        GUIStyle style = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Label(rect, text, style);
    }

    public static void DrawLabeledRow(Rect rect, string label, string value, GUIStyle labelStyle, GUIStyle valueStyle, float labelWidth = 110f)
    {
        Rect left = new Rect(rect.x, rect.y, labelWidth, rect.height);
        Rect right = new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height);
        GUIStyle leftStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleLeft };
        GUIStyle rightStyle = new GUIStyle(valueStyle) { alignment = TextAnchor.MiddleLeft };
        GUI.Label(left, label, leftStyle);
        GUI.Label(right, value, rightStyle);
    }

    public static void DrawSolidRect(Rect rect, Color color)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, WhiteTexture);
        GUI.color = old;
    }

    public static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 2f)
    {
        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (length <= 0.01f) return;

        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.color = color;
        GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), WhiteTexture);

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    public static Rect ClampToScreen(Rect rect, float margin = 8f)
    {
        float width = Screen.width;
        float height = Screen.height;

        rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, width - rect.width - margin));
        rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, height - rect.height - margin));
        return rect;
    }

    public static Rect ClampToArea(Rect rect, float width, float height, float margin = 8f)
    {
        rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, width - rect.width - margin));
        rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, height - rect.height - margin));
        return rect;
    }
}

/// <summary>
/// 可折叠、可滚动、可拖动、可缩放的UI面板基类
/// </summary>
public class CollapsiblePanel
{
    public string title;
    public Rect position;
    public bool isExpanded = true;
    public bool isVisible = true;
    public float scale = 1.0f;
    public float minScale = 0.5f;
    public float maxScale = 2.0f;
    public float scaleStep = 0.1f;

    private bool _isDragging = false;
    private Vector2 _dragStart;
    private Vector2 _scrollPosition = Vector2.zero;
    private const float HeaderHeight = 24f;
    private const float Padding = 10f;

    public CollapsiblePanel(string title, Rect position)
    {
        this.title = title;
        this.position = position;
    }

    /// <summary>
    /// 绘制面板
    /// </summary>
    /// <param name="drawContent">内容绘制回调</param>
    public void Draw(System.Action<Rect> drawContent)
    {
        if (!isVisible) return;

        // 应用缩放
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(position.position, Quaternion.identity, new Vector3(scale, scale, 1f));

        // 计算缩放后的矩形
        Rect scaledRect = new Rect(0, 0, position.width / scale, position.height / scale);

        // 绘制面板背景
        Color bgColor = new Color(0.05f, 0.06f, 0.08f, 0.96f);
        Color borderColor = new Color(1f, 1f, 1f, 0.12f);
        Color accentColor = new Color(0.18f, 0.82f, 1f, 1f);
        RuntimeUiTheme.DrawPanel(scaledRect, bgColor, borderColor, 1f, 4f, accentColor);

        // 绘制标题栏
        Rect headerRect = new Rect(scaledRect.x, scaledRect.y, scaledRect.width, HeaderHeight);
        GUI.Box(headerRect, "");

        // 折叠/展开按钮
        Rect toggleRect = new Rect(headerRect.x + 8, headerRect.y + 4, 16, 16);
        isExpanded = GUI.Toggle(toggleRect, isExpanded, isExpanded ? "▼" : "▶");

        // 标题文本
        Rect titleRect = new Rect(headerRect.x + 30, headerRect.y, headerRect.width - 100, headerRect.height);
        GUI.Label(titleRect, title, RuntimeUiTheme.CreateLabelStyle(14, Color.white, false, FontStyle.Bold));

        // 关闭按钮
        Rect closeRect = new Rect(headerRect.x + headerRect.width - 60, headerRect.y + 2, 50, 20);
        if (GUI.Button(closeRect, "关闭"))
        {
            isVisible = false;
        }

        // 缩放控制
        Rect scaleDownRect = new Rect(headerRect.x + headerRect.width - 110, headerRect.y + 2, 20, 20);
        if (GUI.Button(scaleDownRect, "-"))
        {
            scale = Mathf.Max(minScale, scale - scaleStep);
        }

        Rect scaleUpRect = new Rect(headerRect.x + headerRect.width - 80, headerRect.y + 2, 20, 20);
        if (GUI.Button(scaleUpRect, "+"))
        {
            scale = Mathf.Min(maxScale, scale + scaleStep);
        }

        // 拖动逻辑
        if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
        {
            _isDragging = true;
            _dragStart = Event.current.mousePosition - position.position;
            Event.current.Use();
        }

        if (Event.current.type == EventType.MouseDrag && _isDragging)
        {
            position.position = Event.current.mousePosition - _dragStart;
            position = RuntimeUiTheme.ClampToScreen(position);
            Event.current.Use();
        }

        if (Event.current.type == EventType.MouseUp)
        {
            _isDragging = false;
        }

        // 绘制内容
        if (isExpanded)
        {
            Rect contentRect = new Rect(
                scaledRect.x + Padding,
                scaledRect.y + HeaderHeight + Padding,
                scaledRect.width - Padding * 2,
                scaledRect.height - HeaderHeight - Padding * 2
            );

            // 添加滚动视图
            _scrollPosition = GUI.BeginScrollView(
                contentRect,
                _scrollPosition,
                new Rect(0, 0, contentRect.width - 20, contentRect.height * 2) // 假设内容可能是面板高度的两倍
            );

            // 调用内容绘制回调
            drawContent(new Rect(0, 0, contentRect.width - 20, contentRect.height * 2));

            GUI.EndScrollView();
        }

        // 恢复矩阵
        GUI.matrix = oldMatrix;
    }

    /// <summary>
    /// 切换面板可见性
    /// </summary>
    public void ToggleVisibility()
    {
        isVisible = !isVisible;
    }

    /// <summary>
    /// 重置面板位置和大小
    /// </summary>
    /// <param name="newPosition">新位置</param>
    public void ResetPosition(Rect newPosition)
    {
        position = newPosition;
        scale = 1.0f;
    }
}