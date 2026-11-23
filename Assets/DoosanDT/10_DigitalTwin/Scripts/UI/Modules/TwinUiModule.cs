// TwinUiModule.cs —— 主面板模块/卡片契约（P4 / task-12，P8 扩展为"卡片"表面）
// 设计目标：与具体业务解耦；宿主只认这个抽象，后续新增模块（性能/关节/同步…）只需实现它。
// 依赖：仅 UnityEngine；视图可自由使用 TwinUIStyle / TwinUITheme 令牌，但不要在模块内另建主题体系。
// 生命周期：Build(一次/每表面一次) → OnAttach(每次显示) → Refresh(宿主驱动) → OnDetach(每次隐藏) → DestroyView(Hidden/卸载)
// P8 变更：State 增加 Card（页内卡片，替代旧 Docked 语义）；新增 WorkspaceId/CardSize/Kicker 等元数据；
//          内容视图仍是**单一实例**——拆出只是换父节点，见 TwinUiSurfaceHost。旧 Docked 值保留给 v1 存档迁移。
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>模块展示状态。数值持久化在 PlayerPrefs，不要随意改枚举值。</summary>
    public enum TwinUiModuleState
    {
        Hidden = 0,

        /// <summary>旧底部 dock 条卡片（P4–P6）。V2 不再产生，仅用于 v1 存档迁移。</summary>
        Docked = 1,
        Floating = 2,

        /// <summary>页内卡片（V2 默认表面）。</summary>
        Card = 3
    }

    /// <summary>模块默认停靠槽位；V2 不再使用，保留给旧存档字段兼容。</summary>
    public enum TwinUiModuleSlot
    {
        Bottom = 0,
        Right = 1
    }

    /// <summary>内容渲染表面：卡片（页内）与浮窗共用同一份内容根，只换父节点与内边距。</summary>
    public enum TwinUiModuleSurface
    {
        Card = 0,
        Floating = 1
    }

    /// <summary>
    /// 模块契约。宿主负责外壳（卡片 / 浮窗 / 任务栏），模块只负责内容与数据。
    /// 注意：Build 可能被调用多次（不同表面/重进页面），实现需可重入；View 由宿主销毁。
    /// </summary>
    public abstract class TwinUiModule
    {
        /// <summary>稳定 id（持久化键，改 id = 丢用户状态）。</summary>
        public abstract string Id { get; }

        /// <summary>显示名（卡片标题 / 浮窗标题）。</summary>
        public abstract string Title { get; }

        /// <summary>卡片/窗口标题后缀（可空）。</summary>
        public virtual string Subtitle => string.Empty;

        /// <summary>英文小标（页面里的"数字孪生 DIGITAL TWIN"式层级感；可空）。</summary>
        public virtual string Kicker => string.Empty;

        /// <summary>归属页签 id（见 TwinUiWorkspaceRegistry）；空 = 不进页面，只能从窗口菜单打开。</summary>
        public virtual string WorkspaceId => string.Empty;

        /// <summary>次级分组（页内 SubNav 过滤；空 = 恒显示）。</summary>
        public virtual string Group => string.Empty;

        /// <summary>卡片建议尺寸（宽 × 高，像素）；高度参与瀑布流估算。</summary>
        public virtual Vector2 CardSize => new Vector2(320f, 190f);

        /// <summary>卡片可压缩到的最小尺寸（窄窗单列时用）。</summary>
        public virtual Vector2 CardMinSize => new Vector2(240f, 150f);

        /// <summary>浮窗最小尺寸。</summary>
        public virtual Vector2 FloatingMinSize => new Vector2(300f, 180f);

        /// <summary>是否允许"拆出"为浮窗。</summary>
        public virtual bool Detachable => true;

        /// <summary>是否允许关闭（V2 的 19 张卡均未声明；未声明时不渲染 close，避免假按钮）。</summary>
        public virtual bool Closable => false;

        /// <summary>宿主累计调用 OnAttach 的次数（仅内存，自检用；悬浮/停靠不应增加）。</summary>
        public int HostAttachCount { get; internal set; }

        public virtual TwinUiModuleSlot DefaultSlot => TwinUiModuleSlot.Bottom;

        /// <summary>旧字段兼容（P4–P6 dock 卡片用）；V2 用 CardSize。</summary>
        public virtual Vector2 CompactSize => CardSize;

        /// <summary>旧字段兼容（P4–P6 sunk 卡片用）；V2 用 CardSize。</summary>
        public virtual Vector2 ExpandSize => new Vector2(420f, 320f);

        /// <summary>当前状态（由宿主维护，模块只读）。</summary>
        public TwinUiModuleState State { get; internal set; }

        /// <summary>当前内容表面（由宿主维护，模块只读）。</summary>
        public TwinUiModuleSurface Surface { get; internal set; }

        /// <summary>是否展开（紧凑/展开由每个模块自己解释，宿主只负责按钮与持久化）。</summary>
        public bool IsExpanded { get; internal set; }

        /// <summary>当前视图内容根（Card 时是卡片内容区，Floating 时是浮窗内容区）。</summary>
        public RectTransform View { get; internal set; }

        /// <summary>
        /// 构建内容视图；parent 由宿主提供，尺寸由模块自行锚定（建议四边拉伸）。
        /// 旧签名保留：floating=true 等价于 TwinUiModuleSurface.Floating。
        /// </summary>
        public abstract RectTransform Build(Transform parent, bool floating);

        /// <summary>按表面类型构建（默认转调旧签名，新卡片只需实现旧签名即可）。</summary>
        public virtual RectTransform Build(Transform parent, TwinUiModuleSurface surface)
        {
            Surface = surface;
            return Build(parent, surface == TwinUiModuleSurface.Floating);
        }

        /// <summary>数据刷新（Play 中宿主按固定间隔调用；实现内部做节流）。</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>模块被显示（Card/Floating）时调用：适合接管旧面板显示、订阅数据。</summary>
        public virtual void OnAttach()
        {
        }

        /// <summary>模块被隐藏时调用：适合还原旧面板显示、释放订阅。</summary>
        public virtual void OnDetach()
        {
        }

        public virtual void OnExpandChanged(bool expanded)
        {
        }

        /// <summary>表面切换（卡片 ↔ 浮窗）时调用：用于调整内边距/尺寸敏感的子布局。</summary>
        public virtual void OnSurfaceChanged(TwinUiModuleSurface surface)
        {
        }

        // ---------------------------------------------------------------- P10 响应式契约
        // 语义（2026-11-08 冻结，见 agent_runs/dsh_2026-11-08_ui_v2_plan/执行计划_修订版.md §3.1）：
        //   ResponsiveContent=false → 走 Legacy 路径（整卡 localScale / DesignScaler），19 张既有卡默认值不变。
        //   ResponsiveContent=true  → 宿主按内容宽测高、按内容宽重排，禁止任何局部缩放。
        // 硬规则：MeasureContentHeight 必须是纯函数——不写 Transform、不 Instantiate/Destroy、不 SetActive、
        //         不读业务数据、不改缓存序列、不发事件；返回值必须有限且在 [0, 4000]。

        /// <summary>是否采用新响应式契约（默认 false = Legacy 整卡缩放，保证既有卡片零改动）。</summary>
        public virtual bool ResponsiveContent => false;

        /// <summary>是否希望在两列布局下跨满整行（曲线/长表类）。</summary>
        public virtual bool WideContent => false;

        /// <summary>
        /// 测量内容区在给定宽度下需要的高度（不含卡片标题栏与内边距）。
        /// contentWidth = 卡宽 - 24（卡片）或 浮窗宽 - 24（浮窗）。纯函数，可被高频调用。
        /// </summary>
        public virtual float MeasureContentHeight(float contentWidth, TwinUiModuleSurface surface)
        {
            float fallback = CardSize.y - TwinUiCardView.ChromeHeight;
            return fallback > 0f ? fallback : 96f;
        }

        /// <summary>按内容宽重排自身子节点（只允许动本模块 View 子树；禁止改卡片根尺寸/缩放）。</summary>
        public virtual void ApplyContentLayout(float contentWidth, TwinUiModuleSurface surface)
        {
        }

        /// <summary>内容结构变化（显示/隐藏区块、换单位、文案长度结构变化）时调用，使高度缓存失效。</summary>
        public virtual void InvalidateContentLayout()
        {
            _layoutVersion++;
        }

        /// <summary>布局版本号（缓存键之一）。</summary>
        public int LayoutVersion => _layoutVersion;

        private int _layoutVersion;

        /// <summary>销毁视图（Hidden 或宿主销毁时）。宿主先调 OnDetach。</summary>
        public virtual void DestroyView()
        {
            if (View != null)
            {
                Object.Destroy(View.gameObject);
                View = null;
            }
        }
    }
}