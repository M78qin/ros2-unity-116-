// 三期 UI 设置资产：界面刷新频率 / 应用频率 / 阈值组 / 主题 / 密度 / 数据源。
//
// "运行时改、编辑态持久"：运行时通过 SetUiRefreshHz 等方法修改，编辑器里立即写入 asset；
// 打包运行时只改内存（没有 AssetDatabase），这一点在 README 里如实标注。
//
// 阈值默认值全部标"待校准"：来自用户给定初值，未做现场标定，不得写入论文结果表。
using System;
using DigitalTwin.UI;
using UnityEngine;

namespace DigitalTwin
{
    [Serializable]
    public sealed class TwinUiThresholds
    {
        [Header("链路频率（低于阈值判黄/红）")]
        public double sampleHzWarn = 95d;
        public double sampleHzCrit = 80d;
        public double applyHzWarn = 25d;
        public double applyHzCrit = 15d;

        [Header("新鲜度与链路时延（高于阈值判黄/红）")]
        public double freshnessWarnMs = 200d;
        public double freshnessCritMs = 1000d;
        public double receiveToApplyWarnMs = 20d;
        public double receiveToApplyCritMs = 50d;
        public double jitterP99WarnMs = 2d;
        public double jitterP99CritMs = 5d;

        [Header("关节（位置误差 / 力矩占额定）")]
        public double positionErrorWarnDeg = 0.5d;
        public double positionErrorCritDeg = 1.0d;
        public double torqueWarnRatio = 0.60d;
        public double torqueCritRatio = 0.85d;
        public double torqueRatedNm = 40d;

        [Header("六维力（N，绝对幅值）")]
        public double forceWarnN = 20d;
        public double forceCritN = 50d;

        [Header("元信息")]
        public bool calibrated;
        public string calibrationNote = "阈值待校准：来自联调初值，未做现场标定";

        public string Describe()
        {
            return "sampleHz<" + sampleHzWarn + "/" + sampleHzCrit
                + ";applyHz<" + applyHzWarn + "/" + applyHzCrit
                + ";fresh>" + freshnessWarnMs + "/" + freshnessCritMs + "ms"
                + ";recvApply>" + receiveToApplyWarnMs + "/" + receiveToApplyCritMs + "ms"
                + ";jitterP99>" + jitterP99WarnMs + "/" + jitterP99CritMs + "ms"
                + ";posErr>" + positionErrorWarnDeg + "/" + positionErrorCritDeg + "deg"
                + ";torque>" + (torqueWarnRatio * 100d) + "/" + (torqueCritRatio * 100d) + "%"
                + ";force>" + forceWarnN + "/" + forceCritN + "N"
                + ";calibrated=" + (calibrated ? "true" : "false");
        }

        /// <summary>越低越好（Hz 类）。</summary>
        public static TwinUITone LowerIsBetter(double value, double warn, double crit)
        {
            if (double.IsNaN(value) || value <= 0d)
            {
                return TwinUITone.Unknown;
            }

            if (value < crit)
            {
                return TwinUITone.Crit;
            }

            return value < warn ? TwinUITone.Warn : TwinUITone.Ok;
        }

        /// <summary>越高越差（时延/新鲜度/抖动/误差类）。</summary>
        public static TwinUITone HigherIsWorse(double value, double warn, double crit)
        {
            if (double.IsNaN(value) || value < 0d)
            {
                return TwinUITone.Unknown;
            }

            if (value > crit)
            {
                return TwinUITone.Crit;
            }

            return value > warn ? TwinUITone.Warn : TwinUITone.Ok;
        }

        /// <summary>取值越接近 0 越好（如丢帧计数）。</summary>
        public static TwinUITone ZeroIsBest(long value, long warn, long crit)
        {
            if (value <= 0)
            {
                return TwinUITone.Ok;
            }

            return value > crit ? TwinUITone.Crit : value > warn ? TwinUITone.Warn : TwinUITone.Ok;
        }

        public TwinUITone SampleHz(double hz)
        {
            return LowerIsBetter(hz, sampleHzWarn, sampleHzCrit);
        }

        public TwinUITone ApplyHz(double hz)
        {
            return LowerIsBetter(hz, applyHzWarn, applyHzCrit);
        }

        public TwinUITone Freshness(double ms)
        {
            return HigherIsWorse(ms, freshnessWarnMs, freshnessCritMs);
        }

        public TwinUITone ReceiveToApply(double ms)
        {
            return HigherIsWorse(ms, receiveToApplyWarnMs, receiveToApplyCritMs);
        }

        public TwinUITone Jitter(double ms)
        {
            return HigherIsWorse(ms, jitterP99WarnMs, jitterP99CritMs);
        }

        public TwinUITone PositionError(double absDeg)
        {
            return HigherIsWorse(absDeg, positionErrorWarnDeg, positionErrorCritDeg);
        }

        public TwinUITone TorqueRatio(double ratio)
        {
            return HigherIsWorse(ratio, torqueWarnRatio, torqueCritRatio);
        }

        public TwinUITone Force(double absN)
        {
            return HigherIsWorse(absN, forceWarnN, forceCritN);
        }

        public void ResetToDefaults()
        {
            sampleHzWarn = 95d;
            sampleHzCrit = 80d;
            applyHzWarn = 25d;
            applyHzCrit = 15d;
            freshnessWarnMs = 200d;
            freshnessCritMs = 1000d;
            receiveToApplyWarnMs = 20d;
            receiveToApplyCritMs = 50d;
            jitterP99WarnMs = 2d;
            jitterP99CritMs = 5d;
            positionErrorWarnDeg = 0.5d;
            positionErrorCritDeg = 1.0d;
            torqueWarnRatio = 0.60d;
            torqueCritRatio = 0.85d;
            torqueRatedNm = 40d;
            forceWarnN = 20d;
            forceCritN = 50d;
            calibrated = false;
            calibrationNote = "阈值待校准：来自联调初值，未做现场标定";
        }

        /// <summary>默认阈值组（不依赖资产，供审计/自检使用）。</summary>
        public static TwinUiThresholds CreateDefault()
        {
            var t = new TwinUiThresholds();
            t.ResetToDefaults();
            return t;
        }
    }

    [CreateAssetMenu(menuName = "Digital Twin/Twin UI Settings", fileName = "TwinUiSettings")]
    public sealed class TwinUiSettings : ScriptableObject
    {
        [Header("刷新频率")]
        [Tooltip("界面刷新频率（Hz）。可选项 5/10/20/30/60；运行时修改立即生效。")]
        [Range(1f, 120f)] public float uiRefreshHz = 10f;
        [Tooltip("机器人应用频率（Hz），来自 TwinRuntimeProfile.robotApplyRateHz，仅用于目标值显示。")]
        [Range(1f, 240f)] public float robotApplyRateHz = 60f;
        [Tooltip("ui_snapshot.json 导出频率（Hz），论文外部消费用。")]
        [Range(1f, 60f)] public float snapshotExportHz = 5f;

        [Header("外观")]
        public TwinUIThemeMode themeMode = TwinUIThemeMode.Dark;
        public TwinUIDensity density = TwinUIDensity.Comfortable;

        [Header("按钮悬停渐变（顶点色，不改材质）")]
        [Tooltip("按钮 hover 时顶点色由基色→强调色线性渐变；关闭则回退为纯色（对比度/令牌审计不受影响）。")]
        public bool enableHoverGradient = true;
        [Tooltip("悬停渐变强度：0=纯色，1=完全渐变到强调色。")]
        [Range(0f, 1f)] public float hoverGradientStrength = 0.35f;

        [Header("数据源")]
        public TwinUiSourceKind defaultSource = TwinUiSourceKind.Runtime;
        [Tooltip("回放 CSV（samples.csv）绝对路径。无真机时用于验证 UI 绑定/阈值。")]
        public string replayCsvPath =
            @"D:\Unity Projects\DART_R-data\PaperLogs\20261103_135616_529090_E4_B\samples.csv";
        [Range(1f, 240f)] public float replayHz = 30f;
        public bool replayLoop = true;
        [Range(0.1f, 8f)] public float replaySpeed = 1f;
        [Tooltip("回放窗口（秒），用于计算频率/抖动/丢帧。")]
        [Range(0.5f, 10f)] public float replayWindowSeconds = 2f;

        [Header("阈值（待校准）")]
        public TwinUiThresholds thresholds = new TwinUiThresholds();

        [Header("输出")]
        public string eventLogPath = "Logs/ui_events.jsonl";
        public string snapshotPath = "Logs/ui_snapshot.json";

        public event Action Changed;

        public bool IsMockSource => defaultSource == TwinUiSourceKind.Mock;
        public double UiHz => uiRefreshHz;

        public static bool IsAllowedUiHz(float hz)
        {
            for (int i = 0; i < TwinUITextR3.UiHzOptions.Length; i++)
            {
                if (Mathf.Abs(TwinUITextR3.UiHzOptions[i] - hz) < 0.01f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>设置界面刷新频率；不在 5/10/20/30/60 内则拒绝并返回 false。</summary>
        public bool SetUiRefreshHz(float hz)
        {
            if (!IsAllowedUiHz(hz))
            {
                Debug.LogWarning("[Twin UI] rejected uiHz=" + hz + " (allowed 5/10/20/30/60)");
                return false;
            }

            if (Mathf.Abs(uiRefreshHz - hz) < 0.01f)
            {
                return false;
            }

            uiRefreshHz = hz;
            Debug.Log("[Twin UI] uiHz=" + hz.ToString("F0") + " applied (settings asset)");
            RaiseChanged();
            return true;
        }

        public void SetTheme(TwinUIThemeMode mode)
        {
            themeMode = mode;
            TwinUITheme.SetMode(mode);
            RaiseChanged();
        }

        public void SetDensity(TwinUIDensity value)
        {
            density = value;
            RaiseChanged();
        }

        public void SetSource(TwinUiSourceKind kind)
        {
            defaultSource = kind;
            RaiseChanged();
        }

        public void SetSnapshotExportHz(float hz)
        {
            snapshotExportHz = Mathf.Clamp(hz, 1f, 60f);
            RaiseChanged();
        }

        /// <summary>恢复出厂设置：UI 10 Hz、机器人应用 60 Hz、深色、运行时数据源、阈值回到"待校准"初值。</summary>
        public void ResetToDefaults()
        {
            uiRefreshHz = 10f;
            robotApplyRateHz = 60f;
            snapshotExportHz = 5f;
            themeMode = TwinUIThemeMode.Dark;
            density = TwinUIDensity.Comfortable;
            enableHoverGradient = true;
            hoverGradientStrength = 0.35f;
            defaultSource = TwinUiSourceKind.Runtime;
            replayHz = 30f;
            replayLoop = false;
            replaySpeed = 1f;
            replayWindowSeconds = 2f;
            thresholds.ResetToDefaults();
            RaiseChanged();
        }

        public void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>把内存值写回 asset（仅编辑器有效，用于"实验设置面板改完立即生效并持久"）。</summary>
        public bool PersistToAsset()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[Twin UI] settings persisted to asset: uiHz=" + uiRefreshHz
                + " theme=" + themeMode + " source=" + defaultSource);
            return true;
#else
            Debug.LogWarning("[Twin UI] PersistToAsset only works in the editor");
            return false;
#endif
        }
    }
}