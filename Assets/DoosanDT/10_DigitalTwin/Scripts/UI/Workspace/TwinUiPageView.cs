// TwinUiPageView.cs —— 页内布局：页头 + 二级导航 + 卡片瀑布流（P8）
// 设计目标：一页 = 数据驱动；页面只负责"标题区 + 二级导航 + 卡片区"的排版，不关心卡片内容。
// 硬约束：卡片用 TwinUiCardGrid 手动排版；本类不创建业务对象、不订阅数据。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiPageView
    {
        public const float SubNavHeight = 28f;
        public const float BottomPad = 20f;

        private readonly List<TwinUiModule> _modules = new List<TwinUiModule>();
        private readonly List<TwinUiCardView> _cards = new List<TwinUiCardView>();
        private readonly List<TwinUiCardGrid.Entry> _entries = new List<TwinUiCardGrid.Entry>();
        private readonly List<TwinUIButton> _subButtons = new List<TwinUIButton>();
        private readonly List<string> _subIds = new List<string>();

        private RectTransform _root;
        private RectTransform _header;
        private TMP_Text _kicker;
        private TMP_Text _title;
        private TMP_Text _subtitle;
        private RectTransform _subNav;
        private RectTransform _cardArea;
        private TMP_Text _empty;
        private ImageDot _status;

        // 本批（回放 UI）：页顶"连接状态条"——源类型/连接/新鲜度/更新时间。
        // 只承载只读文本，不订阅数据；由宿主（TwinUiSurfaceHost）每 0.5 s 推送一次。
        public const float ConnectionStripHeight = 20f;
        private RectTransform _connStrip;
        private TMP_Text _connText;
        private UnityEngine.UI.Image _connDot;
        private bool _connActive;
        private TwinUITone _connTone = TwinUITone.Unknown;

        // 本批（A1/A2/A3）：数字孪生页常驻"数据源"控制条。
        // 两行：① 实时/回放/换回放 + 当前源身份；② 记录频率(CSV 只读) / 显示刷新率(可调) / 时间窗(10/20/60 s)。
        // 只调用 TwinUiSourceSwitch（只读回放 + 显示开关），不碰任何数据链写入。
        public const float SourceBarHeight = 46f;
        public static readonly float[] HzOptions = { 5f, 10f, 20f };
        public static readonly float[] WindowOptions = { 10f, 20f, 60f };

        /// <summary>fix1 B(2)：详情浮层高度（展开时压在首卡上方，不改变页面高度、不触发重排版）。</summary>
        public const float DetailPanelHeight = 92f;

        private RectTransform _srcBar;
        private TMP_Text _srcText;        // 会话名（单独一格；超长省略号，完整内容见"详情"）
        private TMP_Text _recordText;     // 记录频率（独立区域，不与会话名拼在一行）
        private TMP_Text _displayText;
        private TMP_Text _windowText;
        private TwinUIButton _btnLive;
        private TwinUIButton _btnReplay;
        private TwinUIButton _btnNext;
        private TwinUIButton _btnPause;   // fix1 B(5)：播放/暂停（与"冻结显示"分开的两个控件）
        private TwinUIButton _btnDetail;  // fix1 B(2)：详情（完整会话信息可查看）
        private TwinUIButton[] _hzButtons;
        private TwinUIButton[] _winButtons;
        private RectTransform _detailPanel;
        private TMP_Text _detailText;
        private bool _detailOpen;
        private float _srcInner;
        private string _detailCaption = string.Empty;
        private bool _srcActive;

        private TwinUiWorkspace _workspace;
        private string _subId = string.Empty;
        private float _width;
        private float _height;

        // P10：列数状态跨页保留（切页/拆出不重算），0 = 尚未初始化
        private int _columns;
        private float _lastC = 720f;

        private struct ImageDot
        {
            public UnityEngine.UI.Image Dot;
            public TMP_Text Text;
        }

        public TwinUiWorkspace Workspace => _workspace;
        public string SubId => _subId;
        public RectTransform Root => _root;
        public RectTransform CardArea => _cardArea;
        public float ContentHeight => _height;
        public int CardCount => _cards.Count;

        /// <summary>当前列数（P10 滞回结果，自检用）。</summary>
        public int Columns => _columns;

        /// <summary>最近一次排版使用的 C（页内可排版宽）。</summary>
        public float ContentWidth => _lastC;

        /// <summary>P11：累计排版次数（自检用；用于证明空闲时零重排、失效时只重排一次）。</summary>
        public int ApplyCount { get; private set; }

        /// <summary>自检用：页头文案（标题/小标）与当前二级导航。</summary>
        public string TitleText => _title != null ? _title.text : string.Empty;

        public string KickerText => _kicker != null ? _kicker.text : string.Empty;

        public string SubtitleText => _subtitle != null ? _subtitle.text : string.Empty;
        public IReadOnlyList<TwinUiCardView> Cards => _cards;

        public void Build(Transform parent)
        {
            _root = TwinUIStyle.Frame(parent, "PageView");
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = Vector2.zero;

            _header = TwinUIStyle.Frame(_root, "PageHeader");
            _header.anchorMin = new Vector2(0f, 1f);
            _header.anchorMax = new Vector2(1f, 1f);
            _header.pivot = new Vector2(0.5f, 1f);
            _header.anchoredPosition = Vector2.zero;
            _header.sizeDelta = new Vector2(0f, 88f);

            _kicker = TwinUIStyle.Text(_header, "Kicker", string.Empty, TwinUIStyle.FontKicker, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(_kicker.rectTransform, 0f, 0f, 300f, 12f);

            _title = TwinUIStyle.Text(_header, "Title", string.Empty, TwinUIStyle.FontPageTitle, TwinUIStyle.TextColor);
            TwinUIStyle.Row(_title.rectTransform, 0f, 14f, 400f, 28f);

            _subtitle = TwinUIStyle.Text(_header, "Subtitle", string.Empty, TwinUIStyle.FontBodySm, TwinUIStyle.MutedColor);
            _subtitle.enableWordWrapping = true;
            TwinUIStyle.Row(_subtitle.rectTransform, 0f, 46f, 520f, 34f);

            var statusRoot = TwinUIStyle.Frame(_header, "PageStatus");
            statusRoot.anchorMin = statusRoot.anchorMax = new Vector2(1f, 1f);
            statusRoot.pivot = new Vector2(1f, 1f);
            statusRoot.anchoredPosition = new Vector2(0f, -16f);
            statusRoot.sizeDelta = new Vector2(120f, 18f);
            var dot = TwinUIStyle.StatusDot(statusRoot, "Dot", 6f, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(dot.rectTransform, 0f, 5f, 6f, 6f);
            var statusText = TwinUIStyle.Text(statusRoot, "Text", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.QuietColor, TextAlignmentOptions.Left);
            TwinUIStyle.Row(statusText.rectTransform, 10f, 0f, 110f, 18f);
            _status = new ImageDot { Dot = dot, Text = statusText };

            _connStrip = TwinUIStyle.Frame(_root, "ConnectionStrip");
            _connStrip.anchorMin = new Vector2(0f, 1f);
            _connStrip.anchorMax = new Vector2(1f, 1f);
            _connStrip.pivot = new Vector2(0.5f, 1f);
            _connStrip.anchoredPosition = Vector2.zero;
            _connStrip.sizeDelta = new Vector2(0f, ConnectionStripHeight);

            _connDot = TwinUIStyle.StatusDot(_connStrip, "Dot", 6f, TwinUIStyle.QuietColor);
            TwinUIStyle.Row(_connDot.rectTransform, 0f, 7f, 6f, 6f);

            _connText = TwinUIStyle.Text(_connStrip, "Text", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.QuietColor, TextAlignmentOptions.Left);
            _connText.enableWordWrapping = false;
            TwinUIStyle.Row(_connText.rectTransform, 10f, 0f, 600f, ConnectionStripHeight);

            _connStrip.gameObject.SetActive(false);

            BuildSourceBar();

            _subNav = TwinUIStyle.Frame(_root, "SubNav");
            _subNav.anchorMin = new Vector2(0f, 1f);
            _subNav.anchorMax = new Vector2(1f, 1f);
            _subNav.pivot = new Vector2(0.5f, 1f);
            _subNav.anchoredPosition = Vector2.zero;
            _subNav.sizeDelta = new Vector2(0f, SubNavHeight);

            _cardArea = TwinUIStyle.Frame(_root, "CardArea");
            _cardArea.anchorMin = new Vector2(0f, 1f);
            _cardArea.anchorMax = new Vector2(1f, 1f);
            _cardArea.pivot = new Vector2(0.5f, 1f);
            _cardArea.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 数字孪生页常驻"数据源"控制条（A1 入口 / A2 时间窗 / A3 两个频率分开标注）。
        /// 按钮直接驱动 TwinUiSourceSwitch（只读回放 + 显示开关），因此不经宿主也能操作。
        /// </summary>
        private void BuildSourceBar()
        {
            _srcBar = TwinUIStyle.Frame(_root, "SourceBar");

            var title = TwinUIStyle.Text(_srcBar, "SrcTitle", "数据源", TwinUIStyle.FontBodySm,
                TwinUIStyle.MutedColor);
            _srcTitleLabel = title;

            _btnLive = TwinUIStyle.Button(_srcBar, "SrcLive", "实时", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(44f, 20f), () => TwinUiSourceSwitch.SetLive());
            _btnReplay = TwinUIStyle.Button(_srcBar, "SrcReplay", "回放", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(44f, 20f), () => TwinUiSourceSwitch.SetReplay());
            _btnNext = TwinUIStyle.Button(_srcBar, "SrcNext", "换回放", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(58f, 20f), () => TwinUiSourceSwitch.NextReplay());
            // fix1 B(5)：生产"播放/暂停"。只暂停回放游标与回放钟，不碰"冻结显示"(TwinUiViewFreeze)。
            _btnPause = TwinUIStyle.Button(_srcBar, "SrcPause", "暂停", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(52f, 20f), TogglePlayback);
            SetSmall(_btnLive);
            SetSmall(_btnReplay);
            SetSmall(_btnNext);
            SetSmall(_btnPause);

            // 会话名单独一格：enableWordWrapping=false + Ellipsis，绝不与频率抢同一行的宽度；
            // 完整内容由「详情」浮层查看（不靠"截断到看不见"）。
            _srcText = TwinUIStyle.Text(_srcBar, "SrcInfo", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.QuietColor, TextAlignmentOptions.Left);
            _srcText.enableWordWrapping = false;
            _srcText.overflowMode = TextOverflowModes.Ellipsis;

            _btnDetail = TwinUIStyle.Button(_srcBar, "SrcDetail", "详情", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(56f, 20f), ToggleDetail);
            SetSmall(_btnDetail);

            _recordText = TwinUIStyle.Text(_srcBar, "RecordHz", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            _recordText.enableWordWrapping = false;
            _displayText = TwinUIStyle.Text(_srcBar, "DisplayHz", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            _displayText.enableWordWrapping = false;

            _hzButtons = new TwinUIButton[HzOptions.Length];
            for (int i = 0; i < HzOptions.Length; i++)
            {
                float hz = HzOptions[i];
                _hzButtons[i] = TwinUIStyle.Button(_srcBar, "Hz" + hz.ToString("F0"), hz.ToString("F0"),
                    TwinUIButton.Kind.Plain, Vector2.zero, new Vector2(36f, 20f),
                    () => TwinUiSourceSwitch.SetDisplayHz(hz));
                SetSmall(_hzButtons[i]);
            }

            _windowText = TwinUIStyle.Text(_srcBar, "WindowLabel", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Left);
            _windowText.enableWordWrapping = false;

            _winButtons = new TwinUIButton[WindowOptions.Length];
            for (int i = 0; i < WindowOptions.Length; i++)
            {
                float seconds = WindowOptions[i];
                _winButtons[i] = TwinUIStyle.Button(_srcBar, "Win" + seconds.ToString("F0"),
                    seconds.ToString("F0") + " s", TwinUIButton.Kind.Plain, Vector2.zero,
                    new Vector2(42f, 20f), () => TwinUiSourceSwitch.SetTimeWindow(seconds));
                SetSmall(_winButtons[i]);
            }

            // 「详情」浮层：完整会话身份 / 路径 / schema / 行数 / 两个频率口径 / 进度 / 播放状态。
            // 它是**浮层**，不改页面高度，所以不会引起卡片重排（避免为了看详情把布局挤坏）。
            _detailPanel = TwinUIStyle.Frame(_srcBar, "SourceDetail");
            var detailBg = _detailPanel.gameObject.AddComponent<UnityEngine.UI.Image>();
            detailBg.color = TwinUIStyle.Alpha(TwinUIStyle.PlotBgColor, 0.98f);
            detailBg.raycastTarget = false;

            _detailText = TwinUIStyle.Text(_detailPanel, "Text", string.Empty, TwinUIStyle.FontUnit,
                TwinUIStyle.TextColor, TextAlignmentOptions.TopLeft);
            _detailText.enableWordWrapping = true;
            _detailText.richText = false;
            TwinUIStyle.Row(_detailText.rectTransform, 6f, 4f, 600f, DetailPanelHeight - 8f);
            _detailPanel.gameObject.SetActive(false);

            _srcBar.gameObject.SetActive(false);
            RefreshSourceBar();
        }

        /// <summary>fix1 B(5)：播放/暂停按钮动作（唯一状态在 TwinUiPlaybackControl，执行点在 feed）。</summary>
        private void TogglePlayback()
        {
            TwinUiSourceSwitch.TogglePause();
            RefreshSourceBar();
        }

        /// <summary>fix1 B(2)：展开/收起详情浮层（只改浮层显隐，不重排版页面）。</summary>
        private void ToggleDetail()
        {
            _detailOpen = !_detailOpen;
            RefreshSourceBar();
        }

        private TMP_Text _srcTitleLabel;

        private static void SetSmall(TwinUIButton button)
        {
            if (button != null && button.Label != null)
            {
                button.Label.fontSize = TwinUIStyle.FontUnit;
            }
        }

        /// <summary>把 TwinUiSourceSwitch 的状态刷到控制条文字与按钮选中态（只读，0.5 s 由宿主调用）。</summary>
        public void RefreshSourceBar()
        {
            if (_srcText == null)
            {
                return;
            }

            TwinUiSourceSwitch.EnsureInit();
            // R2 A-1：replay 与所有数值都派生自 TwinUiReplayFeed.Active（唯一真值），
            // 因此和顶部状态条（binder→Active）不可能再出现"顶部回放 / 底栏实时"。
            bool replay = TwinUiSourceSwitch.IsReplay;
            double recordHz = TwinUiSourceSwitch.RecordHz;
            bool paused = TwinUiPlaybackControl.IsPaused;

            // A-3：整文件速率（加载即可读）与窗口速率（2 s 窗内接收行实测）分字段，口径写在文本里；
            // TARGET 100 Hz 是既有目标元数据，显式标"目标，非实测"，不与实测混排。
            string filePart = TwinUiSourceSwitch.RecordHzFileAvailable
                ? "整文件 " + TwinUiSourceSwitch.RecordHzFile.ToString("F1") + " Hz（"
                  + TwinUiSourceSwitch.RecordRowsFile + " 行/"
                  + TwinUiSourceSwitch.RecordSpanFileSeconds.ToString("F1") + " s）"
                : "整文件 —（不足 2 接收行）";
            _recordFileText = filePart;

            // fix1 B(2)：① 会话名**单独一格**，只放身份；频率不再拼进同一行。
            // 超长由 Ellipsis 收尾，完整路径/schema/口径去「详情」看（不是靠截断藏起来）。
            _srcText.text = replay
                ? "回放 · " + TwinUiSourceSwitch.CurrentRunId
                : "实时（无回放；不连 ROS/硬件）";

            // fix1 B(2)：② 记录频率独立区域，口径写在括号里（窗 / 整文件）。
            if (!replay)
            {
                _recordText.text = "记录 —（无回放）";
            }
            else if (TwinUiSourceSwitch.RecordHzWindowAvailable)
            {
                _recordText.text = "记录 " + recordHz.ToString("F1") + " Hz（窗 "
                    + TwinUiSourceSwitch.RecordWindowSeconds.ToString("F0") + " s）";
            }
            else if (TwinUiSourceSwitch.RecordHzFileAvailable)
            {
                _recordText.text = "记录 " + recordHz.ToString("F1") + " Hz（整文件）";
            }
            else
            {
                _recordText.text = "记录 —（不足 2 接收行）";
            }

            _displayText.text = "显示 " + TwinUiSourceSwitch.DisplayHz.ToString("F0") + " Hz";
            _windowText.text = "时间窗 " + TwinUiSourceSwitch.TimeWindowSeconds.ToString("F0") + " s";

            _btnLive.SetSelected(!replay);
            _btnReplay.SetSelected(replay);
            for (int i = 0; i < _hzButtons.Length; i++)
            {
                _hzButtons[i].SetSelected(Mathf.Abs(HzOptions[i] - TwinUiSourceSwitch.DisplayHz) < 0.01f);
            }

            for (int i = 0; i < _winButtons.Length; i++)
            {
                _winButtons[i].SetSelected(
                    Mathf.Abs(WindowOptions[i] - TwinUiSourceSwitch.TimeWindowSeconds) < 0.01f);
            }

            // fix1 B(5)：播放/暂停按钮（与"冻结显示"是两个控件）。实时源不可暂停 → disabled 且给原因，
            // 不允许"实时模式下点了暂停、界面却显示已暂停"的假生效。
            if (_btnPause != null)
            {
                _btnPause.SetLabel(TwinUiPlaybackControl.ButtonLabel);
                _btnPause.SetSelected(replay && paused);
                _btnPause.SetInteractable(replay, replay ? null : "实时源没有可暂停的回放游标");
            }

            if (_btnDetail != null)
            {
                _btnDetail.SetLabel(_detailOpen ? "收起" : "详情");
            }

            if (_detailText != null)
            {
                _detailText.text = BuildDetailText(replay, paused);
            }

            if (_detailPanel != null)
            {
                _detailPanel.gameObject.SetActive(_detailOpen);
                LayoutDetailPanel();
            }
        }

        /// <summary>fix1 B(2)：详情浮层文本（完整输入路径 / schema / 行数 / 两个频率口径 / 播放状态）。</summary>
        private string BuildDetailText(bool replay, bool paused)
        {
            var sb = new System.Text.StringBuilder(360);
            if (!replay)
            {
                sb.Append("实时（无回放）：未加载任何 CSV；不连 ROS/硬件；记录频率无意义。");
            }
            else
            {
                sb.Append("路径 ").Append(TwinUiSourceSwitch.CurrentCsv).Append('\n');
                var feed = TwinUiReplayFeed.Active;
                sb.Append("schema=").Append(feed != null ? feed.SchemaLabel : "未识别")
                    .Append(" · 接收行 ").Append(TwinUiSourceSwitch.RecordRowsFile)
                    .Append(" · 跨度 ").Append(TwinUiSourceSwitch.RecordSpanFileSeconds.ToString("F1"))
                    .Append(" s").Append('\n');
                sb.Append("记录 ").Append(TwinUiSourceSwitch.RecordHzFile.ToString("F1"))
                    .Append(" Hz（整文件）· 窗口 ").Append(TwinUiSourceSwitch.RecordHzWindow.ToString("F1"))
                    .Append(" Hz（").Append(TwinUiSourceSwitch.RecordWindowSeconds.ToString("F0"))
                    .Append(" s 窗）· 进度 ")
                    .Append((TwinUiSourceSwitch.ReplayProgress01 * 100d).ToString("F0"))
                    .Append("% · TARGET ").Append(TwinUiSourceSwitch.TargetSampleHz.ToString("F0"))
                    .Append(" Hz（目标，非实测）").Append('\n');
                sb.Append("播放 ").Append(paused ? "已暂停（停游标与回放钟）" : "播放中")
                    .Append(" · 分段 #").Append(TwinUiSourceSwitch.Segment)
                    .Append(" · 循环回绕 ").Append(TwinUiSourceSwitch.LoopWrapCount)
                    .Append(" 次 · 额外源 ").Append(TwinUiSourceSwitch.ExtraSourceCount).Append(" 个");
            }

            if (!string.IsNullOrEmpty(_detailCaption))
            {
                sb.Append('\n').Append(_detailCaption);
            }

            return sb.ToString();
        }

        /// <summary>详情浮层的位置/尺寸（随 SourceBar 的实际内容宽收敛，不超出页面）。</summary>
        private void LayoutDetailPanel()
        {
            if (_detailPanel == null)
            {
                return;
            }

            float inner = _srcInner > 0f ? _srcInner : 640f;
            float w = Mathf.Max(240f, inner - 4f);
            float h = DetailPanelHeight;
            if (_detailText != null)
            {
                // fix1：先按目标宽度量一次"完整详情"需要的高度，再定面板高度。
                // 这样 720 窄屏也不会把路径/哈希/schema 裁掉（完整详情必须可看，不是截断）。
                TwinUIStyle.Row(_detailText.rectTransform, 6f, 4f, Mathf.Max(120f, w - 12f), h - 8f);
                float need = _detailText.GetPreferredValues().y;
                if (need > h - 8f)
                {
                    h = Mathf.Min(260f, need + 14f);
                }
            }

            TwinUIStyle.Row(_detailPanel, 0f, SourceBarHeight + 2f, w, h);
            if (_detailText != null)
            {
                TwinUIStyle.Row(_detailText.rectTransform, 6f, 4f, Mathf.Max(120f, w - 12f), h - 8f);
            }
        }

        private void LayoutSourceBar(float inner)
        {
            if (_srcBar == null)
            {
                return;
            }

            _srcInner = inner;

            // 行 1：数据源按钮组 → 播放/暂停 → 会话名（省略号）→ 详情。
            // 每一格的 x 区间互不重叠；会话名宽度 = 详情按钮左边界 − 自己左边界 − 8，
            // 所以"会话名与频率重叠"在布局层面不可能再发生（不靠肉眼判断）。
            TwinUIStyle.Row(_srcTitleLabel.rectTransform, 0f, 0f, 40f, 20f);
            TwinUIStyle.Row((RectTransform)_btnLive.transform, 42f, 0f, 44f, 20f);
            TwinUIStyle.Row((RectTransform)_btnReplay.transform, 88f, 0f, 44f, 20f);
            TwinUIStyle.Row((RectTransform)_btnNext.transform, 134f, 0f, 58f, 20f);
            if (_btnPause != null)
            {
                TwinUIStyle.Row((RectTransform)_btnPause.transform, 196f, 0f, 52f, 20f);
            }

            float detailX = Mathf.Max(260f, inner - 56f);
            float sessionX = 254f;
            float sessionW = Mathf.Max(60f, detailX - sessionX - 8f);
            TwinUIStyle.Row(_srcText.rectTransform, sessionX, 0f, sessionW, 20f);
            if (_btnDetail != null)
            {
                TwinUIStyle.Row((RectTransform)_btnDetail.transform, detailX, 0f, 56f, 20f);
            }

            // 行 2：记录频率（独立）/ 显示刷新率 + 三档 / 时间窗 + 三档。右端 584 < 688（720 宽时的 inner）。
            TwinUIStyle.Row(_recordText.rectTransform, 0f, 24f, 170f, 20f);
            TwinUIStyle.Row(_displayText.rectTransform, 174f, 24f, 68f, 20f);
            for (int i = 0; i < _hzButtons.Length; i++)
            {
                TwinUIStyle.Row((RectTransform)_hzButtons[i].transform, 246f + i * 36f, 24f, 36f, 20f);
            }

            TwinUIStyle.Row(_windowText.rectTransform, 362f, 24f, 88f, 20f);
            for (int i = 0; i < _winButtons.Length; i++)
            {
                TwinUIStyle.Row((RectTransform)_winButtons[i].transform, 454f + i * 44f, 24f, 42f, 20f);
            }

            LayoutDetailPanel();
        }

        public bool SourceBarVisible => _srcActive;

        public string SourceLineText => _srcText != null ? _srcText.text : string.Empty;

        public string RecordLineText => _recordText != null ? _recordText.text : string.Empty;

        private string _recordFileText = string.Empty;

        /// <summary>A-3：整文件速率那一段的原文（"整文件 X.X Hz（N 行/T s）"或"整文件 —（…）"）。</summary>
        public string RecordFileText => _recordFileText;

        public string DisplayLineText => _displayText != null ? _displayText.text : string.Empty;

        public string WindowLineText => _windowText != null ? _windowText.text : string.Empty;

        public TwinUIButton LiveButton => _btnLive;
        public TwinUIButton ReplayButton => _btnReplay;
        public TwinUIButton NextReplayButton => _btnNext;
        public TwinUIButton[] DisplayHzButtons => _hzButtons;
        public TwinUIButton[] TimeWindowButtons => _winButtons;

        // ---- fix1 B(2)/B(5)：自检可读的新控件与详情文本 ----

        /// <summary>播放/暂停按钮（生产控件；与"冻结显示"是两个不同的东西）。</summary>
        public TwinUIButton PauseButton => _btnPause;

        /// <summary>详情按钮。</summary>
        public TwinUIButton DetailButton => _btnDetail;

        /// <summary>详情浮层当前是否可见。</summary>
        public bool DetailVisible => _detailOpen && _detailPanel != null && _detailPanel.gameObject.activeSelf;

        /// <summary>详情浮层全文（完整路径/schema/行数/两个频率口径/播放状态）。</summary>
        public string DetailLineText => _detailText != null ? _detailText.text : string.Empty;

        /// <summary>详情浮层矩形（布局不重叠断言用；未构建时为 null）。</summary>
        public RectTransform DetailPanel => _detailPanel;

        /// <summary>
        /// 供自检/审计把"输入文件 SHA256"等证据写进详情浮层（生产默认空，不猜不算）。
        /// </summary>
        public void SetDetailCaption(string caption)
        {
            _detailCaption = caption ?? string.Empty;
            if (_detailOpen)
            {
                RefreshSourceBar();
            }
        }

        /// <summary>程序化展开/收起详情（等价于点「详情」；截图与断言用，不改变页面高度）。</summary>
        public void SetDetailOpen(bool open)
        {
            if (_detailOpen == open)
            {
                return;
            }

            _detailOpen = open;
            RefreshSourceBar();
        }

        /// <summary>切换页签：重建页头与二级导航（卡片由宿主维护，不在这里销毁）。</summary>
        public void SetWorkspace(TwinUiWorkspace workspace, string subId,
            UnityEngine.Events.UnityAction<string> onSubChanged)
        {
            _workspace = workspace;
            _subId = string.IsNullOrEmpty(subId) ? DefaultSub(workspace) : subId;

            _kicker.text = workspace != null ? workspace.Kicker : string.Empty;
            _title.text = workspace != null ? workspace.Title : string.Empty;
            _subtitle.text = workspace != null ? workspace.Subtitle : string.Empty;
            if (_status.Text != null)
            {
                _status.Text.text = workspace != null ? workspace.StatusText : string.Empty;
            }

            if (_status.Dot != null && workspace != null)
            {
                _status.Dot.color = TwinUITheme.ToneColor(workspace.StatusTone);
            }

            for (int i = 0; i < _subButtons.Count; i++)
            {
                if (_subButtons[i] != null)
                {
                    Object.Destroy(_subButtons[i].gameObject);
                }
            }

            _subButtons.Clear();
            _subIds.Clear();

            if (workspace != null && workspace.HasSubNav)
            {
                float x = 0f;
                for (int i = 0; i < workspace.SubNavIds.Length; i++)
                {
                    string id = workspace.SubNavIds[i];
                    string label = workspace.SubNavLabels != null && i < workspace.SubNavLabels.Length
                        ? workspace.SubNavLabels[i]
                        : id;
                    var btn = TwinUIStyle.Button(_subNav, "Sub_" + id, label, TwinUIButton.Kind.Plain,
                        Vector2.zero, new Vector2(64f, 24f));
                    if (btn.Label != null)
                    {
                        btn.Label.fontSize = TwinUIStyle.FontBodySm;
                    }

                    AddSubButton(btn, id, x, onSubChanged);
                    x += 64f + TwinUIStyle.Space8;
                }
            }

            // 连接状态条只属于"数字孪生"页；切到别的页立刻隐藏，避免残留上一页源身份。
            if (workspace == null || workspace.Id != TwinUiWorkspaceRegistry.Twin)
            {
                SetConnectionStatus(string.Empty, TwinUITone.Unknown);
            }
        }

        /// <summary>
        /// 页顶连接状态条（源类型/连接/新鲜度/更新时间）。空文本 = 隐藏。
        /// 返回"可见状态是否变化"，宿主据此决定是否重排一次页（文本变化不触发重排）。
        /// </summary>
        public bool SetConnectionStatus(string text, TwinUITone tone)
        {
            _connTone = tone;
            if (_connText != null)
            {
                _connText.text = text ?? string.Empty;
                _connText.color = TwinUITheme.ToneTextColor(tone);
            }

            if (_connDot != null)
            {
                _connDot.color = TwinUITheme.ToneColor(tone);
            }

            // 状态条的"存在"由工作区决定（数字孪生页恒有一行），文本变化不改变布局，
            // 因此这里永远返回 false：宿主不需要为了刷新一行字而重排整页。
            return false;
        }

        public bool ConnectionStatusVisible => _connActive;

        public string ConnectionStatusText => _connText != null ? _connText.text : string.Empty;

        private void AddSubButton(TwinUIButton btn, string id, float x,
            UnityEngine.Events.UnityAction<string> onSubChanged)
        {
            _subButtons.Add(btn);
            _subIds.Add(id);
            var rt = (RectTransform)btn.transform;
            TwinUIStyle.Row(rt, x, 2f, 64f, 24f);
            btn.Button.onClick.AddListener(() => onSubChanged?.Invoke(id));
        }

        public void SetSub(string subId)
        {
            _subId = subId;
            for (int i = 0; i < _subButtons.Count; i++)
            {
                _subButtons[i].SetSelected(_subIds[i] == subId);
            }
        }

        public static string DefaultSub(TwinUiWorkspace workspace)
        {
            if (workspace == null || !workspace.HasSubNav)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(workspace.DefaultSubNav)
                ? workspace.SubNavIds[0]
                : workspace.DefaultSubNav;
        }

        public void AttachCard(TwinUiModule module, TwinUiCardView card)
        {
            if (module == null || card == null)
            {
                return;
            }

            _modules.Add(module);
            _cards.Add(card);
        }

        public void ClearCards()
        {
            _modules.Clear();
            _cards.Clear();
            _entries.Clear();
        }

        /// <summary>按内容宽度重排页头/二级导航/卡片；幂等。</summary>
        public void Apply(float width)
        {
            if (_root == null)
            {
                return;
            }

            _width = Mathf.Max(1f, width);
            ApplyCount++;
            const float side = TwinUIStyle.PagePadX;
            float inner = Mathf.Max(200f, _width - side * 2f);
            _root.anchoredPosition = new Vector2(0f, 0f);
            TwinUIStyle.Row(_header, side, TwinUIStyle.PagePadTop, inner, 88f);

            // 页头：标题/副标题按可用宽度收放。
            _title.rectTransform.sizeDelta = new Vector2(inner - 140f, 28f);
            _subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Min(640f, inner - 140f), 34f);

            float top = TwinUIStyle.PagePadTop + 88f;
            _connActive = _workspace != null && _workspace.Id == TwinUiWorkspaceRegistry.Twin;
            if (_connStrip != null)
            {
                _connStrip.gameObject.SetActive(_connActive);
            }

            if (_connActive)
            {
                TwinUIStyle.Row(_connStrip, side, top, inner, ConnectionStripHeight);
                if (_connDot != null)
                {
                    TwinUIStyle.Row(_connDot.rectTransform, 0f, 7f, 6f, 6f);
                }

                if (_connText != null)
                {
                    TwinUIStyle.Row(_connText.rectTransform, 10f, 0f, Mathf.Max(120f, inner - 10f),
                        ConnectionStripHeight);
                }

                top += ConnectionStripHeight + TwinUIStyle.Space8;
            }

            // 本批（A1）：数字孪生页常驻"数据源"控制条（与连接状态条同页同宽）。
            _srcActive = _connActive;
            if (_srcBar != null)
            {
                _srcBar.gameObject.SetActive(_srcActive);
            }

            if (_srcActive)
            {
                TwinUIStyle.Row(_srcBar, side, top, inner, SourceBarHeight);
                LayoutSourceBar(inner);
                top += SourceBarHeight + TwinUIStyle.Space8;
            }

            bool hasSub = _workspace != null && _workspace.HasSubNav;
            _subNav.gameObject.SetActive(hasSub);
            if (hasSub)
            {
                var srt = (RectTransform)_subNav;
                TwinUIStyle.Row(srt, side, top, inner, SubNavHeight);
                SetSub(_subId);
                top += SubNavHeight + TwinUIStyle.Space8;
            }

            var crt = (RectTransform)_cardArea;
            TwinUIStyle.Row(crt, side, top, inner, 100f);

            // P10：先按 C 定列，再让模块测高，最后排版；响应式卡 localScale 恒为 1。
            float c = Mathf.Max(200f, inner - TwinUiLayoutContract.ScrollbarGutter);
            _lastC = c;
            _columns = _columns <= 0
                ? TwinUiLayoutContract.InitialColumns(c)
                : TwinUiLayoutContract.Columns(c, _columns);

            _entries.Clear();
            int visible = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                var module = _modules[i];
                if (card == null || module == null)
                {
                    continue;
                }

                bool show = !hasSub || string.IsNullOrEmpty(_subId) || _subId == "all"
                    || string.IsNullOrEmpty(module.Group) || module.Group == _subId;
                card.gameObject.SetActive(show);
                if (!show)
                {
                    continue;
                }

                visible++;
                var rt = card.Root;
                rt.SetParent(_cardArea, false);

                bool wide = module.WideContent && _columns >= 2;
                float cardWidth = wide
                    ? TwinUiLayoutContract.WideWidth(c)
                    : TwinUiLayoutContract.ColumnWidth(c, _columns);
                float contentWidth = TwinUiLayoutContract.ContentWidth(cardWidth);
                if (module.State != TwinUiModuleState.Floating)
                {
                    TwinUiContentFit.Attach(module);
                    var contentFit = module.View != null ? module.View.GetComponent<TwinUiContentFit>() : null;
                    if (contentFit != null) contentFit.Invalidate();
                    if (module.ResponsiveContent)
                        module.ApplyContentLayout(contentWidth, TwinUiModuleSurface.Card);
                }

                _entries.Add(new TwinUiCardGrid.Entry
                {
                    Rect = rt,
                    Height = module.ResponsiveContent
                        ? TwinUiLayoutContract.CardHeight(module, contentWidth, TwinUiModuleSurface.Card)
                        : TwinUiContentFit.LegacyCardHeight(contentWidth, module),
                    Wide = wide,
                    // P4：V2 主面板所有卡片都按列宽铺满（localScale 恒 1），不再整卡缩放。
                    Responsive = true,
                    DesignSize = new Vector2(Mathf.Max(200f, module.CardSize.x),
                        Mathf.Max(160f, module.CardSize.y))
                });
            }

            float gridHeight = TwinUiCardGrid.LayoutResponsive(c, _columns, _entries);

            if (visible == 0)
            {
                EnsureEmpty(inner);
            }
            else if (_empty != null)
            {
                _empty.gameObject.SetActive(false);
            }

            float cardAreaHeight = Mathf.Max(visible == 0 ? 72f : 120f, gridHeight);
            crt.sizeDelta = new Vector2(inner, cardAreaHeight);
            _height = top + cardAreaHeight + BottomPad;
            // Horizontal stretch already supplies the parent's width. Adding _width
            // here doubles the page and shifts its left edge behind the viewport mask.
            _root.sizeDelta = new Vector2(0f, _height);
        }

        private void EnsureEmpty(float inner)
        {
            if (_empty == null)
            {
                _empty = TwinUIStyle.Text(_cardArea, "EmptyPage", TwinUITextR3.CardEmptyHint,
                    TwinUIStyle.FontBodySm, TwinUIStyle.QuietColor);
                TwinUIStyle.Row(_empty.rectTransform, 0f, 8f, inner, 40f);
            }

            _empty.gameObject.SetActive(true);
        }

        public void ApplyThemeColors()
        {
            if (_kicker != null)
            {
                _kicker.color = TwinUIStyle.QuietColor;
            }

            if (_connText != null)
            {
                _connText.color = TwinUITheme.ToneTextColor(_connTone);
            }

            if (_connDot != null)
            {
                _connDot.color = TwinUITheme.ToneColor(_connTone);
            }

            if (_title != null)
            {
                _title.color = TwinUIStyle.TextColor;
            }

            if (_subtitle != null)
            {
                _subtitle.color = TwinUIStyle.MutedColor;
            }

            if (_status.Text != null && _workspace != null)
            {
                _status.Text.text = _workspace.StatusText;
                _status.Text.color = TwinUITheme.ToneTextColor(_workspace.StatusTone);
                if (_status.Dot != null)
                {
                    _status.Dot.color = TwinUITheme.ToneColor(_workspace.StatusTone);
                }
            }

            for (int i = 0; i < _subButtons.Count; i++)
            {
                _subButtons[i].ApplyTheme();
            }

            if (_srcText != null)
            {
                _srcText.color = TwinUIStyle.QuietColor;
            }

            if (_recordText != null)
            {
                _recordText.color = TwinUIStyle.MutedColor;
            }

            if (_displayText != null)
            {
                _displayText.color = TwinUIStyle.MutedColor;
            }

            if (_windowText != null)
            {
                _windowText.color = TwinUIStyle.MutedColor;
            }

            if (_srcTitleLabel != null)
            {
                _srcTitleLabel.color = TwinUIStyle.MutedColor;
            }

            for (int i = 0; i < _hzButtons.Length; i++)
            {
                if (_hzButtons[i] != null)
                {
                    _hzButtons[i].ApplyTheme();
                }
            }

            for (int i = 0; i < _winButtons.Length; i++)
            {
                if (_winButtons[i] != null)
                {
                    _winButtons[i].ApplyTheme();
                }
            }

            if (_btnPause != null)
            {
                _btnPause.ApplyTheme();
            }

            if (_btnDetail != null)
            {
                _btnDetail.ApplyTheme();
            }

            if (_detailText != null)
            {
                _detailText.color = TwinUIStyle.TextColor;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].ApplyThemeColors();
            }
        }
    }
}
