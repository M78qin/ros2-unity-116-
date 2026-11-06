namespace DigitalTwin.UI
{
    /// <summary>
    /// 二期 UI 全部文案（唯一来源）。改动前先跑 DigitalTwin/UI/8 Check UI Font Coverage，
    /// 静态字形集缺字会直接显示成方框。
    /// </summary>
    public static class TwinUITextR2
    {
        // 顶栏
        public const string Product = "退役动力电池拆解数字孪生实训系统";
        public const string ModeMission = "实训模式";
        public const string ModeTwinOps = "监控模式";

        public const string KpiSource = "数据源";
        public const string KpiRate = "帧率";
        public const string KpiLatency = "时延";
        public const string KpiAge = "帧龄";

        public const string AlarmNone = "无告警";
        public const string AlarmPrefix = "告警 ";
        public const string RecOn = "记录中";
        public const string RecOff = "未记录";
        public const string EstopSet = "急停已触发";
        public const string EstopClear = "急停正常";

        // 主面板
        public const string TabMission = "实训任务台";
        public const string TabTwinOps = "孪生监控台";

        // 监控台子页
        public const string SubOverview = "概览";
        public const string SubTrend = "曲线";
        public const string SubControl = "控制";
        public const string SubRecord = "记录";

        // 概览
        public const string SecSourceHealth = "数据源健康";
        public const string SecDataChain = "数据链路";
        public const string SecQuality = "帧质量";
        public const string SecAlarms = "告警";
        public const string ChainSource = "采集";
        public const string ChainTransport = "传输";
        public const string ChainApply = "应用";
        public const string ChainModel = "模型";
        public const string RowRate = "接收帧率";
        public const string RowDrop = "丢弃帧";
        public const string RowReorder = "乱序帧";
        public const string RowQueue = "待写队列";
        public const string RowJitter = "抖动";
        public const string RowClock = "时钟同步";
        public const string RowMode = "机器人模式";
        public const string RowMotion = "运动状态";
        public const string RowActive = "当前来源";

        public const string NoConnection = "未连接";
        public const string NoSubscription = "未订阅";
        public const string NoData = "无数据";

        // 趋势
        public const string TrendJointPos = "关节角";
        public const string TrendJointTorque = "关节力矩";
        public const string TrendWrench = "六轴力/力矩";
        public const string TrendUnitDeg = "单位：度";
        public const string TrendUnitNm = "单位：N·m";
        public const string TrendUnitN = "单位：N / N·m";

        // 控制
        public const string SecInterlock = "安全校验";
        public const string SecMode = "控制模式";
        public const string SecCommand = "指令";
        public const string SecLog = "执行日志";
        public const string SecJog = "目标位姿";
        public const string SecSpeed = "速度档";
        public const string BtnPreview = "预览";
        public const string BtnCheck = "校验";
        public const string BtnConfirm = "确认";
        public const string BtnExecute = "执行";
        public const string BtnHalt = "急停";
        public const string ChkDryRun = "干运行";
        public const string ChkRealCmd = "真机指令";
        public const string ChkBidirectional = "双向控制";
        public const string ChkCanSend = "可下发";
        public const string ChkFrame = "实时帧";
        public const string ChkArm = "二次确认";
        public const string ChkEstop = "急停状态";
        public const string ChkSafetyChain = "安全链";
        public const string ChkClock = "时钟一致";
        public const string JogTitle = "关节";
        public const string JogDisabled = "需真机授权后启用";
        public const string SpeedSlow = "慢";
        public const string SpeedMid = "中";
        public const string SpeedFast = "快";
        public const string DryRunLabel = "干运行";

        // 记录
        public const string SecRecording = "记录状态";
        public const string SecSession = "实验会话";
        public const string RowRecording = "记录开关";
        public const string RowPending = "待写";
        public const string RowDropped = "丢弃";
        public const string RowCsv = "文件就绪";
        public const string RowDir = "目录";
        public const string RowSession = "会话号";
        public const string RowExperiment = "实验";
        public const string RowPhase = "阶段";

        // 任务台
        public const string SecSteps = "任务步骤";
        public const string SecCurrent = "当前步骤";
        public const string SecEval = "训练评估";
        public const string EvalTimer = "计时";
        public const string EvalProgress = "完成度";
        public const string EvalErrors = "错误次数";
        public const string EvalScore = "自评得分";
        public const string BtnPrev = "上一步";
        public const string BtnNext = "下一步";
        public const string BtnReset = "重置";
        public const string BtnBackRoaming = "回到漫游";
        public const string EvalNote = "本地计时与自评，未接评分系统";
        public const string StepDone = "已完成";
        public const string StepCurrent = "进行中";
        public const string StepWaiting = "待开始";
        public const string HintPrefix = "提示：";

        public static readonly string[] Steps =
        {
            "进入漫游，观察电池包与工位布局",
            "确认机械臂与电池包位置是否对应",
            "开始训练：断电与绝缘检查",
            "拆卸上盖固定螺钉",
            "取出电池模组",
            "分离高低压线束",
            "完成自检与工位复原",
            "结束训练并查看评估"
        };

        public static readonly string[] StepBodies =
        {
            "在场景中自由漫游，了解电池包拆解工位、机械臂与工具位置。确认设备无人占用后再进入下一步。",
            "对照数字孪生界面核对机械臂基座位姿与电池包放置位置，偏差过大时先复位再继续。",
            "按训练要求执行断电、验电与绝缘检查。任一步未通过都不得继续拆卸。",
            "按序拆卸上盖四周固定螺钉，注意螺钉收纳与顺序约束，禁止跳步。",
            "上盖移除后取出电池模组，注意模组重量与承托，避免线束受力。",
            "分离高低压线束，先低压后高压，确认无残余电压后再拆下。",
            "清点工具与零件，复原工位，确认无遗留物后离开。",
            "训练结束，查看本次计时、完成度与错误记录，作为后续练习参考。"
        };

        public static readonly string[] StepHints =
        {
            "漫游页支持鼠标/手柄；VR 里用射线选择页签。",
            "位置偏差>5 cm 时不要开始拆解。",
            "断电与验电至少保留 30 秒确认。",
            "螺钉数量与顺序在拆解页有提示。",
            "模组取出前确认工装支撑到位。",
            "高压线束禁止带电插接。",
            "工位复原后拍一张现场照片留档。",
            "评估结果为本地自评，正式评分规则另行制定。"
        };
    }
}