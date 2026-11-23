// TwinUiLayoutContract.cs —— P10 响应式尺寸契约（唯一权威数值）
// 背景：V2.0 规范 §04 要求"容器测宽 → 断点 → 内容测高 → 内层重排"，禁止整卡 localScale。
//       P9.5 的 1–4 列整卡缩放（TwinUiCardGrid.ScaleFor）仅作为 Legacy 兼容保留，不再用于新卡。
// 边界：本类只做纯计算，不持有 RectTransform、不订阅数据、不写场景。
// 数值来源：agent_runs/dsh_2026-11-08_ui_v2_plan/执行计划_修订版.md §3.2（经用户 2026-11-08 批准）。
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>模块内部密度模式（由内容宽决定，不跟随主窗断点）。</summary>
    public enum TwinUiCardMode
    {
        Compact = 0,
        Normal = 1,
        Expanded = 2
    }

    public static class TwinUiLayoutContract
    {
        // ------------------------------------------------------------- 断点（C = 页内可排版宽）
        /// <summary>1 列 → 2 列 的进入阈值（滞回上沿）。</summary>
        public const float GrowToTwoColumns = 820f;

        /// <summary>2 列 → 1 列 的退出阈值（滞回下沿）。</summary>
        public const float ShrinkToOneColumn = 780f;

        // ------------------------------------------------------------- 卡内模式
        public const float ModeCompactBelow = 420f;
        public const float ModeExpandedFrom = 720f;

        // ------------------------------------------------------------- 间距/尺寸
        /// <summary>卡片列间距（V2.0 §04.3）。行距同值。</summary>
        public const float Gap = 12f;

        public const float RowGap = 12f;

        /// <summary>
        /// 滚动条槽预留：本工程 Viewport 用 RectMask2D + ScrollRect，无可见 Scrollbar，
        /// 内容水平锚定拉伸，因此 C 不会因滚动条出现而抖动（实测 Shell/TwinUiShellV2.BuildViewport）。
        /// 若将来补上可见滚动条，把此常量改为 10 即可，其余逻辑无需改。
        /// </summary>
        public const float ScrollbarGutter = 0f;

        public const float MinContentHeight = 96f;
        public const float MaxContentHeight = 4000f;
        public const float MinCardHeight = 150f;
        public const float MaxCardHeight = 1200f;

        /// <summary>页内可排版宽度 C（= 页宽 - 两侧内边距 - 滚动条槽）。</summary>
        public static float C(float pageWidth)
        {
            return Mathf.Max(200f, pageWidth - 2f * TwinUIStyle.PagePadX - ScrollbarGutter);
        }

        /// <summary>首次进入（新建页/恢复布局）时的列数。</summary>
        public static int InitialColumns(float c)
        {
            return c >= GrowToTwoColumns ? 2 : 1;
        }

        /// <summary>带滞回的列数：已 1 列则需 ≥820 才升；已 2 列则 &lt;780 才降。等于阈值时保持。</summary>
        public static int Columns(float c, int current)
        {
            if (current >= 2)
            {
                return c < ShrinkToOneColumn ? 1 : 2;
            }

            return c >= GrowToTwoColumns ? 2 : 1;
        }

        public static float ColumnWidth(float c, int columns)
        {
            if (columns >= 2)
            {
                return Mathf.Max(120f, (c - Gap) * 0.5f);
            }

            return Mathf.Max(120f, c);
        }

        /// <summary>跨列卡（WideChart）占满整行时的宽度。</summary>
        public static float WideWidth(float c)
        {
            return Mathf.Max(160f, c);
        }

        public static TwinUiCardMode ModeFor(float contentWidth)
        {
            if (contentWidth < ModeCompactBelow)
            {
                return TwinUiCardMode.Compact;
            }

            return contentWidth >= ModeExpandedFrom ? TwinUiCardMode.Expanded : TwinUiCardMode.Normal;
        }

        /// <summary>把测量值夹到合法区间；NaN/Inf/负值一律回落最小高（禁止用重建掩盖排版问题）。</summary>
        public static float ClampContentHeight(float height)
        {
            if (float.IsNaN(height) || float.IsInfinity(height) || height <= 0f)
            {
                return MinContentHeight;
            }

            return Mathf.Clamp(height, MinContentHeight, MaxContentHeight);
        }

        /// <summary>内容区宽（卡宽 - 左右内边距）。</summary>
        public static float ContentWidth(float cardWidth)
        {
            return Mathf.Max(80f, cardWidth - 2f * TwinUiCardView.ContentPadX);
        }

        /// <summary>整卡高 = 边框（标题+间隙+下内边）+ 内容测量高，带上下限。</summary>
        public static float CardHeight(TwinUiModule module, float contentWidth, TwinUiModuleSurface surface)
        {
            if (module == null)
            {
                return MinCardHeight;
            }

            if (!module.ResponsiveContent)
            {
                return ClampCardHeight(module.CardSize.y);
            }

            float measured = ClampContentHeight(module.MeasureContentHeight(contentWidth, surface));
            return ClampCardHeight(TwinUiCardView.ChromeHeight + measured);
        }

        public static float ClampCardHeight(float height)
        {
            if (float.IsNaN(height) || float.IsInfinity(height) || height <= 0f)
            {
                return MinCardHeight;
            }

            return Mathf.Clamp(height, MinCardHeight, MaxCardHeight);
        }

        // ------------------------------------------------------------- 样板卡数值（§3.3，UI/54 实测可调）
        /// <summary>Joints/Torque：图表高与数值网格参数。</summary>
        public static void SampleChartMetrics(TwinUiCardMode mode, out float chartHeight, out int gridColumns,
            out float rowStep)
        {
            switch (mode)
            {
                case TwinUiCardMode.Compact:
                    chartHeight = 72f;
                    gridColumns = 2;
                    rowStep = 20f;
                    return;
                case TwinUiCardMode.Expanded:
                    chartHeight = 128f;
                    gridColumns = 3;
                    rowStep = 24f;
                    return;
                default:
                    chartHeight = 112f;
                    gridColumns = 2;
                    rowStep = 22f;
                    return;
            }
        }

        /// <summary>Joints/Torque 的骨架几何：徽标 16 / 图表顶 24 / 图后 8 间隙 / 网格行高。</summary>
        public const float SampleBadgeHeight = 16f;
        public const float SampleChartTop = 24f;
        public const float SampleChartGap = 8f;
        public const float SampleCellGap = 8f;
        public const float SampleCellLabelWidth = 12f;
        public const float SampleCellValueOffset = 40f;

        /// <summary>TCP：三档行高——Compact 单列 6 行用 22；Normal/Expanded 两列网格用 24。</summary>
        public static float TcpRowStep(TwinUiCardMode mode)
        {
            return mode == TwinUiCardMode.Compact ? 22f : 24f;
        }

        public const float TcpBadgeHeight = 16f;
        public const float TcpTop = 24f;
        public const float TcpCaptionHeight = 16f;
    }
}
