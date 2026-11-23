// TwinUiWidgetTcp.cs —— TCP 位姿卡片（P8 初版；P10.4 响应式样板卡）
// 数据源：场景内 DigitalTwinRuntime（只读）→ HasLatestFrame / LatestFrame：
//         TcpPositionMeters（m→mm）、TcpRotation.eulerAngles（RPY）、HasTcpPose。
//
// 响应式（P10.4，ResponsiveContent=true）：
//   * 断点由内容宽决定（TwinUiLayoutContract.ModeFor）：Compact <420 / Normal 420–719 / Expanded ≥720；
//   * Compact：单列 6 行，行高 22；
//   * Normal ：两列 × 三行（左列 X/Y/Z，右列 R/P/Y），行高 24；
//   * Expanded：同 Normal 网格 + 末行"坐标系 / 来源"说明（16 高，静态说明，不含实测值）；
//   * 全程**不使用 localScale**：宿主按内容宽测高，卡内只重设已存在节点的位置与尺寸（不 Destroy/Instantiate）；
//   * MeasureContentHeight 是纯函数（不写 Transform、不读业务数据），ApplyContentLayout 幂等（同 mode+同宽直接跳过）。
//
// 数据逻辑与 P8 完全一致（一行未改）：无 runtime / 无帧 / 未订阅位姿三条占位分支与徽标文案原样保留；
// 单位仍是 mm 与 °；禁止补 0；不新增订阅、不下发命令、不读写文件。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetTcp : TwinUiModule
    {
        public const string ModuleId = "tcp";

        private const int AxisCount = 6;
        private const int RowsPerColumn = 3;

        /// <summary>两列模式下的列间距（px）。</summary>
        private const float CellGap = 8f;

        /// <summary>Expanded 末行说明的高度（TwinUiLayoutContract 的 +2 +16 中的 16）。</summary>
        private const float FrameNoteHeight = 16f;

        /// <summary>Expanded 末行与上一行的额外间隙（契约里的 +2）。</summary>
        private const float FrameNoteGap = 2f;

        private static readonly string[] AxisLabels = { "X", "Y", "Z", "R", "P", "Y" };

        private DigitalTwinRuntime _runtime;
        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private readonly TwinUiCardParts.Kv[] _rows = new TwinUiCardParts.Kv[AxisCount];
        private readonly RectTransform[] _rowRoots = new RectTransform[AxisCount];
        private TMP_Text _caption;
        private TMP_Text _frameNote;
        private RectTransform _badgeRoot;

        public override string Id => ModuleId;
        public override string Title => "TCP 位姿";
        public override string Kicker => "TCP";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override string Group => "tcp";

        /// <summary>设计尺寸 = 卡片骨架 54 + 内容 168（Compact 档位一列六行 + 说明行）。</summary>
        public override Vector2 CardSize => new Vector2(360f, 222f);

        // ---------------------------------------------------------------- P10 响应式契约

        public override bool ResponsiveContent => true;

        /// <summary>最近一次生效的卡内模式（自检/审计用）。</summary>
        public TwinUiCardMode LastMode { get; private set; }

        /// <summary>最近一次生效的内容宽（自检/审计用）。</summary>
        public float LastContentWidth { get; private set; } = -1f;

        public override float MeasureContentHeight(float contentWidth, TwinUiModuleSurface surface)
        {
            TwinUiCardMode mode = TwinUiLayoutContract.ModeFor(contentWidth);
            float rowStep = RowStepFor(mode);
            if (mode == TwinUiCardMode.Compact)
            {
                // 24 + 6*22 + 2 + 16 = 174
                return TwinUiLayoutContract.TcpTop
                    + rowStep * AxisCount
                    + FrameNoteGap
                    + TwinUiLayoutContract.TcpCaptionHeight;
            }

            // Normal：24 + 3*24 + 2 + 16 = 114
            float normal = TwinUiLayoutContract.TcpTop
                + rowStep * RowsPerColumn
                + FrameNoteGap
                + TwinUiLayoutContract.TcpCaptionHeight;
            if (mode == TwinUiCardMode.Expanded)
            {
                // Expanded = Normal + 2 + 16 = 132（末行说明）
                return normal + FrameNoteGap + FrameNoteHeight;
            }

            return normal;
        }

        /// <summary>
        /// 行高：Compact 用单列档 22；Normal 与 Expanded 共用两列档 24
        /// （契约 TcpRowStep 对 Expanded 也返回 22，但 P10.4 要求 Expanded = Normal + 2 + 16，
        ///  因此这里显式取 Normal 的行高，避免两档数值对不上）。
        /// </summary>
        private static float RowStepFor(TwinUiCardMode mode)
        {
            return TwinUiLayoutContract.TcpRowStep(mode == TwinUiCardMode.Compact
                ? TwinUiCardMode.Compact
                : TwinUiCardMode.Normal);
        }

        public override void ApplyContentLayout(float contentWidth, TwinUiModuleSurface surface)
        {
            if (_rows[0].Label == null)
            {
                return;
            }

            float w = Mathf.Max(80f, contentWidth);
            TwinUiCardMode mode = TwinUiLayoutContract.ModeFor(w);
            if (mode == LastMode && Mathf.Approximately(LastContentWidth, w))
            {
                return;
            }

            LastMode = mode;
            LastContentWidth = w;

            float rowStep = RowStepFor(mode);
            bool twoColumn = mode != TwinUiCardMode.Compact;
            float cellWidth = twoColumn ? (w - CellGap) * 0.5f : w;

            for (int i = 0; i < AxisCount; i++)
            {
                float x = 0f;
                float y;
                if (twoColumn)
                {
                    int column = i / RowsPerColumn;
                    int row = i % RowsPerColumn;
                    x = column * (cellWidth + CellGap);
                    y = TwinUiLayoutContract.TcpTop + row * rowStep;
                }
                else
                {
                    y = TwinUiLayoutContract.TcpTop + i * rowStep;
                }

                ResizeRow(i, x, y, cellWidth, rowStep);
            }

            float rowsHeight = twoColumn ? RowsPerColumn * rowStep : AxisCount * rowStep;
            float captionY = TwinUiLayoutContract.TcpTop + rowsHeight + FrameNoteGap;
            if (_caption != null)
            {
                TwinUIStyle.Row(_caption.rectTransform, 0f, captionY, w, TwinUiLayoutContract.TcpCaptionHeight);
            }

            bool showFrameNote = mode == TwinUiCardMode.Expanded;
            if (_frameNote != null)
            {
                _frameNote.gameObject.SetActive(showFrameNote);
                if (showFrameNote)
                {
                    TwinUIStyle.Row(_frameNote.rectTransform, 0f,
                        captionY + TwinUiLayoutContract.TcpCaptionHeight + FrameNoteGap,
                        w, FrameNoteHeight);
                }
            }

            if (_badgeRoot != null)
            {
                TwinUIStyle.Row(_badgeRoot, 0f, 0f, w, TwinUiLayoutContract.TcpBadgeHeight);
            }

            if (_badge.Text != null)
            {
                TwinUIStyle.Row(_badge.Text.rectTransform, 10f, 0f, Mathf.Max(40f, w - 10f),
                    TwinUiLayoutContract.TcpBadgeHeight);
            }
        }

        /// <summary>
        /// 重设单个 Kv 行根节点与其内两个子文本的几何。
        /// 行内仍是 45%（标签）/ 55%（数值）的比例——只是按新的行宽/行高重新分配，
        /// 因为 TwinUiCardParts.Row 建行时用的是当时的绝对宽，不会跟着行根自动伸缩。
        /// </summary>
        private void ResizeRow(int index, float x, float y, float width, float height)
        {
            RectTransform root = _rowRoots[index];
            if (root == null)
            {
                return;
            }

            TwinUIStyle.Row(root, x, y, width, height);

            var kv = _rows[index];
            float labelWidth = width * 0.45f;
            float valueWidth = width - labelWidth;
            if (kv.Label != null)
            {
                TwinUIStyle.Row(kv.Label.rectTransform, 0f, 0f, labelWidth, height);
            }

            if (kv.Value != null)
            {
                TwinUIStyle.Row(kv.Value.rectTransform, labelWidth, 0f, valueWidth, height);
            }
        }

        // ---------------------------------------------------------------- 构建

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetTcp");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(80f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);
            _badgeRoot = _badge.Text != null ? _badge.Text.rectTransform.parent as RectTransform : null;

            // 先按"单列六行"的默认几何建出来，Build 末尾再由 ApplyContentLayout 统一重排。
            for (int i = 0; i < AxisCount; i++)
            {
                _rows[i] = TwinUiCardParts.Row(content,
                    TwinUiLayoutContract.TcpTop + TwinUiLayoutContract.TcpRowStep(TwinUiCardMode.Compact) * i,
                    AxisLabels[i], w);
                _rowRoots[i] = _rows[i].Label != null ? _rows[i].Label.rectTransform.parent as RectTransform : null;
            }

            _caption = TwinUiCardParts.Caption(content, "位置 mm · 姿态 °", 0f,
                TwinUiLayoutContract.TcpTop
                + TwinUiLayoutContract.TcpRowStep(TwinUiCardMode.Compact) * AxisCount + FrameNoteGap,
                w, TwinUiLayoutContract.TcpCaptionHeight);

            // Expanded 档的末行（静态说明，不含实测值）；默认隐藏，仅 Expanded 显示。
            _frameNote = TwinUiCardParts.Caption(content, "坐标系 BASE · 来源 DigitalTwinRuntime（只读）", 0f, 0f, w,
                FrameNoteHeight);
            _frameNote.gameObject.SetActive(false);

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);

            ApplyContentLayout(w, TwinUiModuleSurface.Card);
            return root;
        }

        // ---------------------------------------------------------------- 数据（P8 原样）

        public override void Refresh()
        {
            if (_rows[0].Value == null)
            {
                return;
            }

            // A 支：回放态优先走同一份回放快照（TwinUIStatusBinder.ReplayOverrideActive），
            // 不再"回放时本卡读不到任何数据"。实时路径原样保留在下面。
            if (_binder == null)
            {
                _binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder != null && _binder.ReplayOverrideActive && _binder.Current.IsReplay)
            {
                RefreshReplay(_binder.Current);
                return;
            }

            if (_caption != null)
            {
                _caption.text = "位置 mm · 姿态 °";
            }

            if (_runtime == null)
            {
                _runtime = Object.FindObjectOfType<DigitalTwinRuntime>();
            }

            if (_runtime == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 DigitalTwinRuntime");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            if (!_runtime.HasLatestFrame)
            {
                _badge.Set(TwinUITone.Unknown, "无帧");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            var frame = _runtime.LatestFrame;
            if (frame == null || !frame.HasTcpPose)
            {
                _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotSubscribed);
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            _badge.Set(TwinUITone.Ok, "已连接");

            Vector3 p = frame.TcpPositionMeters;
            Vector3 r = frame.TcpRotation.eulerAngles;
            _rows[0].Set((p.x * 1000f).ToString("F1") + " mm", TwinUITone.Ok);
            _rows[1].Set((p.y * 1000f).ToString("F1") + " mm", TwinUITone.Ok);
            _rows[2].Set((p.z * 1000f).ToString("F1") + " mm", TwinUITone.Ok);
            _rows[3].Set(r.x.ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
            _rows[4].Set(r.y.ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
            _rows[5].Set(r.z.ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
        }

        /// <summary>
        /// 回放路径：TCP **原始分量**（mm / °）原样显示，不做 m 换算、不推断坐标系与欧拉约定。
        /// 徽标承载 status 真解析结果（异常时 Crit 色 + 原值文字）与设备状态原值；不是"已连接"。
        /// </summary>
        private void RefreshReplay(TwinUIStatusBinder.Snapshot s)
        {
            TwinUITone tone = !s.StatusProvided
                ? TwinUITone.Unknown
                : (s.StatusNormal ? TwinUITone.Ok : TwinUITone.Crit);
            string badge = s.DevicePresent
                ? s.DeviceText
                : (s.StatusProvided ? s.StatusText : "回放 · status 未提供");
            if (!string.IsNullOrEmpty(s.IdentityWarning))
            {
                badge = "身份异常 · " + s.IdentityWarning;
            }

            _badge.Set(tone, Short(badge, 46));

            if (_caption != null)
            {
                _caption.text = "原始分量 mm / ° · 坐标/欧拉 UNKNOWN";
            }

            if (!s.HasFrame)
            {
                _badge.Set(TwinUITone.Unknown, "无帧");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            if (!s.HasRawTcp || s.TcpMm == null || s.TcpMm.Length < 3
                || s.TcpAbcDeg == null || s.TcpAbcDeg.Length < 3)
            {
                _badge.Set(tone, Short(TwinUITextR3.NotSubscribed + " TCP原始分量 · " + badge, 46));
                SetAll(TwinUITextR3.NotSubscribed, TwinUITone.Unknown);
                return;
            }

            _rows[0].Set(s.TcpMm[0].ToString("F1") + " mm", TwinUITone.Ok);
            _rows[1].Set(s.TcpMm[1].ToString("F1") + " mm", TwinUITone.Ok);
            _rows[2].Set(s.TcpMm[2].ToString("F1") + " mm", TwinUITone.Ok);
            _rows[3].Set(s.TcpAbcDeg[0].ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
            _rows[4].Set(s.TcpAbcDeg[1].ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
            _rows[5].Set(s.TcpAbcDeg[2].ToString("F1") + TwinUITextR3.UnitDeg, TwinUITone.Ok);
        }

        private static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text;
            }

            return text.Substring(0, Mathf.Max(1, max - 1)) + "…";
        }

        private void SetAll(string value, TwinUITone tone)
        {
            for (int i = 0; i < AxisCount; i++)
            {
                if (_rows[i].Value != null)
                {
                    _rows[i].Set(value, tone);
                }
            }
        }
    }
}