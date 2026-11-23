// TwinUiLayoutPresets.cs —— 6 套工作空间布局预设（P8）
// 设计目标：像浏览器工作区一样一键切"页签 + 主窗大小 + 常用浮窗组合"；
//           预设只是**起点**，应用后用户仍可自由改，不会被每次进入工作区时覆盖。
using System.Collections.Generic;

namespace DigitalTwin.UI
{
    public sealed class TwinUiLayoutPreset
    {
        public string Id;
        public string Label;
        public string Tab;
        public string Sub = string.Empty;

        /// <summary>该预设历史默认拆出的卡片 id；P3 起不再自动应用，仅为旧存档/文档兼容保留。</summary>
        public string[] Floating = new string[0];

        public float WidthRatio = 0.64f;
        public float HeightRatio = 0.88f;
    }

    public static class TwinUiLayoutPresets
    {
        public const string Default = "default";
        public const string Observe = "observe";
        public const string Monitor = "monitor";
        public const string Control = "control";
        public const string Disassembly = "disassembly";
        public const string Experiment = "experiment";

        private static readonly List<TwinUiLayoutPreset> _all = new List<TwinUiLayoutPreset>
        {
            new TwinUiLayoutPreset
            {
                Id = Default, Label = "默认", Tab = TwinUiWorkspaceRegistry.Overview
            },
            new TwinUiLayoutPreset
            {
                Id = Observe, Label = "观测", Tab = TwinUiWorkspaceRegistry.Overview,
                Floating = new[] { "sync", "performance" }, WidthRatio = 0.58f
            },
            new TwinUiLayoutPreset
            {
                Id = Monitor, Label = "监测", Tab = TwinUiWorkspaceRegistry.Twin,
                Floating = new[] { "joints", "torque", "tcp" }
            },
            new TwinUiLayoutPreset
            {
                Id = Control, Label = "控制", Tab = TwinUiWorkspaceRegistry.Control,
                Floating = new[] { "authority", "plan" }, WidthRatio = 0.60f
            },
            new TwinUiLayoutPreset
            {
                Id = Disassembly, Label = "拆解", Tab = TwinUiWorkspaceRegistry.Mission,
                Floating = new[] { "task" }, WidthRatio = 0.60f
            },
            new TwinUiLayoutPreset
            {
                Id = Experiment, Label = "实验", Tab = TwinUiWorkspaceRegistry.Experiment,
                Floating = new[] { "session", "datalink" }, WidthRatio = 0.58f
            }
        };

        public static IReadOnlyList<TwinUiLayoutPreset> All => _all;

        public static TwinUiLayoutPreset Find(string id)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Id == id)
                {
                    return _all[i];
                }
            }

            return null;
        }

        public static string LabelOf(string id)
        {
            var p = Find(id);
            return p != null ? p.Label : id;
        }
    }
}