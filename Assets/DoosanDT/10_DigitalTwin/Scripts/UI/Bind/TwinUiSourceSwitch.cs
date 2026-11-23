// TwinUiSourceSwitch.cs —— 数字孪生页"数据源"切换的运行时单一入口（2026-11-09 批次 A/UI：A1/A3/A4）
//                                            （2026-11-09 R2 收口：真值改为 TwinUiReplayFeed.Active 派生）
//
// 职责：把"实时 / 回放"切换、回放会话轮换、显示刷新率、曲线时间窗收敛到一处静态状态，
//       让 UI 内的常驻入口（TwinUiSourceBar）与自检（UI/59、UI/61）读写同一个来源。
//
// A-1（R2 根因修复）：本类**不再持有** Current/CurrentCsv 真值。
//   旧实现用 `private static Mode Current` 自带一套真值，而 Editor 舞台直连
//   `TwinUiReplayFeed.Ensure(csv)` 只把 `Active` 挂上、从不改 `Current` →
//   顶部状态条（读 binder→Active）显示"回放"，底栏 SourceBar（读本类 Current）显示"实时"。
//   现在 IsReplay/Current/CurrentCsv/CurrentRunId/RecordHz 全部由 `TwinUiReplayFeed.Active`
//   （且 `Loaded`）派生，任何入口（含遗留直连 Ensure）都不可能再造成不同源。
//
// 边界（硬约束，与 TwinUiReplayFeed 一致）：
//   * 只读：只发现磁盘上已存在的 samples.csv、把路径交给既有 ReplayTwinUiDataSource；
//     不连 ROS/DART、不启动节点、不写任何文件、不下发命令。
//   * 记录频率（CSV）与显示刷新率是**两个独立量**：记录频率来自 CSV 时间戳（只读，两个口径见
//     TwinUiReplayFeed.RecordHzSemantics），DisplayHz 是本类可调项，二者分别标注、分别生效（A3）。
//   * 每次切换源 Segment+1：卡片据此清空曲线缓冲，禁止跨源伪连续（A4）。
//     attach（SetReplay / Ensure）与 detach（SetLive / Shutdown）都 +1。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DigitalTwin.UI
{
    public static class TwinUiSourceSwitch
    {
        public enum Mode
        {
            Live = 0,
            Replay = 1
        }

        /// <summary>既有回放会话根目录（与 TwinUiSettings.replayCsvPath 同一数据根；只读列举）。</summary>
        public const string PaperLogsRoot = @"D:\Unity Projects\DART_R-data\PaperLogs";

        private static readonly List<string> _runs = new List<string>(32);
        private static bool _init;
        private static int _segment;

        /// <summary>源模式：由 TwinUiReplayFeed.Active 派生（A-1 单一真值；不持有独立字段）。</summary>
        public static Mode Current => IsReplay ? Mode.Replay : Mode.Live;

        /// <summary>当前回放 CSV 绝对路径（实时模式为空）；由 Active 派生。</summary>
        public static string CurrentCsv => IsReplay ? TwinUiReplayFeed.Active.CsvPath : string.Empty;

        /// <summary>源分段号：每次源切换（attach/detach）+1；卡片用它判断"要不要清空历史曲线"。</summary>
        public static int Segment => _segment;

        /// <summary>显示刷新率（Hz，可调；默认 5，与 TwinUIStatusBinder.refreshHz 初值一致）。</summary>
        public static float DisplayHz { get; private set; } = 5f;

        /// <summary>曲线时间窗（秒，可调；10 / 20 / 60 三档）。</summary>
        public static float TimeWindowSeconds { get; private set; } = 20f;

        /// <summary>当前是否回放：**唯一真值** = feed 存在且已成功加载（A-1）。</summary>
        public static bool IsReplay => TwinUiReplayFeed.Active != null && TwinUiReplayFeed.Active.Attached;

        public static int RunCount => _runs.Count;
        public static IReadOnlyList<string> Runs => _runs;

        /// <summary>最近一次切换失败的原因（供状态条显示；成功为空）。</summary>
        public static string LastError { get; private set; } = string.Empty;

        public static string ModeLabel => IsReplay ? "回放" : "实时";

        /// <summary>回放会话名（runId）；实时返回 "-"。</summary>
        public static string CurrentRunId
        {
            get
            {
                string csv = CurrentCsv;
                if (!IsReplay || string.IsNullOrEmpty(csv))
                {
                    return "-";
                }

                try
                {
                    DirectoryInfo dir = Directory.GetParent(csv);
                    string name = dir != null ? dir.Name : Path.GetFileNameWithoutExtension(csv);
                    // fix1：rt_raw 副本常放在 <session>/raw/ 下，直接取 parent 会得到 "raw"，
                    // 那不是会话身份。这里上溯到会话目录。
                    if (string.Equals(name, "raw", StringComparison.OrdinalIgnoreCase)
                        && dir != null && dir.Parent != null)
                    {
                        name = dir.Parent.Name;
                    }

                    return name;
                }
                catch (Exception)
                {
                    return Path.GetFileNameWithoutExtension(csv);
                }
            }
        }

        // ---------------------------------------------------------------- A-3 记录频率（两个口径）

        /// <summary>回放窗口（秒）——窗口速率的统计窗口，取自 feed 的 replayWindowSeconds。</summary>
        public static double RecordWindowSeconds => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordWindowSeconds
            : 0d;

        /// <summary>窗口内接收行实测 Hz（需要至少一次 Refresh；不足 2 行不可用）。</summary>
        public static double RecordHzWindow => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordHzWindow
            : 0d;

        /// <summary>整文件接收行实测 Hz（加载完成即可读）。</summary>
        public static double RecordHzFile => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordHzFile
            : 0d;

        public static int RecordRowsWindow => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordRowsWindow
            : 0;

        public static int RecordRowsFile => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordRowsFile
            : 0;

        public static double RecordSpanFileSeconds => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordSpanFileSeconds
            : 0d;

        public static bool RecordHzWindowAvailable => TwinUiReplayFeed.Active != null
            && TwinUiReplayFeed.Active.RecordHzWindowAvailable;

        public static bool RecordHzFileAvailable => TwinUiReplayFeed.Active != null
            && TwinUiReplayFeed.Active.RecordHzFileAvailable;

        /// <summary>既有硬编码目标值（TARGET 元数据，非实测）。</summary>
        public static double TargetSampleHz => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.TargetSampleHz
            : ReplayTwinUiDataSource.TargetSampleHzConstant;

        /// <summary>兼容旧调用方：窗口优先、其次整文件、都不可用为 0（显示层必须用 Available 判断）。</summary>
        public static double RecordHz => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordHz
            : 0d;

        /// <summary>当前 RecordHz 的口径："窗口" / "整文件" / "缺失"。</summary>
        public static string RecordHzScope => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.RecordHzScope
            : "缺失";

        /// <summary>A-3 频率口径说明原文（UI/自检直接打印，避免把窗口速率说成采样率）。</summary>
        public static string RecordHzSemantics => TwinUiReplayFeed.RecordHzSemantics;

        // ---------------------------------------------------------------- 会话发现

        /// <summary>首次使用前调用：发现既有回放会话（按会话名倒序，新的在前）。</summary>
        public static void EnsureInit()
        {
            if (_init)
            {
                return;
            }

            _init = true;
            Discover();
        }

        private static void Discover()
        {
            _runs.Clear();
            // fix1：先读"额外回放源清单"（真实 RT 原始 CSV），再列 PaperLogs 会话。
            LoadExtraSources();
            try
            {
                if (!Directory.Exists(PaperLogsRoot))
                {
                    LastError = _runs.Count == 0 ? "回放会话根目录不存在" : string.Empty;
                    return;
                }

                string[] dirs = Directory.GetDirectories(PaperLogsRoot);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    string csv = Path.Combine(dirs[i], "samples.csv");
                    if (File.Exists(csv))
                    {
                        _runs.Add(csv);
                        if (_runs.Count >= 32)
                        {
                            break;
                        }
                    }
                }

                LastError = _runs.Count == 0 ? "未发现 samples.csv" : string.Empty;
            }
            catch (Exception e)
            {
                LastError = "列举回放会话失败: " + e.GetType().Name;
            }
        }

        // ---------------------------------------------------------------- fix1：额外回放源（真实 RT 原始 CSV）

        /// <summary>额外回放源清单路径（可选文件；一行一个 CSV 绝对路径，# 为注释）。</summary>
        public static string ExtraSourcesManifestPath =>
            Path.Combine(Application.dataPath, "DoosanDT", "10_DigitalTwin", "Config", "ui_replay_sources.txt");

        /// <summary>本次从清单里接受了多少个真实文件（0 = 没有清单/清单为空/全部不可读）。</summary>
        public static int ExtraSourceCount { get; private set; }

        /// <summary>清单读取异常（成功为空）。</summary>
        public static string ExtraSourcesError { get; private set; } = string.Empty;

        /// <summary>
        /// 读清单并把清单项**插到会话列表最前**：这样「换回放」按钮第一下就能轮到真实 RT 原始件，
        /// 不需要点 32 次。仍然复用同一条 SetReplay→feed→ReplayTwinUiDataSource 通路（不建第二套数据链）。
        /// 只读：清单里不存在的文件只记错误、不切换、不创建目录。
        /// </summary>
        private static void LoadExtraSources()
        {
            ExtraSourceCount = 0;
            ExtraSourcesError = string.Empty;
            string manifest = ExtraSourcesManifestPath;
            try
            {
                if (!File.Exists(manifest))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(manifest);
                var extras = new List<string>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!File.Exists(line))
                    {
                        ExtraSourcesError += (ExtraSourcesError.Length > 0 ? "; " : "")
                            + "清单内文件不存在: " + line;
                        continue;
                    }

                    if (!extras.Contains(line))
                    {
                        extras.Add(line);
                    }
                }

                for (int i = extras.Count - 1; i >= 0; i--)
                {
                    _runs.Insert(0, extras[i]);
                }

                ExtraSourceCount = extras.Count;
                if (extras.Count > 0)
                {
                    Debug.Log("[SOURCE] extra replay sources=" + extras.Count + " manifest=" + manifest);
                }
            }
            catch (Exception e)
            {
                ExtraSourcesError = "读清单失败: " + e.GetType().Name;
            }
        }

        /// <summary>
        /// A-5：清运行时静态状态（域重载被关闭时静态字段会跨 Play 会话残留）。
        /// 只清"发现缓存/分段号/错误"，不动 DisplayHz/TimeWindowSeconds 这类用户可调项的默认值。
        /// </summary>
        public static void ResetRuntimeState()
        {
            _runs.Clear();
            _init = false;
            _segment = 0;
            _loopWraps = 0;
            ExtraSourceCount = 0;
            ExtraSourcesError = string.Empty;
            LastError = string.Empty;
            TwinUiReplayFeed.ResetSessionCounters();
        }

        /// <summary>Play 启动时重置静态残留（Enter Play Mode Options 关闭域重载时的兜底）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart()
        {
            ResetRuntimeState();
        }

        // ---------------------------------------------------------------- 源切换（唯一入口）

        /// <summary>切到实时源（销毁回放 feed；binder 恢复实时采样路径）。</summary>
        public static void SetLive()
        {
            EnsureInit();
            if (!IsReplay && TwinUiReplayFeed.Active == null)
            {
                return;
            }

            TwinUiReplayFeed.Shutdown(); // Shutdown 内部 NotifyFeedDetached → Segment+1
            LastError = string.Empty;
            Debug.Log("[SOURCE] switch live segment=" + _segment);
        }

        /// <summary>
        /// 切到回放源。csv 为空时用会话列表第一条；没有会话/加载失败则不切换并返回 false。
        /// 事务式：失败时旧源（Active/CsvPath/记录频率）逐项不变，也不改 Segment（T4）。
        /// </summary>
        public static bool SetReplay(string csv = null, bool allowEmpty = false)
        {
            EnsureInit();
            if (string.IsNullOrEmpty(csv))
            {
                if (_runs.Count == 0)
                {
                    LastError = "没有可用回放会话";
                    return false;
                }

                csv = _runs[0];
            }

            if (!File.Exists(csv))
            {
                LastError = "回放文件不存在: " + csv;
                return false;
            }

            // 默认（allowEmpty=false）：0 数据行会话在**进入 Ensure 之前**就拒绝，
            // 避免把空源挂成 Active（那会污染旧源，"坏/空 CSV 不得污染已加载会话"）。
            if (!allowEmpty && !HasAnyDataRow(csv))
            {
                LastError = "回放文件 0 数据行（空会话不切换）: " + csv;
                Debug.LogWarning("[SOURCE] switch replay rejected empty | csv=" + csv);
                return false;
            }

            TwinUiReplayFeed feed = TwinUiReplayFeed.Ensure(csv);
            // 语义（A 支 vs R2 的边界）：
            //   * 默认 allowEmpty=false：0 数据行会话**不算切换成功**（R2 T3/T4 的既定语义：
            //     "换回放"跳过空会话、坏/空 CSV 不得污染已加载会话）。
            //   * allowEmpty=true：显式加载一个"表头合法 0 数据行"的会话，允许挂载并显示"无数据"
            //     （合同要求：这种真实情况不能静默沿用旧源）。入口显式，不改变默认行为。
            bool accepted = feed != null && feed.LastAttachOk
                            && (feed.Loaded || (allowEmpty && feed.HeaderOnly));
            if (!accepted)
            {
                string detail = feed == null
                    ? "feed=null"
                    : (string.IsNullOrEmpty(feed.LastAttachError) ? feed.LoadError : feed.LastAttachError);
                LastError = "回放加载失败: " + detail;
                Debug.LogWarning("[SOURCE] switch replay FAILED | csv=" + csv + " | allowEmpty=" + allowEmpty
                    + " | " + detail);
                return false;
            }

            LastError = string.Empty;
            Debug.Log("[SOURCE] switch replay run=" + CurrentRunId + " rows=" + feed.RowCount
                + " schema=" + feed.SchemaLabel
                + " headerOnly=" + feed.HeaderOnly
                + " segment=" + _segment + " recordHz(窗口=" + RecordHzWindow.ToString("F1")
                + " 整文件=" + RecordHzFile.ToString("F1") + ")");
            return true;
        }

        /// <summary>
        /// 轮换到下一个**可加载**的回放会话（UI 的"换回放"按钮）。
        /// R2 修复：真实 PaperLogs 里有 0 行的历史会话（如 561 B 的 samples.csv），旧实现
        /// 只会走到下一个目录、失败就停 → "换回放"被空会话卡死。现在最多试一轮，跳过坏会话。
        /// </summary>
        public static bool NextReplay()
        {
            EnsureInit();
            if (_runs.Count == 0)
            {
                LastError = "没有可用回放会话";
                return false;
            }

            int start = _runs.IndexOf(CurrentCsv);
            string firstError = string.Empty;
            for (int step = 1; step <= _runs.Count; step++)
            {
                int index = (start + step) % _runs.Count;
                string candidate = _runs[index];
                // 换回放：空会话按"不可用"跳过（SetReplay 默认 strict，不会 +Segment）
                if (SetReplay(candidate))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(firstError))
                {
                    firstError = candidate + " -> " + LastError;
                }
            }

            LastError = "没有可加载的回放会话（首个失败: " + firstError + "）";
            Debug.LogWarning("[SOURCE] next replay FAILED | " + LastError);
            return false;
        }

        /// <summary>
        /// 只读判断"是否有至少一条数据行"（O(1)：读表头 + 第一条非空行），
        /// 避免为拒绝空会话而把 280k 行的文件整份读进内存。
        /// </summary>
        private static bool HasAnyDataRow(string path)
        {
            try
            {
                using (var reader = new StreamReader(path))
                {
                    if (reader.ReadLine() == null)
                    {
                        return false;
                    }

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
            catch
            {
                return true; // 读取失败不在这里判死，交给下游解析给出明确错误
            }
        }

        /// <summary>
        /// 由 TwinUiReplayFeed 成功挂载后调用：Segment+1，卡片清缓冲。</summary>
        public static void NotifyFeedAttached(TwinUiReplayFeed feed, string csv)
        {
            _segment++;
            LastSegmentChangeAt = Time.unscaledTime;
            Debug.Log("[SOURCE] feed attached run=" + CurrentRunId + " csv=" + (csv ?? string.Empty)
                + " segment=" + _segment);
        }

        /// <summary>由 TwinUiReplayFeed.Shutdown 调用：Segment+1，卡片清缓冲。</summary>
        public static void NotifyFeedDetached()
        {
            _segment++;
            LastSegmentChangeAt = Time.unscaledTime;
            Debug.Log("[SOURCE] feed detached segment=" + _segment);
        }

        // ---------------------------------------------------------------- fix1 B(8)：循环边界

        private static int _loopWraps;

        /// <summary>回放循环已回绕的次数（fix1 B(8) 证据；0 = 未回绕）。</summary>
        public static int LoopWrapCount => _loopWraps;

        /// <summary>
        /// 最近一次"曲线分段变化"的时刻（Time.unscaledTime）。自检用：判断卡片缓冲里的样本
        /// 是否都来自分段之后（即确实清过缓冲、没有跨源/跨周期残留）。
        /// </summary>
        public static float LastSegmentChangeAt { get; private set; }

        /// <summary>
        /// 回放循环回绕一次 → **曲线分段号 +1**（与"换源"共用卡片既有的"清缓冲"通路）。
        /// 这样第 N 周期与第 N+1 周期不会被连成一条假连续的线；回绕本身仍继续回放，不改变数据源。
        /// </summary>
        public static void NotifyLoopWrapped()
        {
            _segment++;
            _loopWraps++;
            LastSegmentChangeAt = Time.unscaledTime;
            Debug.Log("[SOURCE] loop wrap -> segment=" + _segment + " wraps=" + _loopWraps);
        }

        // ---------------------------------------------------------------- fix1 B(5)：播放/暂停

        /// <summary>当前是否暂停（只对回放有意义；见 TwinUiPlaybackControl）。</summary>
        public static bool IsPaused => TwinUiPlaybackControl.IsPaused;

        /// <summary>当前源是否可暂停（只有回放可暂停；实时源没有可停的游标）。</summary>
        public static bool CanPause => IsReplay;

        /// <summary>
        /// 切换播放/暂停（生产按钮与自检共用）。不可暂停时返回 false 且**不改状态**，
        /// 避免"实时模式下点了暂停、界面却显示已暂停"的假生效。
        /// </summary>
        public static bool TogglePause()
        {
            if (!CanPause)
            {
                return false;
            }

            bool paused = TwinUiPlaybackControl.Toggle();
            Debug.Log("[SOURCE] playback paused=" + paused + " run=" + CurrentRunId);
            return true;
        }

        // ---------------------------------------------------------------- 显示开关

        /// <summary>设置显示刷新率（Hz）。只改显示节流，不改 CSV 记录频率（记录频率来自文件）。</summary>
        public static void SetDisplayHz(float hz)
        {
            float value = Mathf.Clamp(hz, 1f, 30f);
            if (Mathf.Abs(value - DisplayHz) < 0.01f)
            {
                return;
            }

            DisplayHz = value;
            Debug.Log("[SOURCE] displayHz=" + DisplayHz.ToString("F0") + " (记录频率由 CSV 决定，未改)");
        }

        /// <summary>设置曲线时间窗（秒）。只改绘制窗口，不改采样/记录。</summary>
        public static void SetTimeWindow(float seconds)
        {
            float value = Mathf.Clamp(seconds, 2f, 120f);
            if (Mathf.Abs(value - TimeWindowSeconds) < 0.01f)
            {
                return;
            }

            TimeWindowSeconds = value;
            Debug.Log("[SOURCE] timeWindow=" + TimeWindowSeconds.ToString("F0") + "s");
        }

        /// <summary>回放时给状态条用的进度（0..1）；实时返回 0。</summary>
        public static double ReplayProgress01 => TwinUiReplayFeed.Active != null
            ? TwinUiReplayFeed.Active.Progress01
            : 0d;
    }
}