using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 智能射线脚本
/// 一个脚本支持两种输入模式：
/// 1) PC 鼠标键盘模拟（WASD/QE 移动 + 右键拖拽视角 + 鼠标射线点击）
/// 2) VR 手柄射线（InputAction 点击）
/// </summary>
[DefaultExecutionOrder(1000)]
public class SmartRaycaster : MonoBehaviour
{
    public enum ControlMode
    {
        DesktopMouseKeyboard,
        VRController
    }

    [Header("Control Mode")]
    [Tooltip("DesktopMouseKeyboard=电脑端模拟 | VRController=VR手柄")]
    public ControlMode controlMode = ControlMode.DesktopMouseKeyboard;

    [Header("Desktop Sim (PC)")]
    [Tooltip("PC 模式主相机（留空时自动取 Camera.main）")]
    public Camera desktopCamera;
    [Tooltip("PC 模式移动根节点（留空时默认 Main Camera）")]
    public Transform desktopMoveRoot;
    [Tooltip("移动速度（米/秒）")]
    public float moveSpeed = 2.0f;
    [Tooltip("按住 Shift 的加速倍率")]
    public float fastMoveMultiplier = 2.5f;
    [Tooltip("鼠标拖拽视角灵敏度")]
    public float lookSensitivity = 0.12f;
    [Tooltip("勾选后，只有按住右键时才允许 WASD/QE 移动（更像 Scene 视图）")]
    public bool requireRightMouseForMove = false;
    [Tooltip("右键拖拽视角时锁定并隐藏鼠标")]
    public bool lockCursorWhileLooking = true;

    [Header("VR Input")]
    [Tooltip("绑定 XRI Left/Right Hand -> Activate 输入事件")]
    public InputActionReference interactAction;
    [Tooltip("VR 射线起点（手柄末端 Transform）")]
    public Transform vrRayOrigin;

    [Header("Raycast")]
    [Tooltip("零件所在 Layer（在 LayerMask 下拉框选择）")]
    public LayerMask partLayer;
    [Tooltip("射线最大检测距离（米）")]
    public float maxDistance = 10f;

    [Header("Ray Visual")]
    [Tooltip("在 Game 视图中显示射线")]
    public bool showRayInGame = true;
    [Tooltip("使用屏幕叠加方式绘制射线，更稳定且不受遮挡")]
    public bool useScreenSpaceRayVisual = true;
    [Tooltip("射线永远显示在最前面，不受场景遮挡")]
    public bool rayAlwaysOnTop = true;
    [Tooltip("未命中时射线颜色")]
    public Color rayColor = Color.cyan;
    [Tooltip("命中时射线颜色")]
    public Color rayHitColor = Color.green;
    [Tooltip("屏幕叠加射线阴影色")]
    public Color rayShadowColor = new Color(0f, 0f, 0f, 0.35f);
    [Tooltip("屏幕叠加射线线宽")]
    public float rayScreenThickness = 4f;
    [Tooltip("射线线宽（世界单位）")]
    public float rayWidth = 0.004f;
    [Tooltip("射线视觉起点前移距离，避免被相机近裁剪面吃掉")]
    public float rayVisualStartOffset = 0.03f;
    [Tooltip("可手动指定 LineRenderer；留空自动创建")]
    public LineRenderer rayLineRenderer;

    [Header("Hover Highlight（悬浮高亮）")]
    [Tooltip("用于高亮的材质（可选；留空则不做 Hover 高亮）")]
    public Material highlightMaterial;
    [Tooltip("高亮颜色")]
    public Color highlightColor = Color.green;
    [Tooltip("轮廓线宽")]
    public float outlineWidth = 2f;

    [System.Serializable]
    public class HoverInfoSettings
    {
        [Tooltip("鼠标悬浮时显示零件文字信息")]
        public bool showHoverInfoPanel = true;
        [Tooltip("在模型旁显示浮动名称标签")]
        public bool showFloatingHoverTag = true;
        [Tooltip("切换悬浮信息面板的按键")]
        public KeyCode hoverInfoToggleKey = KeyCode.V;
        [Tooltip("悬浮信息面板默认是否可见")]
        public bool hoverInfoVisible = true;
        [Tooltip("悬浮信息面板位置与大小")]
        public Rect hoverInfoRect = new Rect(10, 160, 360, 120);
        [Tooltip("悬浮信息面板字体大小")]
        public int hoverInfoFontSize = 16;
        [Tooltip("悬浮信息面板字体颜色")]
        public Color hoverInfoColor = Color.white;
        [Tooltip("悬浮信息显示详细程度")]
        public bool hoverInfoShowDetailed = false;
        [Tooltip("浮动标签宽度")]
        public float hoverTagWidth = 280f;
        [Tooltip("浮动标签最小高度")]
        public float hoverTagMinHeight = 54f;
        [Tooltip("浮动标签在屏幕上的偏移")]
        public Vector2 hoverTagScreenOffset = new Vector2(18f, -10f);
        [Tooltip("浮动标签背景颜色")]
        public Color hoverTagBackgroundColor = new Color(0.08f, 0.08f, 0.10f, 0.94f);
        [Tooltip("浮动标签边框颜色")]
        public Color hoverTagBorderColor = new Color(1f, 1f, 1f, 0.14f);
        [Tooltip("浮动标签强调色")]
        public Color hoverTagAccentColor = new Color(0.15f, 0.85f, 1f, 1f);
        [Tooltip("浮动标签标题颜色")]
        public Color hoverTagTitleColor = Color.white;
        [Tooltip("浮动标签副标题颜色")]
        public Color hoverTagSubtitleColor = new Color(0.85f, 0.90f, 0.95f, 1f);
        [Tooltip("浮动标签标题字号")]
        public int hoverTagTitleFontSize = 18;
        [Tooltip("浮动标签副标题字号")]
        public int hoverTagSubtitleFontSize = 12;
    }

    [Header("Hover Info（悬浮信息）")]
    public HoverInfoSettings hoverInfoSettings = new HoverInfoSettings();

    [Header("Click Feedback（点击反馈）")]
    [Tooltip("点击成功时，零件短暂变色提示")]
    public bool enableClickColorFeedback = true;
    [Tooltip("点击提示颜色")]
    public Color clickColor = Color.yellow;
    [Tooltip("点击提示持续时间（秒）")]
    public float clickFlashDuration = 0.15f;

    // ── 私有状态 ──────────────────────────────────────────

    private DisassemblyPart _lastHovered = null;
    private ControlMode _lastControlMode;
    private float _pitch;
    private float _yaw;
    private float _rayFlashTimer;
    private Color _rayFlashColor = Color.white;
    private string _hoverInfoText = string.Empty;
    private string _hoverInfoTitle = string.Empty;
    private string _hoverInfoSubtitle = string.Empty;
    private string _hoverInfoDetails = string.Empty;
    private Vector3 _cachedHitWorldPoint = Vector3.zero;
    private bool _hasCachedHitWorldPoint = false;
    private Vector2 _cachedRayScreenStart = Vector2.zero;
    private Vector2 _cachedRayScreenEnd = Vector2.zero;
    private bool _hasCachedRayScreenLine = false;
    private Color _cachedRayScreenColor = Color.white;

    // 可折叠面板
    // 可折叠面板
    private CollapsiblePanel _hoverInfoPanel;
    private bool _panelInitialized = false;

    // 仅保存原始材质，实例化的高亮材质用完立即销毁。
    private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
    private readonly Dictionary<Renderer, int> _hoverColorProps = new();
    private readonly Dictionary<Renderer, Color> _hoverOriginalColors = new();
    private readonly List<Material> _tempMaterials = new();
    private readonly Dictionary<Renderer, Coroutine> _clickFlashCoroutines = new();

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public DisassemblyPart CurrentHoveredPart => _cachedHitPart;
    public string CurrentHoverInfoText => _hoverInfoText;

    // ── Unity 生命周期 ────────────────────────────────────

    private void Awake()
    {
        if (desktopCamera == null) desktopCamera = Camera.main;
        if (desktopMoveRoot == null || desktopMoveRoot == transform)
            desktopMoveRoot = desktopCamera != null ? desktopCamera.transform : transform;

        if (partLayer.value == 0)
            partLayer = ~0;

        _lastControlMode = controlMode;
        CacheCameraAngles();
        EnsureRayLineRenderer();

        // 初始化可折叠面板
        _hoverInfoPanel = new CollapsiblePanel("零件信息", hoverInfoSettings.hoverInfoRect);
        _hoverInfoPanel.isVisible = hoverInfoSettings.hoverInfoVisible;
        _panelInitialized = true;
    }

    private void OnEnable()
    {
        interactAction?.action.Enable();
    }

    private void OnDisable()
    {
        interactAction?.action.Disable();
        SetCursorLock(false);
        _hasCachedRayScreenLine = false;
        if (rayLineRenderer != null) rayLineRenderer.enabled = false;
    }

    private void Update()
    {
        HandleModeSwitch();

        if (controlMode == ControlMode.DesktopMouseKeyboard)
            UpdateDesktopMovementAndLook();

        if (hoverInfoSettings.showHoverInfoPanel && Input.GetKeyDown(hoverInfoSettings.hoverInfoToggleKey))
        {
            if (_panelInitialized)
                _hoverInfoPanel.ToggleVisibility();
            else
                hoverInfoSettings.hoverInfoVisible = !hoverInfoSettings.hoverInfoVisible;
        }

        if (_rayFlashTimer > 0f)
            _rayFlashTimer -= Time.deltaTime;

        if (!TryBuildActiveRay(out Ray ray))
        {
            ClearHover();
            UpdateRayVisual(false, default, Vector3.zero, false);
            return;
        }

        bool hasHover = TryGetFirstInteractablePart(ray, out _, out _);
        Vector3 endPoint = ray.origin + ray.direction * maxDistance;
        UpdateHoverFromCachedHit();
        UpdateHoverInfoText();
        UpdateRayVisual(true, ray, endPoint, hasHover);

        bool triggerPressed = controlMode switch
        {
            ControlMode.DesktopMouseKeyboard => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame,
            ControlMode.VRController => interactAction != null && interactAction.action.WasPressedThisFrame(),
            _ => false
        };

        if (triggerPressed)
            TryInteract(ray);
    }

    // ── 输入与射线 ────────────────────────────────────────

    private void HandleModeSwitch()
    {
        if (_lastControlMode == controlMode) return;

        ClearHover();
        SetCursorLock(false);
        CacheCameraAngles();
        _lastControlMode = controlMode;
    }

    private bool TryBuildActiveRay(out Ray ray)
    {
        switch (controlMode)
        {
            case ControlMode.DesktopMouseKeyboard:
                if (desktopCamera == null) desktopCamera = Camera.main;
                if (desktopCamera == null)
                {
                    ray = default;
                    return false;
                }

                Vector2 mousePos = Mouse.current != null
                    ? Mouse.current.position.ReadValue()
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                ray = desktopCamera.ScreenPointToRay(mousePos);
                return true;

            case ControlMode.VRController:
                Transform origin = vrRayOrigin != null ? vrRayOrigin : transform;
                ray = new Ray(origin.position, origin.forward);
                return true;

            default:
                ray = default;
                return false;
        }
    }

    private void UpdateDesktopMovementAndLook()
    {
        if (desktopCamera == null && desktopMoveRoot == null) return;
        if (Keyboard.current == null) return;

        bool rightMouseHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
        bool canMove = !requireRightMouseForMove || rightMouseHeld;
        Transform cameraT = desktopCamera != null ? desktopCamera.transform : desktopMoveRoot;

        if (cameraT == null) return;

        if (canMove)
        {
            float x = 0f;
            float y = 0f;
            float z = 0f;

            if (Keyboard.current.aKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.qKey.isPressed) y -= 1f;
            if (Keyboard.current.eKey.isPressed) y += 1f;
            if (Keyboard.current.sKey.isPressed) z -= 1f;
            if (Keyboard.current.wKey.isPressed) z += 1f;

            Vector3 move = new Vector3(x, y, z);
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = moveSpeed;
            if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
                speed *= fastMoveMultiplier;

            cameraT.Translate(move * speed * Time.deltaTime, Space.Self);
        }

        if (Mouse.current == null) return;

        if (rightMouseHeld)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            float yawDelta = delta.x * lookSensitivity;
            float pitchDelta = -delta.y * lookSensitivity;

            _yaw += yawDelta;
            _pitch = Mathf.Clamp(_pitch + pitchDelta, -85f, 85f);
            cameraT.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);

            SetCursorLock(lockCursorWhileLooking);
        }
        else
        {
            SetCursorLock(false);
        }
    }

    private void CacheCameraAngles()
    {
        Transform lookRef = desktopCamera != null ? desktopCamera.transform : desktopMoveRoot;
        if (lookRef == null) lookRef = transform;

        Vector3 euler = lookRef.localEulerAngles;
        _pitch = NormalizeAngle(euler.x);
        _yaw = NormalizeAngle(euler.y);
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    private void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ── 悬浮高亮 ──────────────────────────────────────────

    private void UpdateHoverFromCachedHit()
    {
        DisassemblyPart hovered = _cachedHitPart;
        if (hovered == _lastHovered) return;

        if (_lastHovered != null) RemoveHighlight(_lastHovered);
        if (hovered != null) ApplyHighlight(hovered);
        _lastHovered = hovered;
    }

    private DisassemblyPart _cachedHitPart;

    private bool TryGetFirstInteractablePart(Ray ray, out RaycastHit closestHit, out DisassemblyPart hoveredPart)
    {
        _cachedHitPart = null;
        _hasCachedHitWorldPoint = false;
        closestHit = default;
        hoveredPart = null;

        if (DisassemblySystem.Instance == null) return false;

        var hits = Physics.RaycastAll(ray, maxDistance, GetRaycastMask(), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var part = hit.collider.GetComponentInParent<DisassemblyPart>();
            if (part == null || part.isCompleted) continue;
            if (!DisassemblySystem.Instance.CheckInteractability(part)) continue;

            _cachedHitPart = part;
            hoveredPart = part;
            closestHit = hit;
            _cachedHitWorldPoint = hit.point;
            _hasCachedHitWorldPoint = true;
            return true;
        }

        return false;
    }

    private void UpdateHoverInfoText()
    {
        if (_cachedHitPart == null)
        {
            _hoverInfoText = string.Empty;
            _hoverInfoTitle = string.Empty;
            _hoverInfoSubtitle = string.Empty;
            _hoverInfoDetails = string.Empty;
            return;
        }

        var layer = _cachedHitPart.GetComponentInParent<LayerGroupConfig>();
        string groupName = _cachedHitPart.myPriorityGroup != null ? _cachedHitPart.myPriorityGroup.groupName : "无pri";
        Transform resolvedSyncRoot = _cachedHitPart.GetResolvedSyncRoot();
        string syncRootName = resolvedSyncRoot != null ? resolvedSyncRoot.name : "无同步组";
        string layerName = layer != null ? layer.name : "无层";

        _hoverInfoTitle = _cachedHitPart.name;
        _hoverInfoSubtitle = $"{layerName}  |  {groupName}  |  同步组: {syncRootName}";

        if (!hoverInfoSettings.hoverInfoShowDetailed)
        {
            _hoverInfoDetails = string.Empty;
            _hoverInfoText = _hoverInfoTitle;
            return;
        }

        string parentName = _cachedHitPart.transform.parent != null ? _cachedHitPart.transform.parent.name : "无父物体";
        _hoverInfoDetails =
            $"父级：{parentName}\n" +
            $"完成：{_cachedHitPart.isCompleted}\n" +
            $"交互：{(_cachedHitPart.isCompleted ? "已完成" : "可检测")}";
        _hoverInfoText =
            $"{_hoverInfoTitle}\n" +
            $"{_hoverInfoSubtitle}\n" +
            $"{_hoverInfoDetails}";
    }

    private void ApplyHighlight(DisassemblyPart part)
    {
        if (part == null) return;
        foreach (var rend in part.GetComponentsInChildren<Renderer>(true))
        {
            if (!rend.enabled) continue;

            if (highlightMaterial != null)
            {
                _originalMaterials[rend] = rend.sharedMaterials;

                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = Instantiate(highlightMaterial);
                    if (mat.HasProperty(OutlineColorId)) mat.SetColor(OutlineColorId, highlightColor);
                    if (mat.HasProperty(OutlineWidthId)) mat.SetFloat(OutlineWidthId, outlineWidth);
                    mats[i] = mat;
                    _tempMaterials.Add(mat);
                }
                rend.materials = mats;
                continue;
            }

            if (!TryGetColorPropertyId(rend, out int colorPropId)) continue;
            _hoverColorProps[rend] = colorPropId;
            _hoverOriginalColors[rend] = GetRendererColorFallback(rend, colorPropId);

            var block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);
            block.SetColor(colorPropId, highlightColor);
            rend.SetPropertyBlock(block);
        }
    }

    private void RemoveHighlight(DisassemblyPart part)
    {
        foreach (var rend in part.GetComponentsInChildren<Renderer>(true))
        {
            if (_originalMaterials.TryGetValue(rend, out var orig))
            {
                foreach (var mat in rend.materials)
                {
                    if (mat != null)
                    {
                        _tempMaterials.Remove(mat);
                        Destroy(mat);
                    }
                }
                rend.sharedMaterials = orig;
                _originalMaterials.Remove(rend);
                continue;
            }

            if (_hoverColorProps.TryGetValue(rend, out int colorPropId))
            {
                var block = new MaterialPropertyBlock();
                rend.GetPropertyBlock(block);
                if (_hoverOriginalColors.TryGetValue(rend, out var originalColor))
                    block.SetColor(colorPropId, originalColor);
                else
                    block.Clear();
                rend.SetPropertyBlock(block);
                _hoverColorProps.Remove(rend);
                _hoverOriginalColors.Remove(rend);
            }
        }
    }

    // ── 交互触发 ──────────────────────────────────────────

    private void TryInteract(Ray ray)
    {
        if (!TryGetFirstInteractablePart(ray, out _, out _)) return;
        DisassemblyPart part = _cachedHitPart;
        if (part == null) return;

        var config = part.GetComponentInParent<LayerGroupConfig>();
        if (config == null) return;

        RemoveHighlight(part);
        part.Interact(config.liftHeight, config.liftSpeed);
        PlayClickFeedback(part);
        BeginRayFlash(clickColor, clickFlashDuration);
        Debug.Log($"[交互] 触发分离：{part.name}");

        _lastHovered = null;
    }

    private void PlayClickFeedback(DisassemblyPart part)
    {
        if (!enableClickColorFeedback || clickFlashDuration <= 0f) return;

        foreach (var rend in part.GetComponentsInChildren<Renderer>(true))
        {
            if (!rend.enabled) continue;
            if (!TryGetColorPropertyId(rend, out int colorPropId)) continue;

            if (_clickFlashCoroutines.TryGetValue(rend, out Coroutine old))
                StopCoroutine(old);

            _clickFlashCoroutines[rend] = StartCoroutine(FlashRendererColor(rend, colorPropId));
        }
    }

    private IEnumerator FlashRendererColor(Renderer rend, int colorPropId)
    {
        if (rend == null)
            yield break;

        Color originalColor = GetRendererColorFallback(rend, colorPropId);

        var block = new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetColor(colorPropId, clickColor);
        rend.SetPropertyBlock(block);

        yield return new WaitForSeconds(clickFlashDuration);

        if (rend != null)
        {
            rend.GetPropertyBlock(block);
            block.SetColor(colorPropId, originalColor);
            rend.SetPropertyBlock(block);
        }

        _clickFlashCoroutines.Remove(rend);
    }

    private bool TryGetColorPropertyId(Renderer rend, out int colorPropId)
    {
        colorPropId = -1;
        if (rend == null) return false;

        foreach (var mat in rend.sharedMaterials)
        {
            if (mat == null) continue;
            if (mat.HasProperty(BaseColorId))
            {
                colorPropId = BaseColorId;
                return true;
            }
            if (mat.HasProperty(ColorId))
            {
                colorPropId = ColorId;
                return true;
            }
        }

        return false;
    }

    private Color GetRendererColorFallback(Renderer rend, int colorPropId)
    {
        foreach (var mat in rend.sharedMaterials)
        {
            if (mat != null && mat.HasProperty(colorPropId))
                return mat.GetColor(colorPropId);
        }
        return Color.white;
    }

    private void BeginRayFlash(Color color, float duration)
    {
        _rayFlashColor = color;
        _rayFlashTimer = Mathf.Max(_rayFlashTimer, duration);
    }

    private LayerMask GetRaycastMask()
    {
        return partLayer.value != 0 ? partLayer : ~0;
    }

    // ── 射线可视化 ────────────────────────────────────────

    private void EnsureRayLineRenderer()
    {
        if (useScreenSpaceRayVisual) return;
        if (rayLineRenderer != null) return;

        var lineGO = new GameObject("SmartRay_Line");
        lineGO.transform.SetParent(transform, false);
        rayLineRenderer = lineGO.AddComponent<LineRenderer>();
        rayLineRenderer.useWorldSpace = true;
        rayLineRenderer.material = CreateRayMaterial();
        rayLineRenderer.alignment = LineAlignment.View;
        rayLineRenderer.textureMode = LineTextureMode.Stretch;
        rayLineRenderer.sortingOrder = short.MaxValue;
        rayLineRenderer.sortingLayerName = "Default";
        rayLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rayLineRenderer.receiveShadows = false;
        rayLineRenderer.allowOcclusionWhenDynamic = false;
        rayLineRenderer.positionCount = 2;
        rayLineRenderer.numCapVertices = 4;
        rayLineRenderer.numCornerVertices = 2;
    }

    private Material CreateRayMaterial()
    {
        Shader shader = rayAlwaysOnTop
            ? Shader.Find("Hidden/Disassembly/AlwaysOnTopLine")
            : null;
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        if (rayAlwaysOnTop && mat.HasProperty("_ZTest"))
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);

        mat.renderQueue = 4000;
        return mat;
    }

    private void UpdateRayVisual(bool hasValidRay, Ray ray, Vector3 endPoint, bool hovered)
    {
        if (useScreenSpaceRayVisual)
        {
            CacheScreenSpaceRayVisual(hasValidRay, ray, endPoint, hovered);
            if (rayLineRenderer != null) rayLineRenderer.enabled = false;
            return;
        }

        if (!showRayInGame)
        {
            if (rayLineRenderer != null) rayLineRenderer.enabled = false;
            return;
        }

        EnsureRayLineRenderer();
        if (rayLineRenderer == null) return;

        rayLineRenderer.enabled = hasValidRay;
        if (!hasValidRay) return;

        Color currentColor = _rayFlashTimer > 0f
            ? _rayFlashColor
            : (hovered ? rayHitColor : rayColor);

        Vector3 visualEndPoint = hovered && _hasCachedHitWorldPoint ? _cachedHitWorldPoint : endPoint;

        rayLineRenderer.startWidth = rayWidth;
        rayLineRenderer.endWidth = rayWidth;
        rayLineRenderer.startColor = currentColor;
        rayLineRenderer.endColor = currentColor;
        Vector3 startPoint = ray.origin + ray.direction * Mathf.Max(0f, rayVisualStartOffset);
        rayLineRenderer.SetPosition(0, startPoint);
        rayLineRenderer.SetPosition(1, visualEndPoint);
    }

    private void CacheScreenSpaceRayVisual(bool hasValidRay, Ray ray, Vector3 endPoint, bool hovered)
    {
        _hasCachedRayScreenLine = false;

        if (!showRayInGame || !hasValidRay) return;

        Camera cam = desktopCamera != null ? desktopCamera : Camera.main;
        if (cam == null) return;

        Vector3 visualEndPoint = hovered && _hasCachedHitWorldPoint ? _cachedHitWorldPoint : endPoint;
        Vector3 screenStart = cam.WorldToScreenPoint(ray.origin);
        Vector3 screenEnd = cam.WorldToScreenPoint(visualEndPoint);
        if (screenStart.z <= 0f || screenEnd.z <= 0f) return;

        _cachedRayScreenStart = new Vector2(screenStart.x, Screen.height - screenStart.y);
        _cachedRayScreenEnd = new Vector2(screenEnd.x, Screen.height - screenEnd.y);
        _cachedRayScreenColor = _rayFlashTimer > 0f
            ? _rayFlashColor
            : (hovered ? rayHitColor : rayColor);
        _hasCachedRayScreenLine = true;
    }

    private bool TryGetHoverTagAnchor(out Vector2 anchor)
    {
        anchor = default;
        if (_cachedHitPart == null) return false;

        Camera cam = desktopCamera != null ? desktopCamera : Camera.main;
        if (cam == null) return false;

        if (!TryGetPartWorldBounds(_cachedHitPart, out Bounds bounds))
            bounds = new Bounds(_cachedHitPart.transform.position, Vector3.one * 0.1f);

        Vector3 worldAnchor = bounds.center + Vector3.up * Mathf.Max(bounds.extents.y, 0.03f);
        Vector3 screen = cam.WorldToScreenPoint(worldAnchor);
        if (screen.z <= 0f) return false;

        anchor = new Vector2(screen.x, Screen.height - screen.y);
        return true;
    }

    private bool TryGetPartWorldBounds(DisassemblyPart part, out Bounds bounds)
    {
        bounds = default;
        if (part == null) return false;

        Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
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

        Collider[] colliders = part.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
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

    private void OnGUI()
    {
        DrawScreenSpaceRayVisual();

        if (_cachedHitPart != null && hoverInfoSettings.showFloatingHoverTag && TryGetHoverTagAnchor(out Vector2 anchor))
            DrawFloatingHoverTag(anchor);

        if (!hoverInfoSettings.showHoverInfoPanel) return;
        if (string.IsNullOrEmpty(_hoverInfoText)) return;

        if (_panelInitialized)
        {
            _hoverInfoPanel.Draw((contentRect) => {
                DrawHoverDetailCardContent(contentRect);
            });
        }
        else if (hoverInfoSettings.hoverInfoVisible)
        {
            DrawHoverDetailCard();
        }
    }

    private void DrawScreenSpaceRayVisual()
    {
        if (!useScreenSpaceRayVisual || !showRayInGame || !_hasCachedRayScreenLine) return;

        RuntimeUiTheme.DrawLine(_cachedRayScreenStart, _cachedRayScreenEnd, rayShadowColor, rayScreenThickness + 2f);
        RuntimeUiTheme.DrawLine(_cachedRayScreenStart, _cachedRayScreenEnd, _cachedRayScreenColor, rayScreenThickness);
    }

    private void DrawFloatingHoverTag(Vector2 anchor)
    {
        float subtitleHeight = hoverInfoSettings.hoverInfoShowDetailed ? 34f : 20f;
        float height = Mathf.Max(hoverInfoSettings.hoverTagMinHeight, hoverInfoSettings.hoverTagTitleFontSize + subtitleHeight + 18f);

        Rect rect = new Rect(
            anchor.x + hoverInfoSettings.hoverTagScreenOffset.x,
            anchor.y + hoverInfoSettings.hoverTagScreenOffset.y - height,
            hoverInfoSettings.hoverTagWidth,
            height);
        rect = RuntimeUiTheme.ClampToScreen(rect, 10f);

        Vector2 lineStart = anchor;
        Vector2 lineEnd = new Vector2(rect.x, rect.center.y);
        if (TryGetHoverLineStart(out Vector2 hitPoint))
        {
            RuntimeUiTheme.DrawLine(hitPoint, lineEnd, rayShadowColor, rayScreenThickness + 2f);
            RuntimeUiTheme.DrawLine(hitPoint, lineEnd, hoverInfoSettings.hoverTagAccentColor, rayScreenThickness);
        }
        else
        {
            RuntimeUiTheme.DrawLine(lineStart, lineEnd, rayShadowColor, rayScreenThickness + 2f);
            RuntimeUiTheme.DrawLine(lineStart, lineEnd, hoverInfoSettings.hoverTagAccentColor, rayScreenThickness);
        }
        RuntimeUiTheme.DrawPanel(rect, hoverInfoSettings.hoverTagBackgroundColor, hoverInfoSettings.hoverTagBorderColor, 1f, 3f, hoverInfoSettings.hoverTagAccentColor);

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(hoverInfoSettings.hoverTagTitleFontSize, hoverInfoSettings.hoverTagTitleColor, false, FontStyle.Bold);
        var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(hoverInfoSettings.hoverTagSubtitleFontSize, hoverInfoSettings.hoverTagSubtitleColor, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(11, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

        Rect titleRect = new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, hoverInfoSettings.hoverTagTitleFontSize + 4f);
        Rect subtitleRect = new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 34f);

        GUI.Label(titleRect, _hoverInfoTitle, titleStyle);
        RuntimeUiTheme.DrawPill(new Rect(rect.x + rect.width - 74f, rect.y + 7f, 62f, 20f), "Hover", pillStyle, new Color(0.18f, 0.82f, 1f, 0.95f), new Color(0.28f, 0.95f, 1f, 1f));
        GUI.Label(subtitleRect, _hoverInfoSubtitle, subtitleStyle);

        RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + rect.height - 5f, 42f, 2f), hoverInfoSettings.hoverTagAccentColor);
    }

    private bool TryGetHoverLineStart(out Vector2 guiPoint)
    {
        guiPoint = default;
        if (!_hasCachedHitWorldPoint) return false;

        Camera cam = desktopCamera != null ? desktopCamera : Camera.main;
        if (cam == null) return false;

        Vector3 screen = cam.WorldToScreenPoint(_cachedHitWorldPoint);
        if (screen.z <= 0f) return false;

        guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        return true;
    }

    private void DrawHoverDetailCard()
    {
        Rect rect = hoverInfoSettings.hoverInfoRect;
        rect.width = Mathf.Max(300f, rect.width);
        rect.height = hoverInfoSettings.hoverInfoShowDetailed ? Mathf.Max(194f, rect.height) : Mathf.Max(156f, rect.height);
        rect = RuntimeUiTheme.ClampToScreen(rect, 10f);

        Color cardBg = new Color(0.05f, 0.06f, 0.08f, 0.96f);
        Color cardBorder = new Color(1f, 1f, 1f, 0.12f);
        Color cardAccent = new Color(0.18f, 0.82f, 1f, 1f);
        RuntimeUiTheme.DrawPanel(rect, cardBg, cardBorder, 1f, 4f, cardAccent);

        var titleStyle = RuntimeUiTheme.CreateLabelStyle(18, Color.white, false, FontStyle.Bold);
        var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(hoverInfoSettings.hoverTagSubtitleFontSize, hoverInfoSettings.hoverTagSubtitleColor, true, FontStyle.Normal);
        var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, hoverInfoSettings.hoverInfoColor, true, FontStyle.Normal);
        var pillStyle = RuntimeUiTheme.CreateLabelStyle(12, new Color(0.05f, 0.06f, 0.08f, 1f), false, FontStyle.Bold);

        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), "零件信息", titleStyle);
        Rect pillRect = new Rect(rect.x + rect.width - 90f, rect.y + 8f, 78f, 22f);
        RuntimeUiTheme.DrawPill(pillRect, "Hover", pillStyle, new Color(0.18f, 0.82f, 1f, 0.95f), new Color(0.28f, 0.95f, 1f, 1f));

        GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 18f), _hoverInfoTitle, subtitleStyle);
        RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 52f, rect.width - 24f, 1f), cardBorder);

        float y = rect.y + 60f;
        float rowHeight = 20f;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "层级", _cachedHitPart.GetComponentInParent<LayerGroupConfig>() != null ? _cachedHitPart.GetComponentInParent<LayerGroupConfig>().name : "无层", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "优先组", _cachedHitPart.myPriorityGroup != null ? _cachedHitPart.myPriorityGroup.groupName : "无 pri", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "同步组", _cachedHitPart.GetResolvedSyncRoot() != null ? _cachedHitPart.GetResolvedSyncRoot().name : "无", labelStyle, valueStyle);
        y += rowHeight;

        if (hoverInfoSettings.hoverInfoShowDetailed)
        {
            RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "父级", _cachedHitPart.transform.parent != null ? _cachedHitPart.transform.parent.name : "无", labelStyle, valueStyle);
            y += rowHeight;
            RuntimeUiTheme.DrawLabeledRow(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "状态", _cachedHitPart.isCompleted ? "已完成" : "可交互", labelStyle, valueStyle);
            y += rowHeight;

            GUI.Label(new Rect(rect.x + 12f, rect.y + rect.height - 24f, rect.width - 24f, 18f), "V 切换详细信息", subtitleStyle);
        }
        else
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + rect.height - 24f, rect.width - 24f, 18f), "V 显示详细信息", subtitleStyle);
        }

        RuntimeUiTheme.DrawSolidRect(new Rect(rect.x + 12f, rect.y + rect.height - 5f, 60f, 2f), cardAccent);
    }

    private void DrawHoverDetailCardContent(Rect contentRect)
    {
        var subtitleStyle = RuntimeUiTheme.CreateLabelStyle(hoverInfoSettings.hoverTagSubtitleFontSize, hoverInfoSettings.hoverTagSubtitleColor, true, FontStyle.Normal);
        var labelStyle = RuntimeUiTheme.CreateLabelStyle(13, new Color(0.78f, 0.82f, 0.86f, 1f), false, FontStyle.Normal);
        var valueStyle = RuntimeUiTheme.CreateLabelStyle(14, hoverInfoSettings.hoverInfoColor, true, FontStyle.Normal);

        float y = 0f;
        float rowHeight = 20f;

        GUI.Label(new Rect(contentRect.x, y, contentRect.width, 24f), _hoverInfoTitle, RuntimeUiTheme.CreateLabelStyle(18, Color.white, false, FontStyle.Bold));
        y += 28f;

        GUI.Label(new Rect(contentRect.x, y, contentRect.width, 18f), _hoverInfoSubtitle, subtitleStyle);
        y += 22f;

        RuntimeUiTheme.DrawSolidRect(new Rect(contentRect.x, y, contentRect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
        y += 10f;

        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "层级", _cachedHitPart.GetComponentInParent<LayerGroupConfig>() != null ? _cachedHitPart.GetComponentInParent<LayerGroupConfig>().name : "无层", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "优先组", _cachedHitPart.myPriorityGroup != null ? _cachedHitPart.myPriorityGroup.groupName : "无 pri", labelStyle, valueStyle);
        y += rowHeight;
        RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "同步组", _cachedHitPart.GetResolvedSyncRoot() != null ? _cachedHitPart.GetResolvedSyncRoot().name : "无", labelStyle, valueStyle);
        y += rowHeight;

        if (hoverInfoSettings.hoverInfoShowDetailed)
        {
            RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "父级", _cachedHitPart.transform.parent != null ? _cachedHitPart.transform.parent.name : "无", labelStyle, valueStyle);
            y += rowHeight;
            RuntimeUiTheme.DrawLabeledRow(new Rect(contentRect.x, y, contentRect.width, rowHeight), "状态", _cachedHitPart.isCompleted ? "已完成" : "可交互", labelStyle, valueStyle);
            y += rowHeight;

            GUI.Label(new Rect(contentRect.x, y + 10f, contentRect.width, 18f), "V 切换详细信息", subtitleStyle);
        }
        else
        {
            GUI.Label(new Rect(contentRect.x, y + 10f, contentRect.width, 18f), "V 显示详细信息", subtitleStyle);
        }
    }

    // ── 资源释放 ──────────────────────────────────────────

    private void OnDestroy()
    {
        ClearHover();
        SetCursorLock(false);
        _rayFlashTimer = 0f;
        _hasCachedRayScreenLine = false;

        foreach (var c in _clickFlashCoroutines.Values)
            if (c != null) StopCoroutine(c);
        _clickFlashCoroutines.Clear();

        foreach (var mat in _tempMaterials)
            if (mat != null) Destroy(mat);
        _tempMaterials.Clear();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || rayLineRenderer == null) return;

        Material old = rayLineRenderer.material;
        rayLineRenderer.material = CreateRayMaterial();
        if (old != null)
            Destroy(old);
    }

    private void ClearHover()
    {
        if (_lastHovered != null)
        {
            RemoveHighlight(_lastHovered);
            _lastHovered = null;
        }

        _cachedHitPart = null;
        _hasCachedHitWorldPoint = false;
    }
}