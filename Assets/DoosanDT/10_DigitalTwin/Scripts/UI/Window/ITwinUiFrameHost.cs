// ITwinUiFrameHost.cs —— TwinUiWindowFrame 的宿主抽象（P5 / task-13）
// 目的：让 P2 的窗口外壳（圆角/边框/阴影/标题栏/八向手柄/最大化按钮/活动窗描边）可被
//       主窗（TwinUiWindowHost）与模块悬浮窗（TwinUiModuleWindow）共用，避免每个 UI 另起炉灶。
// 实现方：TwinUiWindowHost（主窗）、TwinUiModuleWindow 内部适配器（悬浮窗，AllowsMaximize=false）。
using UnityEngine;

namespace DigitalTwin.UI
{
    public interface ITwinUiFrameHost
    {
        /// <summary>宿主是否已完成初始化（Frame 在 Update/指针路径里会检查）。</summary>
        bool IsReady { get; }

        /// <summary>是否允许拖动标题栏（模块悬浮窗恒 true；主窗恒 true）。</summary>
        bool AllowsDrag { get; }

        /// <summary>是否允许最大化（主窗 true；模块悬浮窗 true → 可放大到画布；false 时不创建 MAX 按钮/F11/双击）。</summary>
        bool AllowsMaximize { get; }

        /// <summary>是否允许最小化（V2 主窗 true：收进底部任务栏；模块悬浮窗 false）。</summary>
        bool AllowsMinimize { get; }

        /// <summary>是否允许隐藏（V2 主窗 true：关闭显示，任务栏可恢复；模块悬浮窗 false）。</summary>
        bool AllowsHide { get; }

        /// <summary>最小化（主窗进任务栏）。</summary>
        void Minimize();

        /// <summary>隐藏（关闭主窗显示，但不销毁状态）。</summary>
        void Hide();

        /// <summary>标题栏按钮是否由 Frame 统一生成（主窗 true；模块悬浮窗自己管理 → false，避免按钮重叠）。</summary>
        bool FrameOwnsTitleButtons { get; }

        Canvas Canvas { get; }

        RectTransform CanvasRect { get; }

        /// <summary>主窗内容根（Frame 用它定位 Header 决定标题栏高度）；悬浮窗返回 null → 用 fallback 高度。</summary>
        RectTransform Content { get; }

        /// <summary>窗口矩形（画布坐标，左下角原点）。</summary>
        Rect WindowRect { get; }

        bool IsMaximized { get; }

        /// <summary>按画布坐标平移；返回实际位移（可能被边界钳制）。</summary>
        Vector2 MoveBy(Vector2 deltaCanvas);

        /// <summary>按八向手柄缩放；返回实际尺寸变化。</summary>
        Vector2 ResizeBy(TwinUiWindowEdge edge, Vector2 deltaCanvas);

        /// <summary>拖动/缩放结束后的持久化（主窗写 PlayerPrefs 布局，悬浮窗写模块状态）。</summary>
        void SaveLayout();

        void ToggleMaximized();

        void OnFrameDragBegin();

        void OnFrameDragEnd();

        void OnFrameResizeEnd();
    }
}