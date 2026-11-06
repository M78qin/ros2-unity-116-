// Single source of truth for every user-facing string the unified panel writes.
//
// The project's Chinese font is a STATIC NotoSansSC atlas (1368 glyphs, generated from the
// characters that existed when the project was authored). A character outside that set renders
// as tofu and cannot be added at runtime, and rebuilding the 34 MB atlas is out of scope for a
// UI batch. So: all copy lives here, and the editor tool
// "DigitalTwin/UI/8 Check UI Font Coverage (read-only)" verifies every character against the
// font asset before anything is written into the scene.
namespace DigitalTwin
{
    public static class TwinUIShellText
    {
        // welcome page (no credential check - this is a welcome page, not authentication)
        public const string ProductTitle = "退役动力电池拆解数字孪生实训系统";
        public const string WelcomeHint = "入口页面：不收集账号与口令";
        public const string Enter = "进入系统";
        public const string Quit = "退出";
        public const string QuitHint = "已退出（编辑器内不关闭进程）";

        // tabs
        public const string TabRoaming = "漫游";
        public const string TabTraining = "训练";
        public const string TabDisassembly = "拆解";
        public const string TabData = "数据";

        // per-mode hint line
        public const string HintRoaming = "漫游模式：自由观察双臂与场景";
        public const string HintTraining = "训练模式：按任务流程操作并观察状态";
        public const string HintDisassembly = "拆解模式：按层级顺序拆解电池包";
        public const string HintData = "数据模式：实时曲线与实验会话";

        // status strip
        public const string StatusPrefix = "状态";
        public const string StatusConnected = "已连接";
        public const string StatusDisconnected = "未连接";
        public const string RecordingOn = "记录中";
        public const string RecordingOff = "未记录";
        public const string ModePrefix = "当前模式";
        public const string SourcePrefix = "数据来源";
        public const string SourceNone = "无数据源";

        // chart card
        public const string ChartTitle = "实时曲线";
        public const string ChartUnitForce = "力 / 力矩";
        public const string ChartUnitJoint = "关节角 (deg)";
        public const string ChartNoForce = "未订阅力/力矩通道";
        public const string ChartNoData = "无数据源";
        public const string ChartReplay = "回放数据（自检）";
        public const string ChartLive = "实时数据";

        // command card (presentation only - the safety chain itself is unchanged)
        public const string CommandTitle = "双向控制";
        public const string CommandPreview = "预览";
        public const string CommandCheck = "校验";
        public const string CommandConfirm = "确认";
        public const string CommandExecute = "执行";
        public const string CommandHalt = "急停";
        public const string CommandDryRun = "干运行";
        public const string CommandRealRobot = "真机";
        public const string CommandIdle = "未开始";

        /// <summary>Every string above, so the font coverage check has one authoritative list.</summary>
        public static readonly string[] All =
        {
            ProductTitle, WelcomeHint, Enter, Quit, QuitHint,
            TabRoaming, TabTraining, TabDisassembly, TabData,
            HintRoaming, HintTraining, HintDisassembly, HintData,
            StatusPrefix, StatusConnected, StatusDisconnected, RecordingOn, RecordingOff,
            ModePrefix, SourcePrefix, SourceNone,
            ChartTitle, ChartUnitForce, ChartUnitJoint, ChartNoForce, ChartNoData, ChartReplay, ChartLive,
            CommandTitle, CommandPreview, CommandCheck, CommandConfirm, CommandExecute, CommandHalt,
            CommandDryRun, CommandRealRobot, CommandIdle,
        };
    }
}