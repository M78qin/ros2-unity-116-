namespace DigitalTwin.UI
{
    /// <summary>
    /// 三期（工业数据台）全部文案（唯一来源）。
    /// 新增文案前先跑 DigitalTwin/UI/16 Check UI Glyphs：静态字形集缺字会显示成方框。
    /// </summary>
    public static class TwinUITextR3
    {
        // 导航
        public const string TabIndustrial = "工业数据台";
        public const string SubData = "采集数据";
        public const string SubEvents = "事件流";
        public const string SubSettings = "实验设置";

        // 实验上下文条
        public const string CtxRun = "RUN";
        public const string CtxProfile = "PROFILE";
        public const string CtxCondition = "COND";
        public const string CtxPayload = "PAYLOAD";
        public const string CtxTcp = "TCP";
        public const string CtxSession = "SESSION";
        public const string CtxSelfCheck = "SELF-CHECK";
        public const string CtxNotProvided = "未提供";
        public const string CtxRuntime = "运行时";
        public const string CtxReplay = "回放";
        public const string CtxMock = "演示";

        // 通用列头
        public const string ColItem = "指标";
        public const string ColJoint = "关节";
        public const string ColCurrent = "当前";
        public const string ColTarget = "目标";
        public const string ColError = "误差";
        public const string ColState = "状态";
        public const string ColTime = "时间";
        public const string ColLevel = "级别";
        public const string ColCode = "代码";
        public const string ColText = "内容";
        public const string ColVelocity = "速度 °/s";
        public const string ColTorque = "力矩 N·m";

        // 卡片标题
        public const string SecLink = "链路频率与新鲜度";
        public const string SecJoint = "关节角 当前 / 目标 / 误差";
        public const string SecTorque = "速度与力矩";
        public const string SecWrench = "六维力 / 力矩";
        public const string SecPose = "TCP 位姿（XYZ + RPY）";
        public const string SecChart = "关节误差曲线";

        // 链路卡行名
        public const string RowSampleHz = "采样 Hz";
        public const string RowApplyHz = "应用 Hz";
        public const string RowUiHz = "界面 Hz";
        public const string RowFresh = "新鲜度";
        public const string RowLatency = "应用时延";
        public const string RowJitter = "抖动 p99";
        public const string RowDropped = "丢帧";
        public const string RowClock = "时钟同步";
        public const string RowQuality = "帧质量";

        // 单位
        public const string UnitMs = " ms";
        public const string UnitHz = " Hz";
        public const string UnitDeg = " °";
        public const string UnitDegPerSec = " °/s";
        public const string UnitNm = " N·m";
        public const string UnitN = " N";
        public const string UnitMeter = " m";
        public const string UnitPercent = " %";

        // 轴标签
        public static readonly string[] JointLabels = { "J1", "J2", "J3", "J4", "J5", "J6" };
        public static readonly string[] PoseLabels = { "X", "Y", "Z", "Rx", "Ry", "Rz" };
        public static readonly string[] WrenchLabels = { "Fx", "Fy", "Fz", "Tx", "Ty", "Tz" };

        // 曲线卡
        public const string ChartNote = "目标虚线 = 目标值；色带 = 黄/红阈值（待校准）";
        public const string ChartEmpty = "无数据（等待数据源）";

        // 事件流页
        public const string SecEventStream = "事件与告警流";
        public const string SecAlertSummary = "告警统计与能力位";
        public const string RowWarnCount = "预警数";
        public const string RowCritCount = "超限数";
        public const string RowEventTotal = "事件总数";
        public const string RowEventFile = "事件文件";
        public const string RowSnapshotFile = "快照文件";
        public const string RowSourceKind = "数据源";
        public const string RowCapability = "能力位";
        public const string CapTcp = "TCP 位姿";
        public const string CapTorque = "关节力矩";
        public const string CapForce = "六维力";
        public const string CapVelocity = "关节速度";
        public const string CapOn = "已订阅";
        public const string CapOff = "未订阅";

        // 设置页
        public const string SecRefresh = "刷新频率";
        public const string SecTheme = "主题与密度";
        public const string SecSource = "数据源";
        public const string SecThresholds = "阈值组（待校准）";
        public const string BtnThemeDark = "深色";
        public const string BtnThemeLight = "浅色";
        public const string BtnDensityComfort = "标准";
        public const string BtnDensityCompact = "紧凑";
        public const string BtnSourceRuntime = "运行时";
        public const string BtnSourceReplay = "回放";
        public const string BtnSourceMock = "演示";
        public const string BtnSave = "写回设置资产";
        public const string BtnToggleEvent = "暂停事件";
        public const string SaveOk = "已写回 TwinUiSettings.asset";
        public const string SaveFail = "写回失败（只在编辑器内可持久化）";
        public const string RowUiHzValue = "界面刷新";
        public const string RowExportHz = "快照导出";
        public const string RowRobotApplyHz = "机器人应用";
        public const string RowThemeValue = "当前主题";
        public const string RowDensityValue = "当前密度";
        public const string RowReplayPath = "回放文件";
        public const string RowSourceValue = "当前数据源";
        public const string NoteCalibrated = "阈值未做现场标定：黄/红仅用于联调判读，不得写入论文结果。";

        // 状态词
        public const string NotSubscribed = "未订阅";
        public const string NoData = "无数据";
        public const string NotConnected = "未连接";
        public const string NoSource = "无数据源";
        public const string ReplayRunning = "回放中";
        public const string MockWatermark = "MOCK 演示数据";
        public const string ThresholdDemoTag = "阈值注入（非实测）";
        public const string EmDash = "—";

        // 界面刷新频率档位（只允许这几档，见 TwinUiSettings.SetUiRefreshHz）
        public static readonly float[] UiHzOptions = { 5f, 10f, 20f, 30f, 60f };
    }
}