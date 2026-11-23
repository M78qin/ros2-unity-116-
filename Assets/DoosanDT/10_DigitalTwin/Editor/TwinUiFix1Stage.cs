// TwinUiFix1Stage.cs —— fix1 自检舞台（UI/63）：播放暂停 / 循环边界 / 源条布局 / 数值纵轴 / 真实 RT 输入截图
//
// 为什么单独一个舞台：本批只允许"最小扩现有入口、不造平行系统"，产品侧只改了
//   TwinUiPlaybackControl（新）、ReplayTwinUiDataSource / TwinUiReplayFeed / TwinUiSourceSwitch /
//   TwinUiLineChart / TwinUiPageView / TwinUiShellV2；验证走"真实点击 + 真实产品入口"，不另写一套。
//
// 覆盖（编号对应合同）：
//   F1 播放/暂停：点生产按钮 → 回放游标与回放钟停住；再点恢复 → 继续（不是"视觉冻结"）
//   F2 循环边界：loop 开启跑到末尾 → 游标回到 0、回绕计数 +1、曲线分段（缓冲被清，不跨周期连线）
//   F3 源条布局：720/1200/1920 三档，SourceBar 内可见文本两两不重叠；记录频率独立成格；
//      会话名超长只允许 Ellipsis，完整路径/schema 在「详情」浮层里可看（且不被裁）
//   F4 数值纵轴：有数据才显示 min/mid/max + 单位（真实关节力矩输入），缺数据不虚造
//   F5 真实 RT 输入：通过产品「换回放」按钮加载 a/INPUTS 里的 rt_raw 46 列 / 40 列，
//      同帧 dump TCP / 六维力 / 关节力矩 / 状态 + 卡片实读文本；三档真实像素截图 + 滚动补六核心卡
//
// 边界：只读 CSV；不连 ROS/DART、不写记录器、不下发命令、不保存场景；输出只写本批 fix1/a/**。
// 每次运行独立目录（attempt_<stamp>），不覆盖上一次 attempt。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class TwinUiFix1Stage
    {
        const string DefaultBatchRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_phase1_abc_fix1\a";
        // night30：补交批次的产物只写新交付目录；默认仍是 fix1，保证旧入口的原行为不变。
        const string Night30BatchRoot = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_night_30\a";
        static string BatchRoot = DefaultBatchRoot;
        const string Armed = "Phase1abcFix1.Ui63";
        const int ShotHeight = 1080;
        const double WatchdogSeconds = 300d;
        static readonly int[] Widths = { 720, 1200, 1920 };

        static string _stamp;
        static string _root;
        static string _shotsRoot;
        static StringBuilder _log;
        static StringBuilder _tsv;
        static int _fails;
        static double _watchdog;
        static bool _started;

        static TwinUiWindowHost _window;
        static TwinUiShellV2 _shell;
        static TwinUiSurfaceHost _surface;
        static TwinUiPageView _page;
        static TwinUiWidgetTorque _torque;
        static TwinUiWidgetJoints _joints;
        static TwinUiLineChart _torqueChart;
        static TwinUiLineChart _jointsChart;

        static string _csv46;
        static string _sha46;
        static string _csv40;
        static string _sha40;
        static int _wrapsBefore;
        static int _segmentBeforeWrap;
        static int _cursorBeforeWrap;
        static float _wrapWallTime;
        static int _wrapPollTries;
        static double _playbackBeforePause;
        static int _cursorBeforePause;
        static int _pushBeforePause;
        static double _playbackBeforeFreeze;
        static int _pushBeforeFreeze;

        // night30：截图注记（path / sha256 / schema）与隔离基线
        static string _shotCsv = string.Empty;
        static string _shotSha = string.Empty;
        static string _shotSchema = string.Empty;
        static string _consolePathAtStart = string.Empty;
        static long _consoleBytesAtStart;
        static bool _isolationLogged;
        static double _runStartedAt;

        // 待验证的截图（请求后下一拍读 IHDR）
        static string _pendingShotPath;
        static string _pendingShotExpect;

        struct Item
        {
            public Action Step;
            public double At;
        }

        static readonly List<Item> _queue = new List<Item>();
        static double _lastAt;
        static int _f4Tries;
        static int _f4ScrollTries;

        static TwinUiFix1Stage()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("DigitalTwin/UI/63 Fix1 (auto-exit)", false, 630)]
        public static void Run()
        {
            BatchRoot = DefaultBatchRoot;
            RunCore();
        }

        /// <summary>
        /// night30 补交批次入口：与 Fix1 同一套舞台、同一套断言，只是产物根目录换成
        /// agent_runs/dsh_2026-11-09_night_30/a/**（testout / shots / checks / logs）。
        /// </summary>
        [MenuItem("DigitalTwin/UI/63 Night30 (auto-exit)", false, 631)]
        public static void RunNight30()
        {
            BatchRoot = Night30BatchRoot;
            RunCore();
        }

        /// <summary>清掉残留的武装/运行标记（只在上一轮异常中断、需要重新开始时用）。</summary>
        [MenuItem("DigitalTwin/UI/63 Night30 Reset", false, 632)]
        public static void ResetArmed()
        {
            SessionState.SetBool(Armed, false);
            SessionState.SetBool(Armed + ".running", false);
            SessionState.SetBool(Armed + ".started", false);
            Debug.Log("[UI63] armed state cleared");
        }

        static void RunCore()
        {
            // 防重复触发：MCP/菜单可能被重放一次（进 Play 时桥接会短暂断开）。
            // 只要标记还在跑（420s 内）就忽略任何新的触发，避免两个流程抢同一份静态状态、
            // 把 attempt 目录拆成两半（2026-11-09 23:19 的教训：重放实例在 fix1 根目录另开了 attempt）。
            if (SessionState.GetBool(Armed + ".running", false))
            {
                string at = SessionState.GetString(Armed + ".at", "0");
                long ticks;
                double age = double.MaxValue;
                if (long.TryParse(at, out ticks))
                {
                    age = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
                }

                if (age < 420d)
                {
                    Debug.LogWarning("[UI63] already running (" + age.ToString("F0") + "s); duplicate invocation ignored");
                    return;
                }
            }

            SessionState.SetBool(Armed + ".running", true);
            SessionState.SetString(Armed + ".at", DateTime.UtcNow.Ticks.ToString());

            _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _root = BatchRoot + @"\testout\attempt_" + _stamp;
            _shotsRoot = BatchRoot + @"\shots\attempt_" + _stamp;
            _consolePathAtStart = Application.consoleLogPath;
            _consoleBytesAtStart = TwinUiTestIsolation.ConsoleLogBytes();
            _runStartedAt = EditorApplication.timeSinceStartup;
            _isolationLogged = false;
            // 跨 domain reload 保住产物根与 attempt 号：否则进 Play 后静态字段复位，
            // ResumedAfterDomainReload 会在默认根目录里另开一个 attempt（23:19 就是这样丢的）。
            SessionState.SetString(Armed + ".root", BatchRoot);
            SessionState.SetString(Armed + ".stamp", _stamp);
            SessionState.SetString(Armed + ".startedAtMs", ((long)(_runStartedAt * 1000d)).ToString());
            SessionState.SetString(Armed + ".consoleAt", _consoleBytesAtStart.ToString());
            Directory.CreateDirectory(_root + @"\logs");
            Directory.CreateDirectory(_shotsRoot);
            _log = new StringBuilder();
            _tsv = new StringBuilder();
            _fails = 0;
            _queue.Clear();
            _lastAt = 0d;
            _started = false;
            // 进 Play 前把 Game View 收敛到已知尺寸：上一轮异常退出可能把它留在 137x822 之类
            // 的畸形尺寸，导致 Play 起来后页面/卡片根本不装配（boot refs 永远等不到）。
            try
            {
                SetWidthOnly(1200);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI63] pre-play game view reset failed: " + e.Message);
            }
            TwinUiViewFreeze.Reset();
            TwinUiPlaybackControl.Reset();
            SessionState.SetBool(Armed, true);
            SessionState.SetBool(Armed + ".started", true);
            Log("START ui63 fix1 stamp=" + _stamp + " root=" + _root);
            TsV("META", "batch_root", BatchRoot);
            TsV("META", "attempt", _stamp);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        // ------------------------------------------------------------------ 主循环

        static void Tick()
        {
            if (!SessionState.GetBool(Armed, false) || !Application.isPlaying || EditorApplication.isCompiling)
            {
                return;
            }

            if (!SessionState.GetBool(Armed + ".started", false))
            {
                SessionState.SetBool(Armed, false);
                return;
            }

            if (_log == null)
            {
                ResumedAfterDomainReload();
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (_watchdog <= 0d)
            {
                _watchdog = EditorApplication.timeSinceStartup + WatchdogSeconds;
            }

            if (EditorApplication.timeSinceStartup > _watchdog)
            {
                Fail("watchdog", "timeout; queue=" + _queue.Count);
                Finish();
                return;
            }

            if (!_started)
            {
                _started = true;
                _queue.Clear();
                _lastAt = EditorApplication.timeSinceStartup;
                Enqueue(Boot, 0.5d);
            }

            int guard = 0;
            while (_queue.Count > 0 && EditorApplication.timeSinceStartup >= _queue[0].At && guard < 4)
            {
                guard++;
                Item item = _queue[0];
                _queue.RemoveAt(0);
                try
                {
                    item.Step();
                }
                catch (Exception e)
                {
                    Fail("exception", e.ToString());
                    Finish();
                    return;
                }
            }
        }

        static void ResumedAfterDomainReload()
        {
            // 关键：进 Play 会让静态字段复位。产物根 / attempt 号 / 隔离基线都必须从 SessionState 取回，
            // 否则会在默认根目录另开 attempt（本夜 23:19 的真实故障），日志被拆成两半、且互相看不到对方。
            string storedRoot = SessionState.GetString(Armed + ".root", string.Empty);
            if (!string.IsNullOrEmpty(storedRoot))
            {
                BatchRoot = storedRoot;
            }

            string storedStamp = SessionState.GetString(Armed + ".stamp", string.Empty);
            _stamp = string.IsNullOrEmpty(storedStamp) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : storedStamp;
            _root = BatchRoot + @"\testout\attempt_" + _stamp;
            _shotsRoot = BatchRoot + @"\shots\attempt_" + _stamp;
            Directory.CreateDirectory(_root + @"\logs");
            Directory.CreateDirectory(_shotsRoot);

            string existingTxt = _root + @"\logs\UI63_" + _stamp + ".txt";
            string existingTsv = _root + @"\logs\UI63_" + _stamp + ".tsv";
            _log = File.Exists(existingTxt) ? new StringBuilder(File.ReadAllText(existingTxt)) : new StringBuilder();
            _tsv = File.Exists(existingTsv) ? new StringBuilder(File.ReadAllText(existingTsv)) : new StringBuilder();
            _fails = CountFails(_log.ToString());

            long startedMs;
            if (long.TryParse(SessionState.GetString(Armed + ".startedAtMs", "0"), out startedMs) && startedMs > 0L)
            {
                _runStartedAt = startedMs / 1000d;
            }
            else
            {
                _runStartedAt = EditorApplication.timeSinceStartup;
            }

            long consoleAt;
            if (long.TryParse(SessionState.GetString(Armed + ".consoleAt", "-1"), out consoleAt) && consoleAt >= 0L)
            {
                _consoleBytesAtStart = consoleAt;
            }
            else
            {
                _consoleBytesAtStart = TwinUiTestIsolation.ConsoleLogBytes();
            }

            _consolePathAtStart = Application.consoleLogPath;
            _isolationLogged = false;  // 必须等 Play 起来后在 Boot 里关 XR affordance（编辑态关会弄脏场景）
            _queue.Clear();
            _watchdog = 0d;
            _started = false;
            Log("resumed after domain reload (same attempt dir " + _stamp + " root=" + BatchRoot + ")");
        }

        static int CountFails(string text)
        {
            int failures = 0;
            int at = 0;
            while (!string.IsNullOrEmpty(text))
            {
                at = text.IndexOf("FAIL ", at, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                failures++;
                at += 5;
            }

            return failures;
        }

        static void Enqueue(Action step, double delay)
        {
            double at = Math.Max(EditorApplication.timeSinceStartup, _lastAt) + delay;
            _lastAt = at;
            _queue.Add(new Item { Step = step, At = at });
        }

        static void EnqueueFront(Action step, double delay)
        {
            _queue.Insert(0, new Item { Step = step, At = EditorApplication.timeSinceStartup + delay });
        }

        // ------------------------------------------------------------------ 流程

        static void Boot()
        {
            // night30：Play 前先核实"隔离"现场（不是流量口径，只是把当前状态写进日志）：
            //   * XRI affordance 组件（场景里每帧抛 NRE、让 Editor.log ~6 MB/min 增长）本进程内关掉；
            //   * 控制台/屏幕/是否 batchmode 记一笔，作为后续任何"帧率/响应"结论的边界条件；
            //   * 本舞台只读 CSV、不连 ROS/DART、不写记录器、不保存场景。
            if (!_isolationLogged)
            {
                _isolationLogged = true;
                _consolePathAtStart = Application.consoleLogPath;
                if (_consoleBytesAtStart <= 0L)
                {
                    _consoleBytesAtStart = TwinUiTestIsolation.ConsoleLogBytes();
                }

                if (_runStartedAt <= 0d)
                {
                    _runStartedAt = EditorApplication.timeSinceStartup;
                }

                string closed;
                int closedCount = TwinUiTestIsolation.DisableXrAffordances(out closed);
                Log("N0 isolation xrAffordancesClosed=" + closedCount + " [" + closed + "]");
                Log("N0 isolation console=" + TwinUiTestIsolation.ConsoleLogInfo()
                    + " screen=" + Screen.width + "x" + Screen.height
                    + " isPlaying=" + Application.isPlaying
                    + " batchMode=" + Application.isBatchMode
                    + " online=" + Application.internetReachability);
                TsV("N0", "xr_affordances_closed", closedCount.ToString());
                TsV("N0", "console_at_start", _consoleBytesAtStart.ToString());
                TsV("N0", "batch_root", BatchRoot);
                Check("N0 isolated play (replay-only, XR affordance noise off)",
                    Application.isPlaying, "isPlaying=" + Application.isPlaying + " console=" + _consoleBytesAtStart + "B");
            }

            // 畸形 Game View 尺寸（上一轮异常退出可能留下）会让页面根本不装配：先收敛再找 refs。
            if (Screen.width < 320 || Screen.height < 400)
            {
                Log("boot: absurd game view " + Screen.width + "x" + Screen.height + " -> reset to 1200x" + ShotHeight);
                try
                {
                    SetWidthOnly(1200);
                }
                catch (Exception e)
                {
                    Log("boot: game view reset failed: " + e.Message);
                }

                EnqueueFront(Boot, 0.5d);
                return;
            }

            _window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
            _shell = UnityEngine.Object.FindObjectOfType<TwinUiShellV2>();
            _surface = TwinUiSurfaceHost.Instance;
            if (_surface == null)
            {
                _surface = UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();
            }

            if (_window == null || _shell == null || _surface == null || _surface.Page == null)
            {
                Log("boot refs: window=" + (_window != null) + " shell=" + (_shell != null)
                    + " surface=" + (_surface != null)
                    + " page=" + (_surface != null && _surface.Page != null)
                    + " sceneLoaded=" + UnityEngine.SceneManagement.SceneManager.sceneCount);
                if (!Retry("boot refs"))
                {
                    return;
                }

                return;
            }

            _page = _surface.Page;
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin, false);
            _torque = _surface.Find("torque") as TwinUiWidgetTorque;
            _joints = _surface.Find("joints") as TwinUiWidgetJoints;
            _torqueChart = _torque != null && _torque.View != null
                ? _torque.View.GetComponentInChildren<TwinUiLineChart>(true)
                : null;
            _jointsChart = _joints != null && _joints.View != null
                ? _joints.View.GetComponentInChildren<TwinUiLineChart>(true)
                : null;
            if (_page == null || _torque == null || _joints == null || _torqueChart == null)
            {
                if (!Retry("page/cards/chart refs"))
                {
                    return;
                }

                return;
            }

            _tries = 0;
            _f4Tries = 0;
            _f4ScrollTries = 0;
            Log("boot ok: window=" + _window.name + " surface=" + _surface.name
                + " pageCards=" + _page.CardCount + " screen=" + Screen.width + "x" + Screen.height);
            TsV("META", "screen_at_boot", Screen.width + "x" + Screen.height);
            TsV("META", "page_cards", _page.CardCount.ToString());
            CaptureOriginalSelection();
            PrepareSizes();
            Enqueue(StepIdentityChecks, 0.25d);
        }

        // ------------------------------------------------------------------ night30：身份比较（session_ 前缀）

        static void StepIdentityChecks()
        {
            // 1) 纯函数：前缀差异不是身份差异；真实不同的 run_id 必须仍然不同；空串不可判同
            Check("N2 identity: session_<id> vs <id> is the same identity",
                ReplayTwinUiDataSource.SameRunIdentity(
                    "session_20261103_135616_529090_E4_B", "20261103_135616_529090_E4_B"),
                "normalized='" + ReplayTwinUiDataSource.NormalizeRunIdentity("session_20261103_135616_529090_E4_B") + "'");
            Check("N2 identity: identical run ids match",
                ReplayTwinUiDataSource.SameRunIdentity(
                    "20261103_135616_529090_E4_B", "20261103_135616_529090_E4_B"), string.Empty);
            Check("N2 identity: real different run_ids do NOT match",
                !ReplayTwinUiDataSource.SameRunIdentity(
                    "20261104_204838_488192_E4_B", "20261104_204642_850475_E4_B"),
                "a=20261104_204838_488192_E4_B b=20261104_204642_850475_E4_B");
            Check("N2 identity: empty side is never 'same'",
                !ReplayTwinUiDataSource.SameRunIdentity(string.Empty, "20261103_135616_529090_E4_B"),
                "a='' b=20261103_135616_529090_E4_B");
            Check("N2 identity: normalize trims and strips the session_ prefix only",
                ReplayTwinUiDataSource.NormalizeRunIdentity(" session_X ") == "X"
                && ReplayTwinUiDataSource.NormalizeRunIdentity("X") == "X",
                "trim+strip");
            TsV("N2", "normalize_session_prefix", ReplayTwinUiDataSource.NormalizeRunIdentity("session_abc"));

            // 2) status=identity_mismatch 仍必须是"异常"（前缀修复不能顺手把真实错配吞掉）
            var rawMismatch = TwinUiStatusInfo.From("identity_mismatch");
            Check("N2 real status=identity_mismatch still classified abnormal",
                rawMismatch.Classification == "IdentityMismatch" && !rawMismatch.IsNormal
                && rawMismatch.Display.Contains("异常"),
                "class=" + rawMismatch.Classification + " normal=" + rawMismatch.IsNormal
                + " display=" + rawMismatch.Display);
            Check("N2 status=accepted still normal", TwinUiStatusInfo.From("accepted").IsNormal,
                "class=" + TwinUiStatusInfo.From("accepted").Classification);

            Enqueue(() => SetWidth(1200, StepLoadRt46), 0.3d);
        }

        static void StepLoadRt46()
        {
            // 产品入口：真实点击「换回放」→ TwinUiSourceSwitch.NextReplay → TwinUiReplayFeed.Ensure。
            TwinUiSourceSwitch.ResetRuntimeState();
            TwinUIViewFreezeResetHelper();
            Click("SrcNext", "initial RT raw load");
            _csv46 = TwinUiSourceSwitch.CurrentCsv;
            _sha46 = Sha256(_csv46);
            var feed = TwinUiReplayFeed.Active;
            Log("F5 rt46 attach run=" + TwinUiSourceSwitch.CurrentRunId
                + " schema=" + (feed != null ? feed.SchemaLabel : "none")
                + " rows=" + (feed != null ? feed.RowCount : -1)
                + " extras=" + TwinUiSourceSwitch.ExtraSourceCount
                + " err=" + TwinUiSourceSwitch.LastError);
            Check("F5 product entry loaded ASCII/RT raw (replay active)", TwinUiSourceSwitch.IsReplay,
                "isReplay=" + TwinUiSourceSwitch.IsReplay + " err=" + TwinUiSourceSwitch.LastError);
            Check("F5 loaded source is rt_raw (not samples.csv)",
                feed != null && feed.SchemaLabel.StartsWith("rt_raw", StringComparison.Ordinal),
                "schema=" + (feed != null ? feed.SchemaLabel : "none"));
            Check("F5 product entry came from extra-sources manifest",
                TwinUiSourceSwitch.ExtraSourceCount >= 1,
                "extraSources=" + TwinUiSourceSwitch.ExtraSourceCount
                + " manifest=" + TwinUiSourceSwitch.ExtraSourcesManifestPath
                + " err=" + TwinUiSourceSwitch.ExtraSourcesError);
            TsV("F5", "input_id", "RT46");
            TsV("F5", "csv", _csv46);
            TsV("F5", "sha256", _sha46);
            TsV("F5", "schema", feed != null ? feed.SchemaLabel : "none");
            TsV("F5", "rows", (feed != null ? feed.RowCount : -1).ToString());
            TsV("F5", "run_id", TwinUiSourceSwitch.CurrentRunId);
            Check("F5 rt46 path exists on disk", File.Exists(_csv46), _csv46);
            TsV("F5", "csv_bytes", (File.Exists(_csv46) ? new FileInfo(_csv46).Length : -1).ToString());

            if (feed != null && feed.Source != null)
            {
                // night30 N2：真实 RT 原始件就放在 <session_<run_id>>/raw/ 下——
                // 会话目录前缀不得使它被误标"身份错配"；同时必须仍是单一身份（IdentityUnique）。
                Check("N2 identity: real RT46 under session_<run_id>/raw is NOT flagged as mismatch",
                    feed.Source.IdentityUnique && !feed.Source.DirectoryRunIdMismatch,
                    "dir='" + feed.Source.DirectoryName + "' run='" + feed.Source.SourceRunId
                    + "' unique=" + feed.Source.IdentityUnique
                    + " mismatch=" + feed.Source.DirectoryRunIdMismatch);
                TsV("N2", "rt46_dir", feed.Source.DirectoryName);
                TsV("N2", "rt46_run_id", feed.Source.SourceRunId);
                TsV("N2", "rt46_dir_mismatch", feed.Source.DirectoryRunIdMismatch ? "1" : "0");
            }

            _shotCsv = _csv46;
            _shotSha = _sha46;
            _shotSchema = feed != null ? feed.SchemaLabel : "none";

            if (feed != null && feed.Source != null)
            {
                // 截图/对照要停在同一帧：hold 住游标（自检用；不改生产默认）。
                feed.Source.ForceLoop(false);
                feed.Source.SeekFraction(0.35f);
                feed.Source.SetHold(true);
            }

            Enqueue(StepMismatchProbe, 1.0d);
        }

        /// <summary>
        /// night30 N2：把同一份 RT46 行数据放到"另一个会话目录名"下再解析，必须判为身份错配。
        /// 证明修的是"前缀误判"，不是"把错配检测整个关掉"。只写本交付目录 checks/identity/**。
        /// </summary>
        static void StepMismatchProbe()
        {
            try
            {
                var truth = new ReplayTwinUiDataSource(_csv46);
                string runId = truth.SourceRunId;
                if (string.IsNullOrEmpty(runId) || runId.IndexOf('|') >= 0)
                {
                    Fail("N2 mismatch probe setup", "runId='" + runId + "'");
                }
                else
                {
                    string wrongDir = BatchRoot + @"\checks\identity\mismatch\session_20261104_204642_850475_E4_B\raw";
                    Directory.CreateDirectory(wrongDir);
                    string copy = wrongDir + @"\rt_raw_probe_" + Path.GetFileName(_csv46);
                    if (!File.Exists(copy) || new FileInfo(copy).Length != new FileInfo(_csv46).Length)
                    {
                        File.Copy(_csv46, copy, true);
                    }

                    var probe = new ReplayTwinUiDataSource(copy);
                    Log("N2 mismatch probe dir='" + probe.DirectoryName + "' run='" + probe.SourceRunId
                        + "' unique=" + probe.IdentityUnique + " mismatch=" + probe.DirectoryRunIdMismatch
                        + " schema=" + probe.SchemaLabel + " note=" + probe.ParseNote);
                    TsV("N2", "probe_copy", copy);
                    TsV("N2", "probe_dir", probe.DirectoryName);
                    TsV("N2", "probe_mismatch", probe.DirectoryRunIdMismatch ? "1" : "0");
                    Check("N2 identity: same rows under a different session dir IS flagged as mismatch",
                        probe.IdentityUnique && probe.DirectoryRunIdMismatch,
                        "dir='" + probe.DirectoryName + "' run='" + probe.SourceRunId
                        + "' unique=" + probe.IdentityUnique + " mismatch=" + probe.DirectoryRunIdMismatch);
                }
            }
            catch (Exception e)
            {
                Fail("N2 mismatch probe exception", e.GetType().Name + ": " + e.Message);
            }

            Enqueue(StepDump46, 0.2d);
        }

        static void StepDump46()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F5 rt46 feed lost", "feed=null");
                Enqueue(StepPausePrep, 0.2d);
                return;
            }

            var src = feed.Source;
            var snap = feed.Snapshot;
            int row = src.CursorIndex;
            Log("F5 same-frame cursorRow=" + row + " axis=" + src.TimeAxis + " schema=" + src.SchemaLabel
                + " clockNs=" + src.PlaybackClockNs + " progress=" + feed.Progress01.ToString("F3"));
            TsV("F5", "cursor_row", row.ToString());
            TsV("F5", "time_axis", src.TimeAxis);
            TsV("F5", "time_axis_reason", src.TimeAxisReason);
            TsV("F5", "read_mode", "seek(0.35)->hold->read snapshot+UI");

            if (snap == null)
            {
                Fail("F5 snapshot null", "cursorRow=" + row);
                Enqueue(StepPausePrep, 0.2d);
                return;
            }

            TsV("F5", "status_display", snap.Status.Display);
            TsV("F5", "status_provided", snap.Status.Provided ? "1" : "0");
            TsV("F5", "status_normal", snap.Status.IsNormal ? "1" : "0");
            TsV("F5", "caps", TwinUiCapability.ExtendedBits(snap.Capabilities));
            TsV("F5", "identity_unique", snap.IdentityUnique ? "1" : "0");
            TsV("F5", "quality", snap.Link.Quality);
            TsV("F5", "sample_hz_window", snap.Link.SampleHz.ToString("F3"));
            TsV("F5", "device", snap.Device.Present ? snap.Device.ToCompact() : "未提供");
            TsV("F5", "validity", snap.Validity.ToText());
            TsV("F5", "clock_domains", snap.Clocks.ToText());
            Array("tcp_mm", snap.RawTcp.PositionMm);
            Array("tcp_abc_deg", snap.RawTcp.AbcDeg);
            Array("force_n", snap.Force.HasForce ? snap.Force.Force : null);
            Array("moment_nm", snap.Force.HasMoment ? snap.Force.Moment : null);
            Array("joint_torque_nm", snap.Joints.TorqueNm);
            Array("joint_deg", snap.Joints.PositionDeg);
            Array("joint_vel_deg_s", snap.Joints.VelocityDegPerSec);

            // 同一帧的卡片实读文本（证明屏幕上真的显示了这些值，而不是只在快照里）
            DumpTexts("joints");
            DumpTexts("tcp");
            DumpTexts("wrench");
            DumpTexts("torque");
            DumpTexts("sync");
            DumpTexts("session");

            string tcpRow = CardJoined("tcp");
            string wrenchRow = CardJoined("wrench");
            string torqueRow = CardJoined("torque");
            Check("F5 TCP card shows a value (not 未订阅)",
                tcpRow.Length > 0 && !tcpRow.Contains("未订阅"), "tcp=" + Trim(tcpRow));
            Check("F5 wrench card shows a value (not 未订阅)",
                wrenchRow.Length > 0 && !wrenchRow.Contains("未订阅"), "wrench=" + Trim(wrenchRow));
            Check("F5 torque card shows a value (not 未订阅)",
                torqueRow.Length > 0 && !torqueRow.Contains("未订阅"), "torque=" + Trim(torqueRow));
            Check("F5 joint torque array present (6 channels)",
                snap.Joints.TorqueNm != null && snap.Joints.TorqueNm.Length >= 6,
                "len=" + (snap.Joints.TorqueNm != null ? snap.Joints.TorqueNm.Length : -1));
            Check("F5 tcp array present (3+3 channels)",
                snap.RawTcp.PositionMm != null && snap.RawTcp.PositionMm.Length >= 3,
                "len=" + (snap.RawTcp.PositionMm != null ? snap.RawTcp.PositionMm.Length : -1));

            // 详情浮层：把输入哈希写进画面，并真实点击「详情」展开
            _page.SetDetailCaption("证据 sha256=" + _sha46 + "（a/INPUTS.csv 同一文件）");
            Click("SrcDetail", "open detail");
            Enqueue(StepCheckDetail, 0.7d);
        }

        static void StepCheckDetail()
        {
            // 先把力矩卡滚进视口：图表只在可见时走 canvas rebuild，卡片在视口外时
            // LastDrawSeries 会一直停在 0（不是"纵轴坏了"，是"没画"）。这是取证前提，不是放宽断言。
            if (!TorqueCardOnScreen() && _f4ScrollTries < 15)
            {
                float[] scrolls = { 1f, 0.75f, 0.5f, 0.25f, 0f };
                float target = scrolls[_f4ScrollTries % scrolls.Length];
                _shell.SetPageScrollNormalized(target);
                _page.RefreshSourceBar();
                _f4ScrollTries++;
                Log("F4 bring torque card into view: scroll=" + target.ToString("F2")
                    + " onScreen=" + TorqueCardOnScreen() + " try=" + _f4ScrollTries);
                Enqueue(StepCheckDetail, 0.5d);
                return;
            }

            // 有界等待：图表是否已经拿到本帧数据是"帧序"问题，不是断言标准问题。
            // 不改变断言，只把"push 还没落到 chart"的竞态等掉（最多 5s），并逐次记账。
            if (_torqueChart == null || !_torqueChart.YAxisVisible || _torqueChart.LastDrawSeries <= 0)
            {
                if (_f4Tries < 10)
                {
                    _f4Tries++;
                    var waitFeed = TwinUiReplayFeed.Active;
                    Log("F4 wait #" + _f4Tries + " drawn="
                        + (_torqueChart != null ? _torqueChart.LastDrawSeries : -1)
                        + " visible=" + (_torqueChart != null && _torqueChart.YAxisVisible)
                        + " cursor=" + (waitFeed != null && waitFeed.Source != null ? waitFeed.Source.CursorIndex : -1)
                        + " push=" + (_torque != null ? _torque.ChartPushCount : -1));
                    Enqueue(StepCheckDetail, 0.5d);
                    return;
                }
            }

            _page.RefreshSourceBar();
            string detail = _page.DetailLineText;
            Log("F5 detail visible=" + _page.DetailVisible + " | " + detail.Replace("\n", " ⏎ "));
            TsV("F5", "detail_visible", _page.DetailVisible ? "1" : "0");
            TsV("F5", "detail_text", detail.Replace("\n", " ⏎ "));
            Check("F5 detail overlay visible", _page.DetailVisible, "visible=" + _page.DetailVisible);
            Check("F5 detail shows full csv path", detail.Contains(_csv46), "detail=" + detail);
            Check("F5 detail shows sha256", detail.Contains(_sha46), "detail lacks sha: " + _sha46);
            Check("F5 detail shows rt_raw schema", detail.Contains("rt_raw"), detail);

            // 纵轴：真实关节力矩输入 → 必须有 min/mid/max + 单位
            Check("F4 torque y-axis visible with real torque input", _torqueChart.YAxisVisible,
                "visible=" + _torqueChart.YAxisVisible + " drawn=" + _torqueChart.LastDrawSeries);
            Check("F4 torque y-axis unit is N·m", _torqueChart.YAxisUnitText == "N·m",
                "unit='" + _torqueChart.YAxisUnitText + "'");
            float tqMin = _torqueChart.YAxisRangeMin;
            float tqMax = _torqueChart.YAxisRangeMax;
            string tqMinText = _torqueChart.YAxisMinText;
            string tqMidText = _torqueChart.YAxisMidText;
            string tqMaxText = _torqueChart.YAxisMaxText;
            Log("F4 torque y-axis min='" + tqMinText + "' mid='" + tqMidText + "' max='" + tqMaxText
                + "' range=[" + tqMin.ToString("R") + "," + tqMax.ToString("R") + "]");
            Check("F4 torque y-axis has min/mid/max text",
                !string.IsNullOrEmpty(tqMinText) && !string.IsNullOrEmpty(tqMidText)
                && !string.IsNullOrEmpty(tqMaxText),
                "min='" + tqMinText + "' mid='" + tqMidText + "' max='" + tqMaxText + "'");
            Check("F4 torque y-axis min/mid/max are numeric and distinct",
                tqMinText != tqMidText && tqMidText != tqMaxText && tqMinText != tqMaxText,
                "min/mid/max=" + tqMinText + "/" + tqMidText + "/" + tqMaxText);
            Check("F4 torque y-axis top/bottom match chart range",
                tqMaxText == TwinUiLineChart.FormatAxisValue(tqMax)
                && tqMinText == TwinUiLineChart.FormatAxisValue(tqMin),
                "max=" + tqMaxText + "/" + TwinUiLineChart.FormatAxisValue(tqMax)
                + " min=" + tqMinText + "/" + TwinUiLineChart.FormatAxisValue(tqMin));
            TsV("F4", "torque_y_unit", _torqueChart.YAxisUnitText);
            TsV("F4", "torque_y_min", tqMinText + " (" + tqMin.ToString("R") + ")");
            TsV("F4", "torque_y_mid", tqMidText);
            TsV("F4", "torque_y_max", tqMaxText + " (" + tqMax.ToString("R") + ")");
            TsV("F4", "joints_y_unit", _jointsChart != null ? _jointsChart.YAxisUnitText : "n/a");
            TsV("F4", "joints_y_min", _jointsChart != null ? _jointsChart.YAxisMinText : "n/a");
            TsV("F4", "joints_y_mid", _jointsChart != null ? _jointsChart.YAxisMidText : "n/a");
            TsV("F4", "joints_y_max", _jointsChart != null ? _jointsChart.YAxisMaxText : "n/a");
            Check("F4 joints y-axis unit is deg", _jointsChart != null && _jointsChart.YAxisUnitText == "°",
                _jointsChart != null ? "'" + _jointsChart.YAxisUnitText + "'" : "chart=null");

            // 详情浮层里的完整文本没有被裁（行数/高度都在框内）
            var detailText = _page.DetailPanel != null
                ? _page.DetailPanel.GetComponentInChildren<TMP_Text>(true)
                : null;
            if (detailText != null)
            {
                Vector2 pref = detailText.GetPreferredValues();
                float h = detailText.rectTransform.rect.height;
                int lines = detailText.textInfo != null ? detailText.textInfo.lineCount : -1;
                Log("F5 detail layout pref=" + pref.x.ToString("F1") + "x" + pref.y.ToString("F1")
                    + " rect=" + detailText.rectTransform.rect.width.ToString("F1") + "x" + h.ToString("F1")
                    + " lines=" + lines);
                TsV("F5", "detail_pref_h", pref.y.ToString("F1"));
                TsV("F5", "detail_rect_h", h.ToString("F1"));
                TsV("F5", "detail_line_count", lines.ToString());
                Check("F5 detail text fits inside the overlay (not clipped)",
                    pref.y <= h + 1.5f, "prefH=" + pref.y.ToString("F1") + " rectH=" + h.ToString("F1"));
            }

            // 关掉详情，进入暂停测试（顺手把页面滚回顶部，后面各步不依赖上一段的滚动位置）
            _shell.SetPageScrollNormalized(1f);
            Click("SrcDetail", "close detail");
            Enqueue(StepPausePrep, 0.5d);
        }

        static void StepPausePrep()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F1 no replay feed for pause test", "feed=null");
                Enqueue(StepLoopPrep, 0.3d);
                return;
            }

            feed.Source.SetHold(false); // 交回生产暂停控制
            TwinUiPlaybackControl.Reset();
            if (feed.Source.LoopEnabled)
            {
                feed.Source.ForceLoop(false);
            }

            _playbackBeforePause = feed.PlaybackSeconds;
            _cursorBeforePause = feed.Source.CursorIndex;
            _pushBeforePause = _torque.ChartPushCount;
            _page.RefreshSourceBar();
            Log("F1 pause prep playback=" + _playbackBeforePause.ToString("F3")
                + " cursor=" + _cursorBeforePause + " push=" + _pushBeforePause
                + " button='" + (_page.PauseButton != null && _page.PauseButton.Label != null
                    ? _page.PauseButton.Label.text : "?") + "'");
            Check("F1 pause button exists in product SourceBar",
                _page.PauseButton != null && _page.PauseButton.Button != null,
                "button=" + (_page.PauseButton != null));
            Click("SrcPause", "pause");
            Enqueue(StepCheckPaused, 1.4d);
        }

        static void StepCheckPaused()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F1 feed lost while paused", "feed=null");
                Enqueue(StepLoopPrep, 0.2d);
                return;
            }

            double playback = feed.PlaybackSeconds;
            int cursor = feed.Source.CursorIndex;
            string label = _page.PauseButton != null && _page.PauseButton.Label != null
                ? _page.PauseButton.Label.text : "?";
            _page.RefreshSourceBar();
            Log("F1 paused: playback " + _playbackBeforePause.ToString("F3") + " -> " + playback.ToString("F3")
                + " | cursor " + _cursorBeforePause + " -> " + cursor
                + " | feed.Paused=" + feed.Paused + " | ctrl=" + TwinUiPlaybackControl.IsPaused
                + " | label='" + label + "'");
            TsV("F1", "playback_before_pause_s", _playbackBeforePause.ToString("R"));
            TsV("F1", "playback_after_pause_s", playback.ToString("R"));
            TsV("F1", "cursor_before_pause", _cursorBeforePause.ToString());
            TsV("F1", "cursor_after_pause", cursor.ToString());
            TsV("F1", "button_label_while_paused", label);
            Check("F1 pause stops the replay cursor (playback clock frozen)",
                Math.Abs(playback - _playbackBeforePause) < 0.02d,
                "before=" + _playbackBeforePause.ToString("F3") + " after=" + playback.ToString("F3"));
            Check("F1 pause holds the row cursor", cursor == _cursorBeforePause,
                "before=" + _cursorBeforePause + " after=" + cursor);
            Check("F1 feed reports paused", feed.Paused && TwinUiPlaybackControl.IsPaused,
                "feed=" + feed.Paused + " ctrl=" + TwinUiPlaybackControl.IsPaused);
            Check("F1 button label flips to 播放", label == "播放", "label='" + label + "'");
            Enqueue(StepResume, 0.2d);
        }

        static void StepResume()
        {
            double before = TwinUiReplayFeed.Active != null ? TwinUiReplayFeed.Active.PlaybackSeconds : 0d;
            Click("SrcPause", "resume");
            Enqueue(() =>
            {
                var feed = TwinUiReplayFeed.Active;
                double after = feed != null ? feed.PlaybackSeconds : -1d;
                string label = _page.PauseButton != null && _page.PauseButton.Label != null
                    ? _page.PauseButton.Label.text : "?";
                Log("F1 resumed: playback " + before.ToString("F3") + " -> " + after.ToString("F3")
                    + " paused=" + TwinUiPlaybackControl.IsPaused + " label='" + label + "'");
                TsV("F1", "playback_before_resume_s", before.ToString("R"));
                TsV("F1", "playback_after_resume_s", after.ToString("R"));
                Check("F1 resume advances the playback clock again", after > before + 0.1d,
                    before.ToString("F3") + " -> " + after.ToString("F3"));
                Check("F1 button label back to 暂停", label == "暂停", "label='" + label + "'");
                Enqueue(StepFreezePrep, 0.3d);
            }, 1.0d);
        }

        static void StepFreezePrep()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null)
            {
                Fail("F2 freeze needs replay feed", "feed=null");
                Enqueue(StepLoopPrep, 0.2d);
                return;
            }

            TwinUiViewFreeze.Reset();
            _playbackBeforeFreeze = feed.PlaybackSeconds;
            _pushBeforeFreeze = _torque.ChartPushCount;
            Log("F2 freeze prep playback=" + _playbackBeforeFreeze.ToString("F3")
                + " push=" + _pushBeforeFreeze);
            var freeze = FindUnder(_torque.View, "Freeze");
            if (freeze == null)
            {
                Fail("F2 torque freeze button found", "null");
                Enqueue(StepCheckFrozen, 0.2d);
                return;
            }

            DispatchClick(freeze, "torque Freeze");
            Enqueue(StepCheckFrozen, 1.3d);
        }

        static void StepCheckFrozen()
        {
            var feed = TwinUiReplayFeed.Active;
            double playback = feed != null ? feed.PlaybackSeconds : -1d;
            int push = _torque.ChartPushCount;
            Log("F2 frozen: playback " + _playbackBeforeFreeze.ToString("F3") + " -> "
                + playback.ToString("F3") + " | chartPush " + _pushBeforeFreeze + " -> " + push
                + " | frozen=" + TwinUiViewFreeze.IsFrozen);
            TsV("F2", "freeze_playback_before_s", _playbackBeforeFreeze.ToString("R"));
            TsV("F2", "freeze_playback_after_s", playback.ToString("R"));
            TsV("F2", "freeze_push_before", _pushBeforeFreeze.ToString());
            TsV("F2", "freeze_push_after", push.ToString());
            Check("F2 freeze is a different control than pause (freeze button clicked)",
                TwinUiViewFreeze.IsFrozen && !TwinUiPlaybackControl.IsPaused,
                "frozen=" + TwinUiViewFreeze.IsFrozen + " paused=" + TwinUiPlaybackControl.IsPaused);
            Check("F2 freeze does NOT stop the replay clock (clock keeps running)",
                playback > _playbackBeforeFreeze + 0.1d,
                _playbackBeforeFreeze.ToString("F3") + " -> " + playback.ToString("F3"));
            Check("F2 freeze stops the card redraw (chart push count unchanged)",
                push == _pushBeforeFreeze, "before=" + _pushBeforeFreeze + " after=" + push);

            // 解冻，进入循环边界测试
            var freeze = FindUnder(_torque.View, "Freeze");
            if (freeze != null)
            {
                DispatchClick(freeze, "torque Freeze (unfreeze)");
            }

            Enqueue(StepLoopPrep, 0.4d);
        }

        static void StepLoopPrep()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F3 loop test needs replay feed", "feed=null");
                Enqueue(StepLoadRt40, 0.2d);
                return;
            }

            TwinUiViewFreeze.Reset();
            TwinUiPlaybackControl.Reset();
            feed.Source.SetHold(false);
            feed.Source.ForceLoop(true);
            feed.Source.SeekFraction(0.9985f);
            _wrapsBefore = feed.Source.LoopWraps;
            _segmentBeforeWrap = TwinUiSourceSwitch.Segment;
            _cursorBeforeWrap = feed.Source.CursorIndex;
            _wrapPollTries = 0;
            _wrapWallTime = Time.unscaledTime;
            Log("F3 loop prep: loop=" + feed.Source.LoopEnabled + " cursor=" + _cursorBeforeWrap
                + " wraps=" + _wrapsBefore + " segment=" + _segmentBeforeWrap
                + " total=" + feed.TotalSeconds.ToString("F2") + "s");
            Enqueue(PollLoopWrap, 0.4d);
        }

        static void PollLoopWrap()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F3 loop wrap: feed lost", "feed=null");
                Enqueue(StepLoadRt40, 0.2d);
                return;
            }

            if (feed.Source.LoopWraps > _wrapsBefore)
            {
                _wrapWallTime = Time.unscaledTime;
                Enqueue(StepCheckWrap, 0.45d);
                return;
            }

            _wrapPollTries++;
            if (_wrapPollTries > 30)
            {
                Fail("F3 loop wrap never happened", "tries=" + _wrapPollTries
                    + " cursor=" + feed.Source.CursorIndex + " playback=" + feed.PlaybackSeconds.ToString("F3")
                    + " loop=" + feed.Source.LoopEnabled);
                Enqueue(StepLoadRt40, 0.2d);
                return;
            }

            EnqueueFront(PollLoopWrap, 0.2d);
        }

        static void StepCheckWrap()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed == null || feed.Source == null)
            {
                Fail("F3 loop wrap check: feed lost", "feed=null");
                Enqueue(StepLoadRt40, 0.2d);
                return;
            }

            var src = feed.Source;
            int cursor = src.CursorIndex;
            int segment = TwinUiSourceSwitch.Segment;
            int wraps = src.LoopWraps;
            float oldest = _torque.BufferOldestTime;
            float newest = _torque.BufferNewestTime;
            // 分段变化的**产品侧时刻**（卡片就是在这个时刻被清缓冲的），用它判断缓冲里有没有跨周期残留。
            float segmentAt = TwinUiSourceSwitch.LastSegmentChangeAt;
            Log("F3 after wrap: wraps " + _wrapsBefore + " -> " + wraps
                + " | segment " + _segmentBeforeWrap + " -> " + segment
                + " | cursor " + _cursorBeforeWrap + " -> " + cursor
                + " | buffer[" + oldest.ToString("F3") + "," + newest.ToString("F3") + "]"
                + " segmentAt=" + segmentAt.ToString("F3") + " wrapPolledAt=" + _wrapWallTime.ToString("F3")
                + " | wrapAtNs=" + src.LoopWrapAtNs + " | playback=" + feed.PlaybackSeconds.ToString("F3"));
            TsV("F3", "loop_wraps_before", _wrapsBefore.ToString());
            TsV("F3", "loop_wraps_after", wraps.ToString());
            TsV("F3", "segment_before_wrap", _segmentBeforeWrap.ToString());
            TsV("F3", "segment_after_wrap", segment.ToString());
            TsV("F3", "cursor_before_wrap", _cursorBeforeWrap.ToString());
            TsV("F3", "cursor_after_wrap", cursor.ToString());
            TsV("F3", "buffer_oldest_after_wrap_s", oldest.ToString("R"));
            TsV("F3", "buffer_newest_after_wrap_s", newest.ToString("R"));
            TsV("F3", "segment_changed_at_s", segmentAt.ToString("R"));
            TsV("F3", "wrap_wall_s", _wrapWallTime.ToString("R"));
            TsV("F3", "loop_wrap_count_api", TwinUiSourceSwitch.LoopWrapCount.ToString());
            Check("F3 loop wrap increments the wrap counter", wraps == _wrapsBefore + 1,
                _wrapsBefore + " -> " + wraps);
            Check("F3 loop wrap resets the row cursor (playback continues, not stuck at end)",
                cursor < _cursorBeforeWrap && cursor >= 0,
                _cursorBeforeWrap + " -> " + cursor);
            Check("F3 loop wrap bumps the curve segment (charts clear, no cross-cycle line)",
                segment == _segmentBeforeWrap + 1,
                _segmentBeforeWrap + " -> " + segment);
            Check("F3 curve buffer contains only post-wrap samples", oldest >= segmentAt - 0.05f,
                "oldest=" + oldest.ToString("F3") + " segmentAt=" + segmentAt.ToString("F3"));
            Check("F3 playback clock is past the wrap point", feed.PlaybackSeconds < feed.TotalSeconds * 0.5d,
                "playback=" + feed.PlaybackSeconds.ToString("F3") + "/" + feed.TotalSeconds.ToString("F2"));

            // 截图前停在一个稳定帧，关循环避免截图时又被清空
            src.ForceLoop(false);
            src.SeekFraction(0.45f);
            src.SetHold(true);
            Enqueue(StepLoadRt40, 0.7d);
        }

        static void StepLoadRt40()
        {
            var feed = TwinUiReplayFeed.Active;
            if (feed != null && feed.Source != null)
            {
                feed.Source.SetHold(false);
            }

            TwinUiPlaybackControl.Reset();
            Click("SrcNext", "load RT raw 40-col");
            _csv40 = TwinUiSourceSwitch.CurrentCsv;
            _sha40 = Sha256(_csv40);
            var f = TwinUiReplayFeed.Active;
            string schema = f != null ? f.SchemaLabel : "none";
            Log("F5 rt40 attach run=" + TwinUiSourceSwitch.CurrentRunId + " schema=" + schema
                + " rows=" + (f != null ? f.RowCount : -1));
            TsV("F5", "input_id", "RT40");
            TsV("F5", "csv", _csv40);
            TsV("F5", "sha256", _sha40);
            TsV("F5", "schema", schema);
            TsV("F5", "run_id", TwinUiSourceSwitch.CurrentRunId);
            Check("F5 second product load is RT raw 40-column", schema.StartsWith("rt_raw", StringComparison.Ordinal),
                "schema=" + schema);
            Check("F5 rt40 path exists on disk", File.Exists(_csv40), _csv40);

            if (f != null && f.Source != null)
            {
                f.Source.ForceLoop(false);
                f.Source.SeekFraction(0.45f);
                f.Source.SetHold(true);
            }

            Enqueue(StepDump40, 0.9d);
        }

        static void StepDump40()
        {
            var feed = TwinUiReplayFeed.Active;
            var snap = feed != null ? feed.Snapshot : null;
            if (snap == null)
            {
                Fail("F5 rt40 snapshot null", "feed=" + (feed != null));
                Enqueue(StepShotsStart, 0.2d);
                return;
            }

            Log("F5 rt40 cursor=" + feed.Source.CursorIndex + " schema=" + feed.Source.SchemaLabel
                + " status=" + snap.Status.Display + " caps=" + TwinUiCapability.ExtendedBits(snap.Capabilities));
            TsV("F5", "status_display_40", snap.Status.Display);
            TsV("F5", "status_provided_40", snap.Status.Provided ? "1" : "0");
            TsV("F5", "caps_40", TwinUiCapability.ExtendedBits(snap.Capabilities));
            Array("rt40_tcp_mm", snap.RawTcp.PositionMm);
            Array("rt40_force_n", snap.Force.HasForce ? snap.Force.Force : null);
            Array("rt40_joint_torque_nm", snap.Joints.TorqueNm);
            TsV("F5", "rt40_device", snap.Device.Present ? snap.Device.ToCompact() : "未提供");
            Check("F5 rt40 exposes torque channels", snap.Joints.TorqueNm != null && snap.Joints.TorqueNm.Length >= 6,
                "len=" + (snap.Joints.TorqueNm != null ? snap.Joints.TorqueNm.Length : -1));
            Check("F5 rt40 exposes tcp channels", snap.RawTcp.PositionMm != null,
                "tcp=null?" + (snap.RawTcp.PositionMm == null));
            Check("F5 rt40 status column absent -> 未提供 (not fabricated)",
                !snap.Status.Provided || snap.Status.Display.Contains("未提供"),
                "provided=" + snap.Status.Provided + " display=" + snap.Status.Display);

            // 回到 RT46 做三档截图（RT40 在 1200 单独留一张带详情的）
            _csv46 = _csv46 ?? TwinUiSourceSwitch.CurrentCsv;
            Enqueue(() =>
            {
                var f = TwinUiReplayFeed.Active;
                if (f != null && f.Source != null)
                {
                    f.Source.SetHold(false);
                }

                Enqueue(StepShotsStart, 0.1d);
            }, 0.1d);
        }

        // ---- 三档截图（RT46） ----

        static int _shotWidthIndex;
        static Action _afterShots;

        static void StepShotsStart()
        {
            _shotWidthIndex = 0;
            _afterShots = StepShotRt40Detail;
            LoadRt46ForShots();
        }

        static void LoadRt46ForShots()
        {
            // 用产品入口回到同一个 46 列输入：SetReplay(csv) 与按钮同一入口，避免靠"点 N 次"碰运气。
            bool ok = TwinUiSourceSwitch.SetReplay(_csv46);
            var f = TwinUiReplayFeed.Active;
            Log("shots: reload rt46 ok=" + ok + " schema=" + (f != null ? f.SchemaLabel : "none")
                + " err=" + TwinUiSourceSwitch.LastError);
            Check("F5 screenshots run on the RT raw 46 input",
                ok && f != null && f.SchemaLabel.StartsWith("rt_raw", StringComparison.Ordinal),
                "ok=" + ok + " schema=" + (f != null ? f.SchemaLabel : "none"));
            if (f != null && f.Source != null)
            {
                f.Source.ForceLoop(false);
                f.Source.SeekFraction(0.42f);
                f.Source.SetHold(true);
            }

            _shotCsv = _csv46;
            _shotSha = _sha46;
            _shotSchema = f != null ? f.SchemaLabel : "none";

            Enqueue(StepShotWidth, 1.0d);
        }

        static void StepShotWidth()
        {
            if (_shotWidthIndex >= Widths.Length)
            {
                var next = _afterShots;
                _afterShots = null;
                if (next != null)
                {
                    Enqueue(next, 0.2d);
                }
                else
                {
                    Enqueue(FinishStep, 0.2d);
                }

                return;
            }

            int w = Widths[_shotWidthIndex];
            SetWidthOnly(w);
            Enqueue(() =>
            {
                _shell.SetPageScrollNormalized(1f);
                _page.RefreshSourceBar();
                LayoutPage();
                CheckSourceBarLayout(w, "top");
                CheckSixCardsCovered(w, "top");
                Enqueue(() => Capture("fix1_rt46_" + w + "x" + ShotHeight + "_top", StepShotVerifyTop), 0.45d);
            }, 0.9d);
        }

        static void StepShotVerifyTop()
        {
            if (!VerifyPendingShot())
            {
                Enqueue(FinishStep, 0.1d);
                return;
            }

            int w = Widths[_shotWidthIndex];
            // fix1：720 档一屏放不下六张核心卡，只截"顶部/底部"会漏掉中间的 wrench/sync。
            // 这里再做两次中间滚动（0.66 / 0.33）并把中间档也截一张，最后在底部断言"六张都到过视口"。
            Enqueue(() =>
            {
                _shell.SetPageScrollNormalized(0.66f);
                CheckSixCardsCovered(w, "sweep66");
                Enqueue(() =>
                {
                    _shell.SetPageScrollNormalized(0.33f);
                    CheckSixCardsCovered(w, "sweep33");
                    Enqueue(() => Capture("fix1_rt46_" + w + "x" + ShotHeight + "_mid", StepShotVerifyMid), 0.45d);
                }, 0.55d);
            }, 0.25d);
        }

        static void StepShotVerifyMid()
        {
            if (!VerifyPendingShot())
            {
                Enqueue(FinishStep, 0.1d);
                return;
            }

            int w = Widths[_shotWidthIndex];
            Enqueue(() =>
            {
                _shell.SetPageScrollNormalized(0f);
                CheckSixCardsCovered(w, "bottom");
                Enqueue(() => Capture("fix1_rt46_" + w + "x" + ShotHeight + "_cards", StepShotVerifyCards), 0.45d);
            }, 0.55d);
        }

        static void StepShotVerifyCards()
        {
            if (!VerifyPendingShot())
            {
                Enqueue(FinishStep, 0.1d);
                return;
            }

            _shotWidthIndex++;
            Enqueue(StepShotWidth, 0.25d);
        }

        static void StepShotRt40Detail()
        {
            // RT40 + 详情浮层（1200 档）——证明 40 列输入同样能从产品入口加载
            SetWidthOnly(1200);
            Enqueue(() =>
            {
                _shell.SetPageScrollNormalized(1f);
                LayoutPage();
                bool ok = TwinUiSourceSwitch.SetReplay(_csv40);
                var f = TwinUiReplayFeed.Active;
                if (f != null && f.Source != null)
                {
                    f.Source.ForceLoop(false);
                    f.Source.SeekFraction(0.45f);
                    f.Source.SetHold(true);
                }

                _page.SetDetailCaption("证据 sha256=" + _sha40 + "（rt_raw 40 列）");
                _page.SetDetailOpen(true);
                _page.RefreshSourceBar();
                _shotCsv = _csv40;
                _shotSha = _sha40;
                _shotSchema = f != null ? f.SchemaLabel : "none";
                Log("shots rt40 detail: ok=" + ok + " schema=" + (f != null ? f.SchemaLabel : "none")
                    + " csv=" + _csv40 + " sha256=" + _sha40);
                Check("F5 RT40 detail screenshot source is rt_raw 40",
                    ok && f != null && f.SchemaLabel.Contains("40"), "schema=" + (f != null ? f.SchemaLabel : "none"));
                Enqueue(() => Capture("fix1_rt40_1200x1080_detail", StepShotVerifyDetail), 0.9d);
            }, 0.8d);
        }

        static void StepShotVerifyDetail()
        {
            VerifyPendingShot();
            _page.SetDetailOpen(false);
            Enqueue(StepShot720Detail, 0.3d);
        }

        /// <summary>
        /// night30：720 窄屏"关键来源"取证 —— 会话名在 720 只能省略号，但关键来源
        /// （完整 CSV 路径 / sha256 / schema）必须在「详情」浮层里可读且不被裁。
        /// </summary>
        static void StepShot720Detail()
        {
            SetWidthOnly(720);
            Enqueue(() =>
            {
                _shell.SetPageScrollNormalized(1f);
                LayoutPage();
                bool ok = TwinUiSourceSwitch.SetReplay(_csv46);
                var f = TwinUiReplayFeed.Active;
                if (f != null && f.Source != null)
                {
                    f.Source.ForceLoop(false);
                    f.Source.SeekFraction(0.35f);
                    f.Source.SetHold(true);
                }

                _page.SetDetailCaption("证据 sha256=" + _sha46 + "（rt_raw 46 列 · 720 窄屏）");
                _page.SetDetailOpen(true);
                _page.RefreshSourceBar();
                _shotCsv = _csv46;
                _shotSha = _sha46;
                _shotSchema = f != null ? f.SchemaLabel : "none";

                Check("N3 720 key source load ok via product entry",
                    ok && f != null && f.SchemaLabel.StartsWith("rt_raw", StringComparison.Ordinal),
                    "ok=" + ok + " schema=" + (f != null ? f.SchemaLabel : "none"));
                CheckSourceBarLayout(720, "detail");
                Enqueue(() =>
                {
                    string detail = _page.DetailLineText;
                    Log("N3 720 detail visible=" + _page.DetailVisible + " | " + detail.Replace("\n", " ⏎ "));
                    TsV("N3", "detail720_visible", _page.DetailVisible ? "1" : "0");
                    TsV("N3", "detail720_text", detail.Replace("\n", " ⏎ "));
                    Check("N3 720 key source: detail overlay visible", _page.DetailVisible,
                        "visible=" + _page.DetailVisible);
                    Check("N3 720 key source: full csv path readable", detail.Contains(_csv46), "detail=" + detail);
                    Check("N3 720 key source: sha256 readable", detail.Contains(_sha46), "detail lacks sha=" + _sha46);
                    Check("N3 720 key source: schema readable", detail.Contains("rt_raw"), detail);

                    var detailText = _page.DetailPanel != null
                        ? _page.DetailPanel.GetComponentInChildren<TMP_Text>(true)
                        : null;
                    if (detailText != null)
                    {
                        Vector2 pref = detailText.GetPreferredValues();
                        float h = detailText.rectTransform.rect.height;
                        Log("N3 720 detail layout pref=" + pref.y.ToString("F1") + " rectH=" + h.ToString("F1"));
                        TsV("N3", "detail720_pref_h", pref.y.ToString("F1"));
                        TsV("N3", "detail720_rect_h", h.ToString("F1"));
                        Check("N3 720 key source: detail text fits (not clipped)",
                            pref.y <= h + 1.5f, "prefH=" + pref.y.ToString("F1") + " rectH=" + h.ToString("F1"));
                    }
                    else
                    {
                        Fail("N3 720 key source: detail text present", "text=null");
                    }

                    Enqueue(() => Capture("fix1_rt46_720x1080_detail", StepShotVerify720Detail), 0.5d);
                }, 0.5d);
            }, 0.8d);
        }

        static void StepShotVerify720Detail()
        {
            VerifyPendingShot();
            _page.SetDetailOpen(false);
            Enqueue(FinishStep, 0.3d);
        }

        // ------------------------------------------------------------------ 截图

        static void Capture(string name, Action next)
        {
            string path = _shotsRoot + "\\" + name + ".png";
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _pendingShotPath = path;
            _pendingShotExpect = name;
            ScreenCapture.CaptureScreenshot(path);
            Log("SHOT request " + name + " screen=" + Screen.width + "x" + Screen.height
                + " scroll=" + _shell.PageScrollNormalized.ToString("F2"));
            Enqueue(next, 1.1d);
        }

        static bool VerifyPendingShot()
        {
            int w;
            int h;
            bool ok = TryReadPngSize(_pendingShotPath, out w, out h);
            bool pass = ok && w == Screen.width && h == Screen.height;
            Log("SHOT verify " + _pendingShotExpect + " path=" + _pendingShotPath
                + " ihdr=" + w + "x" + h + " expected=" + Screen.width + "x" + Screen.height
                + " exists=" + File.Exists(_pendingShotPath));
            TsV("SHOT", _pendingShotExpect, w + "x" + h);
            // night30：每张截图配一份旁注（输入路径 / sha256 / schema / 尺寸），不让"截图"脱离输入身份。
            Log("SHOT annotate " + _pendingShotExpect + " csv=" + _shotCsv + " sha256=" + _shotSha
                + " schema=" + _shotSchema + " ihdr=" + w + "x" + h
                + " screen=" + Screen.width + "x" + Screen.height);
            TsV("SHOT_ANNOTATE", _pendingShotExpect,
                _shotSha + "|" + _shotSchema + "|" + _shotCsv + "|" + Screen.width + "x" + Screen.height);
            try
            {
                File.WriteAllText(_pendingShotPath.Replace(".png", ".txt"),
                    "file=" + _pendingShotExpect + ".png" + Environment.NewLine
                    + "csv=" + _shotCsv + Environment.NewLine
                    + "sha256=" + _shotSha + Environment.NewLine
                    + "schema=" + _shotSchema + Environment.NewLine
                    + "png=" + w + "x" + h + Environment.NewLine
                    + "screen=" + Screen.width + "x" + Screen.height + Environment.NewLine
                    + "attempt=" + _stamp + Environment.NewLine);
            }
            catch (Exception e)
            {
                Log("SHOT annotate write failed: " + e.Message);
            }
            Check("shot " + _pendingShotExpect + " real pixels", pass,
                "ihdr=" + w + "x" + h + " screen=" + Screen.width + "x" + Screen.height);
            _pendingShotPath = null;
            _pendingShotExpect = null;
            return pass;
        }

        // ------------------------------------------------------------------ 布局断言

        static void LayoutPage()
        {
            Rect r = _window.WindowRect;
            r.width = Mathf.Max(320f, Screen.width - 16f);
            r.height = Mathf.Max(400f, Screen.height - 16f);
            r.x = 8f;
            r.y = 8f;
            _window.ApplyLayout(r, false, false);
        }

        static void CheckSourceBarLayout(int width, string tag)
        {
            var bar = _page.Root != null ? _page.Root.Find("SourceBar") : null;
            if (bar == null)
            {
                Fail("F3 SourceBar present " + width + " " + tag, "bar=null");
                return;
            }

            var texts = new List<TMP_Text>();
            var tmp = bar.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmp.Length; i++)
            {
                if (tmp[i] == null || !tmp[i].gameObject.activeInHierarchy
                    || string.IsNullOrEmpty(tmp[i].text))
                {
                    continue;
                }

                texts.Add(tmp[i]);
            }

            // 1) 可见文本两两不重叠（锚点/对比度之外的"布局硬事实"）
            int overlaps = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < texts.Count; i++)
            {
                for (int j = i + 1; j < texts.Count; j++)
                {
                    Rect a = WorldRect(texts[i].rectTransform);
                    Rect b = WorldRect(texts[j].rectTransform);
                    Rect inter = Rect.MinMaxRect(
                        Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
                        Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
                    float area = Mathf.Max(0f, inter.width) * Mathf.Max(0f, inter.height);
                    if (area > 2f)
                    {
                        overlaps++;
                        sb.Append(texts[i].name).Append("×").Append(texts[j].name)
                            .Append("(").Append(area.ToString("F0")).Append("px²) ");
                    }
                }
            }

            Log("F3 layout " + width + " " + tag + ": texts=" + texts.Count + " overlaps=" + overlaps
                + (sb.Length > 0 ? " | " + sb : string.Empty));
            TsV("F3", "overlaps_" + width + "_" + tag, overlaps.ToString());
            Check("F3 no overlapping text boxes in SourceBar " + width + " " + tag, overlaps == 0, sb.ToString());

            // 2) 频率独立成格：记录频率文本不与会话名同格 / 不重叠（已在上面覆盖），再查宽度是否够
            for (int i = 0; i < texts.Count; i++)
            {
                TMP_Text t = texts[i];
                if (t.name != "RecordHz" && t.name != "DisplayHz" && t.name != "WindowLabel")
                {
                    continue;
                }

                float pref = t.GetPreferredValues().x;
                float w = t.rectTransform.rect.width;
                bool ell = t.overflowMode == TextOverflowModes.Ellipsis;
                Log("F3 text " + t.name + " width=" + w.ToString("F1") + " pref=" + pref.ToString("F1")
                    + " ellipsis=" + ell + " text='" + t.text + "'");
                Check("F3 " + t.name + " not truncated " + width + " " + tag,
                    pref <= w + 0.5f || ell, "pref=" + pref.ToString("F1") + " width=" + w.ToString("F1"));
            }

            // 3) 会话名：允许省略号，但必须真的是 Ellipsis（不是硬裁切）
            TMP_Text session = null;
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i].name == "SrcInfo")
                {
                    session = texts[i];
                }
            }

            if (session != null)
            {
                float pref = session.GetPreferredValues().x;
                float w = session.rectTransform.rect.width;
                Log("F3 session text pref=" + pref.ToString("F1") + " width=" + w.ToString("F1")
                    + " overflow=" + session.overflowMode + " text='" + session.text + "'");
                TsV("F3", "session_pref_" + width + "_" + tag, pref.ToString("F1"));
                TsV("F3", "session_width_" + width + "_" + tag, w.ToString("F1"));
                Check("F3 session name either fits or uses ellipsis " + width + " " + tag,
                    pref <= w + 0.5f || session.overflowMode == TextOverflowModes.Ellipsis,
                    "pref=" + pref.ToString("F1") + " width=" + w.ToString("F1")
                    + " overflow=" + session.overflowMode);
            }
            else
            {
                Fail("F3 session name text present " + width + " " + tag, "SrcInfo missing");
            }

            // 4) 详情按钮/浮层存在
            Check("F3 detail button present " + width + " " + tag, _page.DetailButton != null,
                "button=" + (_page.DetailButton != null));
            Check("F3 pause button present " + width + " " + tag, _page.PauseButton != null,
                "button=" + (_page.PauseButton != null));
        }

        static readonly HashSet<string> _seenCards = new HashSet<string>();

        static void CheckSixCardsCovered(int width, string tag)
        {
            string[] ids = { "joints", "tcp", "torque", "wrench", "sync", "session" };
            if (tag == "top")
            {
                _seenCards.Clear();
            }

            RectTransform viewport = ViewportRect();
            if (viewport == null)
            {
                Fail("F3 scroll viewport found " + width + " " + tag, "viewport=null");
                return;
            }

            Rect view = viewport.rect;
            int visible = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                var m = _surface.Find(ids[i]);
                if (m == null || m.View == null)
                {
                    sb.Append(ids[i]).Append("=missing ");
                    continue;
                }

                Rect r = LocalRect(viewport, (RectTransform)m.View);
                bool onScreen = r.xMax > view.xMin && r.xMin < view.xMax
                    && r.yMax > view.yMin && r.yMin < view.yMax;
                if (onScreen)
                {
                    visible++;
                    _seenCards.Add(ids[i]);
                }

                sb.Append(ids[i]).Append(onScreen ? "=on " : "=off ");
                TsV("F3", "card_" + ids[i] + "_" + width + "_" + tag,
                    r.xMin.ToString("F0") + "," + r.yMin.ToString("F0") + ","
                    + r.width.ToString("F0") + "x" + r.height.ToString("F0") + " onScreen=" + onScreen);
            }

            Log("F3 cards " + width + " " + tag + ": visibleNow=" + visible + "/6 seenSoFar=" + _seenCards.Count
                + "/6 | " + sb);
            TsV("F3", "cards_visible_" + width + "_" + tag, visible + "/6");
            if (tag == "bottom")
            {
                Check("F3 six core cards all reachable by scrolling " + width, _seenCards.Count >= 6,
                    "seen=" + _seenCards.Count + "/6 [" + string.Join(",", new List<string>(_seenCards).ToArray()) + "]");
            }
        }

        /// <summary>页内容的 ScrollRect viewport（页根 → pageContent → viewport）。</summary>
        static RectTransform ViewportRect()
        {
            if (_page == null || _page.Root == null || _page.Root.parent == null)
            {
                return null;
            }

            return _page.Root.parent.parent as RectTransform;
        }

        /// <summary>把子物体矩形换算到 viewport 的本地坐标系（不依赖 Canvas 是 Overlay 还是 Camera 模式）。</summary>
        static Rect LocalRect(RectTransform space, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float xMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = space.InverseTransformPoint(c[i]);
                xMin = Mathf.Min(xMin, p.x);
                xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y);
                yMax = Mathf.Max(yMax, p.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float xMin = Mathf.Min(Mathf.Min(c[0].x, c[1].x), Mathf.Min(c[2].x, c[3].x));
            float xMax = Mathf.Max(Mathf.Max(c[0].x, c[1].x), Mathf.Max(c[2].x, c[3].x));
            float yMin = Mathf.Min(Mathf.Min(c[0].y, c[1].y), Mathf.Min(c[2].y, c[3].y));
            float yMax = Mathf.Max(Mathf.Max(c[0].y, c[1].y), Mathf.Max(c[2].y, c[3].y));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>力矩卡当前是否在页面 viewport 里（不含"半露"以外的判定：只要有交集就算可见）。</summary>
        static bool TorqueCardOnScreen()
        {
            RectTransform viewport = ViewportRect();
            if (viewport == null || _torque == null || _torque.View == null)
            {
                return false;
            }

            Rect r = LocalRect(viewport, (RectTransform)_torque.View);
            Rect view = viewport.rect;
            return r.xMax > view.xMin && r.xMin < view.xMax && r.yMax > view.yMin && r.yMin < view.yMax;
        }

        // ------------------------------------------------------------------ 工具

        static void Click(string buttonName, string why)
        {
            GameObject go = FindGo(_page.Root, buttonName);
            if (go == null)
            {
                Fail("click target found: " + buttonName, "missing (" + why + ")");
                return;
            }

            DispatchClick(go, buttonName + " (" + why + ")");
        }

        static void DispatchClick(GameObject go, string what)
        {
            if (EventSystem.current == null)
            {
                Fail("EventSystem for click: " + what, "EventSystem.current == null");
                return;
            }

            var b = go.GetComponent<Button>();
            bool interactable = b == null || b.interactable;
            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            ExecuteEvents.Execute<IPointerClickHandler>(go, data, ExecuteEvents.pointerClickHandler);
            Log("CLICK " + what + " | go=" + go.name + " interactable=" + interactable);
            TsV("CLICK", what, "interactable=" + interactable);
        }

        static GameObject FindGo(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root.gameObject;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject found = FindGo(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static GameObject FindUnder(Transform root, string name)
        {
            return FindGo(root, name);
        }

        static string CardJoined(string cardId)
        {
            var module = _surface.Find(cardId);
            if (module == null || module.View == null)
            {
                return string.Empty;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            var sb = new StringBuilder();
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(t.name).Append('=').Append(t.text.Replace("\n", " "));
            }

            return sb.ToString();
        }

        static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s ?? string.Empty;
            }

            return s.Length <= 320 ? s : s.Substring(0, 320) + "…";
        }

        static void DumpTexts(string cardId)
        {
            var module = _surface.Find(cardId);
            if (module == null || module.View == null)
            {
                Fail("dump card " + cardId, "module missing");
                return;
            }

            var texts = module.View.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    continue;
                }

                TsV("TXT", cardId + "|" + NodePath(t.transform), (t.text ?? string.Empty).Replace("\n", " "));
            }
        }

        static string NodePath(Transform t)
        {
            var parts = new List<string>(4);
            Transform cur = t;
            for (int depth = 0; depth < 4 && cur != null; depth++)
            {
                parts.Insert(0, cur.name);
                cur = cur.parent;
            }

            return string.Join("/", parts.ToArray());
        }

        static void Array(string name, float[] values)
        {
            if (values == null || values.Length == 0)
            {
                TsV("ARR", name, "");
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            TsV("ARR", name, sb.ToString());
        }

        static string Sha256(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return "MISSING";
                }

                using (var sha = SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
            catch (Exception e)
            {
                return "ERROR:" + e.GetType().Name;
            }
        }

        static void TwinUIViewFreezeResetHelper()
        {
            TwinUiViewFreeze.Reset();
            TwinUiPlaybackControl.Reset();
        }

        // ---- 重试/收尾 ----

        static int _tries;

        static bool Retry(string what)
        {
            _tries++;
            if (_tries <= 180)
            {
                Log("waiting (" + what + ") try=" + _tries);
                EnqueueFront(Boot, 0.5d);
                return true;
            }

            Fail("wait timeout", what + " tries=" + _tries);
            Finish();
            return false;
        }

        static void FinishStep()
        {
            Finish();
        }

        static void Finish()
        {
            try
            {
                double secs = EditorApplication.timeSinceStartup - _runStartedAt;
                double mbMin = TwinUiTestIsolation.ConsoleLogMbPerMinute(
                    _consolePathAtStart, _consoleBytesAtStart, secs);
                Log("N0 isolation console growth=" + mbMin.ToString("F3") + " MB/min over "
                    + secs.ToString("F1") + "s now=" + TwinUiTestIsolation.ConsoleLogInfo());
                TsV("N0", "console_mb_per_min", mbMin.ToString("F3"));
                TsV("N0", "run_seconds", secs.ToString("F1"));
            }
            catch (Exception e)
            {
                Log("N0 isolation summary failed: " + e.Message);
            }

            try
            {
                if (_tsv != null)
                {
                    Directory.CreateDirectory(_root + @"\logs");
                    string tsv = _root + @"\logs\UI63_" + _stamp + ".tsv";
                    File.WriteAllText(tsv, _tsv.ToString());
                    _log.AppendLine("TSV=" + tsv);
                }

                if (_log != null)
                {
                    _log.AppendLine("RESULT=" + (_fails == 0 ? "PASS" : "FAIL") + " failures=" + _fails);
                    string txt = _root + @"\logs\UI63_" + _stamp + ".txt";
                    File.WriteAllText(txt, _log.ToString());
                    Debug.Log("[UI63] " + (_fails == 0 ? "PASS" : "FAIL") + " failures=" + _fails + " log=" + txt);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[UI63] finish write failed: " + e);
            }

            try
            {
                RestoreSizes();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI63] restore game view size failed: " + e.Message);
            }

            SessionState.SetBool(Armed, false);
            SessionState.SetBool(Armed + ".running", false);
            TwinUiViewFreeze.Reset();
            TwinUiPlaybackControl.Reset();
            EditorApplication.isPlaying = false;
        }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok)
            {
                _fails++;
            }

            if (_log == null)
            {
                _log = new StringBuilder();
            }

            _log.AppendLine((ok ? "PASS " : "FAIL ") + name + " | " + detail);
            Debug.Log("[UI63] " + (ok ? "PASS " : "FAIL ") + name + " | " + detail);
        }

        static void Fail(string name, string detail)
        {
            Check(name, false, detail);
        }

        static void Log(string message)
        {
            if (_log != null)
            {
                _log.AppendLine("INFO " + message);
            }
        }

        static void TsV(string kind, string key, string value)
        {
            if (_tsv == null)
            {
                return;
            }

            _tsv.Append(kind).Append('|').Append(key).Append('|').Append(value ?? string.Empty).Append('\n');
        }

        // ------------------------------------------------------------------ Game View 尺寸（真实像素）

        static readonly List<int> _addedSizes = new List<int>(4);
        static int _originalSelection = -1;

        static void SetWidth(int width, Action then)
        {
            SetWidthOnly(width);
            Enqueue(then, 1.0d);
        }

        static void SetWidthOnly(int width)
        {
            int index = _addedSizes.Count > 0 ? -1 : -1;
            object group = Group();
            Type grpType = group.GetType();
            MethodInfo total = grpType.GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo get = grpType.GetMethod("GetGameViewSize", BindingFlags.Public | BindingFlags.Instance);
            int count = (int)total.Invoke(group, null);
            int target = -1;
            for (int i = 0; i < count; i++)
            {
                object size = get.Invoke(group, new object[] { i });
                if (SizeInt(size, "width") == width && SizeInt(size, "height") == ShotHeight)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                target = AddCustom(group, width, ShotHeight, "F1 " + width + "x" + ShotHeight);
                _addedSizes.Add(target);
            }

            ApplySize(target);
            Log("game view -> " + width + "x" + ShotHeight + " selection=" + target + " (cached " + index + ")");
        }

        static void PrepareSizes()
        {
            // 只添加一次；SetWidthOnly 找不到时再补。
            for (int i = 0; i < Widths.Length; i++)
            {
                object group = Group();
                Type grpType = group.GetType();
                MethodInfo total = grpType.GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo get = grpType.GetMethod("GetGameViewSize", BindingFlags.Public | BindingFlags.Instance);
                int count = (int)total.Invoke(group, null);
                bool found = false;
                for (int k = 0; k < count; k++)
                {
                    object size = get.Invoke(group, new object[] { k });
                    if (SizeInt(size, "width") == Widths[i] && SizeInt(size, "height") == ShotHeight)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    _addedSizes.Add(AddCustom(group, Widths[i], ShotHeight,
                        "F1 " + Widths[i] + "x" + ShotHeight));
                }
            }
        }

        static void CaptureOriginalSelection()
        {
            try
            {
                Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                EditorWindow gv = EditorWindow.GetWindow(gvType);
                _originalSelection = ReadSelection(gvType, gv);
                Log("original game view selection=" + _originalSelection);
            }
            catch (Exception e)
            {
                Log("WARN read game view selection | " + e.GetType().Name);
                _originalSelection = -1;
            }
        }

        static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                byte[] head = new byte[24];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Read(head, 0, 24) < 24)
                    {
                        return false;
                    }
                }

                if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47)
                {
                    return false;
                }

                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static Type SizesType()
        {
            return typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        }

        static object Group()
        {
            Type sizes = SizesType();
            Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizes);
            object inst = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null, null);
            Type groupEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
            object standalone = Enum.Parse(groupEnum, "Standalone");
            MethodInfo getGroup = sizes.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance);
            return getGroup.Invoke(inst, new[] { standalone });
        }

        static int AddCustom(object group, int w, int h, string name)
        {
            Type sizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
            Type sizeEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType");
            object fixedRes = Enum.Parse(sizeEnum, "FixedResolution");
            ConstructorInfo ctor = sizeType.GetConstructor(new[] { sizeEnum, typeof(int), typeof(int), typeof(string) });
            object size = ctor.Invoke(new[] { fixedRes, w, h, name });
            group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(group, new[] { size });
            int total = (int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(group, null);
            return total - 1;
        }

        static int SizeInt(object size, string field)
        {
            PropertyInfo p = size.GetType().GetProperty(field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null ? (int)p.GetValue(size, null) : 0;
        }

        static void ApplySize(int selection)
        {
            Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow gv = EditorWindow.GetWindow(gvType);
            WriteSelection(gvType, gv, selection);
            gv.Repaint();
        }

        static int ReadSelection(Type gvType, EditorWindow gv)
        {
            PropertyInfo prop = SelectionProperty(gvType);
            if (prop != null && prop.CanRead)
            {
                return (int)prop.GetValue(gv, null);
            }

            return (int)SelectionField(gvType).GetValue(gv);
        }

        static void WriteSelection(Type gvType, EditorWindow gv, int selection)
        {
            PropertyInfo prop = SelectionProperty(gvType);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(gv, selection, null);
                return;
            }

            SelectionField(gvType).SetValue(gv, selection);
        }

        static PropertyInfo _selPropCache;

        static PropertyInfo SelectionProperty(Type gvType)
        {
            if (_selPropCache != null)
            {
                return _selPropCache;
            }

            string[] names = { "SizeSelection", "selectedSizeIndex" };
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo p = gvType.GetProperty(names[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite)
                {
                    _selPropCache = p;
                    return p;
                }
            }

            return null;
        }

        static FieldInfo _selFieldCache;

        static FieldInfo SelectionField(Type gvType)
        {
            if (_selFieldCache != null)
            {
                return _selFieldCache;
            }

            string[] names = { "m_SelectedSizeIndex", "selectedSizeIndex", "m_SelectedIndex" };
            for (int i = 0; i < names.Length; i++)
            {
                _selFieldCache = gvType.GetField(names[i], BindingFlags.NonPublic | BindingFlags.Instance);
                if (_selFieldCache != null)
                {
                    return _selFieldCache;
                }
            }

            throw new InvalidOperationException("GameView size selection member not found");
        }

        static void RestoreSizes()
        {
            if (_addedSizes.Count == 0 && _originalSelection < 0)
            {
                return;
            }

            try
            {
                if (_originalSelection >= 0)
                {
                    ApplySize(_originalSelection);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI63] restore game view selection failed: " + e.Message);
            }

            try
            {
                object group = Group();
                MethodInfo remove = group.GetType().GetMethod("RemoveCustomSize",
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = _addedSizes.Count - 1; i >= 0; i--)
                {
                    remove.Invoke(group, new object[] { _addedSizes[i] });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI63] remove custom game view sizes failed: " + e.Message);
            }

            _addedSizes.Clear();
            _originalSelection = -1;
        }
    }
}