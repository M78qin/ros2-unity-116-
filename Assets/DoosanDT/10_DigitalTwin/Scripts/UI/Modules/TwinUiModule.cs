// TwinUiModule.cs —— 主面板模块契约（P4 / task-12）
// 设计目标：与具体业务解耦；宿主只认这个抽象，后续新增模块（性能/任务/日志…）只需实现它。
// 依赖：仅 UnityEngine；视图可自由使用 TwinUIStyle / TwinUITheme 令牌，但不要在模块内另建主题体系。
// 生命周期：Build(一次) → OnAttach(每次显示) → Refresh(宿主驱动) → OnDetach(每次隐藏) → DestroyView(Hidden/卸载)
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>模块展示状态。数值持久化在 PlayerPrefs（TwinUI.Modules.v1），不要随意改枚举值。</summary>
    public enum TwinUiModuleState
    {
        Hidden = 0,
        Docked = 1,
        Floating = 2
    }

    /// <summary>模块默认停靠槽位；宿主当前只实现 Bottom（底部 dock 条），Right 预留给后续竖排 dock。</summary>
    public enum TwinUiModuleSlot
    {
        Bottom = 0,
        Right = 1
    }

    /// <summary>
    /// 模块契约。宿主负责外壳（dock 条 / 悬浮窗），模块只负责内容与数据。
    /// 注意：Build 可能被调用两次（Docked 与 Floating 外壳不同），实现需可重入；View 由宿主销毁。
    /// </summary>
    public abstract class TwinUiModule
    {
        /// <summary>稳定 id（持久化键，改 id = 丢用户状态）。</summary>
        public abstract string Id { get; }

        /// <summary>显示名。</summary>
        public abstract string Title { get; }

        /// <summary>卡片/窗口标题后缀（可空）。</summary>
        public virtual string Subtitle => string.Empty;

        public virtual TwinUiModuleSlot DefaultSlot => TwinUiModuleSlot.Bottom;

        /// <summary>紧凑态尺寸（dock 一行 / 悬浮窗初始尺寸参考）。</summary>
        public virtual Vector2 CompactSize => new Vector2(300f, 56f);

        /// <summary>展开态尺寸（悬浮窗初始尺寸参考；dock 条会按内容高度自适应）。</summary>
        public virtual Vector2 ExpandSize => new Vector2(420f, 320f);

        /// <summary>当前状态（由宿主维护，模块只读）。</summary>
        public TwinUiModuleState State { get; internal set; }

        /// <summary>是否展开（紧凑/展开由每个模块自己解释，宿主只负责按钮与持久化）。</summary>
        public bool IsExpanded { get; internal set; }

        /// <summary>当前视图内容根（Docked 时是 dock 卡片内容，Floating 时是悬浮窗内容）。</summary>
        public RectTransform View { get; internal set; }

        /// <summary>构建内容视图；parent 由宿主提供的容器，尺寸由模块自行锚定（建议四边拉伸）。</summary>
        public abstract RectTransform Build(Transform parent, bool floating);

        /// <summary>数据刷新（Play 中宿主按固定间隔调用；实现内部做节流）。</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>模块被显示（Docked/Floating）时调用：适合接管旧面板显示、订阅数据。</summary>
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