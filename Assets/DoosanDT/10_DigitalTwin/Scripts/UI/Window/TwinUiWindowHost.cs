// P2-1 主窗窗口化：窗口宿主（布局 + 缩放适配 + 持久化）。
//
// 设计决定：P2 先按"1920×1080 设计坐标 + 等比缩进窗口"实现拖动/缩放；P3 起取消整体缩放，
// 6 个直属容器搬进 "UiWindowContent"（pivot 左上），内容尺寸 = 窗口尺寸，由
// TwinUiResponsiveLayout 按三档断点重排（Wide≥1500 / Medium 1100–1499 / Narrow<1100）。
// 这样文字保持 1:1（不再被 0.68 缩放变小），窄窗靠重排而不是缩小。
//
// 只在 Play 下重组（Awake 中执行，Edit 模式不改场景层级）；找不到容器时 LogWarning 并安全退出，
// 不修改任何场景对象。回退方式：禁用本组件（或恢复场景备份）即可回到全屏 UI。
using System;
using DigitalTwin.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>窗口缩放方向位（可组合）：右下角 = Right | Bottom。</summary>
    [Flags]
    public enum TwinUiWindowEdge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right
    }

    /// <summary>持久化数据结构：窗口矩形按画布尺寸归一化，换分辨率仍可用。</summary>
    [Serializable]
    public struct TwinUiWindowLayout
    {
        public float x;
        public float y;
        public float w;
        public float h;
        public bool maximized;
    }

    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class TwinUiWindowHost : MonoBehaviour, ITwinUiFrameHost
    {
        public const string LayoutPrefKey = "TwinUI.Window.Layout.v1";
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const float DefaultWidthRatio = 0.55f;
        public const float DefaultHeightRatio = 0.92f;
        public const float DefaultMargin = 16f;
        public const float MinWidth = 1080f;
        public const float MinHeight = 620f;

        // P3 起内容 1:1 渲染，不再存在"最小缩放"；保留常量仅为旧调用方/回归兼容（UI/30 已同步改断言）。
        public const float MinContentScale = 1.00f;

        /// <summary>要搬进 UiWindowContent 的 Canvas 直属容器（顺序固定，便于自检）。</summary>
        public static readonly string[] ContainerNames =
        {
            "Header", "TabBar", "ContextBar", "MissionRoot", "IndustrialRoot", "TwinOpsRoot"
        };

        [Header("窗口化开关（关闭后本组件完全不动层级，等价全屏 UI）")]
        [SerializeField] private bool enableWindowing = true;

        [Header("是否恢复上次保存的窗口布局（PlayerPrefs）")]
        [SerializeField] private bool restoreSavedLayout = true;

        [Header("可选：显式指定 Canvas（为空时从自身/父级取）")]
        [SerializeField] private Canvas canvasOverride;

        [Header("可选：交互参数模板（UI/29 挂在 Canvas 上的那个帧组件）")]
        [SerializeField] private TwinUiWindowFrame frameTemplate;

        [Header("窗口外区域深海底色（全屏半透明 Image，raycastTarget=false，不挡射线/UI 命中）")]
        [SerializeField] private bool sceneTint = true;

        [Header("P2.5 运行时主题启动引导：Play 时按此主题渲染（UI/31 写 Ocean，UI/32 写回 Dark；旧场景默认 Dark 不变）")]
        [SerializeField] private TwinUIThemeMode bootTheme = TwinUIThemeMode.Dark;

        /// <summary>UI/31 / UI/32 写场景级启动主题；Play 首帧按它设置静态主题。</summary>
        public void SetBootTheme(TwinUIThemeMode mode)
        {
            bootTheme = mode;
        }

        public TwinUIThemeMode BootTheme => bootTheme;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private RectTransform _content;
        private TwinUiWindowFrame _frame;
        private Image _background;
        private Image _backdrop;
        private bool _ready;
        private Rect _rect;
        private Rect _restoreRect;
        private bool _maximized;
        private Vector2 _canvasSize;
        private Vector3 _contentScale = Vector3.one;
        private int _movedContainers;
        private bool _pendingBootstrap;
        private int _bootstrapAttempts;
        private Image _sceneTint;
        private TwinUiResponsiveLayout _responsive;
        private static Sprite _roundedSprite;

        /// <summary>布局或尺寸变化时触发（编辑器自检/调试用）。</summary>
        public event Action<TwinUiWindowHost> LayoutChanged;

        public bool IsReady => _ready;
        public bool AllowsDrag => true;
        public bool AllowsMaximize => true;

        /// <summary>Frame 回调：拖拽开始（主窗当前无额外动作，保留给活动窗强调/日志）。</summary>
        public void OnFrameDragBegin()
        {
        }

        /// <summary>Frame 回调：拖拽结束（持久化由 Frame 随后调 SaveLayout 完成）。</summary>
        public void OnFrameDragEnd()
        {
        }

        /// <summary>Frame 回调：缩放结束。</summary>
        public void OnFrameResizeEnd()
        {
        }

        public RectTransform Root => _root;
        public RectTransform Content => _content;
        public TwinUiWindowFrame Frame => _frame;
        public Canvas Canvas => _canvas;
        public RectTransform CanvasRect => _canvasRect;
        public Vector2 CanvasSize => _canvasSize;
        public Rect WindowRect => _rect;
        public bool IsMaximized => _maximized;
        public Vector3 ContentScale3 => _contentScale;
        public Vector2 ContentScale => new Vector2(_contentScale.x, _contentScale.y);
        public int MovedContainers => _movedContainers;
        public bool WindowingEnabled => enableWindowing;
        public TwinUiResponsiveLayout Responsive => _responsive;

        // ------------------------------------------------------------- 生命周期

        private void Awake()
        {
            if (!Application.isPlaying || !enableWindowing)
            {
                return;
            }

            // 域重载后静态主题回到 Dark；按场景里保存的启动主题恢复，保证换肤后的场景进 Play 就是对应主题。
            if (TwinUITheme.Mode != bootTheme)
            {
                TwinUITheme.SetMode(bootTheme);
                Debug.Log("[THEME] host boot mode=" + bootTheme);
            }

            Bootstrap();
        }

        private void Update()
        {
            if (!_ready && _pendingBootstrap)
            {
                // Canvas 尺寸在首帧由 CanvasScaler 计算，Awake 时可能还没就绪；逐帧重试。
                _bootstrapAttempts++;
                if (_bootstrapAttempts > 180)
                {
                    Debug.LogWarning("[WINDOW] canvas never became valid; windowing disabled (scene untouched).");
                    _pendingBootstrap = false;
                    return;
                }

                Bootstrap();
                return;
            }

            // 画布尺寸变化（Game 视图换分辨率）时按归一化矩形重算，避免窗口跑到画布外。
            if (!_ready || _canvasRect == null)
            {
                return;
            }

            Vector2 size = _canvasRect.rect.size;
            if ((size - _canvasSize).sqrMagnitude <= 1f)
            {
                return;
            }

            _canvasSize = size;
            ApplyLayout(_rect, _maximized, false);
        }

        private void OnDestroy()
        {
            TwinUITheme.Changed -= OnThemeChanged;
            if (_frame != null)
            {
                _frame.ReleaseAll();
            }
        }

        // ------------------------------------------------------------- 启动

        private void Bootstrap()
        {
            _canvas = canvasOverride != null ? canvasOverride : GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            if (_canvas == null)
            {
                Debug.LogWarning("[WINDOW] host could not find Canvas; windowing disabled (scene untouched).");
                return;
            }

            _canvasRect = _canvas.transform as RectTransform;
            if (_canvasRect == null)
            {
                Debug.LogWarning("[WINDOW] Canvas has no RectTransform; windowing disabled.");
                return;
            }

            _canvasSize = _canvasRect.rect.size;
            if (_canvasSize.x < 100f || _canvasSize.y < 100f)
            {
                _pendingBootstrap = true;
                return;
            }

            var containers = new Transform[ContainerNames.Length];
            for (int i = 0; i < ContainerNames.Length; i++)
            {
                containers[i] = _canvasRect.Find(ContainerNames[i]);
                if (containers[i] == null)
                {
                    _pendingBootstrap = false;
                    Debug.LogWarning("[WINDOW] container missing on Canvas: " + ContainerNames[i]
                        + "; windowing disabled (scene untouched).");
                    return;
                }
            }

            if (frameTemplate == null)
            {
                frameTemplate = GetComponent<TwinUiWindowFrame>();
            }

            BuildRootAndContent();

            for (int i = 0; i < containers.Length; i++)
            {
                containers[i].SetParent(_content, false);
                _movedContainers++;
            }

            _frame = _root.gameObject.AddComponent<TwinUiWindowFrame>();
            _frame.Configure(this, frameTemplate);

            // P3 响应式引擎：内容 1:1，按内容宽度三档重排（只改锚点/sizeDelta，不重建层级）。
            _responsive = _root.gameObject.AddComponent<TwinUiResponsiveLayout>();
            _responsive.Configure(this);

            _pendingBootstrap = false;
            _ready = true;
            TwinUITheme.Changed += OnThemeChanged;

            // P6-3：既有卡片/控件圆角（RadiusCard=11 / RadiusControl=8；只改 sprite，不改布局）。
            TwinUiRadii.Apply(_content);
            Debug.Log("[RADIUS] apply content scanned=" + TwinUiRadii.LastScanned
                + " cards=" + TwinUiRadii.AppliedCards + " controls=" + TwinUiRadii.AppliedControls);
            BuildSceneTint();
            if (_frame != null)
            {
                // 域重载后静态主题可能已被 applier 启动引导切换，这里再对齐一次外壳色。
                _frame.ApplyTheme();
            }

            // 窗口自身底色/边框也按当前令牌重刷（Bootstrap 可能晚于 applier 的换肤时机）。
            _background.color = TwinUIStyle.BorderColor;
            _backdrop.color = TwinUIStyle.LayerColor;
            var applier = GetComponent<TwinUIThemeApplier>();
            Debug.Log("[THEME] bootstrap applier=" + (applier != null) + " enabled="
                + (applier != null && applier.enabled) + " mode=" + TwinUITheme.Mode
                + " bg=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.BorderColor) + " backdrop=" + ColorUtility.ToHtmlStringRGBA(TwinUIStyle.LayerColor));

            Rect target = DefaultRect();
            bool maximized = false;
            TwinUiWindowLayout saved;
            if (restoreSavedLayout && TryReadLayout(out saved))
            {
                target = FromNormalized(saved);
                maximized = saved.maximized;
            }

            ApplyLayout(target, maximized, false);
            Debug.Log("[WINDOW] ready canvas=" + _canvasSize.x.ToString("F0") + "x" + _canvasSize.y.ToString("F0")
                + " containers=" + _movedContainers
                + " rect=" + DescribeRect(_rect) + " scale=" + _contentScale.x.ToString("F3")
                + " maximized=" + _maximized
                + " savedLayout=" + PlayerPrefs.HasKey(LayoutPrefKey)
                + " theme=" + TwinUITheme.Mode);
        }

        private void BuildRootAndContent()
        {
            var rootGo = new GameObject("UiWindowRoot", typeof(RectTransform));
            _root = rootGo.GetComponent<RectTransform>();
            _root.SetParent(_canvasRect, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.zero;
            _root.pivot = Vector2.zero;
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(_canvasSize.x, _canvasSize.y);
            _root.SetAsLastSibling();

            // 背景（1px 边框色）+ 内衬（面板底色，内缩 1px 形成 1px 边框）+ 圆角 9-slice + 裁切
            Sprite rounded = RoundedWindowSprite();
            _background = TwinUIStyle.Panel(_root, "WindowBackground", TwinUIStyle.BorderColor, true);
            _background.sprite = rounded;
            _background.type = Image.Type.Sliced;
            _background.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_background.rectTransform, 0f, 0f, 0f, 0f);

            _backdrop = TwinUIStyle.Panel(_root, "WindowBackdrop", TwinUIStyle.LayerColor, false);
            _backdrop.sprite = rounded;
            _backdrop.type = Image.Type.Sliced;
            _backdrop.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_backdrop.rectTransform, 1f, 1f, 1f, 1f);

            var mask = _root.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            var contentGo = new GameObject("UiWindowContent", typeof(RectTransform));
            _content = contentGo.GetComponent<RectTransform>();
            _content.SetParent(_root, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(DesignWidth, DesignHeight);
            _content.localScale = Vector3.one;
            _content.SetAsLastSibling();
        }

        /// <summary>窗口外深海底色层：Canvas 的第一个子节点（在窗口之下、3D 之上），不参与射线命中。</summary>
        private void BuildSceneTint()
        {
            if (!sceneTint || _canvasRect == null || _canvasRect.Find("UiSceneTint") != null)
            {
                return;
            }

            _sceneTint = TwinUIStyle.Panel(_canvasRect, "UiSceneTint", SceneTintColor());
            _sceneTint.raycastTarget = false;
            _sceneTint.sprite = RoundedWindowSprite();
            _sceneTint.type = Image.Type.Sliced;
            _sceneTint.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_sceneTint.rectTransform, 0f, 0f, 0f, 0f);
            _sceneTint.rectTransform.SetAsFirstSibling();
            var mask = _sceneTint.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
        }

        private static Color SceneTintColor()
        {
            // 深海底色压暗 3D 背景；alpha 低到不遮住工位细节。raycastTarget=false，不影响命中。
            Color c = TwinUIStyle.BgColor;
            c.a = 0.30f;
            return c;
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            if (_sceneTint != null)
            {
                _sceneTint.color = SceneTintColor();
            }

            if (_frame != null)
            {
                _frame.ApplyTheme();
            }
        }

        /// <summary>换肤后手动刷新窗口外壳/底色（菜单 UI/31 或 UI/33 用）。</summary>
        public void RefreshTheme()
        {
            OnThemeChanged(TwinUITheme.Mode);
        }

        // ------------------------------------------------------------- 布局

        /// <summary>画布设计坐标下的默认窗口矩形：右停靠、宽 68%、高 92%、右边距 16、垂直居中。</summary>
        public Rect DefaultRect()
        {
            float canvasW = Mathf.Max(1f, _canvasSize.x);
            float canvasH = Mathf.Max(1f, _canvasSize.y);
            float w = Mathf.Min(canvasW, Mathf.Max(MinWidth, canvasW * DefaultWidthRatio));
            float h = Mathf.Min(canvasH, Mathf.Max(MinHeight, canvasH * DefaultHeightRatio));
            float x = Mathf.Max(0f, canvasW - w - DefaultMargin);
            float y = Mathf.Max(0f, (canvasH - h) * 0.5f);
            return new Rect(x, y, w, h);
        }

        /// <summary>P3 起内容 1:1 渲染，缩放恒为 1（保留方法给旧调用方；响应式重排见 TwinUiResponsiveLayout）。</summary>
        public float ContentScaleFor(Rect rect)
        {
            return 1f;
        }

        /// <summary>自检专用：强制把窗口设为指定宽度（不受最小宽度钳制，用于 Narrow 档 1000px 用例），保持右停靠。</summary>
        public void SetWidthForCheck(float width)
        {
            if (!_ready || _root == null)
            {
                return;
            }

            float w = Mathf.Clamp(width, 640f, _canvasSize.x);
            float h = Mathf.Clamp(_rect.height, 200f, _canvasSize.y);
            float x = Mathf.Max(0f, _canvasSize.x - w - DefaultMargin);
            float y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, _canvasSize.y - h));
            ApplyLayoutCore(new Rect(x, y, w, h), false, false);
        }

        public Rect ClampRect(Rect rect)
        {
            float canvasW = Mathf.Max(1f, _canvasSize.x);
            float canvasH = Mathf.Max(1f, _canvasSize.y);
            float minW = Mathf.Min(MinWidth, canvasW);
            float minH = Mathf.Min(MinHeight, canvasH);
            float w = Mathf.Clamp(rect.width, minW, canvasW);
            float h = Mathf.Clamp(rect.height, minH, canvasH);
            float x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, canvasW - w));
            float y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, canvasH - h));
            return new Rect(x, y, w, h);
        }

        /// <summary>应用窗口矩形（画布坐标，左下角原点）。saveLayout=false 用于拖动/缩放过程中的实时布局。</summary>
        public void ApplyLayout(Rect rect, bool maximized, bool saveLayout = true)
        {
            if (!_ready || _root == null)
            {
                return;
            }

            if (maximized)
            {
                rect = new Rect(0f, 0f, _canvasSize.x, _canvasSize.y);
            }

            ApplyLayoutCore(ClampRect(rect), maximized, saveLayout);
        }

        /// <summary>内核：不做最小尺寸钳制（仅供已校验的调用方/自检使用）。</summary>
        private void ApplyLayoutCore(Rect rect, bool maximized, bool saveLayout)
        {
            _rect = rect;
            _maximized = maximized;
            _root.anchoredPosition = new Vector2(rect.x, rect.y);
            _root.sizeDelta = new Vector2(rect.width, rect.height);

            // P3：取消整体等比缩放（文字不再变小），内容尺寸 = 窗口尺寸，交给响应式引擎重排。
            float innerW = Mathf.Max(320f, rect.width - 2f);
            float innerH = Mathf.Max(200f, rect.height - 2f);
            _contentScale = Vector3.one;
            _content.localScale = Vector3.one;
            _content.sizeDelta = new Vector2(innerW, innerH);

            if (_responsive != null)
            {
                _responsive.Apply(innerW, innerH);
            }

            if (_frame != null)
            {
                _frame.OnHostLayout(rect, 1f);
            }

            if (saveLayout)
            {
                SaveLayout();
            }

            var handler = LayoutChanged;
            if (handler != null)
            {
                handler(this);
            }
        }

        /// <summary>拖动窗口：返回实际位移（被画布边界钳制后可能小于请求值）。</summary>
        public Vector2 MoveBy(Vector2 delta)
        {
            if (!_ready || _maximized || delta == Vector2.zero)
            {
                return Vector2.zero;
            }

            Rect moved = _rect;
            moved.x += delta.x;
            moved.y += delta.y;
            Rect clamped = ClampRect(moved);
            Vector2 actual = new Vector2(clamped.x - _rect.x, clamped.y - _rect.y);
            if (actual != Vector2.zero)
            {
                ApplyLayout(clamped, false, false);
            }

            return actual;
        }

        /// <summary>8 向缩放：返回实际尺寸变化（被最小尺寸/画布边界钳制后可能小于请求值）。</summary>
        public Vector2 ResizeBy(TwinUiWindowEdge edge, Vector2 delta)
        {
            if (!_ready || edge == TwinUiWindowEdge.None || delta == Vector2.zero)
            {
                return Vector2.zero;
            }

            if (_maximized)
            {
                // 最大化状态下拖动边缘 = 先退出最大化（以当前全幅矩形为基准），符合窗口习惯。
                _maximized = false;
                if (_frame != null)
                {
                    _frame.RefreshMaxButton();
                }
            }

            Rect rect = _rect;
            if ((edge & TwinUiWindowEdge.Right) != 0)
            {
                rect.width += delta.x;
            }

            if ((edge & TwinUiWindowEdge.Left) != 0)
            {
                rect.x += delta.x;
                rect.width -= delta.x;
            }

            if ((edge & TwinUiWindowEdge.Top) != 0)
            {
                rect.height += delta.y;
            }

            if ((edge & TwinUiWindowEdge.Bottom) != 0)
            {
                rect.y += delta.y;
                rect.height -= delta.y;
            }

            // 最小尺寸钳制：保持"另一条边"不动。
            if (rect.width < MinWidth)
            {
                if ((edge & TwinUiWindowEdge.Left) != 0)
                {
                    rect.x = _rect.x + _rect.width - MinWidth;
                }

                rect.width = MinWidth;
            }

            if (rect.height < MinHeight)
            {
                if ((edge & TwinUiWindowEdge.Bottom) != 0)
                {
                    rect.y = _rect.y + _rect.height - MinHeight;
                }

                rect.height = MinHeight;
            }

            Rect clamped = ClampRect(rect);
            Vector2 actual = new Vector2(clamped.width - _rect.width, clamped.height - _rect.height);
            if (clamped != _rect)
            {
                ApplyLayout(clamped, false, false);
            }

            return actual;
        }

        public void SetMaximized(bool value)
        {
            if (!_ready)
            {
                return;
            }

            if (value == _maximized)
            {
                return;
            }

            if (value)
            {
                _restoreRect = _rect;
                ApplyLayout(new Rect(0f, 0f, _canvasSize.x, _canvasSize.y), true);
            }
            else
            {
                Rect restore = _restoreRect.width > 1f && _restoreRect.height > 1f ? _restoreRect : DefaultRect();
                ApplyLayout(restore, false);
            }

            if (_frame != null)
            {
                _frame.RefreshMaxButton();
            }
        }

        public void ToggleMaximized()
        {
            SetMaximized(!_maximized);
        }

        // ------------------------------------------------------------- 持久化

        public void SaveLayout()
        {
            if (!_ready)
            {
                return;
            }

            var data = new TwinUiWindowLayout
            {
                x = _rect.x / Mathf.Max(1f, _canvasSize.x),
                y = _rect.y / Mathf.Max(1f, _canvasSize.y),
                w = _rect.width / Mathf.Max(1f, _canvasSize.x),
                h = _rect.height / Mathf.Max(1f, _canvasSize.y),
                maximized = _maximized
            };
            PlayerPrefs.SetString(LayoutPrefKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public bool TryReadLayout(out TwinUiWindowLayout data)
        {
            data = default;
            string json = PlayerPrefs.GetString(LayoutPrefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<TwinUiWindowLayout>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WINDOW] saved layout is not valid JSON (" + e.GetType().Name
                    + "); falling back to default layout.");
                return false;
            }

            if (data.w <= 0.01f || data.h <= 0.01f || data.w > 4f || data.h > 4f)
            {
                Debug.LogWarning("[WINDOW] saved layout out of range; falling back to default layout.");
                return false;
            }

            return true;
        }

        public Rect FromNormalized(TwinUiWindowLayout data)
        {
            return new Rect(data.x * _canvasSize.x, data.y * _canvasSize.y,
                data.w * _canvasSize.x, data.h * _canvasSize.y);
        }

        /// <summary>重新读取 PlayerPrefs 并应用（自检用）。</summary>
        public bool ReloadLayoutFromPrefs()
        {
            TwinUiWindowLayout data;
            if (!TryReadLayout(out data))
            {
                return false;
            }

            ApplyLayout(FromNormalized(data), data.maximized, false);
            return true;
        }

        /// <summary>删除保存的布局并恢复默认矩形。</summary>
        public void ResetLayout()
        {
            if (!_ready)
            {
                return;
            }

            PlayerPrefs.DeleteKey(LayoutPrefKey);
            PlayerPrefs.Save();
            _restoreRect = Rect.zero;
            ApplyLayout(DefaultRect(), false, false);
            Debug.Log("[WINDOW] layout reset rect=" + DescribeRect(_rect));
        }

        /// <summary>删除保存的布局但不改变当前窗口（自检收尾用）。</summary>
        public static void ClearSavedLayout()
        {
            PlayerPrefs.DeleteKey(LayoutPrefKey);
            PlayerPrefs.Save();
        }

        public string DescribeRect(Rect rect)
        {
            return "(" + rect.x.ToString("F0") + "," + rect.y.ToString("F0") + ","
                + rect.width.ToString("F0") + "x" + rect.height.ToString("F0") + ")";
        }

        public string DescribeState()
        {
            return "rect=" + DescribeRect(_rect)
                + " canvas=" + _canvasSize.x.ToString("F0") + "x" + _canvasSize.y.ToString("F0")
                + " scale=" + _contentScale.x.ToString("F3")
                + " maximized=" + _maximized
                + " containers=" + _movedContainers;
        }

        // ------------------------------------------------------------- 圆角精灵

        /// <summary>64×64 白色圆角矩形 9-slice 精灵（运行时生成一次，主题色由 Image.color 决定）。</summary>
        public static Sprite RoundedWindowSprite()
        {
            if (_roundedSprite != null)
            {
                return _roundedSprite;
            }

            const int size = 64;
            int radius = Mathf.Clamp(Mathf.RoundToInt(TwinUIStyle.RadiusWindow), 2, 32); // 圆角来自 TwinUIStyle.RadiusWindow 令牌（V3 窗口 13px）
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TwinUiWindowRounded",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float dx = 0f;
                    float dy = 0f;
                    if (px < radius)
                    {
                        dx = radius - px;
                    }
                    else if (px > size - radius)
                    {
                        dx = px - (size - radius);
                    }

                    if (py < radius)
                    {
                        dy = radius - py;
                    }
                    else if (py > size - radius)
                    {
                        dy = py - (size - radius);
                    }

                    bool inside = dx * dx + dy * dy <= radius * radius;
                    pixels[y * size + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _roundedSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _roundedSprite.name = "TwinUiWindowRounded";
            _roundedSprite.hideFlags = HideFlags.HideAndDontSave;
            return _roundedSprite;
        }
    }
}