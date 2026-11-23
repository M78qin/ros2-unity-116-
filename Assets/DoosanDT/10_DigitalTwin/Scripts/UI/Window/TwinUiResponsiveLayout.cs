// P3 响应式重排引擎：窗口内容 1:1 布局（取消整体 scale-to-fit），按内容宽度三档重排。
//
// 档位：Wide ≥ 1500 / Medium 1100–1499 / Narrow < 1100（宽度 = UiWindowContent 的宽度）。
// 设计原则（task-11）：
//   * 只用锚点 / sizeDelta / anchoredPosition 重算，不重建 GameObject、不改层级；
//   * 同一个宽度重复 Apply 结果一致（幂等）；Wide↔Medium↔Narrow 来回切换不累积；
//   * 文本字号一律不动（禁止用缩放兜底）。
// 重排范围：
//   Header：Wide 全部 chip 内联（放不下时 Title 收窄，再按优先级折叠次要 chip）；
//           Medium 隐藏 ModeChip/KpiAge/RecBadge；Narrow 只留 Title+KpiLatency+KpiRate+EstopChip。
//   TabBar/ContextBar：Wide 单行；Medium/Narrow 两行（ContextBar 下移一行），chip 按可用宽度等比收窄。
//   MissionRoot：Wide 三列 420 / Detail 自适应 / 552；Medium Steps 320 + Detail + Eval 右侧抽屉；
//           Narrow Steps 300 + Detail，Eval 抽屉叠加在 Detail 上（宽度 = Detail 宽）。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public enum TwinUiBreakpoint
    {
        Wide = 0,
        Medium = 1,
        Narrow = 2,

        /// <summary>P7：内容宽 &lt; 860（窗口可缩到 720），走单列卡片 + 页签换行。</summary>
        Compact = 3
    }

    [DefaultExecutionOrder(-490)]
    [DisallowMultipleComponent]
    public sealed class TwinUiResponsiveLayout : MonoBehaviour
    {
        // ------------------------------------------------------------- 断点常量
        public const float WideMinWidth = 1500f;
        public const float MediumMinWidth = 1100f;

        /// <summary>P7 Compact 档：&lt; 860（窗口最小 720）走单列卡片与换行页签。</summary>
        public const float CompactMinWidth = 860f;

        public const float HeaderHeight = 56f;
        public const float HeaderSideMargin = 16f;
        public const float HeaderTitleWidth = 440f;
        public const float HeaderTitleMinWidth = 180f;
        public const float HeaderClockWidth = 88f;
        public const float HeaderChipGap = 8f;

        public const float TabBarWidth = 552f;
        public const float TabBarRowTop = 72f;      // 相对窗口内容顶部的偏移（正值）
        public const float ControlRowHeight = 44f;
        public const float ContextSecondRowTop = 120f;
        public const float ContentTopSingleRow = 124f;   // 单行控制条时 MissionRoot 的顶部内缩
        public const float ContentTopTwoRows = 176f;     // 两行控制条时
        public const float ContentBottom = 16f;

        public const float StepsWide = 420f;
        public const float StepsMedium = 320f;
        public const float StepsNarrow = 300f;
        public const float EvalDrawerMaxWidth = 552f;
        public const float ColumnGap = 8f;
        public const float MinDetailWidth = 280f;

        public const float DrawerToggleWidth = 104f;
        public const float DrawerToggleHeight = 32f;
        public const float DrawerToggleMargin = 8f;

        // ------------------------------------------------------------- 状态
        private TwinUiWindowHost _host;
        private RectTransform _content;
        private RectTransform _header;
        private RectTransform _title;
        private RectTransform _clock;
        private RectTransform _tabBar;
        private RectTransform _contextBar;
        private RectTransform _missionRoot;
        private RectTransform _industrialRoot;
        private RectTransform _twinOpsRoot;
        private RectTransform _steps;
        private RectTransform _detail;
        private RectTransform _eval;
        private TwinUIButton _drawerToggle;
        private readonly List<RectTransform> _stepRows = new List<RectTransform>();
        private readonly List<RectTransform> _stepTexts = new List<RectTransform>();
        private readonly List<RectTransform> _detailInner = new List<RectTransform>();
        private readonly List<RectTransform> _evalCells = new List<RectTransform>();
        private RectTransform _timelineTitle;
        private RectTransform _timelineState;
        private RectTransform _evalTitle;
        private RectTransform _evalNote;
        private readonly List<RectTransform> _headerChips = new List<RectTransform>();
        private readonly Dictionary<RectTransform, TMP_Text> _fixedHeaderLabels =
            new Dictionary<RectTransform, TMP_Text>();
        private bool _labelsReady;
        private readonly Dictionary<RectTransform, Vector2> _headerChipSizes = new Dictionary<RectTransform, Vector2>();
        private readonly List<RectTransform> _contextItems = new List<RectTransform>();
        private readonly Dictionary<RectTransform, Vector2> _contextItemSizes = new Dictionary<RectTransform, Vector2>();
        private readonly Dictionary<RectTransform, Vector2> _contextItemPositions = new Dictionary<RectTransform, Vector2>();
        private bool _drawerOpen;
        private bool _configured;
        private TwinUiBreakpoint _current = TwinUiBreakpoint.Wide;
        private float _lastWidth;
        private float _lastHeight;
        private int _applyCount;
        private int _drawerToggleCount;

        private static readonly string[] HeaderChipOrder =
        {
            "ModeChip", "KpiSource", "KpiRate", "KpiLatency", "KpiAge", "AlarmBadge", "RecBadge", "EstopChip"
        };

        private static readonly string[] MediumHeaderChips =
        {
            "KpiSource", "KpiRate", "KpiLatency", "AlarmBadge", "EstopChip"
        };

        private static readonly string[] NarrowHeaderChips =
        {
            "KpiLatency", "KpiRate", "EstopChip"
        };

        /// <summary>Wide 放不下时的折叠优先级（越靠前越先折叠）。</summary>
        private static readonly string[] WideFoldOrder = { "ModeChip", "KpiAge", "KpiSource", "RecBadge", "AlarmBadge" };

        private static readonly string[] ContextChipNames =
        {
            "Chip_Run", "Chip_Profile", "Chip_Condition", "Chip_Payload", "Chip_Tcp", "Chip_Session", "Chip_SelfCheck"
        };

        // ------------------------------------------------------------- 对外只读状态
        public TwinUiBreakpoint Current => _current;
        public float LastWidth => _lastWidth;
        public bool DrawerOpen => _drawerOpen;
        public int ApplyCount => _applyCount;
        public int DrawerToggleCount => _drawerToggleCount;
        public RectTransform MissionRoot => _missionRoot;
        public RectTransform StepsRect => _steps;
        public RectTransform DetailRect => _detail;
        public RectTransform EvalRect => _eval;
        public RectTransform DrawerToggleRect => _drawerToggle != null ? _drawerToggle.transform as RectTransform : null;
        public bool Configured => _configured;

        public static TwinUiBreakpoint Classify(float width)
        {
            if (width >= WideMinWidth)
            {
                return TwinUiBreakpoint.Wide;
            }

            if (width >= MediumMinWidth)
            {
                return TwinUiBreakpoint.Medium;
            }

            return width >= CompactMinWidth ? TwinUiBreakpoint.Narrow : TwinUiBreakpoint.Compact;
        }

        // ------------------------------------------------------------- 初始化

        /// <summary>由 TwinUiWindowHost 在 Play 启动时调用一次；只读取/缓存现有层级，不创建业务对象。</summary>
        public void Configure(TwinUiWindowHost host)
        {
            _host = host;
            _content = host != null ? host.Content : null;
            if (_content == null)
            {
                return;
            }

            _header = _content.Find("Header") as RectTransform;
            _tabBar = _content.Find("TabBar") as RectTransform;
            _contextBar = _content.Find("ContextBar") as RectTransform;
            _missionRoot = _content.Find("MissionRoot") as RectTransform;
            _industrialRoot = _content.Find("IndustrialRoot") as RectTransform;
            _twinOpsRoot = _content.Find("TwinOpsRoot") as RectTransform;

            if (_header != null)
            {
                _title = _header.Find("Title") as RectTransform;
                _clock = _header.Find("Clock") as RectTransform;
                for (int i = 0; i < HeaderChipOrder.Length; i++)
                {
                    var chip = _header.Find(HeaderChipOrder[i]) as RectTransform;
                    if (chip != null)
                    {
                        _headerChips.Add(chip);
                        _headerChipSizes[chip] = chip.sizeDelta;
                    }
                }
            }

            if (_contextBar != null)
            {
                for (int i = 0; i < ContextChipNames.Length; i++)
                {
                    var chip = _contextBar.Find(ContextChipNames[i]) as RectTransform;
                    if (chip != null)
                    {
                        _contextItems.Add(chip);
                        _contextItemSizes[chip] = chip.sizeDelta;
                        _contextItemPositions[chip] = chip.anchoredPosition;
                    }
                }

                for (int i = 0; i < 2; i++)
                {
                    var extra = _contextBar.Find(i == 0 ? "SourceTag" : "Watermark") as RectTransform;
                    if (extra != null)
                    {
                        _contextItems.Add(extra);
                        _contextItemSizes[extra] = extra.sizeDelta;
                        _contextItemPositions[extra] = extra.anchoredPosition;
                    }
                }
            }

            if (_missionRoot != null)
            {
                _steps = _missionRoot.Find("Steps") as RectTransform;
                _detail = _missionRoot.Find("Detail") as RectTransform;
                _eval = _missionRoot.Find("Eval") as RectTransform;

                if (_steps != null)
                {
                    for (int i = 0; i < _steps.childCount; i++)
                    {
                        var child = _steps.GetChild(i) as RectTransform;
                        if (child == null || !child.name.StartsWith("Step"))
                        {
                            continue;
                        }

                        _stepRows.Add(child);
                        var t = child.Find("Title") as RectTransform;
                        var s = child.Find("State") as RectTransform;
                        if (t != null)
                        {
                            _stepTexts.Add(t);
                        }

                        if (s != null)
                        {
                            _stepTexts.Add(s);
                        }
                    }
                }

                if (_detail != null)
                {
                    string[] detailNames = { "Title", "Body", "Hint", "Timeline", "Actions" };
                    for (int i = 0; i < detailNames.Length; i++)
                    {
                        var child = _detail.Find(detailNames[i]) as RectTransform;
                        if (child != null)
                        {
                            _detailInner.Add(child);
                        }
                    }

                    var timeline = _detail.Find("Timeline");
                    if (timeline != null)
                    {
                        _timelineTitle = timeline.Find("Title") as RectTransform;
                        _timelineState = timeline.Find("State") as RectTransform;
                    }
                }

                if (_eval != null)
                {
                    _evalTitle = _eval.Find("Title") as RectTransform;
                    _evalNote = _eval.Find("Note") as RectTransform;
                    string[] cellNames = { "Timer", "Progress", "Errors", "Score" };
                    for (int i = 0; i < cellNames.Length; i++)
                    {
                        var cell = _eval.Find(cellNames[i]) as RectTransform;
                        if (cell != null)
                        {
                            _evalCells.Add(cell);
                        }
                    }
                }
            }

            BuildDrawerToggle();
            _configured = _header != null && _missionRoot != null && _steps != null && _detail != null;
            StartCoroutine(EnableHeaderLabelsAfterDelay());
        }

        private System.Collections.IEnumerator EnableHeaderLabelsAfterDelay()
        {
            // 先让 Canvas/CanvasScaler 与 TMP 资源完成首帧初始化，再建显示层（约 2 帧，不再等 0.5s）。
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (TMP_Settings.defaultFontAsset != null)
            {
                TMP_Settings.defaultFontAsset.ReadFontAssetDefinition();
            }

            _labelsReady = true;
            if (_configured)
            {
                Apply(_lastWidth > 1f ? _lastWidth : 1920f, _lastHeight > 1f ? _lastHeight : 1080f);
            }
        }

        /// <summary>抽屉开关按钮：仅 Play 期创建（不写场景），Wide 档隐藏。</summary>
        private void BuildDrawerToggle()
        {
            if (_drawerToggle != null || _missionRoot == null)
            {
                return;
            }

            var button = TwinUIStyle.Button(_missionRoot, "EvalDrawerToggle", "评估", TwinUIButton.Kind.Default,
                Vector2.zero, new Vector2(DrawerToggleWidth, DrawerToggleHeight), ToggleDrawer);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-DrawerToggleMargin, -DrawerToggleMargin);
            rt.SetAsLastSibling();
            _drawerToggle = button;
            _drawerToggleCount++;
        }

        // ------------------------------------------------------------- 对外操作

        public void ToggleDrawer()
        {
            SetDrawerOpen(!_drawerOpen);
        }

        public void SetDrawerOpen(bool open)
        {
            _drawerOpen = open;
            if (_eval != null)
            {
                bool show = open && _current != TwinUiBreakpoint.Wide;
                _eval.gameObject.SetActive(show);
                if (show)
                {
                    _eval.SetAsLastSibling();
                }
            }

            if (_missionRoot != null && _drawerToggle != null)
            {
                ((RectTransform)_drawerToggle.transform).SetAsLastSibling();
            }

            UpdateDrawerToggleLabel();
        }

        private void UpdateDrawerToggleLabel()
        {
            if (_drawerToggle == null)
            {
                return;
            }

            _drawerToggle.SetLabel(_drawerOpen ? "收起评估" : "评估");
            ((RectTransform)_drawerToggle.transform).sizeDelta = new Vector2(
                _drawerOpen ? 120f : DrawerToggleWidth, DrawerToggleHeight);
        }

        // ------------------------------------------------------------- 主入口

        /// <summary>P7：V2 外壳注册的页面排版钩子（内容宽, 内容高）；Legacy 模式下为 null。</summary>
        public static System.Action<float, float> V2PageLayoutHook;

        /// <summary>P7：V2 模式给标题栏右侧窗口按钮预留的宽度（Legacy 为 0）。</summary>
        public float V2RightInset;

        /// <summary>按内容宽高重算全部布局；幂等，可反复调用。</summary>
        public void Apply(float contentWidth, float contentHeight)
        {
            if (!_configured)
            {
                return;
            }

            _applyCount++;
            float w = Mathf.Max(640f, contentWidth);
            float h = Mathf.Max(300f, contentHeight);
            _lastWidth = w;
            _lastHeight = h;
            _current = Classify(w);

            // V2（浏览器式工作台）：页头/页签/工具栏/页面区由 TwinUiShellV2 排版；
            // 这里只保留顶栏 chip 折叠，并跳过 Legacy 的三列 Mission 重排。
            if (TwinUiShellMode.IsV2)
            {
                ApplyWorkbenchHeader(w);
                if (V2PageLayoutHook != null)
                {
                    V2PageLayoutHook(w, h);
                }

                CleanupDrawerState();
                return;
            }

            ApplyHeader(w);
            ApplyControlRows(w);
            ApplyMission(w, h);
            CleanupDrawerState();
        }

        // ------------------------------------------------------------- Header

        // V2 uses a title row and a status row. The legacy one-row arrangement
        // cannot fit its fixed-width chips beside window controls at 720px.
        /// <summary>
        /// 预置 KPI Label 的 TMP 在运行态不生成字形（UI/64 LABELPROBE：字体/字符表正常、ForceMesh 仍 chars=0）。
        /// 在 Header 层新建同文案 TMP 作为显示层；原 Label 隐藏保留（不删场景对象）。
        /// </summary>
        private TMP_Text EnsureHeaderLabel(RectTransform chip)
        {
            if (_header == null || chip == null || !_labelsReady)
            {
                return null;
            }

            TMP_Text existing;
            if (_fixedHeaderLabels.TryGetValue(chip, out existing) && existing != null)
            {
                return existing;
            }

            var source = chip.Find("Label") as RectTransform;
            TMP_Text sourceText = source != null ? source.GetComponent<TMP_Text>() : null;
            string text = sourceText != null ? sourceText.text : string.Empty;
            var go = new GameObject("HdrLabel_" + chip.name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_header, false);
            var fresh = go.GetComponent<TextMeshProUGUI>();
            fresh.font = TwinUIStyle.GetFont();
            fresh.text = text;
            fresh.fontSize = 12f;
            fresh.color = TwinUIStyle.QuietColor;
            fresh.alignment = TextAlignmentOptions.Left;
            fresh.enableWordWrapping = true;
            fresh.overflowMode = TextOverflowModes.Overflow;
            fresh.raycastTarget = false;
            fresh.maskable = true;
            fresh.ForceMeshUpdate();
            Debug.Log("[HDRLABEL] " + chip.name + " chars="
                + (fresh.textInfo != null ? fresh.textInfo.characterCount : -1)
                + " frame=" + Time.frameCount);
            _fixedHeaderLabels[chip] = fresh;
            return fresh;
        }

        private void ApplyWorkbenchHeader(float w)
        {
            if (_header == null) return;
            float headerW = Mathf.Max(320f, w - 2f * TwinUIStyle.WorkbenchPadX);
            TwinUIStyle.Row(_header, TwinUIStyle.WorkbenchPadX, 0f, headerW,
                TwinUIStyle.WorkbenchHeaderHeight);
            if (_title == null)
                _title = TwinUIStyle.Text(_header, "Title", "DT 工作台", 16f, TwinUIStyle.TextColor).rectTransform;
            var titleText = _title.GetComponent<TMP_Text>();
            if (titleText != null)
            {
                titleText.text = "DT 工作台";
                titleText.fontSize = 16f;
                titleText.enableWordWrapping = false;
                titleText.overflowMode = TextOverflowModes.Ellipsis;
            }

            // 标题行与主窗右上 3 个 28px 图标按钮共用同一条垂直中心线（y=8..36，中心 22）。
            TwinUIStyle.Row(_title, 0f, 8f,
                Mathf.Max(100f, headerW - TwinUIStyle.WorkbenchRightInset), TwinUIStyle.WorkbenchTitleRowHeight);
            _title.gameObject.SetActive(true);
            if (_clock != null) _clock.gameObject.SetActive(false);

            string[] names = w >= 1100f
                ? new[] { "KpiSource", "KpiRate", "KpiLatency", "AlarmBadge", "RecBadge", "EstopChip" }
                : w >= 860f
                    ? new[] { "KpiSource", "KpiRate", "AlarmBadge", "RecBadge", "EstopChip" }
                    : new[] { "KpiSource", "AlarmBadge", "RecBadge", "EstopChip" };
            foreach (var chip in _headerChips) chip.gameObject.SetActive(false);

            int n = names.Length;
            const float chipGap = 8f;
            float cell = Mathf.Max(64f, (headerW - chipGap * (n - 1)) / n);
            float chipY = TwinUIStyle.WorkbenchTitleRowHeight + TwinUIStyle.WorkbenchRowGap;
            for (int i = 0; i < n; i++)
            {
                var chip = FindHeaderChip(names[i]);
                if (chip == null) continue;
                chip.gameObject.SetActive(true);
                TwinUIStyle.Row(chip, i * (cell + chipGap), chipY, cell, TwinUIStyle.WorkbenchChipRowHeight);

                var label = chip.Find("Label") as RectTransform;
                var value = chip.Find("Value") as RectTransform;
                var text = chip.Find("Text") as RectTransform;
                if (label != null && value != null)
                {
                    // 左侧 KPI chip 与右侧状态 chip 统一底色/边框，避免"裸文字 + 卡片"两种权重。
                    var chipImage = chip.GetComponent<UnityEngine.UI.Image>();
                    if (chipImage != null)
                    {
                        chipImage.color = TwinUIStyle.CardAltColor;
                        TwinUIStyle.Outline(chip.gameObject, TwinUIStyle.BorderColor);
                    }

                    // KPI：label 左侧、value 右侧，一行读完，避免 40px 高的上下堆叠留白。
                    float labelW = Mathf.Clamp(cell * 0.40f, 44f, 72f);
                    TMP_Text fixedLabel = EnsureHeaderLabel(chip);
                    if (fixedLabel != null)
                    {
                        label.gameObject.SetActive(false);
                        TwinUIStyle.Row(fixedLabel.rectTransform, i * (cell + chipGap) + 8f,
                            chipY + 9f, labelW, 16f);
                        fixedLabel.fontSize = 12f;
                        fixedLabel.color = TwinUIStyle.QuietColor;
                        fixedLabel.enableWordWrapping = true;
                        fixedLabel.overflowMode = TextOverflowModes.Overflow;
                    }
                    else
                    {
                        TwinUIStyle.Row(label, 8f, 9f, labelW, 16f);
                    }

                    float valueX = 8f + labelW + 6f;
                    TwinUIStyle.Row(value, valueX, 4f, Mathf.Max(32f, cell - valueX - 8f), 28f);
                    var vt = value.GetComponent<TMP_Text>();
                    if (vt != null)
                    {
                        vt.fontSize = 18f;
                        vt.alignment = TextAlignmentOptions.Right;
                        vt.enableWordWrapping = false;
                        vt.overflowMode = TextOverflowModes.Ellipsis;
                        vt.margin = Vector4.zero;
                    }
                }
                else if (text != null)
                {
                    TwinUIStyle.Row(text, 20f, 8f, Mathf.Max(24f, cell - 28f), 20f);
                    var tt = text.GetComponent<TMP_Text>();
                    if (tt != null)
                    {
                        tt.fontSize = TwinUIStyle.FontBodySm;
                        tt.enableWordWrapping = false;
                        tt.overflowMode = TextOverflowModes.Ellipsis;
                    }
                }
            }
        }

        private void ApplyHeader(float w)
        {
            float headerWidth = Mathf.Max(320f, w - HeaderSideMargin * 2f);
            bool showClock = _current == TwinUiBreakpoint.Wide || _current == TwinUiBreakpoint.Medium;
            if (_clock != null)
            {
                _clock.gameObject.SetActive(showClock);
            }

            // 1) 该档位"应当可见"的 chip
            var visible = new List<RectTransform>();
            string[] names = _current == TwinUiBreakpoint.Wide ? HeaderChipOrder
                : _current == TwinUiBreakpoint.Medium ? MediumHeaderChips
                : NarrowHeaderChips;
            for (int i = 0; i < names.Length; i++)
            {
                var chip = FindHeaderChip(names[i]);
                if (chip != null)
                {
                    visible.Add(chip);
                }
            }

            // 2) Title 收窄 + Wide 折叠次要 chip，直到不越界
            // available = 左边距之外、扣除右侧时钟后可用于 Title+chips 的宽度（不再重复扣左边距）。
            float rightReserve = showClock ? HeaderClockWidth + HeaderChipGap : 0f;
            float available = headerWidth - HeaderSideMargin - HeaderChipGap - rightReserve - V2RightInset;
            float chipTotal = SumChipWidths(visible) + HeaderChipGap * Mathf.Max(0, visible.Count - 1);
            float titleWidth = HeaderTitleWidth;
            titleWidth = Mathf.Clamp(available - chipTotal, HeaderTitleMinWidth, HeaderTitleWidth);

            int folded = 0;
            if (titleWidth + chipTotal > available)
            {
                for (int i = 0; i < WideFoldOrder.Length; i++)
                {
                    if (_current != TwinUiBreakpoint.Wide)
                    {
                        break;
                    }

                    var chip = FindHeaderChip(WideFoldOrder[i]);
                    if (chip == null || !visible.Contains(chip))
                    {
                        continue;
                    }

                    visible.Remove(chip);
                    folded++;
                    chipTotal = SumChipWidths(visible) + HeaderChipGap * Mathf.Max(0, visible.Count - 1);
                    titleWidth = Mathf.Clamp(available - chipTotal, HeaderTitleMinWidth, HeaderTitleWidth);
                    if (titleWidth + chipTotal <= available)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < _headerChips.Count; i++)
            {
                var chip = _headerChips[i];
                chip.gameObject.SetActive(visible.Contains(chip));
            }

            if (_title != null)
            {
                var rt = _title;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(titleWidth, 32f);
                rt.anchoredPosition = new Vector2(HeaderSideMargin, 0f);
                var tmp = rt.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            float x = HeaderSideMargin + titleWidth + 12f;
            for (int i = 0; i < visible.Count; i++)
            {
                var chip = visible[i];
                Vector2 size = _headerChipSizes[chip];
                chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
                chip.pivot = new Vector2(0f, 0.5f);
                chip.sizeDelta = size;
                chip.anchoredPosition = new Vector2(x, 0f);
                x += size.x + HeaderChipGap;
            }

            if (_clock != null && showClock)
            {
                var rt = _clock;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-8f - V2RightInset, 0f);
            }

            if (_current == TwinUiBreakpoint.Wide && Debug.isDebugBuild)
            {
                // 折叠数仅调试输出，不污染正式日志。
                if (folded > 0)
                {
                    Debug.Log("[RESPONSIVE] header folded=" + folded + " titleWidth=" + titleWidth.ToString("F0")
                        + " visible=" + visible.Count);
                }
            }
        }

        private RectTransform FindHeaderChip(string name)
        {
            for (int i = 0; i < _headerChips.Count; i++)
            {
                if (_headerChips[i].name == name)
                {
                    return _headerChips[i];
                }
            }

            return null;
        }

        private float SumChipWidths(List<RectTransform> chips)
        {
            float sum = 0f;
            for (int i = 0; i < chips.Count; i++)
            {
                sum += _headerChipSizes[chips[i]].x;
            }

            return sum;
        }

        // ------------------------------------------------------------- TabBar / ContextBar

        private void ApplyControlRows(float w)
        {
            if (_tabBar != null)
            {
                _tabBar.anchorMin = _tabBar.anchorMax = new Vector2(0f, 1f);
                _tabBar.pivot = new Vector2(0f, 1f);
                _tabBar.anchoredPosition = new Vector2(HeaderSideMargin, -TabBarRowTop);
                _tabBar.sizeDelta = new Vector2(Mathf.Min(TabBarWidth, w - HeaderSideMargin * 2f), ControlRowHeight);
            }

            if (_contextBar == null)
            {
                return;
            }

            float contextWidth;
            if (_current == TwinUiBreakpoint.Wide)
            {
                _contextBar.anchoredPosition = new Vector2(HeaderSideMargin + TabBarWidth, -TabBarRowTop);
                contextWidth = Mathf.Max(200f, w - HeaderSideMargin * 2f - TabBarWidth);
            }
            else
            {
                _contextBar.anchoredPosition = new Vector2(HeaderSideMargin, -ContextSecondRowTop);
                contextWidth = Mathf.Max(200f, w - HeaderSideMargin * 2f);
            }

            _contextBar.anchorMin = _contextBar.anchorMax = new Vector2(0f, 1f);
            _contextBar.pivot = new Vector2(0f, 1f);
            _contextBar.sizeDelta = new Vector2(contextWidth, ControlRowHeight);
            ApplyContextItems(contextWidth);
        }

        private void ApplyContextItems(float contextWidth)
        {
            bool hideExtras = _current == TwinUiBreakpoint.Narrow || _current == TwinUiBreakpoint.Compact;
            var visible = new List<RectTransform>();
            for (int i = 0; i < _contextItems.Count; i++)
            {
                var item = _contextItems[i];
                bool isExtra = item.name == "SourceTag" || item.name == "Watermark";
                bool show = !(hideExtras && isExtra);
                item.gameObject.SetActive(show);
                if (show)
                {
                    visible.Add(item);
                }
            }

            float avail = Mathf.Max(40f, contextWidth - 16f);
            float sumOriginal = 0f;
            for (int i = 0; i < visible.Count; i++)
            {
                sumOriginal += _contextItemSizes[visible[i]].x;
            }

            float gaps = HeaderChipGap * Mathf.Max(0, visible.Count - 1);
            float factor = sumOriginal + gaps > avail
                ? Mathf.Clamp((avail - gaps) / Mathf.Max(1f, sumOriginal), 0.30f, 1f)
                : 1f;

            float x = 0f;
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i];
                Vector2 original = _contextItemSizes[item];
                Vector2 originalPos = _contextItemPositions[item];
                float width = Mathf.Max(56f, original.x * factor);
                item.anchorMin = item.anchorMax = new Vector2(0f, 1f);
                item.pivot = new Vector2(0f, 1f);
                item.sizeDelta = new Vector2(width, original.y);
                item.anchoredPosition = new Vector2(x, originalPos.y);
                x += width + HeaderChipGap;
            }
        }

        // ------------------------------------------------------------- MissionRoot

        private void ApplyMission(float w, float h)
        {
            float topInset = _current == TwinUiBreakpoint.Wide ? ContentTopSingleRow : ContentTopTwoRows;
            float missionHeight = Mathf.Max(120f, h - topInset - ContentBottom);
            SetStretch(_missionRoot, topInset);
            SetStretch(_industrialRoot, topInset);
            SetStretch(_twinOpsRoot, topInset);

            float stepsWidth;
            float detailWidth;
            float evalWidth;
            float evalX;
            float stepsX = 0f;
            float detailX;

            switch (_current)
            {
                case TwinUiBreakpoint.Wide:
                    stepsWidth = StepsWide;
                    evalWidth = EvalDrawerMaxWidth;
                    detailX = stepsWidth + ColumnGap;
                    evalX = Mathf.Max(detailX + MinDetailWidth + ColumnGap, w - evalWidth);
                    detailWidth = Mathf.Max(MinDetailWidth, evalX - detailX - ColumnGap);
                    break;
                case TwinUiBreakpoint.Medium:
                    stepsWidth = StepsMedium;
                    detailX = stepsWidth + ColumnGap;
                    detailWidth = Mathf.Max(MinDetailWidth, w - detailX);
                    evalWidth = Mathf.Min(EvalDrawerMaxWidth, detailWidth);
                    evalX = w - evalWidth;
                    break;
                default:
                    stepsWidth = StepsNarrow;
                    detailX = stepsWidth + ColumnGap;
                    detailWidth = Mathf.Max(MinDetailWidth, w - detailX);
                    evalWidth = detailWidth;
                    evalX = detailX;
                    break;
            }

            if (_steps != null)
            {
                PlaceColumn(_steps, stepsX, stepsWidth, missionHeight);
            }

            if (_detail != null)
            {
                PlaceColumn(_detail, detailX, detailWidth, missionHeight);
            }

            if (_eval != null)
            {
                PlaceColumn(_eval, evalX, evalWidth, missionHeight);
                bool show = _current == TwinUiBreakpoint.Wide || _drawerOpen;
                _eval.gameObject.SetActive(show);
                if (show)
                {
                    _eval.SetAsLastSibling();
                }
            }

            if (_drawerToggle != null)
            {
                var rt = (RectTransform)_drawerToggle.transform;
                bool showToggle = _current != TwinUiBreakpoint.Wide;
                rt.gameObject.SetActive(showToggle);
                if (showToggle)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-DrawerToggleMargin, -DrawerToggleMargin);
                    rt.SetAsLastSibling();
                }
            }

            // 卡片内部定宽子件（文本/时间轴/评估格）随列宽收放，避免"卡内越界"（窗口包围盒之外的视觉溢出）。
            ApplyInnerWidths(stepsWidth, detailWidth, evalWidth);

            if (Debug.isDebugBuild)
            {
                Debug.Log("[RESPONSIVE] w=" + w.ToString("F0") + " h=" + h.ToString("F0") + " bp=" + _current
                    + " steps=" + stepsWidth.ToString("F0") + " detail=" + detailWidth.ToString("F0")
                    + " eval=" + evalWidth.ToString("F0") + " drawer=" + _drawerOpen);
            }
        }

        /// <summary>卡片内部定宽子件随列宽收放（只改宽度，不动字号/位置锚点语义）。</summary>
        private void ApplyInnerWidths(float stepsW, float detailW, float evalW)
        {
            float stepTextW = Mathf.Max(120f, stepsW - 80f);
            for (int i = 0; i < _stepRows.Count; i++)
            {
                var row = _stepRows[i];
                row.sizeDelta = new Vector2(stepsW, row.sizeDelta.y);
            }

            for (int i = 0; i < _stepTexts.Count; i++)
            {
                var rt = _stepTexts[i];
                rt.sizeDelta = new Vector2(stepTextW, rt.sizeDelta.y);
            }

            float detailInner = Mathf.Max(200f, detailW - 48f);
            for (int i = 0; i < _detailInner.Count; i++)
            {
                var rt = _detailInner[i];
                rt.sizeDelta = new Vector2(detailInner, rt.sizeDelta.y);
            }

            if (_timelineTitle != null)
            {
                _timelineTitle.sizeDelta = new Vector2(Mathf.Min(300f, Mathf.Max(120f, detailInner - 32f)),
                    _timelineTitle.sizeDelta.y);
            }

            if (_timelineState != null)
            {
                _timelineState.sizeDelta = new Vector2(Mathf.Max(120f, detailInner - 32f),
                    _timelineState.sizeDelta.y);
            }

            float evalInner = Mathf.Max(200f, evalW - 40f);
            if (_evalTitle != null)
            {
                _evalTitle.sizeDelta = new Vector2(evalInner, _evalTitle.sizeDelta.y);
            }

            for (int i = 0; i < _evalCells.Count; i++)
            {
                _evalCells[i].sizeDelta = new Vector2(evalInner, _evalCells[i].sizeDelta.y);
            }

            if (_evalNote != null)
            {
                _evalNote.sizeDelta = new Vector2(evalInner, _evalNote.sizeDelta.y);
            }
        }

        private static void PlaceColumn(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
        }

        private void CleanupDrawerState()
        {
            bool shouldBeOpen = _drawerOpen && _current != TwinUiBreakpoint.Wide;
            _drawerOpen = shouldBeOpen;
            UpdateDrawerToggleLabel();
        }

        private static void SetStretch(RectTransform rt, float topInset)
        {
            if (rt == null)
            {
                return;
            }

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, ContentBottom);
            rt.offsetMax = new Vector2(0f, -topInset);
        }
    }
}
