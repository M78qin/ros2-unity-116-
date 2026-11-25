// TwinUiWorkspace.cs —— 一级页签（工作区）描述与注册表（P8）
// 设计目标：页签 = 数据（id/标题/小标/副标题/二级导航/卡片顺序），不是场景节点。
//           新增一页 = 在 Registry 里加一条 + 在 TwinUiWidgetCatalog 里注册卡片，不改场景、不改布局代码。
// 硬约束：页里只能放真实数据源驱动的卡片；没有数据源的卡片必须显示"未接入"，不得造数。
using System.Collections.Generic;

namespace DigitalTwin.UI
{
    /// <summary>一个一级页签（工作区）。</summary>
    public sealed class TwinUiWorkspace
    {
        public string Id;
        public string Kicker = string.Empty;
        public string Title = string.Empty;
        public string Subtitle = string.Empty;
        public string StatusText = string.Empty;
        public TwinUITone StatusTone = TwinUITone.Unknown;

        /// <summary>页内二级导航（id/label 等长）；空 = 不显示。</summary>
        public string[] SubNavIds = new string[0];

        public string[] SubNavLabels = new string[0];

        /// <summary>卡片顺序（TwinUiModule.Id）。</summary>
        public string[] WidgetIds = new string[0];

        /// <summary>默认二级导航 id（空 = 第一个）。</summary>
        public string DefaultSubNav = string.Empty;

        public bool HasSubNav => SubNavIds != null && SubNavIds.Length > 0;

        public string SubNavLabel(string id)
        {
            if (SubNavIds == null || SubNavLabels == null)
            {
                return id;
            }

            for (int i = 0; i < SubNavIds.Length && i < SubNavLabels.Length; i++)
            {
                if (SubNavIds[i] == id)
                {
                    return SubNavLabels[i];
                }
            }

            return id;
        }
    }

    /// <summary>内置 5 页签。顺序即页签顺序。</summary>
    public static class TwinUiWorkspaceRegistry
    {
        public const string Overview = "overview";
        public const string Twin = "twin";
        public const string Control = "control";
        public const string Mission = "mission";
        public const string Experiment = "experiment";

        private static readonly List<TwinUiWorkspace> _all = Build();

        public static IReadOnlyList<TwinUiWorkspace> All => _all;

        public static int Count => _all.Count;

        public static TwinUiWorkspace Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Id == id)
                {
                    return _all[i];
                }
            }

            return null;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>运行时可追加页签（宿主/实验用）；重复 id 会被忽略。</summary>
        public static bool Register(TwinUiWorkspace workspace)
        {
            if (workspace == null || string.IsNullOrEmpty(workspace.Id) || Find(workspace.Id) != null)
            {
                return false;
            }

            _all.Add(workspace);
            return true;
        }

        private static List<TwinUiWorkspace> Build()
        {
            var list = new List<TwinUiWorkspace>
            {
                new TwinUiWorkspace
                {
                    Id = Overview,
                    Kicker = "SYSTEM OVERVIEW",
                    Title = "工作台总览",
                    Subtitle = "数据链、任务、性能与告警的第一屏摘要；任一卡片都可拆出为浮窗。",
                    StatusText = "只读监测",
                    StatusTone = TwinUITone.Ok,
                    WidgetIds = new[] { "welcome", "datalink", "performance", "task", "sync", "alarm", "session", "selection", "roam", "ik" }
                },
                new TwinUiWorkspace
                {
                    Id = Twin,
                    Kicker = "DIGITAL TWIN",
                    Title = "数字孪生监测",
                    Subtitle = "机器人状态只读展示：关节、力矩、TCP 位姿、六维力与虚实同步。",
                    StatusText = "Actual / Target",
                    StatusTone = TwinUITone.Ok,
                    SubNavIds = new[] { "all", "joints", "torque", "sync", "tcp" },
                    SubNavLabels = new[] { "全部", "关节", "力矩", "同步", "末端" },
                    DefaultSubNav = "all",
                    WidgetIds = new[] { "joints", "tcp", "torque", "wrench", "sync", "session", "ik", "selection" }
                },
                new TwinUiWorkspace
                {
                    Id = Control,
                    Kicker = "BIDIRECTIONAL CONTROL",
                    Title = "双向控制",
                    Subtitle = "目标编辑 → 本地预览 → 安全校验 → 明确确认 → ROS2点位模拟；真机出口禁用。",
                    StatusText = "执行已锁定",
                    StatusTone = TwinUITone.Warn,
                    WidgetIds = new[] { "command", "plan", "authority", "ik", "tcp", "sync" }
                },
                new TwinUiWorkspace
                {
                    Id = Mission,
                    Kicker = "DISASSEMBLY TRAINING",
                    Title = "拆解实训",
                    Subtitle = "拆解进度来自既有 DisassemblySystem；步骤引导与训练评估为本地流程/自评，非评分系统。",
                    StatusText = "本地自评",
                    StatusTone = TwinUITone.Unknown,
                    WidgetIds = new[] { "task", "steps", "eval", "ik" }
                },
                new TwinUiWorkspace
                {
                    Id = Experiment,
                    Kicker = "EXPERIMENT CENTER",
                    Title = "实验数据",
                    Subtitle = "会话、原始 CSV、链路质量与最近告警；UI 显示开关不影响正式采集。",
                    StatusText = "只读",
                    StatusTone = TwinUITone.Ok,
                    WidgetIds = new[] { "session", "datalink", "alarm" }
                }
            };
            return list;
        }
    }
}
