// Play 模式鼠标屏幕射线可视化 + 命中检测接口。
//
// 组成：
//   - 世界射线：LineRenderer 从相机近处画到命中点（未命中则画到最大距离）；
//   - 命中标记：命中点处的圆环（LineRenderer loop，按距离缩放半径）；
//   - HUD：轻量 TMP 文本，显示命中对象名 + 世界坐标 + 屏幕像素（可关）；
//   - 指针来源：真实鼠标（Input System）或测试注入的屏幕坐标（SetPointerOverride）。
//
// UI 指针规则（重要）：EventSystem 判定指针在 UI 上时，世界射线关闭并记录 UI 色，
// 同时 TryGetHit 返回 false（overUi=true），保证"UI 命中不触发任何世界点击"。
//
// 后续 IK/手柄 agent 复用：实现 ITwinPointerHit（TryGetHit(out RaycastHit, out bool overUi)
// + HitChanged 事件），无需依赖本组件的渲染细节。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DigitalTwin.Interaction
{
    /// <summary>一次指针命中快照，供外部（交互/IK/手柄）消费。</summary>
    public struct TwinPointerHitInfo
    {
        public bool HasWorldHit;
        public bool OverUi;
        public RaycastHit Hit;
        public Vector2 ScreenPosition;

        public bool IsPointerBlockedByUi => OverUi;
    }

    /// <summary>对外统一的指针命中接口：后续 IK 手柄 agent 直接实现/调用它。</summary>
    public interface ITwinPointerHit
    {
        /// <summary>世界命中结果；overUi=true 时 worldHit 必为 false（UI 优先，不穿透）。</summary>
        bool TryGetHit(out RaycastHit hit, out bool overUi);

        /// <summary>指针当前是否落在 UI 上（EventSystem 判定）。</summary>
        bool IsPointerOverUi { get; }

        /// <summary>命中信息变化事件（指针进出 UI、命中对象改变时触发）。</summary>
        event Action<TwinPointerHitInfo> HitChanged;
    }

    [DisallowMultipleComponent]
    public sealed class TwinPlayRayVisual : MonoBehaviour, ITwinPointerHit
    {
        [Header("总开关")]
        [Tooltip("射线系统总开关；关闭时不绘制、不检测、不创建 HUD。")]
        [SerializeField] private bool enableRay = true;
        [Tooltip("是否绘制世界射线（总开关打开时才生效）。")]
        [SerializeField] private bool drawWorldRay = true;
        [Tooltip("是否显示命中点标记（圆环）。")]
        [SerializeField] private bool showHitMarker = true;
        [Tooltip("射线与标记是否始终绘制在最前面（关闭深度测试）。")]
        [SerializeField] private bool alwaysOnTop = true;

        [Header("射线参数")]
        [Tooltip("射线所用相机；留空时自动解析 Camera.main。")]
        [SerializeField] private Camera rayCamera;
        [Tooltip("命中层（P1b-1 收敛）：仅 Default(0)；UI(5)/Ignore Raycast(2) 本来就不进 3D 射线。")]
        [SerializeField] private LayerMask hitLayers = 1 << 0;
        [Tooltip("射线最大检测距离（米）。")]
        [SerializeField, Range(1f, 500f)] private float maxDistance = 60f;
        [Tooltip("射线视觉起点相对相机的偏移（米），避免近裁剪面吃掉）。")]
        [SerializeField, Range(0f, 1f)] private float startOffset = 0.05f;
        [Tooltip("射线起点线宽（世界单位）。")]
        [SerializeField, Range(0.0005f, 0.2f)] private float startWidth = 0.02f;
        [Tooltip("射线终点线宽（世界单位）。")]
        [SerializeField, Range(0.0005f, 0.2f)] private float endWidth = 0.006f;

        [Header("四态颜色")]
        [Tooltip("未命中（打到天空/超出最大距离）颜色。")]
        [SerializeField] private Color missColor = new Color(0.13f, 0.85f, 0.94f, 0.9f);
        [Tooltip("命中物体（非 UI）颜色。")]
        [SerializeField] private Color hitObjectColor = new Color(0.13f, 0.77f, 0.37f, 1f);
        [Tooltip("命中 UI 时记录的颜色（此时世界射线关闭）。")]
        [SerializeField] private Color hitUiColor = new Color(0.96f, 0.65f, 0.14f, 1f);
        [Tooltip("按住左键时的射线颜色。")]
        [SerializeField] private Color pressedColor = new Color(1f, 0.95f, 0.6f, 1f);

        [Header("命中点标记")]
        [Tooltip("命中点圆环半径（米），按距离缩放时作为基准。")]
        [SerializeField, Range(0.002f, 0.5f)] private float markerRadius = 0.02f;
        [Tooltip("圆环分段数。")]
        [SerializeField, Range(8, 96)] private int markerSegments = 32;
        [Tooltip("命中点圆环半径是否随命中距离缩放（远处更大，便于观察）。")]
        [SerializeField] private bool markerScaleByDistance = true;

        [Header("HUD（默认开）")]
        [Tooltip("显示命中对象名 + 世界坐标 + 屏幕像素的轻量 HUD。")]
        [SerializeField] private bool showHud = true;
        [Tooltip("HUD 字号。")]
        [SerializeField, Range(8, 48)] private int hudFontSize = 16;
        [Tooltip("HUD 左下角偏移（像素）。")]
        [SerializeField] private Vector2 hudOffset = new Vector2(24f, 24f);
        [Tooltip("HUD 文本颜色（命中 UI 时自动换成 UI 色）。")]
        [SerializeField] private Color hudColor = new Color(0.9f, 0.95f, 1f, 1f);

        private LineRenderer _line;
        private LineRenderer _marker;
        private Material _lineMaterial;
        private Material _markerMaterial;
        private bool _supportsVertexColor = true;
        private bool _visualsCreated;
        private bool _hudAttempted;
        private bool _hudCreated;
        private Canvas _hudCanvas;
        private TMP_Text _hudText;

        private Vector2? _pointerOverride;
        private bool? _pressedOverride;

        private Vector2 _lastScreenPosition;
        private bool _overUi;
        private bool _hasHit;
        private RaycastHit _lastHit;
        private bool _worldRayActive;
        private Color _currentColor = Color.white;

        private TwinPointerHitInfo _lastInfo;
        private bool _hasLastInfo;

        public bool EnableRay
        {
            get { return enableRay; }
            set
            {
                enableRay = value;
                if (!value)
                {
                    HideAllVisuals();
                }
            }
        }

        /// <summary>P1b-2 只读查询：当前是否有更高优先级输入所有者占用（不改变命中逻辑）。</summary>
        public bool IsBlockedByHigherPriorityInput => !TwinUiInput.AllowsSceneRay;

        /// <summary>P1b-2 只读查询：当前输入所有者（无仲裁器时为 None）。</summary>
        public TwinUiInputOwner InputOwner => TwinUiInput.CurrentOwner;

        public bool ShowHud => showHud;
        public bool DrawWorldRay => drawWorldRay;
        public LayerMask HitLayers => hitLayers;
        public Camera RayCamera => rayCamera;
        public bool HudCreated => _hudCreated;
        public bool VisualsCreated => _visualsCreated;
        public bool WorldRayActive => _worldRayActive;
        public bool HasWorldHit => _hasHit;
        public bool IsPointerOverUi => _overUi;
        public Vector2 PointerScreenPosition => _lastScreenPosition;
        public Color CurrentLineColor => _currentColor;
        public Color MissColor => missColor;
        public Color HitObjectColor => hitObjectColor;
        public Color HitUiColor => hitUiColor;
        public Color PressedColor => pressedColor;
        public float MaxDistance => maxDistance;
        public float StartOffset => startOffset;
        public bool AlwaysOnTop => alwaysOnTop;
        public RaycastHit CurrentHit => _lastHit;
        public int LinePositionCount => _line == null ? 0 : _line.positionCount;
        public bool MarkerVisible => _marker != null && _marker.enabled;
        public string HudTextValue => _hudText == null ? string.Empty : _hudText.text;

        public event Action<TwinPointerHitInfo> HitChanged;

        public bool TryGetHit(out RaycastHit hit, out bool overUi)
        {
            hit = _lastHit;
            overUi = _overUi;
            return !_overUi && _hasHit;
        }

        public Vector3 GetLinePosition(int index)
        {
            return _line == null ? Vector3.zero : _line.GetPosition(index);
        }

        private void Awake()
        {
            EnsureResolved();
            EnsureVisuals();
        }

        private void OnEnable()
        {
            EnsureResolved();
            EnsureVisuals();
            RefreshNow();
        }

        private void Update()
        {
            if (!enableRay)
            {
                HideAllVisuals();
                return;
            }

            Vector2 position = _pointerOverride ?? ReadPointerPosition();
            bool pressed = _pressedOverride ?? ReadPointerPressed();
            RefreshAt(position, pressed);
        }

        /// <summary>立即按当前指针（真实或注入）刷新一次，供同步自检/截图使用。</summary>
        public void RefreshNow()
        {
            if (!enableRay)
            {
                HideAllVisuals();
                return;
            }

            Vector2 position = _pointerOverride ?? ReadPointerPosition();
            bool pressed = _pressedOverride ?? ReadPointerPressed();
            RefreshAt(position, pressed);
        }

        /// <summary>测试注入：固定指针屏幕坐标（传 null 恢复真实鼠标）。</summary>
        public void SetPointerOverride(Vector2? screenPosition)
        {
            _pointerOverride = screenPosition;
        }

        /// <summary>测试注入：固定左键按下状态（传 null 恢复真实鼠标）。</summary>
        public void SetPressedOverride(bool? pressed)
        {
            _pressedOverride = pressed;
        }

        public void ClearOverrides()
        {
            _pointerOverride = null;
            _pressedOverride = null;
        }

        /// <summary>编辑器构建器调用：绑定相机/命中层/HUD 开关。</summary>
        public void ConfigureForEditor(Camera camera, LayerMask layers, bool hudEnabled)
        {
            rayCamera = camera;
            hitLayers = layers;
            showHud = hudEnabled;
            _hudAttempted = false;
        }

        /// <summary>按给定屏幕坐标与按下状态刷新射线（自检逐点采样用）。</summary>
        public void RefreshAt(Vector2 screenPosition, bool pressed)
        {
            if (!enableRay)
            {
                HideAllVisuals();
                return;
            }

            EnsureResolved();
            EnsureVisuals();
            _lastScreenPosition = screenPosition;

            if (rayCamera == null || _line == null)
            {
                HideAllVisuals();
                return;
            }

            bool overUi = IsPointerOverUiAt(screenPosition);
            if (overUi)
            {
                _overUi = true;
                _hasHit = false;
                _worldRayActive = false;
                _currentColor = hitUiColor;
                HideRayAndMarker();
                UpdateHud(screenPosition, overUi, false, default);
                NotifyIfChanged(screenPosition, false, true, default);
                return;
            }

            _overUi = false;
            _worldRayActive = true;

            Vector3 origin = rayCamera.transform.position + rayCamera.transform.forward * startOffset;
            Ray ray = rayCamera.ScreenPointToRay(screenPosition);
            RaycastHit hitInfo;
            bool hit = Physics.Raycast(ray, out hitInfo, maxDistance, hitLayers.value, QueryTriggerInteraction.Ignore);

            Vector3 end = hit ? hitInfo.point : ray.origin + ray.direction * maxDistance;
            Color color = hit ? (pressed ? pressedColor : hitObjectColor) : missColor;

            _hasHit = hit;
            _lastHit = hit ? hitInfo : default;
            _currentColor = color;

            if (drawWorldRay)
            {
                _line.enabled = true;
                _line.SetPosition(0, origin);
                _line.SetPosition(1, end);
                ApplyColor(color);
            }
            else
            {
                _line.enabled = false;
            }

            UpdateMarker(hit, end, hitInfo, ray.direction);
            UpdateHud(screenPosition, overUi, hit, hitInfo);
            NotifyIfChanged(screenPosition, hit, overUi, hitInfo);
        }

        /// <summary>EventSystem 判定指针是否在 UI 上；注入坐标时用同坐标做 UI 射线。</summary>
        public bool IsPointerOverUiAt(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            if (_pointerOverride == null)
            {
                // 真实鼠标：直接用 EventSystem 的指针状态（与 uGUI 点击判定完全一致）。
                return EventSystem.current.IsPointerOverGameObject();
            }

            var data = new PointerEventData(EventSystem.current) { position = screenPosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void NotifyIfChanged(Vector2 screenPosition, bool hit, bool overUi, RaycastHit hitInfo)
        {
            var info = new TwinPointerHitInfo
            {
                HasWorldHit = hit,
                OverUi = overUi,
                Hit = hitInfo,
                ScreenPosition = screenPosition
            };

            bool changed = !_hasLastInfo
                || _lastInfo.HasWorldHit != info.HasWorldHit
                || _lastInfo.OverUi != info.OverUi
                || (!hit && _lastInfo.HasWorldHit)
                || (hit && _lastInfo.HasWorldHit && _lastInfo.Hit.collider != info.Hit.collider);

            _lastInfo = info;
            _hasLastInfo = true;

            if (changed)
            {
                Action<TwinPointerHitInfo> handler = HitChanged;
                if (handler != null)
                {
                    handler(info);
                }
            }
        }

        private void EnsureResolved()
        {
            if (rayCamera == null)
            {
                rayCamera = GetComponent<Camera>();
            }

            if (rayCamera == null)
            {
                rayCamera = Camera.main;
            }
        }

        private void EnsureVisuals()
        {
            if (_visualsCreated)
            {
                return;
            }

            _visualsCreated = true;
            bool vertexColor;
            Shader shader = FindOverlayShader(out vertexColor);
            _supportsVertexColor = vertexColor;

            _lineMaterial = CreateMaterial(shader, Color.white, alwaysOnTop);
            _markerMaterial = CreateMaterial(shader, Color.white, alwaysOnTop);

            _line = CreateLine("TwinPlayRayLine", _lineMaterial, false);
            _line.startWidth = startWidth;
            _line.endWidth = endWidth;
            _line.positionCount = 2;

            _marker = CreateLine("TwinPlayRayHitRing", _markerMaterial, true);
            _marker.startWidth = Mathf.Max(0.004f, markerRadius * 0.15f);
            _marker.endWidth = _marker.startWidth;
            _marker.positionCount = markerSegments;
            _marker.enabled = false;

            if (showHud)
            {
                EnsureHud();
            }
        }

        private LineRenderer CreateLine(string name, Material material, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = false;
            line.loop = loop;
            line.enabled = true;
            return line;
        }

        private void EnsureHud()
        {
            if (_hudAttempted)
            {
                return;
            }

            _hudAttempted = true;

            bool hasEventSystem = EventSystem.current != null || FindObjectOfType<EventSystem>() != null;
            bool hasTmp = TMP_Settings.instance != null;
            if (!hasEventSystem || !hasTmp)
            {
                Debug.LogWarning("[PLAY-RAY] HUD skipped: hasEventSystem=" + hasEventSystem + " hasTmp=" + hasTmp);
                return;
            }

            try
            {
                var canvasGo = new GameObject("TwinPlayRayHudCanvas", typeof(Canvas));
                canvasGo.transform.SetParent(transform, false);
                _hudCanvas = canvasGo.GetComponent<Canvas>();
                _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _hudCanvas.sortingOrder = 32000;

                var textGo = new GameObject("TwinPlayRayHudText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(canvasGo.transform, false);
                var rt = textGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                rt.anchoredPosition = hudOffset;
                rt.sizeDelta = new Vector2(1100f, 120f);

                _hudText = textGo.GetComponent<TMP_Text>();
                _hudText.font = DigitalTwin.UI.TwinUIStyle.GetFont();
                _hudText.fontSize = hudFontSize;
                _hudText.color = hudColor;
                _hudText.alignment = TextAlignmentOptions.BottomLeft;
                _hudText.raycastTarget = false;
                _hudText.text = "射线 HUD 初始化…";
                _hudCreated = true;
            }
            catch (Exception e)
            {
                _hudCreated = false;
                Debug.LogWarning("[PLAY-RAY] HUD create failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void UpdateHud(Vector2 screenPosition, bool overUi, bool hit, RaycastHit hitInfo)
        {
            if (!_hudCreated || _hudText == null)
            {
                return;
            }

            if (overUi)
            {
                _hudText.color = hitUiColor;
                _hudText.text = "UI 命中（世界射线已关闭）  PX=(" + Mathf.RoundToInt(screenPosition.x) + ","
                    + Mathf.RoundToInt(screenPosition.y) + ")";
                return;
            }

            _hudText.color = hudColor;
            if (hit)
            {
                Vector3 p = hitInfo.point;
                _hudText.text = "命中: " + hitInfo.collider.name
                    + "  W=(" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2") + ")"
                    + "  PX=(" + Mathf.RoundToInt(screenPosition.x) + "," + Mathf.RoundToInt(screenPosition.y) + ")"
                    + "  L=" + hitInfo.collider.gameObject.layer;
            }
            else
            {
                _hudText.text = "未命中（天空/超距）  PX=(" + Mathf.RoundToInt(screenPosition.x) + ","
                    + Mathf.RoundToInt(screenPosition.y) + ")";
            }
        }

        private void UpdateMarker(bool hit, Vector3 end, RaycastHit hitInfo, Vector3 rayDirection)
        {
            if (_marker == null)
            {
                return;
            }

            if (!hit || !showHitMarker)
            {
                _marker.enabled = false;
                return;
            }

            _marker.enabled = true;
            float radius = markerRadius;
            if (markerScaleByDistance)
            {
                radius = Mathf.Clamp(hitInfo.distance * 0.012f, markerRadius * 0.5f, markerRadius * 6f);
            }

            Vector3 forward = rayDirection.sqrMagnitude < 0.0001f ? Vector3.forward : rayDirection.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;
            for (int i = 0; i < markerSegments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / markerSegments;
                Vector3 offset = right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius);
                _marker.SetPosition(i, end + offset);
            }

            ApplyColor(_currentColor);
        }

        private void ApplyColor(Color color)
        {
            if (_line != null)
            {
                _line.startColor = color;
                _line.endColor = color;
            }

            if (_marker != null)
            {
                _marker.startColor = color;
                _marker.endColor = color;
            }

            Color materialColor = _supportsVertexColor ? Color.white : color;
            SetMaterialColor(_lineMaterial, materialColor);
            SetMaterialColor(_markerMaterial, materialColor);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private void HideRayAndMarker()
        {
            if (_line != null)
            {
                _line.enabled = false;
            }

            if (_marker != null)
            {
                _marker.enabled = false;
            }

            ApplyColor(_currentColor);
        }

        private void HideAllVisuals()
        {
            if (_line != null)
            {
                _line.enabled = false;
            }

            if (_marker != null)
            {
                _marker.enabled = false;
            }

            _worldRayActive = false;
        }

        private static Shader FindOverlayShader(out bool supportsVertexColor)
        {
            string[] candidates =
            {
                "Hidden/Internal-Colored",
                "Universal Render Pipeline/Unlit",
                "Sprites/Default",
                "Unlit/Color",
                "UI/Default"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                Shader shader = Shader.Find(candidates[i]);
                if (shader != null)
                {
                    supportsVertexColor = candidates[i] != "Universal Render Pipeline/Unlit"
                        && candidates[i] != "Unlit/Color";
                    return shader;
                }
            }

            supportsVertexColor = false;
            return Shader.Find("Unlit/Color");
        }

        private static Material CreateMaterial(Shader shader, Color color, bool alwaysOnTop)
        {
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_ZTest"))
            {
                material.SetInt("_ZTest", alwaysOnTop
                    ? (int)CompareFunction.Always
                    : (int)CompareFunction.LessEqual);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", 0);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.renderQueue = alwaysOnTop ? 4004 : 3000;
            return material;
        }

        private Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                return mouse.position.ReadValue();
            }

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
            return Input.mousePosition;
#endif
        }

        private bool ReadPointerPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private void OnDestroy()
        {
            if (_hudCanvas != null)
            {
                Destroy(_hudCanvas.gameObject);
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }

            if (_markerMaterial != null)
            {
                Destroy(_markerMaterial);
            }
        }
    }
}