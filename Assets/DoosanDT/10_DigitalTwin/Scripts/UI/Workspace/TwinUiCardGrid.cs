// TwinUiCardGrid.cs —— 页内卡片排版（P10：真正的 1/2 列响应式，取消整卡缩放）
// 变更（2026-11-08，用户批准）：V2.0 §04.3/F-10 要求普通卡最多两列、禁止用局部 localScale 冒充响应式。
//   新路径 LayoutResponsive()：列宽即卡宽，高度由模块测量（TwinUiLayoutContract.CardHeight），
//   Wide 卡跨满整行并另起新行；响应式卡 localScale 恒为 1。
//   旧 1–4 列 + localScale 路径（ScaleFor/ColumnsFor/Layout）仅保留给 Legacy 卡与 UI/53 历史断言，
//   新代码不得再调用；等 Legacy 卡逐页迁移完成后删除。
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiCardGrid
    {
        // ---------------------------------------------------------------- Legacy 常量（勿新增引用）
        public const float Gap = 8f;
        public const float DesignWidth = 360f;
        public const float MaxCardWidth = 468f;
        public const float MinScale = 0.62f;
        public const float MaxScale = 1.40f;

        /// <summary>Legacy 1–4 列断点（仅 UI/53 历史断言与旧版浮窗设计尺寸使用）。</summary>
        public static int ColumnsFor(float width)
        {
            if (width >= 1560f)
            {
                return 4;
            }

            if (width >= 1160f)
            {
                return 3;
            }

            return width >= 760f ? 2 : 1;
        }

        public static float ColumnWidth(float width, int columns)
        {
            int n = Mathf.Max(1, columns);
            float usable = Mathf.Max(160f, width - Gap * (n - 1));
            return usable / n;
        }

        public static float ScaleFor(float cardWidth, float designWidth = DesignWidth)
        {
            float dw = designWidth > 1f ? designWidth : DesignWidth;
            return Mathf.Clamp(cardWidth / dw, MinScale, MaxScale);
        }

        // ---------------------------------------------------------------- 响应式排版（P10 唯一入口）

        /// <summary>
        /// 按契约排版：1/2 列、Wide 跨行、行内等高对齐、整行居中。
        /// 返回内容总高度。apply=false 时只计算不移动（自检用）。
        /// </summary>
        public static float LayoutResponsive(float c, int columns, IList<Entry> entries, bool apply = true)
        {
            if (entries == null || entries.Count == 0)
            {
                return 0f;
            }

            int cols = columns >= 2 ? 2 : 1;
            float gap = TwinUiLayoutContract.Gap;
            float rowGap = TwinUiLayoutContract.RowGap;
            float columnW = TwinUiLayoutContract.ColumnWidth(c, cols);

            float y = 0f;
            int index = 0;
            int row = 0;

            while (index < entries.Count)
            {
                // Wide 卡：两列时独占一行，宽度 = C
                Entry first = entries[index];
                if (cols >= 2 && first.Wide)
                {
                    float wideW = TwinUiLayoutContract.WideWidth(c);
                    float wideH = TwinUiLayoutContract.ClampCardHeight(first.Height);
                    first.Row = row;
                    first.Column = 0;
                    first.CardWidth = wideW;
                    first.Scale = 1f;
                    first.X = wideW * 0.5f;
                    first.Y = y;
                    if (apply && first.Rect != null)
                    {
                        ApplyRect(first, wideW, wideH, wideW * 0.5f, y);
                    }

                    index++;
                    row++;
                    y += wideH + rowGap;
                    continue;
                }

                // 本行条目数：不超过列数，且被 Wide 卡截断
                int count = Mathf.Min(cols, entries.Count - index);
                for (int k = 0; k < count; k++)
                {
                    if (entries[index + k].Wide)
                    {
                        count = k;
                        break;
                    }
                }

                if (count <= 0)
                {
                    count = 1;
                }

                float rowHeight = 0f;
                for (int i = 0; i < count; i++)
                {
                    rowHeight = Mathf.Max(rowHeight, VisualHeight(entries[index + i], columnW));
                }

                float visualWidth = count * columnW + (count - 1) * gap;
                float startX = 0f;

                for (int i = 0; i < count; i++)
                {
                    Entry e = entries[index + i];
                    float cellCenter = startX + i * (columnW + gap) + columnW * 0.5f;
                    e.Row = row;
                    e.Column = i;
                    e.CardWidth = columnW;
                    e.Y = y;

                    if (e.Responsive)
                    {
                        e.Scale = 1f;
                        e.X = cellCenter;
                        if (apply && e.Rect != null)
                        {
                            ApplyRect(e, columnW, rowHeight, cellCenter, y);
                        }
                    }
                    else
                    {
                        // Legacy：设计尺寸 + 等比缩放，列内水平居中、行内垂直居中（按缩放后的视觉高算）。
                        float designW = e.DesignSize.x > 1f ? e.DesignSize.x : DesignWidth;
                        float designH = e.DesignSize.y > 1f ? e.DesignSize.y : 220f;
                        float scale = ScaleFor(columnW, designW);
                        e.Scale = scale;
                        e.X = cellCenter;
                        if (apply && e.Rect != null)
                        {
                            // 顶对齐（与响应式卡同一条行基线）；行高已按缩放后视觉高取最大，故不会压行。
                            ApplyRect(e, designW, designH, cellCenter, y);
                            e.Rect.localScale = new Vector3(scale, scale, 1f);
                        }
                    }
                }

                index += count;
                row++;
                y += rowHeight + rowGap;
            }

            return Mathf.Max(0f, y - rowGap);
        }

        /// <summary>卡片在该列宽下的视觉高度（Legacy 卡必须乘缩放，否则行高不足会压住下一行）。</summary>
        private static float VisualHeight(Entry e, float columnWidth)
        {
            if (e.Responsive)
            {
                return TwinUiLayoutContract.ClampCardHeight(e.Height);
            }

            float designW = e.DesignSize.x > 1f ? e.DesignSize.x : DesignWidth;
            float designH = e.DesignSize.y > 1f ? e.DesignSize.y : 220f;
            return designH * ScaleFor(columnWidth, designW);
        }

        private static void ApplyRect(Entry e, float width, float height, float centerX, float y)
        {
            RectTransform rt = e.Rect;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(centerX, -y);
            if (e.Responsive)
            {
                rt.localScale = Vector3.one;
            }
        }

        public static float MeasureResponsive(float c, int columns, IList<Entry> entries)
        {
            return LayoutResponsive(c, columns, entries, false);
        }

        public sealed class Entry
        {
            public RectTransform Rect;

            /// <summary>整卡高（响应式=契约算出的卡高；Legacy=CardSize.y）。</summary>
            public float Height;

            /// <summary>是否跨满整行（两列时生效）。</summary>
            public bool Wide;

            /// <summary>是否走新响应式路径（localScale 恒 1）。</summary>
            public bool Responsive;

            /// <summary>Legacy 卡设计尺寸。</summary>
            public Vector2 DesignSize;

            public int Row;
            public int Column;
            public float Scale;
            public float CardWidth;
            public float X;
            public float Y;
        }
    }
}
