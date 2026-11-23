// TwinUiPlaybackControl.cs —— 播放 / 暂停（生产 UI 的单一暂停状态）（2026-11-09 fix1）
//
// 为什么单独一个状态类：
//   * 生产界面必须有"播放/暂停/恢复"，而且**不能**和"冻结显示"混成一个开关。
//   * 暂停 = 停住**回放游标与回放时钟**（数据不再推进），恢复后从原位置继续；
//   * 冻结显示（TwinUiViewFreeze）= 只停卡片重绘，回放时钟 / 数据源 / TwinRecorder 继续走。
//   两者语义不同、证据也必须不同（暂停：PlaybackSeconds 不变；冻结：PlaybackSeconds 继续增长）。
//
// 职责边界（故意做窄）：
//   * 本类只保存一个布尔状态，不直接碰数据源；
//   * 真正的"停游标"只有一个执行点：TwinUiReplayFeed.Update 每帧把本状态同步给
//     ReplayTwinUiDataSource.SetPaused（避免 UI / 卡片 / 自检各自改数据源）。
//   * 只作用于回放；实时源不可暂停（按钮按 disabled 显示，不假装生效）。
using UnityEngine;

namespace DigitalTwin.UI
{
    public static class TwinUiPlaybackControl
    {
        /// <summary>当前是否暂停（只对回放有意义）。</summary>
        public static bool IsPaused { get; private set; }

        /// <summary>状态变化回调（按钮据此改文案；也可轮询）。</summary>
        public static event System.Action<bool> Changed;

        /// <summary>按钮文案：暂停中显示"播放"（点它恢复）。</summary>
        public static string ButtonLabel => IsPaused ? "播放" : "暂停";

        /// <summary>状态短语（自检/详情用）。</summary>
        public static string StateLabel => IsPaused ? "已暂停" : "播放中";

        public static void Set(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            var handler = Changed;
            if (handler != null)
            {
                handler(paused);
            }
        }

        /// <summary>切换并返回切换后的状态。</summary>
        public static bool Toggle()
        {
            Set(!IsPaused);
            return IsPaused;
        }

        public static void Reset()
        {
            Set(false);
        }

        /// <summary>Play 启动清静态残留（关闭域重载时的兜底）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart()
        {
            IsPaused = false;
            Changed = null;
        }
    }
}