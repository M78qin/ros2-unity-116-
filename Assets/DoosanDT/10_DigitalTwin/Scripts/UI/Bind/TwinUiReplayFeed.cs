// TwinUiReplayFeed.cs —— 离线回放喂给 V2 卡片的只读适配器（2026-11-08 大论文批次 A/UI 回放）
//                                            （2026-11-09 R2 收口：单一真值 + 事务式加载 + 双口径频率）
//
// 用途：没有 ROS/硬件时，用既有 ReplayTwinUiDataSource 读 PaperLogs/<runId>/samples.csv，
//       把同一份 TwinUiSnapshot 契约交给 TwinUIStatusBinder，V2 卡片（关节/力矩/wrench/TCP/会话）
//       即可按真实会话回放；不需要改场景、不需要改实时源路径。
//
// 边界（硬约束）：
//   * 只读 CSV；不连 ROS/DART、不写记录文件、不下发命令、不改变 TwinRecorder 状态。
//   * 只有本组件被显式创建（离线自检 / 演示入口）时 binder 才走回放映射；实时路径原样保留。
//   * 缺失通道（samples.csv 没有力矩/六维力/TCP）保持空值，卡片显示"未订阅"，禁止补 0。
//
// R2 A-1（单一真值）：`Active`（且 `Loaded`）是"当前是否回放"的唯一事实来源；
//   `TwinUiSourceSwitch` 的 IsReplay/CurrentCsv/CurrentRunId/RecordHz 全部由本类派生。
//   任何调用方（含遗留舞台直连 `Ensure`）只要走本类，就不可能再出现"顶部回放 / 底栏实时"。
// R2 A-2 T4（事务式）：`TryAttach` 先在**独立候选数据源**上验证可加载，成功才替换 `_source`；
//   失败时旧 `_source` / `Active` / `CsvPath` / 记录频率全部逐项不变（旧回放继续推进）。
// R2 A-3（频率口径）：
//   * `RecordHzWindow` = 回放窗口（默认 2 s）内**接收行实测**：(n-1)/CSV 原始时间戳跨度；
//     这是"窗口内接收率"，既不是机械臂原始采样率，也不是整文件速率。
//   * `RecordHzFile`   = 整文件接收行实测：(rows-1)/首末时间戳跨度，加载完成即可读（不依赖 Refresh）。
//   * `TargetSampleHz` = 既有硬编码**目标值**（100 Hz），属历史/目标元数据，禁止当实测。
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiReplayFeed : MonoBehaviour
    {
        public static TwinUiReplayFeed Active { get; private set; }

        /// <summary>A-3 频率口径说明（供 UI/自检原样打印，避免把窗口速率说成采样率）。</summary>
        public const string RecordHzSemantics =
            "窗口速率=回放窗口内接收行/CSV时间戳跨度；整文件速率=接收行/首末时间戳跨度；TARGET=既有目标值(非实测)";

        [SerializeField] private string csvPath = string.Empty;
        [SerializeField] private float windowSeconds = 2f;
        [SerializeField] private bool loop = true;
        [SerializeField] private float speed = 1f;

        // 本批（A3）：显示刷新率与 CSV 记录频率是两个独立量。
        // DisplayHz 只决定"界面多久取一次数据"；记录频率由 samples.csv 时间戳实测，只读。
        [SerializeField] private float displayHz = 5f;

        private ReplayTwinUiDataSource _source;
        private TwinUiSettings _settings;

        // fix1：暂停状态是否已经同步给数据源（只在变化时同步，见 Update 注释）。
        private bool _pauseSyncedInit;
        private bool _pauseSynced;

        public TwinUiSnapshot Snapshot => _source != null ? _source.Snapshot : null;
        public bool Loaded => _source != null && _source.Loaded;

        /// <summary>
        /// A 支：表头合法但 0 数据行（真实常见：35 个会话里 13 个）。此时**算已挂载**，
        /// 界面必须显示"无数据"，而不是沿用旧源伪造有数据。
        /// </summary>
        public bool HeaderOnly => _source != null && _source.HeaderOnly;

        /// <summary>已挂载 = 有数据行 或 表头合法但 0 行。</summary>
        public bool Attached => _source != null && (_source.Loaded || _source.HeaderOnly);

        /// <summary>回放文件 schema（samples/rt_raw40/rt_raw46），供自检打印。</summary>
        public string SchemaLabel => _source != null ? _source.SchemaLabel : "none";
        public string LoadError => _source != null ? _source.LoadError : string.Empty;
        public double Progress01 => _source != null ? _source.Progress01 : 0d;
        public double TotalSeconds => _source != null ? _source.TotalSeconds : 0d;
        public double PlaybackSeconds => _source != null ? _source.PlaybackSeconds : 0d;

        /// <summary>fix1 B(5)：当前回放是否处于"暂停游标"状态（生产暂停只对回放生效）。</summary>
        public bool Paused => _source != null && _source.Paused;

        /// <summary>fix1 B(8)：本会话已发生的循环回绕次数（0 = 未回绕）。</summary>
        public int LoopWraps => _source != null ? _source.LoopWraps : 0;
        public string CsvPath => csvPath;
        public int RowCount => _source != null ? _source.RowCount : 0;
        public ReplayTwinUiDataSource Source => _source;

        // ---------------------------------------------------------------- A-3 频率（两个口径分开）

        /// <summary>回放窗口秒数（窗口速率的统计窗口；与曲线显示时间窗是两件事）。</summary>
        public double RecordWindowSeconds => windowSeconds;

        /// <summary>窗口内接收行实测 Hz（n-1 / CSV 原始时间戳跨度）；需要至少 1 次 Refresh 才有值。</summary>
        public double RecordHzWindow
        {
            get
            {
                var snap = Snapshot;
                return snap != null && snap.Link != null ? snap.Link.SampleHz : 0d;
            }
        }

        /// <summary>窗口内参与统计的接收行数。</summary>
        public int RecordRowsWindow => _source != null ? _source.WindowReceiveRows : 0;

        /// <summary>整文件接收行实测 Hz（加载完成即可读，不依赖 Refresh）。</summary>
        public double RecordHzFile => _source != null ? _source.FileReceiveHz : 0d;

        /// <summary>整文件接收行数（只数 event=receive 的行）。</summary>
        public int RecordRowsFile => _source != null ? _source.FileReceiveRows : 0;

        /// <summary>整文件首末接收时间戳跨度（秒）。</summary>
        public double RecordSpanFileSeconds => _source != null ? _source.FileSpanSeconds : 0d;

        /// <summary>窗口速率是否可用（≥2 个接收行才成立；不足时显示缺失，不补 0）。</summary>
        public bool RecordHzWindowAvailable => RecordRowsWindow >= 2 && RecordHzWindow > 0d;

        /// <summary>整文件速率是否可用（≥2 个接收行才成立）。</summary>
        public bool RecordHzFileAvailable => RecordRowsFile >= 2 && RecordHzFile > 0d;

        /// <summary>既有硬编码目标值（历史/目标元数据，**不是**实测；禁止当采样率展示）。</summary>
        public double TargetSampleHz => ReplayTwinUiDataSource.TargetSampleHzConstant;

        /// <summary>
        /// 兼容旧调用方的"记录频率"：优先窗口实测，窗口尚未测出时退回整文件实测；
        /// 都不可用返回 0（调用方必须用 Available 判断是否显示缺失）。语义见 <see cref="RecordHzSemantics"/>。
        /// </summary>
        public double RecordHz => RecordHzWindowAvailable
            ? RecordHzWindow
            : (RecordHzFileAvailable ? RecordHzFile : 0d);

        /// <summary>当前 RecordHz 的口径："窗口" / "整文件" / "缺失"。</summary>
        public string RecordHzScope => RecordHzWindowAvailable ? "窗口" : (RecordHzFileAvailable ? "整文件" : "缺失");

        /// <summary>显示刷新率（Hz，可调）。与记录频率分开标注、分开生效。</summary>
        public float DisplayHz => displayHz;

        /// <summary>本 feed 每帧推进的快照次数（**帧数**，不是界面刷新次数；冻结显示时仍增长，A6 证据）。
        /// 注意：**换源（attach）会归零**，跨源比较请用 <see cref="TotalSnapshots"/>（会话级单调）。</summary>
        public int SnapshotCount { get; private set; }

        /// <summary>
        /// 会话级单调计数（跨 attach 不归零）：用于"换源/冻结期间仍然在推进"的留存证据。
        /// Play 启动时由 TwinUiSourceSwitch.ResetRuntimeState 清零。
        /// </summary>
        public static int TotalSnapshots { get; private set; }

        /// <summary>Play 启动清会话级计数（关域重载时静态残留的兜底）。</summary>
        public static void ResetSessionCounters()
        {
            TotalSnapshots = 0;
        }

        /// <summary>真正产出新快照的刷新次数（每帧一次；同样**不是**界面刷新次数，见 binder.SampleCount）。</summary>
        public int RefreshCount { get; private set; }

        /// <summary>本 feed 成功挂载 CSV 的次数（事务式加载成功才 +1）。</summary>
        public int AttachCount { get; private set; }

        /// <summary>最近一次 TryAttach 是否成功（失败的 Ensure 不能凭"旧源还 Loaded"被当成成功）。</summary>
        public bool LastAttachOk { get; private set; }

        /// <summary>最近一次 TryAttach 的失败原因（成功为空）。</summary>
        public string LastAttachError { get; private set; } = string.Empty;

        /// <summary>调整显示刷新率（不影响 CSV 记录频率，不影响回放钟）。</summary>
        public void SetDisplayHz(float hz)
        {
            displayHz = Mathf.Clamp(hz, 1f, 30f);
            if (_settings != null)
            {
                _settings.uiRefreshHz = displayHz;
            }
        }

        /// <summary>销毁当前回放 feed（切回实时源时调用；不写盘、不改场景）。</summary>
        public static void Shutdown()
        {
            TwinUiReplayFeed[] all = Object.FindObjectsOfType<TwinUiReplayFeed>(true);
            bool had = Active != null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                {
                    all[i]._source = null;
                    Object.Destroy(all[i].gameObject);
                }
            }

            Active = null;
            if (had || all.Length > 0)
            {
                // A-1/A-4：detach 也算一次源分段变化，卡片据此清缓冲。
                TwinUiSourceSwitch.NotifyFeedDetached();
            }
        }

        /// <summary>
        /// 找到已存在的 feed（含未激活对象），没有就新建一个隐藏的常驻对象，并**事务式**加载 csv。
        /// 失败时：不替换旧数据源、不改 Active（若原本没有 feed，则销毁新建对象），返回的 feed.Loaded=false。
        /// 成功时：通知 TwinUiSourceSwitch 让 Segment+1（任何入口都清一次缓冲）。
        /// 本方法保留给遗留舞台调用，行为与 TwinUiSourceSwitch.SetReplay 完全一致（同一真值）。
        /// </summary>
        public static TwinUiReplayFeed Ensure(string csvPath)
        {
            TwinUiReplayFeed feed = Active;
            if (feed == null)
            {
                TwinUiReplayFeed[] all = Object.FindObjectsOfType<TwinUiReplayFeed>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null)
                    {
                        feed = all[i];
                        break;
                    }
                }
            }

            bool created = false;
            if (feed == null)
            {
                var go = new GameObject("TwinUiReplayFeed");
                Object.DontDestroyOnLoad(go);
                feed = go.AddComponent<TwinUiReplayFeed>();
                created = true;
            }

            feed.gameObject.SetActive(true);
            string error;
            if (!feed.TryAttach(csvPath, out error))
            {
                Debug.LogWarning("[SOURCE] attach failed | csv=" + (csvPath ?? "<null>") + " | " + error);
                if (created)
                {
                    Active = null;
                    Object.Destroy(feed.gameObject);
                }
            }

            return feed;
        }

        private void Awake()
        {
            Active = this;
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>
        /// 事务式加载：先在独立候选数据源上验证，成功才替换（T4 rollback 的核心保证）。
        /// 失败返回 false，且本对象的 csvPath/_source/SnapshotCount 保持原值。
        /// </summary>
        public bool TryAttach(string path, out string error)
        {
            error = string.Empty;
            var candidate = new ReplayTwinUiDataSource(path ?? string.Empty);
            if (!candidate.Loaded && !candidate.HeaderOnly)
            {
                error = string.IsNullOrEmpty(candidate.LoadError) ? "no data rows" : candidate.LoadError;
                LastAttachOk = false;
                LastAttachError = error;
                return false;
            }

            // 成功：此刻才替换旧源。
            _source = candidate;
            csvPath = path ?? string.Empty;
            _settings = ScriptableObject.CreateInstance<TwinUiSettings>();
            _settings.replayWindowSeconds = Mathf.Max(0.5f, windowSeconds);
            _settings.replayLoop = loop;
            _settings.replaySpeed = speed;
            _settings.uiRefreshHz = displayHz;
            _settings.robotApplyRateHz = 60f;
            SnapshotCount = 0;
            RefreshCount = 0;
            AttachCount++;
            LastAttachOk = true;
            LastAttachError = string.Empty;
            Active = this;
            TwinUiSourceSwitch.NotifyFeedAttached(this, csvPath);
            return true;
        }

        /// <summary>兼容旧签名：转发到 <see cref="TryAttach"/>（失败时记录 warning，但不动旧源）。</summary>
        public void Load(string path)
        {
            string error;
            if (!TryAttach(path, out error))
            {
                Debug.LogWarning("[SOURCE] feed load rejected | csv=" + (path ?? "<null>") + " | " + error);
            }
        }

        private void Update()
        {
            if (_source == null)
            {
                return;
            }

            // fix1 B(5)：生产暂停的**唯一执行点**。UI 只改 TwinUiPlaybackControl，
            // 这里在它的状态**发生变化**时同步给数据源（暂停=停游标与回放钟；冻结显示仍然只停重绘）。
            // 只在变化时同步，是为了不覆盖自检里"同帧读取"用的 SetHold（那是另一条显式自检路径）。
            bool wantPause = TwinUiPlaybackControl.IsPaused;
            if (!_pauseSyncedInit || _pauseSynced != wantPause)
            {
                _pauseSyncedInit = true;
                _pauseSynced = wantPause;
                _source.SetPaused(wantPause);
            }

            SnapshotCount++;
            _source.Refresh(_settings);

            // fix1 B(8)：循环回绕 → 通知"曲线分段"（Segment+1），卡片据此清缓冲，
            // 因此第 N 周期和第 N+1 周期不会连成一条假连续的线。
            if (_source.ConsumeLoopWrap())
            {
                TwinUiSourceSwitch.NotifyLoopWrapped();
            }

            TotalSnapshots++;
            if (_source.Loaded && _source.Snapshot != null)
            {
                RefreshCount++;
            }
        }
    }
}